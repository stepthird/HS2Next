"""
advancedpose_player_check.py - bone corrections, jiggle values and blend-shape overrides in the built app player (Phase 6.3; the row
AdvancedPoseCheck.player).

Starts the app player itself (no plugins: HS2NEXT_NO_PLUGINS=1, HS2NEXT_PRIVATE_OFF=1) and checks over real HTTP from outside the
process, in real frames, with the UI off. It never uses the Unity Editor. Python 3.14, standard library only. Content: the clothed
SFW visual targets of Tools\\targets.json and their non-H idle row, judged by numbers; saves use the placeholder thumbnail; no image
is opened.

Usage (the run_tests.ps1 row AdvancedPoseCheck.player calls the first form):
    python -I Tools\\advancedpose_player_check.py --exe <path to HS2Next.exe> --out <dir> [--project <project root>] [--port 17389]
                                                  [--timeout-min 20]
    more options: --only AT1,AT2,AT3,AO2,AA1,FM15 (default all), --measure-frames 600, --min-free-gb 8, --ram-wait-min 10,
                  --ready-timeout-min 12, --attach (use a player already serving on --port: no start, no quit)
    -I ignores PYTHONUTF8, so the script sets its own stdout/stderr to UTF-8.

Checks (one report line each):
  SETUP  ready time, versions
  AT1    1, 8 and 64 corrected bones (all three channels) on BR-Chan, --measure-frames frames each, clip running: the sum of the median
         times of the phases -100, 450, 950 (app.perf {motion}), the restore at -100 counted as it runs in every mode; 64: at most 0.5 ms
         (over the bar: WARN with the numbers; the lead decides); the sum exceeds the phases 450 + 950 at 64 bones (a restore that does no work: FAIL)
  AT2    after clearing: the character's stage list holds no pose.* stage and 60 frames time none of those phases
  AT3    16 blend-shape overrides on o_head over --measure-frames frames: the median of phase 610 at most 0.1 ms (over the bar: WARN);
         `pose.chains.set` of the longest chain's soft members, 200 calls: the command's time as information (the stage's own time is in the
         headless row, AJ3)
  FM15   the card modifiers' stages: the card with the most modifiers of the local list (Cache\\compare_mods\\cards.json, or --modifier-card <rel>
         --modifier-sex female), row 19/0/0, UI off, --measure-frames frames: the median of the phases 120 + 960 at most 0.2 ms per character
         (over the bar: WARN; no local list: WARN, not run)
  AO2    the real order in the player: corrections on bones the card's modifiers write and on others, twin characters of one card in one
         batch: every corrected bone = the twin's value with the correction composed (1e-5, 0.01 deg) at frames 0, 300, 600 and through 120
         paused frames; clearing: the twin's value (1e-5, 0.01 deg)
  AA1    set corrections, jiggle values and blend overrides, save, scene.new, load: the block in the file equal, the bone values within 1e-5
         (0.01 deg), the second save's block equal
  QUIT   app.quit answers quitting:true and the player exits with 0
  LOG    exceptions in player.log: FAIL with 0 allowed (AA1)

Outputs:
  <out>\\advancedpose_player_report.txt  first and last line "SUMMARY: N PASS, M FAIL", second line "WARN: K (ids)", then one line per check
  <out>\\advancedpose_player_check.json  details of every check
  <out>\\player.log, <out>\\userdata\\ (the saves)
Exit code: 0 every check passed (WARN allowed), 1 a check failed, 2 setup error, 3 aborted by the game gate.
Ports: 17389 by default (17398 = the Editor play rows, 17387 = TimelineCheck.player, 17322 = interactive default).
Always 127.0.0.1, never localhost.
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

DEFAULT_PORT = 17389
BAR_BONES_MS = 0.5
BAR_BLEND_MS = 0.1
BAR_MODIFIERS_MS = 0.2
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

CORR = {"pos": [0.01, -0.005, 0.008], "rot": [7.0, -9.0, 11.0], "scale": [1.05, 0.95, 1.1]}
# bones the targets' cards change and bones they do not; the ones this body lacks are left out
AO2_BONES = ["cf_J_Hand_Index02_L", "cf_J_ArmUp00_R", "cf_J_Spine02", "cf_J_Neck", "cf_J_LegUp00_L", "cf_J_Foot02_L", "cf_J_Hips",
             "cf_J_NoseBase_s", "cf_J_EarBase_s_L", "cf_J_Mouth_L", "cf_J_Spine01_s", "cf_J_Kosi01_s"]


def target_rel(C, sex):
    rel = C.targets["visual"][sex].replace("\\", "/")
    for pre in ("chara/female/", "chara/male/"):
        if rel.lower().startswith(pre):
            return rel[len(pre):]
    return rel


def add_twins(C, n=2, speed=0.0):
    """scene.new, n characters of the female target in one batch, physics off, the idle row in one batch: they run in lockstep."""
    C.call({"cmd": "scene.new"})
    rel = target_rel(C, "female")
    cmds = []
    for i in range(n):
        cmds.append({"cmd": "chara.load", "path": rel, "sex": "female", "as": "c%d" % i})
        cmds.append({"cmd": "chara.motion", "key": "$c%d.added.0.key" % i, "physics": False})
    r = C.call({"cmd": "batch", "cmds": cmds}, timeout=300)
    keys = [r["results"][2 * i]["added"][0]["key"] for i in range(n)]
    C.call({"cmd": "batch", "cmds": [{"cmd": "chara.anime", "key": k, "group": 19, "category": 0, "no": 0, "time": 0.25, "speed": speed} for k in keys]})
    return keys


def dump(C, key):
    return {b["name"]: b for b in C.call({"cmd": "bones.dump", "key": key, "root": "all"}, timeout=120)["bones"]}


def dump2(C, a, b):
    """The bones of two characters read in one frame (one batch)."""
    r = C.call({"cmd": "batch", "cmds": [{"cmd": "bones.dump", "key": k, "root": "all"} for k in (a, b)]}, timeout=120)["results"]
    return [{x["name"]: x for x in reply["bones"]} for reply in r]


def qmul(a, b):
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return [aw * bx + ax * bw + ay * bz - az * by, aw * by - ax * bz + ay * bw + az * bx,
            aw * bz + ax * by - ay * bx + az * bw, aw * bw - ax * bx - ay * by - az * bz]


def euler(e):
    """Unity's Quaternion.Euler: z, then x, then y."""
    x, y, z = [math.radians(v) / 2.0 for v in e]
    qx = [math.sin(x), 0.0, 0.0, math.cos(x)]
    qy = [0.0, math.sin(y), 0.0, math.cos(y)]
    qz = [0.0, 0.0, math.sin(z), math.cos(z)]
    return qmul(qmul(qy, qx), qz)


