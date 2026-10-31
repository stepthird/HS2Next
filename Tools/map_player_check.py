"""
map_player_check - the map shader families in the app player (M7; Phase 3.2 package B).

Starts Builds/App/HS2Next.exe with its bridge (as Tools/player_checks.py), loads maps through the bridge and checks that
the player resolves the map shader targets: package / built-in shaders (URP's SpeedTree7 / SpeedTree7 Billboard, Terrain
Lit, Unity's skyboxes) exist in a player only through the shader variant collection (Editor/MapShaderSet.cs), and
ShaderMap swaps a slot only when Shader.Find resolves its target. Per map: map.load, map.renderers (materials) -> every slot
whose original shader has a ShaderMap entry of the maps block (Runtime/Rendering/ShaderMap.cs) runs on that entry's target;
map.state reports no original (Built-in) shader left; a capture from the HS2 comparison's map camera
(Tools/hs2_compare.py MAP_CAMERA) is saved for viewing. The player log is scanned for "shader not supported" / magenta
fallbacks and for errors.

  python -I Tools/map_player_check.py [--exe Builds/App/HS2Next.exe] [--out Spike/out/player/maps] [--port 17398]
         [--maps illusion.hs2#17,illusion.hs2#29,...] [--timeout-min 20]
Report <out>/map_player_report.txt, last line "SUMMARY: N PASS, M FAIL"; exit 0 pass, 1 fail, 2 setup error.
"""
import argparse, json, math, os, re, shutil, subprocess, sys, time

sys.stdout.reconfigure(encoding="utf-8")
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
sys.path.insert(0, HERE)
from hs2n_bridge import Bridge  # noqa: E402

MAPS = ["illusion.hs2#17", "illusion.hs2#14", "illusion.hs2#29", "illusion.hs2#0", "illusion.hs2#35", "illusion.hs2#32",
        "hooh.map.school#1"]
MAP_CAMERA = {"pos": [0.0, 10.0 + 60.0 * math.sin(math.radians(20.0)), 60.0 * math.cos(math.radians(20.0))], "rot": [20.0, 180.0, 0.0],
              "dist": 60.0, "fov": 50.0}
CREATE_NO_WINDOW = 0x08000000


