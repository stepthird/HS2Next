"""ime_caret_check - opt-in IME check of the app player: the caret after a conversion (Phase 5.3; not a runner row).

The caret was found between 鈴 and 木 after converting "suzuki" in a list's search field: UI Toolkit receives the text an
IME commits before the end of the composition and restored a stale caret (fixed by Runtime/UI/Input/ImeCommitFix.cs; the batch
regression check is UiShellTest group IM, which fakes the IME's events). This check uses a real IME and real keystrokes:
in each text field it clicks the field, switches the IME on, types "suzuki", Space (convert), Enter (commit), switches the
IME off, types a sentinel "x" and reads the field's value over the bridge (ui.query): the sentinel must follow the committed
text ("鈴木x"), not land inside it ("鈴x木"). The "cont" case types on after the conversion ("suzuki" Space "san" Enter), so
the next composition starts while the first one is committed.

The "cand" case opens the candidate list (a second Space) and checks that its window sits at the caret (the candidate-window
fix of Phase 3.5, spike S2a), then commits the chosen candidate.

Fields: the Add panel's character and item list search fields, the tree's rename field (a new folder's row clicked, then F2,
or the row's right-click menu > Rename; a rename without the keyboard focus in its field is a FAIL), the inspector's name
field, the Save As dialog's name field and the settings page's HS2 folder field (File > Options; the player reads a copy of
the project's settings in --out, so the field's write never reaches the project's file). Each field is left empty and
closed (Esc) afterwards.

IMEs: the other Japanese text service of the machine, then the one active in the player's process at start (for example
Copilot Keyboard and the Microsoft IME). The test plugin Tests/Plugins/ImeProbe (built and staged by
Tools/plugins.ps1 into --out) switches the text service for the player's process only and records the field's value, caret
and the key / IME events (written to the report for a failing field); the start profile is activated again before the
player quits. The IME is switched on and off with the Hankaku/Zenkaku key (scan 0x29; the player window has no IMM context)
and checked with a test composition ("a").

Real keystrokes go to the player window only while it is in front (an input stretch stops when it loses the foreground); the
previous foreground window and cursor position are given back, and the IME open state of that window is read before and
after (reported; set back when it changed). Nobody may type while it runs: the caller arranges that and holds the GPU and
input locks (or passes them with --gpu-lock / --input-lock to have them created and removed here). No idle-time wait.

Run:  python -I Tools/ime_caret_check.py --out <dir> [--exe Builds/App/HS2Next.exe] [--port 17606] [--ime start,other]
                                         [--fields all|chara-search,...] [--cases conv,cont] [--w 1600 --h 900]
                                         [--gpu-lock FILE --input-lock FILE]
Report <out>/report.txt: first line "SUMMARY: N PASS, M FAIL"; one PASS / FAIL / SKIP line per field, IME and case.
Exit: 0 PASS, 1 FAIL, 2 setup / bridge error, 3 nothing checked (the IME could not be switched on, the game runs).
"""
import argparse
import ctypes
import ctypes.wintypes as wt
import json
import os
import shutil
import subprocess
import sys
import time
import urllib.request

sys.stdout.reconfigure(encoding="utf-8")
sys.stderr.reconfigure(encoding="utf-8")
TOOLS = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(TOOLS)
sys.path.insert(0, TOOLS)   # -I drops the script folder: the probe's Win32 plumbing and the shared helpers
import ui_ime_probe as probe  # noqa: E402
import noargs_check as nc  # noqa: E402

MS_IME = "{03B5835F-F03C-411B-9CE2-AA23E1171E36}"
PLUGIN = os.path.join(REPO, "Tests", "Plugins", "ImeProbe")
PLUGIN_GUID = "stp3.test.imeprobe"
VK_A, VK_X, VK_F2 = 0x41, 0x58, 0x71
CASES = {"conv": ["suzuki", "SPACE", "ENTER"], "cont": ["suzuki", "SPACE", "san", "ENTER"],
         "cand": ["suzuki", "SPACE", "SPACE", "CANDIDATES", "ENTER"]}
