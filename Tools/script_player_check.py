"""
script_player_check.py - scene scripts in the built app player (Phase 6.9; the row ScriptCheck.player).

Starts the app player itself, several times, and checks it from outside the process, in real frames. It never uses the Unity Editor. Python 3.14, standard library only, plus dotnet for the
test plugin. Content: the clothed SFW visual targets of Tools\\targets.json in every role (female roles BR-Chan, male roles HERO107), judged by numbers; members only as MosaicTest's fixtures
use them, in memory. No image is opened or shown: captures and renders are read as numbers inside the player by the test plugin (Tests\\ScriptProbe) and deleted; the frames of a recording
are counted, hashed (SHA-256) and deleted by this script, never opened. No H animation is played. The per-user HS2Next data is never touched: every player runs with -settings, -userData
<its folder under --out>, -cacheDir and -logFile.

Usage (the run_tests.ps1 row ScriptCheck.player calls the first form):
    python -I Tools\\script_player_check.py --exe <path to HS2Next.exe> --project <project root> --out <dir> [--port 17909] [--timeout-min 80]
    more options: --only N,F,R,D or check ids (default all), --sample <the sample script> (default Tests\\SceneScripts\\sample.hs2nscript),
                  --scripts <file>[,<file>...] (more scripts recorded and played as the sample: JR5's count, JV3's warnings and casts),
                  --expect <json> (the failures each of those scripts is expected to have, by file name: [{phase, cmd, code, t}]),
                  --jd <file name>[,...] (which of --scripts also get JD1 and JD2; the sample always does), --baseline-exe <the last build, e.g. Builds\\AppPrev69\\HS2Next.exe> (JW7's frame),
                  --gfx d3d11 | d3d12, --min-free-gb 8, --ram-wait-min 10, --ready-timeout-min 12, --keep-frames (never: the frames are deleted after they are counted)
    -I ignores PYTHONUTF8, so the script sets its own stdout/stderr to UTF-8.

Sessions (one player at a time; windowed 1920 x 1080 with -noUi):
  no player  JW7s  the subscriptions of the script parts' code read (where they subscribe and when)
  N  -noBridge, the probe with a job file (no listener: nothing reaches it over HTTP): JX6 the host with no listener (its log line "in-process (no listener)", no endpoint.json, no token
     file), JX2's four timed commands in the player, JX4's script.play of the sample through the host's queue; JW4 (off) the host's start (the log's bridge stage), an inline no-op and an
     inline camera.get timed on the main thread
  O1, F, O2  -noBridge, the probe's frame meter on the start scene: O1 and O2 the last build (--baseline-exe) with the probe's frames-only build, F this build: JW7's frame
  R  port 17909, the probe: JR4 JR1 in the player (BR-Chan dancing, captures, screenshots and recorded frames against a render with forced recalculation and the hook-off control,
     per moment, at the screen's rate held at 60 fps); JR6 MosaicTest's fixture walked 8 units by obj.moveTo through capture, screenshot and a recording, (a) the wide pass off; (b) is
     not judged in a player (the wide pass's coverage view is Editor-only; the lead's ruling): one INFO line per path, (b) stands on ScriptTest.play's JR6;
     YT1 (Phase 6.10) BR-Chan dancing, 24 moments each of the scene thumbnail after one scene step in the command's frame, the scene thumbnail at a command's
     point, the Front and the View card thumbnail, each against a render with forced recalculation and the hook-off control; YT3 (a) JR6's fixture walked
     8 units by obj.moveTo, 24 moments through the scene thumbnail (504 x 704), the wide pass off
  D  port 17909, the probe: the sample and every --scripts file: imported into the scene, roles cast to the targets, script.play {load: true, record: {1280 x 720, 30 fps}} with digests
     at k x duration / 8 (JR5's count, JW6, JW8, JV3), saved, played again without a reload (JD2), loaded, played (JD1); JW1 compile, JW2 the Timeline's tick, JW3 the parts' tick,
     JW4 (on), JW5 a set-up of 20 steps, JW7's parts and bytes, JW9 the store
Outputs:
  <out>\\script_player_report.txt  first and last line "SUMMARY: N PASS, M FAIL", second line "WARN: K (ids)", then one line per check
  <out>\\script_player_check.json  details of every check
  <out>\\<n|o1|f|o2|r|d>\\player.log, userdata\\
Exit code: 0 every check passed (WARN allowed), 1 a check failed, 2 setup error, 3 aborted by the game gate. A time over its bar is WARN: the lead decides.
Port: 17909 (17919 = ScriptTest.play, 17322 = interactive default). Always 127.0.0.1, never localhost.
"""
import argparse
import glob
import hashlib
import json
import math
import os
import re
import shutil
import statistics
import subprocess
import sys
import threading
import time
import traceback
import types
import zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)        # python -I does not add the script's folder
from constraint_player_check import (Abort, Results, SetupError, free_gb, game_processes, http, log, port_listeners, running_games, target_rel, CREATE_NO_WINDOW, T0)  # noqa: E402
from wet_player_check import Session, check_log, kill, stop, utc_now  # noqa: E402

# bars of spec 17.3 (ms, µs, s); over a bar: WARN
BAR = {"JW1": 5.0, "JW2med": 100.0, "JW2p95": 200.0, "JW3": 50.0, "JW4start": 50.0, "JW4noop": 50.0, "JW4cam": 150.0, "JW4ratio": 1.5, "JW5": 1000.0, "JW6": 25.0,
       "JW7frame": 0.05, "JW8": 10.0, "JW9list": 100.0, "JW9import": 20.0}
# spec 12.2
BONE_BAR, EXACT_BAR, FLOOR_BAR = 0.05, 1e-4, 0.005
JR1_LEVELS, JR1_LEAST, JR1_JUDGED = 2, 500, 3          # JR1's Present per moment (the lead's ruling for the Editor's JR1, read in the player)
JR6_W, JR6_H, JR6_MOMENTS, JR6_EVERY, JR6_FOOT, JR6_JUDGED, JR6_MOVED = 960, 540, 24, 3, 150, 20, 5.0
JR6_WALK, JR6_LINE, JR6_SECONDS = 8.0, 2.0, 2.5
# Phase 6.10: YT1's Present floors (S0-6's least 94,695 and 55,571 px), its moments; YT3 at the thumbnail's size
YT_W, YT_H, YT_MOMENTS, YT_STEP_FLOOR, YT_FRONT_FLOOR, YT_PRESENT, YT_DT, YT_MOTION = 504, 704, 24, 30000, 20000, 3, 1.0 / 30, 1.0
GUID = "stp3.test.scriptprobe"
PROBE = os.path.join("Tests", "ScriptProbe")
DANCE = {"group": 11, "category": 5, "no": 0}           # dance_00, a plain row of Tools\\targets.json's animation list
FPS = 30
CLOCK = "2026-11-08T00:00:00Z"
STAGE_RE = re.compile(r"\[app\] bridge: (\d+) ms; (.*)$")
TIMED_RE = re.compile(r"\[timeline\] timed command (\S+) failed: ([a-z_]+): (.*)$")
NO_LISTENER = "in-process (no listener)"
# the code read for what the script parts subscribe to in a scene without scripts (JW7s)
SUB_FILES = ("Runtime/Scene/Scripts/*.cs", "Runtime/Scene/Runtime/Scripts/*.cs", "Runtime/Scene/Runtime/Scripted/*.cs", "Runtime/App/Commands/Script*.cs", "Runtime/Scene/Runtime/Ops/StudioOps.Script.cs")
SUB_RE = re.compile(r"\.(Events\.\w+|\w+Changed|\w+Built|Cleared|LateFrame|Frame|Rendered|Starting|Finished)\s*\+=")


def med(v):
    return statistics.median(v) if v else float("nan")


def bad_reply(r):
    return json.dumps(r, ensure_ascii=False)[:240]


# ---------------------------------------------------------------- the test plugin

def build_probe(a, exe, out, frames_only=False):
    """Tests\\ScriptProbe against the Managed folder of <exe>, into <out>\\plugins\\<guid>. Returns the plugins folder."""
    managed = os.path.join(os.path.dirname(exe), "HS2Next_Data", "Managed")
    plugins = os.path.join(out, "plugins")
    target = os.path.join(plugins, GUID)
    os.makedirs(target, exist_ok=True)
    obj = os.path.join(out, "probe_obj") + os.sep
    cmd = ["dotnet", "build", os.path.join(a.project, PROBE, "ScriptProbe.csproj"), "-c", "Release", "-nologo", "-v", "q", "-o", target, "-p:PlayerManaged=" + managed, "-p:BaseIntermediateOutputPath=" + obj]
    if frames_only:
        cmd.append("-p:FramesOnly=true")
    p = subprocess.run(cmd, capture_output=True, timeout=300, creationflags=CREATE_NO_WINDOW)
    if p.returncode != 0:
        raise SetupError("the test plugin did not build: " + p.stdout.decode("utf-8", "replace")[-600:])
    shutil.copyfile(os.path.join(a.project, PROBE, "manifest.json"), os.path.join(target, "manifest.json"))
    return plugins


# ---------------------------------------------------------------- sessions

class Run(Session):
    def __init__(self, a, R, letter, plugins, exe=None):
        super().__init__(a, R, letter, a.port, exe)
        self.plugins = plugins
        self.ud = os.path.join(self.out, "userdata")


def base_args(C):
    a = C.a
    settings = os.path.join(C.project, "Config", "settings.local.json")
    if not os.path.isfile(settings):
        raise SetupError("settings file not found: %s" % settings)
    args = [C.exe, "-noUi", "-screen-width", "1920", "-screen-height", "1080", "-screen-fullscreen", "0", "-window-mode", "windowed", "-pluginsDir", C.plugins]
    if a.gfx:
        args.append("-force-" + a.gfx)
    return args, settings


def popen(C, args):
    env = dict(os.environ)
    env["HS2NEXT_PRIVATE_OFF"] = "1"
    env.pop("HS2NEXT_DATA_ROOT", None)
    env.pop("HS2NEXT_NO_PLUGINS", None)
    si = subprocess.STARTUPINFO()
    si.dwFlags |= subprocess.STARTF_USESHOWWINDOW
    si.wShowWindow = 4   # SW_SHOWNOACTIVATE
    C.info["commandLine"] = args
    log("session %s: starting the player: %s" % (C.letter, subprocess.list2cmdline(args)))
    C.proc = subprocess.Popen(args, cwd=os.path.dirname(C.exe), env=env, stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, creationflags=CREATE_NO_WINDOW, startupinfo=si)
    C.pid = C.proc.pid


