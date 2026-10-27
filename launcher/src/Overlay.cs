using System;
using System.Collections.Generic;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;

// Everything interactive lives in one partial, transparent window of type Notification at level Top (the package's
// Samsung manifest already holds window.priority.set), sized to what it shows - the kind of window Vetro's dock uses.
// Over a picture the TV viewer stays in front and keeps playing: nothing touches the video plane, and focus returns
// to the viewer when the window hides. Layout: the bar at the bottom; Inputs and Apps open above it; Controls and
// Info replace it with a panel on the right (the picture stays visible on the left); the setup sheet is centred.
public sealed partial class SilentGlass
{
    enum Mode { Bar, Inputs, Apps, Controls, Info, Setup }
    Window ow; View oRoot, oDim, oLayer, capsule, highlight;
    Mode mode; bool overlayShown; Tizen.NUI.Timer overlayIdle;
    const int BandTop = 240, BarH = 104, BarBottom = 52, ItemH = 80, ItemPad = 30, GlyphS = 40, GlyphGap = 14, PopGap = 18, OverlayIdleMs = 45000;
    int BarTop => 1080 - BarBottom - BarH;

    sealed class BarItem { public string Action; public ImageView Glyph; public TextLabel Title, Sub; public int X, W; }
    readonly List<BarItem> bar = new();
    int barSel = -1; string barSig = "";

    void EnsureOverlay()
    {
        if (ow != null) return;
        ow = new Window("SilentGlass Overlay", new Rectangle(0, BandTop, 1920, 1080 - BandTop), true);
        ow.Hide();
        ow.Type = WindowType.Notification;
        ow.SetNotificationLevel(NotificationLevel.Top);
        ow.AddAuxiliaryHint("wm.policy.win.user.geometry", "1");  // keep our size: never full screen over the picture
        ow.AddAuxiliaryHint("wm.comp.win.effect.enable", "0");
        ow.BackgroundColor = Clear;
        ow.SetAcceptFocus(true);
        ow.KeyEvent += OnKey;
        oRoot = new View { Size2D = new Size2D(1920, 1080) }; ow.Add(oRoot);
    }
    // the window covers only what is drawn; content is laid out in screen coordinates, shifted by the window origin
    void Place(int x, int y, int w, int h) { try { ow.WindowPositionSize = new Rectangle(x, y, w, h); } catch { } oRoot.Position2D = new Position2D(-x, -y); }
    void ClearOverlay()
    {
        bar.Clear(); rows.Clear(); menu.Clear(); tiles.Clear(); highlight = null; capsule = null; setupSheet = null; popover = null;
        while (oRoot != null && oRoot.ChildCount > 0) { var v = oRoot.GetChildAt(0); oRoot.Remove(v); v.Dispose(); }
        oLayer = new View { Size2D = new Size2D(1920, 1080) }; oDim = null;
    }
    void ShowOverlay()
    {
        if (overlayShown) return;
        ow.Show(); ow.Raise(); try { ow.Activate(); } catch { }
        overlayShown = true;
    }
    void CloseOverlay(string why)
    {
        if (!overlayShown) return;
        overlayShown = false; overlayIdle?.Stop(); infoTimer?.Stop(); CloseSetupServer();
        try { ow.Hide(); } catch { }
        ClearOverlay();
        if (why != null) Note(why);
    }
    // over a picture the bar steps aside by itself; over the wallpaper it stays (the backdrop dims instead)
    void OverlayActivity()
    {
        lastActivity = Environment.TickCount64;
        if (backdropShown) return;
        if (overlayIdle == null) { overlayIdle = new Tizen.NUI.Timer(5000); overlayIdle.Tick += (s, e) => { if (overlayShown && !backdropShown && mode != Mode.Setup && Environment.TickCount64 - lastActivity >= OverlayIdleMs) { CloseOverlay("idle"); return false; } return overlayShown; }; }
        if (!overlayIdle.IsRunning()) overlayIdle.Start();
    }

