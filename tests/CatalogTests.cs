using System;
using System.IO;

static class CatalogTests
{
    static void Equal(int expected, int actual, string context)
    {
        if (expected != actual) throw new Exception(context + ": expected " + expected + ", got " + actual);
    }
    public static int Main(string[] args)
    {
        var catalog = AppLibrary.Load(args[0]);
        if (catalog.Count < 2) throw new Exception("Catalog unexpectedly small");
        Equal(-1, AppLibrary.Move(31, 0, "Up"), "top row returns to tabs");
        Equal(15, AppLibrary.Move(31, 10, "Down"), "last row enters next page");
        Equal(25, AppLibrary.Move(31, 30, "Up"), "partial page returns to matching previous column");
        Equal(30, AppLibrary.Move(31, 29, "Down"), "last partial page clamps column");
        Equal(11, AppLibrary.Move(12, 9, "Down"), "partial bottom row clamps column");
        Equal(11, AppLibrary.Move(12, 11, "Right"), "last item remains selected");
        Equal(-1, AppLibrary.Move(0, 0, "Down"), "empty catalog");
        for (int count = 1; count < 48; count++)
            for (int item = 0; item < count; item++)
                foreach (var key in new[] {"Left", "Right", "Up", "Down"})
                {
                    int next = AppLibrary.Move(count, item, key);
                    if (next < -1 || next >= count) throw new Exception("Out of bounds navigation");
                }
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
        HomeLogicTests();
        Console.WriteLine("Catalog, info parser and Home logic checks passed.");
        return 0;
    }
    static void True(bool ok, string context) { if (!ok) throw new Exception("Info check failed: " + context); }
    static void InfoTests()
    {
        True(InfoStats.ParseUptime("11586.53 34152.99\n") == 11586.53, "uptime");
        var mem = InfoStats.ParseMemory("MemTotal:        1816840 kB\nMemFree:           25760 kB\nMemAvailable:     243688 kB\n");
        True(mem.HasValue && mem.Value.totalKb == 1816840 && mem.Value.availableKb == 243688, "meminfo");
        var stat = "cpu  100 0 50 800 50 0 0 0 0 0\ncpu0 1 0 0 0 0 0 0 0\ncpu1 1 0 0 0 0 0 0 0\nintr 5\n";
        var cpu = InfoStats.ParseCpu(stat); True(cpu.HasValue && cpu.Value.idle == 850 && cpu.Value.total == 1000, "cpu totals");
        True(InfoStats.CpuCount(stat) == 2, "cpu count");
        var dev = InfoStats.ParseNetDev("Inter-|   Receive |  Transmit\n face |bytes packets\n    lo: 10 1 0 0 0 0 0 0 20 2 0 0 0 0 0 0\n  eth0: 58459740 206938 0 2993 0 0 0 0 1234 99 0 0 0 0 0 0\n");
        True(dev["eth0"].rx == 58459740 && dev["eth0"].tx == 1234 && dev["lo"].tx == 20, "net/dev");
        True(InfoStats.ParseDefaultRoute("Iface\tDestination\tGateway\n eth0\t0001A8C0\t00000000\t0001\neth0\t00000000\t011A030A\t0003\n") == "eth0", "default route");
        True(InfoStats.ParseDefaultRoute("Iface\tDestination\n") == null, "no route");
        True(InfoStats.ParseWirelessLevel("h1\nh2\n wlan0: 0000   55.  -61.  -256        0      0      0      0      0        0\n", "wlan0") == -61, "wifi level");
        True(InfoStats.ParseWirelessLevel(" wlan0: 0000    0     0     0        0\n", "wlan0") == null, "wifi down");
        var ns = InfoStats.ParseNameservers("# Generated\nnameserver 192.0.2.1\nnameserver 8.8.8.8\nnameserver 192.0.2.1\n");
        True(ns.Count == 2 && ns[0] == "192.0.2.1" && ns[1] == "8.8.8.8", "resolv.conf");
        string tcp6 = "  sl  local_address remote_address st\n   0: 00000000000000000000000000000000:65F5 00000000000000000000000000000000:0000 0A 0\n   1: 00000000000000000000000000000000:1F90 00000000000000000000000000000000:0000 01 0\n";
        True(InfoStats.ParseListening(tcp6, 26101) && !InfoStats.ParseListening(tcp6, 8080), "listen");
        True(InfoStats.ParseBuildDate("TIZEN-TRUNK2025-PontusM-OSU-2024-HOTFIX-RELEASE_20260514.3") == "2026-05-14", "build date");
        // synthetic documentation-style prefixes, not this network's
        string i6 = "fd00000000000a01000000fffe00b400 05 40 00 a2    wpan0\n"
                  + "fd00000000000b02c6c92fc22fceb270 05 40 00 82    wpan0\n"
                  + "fe800000000000002092eff5346c0df9 05 40 20 82    wpan0\n"
                  + "fe800000000000000000000000000001 02 40 20 80     eth0\n";
        var th = InfoStats.ParseThread(i6, "wpan0");
        True(th.present && th.rloc16 == 0xb400 && (th.rloc16 >> 10) == 45 && !th.leader && th.homeRoute && th.addresses == 3, "thread router");
        th = InfoStats.ParseThread(i6 + "fd00000000000a01000000fffe00fc00 05 40 00 a2    wpan0\n", "wpan0");
        True(th.leader && th.rloc16 == 0xb400, "thread leader aloc");
        th = InfoStats.ParseThread("fd00000000000a01000000fffe00b400 05 40 00 a2    wpan0\n", "wpan0");
        True(th.present && !th.homeRoute, "thread no off-mesh route");
        th = InfoStats.ParseThread("fe800000000000000000000000000001 02 40 20 80     eth0\n", "wpan0");
        True(!th.present && th.rloc16 == -1, "thread absent");
        True(!InfoStats.ParseThread(null, "wpan0").present, "thread null");
        string src = "[{\"connectionState\":true,\"detectedName\":\"\",\"editName\":\"\",\"powerState\":0,\"sourceType\":0},"
            + "{\"connectionState\":true,\"detectedName\":\"Game%20Console\",\"editName\":\"Switch\",\"powerState\":2,\"sourceType\":13},"
            + "{\"connectionState\":true,\"detectedName\":\"Q-Series%20Soundbar\",\"editName\":\"\",\"powerState\":1,\"sourceType\":15},"
            + "{\"connectionState\":true,\"editName\":\"Media%20Server\",\"sourceType\":56}]";
        var ins = InfoStats.ParseSources(src);
        True(ins.Count == 5 && ins[0].Type == 13 && ins[0].Name == "Switch" && ins[0].Port == "HDMI 1" && ins[0].Connected && ins[0].Power == 2, "input edit name");
        True(ins[1].Type == 14 && ins[1].Name == "HDMI 2" && !ins[1].Connected, "input not connected");
        True(ins[2].Name == "Q-Series Soundbar" && ins[2].Power == 1, "input detected name decoded");
        True(ins[4].Type == 0 && ins[4].Name == "Live TV", "live tv last; DLNA skipped");
        True(ins.TrueForAll(i => i.Known), "inputs known when the TV answered");
        True(InfoStats.ParseSources("not json").Count == 4 && InfoStats.ParseSources(null).TrueForAll(i => !i.Connected && !i.Known), "inputs garbage");
        True(InfoStats.ParseSources("[]").TrueForAll(i => !i.Known), "empty list is not an answer");
        True(InfoStats.Bytes(999) == "999 B" && InfoStats.Bytes(58459740) == "58.5 MB" && InfoStats.Bytes(1.5e9) == "1.5 GB", "bytes");
        True(InfoStats.Duration(11586) == "3 h 13 min" && InfoStats.Duration(30) == "1 min" && InfoStats.Duration(90000) == "1 d 1 h", "duration");
        True(InfoStats.CountMarkers(InfoStats.VendorMarkers) == 10, "all markers");
        True(InfoStats.CountMarkers(new[] { "pisa-service", "bixby-prov", "canalysis-daemon.service", "other" }) == 1, "exact marker names only");
    }
    static void HomeLogicTests()
    {
        // runtime conf: last value wins, junk lines ignored, values clamped / validated, missing file = defaults
        var conf = HomeLogic.ParseConf("vsync=9\nwall=shader\nlat=40.5\nlon=abc\n=x\nnoequals\nscrim=0\nscrim=1\n");
        True(HomeLogic.Int(conf, "vsync", 1, 1, 4) == 4, "conf int clamps");
        True(HomeLogic.Pick(conf, "wall", "photo", "photo", "gradient") == "photo", "conf rejects removed modes");
        True(HomeLogic.Num(conf, "lat", 0) == 40.5 && HomeLogic.Num(conf, "lon", -74.0) == -74.0, "conf numbers");
        True(HomeLogic.Flag(conf, "scrim", false) && HomeLogic.Flag(conf, "enabled", true), "conf flags");
        True(HomeLogic.ReadConf("/nonexistent/quiet-home.conf").Count == 0, "missing conf");
        // Watch / Play inputs: only Automatic or HDMI 1-4 survive a round trip
        var saved = new HomeLogic.Prefs { Watch = 15, HideApps = true, Live = false, Dim = false, Wall = "night" };
        var back = HomeLogic.ParsePrefs(HomeLogic.FormatPrefs(saved));
        True(back.Watch == 15 && back.Play == 0 && back.HideApps && !back.Live && !back.Dim && back.Wall == "night", "prefs round trip");
        var junk = HomeLogic.ParsePrefs("watch=99\nplay=14\nwall=sepia\n");
        True(junk.Watch == 0 && junk.Play == 14 && junk.Wall == "auto", "prefs reject junk");
        // a file from an older Quiet Home (or none) keeps every newer setting at its default
        var old = HomeLogic.ParsePrefs("watch=0\nplay=0\nhideapps=1\n");
        True(old.HideApps && old.Live && old.Dim && old.Wall == "auto", "older prefs files load with defaults");
        True(HomeLogic.ParsePrefs(null).Live && !HomeLogic.ParsePrefs(null).HideApps, "no prefs file = defaults");
        True(HomeLogic.NextWall("auto") == "day" && HomeLogic.NextWall("day") == "night" && HomeLogic.NextWall("night") == "auto", "wallpaper cycle");
        // tab bar: a hidden Apps tab is skipped both ways; the ends stop
        True(HomeLogic.NextTab(0, 1, true) == 2 && HomeLogic.NextTab(2, -1, true) == 0, "skip hidden Apps tab");
        True(HomeLogic.NextTab(0, 1, false) == 1 && HomeLogic.NextTab(0, -1, true) == 0 && HomeLogic.NextTab(3, 1, false) == 3, "tab ends");
        True(HomeLogic.NextPort(0, 15) == 13 && HomeLogic.NextPort(16, 15) == 0, "port cycle wraps through Automatic");
        True(HomeLogic.NextPort(0, 13) == 14 && HomeLogic.NextPort(14, 15) == 16, "port cycle skips the other card's input");
        for (int other = 13; other <= 16; other++)
            for (int cur = 0; cur <= 16; cur = cur == 0 ? 13 : cur + 1)
            {
                int n = HomeLogic.NextPort(cur, other);
                True(n != other && (n == 0 || HomeLogic.IsHdmi(n)), "port cycle never collides");
            }
        // live video: the wide card is exactly 16:9 (no crop); a narrow card is covered and centred; plus the bleed
        True(HomeLogic.VideoRect(100, 170, 996, 560, 4) == (96, 166, 1004, 568), "wide card + bleed");
        True(HomeLogic.VideoRect(100, 170, 684, 560, 0) == (-55, 170, 995, 560), "narrow card covered");
        // sunrise / sunset for New York (EDT = UTC-4): 25 Oct 2026 about 07:29 / 18:28 local
        var (rise, set) = HomeLogic.SunTimes(new DateTime(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc), 40.7, -74.0);
        True(Math.Abs(rise - (11 * 60 + 18)) <= 5 && Math.Abs(set - (22 * 60 + 0)) <= 5, "sun times " + rise + " " + set);
        True(!HomeLogic.IsNight(new DateTime(2026, 10, 25, 16, 0, 0, DateTimeKind.Utc), 40.7, -74.0), "noon is day");
        True(HomeLogic.IsNight(new DateTime(2026, 10, 26, 2, 0, 0, DateTimeKind.Utc), 40.7, -74.0), "10 pm is night (after UTC midnight)");
        True(HomeLogic.IsNight(new DateTime(2026, 10, 25, 9, 0, 0, DateTimeKind.Utc), 40.7, -74.0), "5 am is night");
        // a place whose day wraps past UTC midnight (Tokyo): local noon = 03:00 UTC
        True(!HomeLogic.IsNight(new DateTime(2026, 6, 21, 3, 0, 0, DateTimeKind.Utc), 35.7, 139.7), "wrapped day");
    }
}
