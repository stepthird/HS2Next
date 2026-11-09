"""
ragdoll_player_check.py - ragdolls in the built app player (Phase 6.7; the row RagdollCheck.player).

Starts the app player itself, several times, and checks over real HTTP from outside the process, in real frames. It never uses the Unity Editor. Python 3.14, standard library only, plus dotnet for the
test plugins. Content: the clothed SFW visual targets of Tools\\targets.json on non-H rows, judged by numbers. No image is opened or shown and none is written: renders stay in the player's memory through
the test plugins. A body can end in any pose, so every render check of a body is under this rule.

Usage (the run_tests.ps1 row RagdollCheck.player calls the first form):
    python -I Tools\\ragdoll_player_check.py --exe <path to HS2Next.exe> --project <project root> --out <dir> [--port 17869] [--timeout-min 60]
    more options: --only A,K,F,B,M,V or check ids (default all), --port-b 17878, --port-c 17868, --measure-frames 400, --rounds 3, --baseline-exe <an older player> (RW6a against it),
                  --baseline-report <a build report> (RD13 against it), --gfx d3d11 | d3d12, --min-free-gb 8, --ram-wait-min 10, --ready-timeout-min 12
    -I ignores PYTHONUTF8, so the script sets its own stdout/stderr to UTF-8.

Sessions (one player at a time):
  A  -batchmode, no plugin (HS2NEXT_NO_PLUGINS=1), --port. What an ordinary player does:
     CMDS    the 19 commands over HTTP on a real map, each with what it must show; a stain from a shot
     SHADERS the player's log read for the effects' shaders
  R  -batchmode, the test plugins, --port-c:
     TRIP    a body at rest and a body in a fall through save, new, load: the pushed body's pose in the file equals its last rest, the resting body's equals the runtime's settled buffer, no velocity
             member, the pose on load, the second save, an undo of a release
  K  -batchmode, the test plugins, --port-c, twice:
     RK1     scene.step runs from one state in a fresh world, 4 in a process, a run with the push a step later, 2 processes: hashes equal
  F  windowed, the test plugins, --port-c, four times, each one reading:
     FIRST   the first renders after a build, the view's camera off from before the build: a half-float target and the capture path, with a body and without. A number, not a verdict
  B  windowed 1920 x 1080, -noUi, the test plugins, --port-b, the frame cap off, illusion.hs2#10, BR-Chan and HERO107: RW1-RW4, RW6-RW12, RD6 (spec 17.3)
  M  the same on the 50 maps: RL4, RW5, then OBST: items #400 and #906 as obstacles
  V  the same: RV6 on BR-Chan and HERO107, a limp fall and a body knocked down, in the view and in a puddle's mirror, in memory
  No player: RV8 (a search of the new files), RD13 (the build's report).

Outputs:
  <out>\\ragdoll_player_report.txt  first and last line "SUMMARY: N PASS, M FAIL", second line "WARN: K (ids)", then one line per check
  <out>\\ragdoll_player_check.json  details of every check
  <out>\\<session>\\player.log, userdata\\ (the saves)
Exit code: 0 every check passed (WARN allowed), 1 a check failed, 2 setup error, 3 aborted by the game gate. A time over its bar is WARN: the lead decides.
Ports: 17869, 17878, 17868 by default (17879 = RagdollTest.play, 17322 = interactive default). Always 127.0.0.1, never localhost.
"""
import argparse
import glob
import json
import math
import os
import re
import shutil
import statistics
import struct
import subprocess
import sys
import threading
import time
import traceback
import zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)        # python -I does not add the script's folder
from constraint_player_check import (Abort, Results, SetupError, free_gb, game_processes, guard, http, log, port_listeners, running_games, target_rel, CREATE_NO_WINDOW, T0)  # noqa: E402
from wet_player_check import ROW_IDLE, Session, ab, check_log, frame_ms, kill, mirror_on, new_map, read_log, render, stop, two, utc_now, within  # noqa: E402

BAR = {"RW1": 0.6, "RW1p": 1.0, "RW2": 1.5, "RW2p": 2.5, "RW3": 0.05, "RW3x4": 0.2, "RW4": 10.0, "RW5": 40.0, "RW5max": 100.0, "RW5mesh": 5.0, "RW6": 1.0, "RW6a": 0.05, "RW7": 256, "RW8": 0.5, "RW9": 0.2, "RW10": 0.1,
       "RW11": 1 << 20, "RW12": 0.3}
CLOCK = "2026-11-07T00:00:00Z"
PROBES = (("stp3.test.ragdollprobe", os.path.join("Tests", "RagdollProbe"), "RagdollProbe.csproj"), ("stp3.test.wetprobe", os.path.join("Tests", "WetProbe"), "WetProbe.csproj"))
DT = 1.0 / 60.0
HOME_MAP = 10
# the 50 maps of S0-6, the set of 6.6
MAPS = ["illusion.hs2#%d" % n for n in (0, 1)] + ["illusion.hs2#%d" % n for n in range(10, 38)] + ["2155x.map." + x + "#0" for x in (
    "hangar", "hospitallab", "interrogationroom", "morgueroom", "pcalley", "pcjapaneseroom", "pcpark", "pcwesternroom", "phakikoroom", "phbedroom", "phentrance", "phlivingroom", "phritsukoroom", "phsubway",
    "phsubwaynight", "phwashroom", "phyard", "scottishhome", "slums", "undergroundroom")]
# S0-6's two known cases: a body that ended on the surface but not by the bar (a known class has its maxCount; a third case fails)
RL4_KNOWN = {"illusion.hs2#20": "over a desk's edge", "2155x.map.phakikoroom#0": "still rolling after dropping off furniture"}
RL4_MAX_KNOWN = 2
RL4_REACH = 1.0          # a part's lowest point within this of the surface under it
RL4_UNDER = 0.05         # what a contact may sink under the room's floor
RL4_DROP, RL4_STEPS = 4.0, 180
CUBE, NO_MESH_ITEM = "illusion.hs2#400", "illusion.hs2#906"
BASE_PROGRAMS, BASE_COMMIT = 17882, "97604f05"     # the shader programs of the last build before this phase, and that commit
SHADER_SET = "Assets/HS2Next/Resources/HS2NextShaders.shadervariants"
RV_W, RV_H, RV_MOMENTS, RV_LIMP_FRAMES, RV_ACTIVE_FRAMES, RV_VIEW = 960, 540, 36, 2, 8, 10.0
RV_DISTANCES = (10.0, 7.0, 5.0)   # the view's distances in order, each with the nine directions (the lead, fixed before the acceptance run's data)
RV_MASK_MIN = 1000       # the member's pixels at 960 x 540 under which a moment has judged nothing (WV6M's lesson)
RV_LEAST_JUDGED = 30     # a walk with fewer judged moments in the view fails (RV3's rule)
RV_LEAST_MIRROR = 6      # a walk with fewer judged moments in the mirror fails
RV_BODY_MIRROR_MIN = 1000
FIRST_CHARACTER_MIN = 2000
RK1_PUSH, RK1_STEPS, RK1_CHUNK = 60, 180, 20


def bad_reply(r):
    return json.dumps(r, ensure_ascii=False)[:240]


# ---------------------------------------------------------------- the test plugins

def build_probes(a):
    """Both test plugins against the player's own Managed folder, into <out>\\plugins\\<guid>. Returns the plugins folder."""
    managed = os.path.join(os.path.dirname(a.exe), "HS2Next_Data", "Managed")
    plugins = os.path.join(a.out, "plugins")
    for guid, rel, csproj in PROBES:
        target = os.path.join(plugins, guid)
        os.makedirs(target, exist_ok=True)
        obj = os.path.join(a.out, "probe_obj", guid) + os.sep
        cmd = ["dotnet", "build", os.path.join(a.project, rel, csproj), "-c", "Release", "-nologo", "-v", "q", "-o", target, "-p:PlayerManaged=" + managed, "-p:BaseIntermediateOutputPath=" + obj]
        p = subprocess.run(cmd, capture_output=True, timeout=300, creationflags=CREATE_NO_WINDOW)
        if p.returncode != 0:
            raise SetupError("the test plugin %s did not build: %s" % (guid, p.stdout.decode("utf-8", "replace")[-600:]))
        shutil.copyfile(os.path.join(a.project, rel, "manifest.json"), os.path.join(target, "manifest.json"))
    return plugins


# ---------------------------------------------------------------- sessions

def start(C, kind, deadline):
    """Starts a player: kind A batch, no plugin; K batch, the probes; B windowed, no UI, the probes."""
    a = C.a
    os.makedirs(C.out, exist_ok=True)
    busy = port_listeners(C.port)
    if busy:
        raise SetupError("port %d is already in use (PID %s)" % (C.port, ", ".join(map(str, sorted(busy)))))
    settings = os.path.join(C.project, "Config", "settings.local.json")
    if not os.path.isfile(settings):
        raise SetupError("settings file not found: %s" % settings)
    ud, bo, logf = os.path.join(C.out, "userdata"), os.path.join(C.out, "bridge_out"), os.path.join(C.out, "player.log")
    ep = os.path.join(ud, "Bridge", "endpoint.json")
    if os.path.isfile(ep):
        os.remove(ep)
    env = dict(os.environ)
    env["HS2NEXT_PRIVATE_OFF"] = "1"
    env.pop("HS2NEXT_DATA_ROOT", None)
    args = [C.exe]
    si = subprocess.STARTUPINFO()
    if kind == "A":
        env["HS2NEXT_NO_PLUGINS"] = "1"
        args.append("-batchmode")
    else:
        env.pop("HS2NEXT_NO_PLUGINS", None)
        if kind == "K":
            args.append("-batchmode")
        else:
            si.dwFlags |= subprocess.STARTF_USESHOWWINDOW
            si.wShowWindow = 4   # SW_SHOWNOACTIVATE
            args += ["-noUi", "-screen-width", "1920", "-screen-height", "1080", "-screen-fullscreen", "0", "-window-mode", "windowed"]
        args += ["-pluginsDir", a.plugins]
    if a.gfx:
        args.append("-force-" + a.gfx)
    args += ["-bridgePort", str(C.port), "-bridgeAllowQuit", "-settings", settings, "-cacheDir", os.path.join(C.project, "Cache"), "-userData", ud, "-bridgeOutputDir", bo, "-logFile", logf]
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
    C.R.add("SETUP" + C.letter, "PASS", "session %s ready in %.1f s (pid %s, port %d); api %s" % (C.letter, ready, C.pid, C.port, p.get("api")), readySeconds=round(ready, 2))


