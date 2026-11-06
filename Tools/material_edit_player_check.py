"""
material_edit_player_check.py - material edits in the built app player (Phase 6.4; the row MaterialEditCheck.player).

Starts the app player itself (no plugins: HS2NEXT_NO_PLUGINS=1, HS2NEXT_PRIVATE_OFF=1) and checks over real HTTP from outside the process, in real
frames, with the UI off. It never uses the Unity Editor. Python 3.14, standard library only. Content: the clothed SFW visual targets of
Tools\\targets.json and vanilla items and maps, judged by numbers; saves use the placeholder thumbnail; a capture is compared by its bytes and never
opened. The start, the report's form and the exit codes are advancedpose_player_check.py's.

Usage (the run_tests.ps1 row MaterialEditCheck.player calls the first form):
    python -I Tools\\material_edit_player_check.py --exe <path to HS2Next.exe> --out <dir> [--project <project root>] [--port 17403] [--timeout-min 20]
    more options: --only MV1,MV2,MV3,MV4,MV5,RT,MW1,MW2,MW5,MW6,MW7,MJ3 (default all), --steps 120, --editor-list <file>, --map <ref>, --min-free-gb 8,
                  --ram-wait-min 10, --ready-timeout-min 12, --attach (use a player already serving on --port: no start, no quit)

Checks (one report line each):
  SETUP  ready time, versions
  MV1    materials.shaders in the player equals the Editor's candidate list (Spike\\out\\maker\\material_shaders_editor.json, written by
         MaterialEditTest.full): name for name, family, queue; 0 dropped
  MV2    every candidate's declared keywords equal the Editor's; a render probe on _DITHERSWITCH_ON, _EMISSIONCOLORTEXBASE_ON, _ALPHATEST_ON: two captures
         with the keyword off are byte-equal, the keyword on changes the bytes
  MV3    a keyword edit on a material whose shader is not under HS2Next/ is bad_args, and changes nothing; an undeclared keyword is not_found
  MV4    the property lists of the Editor list's named shaders, read through item.materials allProps on an item swapped to each, equal the Editor's
  MV5    a character: a keyword edit, a shader swap, save, scene.new, load: chara.edit.materials' keywords as expected
  RT     a round trip of every target with a map: character, hair, clothes, item and map: a float set, undo, redo, save, scene.new, load
  MW1    a drag step on a character object, an item and the map, each with 150 items and 3 textures: the median and p95 of the object's flush or pass
         (bars 2 ms and 4 ms; over a bar: WARN with the numbers; the lead decides); 0 rebuilds in chara.edit.info
  MW2    the commit of a value edit (bar 10 ms; WARN)
  MW5    a Timeline colour track on an item with 16 edits: the pass of each frame (bar 0.05 ms a frame; WARN)
  MW6    a 4,096 x 4,096 PNG imported: the command's time, reported
  MW7    a build: the value pass bounds the baseline's cost: at most 1 % of the build, or at most 1 ms (WARN)
  MJ3    a hair bone modifier's drag: the median flush of a step (bar 16 ms; WARN)
  QUIT   app.quit answers quitting:true and the player exits with 0
  LOG    exceptions in player.log: FAIL with 0 allowed

Outputs:
  <out>\\material_edit_player_report.txt  first and last line "SUMMARY: N PASS, M FAIL", second line "WARN: K (ids)", then one line per check
  <out>\\material_edit_player_check.json  details of every check
  <out>\\player.log, <out>\\userdata\\ (the saves)
Exit code: 0 every check passed (WARN allowed), 1 a check failed, 2 setup error, 3 aborted by the game gate.
Ports: 17403 by default. Always 127.0.0.1, never localhost.
"""
import argparse
import hashlib
import json
import os
import statistics
import struct
import sys
import threading
import time
import traceback
import zipfile
import zlib

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from advancedpose_player_check import (Abort, CmdError, Ctx, SetupError, check_log, guard, kill_player, log, quit_player, running_games, setup,  # noqa: E402
                                       target_rel, utc_now, T0)

DEFAULT_PORT = 17403
CLOCK = "2026-11-05T00:00:00Z"
BAR_STEP_MS, BAR_STEP_P95_MS, BAR_COMMIT_MS, BAR_TRACK_MS, BAR_HAIR_MS = 2.0, 4.0, 10.0, 0.05, 16.0
BAR_BUILD_SHARE, BAR_BUILD_MS = 0.01, 1.0
PROBE_ITEM = 399
ITEM_MANY = 200
SCENE_MAP = "illusion.hs2#17"
ROUTED_MAP = "2155x.map.interrogationroom#0"


def add(C, cid, ok, text, **data):
    C.R.add(cid, "PASS" if ok else "FAIL", text, **data)


def over(C, cid, ok, text, **data):
    """A bar: over it is the lead's call."""
    C.R.add(cid, "PASS" if ok else "WARN", text, **data)


def p95(xs):
    s = sorted(xs)
    return s[min(len(s) - 1, int(len(s) * 0.95))]


