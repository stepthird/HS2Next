"""
plugin_player_check.py - the acceptance of the sample plugins in the built app player (Phase 5.1, package S; the plugin
kit's samples and the runtime disable / enable since Phase 6.1).

Loads the sample from <exe dir>\\Plugins\\stp3.sample.sequencer\\manifest.json by the app's default discovery (no
-pluginsDir) and checks what row 5.1 asks of it over real HTTP from outside the process: PA1-PA9 of
(PA10-PA12 are the integrator's). Since 6.1 the plugin kit's
samples Hello and HelloUi (Samples\\PluginKit) load next to it, built against the player's own Api
(<exe dir>\\HS2Next_Data\\Managed), and the Sequencer may be built against the Api 1.0.0 fixtures
(--api-dir Tests\\Plugins\\fixtures\\api10: a 1.0 plugin on the 1.x host, KX4b); PK1 / PK2 call the kit samples and
KD9 disables and enables the Sequencer at run time. It never uses the Unity Editor. Python 3.14, standard
library only. Content: scenes hold vanilla Studio items only; no image is written or opened (saves use the placeholder
thumbnail, nothing is captured).

Usage (the run_tests.ps1 row PluginCheck.player calls the first form):
    python -I Tools\\plugin_player_check.py --exe <path to HS2Next.exe> --project <project root> --out <dir>
                                            [--port 17394] [--timeout-min 20]
    more options: --only PA1,PK1,KD9,... (default all), --cycles 20 (PA7 measured cycles), --warmup 2,
                  --measure-frames 600, --keep-staged (leave the staged files), --no-build (stage the existing builds),
                  --api-dir <dir> (build the Sequencer against another HS2Next.Api.dll folder, e.g. the 1.0 fixtures),
                  --kit-api-dir <dir> (the kit samples' Api folder; default <exe dir>\\HS2Next_Data\\Managed), --no-kit
                  (the Sequencer alone, as at 5.1), --min-free-gb 8, --ram-wait-min 10, --ready-timeout-min 12,
                  --attach (use a player already serving on --port with the sample loaded: no build, stage, start or quit)
    --make-fixture   instead of the checks: build Tests\\Plugins\\fixtures\\sequence_scene.hs2nscene through the bridge (two
                     vanilla items, a sequence of 2 transform tracks, a camera track and 3 events; fixed clock and the
                     placeholder thumbnail) and copy it into the project
    -I ignores PYTHONUTF8, so the script sets its own stdout/stderr to UTF-8.

Run:
  1. Setup: the exe, <project>\\Config\\settings.local.json and the fixture exist, the port is free, no process of the game
     gate runs (Tools\\session_gate.json, a local file; without it there is no game gate and a WARN line says so), and
     <exe dir>\\Plugins\\ holds no plugin GUID outside the test set (the manifests of Samples\\<name>\\,
     Samples\\PluginKit\\<name>\\ and Tests\\Plugins\\<name>\\): otherwise exit 2 before anything is staged. Waits up to
     --ram-wait-min for --min-free-gb of RAM.
  2. Tools\\plugins.ps1 builds the samples, verifies their references (the Sequencer exactly netstandard, HS2Next.Api,
     Newtonsoft.Json; Hello and HelloUi exactly netstandard, HS2Next.Api) and stages them into <exe dir>\\Plugins\\ (plain
     file copies; -Report lists the files it wrote). The fixture is copied to <out>\\userdata\\scene\\.
  3. Starts the player: <exe> -batchmode -bridgePort <port> -bridgeAllowQuit -settings <project>\\Config\\settings.local.json
     -cacheDir <project>\\Cache -userData <out>\\userdata -bridgeOutputDir <out>\\bridge_out -logFile <out>\\player.log, with
     HS2NEXT_NO_PLUGINS removed from its environment (and HS2NEXT_PRIVATE_OFF=1). Ready = ping.appReady and
     <out>\\userdata\\Bridge\\endpoint.json naming the started PID.
  4. PA1, PA2, PK1, PK2, PK3, PA3-PA8, KD9, WY3, RY8, then app.quit (PA9). Past --timeout-min the script kills only the PID it started.
  5. Removes exactly the files it staged (and the folders it created, when empty).

Checks (one report line each):
  SETUP  ready time, versions, the staged files
  PA1    bridge.version.plugins lists the sample {guid, version 0.1.0, state loaded} and the kit's three samples (version
         0.1.0, loaded; not with --no-kit), and only test-set plugins;
         plugin.list's dir is <exe dir>\\Plugins\\stp3.sample.sequencer
  PA2    help lists exactly the 8 seq.* commands, owner and group = the GUID
  PA3    plugin.list counts >= 5 event subscriptions; seq.status follows scene.load (3 tracks) and scene.new (none)
  PA4    the fixture's sequence plays: every sampled frame (a batch of seq.status and the nodes' scene.doc in one pump)
         has the two items' positions within 1e-4 and rotations within 1e-3 degrees of the interpolation at the
         sequencer's time (an independent implementation here), the view within 1e-3; each event's rename is seen once
         after its time; eventsFired 3, eventErrors 0
  PA5    save -> scene.new -> load gives the same seq.get text; the saved manifest has formatVersion 1 and the ext entry
         {ns: GUID, scope: scene, root: "", version: 1, required: false}
  PA6    delete of a tracked item turns its track off, undo turns it on again
  PA7    leak rows: cycles of scene.load (fixture) -> seq.play -> 60 frames -> seq.stop -> scene.new, 2 warm-up + 20
         measured, plugin.list {diagnostics: true} after each: managed bytes: least-squares slope over the measured cycles x
         cycles <= 2 MiB (one jump near the end passes, steady growth fails; --selftest), materials / meshes /
         textures / render textures <= cycle 0 + 2, gameObjects never above cycle 0's (a cycle below it only where the
         catalog's rate-limited UnloadUnusedAssets started: app.perf unloadUnused.started, the census names the objects),
         seq.status.tracks empty, the plugin's
         registrations and event subscriptions constant, no plugin warning in log.tail
  PA8    seq.measure over 8 items, 600 frames, UI off: PM1-PM5 recorded, and PM1b (an inline read of one item node's transform,
         scene.doc {key, capture: false}) as information; the line shows whether the threshold (PM1 median > 100 us
         or PM3 median > 1.0 ms) is crossed (what follows for the view is decided by hand)
  PK1    hello.greet {name: "player"} answers "Hello, player!" with the scene's object count; without a name "Hello, world!"
  PK2    helloui.read of the panel form has its 5 values; a commit write of size, a write of an item's weight and
         helloui.reset read back; plugin.list counts the sample's 2 forms (ui.forms; a player without UI counts, not draws)
  KD9    plugin.disable of the Sequencer (fixture loaded): state disabled, its commands gone; plugin.enable: state loaded,
         its registration counts as before, seq.get byte-equal to before (the scene's block read again); the times
  WY3    the tool probe (Tests\\Plugins\\ToolProbe, Api 1.3, built against the player's own Api): with the UI off Tools is null and
         Map is not; a tool's calls run in the player; with no map loaded Loaded is false; on a vanilla map Loaded, Roots and a
         surface under a point (source gpu, both queries); with the map cleared Loaded is false again
  RY8    the stage probe (Tests\\Plugins\\StageProbe, Api 1.3, built against the player's own Api): on BR-Chan, in real frames: the stage counts its Steps at its phase, chara.motion lists it with the plugin as
         owner, a rebuild of the character (chara.change) runs the factory again on a new root, the guide's example reads a head above the floor, a stage taken off stops counting and leaves the list. Reads
         Tools\\targets.json; without it the step is a WARN
  PA9    app.quit: the plugin's "unloaded" log line in player.log (one more after KD9's disable), exit code 0
  LOG    exceptions in player.log (WARN only)
  CLEAN  the staged files removed

Outputs:
  <out>\\plugin_player_check_report.txt  first and last line "SUMMARY: N PASS, M FAIL", second line "WARN: K (ids)", then one
                                         line per check "<id> PASS|FAIL|WARN <numbers>"
  <out>\\plugin_player_check.json        details of every check (PA7 per cycle, PA8 the measurement)
  <out>\\player.log, <out>\\userdata\\ (the fixture and PA5's save), <out>\\stage_report.txt
Exit code: 0 every check passed (WARN allowed), 1 a check failed, 2 setup error.
Ports: 17394 by default (17398 = the Editor play test, 17399 = BridgeChecks.player, 17322 = interactive default).
Always 127.0.0.1, never localhost.
"""
import argparse
import ctypes
import hashlib
import http.client
import json
import math
import os
import random
import shutil
import socket
import subprocess
import sys
import threading
import time
import traceback
import zipfile
from datetime import datetime, timezone

GUID = "stp3.sample.sequencer"
VERSION = "0.1.0"
# the plugin kit's samples: (GUID, project folder under the repository, assembly); version 0.1.0, apiVersion 1.1
KIT = [("stp3.sample.hello", os.path.join("Samples", "PluginKit", "Hello"), "Stp3.SampleHello.dll"),
       ("stp3.sample.helloui", os.path.join("Samples", "PluginKit", "HelloUi"), "Stp3.SampleHelloUi.dll"),
       ("stp3.sample.shapes", os.path.join("Samples", "PluginKit", "Shapes"), "Stp3.SampleShapes.dll")]
# the kit's Shapes (Phase 6.8): its item group and rows
SHAPES_GROUP, SHAPES_ROWS, SHAPES_TORUS = 820001, ["Cube", "Sphere", "Cylinder", "Torus", "Arrow"], "stp3.sample.shapes#4"
KIT_VERSION = "0.1.0"
# the test plugin of the viewport tools and the map query; built and staged as the kit's samples
TOOL_PROBE = ("stp3.test.toolprobe", os.path.join("Tests", "Plugins", "ToolProbe"), "Stp3.Test.ToolProbe.dll")
TOOL_MAP = "illusion.hs2#10"
# the test plugin of the character stages; built and staged as the kit's samples
STAGE_PROBE = ("stp3.test.stageprobe", os.path.join("Tests", "Plugins", "StageProbe"), "Stp3.Test.StageProbe.dll")
STAGE_FRAMES = 30
HELLOUI_KEYS = ["enabled", "mode", "note", "size", "tint"]
SEQ_COMMANDS = ["seq.get", "seq.measure", "seq.pause", "seq.play", "seq.seek", "seq.set", "seq.status", "seq.stop"]
DEFAULT_PORT = 17394
ITEM_NO = 399
FIXTURE_REL = os.path.join("Tests", "Plugins", "fixtures", "sequence_scene.hs2nscene")
FIXTURE_NAME = "sequence_scene"
GATE_FILE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "session_gate.json")
CREATE_NO_WINDOW = 0x08000000
MIB = 1 << 20
T0 = time.perf_counter()