def angle(a, b):
    dot = abs(sum(x * y for x, y in zip(a, b)))
    na = math.sqrt(sum(x * x for x in a) * sum(x * x for x in b))
    return 2.0 * math.degrees(math.acos(min(1.0, dot / na)))


def composed(a, b, c=CORR):
    """The worst of the shown bone a against the twin's bone b with the correction composed: (position and scale, degrees)."""
    p = max(abs(a["lp"][i] - (b["lp"][i] + c["pos"][i])) for i in range(3))
    s = max(abs(a["ls"][i] - b["ls"][i] * c["scale"][i]) for i in range(3))
    r = angle(a["lq"], qmul(b["lq"], euler(c["rot"])))
    return max(p, s), r


def plain(a, b):
    p = max(abs(a["lp"][i] - b["lp"][i]) for i in range(3))
    s = max(abs(a["ls"][i] - b["ls"][i]) for i in range(3))
    return max(p, s), angle(a["lq"], b["lq"])


def perf_reset(C):
    C.call({"cmd": "app.perf", "motion": True, "reset": True})


def perf_read(C):
    return C.call({"cmd": "app.perf", "motion": True})["motion"]


def median(stages, phase):
    e = stages.get(str(phase))
    return e["medianMs"] if e else 0.0


def pick_bones(C, key, n):
    """n addressable, unprotected bones of the body and the head, spread over the list."""
    rows = [b for b in C.call({"cmd": "pose.bones", "key": key, "limit": 2000})["bones"]
            if not b.get("protected") and b.get("part") in ("body", "head")]
    if len(rows) < n:
        raise RuntimeError("only %d bones listed" % len(rows))
    step = len(rows) / float(n)
    return [rows[int(i * step)]["bone"] for i in range(n)]