def ms_text(xs):
    return "median %.3f ms, p95 %.3f ms, max %.3f ms, n %d" % (statistics.median(xs), p95(xs), max(xs), len(xs))


def png_rgba(path, w, h, rgba):
    """A flat PNG of one colour, standard library only."""
    raw = (b"\0" + bytes(rgba) * w) * h

    def chunk(t, d):
        return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xffffffff)
    data = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(raw, 1)) + chunk(b"IEND", b"")
    with open(path, "wb") as f:
        f.write(data)
    return path


def png_gradient(path, w, h):
    """A PNG with varied rows, as a picture decodes, standard library only."""
    rows = [b"\0" + bytes((x * 7 + y * 13) & 255 for x in range(w * 4)) for y in range(256)]
    raw = b"".join(rows[y & 255] for y in range(h))

    def chunk(t, d):
        return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xffffffff)
    data = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(raw, 1)) + chunk(b"IEND", b"")
    with open(path, "wb") as f:
        f.write(data)
    return path


def new_scene(C):
    C.call({"cmd": "scene.new"}, timeout=300)


def add_item(C, no=PROBE_ITEM):
    key = C.call({"cmd": "item.add", "no": no})["added"][0]["key"]
    C.frames(2)
    return key


def load_chara(C, sex="female"):
    rel = target_rel(C, sex)
    key = C.call({"cmd": "chara.load", "path": rel, "sex": sex}, timeout=300)["added"][0]["key"]
    C.frames(2)
    return key


def item_slots(C, key, all_props=True):
    r = C.call({"cmd": "item.materials", "key": key, "allProps": all_props})
    return [(rd, s) for rd in r.get("renderers") or [] for s in rd.get("slots") or [] if s and s.get("material")]


def fname(prop):
    """The stored form of a shader property: without the leading underscore (the runtime adds it back)."""
    return prop[1:] if prop.startswith("_") else prop


def fmt(name):
    """A material's name as an item stores it."""
    return (name or "").replace("(Instance)", "").replace(" Instance", "").strip()


def set_item(C, key, **edit):
    return C.call(dict({"cmd": "item.materials.set", "key": key}, **edit), timeout=120)


def capture(C, name):
    """The sha256 of a capture's bytes; the file goes at once, nothing opens it."""
    C.frames(3)
    r = C.call({"cmd": "capture", "name": name, "width": 256, "height": 256}, timeout=120)
    path = r["path"]
    with open(path, "rb") as f:
        h = hashlib.sha256(f.read()).hexdigest()
    os.remove(path)
    return h


def editor_list(C):
    path = C.a.editor_list or os.path.join(C.project, "Spike", "out", "maker", "material_shaders_editor.json")
    if not os.path.isfile(path):
        return None, path
    with open(path, encoding="utf-8") as f:
        return json.load(f), path


# ---------------------------------------------------------------- MV1, MV2

def mv1(C):
    doc, path = editor_list(C)
    r = C.call({"cmd": "materials.shaders"})
    mine = r.get("shaders") or []
    C.info["candidates"] = [s["name"] for s in mine]
    if doc is None:
        C.R.add("MV1", "WARN", "no Editor list at %s (run MaterialEditTest.full): %d candidates, not compared" % (path, len(mine)))
        return
    want = doc["candidates"]
    a, b = [s["name"] for s in want], [s["name"] for s in mine]
    dropped = [n for n in a if n not in b]
    extra = [n for n in b if n not in a]
    same = {s["name"]: s for s in mine}
    other = [s["name"] for s in want if s["name"] in same and (same[s["name"]].get("family") != s.get("family") or same[s["name"]].get("queue") != s.get("queue"))]
    add(C, "MV1", a == b and not other and len(b) > 0, "%d candidates in the player, %d in the Editor; dropped %d, extra %d, family or queue differs %d%s"
        % (len(b), len(a), len(dropped), len(extra), len(other), (": " + ", ".join((dropped + extra + other)[:6])) if dropped or extra or other else ""),
        dropped=dropped, extra=extra, other=other)


def probe_for(C, keyword, cands):
    for c in cands:
        if keyword in (c.get("keywords") or []) and "Clothes" in c["name"]:
            return c
    for c in cands:
        if keyword in (c.get("keywords") or []):
            return c
    return None


