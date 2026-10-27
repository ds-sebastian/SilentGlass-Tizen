using System;
using System.Collections.Generic;
using System.Linq;

// Drives launcher/src/Screen.cs - the code that decides wallpaper vs bar vs nothing - against a simulated S90D:
// the TV viewer, SHIELD (HDMI 3) and Switch (HDMI 1) that sleep, wake over CEC and resync, Ambient, standby,
// Samsung Home, Home Assistant launches, the overlay's idle close and the OLED dim. Behaviour taken from the TV:
//  - the signal flag is only updated while the viewer runs: in standby it keeps its last value (seen: pm=4, signal=1);
//  - power-off puts SilentGlass in front (seen: fg:com.samsung.tv.bixby-promotion right after XF86PowerOff);
//  - the TV pauses the app whenever its main window is hidden; the foreground key names us only while our main
//    window is up (the overlay alone leaves it on the viewer);
//  - keys reach the overlay only if it was activated while we were the app in front; another app coming to the
//    front takes them.
// Unknowns are variants, all checked: Ambient keeps the flag stale or not; CEC One Touch Play relaunches the viewer
// or only switches the input; the TV launches the Home app at power-on or not.
// Exit code 0 when no scripted scenario and no random run breaks the rules at the bottom.
static class ScreenSim
{
    static int Main(string[] args)
    {
        int fuzzRuns = args.Length > 0 ? int.Parse(args[0]) : 1500;
        var found = new Dictionary<string, (int count, List<string> example)>();
        int runs = 0;
        foreach (bool ambientStale in new[] { true, false })
        foreach (bool cecLaunches in new[] { true, false })
        foreach (bool wakeHome in new[] { false, true })
        foreach (bool quietUnderUs in new[] { false, true })
        {
            string variant = $"ambient-flag-{(ambientStale ? "stale" : "live")}, cec-{(cecLaunches ? "relaunches-viewer" : "switches-only")}, power-on-{(wakeHome ? "launches-home" : "viewer-only")}, flag-notices-{(quietUnderUs ? "none-under-the-wallpaper" : "always")}";
            foreach (var (name, steps) in Scenarios()) { Run(variant, name, steps, ambientStale, cecLaunches, wakeHome, quietUnderUs, found); runs++; }
            var rnd = new Random(1234);
            for (int i = 0; i < fuzzRuns; i++) {
                var steps = Enumerable.Range(0, 24).Select(_ => Pool[rnd.Next(Pool.Length)]).ToArray();
                Run(variant, "random #" + i, steps, ambientStale, cecLaunches, wakeHome, quietUnderUs, found); runs++;
            }
        }
        Console.WriteLine($"{runs} runs.");
        if (found.Count == 0) { Console.WriteLine("No rule broken."); return 0; }
        foreach (var kv in found.OrderBy(k => k.Key)) {
            Console.WriteLine($"\nBROKEN ({kv.Value.count} runs): {kv.Key}");
            foreach (var l in kv.Value.example) Console.WriteLine("    " + l);
        }
        return 1;
    }

    static readonly string[] Pool = {
        "Home", "Home", "Home", "Select Watch", "Select Play", "Back", "Open Info", "Arrow", "Wait 2s", "Wait 10s", "Wait 50s", "Wait 6min",
        "SHIELD sleeps", "SHIELD wakes (its remote)", "SHIELD resyncs", "Switch sleeps", "Switch wakes (docked)", "Ambient",
        "Power off", "Power on", "HA input SHIELD", "HA input Switch", "HA Home" };

