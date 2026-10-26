using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

// SilentGlass Home Assistant bridge: publishes the TV's state over MQTT with Home Assistant discovery and accepts a
// fixed set of commands. Runs as the TV's owner user (not root). Minimal MQTT 3.1.1 client, no dependencies.
// Config: /opt/usr/home/owner/.config/silentglass-ha.conf (mode 600): host, port, user, password, optional name,
// optional input_<id>=Label (ids: HDMI1=13 ... HDMI4=16). Log (events only): /run/user/5001/silentglass-ha.log
static class Bridge
{
    const string Base = "silentglass/s90d", Node = "silentglass_s90d";
    const string Launcher = "com.samsung.tv.bixby-promotion", ArtApp = "org.tizen.art-app";
    const string Data = "/opt/usr/home/owner/apps_rw/com.samsung.tv.bixby-promotion/data";
    const string ThreadUnit = "/opt/usr/home/owner/.config/systemd/user/quiet-otbr-switch.path";
    static readonly string Version = "1.0";
    static Dictionary<string, string> conf;
    static Mqtt mqtt;
    static readonly Dictionary<string, string> last = new();
    static readonly object gate = new();

    static void Log(string s) { try { File.AppendAllText("/run/user/5001/silentglass-ha.log", DateTime.Now.ToString("MM-dd HH:mm:ss ") + s + "\n"); } catch { } }

    // ---- TV state ----
    [DllImport("libvconf.so.0")] static extern int vconf_get_int(string key, out int value);
    [DllImport("libvconf.so.0")] static extern IntPtr vconf_get_str(string key);
    [DllImport("libc.so.6")] static extern void free(IntPtr p);
    static int VInt(string k) => vconf_get_int(k, out int v) == 0 ? v : -1;
    static string VStr(string k) { var p = vconf_get_str(k); if (p == IntPtr.Zero) return ""; var s = Marshal.PtrToStringUTF8(p); free(p); return s ?? ""; }

    public static bool ThreadInstalled => File.Exists(ThreadUnit);
    static bool ThreadWanted => !File.Exists(Data + "/thread-off");

