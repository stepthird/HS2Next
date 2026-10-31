"""
perf_player_check.py - player measurements of the mod-wide loading work in the app player over the bridge: part-change
frame times, the first redirected load, the item list filter, a list scroll, the hair part switch, the first draw-type
switch, the first frame of a new character, the LUT bake and frame timing, GPU memory per map, and the cost of
Resources.UnloadUnusedAssets with 0 / 2 / 4 characters. Numbers and ids only: no image is captured, saved or opened.

Usage (opt-in; the row uses port 17396):
    python -I Tools\\perf_player_check.py --exe <HS2Next.exe> --project <project root> --out <dir> [--port 17396]
                                          [--only PF1,PF2,PF3,PF4,PF5,PF6,PF7,PF8,PF9,UUA] [--timeout-min 20]
    more options: --maps 20 (PF9: the largest maps by lightmap bytes), --frame-poll-ms 1, --lock-dir, --lock-wait-min,
                  --no-locks (diagnosis only), --attach (a player already serving on --port), --label <text>,
                  --gfx d3d11 | d3d12 (the player's graphics API: d3d11 adds -force-d3d11; d3d12 is the build's default),
                  --player-arg=<arg> (repeatable; e.g. --player-arg=-noWarmup for a run without the start-up warm-up)
    The gates, locks and bounds are those of Tools\\maker_session.py (it is imported): the game gate of
    Tools\\session_gate.json, the GPU and input locks, free RAM, every bridge request bounded at 5 min, --timeout-min for
    the whole run, the System log's device events before and after.

Checks (one report line each; the verdict is MET or MISSED for a target, INFO without one, SKIP when this build cannot
measure it, FAIL when the check itself could not run):
  PF1  part changes with mod rows (hair parts, a mod top, a mod head): cold (the first use of the row in this process) and
       warm (switched away and back), plain and with a hover first (the row found through the list's search field, the
       pointer resting 500 ms on its cell: the prefetch path); the synchronous cost (a batch [ping, edit, camera.get,
       ping]) and the frames after the click (app.perf's click frame and the 10 after it when the build has app.perf, else
       the frame poller up to 1 s after the reply); the prefetch's effect = hovered cold against plain cold. Target: warm
       with the hover, no frame above 50 ms.
  PF2  the first redirected load: vanilla hair rows whose prefab is served from a zipmod copy (content.redirect): cold and
       warm synchronous cost. Target: warm <= 30 ms.
  PF4  a list scroll: the body paint list shown and scrolled with 3 x 20 wheel steps; the scroll frames' p95. Target <= 20 ms.
  PF5  the hair part switch between vanilla rows (warm, 10 switches): median synchronous cost. Target <= 5 ms.
  PF6  the first draw-type switch of the hair (the shader type) and the warm ones: synchronous cost and the frames after.
       Target: first <= 20 ms (the app warms the two hair shader materials up after the zipmod scan; its log line is quoted).
  PF8  effects.state {timing}: the LUT bake ms and the frame timing (GPU frame ms null when the player was built without
       frame timing stats). Target: LUT <= 1 ms.
  PF9  dedicated GPU memory per map for the --maps largest maps by lightmap bytes: map.load -> sample -> map.load clear ->
       sample (Windows counters). Target: the map that adds the most <= 2.5 GB.
  UUA  Resources.UnloadUnusedAssets after a map unload with 0 / 2 / 4 characters and 20 items in the scene: the player
       log's "Total: <ms>" line of each unload and the longest frame around it.
  PF3  the Studio item list filter: text typed into the item browser's search field; the longest frame after the search
       fires minus the window's median (the frame poller). Target <= 5 ms.
  PF7  the first frame of a newly built clothed character: shader and keyword set of every renderer slot read in the frame
       of the build (one batch) equal to the state 30 frames later. Target: equal.

Outputs: <out>\\perf_player_check_report.txt (first and last line "SUMMARY: N MET, M MISSED, K INFO, S SKIP, F FAIL"),
<out>\\perf_player_check.json, <out>\\player.log, <out>\\run.log, <out>\\userdata\\.
Exit code: 0 no FAIL, 1 a check failed, 2 setup error or refused, 3 aborted by the game gate.
"""
import argparse
import json
import os
import sys
import threading
import time
import traceback

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)        # python -I does not add the script's folder
import maker_session as M       # noqa: E402

DEFAULT_PORT = 17396
ALL = ["PF1", "PF2", "PF3", "PF4", "PF5", "PF6", "PF7", "PF8", "PF9", "UUA"]


