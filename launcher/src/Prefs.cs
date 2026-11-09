using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

// SilentGlass's own settings: silentglass-prefs.txt in its data directory, key=value lines. SilentGlass reads and
// writes it; the bridge changes it for the phone page and Home Assistant. Both use this class (it is compiled into
// both), so the defaults, the allowed values and the checks exist once. Saving keeps keys this version doesn't know.
public sealed class Prefs
{
    // The bar's shortcuts: up to four, each a name, an icon and an HDMI input. No input = hidden. Nothing here knows
    // a device: the line under a shortcut's name is the TV's own name for that input.
    public sealed class Shortcut
    {
        public string Name = "", Icon = "hdmi";
        public int Port;  // Shared.Hdmi1..Hdmi4; 0 = hidden
        public bool Shown => Port != 0;
    }
    public const int Slots = 4, NameMax = 14;
    public static readonly string[] Icons = { "tv", "gamecontroller", "desktop", "laptop", "disc", "hdmi" };

    public Shortcut[] Shortcuts = Defaults();
    public bool HideApps, InputAlerts = true;  // input alerts: the connected / unplugged toasts
    public int Dim = 5;                        // minutes without a key before the wallpaper dims; 0 = never
    public string Clock = "12";                // 12 | 24 | off
    public string Wall = "auto";               // auto (sunrise / sunset) | day | night
    public double Lat = DefaultLat, Lon = DefaultLon;
    public int Close = 20;                     // seconds of no key before the bar over a picture closes; 0 = never

    // a new install: Watch and Play are named, but point nowhere until an input is picked (Info > Shortcuts, phone)
    static Shortcut[] Defaults() => new[] {
        new Shortcut { Name = "Watch", Icon = "tv" }, new Shortcut { Name = "Play", Icon = "gamecontroller" }, new Shortcut(), new Shortcut() };

    // Until a location is set (phone page), sunrise and sunset come from the TV's time zone: its standard offset puts
    // the longitude within a few degrees (15 per hour), and a mid-northern latitude is close enough for a wallpaper.
    public const double DefaultLat = 40;
    public static readonly double DefaultLon = Math.Clamp(TimeZoneInfo.Local.BaseUtcOffset.TotalHours * 15, -180, 180);
    public static readonly string[] WallModes = { "auto", "day", "night" }, CloseTimes = { "10", "20", "30", "60", "0" },
        DimTimes = { "2", "5", "10", "30", "0" }, ClockModes = { "12", "24", "off" };
    // keys of older versions, dropped on save: watch / play (1.3: two fixed shortcuts, 0 = by the input's name)
    static readonly string[] Retired = { "watch", "play" };

