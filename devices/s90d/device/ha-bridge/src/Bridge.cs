using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

// SilentGlass bridge: the TV in Home Assistant over MQTT discovery (state + a fixed set of commands), notifications
// on the TV (Notices.cs), and the phone setup page (Setup.cs). Runs as the TV's owner user (not root), no dependencies.
// Config: /opt/usr/home/owner/.config/silentglass-ha.conf (mode 600), written by the setup page (or by hand, see
// silentglass-ha.conf.example). Without it the bridge waits; notifications and setup work anyway.
// Log (events only): /run/user/5001/silentglass-ha.log. Status for SilentGlass's Info panel: data/bridge-status.txt.
// What it shares with SilentGlass (app ids, the files in its data directory) is launcher/src/Shared.cs.
static partial class Bridge
{
    const string Base = "silentglass/s90d", Node = "silentglass_s90d";
    // overridable only for the off-TV tests (tests/test_s90d_setup.py runs the bridge with --no-tv)
    static readonly string Data = Environment.GetEnvironmentVariable("SG_DATA") ?? "/opt/usr/home/owner/apps_rw/com.samsung.tv.bixby-promotion/data";
    static readonly string ConfPath = Environment.GetEnvironmentVariable("SG_CONF") ?? "/opt/usr/home/owner/.config/silentglass-ha.conf";
    static string DataFile(string name) => Data + "/" + name;
    static bool noTv;
    const string Version = "2.8.2";
    const int Mbr = -2;  // the TV keeps names per "activity index"; -2 is the default slot (Samsung's own binding uses it)
    static Dictionary<string, string> conf = new();
    static Mqtt mqtt;
    static readonly Dictionary<string, string> last = new();
    static readonly object gate = new(), tv = new();
    static string inputSig = "", optionsSig = "";
    static volatile bool reload;
    static volatile string mqttState = "unconfigured", mqttError = "";

