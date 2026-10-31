"""
capture_player_check - screenshots in the app player (
R-M1; Phase 3.6 package C).

Starts the app player with the Studio UI (-ui, windowed 1600 x 900, minimised and not activated; the option
capture.maxPixels raised to 33,177,600 on the command line so that the 8K case may render) and checks through its bridge:
  F1  F11 through `ui.input` (a key event synthesised in the UI panel, no OS input) writes <UserData>/cap/<17 digits>.png of
      the fit size (the viewport's aspect inside the window, x 1), read from the PNG header; HS2's UserData/cap untouched
  F2  Shift+F11 opens the Screenshot dialog (ui.state modal "screenshot"); Enter (its default button, Take) writes one more
      file; Esc closes it
  M   R-M1: with two characters (the SFW targets of Tools/targets.json) on map 10, one screenshot at a time of 4K
      (3840 x 2160), 16.6 Mpix (5430 x 3054) and 8K (7680 x 4320): the GPU memory baseline and peak from nvidia-smi
      (memory.used of the whole GPU, sampled every 50 ms from 1 s before to 3 s after the reply) and Unity's Gfx Used
      Memory before / after the render (the reply's memory member; -1 where the player does not provide it); the rule of
      a 16.6 Mpix peak above 7.0 GB means the default cap is lowered to 12.4 Mpix (reported as FAIL of that line)
The images are map / clothed-target screenshots; the check reads only their PNG headers (sizes), it never decodes them.
  python -I Tools/capture_player_check.py [--exe Builds/App/HS2Next.exe] [--out Spike/out/player/capture] [--port 17398]
         [--timeout-min 25] [--no-memory]
Report <out>/capture_player_report.txt ("SUMMARY: N PASS, M FAIL"), stats <out>/capture_player_stats.json; exit 0 pass,
1 fail, 2 setup error.
"""
import argparse, json, os, re, struct, subprocess, sys, threading, time

sys.stdout.reconfigure(encoding="utf-8")
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
sys.path.insert(0, HERE)
from hs2n_bridge import Bridge  # noqa: E402

CREATE_NO_WINDOW = 0x08000000
STAMP = re.compile(r"^\d{17}\.png$")
CASES = [("4K", 3840, 2160), ("16.6 Mpix", 5430, 3054), ("8K", 7680, 4320)]
PEAK_RULE_MIB = 7.0 * 1024


def png_size(path):
    with open(path, "rb") as f:
        head = f.read(24)
    if len(head) < 24 or head[:8] != b"\x89PNG\r\n\x1a\n" or head[12:16] != b"IHDR":
        return None
    return struct.unpack(">II", head[16:24])


def fit(vw, vh, ww, wh, scale=1.0):
    if vw <= 0 or vh <= 0:
        vw, vh = ww, wh
    a = vw / vh
    if ww / wh > a:
        h = wh; w = h * a
    else:
        w = ww; h = w / a
    return int(round(w * scale)), int(round(h * scale))


class GpuPoller(threading.Thread):
    """nvidia-smi memory.used (MiB) every interval seconds: [(t, used)]."""

    def __init__(self, interval=0.05):
        super().__init__(daemon=True)
        self.interval, self.samples, self.stop, self.error = interval, [], False, None

    def run(self):
        try:
            p = subprocess.Popen(["nvidia-smi", "--query-gpu=memory.used", "--format=csv,noheader,nounits", "-lms", str(int(self.interval * 1000))],
                                 stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, text=True, creationflags=CREATE_NO_WINDOW)
        except Exception as e:
            self.error = str(e); return
        try:
            for line in p.stdout:
                if self.stop:
                    break
                line = line.strip().split(",")[0]
                if line.isdigit():
                    self.samples.append((time.time(), int(line)))
        finally:
            p.kill()

    def window(self, t0, t1):
        return [u for t, u in self.samples if t0 <= t <= t1]


