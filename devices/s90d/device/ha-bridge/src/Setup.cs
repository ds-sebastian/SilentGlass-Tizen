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
using System.Net.NetworkInformation;
using System.Net.WebSockets;
using System.Security.Cryptography;

// The phone setup page. SilentGlass asks for it (data/setup-request, from Info > Advanced settings) and shows the answer
// (data/setup.json: address + a fresh 6-digit code, then the progress) as a QR code. The page is served on port 7680
// only while that sheet is open - Back on the TV (data/setup-close), 10 minutes, or 8 wrong codes close it - and
// every API call must carry the code. Plain HTTP on the LAN, like the TV's own setup flows; the password is never
// sent back to the phone, and a Home Assistant token is used once to read the Thread network and then dropped.
static partial class Bridge
{
    static readonly int SetupPort = int.TryParse(Environment.GetEnvironmentVariable("SG_SETUP_PORT"), out var p) ? p : 7680;
    const int SetupMinutes = 10, SetupMaxFailures = 8;
    static readonly object setupGate = new();
    static TcpListener setupListener;
    static string setupCode = "", setupUrl = "", haTlv = "", haNetwork = "";
    static DateTime setupUntil;
    static int setupFailures;
    static bool setupPhone;

    // SilentGlass's requests arrive as files in its data folder: watched with inotify, so the bridge sleeps until one
    // comes (a 30 s check covers a missed event; 1 s if the watch could not start). While the page is open its
    // time limit is checked every 5 s.
    static FileSystemWatcher setupFiles;
    static readonly AutoResetEvent setupKick = new(false);
    static void StartSetupWatch() => new Thread(() => {
        bool watching = false;
        try {
            setupFiles = new FileSystemWatcher(Data) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite };
            setupFiles.Created += (s, e) => setupKick.Set(); setupFiles.Changed += (s, e) => setupKick.Set(); setupFiles.Renamed += (s, e) => setupKick.Set();
            setupFiles.EnableRaisingEvents = watching = true;
        } catch (Exception e) { Log("setup watch: " + e.Message + " (checking every second)"); }
        while (true) {
            try {
                if (File.Exists(DataFile(Shared.SetupClose))) { File.Delete(DataFile(Shared.SetupClose)); CloseSetup("closed on the TV"); }
                if (File.Exists(DataFile(Shared.SetupRequest))) { File.Delete(DataFile(Shared.SetupRequest)); OpenSetup(); }
                bool open, expired; lock (setupGate) { open = setupListener != null; expired = open && DateTime.UtcNow > setupUntil; }
                if (expired) { CloseSetup("timed out"); open = false; }
                setupKick.WaitOne(!watching ? 1000 : open ? 5000 : 30000);
            } catch (Exception e) { Log("setup watch: " + e.Message); Thread.Sleep(1000); }
        }
    }) { IsBackground = true, Name = "setup-watch" }.Start();

    static void OpenSetup()
    {
        lock (setupGate) {
            setupCode = RandomNumberGenerator.GetInt32(0, 1000000).ToString("D6");
            setupUntil = DateTime.UtcNow.AddMinutes(SetupMinutes); setupFailures = 0; setupPhone = false; haTlv = ""; haNetwork = "";
            setupUrl = "http://" + (LanAddress() ?? "?") + ":" + SetupPort + "/";
            if (setupListener == null) {
                try { setupListener = new TcpListener(IPAddress.Any, SetupPort); setupListener.Start(); }
                catch (Exception e) { setupListener = null; Log("setup listen: " + e.Message); SetupStatus("error", "Could not open the setup page (" + e.Message + ")."); return; }
                var l = setupListener;
                new Thread(() => SetupServe(l)) { IsBackground = true, Name = "setup" }.Start();
            }
        }
        Log("setup open");
        SetupStatus("waiting");
    }
    static void CloseSetup(string why)
    {
        lock (setupGate) {
            if (setupListener == null) return;
            try { setupListener.Stop(); } catch { }
            setupListener = null; setupCode = ""; haTlv = ""; haNetwork = "";
        }
        Log("setup closed: " + why);
        SetupStatus("closed");
    }
    static void SetupStatus(string state, string message = "")
    {
        string code, url; lock (setupGate) { code = setupCode; url = setupUrl; }
        var json = JsonSerializer.Serialize(new Dictionary<string, string> { ["state"] = state, ["url"] = state == "closed" ? "" : url, ["code"] = state == "closed" ? "" : code, ["message"] = message });
        string file = DataFile(Shared.SetupFile);
        try { File.WriteAllText(file + ".new", json); File.Move(file + ".new", file, true); } catch { }
    }
    // the address phones reach the TV on: the interface that holds the default route
    static string LanAddress()
    {
        try {
            using var u = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            u.Connect("192.0.2.1", 9);  // no packet is sent; this only picks the route
            if (u.LocalEndPoint is IPEndPoint ep && !IPAddress.IsLoopback(ep.Address)) return ep.Address.ToString();
        } catch { }
        foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
            if (n.OperationalStatus == OperationalStatus.Up && (n.Name.StartsWith("eth") || n.Name.StartsWith("wlan")))
                foreach (var a in n.GetIPProperties().UnicastAddresses) if (a.Address.AddressFamily == AddressFamily.InterNetwork) return a.Address.ToString();
        return null;
    }

    static void SetupServe(TcpListener l)
    {
        while (true) {
            TcpClient c;
            try { c = l.AcceptTcpClient(); } catch { return; }  // stopped
            new Thread(() => {
                using (c) try {
                    c.ReceiveTimeout = 15000; c.SendTimeout = 15000;
                    var s = c.GetStream(); var req = HttpRequest.Read(s, 16384);
                    if (req == null) return;
                    var (code, type, body) = SetupHandle(req);
                    HttpRequest.Respond(s, code, type, body);
                } catch (Exception e) { Log("setup request: " + e.GetType().Name); }
            }) { IsBackground = true }.Start();
        }
    }
    static byte[] Json(object o) => JsonSerializer.SerializeToUtf8Bytes(o);
    static (int, string, byte[]) SetupHandle(HttpRequest req)
    {
        const string J = "application/json";
        string code; lock (setupGate) code = setupCode;
        if (code.Length == 0) return (410, "text/plain", null);
        if (req.Method == "GET" && (req.Path == "/" || req.Path == "/index.html")) {
            using var r = typeof(Bridge).Assembly.GetManifestResourceStream("setup.html"); var ms = new MemoryStream(); r.CopyTo(ms);
            return (200, "text/html; charset=utf-8", ms.ToArray());
        }
        if (!req.Path.StartsWith("/api/")) return (404, "text/plain", null);
        var given = Encoding.ASCII.GetBytes(req.Headers.GetValueOrDefault("x-code", ""));
        if (!CryptographicOperations.FixedTimeEquals(given, Encoding.ASCII.GetBytes(code))) {
            int fails; lock (setupGate) fails = ++setupFailures;
            if (fails >= SetupMaxFailures) { CloseSetup("too many wrong codes"); return (410, "text/plain", null); }
            return (403, "text/plain", null);
        }
        lock (setupGate) { setupUntil = DateTime.UtcNow.AddMinutes(SetupMinutes); if (!setupPhone) { setupPhone = true; SetupStatus("phone"); } }
        Dictionary<string, JsonElement> body = new();
        if (req.Method == "POST") try { body = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(req.Body) ?? new(); } catch { return (400, J, Json(new { ok = false, message = "Bad request." })); }
        string S(string k) => body.TryGetValue(k, out var v) ? v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ValueKind == JsonValueKind.Number ? v.GetRawText() : v.ValueKind == JsonValueKind.True ? "true" : "" : "";
        switch (req.Path) {
            case "/api/state": return (200, J, Json(SetupState()));
            case "/api/test": {
                var (ok, message) = TestBroker(S("host").Trim(), S("port"), S("user").Trim(), S("password"));
                return (200, J, Json(new { ok, message }));
            }
            case "/api/thread-ha": {
                var (ok, message) = FetchHaThread(S("url").Trim(), S("token").Trim());
                return (200, J, Json(new { ok, message, network = ok ? haNetwork : "" }));
            }
            case "/api/save": { var (ok, message) = SaveSetup(S); return (200, J, Json(new { ok, message })); }
            case "/api/notify-test": {
                // with the settings as they are on the page (not saved yet)
                var (prefs, err) = NoticePrefs(S);
                if (err != null) return (200, J, Json(new { ok = false, message = err }));
                if (noTv) return (200, J, Json(new { ok = false, message = "Not available here." }));
                string why;
                lock (noticeGate) why = HandleNotice(ApplyPrefs(new NoticeData { Title = "SilentGlass", Message = "A test notification from Advanced settings." }, prefs), test: true);
                return (200, J, Json(new { ok = why == null, message = why == null ? "Sent. Look at the TV." : "Not shown: " + why + "." }));
            }
        }
        return (404, "text/plain", null);
    }

    static object SetupState()
    {
        var c = LoadConf(); var loc = ReadConf(DataFile(Shared.LocationFile)); var p = ReadConf(DataFile(Shared.PrefsFile));
        return new {
            name = TvName, host = c.GetValueOrDefault("host", ""), port = c.GetValueOrDefault("port", ""), user = c.GetValueOrDefault("user", ""),
            has_password = c.GetValueOrDefault("password", "").Length > 0, notify_from = c.GetValueOrDefault("notify_from", ""), update = UpdateSeconds(c),
            notify_position = c.GetValueOrDefault("notify_position", ""), notify_size = c.GetValueOrDefault("notify_size", ""),
            notify_duration = c.GetValueOrDefault("notify_duration", ""), notify_images = c.GetValueOrDefault("notify_images", "1") != "0",
            notify_quiet = c.GetValueOrDefault("notify_quiet", ""),
            notifications = NoticesOn, lat = loc.GetValueOrDefault("lat", ""), lon = loc.GetValueOrDefault("lon", ""),
            mqtt = mqttState, mqtt_error = mqttError, thread_installed = ThreadInstalled, thread_role = ThreadInstalled ? ThreadRole() : "",
            thread_on = ThreadWanted,
            // SilentGlass's own settings (the launcher reloads its prefs file when it changes)
            watch = p.GetValueOrDefault("watch", "0"), play = p.GetValueOrDefault("play", "0"), wall = p.GetValueOrDefault("wall", "auto"),
            hideapps = p.GetValueOrDefault("hideapps", "0") == "1", dim = p.GetValueOrDefault("dim", "1") != "0", close = p.GetValueOrDefault("close", "20"),
            inputs = Inputs().Where(i => i.Type != 0).Select(i => new { type = i.Type, name = i.Name == i.Port ? i.Port : i.Name + " · " + i.Port }).ToList(),
        };
    }

    // ---- checks: everything from the phone is validated before it reaches the config file or a command line ----
    public static bool ValidHost(string h) => h.Length is > 0 and <= 253 && (IPAddress.TryParse(h, out _) || Regex.IsMatch(h, @"^(?=.{1,253}$)[A-Za-z0-9]([A-Za-z0-9\-]{0,61}[A-Za-z0-9])?(\.[A-Za-z0-9]([A-Za-z0-9\-]{0,61}[A-Za-z0-9])?)*\.?$"));
    public static bool ValidText(string s, int max) => s.Length <= max && !s.Any(ch => char.IsControl(ch));
    public static bool ValidDataset(string hex) => hex.Length is >= 20 and <= 508 && hex.Length % 2 == 0 && Regex.IsMatch(hex, "^[0-9A-Fa-f]+$");
    public static bool ValidCoordinate(string v, double limit) => v.Length == 0 || double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && Math.Abs(d) <= limit;

    static (bool, string) TestBroker(string host, string portText, string user, string password)
    {
        if (!ValidHost(host)) return (false, "Enter the server's name or address.");
        if (!int.TryParse(portText.Length == 0 ? "1883" : portText, out int port) || port is < 1 or > 65535) return (false, "The port is a number up to 65535.");
        if (password.Length == 0 && conf.GetValueOrDefault("host", "") == host) password = conf.GetValueOrDefault("password", "");  // unchanged
        try {
            var t = new Mqtt(host, port, "silentglass-setup-test", user.Length > 0 ? user : null, password.Length > 0 ? password : null, Base + "/setup-test", "", (a, b) => { });
            t.Disconnect();
            return (true, "Connected to " + host + ".");
        } catch (SocketException e) { return (false, "Can't reach " + host + ":" + port + " (" + e.SocketErrorCode + ")."); }
        catch (Exception e) { return (false, "Can't connect: " + e.Message + "."); }
    }

    // Home Assistant's preferred Thread network, read once over its websocket API with a token the phone sends; the
    // token is not stored and the dataset stays in memory until Save (or the page closes)
    static (bool, string) FetchHaThread(string url, string token)
    {
        if (!Regex.IsMatch(url, @"^https?://[^\s/]+(:\d+)?/?$")) return (false, "Enter Home Assistant's address, like http://homeassistant.local:8123.");
        if (token.Length is < 20 or > 600 || !ValidText(token, 600)) return (false, "Paste a long-lived access token (your profile › Security).");
        try {
            using var ws = new ClientWebSocket(); using var cts = new CancellationTokenSource(15000);
            ws.ConnectAsync(new Uri(Regex.Replace(url.TrimEnd('/'), "^http", "ws") + "/api/websocket"), cts.Token).Wait();
            JsonElement Recv() {
                var buf = new byte[65536]; int got = 0; WebSocketReceiveResult r;
                do { r = ws.ReceiveAsync(new ArraySegment<byte>(buf, got, buf.Length - got), cts.Token).Result; got += r.Count; } while (!r.EndOfMessage && got < buf.Length);
                return JsonDocument.Parse(new ReadOnlyMemory<byte>(buf, 0, got)).RootElement;
            }
            void Send(object o) => ws.SendAsync(new ArraySegment<byte>(JsonSerializer.SerializeToUtf8Bytes(o)), WebSocketMessageType.Text, true, cts.Token).Wait();
            Recv();  // auth_required
            Send(new Dictionary<string, object> { ["type"] = "auth", ["access_token"] = token });
            if (Recv().GetProperty("type").GetString() != "auth_ok") return (false, "Home Assistant did not accept that token.");
            Send(new Dictionary<string, object> { ["id"] = 1, ["type"] = "thread/list_datasets" });
            var list = Recv();
            if (!list.GetProperty("success").GetBoolean()) return (false, "Home Assistant has no Thread integration.");
            JsonElement? pick = null;
            foreach (var d in list.GetProperty("result").GetProperty("datasets").EnumerateArray()) if (d.TryGetProperty("preferred", out var p) && p.GetBoolean()) pick = d;
            if (pick == null) return (false, "Home Assistant has no preferred Thread network yet.");
            Send(new Dictionary<string, object> { ["id"] = 2, ["type"] = "thread/get_dataset_tlv", ["dataset_id"] = pick.Value.GetProperty("dataset_id").GetString() });
            var tlv = Recv();
            string hex = tlv.GetProperty("success").GetBoolean() ? tlv.GetProperty("result").GetProperty("tlv").GetString() ?? "" : "";
            if (!ValidDataset(hex)) return (false, "Home Assistant did not return the network's dataset.");
            lock (setupGate) { haTlv = hex; haNetwork = pick.Value.TryGetProperty("network_name", out var n) ? n.GetString() ?? "Thread" : "Thread"; }
            try { ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None).Wait(2000); } catch { }
            return (true, "Found " + haNetwork + ".");
        } catch (Exception e) { return (false, "Can't reach Home Assistant (" + (e.InnerException ?? e).Message + ")."); }
    }

    // the notification settings from the page, checked; empty values mean "as sent"
    static (Dictionary<string, string>, string) NoticePrefs(Func<string, string> S)
    {
        string pos = S("notify_position"), size = S("notify_size"), dur = S("notify_duration"), from = S("notify_quiet_from"), to = S("notify_quiet_to");
        if (pos.Length > 0 && !NoticePositions.Contains(pos)) return (null, "Pick where notifications appear.");
        if (size.Length > 0 && !NoticeSizes.Contains(size)) return (null, "Pick a notification size.");
        if (dur.Length > 0 && !(int.TryParse(dur, out int d) && NoticeDurations.Contains(d))) return (null, "Pick how long notifications stay.");
        string quiet = from.Length > 0 || to.Length > 0 ? from + "-" + to : "";
        if (!ValidQuiet(quiet)) return (null, "Quiet hours are two times, like 22:00 and 07:00.");
        return (new Dictionary<string, string> { ["notify_position"] = pos, ["notify_size"] = size, ["notify_duration"] = dur,
            ["notify_images"] = S("notify_images") == "0" ? "0" : "", ["notify_quiet"] = quiet }, null);
    }
    static (bool, string) SaveSetup(Func<string, string> S)
    {
        string host = S("host").Trim(), portText = S("port").Trim(), user = S("user").Trim(), password = S("password"), name = S("name").Trim(),
            from = S("notify_from").Trim(), update = S("update").Trim(), lat = S("lat").Trim(), lon = S("lon").Trim(), threadMode = S("thread"), tlv = S("tlv").Trim();
        if (host.Length > 0 && !ValidHost(host)) return (false, "The server name is not valid.");
        if (portText.Length > 0 && (!int.TryParse(portText, out int port) || port is < 1 or > 65535)) return (false, "The port is a number up to 65535.");
        if (!ValidText(user, 128) || !ValidText(password, 256) || !ValidText(name, 32)) return (false, "Names and passwords can't contain line breaks.");
        var senders = from.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (senders.Length > 8 || senders.Any(h => !ValidHost(h))) return (false, "\"Also from\" takes up to 8 addresses, separated by commas.");
        if (!ValidCoordinate(lat, 90) || !ValidCoordinate(lon, 180)) return (false, "Latitude and longitude are numbers, like 40.7 and -74.0.");
        if (update.Length > 0 && !(int.TryParse(update, out int every) && UpdateChoices.Contains(every))) return (false, "Pick how often to update Home Assistant.");
        var (noticePrefs, noticeErr) = NoticePrefs(S);
        if (noticeErr != null) return (false, noticeErr);
        string dataset = threadMode == "ha" ? haTlv : threadMode == "tlv" ? tlv : "";
        if (threadMode == "ha" && dataset.Length == 0) return (false, "Get Home Assistant's network first.");
        if (threadMode == "tlv" && !ValidDataset(dataset)) return (false, "That doesn't look like a Thread dataset (hex).");
        string watch = S("watch"), play = S("play"), wall = S("wall"), threadOn = S("thread_on");
        static bool Port(string v) => v is "0" or "13" or "14" or "15" or "16";
        if (watch.Length > 0 && (!Port(watch) || !Port(play))) return (false, "Pick an input for Watch and Play.");
        if (watch.Length > 0 && watch != "0" && watch == play) return (false, "Watch and Play need different inputs.");
        if (wall.Length > 0 && wall is not ("auto" or "day" or "night")) return (false, "Pick a wallpaper.");
        string close = S("close");
        if (close.Length > 0 && close is not ("10" or "20" or "30" or "60" or "0")) return (false, "Pick when the menu closes.");

        var c = LoadConf(); bool hostChanged = c.GetValueOrDefault("host", "") != host;
        void Put(string k, string v) { if (v.Length > 0) c[k] = v; else c.Remove(k); }
        Put("host", host); Put("port", portText == "1883" ? "" : portText); Put("user", user); Put("notify_from", string.Join(",", senders));
        // the name is the TV's own (TvApi TvNameKey); the config keeps one only if the TV refused it
        string own = noTv ? "" : VStr(TvNameKey), nameNote = "";
        if (name.Length == 0 || name == own) Put("name", "");
        else if (!noTv && SetTvName(name)) { Put("name", ""); Log("TV name: '" + own + "' -> '" + name + "'"); }
        else { Put("name", name); if (!noTv) nameNote = " The TV's own name could not be changed."; }
        Put("update", update == DefaultUpdate.ToString() ? "" : update);
        foreach (var kv in noticePrefs) Put(kv.Key, kv.Value);
        if (password.Length > 0) c["password"] = password; else if (hostChanged || user.Length == 0) c.Remove("password");
        try { SaveConf(c); } catch (Exception e) { return (false, "Could not save (" + e.Message + ")."); }
        try {
            SetMarker(Shared.NotifyOff, S("notifications") != "true");
            if (lat.Length > 0 && lon.Length > 0) File.WriteAllText(DataFile(Shared.LocationFile), "lat=" + lat + "\nlon=" + lon + "\n"); else File.Delete(DataFile(Shared.LocationFile));
            if (watch.Length > 0) {  // SilentGlass's own file: these keys change, the rest stays
                var p = ReadConf(DataFile(Shared.PrefsFile));
                p["watch"] = watch; p["play"] = play; p["wall"] = wall.Length > 0 ? wall : "auto";
                p["hideapps"] = S("hideapps") == "1" ? "1" : "0"; p["dim"] = S("dim") == "0" ? "0" : "1";
                if (close.Length > 0) p["close"] = close;
                File.WriteAllText(DataFile(Shared.PrefsFile), string.Concat(p.Select(kv => kv.Key + "=" + kv.Value + "\n")));
            }
        } catch { }
        bool threadStarting = false;
        if (ThreadInstalled && threadOn is "on" or "off" && (threadOn == "on") != ThreadWanted) {
            SetThreadSwitch(threadOn == "on"); threadStarting = threadOn == "on";
            if (threadStarting && dataset.Length > 0) Thread.Sleep(8000);  // let the border router start before joining
        }
        Log("setup saved" + (dataset.Length > 0 ? " (thread)" : ""));
        string threadResult = dataset.Length > 0 ? ApplyThread(dataset) : "";
        lock (setupGate) haTlv = "";
        // reconnect with the new settings and report how that went (the main loop leaves "connecting" when it knows)
        string result = "Saved.";
        if (host.Length > 0) SetState("connecting", "");
        reload = true; changed.Set();
        if (host.Length > 0) {
            for (int i = 0; i < 100 && (reload || mqttState == "connecting"); i++) Thread.Sleep(250);
            result = mqttState == "connected" ? "Saved. Connected to " + host + "." : "Saved, but can't connect yet: " + (mqttError.Length > 0 ? mqttError : "no answer") + ".";
        }
        if (threadResult.Length > 0) result += " " + threadResult;
        result += nameNote;
        bool ok = !result.Contains("can't") && !threadResult.StartsWith("Thread:");
        SetupStatus(ok ? "saved" : "error", result);
        return (ok, result);
    }

    // Join the network in `dataset` on the TV's border router (as the owner - no root needed). The active dataset is
    // only ever replaced while Thread is stopped: committing it on a running node would push it to the whole mesh.
    static string ApplyThread(string dataset)
    {
        const string C = "/opt/usr/apps/com.samsung.tizen.smartthings-hub/bin/ot-ctl";
        if (!ThreadInstalled || noTv) return "Thread: the border router is not installed.";
        if (!ThreadWanted) return "Thread: switch the border router on first.";
        string Ot(string args) => Run(C, args, 6000).Replace("\r", "").Trim();
        string current = Ot("dataset active -x").Split('\n')[0].Trim();
        if (string.Equals(current, dataset, StringComparison.OrdinalIgnoreCase)) return "Already on this Thread network.";
        Log("thread: joining a new network");
        Ot("thread stop"); Ot("ifconfig down");
        bool ok = Ot("dataset init tlvs " + dataset).EndsWith("Done") && Ot("dataset commit active").EndsWith("Done") || Ot("dataset set active " + dataset).EndsWith("Done");
        Ot("ifconfig up"); Ot("thread start");
        if (!ok) { Log("thread: dataset rejected"); return "Thread: the border router rejected that dataset."; }
        for (int i = 0; i < 20; i++) { var r = ThreadRole(); if (r is "child" or "router" or "leader") return "Joined the Thread network."; Thread.Sleep(1500); }
        return "Thread is starting - give it a minute.";
    }
}