# ---------------------------------------------------------------- AT1, AT2

def at(C):
    a = C.a
    k = add_twins(C, 1, 1.0)[0]
    C.frames(30)
    numbers = {}
    for n in (1, 8, 64):
        C.call({"cmd": "pose.bones.clear", "key": k, "all": True})
        bones = pick_bones(C, k, n)
        C.call({"cmd": "pose.bones.set", "key": k, "bones": {b: CORR for b in bones}})
        C.frames(10)
        perf_reset(C)
        C.frames(a.measure_frames)
        m = perf_read(C)
        st = m["stages"]
        parts = [median(st, -100), median(st, 450), median(st, 950)]
        numbers[n] = {"frames": m["frames"], "restore": parts[0], "apply": parts[1], "end": parts[2], "sum": round(sum(parts), 4)}
    s64 = numbers[64]["sum"]
    verdict = "PASS" if s64 <= BAR_BONES_MS else "WARN"
    C.R.add("AT1", verdict, "the sum of the phases -100, 450, 950, median per frame: " +
            ", ".join("%d bones %.4f ms (%.4f + %.4f + %.4f, %d frames)" % (n, v["sum"], v["restore"], v["apply"], v["end"], v["frames"]) for n, v in numbers.items()) +
            "; the restore is counted; bar %.1f ms at 64" % BAR_BONES_MS, numbers=numbers)
    restore = numbers[64]["restore"]
    C.R.add("AT1.restore", "PASS" if restore > 0 and s64 > numbers[64]["apply"] + numbers[64]["end"] else "FAIL",
            "the sum at 64 bones (%.4f ms) exceeds the phases 450 + 950 (%.4f ms): the restore at -100 does its work (%.4f ms)"
            % (s64, numbers[64]["apply"] + numbers[64]["end"], restore))
    # AT2
    C.call({"cmd": "pose.bones.clear", "key": k, "all": True})
    C.frames(5)
    perf_reset(C)
    C.frames(60)
    m = perf_read(C)
    left = [p for p in ("-100", "450", "950") if p in m["stages"]]
    names = [x.get("name") if isinstance(x, dict) else x for x in C.call({"cmd": "chara.motion", "key": k}).get("stages") or []]
    pose = [n for n in names if str(n).startswith("pose.")]
    C.R.add("AT2", "PASS" if not left and not pose else "FAIL", "after clearing: timed phases %s, pose stages %s (of %d stages, %d frames)"
            % (left or "none of -100, 450, 950", pose or "none", len(names), m["frames"]))


# ---------------------------------------------------------------- FM15

def modifier_card(C):
    """(rel, sex, hash) of the card with the most modifiers of the local list, or of --modifier-card; None when there is neither."""
    a = C.a
    if a.modifier_card:
        return a.modifier_card, a.modifier_sex, "given"
    path = os.path.join(a.project, "Cache", "compare_mods", "cards.json")
    if not os.path.isfile(path):
        return None
    with open(path, encoding="utf-8") as f:
        cards = json.load(f).get("cards") or []
    if not cards:
        return None
    c = max(cards, key=lambda x: x.get("modifiers") or 0)
    return c["rel"], c["sex"], c["hash"]


