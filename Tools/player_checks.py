"""
player_checks.py - external checks of the HS2Next bridge against the built app player (Phase 2.5, package Q).

Settles bridge prep T1, T4, T7 and T8 in the player:
the script starts the player, talks to its bridge over real HTTP from outside the process, and ends it with app.quit.
It never uses the Unity Editor. Python 3.14, standard library only.

Usage (the run_tests.ps1 row calls exactly the first form):
    python -I Tools\\player_checks.py --exe <path to HS2Next.exe> --project <project root> --out <dir>
                                     [--port 17399] [--timeout-min 20]
    more options: --only T1,T4,T7,T8 (default: all), --edge <msedge.exe>, --no-edge, --edge-timeout 90 (s per Edge run),
                  --keep-captures, --big-mb 10 (T4's text), --min-free-gb 8, --ram-wait-min 10, --ready-timeout-min 12,
                  --attach (use a bridge already serving on --port, e.g. BridgeServe in the Editor: no start, no quit,
                  no firewall comparison)
    -I ignores PYTHONUTF8, so the script sets its own stdout/stderr to UTF-8.
    Every request uses a fresh connection that connects blocking (Python's timed connect adds ~14 ms on Windows
    loopback; see _Conn), so the latencies are the bridge's.

Run:
  1. Setup: the exe and <project>\\Config\\settings.local.json must exist and the port must be free. Snapshot of the
     Windows Firewall rules whose program is the exe (READ ONLY: Get-NetFirewallApplicationFilter / Get-NetFirewallRule,
     netsh as fallback; the script never adds, changes or removes a rule). Waits up to --ram-wait-min for
     --min-free-gb of free RAM (then goes on with a RAM WARN line).
  2. Starts the player: <exe> -batchmode -bridgePort <port> -bridgeAllowQuit
     -settings <project>\\Config\\settings.local.json -cacheDir <project>\\Cache -userData <out>\\userdata
     -bridgeOutputDir <out>\\bridge_out -logFile <out>\\player.log (absolute paths; a graphics device exists in batch
     mode, so never -nographics). Ready = ping.appReady and <out>\\userdata\\Bridge\\endpoint.json naming the started PID.
  3. The checks below, then app.quit and the wait for the exit. Past --timeout-min (or when the player does not exit
     after app.quit) the script kills only the PID it started.

Checks (one report line each; scene content: vanilla item 399, vanilla map 10 and the SFW female visual target of
Tools/targets.json; captures are checked by size only):
  SETUP           ready time, help count, versions
  T1.listen       the player's listening sockets (the system's TCP listener table, netstat -ano, Get-NetTCPConnection /
                  Get-NetUDPEndpoint) at ready and at the end: TCP listeners bound to 127.0.0.1 only (FAIL on 0.0.0.0 / ::);
                  non-loopback UDP = WARN. A listener of the script's own is the control of the table
  T1.firewall     firewall rules naming the exe before and after the run: no new rule. A firewall prompt itself cannot
                  be observed unattended; none is expected because the bridge binds the loopback address only.
  T1.busy.chara   pings answered while chara.load builds the card on the main thread (frame counter stalled)
  T1.busy.map     pings answered while a waiting map.load holds the queue; a scene request behind it answers after it
                  (FIFO, as E3)
  T4.float.doc    float32 values written with obj.transform (shortest float32 text and exact double text) read back
                  bit for bit from scene.doc {key} (canonical writer); -0 is normalized to +0 by design
  T4.float.reply  the same through the bridge's own float form (camera.set reply and camera.get)
  T4.big.id       a ~10 MB non-ASCII request id (Japanese, emoji / surrogate pairs, escapes) echoed byte for byte
  T4.big.title    the same text as the scene title (scene.save {title}) read back byte for byte from scene.doc
  T4.reload       save -> scene.new -> scene.load: scene.doc SHA-256, node count and title equal (save format T15)
  T4.resave       save (fixed clock, placeholder thumbnail) -> load -> save: the two files byte-identical
  T7.origin, T7.secfetch, T7.host
                  raw http.client POSTs of obj.transform with Origin (http://example.com, null) -> 403, Sec-Fetch-Site:
                  cross-site / Sec-Fetch-Mode: cors -> 403, a wrong Host name -> 400 (Mono's listener; localhost too);
                  Host 127.0.0.1:<other port> passes Mono's host-name match and gets the bridge's own 403 (400 or 403
                  accepted for the localhost and other-port forms); no Access-Control-* header on any refusal
  T7.sidefx       scene.doc and history unchanged after the raw requests
  T7.edge.file, T7.edge.loopback, T7.edge.public
                  a local HTML page in headless Microsoft Edge (fresh profile under <out>\\t7, net log, DOM dump) whose
                  fetch() (JSON / text-plain / no-cors) and a text/plain form POST send scene.new to the bridge: from
                  file://, from a page on another loopback port, and from the same page marked public
                  (--ip-address-space-overrides). Refused (403 seen in the net log, or blocked by the browser: recorded)
                  and no side effect. WARN when Edge cannot run headless.
  T8.ping         1,000 sequential pings: all answered with their id; latency median / p95 / max
  T8.capture      100 captures of varying small sizes: every file exists with the requested PNG size; times
  T8.concurrent   4 clients x 250 mixed ping / scene requests with ids: no lost, mixed or reordered replies
  T8.mutate       obj.transform on the test item answers within about one frame (ping frame counter before / after)
  LOG             exceptions in player.log (WARN only)
  QUIT            app.quit answered and the player exited with code 0
  RAM             (only when the free-RAM wait ran out) WARN

Outputs:
  <out>\\player_checks_report.txt  first and last line "SUMMARY: N PASS, M FAIL", second line "WARN: K (ids)", then one
                                   line per check "<id> PASS|FAIL|WARN <numbers>"
  <out>\\player_checks.json        details of every check
  <out>\\player.log, <out>\\userdata\\ (scene files of T4), <out>\\bridge_out\\ (captures; T8's are removed unless
  --keep-captures), <out>\\t7\\ (page, DOM dumps, Edge net logs; the Edge profiles are removed)
Exit code: 0 every check passed (WARN allowed), 1 a check failed, 2 setup error (exe or settings missing, port taken,
the player exited or did not become ready).
Ports: 17399 by default (17397 = the player acceptance, 17398 = the Editor play test, 17322 = interactive default,
17321 = HS2's own bridge). Always 127.0.0.1, never localhost (Mono's listener answers 400 to Host: localhost).
"""
import argparse
import ctypes
import hashlib
import html
import http.client
import http.server
import json
import os
import re
import shutil
import socket
import statistics
import struct
import subprocess
import sys
import threading
import time
import traceback
import urllib.parse
from datetime import datetime, timezone

DEFAULT_PORT = 17399
EDGE_DEFAULTS = (r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
                 r"C:\Program Files\Microsoft\Edge\Application\msedge.exe")
ITEM_NO = 399
MAP_NO = 10
CREATE_NO_WINDOW = 0x08000000
TCP_TABLE_OWNER_PID_LISTENER = 3
LOOPBACK = ("127.0.0.1", "::1", "[::1]")
T0 = time.perf_counter()


def log(msg):
    print("[%7.1f s] %s" % (time.perf_counter() - T0, msg), flush=True)


def utc_now():
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


class SetupError(Exception):
    pass


class Abort(Exception):
    """The player is gone (exited, killed by the watchdog): the remaining checks cannot run."""


class HttpError(RuntimeError):
    def __init__(self, cmd, status, body):
        super().__init__("%s -> HTTP %s %s" % (cmd, status, body[:200].decode("utf-8", "replace")))
        self.status = status
        self.body = body


class CmdError(RuntimeError):
    def __init__(self, cmd, reply):
        super().__init__("%s -> %s (%s)" % (cmd, reply.get("error"), reply.get("code")))
        self.reply = reply


class Reply(dict):
    ms = 0.0


# ---------------------------------------------------------------- HTTP client (http.client: every header controlled)

class _Conn(http.client.HTTPConnection):
    """Connects blocking, then applies the timeout to the exchange. Python's timed connect (non-blocking connect +
    select) costs about 14 ms per connection on Windows loopback; a blocking one 0.1 ms (measured against the player),
    so latencies measure the bridge, not the client. A refused loopback connect still fails within about 2 s."""

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

    def raw(self, body, headers=None, host=None, timeout=None, method="POST", path="/", content_type=True):
        """One exchange on a fresh connection: (status, headers dict, body bytes, ms). host replaces the Host header."""
        conn = _Conn("127.0.0.1", self.port, timeout=timeout or self.timeout)
        t = time.perf_counter()
        try:
            conn.putrequest(method, path, skip_host=host is not None, skip_accept_encoding=True)
            if host is not None:
                conn.putheader("Host", host)
            hdrs = {"Content-Type": "application/json; charset=utf-8"} if content_type else {}
            hdrs.update(headers or {})
            for k, v in hdrs.items():
                if v is not None:
                    conn.putheader(k, v)
            if body is not None:
                conn.putheader("Content-Length", str(len(body)))
            conn.endheaders(body)
            resp = conn.getresponse()
            data = resp.read()
            return resp.status, {k: v for k, v in resp.getheaders()}, data, (time.perf_counter() - t) * 1000.0
        finally:
            conn.close()

    def call(self, req, timeout=None, check=True, cmd=None):
        if isinstance(req, (bytes, bytearray)):
            body = bytes(req)
        elif isinstance(req, str):
            body = req.encode("utf-8")
        else:
            body = json.dumps(req, ensure_ascii=False).encode("utf-8")
            cmd = cmd or req.get("cmd")
        cmd = cmd or "?"
        status, _, data, ms = self.raw(body, timeout=timeout)
        if status != 200:
            raise HttpError(cmd, status, data)
        r = Reply(json.loads(data.decode("utf-8")))
        r.ms = ms
        if check and not r.get("ok"):
            raise CmdError(cmd, r)
        return r


# ---------------------------------------------------------------- small helpers

def stats(xs):
    if not xs:
        return {"n": 0}
    s = sorted(xs)
    n = len(s)
    p95 = s[min(n - 1, int(round(0.95 * (n - 1))))]
    return {"n": n, "median": round(statistics.median(s), 2), "p95": round(p95, 2), "max": round(s[-1], 2),
            "min": round(s[0], 2), "mean": round(sum(s) / n, 2)}


