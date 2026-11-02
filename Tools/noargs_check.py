"""noargs_check - the bare exe of the distribution on an empty data root (row PlayerBuild.noargs; Phase 5.3).

Two starts of a plain copy of the distribution folder (never --dist itself: a start writes next to the exe in the portable mode):
  (1) no arguments, HS2NEXT_DATA_ROOT=<out>/データ root/ (spaces and Japanese characters) holding only Config/settings.local.json
      {hs2Root of the project's settings, cacheDir: the project's cache, bridge.allowQuit: true}:
      endpoint.json in <root>/UserData/Bridge names the player's PID with tokenRequired (<= 60 s); ping without the token -> 401;
      with the token from <root>/UserData/Bridge/token ping.appReady (<= 30 s); the folders under the root exactly the documented
      layout (Config, UserData - Cache only when it is the default; UserData's work folders); nothing new next to the exe; app.paths mode override with this
      root; app.quit -> exit 0 within 10 s and the quit line in Player.log (QT1) with no exception line after it.
  (2) the same copy with portable.txt and Config/settings.local.json beside the exe (HS2NEXT_DATA_ROOT names an empty safety
      folder that must stay empty): Config, UserData and Cache appear next to that exe, app.paths mode portable (UP8), quit.
The player writes Player.log to the per-user folder (LocalLow/stp3/HS2Next); it is copied into --out after each exit.

Usage:  python -I Tools/noargs_check.py --dist <project>/Builds/Dist/HS2Next --project <project> --out <out>
        [--port 17322] [--timeout-min 10] [--gpu-lock FILE] [--input-lock FILE]
The runner holds the GPU and input locks (windowed players take the focus), applies the game gate and waits for the port.
Report <out>/noargs_report.txt: first and last line "SUMMARY: N PASS, M FAIL"; one "PASS|FAIL <id> <text>" line per check;
numbers only. Exit code: 0 pass, 1 a check failed, 2 setup error / refused (no dist, port busy, no project settings),
3 aborted by the game gate (the owner's game started).
"""
import argparse
import json
import os
import shutil
import socket
import stat
import subprocess
import sys
import time
import urllib.error
import urllib.request

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

GAME = "sp26-cod"
USERDATA_WORK = {"scene", "chara", "coordinate", "pose", "Bridge", "cap", "bg", "frame"}
USERDATA_ALLOWED = USERDATA_WORK | {"config", "content", "Plugins", "migration.json", "app.lock"}
ROOT_ALLOWED = {"Config", "UserData", "Cache"}


class Report:
    def __init__(self):
        self.lines, self.passed, self.failed = [], 0, 0

    def check(self, cid, ok, text, detail=""):
        if ok:
            self.passed += 1
        else:
            self.failed += 1
        line = ("PASS " if ok else "FAIL ") + cid + " " + text + (("  " + str(detail)) if detail and not ok else "")
        self.lines.append(line)
        print(line, flush=True)
        return ok

    def info(self, text):
        self.lines.append("   " + text)
        print("   " + text, flush=True)

    def write(self, path, seconds):
        s = "SUMMARY: %d PASS, %d FAIL" % (self.passed, self.failed)
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            f.write(s + " (%.0f s)\n" % seconds)
            for l in self.lines:
                f.write(l + "\n")
            f.write(s + "\n")


def port_busy(port):
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
        s.settimeout(0.5)
        return s.connect_ex(("127.0.0.1", port)) == 0


def game_running():
    try:
        out = subprocess.run(["tasklist", "/FI", "IMAGENAME eq %s.exe" % GAME, "/NH"], capture_output=True, text=True, timeout=10).stdout
        return GAME.lower() in out.lower()
    except Exception:
        return False


def is_reparse(path):
    try:
        return bool(os.lstat(path).st_file_attributes & stat.FILE_ATTRIBUTE_REPARSE_POINT)
    except OSError:
        return False


def remove_own_tree(path):
    """Removes a folder this script made, after proving it holds no reparse point (never follows a link)."""
    if not os.path.exists(path):
        return
    for d, dirs, files in os.walk(path):
        for n in dirs + files:
            if is_reparse(os.path.join(d, n)):
                raise RuntimeError("refusing to delete %s: it holds a reparse point (%s)" % (path, os.path.join(d, n)))
    shutil.rmtree(path)


