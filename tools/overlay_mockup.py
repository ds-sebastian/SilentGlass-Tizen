#!/usr/bin/env python3
"""1:1 HTML mockups of the SilentGlass overlay (Home over the picture), rendered with headless Chromium on the Pi
before anything goes to the TV. Geometry and colours mirror launcher/src/Overlay.cs, Menus.cs, Panels.cs and Ui.cs; keep the
two in step.

    python3 tools/overlay_mockup.py OUTDIR [BACKGROUND.jpg]
writes OUTDIR/{bar,inputs,apps,controls}.html and renders .png next to them (chromium --headless).
"""
import html
import json
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
A = ROOT / "launcher" / "assets"

# ---- shared numbers (Home.Overlay.cs) ----
BAR_H, BAR_R, BAR_BOTTOM, ITEM_H, ITEM_PAD, GLYPH, GAP, LABEL = 104, 52, 52, 80, 30, 40, 14, 30
POP_GAP, ROW_H, POP_W, POP_R = 18, 88, 560, 36
CC_X, CC_W, CC_R, SLIDER_H, VALUE_H = 1236, 624, 44, 88, 64
APP_W, APP_H, APP_GAP, APP_COLS = 256, 144, 24, 4
GLASS = "rgba(30,30,34,.66)"
GLASS_TOP = "rgba(255,255,255,.07)"
EDGE = "rgba(255,255,255,.16)"


def uri(p):
    return Path(p).resolve().as_uri()


CSS = f"""
@font-face{{font-family:QI;src:url({uri(A/'fonts/QuietInter-Medium.ttf')});font-weight:500}}
@font-face{{font-family:QI;src:url({uri(A/'fonts/QuietInter-Semibold.ttf')});font-weight:600}}
@font-face{{font-family:QID;src:url({uri(A/'fonts/QuietInter-Display.ttf')})}}
body{{margin:0;width:1920px;height:1080px;overflow:hidden;background:#000 center/cover;font-family:QI;color:#fff;position:relative}}
.abs{{position:absolute}}
.glass{{position:absolute;background:linear-gradient(180deg,{GLASS_TOP},rgba(255,255,255,0) 40%),{GLASS};box-shadow:inset 0 0 0 1.5px {EDGE}}}
.dim{{position:absolute;left:0;right:0;bottom:0;height:340px;background:linear-gradient(180deg,rgba(0,0,0,0),rgba(0,0,0,.5))}}
.dimr{{position:absolute;top:0;bottom:0;right:0;width:900px;background:linear-gradient(90deg,rgba(0,0,0,0),rgba(0,0,0,.45))}}
.bar{{display:flex;align-items:center;gap:0;padding:0 12px;height:{BAR_H}px;border-radius:{BAR_R}px;bottom:{BAR_BOTTOM}px;left:50%;transform:translateX(-50%)}}
.item{{height:{ITEM_H}px;border-radius:{ITEM_H//2}px;display:flex;align-items:center;gap:{GAP}px;padding:0 {ITEM_PAD}px;font-weight:600;font-size:{LABEL}px;white-space:nowrap}}
.item img,.row img{{width:{GLYPH}px;height:{GLYPH}px}}
.f{{background:rgba(255,255,255,.95);color:#1c1c1e}} .f img{{filter:brightness(0) saturate(100%) invert(9%)}}
.sep{{width:2px;height:40px;background:rgba(255,255,255,.22);margin:0 10px}}
.two{{display:flex;flex-direction:column;line-height:1.12}} .two small{{font-size:21px;font-weight:500;opacity:.62;display:flex;align-items:center;gap:10px}}
.dot{{width:12px;height:12px;border-radius:6px;background:#30d158}}
.pop{{border-radius:{POP_R}px;padding:12px}}
.row{{height:{ROW_H}px;border-radius:26px;display:flex;align-items:center;gap:18px;padding:0 22px;font-weight:600;font-size:30px}}
.row .sub{{font-weight:500;font-size:22px;opacity:.6;display:block;margin-top:2px}}
.row .r{{margin-left:auto;display:flex;align-items:center;gap:16px}}
.cc{{border-radius:{CC_R}px;padding:16px;box-sizing:border-box}}
.cr{{border-radius:28px;display:flex;align-items:center;gap:20px;padding:0 24px;font-weight:600;font-size:28px;position:relative}}
.cr img{{width:38px;height:38px}}
.val{{margin-left:auto;font-weight:500;font-size:26px;opacity:.65;white-space:nowrap}}
.f .val{{opacity:.7}}
.track{{position:absolute;left:82px;right:24px;bottom:18px;height:8px;border-radius:4px;background:rgba(255,255,255,.22)}}
.track i{{display:block;height:8px;border-radius:4px;background:#fff}}
.f .track{{background:rgba(0,0,0,.14)}} .f .track i{{background:#1c1c1e}}
.big{{font-family:QID;font-size:30px;opacity:1}}
.hr{{height:1px;background:rgba(255,255,255,.14);margin:12px 8px}}
.sw{{width:64px;height:36px;border-radius:18px;background:#30d158;position:relative}} .sw b{{position:absolute;right:3px;top:3px;width:30px;height:30px;border-radius:15px;background:#fff}}
.sw.off{{background:rgba(120,120,128,.5)}} .sw.off b{{left:3px}}
.tile{{position:absolute;width:{APP_W}px;height:{APP_H}px;border-radius:20px;background:#333 center/cover}}
.tname{{position:absolute;width:{APP_W}px;text-align:center;font-weight:600;font-size:22px;opacity:.8}}
.tile.f{{transform:scale(1.07);box-shadow:0 0 0 4px #fff,0 18px 40px rgba(0,0,0,.45)}}
"""

