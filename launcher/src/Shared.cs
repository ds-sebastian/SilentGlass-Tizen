using System;
using System.Collections.Generic;
using System.Text.Json;

// What SilentGlass and its Home Assistant bridge (devices/s90d/device/ha-bridge) agree on, compiled into both: app ids,
// the launch extras, the files they exchange in SilentGlass's data directory, and how the TV's input list and the
// small key=value files read. No NUI, no native calls.
public static class Shared
{
    public const string Self = "com.samsung.tv.bixby-promotion", Viewer = "org.tizen.tv-viewer", SamsungHome = "com.samsung.tv.csfs", Art = "org.tizen.art-app";
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
        ThreadSwitch = "thread-switch";                           // touched to apply ThreadOff now (quiet-otbr-switch.path)
    public const string StateFile = "/tmp/silentglass-state.txt";  // SilentGlass's state (ui=, backdrop=, fg=, ...), read by the bridge
    public const string ThreadUnit = "/opt/usr/home/owner/.config/systemd/user/quiet-otbr-switch.path";  // the border router is installed

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
