using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

// Read-only device facts for the Info page. Every reader returns null/empty on failure;
// parsers take file text so they can be unit-tested off the TV.
public static class InfoStats
{
    public static readonly string[] VendorMarkers = {
        "pisa-service", "canalysis-daemon", "slive-provider-daemon", "com.samsung.tv.bixby-prov", "com.samsung.tv.farfieldvoice",
        "com.samsung.tv.ondevice-voice", "com.samsung.tv.voiceid-service", "com.samsung.tv.wakeupless", "org.tizen.voice-app", "org.tizen.voice-client" };
    public static readonly string[] DisabledApps = {
        "com.samsung.tv.acr", "com.samsung.tv.iacr", "com.samsung.tv.contents-recognition-dbmgr", "com.samsung.tv.contents-cognition-dbmgr",
        "com.samsung.tv.farfieldvoice", "com.samsung.tv.ondevice-voice", "com.samsung.tv.voiceid-service", "org.tizen.voice-client", "org.tizen.stt-client",
        "org.tizen.voice-app", "org.tizen.voice-app-service", "com.samsung.tv.wakeup-engine-bixby", "com.samsung.tv.bixby-prov",
        "com.samsung.tv.voiceassistant-music", "com.samsung.tv.vif", "com.samsung.tv.alexa-client", "com.samsung.tv.alexa-fullscreen-app",
        "com.samsung.tv.wakeup-engine-alexa", "AQKO41xyKP.AmazonAlexa", "HEPsqFNie0.tvplusstandalone",
        "com.samsung.tv.targeted-ad-service", "com.samsung.tv.utterance-recommender-core", "com.samsung.tv.AICompanionAgent", "com.samsung.tv.aov-dashboard",
        "com.samsung.tv.home.media", "com.samsung.tv.cobalt-yt" };

    static string Read(string path) { try { return File.ReadAllText(path); } catch { return null; } }