def mv2(C):
    doc, _ = editor_list(C)
    cands = C.call({"cmd": "materials.shaders"}).get("shaders") or []
    if doc is None:
        C.R.add("MV2", "WARN", "no Editor list: keywords not compared")
    else:
        want = {s["name"]: s.get("keywords") or [] for s in doc["candidates"]}
        bad = [c["name"] for c in cands if c["name"] in want and sorted(c.get("keywords") or []) != sorted(want[c["name"]])]
        n = sum(len(c.get("keywords") or []) for c in cands)
        add(C, "MV2", not bad, "%d keywords on %d candidates; shaders whose keywords differ from the Editor's: %d%s" % (n, len(cands), len(bad), (" (" + ", ".join(bad[:5]) + ")") if bad else ""), bad=bad)
    half = png_rgba(os.path.join(C.out, "probe_half.png"), 64, 64, (200, 120, 60, 128))
    for kw in ("_DITHERSWITCH_ON", "_EMISSIONCOLORTEXBASE_ON", "_ALPHATEST_ON"):
        cid = "MV2" + kw
        cand = probe_for(C, kw, cands)
        if cand is None:
            C.R.add(cid, "WARN", "no candidate declares %s" % kw)
            continue
        new_scene(C)
        key = add_item(C)
        C.call({"cmd": "camera.set", "target": [0, 1, 0], "rot": [15, 180, 0], "dist": 6, "fov": 40})
        _, s0 = item_slots(C, key)[0]
        m = fmt(s0["material"])
        set_item(C, key, items=[{"type": "shader", "material": m, "shader": cand["name"]}])
        C.frames(3)
        _, s1 = item_slots(C, key)[0]
        names = s1.get("props") or {}
        edits = []
        if "_MainTex" in names:
            edits.append({"type": "texture", "material": m, "property": "MainTex", "file": half})
        for p, v in (("_EmissionStrength", 1.0), ("_Cutoff", 0.9 if kw == "_ALPHATEST_ON" else 0.1), ("_alpha", 1.0)):
            if p in names:
                edits.append({"type": "float", "material": m, "property": fname(p), "value": v})
        if "_EmissionColor" in names:
            edits.append({"type": "color", "material": m, "property": "EmissionColor", "value": [0, 0, 1, 1]})
        set_item(C, key, items=edits)
        was = kw in (s1.get("keywordsCurrent") or [])
        h1, h2 = capture(C, "mv2_a"), capture(C, "mv2_b")
        set_item(C, key, type="keyword", material=m, property=fname(kw), value=not was)
        h3 = capture(C, "mv2_c")
        if h1 != h2:
            C.R.add(cid, "WARN", "two captures with the keyword unchanged differ on %s: the probe cannot tell" % cand["name"])
        else:
            add(C, cid, h3 != h1, "%s on %s: the capture %s" % (kw, cand["name"], "changed" if h3 != h1 else "did not change"), shader=cand["name"])


# ---------------------------------------------------------------- MV3, MV4, MV5

def mv3(C):
    new_scene(C)
    r = C.call({"cmd": "map.load", "ref": C.a.routed_map, "wait": True}, timeout=600)
    if r.get("state") != "Loaded":
        C.R.add("MV3", "WARN", "%s did not load: %s" % (C.a.routed_map, r.get("state")))
        return
    mats = C.call({"cmd": "map.materials"}).get("materials") or []
    lit = next((m for m in mats if str(m.get("shader")).startswith("Universal Render Pipeline/")), None)
    own = next((m for m in mats if str(m.get("shader")).startswith("HS2Next/")), None)
    if lit is None:
        C.R.add("MV3", "WARN", "no URP material on %s" % C.a.routed_map)
    else:
        r = C.call({"cmd": "map.materials.set", "type": "keyword", "material": lit["name"], "property": "EMISSION", "value": True}, check=False)
        items = C.call({"cmd": "map.materials.items"})["targets"][0]["items"]
        add(C, "MV3", not r.get("ok") and r.get("code") == "bad_args" and not items, "a keyword on %s: %s; items %d" % (lit["shader"], r.get("code"), len(items)))
    if own is not None:
        r = C.call({"cmd": "map.materials.set", "type": "keyword", "material": own["name"], "property": "NO_SUCH_KEYWORD", "value": True}, check=False)
        add(C, "MV3b", not r.get("ok") and r.get("code") == "not_found", "an undeclared keyword on %s: %s" % (own["shader"], r.get("code")))
    new_scene(C)


def mv4(C):
    doc, path = editor_list(C)
    if doc is None:
        C.R.add("MV4", "WARN", "no Editor list at %s" % path)
        return
    new_scene(C)
    key = add_item(C)
    _, s0 = item_slots(C, key)[0]
    m = fmt(s0["material"])
    bad, n = [], 0
    for name, props in doc["shaders"].items():
        set_item(C, key, type="shader", material=m, shader=name)
        C.frames(2)
        _, s = item_slots(C, key)[0]
        got = sorted((s.get("props") or {}).keys())
        n += 1
        if s.get("shader") != name or got != sorted(props):
            bad.append("%s: %s, %d properties against %d" % (name, s.get("shader"), len(got), len(props)))
    add(C, "MV4", n >= 5 and not bad, "%d named shaders: property lists equal the Editor's%s" % (n, ("; differs: " + "; ".join(bad[:4])) if bad else ""), bad=bad)


def chara_material(C, key, part="clothes"):
    """A clothes material on an HS2Next/ shader: (kind, material name, its reply)."""
    for kind in range(8):
        r = C.call({"cmd": "chara.edit.materials", "key": key, "part": part, "kind": kind}, check=False)
        for rd in r.get("renderers") or []:
            for m in rd.get("slots") or []:
                if m and str(m.get("shader")).startswith("HS2Next/"):
                    return kind, fmt(m["material"]), m
    return None, None, None


