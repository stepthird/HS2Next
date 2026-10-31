"""
allcards_player.py - every card of the card folders built once in the app player (graphics, the UI off) through the bridge:
scene.new, then the card added from its file, then the drawn materials of its five part sets read in the same request.
Counts build failures by class, managed exceptions, the materials left on the URP/Lit fallback (unfitted shaders), the
bundle opens whose textures the pre-upload texture check patched or read from a stored repaired copy and the bundles it
refused, the build times and native crashes. Numbers and ids only: no
image is captured, saved or opened; card files appear by count and by their index in the walk, never by name.

Usage (the whole-suite row AllCards.player; port 17396):
    python -I Tools\\allcards_player.py --exe <HS2Next.exe> --project <project root> --out <dir> [--port 17396]
                                        [--timeout-min 45] [--from N] [--limit N] [--max-restarts 5]
    more options: --lock-dir, --lock-wait-min, --no-locks (diagnosis only), --ram-wait-min, --min-free-gb, --label <text>,
                  --player-arg=<argument> (repeatable)
    The gates, locks and bounds are those of Tools\\maker_session.py (it is imported): the game gate of
    Tools\\session_gate.json, the GPU and input locks, free RAM, every bridge request bounded at 5 min, --timeout-min for the
    whole run, the System log's nvlddmkm / Display 4101 events before and after.

The walk: the files *.png under <hs2Root>/UserData/<targets.json cards.female>, then <cards.male>, each recursive and in
ordinal order of the relative path (the walk of the Editor's AllCards row). Per file one batch: scene.new, chara.load (the
file, the folder's sex), chara.edit.materials of face, body, hair, clothes and accessories (one property each); then
log.tail errors and content.stats. A file whose load fails is classed by the reply's error code (a file that holds no
character is a non-character file, as the Editor's row counts them). A native crash of the player ends that file as
"crashed": the player starts again and the walk resumes at the next file (at most --max-restarts restarts).

Checks (report lines):
  AP1  built cards = files - non-character files - failed cards; no failed card outside the non-character class
  AP2  no managed exception logged while building
  AP3  no native crash of the player
  AP4  no device loss (device-removed lines, nvlddmkm id 153, Display 4101)
  AP5  (info) unfitted shaders: renderer material slots drawn with Universal Render Pipeline/Lit (the fallback of a shader
       the substitute map does not know), the cards that have any, the most frequent material names; of them the slots whose
       material arrived with the engine's error shader (chara.load's finding shader.brokenInBundle and its slot count)
  AP6  (info) the texture check: bundle opens that patched a defective bundle now plus opens that read a stored repaired
       copy (content.stats textureCheck patched + repairedOpens), and the refused bundles
  AP7  (info) build times (chara.load buildMs): median, p95, max
Outputs: <out>\\allcards_player_report.txt (first and last line "SUMMARY: N PASS, M FAIL"), <out>\\allcards_player.json,
<out>\\cards.tsv (index, folder, ok, class, buildMs, unfitted slots, exceptions, patched, repaired opens, refused),
<out>\\run.log,
<out>\\player*.log.
Exit code: 0 no FAIL, 1 a check failed, 2 setup error or refused, 3 aborted by the game gate.
"""
import argparse
import collections
import json
import os
import re
import sys
import threading
import time
import traceback

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)        # python -I does not add the script's folder
import maker_session as M       # noqa: E402

DEFAULT_PORT = 17396
PARTS = ["face", "body", "hair", "clothes", "accessories"]
FALLBACK = "Universal Render Pipeline/Lit"
BROKEN_CODE = "shader.brokenInBundle"
CARD_CODE = re.compile(r"\b(card|import|chara)\.[A-Za-z]+\b")


# reply texts of a file that holds no character (the card header reader's findings)
NOT_A_CARD = ("neither a character", "not a card", "no hs2 card header", "without an iend", "notacard", "coordinate card",
              "not an hs2/ai character card")


