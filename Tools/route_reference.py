"""Independent reference values for the route evaluator.

Written from the formulas of the spec (not from game code and not from the C# evaluator): the 32 ease functions
(iTween's forms of Penner's equations, see RouteEases.cs for the stated departures), HS2's padded uniform
Catmull-Rom, the twice-padded `Hs2` length (N = 20 x once-padded length chords), the segment queue (Line / Curve /
links / loop), the continuous timeline with the two-point ping-pong and HS2's end carry after an expo ease.

Output: Tests/Routes/route_reference.json (checked by SceneTests_Routes in the mono `scene` suite).
Usage: python -I Tools/route_reference.py [--out <file>]
"""
import argparse
import json
import math
import os
import sys

EASES = [
    "easeInQuad", "easeOutQuad", "easeInOutQuad", "easeInCubic", "easeOutCubic", "easeInOutCubic", "easeInQuart",
    "easeOutQuart", "easeInOutQuart", "easeInQuint", "easeOutQuint", "easeInOutQuint", "easeInSine", "easeOutSine",
    "easeInOutSine", "easeInExpo", "easeOutExpo", "easeInOutExpo", "easeInCirc", "easeOutCirc", "easeInOutCirc",
    "linear", "spring", "easeInBounce", "easeOutBounce", "easeInOutBounce", "easeInBack", "easeOutBack",
    "easeInOutBack", "easeInElastic", "easeOutElastic", "easeInOutElastic",
]
PROBES = [0.0, 0.1, 0.25, 0.5, 0.6, 0.75, 0.9, 1.0]


def bounce_out(t):
    if t < 1 / 2.75:
        return 7.5625 * t * t
    if t < 2 / 2.75:
        return 7.5625 * (t - 1.5 / 2.75) ** 2 + 0.75
    if t < 2.5 / 2.75:
        return 7.5625 * (t - 2.25 / 2.75) ** 2 + 0.9375
    return 7.5625 * (t - 2.625 / 2.75) ** 2 + 0.984375


def in_out(f_in, f_out, t):
    """Penner in-out from the in and out halves: first half f_in(2t)/2, second half 1/2 + f_out(2t - 1)/2."""
    return f_in(2 * t) / 2 if t < 0.5 else 0.5 + f_out(2 * t - 1) / 2


def ease(name, t):
    s = 1.70158
    pw = {"Quad": 2, "Cubic": 3, "Quart": 4, "Quint": 5}
    for k, n in pw.items():
        if name == "easeIn" + k:
            return t ** n
        if name == "easeOut" + k:
            return 1 - (1 - t) ** n
        if name == "easeInOut" + k:
            return in_out(lambda x: x ** n, lambda x: 1 - (1 - x) ** n, t)
    if name == "linear":
        return t
    if name == "spring":
        v = min(1.0, max(0.0, t))
        return (math.sin(v * math.pi * (0.2 + 2.5 * v ** 3)) * (1 - v) ** 2.2 + v) * (1 + 1.2 * (1 - v))
    if name == "easeInSine":
        return 1 - math.cos(t * math.pi / 2)
    if name == "easeOutSine":
        return math.sin(t * math.pi / 2)
    if name == "easeInOutSine":
        return (1 - math.cos(math.pi * t)) / 2
    # Expo without the end guards (iTween): in(0) = 2^-10, out(1) = 1 - 2^-10.
    if name == "easeInExpo":
        return 2 ** (10 * (t - 1))
    if name == "easeOutExpo":
        return 1 - 2 ** (-10 * t)
    if name == "easeInOutExpo":
        return in_out(lambda x: 2 ** (10 * (x - 1)), lambda x: 1 - 2 ** (-10 * x), t)
    if name == "easeInCirc":
        return 1 - math.sqrt(1 - t * t)
    if name == "easeOutCirc":
        return math.sqrt(1 - (t - 1) ** 2)
    if name == "easeInOutCirc":
        return in_out(lambda x: 1 - math.sqrt(1 - x * x), lambda x: math.sqrt(1 - (x - 1) ** 2), t)
    if name == "easeOutBounce":
        return bounce_out(t)
    if name == "easeInBounce":
        return 1 - bounce_out(1 - t)
    if name == "easeInOutBounce":
        return in_out(lambda x: 1 - bounce_out(1 - x), bounce_out, t)
    if name == "easeInBack":
        return t * t * ((s + 1) * t - s)
    if name == "easeOutBack":
        return 1 + (t - 1) ** 2 * ((s + 1) * (t - 1) + s)
    if name == "easeInOutBack":
        s2 = s * 1.525
        return in_out(lambda x: x * x * ((s2 + 1) * x - s2), lambda x: 1 + (x - 1) ** 2 * ((s2 + 1) * (x - 1) + s2), t)
    p = 0.3
    el_in = lambda x: 0.0 if x == 0 else 1.0 if x == 1 else -(2 ** (10 * (x - 1))) * math.sin((x - 1 - p / 4) * 2 * math.pi / p)
    el_out = lambda x: 0.0 if x == 0 else 1.0 if x == 1 else 2 ** (-10 * x) * math.sin((x - p / 4) * 2 * math.pi / p) + 1
    if name == "easeInElastic":
        return el_in(t)
    if name == "easeOutElastic":
        return el_out(t)
    if name == "easeInOutElastic":
        if t == 0:
            return 0.0
        if t == 1:
            return 1.0
        return in_out(el_in, el_out, t)
    raise KeyError(name)