def fmt_ms(st):
    if not st.get("n"):
        return "no samples"
    return "median %.1f / p95 %.1f / max %.1f ms" % (st["median"], st["p95"], st["max"])


def f32(x):
    return struct.unpack("<f", struct.pack("<f", x))[0]


def f32bits(x):
    return struct.unpack("<I", struct.pack("<f", x))[0]


def f32_short(x):
    """The shortest %g text that reads back as the same float32 (what a float32 printer would write)."""
    for p in range(1, 10):
        s = "%.*g" % (p, x)
        try:
            if f32bits(float(s)) == f32bits(x):
                return s
        except OverflowError:          # e.g. 3.403e+38 for the float32 maximum: beyond the range, try more digits
            continue
    return repr(x)


def png_size(path):
    with open(path, "rb") as f:
        head = f.read(24)
    if len(head) < 24 or head[:8] != b"\x89PNG\r\n\x1a\n" or head[12:16] != b"IHDR":
        return None
    return struct.unpack(">II", head[16:24])


def sha256_text(s):
    return hashlib.sha256(s.encode("utf-8", "surrogatepass")).hexdigest()


def first_diff(a, b):
    n = min(len(a), len(b))
    for i in range(n):
        if a[i] != b[i]:
            return i
    return n if len(a) != len(b) else -1


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


def ps_quote(s):
    return "'" + s.replace("'", "''") + "'"


def powershell(script, timeout=180):
    """Runs a PowerShell script; (exit code, stdout text, stderr text). Output forced to UTF-8."""
    exe = shutil.which("pwsh") or shutil.which("powershell") or "powershell.exe"
    full = "[Console]::OutputEncoding = [Text.Encoding]::UTF8; $ProgressPreference = 'SilentlyContinue'; " + script
    p = subprocess.run([exe, "-NoProfile", "-NonInteractive", "-Command", full], capture_output=True,
                       timeout=timeout, creationflags=CREATE_NO_WINDOW)
    return p.returncode, p.stdout.decode("utf-8", "replace"), p.stderr.decode("utf-8", "replace")


def ps_json(script, timeout=180):
    code, out, err = powershell(script, timeout)
    text = out.strip()
    if code != 0 and not text:
        raise RuntimeError("powershell exit %s: %s" % (code, err.strip()[:300]))
    if not text:
        return []
    v = json.loads(text)
    return v if isinstance(v, list) else [v]


def port_listeners(port):
    """PIDs listening on TCP <port> (any address), from the listener table and netstat."""
    pids = {pid for addr, p, pid in tcp_listener_rows() if p == port}
    for proto, addr, p, pid, listening in netstat_rows():
        if proto == "TCP" and listening and p == port:
            pids.add(pid)
    return pids


def tcp_listener_rows():
    """(addr, port, pid) of every TCP listener, IPv4 and IPv6, from GetExtendedTcpTable's listener class. The class selects
    the rows, so the state column is not read: a system can report a wrong state there, and netstat -ano then hides the row
    and Get-NetTCPConnection -State Listen leaves it out."""
    api = ctypes.WinDLL("iphlpapi")
    rows = []
    for family in (socket.AF_INET, socket.AF_INET6):
        need = ctypes.c_ulong(0)
        api.GetExtendedTcpTable(None, ctypes.byref(need), False, int(family), TCP_TABLE_OWNER_PID_LISTENER, 0)
        buf = ctypes.create_string_buffer(need.value + 4096)
        need = ctypes.c_ulong(len(buf))
        code = api.GetExtendedTcpTable(buf, ctypes.byref(need), False, int(family), TCP_TABLE_OWNER_PID_LISTENER, 0)
        if code:
            raise OSError("GetExtendedTcpTable: %d" % code)
        raw = buf.raw
        count = struct.unpack_from("<I", raw, 0)[0]
        for i in range(count):
            if family == socket.AF_INET:
                at = 4 + i * 24
                addr = socket.inet_ntop(family, raw[at + 4:at + 8])
                port, pid = struct.unpack_from("<I", raw, at + 8)[0], struct.unpack_from("<I", raw, at + 20)[0]
            else:
                at = 4 + i * 56
                addr = socket.inet_ntop(family, raw[at:at + 16])
                port, pid = struct.unpack_from("<I", raw, at + 20)[0], struct.unpack_from("<I", raw, at + 52)[0]
            rows.append((addr, socket.ntohs(port & 0xFFFF), pid))
    return rows


def listener_table_works():
    """A listener of this script must show in the table: the control of what T1.listen reads."""
    s = socket.socket()
    try:
        s.bind(("127.0.0.1", 0))
        s.listen(1)
        port = s.getsockname()[1]
        return ("127.0.0.1", port, os.getpid()) in tcp_listener_rows()
    finally:
        s.close()


def netstat_rows():
    p = subprocess.run(["netstat", "-ano"], capture_output=True, timeout=60, creationflags=CREATE_NO_WINDOW)
    rows = []
    for line in p.stdout.decode("oem", "replace").splitlines():
        t = line.split()
        if not t or t[0] not in ("TCP", "UDP"):
            continue
        try:
            if t[0] == "TCP" and len(t) >= 5:
                local, foreign, pid = t[1], t[2], int(t[-1])
                listening = foreign in ("0.0.0.0:0", "[::]:0")
            elif t[0] == "UDP" and len(t) >= 4:
                local, pid, listening = t[1], int(t[-1]), True
            else:
                continue
            addr, _, port = local.rpartition(":")
            rows.append((t[0], addr, int(port), pid, listening))
        except ValueError:
            continue
    return rows


def is_loopback(addr):
    a = addr.strip("[]")
    return a in ("127.0.0.1", "::1") or a.startswith("127.")


# ---------------------------------------------------------------- results

class Results:
    def __init__(self):
        self.items = []

    def add(self, cid, verdict, text, **data):
        self.items.append({"id": cid, "verdict": verdict, "text": text, "data": data})
        log("%-16s %-4s %s" % (cid, verdict, text))

    def count(self, v):
        return sum(1 for x in self.items if x["verdict"] == v)


# ---------------------------------------------------------------- the run context

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
        self.item = None
        self.listen = []
        self.fw_before = None
        self.fw_after = None
        self.info = {}
        self.only = set(x.strip().upper() for x in a.only.split(",")) if a.only else {"T1", "T4", "T7", "T8"}

    def want(self, t):
        return t in self.only

    def alive(self):
        if self.aborted:
            raise Abort(self.aborted)
        if self.proc is not None and self.proc.poll() is not None:
            self.aborted = "the player exited with code %s" % self.proc.returncode
            raise Abort(self.aborted)

    def call(self, req, timeout=120, check=True, cmd=None):
        self.alive()
        try:
            return self.br.call(req, timeout=timeout, check=check, cmd=cmd)
        except (OSError, http.client.HTTPException):
            self.alive()
            raise


def guard(C, cid, fn):
    """Runs one check function; an exception is a FAIL of <cid>; a dead player stops the run."""
    try:
        fn(C)
    except Abort:
        raise
    except Exception as e:
        tb = traceback.format_exc(limit=6)
        C.R.add(cid, "FAIL", "exception: %s: %s" % (type(e).__name__, str(e)[:300]), traceback=tb)
        C.alive()


# ---------------------------------------------------------------- firewall (read only)

def firewall_rules(exe):
    """Rules whose program is any '...\\<exe name>' (exact = the given path). Read only."""
    name = os.path.basename(exe)
    script = (
        "$p = %s; $n = %s; "
        "$all = @(Get-NetFirewallApplicationFilter -ErrorAction Stop | Where-Object { $_.Program -and "
        "([Environment]::ExpandEnvironmentVariables($_.Program)) -like ('*\\' + $n) }); "
        "$rows = @(foreach ($f in $all) { foreach ($x in @($f | Get-NetFirewallRule -ErrorAction SilentlyContinue)) { "
        "[pscustomobject]@{ id = [string]$x.Name; name = [string]$x.DisplayName; dir = [string]$x.Direction; "
        "action = [string]$x.Action; enabled = [string]$x.Enabled; profile = [string]$x.Profile; "
        "program = [string]$f.Program; exact = ([Environment]::ExpandEnvironmentVariables($f.Program) -ieq $p) } } }); "
        "ConvertTo-Json -Compress -Depth 3 -InputObject $rows"
    ) % (ps_quote(exe), ps_quote(name))
    try:
        return {"source": "Get-NetFirewallRule", "rules": ps_json(script)}
    except Exception as e:
        ps_error = str(e)
    # fallback: netsh (localized labels; blocks that mention the exe name)
    p = subprocess.run(["netsh", "advfirewall", "firewall", "show", "rule", "name=all", "verbose"],
                       capture_output=True, timeout=180, creationflags=CREATE_NO_WINDOW)
    text = p.stdout.decode("oem", "replace")
    rules = []
    for block in re.split(r"\r?\n\s*\r?\n", text):
        if name.lower() in block.lower():
            first = block.strip().splitlines()[0] if block.strip() else ""
            rules.append({"id": first.strip(), "name": first.split(":", 1)[-1].strip(),
                          "exact": exe.lower() in block.lower(), "program": exe if exe.lower() in block.lower() else name})
    return {"source": "netsh (PowerShell failed: %s)" % ps_error[:200], "rules": rules}


# ---------------------------------------------------------------- setup / start / quit