def start(C, deadline):
    """A player with its bridge on the session's port; waits until it is ready."""
    a = C.a
    os.makedirs(C.out, exist_ok=True)
    busy = port_listeners(C.port)
    if busy:
        raise SetupError("port %d is already in use (PID %s)" % (C.port, ", ".join(map(str, sorted(busy)))))
    args, settings = base_args(C)
    bo, logf = os.path.join(C.out, "bridge_out"), os.path.join(C.out, "player.log")
    ep = os.path.join(C.ud, "Bridge", "endpoint.json")
    if os.path.isfile(ep):
        os.remove(ep)
    args += ["-bridgePort", str(C.port), "-bridgeAllowQuit", "-settings", settings, "-cacheDir", os.path.join(C.project, "Cache"), "-userData", C.ud, "-bridgeOutputDir", bo, "-logFile", logf]
    t0 = time.time()
    popen(C, args)
    limit = min(deadline, t0 + a.ready_timeout_min * 60)
    while True:
        if C.proc.poll() is not None:
            raise SetupError("the player exited with code %s before it was ready (see %s)" % (C.proc.returncode, logf))
        if C.aborted:
            raise SetupError("before the player was ready: " + C.aborted)
        try:
            if C.br.call({"cmd": "ping"}, timeout=5, check=False).get("appReady"):
                break
        except (OSError, http.client.HTTPException, ValueError, RuntimeError):
            pass
        if time.time() > limit:
            raise SetupError("not ready after %.0f s" % (time.time() - t0))
        time.sleep(0.5)
    ready = time.time() - t0
    with open(os.path.join(C.project, "Tools", "targets.json"), encoding="utf-8") as f:
        C.targets = json.load(f)
    p = C.call({"cmd": "ping"})
    C.output_root = C.call({"cmd": "app.paths"}).get("outputDir")
    C.R.add("SETUP" + C.letter, "PASS", "session %s ready in %.1f s (pid %s, port %d); api %s" % (C.letter, ready, C.pid, C.port, p.get("api")), readySeconds=round(ready, 2))


def run_job(C, job, deadline, limit_min):
    """A player with -noBridge and the probe's job file: nothing listens, so the result comes back as the probe's result file when the player quits."""
    a = C.a
    os.makedirs(C.out, exist_ok=True)
    os.makedirs(C.ud, exist_ok=True)
    args, settings = base_args(C)
    logf = os.path.join(C.out, "player.log")
    res = os.path.join(C.out, "job.result.json")
    if os.path.isfile(res):
        os.remove(res)
    job = dict(job, result=res)
    jobf = os.path.join(C.out, "job.json")
    with open(jobf, "w", encoding="utf-8", newline="\n") as f:
        json.dump(job, f, indent=1)
    args += ["-noBridge", "-settings", settings, "-cacheDir", os.path.join(C.project, "Cache"), "-userData", C.ud, "-logFile", logf, "-scriptProbeJob", jobf]
    t0 = time.time()
    popen(C, args)
    limit = min(deadline, t0 + limit_min * 60)
    while C.proc.poll() is None:
        if C.aborted:
            raise SetupError("job %s: %s" % (job.get("kind"), C.aborted))
        if time.time() > limit:
            kill(C)
            raise SetupError("job %s: the player did not quit within %.0f min" % (job.get("kind"), limit_min))
        time.sleep(0.5)
    took = time.time() - t0
    if not os.path.isfile(res):
        raise SetupError("job %s: no result file (exit code %s; see %s)" % (job.get("kind"), C.proc.returncode, logf))
    with open(res, encoding="utf-8") as f:
        out = json.load(f)
    C.R.add("SETUP" + C.letter, "PASS" if out.get("ok") else "FAIL", "session %s: job %s ran with no listener in %.1f s, exit code %s%s"
            % (C.letter, job.get("kind"), took, C.proc.returncode, "" if out.get("ok") else "; error: %s" % out.get("error")), seconds=round(took, 1))
    return out


def log_lines(C):
    path = os.path.join(C.out, "player.log")
    if not os.path.isfile(path):
        return []
    with open(path, encoding="utf-8", errors="replace") as f:
        return f.read().splitlines()


def timed_failures(C):
    """{event id: (code, message)} of the timed commands that failed (the Timeline logs each event's first failure)."""
    out = {}
    for line in log_lines(C):
        m = TIMED_RE.search(line)
        if m and m.group(1) not in out:
            out[m.group(1)] = (m.group(2), m.group(3).strip())
    return out


# ---------------------------------------------------------------- scripts

def read_doc(path):
    with open(path, encoding="utf-8-sig") as f:
        return json.load(f)


def role_sexes(doc):
    """{role: female | male} of the character roles (a role of either sex takes BR-Chan)."""
    out = {}
    for r in doc.get("roles") or []:
        if r.get("kind") == "chara":
            out[r["role"]] = "male" if r.get("sex") == "male" else "female"
    return out


def has_note(o, word):
    if isinstance(o, dict):
        return any((k == "note" and isinstance(v, str) and word in v.lower()) or has_note(v, word) for k, v in o.items())
    if isinstance(o, list):
        return any(has_note(x, word) for x in o)
    return False


def sample_expect(doc):
    """The sample's deliberate failures as the sample declares them: the steps and timed commands whose note says it fails ({phase, index or t, cmd}).
    Nothing is inferred: a sample that declares none has no expected set, and the checks that need one FAIL."""
    out = []
    for phase in ("setup", "prepare"):
        for i, s in enumerate(doc.get(phase) or []):
            if isinstance(s, dict) and s.get("cmd") and has_note(s, "fail"):
                out.append({"phase": phase, "index": i, "cmd": s["cmd"]})
    for e in (doc.get("timeline") or {}).get("events") or []:
        if isinstance(e, dict) and "fail" in str(e.get("note") or "").lower():
            out.append({"phase": "event", "cmd": (e.get("do") or {}).get("cmd"), "t": e.get("t")})
    return out


def first_time(doc, cmds):
    """The earliest time of a step or timed command among cmds: 0 for a preparation or set-up step, the event's time otherwise; None when the script has none."""
    if any(isinstance(s, dict) and s.get("cmd") in cmds for p in ("setup", "prepare") for s in doc.get(p) or []):
        return 0.0
    ts = [float(e.get("t", 0)) for e in (doc.get("timeline") or {}).get("events") or [] if isinstance(e, dict) and (e.get("do") or {}).get("cmd") in cmds]
    return min(ts) if ts else None


def load_targets(C, roles):
    """One target per character role (female roles BR-Chan, male roles HERO107), physics off; {role: key}."""
    keys, x = {}, 0.0
    for role, sex in roles.items():
        r = C.call({"cmd": "chara.load", "path": target_rel(C, sex), "sex": sex}, timeout=300)
        k = r.get("key") or r["added"][0]["key"]
        C.call({"cmd": "obj.transform", "key": k, "pos": [x, 0, 0]})
        keys[role] = k
        x += 5.0
    return keys


class CastGuard(RuntimeError):
    """A play not sent: a character role is uncast, so load: true would load its hint card (the lead's rule: never)."""


def guard_cast(C, sid, doc):
    """Before every script.play {load: true}: every character role of the script cast (script.list's uncast holds no character role); else CastGuard, nothing sent."""
    chara = [r["role"] for r in doc.get("roles") or [] if isinstance(r, dict) and r.get("kind") == "chara"]
    lst = C.call({"cmd": "script.list"})
    row = next((x for x in lst.get("scripts") or [] if x.get("script") == sid), None)
    if row is None:
        raise CastGuard("script %s is not in the scene: the play was not sent" % sid)
    bad = [r for r in row.get("uncast") or [] if r in chara]
    if bad:
        raise CastGuard("character roles uncast %s (load: true would load their hint cards): the play was not sent" % bad)
    return row


def frames_of(folder):
    """The recorded frames: count, bytes, SHA-256 of the per-file hashes in name order; record.json read; never opened as images."""
    files = sorted(glob.glob(os.path.join(folder, "*.png")))
    h, size = hashlib.sha256(), 0
    for f in files:
        with open(f, "rb") as fh:
            b = fh.read()
        size += len(b)
        h.update(hashlib.sha256(b).digest())
    rec = None
    rj = os.path.join(folder, "record.json")
    if os.path.isfile(rj):
        try:
            with open(rj, encoding="utf-8") as fh:
                rec = json.load(fh)
        except (OSError, ValueError):
            rec = None
    return {"files": len(files), "bytes": size, "sha256": h.hexdigest(), "record": rec}


def delete_frames(folder):
    if not folder or not os.path.isdir(folder):
        return 0
    if os.path.islink(folder) or (os.stat(folder, follow_symlinks=False).st_file_attributes & 0x400):
        return 0
    n = 0
    for f in os.listdir(folder):
        p = os.path.join(folder, f)
        if os.path.isfile(p) and (f.lower().endswith(".png") or f == "record.json"):
            os.remove(p)
            n += 1
    if not os.listdir(folder):
        os.rmdir(folder)
    return n


def wait_record(C, limit):
    """Until the recording ended and wrote every frame (record.status can read recording false before the last files: wait for written = frames)."""
    end, st = time.time() + limit, {}
    while time.time() < end:
        st = C.call({"cmd": "record.status"})
        if not st.get("recording") and st.get("written") == st.get("frames") and (st.get("pending") or 0) == 0:
            return st
        time.sleep(0.25)
    return dict(st, timedOut=True)


def recorded_pass(C, sid, cast, duration, tag, doc, digests=True):
    """One script.play {load: true, record} pass with digests at k x duration / 8: the reply, the record's status, the frames (counted, hashed, deleted), the digests, the wall time,
    and the Timeline's eventErrors and script.status's lastPlay read before and after (JR5's cross-check)."""
    guard_cast(C, sid, doc)
    err0 = C.call({"cmd": "timeline.status"}).get("eventErrors") or 0
    if digests:
        C.call({"cmd": "scriptprobe.digest.arm", "cast": cast, "duration": duration, "parts": 8})
    t0 = time.time()
    rep = C.call({"cmd": "script.play", "script": sid, "load": True, "record": {"path": "record/" + tag, "width": 1280, "height": 720, "fps": FPS}}, timeout=650, check=False)
    t_reply = time.time() - t0
    st = wait_record(C, duration * 4 + 120) if rep.get("ok") else {}
    wall = time.time() - t0
    folder = ((rep.get("record") or {}).get("path")) or st.get("path")
    fr = frames_of(folder) if folder else {"files": 0, "bytes": 0, "sha256": None, "record": None}
    removed = 0 if C.a.keep_frames else delete_frames(folder)
    dg = C.call({"cmd": "scriptprobe.digest.read"}).get("digests", []) if digests else []
    C.frames(5)   # the replies of the last timed commands
    err1 = C.call({"cmd": "timeline.status"}).get("eventErrors") or 0
    last = (C.call({"cmd": "script.status"}).get("lastPlay") or {})
    return {"reply": rep, "status": st, "frames": fr, "removed": removed, "digests": dg, "wall": wall, "replySeconds": t_reply, "folder": folder,
            "eventErrors": err1 - err0, "lastPlayFailures": len(last.get("failures") or [])}


