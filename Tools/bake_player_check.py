"""
bake_player_check.py - Chara to Item and the Cutter in the built app player (Phase 6.8; the row BakeCheck.player).

Starts the app player itself, several times, and checks over real HTTP from outside the process, in real frames. It never uses the Unity Editor. Python 3.14, standard library only, plus dotnet for the
test plugins. Content: the clothed SFW visual targets of Tools\\targets.json, judged by numbers; members only as MosaicTest's fixtures use them (BR-Chan with the clothes off, HERO107 with his member on), in
memory. No image is opened or shown and none is written: renders stay in the player's memory through the test plugins (Tests\\BakeProbe, Tests\\WetProbe); scenes are saved with the placeholder thumbnail.
No H animation is played. The per-user HS2Next data is never touched: every player runs with -settings, -userData <its folder under --out>, -cacheDir and -logFile.

Usage (the run_tests.ps1 row BakeCheck.player calls the first form):
    python -I Tools\\bake_player_check.py --exe <path to HS2Next.exe> --project <project root> --out <dir> [--port 17889] [--timeout-min 90]
    more options: --only S,L,W,G,V,I or check ids (default all), --measure-frames 400, --rounds 3, --gfx d3d11 | d3d12, --min-free-gb 8, --ram-wait-min 10, --ready-timeout-min 12,
                  --old-dir <the old bakes' folder for BW10> (default <hs2Root>\\UserData\\CharaToItem, read in place, never written: hashed before and after), --no-import (BW10 not run),
                  --baseline-exe <the last build, e.g. Builds\\AppPrev610\\HS2Next.exe> (BW8's frame with no bake and bundle item build against it, sessions O1 and O2;
                  the kept player of session I: I0 and I1)
    -I ignores PYTHONUTF8, so the script sets its own stdout/stderr to UTF-8.

Sessions (one player at a time, each windowed at 1920 x 1080 with -noUi):
  no player  BV9   the new files searched: no switch; the lines that name the mosaic carry no reason
  S  the test plugins and the kit's sample Shapes, its own UserData:
     BA1 (save)  BR-Chan and HERO107 baked whole, BR-Chan's top baked as a part, the whole bake cut by a plane: each bake rendered alone at a fixed view (the SHA-256 of its pixels), the scene saved, its res/
                 entries hashed; BD1 at bake time on both targets, full and face views, every pixel over 8 levels named by renderer
     BD14        every shader name the bakes store, and the marker, section and importer shaders, found by Shader.Find in the player
     BR9 (with)  the sample's rows listed, its torus built, its FK bends it, the scene saved
  L  the test plugins only, S's UserData with its store folder moved away: BA1 (load) the scene opens with no finding, every bake built from res/ alone, each render bit-identical to S's, saved again with the
     same blobs; BR9 (without) the sample's items load as the fallback and save byte-equal
  O1, O2  the last build (--baseline-exe, Builds\\AppPrev68: 6.7's acceptance player) with Tests\\WetProbe's frames-only build, before and after W: BW8's frame with no bake and a bundle item's build,
     over HTTP only, as in W
  W  the test plugins and Shapes, its own UserData in a fresh folder (an earlier run's w is moved aside to w_prev_<time>, kept); nothing else may run: BW1-BW10 (spec 17.3; a time over its bar is WARN; BW7 has a control: a known 256 MB allocation must read at least
     200 MB, else NOT READ), BR9 (again) the torus back, bent as saved, byte-equal. BW10 imports a copy of the old bakes' folder made in W's folder; the originals are hashed before the copy
     and at the end of W (a difference FAILs); without the folder (a public checkout) BW10 is a SKIP line
     Last, BW7C (INFO lines, no bar): three cycles of {BR-Chan baked whole by bake.make, scene.new, one UnloadUnusedAssets + GC}, the private bytes and the managed heap
     after each, and cycle 3 - cycle 1
  G  the test plugins: BD12 Step 0's sample of item rows read in the player, every row with a mesh the player cannot read cut by a plane 1 unit under its bounds (a bake of the item through the GPU
     route; 0 renderers cut, else the case FAILs), drawn as the item
  V  the test plugins, illusion.hs2#10: BV6 a bake's members in a capture, a screenshot and a recorded frame (the async path), the scene thumbnail and a puddle's mirror (a direction's distance
     max(10, (c.y - plane + 1) / (-d.y)) units, at most 30): the mask at least 1,000 px in each, or FAIL; the paths equal to a capture at 0 px, the wide method off and on; the block against F; the positive control: the members drawn flat with the mosaic left out differ from
     the mosaic render in the members' pixels
  I0, I1  the kept player (--baseline-exe, the build before 6.10's speed fixes) with Tests\\BakeProbe built against its own Managed folder: I0 saves the fixtures s0F (BR-Chan and HERO107
     standing still, paused) and s0F2 (s0F's bakes: BR-Chan whole, HERO107 whole, BR-Chan's top; the whole bake cut by a plane at the waist) and renders the four alone; I1, a second process, bakes
     s0F again and loads s0F2 (the kept player repeats itself: YI1b's and YI3's Present)
  I  this build: YI1a one process, BR-Chan and HERO107 paused with physics off, each baked whole and BR-Chan's top through the new path and then the old
     (BakeJob.Legacy): every data entry's blob, the data set and the main with id and created fixed equal (Present: the old path's level-by-level waits >= 1,000 for BR-Chan, the new path's 0; data
     blobs >= 149, 79, 12); YI1b s0F baked here: the texture blobs and the meshes of renderers with no blend shape over 0 (pose.blendShapes at bake time; the main's renderers name the meshes) equal
     to the kept player's; YI2 s0F2's pieces run again from their stored source 5 rounds: the stored main SHA-256 each round (Present: data blobs >= 130 and 69, new mesh blobs >= 10); YI3 s0F2
     loaded with the new load path and with the old: the four rendered alone equal to the kept player's (Present: footprint >= 80,000 px, rendered twice 0 px, the kept player's two processes
     equal). Without the kept player YI1b, YI2, YI3 are WARN "not compared". I0, I1 and I start in fresh folders as W does
  W's stages (INFO lines, no bar): YB BW1's (read-back, texture builds, the encoders' wait), YR BW4's (gather, inflate, uploads, the workers), YU BW5's (the source's decode, the pieces' encoding)

Outputs:
  <out>\\bake_player_report.txt  first and last line "SUMMARY: N PASS, M FAIL", second line "WARN: K (ids)", then one line per check
  <out>\\bake_player_check.json  details of every check
  <out>\\<s|l|w|g|v|i0|i1|i>\\player.log, userdata\\ (the saves and the stores)
Exit code: 0 every check passed (WARN allowed), 1 a check failed, 2 setup error, 3 aborted by the game gate. A time over its bar is WARN: the lead decides.
Port: 17889 (17899 = BakeTest.play, 17322 = interactive default). Always 127.0.0.1, never localhost.
"""
import argparse
import glob
import hashlib
import json
import math
import os
import re
import shutil
import statistics
import subprocess
import sys
import threading
import time
import traceback
import types
import zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)        # python -I does not add the script's folder
from constraint_player_check import (Abort, Results, SetupError, free_gb, game_processes, guard, http, log, port_listeners, running_games, target_rel, CREATE_NO_WINDOW, T0)  # noqa: E402
from wet_player_check import Session, build_probe, check_log, kill, mirror_on, new_map, stop, two, utc_now  # noqa: E402

# bars of spec 17.3 (ms, bytes, ms of a frame); over a bar: WARN
BAR = {"BW1": 3000.0, "BW1b": 20000.0, "BW1bFrame": 5.0, "BW2": 500.0, "BW3": 0.2, "BW4": 3000.0, "BW4g": 200.0, "BW4s": 50.0, "BW5": 100.0, "BW5i": 10.0, "BW5r": 3000.0,
       "BW6": 120 << 20, "BW6g": 10 << 20, "BW7": 1 << 30, "BW7control": 200 << 20, "BW8": 5.0, "BW8frame": 0.05, "BW8item": 0.1, "BW9": 5.0, "BW10": 60.0}
# BW8: the code read for what a scene with no bake subscribes to (no reader exists; the lead, 2026-11-08)
BW8_INSPECTED = ("Runtime/Scene/Runtime/Items/ItemMotion.cs:82 (BakeProvider.Register: two delegates and CutMarkers.Install, no event), "
                 "Runtime/Scene/Bake/BakeProvider.cs:552-557, Runtime/Scene/Cut/CutMarkers.cs:32-37 (delegates set once at start), "
                 "Runtime/Rendering/Capture/CaptureService.cs:210 (a scope inside each capture), Runtime/Scene/Runtime/ItemRealization.cs:152 "
                 "(GeneratedRowRebuilds only for an item of a generated group); no event subscription (+=) in Runtime/Scene/Bake, Runtime/Scene/Cut, "
                 "GeneratedRows.cs, PluginItemRows.cs, StudioOps.Bake.cs, BakeCommands.cs, CutCommands.cs")
BD1_OVER2, BD1_OVER8, BD1_FOOT = 0.2, 0.01, 100000      # % of the footprint, px (the lead's bars, spec 17.2)
BD12_FOOT = 2000
SHOT_FOOT = 2000                                           # BA1: a bake's render with fewer pixels than this has judged nothing
BV6_MASK_MIN = 1000
BD14_NAMED = ["HS2Next/Standard", "HS2Next/Standard (Specular setup)", "HS2Next/AIT Clothes True", "HS2Next/Mosaic", "HS2Next/Unlit/Color", "HS2Next/Legacy Shaders/Particles/Alpha Blended"]
BD14_LEAST = 10
CLOCK = "2026-11-08T00:00:00Z"
ROW_IDLE = {"group": 19, "category": 0, "no": 0, "time": 0.3, "speed": 0.0}   # the idle row standing still, as BakeTest
PROBES = (("stp3.test.bakeprobe", os.path.join("Tests", "BakeProbe"), "BakeProbe.csproj"), ("stp3.test.wetprobe", os.path.join("Tests", "WetProbe"), "WetProbe.csproj"))
SHAPES = ("stp3.sample.shapes", os.path.join("Samples", "PluginKit", "Shapes"), "Stp3.SampleShapes.dll")
SHAPES_GROUP, SHAPES_ROWS = 820001, ["Cube", "Sphere", "Cylinder", "Torus", "Arrow"]
APP_GROUP = 731406
CHAIR = "illusion.hs2#62"
STEP0_ITEMS = ["hooh.map.city.downtown#65", "[Fouetty]Street Props Pack#131"]
HOME_MAP = 10
WAIST = 8.5                 # a plane at the waist: BR-Chan stands about 16 units tall
V_DISTANCES = (10.0, 7.0, 5.0)
V_MIRROR_CAP = 30.0   # BV6's mirror: a direction's farthest distance (the lead's rule after the wiring run dbgGV2)
THUMB_W, THUMB_H = 504, 704


def bad_reply(r):
    return json.dumps(r, ensure_ascii=False)[:240]


def pct(n, d):
    return 100.0 * n / d if d else 100.0


def med(v):
    return statistics.median(v) if v else float("nan")


# ---------------------------------------------------------------- plugins

def build_plugins(a):
    """The test plugins against the player's own Managed folder; the kit's Shapes through Tools\\plugins.ps1 against the same folder.
    Two plugin folders: <out>\\plugins (the probes) and <out>\\plugins_shapes (the probes and Shapes)."""
    managed = os.path.join(os.path.dirname(a.exe), "HS2Next_Data", "Managed")
    probes = os.path.join(a.out, "plugins")
    with_shapes = os.path.join(a.out, "plugins_shapes")
    for guid, rel, csproj in PROBES:
        target = os.path.join(probes, guid)
        os.makedirs(target, exist_ok=True)
        obj = os.path.join(a.out, "probe_obj", guid) + os.sep
        cmd = ["dotnet", "build", os.path.join(a.project, rel, csproj), "-c", "Release", "-nologo", "-v", "q", "-o", target, "-p:PlayerManaged=" + managed, "-p:BaseIntermediateOutputPath=" + obj]
        p = subprocess.run(cmd, capture_output=True, timeout=300, creationflags=CREATE_NO_WINDOW)
        if p.returncode != 0:
            raise SetupError("the test plugin %s did not build: %s" % (guid, p.stdout.decode("utf-8", "replace")[-600:]))
        shutil.copyfile(os.path.join(a.project, rel, "manifest.json"), os.path.join(target, "manifest.json"))
        shutil.copytree(target, os.path.join(with_shapes, guid), dirs_exist_ok=True)
    tool = os.path.join(a.project, "Tools", "plugins.ps1")
    exe = shutil.which("pwsh") or shutil.which("powershell") or "powershell.exe"
    p = subprocess.run([exe, "-NoProfile", "-NonInteractive", "-File", tool, "-Build", os.path.join(a.project, SHAPES[1]), "-ApiDir", managed], capture_output=True, timeout=600, creationflags=CREATE_NO_WINDOW)
    if p.returncode != 0:
        raise SetupError("the sample Shapes did not build: %s" % (p.stdout + p.stderr).decode("utf-8", "replace")[-600:])
    built = os.path.join(a.project, SHAPES[1], "bin", "Release", "netstandard2.1", SHAPES[2])
    target = os.path.join(with_shapes, SHAPES[0])
    os.makedirs(target, exist_ok=True)
    shutil.copyfile(built, os.path.join(target, SHAPES[2]))
    shutil.copyfile(os.path.join(a.project, SHAPES[1], "manifest.json"), os.path.join(target, "manifest.json"))
    return probes, with_shapes