CANDIDATE_TOLERANCE = 8   # px: the candidate window's top-left against the caret's bottom-left (spike S2a: -1 / -1 with the fix)
# field -> (element name, the element it is under or None, the bridge steps that show it, the case list it runs)
FIELDS = {
    "chara-search": ("chara-search", None, [{"cmd": "ui.panel", "panel": "add.chara", "op": "show"}]),
    "items-search": ("items-search", None, [{"cmd": "ui.panel", "panel": "add.item", "op": "show"}]),
    # the rename starts as a user starts it: a click on the new folder's row in the tree, then F2 (rename) or the row's
    # right-click menu > Rename (rename-menu)
    "rename": ("workspace-rename", None, [{"cmd": "ui.panel", "panel": "workspace", "op": "show"}, {"cmd": "ui.action", "action": "add.folder"}]),
    "rename-menu": ("workspace-rename", None, [{"cmd": "ui.panel", "panel": "workspace", "op": "show"}, {"cmd": "ui.action", "action": "add.folder"}]),
    "inspector-name": ("text", "panel-inspector", [{"cmd": "ui.action", "action": "add.folder"},
                                                   {"cmd": "ui.panel", "panel": "inspector", "op": "show"}]),
    "save-as-name": ("save-as-name", None, [{"cmd": "ui.action", "action": "file.saveAs"}]),
    "settings-hs2Root": ("setting-control-hs2Root", None, [{"cmd": "ui.action", "action": "file.options"}]),
}