def fm15(C):
    card = modifier_card(C)
    if card is None:
        C.R.add("FM15", "WARN", "no local list of modifier cards (Cache\\compare_mods\\cards.json) and no --modifier-card: not run")
        return
    rel, sex, tag = card
    C.call({"cmd": "scene.new"})
    r = C.call({"cmd": "chara.load", "path": rel, "sex": sex}, timeout=300)
    k = r.get("key") or (r.get("added") or [{}])[0].get("key")
    C.call({"cmd": "chara.motion", "key": k, "physics": False})
    C.call({"cmd": "chara.anime", "key": k, "group": 19, "category": 0, "no": 0, "time": 0.5, "speed": 0.0})
    C.frames(30)
    perf_reset(C)
    C.frames(C.a.measure_frames)
    m = perf_read(C)
    st = m["stages"]
    p120, p960 = median(st, 120), median(st, 960)
    s = round(p120 + p960, 4)
    names = [x.get("name") if isinstance(x, dict) else x for x in C.call({"cmd": "chara.motion", "key": k}).get("stages") or []]
    ok = "bones.modifiers" in names and "bones.modifiers.end" in names
    C.R.add("FM15", ("PASS" if s <= BAR_MODIFIERS_MS else "WARN") if ok else "FAIL",
            "card %s, row 19/0/0, %d frames: the median of phases 120 + 960 %.4f ms (%.4f + %.4f); bar %.1f ms; stages present: %s"
            % (tag, m["frames"], s, p120, p960, BAR_MODIFIERS_MS, ok), card=tag, numbers={"120": p120, "960": p960, "sum": s})
    C.call({"cmd": "scene.new"})


# ---------------------------------------------------------------- AT3

def at3(C):
    a = C.a
    k = add_twins(C, 1, 1.0)[0]
    head = None
    for rr in C.call({"cmd": "pose.blendShapes", "key": k, "part": "head"})["renderers"]:
        if rr.get("name") == "o_head":
            head = rr
    if head is None or len(head["shapes"]) < 16:
        C.R.add("AT3", "FAIL", "no o_head with 16 shapes in pose.blendShapes")
        return
    weights = {s["name"]: 10.0 + i for i, s in enumerate(head["shapes"][:16])}
    C.call({"cmd": "pose.blendShapes.set", "key": k, "renderer": head["renderer"], "weights": weights})
    C.frames(10)
    perf_reset(C)
    C.frames(a.measure_frames)
    m = perf_read(C)
    med = median(m["stages"], 610)
    C.R.add("AT3", "PASS" if 0.0 < med <= BAR_BLEND_MS else "WARN", "16 overrides on o_head: phase 610 median %.4f ms over %d frames; bar %.1f ms" % (med, m["frames"], BAR_BLEND_MS),
            medianMs=med)
    C.call({"cmd": "pose.blendShapes.clear", "key": k, "all": True})
    chains = C.call({"cmd": "pose.chains", "key": k})["chains"]
    if not chains:
        C.R.add("AT3c", "WARN", "no chain on the target")
        return
    longest = max(chains, key=lambda c: c.get("particles", 0))
    t0 = time.perf_counter()
    for i in range(200):
        C.call({"cmd": "pose.chains.set", "key": k, "chain": longest["chain"], "damping": 0.1 + (i % 7) * 0.05, "undo": False})
    C.R.add("AT3c", "PASS", "pose.chains.set on %d particles, 200 calls over HTTP: %.2f ms a call (the command and the transport, as information)"
            % (longest.get("particles", 0), (time.perf_counter() - t0) * 1000.0 / 200.0))
    C.call({"cmd": "pose.chains.clear", "key": k, "all": True})


# ---------------------------------------------------------------- AO2