class Checks:
    def __init__(self, S, a):
        self.S = S
        self.a = a
        self.items = []
        self.only = set(x.strip().upper() for x in a.only.split(",")) if a.only else set(ALL) - {"PF9"}

    def add(self, cid, verdict, text, **data):
        self.items.append({"id": cid, "verdict": verdict, "text": text, "data": data})
        self.S.log("%-4s %-6s %s" % (cid, verdict, text))

    def count(self, v):
        return sum(1 for x in self.items if x["verdict"] == v)

    # ------------------------------------------------------------ helpers

    def frames_between(self, t0, t1):
        """Poller frames that ended in [t0, t1] (perf_counter): (max ms, frames above 50 ms, frames)."""
        if self.S.poller is None:
            return None, None, 0
        recs, _ = self.S.poller.take(0)
        d = []
        for t, ms, n, ph in recs:
            if t0 <= t <= t1:
                d.extend([ms] * n)
        if not d:
            return None, 0, 0
        return max(d), sum(1 for x in d if x > 50), len(d)

    def edit(self, key, path, ref, hover_spec=None):
        """(ok, sync ms, round trip ms, max frame ms, frames > 50 ms, note, app.perf of the frames after the click) of one
        chara.edit.item: the frames up to 1 s after the reply from the frame poller and, when the build has app.perf, its frame
        times of the click's frame and the 10 frames after it ({frames, median, p95, max} or None)."""
        S = self.S
        hv = ""
        pf0 = self.prefetch_counts() if hover_spec is not None else None
        if hover_spec is not None:
            hv = S.hover(ref, hover_spec, path)        # the pointer rests --hover-dwell-ms on the cell (the prefetch fires at 150 ms)
        perf = S.supports("app.perf")
        if perf:
            S.call({"cmd": "app.perf", "reset": True}, timeout=60, check=False)
        t0 = time.perf_counter()
        ok, m, sync, rt, err = S.timed({"cmd": "chara.edit.item", "key": key, "path": path, "ref": ref}, timeout=120)
        t1 = time.perf_counter()
        ap = None
        if perf:
            S.call({"cmd": "ping", "waitFrames": 10}, timeout=60, check=False)
            r = S.call({"cmd": "app.perf"}, timeout=60, check=False)
            fm = r.get("frameMs") or {}
            ap = {"frames": r.get("frames"), "median": fm.get("median"), "p95": fm.get("p95"), "max": fm.get("max")} if r.get("ok") else None
        time.sleep(max(0.0, 1.0 - (time.perf_counter() - t1)))
        mx, over, n = self.frames_between(t0, t1 + 1.0)
        S.clear_search()
        pfd = None
        if pf0 is not None:
            pf1 = self.prefetch_counts()
            pfd = {k: (pf1.get(k) or 0) - (pf0.get(k) or 0) for k in pf1 if k != "inFlight"} if pf1 else None
        return ok, sync, rt, mx, over, (("hover " + hv) if hv else "") + ("" if ok else " " + err), ap, pfd

    def prefetch_counts(self):
        """content.stats.prefetch {started, assetRequests, used, expired, refused, limited, inFlight}, or None."""
        if not self.S.supports("content.stats"):
            return None
        r = self.S.call({"cmd": "content.stats"}, timeout=60, check=False)
        return dict(r.get("prefetch") or {}) or None

    def current(self, key, path):
        v = self.S.current_values(key, [path]).get(path)
        return v

    def rows(self, key, path):
        return self.S.rows_of(key, "female", path=path)

    # ------------------------------------------------------------ setup

    def setup(self):
        S = self.S
        S.row_cache = {}
        S.call({"cmd": "scene.new"}, timeout=120)
        S.keys = {"female": S.add_chara("female")}
        S.root = S.ui_root()
        S.select(S.keys["female"])
        S.show({"node": "face", "page": "head"})

    # ------------------------------------------------------------ PF1

    def pf1(self):
        S = self.S
        key = S.keys["female"]
        pf_start = self.prefetch_counts()
        res = {"cold": [], "warm": [], "coldHover": [], "warmHover": [], "head": {}}
        lines = []
        for k, part in enumerate(M.HAIR_PARTS):
            path = "chara.hair.%s.item" % part
            spec = {"node": "hair", "page": "hair", "section": "hair.hair.%s.style" % part, "part": part}
            rows = [r for r in self.rows(key, path) if not r[0].startswith(M.VANILLA + "#")]
            if len(rows) < 3:
                continue
            base = self.current(key, path)
            picks = [rows[S.rng.randrange(len(rows))][0] for _ in range(2)]
            for j, ref in enumerate(picks):
                hs = spec if j == 1 else None
                tag = "Hover" if hs else ""
                c = self.edit(key, path, ref, hover_spec=hs)
                self.edit(key, path, base, None)
                w = self.edit(key, path, ref, hover_spec=hs)
                res["cold" + tag].append(c)
                res["warm" + tag].append(w)
                lines.append("%s %s: cold %s ms (max frame %s), warm %s ms (max frame %s)%s"
                             % (part, ref, M.fmt(c[1]), M.fmt(c[3], 0), M.fmt(w[1]), M.fmt(w[3], 0), " hovered" if hs else ""))
            self.edit(key, path, base, None)
        # a mod top (only while a top and a bottom are worn; never a "none" row)
        cp = "coord.clothes.top.item"
        worn = S.current_values(key, [cp, "coord.clothes.bottom.item"])
        if len(worn) == 2 and all(v and not str(v).endswith("#0") for v in worn.values()):
            tops = [r for r in self.rows(key, cp) if not r[0].startswith(M.VANILLA + "#") and not r[0].endswith("#0")
                    and not M.NONE_NAME.search(r[1])]
            if tops:
                base = worn[cp]
                ref = tops[S.rng.randrange(len(tops))][0]
                spec = {"node": "clothes", "page": "clothes", "section": "clothes.clothes.top.type", "kind": "top"}
                c = self.edit(key, cp, ref, None)
                self.edit(key, cp, base, None)
                w = self.edit(key, cp, ref, spec)
                self.edit(key, cp, base, None)
                res["cold"].append(c)
                res["warmHover"].append(w)
                lines.append("mod top %s: cold %s ms (max frame %s ms), warm hovered %s ms (max frame %s ms)"
                             % (ref, M.fmt(c[1]), M.fmt(c[3], 0), M.fmt(w[1]), M.fmt(w[3], 0)))
        # a mod head
        hp = "chara.face.head"
        heads = [r for r in self.rows(key, hp) if not r[0].startswith(M.VANILLA + "#")]
        if heads:
            base = self.current(key, hp)
            ref = heads[S.rng.randrange(len(heads))][0]
            spec = {"node": "face", "page": "head", "section": "face.head.type"}
            c = self.edit(key, hp, ref, None)
            self.edit(key, hp, base, None)
            w = self.edit(key, hp, ref, spec)
            self.edit(key, hp, base, None)
            res["head"] = {"ref": ref, "cold": c, "warmHover": w}
            lines.append("mod head %s: cold %s ms (max frame %s ms, %s frames > 50 ms), warm hovered %s ms (max frame %s ms)"
                         % (ref, M.fmt(c[1]), M.fmt(c[3], 0), c[4], M.fmt(w[1]), M.fmt(w[3], 0)))
        warm_hovered = list(res["warmHover"]) + ([res["head"]["warmHover"]] if res["head"] else [])

        def max_frame(x):
            """The longest frame after the click: app.perf's (the click's frame and the 10 after) when present, else the poller's."""
            ap = x[6] if len(x) > 6 else None
            return ap.get("max") if ap and ap.get("max") is not None else x[3]
        wh = [max_frame(x) for x in warm_hovered if max_frame(x) is not None]

        def med(key_, idx):
            return M.median([x[idx] for x in res[key_] if x[idx] is not None])
        src = "app.perf" if any(len(x) > 6 and x[6] for x in warm_hovered) else "poller"
        cold_plain, cold_hover = med("cold", 1), med("coldHover", 1)
        ratio = (cold_hover / cold_plain) if cold_hover is not None and cold_plain else None
        effect = ("hovered cold %s ms vs plain cold %s ms (ratio %s: %s; different rows, so a rough figure)"
                  % (M.fmt(cold_hover), M.fmt(cold_plain), M.fmt(ratio, 2),
                     "strong" if ratio is not None and ratio < 0.5 else "some" if ratio is not None and ratio < 0.9 else "none seen"))
        text = ("cold sync median %s ms (max frame median %s), warm %s ms; hovered cold %s, warm %s ms; warm hovered max frame %s ms "
                "(%s; target: none > 50); prefetch: %s | %s"
                % (M.fmt(cold_plain), M.fmt(med("cold", 3), 0), M.fmt(med("warm", 1)), M.fmt(cold_hover),
                   M.fmt(med("warmHover", 1)), M.fmt(max(wh) if wh else None, 0), src, effect, " | ".join(lines)))
        pf_end = self.prefetch_counts()
        if pf_start is not None and pf_end is not None:
            tot = {k: (pf_end.get(k) or 0) - (pf_start.get(k) or 0) for k in pf_end if k != "inFlight"}
            hovered = [x for x in list(res["coldHover"]) + list(res["warmHover"]) + ([res["head"]["warmHover"]] if res["head"] else [])
                       if len(x) > 7 and x[7] is not None]
            text += (" | prefetch counters over PF1: %s; hovered edits %d, of them with a prefetch started %d, used by the edit %d"
                     % (", ".join("%s +%d" % kv for kv in sorted(tot.items())), len(hovered),
                        sum(1 for x in hovered if x[7].get("started", 0) > 0), sum(1 for x in hovered if x[7].get("used", 0) > 0)))
            res["prefetch"] = {"start": pf_start, "end": pf_end, "delta": tot}
        verdict = "INFO" if not wh else ("MET" if max(wh) <= 50 else "MISSED")
        self.add("PF1", verdict, text, result=res)

    # ------------------------------------------------------------ PF2

    def pf2(self):
        S = self.S
        key = S.keys["female"]
        found = []
        seen_paths = set()
        for k, part in enumerate(M.HAIR_PARTS):
            path = "chara.hair.%s.item" % part
            for ref, name in self.rows(key, path):
                if not ref.startswith(M.VANILLA + "#"):
                    continue
                r = S.call({"cmd": "content.redirect", "row": {"category": 300 + k, "ref": ref, "column": "MainData"}}, timeout=60, check=False)
                if not r.get("ok") or not r.get("redirectable"):
                    continue
                served = r.get("served") or {}
                if served.get("kind") != "zipmod" or r.get("path") in seen_paths or ref == self.current(key, path):
                    continue
                seen_paths.add(r.get("path"))
                found.append((path, ref, r.get("path"), served.get("guid")))
                if len(found) >= 4:
                    break
            if len(found) >= 4:
                break
        if not found:
            self.add("PF2", "SKIP", "no vanilla hair row is served from a zipmod copy in this install")
            return
        cold, warm, lines = [], [], []
        for path, ref, bpath, guid in found:
            base = self.current(key, path)
            if base == ref:
                lines.append("%s is the character's own value: left out" % ref)
                continue
            c = self.edit(key, path, ref)
            self.edit(key, path, base)
            w = self.edit(key, path, ref)
            self.edit(key, path, base)
            cold.append(c[1])
            warm.append(w[1])
            lines.append("%s (%s from %s): cold %s ms, warm %s ms" % (ref, bpath, guid, M.fmt(c[1]), M.fmt(w[1])))
        wm = M.median([x for x in warm if x is not None])
        self.add("PF2", "MET" if wm is not None and wm <= 30 else "MISSED" if wm is not None else "FAIL",
                 "first redirected load (the whole part change): cold median %s ms, warm median %s ms (target warm <= 30) | %s"
                 % (M.fmt(M.median([x for x in cold if x is not None])), M.fmt(wm), " | ".join(lines)), rows=found, cold=cold, warm=warm)

    # ------------------------------------------------------------ PF3

    def pf3(self):
        """The Studio item list's filter: the add panel's item browser shown, text typed into its search field; the longest
        frame after the search fires over the window's median approximates the filter's main-thread cost."""
        S = self.S
        S.call({"cmd": "ui.panel", "panel": "add.item", "op": "show"}, timeout=60, check=False)
        S.call({"cmd": "ping", "waitFrames": 10}, timeout=60, check=False)
        q = S.call({"cmd": "ui.query", "name": "items-search"}, timeout=60, check=False)
        els = [e for e in q.get("elements") or [] if e.get("visible") and e.get("worldBound")]
        c = S._clip_center(els[0]["worldBound"], S.root) if els else None
        if c is None:
            self.add("PF3", "SKIP", "the item browser's search field is not shown")
            return
        S.call({"cmd": "ui.input", "events": [{"type": "pointer", "phase": "down", "x": c[0], "y": c[1], "button": 0},
                                              {"type": "pointer", "phase": "up", "x": c[0], "y": c[1], "button": 0}], "frames": 2},
               timeout=60, check=False)
        if S.call({"cmd": "ui.state"}, timeout=60, check=False).get("focusKind") != "TextField":
            self.add("PF3", "SKIP", "the search field did not take the focus")
            return
        res = []
        for text in ("a", "ab", "", "椅", ""):
            ev = [{"type": "key", "key": "A", "mods": ["ctrl"]}, {"type": "key", "key": "Delete"}]
            if text:
                ev.append({"type": "text", "text": text})
            t0 = time.perf_counter()
            S.call({"cmd": "ui.input", "events": ev, "frames": 30}, timeout=60, check=False)
            t1 = time.perf_counter()
            recs, _ = S.poller.take(0) if S.poller else ([], 0)
            d = []
            for t, ms, n, ph in recs:
                if t0 <= t <= t1:
                    d.extend([ms] * n)
            if d:
                res.append((text, max(d) - (M.median(d) or 0.0), max(d), M.median(d)))
        S.call({"cmd": "ui.panel", "panel": "inspector", "op": "show"}, timeout=60, check=False)
        if not res:
            self.add("PF3", "SKIP", "no frames measured")
            return
        worst = max(x[1] for x in res)
        self.add("PF3", "MET" if worst <= 5 else "MISSED",
                 "item list filter (the longest frame after the search fired minus the window's median): %s; worst %s ms (target <= 5)"
                 % (", ".join("'%s' %s ms (max frame %s)" % (t, M.fmt(e, 1), M.fmt(m, 1)) for t, e, m, _ in res), M.fmt(worst, 1)), runs=res)

    # ------------------------------------------------------------ PF7

    def pf7(self):
        """The first frame of a newly built clothed character: every renderer's shader and keyword set read in the frame of
        the build (one batch: chara.load, then chara.edit.materials of each part) equal to the settled state 30 frames later."""
        S = self.S
        parts = ["face", "body", "hair", "clothes", "accessories"]
        cmds = [{"cmd": "chara.load", "path": S.cards["female"], "sex": "female", "as": "c"}]
        cmds += [{"cmd": "chara.edit.materials", "key": "$c.added.0.key", "part": p, "props": ["_MainTex"]} for p in parts]
        r = S.call({"cmd": "batch", "cmds": cmds, "stopOnError": True}, timeout=300, check=False)
        res = r.get("results") or []
        if not r.get("ok") or len(res) != len(cmds):
            self.add("PF7", "FAIL", "the batch failed: %s (%s)" % (str(r.get("error"))[:200], r.get("failedAt")))
            return
        key = ((res[0].get("added") or [{}])[0]).get("key")

        def state(replies):
            out = {}
            for p, rep_ in zip(parts, replies):
                for rd in rep_.get("renderers") or []:
                    for i, sl in enumerate(rd.get("slots") or []):
                        out[(p, rd.get("parent"), rd.get("name"), i)] = (sl.get("shader"), tuple(sorted(sl.get("keywords") or [])))
            return out
        first = state(res[1:])
        S.call({"cmd": "ping", "waitFrames": 30}, timeout=60, check=False)
        later = state([S.call({"cmd": "chara.edit.materials", "key": key, "part": p, "props": ["_MainTex"]}, timeout=60, check=False) for p in parts])
        S.call({"cmd": "delete", "key": key}, timeout=60, check=False)
        keys = set(first) | set(later)
        diff = [k for k in sorted(keys, key=str) if first.get(k) != later.get(k)]
        self.add("PF7", "MET" if not diff else "MISSED",
                 "%d renderer slots read in the build's frame, %d after 30 frames; %d differ in shader or keywords%s"
                 % (len(first), len(later), len(diff), (": " + "; ".join("%s/%s/%s[%d] %s -> %s" % (k[0], k[1], k[2], k[3], first.get(k), later.get(k))
                                                                        for k in diff[:5])) if diff else ""), differ=[str(k) for k in diff[:50]])

    # ------------------------------------------------------------ PF4

    def pf4(self):
        S = self.S
        member = "chara.body.paint[0].item"
        spec = {"node": "body", "page": "paint", "section": "body.paint.p1", "listKey": member}
        rows = self.rows(S.keys["female"], member)
        S.view_list(spec, member, len(rows))
        lb, gb = S.find_list(member)
        c = S._clip_center(gb, S.root) if gb else None
        if c is None:
            lb, gb = S.bring_into_view(member, spec, lb, gb)
            c = S._clip_center(gb, S.root) if gb else None
        if c is None:
            self.add("PF4", "SKIP", "no visible body paint list (list %s, grid %s, panel %s)" % (lb, gb, S.root))
            return
        t0 = time.perf_counter()
        for _ in range(3):
            ev = [{"type": "wheel", "x": c[0], "y": c[1], "delta": self.a.wheel_delta} for _ in range(20)]
            S.call({"cmd": "ui.input", "events": ev, "frames": 2}, timeout=120, check=False)
        t1 = time.perf_counter()
        recs, _ = S.poller.take(0) if S.poller else ([], 0)
        d = []
        for t, ms, n, ph in recs:
            if t0 <= t <= t1:
                d.extend([ms] * n)
        p95 = M.pct(d, 95)
        self.add("PF4", "MET" if p95 is not None and p95 <= 20 else "MISSED" if p95 is not None else "SKIP",
                 "body paint list scroll: %d frames, median %s ms, p95 %s ms, max %s ms (target p95 <= 20)"
                 % (len(d), M.fmt(M.median(d)), M.fmt(p95), M.fmt(max(d) if d else None)), frames=len(d))

    # ------------------------------------------------------------ PF5

    def pf5(self):
        S = self.S
        key = S.keys["female"]
        path = "chara.hair.back.item"
        rows = [r for r in self.rows(key, path) if r[0].startswith(M.VANILLA + "#")]
        if len(rows) < 3:
            self.add("PF5", "SKIP", "fewer than 3 vanilla back-hair rows")
            return
        a_, b_ = rows[1][0], rows[2][0]
        base = self.current(key, path)
        for ref in (a_, b_):
            S.timed({"cmd": "chara.edit.item", "key": key, "path": path, "ref": ref}, timeout=120)
        times = []
        for i in range(10):
            ok, m, sync, rt, err = S.timed({"cmd": "chara.edit.item", "key": key, "path": path, "ref": a_ if i % 2 == 0 else b_}, timeout=120)
            if ok and sync is not None:
                times.append(sync)
        S.timed({"cmd": "chara.edit.item", "key": key, "path": path, "ref": base}, timeout=120)
        med = M.median(times)
        self.add("PF5", "MET" if med is not None and med <= 5 else "MISSED" if med is not None else "FAIL",
                 "hair part switch (warm, %s <-> %s, %d switches): median %s ms, max %s ms (target <= 5; includes the settle)"
                 % (a_, b_, len(times), M.fmt(med, 2), M.fmt(max(times) if times else None, 2)), times=times)

    # ------------------------------------------------------------ PF6

    def pf6(self):
        S = self.S
        key = S.keys["female"]
        path = "chara.hair.shaderType"
        v0 = self.current(key, path)
        if v0 is None:
            self.add("PF6", "SKIP", "no %s member" % path)
            return
        other = 1 if v0 in (0, None) else 0
        res = []
        for i, v in enumerate([other, v0, other, v0]):
            t0 = time.perf_counter()
            ok, m, sync, rt, err = S.timed({"cmd": "chara.edit.set", "key": key, "set": {path: v}}, timeout=120)
            t1 = time.perf_counter()
            time.sleep(1.0)
            mx, over, n = self.frames_between(t0, t1 + 1.0)
            res.append((ok, sync, mx, err))
        first = res[0]
        warm = self.warmup_line()
        self.add("PF6", "MISSED" if first[1] is not None and first[1] > 20 else "MET" if first[1] is not None else "INFO",
                 "first draw-type switch %s ms (max frame %s ms), then %s ms (target <= 20); start-up warm-up: %s%s"
                 % (M.fmt(first[1]), M.fmt(first[2], 0), ", ".join(M.fmt(x[1]) for x in res[1:]), warm or "no warm-up line in the log",
                    "" if first[0] else " - " + str(first[3])),
                 runs=res)

    def warmup_line(self):
        """The player log's start-up warm-up line ("[app] warm-up: ..."), or None (a build without the warm-up)."""
        try:
            with open(self.S.player_log, encoding="utf-8", errors="replace") as f:
                for line in f:
                    if line.startswith("[app] warm-up:"):
                        return line.strip()[len("[app] warm-up: "):]
        except (OSError, AttributeError, TypeError):
            pass
        return None

    # ------------------------------------------------------------ PF8

    def pf8(self):
        S = self.S
        r = S.call({"cmd": "effects.state", "blocks": ["colorGrading"], "timing": True}, timeout=60, check=False)
        t = r.get("timing") or {}
        bake = t.get("bakeMs")
        self.add("PF8", "MET" if isinstance(bake, (int, float)) and bake <= 1 else "MISSED" if isinstance(bake, (int, float)) else "INFO",
                 "LUT bake %s ms, base grade %s ms, CPU frame %s ms, GPU frame %s ms (null: no frame timing stats in this build) at %s"
                 % (bake, t.get("baseGradeMs"), M.fmt(t.get("cpuFrameMs")), t.get("gpuFrameMs"), S.root), timing=t)

    # ------------------------------------------------------------ PF9

    def pf9(self):
        S = self.S
        refs = []
        cache = os.path.join(S.project, "Cache", "maps")
        sizes = []
        for fn in ("maps_mods.jsonl", "maps_vanilla.jsonl"):
            p = os.path.join(cache, fn)
            if not os.path.isfile(p):
                continue
            with open(p, encoding="utf-8") as f:
                for line in f:
                    try:
                        d = json.loads(line)
                    except ValueError:
                        continue
                    per = []
                    for sc in (d.get("scenes") or {}).values():
                        tot = 0
                        for lm in ((sc or {}).get("lightmapSettings") or {}).get("lightmaps") or []:
                            for k in ("color", "dir", "mask"):
                                ti = lm.get(k)
                                if isinstance(ti, dict):
                                    tot += int(ti.get("bytes") or 0)
                        per.append(tot)
                    if len(per) == 1:
                        for r in d.get("rows") or []:
                            sizes.append((per[0], "%s#%s" % (r.get("guid"), r.get("no"))))
        sizes.sort(key=lambda x: -x[0])
        for b, ref in sizes:
            if ref not in [x[1] for x in refs] and (S.loadable_maps is None or ref in S.loadable_maps):
                refs.append((b, ref))
            if len(refs) >= self.a.maps:
                break
        if not refs:
            self.add("PF9", "SKIP", "no map scan cache")
            return
        S.call({"cmd": "map.load", "clear": True}, timeout=300, check=False)
        rows = []
        for b, ref in refs:
            time.sleep(2.0)
            g0 = M.gpu_counters(S.pid)
            ok, m, sync, rt, err = S.timed({"cmd": "map.load", "ref": ref}, timeout=300)
            time.sleep(2.0)
            g1 = M.gpu_counters(S.pid)
            S.call({"cmd": "map.load", "clear": True}, timeout=300, check=False)
            time.sleep(3.0)
            g2 = M.gpu_counters(S.pid)
            def ded(g):
                return g.get("procDed") / M.MIB if g.get("procDed") is not None else None
            rows.append({"ref": ref, "lightmapMB": b / M.MIB, "ok": ok, "loadMs": (m or {}).get("loadMs"), "before": ded(g0),
                         "loaded": ded(g1), "after": ded(g2)})
            S.log("PF9 %s: lightmaps %.0f MB, dedicated %s -> %s -> %s MB%s" % (ref, b / M.MIB, M.fmt(ded(g0), 0), M.fmt(ded(g1), 0),
                                                                          M.fmt(ded(g2), 0), "" if ok else " (" + err + ")"))
        for r in rows:
            r["addMB"] = (r["loaded"] - r["before"]) if r["loaded"] is not None and r["before"] is not None else None
        deltas = [r["addMB"] for r in rows if r["addMB"] is not None]
        big = max((r for r in rows if r["addMB"] is not None), key=lambda r: r["addMB"], default=None)
        bd = big["addMB"] if big else None
        self.add("PF9", "MET" if bd is not None and bd <= 2560 else "MISSED" if bd is not None else "FAIL",
                 "%d maps: the largest in GPU memory (%s, %.0f MB of lightmaps) adds %s MB of dedicated memory (target <= 2560); "
                 "median add %s MB; after the unloads the process holds %s MB (before the first load %s MB) | %s"
                 % (len(rows), big["ref"] if big else None, big["lightmapMB"] if big else 0, M.fmt(bd, 0), M.fmt(M.median(deltas), 0),
                    M.fmt(rows[-1]["after"], 0), M.fmt(rows[0]["before"], 0),
                    ", ".join("%s +%s" % (r["ref"], M.fmt(r["addMB"], 0)) for r in rows)), rows=rows)

    # ------------------------------------------------------------ UUA (L0h)

    def log_mark(self):
        try:
            return os.path.getsize(self.S.player_log)
        except (OSError, AttributeError):
            return None

    def log_totals_since(self, mark):
        out = []
        if mark is None:
            return out
        try:
            with open(self.S.player_log, "rb") as f:
                f.seek(mark)
                data = f.read().decode("utf-8", "replace")
        except OSError:
            return out
        unl = None
        for line in data.splitlines():
            s = line.strip()
            if s.startswith("Unloading ") and "unused Assets" in s:
                unl = s
            m = M.UUA_TOTAL.match(s)
            if m:
                out.append({"totalMs": float(m.group(1)), "line": s[:200], "unloading": unl})
        return out

    def uua(self):
        S = self.S
        if self.a.attach:
            self.add("UUA", "SKIP", "attached: no player.log of this run")
            return
        maps = [r for r in S.call({"cmd": "maps", "limit": 100000}, timeout=120, check=False).get("maps") or []]
        vref = next((m.get("id") for m in maps if str(m.get("id", "")).startswith(M.VANILLA + "#")), None)
        if not vref:
            self.add("UUA", "SKIP", "no vanilla map")
            return
        item_rows = []
        for g in M.DEFAULT_ITEM_GROUPS[:4]:
            r = S.call({"cmd": "item.search", "group": g, "limit": 200}, timeout=120, check=False)
            item_rows += [it.get("id") for it in r.get("items") or [] if it.get("guid") == M.VANILLA and it.get("id") not in S.crash_refs]
        res = []
        for n in (0, 2, 4):
            S.call({"cmd": "scene.new"}, timeout=120)
            time.sleep(3.0)
            for i in range(n):
                S.add_chara("female" if i % 2 == 0 else "male")
            for i in range(20 if item_rows else 0):
                S.call({"cmd": "item.add", "ref": item_rows[(i * 7) % len(item_rows)]}, timeout=120, check=False)
            S.call({"cmd": "map.load", "ref": vref}, timeout=300, check=False)
            time.sleep(2.0)
            mark = self.log_mark()
            t0 = time.perf_counter()
            S.call({"cmd": "map.load", "clear": True}, timeout=300, check=False)
            time.sleep(4.0)
            t1 = time.perf_counter()
            tot = self.log_totals_since(mark)
            mx, over, nf = self.frames_between(t0, t1)
            stats = None
            if S.supports("content.stats"):
                uu = S.call({"cmd": "content.stats"}, timeout=60, check=False).get("unloadUnused") or {}
                stats = {"calls": uu.get("calls"), "lastMs": uu.get("lastMs"), "maxMs": uu.get("maxMs")}
            res.append({"characters": n, "items": 20 if item_rows else 0, "totals": tot, "maxFrameMs": mx, "contentStats": stats})
            S.log("UUA %d characters: %s; max frame %s ms; content.stats %s" % (n, [x["totalMs"] for x in tot], M.fmt(mx, 0), stats))
        parts = ["%d characters: %s (max frame %s ms%s)" % (r["characters"], (", ".join(M.fmt(x["totalMs"], 1) for x in r["totals"]) + " ms") if r["totals"] else "no Total line",
                                                            M.fmt(r["maxFrameMs"], 0),
                                                            ("; content.stats last %s ms, calls %s" % (M.fmt(r["contentStats"].get("lastMs"), 1), r["contentStats"].get("calls")))
                                                            if r.get("contentStats") else "") for r in res]
        verdict = "INFO" if any(r["totals"] for r in res) else "SKIP"
        self.add("UUA", verdict, "Resources.UnloadUnusedAssets after a map unload (map %s, 20 items): %s" % (vref, "; ".join(parts)), runs=res)

    # ------------------------------------------------------------ run

    def run(self):
        order = [("PF1", self.pf1), ("PF2", self.pf2), ("PF5", self.pf5), ("PF6", self.pf6), ("PF4", self.pf4), ("PF7", self.pf7),
                 ("PF3", self.pf3), ("PF8", self.pf8), ("UUA", self.uua), ("PF9", self.pf9)]
        self.setup()
        for cid, fn in order:
            if cid not in self.only:
                continue
            self.S.phase = cid.lower()
            try:
                fn()
            except (M.Abort, M.TimeUp):
                raise
            except Exception as e:
                self.add(cid, "FAIL", "exception: %s: %s" % (type(e).__name__, str(e)[:300]), traceback=traceback.format_exc(limit=6))

    def write(self, exit_code, extra):
        head = "SUMMARY: %d MET, %d MISSED, %d INFO, %d SKIP, %d FAIL" % (self.count("MET"), self.count("MISSED"), self.count("INFO"),
                                                                          self.count("SKIP"), self.count("FAIL"))
        lines = [head] + extra + ["%s %s %s" % (x["id"], x["verdict"], x["text"]) for x in self.items] + [head]
        with open(os.path.join(self.S.out, "perf_player_check_report.txt"), "w", encoding="utf-8") as f:
            f.write("\n".join(lines) + "\n")
        with open(os.path.join(self.S.out, "perf_player_check.json"), "w", encoding="utf-8") as f:
            json.dump({"summary": head, "exit": exit_code, "checks": self.items, "info": self.S.info}, f, ensure_ascii=False, indent=1, default=str)
        for line in lines:
            self.S.log(line)


