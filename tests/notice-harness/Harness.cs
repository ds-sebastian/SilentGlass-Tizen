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
            Console.WriteLine(JsonSerializer.Serialize(new { method = "POST", n.Title, n.Message, n.Size, n.Position, n.Duration, n.Ext, n.W, n.H, image = n.Image?.Length ?? 0 }));
        }
        Console.WriteLine(JsonSerializer.Serialize(new {
            app_hidden = Bridge.OnScreenApp("com.samsung.tv.bixby-promotion", "ui=none\nbackdrop=hidden\nfg=org.tizen.tv-viewer\n"),
            app_shown = Bridge.OnScreenApp("com.samsung.tv.bixby-promotion", "ui=none\nbackdrop=shown\nfg=org.tizen.tv-viewer\n"),
            app_bar = Bridge.OnScreenApp("com.samsung.tv.bixby-promotion", "ui=bar\nbackdrop=hidden\nfg=org.tizen.tv-viewer\n"),
            game = new[] { "Auto", "On", "Off" }.Select(Bridge.GameName).ToArray() }));
        return 0;
    }
}
