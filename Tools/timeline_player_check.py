"""
timeline_player_check.py - the Timeline and the frame recording in the built app player (Phase 6.2, package W; the row
TimelineCheck.player).

Starts the app player itself (no plugins: HS2NEXT_NO_PLUGINS=1, HS2NEXT_PRIVATE_OFF=1), builds the Timeline compositions
of Tests\\Timeline\\compositions through the bridge (scene.new, the visual targets of Tools\\targets.json, FK on with the
Body and Neck groups for bones9, timeline.build with the placeholders replaced by the node keys) and checks over real HTTP
from outside the process. It never uses the Unity Editor. Python 3.14, standard library only. Content: the clothed SFW
visual targets, the targets' non-H animation rows; the recorded PNG files are judged by their size and their header bytes
read as numbers (signature, IHDR width / height, bit depth, colour type) - never decoded, opened or shown; saves use the
placeholder thumbnail.

Usage (the run_tests.ps1 row TimelineCheck.player calls the first form):
    python -I Tools\\timeline_player_check.py --exe <path to HS2Next.exe> --project <project root> --out <dir>
                                              [--port 17387] [--timeout-min 20]
    more options: --only TP1,TW6,... (default all), --measure-frames 600, --min-free-gb 8, --ram-wait-min 10,
                  --ready-timeout-min 12, --keep-frames (keep the recorded PNG files; default: removed after their checks,
                  record.json kept), --attach (use a player already serving on --port: no start, no quit)
    -I ignores PYTHONUTF8, so the script sets its own stdout/stderr to UTF-8.

Run:
  1. Setup: the exe, <project>\\Config\\settings.local.json, the compositions and Tools\\targets.json exist, the port is free,
     no process of the game gate runs (Tools\\session_gate.json, a local file; without it there is no game gate and a WARN
     line says so); waits up to --ram-wait-min for --min-free-gb of RAM.
  2. Starts the player: <exe> -batchmode -bridgePort <port> -bridgeAllowQuit -settings <project>\\Config\\settings.local.json
     -cacheDir <project>\\Cache -userData <out>\\userdata -bridgeOutputDir <out>\\bridge_out -logFile <out>\\player.log (the UI
     is off). Ready = ping.appReady and <out>\\userdata\\Bridge\\endpoint.json naming the started PID.
  3. TP1, TP2, TW6, TW7, TW8, TA1, TA6, then app.quit (QUIT). Past --timeout-min the script kills only the PID it started;
     while it runs it polls the game gate every 30 s and stops the player by PID when a game process appears (exit 3).

Checks (one report line each):
  SETUP  ready time, versions
  TP1    bones9 (9 FK bone tracks + the camera on BR-Chan and HERO107) playing, UI off: timeline.status {perf} over
         --measure-frames frames after a reset: median <= 479 us (p95 and max reported)
  TP2    app.perf frameMs median / p95 with the Timeline playing (the TP1 window) and stopped (the same number of frames):
         information (PASS with the numbers)
  TW6    record.start {1280 x 720, 30 fps, 300 frames, timeline}: 300 files frame_000001.png ... and record.json only, every
         header 1280 x 720 8-bit RGB; record.json frames 300, sceneTimes spaced by dt (1e-9), timelineTimes[k] = (k + 1) dt
         (1e-9); ms per frame reported
  TW7    walk (12 s, looping) record.start {timeline, 30 fps, 640 x 360}: stops by itself, stoppedBy "pass", 360 frames
  TW8    record.stop while 4 frames are pending (3840 x 2160 frames, polled until record.status pending reaches 4): the reply
         comes after the files - written = frames, record.status pending 0 right after it, every file there
  TA1    walk: build -> play 120 frames -> timeline.pause -> 5 seeks (the Timeline's slot values from overrides.list and
         camera.get) -> scene.save -> scene.new -> scene.load: timeline.get's timeline byte-equal and the 5 seeks give the
         same values (1e-6)
  TA6    walk, everything under the global pause: save A while it plays, timeline.stop, save B: equal entry for entry (the
         thumbnail aside); then save P0, play (held by the pause), save A1, seek 6 (paused by itself), save A2: A1's and
         A2's scene.json equal P0's (no event ran, no edit)
  QUIT   app.quit answers quitting:true and the player exits with 0
  LOG    exceptions in player.log (WARN only)

Outputs:
  <out>\\timeline_player_report.txt  first and last line "SUMMARY: N PASS, M FAIL", second line "WARN: K (ids)", then one line
                                     per check "<id> PASS|FAIL|WARN <numbers>"
  <out>\\timeline_player_check.json  details of every check
  <out>\\player.log, <out>\\userdata\\ (the saves), <out>\\bridge_out\\record\\<check>\\ (record.json; the frames with --keep-frames)
Exit code: 0 every check passed (WARN allowed), 1 a check failed, 2 setup error, 3 aborted by the game gate.
Ports: 17387 by default (17398 = the Editor play rows, 17394 = PluginCheck.player, 17322 = interactive default).
Always 127.0.0.1, never localhost.
"""
import argparse
import ctypes
import http.client
import json
import math
import os
import shutil
import socket
import statistics
import struct
import subprocess
import sys
import threading
import time
import traceback
import zipfile
from datetime import datetime, timezone

