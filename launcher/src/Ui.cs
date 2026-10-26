using System;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;

// The look, in one place. Over live video nothing can be blurred, so glass is a dark translucent material with a
// hairline edge and a faint top light, on a soft dimming gradient (Apple's guidance for controls over bright media);
// text panels (Info, the setup sheet, notifications) are near-opaque. Focus is a white capsule with dark ink.
// tools/overlay_mockup.py draws the same numbers on the Pi.
public sealed partial class SilentGlass
{
    static readonly Color White = new(1, 1, 1, 1), Clear = new(0, 0, 0, 0), Ink = new(.11f, .11f, .12f, 1), Live = new(.19f, .82f, .35f, 1),
        Amber = new(1f, .62f, .04f, 1), Grey = new(.56f, .56f, .58f, 1), Faint = new(1, 1, 1, .62f), FaintInk = new(.11f, .11f, .12f, .6f),
        FocusFill = new(1, 1, 1, .95f), Edge = new(1, 1, 1, .16f), Hairline = new(1, 1, 1, .14f),
        GlassTop = new(.15f, .15f, .17f, .70f), GlassBottom = new(.115f, .115f, .13f, .66f), Solid = new(.086f, .086f, .10f, .95f);

    static AlphaFunction Ease() => new(AlphaFunction.BuiltinFunctions.EaseOutSine);
    // StopFinal: an interrupted animation lands on its end values (a half-faded focus never sticks)
    static void StopAnimation(ref Animation a) { if (a == null) return; a.EndAction = Animation.EndActions.StopFinal; a.Stop(); a.Dispose(); a = null; }
    static void Play(Animation a) { a.Finished += (s, e) => a.Dispose(); a.Play(); }

    static TextLabel Label(string text, int x, int y, int w, int h, float px, Color color, string face = "Medium") =>
        new() { Text = text, Position2D = new Position2D(x, y), Size2D = new Size2D(w, h), PixelSize = px, FontFamily = "Quiet Inter " + face,
                TextColor = color, VerticalAlignment = VerticalAlignment.Center, Ellipsis = false };
    static int TextWidth(TextLabel l) { try { return (int)Math.Ceiling(l.NaturalSize.Width); } catch { return l.Text.Length * 16; } }
    static ImageView Sym(string name, int x, int y, int s) =>
        new() { ResourceUrl = Res + "sym-" + name + ".svg", Position2D = new Position2D(x, y), Size2D = new Size2D(s, s), DesiredWidth = s * 2, DesiredHeight = s * 2 };
    static View Dot(int x, int y, int s, Color c) => new() { Position2D = new Position2D(x, y), Size2D = new Size2D(s, s), CornerRadius = new Vector4(s / 2f, s / 2f, s / 2f, s / 2f), BackgroundColor = c };
    static View Rule(int x, int y, int w) => new() { Position2D = new Position2D(x, y), Size2D = new Size2D(w, 1), BackgroundColor = Hairline };
    static Vector4 Round(float r) => new(r, r, r, r);
    // continuous (squircle) corners where a corner is a corner; never on circles or capsules
    static void Corners(View v, int r, int w, int h) { if (r > 0 && r * 2 < Math.Min(w, h)) v.CornerSquareness = new Vector4(.6f, .6f, .6f, .6f); }