def clamp01(v):
    return 0.0 if v < 0 else 1.0 if v > 1 else v


# ---- vectors (tuples) ----
def add(a, b): return (a[0] + b[0], a[1] + b[1], a[2] + b[2])
def sub(a, b): return (a[0] - b[0], a[1] - b[1], a[2] - b[2])
def mul(a, s): return (a[0] * s, a[1] * s, a[2] * s)
def dist(a, b): return math.sqrt(sum((x - y) ** 2 for x, y in zip(a, b)))
def near(a, b): return sum((x - y) ** 2 for x, y in zip(a, b)) < 9.99999944e-11


def rot_y(deg, v):
    """Rotation about +Y by deg (left-handed Unity: x' = x cos + z sin, z' = -x sin + z cos)."""
    r = math.radians(deg)
    return (v[0] * math.cos(r) + v[2] * math.sin(r), v[1], -v[0] * math.sin(r) + v[2] * math.cos(r))


def pad(path):
    n = len(path)
    if near(path[0], path[-1]):
        return [path[n - 2]] + list(path) + [path[1]]
    return [sub(mul(path[0], 2), path[1])] + list(path) + [sub(mul(path[-1], 2), path[-2])]


def cr(pts, s):
    m = len(pts) - 3
    k = min(int(math.floor(s * m)), m - 1)
    u = s * m - k
    a, b, c, d = pts[k], pts[k + 1], pts[k + 2], pts[k + 3]
    return tuple(0.5 * ((-a[i] + 3 * b[i] - 3 * c[i] + d[i]) * u ** 3 + (2 * a[i] - 5 * b[i] + 4 * c[i] - d[i]) * u ** 2
                        + (c[i] - a[i]) * u + 2 * b[i]) for i in range(3))


def hs2_length(padded):
    twice = pad(padded)
    n = 20 * len(padded)
    prev, total = cr(twice, 0.0), 0.0
    for i in range(1, n + 1):
        q = cr(twice, i / n)
        total += dist(prev, q)
        prev = q
    return total


# ---- routes ----
def route_points(route):
    pts = [tuple(p["pos"]) for p in route["points"]]
    aids = []
    for i, p in enumerate(route["points"]):
        nxt = pts[i + 1] if i + 1 < len(pts) else pts[0]
        a = p.get("aid")
        if a and a.get("init"):
            local = tuple(a["pos"])
            if i == 0:
                local = rot_y(route["points"][0].get("rotY", 0.0), local)
            aids.append(add(pts[i], local))
        else:
            aids.append(mul(add(pts[i], nxt), 0.5))
    return pts, aids


