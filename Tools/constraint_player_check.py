"""
constraint_player_check.py - constraints in the built app player (Phase 6.5; the row ConstraintCheck.player).

Starts the app player itself (no plugins: HS2NEXT_NO_PLUGINS=1, HS2NEXT_PRIVATE_OFF=1) and checks over real HTTP from outside the process,
in real frames, with the UI off. It never uses the Unity Editor. Python 3.14, standard library only. Content: the clothed SFW visual
targets of Tools\\targets.json on non-H rows, judged by numbers; saves use the placeholder thumbnail; no image is opened.

Usage (the run_tests.ps1 row ConstraintCheck.player calls the first form):
    python -I Tools\\constraint_player_check.py --exe <path to HS2Next.exe> --out <dir> [--project <project root>] [--port 17828]
                                                [--timeout-min 20]
    more options: --only CW1,CW2,CW4,CW5,CW7,CHAIN,TRIP (default all), --measure-frames 600, --min-free-gb 8, --ram-wait-min 10,
                  --ready-timeout-min 12, --attach (use a player already serving on --port: no start, no quit)
    -I ignores PYTHONUTF8, so the script sets its own stdout/stderr to UTF-8.

Checks (one report line each; a time over its bar is WARN, the lead decides):
  SETUP  ready time, versions
  CW5    an empty list: passActive false, no driven stage on a character, the stage list equal before and after a list came and went
  CW1    realistic: 8 constraints on BR-Chan and HERO107 (two hands and two feet on IK targets, a head look-at on a bone, an item following a bone,
         a node on a node, a camera node on a head), --measure-frames frames: the evaluation's median at most 100 us, p95 at most 250 us
  CW4    the pass itself: in CW1's scene the median of what a walk took beside the characters' steps, at most 0.05 ms: an upper bound, the units between
         the steps are in it (a frame's length cannot show it: a batch player runs at 60 frames a second)
  CW2    large: 64 constraints (26 IK targets, 24 bones, 14 nodes): median at most 500 us, p95 at most 1,000 us
  CW7    the stages -99, 440, 955 with 16 driven bones: the sum of their medians less the evaluation, at most 0.1 ms
  CHAIN  A's hand -> a node -> B's IK target -> B's hand -> a node on a moving row: B's hand on A's hand in one read of both, frame after frame
  TRIP   an entry of each target kind, save, scene.new, load: the same list, the second save's scene.json equal, the target as before
  QUIT   app.quit answers quitting:true and the player exits with 0
  LOG    exceptions in player.log: FAIL with 0 allowed
CW3 (no allocation) runs in the headless ConstraintTest, where the allocation of a thread can be read around the walk; CW6 and the time of CW5 there too.

Outputs:
  <out>\\constraint_player_report.txt  first and last line "SUMMARY: N PASS, M FAIL", second line "WARN: K (ids)", then one line per check
  <out>\\constraint_player_check.json  details of every check
  <out>\\player.log, <out>\\userdata\\ (the saves)
Exit code: 0 every check passed (WARN allowed), 1 a check failed, 2 setup error, 3 aborted by the game gate.
Ports: 17828 by default (17829 = ConstraintTest.play, 17322 = interactive default). Always 127.0.0.1, never localhost.
"""
import argparse
import ctypes
import http.client
import json
import math
import os
import socket
import subprocess
import sys
import threading
import time
import traceback
import zipfile
from datetime import datetime, timezone

DEFAULT_PORT = 17828
BAR_CW1_MEDIAN_US = 100.0
BAR_CW1_P95_US = 250.0
BAR_CW2_MEDIAN_US = 500.0
BAR_CW2_P95_US = 1000.0
BAR_CW4_MS = 0.05
BAR_CW7_MS = 0.1
GATE_FILE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "session_gate.json")
CREATE_NO_WINDOW = 0x08000000
CLOCK = "2026-11-04T00:00:00Z"
T0 = time.perf_counter()


def log(msg):
    print("[%7.1f s] %s" % (time.perf_counter() - T0, msg), flush=True)


def utc_now():
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


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
    C.R.add("LOG", "FAIL" if exc else "PASS", "player.log %d lines; %d with 'Exception'%s"
            % (len(lines), len(exc), (" (first: %s)" % exc[0][:160]) if exc else ""), exceptions=exc[:20])