    // ---- parsers ----
    public static double? ParseUptime(string text)
    {
        if (text == null) return null;
        var first = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return double.TryParse(first, NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s : (double?)null;
    }
    public static (long totalKb, long availableKb)? ParseMemory(string text)
    {
        if (text == null) return null;
        long total = -1, avail = -1;
        foreach (var line in text.Split('\n'))
        {
            var p = line.Split(new[] { ' ', ':' }, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length < 2) continue;
            if (p[0] == "MemTotal") long.TryParse(p[1], out total);
            else if (p[0] == "MemAvailable") long.TryParse(p[1], out avail);
        }
        return total > 0 && avail >= 0 ? (total, avail) : null;
    }
    public static (long idle, long total)? ParseCpu(string text)
    {
        var line = text?.Split('\n').FirstOrDefault(l => l.StartsWith("cpu "));
        if (line == null) return null;
        var v = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Take(8).Select(x => long.TryParse(x, out var n) ? n : 0).ToArray();
        if (v.Length < 5) return null;
        return (v[3] + v[4], v.Sum());
    }
    public static int CpuCount(string text) => text == null ? 0 : text.Split('\n').Count(l => l.StartsWith("cpu") && l.Length > 3 && char.IsDigit(l[3]));
    public static Dictionary<string, (long rx, long tx)> ParseNetDev(string text)
    {
        var r = new Dictionary<string, (long, long)>();
        if (text == null) return r;
        foreach (var line in text.Split('\n'))
        {
            int c = line.IndexOf(':'); if (c < 0) continue;
            var name = line.Substring(0, c).Trim();
            var v = line.Substring(c + 1).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (v.Length >= 9 && long.TryParse(v[0], out var rx) && long.TryParse(v[8], out var tx)) r[name] = (rx, tx);
        }
        return r;
    }
    public static string ParseDefaultRoute(string text)
    {
        if (text == null) return null;
        foreach (var line in text.Split('\n').Skip(1))
        {
            var p = line.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length > 2 && p[1] == "00000000") return p[0];
        }
        return null;
    }
    public static int? ParseWirelessLevel(string text, string iface)
    {
        var line = text?.Split('\n').FirstOrDefault(l => l.TrimStart().StartsWith(iface + ":"));
        if (line == null) return null;
        var v = line.Substring(line.IndexOf(':') + 1).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return v.Length > 2 && int.TryParse(v[2].TrimEnd('.'), out var level) && level != 0 ? level : (int?)null;
    }
    public static List<string> ParseNameservers(string text) =>
        text == null ? new List<string>() : text.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("nameserver "))
            .Select(l => l.Substring(11).Trim()).Where(s => s.Length > 0).Distinct().ToList();
    public static bool ParseListening(string text, int port)
    {
        if (text == null) return false;
        string hex = ":" + port.ToString("X4");
        foreach (var line in text.Split('\n').Skip(1))
        {
            var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length > 3 && p[1].EndsWith(hex) && p[3] == "0A") return true;
        }
        return false;
    }
    public static string ParseBuildDate(string build)
    {
        if (build == null) return null;
        var m = System.Text.RegularExpressions.Regex.Match(build, @"(20\d{2})(\d{2})(\d{2})");
        return m.Success ? m.Groups[1].Value + "-" + m.Groups[2].Value + "-" + m.Groups[3].Value : null;
    }

    // ---- readers (TV) ----
    public static double? Uptime() => ParseUptime(Read("/proc/uptime"));
    public static (long totalKb, long availableKb)? Memory() => ParseMemory(Read("/proc/meminfo"));
    public static (long idle, long total)? Cpu() => ParseCpu(Read("/proc/stat"));
    public static int Cores() => CpuCount(Read("/proc/stat"));
    public static string LoadAverage() => Read("/proc/loadavg")?.Split(' ').FirstOrDefault();
    public static Dictionary<string, (long rx, long tx)> NetDev() => ParseNetDev(Read("/proc/net/dev"));
    public static string DefaultInterface() => ParseDefaultRoute(Read("/proc/net/route"));
    public static int? WifiLevel(string iface) => ParseWirelessLevel(Read("/proc/net/wireless"), iface);
    public static List<string> Nameservers() => ParseNameservers(Read("/etc/resolv.conf"));
    public static bool SdbListening() => ParseListening(Read("/proc/net/tcp"), 26101) || ParseListening(Read("/proc/net/tcp6"), 26101);
    public static string LinkSpeed(string iface)
    {
        var speed = Read("/sys/class/net/" + iface + "/speed")?.Trim();
        var duplex = Read("/sys/class/net/" + iface + "/duplex")?.Trim();
        return int.TryParse(speed, out var s) && s > 0 ? s + " Mb/s" + (duplex == "full" ? " full duplex" : "") : null;
    }
    public static string Ipv4(string iface)
    {
        try
        {
            var n = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(x => x.Name == iface);
            return n?.GetIPProperties().UnicastAddresses.Select(u => u.Address).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)?.ToString();
        }
        catch { return null; }
    }
    // Marker files are SMACK-labelled "User" (stat denied to this app) but their directory is "_",
    // so names come from readdir only. -1 = could not list (unknown), never "0 in place".
    public static int CountMarkers(IEnumerable<string> names)
    {
        var set = new HashSet<string>(names, StringComparer.Ordinal);
        return VendorMarkers.Count(set.Contains);
    }
    public static int MarkersInPlace()
    {
        try { return CountMarkers(Directory.EnumerateFileSystemEntries("/opt/usr/share/systemd/system").Select(Path.GetFileName)); }
        catch { return -1; }
    }
    // Other apps' /proc entries are invisible to this sandbox, so ask the app framework instead:
    // disabled apps are not returned by app_manager_get_app_info. -1 = API unusable (self-check failed).
    [DllImport("libcapi-appfw-app-manager.so.0")] static extern int app_manager_get_app_info(string appId, out IntPtr info);
    [DllImport("libcapi-appfw-app-manager.so.0")] static extern int app_info_is_enabled(IntPtr info, out bool enabled);
    [DllImport("libcapi-appfw-app-manager.so.0")] static extern int app_info_destroy(IntPtr info);
    static bool? AppEnabled(string id)
    {
        try
        {
            if (app_manager_get_app_info(id, out var h) != 0 || h == IntPtr.Zero) return false;
            try { return app_info_is_enabled(h, out var e) == 0 ? e : (bool?)null; } finally { app_info_destroy(h); }
        }
        catch { return null; }
    }
    public static int DisabledAppCount(string selfId)
    {
        if (AppEnabled(selfId) != true) return -1;
        int disabled = 0;
        foreach (var id in DisabledApps)
        {
            var e = AppEnabled(id);
            if (e == null) return -1;
            if (e == false) disabled++;
        }
        return disabled;
    }
    [DllImport("libcapi-system-info.so.0")] static extern int system_info_get_platform_string(string key, out IntPtr value);
    [DllImport("libc.so.6")] static extern void free(IntPtr p);
    public static string SystemInfo(string key)
    {
        try
        {
            if (system_info_get_platform_string(key, out var p) != 0 || p == IntPtr.Zero) return null;
            try { return Marshal.PtrToStringUTF8(p); } finally { free(p); }
        }
        catch { return null; }
    }

    // ---- local usage log (data dir, never transmitted) ----
    public static void RecordLaunch(string file, string name, DateTime when)
    {
        try
        {
            File.AppendAllText(file, when.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "\t" + name.Replace('\t', ' ').Replace('\n', ' ') + "\n");
            var lines = File.ReadAllLines(file);
            if (lines.Length > 4000) File.WriteAllLines(file, lines.Skip(lines.Length - 2000));
        }
        catch { }
    }
    public static List<(string name, int count)> UsageSummary(IEnumerable<string> lines, DateTime today, int days)
    {
        var cutoff = today.Date.AddDays(1 - days);
        var counts = new Dictionary<string, int>();
        foreach (var line in lines)
        {
            int t = line.IndexOf('\t'); if (t <= 0) continue;
            if (!DateTime.TryParseExact(line.Substring(0, t), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) || d < cutoff || d > today.Date) continue;
            var n = line.Substring(t + 1).Trim(); if (n.Length == 0) continue;
            counts[n] = (counts.TryGetValue(n, out var c) ? c : 0) + 1;
        }
        return counts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => (kv.Key, kv.Value)).ToList();
    }

    // ---- formatting ----
    // Thread border router state from /proc/net/if_inet6 (readable in the app sandbox; ot-ctl is not).
    // RLOC = mesh-local prefix + 0000:00ff:fe00:<rloc16>; ALOC 0xfc00 = leader; an off-mesh global/ULA address = OMR route.
    public static (bool present, int rloc16, bool leader, bool homeRoute, int addresses) ParseThread(string text, string iface)
    {
        var rows = new List<(string addr, string scope)>();
        foreach (var line in (text ?? "").Split('\n'))
        {
            var f = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (f.Length >= 6 && f[5] == iface && f[0].Length == 32) rows.Add((f[0].ToLowerInvariant(), f[3]));
        }
        int rloc = -1; bool leader = false; string mesh = null;
        foreach (var (a, _) in rows)
        {
            if (!a.Substring(16, 12).Equals("000000fffe00")) continue;
            int v = Convert.ToInt32(a.Substring(28, 4), 16);
            if (v == 0xfc00) leader = true; else if (v < 0xfc00) { rloc = v; mesh = a.Substring(0, 16); }
        }
        bool omr = false;
        foreach (var (a, scope) in rows)
            if (scope == "00" && !a.StartsWith("fe80") && mesh != null && a.Substring(0, 16) != mesh) omr = true;
        return (rows.Count > 0, rloc, leader, omr, rows.Count);
    }
    public static (bool present, int rloc16, bool leader, bool homeRoute, int addresses) Thread() => ParseThread(Read("/proc/net/if_inet6"), "wpan0");
    public static bool Ipv6Forwarding() => Read("/proc/sys/net/ipv6/conf/all/forwarding")?.Trim() == "1";
    // ---- TV inputs: Samsung's own source list (libsource-api get_source_list JSON) ----
    public sealed class InputSource { public int Type; public string Name, Port; public bool Connected, Known; public int Power; }  // Known: the TV answered
    static string Unescape(string v) { try { return Uri.UnescapeDataString(v ?? ""); } catch { return v ?? ""; } }
    // HDMI 1-4 are source types 13-16 and always listed (not-connected ports included); Live TV (0) only if the TV reports it.
    public static List<InputSource> ParseSources(string json)
    {
        var seen = new Dictionary<int, (string edit, string detected, bool conn, int power)>();
        bool known = false;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json ?? "");
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                int t = e.TryGetProperty("sourceType", out var st) && st.TryGetInt32(out var tv) ? tv : -1;
                if (t != 0 && (t < 13 || t > 16)) continue;
                string Str(string k) => e.TryGetProperty(k, out var p) && p.ValueKind == System.Text.Json.JsonValueKind.String ? Unescape(p.GetString()).Trim() : "";
                bool conn = e.TryGetProperty("connectionState", out var c) && c.ValueKind == System.Text.Json.JsonValueKind.True;
                int pw = e.TryGetProperty("powerState", out var ps) && ps.TryGetInt32(out var pv) ? pv : 0;
                seen[t] = (Str("editName"), Str("detectedName"), conn, pw);
            }
            known = doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0;
        }
        catch { }
        var list = new List<InputSource>();
        for (int t = 13; t <= 16; t++)
        {
            string port = "HDMI " + (t - 12);
            if (seen.TryGetValue(t, out var v))
            {
                string name = v.edit.Length > 0 ? v.edit : v.detected.Length > 0 ? v.detected : port;
                list.Add(new InputSource { Type = t, Name = name, Port = port, Connected = v.conn, Power = v.power, Known = known });
            }
            else list.Add(new InputSource { Type = t, Name = port, Port = port, Connected = false, Power = 0, Known = known });
        }
        if (seen.ContainsKey(0)) list.Add(new InputSource { Type = 0, Name = "Live TV", Port = "Antenna", Connected = true, Power = 0, Known = known });
        return list;
    }
    // declared as string (like Samsung's own binding): the marshaller frees the malloc'd JSON buffer
    [System.Runtime.InteropServices.DllImport("/usr/lib/libsource-api.so")] static extern string get_source_list();
    public static List<InputSource> Sources()
    {
        try { return ParseSources(get_source_list()); }
        catch { return ParseSources(null); }
    }

    public static string Bytes(double b)
    {
        string[] u = { "B", "KB", "MB", "GB", "TB" }; int i = 0;
        while (b >= 1000 && i < u.Length - 1) { b /= 1000; i++; }
        return (i == 0 || b >= 100 ? b.ToString("0", CultureInfo.InvariantCulture) : b.ToString("0.0", CultureInfo.InvariantCulture)) + " " + u[i];
    }
    public static string Duration(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return t.TotalDays >= 1 ? (int)t.TotalDays + " d " + t.Hours + " h" : t.TotalHours >= 1 ? (int)t.TotalHours + " h " + t.Minutes + " min" : Math.Max(1, t.Minutes) + " min";
    }
}
