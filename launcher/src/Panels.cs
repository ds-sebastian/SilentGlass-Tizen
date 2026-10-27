using System;
using System.Collections.Generic;
using System.IO;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;

// Panels on the right, in place of the bar: Controls (picture and sound; translucent, so the picture shows how a
// change looks) and Info (how the TV is doing, then SilentGlass's own settings; near-opaque for reading).
// One row model for both: Up/Down move, Left/Right adjust or cycle, Select steps, toggles or opens, Back -> bar.
public sealed partial class SilentGlass
{
    enum RowKind { Status, Level, Value, Switch, Action }
    sealed class Row
    {
        public RowKind Kind; public string Name, Glyph;
        public Func<string> Value; public Func<float> Amount; public Func<bool> On; public Func<Color> Tone;
        public Action<int> Adjust; public Action Select;
        public View Box, Track, Fill, Light; public ImageView Icon; public TextLabel Label, Val; public Switch Sw;
    }
    readonly List<Row> rows = new();
    int rowSel;
    Tizen.NUI.Timer infoTimer;
    const int CcX = 1236, CcW = 624, CcR = 44, SliderH = 88, ValueH = 64, StatusH = 56, RuleH = 25, HeadH = 70;

    static Row Level(string glyph, string name, Func<string> value, Func<float> amount, Action<int> adjust, Action select = null) =>
        new() { Kind = RowKind.Level, Glyph = glyph, Name = name, Value = value, Amount = amount, Adjust = adjust, Select = select };
    static Row Choice(string glyph, string name, Func<string> value, Action<int> adjust) =>
        new() { Kind = RowKind.Value, Glyph = glyph, Name = name, Value = value, Adjust = adjust, Select = () => adjust(1) };
    static Row Toggle(string glyph, string name, Func<bool> on, Action<bool> set) =>
        new() { Kind = RowKind.Switch, Glyph = glyph, Name = name, On = on, Adjust = d => set(d > 0), Select = () => set(!on()) };
    static Row Open(string glyph, string name, Action select, Func<string> value = null) =>
        new() { Kind = RowKind.Action, Glyph = glyph, Name = name, Select = select, Value = value };
    static Row Status(string name, Func<(string text, Color tone)> read)
    {
        (string text, Color tone) last = ("", Grey);
        return new Row { Kind = RowKind.Status, Name = name, Value = () => (last = read()).text, Tone = () => last.tone };
    }

    // ---- Controls ----
    void OpenControls()
    {
        RefreshSettings();
        string Output() => Setting("soundoutput") == "2" ? "Soundbar" : Setting("soundoutput") == "0" ? "TV Speaker" : SettingName("soundoutput");
        BuildPanel(Mode.Controls, null, false, new List<Row> {
            Level("sun", "Brightness", () => Setting("brightness"), () => Level("brightness"), d => ChangeLevel("brightness", d)),
            Level("contrast", "Contrast", () => Setting("contrast"), () => Level("contrast"), d => ChangeLevel("contrast", d)),
            Level("speaker", "Volume", () => mute == 1 ? "Muted" : volume < 0 ? "–" : volume.ToString(), () => volume < 0 ? -1 : volume / 100f, ChangeVolume, ToggleMute),
            null,
            Choice("picture", "Picture Mode", () => SettingName("picturemode"), d => CycleSetting("picturemode", d)),
            Choice("waveform", "Sound Mode", () => SettingName("external-sound-mode"), d => CycleSetting("external-sound-mode", d)),
            Choice("soundbar", "Sound Output", Output, d => SetSetting("soundoutput", Setting("soundoutput") == "0" ? "2" : "0")),
            Choice("gamecontroller", "Game Mode", () => Shared.GameName(SettingName("game-mode-auto")), d => CycleSetting("game-mode-auto", d)),
            Choice("moon", "Sleep Timer", () => SettingName("sleeptimer"), d => CycleSetting("sleeptimer", d)),
            Toggle("pixelshift", "Pixel Shift", () => Setting("pixel-shift-menu") == "1", on => SetSetting("pixel-shift-menu", on ? "1" : "0")),
            null,
            Open("artframe", "Ambient", () => OpenApp(Shared.Art)),
            Open("bluetooth", "Bluetooth", () => OpenTvMenu("add-bluetooth-keyboard")),
            Open("network", "Network", () => OpenTvMenu("networksettings")),
            Open("gear", "TV Settings", () => OpenTvMenu(null)),
        });
    }