def failures_of(rep, events, timed):
    """Every failure of a play: the reply's steps, and the timed commands of the script's events that the log names (event id -> t, cmd)."""
    out = [{"phase": f.get("phase"), "index": f.get("index"), "cmd": f.get("cmd"), "code": f.get("code"), "message": f.get("message")} for f in rep.get("failures") or []]
    for eid, (code, msg) in timed.items():
        if eid in events:
            t, cmd = events[eid]
            out.append({"phase": "event", "id": eid, "t": t, "cmd": cmd, "code": code, "message": msg})
    return out


def fkey(f):
    t = f.get("t")
    return (f.get("phase"), f.get("cmd"), f.get("code") or "*", None if t is None else round(float(t), 2))


def same_set(got, want):
    """want's entries may leave code or t out (any); every got entry must match one want entry and every want entry one got entry."""
    def match(g, w):
        return g.get("phase") == w.get("phase") and g.get("cmd") == w.get("cmd") and (w.get("code") in (None, g.get("code"))) and (w.get("t") is None or (g.get("t") is not None and abs(float(g["t"]) - float(w["t"])) < 0.005))
    return all(any(match(g, w) for w in want) for g in got) and all(any(match(g, w) for g in got) for w in want)


# ---------------------------------------------------------------- digests (spec 12.1, 12.2)

def num_close(x, y, tol):
    if isinstance(x, bool) or isinstance(y, bool):
        return x == y
    if isinstance(x, (int, float)) and isinstance(y, (int, float)):
        return abs(float(x) - float(y)) <= tol
    if isinstance(x, dict) and isinstance(y, dict):
        return x.keys() == y.keys() and all(num_close(x[k], y[k], tol) for k in x)
    if isinstance(x, list) and isinstance(y, list):
        return len(x) == len(y) and all(num_close(p, q, tol) for p, q in zip(x, y))
    return x == y


def dist(p, q):
    return math.sqrt(sum((a - b) ** 2 for a, b in zip(p, q)))


def compare_digests(R, cid, label, A, B, doc):
    """A pass against another at the same k: the bars, each part's presence printed."""
    ka, kb = {d.get("k"): d for d in A if "error" not in d}, {d.get("k"): d for d in B if "error" not in d}
    errs = [d for d in A + B if "error" in d]
    ks = sorted(set(ka) & set(kb))
    stain_t = first_time(doc, {"wet.attach", "wet.use", "wet.stain", "wet.start"})
    current_t = first_time(doc, {"wet.attach", "wet.use"})
    body_t = first_time(doc, {"ragdoll.build"})
    worst = {"bone": 0.0, "holder": 0.0, "camera": 0.0, "floor": 0.0}
    where, bad, present = "", [], []
    bones_per = []
    per_k, over, floors = [], {}, []
    for k in ks:
        p, q = ka[k], kb[k]
        kw, kwhere = 0.0, "-"
        for role, bp in (p.get("bones") or {}).items():
            bq = (q.get("bones") or {}).get(role, {})
            bones_per.append(len(bp))
            for name, v in bp.items():
                if name in bq:
                    d = dist(v, bq[name])
                    if d > kw:
                        kw, kwhere = d, "%s/%s" % (role, name)
                    if d > BONE_BAR:
                        over.setdefault("%s/%s" % (role, name), []).append(k)
                    if d > worst["bone"]:
                        worst["bone"], where = d, "%s/%s at t %.4f" % (role, name, p.get("t", 0))
                else:
                    bad.append("k%d %s/%s missing in the second pass" % (k, role, name))
        per_k.append("k%d %.4f %s" % (k, kw, kwhere))
        for role, hp in (p.get("holders") or {}).items():
            hq = (q.get("holders") or {}).get(role)
            if hq:
                worst["holder"] = max(worst["holder"], dist(hp, hq))
        cp, cq = p.get("camera") or {}, q.get("camera") or {}
        if cp and cq:
            worst["camera"] = max(worst["camera"], dist(cp["pos"], cq["pos"]), dist(cp["rot"], cq["rot"]), abs(cp["fov"] - cq["fov"]))
        slots_p, slots_q = p.get("slots") or [], q.get("slots") or []
        if not num_close(slots_p, slots_q, EXACT_BAR):
            bad.append("k%d slots differ (%d / %d)" % (k, len(slots_p), len(slots_q)))
        if not slots_p:
            present.append("k%d no slot" % k)
        t = p.get("t", 0)
        for role, sp in (p.get("stains") or {}).items():
            sq = (q.get("stains") or {}).get(role)
            if sq is None:
                bad.append("k%d %s: the stain is missing in the second pass" % (k, role))
                continue
            for m in ("t", "radius", "running", "puddles", "targets"):
                if not num_close(sp.get(m), sq.get(m), EXACT_BAR):
                    bad.append("k%d %s: stain %s %s / %s" % (k, role, m, sp.get(m), sq.get(m)))
            # the lead's ruling (2026-11-08, after the wiring run's data, on P2's reading of the code): a stain's floor is runtime state, measured
            # again whenever a stain starts and before any drop step, and wet.status reports the stored value without measuring; so the floor is
            # compared only for a stain that runs at that sample or has puddles (wet.status reports no drop count); for any other stain it is printed
            uses_floor = bool(sp.get("running")) or bool(sq.get("running")) or (sp.get("puddles") or 0) > 0 or (sq.get("puddles") or 0) > 0
            if not uses_floor:
                floors.append("k%d %s %s / %s (not compared: the stain neither runs nor has puddles)" % (k, role, sp.get("floor"), sq.get("floor")))
            elif sp.get("floor") is not None and sq.get("floor") is not None:
                worst["floor"] = max(worst["floor"], abs(sp["floor"] - sq["floor"]))
                floors.append("k%d %s %.4f / %.4f" % (k, role, sp["floor"], sq["floor"]))
            elif sp.get("floor") != sq.get("floor"):
                bad.append("k%d %s: stain floor %s / %s" % (k, role, sp.get("floor"), sq.get("floor")))
            if current_t is not None and t >= current_t and sp.get("current") != sq.get("current"):
                bad.append("k%d %s: current stain %s / %s" % (k, role, sp.get("current"), sq.get("current")))
        if stain_t is not None and t >= stain_t + 0.05 and not (p.get("stains") or {}):
            present.append("k%d no stain after its first use at %.2f" % (k, stain_t))
        if p.get("ragdoll") != q.get("ragdoll"):
            bad.append("k%d ragdoll status differs" % k)
        bodies = (((p.get("ragdoll") or {}).get("world") or {}).get("bodies"))
        if body_t is not None and t >= body_t + 0.05 and not bodies:
            present.append("k%d no ragdoll body after its build at %.2f" % (k, body_t))
    least_bones = min(bones_per) if bones_per else 0
    if len(ks) != 7:
        present.append("digests at %d of 7 times (%d / %d taken)" % (len(ks), len(A), len(B)))
    if least_bones < 300:
        present.append("bones per character %d" % least_bones)
    ok_bars = worst["bone"] <= BONE_BAR and worst["holder"] <= EXACT_BAR and worst["camera"] <= EXACT_BAR and worst["floor"] <= FLOOR_BAR and not bad and not errs
    v = "PASS" if ok_bars and not present else "FAIL"
    R.add(cid, v, "%s: %d times; bones <= %.4g units (%s; bar %.2f), holders %.3g, camera %.3g (bar 1e-4), stain floor %.4g (bar 0.005); %s; Present: bones per character %d, slots at every time, "
          "a stain from %s, a body from %s%s%s"
          % (label, len(ks), worst["bone"], where or "-", BONE_BAR, worst["holder"], worst["camera"], worst["floor"], ("differ: " + "; ".join(bad[:6])) if bad else "slots, stains and ragdoll equal",
             least_bones, "-" if stain_t is None else "%.2f" % stain_t, "-" if body_t is None else "%.2f" % body_t, ("; NOT PRESENT: " + "; ".join(present)) if present else "",
             ("; digest errors: " + "; ".join(str(e.get("error")) for e in errs[:3])) if errs else ""),
          worst=worst, bad=bad, present=present, times=[ka[k].get("t") for k in ks])
    R.add(cid + "i", "INFO", "%s, per time (printed, no verdict): the worst bone %s; bones over %.2f: %s; stain floors (first / second) %s"
          % (label.split(":")[0], "; ".join(per_k) or "-", BONE_BAR, "; ".join("%s at k %s" % (n, ",".join(map(str, v))) for n, v in sorted(over.items())) or "none",
             "; ".join(floors) or "-"), perTime=per_k, over=over, floors=floors)


# ---------------------------------------------------------------- session N: JX6, JW4 (off)

