using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;

// Panels on the right, in place of the bar: Controls (picture and sound; translucent, so the picture shows how a
// change looks) and Info (how the TV is doing, then SilentGlass's own settings; near-opaque for reading).
// One row model for both: Up/Down move, Left/Right adjust or cycle, Select steps, toggles or opens, Back -> bar.
public sealed partial class SilentGlass
{
    enum RowKind { Summary, Status, Level, Value, Switch, Action }
    static bool Passive(Row r) => r.Kind is RowKind.Status or RowKind.Summary;  // shown, never focused
    sealed class Row
    {
        public RowKind Kind; public string Name, Glyph;
        public Func<string> Value; public Func<float> Amount; public Func<bool> On; public Func<(string text, Color tone)> Read; public Func<(string title, string sub)> Sum;
        public Action<int> Adjust; public Action Select;
        public View Box, Track, Fill, Light; public ImageView Icon; public TextLabel Label, Val; public Switch Sw; public Color Tone;
    }
    readonly List<Row> rows = new();
    int rowSel;
    Tizen.NUI.Timer panelTimer;
    const int PanelWx = 1176;  // the panel window's left edge (CcX - 60)
    const int CcX = 1236, CcW = 624, CcR = 44, SliderH = 88, ValueH = 64, StatusH = 56, SummaryH = 84, RuleH = 25, HeadH = 70;
    int RowH(Row r) => r == null ? RuleH : r.Kind switch { RowKind.Level => SliderH, RowKind.Status => StatusH, RowKind.Summary => SummaryH, _ => ValueH };

    static Row Level(string glyph, string name, Func<string> value, Func<float> amount, Action<int> adjust, Action select = null) =>
        new() { Kind = RowKind.Level, Glyph = glyph, Name = name, Value = value, Amount = amount, Adjust = adjust, Select = select };
    static Row Choice(string glyph, string name, Func<string> value, Action<int> adjust) =>
        new() { Kind = RowKind.Value, Glyph = glyph, Name = name, Value = value, Adjust = adjust, Select = () => adjust(1) };
    static Row Toggle(string glyph, string name, Func<bool> on, Action<bool> set) =>
        new() { Kind = RowKind.Switch, Glyph = glyph, Name = name, On = on, Adjust = d => set(d > 0), Select = () => set(!on()) };
    static Row Open(string glyph, string name, Action select, Func<string> value = null) =>
        new() { Kind = RowKind.Action, Glyph = glyph, Name = name, Select = select, Value = value };
    static Row Status(string name, Func<(string text, Color tone)> read) => new() { Kind = RowKind.Status, Name = name, Read = read };

    // ---- Controls: what's used while watching. Contrast, Pixel Shift and the TV's own menus are one level down, in
    // More (Back returns to Controls). ----
    void OpenControls()
    {
        RefreshSettings();
        string Output() => Setting("soundoutput") == "2" ? "Soundbar" : Setting("soundoutput") == "0" ? "TV Speaker" : SettingName("soundoutput");
        BuildPanel(Mode.Controls, null, false, new List<Row> {
            Level("sun", "Brightness", () => Setting("brightness"), () => Level("brightness"), d => ChangeLevel("brightness", d)),
            Level("speaker", "Volume", () => mute == 1 ? "Muted" : volume < 0 ? "–" : volume.ToString(), () => volume < 0 ? -1 : volume / 100f, ChangeVolume, ToggleMute),
            null,
            Choice("picture", "Picture Mode", () => SettingName("picturemode"), d => CycleSetting("picturemode", d)),
            Choice("waveform", "Sound Mode", () => SettingName("external-sound-mode"), d => CycleSetting("external-sound-mode", d)),
            Choice("soundbar", "Sound Output", Output, d => SetSetting("soundoutput", Setting("soundoutput") == "0" ? "2" : "0")),
            Choice("gamecontroller", "Game Mode", () => Shared.GameName(SettingName("game-mode-auto")), d => CycleSetting("game-mode-auto", d)),
            Choice("moon", "Sleep Timer", SleepValue, d => CycleSetting("sleeptimer", d)),
            null,
            Open("artframe", "Ambient", () => OpenApp(Shared.Art)),
            Open("slidersmore", "More", OpenMore),
        });
    }
    void OpenMore()
    {
        BuildPanel(Mode.More, "More", false, new List<Row> {
            Level("contrast", "Contrast", () => Setting("contrast"), () => Level("contrast"), d => ChangeLevel("contrast", d)),
            Toggle("pixelshift", "Pixel Shift", () => Setting("pixel-shift-menu") == "1", on => SetSetting("pixel-shift-menu", on ? "1" : "0")),
            null,
            Open("bluetooth", "Bluetooth", () => OpenTvMenu("add-bluetooth-keyboard")),
            Open("network", "Network", () => OpenTvMenu("networksettings")),
            Open("gear", "TV Settings", () => OpenTvMenu(null)),
            Open("apps", "Developer Mode", () => OpenApp(Shared.SamsungApps), () => "1 2 3 4 5"),
        });
    }
    // the TV reports only the length the timer was set to; started from here, the time left
    string SleepValue() => Logic.SleepText(Setting("sleeptimer"), SettingName("sleeptimer"),
        sleepSet.value == Setting("sleeptimer") ? (Environment.TickCount64 - sleepSet.at) / 60000.0 : -1);

