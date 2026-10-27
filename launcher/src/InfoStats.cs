using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

// Read-only device facts for the Info panel. Every reader returns null/empty on failure; the parsers take file text
// so tests/CatalogTests.cs runs them off the TV.
public static class InfoStats
{
    // the vendor stop markers (/opt/usr/share/systemd/system) and the apps the privacy batches disable
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
        "com.samsung.tv.home.media", "com.samsung.tv.cobalt-yt",
        // batch F: Samsung's country preloads come back after an uninstall, so they stay installed but disabled
        "org.tizen.primevideo", "org.tizen.primevideo-dummy", "org.tizen.primevideo_companionapp", "com.samsung.tv.aria-video", "com.samsung.tv.aria-dummy", "org.tizen.browser",
        // batch E (2026-10-26)
        "com.samsung.tv.cis-api-service-app", "com.samsung.tv.cis", "com.samsung.tizen.samsung-analytics", "org.tizen.multi-assistant-service",
        "com.samsung.tv.aov-service", "com.samsung.tv.voice-instant-app", "org.tizen.voice-onboarding", "com.samsung.tv.mybixby",
        "com.samsung.tv.bixby-context-client", "com.samsung.tv.bixbycapsuleviewer", "adplatform.ADPlayer", "com.samsung.tv.adplayer",
        "com.samsung.tv.ad-web-framework" };

    static string Read(string path) { try { return File.ReadAllText(path); } catch { return null; } }

    // ---- parsers ----
    public static double? ParseUptime(string text) =>
        double.TryParse(text?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(), NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s : null;
    // /proc/net/route: the interface of the default route
    public static string ParseDefaultRoute(string text) =>
        (text ?? "").Split('\n').Skip(1).Select(l => l.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries)).FirstOrDefault(p => p.Length > 2 && p[1] == "00000000")?[0];
    // /proc/net/wireless: the signal level in dBm (0 = not associated)
    public static int? ParseWirelessLevel(string text, string iface)
    {
        var line = text?.Split('\n').FirstOrDefault(l => l.TrimStart().StartsWith(iface + ":"));
        var v = line?.Substring(line.IndexOf(':') + 1).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return v != null && v.Length > 2 && int.TryParse(v[2].TrimEnd('.'), out var level) && level != 0 ? level : null;
    }
    public static int? ParseTemperature(string text) => int.TryParse(text?.Trim(), out int c) && c > 0 && c < 150 ? c : null;
    // Thread from /proc/net/if_inet6 (readable in the app sandbox; ot-ctl is not). RLOC = mesh-local prefix +
    // 0000:00ff:fe00:<rloc16>; ALOC 0xfc00 = leader; a global/ULA address off the mesh prefix = a route to the home network.
    public static (bool present, int rloc16, bool leader, bool homeRoute) ParseThread(string text, string iface)
    {
        var rows = new List<(string addr, string scope)>();
        foreach (var line in (text ?? "").Split('\n')) {
            var f = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (f.Length >= 6 && f[5] == iface && f[0].Length == 32) rows.Add((f[0].ToLowerInvariant(), f[3]));
        }
        int rloc = -1; bool leader = false; string mesh = null;
        foreach (var (a, _) in rows) {
            if (a.Substring(16, 12) != "000000fffe00") continue;
            int v = Convert.ToInt32(a.Substring(28, 4), 16);
            if (v == 0xfc00) leader = true; else if (v < 0xfc00) { rloc = v; mesh = a.Substring(0, 16); }
        }
        bool home = mesh != null && rows.Any(r => r.scope == "00" && !r.addr.StartsWith("fe80") && r.addr.Substring(0, 16) != mesh);
        return (rows.Count > 0, rloc, leader, home);
    }
    public static string Duration(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return t.TotalDays >= 1 ? (int)t.TotalDays + " d " + t.Hours + " h" : t.TotalHours >= 1 ? (int)t.TotalHours + " h " + t.Minutes + " min" : Math.Max(1, t.Minutes) + " min";
    }

    // ---- readers (TV) ----
    public static double? Uptime() => ParseUptime(Read("/proc/uptime"));
    public static string DefaultInterface() => ParseDefaultRoute(Read("/proc/net/route"));
    public static int? WifiLevel(string iface) => ParseWirelessLevel(Read("/proc/net/wireless"), iface);
    public static string LinkSpeed(string iface) => int.TryParse(Read("/sys/class/net/" + iface + "/speed")?.Trim(), out var s) && s > 0 ? s + " Mb/s" : null;
    public static int? ChipTemperature() => ParseTemperature(Read("/sys/devices/platform/soc/thermal/temperature"));
    public static (bool present, int rloc16, bool leader, bool homeRoute) Thread() => ParseThread(Read("/proc/net/if_inet6"), "wpan0");
    public static bool Ipv6Forwarding() => Read("/proc/sys/net/ipv6/conf/all/forwarding")?.Trim() == "1";

    // declared as string (like Samsung's own binding): the marshaller frees the malloc'd JSON buffer
    [DllImport("/usr/lib/libsource-api.so")] static extern string get_source_list();
    public static List<Shared.Input> Inputs(out bool known)
    {
        string json = null; try { json = get_source_list(); } catch { }
        return Shared.ParseInputs(json, out known);
    }

    // ---- privacy: other apps' /proc entries are invisible to this sandbox, so ask the app framework (a disabled app is
    // not returned by app_manager_get_app_info). Marker files are SMACK-labelled "User" (stat is denied to this app)
    // but their directory is "_", so their names come from readdir. ----
    [DllImport("libcapi-appfw-app-manager.so.0")] static extern int app_manager_get_app_info(string appId, out IntPtr info);
    [DllImport("libcapi-appfw-app-manager.so.0")] static extern int app_info_is_enabled(IntPtr info, out bool enabled);
    [DllImport("libcapi-appfw-app-manager.so.0")] static extern int app_info_destroy(IntPtr info);
    static bool? AppEnabled(string id)
    {
        try {
            if (app_manager_get_app_info(id, out var h) != 0 || h == IntPtr.Zero) return false;
            try { return app_info_is_enabled(h, out var e) == 0 ? e : null; } finally { app_info_destroy(h); }
        } catch { return null; }
    }
    // what is not blocked: missing vendor markers ("marker:" + name) and enabled apps; null = could not check
    public static List<string> PrivacyGaps(string selfId)
    {
        HashSet<string> names;
        try { names = Directory.EnumerateFileSystemEntries("/opt/usr/share/systemd/system").Select(Path.GetFileName).ToHashSet(); }
        catch { return null; }
        if (AppEnabled(selfId) != true) return null;  // the self-check: the API answers for an app that is surely enabled
        var gaps = VendorMarkers.Where(m => !names.Contains(m)).Select(m => "marker:" + m).ToList();
        foreach (var id in DisabledApps) {
            var e = AppEnabled(id);
            if (e == null) return null;
            if (e == true) gaps.Add(id);
        }
        return gaps;
    }
}