def session_n(C, deadline):
    a = C.a
    doc = read_doc(a.sample) if os.path.isfile(a.sample) else None
    with open(os.path.join(C.project, "Tools", "targets.json"), encoding="utf-8") as f:
        C.targets = json.load(f)
    job = {"kind": "jx6", "female": target_rel(C, "female"), "male": target_rel(C, "male"), "sample": a.sample if doc else None, "roles": role_sexes(doc) if doc else {},
           "duration": ((doc or {}).get("timeline") or {}).get("duration"), "inlineRounds": 2000}
    out = run_job(C, job, deadline, 15)
    lines = log_lines(C)
    stage = [STAGE_RE.search(l) for l in lines]
    stage = [m for m in stage if m]
    host = out.get("host") or {}
    ep, tok = os.path.join(C.ud, "Bridge", "endpoint.json"), os.path.join(C.ud, "Bridge", "token")
    no_files = not os.path.exists(ep) and not os.path.exists(tok)
    said = bool(stage) and stage[-1].group(2).startswith(NO_LISTENER)
    jx2 = out.get("jx2") or {}
    cam = jx2.get("cameraTarget")
    cam_ok = isinstance(cam, list) and len(cam) == 3 and dist(cam, [0, 10, 0]) < 1e-3
    jx2_ok = jx2.get("fired") == 4 and jx2.get("errors") == 0 and cam_ok and jx2.get("build") == "ok"
    C.R.add("JX6", "PASS" if said and host.get("exists") and not host.get("listening") and no_files and jx2_ok else "FAIL",
            "a player with -noBridge: the log says \"%s\" (Present: %s), host %s, listening %s, %d commands, endpoint.json and token absent %s; JX2 in the player: %s fired, %s errors, camera.set's integer array "
            "reached the view %s (target %s), last error %s"
            % (NO_LISTENER, ("\"%s\"" % stage[-1].group(0)) if stage else "no bridge stage line", host.get("exists"), host.get("listening"), host.get("commands", 0), no_files,
               jx2.get("fired"), jx2.get("errors"), cam_ok, cam, jx2.get("lastEventError")), host=host, jx2=jx2)
    # JX4 in the player: no failure but the sample's deliberate one
    jx4 = out.get("jx4") or {}
    if not doc:
        C.R.add("JX4p", "FAIL", "the sample is absent: %s" % a.sample)
    else:
        play = jx4.get("play") or {}
        timed = timed_failures(C)
        doc_events = {e.get("id"): (e.get("t"), (e.get("do") or {}).get("cmd")) for e in (doc.get("timeline") or {}).get("events") or [] if isinstance(e, dict) and e.get("id")}
        fails = failures_of(play, doc_events, timed)
        unknown_ids = [eid for eid in timed if eid not in doc_events]
        want = sample_expect(doc)
        ok_set = bool(want) and same_set(fails, want)
        # the lead's cross-check: the timed failures read from the log against the Timeline's eventErrors over the one pass (a loop's wrap also fires the next pass's t = 0 events:
        # those failing count once more), the steps' failures against script.status's lastPlay; both read in the player before it quit
        n_timed = sum(1 for f in fails if f["phase"] == "event")
        wrap0 = sum(1 for f in fails if f["phase"] == "event" and float(f.get("t") or 0) == 0.0) if str(jx4.get("passEnd", "")).startswith("the loop's wrap") else 0
        n_steps = sum(1 for f in fails if f["phase"] != "event")
        last = ((jx4.get("status") or {}).get("lastPlay") or {})
        n_last = len(last.get("failures") or [])
        cross = jx4.get("eventErrors") == n_timed + wrap0 and n_steps == n_last
        ok = (bool((jx4.get("import") or {}).get("ok")) and bool((jx4.get("cast") or {}).get("ok")) and not jx4.get("aborted") and bool(play.get("ok")) and ok_set and cross and not unknown_ids)
        C.R.add("JX4p", "PASS" if ok else "FAIL", "script.play of the sample in the player with no listener (through the host's queue, %.0f ms): import %s, cast %s (uncast %s)%s, play %s; failures %d %s; the sample's declared "
                "deliberate failure %s; one pass (%s): timed commands fired %s, failed %s; cross-check: timed failures from the log %d (+%d t = 0 at the wrap) against eventErrors %s, step failures %d "
                "against script.status lastPlay %d: %s; failing events not in the sample %s"
                % (jx4.get("playMs") or 0, (jx4.get("import") or {}).get("ok"), (jx4.get("cast") or {}).get("ok"), jx4.get("uncast"), ("; ABORTED: " + jx4["aborted"]) if jx4.get("aborted") else "",
                   play.get("ok") if play.get("ok") else bad_reply(play), len(fails), "; ".join("%s %s %s %s" % (f["phase"], f.get("index", f.get("t")), f["cmd"], f["code"]) for f in fails),
                   want or "NONE DECLARED (no step or timed command of the sample has a note saying it fails)", jx4.get("passEnd"), jx4.get("eventsFired"), jx4.get("eventErrors"), n_timed, wrap0,
                   jx4.get("eventErrors"), n_steps, n_last, "equal" if cross else "DIFFER", unknown_ids or "none"), failures=fails, jx4=jx4)
    a.store["jw4off"] = {"stage": int(stage[-1].group(1)) if stage else None, "jw4": out.get("jw4") or {}}


# ---------------------------------------------------------------- sessions O1, F, O2: JW7's frame

def session_frames(C, deadline):
    job = {"kind": "frames", "warm": 180, "frames": 600, "rounds": 3}
    out = run_job(C, job, deadline, 10)
    meds = [r["medianMs"] for r in out.get("rounds") or []]
    C.a.store.setdefault("frames", {})[C.letter] = meds
    C.R.add("FRAMES" + C.letter, "PASS" if meds else "FAIL", "session %s (%s): the frame meter with no listener, the start scene, 3 rounds of 600 frames: medians %s ms"
            % (C.letter, os.path.basename(os.path.dirname(C.exe)), ", ".join("%.3f" % m for m in meds)), rounds=out.get("rounds"))


def jw7_frame(a, R):
    fr = a.store.get("frames") or {}
    f, o1, o2 = fr.get("F"), fr.get("O1"), fr.get("O2")
    if not f:
        R.add("JW7f", "FAIL", "the frame with the bridge off: session F gave no frames")
        return
    if not (o1 and o2):
        R.add("JW7f", "WARN", "the frame with the bridge off against today's bridge-off player: no baseline (--baseline-exe Builds\\AppPrev69\\HS2Next.exe absent): this build %.3f ms median, not compared" % med(f))
        return
    base = (med(o1) + med(o2)) / 2.0
    d = med(f) - base
    R.add("JW7f", "PASS" if d <= BAR["JW7frame"] else "WARN", "the frame with the bridge off: this build %.3f ms median against the last build %.3f (O1 %.3f, O2 %.3f; their difference %.3f, the noise): %+.3f ms, bar +%.2f"
          % (med(f), base, med(o1), med(o2), abs(med(o1) - med(o2)), d, BAR["JW7frame"]))


# ---------------------------------------------------------------- session R: JR4, JR6

def jr4(C):
    C.call({"cmd": "scene.new"})
    k = load_targets(C, {"a": "female"})["a"]
    C.call(dict(DANCE, cmd="chara.anime", key=k, speed=1.0, loop=True))
    C.call({"cmd": "camera.set", "target": [0, 8, 0], "rot": [5, 180, 0], "dist": 42, "fov": 23})
    C.frames(23)
    cap0 = C.call({"cmd": "scriptprobe.frames"})
    C.call({"cmd": "scriptprobe.frames", "vsync": 0, "fps": 60})
    C.frames(11)
    C.call({"cmd": "scriptprobe.jr1.arm"})
    res = {"capture": [], "screenshot": [], "record": []}
    try:
        for path in ("capture", "screenshot"):
            for i in range(6):
                if path == "capture":
                    r = C.call({"cmd": "capture", "name": "jr4_%d" % i, "width": 640, "height": 360, "aa": "none", "frame": False})
                else:
                    r = C.call({"cmd": "screenshot", "name": "jr4_s%d" % i, "mode": "custom", "width": 640, "height": 360, "supersample": 1, "frame": False})
                res[path].append(C.call({"cmd": "scriptprobe.jr1.judge", "path": r.get("path")}))
                C.frames(7)
        rec = C.call({"cmd": "record.start", "path": "record/jr4", "width": 640, "height": 360, "fps": 30, "frames": 8, "overwrite": True})
        wait_record(C, 120)
        res["record"] = C.call({"cmd": "scriptprobe.jr1.folder", "folder": rec.get("path")}, timeout=600).get("moments", [])
        delete_frames(rec.get("path"))
    finally:
        C.call({"cmd": "scriptprobe.jr1.disarm"})
        C.call({"cmd": "scriptprobe.frames", "vsync": cap0.get("vsync", 1), "fps": cap0.get("fps", -1)})
    dts = [m["dtMs"] for p in ("capture", "screenshot") for m in res[p] if m.get("armed")]
    rate = med(dts)
    for path, want in (("capture", 6), ("screenshot", 6), ("record", 8)):
        l = res[path]
        bad = [m for m in l if m.get("capture") != 0]
        judged = [m for m in l if (m.get("control") or -1) >= JR1_LEAST]
        errs = sorted(set(e for m in l for e in m.get("errors") or []))
        held = path == "record" or 14.0 <= rate <= 20.0
        ok = len(l) == want and not bad and len(judged) >= JR1_JUDGED and held and not errs
        C.R.add("JR4" + path[0], "PASS" if ok else "FAIL", "JR1 in the player, %s: %d of %d moments, each equal to its forced render at 0 px over %d levels (%d not); Present: the hook-off control over %d px at %d judged "
                "moments (at least %d)%s; per moment capture / control px, frames since the view drew: %s%s"
                % (path, len(l), want, JR1_LEVELS, len(bad), JR1_LEAST, len(judged), JR1_JUDGED, "" if path == "record" else "; frame delta median %.1f ms (held within 14-20: %s)" % (rate, held),
                   ", ".join("%s/%s/%s" % (m.get("capture"), m.get("control"), m.get("sinceDrawn")) for m in l), ("; handler errors: " + "; ".join(errs[:3])) if errs else ""), moments=l)


def jr6_tally(ms, wide):
    t = {"judged": 0, "not": 0, "missing": 0, "marks": 0, "noCtrl": 0, "leastCtrl": None, "under": 0, "noBlock": 0, "leastRatio": None, "uncovered": 0, "blocks": 0, "mixed": 0, "noGroup": 0,
         "leastFp": None, "moved": 0.0}
    first = None
    for m in ms:
        if m.get("missing"):
            t["missing"] += 1
            continue
        c = m.get("centre")
        if c:
            first = first or c
            t["moved"] = max(t["moved"], dist(c, first))
        if m["footprint"] < JR6_FOOT:
            t["not"] += 1
            continue
        t["judged"] += 1
        t["leastFp"] = m["footprint"] if t["leastFp"] is None else min(t["leastFp"], m["footprint"])
        if not wide:
            t["marks"] += m.get("markers", 0)
            if not m.get("leftOut"):
                t["noCtrl"] += 1
            t["leastCtrl"] = m.get("leftOut", 0) if t["leastCtrl"] is None else min(t["leastCtrl"], m.get("leftOut", 0))
            if not m.get("captureFrame") or not m.get("drawn"):
                t["noBlock"] += 1
            t["under"] += m.get("under", 0)
            if m.get("drawn"):
                r = m.get("leastBlock", 0) / max(m.get("floor", 0), 1e-6)
                t["leastRatio"] = r if t["leastRatio"] is None else min(t["leastRatio"], r)
        elif m.get("noGroup"):
            t["noGroup"] += 1
        else:
            t["uncovered"] += m.get("uncovered", 0)
            t["blocks"] += m.get("blocks", 0)
            t["mixed"] += m.get("mixed", 0)
    return t


