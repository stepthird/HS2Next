"""Black-box measurement of the HS2 Maker shape sliders (section 4.8).

Drives a running CharaStudio through a private Studio Bridge build (HTTP 127.0.0.1:17321; the commands shape.info,
shape.set and bones.dump), sets body and face slider vectors on two reference characters and records the local TRS of
every skeleton transform. HS2Next code must not depend on this script or on the bridge; only the CSV files it writes
are inputs of the table fitting.

Usage: py -3.14 -I Tools/shape_measure.py [--sets female_body,female_face,male_body,male_face] [--out DIR] [--oor]
       py -3.14 -I Tools/shape_measure.py --report-only   (README.md and Tools/survey/shape/measure_summary.md)
       --install LABEL names the game install in both reports (default: the folder name of hs2Root).

--oor (v0.2, slider values outside 0..1): the sets are written as <set>_oor and sample
values outside the vanilla range, as the slider-extender plugin of the reference install lets them through: rest; sweep
of every slider at -1, -0.5, 0, 0.5, 1, 1.5, 2 (others 0.5); random 40 vectors uniform in [-1, 2) (seed 2468). Meta in
meta_oor.json; the reports of the 0..1 sets are not rewritten.

Game root: "hs2Root" in Config/settings.local.json. Output (default): Cache/shapemeasure/.
Per set: <set>_samples.csv (sample,kind,slider,value,n,v0..v{n-1}) and <set>_bones.csv
(sample,bone,path,px,py,pz,qx,qy,qz,qw,ex,ey,ez,sx,sy,sz), plus <set>_checks.json (sanity checks) and README.md.
"""
import argparse
import datetime
import json
import random
import time
import urllib.request
from pathlib import Path

import numpy as np

REPO = Path(__file__).resolve().parents[1]
URL = "http://127.0.0.1:17321/"
FEMALE_CARD = "BR-Chan HS2 Rev2.png"
SWEEP = [0.0, 0.25, 0.5, 0.75, 1.0]
N_RANDOM, SEED_RANDOM = 120, 1234
N_PAIR, SEED_PAIR = 40, 4321
NEUTRAL = 0.5
OOR_SWEEP = [-1.0, -0.5, 0.0, 0.5, 1.0, 1.5, 2.0]
N_RANDOM_OOR, SEED_RANDOM_OOR = 40, 2468


def call(cmd, timeout=120, **args):
    args["cmd"] = cmd
    req = urllib.request.Request(URL, data=json.dumps(args).encode("utf-8"), method="POST")
    with urllib.request.urlopen(req, timeout=timeout) as r:
        rep = json.loads(r.read().decode("utf-8"))
    if not rep.get("ok"):
        raise RuntimeError("%s: %s" % (cmd, rep.get("error")))
    return rep


def f32(v):
    """Shortest decimal that round-trips the float32 value."""
    return str(np.float32(v))


def settings():
    p = REPO / "Config" / "settings.local.json"
    return json.loads(p.read_text(encoding="utf-8"))


def install_label(arg):
    """The game install as the reports name it: --install, else the folder name of hs2Root, else "HS2"."""
    if arg:
        return arg
    try:
        return Path(settings()["hs2Root"]).name or "HS2"
    except (OSError, KeyError, ValueError):
        return "HS2"


def first_male_card(hs2):
    d = Path(hs2) / "UserData" / "chara" / "male"
    cards = sorted(p.name for p in d.iterdir() if p.is_file() and p.suffix.lower() == ".png")
    if not cards:
        raise SystemExit("no male cards in " + str(d))
    return cards[0]


def fresh_load(card, sex, state):
    """Delete the character this script loaded in an earlier run (same Studio session), then load the card anew,
    so the physics switch-off below starts from the game's own state."""
    objs = {o["key"]: o for o in call("scene")["objects"]}
    prev = state.get(sex)
    if prev and prev["key"] in objs and objs[prev["key"]].get("name") == prev.get("name"):
        call("delete", key=prev["key"])
    rep = call("chara.load", path=card, sex=sex)
    o = rep["added"][0]
    state[sex] = {"card": card, "key": o["key"], "name": o.get("name")}
    return o["key"]