def save(C, name):
    return C.call({"cmd": "scene.save", "name": name, "overwrite": True, "thumb": "placeholder", "clock": CLOCK}, timeout=300)["path"]


def load(C, path):
    C.call({"cmd": "scene.load", "path": path}, timeout=600)
    t = time.time()
    while time.time() - t < 300:
        st = C.call({"cmd": "map.state"}, check=False)
        if st.get("state") != "Loading":
            break
        C.frames(5)
    C.frames(5)


def mv5(C):
    new_scene(C)
    key = load_chara(C)
    kind, mat, m0 = chara_material(C, key)
    if kind is None:
        C.R.add("MV5", "WARN", "no clothes material on an HS2Next/ shader")
        return
    target = "clothes:%d" % kind
    cands = C.call({"cmd": "materials.shaders"}).get("shaders") or []
    cur = m0["shader"]
    cand = next((c for c in cands if c["name"] != cur and "AIT" in c["name"] and "_DITHERSWITCH_ON" in (c.get("keywords") or [])), None)
    if cand is None:
        C.R.add("MV5", "WARN", "no candidate with _DITHERSWITCH_ON")
        return

    def read():
        for rd in C.call({"cmd": "chara.edit.materials", "key": key, "part": "clothes", "kind": kind}).get("renderers") or []:
            for m in rd.get("slots") or []:
                if m and fmt(m.get("material")) == mat:
                    return m
        return None
    C.call({"cmd": "chara.edit.materials.set", "key": key, "target": target, "type": "shader", "material": mat, "shader": cand["name"]}, timeout=120)
    C.frames(3)
    declared = set(cand.get("keywords") or [])
    m1 = read()
    left = [k for k in m1.get("keywords") or [] if k not in declared and k != "_RECEIVE_SHADOWS_OFF"]
    add(C, "MV5", m1["shader"] == cand["name"] and not left, "the swap: shader %s, undeclared keywords left %d%s" % (m1["shader"], len(left), (" " + " ".join(left[:4])) if left else ""))
    on = "_DITHERSWITCH_ON" in (m1.get("keywords") or [])
    C.call({"cmd": "chara.edit.materials.set", "key": key, "target": target, "type": "keyword", "material": mat, "property": "DITHERSWITCH_ON", "value": not on}, timeout=120)
    C.frames(3)
    m2 = read()
    add(C, "MV5b", ("_DITHERSWITCH_ON" in (m2.get("keywords") or [])) == (not on), "the keyword edit: _DITHERSWITCH_ON %s" % (not on))
    path = save(C, "mv5")
    new_scene(C)
    load(C, path)
    key2 = C.call({"cmd": "scene"})["objects"][0]["key"]
    key = key2
    m3 = read()
    add(C, "MV5c", m3 is not None and m3["shader"] == cand["name"] and sorted(m3.get("keywords") or []) == sorted(m2.get("keywords") or []) and m3.get("queue") == m2.get("queue"),
        "save, load: shader %s, keywords %s, queue %s" % (m3 and m3["shader"], m3 and " ".join(m3.get("keywords") or []), m3 and m3.get("queue")))
    new_scene(C)


# ---------------------------------------------------------------- RT

def float_of(props):
    for p, v in (props or {}).items():
        if isinstance(v, (int, float)) and not isinstance(v, bool) and p.startswith("_") and p not in ("_Cutoff", "_Surface", "_ZWrite", "_Cull", "_SrcBlend", "_DstBlend"):
            return p, float(v)
    return None, None


