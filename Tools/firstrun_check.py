"""firstrun_check - the first-run guide of the distribution walked over the bridge (rows FirstRun.player / FirstRun.cold; Phase 5.3).

A plain copy of the distribution folder with an earlier HS2Next UserData planted next to its exe (the committed save-format
fixtures: a scene, a character, a coordinate, and config/options.json), started with no arguments on an EMPTY override root
(HS2NEXT_DATA_ROOT=<out>/データ root/):
  start 1  the guide opens (ui.state mode setup, page welcome; ping.ready.setup false); Next -> "your files" names the root and
           offers the copy of the earlier folder; its Copy button: the result line, the files under the root, migration.json, the
           earlier folder unchanged, a second copy copies nothing, no second offer (FR7); Next -> the HS2 page: the project's HS2 folder typed into setup.hs2.path, validation ok with the
           zipmod count; Next -> "more mods"; the guide's Quit (FR6: the player ends, exit 0).
  start 2  the guide reopens on "more mods" with the HS2 folder kept (FR6); Next -> the cache page: --cache warm chooses the
           project's cache, cold keeps the default (empty) cache under the root; Build the index: the settings file holds exactly
           the chosen keys (hs2Root, and cacheDir for warm); the index page shows its progress (cold: updated at least every 5 s,
           FR8, and the index time SU4 <= 8 min) and turns into the ready page at studioDb, which shows both camera tips (numpad, no
           numpad) inside the part of the guide seen without scrolling; Finish: the Studio is ready
           (ping.appReady, ui.state of the Studio), setup.json completed (FR4); with an unsaved scene File > Setup guide asks, then
           the quit's unsaved-changes prompt: Cancel keeps the one app running (same PID, bridge, lock; RS1, RS2), Don't save quits
           it and only then a new app starts, waits for the old PID, opens the guide with Next enabled on the HS2 page for the
           settings' valid folder and walks to the Studio (RS3-RS5, a manual walk-through); File > Quit through the UI (ui.action app.quit):
           exit 0 within 10 s, the quit line in Player.log and no exception line after it (QT1).
Player.log (the per-user folder LocalLow/stp3/HS2Next) is copied into --out after each exit.

Usage:  python -I Tools/firstrun_check.py --dist <project>/Builds/Dist/HS2Next --project <project> --out <out> --cache warm|cold
        [--port 17322] [--timeout-min 10|20] [--gpu-lock FILE] [--input-lock FILE]
The runner holds the GPU and input locks, applies the game gate and waits for the port. Report <out>/firstrun_report.txt: first
and last line "SUMMARY: N PASS, M FAIL"; one "PASS|FAIL <id> <text>" line per check; numbers only. Exit code: 0 pass, 1 a check
failed, 2 setup error / refused, 3 aborted by the game gate.
"""
import argparse
import json
import os
import re
import shutil
import subprocess
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import noargs_check as nc  # noqa: E402  (the shared helpers: report, copy, HTTP, waits)

sys.stdout.reconfigure(encoding="utf-8", errors="replace")


def ui(port, token, cmd, **kw):
    body = {"cmd": cmd}
    body.update(kw)
    st, j = nc.http(port, body, token, timeout=60)
    return j if isinstance(j, dict) else {"ok": False, "error": "HTTP %s: %s" % (st, str(j)[:200])}


def action(port, token, name, **args):
    return ui(port, token, "ui.action", action=name, args=args) if args else ui(port, token, "ui.action", action=name)


def query(port, token, name):
    r = ui(port, token, "ui.query", name=name)
    els = r.get("elements") or []
    return els[0] if els else {}


def wait_page(port, token, page, seconds, p):
    t0 = time.time()
    st = {}
    while time.time() - t0 < seconds and p.poll() is None:
        st = ui(port, token, "ui.state")
        if st.get("mode") == "setup" and st.get("page") == page:
            return st
        time.sleep(0.2)
    return None


def tip_strings(project, keys):
    """The cells (en, ja, zh-Hans) of string keys in the project's strings.tsv: {key: set of texts}."""
    path = os.path.join(project, "Assets", "HS2Next", "Resources", "UI", "L10n", "strings.tsv")
    out = {k: set() for k in keys}
    with open(path, encoding="utf-8") as f:
        for line in f:
            cells = line.rstrip("\r\n").split("\t")
            if cells and cells[0] in out:
                out[cells[0]].update(c for c in cells[1:4] if c)
    return out


