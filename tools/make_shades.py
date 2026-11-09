#!/usr/bin/env python3
"""Writes launcher/assets/shade-top.png and shade-bottom.png: the shades behind the clock and the bar.

Black with an alpha that falls smoothly to 0, one value per row plus a 4x4 ordered dither. The TV's own gradient
visual held each colour stop flat for ~27 rows and then changed slope, which showed as stripes over the picture
(1.4.1 on the S90D). Stdlib only; the output is byte-for-byte reproducible.
"""
import struct, zlib
from pathlib import Path

W = 1920
BAYER = [[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]]


def top(t):     # 0 at the screen edge -> 1 at the band's end: 84 % black easing out to nothing (smoothstep)
    return .84 * (1 - t * t * (3 - 2 * t))


def bottom(t):  # 0 where the shade starts -> 1 at the screen edge: nothing, easing in to 86 % black
    return .86 * t ** 1.7


def side(x):    # the top band in a panel's window (744 wide, from x 1176): fades in from its left edge, no line there
    t = min(1, x / 360)
    return t * t * (3 - 2 * t)


def png(path, h, alpha, w=W, across=lambda x: 1):
    rows = bytearray()
    for y in range(h):
        a = alpha(y / (h - 1)) * 255
        rows.append(0)
        for x in range(w):
            v = int(a * across(x) + (BAYER[y % 4][x % 4] + .5) / 16)   # ordered dither between the two nearest levels
            rows += bytes((0, 0, 0, max(0, min(255, v))))
    chunk = lambda k, d: struct.pack(">I", len(d)) + k + d + struct.pack(">I", zlib.crc32(k + d) & 0xffffffff)
    data = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0)) + \
        chunk(b"IDAT", zlib.compress(bytes(rows), 9)) + chunk(b"IEND", b"")
    path.write_bytes(data)
    return len(data)


if __name__ == "__main__":
    A = Path(__file__).resolve().parent.parent / "launcher/assets"
    print("shade-top.png", png(A / "shade-top.png", 260, top), "bytes")
    print("shade-top-side.png", png(A / "shade-top-side.png", 260, top, 744, side), "bytes")
    print("shade-bottom.png", png(A / "shade-bottom.png", 660, bottom), "bytes")
