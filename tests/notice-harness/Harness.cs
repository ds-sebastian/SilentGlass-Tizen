using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

static class NoticeHarness
{
    static int Main(string[] args)
    {
        var l = new TcpListener(IPAddress.Loopback, 7676); l.Start();
        Console.Error.WriteLine("listening");
        for (int served = 0; served < (args.Length > 0 ? int.Parse(args[0]) : 2); served++) {
            using var c = l.AcceptTcpClient(); var s = c.GetStream();
            var req = HttpRequest.Read(s, 12 << 20);
            HttpRequest.Respond(s, 200);
            if (req == null || req.Method != "POST") { Console.WriteLine(JsonSerializer.Serialize(new { method = req?.Method })); continue; }
            var n = Bridge.BuildNotice(Bridge.ParseMultipart(req.Body, req.Boundary));
            Console.WriteLine(JsonSerializer.Serialize(new { method = "POST", n.Title, n.Message, n.Size, n.Position, n.Duration, n.Clear, n.Ext, n.W, n.H, image = n.Image?.Length ?? 0 }));
        }
        Console.WriteLine(JsonSerializer.Serialize(new {
            app_hidden = Bridge.OnScreenApp("com.samsung.tv.bixby-promotion", "ui=none\nbackdrop=hidden\nfg=org.tizen.tv-viewer\n"),
            app_shown = Bridge.OnScreenApp("com.samsung.tv.bixby-promotion", "ui=none\nbackdrop=shown\nfg=org.tizen.tv-viewer\n"),
            app_bar = Bridge.OnScreenApp("com.samsung.tv.bixby-promotion", "ui=bar\nbackdrop=hidden\nfg=org.tizen.tv-viewer\n"),
            game = new[] { "Auto", "On", "Off" }.Select(Shared.GameName).ToArray(),
            names = Bridge.Unique(Shared.ParseInputs("[{\"sourceType\":13,\"editName\":\"Shield\"},{\"sourceType\":15,\"editName\":\"Shield\"}]", out _)).Select(i => i.Name).ToArray(),
            prefs = Prefs(),
            slugs = new[] { "Living Room TV", "  Den / TV #2 ", "Téléviseur", "" }.Select(Bridge.Slug).ToArray(),
            picture_only_dropped = Bridge.ApplyPrefs(new Bridge.NoticeData { Image = new byte[600], Ext = "jpg" }, new() { ["notify_images"] = "0" }) == null,
            quiet = new[] { ("22:00-07:00", 23), ("22:00-07:00", 12), ("22:00-07:00", 6), ("09:00-17:00", 18), ("", 3) }
                .Select(q => Bridge.InQuietHours(q.Item1, TimeSpan.FromHours(q.Item2))).ToArray() }));
        return 0;
    }
    static object[] Prefs()
    {
        var n = Bridge.ApplyPrefs(new Bridge.NoticeData { Title = "t", Image = new byte[600], Ext = "jpg", W = 4, H = 3 },
            new() { ["notify_position"] = "top-left", ["notify_size"] = "small", ["notify_duration"] = "30", ["notify_images"] = "0", ["notify_clear"] = "25" });
        return new object[] { n.Position, n.Size, n.Duration, n.Image?.Length ?? 0, n.Clear };
    }
}