def inside(inner, outer, tol=0.5):
    x, y, w, h = inner
    X, Y, W, H = outer
    return x >= X - tol and y >= Y - tol and x + w <= X + W + tol and y + h <= Y + H + tol


def overlap(a, b):
    x0, y0 = max(a[0], b[0]), max(a[1], b[1])
    x1, y1 = min(a[0] + a[2], b[0] + b[2]), min(a[1] + a[3], b[1] + b[3])
    return [x0, y0, max(0.0, x1 - x0), max(0.0, y1 - y0)]


def ready_tips(port, token, project):
    """The ready page's camera tips (numpad, no numpad): each present once with its text and inside the part of the guide a user
    sees without scrolling - the window, the card and the page body's viewport (panel px). Returns (ok, detail)."""
    texts = tip_strings(project, ["setup.ready.tipCamera", "setup.ready.tipNoNumpad"])
    tips = ui(port, token, "ui.query", name=".hs-setup-tip", all=True).get("elements") or []
    root = query(port, token, ".hs-setup-root")
    card = query(port, token, "setup.card")
    body = query(port, token, "setup.body")
    if not root or not card or not body:
        return False, "no root / card / body"
    vps = [e for e in (ui(port, token, "ui.query", name=".unity-scroll-view__content-viewport", all=True).get("elements") or [])
           if inside(e["worldBound"], body["worldBound"], 1)]
    area = overlap(overlap(root["worldBound"], card["worldBound"]), (vps[0] if vps else body)["worldBound"])
    found, ok = [], True
    for key, cells in texts.items():
        match = [t for t in tips if (t.get("text") or "") in cells]
        t = match[0] if len(match) == 1 else None
        good = t is not None and t.get("visible") and inside(t["worldBound"], area)
        ok = ok and bool(good)
        found.append("%s %s" % (key.split(".")[-1], [round(v, 1) for v in t["worldBound"]] if t else "missing (%d matches)" % len(match)))
    return ok, "visible area %s; %s" % ([round(v, 1) for v in area], "; ".join(found))


def read_endpoint(bridge_dir):
    try:
        with open(os.path.join(bridge_dir, "endpoint.json"), encoding="utf-8") as f:
            return json.load(f)
    except (OSError, ValueError):
        return None


def pid_alive(pid):
    if not pid:
        return False
    try:
        out = subprocess.run(["tasklist", "/FI", "PID eq %d" % int(pid), "/NH", "/FO", "CSV"], capture_output=True, text=True, timeout=10).stdout
        return ('"%d"' % int(pid)) in out
    except Exception:
        return False


def stop_pid(pid):
    subprocess.run(["taskkill", "/PID", str(int(pid)), "/T", "/F"], capture_output=True)


def copies(folder):
    """The PIDs of HS2Next.exe processes started from <folder> (this script's copy of the distribution)."""
    cmd = ("Get-CimInstance Win32_Process -Filter \"Name='HS2Next.exe'\" | Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith('%s', "
           "[StringComparison]::OrdinalIgnoreCase) } | ForEach-Object { $_.ProcessId }") % folder.replace("'", "''")
    try:
        out = subprocess.run(["powershell.exe", "-NoProfile", "-Command", cmd], capture_output=True, text=True, timeout=30).stdout
        return sorted(int(x) for x in out.split() if x.strip().isdigit())
    except Exception:
        return []


def click(port, token, name):
    return ui(port, token, "ui.input", events=[{"type": "pointer", "target": name, "phase": "click", "button": 0}], frames=2)


def wait_modal(port, token, modal, seconds, p):
    t0 = time.time()
    while time.time() - t0 < seconds and p.poll() is None:
        if ui(port, token, "ui.state").get("modal") == modal:
            return True
        time.sleep(0.2)
    return False


def wait_page_pid(port, token, page, seconds, pid):
    t0 = time.time()
    while time.time() - t0 < seconds and pid_alive(pid):
        st = ui(port, token, "ui.state")
        if st.get("mode") == "setup" and st.get("page") == page:
            return st
        time.sleep(0.25)
    return None


