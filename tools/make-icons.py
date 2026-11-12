"""Draws the app icons (plain Python, no packages): four coloured territories,
a pink trail looping out of the pink land and a round head with eyes.

  python tools/make-icons.py
writes Assets/WebGLTemplates/OurFourthGame/icons/*.png
"""
import math
import os
import struct
import zlib

OUT = os.path.join(os.path.dirname(__file__), "..", "Assets", "WebGLTemplates", "OurFourthGame", "icons")
os.makedirs(OUT, exist_ok=True)

GREEN = (77, 209, 115)
PINK = (255, 107, 168)
BLUE = (77, 158, 255)
ORANGE = (255, 168, 56)
NIGHT = (22, 26, 51)
NAVY = (26, 28, 56)


def png(path, size, px):
    raw = bytearray()
    for y in range(size):
        raw.append(0)
        raw.extend(px[y * size * 4:(y + 1) * size * 4])

    def chunk(tag, data):
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)

    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n")
        f.write(chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)))
        f.write(chunk(b"IDAT", zlib.compress(bytes(raw), 9)))
        f.write(chunk(b"IEND", b""))


def rbox(x, y, cx, cy, hw, hh, r):
    dx = abs(x - cx) - hw + r
    dy = abs(y - cy) - hh + r
    return math.hypot(max(dx, 0), max(dy, 0)) + min(max(dx, dy), 0) - r


def seg(x, y, ax, ay, bx, by):
    vx, vy = bx - ax, by - ay
    t = max(0.0, min(1.0, ((x - ax) * vx + (y - ay) * vy) / (vx * vx + vy * vy)))
    return math.hypot(x - ax - vx * t, y - ay - vy * t)


def cover(d, soft):
    return max(0.0, min(1.0, 0.5 - d / soft))


def over(c, col, a):
    return tuple(c[i] + (col[i] - c[i]) * a for i in range(3))


def scene(x, y, soft):
    col = NIGHT
    # four territories (2x2), the bottom-left one slightly bigger
    for cx, cy, colr in ((0.30, 0.70, GREEN), (0.70, 0.70, BLUE), (0.70, 0.30, ORANGE), (0.30, 0.30, PINK)):
        d = rbox(x, y, cx, cy, 0.17, 0.17, 0.07)
        col = over(col, tuple(v * 0.66 for v in colr), cover(d, soft))           # darker rim
        col = over(col, colr, cover(d + 0.025, soft))
    # pink trail leaving the pink land and curving around to the right
    pts = [(0.40, 0.30), (0.52, 0.30), (0.52, 0.52), (0.80, 0.52)]
    d = min(seg(x, y, *pts[i], *pts[i + 1]) for i in range(len(pts) - 1)) - 0.045
    col = over(col, NAVY, cover(d - 0.012, soft))
    col = over(col, (255, 190, 215), cover(d, soft))
    # head with a white ring, two eyes looking right
    hx, hy = 0.80, 0.52
    dh = math.hypot(x - hx, y - hy)
    col = over(col, (255, 255, 255), cover(dh - 0.115, soft))
    col = over(col, PINK, cover(dh - 0.095, soft))
    for ey in (0.48, 0.56):
        de = math.hypot((x - 0.835) / 0.9, (y - ey) * 1.0)
        col = over(col, (255, 255, 255), cover(de - 0.032, soft))
        dp = math.hypot(x - 0.85, y - ey)
        col = over(col, NAVY, cover(dp - 0.016, soft))
    return col


def render(size, scale, rounded, ss=3):
    px = bytearray(size * size * 4)
    soft = 1.4 / size
    sub = [(i + 0.5) / ss for i in range(ss)]
    for py in range(size):
        for pxl in range(size):
            r = g = b = a = 0.0
            for sy in sub:
                for sx in sub:
                    x = (pxl + sx) / size
                    y = (py + sy) / size
                    k = 1.0
                    if rounded:
                        k = cover(rbox(x, y, 0.5, 0.5, 0.5, 0.5, 0.2), soft)
                        if k <= 0:
                            continue
                    c = scene(0.5 + (x - 0.5) / scale, 0.5 + (y - 0.5) / scale, soft / scale)
                    r += c[0] * k
                    g += c[1] * k
                    b += c[2] * k
                    a += k
            i = (py * size + pxl) * 4
            if a > 0:
                px[i] = int(r / a)
                px[i + 1] = int(g / a)
                px[i + 2] = int(b / a)
                px[i + 3] = int(255 * a / (ss * ss))
    return px


for name, size, scale, rounded in (
    ("icon-512.png", 512, 1.0, True),
    ("icon-192.png", 192, 1.0, True),
    ("maskable-512.png", 512, 0.76, False),
    ("apple-touch-icon.png", 180, 0.9, False),
):
    png(os.path.join(OUT, name), size, render(size, scale, rounded, 2 if size > 200 else 3))
    print("wrote", name)