def main(argv):
    for s in (sys.stdout, sys.stderr):
        try:
            s.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, ValueError):
            pass
    ap = argparse.ArgumentParser(description="Player measurements PF1-PF9 and the UnloadUnusedAssets cost (numbers only).")
    ap.add_argument("--exe")
    ap.add_argument("--project")
    ap.add_argument("--out")
    ap.add_argument("--port", type=int, default=DEFAULT_PORT)
    ap.add_argument("--only", default="", help="comma list of PF1-PF9, UUA (default all but PF9)")
    ap.add_argument("--timeout-min", type=float, default=20.0)
    ap.add_argument("--maps", type=int, default=20)
    ap.add_argument("--seed", type=int, default=M.DEFAULT_SEED)
    ap.add_argument("--frame-poll-ms", type=float, default=1.0)
    ap.add_argument("--wheel-delta", type=float, default=12.0)
    ap.add_argument("--lock-dir")
    ap.add_argument("--lock-wait-min", type=float, default=0.0)
    ap.add_argument("--no-locks", action="store_true")
    ap.add_argument("--ram-wait-min", type=float, default=10.0)
    ap.add_argument("--min-free-gb", type=float, default=8.0)
    ap.add_argument("--ready-timeout-min", type=float, default=12.0)
    ap.add_argument("--label")
    ap.add_argument("--attach", action="store_true")
    ap.add_argument("--player-arg", dest="player_args", action="append", default=[],
                    help="one more player argument, repeatable (--player-arg=-noTexturePrescan)")
    ap.add_argument("--gfx", choices=["d3d11", "d3d12"], help="the player's graphics API (d3d11 adds -force-d3d11)")
    a = ap.parse_args(argv)
    if a.gfx == "d3d11" and "-force-d3d11" not in a.player_args:
        a.player_args.append("-force-d3d11")
    # the session's attributes the shared code reads
    a.minutes = a.timeout_min
    a.wall_min = a.timeout_min
    a.items = None
    a.sample_sec = 1e9
    a.cycle_sec = 300.0
    a.gpu_guard_mb = 7000.0
    a.hover_search = True
    a.hover_dwell_ms = 500.0
    a.shared_guard_mb = 4096.0
    if a.attach:
        a.no_locks = True
    if not a.label:
        a.label = "perf_player_check"
    S = M.Session(a)
    C = Checks(S, a)
    S.player_exit, S.quitting = None, False
    S.watch_stop = threading.Event()
    S.sampler_stop = threading.Event()
    S.poll_index = 0
    exit_code = 0
    dev0, dev1 = {}, {}
    extra = []
    watch = None
    try:
        try:
            S.load_config()
            S.preflight()
        except M.SetupError as e:
            C.add("SETUP", "FAIL", "refused: %s" % e)
            C.write(2, [])
            return 2
        dev0 = M.device_events()
        watch = threading.Thread(target=S.watcher, name="watcher", daemon=True)
        watch.start()
        try:
            S.start_player()
            if a.frame_poll_ms > 0:
                S.poller = M.FramePoller(S, a.frame_poll_ms)
                S.poller.start()
            S.t_work = time.perf_counter()
            C.run()
        except M.SetupError as e:
            C.add("SETUP", "FAIL", str(e))
            exit_code = 2
        except M.Abort as e:
            C.add("ABORT", "SKIP" if S.gate_abort else "FAIL", "the run stopped: %s" % e)
            exit_code = 3 if S.gate_abort else 1
        except Exception as e:
            C.add("ERROR", "FAIL", "%s: %s" % (type(e).__name__, str(e)[:300]), traceback=traceback.format_exc(limit=8))
            exit_code = 1
    finally:
        S.phase = "quit"
        if S.poller is not None:
            S.poller.stop_ev.set()
            S.poller.join(10)
        S.quitting = True
        stop_how = None
        if S.proc is not None:
            S.player_exit, stop_how = S.stop_player("end of the checks" if not S.abort_reason else S.abort_reason)
        S.watch_stop.set()
        if watch is not None:
            watch.join(30)
        if S.locks is not None:
            S.locks.release()
        if S.proc is not None:
            dev1 = M.device_events()
            scan = S.scan_player_log()
            extra.append("run: stamp %s, exe %s (modified %s), port %s, pid %s, ready %s s, renderer %s, exit %s (%s), device events "
                         "before %s after %s, exceptions in player.log %s, device-removed lines %s, frame poller %s polls"
                         % (S.stamp, S.exe, S.info.get("exeModified"), S.port, S.pid, S.info.get("readySeconds"), scan.get("renderer"),
                            S.player_exit, stop_how, json.dumps(dev0), json.dumps(dev1), scan.get("exceptionLines"), scan.get("deviceLines"),
                            S.poller.polls if S.poller else 0))
            if scan.get("nativeCrash"):
                C.add("CRASH", "FAIL", "the player crashed natively (exit %s; crash report under %s)" % (S.player_exit, scan.get("crashFolder")))
            if scan.get("deviceLines") or (dev1.get("id153", 0) - dev0.get("id153", 0)) > 0:
                C.add("DEVICE", "FAIL", "device loss: %s device-removed lines, nvlddmkm id 153 +%s" % (scan.get("deviceLines"), dev1.get("id153", 0) - dev0.get("id153", 0)))
        if exit_code == 0 and C.count("FAIL"):
            exit_code = 1
        C.write(exit_code, extra)
        S.logf.close()
    return exit_code


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
