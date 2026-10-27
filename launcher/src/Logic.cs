using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

// SilentGlass logic with no NUI types, so tests/CatalogTests.cs runs it off the TV: preferences, Watch / Play input
// choices, the TV's Game Mode names, notification layout and sunrise / sunset for the wallpaper.
public static class Logic
{
    // ---- key=value text (preferences, small status files) ----
    public static Dictionary<string, string> ParseConf(string text)
    {
        var d = new Dictionary<string, string>();
        foreach (var line in (text ?? "").Split('\n')) { int i = line.IndexOf('='); if (i > 0) d[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim(); }
        return d;
    }
    public static bool Flag(Dictionary<string, string> c, string key, bool fallback) => c.TryGetValue(key, out var v) ? v == "1" : fallback;
    public static double Num(Dictionary<string, string> c, string key, double fallback) =>
        c.TryGetValue(key, out var v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double n) && double.IsFinite(n) ? n : fallback;
    public static string Pick(Dictionary<string, string> c, string key, string fallback, params string[] allowed) =>
        c.TryGetValue(key, out var v) && Array.IndexOf(allowed, v) >= 0 ? v : fallback;

    // a flat JSON object read as strings (setup.json, notice.json from the bridge); missing keys read as ""
    public sealed class Json
    {
        readonly Dictionary<string, string> d = new();
        public Json(string json)
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var p in doc.RootElement.EnumerateObject()) d[p.Name] = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? "" : p.Value.ToString();
        }
        public string this[string key] => d.TryGetValue(key, out var v) ? v : "";
        public int Int(string key) => int.TryParse(this[key], out var n) ? n : 0;
        public long Long(string key) => long.TryParse(this[key], out var n) ? n : 0;
    }

    // ---- preferences (the app's data directory, so they survive reboots); unknown keys keep their defaults ----
    public const int Hdmi1 = 13, Hdmi4 = 16;
    public static bool IsHdmi(int type) => type >= Hdmi1 && type <= Hdmi4;
    // ---- which inputs have a device that is on, from HDMI-CEC (Cec.cs): a player the TV lists on that port. CEC drops
    // a device that sleeps. Plugged straight into the TV it must also not answer "standby" to the port's power query;
    // behind a receiver (soundbar) that query answers for the receiver, so being listed is the signal. ----
    public static HashSet<int> DevicesOn(IEnumerable<(int port, string type)> devices, Func<int, int> power)
    {
        var on = new HashSet<int>(); var receivers = new HashSet<int>(); var players = new List<int>();
        foreach (var (port, type) in devices) {
            if (port < 1 || port > 4 || string.IsNullOrEmpty(type) || type.StartsWith("TV")) continue;
            if (type.StartsWith("AUDIO")) receivers.Add(port); else if (!players.Contains(port)) players.Add(port);
        }
        foreach (int p in players)
            if (receivers.Contains(p) || power(p) is not (1 or 3)) on.Add(Hdmi1 - 1 + p);
        return on;
    }
    public sealed class Prefs
    {
        public int Watch, Play;            // 0 = Automatic (by the TV's input name)
        public bool HideApps, Dim = true;
        public string Wall = "auto";       // auto (sunrise / sunset) | day | night
        public double Lat = DefaultLat, Lon = DefaultLon;
    }
    // Until a location is set (phone page), sunrise and sunset come from the TV's time zone: its standard offset puts
    // the longitude within a few degrees (15 per hour), and a mid-northern latitude is close enough for a wallpaper.
    public const double DefaultLat = 40;
    public static readonly double DefaultLon = Math.Clamp(TimeZoneInfo.Local.BaseUtcOffset.TotalHours * 15, -180, 180);
    public static readonly string[] WallModes = { "auto", "day", "night" };
    public static Prefs ParsePrefs(string text)
    {
        var c = ParseConf(text);
        int Port(string k) => c.TryGetValue(k, out var v) && int.TryParse(v, out int n) && (n == 0 || IsHdmi(n)) ? n : 0;
        return new Prefs { Watch = Port("watch"), Play = Port("play"), HideApps = Flag(c, "hideapps", false), Dim = Flag(c, "dim", true),
            Wall = Pick(c, "wall", "auto", WallModes), Lat = Math.Clamp(Num(c, "lat", DefaultLat), -90, 90), Lon = Math.Clamp(Num(c, "lon", DefaultLon), -180, 180) };
    }
    public static string FormatPrefs(Prefs p) =>
        "watch=" + p.Watch + "\nplay=" + p.Play + "\nhideapps=" + (p.HideApps ? 1 : 0) + "\ndim=" + (p.Dim ? 1 : 0) + "\nwall=" + p.Wall +
        "\nlat=" + p.Lat.ToString(CultureInfo.InvariantCulture) + "\nlon=" + p.Lon.ToString(CultureInfo.InvariantCulture) + "\n";
    public static string NextWall(string mode, int dir = 1) => WallModes[((Array.IndexOf(WallModes, mode) + dir) % 3 + 3) % 3];
    public static string WallName(string mode) => mode == "day" ? "Day" : mode == "night" ? "Night" : "Follows sunset";

    // Watch / Play: dir +1 cycles Automatic -> HDMI 1..4 -> Automatic (dir -1 backwards), skipping the other one's input
    public static int NextPort(int current, int other, int dir = 1)
    {
        int n = current;
        do n = dir >= 0 ? (n == 0 ? Hdmi1 : n >= Hdmi4 ? 0 : n + 1) : (n == 0 ? Hdmi4 : n <= Hdmi1 ? 0 : n - 1); while (n != 0 && n == other);
        return n;
    }

    // ---- the TV's one Game Mode setting (Off / On / Auto) in plain words ----
    public static string GameName(string tvName) => tvName switch { "Auto" => "Automatic", "On" => "Always", "Off" => "Never", _ => tvName };

    // ---- notification banner geometry (tools/overlay_mockup.py draws the same) ----
    public const int NoticeMargin = 56;
    public static int NoticeWidth(string size) => size switch { "small" => 420, "medium" => 520, "max" => 800, _ => 640 };
    // the image keeps its own shape between 16:9 and 3:2 (taller ones are cropped to 3:2)
    public static int NoticeImageHeight(int innerW, int w, int h) => w <= 0 || h <= 0 ? innerW * 9 / 16 : (int)(innerW * Math.Clamp((double)h / w, 9 / 16.0, 2 / 3.0));
    public static (int x, int y) NoticeOrigin(string position, int w, int h) => position switch {
        "top-right" => (1920 - NoticeMargin - w, NoticeMargin), "top-left" => (NoticeMargin, NoticeMargin), "bottom-left" => (NoticeMargin, 1080 - NoticeMargin - h),
        "center" => ((1920 - w) / 2, (1080 - h) / 2), _ => (1920 - NoticeMargin - w, 1080 - NoticeMargin - h) };

    // ---- sunrise / sunset in minutes after UTC midnight (NOAA approximation, within a couple of minutes) ----
    public static (double rise, double set) SunTimes(DateTime utc, double lat, double lon)
    {
        double g = 2 * Math.PI / 365 * (utc.DayOfYear - 1);
        double eq = 229.18 * (0.000075 + 0.001868 * Math.Cos(g) - 0.032077 * Math.Sin(g) - 0.014615 * Math.Cos(2 * g) - 0.040849 * Math.Sin(2 * g));
        double de = 0.006918 - 0.399912 * Math.Cos(g) + 0.070257 * Math.Sin(g) - 0.006758 * Math.Cos(2 * g) + 0.000907 * Math.Sin(2 * g) - 0.002697 * Math.Cos(3 * g) + 0.00148 * Math.Sin(3 * g);
        double la = lat * Math.PI / 180, c = Math.Cos(90.833 * Math.PI / 180) / (Math.Cos(la) * Math.Cos(de)) - Math.Tan(la) * Math.Tan(de);
        double ha = Math.Acos(Math.Clamp(c, -1, 1)) * 180 / Math.PI;
        static double M(double m) => ((m % 1440) + 1440) % 1440;
        return (M(720 - 4 * (lon + ha) - eq), M(720 - 4 * (lon - ha) - eq));
    }
    // UTC based, so the TV's time zone setting cannot break it; the day may wrap past UTC midnight
    public static bool IsNight(DateTime utc, double lat, double lon)
    {
        var (rise, set) = SunTimes(utc, lat, lon); double now = utc.Hour * 60 + utc.Minute;
        return !(rise < set ? now >= rise && now < set : now >= rise || now < set);
    }
}
