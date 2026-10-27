using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

// The TV underneath: settings (Samsung settings API by menu item id), volume, inputs and the picture on them, the app
// in front, launching apps and inputs, and the switches shared with the Home Assistant bridge (data directory files).
// Native callbacks may arrive off the UI thread; everything is posted onto it before touching views.
public sealed partial class SilentGlass
{
    // ---- native ----
    [return: MarshalAs(UnmanagedType.U1)][DllImport("/usr/lib/libsource-api.so")] static extern bool connect_source(int sourceType);
    delegate void SourceListChangedCallback(int eventType, IntPtr userData);
    delegate void VconfChangedCallback(IntPtr keyNode, IntPtr userData);
    [DllImport("/usr/lib/libsource-api.so")] static extern void register_sourcelist_changed_callback(SourceListChangedCallback callback, IntPtr userData);
    [DllImport("libvconf.so.0")] static extern int vconf_get_int(string key, out int value);
    [DllImport("libvconf.so.0")] static extern string vconf_get_str(string key);  // malloc'd; the marshaller frees it
    [DllImport("libvconf.so.0")] static extern int vconf_notify_key_changed(string key, VconfChangedCallback callback, IntPtr userData);
    [DllImport("libcapi-appfw-app-control.so.0")] static extern int app_control_create(out IntPtr app);
    [DllImport("libcapi-appfw-app-control.so.0")] static extern int app_control_set_app_id(IntPtr app, string id);
    [DllImport("libcapi-appfw-app-control.so.0")] static extern int app_control_set_operation(IntPtr app, string operation);
    [DllImport("libcapi-appfw-app-control.so.0")] static extern int app_control_add_extra_data(IntPtr app, string key, string value);
    [DllImport("libcapi-appfw-app-control.so.0")] static extern int app_control_send_launch_request(IntPtr app, IntPtr cb, IntPtr data);
    [DllImport("libcapi-appfw-app-control.so.0")] static extern int app_control_destroy(IntPtr app);
    [DllImport("libcapi-media-sound-manager-tv.so.0")] static extern int sound_manager_get_master_volume(out int value);
    [DllImport("libcapi-media-sound-manager-tv.so.0")] static extern int sound_manager_set_master_volume(int value);
    [DllImport("libcapi-media-sound-manager-tv.so.0")] static extern int sound_manager_get_master_mute(out int value);
    [DllImport("libcapi-media-sound-manager-tv.so.0")] static extern int sound_manager_set_master_mute(int value);
    [DllImport("libsettings-api.so")] static extern bool CSharpGetItemValue(string id, out string name, out string value);
    [DllImport("libsettings-api.so")] static extern bool CSharpSetItemValue(string id, string value);
    [DllImport("libsettings-api.so")] static extern bool CSharpGetItemEnable(string id, out bool enabled);
    [DllImport("libsettings-api.so")] static extern bool CSharpGetItemMinMaxStepValue(string id, out double min, out double max, out double step);
    [DllImport("libsettings-api.so")] static extern bool CSharpGetItemDataList(string id, System.Text.StringBuilder list);
    [DllImport("libsettings-api.so")] static extern bool CSharpGetItemDataListEnable(string id, string value, out bool enabled);
    [DllImport("libsettings-api.so")] static extern bool CSharpGetItemDataListVisible(string id, string value, out bool visible);

    SynchronizationContext ui;
    void OnUi(Action a) { Action safe = () => { try { a(); } catch (Exception e) { Note("error:" + e.GetType().Name); } }; var c = ui; if (c != null) c.Post(_ => safe(), null); else Post(safe); }