def rt(C):
    new_scene(C)
    C.call({"cmd": "map.load", "ref": C.a.map, "wait": True}, timeout=600)
    key = load_chara(C)
    item = add_item(C)
    targets = []
    body = C.call({"cmd": "chara.edit.materials", "key": key, "part": "body"}, check=False)
    for rd in body.get("renderers") or []:
        for m in rd.get("slots") or []:
            p, v = float_of(m.get("props")) if m else (None, None)
            if p and not targets:
                targets.append(("character", {"cmd": "chara.edit.materials.set", "key": key, "target": "character"},
                                {"cmd": "chara.edit.materials.items", "key": key, "target": "character"}, fmt(m["material"]), p, v))
    for part, cmdargs in (("hair", {"part": "hair", "hairPart": 0}), ("clothes", {"part": "clothes", "kind": 0})):
        r = C.call(dict({"cmd": "chara.edit.materials", "key": key}, **cmdargs), check=False)
        for rd in r.get("renderers") or []:
            for m in rd.get("slots") or []:
                p, v = float_of(m.get("props")) if m else (None, None)
                if p and not any(t[0] == part for t in targets):
                    targets.append((part, {"cmd": "chara.edit.materials.set", "key": key, "target": ("hair:0" if part == "hair" else "clothes:0")},
                                    {"cmd": "chara.edit.materials.items", "key": key, "target": ("hair:0" if part == "hair" else "clothes:0")}, fmt(m["material"]), p, v))
    acc = [x["target"] for x in C.call({"cmd": "chara.edit.materials.items", "key": key})["targets"] if str(x.get("target")).startswith("acc:")]
    r = C.call({"cmd": "chara.edit.materials", "key": key, "part": "accessories"}, check=False)
    for rd in r.get("renderers") or []:
        for m in rd.get("slots") or []:
            p, v = float_of(m.get("props")) if m else (None, None)
            if p and acc and not any(t[0] == "accessory" for t in targets):
                # the accessories' slots and ids are in the same order; a name the target lacks is what the set below would say
                tg = acc[min(int(rd.get("slot") or 0), len(acc) - 1)]
                targets.append(("accessory", {"cmd": "chara.edit.materials.set", "key": key, "target": tg}, {"cmd": "chara.edit.materials.items", "key": key, "target": tg}, fmt(m["material"]), p, v))
    if not any(t[0] == "accessory" for t in targets):
        C.R.add("RT_accessory", "WARN", "no float on an accessory of the target: %d targets, %d renderers" % (len(acc), len(r.get("renderers") or [])))
    _, s = item_slots(C, item)[0]
    p, v = float_of(s.get("props"))
    if p:
        targets.append(("item", {"cmd": "item.materials.set", "key": item}, {"cmd": "item.materials.items", "key": item}, fmt(s["material"]), p, v))
    ms = C.call({"cmd": "map.materials", "props": True, "limit": 200}).get("materials") or []
    for m in ms:
        p, v = float_of(m.get("props"))
        if p and str(m.get("shader")).startswith("HS2Next/"):
            targets.append(("map", {"cmd": "map.materials.set"}, {"cmd": "map.materials.items"}, m["name"], p, v))
            break
    done = []

    def stored(q):
        return json.dumps(C.call(q)["targets"][0]["items"], sort_keys=True)
    for name, setq, readq, mat, p, v in targets:
        to = v + 0.25 if v < 0.5 else v - 0.25
        C.call(dict(setq, type="float", material=mat, property=fname(p), value=to), timeout=120)
        items = C.call(readq)["targets"][0]["items"]
        good = any(i.get("material") == mat and abs(float(i.get("value")) - to) < 1e-6 and i.get("status") == "applied" for i in items)
        C.call({"cmd": "undo"})
        gone = not any(i.get("material") == mat and i.get("type") == "float" for i in C.call(readq)["targets"][0]["items"])
        C.call({"cmd": "redo"})
        again = any(i.get("material") == mat for i in C.call(readq)["targets"][0]["items"])
        done.append((name, stored(readq), readq))
        add(C, "RT_" + name, good and gone and again, "%s %s %s: set %s, undo %s, redo %s" % (name, mat, p, good, gone, again))
    path = save(C, "rt")
    new_scene(C)
    load(C, path)
    for name, before, q in done:
        add(C, "RT_" + name + "_load", stored(q) == before, "%s: the array after save, scene.new, load" % name)
    new_scene(C)


# ---------------------------------------------------------------- MW1, MW2

def many_items(slots, png, cap=147):
    """Up to cap float or colour items, 3 textures, over the slots (name, props)."""
    items, tex, seen = [], [], set()
    for name, props in slots:
        for p, v in (props or {}).items():
            if not p.startswith("_") or p.endswith("_ST") or (name, p) in seen:
                continue
            seen.add((name, p))
            if isinstance(v, (int, float)) and not isinstance(v, bool) and len(items) < cap and p not in ("_Cutoff", "_Surface", "_ZWrite", "_Cull", "_SrcBlend", "_DstBlend", "_ZTest"):
                items.append({"type": "float", "material": name, "property": fname(p), "value": float(v) + 0.0625})
            elif isinstance(v, list) and len(v) == 4 and len(items) < cap and "Color" in p and all(x is not None for x in v):
                items.append({"type": "color", "material": name, "property": fname(p), "value": [min(1.0, abs(float(x)) * 0.5 + 0.0625) for x in v]})
            elif (v is None or isinstance(v, (str, dict))) and len(tex) < 3 and "Tex" in p:
                tex.append({"type": "texture", "material": name, "property": fname(p), "file": png})
    return items + tex


def counted(items):
    return "%d items (%d textures)" % (len(items), sum(1 for e in items if e["type"] == "texture"))


def first_float(q):
    return next(i for i in q["items"] if i["type"] == "float")


def drag(C, setq, read, name, steps):
    first = first_float(setq)
    C.call(dict({k: v for k, v in setq.items() if k != "items"}, type="float", material=first["material"], property=first["property"], value=first["value"]))
    xs = []
    base = {k: v for k, v in setq.items() if k != "items"}
    for i in range(steps):
        C.call(dict(base, type="float", material=first["material"], property=first["property"], value=first["value"] + 0.001 * (i + 1), merge=True), timeout=60)
        xs.append(read())
    return xs