# ---------------------------------------------------------------- sessions

class Run(Session):
    """A session with its own plugin folder and UserData folder (L opens S's UserData); O1 and O2 run the baseline player."""
    def __init__(self, a, R, letter, plugins, ud=None, exe=None):
        super().__init__(a, R, letter, a.port, exe)
        self.plugins = plugins
        self.ud = ud or os.path.join(self.out, "userdata")


def start(C, deadline):
    a = C.a
    os.makedirs(C.out, exist_ok=True)
    busy = port_listeners(C.port)
    if busy:
        raise SetupError("port %d is already in use (PID %s)" % (C.port, ", ".join(map(str, sorted(busy)))))
    settings = os.path.join(C.project, "Config", "settings.local.json")
    if not os.path.isfile(settings):
        raise SetupError("settings file not found: %s" % settings)
    bo, logf = os.path.join(C.out, "bridge_out"), os.path.join(C.out, "player.log")
    ep = os.path.join(C.ud, "Bridge", "endpoint.json")
    if os.path.isfile(ep):
        os.remove(ep)
    env = dict(os.environ)
    env["HS2NEXT_PRIVATE_OFF"] = "1"
    env.pop("HS2NEXT_DATA_ROOT", None)
    env.pop("HS2NEXT_NO_PLUGINS", None)
    si = subprocess.STARTUPINFO()
    si.dwFlags |= subprocess.STARTF_USESHOWWINDOW
    si.wShowWindow = 4   # SW_SHOWNOACTIVATE
    args = [C.exe, "-noUi", "-screen-width", "1920", "-screen-height", "1080", "-screen-fullscreen", "0", "-window-mode", "windowed", "-pluginsDir", C.plugins]
    if a.gfx:
        args.append("-force-" + a.gfx)
    args += ["-bridgePort", str(C.port), "-bridgeAllowQuit", "-settings", settings, "-cacheDir", os.path.join(C.project, "Cache"), "-userData", C.ud, "-bridgeOutputDir", bo, "-logFile", logf]
    C.info["commandLine"] = args
    log("session %s: starting the player: %s" % (C.letter, subprocess.list2cmdline(args)))
    t0 = time.time()
    C.proc = subprocess.Popen(args, cwd=os.path.dirname(C.exe), env=env, stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, creationflags=CREATE_NO_WINDOW, startupinfo=si)
    C.pid = C.proc.pid
    limit = min(deadline, t0 + a.ready_timeout_min * 60)
    while True:
        if C.proc.poll() is not None:
            raise SetupError("the player exited with code %s before it was ready (see %s)" % (C.proc.returncode, logf))
        if C.aborted:
            raise SetupError("before the player was ready: " + C.aborted)
        try:
            if C.br.call({"cmd": "ping"}, timeout=5, check=False).get("appReady"):
                break
        except (OSError, http.client.HTTPException, ValueError, RuntimeError):
            pass
        if time.time() > limit:
            raise SetupError("not ready after %.0f s" % (time.time() - t0))
        time.sleep(0.5)
    ready = time.time() - t0
    with open(os.path.join(C.project, "Tools", "targets.json"), encoding="utf-8") as f:
        C.targets = json.load(f)
    p = C.call({"cmd": "ping"})
    C.output_root = C.call({"cmd": "app.paths"}).get("outputDir")
    C.R.add("SETUP" + C.letter, "PASS", "session %s ready in %.1f s (pid %s, port %d); api %s; plugins %s" % (C.letter, ready, C.pid, C.port, p.get("api"), os.path.basename(C.plugins)), readySeconds=round(ready, 2))


# ---------------------------------------------------------------- helpers

def probe(C, cmd, timeout=650, **kw):
    return C.call(dict(kw, cmd="bakeprobe." + cmd), timeout=timeout)


def key_of(r):
    return r.get("key") or ((r.get("added") or [{}])[0]).get("key")


def load_chara(C, sex, at=None):
    """A clothed target standing still in the idle row, the blink off: its key."""
    key = key_of(C.call({"cmd": "chara.load", "path": target_rel(C, sex), "sex": sex}, timeout=300))
    C.call(dict(ROW_IDLE, cmd="chara.anime", key=key))
    C.call({"cmd": "chara.face", "key": key, "blink": False})
    if at:
        C.call({"cmd": "obj.transform", "key": key, "pos": at})
    C.frames(5)
    return key


def bake(C, key, **kw):
    r = C.call(dict(kw, cmd="bake.make", key=key), timeout=300)
    return r["items"][0], r["bakes"][0], r


def scene_save(C, name):
    return C.call({"cmd": "scene.save", "name": name, "overwrite": True, "thumb": "placeholder", "clock": CLOCK}, timeout=600)


def entries_of(path):
    """{entry name: SHA-256 of its bytes} of a saved scene, and the bytes of scene.json."""
    out = {}
    with zipfile.ZipFile(path) as z:
        for n in z.namelist():
            out[n] = hashlib.sha256(z.read(n)).hexdigest()
        doc = z.read("scene.json")
    return out, doc


def res_bins(entries):
    return {n: h for n, h in entries.items() if n.startswith("res/") and n.endswith(".bin")}


def view_args(v):
    return {"pivot": v["pivot"], "rot": v["rot"], "dist": v["dist"], "fov": v["fov"], "width": v["width"], "height": v["height"]}


def look(C, cid, label, a, b, view, judge_foot=BD1_FOOT):
    """BD1's comparison of a source and a bake through the probe; the verdict lines."""
    r = probe(C, "look", a=a, b=b, view=view, timeout=900)
    foot = r["footprint"]
    o2, o8 = pct(r["over2"], foot), pct(r["over8"], foot)
    over8 = [x for x in r["byRenderer"] if x["over8"] > 0]
    unnamed8 = sum(x["over8"] for x in over8 if x["name"] == "(no renderer)")
    present = foot >= judge_foot and r["noise"] == 0
    ok = present and o2 <= BD1_OVER2 and o8 <= BD1_OVER8 and unnamed8 == 0
    C.R.add(cid, "PASS" if ok else "FAIL", "%s: footprint %d px (bake %d, at least %d), the source twice %d px; differ %d, over 2 levels %d (%.4f %%, bar %.2f %%), over 8 %d (%.4f %%, bar %.2f %%), max %d; over 8 by renderer: %s"
            % (label, foot, r["footprintB"], judge_foot, r["noise"], r["differ"], r["over2"], o2, BD1_OVER2, r["over8"], o8, BD1_OVER8, r["max"],
               ", ".join("%s %d" % (x["name"], x["over8"]) for x in over8) or "none"), raw=r)
    return r


def frame_ms(C, n):
    C.frames(20)
    C.call({"cmd": "app.perf", "reset": True})
    C.frames(n)
    r = C.call({"cmd": "app.perf"})
    return r["frameMs"]["median"], r["frameMs"]["p95"], r["frames"]


def others_running():
    """Unity.exe and HS2Next.exe processes on the machine (the timing session must run alone)."""
    p = subprocess.run(["tasklist", "/FO", "CSV", "/NH"], capture_output=True, timeout=60, creationflags=CREATE_NO_WINDOW)
    names = []
    for line in p.stdout.decode("oem", "replace").splitlines():
        t = [x.strip('"') for x in line.split('","')]
        if t and t[0].lower() in ("unity.exe", "hs2next.exe"):
            names.append("%s %s" % (t[0], t[1] if len(t) > 1 else "?"))
    return names


# ---------------------------------------------------------------- session S: BA1 (save), BD1, BD14, BR9 (with)

def ba1_save(C):
    a = C.a
    C.call({"cmd": "scene.new"})
    C.call({"cmd": "scene.pause", "paused": True})
    br = load_chara(C, "female")
    hero = load_chara(C, "male", [14, 0, 0])
    C.frames(10)
    whole, wb, _ = bake(C, br, store=True, hide=True, name="ba1 whole")
    whole2, wb2, _ = bake(C, hero, store=True, hide=True, name="ba1 whole 2")
    C.frames(3)
    for src, item, who in ((br, whole, "BR-Chan"), (hero, whole2, "HERO107")):
        for v in ("full", "face"):
            look(C, "BD1", "%s %s, at bake time" % (who, v), src, item, v)
    part, pb, _ = bake(C, br, parts=["clothes0"], store=True, hide=True, name="ba1 top")
    wi = probe(C, "item", key=whole)
    pos = wi["position"]
    cut = C.call({"cmd": "cut.make", "key": whole, "shape": {"kind": "plane", "pos": [pos[0], pos[1] + WAIST, pos[2]], "rot": [0, 0, 0]}, "mode": "both", "hide": True, "name": "ba1 piece"}, timeout=300)
    pieces = cut["pieces"]
    C.frames(5)
    items = {"whole": whole, "whole2": whole2, "part": part}
    for i, k in enumerate(pieces):
        items["piece%d" % i] = k
    # the views: whole and pieces at the Studio's reset view of the whole bake, the part close to its centre; the same numbers in session L
    full = probe(C, "shot", key=whole, view="full")["view"]
    full2 = probe(C, "shot", key=whole2, view="full")["view"]
    pi = probe(C, "item", key=part)
    pp = pi["position"]
    part_view = {"pivot": pp, "rot": [15.0, 180.0 + pi["euler"][1], 0.0], "dist": 12.0, "fov": 23.0, "width": 1280, "height": 720}
    views = {"whole": full, "whole2": full2, "part": part_view}
    for k in items:
        if k.startswith("piece"):
            views[k] = full
    shots, status, shaders = {}, {}, set()
    for name, k in items.items():
        s = probe(C, "shot", key=k, **view_args(views[name]))
        shots[name] = s
        status[name] = C.call({"cmd": "bake.status", "key": k})
        it = probe(C, "item", key=k)
        shaders |= set((it.get("prototype") or {}).get("shaders") or [])
        ok = s["footprint"] >= SHOT_FOOT and s["twice"] == 0 and it.get("source") == "bake" and not it.get("fallback")
        C.R.add("BA1", "PASS" if ok else "FAIL", "S: %s (%s) drawn alone at its view: footprint %d px (at least %d), rendered twice %d px, source %s, fallback %s; sha %s"
                % (name, k, s["footprint"], SHOT_FOOT, s["twice"], it.get("source"), it.get("fallback"), s["sha"][:16]))
    # BD14: the shaders the bakes name, and the marker, section and importer shaders
    names = sorted(shaders | set(BD14_NAMED))
    rows = probe(C, "find", names=names)["rows"]
    missing = [x["name"] for x in rows if not x["found"]]
    unsupported = [x["name"] for x in rows if x["found"] and not x["supported"]]
    C.R.add("BD14", "PASS" if not missing and not unsupported and len(names) >= BD14_LEAST else "FAIL", "Shader.Find in the player: %d names (at least %d; %d from the bakes, %d named), not found %s, not supported %s"
            % (len(names), BD14_LEAST, len(shaders), len(BD14_NAMED), missing or "none", unsupported or "none"), names=names)
    r = scene_save(C, "ba1")
    path = r["path"]
    entries, _ = entries_of(path)
    bins = res_bins(entries)
    keyed = all(n[len("res/"):-len(".bin")] == h for n, h in bins.items())
    C.R.add("BA1", "PASS" if bins and keyed and len(pieces) == 2 else "FAIL", "S: a scene with a whole bake (two targets), a part bake and %d pieces saved: %s, %d bytes, %d res/ blobs (each named by its content's hash: %s)"
            % (len(pieces), os.path.basename(path), r.get("bytes", 0), len(bins), keyed))
    a.store["ba1"] = {"path": path, "items": items, "views": views, "shots": shots, "status": status, "bins": bins, "bakes": [wb, wb2, pb]}
    C.call({"cmd": "scene.pause", "paused": False})


