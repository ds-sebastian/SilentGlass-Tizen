#!/usr/bin/env python3
"""SilentGlass overlay symbols (SF Symbols style): white, 48x48 viewBox, one stroke weight (3.6), round caps and
joins, filled where Apple's symbol is filled. Knock-outs use fill-rule evenodd (the TV's SVG renderer supports it);
no masks, clip paths, gradients or text. Writes launcher/assets/sym-*.svg.

    python3 tools/make_symbols.py
"""
import math
from pathlib import Path

OUT = Path(__file__).resolve().parents[1] / "launcher" / "assets"
W = 3.6  # stroke weight


def svg(body):
    return ('<svg xmlns="http://www.w3.org/2000/svg" width="96" height="96" viewBox="0 0 48 48">'
            f'<g fill="none" stroke="#fff" stroke-width="{W}" stroke-linecap="round" stroke-linejoin="round">{body}</g></svg>')


def fill(d, rule="evenodd"):
    return f'<path d="{d}" fill="#fff" stroke="none" fill-rule="{rule}"/>'


def rrect(x, y, w, h, r):
    return (f"M{x+r} {y}h{w-2*r}a{r} {r} 0 0 1 {r} {r}v{h-2*r}a{r} {r} 0 0 1 -{r} {r}h-{w-2*r}"
            f"a{r} {r} 0 0 1 -{r} -{r}v-{h-2*r}a{r} {r} 0 0 1 {r} -{r}z")


def circle(cx, cy, r):
    return f"M{cx-r} {cy}a{r} {r} 0 1 0 {2*r} 0a{r} {r} 0 1 0 -{2*r} 0z"


def gear(cx, cy, teeth=8, ro=21, ri=16.5, hole=7):
    pts = []
    for i in range(teeth):
        a0 = 2 * math.pi * i / teeth
        for da, r in ((-0.20, ri), (-0.12, ro), (0.12, ro), (0.20, ri)):
            a = a0 + da * 2 * math.pi / teeth * 2.2
            pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    d = "M" + " L".join(f"{x:.2f} {y:.2f}" for x, y in pts) + "z"
    return d + circle(cx, cy, hole)


