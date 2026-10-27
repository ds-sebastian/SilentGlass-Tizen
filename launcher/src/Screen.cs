using System;

// What SilentGlass puts on screen - the wallpaper backdrop, the bar over the picture, or nothing - decided with no NUI
// types, so tests/screen-sim drives this exact code against a simulated TV (inputs waking and resyncing, Ambient,
// standby, other apps, Home Assistant).
//  - The picture: the TV viewer showing the current input with a signal (memory/tv-viewer/mainscreen_signal_state,
//    settled for 800 ms because it flaps while an input syncs). The flag goes stale while the viewer is not running
//    (standby, Ambient), so it only counts when the viewer is the app behind us.
//  - Home over a picture: the bar over it. Home with no picture: the wallpaper behind the bar.
//  - The wallpaper never covers a picture: when the current input's picture arrives while it is up (a SHIELD waking,
//    an input finishing its sync), SilentGlass steps aside to the picture. Home during a brief dropout (a frame-rate
//    or HDR switch) still counts as over the picture.
//  - Whatever takes the screen ends ours: another app, the viewer coming to the front, an input switch SilentGlass
//    did not make (HDMI-CEC One Touch Play), standby.
//  - Home from Ambient goes to the picture first, then decides like any Home press.
public interface IScreen
{
    bool OverlayShown { get; }
    bool BackdropShown { get; }
    bool SetupOpen { get; }
    long OverlayClosedAt { get; }   // clock ms when the overlay last closed
    int ReadSignal();
    int ReadSource();
    string ReadForeground();
    void ShowBackdrop();
    void HideBackdrop();
    void OpenBar(bool keepFocus);
    void OpenPart(string part);  // controls, info, apps, inputs, setup
    void CloseOverlay(string why);
    void Launch(string appId);
    bool ConnectSource(int type);
    void Wake();                 // ends the OLED dim
    void TvChanged();            // refresh what is showing (bar labels, Inputs, panels)
    void After(int ms, Action action);
    void Note(string what);
}

public sealed class Screen
{
    public const string Viewer = "org.tizen.tv-viewer", Self = "com.samsung.tv.bixby-promotion", SamsungHome = "com.samsung.tv.csfs";
    public const int SettleMs = 800, DropoutGraceMs = 4000, ViewerWaitMs = 3000;
    readonly IScreen ui; readonly Func<long> clock;
    public Screen(IScreen ui, Func<long> clock) { this.ui = ui; this.clock = clock; }

    public int Signal = -1, Source = 15;  // Source: TV and HDMI 1-4 only; USB, DLNA and apps leave the last real input
    public string LastOther = "";         // the last app in front other than us
    string previous = "";
    int settleGen, homeGen, pollGen; bool homeAfterViewer, directHome; string pendingOpen;
    public const int BackdropPollMs = 1000, EchoMs = 1200;
    bool selfLaunch; long lastHome = long.MinValue / 2;  // the next launch request is our own relaunch, not a Home press
    void LaunchSelf() { selfLaunch = true; ui.Launch(Self); }
    long pictureLostAt = long.MinValue / 2; int pictureLostOn = -1;
    public bool HasPicture => Signal == 1;
    // over the picture: the viewer is the app behind us and its input shows a picture, or did a moment ago
    bool OverPicture() => (LastOther == Viewer || LastOther == "") &&
        (HasPicture || pictureLostOn == Source && clock() - pictureLostAt < DropoutGraceMs);
    public static bool IsAmbient(string id) => id == "org.tizen.art-app" || id == "com.samsung.tv.home.art" || id == "com.samsung.tv.ambientfte";
    static bool RealInput(int n) => n == 0 || Logic.IsHdmi(n);

    public void Start() { int s = ui.ReadSource(); if (RealInput(s)) Source = s; Signal = ui.ReadSignal(); NoteForeground(ui.ReadForeground()); }
    void NoteForeground(string id) { if (!string.IsNullOrEmpty(id) && id != Self) LastOther = id; }

    // The Home button. Over a picture it toggles the bar; with nothing to draw over, the wallpaper comes up behind it.
    public void Home(string open, string why)
    {
        bool own = selfLaunch; selfLaunch = false;
        // The remote's Home key reaches us twice: a second launch request 0.4-1 s after the first undid it - the bar
        // closed and reopened (the "bounce", seen in the event log). A plain Home within 1.2 s of the last one, or of
        // the bar closing (Watch, Back), is that echo.
        long now = clock();
        if (!own && open == "" && (now - lastHome < EchoMs || !ui.OverlayShown && now - ui.OverlayClosedAt < EchoMs)) { ui.Note(why + ":echo"); return; }
        if (open == "") lastHome = now;
        // a press while going to the picture first: this press decides, the pending relaunch is dropped
        if (!own && homeAfterViewer) { homeAfterViewer = false; ++homeGen; }
        ui.Wake();
        if (open == "" && pendingOpen != null) open = pendingOpen;  // our own relaunch after going to the picture
        pendingOpen = null;
        bool direct = directHome; directHome = false;
        // over Ambient (Home Assistant's Home button, QuickLaunch): to the picture first, as for Ambient's Home key
        if (!direct && IsAmbient(LastOther)) { PictureFirst(open, why); return; }
        if (open == "" && ui.OverlayShown && !ui.BackdropShown) { ui.CloseOverlay(why + ":close"); return; }
        bool keep = open == "" && ui.OverlayShown;
        if (OverPicture()) ui.HideBackdrop(); else { ui.ShowBackdrop(); PollUnderBackdrop(); }
        ui.OpenBar(keep);
        if (open != "") ui.OpenPart(open);
        ui.Note(why + (open == "" ? "" : ":" + open) + (ui.BackdropShown ? ":backdrop" : ":picture"));
    }