# ---------------------------------------------------------------- helpers of the checks

ROW_DANCE = {"group": 11, "category": 5, "no": 0}
ROW_IDLE = {"group": 19, "category": 0, "no": 0}


def target_rel(C, sex):
    rel = C.targets["visual"][sex].replace("\\", "/")
    for pre in ("chara/female/", "chara/male/"):
        if rel.lower().startswith(pre):
            return rel[len(pre):]
    return rel


def two_characters(C, row_a=ROW_IDLE, speed=1.0):
    """scene.new, BR-Chan and HERO107 in one batch, physics off, on a row in one batch."""
    C.call({"cmd": "scene.new"})
    r = C.call({"cmd": "batch", "cmds": [
        {"cmd": "chara.load", "path": target_rel(C, "female"), "sex": "female", "as": "a"},
        {"cmd": "chara.motion", "key": "$a.added.0.key", "physics": False},
        {"cmd": "chara.load", "path": target_rel(C, "male"), "sex": "male", "as": "b"},
        {"cmd": "chara.motion", "key": "$b.added.0.key", "physics": False},
    ]}, timeout=300)
    a, b = r["results"][0]["added"][0]["key"], r["results"][2]["added"][0]["key"]
    C.call({"cmd": "batch", "cmds": [dict(row_a, cmd="chara.anime", key=a, time=0.0, speed=speed), dict(ROW_IDLE, cmd="chara.anime", key=b, time=0.0, speed=speed)]})
    return a, b


def key_of(reply):
    return reply.get("key") or reply["added"][0]["key"]


def node(C):
    return key_of(C.call({"cmd": "item.add", "no": 399}))


def add(C, source, target, components, **more):
    req = {"cmd": "constraint.add", "source": source, "target": target, "components": components}
    req.update(more)
    return C.call(req)["constraint"]


def status(C, **args):
    return C.call(dict(args, cmd="constraint.status"))


def stage_names(C, key):
    return [s["name"] for s in C.call({"cmd": "chara.motion", "key": key})["stages"]]


def measure(C, frames):
    """The evaluation's window over frames: {frames, medianUs, p95Us, maxUs}."""
    status(C, perf=True, reset=True)
    C.frames(frames)
    return status(C, perf=True)["perf"]


def bar(C, cid, what, value, limit, unit, **data):
    C.R.add(cid, "PASS" if value <= limit else "WARN", "%s %.3f %s (bar %.3f)" % (what, value, unit, limit), **data)


def realistic(C):
    """CW1's scene: the eight constraints; returns (a, b)."""
    a, b = two_characters(C, ROW_DANCE)
    C.call({"cmd": "batch", "cmds": [{"cmd": "ik.copy", "key": a, "from": "current", "enable": True}]})
    C.frames(3)
    for ik, bone in (("LeftHand", "cf_J_Hand_L"), ("RightHand", "cf_J_Hand_R"), ("LeftFoot", "cf_J_Foot01_L"), ("RightFoot", "cf_J_Foot01_R")):
        add(C, {"key": b, "bone": bone}, {"key": a, "ik": ik}, ["position", "rotation"])
    add(C, {"key": b, "bone": "cf_J_Head"}, {"key": a, "bone": "cf_J_Head"}, ["lookAt"])
    add(C, {"key": a, "bone": "cf_J_Hand_R"}, {"key": node(C)}, ["parent"])
    n1, n2 = node(C), node(C)
    add(C, {"key": n1}, {"key": n2}, ["position", "rotation"])
    cam = key_of(C.call({"cmd": "camera.add"}))
    add(C, {"key": b, "bone": "cf_J_Head"}, {"key": cam}, ["position", "rotation"])
    return a, b


# ---------------------------------------------------------------- the checks

def cw5(C):
    a, b = two_characters(C)
    C.frames(5)
    before = stage_names(C, a)
    s = status(C)
    ok = s["count"] == 0 and s["active"] == 0 and not s["passActive"] and not any(n.startswith("bones.driven") for n in before)
    C.R.add("CW5", "PASS" if ok else "FAIL", "an empty list: count %s, active %s, passActive %s, %d stages" % (s["count"], s["active"], s["passActive"], len(before)))
    n = node(C)
    add(C, {"key": n}, {"key": a, "bone": "cf_J_EarBase_s_L"}, ["rotation"])
    C.frames(5)
    with_bone = stage_names(C, a)
    C.call({"cmd": "constraint.clear"})
    C.frames(5)
    after = stage_names(C, a)
    s = status(C)
    ok = "bones.driven" in with_bone and after == before and not s["passActive"]
    C.R.add("CW5", "PASS" if ok else "FAIL", "the list came and went: stages as before (%d), with a driven bone %d, passActive %s" % (len(after), len(with_bone), s["passActive"]))