def setup(C):
    a = C.a
    os.makedirs(C.out, exist_ok=True)
    if a.attach:
        r = C.br.call({"cmd": "ping"}, timeout=10, check=False)
        pids = port_listeners(C.port)
        C.pid = next(iter(pids)) if len(pids) == 1 else None
        C.info["attach"] = {"pids": sorted(pids)}
        if not r.get("appReady"):
            raise SetupError("attach: the bridge on port %d is not ready: %s" % (C.port, json.dumps(r)[:300]))
        return finish_setup(C, 0.0)
    if not C.exe or not os.path.isfile(C.exe):
        raise SetupError("exe not found: %s" % C.exe)
    if not C.project or not os.path.isdir(C.project):
        raise SetupError("project root not found: %s" % C.project)
    settings = os.path.join(C.project, "Config", "settings.local.json")
    if not os.path.isfile(settings):
        raise SetupError("settings file not found: %s" % settings)
    cache = os.path.join(C.project, "Cache")
    busy = port_listeners(C.port)
    if busy:
        raise SetupError("port %d is already in use (PID %s)" % (C.port, ", ".join(map(str, sorted(busy)))))
    # RAM
    need, waited, end = a.min_free_gb, 0.0, time.time() + a.ram_wait_min * 60
    g = free_gb()
    C.info["freeGbAtStart"] = round(g, 1) if g is not None else None
    while g is not None and g < need and time.time() < end:
        log("free RAM %.1f GB < %.1f GB: waiting" % (g, need))
        time.sleep(15)
        waited += 15
        g = free_gb()
    if g is not None and g < need:
        C.R.add("RAM", "WARN", "free RAM %.1f GB < %.1f GB after %d s; started anyway" % (g, need, waited))
    C.info["freeGb"] = round(g, 1) if g is not None else None
    # firewall before (read only)
    if C.want("T1"):
        try:
            C.fw_before = firewall_rules(C.exe)
        except Exception as e:
            C.fw_before = {"error": str(e)}
    ud = os.path.join(C.out, "userdata")
    bo = os.path.join(C.out, "bridge_out")
    logf = os.path.join(C.out, "player.log")
    ep = os.path.join(ud, "Bridge", "endpoint.json")
    if os.path.isfile(ep):
        try:
            os.remove(ep)
        except OSError:
            pass
    args = [C.exe, "-batchmode", "-bridgePort", str(C.port), "-bridgeAllowQuit", "-settings", settings,
            "-cacheDir", cache, "-userData", ud, "-bridgeOutputDir", bo, "-logFile", logf]
    C.info["commandLine"] = args
    C.passed = {"settings": settings, "userData": ud, "cacheDir": cache, "outputDir": bo}
    log("starting the player: " + subprocess.list2cmdline(args))
    t_start = time.time()
    C.proc = subprocess.Popen(args, cwd=os.path.dirname(C.exe), stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL,
                              stderr=subprocess.DEVNULL, creationflags=CREATE_NO_WINDOW)
    C.pid = C.proc.pid
    deadline = min(C.deadline, t_start + a.ready_timeout_min * 60)
    last, last_err = None, None
    while True:
        if C.proc.poll() is not None:
            raise SetupError("the player exited with code %s before it was ready (see %s)" % (C.proc.returncode, logf))
        if C.aborted:
            raise SetupError("timed out (--timeout-min) before the player was ready")
        try:
            r = C.br.call({"cmd": "ping"}, timeout=5, check=False)
            last = r
            if r.get("appReady"):
                break
            det = r.get("readyDetail")
            if det and "startup failed" in json.dumps(det, ensure_ascii=False):
                raise SetupError("startup failed: %s" % json.dumps(det, ensure_ascii=False)[:400])
        except (OSError, http.client.HTTPException, ValueError, HttpError) as e:
            last_err = "%s: %s" % (type(e).__name__, e)
        if C.aborted:
            raise SetupError("before the player was ready: " + C.aborted)
        if time.time() > deadline:
            raise SetupError("not ready after %.0f s; last ping %s; last error %s"
                             % (time.time() - t_start, json.dumps(last, ensure_ascii=False)[:300] if last else None, last_err))
        time.sleep(0.5)
    ready_s = time.time() - t_start
    # the bridge that answered is the started process
    pid_seen = None
    for _ in range(20):
        try:
            with open(ep, encoding="utf-8") as f:
                pid_seen = json.load(f).get("pid")
            break
        except (OSError, ValueError):
            time.sleep(0.25)
    C.info["endpoint"] = {"file": ep, "pid": pid_seen}
    if pid_seen != C.pid:
        raise SetupError("endpoint.json names PID %s, the started player is PID %s" % (pid_seen, C.pid))
    finish_setup(C, ready_s)


def finish_setup(C, ready_s):
    p = C.call({"cmd": "ping"})
    v = C.call({"cmd": "bridge.version"})
    h = C.call({"cmd": "help"})
    n = h.get("count") or len(h.get("commands") or [])
    C.info["ping"] = {k: p.get(k) for k in ("protocol", "app", "bridge", "api", "ready", "frame", "realtime", "screen")}
    C.info["version"] = {k: v.get(k) for k in v if k not in ("ok", "cmd")}
    C.info["helpCount"] = n
    verdict = "PASS" if n >= 50 else "FAIL"
    C.R.add("SETUP", verdict, "ready in %.1f s (pid %s, port %d); help %d commands (>= 50); bridge %s, unity %s, screen %s"
            % (ready_s, C.pid, C.port, n, v.get("bridge"), v.get("unity"), p.get("screen")),
            readySeconds=round(ready_s, 2), helpCount=n, pid=C.pid)
    if not C.a.attach and getattr(C, "passed", None):
        guard(C, "PATHS", paths_check)


def paths_check(C):
    """PATHS (Phase 5.3): app.paths names the folders this script passed (-settings, -userData,
    -cacheDir, -bridgeOutputDir) with the source "arg" each (an explicit folder wins over the data root's defaults)."""
    r = C.call({"cmd": "app.paths"})
    src = r.get("sources") or {}

    def same(a, b):
        return bool(a) and bool(b) and os.path.normcase(os.path.abspath(a)).rstrip("\\/") == os.path.normcase(os.path.abspath(b)).rstrip("\\/")

    bad = []
    for key, member in (("settings", "settingsFile"), ("userData", "userData"), ("cacheDir", "cacheDir"), ("outputDir", "outputDir")):
        want = C.passed.get(key)
        if not same(r.get(member), want):
            bad.append("%s %s (passed %s)" % (member, r.get(member), want))
        if src.get(key) != "arg":
            bad.append("source of %s %s (expected arg)" % (key, src.get(key)))
    C.R.add("PATHS", "FAIL" if bad else "PASS",
            ("app.paths: mode %s; settings, userData, cacheDir and outputDir are the folders passed, each from the command line (arg)"
             % r.get("mode")) if not bad else "app.paths: " + "; ".join(bad),
            mode=r.get("mode"), sources=src)


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
    dt = time.time() - t
    ok = reply is not None and reply.get("quitting") is True and not killed and code == 0
    C.R.add("QUIT", "PASS" if ok else "FAIL",
            "app.quit %s; exit code %s in %.1f s%s" % ("answered quitting:true" if reply and reply.get("quitting") else "failed (%s)" % err,
                                                       code, dt, "; killed after 60 s" if killed else ""),
            exitCode=code, seconds=round(dt, 2), killed=killed)


def kill_player(C):
    if C.proc is not None and C.proc.poll() is None:
        try:
            C.proc.kill()      # TerminateProcess on the started PID only
            C.proc.wait(30)
        except Exception:
            pass


# ---------------------------------------------------------------- T1

def t1_listen_sample(C, label):
    if C.pid is None:
        C.listen.append({"label": label, "error": "no PID known"})
        return
    rows = [(proto, addr, port) for proto, addr, port, pid, listening in netstat_rows() if pid == C.pid and listening]
    table, control = [], None
    try:
        table = [("TCP", addr, port) for addr, port, pid in tcp_listener_rows() if pid == C.pid]
        control = listener_table_works()
    except Exception as e:
        control = "listener table: %s" % e
    ps = None
    try:
        ps = ps_json(
            "$o = @(); try { $o += @(Get-NetTCPConnection -State Listen -OwningProcess %d -ErrorAction Stop | ForEach-Object { "
            "[pscustomobject]@{ proto = 'TCP'; addr = [string]$_.LocalAddress; port = [int]$_.LocalPort } }) } catch {}; "
            "try { $o += @(Get-NetUDPEndpoint -OwningProcess %d -ErrorAction Stop | ForEach-Object { "
            "[pscustomobject]@{ proto = 'UDP'; addr = [string]$_.LocalAddress; port = [int]$_.LocalPort } }) } catch {}; "
            "ConvertTo-Json -Compress -Depth 3 -InputObject $o" % (C.pid, C.pid), timeout=120)
    except Exception as e:
        ps = {"error": str(e)}
    C.listen.append({"label": label, "netstat": [list(r) for r in rows], "table": [list(r) for r in table], "control": control,
                     "powershell": ps})


def t1_listen_verdict(C):
    tcp_bad, udp_bad, tcp_seen, notes = set(), set(), set(), []
    for s in C.listen:
        if "error" in s:
            notes.append("%s: %s" % (s["label"], s["error"]))
            continue
        entries = {(r[0], str(r[1]).strip("[]"), int(r[2])) for r in s["netstat"] + s["table"]}
        if s["control"] is not True:
            notes.append("%s: the listener table missed this script's own listener (%s)" % (s["label"], s["control"]))
        if isinstance(s.get("powershell"), list):
            entries |= {(e.get("proto"), str(e.get("addr")).strip("[]"), int(e.get("port") or 0)) for e in s["powershell"]}
        elif isinstance(s.get("powershell"), dict):
            notes.append("%s: PowerShell %s" % (s["label"], s["powershell"].get("error", "?")[:120]))
        for proto, addr, port in entries:
            key = "%s %s:%s" % (proto, addr, port)
            if proto == "TCP":
                tcp_seen.add(key)
                if not is_loopback(addr):
                    tcp_bad.add("%s (%s)" % (key, s["label"]))
            elif not is_loopback(addr):
                udp_bad.add("%s (%s)" % (key, s["label"]))
    tcp_bad, udp_bad = sorted(tcp_bad), sorted(udp_bad)
    expected = "TCP 127.0.0.1:%d" % C.port
    has_bridge = expected in tcp_seen
    if tcp_bad:
        v = "FAIL"
    elif not has_bridge:
        v = "FAIL"
    elif udp_bad or notes:
        v = "WARN"
    else:
        v = "PASS"
    C.R.add("T1.listen", v, "TCP listeners %s at %s; non-loopback TCP %d%s; non-loopback UDP %d%s%s"
            % (", ".join(sorted(tcp_seen)) or "none", "/".join(s["label"] for s in C.listen),
               len(tcp_bad), (" (" + ", ".join(tcp_bad) + ")") if tcp_bad else "",
               len(udp_bad), (" (" + ", ".join(udp_bad) + ")") if udp_bad else "",
               ("; " + "; ".join(notes)) if notes else ""),
            samples=C.listen)