    public void Foreground(string now)
    {
        if (string.IsNullOrEmpty(now)) return;
        string before = previous; previous = now; NoteForeground(now); ui.Note("fg:" + now);
        // another app came to the front (not us, not the picture): nothing of ours may stay over it
        if (now != Self && now != Viewer && (ui.OverlayShown || ui.BackdropShown)) { ui.CloseOverlay("foreground:" + now); ui.HideBackdrop(); }
        // the viewer came back in front of the wallpaper (CEC, the TV's own Source key): the picture is in front
        else if (now == Viewer && ui.BackdropShown) { ui.CloseOverlay("foreground:viewer"); ui.HideBackdrop(); }
        // Ambient's Home key always opens Samsung Home (hard-coded). Go to the picture instead, let its signal flag
        // settle (it is stale while Ambient runs), then open SilentGlass as for any Home press.
        if (now == SamsungHome && IsAmbient(before)) PictureFirst("", "ambient_home");
        if (now == Viewer && homeAfterViewer) {
            homeAfterViewer = false; int g = ++homeGen;
            ui.After(SettleMs + 300, () => { if (g == homeGen) LaunchSelf(); });
        }
    }
    // launch the viewer, wait for it to come to the front and its signal flag to settle, then open SilentGlass;
    // if it never comes, open SilentGlass anyway (over the wallpaper)
    void PictureFirst(string open, string why)
    {
        homeAfterViewer = true; pendingOpen = open; int g = ++homeGen;
        ui.Launch(Viewer); ui.Note(why + ":picture_first");
        ui.After(ViewerWaitMs, () => { if (homeAfterViewer && g == homeGen) { homeAfterViewer = false; directHome = true; LaunchSelf(); } });
    }
    // the screen went off: nothing of ours is left up for when it comes back
    public void Standby() { homeAfterViewer = false; pendingOpen = null; ui.CloseOverlay("standby"); ui.HideBackdrop(); }

    // the signal flag changed; act once it has held for SettleMs
    public void SignalFlapped() { int g = ++settleGen; ui.After(SettleMs, () => { if (g == settleGen) Settled(); }); }
    // While the wallpaper is in front the TV may not notify flag changes (a SHIELD switched on under it went unseen
    // on the TV): read the flag every second while the wallpaper is up, and treat a change like a notification.
    bool polling;
    void PollUnderBackdrop()
    {
        if (polling) return;  // already reading (a second Home must not push the next read back)
        polling = true; int g = ++pollGen;
        void Check()
        {
            if (g != pollGen || !ui.BackdropShown) { polling = false; return; }
            int v = ui.ReadSignal();
            if (v != Signal) { ui.Note("signal_polled:" + v); SignalFlapped(); }
            ui.After(BackdropPollMs, Check);
        }
        ui.After(BackdropPollMs, Check);
    }
    void Settled()
    {
        bool had = HasPicture; Signal = ui.ReadSignal(); ui.Note("signal:" + Signal);
        if (had && !HasPicture) { pictureLostAt = clock(); pictureLostOn = Source; }
        // the picture arrived under the wallpaper: step aside to it (not while the phone setup sheet is open)
        if (!had && HasPicture && ui.BackdropShown && !ui.SetupOpen) { ToPicture("picture_arrived"); return; }
        ui.TvChanged();
    }
    // an input switch SilentGlass did not make (SelectSource records its own first): HDMI-CEC One Touch Play, the
    // TV's Source key. The new picture takes the screen; an open bar would no longer get the keys.
    public void SourceChanged()
    {
        int n = ui.ReadSource(); if (!RealInput(n) || n == Source) return;
        Source = n; Signal = ui.ReadSignal(); pictureLostOn = -1;
        if (ui.OverlayShown || ui.BackdropShown) { ui.CloseOverlay("input_changed:" + n); ui.HideBackdrop(); }
        ui.TvChanged();
    }
    // an open bar stays, now over the picture (and steps aside after 45 s like any bar over a picture)
    void ToPicture(string why)
    {
        if (ui.OverlayShown) { ui.HideBackdrop(); ui.OpenBar(true); ui.Note(why + ":bar_over_picture"); return; }
        ui.HideBackdrop(); ui.Launch(Viewer); ui.Note(why);
    }

    public void SelectSource(int type)
    {
        bool ok = ui.ConnectSource(type);
        Source = type; pictureLostOn = -1; ui.Note("source:" + type + ":" + ok);
        ui.CloseOverlay(null); ui.HideBackdrop(); ui.Launch(Viewer);
    }
    // Watch / Play on the bar: already showing that picture - just step aside; else switch to it
    public bool BarWatching(int type) => type == Source && HasPicture && !ui.BackdropShown;
    // Back on the bar: over the wallpaper it goes to the picture (the last input); over the picture it closes the bar
    public void BarBack() { if (ui.BackdropShown) SelectSource(Source); else ui.CloseOverlay("back"); }
    // the TV also pauses us whenever only the overlay is up, so a pause alone must not close it
    public void Paused() { if (!ui.OverlayShown) ui.HideBackdrop(); }
}
