using System;
using System.Collections.Generic;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;

// Menus that open above the bar (the bar stays, its item marked): Inputs, anchored over its bar item, and Apps, the
// TV's apps floating above the bar - their own menu, so the bar stays short.
public sealed partial class SilentGlass
{
    const int MenuRowH = 88, PopW = 560, PopR = 36, AppW = 256, AppH = 144, AppGap = 24, AppCols = 4, MaxApps = 12;
    sealed class MenuRow { public View Box; public ImageView Glyph, Check; public TextLabel Name, Port; public int Type; }
    readonly List<MenuRow> menu = new();
    readonly List<(View card, TextLabel name, LibraryApp app)> tiles = new();
    int menuSel;
    View popover;
    static string PortGlyph(int type, int watch, int play) => type == watch ? "tv" : type == play ? "gamecontroller" : type == 0 ? "antenna" : "hdmi";
    void CloseMenu() { if (popover == null) return; oLayer.Remove(popover); popover.Dispose(); popover = null; menu.Clear(); tiles.Clear(); }

    // ---- Inputs: every input by the TV's name for it; a checkmark on the current one, a green dot when it has a picture ----
    void OpenInputs()
    {
        int keep = mode == Mode.Inputs && menuSel < menu.Count ? menu[menuSel].Type : lastSource;
        if (mode != Mode.Bar) OpenBar(true);
        mode = Mode.Inputs; FocusBar(false); CloseMenu(); RefreshInputs();
        int h = 24 + inputs.Count * MenuRowH, x = Math.Clamp(BarItemCentre("inputs") - PopW / 2, 40, 1880 - PopW), y = BarTop - PopGap - h;
        popover = Glass(x, y, PopW, h, PopR); oLayer.Add(popover);
        foreach (var s in inputs) {
            bool cur = s.Type == lastSource, off = s.Known && !s.Connected && !cur, sub = s.Name != s.Port;
            var r = new MenuRow { Type = s.Type, Box = new View { Position2D = new Position2D(12, 12 + menu.Count * MenuRowH), Size2D = new Size2D(PopW - 24, MenuRowH), CornerRadius = Round(26), Opacity = off ? .45f : 1 } };
            r.Glyph = Sym(PortGlyph(s.Type, watchPort, playPort), 22, (MenuRowH - GlyphS) / 2, GlyphS); r.Box.Add(r.Glyph);
            r.Name = Label(s.Name, 80, sub ? 10 : 0, PopW - 24 - 80 - 110, sub ? 44 : MenuRowH, 30, White, "Semibold"); r.Box.Add(r.Name);
            if (sub) { r.Port = Label(s.Port, 80, 50, 300, 30, 22, Faint); r.Box.Add(r.Port); }
            if (cur && HasPicture) r.Box.Add(Dot(PopW - 24 - 92, (MenuRowH - 12) / 2, 12, Live));
            if (cur) { r.Check = Sym("check", PopW - 24 - 62, (MenuRowH - 34) / 2, 34); r.Box.Add(r.Check); }
            popover.Add(r.Box); menu.Add(r);
        }
        menuSel = Math.Max(0, menu.FindIndex(r => r.Type == keep));
        FocusMenu(); FadeIn(popover, 16); Note("inputs");
    }
    void FocusMenu()
    {
        for (int i = 0; i < menu.Count; i++) {
            var r = menu[i]; bool f = i == menuSel; var ink = f ? Ink : White;
            r.Box.BackgroundColor = f ? FocusFill : Clear; r.Name.TextColor = ink; r.Glyph.Color = ink;
            if (r.Port != null) r.Port.TextColor = f ? FaintInk : Faint;
            if (r.Check != null) r.Check.Color = ink;
        }
    }
    void InputsKey(string k, bool back, bool sel)
    {
        if (back || k == "Down" && menuSel == menu.Count - 1) { BackToBar("inputs"); return; }
        if (k == "Up" || k == "Down") { menuSel = Math.Clamp(menuSel + (k == "Up" ? -1 : 1), 0, menu.Count - 1); FocusMenu(); }
        else if (sel && menuSel < menu.Count) SelectSource(menu[menuSel].Type);
    }

    // ---- Apps: artwork floating above the bar, no panel behind it ----
    void OpenApps()
    {
        mode = Mode.Apps; FocusBar(false); CloseMenu();
        var apps = library.Count > MaxApps ? library.GetRange(0, MaxApps) : library;
        int n = apps.Count, lines = (n + AppCols - 1) / AppCols, cols = Math.Min(AppCols, n);
        int w = cols * AppW + (cols - 1) * AppGap, h = lines * (AppH + 50) + (lines - 1) * 18, x0 = 960 - w / 2, y0 = BarTop - PopGap - 20 - h;
        if (oDim != null) { oDim.Position2D = new Position2D(0, BandTop); oDim.Size2D = new Size2D(1920, 1080 - BandTop); }  // dim further up for the names
        popover = new View { Size2D = new Size2D(1920, 1080) }; oLayer.Add(popover);
        for (int i = 0; i < n; i++) {
            var app = apps[i]; int x = x0 + (i % AppCols) * (AppW + AppGap), y = y0 + (i / AppCols) * (AppH + 50 + 18);
            var card = new View { Position2D = new Position2D(x + AppW / 2, y + AppH / 2), Size2D = new Size2D(AppW, AppH), PivotPoint = new Position(.5f, .5f, .5f),
                                  PositionUsesPivotPoint = true, BackgroundColor = HexColor(app.Background), CornerRadius = Round(20), BorderlineColor = White };
            Corners(card, 20, AppW, AppH); card.Add(AppArtwork(app, AppW, AppH, 20));
            var name = Label(app.Name, x - 20, y + AppH + 10, AppW + 40, 34, 22, new Color(1, 1, 1, .86f), "Semibold"); name.HorizontalAlignment = HorizontalAlignment.Center;
            popover.Add(card); popover.Add(name); tiles.Add((card, name, app));
        }
        menuSel = 0; FocusApps(false); FadeIn(popover, 16); Note("apps");
    }
    void FocusApps(bool animate)
    {
        for (int i = 0; i < tiles.Count; i++) {
            var (card, name, _) = tiles[i]; bool f = i == menuSel;
            card.BorderlineWidth = f ? 4 : 0; name.TextColor = f ? White : new Color(1, 1, 1, .8f);
            var sc = f ? new Vector3(1.07f, 1.07f, 1) : new Vector3(1, 1, 1);
            if (animate) { var a = new Animation(150) { DefaultAlphaFunction = Ease() }; a.AnimateTo(card, "Scale", sc); Play(a); } else card.Scale = sc;
            if (f) card.RaiseToTop();
        }
    }
    void AppsKey(string k, bool back, bool sel, bool arrow)
    {
        if (back) { BackToBar("apps"); return; }
        if (sel && menuSel < tiles.Count) { OpenApp(tiles[menuSel].app.Id); return; }
        if (!arrow) return;
        int lines = (tiles.Count + AppCols - 1) / AppCols;
        if (k == "Down" && menuSel / AppCols == lines - 1) { BackToBar("apps"); return; }
        int n = menuSel + (k == "Left" ? -1 : k == "Right" ? 1 : k == "Up" ? -AppCols : AppCols);
        if (n >= 0 && n < tiles.Count) { menuSel = n; FocusApps(true); }
    }
}