def jr6_walk(C, key, wide, path):
    C.call({"cmd": "obj.transform", "key": key, "pos": [-JR6_WALK / 2, 0, JR6_LINE], "rot": [0, 90, 0], "undo": False})
    C.frames(3)
    C.call(C.call({"cmd": "scriptprobe.jr6.choose", "width": JR6_W, "height": JR6_H}, timeout=120)["aim"])
    C.frames(2)
    ms = []
    try:
        if path == "record":
            n = JR6_MOMENTS * JR6_EVERY
            C.call({"cmd": "scriptprobe.jr6.arm", "wide": wide, "record": True, "every": JR6_EVERY})
            C.call({"cmd": "obj.moveTo", "key": key, "target": [JR6_WALK / 2, 0, JR6_LINE], "stop": 0, "time": n / 30.0})
            rec = C.call({"cmd": "record.start", "path": "record/jr6", "width": JR6_W, "height": JR6_H, "fps": 30, "frames": n, "overwrite": True})
            wait_record(C, 300)
            C.call({"cmd": "scriptprobe.jr6.disarm"})
            ms = C.call({"cmd": "scriptprobe.jr6.folder", "folder": rec.get("path")}, timeout=900).get("moments", [])
            delete_frames(rec.get("path"))
        else:
            C.call({"cmd": "scriptprobe.jr6.arm", "wide": wide})
            C.call({"cmd": "obj.moveTo", "key": key, "target": [JR6_WALK / 2, 0, JR6_LINE], "stop": 0, "time": JR6_SECONDS})
            for i in range(JR6_MOMENTS):
                C.call(C.call({"cmd": "scriptprobe.jr6.choose", "width": JR6_W, "height": JR6_H}, timeout=120)["aim"])   # in the frame before the capture, by the footprint alone
                if path == "capture":
                    r = C.call({"cmd": "capture", "name": "jr6", "width": JR6_W, "height": JR6_H, "aa": "none", "frame": False})
                else:
                    r = C.call({"cmd": "screenshot", "name": "jr6_s", "mode": "custom", "width": JR6_W, "height": JR6_H, "supersample": 1, "frame": False})
                ms.append(C.call({"cmd": "scriptprobe.jr6.judge", "path": r.get("path")}, timeout=120))
                C.frames(2)
    finally:
        C.call({"cmd": "scriptprobe.jr6.disarm"})
    return ms


def jr6(C):
    for label, sex in (("BR-Chan", "female"), ("HERO107", "male")):
        C.call({"cmd": "scene.new"})
        k = load_targets(C, {"a": sex})["a"]
        C.call(dict(DANCE, cmd="chara.anime", key=k, speed=1.0, loop=True))
        C.call({"cmd": "obj.transform", "key": k, "pos": [-JR6_WALK / 2, 0, JR6_LINE], "rot": [0, 90, 0], "undo": False})
        C.frames(10)
        fx = C.call({"cmd": "scriptprobe.jr6.fixture", "key": k}, timeout=300)
        try:
            # the lead's ruling (2026-11-08): in a player only (a) is judged; the wide pass's coverage view (MosaicWide.DebugView) is compiled in the
            # Editor only, so (b) stands on the Editor's JR6 (ScriptTest.play) and each path prints one INFO line here
            for path in ("capture", "screenshot", "record"):
                C.R.add("JR6" + label[0] + "b" + path[0], "INFO", "JR6 %s (b) %s: (b) not judged in a player: the wide pass's coverage view is Editor-only" % (label, path))
            for wide in (False,):
                C.call({"cmd": "scriptprobe.jr6.use", "wide": wide})
                C.frames(3)
                for path in ("capture", "screenshot", "record"):
                    ms = jr6_walk(C, k, wide, path)
                    t = jr6_tally(ms, wide)
                    what = "JR6 %s %s %s" % (label, "(b)" if wide else "(a)", path)
                    errs = sorted(set(e for m in ms for e in m.get("errors") or []))
                    if not wide:
                        ok = t["judged"] >= JR6_JUDGED and t["marks"] == 0 and t["noCtrl"] == 0 and t["missing"] == 0 and t["under"] == 0 and t["noBlock"] == 0 and t["moved"] >= JR6_MOVED and not errs
                        text = ("%s: 0 marker pixels in each moment's footprint (%d), every drawn member's block >= F (%d under, %d moments without the capture's frame, least block / F %.2f); Present: %d judged "
                                "(at least %d), %d not judged (footprint under %d px), the marker shows with the mosaic left out at every judged moment (%d without, least %s px), moved %.2f units (at least %.0f), "
                                "%d missing, least footprint %s px" % (what, t["marks"], t["under"], t["noBlock"], t["leastRatio"] or 0, t["judged"], JR6_JUDGED, t["not"], JR6_FOOT, t["noCtrl"], t["leastCtrl"],
                                                                     t["moved"], JR6_MOVED, t["missing"], t["leastFp"]))
                    else:
                        ok = t["judged"] >= JR6_JUDGED and t["uncovered"] == 0 and t["mixed"] == 0 and t["blocks"] > 0 and t["noGroup"] == 0 and t["missing"] == 0 and t["moved"] >= JR6_MOVED and not errs
                        text = ("%s: the coverage holds each moment's footprint dilated by 1.5 blocks (uncovered %d px), the changed pixels form flat blocks (%d blocks, %d mixed, %d moments without the wide pass); "
                                "Present: %d judged (at least %d), %d not judged, moved %.2f units, %d missing" % (what, t["uncovered"], t["blocks"], t["mixed"], t["noGroup"], t["judged"], JR6_JUDGED, t["not"],
                                                                                                                  t["moved"], t["missing"]))
                    C.R.add("JR6" + label[0] + ("b" if wide else "a") + path[0], "PASS" if ok else "FAIL", text + (("; errors: " + "; ".join(errs[:3])) if errs else ""), tally=t, moments=ms, fixture=fx)
        finally:
            C.call({"cmd": "scriptprobe.jr6.restore"})
    C.call({"cmd": "scene.new"})


def yt1(C):
    """YT1 in the player: each moment is one probe command at its command's point, after the player's own draw of the frame. The stepped path runs on a
    new scene's own view (Step 0's thumbstep), the others on JR1's view of the whole figure (the lead's ruling after the Editor's 20261109-0013)."""
    def dancer(view):
        C.call({"cmd": "scene.new"})
        key = load_targets(C, {"a": "female"})["a"]
        C.call(dict(DANCE, cmd="chara.anime", key=key, speed=1.0, loop=True))
        if view:
            C.call({"cmd": "camera.set", "target": [0, 8, 0], "rot": [5, 180, 0], "dist": 42, "fov": 23})
        C.frames(23)
        return key
    k = None
    for path in ("stepped", "command", "front", "view"):
        if path in ("stepped", "command"):
            k = dancer(path == "command")
        ms = []
        if path == "stepped":
            C.call({"cmd": "scene.pause", "paused": True})   # a step steps a paused clock only (the lead's ruling after the Editor's 20261108-2349)
            C.frames(3)
        for i in range(YT_MOMENTS):
            ms.append(C.call({"cmd": "scriptprobe.yt1", "path": path, "key": k, "dt": YT_DT}, timeout=120))
            C.frames(5)
        if path == "stepped":
            C.call({"cmd": "scene.pause", "paused": False})
        bad = [m for m in ms if m.get("thumbnail") != 0 or (path == "stepped" and not m.get("stepped"))]
        if path in ("stepped", "front"):
            floor = YT_STEP_FLOOR if path == "stepped" else YT_FRONT_FLOOR
            over = [m for m in ms if (m.get("control") or -1) >= floor]
            present = len(over) >= YT_PRESENT
            ptext = "the hook-off control over %d px at %d moments (at least %d)" % (floor, len(over), YT_PRESENT)
        else:
            h0 = ms[0].get("hand") if ms else None
            moved = max(dist(m.get("hand") or h0, h0) for m in ms) if h0 else 0.0
            present = moved >= YT_MOTION
            ptext = "the right hand moved %.2f units between the moments (at least %.0f)" % (moved, YT_MOTION)
        ok = len(ms) == YT_MOMENTS and not bad and present
        C.R.add("YT1" + path[0], "PASS" if ok else "FAIL", "YT1 in the player, %s: %d of %d moments, each equal to its forced render at 0 px over %d levels (%d not); Present: %s; "
                "per moment thumbnail / hook-off control px%s: %s" % (path, len(ms), YT_MOMENTS, JR1_LEVELS, len(bad), ptext, " / the step taken, the hand's travel over it" if path == "stepped" else "",
                                                                   ", ".join("%s/%s" % (m.get("thumbnail"), m.get("control")) + ("/%s/%.4f" % (m.get("stepped"), m.get("travel") or 0) if path == "stepped" else "") for m in ms)),
                moments=ms)
    C.call({"cmd": "scene.new"})


def yt3(C):
    """YT3 (a) in the player: JR6's fixture through the scene thumbnail. With fewer than 20 moments over 150 px of footprint nothing is judged."""
    for label, sex in (("BR-Chan", "female"), ("HERO107", "male")):
        C.call({"cmd": "scene.new"})
        k = load_targets(C, {"a": sex})["a"]
        C.call(dict(DANCE, cmd="chara.anime", key=k, speed=1.0, loop=True))
        C.call({"cmd": "obj.transform", "key": k, "pos": [-JR6_WALK / 2, 0, JR6_LINE], "rot": [0, 90, 0], "undo": False})
        C.frames(10)
        fx = C.call({"cmd": "scriptprobe.jr6.fixture", "key": k}, timeout=300)
        what = "YT3 %s (a) scene thumbnail" % label
        try:
            C.call({"cmd": "scriptprobe.jr6.use", "wide": False})
            C.frames(3)
            C.call({"cmd": "obj.transform", "key": k, "pos": [-JR6_WALK / 2, 0, JR6_LINE], "rot": [0, 90, 0], "undo": False})
            C.frames(3)
            ms = []
            C.call({"cmd": "obj.moveTo", "key": k, "target": [JR6_WALK / 2, 0, JR6_LINE], "stop": 0, "time": JR6_SECONDS})
            for i in range(YT_MOMENTS):
                C.call(C.call({"cmd": "scriptprobe.jr6.choose", "width": YT_W, "height": YT_H}, timeout=120)["aim"])   # in the frame before the thumbnail, by the footprint alone
                ms.append(C.call({"cmd": "scriptprobe.yt3"}, timeout=120))
                C.frames(2)
            over = [m for m in ms if not m.get("missing") and (m.get("footprint") or 0) >= JR6_FOOT]
            if len(over) < JR6_JUDGED:
                C.R.add("YT3" + label[0], "FAIL", "%s: Present: %d moments with a footprint of %d px or more (at least %d): nothing judged; footprints %s"
                        % (what, len(over), JR6_FOOT, JR6_JUDGED, ", ".join(str(m.get("footprint")) for m in ms)), footprints=[m.get("footprint") for m in ms])
                continue
            t = jr6_tally(ms, False)
            errs = sorted(set(e for m in ms for e in m.get("errors") or []))
            ok = t["judged"] >= JR6_JUDGED and t["marks"] == 0 and t["noCtrl"] == 0 and t["missing"] == 0 and t["under"] == 0 and t["noBlock"] == 0 and t["moved"] >= JR6_MOVED and not errs
            text = ("%s: 0 marker pixels in each moment's footprint (%d), every drawn member's block >= F (%d under, %d moments without the thumbnail camera's step, least block / F %.2f); Present: %d judged "
                    "(at least %d), %d not judged (footprint under %d px), the marker shows with the mosaic left out at every judged moment (%d without, least %s px), moved %.2f units (at least %.0f), "
                    "%d missing, least footprint %s px" % (what, t["marks"], t["under"], t["noBlock"], t["leastRatio"] or 0, t["judged"], JR6_JUDGED, t["not"], JR6_FOOT, t["noCtrl"], t["leastCtrl"],
                                                         t["moved"], JR6_MOVED, t["missing"], t["leastFp"]))
            C.R.add("YT3" + label[0], "PASS" if ok else "FAIL", text + (("; errors: " + "; ".join(errs[:3])) if errs else ""), tally=t, moments=ms, fixture=fx)
        finally:
            C.call({"cmd": "scriptprobe.jr6.restore"})
    C.call({"cmd": "scene.new"})


