using System;
using System.IO;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;

// The app's own full-screen window, used only as a backdrop when Home has no picture to draw over (no signal, or
// Home pressed from Ambient or an app): the wallpaper and the time. Photo wallpaper (wall-day.jpg / wall-night.jpg,
// switched at local sunrise and sunset; not in git) or the gradient art. After 5 idle minutes it fades to black with
// a dim drifting clock (OLED care). Hidden whenever anything else is in front.
public sealed partial class SilentGlass
{
    ImageView wallpaper; TextLabel clock; View dimLayer; TextLabel dimClock;
    View backdrop;  // wallpaper + clock; hidden (with a clear window) except while the backdrop is wanted
    bool backdropShown, wallNight, dimmed;
    long lastActivity = Environment.TickCount64;
    const long IdleMs = 5 * 60 * 1000;
    readonly Random drift = new();
    static bool PhotoWall => File.Exists(Res + "wallpapers/wall-day.jpg") && File.Exists(Res + "wallpapers/wall-night.jpg");
    string WallUrl(bool night) => Res + "wallpapers/wall-" + (night ? "night" : "day") + ".jpg";
    bool WantNight() => prefs.Wall == "night" || prefs.Wall != "day" && Logic.IsNight(DateTime.UtcNow, prefs.Lat, prefs.Lon);

    void BuildBackdrop()
    {
        var w = Window.Default;
        w.Title = "SilentGlass"; w.KeyEvent += OnKey;
        // no compositor show/hide effect (the TV zooms app windows in): the window hides and shows on Home presses
        try { w.AddAuxiliaryHint("wm.comp.win.effect.enable", "0"); } catch { }
        backdrop = new View { Size2D = new Size2D(1920, 1080) };
        if (PhotoWall) { wallNight = WantNight(); wallpaper = new ImageView { ResourceUrl = WallUrl(wallNight), Size2D = new Size2D(1920, 1080) }; backdrop.Add(wallpaper); }
        else backdrop.Add(new ImageView { ResourceUrl = Res + "free-background.svg", Size2D = new Size2D(1920, 1080), DesiredWidth = 960, DesiredHeight = 540 });
        clock = Label("", 1420, 40, 420, 64, 40, White, "Display"); clock.HorizontalAlignment = HorizontalAlignment.End; backdrop.Add(clock);
        w.Add(backdrop);
        // The TV shows this window when the app starts, and its launch placeholder (which holds the remote's keys)
        // only goes away once that has happened. So it is not hidden here: Home hides it when it opens the overlay
        // (or keeps it as the backdrop). It starts black and empty.
        w.BackgroundColor = new Color(0, 0, 0, 1); backdrop.Hide();
    }
    Tizen.NUI.Timer mainHide;
    bool mainUp = true;  // the window is on screen (the TV shows it at start)
    void ShowBackdrop()
    {
        if (backdropShown) return;
        backdropShown = true; mainUp = true; UpdateClock(); CheckWallpaper(); lastActivity = Environment.TickCount64;
        mainHide?.Stop();
        backdrop.Show(); Note("backdrop:show");
        try { Window.Default.Show(); Window.Default.Raise(); } catch { }
        if (overlayShown) { ow.Raise(); try { ow.Activate(); } catch { } }
    }
    void HideBackdrop()
    {
        if (!backdropShown && !mainUp) return;
        backdropShown = false; mainUp = false; Wake(); Note("backdrop:hide");
        // end the dim first and hide the window a few frames later, so its last frame is the plain wallpaper - what
        // the next Show draws anyway (the window is black underneath: emptying it first would flash black)
        mainHide ??= ClearThenHide(HideMainNow);
        mainHide.Stop(); mainHide.Start();
    }
    void HideMainNow() { if (backdropShown) return; try { Window.Default.Hide(); } catch { } backdrop.Hide(); }
    // A cold launch (the first Home after SilentGlass restarted) makes it the TV's front app, and the TV keeps it
    // there with a near-black launch placeholder under its hidden main window - over the picture, holding the remote.
    // Whenever SilentGlass is the front app with nothing of its own full screen, the viewer is brought back; an open
    // bar takes the remote again once the viewer is in front.
    int placeholderGen;
    void PlaceholderGuard()
    {
        string f = Foreground();
        if (f == Viewer && overlayShown && !backdropShown) { ow.Raise(); try { ow.Activate(); } catch { } return; }
        if (f != SelfId || backdropShown || mainUp) return;
        int g = ++placeholderGen;
        ((IScreen)this).After(400, () => {
            if (g != placeholderGen || backdropShown || mainUp || Foreground() != SelfId) return;
            Note("placeholder:viewer"); Launch(Viewer);
        });
    }
    void UpdateClock() { clock.Text = DateTime.Now.ToString("h:mm tt"); }
    void CheckWallpaper()
    {
        if (wallpaper == null) return;
        bool night = WantNight(); if (night == wallNight) return;
        wallNight = night; wallpaper.ResourceUrl = WallUrl(night);
    }

    // ---- OLED care ----
    void IdleTick()
    {
        if (dimmed) { MoveDimClock(); return; }
        if (prefs.Dim && backdropShown && Environment.TickCount64 - lastActivity >= IdleMs) Dim();
    }
    void Dim()
    {
        dimmed = true; CloseOverlay("dimmed");
        dimLayer = new View { Size2D = new Size2D(1920, 1080), BackgroundColor = new Color(0, 0, 0, 1), Opacity = 0 };
        dimClock = new TextLabel { Size2D = new Size2D(360, 90), PixelSize = 64, FontFamily = "Quiet Inter Display", TextColor = new Color(.32f, .33f, .36f, 1),
                                   HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        dimLayer.Add(dimClock); MoveDimClock(); Window.Default.Add(dimLayer);
        var a = new Animation(2000); a.AnimateTo(dimLayer, "Opacity", 1f); Play(a);
    }
    void MoveDimClock()
    {
        if (dimClock == null) return;
        dimClock.Text = DateTime.Now.ToString("h:mm");
        dimClock.Position2D = new Position2D(120 + drift.Next(1920 - 240 - 360), 120 + drift.Next(1080 - 240 - 90));
    }
    // any key (or Home) wakes; over the backdrop the bar comes back
    void Wake()
    {
        lastActivity = Environment.TickCount64;
        if (!dimmed) return;
        dimmed = false;
        if (dimLayer != null) { Window.Default.Remove(dimLayer); dimLayer.Dispose(); dimLayer = null; dimClock = null; }
        if (backdropShown && !overlayShown) OpenBar(false);
    }
}
