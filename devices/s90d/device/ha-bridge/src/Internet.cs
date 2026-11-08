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
        string rec = DataFile(Shared.InternetApplied), hold = DataFile(Shared.InternetFailsafe);
        CheckClock(rec);
        if (wanted && File.Exists(hold)) File.Delete(hold);
        if (!wanted && !route && File.Exists(rec)) { WatchHomeNetwork(rec, hold); return; }  // off, as wanted
        if (!wanted && File.Exists(hold) && !File.Exists(rec)) {  // the fail-safe opened it: off again once the home network answers
            if (!Answers(RouteGateway())) return;
            Log("internet: the home network answers again; switching the Internet off again");
            File.Delete(hold);
        }
        if (wanted && !File.Exists(rec)) return;  // on, as wanted
        if (Environment.TickCount64 - netApplied < 20_000) { Thread.Sleep(5000); netCheck.Set(); return; }  // let the last change settle
        var (path, props) = ConnectedService();
        if (path == null) { Log("internet: no connected network service"); return; }
        if (!wanted) {
            var cur = Block(props, "IPv4"); var conf = Block(props, "IPv4.Configuration");
            string addr = Field(cur, "Address"), mask = Field(cur, "Netmask");
            if (!File.Exists(rec))  // what to put back: the configured method, and a manual setup's own values; the router, to watch
                File.WriteAllText(rec, "path=" + path + "\nmethod=" + Field(conf, "Method") + "\naddress=" + Field(conf, "Address") + "\nnetmask=" + Field(conf, "Netmask") + "\ngateway=" + Field(conf, "Gateway") + "\nrouter=" + Field(cur, "Gateway") + "\n");
            bool ok = SetIpv4(path, "manual", addr, mask);
            Log("internet: off (no gateway) " + (ok ? "ok" : "FAILED"));
        } else {
            bool ok = PutBack(rec, path);
            Log("internet: on " + (ok ? "ok" : "FAILED"));
        }
        netApplied = Environment.TickCount64;
        changed.Set();
    }
    // put back what the switch replaced (DHCP, or the owner's own manual settings)
    static bool PutBack(string rec, string path)
    {
        var r = Shared.ParseConf(File.ReadAllText(rec));
        string p = r.GetValueOrDefault("path", path);
        bool own = r.GetValueOrDefault("method") == "manual" && r.GetValueOrDefault("gateway", "").Length > 0;
        bool ok = own ? SetIpv4(p, "manual", r["address"], r["netmask"], r["gateway"]) : SetIpv4(p, "dhcp");
        Log("internet: back to " + (own ? "the own manual settings" : "dhcp") + " " + (ok ? "ok" : "FAILED"));
        if (ok) File.Delete(rec);
        return ok;
    }

    // ---- Fail-safe. Off pins the TV's current address with no gateway and stops DHCP. If the home network then
    // changes (new router, other subnet, the address handed to another device), the TV could no longer be reached to
    // switch it back. So while off, the bridge checks every minute that the router (or the Home Assistant broker)
    // answers; after 10 minutes without an answer it puts the TV's own settings back and holds them (InternetFailsafe)
    // until the home network answers again, then switches off again with the new values. ----
    const long FailsafeMs = 10 * 60_000;
    static long lanLost;  // TickCount64 when the home network stopped answering; 0 = it answers
    static void WatchHomeNetwork(string rec, string hold)
    {
        var r = Shared.ParseConf(Read(rec));  // records from 1.3.0 have no router=: fall back to a manual gateway, then the broker alone
        string router = r.GetValueOrDefault("router", "").Length > 0 ? r["router"] : r.GetValueOrDefault("gateway", "");
        if (Answers(router) || Answers(conf.GetValueOrDefault("host", ""), int.TryParse(conf.GetValueOrDefault("port", "1883"), out int pt) ? pt : 1883)) {
            if (lanLost != 0) Log("internet: the home network answers again");
            lanLost = 0; return;
        }
        if (lanLost == 0) { lanLost = Environment.TickCount64; Log("internet: the home network doesn't answer (router '" + router + "'); the TV's own settings come back after 10 minutes of this"); return; }
        if (Environment.TickCount64 - lanLost < FailsafeMs) return;
        var (path, _) = ConnectedService();
        if (path == null) return;  // no link at all (cable out): nothing to put back yet
        File.WriteAllText(hold, "since=" + DateTime.UtcNow.ToString("o") + "\nrouter=" + router + "\n");
        Log("internet: the home network has not answered for 10 minutes; fail-safe: the TV's own network settings are back until it answers");
        if (PutBack(rec, path)) { lanLost = 0; netApplied = Environment.TickCount64; changed.Set(); }
    }
    // a host answers if it accepts or refuses a TCP connection (refusal proves it's there); IP addresses only, since
    // with the switch off there is no DNS
    static bool Answers(string host, params int[] ports)
    {
        if (!System.Net.IPAddress.TryParse(host ?? "", out var ip)) return false;
        foreach (int port in ports.Length > 0 ? ports : new[] { 80, 443, 53 }) {
            try {
                using var s = new System.Net.Sockets.Socket(ip.AddressFamily, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
                if (s.ConnectAsync(ip, port).Wait(2000)) return true;
            } catch (AggregateException e) when (e.InnerException is System.Net.Sockets.SocketException se && se.SocketErrorCode == System.Net.Sockets.SocketError.ConnectionRefused) { return true; }
            catch { }
        }
        return false;
    }
    // the IPv4 default gateway from the kernel's table ("" if none)
    static string RouteGateway()
    {
        foreach (var line in Read("/proc/net/route").Split('\n')) {
            var f = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (f.Length > 7 && f[1] == "00000000" && f[2] != "00000000" && uint.TryParse(f[2], System.Globalization.NumberStyles.HexNumber, null, out uint g))
                return new System.Net.IPAddress(g).ToString();
        }
        return "";
    }

    // ---- The clock. Only Samsung's network service may set it, and only from Samsung's own time server; after a
    // cold start with the switch off it stays at 1970. Web servers stamp every answer with the time (Date:), so the
    // bridge reads the router's or Home Assistant's and leaves the difference for SilentGlass (Shared.ClockOffset). ----
    static long clockRead = long.MinValue / 2;
    static void CheckClock(string rec)
    {
        string f = DataFile(Shared.ClockOffset);
        if (!Shared.ClockUnset(DateTime.UtcNow)) { if (File.Exists(f)) { File.Delete(f); Log("clock: the TV's own clock is set"); } return; }
        if (Environment.TickCount64 - clockRead < 10 * 60_000 && File.Exists(f)) return;
        // any web server on the home network stamps its answers: the router, else the Home Assistant host
        var r = Shared.ParseConf(Read(rec)); string broker = conf.GetValueOrDefault("host", "");
        DateTime? t = null;
        foreach (var (h, port) in new[] { (RouteGateway(), 80), (r.GetValueOrDefault("router", ""), 80), (r.GetValueOrDefault("gateway", ""), 80), (broker, 80), (broker, 8123) })
            if ((t = WebTime(h, port)) != null) break;
        if (t == null) return;
        clockRead = Environment.TickCount64;
        double off = (t.Value - DateTime.UtcNow).TotalSeconds;
        bool first = !File.Exists(f);
        File.WriteAllText(f, off.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + "\n");
        if (first) Log("clock: the TV's clock is unset; using the home network's (" + t.Value.ToString("u") + ")");
    }
    static DateTime? WebTime(string host, int port)
    {
        if (!System.Net.IPAddress.TryParse(host ?? "", out var ip)) return null;
        try {
            using var c = new System.Net.Sockets.TcpClient();
            if (!c.ConnectAsync(ip, port).Wait(2000)) return null;
            var st = c.GetStream(); st.ReadTimeout = 3000; st.WriteTimeout = 3000;
            st.Write(System.Text.Encoding.ASCII.GetBytes("HEAD / HTTP/1.0\r\nHost: " + host + "\r\n\r\n"));
            var buf = new byte[4096]; int n = 0, k;
            while (n < buf.Length && (k = st.Read(buf, n, buf.Length - n)) > 0) { n += k; if (System.Text.Encoding.ASCII.GetString(buf, 0, n).Contains("\r\n\r\n")) break; }
            var m = Regex.Match(System.Text.Encoding.ASCII.GetString(buf, 0, n), @"(?im)^Date:[ \t]*(.+?)[ \t]*\r?$");
            if (m.Success && DateTime.TryParseExact(m.Groups[1].Value, "r", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var t) && !Shared.ClockUnset(t)) return t;
        } catch { }
        return null;
    }

    // ---- Home guard. Samsung opens the Home app only after checking its app list: our slot inherited the Bixby app's
    // "needs the network" mark, so with no gateway (the switch off, or the router down) Home showed Samsung's
    // "network error" instead of SilentGlass. Samsung's web-app service rewrites that row from its memory on every
    // launch, so watch the file and keep the mark 0. ----
    static FileSystemWatcher wasWatch;
    static readonly AutoResetEvent homeCheck = new(true);
    static long guardLogged = long.MinValue / 2; static int guardFixes;
    static void StartHomeGuard() => new Thread(() => {
        try {
            wasWatch = new FileSystemWatcher(Path.GetDirectoryName(Shared.WasDb), Path.GetFileName(Shared.WasDb)) { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size };
            wasWatch.Changed += (_, _) => homeCheck.Set();
            wasWatch.EnableRaisingEvents = true;
        } catch (Exception e) { Log("home guard: no watcher (" + e.Message + "), checking every 5 minutes"); }
        while (true) {
            homeCheck.WaitOne(300_000);
            Thread.Sleep(300);  // let the writer finish
            try { GuardHome(); } catch (Exception e) { Log("home guard: " + e.Message); }
        }
    }) { IsBackground = true, Name = "homeguard" }.Start();
    static void GuardHome()
    {
        string where = " from widgetinfo where tizen_id='" + Shared.Self + "'";
        if (Sqlite("select is_network" + where + ";")?.Trim() != "1") return;
        bool ok = Sqlite("update widgetinfo set is_network=0 where tizen_id='" + Shared.Self + "' and is_network<>0; select changes();")?.Trim() == "1";
        guardFixes++;
        if (!ok || Environment.TickCount64 - guardLogged > 3_600_000) {  // at most hourly: Samsung re-marks it after each Home press
            Log("home guard: Samsung marked SilentGlass as needing the Internet; cleared " + (ok ? "ok" : "FAILED") + " (" + guardFixes + " since the last note)");
            guardLogged = Environment.TickCount64; guardFixes = 0;
        }
    }
    static string Sqlite(string sql)
    {
        var psi = new ProcessStartInfo("/usr/bin/sqlite3") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in new[] { Shared.WasDb, ".timeout 5000", sql }) psi.ArgumentList.Add(a);
        try {
            using var p = Process.Start(psi); var o = p.StandardOutput.ReadToEndAsync();
            if (!p.WaitForExit(10_000)) { try { p.Kill(true); } catch { } return null; }
            return p.ExitCode == 0 ? o.Result : null;
        } catch { return null; }
    }
}