def bridge(port, req, timeout=60):
    data = json.dumps(req).encode("utf-8")
    r = urllib.request.Request(f"http://127.0.0.1:{port}/", data=data, headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(r, timeout=timeout) as resp:
        return json.loads(resp.read().decode("utf-8"))


class Stretch(probe.Stretch):
    """The probe's input stretch without its idle wait and lock file (the caller holds the input lock)."""

    def __enter__(self):
        self.prev_fg = probe.foreground()
        self.prev_fg_ime = probe.ime_open_of(self.prev_fg)
        p = wt.POINT()
        probe.user32.GetCursorPos(ctypes.byref(p))
        self.prev_cursor = (p.x, p.y)
        if not probe.set_foreground(self.hwnd):
            self.__exit__(None, None, None)
            raise RuntimeError("could not bring the player window to the foreground")
        return self

    def __exit__(self, *exc):
        if self.prev_cursor:
            probe.user32.SetCursorPos(*self.prev_cursor)
        if self.prev_fg and self.prev_fg != self.hwnd:
            probe.set_foreground(self.prev_fg)
        self.after_fg_ime = probe.ime_open_of(self.prev_fg)
        self.restored_fg_ime = None
        if self.prev_fg_ime is not None and self.after_fg_ime is not None and self.after_fg_ime != self.prev_fg_ime:
            # switching the player's text service back can change the IME open state the user's window shows: set it back
            ime = probe.imm32.ImmGetDefaultIMEWnd(self.prev_fg)
            if ime:
                probe.user32.SendMessageW(ime, 0x283, 0x6, self.prev_fg_ime)   # WM_IME_CONTROL, IMC_SETOPENSTATUS
                time.sleep(0.2)
                self.restored_fg_ime = probe.ime_open_of(self.prev_fg)
        return False


def field_state(port):
    return bridge(port, {"cmd": "imeprobe.field"})


def composing(port):
    return bool(field_state(port).get("comp"))


def click_at(st, hwnd, scale, wb, fx=0.5, right=False):
    ox, oy = probe.client_origin(hwnd)
    x, y = round(ox + (wb[0] + wb[2] * fx) * scale), round(oy + (wb[1] + wb[3] / 2) * scale)
    st.check()
    probe.user32.SetCursorPos(x, y)
    time.sleep(0.12)
    if right:
        for flag in (0x8, 0x10):   # MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP
            i = probe.INPUT(type=0)
            i.u.mi = probe.MOUSEINPUT(0, 0, 0, flag, 0, 0)
            probe.send([i])
            time.sleep(0.05)
    else:
        probe.mouse_button(True); time.sleep(0.05); probe.mouse_button(False)
    time.sleep(0.35)


def clear_field(st):
    probe.tap(st, VK_A, mods=(probe.VK_CONTROL,), gap=0.1)
    probe.tap(st, probe.VK_BACK, gap=0.15)


def ime_on(st, port):
    """Hankaku/Zenkaku until a typed 'a' composes (the test composition is cancelled with Esc)."""
    for k in range(3):
        probe.tap(st, VK_A, gap=0.35)
        if composing(port):
            for _ in range(2):
                if composing(port):
                    probe.tap(st, probe.VK_ESCAPE, gap=0.3)
            return {"on": True, "presses": k}
        probe.tap(st, probe.VK_BACK, gap=0.12)   # the 'a' went in as plain text
        probe.hankaku(st)
    return {"on": False}


def ime_off(st, port):
    """Hankaku/Zenkaku until a typed 'a' does not compose (then the 'a' is removed)."""
    for k in range(3):
        probe.hankaku(st)
        probe.tap(st, VK_A, gap=0.35)
        if composing(port):
            probe.tap(st, probe.VK_ESCAPE, gap=0.3)
            continue
        probe.tap(st, probe.VK_BACK, gap=0.12)
        return {"off": True, "presses": k + 1}
    return {"off": False}


def snap(port, label):
    """The focused field: its shown text (a delayed field's value changes only on Enter or blur), caret and composition."""
    f = field_state(port)
    return {"at": label, "text": f.get("shown"), "value": f.get("value"), "cursor": f.get("cursor"), "select": f.get("select"), "comp": f.get("comp")}


def candidate_window(st, hwnd, port, scale, key, before, lines, results):
    """The IME's candidate list (a second Space): its window's top-left against the caret's bottom-left, physical px (the app's
    candidate-window fix puts it there; a new top-level window of any process, the one nearest the caret)."""
    time.sleep(0.4)
    f = field_state(port)
    cp = f.get("caretPanel")
    if not cp:
        lines.append(f"INFO   {key} candidate window: no caret position from the probe")
        return
    ox, oy = probe.client_origin(hwnd)
    caret = (round(ox + cp[0] * scale), round(oy + cp[1] * scale))
    new = [w for w in probe.top_windows() if w["hwnd"] not in before and w["hwnd"] != hwnd
           and w["rect"][2] - w["rect"][0] > 40 and w["rect"][3] - w["rect"][1] > 40]
    if not new:
        lines.append(f"INFO   {key} candidate window: none found among the new top-level windows (caret {caret}; not measured)")
        return
    w = min(new, key=lambda w: abs(w["rect"][0] - caret[0]) + abs(w["rect"][1] - caret[1]))
    dx, dy = w["rect"][0] - caret[0], w["rect"][1] - caret[1]
    ok = abs(dx) <= CANDIDATE_TOLERANCE and abs(dy) <= CANDIDATE_TOLERANCE
    results.append(ok)
    lines.append(("PASS " if ok else "FAIL ") + f"{key} [cand]: the candidate window sits at the caret: dx {dx}, dy {dy} px "
                 f"(<= {CANDIDATE_TOLERANCE}; window class {w['class']}, pid {'player' if w['pid'] == probe.window_pid(hwnd) else w['pid']})")


def locate(port, name, under):
    req = {"cmd": "imeprobe.find", "name": name, "scroll": True}
    if under:
        req["under"] = under
    bridge(port, req)
    time.sleep(0.4)
    req["scroll"] = False
    return bridge(port, req)


def one_field(st, hwnd, port, scale, key, case, lines, results):
    name, under, steps = FIELDS[key]
    for s in steps:
        r = bridge(port, s)
        if r.get("ok") is False:
            lines.append(f"INFO   {key}: {s} -> {r.get('error')}")
    time.sleep(0.5)
    if key in ("rename", "rename-menu"):
        row = bridge(port, {"cmd": "imeprobe.find", "cls": "hs-tree-row--selected", "under": "workspace-tree"})
        if not row.get("found"):
            lines.append(f"SKIP {key} [{case}]: no selected row in the tree ({row})")
            return
        click_at(st, hwnd, scale, row["worldBound"], 0.5)
        how = "F2 on the folder's row"
        if key == "rename":
            probe.tap(st, VK_F2, gap=0.6)
        else:
            how = "the row's right-click menu > Rename"
            click_at(st, hwnd, scale, row["worldBound"], 0.5, right=True)
            item = bridge(port, {"cmd": "imeprobe.find", "name": "menu-item-ctx-rename"})
            if not item.get("found"):
                lines.append(f"SKIP {key} [{case}]: the context menu has no Rename item ({item})")
                probe.tap(st, probe.VK_ESCAPE, gap=0.3)
                return
            click_at(st, hwnd, scale, item["worldBound"], 0.3)
            time.sleep(0.3)
        f0 = field_state(port)
        if f0.get("field") != name:
            lines.append(f"FAIL {key} [{case}]: {how} did not put the keyboard focus into the rename field "
                         f"(focus {f0.get('focus')}; field shown {locate(port, name, under).get('found')})")
            results.append(False)
            probe.tap(st, probe.VK_ESCAPE, gap=0.3)
            return
    loc = locate(port, name, under)
    if not loc.get("found") or not loc.get("enabled") or loc["worldBound"][2] < 20:
        lines.append(f"SKIP {key} [{case}]: the field is not shown ({loc})")
        return
    if field_state(port).get("field") != name:
        # a click puts the caret into the field as a user does (the rename field has the focus from F2 already)
        click_at(st, hwnd, scale, loc["worldBound"], 0.6)
    f0 = field_state(port)
    if f0.get("field") != name:
        lines.append(f"FAIL {key} [{case}]: the click did not focus it (focus {f0.get('focus')}, field {f0.get('field')})")
        results.append(False)
        return
    try:
        clear_field(st)
        on = ime_on(st, port)
        if not on.get("on"):
            lines.append(f"SKIP {key} [{case}]: the IME could not be switched on ({on})")
            return
        if field_state(port).get("value"):
            clear_field(st)
        log0 = bridge(port, {"cmd": "imeprobe.clear"})["next"]
        trail = []
        for tok in CASES[case]:
            if tok == "CANDIDATES":
                candidate_window(st, hwnd, port, scale, key, before, lines, results)
                continue
            if tok == "SPACE" and case == "cand" and trail and trail[-1]["at"] == "SPACE":
                before = {w["hwnd"] for w in probe.top_windows()}
            if tok == "SPACE":
                probe.tap(st, probe.VK_SPACE, gap=0.6)
            elif tok == "ENTER":
                probe.tap(st, probe.VK_RETURN, gap=0.6)
            else:
                for ch in tok:
                    probe.tap(st, ord(ch.upper()), gap=0.12)
                time.sleep(0.3)
            trail.append(snap(port, tok))
        committed = trail[-1]["text"] or ""
        off = ime_off(st, port)
        probe.tap(st, VK_X, gap=0.4)
        trail.append(snap(port, "x"))
        value = trail[-1]["text"]
        if not under:
            # the bridge's own view of the field: ui.query's value (a delayed field's value is its last commit)
            q = bridge(port, {"cmd": "ui.query", "name": "#" + name})
            els = [e for e in (q.get("elements") or []) if e.get("name") == name]
            if els and els[0].get("value") != trail[-1]["value"]:
                lines.append(f"WARN {key}: ui.query value {els[0].get('value')!r} differs from the field's {trail[-1]['value']!r}")
        ok = isinstance(value, str) and value == committed + "x" and len(committed) > 0 and not committed.isascii()
        results.append(ok)
        lines.append(("PASS " if ok else "FAIL ") + f"{key} [{case}]: committed {committed!r}, IME off, 'x' -> {value!r} "
                     f"(caret after the commit {trail[-2]['cursor']}/{trail[-2]['select']}; IME off {off.get('off')})")
        for s in trail:
            lines.append(f"INFO   {key} after {s['at']}: text {s['text']!r} value {s['value']!r} caret {s['cursor']}/{s['select']} composition {s['comp']!r}")
        if not ok:
            for e in bridge(port, {"cmd": "imeprobe.log", "since": log0}).get("entries", []):
                if e["kind"] in ("key", "ime", "change"):
                    extra = {k: e[k] for k in ("key", "ch", "evComp", "old", "new") if k in e}
                    lines.append(f"INFO     #{e['i']} f{e['frame']} {e['kind']:6s} value {e.get('value')!r} caret {e.get('cursor')} "
                                 f"composition {e.get('comp')!r} {json.dumps(extra, ensure_ascii=False)}")
    finally:
        # leave the field empty and closed (Esc cancels a rename or a dialog; the IME is off here unless switching it off failed)
        clear_field(st)
        probe.tap(st, probe.VK_ESCAPE, gap=0.3)
        if bridge(port, {"cmd": "ui.state"}).get("modal"):
            probe.tap(st, probe.VK_ESCAPE, gap=0.3)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--exe", default=os.path.join(REPO, "Builds", "App", "HS2Next.exe"))
    ap.add_argument("--port", type=int, default=17606)
    ap.add_argument("--out", required=True)
    ap.add_argument("--ime", default="other,start", help="other (the other Japanese text service), start (the process's one at start; last, so "
                    "the process ends as it began), ms, or a CLSID")
    ap.add_argument("--fields", default="all")
    ap.add_argument("--cases", default="conv,cont,cand", help="cont and cand run on the first field only")
    ap.add_argument("--w", type=int, default=1600)
    ap.add_argument("--h", type=int, default=900)
    ap.add_argument("--ready-timeout", type=float, default=300)
    ap.add_argument("--gpu-lock")
    ap.add_argument("--input-lock")
    a = ap.parse_args()
    out = os.path.abspath(a.out)
    os.makedirs(out, exist_ok=True)
    fields = list(FIELDS) if a.fields == "all" else a.fields.split(",")
    lines, results, locks = [], [], []
    proc = None
    t0 = time.time()
    code = None
    try:
        if nc.game_running():
            lines.append("SKIP the game runs (game gate)")
            code = 3
            return code
        if nc.port_busy(a.port):
            lines.append(f"ERROR port {a.port} is busy")
            code = 2
            return code
        for lk in (a.gpu_lock, a.input_lock):
            if lk:
                fd = os.open(lk, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
                os.write(fd, f"ime_caret_check PID {os.getpid()} start {time.strftime('%Y-%m-%d %H:%M:%S')}\n".encode())
                os.close(fd)
                locks.append(lk)
        plugins = os.path.join(out, "plugins")
        os.makedirs(plugins, exist_ok=True)
        r = subprocess.run(["pwsh", "-NoProfile", "-File", os.path.join(TOOLS, "plugins.ps1"), "-Build", PLUGIN, "-Stage", plugins, "-Only", PLUGIN_GUID],
                           capture_output=True, text=True)
        if r.returncode != 0:
            lines.append("ERROR plugins.ps1: " + (r.stdout + r.stderr)[-600:])
            code = 2
            return code
        settings = os.path.join(out, "settings.json")
        shutil.copy2(os.path.join(REPO, "Config", "settings.local.json"), settings)
        env = dict(os.environ)
        env.pop("HS2NEXT_NO_PLUGINS", None)
        env["HS2NEXT_PRIVATE_OFF"] = "1"
        env["HS2NEXT_DATA_ROOT"] = os.path.join(out, "root")
        args = [a.exe, "-ui", "-bridgePort", str(a.port), "-bridgeAllowQuit", "-screen-fullscreen", "0",
                "-screen-width", str(a.w), "-screen-height", str(a.h), "-settings", settings, "-cacheDir", os.path.join(REPO, "Cache"),
                "-userData", os.path.join(out, "userdata"), "-bridgeOutputDir", os.path.join(out, "bridge_out"),
                "-pluginsDir", plugins, "-logFile", os.path.join(out, "player.log")]
        proc = subprocess.Popen(args, env=env, creationflags=0x00000200)
        lines.append(f"INFO player pid {proc.pid}: {os.path.basename(a.exe)} {' '.join(args[1:12])} ...")
        while True:
            if proc.poll() is not None:
                lines.append(f"ERROR the player exited {proc.returncode} before ready")
                code = 2
                return code
            try:
                if bridge(a.port, {"cmd": "ping"}, timeout=5).get("appReady"):
                    break
            except Exception:
                pass
            if time.time() - t0 > a.ready_timeout:
                lines.append("ERROR not ready in time")
                code = 2
                return code
            time.sleep(1.0)
        lines.append(f"INFO ready after {time.time() - t0:.1f} s")
        time.sleep(2.0)
        hwnd = None
        for _ in range(20):
            hwnd = probe.find_player(proc.pid)
            if hwnd:
                break
            time.sleep(0.5)
        if not hwnd:
            lines.append("ERROR no player window")
            code = 2
            return code
        st0 = bridge(a.port, {"cmd": "ui.state"})
        scale = st0.get("panelScale") or 1.0
        tsf0 = bridge(a.port, {"cmd": "imeprobe.tsf", "op": "active"})["active"]
        profs = bridge(a.port, {"cmd": "imeprobe.tsf", "op": "list"})["profiles"]
        jp = [p for p in profs if p["type"] == 1 and p["langid"] == 0x411]
        other = next((p["clsid"] for p in jp if p["clsid"] != tsf0["clsid"]), None)
        lines.append(f"INFO window {probe.rect(hwnd)}, panel scale {scale}, screen {st0.get('screen')}, keyboard {st0.get('keyboard')}; "
                     f"text service at start {tsf0['clsid']}; Japanese text services {[p['clsid'] for p in jp]}")
        with Stretch(hwnd, "ime caret") as st:
            try:
                for ime in a.ime.split(","):
                    clsid = {"ms": MS_IME, "start": tsf0["clsid"], "other": other}.get(ime, ime)
                    if not clsid:
                        lines.append(f"SKIP no text service for '{ime}'")
                        continue
                    act = bridge(a.port, {"cmd": "imeprobe.tsf", "op": "activate", "clsid": clsid})
                    label = "Microsoft IME" if clsid.upper() == MS_IME else clsid
                    lines.append(f"INFO --- text service {ime} = {label} (activated for the player's process: hr {act.get('hr')})")
                    time.sleep(0.5)
                    for k, key in enumerate(fields):
                        for case in a.cases.split(","):
                            if case != "conv" and k > 0:
                                continue
                            one_field(st, hwnd, a.port, scale, key, case, lines, results)
            finally:
                try:
                    act = bridge(a.port, {"cmd": "imeprobe.tsf", "op": "activate", "clsid": tsf0["clsid"]})
                    lines.append(f"INFO the start text service is active again in the player: {act['active']['clsid']}")
                except Exception as e:
                    lines.append(f"WARN restoring the text service: {e}")
        lines.append(f"INFO the IME open state of the window in front before the check {st.prev_fg_ime}, after {st.after_fg_ime}"
                     + (f", set back to {st.restored_fg_ime}" if st.restored_fg_ime is not None else ""))
    except Exception as e:
        lines.append(f"ERROR {type(e).__name__}: {e}")
        code = 2
    finally:
        if proc is not None and proc.poll() is None:
            try:
                bridge(a.port, {"cmd": "app.quit"}, timeout=10)
            except Exception:
                pass
            try:
                proc.wait(30)
            except subprocess.TimeoutExpired:
                proc.kill()
                lines.append(f"WARN the player did not quit in 30 s: stopped by PID {proc.pid}")
            lines.append(f"INFO player exit {proc.returncode}")
        for lk in locks:
            try:
                os.remove(lk)
            except OSError:
                pass
        fails = sum(1 for r in results if not r)
        summary = f"SUMMARY: {len(results) - fails} PASS, {fails} FAIL ({time.time() - t0:.0f} s)"
        text = "\n".join([summary] + lines) + "\n"
        print(text)
        with open(os.path.join(out, "report.txt"), "w", encoding="utf-8") as f:
            f.write(text)
    if code is not None:
        return code
    if not results:
        return 3
    return 1 if any(not r for r in results) else 0


if __name__ == "__main__":
    sys.exit(main())