    // inputs as SilentGlass Home sees them (type,name,...;...), labels from the config win
    static List<(int id, string name)> Inputs()
    {
        var list = new List<(int, string)>();
        try {
            var line = File.ReadLines("/tmp/quiet-home-state.txt").FirstOrDefault(l => l.StartsWith("inputs="));
            foreach (var part in (line ?? "").Substring(Math.Min(7, (line ?? "").Length)).Split(';', StringSplitOptions.RemoveEmptyEntries)) {
                var f = part.Split(','); if (f.Length > 1 && int.TryParse(f[0], out int id)) list.Add((id, f[1]));
            }
        } catch { }
        if (list.Count == 0) for (int i = 1; i <= 4; i++) list.Add((12 + i, "HDMI " + i));
        return list.Select(i => (i.Item1, conf.TryGetValue("input_" + i.Item1, out var n) && n.Length > 0 ? n : i.Item2)).ToList();
    }
    public static int SourceId(string json)
    {
        var m = Regex.Match(json ?? "", "SOURCE_TYPE_HDMI(\\d)");
        return m.Success ? 12 + int.Parse(m.Groups[1].Value) : -1;
    }
    public static string AppName(string id, string input) => id switch {
        Launcher => "SilentGlass Home",
        "org.tizen.tv-viewer" => input,
        "com.samsung.tv.csfs" => "Samsung Home",
        ArtApp or "com.samsung.tv.home.art" or "com.samsung.tv.ambientfte" => "Ambient",
        "" => "None",
        _ => id,
    };
    static string ThreadRole()
    {
        if (!ThreadWanted) return "off";
        var r = Run("/opt/usr/apps/com.samsung.tizen.smartthings-hub/bin/ot-ctl", "state", 4000);
        var role = r.Split('\n')[0].Trim();
        return Regex.IsMatch(role, "^(router|leader|child|detached|disabled)$") ? role : "unavailable";
    }
    static string Run(string file, string args, int ms)
    {
        try {
            using var p = Process.Start(new ProcessStartInfo(file, args) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false });
            var o = p.StandardOutput.ReadToEndAsync();
            if (!p.WaitForExit(ms)) { try { p.Kill(true); } catch { } return ""; }
            return o.Result;
        } catch { return ""; }
    }

    // ---- publishing ----
    static void Pub(string sub, string value, bool force = false)
    {
        lock (gate) {
            if (!force && last.TryGetValue(sub, out var v) && v == value) return;
            if (mqtt?.Publish(Base + "/" + sub, value, true) == true) last[sub] = value;
        }
    }
    static string inputName = "";
    static void PublishFast(bool force)
    {
        int pm = VInt("memory/pm/state");
        Pub("power", pm == 4 ? "OFF" : pm < 0 ? "unknown" : "ON", force);
        var ins = Inputs(); int src = SourceId(VStr("memory/eden/source/current_source"));
        inputName = ins.Where(i => i.id == src).Select(i => i.name).FirstOrDefault() ?? "Other";
        Pub("input", inputName, force);
        Pub("app", AppName(VStr("memory/appfw/current_fullscreen_appid"), inputName), force);
        if (ThreadInstalled) Pub("thread_switch", ThreadWanted ? "ON" : "OFF", force);
    }
    static void PublishSlow(bool force)
    {
        if (int.TryParse(Read("/sys/devices/platform/soc/thermal/temperature").Trim(), out int t)) Pub("temperature", t.ToString(), force);
        var mem = Regex.Match(Read("/proc/meminfo"), @"MemAvailable:\s+(\d+)");
        if (mem.Success) Pub("memory", (long.Parse(mem.Groups[1].Value) / 1024).ToString(), force);
        if (ThreadInstalled) Pub("thread", ThreadRole(), force);
    }
    static string Read(string f) { try { return File.ReadAllText(f); } catch { return ""; } }

    static string Device() => "{\"identifiers\":[\"" + Node + "\"],\"name\":\"" + J(conf.GetValueOrDefault("name", "Living Room TV")) +
        "\",\"manufacturer\":\"Samsung\",\"model\":\"QN65S90D (SilentGlass-Tizen)\",\"sw_version\":\"silentglass-ha " + Version + "\"}";
    static string J(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    static void Discovery()
    {
        string common(string obj, string name, string icon) => "\"unique_id\":\"" + Node + "_" + obj + "\",\"object_id\":\"living_room_tv_" + obj + "\",\"name\":\"" + name +
            "\",\"icon\":\"" + icon + "\",\"availability_topic\":\"" + Base + "/availability\",\"device\":" + Device();
        void cfg(string comp, string obj, string body) => mqtt.Publish("homeassistant/" + comp + "/" + Node + "/" + obj + "/config", "{" + body + "}", true);
        var options = string.Join(",", Inputs().Select(i => "\"" + J(i.name) + "\"").Append("\"Other\""));
        cfg("binary_sensor", "power", common("power", "Screen", "mdi:television") + ",\"device_class\":\"power\",\"state_topic\":\"" + Base + "/power\"");
        cfg("select", "input", common("input", "Input", "mdi:video-input-hdmi") + ",\"state_topic\":\"" + Base + "/input\",\"command_topic\":\"" + Base + "/input/set\",\"options\":[" + options + "]");
        cfg("sensor", "app", common("app", "App", "mdi:application") + ",\"state_topic\":\"" + Base + "/app\"");
        cfg("sensor", "temperature", common("temperature", "Chip temperature", "mdi:thermometer") + ",\"state_topic\":\"" + Base + "/temperature\",\"device_class\":\"temperature\",\"unit_of_measurement\":\"°C\",\"state_class\":\"measurement\",\"entity_category\":\"diagnostic\"");
        cfg("sensor", "memory", common("memory", "Free memory", "mdi:memory") + ",\"state_topic\":\"" + Base + "/memory\",\"device_class\":\"data_size\",\"unit_of_measurement\":\"MB\",\"state_class\":\"measurement\",\"entity_category\":\"diagnostic\"");
        cfg("button", "home", common("home", "Home", "mdi:home") + ",\"command_topic\":\"" + Base + "/home/press\"");
        cfg("button", "ambient", common("ambient", "Ambient", "mdi:image-frame") + ",\"command_topic\":\"" + Base + "/ambient/press\"");
        if (ThreadInstalled) {
            cfg("sensor", "thread", common("thread", "Thread role", "mdi:access-point-network") + ",\"state_topic\":\"" + Base + "/thread\"");
            cfg("switch", "thread_switch", common("thread_switch", "Thread border router", "mdi:access-point") + ",\"state_topic\":\"" + Base + "/thread_switch\",\"command_topic\":\"" + Base + "/thread_switch/set\"");
        }
    }

    // ---- commands (fixed allowlist; no shell) ----
    static void OnMessage(string topic, string payload)
    {
        if (topic == "homeassistant/status") { if (payload == "online") Resync(); return; }
        Log("command " + topic.Substring(Base.Length + 1) + " " + (payload.Length > 40 ? payload.Substring(0, 40) : payload));
        if (topic == Base + "/input/set") {
            var match = Inputs().Where(i => i.name == payload).ToList();
            if (match.Count == 1) LaunchApp(Launcher, "quiet_source", match[0].id.ToString());
        } else if (topic == Base + "/home/press") LaunchApp(Launcher, "quiet_page", "home");
        else if (topic == Base + "/ambient/press") LaunchApp(ArtApp);
        else if (topic == Base + "/thread_switch/set" && ThreadInstalled) {
            var marker = Data + "/thread-off";
            if (payload != "ON" && payload != "OFF") return;
            if (payload == "OFF") File.WriteAllText(marker, ""); else File.Delete(marker);
            // same as the Info switch: quiet-otbr-switch.path watches this file and applies the change at once
            File.WriteAllText(Data + "/thread-switch", DateTime.UtcNow.ToString("o") + (payload == "ON" ? " on\n" : " off\n"));
        }
        Thread.Sleep(300); PublishFast(false);
    }
    static void LaunchApp(string app, string key = null, string value = null) =>
        Log("launch " + app + " -> " + Run("/usr/bin/launch_app", key == null ? app : app + " " + key + " " + value, 8000).Trim());

    static void Resync() { Discovery(); PublishFast(true); PublishSlow(true); }

    static int Main()
    {
        conf = File.ReadAllLines("/opt/usr/home/owner/.config/silentglass-ha.conf").Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith("#") && l.Contains('=')).ToDictionary(l => l.Split('=', 2)[0].Trim(), l => l.Split('=', 2)[1].Trim());
        Log("start");
        int backoff = 5;
        while (true) {
            try {
                mqtt = new Mqtt(conf["host"], int.Parse(conf.GetValueOrDefault("port", "1883")), "silentglass-s90d", conf.GetValueOrDefault("user"), conf.GetValueOrDefault("password"),
                    Base + "/availability", "offline", OnMessage);
                Log("connected"); backoff = 5;
                mqtt.Subscribe(Base + "/+/set", Base + "/+/press", "homeassistant/status");
                lock (gate) last.Clear();
                Resync(); mqtt.Publish(Base + "/availability", "online", true);
                for (long n = 1; mqtt.Alive; n++) {
                    Thread.Sleep(2000);
                    PublishFast(false);
                    if (n % 15 == 0) PublishSlow(false);
                    if (n % 150 == 0) { PublishFast(true); PublishSlow(true); }
                }
                Log("disconnected");
            } catch (Exception e) { Log("error " + e.GetType().Name + ": " + e.Message); }
            try { mqtt?.Close(); } catch { }
            Thread.Sleep(backoff * 1000); backoff = Math.Min(backoff * 2, 60);
        }
    }
}