def log(msg):
    print("[%7.1f s] %s" % (time.perf_counter() - T0, msg), flush=True)


def utc_now():
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


class SetupError(Exception):
    pass


class Abort(Exception):
    """The player is gone (exited, killed by the watchdog): the remaining checks cannot run."""


class CmdError(RuntimeError):
    def __init__(self, cmd, reply):
        super().__init__("%s -> %s (%s)" % (cmd, reply.get("error"), reply.get("code")))
        self.reply = reply


# ---------------------------------------------------------------- HTTP client

class _Conn(http.client.HTTPConnection):
    """Connects blocking, then applies the timeout to the exchange (a timed connect costs ~14 ms on Windows loopback)."""

    def connect(self):
        s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        try:
            s.settimeout(None)
            s.connect((self.host, self.port))
            s.settimeout(self.timeout)
        except BaseException:
            s.close()
            raise
        self.sock = s


class Bridge:
    def __init__(self, port, timeout=120.0):
        self.port = port
        self.timeout = timeout

    def text(self, req, timeout=None):
        """The reply body text of one request (a dict or JSON text) on a fresh connection."""
        body = req.encode("utf-8") if isinstance(req, str) else json.dumps(req, ensure_ascii=False).encode("utf-8")
        conn = _Conn("127.0.0.1", self.port, timeout=timeout or self.timeout)
        try:
            conn.putrequest("POST", "/", skip_accept_encoding=True)
            conn.putheader("Content-Type", "application/json; charset=utf-8")
            conn.putheader("Content-Length", str(len(body)))
            conn.endheaders(body)
            resp = conn.getresponse()
            data = resp.read()
            if resp.status != 200:
                raise RuntimeError("HTTP %s: %s" % (resp.status, data[:200].decode("utf-8", "replace")))
            return data.decode("utf-8")
        finally:
            conn.close()

    def call(self, req, timeout=None, check=True):
        r = json.loads(self.text(req, timeout))
        if check and not r.get("ok"):
            raise CmdError(req.get("cmd") if isinstance(req, dict) else "?", r)
        return r


# ---------------------------------------------------------------- the interpolation, independently of the plugin

def quat_from_euler(e):
    """Euler degrees applied z, then x, then y (the engine's order): q = qy * qx * qz, as (x, y, z, w)."""
    hx, hy, hz = (math.radians(v) * 0.5 for v in e)
    qx = (math.sin(hx), 0.0, 0.0, math.cos(hx))
    qy = (0.0, math.sin(hy), 0.0, math.cos(hy))
    qz = (0.0, 0.0, math.sin(hz), math.cos(hz))
    return qmul(qmul(qy, qx), qz)


def qmul(a, b):
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return (aw * bx + ax * bw + ay * bz - az * by,
            aw * by - ax * bz + ay * bw + az * bx,
            aw * bz + ax * by - ay * bx + az * bw,
            aw * bw - ax * bx - ay * by - az * bz)


def qnorm(q):
    n = math.sqrt(sum(c * c for c in q))
    return tuple(c / n for c in q) if n > 0 else (0.0, 0.0, 0.0, 1.0)


def slerp(a, b, u):
    d = sum(x * y for x, y in zip(a, b))
    if d < 0:
        b = tuple(-c for c in b)
        d = -d
    if d > 0.9995:
        return qnorm(tuple(x + (y - x) * u for x, y in zip(a, b)))
    th = math.acos(min(1.0, d))
    s = math.sin(th)
    wa, wb = math.sin((1 - u) * th) / s, math.sin(u * th) / s
    return tuple(x * wa + y * wb for x, y in zip(a, b))


def angle_between(a, b):
    """Degrees between two rotations (precise near 0)."""
    a, b = qnorm(a), qnorm(b)
    r = qmul((-a[0], -a[1], -a[2], a[3]), b)
    return math.degrees(2 * math.atan2(math.sqrt(r[0] ** 2 + r[1] ** 2 + r[2] ** 2), abs(r[3])))


def segment(keys, t, ease):
    n = len(keys)
    if n == 0 or t <= keys[0]["t"]:
        return 0, 0.0
    if t >= keys[-1]["t"]:
        return n - 1, 0.0
    i = max(k for k in range(n) if keys[k]["t"] <= t)
    if ease == "step":
        return i, 0.0
    return i, (t - keys[i]["t"]) / (keys[i + 1]["t"] - keys[i]["t"])


def lerp(a, b, u):
    return [x + (y - x) * u for x, y in zip(a, b)]


def eval_track(track, t):
    """(position-like members, rotation quaternion) of a track at t: transform {pos}, camera {target, dist, fov}."""
    keys = track["keys"]
    i, u = segment(keys, t, track.get("ease", "linear"))
    a = keys[i]
    if u <= 0:
        b, u = a, 0.0
    else:
        b = keys[i + 1]
    q = slerp(quat_from_euler(a["rot"]), quat_from_euler(b["rot"]), u) if u > 0 else quat_from_euler(a["rot"])
    if track["kind"] == "transform":
        return {"pos": lerp(a["pos"], b["pos"], u)}, q
    return {"target": lerp(a["target"], b["target"], u), "dist": a["dist"] + (b["dist"] - a["dist"]) * u,
            "fov": a["fov"] + (b["fov"] - a["fov"]) * u}, q


# ---------------------------------------------------------------- the fixture

def fixture_sequence(a, b):
    """The fixture's sequence over items a and b: 3 s, two transform tracks (b steps), the camera, 3 renames."""
    return {
        "duration": 3.0, "loop": False, "autoplay": False,
        "tracks": [
            {"kind": "transform", "key": a, "ease": "linear", "keys": [
                {"t": 0.0, "pos": [0.0, 0.0, 0.0], "rot": [0.0, 0.0, 0.0]},
                {"t": 1.5, "pos": [1.0, 0.5, 0.0], "rot": [0.0, 350.0, 0.0]},
                {"t": 3.0, "pos": [2.0, 0.0, 1.0], "rot": [20.0, 10.0, 5.0]}]},
            {"kind": "transform", "key": b, "ease": "step", "keys": [
                {"t": 0.0, "pos": [-1.0, 0.0, 0.0], "rot": [0.0, 0.0, 0.0]},
                {"t": 1.0, "pos": [-1.0, 1.0, 0.0], "rot": [0.0, 90.0, 0.0]},
                {"t": 2.0, "pos": [-1.0, 0.0, 0.5], "rot": [30.0, 0.0, 0.0]}]},
            {"kind": "camera", "ease": "linear", "keys": [
                {"t": 0.0, "target": [0.0, 1.0, 0.0], "rot": [10.0, 180.0, 0.0], "dist": 4.0, "fov": 30.0},
                {"t": 3.0, "target": [0.5, 1.0, 1.0], "rot": [15.0, 210.0, 0.0], "dist": 5.0, "fov": 35.0}]}],
        "events": [
            {"t": 0.5, "do": {"cmd": "obj.rename", "key": a, "name": "seq-ev1"}, "note": "first"},
            {"t": 1.25, "do": {"cmd": "obj.rename", "key": b, "name": "seq-ev2"}, "note": "second"},
            {"t": 2.5, "do": {"cmd": "obj.rename", "key": a, "name": "seq-ev3"}, "note": "third"}],
    }


# ---------------------------------------------------------------- helpers

class MEMORYSTATUSEX(ctypes.Structure):
    _fields_ = [("dwLength", ctypes.c_ulong), ("dwMemoryLoad", ctypes.c_ulong),
                ("ullTotalPhys", ctypes.c_ulonglong), ("ullAvailPhys", ctypes.c_ulonglong),
                ("ullTotalPageFile", ctypes.c_ulonglong), ("ullAvailPageFile", ctypes.c_ulonglong),
                ("ullTotalVirtual", ctypes.c_ulonglong), ("ullAvailVirtual", ctypes.c_ulonglong),
                ("ullAvailExtendedVirtual", ctypes.c_ulonglong)]


def free_gb():
    try:
        m = MEMORYSTATUSEX()
        m.dwLength = ctypes.sizeof(m)
        if ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(m)):
            return m.ullAvailPhys / 2.0 ** 30
    except Exception:
        pass
    return None


def port_listeners(port):
    p = subprocess.run(["netstat", "-ano", "-p", "TCP"], capture_output=True, timeout=60, creationflags=CREATE_NO_WINDOW)
    pids = set()
    for line in p.stdout.decode("oem", "replace").splitlines():
        t = line.split()
        if len(t) >= 5 and t[0] == "TCP" and t[3] == "LISTENING" and t[1].rpartition(":")[2] == str(port):
            try:
                pids.add(int(t[-1]))
            except ValueError:
                pass
    return pids


def game_processes():
    """(process name prefixes of the game gate, warning or None) from Tools\\session_gate.json (a local file naming the
    machine owner's processes; never shipped). Missing or unreadable: no game gate, with a warning."""
    try:
        with open(GATE_FILE, encoding="utf-8") as f:
            names = [str(x).lower() for x in json.load(f).get("processes") or [] if str(x).strip()]
        return names, (None if names else "session_gate.json names no process: no game gate")
    except (OSError, ValueError, TypeError, AttributeError) as e:
        return [], "session_gate.json not read (%s): no game gate" % type(e).__name__


def running_games():
    p = subprocess.run(["tasklist", "/FO", "CSV", "/NH"], capture_output=True, timeout=60, creationflags=CREATE_NO_WINDOW)
    names = set()
    for line in p.stdout.decode("oem", "replace").splitlines():
        name = line.split(",")[0].strip('"').lower()
        for g in game_processes()[0]:
            if name.startswith(g):
                names.add(name)
    return sorted(names)