def cw1(C):
    realistic(C)
    C.frames(30)
    s = status(C)
    if s["active"] != 8 or not s["passActive"]:
        C.R.add("CW1", "FAIL", "active %s of 8, passActive %s: %s" % (s["active"], s["passActive"], json.dumps(C.call({"cmd": "constraint.list"})["constraints"])[:400]))
        return
    p = measure(C, C.a.measure_frames)
    bar(C, "CW1", "median", p["medianUs"], BAR_CW1_MEDIAN_US, "us", perf=p)
    bar(C, "CW1", "p95", p["p95Us"], BAR_CW1_P95_US, "us", perf=p)
    C.info["cw1"] = p


def cw4(C):
    realistic(C)
    C.frames(30)
    # a batch player runs at 60 frames a second, so a frame's length cannot show this: the walk's own time, its units included, an upper bound
    p = measure(C, C.a.measure_frames)
    bar(C, "CW4", "the pass itself, the walk beside the steps (evaluation between the steps included):", p["walkUs"] / 1000.0, BAR_CW4_MS, "ms", perf=p)


def cw2(C):
    a, b = two_characters(C, ROW_DANCE)
    C.call({"cmd": "batch", "cmds": [{"cmd": "ik.copy", "key": k, "from": "current", "enable": True} for k in (a, b)]})
    C.frames(3)
    src = {"key": node(C)}
    names = ["Body", "LeftShoulder", "LeftArmChain", "LeftHand", "RightShoulder", "RightArmChain", "RightHand", "LeftThigh", "LeftLegChain", "LeftFoot", "RightThigh", "RightLegChain", "RightFoot"]
    for k in (a, b):
        for ik in names:
            add(C, src, {"key": k, "ik": ik}, ["position"])
    rows = [x["bone"] for x in C.call({"cmd": "pose.bones", "key": a, "limit": 2000})["bones"] if not x.get("protected") and x.get("part") == "body"]
    step = max(1, len(rows) // 24)
    bones = rows[::step][:24]
    for bone in bones:
        add(C, {"key": b, "bone": "cf_J_Head"}, {"key": a, "bone": bone}, ["rotation"])
    for i in range(14):
        add(C, {"key": a, "bone": "cf_J_Hand_R"}, {"key": node(C)}, ["parent"])
    C.frames(30)
    s = status(C)
    if s["count"] != 64:
        C.R.add("CW2", "FAIL", "%s entries" % s["count"])
        return
    p = measure(C, C.a.measure_frames)
    C.R.add("CW2", "PASS" if s["active"] >= 60 else "WARN", "%s of 64 active" % s["active"])
    bar(C, "CW2", "median", p["medianUs"], BAR_CW2_MEDIAN_US, "us", perf=p)
    bar(C, "CW2", "p95", p["p95Us"], BAR_CW2_P95_US, "us", perf=p)


def cw7(C):
    a, b = two_characters(C)
    C.frames(3)
    src = {"key": node(C)}
    rows = [x["bone"] for x in C.call({"cmd": "pose.bones", "key": a, "limit": 2000})["bones"] if not x.get("protected") and x.get("part") == "body"]
    step = max(1, len(rows) // 16)
    for bone in rows[::step][:16]:
        add(C, src, {"key": a, "bone": bone}, ["position", "rotation"])
    C.frames(30)
    C.call({"cmd": "app.perf", "motion": True, "reset": True})
    p = measure(C, C.a.measure_frames)
    stages = C.call({"cmd": "app.perf", "motion": True})["motion"]["stages"]
    ms = lambda ph: stages.get(str(ph), {}).get("medianMs", 0.0)
    total = ms(-99) + ms(440) + ms(955)
    bar(C, "CW7", "stages -99 %.4f, 440 %.4f, 955 %.4f ms, evaluation %.1f us: without it" % (ms(-99), ms(440), ms(955), p["medianUs"]), total - p["medianUs"] / 1000.0, BAR_CW7_MS, "ms")
    work = ms(-99) > 0 and ms(440) > 0 and ms(955) > 0
    C.R.add("CW7", "PASS" if work else "FAIL", "the three stages are timed: %s" % work)


def chain(C):
    a, b = two_characters(C, ROW_IDLE)
    C.call({"cmd": "ik.copy", "key": b, "from": "current", "enable": True})
    C.frames(3)
    bones = C.call({"cmd": "batch", "cmds": [{"cmd": "chara.bones", "key": a, "names": ["cf_J_Hand_R"]}, {"cmd": "chara.bones", "key": b, "names": ["cf_J_Hand_L", "cf_J_ArmUp00_L"]}]})["results"]
    pa, pb, sh = bones[0]["bones"]["cf_J_Hand_R"]["pos"], bones[1]["bones"]["cf_J_Hand_L"]["pos"], bones[1]["bones"]["cf_J_ArmUp00_L"]["pos"]
    o = C.call({"cmd": "obj.transform", "key": b})["object"]
    now = o.get("pos") or o["transform"]["pos"]   # a new node stands at the camera's pivot
    # A's hand three quarters of an arm from B's shoulder: inside the reach, so the hand can stand on it
    C.call({"cmd": "obj.transform", "key": b, "pos": [now[i] + pa[i] - sh[i] - 0.75 * (pb[i] - sh[i]) for i in range(3)]})
    n1, n2 = node(C), node(C)
    add(C, {"key": a, "bone": "cf_J_Hand_R"}, {"key": n1}, ["parent"], keepOffset=False)
    add(C, {"key": n1}, {"key": b, "ik": "LeftHand"}, ["position"], keepOffset=False)
    add(C, {"key": b, "bone": "cf_J_Hand_L"}, {"key": n2}, ["parent"], keepOffset=False)
    C.frames(5)
    s = status(C)
    ok = s["active"] == 3 and not s["lagged"]
    C.R.add("CHAIN", "PASS" if ok else "FAIL", "three active, no lag: active %s, lagged %s, order %s" % (s["active"], s["lagged"], [o["kind"] for o in s["order"]]))
    worst, moved, last = 0.0, 0.0, None
    for _ in range(60):
        r = C.call({"cmd": "batch", "cmds": [{"cmd": "chara.bones", "key": a, "names": ["cf_J_Hand_R"]}, {"cmd": "chara.bones", "key": b, "names": ["cf_J_Hand_L"]}]})["results"]
        pa, pb = r[0]["bones"]["cf_J_Hand_R"]["pos"], r[1]["bones"]["cf_J_Hand_L"]["pos"]
        worst = max(worst, math.dist(pa, pb))
        if last is not None:
            moved = max(moved, math.dist(pa, last))
        last = pa
        C.frames(2)
    # the reads are rounded to 4 decimals
    C.R.add("CHAIN", "PASS" if worst <= 5e-4 else "FAIL", "B's hand on A's hand in one read, 60 reads: worst %.5f; A's hand moved up to %.4f between reads" % (worst, moved))
    C.R.add("CHAIN", "PASS" if moved > 5e-4 else "WARN", "the source moves enough to show a frame behind: %.4f" % moved)


def trip(C):
    a, b = two_characters(C, ROW_IDLE, speed=0.0)
    C.call({"cmd": "ik.copy", "key": b, "from": "current", "enable": True})
    C.frames(3)
    n, t = node(C), node(C)
    C.call({"cmd": "obj.transform", "key": n, "pos": [1.0, 8.0, 2.0], "rot": [10.0, 20.0, 30.0]})
    add(C, {"key": n}, {"key": t}, ["position", "rotation"], keepOffset=False)
    add(C, {"key": n}, {"key": a, "bone": "cf_J_EarBase_s_L"}, ["rotation"], keepOffset=False)
    add(C, {"key": n}, {"key": b, "ik": "RightHand"}, ["position"], weight=0.5, label="trip")
    C.frames(5)
    before = C.call({"cmd": "constraint.list"})["constraints"]
    stable = lambda rows: [{k: v for k, v in r.items() if k not in ("state", "lag", "effective", "reason")} for r in rows]
    C.call({"cmd": "scene.pause", "paused": True})
    C.frames(3)
    p1 = C.call({"cmd": "scene.save", "name": "trip1", "overwrite": True, "thumb": "placeholder", "clock": CLOCK})["path"]
    C.call({"cmd": "scene.new"})
    C.call({"cmd": "scene.load", "path": p1}, timeout=300)
    C.frames(10)
    after = C.call({"cmd": "constraint.list"})["constraints"]
    same = stable(before) == stable(after) and all(r["state"] == "active" for r in after)
    C.R.add("TRIP", "PASS" if same else "FAIL", "the list after a load: %d entries, states %s" % (len(after), [r["state"] for r in after]))
    C.call({"cmd": "scene.pause", "paused": True})
    C.frames(3)
    p2 = C.call({"cmd": "scene.save", "name": "trip2", "overwrite": True, "thumb": "placeholder", "clock": CLOCK})["path"]
    read = lambda p: json.loads(zipfile.ZipFile(p).read("scene.json").decode("utf-8"))
    d1, d2 = read(p1), read(p2)
    C.R.add("TRIP", "PASS" if d1.get("constraints") == d2.get("constraints") and d1.get("constraints") else "FAIL",
            "scene.json's member equal in the second save: %d entries" % len(d1.get("constraints") or []))
    C.call({"cmd": "scene.pause", "paused": False})


def write_reports(C, exit_code, started):
    R = C.R
    n_pass, n_fail, n_warn = R.count("PASS"), R.count("FAIL"), R.count("WARN")
    summary = "SUMMARY: %d PASS, %d FAIL" % (n_pass, n_fail)
    warns = [x["id"] for x in R.items if x["verdict"] == "WARN"]
    lines = [summary, "WARN: %d%s" % (n_warn, (" (" + ", ".join(warns) + ")") if warns else ""),
             "constraint_player_check %s; exe %s; port %d; pid %s; exit code %d; %.0f s"
             % (started, C.exe or "(attach)", C.port, C.pid, exit_code, time.perf_counter() - T0)]
    for x in R.items:
        lines.append("%-7s %-4s  %s" % (x["id"], x["verdict"], x["text"]))
    lines.append(summary)
    with open(os.path.join(C.out, "constraint_player_report.txt"), "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")
    doc = {"started": started, "exe": C.exe, "project": C.project, "out": C.out, "port": C.port, "pid": C.pid,
           "exitCode": exit_code, "seconds": round(time.perf_counter() - T0, 1), "summary": summary,
           "counts": {"pass": n_pass, "fail": n_fail, "warn": n_warn}, "info": C.info,
           "python": sys.version.split()[0], "checks": R.items}
    with open(os.path.join(C.out, "constraint_player_check.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(doc, f, ensure_ascii=False, indent=1, default=str)


def main(argv):
    for s in (sys.stdout, sys.stderr):
        try:
            s.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, ValueError):
            pass
    ap = argparse.ArgumentParser(description="Constraints in the HS2Next app player (CW1, CW2, CW4, CW5, CW7, CHAIN, TRIP).")
    ap.add_argument("--exe", help="path to HS2Next.exe (the built app player)")
    ap.add_argument("--project", default=os.path.dirname(os.path.dirname(os.path.abspath(__file__))), help="project root (Config\\settings.local.json, Cache, Tools\\targets.json); default: the folder above Tools")
    ap.add_argument("--out", required=True, help="output folder (report, json, player.log, userdata, bridge_out)")
    ap.add_argument("--port", type=int, default=DEFAULT_PORT)
    ap.add_argument("--timeout-min", type=float, default=20.0, help="whole run; past it the started PID is killed")
    ap.add_argument("--ready-timeout-min", type=float, default=12.0)
    ap.add_argument("--only", default="", help="comma list of CW1, CW2, CW4, CW5, CW7, CHAIN, TRIP (QUIT always runs when the player was started)")
    ap.add_argument("--measure-frames", type=int, default=600)
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
                for cid, fn in (("CW5", cw5), ("CW1", cw1), ("CW4", cw4), ("CW2", cw2), ("CW7", cw7), ("CHAIN", chain), ("TRIP", trip)):
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
            % (C.R.count("PASS"), C.R.count("FAIL"), C.R.count("WARN"), exit_code, os.path.join(C.out, "constraint_player_report.txt")))
    return exit_code


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
