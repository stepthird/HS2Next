"""
effects_player_check - the screen effects in the app player (U13;
Phase 3.3 package B).

Starts the app player (as Tools/map_player_check.py) or, with --url, talks to a running bridge (e.g. the Editor's
`Tools/unity_batch.ps1 -Method BridgeServe.Run -NoQuit`, to record the Editor reference), and checks through the bridge:
  - effects.state: the renderer runs post-processing (URP PostProcessData present), the effect profile is the shipped
    template (Resources/HS2NextEffects: post-processing variants kept, U5), the base grade is read from the HS2 install (E7),
    the strip is baked (its hash), the SSAO feature's source;
  - on map 10 from a fixed camera, captures with the effects one at a time against an "off" capture (sliders 0, no LUT,
    bloom / vignette / AO / probe off; HS2's base grade stays): grading (defaults + Filter row 10), bloom 10, vignette 0.6,
    AO 1 / 4 (when the renderer has SSAO), probe row 5 x 2: each changes the image (mean |d| > 0.5 / 255) with the expected
    sign (bloom brighter, vignette and AO darker);
  - with --ref <stats.json of an Editor run>: the same strip hashes (the CPU bake is deterministic) and, per effect, the same
    sign and a mean difference within the URP run-to-run noise (8 / 255) of the Editor's;
  - Phase 5.R (A3 / SR5): fog (map 10, the block's defaults: brighter, realized.fog.drawn), sun
    shafts (sky pattern 0, caster none, a camera looking up at the sky: brighter, realized.sunShafts.drawn) and SSR (render.ssr on +
    the scene flag: realized.ssr.drawn, the image changes) against their own "off" capture; no magenta pixels in any of them (a
    missing shader variant); with --ref the same sign and mean within 8 / 255 of the Editor's;
  - the player log: no shader errors; frame timing of effects.state (gpuFrameMs when the player measures it).
The player runs windowed (minimised) by default: in a -batchmode player (--batchmode) the run-time camera reflection probe
shows no effect (no frame of its own: URP's probe atlas never takes its cubemap); the probe case is then reported only.
  python -I Tools/effects_player_check.py [--exe Builds/App/HS2Next.exe | --url http://127.0.0.1:17322/] [--out Spike/out/player/effects]
         [--port 17398] [--ref Spike/out/effects/effects_editor_stats.json] [--timeout-min 15]
Report <out>/effects_player_report.txt ("SUMMARY: N PASS, M FAIL"), stats <out>/effects_stats.json; exit 0 pass, 1 fail, 2 setup error.
Content: map 10 only (no characters); the captures are map images.
"""
import argparse, json, os, re, shutil, subprocess, sys, time

sys.stdout.reconfigure(encoding="utf-8")
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
sys.path.insert(0, HERE)
from hs2n_bridge import Bridge  # noqa: E402

CREATE_NO_WINDOW = 0x08000000
CAMERA = {"cmd": "camera.set", "target": [0, 10, 0], "rot": [10, 180, 0], "dist": 45, "fov": 40}
OFF = {"colorGrading.saturation": 0, "colorGrading.brightness": 0, "colorGrading.contrast": 0, "colorGrading.lut": "illusion.hs2#0",
       "colorGrading.blend": 0, "bloom.enabled": False, "vignette.enabled": False, "ambientOcclusion.enabled": False,
       "reflectionProbe.enabled": False}
EFFECTS = [
    ("grading", {"colorGrading.saturation": 15, "colorGrading.brightness": -20, "colorGrading.contrast": 40, "colorGrading.lut": "illusion.hs2#10",
                 "colorGrading.blend": 1}, 0),
    ("bloom", {"bloom.enabled": True, "bloom.intensity": 10}, +1),
    ("vignette", {"vignette.enabled": True, "vignette.intensity": 0.6}, -1),
    ("ao", {"ambientOcclusion.enabled": True, "ambientOcclusion.intensity": 1, "ambientOcclusion.thickness": 4}, -1),
    ("probe", {"reflectionProbe.enabled": True, "reflectionProbe.cubemap": "illusion.hs2#5", "reflectionProbe.intensity": 2}, 0),
]