def br9_with(C):
    a = C.a
    C.call({"cmd": "scene.new"})
    pl = C.call({"cmd": "plugin.list"})
    e = next((x for x in pl.get("plugins") or [] if str(x.get("guid")).lower() == SHAPES[0]), {})
    rows = C.call({"cmd": "item.search", "group": SHAPES_GROUP, "limit": 100})["items"]
    names = [x["name"] for x in rows]
    keys = {}
    for i in range(1, 6):
        keys[i] = key_of(C.call({"cmd": "item.add", "ref": "%s#%d" % (SHAPES[0], i)}))
    C.frames(3)
    built = {i: probe(C, "item", key=k) for i, k in keys.items()}
    torus = keys[4]
    t = built[4]
    ok = e.get("state") == "loaded" and names == SHAPES_ROWS and all(b.get("source") == "generated" and not b.get("fallback") for b in built.values()) and t.get("skinnedBones") == 8 and len(t.get("fk") or []) == 8
    C.R.add("BR9", "PASS" if ok else "FAIL", "with the sample: state %s, item.search {group: %d} %s; the five items built from their rows: %s; the torus skinned on %s bones, FK bones %s"
            % (e.get("state"), SHAPES_GROUP, names, [b.get("source") for b in built.values()], t.get("skinnedBones"), t.get("fk")))
    probe(C, "verts", key=torus, keep=True)
    C.call({"cmd": "item.set", "key": torus, "set": {"fk.enabled": True, "fk.bones[\"ring2\"]": [0, -45, 40], "fk.bones[\"ring5\"]": [-30, -45, 0]}})
    C.frames(3)
    v = probe(C, "verts", key=torus)
    moved = v.get("moved", 0)
    ring2 = (v.get("bones") or {}).get("ring2")
    bent = ring2 is not None and math.dist(ring2, [0, 315, 40]) < 1e-2
    C.R.add("BR9", "PASS" if moved >= 1 and bent else "FAIL", "its FK bends it in the player: the largest vertex move %.3f units over %d vertices (at least 1), ring2 local %s" % (moved, v.get("vertices", 0), ring2))
    r = scene_save(C, "br9")
    a.store["br9"] = {"path": r["path"], "keys": keys}
    with open(r["path"], "rb") as f:
        a.store["br9"]["bytes"] = f.read()


def run_s(C):
    probe(C, "frames", vsync=0, fps=-1)
    guard(C, "BA1", ba1_save)
    guard(C, "BR9", br9_with)


# ---------------------------------------------------------------- session L: BA1 (load), BR9 (without)

def ba1_load(C):
    a = C.a
    s = a.store.get("ba1")
    if not s:
        C.R.add("BA1", "FAIL", "L: session S left no scene")
        return
    store = os.path.join(C.ud, "bakes")
    away = os.path.isdir(a.store.get("away") or "")
    r = C.call({"cmd": "scene.load", "path": s["path"]}, timeout=600)
    C.call({"cmd": "scene.pause", "paused": True})
    C.frames(10)
    findings = r.get("findings") or []
    C.R.add("BA1", "PASS" if not findings and away and not os.path.isdir(store) else "FAIL", "L: a fresh process opens the scene with the store folder moved away (moved %s, absent %s): %d findings%s"
            % (away, not os.path.isdir(store), len(findings), (": " + "; ".join("%s %s" % (f.get("code"), f.get("path")) for f in findings[:6])) if findings else ""))
    bad, differ = [], []
    for name, k in s["items"].items():
        it = probe(C, "item", key=k)
        if it.get("source") != "bake" or it.get("fallback") or it.get("state") != "Built" or not it.get("prototype"):
            bad.append("%s: source %s, fallback %s, state %s" % (name, it.get("source"), it.get("fallback"), it.get("state")))
        sh = probe(C, "shot", key=k, **view_args(s["views"][name]))
        if sh["sha"] != s["shots"][name]["sha"]:
            differ.append("%s: %s against %s, footprint %d against %d" % (name, sh["sha"][:16], s["shots"][name]["sha"][:16], sh["footprint"], s["shots"][name]["footprint"]))
        st = C.call({"cmd": "bake.status", "key": k})
        if st.get("main") != s["status"][name].get("main") or st.get("data") != s["status"][name].get("data"):
            bad.append("%s: bake.status main or data differ" % name)
    C.R.add("BA1", "PASS" if not bad and not os.path.isdir(store) else "FAIL", "L: every bake built from res/ alone (source bake, not the fallback, built; the store folder still absent: %s): %d items; %s"
            % (not os.path.isdir(store), len(s["items"]), "; ".join(bad) or "all"))
    C.R.add("BA1", "PASS" if not differ else "FAIL", "L: each bake rendered alone at its view of S, bit for bit: %d of %d equal%s" % (len(s["items"]) - len(differ), len(s["items"]), ("; differ " + "; ".join(differ)) if differ else ""))
    r2 = scene_save(C, "ba1_again")
    e2, _ = entries_of(r2["path"])
    b2 = res_bins(e2)
    same = b2 == s["bins"]
    C.R.add("BA1", "PASS" if same else "FAIL", "L: saved again: %d res/ blobs, hashes equal to S's: %s (only in S %d, only now %d)"
            % (len(b2), same, len(set(s["bins"]) - set(b2)), len(set(b2) - set(s["bins"]))))
    C.call({"cmd": "scene.pause", "paused": False})


def br9_without(C):
    a = C.a
    s = a.store.get("br9")
    if not s:
        C.R.add("BR9", "FAIL", "without the sample: session S left no scene")
        return
    C.call({"cmd": "scene.load", "path": s["path"]}, timeout=600)
    C.frames(5)
    fb = [probe(C, "item", key=k).get("fallback") for k in s["keys"].values()]
    r = scene_save(C, "br9_without")
    with open(r["path"], "rb") as f:
        b = f.read()
    a.store["br9"]["without"] = r["path"]
    C.R.add("BR9", "PASS" if all(fb) and b == s["bytes"] else "FAIL", "without the sample: the five items load as the fallback %s; saved again byte-equal %s (%d bytes)" % (fb, b == s["bytes"], len(b)))


def run_l(C):
    probe(C, "frames", vsync=0, fps=-1)
    guard(C, "BA1", ba1_load)
    guard(C, "BR9", br9_without)


# ---------------------------------------------------------------- session W: BW1-BW10, BR9 (again)

def http_measure(C, label):
    """BW8's numbers over HTTP only, the same in this player and the last one: the frame of a scene with no bake (map illusion.hs2#10, BR-Chan and HERO107
    standing still, the frame cap off through Tests\\WetProbe) in rounds, and a bundle item's build (item.add of illusion.hs2#62 and a frame after it, 10 times)."""
    a = C.a
    C.call({"cmd": "wetprobe.frames", "vsync": 0, "fps": -1})
    new_map(C, no=HOME_MAP)
    two(C)
    C.frames(60)
    frames = [frame_ms(C, a.measure_frames)[0] for _ in range(a.rounds)]
    items, keys = [], []
    for _ in range(10):
        t0 = time.perf_counter()
        r = C.call({"cmd": "item.add", "ref": CHAIR})
        C.call({"cmd": "ping", "waitFrames": 1})
        items.append((time.perf_counter() - t0) * 1000)
        keys.append(key_of(r))
    C.call({"cmd": "delete", "keys": keys})
    C.a.store.setdefault("bw8", {})[label] = {"frames": frames, "items": items}
    C.R.add("BW8" + label, "PASS", "%s over HTTP: the frame with no bake %s ms (median %.3f); a bundle item's build with a frame after it %s ms (median %.2f)"
            % (label, ", ".join("%.3f" % x for x in frames), med(frames), ", ".join("%.1f" % x for x in items), med(items)))


def bw8_compare(a, R):
    """The frame with no bake and a bundle item's build against the last build (the lead's bars: +0.05 ms; max(0.1 ms, 2 x the two old runs' difference))."""
    d = a.store.get("bw8") or {}
    if "W" not in d or "O1" not in d or "O2" not in d:
        R.add("BW8", "WARN", "no comparison with the last build: measured %s (--baseline-exe %s)" % (sorted(d), a.baseline_exe))
        return
    old_f = d["O1"]["frames"] + d["O2"]["frames"]
    df = med(d["W"]["frames"]) - med(old_f)
    R.add("BW8", "PASS" if df <= BAR["BW8frame"] else "WARN", "a frame with no bake: this player %.3f ms (%s), the last build %.3f ms (before %s, after %s): %+.3f ms. Bar +%.2f"
          % (med(d["W"]["frames"]), d["W"]["frames"], med(old_f), d["O1"]["frames"], d["O2"]["frames"], df, BAR["BW8frame"]))
    o1, o2 = med(d["O1"]["items"]), med(d["O2"]["items"])
    bar = max(BAR["BW8item"], 2 * abs(o1 - o2))
    di = med(d["W"]["items"]) - med(d["O1"]["items"] + d["O2"]["items"])
    R.add("BW8", "PASS" if di <= bar else "WARN", "a bundle item's build (%s, over HTTP with a frame): this player %.2f ms, the last build %.2f ms (runs %.2f and %.2f): %+.2f ms. Bar max(0.1, 2 x %.2f) = %.2f"
          % (CHAIR, med(d["W"]["items"]), med(d["O1"]["items"] + d["O2"]["items"]), o1, o2, di, abs(o1 - o2), bar))


def bw8(C):
    """A scene with no bake, no cut and no plugin's generated row: the runtime's parts of this phase, the file's members and entries; the app's group; then the HTTP numbers."""
    C.call({"cmd": "scene.new"})
    load_chara(C, "female")
    C.call({"cmd": "item.add", "ref": CHAIR})
    C.frames(5)
    parts = probe(C, "parts")
    r = scene_save(C, "bw8")
    entries, doc = entries_of(r["path"])
    text = doc.decode("utf-8")
    members = re.findall(r'"(bake|cut)"\s*:', text)
    bins = [n for n in entries if n.endswith(".bin")]
    ok = not parts["bakeParts"] and not parts["bakeCache"] and not members and not bins
    C.R.add("BW8", "PASS" if ok else "FAIL", "a scene with no bake, no cut, no generated row: runtime parts of this phase %s, the bake cache %s; bake or cut members in scene.json %d, bin entries %d"
            % (parts["bakeParts"] or "none", parts["bakeCache"], len(members), len(bins)))
    C.R.add("BW8", "PASS", "subscriptions: not measured (no reader), code inspected: " + BW8_INSPECTED)
    g = probe(C, "appgroup", rounds=7)
    cost = g["refreshMedian"] + g["shapeMedian"]
    C.R.add("BW8", "PASS" if cost <= BAR["BW8"] else "WARN", "the start-up cost of the app's group: its store rows listed again %.3f ms (%d rows) + a group of its shape registered and disposed (two categories, four rows) %.3f ms = %.3f ms; registered %s. Bar %.0f"
            % (g["refreshMedian"], g["storeRows"], g["shapeMedian"], cost, g["registered"], BAR["BW8"]), raw=g)
    http_measure(C, "W")

def bw9(C):
    t = probe(C, "rows", n=1000, rounds=7)
    worst = max(t["appendMedian"], t["replaceMedian"])
    C.R.add("BW9", "PASS" if worst <= BAR["BW9"] else "WARN", "rows through IItemRows: append %d rows %.3f ms, replace them %.3f ms (medians of %d: %s; %s). Bar %.0f"
            % (t["n"], t["appendMedian"], t["replaceMedian"], len(t["append"]), t["append"], t["replace"], BAR["BW9"]))