# ---------------------------------------------------------------- helpers

def pause(C, on=True):
    C.call({"cmd": "scene.pause", "paused": on})


def steps(C, n, dt=DT):
    if n > 0:
        C.call({"cmd": "scene.step", "n": n, "dt": dt}, timeout=max(60, n))


def load_chara(C, sex):
    """A clothed target, physics off: its key."""
    key = C.call({"cmd": "chara.load", "path": target_rel(C, sex), "sex": sex}, timeout=300)["added"][0]["key"]
    C.call({"cmd": "chara.motion", "key": key, "physics": False})
    return key


def idle(C, key, speed=1.0):
    C.call(dict(ROW_IDLE, cmd="chara.anime", key=key, time=0.0, speed=speed))


def status(C, key=None):
    q = {"cmd": "ragdoll.status"}
    if key:
        q["key"] = key
    return C.call(q)


def row_of(C, key):
    rows = status(C, key)["characters"]
    return rows[0] if rows else None


def body(C, key):
    return C.call({"cmd": "ragdollprobe.body", "key": key})


def parts(C, key):
    return C.call({"cmd": "ragdoll.parts", "key": key})["parts"]


def pelvis(C, key):
    return parts(C, key)["pelvis"]["pos"]


def distance(p, q):
    return math.sqrt(sum((x - y) * (x - y) for x, y in zip(p, q)))


def build(C, key, **more):
    return C.call(dict(more, cmd="ragdoll.build", key=key), timeout=60)


def release_all(C):
    C.call({"cmd": "ragdoll.release"})


def rest(C, key, limit=2400, chunk=60):
    """Steps until the body's pose is kept as settled; the steps it took, or None."""
    done = 0
    while done < limit:
        steps(C, chunk)
        done += chunk
        row = row_of(C, key)
        if row and row.get("settled"):
            return done
    return None


