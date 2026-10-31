"""
mod_sweep_player - the mod sweep's player pass (Phase 5.2, row ModSweep.player): Studio item
rows that ended an Editor process of the sweep are added one by one in the app player with item.add and deleted, in a fresh
player per row, to state whether they also end a player. No new bridge command; numbers and ids only; no capture.

  python -I Tools/mod_sweep_player.py [--rows FILE] [--exe Builds/App/HS2Next.exe] [--out Spike/out/modsweep/player]
         [--port 17392] [--row-timeout-s 300] [--ready-timeout-s 600] [--settle-s 3] [--no-locks] [--lock-dir DIR]
         [--one-player] [--userdata DIR]

--rows: one row per line, "<guid>#<id>|<group>|<category>[|<bundle>|...]" (the bundle names a death's bundle in
--one-player; more fields are ignored; lines starting with "#" are comments); default Tools/mod_sweep_player_rows.txt, the
fixed list of the rows that once ended an Editor process (the row ModSweep.player), or the item rows of a sweep that ended
its process (`mod_sweep_report.py player-rows`), or the item rows of the bundles with an unusual Unity header
(`mod_sweep_report.py header-rows`, with --one-player).
--one-player: many rows cheaply - one player adds the rows in turn (settle 0.3 s unless --settle-s) and a fresh player
continues after a death, in one UserData per pass (--userdata, else a new <out>/userdata_<time>), so the app's crash
quarantine learns from a death and refuses that bundle's later rows; the RESULT line adds firstDeaths (the first death of
a bundle), repeatDeaths (a bundle that had already ended a player: the quarantine did not hold), quitDeaths and players.
Without it, the per-row mode below (its UserData: --userdata, else <out>/userdata). Per row: the game gate (Tools/session_gate.json; no row starts while a listed process runs, a running row polls and
ends the player by PID when one appears: "aborted by the game gate", not a verdict), the player started by path
(-batchmode, its bridge on --port, HS2NEXT_NO_PLUGINS=1, HS2NEXT_PRIVATE_OFF=1, its own UserData under --out), ready =
ping.appReady; item.add {guid, no, group, category}; --settle-s of pings (frames advance); delete; app.quit; the exit.
Verdicts: added (the player built the row and quit normally), refused (item.add answered ok false: its code), died (the
process ended during or after the add; its exit code), hung (no reply within --row-timeout-s; the player is ended by PID).
The player log is scanned for managed exceptions and device-removed lines and the System log's nvlddmkm events (all, id 153)
and Display 4101 are counted before the pass and after every row; a device loss (device-removed lines or a new nvlddmkm
event) ends the series.
Locks (Phase 5.2 machine rule): unless --no-locks (when the caller holds them), the GPU lock and the input lock of the lock
folder (--lock-dir, HS2NEXT_LOCK_DIR, else <system drive>\\tmp\\hs2next_tests) are both checked absent and created
exclusively for the pass, and removed at its end (also on failure); the helpers are those of Tools/maker_session.py.
Report <out>/mod_sweep_player_report.txt: one line per row, then "RESULT rows=N added=N refused=N died=N hung=N aborted=N
deaths=N deviceLoss=N" and the last line "SUMMARY: N PASS, M FAIL" (a row that ends or hangs the player is a FAIL).
Exit code 0 = no row ended a player, 1 = some row did, 2 = setup error (no player, port taken, locks held, game running).
"""
import argparse
import json
import os
import re
import subprocess
import sys
import time
from datetime import datetime, timedelta

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
sys.path.insert(0, HERE)
from hs2n_bridge import Bridge  # noqa: E402
from maker_session import (DEVICE_TEXT, EXC_LINE, Locks, SetupError, device_events,  # noqa: E402
                           read_game_gate)

CREATE_NO_WINDOW = 0x08000000
ROW = re.compile(r"^\s*([^#|\s][^#|]*)#(-?\d+)\|(-?\d+)\|(-?\d+)(?:\|([^|\r\n]*))?")


def game_running(names):
    if not names:
        return None
    try:
        out = subprocess.run(["tasklist", "/FO", "CSV", "/NH"], capture_output=True, timeout=60, creationflags=CREATE_NO_WINDOW).stdout
        text = out.decode("utf-8", "replace").lower()
    except Exception:
        return None
    for line in text.splitlines():
        image = line.split('","')[0].strip('"')
        for n in names:
            if image.startswith(n.lower()):
                return image
    return None