def ao2(C):
    a, b = add_twins(C, 2, 0.0)
    C.frames(30)
    d0 = dump(C, a)
    bones = [n for n in AO2_BONES if n in d0]
    C.call({"cmd": "pose.bones.set", "key": a, "bones": {n: CORR for n in bones}})
    worst = {"num": 0.0, "deg": 0.0, "at": ""}

    def look(tag):
        da, db = dump2(C, a, b)
        for n in bones:
            p, r = composed(da[n], db[n])
            if p > worst["num"] or r > worst["deg"]:
                worst["num"], worst["deg"] = max(p, worst["num"]), max(r, worst["deg"])
                worst["at"] = "%s, %s" % (n, tag)

    look("frame 0")
    C.frames(300)
    look("frame 300")
    C.frames(300)
    look("frame 600")
    C.call({"cmd": "scene.pause", "paused": True})
    C.frames(120)
    look("paused")
    C.call({"cmd": "scene.pause", "paused": False})
    C.frames(60)
    look("resumed")
    C.R.add("AO2", "PASS" if worst["num"] <= 1e-5 and worst["deg"] <= 0.01 else "FAIL",
            "%d bones against the twin with the correction composed, 600 frames, 120 paused: worst %.3g units, %.3g deg (%s)" % (len(bones), worst["num"], worst["deg"], worst["at"]), bones=bones)
    C.call({"cmd": "pose.bones.clear", "key": a, "all": True})
    C.frames(2)
    da, db = dump2(C, a, b)
    p, r = 0.0, 0.0
    for n in bones:
        x, y = plain(da[n], db[n])
        p, r = max(p, x), max(r, y)
    C.R.add("AO2c", "PASS" if p <= 1e-5 and r <= 0.01 else "FAIL", "after clearing against the twin: worst %.3g units, %.3g deg" % (p, r))


# ---------------------------------------------------------------- AA1

def zip_block(path):
    with zipfile.ZipFile(path) as z:
        doc = json.loads(z.read("scene.json").decode("utf-8"))

    def walk(x):
        if isinstance(x, dict):
            if "advancedPose" in x:
                return x["advancedPose"]
            for v in x.values():
                r = walk(v)
                if r is not None:
                    return r
        elif isinstance(x, list):
            for v in x:
                r = walk(v)
                if r is not None:
                    return r
        return None

    return walk(doc)


def aa1(C):
    a = add_twins(C, 1, 0.0)[0]
    C.frames(5)
    bones = pick_bones(C, a, 16)
    C.call({"cmd": "pose.bones.set", "key": a, "bones": {n: CORR for n in bones}})
    chains = C.call({"cmd": "pose.chains", "key": a})["chains"]
    if chains:
        C.call({"cmd": "pose.chains.set", "key": a, "chain": chains[0]["chain"], "damping": 0.25, "gravity": [0.0, -0.01, 0.0]})
    C.call({"cmd": "pose.bust.set", "key": a, "chain": "breastL", "stiffness": 0.3})
    rend = [r for r in C.call({"cmd": "pose.blendShapes", "key": a, "part": "head"})["renderers"] if r.get("name") == "o_head"]
    if rend:
        C.call({"cmd": "pose.blendShapes.set", "key": a, "renderer": rend[0]["renderer"], "shape": rend[0]["shapes"][3]["name"], "weight": 37.5})
    C.frames(5)
    before = dump(C, a)
    p1 = C.call({"cmd": "scene.save", "name": "aa1_first", "overwrite": True, "thumb": "placeholder", "clock": CLOCK}, timeout=300)["path"]
    block1 = zip_block(p1)
    C.call({"cmd": "scene.new"})
    C.call({"cmd": "scene.load", "path": p1}, timeout=300)
    C.frames(10)
    scene = C.call({"cmd": "scene"})
    k = None
    for n in scene.get("objects") or []:
        if str(n.get("kind", "")).lower().startswith("chara"):
            k = n.get("key")
    if k is None:
        raise RuntimeError("no character after the load: " + json.dumps(scene)[:300])
    C.call({"cmd": "chara.motion", "key": k, "physics": False})
    C.frames(10)
    after = dump(C, k)
    p, r = 0.0, 0.0
    w = ""
    for n in bones:
        if n not in after or n not in before:
            p = 9e9
            w = n
            continue
        x, y = plain(after[n], before[n])
        if x > p or y > r:
            w = n
        p, r = max(p, x), max(r, y)
    p2 = C.call({"cmd": "scene.save", "name": "aa1_second", "overwrite": True, "thumb": "placeholder", "clock": CLOCK}, timeout=300)["path"]
    block2 = zip_block(p2)
    ok_block = block1 is not None and block1 == block2
    C.R.add("AA1", "PASS" if ok_block and p <= 1e-5 and r <= 0.01 else "FAIL",
            "set, save, scene.new, load: %d corrected bones, worst %.3g units, %.3g deg (%s); the block in the second save %s"
            % (len(bones), p, r, w, "equals the first" if ok_block else "DIFFERS"), block=block1)


