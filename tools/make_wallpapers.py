#!/usr/bin/env python3
"""Build the photo wallpaper assets (not in git): python -I tools/make_wallpapers.py DAY.jpg NIGHT.jpg
Writes launcher/assets/wallpapers/wall-{day,night}.jpg (1920x1080, darker top for the clock, dimmed for OLED).
Needs pillow + numpy."""
import sys
from pathlib import Path
import numpy as np
from PIL import Image

Image.MAX_IMAGE_PIXELS = None
OUT = Path(__file__).resolve().parents[1] / "launcher/assets/wallpapers"
OUT.mkdir(parents=True, exist_ok=True)
for name, path, dim in (("day", sys.argv[1], .86), ("night", sys.argv[2], 1.0)):
    im = Image.open(path); im.draft("RGB", (3008, 3008)); im = im.convert("RGB")
    w = im.width; h = round(w * 9 / 16); top = round(min(w * .18, im.height - h))   # square 6K source: island centred
    im = im.crop((0, top, w, top + h)).resize((1920, 1080), Image.LANCZOS)
    a = np.asarray(im).astype(np.float32) / 255
    y = np.linspace(0, 1, 1080)[:, None, None]
    v = 1 - .62 * np.clip(1 - y / .24, 0, 1) ** 1.4 - .18 * np.clip((y - .62) / .38, 0, 1)
    disp = Image.fromarray((np.clip(a * v * dim, 0, 1) * 255 + .5).astype(np.uint8))
    disp.save(OUT / f"wall-{name}.jpg", quality=90, optimize=True, subsampling="4:2:0")
    print("wrote", name)