def run_r(C):
    if C.a.want("JR4"):
        jr4(C)
    if C.a.want("JR6"):
        jr6(C)
    if C.a.want("YT1"):
        yt1(C)
    if C.a.want("YT3"):
        yt3(C)


# ---------------------------------------------------------------- session D: the scripts, JD1, JD2, JW

def script_ids(C, sid):
    """The scene script's stored events: {event id: (t, cmd)} (ids as the scene holds them after the import)."""
    g = C.call({"cmd": "script.get", "script": sid})
    doc = g.get("script") or {}
    return doc, {e.get("id"): (e.get("t"), (e.get("do") or {}).get("cmd")) for e in (doc.get("timeline") or {}).get("events") or [] if isinstance(e, dict) and e.get("id")}


def play_script(C, path, expect, do_jd):
    """One script: import, cast, the recorded pass A (JR5, JW6, JW8), JW1, then (do_jd) save, the replay A2 (JD2), load, pass B (JD1); JW2."""
    a, R = C.a, C.R
    name = os.path.basename(path)
    tag = re.sub(r"[^A-Za-z0-9_]", "_", os.path.splitext(name)[0])[:24]
    doc0 = read_doc(path)
    roles = role_sexes(doc0)
    duration = float((doc0.get("timeline") or {}).get("duration") or 0)
    want_frames = int(math.floor(duration * FPS + 0.5))
    t0 = time.time()
    C.call({"cmd": "scene.new"})
    keys = load_targets(C, roles)
    imp = C.call({"cmd": "script.import", "path": path, "to": "scene"}, timeout=120)
    sid = imp.get("script")
    cast = C.call({"cmd": "script.cast", "script": sid, "roles": keys})
    t_setup = time.time() - t0
    lst = C.call({"cmd": "script.list"})
    row = next((s for s in lst.get("scripts") or [] if s.get("script") == sid), {})
    # every character role cast to a target; a prop role is added by the play's load from its row (the lead's ruling on Q7)
    unc_chara = [r for r in row.get("uncast") or [] if r in roles]
    R.add("JV3" + tag, "PASS" if row and not unc_chara and cast.get("ok") else "FAIL", "%s imported into the player: warnings %s; character roles cast to the targets %s, uncast character roles %s "
          "(uncast in all %s: prop roles are added by the play's load)" % (name, imp.get("warnings") or "none", json.dumps(cast.get("cast")), unc_chara or "none", row.get("uncast") or "none"),
          warnings=imp.get("warnings"))
    comp = C.call({"cmd": "scriptprobe.compile", "script": sid, "rounds": 50}, timeout=600)
    R.add("JW1" + tag, "PASS" if comp["medianMs"] <= BAR["JW1"] else "WARN", "%s: compile and substitution with its cast %.3f ms median of 50 (max %.3f; %d tracks, %d keys, %d events; keys replaced %d, request strings %d). Bar %.0f ms"
          % (name, comp["medianMs"], comp["maxMs"], comp["tracks"], comp["keys"], comp["events"], comp["keysReplaced"], comp["requestsReplaced"], BAR["JW1"]), raw=comp)
    doc, events = script_ids(C, sid)
    cast_map = dict(keys)
    # pass A: the first play, recorded (JR5), with digests
    A = recorded_pass(C, sid, cast_map, duration, tag + "_a", doc0)
    fr, st = A["frames"], A["status"]
    frames_ok = bool(A["reply"].get("ok")) and st.get("written") == want_frames and st.get("frames") == want_frames and fr["files"] == want_frames and A["removed"] >= fr["files"]
    a.store.setdefault("jr5", []).append({"name": name, "tag": tag, "events": events, "reply": A["reply"], "status": st, "frames": fr, "removed": A["removed"], "want": want_frames,
                                          "duration": duration, "framesOk": frames_ok, "expect": expect, "eventErrors": A["eventErrors"], "lastPlayFailures": A["lastPlayFailures"]})
    R.add("JW6" + tag, "PASS" if A["wall"] <= BAR["JW6"] and frames_ok else "WARN", "%s: one recorded pass at 1280 x 720, 30 fps: wall %.1f s from script.play to written = frames (%s), the reply after %.1f s. Bar %.0f s"
          % (name, A["wall"], st.get("written"), A["replySeconds"], BAR["JW6"]))
    jw8 = t_setup + A["replySeconds"]
    R.add("JW8" + tag, "PASS" if jw8 <= BAR["JW8"] else "WARN", "%s on an empty scene: the targets loaded, imported and cast %.1f s, then script.play {load: true} (map, props, preparation, set-up, the recording started) "
          "answered in %.1f s: %.1f s. Bar %.0f s" % (name, t_setup, A["replySeconds"], jw8, BAR["JW8"]))
    with open(os.path.join(C.out, "digests_%s_a.json" % tag), "w", encoding="utf-8", newline="\n") as f:
        json.dump(A["digests"], f)
    if do_jd:
        C.call({"cmd": "scene.save", "name": "script_jd_" + tag, "overwrite": True, "thumb": "placeholder", "clock": CLOCK}, timeout=600)
        A2 = recorded_pass(C, sid, cast_map, duration, tag + "_a2", doc0)
        with open(os.path.join(C.out, "digests_%s_a2.json" % tag), "w", encoding="utf-8", newline="\n") as f:
            json.dump(A2["digests"], f)
        compare_digests(R, "JD2" + tag, "%s: a replay without a reload against the first play" % name, A["digests"], A2["digests"], doc)
        C.call({"cmd": "scene.load", "name": "script_jd_" + tag}, timeout=600)
        C.frames(5)
        lst = C.call({"cmd": "script.list"})
        row = next((s for s in lst.get("scripts") or [] if s.get("script") == sid), {})
        B = recorded_pass(C, sid, cast_map, duration, tag + "_b", doc0)
        with open(os.path.join(C.out, "digests_%s_b.json" % tag), "w", encoding="utf-8", newline="\n") as f:
            json.dump(B["digests"], f)
        compare_digests(R, "JD1" + tag, "%s: played after save and load against the first play (the loaded scene holds the script, its cast %s, prepared %s)"
                        % (name, json.dumps(row.get("cast")), row.get("prepared")), A["digests"], B["digests"], doc)
    # JW2: the Timeline's tick with this script loaded, playing at the screen's rate
    guard_cast(C, sid, doc0)
    C.call({"cmd": "script.play", "script": sid, "load": True}, timeout=650)
    C.call({"cmd": "timeline.status", "perf": True, "reset": True})
    C.frames(max(120, min(600, int(duration * 60))))
    p = (C.call({"cmd": "timeline.status", "perf": True}).get("perf") or {})
    C.call({"cmd": "script.stop", "reset": True})
    ok = (p.get("medianUs") or 1e9) <= BAR["JW2med"] and (p.get("p95Us") or 1e9) <= BAR["JW2p95"]
    R.add("JW2" + tag, "PASS" if ok else "WARN", "%s loaded from a script, playing: the Timeline's tick median %s µs, p95 %s, max %s over %s frames (%d tracks, %d events). Bars %.0f µs, %.0f µs"
          % (name, p.get("medianUs"), p.get("p95Us"), p.get("maxUs"), p.get("frames"), comp["tracks"], comp["events"], BAR["JW2med"], BAR["JW2p95"]), raw=p)


def jr5_judge(a, R, log_path):
    """JR5 for every script session D played: the frames of pass A and every failure of its steps and of its events (the Timeline logs each event's first failure; read once the player quit)."""
    timed = {}
    if os.path.isfile(log_path):
        with open(log_path, encoding="utf-8", errors="replace") as f:
            for line in f:
                m = TIMED_RE.search(line)
                if m and m.group(1) not in timed:
                    timed[m.group(1)] = (m.group(2), m.group(3).strip())
    for x in a.store.get("jr5") or []:
        fails = failures_of(x["reply"], x["events"], timed)
        unknown = [f for f in fails if f["code"] == "unknown_command"]
        badargs = [f for f in fails if f["code"] == "bad_args"]
        others = [f for f in fails if f["code"] not in ("unknown_command", "bad_args")]
        expect = x["expect"]
        if expect is None:
            exp_text, set_ok = "no expectation given: the failures printed, not judged", True
        elif isinstance(expect, tuple):   # the sample: no failure but its declared deliberate one (JX4's rule; any code)
            want = expect[1]
            set_ok = bool(want) and same_set(fails, want)
            exp_text = "the sample's declared deliberate failure %s: %s" % (want or "NONE DECLARED (no step or timed command of the sample has a note saying it fails)", set_ok)
            unknown, badargs = [], []
        else:
            set_ok = same_set(others, [w for w in expect if w.get("code") not in ("unknown_command", "bad_args")])
            exp_text = "the others equal as a set to the expectation (%d entries): %s" % (len(expect), set_ok)
            if any(w.get("code") in ("unknown_command", "bad_args") for w in expect):
                exp_text += "; the expectation names unknown_command or bad_args, which JR5 never accepts"
        fr, st = x["frames"], x["status"]
        # the lead's cross-check: the timed failures read from the log against the Timeline's eventErrors of pass A, the steps' against script.status's lastPlay (both read before the player quit)
        n_timed, n_steps = sum(1 for f in fails if f["phase"] == "event"), sum(1 for f in fails if f["phase"] != "event")
        # the pass's last frame is the wrap: a failing timed command at t = 0 may count once more in eventErrors there
        wrap0 = sum(1 for f in fails if f["phase"] == "event" and float(f.get("t") or 0) == 0.0)
        cross = x["eventErrors"] in (n_timed, n_timed + wrap0) and n_steps == x["lastPlayFailures"]
        exp_text += "; cross-check: timed failures from the log %d (%d at t = 0), eventErrors of pass A %s; step failures in the reply %d, script.status lastPlay %s: %s" % (
            n_timed, wrap0, x["eventErrors"], n_steps, x["lastPlayFailures"], "equal" if cross else "DIFFER")
        v = "PASS" if x["framesOk"] and not unknown and not badargs and set_ok and cross else "FAIL"
        R.add("JR5" + x["tag"], v, "%s played with load and record (1280 x 720, 30 fps): frames written %s of round(%.3f x 30) = %d, files %d (%d bytes, SHA-256 %s), deleted %d; unknown_command %d, bad_args %d; "
              "other failures %d: %s; %s" % (x["name"], st.get("written"), x["duration"], x["want"], fr["files"], fr["bytes"], (fr["sha256"] or "-")[:16], x["removed"], len(unknown), len(badargs), len(others),
                                             "; ".join("%s %s %s %s: %s" % (f["phase"], f.get("index", f.get("t")), f["cmd"], f["code"], (f.get("message") or "")[:80]) for f in fails) or "none", exp_text),
              failures=fails, status=st, reply=x["reply"])