def stats(v):
    s = sorted(v)
    return s[len(s) // 2], s[min(len(s) - 1, int(len(s) * .95))], s[-1]


# ---------------------------------------------------------------- session A

def find_blocks(o, owner=None):
    """Every object named ragdoll in scene.json: (the id of the node it stands under, block); the characters' blocks have `on`, the scene's has no owner."""
    if isinstance(o, dict):
        if isinstance(o.get("id"), str):
            owner = o["id"]
        for k, v in o.items():
            if k == "ragdoll" and isinstance(v, dict):
                yield owner, v
            yield from find_blocks(v, owner)
    elif isinstance(o, list):
        for v in o:
            yield from find_blocks(v, owner)


def scene_of(path):
    with zipfile.ZipFile(path) as z:
        return z.read("scene.json"), json.loads(z.read("manifest.json").decode("utf-8"))


def blocks_of(path):
    """(scene.json's bytes, the manifest, {node id: a character's block}, [the scene's block])."""
    raw, man = scene_of(path)
    doc = json.loads(raw.decode("utf-8"))
    rows = list(find_blocks(doc))
    return raw, man, {k: b for k, b in rows if "on" in b}, [b for k, b in rows if "on" not in b]


def bits(v):
    return struct.unpack("<i", struct.pack("<f", v))[0]


def deg(q, r):
    d = abs(sum(x * y for x, y in zip(q, r)))
    return math.degrees(2 * math.acos(min(1.0, d)))


def save(C, name):
    return C.call({"cmd": "scene.save", "name": name, "overwrite": True, "thumb": "placeholder", "clock": CLOCK})["path"]


def velocity_members(o):
    if isinstance(o, dict):
        for k, v in o.items():
            if re.search(r"veloc|speed|contact|stagger|weak", k, re.I) and k != "pushSpeed" and k != "propSpeed":
                yield k
            yield from velocity_members(v)
    elif isinstance(o, list):
        for v in o:
            yield from velocity_members(v)


def trip(C):
    new_map(C, no=HOME_MAP)
    a, b = two(C, 6.0)      # both inside the room of the first body
    pause(C)
    C.frames(3)
    for k in (a, b):
        build(C, k)
        C.call({"cmd": "ragdoll.set", "key": k, "preset": "limp"})
    took = rest(C, a)
    took2 = rest(C, b)
    if took is None or took2 is None:
        C.R.add("TRIP", "FAIL", "the bodies did not settle: %s, %s steps; %s" % (took, took2, bad_reply(status(C))))
        return
    at_a, at_b = parts(C, a), parts(C, b)
    p0 = save(C, "trip0")
    _, man0, blocks0, world0 = blocks_of(p0)
    posed = [x for x in blocks0.values() if x.get("pose", {}).get("parts")]
    C.R.add("TRIP", "PASS" if len(blocks0) == 2 and len(posed) == 2 and man0.get("formatVersion") == 5 else "FAIL", "at rest after %s and %s steps: %d blocks, %d with a pose of %s parts, manifest revision %s" % (took, took2, len(blocks0), len(posed), len(posed[0]["pose"]["parts"]) if posed else 0, man0.get("formatVersion")))
    # b in a fall; a lies where it settled, and may go on sliding under the speed that counts as rest (11.3): its buffer is taken again every 0.4 s
    C.call({"cmd": "ragdoll.push", "key": b, "part": "chest", "impulse": [0, 1500, 0]})
    steps(C, 12)
    moved = distance(parts(C, b)["pelvis"]["pos"], at_b["pelvis"]["pos"])
    p1 = save(C, "trip1")
    held = C.call({"cmd": "ragdollprobe.settled", "key": a})
    raw1, _, blocks1, world1 = blocks_of(p1)
    pose0, pose1 = blocks0.get(b, {}).get("pose"), blocks1.get(b, {}).get("pose")
    vel = sorted(set(velocity_members(list(blocks1.values()) + world1)))
    ok = moved >= 3 and pose0 is not None and json.dumps(pose0, sort_keys=True) == json.dumps(pose1, sort_keys=True) and not vel
    C.R.add("TRIP", "PASS" if ok else "FAIL", "RF10, a save during a fall: the pushed body %.2f units from its rest (at least 3); its pose in the file equals its last rest, bits: %s; moving members %s" % (moved, ok and pose0 is not None, vel))
    qa0, qa1 = (blocks0.get(a, {}).get("pose") or {}).get("parts", {}), (blocks1.get(a, {}).get("pose") or {}).get("parts", {})
    moved_parts = [k for k in qa0 if qa0[k] != qa1.get(k)]
    most = max(((deg(qa0[k], qa1[k]), k) for k in moved_parts if k in qa1), default=(0.0, None))
    C.R.add("TRIP", "PASS", "the unpushed body between the two saves, printed: %d of %d parts differ, the most %s %.4f degrees, the hips %.5f units" % (len(moved_parts), len(qa0), most[1], most[0], distance((blocks0.get(a, {}).get("pose") or {}).get("hips", [0, 0, 0]), (blocks1.get(a, {}).get("pose") or {}).get("hips", [0, 0, 0]))))
    want = held.get("parts") or {}
    got = {k: [bits(x) for x in v] for k, v in qa1.items()}
    hips = [bits(x) for x in (blocks1.get(a, {}).get("pose") or {}).get("hips", [])]
    differ = [k for k in want if want[k] != got.get(k)]
    ok = held.get("held") is True and len(want) == 16 and len(got) == 16 and not differ and hips == held.get("hips")
    C.R.add("TRIP", "PASS" if ok else "FAIL", "the unpushed body's pose in the save during the fall equals the runtime's settled buffer at that save, bits: %d of %d parts, hips %s; buffer held %s, written since %s%s"
            % (len(want) - len(differ), len(want), hips == held.get("hips"), held.get("held"), not held.get("changed"), ("; differ " + ", ".join(differ)) if differ else ""), held=held)
    C.call({"cmd": "scene.new"})
    gone = C.call({"cmd": "ragdoll.status"})["world"]
    C.call({"cmd": "scene.load", "path": p1}, timeout=300)
    pause(C)
    C.frames(5)
    st = status(C)
    rows = st["characters"]
    keys = [r["key"] for r in rows]
    ok = len(rows) == 2 and st["world"]["bodies"] == 2 and not gone.get("bodies") and all(r["awake"] is False for r in rows)
    C.R.add("TRIP", "PASS" if ok else "FAIL", "after new: %d bodies; after a load: %s, every part asleep %s" % (gone.get("bodies") or 0, json.dumps([{x: r.get(x) for x in ("state", "awake", "settled", "parts")} for r in rows]), all(r["awake"] is False for r in rows)), rows=rows)
    if len(keys) == 2:
        now = [parts(C, k) for k in keys]
        worst, part = 0.0, None
        for was in (at_a, at_b):
            near = min(now, key=lambda p: distance(p["pelvis"]["pos"], was["pelvis"]["pos"]))
            for n, v in was.items():
                d = distance(v["pos"], near[n]["pos"])
                if d > worst:
                    worst, part = d, n
        stand = pelvis(C, keys[0])
        C.R.add("TRIP", "PASS" if worst <= 0.02 else "FAIL", "the load shows the last rest: worst part %s %.3f units from it, bar 0.02 (three decimals of ragdoll.parts); both bodies, the fall's too; pelvis now at y %.3f" % (part, worst, stand[1]))
    p2 = save(C, "trip2")
    raw2, _, blocks2, world2 = blocks_of(p2)
    same_blocks = json.dumps(blocks1, sort_keys=True) == json.dumps(blocks2, sort_keys=True)
    same_world = json.dumps(world1, sort_keys=True) == json.dumps(world2, sort_keys=True)
    lines = sum(1 for x, y in zip(raw1.split(b"\n"), raw2.split(b"\n")) if x != y) + abs(raw1.count(b"\n") - raw2.count(b"\n"))
    C.R.add("TRIP", "PASS" if same_blocks and same_world else "FAIL", "the second save: the characters' blocks equal %s, the scene's block equal %s; scene.json %d lines differ of %d (printed; a line outside the blocks is not this check's)" % (same_blocks, same_world, lines, raw2.count(b"\n") + 1))
    if keys:
        k = keys[0]
        was = pelvis(C, k)
        C.call({"cmd": "ragdoll.release", "key": k})
        C.frames(3)
        none = row_of(C, k)
        C.call({"cmd": "undo"})
        C.frames(3)
        back = row_of(C, k)
        d = distance(pelvis(C, k), was) if back and back.get("state") else None
        C.R.add("TRIP", "PASS" if none and none.get("state") is None and d is not None and d <= 0.02 else "FAIL", "a release: state %s; undone: state %s, the pelvis %s from where it lay" % (none and none.get("state"), back and back.get("state"), "%.3f" % d if d is not None else "not read"))
    pause(C, False)


def cmds(C):
    """Every command over HTTP in a player on a real map: C's rows ran in process, headless, on a stand-in map."""
    new_map(C, no=HOME_MAP)
    a, b = two(C, 6.0)
    pause(C)
    C.frames(3)
    g = C.call({"cmd": "ground.probe", "points": [[x, z] for x in (-8, -7, -6, -5, -4) for z in (-2, -1, 0, 1, 2)], "y": 6, "above": 0, "below": 26})
    lowest = min(h["y"] for h in g["hits"] if h.get("y") is not None)
    r = build(C, a)
    st = status(C)
    w = st["world"]
    # the grid's lowest answer, then the patch's lowest if lower (8.2)
    floor = r.get("floor")
    good = r.get("built") is True and floor is not None and floor <= lowest + 0.05 and abs(floor - w["floor"]) <= 1e-3 and w["floorSource"] == "map" and w["bodies"] == 1
    C.R.add("CMDS", "PASS" if good else "FAIL", "ragdoll.build: built %s, floor %s, the grid's lowest %.3f (%s), world %s" % (r.get("built"), floor, lowest, g.get("source"), json.dumps(w)[:220]))
    n = len(parts(C, a))
    done = []
    ok = n == 16
    for q, want in (({"cmd": "ragdoll.lock", "key": a, "parts": "forearm"}, lambda x: x.get("locked") == 2),
                    ({"cmd": "ragdoll.tie", "key": a}, lambda x: x.get("tied") is True),
                    ({"cmd": "ragdoll.pin", "key": a, "part": "hand_R"}, lambda x: x.get("pinned") is True),
                    ({"cmd": "ragdoll.unpin", "key": a}, lambda x: x.get("unpinned") == 1),
                    ({"cmd": "ragdoll.follower", "key": a, "part": "hand_R"}, lambda x: bool(x.get("folder")) and bool(x.get("constraint"))),
                    ({"cmd": "ragdoll.turn", "key": a}, lambda x: x.get("ok2") is True),
                    ({"cmd": "ragdoll.settle", "key": a, "seconds": 0.2}, lambda x: x.get("ok2") is True),
                    ({"cmd": "ragdoll.pusher", "key": b}, lambda x: x.get("count") == 1),
                    ({"cmd": "ragdoll.push", "key": a, "part": "chest", "local": [0, 0, -100]}, lambda x: x.get("pushed") is True),
                    ({"cmd": "ragdoll.knock", "key": a}, lambda x: x.get("state") == "down"),
                    ({"cmd": "ragdoll.set", "values": {"timeScale": 0.5}}, lambda x: "timeScale" in (x.get("changed") or [])),
                    ({"cmd": "ragdoll.set", "values": {"timeScale": 1}}, lambda x: "timeScale" in (x.get("changed") or []))):
        x = C.call(q, check=False)
        good = bool(x.get("ok")) and want(x)
        ok = ok and good
        done.append("%s %s" % (q["cmd"].split(".")[1], "ok" if good else bad_reply(x)))
    C.R.add("CMDS", "PASS" if ok else "FAIL", "%d parts; %s" % (n, "; ".join(done)))
    steps(C, 30)
    C.call({"cmd": "wet.attach", "key": a, "preset": "blood"}, timeout=60)
    was = C.call({"cmd": "wet.status", "key": a})["characters"][0]
    p = parts(C, a)["chest"]["pos"]
    s = C.call({"cmd": "fx.shoot", "from": [p[0], p[1], p[2] + 20], "target": {"key": a, "bone": "cf_J_Spine02"}, "stain": True})
    steps(C, 5)
    now = C.call({"cmd": "wet.status", "key": a})["characters"][0]
    C.R.add("CMDS", "PASS" if s.get("hits") == 1 and now["maps"]["live"] > was["maps"]["live"] else "FAIL", "fx.shoot stain true: %s hits, end %s; maps live %d -> %d" % (s.get("hits"), s.get("target"), was["maps"]["live"], now["maps"]["live"]))
    t = C.call({"cmd": "ragdoll.throw", "from": [p[0], p[1] + 2, p[2] + 15], "target": {"key": a, "bone": "cf_J_Spine02"}, "kind": "heavy"})
    steps(C, 30)
    w = status(C)["world"]
    C.R.add("CMDS", "PASS" if t.get("props") == 1 and w.get("props") == 1 else "FAIL", "ragdoll.throw: %s props; world props %s, last hit %s" % (t.get("props"), w.get("props"), json.dumps(w.get("lastHit"))))
    item = C.call({"cmd": "item.add", "ref": CUBE})["added"][0]["key"]
    o = C.call({"cmd": "ragdoll.obstacle", "key": item})
    bk = C.call({"cmd": "ragdoll.bake", "key": a})
    C.frames(3)
    left = status(C)["world"]
    C.R.add("CMDS", "PASS" if o.get("obstacles") == 1 and (bk.get("bones") or 0) >= 16 else "FAIL", "ragdoll.obstacle: %s; ragdoll.bake: %s bones; then %s" % (o.get("obstacles"), bk.get("bones"), json.dumps(left)[:160]))
    release_all(C)
    C.frames(3)
    w = status(C)["world"]
    C.R.add("CMDS", "PASS" if not w.get("bodies") and not w.get("props") and not w.get("pushers") else "FAIL", "ragdoll.release with no key: %s" % json.dumps(w)[:200])
    pause(C, False)


def shader_log(C):
    lines = read_log(C)
    said = [l for l in lines if "WetDrops" in l or "Unlit/Color" in l]
    bad = [l for l in said if any(w in l.lower() for w in ("error", "fail", "not found", "unsupported", "not supported", "missing"))]
    C.R.add("SHADERS", "FAIL" if bad else "PASS", "%d lines name the effects' shaders, %d say a fault%s" % (len(said), len(bad), (" (first: %s)" % bad[0][:160]) if bad else ""))


def run_a(C):
    for cid, fn in (("CMDS", cmds), ("SHADERS", shader_log)):
        if C.a.want(cid) or C.a.want("A"):
            guard(C, cid, fn)


# ---------------------------------------------------------------- session K: RK1

def rk1_run(C, push_at):
    """One run of the row's RK1 in a fresh world, the clock paused from before the characters."""
    C.call({"cmd": "scene.new"})
    pause(C)
    C.call({"cmd": "ragdollprobe.seed", "value": 0x5eed})
    a = load_chara(C, "female")
    b = load_chara(C, "male")
    C.call({"cmd": "obj.transform", "key": b, "pos": [5, 0, 2], "rot": [0, 200, 0]})
    idle(C, a)
    idle(C, b)
    C.frames(2)
    C.call({"cmd": "ragdoll.set", "values": {"floor.mode": "fixed", "floor.height": 0}})
    build(C, a)
    build(C, b)
    h = C.call({"cmd": "ragdollprobe.hash", "keys": [a, b]})
    built, start_at, moved, world = h["hash"], h["chest"], 0.0, h["world"]
    C.call({"cmd": "ragdollprobe.startwatch", "keys": [a, b]})
    for i in range(RK1_STEPS):       # every step read: the greatest distance is P's reading
        if i == push_at:
            C.call({"cmd": "ragdoll.push", "key": a, "part": "chest", "impulse": [0, 0, -1500]})
        steps(C, 1)
        h = C.call({"cmd": "ragdollprobe.hash", "keys": [a, b]})
        moved = max(moved, distance(h["chest"], start_at))
    end = distance(h["chest"], start_at)
    first = C.call({"cmd": "ragdollprobe.start"})
    C.call({"cmd": "scene.new"})
    C.frames(2)
    return h["hash"], moved, end, world, h["steps"], (first.get("hash") if first.get("read") else None), built


def rk1(C):
    hashes, starts, builts, worlds, moved, end, sub = [], [], [], [], 0.0, 0.0, 0
    for _ in range(4):
        h, moved, end, w, sub, first, built = rk1_run(C, RK1_PUSH)
        hashes.append(h)
        starts.append(first)
        builts.append(built)
        worlds.append(w)
    r = rk1_run(C, RK1_PUSH + 1)
    other = r[0]
    starts.append(r[5])
    builts.append(r[6])
    pause(C, False)
    C.a.rk1.append((starts, hashes[0]))
    ok = len(set(hashes)) == 1 and None not in starts and len(set(starts)) == 1 and moved >= 1 and other != hashes[0] and len(set(worlds)) == 4
    C.R.add("RK1", "PASS" if ok else "FAIL", "process %s, batch player, UI off, every step read: the start at the first tick before its first substep %s in all 5 runs %s; 4 runs end %s; 4 worlds %d; the bodies moved %.3f at most (at least 1), %.3f at the end, %d substeps; the push a step later %s; printed, read right after the build: %s"
            % (C.letter, " ".join(str(x) for x in sorted(set(starts), key=str)), len(set(starts)) == 1, " ".join(hashes), len(set(worlds)), moved, end, sub, other, " ".join(builts)))


# ---------------------------------------------------------------- session F: the first renders

def first_case(C, mode, with_body, label):
    """The character is built with the view's camera off, so nothing has drawn it; the clock stands, the blink is off."""
    C.call({"cmd": "ragdollprobe.view", "on": False})
    before = C.call({"cmd": "ragdollprobe.view"})
    pause(C)
    k = load_chara(C, "female")
    idle(C, k, 0.0)
    C.call({"cmd": "chara.face", "key": k, "blink": False})
    C.frames(2)
    if with_body:
        C.call({"cmd": "ragdoll.set", "values": {"floor.mode": "fixed", "floor.height": 0}})
        build(C, k)
        steps(C, 2)
    bone = C.call({"cmd": "chara.bones", "key": k, "names": ["cf_J_Spine02"]})["bones"]["cf_J_Spine02"]["pos"]
    C.call({"cmd": "camera.set", "target": bone, "rot": [0, 180, 0], "dist": 26, "fov": 23})
    C.frames(1)
    was = C.call({"cmd": "ragdollprobe.view"})
    r = C.call({"cmd": "ragdollprobe.first", "mode": mode, "key": k, "width": RV_W, "height": RV_H}, timeout=120)
    drawn = was["viewRendersWhileOff"]
    if mode == "half":
        d12, d23 = r["firstSecond"], r["secondThird"]
        text = "first against second %d px (%d on the character, box %s), second against third %d px; no number in the first, second, third render %s; the character's mask counts channels more than 1/255 apart" % (d12["differ"], d12["onCharacter"], d12["box"], d23["differ"], r["noNumber"])
    else:
        text = "first capture against the second %d px (%d on the character), second against third %d px; 8-bit, a value that is no number cannot show" % (r["firstSecond"], r["firstSecondOnCharacter"], r["secondThird"])
    ok = FIRST_CHARACTER_MIN <= r["character"] <= r["pixels"] // 2 and drawn == 0
    C.R.add("FIRST", "PASS" if ok else "FAIL", "%s, %s, %s: the character %d px of %d (at least %d, at most half the frame: a mask that is not the character judges nothing); %s; the view drew %d times while off, every camera before it %d, %s" % (label, mode, "with a body" if with_body else "no body", r["character"], r["pixels"], FIRST_CHARACTER_MIN, text, drawn, before["cameraRenders"], before["device"]), reply=r)
    C.call({"cmd": "scene.new"})
    C.frames(2)
    C.call({"cmd": "ragdollprobe.view", "on": True})


def run_first(mode, with_body):
    def body_(C):
        first_case(C, mode, with_body, "the first of the process")
        first_case(C, mode, with_body, "the second build of the process")
    return body_


# ---------------------------------------------------------------- session B: time

def rw6(C):
    """No body: the part's calls of a frame; with a known allocation as control."""
    a, b = two(C)
    r = C.call({"cmd": "ragdollprobe.idle", "n": 400000})
    us, alloc, frames = r["usPerFrame"] - r["emptyLoopUs"], r["bytes"], r["frames"]
    good = r["allocationControl"] >= frames * 8 or r["controlCollections"] > 0
    none = alloc < frames // 4 and r["collections"] == 0
    clean = r["rigidbodies"] == 0 and not r["world"] and not r["shots"] and r["scenes"] == 0
    v = "FAIL" if not clean else ("PASS" if us <= BAR["RW6"] and none and good else "WARN")
    C.R.add("RW6", v, "no body: Apply, Tick, LateApply %.4f us a frame (the loop alone %.4f); the heap moved %d B over %d frames, %d collections; one 16-byte array a frame moves it %d B, %d collections; rigid bodies %d, a world %s, a shots part %s, scenes of a world %d. Bars 1 us, 0 B"
            % (us, r["emptyLoopUs"], alloc, frames, r["collections"], r["allocationControl"], r["controlCollections"], r["rigidbodies"], r["world"], r["shots"], r["scenes"]), raw=r)
    m, p, n = frame_ms(C, C.a.measure_frames * 2)
    C.info["noBodyFrameMs"] = [frame_ms(C, C.a.measure_frames)[0] for _ in range(C.a.rounds)]
    C.R.add("RW6f", "PASS", "no body: frame median %.3f ms, p95 %.3f over %d frames; rounds %s" % (m, p, n, C.info["noBodyFrameMs"]), rounds=C.info["noBodyFrameMs"])
    return a, b


def tick_text(r):
    return "median %.3f ms, p95 %.3f, max %.3f; %d bodies, %d parts, awake at least %d; %d substeps over %d frames, most %d a frame, %s Hz; lowest %.3f, floor %.3f" % (r["median"], r["p95"], r["max"], r["bodies"], r["parts"], r["awakeLeast"], r["substepsAll"], r["n"], r["substepsMost"], r["hz"], r["lowest"], r["floor"])


def rw1(C):
    a, b = C.key_a, C.key_b
    pause(C)
    worst_m = worst_p = 0.0
    rows = []
    for name, k in (("BR-Chan", a), ("HERO107", b)):
        release_all(C)
        C.frames(3)
        build(C, k)
        C.frames(5)
        r = C.call({"cmd": "ragdollprobe.tick", "frames": 600, "warm": 120, "wake": True}, timeout=60)
        rows.append("%s: %s" % (name, tick_text(r)))
        worst_m, worst_p = max(worst_m, r["median"]), max(worst_p, r["p95"])
        if r["awakeLeast"] < 16 or r["lowest"] > r["floor"] + 1.0:
            C.R.add("RW1", "FAIL", "%s: the reading is not of a body in contact: %s" % (name, tick_text(r)))
    C.R.add("RW1", "PASS" if worst_m <= BAR["RW1"] and worst_p <= BAR["RW1p"] else "WARN", "RagdollPart.Tick, a frame of 1/60 s. " + " ".join(rows) + " Bars 0.6, 1")
    release_all(C)
    pause(C, False)


def four(C):
    """Two more characters beside the two: the keys of four, the first two as they were."""
    a, b = C.key_a, C.key_b
    c, d = load_chara(C, "female"), load_chara(C, "male")
    for k, x in ((a, -15), (b, -5), (c, 5), (d, 15)):
        C.call({"cmd": "obj.transform", "key": k, "pos": [x, 0, 0]})
    idle(C, c, 0.0)
    idle(C, d, 0.0)
    C.call({"cmd": "ragdoll.set", "values": {"room": {"center": [0, 0], "size": [60, 40, 60]}}})    # every body inside one room
    return [a, b, c, d]


def rw2(C):
    keys = four(C)
    pause(C)
    C.frames(3)
    for k in keys:
        build(C, k)
    C.frames(5)
    r = C.call({"cmd": "ragdollprobe.tick", "frames": 600, "warm": 120, "wake": True}, timeout=60)
    C.R.add("RW2", "PASS" if r["median"] <= BAR["RW2"] and r["p95"] <= BAR["RW2p"] else "WARN", "4 bodies apart: %s. Bars 1.5, 2.5" % tick_text(r))
    if r["awakeLeast"] < 64:
        C.R.add("RW2", "FAIL", "not every part awake: %d of 64" % r["awakeLeast"])
    release_all(C)
    C.frames(3)
    for i, k in enumerate(keys):
        C.call({"cmd": "obj.transform", "key": k, "pos": [0, i * 7, 0]})
        build(C, k)
        C.call({"cmd": "ragdoll.set", "key": k, "preset": "limp"})
    steps(C, 120)
    rows = []
    for rnd in range(2):
        r = C.call({"cmd": "ragdollprobe.tick", "frames": 300, "warm": 0, "wake": True}, timeout=60)
        rows.append(r)
    worst_m, worst_p = max(x["median"] for x in rows), max(x["p95"] for x in rows)
    C.R.add("RW2", "PASS" if worst_m <= BAR["RW2"] and worst_p <= BAR["RW2p"] else "WARN", "4 bodies in a pile, 2 x 300 frames: %s; %s. Bars 1.5, 2.5" % (tick_text(rows[0]), tick_text(rows[1])))
    release_all(C)
    C.frames(3)
    for k, x in zip(keys, (-15, -5, 5, 15)):
        C.call({"cmd": "obj.transform", "key": k, "pos": [x, 0, 0]})
        C.call({"cmd": "ragdoll.set", "key": k, "preset": "puppet"})
    pause(C, False)
    C.keys4 = keys


def perf_stage(C, frames):
    C.call({"cmd": "app.perf", "motion": True, "reset": True})
    C.frames(frames)
    m = C.call({"cmd": "app.perf", "motion": True})["motion"]
    return m["stages"], m["frames"]


def rw3(C):
    a = C.key_a
    keys = C.keys4 = C.keys4 or four(C)
    for k in keys:
        idle(C, k)
    C.frames(30)
    perf_stage(C, 60)          # the first request turns the timing on
    release_all(C)
    C.frames(3)
    base, _ = perf_stage(C, C.a.measure_frames)
    gone = [p for p in ("-98", "410", "956") if p in base]
    build(C, a)
    C.frames(30)
    one, n1 = perf_stage(C, C.a.measure_frames)
    for k in keys[1:]:
        build(C, k)
    C.frames(30)
    all4, n4 = perf_stage(C, C.a.measure_frames)
    g = lambda s, p: (s.get(p) or {}).get("medianMs", 0.0)
    v1, v4 = g(one, "410"), g(all4, "410")
    ok = "410" in one and "410" in all4 and not gone
    C.R.add("RW3", ("PASS" if v1 <= BAR["RW3"] and v4 <= BAR["RW3x4"] else "WARN") if ok else "FAIL", "stage 410 median: 1 body %.4f ms (p95 %.4f, %d frames), 4 bodies %.4f ms (p95 %.4f, %d frames); -98 %.4f, %.4f; 956 %.4f, %.4f; with no body the stages timed %s. Bars 0.05, 0.2"
            % (v1, (one.get("410") or {}).get("p95Ms", 0), n1, v4, (all4.get("410") or {}).get("p95Ms", 0), n4, g(one, "-98"), g(all4, "-98"), g(one, "956"), g(all4, "956"), gone), one=one, four=all4)
    release_all(C)
    C.frames(3)


def rw4(C):
    a, b = C.key_a, C.key_b
    rows, worst = [], 0.0
    for name, k in (("BR-Chan", a), ("HERO107", b)):
        v = []
        for _ in range(5):
            build(C, k)
            C.frames(2)
            v.append(body(C, k)["buildMs"])
            release_all(C)
            C.frames(2)
        worst = max(worst, max(v))
        rows.append("%s build %s" % (name, v))
    sex = {a: "female", b: "male"}
    for name, k in (("BR-Chan", a), ("HERO107", b)):
        build(C, k)
        C.frames(3)
        one = body(C, k)
        v = []
        for _ in range(3):
            C.call({"cmd": "chara.change", "key": k, "path": target_rel(C, sex[k]), "sex": sex[k]}, timeout=300)
            C.frames(5)
            v.append(body(C, k)["buildMs"])
        worst = max(worst, max(v))
        rows.append("%s after chara.change %s (world %s, one body kept %s)" % (name, v, one["worldHandle"], status(C)["world"]["bodies"] == 1))
        release_all(C)
        C.frames(2)
    C.R.add("RW4", within("RW4", worst, BAR["RW4"]), "; ".join(rows) + ". Bar 10")


def rw7(C):
    """Ticks against the same time doing nothing: Mono's heap counts every thread. A window with a collection reads nothing and is run again, three tries."""
    a = C.key_a
    pause(C)
    build(C, a)
    C.frames(5)
    tries = []
    for _ in range(3):      # each window starts after a collection and runs 1,000 frames (the lead, after the acceptance run read 3 void tries of 4,000)
        r = C.call({"cmd": "ragdollprobe.heap", "frames": 1000, "bytes": 256}, timeout=60)
        tries.append(r)
        if r["collections"] == 0 and r["baseCollections"] == 0 and r["controlCollections"] == 0:
            break
    r = tries[-1]
    clean = r["collections"] == 0 and r["baseCollections"] == 0 and r["controlCollections"] == 0
    per = (r["bytes"] - r["baseBytes"]) / float(r["frames"])
    good = r["controlBytes"] - r["baseBytes"] * r["controlMs"] / max(r["baseMs"], 1e-6) >= r["frames"] * r["controlSize"] * 0.5
    r["collections"] = sum(x["collections"] for x in tries)
    pause(C, False)
    C.call({"cmd": "ragdoll.set", "key": a, "preset": "puppet"})
    idle(C, a)
    C.frames(30)
    g0 = C.call({"cmd": "ragdollprobe.gc"})
    C.frames(C.a.measure_frames * 3)
    g1 = C.call({"cmd": "ragdollprobe.gc"})
    release_all(C)
    C.frames(30)
    g2 = C.call({"cmd": "ragdollprobe.gc"})
    C.frames(C.a.measure_frames * 3)
    g3 = C.call({"cmd": "ragdollprobe.gc"})
    with_body, without = g1["gen0"] - g0["gen0"], g3["gen0"] - g2["gen0"]
    v = ("PASS" if per <= BAR["RW7"] and with_body <= without else "WARN") if clean and good else "FAIL"
    C.R.add("RW7", v, ("" if clean else "NOT READ: every try had a collection; ") + ("" if good else "the control did not show; ") + "the same time doing nothing grows %.2f MB a second on this player, source unknown; ticks: the heap moved" % (r["baseBytes"] / 1048576.0 / max(r["baseMs"] / 1000.0, 1e-6)) + " %d B over %d frames in %.0f ms, the same time doing nothing %d B: %.1f B a frame beyond it; a %d-byte array a frame in %.0f ms moves it %d B; %d tries, the read one with no collection %s; real frames: %d collections over %d with a body, %d over %d with none. Bars 256 B, no collection beyond the base"
            % (r["bytes"], r["frames"], r["ms"], r["baseBytes"], per, r["controlSize"], r["controlMs"], r["controlBytes"], len(tries), clean, with_body, g1["frame"] - g0["frame"], without, g3["frame"] - g2["frame"]), tries=tries)


def rw8(C):
    r = C.call({"cmd": "ragdollprobe.fx", "steps": 300}, timeout=60)
    x = r["both"]
    ok = r["droplets"] >= 2940 and r["splats"] >= 400
    C.R.add("RW8", within("RW8", x["median"], BAR["RW8"]) if ok else "FAIL", "at least %d droplets, %d splats: step median %.4f ms, mesh median %.4f; both median %.4f, p95 %.4f, max %.4f. Bar 0.5" % (r["droplets"], r["splats"], r["step"]["median"], r["mesh"]["median"], x["median"], x["p95"], x["max"]), raw=r)


def rw9_10(C):
    a = C.key_a
    pause(C)
    build(C, a)
    C.frames(5)
    r = C.call({"cmd": "ragdollprobe.pick", "key": a, "n": 2000}, timeout=60)
    ok = r["hit"] >= 200
    C.R.add("RW10", within("RW10", r["median"], BAR["RW10"]) if ok else "FAIL", "RagdollWorld.Pick, %d rays, %d hit: median %.4f ms, p95 %.4f, max %.4f. Bar 0.1" % (r["n"], r["hit"], r["median"], r["p95"], r["max"]), raw=r)
    r = C.call({"cmd": "ragdollprobe.bullet", "key": a, "n": 300}, timeout=60)
    ok = r["hit"] >= r["n"] * 0.9
    C.R.add("RW9", within("RW9", r["median"], BAR["RW9"]) if ok else "FAIL", "a bullet through a body, %d shots, %d hit, %d blows, %d droplets at the end: median %.4f ms, p95 %.4f, max %.4f, first %.4f. Bar 0.2" % (r["n"], r["hit"], r["blows"], r["droplets"], r["median"], r["p95"], r["max"], r["first"]), raw=r)
    release_all(C)
    C.frames(3)
    pause(C, False)


def iqr(v):
    s = sorted(v)
    q = statistics.quantiles(s, n=4, method="inclusive")
    return q[0], q[1], q[2]


def rw11(C):
    """The first world of the process, so that what a world makes once is counted: none, one body, four bodies, ten cycles, each read after a collection and a frame; medians and
    interquartile ranges. An interquartile range wider than the bar cannot judge it: NOT READ, with the arithmetic as the estimate."""
    keys = C.keys4 = C.keys4 or four(C)
    release_all(C)
    C.frames(5)
    c0 = C.call({"cmd": "ragdollprobe.memory", "control": True})
    size = c0["controlSize"]
    meters = c0["controlNative"] >= size * 0.9 and c0["controlManaged"] >= size * 0.9
    pause(C)
    total = lambda m: m["managed"] + m["native"]
    one, four_ = [], []
    p = {}
    worlds, pers = [], []
    for _ in range(10):
        C.frames(1)
        none = C.call({"cmd": "ragdollprobe.memory"})
        build(C, keys[0])
        C.frames(1)
        m1 = C.call({"cmd": "ragdollprobe.memory"})
        for k in keys[1:]:
            build(C, k)
        C.frames(1)
        m4 = C.call({"cmd": "ragdollprobe.memory"})
        p = body(C, keys[0]).get("patch") or {}
        release_all(C)
        C.frames(3)
        one.append(total(m1) - total(none))
        four_.append(total(m4) - total(none))
        pers.append((four_[-1] - one[-1]) / 3.0)
        worlds.append(one[-1] - pers[-1])
    mb = 2.0 ** 20
    per = statistics.median(pers)
    world = statistics.median(worlds)
    q1, q4, qp, qw = iqr(one), iqr(four_), iqr(pers), iqr(worlds)
    spread = qp[2] - qp[0]
    n = 65 * 65
    mesh = n * 12 + (p.get("quads") or 0) * 6 * 4 + n * 4 + n   # vertices, indices, heights, answered
    buffers = n * 12 * 2 + n * 24 + 64 * 64 * 6 * 4                # per world: points, vertices, hits (float, Vector3, a reference), triangles
    readable = spread <= BAR["RW11"]
    v = "FAIL" if not meters or not p.get("found") or not readable else ("PASS" if per <= BAR["RW11"] else "WARN")
    f = lambda q: "%.3f (%.3f to %.3f)" % (q[1] / mb, q[0] / mb, q[2] / mb)
    C.R.add("RW11", v, "%sthe process's first world, 10 cycles of 4 builds and a full release, each read after a collection and a frame, managed and native, median (interquartile range) in MB: one body %s, four %s, a body %s, the world beside its bodies %s; the range of a body %.3f MB against the bar. By arithmetic, the estimate: a patch of %s points and %s quads holds %.3f MB of mesh and arrays, its collider not counted apart; the query buffers of a world %.3f MB; meters: 1 MiB moved native %d B, managed %d B. Bar 1 MB a body"
            % ("" if readable else "NOT READ: the interquartile range is wider than the bar; ", f(q1), f(q4), f(qp), f(qw), spread / mb, p.get("found"), p.get("quads"), mesh / mb, buffers / mb, c0["controlNative"], c0["controlManaged"]), one=one, four=four_)
    pause(C, False)


def rw12(C):
    a = C.key_a
    idle(C, a)
    C.frames(30)

    def off():
        release_all(C)
        C.frames(5)

    def on():
        build(C, a)
        C.frames(5)

    x, y, d, xs, ys = ab(C, off, on)
    w = status(C)["world"]
    C.R.add("RW12", ("PASS" if d <= BAR["RW12"] else "WARN") if w.get("bodies") == 1 and w.get("hz") == 240 else "FAIL", "1 active body against none, %s Hz: off %.3f, on %.3f ms: +%.3f (rounds %s, %s), %s substeps so far. Bar 0.3" % (w.get("hz"), x, y, d, xs, ys, w.get("steps")))
    release_all(C)
    C.frames(3)


def rd6(C):
    rows = C.call({"cmd": "ragdollprobe.shaders"})["rows"]
    good = [r["name"] for r in rows if r["found"] and r["supported"] and r["passes"] > 0]
    C.R.add("RD6", "PASS" if len(good) == 2 else "FAIL", "Shader.Find in the player: %s" % json.dumps(rows))


def run_b(C):
    C.call({"cmd": "wetprobe.frames", "vsync": 0, "fps": -1})
    f = C.call({"cmd": "wetprobe.frames"})
    C.info["frames"] = f
    C.R.add("FRAMES", "PASS" if f.get("vsync") == 0 else "FAIL", "the frame cap off: %s" % json.dumps(f))
    if C.a.want("RD6") or C.a.want("B"):
        guard(C, "RD6", rd6)
    new_map(C, no=HOME_MAP)
    guard(C, "RW6", rw6)
    for cid, fn in (("RW11", rw11), ("RW1", rw1), ("RW2", rw2), ("RW3", rw3), ("RW4", rw4), ("RW7", rw7), ("RW8", rw8), ("RW9", rw9_10), ("RW12", rw12)):
        if C.a.want(cid) or C.a.want("B"):
            guard(C, cid, fn)


# ---------------------------------------------------------------- session M: the 50 maps

def surface_under(C, x, y, z):
    r = C.call({"cmd": "ground.probe", "points": [[x, z]], "y": y, "above": 0, "below": 60})
    return r["hits"][0].get("y"), r.get("source")


def rl4_map(C, ref):
    """S0-6's drop: a limp body 4 units over the surface under the origin, 3 s of steps."""
    C.call({"cmd": "scene.new"})
    t0 = time.time()
    r = C.call({"cmd": "map.load", "ref": ref, "wait": True}, timeout=600)
    row = {"ref": ref, "loadSec": round(time.time() - t0, 2), "load": r.get("state")}     # "state" below is the body's
    if r.get("state") != "Loaded":
        return row
    C.frames(3)
    y0, source = surface_under(C, 0, 12, 0)
    row["surface"], row["source"] = y0, source
    base = y0 if y0 is not None else 0.0
    k = load_chara(C, "female")
    idle(C, k, 0.0)
    C.call({"cmd": "obj.transform", "key": k, "pos": [0, base + RL4_DROP, 0]})
    pause(C)
    C.frames(3)
    build(C, k)
    C.call({"cmd": "ragdoll.set", "key": k, "preset": "limp"})
    steps(C, RL4_STEPS)
    b = body(C, k)
    row.update({x: b.get(x) for x in ("lowest", "speed", "pelvis", "floor", "floorSource", "state", "patch", "awake")})
    gaps = []
    for p in b["parts"]:
        s, _ = surface_under(C, p["pos"][0], p["lowest"] + 1.0, p["pos"][2])
        if s is not None:
            gaps.append((abs(p["lowest"] - s), p["key"], round(s, 3), p["lowest"]))
    row["answers"] = len(gaps)
    row["gap"] = min(gaps) if gaps else None
    row["under"] = round(b["floor"] - min(p["lowest"] for p in b["parts"]), 4)
    row["finite"] = all(p["finite"] for p in b["parts"])
    release_all(C)
    pause(C, False)
    return row


def rl4(C):
    rows, bad, known = [], [], []
    for i, ref in enumerate(MAPS):
        try:
            row = rl4_map(C, ref)
        except Exception as e:
            if C.aborted:
                raise
            row = {"ref": ref, "error": "%s: %s" % (type(e).__name__, e)}
        rows.append(row)
        p = row.get("patch") or {}
        why = []
        if row.get("error") or row.get("load") != "Loaded":
            why.append(row.get("error") or "not loaded")
        else:
            if row.get("surface") is None:
                why.append("no surface under the origin")
            if row.get("source") != "gpu" or not p.get("found") or p.get("gpu") != p.get("found"):
                why.append("source %s, patch %s of %s from the device" % (row.get("source"), p.get("gpu"), p.get("found")))
            if not row.get("gap") or row["gap"][0] > RL4_REACH:
                why.append("no part within %.1f of a surface: nearest %s" % (RL4_REACH, row.get("gap")))
            if row.get("under", 0) > RL4_UNDER:
                why.append("a part %.3f under the room's floor" % row["under"])
            if not row.get("finite"):
                why.append("a part's place is no number")
        if why:
            (known if ref in RL4_KNOWN else bad).append("%s: %s" % (ref, "; ".join(why)))
        log("RL4 %d of %d %s: %s" % (i + 1, len(MAPS), ref, "; ".join(why) or "lands, gap %s" % (row.get("gap") or (None,))[0]))
    done = [r for r in rows if r.get("patch")]
    found = sum(1 for r in rows if r.get("surface") is not None)
    never = [m for m in RL4_KNOWN if not any(x.startswith(m) for x in known)]
    ok = not bad and len(known) <= RL4_MAX_KNOWN and found == len(MAPS) and len(done) == len(MAPS)
    C.R.add("RL4", "PASS" if ok else "FAIL", "%d maps: a surface under the origin on %d, a patch on %d, %d land by the bar; known cases %d of at most %d (%s); known cases that did not occur in this run: %s; failures %d (%s)"
            % (len(MAPS), found, len(done), len(done) - len(known) - len(bad), len(known), RL4_MAX_KNOWN, "; ".join(known) or "none", ", ".join(never) or "none", len(bad), "; ".join(bad[:8])), rows=rows)
    q = [r["patch"]["queryMs"] for r in done]
    m = [r["patch"]["meshMs"] for r in done]
    if q:
        qm, qp, qx = stats(q)
        mm, _, mx = stats(m)
        worst = max(done, key=lambda r: r["patch"]["queryMs"])
        named = next((r["patch"]["queryMs"] for r in done if r["ref"] == "illusion.hs2#17"), None)
        v = "PASS" if qm <= BAR["RW5"] and qx <= BAR["RW5max"] and mx <= BAR["RW5mesh"] else "WARN"
        C.R.add("RW5", v, "the patch over %d maps: query median %.2f ms, p95 %.2f, max %.2f (%s); illusion.hs2#17 %s ms; mesh and collider median %.2f, max %.2f. Bars median 40, max 100; 5" % (len(q), qm, qp, qx, worst["ref"], named, mm, mx))


def item_bounds(C, key):
    return C.call({"cmd": "ragdollprobe.item", "key": key})


def obstacle_case(C, ref, label):
    C.call({"cmd": "scene.new"})
    pause(C)
    k = load_chara(C, "female")
    idle(C, k, 0.0)
    C.call({"cmd": "ragdoll.set", "values": {"floor.mode": "fixed", "floor.height": -30, "room": {"size": [120, 100, 120]}}})
    item = C.call({"cmd": "item.add", "ref": ref})["added"][0]["key"]
    C.frames(2)
    b = item_bounds(C, item)
    scale = 16.0 / max(0.01, min(b["size"][0], b["size"][2]))
    C.call({"cmd": "obj.transform", "key": item, "scale": [scale] * 3})
    C.frames(2)
    b = item_bounds(C, item)
    C.call({"cmd": "obj.transform", "key": item, "pos": [-b["center"][0], -b["max"][1], -b["center"][2]]})
    C.frames(2)
    b = item_bounds(C, item)

    def drop():
        C.call({"cmd": "obj.transform", "key": k, "pos": [0, 2, 0]})
        C.frames(2)
        build(C, k)
        C.call({"cmd": "ragdoll.set", "key": k, "preset": "limp"})
        steps(C, 150)
        return body(C, k)["pelvis"][1]

    past = drop()
    C.call({"cmd": "ragdoll.release", "key": k})
    C.frames(2)
    C.call({"cmd": "ragdoll.obstacle", "key": item})
    on = drop()
    o = C.call({"cmd": "ragdollprobe.obstacle", "key": item})
    release_all(C)
    pause(C, False)
    ok = on > past + 5 and on > b["min"][1] and (past < b["min"][1] - 1 or past < -20) and o.get("found")
    C.R.add("OBST", "PASS" if ok else "FAIL", "%s (%s): %d renderers, %d meshes readable in this player, %d vertices; without it the pelvis at %.2f, with it %.2f, the item %.2f to %.2f; it became %s" % (ref, label, b["renderers"], b["readable"], b["vertices"], past, on, b["min"][1], b["max"][1], json.dumps(o)), bounds=b)


def obst(C):
    obstacle_case(C, CUBE, "a cube in the Editor: one readable mesh")
    obstacle_case(C, NO_MESH_ITEM, "no readable mesh in the Editor")


def run_m(C):
    C.call({"cmd": "wetprobe.frames", "vsync": 0, "fps": -1})
    for cid, fn in (("RL4", rl4), ("OBST", obst)):
        if C.a.want(cid) or C.a.want("M"):
            guard(C, cid, fn)


# ---------------------------------------------------------------- session V: RV6

def look(c, d, dist):
    """camera.set's rot for a camera at c + d x dist looking at c."""
    pitch, yaw = math.degrees(math.asin(d[1])), math.degrees(math.atan2(-d[0], -d[2]))
    return [pitch, yaw, 0]


def norm(v):
    n = math.sqrt(sum(x * x for x in v)) or 1.0
    return [x / n for x in v]


def nine(n):
    """The member's own view, four 40 degrees off it and four 70 degrees off it, as RV3's."""
    up = [0.0, 1.0, 0.0] if abs(n[1]) < .9 else [1.0, 0.0, 0.0]
    x = norm([n[1] * up[2] - n[2] * up[1], n[2] * up[0] - n[0] * up[2], n[0] * up[1] - n[1] * up[0]])
    y = [n[1] * x[2] - n[2] * x[1], n[2] * x[0] - n[0] * x[2], n[0] * x[1] - n[1] * x[0]]
    out = [list(n)]
    for deg in (40, 70):
        c, s = math.cos(math.radians(deg)), math.sin(math.radians(deg))
        for a, sign in ((x, 1), (x, -1), (y, 1), (y, -1)):
            out.append(norm([c * n[i] + sign * s * a[i] for i in range(3)]))
    return out


class Rv:
    """What the walks of one character share."""
    def __init__(self, who, me, other, member, plane, floor):
        self.who, self.me, self.other, self.member, self.plane, self.floor = who, me, other, member, plane, floor


def rv_member(C, v):
    rows = C.call({"cmd": "wetprobe.members", "key": v.me, "width": RV_W, "height": RV_H, "plane": v.plane})["rows"]
    m = next((x for x in rows if x["name"] == v.member), None)
    if m is None or not m.get("centroid"):
        raise RuntimeError("no member %s among %s" % (v.member, [x["name"] for x in rows]))
    c, n = m["centroid"], norm(m["normal"])
    if v.who == "HERO107":   # a procedural member's region has no surface to look along: from below and in front of the hips, as WV3c
        h = C.call({"cmd": "chara.bones", "key": v.me, "names": ["cf_J_Hips"]})["bones"]["cf_J_Hips"]
        f = norm(h.get("forward") or [0, 0, 1])
        n = norm([0.6 * f[0], -0.8 + 0.6 * f[1], 0.6 * f[2]])
    return c, n


def members(C, v, hide):
    C.call({"cmd": "wetprobe.members", "key": v.me, "width": RV_W, "height": RV_H, "plane": v.plane, "hide": hide})
    C.frames(2)


def masked(C, v, within_mirror=False):
    """Renders with the member drawn and undrawn: the mask in px; within the mirror's area, against the render `off`."""
    render(C, "sync", width=RV_W, height=RV_H)
    members(C, v, True)
    render(C, "nomember", width=RV_W, height=RV_H)
    members(C, v, False)
    q = {"cmd": "wetprobe.compare", "a": "sync", "b": "nomember", "threshold": 2}
    if within_mirror:
        q.update({"within": "sync", "withinOf": "off"})
    return C.call(q)["differ"]


def paths(C):
    """RenderAsync against RenderSync and two RenderSync on a stopped clock."""
    render(C, "sync", width=RV_W, height=RV_H)
    render(C, "async", True, width=RV_W, height=RV_H)
    render(C, "sync2", width=RV_W, height=RV_H)
    return C.call({"cmd": "wetprobe.compare", "a": "sync", "b": "async", "threshold": 2}), C.call({"cmd": "wetprobe.compare", "a": "sync", "b": "sync2", "threshold": 2})


def wide(C, on):
    C.call({"cmd": "app.options", "render": {"mosaicWide": on}})
    C.frames(3)


def rv_view(C, v, c, n):
    """The moment in the view: at 10, then 7, then 5 units, the first of the nine directions with a mask at the floor, by the mask alone, before any comparison;
    (mask, comparisons, distance) or (best mask, None, None)."""
    best = 0
    for dist in RV_DISTANCES:
        for d in nine(n):
            C.call({"cmd": "camera.set", "target": c, "rot": look(c, d, dist), "dist": dist, "fov": 23})
            C.frames(2)
            mask = masked(C, v)
            best = max(best, mask)
            if mask >= RV_MASK_MIN:
                same = list(paths(C))
                wide(C, True)
                same += list(paths(C))
                wide(C, False)
                return mask, same, dist
    return best, None, None


def rv_mirror(C, v, c, n):
    """The moment in a puddle's mirror: the virtual camera under the floor along a direction of the nine, the real one its image; a puddle where the ray meets the floor."""
    best, tried = 0, 0
    for d in nine(n):
        below = [c[i] + d[i] * RV_VIEW for i in range(3)]
        if below[1] >= v.plane - 0.5:
            continue
        tried += 1
        view = [below[0], 2 * v.plane - below[1], below[2]]
        target = [c[0], 2 * v.plane - c[1], c[2]]
        f = norm([target[i] - view[i] for i in range(3)])
        t = (view[1] - v.floor) / max(view[1] - target[1], 1e-3)
        hit = [view[i] + (target[i] - view[i]) * t for i in range(3)]
        C.call({"cmd": "wetprobe.puddle", "key": v.other, "x": hit[0], "y": v.floor, "z": hit[2], "area": 200, "clear": True})
        C.call({"cmd": "camera.set", "target": target, "rot": [math.degrees(math.asin(-f[1])), math.degrees(math.atan2(f[0], f[2])), 0], "dist": RV_VIEW, "fov": 23})
        C.frames(4)
        mirror_on(C, (v.other,), False)
        render(C, "off", width=RV_W, height=RV_H)
        mirror_on(C, (v.other,), True)
        C.frames(2)
        mask = masked(C, v, True)
        best = max(best, mask)
        if mask >= RV_MASK_MIN:
            same = list(paths(C))
            wide(C, True)
            same += list(paths(C))
            wide(C, False)
            return mask, same, tried
    return best, None, tried


def rv_floor(C, v, label):
    """A body on a floor with puddles, the walk's last moment: the view on the puddle round the pelvis; the body in the mirror's area as Present."""
    p = pelvis(C, v.me)
    C.call({"cmd": "wetprobe.puddle", "key": v.other, "x": p[0], "y": v.floor, "z": p[2], "area": 300, "clear": True})
    C.call({"cmd": "camera.set", "target": [p[0], v.floor, p[2]], "rot": [38, 180, 0], "dist": 30, "fov": 30})
    C.frames(4)
    mirror_on(C, (v.other,), False)
    render(C, "off", width=RV_W, height=RV_H)
    mirror_on(C, (v.other,), True)
    C.frames(2)
    render(C, "sync", width=RV_W, height=RV_H)
    C.call({"cmd": "obj.visible", "key": v.me, "visible": False})
    C.frames(3)
    render(C, "gone", width=RV_W, height=RV_H)
    C.call({"cmd": "obj.visible", "key": v.me, "visible": True})
    C.frames(3)
    shown = C.call({"cmd": "wetprobe.compare", "a": "sync", "b": "gone", "threshold": 2, "within": "sync", "withinOf": "off"})["differ"]
    a, s = paths(C)
    ok = shown >= RV_BODY_MIRROR_MIN and a["differ"] == 0 and s["differ"] == 0
    C.R.add("RV6F", "PASS" if ok else "FAIL", "%s: a body on a floor with a puddle: the body %d px in the mirror's area (at least %d); RenderAsync against RenderSync %d px, two RenderSync %d" % (label, shown, RV_BODY_MIRROR_MIN, a["differ"], s["differ"]))


def rv_walk(C, v, kind):
    label = "%s, %s" % (v.who, kind)
    release_all(C)
    C.call({"cmd": "obj.transform", "key": v.me, "pos": [0, 0, 0], "rot": [0, 0, 0]})
    C.frames(3)
    build(C, v.me)
    C.call({"cmd": "ragdoll.set", "key": v.me, "preset": "limp" if kind == "limp" else "puppet"})   # a block's values outlive a release
    view, mirror, skipped, skipped_m, worst, dists = [], [], [], [], 0, []
    at0 = pelvis(C, v.me)
    moved = 0.0
    for i in range(RV_MOMENTS):
        if i:
            steps(C, RV_LIMP_FRAMES if kind == "limp" else RV_ACTIVE_FRAMES)
        if kind == "limp" and i == 1:
            C.call({"cmd": "ragdoll.push", "key": v.me, "part": "chest", "local": [0, 0, -150]})
        if kind == "active" and i == 2:
            C.call({"cmd": "ragdoll.push", "key": v.me, "part": "chest", "local": [0, 0, -300]})
        if kind == "active" and i == 3:
            C.call({"cmd": "ragdoll.knock", "key": v.me})
        c, n = rv_member(C, v)
        moved = max(moved, distance(pelvis(C, v.me), at0))
        mask, same, dist = rv_view(C, v, c, n)
        if same is None:
            skipped.append("m%02d %d" % (i, mask))
        else:
            view.append(mask)
            dists.append("m%02d %g" % (i, dist))
            differ = [x["differ"] for x in same]
            if any(differ):
                worst = max(worst, max(differ))
                C.R.add("RV6", "FAIL", "%s, moment %d in the view: mask %d, RenderAsync against RenderSync %s, two RenderSync %s, wide on the same, %s" % (label, i, mask, differ[0::2], differ[1::2], json.dumps({k: same[0].get(k) for k in ("box", "most", "onMember", "outside")})))
        mask, same, tried = rv_mirror(C, v, c, n)
        if same is None:
            skipped_m.append("m%02d %d of %d views" % (i, mask, tried))
        else:
            mirror.append(mask)
            differ = [x["differ"] for x in same]
            if any(differ):
                worst = max(worst, max(differ))
                C.R.add("RV6", "FAIL", "%s, moment %d in the mirror: mask %d, RenderAsync against RenderSync %s, two RenderSync %s, wide on the same, %s" % (label, i, mask, differ[0::2], differ[1::2], json.dumps({k: same[0].get(k) for k in ("box", "most", "onMember", "outside")})))
        C.call({"cmd": "wetprobe.forget"})
    ok = len(view) >= RV_LEAST_JUDGED and worst == 0 and moved >= 5
    near = [x for x in dists if not x.endswith(" 10")]
    C.R.add("RV6", "PASS" if ok else "FAIL", "%s, the view: %d of %d moments judged (at least %d), masks %s-%s px (at least %d), the pelvis moved %.1f; 4 comparisons each at 0 px %s; distances 10, 7, 5 in order: %d moments needed 7 or 5 (%s); not judged, best mask over all three: %s"
            % (label, len(view), RV_MOMENTS, RV_LEAST_JUDGED, min(view, default=0), max(view, default=0), RV_MASK_MIN, moved, worst == 0, len(near), ", ".join(near) or "none", ", ".join(skipped) or "none"), distances=dists)
    ok = len(mirror) >= RV_LEAST_MIRROR and worst == 0
    C.R.add("RV6", "PASS" if ok else "FAIL", "%s, the mirror: %d of %d moments judged (at least %d), masks in the mirror's area %s-%s px (at least %d); not judged: %s" % (label, len(mirror), RV_MOMENTS, RV_LEAST_MIRROR, min(mirror, default=0), max(mirror, default=0), RV_MASK_MIN, ", ".join(skipped_m) or "none"))
    rv_floor(C, v, label)
    release_all(C)
    C.call({"cmd": "wetprobe.forget"})


def rv6(C):
    for who in ("BR-Chan", "HERO107"):
        new_map(C, no=HOME_MAP)
        a, b = two(C)
        me, other = (a, b) if who == "BR-Chan" else (b, a)
        for k in (a, b):
            C.call({"cmd": "chara.face", "key": k, "blink": False})
            C.call({"cmd": "wet.attach", "key": k, "preset": "water"}, timeout=60)
        C.frames(5)
        C.call({"cmd": "obj.transform", "key": me, "pos": [0, 0, 0]})
        C.call({"cmd": "obj.transform", "key": other, "pos": [60, 0, 60]})
        C.call({"cmd": "chara.clothes", "key": me, "all": 2})
        if who == "HERO107":
            C.call({"cmd": "chara.state", "key": me, "set": {"son": {"visible": True}}})
        idle(C, me, 1.0)
        C.frames(10)
        floor = C.call({"cmd": "wet.floor", "key": me})["y"]
        v = Rv(who, me, other, "cm_o_dan00" if who == "HERO107" else "o_mnpb", floor + 0.02, floor)
        C.call({"cmd": "wetprobe.puddle", "key": me, "area": 0, "clear": True})
        pause(C)
        C.frames(5)
        for kind in ("limp", "active"):
            rv_walk(C, v, kind)
        pause(C, False)
    C.call({"cmd": "app.options", "render": {"mosaicWide": False}})


def run_v(C):
    C.call({"cmd": "wetprobe.frames", "vsync": 0, "fps": -1})
    guard(C, "RV6", rv6)


# ---------------------------------------------------------------- no player

def rv8(a, R):
    """No switch in the new files; the option reads effects only. WV7's search."""
    root = os.path.join(a.project, "Assets", "HS2Next")
    files = []
    for rel in ("Runtime/Scene/Ragdoll/*.cs", "Runtime/Scene/Runtime/Ragdoll/*.cs", "Runtime/UI/Panels/Ragdoll/*.cs", "Runtime/Motion/AdvancedPose/RagdollStages.cs", "Runtime/App/Commands/RagdollCommands.cs", "Runtime/Scene/Runtime/Ops/StudioOps.Ragdoll.cs",
                "Runtime/Save/Documents/SceneRagdoll.cs", "Runtime/Scene/Commands/Ragdoll*EditCommand.cs", "Runtime/Scene/Runtime/CharaStages.cs", "ApiUnity/CharaStages.cs", "Runtime/App/Plugins/Engine/PluginCharaStages.cs"):
        files += glob.glob(os.path.join(root, rel))
    switch = re.compile(r"GetEnvironmentVariable|GetCommandLineArgs|PlayerPrefs|EditorPrefs|AppSettings|#if\s|#elif\s")
    mosaic = re.compile(r"Mosaic|AdvancedPoseGuard")
    blood = re.compile(r"ShowBlood|studio\.blood|OptionKey")
    hits, rule, reads = [], [], []
    for path in files:
        rel = os.path.relpath(path, root).replace("\\", "/")
        with open(path, encoding="utf-8") as f:
            for no, line in enumerate(f, 1):
                code = line.split("//")[0]
                if switch.search(code):
                    hits.append("%s:%d" % (rel, no))
                if mosaic.search(code):
                    rule.append("%s:%d" % (rel, no))
                if blood.search(code):
                    reads.append("%s:%d" % (rel, no))
    outside = [x for x in reads if not re.match(r"Runtime/Scene/Runtime/Ragdoll/Ragdoll(Fx|Shots)\.cs|Runtime/UI/Panels/Ragdoll/", x)]
    ok = len(files) >= 20 and bool(reads) and not outside and not hits
    R.add("RV8", "PASS" if ok else "FAIL", "%d new files searched: what reads a setting, an argument or a variable: %s; lines naming the rule's classes (read by hand): %s; the blood option: %d lines, outside the effects and the panel %s" % (len(files), hits or "none", rule or "none", len(reads), outside or "none"), reads=reads)


def rd13(a, R):
    exe_dir = os.path.dirname(a.exe)
    report = os.path.join(os.path.dirname(exe_dir), os.path.basename(exe_dir) + ".build_report.txt")
    if not os.path.isfile(report):
        R.add("RD13", "FAIL", "no build report: %s" % report)
        return
    with open(report, encoding="utf-8", errors="replace") as f:
        text = f.read()
    m = re.search(r"shader programs: \d+ after built-in stripping -> (\d+) after scriptable", text)
    set_line = next((l for l in text.splitlines() if l.startswith("shader set ")), "")
    base = BASE_PROGRAMS
    if a.baseline_report and os.path.isfile(a.baseline_report):
        with open(a.baseline_report, encoding="utf-8", errors="replace") as f:
            b = re.search(r"shader programs: \d+ after built-in stripping -> (\d+) after scriptable", f.read())
        base = int(b.group(1)) if b else base
    p = subprocess.run(["git", "-C", a.project, "diff", "--stat", BASE_COMMIT, "--", SHADER_SET, "Assets/HS2Next/Shaders"], capture_output=True, timeout=120, creationflags=CREATE_NO_WINDOW)
    diff = p.stdout.decode("utf-8", "replace").strip()
    programs = int(m.group(1)) if m else None
    ok = programs == base and "unchanged" in set_line and not diff and p.returncode == 0
    R.add("RD13", "PASS" if ok else "FAIL", "the build's report: %d programs against %d; %s; git diff of the set and of Shaders against %s: %s" % (programs or 0, base, set_line[:110], BASE_COMMIT, diff or "none"))


# ---------------------------------------------------------------- main

def write_reports(a, R, exit_code, started):
    n_pass, n_fail, n_warn = R.count("PASS"), R.count("FAIL"), R.count("WARN")
    summary = "SUMMARY: %d PASS, %d FAIL" % (n_pass, n_fail)
    warns = [x["id"] for x in R.items if x["verdict"] == "WARN"]
    lines = [summary, "WARN: %d%s" % (n_warn, (" (" + ", ".join(warns) + ")") if warns else ""), "ragdoll_player_check %s; exe %s; exit code %d; %.0f s" % (started, a.exe, exit_code, time.perf_counter() - T0)]
    for x in R.items:
        lines.append("%-7s %-4s  %s" % (x["id"], x["verdict"], x["text"]))
    lines.append(summary)
    with open(os.path.join(a.out, "ragdoll_player_report.txt"), "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")
    with open(os.path.join(a.out, "ragdoll_player_check.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump({"started": started, "exe": a.exe, "exitCode": exit_code, "summary": summary, "checks": R.items}, f, ensure_ascii=False, indent=1, default=str)


def frames_of(a, R, exe, label, port, deadline):
    """RW6a: the frame of a scene with no body on an older player, the test plugin of frames only (Tests\\WetProbe's FramesOnly build)."""
    from wet_player_check import build_probe
    C = Session(a, R, "O" + label, port, exe)
    try:
        C.a.plugins = build_probe(a, C.out, True)
        start(C, "B", deadline)
        C.call({"cmd": "wetprobe.frames", "vsync": 0, "fps": -1})
        new_map(C, no=HOME_MAP)
        two(C)
        C.frames(60)
        rows = [frame_ms(C, a.measure_frames)[0] for _ in range(a.rounds)]
        stop(C)
        return rows
    finally:
        kill(C)


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
    ap.add_argument("--port", type=int, default=17869)
    ap.add_argument("--port-b", type=int, default=17878)
    ap.add_argument("--port-c", type=int, default=17868)
    ap.add_argument("--timeout-min", type=float, default=60)
    ap.add_argument("--only", default="")
    ap.add_argument("--measure-frames", type=int, default=400)
    ap.add_argument("--rounds", type=int, default=3)
    ap.add_argument("--baseline-exe", default=None)
    ap.add_argument("--baseline-report", default=None)
    ap.add_argument("--gfx", choices=["d3d11", "d3d12"], default=None)
    ap.add_argument("--min-free-gb", type=float, default=8)
    ap.add_argument("--ram-wait-min", type=float, default=10)
    ap.add_argument("--ready-timeout-min", type=float, default=12)
    a = ap.parse_args(argv)
    a.exe, a.project, a.out = os.path.abspath(a.exe), os.path.abspath(a.project), os.path.abspath(a.out)
    only = set(x.strip().upper() for x in a.only.split(",") if x.strip())
    a.want = lambda c: not only or c.upper() in only
    a.session_on = lambda letter, ids: not only or letter in only or bool(only & set(i.upper() for i in ids))
    a.rk1 = []
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

    def session(letter, port, kind, body_):
        nonlocal exit_code
        C = Session(a, R, letter, port)
        C.keys4 = []
        live.append(C)
        try:
            start(C, kind, deadline)
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
        finally:
            kill(C)
            try:
                check_log(C)
            except Exception as e:
                R.add("LOG" + letter, "WARN", "player.log not read: %s" % e)
            live.remove(C)

    try:
        gate_warning = game_processes()[1]
        if gate_warning:
            R.add("GATE", "WARN", gate_warning)
        games = running_games()
        if games:
            R.add("SETUP", "WARN", "the game gate: %s runs; no player row now" % ", ".join(games))
            exit_code = 3
        need, end = a.min_free_gb, time.time() + a.ram_wait_min * 60
        g = free_gb()
        while exit_code == 0 and g is not None and g < need and time.time() < end:
            log("free RAM %.1f GB < %.1f GB: waiting" % (g, need))
            time.sleep(15)
            g = free_gb()
        if a.want("RV8"):
            rv8(a, R)
        if a.want("RD13"):
            rd13(a, R)
        a.plugins = None
        if exit_code == 0 and (not only or only & {"R", "K", "F", "B", "M", "V", "TRIP", "RK1", "FIRST", "RD6", "RL4", "RW5", "OBST", "RV6"} or any(x.startswith("RW") for x in only)):
            a.plugins = build_probes(a)
        if exit_code == 0 and a.session_on("A", ["CMDS", "SHADERS"]):
            session("A", a.port, "A", run_a)
        if exit_code == 0 and a.session_on("R", ["TRIP"]):
            session("R", a.port_c, "K", lambda C: guard(C, "TRIP", trip))
        if exit_code == 0 and a.session_on("K", ["RK1"]):
            session("K1", a.port_c, "K", lambda C: guard(C, "RK1", rk1))
            session("K2", a.port_c, "K", lambda C: guard(C, "RK1", rk1))
            starts = set(x for s_, _ in a.rk1 for x in s_)
            ends = set(e for _, e in a.rk1)
            same = len(a.rk1) == 2 and len(starts) == 1 and None not in starts and len(ends) == 1
            R.add("RK1", "PASS" if same else "FAIL", "across 2 processes, UI off in both: the starts of all 10 runs %s, the ends %s (an end is comparable with the Editor's only under the same stepping: here scene.step n 1, 180 times)" % (sorted(starts, key=str), sorted(ends)))
        if exit_code == 0 and a.session_on("F", ["FIRST"]):
            for i, (mode, with_body) in enumerate((("half", True), ("half", False), ("capture", True), ("capture", False))):
                session("F%d" % (i + 1), a.port_c, "B", run_first(mode, with_body))
        old1 = []
        if exit_code == 0 and a.baseline_exe:
            old1 = frames_of(a, R, os.path.abspath(a.baseline_exe), "1", a.port_b, deadline)
        if exit_code == 0 and a.session_on("B", ["RD6", "RW1", "RW2", "RW3", "RW4", "RW6", "RW7", "RW8", "RW9", "RW10", "RW11", "RW12"]):
            session("B", a.port_b, "B", run_b)
        if exit_code == 0 and a.baseline_exe:
            old2 = frames_of(a, R, os.path.abspath(a.baseline_exe), "2", a.port_b, deadline)
            rounds = next((x["data"]["rounds"] for x in R.items if x["id"] == "RW6f"), None)
            if rounds:
                before = statistics.median(old1 + old2)
                d = statistics.median(rounds) - before
                R.add("RW6a", "PASS" if d <= BAR["RW6a"] else "WARN", "no body, the frame: this player %.3f ms (%s), the last build %.3f ms (before %s, after %s): %+.3f. Bar 0.05" % (statistics.median(rounds), rounds, before, old1, old2, d))
        if exit_code == 0 and a.session_on("M", ["RL4", "RW5", "OBST"]):
            session("M", a.port_b, "B", run_m)
        if exit_code == 0 and a.session_on("V", ["RV6"]):
            session("V", a.port_b, "B", run_v)
    except Exception as e:
        R.add("RUN", "FAIL", "%s: %s" % (type(e).__name__, e), traceback=traceback.format_exc(limit=8))
    finally:
        stop_gate.set()
        watchdog.cancel()
        if exit_code == 0 and R.count("FAIL"):
            exit_code = 1
        write_reports(a, R, exit_code, started)
        log("SUMMARY: %d PASS, %d FAIL, %d WARN -> exit %d; report %s" % (R.count("PASS"), R.count("FAIL"), R.count("WARN"), exit_code, os.path.join(a.out, "ragdoll_player_report.txt")))
    return exit_code


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
