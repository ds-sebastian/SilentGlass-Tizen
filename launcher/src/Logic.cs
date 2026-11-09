using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

// SilentGlass logic with no NUI types, so tests/CatalogTests.cs runs it off the TV: which devices are on (HDMI-CEC),
// setting names and cycles, notification layout and sunrise / sunset for the wallpaper. The settings file: Prefs.cs.
public static class Logic
{
    public static double Num(Dictionary<string, string> c, string key, double fallback) =>
        c.TryGetValue(key, out var v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double n) && double.IsFinite(n) ? n : fallback;

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
            if (receivers.Contains(p) || power(p) is not (1 or 3)) on.Add(Shared.Hdmi1 - 1 + p);
        return on;
    }
    public static string NextWall(string mode, int dir = 1) => Prefs.WallModes[((Array.IndexOf(Prefs.WallModes, mode) + dir) % 3 + 3) % 3];
    public static int NextDim(int minutes, int dir = 1)
    {
        int i = Array.IndexOf(Prefs.DimTimes, minutes.ToString()), n = Prefs.DimTimes.Length;
        return int.Parse(Prefs.DimTimes[(((i < 0 ? 1 : i) + dir) % n + n) % n]);
    }
    public static string DimName(int minutes) => minutes == 0 ? "Never" : minutes + " min";
    // the wallpaper clock (big = with AM / PM in 12-hour mode; the dimmed clock never shows it)
    public static string ClockText(string mode, DateTime t, bool big) =>
        mode == "off" ? "" : mode == "24" ? t.ToString("H:mm", CultureInfo.InvariantCulture) : t.ToString(big ? "h:mm tt" : "h:mm", CultureInfo.InvariantCulture);
    // Sleep Timer's value: "None", or the time left when SilentGlass started it (the TV reports only the length it was
    // set to, e.g. "30 minutes"), else that length. minutesAgo < 0: not started here.
    public static string SleepText(string value, string name, double minutesAgo)
    {
        if (value == "0" || value.Length == 0) return "None";
        var m = System.Text.RegularExpressions.Regex.Match(name ?? "", @"^(\d+)\s*min");
        if (!m.Success) return name;
        int total = int.Parse(m.Groups[1].Value);
        if (minutesAgo < 0 || minutesAgo >= total) return total + " min";
        int left = (int)Math.Ceiling(total - minutesAgo);
        return (left >= 60 ? left / 60 + " h" + (left % 60 > 0 ? " " + left % 60 + " min" : "") : left + " min") + " left";
    }
    public static string WallName(string mode) => mode == "day" ? "Day" : mode == "night" ? "Night" : "Follows sunset";

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
