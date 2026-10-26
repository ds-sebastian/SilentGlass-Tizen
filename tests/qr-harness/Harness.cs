using System;
using System.IO;

// usage: QrHarness OUTDIR TEXT...  -> OUTDIR/qr-N.pgm (8 px per module) and qr-N.svg
static class QrHarness
{
    static int Main(string[] args)
    {
        for (int n = 1; n < args.Length; n++) {
            var m = Qr.Encode(args[n]); int size = m.GetLength(0), s = 8, full = (size + 8) * s;
            var px = new byte[full * full];
            for (int y = 0; y < full; y++) for (int x = 0; x < full; x++) {
                int mx = x / s - 4, my = y / s - 4;
                px[y * full + x] = mx >= 0 && my >= 0 && mx < size && my < size && m[my, mx] ? (byte)0 : (byte)255;
            }
            using (var f = File.Create(Path.Combine(args[0], "qr-" + n + ".pgm"))) {
                var h = System.Text.Encoding.ASCII.GetBytes("P5\n" + full + " " + full + "\n255\n"); f.Write(h, 0, h.Length); f.Write(px, 0, px.Length);
            }
            File.WriteAllText(Path.Combine(args[0], "qr-" + n + ".svg"), Qr.Svg(m));
            Console.WriteLine(n + " size=" + size);
        }
        return 0;
    }
}
