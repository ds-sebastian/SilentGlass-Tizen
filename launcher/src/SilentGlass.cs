using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Tizen.Applications;
using Tizen.NUI;

// SilentGlass: the S90D's Home button, SHIELD-first, as overlays only (tvOS 26 style). There are no full-screen pages.
//  Backdrop.cs  the app's own window: the wallpaper + clock, shown only when there is no picture to draw over
//  Overlay.cs   the bar (Watch, Play, Inputs, Controls, Apps, Info) in a partial notification window above the TV
//               viewer, which keeps playing untouched; Menus.cs (Inputs, Apps), Panels.cs (Controls, Info),
//               Setup.cs (phone setup sheet) all live in that window
//  Notice.cs    notifications from Home Assistant and short toasts, in their own non-focusable window
//  Tv.cs        the TV underneath (settings, inputs, picture, apps); Logic.cs and Qr.cs are NUI-free and tested off the TV
// The package stays com.samsung.tv.bixby-promotion: that is the app the TV's Home button opens.
public sealed partial class SilentGlass : NUIApplication
{
    public const string SelfId = "com.samsung.tv.bixby-promotion", Viewer = "org.tizen.tv-viewer", ModVersion = "v42d";
    const string Res = "/opt/usr/apps/com.samsung.tv.bixby-promotion/res/quiet-home/";
    List<LibraryApp> library = new();
    Tizen.NUI.Timer minuteTimer;

    protected override void OnCreate()
    {
        base.OnCreate();
        ui = System.Threading.SynchronizationContext.Current;
        try { FontClient.Instance.AddCustomFontDirectory(Res + "fonts"); } catch { }
        try { library = AppLibrary.Load(Res + "apps.json").FindAll(a => !a.Utility); } catch { }
        if (!library.Exists(a => a.Id == "com.samsung.tv.csfs"))
            library.Add(new LibraryApp { Name = "Samsung Home", Id = "com.samsung.tv.csfs", Asset = "tile-samsung-home.svg", Fit = "cover", Background = "#1C2638", Width = 640, Height = 360 });
        try { SetRenderRefreshRate(1); } catch { }  // DALi draws every 2nd vsync here by default: 60 fps
        LoadPrefs();
        BuildBackdrop();  // hidden until there is nothing to draw over; never flashes over the picture
        EnsureOverlay();  // created while the app's main window is still up (as v31 did), so it can take focus later
        WatchTv(); WatchData();
        minuteTimer = new Tizen.NUI.Timer(30000);
        minuteTimer.Tick += (s, e) => { MinuteTick(); return true; };
        minuteTimer.Start();
        // started by the bridge only to show a notification: stay hidden, the picture stays in front
        if (NoticePending()) { HideBackdrop(); TakeNotice("start"); }
        Note("created");
        var privacy = new Tizen.NUI.Timer(8000); privacy.Tick += (s, e) => { PrivacyStatus(); Note("privacy:" + privacyGaps); return false; }; privacy.Start(); privacyTimer = privacy;
    }
    Tizen.NUI.Timer privacyTimer;
    // AMD resumes us for every launch request; what to show is decided in OnAppControlReceived
    protected override void OnResume() { base.OnResume(); Note("resumed fg=" + Foreground()); }
    // The TV also pauses us whenever our main window is hidden - i.e. whenever only the overlay is up - so a pause
    // alone must not close it. Another app coming to the front does (OnForegroundChanged); the backdrop never stays
    // behind anything.
    protected override void OnPause() { base.OnPause(); screen.Paused(); Note("paused"); WriteState("paused"); }