def sanitize(text, path):
    """A reply text without the file's path or file name (card files appear by count only), at most 160 characters."""
    out = str(text)
    for token in (path, path.replace("\\", "/"), os.path.basename(path), os.path.splitext(os.path.basename(path))[0]):
        if token:
            out = out.replace(token, "<file>")
    return out[:160]


class Walk:
    def __init__(self, S, a):
        self.S = S
        self.a = a
        self.rows = []
        self.classes = collections.Counter()
        self.exc_kinds = collections.Counter()
        self.unfitted = collections.Counter()
        self.class_text = {}
        self.crashes = []
        self.hangs = []
        self.restarts = 0
        self.tc0 = None
        self.watch = None

    def files(self):
        """[(folder, absolute path)] in the walk's order."""
        with open(os.path.join(self.S.project, "Config", "settings.local.json"), encoding="utf-8-sig") as f:
            root = json.load(f).get("hs2Root")
        with open(os.path.join(self.S.project, "Tools", "targets.json"), encoding="utf-8") as f:
            cards = json.load(f).get("cards") or {}
        out = []
        for folder in ("female", "male"):
            rel = cards.get(folder)
            if not rel or not root:
                continue
            d = os.path.join(root, "UserData", rel.replace("/", os.sep))
            found = []
            for dp, dn, fn in os.walk(d):
                for name in fn:
                    if name.lower().endswith(".png"):
                        full = os.path.join(dp, name)
                        found.append((os.path.relpath(full, d).replace("\\", "/"), full))
            found.sort(key=lambda x: x[0])      # ordinal order of the relative path
            out += [(folder, full) for _, full in found]
        return out

    def stats(self):
        r = self.S.call({"cmd": "content.stats"}, timeout=60, check=False)
        return (r.get("textureCheck") or {}) if r.get("ok") else {}

    def restart(self):
        """A new player process after a native crash: the crashed player's log is kept as player_crash<n>.log, the watcher
        (game gate, wall bound) starts again."""
        S = self.S
        self.restarts += 1
        kept = os.path.join(S.out, "player_crash%d.log" % self.restarts)
        try:
            os.replace(S.player_log, kept)
            S.player_log_prev = getattr(S, "player_log_prev", []) + [kept]
        except OSError:
            pass
        S.abort_reason = None
        S.proc = None
        S.start_player()
        S.log_since = 0
        self.tc0 = None
        if self.watch is None or not self.watch.is_alive():
            self.watch = threading.Thread(target=S.watcher, name="watcher", daemon=True)
            self.watch.start()

    def one(self, i, folder, path):
        S = self.S
        cmds = [{"cmd": "scene.new"}, {"cmd": "chara.load", "path": path, "sex": folder, "as": "c"}]
        cmds += [{"cmd": "chara.edit.materials", "key": "$c.added.0.key", "part": p, "props": ["_MainTex"]} for p in PARTS]
        row = {"index": i, "folder": folder, "ok": False, "class": "", "buildMs": None, "unfitted": 0, "exceptions": 0,
               "patched": 0, "repaired": 0, "refused": 0, "broken": 0}
        if self.tc0 is None:
            self.tc0 = self.stats()
        t = time.perf_counter()
        r = S.call({"cmd": "batch", "cmds": cmds, "stopOnError": True}, timeout=300, check=False)
        row["rtMs"] = (time.perf_counter() - t) * 1000.0
        res = r.get("results") or []
        added = ((res[1].get("added") or [{}])[0]) if len(res) > 1 and isinstance(res[1], dict) else {}
        if r.get("ok") and len(res) == len(cmds) and added.get("state") not in (None, "Built"):
            row["class"] = "chara.load not built (%s)" % added.get("state")
            self.classes[row["class"]] += 1
        elif r.get("ok") and len(res) == len(cmds):
            row["ok"] = True
            row["buildMs"] = res[1].get("buildMs")
            for m in res[2:]:
                for rd in m.get("renderers") or []:
                    for sl in rd.get("slots") or []:
                        if sl.get("shader") == FALLBACK:
                            row["unfitted"] += 1
                            self.unfitted[str(sl.get("material") or "?")[:80]] += 1
            sev = collections.Counter(str(f.get("severity")) for f in (res[1].get("findings") or []) if isinstance(f, dict))
            row["findings"] = dict(sev)
            # slots whose material arrived with the engine's error shader (finding shader.brokenInBundle: "<n> material slot(s) ...")
            for f in res[1].get("findings") or []:
                if isinstance(f, dict) and f.get("code") == BROKEN_CODE:
                    m = re.match(r"\s*(\d+) material slot", str(f.get("message") or ""))
                    row["broken"] += int(m.group(1)) if m else 1
        else:
            at = r.get("failedAt")
            member = res[at] if isinstance(at, int) and at < len(res) else {}
            code = (member or {}).get("code") or r.get("code") or "?"
            err = str((member or {}).get("error") or r.get("error") or "")
            m = CARD_CODE.search(err)
            # the reply's text without the file's path or name (card files appear by count only)
            words = sanitize(err, path)
            row["class"] = "%s %s%s" % (["scene.new", "chara.load"][at] if at in (0, 1) else "materials", code,
                                        (" " + m.group(0)) if m else "")
            if at == 1 and (m and m.group(0) == "card.notACard" or any(k in err.lower() for k in NOT_A_CARD)):
                row["class"] += " (non-character file)"
            row["error"] = words
            self.classes[row["class"]] += 1
            self.class_text.setdefault(row["class"], words)
        # exceptions logged meanwhile (by type only: messages may name files)
        lt = S.call({"cmd": "log.tail", "n": 1000, "level": "error", "since": S.log_since}, timeout=60, check=False)
        if lt.get("ok"):
            S.log_since = lt.get("last", S.log_since)
            for e in lt.get("entries") or []:
                msg = str(e.get("message") or "")
                if e.get("type") == "exception" or M.EXC_LINE.match(msg):
                    row["exceptions"] += 1
                    self.exc_kinds[msg.split(":")[0][:60]] += 1
        tc = self.stats()
        if tc and self.tc0:
            row["patched"] = (tc.get("patched") or 0) - (self.tc0.get("patched") or 0)
            row["repaired"] = (tc.get("repairedOpens") or 0) - (self.tc0.get("repairedOpens") or 0)
            row["refused"] = (tc.get("refused") or 0) - (self.tc0.get("refused") or 0)
        self.tc0 = tc or self.tc0
        return row

    def run(self, files):
        S = self.S
        a = self.a
        todo = list(enumerate(files))[a.from_index:]
        if a.limit:
            todo = todo[:a.limit]
        t0 = time.perf_counter()
        for n, (i, (folder, path)) in enumerate(todo):
            S.check_abort()
            try:
                row = self.one(i, folder, path)
            except (M.Abort, M.CmdError) as e:
                if S.gate_abort or not S.proc or S.proc.poll() is None:
                    if isinstance(e, M.CmdError):          # a transport error with the player alive: recorded, the walk goes on
                        self.classes["transport"] += 1
                        self.rows.append({"index": i, "folder": folder, "ok": False, "class": "transport", "buildMs": None,
                                          "unfitted": 0, "exceptions": 0, "patched": 0, "repaired": 0, "refused": 0})
                        continue
                    if (not S.gate_abort and S.proc is not None and S.proc.poll() is None
                            and "passed its bound" in str(S.abort_reason or "") and self.restarts < a.max_restarts):
                        # a file whose request outlived its bound: the hung player is stopped by its PID, the walk goes on
                        S.log("card %d (%s): %s - the player is stopped by its PID and started again" % (i, folder, S.abort_reason))
                        self.hangs.append({"index": i, "folder": folder})
                        self.rows.append({"index": i, "folder": folder, "ok": False, "class": "hung (request bound)", "buildMs": None,
                                          "unfitted": 0, "exceptions": 0, "patched": 0, "repaired": 0, "refused": 0})
                        self.classes["hung (request bound)"] += 1
                        S.proc.kill()
                        try:
                            S.proc.wait(30)
                        except Exception:
                            pass
                        self.restart()
                        continue
                    raise
                # a native crash: the file is recorded and the walk resumes at the next one in a new player
                code = S.proc.poll()
                self.crashes.append({"index": i, "folder": folder, "exit": code})
                self.rows.append({"index": i, "folder": folder, "ok": False, "class": "native crash (exit %s)" % code,
                                  "buildMs": None, "unfitted": 0, "exceptions": 0, "patched": 0, "repaired": 0, "refused": 0})
                S.log("card %d (%s) ended the player (exit %s): %s" % (i, folder, code, e))
                if self.restarts >= a.max_restarts:
                    S.log("%d restarts: the walk stops" % self.restarts)
                    raise
                self.restart()
                continue
            self.rows.append(row)
            if n % 50 == 0 or not row["ok"]:
                S.log("card %d / %d (%s): %s, build %s ms, unfitted %d, exceptions %d, patched %d, repaired opens %d (%.0f s)"
                      % (i, len(files), folder, "ok" if row["ok"] else row["class"], M.fmt(row["buildMs"], 0), row["unfitted"],
                         row["exceptions"], row["patched"], row["repaired"], time.perf_counter() - t0))