    // ---- Info: one line for how the TV is doing (a row for each thing that needs a look; the full list is in Status
    // details), then SilentGlass's own settings ----
    List<(string name, Func<(string text, Color tone)> read)> StatusList()
    {
        var list = new List<(string, Func<(string, Color)>)> { ("Privacy", PrivacyStatus), ("Network", NetworkStatus) };
        if (File.Exists(Shared.BridgeUnit)) list.Add(("Internet", InternetStatus));
        if (ThreadInstalled) list.Add(("Thread", ThreadStatus));
        list.Add(("Home Assistant", BridgeStatus));
        list.Add(("This TV", TvStatus));
        return list;
    }
    (string, Color) InternetStatus() => Shared.InternetStatus(DataDir) switch {
        "Blocked" => ("Blocked · home network only", Live), "Allowed" => ("Connected", Live), var s => (s, Amber) };
    string infoIssues = "";
    void OpenInfo()
    {
        var status = StatusList().Select(s => (s.name, s.read, now: s.read())).ToList();
        var issues = status.Where(s => s.now.Item2 == Amber).ToList();
        infoIssues = string.Join(",", issues.Select(s => s.name));
        var list = new List<Row> { new() { Kind = RowKind.Summary, Name = "Summary", Sum = Summary, Tone = issues.Count == 0 ? Live : Amber } };
        foreach (var s in issues) list.Add(Status(s.name, s.read));
        list.Add(null);
        list.Add(Open("tv", "Shortcuts", OpenShortcuts, () => Shortcuts.Count() is int n && n > 0 ? n + " on the bar" : "None"));
        list.Add(Toggle("apps", "Apps in the bar", () => !prefs.HideApps, on => { prefs.HideApps = !on; SavePrefs(); }));
        list.Add(Choice("moon", "Dim after", () => Logic.DimName(prefs.Dim), d => { prefs.Dim = Logic.NextDim(prefs.Dim, d); SavePrefs(); }));
        if (PhotoWall) list.Add(Choice("picture", "Wallpaper", () => Logic.WallName(prefs.Wall), d => { prefs.Wall = Logic.NextWall(prefs.Wall, d); SavePrefs(); CheckWallpaper(); }));
        list.Add(Toggle("bell", "Notifications", () => NoticesOn, SetNotices));
        if (File.Exists(Shared.BridgeUnit)) list.Add(Toggle("network", "Home network only", () => !Shared.InternetWanted(DataDir), on => Shared.SetInternet(DataDir, !on)));
        list.Add(null);
        list.Add(Open("info", "Status details", OpenStatus));
        // everything else (Home Assistant, the Thread border router, the wallpaper's location) is set from a phone
        list.Add(Open("gear", "Advanced settings", OpenSetup));
        BuildPanel(Mode.Info, "SilentGlass " + ModVersion, true, list);
    }
    // "Everything is fine" + how it's connected and how long it's been up; or how many things need a look
    (string, string) Summary()
    {
        var status = StatusList().Select(s => (s.name, now: s.read())).ToList();
        int issues = status.Count(s => s.now.Item2 == Amber);
        string Text(string name) => status.Find(s => s.name == name).now.Item1 ?? "";
        var up = InfoStats.Uptime(); string upText = up.HasValue ? "up " + InfoStats.Duration(up.Value) : "";
        if (issues > 0) {
            int? temp = InfoStats.ChipTemperature();
            return (issues == 1 ? "One thing to check" : issues + " things to check",
                    string.Join(" · ", new[] { up.HasValue ? "Up " + InfoStats.Duration(up.Value) : null, temp.HasValue ? temp + " °C" : null }.Where(x => x != null)));
        }
        var parts = new List<string> { Text("Network").Split(" · ")[0] };
        if (Text("Internet").EndsWith("home network only")) parts.Add("home network only");
        if (upText.Length > 0) parts.Add(upText);
        return ("Everything is fine", string.Join(" · ", parts.Where(x => x.Length > 0)));
    }
    void OpenStatus()
    {
        BuildPanel(Mode.Status, "Status", true, StatusList().Select(s => Status(s.name, s.read)).ToList());
        rowSel = -1;  // nothing to focus: Back returns to Info
    }