def jw3(C):
    """Two moves, a follow and a dof blend running: the parts' tick called again with dt 0 (and the follow's late apply), rounds timed."""
    C.call({"cmd": "scene.new"})
    keys = load_targets(C, {"a": "female", "b": "male"})
    C.call({"cmd": "obj.moveTo", "key": keys["a"], "target": [0, 0, 40], "stop": 0, "time": 120})
    C.call({"cmd": "obj.moveTo", "key": keys["b"], "target": [5, 0, 40], "stop": 0, "time": 120})
    C.call({"cmd": "camera.follow", "target": {"key": keys["a"]}, "seconds": 120})
    C.call({"cmd": "dof", "on": True, "focus": keys["a"]})
    C.call({"cmd": "dof", "focus": keys["b"], "blend": 60})
    C.frames(10)
    r = C.call({"cmd": "scriptprobe.parts", "rounds": 1000}, timeout=600)
    present = r.get("moves") == 2 and r.get("follow") and r.get("blending")
    C.R.add("JW3", ("PASS" if r["medianUs"] <= BAR["JW3"] else "WARN") if present else "FAIL", "the script parts' tick with 2 moves, a follow and a dof blend running (called again by the probe with dt 0, and the follow's late apply, 1000 rounds): "
            "median %.2f µs, p95 %.2f. Bar %.0f µs. Present: moves %s, follow %s, blending %s" % (r["medianUs"], r["p95Us"], BAR["JW3"], r.get("moves"), r.get("follow"), r.get("blending")), raw=r)
    C.call({"cmd": "script.stop", "reset": False})


def jw4_on(C):
    C.call({"cmd": "scene.new"})
    keys = load_targets(C, {"a": "female", "b": "male"})
    C.call({"cmd": "obj.transform", "key": keys["b"], "pos": [5, 0, 0], "rot": [0, -90, 0]})
    C.frames(30)
    noop = C.call({"cmd": "scriptprobe.inline", "request": {"cmd": "ping"}, "rounds": 2000}, timeout=600)
    cam = C.call({"cmd": "scriptprobe.inline", "request": {"cmd": "camera.get"}, "rounds": 2000}, timeout=600)
    C.a.store["jw4on"] = {"noop": noop, "cameraGet": cam}


def jw4_judge(a, R):
    off, on = a.store.get("jw4off"), a.store.get("jw4on")
    if not off:
        R.add("JW4", "FAIL", "the executor with the bridge off: session N gave no numbers")
        return
    j = off.get("jw4") or {}
    noop, cam = j.get("noop") or {}, j.get("cameraGet") or {}
    start = off.get("stage")
    on_cam = ((on or {}).get("cameraGet") or {}).get("medianUs")
    ratio = (cam.get("medianUs") / on_cam) if on_cam else None
    ok = (start is not None and start <= BAR["JW4start"] and (noop.get("medianUs") or 1e9) <= BAR["JW4noop"] and (cam.get("medianUs") or 1e9) <= BAR["JW4cam"]
          and ratio is not None and ratio <= BAR["JW4ratio"] and noop.get("ok") and cam.get("ok"))
    R.add("JW4", "PASS" if ok else "WARN", "the executor with the bridge off: the host's start with registration %s ms (the log's bridge stage; bar %.0f), an inline no-op (ping) %.1f µs median, p95 %.1f (bar %.0f), "
          "an inline camera.get %.1f µs, p95 %.1f (bar %.0f) against %s µs with the bridge on in the same scene: x %s (bar %.1f); 2000 rounds each"
          % (start, BAR["JW4start"], noop.get("medianUs") or float("nan"), noop.get("p95Us") or float("nan"), BAR["JW4noop"], cam.get("medianUs") or float("nan"), cam.get("p95Us") or float("nan"),
             BAR["JW4cam"], "%.1f" % on_cam if on_cam else "-", "%.2f" % ratio if ratio else "-", BAR["JW4ratio"]), off=off, on=on)


def write_script(path, doc):
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(doc, f, indent=1)


def tiny_script(name, steps=0):
    """A script of one character role a: <steps> set-up steps (obj.transform), an empty 1 s Timeline (the stored form)."""
    return {"format": "hs2next.script", "version": 1, "name": name, "description": "", "map": None,
            "roles": [{"role": "a", "kind": "chara", "sex": "female"}],
            "setup": [{"cmd": "obj.transform", "key": "$a", "pos": [i * 0.1, 0, 0]} for i in range(steps)], "prepare": [],
            "timeline": {"duration": 1, "loop": False, "autoplay": False, "tracks": [], "events": []}}


def jw5(C):
    C.call({"cmd": "scene.new"})
    keys = load_targets(C, {"a": "female"})
    path = os.path.join(C.ud, "jw5_setup20.hs2nscript")
    write_script(path, tiny_script("Set-up of 20 steps", 20))
    sid = C.call({"cmd": "script.import", "path": path, "to": "scene"})["script"]
    C.call({"cmd": "script.cast", "script": sid, "roles": keys})
    guard_cast(C, sid, read_doc(path))
    r = C.call({"cmd": "scriptprobe.play", "script": sid, "prepare": "never"}, timeout=600)
    present = r.get("setupSent") == 20 and r.get("done")
    C.R.add("JW5", ("PASS" if r["ms"] <= BAR["JW5"] else "WARN") if present else "FAIL", "a set-up of 20 steps: the play %.2f ms on the main thread (steps sent %s, done %s, failures %s). Bar %.0f ms"
            % (r["ms"], r.get("setupSent"), r.get("done"), r.get("failures"), BAR["JW5"]), raw=r)
    C.call({"cmd": "script.stop", "reset": False})


def jw7_parts(C):
    """A scene without scripts: the parts hold nothing and do no work, no subscription, no byte in the file."""
    C.call({"cmd": "scene.new"})
    load_targets(C, {"a": "female", "b": "male"})
    C.frames(30)
    r = C.call({"cmd": "scriptprobe.parts", "rounds": 1000}, timeout=600)
    saved = C.call({"cmd": "scene.save", "name": "script_jw7", "overwrite": True, "thumb": "placeholder", "clock": CLOCK}, timeout=600)
    path = saved.get("path")
    members, marker = None, None
    if path and os.path.isfile(path):
        with zipfile.ZipFile(path) as z:
            sc = json.loads(z.read("scene.json").decode("utf-8-sig"))
        members = [k for k in sc.keys() if k == "scripts"]
        marker = "script" in (sc.get("timeline") or {})
    work = r.get("moves", 1) == 0 and not r.get("follow") and not r.get("dof") and r.get("slots") == [0, 0, 0] and not r.get("running")
    ok = work and r.get("subscribed") is False and members == [] and marker is False
    C.R.add("JW7p", "PASS" if ok else "FAIL", "a scene without scripts: the parts' work: moves %s, follow %s, dof %s, slots %s, a play running %s (their tick %.2f µs median, idle); subscriptions: the script part subscribed %s; "
            "bytes in the file: a member scripts %s, a Timeline marker %s (scene.json of %s)" % (r.get("moves"), r.get("follow"), r.get("dof"), r.get("slots"), r.get("running"), r.get("medianUs", 0),
                                                                                                 r.get("subscribed"), members, marker, os.path.basename(path or "?")), raw=r)


def jw9(C):
    path = os.path.join(C.ud, "jw9_row.hs2nscript")
    write_script(path, tiny_script("A store row"))
    t0 = time.time()
    n_ok = 0
    for _ in range(1000):
        if C.call({"cmd": "script.import", "path": path, "to": "store"}, check=False).get("ok"):
            n_ok += 1
    fill = time.time() - t0
    lst = C.call({"cmd": "scriptprobe.inline", "request": {"cmd": "script.list", "store": True}, "rounds": 7, "warm": 1}, timeout=600)
    imp = C.call({"cmd": "scriptprobe.inline", "request": {"cmd": "script.import", "path": path, "to": "store"}, "rounds": 5, "warm": 0}, timeout=600)
    rows = len(C.call({"cmd": "script.list", "store": True}).get("rows") or [])
    lms, ims = lst["medianUs"] / 1000.0, imp["medianUs"] / 1000.0
    present = n_ok == 1000 and rows >= 1000 and lst.get("ok") and imp.get("ok")
    C.R.add("JW9", ("PASS" if lms <= BAR["JW9list"] and ims <= BAR["JW9import"] else "WARN") if present else "FAIL",
            "the store: script.list {store} of %d rows %.2f ms median of 7 (bar %.0f); one import %.2f ms median of 5 (bar %.0f); inline on the main thread; filled with 1000 imports over HTTP in %.1f s (%d ok)"
            % (rows, lms, BAR["JW9list"], ims, BAR["JW9import"], fill, n_ok), list=lst, imp=imp)


def run_d(C):
    a = C.a
    files = ([a.sample] if os.path.isfile(a.sample) else []) + a.scripts
    if not os.path.isfile(a.sample):
        C.R.add("SAMPLE", "FAIL", "the sample is absent: %s" % a.sample)
    expect = {}
    if a.expect:
        with open(a.expect, encoding="utf-8-sig") as f:
            expect = json.load(f)
    for p in files:
        name = os.path.basename(p)
        if os.path.abspath(p) == os.path.abspath(a.sample):
            ex, jd = ("sample", sample_expect(read_doc(p))), True
        else:
            ex, jd = expect.get(name), name in a.jd
        try:
            play_script(C, p, ex, jd)
        except CastGuard as e:
            C.R.add("GUARD" + re.sub(r"[^A-Za-z0-9_]", "_", name)[:16], "FAIL", "%s: %s" % (name, e))
        except Abort:
            raise
        except Exception as e:   # one script's error: its line, then the next
            C.R.add("RUN" + re.sub(r"[^A-Za-z0-9_]", "_", name)[:16], "FAIL", "%s: %s: %s" % (name, type(e).__name__, e), traceback=traceback.format_exc(limit=8))
    for cid, fn in (("JW3", jw3), ("JW4", jw4_on), ("JW5", jw5), ("JW7", jw7_parts), ("JW9", jw9)):
        if a.want(cid):
            try:
                fn(C)
            except Abort:
                raise
            except Exception as e:
                C.R.add(cid, "FAIL", "%s: %s" % (type(e).__name__, e), traceback=traceback.format_exc(limit=8))