def bw1(C, key):
    """BW1, BW1b, BW7: the meters' control, a whole character captured on the main thread, the managed heap's peak (the bar) and the private bytes' (printed; also after the job's disposal and after one UnloadUnusedAssets + GC)
    during it, the encoding on a worker and the frame during it."""
    a = C.a
    ctl = probe(C, "heapcontrol", mb=256)
    base = [frame_ms(C, a.measure_frames)[0] for _ in range(a.rounds)]
    cap = probe(C, "capture", key=key, timeout=600)
    C.call({"cmd": "app.perf", "reset": True})
    end = time.time() + 120
    enc = None
    while time.time() < end:
        C.frames(15)
        enc = probe(C, "encoded")
        if enc.get("done"):
            break
    during = C.call({"cmd": "app.perf"})
    after_dispose = probe(C, "release").get("private", -1)
    probe(C, "unload", start=True)
    after_unload = None
    end = time.time() + 120
    while time.time() < end:
        C.frames(2)
        u = probe(C, "unload")
        if u.get("done"):
            after_unload = u.get("private", -1)
            break
    heap = cap["heapPeak"] - cap["heapBefore"]
    priv = cap["privatePeak"] - cap["privateBefore"]
    C.R.add("BW1", "PASS" if cap["captureMs"] <= BAR["BW1"] else "WARN", "a whole character (BR-Chan) captured on the main thread: %.0f ms (%s); %d renderers, %d textures, %d meshes. Bar %.0f"
            % (cap["captureMs"], ", ".join("%s %.0f" % (k, v) for k, v in sorted(cap["split"].items())), cap["renderers"], cap["textures"], cap["meshes"], BAR["BW1"]), raw=cap)
    yb(C, cap)
    if not enc or not enc.get("done") or enc.get("faulted"):
        C.R.add("BW1b", "FAIL", "the encoding on a worker did not end in 120 s or faulted: %s" % bad_reply(enc or {}))
    else:
        fm = during["frameMs"]["median"]
        d = fm - med(base)
        ok = enc["ms"] <= BAR["BW1b"] and d <= BAR["BW1bFrame"]
        C.R.add("BW1b", "PASS" if ok else "WARN", "the encoding on a worker after the capture: %.0f ms (bar %.0f), %d blobs, %.1f MiB; the frame during it: median %.3f ms over %d frames against %.3f ms before (%s): %+.3f (bar +%.0f)"
                % (enc["ms"], BAR["BW1b"], enc.get("blobs", 0), enc.get("bytes", 0) / 1048576.0, fm, during.get("frames", 0), med(base), ", ".join("%.3f" % x for x in base), d, BAR["BW1bFrame"]))
    control = "the control: %d MiB allocated read %.1f MiB on the managed meter (at least %.0f), %.1f MiB on the private bytes, %d samples" % (
        ctl["allocated"] >> 20, ctl["heapRead"] / 1048576.0, BAR["BW7control"] / 1048576.0, ctl["privateRead"] / 1048576.0, ctl["samples"])
    def over(v):
        return "not read" if v is None or v < 0 else "%+.1f MiB (%.1f MiB)" % ((v - cap["privateBefore"]) / 1048576.0, v / 1048576.0)
    after = "after the encoding ended and the job was disposed %s, after one Resources.UnloadUnusedAssets and a GC %s" % (
        over(after_dispose), over(after_unload) if after_unload is not None else "not read (the unload did not end in 120 s)")
    line = "the capture's peak managed heap: %.1f MiB over the %.1f MiB before it (bar %.0f MiB); the private bytes' peak %.1f MiB over the %.1f MiB before it, %s (printed); %d samples; textures in flight at most %.1f MiB, texels %.0f MiB, the largest %.1f MiB; %s" % (
        heap / 1048576.0, cap["heapBefore"] / 1048576.0, BAR["BW7"] / 1048576.0, priv / 1048576.0, cap["privateBefore"] / 1048576.0, after, cap["heapSamples"], cap["peakInFlight"] / 1048576.0,
        cap["textureBytes"] / 1048576.0, cap["largestTexture"] / 1048576.0, control)
    if ctl["heapRead"] < BAR["BW7control"]:
        C.R.add("BW7", "FAIL", "NOT READ (the meter missed its control): " + line, control=ctl)
    else:
        C.R.add("BW7", "PASS" if heap <= BAR["BW7"] else "WARN", line, control=ctl)

def yb(C, cap):
    """YB: BW1's stages (printed, no bar): the read-back, the texture builds, the wait for the encoders, the rest of the materials, the meshes, the nodes."""
    st = cap.get("stages")
    if not st:
        C.R.add("YB", "INFO", "BW1's stages: this build's probe reports none (legacy %s)" % cap.get("legacy"))
        return
    C.R.add("YB", "INFO", "BW1's stages (ms): read-back %.0f, texture builds %.0f, the wait for the encoders %.0f (%.1f), the materials' rest %.0f, meshes %.0f, nodes %.1f (materials %.0f, capture %.0f); "
            "%s levels read, %s level-by-level waits; in flight at most %.0f MiB under the limit %.0f MiB; the old path %s"
            % (st.get("readback", -1), st.get("textureBuild", -1), st.get("encodeWait", -1), cap.get("encodeWaitMs", -1), st.get("materialsRest", -1), st.get("meshes", -1), st.get("nodes", -1),
               st.get("materials", -1), st.get("capture", -1), cap.get("levelsRead"), cap.get("levelByLevelWaits"), cap.get("peakInFlight", 0) / 1048576.0, (cap.get("maxInFlight") or 0) / 1048576.0, cap.get("legacy")))


def bw2(C, key):
    cap = probe(C, "capture", key=key, parts=["clothes0"], timeout=600)
    end = time.time() + 60
    while time.time() < end and not probe(C, "encoded").get("done"):
        C.frames(5)
    probe(C, "release")
    C.R.add("BW2", "PASS" if cap["captureMs"] <= BAR["BW2"] else "WARN", "a part bake (BR-Chan's top) captured: %.0f ms (%s); %d renderers, %d textures. Bar %.0f"
            % (cap["captureMs"], ", ".join("%s %.0f" % (k, v) for k, v in sorted(cap["split"].items())), cap["renderers"], cap["textures"], BAR["BW2"]))


def bw3(C, chara, item):
    """V22: the frame with the bake drawn against the frame with the character drawn, the same view, alternating rounds."""
    a = C.a
    pos = probe(C, "item", key=item)["position"]
    C.call({"cmd": "camera.set", "target": [pos[0], pos[1] + 10, pos[2]], "rot": [15, 180, 0], "dist": 25, "fov": 23})

    def show(c_on):
        C.call({"cmd": "obj.visible", "key": chara, "visible": c_on})
        C.call({"cmd": "obj.visible", "key": item, "visible": not c_on})
        C.frames(5)
    offs, ons = [], []
    for _ in range(a.rounds):
        show(True)
        offs.append(frame_ms(C, a.measure_frames)[0])
        show(False)
        ons.append(frame_ms(C, a.measure_frames)[0])
    d = med([y - x for x, y in zip(offs, ons)])
    C.R.add("BW3", "PASS" if d <= BAR["BW3"] else "WARN", "drawing the whole bake against drawing the character, the same view: %.3f ms against %.3f ms (bake %s, character %s): %+.3f ms. Bar +%.1f"
            % (med(ons), med(offs), ", ".join("%.3f" % x for x in ons), ", ".join("%.3f" % x for x in offs), d, BAR["BW3"]))


def bw4(C, no_whole, no_part):
    """Load: a scene holding only a whole bake, only a garment: the bake's entries read from the file (bakeprobe.readres) plus the prototype's load (gather, inflate, build);
    a second item of the same bake."""
    out = {}
    for label, no, bar in (("whole", no_whole, BAR["BW4"]), ("garment", no_part, BAR["BW4g"])):
        C.call({"cmd": "scene.new"})
        k = key_of(C.call({"cmd": "item.add", "group": APP_GROUP, "category": 0, "no": no}, timeout=300))
        C.frames(3)
        path = scene_save(C, "bw4_" + label)["path"]
        C.call({"cmd": "scene.new"})
        C.frames(3)
        t0 = time.perf_counter()
        C.call({"cmd": "scene.load", "path": path}, timeout=600)
        C.frames(1)
        wall = (time.perf_counter() - t0) * 1000
        it = probe(C, "item", key=k)
        p = it.get("prototype") or {}
        ms = p.get("loadMs", -1)
        rd = probe(C, "readres", key=k, path=path)
        total = rd["ms"] + ms
        ok = it.get("source") == "bake" and not it.get("fallback") and ms >= 0 and rd["entries"] == rd["named"]
        C.R.add("BW4", ("PASS" if total <= bar else "WARN") if ok else "FAIL", "load of a scene holding a %s bake: %.0f ms = the bake's %d entries read from the file %.0f ms (%.1f MiB) + the bake gathered, inflated and built %.0f ms (build %.0f ms); "
                "scene.load and a frame over HTTP %.0f ms; video %.0f MiB (computed from formats). Bar %.0f"
                % (label, total, rd["entries"], rd["ms"], rd["bytes"] / 1048576.0, ms, p.get("buildMs", -1), wall, p.get("videoBytes", 0) / 1048576.0, bar))
        sp = p.get("split") or {}
        # YR: the load's stages (printed, no bar)
        C.R.add("YR", "INFO", "BW4's stages of the %s bake's load (ms): read %.0f, the pre-start %.1f, gathered and the main read %.1f, recompute %.1f; in the build: textures had %.1f, meshes had %.1f, uploads %.1f, "
                "the main thread waited %.1f; the workers' inflate %.0f ms of CPU, %s of %s planned blobs taken from workers; the build %.0f (nodes %.1f, textures %.1f, materials %.1f, meshes %.1f, renderers %.1f)"
                % (label, rd["ms"], sp.get("load.start", -1), sp.get("load.gather", -1), sp.get("load.recompute", -1), sp.get("inflate.textures", -1), sp.get("inflate.meshes", -1), sp.get("upload", -1),
                   sp.get("inflate.wait", -1), sp.get("inflate.workers", -1), sp.get("inflate.taken", "-"), sp.get("inflate.planned", "-"), p.get("buildMs", -1), sp.get("nodes", -1), sp.get("textures", -1),
                   sp.get("materials", -1), sp.get("meshes", -1), sp.get("renderers", -1)) if sp else "BW4's stages of the %s bake's load: this build's probe reports none" % label)
        out[label] = k
    d = probe(C, "duplicate", key=out["garment"])
    C.R.add("BW4", ("PASS" if d["ms"] <= BAR["BW4s"] else "WARN") if d.get("ok") else "FAIL", "a second item of the same bake (a duplicate of the garment and the apply after it): %.1f ms. Bar %.0f" % (d["ms"], BAR["BW4s"]))

def bw5(C, whole):
    """Cuts: the procedure of the pieces run again from their source (the cut's own time, encoding on workers included); cut.make's time beside it."""
    pos = probe(C, "item", key=whole)["position"]
    cases = [("a whole character, clean", whole, {"kind": "plane", "pos": [pos[0], pos[1] + WAIST, pos[2]], "rot": [0, 0, 0]}, None, BAR["BW5"]),
             ("a whole character, ragged", whole, {"kind": "plane", "pos": [pos[0], pos[1] + WAIST, pos[2]], "rot": [0, 0, 0]}, {"amp": 0.1, "tooth": 0.2, "random": 0.5, "seed": 1}, BAR["BW5r"])]
    chair = key_of(C.call({"cmd": "item.add", "ref": CHAIR}))
    C.frames(3)
    m = probe(C, "meshes", key=chair)
    mid = [(x + y) / 2 for x, y in zip(m["min"], m["max"])]
    cases.insert(1, ("an item (%s), clean" % CHAIR, chair, {"kind": "plane", "pos": mid, "rot": [0, 0, 0]}, None, BAR["BW5i"]))
    for label, target, shape, ragged, bar in cases:
        q = {"cmd": "cut.make", "key": target, "shape": shape, "mode": "both", "hide": False}
        if ragged:
            q["ragged"] = ragged
        r = C.call(q, timeout=600)
        t = probe(C, "cut", keys=r["pieces"], rounds=5, timeout=600)
        ok = t.get("error") is None and all(t["sameBits"])
        C.R.add("BW5", ("PASS" if t["median"] <= bar else "WARN") if ok else "FAIL", "cut of %s: the procedure %.1f ms median of %s (the same bits each time: %s); cut.make %.1f ms with its preparation and commit. Bar %.0f"
                % (label, t["median"], t["ms"], all(t["sameBits"]), r["ms"], bar))
        # YU: the cut's stages (printed, no bar)
        dec = [x for x in t.get("decodeMs") or [] if x is not None]
        enc = [x for x in t.get("encodeMs") or [] if x is not None]
        C.R.add("YU", "INFO", ("BW5's stages of the cut of %s (ms, per round): the source's meshes decoded %s (median %.1f, meshes %s), the pieces' blobs encoded on workers %s (median %.1f); the procedure %s"
                               % (label, dec, med(dec), t.get("meshesDecoded"), enc, med(enc), t["ms"])) if t.get("stages") else "BW5's stages of the cut of %s: this build records none" % label)
        C.call({"cmd": "delete", "keys": r["pieces"]})


def old_folder(a):
    if a.old_dir:
        return a.old_dir
    with open(os.path.join(a.project, "Config", "settings.local.json"), encoding="utf-8") as f:
        hs2 = json.load(f).get("hs2Root")
    return os.path.join(os.path.abspath(os.path.join(a.project, hs2)), "UserData", "CharaToItem") if hs2 else None


def hash_tree(folder):
    """{relative path: SHA-256} of every file (read with Python's default sharing: others may read and write meanwhile)."""
    out = {}
    for p in sorted(glob.glob(os.path.join(folder, "**", "*"), recursive=True)):
        if os.path.isfile(p):
            with open(p, "rb") as f:
                out[os.path.relpath(p, folder)] = hashlib.sha256(f.read()).hexdigest()
    return out