def mw1(C):
    steps = C.a.steps
    png = png_rgba(os.path.join(C.out, "probe_small.png"), 64, 64, (90, 160, 200, 255))
    new_scene(C)
    item = add_item(C, ITEM_MANY)
    slots = [(fmt(s["material"]), s.get("props")) for _, s in item_slots(C, item)]
    q = {"cmd": "item.materials.set", "key": item, "items": many_items(slots, png)}
    C.call(q, timeout=300)

    def item_ms():
        return float(C.call({"cmd": "item.materials.items", "key": item})["targets"][0].get("passMs") or 0)
    xs = drag(C, q, item_ms, "item", steps)
    over(C, "MW1_item", statistics.median(xs) <= BAR_STEP_MS and p95(xs) <= BAR_STEP_P95_MS, "item, %s: %s (bars %.0f and %.0f ms)" % (counted(q["items"]), ms_text(xs), BAR_STEP_MS, BAR_STEP_P95_MS), ms=xs)
    f = first_float(q)
    C.call({"cmd": "item.materials.set", "key": item, "type": "float", "material": f["material"], "property": f["property"], "value": f["value"] + 0.5})
    xs = [item_ms()]
    over(C, "MW2_item", xs[0] <= BAR_COMMIT_MS, "item: a commit's pass %.3f ms (bar %.0f ms)" % (xs[0], BAR_COMMIT_MS))

    new_scene(C)
    C.call({"cmd": "map.load", "ref": C.a.map, "wait": True}, timeout=600)
    ms = C.call({"cmd": "map.materials", "props": True, "limit": 400}).get("materials") or []
    slots = [(m["name"], m.get("props")) for m in ms if str(m.get("shader")).startswith("HS2Next/")]
    q = {"cmd": "map.materials.set", "items": many_items(slots, png)}
    C.call(q, timeout=300)

    def map_ms():
        return float(C.call({"cmd": "map.materials.items"})["targets"][0].get("passMs") or 0)
    xs = drag(C, q, map_ms, "map", steps)
    over(C, "MW1_map", statistics.median(xs) <= BAR_STEP_MS and p95(xs) <= BAR_STEP_P95_MS, "map, %s: %s" % (counted(q["items"]), ms_text(xs)), ms=xs)
    new_scene(C)

    key = load_chara(C)
    first = float((C.call({"cmd": "chara.edit.info", "key": key}).get("lastFlush") or {}).get("ms") or 0)
    slots = []
    for part in ("body", "face"):
        r = C.call({"cmd": "chara.edit.materials", "key": key, "part": part})
        slots += [(fmt(m["material"]), m.get("props")) for rd in r.get("renderers") or [] for m in rd.get("slots") or [] if m and m.get("material") and str(m.get("shader")).startswith("HS2Next/")]
    target = "character"
    # one by one: a character refuses what only a replacement shader declares (a rebuild would lose it)
    q = {"cmd": "chara.edit.materials.set", "key": key, "target": target, "items": []}
    for e in many_items(slots, png, cap=600):
        if len(q["items"]) >= 150:
            break
        if C.call(dict({k: v for k, v in q.items() if k != "items"}, **e), timeout=120, check=False).get("ok"):
            q["items"].append(e)
    # the bar is at 150 items and the object's properties give fewer: a file may repeat a key, so the rest goes in as a stored array, which a pass runs
    stored = 0
    if len(q["items"]) < 150:
        path = "chara.materialOverrides"
        array = C.call({"cmd": "chara.edit.get", "key": key, "paths": [path]})["values"][path]
        floats = [x for x in array if x.get("type") == "float"][1:]
        more = (floats * 3)[:150 - len(array)]
        C.call({"cmd": "chara.edit.set", "key": key, "set": {path: array + more}}, timeout=120)
        stored = len(more)
    i0 = C.call({"cmd": "chara.edit.info", "key": key})

    def flush_ms():
        return float((C.call({"cmd": "chara.edit.info", "key": key}).get("lastFlush") or {}).get("ms") or 0)
    xs = drag(C, q, flush_ms, "character", steps)
    i1 = C.call({"cmd": "chara.edit.info", "key": key})
    rebuilds = int(i1.get("editRebuilds") or 0) - int(i0.get("editRebuilds") or 0)
    over(C, "MW1_chara", statistics.median(xs) <= BAR_STEP_MS and p95(xs) <= BAR_STEP_P95_MS and rebuilds == 0, "character object, %s and %d stored floats repeating a key: %s; rebuilds %d" % (counted(q["items"]), stored, ms_text(xs), rebuilds), ms=xs)
    f = first_float(q)
    C.call({"cmd": "chara.edit.materials.set", "key": key, "target": target, "type": "float", "material": f["material"], "property": f["property"], "value": f["value"] + 0.5})
    x = flush_ms()
    over(C, "MW2_chara", x <= BAR_COMMIT_MS, "character: a commit's flush %.3f ms (bar %.0f ms)" % (x, BAR_COMMIT_MS))
    # MW7's bound: the same array built
    path = save(C, "mw7")
    new_scene(C)
    t0 = time.perf_counter()
    load(C, path)
    wall = (time.perf_counter() - t0) * 1000
    key = C.call({"cmd": "scene"})["objects"][0]["key"]
    last = C.call({"cmd": "chara.edit.info", "key": key}).get("lastFlush") or {}
    C.info["mw7_flush"] = last
    build = float(last.get("ms") or 0)
    share = x / build if build > 0 else 0
    over(C, "MW7", x <= BAR_BUILD_MS or share <= BAR_BUILD_SHARE, "a value pass %.3f ms bounds the baseline in a build's flush of %.1f ms (%.2f %%; bars 1 %% or 1 ms); this session's first build flush %.1f ms, scene.load with its frames %.0f ms" % (x, build, share * 100, first, wall))
    new_scene(C)


