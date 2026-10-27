using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;

// Minimal MQTT 3.1.1 client: CONNECT (with will), PUBLISH QoS 0, SUBSCRIBE QoS 0, PINGREQ, incoming PUBLISH. A broker
// that answers nothing for 90 s (a ping goes out every 30 s) is gone: a lost network leaves no reset to read.
sealed class Mqtt
{
    readonly TcpClient tcp; readonly NetworkStream s; readonly object w = new(); readonly Action<string, string> onMessage;
    volatile bool alive = true; ushort pid = 1;
    public bool Alive => alive;
    public Action Closed;  // called once when the connection is lost or closed
    void Die() { if (!alive) return; alive = false; try { Closed?.Invoke(); } catch { } }
    public Mqtt(string host, int port, string clientId, string user, string pass, string willTopic, string willMsg, Action<string, string> onMsg)
    {
        onMessage = onMsg;
        tcp = new TcpClient();
        if (!tcp.ConnectAsync(host, port).Wait(6000)) { tcp.Close(); throw new TimeoutException("no answer from " + host + ":" + port); }
        tcp.NoDelay = true; s = tcp.GetStream();
        var p = new List<byte>();
        Str(p, "MQTT"); p.Add(4);
        byte flags = 0x02 | 0x04 | 0x20; if (user != null) flags |= 0x80; if (pass != null) flags |= 0x40;
        p.Add(flags); p.Add(0); p.Add(60);
        Str(p, clientId); Str(p, willTopic); Str(p, willMsg); if (user != null) Str(p, user); if (pass != null) Str(p, pass);
        Send(0x10, p);
        s.ReadTimeout = 10000;
        var h = ReadPacket(out var body);
        if (h != 0x20 || body.Length < 2 || body[1] != 0) {
            int rc = body.Length > 1 ? body[1] : -1; tcp.Close();
            throw new Exception(rc == 4 ? "wrong username or password" : rc == 5 ? "not authorized" : rc == 2 ? "client id rejected" : "refused (" + rc + ")");
        }
        s.ReadTimeout = 90000;
        new Thread(Reader) { IsBackground = true }.Start();
        new Thread(() => { while (alive) { Thread.Sleep(30000); try { Send(0xC0, new List<byte>()); } catch { Die(); } } }) { IsBackground = true }.Start();
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
        catch { Die(); return false; }
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
        Die();
    }
    public void Close() { Die(); try { tcp.Close(); } catch { } }
    // a clean DISCONNECT: the broker drops the last will instead of publishing it
    public void Disconnect() { try { Send(0xE0, new List<byte>()); } catch { } Close(); }
}