def bw10(C):
    """The import of the old bakes from a copy in W's folder (the lead's ruling Q1): the originals hashed, copied, the copy imported; the originals hashed again
    at the end of W (bw10_end)."""
    a = C.a
    if a.no_import:
        C.R.add("BW10", "WARN", "not run (--no-import)")
        return
    folder = old_folder(a)
    if not folder or not os.path.isdir(folder):
        C.R.add("BW10", "SKIP", "the old bakes' folder is absent: nothing imported")
        return
    before = hash_tree(folder)
    a.store["bw10"] = {"folder": folder, "before": before}
    copy = os.path.join(C.out, "old_copy")
    if os.path.isdir(copy):
        shutil.rmtree(copy)
    for rel in before:
        to = os.path.join(copy, rel)
        os.makedirs(os.path.dirname(to), exist_ok=True)
        with open(os.path.join(folder, rel), "rb") as src, open(to, "wb") as dst:
            shutil.copyfileobj(src, dst)
    copied = hash_tree(copy)
    t0 = time.perf_counter()
    r = C.call({"cmd": "bake.import", "path": copy}, timeout=900)
    secs = time.perf_counter() - t0
    imp, skipped = r.get("imported") or [], r.get("skipped") or []
    a.store["bw10"].update({"secs": secs, "imported": [x.get("no") for x in imp], "skipped": len(skipped), "copyEqual": copied == before})


def bw10_end(C):
    a = C.a
    b = a.store.get("bw10")
    if not b:
        return
    after = hash_tree(b["folder"])
    untouched = after == b["before"]
    ok = untouched and b.get("copyEqual") and b.get("imported")
    secs = b.get("secs", -1)
    C.R.add("BW10", ("PASS" if secs <= BAR["BW10"] else "WARN") if ok else "FAIL", "the import of the old bakes from a copy in this session's folder: %d imported (%s), %d skipped, %.1f s (bar %.0f); the copy equal to the originals %s; "
            "the originals' SHA-256 before the copy and at the end of the session: %d files, equal %s"
            % (len(b.get("imported") or []), ", ".join(str(x) for x in b.get("imported") or []), b.get("skipped", 0), secs, BAR["BW10"], b.get("copyEqual"), len(b["before"]), untouched))

def br9_again(C):
    a = C.a
    s = a.store.get("br9")
    if not s or not s.get("without"):
        if not a.session_on("L", ["BA1", "BR9"]):
            C.R.add("BR9", "SKIP", "with the sample again: needs sessions S and L in the same run (--only)")
        else:
            C.R.add("BR9", "FAIL", "with the sample again: no scene of session L")
        return
    C.call({"cmd": "scene.load", "path": s["without"]}, timeout=600)
    C.frames(5)
    torus = s["keys"][4]
    it = probe(C, "item", key=torus)
    v = probe(C, "verts", key=torus)
    ring2 = (v.get("bones") or {}).get("ring2")
    r = scene_save(C, "br9_again")
    with open(r["path"], "rb") as f:
        b = f.read()
    ok = it.get("source") == "generated" and not it.get("fallback") and ring2 is not None and math.dist(ring2, [0, 315, 40]) < 1e-2 and b == s["bytes"]
    C.R.add("BR9", "PASS" if ok else "FAIL", "with the sample again: the torus %s (fallback %s), bent as saved: ring2 %s; saved again byte-equal %s" % (it.get("source"), it.get("fallback"), ring2, b == s["bytes"]))


def unload_read(C):
    """One Resources.UnloadUnusedAssets, then a GC: the probe's private bytes and managed heap after it."""
    probe(C, "unload", start=True)
    end = time.time() + 120
    while time.time() < end:
        C.frames(2)
        u = probe(C, "unload")
        if u.get("done"):
            return u
    raise RuntimeError("the unload did not end in 120 s")


def bw7_cycles(C):
    """BW7's growth after a capture, printed only (the lead, after the wiring run dbgWV3): three cycles of {a whole BR-Chan loaded and baked by bake.make,
    scene.new, one UnloadUnusedAssets + GC}, the private bytes and the managed heap after each, and cycle 3 - cycle 1. No bar, no verdict."""
    C.call({"cmd": "scene.new"})
    C.frames(5)
    base = unload_read(C)
    rows = []
    for i in range(3):
        br = load_chara(C, "female")
        C.frames(10)
        _, b, r = bake(C, br, store=False, hide=True, name="bw7 cycle %d" % (i + 1))
        C.call({"cmd": "scene.new"})
        C.frames(5)
        u = unload_read(C)
        rows.append(u)
        C.R.add("BW7C", "INFO", "cycle %d (BR-Chan loaded, bake.make whole %.0f ms, %.1f MiB in res/; scene.new; UnloadUnusedAssets + GC): private bytes %.1f MiB (%+.1f over the start), managed heap %.1f MiB (%+.1f)"
                % (i + 1, r.get("ms", 0), b.get("bytes", 0) / 1048576.0, u["private"] / 1048576.0, (u["private"] - base["private"]) / 1048576.0,
                   u["heap"] / 1048576.0, (u["heap"] - base["heap"]) / 1048576.0))
    mib = [x["private"] / 1048576.0 for x in rows]
    heap = [x["heap"] / 1048576.0 for x in rows]
    C.R.add("BW7C", "INFO", "cycle 3 - cycle 1: private bytes %+.1f MiB (cycle 2 - 1 %+.1f, 3 - 2 %+.1f), managed heap %+.1f MiB; the start (scene.new, UnloadUnusedAssets + GC) %.1f MiB private, %.1f MiB managed (printed, no bar)"
            % (mib[2] - mib[0], mib[1] - mib[0], mib[2] - mib[1], heap[2] - heap[0], base["private"] / 1048576.0, base["heap"] / 1048576.0))


def run_w(C):
    a = C.a
    probe(C, "frames", vsync=0, fps=-1)
    others = [x for x in others_running() if not x.endswith(" %s" % C.pid)]
    if others:
        C.R.add("BWRUN", "WARN", "the timing session ran beside other processes: %s (the row must run alone)" % ", ".join(others))
    guard(C, "BW8", bw8)
    guard(C, "BW9", bw9)
    C.call({"cmd": "scene.new"})
    br = load_chara(C, "female")
    C.frames(30)
    guard(C, "BW1", lambda C_: bw1(C_, br))
    guard(C, "BW2", lambda C_: bw2(C_, br))
    whole, wb, wr = bake(C, br, store=True, hide=True, name="bw whole")
    part, pb, pr = bake(C, br, parts=["clothes0"], store=True, hide=False, name="bw top")
    C.call({"cmd": "obj.visible", "key": part, "visible": False})
    C.R.add("BW6", "PASS" if wb["bytes"] <= BAR["BW6"] else "WARN", "bytes in res/ of a whole character: %.1f MiB (bake.make %.0f ms). Bar %.0f MiB" % (wb["bytes"] / 1048576.0, wr["ms"], BAR["BW6"] / 1048576.0))
    C.R.add("BW6", "PASS" if pb["bytes"] <= BAR["BW6g"] else "WARN", "bytes in res/ of a garment: %.2f MiB (bake.make %.0f ms). Bar %.0f MiB" % (pb["bytes"] / 1048576.0, pr["ms"], BAR["BW6g"] / 1048576.0))
    wi = probe(C, "item", key=whole)
    C.R.add("BW7", "PASS", "video memory of a whole bake: %.0f MiB of textures (computed from formats: the device's counter is not read), meshes %.1f MiB; no bar (BQ21)"
            % ((wi.get("prototype") or {}).get("videoBytes", 0) / 1048576.0, (wi.get("prototype") or {}).get("meshBytes", 0) / 1048576.0))
    guard(C, "BW3", lambda C_: bw3(C_, br, whole))
    guard(C, "BW5", lambda C_: bw5(C_, whole))
    guard(C, "BW4", lambda C_: bw4(C_, wb["no"], pb["no"]))
    guard(C, "BW10", bw10)
    guard(C, "BR9", br9_again)
    guard(C, "BW10", bw10_end)
    guard(C, "BW7C", bw7_cycles)


def run_old(C):
    """O1, O2: the last build with the frames-only probe: BW8's HTTP numbers."""
    http_measure(C, C.letter)

# ---------------------------------------------------------------- session G: BD12

def run_g(C):
    s = probe(C, "sample", n=150, floorGb=9, timeout=650)
    found = [x["ref"] for x in s["found"]]
    if s["stoppedAt"] >= 0:
        C.R.add("BD12W1", "WARN", "the sample stopped after %d rows: free memory under 9 GB (%.1f GB)" % (s["stoppedAt"], s["freeGb"]))
    C.R.add("BD12", "PASS" if s["unreadable"] > 0 else "FAIL", "present: Step 0's sample in the player: %d rows of %d tried, %d loaded, %d meshes, %d the player cannot read, in %d rows: %s"
            % (s["tried"], s["rows"], s["loaded"], s["meshes"], s["unreadable"], len(found), ", ".join("%s %d" % (x["ref"], x["unreadable"]) for x in s["found"]) or "none"))
    for ref in STEP0_ITEMS + [x for x in found if x not in STEP0_ITEMS]:
        C.call({"cmd": "scene.new"})
        r = C.call({"cmd": "item.add", "ref": ref}, check=False)
        if not r.get("ok"):
            C.R.add("BD12", "FAIL", "%s: not added: %s" % (ref, bad_reply(r)))
            continue
        k = key_of(r)
        C.frames(5)
        m = probe(C, "meshes", key=k)
        under = [m["min"][0] / 2 + m["max"][0] / 2, m["min"][1] - 1.0, m["min"][2] / 2 + m["max"][2] / 2]
        cut = C.call({"cmd": "cut.make", "key": k, "shape": {"kind": "plane", "pos": under, "rot": [0, 0, 0]}, "mode": "front", "hide": False, "name": "bd12"}, timeout=600, check=False)
        if not cut.get("ok") or len(cut.get("pieces") or []) != 1:
            C.R.add("BD12", "FAIL", "%s: the item could not be baked by a cut under it: %s" % (ref, bad_reply(cut)))
            continue
        C.frames(5)
        warn = [w for w in cut.get("warnings") or []]
        pi = probe(C, "item", key=cut["pieces"][0])
        cut_r = (pi.get("prototype") or {}).get("sections", -1)
        new_b = pi.get("newBlobs", -1)
        if cut_r != 0 or new_b != 0:
            C.R.add("BD12", "FAIL", "%s: the plane under the item cut something: cut renderers %s, blobs the source does not hold %s" % (ref, cut_r, new_b))
            continue
        C.R.add("BD12", "PASS", "%s: the plane 1 unit under its bounds: cut renderers 0, the piece's blobs all the source's" % ref)
        look(C, "BD12", "%s (%d of %d meshes unreadable, %d vertices; the cut's warnings %s): the item against its bake through the GPU route" % (ref, m["unreadable"], m["meshes"], m["unreadableVertices"], warn or "none"),
             k, cut["pieces"][0], "auto", BD12_FOOT)


# ---------------------------------------------------------------- session V: BV6

def norm(v):
    n = math.sqrt(sum(x * x for x in v)) or 1.0
    return [x / n for x in v]


def look_rot(d):
    """camera.set's rot for a camera at c + d x dist looking at c."""
    return [math.degrees(math.asin(max(-1.0, min(1.0, d[1])))), math.degrees(math.atan2(-d[0], -d[2])), 0]


def nine(n):
    """The member's own view, four 40 degrees off it and four 70 degrees off it (RV3's directions)."""
    up = [0.0, 1.0, 0.0] if abs(n[1]) < .9 else [1.0, 0.0, 0.0]
    x = norm([n[1] * up[2] - n[2] * up[1], n[2] * up[0] - n[0] * up[2], n[0] * up[1] - n[1] * up[0]])
    y = [n[1] * x[2] - n[2] * x[1], n[2] * x[0] - n[0] * x[2], n[0] * x[1] - n[1] * x[0]]
    out = [list(n)]
    for deg in (40, 70):
        c, s = math.cos(math.radians(deg)), math.sin(math.radians(deg))
        for a_, sign in ((x, 1), (x, -1), (y, 1), (y, -1)):
            out.append(norm([c * n[i] + sign * s * a_[i] for i in range(3)]))
    return out


def render(C, tag, path="sync", w=1280, h=720):
    r = probe(C, "render", tag=tag, path=path, width=w, height=h, aa="none" if path != "thumb" else "smaa")
    if path == "async":
        end = time.time() + 120
        while time.time() < end:
            r = probe(C, "rendered", tag=tag)
            if r.get("done"):
                break
            C.frames(2)
        else:
            raise RuntimeError("the render %s did not finish" % tag)
    if r.get("error"):
        raise RuntimeError("render %s: %s" % (tag, r["error"]))
    return r


def members(C, item, hide):
    probe(C, "members", key=item, hide=hide)
    C.frames(2)


def mask_of(C, item, path, w=1280, h=720, within=None):
    """The member's pixels in one path: rendered drawn and undrawn; within the mirror's area when asked."""
    render(C, path + "1", path, w, h)
    members(C, item, True)
    render(C, path + "0", path, w, h)
    members(C, item, False)
    q = {"a": path + "1", "b": path + "0", "threshold": 2}
    if within:
        q.update({"within": path + "1", "withinOf": within})
    return probe(C, "compare", **q)["differ"]


