using System;
using System.Collections.Generic;
using System.Text.Json;

// What SilentGlass and its Home Assistant bridge (devices/s90d/device/ha-bridge) agree on, compiled into both: app ids,
// the launch extras, the files they exchange in SilentGlass's data directory, and how the TV's input list and the
// small key=value files read. No NUI, no native calls.
public static class Shared
{
    // the one version of the whole thing: Info, the bridge (Home Assistant sw_version), the release and install.sh
    public const string Version = "1.4.1";
    public const string Self = "com.samsung.tv.bixby-promotion", Viewer = "org.tizen.tv-viewer", SamsungHome = "com.samsung.tv.csfs", Art = "org.tizen.art-app",
        SamsungApps = "org.volt.apps";  // Samsung's Apps panel: 1 2 3 4 5 there opens Developer Mode (the Host PC IP)
    public static bool IsAmbient(string id) => id is Art or "com.samsung.tv.home.art" or "com.samsung.tv.ambientfte";

    // launch extras: from the bridge (show a notification / switch input), any caller (open one part), our own relaunch
    public const string ExtraNotice = "sg_notice", ExtraSource = "sg_source", ExtraOpen = "sg_open", ExtraSelf = "sg_self";

    // in the data directory (/opt/usr/home/owner/apps_rw/com.samsung.tv.bixby-promotion/data)
    public const string NoticeFile = "notice.json",          // bridge -> SilentGlass; taking (deleting) it is the acknowledgement
        SetupRequest = "setup-request", SetupClose = "setup-close", // SilentGlass -> bridge: open / close the phone page
        SetupFile = "setup.json",                                 // bridge -> SilentGlass: the page's address, code and progress
        BridgeStatus = "bridge-status.txt",                       // bridge -> Info, rewritten every minute
        PrefsFile = "silentglass-prefs.txt", LocationFile = "location.txt", // SilentGlass's settings; the phone page edits them too
        NotifyOff = "notify-off", ThreadOff = "thread-off",       // switches: present = off
        InternetOff = "internet-off", InternetApplied = "internet-applied.txt", // present = off; what the switch replaced
        ThreadSwitch = "thread-switch",                           // touched to apply ThreadOff now (quiet-otbr-switch.path)
        ClockOffset = "clock-offset",                             // bridge -> SilentGlass: seconds the TV's unset clock is behind the router's
        InternetFailsafe = "internet-failsafe.txt";               // bridge: the switch is off, but the home network was lost, so the TV's own settings are back until it answers again
    // Samsung's app list. Its `is_network` = 1 on our app slot (a leftover of the Bixby app) makes Samsung refuse to open
    // SilentGlass from Home ("network error") whenever there is no gateway; the bridge keeps it 0.
    public const string WasDb = "/opt/dbspace/.webappservice.db";
    public const string StateFile = "/tmp/silentglass-state.txt";  // SilentGlass's state (ui=, backdrop=, fg=, ...), read by the bridge
    public const string ThreadUnit = "/opt/usr/home/owner/.config/systemd/user/quiet-otbr-switch.path";  // the border router is installed

    public const string BridgeUnit = "/opt/usr/home/owner/.config/systemd/user/silentglass-ha.service";  // applies the Internet switch
    public static bool InternetWanted(string data) => !System.IO.File.Exists(data + "/" + InternetOff);
    public static void SetInternet(string data, bool on)
    {
        if (on) System.IO.File.Delete(data + "/" + InternetOff);
        else System.IO.File.WriteAllText(data + "/" + InternetOff, "");
    }
    // routes to the Internet from the kernel's own tables. Only routes through a gateway count: with the switch off,
    // connman leaves an on-link "default dev eth0" that can't leave the home network (nothing answers its ARP).
    public static bool DefaultRoute4(string routeTable)
    {
        foreach (var line in (routeTable ?? "").Split('\n')) {
            var f = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (f.Length > 7 && f[0] != "Iface" && f[0] != "lo" && f[1] == "00000000" && f[7] == "00000000"
                && int.TryParse(f[3], System.Globalization.NumberStyles.HexNumber, null, out int fl) && (fl & 3) == 3) return true;  // up + gateway
        }
        return false;
    }
    // IPv6 reaches the Internet only with a gateway route AND a global address (2000::/3); the home network's
    // ULA/Thread routers advertise routes, but without a public address nothing gets out.
    public static bool DefaultRoute6(string routeTable, string addresses)
    {
        bool global = false;
        foreach (var line in (addresses ?? "").Split('\n')) {
            var f = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (f.Length >= 6 && f[5] != "lo" && f[0].Length == 32 && (f[0][0] == '2' || f[0][0] == '3')) global = true;
        }
        if (!global) return false;
        foreach (var line in (routeTable ?? "").Split('\n')) {
            var f = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (f.Length >= 10 && f[0] == new string('0', 32) && f[1] == "00" && f[9] != "lo"
                && int.TryParse(f[8], System.Globalization.NumberStyles.HexNumber, null, out int fl) && (fl & 0x200) == 0 && (fl & 3) == 3) return true;
        }
        return false;
    }
    public static string InternetStatus(bool wanted, string route4, string route6, string addr6)
    {
        bool v4 = DefaultRoute4(route4), v6 = DefaultRoute6(route6, addr6);
        if (wanted) return v4 || v6 ? "Allowed" : "No connection";
        return v4 ? "Applying" : v6 ? "IPv6 open" : "Blocked";
    }
    public static string InternetStatus(string data)
    {
        string R(string p) { try { return System.IO.File.ReadAllText(p); } catch { return ""; } }
        if (!InternetWanted(data) && System.IO.File.Exists(data + "/" + InternetFailsafe)) return "Open · home network lost";
        return InternetStatus(InternetWanted(data), R("/proc/net/route"), R("/proc/net/ipv6_route"), R("/proc/net/if_inet6"));
    }