def freeze(key):
    """Static pose, so only the sliders move bones: animation speed 0, eyes to the front, no blinking; every
    DynamicBone and the eye/neck look controllers switched off (shape.physics). A switched-off DynamicBone puts its
    bones back to the rest pose; the bridge keeps the bust/hip chains (DynamicBone_Ver02) on the kinematic pose of
    the current sliders by copying their reference transforms each frame, as the component itself does before it
    simulates."""
    call("chara.speed", key=key, speed=0)
    call("chara.face", key=key, eyesLook=0, neckLook=4, blink=False)
    phys = call("shape.physics", key=key, on=False)["components"]
    # Plugins: remove the card's ABMX bone modifiers (ABMX writes baseline x modifier every frame and would hide the
    # sliders' effect on the modified bones), then switch off every plugin controller on the character object.
    # Harmony patches of plugins stay active (see README).
    ctl = call("shape.controllers", key=key, abmxClear=True, foreignOff=True)
    off = [c["type"] for c in ctl["components"] if not c["enabled"]]
    return phys, ctl.get("abmxRemoved", []), off


def dump(key, root):
    rep = call("bones.dump", key=key, root=root, timeout=300)
    if rep.get("updatePending"):
        raise RuntimeError("shape update still pending at dump")
    return rep["bones"]


def as_rows(bones):
    """bone dump -> {path: (name, 13 float32 values)}"""
    out = {}
    for b in bones:
        vals = np.array(b["lp"] + b["lq"] + b["le"] + b["ls"], dtype=np.float32)
        if b["path"] in out:
            raise RuntimeError("duplicate path " + b["path"])
        out[b["path"]] = (b["name"], vals)
    return out


def set_shape(key, body, face):
    rep = call("shape.set", key=key, body=body, face=face, timeout=300)
    if not rep.get("settled"):
        raise RuntimeError("shape.set did not settle in %s frames" % rep.get("framesWaited"))
    return rep


def build_samples(n, oor=False):
    s = [("rest", -1, None, [NEUTRAL] * n)]
    if oor:
        for i in range(n):
            for v in OOR_SWEEP:
                vec = [NEUTRAL] * n
                vec[i] = v
                s.append(("sweep", i, v, vec))
        rng = random.Random(SEED_RANDOM_OOR)
        for _ in range(N_RANDOM_OOR):
            s.append(("random", -1, None, [-1.0 + 3.0 * rng.random() for _ in range(n)]))
        return s
    for i in range(n):
        for v in SWEEP:
            vec = [NEUTRAL] * n
            vec[i] = v
            s.append(("sweep", i, v, vec))
    rng = random.Random(SEED_RANDOM)
    for _ in range(N_RANDOM):
        s.append(("random", -1, None, [rng.random() for _ in range(n)]))
    rng = random.Random(SEED_PAIR)
    for _ in range(N_PAIR):
        vec = [NEUTRAL] * n
        i, j = rng.sample(range(n), 2)
        vec[i] = rng.random()
        vec[j] = rng.random()
        s.append(("pair", -1, None, vec))
    return s


def same(a, b):
    """exact equality of two as_rows() dumps"""
    if a.keys() != b.keys():
        return False
    return all(np.array_equal(a[k][1], b[k][1]) for k in a)