def unity_running():
    try:
        out = subprocess.run(["tasklist", "/FI", "IMAGENAME eq Unity.exe", "/NH"], capture_output=True, text=True, creationflags=CREATE_NO_WINDOW).stdout
        return "Unity.exe" in out
    except Exception:
        return None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--exe", default=os.path.join(REPO, "Builds", "App", "HS2Next.exe"))
    ap.add_argument("--out", default=os.path.join(REPO, "Spike", "out", "player", "capture"))
    ap.add_argument("--port", type=int, default=17398)
    ap.add_argument("--timeout-min", type=float, default=25)
    ap.add_argument("--no-memory", action="store_true", help="skip R-M1 (the large renders)")
    a = ap.parse_args()
    a.exe, a.out = os.path.abspath(a.exe), os.path.abspath(a.out)
    os.makedirs(a.out, exist_ok=True)
    lines, npass, nfail = [], 0, 0
    stats = {"written": time.strftime("%Y-%m-%d %H:%M"), "cases": {}}

    def add(ok, what, detail=""):
        nonlocal npass, nfail
        npass += bool(ok); nfail += not ok
        lines.append("   %s %s%s" % ("PASS" if ok else "FAIL", what, (" - " + detail) if detail else ""))
        print(lines[-1], flush=True)

    def info(s):
        lines.append("   " + s); print(lines[-1], flush=True)

    def finish(code):
        lines.insert(0, "SUMMARY: %d PASS, %d FAIL" % (npass, nfail))
        with open(os.path.join(a.out, "capture_player_report.txt"), "w", encoding="utf-8", newline="\n") as f:
            f.write("\n".join(lines) + "\n")
        with open(os.path.join(a.out, "capture_player_stats.json"), "w", encoding="utf-8", newline="\n") as f:
            json.dump(stats, f, indent=1)
        return code

    lines.append("capture_player_check %s; player %s" % (time.strftime("%Y-%m-%d %H:%M"), a.exe))
    if not os.path.isfile(a.exe):
        lines.append("setup: no player " + a.exe); return finish(2)
    settings = os.path.join(REPO, "Config", "settings.local.json")
    try:
        hs2_root = json.load(open(settings, encoding="utf-8")).get("content", {}).get("hs2Root")
    except Exception:
        hs2_root = None
    hs2_cap = os.path.join(hs2_root, "UserData", "cap") if hs2_root else None
    hs2_cap0 = sorted(os.listdir(hs2_cap)) if hs2_cap and os.path.isdir(hs2_cap) else None
    ud, bo, logf = os.path.join(a.out, "userdata"), os.path.join(a.out, "bridge_out"), os.path.join(a.out, "player.log")
    cap = os.path.join(ud, "cap")
    if os.path.isdir(cap):
        for f in os.listdir(cap):
            try: os.remove(os.path.join(cap, f))
            except OSError: pass
    args = [a.exe, "-ui", "-screen-fullscreen", "0", "-screen-width", "1600", "-screen-height", "900", "-bridgePort", str(a.port), "-bridgeAllowQuit",
            "-settings", settings, "-cacheDir", os.path.join(REPO, "Cache"), "-userData", ud, "-bridgeOutputDir", bo, "-logFile", logf,
            "-uiOption", "capture.maxPixels=33177600"]
    si = subprocess.STARTUPINFO()
    si.dwFlags |= subprocess.STARTF_USESHOWWINDOW
    si.wShowWindow = 7   # SW_SHOWMINNOACTIVE
    t0 = time.time()
    proc = subprocess.Popen(args, cwd=os.path.dirname(a.exe), stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                            creationflags=CREATE_NO_WINDOW, startupinfo=si)
    stats["pid"] = proc.pid
    info("player pid %d (windowed 1600 x 900, minimised)" % proc.pid)
    br = Bridge(url="http://127.0.0.1:%d/" % a.port, retries=2)
    deadline = t0 + a.timeout_min * 60
    poller = None
    try:
        while True:
            if proc.poll() is not None:
                lines.append("setup: the player exited with %s" % proc.returncode); return finish(2)
            try:
                if br.call({"cmd": "ping"}, timeout=5, check=False).get("appReady"): break
            except Exception:
                pass
            if time.time() > deadline:
                lines.append("setup: not ready"); return finish(2)
            time.sleep(0.5)
        info("ready %.1f s after the start" % (time.time() - t0))
        ui = br.call({"cmd": "ui.state"}, check=False)
        add(ui.get("ok") is True, "the Studio UI runs (ui.state)", "" if ui.get("ok") else json.dumps(ui)[:200])
        br.call({"cmd": "scene.new"}, check=False)
        r = br.call({"cmd": "map.load", "no": 10, "wait": True}, timeout=600, check=False)
        add(r.get("ok") and r.get("state") == "Loaded", "map 10 loaded", "" if r.get("ok") else json.dumps(r)[:200])
        br.call({"cmd": "camera.set", "target": [0, 10, 0], "rot": [10, 180, 0], "dist": 40, "fov": 40}, check=False)

        # ---------------------------------------------------------------- F1 F11
        def new_files(before, wait_s=30):
            end = time.time() + wait_s
            while time.time() < end:
                now = set(os.listdir(cap)) if os.path.isdir(cap) else set()
                fresh = sorted(now - before)
                if fresh:
                    p = os.path.join(cap, fresh[-1])
                    s0 = -1
                    for _ in range(40):   # the asynchronous write: wait until the size settles
                        s1 = os.path.getsize(p)
                        if s1 > 0 and s1 == s0: break
                        s0 = s1; time.sleep(0.25)
                    return fresh
                time.sleep(0.25)
            return []

        st = br.call({"cmd": "camera.state"}, check=False).get("camera") or {}
        vw, vh = st.get("pixelWidth", 0), st.get("pixelHeight", 0)
        sw, sh = (st.get("screen") or [0, 0])[:2]
        want = fit(vw, vh, sw, sh)
        stats["view"] = {"viewport": [vw, vh], "screen": [sw, sh], "fit": list(want)}
        info("view %d x %d in a window of %d x %d: fit %d x %d" % (vw, vh, sw, sh, want[0], want[1]))
        before = set(os.listdir(cap)) if os.path.isdir(cap) else set()
        r = br.call({"cmd": "ui.input", "events": [{"type": "key", "key": "F11"}], "frames": 3}, check=False)
        fresh = new_files(before)
        size = png_size(os.path.join(cap, fresh[-1])) if fresh else None
        add(r.get("ok") and len(fresh) == 1 and STAMP.match(fresh[-1]) and size == want,
            "F11 (ui.input) writes <UserData>/cap/<yyyyMMddHHmmssfff>.png of the fit size",
            "%s %s (expected %d x %d); ui.input %s" % (fresh, size, want[0], want[1], json.dumps(r)[:160]))

        # ---------------------------------------------------------------- F2 Shift+F11, Enter, Esc
        r = br.call({"cmd": "ui.input", "events": [{"type": "key", "key": "F11", "mods": ["shift"]}], "frames": 5}, check=False)
        st2 = br.call({"cmd": "ui.state"}, check=False)
        modal = json.dumps(st2.get("modal") if isinstance(st2, dict) else None)
        add("screenshot" in modal, "Shift+F11 opens the Screenshot dialog", "modal " + modal[:160])
        before = set(os.listdir(cap)) if os.path.isdir(cap) else set()
        br.call({"cmd": "ui.input", "events": [{"type": "key", "key": "Return"}], "frames": 3}, check=False)
        fresh = new_files(before)
        st3 = br.call({"cmd": "ui.state"}, check=False)
        add(len(fresh) == 1 and "screenshot" in json.dumps(st3.get("modal")), "Enter (Take) writes a file and the dialog stays open", "%s" % fresh)
        br.call({"cmd": "ui.input", "events": [{"type": "key", "key": "Escape"}], "frames": 3}, check=False)
        st4 = br.call({"cmd": "ui.state"}, check=False)
        add("screenshot" not in json.dumps(st4.get("modal")), "Esc closes it", json.dumps(st4.get("modal"))[:120])

        # ---------------------------------------------------------------- M R-M1
        if not a.no_memory:
            targets = json.load(open(os.path.join(HERE, "targets.json"), encoding="utf-8"))["visual"]
            for sex in ("female", "male"):
                rel = targets[sex].split("/", 2)[-1]
                r = br.call({"cmd": "chara.load", "path": rel, "sex": sex}, timeout=600, check=False)
                add(r.get("ok") is True, "chara.load %s (%s)" % (rel, sex), "" if r.get("ok") else json.dumps(r)[:200])
            br.call({"cmd": "camera.set", "target": [0, 10, 0], "rot": [5, 180, 0], "dist": 30, "fov": 40}, check=False)
            time.sleep(2)
            poller = GpuPoller()
            poller.start()
            time.sleep(1.5)
            if poller.error or not poller.samples:
                info("nvidia-smi unavailable (%s): only Unity's counters are recorded" % poller.error)
            stats["unityEditorRunning"] = unity_running()
            info("a Unity Editor runs at the same time: %s (nvidia-smi counts the whole GPU)" % stats["unityEditorRunning"])
            for name, w, h in CASES:
                time.sleep(3)
                tb = time.time()
                base = poller.window(tb - 1.0, tb)
                r = br.call({"cmd": "screenshot", "mode": "custom", "width": w, "height": h, "frame": False, "name": "rm1_" + name.replace(" ", "_").replace(".", "")}, timeout=300, check=False)
                te = time.time()
                time.sleep(3)
                during = poller.window(tb, te + 3.0)
                after = poller.window(time.time() - 0.5, time.time())
                mem = (r.get("memory") or {}) if r.get("ok") else {}
                tm = (r.get("timing") or {}) if r.get("ok") else {}
                c = {"size": [w, h], "megapixels": w * h / 1e6, "ok": bool(r.get("ok")), "baselineMiB": min(base) if base else None,
                     "peakMiB": max(during) if during else None, "afterMiB": min(after) if after else None,
                     "gfxBeforeMiB": round(mem.get("gfxBefore", -1) / 1048576.0, 1) if mem.get("gfxBefore", -1) >= 0 else None,
                     "gfxAfterRenderMiB": round(mem.get("gfxAfterRender", -1) / 1048576.0, 1) if mem.get("gfxAfterRender", -1) >= 0 else None,
                     "timing": tm, "warnings": r.get("warnings"), "error": None if r.get("ok") else r.get("error")}
                stats["cases"][name] = c
                path = r.get("path") if r.get("ok") else None
                size = png_size(path) if path and os.path.isfile(path) else None
                add(r.get("ok") is True and size == (w, h), "R-M1 %s screenshot %d x %d written" % (name, w, h),
                    "%s; %s" % (size, (r.get("error") or "")[:160]))
                info("R-M1 %s: GPU memory baseline %s MiB, peak %s MiB (+%s), 3 s after %s MiB; Unity Gfx Used %s -> %s MiB; render %.0f ms, read-back %.0f ms, PNG %.0f ms (worker), total %.0f ms"
                     % (name, c["baselineMiB"], c["peakMiB"], (c["peakMiB"] - c["baselineMiB"]) if c["peakMiB"] and c["baselineMiB"] else "?", c["afterMiB"],
                        c["gfxBeforeMiB"], c["gfxAfterRenderMiB"], tm.get("renderMs", 0), tm.get("readbackMs", 0), tm.get("encodeMs", 0), tm.get("totalMs", 0)))
            c16 = stats["cases"].get("16.6 Mpix") or {}
            if c16.get("peakMiB") is not None:
                add(c16["peakMiB"] <= PEAK_RULE_MIB, "R-M1 rule: the 16.6 Mpix peak stays at or under 7.0 GB, the default cap 16.6 Mpix stands",
                    "peak %d MiB (%.2f GB)" % (c16["peakMiB"], c16["peakMiB"] / 1024.0))
            c8 = stats["cases"].get("8K") or {}
            if c8.get("peakMiB") is not None:
                info("8K peak %d MiB of %s MiB on the card: %s" % (c8["peakMiB"], 8188, "within the card" if c8["peakMiB"] < 8188 else "AT THE LIMIT"))
        if hs2_cap0 is not None:
            add(sorted(os.listdir(hs2_cap)) == hs2_cap0, "HS2's UserData/cap is untouched", "%d files" % len(hs2_cap0))
        br.call({"cmd": "scene.new"}, check=False)
        br.call({"cmd": "app.quit"}, check=False)
        try: proc.wait(60)
        except Exception: proc.kill()
    finally:
        if poller is not None:
            poller.stop = True
        if proc.poll() is None:
            proc.kill()
    if os.path.isfile(logf):
        txt = open(logf, encoding="utf-8", errors="replace").read()
        bad = [l for l in txt.splitlines() if re.search(r"Shader error|Could not find shader|HS2SceneImage.*not supported|capture failed|OutOfMemory|out of memory", l)]
        add(not bad, "player log: no shader / capture / memory errors", "; ".join(bad[:5]))
    return finish(0 if nfail == 0 else 1)


if __name__ == "__main__":
    sys.exit(main())