    protected override void OnAppControlReceived(AppControlReceivedEventArgs e)
    {
        base.OnAppControlReceived(e);
        var x = e.ReceivedAppControl.ExtraData;
        string caller = null; try { caller = e.ReceivedAppControl.CallerApplicationId; } catch { }
        string keys = ""; try { keys = string.Join(",", x.GetKeys().Where(n => !n.StartsWith("__AUL_") && !n.StartsWith("__APP_SVC_"))); } catch { }
        Note("request from " + (string.IsNullOrEmpty(caller) ? "system" : caller) + (keys.Length > 0 ? " [" + keys + "]" : ""));
        // from the Home Assistant bridge: a notification is waiting / switch input like a press on its bar item
        if (x.TryGet("sg_notice", out string _)) { TakeNotice("launch"); return; }
        if (x.TryGet("sg_source", out string src) && int.TryParse(src, out int t) && inputs.Exists(i => i.Type == t)) { SelectSource(t); return; }
        // from the bridge, QuickLaunch (Samsung's quick-settings key) or Home Assistant: open one part directly
        // (QuickLaunch builds before v40 send quiet_page=settings: the installed copy is not replaced by launcher updates)
        if (!x.TryGet("sg_open", out string open) && x.TryGet("quiet_page", out string page)) open = page == "settings" ? "controls" : page == "home" ? "" : page;
        Home(open ?? "", "launch");
    }

    // The Home button: Screen.cs decides (bar over the picture, the wallpaper behind it, or picture first from Ambient)
    void Home(string open, string why) => screen.Home(open, why);

    // ---- keys: both windows route here ----
    string lastKey = ""; long lastKeyAt, keyCount, keyTicksMax;
    void OnKey(object sender, Window.KeyEventArgs e)
    {
        if (e.Key == null) return;
        string k = e.Key.KeyPressedName; long now = Environment.TickCount64;
        if (e.Key.State == Key.StateType.Up) { KeyReleased(k); return; }
        if (e.Key.State != Key.StateType.Down) return;
        held[k] = now; if (k == "XF86Home") homeKeyAt = now;
        bool arrow = k == "Up" || k == "Down" || k == "Left" || k == "Right";
        if (k == lastKey && now - lastKeyAt < (arrow ? 90 : 300)) return;  // drop auto-repeat floods only
        lastKey = k; lastKeyAt = now; lastActivity = now;
        if (dimmed) { Wake(); return; }  // the first press only wakes
        long t0 = Stopwatch.GetTimestamp();
        try { HandleKey(k, arrow); } catch (Exception ex) { Note("key-error:" + ex.GetType().Name); }
        keyCount++; keyTicksMax = Math.Max(keyTicksMax, Stopwatch.GetTimestamp() - t0);
        Note("key " + k);  // coalesced: the state file is written later by a timer, never here
    }
    // ---- hide on release: the remote sends press and release separately; if our windows hide on the press, the TV
    // gets the release with nothing of ours up and relaunches the Home app (the stray launch requests that reopened
    // the bar). So whatever a key hides waits until that key is released (at most 1.5 s). ----
    readonly Dictionary<string, long> held = new();
    readonly List<Action> afterRelease = new();
    Tizen.NUI.Timer releaseTimer;
    long homeKeyAt = long.MinValue / 2;
    void WhenReleased(Action a)
    {
        long now = Environment.TickCount64;
        foreach (var k in held.Where(h => now - h.Value > 1500).Select(h => h.Key).ToList()) held.Remove(k);
        if (held.Count == 0) { a(); return; }
        afterRelease.Add(a);
        if (releaseTimer == null) { releaseTimer = new Tizen.NUI.Timer(1500); releaseTimer.Tick += (s, e) => { held.Clear(); RunReleased(); return false; }; }
        releaseTimer.Stop(); releaseTimer.Start();
    }
    void KeyReleased(string k) { if (held.Remove(k) && held.Count == 0 && afterRelease.Count > 0) { Note("released " + k); RunReleased(); } }
    void RunReleased()
    {
        releaseTimer?.Stop();
        var todo = afterRelease.ToArray(); afterRelease.Clear();
        foreach (var a in todo) try { a(); } catch (Exception ex) { Note("release-error:" + ex.GetType().Name); }
    }
    static bool IsBack(string k) => k == "XF86Back" || k == "Back" || k == "Escape" || k == "XF86Exit";
    static bool IsSelect(string k) => k == "Return" || k == "KP_Enter";
    void HandleKey(string k, bool arrow)
    {
        if (k == "XF86Home") return;  // Home arrives as a launch request (OnAppControlReceived)
        if (k == "XF86SysMenu" || k == "XF86More") { if (!overlayShown) OpenBar(false); OpenControls(); return; }
        if (!overlayShown) { if (backdropShown) OpenBar(false); return; }
        OverlayKey(k, arrow);
    }