def run_set(name, key, part, counts, out, log, oor=False):
    n = counts[part]
    sweep = OOR_SWEEP if oor else SWEEP
    other = "face" if part == "body" else "body"
    root = "body" if part == "body" else "head"
    neutral_other = [NEUTRAL] * counts[other]
    samples = build_samples(n, oor)
    checks = {"set": name, "sliders": n, "root": root}
    t0 = time.time()

    # warm-up: a few extreme vectors, then neutral. The first slider change after loading moves some bones once
    # (seen on the female's cm_J_dan* chain: rest before any change differed from rest afterwards).
    for v in (0.0, 1.0, 0.25, 0.75):
        set_shape(key, [v] * counts["body"], [v] * counts["face"])
    # neutral, then the two checks that need no CSV: dump twice without changes
    set_shape(key, [NEUTRAL] * counts["body"], [NEUTRAL] * counts["face"])
    d1 = as_rows(dump(key, root))
    d2 = as_rows(dump(key, root))
    checks["a_dump_twice_identical"] = same(d1, d2)

    sp = open(out / (name + "_samples.csv"), "w", encoding="utf-8", newline="\n")
    bp = open(out / (name + "_bones.csv"), "w", encoding="utf-8", newline="\n")
    sp.write("sample,kind,slider,value,n," + ",".join("v%d" % i for i in range(n)) + "\n")
    bp.write("sample,bone,path,px,py,pz,qx,qy,qz,qw,ex,ey,ez,sx,sy,sz\n")
    rest = None
    sweep_rows = {}           # (slider, value) -> rows
    info_mismatch = []
    unsettled_frames = []
    for si, (kind, slider, value, vec) in enumerate(samples):
        body, face = (vec, neutral_other) if part == "body" else (neutral_other, vec)
        rep = set_shape(key, body, face)
        unsettled_frames.append(rep["framesWaited"])
        applied = rep[part + "Values"]
        info = call("shape.info", key=key)
        sent32 = [float(np.float32(v)) for v in vec]
        if info[part + "Values"] != applied or [float(np.float32(v)) for v in applied] != sent32 \
                or info[other + "Values"] != [float(np.float32(v)) for v in neutral_other]:
            info_mismatch.append(si)
        rows = as_rows(dump(key, root))
        if kind == "rest":
            rest = rows
        if kind == "sweep":
            sweep_rows[(slider, value)] = rows
        sp.write("%d,%s,%d,%s,%d,%s\n" % (si, kind, slider, "" if value is None else f32(value), n,
                                         ",".join(f32(v) for v in applied)))
        lines = []
        for path, (bname, vals) in rows.items():
            lines.append("%d,%s,%s,%s\n" % (si, bname, path, ",".join(str(v) for v in vals)))
        bp.write("".join(lines))
        if si % 50 == 0:
            log("  %s sample %d/%d  %.0fs" % (name, si, len(samples), time.time() - t0))
    sp.close()
    bp.close()

    # rest again at the end: no hysteresis
    set_shape(key, [NEUTRAL] * counts["body"], [NEUTRAL] * counts["face"])
    checks["rest_end_identical_to_rest"] = same(rest, as_rows(dump(key, root)))
    checks["a_dump_twice_identical"] = checks["a_dump_twice_identical"] and same(d1, rest)
    checks["b_sweep_half_identical_to_rest"] = all(same(rest, sweep_rows[(i, 0.5)]) for i in range(n))
    checks["b_failing_sliders"] = [i for i in range(n) if not same(rest, sweep_rows[(i, 0.5)])]
    checks["d_info_mismatch_samples"] = info_mismatch
    checks["samples"] = len(samples)
    checks["transforms"] = len(rest)
    names = [v[0] for v in rest.values()]
    cj = [x for x in names if x.startswith("cf_J_")]
    checks["e_counts"] = {
        "cf_s_": sum(1 for x in names if x.startswith("cf_s_")),
        "cf_J_": len(cj),
        "cf_J_*_s / *_s_L|R": sum(1 for x in cj if x.endswith("_s") or "_s_" in x),
        "cf_N_": sum(1 for x in names if x.startswith("cf_N_")),
        "cf_hit_": sum(1 for x in names if x.startswith("cf_hit_")),
        "k_f_": sum(1 for x in names if x.startswith("k_f_")),
        "other": sum(1 for x in names if not x.startswith(("cf_s_", "cf_J_", "cf_N_", "cf_hit_", "k_f_"))),
    }
    checks["frames_waited_min_max"] = [min(unsettled_frames), max(unsettled_frames)]

    # per slider: bones whose local TRS changed between value 0 and 1 (and anywhere in the sweep)
    per = []
    for i in range(n):
        a, b = sweep_rows[(i, 0.0)], sweep_rows[(i, 1.0)]
        moved = []
        for path in a:
            va, vb = a[path][1].astype(np.float64), b[path][1].astype(np.float64)
            dp = float(np.max(np.abs(vb[0:3] - va[0:3])))
            qa, qb = va[3:7], vb[3:7]
            dot = min(1.0, abs(float(np.dot(qa, qb))) / (np.linalg.norm(qa) * np.linalg.norm(qb)))
            dr = float(np.degrees(2.0 * np.arccos(dot)))
            dq = float(np.max(np.abs(vb[3:7] - va[3:7])))
            ds = float(np.max(np.abs(vb[10:13] - va[10:13])))
            if dp > 0 or dq > 0 or ds > 0:
                moved.append({"bone": a[path][0], "path": path, "pos": dp, "rotDeg": dr, "quat": dq, "scale": ds})
        any_sweep = set()
        for v in sweep:
            r = sweep_rows[(i, v)]
            for path in r:
                if not np.array_equal(r[path][1], rest[path][1]):
                    any_sweep.add(path)
        per.append({"slider": i, "moved01": moved, "movedAnySweep": len(any_sweep)})
    checks["c_sliders_moving_nothing"] = [p["slider"] for p in per if p["movedAnySweep"] == 0]
    checks["c_sliders_same_at_0_and_1_but_moving"] = [p["slider"] for p in per if not p["moved01"] and p["movedAnySweep"] > 0]
    checks["per_slider"] = per
    checks["seconds"] = round(time.time() - t0, 1)
    (out / (name + "_checks.json")).write_text(json.dumps(checks, ensure_ascii=False, indent=1), encoding="utf-8")
    return checks