def read_rows(path):
    rows = []
    with open(path, encoding="utf-8", errors="replace") as f:
        for line in f:
            m = ROW.match(line)
            if m:
                rows.append((m.group(1).strip(), int(m.group(2)), int(m.group(3)), int(m.group(4)), (m.group(5) or "").strip()))
    return rows


def log_counts(logf):
    """Managed exception lines and device-removed lines of a player log."""
    exc = devline = 0
    if os.path.isfile(logf):
        for line in open(logf, encoding="utf-8", errors="replace"):
            if EXC_LINE.match(line):
                exc += 1
            low = line.lower()
            if any(t in low for t in DEVICE_TEXT):
                devline += 1
    return exc, devline


def one_player(a, rows, names, poll, locks, env, settings, dev0, counts, extra, say):
    """--one-player: the rows go through one player in turn (item.add, --settle-s of pings, delete); after a death or a hang
    a fresh player continues with the next row, in the same UserData (so the crash quarantine of the app sees the death).
    The UserData is --userdata, else a new folder <out>/userdata_<time> per pass. Deaths are split: firstDeaths (the first
    death of a bundle in the pass), repeatDeaths (a bundle that had already ended a player: the quarantine did not hold),
    quitDeaths (a nonzero exit at app.quit). The System log is read every 25 rows and after each death; a device loss ends
    the pass, as does the game gate (the rows left are aborted). Returns 2 on a setup error, else 0."""
    ud = a.userdata or os.path.join(a.out, "userdata_" + time.strftime("%Y%m%d-%H%M%S"))
    bo = os.path.join(a.out, "bridge_out")
    say("one player: UserData %s; settle %.1f s" % (ud, a.settle_s))
    extra.update({"firstDeaths": 0, "repeatDeaths": 0, "quitDeaths": 0, "players": 0})
    died_bundles = set()
    proc = br = None
    logf = None
    last_gate = 0.0

    def device_loss(logf):
        exc, devline = log_counts(logf) if logf else (0, 0)
        devr = device_events()
        dnv = devr["nvlddmkm"] - dev0["nvlddmkm"] if "nvlddmkm" in devr and "nvlddmkm" in dev0 else 0
        return exc, devline, dnv

    def stop_device(devline, dnv):
        counts["deviceLoss"] += 1
        say("STOP: a device loss (device-removed lines %d, nvlddmkm events since the start +%d) ends the pass" % (devline, dnv))

    i = 0
    try:
        while i < len(rows):
            guid, no, group, category, bundle = rows[i]
            ref = "%s#%d" % (guid, no)
            if proc is None or time.time() - last_gate > poll:
                g = game_running(names)
                last_gate = time.time()
                if g:
                    left = len(rows) - i
                    counts["rows"] += left; counts["aborted"] += left
                    say("aborted by the game gate (%s running): %d rows left" % (g, left))
                    break
            if proc is None:
                extra["players"] += 1
                logf = os.path.join(a.out, "player_%d.log" % extra["players"])
                args = [a.exe, "-batchmode", "-bridgePort", str(a.port), "-bridgeAllowQuit", "-settings", settings, "-cacheDir",
                        os.path.join(REPO, "Cache"), "-userData", ud, "-bridgeOutputDir", bo, "-logFile", logf]
                t0 = time.time()
                proc = subprocess.Popen(args, cwd=os.path.dirname(a.exe), stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL,
                                        stderr=subprocess.DEVNULL, creationflags=CREATE_NO_WINDOW, env=env)
                if locks:
                    locks.note_player(proc.pid)
                br = Bridge(url="http://127.0.0.1:%d/" % a.port)
                err = None
                while True:
                    if proc.poll() is not None:
                        err = "the player exited before ready (%s)" % proc.returncode
                        break
                    try:
                        if br.call({"cmd": "ping"}, timeout=5, check=False).get("appReady"):
                            break
                    except Exception:
                        pass
                    if time.time() - t0 > a.ready_timeout_s:
                        err = "not ready"
                        break
                    time.sleep(1)
                if err:
                    say("player %d: setup failed: %s" % (extra["players"], err))
                    return 2
                say("player %d ready %.0f s (pid %d) at row %d" % (extra["players"], time.time() - t0, proc.pid, i + 1))
                q = []
                if os.path.isfile(logf):
                    with open(logf, encoding="utf-8", errors="replace") as f:
                        q = [line.strip() for line in f if "content.quarantined" in line]
                if q:
                    say("player %d log: %s" % (extra["players"], q[0][:300]))
            counts["rows"] += 1
            t1 = time.time()
            reply, err = None, None
            try:
                reply = br.call({"cmd": "item.add", "guid": guid, "no": no, "group": group, "category": category},
                                timeout=a.row_timeout_s, check=False)
            except Exception as e:
                err = "%s: %s" % (type(e).__name__, str(e)[:160])
            add_s = time.time() - t1
            if err is not None or proc.poll() is not None:
                try:
                    proc.wait(60)   # a crashing player can take a while to write its dump
                except Exception:
                    pass
                if proc.poll() is not None:
                    verdict, detail = "died", "exit %s during item.add (%.1f s)" % (proc.returncode, add_s)
                else:
                    verdict, detail = "hung", "no reply to item.add within %.0f s (%s)" % (a.row_timeout_s, err)
            elif not reply.get("ok"):
                verdict, detail = "refused", "code %s" % reply.get("code")
            else:
                keys = [x.get("key") for x in reply.get("added") or [] if x.get("key")]
                t2 = time.time()
                while time.time() - t2 < a.settle_s and proc.poll() is None:
                    try:
                        br.call({"cmd": "ping"}, timeout=30, check=False)
                    except Exception:
                        break
                    time.sleep(0.1)
                if proc.poll() is not None:
                    verdict, detail = "died", "exit %s within %.1f s after the add" % (proc.returncode, a.settle_s)
                else:
                    for k in keys:
                        br.call({"cmd": "delete", "key": k}, timeout=a.row_timeout_s, check=False)
                    verdict, detail = "added", "item.add %.2f s, %d node(s)" % (add_s, len(keys))
            counts[verdict] += 1
            i += 1
            if verdict in ("died", "hung"):
                if proc.poll() is None:
                    proc.kill()
                    try:
                        proc.wait(60)
                    except Exception:
                        pass
                key = (bundle or ref).lower()
                extra["repeatDeaths" if key in died_bundles else "firstDeaths"] += 1
                died_bundles.add(key)
                exc, devline, dnv = device_loss(logf)
                say("%s %d/%d: %s - %s; player %d; bundle %s; log: %d exceptions, %d device-removed lines; nvlddmkm +%d"
                    % (ref, group, category, verdict, detail, extra["players"], bundle or "-", exc, devline, dnv))
                proc = br = None
                if devline or dnv > 0:
                    stop_device(devline, dnv)
                    break
                continue
            say("%s %d/%d: %s - %s; player %d" % (ref, group, category, verdict, detail, extra["players"]))
            if i % 25 == 0:
                exc, devline, dnv = device_loss(logf)
                if devline or dnv > 0:
                    stop_device(devline, dnv)
                    break
    finally:
        if proc is not None and proc.poll() is None:
            try:
                q = br.call({"cmd": "content.quarantine"}, timeout=30, check=False)
                say("player %d content.quarantine: %s" % (extra["players"], json.dumps({k: v for k, v in q.items() if k != "ok"})[:500]))
            except Exception as e:
                say("player %d content.quarantine: %s" % (extra["players"], type(e).__name__))
            try:
                br.call({"cmd": "app.quit"}, timeout=30, check=False)
            except Exception:
                pass
            try:
                proc.wait(120)
            except Exception:
                pass
            if proc.poll() is None:
                proc.kill()
                say("player %d: no exit within 120 s after app.quit (ended by PID)" % extra["players"])
            elif proc.returncode != 0:
                counts["died"] += 1; extra["quitDeaths"] += 1
                say("player %d: exit %s after app.quit (a death)" % (extra["players"], proc.returncode))
            else:
                say("player %d: exit 0 after app.quit" % extra["players"])
    return 0


