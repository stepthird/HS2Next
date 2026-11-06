// The parts of the bridge acceptance that both hosts run (Phase 2.5): the check context
// (AcceptanceCtx, formerly BridgeTest.PlayCtx of the Editor), the part guard and the report text, the AutoStep checks
// S1–S6 (package W2's, moved from Editor/BridgeTest_Motion.cs; bridge commands plus Time.captureDeltaTime and the face
// random seed) and the error and load checks E1–E3 (moved from Editor/BridgeTest.cs; bridge commands over HTTP only).
// The Editor's BridgeTest.play runs A1–A14, S1–S6, its HS2-shape checks H1–H5 (Editor helpers and Cache/compare), E1–E3
// and the render-parity shots T5; the player's -bridgeAccept (PlayerAcceptance) runs A1–A14, S1–S6, E1–E3 and T5 and
// compares its T5 shots with the Editor's.
//   S1–S6 (the play-mode AutoStep test) with Time.captureDeltaTime = 1/60 (restored after): each check reads
//     chara.motion {key} (with scene in the same batch) about 60 frames apart; Δf = the difference of the replies' frame
//     members (a reply at frame f has seen the steps of the frames before f only)
//     S1 autoStep, not paused, time scale 1, Idle_00 speed 1: Δsteps = Δf, Δtime = Δf / 60 (± 1e-5), Δ normalizedTime =
//        Δf / 60 / length (± 1e-3)       S2 paused: Δtime 0, normalized time unchanged, Δsteps = Δf (dt-0 steps)
//     S3 time scale 0.5: Δtime = 0.5 Δf / 60 (± 1e-5)       S4 autoStep false: Δsteps 0
//     S5 AutoStep vs manual: two fresh characters of the acceptance card (one batch, same face random seed, autoStep off
//        and reset before any frame, Idle_00 at 0); A auto-steps about 60 frames, then B steps the same count by hand with
//        dt 1/60 → bones.dump all of A and B equal value for value (physics on)
//     S6 app.options {rebuild} keeps the acceptance character's overrides (paused true after the rebuild)
//   E1 a POST with a browser Origin header (and Sec-Fetch-Site: cross-site) → HTTP 403, no side effect; E2 1,000
//     sequential pings and 4 concurrent clients × 50 scene requests: no lost or mixed replies; E3 scene requests sent while
//     a map.load waits are answered after it (FIFO), a ping meanwhile at once.
//   T5 render-parity shots in a fixed state (Parity: a fresh character with the fixed face seed, paused with physics and
//     look off before its first frame, Idle_00 at 0.5, a fixed camera; with map 10 in SMAA and MSAA 4, without a map in
//     SMAA).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HS2Next.Motion.Face;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace HS2Next.App
{
    /// <summary>What the acceptance parts work with: the client, the check counts and the report lines (both hosts).</summary>
    public sealed class AcceptanceCtx : IDisposable
    {
        public readonly StringBuilder Rep = new StringBuilder();
        public readonly Dictionary<string, (int pass, int fail, int warn)> PerCheck = new Dictionary<string, (int, int, int)>(StringComparer.Ordinal);

        public BridgeClient Client { get; set; }
        public string Url => Client?.Url;
        /// <summary>The acceptance card, relative to the HS2 female card folder (chara.load's path with sex "female").</summary>
        public string FemaleRel { get; set; }
        /// <summary>The acceptance character's node id (after the reload of A11), or null.</summary>
        public string FemaleKey { get; set; }
        /// <summary>The check id the next lines are filed under ("S1").</summary>
        public string Id { get; set; } = "";
        /// <summary>The reply of the last <see cref="Call"/>.</summary>
        public BridgeResponse Last { get; private set; }
        public int Passed, Failed, Warned;

        /// <summary>Posts a request (JSON text or an object to serialize) and waits for the reply (frames keep running); see <see cref="Last"/>.</summary>
        public IEnumerator Call(object request, double timeoutSec = 300, IDictionary<string, string> headers = null)
        {
            string json = request is string s ? s : JsonConvert.SerializeObject(request);
            var box = new BridgeResponse[1];
            yield return Client.Call(json, box, timeoutSec, headers);
            Last = box[0];
        }

        /// <summary>The last reply's JSON (an empty object when it was not one).</summary>
        public JObject J => Last?.Json ?? new JObject();

        /// <summary>Checks that the last reply is ok (FAIL with the reply otherwise).</summary>
        public bool CheckOk(string what) => Check(Last != null && Last.Ok, what, Last != null && Last.Ok ? "" : Short(Last?.ToString()));

        /// <summary>Waits <paramref name="n"/> Unity frames.</summary>
        public IEnumerator Frames(int n)
        {
            int target = Time.frameCount + Math.Max(0, n);
            while (Time.frameCount < target) yield return null;
        }

        public bool Check(bool ok, string what, string detail = "")
        {
            Rep.AppendLine($"{(ok ? "PASS" : "FAIL")}  [{Id}] {what}{(string.IsNullOrEmpty(detail) ? "" : "  " + detail)}");
            if (ok) Passed++; else Failed++;
            PerCheck.TryGetValue(Id, out var v);
            PerCheck[Id] = ok ? (v.pass + 1, v.fail, v.warn) : (v.pass, v.fail + 1, v.warn);
            return ok;
        }

        /// <summary>The check callback of <see cref="BridgeAcceptance.Run"/>: "A&lt;n&gt;: what  detail" is filed under A&lt;n&gt;.</summary>
        public void CheckLine(bool ok, string text)
        {
            int colon = text.IndexOf(':');
            bool hasId = colon > 0 && colon < 6;
            Id = hasId ? text.Substring(0, colon) : "A";
            Check(ok, hasId ? text.Substring(colon + 1).Trim() : text);
        }

        public void Warn(string what)
        {
            Rep.AppendLine($"WARN  [{Id}] {what}");
            Warned++;
            PerCheck.TryGetValue(Id, out var v);
            PerCheck[Id] = (v.pass, v.fail, v.warn + 1);
        }

        public void Info(string line) => Rep.AppendLine("   " + line);

        public static string Short(string s) => s == null ? "null" : s.Length > 300 ? s.Substring(0, 300) + "…" : s;

        /// <summary>The "SUMMARY: N PASS, M FAIL (K WARN, S s)" line.</summary>
        public string SummaryLine(double seconds) => $"SUMMARY: {Passed} PASS, {Failed} FAIL ({Warned} WARN, {seconds:F0} s)";

        /// <summary>
        /// The report: the summary line and the counts per check id first, then <paramref name="preamble"/>, the check lines and
        /// <paramref name="trailer"/>, and the summary line again as the last line (run_tests reads the first summary line,
        /// the player-checks convention the last).
        /// </summary>
        public string Report(double seconds, string trailer = null, string preamble = null)
        {
            var sum = new StringBuilder();
            sum.AppendLine(SummaryLine(seconds));
            foreach (var kv in PerCheck) sum.AppendLine($"  {kv.Key}: {kv.Value.pass} pass, {kv.Value.fail} fail{(kv.Value.warn > 0 ? ", " + kv.Value.warn + " warn" : "")}");
            if (!string.IsNullOrEmpty(preamble)) sum.AppendLine(preamble);
            sum.Append(Rep);
            if (!string.IsNullOrEmpty(trailer)) sum.AppendLine(trailer);
            sum.AppendLine(SummaryLine(seconds));
            return sum.ToString();
        }

        public void Dispose() => Client?.Dispose();
    }

    public static partial class BridgeAcceptance
    {
        /// <summary>Face random seed of the determinism checks (C13, S5): two characters draw the same blink / mouth sequence.</summary>
        public const int FaceSeed = 0x2a4b;

        /// <summary>
        /// Pixel comparison of two PNGs of the same size (render parity, bridge prep T5): the pixels whose RGB differ, the
        /// largest channel difference (of 255) and the mean absolute channel difference; null when a file is missing,
        /// unreadable or the sizes differ (<paramref name="why"/> says which).
        /// </summary>
        public static (int w, int h, long differing, int maxDiff, double meanAbs)? PngDiff(string a, string b, out string why)
        {
            why = null;
            if (a == null || !System.IO.File.Exists(a)) { why = "missing " + (a ?? "(null)"); return null; }
            if (b == null || !System.IO.File.Exists(b)) { why = "missing " + (b ?? "(null)"); return null; }
            var ta = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            var tb = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!ta.LoadImage(System.IO.File.ReadAllBytes(a)) || !tb.LoadImage(System.IO.File.ReadAllBytes(b))) { why = "unreadable PNG"; return null; }
                if (ta.width != tb.width || ta.height != tb.height) { why = $"sizes differ: {ta.width}x{ta.height} vs {tb.width}x{tb.height}"; return null; }
                var pa = ta.GetPixels32();
                var pb = tb.GetPixels32();
                long differing = 0, sum = 0;
                int max = 0;
                for (int i = 0; i < pa.Length; i++)
                {
                    int dr = Math.Abs(pa[i].r - pb[i].r), dg = Math.Abs(pa[i].g - pb[i].g), db = Math.Abs(pa[i].b - pb[i].b);
                    int m = Math.Max(dr, Math.Max(dg, db));
                    if (m > 0) differing++;
                    if (m > max) max = m;
                    sum += dr + dg + db;
                }
                return (ta.width, ta.height, differing, max, sum / (3.0 * pa.Length));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(ta);
                UnityEngine.Object.DestroyImmediate(tb);
            }
        }

        /// <summary>Runs a part, flattening nested enumerators; an exception inside it is a FAIL of that part, the run goes on.</summary>
        public static IEnumerator Guard(AcceptanceCtx c, string id, IEnumerator part)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(part);
            while (stack.Count > 0)
            {
                var top = stack.Peek();
                bool more;
                try { more = top.MoveNext(); }
                catch (Exception e)
                {
                    c.Id = id;
                    c.Check(false, "exception", e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace);
                    yield break;
                }
                if (!more) { stack.Pop(); continue; }
                if (top.Current is IEnumerator nested) { stack.Push(nested); continue; }
                yield return top.Current;
            }
        }

        // ================================================================ S1–S6 (AutoStep)

        static double Dbl(JToken t) => t == null || t.Type == JTokenType.Null ? double.NaN : t.Value<double>();

        /// <summary>chara.motion {key} and scene in one batch (same frame) → into[0] = the motion reply, into[1] = the scene object.</summary>
        static IEnumerator ReadMotion(AcceptanceCtx c, string key, JObject[] into)
        {
            yield return c.Call(new { cmd = "batch", cmds = new object[] { new { cmd = "chara.motion", key }, new { cmd = "scene" } } });
            var res = c.J["results"] as JArray;
            into[0] = res?[0] as JObject;
            into[1] = (res?[1]?["objects"] as JArray)?.OfType<JObject>().FirstOrDefault(o => (string)o["key"] == key);
            if (!c.Last.Ok || into[0] == null || into[0].Value<bool?>("ok") != true) c.Check(false, "read chara.motion + scene", AcceptanceCtx.Short(c.Last?.ToString()));
        }

        static IEnumerator Interval(AcceptanceCtx c, string key, int frames, Action<JObject, JObject, JObject, JObject> check)
        {
            var a = new JObject[2];
            var b = new JObject[2];
            yield return ReadMotion(c, key, a);
            yield return c.Frames(frames);
            yield return ReadMotion(c, key, b);
            if (a[0] != null && b[0] != null) check(a[0], b[0], a[1], b[1]);
        }

        /// <summary>S1–S6 (the AutoStep test) on the acceptance character <see cref="AcceptanceCtx.FemaleKey"/>.</summary>
        public static IEnumerator AutoStep(AcceptanceCtx c)
        {
            string key = c.FemaleKey;
            if (key == null) { c.Id = "S1"; c.Warn("no acceptance character: S1–S6 skipped"); yield break; }
            float oldCapture = Time.captureDeltaTime;
            Time.captureDeltaTime = 1f / 60f;
            try
            {
                // ---- S1
                c.Id = "S1";
                yield return c.Call(new
                {
                    cmd = "batch",
                    cmds = new object[]
                    {
                        new { cmd = "chara.motion", key, autoStep = true, paused = false, timeScale = 1f },
                        new { cmd = "chara.anime", key, group = 19, category = 0, no = 0, speed = 1f },
                    },
                });
                c.CheckOk("chara.motion {autoStep, not paused, timeScale 1} + chara.anime Idle_00 speed 1");
                yield return c.Frames(2);
                yield return Interval(c, key, 60, (m1, m2, o1, o2) =>
                {
                    int df = (int)m2["frame"] - (int)m1["frame"], ds = (int)m2["stepCount"] - (int)m1["stepCount"];
                    double dt = Dbl(m2["time"]) - Dbl(m1["time"]);
                    c.Check(df > 0 && ds == df, "Δ stepCount = Δf", $"Δf {df}, Δsteps {ds}");
                    c.Check(Math.Abs(dt - df / 60.0) <= 1e-5, "Δ time = Δf / 60 ± 1e-5", $"Δtime {dt:R}, expected {df / 60.0:R}");
                    double len = Dbl(o2?["anime"]?["length"]);
                    double dn = Dbl(o2?["anime"]?["normalizedTime"]) - Dbl(o1?["anime"]?["normalizedTime"]);
                    c.Check(len > 0 && Math.Abs(dn - df / 60.0 / len) <= 1e-3, "Δ normalizedTime = Δf / 60 / length ± 1e-3", $"Δ {dn:R}, expected {df / 60.0 / len:R} (length {len:R})");
                });

                // ---- S2
                c.Id = "S2";
                yield return c.Call(new { cmd = "chara.motion", key, paused = true });
                c.CheckOk("chara.motion {paused true}");
                yield return Interval(c, key, 60, (m1, m2, o1, o2) =>
                {
                    int df = (int)m2["frame"] - (int)m1["frame"], ds = (int)m2["stepCount"] - (int)m1["stepCount"];
                    c.Check(Dbl(m2["time"]) == Dbl(m1["time"]), "Δ time = 0", $"{m1["time"]} → {m2["time"]}");
                    c.Check(Dbl(o2?["anime"]?["normalizedTime"]) == Dbl(o1?["anime"]?["normalizedTime"]), "the normalized time is unchanged",
                        $"{o1?["anime"]?["normalizedTime"]} → {o2?["anime"]?["normalizedTime"]}");
                    c.Check(df > 0 && ds == df, "Δ stepCount = Δf (paused steps run with dt 0)", $"Δf {df}, Δsteps {ds}");
                });

                // ---- S3
                c.Id = "S3";
                yield return c.Call(new { cmd = "chara.motion", key, paused = false, timeScale = 0.5f });
                c.CheckOk("chara.motion {paused false, timeScale 0.5}");
                yield return Interval(c, key, 60, (m1, m2, o1, o2) =>
                {
                    int df = (int)m2["frame"] - (int)m1["frame"];
                    double dt = Dbl(m2["time"]) - Dbl(m1["time"]);
                    c.Check(df > 0 && Math.Abs(dt - 0.5 * df / 60.0) <= 1e-5, "Δ time = 0.5 × Δf / 60 ± 1e-5", $"Δf {df}, Δtime {dt:R}, expected {0.5 * df / 60.0:R}");
                });

                // ---- S4
                c.Id = "S4";
                yield return c.Call(new { cmd = "chara.motion", key, autoStep = false, timeScale = 1f });
                c.CheckOk("chara.motion {autoStep false}");
                yield return Interval(c, key, 60, (m1, m2, o1, o2) =>
                {
                    int df = (int)m2["frame"] - (int)m1["frame"], ds = (int)m2["stepCount"] - (int)m1["stepCount"];
                    c.Check(df > 0 && ds == 0, "Δ stepCount = 0", $"Δf {df}, Δsteps {ds}");
                });

                // ---- S5
                c.Id = "S5";
                int? seed = FaceSetup.Seed;
                FaceSetup.Seed = FaceSeed;
                try
                {
                    yield return c.Call(new
                    {
                        cmd = "batch",
                        cmds = new object[]
                        {
                            new Dictionary<string, object> { ["cmd"] = "chara.load", ["path"] = c.FemaleRel, ["sex"] = "female", ["as"] = "a" },
                            new Dictionary<string, object> { ["cmd"] = "chara.load", ["path"] = c.FemaleRel, ["sex"] = "female", ["as"] = "b" },
                            new Dictionary<string, object> { ["cmd"] = "chara.motion", ["key"] = "$a.added.0.key", ["autoStep"] = false, ["reset"] = true },
                            new Dictionary<string, object> { ["cmd"] = "chara.motion", ["key"] = "$b.added.0.key", ["autoStep"] = false, ["reset"] = true },
                            new Dictionary<string, object> { ["cmd"] = "chara.anime", ["key"] = "$a.added.0.key", ["group"] = 19, ["category"] = 0, ["no"] = 0, ["time"] = 0, ["speed"] = 1 },
                            new Dictionary<string, object> { ["cmd"] = "chara.anime", ["key"] = "$b.added.0.key", ["group"] = 19, ["category"] = 0, ["no"] = 0, ["time"] = 0, ["speed"] = 1 },
                        },
                    }, 600);
                }
                finally { FaceSetup.Seed = seed; }
                c.CheckOk("batch: two fresh characters of the acceptance card, autoStep off and reset before any frame, Idle_00 at 0");
                string ka = (string)c.J["results"]?[0]?["added"]?[0]?["key"], kb = (string)c.J["results"]?[1]?["added"]?[0]?["key"];
                if (ka == null || kb == null) { c.Check(false, "both characters added", AcceptanceCtx.Short(c.Last?.ToString())); }
                else
                {
                    yield return c.Call(new { cmd = "chara.motion", key = ka, autoStep = true });
                    c.CheckOk("A: chara.motion {autoStep true}");
                    int s0 = c.J.Value<int?>("stepCount") ?? -1;
                    yield return c.Frames(60);
                    yield return c.Call(new { cmd = "chara.motion", key = ka, autoStep = false });
                    c.CheckOk("A: chara.motion {autoStep false}");
                    int n = (c.J.Value<int?>("stepCount") ?? -1) - s0;
                    c.Check(n >= 50, "A auto-stepped about 60 frames", "" + n);
                    yield return c.Call(new { cmd = "chara.motion", key = kb, step = new { dt = 1f / 60f, n } });
                    c.CheckOk("B: chara.motion {step {dt 1/60, n " + n + "}}");
                    c.Check((c.J.Value<int?>("stepCount") ?? -2) == n, "B stepped the same count", "" + c.J.Value<int?>("stepCount"));
                    yield return c.Call(new { cmd = "bones.dump", key = ka, root = "all" });
                    var da = c.J;
                    yield return c.Call(new { cmd = "bones.dump", key = kb, root = "all" });
                    var db = c.J;
                    var ba = da["bones"] as JArray;
                    var bb = db["bones"] as JArray;
                    int count = Math.Min(ba?.Count ?? 0, bb?.Count ?? 0), different = 0;
                    var first = new List<string>();
                    for (int i = 0; i < count; i++)
                    {
                        if (ba[i].ToString(Formatting.None) == bb[i].ToString(Formatting.None)) continue;
                        different++;
                        if (first.Count < 8) first.Add((string)ba[i]["path"]);
                    }
                    c.Check(ba != null && bb != null && ba.Count == bb.Count && ba.Count > 700 && different == 0, "bones.dump all of A (AutoStep) = B (manual steps) value for value",
                        $"{ba?.Count} / {bb?.Count} entries, {different} different" + (first.Count > 0 ? ": " + string.Join(", ", first) : ""));
                    yield return c.Call(new { cmd = "delete", keys = new[] { ka, kb } });
                    c.CheckOk("delete the two S5 characters");
                }

                // ---- S6
                c.Id = "S6";
                yield return c.Call(new { cmd = "chara.motion", key, paused = true });
                c.CheckOk("chara.motion {paused true}");
                yield return c.Call(new { cmd = "app.options", rebuild = true }, 600);
                c.CheckOk("app.options {rebuild: true}");
                yield return c.Frames(2);
                yield return c.Call(new { cmd = "chara.motion", key });
                c.CheckOk("chara.motion after the rebuild");
                c.Check(c.J.Value<bool?>("paused") == true && c.J.Value<bool?>("autoStep") == false, "the rebuilt character reports paused true (and autoStep false)", AcceptanceCtx.Short(c.Last?.ToString()));
            }
            finally { Time.captureDeltaTime = oldCapture; }
        }

        // ================================================================ T5 (render parity shots)

        /// <summary>
        /// The captures of <see cref="Parity"/> in order: name, the capture's <c>aa</c> (null = the default SMAA), whether the
        /// map is loaded, and why a player-vs-Editor difference is expected (null = none: the comparison is judged).
        /// </summary>
        public static readonly (string name, string aa, bool map, string known)[] ParityShots =
        {
            ("parity_map", null, true, null),
            ("parity_map_msaa4", "msaa4", true, null),
            ("parity_nomap", null, false,
             "known: without a map the start scene's ambient probe (ambient mode Skybox, no baked lighting data) is computed from the skybox by the Editor and taken from the scene in a player, so the character is lit differently"),
        };

        /// <summary>
        /// Render-parity shots in a fixed state (bridge prep T5, run after E1–E3 by both hosts; the player compares its shots
        /// with the Editor's, PlayerAcceptance -acceptCompare): a new scene with map 10, a fresh character of the acceptance
        /// card loaded with the fixed face seed and, in the same batch before its first frame, paused with physics and look
        /// off, Idle_00 at normalized time 0.5 with speed 0, a fixed camera (target [0, 10, 0], rot [5, 180, 0], dist 28,
        /// fov 23); captures parity_map (SMAA) and parity_map_msaa4 (MSAA 4), then without the map parity_nomap (SMAA). Unlike
        /// A9 / A14 (paused at whatever time the frames reached), nothing here depends on frame timing.
        /// </summary>
        public static IEnumerator Parity(AcceptanceCtx c)
        {
            c.Id = "T5";
            yield return c.Call(new { cmd = "scene.new" });
            c.CheckOk("scene.new");
            yield return c.Call(new { cmd = "map.load", no = 10 }, 180);
            c.Check(c.Last != null && c.Last.Ok && (string)c.J["state"] == "Loaded", "map.load 10", c.Last != null && c.Last.Ok ? "" : AcceptanceCtx.Short(c.Last?.ToString()));
            int? seed = FaceSetup.Seed;
            FaceSetup.Seed = FaceSeed;
            try
            {
                yield return c.Call(new
                {
                    cmd = "batch",
                    cmds = new object[]
                    {
                        new Dictionary<string, object> { ["cmd"] = "chara.load", ["path"] = c.FemaleRel, ["sex"] = "female", ["as"] = "p" },
                        new Dictionary<string, object> { ["cmd"] = "chara.motion", ["key"] = "$p.added.0.key", ["paused"] = true, ["physics"] = false, ["look"] = false },
                        new Dictionary<string, object> { ["cmd"] = "chara.anime", ["key"] = "$p.added.0.key", ["group"] = 19, ["category"] = 0, ["no"] = 0, ["time"] = 0.5, ["speed"] = 0 },
                        new Dictionary<string, object> { ["cmd"] = "camera.set", ["target"] = new[] { 0, 10, 0 }, ["rot"] = new[] { 5, 180, 0 }, ["dist"] = 28, ["fov"] = 23 },
                    },
                }, 600);
            }
            finally { FaceSetup.Seed = seed; }
            if (!c.CheckOk("batch: a fresh character (fixed face seed) paused with physics and look off before its first frame, Idle_00 at 0.5, a fixed camera")) yield break;
            yield return c.Frames(3);
            bool mapLoaded = true;
            foreach (var (name, aa, map, _) in ParityShots)
            {
                if (!map && mapLoaded)
                {
                    mapLoaded = false;
                    yield return c.Call(new { cmd = "map.load", clear = true });
                    c.CheckOk("map.load clear");
                    yield return c.Frames(3);
                }
                yield return c.Call(aa == null ? (object)new { cmd = "capture", name } : new { cmd = "capture", name, aa });
                string path = (string)c.J["path"];
                if (c.CheckOk("capture " + name + (aa != null ? " (aa " + aa + ")" : "")))
                {
                    var (w, h, fg, _) = Png(path, null);
                    c.Check(w == 1280 && h == 720 && fg >= 0.01, $"{name}: PNG 1280 × 720 with a foreground ≥ 1 %", $"{w} × {h}, foreground {fg:P1}");
                    c.Info($"{name}: {path}");
                }
            }
        }

        // ================================================================ E1–E3 (errors and load)

        /// <summary>E1–E3, each guarded.</summary>
        public static IEnumerator Errors(AcceptanceCtx c)
        {
            yield return Guard(c, "E1", E1(c));
            yield return Guard(c, "E2", E2(c));
            yield return Guard(c, "E3", E3(c));
        }

        /// <summary>E1: a POST with a browser Origin header → 403 from the bridge, no side effect.</summary>
        static IEnumerator E1(AcceptanceCtx c)
        {
            c.Id = "E1";
            yield return c.Call(new { cmd = "history" });
            int before = (c.J["undo"] as JArray)?.Count ?? -1;
            string key = c.FemaleKey;
            var req = key != null ? (object)new { cmd = "obj.transform", key, pos = new[] { 7, 0, 7 } } : new { cmd = "scene.new" };
            yield return c.Call(req, 30, new Dictionary<string, string> { ["Origin"] = "http://example.com" });
            c.Check(c.Last != null && c.Last.Status == 403, "Origin: http://example.com → HTTP 403", c.Last?.ToString());
            yield return c.Call(new { cmd = "history" });
            c.Check(((c.J["undo"] as JArray)?.Count ?? -2) == before, "no side effect (history unchanged)", before + " → " + ((c.J["undo"] as JArray)?.Count ?? -2));
            yield return c.Call(req, 30, new Dictionary<string, string> { ["Sec-Fetch-Site"] = "cross-site" });
            c.Check(c.Last != null && c.Last.Status == 403, "Sec-Fetch-Site: cross-site → HTTP 403", c.Last?.ToString());
        }

        /// <summary>E2: 1,000 sequential pings, then 4 concurrent clients × 50 scene requests: no lost or mixed replies (id echoed).</summary>
        static IEnumerator E2(AcceptanceCtx c)
        {
            c.Id = "E2";
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var seq = System.Threading.Tasks.Task.Run(async () =>
            {
                int ok = 0, mixed = 0;
                for (int i = 0; i < 1000; i++)
                {
                    var r = await c.Client.Post("{\"cmd\":\"ping\",\"id\":" + i + "}", null, 30).ConfigureAwait(false);
                    if (r.Ok) ok++;
                    if (r.Json?.Value<int?>("id") != i) mixed++;
                }
                return (ok, mixed);
            });
            while (!seq.IsCompleted) yield return null;
            var (pok, pmixed) = seq.Result;
            c.Check(pok == 1000 && pmixed == 0, $"1,000 sequential pings: every reply ok with its id ({sw.Elapsed.TotalSeconds:F1} s)", pok + " ok, " + pmixed + " mixed");
            sw.Restart();
            var clients = new List<System.Threading.Tasks.Task<(int ok, int mixed)>>();
            for (int k = 0; k < 4; k++)
            {
                int kk = k;
                clients.Add(System.Threading.Tasks.Task.Run(async () =>
                {
                    using (var own = new BridgeClient(c.Url, c.Client.Token))
                    {
                        int ok = 0, mixed = 0;
                        for (int i = 0; i < 50; i++)
                        {
                            string id = "c" + kk + "-" + i;
                            var r = await own.Post("{\"cmd\":\"scene\",\"id\":\"" + id + "\"}", null, 60).ConfigureAwait(false);
                            if (r.Ok && r.Json["objects"] is JArray) ok++;
                            if (r.Json?.Value<string>("id") != id) mixed++;
                        }
                        return (ok, mixed);
                    }
                }));
            }
            while (clients.Any(t => !t.IsCompleted)) yield return null;
            int sok = clients.Sum(t => t.Result.ok), smixed = clients.Sum(t => t.Result.mixed);
            c.Check(sok == 200 && smixed == 0, $"4 concurrent clients × 50 scene requests: every reply ok with its id ({sw.Elapsed.TotalSeconds:F1} s)", sok + " ok, " + smixed + " mixed");
        }

        /// <summary>E3: scene requests sent while a map.load waits are answered after it (FIFO); a ping meanwhile at once.</summary>
        static IEnumerator E3(AcceptanceCtx c)
        {
            c.Id = "E3";
            yield return c.Call(new { cmd = "map.load", clear = true });
            c.CheckOk("map.load clear");
            var clock = System.Diagnostics.Stopwatch.StartNew();
            System.Threading.Tasks.Task<double> Timed(string json) => System.Threading.Tasks.Task.Run(async () =>
            {
                using (var own = new BridgeClient(c.Url, c.Client.Token))
                {
                    var r = await own.Post(json, null, 300).ConfigureAwait(false);
                    return r.Ok ? clock.Elapsed.TotalMilliseconds : -1;
                }
            });
            var map = Timed("{\"cmd\":\"map.load\",\"no\":10}");
            // wait until the map.load is the queue's (deferred) head: separate connections may arrive in any order
            var wait = System.Diagnostics.Stopwatch.StartNew();
            int queued = 0;
            while (!map.IsCompleted && queued < 1 && wait.Elapsed.TotalSeconds < 10)
            {
                yield return c.Call(new { cmd = "ping" });
                queued = c.J.Value<int?>("queued") ?? 0;
            }
            c.Check(queued >= 1 || map.IsCompleted, "the map.load is queued (ping.queued " + queued + ")");
            var scene = Timed("{\"cmd\":\"scene\"}");
            var ping = Timed("{\"cmd\":\"ping\"}");
            var frameTimes = new List<double>();
            while (!map.IsCompleted || !scene.IsCompleted || !ping.IsCompleted)
            {
                frameTimes.Add(Time.unscaledDeltaTime * 1000.0);
                yield return null;
            }
            double tm = map.Result, ts = scene.Result, tp = ping.Result;
            // These replies carry no frame number, so the times are the clients': two replies of one frame can finish here in either
            // order. The tolerance is one frame's time: the median of this wait, at least 1/60 s (a map's load makes single frames long).
            frameTimes.Sort();
            double frameMs = Math.Max(1000.0 / 60.0, frameTimes.Count > 0 ? frameTimes[frameTimes.Count / 2] : 0);
            c.Info($"map.load done at {tm:F0} ms, scene at {ts:F0} ms, ping at {tp:F0} ms; scene - map.load {ts - tm:F3} ms, one frame {frameMs:F1} ms");
            c.Check(tm > 0 && ts > 0 && tp > 0, "all three answered ok");
            c.Check(ts >= tm - frameMs, "the scene request is answered after the waiting map.load (FIFO, within one frame's time)", ts + " vs " + tm + ", one frame " + frameMs);
            c.Check(tp < tm, "the ping is answered at once (before the map.load completes)", tp + " vs " + tm);
        }
    }
}