    // ---- Info > Shortcuts: the input each shortcut opens (Left / Right; Hidden leaves it off the bar). Names and icons
    // are typed on the phone. ----
    void OpenShortcuts()
    {
        var list = new List<Row>();
        for (int i = 0; i < Prefs.Slots; i++) {
            int slot = i; var s = prefs.Shortcuts[i];
            list.Add(Choice(s.Icon, s.Name.Length > 0 ? s.Name : "Shortcut " + (i + 1), () => ShortcutValue(slot), d => CycleShortcut(slot, d)));
        }
        list.Add(null);
        list.Add(Open("gear", "Names and icons", OpenSetup, () => "On your phone"));
        BuildPanel(Mode.Shortcuts, "Shortcuts", true, list);
    }
    void BackToInfo(string row) { OpenInfo(); rowSel = Math.Max(0, rows.FindIndex(r => r.Name == row)); UpdatePanel(); }

    // ~45 app-framework calls: read at most every 30 s
    (string, Color) privacy; long privacyAt = long.MinValue / 2; string privacyGaps = "";
    (string, Color) PrivacyStatus()
    {
        if (Environment.TickCount64 - privacyAt < 30000) return privacy;
        privacyAt = Environment.TickCount64;
        var gaps = InfoStats.PrivacyGaps(SelfId); privacyGaps = gaps == null ? "unknown" : string.Join(",", gaps);
        if (gaps == null) return privacy = ("Unknown", Grey);
        if (gaps.Count == 0) return privacy = ("Protected", Live);
        // name what is on (the app id's last part) so it can be found; several: how many
        string What(string g) { var n = g.StartsWith("marker:") ? g.Substring(7) : g; int d = n.LastIndexOf('.'); return d >= 0 ? n.Substring(d + 1) : n; }
        return privacy = (gaps.Count == 1 ? "Check · " + What(gaps[0]) + " is on" : "Check · " + gaps.Count + " not blocked", Amber);
    }
    (string, Color) NetworkStatus()
    {
        string i = InfoStats.DefaultInterface();
        if (i == null) return ("Offline", Amber);
        bool wifi = i.StartsWith("wlan");
        string detail = wifi ? (InfoStats.WifiLevel(i) is int dbm ? dbm + " dBm" : null) : InfoStats.LinkSpeed(i);
        return ((wifi ? "Wi-Fi" : "Wired") + (detail != null ? " · " + detail : ""), Live);
    }
    (string, Color) ThreadStatus()
    {
        var th = InfoStats.Thread();
        if (!ThreadOn) return ("Paused", Grey);
        if (!th.present) return ("Not running", Amber);
        if (th.rloc16 < 0) return ("Starting…", Amber);
        bool routing = (th.rloc16 & 0x1ff) == 0;
        string role = routing ? (th.leader ? "Leader" : "Router") : "Child";
        return (role + (InfoStats.Ipv6Forwarding() && th.homeRoute ? " · home reachable" : " · no home route"), routing ? Live : Amber);
    }
    (string, Color) TvStatus()
    {
        var up = InfoStats.Uptime(); int? t = InfoStats.ChipTemperature();
        return ((up.HasValue ? "Up " + InfoStats.Duration(up.Value) : "Running") + (t.HasValue ? " · " + t + " °C" : ""), Live);
    }
    // written by the bridge whenever its state changes and every minute
    (string, Color) BridgeStatus()
    {
        try {
            var f = new FileInfo(DataFile(Shared.BridgeStatus));
            if (!f.Exists || (DateTime.UtcNow - f.LastWriteTimeUtc).TotalMinutes > 3) return ("Not running", Grey);
            return Shared.ParseConf(File.ReadAllText(f.FullName)).GetValueOrDefault("mqtt") switch {
                "connected" => ("Connected", Live), "connecting" => ("Connecting…", Grey), "unconfigured" => ("Not set up", Grey), _ => ("Can't connect", Amber) };
        } catch { return ("Not running", Grey); }
    }