def maps_block():
    text = open(os.path.join(REPO, "Assets", "HS2Next", "Runtime", "Rendering", "ShaderMap.cs"), encoding="utf-8").read()
    a, b = text.find("---- maps (3.2 package B)"), text.find("---- end of maps (3.2 package B)")
    return dict(re.findall(r'\{\s*"([^"]+)"\s*,\s*"([^"]+)"\s*\}', text[a:b]))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--exe", default=os.path.join(REPO, "Builds", "App", "HS2Next.exe"))
    ap.add_argument("--out", default=os.path.join(REPO, "Spike", "out", "player", "maps"))
    ap.add_argument("--port", type=int, default=17398)
    ap.add_argument("--maps", default=",".join(MAPS))
    ap.add_argument("--timeout-min", type=float, default=20)
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    lines, npass, nfail = [], 0, 0

    def add(ok, what, detail=""):
        nonlocal npass, nfail
        npass += ok; nfail += not ok
        lines.append("   %s %s%s" % ("PASS" if ok else "FAIL", what, (" - " + detail) if detail else ""))
        print(lines[-1], flush=True)

    def info(s):
        lines.append("   " + s); print(lines[-1], flush=True)

    def finish(code):
        lines.append("SUMMARY: %d PASS, %d FAIL" % (npass, nfail))
        with open(os.path.join(a.out, "map_player_report.txt"), "w", encoding="utf-8", newline="\n") as f:
            f.write("\n".join(lines) + "\n")
        return code

    if not os.path.isfile(a.exe):
        lines.append("setup: no player " + a.exe); return finish(2)
    block = maps_block()
    settings = os.path.join(REPO, "Config", "settings.local.json")
    ud, bo, logf = os.path.join(a.out, "userdata"), os.path.join(a.out, "bridge_out"), os.path.join(a.out, "player.log")
    args = [a.exe, "-batchmode", "-bridgePort", str(a.port), "-bridgeAllowQuit", "-settings", settings, "-cacheDir", os.path.join(REPO, "Cache"),
            "-userData", ud, "-bridgeOutputDir", bo, "-logFile", logf]
    lines.append("map_player_check %s; player %s (%s)" % (time.strftime("%Y-%m-%d %H:%M"), a.exe,
                 time.strftime("%Y-%m-%d %H:%M", time.localtime(os.path.getmtime(a.exe)))))
    t0 = time.time()
    proc = subprocess.Popen(args, cwd=os.path.dirname(a.exe), stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                            creationflags=CREATE_NO_WINDOW)
    br = Bridge(url="http://127.0.0.1:%d/" % a.port)
    deadline = t0 + a.timeout_min * 60
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
        info("ready %.1f s after the start (pid %d)" % (time.time() - t0, proc.pid))
        for o in br.call({"cmd": "scene"}).get("objects") or []:
            br.call({"cmd": "delete", "key": o.get("key")}, check=False)
        for mref in [m for m in a.maps.split(",") if m]:
            lines.append("\n== " + mref)
            t1 = time.time()
            r = br.call({"cmd": "map.load", "ref": mref, "wait": True}, timeout=900, check=False)
            if not r.get("ok") or r.get("state") != "Loaded":
                add(False, "loaded", json.dumps(r, ensure_ascii=False)[:300]); continue
            add(True, "loaded in %.1f s" % (time.time() - t1))
            rs = br.call({"cmd": "map.renderers", "materials": True, "limit": 200000}, timeout=300)
            slots = mapped = 0
            wrong, seen = {}, {}
            routed = 0
            for rd in rs.get("renderers") or []:
                lmi = rd.get("lightmapIndex")
                lightmapped = isinstance(lmi, int) and 0 <= lmi < 0xFFFE
                for m in rd.get("materials") or []:
                    if not m: continue
                    orig, sh = m.get("shaderOriginal"), m.get("shader")
                    if orig not in block: continue
                    slots += 1
                    seen[(orig, sh)] = seen.get((orig, sh), 0) + 1
                    if sh == block[orig]: mapped += 1
                    elif lightmapped and sh == "Universal Render Pipeline/Lit": routed += 1   # ShaderMap.RouteLightmapped (no lightmap path)
                    else: wrong[(orig, sh)] = wrong.get((orig, sh), 0) + 1
            for (o, s), n in sorted(seen.items(), key=lambda kv: -kv[1]): info("%6d  %s -> %s" % (n, o, s))
            ok = not wrong
            add(ok, "every slot of a maps-block shader runs on its target or, lightmapped, on URP/Lit (%d slots, %d on target, %d routed)" % (slots, mapped, routed),
                "; ".join("%s -> %s x%d" % (o, s, n) for (o, s), n in list(wrong.items())[:6]))
            st = br.call({"cmd": "map.state"}, timeout=120)
            sky = (st.get("renderSettings") or {}).get("skybox") or {}
            info("skybox %s; findings %s" % (json.dumps(sky, ensure_ascii=False), ", ".join(sorted({f.get("code", "") for f in st.get("findings") or []}))))
            br.call({"cmd": "camera.state", "set": dict(MAP_CAMERA)}, check=False)
            time.sleep(0.5)
            cap = br.call({"cmd": "capture", "name": "mapcheck_" + re.sub(r"[^A-Za-z0-9_]", "_", mref), "width": 1280, "height": 720, "waitFrames": 3},
                          timeout=120, check=False)
            if cap.get("ok") and cap.get("path"):
                dst = os.path.join(a.out, re.sub(r"[^A-Za-z0-9_]", "_", mref) + ".png")
                shutil.copyfile(cap["path"], dst)
                info("capture " + dst)
            else:
                add(False, "capture", json.dumps(cap, ensure_ascii=False)[:200])
        br.call({"cmd": "app.quit"}, check=False)
        try: proc.wait(60)
        except Exception: proc.kill()
    finally:
        if proc.poll() is None:
            proc.kill()
    # the player log: shader errors
    if os.path.isfile(logf):
        txt = open(logf, encoding="utf-8", errors="replace").read()
        bad = [l for l in txt.splitlines() if re.search(r"shader is not supported|Shader '.*' is not supported|Could not find shader|ERROR: Shader|Shader error", l)]
        add(not bad, "player log: no shader errors", "; ".join(bad[:5]))
    return finish(0 if nfail == 0 else 1)


if __name__ == "__main__":
    sys.exit(main())
