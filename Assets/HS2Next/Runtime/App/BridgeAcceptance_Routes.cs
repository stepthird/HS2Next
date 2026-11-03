// RT1 of the bridge acceptance (Phase 3.6 package R with K's clock and C's capture), in
// Editor play mode (BridgeTest.RunPlay after UI1, BridgeTest.RunPlayRoutes alone) and in the app player (-bridgeAccept):
//   scene.new; route.add; three route.addPoint (point 1 Curve, link off); a vanilla item attached to the route; route.play;
//   60 frames at a fixed 1/60 s (Time.captureFramerate) → route.state τ ≈ 1 s and the child root = the evaluator at τ (1e-4),
//   the item's world pose = child root × its local pose (1e-4); scene.pause → 30 frames → the child root unchanged
//   (bit-equal); scene.step {n: 30} → τ + 30 × 1/60 exactly; screenshot {mode: custom, 2560 × 1440} → a 2560 × 1440 PNG in
//   <UserData>/cap; scene.images {background: hs2:DefaultData/bg/<first>} → stored as an hs2 reference; save → new → load →
//   the route plays from τ 0, the image reference equal; a new route's edits undone back to the start point alone.
//   After 1.1.0, while paused: chara.load (BR-Chan) and obj.transform (3, 1, 2) → after 3 frames the character has settled
//   (CharaMotion.Settles ≥ 1, none pending), its motion time is still 0, every breast / hip chain segment is shorter than 1 unit
//   (1.1.0 left them stretched toward the scene origin, 3.7 units for this placement); the character deleted.
using System;
using System.Collections;
using System.IO;
using System.Linq;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Routes;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace HS2Next.App
{
    public static partial class BridgeAcceptance
    {
        /// <summary>After 1.1.0: a character added and placed while the scene is paused stands settled at once (RT1).</summary>
        static IEnumerator PausedAdd(AcceptanceCtx c)
        {
            if (string.IsNullOrEmpty(c.FemaleRel)) { c.Warn("no female card given: the paused add is skipped"); yield break; }
            yield return c.Call(new { cmd = "chara.load", path = c.FemaleRel, sex = "female" }, 180);
            string ch = (string)c.J["added"]?[0]?["key"];
            if (!c.Check(c.Last != null && c.Last.Ok && ch != null, "paused: chara.load BR-Chan", AcceptanceCtx.Short(c.Last?.ToString()))) yield break;
            yield return c.Call(new { cmd = "obj.transform", key = ch, pos = new[] { 3, 1, 2 } });
            c.CheckOk("paused: obj.transform (3, 1, 2)");
            yield return c.Frames(3);
            var cr = AppCore.Current?.Runtime?.FindChara(ch);
            var m = cr != null ? cr.Motion : null;
            if (c.Check(m != null && cr.State == RealizationState.Built, "paused: the character is built"))
            {
                c.Check(m.Settles >= 1 && !m.SettlePending && m.Time == 0f, "paused: its physics settled at once (" + m.Settles + " settle(s), " + m.LastSettleMs.ToString("F1") + " ms), its motion time still 0",
                    $"settles {m.Settles}, pending {m.SettlePending}, time {m.Time:R}");
                float worst = 0f;
                foreach (var b in m.GetAll<HS2Next.Motion.DynamicBones.BustStage>())
                    foreach (var ch2 in b.All)
                        for (int i = 1; i < ch2.ParticleCount; i++)
                        {
                            Transform t = ch2.ParticleTransform(i), u = ch2.ParticleTransform(i - 1);
                            if (t && u) worst = Mathf.Max(worst, (t.position - u.position).magnitude);
                        }
                c.Check(worst > 0f && worst < 1f, "paused: every breast / hip chain segment shorter than 1 unit (not stretched toward the origin)", $"longest {worst:F3} units");
            }
            yield return c.Call(new { cmd = "delete", keys = new[] { ch } });
            c.CheckOk("paused: the character deleted");
        }

        static Vector3 RtV(JToken t) => t is JArray a && a.Count >= 3 ? new Vector3((float)a[0], (float)a[1], (float)a[2]) : new Vector3(float.NaN, float.NaN, float.NaN);

        public static IEnumerator Routes(AcceptanceCtx c)
        {
            c.Id = "RT1";
            int capture = Time.captureFramerate;
            Time.captureFramerate = 60;
            try
            {
                var e = RoutesInner(c);
                while (true)
                {
                    bool more;
                    try { more = e.MoveNext(); }
                    catch (Exception ex) { c.Check(false, "exception", ex.GetType().Name + ": " + ex.Message + "\n" + ex.StackTrace); break; }
                    if (!more) break;
                    yield return e.Current;
                }
            }
            finally
            {
                Time.captureFramerate = capture;
                var rt = AppCore.Current?.Runtime;
                if (rt != null && rt.Paused) rt.SetPaused(false);
            }
        }

        static IEnumerator RoutesInner(AcceptanceCtx c)
        {
            var app = AppCore.Current;
            yield return c.Call(new { cmd = "scene.new" });
            c.CheckOk("scene.new");
            yield return c.Call(new { cmd = "route.add" });
            string key = (string)c.J["key"];
            if (!c.Check(c.Last.Ok && key != null, "route.add", AcceptanceCtx.Short(c.Last?.ToString()))) yield break;
            yield return c.Call(new { cmd = "route.addPoint", key, pos = new[] { 8, 0, 0 } });
            c.CheckOk("route.addPoint (8, 0, 0)");
            yield return c.Call(new { cmd = "route.addPoint", key, pos = new[] { 8, 2, 8 } });
            c.CheckOk("route.addPoint (8, 2, 8)");
            yield return c.Call(new { cmd = "route.addPoint", key });
            c.Check(c.Last.Ok && (int)c.J["count"] == 4, "route.addPoint (continuing the path, R8): 4 points", AcceptanceCtx.Short(c.Last?.ToString()));
            yield return c.Call(new { cmd = "route.setPoint", key, point = 1, connection = "curve", link = false });
            c.Check(c.Last.Ok && c.J["aid"]?["init"]?.Value<bool>() == true, "route.setPoint point 1 Curve (link off): the midpoint aid", AcceptanceCtx.Short(c.Last?.ToString()));
            yield return c.Call(new { cmd = "item.add", no = 399, parent = key });
            string item = (string)c.J["added"]?[0]?["key"] ?? (string)c.J["key"];
            c.Check(c.Last.Ok && item != null, "item.add 399 under the route (a rider on the child root)", AcceptanceCtx.Short(c.Last?.ToString()));
            yield return c.Call(new { cmd = "obj.transform", key = item, pos = new[] { 0, 1, 0 }, rot = new[] { 0, 30, 0 } });
            c.CheckOk("obj.transform of the rider");
            yield return c.Call(new { cmd = "route.play", key });
            c.Check(c.Last.Ok && (string)c.J["routes"]?[0]?["state"] == "playing", "route.play", AcceptanceCtx.Short(c.Last?.ToString()));
            var ops = StudioOps.For(app.Runtime);
            var real = ops.RouteReal(key);
            if (!c.Check(real != null, "the route is realized")) yield break;
            double t0 = real.Time;
            yield return c.Frames(60);
            double t1 = real.Time;
            c.Check(Math.Abs(t1 - t0 - 1.0) < 1e-6, "60 frames at 1/60 s advance τ by 1 s (in-process, exact frames)", $"Δτ {t1 - t0:R}");
            yield return c.Call(new { cmd = "route.state", key, line = false });
            double tau = (double?)c.J["time"] ?? double.NaN;
            c.Check(c.Last.Ok && (string)c.J["state"] == "playing" && Math.Abs(tau - 1.0) <= 4.0 / 60.0, "route.state after 60 frames: playing, τ ≈ 1 s (± the request's frames)", $"τ {tau:F4}");
            var route = (RouteNodeState)app.Session.Model.Find(key).State;
            var path = RoutePath.Build(route, RouteSettings.Options());
            var s = path.Evaluate(tau);
            var lp = RtV(c.J["childRoot"]?["localPos"]);
            var ev = new Vector3((float)s.Position.X, (float)s.Position.Y, (float)s.Position.Z);
            c.Check((lp - ev).magnitude <= 1e-4, "the child root's local position = the evaluator at the reply's τ (1e-4)", $"{lp} vs {ev}");
            var root = real.ChildRoot;
            var h = app.Runtime.HolderOf(item);
            c.Check(h && root && h.parent == root && (h.position - root.TransformPoint(new Vector3(0, 1, 0))).magnitude <= 1e-4 &&
                    Quaternion.Angle(h.rotation, root.rotation * Quaternion.Euler(0, 30, 0)) <= 0.01f,
                "the rider's world pose = child root × its local pose (1e-4)", h ? $"{h.position} / {root.TransformPoint(new Vector3(0, 1, 0))}" : "no holder");
            // the global pause
            yield return c.Call(new { cmd = "scene.pause", paused = true });
            c.Check(c.Last.Ok && c.J["paused"]?.Value<bool>() == true, "scene.pause true");
            yield return c.Call(new { cmd = "route.state", key, line = false });
            var a = c.J["childRoot"]?.ToString(Newtonsoft.Json.Formatting.None);
            double ta = (double?)c.J["time"] ?? double.NaN;
            yield return c.Frames(30);
            yield return c.Call(new { cmd = "route.state", key, line = false });
            var b = c.J["childRoot"]?.ToString(Newtonsoft.Json.Formatting.None);
            double tb = (double?)c.J["time"] ?? double.NaN;
            c.Check(a != null && a == b && ta == tb, "paused: 30 frames leave τ and the child root unchanged (bit-equal)", $"τ {ta:R} / {tb:R}");
            yield return c.Call(new { cmd = "scene.step", n = 30 });
            c.CheckOk("scene.step 30");
            yield return c.Call(new { cmd = "route.state", key, line = false });
            double tc = (double?)c.J["time"] ?? double.NaN;
            double expect = tb;
            for (int i = 0; i < 30; i++) expect += 1f / 60f;
            c.Check(tc == expect, "scene.step 30 advances τ by 30 × 1/60 exactly", $"τ {tc:R} vs {expect:R}");
            yield return PausedAdd(c);
            yield return c.Call(new { cmd = "scene.pause", paused = false });
            c.CheckOk("scene.pause false");
            // the screenshot (package C)
            yield return c.Call(new { cmd = "screenshot", mode = "custom", width = 2560, height = 1440, name = "rt1_custom" }, 180);
            string shot = (string)c.J["path"];
            if (c.Check(c.Last.Ok && shot != null, "screenshot custom 2560 × 1440", AcceptanceCtx.Short(c.Last?.ToString())))
            {
                string capDir = Path.GetFullPath(app.Paths.CapDir);
                var (w, hh, _, _) = Png(shot, null);
                c.Check(Path.GetFullPath(shot).StartsWith(capDir, StringComparison.OrdinalIgnoreCase) && w == 2560 && hh == 1440, "the file is in <UserData>/cap, 2560 × 1440", $"{shot}: {w} × {hh}");
            }
            // a DefaultData background (package C)
            string bgDir = app.Paths.Hs2Root != null ? Path.Combine(app.Paths.Hs2Root, "DefaultData", "bg") : null;
            string bg = bgDir != null && Directory.Exists(bgDir) ? Directory.GetFiles(bgDir, "*.png").OrderBy(x => x, StringComparer.Ordinal).FirstOrDefault() : null;
            JToken bgRef = null;
            if (bg == null) c.Warn("no HS2 DefaultData/bg image: scene.images skipped");
            else
            {
                yield return c.Call(new { cmd = "scene.images", background = "hs2:DefaultData/bg/" + Path.GetFileName(bg) });
                bgRef = c.J["background"];
                c.Check(c.Last.Ok && bgRef?["hs2"] != null && ((string)bgRef["hs2"]).EndsWith(Path.GetFileName(bg), StringComparison.Ordinal), "scene.images background: stored as an hs2 reference", AcceptanceCtx.Short(c.Last?.ToString()));
            }
            // save → new → load
            yield return c.Call(new { cmd = "scene.save", name = "rt1_routes", overwrite = true, thumb = "placeholder" });
            c.CheckOk("scene.save rt1_routes");
            yield return c.Call(new { cmd = "scene.new" });
            yield return c.Call(new { cmd = "route.list" });
            c.Check((c.J["routes"] as JArray)?.Count == 0, "scene.new: no routes");
            yield return c.Call(new { cmd = "scene.load", name = "rt1_routes" }, 300);
            c.CheckOk("scene.load rt1_routes");
            yield return c.Call(new { cmd = "route.state", key, line = false });
            c.Check(c.Last.Ok && (string)c.J["state"] == "playing" && (double)c.J["time"] < 0.2, "after load the playing route plays again from τ 0", AcceptanceCtx.Short(c.Last?.ToString()));
            if (bgRef != null)
            {
                yield return c.Call(new { cmd = "scene.images" });
                c.Check(JToken.DeepEquals(c.J["background"], bgRef), "the background reference after load equals the saved one", AcceptanceCtx.Short(c.J["background"]?.ToString()));
            }
            // undo chain over a new route's edits back to the route with its start point alone
            yield return c.Call(new { cmd = "route.add", pos = new[] { 0, 0, 20 } });
            string r2 = (string)c.J["key"];
            yield return c.Call(new { cmd = "route.state", key = r2, line = false });
            string startOnly = c.J["points"]?.ToString(Newtonsoft.Json.Formatting.None);
            int edits = 0;
            foreach (var req in new object[]
                     {
                         new { cmd = "route.addPoint", key = r2 }, new { cmd = "route.addPoint", key = r2 },
                         new { cmd = "route.set", key = r2, loop = false, orient = "xy" }, (object)new { cmd = "route.setPoint", key = r2, point = 1, connection = "curve", speed = 5 },
                     })
            {
                yield return c.Call(req);
                if (c.Last.Ok) edits++;
            }
            c.Check(edits == 4, "four route edits on the new route");
            for (int i = 0; i < edits; i++) yield return c.Call(new { cmd = "undo" });
            yield return c.Call(new { cmd = "route.state", key = r2, line = false });
            c.Check(c.J["points"]?.ToString(Newtonsoft.Json.Formatting.None) == startOnly && (bool?)c.J["loop"] == true && (string)c.J["orient"] == "none",
                "undo × 4 back to the route with its start point alone (the document of route.add)", AcceptanceCtx.Short(c.J["points"]?.ToString()));
        }
    }
}
