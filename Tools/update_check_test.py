"""UC7 (added after 1.0.0): the app's update check against a local release server.

Starts a local server that answers like the GitHub latest-release request, then the Editor app with the Studio UI
(Tools/unity_batch.ps1 -Method BridgeServe.Run -NoQuit, MethodArgs -ui -updateCheck -updateCheckUrl <local>), and checks
through the bridge (ui.state, ui.query, ui.input, ui.action):
  1  at start, a newer release (v9.9.9): dialog "update" naming 9.9.9 and the build's version; Skip this version writes
     skipped v9.9.9 (lastResult newer) into <UserData>/config/update_check.json
  2  by hand (help.checkUpdates), the same version as the build: dialog "update" saying it is the latest
  3  by hand, the server answers 403 (GitHub's rate limit): dialog "update" saying the check failed (HTTP 403)
  4  by hand, the server stopped: dialog "update" saying the check failed
  5  every request carried User-Agent HS2Next/<version>, the GitHub Accept header and the API version header
The state file of the developer's UserData is put back (or removed) afterwards; nothing reaches GitHub.

Usage: py -3.14 -I Tools/update_check_test.py [--port 17641] [--serve-minutes 20]
Exit 0 pass, 1 fail. Report: Spike/out/update_check/report.txt
"""
import argparse
import http.server
import json
import os
import shutil
import subprocess
import sys
import threading
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent
sys.path.insert(0, str(HERE))     # python -I does not add the script's folder
import hs2n_bridge  # noqa: E402

REPORT = ROOT / "Spike" / "out" / "update_check" / "report.txt"


class Release:
    """What the local server answers: (status, body) and the headers of every request."""
    def __init__(self):
        self.status, self.body = 200, {}
        self.seen = []