    // ---- the clock. Samsung sets the TV's clock only from its own time server, so after a cold start with the
    // Internet switch off it stays at 1970. The bridge then measures the router's clock and leaves the difference in
    // ClockOffset; these add it while (and only while) the TV's own clock is unset. ----
    public static bool ClockUnset(DateTime utc) => utc.Year < 2020;
    static long offsetRead = long.MinValue / 2; static double offsetSec;
    public static DateTime UtcNow(string data)
    {
        var now = DateTime.UtcNow;
        if (!ClockUnset(now)) return now;
        if (Environment.TickCount64 - offsetRead > 30_000) {  // read the file at most every 30 s
            offsetRead = Environment.TickCount64; offsetSec = 0;
            try { double.TryParse(System.IO.File.ReadAllText(data + "/" + ClockOffset).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out offsetSec); } catch { }
        }
        return now.AddSeconds(offsetSec);
    }
    public static DateTime Now(string data) => UtcNow(data).ToLocalTime();

    // the TV's one Game Mode setting (Off / On / Auto) in plain words
    public static string GameName(string tvName) => tvName switch { "Auto" => "Automatic", "On" => "Always", "Off" => "Never", _ => tvName };

    // key=value lines; '#' comments; the last value of a key wins
    public static Dictionary<string, string> ParseConf(string text)
    {
        var d = new Dictionary<string, string>();
        foreach (var raw in (text ?? "").Split('\n')) {
            var line = raw.Trim(); int i = line.IndexOf('=');
            if (i > 0 && line[0] != '#') d[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
        }
        return d;
    }

    // ---- the TV's inputs, from libsource-api get_source_list (JSON). HDMI 1-4 (source types 13-16) are always listed,
    // not-connected ports too; Live TV (0) only if the TV reports it. Name: the one set on the TV, else the HDMI-CEC
    // name, else the port. powerState is not used: devices behind the soundbar report Off. ----
    public const int Hdmi1 = 13, Hdmi4 = 16;
    public static bool IsHdmi(int type) => type >= Hdmi1 && type <= Hdmi4;
    public sealed class Input { public int Type; public string Port, Edit = "", Detected = "", Name; public bool Connected; }
    // known: the TV answered with a list (otherwise every port reads as not connected)
    public static List<Input> ParseInputs(string json, out bool known)
    {
        var seen = new Dictionary<int, Input>(); known = false;
        try {
            using var doc = JsonDocument.Parse(json ?? "");
            foreach (var e in doc.RootElement.EnumerateArray()) {
                int t = e.TryGetProperty("sourceType", out var st) && st.TryGetInt32(out var v) ? v : -1;
                if (t != 0 && !IsHdmi(t)) continue;
                string S(string k) => e.TryGetProperty(k, out var p) && p.ValueKind == JsonValueKind.String ? Unescape(p.GetString()).Trim() : "";
                seen[t] = new Input { Type = t, Edit = S("editName"), Detected = S("detectedName"), Connected = e.TryGetProperty("connectionState", out var c) && c.ValueKind == JsonValueKind.True };
            }
            known = doc.RootElement.GetArrayLength() > 0;
        } catch { }
        var list = new List<Input>();
        for (int t = Hdmi1; t <= Hdmi4; t++) {
            var i = seen.TryGetValue(t, out var x) ? x : new Input { Type = t };
            i.Port = "HDMI " + (t - 12);
            i.Name = i.Edit.Length > 0 ? i.Edit : i.Detected.Length > 0 ? i.Detected : i.Port;
            list.Add(i);
        }
        if (seen.ContainsKey(0)) list.Add(new Input { Type = 0, Port = "Antenna", Name = "Live TV", Connected = true });
        return list;
    }
    static string Unescape(string v) { try { return Uri.UnescapeDataString(v ?? ""); } catch { return v ?? ""; } }
}