// Minimal MQTT 3.1.1 client: CONNECT (with will), PUBLISH QoS 0, SUBSCRIBE QoS 0, PINGREQ, incoming PUBLISH.
sealed class Mqtt
{
    readonly TcpClient tcp; readonly NetworkStream s; readonly object w = new(); readonly Action<string, string> onMessage;
    volatile bool alive = true; ushort pid = 1;
    public bool Alive => alive;
    public Mqtt(string host, int port, string clientId, string user, string pass, string willTopic, string willMsg, Action<string, string> onMsg)
    {
        onMessage = onMsg;
        tcp = new TcpClient(); tcp.Connect(host, port); tcp.NoDelay = true; s = tcp.GetStream();
        var p = new List<byte>();
        Str(p, "MQTT"); p.Add(4);
        byte flags = 0x02 | 0x04 | 0x20; if (user != null) flags |= 0x80; if (pass != null) flags |= 0x40;
        p.Add(flags); p.Add(0); p.Add(60);
        Str(p, clientId); Str(p, willTopic); Str(p, willMsg); if (user != null) Str(p, user); if (pass != null) Str(p, pass);
        Send(0x10, p);
        s.ReadTimeout = 10000;
        var h = ReadPacket(out var body);
        if (h != 0x20 || body.Length < 2 || body[1] != 0) throw new Exception("connect refused (" + (body.Length > 1 ? body[1] : -1) + ")");
        s.ReadTimeout = Timeout.Infinite;
        new Thread(Reader) { IsBackground = true }.Start();
        new Thread(() => { while (alive) { Thread.Sleep(30000); try { Send(0xC0, new List<byte>()); } catch { alive = false; } } }) { IsBackground = true }.Start();
    }
    static void Str(List<byte> p, string v) { var b = Encoding.UTF8.GetBytes(v); p.Add((byte)(b.Length >> 8)); p.Add((byte)b.Length); p.AddRange(b); }
    void Send(byte header, List<byte> payload)
    {
        var buf = new List<byte> { header }; int len = payload.Count;
        do { byte d = (byte)(len % 128); len /= 128; if (len > 0) d |= 0x80; buf.Add(d); } while (len > 0);
        buf.AddRange(payload);
        lock (w) s.Write(buf.ToArray(), 0, buf.Count);
    }
    public bool Publish(string topic, string msg, bool retain)
    {
        try { var p = new List<byte>(); Str(p, topic); p.AddRange(Encoding.UTF8.GetBytes(msg)); Send((byte)(0x30 | (retain ? 1 : 0)), p); return true; }
        catch { alive = false; return false; }
    }
    public void Subscribe(params string[] topics)
    {
        var p = new List<byte> { (byte)(pid >> 8), (byte)pid }; pid++;
        foreach (var t in topics) { Str(p, t); p.Add(0); }
        Send(0x82, p);
    }
    byte ReadPacket(out byte[] body)
    {
        int h = s.ReadByte(); if (h < 0) throw new IOException("closed");
        int len = 0, mul = 1, d;
        do { d = s.ReadByte(); if (d < 0) throw new IOException("closed"); len += (d & 127) * mul; mul *= 128; } while ((d & 128) != 0);
        body = new byte[len]; int off = 0;
        while (off < len) { int n = s.Read(body, off, len - off); if (n <= 0) throw new IOException("closed"); off += n; }
        return (byte)h;
    }
    void Reader()
    {
        try {
            while (alive) {
                var h = ReadPacket(out var b);
                if ((h & 0xF0) != 0x30) continue;
                int tl = (b[0] << 8) | b[1]; var topic = Encoding.UTF8.GetString(b, 2, tl);
                int start = 2 + tl + (((h >> 1) & 3) > 0 ? 2 : 0);
                var msg = Encoding.UTF8.GetString(b, start, b.Length - start);
                ThreadPool.QueueUserWorkItem(_ => { try { onMessage(topic, msg); } catch { } });
            }
        } catch { }
        alive = false;
    }
    public void Close() { alive = false; try { tcp.Close(); } catch { } }
}