    // ---- Info ----
    void OpenInfo()
    {
        var list = new List<Row> {
            Status("Privacy", PrivacyStatus), Status("Network", NetworkStatus) };
        if (ThreadInstalled) list.Add(Status("Thread", ThreadStatus));
        list.Add(Status("Home Assistant", BridgeStatus));
        list.Add(Status("This TV", TvStatus));
        list.Add(null);
        list.Add(Choice("tv", "Watch", () => PortChoice(prefs.Watch, watchPort), d => CyclePort(true, d)));
        list.Add(Choice("gamecontroller", "Play", () => PortChoice(prefs.Play, playPort), d => CyclePort(false, d)));
        list.Add(Toggle("apps", "Apps in the bar", () => !prefs.HideApps, on => { prefs.HideApps = !on; SavePrefs(); }));
        list.Add(Toggle("moon", "Dim when idle", () => prefs.Dim, on => { prefs.Dim = on; SavePrefs(); }));
        if (PhotoWall) list.Add(Choice("picture", "Wallpaper", () => Logic.WallName(prefs.Wall), d => { prefs.Wall = Logic.NextWall(prefs.Wall, d); SavePrefs(); CheckWallpaper(); }));
        list.Add(Toggle("bell", "Notifications", () => NoticesOn, SetNotices));
        list.Add(null);
        // everything else (Home Assistant, the Thread border router, the wallpaper's location) is set from a phone
        list.Add(Open("gear", "Advanced settings", OpenSetup));
        BuildPanel(Mode.Info, "SilentGlass " + ModVersion, true, list);
        infoTimer ??= new Tizen.NUI.Timer(3000);
        infoTimer.Tick -= InfoTick; infoTimer.Tick += InfoTick; infoTimer.Start();
    }
    bool InfoTick(object s, Tizen.NUI.Timer.TickEventArgs e) { if (!overlayShown || mode != Mode.Info) return false; UpdatePanel(); return true; }

    (string, Color) PrivacyStatus()
    {
        var gaps = InfoStats.PrivacyGaps(SelfId); privacyGaps = gaps == null ? "unknown" : string.Join(",", gaps);
        if (gaps == null) return ("Unknown", Grey);
        if (gaps.Count == 0) return ("Protected", Live);
        // name what is on (the app id's last part) so it can be found; several: how many
        string What(string g) { var n = g.StartsWith("marker:") ? g.Substring(7) : g; int d = n.LastIndexOf('.'); return d >= 0 ? n.Substring(d + 1) : n; }
        return (gaps.Count == 1 ? "Check · " + What(gaps[0]) + " is on" : "Check · " + gaps.Count + " not blocked", Amber);
    }
    string privacyGaps = "";
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
        ClearOverlay(); mode = m; int wx = CcX - 60; Place(wx, 0, 1920 - wx, 1080);
        oDim = Dimming(true, wx, 0, 1920 - wx, 1080, .45f); oRoot.Add(oDim); oRoot.Add(oLayer);
        int h = 32 + (title != null ? HeadH : 0);
        foreach (var r in list) h += r == null ? RuleH : r.Kind == RowKind.Level ? SliderH : r.Kind == RowKind.Status ? StatusH : ValueH;
        var panel = Glass(CcX, Math.Max(32, (1080 - h) / 2), CcW, h, CcR, solid); oLayer.Add(panel);
        int y = 16;
        if (title != null) {
            var t = Label(title, 24, y + 8, CcW - 48, 52, 34, White, "Display"); panel.Add(t); y += HeadH;
        }
        foreach (var r in list) {
            if (r == null) { panel.Add(Rule(24, y + 12, CcW - 48)); y += RuleH; continue; }
            int rh = r.Kind == RowKind.Level ? SliderH : r.Kind == RowKind.Status ? StatusH : ValueH, top = r.Kind == RowKind.Level ? 12 : (rh - 42) / 2;
            r.Box = new View { Position2D = new Position2D(16, y), Size2D = new Size2D(CcW - 32, rh), CornerRadius = Round(28) };
            if (r.Kind == RowKind.Status) {
                r.Light = Dot(34, (rh - 14) / 2, 14, Grey); r.Box.Add(r.Light);
                r.Label = Label(r.Name, 82, top, 220, 42, 26, White, "Semibold"); r.Box.Add(r.Label);
                r.Val = Label("", CcW - 32 - 24 - 360, top, 360, 42, 25, Faint); r.Val.HorizontalAlignment = HorizontalAlignment.End; r.Box.Add(r.Val);
            } else {
                r.Icon = Sym(r.Glyph, 24, r.Kind == RowKind.Level ? 14 : (rh - 38) / 2, 38); r.Box.Add(r.Icon);
                r.Label = Label(r.Name, 82, top - 2, 320, 42, 28, White, "Semibold"); r.Box.Add(r.Label);
                if (r.Value != null) {
                    int vw = r.Kind == RowKind.Level ? 160 : 330, right = r.Kind == RowKind.Action ? 34 : 0;
                    r.Val = Label("", CcW - 32 - 24 - vw - right, top - 2, vw, 42, r.Kind == RowKind.Level ? 30 : 26, Faint, r.Kind == RowKind.Level ? "Display" : "Medium");
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
        rowSel = rows.FindIndex(r => r.Kind != RowKind.Status);
        UpdatePanel(); FadeIn(panel); Note(m.ToString().ToLowerInvariant());
    }
    void UpdatePanel()
    {
        if (mode == Mode.Controls) RefreshSettings();
        for (int i = 0; i < rows.Count; i++) {
            var r = rows[i]; bool f = i == rowSel; var ink = f ? Ink : White;
            if (r.Value != null) { var v = r.Value(); if (r.Val.Text != v) r.Val.Text = v; }
            if (r.Kind == RowKind.Status) { r.Light.BackgroundColor = r.Tone(); continue; }
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
        if (back) { BackToBar(mode == Mode.Controls ? "controls" : "info"); return; }
        if (rowSel < 0) return;
        if (k == "Up" || k == "Down") {
            int d = k == "Up" ? -1 : 1, n = rowSel + d;
            while (n >= 0 && n < rows.Count && rows[n].Kind == RowKind.Status) n += d;
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