    // ---- settings: values are read on demand (each read is an IPC round trip), never on the key path ----
    static readonly string[] SettingIds = { "brightness", "contrast", "sleeptimer", "external-sound-mode", "soundoutput", "picturemode", "game-mode-auto", "pixel-shift-menu" };
    readonly Dictionary<string, string> settingValues = new(), settingNames = new();
    readonly Dictionary<string, bool> settingEnabled = new();
    readonly Dictionary<string, (double min, double max, double step)> ranges = new();
    int volume = -1, mute = -1;
    void RefreshSettings()
    {
        volume = sound_manager_get_master_volume(out var v) == 0 ? v : -1;
        mute = sound_manager_get_master_mute(out var m) == 0 ? m : -1;
        foreach (var id in new[] { "brightness", "contrast" })
            try { if (CSharpGetItemMinMaxStepValue(id, out var mn, out var mx, out var st) && st > 0 && mx > mn) ranges[id] = (mn, mx, st); } catch { }
        foreach (var id in SettingIds)
            try {
                if (CSharpGetItemValue(id, out var name, out var value) && !string.IsNullOrEmpty(value)) { settingValues[id] = value; settingNames[id] = name; }
                else { settingValues.Remove(id); settingNames.Remove(id); }
                settingEnabled[id] = CSharpGetItemEnable(id, out var enabled) && enabled;
            } catch { settingValues.Remove(id); settingNames.Remove(id); settingEnabled[id] = false; }
    }
    string Setting(string id) => settingValues.TryGetValue(id, out var v) ? v : "";
    string SettingName(string id) => settingNames.TryGetValue(id, out var v) ? v : "–";
    bool CanEdit(string id) => settingEnabled.TryGetValue(id, out var e) && e && settingValues.ContainsKey(id);
    (double min, double max, double step) Range(string id) => ranges.TryGetValue(id, out var r) ? r : (0, 50, 1);
    float Level(string id) { var (mn, mx, _) = Range(id); return mx > mn && double.TryParse(Setting(id), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? (float)((n - mn) / (mx - mn)) : -1; }
    // options the TV offers for the current input (enabled and visible); numeric values only (the native list can end with garbage)
    List<string> Options(string id, bool visibleOnly = true)
    {
        var result = new List<string>(); var sb = new System.Text.StringBuilder(32768);
        try { if (!CSharpGetItemDataList(id, sb)) return result; } catch { return result; }
        foreach (var entry in sb.ToString().Split(';')) {
            int c = entry.IndexOf(','); string v = c < 0 ? entry : entry.Substring(0, c);
            if (v.Length == 0 || v.Length > 9 || !int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out _)) continue;
            try { if (CSharpGetItemDataListEnable(id, v, out var en) && en && (!visibleOnly || CSharpGetItemDataListVisible(id, v, out var vis) && vis) && !result.Contains(v)) result.Add(v); } catch { }
        }
        return result;
    }
    bool SetSetting(string id, string value)
    {
        if (!CanEdit(id)) { Note("setting:" + id + ":unavailable"); return false; }
        bool ok = false; try { ok = CSharpSetItemValue(id, value); } catch { }
        RefreshSettings(); ok &= Setting(id) == value;
        Note("setting:" + id + ":" + value + ":" + ok); return ok;
    }
    // Left / Right step through the TV's own option list; Game Mode's item is marked hidden here though all its values apply
    void CycleSetting(string id, int dir)
    {
        RefreshSettings(); var options = Options(id, id != "game-mode-auto");
        if (options.Count < 2) return;
        int i = options.IndexOf(Setting(id)); SetSetting(id, options[((i < 0 ? 0 : i) + dir + options.Count) % options.Count]);
    }
    void ChangeLevel(string id, int dir)
    {
        RefreshSettings(); var (mn, mx, st) = Range(id);
        if (double.TryParse(Setting(id), NumberStyles.Float, CultureInfo.InvariantCulture, out double n)) SetSetting(id, Math.Clamp(n + dir * st, mn, mx).ToString(CultureInfo.InvariantCulture));
    }
    void ChangeVolume(int dir) { if (sound_manager_get_master_volume(out int n) == 0) sound_manager_set_master_volume(Math.Clamp(n + dir, 0, 100)); RefreshSettings(); }
    void ToggleMute() { if (sound_manager_get_master_mute(out int n) == 0) sound_manager_set_master_mute(n == 1 ? 0 : 1); RefreshSettings(); }

