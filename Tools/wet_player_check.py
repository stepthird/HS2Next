"""
wet_player_check.py - stains in the built app player (Phase 6.6; the row WetCheck.player).

Starts the app player itself, three times, and checks over real HTTP from outside the process, in real frames. It never uses the Unity Editor. Python 3.14, standard library
only, plus dotnet for the test plugin. Content: the clothed SFW visual targets of Tools\\targets.json on non-H rows, judged by numbers. No image is opened or shown: renders stay in
the player's memory through the test plugin; the frames of a recording and the files of a dialog are the one exception, as those paths write files: they are decoded here as
numbers and deleted by this script, on clothed targets only.

Usage (the run_tests.ps1 row WetCheck.player calls the first form):
    python -I Tools\\wet_player_check.py --exe <path to HS2Next.exe> --project <project root> --out <dir> [--port 17849] [--timeout-min 30]
    more options: --only A,B,C or check ids (default all), --port-b 17858, --port-c 17848, --measure-frames 400, --rounds 3, --baseline-exe <an older player> (WW7a against it),
                  --gfx d3d11 | d3d12, --min-free-gb 8, --ram-wait-min 10, --ready-timeout-min 12
    -I ignores PYTHONUTF8, so the script sets its own stdout/stderr to UTF-8.

Sessions:
  A  -batchmode, no plugin (HS2NEXT_NO_PLUGINS=1), --port. What an ordinary player does:
     GPU     ground.probe on a vanilla map and on a map with no readable mesh: source gpu, every point answered
     TRIP    two characters with stains, running to a moment; save, new, load: clock, maps, puddles as they were; the second save's block equal; maps 8-bit gray PNG; format revision;
             a dry undone brings the clock back
     DRIPS   a stain run in steps on the map: drops land, puddles at the floor the query gives
     WB10    a garment of fo_top_40, whose meshes cannot be read: status unreadable and dry, or a normal stain; no exception, no error line; the other parts stain as usual
     WT2     accessories by their switch: a static one and a skinned one whose mesh cannot be read take the stain, a static one whose mesh cannot be read stays dry and is counted; shoes too
     SHADERS the player's log read for each new shader
  B  windowed 1920 x 1080, -noUi, the test plugin Tests\\WetProbe, --port-b, the frame cap off, illusion.hs2#10, BR-Chan and HERO107 44 units away:
     WW1-WW8, WW10-WW13 (spec 15.3). A/B frames from app.perf in alternating rounds; the rest by the plugin's Stopwatch on the main thread.
     WW4M, WW4B  the film again with its stains' puddles live, and with no mirror in the frame: camera renders, mirror renders and films counted beside the time
     WW12T   every render texture against what the rig owns after a stain has ended
     WO6R    a recorded frame through record.start against a capture of the same moment, the puddle's pixels; in memory, as WV6M, the blink off and the clock paused: RenderAsync against RenderSync and two
             RenderSync at 0 px, the mirror changing at least WO6R_MIRROR_MIN px or FAIL; the line gives the box of a difference and its counts on the character, the member and outside
     WO6RR   the same comparisons with the clock running, the blink off: any difference is a numbered WARN
     GPUSRC  the probe's IMapQuery source, and every shader of this phase found and supported
  C  the UI, minimised, the probe, --port-c:
     WW9     PickSurface through IViewportTools.Pick over rays through the character; viewport.surfacePickMs of ui.state after presses under Pick origin
     WO6D    the Screenshot dialog's file against a capture of the same size, the puddle's pixels
     BIG     one render at 5430 x 3054 with stains and a mirror, in memory: the GPU's peak from nvidia-smi
     WV6M    a real member seen in a puddle, along its region's normal, the blink off: its mask at least WV6_MASK_MIN px or FAIL, its block against F, RenderAsync against RenderSync and two RenderSync at 0 px with
             the clock paused, in memory only, with the wide method off and on (the path of a recorded frame and of the dialog)
     WV6R    the same comparisons with the clock running: any difference is a WARN
     WV6V    the member moving: 12 moments of an orbit with the idle stepped between them, the clock paused at each, the wide method off and on. FAIL: a member's block or the wide block under F, RenderAsync
             against RenderSync over 0 px, a mask under WV6_MASK_MIN, travel under WV6V_TRAVEL_MIN. WARN, numbered: a statistic over its bar in WV6V_BARS (spread, paired runs, ring, the unit's control)

Outputs:
  <out>\\wet_player_report.txt  first and last line "SUMMARY: N PASS, M FAIL", second line "WARN: K (ids)", then one line per check
  <out>\\wet_player_check.json  details of every check
  <out>\\<a|b|c>\\player.log, userdata\\ (the saves)
Exit code: 0 every check passed (WARN allowed), 1 a check failed, 2 setup error, 3 aborted by the game gate. A time over its bar is WARN: the lead decides.
Ports: 17849, 17858, 17848 by default (17859 = WetTest.play, 17322 = interactive default). Always 127.0.0.1, never localhost.
"""
import argparse
import json
import math
import os
import shutil
import statistics
import struct
import subprocess
import sys
import threading
import time
import traceback
import types
import zipfile
import zlib
from datetime import datetime, timezone

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)        # python -I does not add the script's folder
from constraint_player_check import (Abort, CmdError, Ctx, Results, SetupError, free_gb, game_processes, guard, log, port_listeners, running_games, target_rel,  # noqa: E402
                                     CREATE_NO_WINDOW, T0, http)

BAR = {"WW1": 100.0, "WW2": 300.0, "WW3": 5.0, "WW4": 0.2, "WW5": 2.5, "WW6": 30.0, "WW7": 1.0, "WW7a": 0.05, "WW8": 1.0, "WW8first": 60.0, "WW9": 10.0, "WW10": 200.0,
       "WW11": 0.3, "WW12": 200.0, "WW13": 4.0}
ROW_IDLE = {"group": 19, "category": 0, "no": 0}
CLOCK = "2026-11-06T00:00:00Z"
MOD_MAP = "2155x.map.hangar#0"     # a map with no readable mesh
UNREADABLE_BUNDLE = "fo_top_40.unity3d"
# by m_IsReadable and the component that draws, over the 196 accessory bundles of the base game: name, bundle, takes the stain in a player
ACCESSORIES = (("a static accessory", "ao_head_54.unity3d", True), ("a skinned accessory whose mesh cannot be read", "ao_neck_40.unity3d", True), ("a static accessory whose mesh cannot be read", "ao_head_34.unity3d", False))
SHADERS = ["HS2Next/Hidden/WetBake", "HS2Next/Hidden/WetCompose", "HS2Next/Hidden/WetFilm", "HS2Next/Hidden/WetPuddle", "HS2Next/Hidden/WetDrops", "HS2Next/Hidden/MapSurface"]
GUID = "stp3.test.wetprobe"
WO6R_MIRROR_MIN = 32000   # px over 8 levels the mirror changes at 640 x 360: every row so far reads 63,946 here, half of it, so an empty or a half-drawn mirror fails
WV6V_MOMENTS, WV6V_ORBIT, WV6V_FRAMES, WV6V_DT = 12, 0.6, 12, 1.0 / 30.0
WV6V_TRAVEL_MIN = 50   # output px at 960 x 540 from the first moment: an orbit of 0.6 gives about 99, the idle alone 3
# fixed by the lead before the run. Wide on: a block's interior spread at most spreadMax; paired with the same moment's render without the member: run median at least its less pairedRunLess,
# spread max at most its plus pairedSpreadPlus; controls: the ring's spread at least ringSpreadMin, wide off in the same cells a spread median at least offSpreadMedianMin. Wide off: the ring's equal neighbours at most ringEqualMax.
WV6V_BARS = {"spreadMax": 6, "pairedRunLess": 1, "pairedSpreadPlus": 2, "ringSpreadMin": 16, "offSpreadMedianMin": 15, "ringEqualMax": 0.40}
WV6_MASK_MIN = 1000   # the members' pixels at 960 x 540 under which WV6M has judged nothing: the still pose gives 1,325 with the wide method off, 2,025 on


def utc_now():
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def within(cid, value, bar):
    return "PASS" if value <= bar else "WARN"


# ---------------------------------------------------------------- the test plugin