def main(argv):
    for s in (sys.stdout, sys.stderr):
        try:
            s.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, ValueError):
            pass
    ap = argparse.ArgumentParser(description="Every card built once in the app player (AllCards.player; numbers only).")
    ap.add_argument("--exe")
    ap.add_argument("--project")
    ap.add_argument("--out")
    ap.add_argument("--port", type=int, default=DEFAULT_PORT)
    ap.add_argument("--timeout-min", type=float, default=45.0)
    ap.add_argument("--from", dest="from_index", type=int, default=0)
    ap.add_argument("--limit", type=int, default=0)
    ap.add_argument("--max-restarts", type=int, default=5)
    ap.add_argument("--lock-dir")
    ap.add_argument("--lock-wait-min", type=float, default=0.0)
    ap.add_argument("--no-locks", action="store_true")
    ap.add_argument("--ram-wait-min", type=float, default=10.0)
    ap.add_argument("--min-free-gb", type=float, default=8.0)
    ap.add_argument("--ready-timeout-min", type=float, default=12.0)
    ap.add_argument("--label")
    ap.add_argument("--player-arg", dest="player_args", action="append", default=[])
    a = ap.parse_args(argv)
    # the session's attributes the shared code reads
    a.minutes = a.timeout_min
    a.wall_min = a.timeout_min + a.lock_wait_min + a.ram_wait_min     # the walk's bound counts from the start of the script
    a.items, a.sample_sec, a.cycle_sec, a.seed, a.attach = None, 1e9, 300.0, M.DEFAULT_SEED, False
    a.gpu_guard_mb, a.shared_guard_mb, a.hover_search, a.hover_dwell_ms, a.wheel_delta = 7000.0, 4096.0, False, 0.0, 12.0
    a.frame_poll_ms, a.ui = 0.0, False
    a.label = a.label or "AllCards.player"
    S = M.Session(a)
    W = Walk(S, a)
    S.player_exit, S.quitting = None, False
    S.watch_stop = threading.Event()
    S.sampler_stop = threading.Event()
    S.poll_index = 0
    exit_code, dev0, dev1, watch, stop_how = 0, {}, {}, None, None
    files, status = [], "not started"
    t_start = time.perf_counter()
    try:
        try:
            S.load_config()
            S.preflight()
            files = W.files()
            if not files:
                raise M.SetupError("no card file under the card folders")
        except M.SetupError as e:
            status = "refused: %s" % e
            exit_code = 2
            raise
        dev0 = M.device_events()
        watch = threading.Thread(target=S.watcher, name="watcher", daemon=True)
        watch.start()
        W.watch = watch
        g0 = M.gpu_counters(None)
        S.info["adaptersBeforeMB"] = {k: round(v["ded"] / M.MIB) for k, v in (g0.get("adapters") or {}).items()}
        S.start_player()
        S.t_work = time.perf_counter()
        W.run(files)
        status = "completed"
    except M.SetupError as e:
        status = status if status.startswith("refused") else "setup error: %s" % e
        exit_code = 2
    except M.Abort as e:
        status = "aborted by the game gate" if S.gate_abort else "stopped: %s" % e
        exit_code = 3 if S.gate_abort else 1
    except Exception as e:
        status = "error: %s: %s" % (type(e).__name__, str(e)[:300])
        S.log(traceback.format_exc())
        exit_code = 1
    finally:
        S.quitting = True
        if S.proc is not None:
            g1 = M.gpu_counters(S.pid)
            ws, pb = M.process_memory(S.pid) if S.proc.poll() is None else (None, None)
            S.info["end"] = {"gpuDedMB": round(g1["procDed"] / M.MIB) if g1.get("procDed") is not None else None,
                             "wsMB": round(ws / M.MIB) if ws else None, "privMB": round(pb / M.MIB) if pb else None}
            S.player_exit, stop_how = S.stop_player("end of the walk" if not S.abort_reason else S.abort_reason)
        S.watch_stop.set()
        for w in (watch, W.watch):
            if w is not None:
                w.join(30)
        if S.locks is not None:
            S.locks.release()
        if S.proc is not None:
            dev1 = M.device_events()
        exit_code = report(S, W, a, files, status, exit_code, dev0, dev1, stop_how, time.perf_counter() - t_start)
        S.logf.close()
    return exit_code