    // ---- inputs: Samsung's source list (names, connected) and the current source, pushed by the TV.
    // powerState is never used: devices behind the soundbar report Off. The only trusted power signal is the picture
    // on the current input (memory/tv-viewer/mainscreen_signal_state). ----
    const string SignalKey = "memory/tv-viewer/mainscreen_signal_state", SourceKey = "memory/system/source_type", ForegroundKey = "memory/appfw/current_fullscreen_appid";
    const string PmKey = "memory/pm/state";  // 1 on, 2 dim, 3 screen off, 4 standby
    List<InfoStats.InputSource> inputs = new();
    int watchPort = 15, playPort = 13;
    // what is on screen (wallpaper / bar / nothing) is decided in Screen.cs; it keeps the current input and the
    // settled signal flag
    Screen screen;
    int lastSource => screen.Source;
    bool HasPicture => screen.HasPicture;
    SourceListChangedCallback sourceListCallback; VconfChangedCallback sourceTypeCallback, signalCallback, foregroundCallback, pmCallback;  // kept alive: native code holds them
    void WatchTv()
    {
        screen = new Screen(this, () => Environment.TickCount64);
        try { sourceListCallback = (t, d) => OnUi(() => OnInputsChanged(true)); register_sourcelist_changed_callback(sourceListCallback, IntPtr.Zero); } catch { }
        try { sourceTypeCallback = (n, d) => OnUi(screen.SourceChanged); vconf_notify_key_changed(SourceKey, sourceTypeCallback, IntPtr.Zero); } catch { }
        try { signalCallback = (n, d) => OnUi(screen.SignalFlapped); vconf_notify_key_changed(SignalKey, signalCallback, IntPtr.Zero); } catch { }
        try { foregroundCallback = (n, d) => OnUi(() => screen.Foreground(Foreground())); vconf_notify_key_changed(ForegroundKey, foregroundCallback, IntPtr.Zero); } catch { }
        try {
            pmCallback = (n, d) => OnUi(() => { if (vconf_get_int(PmKey, out int v) == 0 && v >= 3) screen.Standby(); });
            vconf_notify_key_changed(PmKey, pmCallback, IntPtr.Zero);
        } catch { }
        RefreshInputs(); screen.Start(); ResolvePorts();
    }
    void RefreshInputs() => inputs = InfoStats.Sources();
    void OnInputsChanged(bool announce)
    {
        var before = inputs; RefreshInputs(); ResolvePorts();
        if (announce)
            foreach (var now in inputs) {
                var was = before.Find(i => i.Type == now.Type);
                if (was != null && was.Known && now.Known && was.Connected != now.Connected) { Toast(now.Name + (now.Connected ? " connected" : " unplugged")); break; }
            }
        OnTvChanged();
    }
    // Watch / Play: the input chosen in Info, or (Automatic) the input the TV calls "Shield" / "Switch", else HDMI 3 / HDMI 1
    void ResolvePorts()
    {
        watchPort = prefs.Watch != 0 ? prefs.Watch : PortNamed("shield", 15);
        playPort = prefs.Play != 0 ? prefs.Play : PortNamed("switch", 13);
    }
    int PortNamed(string word, int fallback) { var m = inputs.Find(i => i.Known && Logic.IsHdmi(i.Type) && i.Name.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0); return m != null ? m.Type : fallback; }
    string InputName(int type) { var i = inputs.Find(x => x.Type == type); return i != null && i.Known && !string.IsNullOrWhiteSpace(i.Name) ? i.Name : type == 0 ? "Live TV" : "HDMI " + (type - 12); }
    string PortChoice(int chosen, int resolved) => chosen == 0 ? "Auto · " + InputName(resolved) : InputName(chosen);
    void CyclePort(bool watch, int dir)
    {
        if (watch) prefs.Watch = Logic.NextPort(prefs.Watch, playPort, dir); else prefs.Play = Logic.NextPort(prefs.Play, watchPort, dir);
        SavePrefs(); ResolvePorts();
    }
    void SelectSource(int type) => screen.SelectSource(type);
    string Foreground() { try { return vconf_get_str(ForegroundKey) ?? ""; } catch { return ""; } }

    void Launch(string id, string menuItem = null)
    {
        if (app_control_create(out IntPtr c) != 0) return;
        try {
            int r = app_control_set_app_id(c, id);
            if (r == 0) r = app_control_set_operation(c, "http://tizen.org/appcontrol/operation/default");
            if (r == 0 && id == "org.tizen.menu") {
                if (menuItem != null) { r = app_control_add_extra_data(c, "menuitem", menuItem); if (r == 0) r = app_control_add_extra_data(c, "launchoption", "pressed"); }
                if (r == 0) r = app_control_add_extra_data(c, "__K_EX_FORCE_BOOST", "enable");
                if (r == 0) r = app_control_add_extra_data(c, "__K_SPLASH_SCREEN", "LA_UI_VR_TRANSPARENT");
            }
            if (r == 0) r = app_control_send_launch_request(c, IntPtr.Zero, IntPtr.Zero);
            Note("launch:" + id + (menuItem == null ? "" : ":" + menuItem) + ":" + r);
        } finally { app_control_destroy(c); }
    }
    void OpenApp(string id) { CloseOverlay(null); HideBackdrop(); Launch(id); }
    void OpenTvMenu(string item) { CloseOverlay(null); HideBackdrop(); Launch("org.tizen.menu", item); }