def queue(route):
    P = route["points"]
    n, loop = len(P), route["loop"]
    curve = lambda i: P[i].get("connection", "line") == "curve"
    link = lambda i: curve(i) and P[i].get("link", False)
    q, i = [], 0
    while i < n:
        if not loop and i == n - 1:
            break
        start = i
        i += 1
        if not curve(start):
            to = 0 if start == n - 1 else start + 1
            q.append({"from": start, "to": to, "entries": [("p", start), ("p", to)]})
        else:
            ent = [("p", start), ("a", start)]
            while i < n and (loop or i != n - 1) and link(i):
                ent += [("p", i), ("a", i)]
                i += 1
            to = 0 if i >= n else i
            q.append({"from": start, "to": to, "entries": ent + [("p", to)]})
    return q


def realize(route, q, carry_from):
    pts, aids = route_points(route)
    out, prev_end = [], carry_from
    for s in q:
        ctl = [pts[k] if t == "p" else aids[k] for t, k in s["entries"]]
        lead = prev_end is not None and not near(prev_end, ctl[0])
        if lead:
            ctl = [prev_end] + ctl
        P = route["points"][s["from"]]
        padded = pad(ctl)
        length = hs2_length(padded)
        speed = P.get("speed", 2.0)
        dur = math.inf if not speed > 0 else (length / (10 * speed) if length > 0 else 0.0)
        e = P.get("ease", "linear")
        seg = {"from": s["from"], "to": s["to"], "padded": padded, "length": length, "duration": dur, "ease": e, "lead": lead}
        out.append(seg)
        prev_end = cr(padded, clamp01(ease(e, 1.0)))
    return out


def build(route):
    n = len(route["points"])
    mode = "once" if not route["loop"] else ("pingpong" if n == 2 else "loop")
    q = queue(route)
    first = realize(route, q, None)
    repeat = []
    if mode == "loop":
        cur = first
        for _ in range(8):
            nxt = realize(route, q, cr(cur[-1]["padded"], clamp01(ease(cur[-1]["ease"], 1.0))))
            same = all(a["lead"] == b["lead"] and (not a["lead"] or near(a["padded"][1], b["padded"][1])) for a, b in zip(nxt, cur))
            cur = nxt
            if same:
                break
        repeat = cur if any(s["lead"] for s in cur) or any(s["lead"] for s in first) else first
    elif mode == "pingpong":
        repeat = [first[-1]]
    return mode, first, repeat


def locate(segs, t):
    start = 0.0
    for k, s in enumerate(segs):
        if t < start + s["duration"]:
            local = t - start
            if math.isinf(s["duration"]):
                return k, 0.0
            return k, min(1.0, local / s["duration"]) if s["duration"] > 0 else 1.0
        start += s["duration"]
    return len(segs) - 1, 1.0


def sample(mode, first, repeat, tau, look=0.05):
    d1 = sum(s["duration"] for s in first)
    reversed_leg, finished = False, False
    if tau < d1:
        k, p = locate(first, tau)
        seg = first[k]
    elif mode == "once":
        k, p, seg, finished = len(first) - 1, 1.0, first[-1], True
    elif mode == "loop":
        d2 = sum(s["duration"] for s in repeat)
        local = (tau - d1) % d2
        k, p = locate(repeat, local)
        seg = repeat[k]
    else:
        seg = repeat[0]
        k = len(first) - 1
        r = tau - d1
        legs = math.floor(r / seg["duration"])
        u = (r - legs * seg["duration"]) / seg["duration"]
        reversed_leg = legs % 2 == 0
        p = 1 - u if reversed_leg else u
    s = clamp01(ease(seg["ease"], p))
    pos = cr(seg["padded"], s)
    lt = cr(seg["padded"], clamp01(ease(seg["ease"], min(1.0, p + look))))
    return {"tau": tau, "segment": k, "p": p, "s": s, "pos": list(pos), "look": list(lt), "reversed": reversed_leg, "finished": finished}


def pt(pos, **kw):
    d = {"pos": list(pos)}
    d.update(kw)
    return d


