using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

// SilentGlass Home Assistant bridge: publishes the TV's state over MQTT with Home Assistant discovery and accepts a
// fixed set of commands. Runs as the TV's owner user (not root). Minimal MQTT 3.1.1 client, no dependencies.
// Config: /opt/usr/home/owner/.config/silentglass-ha.conf (mode 600): host, port, user, password, optional name.
// Input names come from the TV itself (renamable from Home Assistant). Log (events only): /run/user/5001/silentglass-ha.log
// Notifications: Home Assistant's "Notifications for Android TV" integration can send to the TV (port 7676, accepted
// only from the broker's host and notify_from= addresses); SilentGlass shows them as a banner on any input.
static class Bridge
{
    const string Base = "silentglass/s90d", Node = "silentglass_s90d";
    const string Launcher = "com.samsung.tv.bixby-promotion", ArtApp = "org.tizen.art-app";
    const string Data = "/opt/usr/home/owner/apps_rw/com.samsung.tv.bixby-promotion/data";
    const string ThreadUnit = "/opt/usr/home/owner/.config/systemd/user/quiet-otbr-switch.path";
    const string Version = "2.2";
    const int Mbr = -2;  // the TV keeps names per "activity index"; -2 is the default slot (Samsung's own binding uses it)
    static Dictionary<string, string> conf;
    static Mqtt mqtt;
    static readonly Dictionary<string, string> last = new();
    static readonly object gate = new(), tv = new();
    static string discoverySig = "";

    static void Log(string s) { try { File.AppendAllText("/run/user/5001/silentglass-ha.log", DateTime.Now.ToString("MM-dd HH:mm:ss ") + s + "\n"); } catch { } }

    // ---- native TV APIs (all usable from the owner session) ----
    [DllImport("libvconf.so.0")] static extern int vconf_get_int(string key, out int value);
    [DllImport("libvconf.so.0")] static extern IntPtr vconf_get_str(string key);
    [DllImport("libc.so.6")] static extern void free(IntPtr p);
    [DllImport("/usr/lib/libsource-api.so")] static extern string get_source_list();
    [DllImport("/usr/lib/libsource-api.so")] static extern int get_device_type(int type, int mbr);
    [DllImport("/usr/lib/libsource-api.so")] static extern bool set_edit_name(int type, string name, int mbr);
    [DllImport("/usr/lib/libsource-api.so")] static extern bool set_source_detetedname_and_devicetype(int type, string name, uint deviceType, int mbr);
    [DllImport("libsettings-api.so")] static extern bool CSharpGetItemValue(string id, out string name, out string value);
    [DllImport("libsettings-api.so")] static extern bool CSharpSetItemValue(string id, string value);
    [DllImport("libsettings-api.so")] static extern bool CSharpGetItemMinMaxStepValue(string id, out double min, out double max, out double step);
    [DllImport("libsettings-api.so")] static extern bool CSharpGetItemDataList(string id, StringBuilder list);
    [DllImport("libsettings-api.so")] static extern bool CSharpGetItemDataListEnable(string id, string value, out bool enabled);
    [DllImport("libsettings-api.so")] static extern bool CSharpGetItemDataListVisible(string id, string value, out bool visible);
    [DllImport("libcapi-media-sound-manager-tv.so.0")] static extern int sound_manager_get_master_volume(out int value);
    [DllImport("libcapi-media-sound-manager-tv.so.0")] static extern int sound_manager_set_master_volume(int value);
    [DllImport("libcapi-media-sound-manager-tv.so.0")] static extern int sound_manager_get_master_mute(out int value);
    [DllImport("libcapi-media-sound-manager-tv.so.0")] static extern int sound_manager_set_master_mute(int value);
    // the source library caches the list and refreshes it from D-Bus signals: those need a running GLib loop
    delegate void SourceListChangedCallback(int eventType, IntPtr userData);
    [DllImport("/usr/lib/libsource-api.so")] static extern void register_sourcelist_changed_callback(SourceListChangedCallback cb, IntPtr userData);
    [DllImport("libglib-2.0.so.0")] static extern IntPtr g_main_loop_new(IntPtr context, bool isRunning);
    [DllImport("libglib-2.0.so.0")] static extern void g_main_loop_run(IntPtr loop);
    static SourceListChangedCallback sourceListCallback;  // kept alive: native code holds the pointer
    static volatile bool inputsChanged;
    static void StartSourceWatch() => new Thread(() => {
        try {
            sourceListCallback = (t, d) => inputsChanged = true;
            register_sourcelist_changed_callback(sourceListCallback, IntPtr.Zero);
            g_main_loop_run(g_main_loop_new(IntPtr.Zero, false));
        } catch (Exception e) { Log("source watch: " + e.GetType().Name); }
    }) { IsBackground = true, Name = "glib" }.Start();
    // ---- power: a virtual Power key on /dev/uinput (crw-rw-rw- on the TV), the same mechanism Samsung's remote server
    // (msf-server via libautoinput) uses. One persistent device, so a press needs no hot-plug wait. ----
    [DllImport("libc.so.6", SetLastError = true)] static extern int open(string path, int flags);
    [DllImport("libc.so.6", SetLastError = true)] static extern int ioctl(int fd, uint req, int arg);
    [DllImport("libc.so.6", SetLastError = true)] static extern int ioctl(int fd, uint req);
    [DllImport("libc.so.6", SetLastError = true)] static extern IntPtr write(int fd, byte[] buf, IntPtr n);
    [DllImport("libc.so.6")] static extern int close(int fd);
    const int KeyPower = 116; const uint UiSetEvBit = 0x40045564, UiSetKeyBit = 0x40045565, UiDevCreate = 0x5501;
    static int keyFd = -1;
    static bool OpenPowerKey()
    {
        if (keyFd >= 0) return true;
        int fd = open("/dev/uinput", 1 | 2048);  // O_WRONLY | O_NONBLOCK
        if (fd < 0) { Log("power key: cannot open /dev/uinput (" + Marshal.GetLastWin32Error() + ")"); return false; }
        var dev = new byte[1116];  // struct uinput_user_dev
        Encoding.ASCII.GetBytes("SilentGlass power key").CopyTo(dev, 0);
        BitConverter.GetBytes((ushort)6).CopyTo(dev, 80);       // BUS_VIRTUAL
        BitConverter.GetBytes((ushort)0x5347).CopyTo(dev, 82);  // vendor "SG"
        BitConverter.GetBytes((ushort)1).CopyTo(dev, 84);
        BitConverter.GetBytes((ushort)1).CopyTo(dev, 86);
        if (ioctl(fd, UiSetEvBit, 1) != 0 || ioctl(fd, UiSetKeyBit, KeyPower) != 0 || (long)write(fd, dev, (IntPtr)dev.Length) != dev.Length || ioctl(fd, UiDevCreate) != 0) {
            Log("power key: uinput setup failed (" + Marshal.GetLastWin32Error() + ")"); close(fd); return false;
        }
        keyFd = fd; return true;
    }
    static bool KeyEvent(ushort type, ushort code, int value)
    {
        var b = new byte[IntPtr.Size == 8 ? 24 : 16]; int o = IntPtr.Size == 8 ? 16 : 8;  // struct input_event
        BitConverter.GetBytes(type).CopyTo(b, o); BitConverter.GetBytes(code).CopyTo(b, o + 2); BitConverter.GetBytes(value).CopyTo(b, o + 4);
        return (long)write(keyFd, b, (IntPtr)b.Length) == b.Length;
    }
    static void PressPower()
    {
        lock (tv) {
            if (!OpenPowerKey()) return;
            bool ok = KeyEvent(1, KeyPower, 1) && KeyEvent(0, 0, 0);
            Thread.Sleep(80);
            ok &= KeyEvent(1, KeyPower, 0) && KeyEvent(0, 0, 0);
            if (!ok) { close(keyFd); keyFd = -1; Log("power key: write failed, device reset"); }
        }
    }
    static int VInt(string k) => vconf_get_int(k, out int v) == 0 ? v : -1;
    static string VStr(string k) { var p = vconf_get_str(k); if (p == IntPtr.Zero) return ""; var s = Marshal.PtrToStringUTF8(p); free(p); return s ?? ""; }

