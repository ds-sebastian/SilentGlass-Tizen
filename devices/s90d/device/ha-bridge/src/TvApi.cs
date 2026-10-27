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

// The TV underneath, as seen from the owner session: power key (uinput), inputs and their names (libsource-api,
// HDMI-CEC), settings (libsettings-api), volume, the app in front, Thread role.
static partial class Bridge
{
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
    // power, input and the app in front are vconf keys: their change notifications (on the same GLib loop) publish
    // at once, so the timed check only has to catch the rest (volume, names)
    delegate void VconfChangedCallback(IntPtr node, IntPtr userData);
    [DllImport("libvconf.so.0")] static extern int vconf_notify_key_changed(string key, VconfChangedCallback cb, IntPtr userData);
    static readonly string[] WatchedKeys = { "memory/pm/state", "memory/eden/source/current_source", "memory/appfw/current_fullscreen_appid" };
    static SourceListChangedCallback sourceListCallback;  // kept alive: native code holds the pointers
    static readonly List<VconfChangedCallback> keyCallbacks = new();
    static readonly int[] keyEvents = new int[4];  // per watched key + source list, logged once a minute when nonzero
    static readonly AutoResetEvent changed = new(false);
    static void StartSourceWatch() => new Thread(() => {
        try {
            sourceListCallback = (t, d) => { Interlocked.Increment(ref keyEvents[3]); changed.Set(); };
            register_sourcelist_changed_callback(sourceListCallback, IntPtr.Zero);
            for (int i = 0; i < WatchedKeys.Length; i++) {
                int k = i; VconfChangedCallback cb = (n, d) => { Interlocked.Increment(ref keyEvents[k]); changed.Set(); };
                keyCallbacks.Add(cb);
                if (vconf_notify_key_changed(WatchedKeys[k], cb, IntPtr.Zero) != 0) Log("watch " + WatchedKeys[k] + ": failed");
            }
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
    // While SilentGlass shows nothing full-screen (only a notification, or nothing at all) the foreground app id still
    // names it; report the app it last saw in front instead (its state file: ui=, backdrop=, fg=)
    public static string OnScreenApp(string fg, string state = null)
    {
        if (fg != Launcher) return fg;
        string V(string key) { var m = Regex.Match(state ?? Read("/tmp/silentglass-state.txt"), "(?m)^" + key + "=(.*)$"); return m.Success ? m.Groups[1].Value.Trim() : ""; }
        string other = V("fg");
        return V("ui") == "none" && V("backdrop") == "hidden" && Regex.IsMatch(other, @"^[\w.\-]+$") ? other : fg;
    }

}