    // ---- the bar ----
    string BarSignature() => InputName(watchPort) + "|" + InputName(playPort) + "|" + lastSource + "|" + screen.Signal + "|" + prefs.HideApps;
    bool PortsChanged() { RefreshInputs(); ResolvePorts(); return BarSignature() != barSig; }
    bool LiveDot(string action) => action == "hdmi:" + lastSource && HasPicture;
    void OpenBar(bool keepFocus)
    {
        EnsureOverlay();
        string keep = keepFocus && barSel >= 0 && barSel < bar.Count ? bar[barSel].Action : null;
        ClearOverlay(); mode = Mode.Bar; Place(0, BandTop, 1920, 1080 - BandTop);
        RefreshInputs(); ResolvePorts(); barSig = BarSignature();
        oDim = Dimming(false, 0, BandTop + 180, 1920, 1080 - BandTop - 180, .5f); oRoot.Add(oDim); oRoot.Add(oLayer);
        // Watch / Play keep their names; the device each opens (the TV's input name) is the line underneath
        var items = new List<(string glyph, string title, string sub, string action)> {
            ("tv", "Watch", InputName(watchPort), "hdmi:" + watchPort), ("gamecontroller", "Play", InputName(playPort), "hdmi:" + playPort),
            (null, null, null, null), ("inputs", "Inputs", null, "inputs"), ("controls", "Controls", null, "controls") };
        if (!prefs.HideApps && library.Count > 0) items.Add(("apps", "Apps", null, "apps"));
        items.Add(("info", "Info", null, "info"));
        // measure, then centre the capsule
        var labels = new List<(TextLabel t, TextLabel s, int w)>(); int total = 24;
        foreach (var it in items) {
            if (it.glyph == null) { labels.Add((null, null, 0)); total += 22; continue; }
            var t = Label(it.title, 0, 0, 10, ItemH, 30, White, "Semibold");
            TextLabel s = it.sub == null ? null : Label(it.sub, 0, 0, 10, 28, 21, Faint);
            int w = Math.Max(TextWidth(t), s == null ? 0 : TextWidth(s) + (LiveDot(it.action) ? 22 : 0));
            labels.Add((t, s, w)); total += 2 * ItemPad + GlyphS + GlyphGap + w;
        }
        capsule = Glass(960 - total / 2, BarTop, total, BarH, BarH / 2); oLayer.Add(capsule);
        highlight = new View { Size2D = new Size2D(10, ItemH), BackgroundColor = FocusFill, CornerRadius = Round(ItemH / 2f) }; capsule.Add(highlight);
        int ix = 12;
        for (int i = 0; i < items.Count; i++) {
            var it = items[i];
            if (it.glyph == null) { capsule.Add(new View { Position2D = new Position2D(ix + 10, (BarH - 40) / 2), Size2D = new Size2D(2, 40), BackgroundColor = new Color(1, 1, 1, .22f) }); ix += 22; continue; }
            var (t, s, w) = labels[i]; int iw = 2 * ItemPad + GlyphS + GlyphGap + w, tx = ix + ItemPad + GlyphS + GlyphGap;
            var g = Sym(it.glyph, ix + ItemPad, (BarH - GlyphS) / 2, GlyphS); capsule.Add(g);
            if (s == null) { t.Position2D = new Position2D(tx, (BarH - ItemH) / 2); t.Size2D = new Size2D(w + 4, ItemH); capsule.Add(t); }
            else {
                t.Position2D = new Position2D(tx, 19); t.Size2D = new Size2D(w + 4, 38); capsule.Add(t);
                s.Position2D = new Position2D(tx, 55); s.Size2D = new Size2D(TextWidth(s) + 4, 28); capsule.Add(s);
                if (LiveDot(it.action)) capsule.Add(Dot(tx + TextWidth(s) + 10, 63, 12, Live));
            }
            bar.Add(new BarItem { Action = it.action, Glyph = g, Title = t, Sub = s, X = ix, W = iw });
            ix += iw;
        }
        barSel = keep != null ? bar.FindIndex(b => b.Action == keep) : -1;
        if (barSel < 0) barSel = Math.Max(0, bar.FindIndex(b => b.Action == "hdmi:" + lastSource));
        FocusBar(false);
        bool fresh = !overlayShown;
        ShowOverlay(); OverlayActivity();
        if (fresh) FadeIn(capsule, 24);
    }
    // the focused item is a white capsule; while one of its menus is open it stays marked in faint white
    void FocusBar(bool animate)
    {
        for (int i = 0; i < bar.Count; i++) {
            bool f = i == barSel && mode == Mode.Bar;
            bar[i].Title.TextColor = f ? Ink : White; bar[i].Glyph.Color = f ? Ink : White;
            if (bar[i].Sub != null) bar[i].Sub.TextColor = f ? FaintInk : Faint;
        }
        if (highlight == null || barSel < 0 || barSel >= bar.Count) return;
        var b = bar[barSel]; highlight.BackgroundColor = mode == Mode.Bar ? FocusFill : new Color(1, 1, 1, .18f);
        if (animate) { var a = new Animation(150) { DefaultAlphaFunction = Ease() }; a.AnimateTo(highlight, "PositionX", (float)b.X); a.AnimateTo(highlight, "SizeWidth", (float)b.W); Play(a); }
        else { highlight.Position2D = new Position2D(b.X, (BarH - ItemH) / 2); highlight.Size2D = new Size2D(b.W, ItemH); }
    }
    int BarItemCentre(string action) { var b = bar.Find(i => i.Action == action); return b == null ? 960 : capsule.Position2D.X + b.X + b.W / 2; }
    void ActivateBar()
    {
        string a = bar[barSel].Action;
        if (a.StartsWith("hdmi:")) {
            int t = int.Parse(a.Substring(5));
            if (screen.BarWatching(t)) CloseOverlay("bar:watching"); else SelectSource(t);
        }
        else if (a == "inputs") OpenInputs();
        else if (a == "apps") OpenApps();
        else if (a == "controls") OpenControls();
        else if (a == "info") OpenInfo();
    }
    // back to the bar from a menu or panel, focus on the item that opened it
    void BackToBar(string from) { OpenBar(false); int i = bar.FindIndex(b => b.Action == from); if (i >= 0) { barSel = i; FocusBar(false); } }