def plant_legacy(project, exe_dir):
    """An earlier HS2Next UserData next to the exe: the save-format fixtures and an options file."""
    fx = os.path.join(project, "Tests", "SaveFormat", "fixtures", "v02_dev1")
    ud = os.path.join(exe_dir, "UserData")
    planted = {"scene": ("scene.hs2nscene", "first.hs2nscene"), "chara": ("chara.hs2nchara", "first.hs2nchara"),
               "coordinate": ("coord.hs2ncoord", "first.hs2ncoord")}
    for folder, (src, dst) in planted.items():
        os.makedirs(os.path.join(ud, folder), exist_ok=True)
        shutil.copy2(os.path.join(fx, src), os.path.join(ud, folder, dst))
    os.makedirs(os.path.join(ud, "config"), exist_ok=True)
    with open(os.path.join(ud, "config", "options.json"), "w", encoding="utf-8") as f:
        json.dump({"version": 1, "general": {"language": "en"}}, f)
    return ud, ["scene/first.hs2nscene", "chara/first.hs2nchara", "coordinate/first.hs2ncoord", "config/options.json"]


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--dist", required=True)
    ap.add_argument("--project", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--cache", choices=["warm", "cold"], default="warm")
    ap.add_argument("--port", type=int, default=17322)
    ap.add_argument("--timeout-min", type=float, default=10)
    ap.add_argument("--gpu-lock")
    ap.add_argument("--input-lock")
    a = ap.parse_args()
    t_all = time.time()
    rep = nc.Report()
    out = os.path.abspath(a.out)
    os.makedirs(out, exist_ok=True)
    report_path = os.path.join(out, "firstrun_report.txt")
    locks = []

    def finish(code):
        for l in locks:
            try:
                os.remove(l)
            except OSError:
                pass
        rep.write(report_path, time.time() - t_all)
        sys.exit(code)

    project = os.path.abspath(a.project)
    dist = os.path.abspath(a.dist)
    if not os.path.isfile(os.path.join(dist, "HS2Next.exe")):
        rep.check("setup", False, "the distribution folder holds HS2Next.exe", dist)
        finish(2)
    if nc.port_busy(a.port):
        rep.check("setup", False, "port %d is free" % a.port)
        finish(2)
    try:
        with open(os.path.join(project, "Config", "settings.local.json"), encoding="utf-8") as f:
            hs2 = json.load(f).get("hs2Root")
        hs2 = os.path.abspath(os.path.join(project, hs2)) if hs2 else None
    except (OSError, ValueError):
        hs2 = None
    if not hs2 or not os.path.isdir(hs2):
        rep.check("setup", False, "the project's settings name the HS2 folder", hs2)
        finish(2)
    for lk in (a.gpu_lock, a.input_lock):
        if lk:
            fd = os.open(lk, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
            os.write(fd, ("F firstrun_check PID %d start %s\n" % (os.getpid(), time.strftime("%Y-%m-%d %H:%M:%S"))).encode())
            os.close(fd)
            locks.append(lk)
    limit = a.timeout_min * 60
    warm = a.cache == "warm"
    project_cache = os.path.join(project, "Cache")
    p = None
    try:
        copy = os.path.join(out, "copy")
        nc.remove_own_tree(copy)
        nc.plain_copy(dist, copy)
        legacy, planted = plant_legacy(project, copy)
        root = os.path.join(out, "データ root")
        nc.remove_own_tree(root)
        os.makedirs(root)
        exe = os.path.join(copy, "HS2Next.exe")
        env = dict(os.environ)
        env["HS2NEXT_DATA_ROOT"] = root
        env.setdefault("HS2NEXT_NO_PLUGINS", "1")
        env.setdefault("HS2NEXT_PRIVATE_OFF", "1")
        bridge_dir = os.path.join(root, "UserData", "Bridge")

        def token_now():
            try:
                with open(os.path.join(bridge_dir, "token"), encoding="utf-8") as f:
                    return f.read().strip() or None
            except OSError:
                return None

        # ---------------- start 1
        so = open(os.path.join(out, "start1_stdout.txt"), "w", encoding="utf-8")
        p = nc.start(exe, env, so)
        info = nc.wait_endpoint(bridge_dir, p.pid, 60, p)
        if info == "game":
            nc.stop(p)
            rep.info("aborted by the game gate")
            finish(3)
        rep.check("FR4", isinstance(info, dict), "start 1: endpoint.json names the player's PID", info)
        tok = token_now() if isinstance(info, dict) and info.get("tokenRequired") else None
        st = wait_page(a.port, tok, "welcome", 60, p)
        rep.check("FR4", st is not None, "the guide opens on the welcome page")
        ping = ui(a.port, tok, "ping")
        rep.check("FR4", (ping.get("ready") or {}).get("setup") is False and not ping.get("appReady"), "ping: setup not ready while the guide runs", json.dumps(ping)[:200])
        # the guide preselects the system language; the checks read English texts
        action(a.port, tok, "setup.language.en")
        lang = ui(a.port, tok, "ui.state").get("language")
        rep.check("FR4", lang == "en", "page 0 switches the language at once (en)", lang)
        action(a.port, tok, "setup.next")
        rep.check("FR4", wait_page(a.port, tok, "files", 20, p) is not None, "Next -> your files")
        shown = (query(a.port, tok, "setup.files.root").get("text") or "")
        rep.check("FR4", os.path.normcase(shown) == os.path.normcase(root), "the files page names the data root", shown)
        # FR7: the page's copy offer of the earlier UserData next to the exe, accepted with its Copy button
        offer = query(a.port, tok, "setup.migrate.text").get("text") or ""
        rep.check("FR7", ("%d of your files" % len(planted)) in offer and os.path.normcase(legacy) in os.path.normcase(offer),
                  "page 1 offers the copy of the earlier UserData (%d files)" % len(planted), offer)
        action(a.port, tok, "setup.migrate.copy")
        result = ""
        t0 = time.time()
        while time.time() - t0 < 60 and p.poll() is None:
            result = query(a.port, tok, "setup.migrate.result").get("text") or ""
            if result:
                break
            time.sleep(0.25)
        # page 0 stored the language in config/options.json already: the planted options file is a conflict, kept as it was
        rep.check("FR7", result.startswith("Copied %d files" % (len(planted) - 1)) and "1 conflicts" in result,
                  "the copy reports %d files and the options file as a kept conflict" % (len(planted) - 1), result)
        missing = [r for r in planted if not os.path.isfile(os.path.join(root, "UserData", *r.split("/")))]
        rep.check("FR7", not missing, "the copied files are under the new root", missing)
        rep.check("FR7", os.path.isfile(os.path.join(root, "UserData", "migration.json")), "migration.json is written")
        rep.check("FR7", os.path.isfile(os.path.join(legacy, "scene", "first.hs2nscene")), "the earlier folder is unchanged (its scene still there)")
        mig2 = ui(a.port, tok, "app.setup", op="migrate", source=legacy, suggestions=False)
        rep.check("FR7", (mig2.get("migration") or {}).get("copied") == 0 and (mig2.get("migration") or {}).get("same") == len(planted) - 1,
                  "a second copy copies nothing (all the same)", json.dumps(mig2.get("migration"))[:200])
        paths = ui(a.port, tok, "app.paths")
        rep.check("FR7", paths.get("migration") is None, "no second offer after the copy (app.paths)", json.dumps(paths.get("migration"))[:200])
        action(a.port, tok, "setup.next")
        rep.check("FR4", wait_page(a.port, tok, "hs2", 20, p) is not None, "Next -> the HS2 folder page")
        action(a.port, tok, "setup.hs2.path", value=hs2)
        status = ""
        t0 = time.time()
        while time.time() - t0 < 120 and p.poll() is None:
            status = query(a.port, tok, "setup.hs2.status").get("text") or ""
            if re.search(r"\d+ mods", status):
                break
            time.sleep(0.3)
        rep.check("FR4", status.startswith("Honey Select 2 found") and re.search(r"\d+ mods", status) is not None, "validation ok with the zipmod count", status)
        action(a.port, tok, "setup.next")
        rep.check("FR6", wait_page(a.port, tok, "mods", 20, p) is not None, "Next -> more mods (page 3)")
        action(a.port, tok, "setup.quit")
        code = nc.wait_exit(p, 15)
        rep.check("FR6", code == 0, "the guide's Quit ends the player (exit 0)", code)
        nc.stop(p)
        so.close()
        try:
            shutil.copy2(nc.per_user_log(), os.path.join(out, "start1_Player.log"))
        except OSError:
            pass
        try:
            with open(os.path.join(root, "UserData", "config", "setup.json"), encoding="utf-8") as f:
                sj = json.load(f)
        except (OSError, ValueError):
            sj = {}
        rep.check("FR6", sj.get("lastPage") == "mods", "setup.json keeps the last page (mods)", sj.get("lastPage"))
        rep.check("FR6", not os.path.exists(os.path.join(root, "Config", "settings.local.json")), "no settings file before the guide ends")

        # ---------------- start 2
        so = open(os.path.join(out, "start2_stdout.txt"), "w", encoding="utf-8")
        p = nc.start(exe, env, so)
        info = nc.wait_endpoint(bridge_dir, p.pid, 60, p)
        if info == "game":
            nc.stop(p)
            rep.info("aborted by the game gate")
            finish(3)
        tok = token_now() if isinstance(info, dict) and info.get("tokenRequired") else None
        st = wait_page(a.port, tok, "mods", 60, p)
        rep.check("FR6", st is not None, "start 2: the guide resumes on more mods")
        inst = query(a.port, tok, "setup.mods.install").get("text") or ""
        rep.check("FR6", os.path.normcase(os.path.join(hs2, "mods")) in os.path.normcase(inst), "the HS2 folder chosen before is kept", inst)
        action(a.port, tok, "setup.next")
        rep.check("FR4", wait_page(a.port, tok, "cache", 20, p) is not None, "Next -> the cache page")
        free = query(a.port, tok, "setup.cache.free").get("text") or ""
        rep.check("FR4", "GB" in free, "the cache page shows the free space", free)
        if warm:
            action(a.port, tok, "setup.cache.path", value=project_cache)
        t_index = time.time()
        action(a.port, tok, "setup.next")
        st = wait_page(a.port, tok, "index", 30, p)
        rep.check("FR4", st is not None or wait_page(a.port, tok, "ready", 5, p) is not None, "Build the index -> the index page")
        settings_file = os.path.join(root, "Config", "settings.local.json")
        try:
            with open(settings_file, encoding="utf-8") as f:
                written = json.load(f)
        except (OSError, ValueError):
            written = {}
        want = {"hs2Root", "cacheDir"} if warm else {"hs2Root"}
        rep.check("FR4", set(written) == want and os.path.normcase(written.get("hs2Root") or "") == os.path.normcase(hs2) and
                  (not warm or os.path.normcase(written.get("cacheDir") or "") == os.path.normcase(project_cache)),
                  "the settings file holds exactly the chosen keys (%s)" % ", ".join(sorted(want)), json.dumps(written, ensure_ascii=False))
        # the progress until the ready page
        texts, stamps = [], []
        ready_at = None
        while time.time() - t_index < limit and p.poll() is None:
            if nc.game_running():
                nc.stop(p)
                rep.info("aborted by the game gate")
                finish(3)
            s = ui(a.port, tok, "ui.state")
            if s.get("mode") == "setup" and s.get("page") == "ready":
                ready_at = time.time() - t_index
                break
            t = query(a.port, tok, "setup.progress").get("text")
            if t and (not texts or t != texts[-1]):
                texts.append(t)
                stamps.append(time.time() - t_index)
            time.sleep(0.5)
        rep.check("FR4", ready_at is not None, "the index page turns into the ready page (%.1f s)" % (ready_at or -1))
        gaps = [b2 - b1 for b1, b2 in zip(stamps, stamps[1:])]
        rep.info("index: %.1f s, %d progress lines, longest gap %.1f s" % (ready_at or -1, len(texts), max(gaps) if gaps else 0))
        if not warm:
            rep.check("FR8", len(texts) >= 2 and (max(gaps) if gaps else 99) <= 5.5, "the progress line updates at least every 5 s (%d lines)" % len(texts), gaps[:10])
            rep.check("SU4", ready_at is not None and ready_at <= 480, "cold index %.1f s (<= 480 s), progress every <= 5 s" % (ready_at or -1))
        tip = query(a.port, tok, "setup.ready.bridge")
        rep.check("FR4", bool(tip.get("text")), "the ready page has its tips (the bridge token line)")
        tips_ok, tips_detail = ready_tips(a.port, tok, project)
        rep.check("FR4", tips_ok, "the ready page shows both camera tips (numpad; no numpad) inside the visible part of the guide", tips_detail)
        rep.info("ready page tips: " + tips_detail)
        action(a.port, tok, "setup.finish")
        ready = None
        t0 = time.time()
        while time.time() - t0 < 60 and p.poll() is None:
            pg = ui(a.port, tok, "ping")
            s = ui(a.port, tok, "ui.state")
            if pg.get("appReady") and s.get("enabled") and s.get("mode") is None and s.get("layout") is not None:
                ready = time.time() - t0
                break
            time.sleep(0.25)
        rep.check("FR4", ready is not None, "Finish: the Studio is ready (ping.appReady, the Studio UI)")
        try:
            with open(os.path.join(root, "UserData", "config", "setup.json"), encoding="utf-8") as f:
                sj = json.load(f)
        except (OSError, ValueError):
            sj = {}
        rep.check("FR4", bool(sj.get("completedAt")) and not sj.get("skipped"), "setup.json: completed")
        banner = ui(a.port, tok, "ui.query", name="setup.banner")
        rep.check("FR4", banner.get("count") == 0, "no banner with an HS2 folder")
        # ---------------- File > Setup guide restarts through the quit (a manual walk-through: a second app had started on the same
        # UserData while the first one's unsaved-changes prompt held its quit)
        old_pid = p.pid
        action(a.port, tok, "add.folder")
        st0 = ui(a.port, tok, "ui.state")
        rep.check("RS1", st0.get("dirty") is True, "a new folder makes the scene unsaved", st0.get("dirty"))
        action(a.port, tok, "file.setupGuide")
        rep.check("RS1", wait_modal(a.port, tok, "setup.restart.title", 10, p), "File > Setup guide asks first")
        click(a.port, tok, "dialog-button-ok")
        rep.check("RS1", wait_modal(a.port, tok, "unsaved", 10, p), "the unsaved-changes prompt comes before any restart")
        rep.check("RS1", copies(copy) == [old_pid], "no second HS2Next runs while the prompt is open", copies(copy))
        click(a.port, tok, "dialog-button-cancel")
        time.sleep(2)
        st1 = ui(a.port, tok, "ui.state")
        ep = read_endpoint(bridge_dir)
        rep.check("RS2", p.poll() is None and st1.get("modal") is None and (ep or {}).get("pid") == old_pid and copies(copy) == [old_pid]
                  and os.path.isfile(os.path.join(root, "UserData", "app.lock")) and ui(a.port, tok, "ping").get("appReady") is True,
                  "Cancel keeps the app running: the same PID, its bridge answers, its UserData lock held, no restart",
                  "modal %s, endpoint pid %s, copies %s" % (st1.get("modal"), (ep or {}).get("pid"), copies(copy)))
        action(a.port, tok, "file.setupGuide")
        wait_modal(a.port, tok, "setup.restart.title", 10, p)
        click(a.port, tok, "dialog-button-ok")
        wait_modal(a.port, tok, "unsaved", 10, p)
        t_quit = time.time()
        click(a.port, tok, "dialog-button-discard")
        code = nc.wait_exit(p, 20)
        rep.check("RS3", code == 0, "Don't save: the app quits (exit 0) in %.1f s" % (time.time() - t_quit), code)
        so.close()
        # the new app: started by the helper only after the old process has ended; it waits for the old PID and the lock itself
        info, new_pid = None, None
        t0 = time.time()
        while time.time() - t0 < 90:
            ep = read_endpoint(bridge_dir)
            if ep and ep.get("pid") and ep.get("pid") != old_pid and pid_alive(ep.get("pid")):
                info, new_pid = ep, ep.get("pid")
                break
            time.sleep(0.3)
        rep.check("RS3", new_pid is not None, "a new app starts (%.1f s after the old one quit)" % (time.time() - t_quit), info)
        rep.check("RS3", not pid_alive(old_pid) and copies(copy) == ([new_pid] if new_pid else []), "only the new app runs (the old PID has ended)", copies(copy))
        if new_pid is None:
            finish(1)
        tok = token_now() if info.get("tokenRequired") else None
        rep.check("RS4", wait_page_pid(a.port, tok, "welcome", 60, new_pid) is not None, "the new app opens the guide (-setup) on the welcome page")
        action(a.port, tok, "setup.language.en")
        action(a.port, tok, "setup.next")
        wait_page_pid(a.port, tok, "files", 20, new_pid)
        action(a.port, tok, "setup.next")
        rep.check("RS5", wait_page_pid(a.port, tok, "hs2", 20, new_pid) is not None, "the re-run reaches the HS2 page")
        t0 = time.time()
        nxt, status = {}, ""
        while time.time() - t0 < 30:
            nxt = query(a.port, tok, "setup.next")
            status = query(a.port, tok, "setup.hs2.status").get("text") or ""
            if nxt.get("enabled") and status.startswith("Honey Select 2 found"):
                break
            time.sleep(0.25)
        rep.check("RS5", nxt.get("enabled") is True and status.startswith("Honey Select 2 found"),
                  "with the valid HS2 folder of the settings, Next is enabled without a keystroke (a manual walk-through)", "enabled %s, %s" % (nxt.get("enabled"), status))
        action(a.port, tok, "setup.next")
        wait_page_pid(a.port, tok, "mods", 20, new_pid)
        action(a.port, tok, "setup.next")
        wait_page_pid(a.port, tok, "cache", 20, new_pid)
        action(a.port, tok, "setup.next")
        rep.check("RS5", wait_page_pid(a.port, tok, "ready", 120, new_pid) is not None, "the re-run reaches the ready page")
        action(a.port, tok, "setup.finish")
        t0 = time.time()
        ready = None
        while time.time() - t0 < 60 and pid_alive(new_pid):
            pg = ui(a.port, tok, "ping")
            s2 = ui(a.port, tok, "ui.state")
            if pg.get("appReady") and s2.get("enabled") and s2.get("mode") is None and s2.get("layout") is not None:
                ready = time.time() - t0
                break
            time.sleep(0.25)
        rep.check("RS5", ready is not None, "Finish: the Studio of the new app is ready")
        log3 = os.path.join(out, "restart_Player.log")
        try:
            shutil.copy2(nc.per_user_log(), log3)
        except OSError:
            pass
        try:
            with open(log3, "rb") as f:
                raw = f.read()
        except OSError:
            raw = b""
        waited = re.search(rb"\[app\] restart: the previous HS2Next \(PID %d\) ended" % old_pid, raw) is not None
        rep.check("RS4", raw.count(b"\0") == 0 and waited, "the new app's Player.log is its own (no NUL bytes) and logs the wait for the old PID",
                  "NUL %d, wait line %s" % (raw.count(b"\0"), waited))
        q = action(a.port, tok, "app.quit")
        rep.check("QT1", q.get("ok") is True, "File > Quit through the UI answers", json.dumps(q)[:200])
        t0 = time.time()
        while time.time() - t0 < 10 and pid_alive(new_pid):
            time.sleep(0.2)
        rep.check("QT1", not pid_alive(new_pid), "the player ends within 10 s")
        if pid_alive(new_pid):
            stop_pid(new_pid)
        log2 = os.path.join(out, "start2_Player.log")
        try:
            shutil.copy2(nc.per_user_log(), log2)
        except OSError:
            pass
        ql, exc = nc.quit_lines(log2)
        rep.check("QT1", ql and exc == 0, "Player.log has the quit line and no exception line after it", "quit line %s, exceptions after %d" % (ql, exc))
    except SystemExit:
        raise
    except Exception as e:
        rep.check("setup", False, "the check ran to its end", "%s: %s" % (type(e).__name__, e))
        if p is not None:
            nc.stop(p)
        finish(1)
    finish(0 if rep.failed == 0 else 1)


if __name__ == "__main__":
    main()