# ---------------------------------------------------------------- no player: JW7s

def jw7_subs(a, R):
    """Where the script parts' code subscribes to events, read (a scene without scripts subscribes to none: JW7p reads the part's flag in the player)."""
    root = os.path.join(a.project, "Assets", "HS2Next")
    files = []
    for rel in SUB_FILES:
        files += glob.glob(os.path.join(root, rel))
    hits = []
    for path in sorted(set(files)):
        rel = os.path.relpath(path, root).replace("\\", "/")
        with open(path, encoding="utf-8") as f:
            for no, line in enumerate(f, 1):
                if SUB_RE.search(line.split("//")[0]):
                    hits.append("%s:%d %s" % (rel, no, line.strip()[:100]))
    R.add("JW7s", "PASS" if files else "FAIL", "the code read for subscriptions: %d files; event subscriptions: %s (each must be made by a play or a recording only: read by hand; JW7p reads the flag in the player)"
          % (len(set(files)), "; ".join(hits) or "none"), hits=hits)


# ---------------------------------------------------------------- main

def write_reports(a, R, exit_code, started):
    n_pass, n_fail, n_warn = R.count("PASS"), R.count("FAIL"), R.count("WARN")
    summary = "SUMMARY: %d PASS, %d FAIL" % (n_pass, n_fail)
    warns = [x["id"] for x in R.items if x["verdict"] == "WARN"]
    lines = [summary, "WARN: %d%s" % (n_warn, (" (" + ", ".join(warns) + ")") if warns else ""), "script_player_check %s; exe %s; exit code %d; %.0f s" % (started, a.exe, exit_code, time.perf_counter() - T0)]
    for x in R.items:
        lines.append("%-7s %-4s  %s" % (x["id"], x["verdict"], x["text"]))
    lines.append(summary)
    with open(os.path.join(a.out, "script_player_report.txt"), "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")
    with open(os.path.join(a.out, "script_player_check.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump({"started": started, "exe": a.exe, "exitCode": exit_code, "summary": summary, "checks": R.items}, f, ensure_ascii=False, indent=1, default=str)


def main(argv):
    for s in (sys.stdout, sys.stderr):
        try:
            s.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, ValueError):
            pass
    ap = argparse.ArgumentParser()
    ap.add_argument("--exe", required=True)
    ap.add_argument("--project", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--port", type=int, default=17909)
    ap.add_argument("--timeout-min", type=float, default=80)
    ap.add_argument("--only", default="")
    ap.add_argument("--sample", default=None)
    ap.add_argument("--scripts", default="")
    ap.add_argument("--expect", default=None)
    ap.add_argument("--jd", default="")
    ap.add_argument("--baseline-exe", default=None)
    ap.add_argument("--gfx", choices=["d3d11", "d3d12"], default=None)
    ap.add_argument("--min-free-gb", type=float, default=8)
    ap.add_argument("--ram-wait-min", type=float, default=10)
    ap.add_argument("--ready-timeout-min", type=float, default=12)
    ap.add_argument("--keep-frames", action="store_true", help=argparse.SUPPRESS)
    a = ap.parse_args(argv)
    a.exe, a.project, a.out = os.path.abspath(a.exe), os.path.abspath(a.project), os.path.abspath(a.out)
    a.sample = os.path.abspath(a.sample or os.path.join(a.project, "Tests", "SceneScripts", "sample.hs2nscript"))
    a.scripts = [os.path.abspath(x.strip()) for x in a.scripts.split(",") if x.strip()]
    a.jd = set(x.strip() for x in a.jd.split(",") if x.strip())
    a.keep_frames = False   # the frames are always deleted after they are counted (the content rule)
    only = set(x.strip().upper() for x in a.only.split(",") if x.strip())
    # a session's letter selects every check of that session (the sessions themselves are chosen from `only` as given)
    wanted = set(only)
    for letter, ids in (("N", ("JX6", "JX4P", "JW4")), ("F", ("JW7F",)), ("R", ("JR4", "JR6", "YT1", "YT3")),
                        ("D", ("JR5", "JD1", "JD2", "JV3", "JW1", "JW2", "JW3", "JW4", "JW5", "JW6", "JW7", "JW8", "JW9"))):
        if letter in only:
            wanted.update(ids)
    a.want = lambda c: not wanted or c.upper() in wanted or any(c.upper().startswith(o) for o in wanted if len(o) > 1)
    a.session_on = lambda letter, ids: not only or letter in only or any(i.upper() in only for i in ids)
    a.store = {}
    os.makedirs(a.out, exist_ok=True)
    started = utc_now()
    R = Results()
    deadline = time.time() + a.timeout_min * 60
    exit_code = 0
    live = []
    stop_gate = threading.Event()
    gated = [False]

    def stop_all(why):
        for C in live:
            C.aborted = why
            kill(C)

    def on_timeout():
        log("timed out after %.1f min" % a.timeout_min)
        stop_all("timed out after %.1f min (--timeout-min); the started player was killed" % a.timeout_min)

    def gate_poll():
        while not stop_gate.wait(30):
            try:
                games = running_games()
            except Exception:
                continue
            if games:
                gated[0] = True
                stop_all("aborted by the game gate (%s)" % ", ".join(games))
                return

    watchdog = threading.Timer(a.timeout_min * 60, on_timeout)
    watchdog.daemon = True
    watchdog.start()
    threading.Thread(target=gate_poll, daemon=True).start()

    def ram():
        need, end = a.min_free_gb, time.time() + a.ram_wait_min * 60
        g = free_gb()
        while g is not None and g < need and time.time() < end:
            log("free RAM %.1f GB < %.1f GB: waiting" % (g, need))
            time.sleep(15)
            g = free_gb()
        return g

    def session(letter, plugins, body_, exe=None, job=False):
        nonlocal exit_code
        g = ram()
        if g is not None and g < a.min_free_gb:
            R.add("SETUP" + letter, "FAIL", "free RAM %.1f GB < %.1f GB after %.0f min: session %s not started" % (g, a.min_free_gb, a.ram_wait_min, letter))
            return
        if letter == "D":
            # session D starts in a fresh folder: an earlier run's script store would hold JW9's 1,000 imports and the next fill doubles it (6.10's whole suite read 2,010 rows)
            d = os.path.join(a.out, "d")
            if os.path.isdir(d):
                kept = "%s_prev_%s" % (d, time.strftime("%Y%m%d-%H%M%S"))
                os.rename(d, kept)
                R.add("FRESHD", "INFO", "session D's folder of an earlier run moved aside to %s (kept for the record)" % kept)
        C = Run(a, R, letter, plugins, exe)
        live.append(C)
        try:
            if job:
                body_(C, deadline)
            else:
                start(C, deadline)
                body_(C)
                stop(C)
        except SetupError as e:
            R.add("SETUP" + letter, "WARN" if gated[0] else "FAIL", str(e))
            exit_code = 3 if gated[0] else 2
        except Abort as e:
            if gated[0]:
                R.add("GATE", "WARN", str(e))
                exit_code = 3
            else:
                R.add("ABORT", "FAIL", "session %s stopped: %s" % (letter, e))
        except Exception as e:   # one session's error: its line, then the next session
            R.add("RUN" + letter, "FAIL", "session %s: %s: %s" % (letter, type(e).__name__, e), traceback=traceback.format_exc(limit=8))
        finally:
            kill(C)
            try:
                check_log(C)
            except Exception as e:
                R.add("LOG" + letter, "WARN", "player.log not read: %s" % e)
            live.remove(C)
        return C

    try:
        gate_warning = game_processes()[1]
        if gate_warning:
            R.add("GATE", "WARN", gate_warning)
        games = running_games()
        if games:
            R.add("SETUP", "WARN", "the game gate: %s runs; no player row now" % ", ".join(games))
            exit_code = 3
        if a.want("JW7s") or a.want("JW7"):
            jw7_subs(a, R)
        need_player = not only or bool(only & {"N", "F", "R", "D"}) or any(x.startswith(("JX", "JR", "JD", "JW", "JV", "YT")) for x in only - {"JW7S"})
        if exit_code == 0 and need_player:
            plugins = build_probe(a, a.exe, a.out)
            if a.session_on("N", ["JX6", "JX4P", "JW4"]):
                session("N", plugins, session_n, job=True)
            if exit_code == 0 and a.session_on("F", ["JW7F", "JW7"]):
                old = os.path.abspath(a.baseline_exe) if a.baseline_exe else None
                old_plugins = build_probe(a, old, os.path.join(a.out, "old"), True) if old and os.path.isfile(old) else None
                if old_plugins:
                    session("O1", old_plugins, session_frames, old, job=True)
                if exit_code == 0:
                    session("F", plugins, session_frames, job=True)
                if exit_code == 0 and old_plugins:
                    session("O2", old_plugins, session_frames, old, job=True)
                jw7_frame(a, R)
            if exit_code == 0 and a.session_on("R", ["JR4", "JR6", "YT1", "YT3"]):
                session("R", plugins, run_r)
            if exit_code == 0 and a.session_on("D", ["JR5", "JD1", "JD2", "JV3", "JW1", "JW2", "JW3", "JW4", "JW5", "JW6", "JW7", "JW8", "JW9"]):
                session("D", plugins, run_d)
                jr5_judge(a, R, os.path.join(a.out, "d", "player.log"))
            if a.session_on("N", ["JW4"]) or a.session_on("D", ["JW4"]):
                jw4_judge(a, R)
    except SetupError as e:
        R.add("SETUP", "FAIL", str(e))
        exit_code = 2
    except Exception as e:
        R.add("RUN", "FAIL", "%s: %s" % (type(e).__name__, e), traceback=traceback.format_exc(limit=8))
    finally:
        stop_gate.set()
        watchdog.cancel()
        if exit_code == 0 and R.count("FAIL"):
            exit_code = 1
        write_reports(a, R, exit_code, started)
        log("SUMMARY: %d PASS, %d FAIL, %d WARN -> exit %d; report %s" % (R.count("PASS"), R.count("FAIL"), R.count("WARN"), exit_code, os.path.join(a.out, "script_player_report.txt")))
    return exit_code


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