    // the border router's switch (also on the phone page): thread-off survives reboots, and quiet-otbr-switch.path
    // watches thread-switch and applies the change at once
    static void SetThreadSwitch(bool on)
    {
        SetMarker(Shared.ThreadOff, !on);
        File.WriteAllText(DataFile(Shared.ThreadSwitch), DateTime.UtcNow.ToString("o") + (on ? " on\n" : " off\n"));
    }
    static void SetMarker(string name, bool present) { if (present) File.WriteAllText(DataFile(name), ""); else File.Delete(DataFile(name)); }
    static void Log(string s) { if (noTv) Console.WriteLine(s); try { File.AppendAllText("/run/user/5001/silentglass-ha.log", DateTime.Now.ToString("MM-dd HH:mm:ss ") + s + "\n"); } catch { } }
    // the TV's own name (TvApi TvNameKey), unless the config sets one: older installs, or the TV refused the change
    static string TvName { get { string n = conf.GetValueOrDefault("name", ""); if (n.Length == 0 && !noTv) n = VStr(TvNameKey); return n.Length > 0 ? n : "Samsung TV"; } }
    // "Living Room TV" -> "living_room_tv": the entity ids Home Assistant picks when it first sees the TV
    public static string Slug(string s) { var t = Regex.Replace((s ?? "").ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_'); return t.Length > 0 ? t : "samsung_tv"; }
    static string discoveredName = "";
    static bool Configured => conf.GetValueOrDefault("host", "").Length > 0;
    // how often volume, mute, input names and the rest are checked (power, input and app are sent when they change);
    // temperature, memory, Thread and picture settings every minute
    public static readonly int[] UpdateChoices = { 2, 5, 10, 30, 60 };
    public const int DefaultUpdate = 10, SlowSeconds = 60;
    public static int UpdateSeconds(Dictionary<string, string> c) => int.TryParse(c.GetValueOrDefault("update", ""), out int s) && UpdateChoices.Contains(s) ? s : DefaultUpdate;

    // ---- config: key=value lines; the file is the owner's alone (it holds the broker password) ----
    static Dictionary<string, string> ReadConf(string path) { try { return Shared.ParseConf(File.ReadAllText(path)); } catch { return new(); } }
    static Dictionary<string, string> LoadConf() => ReadConf(ConfPath);
    static void SaveConf(Dictionary<string, string> c)
    {
        string tmp = ConfPath + ".new";
        File.WriteAllText(tmp, ""); Run("/bin/chmod", "600 " + tmp, 3000);  // private before anything is written
        File.AppendAllText(tmp, "# SilentGlass bridge - written by the setup page (Info > Advanced settings on the TV)\n" +
            string.Join("", new[] { "host", "port", "user", "password", "name", "notify_from", "notify_position", "notify_size", "notify_duration", "notify_images", "notify_quiet", "update" }.Where(c.ContainsKey).Select(k => k + "=" + c[k] + "\n")));
        File.Move(tmp, ConfPath, true);
    }
    // what the Info panel shows (rewritten on every change and once a minute, so a stale file means "not running")
    static void SetState(string state, string error)
    {
        mqttState = state; mqttError = error ?? "";
        try { File.WriteAllText(DataFile(Shared.BridgeStatus), "mqtt=" + state + "\nerror=" + mqttError.Replace('\n', ' ') + "\nversion=" + Version + "\nname=" + TvName + "\nat=" + DateTime.UtcNow.ToString("o") + "\n"); } catch { }
    }

    // ---- publishing ----
    static void Pub(string sub, string value, bool force = false)
    {
        lock (gate) {
            if (!force && last.TryGetValue(sub, out var v) && v == value) return;
            if (mqtt?.Publish(Base + "/" + sub, value, true) == true) last[sub] = value;
        }
    }
    static void PublishFast(bool force)
    {
        var ins = Inputs();
        // new names change the selector's options: announce them first, or Home Assistant rejects the new state
        // the same for a new TV name (the device's name in Home Assistant follows it)
        if (InputSig(ins) != inputSig || TvName != discoveredName) { Discovery(); force = true; }
        Pub("power", ScreenOn ? "ON" : "OFF", force);
        int src = SourceId(VStr(SourceKey));
        string input = ins.Where(i => i.Type == src).Select(i => i.Name).FirstOrDefault() ?? "Other";
        Pub("input", input, force);
        Pub("app", AppName(OnScreenApp(VStr(ForegroundKey)), input), force);
        foreach (var i in ins.Where(i => i.Type >= 13)) {
            Pub("hdmi" + (i.Type - 12) + "_name", i.Edit, force);
            Pub("hdmi" + (i.Type - 12) + "_attr", "{\"detected_name\":\"" + J(i.Detected) + "\",\"shown_as\":\"" + J(i.Name) + "\",\"connected\":" + (i.Connected ? "true" : "false") + "}", force);
        }
        int vol = -1, mute = -1;
        lock (tv) { try { if (sound_manager_get_master_volume(out var v) == 0) vol = v; if (sound_manager_get_master_mute(out var m) == 0) mute = m; } catch { } }
        if (vol >= 0) Pub("volume", vol.ToString(), force);
        if (mute >= 0) Pub("mute", mute == 1 ? "ON" : "OFF", force);
        if (ThreadInstalled) Pub("thread_switch", ThreadWanted ? "ON" : "OFF", force);
        Pub("notifications", NoticesOn ? "ON" : "OFF", force);
    }
    static void PublishSlow(bool force)
    {
        // the picture/sound mode lists take dozens of settings calls: checked here, not with every state check
        if (OptionsSig() != optionsSig) { Discovery(); PublishFast(true); force = true; }
        if (int.TryParse(Read("/sys/devices/platform/soc/thermal/temperature").Trim(), out int t)) Pub("temperature", t.ToString(), force);
        var mem = Regex.Match(Read("/proc/meminfo"), @"MemAvailable:\s+(\d+)");
        if (mem.Success) Pub("memory", (long.Parse(mem.Groups[1].Value) / 1024).ToString(), force);
        if (ThreadInstalled) Pub("thread", ThreadRole(), force);
        foreach (var id in new[] { "brightness", "contrast" }) { var v = Setting(id); if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) Pub(id, v, force); }
        Pub("picture_mode", OptionName(Options("picturemode"), Setting("picturemode")), force);
        Pub("sound_mode", OptionName(Options("external-sound-mode"), Setting("external-sound-mode")), force);
        Pub("sound_output", OptionName(Outputs.ToList(), Setting("soundoutput")) is var o && o.Length > 0 ? o : "Other", force);
        Pub("sleep_timer", OptionName(Options("sleeptimer"), Setting("sleeptimer")), force);
        Pub("game_mode", OptionName(GameOptions(), Setting("game-mode-auto")), force);
    }
    // discovery depends on what the TV currently offers (input names, picture/sound modes)
    static string InputSig(List<Shared.Input> ins) => string.Join("|", ins.Select(i => i.Name));
    static string OptionsSig() => string.Join("|", Options("picturemode").Select(o => o.name)) + "#" + string.Join("|", Options("external-sound-mode").Select(o => o.name));

    static string Device() => "{\"identifiers\":[\"" + Node + "\"],\"name\":\"" + J(TvName) +
        "\",\"manufacturer\":\"Samsung\",\"model\":\"" + J((noTv ? "" : Model()) is { Length: > 0 } m ? m : "Samsung TV") + " (SilentGlass-Tizen)\",\"sw_version\":\"silentglass-ha " + Version + "\"}";
    static string J(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
    static string List(IEnumerable<string> xs) => "[" + string.Join(",", xs.Select(x => "\"" + J(x) + "\"")) + "]";
    static void Discovery()
    {
        var ins = Inputs(); inputSig = InputSig(ins); optionsSig = OptionsSig(); discoveredName = TvName; string slug = Slug(discoveredName);
        string common(string obj, string name, string icon, bool diag = false, bool config = false) => "\"unique_id\":\"" + Node + "_" + obj + "\",\"object_id\":\"" + slug + "_" + obj +
            "\",\"name\":\"" + name + "\",\"icon\":\"" + icon + "\",\"availability_topic\":\"" + Base + "/availability\",\"device\":" + Device() +
            (diag ? ",\"entity_category\":\"diagnostic\"" : config ? ",\"entity_category\":\"config\"" : "");
        string st(string obj, bool cmd = true) => ",\"state_topic\":\"" + Base + "/" + obj + "\"" + (cmd ? ",\"command_topic\":\"" + Base + "/" + obj + "/set\"" : "");
        void cfg(string comp, string obj, string body) => mqtt.Publish("homeassistant/" + comp + "/" + Node + "/" + obj + "/config", body == null ? "" : "{" + body + "}", true);
        cfg("switch", "power_switch", common("power", "Power", "mdi:power") + ",\"state_topic\":\"" + Base + "/power\",\"command_topic\":\"" + Base + "/power/set\"");
        cfg("select", "input", common("input", "Input", "mdi:video-input-hdmi") + st("input") + ",\"options\":" + List(ins.Select(i => i.Name).Append("Other")));
        cfg("sensor", "app", common("app", "App", "mdi:application") + st("app", false));
        cfg("number", "volume", common("volume", "Volume", "mdi:volume-high") + st("volume") + ",\"min\":0,\"max\":100,\"step\":1,\"mode\":\"slider\"");
        cfg("switch", "mute", common("mute", "Mute", "mdi:volume-off") + st("mute"));
        var br = Range("brightness", 0, 50); var co = Range("contrast", 0, 50);
        cfg("number", "brightness", common("brightness", "Brightness", "mdi:brightness-6") + st("brightness") + ",\"min\":" + br.min + ",\"max\":" + br.max + ",\"step\":1,\"mode\":\"slider\"");
        cfg("number", "contrast", common("contrast", "Contrast", "mdi:contrast-box", config: true) + st("contrast") + ",\"min\":" + co.min + ",\"max\":" + co.max + ",\"step\":1,\"mode\":\"slider\"");
        cfg("select", "picture_mode", common("picture_mode", "Picture mode", "mdi:palette") + st("picture_mode") + ",\"options\":" + List(Options("picturemode").Select(o => o.name).DefaultIfEmpty("Unavailable")));
        cfg("select", "sound_mode", common("sound_mode", "Sound mode", "mdi:surround-sound") + st("sound_mode") + ",\"options\":" + List(Options("external-sound-mode").Select(o => o.name).DefaultIfEmpty("Unavailable")));
        cfg("select", "sound_output", common("sound_output", "Sound output", "mdi:speaker") + st("sound_output") + ",\"options\":" + List(Outputs.Select(o => o.name).Append("Other")));
        cfg("select", "sleep_timer", common("sleep_timer", "Sleep timer", "mdi:timer-sand") + st("sleep_timer") + ",\"options\":" + List(Options("sleeptimer").Select(o => o.name).DefaultIfEmpty("Off")));
        cfg("select", "game_mode_select", common("game_mode", "Game mode", "mdi:controller", config: true) + st("game_mode") + ",\"options\":" + List(GameOptions().Select(o => o.name).DefaultIfEmpty("Unavailable")));
        cfg("switch", "notifications", common("notifications", "Notifications", "mdi:message-badge", config: true) + st("notifications"));
        cfg("button", "home", common("home", "Home", "mdi:home") + ",\"command_topic\":\"" + Base + "/home/press\"");
        cfg("button", "ambient", common("ambient", "Ambient", "mdi:image-frame") + ",\"command_topic\":\"" + Base + "/ambient/press\"");
        cfg("button", "resync", common("resync", "Resync input names", "mdi:refresh", config: true) + ",\"command_topic\":\"" + Base + "/resync/press\"");
        for (int p = 1; p <= 4; p++)
            cfg("text", "hdmi" + p + "_name", common("hdmi" + p + "_name", "HDMI " + p + " name", "mdi:rename", config: true) + st("hdmi" + p + "_name") +
                ",\"max\":30,\"json_attributes_topic\":\"" + Base + "/hdmi" + p + "_attr\"");
        cfg("sensor", "temperature", common("temperature", "Chip temperature", "mdi:thermometer", diag: true) + st("temperature", false) + ",\"device_class\":\"temperature\",\"unit_of_measurement\":\"°C\",\"state_class\":\"measurement\"");
        cfg("sensor", "memory", common("memory", "Free memory", "mdi:memory", diag: true) + st("memory", false) + ",\"device_class\":\"data_size\",\"unit_of_measurement\":\"MB\",\"state_class\":\"measurement\"");
        if (ThreadInstalled) {
            cfg("sensor", "thread", common("thread", "Thread role", "mdi:access-point-network") + st("thread", false));
            cfg("switch", "thread_switch", common("thread_switch", "Thread border router", "mdi:access-point") + st("thread_switch"));
        }
    }

    // ---- commands (fixed allowlist; no shell) ----
    static void OnMessage(string topic, string payload)
    {
        if (topic == "homeassistant/status") { if (payload == "online") Resync(); return; }
        var cmd = topic.Substring(Base.Length + 1);
        Log("command " + cmd + " " + (payload.Length > 40 ? payload.Substring(0, 40) : payload));
        bool slow = true;
        switch (cmd) {
            case "power/set" when payload == "ON" || payload == "OFF":
                // the key toggles: press only when the TV is not already in the requested state
                if ((payload == "ON") != ScreenOn) { PressPower(); Log("power key pressed (" + payload + ")"); }
                Thread.Sleep(3000); slow = false; break;
            case "input/set": {
                var match = Inputs().Where(i => i.Name == payload).ToList();
                if (match.Count == 1) LaunchApp(Shared.Self, Shared.ExtraSource, match[0].Type.ToString());
                slow = false; break;
            }
            case "home/press": LaunchApp(Shared.Self); slow = false; break;
            case "ambient/press": LaunchApp(Shared.Art); slow = false; break;
            case "resync/press": ResyncNames(); break;
            case "thread_switch/set" when ThreadInstalled && (payload == "ON" || payload == "OFF"):
                SetThreadSwitch(payload == "ON"); slow = false; break;
            case "volume/set" when double.TryParse(payload, NumberStyles.Float, CultureInfo.InvariantCulture, out var vol):
                lock (tv) sound_manager_set_master_volume((int)Math.Clamp(Math.Round(vol), 0, 100)); slow = false; break;
            case "mute/set" when payload == "ON" || payload == "OFF":
                lock (tv) sound_manager_set_master_mute(payload == "ON" ? 1 : 0); slow = false; break;
            case "brightness/set" or "contrast/set" when double.TryParse(payload, NumberStyles.Float, CultureInfo.InvariantCulture, out var n): {
                var id = cmd.Split('/')[0]; var r = Range(id, 0, 50);
                SetSetting(id, ((int)Math.Clamp(Math.Round(n), r.min, r.max)).ToString(CultureInfo.InvariantCulture)); break;
            }
            case "picture_mode/set": SetOption("picturemode", Options("picturemode"), payload); break;
            case "sound_mode/set": SetOption("external-sound-mode", Options("external-sound-mode"), payload); break;
            case "sleep_timer/set": SetOption("sleeptimer", Options("sleeptimer"), payload); break;
            case "sound_output/set": SetOption("soundoutput", Outputs.ToList(), payload); break;
            case "game_mode/set": SetOption("game-mode-auto", GameOptions(), payload); break;
            case "notifications/set" when payload == "ON" || payload == "OFF":
                SetMarker(Shared.NotifyOff, payload == "OFF"); slow = false; break;
            default: {
                var m = Regex.Match(cmd, "^hdmi([1-4])_name/set$");
                if (m.Success) {
                    var name = Regex.Replace(payload, @"[\x00-\x1f]", "").Trim(); if (name.Length > 30) name = name.Substring(0, 30);
                    bool ok; lock (tv) ok = set_edit_name(12 + int.Parse(m.Groups[1].Value), name, Mbr);
                    Log("rename HDMI " + m.Groups[1].Value + " -> '" + name + "' " + (ok ? "ok" : "FAILED"));
                }
                break;
            }
        }
        Thread.Sleep(400); PublishFast(false); if (slow) PublishSlow(false);
    }
    static void SetOption(string id, List<(string value, string name)> opts, string name)
    {
        var o = opts.Where(x => x.name == name).ToList();
        if (o.Count == 1) Log(id + " -> " + name + " " + (SetSetting(id, o[0].value) ? "ok" : "FAILED"));
    }
    static void LaunchApp(string app, string key = null, string value = null) =>
        Log("launch " + app + " -> " + Run("/usr/bin/launch_app", key == null ? app : app + " " + key + " " + value, 8000).Trim());

    static void Resync() { Discovery(); PublishFast(true); PublishSlow(true); }

    static int Main(string[] args)
    {
        noTv = args.Contains("--no-tv");  // tests: setup page and broker connection only, no TV APIs, nothing published
        Log("start " + Version);
        if (!noTv) { StartSourceWatch(); OpenPowerKey(); StartNoticeServer(); }
        StartSetupWatch();
        int backoff = 5;
        while (true) {
            // in this order: the setup page waits until reload is clear and the state is no longer "connecting", and a
            // save that lands while the file is read is not lost (it sets reload again)
            if (LoadConf().GetValueOrDefault("host", "").Length > 0) SetState("connecting", mqttError);
            reload = false; conf = LoadConf();
            if (!Configured) {
                Log("not configured: waiting for the setup page");
                for (int i = 0; !reload; i++) { if (i % 60 == 0) SetState("unconfigured", ""); Thread.Sleep(1000); }
                continue;
            }
            try {
                mqtt = new Mqtt(conf["host"], int.Parse(conf.GetValueOrDefault("port", "1883")), "silentglass-s90d", conf.GetValueOrDefault("user"), conf.GetValueOrDefault("password"),
                    Base + "/availability", "offline", OnMessage);
                mqtt.Closed = () => changed.Set();
                Log("connected"); backoff = 5; SetState("connected", "");
                if (noTv) while (mqtt.Alive && !reload) Thread.Sleep(200); else Serve();
                Log(reload ? "reconnecting with the new settings" : "disconnected");
                if (!reload) SetState("error", "connection lost");
            } catch (Exception e) { Log("error " + e.GetType().Name + ": " + e.Message); SetState("error", e.Message); }
            try { mqtt?.Close(); } catch { }
            mqtt = null;
            for (int i = 0; i < backoff && !reload; i++) Thread.Sleep(1000);
            if (!reload) backoff = Math.Min(backoff * 2, 60);
        }
    }

    // connected: announce the TV, then publish until the connection drops or the settings change
    static void Serve()
    {
        mqtt.Subscribe(Base + "/+/set", Base + "/+/press", "homeassistant/status");
        lock (gate) last.Clear();
        Resync(); mqtt.Publish(Base + "/availability", "online", true);
        int every = UpdateSeconds(conf); Log("updates every " + every + " s, and on changes");
        var clock = Stopwatch.StartNew(); long nextFast = every * 1000L, nextSlow = SlowSeconds * 1000L, nextFull = 300_000;
        while (mqtt.Alive && !reload) {
            // sleeps until the next check, a change notification (published 0.3 s later: they come in bursts), a lost
            // connection or new settings
            long wait = Math.Min(nextFast, Math.Min(nextSlow, nextFull)) - clock.ElapsedMilliseconds;
            bool ev = changed.WaitOne((int)Math.Clamp(wait, 1, 300_000));
            if (!mqtt.Alive || reload) break;
            if (ev) { Thread.Sleep(300); changed.Reset(); }
            long now = clock.ElapsedMilliseconds;
            bool full = now >= nextFull, slow = full || now >= nextSlow;
            if (ev || full || now >= nextFast) { PublishFast(full); nextFast = now + every * 1000L; }
            if (slow) {
                PublishSlow(full); SetState("connected", ""); nextSlow = now + SlowSeconds * 1000L;
                var counts = keyEvents.Select((_, i) => Interlocked.Exchange(ref keyEvents[i], 0)).ToArray();
                if (counts.Any(c => c > 0)) Log("changes in the last minute: power " + counts[0] + ", input " + counts[1] + ", app " + counts[2] + ", name " + counts[3] + ", input list " + counts[4]);
            }
            if (full) nextFull = now + 300_000;
        }
    }
}