def build_probe(a, out, frames_only=False):
    """Tests\\WetProbe against the player's own Managed folder, into <out>\\plugins\\<guid>. Returns the plugins folder."""
    managed = os.path.join(os.path.dirname(a.exe), "HS2Next_Data", "Managed")
    proj = os.path.join(a.project, "Tests", "WetProbe", "WetProbe.csproj")
    plugins = os.path.join(out, "plugins")
    target = os.path.join(plugins, GUID)
    os.makedirs(target, exist_ok=True)
    obj = os.path.join(out, "probe_obj") + os.sep
    cmd = ["dotnet", "build", proj, "-c", "Release", "-nologo", "-v", "q", "-o", target, "-p:PlayerManaged=" + managed, "-p:BaseIntermediateOutputPath=" + obj]
    if frames_only:
        cmd.append("-p:FramesOnly=true")
    p = subprocess.run(cmd, capture_output=True, timeout=300, creationflags=CREATE_NO_WINDOW)
    if p.returncode != 0:
        raise SetupError("the test plugin did not build: " + p.stdout.decode("utf-8", "replace")[-600:])
    with open(os.path.join(a.project, "Tests", "WetProbe", "manifest.json"), encoding="utf-8") as f:
        m = json.load(f)
    if frames_only:
        m["apiVersion"] = "1.2"
    with open(os.path.join(target, "manifest.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(m, f, indent=1)
    return plugins


# ---------------------------------------------------------------- sessions

class Session(Ctx):
    def __init__(self, a, R, letter, port, exe=None):
        n = types.SimpleNamespace(**vars(a))
        n.port = port
        n.out = os.path.join(a.out, letter.lower())
        n.exe = exe or a.exe
        n.only = None
        n.attach = False
        super().__init__(n)
        self.R = R
        self.letter = letter
        self.frames_only = False
        self.key_a = self.key_b = None


def start(C, kind, deadline):
    """Starts a player: kind A batch, no plugin; B windowed, no UI, the probe; C the UI, minimised, the probe."""
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
        plugins = build_probe(a, C.out, C.frames_only)
        si.dwFlags |= subprocess.STARTF_USESHOWWINDOW
        if kind == "B":
            si.wShowWindow = 4   # SW_SHOWNOACTIVATE
            args += ["-noUi", "-screen-width", "1920", "-screen-height", "1080", "-screen-fullscreen", "0", "-window-mode", "windowed"]
        else:
            si.wShowWindow = 7   # SW_SHOWMINNOACTIVE
            args += ["-ui", "-screen-fullscreen", "0", "-screen-width", "1600", "-screen-height", "900", "-uiOption", "capture.maxPixels=33177600"]
        args += ["-pluginsDir", plugins]
    if a.gfx:
        args.append("-force-" + a.gfx)
    args += ["-bridgePort", str(C.port), "-bridgeAllowQuit", "-settings", settings, "-cacheDir", os.path.join(C.project, "Cache"), "-userData", ud, "-bridgeOutputDir", bo, "-logFile", logf]
    C.info["commandLine"] = args
    log("session %s: starting the player: %s" % (C.letter, subprocess.list2cmdline(args)))
    t0 = time.time()
    C.proc = subprocess.Popen(args, cwd=os.path.dirname(C.exe), env=env, stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                              creationflags=CREATE_NO_WINDOW, startupinfo=si)
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


def stop(C):
    if C.proc is None:
        return
    if C.proc.poll() is None:
        t = time.time()
        reply = None
        try:
            reply = C.br.call({"cmd": "app.quit", "code": 0}, timeout=30)
        except Exception as e:
            log("app.quit: %s" % e)
        try:
            code = C.proc.wait(60)
            killed = False
        except subprocess.TimeoutExpired:
            C.proc.kill()
            code = C.proc.wait(30)
            killed = True
        ok = bool(reply and reply.get("quitting")) and not killed and code == 0
        C.R.add("QUIT" + C.letter, "PASS" if ok else "FAIL", "app.quit; exit code %s in %.1f s%s" % (code, time.time() - t, "; killed after 60 s" if killed else ""))
    else:
        C.R.add("QUIT" + C.letter, "FAIL", "the player had already exited with code %s" % C.proc.returncode)


def kill(C):
    if C.proc is not None and C.proc.poll() is None:
        try:
            C.proc.kill()
            C.proc.wait(30)
        except Exception:
            pass


def read_log(C):
    path = os.path.join(C.out, "player.log")
    if not os.path.isfile(path):
        return []
    with open(path, encoding="utf-8", errors="replace") as f:
        return f.read().splitlines()


def check_log(C):
    lines = read_log(C)
    exc = [l for l in lines if "Exception" in l]
    C.R.add("LOG" + C.letter, "FAIL" if exc else "PASS", "player.log %d lines; %d with 'Exception'%s" % (len(lines), len(exc), (" (first: %s)" % exc[0][:160]) if exc else ""), exceptions=exc[:20])


# ---------------------------------------------------------------- helpers

def new_map(C, **how):
    C.call({"cmd": "scene.new"})
    r = C.call(dict(how, cmd="map.load", wait=True), timeout=600)
    if r.get("state") != "Loaded":
        raise RuntimeError("map.load: %s" % json.dumps(r)[:200])
    return r


def two(C, spread=5.0):
    """BR-Chan and HERO107, physics off, standing still, 44 units from the view (spec 2.12)."""
    r = C.call({"cmd": "batch", "cmds": [
        {"cmd": "chara.load", "path": target_rel(C, "female"), "sex": "female", "as": "a"},
        {"cmd": "chara.motion", "key": "$a.added.0.key", "physics": False},
        {"cmd": "chara.load", "path": target_rel(C, "male"), "sex": "male", "as": "b"},
        {"cmd": "chara.motion", "key": "$b.added.0.key", "physics": False},
    ]}, timeout=300)
    a, b = r["results"][0]["added"][0]["key"], r["results"][2]["added"][0]["key"]
    C.key_a, C.key_b = a, b
    C.call({"cmd": "batch", "cmds": [
        dict(ROW_IDLE, cmd="chara.anime", key=a, time=0.0, speed=0.0), dict(ROW_IDLE, cmd="chara.anime", key=b, time=0.0, speed=0.0),
        {"cmd": "obj.transform", "key": a, "pos": [-spread, 0, 0]}, {"cmd": "obj.transform", "key": b, "pos": [spread, 0, 0]},
        {"cmd": "camera.set", "target": [0, 8, 0], "rot": [14, 180, 0], "dist": 44, "fov": 23}]})
    C.frames(5)
    return a, b


def status(C, key):
    rows = C.call({"cmd": "wet.status", "key": key})["characters"]
    return rows[0] if rows else None


def run_out(C, key, dt=1.0 / 60.0):
    """The current stain to its end in scene steps: a short spread and tail, so that a step run stays short. The pause is left as it was."""
    C.call({"cmd": "wet.set", "key": key, "values": {"spreadTime": 0.5, "dripTail": 0.3}})
    C.call({"cmd": "wet.start", "key": key})
    C.call({"cmd": "scene.pause", "paused": True})
    C.call({"cmd": "scene.step", "n": 60, "dt": dt}, timeout=120)
    C.call({"cmd": "scene.pause", "paused": False})
    C.frames(3)


def frame_ms(C, n):
    C.frames(20)
    C.call({"cmd": "app.perf", "reset": True})
    C.frames(n)
    r = C.call({"cmd": "app.perf"})
    return r["frameMs"]["median"], r["frameMs"]["p95"], r["frames"]


def ab(C, off, on):
    """Alternating rounds; the paired differences' median, as spec 2.12's rounds."""
    n, rounds = C.a.measure_frames, C.a.rounds
    offs, ons = [], []
    for _ in range(rounds):
        off()
        offs.append(frame_ms(C, n)[0])
        on()
        ons.append(frame_ms(C, n)[0])
    diffs = [y - x for x, y in zip(offs, ons)]
    return statistics.median(offs), statistics.median(ons), statistics.median(diffs), offs, ons


def wait_render(C, tag, limit=120):
    end = time.time() + limit
    while time.time() < end:
        r = C.call({"cmd": "wetprobe.rendered", "tag": tag})
        if r.get("done"):
            return r
        C.frames(2)
    raise RuntimeError("the render %s did not finish" % tag)


def render(C, tag, is_async=False, **more):
    q = dict(more, cmd="wetprobe.render", tag=tag)
    q["async"] = is_async
    r = C.call(q, timeout=180)
    return wait_render(C, tag) if is_async else r


def mirror_on(C, keys, on):
    for k in keys:
        C.call({"cmd": "wet.set", "key": k, "values": {"mirror.on": on}})
    C.frames(3)


# ---------------------------------------------------------------- a PNG as numbers (never shown)

def png_pixels(path):
    """(width, height, channels, rows of bytes) of an 8-bit RGB or RGBA PNG without interlace; decoded for numbers only."""
    with open(path, "rb") as f:
        data = f.read()
    if data[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError("not a PNG: " + path)
    i, idat, w, h, depth, colour = 8, [], 0, 0, 0, 0
    while i < len(data):
        n, kind = struct.unpack(">I4s", data[i:i + 8])
        body = data[i + 8:i + 8 + n]
        if kind == b"IHDR":
            w, h, depth, colour = struct.unpack(">IIBB", body[:10])
        elif kind == b"IDAT":
            idat.append(body)
        i += 12 + n
    if depth != 8 or colour not in (2, 6):
        raise ValueError("depth %d colour %d" % (depth, colour))
    ch = 3 if colour == 2 else 4
    raw = zlib.decompress(b"".join(idat))
    stride = w * ch
    rows = []
    prev = bytearray(stride)
    p = 0
    for _ in range(h):
        ft = raw[p]
        line = bytearray(raw[p + 1:p + 1 + stride])
        p += 1 + stride
        if ft == 1:
            for x in range(ch, stride):
                line[x] = (line[x] + line[x - ch]) & 255
        elif ft == 2:
            for x in range(stride):
                line[x] = (line[x] + prev[x]) & 255
        elif ft == 3:
            for x in range(stride):
                left = line[x - ch] if x >= ch else 0
                line[x] = (line[x] + ((left + prev[x]) >> 1)) & 255
        elif ft == 4:
            for x in range(stride):
                aa = line[x - ch] if x >= ch else 0
                b = prev[x]
                c = prev[x - ch] if x >= ch else 0
                pa, pb, pc = abs(b - c), abs(aa - c), abs(aa + b - 2 * c)
                line[x] = (line[x] + (aa if pa <= pb and pa <= pc else b if pb <= pc else c)) & 255
        rows.append(line)
        prev = line
    return w, h, ch, rows


def compare_png(on, off, other, threshold=6):
    """Where the images on and off differ is the mirror's own area; in that area, how many pixels of `other` differ from `on`."""
    w, h, ch, a = on
    _, _, ch2, b = off
    _, _, ch3, c = other
    area = differ = 0
    most = 0
    for y in range(h):
        ra, rb, rc = a[y], b[y], c[y]
        for x in range(w):
            i, j, k = x * ch, x * ch2, x * ch3
            d = max(abs(ra[i] - rb[j]), abs(ra[i + 1] - rb[j + 1]), abs(ra[i + 2] - rb[j + 2]))
            if d < 8:
                continue
            area += 1
            e = max(abs(ra[i] - rc[k]), abs(ra[i + 1] - rc[k + 1]), abs(ra[i + 2] - rc[k + 2]))
            most = max(most, e)
            if e > threshold:
                differ += 1
    return area, differ, most


def remove(path):
    try:
        os.remove(path)
    except OSError:
        pass


# ---------------------------------------------------------------- session A

def gpu_source(C):
    new_map(C, no=10)
    pts = [[x, z] for x in (-6, -3, 0, 3, 6) for z in (-6, 0, 6)]
    r = C.call({"cmd": "ground.probe", "points": pts, "y": 12, "above": 0, "below": 30})
    ans = [h for h in r["hits"] if h.get("y") is not None]
    C.R.add("GPU", "PASS" if r.get("source") == "gpu" and len(ans) == len(pts) else "FAIL", "ground.probe on illusion.hs2#10: source %s, %d of %d points answer" % (r.get("source"), len(ans), len(pts)))
    new_map(C, ref=MOD_MAP)
    r = C.call({"cmd": "ground.probe", "points": pts, "y": 12, "above": 0, "below": 30})
    ans = [h for h in r["hits"] if h.get("y") is not None]
    C.R.add("GPU", "PASS" if r.get("source") == "gpu" and ans else "FAIL", "ground.probe on %s (no readable mesh): source %s, %d of %d points answer" % (MOD_MAP, r.get("source"), len(ans), len(pts)))


def find_wet(o):
    if isinstance(o, dict):
        if "wet" in o and isinstance(o["wet"], dict) and "stains" in o["wet"]:
            yield o["wet"]
        for v in o.values():
            yield from find_wet(v)
    elif isinstance(o, list):
        for v in o:
            yield from find_wet(v)


def trip(C):
    """a's stain goes on running through the load, b's has ended: b's block is exact, a's clock only moved on."""
    new_map(C, no=10)
    a, b = two(C)
    C.call({"cmd": "wet.attach", "key": a, "preset": "blood"})
    C.call({"cmd": "wet.attach", "key": b, "preset": "water"})
    run_out(C, b)
    C.call({"cmd": "wet.start", "key": a})
    C.call({"cmd": "scene.pause", "paused": True})
    C.call({"cmd": "scene.step", "n": 150, "dt": 1.0 / 60.0}, timeout=120)
    C.frames(3)
    before = {k: status(C, k) for k in (a, b)}
    p1 = C.call({"cmd": "scene.save", "name": "trip1", "overwrite": True, "thumb": "placeholder", "clock": CLOCK})["path"]
    C.call({"cmd": "scene.new"})
    C.call({"cmd": "scene.load", "path": p1}, timeout=300)
    C.call({"cmd": "scene.pause", "paused": True})
    C.frames(10)
    rows = C.call({"cmd": "wet.status"})["characters"]
    pick = lambda s: (s["current"], s["maps"]["live"], s["maps"]["dormant"], s["targets"], s["running"])
    after_running = [s for s in rows if s["running"]]
    after_stopped = [s for s in rows if not s["running"]]
    ok = len(rows) == 2 and len(after_running) == 1 and len(after_stopped) == 1
    if ok:
        x, y = before[a], after_running[0]
        p, q = before[b], after_stopped[0]
        ok = pick(x) == pick(y) and 0 <= y["t"] - x["t"] < 1.0 and pick(p) == pick(q) and abs(p["t"] - q["t"]) < 1e-5 and p["puddles"] == q["puddles"] and abs(p["floor"] - q["floor"]) < 1e-4
    C.R.add("TRIP", "PASS" if ok else "FAIL", "after a load: %s; before: %s" % (json.dumps(rows)[:300], json.dumps(list(before.values()))[:300]))
    p2 = C.call({"cmd": "scene.save", "name": "trip2", "overwrite": True, "thumb": "placeholder", "clock": CLOCK})["path"]
    z1, z2 = zipfile.ZipFile(p1), zipfile.ZipFile(p2)
    d1, d2 = json.loads(z1.read("scene.json").decode("utf-8")), json.loads(z2.read("scene.json").decode("utf-8"))
    w1, w2 = list(find_wet(d1)), list(find_wet(d2))
    stopped = [w for w in w1 if not any(s.get("running") for s in w["stains"])]
    norm = lambda w: json.dumps({k: ([{m: v for m, v in s.items() if m != "t"} for s in x] if k == "stains" else x) for k, x in w.items() if k != "puddles"}, sort_keys=True)
    ok = len(w1) == 2 and stopped and all(w in w2 for w in stopped) and sorted(map(norm, w1)) == sorted(map(norm, w2))
    C.R.add("TRIP", "PASS" if ok else "FAIL", "the second save: %d blocks, the ended stain's block equal, the running one equal but for its clock and puddles" % len(w2))
    mapped = {m["res"] for w in w1 for s in w["stains"] for m in s["maps"]}
    grays = sum(1 for m in mapped if z1.read(m)[24:26] == b"\x08\x00")
    C.R.add("TRIP", "PASS" if mapped and grays == len(mapped) else "FAIL", "%d maps in the file, %d of them 8-bit gray PNG" % (len(mapped), grays))
    man = json.loads(z1.read("manifest.json").decode("utf-8"))
    C.info["manifest"] = {k: man.get(k) for k in ("formatVersion", "minReaderVersion", "revision")}
    C.R.add("TRIP", "PASS" if man.get("formatVersion") == 5 else "FAIL", "manifest %s" % json.dumps(C.info["manifest"]))
    k = rows[0]["key"]
    t0 = status(C, k)["t"]
    C.call({"cmd": "wet.dry", "key": k})
    C.frames(3)
    t1 = status(C, k)["t"]
    C.call({"cmd": "undo"})
    C.frames(3)
    t2 = status(C, k)["t"]
    C.R.add("TRIP", "PASS" if t1 == 0 and abs(t2 - t0) < 1e-4 else "FAIL", "a dry: t %.3f, then %.3f; undone %.3f" % (t0, t1, t2))
    C.call({"cmd": "scene.pause", "paused": False})


def drips(C):
    new_map(C, no=10)
    a, b = two(C)
    C.call({"cmd": "wet.attach", "key": a, "preset": "water"})
    C.call({"cmd": "wet.set", "key": a, "values": {"maxRadius": 6, "spreadTime": 1, "dripRate": 60, "dripTail": 2}})
    C.call({"cmd": "wet.start", "key": a})
    C.call({"cmd": "scene.pause", "paused": True})
    C.call({"cmd": "scene.step", "n": 300, "dt": 1.0 / 60.0}, timeout=180)
    C.frames(3)
    s = status(C, a)
    q = C.call({"cmd": "ground.probe", "points": [[-5, 0]], "y": 12, "above": 0, "below": 30})
    C.call({"cmd": "scene.pause", "paused": False})
    y = q["hits"][0].get("y")
    ok = s["puddles"] > 0 and s["floorSource"] == "map" and y is not None and abs(s["floor"] - y) < 0.01
    C.R.add("DRIPS", "PASS" if ok else "FAIL", "%d puddles; floor %.4f from %s, the query %.4f (%s)" % (s["puddles"], s["floor"], s["floorSource"], y if y is not None else float("nan"), q.get("source")))


def wb10(C):
    new_map(C, no=10)
    a, b = two(C)
    rows = C.call({"cmd": "chara.edit.list", "key": a, "path": "coord.clothes.top.item", "columns": ["MainAB", "MainData"], "limit": 10000})["rows"]
    hit = [r for r in rows if str((r.get("values") or {}).get("MainAB", "")).lower().endswith(UNREADABLE_BUNDLE)]
    if not hit:
        C.R.add("WB10", "FAIL", "no top row of %s among %d rows" % (UNREADABLE_BUNDLE, len(rows)))
        return
    row = hit[0]
    log_before = len(read_log(C))
    C.call({"cmd": "chara.edit.item", "key": a, "path": "coord.clothes.top.item", "ref": row["ref"]}, timeout=120)
    C.frames(10)
    worn = next((k for k in C.call({"cmd": "chara.edit.clothes", "key": a, "kind": "top"})["kinds"]), {})
    C.call({"cmd": "wet.set", "key": a, "values": {"maxRadius": 10}})
    r = C.call({"cmd": "wet.attach", "key": a, "preset": "blood"}, check=False)
    if not r.get("ok"):
        C.R.add("WB10", "FAIL", "wet.attach: %s" % json.dumps(r)[:240])
        return
    C.call({"cmd": "wet.set", "key": a, "values": {"spreadTime": 0.2, "dripTail": 0.1}})
    C.call({"cmd": "wet.start", "key": a})
    C.frames(30)
    s = status(C, a)
    lines = read_log(C)[log_before:]
    bad = [l for l in lines if "Exception" in l or l.startswith("Error") or "not readable" in l.lower() or "isReadable" in l or "BakeMesh" in l]
    state = "unreadable and dry" if s["unreadable"] > 0 else "a normal stain: BakeMesh serves it"
    ok = not bad and s["targets"] + s["unreadable"] > 0 and s["maps"]["live"] > 0
    C.R.add("WB10", "PASS" if ok else "FAIL", "%s (%s, %s): %s: unreadable %d, targets %d, maps live %d, %d log lines read, %d bad%s"
            % (row["ref"], (row.get("values") or {}).get("MainData"), "realized" if worn.get("realized") else "not realized", state, s["unreadable"], s["targets"], s["maps"]["live"], len(lines), len(bad),
               (" (first: %s)" % bad[0][:160]) if bad else ""), bad=bad[:12], status=s, row=row)


def accessory_rows(C):
    """The first row of each bundle of ACCESSORIES: {bundle: (category, ref)}."""
    found = {}
    for category in range(351, 364):
        rows = C.call({"cmd": "chara.edit.list", "category": category, "sex": "female", "columns": ["MainAB", "MainData"], "limit": 10000}).get("rows") or []
        for r in rows:
            ab = str((r.get("values") or {}).get("MainAB", "")).lower()
            for _, bundle, _ in ACCESSORIES:
                if ab.endswith(bundle) and bundle not in found:
                    found[bundle] = (category, r["ref"])
    return found


def wt2(C):
    """An Editor reads a mesh a player cannot, so what a stain does on an accessory is only known here. The stain is drawn to reach the whole body."""
    new_map(C, no=10)
    a, b = two(C)
    found = accessory_rows(C)
    gone = [x[1] for x in ACCESSORIES if x[1] not in found]
    if gone:
        C.R.add("WT2", "FAIL", "no row of %s" % ", ".join(gone))
        return
    r = C.call({"cmd": "wet.attach", "key": a, "preset": "water"}, check=False)
    if not r.get("ok"):
        C.R.add("WT2", "FAIL", "wet.attach: %s" % json.dumps(r)[:240])
        return
    C.call({"cmd": "wet.set", "key": a, "values": {"shape.radius": 10, "shape.up": 0.5, "shape.down": 0.1, "shape.front": 0.3, "shape.back": 0.3, "shape.side": 0.3, "shape.outer": 0, "shape.noise": 0, "spreadTime": 0.2, "dripTail": 0.1}}, timeout=60)
    C.call({"cmd": "wet.start", "key": a})
    C.frames(30)
    log_before = len(read_log(C))
    was = status(C, a)
    C.call({"cmd": "wet.set", "key": a, "values": {"targets.accessories": True}}, timeout=60)
    C.frames(5)
    for name, bundle, takes in ACCESSORIES:
        category, ref = found[bundle]
        before = status(C, a)
        C.call({"cmd": "chara.edit.accessory", "key": a, "op": "add", "category": category, "ref": ref}, timeout=120)
        C.frames(10)
        # a part that came after the switch waits for a bake: off and on again gives it, and keeps what is there
        C.call({"cmd": "wet.set", "key": a, "values": {"targets.accessories": False}}, timeout=60)
        C.call({"cmd": "wet.set", "key": a, "values": {"targets.accessories": True}}, timeout=60)
        C.frames(5)
        s = status(C, a)
        live, unread = s["maps"]["live"] - before["maps"]["live"], s["unreadable"] - before["unreadable"]
        ok = live > 0 and unread == 0 if takes else live == 0 and unread > 0
        C.R.add("WT2", "PASS" if ok else "FAIL", "%s (%s, %s): maps live %+d, unreadable %+d, no film %d; %s" % (name, bundle, ref, live, unread, s["noFilm"], "takes the stain" if takes else "stays dry and is counted"), status=s)
    before = status(C, a)
    C.call({"cmd": "wet.set", "key": a, "values": {"targets.shoes": True}}, timeout=60)
    C.frames(5)
    s = status(C, a)
    C.R.add("WT2", "PASS" if s["maps"]["live"] > before["maps"]["live"] else "FAIL", "shoes by their switch: maps live %d -> %d" % (before["maps"]["live"], s["maps"]["live"]), status=s)
    lines = read_log(C)[log_before:]
    bad = [l for l in lines if "Exception" in l or l.startswith("Error") or "not readable" in l.lower() or "isReadable" in l or "BakeMesh" in l]
    C.R.add("WT2", "PASS" if not bad else "FAIL", "%d log lines read, %d bad%s; the character started at %d maps live" % (len(lines), len(bad), (" (first: %s)" % bad[0][:160]) if bad else "", was["maps"]["live"]), bad=bad[:12])


def shader_log(C):
    lines = read_log(C)
    for name in SHADERS:
        short = name.rsplit("/", 1)[1]
        said = [l for l in lines if short in l]
        bad = [l for l in said if any(w in l.lower() for w in ("error", "fail", "not found", "unsupported", "not supported", "missing"))]
        C.R.add("SHADERS", "FAIL" if bad else "PASS", "%s: %d lines in the log, %d say a fault%s" % (name, len(said), len(bad), (" (first: %s)" % bad[0][:160]) if bad else ""))
    general = [l for l in lines if ("Hidden/" in l and "not" in l.lower()) or "WARNING: Shader" in l or "Shader error" in l]
    C.R.add("SHADERS", "PASS" if not [l for l in general if "Wet" in l or "MapSurface" in l] else "FAIL", "%d shader warnings in the log, none of this phase's%s" % (len(general), (" (first other: %s)" % general[0][:140]) if general else ""))


# ---------------------------------------------------------------- session B

def probe_shaders(C):
    rows = C.call({"cmd": "wetprobe.shaders"})["rows"]
    good = [r["name"] for r in rows if r["found"] and r["supported"] and r["passes"] > 0]
    C.R.add("SHADERS", "PASS" if len(good) == len(SHADERS) else "FAIL", "Shader.Find in the player: %d of %d found, supported, with passes; %s" % (len(good), len(SHADERS), json.dumps([r for r in rows if r["name"] not in good])[:200]))


def ww8(C):
    new_map(C, no=10)
    r = C.call({"cmd": "wetprobe.surface", "x": 0, "y": 12, "z": 0, "n": 225})
    t = r["times"]
    src = r["sources"]
    C.R.add("GPUSRC", "PASS" if list(src) == ["gpu"] and r["hits"] == 225 else "FAIL", "IMapQuery in the player: sources %s, %d of 225 answer, %d roots" % (json.dumps(src), r["hits"], r["roots"]))
    ok = t["median"] <= BAR["WW8"] and t["first"] <= BAR["WW8first"]
    C.R.add("WW8", "PASS" if ok else "WARN", "Surface median %.3f ms, p95 %.3f, max %.3f; first of the map %.3f ms. Bars 1, 60" % (t["median"], t["p95"], t["max"], t["first"]), times=t)


def ww7(C):
    a, b = two(C)
    r = C.call({"cmd": "wetprobe.idle", "n": 400000})
    us, alloc = r["usPerFrame"] - r["emptyLoopUs"], r["bytes"]
    frames = r["frames"]
    good = r["allocationControl"] >= frames * 8 or r["controlCollections"] > 0   # one 16-byte array a frame must show
    none = alloc < frames // 4 and r["collections"] == 0
    v = "PASS" if us <= BAR["WW7"] and none and good else "WARN"
    C.R.add("WW7", v, "no wet: Apply, Tick, LateApply %.4f us a frame (the loop alone %.4f); the heap moved %d B over %d frames, %d collections; one 16-byte array a frame moves it %d B, %d collections; rigs %d, subscribed %s, planes %d, cameras %d, targets %d, root %s. Bars 1 us, 0 B"
            % (us, r["emptyLoopUs"], alloc, frames, r["collections"], r["allocationControl"], r["controlCollections"], r["rigs"], r["subscribed"], r["planes"], r["cameras"], r["targets"], r["root"]), raw=r)
    m, p, n = frame_ms(C, C.a.measure_frames * 2)
    C.info["noWetFrameMs"] = [frame_ms(C, C.a.measure_frames)[0] for _ in range(C.a.rounds)]
    C.R.add("WW7f", "PASS", "no wet: frame median %.3f ms, p95 %.3f over %d frames; rounds %s" % (m, p, n, C.info["noWetFrameMs"]), rounds=C.info["noWetFrameMs"])
    return a, b


def bake_rows(C, name, k):
    r = C.call({"cmd": "wetprobe.bake", "key": k, "rounds": 12}, timeout=120)
    if r.get("why"):
        C.R.add("WW1", "FAIL", "%s: %s" % (name, r["why"]))
        return
    x, pv, cm, wh = r["bake"], r["preview"], r["commit"], r["whole"]
    C.R.add("WW1", within("WW1", x["p95"], BAR["WW1"]), "%s: a bake, no picture: median %.2f ms, p95 %.2f, max %.2f (%d maps); a drag's step with its compose: median %.2f, max %.2f. Bar 100" % (name, x["median"], x["p95"], x["max"], r["maps"], pv["median"], pv["max"]), raw=r)
    C.R.add("WW2", within("WW2", cm["p95"], BAR["WW2"]), "%s: the pictures' read-back and encode: median %.2f ms, p95 %.2f, max %.2f; a whole bake with them: median %.2f. Bar 300" % (name, cm["median"], cm["p95"], cm["max"], wh["median"]))


def ww1_2_3(C):
    a, b = C.key_a, C.key_b
    for name, k in (("BR-Chan", a), ("HERO107", b)):
        C.call({"cmd": "wet.attach", "key": k, "preset": "water"}, timeout=60)
        C.frames(5)
        bake_rows(C, name + " at 2.2", k)
        C.call({"cmd": "wet.set", "key": k, "values": {"maxRadius": 10}}, timeout=60)
        C.frames(5)
        bake_rows(C, name + " at 10", k)
        if k == a:
            C.call({"cmd": "wet.set", "key": k, "values": {"drips.on": False}})
            s = C.call({"cmd": "wetprobe.step", "key": k, "ticks": 200}, timeout=120)
            C.call({"cmd": "wet.set", "key": k, "values": {"drips.on": True}})
            C.R.add("WW3", within("WW3", s["perStep"]["median"], BAR["WW3"]), "%s, %d targets a step, drips off: to the GPU's end over runs of steps median %.3f ms, max %.3f; the main thread alone median %.3f, p95 %.3f; a read with nothing queued %.3f. Bar 5"
                    % (name, s["targets"], s["perStep"]["median"], s["perStep"]["max"], s["cpu"]["median"], s["cpu"]["p95"], s["read"]["median"]), raw=s)
        C.call({"cmd": "wet.set", "key": k, "values": {"maxRadius": 2.2}}, timeout=60)


def ww12(C):
    a, b = C.key_a, C.key_b
    for k in (a, b):
        C.call({"cmd": "wet.remove", "key": k, "all": True})
    C.frames(40)
    worst = 0.0
    rows = []
    mb = 2.0 ** 20
    for name, k in (("BR-Chan", a), ("HERO107", b)):
        was = C.call({"cmd": "wetprobe.rts"})
        C.call({"cmd": "wet.attach", "key": k, "preset": "water"}, timeout=60)
        run_out(C, k)
        C.frames(40)
        one = C.call({"cmd": "wetprobe.rts", "key": k})
        C.call({"cmd": "wet.set", "key": k, "values": {"maxRadius": 10}}, timeout=60)
        run_out(C, k)
        C.frames(40)
        two_ = C.call({"cmd": "wetprobe.rts", "key": k})
        s = status(C, k)
        own1 = (one["composites"] + one["masks"] + one["maps"]) / mb
        own2 = (two_["composites"] + two_["masks"] + two_["maps"]) / mb
        worst = max(worst, own2)
        rows.append("%s at 2.2 %.1f MB; at 10 %.1f MB (composites %.1f, masks %.1f, maps %.1f; %d targets; every render texture +%.1f MB, the pool's temporaries in it)"
                    % (name, own1, own2, two_["composites"] / mb, two_["masks"] / mb, two_["maps"] / mb, s["targets"], (two_["bytes"] - was["bytes"]) / mb))
    C.R.add("WW12", within("WW12", worst, BAR["WW12"]), "by arithmetic: " + "; ".join(rows) + ". Bar 200")
    for k in (a, b):
        C.call({"cmd": "wet.set", "key": k, "values": {"maxRadius": 2.2}}, timeout=60)
        run_out(C, k)


def ww4(C):
    a, b = C.key_a, C.key_b
    def film(on):
        for k in (a, b):
            C.call({"cmd": "wet.set", "key": k, "values": {"film.on": on}})
    off, on, d, xs, ys = ab(C, lambda: film(False), lambda: film(True))
    C.R.add("WW4", within("WW4", d, BAR["WW4"]), "film off %.3f, on %.3f ms: +%.3f (rounds %s, %s). Bar 0.2" % (off, on, d, xs, ys))


def counts(C, keys, frames=120):
    """Camera renders and mirror renders over frames; the films of each character: (renders per frame text, mirror renders a frame, camera renders a frame, films drawn by character)."""
    C.call({"cmd": "wetprobe.count", "start": True})
    C.frames(frames)
    r = C.call({"cmd": "wetprobe.count"})
    n = float(max(1, r["frames"]))
    cams = sum(v for k, v in r["cameras"].items() if k.startswith("Game"))
    films = [C.call({"cmd": "wetprobe.films", "key": k}) for k in keys]
    return r, r["mirrorRenders"] / n, cams / n, films


def ww4b(C):
    """WW4 again with the mirror in the frame and with none: the same stains as WW4 had, the frame's renders and the films counted beside the times."""
    new_map(C, no=10)
    a, b = two(C)
    keys = (a, b)
    for k in keys:
        C.call({"cmd": "wet.attach", "key": k, "preset": "water"}, timeout=60)
        run_out(C, k)
        C.call({"cmd": "wet.set", "key": k, "values": {"maxRadius": 10}}, timeout=60)
        run_out(C, k)
        C.call({"cmd": "wet.set", "key": k, "values": {"maxRadius": 2.2}}, timeout=60)
        run_out(C, k)
    C.frames(30)

    def film(on, which=keys):
        for k in which:
            C.call({"cmd": "wet.set", "key": k, "values": {"film.on": on}})

    def state():
        r, mirror, cams, films = counts(C, keys)
        return ("%d frames: %.2f mirror renders and %.2f camera renders a frame, planes %d, subscribed %s, cameras %s; films made / drawn / seen BR-Chan %d / %d / %d of %d targets, HERO107 %d / %d / %d of %d"
                % (r["frames"], mirror, cams, r["planes"], r["subscribed"], json.dumps(r["cameras"]), films[0]["made"], films[0]["drawn"], films[0]["seen"], films[0]["slots"], films[1]["made"], films[1]["drawn"], films[1]["seen"], films[1]["slots"]), mirror, cams, films)

    s, mirror, cams, films = state()
    off, on, d, xs, ys = ab(C, film_off := (lambda: film(False)), film_on := (lambda: film(True)))
    C.frames(5)
    s2 = state()[0]
    C.R.add("WW4M", "PASS", "as WW4, natural puddles live: film off %.3f, on %.3f ms: +%.3f (rounds %s, %s); on: %s" % (off, on, d, xs, ys, s2), raw=s2)
    for k in keys:
        C.call({"cmd": "wetprobe.puddle", "key": k, "area": 0, "clear": True})
    C.frames(10)
    s, mirror, cams, films = state()
    clean = mirror == 0 and not C.call({"cmd": "wetprobe.mirror"})["subscribed"]
    off, on, d, xs, ys = ab(C, film_off, film_on)
    s, mirror, cams, films = state()
    C.R.add("WW4B", ("PASS" if d <= BAR["WW4"] else "WARN") if clean and mirror == 0 else "FAIL", "no mirror in the frame, both characters: film off %.3f, on %.3f ms: +%.3f (rounds %s, %s); on: %s. Bar 0.2" % (off, on, d, xs, ys, s), raw=s)
    for name, k in (("BR-Chan", a), ("HERO107", b)):
        other = b if k == a else a
        film(False, (other,))
        off, on, d, xs, ys = ab(C, lambda: film(False, (k,)), lambda: film(True, (k,)))
        one = C.call({"cmd": "wetprobe.films", "key": k})
        C.R.add("WW4B", "PASS" if d <= BAR["WW4"] else "WARN", "no mirror in the frame, %s's films alone: off %.3f, on %.3f ms: +%.3f (rounds %s, %s); films drawn %d, seen %d, %s. Bar 0.2" % (name, off, on, d, xs, ys, one["drawn"], one["seen"], json.dumps(one["families"])))
        film(True, (other,))


def ww12t(C):
    """What the pool's temporaries do after a stain has ended: every render texture against what the rig owns, at growing distances from the end."""
    new_map(C, no=10)
    a, b = two(C)
    C.frames(30)
    mb = 2.0 ** 20
    was = C.call({"cmd": "wetprobe.rts"})
    C.call({"cmd": "wet.attach", "key": a, "preset": "water"}, timeout=60)
    C.call({"cmd": "wet.set", "key": a, "values": {"maxRadius": 10}}, timeout=60)
    run_out(C, a)
    rows = []
    done = 0
    for n in (5, 35, 260, 900):
        C.frames(n)
        done += n
        r = C.call({"cmd": "wetprobe.rts", "key": a})
        own = r["composites"] + r["masks"] + r["maps"]
        rows.append("%d frames after: every render texture +%.1f MB (%d), the rig %.1f MB, beside it %.1f MB" % (done, (r["bytes"] - was["bytes"]) / mb, r["count"] - was["count"], own / mb, (r["bytes"] - was["bytes"] - own) / mb))
    C.call({"cmd": "wetprobe.puddle", "key": a, "area": 0, "clear": True})
    C.frames(300)
    m = C.call({"cmd": "wetprobe.mirror"})
    r = C.call({"cmd": "wetprobe.rts", "key": a})
    own = r["composites"] + r["masks"] + r["maps"]
    rows.append("300 frames after its puddles went (planes %d): every render texture +%.1f MB (%d), the rig %.1f MB, beside it %.1f MB" % (m["planes"], (r["bytes"] - was["bytes"]) / mb, r["count"] - was["count"], own / mb, (r["bytes"] - was["bytes"] - own) / mb))
    C.call({"cmd": "wet.remove", "key": a, "all": True})
    C.frames(300)
    r = C.call({"cmd": "wetprobe.rts"})
    rows.append("300 frames after the stain was removed: +%.1f MB (%d)" % ((r["bytes"] - was["bytes"]) / mb, r["count"] - was["count"]))
    C.R.add("WW12T", "PASS", "BR-Chan at 10: " + "; ".join(rows))


def ww5_13_6(C):
    a, b = C.key_a, C.key_b
    keys = (a, b)
    C.call({"cmd": "wetprobe.puddle", "key": b, "area": 0, "clear": True})
    p = C.call({"cmd": "wetprobe.puddle", "key": a, "x": 0, "z": 6, "area": 270, "clear": True})
    C.frames(5)
    m0 = C.call({"cmd": "wetprobe.mirror"})
    C.frames(30)
    m1 = C.call({"cmd": "wetprobe.mirror"})
    if m1["renders"] <= m0["renders"] or m1["planes"] != 1:
        C.R.add("WW5", "FAIL", "no mirror renders with a puddle in view: %s then %s" % (json.dumps(m0), json.dumps(m1)))
    off, on, d, xs, ys = ab(C, lambda: mirror_on(C, keys, False), lambda: mirror_on(C, keys, True))
    C.R.add("WW5", within("WW5", d, BAR["WW5"]), "a mirror at scale 0.5, 2 characters, a puddle of radius 14 in view: off %.3f, on %.3f ms: +%.3f (rounds %s, %s); %d renders in 30 frames. Bar 2.5" % (off, on, d, xs, ys, m1["renders"] - m0["renders"]))
    # WW6: a capture at 1920 x 1080, mirror on against off, in memory
    times = {True: [], False: []}
    for _ in range(6):
        for flag in (False, True):
            mirror_on(C, keys, flag)
            r = render(C, "ww6", keep=False, width=1920, height=1080)
            times[flag].append(r["mainThreadMs"])
    off6, on6 = statistics.median(times[False][1:]), statistics.median(times[True][1:])
    C.R.add("WW6", within("WW6", on6 - off6, BAR["WW6"]), "a capture 1920 x 1080, render and read-back: mirror off %.2f ms, on %.2f: +%.2f (on %s). Bar 30" % (off6, on6, on6 - off6, times[True]))
    # WW13: a second plane
    mirror_on(C, keys, True)
    y = p["y"] + 0.6
    C.call({"cmd": "wetprobe.puddle", "key": b, "x": 0, "y": y, "z": 2, "area": 90})
    C.frames(5)
    m0 = C.call({"cmd": "wetprobe.mirror"})
    C.frames(30)
    m1 = C.call({"cmd": "wetprobe.mirror"})
    per = (m1["renders"] - m0["renders"]) / 30.0
    off, on, d, xs, ys = ab(C, lambda: mirror_on(C, keys, False), lambda: mirror_on(C, keys, True))
    v = within("WW13", d, BAR["WW13"]) if m1["planes"] == 2 and per > 1.5 else "FAIL"
    C.R.add("WW13", v, "2 planes (%d), %.2f renders a frame: off %.3f, on %.3f ms: +%.3f (rounds %s, %s), skipped %d, failures %d. Bar 4" % (m1["planes"], per, off, on, d, xs, ys, m1["skipped"], m1["failures"]))


def ww11(C):
    r = C.call({"cmd": "wetprobe.drops", "n": 300}, timeout=60)
    x = r["both"]
    C.R.add("WW11", within("WW11", x["median"], BAR["WW11"]), "%d drops: step median %.4f ms, mesh median %.4f; both median %.4f, p95 %.4f, max %.4f. Bar 0.3" % (r["drops"], r["step"]["median"], r["mesh"]["median"], x["median"], x["p95"], x["max"]), raw=r)


def where(c):
    """Where a comparison's difference lies, so that a failure says it."""
    return "box %s, on the character %d, in the mirror's area %d, on the member %d, outside %d" % (c.get("box"), c["onCharacter"], c["onMirror"], c["onMember"], c["outside"])


def wo6r(C):
    """A recorded frame against a capture of the same moment; RenderAsync against RenderSync in memory."""
    a, b = C.key_a, C.key_b
    keys = (a, b)
    C.call({"cmd": "camera.set", "target": [0, 1, 5], "rot": [38, 180, 0], "dist": 34, "fov": 30})
    C.frames(5)
    w, h = 640, 360
    # in memory: the verdict with the blink off and the clock paused
    for k in keys:
        C.call({"cmd": "chara.face", "key": k, "blink": False})
    C.frames(5)
    try:
        mirror_on(C, keys, False)
        C.call({"cmd": "scene.pause", "paused": True})
        C.frames(5)
        render(C, "off", width=w, height=h)
        C.call({"cmd": "scene.pause", "paused": False})
        mirror_on(C, keys, True)
        C.call({"cmd": "scene.pause", "paused": True})
        C.frames(5)
        render(C, "ref", width=w, height=h)
        for k in keys:
            C.call({"cmd": "wetprobe.members", "key": k, "width": w, "height": h, "hide": True})
        C.frames(2)
        render(C, "nomember", width=w, height=h)
        for k in keys:
            C.call({"cmd": "wetprobe.members", "key": k, "width": w, "height": h, "hide": False})
            C.call({"cmd": "obj.visible", "key": k, "visible": False})
        C.frames(3)
        render(C, "gone", width=w, height=h)
        for k in keys:
            C.call({"cmd": "obj.visible", "key": k, "visible": True})
        C.frames(3)
        mask = C.call({"cmd": "wetprobe.compare", "a": "ref", "b": "nomember", "threshold": 2})["differ"]
        c0 = C.call({"cmd": "wetprobe.compare", "a": "ref", "b": "off", "threshold": 8})
        q = {"cmd": "wetprobe.compare", "threshold": 2, "character": ["ref", "gone"], "mirror": ["ref", "off"], "member": ["ref", "nomember"]}
        same, again = [], []
        for _ in range(3):
            render(C, "sync", width=w, height=h)
            render(C, "async", True, width=w, height=h)
            render(C, "sync2", width=w, height=h)
            same.append(C.call(dict(q, a="sync", b="async")))
            again.append(C.call(dict(q, a="sync", b="sync2")))
        c2 = C.call({"cmd": "wetprobe.compare", "a": "sync", "b": "async", "threshold": 2, "within": "ref", "withinOf": "off"})
        C.call({"cmd": "scene.pause", "paused": False})
        C.frames(5)
        # with the clock running, the blink still off: any difference is news
        render(C, "sync", width=w, height=h)
        render(C, "async", True, width=w, height=h)
        render(C, "sync2", width=w, height=h)
        run_a = C.call(dict(q, a="sync", b="async"))
        run_s = C.call(dict(q, a="sync", b="sync2"))
    finally:
        C.call({"cmd": "scene.pause", "paused": False})
        for k in keys:
            C.call({"cmd": "chara.face", "key": k, "blink": True})
    worst = max(same + again, key=lambda x: x["differ"])
    ok = c0["differ"] >= WO6R_MIRROR_MIN and all(x["differ"] == 0 for x in same + again)
    C.R.add("WO6R", "PASS" if ok else "FAIL", "in memory, %d x %d, the blink off, the clock paused: the mirror changes %d px (at least %d); RenderAsync against RenderSync %s px over 2 levels of %d, two RenderSync %s, most %d, inside the mirror's area %d of %d; %s; the members' mask %d px"
            % (w, h, c0["differ"], WO6R_MIRROR_MIN, [x["differ"] for x in same], same[0]["pixels"], [x["differ"] for x in again], worst["most"], c2["differ"], c2["pixels"], where(worst), mask))
    warned = 0
    for label, c in (("RenderAsync against RenderSync", run_a), ("two RenderSync", run_s)):
        if c["differ"]:
            warned += 1
            C.R.add("WO6RR", "WARN", "%d: the blink off, the clock running: %s %d px, most %d; %s" % (warned, label, c["differ"], c["most"], where(c)))
    C.call({"cmd": "wetprobe.forget"})
    # through the files
    made = []
    try:
        mirror_on(C, keys, False)
        off = C.call({"cmd": "capture", "name": "wo6_off", "width": w, "height": h}, timeout=120)["path"]
        made.append(off)
        mirror_on(C, keys, True)
        on = C.call({"cmd": "capture", "name": "wo6_on", "width": w, "height": h}, timeout=120)["path"]
        made.append(on)
        m0 = C.call({"cmd": "wetprobe.mirror"})
        st = C.call({"cmd": "record.start", "path": "record/wo6", "width": w, "height": h, "fps": 30, "frames": 4, "overwrite": True})
        end = time.time() + 120
        rs = None
        while time.time() < end:
            rs = C.call({"cmd": "record.status"})
            if not rs.get("recording") and rs.get("pending") == 0 and isinstance(rs.get("stoppedBy"), str):
                break
            C.frames(3)
        m1 = C.call({"cmd": "wetprobe.mirror"})
        folder = (rs or {}).get("path") or st.get("path") or os.path.join(C.output_root, "record", "wo6")
        frames = sorted(os.path.join(folder, f) for f in os.listdir(folder) if f.endswith(".png"))
        made += frames
        pon, poff = png_pixels(on), png_pixels(off)
        rows, worst = [], 0.0
        for f in frames:
            area, differ, most = compare_png(pon, poff, png_pixels(f))
            rows.append("%d of %d, most %d" % (differ, area, most))
            worst = max(worst, differ / float(max(1, area)))
        ok = bool(frames) and rs and rs.get("written") == len(frames) and pon[0] == w and worst <= 0.01
        C.R.add("WO6R", "PASS" if ok else "FAIL", "a recording through FrameRecorder: %d frames written, stopped by %s, %d mirror renders; a frame against a capture in the puddle's pixels: %s" % (len(frames), rs.get("stoppedBy") if rs else None, m1["renders"] - m0["renders"], "; ".join(rows)))
    finally:
        for p in made:
            remove(p)
        folder = os.path.join(C.output_root or "", "record")
        if os.path.isdir(folder):
            shutil.rmtree(folder, ignore_errors=True)


def ww10(C):
    a, b = C.key_a, C.key_b
    r = C.call({"cmd": "wetprobe.load", "key": a, "keys": [a, b], "rounds": 5}, timeout=120)
    x = r["load"]
    C.R.add("WW10", within("WW10", x["median"], BAR["WW10"]), "BR-Chan's maps read again and composed: median %.2f ms, max %.2f, first %.2f; %d maps, %d decoded. Bar 200" % (x["median"], x["max"], x["first"], r["maps"], r["decoded"]), raw=r)


def wb10p(C):
    """The same garment with the probe: which parts cannot be read, and what became of them."""
    new_map(C, no=10)
    a, b = two(C)
    rows = C.call({"cmd": "chara.edit.list", "key": a, "path": "coord.clothes.top.item", "columns": ["MainAB", "MainData"], "limit": 10000})["rows"]
    hit = [r for r in rows if str((r.get("values") or {}).get("MainAB", "")).lower().endswith(UNREADABLE_BUNDLE)]
    C.call({"cmd": "chara.edit.item", "key": a, "path": "coord.clothes.top.item", "ref": hit[0]["ref"]}, timeout=120)
    C.frames(10)
    C.call({"cmd": "wet.set", "key": a, "values": {"maxRadius": 10}})
    C.call({"cmd": "wet.attach", "key": a, "preset": "blood"})
    C.call({"cmd": "wet.set", "key": a, "values": {"spreadTime": 0.3, "dripTail": 1.0, "dripRate": 60}})
    C.call({"cmd": "wet.start", "key": a})
    C.call({"cmd": "scene.pause", "paused": True})
    C.call({"cmd": "scene.step", "n": 60, "dt": 1.0 / 60.0}, timeout=60)
    C.call({"cmd": "scene.pause", "paused": False})
    C.frames(3)
    parts = C.call({"cmd": "wetprobe.parts", "key": a})["rows"]
    top = [x for x in parts if str(x["address"]).startswith("clothes:0")]
    closed = [x for x in top if not x["readable"]]
    s = status(C, a)
    others = [x for x in parts if x["live"] and not str(x["address"]).startswith("clothes:0")]
    state = "; ".join("%s %s, %s, %s" % (x["address"], x["state"], "not readable" if not x["readable"] else "readable", "a live map" if x["live"] else "no map") for x in top)
    ok = bool(closed) and bool(others)
    C.R.add("WB10", "PASS" if ok else "FAIL", "with the probe, %s: %d parts of the top, %d of them not readable: %s; %d other parts live; wet.status unreadable %d, targets %d; emitters %s" % (hit[0]["ref"], len(top), len(closed), state[:400], len(others), s["unreadable"], s["targets"], parts[0]["emitters"] if parts else 0), parts=parts)


def run_b(C):
    C.call({"cmd": "wetprobe.frames", "vsync": 0, "fps": -1})
    f = C.call({"cmd": "wetprobe.frames"})
    C.info["frames"] = f
    C.R.add("FRAMES", "PASS" if f.get("vsync") == 0 else "FAIL", "the frame cap off: %s" % json.dumps(f))
    for cid, fn in (("SHADERS", probe_shaders), ("WW8", ww8), ("WW7", ww7), ("WW1", ww1_2_3), ("WW12", ww12), ("WW4", ww4), ("WW5", ww5_13_6), ("WW11", ww11), ("WO6R", wo6r), ("WW10", ww10), ("WB10P", wb10p), ("WW4B", ww4b), ("WW12T", ww12t)):
        if C.a.want(cid) or C.a.want("B"):
            guard(C, cid, fn)


# ---------------------------------------------------------------- WW7a against an older player

def frames_of(a, R, exe, label, port, deadline):
    C = Session(a, R, "O" + label, port, exe)
    C.frames_only = True
    try:
        start(C, "B", deadline)
        C.call({"cmd": "wetprobe.frames", "vsync": 0, "fps": -1})
        new_map(C, no=10)
        two(C)
        C.frames(60)
        rows = [frame_ms(C, a.measure_frames)[0] for _ in range(a.rounds)]
        stop(C)
        return rows
    finally:
        kill(C)


# ---------------------------------------------------------------- session C

def nv_used():
    p = subprocess.run(["nvidia-smi", "--query-gpu=memory.used,memory.total", "--format=csv,noheader,nounits"], capture_output=True, timeout=20, creationflags=CREATE_NO_WINDOW)
    x = p.stdout.decode("ascii", "replace").split(",")
    return int(x[0]), int(x[1])


class GpuPoll(threading.Thread):
    def __init__(self):
        super().__init__(daemon=True)
        self.peak, self.stop_it = 0, False

    def run(self):
        while not self.stop_it:
            try:
                self.peak = max(self.peak, nv_used()[0])
            except Exception:
                pass
            time.sleep(0.05)


def reveal(C, name, scroll):
    """Wheels the panel's scroll view until the element lies inside it."""
    for _ in range(14):
        e = C.call({"cmd": "ui.query", "name": name})["elements"]
        s = C.call({"cmd": "ui.query", "name": scroll})["elements"]
        if not e or not s:
            return False
        _, ey, _, eh = e[0]["worldBound"]
        _, sy, _, sh = s[0]["worldBound"]
        if ey >= sy and ey + eh <= sy + sh:
            return True
        C.call({"cmd": "ui.input", "events": [{"type": "wheel", "target": scroll, "delta": 3 if ey + eh > sy + sh else -3}], "frames": 2})
    return False


def ww9(C):
    a, b = C.key_a, C.key_b
    pos = C.call({"cmd": "chara.bones", "key": a, "names": ["cf_J_Spine02"]})["bones"]["cf_J_Spine02"]["pos"]
    C.call({"cmd": "camera.set", "target": pos, "rot": [0, 180, 0], "dist": 26, "fov": 23})
    C.frames(5)
    C.call({"cmd": "wet.attach", "key": a, "preset": "water"}, timeout=60)
    C.frames(5)
    r = C.call({"cmd": "wetprobe.pick", "key": a, "passes": 3}, timeout=120)
    x = r["picks"]
    y = r["onBody"]
    ok = r["hits"] > 0 and r["hits"] == r["precise"]
    C.R.add("WW9", ("PASS" if x["p95"] <= BAR["WW9"] and x["max"] <= BAR["WW9"] else "WARN") if ok else "FAIL", "PickSurface through Tools.Pick, %d rays: median %.2f ms, p95 %.2f, max %.2f; the %d on the body: median %.2f, p95 %.2f, max %.2f, all precise %s; the very first %.2f. Bar 10"
            % (x["n"], x["median"], x["p95"], x["max"], r["hits"], y["median"], y["p95"], y["max"], r["hits"] == r["precise"], r["veryFirst"]), raw=r)
    # presses under Pick origin, as a user makes them
    C.call({"cmd": "select", "keys": [a]}, check=False)
    if not [e for e in C.call({"cmd": "ui.query", "name": "wet-pick-origin"})["elements"] if e.get("visible")]:   # the action toggles the panel, and a layout may have kept it open
        C.call({"cmd": "ui.action", "action": "wet.panel"})
        C.frames(10)
    shown = reveal(C, "wet-pick-origin", "wet-scroll")
    wb = C.call({"cmd": "ui.state"})["viewport"]["worldBound"]
    if isinstance(wb, dict):
        vx, vy, vw, vh = wb.get("x", 0), wb.get("y", 0), wb.get("width", wb.get("w", 0)), wb.get("height", wb.get("h", 0))
    else:
        vx, vy, vw, vh = wb
    cx, cy = vx + vw / 2.0, vy + vh / 2.0
    times = []
    for i in range(6):
        C.call({"cmd": "ui.input", "events": [{"type": "pointer", "target": "wet-pick-origin", "phase": "click", "button": 0}], "frames": 3})
        ox, oy = (i % 3 - 1) * 6, (i // 3) * 8 - 4
        C.call({"cmd": "ui.input", "events": [{"type": "pointer", "x": cx + ox, "y": cy + oy, "phase": "down", "button": 0}, {"type": "pointer", "x": cx + ox, "y": cy + oy, "phase": "up", "button": 0}], "frames": 3})
        st = C.call({"cmd": "ui.state"})["viewport"]
        times.append(st.get("surfacePickMs"))
        if st.get("toolRunning"):
            C.call({"cmd": "ui.input", "events": [{"type": "key", "key": "Escape"}], "frames": 3})
    t = [v for v in times if v]
    C.R.add("WW9", ("PASS" if max(t) <= BAR["WW9"] else "WARN") if t else "FAIL", "viewport.surfacePickMs after presses under Pick origin: %s; the button in view %s. Bar 10" % (times, shown))


def wo6d(C):
    a, b = C.key_a, C.key_b
    keys = (a, b)
    made = []
    cap = os.path.join(C.out, "userdata", "cap")
    try:
        C.call({"cmd": "wet.attach", "key": b, "preset": "water"}, timeout=60)
        C.call({"cmd": "wetprobe.puddle", "key": a, "x": 0, "z": 6, "area": 270})
        C.call({"cmd": "camera.set", "target": [0, 1, 5], "rot": [38, 180, 0], "dist": 34, "fov": 30})
        C.frames(5)
        before = set(os.listdir(cap)) if os.path.isdir(cap) else set()
        C.call({"cmd": "ui.input", "events": [{"type": "key", "key": "F11", "mods": ["shift"]}], "frames": 5})
        modal = json.dumps(C.call({"cmd": "ui.state"}).get("modal"))
        C.call({"cmd": "ui.input", "events": [{"type": "key", "key": "Return"}], "frames": 3})
        end = time.time() + 30
        fresh = []
        while time.time() < end and not fresh:
            now = set(os.listdir(cap)) if os.path.isdir(cap) else set()
            fresh = sorted(now - before)
            time.sleep(0.25)
        time.sleep(1.0)
        C.call({"cmd": "ui.input", "events": [{"type": "key", "key": "Escape"}], "frames": 3})
        if not fresh:
            C.R.add("WO6D", "FAIL", "the dialog wrote no file; modal %s" % modal[:120])
            return
        f = os.path.join(cap, fresh[-1])
        made.append(f)
        with open(f, "rb") as x:
            head = x.read(24)
        w, h = struct.unpack(">II", head[16:24])
        on = C.call({"cmd": "capture", "name": "wo6d_on", "width": w, "height": h}, timeout=120)["path"]
        made.append(on)
        mirror_on(C, keys, False)
        off = C.call({"cmd": "capture", "name": "wo6d_off", "width": w, "height": h}, timeout=120)["path"]
        made.append(off)
        mirror_on(C, keys, True)
        area, differ, most = compare_png(png_pixels(on), png_pixels(off), png_pixels(f))
        ok = area > 100 and differ <= 0.01 * area
        C.R.add("WO6D", "PASS" if ok else "FAIL", "the Screenshot dialog's file, %d x %d, against a capture: %d px of the puddle's %d differ, most %d" % (w, h, differ, area, most))
    finally:
        for p in made:
            remove(p)


def big(C):
    a, b = C.key_a, C.key_b
    C.frames(10)
    base, total = nv_used()
    g = GpuPoll()
    g.start()
    r = render(C, "big", True, width=5430, height=3054, keep=False)
    time.sleep(1.0)
    g.stop_it = True
    g.join(3)
    m = C.call({"cmd": "wetprobe.mirror"})
    ok = not r.get("error") and r.get("width") == 5430 and m["failures"] == 0
    C.R.add("BIG", "PASS" if ok else "FAIL", "5430 x 3054 with stains and a mirror: render %.1f ms, read-back %.1f, encode %.1f, total %.1f; error %s; GPU memory %d MiB before, peak %d of %d; mirror failures %d"
            % (r.get("renderMs") or 0, r.get("readbackMs") or 0, r.get("encodeMs") or 0, r.get("totalMs") or 0, r.get("error"), base, g.peak, total, m["failures"]), raw=r)


def wv6_stage(C):
    """A real member in a puddle: the mirror camera along the region's normal, where the row Idle_00 shows it. The blink is off, so with the clock paused nothing moves between two renders. The puddle is the other
    character's, so it stays when this one is hidden. Returns the render's width and height, and the mirror's plane."""
    a, b = C.key_a, C.key_b
    for k in (a, b):
        C.call({"cmd": "chara.face", "key": k, "blink": False})
        s = status(C, k)
        if s is None or not s.get("current"):
            C.call({"cmd": "wet.attach", "key": k, "preset": "water"}, timeout=60)
    C.frames(5)
    C.call({"cmd": "obj.transform", "key": a, "pos": [0, 0, 0]})
    C.call({"cmd": "obj.transform", "key": b, "pos": [60, 0, 60]})
    C.call({"cmd": "chara.clothes", "key": a, "all": 2})
    C.frames(10)
    floor = C.call({"cmd": "wet.floor", "key": a})["y"]
    plane = floor + 0.02
    w, h = 960, 540
    region = next(x for x in C.call({"cmd": "wetprobe.members", "key": a, "width": w, "height": h, "plane": plane})["rows"] if x["name"] == "o_mnpb")
    c, n = region["centroid"], region["normal"]
    size = math.sqrt(sum(x * x for x in n))
    below = [c[i] + n[i] / size * 16 for i in range(3)]
    view = [below[0], 2 * plane - below[1], below[2]]
    target = [c[0], 2 * plane - c[1], c[2]]
    f = [(target[i] - view[i]) / 16.0 for i in range(3)]
    pitch, yaw = math.degrees(math.asin(-f[1])), math.degrees(math.atan2(f[0], f[2]))
    at = (view[1] - floor) / max(view[1] - target[1], 1e-3)
    hit = [view[i] + (target[i] - view[i]) * at for i in range(3)]
    C.call({"cmd": "wetprobe.puddle", "key": a, "area": 0, "clear": True})
    C.call({"cmd": "wetprobe.puddle", "key": b, "x": hit[0], "y": floor, "z": hit[2], "area": 200, "clear": True})
    C.call({"cmd": "camera.set", "target": target, "rot": [pitch, yaw, 0], "dist": 16, "fov": 23})
    C.frames(5)
    return w, h, plane


def wv6_unstage(C):
    a, b = C.key_a, C.key_b
    C.call({"cmd": "wetprobe.forget"})
    C.call({"cmd": "app.options", "render": {"mosaicWide": False}})
    C.call({"cmd": "chara.clothes", "key": a, "all": 0})
    for k in (a, b):
        C.call({"cmd": "chara.face", "key": k, "blink": True})


def wv6m(C):
    """WV6M. Renders stay in the player's memory, this script sees numbers."""
    a, b = C.key_a, C.key_b
    w, h, plane = wv6_stage(C)
    for wide in (False, True):
        C.call({"cmd": "app.options", "render": {"mosaicWide": wide}})
        C.frames(5)
        mirror_on(C, (b,), False)
        C.call({"cmd": "scene.pause", "paused": True})
        C.frames(5)
        render(C, "off", width=w, height=h)
        C.call({"cmd": "scene.pause", "paused": False})
        mirror_on(C, (b,), True)
        C.call({"cmd": "scene.pause", "paused": True})
        C.frames(5)
        render(C, "sync", width=w, height=h)
        C.call({"cmd": "wetprobe.members", "key": a, "width": w, "height": h, "plane": plane, "hide": True})
        C.frames(2)
        render(C, "nomember", width=w, height=h)
        C.call({"cmd": "wetprobe.members", "key": a, "width": w, "height": h, "plane": plane, "hide": False})
        C.frames(2)
        mask = C.call({"cmd": "wetprobe.compare", "a": "sync", "b": "nomember", "threshold": 2})["differ"]
        frame = C.call({"cmd": "wetprobe.mirrorframe", "width": w, "height": h, "plane": plane, "scale": 0.5, "k": 1})
        least = min((m["blockPx"] for m in frame["members"] if m["mode"] == "Texture"), default=0.0)
        # the clock stands, so what differs is the path: and a second RenderSync says what two renders of one moment give
        same, again = [], []
        for _ in range(3):
            render(C, "sync", width=w, height=h)
            render(C, "async", True, width=w, height=h)
            render(C, "sync2", width=w, height=h)
            same.append(C.call({"cmd": "wetprobe.compare", "a": "sync", "b": "async", "threshold": 2}))
            again.append(C.call({"cmd": "wetprobe.compare", "a": "sync", "b": "sync2", "threshold": 2}))
        C.call({"cmd": "scene.pause", "paused": False})
        C.frames(5)
        # with the clock running: the blink is off, so any difference is news
        render(C, "sync", width=w, height=h)
        render(C, "async", True, width=w, height=h)
        render(C, "sync2", width=w, height=h)
        run_a = C.call({"cmd": "wetprobe.compare", "a": "sync", "b": "async", "threshold": 2})
        run_s = C.call({"cmd": "wetprobe.compare", "a": "sync", "b": "sync2", "threshold": 2})
        ok = mask >= WV6_MASK_MIN and least >= frame["floorPx"] and all(x["differ"] == 0 for x in same + again)
        C.R.add("WV6M", "PASS" if ok else "FAIL", "the wide method %s, the clock paused: the members' mask %d px (at least %d), least block %.1f against F %.1f render px; RenderAsync against RenderSync %s px over 2 levels of %d, two RenderSync %s"
                % ("on" if wide else "off", mask, WV6_MASK_MIN, least, frame["floorPx"], [x["differ"] for x in same], same[0]["pixels"], [x["differ"] for x in again]))
        if run_a["differ"] or run_s["differ"]:
            C.R.add("WV6R", "WARN", "the wide method %s, the clock running: RenderAsync against RenderSync %d px, most %d, box %s; two RenderSync %d px, most %d, box %s" % ("on" if wide else "off", run_a["differ"], run_a["most"], run_a["box"], run_s["differ"], run_s["most"], run_s["box"]), a=run_a, s=run_s)
    wv6_unstage(C)


def wv6v_moment(C, a, plane, wide, w, h):
    """One paused moment: RenderSync, RenderAsync, the render with the members hidden, the render with the character hidden; the same pixels read in each. In memory only."""
    s = "1" if wide else "0"
    v, y, n, g = "v" + s, "y" + s, "n" + s, "g" + s
    C.call({"cmd": "app.options", "render": {"mosaicWide": wide}})
    C.frames(3)
    render(C, v, width=w, height=h)
    render(C, y, True, width=w, height=h)
    C.call({"cmd": "wetprobe.members", "key": a, "width": w, "height": h, "plane": plane, "hide": True})
    C.frames(2)
    render(C, n, width=w, height=h)
    C.call({"cmd": "wetprobe.members", "key": a, "width": w, "height": h, "plane": plane, "hide": False})
    C.frames(2)
    C.call({"cmd": "obj.visible", "key": a, "visible": False})
    C.frames(3)
    render(C, g, width=w, height=h)
    C.call({"cmd": "obj.visible", "key": a, "visible": True})
    C.frames(3)
    same = C.call({"cmd": "wetprobe.compare", "a": v, "b": y, "threshold": 2})
    fr = C.call({"cmd": "wetprobe.mirrorframe", "width": w, "height": h, "plane": plane, "scale": 0.5, "k": 1})
    mem = C.call({"cmd": "wetprobe.members", "key": a, "width": w, "height": h, "plane": plane})["rows"]
    cen = next((m.get("mirrorCentroid") for m in mem if m["name"] == "o_mnpb"), None)
    gain = fr["gain"]
    grp = fr["groups"][0] if fr["groups"] else None
    q = {"cmd": "wetprobe.region", "mask": [v, n], "threshold": 2, "erode": 1, "character": [v, g], "ringFrom": 22 if wide else 3, "ringTo": 34 if wide else 9, "band": 2}
    if grp:
        q.update({"block": grp["block"] * gain, "anchorX": grp["anchor"][0] * gain, "anchorY": grp["anchor"][1] * gain})
    r = {k: C.call(dict(q, tag=tag)) for k, tag in (("v", v), ("a", y), ("n", n))}
    row = {"wide": wide, "same": same["differ"], "mask": r["v"]["maskPx"], "centre": cen[:2] if cen else None, "floor": fr["floorPx"], "least": min((m["blockPx"] for m in fr["members"]), default=0.0), "group": grp,
           "view": r["v"]["mask"], "async": r["a"]["mask"], "undrawn": r["n"]["mask"], "ring": r["v"]["ring"]}
    if wide:
        row["off"] = C.call(dict(q, tag="v0"))["mask"]
    return row


def wv6v_judge(row):
    """(what fails, what warns) of one reading."""
    b = WV6V_BARS
    bad, warn = [], []
    if row["same"]:
        bad.append("RenderAsync against RenderSync %d px" % row["same"])
    if row["least"] < row["floor"]:
        bad.append("a member's block %.1f under F %.1f" % (row["least"], row["floor"]))
    if row["mask"] < WV6_MASK_MIN:
        bad.append("mask %d px under %d" % (row["mask"], WV6_MASK_MIN))
    u = row["undrawn"]
    if row["wide"]:
        if not row["group"] or row["group"]["block"] < row["floor"]:
            bad.append("the wide block %s under F %.1f" % (row["group"] and row["group"]["block"], row["floor"]))
        for path in ("view", "async"):
            v = row[path]
            if v["spreadMax"] > b["spreadMax"]:
                warn.append("%s: a block's interior spreads %d levels, bar %d, without the member %d" % (path, v["spreadMax"], b["spreadMax"], u["spreadMax"]))
            if v["runMedian"] < u["runMedian"] - b["pairedRunLess"]:
                warn.append("%s: run median %d, without the member %d" % (path, v["runMedian"], u["runMedian"]))
            if v["spreadMax"] > u["spreadMax"] + b["pairedSpreadPlus"]:
                warn.append("%s: spread max %d, without the member %d" % (path, v["spreadMax"], u["spreadMax"]))
        if row["ring"]["spreadMax"] < b["ringSpreadMin"]:
            warn.append("control: the ring's spread %d under %d" % (row["ring"]["spreadMax"], b["ringSpreadMin"]))
        if row["off"]["spreadMedian"] < b["offSpreadMedianMin"]:
            warn.append("control: wide off in the same cells, spread median %d under %d" % (row["off"]["spreadMedian"], b["offSpreadMedianMin"]))
    elif row["ring"]["equal"] > b["ringEqualMax"]:
        warn.append("control: the ring's equal neighbours %.3f over %.2f" % (row["ring"]["equal"], b["ringEqualMax"]))
    return bad, warn


def wv6v(C):
    """The member moving through the view of WV6M: the character carried round an orbit, the idle stepped between moments, the clock paused at each. Equal neighbours are printed, no bar."""
    a = C.key_a
    w, h, plane = wv6_stage(C)
    C.call(dict(ROW_IDLE, cmd="chara.anime", key=a, time=0.0, speed=1.0))
    C.call({"cmd": "scene.pause", "paused": True})
    C.frames(5)
    rows, bad, warned = [], [], 0
    for i in range(WV6V_MOMENTS):
        if i:
            C.call({"cmd": "scene.step", "n": WV6V_FRAMES, "dt": WV6V_DT}, timeout=60)
        an = 2 * math.pi * i / WV6V_MOMENTS
        C.call({"cmd": "obj.transform", "key": a, "pos": [WV6V_ORBIT * math.cos(an), 0, WV6V_ORBIT * math.sin(an)]})
        C.frames(3)
        for wide in (False, True):
            row = wv6v_moment(C, a, plane, wide, w, h)
            rows.append(row)
            f, k = wv6v_judge(row)
            bad += ["moment %d, the wide method %s: %s" % (i, "on" if wide else "off", x) for x in f]
            for x in k:
                warned += 1
                C.R.add("WV6V", "WARN", "%d: moment %d, the wide method %s: %s" % (warned, i, "on" if wide else "off", x))
    C.call({"cmd": "scene.pause", "paused": False})
    C.call({"cmd": "obj.transform", "key": a, "pos": [0, 0, 0]})
    C.call(dict(ROW_IDLE, cmd="chara.anime", key=a, time=0.0, speed=0.0))
    wv6_unstage(C)
    cs = [r["centre"] for r in rows if r["centre"]]
    travel = max((math.hypot(p[0] - cs[0][0], p[1] - cs[0][1]) for p in cs), default=0.0)
    off, on = [r for r in rows if not r["wide"]], [r for r in rows if r["wide"]]
    ok = not bad and travel >= WV6V_TRAVEL_MIN and len(cs) == len(rows)
    C.R.add("WV6V", "PASS" if ok else "FAIL", "%d moments of an orbit of %.1f, the wide method off and on: travel %.1f px (at least %d); masks %d-%d px off, %d-%d on (at least %d); least block %.1f against F %.1f, wide block %s; RenderAsync against RenderSync most %d px; equal neighbours printed, %.3f-%.3f off%s"
            % (WV6V_MOMENTS, WV6V_ORBIT, travel, WV6V_TRAVEL_MIN, min(r["mask"] for r in off), max(r["mask"] for r in off), min(r["mask"] for r in on), max(r["mask"] for r in on), WV6_MASK_MIN,
               min(r["least"] for r in rows), rows[0]["floor"], min((r["group"]["block"] for r in on if r["group"]), default=0.0), max(r["same"] for r in rows),
               min(r["view"]["equal"] for r in off), max(r["view"]["equal"] for r in off), ("; " + "; ".join(bad[:6])) if bad else ""))


def run_c(C):
    new_map(C, no=10)
    two(C)
    for cid, fn in (("WW9", ww9), ("WO6D", wo6d), ("BIG", big), ("WV6M", wv6m), ("WV6V", wv6v)):
        if C.a.want(cid) or C.a.want("C"):
            guard(C, cid, fn)


def run_a(C):
    for cid, fn in (("GPU", gpu_source), ("TRIP", trip), ("DRIPS", drips), ("WB10", wb10), ("WT2", wt2), ("SHADERS", shader_log)):
        if C.a.want(cid) or C.a.want("A"):
            guard(C, cid, fn)


# ---------------------------------------------------------------- main

def write_reports(a, R, exit_code, started):
    n_pass, n_fail, n_warn = R.count("PASS"), R.count("FAIL"), R.count("WARN")
    summary = "SUMMARY: %d PASS, %d FAIL" % (n_pass, n_fail)
    warns = [x["id"] for x in R.items if x["verdict"] == "WARN"]
    lines = [summary, "WARN: %d%s" % (n_warn, (" (" + ", ".join(warns) + ")") if warns else ""), "wet_player_check %s; exe %s; exit code %d; %.0f s" % (started, a.exe, exit_code, time.perf_counter() - T0)]
    for x in R.items:
        lines.append("%-7s %-4s  %s" % (x["id"], x["verdict"], x["text"]))
    lines.append(summary)
    with open(os.path.join(a.out, "wet_player_report.txt"), "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")
    with open(os.path.join(a.out, "wet_player_check.json"), "w", encoding="utf-8", newline="\n") as f:
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
    ap.add_argument("--port", type=int, default=17849)
    ap.add_argument("--port-b", type=int, default=17858)
    ap.add_argument("--port-c", type=int, default=17848)
    ap.add_argument("--timeout-min", type=float, default=30)
    ap.add_argument("--only", default="")
    ap.add_argument("--measure-frames", type=int, default=400)
    ap.add_argument("--rounds", type=int, default=3)
    ap.add_argument("--baseline-exe", default=None)
    ap.add_argument("--gfx", choices=["d3d11", "d3d12"], default=None)
    ap.add_argument("--min-free-gb", type=float, default=8)
    ap.add_argument("--ram-wait-min", type=float, default=10)
    ap.add_argument("--ready-timeout-min", type=float, default=12)
    a = ap.parse_args(argv)
    a.exe, a.project, a.out = os.path.abspath(a.exe), os.path.abspath(a.project), os.path.abspath(a.out)
    only = set(x.strip().upper() for x in a.only.split(",") if x.strip())
    a.want = lambda c: not only or c.upper() in only
    a.session_on = lambda letter, ids: not only or letter in only or bool(only & set(i.upper() for i in ids))
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

    def session(letter, port, kind, body):
        nonlocal exit_code
        C = Session(a, R, letter, port)
        live.append(C)
        try:
            start(C, kind, deadline)
            body(C)
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
        old1 = []
        if exit_code == 0 and a.baseline_exe:
            old1 = frames_of(a, R, os.path.abspath(a.baseline_exe), "1", a.port_b, deadline)
        if exit_code == 0 and a.session_on("A", ["GPU", "TRIP", "DRIPS", "WB10", "WT2"]):
            session("A", a.port, "A", run_a)
        if exit_code == 0 and a.session_on("B", ["WW1", "WW2", "WW3", "WW4", "WW4B", "WW12T", "WW5", "WW6", "WW7", "WW8", "WW10", "WW11", "WW12", "WW13", "WO6R", "WB10P"]):
            session("B", a.port_b, "B", run_b)
        if exit_code == 0 and a.baseline_exe:
            old2 = frames_of(a, R, os.path.abspath(a.baseline_exe), "2", a.port_b, deadline)
            rounds = next((x["data"]["rounds"] for x in R.items if x["id"] == "WW7f"), None)
            if rounds:
                before = statistics.median(old1 + old2)
                d = statistics.median(rounds) - before
                R.add("WW7a", "PASS" if d <= BAR["WW7a"] else "WARN", "no wet, the frame: this player %.3f ms (%s), the last build %.3f ms (before %s, after %s): %+.3f. Bar 0.05" % (statistics.median(rounds), rounds, before, old1, old2, d))
        if exit_code == 0 and a.session_on("C", ["WW9", "WO6D", "BIG", "WV6M", "WV6V"]):
            session("C", a.port_c, "C", run_c)
    except Exception as e:
        R.add("RUN", "FAIL", "%s: %s" % (type(e).__name__, e), traceback=traceback.format_exc(limit=8))
    finally:
        stop_gate.set()
        watchdog.cancel()
        if exit_code == 0 and R.count("FAIL"):
            exit_code = 1
        write_reports(a, R, exit_code, started)
        log("SUMMARY: %d PASS, %d FAIL, %d WARN -> exit %d; report %s" % (R.count("PASS"), R.count("FAIL"), R.count("WARN"), exit_code, os.path.join(a.out, "wet_player_report.txt")))
    return exit_code


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