    void MinuteTick()
    {
        if (backdropShown) { UpdateClock(); CheckWallpaper(); IdleTick(); }
        if (overlayShown && mode == Mode.Bar && PortsChanged()) OpenBar(true);  // a renamed input fires no event
    }

    // ---- diagnostics for the Pi-side checks and the bridge's app sensor: /tmp/silentglass-state.txt. Coalesced
    // (one write per 600 ms at most, never on the key path). ----
    const string StateFile = "/tmp/silentglass-state.txt";
    Tizen.NUI.Timer stateTimer; string activity = "";
    readonly Queue<string> trail = new();  // the last few events, for diagnosing on the TV
    void Note(string what)
    {
        activity = what;
        string line = DateTime.Now.ToString("HH:mm:ss.f") + " " + what;
        trail.Enqueue(line); while (trail.Count > 16) trail.Dequeue();
        unlogged.Add(line);
        if (stateTimer == null) { stateTimer = new Tizen.NUI.Timer(600); stateTimer.Tick += (s, e) => { WriteState(activity); return false; }; }
        if (!stateTimer.IsRunning()) stateTimer.Start();
    }
    // every event, appended with the state write (never on the key path); /tmp, so it ends at reboot; kept under 256 KB
    const string EventLog = "/tmp/silentglass-events.log";
    readonly List<string> unlogged = new();
    void FlushEvents()
    {
        if (unlogged.Count == 0) return;
        try {
            var f = new FileInfo(EventLog);
            if (f.Exists && f.Length > 256 * 1024) { var keep = File.ReadAllLines(EventLog); File.WriteAllLines(EventLog, keep[(keep.Length / 2)..]); }
            File.AppendAllLines(EventLog, unlogged);
        } catch { }
        unlogged.Clear();
    }
    void WriteState(string what)
    {
        stateTimer?.Stop(); FlushEvents();
        var b = new System.Text.StringBuilder(DateTime.UtcNow.ToString("o"));
        void Put(string key, object value) => b.Append('\n').Append(key).Append('=').Append(value);
        Put("version", ModVersion); Put("activity", what);
        // read by the bridge: while SilentGlass shows nothing full-screen, the app on screen is `fg`
        Put("ui", overlayShown ? mode.ToString().ToLowerInvariant() : "none"); Put("backdrop", backdropShown ? "shown" : "hidden"); Put("fg", screen.LastOther);
        Put("focus", FocusName()); Put("key", lastKey); Put("key_count", keyCount);
        Put("key_ms_max", (keyTicksMax * 1000.0 / Stopwatch.Frequency).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
        Put("source", lastSource); Put("signal", screen.Signal); Put("ports", watchPort + "," + playPort + "(" + prefs.Watch + "," + prefs.Play + ")");
        Put("inputs", string.Join(";", inputs.ConvertAll(i => i.Type + "," + i.Name + "," + (i.Known ? i.Connected ? "1" : "0" : "?"))));
        Put("prefs", Logic.FormatPrefs(prefs).Trim().Replace('\n', ','));
        Put("brightness", Setting("brightness")); Put("contrast", Setting("contrast")); Put("picturemode", Setting("picturemode"));
        Put("game", Setting("game-mode-auto")); Put("volume", volume);
        Put("trail", string.Join(" | ", trail));
        Put("privacy_gaps", privacyGaps);
        Put("notice", noticeState); Put("setup", setupState); Put("dimmed", dimmed); Put("library", library.Count);
        b.Append('\n');
        try { File.WriteAllText(StateFile, b.ToString()); } catch { }
    }
    public static void Main(string[] args) { using var app = new SilentGlass(); app.Run(args); }
}