def bv6_view(C, item, c, n):
    best = 0
    for dist in V_DISTANCES:
        for d in nine(n):
            C.call({"cmd": "camera.set", "target": c, "rot": look_rot(d), "dist": dist, "fov": 23})
            C.frames(2)
            m = mask_of(C, item, "sync")
            best = max(best, m)
            if m >= BV6_MASK_MIN:
                return m, dist, best
    return None, None, best


def bv6_paths(C, label, item, wide):
    """At the view found: each path's mask and its pixels against a capture's; the block against F."""
    C.call({"cmd": "app.options", "render": {"mosaicWide": wide}})
    C.frames(3)
    rows, fails = [], []
    m_sync = mask_of(C, item, "sync")
    render(C, "ref", "sync")
    render(C, "ref2", "sync")
    two = probe(C, "compare", a="ref", b="ref2", threshold=2)["differ"]
    mem = probe(C, "members", key=item, width=1280, height=720)
    blocks = [x["blockPx"] for x in mem["rows"]]
    m_async = mask_of(C, item, "async")
    same_async = probe(C, "compare", a="async1", b="ref", threshold=2)["differ"]
    m_thumb = mask_of(C, item, "thumb", THUMB_W, THUMB_H)
    render(C, "thumb2", "thumb", THUMB_W, THUMB_H)
    same_thumb = probe(C, "compare", a="thumb1", b="thumb2", threshold=2)["differ"]
    mem_t = probe(C, "members", key=item, width=THUMB_W, height=THUMB_H)
    for name, m in (("capture", m_sync), ("screenshot and recorded frame (async)", m_async), ("scene thumbnail", m_thumb)):
        rows.append("%s %d px" % (name, m))
        if m < BV6_MASK_MIN:
            fails.append("%s: mask %d px under %d" % (name, m, BV6_MASK_MIN))
    if two or same_async or same_thumb:
        fails.append("paths differ: two captures %d px, async against a capture %d px, two thumbnails %d px" % (two, same_async, same_thumb))
    least = min(blocks) if blocks else 0
    pl = probe(C, "plain", key=item, tag="plain")
    control = probe(C, "compare", a="plain", b="ref", threshold=2, within="sync1", withinOf="sync0")
    if control["differ"] <= 0:
        fails.append("the positive control: the members drawn flat with the mosaic left out equal the mosaic render in their pixels")
    if least < mem["floorPx"]:
        fails.append("a block %.1f px under F %.1f" % (least, mem["floorPx"]))
    C.R.add("BV6", "FAIL" if fails else "PASS", "%s, wide %s: masks %s (at least %d each); two captures %d px, async against a capture %d px, two thumbnails %d px; least block %.1f px against F %.1f (thumbnail's last %s against F %.1f); the members drawn flat, the mosaic left out (%d members): %d of %d px of the mask differ from the mosaic render (> 0); modes %s%s"
            % (label, "on" if wide else "off", ", ".join(rows), BV6_MASK_MIN, two, same_async, same_thumb, least, mem["floorPx"], [x["blockPx"] for x in mem_t["rows"]], mem_t["floorPx"],
               pl.get("members", 0), control["differ"], control["pixels"],
               sorted(set(x["mode"] for x in mem["rows"])), ("; " + "; ".join(fails)) if fails else ""))


def bv6_mirror(C, label, item, other, c, n, floor):
    """The member in a puddle's mirror: the virtual camera under the floor along one of the nine directions, a puddle where the ray meets the floor (RV6's geometry).
    The distance per direction (the lead's rule after the wiring run dbgGV2): for a direction with d.y < 0, max(10, (c.y - plane + 1) / (-d.y)), at most 30 units;
    printed for each view."""
    plane = floor + 0.02
    best, tried = 0, 0
    dists = []
    for k, d in enumerate(nine(n), 1):
        if d[1] >= 0:
            dists.append("%d: d.y %+.2f, left out" % (k, d[1]))
            continue
        dist = max(V_DISTANCES[0], (c[1] - plane + 1.0) / -d[1])
        if dist > V_MIRROR_CAP:
            dists.append("%d: %.1f over %g, left out" % (k, dist, V_MIRROR_CAP))
            continue
        tried += 1
        below = [c[i] + d[i] * dist for i in range(3)]
        view = [below[0], 2 * plane - below[1], below[2]]
        target = [c[0], 2 * plane - c[1], c[2]]
        f = norm([target[i] - view[i] for i in range(3)])
        t = (view[1] - floor) / max(view[1] - target[1], 1e-3)
        hit = [view[i] + (target[i] - view[i]) * t for i in range(3)]
        C.call({"cmd": "wetprobe.puddle", "key": other, "x": hit[0], "y": floor, "z": hit[2], "area": 200, "clear": True})
        C.call({"cmd": "camera.set", "target": target, "rot": [math.degrees(math.asin(-f[1])), math.degrees(math.atan2(f[0], f[2])), 0], "dist": dist, "fov": 23})
        C.frames(4)
        mirror_on(C, (other,), False)
        render(C, "off", "sync")
        mirror_on(C, (other,), True)
        C.frames(2)
        m = mask_of(C, item, "sync", within="off")
        dists.append("%d: %.2f units, %d px" % (k, dist, m))
        best = max(best, m)
        if m >= BV6_MASK_MIN:
            render(C, "mref", "sync")
            render(C, "masy", "async")
            same = probe(C, "compare", a="mref", b="masy", threshold=2, within="mref", withinOf="off")
            C.R.add("BV6", "PASS" if same["differ"] == 0 else "FAIL", "%s, a puddle's mirror: the mask %d px in the mirror's area (at least %d) at %.2f units, view %d of the nine; async against a capture there %d px of %d; the centre %.2f units over the plane; by direction %s"
                    % (label, m, BV6_MASK_MIN, dist, k, same["differ"], same["pixels"], c[1] - plane, "; ".join(dists)))
            return
    C.R.add("BV6", "FAIL", "%s, a puddle's mirror: no view of %d tried with the mask at least %d px in the mirror's area within %g units (best %d); the centre %.2f units over the plane; by direction %s"
            % (label, tried, BV6_MASK_MIN, V_MIRROR_CAP, best, c[1] - plane, "; ".join(dists)))


def run_v(C):
    probe(C, "frames", vsync=0, fps=-1)
    for who in ("BR-Chan", "HERO107"):
        new_map(C, no=HOME_MAP)
        me = load_chara(C, "female" if who == "BR-Chan" else "male")
        other = load_chara(C, "male" if who == "BR-Chan" else "female", [60, 0, 60])
        C.call({"cmd": "chara.clothes", "key": me, "all": 2})
        if who == "HERO107":
            C.call({"cmd": "chara.state", "key": me, "set": {"son": {"visible": True}}})
        C.call({"cmd": "wet.attach", "key": other, "preset": "water"}, timeout=60)
        C.frames(10)
        floor = C.call({"cmd": "wet.floor", "key": me})["y"]
        item, _, _ = bake(C, me, store=False, hide=True, name="bv6")
        C.call({"cmd": "scene.pause", "paused": True})
        C.frames(5)
        rows = probe(C, "members", key=item)
        main = "cm_o_dan00" if who == "HERO107" else "o_mnpb"
        m = next((x for x in rows["rows"] if x["name"] == main), rows["rows"][0] if rows["rows"] else None)
        if m is None:
            C.R.add("BV6", "FAIL", "%s: the bake holds no member" % who)
            continue
        c = m.get("centroid") or m["centre"]
        n = norm(m.get("normal") or rows["forward"])
        if who == "HERO107":   # a procedural member's region has no surface to look along: from below and in front, as RV6
            f = norm(rows["forward"])
            n = norm([0.6 * f[0], -0.8 + 0.6 * f[1], 0.6 * f[2]])
        mask, dist, best = bv6_view(C, item, c, n)
        if mask is None:
            C.R.add("BV6", "FAIL", "%s: no view of 27 (10, 7, 5 units by the nine directions) with the member's mask at least %d px (best %d)" % (who, BV6_MASK_MIN, best))
        else:
            C.R.add("BV6", "PASS", "%s: the view at %g units, the member's mask %d px in a capture; members %s" % (who, dist, mask, [x["name"] for x in rows["rows"]]))
            for wide in (False, True):
                bv6_paths(C, who, item, wide)
            C.call({"cmd": "app.options", "render": {"mosaicWide": False}})
        bv6_mirror(C, who, item, other, c, n, floor)
        C.call({"cmd": "scene.pause", "paused": False})
        probe(C, "forget")


# ---------------------------------------------------------------- session I: identity (YI1a, YI1b, YI2, YI3)
# I0 and I1 run the kept player (--baseline-exe, Builds\AppPrev610: the build before this phase's speed fixes) with Tests\BakeProbe built against its own Managed folder; I runs this build.
# I0 makes and saves the fixtures (s0F: BR-Chan and HERO107 standing still, paused; s0F2: s0F's three bakes and the whole bake cut by a plane), I1 bakes s0F again and loads s0F2 in a
# second process of the kept player (the Present of YI1b and YI3: the kept player repeats itself), I runs YI1a in one process and bakes s0F and loads s0F2 as the kept player did.

YI_BAKES = (("brWhole", "br", None), ("heWhole", "he", None), ("brTop", "br", ["clothes0"]))
YI_LEAST = {"brWhole": 149, "heWhole": 79, "brTop": 12}   # YI1a's Present: data blobs per bake
YI_OLD_WAITS = 1000                                         # YI1a's Present: the old path's level-by-level waits for BR-Chan whole
YI2_LEAST, YI2_NEW_MESHES, YI2_ROUNDS = (130, 69), 10, 5     # YI2's Present: the pieces' data blobs, their new mesh blobs
YI3_FOOT = 80000


def yi_capture(C, key, parts):
    """A capture through the probe, then its encoding to the end: the capture's reply and the encoded identity."""
    cap = probe(C, "capture", key=key, parts=parts or [], timeout=600)
    end, enc = time.time() + 180, None
    while time.time() < end:
        C.frames(5)
        enc = probe(C, "encoded")
        if enc.get("done"):
            break
    probe(C, "release")
    if not enc or not enc.get("done") or enc.get("faulted") or not enc.get("identity"):
        raise RuntimeError("the encoding did not end or holds no identity: %s" % bad_reply(enc or {}))
    return cap, enc["identity"]


def yi_legacy(C, on):
    r = probe(C, "legacy", on=on)
    if r.get("legacy") is not on:
        raise RuntimeError("bakeprobe.legacy did not take %s: %s" % (on, bad_reply(r)))
    return r


def yi1a(C):
    """One process, the new path against the old (BakeJob.Legacy): BR-Chan and HERO107 paused, chara.motion physics false, blink off; each baked whole and BR-Chan's top."""
    C.call({"cmd": "scene.new"})
    br = load_chara(C, "female")
    he = load_chara(C, "male", [12, 0, 0])
    for k in (br, he):
        C.call({"cmd": "chara.motion", "key": k, "physics": False})
    C.call({"cmd": "scene.pause", "paused": True})
    C.frames(10)
    keys = {"br": br, "he": he}
    res = {}
    try:
        for path in ("new", "old"):
            yi_legacy(C, path == "old")
            for label, who, parts in YI_BAKES:
                res[(path, label)] = yi_capture(C, keys[who], parts)
    finally:
        probe(C, "legacy", on=False)
    for label, _, _ in YI_BAKES:
        (cn, a), (co, b) = res[("new", label)], res[("old", label)]
        diff = [(x["kind"], x["index"], x["name"]) for x, y in zip(a["entries"], b["entries"]) if x["blob"] != y["blob"]]
        same = len(a["entries"]) == len(b["entries"]) and not diff and a["dataSet"] == b["dataSet"] and a["mainNormalized"] == b["mainNormalized"]
        floor = YI_OLD_WAITS if label == "brWhole" else 1
        present = co.get("levelByLevelWaits", 0) >= floor and cn.get("levelByLevelWaits", -1) == 0 and a["data"] >= YI_LEAST[label] and b["data"] >= YI_LEAST[label]
        C.R.add("YI1a", "PASS" if same and present else "FAIL", "%s, one process, the new path against the old (BakeJob.Legacy): %d of %d entries' blobs equal (differ: %s), the data set equal %s, the main with id and created fixed equal %s; "
                "Present: data blobs %d / %d (at least %d), the old path's level-by-level waits %d (at least %d), the new path's %d; levels read %s / %s; the capture %.0f / %.0f ms (limit %.0f / %.0f MiB)"
                % (label, len(a["entries"]) - len(diff), len(a["entries"]), diff[:8] or "none", a["dataSet"] == b["dataSet"], a["mainNormalized"] == b["mainNormalized"], a["data"], b["data"],
                   YI_LEAST[label], co.get("levelByLevelWaits", 0), floor, cn.get("levelByLevelWaits", -1), cn.get("levelsRead"), co.get("levelsRead"), cn["captureMs"], co["captureMs"],
                   (cn.get("maxInFlight") or 0) / 1048576.0, (co.get("maxInFlight") or 0) / 1048576.0))