    static IEnumerable<(string, string[])> Scenarios() => new[] {
        ("Home over SHIELD, Home again", new[] { "Home", "Wait 2s", "Home", "Wait 2s" }),
        ("Home, Home, Home quickly (the bounce)", new[] { "Home", "Home", "Home", "Wait 3s" }),
        ("SHIELD asleep: Home, then SHIELD's remote", new[] { "SHIELD sleeps", "Wait 2s", "Home", "Wait 2s", "SHIELD wakes (its remote)", "Wait 10s" }),
        ("SHIELD asleep: Home, wallpaper dims, then SHIELD's remote", new[] { "SHIELD sleeps", "Wait 2s", "Home", "Wait 6min", "SHIELD wakes (its remote)", "Wait 10s" }),
        ("Home during a frame-rate switch", new[] { "SHIELD resyncs", "Wait 1s", "Home", "Wait 10s" }),
        ("Ambient, then Home with SHIELD on", new[] { "Ambient", "Wait 10s", "Home", "Wait 10s" }),
        ("Wallpaper up, SHIELD wakes, Home again", new[] { "SHIELD sleeps", "Home", "Wait 2s", "SHIELD wakes (its remote)", "Wait 2s", "Home", "Wait 2s" }),
        ("On Switch (asleep), Home, then SHIELD's remote", new[] { "SHIELD sleeps", "Switch sleeps", "HA input Switch", "Wait 2s", "Home", "Wait 2s", "SHIELD wakes (its remote)", "Wait 10s" }),
        ("TV on from standby, Home at once", new[] { "Power off", "Wait 50s", "Power on", "Wait 1s", "Home", "Wait 10s" }),
        ("Bar open at power-off, power on", new[] { "Home", "Power off", "Wait 50s", "Power on", "Wait 10s", "Home", "Wait 2s" }),
        ("Play, then Watch", new[] { "Home", "Select Play", "Wait 2s", "Home", "Select Watch", "Wait 2s" }),
        ("TV on with SHIELD off, wallpaper, SHIELD switched on", new[] { "SHIELD sleeps", "Power off", "Wait 10s", "Power on", "Wait 10s", "SHIELD sleeps", "Wait 10s", "Home", "Wait 10s", "SHIELD wakes (its remote)", "Wait 10s" }),
        ("HA switches input under the wallpaper", new[] { "SHIELD sleeps", "Home", "Wait 2s", "SHIELD wakes (its remote)", "HA input SHIELD", "Wait 5s" }),
    };

    static void Run(string variant, string name, string[] steps, bool ambientStale, bool cecLaunches, bool wakeHome, bool quietUnderUs,
                    Dictionary<string, (int, List<string>)> found)
    {
        var tv = new FakeTv { AmbientStale = ambientStale, CecLaunchesViewer = cecLaunches, WakeLaunchesHome = wakeHome, NoNoticeUnderWallpaper = quietUnderUs };
        tv.Boot();
        var done = new List<string>();
        foreach (var s in steps) {
            done.Add(s);
            tv.Do(s);
            foreach (var v in tv.TakeViolations()) {
                var example = new List<string> { "variant: " + variant, "scenario: " + name, "steps: " + string.Join(" > ", done) };
                example.AddRange(tv.Log.Skip(Math.Max(0, tv.Log.Count - 14)).Select(l => "  " + l));
                // keep the shortest example; scripted scenarios run first, so they win ties
                if (found.TryGetValue(v, out var f)) found[v] = (f.Item1 + 1, done.Count < StepCount(f.Item2) ? example : f.Item2);
                else found[v] = (1, example);
            }
        }
    }
    static int StepCount(List<string> ex) => ex.First(l => l.StartsWith("steps: ")).Split(" > ").Length;
}

sealed class FakeTv : IScreen
{
    public const string Viewer = Screen.Viewer, Self = Screen.Self, Csfs = Screen.SamsungHome, Art = "org.tizen.art-app";
    public bool AmbientStale, CecLaunchesViewer, WakeLaunchesHome, NoNoticeUnderWallpaper;
    public Screen App;
    public long T;
    public readonly List<string> Log = new();
    readonly List<(long at, long seq, Action a)> queue = new(); long seq;

    // the TV
    public bool Power = true, ShieldAwake = true, SwitchAwake = true;
    public string Front = Viewer, LastNonSelf = Viewer; public int Src = 15, SignalVconf = 1; public long ResyncUntil;
    string fgKey = Viewer;
    // SilentGlass' windows (as SilentGlass.cs / Backdrop.cs / Overlay.cs keep them)
    public bool Overlay, Focused, Bd, MainUp, Dimmed; public string Mode = ""; long lastActivity;