# ---------------------------------------------------------------- MW5, MW6, MJ3

def mw5(C):
    new_scene(C)
    item = add_item(C, ITEM_MANY)
    slots = [(fmt(s["material"]), s.get("props")) for _, s in item_slots(C, item)]
    edits = [e for e in many_items(slots, os.path.join(C.out, "probe_small.png")) if e["type"] != "texture"][:16]
    C.call({"cmd": "item.materials.set", "key": item, "items": edits}, timeout=120)
    C.call({"cmd": "timeline.build", "duration": 4, "loop": True, "play": False,
            "tracks": [{"type": "color", "key": item, "slot": 0, "keys": [{"t": 0, "v": [1, 1, 1, 1], "ease": "linear"}, {"t": 4, "v": [0.2, 0.4, 0.8, 1]}]}]})
    C.call({"cmd": "timeline.play", "from": 0})
    C.frames(30)
    xs = []
    for _ in range(120):
        C.frames(1)
        xs.append(float(C.call({"cmd": "item.materials.items", "key": item})["targets"][0].get("passMs") or 0))
    C.call({"cmd": "timeline.stop"})
    over(C, "MW5", statistics.median(xs) <= BAR_TRACK_MS, "a colour track on an item with %d edits: the pass of a frame: %s (bar %.2f ms)" % (len(edits), ms_text(xs), BAR_TRACK_MS), ms=xs)
    new_scene(C)


def mw6(C):
    path = os.path.join(C.out, "probe_4096.png")
    png_gradient(path, 4096, 4096)
    new_scene(C)
    item = add_item(C)
    _, s = item_slots(C, item)[0]
    t = time.perf_counter()
    C.call({"cmd": "item.materials.set", "key": item, "type": "texture", "material": fmt(s["material"]), "property": "MainTex", "file": path}, timeout=300)
    ms = (time.perf_counter() - t) * 1000
    C.R.add("MW6", "PASS", "a 4,096 x 4,096 PNG (%.1f MB): the command took %.0f ms" % (os.path.getsize(path) / 1e6, ms), ms=ms)
    os.remove(path)
    new_scene(C)


def mj3(C):
    new_scene(C)
    key = load_chara(C)
    r = C.call({"cmd": "chara.edit.boneMods", "key": key})
    bones = r.get("bones") or []
    if not bones:
        C.R.add("MJ3", "WARN", "no hair bone on the target")
        return
    bone = bones[min(1, len(bones) - 1)]
    xs = []
    for i in range(C.a.steps):
        C.call({"cmd": "chara.edit.boneMods.set", "key": key, "bone": bone, "scale": [1 + 0.001 * i, 1, 1], "merge": True}, timeout=60)
        xs.append(float((C.call({"cmd": "chara.edit.info", "key": key}).get("lastFlush") or {}).get("ms") or 0))
    over(C, "MJ3", statistics.median(xs) <= BAR_HAIR_MS, "a hair bone's drag step on %s: %s (bar %.0f ms)" % (bone, ms_text(xs), BAR_HAIR_MS), ms=xs)
    new_scene(C)


# ---------------------------------------------------------------- report and main