DEFAULT_PORT = 17387
PERF_BAR_US = 479.0
COMPOSITIONS = os.path.join("Tests", "Timeline", "compositions")
GATE_FILE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "session_gate.json")
CREATE_NO_WINDOW = 0x08000000
PNG_SIG = b"\x89PNG\r\n\x1a\n"
CLOCK = "2026-11-03T00:00:00Z"
T0 = time.perf_counter()


def log(msg):
    print("[%7.1f s] %s" % (time.perf_counter() - T0, msg), flush=True)


def utc_now():
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def f32(x):
    """x rounded to single precision (the time step the engine uses)."""
    return struct.unpack("<f", struct.pack("<f", x))[0]


class SetupError(Exception):
    pass


class Abort(Exception):
    """The player is gone (exited, killed by the watchdog or the game gate): the remaining checks cannot run."""


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


# ---------------------------------------------------------------- machine helpers

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
    gates = game_processes()[0]
    if not gates:
        return []
    p = subprocess.run(["tasklist", "/FO", "CSV", "/NH"], capture_output=True, timeout=60, creationflags=CREATE_NO_WINDOW)
    names = set()
    for line in p.stdout.decode("oem", "replace").splitlines():
        name = line.split(",")[0].strip('"').lower()
        for g in gates:
            if name.startswith(g):
                names.add(name)
    return sorted(names)


def png_header(path, w, h):
    """None when the file starts with the PNG signature and an IHDR of w x h, bit depth 8, colour type 2 (RGB); else what
    differs. Reads the first 26 bytes as numbers only (the image is never decoded)."""
    with open(path, "rb") as f:
        b = f.read(26)
    if len(b) < 26:
        return "shorter than a PNG header"
    if b[:8] != PNG_SIG:
        return "no PNG signature"
    if b[12:16] != b"IHDR":
        return "no IHDR first"
    fw, fh = struct.unpack(">II", b[16:24])
    if (fw, fh) != (w, h):
        return "%d x %d" % (fw, fh)
    if b[24] != 8 or b[25] != 2:
        return "bit depth %d, colour type %d" % (b[24], b[25])
    return None


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
        self.br = Bridge(a.port)
        self.R = Results()
        self.proc = None
        self.pid = None
        self.aborted = None
        self.gated = False
        self.info = {}
        self.only = set(x.strip().upper() for x in a.only.split(",")) if a.only else None
        self.deadline = None
        self.output_root = None
        self.targets = None

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
        return self.call({"cmd": "ping", "waitFrames": int(n)}, timeout=300)