    public void Boot() { App = new Screen(this, () => T); App.Start(); }

    // ---- IScreen: what SilentGlass does with its windows ----
    public bool OverlayShown => Overlay;
    public bool BackdropShown => Bd;
    public bool SetupOpen => Overlay && Mode == "setup";
    public bool HomeKeyJustPressed => T - homeKeyAt < 300; long homeKeyAt = long.MinValue / 2;
    public long OverlayClosedAt => closedAt; long closedAt = long.MinValue / 2, openedAt = long.MinValue / 2;
    public readonly Random Rnd = new(7);
    public int ReadSignal() => SignalVconf;
    public int ReadSource() => Src;
    public string ReadForeground() => fgKey;
    public void ShowBackdrop() { if (Bd) return; Bd = true; MainUp = true; lastActivity = T; Fg(); }
    public void HideBackdrop()
    {
        if (!Bd && !MainUp) return;
        Bd = false; MainUp = false; Wake(); Fg();
        if (Front == Self) Later(30, () => App.Paused());  // the TV pauses us when our main window hides
    }
    public void OpenBar(bool keep)
    {
        if (!Overlay) { Overlay = true; openedAt = T; Focused = Front == Self && Power; }  // ShowOverlay: Show, Raise, Activate
        Mode = "bar"; lastActivity = T;
    }
    public void OpenPart(string part) { Mode = part; }
    public void CloseOverlay(string why)
    {
        if (!Overlay) return;
        // seen on the TV (event log, v41e): after the bar closes on a remote key, the Home app gets one or two stray
        // launch requests 0.7-2.2 s later
        if (why == "back" || why == null || why == "launch:close")  // null: SelectSource
            foreach (var at in new[] { 700 + Rnd.Next(300), 1400 + Rnd.Next(800) })
                Later(at, () => { if (Power && Front != Art && Front != Csfs) Amd(Self, null); });
        CloseOverlayNow(why);
    }
    void CloseOverlayNow(string why) { if (!Overlay) return; Overlay = false; closedAt = T; Focused = false; Mode = ""; if (why != null) Note(why); }
    public void Launch(string id) => Later(150, () => Amd(id, null));
    public bool ConnectSource(int t)
    {
        if (!Power || t != 15 && t != 13) return false;
        Later(300, () => SetSource(t)); return true;
    }
    public void Wake()
    {
        lastActivity = T;
        if (!Dimmed) return;
        Dimmed = false;
        if (Bd && !Overlay) OpenBar(false);
    }
    public void TvChanged() { }
    public void After(int ms, Action a) => Later(ms, a);
    public void Note(string what) => Log.Add($"{T / 1000.0,8:0.0}s app   {what}");

    // ---- the TV ----
    void Later(long ms, Action a) => queue.Add((T + ms, seq++, a));
    void Tv(string what) => Log.Add($"{T / 1000.0,8:0.0}s tv    {what}");
    bool DeviceUp => Src == 15 && ShieldAwake || Src == 13 && SwitchAwake;
    public bool InputHasPicture => Power && DeviceUp && T >= ResyncUntil;
    void UpdateSignal()
    {
        bool tracks = Power && (Front != Art || !AmbientStale);
        if (!tracks) return;
        int v = InputHasPicture ? 1 : 0;
        if (v != SignalVconf) {
            SignalVconf = v;
            // seen on the TV (v41a): a SHIELD switched on under the wallpaper went unseen - the flag changed, or its
            // notification never came
            if (!(NoNoticeUnderWallpaper && Front == Self && MainUp)) App.SignalFlapped();
        }
    }
    void Fg()
    {
        string k = !Power ? Self : Front == Self ? (MainUp ? Self : LastNonSelf) : Front;
        if (k == fgKey) return;
        fgKey = k; Later(20, () => App.Foreground(k));
    }
    void SetFront(string id)
    {
        if (id == Front) return;
        bool wasSelf = Front == Self;
        Front = id; Tv("front: " + id);
        if (id != Self) { LastNonSelf = id; Focused = false; if (wasSelf) Later(30, () => App.Paused()); }
        Fg();
    }
    void Amd(string id, string extra)
    {
        if (!Power) return;
        if (id != Self) { SetFront(id); return; }
        SetFront(Self);
        if (Overlay) Focused = true;  // we are the app in front again: our focusable window has the keys
        if (extra == null) App.Home("", "launch");
        else if (extra.StartsWith("src:")) App.SelectSource(int.Parse(extra.Substring(4)));
    }
    void SetSource(int t)
    {
        if (t == Src) return;
        Src = t; Tv("source: " + t);
        ResyncUntil = T + 1200;  // a new input takes a moment to sync
        SignalVconf = 0; App.SignalFlapped(); Later(5, () => App.SourceChanged());
    }