def main():
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except Exception:
        pass
    ap = argparse.ArgumentParser()
    ap.add_argument("--rows", default=os.path.join(HERE, "mod_sweep_player_rows.txt"))
    ap.add_argument("--exe", default=os.path.join(REPO, "Builds", "App", "HS2Next.exe"))
    ap.add_argument("--out", default=os.path.join(REPO, "Spike", "out", "modsweep", "player"))
    ap.add_argument("--port", type=int, default=17392)
    ap.add_argument("--row-timeout-s", type=float, default=300)
    ap.add_argument("--ready-timeout-s", type=float, default=600)
    ap.add_argument("--settle-s", type=float)
    ap.add_argument("--one-player", action="store_true")
    ap.add_argument("--userdata")
    ap.add_argument("--no-locks", action="store_true")
    ap.add_argument("--lock-dir")
    a = ap.parse_args()
    if a.settle_s is None:
        a.settle_s = 0.3 if a.one_player else 3
    # the player runs in its own folder: a relative path would land under Builds/App
    a.out, a.exe = os.path.abspath(a.out), os.path.abspath(a.exe)
    a.userdata = os.path.abspath(a.userdata) if a.userdata else None
    os.makedirs(a.out, exist_ok=True)
    lines = []
    counts = {"rows": 0, "added": 0, "refused": 0, "died": 0, "hung": 0, "aborted": 0, "deviceLoss": 0}
    extra = {}   # --one-player: firstDeaths / repeatDeaths / quitDeaths / players, after deaths= on the RESULT line

    def say(s):
        lines.append(s)
        print(s, flush=True)

    def finish(code):
        deaths = counts["died"] + counts["hung"]
        say("RESULT " + " ".join("%s=%d" % (k, v) for k, v in counts.items()) + " deaths=%d" % deaths
            + "".join(" %s=%d" % kv for kv in extra.items()))
        fails = deaths + counts["deviceLoss"]
        say("SUMMARY: %d PASS, %d FAIL" % (counts["added"] + counts["refused"], fails))
        with open(os.path.join(a.out, "mod_sweep_player_report.txt"), "w", encoding="utf-8", newline="\n") as f:
            f.write("\n".join(lines) + "\n")
        return code if code else (1 if fails else 0)

    say("mod_sweep_player %s; player %s" % (time.strftime("%Y-%m-%d %H:%M:%S"), os.path.basename(a.exe)))
    if not os.path.isfile(a.exe):
        say("setup: no player"); return finish(2)
    rows = read_rows(a.rows) if os.path.isfile(a.rows) else []
    if not rows:
        say("setup: no rows in --rows"); return finish(2)
    names, poll, warn = read_game_gate(os.path.join(HERE, "session_gate.json"))
    if warn:
        say("game gate: " + warn)
    g = game_running(names)
    if g:
        say("setup: the game gate holds (%s running)" % g); return finish(2)
    lock_dir = a.lock_dir or os.environ.get("HS2NEXT_LOCK_DIR") or os.path.join(os.environ.get("SystemDrive", "C:") + os.sep, "tmp", "hs2next_tests")
    locks = None if a.no_locks else Locks(lock_dir, "mod_sweep_player", say)
    if locks:
        busy = locks.busy()
        if busy:
            say("setup: " + busy); return finish(2)
        try:
            per_row = (a.settle_s + 10) if a.one_player else (a.ready_timeout_s + a.row_timeout_s + 60)
            locks.acquire(datetime.now() + timedelta(seconds=a.ready_timeout_s + len(rows) * per_row))
        except SetupError as e:
            say("setup: %s" % e); return finish(2)
    env = dict(os.environ, HS2NEXT_NO_PLUGINS="1", HS2NEXT_PRIVATE_OFF="1")
    settings = os.path.join(REPO, "Config", "settings.local.json")
    dev0 = device_events()
    code = 0
    try:
        if a.one_player:
            code = one_player(a, rows, names, poll, locks, env, settings, dev0, counts, extra, say)
            rows = []   # the per-row loop below has nothing left to do
        for n, (guid, no, group, category, _bundle) in enumerate(rows):
            counts["rows"] += 1
            ref = "%s#%d" % (guid, no)
            g = game_running(names)
            if g:
                counts["aborted"] += 1; say("%s %d/%d: aborted by the game gate before the start (%s)" % (ref, group, category, g)); continue
            ud, bo, logf = (a.userdata or os.path.join(a.out, "userdata"), os.path.join(a.out, "bridge_out"), os.path.join(a.out, "player_%d.log" % n))
            args = [a.exe, "-batchmode", "-bridgePort", str(a.port), "-bridgeAllowQuit", "-settings", settings, "-cacheDir",
                    os.path.join(REPO, "Cache"), "-userData", ud, "-bridgeOutputDir", bo, "-logFile", logf]
            t0 = time.time()
            proc = subprocess.Popen(args, cwd=os.path.dirname(a.exe), stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL,
                                    stderr=subprocess.DEVNULL, creationflags=CREATE_NO_WINDOW, env=env)
            if locks:
                locks.note_player(proc.pid)
            br = Bridge(url="http://127.0.0.1:%d/" % a.port)
            verdict, detail = None, ""
            try:
                while verdict is None:
                    if proc.poll() is not None:
                        verdict, detail = "setup", "the player exited before ready (%s)" % proc.returncode
                        break
                    try:
                        if br.call({"cmd": "ping"}, timeout=5, check=False).get("appReady"):
                            break
                    except Exception:
                        pass
                    if time.time() - t0 > a.ready_timeout_s:
                        verdict, detail = "setup", "not ready"
                    time.sleep(1)
                if verdict == "setup":
                    say("%s %d/%d: setup failed: %s" % (ref, group, category, detail))
                    return finish(2)
                ready = time.time() - t0
                t1 = time.time()
                reply, err = None, None
                try:
                    reply = br.call({"cmd": "item.add", "guid": guid, "no": no, "group": group, "category": category},
                                    timeout=a.row_timeout_s, check=False)
                except Exception as e:
                    err = "%s: %s" % (type(e).__name__, str(e)[:160])
                add_s = time.time() - t1
                if proc.poll() is not None:
                    verdict, detail = "died", "exit %s during item.add (%.1f s)" % (proc.returncode, add_s)
                elif err is not None:
                    verdict, detail = "hung", "no reply to item.add within %.0f s (%s)" % (a.row_timeout_s, err)
                elif not reply.get("ok"):
                    verdict, detail = "refused", "code %s" % reply.get("code")
                else:
                    keys = [x.get("key") for x in reply.get("added") or [] if x.get("key")]
                    t2 = time.time()
                    while time.time() - t2 < a.settle_s and proc.poll() is None:
                        try:
                            br.call({"cmd": "ping"}, timeout=30, check=False)
                        except Exception:
                            break
                        time.sleep(0.2)
                        g = game_running(names) if time.time() - t2 > poll else None
                        if g:
                            break
                    if proc.poll() is not None:
                        verdict, detail = "died", "exit %s within %.0f s after the add" % (proc.returncode, a.settle_s)
                    else:
                        for k in keys:
                            br.call({"cmd": "delete", "key": k}, timeout=a.row_timeout_s, check=False)
                        verdict, detail = "added", "item.add %.2f s, %d node(s)" % (add_s, len(keys))
                if proc.poll() is None and verdict in ("added", "refused"):
                    try:
                        br.call({"cmd": "app.quit"}, timeout=30, check=False)
                    except Exception:
                        pass
                    try:
                        proc.wait(120)
                    except Exception:
                        pass
                    if proc.poll() is None:
                        detail += "; did not exit after app.quit (ended by PID)"
                    elif proc.returncode != 0:
                        verdict, detail = "died", detail + "; exit %s after app.quit" % proc.returncode
            finally:
                if proc.poll() is None:
                    proc.kill()
                    try:
                        proc.wait(60)
                    except Exception:
                        pass
            counts[verdict] += 1
            exc = devline = 0
            if os.path.isfile(logf):
                for line in open(logf, encoding="utf-8", errors="replace"):
                    if EXC_LINE.match(line):
                        exc += 1
                    low = line.lower()
                    if any(t in low for t in DEVICE_TEXT):
                        devline += 1
            devr = device_events()
            dnv = devr["nvlddmkm"] - dev0["nvlddmkm"] if "nvlddmkm" in devr and "nvlddmkm" in dev0 else 0
            if devline or dnv > 0:
                counts["deviceLoss"] += 1
            say("%s %d/%d: %s - %s; ready %.0f s; log: %d exceptions, %d device-removed lines; nvlddmkm events since the start +%d"
                % (ref, group, category, verdict, detail, ready, exc, devline, dnv))
            if devline or dnv > 0:
                say("STOP: a device loss (device-removed lines or new nvlddmkm events) ends the series")
                break
    finally:
        if locks:
            locks.release()
    dev1 = device_events()
    if "nvlddmkm" in dev0 and "nvlddmkm" in dev1:
        dn = dev1["nvlddmkm"] - dev0["nvlddmkm"]
        say("System log: nvlddmkm events +%d (id 153 +%d), Display 4101 +%d" % (dn, dev1["id153"] - dev0["id153"], dev1["display4101"] - dev0["display4101"]))
        if dn > 0 and not counts["deviceLoss"]:
            counts["deviceLoss"] = 1
    else:
        say("System log: not read (%s)" % (dev0.get("error") or dev1.get("error")))
    return finish(code)


if __name__ == "__main__":
    sys.exit(main())