def guard(C, cid, fn):
    try:
        fn(C)
    except Abort:
        raise
    except Exception as e:
        C.R.add(cid, "FAIL", "exception: %s: %s" % (type(e).__name__, str(e)[:300]), traceback=traceback.format_exc(limit=6))
        C.alive()


# ---------------------------------------------------------------- setup, start, quit

def preflight(C):
    a = C.a
    if not C.exe or not os.path.isfile(C.exe):
        raise SetupError("exe not found: %s" % C.exe)
    if not C.project or not os.path.isdir(C.project):
        raise SetupError("project root not found: %s" % C.project)
    for name in ("bones9", "walk", "camera_dof"):
        if not os.path.isfile(os.path.join(C.project, COMPOSITIONS, name + ".json")):
            raise SetupError("composition not found: %s" % os.path.join(COMPOSITIONS, name + ".json"))
    gate_warning = game_processes()[1]
    if gate_warning:
        C.R.add("GATE", "WARN", gate_warning)
    games = running_games()
    if games:
        C.gated = True
        raise SetupError("the game gate: %s runs; no player row now" % ", ".join(games))
    busy = port_listeners(C.port)
    if busy:
        raise SetupError("port %d is already in use (PID %s)" % (C.port, ", ".join(map(str, sorted(busy)))))
    need, end = a.min_free_gb, time.time() + a.ram_wait_min * 60
    g = free_gb()
    while g is not None and g < need and time.time() < end:
        log("free RAM %.1f GB < %.1f GB: waiting" % (g, need))
        time.sleep(15)
        g = free_gb()
    C.info["freeGb"] = round(g, 1) if g is not None else None
    if g is not None and g < need:
        C.R.add("RAM", "WARN", "free RAM %.1f GB < %.1f GB; started anyway" % (g, need))


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
    env["HS2NEXT_NO_PLUGINS"] = "1"
    env["HS2NEXT_PRIVATE_OFF"] = "1"
    env.pop("HS2NEXT_DATA_ROOT", None)
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
        ready_s = start_player(C)
    with open(os.path.join(C.project, "Tools", "targets.json"), encoding="utf-8") as f:
        C.targets = json.load(f)
    p = C.call({"cmd": "ping"})
    v = C.call({"cmd": "bridge.version"})
    paths = C.call({"cmd": "app.paths"})
    C.output_root = paths.get("outputDir")
    C.info["ping"] = {k: p.get(k) for k in ("protocol", "app", "bridge", "api", "frame", "screen")}
    C.info["version"] = {k: v.get(k) for k in v if k not in ("ok", "cmd")}
    C.R.add("SETUP", "PASS", "ready in %.1f s (pid %s, port %d); api %s; output root %s"
            % (ready_s, C.pid, C.port, p.get("api"), C.output_root), readySeconds=round(ready_s, 2))


def quit_player(C):
    if C.a.attach or C.proc is None:
        return
    if C.proc.poll() is not None:
        C.R.add("QUIT", "FAIL", "the player had already exited with code %s" % C.proc.returncode)
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
    ok = reply is not None and reply.get("quitting") is True and not killed and code == 0
    C.R.add("QUIT", "PASS" if ok else "FAIL", "app.quit %s; exit code %s in %.1f s%s"
            % ("answered quitting:true" if reply and reply.get("quitting") else "failed (%s)" % err, code, time.time() - t,
               "; killed after 60 s" if killed else ""), exitCode=code, killed=killed)


def kill_player(C):
    if C.proc is not None and C.proc.poll() is None:
        try:
            C.proc.kill()
            C.proc.wait(30)
        except Exception:
            pass