def serve(port, rel):
    class H(http.server.BaseHTTPRequestHandler):
        def do_GET(self):
            rel.seen.append({k: v for k, v in self.headers.items()})
            data = json.dumps(rel.body).encode("utf-8")
            self.send_response(rel.status)
            self.send_header("Content-Type", "application/json; charset=utf-8")
            self.send_header("Content-Length", str(len(data)))
            self.end_headers()
            self.wfile.write(data)

        def log_message(self, *a):
            pass
    srv = http.server.ThreadingHTTPServer(("127.0.0.1", port), H)
    threading.Thread(target=srv.serve_forever, daemon=True).start()
    return srv


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--port", type=int, default=17641)
    ap.add_argument("--serve-minutes", type=int, default=20)
    a = ap.parse_args()
    REPORT.parent.mkdir(parents=True, exist_ok=True)
    lines, fails = [], []

    def say(s):
        print(s, flush=True)
        lines.append(s)

    def check(ok, what):
        say(("PASS " if ok else "FAIL ") + what)
        if not ok:
            fails.append(what)

    rel = Release()
    rel.body = {"tag_name": "v9.9.9", "name": "HS2Next 9.9.9", "html_url": "https://github.com/stepthird/HS2Next/releases/tag/v9.9.9",
                "published_at": "2026-11-02T00:00:00Z"}
    # the Editor's UserData is the project's (the start check writes the state file before the bridge can say where): kept
    # aside before the start, put back afterwards
    state_file = ROOT / "UserData" / "config" / "update_check.json"
    backup = state_file.read_bytes() if state_file.exists() else None
    srv = serve(a.port, rel)
    url = "http://127.0.0.1:%d/releases/latest" % a.port
    margs = ",".join(["-serveMinutes", str(a.serve_minutes), "-ui", "-updateCheck", "-updateCheckUrl", url])
    unity = subprocess.Popen(["pwsh", "-NoProfile", "-File", str(HERE / "unity_batch.ps1"), "-Method", "BridgeServe.Run", "-NoQuit",
                              "-MethodArgs:" + margs, "-LogName", "update_check_test"], cwd=str(ROOT))
    b = hs2n_bridge.Bridge(url="http://127.0.0.1:17322/", timeout=30, retries=0)
    try:
        t0 = time.time()
        while True:
            try:
                p = b.call({"cmd": "ping"})
                if p.get("appReady") and all((p.get("ready") or {}).values()):
                    break
            except Exception:  # noqa: BLE001 - not up yet
                pass
            if unity.poll() is not None or time.time() - t0 > 900:
                raise SystemExit("the app did not come up")
            time.sleep(3)
        version = b.call({"cmd": "bridge.version"}).get("app")
        user_data = b.call({"cmd": "app.paths"}).get("userData")
        check(Path(user_data).resolve() == (ROOT / "UserData").resolve(), "0 the app's UserData is the project's (%s)" % user_data)
        say("app %s, state file %s (kept aside: %s)" % (version, state_file, backup is not None))

        def modal(wait=30):
            t1 = time.time()
            while time.time() - t1 < wait:
                st = b.call({"cmd": "ui.state"})
                if st.get("modal"):
                    return st.get("modal")
                time.sleep(0.5)
            return None

        def message():
            q = b.call({"cmd": "ui.query", "name": ".hs-dialog__message"})
            els = q.get("elements") or ([q.get("element")] if q.get("element") else [])
            return " ".join(str(e.get("text") or "") for e in els if e)

        def press(button):
            b.call({"cmd": "ui.input", "events": [{"type": "pointer", "target": "dialog-button-" + button, "phase": "down"},
                                                  {"type": "pointer", "target": "dialog-button-" + button, "phase": "up"}], "frames": 3})
            t1 = time.time()
            while b.call({"cmd": "ui.state"}).get("modal") and time.time() - t1 < 10:
                time.sleep(0.3)

        # 1 at start
        m = modal(60)
        msg = message() if m else ""
        check(m == "update" and "9.9.9" in msg and (version or "?") in msg, "1 at start: dialog update for 9.9.9 (%s: %s)" % (m, msg))
        if m == "update":
            press("skip")
        st = json.loads(state_file.read_text(encoding="utf-8")) if state_file.exists() else {}
        check(st.get("skipped") == "v9.9.9" and st.get("lastResult") == "newer" and st.get("lastTag") == "v9.9.9",
              "1 Skip this version stores skipped v9.9.9 (%s)" % st)
        check(b.call({"cmd": "ui.state"}).get("modal") is None, "1 the dialog closed")

        # 2 by hand, the same version
        rel.body = dict(rel.body, tag_name="v" + (version or "0"))
        b.call({"cmd": "ui.action", "action": "help.checkUpdates"})
        m = modal()
        msg = message() if m else ""
        check(m == "update" and (version or "?") in msg and "9.9.9" not in msg and len(rel.seen) >= 2, "2 by hand, same version: latest (%s: %s)" % (m, msg))
        if m:
            press("ok")

        # 3 by hand, HTTP 403
        rel.status, rel.body = 403, {"message": "API rate limit exceeded"}
        b.call({"cmd": "ui.action", "action": "help.checkUpdates"})
        m = modal()
        msg = message() if m else ""
        check(m == "update" and "403" in msg, "3 by hand, HTTP 403: failed (%s: %s)" % (m, msg))
        if m:
            press("ok")

        # 4 by hand, the server gone
        srv.shutdown()
        srv.server_close()
        b.call({"cmd": "ui.action", "action": "help.checkUpdates"})
        m = modal(40)
        msg = message() if m else ""
        st = json.loads(state_file.read_text(encoding="utf-8")) if state_file.exists() else {}
        check(m == "update" and msg and st.get("lastResult") == "failed" and st.get("skipped") == "v9.9.9",
              "4 by hand, no server: failed, the skipped tag kept (%s: %s; %s)" % (m, msg, st))
        if m:
            press("ok")

        # 5 the request headers
        ua_ok = all(h.get("User-Agent") == "HS2Next/" + (version or "?") for h in rel.seen)
        acc_ok = all(h.get("Accept") == "application/vnd.github+json" and h.get("X-GitHub-Api-Version") == "2022-11-28" for h in rel.seen)
        check(len(rel.seen) == 3 and ua_ok and acc_ok, "5 %d requests, User-Agent HS2Next/%s, Accept and API version headers (%s)" % (
            len(rel.seen), version, rel.seen[:1]))
    finally:
        try:
            b.call({"cmd": "app.quit", "code": 0})
        except Exception:  # noqa: BLE001
            pass
        try:
            unity.wait(timeout=300)
        except subprocess.TimeoutExpired:
            say("Unity did not end within 5 minutes")
        try:
            srv.shutdown()
        except Exception:  # noqa: BLE001
            pass
        try:
            if backup is not None:
                state_file.write_bytes(backup)
                say("the state file put back")
            elif state_file.exists():
                state_file.unlink()
                say("the state file of this run removed")
        except OSError as e:
            say("state file not restored: %s" % e)
    say("RESULT: %s (%d failed)" % ("PASS" if not fails else "FAIL", len(fails)))
    REPORT.write_text("\n".join(lines) + "\n", encoding="utf-8")
    sys.exit(0 if not fails else 1)


if __name__ == "__main__":
    main()
