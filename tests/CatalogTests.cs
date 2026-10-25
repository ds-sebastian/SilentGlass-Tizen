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
        Console.WriteLine("Catalog loading, invalid-input and navigation checks passed.");
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
        var today = new DateTime(2026, 10, 24);
        var usage = InfoStats.UsageSummary(new[] { "2026-10-24\tSHIELD", "2026-10-23\tSHIELD", "2026-10-20\tSwitch", "2026-10-17\tNetflix", "bad", "2026-10-25\tFuture", "2026-10-22\t" }, today, 7);
        True(usage.Count == 2 && usage[0].name == "SHIELD" && usage[0].count == 2 && usage[1].name == "Switch", "usage window");
        True(InfoStats.Bytes(999) == "999 B" && InfoStats.Bytes(58459740) == "58.5 MB" && InfoStats.Bytes(1.5e9) == "1.5 GB", "bytes");
        True(InfoStats.Duration(11586) == "3 h 13 min" && InfoStats.Duration(30) == "1 min" && InfoStats.Duration(90000) == "1 d 1 h", "duration");
        True(InfoStats.CountMarkers(InfoStats.VendorMarkers) == 10, "all markers");
        True(InfoStats.CountMarkers(new[] { "pisa-service", "bixby-prov", "canalysis-daemon.service", "other" }) == 1, "exact marker names only");
        string tmp = Path.GetTempFileName();
        try { InfoStats.RecordLaunch(tmp, "Tab\tName", today); True(File.ReadAllText(tmp) == "2026-10-24\tTab Name\n", "record sanitises"); }
        finally { File.Delete(tmp); }
    }
}