    public static bool IsPort(int n) => n >= Shared.Hdmi1 && n <= Shared.Hdmi4;
    public static string Key(int slot, string field) => "shortcut" + (slot + 1) + "_" + field;
    // what a name may be: no line breaks or other control characters, at most NameMax characters
    public static string CleanName(string s)
    {
        var t = new string((s ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        return t.Length > NameMax ? t.Substring(0, NameMax).TrimEnd() : t;
    }

    public static Prefs Parse(string text)
    {
        var c = Shared.ParseConf(text);
        string Pick(string key, string fallback, string[] allowed) => c.TryGetValue(key, out var v) && Array.IndexOf(allowed, v) >= 0 ? v : fallback;
        double Num(string key, double fallback) =>
            c.TryGetValue(key, out var v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double n) && double.IsFinite(n) ? n : fallback;
        int Port(string key, int fallback) => c.TryGetValue(key, out var v) && int.TryParse(v, out int n) && (n == 0 || IsPort(n)) ? n : fallback;
        var p = new Prefs {
            HideApps = c.GetValueOrDefault("hideapps") == "1", InputAlerts = c.GetValueOrDefault("inputalerts") != "0",
            Dim = int.Parse(c.GetValueOrDefault("dim") == "1" ? "5" : Pick("dim", "5", DimTimes)),  // before v43c dim was 1 / 0
            Clock = Pick("clock", "12", ClockModes), Wall = Pick("wall", "auto", WallModes),
            Lat = Math.Clamp(Num("lat", DefaultLat), -90, 90), Lon = Math.Clamp(Num("lon", DefaultLon), -180, 180),
            Close = int.Parse(Pick("close", "20", CloseTimes)) };
        if (!c.ContainsKey(Key(0, "port")) && c.Count > 0) {
            // a file from before shortcuts: Watch and Play keep the inputs they showed. "0" was the automatic choice,
            // whose fallback inputs were HDMI 3 and HDMI 1.
            p.Shortcuts[0].Port = Port("watch", 0) is int w && w != 0 ? w : Shared.Hdmi1 + 2;
            p.Shortcuts[1].Port = Port("play", 0) is int pl && pl != 0 ? pl : Shared.Hdmi1;
        } else
            for (int i = 0; i < Slots; i++) {
                var s = p.Shortcuts[i];
                if (c.TryGetValue(Key(i, "name"), out var n)) s.Name = CleanName(n);
                s.Icon = Pick(Key(i, "icon"), s.Icon, Icons);
                s.Port = Port(Key(i, "port"), 0);
            }
        // one input, one shortcut: a later one that repeats an input is hidden
        for (int i = 1; i < Slots; i++)
            if (p.Shortcuts.Take(i).Any(o => o.Shown && o.Port == p.Shortcuts[i].Port)) p.Shortcuts[i].Port = 0;
        return p;
    }

    public Dictionary<string, string> Values()
    {
        var d = new Dictionary<string, string>();
        for (int i = 0; i < Slots; i++) {
            var s = Shortcuts[i];
            d[Key(i, "name")] = s.Name; d[Key(i, "icon")] = s.Icon; d[Key(i, "port")] = s.Port.ToString(CultureInfo.InvariantCulture);
        }
        d["hideapps"] = HideApps ? "1" : "0"; d["dim"] = Dim.ToString(CultureInfo.InvariantCulture); d["wall"] = Wall; d["clock"] = Clock;
        d["inputalerts"] = InputAlerts ? "1" : "0"; d["lat"] = Lat.ToString(CultureInfo.InvariantCulture); d["lon"] = Lon.ToString(CultureInfo.InvariantCulture);
        d["close"] = Close.ToString(CultureInfo.InvariantCulture);
        return d;
    }
    public string Format() => string.Concat(Values().Select(kv => kv.Key + "=" + kv.Value + "\n"));
    // the file to write: these settings, plus any key a newer version (or the bridge) put there that this one doesn't know
    public string Merge(string old)
    {
        var d = Shared.ParseConf(old);
        foreach (var k in Retired) d.Remove(k);
        foreach (var kv in Values()) d[kv.Key] = kv.Value;
        return string.Concat(d.Select(kv => kv.Key + "=" + kv.Value + "\n"));
    }

    // write the settings file whole or not at all: SilentGlass and the bridge each watch it, and a half-written file
    // would read as defaults. The rename is the one change the watcher sees (Setup.cs WatchData: Renamed).
    public void Save(string path)
    {
        string old = null; try { old = System.IO.File.ReadAllText(path); } catch { }
        System.IO.File.WriteAllText(path + ".new", Merge(old)); System.IO.File.Move(path + ".new", path, true);
    }

    // null when the shortcuts are fine, else what to fix (the phone page shows it)
    public string Problem()
    {
        var used = new HashSet<int>();
        foreach (var s in Shortcuts) {
            if (s.Port != 0 && !IsPort(s.Port)) return "Pick an input for each shortcut, or Hidden.";
            if (Array.IndexOf(Icons, s.Icon) < 0) return "Pick an icon for each shortcut.";
            if (s.Name != CleanName(s.Name)) return "Shortcut names have up to " + NameMax + " characters, on one line.";
            if (s.Shown && !used.Add(s.Port)) return "Two shortcuts can't use the same input.";
        }
        return null;
    }
    // Left / Right on a shortcut in Info: hidden -> HDMI 1..4 -> hidden, skipping inputs other shortcuts use
    public int NextPort(int slot, int dir = 1)
    {
        var taken = Shortcuts.Where((s, i) => i != slot && s.Shown).Select(s => s.Port).ToHashSet();
        int n = Shortcuts[slot].Port;
        do n = dir >= 0 ? (n == 0 ? Shared.Hdmi1 : n >= Shared.Hdmi4 ? 0 : n + 1) : (n == 0 ? Shared.Hdmi4 : n <= Shared.Hdmi1 ? 0 : n - 1);
        while (n != 0 && taken.Contains(n));
        return n;
    }
    public Shortcut For(int port) => port == 0 ? null : Array.Find(Shortcuts, s => s.Port == port);
}