    void OverlayKey(string k, bool arrow)
    {
        OverlayActivity();
        bool back = IsBack(k), sel = IsSelect(k);
        switch (mode) {
            case Mode.Bar:
                // over the wallpaper Back goes to the picture (the last input); over the picture it closes the bar
                if (back) { screen.BarBack(); return; }
                if (k == "Left" || k == "Right") { int n = Math.Clamp(barSel + (k == "Left" ? -1 : 1), 0, bar.Count - 1); if (n != barSel) { barSel = n; FocusBar(true); } }
                else if (sel || k == "Up" && !bar[barSel].Action.StartsWith("hdmi:")) ActivateBar();
                return;
            case Mode.Inputs: InputsKey(k, back, sel); return;
            case Mode.Apps: AppsKey(k, back, sel, arrow); return;
            case Mode.Controls: case Mode.Info: PanelKey(k, back, sel); return;
            case Mode.Setup: if (back) CloseSetup(); return;
        }
    }
    // the TV changed underneath (input, signal, names): refresh what is showing
    void OnTvChanged()
    {
        Note("tv_changed");
        if (!overlayShown) return;
        if (mode == Mode.Bar && BarSignature() != barSig) OpenBar(true);
        else if (mode == Mode.Inputs) OpenInputs();
        else if (mode == Mode.Controls || mode == Mode.Info) UpdatePanel();
    }
    string FocusName() => !overlayShown ? "" : mode == Mode.Bar && barSel >= 0 && barSel < bar.Count ? bar[barSel].Action :
        (mode == Mode.Controls || mode == Mode.Info) && rowSel >= 0 && rowSel < rows.Count ? rows[rowSel].Name : mode.ToString();
}
