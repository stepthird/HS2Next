// Batch test of the Studio item motion (N1–N8; package B of Phase 3.1). N9 (frame order in
// play mode) is Editor/ItemMotionTest_Play.cs.
// Run: -executeMethod HS2Next.Scenes.Runtime.Tests.ItemMotionTest.Run [-only N1,N5] (headless, -nographics, edit mode,
//      ManualStepping: SceneRuntime.Tick steps the characters, then the items).
//   N1  FK capture: new 1398 / 1399 (vanilla FK items): item.fk.bones = the prefab's local rotation of every found bone
//       (the stage's base, and package C's expectation file when present)
//   N2  FK apply: values + fk.enabled → every bone localRotation = Euler(value) after a step; off → the base exactly; an
//       entry removed from the map → the base while on
//   N3  FK vs animation: the mod FK item with a driven Animator (C's meta.targets fkAnimated): FK wins every step; off → the
//       animated pose of a twin stepped alike at the next step
//   N4  FK vs dynamic bones: the mod FK item with dynamic bones (fkDynamicBones): FK on → chains inactive, components
//       disabled, bones at the FK values, the other chain bones at their Start pose; off → chains active again with the
//       particles reset at the bones
//   N5  animation: 1022 鳥 (anime patterns) and 957 回転（obj無し） (isAnime, no patterns): pattern change plays the state, the
//       same state continues, speed 0 freezes, time jumps, restart → 0, capture equals the driver's time, save → load
//       restores it, save → load → save byte-identical without stepping
//   N6  determinism: two scenes with the same items stepped 120 frames by hand: every transform bit-equal
//   N7  dynamic bones: 498 猫じゃらし at a character's N_Hand_R: the chain root particle is at the hand's pose of the same
//       frame after every tick (items step after characters), the chain moves when the hand moves, dynamicBones false →
//       Start pose
//   N8  plane colliders / FreezeAxis: synthetic chains (default options: bit-equal to a run without them; IntendedBehaviour:
//       the plane keeps particles on its positive side, FreezeAxis X keeps them in the parent's plane) and the FreezeAxis
//       mod items of the census (hooh.props.minimod_april #1, #4: bit-equal to a twin with FreezeAxis 0). The only
//       plane-collider item of the census is an adult item: not built (content rule), covered by the synthetic chains.
// Report: Spike/out/items/item_motion_report.txt, first line "SUMMARY: N PASS, M FAIL"; EditorApplication.Exit(1) on a FAIL.
// Numeric checks only; no image is rendered or viewed.
namespace HS2Next.Scenes.Runtime.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.IO.Compression;
    using System.Linq;
    using System.Text;
    using HS2Next.Content;
    using HS2Next.Content.Studio;
    using HS2Next.Motion;
    using HS2Next.Motion.DynamicBones;
    using HS2Next.Save;
    using HS2Next.Scenes;
    using HS2Next.Scenes.Runtime;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using UnityEditor;
    using UnityEngine;
    using Debug = UnityEngine.Debug;
    using Object = UnityEngine.Object;

    public static partial class ItemMotionTest
    {
        public const float Dt = 1f / 60f;
        public static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "items");
        public const string ReportFile = "item_motion_report.txt";

        /// <summary>Package C's meta.targets (Cache/items/expect_*.json.gz), used when the file is absent.</summary>
        public const string FkAnimatedRef = "kky.Studio.OnTheMoveCyber3#14";
        public const string FkDynamicBonesRef = "kky.AIStudio.JamSession#7";
        public static readonly string[] FreezeAxisRefs = { "hooh.props.minimod_april#1", "hooh.props.minimod_april#4" };

        // ================================================================ report

        static StringBuilder rep;
        static int passes, fails, warns;
        static readonly Dictionary<string, (int pass, int fail, int warn)> perTest = new Dictionary<string, (int, int, int)>();
        static string current = "setup";
        static readonly List<string> exceptions = new List<string>();
        static string[] only;

        static bool Check(string name, bool ok, string detail = "")
        {
            rep.AppendLine($"{(ok ? "PASS" : "FAIL")}  {current} {name}{(string.IsNullOrEmpty(detail) ? "" : "  " + detail)}");
            if (ok) passes++; else fails++;
            perTest.TryGetValue(current, out var c);
            perTest[current] = ok ? (c.pass + 1, c.fail, c.warn) : (c.pass, c.fail + 1, c.warn);
            return ok;
        }

        static void Warn(string what)
        {
            rep.AppendLine($"WARN  {current} {what}");
            warns++;
            perTest.TryGetValue(current, out var c);
            perTest[current] = (c.pass, c.fail, c.warn + 1);
        }

        static void Info(string s) => rep.AppendLine("   " + s);
        static bool Want(string id) => only == null || only.Contains(id);

        static void Guard(string id, string title, Action a)
        {
            if (!Want(id)) return;
            current = id;
            rep.AppendLine();
            rep.AppendLine("== " + id + " " + title);
            int ex0 = exceptions.Count;
            var sw = Stopwatch.StartNew();
            try { a(); }
            catch (Exception e) { current = id; Check("(exception)", false, e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); }
            if (exceptions.Count > ex0)
            {
                current = id;
                Check("no exception logged", false, $"{exceptions.Count - ex0}: " + string.Join(" | ", exceptions.Skip(ex0).Take(3)));
            }
            Info($"{id}: {sw.Elapsed.TotalSeconds:F1} s");
        }

        static void OnLog(string condition, string stack, LogType type)
        {
            if (type == LogType.Exception) exceptions.Add(condition.Length > 300 ? condition.Substring(0, 300) : condition);
        }

        static string[] OnlyArgs()
        {
            var a = TestArgs.Get();
            int i = Array.IndexOf(a, "-only");
            if (i < 0) return null;
            var l = new List<string>();
            for (int k = i + 1; k < a.Length && !a[k].StartsWith("-", StringComparison.Ordinal); k++)
                l.AddRange(a[k].Split(',', '+', ';').Select(x => x.Trim()).Where(x => x.Length > 0));
            return l.ToArray();
        }

        static string F(float f) => f.ToString("G6", System.Globalization.CultureInfo.InvariantCulture);
        static string F(Vector3 v) => "(" + F(v.x) + ", " + F(v.y) + ", " + F(v.z) + ")";

        /// <summary>Largest component difference of two rotations (q and −q are the same rotation).</summary>
        static float QErr(Quaternion a, Quaternion b)
        {
            float d1 = Mathf.Max(Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y)), Mathf.Max(Mathf.Abs(a.z - b.z), Mathf.Abs(a.w - b.w)));
            float d2 = Mathf.Max(Mathf.Max(Mathf.Abs(a.x + b.x), Mathf.Abs(a.y + b.y)), Mathf.Max(Mathf.Abs(a.z + b.z), Mathf.Abs(a.w + b.w)));
            return Mathf.Min(d1, d2);
        }

        static bool Same(Quaternion a, Quaternion b) => a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;
        static bool Same(Vector3 a, Vector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;

        // ================================================================ fixture

        /// <summary>A session and a runtime (no drivers) on the shared services; ManualStepping on.</summary>
        public sealed class Scene : IDisposable
        {
            public SceneSession Session;
            public SceneRuntime Runtime;

            public Scene(RuntimeServices services, ulong seed = 0x17e3a0710e5UL)
            {
                Session = global::SceneRuntimeTest.NewSession(services, seed);
                Runtime = new SceneRuntime(Session, services, null, false);
            }

            public string Add(int no, string parent = null, string point = null) => Add(ContentId.Vanilla(no), parent, point);

            public string Add(ContentId id, string parent = null, string point = null)
            {
                var r = Runtime.Ops.AddItem(id, parent, point);
                Runtime.Apply();
                if (r == null || r.Status == CommandStatus.Error) { Check("add item " + id, false, r?.ToString() ?? "null"); return null; }
                return r.NewId;
            }

            public string Add(string reference, string parent = null) =>
                ContentId.TryParse(reference, out var id) && !id.IsEmpty ? Add(id, parent) : null;

            public ItemRealization Item(string id) => id == null ? null : Runtime.FindItem(id);

            /// <summary>Realization state, row and the node's findings (for FAIL details).</summary>
            public string Describe(string id)
            {
                var it = Item(id);
                var f = Session.Findings.Where(x => x.NodeId == id).Select(x => x.Code + ": " + x.Message);
                return $"{id}: {it?.State} {it?.Detail} row {it?.Row} fallback {it?.IsFallback} animator {(it != null && it.Animator ? it.Animator.name : "-")} " +
                       $"isAnime {it?.Component?.isAnime} motion {ItemMotion.Of(it) != null}; findings [{string.Join(" | ", f)}]";
            }
            public ItemMotion Motion(string id) => ItemMotion.Of(Item(id));
            public ItemNodeState State(string id) => Session.Model.Find(id)?.State as ItemNodeState;

            public CommandResult Set(string id, string path, object value, bool apply = true)
            {
                var r = Session.Set(id, path, value);
                if (r == null || r.Status == CommandStatus.Error) Check("set " + path, false, r?.ToString() ?? "null");
                if (apply) Runtime.Apply();
                return r;
            }

            public void Tick(int n, float dt = Dt) { for (int i = 0; i < n; i++) Runtime.Tick(dt); }

            public void Dispose()
            {
                Runtime?.Dispose();
                Runtime = null;
            }
        }

        static RuntimeServices services;

        static RuntimeServices Services(StringBuilder head)
        {
            if (services != null) return services;
            // the AI install gate off: the targets 498 and 1022 are AI-content rows, which the reference HS2 install shows
            var gd = global::CardResolveTest.BuildDb(head);
            var db = gd.LoadStudioLists(new StudioLoadOptions { EmulateAIInstallGate = false });
            services = new RuntimeServices { Catalog = gd, Db = db, Hs2Root = global::CardResolveTest.GameRoot, Settings = new SceneRuntimeSettings { ManualStepping = true } };
            head.AppendLine("Studio lists loaded with the AI install gate off");
            return services;
        }

        // ================================================================ expectation file (package C)

        static JObject expectMeta;
        static readonly Dictionary<string, JObject> expectRows = new Dictionary<string, JObject>(StringComparer.Ordinal);

        /// <summary>Reads meta and the rows of <paramref name="refs"/> from Cache/items/expect_&lt;hash&gt;.json.gz; false without the file.</summary>
        static bool LoadExpect(IEnumerable<string> refs)
        {
            var root = global::CardResolveTest.GameRoot;
            string path = Path.Combine(ContentSettings.ProjectRoot, "Cache", "items", "expect_" + ZipmodSourceBase.PathHash(root) + ".json.gz");
            if (!File.Exists(path)) { Info("expectation file " + path + " not found"); return false; }
            var want = new HashSet<string>(refs, StringComparer.OrdinalIgnoreCase);
            var sw = Stopwatch.StartNew();
            using (var fs = File.OpenRead(path))
            using (var gz = new GZipStream(fs, CompressionMode.Decompress))
            using (var tr = new StreamReader(gz, Encoding.UTF8))
            using (var jr = new JsonTextReader(tr) { FloatParseHandling = FloatParseHandling.Double })
            {
                var all = JObject.Load(jr);
                expectMeta = all["meta"] as JObject;
                foreach (JObject it in (JArray)all["items"])
                {
                    string r = (string)it["ref"];
                    if (r != null && want.Contains(r) && !expectRows.ContainsKey(r.ToLowerInvariant())) expectRows[r.ToLowerInvariant()] = it;
                }
            }
            Info($"expectation file {Path.GetFileName(path)} read in {sw.ElapsedMilliseconds} ms ({expectRows.Count} rows used)");
            return true;
        }

        static string Target(string name, string fallback)
        {
            var t = (string)expectMeta?["targets"]?[name];
            return string.IsNullOrEmpty(t) ? fallback : t;
        }

        // ================================================================ entry

        public static void Run()
        {
            rep = new StringBuilder(); passes = fails = warns = 0; perTest.Clear(); exceptions.Clear();
            only = OnlyArgs();
            Directory.CreateDirectory(OutDir);
            Application.logMessageReceived += OnLog;
            var sw = Stopwatch.StartNew();
            var head = new StringBuilder();
            bool batch = Application.isBatchMode;
            try
            {
                var sv = Services(head);
                rep.AppendLine($"Item motion test (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType}); DynamicBoneSetup.Options: " +
                               $"frameLocked {DynamicBoneSetup.Options.FrameLocked}, intended {DynamicBoneSetup.Options.IntendedBehaviour}");
                rep.Append(head);
                current = "setup";
                LoadExpect(new[] { "illusion.hs2#1398", "illusion.hs2#1399", FkAnimatedRef, FkDynamicBonesRef });
                Guard("N1", "FK capture", () => N1(sv));
                Guard("N2", "FK apply", () => N2(sv));
                Guard("N3", "FK vs animation", () => N3(sv));
                Guard("N4", "FK vs dynamic bones", () => N4(sv));
                Guard("N5", "animation", () => N5(sv));
                Guard("N6", "determinism", () => N6(sv));
                Guard("N7", "dynamic bones on a character's hand", () => N7(sv));
                Guard("N8", "plane colliders / FreezeAxis", () => N8(sv));
            }
            catch (Exception e) { current = "run"; Check("run (exception)", false, e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); }
            finally
            {
                Application.logMessageReceived -= OnLog;
                var sum = new StringBuilder();
                sum.AppendLine($"SUMMARY: {passes} PASS, {fails} FAIL ({warns} WARN, {sw.Elapsed.TotalSeconds:F0} s)");
                foreach (var kv in perTest.OrderBy(k => k.Key, StringComparer.Ordinal))
                    sum.AppendLine($"  {kv.Key}: {kv.Value.pass} pass, {kv.Value.fail} fail{(kv.Value.warn > 0 ? ", " + kv.Value.warn + " warn" : "")}");
                rep.Insert(0, sum.ToString());
                File.WriteAllText(Path.Combine(OutDir, ReportFile), rep.ToString(), new UTF8Encoding(false));
                try { services?.Catalog?.UnloadAll(); } catch (Exception) { }
                services = null;
                // the expectation file's rows (gigabytes as JSON objects) are not kept after the run
                expectMeta = null; expectRows.Clear();
                Debug.Log($"ITEM MOTION TEST DONE: {passes} PASS, {fails} FAIL, {warns} WARN");
            }
            if (batch) TestExit.Exit(fails > 0 ? 1 : 0);
        }

        // ================================================================ N1 FK capture

        static void N1(RuntimeServices sv)
        {
            using (var s = new Scene(sv))
            {
                foreach (int no in new[] { 1398, 1399 })
                {
                    string id = s.Add(no);
                    var it = s.Item(id);
                    var m = s.Motion(id);
                    var st = s.State(id);
                    if (!Check($"{no}: built with an FK stage", it != null && it.State == RealizationState.Built && m?.Fk != null && m.Fk.Count > 0,
                            $"state {it?.State}, fk bones {m?.Fk?.Count}")) continue;
                    var names = m.Fk.Names;
                    bool keys = st.Fk.Bones.Count == names.Length && names.All(n => st.Fk.Bones.ContainsKey(n));
                    Check($"{no}: fk.bones has exactly the {names.Length} found bones, fk.enabled false", keys && !st.Fk.Enabled,
                          "stored [" + string.Join(", ", st.Fk.Bones.Keys) + "], found [" + string.Join(", ", names) + "]");
                    float worst = 0f;
                    for (int i = 0; i < names.Length; i++)
                    {
                        if (!st.Fk.Bones.TryGetValue(names[i], out var v)) continue;
                        worst = Mathf.Max(worst, Quaternion.Angle(Quaternion.Euler(v.X, v.Y, v.Z), m.Fk.Base[i]));
                    }
                    Check($"{no}: stored values = the prefab local rotation (stage base) within 1e-3°", worst <= 1e-3f, "max " + F(worst) + "°");
                    // package C's expectation (HS2's depth-first search, Unity eulerAngles of the prefab rotation)
                    if (expectRows.TryGetValue(("illusion.hs2#" + no).ToLowerInvariant(), out var row) && row["fk"] is JArray fk)
                    {
                        float w2 = 0f; int n = 0; var miss = new List<string>();
                        foreach (JObject b in fk)
                        {
                            if (b["found"]?.Value<bool>() != true) continue;
                            string name = (string)b["name"];
                            var e = (JArray)b["euler"];
                            if (!st.Fk.Bones.TryGetValue(name, out var v)) { miss.Add(name); continue; }
                            var qe = Quaternion.Euler((float)(double)e[0], (float)(double)e[1], (float)(double)e[2]);
                            w2 = Mathf.Max(w2, Quaternion.Angle(qe, Quaternion.Euler(v.X, v.Y, v.Z)));
                            n++;
                        }
                        Check($"{no}: stored values = the expectation file's fk euler ({n} bones) within 1e-3°", miss.Count == 0 && n > 0 && w2 <= 1e-3f,
                              "max " + F(w2) + "°" + (miss.Count > 0 ? ", missing " + string.Join(", ", miss) : ""));
                    }
                    else Warn($"{no}: no expectation row (package C's file absent?)");
                }
            }
        }

        // ================================================================ N2 FK apply

        static void N2(RuntimeServices sv)
        {
            using (var s = new Scene(sv))
            {
                string id = s.Add(1399);
                var m = s.Motion(id);
                if (!Check("1399: FK stage with bones", m?.Fk != null && m.Fk.Count >= 2, "bones " + m?.Fk?.Count)) return;
                var fk = m.Fk;
                var values = new Dictionary<string, Vector3>();
                for (int i = 0; i < fk.Count; i++) values[fk.Names[i]] = new Vector3(10f + 7f * i, -20f + 3f * i, 30f - 11f * i);
                foreach (var kv in values) s.Set(id, "item.fk.bones[" + FieldPath.Quote(kv.Key) + "]", UnityObjects.V(kv.Value), false);
                s.Set(id, "item.fk.enabled", true);
                s.Tick(1);
                float worst = 0f;
                for (int i = 0; i < fk.Count; i++) worst = Mathf.Max(worst, QErr(fk.Bones[i].localRotation, Quaternion.Euler(values[fk.Names[i]])));
                Check("FK on: every bone localRotation = Euler(value) after a step (≤ 1e-6 per component)", worst <= 1e-6f, "max " + worst.ToString("E2"));
                s.Tick(5);
                worst = 0f;
                for (int i = 0; i < fk.Count; i++) worst = Mathf.Max(worst, QErr(fk.Bones[i].localRotation, Quaternion.Euler(values[fk.Names[i]])));
                Check("FK on: still the values after 5 more steps", worst <= 1e-6f, "max " + worst.ToString("E2"));

                s.Set(id, "item.fk.enabled", false);
                bool exact = Enumerable.Range(0, fk.Count).All(i => Same(fk.Bones[i].localRotation, fk.Base[i]));
                Check("FK off: every bone back at its base rotation exactly (at once, HS2's OnDisable)", exact);
                s.Tick(3);
                exact = Enumerable.Range(0, fk.Count).All(i => Same(fk.Bones[i].localRotation, fk.Base[i]));
                Check("FK off: the base stays after 3 steps", exact);

                // an entry removed from the map → the base while on
                s.Set(id, "item.fk.enabled", true);
                var st = s.State(id);
                var map = new SortedDictionary<string, Vec3>(st.Fk.Bones, StringComparer.Ordinal);
                string removed = fk.Names[0];
                map.Remove(removed);
                s.Set(id, "item.fk.bones", map);
                s.Tick(2);
                bool baseRemoved = Same(fk.Bones[0].localRotation, fk.Base[0]);
                worst = 0f;
                for (int i = 1; i < fk.Count; i++) worst = Mathf.Max(worst, QErr(fk.Bones[i].localRotation, Quaternion.Euler(values[fk.Names[i]])));
                Check("entry \"" + removed + "\" removed while on: that bone at its base, the others at their values", baseRemoved && worst <= 1e-6f,
                      $"removed bone base {baseRemoved}, others max {worst:E2}");
                // undo restores the entry and FK applies it again
                var u = s.Session.Commands.Undo();
                s.Tick(1);
                Check("undo of the removal: the value applies again", u.Ok && QErr(fk.Bones[0].localRotation, Quaternion.Euler(values[removed])) <= 1e-6f, u.ToString());
            }
        }

        // ================================================================ N3 FK vs animation

        static void N3(RuntimeServices sv)
        {
            string target = Target("fkAnimated", FkAnimatedRef);
            using (var s = new Scene(sv))
            {
                string a = s.Add(target), b = s.Add(target);
                var ma = s.Motion(a); var mb = s.Motion(b);
                if (!Check(target + ": two instances with a driven Animator and FK bones", ma?.Animation != null && ma.Fk != null && mb?.Animation != null && mb.Fk != null,
                        $"A anim {ma?.Animation != null} fk {ma?.Fk?.Count}; B anim {mb?.Animation != null} fk {mb?.Fk?.Count}")) return;
                Info($"{target}: {s.Item(a)?.Row}; FK bones {ma.Fk.Count}, state {ma.Animation.StateName ?? "(" + ma.Animation.StateInfo.shortNameHash + ")"}");
                s.Tick(10);
                // FK values: the current pose + 15° about x (so FK differs from both the base and the animation)
                var fk = ma.Fk;
                var vals = new Vector3[fk.Count];
                for (int i = 0; i < fk.Count; i++)
                {
                    vals[i] = (fk.Bones[i].localRotation * Quaternion.Euler(15f, 0f, 0f)).eulerAngles;
                    s.Set(a, "item.fk.bones[" + FieldPath.Quote(fk.Names[i]) + "]", UnityObjects.V(vals[i]), false);
                }
                s.Set(a, "item.fk.enabled", true);
                float worst = 0f, moved = 0f;
                var prevB = fk.Bones.Select((t, i) => mb.Fk.Bones[i].localRotation).ToArray();
                for (int k = 0; k < 20; k++)
                {
                    s.Tick(1);
                    for (int i = 0; i < fk.Count; i++)
                    {
                        worst = Mathf.Max(worst, QErr(fk.Bones[i].localRotation, Quaternion.Euler(vals[i])));
                        moved = Mathf.Max(moved, Quaternion.Angle(prevB[i], mb.Fk.Bones[i].localRotation));
                        prevB[i] = mb.Fk.Bones[i].localRotation;
                    }
                }
                Check("FK on: the FK values win over the animation at every one of 20 steps (≤ 1e-6)", worst <= 1e-6f, "max " + worst.ToString("E2"));
                if (moved <= 1e-4f) Warn("the twin's FK bones did not move in 20 steps (max " + F(moved) + "°): the animation does not drive them");
                else Info("the twin's FK bones moved up to " + F(moved) + "° per step (the animation drives them)");
                Check("both instances advanced the same animation time", Mathf.Abs(ma.Animation.NormalizedTime - mb.Animation.NormalizedTime) <= 1e-6f,
                      $"A {ma.Animation.NormalizedTime} B {mb.Animation.NormalizedTime}");
                s.Set(a, "item.fk.enabled", false);
                s.Tick(1);
                float diff = 0f;
                for (int i = 0; i < fk.Count; i++) diff = Mathf.Max(diff, QErr(fk.Bones[i].localRotation, mb.Fk.Bones[i].localRotation));
                Check("FK off: at the next step every FK bone has the twin's animated pose (≤ 1e-5)", diff <= 1e-5f, "max " + diff.ToString("E2"));
            }
        }

        // ================================================================ N4 FK vs dynamic bones

        static List<(Transform t, Vector3 p, Quaternion r)> ChainPose(DynamicBoneStage stage)
        {
            var l = new List<(Transform, Vector3, Quaternion)>();
            foreach (var c in stage.Chains)
                for (int i = 0; i < c.ParticleCount; i++)
                {
                    var t = c.ParticleTransform(i);
                    if (t) l.Add((t, t.localPosition, t.localRotation));
                }
            return l;
        }

        static void N4(RuntimeServices sv)
        {
            string target = Target("fkDynamicBones", FkDynamicBonesRef);
            using (var s = new Scene(sv))
            {
                string id = s.Add(target);
                var m = s.Motion(id);
                if (!Check(target + ": FK bones and dynamic-bone chains", m?.Fk != null && m.Dynamic != null && m.Dynamic.Chains.Count > 0,
                        $"fk {m?.Fk?.Count}, chains {m?.Dynamic?.Chains.Count}")) return;
                Info($"{target}: {s.Item(id)?.Row}; FK bones {m.Fk.Count}, chains {m.Dynamic.Chains.Count}, particles {m.Dynamic.ParticleCount}");
                var start = ChainPose(m.Dynamic);
                var fkSet = new HashSet<Transform>(m.Fk.Bones);
                // move the item so the chains swing
                for (int k = 0; k < 30; k++)
                {
                    s.Set(id, "transform.pos", new Vec3(0.02f * k, 0f, 0.01f * k), false);
                    s.Tick(1);
                }
                int chains = m.Dynamic.Chains.Count(c => c.ParticleCount > 0);
                Check("FK off: every chain active", m.Dynamic.ActiveChains == chains && m.DynamicActive, $"{m.Dynamic.ActiveChains} of {chains}");
                // FK on through the capture rule (missing entries = base) — the bridge / UI transaction
                var vals = new Dictionary<string, Vector3>();
                for (int i = 0; i < m.Fk.Count; i++) vals[m.Fk.Names[i]] = (m.Fk.Base[i] * Quaternion.Euler(0f, 0f, 20f)).eulerAngles;
                foreach (var kv in vals) s.Set(id, "item.fk.bones[" + FieldPath.Quote(kv.Key) + "]", UnityObjects.V(kv.Value), false);
                s.Set(id, "item.fk.enabled", true);
                bool compsOff = m.Components.All(c => !c.enabled);
                Check("FK on: chains inactive, components disabled (HS2's ActiveFK)", m.Dynamic.ActiveChains == 0 && !m.DynamicActive && compsOff,
                      $"active chains {m.Dynamic.ActiveChains}, components off {compsOff}");
                s.Tick(10);
                float worst = 0f;
                for (int i = 0; i < m.Fk.Count; i++) worst = Mathf.Max(worst, QErr(m.Fk.Bones[i].localRotation, Quaternion.Euler(vals[m.Fk.Names[i]])));
                Check("FK on: the FK bones at their values after 10 steps", worst <= 1e-6f, "max " + worst.ToString("E2"));
                int off = 0, n = 0;
                foreach (var (t, p, r) in start)
                {
                    if (fkSet.Contains(t)) continue;
                    n++;
                    if (!Same(t.localPosition, p) || !Same(t.localRotation, r)) off++;
                }
                Check($"FK on: the {n} other chain bones at their Start pose", off == 0, $"{off} differ");
                s.Set(id, "item.fk.enabled", false);
                bool atBase = Enumerable.Range(0, m.Fk.Count).All(i => Same(m.Fk.Bones[i].localRotation, m.Fk.Base[i]));
                bool reset = true;
                foreach (var c in m.Dynamic.Chains)
                    for (int i = 0; i < c.ParticleCount; i++)
                    {
                        var t = c.ParticleTransform(i);
                        if (t && (c.Position(i) - t.position).sqrMagnitude > 1e-10f) reset = false;
                    }
                Check("FK off: FK bones at base, chains active again, particles reset at the bones (Start pose)",
                      atBase && m.Dynamic.ActiveChains == chains && m.DynamicActive && m.Components.All(c => c.enabled) && reset,
                      $"base {atBase}, active {m.Dynamic.ActiveChains} of {chains}, particles at bones {reset}");
                s.Tick(5);
                Check("FK off: chains keep stepping", m.Dynamic.ActiveChains == chains && m.Motion.Errors == 0, $"errors {m.Motion.Errors}");
            }
        }

        // ================================================================ N5 animation

        static SaveOptions FixedSave() => new SaveOptions { Clock = () => new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc), Thumbnail = ThumbnailMode.Placeholder, AppVersion = "0.1.0" };

        static void N5(RuntimeServices sv)
        {
            byte[] saved = null;
            float capturedTime = float.NaN;
            using (var s = new Scene(sv))
            {
                string bird = s.Add(1022);
                var m = s.Motion(bird);
                var a = m?.Animation;
                if (!Check("1022: driven Animator with valid anime patterns", a != null && a.Patterns.Count(p => p.valid) >= 2,
                        $"anim {a != null}, patterns {a?.Patterns.Count}; {s.Describe(bird)}")) return;
                Info("1022 patterns: " + string.Join(", ", a.Patterns.Select((p, i) => i + " " + p.name + " → " + p.state + (p.valid ? "" : " (invalid)"))));
                Check("1022: the Animator is enabled and its controller moved into the graph", a.Animator.enabled && a.Animator.runtimeAnimatorController == null);
                s.Tick(5);
                Check("pattern 0 (default state) advances", a.NormalizedTime > 0f, "time " + a.NormalizedTime);

                int p1 = Enumerable.Range(1, a.Patterns.Count - 1).First(i => a.Patterns[i].valid);
                s.Set(bird, "item.anime.pattern", p1);
                string st1 = a.Patterns[p1].state;
                Check($"pattern {p1}: plays \"{st1}\" from 0 (state hash)", a.StateName == st1 && a.StateInfo.shortNameHash == Animator.StringToHash(st1) && a.NormalizedTime == 0f,
                      $"state {a.StateName}, hash {a.StateInfo.shortNameHash}, time {a.NormalizedTime}");
                s.Tick(12);
                float t1 = a.NormalizedTime;
                a.SetPattern(p1);
                a.EvaluateNow();
                Check("the same pattern again continues (no restart)", a.StateName == st1 && Mathf.Abs(a.NormalizedTime - t1) <= 1e-6f && t1 > 0f, $"{t1} → {a.NormalizedTime}");

                s.Set(bird, "item.anime.speed", 0f);
                float t2 = a.NormalizedTime;
                s.Tick(10);
                Check("speed 0 freezes the time", a.NormalizedTime == t2, $"{t2} → {a.NormalizedTime}");
                s.Set(bird, "item.anime.time", 0.37f);
                Check("item.anime.time 0.37 jumps there", Mathf.Abs(a.NormalizedTime - 0.37f) <= 1e-5f && a.StateName == st1, "time " + a.NormalizedTime);
                s.Set(bird, "item.anime.speed", 1f);
                s.Tick(6);
                m.Restart();
                Check("restart → time 0 of the same state, no model change", a.NormalizedTime == 0f && a.StateName == st1 && Mathf.Abs(s.State(bird).Anime.Time - 0.37f) <= 1e-6f,
                      $"time {a.NormalizedTime}, stored {s.State(bird).Anime.Time}");

                s.Tick(20);
                float live = a.NormalizedTime;
                s.Runtime.CaptureNow();
                Check("capture after 20 steps: item.anime.time = the driver's time", s.State(bird).Anime.Time == live && !a.Advanced, $"stored {s.State(bird).Anime.Time}, live {live}");
                int w0 = s.Runtime.Capture.TotalWrites;
                s.Runtime.CaptureNow();
                Check("a second capture without stepping writes nothing", s.Runtime.Capture.TotalWrites == w0);

                // 957: isAnime with a controller, no anime patterns: the default state runs
                string gim = s.Add(957);
                var g = s.Motion(gim)?.Animation;
                if (Check("957: driven Animator (isAnime, controller), no valid patterns", g != null && g.Patterns.All(p => !p.valid), "anim " + (g != null)))
                {
                    float g0 = g.NormalizedTime;
                    s.Tick(30);
                    Check("957: the default state advances", g.NormalizedTime > g0, $"{g0} → {g.NormalizedTime}");
                    s.Set(gim, "item.anime.pattern", 1);
                    Check("957: a pattern without an entry only stores the value", s.State(gim).Anime.Pattern == 1 && g.Pattern == 1);
                }
                // an item without a driven Animator keeps its Animator disabled
                string fkItem = s.Add(1398);
                var fi = s.Item(fkItem);
                Check("1398 (Animator without controller): no animation stage, Animator disabled", s.Motion(fkItem)?.Animation == null && fi.Animator && !fi.Animator.enabled);
                s.Runtime.Apply();
                capturedTime = a.NormalizedTime;   // the save captures the bird's live time
                try { saved = s.Runtime.Ops.ToBytes(FixedSave()); } catch (Exception e) { Check("save", false, e.Message); }
                Check("the save captured the live time", s.State(bird).Anime.Time == capturedTime, $"stored {s.State(bird).Anime.Time}, live {capturedTime}");
            }
            if (saved == null) return;
            using (var s2 = new Scene(sv, 0x10ad5eedUL))
            {
                var ld = s2.Runtime.Ops.Load(saved);
                s2.Runtime.Apply();
                if (!Check("load the saved scene", ld.Ok, ld.ToString())) return;
                var bird = s2.Session.Model.PreOrder().FirstOrDefault(n => (n.State as ItemNodeState)?.Ref?.EndsWith("#1022") == true);
                var a = bird != null ? s2.Motion(bird.Id)?.Animation : null;
                Check("after load the bird's time = the captured time (≤ 1e-4)", a != null && Mathf.Abs(a.NormalizedTime - capturedTime) <= 1e-4f,
                      $"live {a?.NormalizedTime}, captured {capturedTime}, state {a?.StateName}");
                var again = s2.Runtime.Ops.ToBytes(FixedSave());
                Check("save → load → save without stepping: byte-identical", again.SequenceEqual(saved), $"{saved.Length} vs {again.Length} bytes");
            }
        }

        // ================================================================ N6 determinism

        static List<(string path, Vector3 p, Quaternion r, Vector3 sc)> Walk(Transform root)
        {
            var l = new List<(string, Vector3, Quaternion, Vector3)>();
            void W(Transform t, string path)
            {
                l.Add((path, t.localPosition, t.localRotation, t.localScale));
                for (int i = 0; i < t.childCount; i++)
                {
                    var c = t.GetChild(i);
                    if (SceneNodeHolder.IsHolder(c)) continue;
                    W(c, path + "/" + c.name);
                }
            }
            if (root) W(root, root.name);
            return l;
        }

        static void N6(RuntimeServices sv)
        {
            var poses = new List<List<(string, Vector3, Quaternion, Vector3)>>();
            for (int run = 0; run < 2; run++)
            {
                using (var s = new Scene(sv, 0xde7e3a1UL))
                {
                    string bird = s.Add(1022), gim = s.Add(957), fk = s.Add(1399), cat = s.Add(498, bird), db = s.Add(FkDynamicBonesRef);
                    s.Set(bird, "item.anime.pattern", 1, false);
                    s.Set(bird, "item.anime.speed", 1.5f, false);
                    var m = s.Motion(fk);
                    if (m?.Fk != null)
                        for (int i = 0; i < m.Fk.Count; i++) s.Set(fk, "item.fk.bones[" + FieldPath.Quote(m.Fk.Names[i]) + "]", new Vec3(5f * i, 10f, -15f), false);
                    s.Set(fk, "item.fk.enabled", true);
                    for (int k = 0; k < 120; k++)
                    {
                        s.Set(bird, "transform.rot", new Vec3(0f, 3f * k, 0f), false);
                        s.Set(db, "transform.pos", new Vec3(0.01f * k, 0f, 0f), false);
                        s.Tick(1);
                    }
                    var all = new List<(string, Vector3, Quaternion, Vector3)>();
                    foreach (var id in new[] { bird, gim, fk, cat, db }) all.AddRange(Walk(s.Item(id)?.Instance ? s.Item(id).Instance.transform : null));
                    poses.Add(all);
                    if (run == 0) Info($"run 1: {all.Count} transforms of 5 items (bird, gimmick, FK item, cat toy under the bird, {FkDynamicBonesRef})");
                }
            }
            var a = poses[0]; var b = poses[1];
            int diff = 0; string first = null;
            for (int i = 0; i < Math.Min(a.Count, b.Count); i++)
            {
                var x = a[i]; var y = b[i];
                if (x.Item1 != y.Item1 || !Same(x.Item2, y.Item2) || !Same(x.Item3, y.Item3) || !Same(x.Item4, y.Item4)) { diff++; first = first ?? x.Item1; }
            }
            Check("two builds stepped 120 frames by hand: every transform bit-equal", a.Count == b.Count && a.Count > 0 && diff == 0,
                  $"{a.Count} / {b.Count} transforms, {diff} differ{(first != null ? ", first " + first : "")}");
        }

        // ================================================================ N7 dynamic bones on a hand

        static void N7(RuntimeServices sv)
        {
            var t = global::TestTargets.Load();
            string card = global::TestTargets.Abs(t.visual.female);
            using (var s = new Scene(sv, 0x7a7d5eedUL))
            {
                var cr = s.Runtime.Ops.AddCharaFromCard(card);
                s.Runtime.Apply();
                if (!Check("character added", cr != null && cr.Status != CommandStatus.Error, cr?.ToString())) return;
                string chara = cr.NewId;
                s.Session.SetAnimation(chara, global::SceneRuntimeTest.Fixture.Idle);
                string cat = s.Add(498, chara, "N_Hand_R");
                var it = s.Item(cat);
                var m = s.Motion(cat);
                var hand = s.Runtime.FindChara(chara)?.AttachPoints?["N_Hand_R"];
                if (!Check("498 at N_Hand_R with a dynamic-bone chain", m?.Dynamic != null && m.Dynamic.Chains.Count > 0 && hand && it.Holder.parent == hand,
                        $"chains {m?.Dynamic?.Chains.Count}, hand {(bool)hand}; {s.Describe(cat)}")) return;
                var chain = m.Dynamic.Chains.First(c => c.ParticleCount > 1);
                var start = ChainPose(m.Dynamic);
                Info($"chain {chain}");
                // 60 frames: the character animates (Idle) and its root moves; dt 1/50 so every frame has at least one 60 Hz step
                const float dt = 0.02f;
                int frames = 60, followed = 0, handMoved = 0;
                float worst = 0f, swing = 0f;
                var root = chain.ParticleTransform(0);
                Vector3 prevHand = hand.position;
                int tip = chain.ParticleCount - 1;
                Vector3 restTipLocal = root.InverseTransformPoint(chain.Position(tip));
                for (int k = 0; k < frames; k++)
                {
                    s.Set(chara, "transform.pos", new Vec3(0.015f * k, 0f, 0f), false);
                    s.Runtime.Tick(dt);
                    if ((hand.position - prevHand).sqrMagnitude > 1e-12f) handMoved++;
                    prevHand = hand.position;
                    float e = (chain.Position(0) - root.position).magnitude;
                    worst = Mathf.Max(worst, e);
                    if (e <= 1e-5f) followed++;
                    swing = Mathf.Max(swing, (root.InverseTransformPoint(chain.Position(tip)) - restTipLocal).magnitude);
                }
                Check($"after every tick the chain root particle is at the hand-carried root bone (same frame; ≤ 1e-5) — items step after the character",
                      followed == frames && handMoved >= frames / 2, $"{followed} of {frames} frames, max {worst:E2}; hand moved in {handMoved} frames");
                Check("the chain moves when the hand moves (tip off its rest offset)", swing > 1e-3f, "max tip deviation " + F(swing));
                s.Set(cat, "item.dynamicBones", false);
                int off = start.Count(x => !Same(x.t.localPosition, x.p) || !Same(x.t.localRotation, x.r));
                Check("dynamicBones false → every chain bone at its Start pose, chains inactive", off == 0 && m.Dynamic.ActiveChains == 0 && !m.DynamicActive,
                      $"{off} of {start.Count} differ, active {m.Dynamic.ActiveChains}");
                s.Runtime.Tick(dt);
                off = start.Count(x => !Same(x.t.localPosition, x.p) || !Same(x.t.localRotation, x.r));
                Check("dynamicBones false: still the Start pose after a tick", off == 0, $"{off} differ");
                s.Set(cat, "item.dynamicBones", true);
                s.Runtime.Tick(dt);
                Check("dynamicBones true: chains active again", m.Dynamic.ActiveChains > 0 && m.DynamicActive);
            }
        }
    }
}