    public static bool ThreadInstalled => File.Exists(ThreadUnit);
    static bool ThreadWanted => !File.Exists(Data + "/thread-off");
    static bool ScreenOn => VInt("memory/pm/state") is int pm && pm >= 0 && pm != 4;

    // ---- inputs: names come from the TV (name set on the TV, else the HDMI-CEC name, else the port) ----
    public sealed class Input { public int Type; public string Port, Edit = "", Detected = "", Name; public bool Connected; }
    public static List<Input> ParseSources(string json)
    {
        var seen = new Dictionary<int, Input>();
        try {
            using var doc = JsonDocument.Parse(json ?? "");
            foreach (var e in doc.RootElement.EnumerateArray()) {
                int t = e.TryGetProperty("sourceType", out var st) && st.TryGetInt32(out var v) ? v : -1;
                if (t != 0 && (t < 13 || t > 16)) continue;
                string S(string k) { if (!e.TryGetProperty(k, out var p) || p.ValueKind != JsonValueKind.String) return ""; try { return Uri.UnescapeDataString(p.GetString()).Trim(); } catch { return p.GetString().Trim(); } }
                seen[t] = new Input { Type = t, Edit = S("editName"), Detected = S("detectedName"), Connected = e.TryGetProperty("connectionState", out var c) && c.ValueKind == JsonValueKind.True };
            }
        } catch { }
        var list = new List<Input>();
        for (int t = 13; t <= 16; t++) {
            var i = seen.TryGetValue(t, out var x) ? x : new Input { Type = t };
            i.Port = "HDMI " + (t - 12);
            i.Name = i.Edit.Length > 0 ? i.Edit : i.Detected.Length > 0 ? i.Detected : i.Port;
            list.Add(i);
        }
        if (seen.ContainsKey(0)) list.Add(new Input { Type = 0, Port = "TV", Name = "Live TV", Connected = true });
        // two ports with the same name would make the selector ambiguous: add the port
        foreach (var g in list.GroupBy(i => i.Name).Where(g => g.Count() > 1)) foreach (var i in g) i.Name += " (" + i.Port + ")";
        return list;
    }
    static List<Input> Inputs() { lock (tv) { try { return ParseSources(get_source_list()); } catch { return ParseSources(null); } } }
    public static int SourceId(string json)
    {
        var m = Regex.Match(json ?? "", "SOURCE_TYPE_HDMI(\\d)");
        if (m.Success) return 12 + int.Parse(m.Groups[1].Value);
        return Regex.IsMatch(json ?? "", "SOURCE_TYPE_(ATV|DTV|TV)") ? 0 : -1;
    }
    public static string AppName(string id, string input) => id switch {
        Launcher => "SilentGlass Home",
        "org.tizen.tv-viewer" => input,
        "com.samsung.tv.csfs" => "Samsung Home",
        ArtApp or "com.samsung.tv.home.art" or "com.samsung.tv.ambientfte" => "Ambient",
        "" => "None",
        _ => id,
    };