def write_reports(C, exit_code, started):
    R = C.R
    n_pass, n_fail, n_warn = R.count("PASS"), R.count("FAIL"), R.count("WARN")
    summary = "SUMMARY: %d PASS, %d FAIL" % (n_pass, n_fail)
    warns = [x["id"] for x in R.items if x["verdict"] == "WARN"]
    lines = [summary, "WARN: %d%s" % (n_warn, (" (" + ", ".join(warns) + ")") if warns else ""),
             "material_edit_player_check %s; exe %s; port %d; pid %s; exit code %d; %.0f s"
             % (started, C.exe or "(attach)", C.port, C.pid, exit_code, time.perf_counter() - T0)]
    for x in R.items:
        lines.append("%-14s %-4s  %s" % (x["id"], x["verdict"], x["text"]))
    lines.append(summary)
    with open(os.path.join(C.out, "material_edit_player_report.txt"), "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")
    doc = {"started": started, "exe": C.exe, "project": C.project, "out": C.out, "port": C.port, "pid": C.pid, "exitCode": exit_code,
           "seconds": round(time.perf_counter() - T0, 1), "summary": summary, "counts": {"pass": n_pass, "fail": n_fail, "warn": n_warn}, "info": C.info,
           "python": sys.version.split()[0], "checks": R.items}
    with open(os.path.join(C.out, "material_edit_player_check.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(doc, f, ensure_ascii=False, indent=1, default=str)


def main(argv):
    for s in (sys.stdout, sys.stderr):
        try:
            s.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, ValueError):
            pass
    ap = argparse.ArgumentParser(description="Material edits in the HS2Next app player (MV1-MV5, MW1, MW2, MW5-MW7, MJ3, a round trip of every target).")
    ap.add_argument("--exe", help="path to HS2Next.exe (the built app player)")
    ap.add_argument("--project", default=os.path.dirname(os.path.dirname(os.path.abspath(__file__))), help="project root; default: the folder above Tools")
    ap.add_argument("--out", required=True, help="output folder (report, json, player.log, userdata, bridge_out)")
    ap.add_argument("--port", type=int, default=DEFAULT_PORT)
    ap.add_argument("--timeout-min", type=float, default=20.0, help="whole run; past it the started PID is killed")
    ap.add_argument("--ready-timeout-min", type=float, default=12.0)
    ap.add_argument("--only", default="", help="comma list of MV1, MV2, MV3, MV4, MV5, RT, MW1, MW5, MW6, MJ3 (QUIT always runs when the player was started)")
    ap.add_argument("--steps", type=int, default=120)
    ap.add_argument("--editor-list", default="", help="the Editor's list; default Spike\\out\\maker\\material_shaders_editor.json")
    ap.add_argument("--map", default=SCENE_MAP, help="the map of RT and MW1")
    ap.add_argument("--routed-map", default=ROUTED_MAP, help="a map with URP/Lit routed slots (MV3)")
    ap.add_argument("--min-free-gb", type=float, default=8.0)
    ap.add_argument("--ram-wait-min", type=float, default=10.0)
    ap.add_argument("--attach", action="store_true", help="use a player already serving on --port (no start or quit)")
    a = ap.parse_args(argv)
    if not a.attach and not a.exe:
        ap.error("--exe is required (unless --attach)")
    C = Ctx(a)
    started = utc_now()
    C.deadline = time.time() + a.timeout_min * 60
    stop_gate = threading.Event()

    def on_timeout():
        C.aborted = "timed out after %.1f min (--timeout-min); the started player PID %s was killed" % (a.timeout_min, C.pid)
        log(C.aborted)
        kill_player(C)

    def gate_poll():
        while not stop_gate.wait(30):
            try:
                games = running_games()
            except Exception:
                continue
            if games and C.proc is not None:
                C.gated = True
                C.aborted = "aborted by the game gate (%s); the started player PID %s was stopped" % (", ".join(games), C.pid)
                log(C.aborted)
                kill_player(C)
                return

    watchdog = threading.Timer(a.timeout_min * 60, on_timeout)
    watchdog.daemon = True
    watchdog.start()
    gate = threading.Thread(target=gate_poll, daemon=True)
    gate.start()
    exit_code = 0
    os.makedirs(C.out, exist_ok=True)
    try:
        try:
            setup(C)
        except SetupError as e:
            C.R.add("SETUP", "FAIL" if not C.gated else "WARN", str(e))
            exit_code = 3 if C.gated else 2
        except Exception as e:
            C.R.add("SETUP", "FAIL", "%s: %s" % (type(e).__name__, e), traceback=traceback.format_exc(limit=6))
            exit_code = 2
        if exit_code == 0:
            try:
                for cid, fn in (("MV1", mv1), ("MV2", mv2), ("MV3", mv3), ("MV4", mv4), ("MV5", mv5), ("RT", rt), ("MW1", mw1), ("MW5", mw5), ("MW6", mw6), ("MJ3", mj3)):
                    if C.want(cid):
                        guard(C, cid, fn)
            except Abort as e:
                if C.gated:
                    C.R.add("GATE", "WARN", str(e))
                    exit_code = 3
                else:
                    C.R.add("ABORT", "FAIL", "the run stopped: %s" % e)
    finally:
        stop_gate.set()
        watchdog.cancel()
        if C.aborted is None and exit_code == 0:
            try:
                quit_player(C)
            except Exception as e:
                C.R.add("QUIT", "FAIL", "%s: %s" % (type(e).__name__, e))
        kill_player(C)
        try:
            check_log(C)
        except Exception as e:
            C.R.add("LOG", "WARN", "player.log not read: %s" % e)
        if exit_code == 0 and C.R.count("FAIL"):
            exit_code = 1
        write_reports(C, exit_code, started)
        log("SUMMARY: %d PASS, %d FAIL, %d WARN -> exit %d; report %s"
            % (C.R.count("PASS"), C.R.count("FAIL"), C.R.count("WARN"), exit_code, os.path.join(C.out, "material_edit_player_report.txt")))
    return exit_code


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
