"""
maker_session.py - the bridge-driven Maker session in the app player: two characters edited through every Maker list for an
hour (or the minutes asked), with coordinate re-applies, Studio item churn, map loads and scene save / new / load cycles,
while the process's GPU memory, its working set, the catalog's numbers and the frame times are sampled every 30 s.
Numbers and ids only: no image is captured, saved or opened (saves use the placeholder thumbnail).

Usage (the opt-in row MakerSession.player: --minutes 60, port 17396):
    python -I Tools\\maker_session.py --exe <HS2Next.exe> --project <project root> [--out <dir>] [--minutes 60]
                                      [--port 17396] [--seed 5201] [--label <text>]
    more options: --wall-min (default minutes + 15; the whole run, setup and quit included), --sample-sec 30,
                  --cycle-sec 300, --frame-poll-ms 1 (0 = no frame poller), --lock-dir <dir> (default HS2NEXT_LOCK_DIR,
                  else <system drive>\\tmp\\hs2next_tests), --lock-wait-min 0, --ram-wait-min 10, --min-free-gb 8,
                  --ready-timeout-min 12, --items <json> (default Tools\\session_items.json when present, else the
                  built-in vanilla item groups), --no-locks (diagnosis only: no GPU / input lock; never for a measured
                  run), --attach (a player already serving on --port: no start, no quit, no locks)
    -I ignores PYTHONUTF8: the script sets its own stdout / stderr to UTF-8.

Gates and bounds:
  * Game gate: the processes of Tools\\session_gate.json (a local file, not shipped; an image name starting with an entry)
    - the run refuses to start while one runs; a watcher polls every 10 s and, when one appears, ends the player by PID
    (app.quit, then a forced stop after 20 s) and records "aborted by the game gate" (exit 3, not a failure). Without the
    file there is no game gate and the run says so. Never gated on idle time.
  * Locks: phase52_gpu.lock and phase52_input.lock in the lock folder are both checked absent, then created exclusively
    (one line: who, the driver's PID (and the player's), start, expected end) and removed at the end, also on failure. A
    stale input lock (older than 90 min, its PID gone) or GPU lock (past its expected end + 30 min, its PID gone) is removed
    and the removal is logged.
  * Free RAM >= --min-free-gb before the start (waits up to --ram-wait-min); --wall-min for the whole run (the player is
    stopped by its PID); every bridge request bounded at 5 min.
  * Device watch: the System log's nvlddmkm events (all, id 153) and Display 4101 counted before and after; log.tail
    errors polled at every sample and player.log scanned at the end for device-removed codes; a device loss ends the run.

Player: <exe> -batchmode -ui -bridgePort <port> -bridgeAllowQuit -settings <project>\\Config\\settings.local.json
-cacheDir <cache> -userData <out>\\userdata -bridgeOutputDir <out>\\bridge_out -logFile <out>\\player.log, with
HS2NEXT_NO_PLUGINS=1 and HS2NEXT_PRIVATE_OFF=1 (batch mode runs at 60 fps; no window, no OS input).

Workload (seeded; every chosen id goes to events.tsv), cycles of --cycle-sec until --minutes have passed:
  setup   scene.new; the two SFW targets of Tools\\targets.json from their cards; each one's own coordinate saved
          (coord.save, placeholder thumbnail); the Edit tab opened (select + ui.action edit.show).
  cycle   the edited character alternates (odd cycles the female target, even the male); 40 part changes: hair 8 (two per
          slot), face / body 8, clothes 12 (only the outer kinds the character wears, swapped within the kind, never a
          "none" row; a one-piece top's kind and the inner layer are left alone), accessories 12 (add to free slots including the extra slots, change within
          the category, remove); half of them are first found through the list's search field and hovered on their
          cell through ui.input (text, pointer, then --hover-dwell-ms at rest: the prefetch path); every list touched is shown (its first-view settle
          time measured), expanded and scrolled with 20 wheel steps through ui.input; the saved coordinate re-applied; 20 Studio items added, 10 deleted, undo / redo bursts of 5; one map
          load (the five largest maps by lightmap bytes in the first six cycles); every third cycle scene.save ->
          scene.new -> scene.load and one character deleted and re-added from its card.
  The synchronous cost of each edit, item add and map load is the realtime difference of a batch [ping, command,
  camera.get (which settles the runtime), ping].

Sampling (every --sample-sec, from the player's start): the process's GPU memory (dedicated / shared, summed over
adapters) and the adapters' totals by one Get-Counter, the working set and private bytes, frame statistics of the
window from the frame poller (ping alone is answered off the main thread; the poller records when the frame number
advances, on one keep-alive connection), ui.state (fps, the thumbnail numbers, open bundles), plugin.list diagnostics
(managed bytes after a full collection, object counts; frames while it runs are left out of the frame statistics),
content.stats and app.perf {motion: true} when the build has them (otherwise their columns are NA and the summary lists
them as missing), log.tail errors.

Outputs (<out>, default <project>\\Spike\\out\\session\\<stamp>): samples.tsv, events.tsv, views.tsv (list first views),
frames_by_phase.tsv, summary.txt (first line "SUMMARY: ..."), summary.json, run.log, player.log, userdata\\.
Exit code: 0 completed and no KS check failed, 1 a KS check failed (or the run stopped on an error), 2 setup error or
refused (lock held, game running, no RAM), 3 aborted by the game gate.
"""
import argparse
import ctypes
import ctypes.wintypes as wt
import http.client
import json
import math
import os
import random
import re
import shutil
import socket
import subprocess
import sys
import threading
import time
import traceback
from datetime import datetime, timedelta

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_PORT = 17396
DEFAULT_SEED = 5201
MIB = 1 << 20
CREATE_NO_WINDOW = 0x08000000
VANILLA = "illusion.hs2"
REQUEST_BOUND_S = 300.0
NA = "NA"

# item groups of the base game used until a session item list is given (no adult, character, screen-effect, sound or
# FK groups): basic shapes, base, furniture, ornaments, food, gimmicks, weapons, pets and their "2" groups
DEFAULT_ITEM_GROUPS = [0, 1, 2, 3, 4, 8, 10, 12, 14, 15, 16, 18]
# the five largest maps by lightmap bytes when the map scan cache is not there (2.0, 1.5, 1.3, 1.2 and 0.9 GB)
FALLBACK_LARGEST_MAPS = ["com.nyaacho.map.park#1807", "MFD.tokyosb#0", "hooh.map.apartment#1", "[weir]JapaneseStreet1#0",
                         "MFD.parkinge#0"]

HAIR_PARTS = ["back", "front", "side", "option"]
CLOTHES_KINDS = ["top", "bottom", "innerTop", "innerBottom", "gloves", "pantyhose", "socks", "shoes"]
# the inner layer is never swapped (only what the character visibly wears changes, within its kind)
INNER_KINDS = ("innerTop", "innerBottom")
# (member path under appearance., tree node, page, section)
FACE_BODY = [
    ("chara.face.detail.item", "face", "head", "face.head.detail"),
    ("chara.face.eyebrow.item", "face", "eyebrow", "face.eyebrow.type"),
    ("chara.face.eyes.left.iris.item", "face", "eyes", "face.eyes.iris"),
    ("chara.face.eyes.left.pupil.item", "face", "eyes", "face.eyes.pupil"),
    ("chara.face.eyes.highlight.item", "face", "eyes", "face.eyes.highlight"),
    ("chara.face.eyelashes.item", "face", "eyelashes", "face.eyelashes.type"),
    ("chara.face.makeup.eyeshadow.item", "face", "makeup", "face.makeup.eyeshadow"),
    ("chara.face.makeup.cheek.item", "face", "makeup", "face.makeup.cheek"),
    ("chara.face.makeup.lip.item", "face", "makeup", "face.makeup.lip"),
    ("chara.face.makeup.paint[0].item", "face", "paint", "face.paint.p1"),
    ("chara.face.mole.item", "face", "mole", "face.mole.type"),
    ("chara.face.beard.item", "face", "beard", "face.beard.type"),
    ("chara.body.skin.item", "body", "skin", "body.skin.type"),
    ("chara.body.detail.item", "body", "skin", "body.skin.detail"),
    ("chara.body.sunburn.item", "body", "sunburn", "body.sunburn.type"),
    ("chara.body.paint[0].item", "body", "paint", "body.paint.p1"),
]
ACC_FIRST, ACC_LAST = 351, 363


def acc_section(slot):
    """The Type section of a build slot (0-based): "accessories.accessories.05.type" for slot 4."""
    return "accessories.accessories.%02d.type" % (int(slot) + 1)


NONE_NAME = re.compile(r"(^|\W)(none|nothing|empty|blank|invisible|transparent|nude|naked)(\W|$)|なし|無し|透明|ヌード|全裸", re.I)
EXC_LINE = re.compile(r"^[A-Za-z_][\w.`]*Exception\b")
DEVICE_TEXT = ("device removed", "device_removed", "device was removed", "device lost", "device_hung", "devicehung",
               "887a0005", "887a0006", "887a0007", "887a0020")
UUA_TOTAL = re.compile(r"^Total: ([\d.]+) ms \(FindLiveObjects")

T0 = time.perf_counter()


def now_text():
    return datetime.now().strftime("%Y-%m-%d %H:%M:%S")


def fmt(v, nd=1):
    if v is None:
        return NA
    if isinstance(v, bool):
        return "1" if v else "0"
    if isinstance(v, int):
        return str(v)
    if isinstance(v, float):
        if math.isnan(v) or math.isinf(v):
            return NA
        return ("%%.%df" % nd) % v
    return str(v)


def pct(values, p):
    """Nearest-rank percentile of a list (None for an empty list)."""
    if not values:
        return None
    s = sorted(values)
    k = max(0, min(len(s) - 1, int(math.ceil(p / 100.0 * len(s))) - 1))
    return s[k]


