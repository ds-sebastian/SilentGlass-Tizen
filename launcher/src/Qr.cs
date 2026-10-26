using System;
using System.Collections.Generic;
using System.Text;

// QR code encoder (ISO/IEC 18004), byte mode, error correction M, versions 1-10 (up to 213 bytes): enough for the
// setup URL shown on the TV. No dependencies, no NUI: tests/CatalogTests.cs writes codes that the Pi-side test
// decodes with OpenCV. Svg() draws it as one path (DALi renders SVG natively).
public static class Qr
{
    // per version (1-10), level M: total codewords, EC codewords per block, blocks in group 1, data cw per block g1,
    // blocks in group 2, data cw per block g2
    static readonly int[,] Table = {
        { 26, 10, 1, 16, 0, 0 }, { 44, 16, 1, 28, 0, 0 }, { 70, 26, 1, 44, 0, 0 }, { 100, 18, 2, 32, 0, 0 },
        { 134, 24, 2, 43, 0, 0 }, { 172, 16, 4, 27, 0, 0 }, { 196, 18, 4, 31, 0, 0 }, { 242, 22, 2, 38, 2, 39 },
        { 292, 22, 3, 36, 2, 37 }, { 346, 26, 4, 43, 1, 44 } };
    static readonly int[][] Align = {
        new int[0], new[] { 6, 18 }, new[] { 6, 22 }, new[] { 6, 26 }, new[] { 6, 30 }, new[] { 6, 34 },
        new[] { 6, 22, 38 }, new[] { 6, 24, 42 }, new[] { 6, 26, 46 }, new[] { 6, 28, 50 } };