def yi_blend_on(C, key):
    """The names of a character's renderers with a blend shape weighing more than 0 (pose.blendShapes): their meshes are drawn through BakeMesh with the live weights."""
    r = C.call({"cmd": "pose.blendShapes", "key": key})
    return sorted(set(x["name"] for x in r.get("renderers") or [] if any(abs(s.get("weight") or 0) > 0 for s in x.get("shapes") or [])))


def yi_main(path, sha):
    """A bake's main (JSON) read from a saved scene's res/ entry: the blob's 16-byte header, then the raw deflate stream."""
    with zipfile.ZipFile(path) as z:
        b = z.read("res/%s.bin" % sha)
    import zlib
    return json.loads(zlib.decompress(b[16:], -15).decode("utf-8"))


def yi_bake_fixture(C, keys, tag):
    """s0F's three bakes as Step 0 made them (bake.make, hide false): per bake its identity, the blend-on renderers at bake time, and from the saved scene the meshes those renderers draw."""
    out = {}
    for label, who, parts in YI_BAKES:
        blend = yi_blend_on(C, keys[who])
        q = {"cmd": "bake.make", "key": keys[who], "hide": False, "store": True, "name": "yi %s %s" % (tag, label)}
        if parts:
            q["parts"] = parts
        r = C.call(q, timeout=600)
        it = r["items"][0]
        C.frames(3)
        out[label] = {"item": it, "identity": probe(C, "blobs", key=it), "blend": blend, "ms": r.get("ms")}
    path = scene_save(C, "yi_bakes_" + tag)["path"]
    for label, v in out.items():
        m = yi_main(path, v["identity"]["main"])
        names = [n.get("name") for n in m.get("nodes") or []]
        v["blendMeshes"] = sorted(set(x["mesh"] for x in m.get("renderers") or [] if x.get("mesh", -1) >= 0 and 0 <= x.get("node", -1) < len(names) and names[x["node"]] in v["blend"]))
    return out


def yi_shots(C, items):
    return {label: probe(C, "shot", key=k, view="auto", timeout=900) for label, k in items}


def yi_copy_in(C, src):
    """A fixture copied into this session's UserData (a scene loads from there)."""
    d = os.path.join(C.ud, "scene")
    os.makedirs(d, exist_ok=True)
    to = os.path.join(d, os.path.basename(src))
    shutil.copyfile(src, to)
    return to


def run_i0(C):
    """The kept player: the fixtures s0F and s0F2 saved, s0F's bakes, the cut and the four renders."""
    a = C.a
    C.call({"cmd": "scene.new"})
    br = load_chara(C, "female")
    he = load_chara(C, "male", [12, 0, 0])
    C.call({"cmd": "scene.pause", "paused": True})
    C.frames(10)
    keys = {"br": br, "he": he}
    f = scene_save(C, "s0F")["path"]
    bakes = yi_bake_fixture(C, keys, "I0")
    w = bakes["brWhole"]["item"]
    pos = probe(C, "item", key=w).get("position") or [0, 0, 0]
    cut = C.call({"cmd": "cut.make", "key": w, "shape": {"kind": "plane", "pos": [pos[0], pos[1] + WAIST, pos[2]], "rot": [0, 0, 0]}, "mode": "both", "hide": False}, timeout=600)
    items = [("brWhole", w), ("brTop", bakes["brTop"]["item"])] + [("piece%d" % i, k) for i, k in enumerate(cut["pieces"])]
    shots = yi_shots(C, items)
    f2 = scene_save(C, "s0F2")["path"]
    a.store["yi"] = {"F": f, "F2": f2, "keys": keys, "items": items, "pieces": cut["pieces"], "bakes": {"I0": bakes}, "shots": {"I0": shots}}
    C.R.add("YI0", "INFO", "the kept player %s: fixtures saved (s0F, s0F2), bakes %s, the cut's pieces %d, renders %s"
            % (C.exe, ", ".join("%s %d data" % (k, v["identity"]["data"]) for k, v in bakes.items()), len(cut["pieces"]), ", ".join("%s %d px" % (k, v.get("footprint", 0)) for k, v in shots.items())))


def run_i1(C):
    """The kept player again, a second process: s0F baked and s0F2 loaded (the Present of YI1b and YI3)."""
    s = C.a.store.get("yi")
    if not s:
        raise RuntimeError("session I0 left no fixture")
    C.call({"cmd": "scene.load", "path": yi_copy_in(C, s["F"])}, timeout=600)
    C.frames(10)
    C.call({"cmd": "scene.pause", "paused": True})
    C.frames(5)
    s["bakes"]["I1"] = yi_bake_fixture(C, s["keys"], "I1")
    C.call({"cmd": "scene.new"})
    C.frames(5)
    C.call({"cmd": "scene.load", "path": yi_copy_in(C, s["F2"])}, timeout=600)
    C.frames(10)
    s["shots"]["I1"] = yi_shots(C, s["items"])


def yi_compare(x, y, left_out):
    """Two processes' bakes of one fixture: the texture blobs' SHA-256 sets, and the mesh blobs by index with the meshes of blend-on renderers left out."""
    ex, ey = x["identity"]["entries"], y["identity"]["entries"]
    tx = [e["blob"] for e in ex if e["kind"] == "texture"]
    ty = [e["blob"] for e in ey if e["kind"] == "texture"]
    mx = [e for e in ex if e["kind"] == "mesh"]
    my = [e for e in ey if e["kind"] == "mesh"]
    tex_equal = sum(1 for p, q in zip(tx, ty) if p == q)
    compared = [(p, q) for p, q in zip(mx, my) if p["index"] not in left_out]
    mesh_equal = [p["name"] for p, q in compared if p["blob"] == q["blob"] and p["name"] == q["name"]]
    mesh_differ = [p["name"] for p, q in compared if not (p["blob"] == q["blob"] and p["name"] == q["name"])]
    ok = len(tx) == len(ty) and set(tx) == set(ty) and tex_equal == len(tx) and len(mx) == len(my) and not mesh_differ and len(compared) > 0
    return ok, {"textures": len(tx), "texturesEqual": tex_equal, "textureSet": set(tx) == set(ty), "meshes": len(mx), "meshesOther": len(my), "compared": len(compared),
                "meshEqual": len(mesh_equal), "meshDiffer": mesh_differ, "leftOut": sorted(p["name"] for p in mx if p["index"] in left_out)}


def yi1b(C, s):
    """Capture across versions: the kept player and this one bake the fixture s0F saved by the kept player."""
    for label, _, _ in YI_BAKES:
        b0, b1, bn = s["bakes"]["I0"][label], s["bakes"]["I1"][label], s["bakes"]["I"][label]
        left = set(b0["blendMeshes"]) | set(b1["blendMeshes"]) | set(bn["blendMeshes"])
        rep_ok, rep = yi_compare(b0, b1, left)
        ok, d = yi_compare(b0, bn, left)
        C.R.add("YI1b", "PASS" if ok and rep_ok else "FAIL", "%s, s0F (saved by the kept player) baked by the kept player and by this one: texture blobs %d of %d equal (the sets equal %s); meshes with no blend shape over 0: "
                "%d of %d equal (differ: %s); left out %d meshes of renderers with a blend shape over 0 (%s; renderers %s); Present: the kept player's two processes: textures %d of %d (sets %s), meshes %d of %d (differ: %s)"
                % (label, d["texturesEqual"], d["textures"], d["textureSet"], d["meshEqual"], d["compared"], d["meshDiffer"] or "none", len(d["leftOut"]), ", ".join(d["leftOut"]) or "none",
                   ", ".join(sorted(set(b0["blend"]) | set(bn["blend"]))) or "none", rep["texturesEqual"], rep["textures"], rep["textureSet"], rep["meshEqual"], rep["compared"], rep["meshDiffer"] or "none"),
                kept=rep, here=d)


def yi2(C, s):
    """The cut across versions: each piece of s0F2 (saved by the kept player) run again from its stored source here, the stored main SHA-256 every round."""
    pieces = s["pieces"]
    t = probe(C, "cut", keys=pieces, rounds=YI2_ROUNDS, timeout=600)
    src = {e["blob"] for e in probe(C, "blobs", key=dict(s["items"])["brWhole"])["entries"] if e["kind"] == "mesh"}
    data, new = [], []
    for k in pieces:
        b = probe(C, "blobs", key=k)
        data.append(b["data"])
        new.append(len({e["blob"] for e in b["entries"] if e["kind"] == "mesh" and e["blob"] not in src}))
    same = t.get("sameBits") or []
    present = len(data) == 2 and sorted(data, reverse=True)[0] >= YI2_LEAST[0] and sorted(data, reverse=True)[1] >= YI2_LEAST[1] and sum(new) >= YI2_NEW_MESHES
    ok = t.get("error") is None and len(same) == YI2_ROUNDS and all(same) and present
    C.R.add("YI2", "PASS" if ok else "FAIL", "the cut of s0F2 (saved by the kept player): its %d pieces run again from their stored source %d rounds here: the stored main SHA-256 each round %s (error %s); "
            "Present: the pieces' data blobs %s (at least %d and %d), new mesh blobs %s (at least %d in all); the procedure %s ms"
            % (len(pieces), YI2_ROUNDS, same, t.get("error"), data, YI2_LEAST[0], YI2_LEAST[1], new, YI2_NEW_MESHES, t.get("ms")))


def yi3(C, s):
    """Load and render across versions: s0F2's bakes and pieces rendered alone here (the new load path, then the old one) against the kept player's two processes."""
    for label, _ in s["items"]:
        k0, k1 = s["shots"]["I0"][label], s["shots"]["I1"][label]
        n, o = s["shots"].get("I", {}).get(label, {}), s["shots"].get("Iold", {}).get(label, {})
        rep = k0.get("sha") == k1.get("sha")
        present = rep and (n.get("footprint") or 0) >= YI3_FOOT and n.get("twice") == 0
        ok = present and n.get("sha") == k1.get("sha") and o.get("sha") == k1.get("sha")
        C.R.add("YI3", "PASS" if ok else "FAIL", "%s, s0F2 (saved by the kept player) loaded and rendered alone: this player %s (the old load path %s), the kept player %s (made) / %s (loaded), equal %s; "
                "Present: footprint %d px (at least %d), rendered twice differs on %s px, the kept player's two processes equal %s"
                % (label, (n.get("sha") or "")[:16], (o.get("sha") or "")[:16], (k0.get("sha") or "")[:16], (k1.get("sha") or "")[:16], n.get("sha") == k1.get("sha") and o.get("sha") == k1.get("sha"),
                   n.get("footprint") or 0, YI3_FOOT, n.get("twice"), rep))


def run_i(C):
    a = C.a
    lg = probe(C, "legacy")
    if not lg.get("switch"):
        C.R.add("YI1a", "FAIL", "this build has no old path switch (BakeJob.Legacy): %s" % bad_reply(lg))
    else:
        guard(C, "YI1a", yi1a)
    s = a.store.get("yi")
    if not s or "I1" not in s["bakes"] or "I1" not in s["shots"]:
        for cid in ("YI1b", "YI2", "YI3"):
            C.R.add(cid, "WARN" if not a.baseline_exe else "FAIL", "not compared: the kept player's sessions left no fixture or no second process (--baseline-exe %s)" % a.baseline_exe)
        return
    C.call({"cmd": "scene.new"})
    C.frames(5)
    C.call({"cmd": "scene.load", "path": yi_copy_in(C, s["F"])}, timeout=600)
    C.frames(10)
    C.call({"cmd": "scene.pause", "paused": True})
    C.frames(5)
    s["bakes"]["I"] = yi_bake_fixture(C, s["keys"], "I")
    guard(C, "YI1b", lambda C_: yi1b(C_, s))
    f2 = yi_copy_in(C, s["F2"])

    def load_shots(C_, tag, legacy):
        """s0F2 loaded here and its four rendered alone; with legacy the load takes the old path (the switch set before the load, put back after)."""
        C_.call({"cmd": "scene.new"})
        C_.frames(5)
        try:
            if legacy:
                yi_legacy(C_, True)
            C_.call({"cmd": "scene.load", "path": f2}, timeout=600)
            C_.frames(10)
            s["shots"][tag] = yi_shots(C_, s["items"])
        finally:
            if legacy:
                yi_legacy(C_, False)
    guard(C, "YI3", lambda C_: load_shots(C_, "I", False))
    if "I" in s["shots"]:
        guard(C, "YI2", lambda C_: yi2(C_, s))
    else:
        C.R.add("YI2", "FAIL", "s0F2 did not load here (YI3's exception line)")
    guard(C, "YI3", lambda C_: load_shots(C_, "Iold", True))
    guard(C, "YI3", lambda C_: yi3(C_, s))


