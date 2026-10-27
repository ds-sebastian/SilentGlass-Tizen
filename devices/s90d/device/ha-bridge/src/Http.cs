using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

// Minimal HTTP/1.1 server side for the bridge's two small endpoints (notifications on 7676, the setup page on 7680):
// one request per connection, Content-Length bodies, no keep-alive.
sealed class HttpRequest
{
    public string Method = "", Path = "/", Boundary = ""; public byte[] Body = Array.Empty<byte>();
    public readonly Dictionary<string, string> Headers = new();
    public static HttpRequest Read(Stream s, int maxBody)
    {
        var buf = new byte[65536]; int got = 0, end = -1;
        while (end < 0) {
            if (got == buf.Length) return null;  // headers too large
            int n = s.Read(buf, got, buf.Length - got); if (n <= 0) return null; got += n;
            for (int i = Math.Max(0, got - n - 3); i + 3 < got; i++) if (buf[i] == 13 && buf[i + 1] == 10 && buf[i + 2] == 13 && buf[i + 3] == 10) { end = i; break; }
        }
        var lines = Encoding.ASCII.GetString(buf, 0, end).Split("\r\n");
        var first = lines[0].Split(' ');
        var r = new HttpRequest { Method = first[0] };
        string target = first.Length > 1 ? first[1] : "/"; int q = target.IndexOf('?');
        r.Path = q < 0 ? target : target.Substring(0, q);  // the query (the QR code's ?s=) only makes the address unique
        int length = 0;
        foreach (var l in lines.Skip(1)) {
            int c = l.IndexOf(':'); if (c < 0) continue;
            string k = l.Substring(0, c).Trim().ToLowerInvariant(), v = l.Substring(c + 1).Trim();
            r.Headers[k] = v;
            if (k == "content-length" && !int.TryParse(v, out length)) return null;
            if (k == "content-type") { var m = Regex.Match(v, "boundary=\"?([^\";]+)\"?"); if (m.Success) r.Boundary = m.Groups[1].Value; }
        }
        if (length < 0 || length > maxBody) return null;
        r.Body = new byte[length]; int have = Math.Min(length, got - end - 4);
        Array.Copy(buf, end + 4, r.Body, 0, have);
        while (have < length) { int n = s.Read(r.Body, have, length - have); if (n <= 0) return null; have += n; }
        return r;
    }
    static string Reason(int code) => code switch { 200 => "OK", 403 => "Forbidden", 404 => "Not Found", 405 => "Method Not Allowed", 410 => "Gone", _ => "Error" };
    public static void Respond(Stream s, int code, string type = "text/plain", byte[] body = null)
    {
        body ??= Encoding.ASCII.GetBytes(code == 200 ? "OK" : Reason(code));
        var head = Encoding.ASCII.GetBytes("HTTP/1.1 " + code + " " + Reason(code) + "\r\nContent-Type: " + type + "\r\nContent-Length: " + body.Length +
            "\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nConnection: close\r\n\r\n");
        s.Write(head, 0, head.Length); s.Write(body, 0, body.Length); s.Flush();
    }
}