    // ---- what people do ----
    public void Do(string step)
    {
        Tv("> " + step);
        if (!step.StartsWith("Wait ")) homeCheck = false;  // a later press decides instead
        switch (step) {
            case "Home":
                if (!Power) break;
                // a Home press closes a working bar over the picture; otherwise it must leave a working bar
                homeCheck = true; homeAt = T; homeExpectClose = Overlay && Focused && !Bd && Front != Art && Front != Csfs;
                homeNoPicture = !homeExpectClose && !InputHasPicture && T - pictureSeenAt > 6000;
                // within 1.2 s of the bar opening, or 2.6 s of it closing, a press is taken for one of the TV's stray
                // launch requests (Screen.EchoMs / AfterCloseMs): either outcome is fine
                if (T - openedAt < Screen.EchoMs || T - closedAt < Screen.AfterCloseMs) homeCheck = false;
                if (Front == Art) { SetFront(Csfs); break; }  // Ambient's Home key opens Samsung Home
                if (Overlay && Focused) homeKeyAt = T + 140;  // the key reaches the focused bar just before its launch request
                Later(150, () => Amd(Self, null));
                // seen on the TV (event log, v41b): the remote's Home reaches the app twice, the second launch request
                // 0.4-1 s after the first
                Later(150 + 400 + Rnd.Next(600), () => { if (Power && Front != Art && Front != Csfs) Amd(Self, null); });
                break;
            case "HA Home": Later(150, () => Amd(Self, null)); break;
            case "HA input SHIELD": Later(150, () => Amd(Self, "src:15")); break;
            case "HA input Switch": Later(150, () => Amd(Self, "src:13")); break;
            case "Select Watch": Key(() => Activate(15)); break;
            case "Select Play": Key(() => Activate(13)); break;
            case "Back": Key(() => { if (Mode == "bar") App.BarBack(); else OpenBar(false); }); break;
            case "Open Info": Key(() => { if (Mode == "bar") Mode = "info"; }); break;
            case "Arrow": Key(() => { }); break;
            case "SHIELD sleeps": ShieldAwake = false; break;
            case "SHIELD wakes (its remote)": DeviceWakes(15, () => ShieldAwake = true, ShieldAwake); break;
            case "Switch sleeps": SwitchAwake = false; break;
            case "Switch wakes (docked)": DeviceWakes(13, () => SwitchAwake = true, SwitchAwake); break;
            case "SHIELD resyncs": if (Src == 15 && ShieldAwake) ResyncUntil = T + 1500; break;  // frame-rate / HDR switch
            case "Ambient": if (Power) SetFront(Art); break;
            case "Power off": if (!Power) break; Power = false; Focused = false; Front = Self; Fg(); Later(100, () => App.Standby()); break;  // memory/pm/state
            case "Power on": PowerOn(Src); break;
            default:
                if (step.StartsWith("Wait ")) { Advance(step.EndsWith("min") ? int.Parse(step[5..^3]) * 60000 : int.Parse(step[5..^1]) * 1000); return; }
                throw new ArgumentException(step);
        }
        Advance(1500);  // people take a moment between presses
    }
    void Activate(int t) { if (Mode != "bar") return; App.SelectSource(t); }
    // a key on the remote: to the overlay if it has focus, to our main window if that is in front, else to the TV
    void Key(Action onBar)
    {
        if (!Power) return;
        if (Overlay && Focused) { lastActivity = T; if (Dimmed) { Wake(); return; } onBar(); return; }
        if (Front == Self && MainUp) { lastActivity = T; if (Dimmed) { Wake(); return; } if (Bd) OpenBar(false); }
    }
    void DeviceWakes(int port, Action wake, bool already)
    {
        if (already) return;
        wake();
        if (!Power) { PowerOn(port); return; }  // One Touch Play turns the TV on
        if (Front == Art) { SetFront(Viewer); if (Src != port) SetSource(port); }
        else if (Src != port) { SetSource(port); if (CecLaunchesViewer) SetFront(Viewer); }
        ResyncUntil = Math.Max(ResyncUntil, T + 2500);  // a waking device takes a few seconds to send a picture
    }
    void PowerOn(int port)
    {
        if (Power) return;
        Power = true; Tv("power on");
        pictureSeenAt = T;  // just on: a picture is on its way (bar over black for a moment beats a wallpaper flash)
        if (Src != port) { Src = port; Later(5, () => App.SourceChanged()); }
        ShieldAwake = ShieldAwake || port == 15; ResyncUntil = T + 4000;  // the TV wakes SHIELD over CEC
        SignalVconf = 0; App.SignalFlapped();
        SetFront(Viewer);  // startup_appid
        if (WakeLaunchesHome) Later(600, () => Amd(Self, null));
    }