    public static bool[,] Encode(string text)
    {
        var data = Encoding.UTF8.GetBytes(text);
        int ver = 0;
        for (int v = 1; v <= 10; v++) if (DataCapacity(v) * 8 >= 4 + (v < 10 ? 8 : 16) + data.Length * 8) { ver = v; break; }
        if (ver == 0) throw new ArgumentException("too long for a version 1-10 QR code");
        // bit stream: mode 0100 (byte), count, data, terminator, pad to bytes, pad codewords
        var bits = new List<bool>();
        void Put(int value, int n) { for (int i = n - 1; i >= 0; i--) bits.Add(((value >> i) & 1) != 0); }
        Put(4, 4); Put(data.Length, ver < 10 ? 8 : 16);
        foreach (var b in data) Put(b, 8);
        int cap = DataCapacity(ver) * 8;
        Put(0, Math.Min(4, cap - bits.Count));
        while (bits.Count % 8 != 0) bits.Add(false);
        for (int p = 0; bits.Count < cap; p++) Put(p % 2 == 0 ? 0xEC : 0x11, 8);
        var cw = new byte[bits.Count / 8];
        for (int i = 0; i < cw.Length; i++) for (int j = 0; j < 8; j++) if (bits[i * 8 + j]) cw[i] |= (byte)(0x80 >> j);
        var all = Interleave(cw, ver);
        int size = 17 + 4 * ver;
        var m = new bool[size, size]; var fn = new bool[size, size];
        DrawFunctions(m, fn, ver);
        // data in the zig-zag order
        int bit = 0, total = all.Length * 8;
        for (int right = size - 1; right >= 1; right -= 2) {
            if (right == 6) right = 5;
            for (int vert = 0; vert < size; vert++)
                for (int j = 0; j < 2; j++) {
                    int x = right - j; bool up = ((right + 1) & 2) == 0; int y = up ? size - 1 - vert : vert;
                    if (fn[y, x]) continue;
                    if (bit < total) m[y, x] = ((all[bit >> 3] >> (7 - (bit & 7))) & 1) != 0;
                    bit++;
                }
        }
        // the mask with the lowest penalty
        bool[,] best = null; int bestScore = int.MaxValue;
        for (int mask = 0; mask < 8; mask++) {
            var t = (bool[,])m.Clone();
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) if (!fn[y, x] && MaskBit(mask, x, y)) t[y, x] = !t[y, x];
            DrawFormat(t, mask);
            int s = Penalty(t);
            if (s < bestScore) { bestScore = s; best = t; }
        }
        return best;
    }

    static int DataCapacity(int v) { int i = v - 1; return Table[i, 2] * Table[i, 3] + Table[i, 4] * Table[i, 5]; }

    static byte[] Interleave(byte[] data, int ver)
    {
        int i = ver - 1, ec = Table[i, 1];
        var blocks = new List<byte[]>(); var ecs = new List<byte[]>(); int at = 0;
        for (int g = 0; g < 2; g++)
            for (int b = 0; b < Table[i, 2 + 2 * g]; b++) {
                int n = Table[i, 3 + 2 * g]; var blk = new byte[n]; Array.Copy(data, at, blk, 0, n); at += n;
                blocks.Add(blk); ecs.Add(ReedSolomon(blk, ec));
            }
        var r = new List<byte>();
        int maxData = 0; foreach (var b in blocks) maxData = Math.Max(maxData, b.Length);
        for (int k = 0; k < maxData; k++) foreach (var b in blocks) if (k < b.Length) r.Add(b[k]);
        for (int k = 0; k < ec; k++) foreach (var e in ecs) r.Add(e[k]);
        return r.ToArray();
    }

    // GF(256) with the QR polynomial 0x11D
    static byte Mul(byte a, byte b) { int r = 0; for (int i = 7; i >= 0; i--) { r = (r << 1) ^ ((r >> 7) * 0x11D); r ^= ((b >> i) & 1) * a; } return (byte)r; }
    static byte[] ReedSolomon(byte[] data, int degree)
    {
        var gen = new byte[degree]; gen[degree - 1] = 1; byte root = 1;
        for (int i = 0; i < degree; i++) {
            for (int j = 0; j < degree; j++) { gen[j] = Mul(gen[j], root); if (j + 1 < degree) gen[j] ^= gen[j + 1]; }
            root = Mul(root, 2);
        }
        var res = new byte[degree];
        foreach (var b in data) {
            byte f = (byte)(b ^ res[0]);
            Array.Copy(res, 1, res, 0, degree - 1); res[degree - 1] = 0;
            for (int j = 0; j < degree; j++) res[j] ^= Mul(gen[j], f);
        }
        return res;
    }

    static void Set(bool[,] m, bool[,] fn, int x, int y, bool dark) { m[y, x] = dark; fn[y, x] = true; }
    static void DrawFunctions(bool[,] m, bool[,] fn, int ver)
    {
        int size = m.GetLength(0);
        for (int i = 0; i < size; i++) { Set(m, fn, 6, i, i % 2 == 0); Set(m, fn, i, 6, i % 2 == 0); }  // timing
        foreach (var (cx, cy) in new[] { (3, 3), (size - 4, 3), (3, size - 4) })                          // finders
            for (int dy = -4; dy <= 4; dy++) for (int dx = -4; dx <= 4; dx++) {
                int x = cx + dx, y = cy + dy; if (x < 0 || y < 0 || x >= size || y >= size) continue;
                int d = Math.Max(Math.Abs(dx), Math.Abs(dy)); Set(m, fn, x, y, d != 2 && d != 4);
            }
        var al = Align[ver - 1]; int na = al.Length;                                                   // alignment
        for (int a = 0; a < na; a++) for (int b = 0; b < na; b++) {
            if (a == 0 && b == 0 || a == 0 && b == na - 1 || a == na - 1 && b == 0) continue;
            for (int dy = -2; dy <= 2; dy++) for (int dx = -2; dx <= 2; dx++) Set(m, fn, al[a] + dx, al[b] + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
        }
        DrawFormat(m, 0, fn);  // reserve the format areas (real bits per mask later)
        if (ver >= 7) {                                                                               // version info
            int rem = ver; for (int i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
            int bitsV = ver << 12 | rem;
            for (int i = 0; i < 18; i++) { bool d = ((bitsV >> i) & 1) != 0; int a = size - 11 + i % 3, b = i / 3; Set(m, fn, a, b, d); Set(m, fn, b, a, d); }
        }
    }
    static void DrawFormat(bool[,] m, int mask, bool[,] fn = null)
    {
        int size = m.GetLength(0), data = 0 << 3 | mask;  // level M = 00
        int rem = data; for (int i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
        int bitsF = (data << 10 | rem) ^ 0x5412;
        void S(int x, int y, bool d) { m[y, x] = d; if (fn != null) fn[y, x] = true; }
        bool B(int i) => ((bitsF >> i) & 1) != 0;
        for (int i = 0; i <= 5; i++) S(8, i, B(i));
        S(8, 7, B(6)); S(8, 8, B(7)); S(7, 8, B(8));
        for (int i = 9; i < 15; i++) S(14 - i, 8, B(i));
        for (int i = 0; i < 8; i++) S(size - 1 - i, 8, B(i));
        for (int i = 8; i < 15; i++) S(8, size - 15 + i, B(i));
        S(8, size - 8, true);  // the dark module
    }
    static bool MaskBit(int mask, int x, int y) => mask switch {
        0 => (x + y) % 2 == 0, 1 => y % 2 == 0, 2 => x % 3 == 0, 3 => (x + y) % 3 == 0, 4 => (x / 3 + y / 2) % 2 == 0,
        5 => x * y % 2 + x * y % 3 == 0, 6 => (x * y % 2 + x * y % 3) % 2 == 0, _ => ((x + y) % 2 + x * y % 3) % 2 == 0 };
    static int Penalty(bool[,] m)
    {
        int size = m.GetLength(0), score = 0, dark = 0;
        for (int pass = 0; pass < 2; pass++)
            for (int a = 0; a < size; a++) {
                int run = 1;
                for (int b = 1; b <= size; b++) {
                    bool same = b < size && (pass == 0 ? m[a, b] == m[a, b - 1] : m[b, a] == m[b - 1, a]);
                    if (same) run++; else { if (run >= 5) score += run - 2; run = 1; }
                }
                for (int b = 0; b + 10 < size; b++) {  // finder-like 1011101 with 4 light on a side
                    bool P(int k) => pass == 0 ? m[a, b + k] : m[b + k, a];
                    bool core = P(4) && !P(5) && P(6) && P(7) && P(8) && !P(9) && P(10);
                    bool core2 = P(0) && !P(1) && P(2) && P(3) && P(4) && !P(5) && P(6);
                    if (core && !P(0) && !P(1) && !P(2) && !P(3)) score += 40;
                    if (core2 && !P(7) && !P(8) && !P(9) && !P(10)) score += 40;
                }
            }
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) {
            if (m[y, x]) dark++;
            if (x + 1 < size && y + 1 < size && m[y, x] == m[y, x + 1] && m[y, x] == m[y + 1, x] && m[y, x] == m[y + 1, x + 1]) score += 3;
        }
        score += Math.Abs(dark * 20 - size * size * 10) / (size * size) * 10;
        return score;
    }

    // one SVG path, dark on a white quiet zone of 4 modules
    public static string Svg(bool[,] m, string dark = "#111")
    {
        int size = m.GetLength(0), full = size + 8;
        var d = new StringBuilder();
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) if (m[y, x]) d.Append('M').Append(x + 4).Append(' ').Append(y + 4).Append("h1v1h-1z");
        return "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"" + full * 8 + "\" height=\"" + full * 8 + "\" viewBox=\"0 0 " + full + " " + full +
               "\" shape-rendering=\"crispEdges\"><rect width=\"" + full + "\" height=\"" + full + "\" fill=\"#fff\"/><path fill=\"" + dark + "\" d=\"" + d + "\"/></svg>";
    }
}