    // HDMI-CEC: re-scan, then read which device answers on which port
    static string Cec(string method, params string[] args) =>
        Run("/usr/bin/dbus-send", "--system --print-reply --dest=org.tizen.tv.hdmicec /org/tizen/tv/hdmicec org.tizen.tv.HdmiCec." + method + (args.Length > 0 ? " " + string.Join(" ", args) : ""), 8000);
    static List<int> Ints(string reply) => Regex.Matches(reply, @"int32 (-?\d+)").Select(m => int.Parse(m.Groups[1].Value)).ToList();
    static string Str(string reply) { var m = Regex.Match(reply, "string \"(.*)\""); return m.Success && !m.Groups[1].Value.Contains("Invalid UTF-8") ? m.Groups[1].Value.Trim() : ""; }
    public static Dictionary<int, string> CecNames()
    {
        var names = new Dictionary<int, string>();
        var count = Ints(Cec("GetDeviceCount", "boolean:true"));
        int n = count.Count > 1 && count[0] == 0 ? count[1] : 0;
        for (int i = 0; i < Math.Min(n, 15); i++) {
            var nm = Cec("GetDeviceName", "int32:" + i); var port = Ints(Cec("GetDevicePortNum", "int32:" + i));
            if (Ints(nm).FirstOrDefault(-1) != 0 || port.Count < 2 || port[0] != 0) continue;
            string name = Str(nm); int t = 12 + port[1];
            if (name.Length > 0 && t >= 13 && t <= 16 && !names.ContainsKey(t)) names[t] = name;
        }
        return names;
    }
    static void ResyncNames()
    {
        bool on = ScreenOn;
        Log("resync: searching (" + (on ? "screen on" : "standby") + ")");
        Cec("StartDeviceSearching"); Thread.Sleep(20000);
        var live = CecNames();
        Log("resync: CEC sees " + (live.Count == 0 ? "no devices" : string.Join(", ", live.Select(kv => "HDMI " + (kv.Key - 12) + "=" + kv.Value))));
        foreach (var i in Inputs().Where(i => i.Type >= 13)) {
            // in standby the devices sleep too: only learn names then, never clear one
            string want = live.TryGetValue(i.Type, out var n) ? n : on ? "" : i.Detected;
            if (want == i.Detected) continue;
            bool ok, kept = true;
            lock (tv) {
                ok = set_source_detetedname_and_devicetype(i.Type, want, (uint)Math.Max(0, get_device_type(i.Type, Mbr)), Mbr);
                // the TV treats a new detected name as a new device and resets the port's own name: put it back
                if (ok && i.Edit.Length > 0) kept = set_edit_name(i.Type, i.Edit, Mbr);
            }
            Log("resync: " + i.Port + " detected name '" + i.Detected + "' -> '" + want + "' " + (ok ? "ok" : "FAILED") + (i.Edit.Length > 0 ? ", kept name '" + i.Edit + "' " + (kept ? "ok" : "FAILED") : ""));
        }
    }

    // ---- settings ----
    static string Setting(string id) { lock (tv) { try { return CSharpGetItemValue(id, out _, out var v) ? v ?? "" : ""; } catch { return ""; } } }
    static bool SetSetting(string id, string v) { lock (tv) { try { return CSharpSetItemValue(id, v); } catch { return false; } } }
    public static List<(string value, string name)> ParseOptions(string list) =>
        (list ?? "").Split(';').Select(e => e.Split(',', 2)).Where(p => p.Length == 2 && p[0].Length is > 0 and < 10 && p[0].All(char.IsDigit) && p[1].Trim().Length > 0)
            .Select(p => (p[0], p[1].Trim())).ToList();
    static List<(string value, string name)> Options(string id, bool visibleOnly = true)
    {
        lock (tv) {
            var sb = new StringBuilder(32768);
            try { if (!CSharpGetItemDataList(id, sb)) return new(); } catch { return new(); }
            var all = ParseOptions(sb.ToString()); var r = new List<(string, string)>();
            foreach (var o in all)
                try { if (CSharpGetItemDataListEnable(id, o.value, out var en) && en && (!visibleOnly || CSharpGetItemDataListVisible(id, o.value, out var vis) && vis) && !r.Any(x => x.Item2 == o.name)) r.Add(o); } catch { }
            return r;
        }
    }
    static (double min, double max) Range(string id, double dmin, double dmax)
    {
        lock (tv) { try { if (CSharpGetItemMinMaxStepValue(id, out var a, out var b, out _) && b > a) return (a, b); } catch { } }
        return (dmin, dmax);
    }
    static readonly (string value, string name)[] Outputs = { ("0", "TV Speaker"), ("2", "Soundbar") };
    // Game Mode is one three-way setting (game-mode-auto: Auto / On / Off); "game-mode" only mirrors whether it is engaged
    public static string GameName(string tvName) => tvName switch { "Auto" => "Automatic", "On" => "Always", "Off" => "Never", _ => tvName };
    static List<(string value, string name)> GameOptions() => Options("game-mode-auto", false).Select(o => (o.value, GameName(o.name))).ToList();
    static string OptionName(List<(string value, string name)> opts, string v) => opts.Where(o => o.value == v).Select(o => o.name).FirstOrDefault() ?? "";