def check_log(C):
    path = os.path.join(C.out, "player.log")
    if C.a.attach or not os.path.isfile(path):
        return
    with open(path, encoding="utf-8", errors="replace") as f:
        lines = f.read().splitlines()
    exc = [l for l in lines if "Exception" in l]
    rec = [l for l in lines if "[record]" in l]
    C.R.add("LOG", "WARN" if exc else "PASS", "player.log %d lines; %d with 'Exception'%s; %d recorder lines%s"
            % (len(lines), len(exc), (" (first: %s)" % exc[0][:160]) if exc else "", len(rec), (" (last: %s)" % rec[-1][:160]) if rec else ""),
            exceptions=exc[:20], recorder=rec[-20:])


# ---------------------------------------------------------------- the compositions

def build(C, name):
    """scene.new, the nodes the composition names ($a / $b the visual targets, $p item 399, $l a light), FK on with the Body
    and Neck groups for bones9, then timeline.build {play: false}. Returns the placeholder -> key map."""
    with open(os.path.join(C.project, COMPOSITIONS, name + ".json"), encoding="utf-8") as f:
        text = f.read()
    C.call({"cmd": "scene.new"}, timeout=300)
    keys = {}
    for ph, sex in (("$a", "female"), ("$b", "male")):
        if '"%s"' % ph not in text:
            continue
        card = os.path.basename(C.targets["visual"][sex])
        r = C.call({"cmd": "chara.load", "path": card, "sex": sex}, timeout=300)
        keys[ph] = r["added"][0]["key"]
        if name == "bones9":
            C.call({"cmd": "fk.enable", "key": keys[ph], "on": True})
            C.call({"cmd": "fk.enable", "key": keys[ph], "groups": ["Body", "Neck"]})
    if '"$p"' in text:
        keys["$p"] = C.call({"cmd": "item.add", "no": 399})["added"][0]["key"]
    if '"$l"' in text:
        r = C.call({"cmd": "light.add"})
        keys["$l"] = r.get("key") or r["added"][0]["key"]
    for ph, key in keys.items():
        text = text.replace('"%s"' % ph, json.dumps(key))
    req = json.loads(text)
    req["cmd"] = "timeline.build"
    req["play"] = False
    C.call(req, timeout=120)
    C.frames(3)
    return keys


def record_folder(C, name):
    """<output root>/record/<name>, emptied of an earlier run's frame_*.png and record.json (not recursive)."""
    folder = os.path.join(C.output_root, "record", name)
    if os.path.isdir(folder):
        for fn in os.listdir(folder):
            if (fn.startswith("frame_") and fn.endswith(".png")) or fn == "record.json":
                os.remove(os.path.join(folder, fn))
    return folder


def wait_recording(C, timeout_s):
    """record.status polled until the recording finished (not recording, nothing pending, stoppedBy set); None on timeout."""
    end = time.time() + timeout_s
    while time.time() < end:
        s = C.call({"cmd": "record.status"})
        if not s.get("recording") and s.get("pending") == 0 and isinstance(s.get("stoppedBy"), str):
            return s
        time.sleep(0.1)
    return None


def judge_files(folder, n, w, h):
    """(files ok, text): the folder holds frame_000001.png ... frame_<n>.png and record.json only, headers w x h RGB8."""
    names = sorted(os.listdir(folder)) if os.path.isdir(folder) else []
    want = sorted(["frame_%06d.png" % i for i in range(1, n + 1)] + ["record.json"])
    bad, first, total = 0, None, 0
    for fn in names:
        if fn.endswith(".png"):
            p = os.path.join(folder, fn)
            total += os.path.getsize(p)
            why = png_header(p, w, h)
            if why:
                bad += 1
                first = first or "%s: %s" % (fn, why)
    ok = names == want and bad == 0
    return ok, "%d files (%s), %d bad headers%s, %.1f MB" % (len(names), "as expected" if names == want else "differ", bad,
                                                             "; " + first if first else "", total / 1e6)