# Phase 5.R cases: (name, the case's effects.set on top of OFF, the reference's extra set, camera, expected sign, the realized
# block whose "drawn" must be true, app.options before the case or None)
CAMERA_UP = {"cmd": "camera.set", "target": [0, 22.2, 5.4], "rot": [-35, 180, 0], "dist": 30, "fov": 60}   # at (0, 5, 30) looking up
SKY0 = {"sky.enabled": True, "sky.pattern": "illusion.hs2#0"}
EFFECTS_5R = [
    ("fog", {"fog.enabled": True}, {}, CAMERA, +1, "fog", None),
    ("shafts", dict(SKY0, **{"sunShafts.enabled": True, "sunShafts.caster": None}), SKY0, CAMERA_UP, +1, "sunShafts", None),
    ("ssr", {"ssr.enabled": True}, {"ssr.enabled": False}, CAMERA, 0, "ssr", {"ssr": "on"}),
]


def magenta(px):
    """The share of pure-magenta pixels (the engine's missing-shader colour)."""
    return sum(1 for p in px if p[0] > 200 and p[2] > 200 and p[1] < 60) / max(1, len(px))


def png_pixels(path):
    from PIL import Image
    im = Image.open(path).convert("RGB")
    return im.size, list(im.getdata())


def diff(a, b):
    """mean signed luma difference and mean |d| (0..255) of two equal-size RGB pixel lists."""
    s = ad = 0.0
    for p, q in zip(a, b):
        la = 0.2126 * p[0] + 0.7152 * p[1] + 0.0722 * p[2]
        lb = 0.2126 * q[0] + 0.7152 * q[1] + 0.0722 * q[2]
        s += la - lb
        ad += (abs(p[0] - q[0]) + abs(p[1] - q[1]) + abs(p[2] - q[2])) / 3.0
    n = max(1, len(a))
    return s / n, ad / n


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--exe", default=os.path.join(REPO, "Builds", "App", "HS2Next.exe"))
    ap.add_argument("--url")
    ap.add_argument("--out", default=os.path.join(REPO, "Spike", "out", "player", "effects"))
    ap.add_argument("--port", type=int, default=17398)
    ap.add_argument("--ref")
    ap.add_argument("--timeout-min", type=float, default=15)
    ap.add_argument("--batchmode", action="store_true", help="start the player with -batchmode (the camera probe then shows no effect: see the docstring)")
    a = ap.parse_args()
    a.exe = os.path.abspath(a.exe)
    a.out = os.path.abspath(a.out)
    os.makedirs(a.out, exist_ok=True)
    lines, npass, nfail = [], 0, 0

    def add(ok, what, detail=""):
        nonlocal npass, nfail
        npass += bool(ok); nfail += not ok
        lines.append("   %s %s%s" % ("PASS" if ok else "FAIL", what, (" - " + detail) if detail else ""))
        print(lines[-1], flush=True)

    def info(s):
        lines.append("   " + s); print(lines[-1], flush=True)

    def finish(code):
        lines.insert(0, "SUMMARY: %d PASS, %d FAIL" % (npass, nfail))
        with open(os.path.join(a.out, "effects_player_report.txt"), "w", encoding="utf-8", newline="\n") as f:
            f.write("\n".join(lines) + "\n")
        return code

    proc, logf = None, None
    target = a.url or a.exe
    lines.append("effects_player_check %s; target %s" % (time.strftime("%Y-%m-%d %H:%M"), target))
    t0 = time.time()
    if not a.url:
        if not os.path.isfile(a.exe):
            lines.append("setup: no player " + a.exe); return finish(2)
        lines.append("player built %s" % time.strftime("%Y-%m-%d %H:%M", time.localtime(os.path.getmtime(a.exe))))
        settings = os.path.join(REPO, "Config", "settings.local.json")
        ud, bo, logf = os.path.join(a.out, "userdata"), os.path.join(a.out, "bridge_out"), os.path.join(a.out, "player.log")
        # windowed by default (a small, minimised, unfocused window; AppCore sets runInBackground): a -batchmode player renders no
        # frame of its own and URP's reflection-probe atlas never takes the run-time camera probe's cubemap there (package S);
        # the other effects behave the same in both modes
        mode = ["-batchmode"] if a.batchmode else ["-screen-fullscreen", "0", "-screen-width", "640", "-screen-height", "360"]
        args = [a.exe] + mode + ["-bridgePort", str(a.port), "-bridgeAllowQuit", "-settings", settings, "-cacheDir", os.path.join(REPO, "Cache"),
                "-userData", ud, "-bridgeOutputDir", bo, "-logFile", logf]
        si = None
        if not a.batchmode:
            si = subprocess.STARTUPINFO()
            si.dwFlags |= subprocess.STARTF_USESHOWWINDOW
            si.wShowWindow = 7   # SW_SHOWMINNOACTIVE: minimised, not activated
        proc = subprocess.Popen(args, cwd=os.path.dirname(a.exe), stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                                creationflags=CREATE_NO_WINDOW, startupinfo=si)
        lines.append("player mode: " + ("-batchmode" if a.batchmode else "windowed (minimised)"))
        url = "http://127.0.0.1:%d/" % a.port
    else:
        url = a.url
    br = Bridge(url=url)
    deadline = t0 + a.timeout_min * 60
    stats = {"target": "player" if not a.url else "bridge " + a.url, "written": time.strftime("%Y-%m-%d %H:%M"), "effects": {}}
    try:
        while True:
            if proc is not None and proc.poll() is not None:
                lines.append("setup: the player exited with %s" % proc.returncode); return finish(2)
            try:
                if br.call({"cmd": "ping"}, timeout=5, check=False).get("appReady"): break
            except Exception:
                pass
            if time.time() > deadline:
                lines.append("setup: not ready"); return finish(2)
            time.sleep(0.5)
        info("ready %.1f s after the start" % (time.time() - t0))
        br.call({"cmd": "scene.new"})
        st = br.call({"cmd": "effects.state"})
        re_ = st.get("realized") or {}
        rend = re_.get("renderer") or {}
        cg = re_.get("colorGrading") or {}
        stats["renderer"] = rend
        stats["profile"] = cg.get("profile")
        post = bool(rend.get("postProcessing"))
        add(post, "the URP renderer runs post-processing (PostProcessData present)", json.dumps(rend))
        add(cg.get("profile") == "template", "the effect profile is the shipped template (Resources/HS2NextEffects, U5)", str(cg.get("profile")))
        add((cg.get("baseGrade") or {}).get("ok") is True, "HS2's base grade read from the install (E7)", json.dumps(cg.get("baseGrade"), ensure_ascii=False))
        stats["lutHashDefaults"] = cg.get("lutHash")
        info("strip hash (defaults) %s; SSAO source %s" % (cg.get("lutHash"), (re_.get("ambientOcclusion") or {}).get("source")))
        r = br.call({"cmd": "map.load", "no": 10, "wait": True}, timeout=600, check=False)
        add(r.get("ok") and r.get("state") == "Loaded", "map 10 loaded", "" if r.get("ok") else json.dumps(r)[:200])
        br.call(CAMERA)

        def shot(name):
            cap = br.call({"cmd": "capture", "name": "fxcheck_" + name, "width": 1280, "height": 720, "waitFrames": 3}, timeout=120, check=False)
            if not cap.get("ok"):
                add(False, "capture " + name, json.dumps(cap)[:200]); return None
            dst = os.path.join(a.out, name + ".png")
            shutil.copyfile(cap["path"], dst)
            return png_pixels(dst)

        br.call({"cmd": "effects.set", "set": OFF})
        base = shot("off")
        st = br.call({"cmd": "effects.state", "blocks": ["colorGrading"]})
        stats["lutHashOff"] = (st.get("realized") or {}).get("colorGrading", {}).get("lutHash")
        has_ao = (re_.get("ambientOcclusion") or {}).get("source") not in (None, "none")
        for name, values, sign in EFFECTS:
            if name == "ao" and not has_ao:
                info("ao: no AO pass in the renderer: skipped"); continue
            br.call({"cmd": "effects.set", "set": values})
            img = shot(name)
            if name == "grading":
                stats["lutHashGrading"] = (br.call({"cmd": "effects.state", "blocks": ["colorGrading"]}).get("realized") or {}).get("colorGrading", {}).get("lutHash")
            t = br.call({"cmd": "effects.state", "blocks": ["bloom"], "timing": True}).get("timing") or {}
            br.call({"cmd": "effects.set", "set": OFF})
            if base is None or img is None or img[0] != base[0]:
                add(False, name + ": capture size", ""); continue
            sd, ad = diff(img[1], base[1])
            stats["effects"][name] = {"meanSigned": round(sd, 3), "meanAbs": round(ad, 3), "gpuFrameMs": t.get("gpuFrameMs"), "cpuFrameMs": t.get("cpuFrameMs")}
            ok = ad > 0.5 and (sign == 0 or sd * sign > 0)
            if name == "probe" and a.batchmode and not ok:
                info("probe: no effect in a -batchmode player (known: URP's probe atlas is not fed without the player's own frames); run windowed")
                continue
            add(ok, "%s changes the image (mean |d| %.2f, signed %+.2f / 255%s)" % (name, ad, sd, "" if sign == 0 else ", expected " + ("brighter" if sign > 0 else "darker")))
        # Phase 5.R: fog, sun shafts, SSR (each against its own reference capture)
        ropts = (br.call({"cmd": "app.options"}, check=False) or {}).get("render") or {}
        for name, values, refset, cam, sign, blk, opts in EFFECTS_5R:
            if opts and not all(k in ropts for k in opts):
                add(False, "%s: app.options render lacks %s" % (name, ", ".join(k for k in opts if k not in ropts)), json.dumps(ropts)[:200]); continue
            br.call(cam)
            br.call({"cmd": "effects.set", "set": dict(OFF, **refset)})
            b5 = shot(name + "_off")
            if opts:
                br.call({"cmd": "app.options", "render": opts})
            br.call({"cmd": "effects.set", "set": values})
            img = shot(name)
            rz = (br.call({"cmd": "effects.state", "blocks": [blk], "timing": True}) or {})
            real = (rz.get("realized") or {}).get(blk) or {}
            t = rz.get("timing") or {}
            if opts:
                br.call({"cmd": "app.options", "render": {k: ropts.get(k) for k in opts}})
            br.call({"cmd": "effects.set", "set": dict(OFF, **{k: False for k in values if k.endswith(".enabled")})})
            br.call(CAMERA)
            add(real.get("drawn") is True, "%s: realized.%s.drawn" % (name, blk), json.dumps(real)[:240])
            if b5 is None or img is None or img[0] != b5[0]:
                add(False, name + ": capture size", ""); continue
            sd, ad = diff(img[1], b5[1])
            mg = magenta(img[1])
            stats["effects"][name] = {"meanSigned": round(sd, 3), "meanAbs": round(ad, 3), "magenta": round(mg, 5),
                                      "gpuFrameMs": t.get("gpuFrameMs"), "cpuFrameMs": t.get("cpuFrameMs")}
            ok = ad > (0.1 if name == "ssr" else 0.5) and (sign == 0 or sd * sign > 0)
            add(ok, "%s changes the image (mean |d| %.2f, signed %+.2f / 255%s)" % (name, ad, sd, "" if sign == 0 else ", expected " + ("brighter" if sign > 0 else "darker")))
            add(mg <= magenta(b5[1]) + 1e-4, "%s: no magenta pixels (missing variant)" % name, "%.5f vs %.5f" % (mg, magenta(b5[1])))
        if a.ref and os.path.isfile(a.ref):
            ref = json.load(open(a.ref, encoding="utf-8"))
            info("reference: %s (%s)" % (a.ref, ref.get("written")))
            for k in ("lutHashDefaults", "lutHashOff", "lutHashGrading"):
                if ref.get(k) and stats.get(k):
                    add(ref[k] == stats[k], "strip hash %s equals the reference's (deterministic CPU bake)" % k, "%s vs %s" % (stats[k], ref[k]))
            for name, e in stats["effects"].items():
                r2 = (ref.get("effects") or {}).get(name)
                if not r2: continue
                same = (e["meanSigned"] >= 0) == (r2["meanSigned"] >= 0) or abs(e["meanSigned"]) < 0.5
                add(same and abs(e["meanAbs"] - r2["meanAbs"]) <= 8, "%s as in the reference (mean |d| %.2f vs %.2f, signed %+.2f vs %+.2f)" % (name, e["meanAbs"], r2["meanAbs"], e["meanSigned"], r2["meanSigned"]))
        elif a.ref:
            info("reference %s not found: the comparison is skipped" % a.ref)
        with open(os.path.join(a.out, "effects_stats.json"), "w", encoding="utf-8", newline="\n") as f:
            json.dump(stats, f, indent=1)
        br.call({"cmd": "scene.new"}, check=False)
        if proc is not None:
            br.call({"cmd": "app.quit"}, check=False)
            try: proc.wait(60)
            except Exception: proc.kill()
    finally:
        if proc is not None and proc.poll() is None:
            proc.kill()
    if logf and os.path.isfile(logf):
        txt = open(logf, encoding="utf-8", errors="replace").read()
        bad = [l for l in txt.splitlines() if re.search(r"shader is not supported|Shader '.*' is not supported|Could not find shader|ERROR: Shader|Shader error", l)]
        add(not bad, "player log: no shader errors", "; ".join(bad[:5]))
    return finish(0 if nfail == 0 else 1)


if __name__ == "__main__":
    sys.exit(main())