    static string Run(string file, string args, int ms)
    {
        try {
            using var p = Process.Start(new ProcessStartInfo(file, args) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false });
            var o = p.StandardOutput.ReadToEndAsync();
            if (!p.WaitForExit(ms)) { try { p.Kill(true); } catch { } return ""; }
            return o.Result;
        } catch { return ""; }
    }
    static string ThreadRole()
    {
        if (!ThreadWanted) return "off";
        var role = Run("/opt/usr/apps/com.samsung.tizen.smartthings-hub/bin/ot-ctl", "state", 4000).Split('\n')[0].Trim();
        return Regex.IsMatch(role, "^(router|leader|child|detached|disabled)$") ? role : "unavailable";
    }
    static string Read(string f) { try { return File.ReadAllText(f); } catch { return ""; } }
    // SilentGlass keeps its full-screen window hidden while it only shows the bar or a banner over the picture; the
    // foreground app id still names it then, so report the app it last saw in front (from its state file)
    public static string OnScreenApp(string fg, string state = null)
    {
        if (fg != Launcher) return fg;
        var m = Regex.Match(state ?? Read("/tmp/quiet-home-state.txt"), @"(?m)^overlay=([^\n]*)$");
        if (!m.Success || !m.Groups[1].Value.Contains("main-hidden")) return fg;
        var other = Regex.Match(m.Groups[1].Value, @"fg=([\w.\-]+)");
        return m.Groups[1].Value.StartsWith("off") && other.Success ? other.Groups[1].Value : fg;
    }

    // ---- publishing ----
    static void Pub(string sub, string value, bool force = false)
    {
        lock (gate) {
            if (!force && last.TryGetValue(sub, out var v) && v == value) return;
            if (mqtt?.Publish(Base + "/" + sub, value, true) == true) last[sub] = value;
        }
    }
    static void PublishFast(bool force)
    {
        var ins = Inputs();
        // new names change the selector's options: announce them first, or Home Assistant rejects the new state
        if (Signature(ins) != discoverySig) { Discovery(); force = true; }
        Pub("power", ScreenOn ? "ON" : "OFF", force);
        int src = SourceId(VStr("memory/eden/source/current_source"));
        string input = ins.Where(i => i.Type == src).Select(i => i.Name).FirstOrDefault() ?? "Other";
        Pub("input", input, force);
        Pub("app", AppName(OnScreenApp(VStr("memory/appfw/current_fullscreen_appid")), input), force);
        foreach (var i in ins.Where(i => i.Type >= 13)) {
            Pub("hdmi" + (i.Type - 12) + "_name", i.Edit, force);
            Pub("hdmi" + (i.Type - 12) + "_attr", "{\"detected_name\":\"" + J(i.Detected) + "\",\"shown_as\":\"" + J(i.Name) + "\",\"connected\":" + (i.Connected ? "true" : "false") + "}", force);
        }
        int vol = -1, mute = -1;
        lock (tv) { try { if (sound_manager_get_master_volume(out var v) == 0) vol = v; if (sound_manager_get_master_mute(out var m) == 0) mute = m; } catch { } }
        if (vol >= 0) Pub("volume", vol.ToString(), force);
        if (mute >= 0) Pub("mute", mute == 1 ? "ON" : "OFF", force);
        if (ThreadInstalled) Pub("thread_switch", ThreadWanted ? "ON" : "OFF", force);
        Pub("notifications", NoticesOn ? "ON" : "OFF", force);
    }
    static void PublishSlow(bool force)
    {
        if (int.TryParse(Read("/sys/devices/platform/soc/thermal/temperature").Trim(), out int t)) Pub("temperature", t.ToString(), force);
        var mem = Regex.Match(Read("/proc/meminfo"), @"MemAvailable:\s+(\d+)");
        if (mem.Success) Pub("memory", (long.Parse(mem.Groups[1].Value) / 1024).ToString(), force);
        if (ThreadInstalled) Pub("thread", ThreadRole(), force);
        foreach (var id in new[] { "brightness", "contrast" }) { var v = Setting(id); if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) Pub(id, v, force); }
        Pub("picture_mode", OptionName(Options("picturemode"), Setting("picturemode")), force);
        Pub("sound_mode", OptionName(Options("external-sound-mode"), Setting("external-sound-mode")), force);
        Pub("sound_output", OptionName(Outputs.ToList(), Setting("soundoutput")) is var o && o.Length > 0 ? o : "Other", force);
        Pub("sleep_timer", OptionName(Options("sleeptimer"), Setting("sleeptimer")), force);
        Pub("game_mode", OptionName(GameOptions(), Setting("game-mode-auto")), force);
    }
    // discovery depends on what the TV currently offers (input names, picture/sound modes)
    static string Signature(List<Input> ins) => string.Join("|", ins.Select(i => i.Name)) + "#" + string.Join("|", Options("picturemode").Select(o => o.name)) +
        "#" + string.Join("|", Options("external-sound-mode").Select(o => o.name));

    static string Device() => "{\"identifiers\":[\"" + Node + "\"],\"name\":\"" + J(conf.GetValueOrDefault("name", "Living Room TV")) +
        "\",\"manufacturer\":\"Samsung\",\"model\":\"QN65S90D (SilentGlass-Tizen)\",\"sw_version\":\"silentglass-ha " + Version + "\"}";
    static string J(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
    static string List(IEnumerable<string> xs) => "[" + string.Join(",", xs.Select(x => "\"" + J(x) + "\"")) + "]";
    static void Discovery()
    {
        var ins = Inputs(); discoverySig = Signature(ins);
        string common(string obj, string name, string icon, bool diag = false, bool config = false) => "\"unique_id\":\"" + Node + "_" + obj + "\",\"object_id\":\"living_room_tv_" + obj +
            "\",\"name\":\"" + name + "\",\"icon\":\"" + icon + "\",\"availability_topic\":\"" + Base + "/availability\",\"device\":" + Device() +
            (diag ? ",\"entity_category\":\"diagnostic\"" : config ? ",\"entity_category\":\"config\"" : "");
        string st(string obj, bool cmd = true) => ",\"state_topic\":\"" + Base + "/" + obj + "\"" + (cmd ? ",\"command_topic\":\"" + Base + "/" + obj + "/set\"" : "");
        void cfg(string comp, string obj, string body) => mqtt.Publish("homeassistant/" + comp + "/" + Node + "/" + obj + "/config", body == null ? "" : "{" + body + "}", true);
        cfg("binary_sensor", "power", null);  // v1 entity, replaced by the power switch
        cfg("switch", "power_switch", common("power", "Power", "mdi:power") + ",\"state_topic\":\"" + Base + "/power\",\"command_topic\":\"" + Base + "/power/set\"");
        cfg("select", "input", common("input", "Input", "mdi:video-input-hdmi") + st("input") + ",\"options\":" + List(ins.Select(i => i.Name).Append("Other")));
        cfg("sensor", "app", common("app", "App", "mdi:application") + st("app", false));
        cfg("number", "volume", common("volume", "Volume", "mdi:volume-high") + st("volume") + ",\"min\":0,\"max\":100,\"step\":1,\"mode\":\"slider\"");
        cfg("switch", "mute", common("mute", "Mute", "mdi:volume-off") + st("mute"));
        var br = Range("brightness", 0, 50); var co = Range("contrast", 0, 50);
        cfg("number", "brightness", common("brightness", "Brightness", "mdi:brightness-6") + st("brightness") + ",\"min\":" + br.min + ",\"max\":" + br.max + ",\"step\":1,\"mode\":\"slider\"");
        cfg("number", "contrast", common("contrast", "Contrast", "mdi:contrast-box", config: true) + st("contrast") + ",\"min\":" + co.min + ",\"max\":" + co.max + ",\"step\":1,\"mode\":\"slider\"");
        cfg("select", "picture_mode", common("picture_mode", "Picture mode", "mdi:palette") + st("picture_mode") + ",\"options\":" + List(Options("picturemode").Select(o => o.name).DefaultIfEmpty("Unavailable")));
        cfg("select", "sound_mode", common("sound_mode", "Sound mode", "mdi:surround-sound") + st("sound_mode") + ",\"options\":" + List(Options("external-sound-mode").Select(o => o.name).DefaultIfEmpty("Unavailable")));
        cfg("select", "sound_output", common("sound_output", "Sound output", "mdi:speaker") + st("sound_output") + ",\"options\":" + List(Outputs.Select(o => o.name).Append("Other")));
        cfg("select", "sleep_timer", common("sleep_timer", "Sleep timer", "mdi:timer-sand") + st("sleep_timer") + ",\"options\":" + List(Options("sleeptimer").Select(o => o.name).DefaultIfEmpty("Off")));
        cfg("switch", "game_mode", null);  // v2.0/2.1 switch drove the mirror value; replaced by the three-way select
        cfg("select", "game_mode_select", common("game_mode", "Game mode", "mdi:controller", config: true) + st("game_mode") + ",\"options\":" + List(GameOptions().Select(o => o.name).DefaultIfEmpty("Unavailable")));
        cfg("switch", "notifications", common("notifications", "Notifications", "mdi:message-badge", config: true) + st("notifications"));
        cfg("button", "home", common("home", "Home", "mdi:home") + ",\"command_topic\":\"" + Base + "/home/press\"");
        cfg("button", "ambient", common("ambient", "Ambient", "mdi:image-frame") + ",\"command_topic\":\"" + Base + "/ambient/press\"");
        cfg("button", "resync", common("resync", "Resync input names", "mdi:refresh", config: true) + ",\"command_topic\":\"" + Base + "/resync/press\"");
        for (int p = 1; p <= 4; p++)
            cfg("text", "hdmi" + p + "_name", common("hdmi" + p + "_name", "HDMI " + p + " name", "mdi:rename", config: true) + st("hdmi" + p + "_name") +
                ",\"max\":30,\"json_attributes_topic\":\"" + Base + "/hdmi" + p + "_attr\"");
        cfg("sensor", "temperature", common("temperature", "Chip temperature", "mdi:thermometer", diag: true) + st("temperature", false) + ",\"device_class\":\"temperature\",\"unit_of_measurement\":\"°C\",\"state_class\":\"measurement\"");
        cfg("sensor", "memory", common("memory", "Free memory", "mdi:memory", diag: true) + st("memory", false) + ",\"device_class\":\"data_size\",\"unit_of_measurement\":\"MB\",\"state_class\":\"measurement\"");
        if (ThreadInstalled) {
            cfg("sensor", "thread", common("thread", "Thread role", "mdi:access-point-network") + st("thread", false));
            cfg("switch", "thread_switch", common("thread_switch", "Thread border router", "mdi:access-point") + st("thread_switch"));
        }
    }

    // ---- commands (fixed allowlist; no shell) ----
    static void OnMessage(string topic, string payload)
    {
        if (topic == "homeassistant/status") { if (payload == "online") Resync(); return; }
        var cmd = topic.Substring(Base.Length + 1);
        Log("command " + cmd + " " + (payload.Length > 40 ? payload.Substring(0, 40) : payload));
        bool slow = true;
        switch (cmd) {
            case "power/set" when payload == "ON" || payload == "OFF":
                // the key toggles: press only when the TV is not already in the requested state
                if ((payload == "ON") != ScreenOn) { PressPower(); Log("power key pressed (" + payload + ")"); }
                Thread.Sleep(3000); slow = false; break;
            case "input/set": {
                var match = Inputs().Where(i => i.Name == payload).ToList();
                if (match.Count == 1) LaunchApp(Launcher, "quiet_source", match[0].Type.ToString());
                slow = false; break;
            }
            case "home/press": LaunchApp(Launcher, "quiet_page", "home"); slow = false; break;
            case "ambient/press": LaunchApp(ArtApp); slow = false; break;
            case "resync/press": ResyncNames(); break;
            case "thread_switch/set" when ThreadInstalled && (payload == "ON" || payload == "OFF"):
                if (payload == "OFF") File.WriteAllText(Data + "/thread-off", ""); else File.Delete(Data + "/thread-off");
                // same as the Info switch: quiet-otbr-switch.path watches this file and applies the change at once
                File.WriteAllText(Data + "/thread-switch", DateTime.UtcNow.ToString("o") + (payload == "ON" ? " on\n" : " off\n"));
                slow = false; break;
            case "volume/set" when double.TryParse(payload, NumberStyles.Float, CultureInfo.InvariantCulture, out var vol):
                lock (tv) sound_manager_set_master_volume((int)Math.Clamp(Math.Round(vol), 0, 100)); slow = false; break;
            case "mute/set" when payload == "ON" || payload == "OFF":
                lock (tv) sound_manager_set_master_mute(payload == "ON" ? 1 : 0); slow = false; break;
            case "brightness/set" or "contrast/set" when double.TryParse(payload, NumberStyles.Float, CultureInfo.InvariantCulture, out var n): {
                var id = cmd.Split('/')[0]; var r = Range(id, 0, 50);
                SetSetting(id, ((int)Math.Clamp(Math.Round(n), r.min, r.max)).ToString(CultureInfo.InvariantCulture)); break;
            }
            case "picture_mode/set": SetOption("picturemode", Options("picturemode"), payload); break;
            case "sound_mode/set": SetOption("external-sound-mode", Options("external-sound-mode"), payload); break;
            case "sleep_timer/set": SetOption("sleeptimer", Options("sleeptimer"), payload); break;
            case "sound_output/set": SetOption("soundoutput", Outputs.ToList(), payload); break;
            case "game_mode/set": SetOption("game-mode-auto", GameOptions(), payload); break;
            case "notifications/set" when payload == "ON" || payload == "OFF":
                if (payload == "OFF") File.WriteAllText(Data + "/notify-off", ""); else File.Delete(Data + "/notify-off");
                slow = false; break;
            default: {
                var m = Regex.Match(cmd, "^hdmi([1-4])_name/set$");
                if (m.Success) {
                    var name = Regex.Replace(payload, @"[\x00-\x1f]", "").Trim(); if (name.Length > 30) name = name.Substring(0, 30);
                    bool ok; lock (tv) ok = set_edit_name(12 + int.Parse(m.Groups[1].Value), name, Mbr);
                    Log("rename HDMI " + m.Groups[1].Value + " -> '" + name + "' " + (ok ? "ok" : "FAILED"));
                }
                break;
            }
        }
        Thread.Sleep(400); PublishFast(false); if (slow) PublishSlow(false);
    }
    static void SetOption(string id, List<(string value, string name)> opts, string name)
    {
        var o = opts.Where(x => x.name == name).ToList();
        if (o.Count == 1) Log(id + " -> " + name + " " + (SetSetting(id, o[0].value) ? "ok" : "FAILED"));
    }
    static void LaunchApp(string app, string key = null, string value = null) =>
        Log("launch " + app + " -> " + Run("/usr/bin/launch_app", key == null ? app : app + " " + key + " " + value, 8000).Trim());

    static void Resync() { Discovery(); PublishFast(true); PublishSlow(true); }

    // ---- notifications: the "Notifications for Android TV" protocol (Home Assistant nfandroidtv, library 0.1.5):
    // GET / is the connection check; POST / is multipart/form-data with msg, title, duration (s), fontsize (1 small,
    // 0 medium, 2 large, 3 max), position (0 bottom-right, 1 bottom-left, 2 top-right, 3 top-left, 4 center),
    // transparency, bkgcolor, interrupt, filename (icon) and filename2 (image). Shown by SilentGlass as a banner. ----
    const int NoticePort = 7676, NoticeMaxBody = 12 << 20;
    static readonly object noticeGate = new();
    static long noticeSeq;
    static bool NoticesOn => !File.Exists(Data + "/notify-off");
    static bool NoticeAllowed(IPAddress peer)
    {
        if (IPAddress.IsLoopback(peer)) return true;
        foreach (var h in (conf.GetValueOrDefault("host", "") + "," + conf.GetValueOrDefault("notify_from", "")).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            try { if (IPAddress.TryParse(h, out var ip) ? ip.Equals(peer) : Dns.GetHostAddresses(h).Any(a => a.Equals(peer))) return true; } catch { }
        return false;
    }
    static void StartNoticeServer() => new Thread(() => {
        while (true) {
            TcpListener l = null;
            try {
                l = new TcpListener(IPAddress.Any, NoticePort); l.Start(); Log("notifications: listening on " + NoticePort);
                while (true) { var c = l.AcceptTcpClient(); ThreadPool.QueueUserWorkItem(_ => ServeNotice(c)); }
            } catch (Exception e) { Log("notifications: listener " + e.GetType().Name + ": " + e.Message); }
            try { l?.Stop(); } catch { }
            Thread.Sleep(30000);
        }
    }) { IsBackground = true, Name = "notices" }.Start();
    static void ServeNotice(TcpClient c)
    {
        using (c) try {
            var peer = ((IPEndPoint)c.Client.RemoteEndPoint).Address; if (peer.IsIPv4MappedToIPv6) peer = peer.MapToIPv4();
            if (!NoticeAllowed(peer)) { Log("notifications: refused " + peer); return; }
            var s = c.GetStream(); s.ReadTimeout = 10000; s.WriteTimeout = 5000;
            var req = HttpRequest.Read(s, NoticeMaxBody);
            if (req == null) return;
            HttpRequest.Respond(s, req.Method == "GET" || req.Method == "POST" ? 200 : 405);
            if (req.Method == "POST") lock (noticeGate) HandleNotice(BuildNotice(ParseMultipart(req.Body, req.Boundary)));
        } catch (Exception e) { Log("notifications: " + e.GetType().Name + ": " + e.Message); }
    }
    public sealed class NoticeData { public string Title = "", Message = "", Size = "large", Position = "bottom-right", Ext = ""; public int Duration = 5, W, H; public byte[] Image; }
    public static NoticeData BuildNotice(Dictionary<string, byte[]> parts)
    {
        string T(string k, int max) { if (!parts.TryGetValue(k, out var b)) return ""; var s = Regex.Replace(Encoding.UTF8.GetString(b), @"[\x00-\x08\x0b-\x1f\x7f]", "").Trim(); return s.Length > max ? s.Substring(0, max) : s; }
        int N(string k, int d) => int.TryParse(T(k, 6), out var v) ? v : d;
        var n = new NoticeData { Title = T("title", 120), Message = T("msg", 400), Duration = Math.Clamp(N("duration", 5), 1, 300) };
        n.Size = N("fontsize", 2) switch { 1 => "small", 0 => "medium", 3 => "max", _ => "large" };
        n.Position = N("position", 0) switch { 1 => "bottom-left", 2 => "top-right", 3 => "top-left", 4 => "center", _ => "bottom-right" };
        // the image; the icon only when it is a real picture (the library always sends a 1x1 placeholder)
        foreach (var k in new[] { "filename2", "filename" })
            if (parts.TryGetValue(k, out var img) && img.Length > 512 && ImageType(img) is var ext && ext != null) {
                n.Image = img; n.Ext = ext; (n.W, n.H) = ImageSize(img); break;
            }
        return n.Title.Length > 0 || n.Message.Length > 0 || n.Image != null ? n : null;
    }
    public static string ImageType(byte[] b) =>
        b.Length > 12 && b[0] == 0xFF && b[1] == 0xD8 ? "jpg" : b.Length > 24 && b[0] == 0x89 && b[1] == (byte)'P' && b[2] == (byte)'N' && b[3] == (byte)'G' ? "png" :
        b.Length > 10 && b[0] == (byte)'G' && b[1] == (byte)'I' && b[2] == (byte)'F' ? "gif" :
        b.Length > 12 && Encoding.ASCII.GetString(b, 0, 4) == "RIFF" && Encoding.ASCII.GetString(b, 8, 4) == "WEBP" ? "webp" : null;
    // pixel size from the header (JPEG SOFn, PNG IHDR, GIF screen); (0,0) when unknown
    public static (int w, int h) ImageSize(byte[] b)
    {
        try {
            switch (ImageType(b)) {
                case "png": return ((b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19], (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23]);
                case "gif": return (b[6] | (b[7] << 8), b[8] | (b[9] << 8));
                case "jpg":
                    for (int i = 2; i + 9 < b.Length;) {
                        if (b[i] != 0xFF) { i++; continue; }
                        int m = b[i + 1];
                        if (m == 0xFF) { i++; continue; }
                        if (m == 0xD8 || m == 0x01 || (m >= 0xD0 && m <= 0xD7)) { i += 2; continue; }
                        int len = (b[i + 2] << 8) | b[i + 3];
                        if (m >= 0xC0 && m <= 0xCF && m != 0xC4 && m != 0xC8 && m != 0xCC) return ((b[i + 7] << 8) | b[i + 8], (b[i + 5] << 8) | b[i + 6]);
                        i += 2 + len;
                    }
                    break;
            }
        } catch { }
        return (0, 0);
    }
    static bool IsAmbient(string app) => app is ArtApp or "com.samsung.tv.home.art" or "com.samsung.tv.ambientfte";
    // hand the notice to SilentGlass: data/notice.json (+ image). Taking the file is its acknowledgement.
    static void HandleNotice(NoticeData n)
    {
        if (n == null) { Log("notifications: empty request"); return; }
        string fg = VStr("memory/appfw/current_fullscreen_appid"), what = "'" + (n.Title.Length > 0 ? n.Title : n.Message.Length > 40 ? n.Message.Substring(0, 40) : n.Message) + "'";
        string why = !NoticesOn ? "notifications are off" : !ScreenOn ? "screen off" : IsAmbient(fg) ? "Ambient" : null;
        if (why != null) { Log("notifications: " + what + " not shown (" + why + ")"); return; }
        foreach (var f in Directory.GetFiles(Data, "notice-*")) try { File.Delete(f); } catch { }
        long id = ++noticeSeq; string image = n.Image != null ? "notice-" + id + "." + n.Ext : "";
        if (image.Length > 0) File.WriteAllBytes(Data + "/" + image, n.Image);
        var json = JsonSerializer.Serialize(new Dictionary<string, object> {
            ["title"] = n.Title, ["message"] = n.Message, ["size"] = n.Size, ["position"] = n.Position, ["duration"] = n.Duration,
            ["image"] = image, ["w"] = n.W, ["h"] = n.H, ["at"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
        string file = Data + "/notice.json";
        File.WriteAllText(file + ".tmp", json); File.Move(file + ".tmp", file, true);
        bool Taken(int ms) { for (int i = 0; i < ms / 100 && File.Exists(file); i++) Thread.Sleep(100); return !File.Exists(file); }
        if (Taken(1500)) { Log("notifications: " + what + " shown"); return; }
        // SilentGlass is not running (it is closed in standby): start it for the notice - only over the TV viewer,
        // where starting it hidden is invisible; over another app it would send that app to the background
        string onScreen = OnScreenApp(fg);
        if (onScreen != "org.tizen.tv-viewer" && fg != Launcher) { File.Delete(file); Log("notifications: " + what + " not shown (SilentGlass not running over " + onScreen + ")"); return; }
        LaunchApp(Launcher, "quiet_notice", "1");
        Log("notifications: " + what + (Taken(8000) ? " shown (started SilentGlass)" : " NOT taken"));
        if (File.Exists(file)) File.Delete(file);
    }
    // multipart/form-data body -> field name -> bytes
    public static Dictionary<string, byte[]> ParseMultipart(byte[] body, string boundary)
    {
        var parts = new Dictionary<string, byte[]>();
        if (body == null || string.IsNullOrEmpty(boundary)) return parts;
        var delim = Encoding.ASCII.GetBytes("--" + boundary);
        int at = IndexOf(body, delim, 0);
        while (at >= 0) {
            int start = at + delim.Length;
            if (start + 1 < body.Length && body[start] == '-' && body[start + 1] == '-') break;  // closing delimiter
            int head = IndexOf(body, Encoding.ASCII.GetBytes("\r\n\r\n"), start);
            if (head < 0) break;
            int next = IndexOf(body, Encoding.ASCII.GetBytes("\r\n--" + boundary), head + 4);
            if (next < 0) break;
            var headers = Encoding.UTF8.GetString(body, start, head - start);
            var name = Regex.Match(headers, "name=\"([^\"]*)\"");
            if (name.Success) parts[name.Groups[1].Value] = body[(head + 4)..next];
            at = next + 2;
        }
        return parts;
    }
    static int IndexOf(byte[] hay, byte[] needle, int from)
    {
        for (int i = Math.Max(0, from); i <= hay.Length - needle.Length; i++) {
            int j = 0; while (j < needle.Length && hay[i + j] == needle[j]) j++;
            if (j == needle.Length) return i;
        }
        return -1;
    }

    static int Main()
    {
        conf = File.ReadAllLines("/opt/usr/home/owner/.config/silentglass-ha.conf").Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith("#") && l.Contains('=')).ToDictionary(l => l.Split('=', 2)[0].Trim(), l => l.Split('=', 2)[1].Trim());
        Log("start " + Version);
        StartSourceWatch();
        OpenPowerKey();
        StartNoticeServer();
        int backoff = 5;
        while (true) {
            try {
                mqtt = new Mqtt(conf["host"], int.Parse(conf.GetValueOrDefault("port", "1883")), "silentglass-s90d", conf.GetValueOrDefault("user"), conf.GetValueOrDefault("password"),
                    Base + "/availability", "offline", OnMessage);
                Log("connected"); backoff = 5;
                mqtt.Subscribe(Base + "/+/set", Base + "/+/press", "homeassistant/status");
                lock (gate) last.Clear();
                Resync(); mqtt.Publish(Base + "/availability", "online", true);
                for (long n = 1; mqtt.Alive; n++) {
                    Thread.Sleep(2000);
                    PublishFast(false);
                    if (inputsChanged) { inputsChanged = false; Log("source list changed"); }
                    if (n % 15 == 0) PublishSlow(false);
                    if (n % 150 == 0) { PublishFast(true); PublishSlow(true); }
                }
                Log("disconnected");
            } catch (Exception e) { Log("error " + e.GetType().Name + ": " + e.Message); }
            try { mqtt?.Close(); } catch { }
            Thread.Sleep(backoff * 1000); backoff = Math.Min(backoff * 2, 60);
        }
    }
}

// Minimal HTTP/1.1 request reader for the notification endpoint: one request per connection, Content-Length bodies.
sealed class HttpRequest
{
    public string Method = "", Boundary = ""; public byte[] Body = Array.Empty<byte>();
    public static HttpRequest Read(Stream s, int maxBody)
    {
        var buf = new byte[65536]; int got = 0, end = -1;
        while (end < 0) {
            if (got == buf.Length) return null;  // headers too large
            int n = s.Read(buf, got, buf.Length - got); if (n <= 0) return null; got += n;
            for (int i = Math.Max(0, got - n - 3); i + 3 < got; i++) if (buf[i] == 13 && buf[i + 1] == 10 && buf[i + 2] == 13 && buf[i + 3] == 10) { end = i; break; }
        }
        var lines = Encoding.ASCII.GetString(buf, 0, end).Split("\r\n");
        var r = new HttpRequest { Method = lines[0].Split(' ')[0] };
        int length = 0;
        foreach (var l in lines.Skip(1)) {
            int c = l.IndexOf(':'); if (c < 0) continue;
            string k = l.Substring(0, c).Trim().ToLowerInvariant(), v = l.Substring(c + 1).Trim();
            if (k == "content-length" && !int.TryParse(v, out length)) return null;
            if (k == "content-type") { var m = Regex.Match(v, "boundary=\"?([^\";]+)\"?"); if (m.Success) r.Boundary = m.Groups[1].Value; }
        }
        if (length < 0 || length > maxBody) return null;
        r.Body = new byte[length]; int have = Math.Min(length, got - end - 4);
        Array.Copy(buf, end + 4, r.Body, 0, have);
        while (have < length) { int n = s.Read(r.Body, have, length - have); if (n <= 0) return null; have += n; }
        return r;
    }
    public static void Respond(Stream s, int code)
    {
        var b = Encoding.ASCII.GetBytes("HTTP/1.1 " + code + (code == 200 ? " OK" : " Method Not Allowed") + "\r\nContent-Type: text/plain\r\nContent-Length: 2\r\nConnection: close\r\n\r\nOK");
        s.Write(b, 0, b.Length); s.Flush();
    }
}

// Minimal MQTT 3.1.1 client: CONNECT (with will), PUBLISH QoS 0, SUBSCRIBE QoS 0, PINGREQ, incoming PUBLISH.
sealed class Mqtt
{
    readonly TcpClient tcp; readonly NetworkStream s; readonly object w = new(); readonly Action<string, string> onMessage;
    volatile bool alive = true; ushort pid = 1;
    public bool Alive => alive;
    public Mqtt(string host, int port, string clientId, string user, string pass, string willTopic, string willMsg, Action<string, string> onMsg)
    {
        onMessage = onMsg;
        tcp = new TcpClient(); tcp.Connect(host, port); tcp.NoDelay = true; s = tcp.GetStream();
        var p = new List<byte>();
        Str(p, "MQTT"); p.Add(4);
        byte flags = 0x02 | 0x04 | 0x20; if (user != null) flags |= 0x80; if (pass != null) flags |= 0x40;
        p.Add(flags); p.Add(0); p.Add(60);
        Str(p, clientId); Str(p, willTopic); Str(p, willMsg); if (user != null) Str(p, user); if (pass != null) Str(p, pass);
        Send(0x10, p);
        s.ReadTimeout = 10000;
        var h = ReadPacket(out var body);
        if (h != 0x20 || body.Length < 2 || body[1] != 0) throw new Exception("connect refused (" + (body.Length > 1 ? body[1] : -1) + ")");
        s.ReadTimeout = Timeout.Infinite;
        new Thread(Reader) { IsBackground = true }.Start();
        new Thread(() => { while (alive) { Thread.Sleep(30000); try { Send(0xC0, new List<byte>()); } catch { alive = false; } } }) { IsBackground = true }.Start();
    }
    static void Str(List<byte> p, string v) { var b = Encoding.UTF8.GetBytes(v); p.Add((byte)(b.Length >> 8)); p.Add((byte)b.Length); p.AddRange(b); }
    void Send(byte header, List<byte> payload)
    {
        var buf = new List<byte> { header }; int len = payload.Count;
        do { byte d = (byte)(len % 128); len /= 128; if (len > 0) d |= 0x80; buf.Add(d); } while (len > 0);
        buf.AddRange(payload);
        lock (w) s.Write(buf.ToArray(), 0, buf.Count);
    }
    public bool Publish(string topic, string msg, bool retain)
    {
        try { var p = new List<byte>(); Str(p, topic); p.AddRange(Encoding.UTF8.GetBytes(msg)); Send((byte)(0x30 | (retain ? 1 : 0)), p); return true; }
        catch { alive = false; return false; }
    }
    public void Subscribe(params string[] topics)
    {
        var p = new List<byte> { (byte)(pid >> 8), (byte)pid }; pid++;
        foreach (var t in topics) { Str(p, t); p.Add(0); }
        Send(0x82, p);
    }
    byte ReadPacket(out byte[] body)
    {
        int h = s.ReadByte(); if (h < 0) throw new IOException("closed");
        int len = 0, mul = 1, d;
        do { d = s.ReadByte(); if (d < 0) throw new IOException("closed"); len += (d & 127) * mul; mul *= 128; } while ((d & 128) != 0);
        body = new byte[len]; int off = 0;
        while (off < len) { int n = s.Read(body, off, len - off); if (n <= 0) throw new IOException("closed"); off += n; }
        return (byte)h;
    }
    void Reader()
    {
        try {
            while (alive) {
                var h = ReadPacket(out var b);
                if ((h & 0xF0) != 0x30) continue;
                int tl = (b[0] << 8) | b[1]; var topic = Encoding.UTF8.GetString(b, 2, tl);
                int start = 2 + tl + (((h >> 1) & 3) > 0 ? 2 : 0);
                var msg = Encoding.UTF8.GetString(b, start, b.Length - start);
                ThreadPool.QueueUserWorkItem(_ => { try { onMessage(topic, msg); } catch { } });
            }
        } catch { }
        alive = false;
    }
    public void Close() { alive = false; try { tcp.Close(); } catch { } }
}
