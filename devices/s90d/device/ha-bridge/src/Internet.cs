using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;

// The Internet switch (Info, phone page, Home Assistant): `internet-off` in SilentGlass's data folder means off.
// Off = the TV's own network settings (connman) switched to manual IPv4 with the same address and no gateway, so
// nothing on the TV can reach past the home network; the home network, Home Assistant, notifications and Thread keep
// working. connman saves it, so it holds through restarts with no gap. The owner user may change it
// (net.connman.Service.SetProperty needs network.profile, which the owner has). What it replaced is kept in
// `internet-applied.txt` and put back when the switch goes on; Samsung's IP Settings menu also undoes it.
static partial class Bridge
{
    static FileSystemWatcher netWatch;
    static readonly AutoResetEvent netCheck = new(true);
    static readonly Regex Ip4 = new(@"^\d{1,3}(\.\d{1,3}){3}$");

    static void StartInternetWatch() => new Thread(() => {
        try {
            netWatch = new FileSystemWatcher(Data, Shared.InternetOff) { NotifyFilter = NotifyFilters.FileName };
            netWatch.Created += (_, _) => netCheck.Set(); netWatch.Deleted += (_, _) => netCheck.Set();
            netWatch.EnableRaisingEvents = true;
        } catch (Exception e) { Log("internet: no watcher (" + e.Message + "), checking every minute"); }
        while (true) {
            netCheck.WaitOne(60_000);
            try { ReconcileInternet(); } catch (Exception e) { Log("internet: " + e.Message); }
        }
    }) { IsBackground = true, Name = "internet" }.Start();

    static string Gdbus(params string[] args)
    {
        var psi = new ProcessStartInfo("/usr/bin/gdbus") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in new[] { "call", "--system", "--dest", "net.connman" }) psi.ArgumentList.Add(a);
        foreach (var a in args) psi.ArgumentList.Add(a);
        try {
            using var p = Process.Start(psi); var o = p.StandardOutput.ReadToEndAsync();
            if (!p.WaitForExit(8000)) { try { p.Kill(true); } catch { } return null; }
            return p.ExitCode == 0 ? o.Result : null;
        } catch { return null; }
    }
    // the connected service (wired or Wi-Fi): its object path and properties
    static (string path, string props) ConnectedService()
    {
        var all = Gdbus("--object-path", "/", "--method", "net.connman.Manager.GetServices") ?? "";
        foreach (var part in all.Split("objectpath '")[1..]) {
            int q = part.IndexOf('\'');
            if (q > 0 && Regex.IsMatch(part, @"'State': <'(ready|online)'>")) return (part.Substring(0, q), part);
        }
        return (null, null);
    }
    static string Field(string block, string key) => Regex.Match(block ?? "", "'" + key + @"': <'([^']*)'>").Groups[1].Value;
    static string Block(string props, string key) => Regex.Match(props ?? "", "'" + Regex.Escape(key) + @"': <\{([^}]*)\}>").Groups[1].Value;
    static bool SetIpv4(string path, string method, string addr = "", string mask = "", string gw = "")
    {
        if (!Regex.IsMatch(path ?? "", @"^/net/connman/service/[A-Za-z0-9_]+$") || method is not ("dhcp" or "manual")) return false;
        string v = "{'Method': <'" + method + "'>";
        if (method == "manual") {
            if (!Ip4.IsMatch(addr) || !Ip4.IsMatch(mask) || (gw.Length > 0 && !Ip4.IsMatch(gw))) return false;
            v += ", 'Address': <'" + addr + "'>, 'Netmask': <'" + mask + "'>" + (gw.Length > 0 ? ", 'Gateway': <'" + gw + "'>" : "");
        }
        return Gdbus("--object-path", path, "--method", "net.connman.Service.SetProperty", "IPv4.Configuration", "<" + v + "}>") != null;
    }

    static long netApplied = long.MinValue / 2;
    static void ReconcileInternet()
    {
        bool wanted = Shared.InternetWanted(Data), route = Shared.DefaultRoute4(Read("/proc/net/route"));
        string rec = DataFile(Shared.InternetApplied);
        if (wanted ? !File.Exists(rec) : !route && File.Exists(rec)) return;  // already as wanted
        if (Environment.TickCount64 - netApplied < 20_000) { Thread.Sleep(5000); netCheck.Set(); return; }  // let the last change settle
        var (path, props) = ConnectedService();
        if (path == null) { Log("internet: no connected network service"); return; }
        if (!wanted) {
            var cur = Block(props, "IPv4"); var conf = Block(props, "IPv4.Configuration");
            string addr = Field(cur, "Address"), mask = Field(cur, "Netmask");
            if (!File.Exists(rec))  // what to put back: the configured method, and a manual setup's own values
                File.WriteAllText(rec, "path=" + path + "\nmethod=" + Field(conf, "Method") + "\naddress=" + Field(conf, "Address") + "\nnetmask=" + Field(conf, "Netmask") + "\ngateway=" + Field(conf, "Gateway") + "\n");
            bool ok = SetIpv4(path, "manual", addr, mask);
            Log("internet: off (no gateway) " + (ok ? "ok" : "FAILED"));
        } else {
            var r = Shared.ParseConf(File.ReadAllText(rec));
            string p = r.GetValueOrDefault("path", path);
            bool own = r.GetValueOrDefault("method") == "manual" && r.GetValueOrDefault("gateway", "").Length > 0;
            bool ok = own ? SetIpv4(p, "manual", r["address"], r["netmask"], r["gateway"]) : SetIpv4(p, "dhcp");
            Log("internet: on (" + (own ? "own manual settings" : "dhcp") + ") " + (ok ? "ok" : "FAILED"));
            if (ok) File.Delete(rec);
        }
        netApplied = Environment.TickCount64;
        changed.Set();
    }
}