def is_reparse(path):
    try:
        st = os.lstat(path)
    except OSError:
        return False
    return bool(getattr(st, "st_file_attributes", 0) & 0x400)


def manifest_guid(folder):
    try:
        with open(os.path.join(folder, "manifest.json"), encoding="utf-8-sig") as f:
            return str(json.load(f).get("guid") or "")
    except (OSError, ValueError):
        return None


def test_set(project):
    """GUIDs of the plugin projects in the repository (Samples\\*, Samples\\PluginKit\\*, Tests\\Plugins\\*), lower case."""
    out = set()
    for base in (os.path.join(project, "Samples"), os.path.join(project, "Samples", "PluginKit"), os.path.join(project, "Tests", "Plugins")):
        if not os.path.isdir(base):
            continue
        for d in os.listdir(base):
            g = manifest_guid(os.path.join(base, d))
            if g:
                out.add(g.lower())
    return out


def raw_member(text, name):
    """The raw JSON text of a top-level member of a compact reply (byte-exact), or None."""
    key = '"%s":' % name
    i = text.find(key)
    if i < 0:
        return None
    j = i + len(key)
    _, end = json.JSONDecoder().raw_decode(text, j)
    return text[j:end]


def ps_file(script, args, timeout=600):
    exe = shutil.which("pwsh") or shutil.which("powershell") or "powershell.exe"
    cmd = [exe, "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script] + args
    p = subprocess.run(cmd, capture_output=True, timeout=timeout, creationflags=CREATE_NO_WINDOW)
    return p.returncode, p.stdout.decode("utf-8", "replace") + p.stderr.decode("utf-8", "replace")


# ---------------------------------------------------------------- results and the run context

class Results:
    def __init__(self):
        self.items = []

    def add(self, cid, verdict, text, **data):
        self.items.append({"id": cid, "verdict": verdict, "text": text, "data": data})
        log("%-6s %-4s %s" % (cid, verdict, text))

    def count(self, v):
        return sum(1 for x in self.items if x["verdict"] == v)


class Ctx:
    def __init__(self, a):
        self.a = a
        self.port = a.port
        self.out = os.path.abspath(a.out)
        self.project = os.path.abspath(a.project) if a.project else None
        self.exe = os.path.abspath(a.exe) if a.exe else None
        self.plugins_dir = os.path.join(os.path.dirname(self.exe), "Plugins") if self.exe else None
        self.br = Bridge(a.port)
        self.R = Results()
        self.proc = None
        self.pid = None
        self.aborted = None
        self.info = {}
        self.staged = []
        self.created_dirs = []
        self.dirs_before = set()
        self.only = set(x.strip().upper() for x in a.only.split(",")) if a.only else None
        self.deadline = None
        self.guids = [GUID] + ([] if a.no_kit else [g for g, _, _ in KIT + [TOOL_PROBE, STAGE_PROBE]])
        self.extra_unloads = 0

    def want(self, cid):
        return self.only is None or cid in self.only

    def alive(self):
        if self.aborted:
            raise Abort(self.aborted)
        if self.proc is not None and self.proc.poll() is not None:
            self.aborted = "the player exited with code %s" % self.proc.returncode
            raise Abort(self.aborted)

    def call(self, req, timeout=120, check=True):
        self.alive()
        try:
            return self.br.call(req, timeout=timeout, check=check)
        except (OSError, http.client.HTTPException):
            self.alive()
            raise

    def text(self, req, timeout=120):
        self.alive()
        try:
            return self.br.text(req, timeout=timeout)
        except (OSError, http.client.HTTPException):
            self.alive()
            raise

    def frames(self, n):
        """Waits n frames (a ping whose reply the bridge holds n frames)."""
        return self.call({"cmd": "ping", "waitFrames": n}, timeout=120)


def guard(C, cid, fn):
    try:
        fn(C)
    except Abort:
        raise
    except Exception as e:
        C.R.add(cid, "FAIL", "exception: %s: %s" % (type(e).__name__, str(e)[:300]), traceback=traceback.format_exc(limit=6))
        C.alive()


# ---------------------------------------------------------------- setup: checks, build, stage, start

def preflight(C):
    a = C.a
    if not C.exe or not os.path.isfile(C.exe):
        raise SetupError("exe not found: %s" % C.exe)
    if not C.project or not os.path.isdir(C.project):
        raise SetupError("project root not found: %s" % C.project)
    gate_warning = game_processes()[1]
    if gate_warning:
        C.R.add("GATE", "WARN", gate_warning)
    games = running_games()
    if games:
        raise SetupError("the game gate: %s runs; no player row now" % ", ".join(games))
    busy = port_listeners(C.port)
    if busy:
        raise SetupError("port %d is already in use (PID %s)" % (C.port, ", ".join(map(str, sorted(busy)))))
    allowed = test_set(C.project)
    C.info["testSet"] = sorted(allowed)
    if GUID not in allowed:
        raise SetupError("the sample's manifest (Samples\\Sequencer\\manifest.json) was not found")
    missing = [g for g in C.guids if g not in allowed]
    if missing:
        raise SetupError("the kit samples' manifests (Samples\\PluginKit\\*\\manifest.json) were not found: %s" % ", ".join(missing))
    foreign, others = [], []
    if os.path.isdir(C.plugins_dir):
        for d in sorted(os.listdir(C.plugins_dir)):
            full = os.path.join(C.plugins_dir, d)
            if not os.path.isdir(full):
                continue
            g = manifest_guid(full)
            if g is None:
                continue
            if g.lower() not in allowed:
                foreign.append(g or d)
            elif g.lower() not in C.guids:
                others.append(g)
    C.info["otherTestPlugins"] = others
    if foreign:
        raise SetupError("%s holds plugins outside the test set: %s (move them away; this row does not touch them)"
                         % (C.plugins_dir, ", ".join(foreign)))
    need, end = a.min_free_gb, time.time() + a.ram_wait_min * 60
    g = free_gb()
    while g is not None and g < need and time.time() < end:
        log("free RAM %.1f GB < %.1f GB: waiting" % (g, need))
        time.sleep(15)
        g = free_gb()
    C.info["freeGb"] = round(g, 1) if g is not None else None
    if g is not None and g < need:
        C.R.add("RAM", "WARN", "free RAM %.1f GB < %.1f GB; started anyway" % (g, need))


def build_and_stage(C):
    a = C.a
    tool = os.path.join(C.project, "Tools", "plugins.ps1")
    kit_api = a.kit_api_dir or os.path.join(os.path.dirname(C.exe), "HS2Next_Data", "Managed")
    plan = [(GUID, os.path.join("Samples", "Sequencer"), "Stp3.SampleSequencer.dll", a.api_dir)]
    if not a.no_kit:
        plan += [(g, rel, dll, kit_api) for g, rel, dll in KIT + [TOOL_PROBE, STAGE_PROBE]]
    C.info["build"], C.info["verify"], C.info["apiDirs"] = {}, {}, {}
    for guid, rel, dll_name, api in plan:
        src = os.path.join(C.project, rel)
        dll = os.path.join(src, "bin", "Release", "netstandard2.1", dll_name)
        C.info["apiDirs"][guid] = api or "Library\\ScriptAssemblies"
        if not a.no_build:
            args = ["-Build", src] + (["-ApiDir", api] if api else [])
            code, out = ps_file(tool, args)
            C.info["build"][guid] = out[-2000:]
            if code != 0:
                raise SetupError("plugins.ps1 -Build %s failed (exit %s): %s" % (rel, code, out.strip()[-400:]))
        if not os.path.isfile(dll):
            raise SetupError("%s was not built: %s" % (guid, dll))
        code, out = ps_file(tool, ["-Verify", dll])
        C.info["verify"][guid] = out.strip()
        if code != 0:
            raise SetupError("plugins.ps1 -Verify failed: %s" % out.strip()[-400:])
    # Folders this run creates (removed afterwards when empty); none of the path may be a reparse point.
    probe = C.plugins_dir
    while probe and not os.path.exists(probe):
        probe = os.path.dirname(probe)
    p = probe
    while p and os.path.dirname(p) != p:
        if is_reparse(p):
            raise SetupError("a reparse point lies on the plugin path: %s" % p)
        p = os.path.dirname(p)
    C.targets = [os.path.join(C.plugins_dir, g) for g in C.guids]
    for d in [C.plugins_dir] + C.targets:
        if not os.path.exists(d):
            C.created_dirs.append(d)
    before = {}
    C.dirs_before = set()
    for target in C.targets:
        if os.path.isdir(target):
            for root, dirs, files in os.walk(target):
                C.dirs_before.add(os.path.normcase(os.path.abspath(root)))
                for f in files:
                    before[os.path.abspath(os.path.join(root, f))] = True
    report = os.path.join(C.out, "stage_report.txt")
    if os.path.exists(report):
        os.remove(report)
    code, out = ps_file(tool, ["-Stage", C.plugins_dir, "-Only", ",".join(C.guids), "-Report", report])
    C.info["stage"] = out.strip()[-2000:]
    if code != 0:
        raise SetupError("plugins.ps1 -Stage failed (exit %s): %s" % (code, out.strip()[-400:]))
    staged = []
    if os.path.isfile(report):
        with open(report, encoding="utf-8-sig") as f:
            staged = [l.strip() for l in f if l.strip()]
    # Only files this run created are removed afterwards; files that were there before are left as they are.
    C.staged = [x for x in staged if os.path.abspath(x) not in before]
    C.info["staged"] = staged
    C.info["preexisting"] = sorted(before)
    for target in C.targets:
        if not os.path.isfile(os.path.join(target, "manifest.json")):
            raise SetupError("nothing staged at %s" % target)


def unstage(C):
    if C.a.keep_staged or C.a.attach or not C.plugins_dir:
        return
    targets = [os.path.normcase(os.path.abspath(t)) for t in (getattr(C, "targets", None) or [os.path.join(C.plugins_dir, GUID)])]

    def owner(path):
        return next((t for t in targets if path == t or path.startswith(t + os.sep)), None)

    removed, failed = [], []
    for f in C.staged:
        full = os.path.abspath(f)
        if owner(os.path.normcase(full)) is None:
            failed.append(full + " (outside the plugin folders: left)")
            continue
        try:
            if os.path.isfile(full) and not is_reparse(full):
                os.remove(full)
                removed.append(full)
        except OSError as e:
            failed.append("%s (%s)" % (full, e))
    # Folders this run created, deepest first, only when empty and not a link (never a recursive delete).
    made = set()
    for f in C.staged:
        d = os.path.normcase(os.path.dirname(os.path.abspath(f)))
        t = owner(d)
        while t and d.startswith(t) and d not in getattr(C, "dirs_before", set()):
            made.add(d)
            d = os.path.dirname(d)
    made.update(os.path.normcase(os.path.abspath(d)) for d in C.created_dirs)
    for d in sorted(made, key=len, reverse=True):
        try:
            if os.path.isdir(d) and not is_reparse(d) and not os.listdir(d):
                os.rmdir(d)
        except OSError:
            pass
    C.R.add("CLEAN", "PASS" if not failed else "WARN", "removed %d staged file(s)%s" % (len(removed), ("; left: " + "; ".join(failed)) if failed else ""),
            removed=removed, failed=failed)


def start_player(C):
    a = C.a
    settings = os.path.join(C.project, "Config", "settings.local.json")
    if not os.path.isfile(settings):
        raise SetupError("settings file not found: %s" % settings)
    cache = os.path.join(C.project, "Cache")
    ud = os.path.join(C.out, "userdata")
    bo = os.path.join(C.out, "bridge_out")
    logf = os.path.join(C.out, "player.log")
    ep = os.path.join(ud, "Bridge", "endpoint.json")
    if os.path.isfile(ep):
        os.remove(ep)
    env = dict(os.environ)
    env.pop("HS2NEXT_NO_PLUGINS", None)
    env["HS2NEXT_PRIVATE_OFF"] = "1"
    args = [C.exe, "-batchmode", "-bridgePort", str(C.port), "-bridgeAllowQuit", "-settings", settings,
            "-cacheDir", cache, "-userData", ud, "-bridgeOutputDir", bo, "-logFile", logf]
    C.info["commandLine"] = args
    log("starting the player: " + subprocess.list2cmdline(args))
    t_start = time.time()
    C.proc = subprocess.Popen(args, cwd=os.path.dirname(C.exe), env=env, stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL,
                              stderr=subprocess.DEVNULL, creationflags=CREATE_NO_WINDOW)
    C.pid = C.proc.pid
    deadline = min(C.deadline, t_start + a.ready_timeout_min * 60)
    last, last_err = None, None
    while True:
        if C.proc.poll() is not None:
            raise SetupError("the player exited with code %s before it was ready (see %s)" % (C.proc.returncode, logf))
        if C.aborted:
            raise SetupError("before the player was ready: " + C.aborted)
        try:
            r = C.br.call({"cmd": "ping"}, timeout=5, check=False)
            last = r
            if r.get("appReady"):
                break
        except (OSError, http.client.HTTPException, ValueError, RuntimeError) as e:
            last_err = "%s: %s" % (type(e).__name__, e)
        if time.time() > deadline:
            raise SetupError("not ready after %.0f s; last ping %s; last error %s"
                             % (time.time() - t_start, json.dumps(last)[:300] if last else None, last_err))
        time.sleep(0.5)
    ready_s = time.time() - t_start
    pid_seen = None
    for _ in range(20):
        try:
            with open(ep, encoding="utf-8") as f:
                pid_seen = json.load(f).get("pid")
            break
        except (OSError, ValueError):
            time.sleep(0.25)
    if pid_seen != C.pid:
        raise SetupError("endpoint.json names PID %s, the started player is PID %s" % (pid_seen, C.pid))
    return ready_s


def setup(C):
    a = C.a
    os.makedirs(C.out, exist_ok=True)
    if a.attach:
        r = C.br.call({"cmd": "ping"}, timeout=10, check=False)
        if not r.get("appReady"):
            raise SetupError("attach: the bridge on port %d is not ready" % C.port)
        ready_s = 0.0
    else:
        preflight(C)
        if not a.make_fixture:
            fixture = os.path.join(C.project, FIXTURE_REL)
            if not os.path.isfile(fixture):
                raise SetupError("fixture not found: %s (make it with --make-fixture)" % fixture)
            sd = os.path.join(C.out, "userdata", "scene")
            os.makedirs(sd, exist_ok=True)
            shutil.copyfile(fixture, os.path.join(sd, FIXTURE_NAME + ".hs2nscene"))
        build_and_stage(C)
        ready_s = start_player(C)
    p = C.call({"cmd": "ping"})
    v = C.call({"cmd": "bridge.version"})
    C.info["ping"] = {k: p.get(k) for k in ("protocol", "app", "bridge", "api", "frame", "screen")}
    C.info["version"] = {k: v.get(k) for k in v if k not in ("ok", "cmd")}
    C.R.add("SETUP", "PASS", "ready in %.1f s (pid %s, port %d); api %s; built against %s; staged %d file(s)%s"
            % (ready_s, C.pid, C.port, p.get("api"),
               ", ".join("%s: %s" % (g, d) for g, d in (C.info.get("apiDirs") or {}).items()) or "(attached)", len(C.staged),
               ("; other test plugins present: " + ", ".join(C.info.get("otherTestPlugins") or [])) if C.info.get("otherTestPlugins") else ""),
            readySeconds=round(ready_s, 2))


def quit_player(C):
    if C.a.attach or C.proc is None:
        return
    if C.proc.poll() is not None:
        C.R.add("PA9", "FAIL", "the player had already exited with code %s" % C.proc.returncode)
        return
    t = time.time()
    reply, err = None, None
    try:
        reply = C.br.call({"cmd": "app.quit", "code": 0}, timeout=30)
    except Exception as e:
        err = "%s: %s" % (type(e).__name__, e)
    try:
        code = C.proc.wait(60)
        killed = False
    except subprocess.TimeoutExpired:
        C.proc.kill()
        code = C.proc.wait(30)
        killed = True
    dt = time.time() - t
    lines = []
    try:
        with open(os.path.join(C.out, "player.log"), encoding="utf-8", errors="replace") as f:
            lines = [l.rstrip() for l in f if GUID in l]
    except OSError:
        pass
    unload = [l for l in lines if ("[plugin " + GUID + "]") in l and l.rstrip().endswith("unloaded")]
    want = 1 + C.extra_unloads
    ok = reply is not None and reply.get("quitting") is True and not killed and code == 0 and len(unload) == want
    C.R.add("PA9", "PASS" if ok else "FAIL",
            "app.quit %s; exit code %s in %.1f s%s; the plugin's unload line %s"
            % ("answered quitting:true" if reply and reply.get("quitting") else "failed (%s)" % err, code, dt,
               "; killed after 60 s" if killed else "", ("found %d (%s): %s" % (len(unload), "quit and KD9's disable" if want == 2 else "quit", unload[-1].strip()[:120]))
               if len(unload) == want else "seen %d times, expected %d" % (len(unload), want)),
            exitCode=code, seconds=round(dt, 2), killed=killed, pluginLines=lines[-20:])


def kill_player(C):
    if C.proc is not None and C.proc.poll() is None:
        try:
            C.proc.kill()
            C.proc.wait(30)
        except Exception:
            pass


# ---------------------------------------------------------------- scene helpers

def add_item(C):
    return C.call({"cmd": "item.add", "no": ITEM_NO})["added"][0]["key"]


def load_fixture(C):
    return C.call({"cmd": "scene.load", "name": FIXTURE_NAME}, timeout=300)


def status(C):
    return C.call({"cmd": "seq.status"})


def plugin_entry(pl):
    for p in pl.get("plugins") or []:
        if str(p.get("guid", "")).lower() == GUID:
            return p
    return None


def node_doc(reply):
    node = reply.get("node")
    return json.loads(node) if isinstance(node, str) else node


def sample_frame(C, keys):
    """One batch (one pump): seq.status, scene.doc of each key, camera.get, scene (names)."""
    cmds = [{"cmd": "seq.status"}] + [{"cmd": "scene.doc", "key": k, "capture": False, "embed": True} for k in keys] + \
           [{"cmd": "camera.get"}, {"cmd": "scene"}]
    r = C.call({"cmd": "batch", "cmds": cmds})
    replies = r.get("results") or r.get("replies") or r.get("cmds")
    if not isinstance(replies, list) or len(replies) != len(cmds):
        raise RuntimeError("unexpected batch reply: %s" % json.dumps(r)[:300])
    st = replies[0]
    nodes = [node_doc(x) for x in replies[1:1 + len(keys)]]
    cam = replies[1 + len(keys)].get("camera") or {}
    names = {o.get("key"): o.get("name") for o in replies[-1].get("objects") or []}
    return st, nodes, cam, names


# ---------------------------------------------------------------- the checks

def entry_of(pl, guid):
    for p in pl.get("plugins") or []:
        if str(p.get("guid", "")).lower() == guid:
            return p
    return None


def pa1(C):
    v = C.call({"cmd": "bridge.version"})
    plugins = v.get("plugins") or []
    by = {str(p.get("guid", "")).lower(): p for p in plugins}
    want = {GUID: VERSION}
    want.update({g: KIT_VERSION for g in C.guids if g != GUID})
    bad = [g for g, ver in want.items() if not (by.get(g) and by[g].get("version") == ver and by[g].get("state") == "loaded")]
    allowed = set(C.info.get("testSet") or want)
    others = [p for p in plugins if str(p.get("guid", "")).lower() not in allowed]
    pl = C.call({"cmd": "plugin.list"})
    e = plugin_entry(pl) or {}
    want_dir = os.path.normcase(os.path.abspath(os.path.join(C.plugins_dir, GUID))) if C.plugins_dir else None
    got_dir = os.path.normcase(os.path.abspath(e.get("dir"))) if e.get("dir") else None
    ok = (not bad and not others and (want_dir is None or got_dir == want_dir) and pl.get("source") in (None, "settings"))
    C.info["pluginList"] = pl
    C.R.add("PA1", "PASS" if ok else "FAIL",
            "%d of %d expected plugins loaded%s; bridge.version.plugins %s; plugin.list dir %s (%s), root %s, source %s"
            % (len(want) - len(bad), len(want), (" (not loaded: %s)" % ", ".join(bad)) if bad else "", json.dumps(plugins),
               e.get("dir"), "next to the exe" if got_dir == want_dir else "expected " + str(want_dir), pl.get("root"), pl.get("source")),
            plugins=plugins)


def pa2(C):
    h = C.call({"cmd": "help"})
    seq = sorted([c for c in h.get("commands") or [] if str(c.get("name", "")).startswith("seq.")], key=lambda c: c["name"])
    names = [c["name"] for c in seq]
    owners = set(c.get("owner") for c in seq)
    groups = set(c.get("group") for c in seq)
    ok = names == SEQ_COMMANDS and owners == {GUID} and groups == {GUID}
    C.R.add("PA2", "PASS" if ok else "FAIL", "help: %d seq.* commands %s; owners %s; groups %s; %d commands in all"
            % (len(names), ",".join(n[4:] for n in names), sorted(owners), sorted(groups), h.get("count") or 0), names=names)


def pa3(C):
    pl = C.call({"cmd": "plugin.list"})
    e = plugin_entry(pl) or {}
    events = e.get("events")
    load_fixture(C)
    s1 = status(C)
    C.call({"cmd": "scene.new"})
    s2 = status(C)
    load_fixture(C)
    s3 = status(C)
    ok = (isinstance(events, int) and events >= 5 and len(s1["tracks"]) == 3 and s1["state"] == "stopped"
          and len(s2["tracks"]) == 0 and len(s3["tracks"]) == 3)
    C.R.add("PA3", "PASS" if ok else "FAIL", "plugin.list events %s (>= 5); seq.status tracks after load / new / load: %d / %d / %d (%s / %s / %s)"
            % (events, len(s1["tracks"]), len(s2["tracks"]), len(s3["tracks"]), s1["state"], s2["state"], s3["state"]))


def pa4(C):
    load_fixture(C)
    seq = json.loads(raw_member(C.text({"cmd": "seq.get"}), "sequence"))
    tracks = seq["tracks"]
    keys = [t["key"] for t in tracks if t["kind"] == "transform"]
    cam_track = next((t for t in tracks if t["kind"] == "camera"), None)
    C.frames(2)
    C.call({"cmd": "seq.play"})
    samples, worst_pos, worst_rot, worst_cam, worst_cam_rot = [], 0.0, 0.0, 0.0, 0.0
    first_seen = {}
    expect_names = {}
    for ev in seq["events"]:
        expect_names.setdefault((ev["do"]["key"], ev["do"]["name"]), ev["t"])
    t_end = time.time() + 30
    last = None
    while time.time() < t_end:
        st, nodes, cam, names = sample_frame(C, keys)
        t = st["time"]
        last = st
        if 0 < t < seq["duration"] or st["state"] == "ended":
            samples.append(t)
            for tr, node in zip([x for x in tracks if x["kind"] == "transform"], nodes):
                exp, q = eval_track(tr, t)
                got = node["transform"]
                worst_pos = max(worst_pos, max(abs(x - y) for x, y in zip(exp["pos"], got["pos"])))
                worst_rot = max(worst_rot, angle_between(q, quat_from_euler(got["rot"])))
            if cam_track and cam:
                exp, q = eval_track(cam_track, t)
                worst_cam = max(worst_cam, max(abs(x - y) for x, y in zip(exp["target"], cam["target"])),
                                abs(exp["dist"] - cam["dist"]), abs(exp["fov"] - cam["fov"]))
                worst_cam_rot = max(worst_cam_rot, angle_between(q, quat_from_euler(cam["rot"])))
        for (k, n), te in expect_names.items():
            if names.get(k) == n and (k, n) not in first_seen:
                first_seen[(k, n)] = t
        if st["state"] == "ended":
            break
    st = status(C)
    distinct = sorted(set(round(x, 6) for x in samples))
    late = {"%s=%s" % k: round(first_seen[k] - te, 3) for k, te in expect_names.items() if k in first_seen}
    timely = all(k in first_seen and 0 <= first_seen[k] - te <= 0.5 for k, te in expect_names.items())
    # the third event renames a again: its first name must have been seen before the third
    ok = (len(distinct) >= 3 and worst_pos <= 1e-4 and worst_rot <= 1e-3 and worst_cam <= 1e-3 and worst_cam_rot <= 1e-3
          and timely and st["eventsFired"] == 3 and st["eventErrors"] == 0 and st["state"] == "ended" and st["writes"]["error"] == 0)
    C.R.add("PA4", "PASS" if ok else "FAIL",
            "%d sampled frames (t %.3f-%.3f); worst position %.2e, rotation %.2e deg, view %.2e / %.2e deg; events seen after their "
            "times by %s s; eventsFired %s, eventErrors %s; state %s; writes %s"
            % (len(distinct), distinct[0] if distinct else -1, distinct[-1] if distinct else -1, worst_pos, worst_rot, worst_cam,
               worst_cam_rot, json.dumps(late), st["eventsFired"], st["eventErrors"], st["state"], json.dumps(st["writes"])),
            samples=distinct, lastStatus=last)


def pa5(C):
    C.call({"cmd": "seq.stop"})
    C.frames(2)
    before = C.text({"cmd": "seq.get"})
    saved = C.call({"cmd": "scene.save", "name": "pa5_saved", "overwrite": True, "thumb": "placeholder"}, timeout=300)
    C.call({"cmd": "scene.new"})
    mid = C.call({"cmd": "seq.get"})
    C.call({"cmd": "scene.load", "name": "pa5_saved"}, timeout=300)
    after = C.text({"cmd": "seq.get"})
    with zipfile.ZipFile(saved["path"]) as z:
        man = json.loads(z.read("manifest.json").decode("utf-8"))
    entries = [e for e in man.get("ext") or [] if str(e.get("ns", "")).lower() == GUID]
    want = {"ns": GUID, "scope": "scene", "root": "", "version": 1, "required": False}
    entry_ok = len(entries) == 1 and all(entries[0].get(k) == v for k, v in want.items())
    ok = before == after and mid.get("sequence") is None and entry_ok and man.get("formatVersion") == 1
    C.R.add("PA5", "PASS" if ok else "FAIL",
            "seq.get after save -> new -> load %s (%d bytes); after scene.new %s; manifest formatVersion %s, ext entry %s"
            % ("byte-equal" if before == after else "DIFFERS", len(after), "no sequence" if mid.get("sequence") is None else "a sequence",
               man.get("formatVersion"), json.dumps(entries)),
            before=before[:2000], after=after[:2000])


def pa6(C):
    st = status(C)
    tr = [t for t in st["tracks"] if t["kind"] == "transform"]
    key = tr[0]["key"]
    C.call({"cmd": "delete", "key": key})
    a = next(t for t in status(C)["tracks"] if t.get("key") == key)
    C.call({"cmd": "undo"})
    b = next(t for t in status(C)["tracks"] if t.get("key") == key)
    s2 = status(C)
    ok = a["active"] is False and b["active"] is True and s2["writes"]["missingNode"] == 0
    C.R.add("PA6", "PASS" if ok else "FAIL", "delete %s: track active %s; undo: active %s; missingNode %s"
            % (key[:12], a["active"], b["active"], s2["writes"]["missingNode"]))


def diag_of(pl):
    d = pl.get("diagnostics")
    if d is None:
        e = plugin_entry(pl) or {}
        d = e.get("diagnostics")
    return d or {}


def owned_counts(pl):
    e = plugin_entry(pl) or {}
    return {"commands": len(e.get("commands") or []), "extNamespaces": len(e.get("extNamespaces") or []), "events": e.get("events"),
            "hooks": len(e.get("hooks") or []), "supplies": e.get("supplies"), "state": e.get("state")}


def census_diff(a, b):
    """' (gameObjects -x, +y; materials ...)' between two rows' app.perf census, '' without a census."""
    ca, cb = a.get("census"), b.get("census")
    if not ca or not cb:
        return ""
    parts = []
    for kind in ("gameObjects", "materials"):
        x, y = ca.get(kind) or {}, cb.get(kind) or {}
        d = []
        for k in sorted(set(x) | set(y)):
            n = (y.get(k) or 0) - (x.get(k) or 0)
            if n:
                d.append(("+" if n > 0 else "-") + k + (" x%d" % abs(n) if abs(n) > 1 else ""))
        parts.append(kind + " " + (", ".join(d) if d else "="))
    return " (" + "; ".join(parts) + ")"


def managed_growth(series, budget=2 * MIB):
    """PA7's managed memory: the samples of the measured cycles (cycle 0 first), in bytes or any one unit with the budget.
    The least-squares slope per cycle times the number of cycles must stay within the budget, so that a leak fails however it
    is spread and one jump near the end of a flat series does not. Returns (ok, slope per cycle, slope x cycles). With fewer than
    two samples nothing has grown."""
    n = len(series)
    cycles = n - 1
    if cycles < 1:
        return True, 0.0, 0.0
    mean_x = cycles / 2.0
    mean_y = sum(series) / float(n)
    sxx = sum((x - mean_x) ** 2 for x in range(n))
    sxy = sum((x - mean_x) * (y - mean_y) for x, y in enumerate(series))
    slope = sxy / sxx
    grown = slope * cycles
    return grown <= budget, slope, grown


def selftest():
    """The worked series of managed_growth, in MiB; no player. Returns the failure count."""
    rng = random.Random(63)
    n = 21
    half = n // 2
    cases = [
        # name, series, expected verdict, expected slope x cycles (None: not checked)
        ("one jump of +2.36 at the last of 21 samples", [0.0] * (n - 1) + [2.36], True, 20 * (10 * 2.36) / 770),
        ("growth of 0.15 per cycle", [0.15 * i for i in range(n)], False, 3.0),
        ("noise +-0.14, alternating", [0.14 * (-1) ** i for i in range(n)], True, 0.0),
        ("noise +-0.14, the worst arrangement (- then +)", [-0.14 if i < half else (0.0 if i == half else 0.14) for i in range(n)], True, 0.4),
        ("noise +-0.14, seeded", [rng.choice((-0.14, 0.14)) for _ in range(n)], True, None),
        ("flat", [5.0] * n, True, 0.0),
        ("a jump and back, late", [0.0] * (n - 3) + [2.36, -5.59 + 2.36, 0.0], True, None),
        ("growth just under the budget", [0.0999 * i for i in range(n)], True, 1.998),
        ("growth just over the budget", [0.1001 * i for i in range(n)], False, 2.002),
        ("a jump of +2.36 at sample 1 that stays", [0.0] + [2.36] * (n - 1), True, 20 * (10 * 2.36) / 770),
        ("a jump of +2.36 at the middle that stays", [0.0] * half + [2.36] * (n - half), False, 20 * (55 * 2.36) / 770),
        ("two cycles: the last minus the first", [0.0, 3.0], False, 3.0),
        ("a shrinking series", [3.0 - 0.1 * i for i in range(n)], True, -2.0),
        ("one sample", [7.0], True, 0.0),
        ("no sample", [], True, 0.0),
    ]
    bad = 0
    for name, series, want, want_grown in cases:
        ok, slope, grown = managed_growth(series, 2.0)
        fine = ok == want and (want_grown is None or abs(grown - want_grown) < 1e-9)
        bad += 0 if fine else 1
        print("%s %s: slope %+.4f MiB per cycle, x cycles %+.3f MiB: %s" % ("PASS" if fine else "FAIL", name, slope, grown, "pass" if ok else "fail"))
    # the same verdict in bytes, with the default budget (what pa7 calls)
    ok_b = managed_growth([b * MIB for b in [0.0] * 20 + [2.36]])[0]
    fail_b = managed_growth([0.15 * i * MIB for i in range(21)])[0]
    fine = ok_b and not fail_b
    bad += 0 if fine else 1
    print("%s in bytes with the default budget: jump %s, growth %s" % ("PASS" if fine else "FAIL", ok_b, fail_b))
    print("SUMMARY: %d PASS, %d FAIL" % (len(cases) + 1 - bad, bad))
    return bad


def pa7(C):
    a = C.a
    log_since = C.call({"cmd": "log.tail", "n": 1}).get("last") or 0
    rows = []
    total = a.warmup + a.cycles + 1
    for i in range(total):
        load_fixture(C)
        C.call({"cmd": "seq.play"})
        C.frames(60)
        C.call({"cmd": "seq.stop"})
        C.call({"cmd": "scene.new"})
        C.frames(2)
        pl = C.call({"cmd": "plugin.list", "diagnostics": True}, timeout=120)
        st = status(C)
        d = diag_of(pl)
        # the catalog's UnloadUnusedAssets calls started so far, and the objects census (players from 353abc4 on: app.perf
        # census / unloadUnused.started; older ones: the ended calls, no census)
        perf = C.call({"cmd": "app.perf", "objects": True, "census": True}, check=False)
        if not perf.get("ok"):
            perf = C.call({"cmd": "app.perf"}, check=False)
        uu = perf.get("unloadUnused") or {}
        rows.append({"cycle": i - a.warmup, "diag": d, "tracks": len(st["tracks"]), "owned": owned_counts(pl),
                     "uuaStarted": uu.get("started", uu.get("calls")), "census": (perf.get("objects") or {}).get("census")})
    base = rows[a.warmup]
    last = rows[-1]
    measured = rows[a.warmup:]
    fails = []
    dips = []

    def num(r, k):
        v = r["diag"].get(k)
        return v if isinstance(v, (int, float)) else None

    managed = [num(r, "managedBytes") for r in measured]
    slope, grown = 0.0, 0.0
    if any(m is None for m in managed):
        fails.append("plugin.list {diagnostics: true} has no managedBytes")
    else:
        managed_ok, slope, grown = managed_growth(managed)
        if not managed_ok:
            fails.append("managed slope %+.3f MiB per cycle x %d cycles = %+.2f MiB (budget 2)" % (slope / MIB, len(managed) - 1, grown / MIB))
        for k in ("materials", "meshes", "textures", "renderTextures"):
            if num(last, k) is None or num(last, k) > num(base, k) + 2:
                fails.append("%s %s -> %s" % (k, num(base, k), num(last, k)))
        # gameObjects: a leak grows - no measured cycle above the reference (cycle 0's count; cycle 1's when cycle 0 itself
        # dipped through an UnloadUnusedAssets), the last cycle not above it. A cycle below it is the catalog's rate-limited
        # UnloadUnusedAssets freeing the unreferenced prefab asset between that cycle's scene.new and its sample (the next
        # load brings it back): info when a call started in that cycle or the one before, else a FAIL.
        go = [num(r, "gameObjects") for r in measured]
        uua = [r.get("uuaStarted") for r in rows]
        k0 = a.warmup
        ref = go[0]
        if len(go) > 1 and go[1] > go[0] and isinstance(uua[k0 + 1], int) and isinstance(uua[k0], int) and uua[k0 + 1] > uua[k0]:
            ref = go[1]
        above = [r["cycle"] for r, g in zip(measured, go) if g > ref]
        if above or go[-1] > ref:
            fails.append("gameObjects above the reference %s in cycles %s (%s)" % (ref, above, go))
        for j, (r, g) in enumerate(zip(measured, go)):
            if g >= ref:
                continue
            s, before = uua[k0 + j], uua[max(0, k0 + j - 2)]
            what = "cycle %s gameObjects %s%s" % (r["cycle"], g, census_diff(rows[k0], r))
            if isinstance(s, int) and isinstance(before, int) and s > before:
                dips.append(what + " (UnloadUnusedAssets started %s -> %s)" % (before, s))
            else:
                fails.append("gameObjects dip without an UnloadUnusedAssets: " + what)
    if any(r["tracks"] != 0 for r in measured):
        fails.append("status.tracks not empty after scene.new")
    owned = [json.dumps(r["owned"], sort_keys=True) for r in measured]
    if len(set(owned)) != 1:
        fails.append("the plugin's registrations changed: %s" % sorted(set(owned)))
    tail = C.call({"cmd": "log.tail", "n": 1000, "level": "warning", "since": log_since})
    warnings = [e.get("message", "") for e in tail.get("entries") or [] if "[plugin" in e.get("message", "")]
    if warnings:
        fails.append("%d plugin warnings (first: %s)" % (len(warnings), warnings[0][:120]))
    db = (num(last, "managedBytes") or 0) - (num(base, "managedBytes") or 0)
    C.R.add("PA7", "PASS" if not fails else "FAIL",
            "%d warm-up + %d measured cycles; managed last - first %+.2f MiB, slope x cycles %+.2f MiB (budget 2); gameObjects %s; materials %s -> %s, "
            "meshes %s -> %s, textures %s -> %s, render textures %s -> %s; registrations %s%s"
            % (a.warmup, a.cycles, db / MIB, grown / MIB, num(last, "gameObjects"), num(base, "materials"), num(last, "materials"),
               num(base, "meshes"), num(last, "meshes"), num(base, "textures"), num(last, "textures"),
               num(base, "renderTextures"), num(last, "renderTextures"), owned[0] if owned else "-",
               ("; " + "; ".join(fails)) if fails else "")
            + (("; below the reference where the catalog's UnloadUnusedAssets ran (not a leak): " + " | ".join(dips)) if dips else ""),
            cycles=rows, pluginWarnings=warnings[:20])


def pa8(C):
    a = C.a
    C.call({"cmd": "scene.new"})
    keys = [add_item(C) for _ in range(8)]
    C.frames(2)
    start = C.call({"cmd": "seq.measure", "keys": keys, "frames": a.measure_frames})
    t_end = time.time() + max(120, a.measure_frames / 10)
    r = start
    while time.time() < t_end:
        r = C.call({"cmd": "seq.measure"})
        if r.get("state") != "running":
            break
        time.sleep(0.5)
    C.info["measure"] = r
    done = r.get("state") == "done"
    pm = {k: r.get(k) or {} for k in ("pm1", "pm1b", "pm2", "pm3", "pm4", "pm5")}
    ok = (done and all(pm[k].get("n", 1) for k in ("pm1", "pm2", "pm3", "pm4")) and pm["pm4"].get("lost") == 0
          and pm["pm5"].get("withMs") is not None and r.get("uiOn") is False)
    view = done and ((pm["pm1"].get("medianUs") or 0) > 100 or (pm["pm3"].get("medianMs") or 0) > 1.0)
    C.R.add("PA8", "PASS" if ok else "FAIL",
            "%s: PM1 camera.get %s / %s us; PM1b node read (%s) %s / %s us (info); PM2 obj.transform %s / %s us; PM3 update %s / %s ms "
            "(median / p95); PM4 queued read %s frames (max %s, lost %s); PM5 frame %s ms with / %s ms without; frames %s, ui %s -> threshold %s"
            % (r.get("state"), pm["pm1"].get("medianUs"), pm["pm1"].get("p95Us"), pm["pm1b"].get("cmd"), pm["pm1b"].get("medianUs"),
               pm["pm1b"].get("p95Us"), pm["pm2"].get("medianUs"), pm["pm2"].get("p95Us"),
               pm["pm3"].get("medianMs"), pm["pm3"].get("p95Ms"), pm["pm4"].get("medianFrames"), pm["pm4"].get("max"),
               pm["pm4"].get("lost"), pm["pm5"].get("withMs"), pm["pm5"].get("withoutMs"), r.get("frames"), r.get("uiOn"),
               "crossed (the view is indicated)" if view else "not crossed (no view)"),
            measure=r)


def check_log(C):
    path = os.path.join(C.out, "player.log")
    if C.a.attach or not os.path.isfile(path):
        return
    with open(path, encoding="utf-8", errors="replace") as f:
        lines = f.read().splitlines()
    exc = [l for l in lines if "Exception" in l]
    plug = [l for l in lines if "[plugin " + GUID in l]
    C.R.add("LOG", "WARN" if exc else "PASS", "player.log %d lines; %d with 'Exception'%s; %d lines of the plugin"
            % (len(lines), len(exc), (" (first: %s)" % exc[0][:160]) if exc else "", len(plug)), exceptions=exc[:20])


# ---------------------------------------------------------------- the plugin kit's samples and the runtime disable / enable

def pk1(C):
    if C.a.no_kit:
        C.R.add("PK1", "WARN", "not run (--no-kit)")
        return
    r = C.call({"cmd": "hello.greet", "name": "player"}, check=False)
    n = len(C.call({"cmd": "scene"}).get("objects") or [])
    r2 = C.call({"cmd": "hello.greet"}, check=False)
    ok = (r.get("ok") and r.get("text") == "Hello, player!" and r.get("objects") == n and r2.get("ok")
          and r2.get("text") == "Hello, world!")
    C.R.add("PK1", "PASS" if ok else "FAIL", "hello.greet {name: player} -> %s, objects %s (the scene has %d); without a name -> %s"
            % (json.dumps(r.get("text") if r.get("ok") else r.get("error")), r.get("objects"), n,
               json.dumps(r2.get("text") if r2.get("ok") else r2.get("error"))), replies=[r, r2])


def pk3(C):
    """The kit's Shapes in the player (Phase 6.8, BY6): its five rows listed, its torus built from its row with its eight FK bones."""
    if C.a.no_kit:
        C.R.add("PK3", "WARN", "not run (--no-kit)")
        return
    rows = C.call({"cmd": "item.search", "group": SHAPES_GROUP, "limit": 100}).get("items") or []
    names = [x.get("name") for x in rows]
    add = C.call({"cmd": "item.add", "ref": SHAPES_TORUS}, check=False)
    key = ((add.get("added") or [{}])[0]).get("key") if add.get("ok") else None
    st = C.call({"cmd": "item.state", "key": key}, check=False) if key else {}
    row, real = st.get("row") or {}, st.get("realized") or {}
    fk = real.get("fkBones")
    fk = len(fk) if isinstance(fk, list) else fk
    if key:
        C.call({"cmd": "delete", "key": key}, check=False)
    ok = names == SHAPES_ROWS and key is not None and row.get("fallback") is False and (real.get("renderers") or 0) >= 1 and fk == 8
    C.R.add("PK3", "PASS" if ok else "FAIL", "the kit's Shapes: item.search {group: %d} %s; item.add %s -> row fallback %s, renderers %s, FK bones %s"
            % (SHAPES_GROUP, names, SHAPES_TORUS, row.get("fallback"), real.get("renderers"), fk), state=st)


def forms_count(e):
    ui = e.get("ui")
    if isinstance(ui, dict) and "forms" in ui:
        f = ui.get("forms")
    else:
        f = e.get("ui.forms", e.get("forms"))
    if isinstance(f, list):
        return len(f)
    return f if isinstance(f, int) else None


def pk2(C):
    if C.a.no_kit:
        C.R.add("PK2", "WARN", "not run (--no-kit)")
        return
    def read(form, node=None):
        q = {"cmd": "helloui.read", "form": form}
        if node:
            q["node"] = node
        return C.call(q).get("values") or {}
    v0 = read("settings")
    C.call({"cmd": "helloui.write", "form": "settings", "key": "size", "value": 3.5, "phase": "commit", "undo": False})
    v1 = read("settings")
    C.call({"cmd": "helloui.write", "form": "tag", "node": "pk2-node", "key": "weight", "value": 0.25, "phase": "commit", "undo": False})
    t1 = read("tag", "pk2-node")
    C.call({"cmd": "helloui.reset", "form": "settings", "key": "reset"})
    v2 = read("settings")
    bad = C.call({"cmd": "helloui.write", "form": "settings", "key": "mode", "value": "loud", "phase": "commit", "undo": False}, check=False)
    e = entry_of(C.call({"cmd": "plugin.list"}), "stp3.sample.helloui") or {}
    forms = forms_count(e)
    ok = (sorted(v0) == HELLOUI_KEYS and v0.get("size") == 1 and v1.get("size") == 3.5 and t1.get("weight") == 0.25
          and v2.get("size") == 1 and not bad.get("ok") and bad.get("code") == "bad_args" and forms in (2, None))
    verdict = "PASS" if ok and forms == 2 else ("WARN" if ok else "FAIL")
    C.R.add("PK2", verdict, "helloui.read settings keys %s, size %s -> write 3.5 -> %s -> reset -> %s; tag weight of a node %s; "
            "a bad mode -> %s; plugin.list forms %s"
            % (",".join(sorted(v0)), v0.get("size"), v1.get("size"), v2.get("size"), t1.get("weight"), bad.get("code"),
               forms if forms is not None else "not reported"), entry=e)


def wy3(C):
    if C.a.no_kit:
        C.R.add("WY3", "WARN", "not run (--no-kit)")
        return
    s0 = C.call({"cmd": "toolprobe.state"}, check=False)
    t = C.call({"cmd": "toolprobe.selftest"}, check=False)
    C.call({"cmd": "map.load", "ref": TOOL_MAP})
    s1 = C.call({"cmd": "toolprobe.state"}, check=False)
    q = C.call({"cmd": "toolprobe.surface", "x": 0, "y": 30, "z": 0}, check=False)
    C.call({"cmd": "map.load", "clear": True})
    s2 = C.call({"cmd": "toolprobe.state"}, check=False)
    calls = ["Started", "PointerDown 0 Ctrl hit True", "PointerUp 0", "Ended Finished"]
    ok = (s0.get("ok") and s0.get("unity") is True and s0.get("tools") is False and s0.get("map") is True and s0.get("loaded") is False
          and t.get("ok") and t.get("down") is True and t.get("picks") == 1 and t.get("calls") == calls
          and s1.get("loaded") is True and (s1.get("roots") or 0) >= 1
          and q.get("ok") and q.get("hit") is True and q.get("source") == "gpu" and q.get("answers") == 2
          and s2.get("loaded") is False)
    C.R.add("WY3", "PASS" if ok else "FAIL",
            "the UI off: tools %s, map %s; tool calls %s, picks %s; %s: loaded %s, roots %s, surface %s at %s (%s), answers %s; cleared: loaded %s"
            % (s0.get("tools"), s0.get("map"), t.get("calls"), t.get("picks"), TOOL_MAP, s1.get("loaded"), s1.get("roots"), q.get("hit"),
               q.get("height"), q.get("source"), q.get("answers"), s2.get("loaded")), replies=[s0, t, s1, q, s2])


def motion_row(C, key, stage):
    """The row of a stage in chara.motion, or None."""
    r = C.call({"cmd": "chara.motion", "key": key}, check=False)
    return next((x for x in (r.get("stages") or []) if x.get("name") == stage), None)


def ry8(C):
    """A plugin's stage on a character in a player: the port has run only headless."""
    if C.a.no_kit:
        C.R.add("RY8", "WARN", "not run (--no-kit)")
        return
    path = os.path.join(C.project, "Tools", "targets.json")
    if not os.path.isfile(path):
        C.R.add("RY8", "WARN", "not run: Tools\\targets.json is absent")
        return
    with open(path, encoding="utf-8") as f:
        rel = json.load(f)["visual"]["female"].replace("\\", "/")
    rel = rel[len("chara/female/"):] if rel.lower().startswith("chara/female/") else rel
    C.call({"cmd": "scene.new"})
    s0 = C.call({"cmd": "stageprobe.state"}, check=False)
    C.call({"cmd": "stageprobe.clear"})
    key = C.call({"cmd": "chara.load", "path": rel, "sex": "female"}, timeout=300)["added"][0]["key"]
    name = STAGE_PROBE[0] + "/probe"
    add = C.call({"cmd": "stageprobe.add", "key": key, "phase": 420})
    C.frames(STAGE_FRAMES)
    s1 = C.call({"cmd": "stageprobe.state"})
    row = motion_row(C, key, name)
    r = C.call({"cmd": "chara.change", "key": key, "path": rel}, timeout=300)
    C.frames(STAGE_FRAMES)
    s2 = C.call({"cmd": "stageprobe.state"})
    row2 = motion_row(C, key, name)
    ok = (s0.get("ok") and s0.get("unity") is True and s0.get("stages") is True and add.get("added") is True and s1["steps"] >= STAGE_FRAMES and s1["makes"] == 1
          and row is not None and row.get("phase") == 420 and row.get("owner") == STAGE_PROBE[0])
    C.R.add("RY8", "PASS" if ok else "FAIL", "a stage at 420 on BR-Chan in real frames: unity %s, stages %s; %d steps over %d frames, %d made, chara.motion: %s" % (s0.get("unity"), s0.get("stages"), s1["steps"], STAGE_FRAMES, s1["makes"], json.dumps(row)),
            replies=[s0, add, s1])
    ok = s2["makes"] == 2 and s2["root"] not in (0, s1["root"]) and s2["steps"] > s1["steps"] and row2 is not None
    C.R.add("RY8", "PASS" if ok else "FAIL", "chara.change: %d made, root %s -> %s, %d steps, still listed %s" % (s2["makes"], s1["root"], s2["root"], s2["steps"], row2 is not None), reply=r, state=s2)
    removed = C.call({"cmd": "stageprobe.remove"})
    s3 = C.call({"cmd": "stageprobe.state"})
    C.frames(5)
    s4 = C.call({"cmd": "stageprobe.state"})
    gone = motion_row(C, key, name)
    C.R.add("RY8", "PASS" if removed.get("removed") == 1 and s4["steps"] == s3["steps"] and gone is None else "FAIL", "taken off: %s removed, steps %d then %d, listed %s" % (removed.get("removed"), s3["steps"], s4["steps"], gone is not None))
    C.call({"cmd": "stageprobe.watch", "key": key})
    C.frames(5)
    y = C.call({"cmd": "stageprobe.head"}).get("y", 0)
    C.call({"cmd": "stageprobe.remove"})
    C.R.add("RY8", "PASS" if y > 1 else "FAIL", "the guide's example at the final phase saw the head at y %.3f (at least 1)" % y)
    C.call({"cmd": "scene.new"})


def kd9(C):
    load_fixture(C)
    before = raw_member(C.text({"cmd": "seq.get"}), "sequence")
    c0 = owned_counts(C.call({"cmd": "plugin.list"}))
    t = time.perf_counter()
    d = C.call({"cmd": "plugin.disable", "guid": GUID}, check=False)
    d_ms = (time.perf_counter() - t) * 1000
    if d.get("ok"):
        C.extra_unloads = 1
    c1 = owned_counts(C.call({"cmd": "plugin.list"}))
    gone = C.call({"cmd": "seq.get"}, check=False)
    t = time.perf_counter()
    en = C.call({"cmd": "plugin.enable", "guid": GUID}, check=False)
    e_ms = (time.perf_counter() - t) * 1000
    c2 = owned_counts(C.call({"cmd": "plugin.list"}))
    after = raw_member(C.text({"cmd": "seq.get"}), "sequence") if en.get("ok") else None
    ok = (d.get("ok") and d.get("state") == "disabled" and c1.get("state") == "disabled" and c1.get("commands") == 0
          and not gone.get("ok") and en.get("ok") and en.get("state") == "loaded" and c2 == c0 and before is not None
          and after == before)
    C.R.add("KD9", "PASS" if ok else "FAIL",
            "plugin.disable -> %s (%s; reply ms %s, round trip %.1f ms); then %d commands, seq.get %s; plugin.enable -> %s "
            "(%s; reply ms %s, round trip %.1f ms, extBlocks %s, warnings %d); counts before / after %s / %s; seq.get %s"
            % (d.get("state") if d.get("ok") else d.get("code"), c1.get("state"), d.get("ms"), d_ms, c1.get("commands") or 0,
               "refused (%s)" % gone.get("code") if not gone.get("ok") else "still answers",
               en.get("state") if en.get("ok") else "%s %s" % (en.get("code"), json.dumps(en.get("detail"))[:120]),
               c2.get("state"), en.get("ms"), e_ms, en.get("extBlocks"), len(en.get("warnings") or []),
               json.dumps(c0, sort_keys=True), json.dumps(c2, sort_keys=True),
               "byte-equal (%d bytes)" % len(before) if before is not None and after == before else "differs"),
            disable=d, enable=en, before=c0, during=c1, after=c2)


# ---------------------------------------------------------------- the fixture

def make_fixture(C):
    C.call({"cmd": "scene.new"})
    a, b = add_item(C), add_item(C)
    C.call({"cmd": "obj.transform", "key": a, "pos": [0, 0, 0], "rot": [0, 0, 0]})
    C.call({"cmd": "obj.transform", "key": b, "pos": [-1, 0, 0], "rot": [0, 0, 0]})
    seq = fixture_sequence(a, b)
    C.call({"cmd": "seq.set", "sequence": seq})
    C.frames(2)
    r = C.call({"cmd": "scene.save", "name": FIXTURE_NAME, "overwrite": True, "thumb": "placeholder",
                "title": "sequence fixture", "clock": "2026-10-29T00:00:00Z"}, timeout=300)
    get1 = C.text({"cmd": "seq.get"})
    C.call({"cmd": "scene.new"})
    C.call({"cmd": "scene.load", "name": FIXTURE_NAME}, timeout=300)
    get2 = C.text({"cmd": "seq.get"})
    st = status(C)
    with zipfile.ZipFile(r["path"]) as z:
        man = json.loads(z.read("manifest.json").decode("utf-8"))
    dest = os.path.join(C.project, FIXTURE_REL)
    os.makedirs(os.path.dirname(dest), exist_ok=True)
    shutil.copyfile(r["path"], dest)
    with open(dest, "rb") as f:
        sha = hashlib.sha256(f.read()).hexdigest()
    ok = get1 == get2 and len(st["tracks"]) == 3 and man.get("formatVersion") == 1
    C.R.add("FIXTURE", "PASS" if ok else "FAIL", "%s: %d bytes, sha256 %s; items %s, %s; reload seq.get %s; ext %s"
            % (dest, r.get("bytes") or 0, sha[:16], a[:12], b[:12], "equal" if get1 == get2 else "DIFFERS",
               json.dumps([e for e in man.get("ext") or [] if e.get("ns") == GUID])), sha256=sha)


# ---------------------------------------------------------------- report and main

def write_reports(C, exit_code, started):
    R = C.R
    n_pass, n_fail, n_warn = R.count("PASS"), R.count("FAIL"), R.count("WARN")
    summary = "SUMMARY: %d PASS, %d FAIL" % (n_pass, n_fail)
    warns = [x["id"] for x in R.items if x["verdict"] == "WARN"]
    lines = [summary, "WARN: %d%s" % (n_warn, (" (" + ", ".join(warns) + ")") if warns else ""),
             "plugin_player_check %s; exe %s; port %d; pid %s; exit code %d; %.0f s"
             % (started, C.exe or "(attach)", C.port, C.pid, exit_code, time.perf_counter() - T0)]
    for x in R.items:
        lines.append("%-7s %-4s  %s" % (x["id"], x["verdict"], x["text"]))
    lines.append(summary)
    with open(os.path.join(C.out, "plugin_player_check_report.txt"), "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")
    doc = {"started": started, "exe": C.exe, "project": C.project, "out": C.out, "port": C.port, "pid": C.pid,
           "exitCode": exit_code, "seconds": round(time.perf_counter() - T0, 1), "summary": summary,
           "counts": {"pass": n_pass, "fail": n_fail, "warn": n_warn}, "info": C.info,
           "python": sys.version.split()[0], "checks": R.items}
    with open(os.path.join(C.out, "plugin_player_check.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(doc, f, ensure_ascii=False, indent=1, default=str)


def main(argv):
    for s in (sys.stdout, sys.stderr):
        try:
            s.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, ValueError):
            pass
    ap = argparse.ArgumentParser(description="The sample plugins' acceptance (PA1-PA9, PK1, PK2, KD9) in the HS2Next app player.")
    ap.add_argument("--exe", help="path to HS2Next.exe (the built app player)")
    ap.add_argument("--project", help="project root (Config\\settings.local.json, Cache, Samples, Tests\\Plugins, Tools\\plugins.ps1)")
    ap.add_argument("--out", help="output folder (report, json, player.log, userdata, bridge_out); required but with --selftest")
    ap.add_argument("--selftest", action="store_true", help="PA7's managed memory rule on its worked series; no player, exit 1 on a failure")
    ap.add_argument("--port", type=int, default=DEFAULT_PORT)
    ap.add_argument("--timeout-min", type=float, default=20.0, help="whole run; past it the started PID is killed")
    ap.add_argument("--ready-timeout-min", type=float, default=12.0)
    ap.add_argument("--only", default="", help="comma list of PA1..PA8, PK1, PK2, PK3, KD9, WY3, RY8 (PA9 always runs when the player was started)")
    ap.add_argument("--cycles", type=int, default=20, help="PA7 measured cycles")
    ap.add_argument("--warmup", type=int, default=2, help="PA7 warm-up cycles")
    ap.add_argument("--measure-frames", type=int, default=600)
    ap.add_argument("--keep-staged", action="store_true", help="leave the staged plugin files in <exe dir>\\Plugins")
    ap.add_argument("--no-build", action="store_true", help="stage the existing build of the sample")
    ap.add_argument("--api-dir", help="build the Sequencer against the HS2Next.Api.dll in this folder (plugins.ps1 -ApiDir), "
                    "e.g. Tests\\Plugins\\fixtures\\api10")
    ap.add_argument("--kit-api-dir", help="the Api folder of the kit samples (default: <exe dir>\\HS2Next_Data\\Managed)")
    ap.add_argument("--no-kit", action="store_true", help="the Sequencer alone, as at 5.1 (no Hello / HelloUi, PK1 / PK2 not run)")
    ap.add_argument("--min-free-gb", type=float, default=8.0)
    ap.add_argument("--ram-wait-min", type=float, default=10.0)
    ap.add_argument("--attach", action="store_true", help="use a player already serving on --port (no build, stage, start or quit)")
    ap.add_argument("--make-fixture", action="store_true", help="write Tests\\Plugins\\fixtures\\sequence_scene.hs2nscene instead of checking")
    a = ap.parse_args(argv)
    if a.selftest:
        return 1 if selftest() else 0
    if not a.out:
        ap.error("--out is required (unless --selftest)")
    if not a.attach and (not a.exe or not a.project):
        ap.error("--exe and --project are required (unless --attach)")
    if a.make_fixture and not a.project:
        ap.error("--make-fixture needs --project")
    C = Ctx(a)
    started = utc_now()
    C.deadline = time.time() + a.timeout_min * 60

    def on_timeout():
        C.aborted = "timed out after %.1f min (--timeout-min); the started player PID %s was killed" % (a.timeout_min, C.pid)
        log(C.aborted)
        kill_player(C)

    watchdog = threading.Timer(a.timeout_min * 60, on_timeout)
    watchdog.daemon = True
    watchdog.start()
    exit_code = 0
    os.makedirs(C.out, exist_ok=True)
    try:
        try:
            setup(C)
        except SetupError as e:
            C.R.add("SETUP", "FAIL", str(e))
            exit_code = 2
        except Exception as e:
            C.R.add("SETUP", "FAIL", "%s: %s" % (type(e).__name__, e), traceback=traceback.format_exc(limit=6))
            exit_code = 2
        if exit_code == 0:
            try:
                if a.make_fixture:
                    guard(C, "FIXTURE", make_fixture)
                else:
                    for cid, fn in (("PA1", pa1), ("PA2", pa2), ("PK1", pk1), ("PK2", pk2), ("PK3", pk3), ("PA3", pa3), ("PA4", pa4),
                                    ("PA5", pa5), ("PA6", pa6), ("PA7", pa7), ("PA8", pa8), ("KD9", kd9), ("WY3", wy3), ("RY8", ry8)):
                        if C.want(cid):
                            guard(C, cid, fn)
            except Abort as e:
                C.R.add("ABORT", "FAIL", "the run stopped: %s" % e)
    finally:
        watchdog.cancel()
        if C.aborted is None and exit_code == 0:
            try:
                quit_player(C)
            except Exception as e:
                C.R.add("PA9", "FAIL", "%s: %s" % (type(e).__name__, e))
        kill_player(C)
        try:
            check_log(C)
        except Exception as e:
            C.R.add("LOG", "WARN", "player.log not read: %s" % e)
        try:
            unstage(C)
        except Exception as e:
            C.R.add("CLEAN", "WARN", "%s: %s" % (type(e).__name__, e))
        if exit_code == 0 and C.R.count("FAIL"):
            exit_code = 1
        write_reports(C, exit_code, started)
        log("SUMMARY: %d PASS, %d FAIL, %d WARN -> exit %d; report %s"
            % (C.R.count("PASS"), C.R.count("FAIL"), C.R.count("WARN"), exit_code, os.path.join(C.out, "plugin_player_check_report.txt")))
    return exit_code


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