    // ---- switches shared through the data directory (the bridge and the owner's systemd units read them) ----
    static string DataFile(string name) => Path.Combine(Tizen.Applications.Application.Current.DirectoryInfo.Data, name);
    const string ThreadUnits = "/opt/usr/home/owner/.config/systemd/user/quiet-otbr-switch.path";
    static bool ThreadInstalled => File.Exists(ThreadUnits);
    static bool ThreadOn => !File.Exists(DataFile("thread-off"));
    // switched from the phone page or Home Assistant (the bridge writes thread-off / thread-switch)
    static bool NoticesOn => !File.Exists(DataFile("notify-off"));
    void SetNotices(bool on) { try { if (on) File.Delete(DataFile("notify-off")); else File.WriteAllText(DataFile("notify-off"), ""); } catch { } }

    // ---- preferences ----
    Logic.Prefs prefs = new();
    void LoadPrefs()
    {
        string text = null;
        foreach (var f in new[] { "silentglass-prefs.txt", "quiet-home-prefs.txt" }) try { text = File.ReadAllText(DataFile(f)); break; } catch { }
        prefs = Logic.ParsePrefs(text);
        // the wallpaper's location comes from the phone setup page (written by the bridge)
        try { var c = Logic.ParseConf(File.ReadAllText(DataFile("location.txt"))); prefs.Lat = Logic.Num(c, "lat", prefs.Lat); prefs.Lon = Logic.Num(c, "lon", prefs.Lon); } catch { }
    }
    void SavePrefs() { try { File.WriteAllText(DataFile("silentglass-prefs.txt"), Logic.FormatPrefs(prefs)); File.Delete(DataFile("quiet-home-prefs.txt")); } catch { } }
}

// Screen.cs talks to the windows through this
public sealed partial class SilentGlass : IScreen
{
    bool IScreen.OverlayShown => overlayShown;
    bool IScreen.BackdropShown => backdropShown;
    bool IScreen.SetupOpen => overlayShown && mode == Mode.Setup;
    long IScreen.OverlayClosedAt => overlayClosedAt;
    int IScreen.ReadSignal() { try { return vconf_get_int(SignalKey, out int v) == 0 ? v : -1; } catch { return -1; } }
    int IScreen.ReadSource() { try { return vconf_get_int(SourceKey, out int n) == 0 ? n : -1; } catch { return -1; } }
    string IScreen.ReadForeground() => Foreground();
    void IScreen.ShowBackdrop() => ShowBackdrop();
    void IScreen.HideBackdrop() => HideBackdrop();
    void IScreen.OpenBar(bool keepFocus) => OpenBar(keepFocus);
    void IScreen.OpenPart(string part)
    {
        switch (part) {
            case "controls": OpenControls(); break;
            case "info": OpenInfo(); break;
            case "apps": OpenApps(); break;
            case "inputs": OpenInputs(); break;
            case "setup": OpenInfo(); OpenSetup(); break;
        }
    }
    void IScreen.CloseOverlay(string why) => CloseOverlay(why);
    void IScreen.Launch(string appId) => Launch(appId);
    bool IScreen.ConnectSource(int type) { try { return connect_source(type); } catch { return false; } }
    void IScreen.Wake() => Wake();
    void IScreen.TvChanged() => OnTvChanged();
    readonly HashSet<Tizen.NUI.Timer> afterTimers = new();  // referenced until they fire
    void IScreen.After(int ms, Action action)
    {
        var t = new Tizen.NUI.Timer((uint)Math.Max(1, ms)); afterTimers.Add(t);
        t.Tick += (s, e) => { afterTimers.Remove(t); try { action(); } catch (Exception ex) { Note("after-error:" + ex.GetType().Name); } return false; };
        t.Start();
    }
    void IScreen.Note(string what) => Note(what);
}
