using System;
using System.IO;
using System.Linq;

// Off-TV checks of the NUI-free launcher code: dotnet run --project tests/CatalogTests.csproj -- launcher/assets/apps.json
static class CatalogTests
{
    public static int Main(string[] args)
    {
        if (args[0] == "--qr") {  // the module matrix of each text, one row per line (tests/test_launcher.py compares it with segno)
            foreach (var text in args.Skip(1)) {
                var m = Qr.Encode(text);
                for (int y = 0; y < m.GetLength(0); y++) Console.WriteLine(string.Concat(Enumerable.Range(0, m.GetLength(1)).Select(x => m[y, x] ? '1' : '0')));
                Console.WriteLine();
            }
            return 0;
        }
        var catalog = AppLibrary.Load(args[0]);
        if (catalog.Count < 2) throw new Exception("Catalog unexpectedly small");
        string tmp = Path.GetTempFileName();
        try
        {
            foreach (string json in new[] {
                "[{\"Name\":\"Bad\",\"Id\":\"one\",\"Asset\":\"../escape.svg\",\"Width\":100,\"Height\":50}]",
                "[{\"Name\":\"Bad\",\"Id\":\"one\",\"Asset\":\"ok.svg\",\"Width\":100,\"Height\":50,\"Crop\":[0.8,0,0.4,1]}]",
                "[{\"Name\":\"A\",\"Id\":\"one\",\"Asset\":\"ok.svg\",\"Width\":100,\"Height\":50},{\"Name\":\"B\",\"Id\":\"one\",\"Asset\":\"ok.svg\",\"Width\":100,\"Height\":50}]"
            })
            {
                File.WriteAllText(tmp, json);
                bool rejected = false;
                try { AppLibrary.Load(tmp); } catch (InvalidDataException) { rejected = true; }
                if (!rejected) throw new Exception("Invalid catalog accepted");
            }
        }
        finally { File.Delete(tmp); }
        InfoTests();
        SharedTests();
        HomeLogicTests();
        Console.WriteLine("Catalog, info parser, shared and Home logic checks passed.");
        return 0;
    }
    static void True(bool ok, string context) { if (!ok) throw new Exception("Check failed: " + context); }
    static void InfoTests()
    {
        True(InfoStats.ParseUptime("11586.53 34152.99\n") == 11586.53 && InfoStats.ParseUptime(null) == null, "uptime");
        True(InfoStats.ParseDefaultRoute("Iface\tDestination\tGateway\n eth0\t0001A8C0\t00000000\t0001\neth0\t00000000\t011A030A\t0003\n") == "eth0", "default route");
        True(InfoStats.ParseDefaultRoute("Iface\tDestination\n") == null && InfoStats.ParseDefaultRoute(null) == null, "no route");
        True(InfoStats.ParseWirelessLevel("h1\nh2\n wlan0: 0000   55.  -61.  -256        0      0      0      0      0        0\n", "wlan0") == -61, "wifi level");
        True(InfoStats.ParseWirelessLevel(" wlan0: 0000    0     0     0        0\n", "wlan0") == null && InfoStats.ParseWirelessLevel(null, "wlan0") == null, "wifi down");
        True(InfoStats.ParseTemperature("36\n") == 36 && InfoStats.ParseTemperature("") == null && InfoStats.ParseTemperature("900") == null, "chip temperature");
        // synthetic documentation-style prefixes, not this network's
        string i6 = "fd00000000000a01000000fffe00b400 05 40 00 a2    wpan0\n"
                  + "fd00000000000b02c6c92fc22fceb270 05 40 00 82    wpan0\n"
                  + "fe800000000000002092eff5346c0df9 05 40 20 82    wpan0\n"
                  + "fe800000000000000000000000000001 02 40 20 80     eth0\n";
        var th = InfoStats.ParseThread(i6, "wpan0");
        True(th.present && th.rloc16 == 0xb400 && (th.rloc16 >> 10) == 45 && !th.leader && th.homeRoute, "thread router");
        th = InfoStats.ParseThread(i6 + "fd00000000000a01000000fffe00fc00 05 40 00 a2    wpan0\n", "wpan0");
        True(th.leader && th.rloc16 == 0xb400, "thread leader aloc");
        th = InfoStats.ParseThread("fd00000000000a01000000fffe00b400 05 40 00 a2    wpan0\n", "wpan0");
        True(th.present && !th.homeRoute, "thread no off-mesh route");
        th = InfoStats.ParseThread("fe800000000000000000000000000001 02 40 20 80     eth0\n", "wpan0");
        True(!th.present && th.rloc16 == -1, "thread absent");
        True(!InfoStats.ParseThread(null, "wpan0").present, "thread null");
        True(InfoStats.Duration(11586) == "3 h 13 min" && InfoStats.Duration(30) == "1 min" && InfoStats.Duration(90000) == "1 d 1 h", "duration");
    }
    static void SharedTests()
    {
        string src = "[{\"connectionState\":true,\"detectedName\":\"\",\"editName\":\"\",\"powerState\":0,\"sourceType\":0},"
            + "{\"connectionState\":true,\"detectedName\":\"Game%20Console\",\"editName\":\"Switch\",\"powerState\":2,\"sourceType\":13},"
            + "{\"connectionState\":true,\"detectedName\":\"Q-Series%20Soundbar\",\"editName\":\"\",\"powerState\":1,\"sourceType\":15},"
            + "{\"connectionState\":true,\"editName\":\"Media%20Server\",\"sourceType\":56}]";
        var ins = Shared.ParseInputs(src, out bool known);
        True(known && ins.Count == 5 && ins[0].Type == 13 && ins[0].Name == "Switch" && ins[0].Detected == "Game Console" && ins[0].Port == "HDMI 1" && ins[0].Connected, "input edit name");
        True(ins[1].Type == 14 && ins[1].Name == "HDMI 2" && !ins[1].Connected, "input not connected");
        True(ins[2].Name == "Q-Series Soundbar" && ins[2].Edit == "", "input detected name decoded");
        True(ins[4].Type == 0 && ins[4].Name == "Live TV", "live tv last; DLNA skipped");
        True(Shared.ParseInputs("not json", out known).Count == 4 && !known && Shared.ParseInputs(null, out known).TrueForAll(i => !i.Connected) && !known, "inputs garbage");
        Shared.ParseInputs("[]", out known); True(!known, "an empty list is not an answer");
        var c = Shared.ParseConf("# comment\nhost = broker.lan \nport=1883\nbad line\nhost=other\npassword=a=b\n");
        True(c.Count == 3 && c["host"] == "other" && c["port"] == "1883" && c["password"] == "a=b", "key=value");
        True(Shared.ParseConf(null).Count == 0, "no file");
        True(Shared.GameName("Auto") == "Automatic" && Shared.GameName("On") == "Always" && Shared.GameName("Off") == "Never", "Game Mode words");
        True(Shared.IsAmbient(Shared.Art) && !Shared.IsAmbient(Shared.Viewer), "ambient");
    }
    static void HomeLogicTests()
    {
        // preferences (Prefs.cs, shared with the bridge): round trip, junk rejected, older files load
        var saved = new Prefs { HideApps = true, Dim = 0, Wall = "night", Lat = 51.5, Lon = -0.1, Close = 0, Clock = "24", InputAlerts = false };
        saved.Shortcuts[0].Port = 15; saved.Shortcuts[2] = new Prefs.Shortcut { Name = "Work", Icon = "desktop", Port = 14 };
        var back = Prefs.Parse(saved.Format());
        True(back.Format() == saved.Format() && back.HideApps && back.Dim == 0 && back.Clock == "24" && !back.InputAlerts && back.Wall == "night" && back.Lat == 51.5 && back.Lon == -0.1 && back.Close == 0, "prefs round trip");
        True(back.Shortcuts[0].Name == "Watch" && back.Shortcuts[0].Port == 15 && !back.Shortcuts[1].Shown && back.Shortcuts[2].Icon == "desktop" && back.For(14).Name == "Work", "shortcuts round trip");
        var junk = Prefs.Parse("shortcut1_port=99\nshortcut2_port=14\nshortcut2_icon=rocket\nshortcut3_name=A very long name indeed\nshortcut4_port=14\nwall=sepia\nlat=400\nlon=abc\nclose=7\ndim=7\nclock=13\n");
        True(!junk.Shortcuts[0].Shown && junk.Shortcuts[1].Port == 14 && junk.Shortcuts[1].Icon == "gamecontroller" && junk.Shortcuts[2].Name.Length == Prefs.NameMax && !junk.Shortcuts[3].Shown, "shortcuts reject junk; one input, one shortcut");
        True(junk.Wall == "auto" && junk.Lat == 90 && junk.Lon == Prefs.DefaultLon && junk.Close == 20 && junk.Dim == 5 && junk.Clock == "12", "prefs reject junk");
        // 1.3: watch / play, 0 = automatic (fallback HDMI 3 / HDMI 1): the same inputs as shortcuts, the old keys dropped on save
        var v13 = Prefs.Parse("watch=0\nplay=0\nhideapps=1\ndim=5\n");
        True(v13.Shortcuts[0].Port == 15 && v13.Shortcuts[1].Port == 13 && v13.Shortcuts[0].Name == "Watch" && v13.Shortcuts[1].Icon == "gamecontroller" && v13.HideApps, "1.3 prefs keep Watch / Play");
        True(Prefs.Parse("watch=14\nplay=16\n").Shortcuts[0].Port == 14 && Prefs.Parse("watch=14\nplay=16\n").Shortcuts[1].Port == 16, "1.3 chosen inputs kept");
        True(Prefs.Parse("watch=13\nplay=0\n").Shortcuts[1].Port == 0, "1.3 automatic Play on Watch's input is hidden");
        string merged = v13.Merge("watch=0\nplay=0\nfuture=7\n");
        True(!merged.Contains("watch=") && !merged.Contains("play=") && merged.Contains("future=7\n") && merged.Contains("shortcut1_port=15\n"), "save drops retired keys, keeps unknown ones");
        var old = Prefs.Parse("watch=0\nplay=0\nhideapps=1\nlive=0\noverlay=1\n");
        True(old.HideApps && old.Dim == 5 && old.Close == 20 && old.Clock == "12" && old.InputAlerts, "pre-v40 prefs load");
        True(Prefs.Parse("dim=1\n").Dim == 5 && Prefs.Parse("dim=0\n").Dim == 0, "pre-v43c dim switch loads");
        var fresh = Prefs.Parse(null);
        True(!fresh.HideApps && fresh.Dim == 5 && fresh.Shortcuts.All(s => !s.Shown) && fresh.Shortcuts[0].Name == "Watch" && fresh.Shortcuts[1].Name == "Play", "no prefs file = defaults, no shortcut shown");
        True(Logic.NextDim(5) == 10 && Logic.NextDim(0) == 2 && Logic.NextDim(2, -1) == 0 && Logic.DimName(0) == "Never", "dim cycle");
        var t = new DateTime(2026, 1, 1, 21, 5, 0);
        True(Logic.ClockText("12", t, true) == "9:05 PM" && Logic.ClockText("12", t, false) == "9:05" && Logic.ClockText("24", t, true) == "21:05" && Logic.ClockText("off", t, true) == "", "clock formats");
        True(Logic.NextWall("auto") == "day" && Logic.NextWall("day") == "night" && Logic.NextWall("night") == "auto" && Logic.NextWall("auto", -1) == "night", "wallpaper cycle");
        // shortcut inputs from the remote: hidden -> HDMI 1..4 -> hidden, skipping inputs other shortcuts use
        var cyc = Prefs.Parse("shortcut1_port=15\nshortcut2_port=13\n");
        True(cyc.NextPort(0) == 16 && cyc.NextPort(1) == 14 && cyc.NextPort(1, -1) == 0 && cyc.NextPort(2) == 14 && cyc.NextPort(2, -1) == 16, "port cycle skips the other shortcuts' inputs");
        cyc.Shortcuts[0].Port = 16; True(cyc.NextPort(0) == 0 && cyc.NextPort(0, -1) == 15, "port cycle wraps through hidden");
        for (int a = 0; a <= 16; a = a == 0 ? 13 : a + 1)
            for (int b = 0; b <= 16; b = b == 0 ? 13 : b + 1) {
                var pp = new Prefs(); pp.Shortcuts[0].Port = a; pp.Shortcuts[1].Port = b == a ? 0 : b;
                for (int dir = -1; dir <= 1; dir += 2) { int n = pp.NextPort(2, dir); True(n == 0 || Prefs.IsPort(n) && n != pp.Shortcuts[0].Port && n != pp.Shortcuts[1].Port, "port cycle never collides"); }
            }
        True(new Prefs().Problem() == null && Prefs.Parse("shortcut1_port=13\nshortcut2_port=14\n").Problem() == null, "valid shortcuts");
        var dup = new Prefs(); dup.Shortcuts[0].Port = 13; dup.Shortcuts[3].Port = 13; True(dup.Problem() != null, "two shortcuts, one input: refused");
        var bad = new Prefs(); bad.Shortcuts[2].Name = "two\nlines"; True(bad.Problem() != null, "a name with a line break: refused");
        bad = new Prefs(); bad.Shortcuts[2].Icon = "rocket"; True(bad.Problem() != null, "an unknown icon: refused");
        bad = new Prefs(); bad.Shortcuts[2].Port = 12; True(bad.Problem() != null, "not an HDMI input: refused");
        True(Prefs.CleanName("  Gaming\tPC  ") == "GamingPC" && Prefs.CleanName(new string('x', 30)).Length == Prefs.NameMax, "names cleaned");
        // notification banner geometry stays on screen
        foreach (var size in new[] { "small", "medium", "large", "max", "?" })
            foreach (var pos in new[] { "bottom-right", "bottom-left", "top-right", "top-left", "center", "?" }) {
                int w = Logic.NoticeWidth(size), h = 600; var (x, y) = Logic.NoticeOrigin(pos, w, h);
                True(x >= 0 && y >= 0 && x + w <= 1920 && y + h <= 1080, "banner on screen");
            }
        True(Logic.NoticeImageHeight(612, 896, 672) == 408 && Logic.NoticeImageHeight(612, 1920, 1080) == 344 && Logic.NoticeImageHeight(612, 0, 0) == 344, "banner image shape");
        var j = new Logic.Json("{\"state\":\"waiting\",\"w\":896,\"at\":1792800000000}");
        True(j["state"] == "waiting" && j.Int("w") == 896 && j.Long("at") == 1792800000000 && j["missing"] == "", "flat json");
        // QR: the shape here; module for module against a reference encoder in tests/test_launcher.py
        var q = Qr.Encode("http://10.0.0.2:7680/?code=123456");
        True(q.GetLength(0) == 29 && q[0, 0] && q[6, 6] && !q[7, 7], "QR version 3 with finders");
        True(Qr.Svg(q).StartsWith("<svg") && Qr.Svg(q).Contains("h1v1h-1z"), "QR svg");
        // CEC: a listed player is on; straight into the TV it must not answer standby; behind a soundbar, listed is enough
        var cec = Logic.DevicesOn(new[] { (3, "PLAYBACK_DEVICE1"), (3, "AUDIO_SYSTEM"), (1, "PLAYBACK_DEVICE2"), (2, "RECORDER_DEVICE1"), (4, "TV"), (9, "PLAYBACK_DEVICE3") },
            p => p == 3 ? 1 : p == 2 ? 1 : p == 1 ? 0 : -1);
        True(string.Join(",", cec.OrderBy(x => x)) == "13,15", "cec devices on " + string.Join(",", cec));
        True(Logic.DevicesOn(new[] { (1, "PLAYBACK_DEVICE1") }, p => -1).Contains(13), "cec: no power answer still counts");
        True(Logic.DevicesOn(new (int, string)[0], p => 0).Count == 0, "cec: nothing listed");
        // New York, 25 Oct 2026: 11:18 / 22:00 UTC (astral)
        var (rise, set) = Logic.SunTimes(new DateTime(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc), 40.7, -74.0);
        True(Math.Abs(rise - (11 * 60 + 18)) <= 5 && Math.Abs(set - (22 * 60 + 0)) <= 5, "sun times " + rise + " " + set);
        True(!Logic.IsNight(new DateTime(2026, 10, 25, 16, 0, 0, DateTimeKind.Utc), 40.7, -74.0), "noon is day");
        True(Logic.IsNight(new DateTime(2026, 10, 26, 2, 0, 0, DateTimeKind.Utc), 40.7, -74.0), "10 pm is night (after UTC midnight)");
        True(Logic.IsNight(new DateTime(2026, 10, 25, 9, 0, 0, DateTimeKind.Utc), 40.7, -74.0), "5 am is night");
        True(!Logic.IsNight(new DateTime(2026, 6, 21, 3, 0, 0, DateTimeKind.Utc), 35.7, 139.7), "wrapped day");
    }
}