def judge_json(folder, n, fps, timeline):
    """(ok, text, doc): record.json frames n, written n, sceneTimes spaced by dt, timelineTimes[k] = (k + 1) dt."""
    with open(os.path.join(folder, "record.json"), encoding="utf-8") as f:
        doc = json.load(f)
    dt = f32(1.0 / fps)
    st = doc.get("sceneTimes") or []
    worst = max((abs(st[k] - st[k - 1] - dt) for k in range(1, len(st))), default=0.0)
    ok = doc.get("frames") == n and doc.get("written") == n and doc.get("dt") == dt and len(st) == n and worst <= 1e-9
    text = "frames %s, written %s, dt %r, sceneTimes spacing worst %.3g" % (doc.get("frames"), doc.get("written"), doc.get("dt"), worst)
    if timeline:
        tt = doc.get("timelineTimes") or []
        wt = max((abs(tt[k] - (k + 1) * dt) if tt[k] is not None else math.inf for k in range(len(tt))), default=math.inf)
        ok = ok and len(tt) == n and wt <= 1e-9
        text += ", timelineTimes worst %.3g" % wt
    return ok, text, doc


def drop_frames(C, folder):
    if C.a.keep_frames or not os.path.isdir(folder):
        return
    for fn in os.listdir(folder):
        if fn.startswith("frame_") and fn.endswith(".png"):
            os.remove(os.path.join(folder, fn))


# ---------------------------------------------------------------- TP1, TP2: the Timeline's time per frame

def tp(C):
    n = C.a.measure_frames
    build(C, "bones9")
    C.call({"cmd": "timeline.play", "from": 0})
    C.frames(60)
    C.call({"cmd": "timeline.status", "perf": True, "reset": True})
    C.call({"cmd": "app.perf", "reset": True})
    C.frames(n)
    st = C.call({"cmd": "timeline.status", "perf": True})
    playing = C.call({"cmd": "app.perf"})
    perf = st.get("perf") or {}
    med = perf.get("medianUs")
    ok = isinstance(med, (int, float)) and med <= PERF_BAR_US and (perf.get("frames") or 0) >= n and st.get("state") == "playing"
    if C.want("TP1"):
        C.R.add("TP1", "PASS" if ok else "FAIL", "bones9 playing, UI off, %s frames: median %s us (bar %.0f), p95 %s us, max %s us; tracks %s, interpolables %s"
                % (perf.get("frames"), med, PERF_BAR_US, perf.get("p95Us"), perf.get("maxUs"), st.get("tracks"), st.get("interpolables")),
                perf=perf, status=st)
    C.call({"cmd": "timeline.stop"})
    C.frames(30)
    C.call({"cmd": "app.perf", "reset": True})
    C.frames(n)
    stopped = C.call({"cmd": "app.perf"})
    if C.want("TP2"):
        fp, fs = playing.get("frameMs") or {}, stopped.get("frameMs") or {}
        C.R.add("TP2", "PASS", "app.perf frameMs playing: median %s, p95 %s (%s frames); stopped: median %s, p95 %s (%s frames) (information)"
                % (fp.get("median"), fp.get("p95"), playing.get("frames"), fs.get("median"), fs.get("p95"), stopped.get("frames")),
                playing=playing.get("frameMs"), stopped=stopped.get("frameMs"))


# ---------------------------------------------------------------- TW6-TW8: the recording

def tw6(C):
    build(C, "bones9")
    folder = record_folder(C, "tw6")
    t = time.time()
    st = C.call({"cmd": "record.start", "path": "record/tw6", "width": 1280, "height": 720, "fps": 30, "frames": 300,
                 "timeline": True, "overwrite": True})
    done = wait_recording(C, 600)
    wall = time.time() - t
    if done is None:
        C.R.add("TW6", "FAIL", "the recording did not finish within 600 s")
        return
    files_ok, ftext = judge_files(folder, 300, 1280, 720)
    json_ok, jtext, doc = judge_json(folder, 300, 30, True)
    ok = files_ok and json_ok and done.get("stoppedBy") == "frames" and os.path.normcase(st["path"]) == os.path.normcase(folder)
    C.R.add("TW6", "PASS" if ok else "FAIL", "1280 x 720, 30 fps, 300 frames: %s; %s; stoppedBy %s; %.1f ms per frame (wall %.1f s); maxPending %s, waits %s, syncFrames %s"
            % (ftext, jtext, done.get("stoppedBy"), wall * 1000 / 300, wall, done.get("maxPending"), done.get("waits"), done.get("syncFrames")),
            status=done)
    drop_frames(C, folder)


