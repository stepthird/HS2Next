"""
startup_check.py - start-up times of the app player (the Startup.player row; opt-in). Numbers only: no image is captured,
saved or opened. Python 3.14, standard library only (maker_session.py's helpers are imported).

Usage (the row calls the first form; the row's runner holds the GPU and input locks, so the row passes no lock option):
    python -I Tools\\startup_check.py --exe <HS2Next.exe> --project <project root> --out <dir> [--port 17611]
    more options: --warm 3 (warm starts per mode), --cold 1 (starts of fresh copies of the player folder), --modes batch,ui,
                  --gpu-lock <file> --input-lock <file> --quiet-lock <file> (taken exclusively when given, removed at the end),
                  --ready-timeout-s 300, --timeout-min 15, --keep-copy

What one start is: <exe> -batchmode [-ui] -bridgePort <port> -bridgeAllowQuit -settings <project>\\Config\\settings.local.json
-cacheDir <the settings' cache> -userData <out>\\<start>\\userdata -bridgeOutputDir <out>\\<start>\\bridge_out -logFile
<out>\\<start>\\player.log, with HS2NEXT_NO_PLUGINS=1 and HS2NEXT_PRIVATE_OFF=1; started by path, waited for ping.appReady
(the endpoint file polled every 10 ms, then ping every 20 ms), then app.quit and the exit waited by PID (killed by PID after
20 s). The player log is read while it grows and each line is stamped, which splits the start into: the image load (process
start to the engine's first lines), the graphics device, the managed assembly load, the first scene, the app's stages and the
UI. A cold start is the first start of a fresh copy of the player folder (the copy leaves out UserData and every link): that is
what a user's first start after unpacking costs (the antivirus reads the new binaries; the same folder started again is warm).

Measurements (one report line each; MET / MISSED against the target, INFO without one):
  SU1      warm start, process start -> ping.appReady: the median of --warm starts per mode (batch: target <= 3.0 s;
           ui (-ui, the Studio UI built): target <= 3.5 s)
  SU2      cold start (a fresh copy): target <= 10.5 s (no regression; the cost is the antivirus' first read of new files)
  SU3      the app's own stages ("[app] ready in <ms> ms"), the median of the warm starts: target <= 2.2 s
  SU5      the first frame with a UI after the first scene (the log's "[ui] ready" or "[setup] ready" line minus the first scene's
           line), the median of the warm ui starts: target <= 1.5 s
  PHASES   per start: image load, graphics device, assembly load, first scene, stages, realtimeSinceStartup at ready, the
           player's CPU seconds, the antivirus engine's CPU seconds during the start (when its process can be read), the quit time
  QUIT     every start ended by app.quit with exit 0 within 10 s, and its log holds "[app] quit:" when the build writes it

Outputs: <out>\\startup_check_report.txt (first and last line "SUMMARY: N MET, M MISSED, K INFO, F FAIL"),
<out>\\startup_check.json, <out>\\run.log, one folder per start. Exit code: 0 no FAIL, 1 a start failed, 2 setup error or
refused, 3 aborted by the game gate.
"""
import argparse
import ctypes
import ctypes.wintypes as wt
import json
import os
import re
import shutil
import statistics
import subprocess
import sys
import threading
import time
import traceback
from datetime import datetime, timedelta

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)        # python -I does not add the script's folder
import maker_session as M       # noqa: E402

DEFAULT_PORT = 17611
MARKERS = [
    ("first", None),
    ("engine", "Initialize engine version"),
    ("gfxBegin", "GfxDevice: creating device client"),
    ("gfxEnd", "Direct3D:"),
    ("asmBegin", "Begin MonoManager ReloadAssembly"),
    ("asmEnd", "- Finished resetting the current domain"),
    ("scene", "UnloadTime:"),
    ("settings", "[app] settings:"),
    ("pipeline", "[app] pipeline:"),
    ("ready", "[app] ready in"),
    ("ui", "[ui] ready in"),
    ("setup", "[setup] ready"),
]
TARGETS = {"SU1.batch": 3.0, "SU1.ui": 3.5, "SU2": 10.5, "SU3": 2.2, "SU5": 1.5}