BAR = [("tv", "Watch", "SHIELD", True), ("gamecontroller", "Play", "Nintendo Switch", False), None,
       ("inputs", "Inputs", None, False), ("controls", "Controls", None, False), ("apps", "Apps", None, False), ("info", "Info", None, False)]


def sym(n):
    return uri(A / f"sym-{n}.svg")


def bar(focus):
    out = []
    for i, it in enumerate(BAR):
        if it is None:
            out.append('<div class="sep"></div>')
            continue
        g, label, sub, live = it
        text = (f'<span class="two">{label}<small>{sub}{"<span class=dot></span>" if live else ""}</small></span>'
                if sub else label)
        out.append(f'<div class="item{" f" if i == focus else ""}"><img src="{sym(g)}">{text}</div>')
    return f'<div class="dim"></div><div class="glass bar" id="bar">{"".join(out)}</div>'


def page(name, body, bg):
    return (f'<html><head><meta charset="utf-8"><style>{CSS}</style></head>'
            f'<body style="background-image:url({uri(bg)})">{body}</body></html>')


def inputs_pop():
    rows = [("tv", "SHIELD", "HDMI 3", True, True), ("gamecontroller", "Nintendo Switch", "HDMI 1", False, False),
            ("hdmi", "HDMI 2", None, False, False), ("hdmi", "HDMI 4", None, False, False), ("antenna", "Live TV", None, False, False)]
    h = 24 + len(rows) * ROW_H
    rs = []
    for i, (g, n, p, cur, live) in enumerate(rows):
        sub = f'<span class="sub">{p}</span>' if p else ""
        right = ('<span class="dot"></span>' if live else "") + (f'<img src="{sym("check")}" style="width:34px;height:34px">' if cur else "")
        rs.append(f'<div class="row{" f" if i == 1 else ""}"><img src="{sym(g)}"><span>{n}{sub}</span><span class="r">{right}</span></div>')
    top = 1080 - BAR_BOTTOM - BAR_H - POP_GAP - h
    return f'<div class="glass pop" style="left:{INPUTS_X - POP_W//2}px;top:{top}px;width:{POP_W - 24}px;height:{h - 24}px">{"".join(rs)}</div>'