    // ---- time, SilentGlass' own timers, and the rules ----
    readonly Dictionary<string, long> since = new(); readonly List<string> violations = new(); bool homeCheck, homeExpectClose, homeNoPicture; long homeAt, pictureSeenAt;
    void Advance(long ms)
    {
        long end = T + ms;
        while (T < end) {
            T += 50;
            foreach (var e in queue.Where(q => q.at <= T).OrderBy(q => q.at).ThenBy(q => q.seq).ToList()) { queue.Remove(e); e.a(); }
            UpdateSignal();
            if (InputHasPicture) pictureSeenAt = T;
            // SilentGlass.cs / Overlay.cs / Backdrop.cs timers: the bar steps aside after 45 s over a picture; the
            // wallpaper dims after 5 min
            if (Overlay && !Bd && Mode != "setup" && T - lastActivity >= 45000) CloseOverlay("idle");
            if (Bd && !Dimmed && T - lastActivity >= 300000) { Dimmed = true; CloseOverlay("dimmed"); }
            Check();
        }
    }
    void Rule(string name, bool broken)
    {
        if (!broken) { since.Remove(name); return; }
        if (!since.TryGetValue(name, out long s)) { since[name] = T; return; }
        if (T - s == 2000) violations.Add(name);  // held for 2 s: not just a transition
    }
    void Check()
    {
        Rule("the wallpaper (or its dimmed black) covers a live picture on the current input", Power && Front == Self && MainUp && InputHasPicture && !SetupOpen);
        Rule("the bar is on screen but the remote's keys go elsewhere", Power && Overlay && !Focused);
        Rule("SilentGlass stays over another app (Ambient / Samsung Home)", Power && (Front == Art || Front == Csfs) && (Overlay || MainUp));
        if (homeCheck && T - homeAt == 3000) {
            homeCheck = false;
            if (Power && (homeExpectClose ? Overlay : !(Overlay && Focused))) violations.Add(homeExpectClose ? "Home over the bar did not close it" : "Home press: no working bar 3 s later");
            // the other way round: with no picture for a while, Home must bring the wallpaper up behind the bar
            else if (Power && homeNoPicture && !InputHasPicture && !(Bd && MainUp && Front == Self)) violations.Add("Home with no picture: no wallpaper");
        }
    }
    public IEnumerable<string> TakeViolations() { var v = violations.Distinct().ToList(); violations.Clear(); return v; }
}