_k32 = ctypes.WinDLL("kernel32", use_last_error=True)


class FILETIME(ctypes.Structure):
    _fields_ = [("lo", wt.DWORD), ("hi", wt.DWORD)]


def process_cpu_s(pid):
    """User + kernel CPU seconds of a process (query-limited access, which the antivirus engine's process allows), or None."""
    h = _k32.OpenProcess(0x1000, False, int(pid))
    if not h:
        return None
    try:
        c, e, k, u = FILETIME(), FILETIME(), FILETIME(), FILETIME()
        if not _k32.GetProcessTimes(h, ctypes.byref(c), ctypes.byref(e), ctypes.byref(k), ctypes.byref(u)):
            return None
        return ((k.hi << 32 | k.lo) + (u.hi << 32 | u.lo)) / 1e7
    finally:
        _k32.CloseHandle(h)


def image_cpu_s(image):
    """User + kernel CPU seconds summed over the processes named <image> (case ignored), read from the system's process list
    (NtQuerySystemInformation), which also covers a protected process such as the antivirus engine; None when unreadable."""
    try:
        ntdll = ctypes.WinDLL("ntdll")
        size = 1 << 22
        for _ in range(6):
            buf = ctypes.create_string_buffer(size)
            need = wt.ULONG(0)
            st = ntdll.NtQuerySystemInformation(5, buf, size, ctypes.byref(need)) & 0xFFFFFFFF
            if st == 0xC0000004:            # the list grew: a bigger buffer
                size = max(size * 2, need.value + (1 << 16))
                continue
            if st != 0:
                return None
            total, off, found = 0, 0, False
            base = ctypes.addressof(buf)
            while True:
                nxt = ctypes.c_uint32.from_address(base + off).value
                user = ctypes.c_int64.from_address(base + off + 40).value
                kern = ctypes.c_int64.from_address(base + off + 48).value
                length = ctypes.c_uint16.from_address(base + off + 56).value
                ptr = ctypes.c_void_p.from_address(base + off + 64).value
                if ptr and length:
                    name = ctypes.wstring_at(ptr, length // 2)
                    if name.lower() == image.lower():
                        total += user + kern
                        found = True
                if nxt == 0:
                    break
                off += nxt
            return total / 1e7 if found else None
        return None
    except (OSError, ValueError, AttributeError):
        return None


def antivirus_cpu():
    return image_cpu_s("MsMpEng.exe")


class LogTail(threading.Thread):
    """Reads a growing log and stamps each new line with the time since t0 (polled every 5 ms)."""

    def __init__(self, path, t0):
        super().__init__(daemon=True)
        self.path, self.t0 = path, t0
        self.lines = []
        self.stop = threading.Event()

    def run(self):
        f, buf = None, b""
        try:
            while not self.stop.is_set():
                if f is None:
                    try:
                        f = open(self.path, "rb")
                    except OSError:
                        time.sleep(0.005)
                        continue
                data = f.read()
                if data:
                    now = time.perf_counter() - self.t0
                    buf += data
                    parts = buf.split(b"\n")
                    buf = parts.pop()
                    for ln in parts:
                        self.lines.append((now, ln.decode("utf-8", "replace").rstrip("\r")))
                else:
                    time.sleep(0.005)
        finally:
            if f:
                f.close()

    def markers(self):
        res = {}
        for key, text in MARKERS:
            for t, ln in self.lines:
                if text is None or ln.startswith(text):
                    res[key] = round(t, 3)
                    break
        return res


class Run:
    def __init__(self, a):
        self.a = a
        self.exe = os.path.abspath(a.exe)
        self.project = os.path.abspath(a.project)
        self.out = os.path.abspath(a.out)
        os.makedirs(self.out, exist_ok=True)
        self.logf = open(os.path.join(self.out, "run.log"), "w", encoding="utf-8")
        self.t_begin = time.time()
        self.items = []
        self.starts = []
        self.locks = []
        # the game gate: the local file's process names (no file: no gate, said in the report)
        self.gate, _, self.gate_note = M.read_game_gate(os.path.join(HERE, "session_gate.json"))

    def log(self, m):
        line = "[%s %6.1f s] %s" % (datetime.now().strftime("%H:%M:%S"), time.time() - self.t_begin, m)
        print(line, flush=True)
        self.logf.write(line + "\n")
        self.logf.flush()

    def add(self, cid, verdict, text, **data):
        self.items.append({"id": cid, "verdict": verdict, "text": text, "data": data})
        self.log("%-8s %-6s %s" % (cid, verdict, text))

    # ------------------------------------------------------------ setup

    def setup(self):
        a = self.a
        if M.running_processes(self.gate):
            raise GateAbort("the game runs")
        if not os.path.isfile(self.exe):
            raise SetupError("no player: " + self.exe)
        self.settings = os.path.join(self.project, "Config", "settings.local.json")
        if not os.path.isfile(self.settings):
            raise SetupError("settings file not found: " + self.settings)
        self.cache = os.path.join(self.project, "Cache")
        try:
            with open(self.settings, encoding="utf-8-sig") as f:
                cd = json.load(f).get("cacheDir")
            if cd:
                self.cache = os.path.abspath(os.path.join(self.project, cd))
        except (OSError, ValueError):
            pass
        if M.port_listeners(a.port):
            raise SetupError("port %d is taken" % a.port)
        fg = M.free_gb()
        if fg is not None and fg < 8:
            raise SetupError("free RAM %.1f GB < 8 GB" % fg)
        end = datetime.now() + timedelta(minutes=a.timeout_min)
        for path in (a.gpu_lock, a.input_lock, a.quiet_lock):
            if path and os.path.exists(path):
                raise SetupError("lock held: %s: %s" % (path, open(path, encoding="utf-8", errors="replace").read().strip()[:200]))
        for path in (a.gpu_lock, a.input_lock, a.quiet_lock):
            if not path:
                continue
            fd = os.open(path, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
            os.write(fd, ("startup_check PID %d start %s expected end %s\n" % (os.getpid(), datetime.now().strftime("%Y-%m-%d %H:%M:%S"),
                                                                            end.strftime("%Y-%m-%d %H:%M:%S"))).encode("utf-8"))
            os.close(fd)
            self.locks.append(path)
        if self.locks:
            self.log("locks taken: " + ", ".join(os.path.basename(p) for p in self.locks))

    def release(self):
        for p in self.locks:
            try:
                os.remove(p)
            except OSError as e:
                self.log("could not remove %s: %s" % (p, e))
        if self.locks:
            self.log("locks removed")
        self.locks = []

    # ------------------------------------------------------------ one start

    def start(self, label, exe, ui, note):
        a = self.a
        if M.running_processes(self.gate):
            raise GateAbort("the game runs")
        if time.time() - self.t_begin > a.timeout_min * 60:
            raise SetupError("the run's time limit passed")
        od = os.path.join(self.out, label)
        os.makedirs(od, exist_ok=True)
        ud, bo, pl = os.path.join(od, "userdata"), os.path.join(od, "bridge_out"), os.path.join(od, "player.log")
        ep = os.path.join(ud, "Bridge", "endpoint.json")
        for f in (ep, pl):
            if os.path.exists(f):
                os.remove(f)
        env = dict(os.environ)
        env["HS2NEXT_NO_PLUGINS"] = "1"
        env["HS2NEXT_PRIVATE_OFF"] = "1"
        args = [exe, "-batchmode"] + (["-ui"] if ui else []) + ["-bridgePort", str(a.port), "-bridgeAllowQuit", "-settings", self.settings,
                                                                "-cacheDir", self.cache, "-userData", ud, "-bridgeOutputDir", bo, "-logFile", pl]
        rec = {"label": label, "ui": ui, "note": note, "exe": exe}
        av0 = antivirus_cpu()
        t0 = time.perf_counter()
        tail = LogTail(pl, t0)
        tail.start()
        proc = subprocess.Popen(args, cwd=os.path.dirname(exe), env=env, stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL,
                                stderr=subprocess.DEVNULL, creationflags=M.CREATE_NO_WINDOW)
        rec["pid"] = proc.pid
        t_ep = t_ready = None
        reply = None
        try:
            while time.perf_counter() - t0 < a.ready_timeout_s:
                if proc.poll() is not None:
                    raise StartError("the player exited with %s before it was ready" % proc.returncode)
                if t_ep is None:
                    if os.path.exists(ep):
                        t_ep = time.perf_counter() - t0
                    else:
                        time.sleep(0.01)
                        continue
                try:
                    r = M.http_post(a.port, {"cmd": "ping"}, 5)
                    if r.get("appReady"):
                        t_ready = time.perf_counter() - t0
                        reply = r
                        break
                except Exception:
                    pass
                time.sleep(0.02)
            if t_ready is None:
                raise StartError("not ready in %d s" % a.ready_timeout_s)
            cpu = process_cpu_s(proc.pid)
            av1 = antivirus_cpu()
            rec.update({"readyS": round(t_ready, 3), "endpointS": round(t_ep, 3), "rtss": reply.get("realtime"),
                        "playerCpuS": round(cpu, 2) if cpu is not None else None,
                        "antivirusCpuS": round(av1 - av0, 2) if av0 is not None and av1 is not None else None})
            if ui:
                # the UI is built in the frames after the scene stage: wait for its line (bounded)
                t_ui = time.perf_counter()
                while time.perf_counter() - t_ui < 10 and not any(ln.startswith(("[ui] ready in", "[setup] ready")) for _, ln in list(tail.lines)):
                    time.sleep(0.02)
        finally:
            tq = time.perf_counter()
            try:
                M.http_post(a.port, {"cmd": "app.quit", "code": 0}, 30)
            except Exception as e:
                rec["quitError"] = "%s: %s" % (type(e).__name__, e)
            try:
                rec["exit"] = proc.wait(20)
            except subprocess.TimeoutExpired:
                proc.kill()
                rec["exit"] = "killed"
            rec["quitS"] = round(time.perf_counter() - tq, 2)
            time.sleep(0.3)
            tail.stop.set()
            tail.join(5)
        mk = tail.markers()
        text = "\n".join(ln for _, ln in tail.lines)
        m = re.search(r"\[app\] ready in (\d+) ms", text)
        rec["appReadyMs"] = int(m.group(1)) if m else None
        rec["stages"] = {k: int(v) for k, v in re.findall(r"\[app\] ([\w.]+): (\d+) ms", text)}
        rec["markers"] = mk

        def span(x, y):
            return round(mk[y] - mk[x], 3) if x in mk and y in mk else None
        rec["phases"] = {"imageLoad": mk.get("engine"), "gfxDevice": span("gfxBegin", "gfxEnd"), "assemblyLoad": span("asmBegin", "asmEnd"),
                         "firstScene": mk.get("scene"), "uiAfterScene": (span("scene", "ui") if "ui" in mk else span("scene", "setup"))}
        rec["quitLine"] = next((ln for _, ln in tail.lines if ln.startswith("[app] quit:")), None)
        rec["api"] = (re.search(r"Version:\s+(Direct3D [^\n]+)", text) or [None, None])[1]
        self.starts.append(rec)
        self.log("%-10s ready %6.2f s (rtss %s, [app] ready %s ms) phases %s cpu %s av %s exit %s quit %s s"
                 % (label, rec["readyS"], round(rec["rtss"], 2) if isinstance(rec.get("rtss"), (int, float)) else None, rec["appReadyMs"],
                    json.dumps(rec["phases"]), rec["playerCpuS"], rec["antivirusCpuS"], rec["exit"], rec["quitS"]))
        return rec

    # ------------------------------------------------------------ the fresh copy

    def copy_player(self, n):
        src = os.path.dirname(self.exe)
        dst = os.path.join(self.out, "copy%d_%s" % (n, datetime.now().strftime("%H%M%S")))
        a = ["robocopy", src, dst, "/E", "/XJ", "/XD", os.path.join(src, "UserData"), os.path.join(src, "Plugins"),
             "/R:0", "/W:0", "/NFL", "/NDL", "/NJH", "/NJS", "/NP"]
        t = time.perf_counter()
        r = subprocess.run(a, capture_output=True, timeout=600, creationflags=M.CREATE_NO_WINDOW)
        if r.returncode >= 8:
            raise SetupError("robocopy failed (%d)" % r.returncode)
        self.log("fresh copy %s in %.1f s" % (dst, time.perf_counter() - t))
        return dst

    def remove_copy(self, path):
        """Removes a copy made by this script; never when a link or junction appeared inside it."""
        for dp, dn, fn in os.walk(path):
            for d in dn + fn:
                q = os.path.join(dp, d)
                if os.path.islink(q) or os.path.isjunction(q):
                    self.log("kept %s: it holds a link (%s)" % (path, q))
                    return
        shutil.rmtree(path, ignore_errors=True)

    # ------------------------------------------------------------ the measurements

    def run(self):
        a = self.a
        modes = [m.strip() for m in a.modes.split(",") if m.strip()]
        for mode in modes:
            for i in range(a.warm):
                self.start("%s_w%d" % (mode, i + 1), self.exe, mode == "ui", "warm")
                time.sleep(2)
        for i in range(a.cold):
            dst = self.copy_player(i + 1)
            time.sleep(2)
            try:
                self.start("cold%d" % (i + 1), os.path.join(dst, os.path.basename(self.exe)), False, "first start of a fresh copy")
            finally:
                if not a.keep_copy:
                    self.remove_copy(dst)
            time.sleep(2)

    def judge(self):
        def med(xs):
            xs = [x for x in xs if isinstance(x, (int, float))]
            return round(statistics.median(xs), 3) if xs else None

        def verdict(v, target):
            return "INFO" if v is None else "MET" if v <= target else "MISSED"
        warm = [s for s in self.starts if s["note"] == "warm"]
        for mode in ("batch", "ui"):
            ss = [s for s in warm if s["ui"] == (mode == "ui")]
            if ss:
                v = med([s.get("readyS") for s in ss])
                self.add("SU1", verdict(v, TARGETS["SU1." + mode]), "warm start (%s), process start -> ping.appReady: median %s s of %s (target <= %s s)"
                         % (mode, v, ", ".join(str(s.get("readyS")) for s in ss), TARGETS["SU1." + mode]), mode=mode, values=[s.get("readyS") for s in ss])
        cold = [s for s in self.starts if s["note"] != "warm"]
        if cold:
            v = max(s.get("readyS") or 0 for s in cold)
            self.add("SU2", verdict(v, TARGETS["SU2"]), "cold start (a fresh copy of the player folder): %s s; assembly load %s s, antivirus CPU %s s "
                     "(target <= %s s)" % (", ".join(str(s.get("readyS")) for s in cold), ", ".join(str(s["phases"].get("assemblyLoad")) for s in cold),
                                           ", ".join(str(s.get("antivirusCpuS")) for s in cold), TARGETS["SU2"]))
        if warm:
            v = med([s.get("appReadyMs") for s in warm])
            v = v / 1000.0 if v is not None else None
            self.add("SU3", verdict(v, TARGETS["SU3"]), "the app's stages ([app] ready in), warm median %s s (target <= %s s); stages of the first warm start: %s"
                     % (v, TARGETS["SU3"], json.dumps(warm[0].get("stages"))))
        uis = [s for s in warm if s["ui"]]
        if uis:
            v = med([s["phases"].get("uiAfterScene") for s in uis])
            self.add("SU5", verdict(v, TARGETS["SU5"]), "the first frame with a UI after the first scene, warm median %s s (target <= %s s)" % (v, TARGETS["SU5"]))
        for s in self.starts:
            self.add("PHASES", "INFO", "%s: ready %s s, image load %s s, graphics device %s s, assembly load %s s, first scene %s s, rtss at ready %s, "
                     "[app] ready %s ms, player CPU %s s, antivirus CPU %s s, %s" % (s["label"], s.get("readyS"), s["phases"]["imageLoad"],
                                                                                   s["phases"]["gfxDevice"], s["phases"]["assemblyLoad"], s["phases"]["firstScene"],
                                                                                   round(s["rtss"], 2) if isinstance(s.get("rtss"), (int, float)) else None,
                                                                                   s.get("appReadyMs"), s.get("playerCpuS"), s.get("antivirusCpuS"), s.get("api")))
        bad = [s for s in self.starts if s.get("exit") != 0 or (s.get("quitS") or 99) > 10]
        lines = [s for s in self.starts if s.get("quitLine")]
        self.add("QUIT", "FAIL" if bad else "MET", "app.quit -> exit 0 within 10 s: %d of %d (quit %s s); the quit line in %d logs%s"
                 % (len(self.starts) - len(bad), len(self.starts), ", ".join(str(s.get("quitS")) for s in self.starts), len(lines),
                    (": " + lines[0]["quitLine"][:160]) if lines else ""))

    def write(self, code, extra):
        cnt = {v: sum(1 for x in self.items if x["verdict"] == v) for v in ("MET", "MISSED", "INFO", "FAIL")}
        head = "SUMMARY: %d MET, %d MISSED, %d INFO, %d FAIL" % (cnt["MET"], cnt["MISSED"], cnt["INFO"], cnt["FAIL"])
        lines = [head] + extra + ["%s %s %s" % (x["id"], x["verdict"], x["text"]) for x in self.items] + [head]
        with open(os.path.join(self.out, "startup_check_report.txt"), "w", encoding="utf-8") as f:
            f.write("\n".join(lines) + "\n")
        with open(os.path.join(self.out, "startup_check.json"), "w", encoding="utf-8") as f:
            json.dump({"summary": head, "exit": code, "checks": self.items, "starts": self.starts}, f, ensure_ascii=False, indent=1, default=str)
        for line in lines:
            self.log(line)


class SetupError(Exception):
    pass


class StartError(Exception):
    pass


class GateAbort(Exception):
    pass


def main(argv):
    for s in (sys.stdout, sys.stderr):
        try:
            s.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, ValueError):
            pass
    ap = argparse.ArgumentParser(description="Start-up times of the app player SU1-SU3, SU5 (numbers only).")
    ap.add_argument("--exe", required=True)
    ap.add_argument("--project", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--port", type=int, default=DEFAULT_PORT)
    ap.add_argument("--warm", type=int, default=3)
    ap.add_argument("--cold", type=int, default=1)
    ap.add_argument("--modes", default="batch,ui")
    ap.add_argument("--gpu-lock")
    ap.add_argument("--input-lock")
    ap.add_argument("--quiet-lock")
    ap.add_argument("--ready-timeout-s", type=int, default=300)
    ap.add_argument("--timeout-min", type=float, default=15.0)
    ap.add_argument("--keep-copy", action="store_true")
    a = ap.parse_args(argv)
    R = Run(a)
    code, extra = 0, []
    dev0 = dev1 = {}
    try:
        try:
            R.setup()
        except GateAbort as e:
            R.add("SETUP", "INFO", "aborted by the game gate: %s" % e)
            R.write(3, [])
            return 3
        except (SetupError, OSError) as e:
            R.add("SETUP", "FAIL", "refused: %s" % e)
            R.write(2, [])
            return 2
        dev0 = M.device_events()
        try:
            R.run()
        except GateAbort as e:
            R.add("ABORT", "INFO", "aborted by the game gate: %s" % e)
            code = 3
        except Exception as e:
            R.add("ERROR", "FAIL", "%s: %s" % (type(e).__name__, str(e)[:300]), traceback=traceback.format_exc(limit=6))
            code = 1
        dev1 = M.device_events()
    finally:
        R.release()
    R.judge()
    extra.append("run: exe %s (modified %s), port %d, starts %d, device events before %s after %s; game gate %s"
                 % (R.exe, M.player_build_time(R.exe), a.port, len(R.starts), json.dumps(dev0), json.dumps(dev1),
                    R.gate_note or "%d process names" % len(R.gate)))
    if isinstance(dev0, dict) and isinstance(dev1, dict) and dev1.get("id153", 0) - dev0.get("id153", 0) > 0:
        R.add("DEVICE", "FAIL", "nvlddmkm id 153 +%d during the run" % (dev1["id153"] - dev0["id153"]))
    if code == 0 and any(x["verdict"] == "FAIL" for x in R.items):
        code = 1
    R.write(code, extra)
    return code


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