def t1_firewall_verdict(C):
    note = ("a firewall prompt itself cannot be observed unattended; none is expected because the bridge binds the "
            "literal loopback address 127.0.0.1 only (Windows Firewall prompts for non-loopback listeners)")
    if C.a.attach:
        C.R.add("T1.firewall", "WARN", "attach mode: no before/after comparison; " + note)
        return
    b, a = C.fw_before or {}, C.fw_after or {}
    if "error" in b or "error" in a or b.get("rules") is None or a.get("rules") is None:
        C.R.add("T1.firewall", "WARN", "rules could not be read (before: %s; after: %s); %s"
                % (b.get("error", "ok"), a.get("error", "ok"), note), before=b, after=a)
        return
    def keys(x, exact):
        return {(r.get("id"), r.get("program")) for r in x["rules"] if bool(r.get("exact")) == exact or not exact}
    eb, ea = keys(b, True), keys(a, True)
    anyb, anya = keys(b, False), keys(a, False)
    new_exact = ea - eb
    new_any = anya - anyb
    if new_exact:
        v = "FAIL"
    elif eb or new_any:
        v = "WARN"
    else:
        v = "PASS"
    C.R.add("T1.firewall", v,
            "rules for this exe: before %d, after %d, new %d; rules naming %s at any path: before %d, after %d (%s); %s"
            % (len(eb), len(ea), len(new_exact), os.path.basename(C.exe or "?"), len(anyb), len(anya),
               b.get("source"), note), before=b, after=a, newExact=sorted(map(list, new_exact)))


class PingProbe(threading.Thread):
    """Sequential pings on its own connections every <interval> s: (sent s, ms, ok, frame, queued)."""

    def __init__(self, port, t0, interval=0.01):
        super().__init__(daemon=True)
        self.br = Bridge(port, 10)
        self.t0 = t0
        self.interval = interval
        self.samples = []
        self.halt = threading.Event()

    def run(self):
        while not self.halt.is_set():
            ts = time.perf_counter()
            try:
                r = self.br.call({"cmd": "ping"}, timeout=10, check=False)
                self.samples.append((ts - self.t0, r.ms, bool(r.get("ok")), r.get("frame"), r.get("queued")))
            except Exception:
                self.samples.append((ts - self.t0, (time.perf_counter() - ts) * 1000.0, False, None, None))
            self.halt.wait(self.interval)

    def finish(self):
        self.halt.set()
        self.join(20)


def window(samples, t_a, t_b):
    """Pings sent after t_a and answered before t_b, the latencies and the longest run with an unchanged frame."""
    w = [s for s in samples if s[0] >= t_a and s[0] + s[1] / 1000.0 <= t_b + 0.002]
    lat = [s[1] for s in w if s[2]]
    fails = sum(1 for s in w if not s[2])
    best_ms, best_n, prev, start, n = 0.0, 0, None, None, 0
    for s in w:
        if not s[2]:
            continue
        recv = s[0] + s[1] / 1000.0
        if s[3] is not None and s[3] == prev:
            n += 1
            span = (recv - start) * 1000.0
            if span > best_ms:
                best_ms, best_n = span, n
        else:
            prev, start, n = s[3], recv, 1
    frames = [s[3] for s in w if s[2] and s[3] is not None]
    return {"pings": len(w), "failed": fails, "latency": stats(lat), "stallMs": round(best_ms, 1), "stallPings": best_n,
            "frames": (frames[-1] - frames[0]) if len(frames) > 1 else 0,
            "queuedMax": max([s[4] for s in w if s[4] is not None] or [0])}


def card_path(C):
    tp = os.path.join(C.project or "", "Tools", "targets.json")
    with open(tp, encoding="utf-8") as f:
        rel = json.load(f)["visual"]["female"]
    pre = "chara/female/"
    return rel[len(pre):] if rel.startswith(pre) else rel


def t1_busy_chara(C):
    path = card_path(C)
    probe = PingProbe(C.port, time.perf_counter())
    probe.start()
    time.sleep(0.3)
    ta = time.perf_counter() - probe.t0
    r = C.call({"cmd": "chara.load", "path": path, "sex": "female"}, timeout=300, check=False)
    tb = time.perf_counter() - probe.t0
    time.sleep(0.2)
    probe.finish()
    w = window(probe.samples, ta, tb)
    added = (r.get("added") or [{}])[0] if r.get("ok") else {}
    C.chara = added.get("key")
    lat = w["latency"]
    ok = (r.get("ok") and added.get("state") == "Built" and w["failed"] == 0 and lat.get("n", 0) >= 3
          and lat.get("max", 1e9) <= 250)
    C.R.add("T1.busy.chara", "PASS" if ok else "FAIL",
            "chara.load %s in %.0f ms (%s); %d pings answered during it, %d failed, latency %s; main thread stalled "
            "%.0f ms (frame unchanged) with %d pings answered meanwhile; frame advanced %d"
            % ("ok, Built" if added.get("state") == "Built" else "FAILED %s" % json.dumps(r, ensure_ascii=False)[:200],
               (tb - ta) * 1000.0, path, lat.get("n", 0), w["failed"], fmt_ms(lat), w["stallMs"], w["stallPings"], w["frames"]),
            window=w, charaLoadMs=round((tb - ta) * 1000.0, 1))


def t1_busy_map(C):
    C.call({"cmd": "map.load", "clear": True})
    probe = PingProbe(C.port, time.perf_counter())
    probe.start()
    time.sleep(0.3)
    res = {}

    def run(name, req, timeout):
        b = Bridge(C.port)
        try:
            r = b.call(req, timeout=timeout, check=False)
            res[name] = (r, time.perf_counter() - probe.t0)
        except Exception as e:
            res[name] = ({"ok": False, "error": str(e)}, time.perf_counter() - probe.t0)

    ta = time.perf_counter() - probe.t0
    tm = threading.Thread(target=run, args=("map", {"cmd": "map.load", "no": MAP_NO}, 300), daemon=True)
    tm.start()
    queued, end = 0, time.time() + 10
    while "map" not in res and queued < 1 and time.time() < end:
        try:
            queued = Bridge(C.port, 5).call({"cmd": "ping"}, timeout=5, check=False).get("queued") or 0
        except Exception:
            pass
        if queued < 1:
            time.sleep(0.005)
    behind = "map" not in res
    ts_sent = time.perf_counter() - probe.t0
    tsc = threading.Thread(target=run, args=("scene", {"cmd": "scene"}, 300), daemon=True)
    tsc.start()
    tm.join(320)
    tsc.join(320)
    time.sleep(0.2)
    probe.finish()
    rm, t_m = res.get("map", ({}, 0))
    rs, t_s = res.get("scene", ({}, 0))
    w = window(probe.samples, ta, t_m)
    lat = w["latency"]
    loaded = rm.get("ok") and rm.get("state") == "Loaded"
    fifo = (t_s >= t_m) if behind else None
    ok = loaded and rs.get("ok") and fifo is not False and w["failed"] == 0 and lat.get("n", 0) >= 3 and lat.get("max", 1e9) <= 250
    C.R.add("T1.busy.map", "PASS" if ok else "FAIL",
            "map.load %d %s in %.0f ms; %d pings answered meanwhile, %d failed, latency %s; scene sent behind it %s "
            "(answered %.0f ms after the map.load%s); queued max %d"
            % (MAP_NO, "Loaded" if loaded else "FAILED %s" % json.dumps(rm, ensure_ascii=False)[:200], (t_m - ta) * 1000.0,
               lat.get("n", 0), w["failed"], fmt_ms(lat), "yes" if behind else "no (the map.load finished first)",
               (t_s - t_m) * 1000.0, ", FIFO kept" if fifo else (", FIFO BROKEN" if fifo is False else ""), w["queuedMax"]),
            window=w, mapMs=round((t_m - ta) * 1000.0, 1), sceneAfterMapMs=round((t_s - t_m) * 1000.0, 1),
            sceneSentMs=round((ts_sent - ta) * 1000.0, 1), behind=behind)


# ---------------------------------------------------------------- T4

FLOAT_SETS = [
    [0.1, 1.0 / 3.0, 1e-7],
    [1.4e-45, 1.1754942e-38, 1.17549435e-38],         # min subnormal, max subnormal, min normal
    [3.4028235e38, -3.4028235e38, -0.0],
    [0.70710677, -1.4e-45, 123456.79],
]
CAMERA_SETS = [(0.1, 1.0 / 3.0), (1.0 / 3.0, 0.1), (1e-7, 12.345679), (1.4e-45, 23.000002)]


def num_text(x, form):
    return f32_short(x) if form == "short" else repr(f32(x))


def node_transform(C, key):
    r = C.call({"cmd": "scene.doc", "key": key, "capture": False})
    text = r["node"]
    node = json.loads(text)
    tr = node.get("transform") if isinstance(node, dict) else None
    m = re.search(r'"transform"\s*:\s*\{(.*?)\}', text, re.S)
    raw = {}
    if m:
        for k in ("pos", "rot", "scale"):
            mm = re.search(r'"%s"\s*:\s*\[([^\]]*)\]' % k, m.group(1))
            if mm:
                raw[k] = [t.strip() for t in mm.group(1).split(",")]
    return tr or {}, raw


def expect_bits(x):
    b = f32bits(f32(x))
    return 0 if b == 0x80000000 else b      # the canonical writer normalizes -0 to +0


def add_item(C, parent=None):
    req = {"cmd": "item.add", "no": ITEM_NO}
    if parent:
        req["parent"] = parent
    r = C.call(req)
    return r["added"][0]["key"]