def apps_pop(apps):
    n = min(len(apps), 12)
    rows = (n + APP_COLS - 1) // APP_COLS
    w = APP_COLS * APP_W + (APP_COLS - 1) * APP_GAP + 2 * 32
    h = rows * (APP_H + 50) + (rows - 1) * 18 + 2 * 28
    left, top = 960 - w // 2, 1080 - BAR_BOTTOM - BAR_H - POP_GAP - h
    tiles = []
    for i, a in enumerate(apps[:n]):
        x = left + 32 + (i % APP_COLS) * (APP_W + APP_GAP)
        y = top + 28 + (i // APP_COLS) * (APP_H + 50 + 18)
        art = A / a.get("Asset", "")
        style = f"background-color:{a.get("Background", "#333")};" + (f"background-image:url({uri(art)})" if art.is_file() else "")
        tiles.append(f'<div class="tile{" f" if i == 0 else ""}" style="left:{x}px;top:{y}px;{style}"></div>'
                     f'<div class="tname" style="left:{x}px;top:{y + APP_H + 12}px">{html.escape(a["Name"])}</div>')
    return f'<div class="glass" style="left:{left}px;top:{top}px;width:{w}px;height:{h}px;border-radius:44px"></div>{"".join(tiles)}'


PANEL_PAD, RULE_H = 16, 25


def panel_rows(rows, focus):
    out, i = [], 0
    for r in rows:
        if r == "hr":
            out.append('<div class="hr"></div>')
            continue
        kind, g, name, val = r[0], r[1], r[2], r[3]
        f = " f" if i == focus else ""
        if kind == "s":
            out.append(f'<div class="cr{f}" style="height:{SLIDER_H}px;align-items:flex-start;padding-top:12px;box-sizing:border-box"><img src="{sym(g)}">{name}'
                       f'<span class="val big">{val}</span><div class="track"><i style="width:{int(r[4]*100)}%"></i></div></div>')
        elif kind == "t":
            out.append(f'<div class="cr{f}" style="height:{VALUE_H}px"><img src="{sym(g)}">{name}<span class="sw{"" if val else " off"}" style="margin-left:auto"><b></b></span></div>')
        elif kind == "d":  # status row: coloured dot, name, value
            out.append(f'<div class="cr" style="height:{VALUE_H - 8}px"><span style="width:14px;height:14px;border-radius:7px;background:{r[4]};margin:0 12px"></span>{name}<span class="val">{val}</span></div>')
        else:
            chevron = f'<img src="{sym("chevron")}" style="width:26px;height:26px;opacity:.5">' if kind == "a" else ""
            out.append(f'<div class="cr{f}" style="height:{VALUE_H}px"><img src="{sym(g)}">{name}<span class="val">{val}</span>{chevron}</div>')
        if kind != "d":
            i += 1
    return "".join(out)


def panel(rows, focus, title=None, opaque=False):
    body = panel_rows(rows, focus)
    head = (f'<div style="padding:14px 24px 6px;display:flex;align-items:baseline;gap:14px"><span style="font-family:QID;font-size:34px">{title[0]}</span>'
            f'<span style="font-size:22px;opacity:.5">{title[1]}</span></div>') if title else ""
    bg = "background:rgba(22,22,26,.95)" if opaque else ""
    return (f'<div class="dimr"></div><div class="glass cc" style="left:{CC_X}px;top:50%;transform:translateY(-50%);width:{CC_W}px;{bg}">'
            f'{head}{body}</div>')


def controls(focus=0):
    rows = [("s", "sun", "Brightness", "50", .99), ("s", "contrast", "Contrast", "45", .9), ("s", "speaker", "Volume", "18", .18), "hr",
            ("v", "picture", "Picture Mode", "Movie (Calibrated)"), ("v", "waveform", "Sound Mode", "Adaptive Sound"),
            ("v", "soundbar", "Sound Output", "Soundbar"), ("v", "gamecontroller", "Game Mode", "Automatic"),
            ("v", "moon", "Sleep Timer", "Off"), ("v", "pixelshift", "Pixel Shift", "On"), "hr",
            ("a", "artframe", "Ambient", ""), ("a", "bluetooth", "Bluetooth", ""), ("a", "network", "Network", ""), ("a", "gear", "TV Settings", "")]
    return panel(rows, focus)


def info(focus=0):
    green, grey = "#30d158", "#8e8e93"
    rows = [("d", "", "Privacy", "Protected", green), ("d", "", "Network", "Wired · 1 Gb/s", green),
            ("d", "", "Thread", "Router · home reachable", green), ("d", "", "Home Assistant", "Connected", green),
            ("d", "", "This TV", "Up 3 h · 41 °C", green), "hr",
            ("v", "tv", "Watch", "Auto · SHIELD"), ("v", "gamecontroller", "Play", "Auto · Nintendo Switch"),
            ("t", "apps", "Apps in the bar", False), ("t", "moon", "Dim when idle", True), ("v", "picture", "Wallpaper", "Follows sunset"),
            ("t", "bell", "Notifications", True), "hr",
            ("a", "gear", "Advanced settings", "")]
    return panel(rows, focus, title=("SilentGlass", "v42e · nothing sent anywhere"), opaque=True)


def setup_sheet(qr):
    w, h = 900, 520
    x, y = (1920 - w) // 2, (1080 - h) // 2 - 40
    qr_img = f'<img src="{uri(qr)}" style="width:300px;height:300px">' if qr and Path(qr).exists() else '<div style="width:300px;height:300px;background:#ccc"></div>'
    return (f'<div style="position:absolute;inset:0;background:rgba(0,0,0,.55)"></div>'
            f'<div class="glass" style="left:{x}px;top:{y}px;width:{w}px;height:{h}px;border-radius:44px;background:rgba(22,22,26,.96)">'
            f'<div style="position:absolute;left:48px;top:48px;width:340px;height:340px;border-radius:28px;background:#fff;display:flex;align-items:center;justify-content:center">{qr_img}</div>'
            f'<div style="position:absolute;left:430px;top:52px;right:48px">'
            f'<div style="font-family:QID;font-size:44px;line-height:1.1">Advanced settings</div>'
            f'<div style="font-size:27px;opacity:.7;margin-top:18px;line-height:1.35">Scan with your phone, or open<br><b style="opacity:1;font-weight:600">192.168.1.50:7680</b> and enter</div>'
            f'<div style="font-family:QID;font-size:72px;letter-spacing:10px;margin-top:22px">482 913</div></div>'
            f'<div style="position:absolute;left:48px;right:48px;bottom:40px;display:flex;align-items:center;gap:14px;font-size:26px;opacity:.85">'
            f'<span style="width:14px;height:14px;border-radius:7px;background:#30d158"></span>Saved. Connected to your broker.'
            f'<span style="margin-left:auto;opacity:.6">Back to close</span></div></div>')


def backdrop():
    return ('<div style="position:absolute;right:72px;top:44px;font-family:QID;font-size:40px;opacity:.9">7:42 PM</div>')


# ---- notification banner (Home.Notice.cs) ----
NOTICE_W = {"small": 420, "medium": 520, "large": 640, "max": 800}
NOTICE_MARGIN, NOTICE_R, NOTICE_PAD, NOTICE_TEXT_PAD = 56, 36, 14, 26


def notice(img, size, pos, title, msg, aspect=0.75):
    w = NOTICE_W[size]
    iw = w - 2 * NOTICE_PAD
    ih = int(iw * min(max(aspect, 9 / 16), 3 / 4)) if img else 0
    # text block height is measured by the browser; position with CSS from the chosen corner
    side = "right" if "right" in pos else "left"
    vert = "bottom" if "bottom" in pos else "top"
    image = (f'<div style="margin:{NOTICE_PAD}px {NOTICE_PAD}px 0;height:{ih}px;border-radius:{NOTICE_R - NOTICE_PAD}px;'
             f'background:url({uri(img)}) center/cover"></div>') if img else ""
    return (f'<div class="abs" style="{side}:{NOTICE_MARGIN}px;{vert}:{NOTICE_MARGIN}px;width:{w}px;border-radius:{NOTICE_R}px;'
            f'background:rgba(26,26,30,.9);box-shadow:inset 0 0 0 1.5px {EDGE},0 18px 50px rgba(0,0,0,.35)">{image}'
            f'<div style="padding:{20 if img else 24}px {NOTICE_TEXT_PAD}px 24px"><div style="font-weight:600;font-size:30px;line-height:1.2">{html.escape(title)}</div>'
            f'<div style="font-weight:500;font-size:25px;line-height:1.3;opacity:.72;margin-top:4px">{html.escape(msg)}</div></div></div>')


INPUTS_X = 0  # set after measuring the bar


def main():
    out = Path(sys.argv[1]).resolve()
    out.mkdir(parents=True, exist_ok=True)
    bg = Path(sys.argv[2]) if len(sys.argv) > 2 else A / "wallpapers" / "wall-day.jpg"
    cat = ROOT / "launcher/assets/apps.local.json"
    apps = json.loads((cat if cat.exists() else A / "apps.json").read_text())
    apps = apps if isinstance(apps, list) else apps.get("apps", [])
    apps.append({"Name": "Samsung Home", "Asset": "tile-samsung-home.svg", "Background": "#1C2638"})
    global INPUTS_X
    INPUTS_X = 960 - 150  # approximate centre of "Inputs" in the mock bar
    sample = ROOT / ".local/notice-mock/sample.jpg"
    pages = {"notice": notice(sample if sample.exists() else None, "large", "bottom-right", "Doorbell",
                              "A person was detected on the Doorbell camera in the Porch.")
             + notice(None, "medium", "top-right", "Washer", "The laundry is done."),
             "bar": bar(0),
             "info": bar(6) + info(5),
             "backdrop": backdrop() + bar(0),
             "setup": setup_sheet(ROOT / ".local/overlay-mock/qr.svg"), "inputs": bar(3) + inputs_pop(), "apps": bar(5) + apps_pop(apps), "controls": controls(0)}
    for name, body in pages.items():
        (out / f"{name}.html").write_text(page(name, body, bg))
        subprocess.run(["chromium", "--headless=new", "--disable-gpu", "--no-sandbox", "--hide-scrollbars",
                        "--allow-file-access-from-files", "--window-size=1920,1080", "--virtual-time-budget=3000",
                        f"--screenshot={out / (name + '.png')}", (out / f"{name}.html").as_uri()],
                       check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    print("rendered", ", ".join(pages), "->", out)


if __name__ == "__main__":
    main()