def report(S, W, a, files, status, exit_code, dev0, dev1, stop_how, seconds):
    rows = W.rows
    scans = []
    for p in (getattr(S, "player_log_prev", []) or []) + ([S.player_log] if getattr(S, "player_log", None) else []):
        try:
            S.player_log = p
            scans.append(S.scan_player_log())
        except Exception:
            pass
    nonchar = sum(v for k, v in W.classes.items() if "non-character" in k)
    failed = [r for r in rows if not r["ok"]]
    other_fail = [r for r in failed if "non-character" not in r["class"] and not r["class"].startswith("native crash")]
    built = sum(1 for r in rows if r["ok"])
    exc = sum(r["exceptions"] for r in rows)
    devl = sum(x.get("deviceLines", 0) for x in scans)
    crash = len(W.crashes) + sum(1 for x in scans if x.get("nativeCrash") and not W.crashes)
    d153 = (dev1.get("id153", 0) - dev0.get("id153", 0)) if "id153" in dev0 and "id153" in dev1 else None
    d4101 = (dev1.get("display4101", 0) - dev0.get("display4101", 0)) if "display4101" in dev0 and "display4101" in dev1 else None
    unf_cards = sum(1 for r in rows if r["unfitted"])
    unf_slots = sum(r["unfitted"] for r in rows)
    brk_cards = sum(1 for r in rows if r.get("broken"))
    brk_slots = sum(r.get("broken", 0) for r in rows)
    bms = [r["buildMs"] for r in rows if r["buildMs"] is not None]
    patched = sum(r["patched"] for r in rows)
    repaired = sum(r.get("repaired", 0) for r in rows)
    repaired_cards = sum(1 for r in rows if r.get("repaired", 0) or r["patched"])
    refused = sum(r["refused"] for r in rows)
    checks = []
    walked = len(rows)
    complete = walked == len(files[a.from_index:][:a.limit or None])
    checks.append(("AP1", "PASS" if complete and not other_fail else "FAIL",
                   "%d of %d files walked: %d built, %d non-character files, %d other failures%s"
                   % (walked, len(files), built, nonchar, len(other_fail),
                      (" (" + ", ".join("%s x%d: \"%s\"" % (k, v, W.class_text.get(k, "")[:100])
                                        for k, v in W.classes.most_common(5)) + ")") if W.classes else "")))
    checks.append(("AP2", "PASS" if exc == 0 else "FAIL", "managed exceptions while building: %d%s"
                   % (exc, (" (" + ", ".join("%s x%d" % kv for kv in W.exc_kinds.most_common(5)) + ")") if exc else "")))
    checks.append(("AP3", "PASS" if crash == 0 and not W.hangs else "FAIL", "native crashes: %d%s; hung files (request bound): %d%s; "
                   "player restarts %d" % (crash, (" at walk indices " + ", ".join(str(c["index"]) for c in W.crashes)) if W.crashes else "",
                                           len(W.hangs), (" at " + ", ".join(str(h["index"]) for h in W.hangs)) if W.hangs else "", W.restarts)))
    checks.append(("AP4", "PASS" if devl == 0 and not d153 and not d4101 else "FAIL",
                   "device-removed lines %d, nvlddmkm id 153 +%s, Display 4101 +%s (before %s, after %s)"
                   % (devl, M.fmt(d153), M.fmt(d4101), json.dumps(dev0), json.dumps(dev1))))
    checks.append(("AP5", "INFO", "unfitted shaders (slots drawn with the URP/Lit fallback): %d slots on %d of %d built cards; most "
                   "frequent materials: %s; of them %d slots on %d cards arrived with the engine's error shader (finding %s: a shader "
                   "that did not load from its bundle or a slot without a material), %d ordinary fallbacks"
                   % (unf_slots, unf_cards, built, ", ".join("%s x%d" % kv for kv in W.unfitted.most_common(8)), brk_slots, brk_cards,
                      BROKEN_CODE, unf_slots - brk_slots)))
    checks.append(("AP6", "INFO", "texture check during the walk: %d bundle opens with repaired textures (patched now %d + read from "
                   "a stored repaired copy %d) on %d cards; %d refused" % (patched + repaired, patched, repaired, repaired_cards, refused)))
    checks.append(("AP7", "INFO", "build ms (chara.load buildMs): median %s, p95 %s, max %s; the walk %.1f min (%.2f s per file)"
                   % (M.fmt(M.median(bms), 0), M.fmt(M.pct(bms, 95), 0), M.fmt(max(bms) if bms else None, 0), seconds / 60.0,
                      seconds / max(1, walked))))
    fails = sum(1 for c in checks if c[1] == "FAIL")
    if exit_code == 0 and fails:
        exit_code = 1
    head = "SUMMARY: %d PASS, %d FAIL" % (sum(1 for c in checks if c[1] == "PASS"), fails)
    lines = [head, "run: %s; exe %s (built %s), port %s, player exit %s (%s), end memory %s, status %s"
             % (S.stamp, S.exe, S.info.get("exeModified"), S.port, S.player_exit, stop_how, json.dumps(S.info.get("end")), status)]
    lines += ["%s %s %s" % c for c in checks] + [head]
    with open(os.path.join(S.out, "allcards_player_report.txt"), "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")
    with open(os.path.join(S.out, "cards.tsv"), "w", encoding="utf-8") as f:
        f.write("index\tfolder\tok\tclass\tbuildMs\tunfitted\texceptions\tpatched\trepaired\trefused\n")
        for r in rows:
            f.write("%d\t%s\t%d\t%s\t%s\t%d\t%d\t%d\t%d\t%d\n" % (r["index"], r["folder"], 1 if r["ok"] else 0, r["class"], M.fmt(r["buildMs"], 1),
                                                             r["unfitted"], r["exceptions"], r["patched"], r.get("repaired", 0), r["refused"]))
    with open(os.path.join(S.out, "allcards_player.json"), "w", encoding="utf-8") as f:
        json.dump({"summary": head, "checks": checks, "classes": dict(W.classes), "classText": W.class_text, "exceptionKinds": dict(W.exc_kinds),
                   "unfitted": dict(W.unfitted.most_common(100)), "brokenInBundle": {"slots": brk_slots, "cards": brk_cards,
                   "indices": [r["index"] for r in rows if r.get("broken")]}, "crashes": W.crashes, "info": S.info, "status": status}, f,
                  ensure_ascii=False, indent=1, default=str)
    for line in lines:
        S.log(line)
    return exit_code


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