def big_text(target_bytes):
    chunk = ("日本語のとても長い題名・テスト。かな漢字カナ、半角ｶﾅ、記号〜㈱①。🎌😀👨‍👩‍👧‍👦𝄞🀄 "
             "\"quote\" \\back\\slash\\ \t tab \n newline é ü ß ñ — \u2028\u2029\u00a0\ufeff 中文 한국어 Ωμέγα / ")
    n = max(1, target_bytes // len(chunk.encode("utf-8")))
    return chunk * n


def t4(C):
    C.call({"cmd": "scene.new"})
    a = add_item(C)
    b = add_item(C, parent=a)
    # --- float32 through obj.transform -> scene.doc {key} (canonical writer)
    rows, bad = [], []
    for vals in FLOAT_SETS:
        for form in ("short", "exact"):
            texts = [num_text(v, form) for v in vals]
            body = '{"cmd":"obj.transform","key":%s,"pos":[%s]}' % (json.dumps(a), ",".join(texts))
            C.call(body.encode("ascii"), cmd="obj.transform")
            tr, raw = node_transform(C, a)
            got = tr.get("pos") or []
            for i, v in enumerate(vals):
                g = got[i] if i < len(got) else None
                gb = f32bits(g) if isinstance(g, (int, float)) else None
                eb = expect_bits(v)
                row = {"value": repr(f32(v)), "sent": texts[i], "form": form, "doc": (raw.get("pos") or [None] * 3)[i],
                       "expectBits": "%08x" % eb, "gotBits": ("%08x" % gb) if gb is not None else None}
                rows.append(row)
                if gb != eb:
                    bad.append(row)
    C.R.add("T4.float.doc", "PASS" if not bad else "FAIL",
            "%d/%d float32 values bit-equal after obj.transform -> scene.doc {key} (shortest float32 and exact double "
            "text; 0.1, 1/3, 1e-7, min/max subnormal, min normal, +-max, -0 -> +0 by design, 0.70710677, -min subnormal, "
            "123456.79)%s" % (len(rows) - len(bad), len(rows), ("; first mismatch %s" % json.dumps(bad[0])) if bad else ""),
            rows=rows)
    # --- the bridge's own float form (camera.set reply, camera.get)
    rows2, bad2 = [], []
    for dist, fov in CAMERA_SETS:
        body = '{"cmd":"camera.set","dist":%s,"fov":%s}' % (f32_short(dist), f32_short(fov))
        r1 = C.call(body.encode("ascii"), cmd="camera.set")
        r2 = C.call({"cmd": "camera.get"})
        for src, r in (("camera.set", r1), ("camera.get", r2)):
            cam = r.get("camera") or {}
            for name, v in (("dist", dist), ("fov", fov)):
                g = cam.get(name)
                gb = f32bits(g) if isinstance(g, (int, float)) else None
                row = {"src": src, "field": name, "value": repr(f32(v)), "expectBits": "%08x" % expect_bits(v),
                       "gotBits": ("%08x" % gb) if gb is not None else None, "got": g}
                rows2.append(row)
                if gb != expect_bits(v):
                    bad2.append(row)
    C.call({"cmd": "camera.set", "target": [0, 10, 0], "rot": [0, 180, 0], "dist": 30, "fov": 23})
    C.R.add("T4.float.reply", "PASS" if not bad2 else "FAIL",
            "%d/%d float32 values bit-equal in the bridge's float form (camera.set reply and camera.get dist / fov: "
            "0.1, 1/3, 1e-7, min subnormal, 12.345679, 23.000002)%s"
            % (len(rows2) - len(bad2), len(rows2), ("; first mismatch %s" % json.dumps(bad2[0])) if bad2 else ""), rows=rows2)
    # final transforms of the two items (kept through the save round trips)
    C.call({"cmd": "obj.transform", "key": a, "pos": [f32(0.1), f32(1 / 3), f32(1e-7)],
            "rot": [f32(1.4e-45), -0.0, f32(0.70710677)], "scale": [f32(1 / 3), f32(0.1), f32(0.70710677)]})
    C.call({"cmd": "obj.transform", "key": b, "pos": [f32(1.1754942e-38), f32(-1.4e-45), f32(123456.79)]})
    # --- ~10 MB non-ASCII text
    text = big_text(C.a.big_mb * 1_000_000 - 200)
    tb = text.encode("utf-8")
    th = hashlib.sha256(tb).hexdigest()
    body = json.dumps({"cmd": "history", "id": text}, ensure_ascii=False).encode("utf-8")
    t = time.perf_counter()
    r = C.call(body, timeout=300, cmd="history")
    ms = (time.perf_counter() - t) * 1000.0
    got = r.get("id")
    gbytes = got.encode("utf-8", "surrogatepass") if isinstance(got, str) else b""
    eq = gbytes == tb
    C.R.add("T4.big.id", "PASS" if eq else "FAIL",
            "request id of %d chars / %d UTF-8 bytes (body %d bytes: Japanese, emoji with surrogate pairs and ZWJ, "
            "escapes, U+2028/2029/FEFF) echoed by history %s in %.0f ms%s"
            % (len(text), len(tb), len(body), "byte-identical (sha256 %s)" % th[:16] if eq else "DIFFERENT", ms,
               "" if eq else "; got %d bytes, first difference at char %d" % (len(gbytes), first_diff(got or "", text))),
            chars=len(text), bytes=len(tb), bodyBytes=len(body), ms=round(ms, 1), sha256=th)
    body = json.dumps({"cmd": "scene.save", "name": "q_t4_title", "overwrite": True, "title": text},
                      ensure_ascii=False).encode("utf-8")
    t = time.perf_counter()
    rs = C.call(body, timeout=300, cmd="scene.save")
    save_ms = (time.perf_counter() - t) * 1000.0
    t = time.perf_counter()
    d1 = C.call({"cmd": "scene.doc"}, timeout=300)
    doc_ms = (time.perf_counter() - t) * 1000.0
    g1 = d1.get("title") or ""
    eq1 = g1.encode("utf-8", "surrogatepass") == tb
    C.R.add("T4.big.title", "PASS" if eq1 else "FAIL",
            "scene.save {title} (body %d bytes) %.0f ms -> file %d bytes, thumbnail %s; scene.doc title %s, %.0f ms%s"
            % (len(body), save_ms, rs.get("bytes", -1), rs.get("thumbnail"), "byte-identical" if eq1 else "DIFFERENT",
               doc_ms, "" if eq1 else "; first difference at char %d" % first_diff(g1, text)),
            saveMs=round(save_ms, 1), docMs=round(doc_ms, 1), fileBytes=rs.get("bytes"), path=rs.get("path"))
    # --- save -> scene.new -> scene.load -> scene.doc (T15)
    e = C.call({"cmd": "scene.new"})
    d0 = C.call({"cmd": "scene.doc"})
    t = time.perf_counter()
    rl = C.call({"cmd": "scene.load", "name": "q_t4_title"}, timeout=300)
    load_ms = (time.perf_counter() - t) * 1000.0
    d2 = C.call({"cmd": "scene.doc"}, timeout=300)
    g2 = d2.get("title") or ""
    same = (d2.get("sha256") == d1.get("sha256") and d2.get("scene") == d1.get("scene") and d2.get("nodes") == d1.get("nodes")
            and g2.encode("utf-8", "surrogatepass") == tb and d2.get("charas") == d1.get("charas") and d2.get("res") == d1.get("res"))
    tra, _ = node_transform(C, a)
    C.R.add("T4.reload", "PASS" if same and d0.get("nodes") == 0 else "FAIL",
            "save -> scene.new (nodes %s) -> scene.load %.0f ms (nodes %s, %d findings) -> scene.doc: sha256 %s, title %s, "
            "item pos after reload %s (save format T15 in the player; P's A12 covers the character case)"
            % (d0.get("nodes"), load_ms, rl.get("nodes"), len(rl.get("findings") or []),
               "equal (%s)" % (d1.get("sha256") or "")[:16] if d2.get("sha256") == d1.get("sha256") else "DIFFERENT %s vs %s" % (d1.get("sha256"), d2.get("sha256")),
               "equal" if g2.encode("utf-8", "surrogatepass") == tb else "DIFFERENT", tra.get("pos")),
            sha256Before=d1.get("sha256"), sha256After=d2.get("sha256"), loadMs=round(load_ms, 1),
            findings=rl.get("findings"), nodes=d2.get("nodes"))
    # --- save -> load -> save byte identity (fixed clock, placeholder thumbnails)
    clock = "2026-01-01T00:00:00Z"
    sa = C.call({"cmd": "scene.save", "name": "q_t4_a", "overwrite": True, "thumb": "placeholder", "clock": clock}, timeout=300)
    C.call({"cmd": "scene.new"})
    C.call({"cmd": "scene.load", "name": "q_t4_a"}, timeout=300)
    sb = C.call({"cmd": "scene.save", "name": "q_t4_b", "overwrite": True, "thumb": "placeholder", "clock": clock}, timeout=300)
    with open(sa["path"], "rb") as f:
        ba = f.read()
    with open(sb["path"], "rb") as f:
        bb = f.read()
    ident = ba == bb
    C.R.add("T4.resave", "PASS" if ident else "FAIL",
            "scene.save (clock %s, placeholder) -> scene.load -> scene.save: %d and %d bytes, %s"
            % (clock, len(ba), len(bb), "byte-identical" if ident else "DIFFERENT (first byte %d)" % first_diff(ba, bb)),
            a=sa["path"], b=sb["path"], bytes=[len(ba), len(bb)])
    C.call({"cmd": "scene.new"})      # drop the 10 MB title before the next checks


# ---------------------------------------------------------------- T7

def snapshot(C):
    d = C.call({"cmd": "scene.doc", "capture": False})
    h = C.call({"cmd": "history"})
    return {"sha256": d.get("sha256"), "nodes": d.get("nodes"), "version": d.get("version"), "dirty": d.get("dirty"),
            "undo": h.get("undo"), "redo": h.get("redo")}


def t7_headers(C):
    C.call({"cmd": "scene.new"})
    C.item = add_item(C)
    C.call({"cmd": "obj.transform", "key": C.item, "pos": [0, 0, 0]})
    s0 = snapshot(C)
    body = json.dumps({"cmd": "obj.transform", "key": C.item, "pos": [7, 0, 7]}).encode("utf-8")
    host = "127.0.0.1:%d" % C.port
    # wanted status: Origin / Sec-Fetch-* -> the bridge's 403; a wrong Host -> Mono's own 400 (the bridge's Host check
    # would answer 403 behind another transport, so 403 is accepted for the localhost and other-port forms)
    cases = [
        ("T7.origin", "Origin: http://example.com", {"Origin": "http://example.com"}, None, (403,)),
        ("T7.origin", "Origin: null", {"Origin": "null"}, None, (403,)),
        ("T7.secfetch", "Sec-Fetch-Site: cross-site", {"Sec-Fetch-Site": "cross-site"}, None, (403,)),
        ("T7.secfetch", "Sec-Fetch-Mode: cors", {"Sec-Fetch-Mode": "cors"}, None, (403,)),
        ("T7.host", "Host: evil.example:%d" % C.port, None, "evil.example:%d" % C.port, (400,)),
        ("T7.host", "Host: localhost:%d" % C.port, None, "localhost:%d" % C.port, (400, 403)),
        ("T7.host", "Host: 127.0.0.1:1", None, "127.0.0.1:1", (400, 403)),
    ]
    got = {}
    for cid, what, hdrs, hst, want in cases:
        C.alive()
        st, rh, data, ms = C.br.raw(body, headers=hdrs, host=hst, timeout=30)
        cors = [k for k in rh if k.lower().startswith("access-control-")]
        got.setdefault(cid, []).append({"case": what, "status": st, "want": list(want), "ms": round(ms, 1), "cors": cors,
                                        "body": data[:160].decode("utf-8", "replace")})
    # a plain request still works (the bridge is not refusing everything)
    st_ok, _, data_ok, _ = C.br.raw(json.dumps({"cmd": "ping"}).encode(), headers=None, host=host)
    s1 = snapshot(C)
    for cid in ("T7.origin", "T7.secfetch", "T7.host"):
        rows = got[cid]
        ok = all(r["status"] in r["want"] and not r["cors"] for r in rows)
        C.R.add(cid, "PASS" if ok else "FAIL",
                "; ".join("%s -> %s (want %s)%s" % (r["case"], r["status"], "/".join(map(str, r["want"])),
                                                    " CORS headers %s" % r["cors"] if r["cors"] else "") for r in rows)
                + "; no Access-Control-* header", rows=rows)
    same = s0 == s1
    C.R.add("T7.sidefx", "PASS" if same and st_ok == 200 else "FAIL",
            "scene.doc sha256 / nodes / version and history %s after the refused obj.transform requests; a plain ping -> %s"
            % ("unchanged" if same else "CHANGED %s -> %s" % (json.dumps(s0), json.dumps(s1)), st_ok), before=s0, after=s1)


PAGE = r"""<!doctype html>
<html><head><meta charset="utf-8"><title>T7 bridge refusal check</title></head>
<body>
<p>HS2Next player_checks T7: this page sends scene.new to the bridge; the bridge must refuse it.</p>
<pre id="out">pending</pre>
<script>
const BRIDGE = %(bridge)s;
const REPORT = %(report)s;
const VARIANT = %(variant)s;
const BODY = JSON.stringify({cmd: "scene.new"});
function limit(p, ms, name) {
  return Promise.race([p, new Promise(res => setTimeout(() => res({name, outcome: "pending after " + ms + " ms (held by the browser)"}), ms))]);
}
function attempt(name, init) {
  return limit(fetch(BRIDGE, init).then(async r => {
    let text = null;
    try { text = (await r.text()).slice(0, 300); } catch (e) { text = "unreadable: " + e; }
    return {name, outcome: "response", type: r.type, status: r.status, text};
  }, e => ({name, outcome: "error", error: String(e)})), 8000, name);
}
function formPost() {
  return new Promise(resolve => {
    const f = document.createElement("iframe");
    f.name = "sink";
    document.body.appendChild(f);
    let done = false;
    const finish = how => { if (!done) { done = true; resolve({name: "form-text-plain", outcome: how}); } };
    f.addEventListener("load", () => finish("iframe load event"));
    setTimeout(() => finish("no load event after 8000 ms"), 8000);
    const form = document.createElement("form");
    form.method = "POST"; form.action = BRIDGE; form.enctype = "text/plain"; form.target = "sink";
    const i = document.createElement("input");
    i.type = "hidden"; i.name = '{"cmd":"scene.new","pad":"'; i.value = '"}';
    form.appendChild(i);
    document.body.appendChild(form);
    form.submit();
  });
}
(async () => {
  const results = [];
  results.push(await attempt("fetch-json", {method: "POST", headers: {"Content-Type": "application/json"}, body: BODY}));
  results.push(await attempt("fetch-text-plain", {method: "POST", body: BODY}));
  results.push(await attempt("fetch-no-cors", {method: "POST", mode: "no-cors", body: BODY}));
  results.push(await formPost());
  const doc = {variant: VARIANT, origin: String(self.origin), href: location.href, secure: self.isSecureContext,
               ua: navigator.userAgent, results};
  const text = JSON.stringify(doc);
  const tag = "T7" + "RESULT", end = "T7" + "END";      // split so that the script text never matches the markers
  document.getElementById("out").textContent = tag + " " + text + " " + end;
  try { navigator.sendBeacon(REPORT, text); } catch (e) {}
})();
</script>
</body></html>
"""


class ReportServer:
    """The page's other origin (127.0.0.1:<ephemeral>): serves /t7.html and takes the page's beacon at /report."""

    def __init__(self, pages):
        self.pages = pages
        self.reports = {}
        outer = self

        class H(http.server.BaseHTTPRequestHandler):
            def log_message(self, *a):
                pass

            def do_GET(self):
                body = outer.pages.get(urllib.parse.urlsplit(self.path).path)
                if body is None:
                    self.send_response(404)
                    self.send_header("Content-Length", "0")
                    self.end_headers()
                    return
                self.send_response(200)
                self.send_header("Content-Type", "text/html; charset=utf-8")
                self.send_header("Content-Length", str(len(body)))
                self.send_header("Cache-Control", "no-store")
                self.end_headers()
                self.wfile.write(body)

            def do_POST(self):
                u = urllib.parse.urlsplit(self.path)
                n = int(self.headers.get("Content-Length") or 0)
                data = self.rfile.read(n) if n else b""
                if u.path == "/report":
                    v = urllib.parse.parse_qs(u.query).get("v", ["?"])[0]
                    outer.reports[v] = data.decode("utf-8", "replace")
                self.send_response(204)
                self.end_headers()

        self.srv = http.server.ThreadingHTTPServer(("127.0.0.1", 0), H)
        self.port = self.srv.server_address[1]
        self.th = threading.Thread(target=self.srv.serve_forever, daemon=True)
        self.th.start()

    def close(self):
        self.srv.shutdown()
        self.srv.server_close()


def owned_processes(marker):
    """PIDs of processes whose command line contains <marker> (this run's Edge profile folder)."""
    try:
        rows = ps_json("$m = %s; ConvertTo-Json -Compress -InputObject @(Get-CimInstance Win32_Process | Where-Object { "
                       "$_.CommandLine -and $_.CommandLine.Contains($m) } | ForEach-Object { [int]$_.ProcessId })"
                       % ps_quote(marker), timeout=60)
        return [int(x) for x in rows]
    except Exception:
        return []


def kill_pids(pids):
    for pid in pids:
        subprocess.run(["taskkill", "/PID", str(pid), "/F"], capture_output=True, creationflags=CREATE_NO_WINDOW)


def stop_owned(marker, root_proc):
    """Stops the Edge process this run started and any process whose command line holds this run's profile folder."""
    if root_proc is not None and root_proc.poll() is None:
        try:
            root_proc.kill()
        except Exception:
            pass
    left = [p for p in owned_processes(marker) if p != os.getpid()]
    kill_pids(left)
    return left


INTERESTING = {"origin", "sec-fetch-site", "sec-fetch-mode", "sec-fetch-dest", "content-type", "host",
               "access-control-request-method", "access-control-request-headers",
               "access-control-request-private-network", "access-control-request-local-network"}


def netlog_requests(path, prefix):
    """Requests to <prefix> in a Chromium net log (tolerates a truncated file): method, sent headers, status, errors."""
    info = {"file": path, "parsed": False}
    try:
        with open(path, encoding="utf-8", errors="replace") as f:
            text = f.read()
    except OSError as e:
        info["error"] = str(e)
        return info
    dec = json.JSONDecoder()
    try:
        i = text.index('"constants"')
        constants, end = dec.raw_decode(text, text.index("{", i))
        k = text.index("[", text.index('"events"', end)) + 1
    except ValueError as e:
        info["error"] = "no constants / events: %s" % e
        return info
    names = {v: n for n, v in (constants.get("logEventTypes") or {}).items()}
    nerr = {v: n for n, v in (constants.get("netError") or {}).items()}
    events, n_text = [], len(text)
    while True:
        while k < n_text and text[k] in " \t\r\n,":
            k += 1
        if k >= n_text or text[k] != "{":
            break
        try:
            ev, k = dec.raw_decode(text, k)
        except ValueError:
            info["truncated"] = True
            break
        events.append(ev)
    info["parsed"] = True
    info["events"] = len(events)
    by_src = {}
    for ev in events:
        by_src.setdefault((ev.get("source") or {}).get("id"), []).append(ev)
    start_job = constants.get("logEventTypes", {}).get("URL_REQUEST_START_JOB")
    reqs = []
    for src, evs in sorted(by_src.items(), key=lambda kv: kv[0] or 0):
        url = method = None
        for ev in evs:
            p = ev.get("params") or {}
            if ev.get("type") == start_job and isinstance(p, dict) and str(p.get("url", "")).startswith(prefix):
                url = p["url"]
                method = p.get("method") or method
        if url is None:            # only URL requests (CORS bookkeeping and stream jobs have their own sources)
            continue
        r = {"source": src, "url": url, "method": method, "sent": None, "status": None, "netError": None, "events": []}
        for ev in evs:
            n = names.get(ev.get("type"), str(ev.get("type")))
            p = ev.get("params") or {}
            if n not in r["events"]:
                r["events"].append(n)
            if not isinstance(p, dict):
                continue
            if "SEND_REQUEST_HEADERS" in n:
                hl = p.get("headers")
                hdrs = hl if isinstance(hl, list) else ["%s: %s" % kv for kv in (hl or {}).items()]
                line = (p.get("line") or "").strip()
                r["sent"] = {"line": line, "headers": [h for h in hdrs if h.split(":")[0].strip().lower() in INTERESTING]}
                if not r["method"] and line:
                    r["method"] = line.split(" ")[0]
            if "READ_RESPONSE_HEADERS" in n:
                hl = p.get("headers") or []
                if isinstance(hl, list) and hl:
                    r["status"] = hl[0]
            ne = p.get("net_error")
            if isinstance(ne, int) and ne != 0:
                r["netError"] = nerr.get(ne, str(ne))
            if n == "LOCAL_NETWORK_ACCESS_CHECK" or n == "PRIVATE_NETWORK_ACCESS_CHECK":
                r["lna"] = "%s -> %s: %s" % (p.get("client_address_space"), p.get("resource_address_space"), p.get("result"))
            if any(x in n for x in ("CORS", "NETWORK_ACCESS", "PRIVATE_NETWORK", "LOCAL_NETWORK")):
                r.setdefault("checks", []).append({"event": n, "params": json.dumps(p, ensure_ascii=False)[:300]})
        if r["sent"]:
            for h in r["sent"]["headers"]:
                k, _, v = h.partition(":")
                if k.strip().lower() in ("sec-fetch-mode", "sec-fetch-dest", "sec-fetch-site"):
                    r[k.strip().lower()] = v.strip()
        reqs.append(r)
    info["requests"] = reqs
    return info


def edge_exe(C):
    if C.a.edge:
        return C.a.edge if os.path.isfile(C.a.edge) else None
    for p in EDGE_DEFAULTS:
        if os.path.isfile(p):
            return p
    return None


def edge_version(exe):
    try:
        for n in os.listdir(os.path.dirname(exe)):
            if re.fullmatch(r"\d+\.\d+\.\d+\.\d+", n):
                return n
    except OSError:
        pass
    return None


def run_edge(C, edge, variant, url, extra, srv, t7dir):
    prof = os.path.join(t7dir, "edge_profile_" + variant)
    shutil.rmtree(prof, ignore_errors=True)
    os.makedirs(prof, exist_ok=True)
    netlog = os.path.join(t7dir, "netlog_%s.json" % variant)
    if os.path.exists(netlog):
        os.remove(netlog)
    base = [edge, "--headless=new", "--no-first-run", "--no-default-browser-check", "--disable-extensions",
            "--disable-gpu", "--disable-sync", "--disable-background-networking", "--disable-component-update",
            "--deny-permission-prompts", "--user-data-dir=" + prof, "--log-net-log=" + netlog,
            "--net-log-capture-mode=Everything"] + extra
    out = {"variant": variant, "url": url, "flags": extra}
    result = None
    # 1) --dump-dom with a virtual time budget: Edge waits for the page's fetches and exits by itself
    args = base + ["--dump-dom", "--virtual-time-budget=40000", url]
    with open(os.path.join(t7dir, "edge_%s_stderr.txt" % variant), "wb") as ef:
        t = time.time()
        p = subprocess.Popen(args, stdout=subprocess.PIPE, stderr=ef, stdin=subprocess.DEVNULL, creationflags=CREATE_NO_WINDOW)
        try:
            dom, _ = p.communicate(timeout=C.a.edge_timeout)
            out["exit"] = p.returncode
        except subprocess.TimeoutExpired:
            out["killed"] = stop_owned(prof, p)
            dom = b""
            try:
                dom, _ = p.communicate(timeout=10)
            except Exception:
                pass
            out["exit"] = "timeout after %d s" % C.a.edge_timeout
        out["seconds"] = round(time.time() - t, 1)
    dom = dom.decode("utf-8", "replace")
    with open(os.path.join(t7dir, "dom_%s.html" % variant), "w", encoding="utf-8") as f:
        f.write(dom)
    m = re.search(r"T7RESULT (.*?) T7END", dom, re.S)
    if m:
        try:
            result = json.loads(html.unescape(m.group(1)))
            out["channel"] = "dump-dom"
        except ValueError as e:
            out["domParseError"] = str(e)
    if result is None and variant in srv.reports:
        result = json.loads(srv.reports[variant])
        out["channel"] = "beacon"
    if result is None:
        # 2) fallback: headless without --dump-dom, wait for the page's beacon, then stop this run's Edge processes
        out["fallback"] = True
        args = base + [url]
        with open(os.path.join(t7dir, "edge_%s_fallback_stderr.txt" % variant), "wb") as ef:
            p = subprocess.Popen(args, stdout=subprocess.DEVNULL, stderr=ef, stdin=subprocess.DEVNULL, creationflags=CREATE_NO_WINDOW)
            end = time.time() + C.a.edge_timeout
            while time.time() < end and variant not in srv.reports and p.poll() is None:
                time.sleep(0.25)
            time.sleep(3)          # let the net log flush
            out["killed"] = stop_owned(prof, p)
        if variant in srv.reports:
            result = json.loads(srv.reports[variant])
            out["channel"] = "beacon (fallback)"
    left = owned_processes(prof)
    if left:
        out["leftover"] = stop_owned(prof, None)
    out["result"] = result
    out["net"] = netlog_requests(netlog, "http://127.0.0.1:%d/" % C.port)
    for _ in range(10):
        try:
            shutil.rmtree(prof)
            break
        except OSError:
            time.sleep(0.5)
    return out


def t7_edge(C):
    variants = ["file", "loopback", "public"]
    edge = None if C.a.no_edge else edge_exe(C)
    if edge is None:
        for v in variants:
            C.R.add("T7.edge." + v, "WARN", "Edge not run: %s" % ("--no-edge" if C.a.no_edge else "msedge.exe not found"))
        return
    t7dir = os.path.join(C.out, "t7")
    os.makedirs(t7dir, exist_ok=True)
    C.info["edge"] = {"path": edge, "version": edge_version(edge)}
    srv = ReportServer({})
    try:
        bridge_url = "http://127.0.0.1:%d/" % C.port
        for v in variants:
            C.alive()
            page = (PAGE % {"bridge": json.dumps(bridge_url), "variant": json.dumps(v),
                            "report": json.dumps("http://127.0.0.1:%d/report?v=%s" % (srv.port, v))}).encode("utf-8")
            srv.pages["/t7_%s.html" % v] = page
            fpath = os.path.join(t7dir, "t7_%s.html" % v)
            with open(fpath, "wb") as f:
                f.write(page)
            if v == "file":
                url, extra = "file:///" + fpath.replace("\\", "/"), []
            elif v == "loopback":
                url, extra = "http://127.0.0.1:%d/t7_%s.html" % (srv.port, v), []
            else:
                url, extra = ("http://127.0.0.1:%d/t7_%s.html" % (srv.port, v),
                              ["--ip-address-space-overrides=127.0.0.1:%d=public" % srv.port])
            s0 = snapshot(C)
            log("T7.edge.%s: %s" % (v, url))
            o = run_edge(C, edge, v, url, extra, srv, t7dir)
            s1 = snapshot(C)
            judge_edge(C, v, o, s0, s1, edge)
    finally:
        srv.close()


def judge_edge(C, v, o, s0, s1, edge):
    res = o.get("result")
    net = o.get("net") or {}
    reqs = net.get("requests") or []
    same = s0 == s1
    js = []
    js_ok = False
    if res:
        for r in res.get("results", []):
            if r.get("outcome") == "response":
                js.append("%s: response %s %s" % (r["name"], r.get("type"), r.get("status")))
                if r.get("type") in ("basic", "cors") and 200 <= (r.get("status") or 0) < 300:
                    js_ok = True
            else:
                js.append("%s: %s" % (r["name"], r.get("error") or r.get("outcome")))
    nets = []
    net_ok = False
    for r in reqs:
        st = r.get("status")
        if st and re.match(r"HTTP/\S+\s+2\d\d", st):
            net_ok = True
        mode = "/".join(x for x in (r.get("sec-fetch-mode"), r.get("sec-fetch-dest")) if x)
        if st:
            what = "-> %s" % st.split(" ", 1)[1]
        elif r.get("sent"):
            what = "-> no answer"
        else:
            what = "not sent (%s%s)" % (r.get("netError") or "?", "; LNA " + r["lna"] if r.get("lna") else "")
        nets.append("%s%s %s" % (r.get("method") or "?", (" " + mode) if mode else "", what))
    if not same or js_ok or net_ok:
        verdict = "FAIL"
    elif not res:
        verdict = "WARN"
    else:
        verdict = "PASS"
    sent = [r for r in reqs if r.get("sent")]
    if sent:
        how = "%d requests reached the bridge: %s" % (len(sent), ", ".join(nets))
    elif reqs:
        how = "no request reached the bridge, the browser blocked them: %s" % ", ".join(nets)
    else:
        how = "no request to the bridge in the net log%s" % ("" if net.get("parsed") else " (net log not readable: %s)" % net.get("error"))
    C.R.add("T7.edge." + v, verdict,
            "%s origin %s: %s; JS: %s; scene.doc and history %s%s"
            % (os.path.basename(edge), (res or {}).get("origin", "?"), how, "; ".join(js) if js else "no result from the page",
               "unchanged" if same else "CHANGED", "" if res else " (Edge exit %s; see t7\\edge_%s_stderr.txt)" % (o.get("exit"), v)),
            edge=o, before=s0, after=s1)


# ---------------------------------------------------------------- T8

def t8_ping(C):
    lat, bad, first, last = [], 0, None, None
    t = time.perf_counter()
    for i in range(1000):
        r = C.call({"cmd": "ping", "id": i}, timeout=30, check=False)
        if not r.get("ok") or r.get("id") != i:
            bad += 1
        lat.append(r.ms)
        if first is None:
            first = r
        last = r
    total = time.perf_counter() - t
    st = stats(lat)
    dfr = (last.get("frame") or 0) - (first.get("frame") or 0)
    drt = (last.get("realtime") or 0) - (first.get("realtime") or 0)
    C.fps = dfr / drt if drt > 0 else None
    C.R.add("T8.ping", "PASS" if bad == 0 else "FAIL",
            "1,000 sequential pings: %d ok with their id, %d bad; %.2f s; latency %s; player frame rate %.1f fps"
            % (1000 - bad, bad, total, fmt_ms(st), C.fps or 0), latency=st, seconds=round(total, 2), fps=C.fps)


def t8_capture(C):
    shots = []
    bad = []
    t = time.perf_counter()
    for i in range(100):
        w, h = 64 + 16 * (i % 13), 48 + 16 * (i % 7)
        name = "q_t8_%03d" % i
        r = C.call({"cmd": "capture", "name": name, "width": w, "height": h}, timeout=60, check=False)
        path = r.get("path")
        size = png_size(path) if path and os.path.isfile(path) else None
        ok = r.get("ok") and size == (w, h) and r.get("width") == w and r.get("height") == h
        shots.append({"name": name, "want": [w, h], "png": list(size) if size else None, "ms": round(r.ms, 1),
                      "renderMs": r.get("ms"), "path": path, "ok": bool(ok)})
        if not ok:
            bad.append(shots[-1] if r.get("ok") else {"name": name, "reply": json.dumps(r, ensure_ascii=False)[:300]})
    total = time.perf_counter() - t
    st = stats([s["ms"] for s in shots])
    rst = stats([s["renderMs"] for s in shots if isinstance(s.get("renderMs"), (int, float))])
    under = os.path.join(C.out, "bridge_out").lower()
    outside = [] if C.a.attach else \
        [s["path"] for s in shots if s.get("path") and not os.path.abspath(s["path"]).lower().startswith(under)]
    removed = 0
    if not C.a.keep_captures:
        for s in shots:
            try:
                if s.get("path") and os.path.isfile(s["path"]):
                    os.remove(s["path"])
                    removed += 1
            except OSError:
                pass
    C.R.add("T8.capture", "PASS" if not bad and not outside else "FAIL",
            "100 captures (64-256 x 48-144): %d with the requested PNG size, %d bad; total %.2f s; per capture %s; "
            "render+encode+write %s; files under bridge_out %s; %s"
            % (100 - len(bad), len(bad), total, fmt_ms(st), fmt_ms(rst), "yes" if not outside else "NO (%d)" % len(outside),
               "kept" if C.a.keep_captures else "removed %d" % removed),
            latency=st, render=rst, seconds=round(total, 2), bad=bad[:10], outside=outside[:5])


def t8_concurrent(C):
    clients, each = 4, 250
    out = [None] * clients

    def client(k):
        b = Bridge(C.port, 60)
        lost = mixed = reorder = 0
        last_frame = -1
        lat = {"ping": [], "scene": []}
        for i in range(each):
            cmd = "ping" if i % 2 == 0 else "scene"
            rid = "c%d-%03d-日本" % (k, i)
            try:
                r = b.call({"cmd": cmd, "id": rid}, timeout=60, check=False)
            except Exception:
                lost += 1
                continue
            if not r.get("ok"):
                lost += 1
                continue
            if r.get("id") != rid or r.get("cmd") != cmd or (cmd == "scene" and not isinstance(r.get("objects"), list)):
                mixed += 1
            if cmd == "ping":
                f = r.get("frame")
                if isinstance(f, int):
                    if f < last_frame:
                        reorder += 1
                    last_frame = f
            lat[cmd].append(r.ms)
        out[k] = {"lost": lost, "mixed": mixed, "reordered": reorder, "ping": stats(lat["ping"]), "scene": stats(lat["scene"])}

    t = time.perf_counter()
    ths = [threading.Thread(target=client, args=(k,), daemon=True) for k in range(clients)]
    for th in ths:
        th.start()
    for th in ths:
        th.join(600)
    total = time.perf_counter() - t
    C.alive()
    done = [o for o in out if o is not None]
    lost = sum(o["lost"] for o in done) + (clients - len(done)) * each
    mixed = sum(o["mixed"] for o in done)
    reorder = sum(o["reordered"] for o in done)
    C.R.add("T8.concurrent", "PASS" if lost == mixed == reorder == 0 else "FAIL",
            "%d clients x %d mixed ping / scene requests with ids in %.2f s: lost %d, mixed %d, reordered %d; per client "
            "scene median %s ms, ping median %s ms"
            % (clients, each, total, lost, mixed, reorder, "/".join("%.1f" % o["scene"].get("median", 0) for o in done),
               "/".join("%.1f" % o["ping"].get("median", 0) for o in done)),
            clients=out, seconds=round(total, 2))


def t8_mutate(C):
    if not C.item:
        C.item = add_item(C)
    lat, dfs = [], []
    for i in range(30):
        p0 = C.call({"cmd": "ping"})
        r = C.call({"cmd": "obj.transform", "key": C.item, "pos": [0.25 * (i % 4), 0, 0.5 * (i % 3)]})
        p1 = C.call({"cmd": "ping"})
        lat.append(r.ms)
        dfs.append((p1.get("frame") or 0) - (p0.get("frame") or 0))
    st = stats(lat)
    frame_ms = 1000.0 / C.fps if getattr(C, "fps", None) else 1000.0 / 60
    med_df = statistics.median(dfs)
    ok = st["median"] <= max(40.0, 2.5 * frame_ms) and med_df <= 2
    hist = {}
    for d in dfs:
        hist[d] = hist.get(d, 0) + 1
    C.R.add("T8.mutate", "PASS" if ok else "FAIL",
            "obj.transform x30: latency %s (frame %.1f ms); ping frame delta around each call median %g, histogram %s"
            % (fmt_ms(st), frame_ms, med_df, ", ".join("%d:%d" % (k, hist[k]) for k in sorted(hist))),
            latency=st, frameDelta=dfs, frameMs=round(frame_ms, 2))


# ---------------------------------------------------------------- log

def check_log(C):
    path = os.path.join(C.out, "player.log")
    if C.a.attach or not os.path.isfile(path):
        return
    with open(path, encoding="utf-8", errors="replace") as f:
        lines = f.read().splitlines()
    exc = [l for l in lines if "Exception" in l]
    ready = [l for l in lines if "[app] ready in" in l]
    C.R.add("LOG", "WARN" if exc else "PASS",
            "player.log %d lines; %d lines with 'Exception'%s; %s"
            % (len(lines), len(exc), (" (first: %s)" % exc[0][:160]) if exc else "", ready[0].strip()[:120] if ready else "no '[app] ready' line"),
            exceptions=exc[:20])


# ---------------------------------------------------------------- report

def write_reports(C, exit_code, started):
    R = C.R
    n_pass, n_fail, n_warn = R.count("PASS"), R.count("FAIL"), R.count("WARN")
    summary = "SUMMARY: %d PASS, %d FAIL" % (n_pass, n_fail)
    warns = [x["id"] for x in R.items if x["verdict"] == "WARN"]
    lines = [summary, "WARN: %d%s" % (n_warn, (" (" + ", ".join(warns) + ")") if warns else ""),
             "player_checks %s; exe %s; port %d; pid %s; exit code %d; %.0f s"
             % (started, C.exe or "(attach)", C.port, C.pid, exit_code, time.perf_counter() - T0),
             "NOTE: T1 - a Windows Firewall prompt cannot be observed unattended; none is expected because the bridge "
             "binds the literal loopback address 127.0.0.1 only (prompts are raised for non-loopback listeners); "
             "T1.listen and T1.firewall check the binding and that no rule for the exe appeared."]
    for x in R.items:
        lines.append("%-16s %-4s  %s" % (x["id"], x["verdict"], x["text"]))
    lines.append(summary)
    with open(os.path.join(C.out, "player_checks_report.txt"), "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")
    doc = {"started": started, "exe": C.exe, "project": C.project, "out": C.out, "port": C.port, "pid": C.pid,
           "exitCode": exit_code, "seconds": round(time.perf_counter() - T0, 1), "summary": summary,
           "counts": {"pass": n_pass, "fail": n_fail, "warn": n_warn}, "info": C.info,
           "python": sys.version.split()[0], "checks": R.items}
    with open(os.path.join(C.out, "player_checks.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(doc, f, ensure_ascii=False, indent=1, default=str)


# ---------------------------------------------------------------- main

def main(argv):
    for s in (sys.stdout, sys.stderr):
        try:
            s.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, ValueError):
            pass
    ap = argparse.ArgumentParser(description="External bridge checks (T1, T4, T7, T8) against the HS2Next app player.")
    ap.add_argument("--exe", help="path to HS2Next.exe (the built app player)")
    ap.add_argument("--project", help="project root (Config\\settings.local.json, Cache, Tools\\targets.json)")
    ap.add_argument("--out", required=True, help="output folder (report, json, player.log, userdata, bridge_out, t7)")
    ap.add_argument("--port", type=int, default=DEFAULT_PORT)
    ap.add_argument("--timeout-min", type=float, default=20.0, help="whole run; past it the started PID is killed")
    ap.add_argument("--ready-timeout-min", type=float, default=12.0, help="start to ping.appReady (a cold zipmod scan takes ~8 min)")
    ap.add_argument("--only", default="", help="comma list of T1,T4,T7,T8 (default all)")
    ap.add_argument("--edge", help="msedge.exe (default: the standard install path)")
    ap.add_argument("--no-edge", action="store_true", help="skip the browser part of T7 (WARN lines)")
    ap.add_argument("--edge-timeout", type=int, default=90, help="seconds per Edge run")
    ap.add_argument("--keep-captures", action="store_true", help="keep T8's 100 small PNGs")
    ap.add_argument("--big-mb", type=int, default=10, help="size of T4's non-ASCII text in MB (UTF-8)")
    ap.add_argument("--min-free-gb", type=float, default=8.0)
    ap.add_argument("--ram-wait-min", type=float, default=10.0)
    ap.add_argument("--attach", action="store_true", help="use a bridge already serving on --port (no start / quit)")
    a = ap.parse_args(argv)
    if not a.attach and (not a.exe or not a.project):
        ap.error("--exe and --project are required (unless --attach)")
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
                if C.want("T1"):
                    t1_listen_sample(C, "ready")
                if C.want("T4"):
                    guard(C, "T4", t4)
                if C.want("T7"):
                    guard(C, "T7", t7_headers)
                    guard(C, "T7.edge", t7_edge)
                if C.want("T1"):
                    if C.project and os.path.isfile(os.path.join(C.project, "Tools", "targets.json")):
                        guard(C, "T1.busy.chara", t1_busy_chara)
                    else:
                        C.R.add("T1.busy.chara", "WARN", "Tools/targets.json not found under --project: no card to load")
                    guard(C, "T1.busy.map", t1_busy_map)
                if C.want("T8"):
                    guard(C, "T8.ping", t8_ping)
                    guard(C, "T8.capture", t8_capture)
                    guard(C, "T8.concurrent", t8_concurrent)
                    guard(C, "T8.mutate", t8_mutate)
                if C.want("T1"):
                    t1_listen_sample(C, "end")
                    t1_listen_verdict(C)
            except Abort as e:
                C.R.add("ABORT", "FAIL", "the run stopped: %s" % e)
    finally:
        watchdog.cancel()
        if C.aborted is None:
            try:
                quit_player(C)
            except Exception as e:
                C.R.add("QUIT", "FAIL", "%s: %s" % (type(e).__name__, e))
        kill_player(C)
        if C.want("T1") and exit_code == 0 and not a.attach and C.exe:
            try:
                C.fw_after = firewall_rules(C.exe)
            except Exception as e:
                C.fw_after = {"error": str(e)}
            t1_firewall_verdict(C)
        elif C.want("T1") and a.attach and exit_code == 0:
            t1_firewall_verdict(C)
        try:
            check_log(C)
        except Exception as e:
            C.R.add("LOG", "WARN", "player.log not read: %s" % e)
        if exit_code == 0 and C.R.count("FAIL"):
            exit_code = 1
        write_reports(C, exit_code, started)
        log("%s -> exit %d; report %s" % ("SUMMARY: %d PASS, %d FAIL, %d WARN" % (C.R.count("PASS"), C.R.count("FAIL"), C.R.count("WARN")),
                                          exit_code, os.path.join(C.out, "player_checks_report.txt")))
    return exit_code


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
