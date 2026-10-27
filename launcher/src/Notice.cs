using System;
using System.IO;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;

// Notifications on any input (tvOS-style banner) and short toasts. The Home Assistant bridge accepts the
// "Notifications for Android TV" protocol, drops anything while the screen is off or Ambient is up, and hands the
// rest over as data/notice.json (+ the image next to it). SilentGlass takes it at once (data watcher) - or, if it is
// not running, the bridge starts it with sg_notice=1 and it starts hidden. Taking the file is the acknowledgement.
// The banner is its own small notification-level window that never takes focus: the remote keeps controlling what is
// on screen, and the picture is never touched. A newer notification replaces the one showing.
public sealed partial class SilentGlass
{
    Window nw; View nCard; Tizen.NUI.Timer nTimer; Animation nAnimation;
    string noticeState = "none";
    const int NoticeR = 36, NoticePad = 14, NoticeTextPad = 26, NoticeMaxAgeMs = 30000;

    static string NoticeFile => DataFile(Shared.NoticeFile);
    // a notice waiting at start-up means the bridge started us for it (we stay hidden)
    static bool NoticePending() { try { var f = new FileInfo(NoticeFile); return f.Exists && (DateTime.UtcNow - f.LastWriteTimeUtc).TotalSeconds < 15; } catch { return false; } }
    void TakeNotice(string via)
    {
        Logic.Json n;
        try { if (!File.Exists(NoticeFile)) return; n = new Logic.Json(File.ReadAllText(NoticeFile)); File.Delete(NoticeFile); } catch { return; }
        if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - n.Long("at") > NoticeMaxAgeMs) { noticeState = "stale"; return; }
        string image = n["image"];
        // only a bare file name in our own data directory
        if (image.Contains('/') || image.Contains("..") || image.Length > 0 && !File.Exists(DataFile(image))) image = "";
        if (n["title"].Length == 0 && n["message"].Length == 0 && image.Length == 0) return;
        ShowNotice(n["title"], n["message"], n["size"], n["position"], image, n.Int("w"), n.Int("h"), n.Int("duration"));
        noticeState = "shown:" + via; Note("notice:" + via);
    }
    // an input plugged in or unplugged: a small banner in the corner
    void Toast(string text) { if (NoticesOn) ShowNotice(text, "", "small", "top-right", "", 0, 0, 4); }

    void EnsureNoticeWindow()
    {
        if (nw != null) return;
        nw = new Window("SilentGlass Notice", new Rectangle(1920 - Logic.NoticeMargin - 640, 600, 640, 400), true);
        nw.Hide();
        nw.Type = WindowType.Notification;
        nw.SetNotificationLevel(NotificationLevel.Top);
        nw.AddAuxiliaryHint("wm.policy.win.user.geometry", "1");
        nw.AddAuxiliaryHint("wm.comp.win.effect.enable", "0");
        nw.BackgroundColor = Clear;
        nw.SetAcceptFocus(false);  // never takes the remote away from what is on screen
    }
    void ShowNotice(string titleText, string message, string size, string position, string image, int iw0, int ih0, int seconds)
    {
        EnsureNoticeWindow();
        StopAnimation(ref nAnimation);
        if (nCard != null) { nw.Remove(nCard); nCard.Dispose(); nCard = null; }
        int w = Logic.NoticeWidth(size), inner = w - 2 * NoticePad, tw = w - 2 * NoticeTextPad;
        bool img = image.Length > 0; int ih = img ? Logic.NoticeImageHeight(inner, iw0, ih0) : 0;
        var title = Label(titleText, NoticeTextPad, 0, tw, 10, 30, White, "Semibold"); title.MultiLine = true;
        var msg = Label(message, NoticeTextPad, 0, tw, 10, 25, Faint); msg.MultiLine = true;
        int th = titleText.Length > 0 ? (int)Math.Ceiling(title.GetHeightForWidth(tw)) : 0, mh = message.Length > 0 ? Math.Min((int)Math.Ceiling(msg.GetHeightForWidth(tw)), 3 * 34) : 0;
        int y = img ? NoticePad + ih + 20 : 24;
        title.Position2D = new Position2D(NoticeTextPad, y); title.Size2D = new Size2D(tw, Math.Max(th, 1)); y += th + (th > 0 && mh > 0 ? 4 : 0);
        msg.Position2D = new Position2D(NoticeTextPad, y); msg.Size2D = new Size2D(tw, Math.Max(mh, 1)); y += mh + 24;
        int h = y;
        nCard = Glass(0, 0, w, h, NoticeR, true);
        if (img) {
            var pic = new ImageView { ResourceUrl = DataFile(image), Position2D = new Position2D(NoticePad, NoticePad), Size2D = new Size2D(inner, ih), CornerRadius = Round(NoticeR - NoticePad) };
            // crop to fill (centre), from the size the bridge read from the image header
            if (iw0 > 0 && ih0 > 0) {
                double want = (double)ih / inner, have = (double)ih0 / iw0;
                if (have > want) { float f = (float)(want / have); pic.PixelArea = new RelativeVector4(0, (1 - f) / 2, 1, f); }
                else if (have < want) { float f = (float)(have / want); pic.PixelArea = new RelativeVector4((1 - f) / 2, 0, f, 1); }
            }
            nCard.Add(pic);
        }
        if (titleText.Length > 0) nCard.Add(title); else title.Dispose();
        if (message.Length > 0) nCard.Add(msg); else msg.Dispose();
        var (x, oy) = Logic.NoticeOrigin(position, w, h);
        try { nw.WindowPositionSize = new Rectangle(x, oy, w, h); } catch { }
        nw.Add(nCard);
        int from = position.StartsWith("top") ? -16 : 16;  // in from the nearer edge
        nCard.Opacity = 0; nCard.Position2D = new Position2D(0, from);
        nAnimation = new Animation(240) { DefaultAlphaFunction = Ease() };
        nAnimation.AnimateTo(nCard, "Opacity", 1f); nAnimation.AnimateTo(nCard, "PositionY", 0f);
        nw.Show(); nw.Raise(); nAnimation.Play();
        nTimer?.Stop(); nTimer = new Tizen.NUI.Timer((uint)(Math.Clamp(seconds <= 0 ? 5 : seconds, 3, 60) * 1000));
        nTimer.Tick += (s, e) => { HideNotice(); return false; }; nTimer.Start();
    }
    void HideNotice()
    {
        if (nCard == null) return;
        StopAnimation(ref nAnimation);
        var card = nCard;
        nAnimation = new Animation(300) { DefaultAlphaFunction = Ease() };
        nAnimation.AnimateTo(card, "Opacity", 0f);
        nAnimation.Finished += (s, e) => { if (nCard != card) return; try { nw.Hide(); } catch { } nw.Remove(card); card.Dispose(); nCard = null; noticeState = "hidden"; };
        nAnimation.Play();
    }
}