def plain_copy(src, dst):
    """A file-by-file copy of the distribution folder; a reparse point anywhere refuses the copy."""
    for d, dirs, files in os.walk(src):
        rel = os.path.relpath(d, src)
        os.makedirs(os.path.join(dst, rel), exist_ok=True)
        for n in list(dirs):
            if is_reparse(os.path.join(d, n)):
                raise RuntimeError("the distribution holds a reparse point: " + os.path.join(rel, n))
        for n in files:
            p = os.path.join(d, n)
            if is_reparse(p):
                raise RuntimeError("the distribution holds a reparse point: " + os.path.join(rel, n))
            shutil.copy2(p, os.path.join(dst, rel, n))


def listing(path):
    out = set()
    for d, dirs, files in os.walk(path):
        for n in dirs + files:
            out.add(os.path.relpath(os.path.join(d, n), path))
    return out


def http(port, body, token=None, timeout=10):
    """(status, json or text) of one request."""
    req = urllib.request.Request("http://127.0.0.1:%d/" % port, data=json.dumps(body).encode("utf-8"), method="POST",
                                 headers={"Content-Type": "application/json"})
    if token:
        req.add_header("X-HS2Next-Token", token)
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            text = r.read().decode("utf-8", "replace")
            try:
                return r.status, json.loads(text)
            except ValueError:
                return r.status, text
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode("utf-8", "replace")
    except Exception as e:
        return 0, str(e)


def per_user_log():
    return os.path.join(os.environ.get("USERPROFILE", ""), "AppData", "LocalLow", "stp3", "HS2Next", "Player.log")


def wait_exit(p, seconds):
    try:
        return p.wait(seconds)
    except subprocess.TimeoutExpired:
        return None


def stop(p):
    if p.poll() is None:
        subprocess.run(["taskkill", "/PID", str(p.pid), "/T", "/F"], capture_output=True)
        wait_exit(p, 20)


def quit_lines(log_path):
    """(the quit line was logged, exception lines after it)."""
    try:
        with open(log_path, encoding="utf-8", errors="replace") as f:
            lines = f.read().splitlines()
    except OSError:
        return False, -1
    idx = [i for i, l in enumerate(lines) if l.startswith("[app] quit:")]
    if not idx:
        return False, -1
    after = [l for l in lines[idx[-1] + 1:] if "Exception" in l]
    return True, len(after)


def start(exe, env, out_log):
    # a bare exe only on a data root of this check: without HS2NEXT_DATA_ROOT it would write into the owner's per-user folder
    if not env.get("HS2NEXT_DATA_ROOT"):
        raise RuntimeError("refusing a bare start without HS2NEXT_DATA_ROOT (the per-user folder is the owner's)")
    # the distributed exe checks GitHub for a newer release at start: never from a test
    env = dict(env)
    env.setdefault("HS2NEXT_UPDATE_CHECK", "off")
    return subprocess.Popen([exe], cwd=os.path.dirname(exe), env=env, stdout=out_log, stderr=subprocess.STDOUT)


def wait_endpoint(bridge_dir, pid, seconds, p):
    ep = os.path.join(bridge_dir, "endpoint.json")
    t0 = time.time()
    while time.time() - t0 < seconds and p.poll() is None:
        if game_running():
            return "game"
        try:
            with open(ep, encoding="utf-8") as f:
                info = json.load(f)
            if info.get("pid") == pid:
                return info
        except (OSError, ValueError):
            pass
        time.sleep(0.2)
    return None