def tw7(C):
    build(C, "walk")
    folder = record_folder(C, "tw7")
    C.call({"cmd": "record.start", "path": "record/tw7", "width": 640, "height": 360, "fps": 30, "timeline": True, "overwrite": True})
    done = wait_recording(C, 600)
    if done is None:
        C.R.add("TW7", "FAIL", "the recording did not stop by itself within 600 s")
        return
    files_ok, ftext = judge_files(folder, 360, 640, 360)
    ok = files_ok and done.get("stoppedBy") == "pass" and done.get("frames") == 360 and done.get("written") == 360
    C.R.add("TW7", "PASS" if ok else "FAIL", "walk (12 s, looping) at 30 fps, 640 x 360: stoppedBy %s, frames %s, written %s; %s"
            % (done.get("stoppedBy"), done.get("frames"), done.get("written"), ftext), status=done)
    drop_frames(C, folder)


def tw8(C):
    build(C, "walk")
    folder = record_folder(C, "tw8")
    C.call({"cmd": "timeline.play", "from": 0})
    C.call({"cmd": "record.start", "path": "record/tw8", "width": 3840, "height": 2160, "fps": 30, "overwrite": True})
    seen, end = 0, time.time() + 60
    while time.time() < end:
        s = C.call({"cmd": "record.status"})
        seen = max(seen, s.get("pending") or 0)
        if seen >= 4 or not s.get("recording"):
            break
        time.sleep(0.005)
    t = time.time()
    stop = C.call({"cmd": "record.stop"}, timeout=120)
    ms = (time.time() - t) * 1000
    after = C.call({"cmd": "record.status"})
    n = stop.get("frames") or 0
    missing = [i for i in range(1, n + 1) if not os.path.isfile(os.path.join(folder, "frame_%06d.png" % i))]
    ok = stop.get("written") == n and n > 0 and after.get("pending") == 0 and not missing and os.path.isfile(os.path.join(folder, "record.json"))
    verdict = "PASS" if ok and seen >= 4 else ("WARN" if ok else "FAIL")
    C.R.add("TW8", verdict, "record.stop with %d pending (4 wanted%s): the reply after %.0f ms: frames %s, written %s, pending after %s, missing files %d; maxPending %s, waits %s, syncFrames %s"
            % (seen, "" if seen >= 4 else ": not reached, WARN", ms, n, stop.get("written"), after.get("pending"), len(missing),
               stop.get("maxPending"), stop.get("waits"), stop.get("syncFrames")), stop=stop)
    C.call({"cmd": "timeline.stop"})
    drop_frames(C, folder)


# ---------------------------------------------------------------- TA1, TA6 of walk

def sample(C, key):
    """The values the Timeline shows (paused): every slot's value from overrides.list (the character's position, rotation,
    animation row and speed, the camera) and camera.get's view."""
    ov = C.call({"cmd": "overrides.list"}).get("overrides") or []
    cam = C.call({"cmd": "camera.get"})
    cam = cam.get("camera", cam)
    return {"slots": {"%s|%s" % (o.get("key"), o.get("path")): o.get("value") for o in ov if o.get("owner") == "timeline"},
            "camera": {k: cam.get(k) for k in ("target", "rot", "dist", "fov") if k in cam}}