def median(values):
    if not values:
        return None
    s = sorted(values)
    n = len(s)
    return s[n // 2] if n % 2 else 0.5 * (s[n // 2 - 1] + s[n // 2])


def slope_per_min(points):
    """Least-squares slope (value units per minute) of [(minute, value)], None with fewer than 3 points."""
    pts = [(x, y) for x, y in points if x is not None and y is not None]
    if len(pts) < 3:
        return None
    n = len(pts)
    mx = sum(x for x, _ in pts) / n
    my = sum(y for _, y in pts) / n
    sxx = sum((x - mx) ** 2 for x, _ in pts)
    if sxx <= 0:
        return None
    return sum((x - mx) * (y - my) for x, y in pts) / sxx


class SetupError(Exception):
    pass


class Abort(Exception):
    """The run stops: the game gate, the wall bound, the player gone, a device loss or a request past its bound."""


class CmdError(RuntimeError):
    def __init__(self, cmd, reply):
        super().__init__("%s -> %s (%s)" % (cmd, reply.get("error"), reply.get("code")))
        self.reply = reply


# ---------------------------------------------------------------- Windows helpers

class MEMORYSTATUSEX(ctypes.Structure):
    _fields_ = [("dwLength", ctypes.c_ulong), ("dwMemoryLoad", ctypes.c_ulong),
                ("ullTotalPhys", ctypes.c_ulonglong), ("ullAvailPhys", ctypes.c_ulonglong),
                ("ullTotalPageFile", ctypes.c_ulonglong), ("ullAvailPageFile", ctypes.c_ulonglong),
                ("ullTotalVirtual", ctypes.c_ulonglong), ("ullAvailVirtual", ctypes.c_ulonglong),
                ("ullAvailExtendedVirtual", ctypes.c_ulonglong)]


class PROCESS_MEMORY_COUNTERS_EX(ctypes.Structure):
    _fields_ = [("cb", wt.DWORD), ("PageFaultCount", wt.DWORD), ("PeakWorkingSetSize", ctypes.c_size_t),
                ("WorkingSetSize", ctypes.c_size_t), ("QuotaPeakPagedPoolUsage", ctypes.c_size_t),
                ("QuotaPagedPoolUsage", ctypes.c_size_t), ("QuotaPeakNonPagedPoolUsage", ctypes.c_size_t),
                ("QuotaNonPagedPoolUsage", ctypes.c_size_t), ("PagefileUsage", ctypes.c_size_t),
                ("PeakPagefileUsage", ctypes.c_size_t), ("PrivateUsage", ctypes.c_size_t)]


_k32 = ctypes.WinDLL("kernel32", use_last_error=True)
_k32.OpenProcess.restype = wt.HANDLE
_k32.OpenProcess.argtypes = [wt.DWORD, wt.BOOL, wt.DWORD]
_k32.CloseHandle.argtypes = [wt.HANDLE]
_k32.GetExitCodeProcess.argtypes = [wt.HANDLE, ctypes.POINTER(wt.DWORD)]
_k32.K32GetProcessMemoryInfo.argtypes = [wt.HANDLE, ctypes.c_void_p, wt.DWORD]


def free_gb():
    try:
        m = MEMORYSTATUSEX()
        m.dwLength = ctypes.sizeof(m)
        if _k32.GlobalMemoryStatusEx(ctypes.byref(m)):
            return m.ullAvailPhys / float(1 << 30)
    except Exception:
        pass
    return None


def pid_alive(pid):
    """True while the process runs (STILL_ACTIVE), False when it is gone or the PID is not a number."""
    try:
        pid = int(pid)
    except (TypeError, ValueError):
        return False
    h = _k32.OpenProcess(0x1000, False, pid)        # PROCESS_QUERY_LIMITED_INFORMATION
    if not h:
        return ctypes.get_last_error() == 5           # access denied: it exists
    try:
        code = wt.DWORD()
        if _k32.GetExitCodeProcess(h, ctypes.byref(code)):
            return code.value == 259
        return True
    finally:
        _k32.CloseHandle(h)


def process_memory(pid):
    """(working set, private bytes) of a process, or (None, None)."""
    h = _k32.OpenProcess(0x1000 | 0x0010, False, int(pid))
    if not h:
        return None, None
    try:
        c = PROCESS_MEMORY_COUNTERS_EX()
        c.cb = ctypes.sizeof(c)
        if _k32.K32GetProcessMemoryInfo(h, ctypes.byref(c), c.cb):
            return int(c.WorkingSetSize), int(c.PrivateUsage)
        return None, None
    finally:
        _k32.CloseHandle(h)


def running_processes(prefixes):
    """Image names of running processes that start with one of the prefixes (case ignored)."""
    p = subprocess.run(["tasklist", "/FO", "CSV", "/NH"], capture_output=True, timeout=60, creationflags=CREATE_NO_WINDOW)
    found = set()
    for line in p.stdout.decode("oem", "replace").splitlines():
        name = line.split(",")[0].strip('"').lower()
        for g in prefixes:
            if g and name.startswith(g.lower()):
                found.add(name)
    return sorted(found)


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


def read_game_gate(path):
    """(process name prefixes, poll seconds <= 30, warning or None) from the game-gate file (a local file that names the
    processes of the machine's owner; never shipped). A missing or unreadable file means no game gate, with a warning."""
    try:
        with open(path, encoding="utf-8") as f:
            g = json.load(f)
        names = [str(x) for x in g.get("processes") or [] if str(x).strip()]
        poll = min(float(g.get("pollSec") or 10.0), 30.0)
        return names, poll, (None if names else "%s names no process: no game gate" % os.path.basename(path))
    except (OSError, ValueError, TypeError, AttributeError) as e:
        return [], 10.0, "%s not read (%s): no game gate" % (os.path.basename(path), type(e).__name__)


def player_build_time(exe):
    """The newest modification time of the player's managed assemblies (the exe itself is the engine's stub)."""
    d = os.path.join(os.path.dirname(exe), os.path.splitext(os.path.basename(exe))[0] + "_Data", "Managed")
    try:
        t = max(os.path.getmtime(os.path.join(d, f)) for f in os.listdir(d) if f.lower().endswith(".dll"))
        return datetime.fromtimestamp(t).strftime("%Y-%m-%d %H:%M:%S")
    except (OSError, ValueError):
        try:
            return datetime.fromtimestamp(os.path.getmtime(exe)).strftime("%Y-%m-%d %H:%M:%S")
        except OSError:
            return None


def powershell(script, timeout=90):
    exe = shutil.which("powershell") or "powershell.exe"
    p = subprocess.run([exe, "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script],
                       capture_output=True, timeout=timeout, creationflags=CREATE_NO_WINDOW)
    return p.returncode, p.stdout.decode("utf-8", "replace"), p.stderr.decode("utf-8", "replace")


LUID = re.compile(r"luid_0x[0-9a-f]+_0x[0-9a-f]+_phys_\d+", re.I)


def gpu_counters(pid):
    """One Get-Counter: the process's dedicated / shared GPU memory per adapter and every adapter's totals (bytes).
    Returns {procDed, procShr, adapters {luid: {ded, shr}}, procLuids [..], ms} or {error}."""
    paths = []
    if pid:
        paths += ["\\GPU Process Memory(pid_%d_*)\\Dedicated Usage" % pid, "\\GPU Process Memory(pid_%d_*)\\Shared Usage" % pid]
    paths += ["\\GPU Adapter Memory(*)\\Dedicated Usage", "\\GPU Adapter Memory(*)\\Shared Usage"]
    script = ("$ErrorActionPreference='SilentlyContinue'; $s = (Get-Counter -Counter @(%s)).CounterSamples; "
              "foreach ($x in $s) { \"{0}`t{1}\" -f $x.Path, [long]$x.CookedValue }") % ",".join("'%s'" % p for p in paths)
    t = time.perf_counter()
    try:
        code, out, err = powershell(script, timeout=90)
    except subprocess.TimeoutExpired:
        return {"error": "Get-Counter timed out"}
    res = {"procDed": None, "procShr": None, "adapters": {}, "procLuids": [], "ms": (time.perf_counter() - t) * 1000}
    proc = {}
    for line in out.splitlines():
        if "\t" not in line:
            continue
        path, _, val = line.rpartition("\t")
        try:
            v = int(val.strip())
        except ValueError:
            continue
        pl = path.lower()
        m = LUID.search(pl)
        luid = m.group(0) if m else "?"
        kind = "ded" if pl.endswith("dedicated usage") else "shr" if pl.endswith("shared usage") else None
        if kind is None:
            continue
        if "gpu process memory" in pl:
            proc.setdefault(luid, {"ded": 0, "shr": 0})[kind] += v
        elif "gpu adapter memory" in pl:
            res["adapters"].setdefault(luid, {"ded": 0, "shr": 0})[kind] += v
    if pid:
        if proc:
            res["procDed"] = sum(x["ded"] for x in proc.values())
            res["procShr"] = sum(x["shr"] for x in proc.values())
            res["procLuids"] = sorted((k for k, x in proc.items() if x["ded"] > 0), key=lambda k: -proc[k]["ded"])
        res["proc"] = proc
    if not res["adapters"] and not proc:
        res["error"] = "no counter samples (%s)" % (err.strip()[:200] or "exit %s" % code)
    return res


def device_events():
    """System log counts: nvlddmkm events (all, id 153) and Display 4101 (driver reset), the newest nvlddmkm time."""
    script = ("$n=@(Get-WinEvent -FilterHashtable @{LogName='System'; ProviderName='nvlddmkm'} -ErrorAction SilentlyContinue); "
              "$d=@(Get-WinEvent -FilterHashtable @{LogName='System'; ProviderName='Display'; Id=4101} -ErrorAction SilentlyContinue); "
              "'{0} {1} {2} {3}' -f $n.Count, @($n | Where-Object Id -eq 153).Count, $d.Count, "
              "$(if ($n.Count) { $n[0].TimeCreated.ToString('s') } else { '-' })")
    try:
        code, out, err = powershell(script, timeout=120)
        t = out.strip().split()
        return {"nvlddmkm": int(t[0]), "id153": int(t[1]), "display4101": int(t[2]), "newest": t[3] if len(t) > 3 else "-"}
    except Exception as e:
        return {"error": "%s: %s" % (type(e).__name__, e)}


# ---------------------------------------------------------------- HTTP

class _Conn(http.client.HTTPConnection):
    """Connects blocking, then applies the timeout (a timed connect costs ~14 ms on Windows loopback)."""

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
        try:
            s.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
        except OSError:
            pass


def http_post(port, req, timeout):
    body = json.dumps(req, ensure_ascii=False).encode("utf-8")
    conn = _Conn("127.0.0.1", port, timeout=timeout)
    try:
        conn.putrequest("POST", "/", skip_accept_encoding=True)
        conn.putheader("Content-Type", "application/json; charset=utf-8")
        conn.putheader("Content-Length", str(len(body)))
        conn.putheader("Connection", "close")
        conn.endheaders(body)
        resp = conn.getresponse()
        data = resp.read()
        if resp.status != 200:
            raise RuntimeError("HTTP %s: %s" % (resp.status, data[:200].decode("utf-8", "replace")))
        return json.loads(data.decode("utf-8"))
    finally:
        conn.close()


# ---------------------------------------------------------------- the frame poller

class FramePoller(threading.Thread):
    """Sends ping alone (answered off the main thread) on one keep-alive connection every interval and records when the
    frame number advances: per-frame durations (ms) tagged with the driver's phase at the time ('diag' while a full
    collection runs, 'gap' after a poll error)."""

    FRAME = re.compile(rb'"frame"\s*:\s*(\d+)')

    def __init__(self, sess, interval_ms):
        super().__init__(name="frame-poller", daemon=True)
        self.s = sess
        self.interval = max(0.0, interval_ms / 1000.0)
        self.stop_ev = threading.Event()
        self.records = []          # (t perf_counter, duration ms per frame, frames, phase)
        self.polls = 0
        self.errors = 0
        self.lock = threading.Lock()

    def run(self):
        body = b'{"cmd":"ping"}'
        conn = None
        last_frame, last_t = None, None
        while not self.stop_ev.is_set():
            try:
                if conn is None:
                    conn = _Conn("127.0.0.1", self.s.port, timeout=10)
                conn.putrequest("POST", "/", skip_accept_encoding=True)
                conn.putheader("Content-Type", "application/json")
                conn.putheader("Content-Length", str(len(body)))
                conn.endheaders(body)
                resp = conn.getresponse()
                data = resp.read()
                t = time.perf_counter()
                self.polls += 1
                if resp.getheader("Connection", "").lower() == "close":
                    conn.close()
                    conn = None
                m = self.FRAME.search(data)
                if m:
                    f = int(m.group(1))
                    if last_frame is not None and f > last_frame:
                        n = f - last_frame
                        phase = "diag" if self.s.diag_inflight > 0 or t <= self.s.diag_until else self.s.phase
                        with self.lock:
                            self.records.append((t, (t - last_t) * 1000.0 / n, n, phase))
                    if last_frame is None or f != last_frame:
                        last_frame, last_t = f, t
            except Exception:
                self.errors += 1
                if conn is not None:
                    try:
                        conn.close()
                    except Exception:
                        pass
                conn = None
                last_frame, last_t = None, None
                if self.stop_ev.wait(0.05):
                    break
                continue
            if self.interval > 0:
                time.sleep(self.interval)
        if conn is not None:
            try:
                conn.close()
            except Exception:
                pass

    def take(self, start_index):
        with self.lock:
            return self.records[start_index:], len(self.records)


def frame_stats(recs, skip=("diag", "setup", "quit")):
    """{frames, medianMs, p95Ms, p99Ms, maxMs, over50, meanMs} of poller records (each frame of a multi-frame record counted)."""
    d = []
    for _, ms, n, ph in recs:
        if ph in skip:
            continue
        d.extend([ms] * n)
    if not d:
        return {"frames": 0, "medianMs": None, "p95Ms": None, "p99Ms": None, "maxMs": None, "over50": 0, "meanMs": None}
    return {"frames": len(d), "medianMs": median(d), "p95Ms": pct(d, 95), "p99Ms": pct(d, 99), "maxMs": max(d),
            "over50": sum(1 for x in d if x > 50), "meanMs": sum(d) / len(d)}


# ---------------------------------------------------------------- locks

LOCK_PID = re.compile(r"PID\D*?(\d+)")
LOCK_TIME = r"([0-9]{4}-[0-9]{2}-[0-9]{2}[ T][0-9]{2}[:][0-9]{2}(?:[:][0-9]{2})?)"
LOCK_START = re.compile(r"start " + LOCK_TIME)
LOCK_END = re.compile(r"expected end " + LOCK_TIME)


def _parse_time(text):
    for f in ("%Y-%m-%d %H:%M:%S", "%Y-%m-%dT%H:%M:%S", "%Y-%m-%d %H:%M", "%Y-%m-%dT%H:%M"):
        try:
            return datetime.strptime(text, f)
        except ValueError:
            pass
    return None


class Locks:
    """The GPU lock and the input lock of the shared lock folder (created exclusively, removed at the end)."""

    def __init__(self, folder, who, log):
        self.folder = folder
        self.who = who
        self.log = log
        self.gpu = os.path.join(folder, "phase52_gpu.lock")
        self.input = os.path.join(folder, "phase52_input.lock")
        self.held = []
        self.notes = []

    @staticmethod
    def read(path):
        try:
            with open(path, encoding="utf-8", errors="replace") as f:
                return f.read().strip()
        except OSError:
            return None

    def stale(self, path, text):
        """(stale, why): the input lock is stale when older than 90 min with its PID gone; the GPU lock when past its expected
        end + 30 min with its PID gone. A line that cannot be read is never stale."""
        pm = LOCK_PID.search(text or "")
        pid = int(pm.group(1)) if pm else None
        if pid is None or pid_alive(pid):
            return False, "PID %s alive or unknown" % pid
        if path == self.input:
            sm = LOCK_START.search(text)
            st = _parse_time(sm.group(1)) if sm else None
            if st is None:
                try:
                    st = datetime.fromtimestamp(os.path.getmtime(path))
                except OSError:
                    return False, "no start time"
            if datetime.now() - st > timedelta(minutes=90):
                return True, "input lock started %s (> 90 min), PID %d gone" % (st, pid)
            return False, "input lock younger than 90 min"
        em = LOCK_END.search(text)
        end = _parse_time(em.group(1)) if em else None
        if end is None:
            return False, "no expected end"
        if datetime.now() > end + timedelta(minutes=30):
            return True, "GPU lock expected end %s (+ 30 min passed), PID %d gone" % (end, pid)
        return False, "GPU lock within its expected end + 30 min"

    def busy(self):
        """None when both locks are absent (stale ones removed), else a text naming the holder."""
        for path in (self.gpu, self.input):
            if not os.path.exists(path):
                continue
            text = self.read(path) or ""
            is_stale, why = self.stale(path, text)
            if is_stale:
                try:
                    os.remove(path)
                    note = "removed a stale lock %s: %s (it read: %s)" % (os.path.basename(path), why, text[:200])
                    self.notes.append(note)
                    self.log(note)
                    continue
                except OSError as e:
                    return "%s could not be removed: %s" % (os.path.basename(path), e)
            return "%s held: %s" % (os.path.basename(path), text[:200])
        return None

    def line(self, expected_end, player_pid=None):
        return "%s PID %d%s start %s expected end %s" % (self.who, os.getpid(), (" player %d" % player_pid) if player_pid else "",
                                                       self.started, expected_end.strftime("%Y-%m-%d %H:%M:%S"))

    def acquire(self, expected_end):
        os.makedirs(self.folder, exist_ok=True)
        self.started = now_text()
        self.expected_end = expected_end
        text = self.line(expected_end)
        for path in (self.gpu, self.input):
            try:
                fd = os.open(path, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
            except FileExistsError:
                held = self.read(path)
                self.release()
                raise SetupError("%s appeared while taking the locks: %s" % (os.path.basename(path), held))
            try:
                os.write(fd, (text + "\n").encode("utf-8"))
            finally:
                os.close(fd)
            self.held.append(path)
        self.log("locks taken: " + text)

    def note_player(self, pid):
        text = self.line(self.expected_end, pid)
        for path in self.held:
            try:
                with open(path, "w", encoding="utf-8") as f:
                    f.write(text + "\n")
            except OSError:
                pass

    def release(self):
        for path in list(self.held):
            try:
                os.remove(path)
            except OSError as e:
                self.log("could not remove %s: %s" % (path, e))
            self.held.remove(path)


# ---------------------------------------------------------------- the session

class Session:
    def __init__(self, a):
        self.a = a
        self.port = a.port
        self.project = os.path.abspath(a.project) if a.project else os.path.dirname(HERE)
        self.exe = os.path.abspath(a.exe) if a.exe else os.path.join(self.project, "Builds", "App", "HS2Next.exe")
        stamp = datetime.now().strftime("%Y%m%d-%H%M%S")
        self.stamp = stamp
        self.out = os.path.abspath(a.out) if a.out else os.path.join(self.project, "Spike", "out", "session", stamp)
        self.rng = random.Random(a.seed)
        self.phase = "setup"
        self.diag_inflight = 0
        self.diag_lock = threading.Lock()
        self.diag_until = 0.0
        self.cycle = 0
        self.proc = None
        self.pid = None
        self.abort_reason = None
        self.gate_abort = False
        self.t_player = None          # perf_counter at the player's start
        self.t_work = None            # perf_counter at the workload's start
        self.samples = []
        self.events = []
        self.views = []
        self.view_counts = {}
        self.list_sizes = {}
        self.errors = []
        self.info = {"stamp": stamp, "seed": a.seed, "minutes": a.minutes, "port": a.port}
        self.missing = {}
        self.has_cmd = {}
        self.log_since = 0
        self.log_exceptions = 0
        self.log_device = 0
        self.log_device_lines = []
        self.ks6 = []
        self.cycles_done = 0
        self.poller = None
        self.sampler = None
        self.locks = None
        self.lock = threading.Lock()
        self.status = "not started"
        self.gate_warning = None
        self.memory_stop = None
        self.scroll_moved = {}
        self.searched = None
        self.click_note = ""
        os.makedirs(self.out, exist_ok=True)
        self.logf = open(os.path.join(self.out, "run.log"), "a", encoding="utf-8")

    # ------------------------------------------------------------ logging

    def log(self, msg):
        line = "[%s %7.1f s] %s" % (datetime.now().strftime("%H:%M:%S"), time.perf_counter() - T0, msg)
        with self.lock:
            print(line, flush=True)
            try:
                self.logf.write(line + "\n")
                self.logf.flush()
            except Exception:
                pass

    def t_session(self):
        return None if self.t_work is None else time.perf_counter() - self.t_work

    def event(self, op, what="", ref="", ok=True, rt_ms=None, sync_ms=None, note=""):
        ts = self.t_session()
        e = {"t": ts, "cycle": self.cycle, "phase": self.phase, "op": op, "what": what, "ref": ref, "ok": ok,
             "rtMs": rt_ms, "syncMs": sync_ms, "note": note}
        self.events.append(e)
        if not ok:
            self.log("event %s %s %s failed: %s" % (op, what, ref, note))

    # ------------------------------------------------------------ bridge

    def check_abort(self):
        if self.abort_reason:
            raise Abort(self.abort_reason)

    def alive(self):
        if self.a.attach:
            return True
        return self.proc is not None and self.proc.poll() is None

    def call(self, req, timeout=120, check=True):
        """One bridge request on a fresh connection, bounded by min(timeout, 5 min); Abort when the run is stopping or the
        player is gone."""
        self.check_abort()
        t = min(float(timeout), REQUEST_BOUND_S)
        try:
            r = http_post(self.port, req, t)
        except socket.timeout:
            self.abort_reason = self.abort_reason or "a bridge request (%s) passed its bound of %.0f s" % (req.get("cmd"), t)
            raise Abort(self.abort_reason)
        except (OSError, http.client.HTTPException, ValueError, RuntimeError) as e:
            for _ in range(50):            # a crashing player resets the connection before its process ends
                if not self.alive():
                    break
                time.sleep(0.1)
            if not self.alive():
                self.abort_reason = self.abort_reason or "the player exited (code %s)" % (self.proc.poll() if self.proc else "?")
                raise Abort(self.abort_reason)
            self.check_abort()
            raise CmdError(req.get("cmd", "?"), {"error": "%s: %s" % (type(e).__name__, e), "code": "transport"})
        if check and not r.get("ok"):
            raise CmdError(req.get("cmd", "?"), r)
        return r

    def try_call(self, req, timeout=120):
        """The reply or None (ok false or a transport error); Abort still propagates."""
        try:
            return self.call(req, timeout=timeout, check=True)
        except CmdError:
            return None

    def timed(self, req, timeout=300, settle=True):
        """Runs req inside a batch [ping, req, (camera.get,) ping]: (ok, member reply, sync ms, round-trip ms, error text).
        sync ms = the main thread's time from before the command to after the settle (the realtime difference)."""
        cmds = [{"cmd": "ping"}, req] + ([{"cmd": "camera.get"}] if settle else []) + [{"cmd": "ping"}]
        t = time.perf_counter()
        r = self.call({"cmd": "batch", "cmds": cmds, "stopOnError": True}, timeout=timeout, check=False)
        rt = (time.perf_counter() - t) * 1000.0
        res = r.get("results") or []
        member = res[1] if len(res) > 1 else None
        if r.get("ok") and len(res) == len(cmds):
            try:
                sync = (float(res[-1]["realtime"]) - float(res[0]["realtime"])) * 1000.0
            except (KeyError, TypeError, ValueError):
                sync = None
            return True, member, sync, rt, ""
        err = r.get("error") or (member or {}).get("error") or "failed"
        return False, member, None, rt, "%s (%s)" % (str(err)[:200], r.get("code") or (member or {}).get("code"))

    def supports(self, name):
        """Whether the build has a command (help {name}); cached."""
        if name not in self.has_cmd:
            r = None
            try:
                r = self.call({"cmd": "help", "name": name}, timeout=30, check=False)
            except CmdError:
                r = None
            self.has_cmd[name] = bool(r and r.get("ok"))
        return self.has_cmd[name]

    # ------------------------------------------------------------ configuration

    def load_config(self):
        a = self.a
        self.gate_names, self.gate_poll, warn = read_game_gate(os.path.join(self.project, "Tools", "session_gate.json"))
        if warn:
            self.gate_warning = warn
            self.log("WARN " + warn)
        with open(os.path.join(self.project, "Tools", "targets.json"), encoding="utf-8") as f:
            t = json.load(f)
        vis = t.get("visual") or {}
        self.cards = {"female": vis.get("female", "").replace("chara/female/", "", 1),
                      "male": vis.get("male", "").replace("chara/male/", "", 1)}
        if not self.cards["female"] or not self.cards["male"]:
            raise SetupError("Tools/targets.json has no visual female / male target")
        self.crash_refs = set()
        cf = os.path.join(self.project, "Cache", "items", "corpus_crashes.txt")
        if os.path.isfile(cf):
            with open(cf, encoding="utf-8", errors="replace") as f:
                for line in f:
                    ref = line.split("|")[0].strip()
                    if ref and not ref.startswith("#"):
                        self.crash_refs.add(ref)
        self.info["crashRowsLeftOut"] = len(self.crash_refs)
        # items: an explicit file, the session list when present, else the built-in vanilla groups
        items_file = a.items or os.path.join(self.project, "Tools", "session_items.json")
        self.item_groups, self.item_categories, self.items_source = list(DEFAULT_ITEM_GROUPS), None, "built-in vanilla groups"
        if a.items or os.path.isfile(items_file):
            try:
                self.item_groups, self.item_categories = self.read_items(items_file)
                self.items_source = os.path.relpath(items_file, self.project) if items_file.startswith(self.project) else items_file
            except (OSError, ValueError) as e:
                if a.items:
                    raise SetupError("--items %s not read: %s" % (items_file, e))
                self.log("session items file not usable (%s): the built-in vanilla groups" % e)
        self.info["items"] = {"source": self.items_source, "groups": self.item_groups,
                              "categories": sorted(self.item_categories) if self.item_categories else None}
        self.largest_maps, self.loadable_maps, self.map_source = self.map_candidates()

    @staticmethod
    def read_items(path):
        """(groups, {(group, category)} or None) from a list of {group, category?/categories?} objects (top level, or the
        member groups / items / rows)."""
        with open(path, encoding="utf-8") as f:
            d = json.load(f)
        rows = d if isinstance(d, list) else (d.get("groups") or d.get("items") or d.get("rows") or [])
        groups, cats = [], set()
        for r in rows:
            if isinstance(r, int):
                groups.append(r)
                continue
            if not isinstance(r, dict) or "group" not in r:
                continue
            if r.get("use") is False or r.get("include") is False:
                continue
            g = int(r["group"])
            if g not in groups:
                groups.append(g)
            c = r.get("categories") if r.get("categories") is not None else ([r["category"]] if r.get("category") is not None else None)
            if c:
                for x in c:
                    cats.add((g, int(x)))
        if not groups:
            raise ValueError("no group in the file")
        return groups, (cats or None)

    def map_candidates(self):
        """(the five largest map refs by lightmap bytes, the loadable refs from the last map corpus or None, source text)."""
        cache = os.path.join(self.project, "Cache", "maps")
        largest = []
        try:
            sizes = []
            for fn in ("maps_mods.jsonl", "maps_vanilla.jsonl"):
                p = os.path.join(cache, fn)
                if not os.path.isfile(p):
                    continue
                with open(p, encoding="utf-8") as f:
                    for line in f:
                        try:
                            d = json.loads(line)
                        except ValueError:
                            continue
                        scenes = d.get("scenes") or {}
                        per = {}
                        for sn, sc in scenes.items():
                            tot = 0
                            for lm in ((sc or {}).get("lightmapSettings") or {}).get("lightmaps") or []:
                                for k in ("color", "dir", "mask"):
                                    ti = lm.get(k)
                                    if isinstance(ti, dict):
                                        tot += int(ti.get("bytes") or 0)
                            per[sn] = tot
                        b = max(per.values()) if len(per) == 1 else None
                        for r in d.get("rows") or []:
                            if b is not None and r.get("guid") is not None and r.get("no") is not None:
                                sizes.append((b, "%s#%s" % (r["guid"], r["no"])))
            sizes.sort(key=lambda x: -x[0])
            seen = []
            for b, ref in sizes:
                if ref not in seen:
                    seen.append(ref)
                if len(seen) == 5:
                    break
            largest = seen
            self.info["largestMapsMB"] = [round(b / MIB) for b, _ in sizes[:5]]
        except Exception as e:
            self.log("map scan cache not read (%s)" % e)
        source = "map scan cache"
        if len(largest) < 5:
            largest, source = list(FALLBACK_LARGEST_MAPS), "built-in list"
        loadable = None
        try:
            files = sorted(f for f in os.listdir(cache) if re.match(r"corpus_\d{8}_\d+\.tsv$", f))
            if files:
                last_day = files[-1].split("_")[1]
                loadable = set()
                for fn in files:
                    if fn.split("_")[1] != last_day:
                        continue
                    with open(os.path.join(cache, fn), encoding="utf-8", errors="replace") as f:
                        head = None
                        for line in f:
                            t = line.rstrip("\n").split("\t")
                            if head is None:
                                head = t
                                continue
                            row = dict(zip(head, t))
                            ref = row.get("ref", "")
                            if len(ref) > 1 and ref.startswith('"'):
                                ref = re.sub(r'^"(.*)"(#\d+)$', lambda m: m.group(1).replace('""', '"') + m.group(2), ref)
                            if "loadFailed" in row.get("findings", "") or row.get("state", "") == "failed":
                                continue
                            loadable.add(ref)
                self.info["mapCorpus"] = "corpus %s (%d loadable)" % (last_day, len(loadable))
        except Exception as e:
            self.log("map corpus not read (%s)" % e)
            loadable = None
        return largest, loadable, source

    # ------------------------------------------------------------ setup: gates, locks, player

    def preflight(self):
        a = self.a
        games = running_processes(self.gate_names)
        if games:
            raise SetupError("the game gate: %s runs; no player run now" % ", ".join(games))
        if a.attach:
            return
        if not os.path.isfile(self.exe):
            raise SetupError("no player: %s" % self.exe)
        self.settings = os.path.join(self.project, "Config", "settings.local.json")
        if not os.path.isfile(self.settings):
            raise SetupError("settings file not found: %s" % self.settings)
        self.cache = os.path.join(self.project, "Cache")
        try:
            with open(self.settings, encoding="utf-8-sig") as f:
                cd = json.load(f).get("cacheDir")
            if cd:
                self.cache = os.path.abspath(os.path.join(self.project, cd))
        except (OSError, ValueError):
            pass
        if port_listeners(self.port):
            raise SetupError("port %d is taken (PIDs %s)" % (self.port, sorted(port_listeners(self.port))))
        self.info["exeModified"] = player_build_time(self.exe)
        # RAM
        deadline = time.time() + a.ram_wait_min * 60
        while True:
            fg = free_gb()
            if fg is None or fg >= a.min_free_gb:
                self.info["freeGbAtStart"] = round(fg, 1) if fg is not None else None
                break
            if time.time() > deadline:
                raise SetupError("free RAM %.1f GB < %.1f GB for %.0f min" % (fg, a.min_free_gb, a.ram_wait_min))
            self.log("free RAM %.1f GB < %.1f GB: waiting" % (fg, a.min_free_gb))
            time.sleep(20)
        # locks (bounded wait, the game gate checked while waiting)
        if not a.no_locks:
            self.locks = Locks(self.lock_dir(), "K (%s)" % (a.label or "MakerSession %g min" % a.minutes), self.log)
            deadline = time.time() + a.lock_wait_min * 60
            while True:
                held = self.locks.busy()
                if held is None:
                    break
                if time.time() > deadline:
                    raise SetupError("lock: " + held)
                self.log("waiting for the locks: " + held)
                time.sleep(30)
                games = running_processes(self.gate_names)
                if games:
                    raise SetupError("the game gate: %s runs; no player run now" % ", ".join(games))
            self.locks.acquire(datetime.now() + timedelta(minutes=a.wall_min))
            self.info["lockNotes"] = self.locks.notes
        else:
            self.log("--no-locks: no GPU / input lock (diagnosis only)")

    def lock_dir(self):
        if self.a.lock_dir:
            return os.path.abspath(self.a.lock_dir)
        env = os.environ.get("HS2NEXT_LOCK_DIR")
        if env:
            return os.path.abspath(env)
        return os.path.join(os.environ.get("SystemDrive", "C:") + os.sep, "tmp", "hs2next_tests")

    def start_player(self):
        a = self.a
        if a.attach:
            r = http_post(self.port, {"cmd": "ping"}, 10)
            if not r.get("appReady"):
                raise SetupError("attach: the bridge on port %d is not ready" % self.port)
            self.t_player = time.perf_counter()
            self.info["readySeconds"] = 0.0
            return
        ud = os.path.join(self.out, "userdata")
        bo = os.path.join(self.out, "bridge_out")
        self.player_log = os.path.join(self.out, "player.log")
        ep = os.path.join(ud, "Bridge", "endpoint.json")
        if os.path.isfile(ep):
            os.remove(ep)
        env = dict(os.environ)
        env["HS2NEXT_NO_PLUGINS"] = "1"
        env["HS2NEXT_PRIVATE_OFF"] = "1"
        args = [self.exe, "-batchmode", "-ui" if getattr(self.a, "ui", True) else "-noUi", "-bridgePort", str(self.port),
                "-bridgeAllowQuit", "-settings", self.settings, "-cacheDir", self.cache, "-userData", ud, "-bridgeOutputDir", bo,
                "-logFile", self.player_log] + list(getattr(self.a, "player_args", None) or [])
        self.info["commandLine"] = subprocess.list2cmdline(args)
        self.log("starting the player: " + self.info["commandLine"])
        t0 = time.perf_counter()
        self.proc = subprocess.Popen(args, cwd=os.path.dirname(self.exe), env=env, stdin=subprocess.DEVNULL,
                                     stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, creationflags=CREATE_NO_WINDOW)
        self.pid = self.proc.pid
        self.t_player = t0
        self.info["playerPid"] = self.pid
        if self.locks:
            self.locks.note_player(self.pid)
        deadline = time.time() + a.ready_timeout_min * 60
        last, last_err = None, None
        while True:
            self.check_abort()
            if self.proc.poll() is not None:
                raise SetupError("the player exited with code %s before it was ready (see %s)" % (self.proc.returncode, self.player_log))
            try:
                r = http_post(self.port, {"cmd": "ping"}, 5)
                last = r
                if r.get("appReady"):
                    break
            except Exception as e:
                last_err = "%s: %s" % (type(e).__name__, e)
            if time.time() > deadline:
                raise SetupError("not ready after %.0f s; last ping %s; last error %s" % (time.perf_counter() - t0, json.dumps(last)[:300] if last else None, last_err))
            time.sleep(0.5)
        self.info["readySeconds"] = round(time.perf_counter() - t0, 1)
        pid_seen = None
        for _ in range(20):
            try:
                with open(ep, encoding="utf-8") as f:
                    pid_seen = json.load(f).get("pid")
                break
            except (OSError, ValueError):
                time.sleep(0.25)
        if pid_seen != self.pid:
            raise SetupError("endpoint.json names PID %s, the started player is PID %s" % (pid_seen, self.pid))
        self.log("ready in %.1f s (pid %d, port %d)" % (self.info["readySeconds"], self.pid, self.port))

    def stop_player(self, why):
        """app.quit, then a forced stop by PID after 20 s. Returns (exit code, how)."""
        if self.a.attach or self.proc is None:
            return None, "attached"
        if self.proc.poll() is not None:
            return self.proc.returncode, "already exited"
        how = "app.quit"
        try:
            http_post(self.port, {"cmd": "app.quit", "code": 0}, 30)
        except Exception as e:
            how = "app.quit failed (%s: %s)" % (type(e).__name__, e)
        try:
            code = self.proc.wait(20)
        except subprocess.TimeoutExpired:
            self.proc.kill()
            try:
                code = self.proc.wait(30)
            except subprocess.TimeoutExpired:
                code = None
            how += "; killed by PID %d after 20 s" % self.pid
        self.log("player stopped (%s): %s, exit %s" % (why, how, code))
        return code, how

    # ------------------------------------------------------------ the watcher: game gate, wall bound, player

    def watcher(self):
        deadline = T0 + self.a.wall_min * 60
        next_gate = 0.0
        while not self.watch_stop.is_set():
            now = time.perf_counter()
            if now >= next_gate:
                next_gate = now + self.gate_poll
                try:
                    games = running_processes(self.gate_names)
                except Exception:
                    games = []
                if games and not self.abort_reason:
                    self.gate_abort = True
                    self.abort_reason = "aborted by the game gate (%s appeared)" % ", ".join(games)
                    self.log(self.abort_reason)
                    self.stop_player("game gate")
                    return
            if now >= deadline and not self.abort_reason:
                self.abort_reason = "the wall bound of %.0f min passed" % self.a.wall_min
                self.log(self.abort_reason)
                self.stop_player("wall bound")
                return
            if self.proc is not None and self.proc.poll() is not None and not self.abort_reason and not self.quitting:
                self.abort_reason = "the player exited with code %s" % self.proc.returncode
                self.log(self.abort_reason)
                return
            self.watch_stop.wait(1.0)

    # ------------------------------------------------------------ sampling

    def sample(self, final=False):
        """One sample row (dict). OS counters first (their time is the sample's time), then the bridge numbers."""
        t = time.perf_counter()
        row = {"t_s": t - self.t_player if self.t_player else None, "session_min": None, "wall": now_text(),
               "cycle": self.cycle, "phase": self.phase}
        ts = self.t_session()
        row["session_min"] = ts / 60.0 if ts is not None else None
        pid = self.pid
        g = gpu_counters(pid)
        row["gpuDedMB"] = g["procDed"] / MIB if g.get("procDed") is not None else None
        row["gpuShrMB"] = g["procShr"] / MIB if g.get("procShr") is not None else None
        luids = g.get("procLuids") or []
        ad = g.get("adapters") or {}
        if luids:
            row["adpDedMB"] = sum(ad.get(l, {}).get("ded", 0) for l in luids) / MIB
            row["adpShrMB"] = sum(ad.get(l, {}).get("shr", 0) for l in luids) / MIB
            self.info.setdefault("gpuLuids", luids)
        else:
            row["adpDedMB"] = max((x["ded"] for x in ad.values()), default=0) / MIB if ad else None
            row["adpShrMB"] = None
        proc = g.get("proc") or {}
        row["gpuByLuid"] = ",".join("%s:%d/%d" % (k[-14:], round(v["ded"] / MIB), round(v["shr"] / MIB)) for k, v in sorted(proc.items())) or NA
        row["counterMs"] = g.get("ms")
        if g.get("error"):
            row["counterError"] = g["error"]
        if pid:
            ws, pb = process_memory(pid)
            row["wsMB"] = ws / MIB if ws is not None else None
            row["privMB"] = pb / MIB if pb is not None else None
        # frame numbers: ping (off the main thread) and the poller's window
        try:
            p = http_post(self.port, {"cmd": "ping"}, 10)
            row["frame"] = p.get("frame")
            row["realtime"] = p.get("realtime")
        except Exception:
            row["frame"] = None
            row["realtime"] = None
        prev = self.samples[-1] if self.samples else None
        if prev and prev.get("frame") is not None and row.get("frame") is not None and prev.get("realtime") is not None and row.get("realtime") is not None:
            dt = float(row["realtime"]) - float(prev["realtime"])
            df = int(row["frame"]) - int(prev["frame"])
            row["fpsMean"] = df / dt if dt > 0 else None
        if self.poller is not None:
            recs, idx = self.poller.take(self.poll_index)
            self.poll_index = idx
            fs = frame_stats(recs)
            row.update({"frames": fs["frames"], "frameMedMs": fs["medianMs"], "frameP95Ms": fs["p95Ms"],
                        "frameMaxMs": fs["maxMs"], "framesOver50": fs["over50"]})
        if self.alive():
            self.sample_bridge(row)
        row["logExceptions"] = self.log_exceptions
        row["logDevice"] = self.log_device
        row["sampleMs"] = (time.perf_counter() - t) * 1000.0
        self.memory_guard(row)
        self.samples.append(row)
        self.write_sample(row)
        return row

    def memory_guard(self, row):
        """Ends the run before the card runs out of memory: the process's dedicated GPU memory at or above --gpu-guard-mb,
        or its shared GPU memory at or above --shared-guard-mb (a run past the acceptance limits that could hang the device)."""
        ded, shr = row.get("gpuDedMB"), row.get("gpuShrMB")
        why = None
        if ded is not None and self.a.gpu_guard_mb > 0 and ded >= self.a.gpu_guard_mb:
            why = "dedicated GPU memory %.0f MB >= %.0f MB" % (ded, self.a.gpu_guard_mb)
        elif shr is not None and self.a.shared_guard_mb > 0 and shr >= self.a.shared_guard_mb:
            why = "shared GPU memory %.0f MB >= %.0f MB" % (shr, self.a.shared_guard_mb)
        if why and not self.abort_reason:
            self.memory_stop = why
            self.abort_reason = "stopped by the memory guard: " + why
            self.log(self.abort_reason)

    def sample_bridge(self, row):
        try:
            u = self.call({"cmd": "ui.state"}, timeout=300, check=False)
            if u.get("ok"):
                row["uiFps"] = u.get("fps")
                th = u.get("thumbs") or {}
                if th:
                    row["openBundles"] = th.get("openBundles")
                    row["thumbOnlyBundles"] = th.get("thumbnailOnlyBundles")
                    row["thumbsShown"] = th.get("shown")
                    row["thumbsPending"] = th.get("pending")
                    row["thumbsFromBundles"] = th.get("fromBundles")
                    row["thumbsFailed"] = th.get("failed")
            pl = self.diagnostics()
            d = (pl or {}).get("diagnostics") or {}
            if d:
                row["managedMB"] = d.get("managedBytes") / MIB if d.get("managedBytes") is not None else None
                for k in ("gameObjects", "materials", "meshes", "textures", "renderTextures"):
                    row[k] = d.get(k)
            if self.supports("content.stats"):
                cs = self.call({"cmd": "content.stats"}, timeout=300, check=False)
                if cs.get("ok"):
                    for k in ("open", "owned", "lru", "pinned", "thumbnailOnly", "unscoped", "pendingOpens", "evictions",
                              "reloadedAssets", "unscopedLoads"):
                        row["cs_" + k] = cs.get(k)
                    for k in ("ownedBytes", "lruBytes", "budgetBytes"):
                        row["cs_" + k.replace("Bytes", "MB")] = cs.get(k) / MIB if cs.get(k) is not None else None
                    row["cs_owners"] = cs.get("owners")
                    uu = cs.get("unloadUnused") or {}
                    row["cs_uuaCalls"], row["cs_uuaLastMs"], row["cs_uuaMaxMs"] = uu.get("calls"), uu.get("lastMs"), uu.get("maxMs")
                    tc = cs.get("textureCheck") or {}
                    row["cs_tcPatched"], row["cs_tcRefused"] = tc.get("patched"), tc.get("refused")
                    row["cs_tcRepairedOpens"] = tc.get("repairedOpens")
                    pf = cs.get("prefetch") or {}
                    row["cs_pfStarted"], row["cs_pfUsed"], row["cs_pfExpired"] = pf.get("started"), pf.get("used"), pf.get("expired")
                    row["cs_pfAssetRequests"], row["cs_pfRefused"] = pf.get("assetRequests"), pf.get("refused")
                    try:    # the latest reply whole (counters): the report's content.stats "at the end"
                        with open(os.path.join(self.out, "content_stats_last.json"), "w", encoding="utf-8") as f:
                            json.dump(cs, f, indent=1, ensure_ascii=False)
                    except OSError:
                        pass
            else:
                self.missing["content.stats"] = "not in this build: open / owned / LRU bytes, evictions, unscoped loads (KS6)"
            if self.supports("app.perf"):
                ap = self.call({"cmd": "app.perf", "reset": True, "motion": True}, timeout=300, check=False)
                if ap.get("ok"):
                    fm = ap.get("frameMs") or {}
                    row["ap_frameMed"], row["ap_frameP95"], row["ap_frameMax"] = fm.get("median"), fm.get("p95"), fm.get("max")
                    db = (ap.get("motion") or {}).get("dynamicBone") or {}
                    row["ap_dbMed"], row["ap_dbP95"] = db.get("medianMs"), db.get("p95Ms")
                    mem = ap.get("memory") or {}
                    gb = mem.get("graphicsDriverBytes")
                    row["ap_gfxMB"] = gb / MIB if isinstance(gb, (int, float)) and gb >= 0 else None
                    ua = mem.get("unityAllocatedBytes")
                    row["ap_unityAllocMB"] = ua / MIB if isinstance(ua, (int, float)) else None
                    row["ap_frames"] = ap.get("frames")
                    uu = ap.get("unloadUnused") or {}
                    row["ap_uuaCalls"], row["ap_uuaMaxMs"] = uu.get("calls"), uu.get("maxMs")
            else:
                self.missing["app.perf"] = "not in this build: frame / motion stage times, DynamicBone (KS7), memory, UnloadUnusedAssets calls"
            self.poll_log()
        except Abort:
            raise
        except Exception as e:
            row["sampleError"] = "%s: %s" % (type(e).__name__, str(e)[:200])

    def diagnostics(self):
        """plugin.list {diagnostics: true} (managed bytes after a full collection, object counts); the frames meanwhile are
        tagged 'diag' and left out of the frame statistics."""
        with self.diag_lock:
            self.diag_inflight += 1
        try:
            return self.call({"cmd": "plugin.list", "diagnostics": True}, timeout=300, check=False)
        finally:
            with self.diag_lock:
                self.diag_inflight -= 1
                # the collection's long frame ends after the reply: the frames of the next half second count as 'diag' too
                self.diag_until = time.perf_counter() + 0.5

    def poll_log(self):
        """log.tail errors since the last poll: managed exceptions and device-removed codes (a device loss ends the run)."""
        r = self.call({"cmd": "log.tail", "n": 1000, "level": "error", "since": self.log_since}, timeout=60, check=False)
        if not r.get("ok"):
            return
        self.log_since = r.get("last", self.log_since)
        for e in r.get("entries") or []:
            msg = str(e.get("message") or "")
            if e.get("type") == "exception" or EXC_LINE.match(msg):
                self.log_exceptions += 1
            low = msg.lower()
            if any(k in low for k in DEVICE_TEXT):
                self.log_device += 1
                if len(self.log_device_lines) < 5:
                    self.log_device_lines.append(msg[:200])
        if self.log_device and not self.abort_reason:
            self.abort_reason = "a device loss in the player log (%s)" % self.log_device_lines[0][:120]
            self.log(self.abort_reason)

    COLUMNS = ["t_s", "session_min", "wall", "cycle", "phase", "gpuDedMB", "gpuShrMB", "adpDedMB", "adpShrMB", "wsMB", "privMB",
               "frame", "fpsMean", "frames", "frameMedMs", "frameP95Ms", "frameMaxMs", "framesOver50", "uiFps", "openBundles",
               "thumbOnlyBundles", "thumbsShown", "thumbsPending", "thumbsFromBundles", "thumbsFailed", "managedMB",
               "gameObjects", "materials", "meshes", "textures", "renderTextures", "items",
               "cs_open", "cs_owned", "cs_lru", "cs_ownedMB", "cs_lruMB", "cs_budgetMB", "cs_evictions", "cs_unscoped", "cs_unscopedLoads",
               "cs_reloadedAssets", "cs_owners", "cs_uuaCalls", "cs_uuaLastMs", "cs_uuaMaxMs", "cs_tcPatched", "cs_tcRefused",
               "cs_tcRepairedOpens", "cs_pfStarted", "cs_pfUsed", "cs_pfExpired", "cs_pfAssetRequests", "cs_pfRefused",
               "ap_frames", "ap_frameMed", "ap_frameP95", "ap_frameMax", "ap_dbMed", "ap_dbP95", "ap_gfxMB", "ap_unityAllocMB",
               "ap_uuaCalls", "ap_uuaMaxMs", "logExceptions", "logDevice", "gpuByLuid", "counterMs", "sampleMs"]

    def write_sample(self, row):
        path = os.path.join(self.out, "samples.tsv")
        new = not os.path.exists(path)
        row.setdefault("items", len(getattr(self, "item_keys", []) or []))
        with open(path, "a", encoding="utf-8") as f:
            if new:
                f.write("\t".join(self.COLUMNS) + "\n")
            vals = []
            for c in self.COLUMNS:
                v = row.get(c)
                vals.append(fmt(v, 3 if c in ("session_min",) else 1) if not isinstance(v, str) else v)
            f.write("\t".join(vals) + "\n")

    def sampler_loop(self):
        period = self.a.sample_sec
        nxt = time.perf_counter()
        while not self.sampler_stop.is_set():
            try:
                self.sample()
            except Abort:
                return
            except Exception as e:
                self.log("sample failed: %s: %s" % (type(e).__name__, e))
            nxt += period
            while nxt < time.perf_counter():
                nxt += period
            if self.sampler_stop.wait(max(0.0, nxt - time.perf_counter())):
                return

    # ------------------------------------------------------------ UI helpers

    def ui_root(self):
        u = self.call({"cmd": "ui.state"}, timeout=120, check=False)
        r = u.get("root") or [0, 0, 1920, 1080]
        return r

    def select(self, key):
        self.call({"cmd": "select", "key": key}, timeout=60, check=False)

    def show(self, spec):
        args = {k: v for k, v in spec.items() if v is not None and k in ("node", "page", "section", "part", "kind", "slot")}
        r = self.call({"cmd": "ui.action", "action": "edit.show", "args": args}, timeout=120, check=False)
        return bool(r.get("ok") and r.get("ran", True))

    @staticmethod
    def _inside(b, outer):
        return b[0] >= outer[0] - 1 and b[1] >= outer[1] - 1 and b[0] + b[2] <= outer[0] + outer[2] + 1 and b[1] + b[3] <= outer[1] + outer[3] + 1

    @staticmethod
    def _clip_center(b, root):
        x0, y0 = max(b[0], root[0]), max(b[1], root[1])
        x1, y1 = min(b[0] + b[2], root[0] + root[2]), min(b[1] + b[3], root[1] + root[3])
        if x1 - x0 < 4 or y1 - y0 < 4:
            return None
        return [round((x0 + x1) / 2.0, 1), round((y0 + y1) / 2.0, 1)]

    def find_list(self, member):
        """(the list's bound, its grid's bound) of the shown list of a member (by the field name, else the first visible
        Maker list), or (None, None)."""
        want = None
        if member:
            q = self.call({"cmd": "ui.query", "name": "field-appearance." + member}, timeout=60, check=False)
            for e in q.get("elements") or []:
                b = e.get("worldBound")
                if e.get("visible") and b and b[2] > 4 and b[3] > 4:
                    want = b
                    break
        if want is None:
            q = self.call({"cmd": "ui.query", "name": ".hs-partlist", "all": True}, timeout=60, check=False)
            for e in q.get("elements") or []:
                b = e.get("worldBound")
                if e.get("visible") and b and b[2] > 4 and b[3] > 40:
                    want = b
                    break
        if want is None:
            return None, None
        q = self.call({"cmd": "ui.query", "name": ".hs-partlist__grid", "all": True}, timeout=60, check=False)
        for e in q.get("elements") or []:
            b = e.get("worldBound")
            if b and b[2] > 4 and b[3] > 4 and self._inside(b, want):
                return want, b
        return want, want

    def thumbs_pending(self):
        u = self.call({"cmd": "ui.state"}, timeout=120, check=False)
        th = u.get("thumbs") or {}
        return th.get("pending")

    def view_list(self, spec, member, cells):
        """Shows a list (edit.show), measures its settle time (until ui.state thumbs.pending is 0 after 3 frames, at most
        10 s) and scrolls it with 20 wheel steps; returns the grid's bound."""
        key = spec.get("listKey") or member
        self.phase = "view"
        t = time.perf_counter()
        if not self.show(spec):
            self.event("list.show", key, ok=False, note="edit.show did not show it")
            self.phase = "edit"
            return None
        self.call({"cmd": "ping", "waitFrames": 3}, timeout=60, check=False)
        settle, pending = None, None
        end = time.perf_counter() + 10.0
        while time.perf_counter() < end:
            pending = self.thumbs_pending()
            if pending == 0:
                settle = (time.perf_counter() - t) * 1000.0
                break
            time.sleep(0.03)
        n = self.view_counts.get(key, 0) + 1
        self.view_counts[key] = n
        self.views.append({"t": self.t_session(), "cycle": self.cycle, "list": key, "view": n, "cells": cells,
                           "settleMs": settle, "pendingLeft": pending if settle is None else 0})
        lb, gb = self.find_list(member)
        self.phase = "edit"
        if gb is None:
            self.event("list.scroll", key, ok=False, note="no visible list")
            return None
        # the list expanded to its 8 rows first (as a user browsing it does), folded again after the scroll
        pane = self.inspector_bound()
        expanded = self.click_child("partlist-expand", lb, beside=80.0, pane=pane)
        expand_note = self.click_note
        if expanded:
            lb, gb = self.find_list(member)
            if gb is None:
                self.event("list.scroll", key, ok=False, note="no visible list after the expand")
                return None
        c = self._clip_center(gb, self.root)
        if c is None:
            lb, gb = self.bring_into_view(member, spec, lb, gb)
            c = self._clip_center(gb, self.root) if gb else None
        if c is None:
            self.event("list.scroll", key, ok=False, note="list outside the panel (grid %s, root %s)" % (gb, self.root))
            if expanded:
                self.click_child("partlist-expand", lb, beside=80.0, pane=pane)
            return gb
        before = self.cells_in(gb)
        self.phase = "scroll"
        t = time.perf_counter()
        ev = [{"type": "wheel", "x": c[0], "y": c[1], "delta": self.a.wheel_delta} for _ in range(20)]
        r = self.call({"cmd": "ui.input", "events": ev, "frames": 2}, timeout=120, check=False)
        rt = (time.perf_counter() - t) * 1000.0
        self.phase = "edit"
        lb2, gb2 = self.find_list(member)
        after = self.cells_in(gb2 or gb)
        moved = bool(before) and bool(after) and before != after
        self.scroll_moved[moved] = self.scroll_moved.get(moved, 0) + 1
        self.event("list.scroll", key, ok=bool(r.get("ok")), rt_ms=rt,
                   note="wheels %s, expanded %s%s, cells shown before %d after %d, moved %s%s"
                   % (r.get("sent"), expanded, "" if expanded else " (%s)" % expand_note, len(before), len(after), moved,
                      "" if gb2 == gb else ", the list moved in the panel"))
        if expanded:
            self.click_child("partlist-expand", lb2 or lb, beside=80.0, pane=pane)
        return gb

    def bring_into_view(self, member, spec, lb, gb):
        """Wheel steps over the shown section's header (or the inspector beside the list) until the list's grid shows at least
        120 px (or all of it) inside the panel, at most 12 steps; returns the list's and the grid's bound after."""
        anchor = None
        sec = spec.get("section")
        if sec:
            q = self.call({"cmd": "ui.query", "name": "edit-section-" + sec}, timeout=60, check=False)
            for e in q.get("elements") or []:
                b = e.get("worldBound")
                if e.get("visible") and b:
                    head = [b[0], b[1], b[2], min(b[3], 20.0)]
                    anchor = self._clip_center(head, self.root)
                    break
        if anchor is None and lb:
            # the inspector's column at the list's x, inside the panel
            anchor = [round(lb[0] + 8, 1), round(self.root[1] + self.root[3] * 0.5, 1)]
        if anchor is None:
            return lb, gb
        top, bottom = self.root[1], self.root[1] + self.root[3]
        for _ in range(12):
            if gb is not None:
                shown = min(gb[1] + gb[3], bottom) - max(gb[1], top)
                if shown >= min(gb[3], 120.0):
                    break
                delta = self.a.wheel_delta if gb[1] + gb[3] > bottom else -self.a.wheel_delta
            else:
                delta = self.a.wheel_delta
            self.call({"cmd": "ui.input", "events": [{"type": "wheel", "x": anchor[0], "y": anchor[1], "delta": delta}], "frames": 2},
                      timeout=60, check=False)
            lb, gb = self.find_list(member)
        return lb, gb

    def inspector_bound(self):
        """The inspector pane's bound from ui.state's layout (the region whose active panel is the inspector), or the root."""
        u = self.call({"cmd": "ui.state"}, timeout=60, check=False)
        for r in (((u.get("layout") or {}).get("regions")) or {}).values():
            if isinstance(r, dict) and r.get("active") == "inspector" and not r.get("collapsed") and r.get("worldBound"):
                return r["worldBound"]
        return self.root

    def click_child(self, name, bound, beside=0.0, pane=None):
        """Clicks the visible element of that name on a bound's rows (inside it, or up to `beside` px right of it) and inside
        the pane (default the root) through ui.input pointer down / up; True when one was clicked."""
        self.click_note = "no bound"
        if not bound:
            return False
        pane = pane or self.root
        q = self.call({"cmd": "ui.query", "name": name, "all": True}, timeout=60, check=False)
        seen = []
        for e in q.get("elements") or []:
            b = e.get("worldBound")
            seen.append("%s%s" % ("v" if e.get("visible") else "h", [round(x) for x in b] if b else None))
            if not (e.get("visible") and b and b[2] > 2 and b[3] > 2):
                continue
            wide = [bound[0], bound[1], bound[2] + beside, bound[3]]
            if self._inside(b, wide):
                # the part of the element the pane shows (the pane may clip the tool row's end): its centre is clicked
                shown = [max(b[0], pane[0]), max(b[1], pane[1])]
                shown += [min(b[0] + b[2], pane[0] + pane[2]) - shown[0], min(b[1] + b[3], pane[1] + pane[3]) - shown[1]]
                c = self._clip_center(shown, self.root) if shown[2] >= 8 and shown[3] >= 8 else None
                if c is None:
                    self.click_note = "%s outside the pane %s" % ([round(x) for x in b], [round(x) for x in pane])
                    return False
                self.call({"cmd": "ui.input", "events": [{"type": "pointer", "phase": "down", "x": c[0], "y": c[1], "button": 0},
                                                         {"type": "pointer", "phase": "up", "x": c[0], "y": c[1], "button": 0}],
                           "frames": 3}, timeout=60, check=False)
                self.click_note = "clicked"
                return True
        self.click_note = "none inside %s of %d: %s" % ([round(x) for x in bound], len(seen), " ".join(seen[:4]))
        return False

    def cells_in(self, gb):
        """Names of the shown cells (part-<ref>) inside a grid's bound."""
        q = self.call({"cmd": "ui.query", "name": ".hs-card", "all": True}, timeout=60, check=False)
        out = set()
        for e in q.get("elements") or []:
            b = e.get("worldBound")
            n = e.get("name") or ""
            if e.get("visible") and b and n.startswith("part-") and self._inside(b, gb):
                out.add(n)
        return frozenset(out)

    def search_box(self, member):
        """(the center, the value) of the search field of a member's shown list, or (None, None)."""
        lb, _ = self.find_list(member)
        if lb is None:
            return None, None
        q = self.call({"cmd": "ui.query", "name": "partlist-search", "all": True}, timeout=60, check=False)
        for e in q.get("elements") or []:
            b = e.get("worldBound")
            if e.get("visible") and b and b[2] > 4 and b[3] > 4 and self._inside(b, lb):
                return self._clip_center(b, self.root), e.get("value")
        return None, None

    def type_search(self, member, text):
        """Types text into the list's search field (clicked first); "" clears it (Ctrl+A, Delete). Keys are sent only while a
        text field has the focus (a Delete elsewhere would delete the selection). True when the field holds the text after."""
        c, v = self.search_box(member)
        if c is None:
            return False
        if (v or "") == text:
            return True
        self.call({"cmd": "ui.input", "events": [{"type": "pointer", "phase": "down", "x": c[0], "y": c[1], "button": 0},
                                                 {"type": "pointer", "phase": "up", "x": c[0], "y": c[1], "button": 0}], "frames": 2},
                  timeout=60, check=False)
        u = self.call({"cmd": "ui.state"}, timeout=60, check=False)
        if u.get("focusKind") != "TextField":
            self.event("search", member, ok=False, note="the search field did not take the focus (%s)" % u.get("focus"))
            return False
        ev = []
        if v:
            ev += [{"type": "key", "key": "A", "mods": ["ctrl"]}, {"type": "key", "key": "Delete"}]
        if text:
            ev.append({"type": "text", "text": text})
        self.call({"cmd": "ui.input", "events": ev, "frames": 12}, timeout=60, check=False)   # the search waits 120 ms
        _, v2 = self.search_box(member)
        return (v2 or "") == text

    def hover(self, ref, spec, member):
        """Shows the row's list, finds the row through the list's search field (as a user does), moves the pointer over its
        cell (else over the list): the prefetch path. The search is cleared after the edit (clear_search)."""
        self.phase = "hover"
        self.show(spec)
        self.call({"cmd": "ping", "waitFrames": 3}, timeout=60, check=False)
        if self.a.hover_search and member and self.type_search(member, ref):
            self.searched = member
        target, hit = None, False
        q = self.call({"cmd": "ui.query", "name": "part-" + ref}, timeout=60, check=False)
        for e in q.get("elements") or []:
            b = e.get("worldBound")
            if e.get("visible") and b and b[2] > 4 and b[3] > 4:
                target = self._clip_center(b, self.root)
                hit = target is not None
                break
        if target is None:
            _, gb = self.find_list(member)
            target = self._clip_center(gb, self.root) if gb else None
        if target is None:
            self.phase = "edit"
            return "none"
        self.call({"cmd": "ui.input", "events": [{"type": "pointer", "phase": "move", "x": target[0], "y": target[1]}], "frames": 2},
                  timeout=60, check=False)
        if self.a.hover_dwell_ms > 0:
            time.sleep(self.a.hover_dwell_ms / 1000.0)      # the pointer rests on the cell (no other UI event meanwhile)
        self.phase = "edit"
        return "cell" if hit else "list"

    # ------------------------------------------------------------ workload setup

    def add_chara(self, sex):
        ok, m, sync, rt, err = self.timed({"cmd": "chara.load", "path": self.cards[sex], "sex": sex}, timeout=300)
        if not ok:
            raise CmdError("chara.load", {"error": err})
        key = ((m or {}).get("added") or [{}])[0].get("key")
        if not key:
            raise CmdError("chara.load", {"error": "no key in the reply"})
        self.event("chara.add", sex, ok=True, rt_ms=rt, sync_ms=sync, note="buildMs %s" % (m or {}).get("buildMs"))
        return key

    def rows_of(self, key, sex, path=None, category=None):
        cache_key = (sex, path or "cat%s" % category)
        if cache_key in self.row_cache:
            return self.row_cache[cache_key]
        req = {"cmd": "chara.edit.list", "limit": 10000}
        if path:
            req.update({"key": key, "path": path})
        else:
            req.update({"category": category, "sex": sex})
        r = self.call(req, timeout=120, check=False)
        rows = []
        if r.get("ok"):
            for x in r.get("rows") or []:
                ref = x.get("ref")
                if not ref or x.get("gated") or ref in self.crash_refs:
                    continue
                rows.append((ref, x.get("name") or ""))
        self.row_cache[cache_key] = rows
        self.list_sizes[path or "category %s" % category] = r.get("total") if r.get("ok") else None
        return rows

    def current_values(self, key, paths):
        r = self.call({"cmd": "chara.edit.get", "key": key, "paths": list(paths)}, timeout=60, check=False)
        return (r.get("values") or {}) if r.get("ok") else {}

    def plan_lists(self, sex, key):
        """The lists this character can cycle: hair, face / body, clothes kinds worn (never none), accessory categories."""
        P = {"hair": [], "face": [], "clothes": [], "acc": {}}
        for k, part in enumerate(HAIR_PARTS):
            path = "chara.hair.%s.item" % part
            rows = self.rows_of(key, sex, path=path)
            if len(rows) > 1:
                P["hair"].append({"path": path, "rows": rows, "spec": {"node": "hair", "page": "hair", "section": "hair.hair.%s.style" % part, "part": part}})
        for path, node, page, section in FACE_BODY:
            rows = self.rows_of(key, sex, path=path)
            if len(rows) > 1:
                P["face"].append({"path": path, "rows": rows, "spec": {"node": node, "page": page, "section": section}})
        cur = self.current_values(key, ["coord.clothes.%s.item" % k for k in CLOTHES_KINDS])
        worn = {k: cur.get("coord.clothes.%s.item" % k) for k in CLOTHES_KINDS}
        def is_none(v):
            return v in (None, "", 0) or (isinstance(v, str) and (v.endswith("#0") and v.startswith(VANILLA)))
        for k in CLOTHES_KINDS:
            v = worn.get(k)
            if is_none(v) or k in INNER_KINDS:
                continue
            # a top worn without a bottom (a one-piece): swapping it could uncover; same for the inner top
            if k == "top" and is_none(worn.get("bottom")):
                continue
            if k == "innerTop" and is_none(worn.get("innerBottom")):
                continue
            path = "coord.clothes.%s.item" % k
            rows = [x for x in self.rows_of(key, sex, path=path) if not NONE_NAME.search(x[1]) and not x[0].endswith("#0")]
            if len(rows) > 1:
                P["clothes"].append({"path": path, "rows": rows, "kind": k,
                                     "spec": {"node": "clothes", "page": "clothes", "section": "clothes.clothes.%s.type" % k, "kind": k}})
        for cat in range(ACC_FIRST, ACC_LAST + 1):
            rows = [x for x in self.rows_of(None, sex, category=cat) if not NONE_NAME.search(x[1])]
            if rows:
                P["acc"][cat] = rows
        self.info.setdefault("lists", {})[sex] = {"hair": len(P["hair"]), "faceBody": len(P["face"]),
                                                   "clothesKinds": [c["kind"] for c in P["clothes"]],
                                                   "accessoryCategories": len(P["acc"]),
                                                   "worn": {k: (worn.get(k) is not None and not is_none(worn.get(k))) for k in CLOTHES_KINDS}}
        return P

    def setup_scene(self):
        self.phase = "setup"
        self.row_cache = {}
        self.call({"cmd": "scene.new"}, timeout=120)
        self.keys = {"female": self.add_chara("female"), "male": self.add_chara("male")}
        self.root = self.ui_root()
        self.ks6_point("setup: scene.new + the two targets")
        self.coords = {}
        for sex in ("female", "male"):
            r = self.call({"cmd": "coord.save", "key": self.keys[sex], "name": "session_" + sex, "overwrite": True,
                           "thumb": "placeholder"}, timeout=120)
            self.coords[sex] = r.get("path")
            self.event("coord.save", sex, ok=True, note="%s bytes" % r.get("bytes"))
        self.plans = {}
        for sex in ("female", "male"):
            self.plans[sex] = self.plan_lists(sex, self.keys[sex])
        self.item_rows = self.collect_items()
        self.maps_all = self.collect_maps()
        self.item_keys = []
        self.acc_added = {"female": [], "male": []}

    def collect_items(self):
        rows = []
        for g in self.item_groups:
            r = self.call({"cmd": "item.search", "group": g, "limit": 100000}, timeout=120, check=False)
            for it in r.get("items") or []:
                ref = it.get("id")
                if not ref or ref in self.crash_refs:
                    continue
                if self.item_categories and (it.get("group"), it.get("category")) not in self.item_categories:
                    continue
                rows.append((ref, it.get("group"), it.get("category"), it.get("guid") == VANILLA))
        self.info["itemRows"] = {"rows": len(rows), "vanilla": sum(1 for x in rows if x[3]), "groups": len(self.item_groups)}
        if not rows:
            raise SetupError("no item rows in the groups %s" % self.item_groups)
        return rows

    def collect_maps(self):
        r = self.call({"cmd": "maps", "limit": 100000}, timeout=120, check=False)
        refs = [m.get("id") for m in r.get("maps") or [] if m.get("id")]
        if self.loadable_maps is not None:
            refs = [x for x in refs if x in self.loadable_maps]
        refs = [x for x in refs if x not in self.crash_refs]
        largest = [x for x in self.largest_maps if x in refs or self.loadable_maps is None]
        self.info["maps"] = {"loadable": len(refs), "largest": largest, "largestSource": self.map_source}
        # the schedule: the largest in the first six cycles (seeded positions), the rest seeded among the loadable rows
        n = max(12, int(math.ceil(self.a.minutes * 60.0 / self.a.cycle_sec)) + 2)
        sched = [None] * n
        pos = self.rng.sample(range(min(6, n)), min(len(largest), min(6, n)))
        for p, ref in zip(pos, largest):
            sched[p] = ref
        others = [x for x in refs if x not in largest]
        for i in range(n):
            if sched[i] is None and others:
                sched[i] = others.pop(self.rng.randrange(len(others)))
        self.map_schedule = sched
        return refs

    def ks6_point(self, what):
        """After a scene.new (with what it holds then): the open bundles and the catalog's owned bytes when the build has
        them, the managed bytes and object counts."""
        p = {"t": self.t_session(), "cycle": self.cycle, "what": what}
        u = self.call({"cmd": "ui.state"}, timeout=120, check=False)
        th = u.get("thumbs") or {}
        p["openBundles"] = th.get("openBundles")
        if self.supports("content.stats"):
            cs = self.call({"cmd": "content.stats"}, timeout=120, check=False)
            p["ownedMB"] = cs.get("ownedBytes") / MIB if cs.get("ownedBytes") is not None else None
            p["unscopedLoads"] = cs.get("unscopedLoads")
            p["open"] = cs.get("open")
        pl = self.diagnostics()
        d = pl.get("diagnostics") or {}
        p["managedMB"] = d.get("managedBytes") / MIB if d.get("managedBytes") is not None else None
        for k in ("gameObjects", "materials", "meshes", "textures", "renderTextures"):
            p[k] = d.get(k)
        self.ks6.append(p)
        self.log("KS6 point (%s): open bundles %s, owned MB %s, managed MB %s, objects %s / materials %s / textures %s"
                 % (what, p.get("openBundles"), fmt(p.get("ownedMB")), fmt(p.get("managedMB")), p.get("gameObjects"),
                    p.get("materials"), p.get("textures")))

    # ------------------------------------------------------------ one cycle

    def pick(self, rows, avoid):
        if not rows:
            return None
        for _ in range(8):
            ref = rows[self.rng.randrange(len(rows))][0]
            if ref != avoid:
                return ref
        return rows[self.rng.randrange(len(rows))][0]

    def edit_item(self, key, path, ref, what, hover_spec=None, member=None):
        hv = ""
        if hover_spec is not None:
            hv = self.hover(ref, hover_spec, member)
        self.phase = "edit"
        ok, m, sync, rt, err = self.timed({"cmd": "chara.edit.item", "key": key, "path": path, "ref": ref}, timeout=120)
        self.event("edit", what, ref, ok=ok, rt_ms=rt, sync_ms=sync, note=("hover %s" % hv if hv else "") + ("" if ok else " " + err))
        self.clear_search()
        return ok

    def clear_search(self):
        if self.searched:
            member, self.searched = self.searched, None
            if not self.type_search(member, ""):
                self.event("search.clear", member, ok=False, note="the search field kept its text")

    def part_changes(self, sex, key, P, scrolled):
        ops = []
        for k in range(8):
            if P["hair"]:
                ops.append(("hair", P["hair"][k % len(P["hair"])]))
        face = list(P["face"])
        self.rng.shuffle(face)
        for k in range(8):
            if face:
                ops.append(("face", face[k % len(face)]))
        for k in range(12):
            if P["clothes"]:
                ops.append(("clothes", P["clothes"][k % len(P["clothes"])]))
        hover_idx = set(self.rng.sample(range(len(ops) + 12), (len(ops) + 12) // 2))
        current = {}
        for i, (kind, L) in enumerate(ops):
            self.check_abort()
            self.check_time()
            path = L["path"]
            ref = self.pick(L["rows"], current.get(path))
            current[path] = ref
            hs = L["spec"] if i in hover_idx else None
            self.edit_item(key, path, ref, "%s %s" % (kind, path), hover_spec=hs, member=path)
            if path not in scrolled:
                scrolled.add(path)
                self.view_list(dict(L["spec"], listKey=path), path, len(L["rows"]))
        self.accessory_changes(sex, key, P, hover_idx, len(ops), scrolled)

    def accessory_changes(self, sex, key, P, hover_idx, base, scrolled):
        if not P["acc"]:
            return
        r = self.call({"cmd": "chara.edit.accessories", "key": key, "served": False}, timeout=120, check=False)
        free = list(r.get("freeSlots") or [])
        extra_on = bool(r.get("extraSlotsOn"))
        present = [(s.get("accessory"), s.get("type"), s.get("slot")) for s in r.get("slots") or []
                   if s.get("accessory") and s.get("type") not in (None, 350)]
        cats = sorted(P["acc"])
        i = base
        # 5 adds (one into an extra slot when the build loads them), 4 changes, 3 removes
        for n in range(5):
            self.check_abort()
            self.check_time()
            if not free:
                break
            lows = [s for s in free if s < 20]
            highs = [s for s in free if s >= 20]
            slot = highs[0] if (n == 4 and extra_on and highs) else (lows[self.rng.randrange(len(lows))] if lows else free[0])
            free.remove(slot)
            cat = cats[self.rng.randrange(len(cats))]
            ref = self.pick(P["acc"][cat], None)
            self.phase = "edit"
            ok, m, sync, rt, err = self.timed({"cmd": "chara.edit.accessory", "key": key, "op": "add", "category": cat,
                                               "ref": ref, "slot": slot}, timeout=120)
            acc = (m or {}).get("accessory") if ok else None
            self.event("acc.add", "slot %d category %d" % (slot, cat), ref, ok=ok, rt_ms=rt, sync_ms=sync, note="" if ok else err)
            if acc:
                present.append((acc, cat, slot))
                if "acc:%d" % cat not in scrolled:
                    scrolled.add("acc:%d" % cat)
                    member = 'coord.accessories["%s"].item' % acc
                    self.view_list({"node": "accessories", "page": "accessories", "slot": slot, "section": acc_section(slot),
                                    "listKey": "accessory category %d" % cat}, member, len(P["acc"][cat]))
            i += 1
        for n in range(4):
            self.check_abort()
            self.check_time()
            cand = [p for p in present if p[1] in P["acc"]]
            if not cand:
                break
            acc, cat, slot = cand[self.rng.randrange(len(cand))]
            ref = self.pick(P["acc"][cat], None)
            member = 'coord.accessories["%s"].item' % acc
            hs = {"node": "accessories", "page": "accessories", "slot": slot, "section": acc_section(slot)} if i in hover_idx else None
            self.edit_item(key, member, ref, "accessory category %d slot %s" % (cat, slot), hover_spec=hs, member=member)
            i += 1
        for n in range(3):
            self.check_abort()
            self.check_time()
            if not present:
                break
            acc, cat, slot = present.pop(self.rng.randrange(len(present)))
            self.phase = "edit"
            ok, m, sync, rt, err = self.timed({"cmd": "chara.edit.accessory", "key": key, "op": "remove", "accessory": acc}, timeout=120)
            self.event("acc.remove", "slot %s category %s" % (slot, cat), ok=ok, rt_ms=rt, sync_ms=sync, note="" if ok else err)
            i += 1

    def reapply_coordinate(self, sex, key):
        self.phase = "coord"
        ok, m, sync, rt, err = self.timed({"cmd": "chara.edit.coordinate", "key": key, "file": self.coords[sex], "mode": "all"}, timeout=300)
        self.event("coord.apply", sex, ok=ok, rt_ms=rt, sync_ms=sync, note="" if ok else err)

    def item_churn(self):
        self.phase = "items"
        for n in range(20):
            self.check_abort()
            self.check_time()
            ref, g, c, van = self.item_rows[self.rng.randrange(len(self.item_rows))]
            ok, m, sync, rt, err = self.timed({"cmd": "item.add", "ref": ref}, timeout=120)
            key = (((m or {}).get("added") or [{}])[0]).get("key") if ok else None
            if key:
                self.item_keys.append(key)
            self.event("item.add", "group %s category %s%s" % (g, c, "" if van else " mod"), ref, ok=ok, rt_ms=rt, sync_ms=sync, note="" if ok else err)
        self.undo_redo("after the additions")
        dels = 0
        for n in range(10):
            self.check_abort()
            if not self.item_keys:
                break
            key = self.item_keys.pop(self.rng.randrange(len(self.item_keys)))
            ok, m, sync, rt, err = self.timed({"cmd": "delete", "key": key}, timeout=120)
            dels += 1 if ok else 0
            self.event("item.delete", key, ok=ok, rt_ms=rt, sync_ms=sync, note="" if ok else err)
        self.undo_redo("after the deletions")
        self.refresh_items()

    def undo_redo(self, what):
        """undo 5, redo 5 (busy inside a batch: plain requests, round trip only)."""
        for cmd in ("undo", "redo"):
            t = time.perf_counter()
            r = self.call({"cmd": cmd, "n": 5}, timeout=120, check=False)
            ok = bool(r.get("ok"))
            self.event(cmd, "5 %s" % what, ok=ok, rt_ms=(time.perf_counter() - t) * 1000.0,
                       note=("%s entries" % r.get("undone" if cmd == "undo" else "redone")) if ok else "%s (%s)" % (str(r.get("error"))[:200], r.get("code")))

    def refresh_items(self):
        r = self.call({"cmd": "scene"}, timeout=120, check=False)
        objs = r.get("objects") or []
        self.item_keys = [o.get("key") for o in objs if o.get("kind") == "item" and o.get("key")]
        charas = [o for o in objs if o.get("kind") in ("chara", "character", "female", "male")]
        return objs, charas

    def load_map(self):
        idx = self.cycle - 1
        ref = self.map_schedule[idx] if 0 <= idx < len(self.map_schedule) else None
        if not ref:
            return
        self.phase = "map"
        ok, m, sync, rt, err = self.timed({"cmd": "map.load", "ref": ref}, timeout=300)
        note = "state %s loadMs %s" % ((m or {}).get("state"), (m or {}).get("loadMs")) if ok else err
        self.event("map.load", "largest" if ref in self.largest_maps else "seeded", ref, ok=ok, rt_ms=rt, sync_ms=sync, note=note)

    def scene_churn(self):
        self.phase = "scene"
        ok, m, sync, rt, err = self.timed({"cmd": "scene.save", "name": "session", "overwrite": True, "thumb": "placeholder"}, timeout=300)
        self.event("scene.save", ok=ok, rt_ms=rt, sync_ms=sync, note=("%s bytes" % (m or {}).get("bytes")) if ok else err)
        t = time.perf_counter()
        self.call({"cmd": "scene.new"}, timeout=120)
        self.event("scene.new", ok=True, rt_ms=(time.perf_counter() - t) * 1000.0)
        self.ks6_point("cycle %d: scene.new" % self.cycle)
        t = time.perf_counter()
        r = self.call({"cmd": "scene.load", "name": "session"}, timeout=300, check=False)
        self.event("scene.load", ok=bool(r.get("ok")), rt_ms=(time.perf_counter() - t) * 1000.0,
                   note="nodes %s built %s failed %s" % (r.get("nodes"), r.get("built"), r.get("failed")) if r.get("ok") else str(r.get("error"))[:200])
        objs, charas = self.refresh_items()
        self.remap_keys(objs)
        present = set(o.get("key") for o in objs)
        for sx in ("female", "male"):
            if self.keys.get(sx) not in present:
                self.event("chara.missing", sx, ok=False, note="not in the loaded scene: added again from its card")
                self.keys[sx] = self.add_chara(sx)
        # graveyard churn: one character deleted and re-added from its card
        sex = "female" if (self.cycle // 3) % 2 == 1 else "male"
        key = self.keys.get(sex)
        if key:
            ok, m, sync, rt, err = self.timed({"cmd": "delete", "key": key}, timeout=120)
            self.event("chara.delete", sex, ok=ok, rt_ms=rt, sync_ms=sync, note="" if ok else err)
            self.keys[sex] = self.add_chara(sex)

    def remap_keys(self, objs):
        """After scene.load: the characters' keys by sex (the loaded scene keeps the node ids; this only guards)."""
        for o in objs:
            if o.get("sex") in (0, 1) and o.get("kind") not in ("item",):
                sex = "male" if o.get("sex") == 0 else "female"
                if self.keys.get(sex) != o.get("key") and not any(x.get("key") == self.keys.get(sex) for x in objs):
                    self.keys[sex] = o.get("key")

    def check_time(self):
        if self.t_work is not None and time.perf_counter() - self.t_work >= self.a.minutes * 60.0:
            raise TimeUp()

    def run_cycle(self):
        sex = "female" if self.cycle % 2 == 1 else "male"
        key = self.keys[sex]
        self.phase = "edit"
        self.select(key)
        self.show({"node": "face", "page": "head"})
        scrolled = set()
        self.part_changes(sex, key, self.plans[sex], scrolled)
        self.check_time()
        self.reapply_coordinate(sex, key)
        self.check_time()
        self.item_churn()
        self.check_time()
        self.load_map()
        self.check_time()
        if self.cycle % 3 == 0:
            self.scene_churn()

    def workload(self):
        self.t_work = time.perf_counter()
        self.cycle = 1
        self.setup_scene()
        self.log("setup done in %.1f s: items %s, maps %s, lists %s" % (time.perf_counter() - self.t_work, self.info.get("itemRows"),
                                                                        self.info.get("maps"), json.dumps(self.info.get("lists"))[:600]))
        try:
            while True:
                self.check_abort()
                t_cycle = self.t_work + (self.cycle - 1) * self.a.cycle_sec
                self.log("cycle %d (%s) starts at %.1f min" % (self.cycle, "female" if self.cycle % 2 == 1 else "male", self.t_session() / 60.0))
                self.run_cycle()
                self.cycles_done = self.cycle
                self.log("cycle %d done at %.1f min" % (self.cycle, self.t_session() / 60.0))
                self.phase = "idle"
                end = t_cycle + self.a.cycle_sec
                while time.perf_counter() < end:
                    self.check_abort()
                    self.check_time()
                    time.sleep(min(1.0, max(0.0, end - time.perf_counter())))
                self.cycle += 1
        except TimeUp:
            self.log("the session's %.1f minutes passed (cycle %d, %s)" % (self.a.minutes, self.cycle,
                                                                            "complete" if self.cycles_done == self.cycle else "in progress"))

    # ------------------------------------------------------------ the end: log scan, summary

    def scan_player_log(self):
        res = {"exceptionLines": 0, "deviceLines": 0, "uuaTotalsMs": [], "exceptionKinds": {}, "renderer": None, "vram": None}
        path = getattr(self, "player_log", None)
        if not path or not os.path.isfile(path):
            return res
        with open(path, encoding="utf-8", errors="replace") as f:
            for line in f:
                s = line.rstrip()
                if EXC_LINE.match(s):
                    res["exceptionLines"] += 1
                    k = s.split(":")[0][:80]
                    res["exceptionKinds"][k] = res["exceptionKinds"].get(k, 0) + 1
                low = s.lower()
                if any(k in low for k in DEVICE_TEXT):
                    res["deviceLines"] += 1
                if s.startswith("Crash!!!") or "Native Crash Reporting" in s:
                    res["nativeCrash"] = True
                if s.startswith(" * ") and "Crashes" in s:
                    res["crashFolder"] = s[3:].strip()
                m = UUA_TOTAL.match(s)
                if m:
                    res["uuaTotalsMs"].append(float(m.group(1)))
                if s.strip().startswith("Renderer:") and res["renderer"] is None:
                    res["renderer"] = s.split(":", 1)[1].strip()
                if s.strip().startswith("VRAM:") and res["vram"] is None:
                    res["vram"] = s.split(":", 1)[1].strip()
                if s.strip().startswith("Version:") and "Direct3D" in s and "api" not in res:
                    res["api"] = s.split(":", 1)[1].strip()
        return res

    def write_tables(self):
        with open(os.path.join(self.out, "events.tsv"), "w", encoding="utf-8") as f:
            f.write("t_s\tcycle\tphase\top\twhat\tref\tok\trtMs\tsyncMs\tnote\n")
            for e in self.events:
                f.write("\t".join([fmt(e["t"]), str(e["cycle"]), e["phase"], e["op"], str(e["what"]), str(e["ref"] or ""),
                                   "1" if e["ok"] else "0", fmt(e["rtMs"]), fmt(e["syncMs"], 2), str(e["note"]).replace("\t", " ")]) + "\n")
        with open(os.path.join(self.out, "views.tsv"), "w", encoding="utf-8") as f:
            f.write("t_s\tcycle\tlist\tview\tcells\tsettleMs\tpendingLeft\n")
            for v in self.views:
                f.write("\t".join([fmt(v["t"]), str(v["cycle"]), v["list"], str(v["view"]), str(v["cells"]), fmt(v["settleMs"]),
                                   str(v["pendingLeft"])]) + "\n")

    def phase_frames(self):
        if self.poller is None:
            return {}
        recs, _ = self.poller.take(0)
        by = {}
        for t, ms, n, ph in recs:
            by.setdefault(ph, []).extend([ms] * n)
        out = {}
        for ph, d in sorted(by.items()):
            out[ph] = {"frames": len(d), "medianMs": median(d), "p95Ms": pct(d, 95), "p99Ms": pct(d, 99), "maxMs": max(d),
                       "over50": sum(1 for x in d if x > 50)}
        with open(os.path.join(self.out, "frames_by_phase.tsv"), "w", encoding="utf-8") as f:
            f.write("phase\tframes\tmedianMs\tp95Ms\tp99Ms\tmaxMs\tover50\n")
            for ph, s in out.items():
                f.write("%s\t%d\t%s\t%s\t%s\t%s\t%d\n" % (ph, s["frames"], fmt(s["medianMs"], 2), fmt(s["p95Ms"], 2), fmt(s["p99Ms"], 2),
                                                         fmt(s["maxMs"], 1), s["over50"]))
        return out

    def window(self, key, lo, hi):
        return [(r["session_min"], r.get(key)) for r in self.samples
                if r.get("session_min") is not None and lo <= r["session_min"] <= hi and r.get(key) is not None]

    def growth(self, key, early, late, slope_from, slope_to):
        """(max(late) - max(early), least-squares slope over [slope_from, slope_to]) in the column's unit per minute."""
        e = [v for _, v in self.window(key, *early)]
        l = [v for _, v in self.window(key, *late)]
        g = (max(l) - max(e)) if e and l else None
        return g, slope_per_min(self.window(key, slope_from, slope_to))

    def summarize(self, exit_code, stop_how, dev0, dev1, logscan, phases):
        s = self.samples
        work = [r for r in s if r.get("session_min") is not None]
        def peak(key, rows):
            v = [(r.get(key), r.get("session_min")) for r in rows if r.get(key) is not None]
            return max(v, key=lambda x: x[0]) if v else (None, None)
        def first(key, rows):
            for r in rows:
                if r.get(key) is not None:
                    return r.get(key)
            return None
        def last(key, rows):
            for r in reversed(rows):
                if r.get(key) is not None:
                    return r.get(key)
            return None
        minutes_done = (work[-1]["session_min"] if work else 0.0)
        full = self.a.minutes >= 60 and minutes_done >= 59.0
        ded_peak, ded_at = peak("gpuDedMB", s)
        shr_peak, shr_at = peak("gpuShrMB", s)
        ws_peak, ws_at = peak("wsMB", s)
        adp_peak, _ = peak("adpDedMB", s)
        lines = []
        ks = {}
        # KS1 / KS3: the whole run
        ks["KS1"] = ("PASS" if ded_peak is not None and ded_peak <= 6.0 * 1024 else "FAIL" if ded_peak is not None else "n/a",
                     "dedicated GPU memory peak %s MB at %s min (limit 6144)" % (fmt(ded_peak, 0), fmt(ded_at, 1)))
        ks["KS3"] = ("PASS" if shr_peak is not None and shr_peak <= 2.0 * 1024 else "FAIL" if shr_peak is not None else "n/a",
                     "shared GPU memory peak %s MB at %s min (limit 2048)" % (fmt(shr_peak, 0), fmt(shr_at, 1)))
        # KS2 / KS4: 15-30 vs 45-60 and the slope over 15-60; a shorter run reports its own window (5-10 vs the last 5 min)
        if full:
            g2, sl2 = self.growth("gpuDedMB", (15, 30), (45, 60), 15, 60)
            g4, sl4 = self.growth("wsMB", (15, 30), (45, 60), 15, 60)
            ks["KS2"] = ("PASS" if g2 is not None and sl2 is not None and g2 <= 256 and sl2 <= 3 else "FAIL" if g2 is not None else "n/a",
                         "dedicated growth (max 45-60 - max 15-30) %s MB (<= 256), slope 15-60 %s MB/min (<= 3)" % (fmt(g2, 0), fmt(sl2, 2)))
            ks["KS4"] = ("PASS" if g4 is not None and sl4 is not None and g4 <= 512 and sl4 <= 6 else "FAIL" if g4 is not None else "n/a",
                         "working-set growth %s MB (<= 512), slope 15-60 %s MB/min (<= 6)" % (fmt(g4, 0), fmt(sl4, 2)))
        else:
            end = minutes_done
            e0, e1 = (5.0, 10.0) if end >= 15 else (end * 0.25, end * 0.5)
            l0, l1 = max(e1, end - 5.0), end
            g2, sl2 = self.growth("gpuDedMB", (e0, e1), (l0, l1), e0, end)
            g4, sl4 = self.growth("wsMB", (e0, e1), (l0, l1), e0, end)
            ks["KS2"] = ("n/a", "run of %.1f min (needs 60): dedicated growth (max %.0f-%.0f - max %.0f-%.0f) %s MB, slope %.0f-%.0f %s MB/min"
                         % (end, l0, l1, e0, e1, fmt(g2, 0), e0, end, fmt(sl2, 2)))
            ks["KS4"] = ("n/a", "run of %.1f min (needs 60): working-set growth %s MB, slope %s MB/min" % (end, fmt(g4, 0), fmt(sl4, 2)))
        self.info["growth"] = {"dedMB": g2, "dedSlope": sl2, "wsMB": g4, "wsSlope": sl4}
        # KS5
        exc = max(self.log_exceptions, logscan.get("exceptionLines", 0))
        devl = max(self.log_device, logscan.get("deviceLines", 0))
        dnv = (dev1.get("nvlddmkm", 0) - dev0.get("nvlddmkm", 0)) if "nvlddmkm" in dev0 and "nvlddmkm" in dev1 else None
        d153 = (dev1.get("id153", 0) - dev0.get("id153", 0)) if "id153" in dev0 and "id153" in dev1 else None
        d4101 = (dev1.get("display4101", 0) - dev0.get("display4101", 0)) if "display4101" in dev0 and "display4101" in dev1 else None
        exit_ok = (self.player_exit == 0 and "killed" not in (stop_how or "")) if not self.a.attach else True
        ks5_ok = exc == 0 and devl == 0 and (d153 or 0) == 0 and (d4101 or 0) == 0 and exit_ok
        crash = logscan.get("nativeCrash")
        ks5_ok = ks5_ok and not crash
        ks["KS5"] = ("PASS" if ks5_ok else "FAIL", "managed exceptions %d, device-removed lines %d, nvlddmkm events +%s (id 153 +%s), "
                     "Display 4101 +%s, exit %s (%s)%s" % (exc, devl, fmt(dnv), fmt(d153), fmt(d4101), self.player_exit, stop_how,
                                                         ("; NATIVE CRASH (crash report under %s)" % logscan.get("crashFolder")) if crash else ""))
        # KS6
        if any(p.get("ownedMB") is not None for p in self.ks6):
            # ownership is released: after every later scene.new the owned bytes are not above the first value (scene.new +
            # the two targets) + 10 %; the unscoped loads stay constant after cycle 1
            ref = self.ks6[0].get("ownedMB")
            later = [p.get("ownedMB") for p in self.ks6[1:] if p.get("ownedMB") is not None]
            ok6 = ref is not None and all(v <= 1.1 * ref + 1e-6 for v in later)
            uns = sorted(set(r.get("cs_unscopedLoads") for r in work if (r.get("cycle") or 0) >= 2 and r.get("cs_unscopedLoads") is not None))
            ok6 = ok6 and len(uns) <= 1
            ks["KS6"] = ("PASS" if ok6 and later else "FAIL" if not ok6 else "n/a", "owned MB after the first scene.new + targets %s, after later scene.new %s (<= +10 %%); "
                         "unscoped loads after cycle 1: %s" % (fmt(ref), [fmt(v) for v in later], uns))
        else:
            ks["KS6"] = ("n/a", "no content.stats in this build; open bundles after scene.new: %s" % [p.get("openBundles") for p in self.ks6])
        # KS7
        dbm = [r.get("ap_dbMed") for r in work if r.get("ap_dbMed") is not None]
        dbp = [r.get("ap_dbP95") for r in work if r.get("ap_dbP95") is not None]
        if dbm:
            ks["KS7"] = ("PASS" if median(dbm) <= 1.0 and median(dbp or [0]) <= 2.0 else "FAIL",
                         "DynamicBone stage median %s ms, p95 %s ms per frame (median of the samples)" % (fmt(median(dbm), 3), fmt(median(dbp), 3)))
        else:
            ks["KS7"] = ("n/a", "no app.perf motion stages in this build")
        # KS8: the largest list touched, its first three views; the scroll frames
        big = None
        if self.views:
            sizes = {}
            for v in self.views:
                if v["cells"]:
                    sizes[v["list"]] = max(sizes.get(v["list"], 0), v["cells"])
            if sizes:
                big = max(sizes, key=lambda k: sizes[k])
        big_views = [v["settleMs"] for v in self.views if v["list"] == big][:3]
        first_views = [v["settleMs"] for v in self.views if v["view"] == 1 and v["settleMs"] is not None]
        scroll = phases.get("scroll") or {}
        if big_views:
            bv = [x if x is not None else 10000.0 for x in big_views]
            ok8 = median(bv) <= 500 and (scroll.get("p95Ms") or 0) <= 20
            ks["KS8"] = ("PASS" if ok8 else "FAIL",
                         "largest list touched %s (%s cells): first views %s ms (median %s; <= 500); first views of all lists median %s ms, "
                         "p95 %s ms; scroll frames %s, p95 %s ms (<= 20)" % (big, max(v["cells"] for v in self.views if v["list"] == big),
                                                                          [fmt(x, 0) for x in big_views], fmt(median(bv), 0),
                                                                          fmt(median(first_views), 0), fmt(pct(first_views, 95), 0),
                                                                          scroll.get("frames"), fmt(scroll.get("p95Ms"), 1)))
        else:
            ks["KS8"] = ("n/a", "no list view measured")
        # numbers
        ed = [e["syncMs"] for e in self.events if e["op"] == "edit" and e["ok"] and e["syncMs"] is not None]
        by_kind = {}
        for e in self.events:
            if e["op"] in ("edit", "acc.add", "acc.remove", "item.add", "map.load", "coord.apply", "chara.add", "item.delete") and e["ok"] and e["syncMs"] is not None:
                k = e["op"] if e["op"] != "edit" else "edit " + str(e["what"]).split(" ")[0]
                by_kind.setdefault(k, []).append(e["syncMs"])
        fails = [e for e in self.events if not e["ok"]]
        uua = logscan.get("uuaTotalsMs") or []
        status = self.status
        head = "SUMMARY: %s; %.1f of %g min, %d cycle(s) done; %s" % (status, minutes_done, self.a.minutes, self.cycles_done,
                                                                     ", ".join("%s %s" % (k, ks[k][0]) for k in sorted(ks)))
        lines.append(head)
        lines.append("run: stamp %s, seed %s, exe %s (modified %s), port %s, player pid %s, ready %s s, renderer %s (%s, VRAM %s), "
                     "wall bound %g min, frame poller %s ms (%s polls, %s errors)"
                     % (self.stamp, self.a.seed, self.exe, self.info.get("exeModified"), self.port, self.pid, self.info.get("readySeconds"),
                        logscan.get("renderer"), logscan.get("api"), logscan.get("vram"), self.a.wall_min, self.a.frame_poll_ms,
                        self.poller.polls if self.poller else 0, self.poller.errors if self.poller else 0))
        lines.append("gates: game gate %s (poll %g s)%s; locks %s; free RAM at start %s GB; device events before %s, after %s"
                     % (("%d process name(s)" % len(self.gate_names)) if self.gate_names else "OFF (%s)" % self.gate_warning,
                        self.gate_poll, " - ABORTED" if self.gate_abort else "", "off" if self.a.no_locks or self.a.attach else "taken and removed",
                        self.info.get("freeGbAtStart"), json.dumps(dev0), json.dumps(dev1)))
        if self.abort_reason:
            lines.append("stopped: " + self.abort_reason)
        for k in sorted(ks):
            lines.append("%s %s %s" % (k, ks[k][0], ks[k][1]))
        lines.append("numbers:")
        lines.append("  GPU dedicated MB: first %s, after setup %s, peak %s at %s min, last %s; adapter dedicated peak %s MB (luids %s)"
                     % (fmt(first("gpuDedMB", s), 0), fmt(first("gpuDedMB", work), 0), fmt(ded_peak, 0), fmt(ded_at, 1), fmt(last("gpuDedMB", s), 0),
                        fmt(adp_peak, 0), self.info.get("gpuLuids")))
        lines.append("  GPU shared MB: first %s, peak %s at %s min, last %s" % (fmt(first("gpuShrMB", s), 0), fmt(shr_peak, 0), fmt(shr_at, 1), fmt(last("gpuShrMB", s), 0)))
        lines.append("  working set MB: first %s, peak %s at %s min, last %s; private MB peak %s, last %s"
                     % (fmt(first("wsMB", s), 0), fmt(ws_peak, 0), fmt(ws_at, 1), fmt(last("wsMB", s), 0), fmt(peak("privMB", s)[0], 0), fmt(last("privMB", s), 0)))
        lines.append("  growth: dedicated %s MB (slope %s MB/min), working set %s MB (slope %s MB/min)"
                     % (fmt(g2, 0), fmt(sl2, 2), fmt(g4, 0), fmt(sl4, 2)))
        lines.append("  catalog (content.stats): open first %s / last %s, owned MB last %s, LRU MB last %s (budget %s), evictions %s, "
                     "unscoped loads %s, UnloadUnusedAssets calls %s (max %s ms), prefetch started %s / used %s / expired %s"
                     % (first("cs_open", work), last("cs_open", work), fmt(last("cs_ownedMB", work)), fmt(last("cs_lruMB", work)),
                        fmt(last("cs_budgetMB", work)), last("cs_evictions", work), last("cs_unscopedLoads", work),
                        last("cs_uuaCalls", work), fmt(last("cs_uuaMaxMs", work)), last("cs_pfStarted", work), last("cs_pfUsed", work),
                        last("cs_pfExpired", work)))
        lines.append("  open bundles (ui.state thumbs): first %s, peak %s, last %s; thumbnail-only bundles last %s"
                     % (first("openBundles", work), peak("openBundles", work)[0], last("openBundles", work), last("thumbOnlyBundles", work)))
        lines.append("  managed MB (after a full collection): first %s, peak %s, last %s; objects first / last: gameObjects %s / %s, materials %s / %s, "
                     "meshes %s / %s, textures %s / %s, render textures %s / %s"
                     % (fmt(first("managedMB", work)), fmt(peak("managedMB", work)[0]), fmt(last("managedMB", work)),
                        first("gameObjects", work), last("gameObjects", work), first("materials", work), last("materials", work),
                        first("meshes", work), last("meshes", work), first("textures", work), last("textures", work),
                        first("renderTextures", work), last("renderTextures", work)))
        lines.append("  frames by phase (poller, ms): " + "; ".join("%s %d fr median %s p95 %s max %s >50ms %d" % (ph, x["frames"], fmt(x["medianMs"], 1), fmt(x["p95Ms"], 1), fmt(x["maxMs"], 0), x["over50"])
                                                              for ph, x in phases.items() if not ph.startswith("_")))
        lines.append("  synchronous cost (ms, median / p95 / max): " + "; ".join("%s n %d %s / %s / %s" % (k, len(v), fmt(median(v), 1), fmt(pct(v, 95), 1), fmt(max(v), 0))
                                                                               for k, v in sorted(by_kind.items())))
        lines.append("  edits: %d ok, %d failed operations in all (%s)" % (len(ed), len(fails), "; ".join("%s %s %s" % (e["op"], e["what"], str(e["note"])[:80]) for e in fails[:6])))
        lines.append("  list views: %d (%d lists); first views settle median %s ms, p95 %s ms, max %s ms; scrolls that moved the shown cells %s, did not %s"
                     % (len(self.views), len(self.view_counts), fmt(median(first_views), 0), fmt(pct(first_views, 95), 0), fmt(max(first_views) if first_views else None, 0),
                        self.scroll_moved.get(True, 0), self.scroll_moved.get(False, 0)))
        lines.append("  UnloadUnusedAssets (player.log 'Total' lines): %d, median %s ms, max %s ms" % (len(uua), fmt(median(uua), 1), fmt(max(uua) if uua else None, 1)))
        lines.append("  KS6 points: " + "; ".join("%s: open %s owned %s managed %s objects %s" % (p["what"], p.get("openBundles"), fmt(p.get("ownedMB")), fmt(p.get("managedMB")), p.get("gameObjects")) for p in self.ks6))
        lines.append("  items: %s; maps: %s" % (json.dumps(self.info.get("itemRows")), json.dumps(self.info.get("maps"))))
        lines.append("  lists: %s" % json.dumps(self.info.get("lists"), ensure_ascii=False))
        if self.missing:
            lines.append("missing on this build: " + "; ".join("%s (%s)" % kv for kv in sorted(self.missing.items())))
        if logscan.get("exceptionKinds"):
            lines.append("exception kinds: " + json.dumps(logscan["exceptionKinds"]))
        lines.append(head)
        with open(os.path.join(self.out, "summary.txt"), "w", encoding="utf-8") as f:
            f.write("\n".join(lines) + "\n")
        j = {"summary": head, "ks": {k: {"result": v[0], "text": v[1]} for k, v in ks.items()}, "info": self.info,
             "phases": phases, "syncMs": {k: {"n": len(v), "median": median(v), "p95": pct(v, 95), "max": max(v)} for k, v in by_kind.items()},
             "ks6": self.ks6, "uuaTotalsMs": uua, "deviceBefore": dev0, "deviceAfter": dev1, "exit": exit_code,
             "abort": self.abort_reason, "missing": self.missing, "minutesDone": minutes_done, "cyclesDone": self.cycles_done}
        with open(os.path.join(self.out, "summary.json"), "w", encoding="utf-8") as f:
            json.dump(j, f, ensure_ascii=False, indent=1, default=str)
        for line in lines:
            self.log(line)
        return ks


class TimeUp(Exception):
    """The session's minutes have passed (not an error)."""


def run(a):
    S = Session(a)
    exit_code = 0
    dev0, dev1 = {}, {}
    stop_how = None
    S.player_exit = None
    S.quitting = False
    S.watch_stop = threading.Event()
    S.sampler_stop = threading.Event()
    S.poll_index = 0
    S.log("maker_session: out %s, minutes %g, wall %g min, port %d, seed %d" % (S.out, a.minutes, a.wall_min, a.port, a.seed))
    watch = None
    try:
        try:
            S.load_config()
            S.preflight()
        except SetupError as e:
            S.status = "refused: %s" % e
            S.log(S.status)
            with open(os.path.join(S.out, "summary.txt"), "w", encoding="utf-8") as f:
                f.write("SUMMARY: %s\n" % S.status)
            return 2
        dev0 = device_events()
        S.log("device events before: %s" % dev0)
        watch = threading.Thread(target=S.watcher, name="watcher", daemon=True)
        watch.start()
        # the adapters before the player (the desktop and other programs)
        g = gpu_counters(None)
        S.info["adaptersBeforeMB"] = {k: round(v["ded"] / MIB) for k, v in (g.get("adapters") or {}).items()}
        try:
            S.start_player()
            if a.frame_poll_ms > 0:
                S.poller = FramePoller(S, a.frame_poll_ms)
                S.poller.start()
            S.sampler = threading.Thread(target=S.sampler_loop, name="sampler", daemon=True)
            S.sampler.start()
            S.workload()
            S.status = "completed"
        except SetupError as e:
            S.status = "setup error: %s" % e
            S.log(S.status)
            exit_code = 2
        except Abort as e:
            S.status = "aborted by the game gate" if S.gate_abort else ("stopped by the memory guard" if S.memory_stop else "stopped: %s" % e)
            exit_code = 3 if S.gate_abort else 1
        except Exception as e:
            S.status = "error: %s: %s" % (type(e).__name__, str(e)[:300])
            S.log(traceback.format_exc())
            exit_code = 1
    except SetupError:
        exit_code = 2
    except Exception as e:
        S.status = "error: %s: %s" % (type(e).__name__, str(e)[:300])
        S.log(traceback.format_exc())
        exit_code = 1
    finally:
        S.phase = "quit"
        S.sampler_stop.set()
        if S.sampler is not None:
            S.sampler.join(120)
        if S.alive() and not S.abort_reason:
            try:
                S.sample(final=True)
            except Exception as e:
                S.log("final sample failed: %s" % e)
        if S.poller is not None:
            S.poller.stop_ev.set()
            S.poller.join(10)
        S.quitting = True
        if S.proc is not None:
            S.player_exit, stop_how = S.stop_player("end of the session" if not S.abort_reason else S.abort_reason)
        S.watch_stop.set()
        if watch is not None:
            watch.join(30)
        if S.locks is not None:
            S.locks.release()
            S.log("locks removed")
        if S.proc is not None or a.attach:
            dev1 = device_events()
            S.log("device events after: %s" % dev1)
            try:
                logscan = S.scan_player_log()
            except Exception as e:
                logscan = {}
                S.log("player.log not read: %s" % e)
            S.write_tables()
            phases = S.phase_frames()
            if S.poller is not None:
                recs, _ = S.poller.take(0)
                phases["_all"] = frame_stats(recs)
            try:
                ks = S.summarize(exit_code, stop_how, dev0, dev1, logscan, phases)
                if exit_code == 0 and any(v[0] == "FAIL" for v in ks.values()):
                    exit_code = 1
            except Exception as e:
                S.log("summary failed: %s\n%s" % (e, traceback.format_exc()))
                exit_code = exit_code or 1
        S.logf.close()
    return exit_code


def parse_args(argv):
    ap = argparse.ArgumentParser(description="The bridge-driven Maker session in the HS2Next app player (numbers only).")
    ap.add_argument("--exe", help="HS2Next.exe (default <project>\\Builds\\App\\HS2Next.exe)")
    ap.add_argument("--project", help="project root (default the folder above Tools)")
    ap.add_argument("--out", help="output folder (default <project>\\Spike\\out\\session\\<stamp>)")
    ap.add_argument("--minutes", type=float, default=60.0)
    ap.add_argument("--wall-min", type=float, default=None, help="the whole run's bound (default minutes + 15)")
    ap.add_argument("--port", type=int, default=DEFAULT_PORT)
    ap.add_argument("--seed", type=int, default=DEFAULT_SEED)
    ap.add_argument("--sample-sec", type=float, default=30.0)
    ap.add_argument("--cycle-sec", type=float, default=300.0)
    ap.add_argument("--frame-poll-ms", type=float, default=1.0, help="0 = no frame poller")
    ap.add_argument("--wheel-delta", type=float, default=12.0)
    ap.add_argument("--hover-dwell-ms", type=float, default=500.0,
                    help="how long the pointer rests on a hovered cell before the edit (the list prefetches after 150 ms)")
    ap.add_argument("--no-hover-search", dest="hover_search", action="store_false",
                    help="hover without finding the row through the list's search field first")
    ap.add_argument("--gpu-guard-mb", type=float, default=7000.0, help="end the run at this dedicated GPU memory (0 = off)")
    ap.add_argument("--shared-guard-mb", type=float, default=4096.0, help="end the run at this shared GPU memory (0 = off)")
    ap.add_argument("--lock-dir")
    ap.add_argument("--lock-wait-min", type=float, default=0.0)
    ap.add_argument("--no-locks", action="store_true", help="diagnosis only: no GPU / input lock")
    ap.add_argument("--ram-wait-min", type=float, default=10.0)
    ap.add_argument("--min-free-gb", type=float, default=8.0)
    ap.add_argument("--ready-timeout-min", type=float, default=12.0)
    ap.add_argument("--items", help="item groups file (default Tools\\session_items.json when present)")
    ap.add_argument("--label", help="the lock line's label")
    ap.add_argument("--player-arg", dest="player_args", action="append", default=[],
                    help="one more player argument, repeatable; write it with '=' when it starts with '-' (--player-arg=-noTexturePrescan)")
    ap.add_argument("--attach", action="store_true", help="a player already serving on --port (no start, quit or locks)")
    a = ap.parse_args(argv)
    if a.wall_min is None:
        a.wall_min = a.minutes + 15.0
    if a.attach:
        a.no_locks = True
    return a


def main(argv):
    for s in (sys.stdout, sys.stderr):
        try:
            s.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, ValueError):
            pass
    return run(parse_args(argv))


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