def wait_ready(port, token, seconds, p):
    t0 = time.time()
    last = None
    while time.time() - t0 < seconds and p.poll() is None:
        st, j = http(port, {"cmd": "ping"}, token, timeout=5)
        last = j
        if st == 200 and isinstance(j, dict) and j.get("appReady"):
            return j, time.time() - t0
        time.sleep(0.25)
    return last, None


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--dist", required=True)
    ap.add_argument("--project", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--port", type=int, default=17322)
    ap.add_argument("--timeout-min", type=float, default=10)
    ap.add_argument("--gpu-lock")
    ap.add_argument("--input-lock")
    a = ap.parse_args()
    t_all = time.time()
    rep = Report()
    out = os.path.abspath(a.out)
    os.makedirs(out, exist_ok=True)
    report_path = os.path.join(out, "noargs_report.txt")
    locks = []

    def finish(code):
        for l in locks:
            try:
                os.remove(l)
            except OSError:
                pass
        rep.write(report_path, time.time() - t_all)
        sys.exit(code)

    dist = os.path.abspath(a.dist)
    exe_name = "HS2Next.exe"
    if not os.path.isfile(os.path.join(dist, exe_name)):
        rep.check("setup", False, "the distribution folder holds HS2Next.exe", dist)
        finish(2)
    if port_busy(a.port):
        rep.check("setup", False, "port %d is free" % a.port)
        finish(2)
    try:
        with open(os.path.join(a.project, "Config", "settings.local.json"), encoding="utf-8") as f:
            proj = json.load(f)
        hs2 = proj.get("hs2Root")
        hs2 = os.path.abspath(os.path.join(a.project, hs2)) if hs2 else None
    except (OSError, ValueError) as e:
        hs2 = None
    if not hs2 or not os.path.isdir(hs2):
        rep.check("setup", False, "the project's settings name the HS2 folder", hs2)
        finish(2)
    for lk in (a.gpu_lock, a.input_lock):
        if lk:
            fd = os.open(lk, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
            os.write(fd, ("F noargs_check PID %d start %s\n" % (os.getpid(), time.strftime("%Y-%m-%d %H:%M:%S"))).encode())
            os.close(fd)
            locks.append(lk)
    cache = os.path.join(os.path.abspath(a.project), "Cache")
    limit = a.timeout_min * 60
    try:
        # ---------------- run 1: no arguments, an override root with spaces and Japanese characters
        copy1 = os.path.join(out, "copy")
        remove_own_tree(copy1)
        plain_copy(dist, copy1)
        root = os.path.join(out, "データ root")
        remove_own_tree(root)
        os.makedirs(os.path.join(root, "Config"))
        with open(os.path.join(root, "Config", "settings.local.json"), "w", encoding="utf-8") as f:
            json.dump({"hs2Root": hs2, "cacheDir": cache, "bridge": {"allowQuit": True}}, f, ensure_ascii=False, indent=1)
        exe = os.path.join(copy1, exe_name)
        before = listing(copy1)
        env = dict(os.environ)
        env["HS2NEXT_DATA_ROOT"] = root
        env.setdefault("HS2NEXT_NO_PLUGINS", "1")
        env.setdefault("HS2NEXT_PRIVATE_OFF", "1")
        with open(os.path.join(out, "run1_stdout.txt"), "w", encoding="utf-8") as so:
            t0 = time.time()
            p = start(exe, env, so)
            try:
                info = wait_endpoint(os.path.join(root, "UserData", "Bridge"), p.pid, 60, p)
                if info == "game":
                    stop(p)
                    rep.info("aborted by the game gate")
                    finish(3)
                rep.check("UP7", isinstance(info, dict), "endpoint.json under the root names the player's PID (%.1f s)" % (time.time() - t0), info)
                rep.check("TK6", isinstance(info, dict) and info.get("tokenRequired") is True, "endpoint.json says tokenRequired", info)
                st, body = http(a.port, {"cmd": "ping"})
                rep.check("TK6", st == 401, "ping without the token -> 401", "%s %s" % (st, str(body)[:200]))
                token = None
                try:
                    with open(os.path.join(root, "UserData", "Bridge", "token"), encoding="utf-8") as f:
                        token = f.read().strip()
                except OSError:
                    pass
                rep.check("TK6", bool(token) and len(token) == 32, "the token file holds a 32-digit token")
                ping, secs = wait_ready(a.port, token, 30, p)
                rep.check("A1", secs is not None, "ping.appReady with the token within 30 s (%.1f s after the endpoint)" % (secs or -1),
                          json.dumps(ping)[:300] if ping is not None else "no reply")
                rep.info("start to ready %.1f s" % (time.time() - t0))
                st, paths = http(a.port, {"cmd": "app.paths"}, token)
                ok_paths = st == 200 and isinstance(paths, dict) and paths.get("mode") == "override" and \
                    os.path.normcase(os.path.abspath(paths.get("dataRoot") or "")) == os.path.normcase(root)
                rep.check("UP7", ok_paths, "app.paths: mode override, this root", json.dumps(paths, ensure_ascii=False)[:300])
                top = set(os.listdir(root))
                ud = set(os.listdir(os.path.join(root, "UserData"))) if os.path.isdir(os.path.join(root, "UserData")) else set()
                # Cache appears under the root only as the default cache (this settings file names the project's cache)
                rep.check("UP7", {"Config", "UserData"} <= top <= ROOT_ALLOWED, "the root holds Config and UserData (and Cache only as the default cache), nothing else", sorted(top))
                rep.check("UP7", USERDATA_WORK <= ud and ud <= USERDATA_ALLOWED, "UserData holds the documented folders and files only", sorted(ud))
                new = listing(copy1) - before
                rep.check("UP7", not new, "nothing new next to the exe", sorted(new)[:10])
                st, q = http(a.port, {"cmd": "app.quit"}, token)
                rep.check("QT1", st == 200 and isinstance(q, dict) and q.get("ok"), "app.quit answers", str(q)[:200])
                code = wait_exit(p, 10)
                rep.check("QT1", code == 0, "the player exits 0 within 10 s", code)
            finally:
                stop(p)
        log1 = os.path.join(out, "run1_Player.log")
        try:
            shutil.copy2(per_user_log(), log1)
        except OSError:
            pass
        ql, exc = quit_lines(log1)
        rep.check("QT1", ql and exc == 0, "Player.log has the quit line and no exception line after it", "quit line %s, exceptions after %d" % (ql, exc))

        # ---------------- run 2: the portable mode
        copy2 = os.path.join(out, "copy_portable")
        remove_own_tree(copy2)
        plain_copy(dist, copy2)
        with open(os.path.join(copy2, "portable.txt"), "w", encoding="utf-8") as f:
            f.write("portable\n")
        os.makedirs(os.path.join(copy2, "Config"))
        with open(os.path.join(copy2, "Config", "settings.local.json"), "w", encoding="utf-8") as f:
            json.dump({"hs2Root": hs2, "cacheDir": cache, "bridge": {"allowQuit": True}}, f, ensure_ascii=False, indent=1)
        safety = os.path.join(out, "safety root")
        remove_own_tree(safety)
        os.makedirs(safety)
        env2 = dict(env)
        env2["HS2NEXT_DATA_ROOT"] = safety
        exe2 = os.path.join(copy2, exe_name)
        with open(os.path.join(out, "run2_stdout.txt"), "w", encoding="utf-8") as so:
            p = start(exe2, env2, so)
            try:
                info = wait_endpoint(os.path.join(copy2, "UserData", "Bridge"), p.pid, 60, p)
                if info == "game":
                    stop(p)
                    rep.info("aborted by the game gate")
                    finish(3)
                rep.check("UP8", isinstance(info, dict), "portable: endpoint.json next to the exe names the PID", info)
                token = None
                try:
                    with open(os.path.join(copy2, "UserData", "Bridge", "token"), encoding="utf-8") as f:
                        token = f.read().strip()
                except OSError:
                    pass
                ping, secs = wait_ready(a.port, token, 30, p)
                rep.check("UP8", secs is not None, "portable: ping.appReady", json.dumps(ping)[:200] if ping is not None else "no reply")
                st, paths = http(a.port, {"cmd": "app.paths"}, token)
                rep.check("UP8", st == 200 and isinstance(paths, dict) and paths.get("mode") == "portable" and
                          os.path.normcase(os.path.abspath(paths.get("dataRoot") or "")) == os.path.normcase(copy2),
                          "portable: app.paths mode portable, the exe folder", json.dumps(paths, ensure_ascii=False)[:300])
                # the settings name the project's cache, so no Cache folder is made next to the exe
                for n in ("Config", "UserData"):
                    rep.check("UP8", os.path.isdir(os.path.join(copy2, n)), "portable: %s next to the exe" % n)
                rep.check("UP8", os.path.isdir(os.path.join(copy2, "UserData", "Bridge")) and os.path.isdir(os.path.join(copy2, "UserData", "scene")),
                          "portable: UserData's work folders next to the exe")
                rep.check("UP8", not os.listdir(safety), "portable: HS2NEXT_DATA_ROOT is not used (portable.txt wins)", os.listdir(safety)[:5])
                st, q = http(a.port, {"cmd": "app.quit"}, token)
                code = wait_exit(p, 10)
                rep.check("QT1", code == 0, "portable: the player exits 0 within 10 s", code)
            finally:
                stop(p)
        try:
            shutil.copy2(per_user_log(), os.path.join(out, "run2_Player.log"))
        except OSError:
            pass
    except SystemExit:
        raise
    except Exception as e:
        rep.check("setup", False, "the check ran to its end", "%s: %s" % (type(e).__name__, e))
        finish(2 if rep.failed == 1 and rep.passed == 0 else 1)
    finish(0 if rep.failed == 0 else 1)


if __name__ == "__main__":
    main()