SYMBOLS = {
    # devices
    "tv": f'<path d="{rrect(4, 7, 40, 28, 5)}"/><path d="M17 42h14"/>' + fill("M21 15.5v11l9-5.5z", "nonzero"),
    "gamecontroller": fill(
        "M15 12h18c6 0 9.6 3.4 11 9.6l2 9.2c1.2 5.4-4.6 8.8-8.6 5.4L33 32H15l-4.4 4.2c-4 3.4-9.8 0-8.6-5.4l2-9.2C5.4 15.4 9 12 15 12z"
        "M12.4 18.6h3.2v3.6h3.6v3.2h-3.6V29h-3.2v-3.6H8.8v-3.2h3.6z" + circle(33.5, 20.5, 2.2) + circle(37.5, 25.5, 2.2)),
    "antenna": f'<path d="M24 22v20"/><path d="M14 12a14 14 0 0 0 0 20M34 12a14 14 0 0 1 0 20M19 17a7 7 0 0 0 0 10M29 17a7 7 0 0 1 0 10"/>'
               + fill(circle(24, 22, 3.4), "nonzero"),
    "hdmi": f'<path d="M8 14h32v9l-5 7H13l-5-7z"/><path d="M17 30v5h14v-5"/><path d="M15 20h18"/>',
    # bar
    "inputs": f'<path d="M20 9h17a6 6 0 0 1 6 6v18a6 6 0 0 1-6 6H20"/><path d="M4 24h22M18 15l9 9-9 9"/>',
    "controls": ('<path d="M6 12h22M38 12h4M6 24h6M22 24h20M6 36h18M34 36h8"/>'
                 '<circle cx="33" cy="12" r="5"/><circle cx="17" cy="24" r="5"/><circle cx="29" cy="36" r="5"/>'),
    "apps": fill(rrect(5, 5, 17, 17, 5) + rrect(26, 5, 17, 17, 5) + rrect(5, 26, 17, 17, 5) + rrect(26, 26, 17, 17, 5), "nonzero"),
    "info": fill(circle(24, 24, 21) + circle(24, 14.5, 2.9) + rrect(21.6, 20.5, 4.8, 15, 2.4)),
    # controls
    "sun": fill(circle(24, 24, 8.5), "nonzero") + "".join(
        f'<path d="M{24+14*math.cos(a):.2f} {24+14*math.sin(a):.2f}L{24+19.5*math.cos(a):.2f} {24+19.5*math.sin(a):.2f}"/>'
        for a in (i * math.pi / 4 for i in range(8))),
    "contrast": f'<circle cx="24" cy="24" r="18"/>' + fill("M24 6a18 18 0 0 0 0 36z", "nonzero"),
    "speaker": fill("M7 18h7l10-8v28l-10-8H7a2 2 0 0 1-2-2V20a2 2 0 0 1 2-2z", "nonzero")
               + '<path d="M30 18a8 8 0 0 1 0 12M35 12a16 16 0 0 1 0 24"/>',
    "mute": fill("M7 18h7l10-8v28l-10-8H7a2 2 0 0 1-2-2V20a2 2 0 0 1 2-2z", "nonzero") + '<path d="M31 19l10 10M41 19l-10 10"/>',
    "picture": f'<path d="{rrect(4, 8, 40, 32, 6)}"/>' + fill("M9 35l10-12 7 8 5-5 8 9z", "nonzero") + fill(circle(33, 17, 3.6), "nonzero"),
    "waveform": '<path d="M6 20v8M13 14v20M20 8v32M27 16v16M34 11v26M41 19v10"/>',
    "soundbar": f'<path d="{rrect(3, 17, 42, 14, 7)}"/>' + fill(circle(12, 24, 2.4) + circle(24, 24, 2.4) + circle(36, 24, 2.4), "nonzero"),
    "moon": fill("M29 5.5A19 19 0 1 0 42.5 34 15 15 0 0 1 29 5.5z", "nonzero"),
    "artframe": f'<path d="{rrect(5, 5, 38, 38, 4)}"/><path d="{rrect(12, 12, 24, 24, 2)}"/>',
    "gear": fill(gear(24, 24)),
    "slidersmore": '<path d="M8 24h.01M24 24h.01M40 24h.01" stroke-width="6"/>',
    "pixelshift": fill(rrect(6, 6, 15, 15, 3.5) + rrect(27, 27, 15, 15, 3.5), "nonzero") + '<path d="M30 9h9v9M18 39H9v-9"/>',
    "bluetooth": '<path d="M14 15l20 18-10 9V6l10 9-20 18"/>',
    "network": '<path d="M5 18a27 27 0 0 1 38 0M11 25a18 18 0 0 1 26 0M17 32a9 9 0 0 1 14 0"/>' + fill(circle(24, 39, 3.2), "nonzero"),
    "bell": fill("M24 5a3 3 0 0 1 3 3v1.4c6 1.4 10 6.6 10 13V30l4 5H7l4-5v-7.6c0-6.4 4-11.6 10-13V8a3 3 0 0 1 3-3z", "nonzero")
            + fill("M19 38h10a5 5 0 0 1-10 0z", "nonzero"),
    "homeassistant": '<path d="M5 24L24 6l19 18M10 20v22h28V20"/>' + fill(circle(24, 27, 3.4) + circle(17, 35, 2.6) + circle(31, 35, 2.6), "nonzero")
                     + '<path d="M24 30v3M21.5 33.5l-2.6 1.2M26.5 33.5l2.6 1.2" stroke-width="2.6"/>',
    "thread": fill(circle(24, 9, 4) + circle(9, 37, 4) + circle(39, 37, 4) + circle(24, 28, 3.4), "nonzero")
              + '<path d="M24 13v12M12 35l9.5-5.5M36 35l-9.5-5.5M13 37h22M21 11l-10 23M27 11l10 23" stroke-width="2.8"/>',
    "check": '<path d="M10 25l9 9 19-20" stroke-width="4.4"/>',
    "chevron": '<path d="M19 12l12 12-12 12" stroke-width="4.4"/>',
}


def main():
    for name, body in SYMBOLS.items():
        (OUT / f"sym-{name}.svg").write_text(svg(body))
    print(f"wrote {len(SYMBOLS)} symbols to {OUT}")


if __name__ == "__main__":
    main()