    static PropertyMap Gradient(bool horizontal, float r, params (float at, Color c)[] stops)
    {
        var s = new PropertyArray(); var o = new PropertyArray();
        foreach (var (at, c) in stops) { o.PushBack(new PropertyValue(at)); s.PushBack(new PropertyValue(new Vector4(c.R, c.G, c.B, c.A))); }
        var m = new PropertyMap();
        m.Add(Visual.Property.Type, new PropertyValue((int)Visual.Type.Gradient));
        m.Add(GradientVisualProperty.StartPosition, new PropertyValue(horizontal ? new Vector2(-.5f, 0) : new Vector2(0, -.5f)));
        m.Add(GradientVisualProperty.EndPosition, new PropertyValue(horizontal ? new Vector2(.5f, 0) : new Vector2(0, .5f)));
        m.Add(GradientVisualProperty.StopOffset, new PropertyValue(o));
        m.Add(GradientVisualProperty.StopColor, new PropertyValue(s));
        if (r > 0) m.Add(Visual.Property.CornerRadius, new PropertyValue(Round(r)));
        return m;
    }
    // glass: translucent over video (bar, menus, Controls), or near-opaque for reading (Info, setup, notifications)
    static View Glass(int x, int y, int w, int h, int r, bool solid = false)
    {
        var v = new View { Position2D = new Position2D(x, y), Size2D = new Size2D(w, h), CornerRadius = Round(r), BorderlineWidth = 1.5f, BorderlineColor = Edge, BorderlineOffset = -1 };
        if (solid) v.BackgroundColor = Solid; else v.Background = Gradient(false, r, (0f, GlassTop), (.4f, GlassBottom), (1f, GlassBottom));
        Corners(v, r, w, h);
        return v;
    }
    // soft dimming behind the glass (from transparent to `max` black)
    static View Dimming(bool horizontal, int x, int y, int w, int h, float max) =>
        new() { Position2D = new Position2D(x, y), Size2D = new Size2D(w, h), Background = Gradient(horizontal, 0, (0f, Clear), (1f, new Color(0, 0, 0, max))) };
    // iOS switch: green track when on; the knob slides
    sealed class Switch { public View Track, Knob; }
    static Switch NewSwitch(int x, int y)
    {
        var s = new Switch { Track = new View { Position2D = new Position2D(x, y), Size2D = new Size2D(64, 36), CornerRadius = Round(18) },
                             Knob = new View { Size2D = new Size2D(30, 30), CornerRadius = Round(15), BackgroundColor = White } };
        s.Track.Add(s.Knob); return s;
    }
    static void SetSwitch(Switch s, bool on) { s.Track.BackgroundColor = on ? Live : new Color(.47f, .47f, .5f, .5f); s.Knob.Position2D = new Position2D(on ? 31 : 3, 3); }
    static void FadeIn(View v, int dy, int ms = 170)
    {
        int y = v.Position2D.Y; v.Opacity = 0; v.Position2D = new Position2D(v.Position2D.X, y + dy);
        var a = new Animation(ms) { DefaultAlphaFunction = Ease() };
        a.AnimateTo(v, "Opacity", 1f); a.AnimateTo(v, "PositionY", (float)y); Play(a);
    }
    static Color HexColor(string hex)
    {
        try { int n = Convert.ToInt32((hex ?? "").TrimStart('#'), 16); return new Color(((n >> 16) & 255) / 255f, ((n >> 8) & 255) / 255f, (n & 255) / 255f, 1); }
        catch { return new Color(.2f, .2f, .22f, 1); }
    }
    // an app's art at w x h (cover, contain or crop as the catalog says)
    static ImageView AppArtwork(LibraryApp app, int w, int h, int radius)
    {
        bool cover = app.Fit == "cover", full = cover || app.Fit == "contain-full";
        int iw = full ? w : w - 54, ih = full ? h : h - 40; var crop = app.Crop;
        float cw = crop == null ? 1 : crop[2], ch = crop == null ? 1 : crop[3];
        var image = new ImageView {
            Position2D = new Position2D((w - iw) / 2, (h - ih) / 2), Size2D = new Size2D(iw, ih),
            FittingMode = cover ? FittingModeType.ScaleToFill : FittingModeType.ShrinkToFit, SamplingMode = SamplingModeType.BoxThenLanczos,
            DesiredWidth = (int)Math.Ceiling(iw * 2 / cw), DesiredHeight = (int)Math.Ceiling(ih * 2 / ch),
            ReleasePolicy = ReleasePolicyType.Destroyed, CornerRadius = Round(radius), ResourceUrl = Res + app.Asset };
        if (crop != null) image.PixelArea = new RelativeVector4(crop[0], crop[1], crop[2], crop[3]);
        return image;
    }
}
