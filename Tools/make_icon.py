"""make_icon - writes the application icon of HS2Next: a 1024 x 1024 RGBA PNG drawn from simple shapes.

The mark: a rounded square with a vertical gradient from deep indigo to teal, a bold rounded letter N in off-white, and a
small amber dot at the upper right. Every pixel is computed from signed distances to the shapes (rounded square, three
capsules, a circle) with a one-pixel anti-aliased edge, so the image is the same on every run and machine; the PNG is
written with zlib (standard library only, no image library). The script prints the size and the SHA-256 of the pixels and
of the file; it never displays the image. Unity reads the PNG as the player's icon (PlayerBuild.ApplyProjectSettings).

Usage (Python 3, standard library only; run with -I)
  python -I Tools/make_icon.py                       # writes Assets/HS2Next/Branding/HS2Next_icon.png
  python -I Tools/make_icon.py --out <file> --size 256
Exit 0 when the file was written.
"""
import argparse
import hashlib
import math
import os
import struct
import sys
import zlib

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT_OUT = os.path.join(ROOT, "Assets", "HS2Next", "Branding", "HS2Next_icon.png")

# colours (sRGB 0-255)
TOP = (36, 52, 92)          # deep indigo
BOTTOM = (18, 96, 112)      # teal
MARK = (240, 244, 248)      # off-white
ACCENT = (255, 184, 64)     # amber

# geometry in units of the image side (y grows downwards)
CORNER = 0.20               # corner radius of the rounded square
STROKE = 0.068              # half the stroke width of the N
N_LEFT, N_RIGHT, N_TOP, N_BOTTOM = 0.30, 0.70, 0.28, 0.72
DOT = (0.82, 0.18, 0.058)   # centre x, centre y, radius


def sd_round_square(x, y, half, r):
    """Signed distance to a square of half side 'half' centred at 0, corners rounded with radius r."""
    qx, qy = abs(x) - half + r, abs(y) - half + r
    ox, oy = max(qx, 0.0), max(qy, 0.0)
    return math.hypot(ox, oy) + min(max(qx, qy), 0.0) - r


def sd_segment(px, py, ax, ay, bx, by):
    """Distance to the segment a-b."""
    dx, dy = bx - ax, by - ay
    t = ((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy)
    t = 0.0 if t < 0.0 else 1.0 if t > 1.0 else t
    return math.hypot(px - ax - t * dx, py - ay - t * dy)


def coverage(d, px):
    """Area coverage of a pixel whose centre lies at signed distance d (units) from an edge; one pixel wide ramp."""
    c = 0.5 - d / px
    return 0.0 if c <= 0.0 else 1.0 if c >= 1.0 else c


def over(dst, src, a):
    """src over dst (premultiplied by coverage a) for straight-alpha RGBA dst."""
    dr, dg, db, da = dst
    if a <= 0.0:
        return dst
    oa = a + da * (1.0 - a)
    if oa <= 0.0:
        return (0.0, 0.0, 0.0, 0.0)
    f = da * (1.0 - a)
    return ((src[0] * a + dr * f) / oa, (src[1] * a + dg * f) / oa, (src[2] * a + db * f) / oa, oa)


def render(size):
    px = 1.0 / size
    segs = [(N_LEFT, N_BOTTOM, N_LEFT, N_TOP), (N_LEFT, N_TOP, N_RIGHT, N_BOTTOM), (N_RIGHT, N_BOTTOM, N_RIGHT, N_TOP)]
    rows = []
    for j in range(size):
        y = (j + 0.5) * px
        g = y  # gradient position
        bg = tuple(TOP[k] + (BOTTOM[k] - TOP[k]) * g for k in range(3))
        row = bytearray()
        for i in range(size):
            x = (i + 0.5) * px
            a = coverage(sd_round_square(x - 0.5, y - 0.5, 0.5, CORNER), px)
            c = (bg[0], bg[1], bg[2], a)
            if N_LEFT - STROKE - px <= x <= N_RIGHT + STROKE + px and N_TOP - STROKE - px <= y <= N_BOTTOM + STROKE + px:
                d = min(sd_segment(x, y, *s) for s in segs) - STROKE
                c = over(c, MARK, coverage(d, px))
            dd = math.hypot(x - DOT[0], y - DOT[1]) - DOT[2]
            if dd < 2 * px:
                c = over(c, ACCENT, coverage(dd, px))
            row += bytes((int(round(c[0])), int(round(c[1])), int(round(c[2])), int(round(c[3] * 255.0))))
        rows.append(bytes(row))
    return rows


def png(rows, size):
    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)
    raw = b"".join(b"\x00" + r for r in rows)          # filter type 0 (none) on every row
    ihdr = struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)   # 8-bit RGBA, no interlace
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", ihdr) + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""), raw


def main():
    ap = argparse.ArgumentParser(description="Writes the HS2Next application icon (a PNG; never displayed).")
    ap.add_argument("--out", default=DEFAULT_OUT)
    ap.add_argument("--size", type=int, default=1024)
    a = ap.parse_args()
    if a.size < 16 or a.size > 4096:
        print("make_icon: --size must be 16-4096")
        return 2
    rows = render(a.size)
    data, raw = png(rows, a.size)
    out = os.path.abspath(a.out)
    os.makedirs(os.path.dirname(out), exist_ok=True)
    tmp = out + ".tmp"
    with open(tmp, "wb") as f:
        f.write(data)
    os.replace(tmp, out)
    print("make_icon: %s %d x %d RGBA, %d bytes; pixels sha256 %s; file sha256 %s"
          % (os.path.relpath(out, ROOT).replace("\\", "/") if out.startswith(ROOT) else os.path.basename(out),
             a.size, a.size, len(data), hashlib.sha256(raw).hexdigest(), hashlib.sha256(data).hexdigest()))
    return 0


if __name__ == "__main__":
    sys.exit(main())