def max_diff(a, b):
    """The largest absolute difference between two nested values of numbers (inf when their shapes differ)."""
    if isinstance(a, (int, float)) and isinstance(b, (int, float)) and not isinstance(a, bool):
        return abs(a - b)
    if isinstance(a, dict) and isinstance(b, dict):
        if a.keys() != b.keys():
            return math.inf
        return max((max_diff(a[k], b[k]) for k in a), default=0.0)
    if isinstance(a, (list, tuple)) and isinstance(b, (list, tuple)):
        if len(a) != len(b):
            return math.inf
        return max((max_diff(x, y) for x, y in zip(a, b)), default=0.0)
    return 0.0 if a == b else math.inf


SEEKS = [0.0, 1.25, 4.5, 7.75, 11.9]


def ta1(C):
    keys = build(C, "walk")
    key = keys["$b"]
    C.call({"cmd": "timeline.play", "from": 0})
    C.frames(120)
    C.call({"cmd": "timeline.pause"})   # a playing head moves on in the frame after a seek
    before = []
    for t in SEEKS:
        C.call({"cmd": "timeline.seek", "t": t})
        C.frames(3)
        before.append(sample(C, key))
    get1 = json.loads(C.text({"cmd": "timeline.get"})).get("timeline")
    C.call({"cmd": "scene.save", "name": "ta1_walk", "overwrite": True, "thumb": "placeholder", "clock": CLOCK}, timeout=300)
    C.call({"cmd": "scene.new"}, timeout=300)
    C.call({"cmd": "scene.load", "name": "ta1_walk"}, timeout=300)
    C.frames(10)
    get2 = json.loads(C.text({"cmd": "timeline.get"})).get("timeline")
    after = []
    for t in SEEKS:
        C.call({"cmd": "timeline.seek", "t": t})
        C.frames(3)
        after.append(sample(C, key))
    worst = max(max_diff(x, y) for x, y in zip(before, after))
    same_doc = json.dumps(get1, sort_keys=False, separators=(",", ":")) == json.dumps(get2, sort_keys=False, separators=(",", ":"))
    ok = same_doc and worst <= 1e-6
    C.R.add("TA1", "PASS" if ok else "FAIL", "walk: play 120 frames, %d seeks, save, new, load: the timeline member %s; the seeks' values worst %.3g (1e-6)"
            % (len(SEEKS), "equal" if same_doc else "DIFFERS", worst), worst=worst)
    C.call({"cmd": "timeline.stop"})


def zip_entries(path):
    with zipfile.ZipFile(path) as z:
        return {n: z.read(n) for n in z.namelist()}


def ta6(C):
    def save(name):
        return C.call({"cmd": "scene.save", "name": name, "overwrite": True, "thumb": "placeholder", "clock": CLOCK}, timeout=300)["path"]

    build(C, "walk")
    C.call({"cmd": "timeline.play", "from": 0})
    C.frames(120)
    C.call({"cmd": "scene.pause", "paused": True})
    C.frames(2)
    a = save("ta6_a")                                   # the Timeline plays, held by the global pause
    C.call({"cmd": "timeline.stop"})
    C.frames(2)
    b = save("ta6_b")                                   # stopped: the model shows
    # the save before playback, still under the global pause (the characters' own motion, e.g. the eyes' look, would go on
    # while the scene runs), then the Timeline plays held by the pause, then it is paused by itself at another time
    p0 = save("ta6_p0")
    C.call({"cmd": "timeline.play", "from": 0})
    C.frames(30)
    a1 = save("ta6_a1")
    C.call({"cmd": "timeline.seek", "t": 6})
    C.frames(3)
    a2 = save("ta6_a2")
    C.call({"cmd": "timeline.stop"})
    C.call({"cmd": "scene.pause", "paused": False})
    ea, eb = zip_entries(a), zip_entries(b)
    thumb = [n for n in set(ea) | set(eb) if "thumb" in n.lower()]
    differ = sorted(n for n in set(ea) | set(eb) if n not in thumb and ea.get(n) != eb.get(n))
    s0 = zip_entries(p0).get("scene.json")
    same1, same2 = zip_entries(a1).get("scene.json") == s0, zip_entries(a2).get("scene.json") == s0
    ok = not differ and same1 and same2 and s0 is not None
    C.R.add("TA6", "PASS" if ok else "FAIL", "walk under the global pause: save A (playing) and B (after timeline.stop) %d entries %s; "
            "scene.json saved while playing %s and while paused at 6 %s the one saved before playback"
            % (len(ea), "equal (thumbnail aside)" if not differ else "DIFFER: " + ", ".join(differ[:6]),
               "equals" if same1 else "DIFFERS from", "equals" if same2 else "DIFFERS from"), differ=differ, thumbnail=thumb)