    // ---- the panel ----
    void BuildPanel(Mode m, string title, bool solid, List<Row> list)
    {
        if (!overlayShown) OpenBar(false);
        ClearOverlay(); mode = m; int wx = PanelWx; Place(wx, 0, 1920 - wx, 1080);
        oDim = Dimming(true, wx, 0, 1920 - wx, 1080, .45f); oRoot.Add(oDim); oRoot.Add(oLayer);
        int h = 32 + (title != null ? HeadH : 0);
        foreach (var r in list) h += RowH(r);
        int top = Math.Max(16, (1080 - h) / 2);
        var panel = Glass(CcX, top, CcW, h, CcR, solid); oLayer.Add(panel);
        if (top >= 120) CornerClock();  // above the panel when it leaves room
        int y = 16;
        if (title != null) {
            var t = Label(title, 24, y + 8, CcW - 48, 52, 34, White, "Display"); panel.Add(t); y += HeadH;
        }
        foreach (var r in list) {
            if (r == null) { panel.Add(Rule(24, y + 12, CcW - 48)); y += RuleH; continue; }
            int rh = RowH(r), rt = r.Kind == RowKind.Level ? 12 : (rh - 42) / 2;
            r.Box = new View { Position2D = new Position2D(16, y), Size2D = new Size2D(CcW - 32, rh), CornerRadius = Round(28) };
            if (r.Kind == RowKind.Summary) {
                var disc = new View { Position2D = new Position2D(24, (rh - 52) / 2), Size2D = new Size2D(52, 52), CornerRadius = Round(26), BackgroundColor = r.Tone };
                disc.Add(Sym(r.Tone == Live ? "check" : "info", 11, 11, 30)); r.Box.Add(disc);
                r.Label = Label("", 94, 6, CcW - 32 - 94 - 24, 40, 30, White, "Semibold"); r.Box.Add(r.Label);
                r.Val = Label("", 94, 46, CcW - 32 - 94 - 24, 30, 23, Faint); r.Box.Add(r.Val);
            } else if (r.Kind == RowKind.Status) {
                r.Light = Dot(34, (rh - 14) / 2, 14, Grey); r.Box.Add(r.Light);
                r.Label = Label(r.Name, 82, rt, 220, 42, 26, White, "Semibold"); r.Box.Add(r.Label);
                r.Val = Label("", CcW - 32 - 24 - 360, rt, 360, 42, 25, Faint); r.Val.HorizontalAlignment = HorizontalAlignment.End; r.Box.Add(r.Val);
            } else {
                r.Icon = Sym(r.Glyph, 24, r.Kind == RowKind.Level ? 14 : (rh - 38) / 2, 38); r.Box.Add(r.Icon);
                r.Label = Label(r.Name, 82, rt - 2, 320, 42, 28, White, "Semibold"); r.Box.Add(r.Label);
                if (r.Value != null) {
                    int vw = r.Kind == RowKind.Level ? 160 : 330, right = r.Kind == RowKind.Action ? 34 : 0;
                    r.Val = Label("", CcW - 32 - 24 - vw - right, rt - 2, vw, 42, r.Kind == RowKind.Level ? 30 : 26, Faint, r.Kind == RowKind.Level ? "Display" : "Medium");
                    r.Val.HorizontalAlignment = HorizontalAlignment.End; r.Box.Add(r.Val);
                }
                if (r.Kind == RowKind.Level) {
                    r.Track = new View { Position2D = new Position2D(82, rh - 26), Size2D = new Size2D(CcW - 32 - 82 - 24, 8), CornerRadius = Round(4) };
                    r.Fill = new View { Size2D = new Size2D(0, 8), CornerRadius = Round(4) }; r.Track.Add(r.Fill); r.Box.Add(r.Track);
                }
                if (r.Kind == RowKind.Switch) { r.Sw = NewSwitch(CcW - 32 - 24 - 64, (rh - 36) / 2); r.Box.Add(r.Sw.Track); }
                if (r.Kind == RowKind.Action) { var c = Sym("chevron", CcW - 32 - 24 - 26, (rh - 26) / 2, 26); c.Opacity = .5f; r.Box.Add(c); }
            }
            panel.Add(r.Box); rows.Add(r); y += rh;
        }
        rowSel = rows.FindIndex(r => !Passive(r));
        UpdatePanel(true); FadeIn(panel); Note(m.ToString().ToLowerInvariant());
        // volume, mute and the sleep timer change without telling us; Info's status rows too
        panelTimer ??= new Tizen.NUI.Timer(3000);
        panelTimer.Tick -= PanelTick; panelTimer.Tick += PanelTick; panelTimer.Start();
    }
    bool PanelTick(object s, Tizen.NUI.Timer.TickEventArgs e) { if (!overlayShown || !InPanel) return false; UpdatePanel(true); return true; }
    // refresh: read the TV again (settings for Controls, the status rows for Info) - on open, every 3 s and when the TV
    // changes, never for a focus move (each read is IPC or file I/O); a changed setting reads itself back
    void UpdatePanel(bool refresh = false)
    {
        if (refresh && mode is Mode.Controls or Mode.More) RefreshSettings();
        // Info: something started or stopped needing a look -> its row comes or goes
        if (refresh && mode == Mode.Info && string.Join(",", StatusList().Where(s => s.read().Item2 == Amber).Select(s => s.name)) != infoIssues) {
            string keep = rowSel >= 0 && rowSel < rows.Count ? rows[rowSel].Name : null;
            OpenInfo(); rowSel = Math.Max(0, rows.FindIndex(r => r.Name == keep)); if (Passive(rows[rowSel])) rowSel = rows.FindIndex(r => !Passive(r));
        }
        for (int i = 0; i < rows.Count; i++) {
            var r = rows[i]; bool f = i == rowSel; var ink = f ? Ink : White;
            if (r.Kind == RowKind.Summary) {
                if (refresh) { var (title, sub) = r.Sum(); if (r.Label.Text != title) r.Label.Text = title; if (r.Val.Text != sub) r.Val.Text = sub; }
                continue;
            }
            if (r.Kind == RowKind.Status) {
                if (refresh) { var (text, tone) = r.Read(); if (r.Val.Text != text) r.Val.Text = text; r.Light.BackgroundColor = tone; }
                continue;
            }
            if (r.Value != null) { var v = r.Value(); if (r.Val.Text != v) r.Val.Text = v; }
            r.Box.BackgroundColor = f ? FocusFill : Clear; r.Label.TextColor = ink; r.Icon.Color = ink;
            if (r.Val != null) r.Val.TextColor = r.Kind == RowKind.Level ? ink : f ? FaintInk : Faint;
            if (r.Track != null) {
                float l = r.Amount(); r.Track.BackgroundColor = f ? new Color(0, 0, 0, .14f) : new Color(1, 1, 1, .22f); r.Fill.BackgroundColor = ink;
                r.Fill.Size2D = new Size2D(l < 0 ? 0 : (int)(r.Track.Size2D.Width * Math.Clamp(l, 0f, 1f)), 8);
            }
            if (r.Sw != null) SetSwitch(r.Sw, r.On());
        }
    }
    void PanelKey(string k, bool back, bool sel)
    {
        if (back) {
            if (mode == Mode.Shortcuts) BackToInfo("Shortcuts");
            else if (mode == Mode.Status) BackToInfo("Status details");
            else if (mode == Mode.More) { OpenControls(); rowSel = Math.Max(0, rows.FindIndex(r => r.Name == "More")); UpdatePanel(); }
            else BackToBar(mode == Mode.Controls ? "controls" : "info");
            return;
        }
        if (rowSel < 0) return;
        if (k == "Up" || k == "Down") {
            int d = k == "Up" ? -1 : 1, n = rowSel + d;
            while (n >= 0 && n < rows.Count && Passive(rows[n])) n += d;
            if (n >= 0 && n < rows.Count) { rowSel = n; UpdatePanel(); }
            return;
        }
        var r = rows[rowSel];
        Mode before = mode;
        if ((k == "Left" || k == "Right") && r.Adjust != null) r.Adjust(k == "Left" ? -1 : 1);
        else if (sel) r.Select?.Invoke();
        else return;
        if (overlayShown && mode == before) UpdatePanel();
        Note("row:" + r.Name);
    }
}