# ---------------------------------------------------------------- the report

def write_reports(C, exit_code, started):
    R = C.R
    n_pass, n_fail, n_warn = R.count("PASS"), R.count("FAIL"), R.count("WARN")
    summary = "SUMMARY: %d PASS, %d FAIL" % (n_pass, n_fail)
    warns = [x["id"] for x in R.items if x["verdict"] == "WARN"]
    lines = [summary, "WARN: %d%s" % (n_warn, (" (" + ", ".join(warns) + ")") if warns else ""),
             "advancedpose_player_check %s; exe %s; port %d; pid %s; exit code %d; %.0f s"
             % (started, C.exe or "(attach)", C.port, C.pid, exit_code, time.perf_counter() - T0)]
    for x in R.items:
        lines.append("%-7s %-4s  %s" % (x["id"], x["verdict"], x["text"]))
    lines.append(summary)
    with open(os.path.join(C.out, "advancedpose_player_report.txt"), "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")
    doc = {"started": started, "exe": C.exe, "project": C.project, "out": C.out, "port": C.port, "pid": C.pid,
           "exitCode": exit_code, "seconds": round(time.perf_counter() - T0, 1), "summary": summary,
           "counts": {"pass": n_pass, "fail": n_fail, "warn": n_warn}, "info": C.info,
           "python": sys.version.split()[0], "checks": R.items}
    with open(os.path.join(C.out, "advancedpose_player_check.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(doc, f, ensure_ascii=False, indent=1, default=str)


def main(argv):
    for s in (sys.stdout, sys.stderr):
        try:
            s.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, ValueError):
            pass
    ap = argparse.ArgumentParser(description="Advanced pose in the HS2Next app player (AT1, AT2, AT3, AO2, AA1, FM15).")
    ap.add_argument("--exe", help="path to HS2Next.exe (the built app player)")
    ap.add_argument("--project", default=os.path.dirname(os.path.dirname(os.path.abspath(__file__))), help="project root (Config\\settings.local.json, Cache, Tools\\targets.json); default: the folder above Tools")
    ap.add_argument("--out", required=True, help="output folder (report, json, player.log, userdata, bridge_out)")
    ap.add_argument("--port", type=int, default=DEFAULT_PORT)
    ap.add_argument("--timeout-min", type=float, default=20.0, help="whole run; past it the started PID is killed")
    ap.add_argument("--ready-timeout-min", type=float, default=12.0)
    ap.add_argument("--only", default="", help="comma list of AT1, AT2, AT3, AO2, AA1, FM15 (QUIT always runs when the player was started)")
    ap.add_argument("--modifier-card", default="", help="FM15's card: its path under UserData\\chara\\<sex> (default: the largest of the local list)")
    ap.add_argument("--modifier-sex", default="female", help="FM15's card's sex with --modifier-card")
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
                if C.want("AT1") or C.want("AT2"):
                    guard(C, "AT1", at)
                for cid, fn in (("AT3", at3), ("FM15", fm15), ("AO2", ao2), ("AA1", aa1)):
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
            % (C.R.count("PASS"), C.R.count("FAIL"), C.R.count("WARN"), exit_code, os.path.join(C.out, "advancedpose_player_report.txt")))
    return exit_code


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