# ---------------------------------------------------------------- report and main

def write_reports(C, exit_code, started):
    R = C.R
    n_pass, n_fail, n_warn = R.count("PASS"), R.count("FAIL"), R.count("WARN")
    summary = "SUMMARY: %d PASS, %d FAIL" % (n_pass, n_fail)
    warns = [x["id"] for x in R.items if x["verdict"] == "WARN"]
    lines = [summary, "WARN: %d%s" % (n_warn, (" (" + ", ".join(warns) + ")") if warns else ""),
             "timeline_player_check %s; exe %s; port %d; pid %s; exit code %d; %.0f s"
             % (started, C.exe or "(attach)", C.port, C.pid, exit_code, time.perf_counter() - T0)]
    for x in R.items:
        lines.append("%-7s %-4s  %s" % (x["id"], x["verdict"], x["text"]))
    lines.append(summary)
    with open(os.path.join(C.out, "timeline_player_report.txt"), "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")
    doc = {"started": started, "exe": C.exe, "project": C.project, "out": C.out, "port": C.port, "pid": C.pid,
           "exitCode": exit_code, "seconds": round(time.perf_counter() - T0, 1), "summary": summary,
           "counts": {"pass": n_pass, "fail": n_fail, "warn": n_warn}, "info": C.info,
           "python": sys.version.split()[0], "checks": R.items}
    with open(os.path.join(C.out, "timeline_player_check.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(doc, f, ensure_ascii=False, indent=1, default=str)


def main(argv):
    for s in (sys.stdout, sys.stderr):
        try:
            s.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, ValueError):
            pass
    ap = argparse.ArgumentParser(description="The Timeline and the frame recording in the HS2Next app player (TP1, TP2, TW6-TW8, TA1, TA6).")
    ap.add_argument("--exe", help="path to HS2Next.exe (the built app player)")
    ap.add_argument("--project", required=True, help="project root (Config\\settings.local.json, Cache, Tests\\Timeline, Tools\\targets.json)")
    ap.add_argument("--out", required=True, help="output folder (report, json, player.log, userdata, bridge_out)")
    ap.add_argument("--port", type=int, default=DEFAULT_PORT)
    ap.add_argument("--timeout-min", type=float, default=20.0, help="whole run; past it the started PID is killed")
    ap.add_argument("--ready-timeout-min", type=float, default=12.0)
    ap.add_argument("--only", default="", help="comma list of TP1, TP2, TW6, TW7, TW8, TA1, TA6 (QUIT always runs when the player was started)")
    ap.add_argument("--measure-frames", type=int, default=600)
    ap.add_argument("--keep-frames", action="store_true", help="keep the recorded PNG files (default: removed after their checks)")
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
                if C.want("TP1") or C.want("TP2"):
                    guard(C, "TP1", tp)
                for cid, fn in (("TW6", tw6), ("TW7", tw7), ("TW8", tw8), ("TA1", ta1), ("TA6", ta6)):
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
            % (C.R.count("PASS"), C.R.count("FAIL"), C.R.count("WARN"), exit_code, os.path.join(C.out, "timeline_player_report.txt")))
    return exit_code


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