# ---------------------------------------------------------------- reports (no Studio needed)

SETS = ("female_body", "female_face", "male_body", "male_face")
TINY = 1e-6


def g(x):
    return "%.4g" % x


def load_bones(path, samples=None):
    d = {}
    with open(path, encoding="utf-8") as f:
        next(f)
        for line in f:
            a = line.rstrip("\n").split(",")
            s = int(a[0])
            if samples is not None and s not in samples:
                continue
            d.setdefault(s, {})[a[2]] = (a[1], np.array(a[3:], dtype=np.float64))
    return d


def sweep_index(out, name):
    """(slider, value) -> sample number, and the kind counts"""
    idx, kinds = {}, {}
    with open(out / (name + "_samples.csv"), encoding="utf-8") as f:
        next(f)
        for line in f:
            a = line.split(",", 4)
            kinds[a[1]] = kinds.get(a[1], 0) + 1
            if a[1] == "sweep":
                idx[(int(a[2]), float(a[3]))] = int(a[0])
    return idx, kinds


def euler_wrap_count(rows):
    """transforms whose Unity Euler angles are reported above 180 (small negative angles shown as 360 - x)"""
    return sum(1 for (_, v) in rows.values() if np.any(v[7:10] > 180.0))


def write_reports(out, summary_path, install):
    meta = json.loads((out / "meta.json").read_text(encoding="utf-8"))
    checks = {n: json.loads((out / (n + "_checks.json")).read_text(encoding="utf-8")) for n in SETS}
    idx = {n: sweep_index(out, n) for n in SETS}
    fm, mm = meta["female"], meta["male"]
    abmx_m = ", ".join(meta["abmxRemoved"]["male"]) or "none"
    abmx_f = ", ".join(meta["abmxRemoved"]["female"]) or "none"

    # README in the cache folder
    L = ["# Shape-slider measurement (HS2Next Phase 1.7)", "",
         "Black-box measurement of the HS2 Maker body and face shape sliders in the running CharaStudio, written by",
         "`Tools/shape_measure.py` through a private Studio Bridge build. Provenance summary:",
         "`Tools/survey/shape/measure_summary.md`.", "",
         "- Date: %s" % meta["date"],
         "- Game: %s, Application.version %s, Unity %s (install: %s)" % (
             fm["product"], fm["gameVersion"], fm["unityVersion"], install),
         "- Cards: female `%s`, male `%s` (first male card by file name)" % (meta["cards"]["female"], meta["cards"]["male"]),
         "- Head types: female headId %s (%s, curves %s), male headId %s (%s, curves %s). The face sliders' curves come"
         % (fm["headId"], fm["headMainAB"], fm["headShapeAnime"], mm["headId"], mm["headMainAB"], mm["headShapeAnime"]),
         "  from the head type's ShapeAnime asset, so face tables are per head type.",
         "- Slider counts: body %d, face %d (both sexes)" % (fm["bodyCount"], fm["faceCount"]), "",
         "## Files", "",
         "Per set (`female_body`, `female_face`, `male_body`, `male_face`):", "",
         "- `<set>_samples.csv`: `sample,kind,slider,value,n,v0..v{n-1}`. kind is rest | sweep | random | pair. For sweep,",
         "  slider/value are the swept index and value; otherwise slider is -1 and value empty. v0.. are the values of the",
         "  measured part as the game stored them (read back with GetShapeBodyValue / GetShapeFaceValue). The other part",
         "  (face for *_body, body for *_face) is at 0.5 for the whole set.",
         "- `<set>_bones.csv`: `sample,bone,path,px,py,pz,qx,qy,qz,qw,ex,ey,ez,sx,sy,sz`, one row per transform: local",
         "  position, local rotation (quaternion xyzw), Unity's localEulerAngles (degrees, 0..360, so small negative",
         "  angles appear as ~359.x), local scale. path is relative to the root (`cf_J_Root` of `p_cf_anim` for *_body,",
         "  without the `p_cf_head_bone` subtree; `p_cf_head_bone` for *_face); the root itself is the row with an empty",
         "  path. Rows are in depth-first hierarchy order. Floats are the shortest decimals that round-trip the float32",
         "  values.",
         "- `<set>_checks.json`: sanity checks and the per-slider list of bones that changed between 0 and 1.",
         "- `meta.json`: cards, versions, slider names, the cards' own slider values, switched-off components.",
         "- `run.log`, `session.json`: run log; Studio object keys of the loaded characters.", "",
         "Samples: rest (all sliders 0.5) is sample 0; sweep: each slider at 0, 0.25, 0.5, 0.75, 1 with the others at 0.5",
         "(5 per slider, slider by slider); random: %d vectors, every slider uniform in [0, 1) (Python" % N_RANDOM,
         "random.Random(%d).random(), vector by vector, slider by slider); pair: %d vectors from random.Random(%d): two" % (
             SEED_RANDOM, N_PAIR, SEED_PAIR),
         "sliders rng.sample(range(n), 2), then their two values rng.random(); the rest at 0.5.", "",
         "## Conditions", "",
         "- Characters loaded fresh into an empty scene; default Studio pose (anime group 0, category 0, no 0) with",
         "  animation speed 0; eyes front; no blinking. The cf_J_* pose bones therefore carry that pose.",
         "- Every DynamicBone / DynamicBone_Ver01 / DynamicBone_Ver02 and the eye and neck look controllers switched off.",
         "  A switched-off DynamicBone puts its bones back to the rest pose. For the female bust/hip chains",
         "  (DynamicBone_Ver02; the male's are switched off by the game itself) the bridge copies each particle bone's",
         "  reference transform every frame, as the component does before it simulates, so those bones show the kinematic",
         "  pose for the current sliders.",
         "- ABMX bone modifiers of the cards removed (female: %s; male: %s), then every plugin controller on the" % (abmx_f, abmx_m),
         "  character object switched off (%d components, among them ABMX, Heelz, BetterPenetration, UncensorSelector," % len(
             meta["controllersOff"]["female"]),
         "  Bulge, EarWiggle, HS2PE). Harmony patches of plugins stay active. Known one that matters:",
         "  HS2_UnlockPlayerHeight (the male height slider works; the unpatched game pins the male height value at 0.75).",
         "  Sliders stay inside [0, 1], the range of the unpatched sliders.",
         "- Before each set: four warm-up vectors (all 0, 1, 0.25, 0.75), then neutral. The first slider change after",
         "  loading moved some bones once (female cm_J_dan* chain); the warm-up keeps that out of the samples.",
         "- Each sample: shape.set (ChaControl.SetShapeBodyValue / SetShapeFaceValue for every slider of both parts); the",
         "  bridge waits until the update flags are clear and every local transform under cf_J_Root is equal on two",
         "  consecutive frames (at least 3 frames), then bones.dump.",
         "- `../shapemeasure_pluginson/` (if present): an earlier run with the plugin controllers and the cards' ABMX data",
         "  active, kept for comparison only (differences: male cf_N_height scale from its ABMX modifier, the female",
         "  cm_J_dan* chain, and changes below 1e-6 in a few *_dam bones and the eyes' cf_J_look_*).", ""]
    (out / "README.md").write_text("\n".join(L), encoding="utf-8")

    # provenance summary (committed; no paths)
    S = ["# Shape-slider measurement summary (Phase 1.7)", "",
         "Provenance record of the body/face shape tables: black-box measurement of the running CharaStudio",
         "(%s, Application.version %s, Unity %s, install %s) on %s. Driver: `Tools/shape_measure.py`" % (
             fm["product"], fm["gameVersion"], fm["unityVersion"], install, meta["date"]),
         "(talks to a private Studio Bridge build: shape.info, shape.set, shape.physics, shape.controllers, bones.dump).",
         "The measured CSV files are not committed; the driver regenerates them (about 4 minutes) and this summary.", "",
         "## Setup", "",
         "- Cards: female `%s` (headId %s, face curves `%s`), male `%s` (headId %s, face curves `%s`; the first male" % (
             meta["cards"]["female"], fm["headId"], fm["headShapeAnime"], meta["cards"]["male"], mm["headId"],
             mm["headShapeAnime"]),
         "  card by file name).",
         "- Sliders: body %d, face %d for both sexes. Neutral = 0.5 for every slider; the part not being measured stays" % (
             fm["bodyCount"], fm["faceCount"]),
         "  neutral for the whole set.",
         "- Samples per set: rest 1; sweep 5 per slider (0, 0.25, 0.5, 0.75, 1); random %d (seed %d); pair %d (seed %d)." % (
             N_RANDOM, SEED_RANDOM, N_PAIR, SEED_PAIR),
         "- Static pose (default Studio pose, animation speed 0). DynamicBone physics and eye/neck look switched off; the",
         "  female bust/hip chains are kept on their kinematic pose. The cards' ABMX modifiers removed (male: %s;" % abmx_m,
         "  female: %s) and every plugin controller on the character switched off. Plugin Harmony patches stay active;" % abmx_f,
         "  HS2_UnlockPlayerHeight makes the male height slider work (the unpatched game pins it at 0.75).",
         "- Each sample is set through ChaControl.SetShapeBodyValue / SetShapeFaceValue and dumped after the game applied",
         "  it (update flags clear, every local transform equal on two consecutive frames). Every sample settled at the",
         "  3-frame minimum: the values take effect in the LateUpdate of the frame they are set in.", "",
         "## Sets", "",
         "| set | root | sliders | samples (rest/sweep/random/pair) | transforms | cf_J_ (of which *_s) | cf_N_ | cf_hit_ | k_f_ | other | cf_s_ |",
         "|---|---|---|---|---|---|---|---|---|---|---|"]
    for n in SETS:
        c = checks[n]
        k = idx[n][1]
        e = c["e_counts"]
        S.append("| %s | %s | %d | %d (%d/%d/%d/%d) | %d | %d (%d) | %d | %d | %d | %d | %d |" % (
            n, "cf_J_Root without head" if c["root"] == "body" else "p_cf_head_bone", c["sliders"], c["samples"],
            k.get("rest", 0), k.get("sweep", 0), k.get("random", 0), k.get("pair", 0), c["transforms"], e["cf_J_"],
            e["cf_J_*_s / *_s_L|R"], e["cf_N_"], e["cf_hit_"], e["k_f_"], e["other"], e["cf_s_"]))
    S += ["", "HS2 has no `cf_s_*` transforms (that is the Koikatsu naming). The shape helpers are the `cf_J_*_s` /",
          "`cf_J_*_s_L|R` transforms, plus some `cf_J_*_t*`, `cf_hit_*`, `cf_N_height` and accessory points `k_f_*`; the",
          "counts above are by name prefix.", "",
          "## Sanity checks", "",
          "| check | female_body | female_face | male_body | male_face |", "|---|---|---|---|---|"]

    def row(label, f):
        S.append("| %s | %s |" % (label, " | ".join(f(checks[n]) for n in SETS)))

    yn = lambda b: "pass" if b else "FAIL"
    row("(a) two dumps without change identical (and equal to sample 0)", lambda c: yn(c["a_dump_twice_identical"]))
    row("(b) rest identical to every sweep sample at 0.5", lambda c: yn(c["b_sweep_half_identical_to_rest"]))
    row("rest set again after the set identical to sample 0", lambda c: yn(c["rest_end_identical_to_rest"]))
    row("(c) sliders that move no bone", lambda c: ", ".join(map(str, c["c_sliders_moving_nothing"])) or "none")
    row("(d) samples where shape.info differs from the values set", lambda c: str(len(c["d_info_mismatch_samples"])))
    row("frames waited per sample (min-max)", lambda c: "%d-%d" % tuple(c["frames_waited_min_max"]))
    S += ["", "Identity checks are exact float comparisons of every transform's local position, rotation, Euler angles and",
          "scale. (d) compares the values read back by shape.info with the float32 of the values sent, for both parts.", ""]

    # female vs male body sweeps
    fb = load_bones(out / "female_body_bones.csv")
    mb = load_bones(out / "male_body_bones.csv")
    worst = []
    for i in range(fm["bodyCount"]):
        for v in SWEEP:
            rf, rm = fb[idx["female_body"][0][(i, v)]], mb[idx["male_body"][0][(i, v)]]
            for p in rf:
                if p in rm and rf[p][0].startswith("cf_"):
                    df = rf[p][1] - fb[0][p][1]
                    dm = rm[p][1] - mb[0][p][1]
                    df[7:10] = 0
                    dm[7:10] = 0
                    d = float(np.max(np.abs(df - dm)))
                    if d > TINY:
                        worst.append((d, i, v, rf[p][0]))
    worst.sort(reverse=True)
    sl = sorted(set(w[1] for w in worst))
    S += ["## Female vs male body sliders", "",
          "Change from rest (position, quaternion, scale) of every `cf_*` transform the two skeletons share, female minus",
          "male, over all sweep samples: " + (
              "identical within %g for every slider." % TINY if not worst else
              "differs for sliders %s (largest: %s)." % (", ".join(map(str, sl)), "; ".join(
                  "slider %d at %s, %s %s" % (w[1], g(w[2]), w[3], g(w[0])) for w in worst[:6]))), ""]
    fh = load_bones(out / "female_face_bones.csv", {0})[0]
    mh = load_bones(out / "male_face_bones.csv", {0})[0]
    S += ["Euler wrap: in the rest samples %d (female body), %d (female head), %d (male body) and %d (male head)" % (
              euler_wrap_count(fb[0]), euler_wrap_count(fh), euler_wrap_count(mb[0]), euler_wrap_count(mh)),
          "transforms have a Unity Euler component above 180 (a small negative angle reported as 360 - x), and sweeps",
          "cross that wrap. Fit rotations from the quaternions.", ""]
    del fb, mb

    S += ["## Bones moved by each slider", "",
          "Bones whose local TRS differs between slider value 0 and 1 (others at 0.5). pos = max |delta| over x, y, z (game",
          "units, 1 = 10 cm); rot = angle between the two local rotations in degrees; scl = max |delta| of the local scale.",
          "Changes no larger than %g in every channel are counted as tiny and not listed. Slider names are the game's." % TINY,
          ""]
    for n in SETS:
        sex, part = n.split("_")
        names = meta[sex]["bodyNames" if part == "body" else "faceNames"]
        S += ["### %s" % n, ""]
        for p in checks[n]["per_slider"]:
            i = p["slider"]
            big = [m for m in p["moved01"] if max(m["pos"], m["quat"], m["scale"]) > TINY]
            tiny = len(p["moved01"]) - len(big)
            parts = []
            for m in big:
                ch = []
                if m["pos"] > TINY:
                    ch.append("pos " + g(m["pos"]))
                if m["quat"] > TINY:
                    ch.append("rot " + g(m["rotDeg"]))
                if m["scale"] > TINY:
                    ch.append("scl " + g(m["scale"]))
                parts.append("%s (%s)" % (m["bone"], ", ".join(ch)))
            extra = "" if p["movedAnySweep"] == len(p["moved01"]) else \
                " (%d differ from rest somewhere in the sweep)" % p["movedAnySweep"]
            S.append("- %d %s: %d bones%s%s. %s" % (i, names[i], len(big), " + %d tiny" % tiny if tiny else "", extra,
                                                  "; ".join(parts)))
        S.append("")
    summary_path.parent.mkdir(parents=True, exist_ok=True)
    summary_path.write_text("\n".join(S), encoding="utf-8")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--sets", default="female_body,female_face,male_body,male_face")
    ap.add_argument("--out", default=str(REPO / "Cache" / "shapemeasure"))
    ap.add_argument("--report-only", action="store_true", help="write README.md and the summary from existing files")
    ap.add_argument("--oor", action="store_true", help="values outside 0..1 (sets <set>_oor, meta_oor.json; see the docstring)")
    ap.add_argument("--install", help="the game install as the reports name it (default: the folder name of hs2Root)")
    args = ap.parse_args()
    out = Path(args.out)
    summary = REPO / "Tools" / "survey" / "shape" / "measure_summary.md"
    if args.report_only:
        write_reports(out, summary, install_label(args.install))
        return
    out.mkdir(parents=True, exist_ok=True)
    logf = open(out / "run.log", "a", encoding="utf-8")

    def log(msg):
        line = "%s %s" % (datetime.datetime.now().strftime("%H:%M:%S"), msg)
        print(line, flush=True)
        logf.write(line + "\n")
        logf.flush()

    hs2 = settings()["hs2Root"]
    male_card = first_male_card(hs2)
    statep = out / "session.json"
    state = json.loads(statep.read_text(encoding="utf-8")) if statep.exists() else {}
    ping = call("ping")
    if state.get("realtime", 1e18) > ping["realtime"]:
        state = {}                 # Studio restarted since: keys are stale
    state["realtime"] = ping["realtime"]
    keys = {"female": fresh_load(FEMALE_CARD, "female", state), "male": fresh_load(male_card, "male", state)}
    statep.write_text(json.dumps(state, ensure_ascii=False, indent=1), encoding="utf-8")
    time.sleep(2.0)
    meta = {"date": datetime.date.today().isoformat(), "cards": {"female": FEMALE_CARD, "male": male_card}, "sets": {},
            "physicsOff": {}, "abmxRemoved": {}, "controllersOff": {}}
    for sex, k in keys.items():
        phys, abmx, off = freeze(k)
        meta["physicsOff"][sex] = [c for c in phys if isinstance(c, str)]
        meta["abmxRemoved"][sex] = abmx
        meta["controllersOff"][sex] = off
    time.sleep(1.0)
    for sex, k in keys.items():
        info = call("shape.info", key=k)
        meta[sex] = {k2: info[k2] for k2 in ("sex", "bodyCount", "faceCount", "bodyNames", "faceNames", "gameVersion",
                                              "unityVersion", "product", "bodyRoot", "bodyRootParent", "headRoot",
                                              "headRootParent", "headId", "headName", "headMainAB",
                                              "headShapeAnime")}
        meta[sex]["cardValuesBody"] = info["bodyValues"]
        meta[sex]["cardValuesFace"] = info["faceValues"]
    meta_name = "meta_oor.json" if args.oor else "meta.json"
    old = {}
    if (out / meta_name).exists():
        old = json.loads((out / meta_name).read_text(encoding="utf-8")).get("sets", {})
    meta["sets"].update(old)
    for name in args.sets.split(","):
        sex, part = name.split("_")
        counts = {"body": meta[sex]["bodyCount"], "face": meta[sex]["faceCount"]}
        out_name = name + "_oor" if args.oor else name
        log("set %s: %d sliders" % (out_name, counts[part]))
        c = run_set(out_name, keys[sex], part, counts, out, log, args.oor)
        meta["sets"][out_name] = {k2: c[k2] for k2 in c if k2 != "per_slider"}
        log("set %s done in %ss: %s" % (out_name, c["seconds"], json.dumps(meta["sets"][out_name], ensure_ascii=False)))
        (out / meta_name).write_text(json.dumps(meta, ensure_ascii=False, indent=1), encoding="utf-8")
    if not args.oor and all((out / (n + "_checks.json")).exists() for n in SETS):
        write_reports(out, summary, install_label(args.install))
        log("reports written")
    log("all done")


if __name__ == "__main__":
    main()
