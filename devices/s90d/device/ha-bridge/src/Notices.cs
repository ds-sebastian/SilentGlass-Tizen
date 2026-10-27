using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

static partial class Bridge
{
    // ---- notifications: the "Notifications for Android TV" protocol (Home Assistant nfandroidtv, library 0.1.5):
    // GET / is the connection check; POST / is multipart/form-data with msg, title, duration (s), fontsize (1 small,
    // 0 medium, 2 large, 3 max), position (0 bottom-right, 1 bottom-left, 2 top-right, 3 top-left, 4 center),
    // transparency, bkgcolor, interrupt, filename (icon) and filename2 (image). Shown by SilentGlass as a banner. ----
    const int NoticePort = 7676, NoticeMaxBody = 12 << 20;
    static readonly object noticeGate = new();
    static long noticeSeq;
    static bool NoticesOn => !File.Exists(DataFile(Shared.NotifyOff));
    static bool NoticeAllowed(IPAddress peer)
    {
        if (IPAddress.IsLoopback(peer)) return true;
        foreach (var h in (conf.GetValueOrDefault("host", "") + "," + conf.GetValueOrDefault("notify_from", "")).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            try { if (IPAddress.TryParse(h, out var ip) ? ip.Equals(peer) : Dns.GetHostAddresses(h).Any(a => a.Equals(peer))) return true; } catch { }
        return false;
    }
    static void StartNoticeServer() => new Thread(() => {
        while (true) {
            TcpListener l = null;
            try {
                l = new TcpListener(IPAddress.Any, NoticePort); l.Start(); Log("notifications: listening on " + NoticePort);
                while (true) { var c = l.AcceptTcpClient(); ThreadPool.QueueUserWorkItem(_ => ServeNotice(c)); }
            } catch (Exception e) { Log("notifications: listener " + e.GetType().Name + ": " + e.Message); }
            try { l?.Stop(); } catch { }
            Thread.Sleep(30000);
        }
    }) { IsBackground = true, Name = "notices" }.Start();
    static void ServeNotice(TcpClient c)
    {
        using (c) try {
            var peer = ((IPEndPoint)c.Client.RemoteEndPoint).Address; if (peer.IsIPv4MappedToIPv6) peer = peer.MapToIPv4();
            if (!NoticeAllowed(peer)) { Log("notifications: refused " + peer); return; }
            var s = c.GetStream(); s.ReadTimeout = 10000; s.WriteTimeout = 5000;
            var req = HttpRequest.Read(s, NoticeMaxBody);
            if (req == null) return;
            HttpRequest.Respond(s, req.Method == "GET" || req.Method == "POST" ? 200 : 405);
            if (req.Method == "POST") lock (noticeGate) HandleNotice(ApplyPrefs(BuildNotice(ParseMultipart(req.Body, req.Boundary)), conf));
        } catch (Exception e) { Log("notifications: " + e.GetType().Name + ": " + e.Message); }
    }
    public sealed class NoticeData { public string Title = "", Message = "", Size = "large", Position = "bottom-right", Ext = ""; public int Duration = 5, W, H; public byte[] Image; }
    public static NoticeData BuildNotice(Dictionary<string, byte[]> parts)
    {
        string T(string k, int max) { if (!parts.TryGetValue(k, out var b)) return ""; var s = Regex.Replace(Encoding.UTF8.GetString(b), @"[\x00-\x08\x0b-\x1f\x7f]", "").Trim(); return s.Length > max ? s.Substring(0, max) : s; }
        int N(string k, int d) => int.TryParse(T(k, 6), out var v) ? v : d;
        var n = new NoticeData { Title = T("title", 120), Message = T("msg", 400), Duration = Math.Clamp(N("duration", 5), 1, 300) };
        n.Size = N("fontsize", 2) switch { 1 => "small", 0 => "medium", 3 => "max", _ => "large" };
        n.Position = N("position", 0) switch { 1 => "bottom-left", 2 => "top-right", 3 => "top-left", 4 => "center", _ => "bottom-right" };
        // the image; the icon only when it is a real picture (the library always sends a 1x1 placeholder)
        foreach (var k in new[] { "filename2", "filename" })
            if (parts.TryGetValue(k, out var img) && img.Length > 512 && ImageType(img) is var ext && ext != null) {
                n.Image = img; n.Ext = ext; (n.W, n.H) = ImageSize(img); break;
            }
        return n.Title.Length > 0 || n.Message.Length > 0 || n.Image != null ? n : null;
    }
    // ---- the owner's notification settings (Advanced settings › Notifications; silentglass-ha.conf). Each one empty
    // means "as Home Assistant sent it". ----
    public static readonly string[] NoticePositions = { "bottom-right", "bottom-left", "top-right", "top-left", "center" };
    public static readonly string[] NoticeSizes = { "small", "medium", "large" };
    public static readonly int[] NoticeDurations = { 5, 10, 20, 30 };
    public static bool ValidQuiet(string spec) => spec.Length == 0 || Regex.IsMatch(spec, @"^([01]\d|2[0-3]):[0-5]\d-([01]\d|2[0-3]):[0-5]\d$");
    public static NoticeData ApplyPrefs(NoticeData n, Dictionary<string, string> c)
    {
        if (n == null) return null;
        string pos = c.GetValueOrDefault("notify_position", ""), size = c.GetValueOrDefault("notify_size", "");
        if (NoticePositions.Contains(pos)) n.Position = pos;
        if (NoticeSizes.Contains(size)) n.Size = size;
        if (int.TryParse(c.GetValueOrDefault("notify_duration", ""), out int d) && NoticeDurations.Contains(d)) n.Duration = d;
        if (c.GetValueOrDefault("notify_images", "1") == "0") { n.Image = null; n.Ext = ""; n.W = n.H = 0; }
        return n.Title.Length > 0 || n.Message.Length > 0 || n.Image != null ? n : null;  // a picture-only notice with pictures off
    }
    // "22:00-07:00" (may wrap past midnight); empty = never quiet
    public static bool InQuietHours(string spec, TimeSpan now)
    {
        if (string.IsNullOrEmpty(spec) || !ValidQuiet(spec)) return false;
        var p = spec.Split('-'); var from = TimeSpan.Parse(p[0], CultureInfo.InvariantCulture); var to = TimeSpan.Parse(p[1], CultureInfo.InvariantCulture);
        return from == to ? false : from < to ? now >= from && now < to : now >= from || now < to;
    }
    public static string ImageType(byte[] b) =>
        b.Length > 12 && b[0] == 0xFF && b[1] == 0xD8 ? "jpg" : b.Length > 24 && b[0] == 0x89 && b[1] == (byte)'P' && b[2] == (byte)'N' && b[3] == (byte)'G' ? "png" :
        b.Length > 10 && b[0] == (byte)'G' && b[1] == (byte)'I' && b[2] == (byte)'F' ? "gif" :
        b.Length > 12 && Encoding.ASCII.GetString(b, 0, 4) == "RIFF" && Encoding.ASCII.GetString(b, 8, 4) == "WEBP" ? "webp" : null;
    // pixel size from the header (JPEG SOFn, PNG IHDR, GIF screen); (0,0) when unknown
    public static (int w, int h) ImageSize(byte[] b)
    {
        try {
            switch (ImageType(b)) {
                case "png": return ((b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19], (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23]);
                case "gif": return (b[6] | (b[7] << 8), b[8] | (b[9] << 8));
                case "jpg":
                    for (int i = 2; i + 9 < b.Length;) {
                        if (b[i] != 0xFF) { i++; continue; }
                        int m = b[i + 1];
                        if (m == 0xFF) { i++; continue; }
                        if (m == 0xD8 || m == 0x01 || (m >= 0xD0 && m <= 0xD7)) { i += 2; continue; }
                        int len = (b[i + 2] << 8) | b[i + 3];
                        if (m >= 0xC0 && m <= 0xCF && m != 0xC4 && m != 0xC8 && m != 0xCC) return ((b[i + 7] << 8) | b[i + 8], (b[i + 5] << 8) | b[i + 6]);
                        i += 2 + len;
                    }
                    break;
            }
        } catch { }
        return (0, 0);
    }
    // hand the notice to SilentGlass: data/notice.json (+ image). Taking the file is its acknowledgement.
    // Returns why it was not shown, or null. A test (from the phone page) ignores the on/off switch and quiet hours.
    static string HandleNotice(NoticeData n, bool test = false)
    {
        if (n == null) { Log("notifications: empty request (or a picture with pictures off)"); return "nothing to show"; }
        string fg = VStr(ForegroundKey), what = "'" + (n.Title.Length > 0 ? n.Title : n.Message.Length > 40 ? n.Message.Substring(0, 40) : n.Message) + "'";
        string why = !test && !NoticesOn ? "notifications are off" : !ScreenOn ? "the TV is off" : Shared.IsAmbient(fg) ? "Ambient is on" :
            !test && InQuietHours(conf.GetValueOrDefault("notify_quiet", ""), DateTime.Now.TimeOfDay) ? "quiet hours" : null;
        if (why != null) { Log("notifications: " + what + " not shown (" + why + ")"); return why; }
        foreach (var f in Directory.GetFiles(Data, "notice-*")) try { File.Delete(f); } catch { }
        long id = ++noticeSeq; string image = n.Image != null ? "notice-" + id + "." + n.Ext : "";
        if (image.Length > 0) File.WriteAllBytes(DataFile(image), n.Image);
        var json = JsonSerializer.Serialize(new Dictionary<string, object> {
            ["title"] = n.Title, ["message"] = n.Message, ["size"] = n.Size, ["position"] = n.Position, ["duration"] = n.Duration,
            ["image"] = image, ["w"] = n.W, ["h"] = n.H, ["at"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
        string file = DataFile(Shared.NoticeFile);
        File.WriteAllText(file + ".tmp", json); File.Move(file + ".tmp", file, true);
        bool Taken(int ms) { for (int i = 0; i < ms / 100 && File.Exists(file); i++) Thread.Sleep(100); return !File.Exists(file); }
        if (Taken(1500)) { Log("notifications: " + what + " shown"); return null; }
        // SilentGlass is not running (it is closed in standby): start it for the notice - only over the TV viewer,
        // where starting it hidden is invisible; over another app it would send that app to the background
        string onScreen = OnScreenApp(fg);
        if (onScreen != Shared.Viewer && fg != Shared.Self) { File.Delete(file); Log("notifications: " + what + " not shown (SilentGlass not running over " + onScreen + ")"); return "an app is on screen and SilentGlass is not running"; }
        LaunchApp(Shared.Self, Shared.ExtraNotice, "1");
        bool taken = Taken(8000);
        Log("notifications: " + what + (taken ? " shown (started SilentGlass)" : " NOT taken"));
        if (File.Exists(file)) File.Delete(file);
        return taken ? null : "SilentGlass did not answer";
    }
    // multipart/form-data body -> field name -> bytes
    public static Dictionary<string, byte[]> ParseMultipart(byte[] body, string boundary)
    {
        var parts = new Dictionary<string, byte[]>();
        if (body == null || string.IsNullOrEmpty(boundary)) return parts;
        var delim = Encoding.ASCII.GetBytes("--" + boundary);
        int at = IndexOf(body, delim, 0);
        while (at >= 0) {
            int start = at + delim.Length;
            if (start + 1 < body.Length && body[start] == '-' && body[start + 1] == '-') break;  // closing delimiter
            int head = IndexOf(body, Encoding.ASCII.GetBytes("\r\n\r\n"), start);
            if (head < 0) break;
            int next = IndexOf(body, Encoding.ASCII.GetBytes("\r\n--" + boundary), head + 4);
            if (next < 0) break;
            var headers = Encoding.UTF8.GetString(body, start, head - start);
            var name = Regex.Match(headers, "name=\"([^\"]*)\"");
            if (name.Success) parts[name.Groups[1].Value] = body[(head + 4)..next];
            at = next + 2;
        }
        return parts;
    }
    static int IndexOf(byte[] hay, byte[] needle, int from)
    {
        for (int i = Math.Max(0, from); i <= hay.Length - needle.Length; i++) {
            int j = 0; while (j < needle.Length && hay[i + j] == needle[j]) j++;
            if (j == needle.Length) return i;
        }
        return -1;
    }
}