CASES = {
    "line2": {"loop": False, "points": [pt((0, 0, 0)), pt((10, 0, 0))], "taus": [0, 0.3, 0.75, 1.2, 1.5, 2.0]},
    "line3_loop": {"loop": True, "points": [pt((0, 0, 0), speed=2), pt((10, 0, 0), speed=3, ease="easeInQuad"),
                                            pt((10, 0, 10), speed=4, ease="easeOutBounce")],
                   "taus": [0, 0.5, 1.4, 1.6, 2.2, 3.0, 3.9, 5.5, 11.0]},
    "curve_rotated_start": {"loop": False, "points": [
        pt((0, 0, 0), rotY=90.0, connection="curve", aid={"pos": (5, 2, 5), "init": True}, speed=1.5, ease="easeInOutSine"),
        pt((20, 0, 0)), pt((20, 0, 20), speed=2)], "taus": [0, 0.4, 1.1, 2.5, 3.7, 6.0, 9.0]},
    "curve_default_aid": {"loop": False, "points": [pt((0, 0, 0), connection="curve"), pt((10, 4, 0))],
                          "taus": [0, 0.5, 1.0, 2.0]},
    "chain_closed_loop": {"loop": True, "points": [
        pt((0, 0, 0), connection="curve", aid={"pos": (5, 0, -3), "init": True}),
        pt((10, 0, 0), connection="curve", link=True, aid={"pos": (2, 0, 6), "init": True}, speed=9),
        pt((5, 3, 10), connection="curve", link=True, aid={"pos": (-6, 0, -2), "init": True}, speed=9)],
        "taus": [0, 1.0, 2.5, 4.0, 7.0, 13.0]},
    "chain_open": {"loop": False, "points": [
        pt((0, 0, 0), connection="curve"), pt((10, 0, 0), connection="curve", link=True), pt((10, 0, 10), connection="curve", link=True),
        pt((0, 5, 10), connection="curve", link=True)], "taus": [0, 1.0, 3.0, 5.0, 8.0, 30.0]},
    "pingpong": {"loop": True, "points": [pt((0, 0, 0), speed=2), pt((10, 0, 0), speed=5, ease="easeOutQuad")],
                 "taus": [0, 0.75, 1.4, 1.8, 2.0, 2.3, 2.7, 3.1, 3.3, 10.0]},
    "pingpong_chain": {"loop": True, "points": [pt((0, 0, 0), connection="curve", aid={"pos": (5, 0, 5), "init": True}),
                                                pt((10, 0, 0), connection="curve", link=True, aid={"pos": (-5, 0, -5), "init": True})],
                       "taus": [0, 1.0, 2.0, 4.0, 6.0, 9.0]},
    "expo_carry": {"loop": False, "points": [pt((0, 0, 0), ease="easeOutExpo"), pt((10, 0, 0), ease="linear"), pt((10, 0, 10))],
                   "taus": [0, 1.0, 1.5, 1.6, 2.0, 2.6, 3.0, 4.0]},
    "expo_carry_loop": {"loop": True, "points": [pt((0, 0, 0), ease="easeInOutExpo"), pt((10, 0, 0)), pt((10, 0, 10), ease="easeOutExpo")],
                        "taus": [0, 1.0, 4.0, 5.0, 6.5, 9.0, 20.0]},
}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Tests", "Routes", "route_reference.json"))
    a = ap.parse_args()
    eases = {n: {"probes": PROBES, "raw": [ease(n, t) for t in PROBES], "clamped": [clamp01(ease(n, t)) for t in PROBES]} for n in EASES}
    cases = {}
    for name, c in CASES.items():
        route = {"loop": c["loop"], "points": c["points"]}
        mode, first, repeat = build(route)
        cases[name] = {
            "route": route,
            "mode": mode,
            "segments": [{"from": s["from"], "to": s["to"], "length": s["length"], "duration": s["duration"], "lead": s["lead"]} for s in first],
            "repeat": [{"from": s["from"], "to": s["to"], "length": s["length"], "duration": s["duration"], "lead": s["lead"]} for s in repeat],
            "samples": [sample(mode, first, repeat, float(t)) for t in c["taus"]],
        }
    doc = {"about": "Tools/route_reference.py: independent reference values of the route evaluator",
           "eases": eases, "cases": cases}
    text = json.dumps(doc, indent=1, allow_nan=True, sort_keys=False)
    os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)
    with open(a.out, "w", encoding="utf-8", newline="\n") as f:
        f.write(text + "\n")
    sys.stdout.write("wrote %s: %d eases, %d cases\n" % (os.path.normpath(a.out), len(eases), len(cases)))


if __name__ == "__main__":
    main()