def build_old_probe(a, exe, folder):
    """Tests\\BakeProbe built against the kept player's own Managed folder (it reads this phase's members by name, so it builds against both)."""
    managed = os.path.join(os.path.dirname(exe), "HS2Next_Data", "Managed")
    guid, rel, csproj = PROBES[0]
    target = os.path.join(folder, guid)
    os.makedirs(target, exist_ok=True)
    obj = os.path.join(a.out, "probe_obj_old", guid) + os.sep
    cmd = ["dotnet", "build", os.path.join(a.project, rel, csproj), "-c", "Release", "-nologo", "-v", "q", "-o", target, "-p:PlayerManaged=" + managed, "-p:BaseIntermediateOutputPath=" + obj]
    p = subprocess.run(cmd, capture_output=True, timeout=300, creationflags=CREATE_NO_WINDOW)
    if p.returncode != 0:
        raise SetupError("the test plugin %s did not build against the kept player: %s" % (guid, p.stdout.decode("utf-8", "replace")[-600:]))
    shutil.copyfile(os.path.join(a.project, rel, "manifest.json"), os.path.join(target, "manifest.json"))
    return folder


# ---------------------------------------------------------------- no player: BV9

def bv9(a, R):
    """The new files: no switch; the lines that name the mosaic carry no reason (WV7's and RV8's search)."""
    root = os.path.join(a.project, "Assets", "HS2Next")
    files = []
    for rel in ("Runtime/Scene/Bake/*.cs", "Runtime/Scene/Cut/*.cs", "Runtime/UI/Panels/Bake/**/*.cs", "Runtime/UI/Panels/Cut/**/*.cs", "Runtime/App/Commands/BakeCommands.cs", "Runtime/App/Commands/CutCommands.cs",
                "Runtime/Scene/Runtime/Ops/StudioOps.Bake.cs", "Runtime/Save/Documents/SceneBake.cs", "Runtime/Scene/Commands/BakeEditCommand.cs", "Runtime/Content/Studio/GeneratedRows.cs",
                "ApiUnity/ItemRows.cs", "Runtime/App/Plugins/Engine/PluginItemRows.cs"):
        files += glob.glob(os.path.join(root, rel), recursive=True)
    files = sorted(set(files))
    switch = re.compile(r"GetEnvironmentVariable|GetCommandLineArgs|PlayerPrefs|EditorPrefs|AppSettings|#if\s|#elif\s")
    mosaic = re.compile(r"mosaic|member", re.I)
    reason = re.compile(r"\bbecause\b|\bso that\b|\bin order to\b|censor|\blaws?\b|\blegal\b|\bregulat", re.I)
    hits, rule, why = [], [], []
    for path in files:
        rel = os.path.relpath(path, root).replace("\\", "/")
        with open(path, encoding="utf-8") as f:
            for no, line in enumerate(f, 1):
                if switch.search(line.split("//")[0]):
                    hits.append("%s:%d" % (rel, no))
                if mosaic.search(line):
                    rule.append("%s:%d" % (rel, no))
                    if reason.search(line):
                        why.append("%s:%d" % (rel, no))
    ok = len(files) >= 20 and bool(rule) and not hits and not why
    R.add("BV9", "PASS" if ok else "FAIL", "%d new files searched: what reads a setting, an argument or a variable: %s; %d lines name the mosaic or a member (read by hand: in the json), with a reason on them: %s"
          % (len(files), hits or "none", len(rule), why or "none"), rule=rule)


# ---------------------------------------------------------------- main

def write_reports(a, R, exit_code, started):
    n_pass, n_fail, n_warn = R.count("PASS"), R.count("FAIL"), R.count("WARN")
    summary = "SUMMARY: %d PASS, %d FAIL" % (n_pass, n_fail)
    warns = [x["id"] for x in R.items if x["verdict"] == "WARN"]
    lines = [summary, "WARN: %d%s" % (n_warn, (" (" + ", ".join(warns) + ")") if warns else ""), "bake_player_check %s; exe %s; exit code %d; %.0f s" % (started, a.exe, exit_code, time.perf_counter() - T0)]
    for x in R.items:
        lines.append("%-7s %-4s  %s" % (x["id"], x["verdict"], x["text"]))
    lines.append(summary)
    with open(os.path.join(a.out, "bake_player_report.txt"), "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")
    with open(os.path.join(a.out, "bake_player_check.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump({"started": started, "exe": a.exe, "exitCode": exit_code, "summary": summary, "checks": R.items}, f, ensure_ascii=False, indent=1, default=str)


def main(argv):
    for s in (sys.stdout, sys.stderr):
        try:
            s.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, ValueError):
            pass
    ap = argparse.ArgumentParser()
    ap.add_argument("--exe", required=True)
    ap.add_argument("--project", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--port", type=int, default=17889)
    ap.add_argument("--timeout-min", type=float, default=90)
    ap.add_argument("--only", default="")
    ap.add_argument("--measure-frames", type=int, default=400)
    ap.add_argument("--rounds", type=int, default=3)
    ap.add_argument("--gfx", choices=["d3d11", "d3d12"], default=None)
    ap.add_argument("--min-free-gb", type=float, default=8)
    ap.add_argument("--ram-wait-min", type=float, default=10)
    ap.add_argument("--ready-timeout-min", type=float, default=12)
    ap.add_argument("--old-dir", default=None)
    ap.add_argument("--no-import", action="store_true")
    ap.add_argument("--baseline-exe", default=None, help="the last build (Builds\\AppPrev68\\HS2Next.exe): BW8 against it over HTTP")
    a = ap.parse_args(argv)
    a.exe, a.project, a.out = os.path.abspath(a.exe), os.path.abspath(a.project), os.path.abspath(a.out)
    only = set(x.strip().upper() for x in a.only.split(",") if x.strip())
    a.want = lambda c: not only or c.upper() in only
    a.session_on = lambda letter, ids: not only or letter in only or bool(only & set(i.upper() for i in ids))
    # what one session leaves for the next (the sessions' namespaces are copies of this one; the dict is shared)
    a.store = {"away": os.path.join(a.out, "bakes_away")}
    os.makedirs(a.out, exist_ok=True)
    started = utc_now()
    R = Results()
    deadline = time.time() + a.timeout_min * 60
    exit_code = 0
    live = []
    stop_gate = threading.Event()
    gated = [False]

    def stop_all(why):
        for C in live:
            C.aborted = why
            kill(C)

    def on_timeout():
        log("timed out after %.1f min" % a.timeout_min)
        stop_all("timed out after %.1f min (--timeout-min); the started player was killed" % a.timeout_min)

    def gate_poll():
        while not stop_gate.wait(30):
            try:
                games = running_games()
            except Exception:
                continue
            if games:
                gated[0] = True
                stop_all("aborted by the game gate (%s)" % ", ".join(games))
                return

    watchdog = threading.Timer(a.timeout_min * 60, on_timeout)
    watchdog.daemon = True
    watchdog.start()
    threading.Thread(target=gate_poll, daemon=True).start()

    def ram():
        need, end = a.min_free_gb, time.time() + a.ram_wait_min * 60
        g = free_gb()
        while g is not None and g < need and time.time() < end:
            log("free RAM %.1f GB < %.1f GB: waiting" % (g, need))
            time.sleep(15)
            g = free_gb()
        return g

    def session(letter, plugins, body_, ud=None, exe=None):
        nonlocal exit_code
        g = ram()
        if g is not None and g < a.min_free_gb:
            R.add("SETUP" + letter, "FAIL", "free RAM %.1f GB < %.1f GB after %.0f min: session %s not started" % (g, a.min_free_gb, a.ram_wait_min, letter))
            return
        if letter in ("W", "I0", "I1", "I") and ud is None:
            # session W starts in a fresh folder: an earlier run's store would hold BW10's imported bakes, and a second import adds nothing; the identity sessions likewise (their fixtures and stores)
            w = os.path.join(a.out, letter.lower())
            if os.path.isdir(w):
                kept = "%s_prev_%s" % (w, time.strftime("%Y%m%d-%H%M%S"))
                os.rename(w, kept)
                R.add("FRESH" + letter, "INFO", "session %s's folder of an earlier run moved aside to %s (kept for the record)" % (letter, kept))
        C = Run(a, R, letter, plugins, ud, exe)
        live.append(C)
        try:
            start(C, deadline)
            body_(C)
            stop(C)
        except SetupError as e:
            R.add("SETUP" + letter, "WARN" if gated[0] else "FAIL", str(e))
            exit_code = 3 if gated[0] else 2
        except Abort as e:
            if gated[0]:
                R.add("GATE", "WARN", str(e))
                exit_code = 3
            else:
                R.add("ABORT", "FAIL", "session %s stopped: %s" % (letter, e))
        except Exception as e:   # one session's error: its line, then the next session
            R.add("RUN" + letter, "FAIL", "session %s: %s: %s" % (letter, type(e).__name__, e), traceback=traceback.format_exc(limit=8))
        finally:
            kill(C)
            try:
                check_log(C)
            except Exception as e:
                R.add("LOG" + letter, "WARN", "player.log not read: %s" % e)
            live.remove(C)
        return C

    try:
        gate_warning = game_processes()[1]
        if gate_warning:
            R.add("GATE", "WARN", gate_warning)
        games = running_games()
        if games:
            R.add("SETUP", "WARN", "the game gate: %s runs; no player row now" % ", ".join(games))
            exit_code = 3
        if a.want("BV9"):
            bv9(a, R)
        need_player = not only or bool(only & {"S", "L", "W", "G", "V", "I", "BA1", "BD1", "BD12", "BD14", "BR9", "BV6", "YI1A", "YI1B", "YI2", "YI3"}) or any(x.startswith("BW") for x in only)
        if exit_code == 0 and need_player:
            probes, with_shapes = build_plugins(a)
            s_ud = os.path.join(a.out, "s", "userdata")
            if a.session_on("S", ["BA1", "BD1", "BD14", "BR9"]):
                session("S", with_shapes, run_s)
                store, away = os.path.join(s_ud, "bakes"), a.store["away"]
                if os.path.isdir(store):
                    if os.path.isdir(away):
                        shutil.rmtree(away)
                    os.rename(store, away)
            if exit_code == 0 and a.session_on("L", ["BA1", "BR9"]):
                session("L", probes, run_l, s_ud)
            want_w = a.session_on("W", ["BR9", "BWRUN"]) or any(x.startswith("BW") for x in only)
            old_exe = os.path.abspath(a.baseline_exe) if a.baseline_exe else None
            old_plugins = None
            if want_w and old_exe and os.path.isfile(old_exe):
                old_plugins = build_probe(types.SimpleNamespace(exe=old_exe, project=a.project), os.path.join(a.out, "old"), True)
            if exit_code == 0 and want_w and old_plugins:
                session("O1", old_plugins, run_old, None, old_exe)
            if exit_code == 0 and want_w:
                session("W", with_shapes, run_w)
            if exit_code == 0 and want_w and old_plugins:
                session("O2", old_plugins, run_old, None, old_exe)
            if want_w:
                bw8_compare(a, R)
            if exit_code == 0 and a.session_on("G", ["BD12"]):
                session("G", probes, run_g)
            if exit_code == 0 and a.session_on("V", ["BV6"]):
                session("V", probes, run_v)
            # session I (identity): I0 and I1 the kept player (--baseline-exe), then I this build
            if exit_code == 0 and a.session_on("I", ["YI1a", "YI1b", "YI2", "YI3"]):
                cross = only == set() or bool(only & {"I", "YI1B", "YI2", "YI3"})
                if cross and old_exe and os.path.isfile(old_exe):
                    old_bake = build_old_probe(a, old_exe, os.path.join(a.out, "plugins_old"))
                    session("I0", old_bake, run_i0, None, old_exe)
                    if a.store.get("yi"):
                        session("I1", old_bake, run_i1, None, old_exe)
                if exit_code == 0:
                    session("I", probes, run_i)
    except SetupError as e:
        R.add("SETUP", "FAIL", str(e))
        exit_code = 2
    except Exception as e:
        R.add("RUN", "FAIL", "%s: %s" % (type(e).__name__, e), traceback=traceback.format_exc(limit=8))
    finally:
        stop_gate.set()
        watchdog.cancel()
        if exit_code == 0 and R.count("FAIL"):
            exit_code = 1
        write_reports(a, R, exit_code, started)
        log("SUMMARY: %d PASS, %d FAIL, %d WARN -> exit %d; report %s" % (R.count("PASS"), R.count("FAIL"), R.count("WARN"), exit_code, os.path.join(a.out, "bake_player_report.txt")))
    return exit_code


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
