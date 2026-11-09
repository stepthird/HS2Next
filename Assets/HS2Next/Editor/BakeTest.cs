// BakeTest (Phase 6.8): Chara to Item and the Cutter on the SFW targets of Tools/targets.json
// (BR-Chan, HERO107, clothed), judged by numbers; images stay in memory and nothing is written as a picture.
//   BakeTest.Run        headless, edit mode (AppCore.ForTests hosts, frames pumped by hand); report Spike/out/bake/bake_report.txt
//   BakeTest.RunRender  graphics, edit mode, the production URP asset; report Spike/out/bake/bake_render_report.txt
// The checks live in partial files of this class found by name: a static method named Headless_<part>() (no arguments) is a part of
// Run, Render_<part>() a part of RunRender; the parts run in ordinal order of their names and open their groups with Group(id, title,
// action). -only BM2+BD1 runs those groups. Each part owns its file. A store a test makes lives under Spike/out/bake/ (never the
// project's or a user's UserData).
//   headless (this file): BM2 unskinning, BM3 FK list, BM4 origin, BM5 inside copy
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HS2Next;
using HS2Next.App;
using HS2Next.Content;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Bake;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;

public static partial class BakeTest
{
    public const float Dt = 1f / 60f;
    public const ulong DefaultSeed = 0x68ba4eUL;
    public static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "bake");
    static string Report => Arg("-report") ?? Path.Combine(OutDir, "bake_report.txt");
    static string RenderReport => Arg("-report") ?? Path.Combine(OutDir, "bake_render_report.txt");

    static StringBuilder rep;
    static int passes, fails, warns;
    static string current = "";
    static readonly Dictionary<string, (int pass, int fail)> perGroup = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
    static readonly List<string> measurements = new List<string>();
    static string[] only;

    /// <summary>Catalog and Studio lists, built once by a run (shared by every host; never disposed by a part).</summary>
    static RuntimeServices services;
    static TestTargets targets;
    /// <summary>The SFW visual targets, relative to the HS2 card folders (what chara.load's path takes with sex).</summary>
    static string femaleRel, maleRel;

    // ------------------------------------------------------------ report

    static string Arg(string name)
    {
        var a = TestArgs.Get();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
    }

    static void Info(string s) => rep.AppendLine("   " + s);
    static void Warn(string s) { warns++; rep.AppendLine($"WARN  [{current}] {s}"); }
    static void Measure(string s) { measurements.Add(s); Info(s); }

    static bool Check(string name, bool ok, string detail = "")
    {
        rep.AppendLine($"{(ok ? "PASS" : "FAIL")}  [{current}] {name}{(string.IsNullOrEmpty(detail) ? "" : "  " + detail)}");
        if (ok) passes++; else fails++;
        perGroup.TryGetValue(current, out var v);
        perGroup[current] = ok ? (v.pass + 1, v.fail) : (v.pass, v.fail + 1);
        return ok;
    }

    static bool Want(string id) => only == null || only.Contains(id);

    /// <summary>Runs one group (skipped unless -only names it, when given); an exception is a FAIL of the group.</summary>
    static void Group(string id, string title, Action a)
    {
        if (!Want(id)) return;
        current = id;
        rep.AppendLine();
        rep.AppendLine($"== {id} {title}");
        try { a(); }
        catch (Exception e) { Check("exception", false, e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); }
        if (shots > 0) Info($"shots of {id}: {shots} renders; skinning matrices recalculated at the render on up to {shotsForcedMost} skinned renderers ({shotsForced} turned on in all)");
        shots = shotsForced = shotsForcedMost = 0;
        // a group's bakes hold about a gigabyte of textures each: what no host holds goes before the next group
        if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null) { Resources.UnloadUnusedAssets(); GC.Collect(); }
        Info($"memory after {id}: process {Process.GetCurrentProcess().WorkingSet64 >> 20} MiB, managed {GC.GetTotalMemory(false) >> 20} MiB, Unity allocated {UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() >> 20} MiB");
    }

    static string Short(string s) => s == null ? "null" : s.Length > 400 ? s.Substring(0, 400) + "..." : s;
    static string F(double v, string f = "G4") => v.ToString(f, System.Globalization.CultureInfo.InvariantCulture);

    static void Begin()
    {
        rep = new StringBuilder(); passes = fails = warns = 0; perGroup.Clear(); measurements.Clear();
        var o = Arg("-only");
        only = o == null ? null : o.Split(',', '+').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
        Directory.CreateDirectory(OutDir);
    }

    static void End(string path, Stopwatch sw, string title)
    {
        var sum = new StringBuilder();
        sum.AppendLine($"SUMMARY: {passes} PASS, {fails} FAIL ({warns} WARN, {sw.Elapsed.TotalSeconds:F0} s)");
        foreach (var kv in perGroup.OrderBy(k => k.Key, StringComparer.Ordinal)) sum.AppendLine($"  {kv.Key}: {kv.Value.pass} pass, {kv.Value.fail} fail");
        if (measurements.Count > 0) { sum.AppendLine("measurements:"); foreach (var m in measurements) sum.AppendLine("  " + m); }
        sum.AppendLine(title);
        rep.Insert(0, sum.ToString());
        File.WriteAllText(path, rep.ToString(), new UTF8Encoding(false));
    }

    /// <summary>The static methods of this class whose names start with <paramref name="prefix"/>, in ordinal order of their names.</summary>
    static List<MethodInfo> Parts(string prefix, Type returns) =>
        typeof(BakeTest).GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(m => m.Name.StartsWith(prefix, StringComparison.Ordinal) && m.GetParameters().Length == 0 && m.ReturnType == returns)
            .OrderBy(m => m.Name, StringComparer.Ordinal).ToList();

    static void RunParts(string prefix)
    {
        var parts = Parts(prefix, typeof(void));
        Info("parts: " + string.Join(", ", parts.Select(p => p.Name.Substring(prefix.Length))));
        foreach (var p in parts)
        {
            current = p.Name;
            try { p.Invoke(null, null); }
            catch (TargetInvocationException e) { Check("part " + p.Name, false, e.InnerException?.ToString() ?? e.ToString()); }
            catch (Exception e) { Check("part " + p.Name, false, e.ToString()); }
        }
    }

    // ------------------------------------------------------------ headless (BakeTest.Run)

    public static void Run()
    {
        Begin();
        var sw = Stopwatch.StartNew();
        var head = new StringBuilder();
        try
        {
            services = SceneRuntimeTest.NewServices(head);
            LoadTargets();
            rep.AppendLine($"BakeTest (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType})");
            rep.Append(head);
            RunParts("Headless_");
        }
        catch (Exception e) { current = "run"; Check("run (exception)", false, e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); }
        Restore();
        End(Report, sw, "BakeTest.Run (headless)");
        services?.Catalog?.UnloadAll();
        Debug.Log($"BAKE TEST DONE: {passes} PASS, {fails} FAIL");
        if (fails > 0) TestExit.Exit(1);
    }

    // ------------------------------------------------------------ graphics (BakeTest.RunRender)

    /// <summary>The production URP asset for a run, every setting back at the end.</summary>
    sealed class Pipeline : IDisposable
    {
        readonly RenderPipelineAsset d, q;
        readonly bool async;
        public readonly bool Ok;
        public Pipeline()
        {
            d = GraphicsSettings.defaultRenderPipeline; q = QualitySettings.renderPipeline; async = ShaderUtil.allowAsyncCompilation;
            var prod = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AppPipeline.EditorAssetPath);
            Ok = prod && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
            if (!Ok) return;
            ShaderUtil.allowAsyncCompilation = false;
            GraphicsSettings.defaultRenderPipeline = prod; QualitySettings.renderPipeline = prod;
        }
        public void Dispose()
        {
            GraphicsSettings.defaultRenderPipeline = d; QualitySettings.renderPipeline = q; ShaderUtil.allowAsyncCompilation = async;
        }
    }

    public static void RunRender()
    {
        Begin();
        var sw = Stopwatch.StartNew();
        var head = new StringBuilder();
        try
        {
            services = SceneRuntimeTest.NewServices(head);
            LoadTargets();
            rep.AppendLine($"BakeTest.render (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType})");
            rep.Append(head);
            using (var p = new Pipeline())
            {
                current = "render";
                if (Check("the production URP asset and a graphics device", p.Ok, SystemInfo.graphicsDeviceType.ToString())) RunParts("Render_");
            }
        }
        catch (Exception e) { current = "run"; Check("run (exception)", false, e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); }
        Restore();
        End(RenderReport, sw, "BakeTest.RunRender (images in memory only)");
        services?.Catalog?.UnloadAll();
        Debug.Log($"BAKE RENDER TEST DONE: {passes} PASS, {fails} FAIL");
        if (fails > 0) TestExit.Exit(1);
    }

    /// <summary>Every static switch a part may set, back at its default.</summary>
    static void Restore()
    {
        BakeMeshes.ForceGpuRoute = false;
        BakeBuild.SkipMembers = false;
        HS2Next.Motion.Face.FaceSetup.Seed = null;
    }

    static void LoadTargets()
    {
        targets = TestTargets.Load(Arg("-targets"));
        femaleRel = RelToCards(targets.visual.female);
        maleRel = RelToCards(targets.visual.male);
    }

    /// <summary>A UserData-relative card path made relative to its HS2 card folder (what chara.load's path takes with sex).</summary>
    static string RelToCards(string rel)
    {
        if (string.IsNullOrEmpty(rel)) return rel;
        foreach (var pre in new[] { "chara/female/", "chara/male/" })
            if (rel.StartsWith(pre, StringComparison.OrdinalIgnoreCase)) return rel.Substring(pre.Length);
        return rel;
    }

    // ------------------------------------------------------------ hosts (edit mode)

    internal sealed class Host : IDisposable
    {
        public AppCore App;
        public SceneRuntime Rt => App.Runtime;
        public SceneSession Session => App.Session;
        public string LastText;

        public JObject Call(object request)
        {
            string json = request is string s ? s : JsonConvert.SerializeObject(request);
            LastText = App.Call(json);
            return JObject.Parse(LastText);
        }

        /// <summary>Calls and checks ok: true; returns the reply.</summary>
        public JObject Ok(object request, string what)
        {
            var r = Call(request);
            Check(what, r.Value<bool?>("ok") == true, r.Value<bool?>("ok") == true ? "" : Short(LastText));
            return r;
        }

        public void Frames(int n, float dt = Dt)
        {
            for (int i = 0; i < n; i++) App.Frame(dt);
        }

        public void Dispose() { try { App?.Dispose(); } catch (Exception e) { Info("host dispose threw: " + e.Message); } }
    }

    static string UserDataDir => Path.Combine(OutDir, "userdata");
    static string OutputDir => Path.Combine(OutDir, "out");

    /// <summary>A fresh test host: counting ids, the run's own UserData under Spike/out/bake/ (its store is the test's).</summary>
    internal static Host NewHost(ulong seed = DefaultSeed)
    {
        HS2Next.Motion.Face.FaceSetup.Seed = 0x5eed;
        var s = AppSettings.Defaults(Array.Empty<string>());
        s.Content = CardResolveTest.Settings;
        s.UserData = UserDataDir;
        s.Bridge.OutputDir = OutputDir;
        var o = services.NewSessionOptions();
        o.Ids = new CountingIdSource(seed);
        return new Host { App = AppCore.ForTests(services, s, null, o) };
    }

    /// <summary>The node id of an add reply: its key, or its first added node's key (chara.load, item.add).</summary>
    internal static string Key(JObject r) => (string)r?["key"] ?? (string)r?["added"]?[0]?["key"];

    /// <summary>Loads a visual target into a host, standing still in the idle row at <paramref name="time"/>; its key.</summary>
    internal static string LoadChara(Host h, bool male = false, float time = 0.3f)
    {
        var r = h.Ok(new { cmd = "chara.load", path = male ? maleRel : femaleRel, sex = male ? "male" : "female" }, "chara.load " + (male ? "HERO107" : "BR-Chan"));
        string key = Key(r);
        h.Rt.ApplyNow();
        h.Ok(new { cmd = "chara.anime", key, group = 19, category = 0, no = 0, time, speed = 0f }, "the idle row");
        h.Rt.ApplyNow();
        return key;
    }

    internal static CharaRealization Chara(Host h, string key) => h.Rt.Realizations.OfType<CharaRealization>().FirstOrDefault(x => x.NodeId == key);

    // ------------------------------------------------------------ BM2-BM5 (Unity-free arithmetic, headless)

    static void Headless_Model()
    {
        Group("BM2", "unskinning in double", Bm2);
        Group("BM3", "the FK list: pruning and names", Bm3);
        Group("BM4", "the origin: the area-weighted centroid", Bm4);
        Group("BM5", "the inside copy", Bm5);
    }

    /// <summary>A rotation from three angles in degrees.</summary>
    static Quaternion Q(System.Random r, float range) => Quaternion.Euler((float)(r.NextDouble() * 2 - 1) * range, (float)(r.NextDouble() * 2 - 1) * range, (float)(r.NextDouble() * 2 - 1) * range);

    static void Bm2()
    {
        // a synthetic skinned strip: 4 bones in a chain, 200 vertices with 1-4 influences, random bone poses
        var rnd = new System.Random(682);
        const int nb = 4, nv = 200;
        var bindWorld = new Matrix4x4[nb];
        var bp = new Matrix4x4[nb];
        for (int i = 0; i < nb; i++)
        {
            bindWorld[i] = Matrix4x4.TRS(new Vector3(0f, 2f * i, 0f), Quaternion.identity, Vector3.one);
            bp[i] = bindWorld[i].inverse;
        }
        var v = new Vector3[nv];
        var n = new Vector3[nv];
        var t = new Vector4[nv];
        var w = new BoneWeight[nv];
        for (int k = 0; k < nv; k++)
        {
            v[k] = new Vector3((float)rnd.NextDouble() * 2 - 1, (float)rnd.NextDouble() * 8, (float)rnd.NextDouble() * 2 - 1);
            n[k] = new Vector3((float)rnd.NextDouble() - 0.5f, (float)rnd.NextDouble() - 0.5f, (float)rnd.NextDouble() - 0.5f).normalized;
            var tt = Vector3.Cross(n[k], Vector3.up).normalized;
            t[k] = new Vector4(tt.x, tt.y, tt.z, k % 2 == 0 ? 1f : -1f);
            int c = 1 + k % 4;
            var ws = new float[4];
            float sum = 0f;
            for (int j = 0; j < c; j++) { ws[j] = 0.1f + (float)rnd.NextDouble(); sum += ws[j]; }
            for (int j = 0; j < c; j++) ws[j] /= sum;
            Array.Sort(ws, (a, b) => b.CompareTo(a));
            int b0 = k % nb;
            w[k] = new BoneWeight
            {
                boneIndex0 = b0, weight0 = ws[0], boneIndex1 = (b0 + 1) % nb, weight1 = ws[1],
                boneIndex2 = (b0 + 2) % nb, weight2 = ws[2], boneIndex3 = (b0 + 3) % nb, weight3 = ws[3],
            };
        }
        // posed bones: a chain bent by random rotations, the root moved and scaled
        var posed = new Matrix4x4[nb];
        var root = Matrix4x4.TRS(new Vector3(3f, 1f, -2f), Q(rnd, 180f), Vector3.one * 1.3f);
        var acc = root;
        for (int i = 0; i < nb; i++)
        {
            acc = acc * Matrix4x4.TRS(i == 0 ? Vector3.zero : new Vector3(0f, 2f, 0f), Q(rnd, 60f), Vector3.one);
            posed[i] = acc;
        }
        // forward in double: world positions, world normals and tangents (by the blended matrix as directions)
        var wp = BakeMeshes.SkinWorld(v, w, posed, bp);
        var skin = new BakeMeshes.D34[nb];
        for (int i = 0; i < nb; i++) skin[i] = BakeMeshes.D34.Mul(BakeMeshes.D34.Of(posed[i]), BakeMeshes.D34.Of(bp[i]));
        var wn = new Vector3[nv];
        var wt = new Vector4[nv];
        double moved = 0;
        for (int k = 0; k < nv; k++)
        {
            var M = new BakeMeshes.D34();
            var bw = w[k];
            M.AddScaled(skin[bw.boneIndex0], bw.weight0); M.AddScaled(skin[bw.boneIndex1], bw.weight1);
            M.AddScaled(skin[bw.boneIndex2], bw.weight2); M.AddScaled(skin[bw.boneIndex3], bw.weight3);
            wn[k] = M.Dir(n[k], true);
            var d = M.Dir(new Vector3(t[k].x, t[k].y, t[k].z), true);
            wt[k] = new Vector4(d.x, d.y, d.z, t[k].w);
            moved = Math.Max(moved, (wp[k] - v[k]).magnitude);
        }
        var bv = (Vector3[])wp.Clone();
        var bn = (Vector3[])wn.Clone();
        var bt = (Vector4[])wt.Clone();
        int singular = BakeMeshes.Unskin(bv, bn, bt, w, posed, bp);
        double pe = 0, ne = 0, te = 0;
        bool wKept = true;
        for (int k = 0; k < nv; k++)
        {
            pe = Math.Max(pe, (bv[k] - v[k]).magnitude);
            ne = Math.Max(ne, AngleRad(bn[k], n[k]));
            te = Math.Max(te, AngleRad(new Vector3(bt[k].x, bt[k].y, bt[k].z), new Vector3(t[k].x, t[k].y, t[k].z)));
            if (bt[k].w != t[k].w) wKept = false;
        }
        Check("BM2 present: the pose moves vertices >= 1 unit", moved >= 1.0, "greatest move " + F(moved));
        Check("BM2 unskinned positions equal the bind positions <= 1e-5 units", pe <= 1e-5 && singular == 0, "max " + F(pe, "E3") + ", singular " + singular + ", " + nv + " vertices, 1-4 influences");
        Check("BM2 unskinned normals <= 1e-4 rad", ne <= 1e-4, "max " + F(ne, "E3") + " rad");
        Check("BM2 unskinned tangents <= 1e-4 rad, w kept", te <= 1e-4 && wKept, "max " + F(te, "E3") + " rad");
    }

    /// <summary>The angle between two directions in double (Quaternion.Angle reads 0 under 0.16 degrees).</summary>
    static double AngleRad(Vector3 a, Vector3 b)
    {
        double ax = a.x, ay = a.y, az = a.z, bx = b.x, by = b.y, bz = b.z;
        double la = Math.Sqrt(ax * ax + ay * ay + az * az), lb = Math.Sqrt(bx * bx + by * by + bz * bz);
        if (la == 0 || lb == 0) return Math.PI;
        double cx = ay * bz - az * by, cy = az * bx - ax * bz, cz = ax * by - ay * bx;
        return Math.Atan2(Math.Sqrt(cx * cx + cy * cy + cz * cz), ax * bx + ay * by + az * bz);
    }

    static void Bm3()
    {
        // root(0) - hips(1) - spine(2) - neck(3) - head(4) - hairA(5) - hairA_tip(6)
        //                              \ armL(7) - handL(8) - handL_s(9)
        //                  \ legL(10) - footL(11) - "neck"(12, an accessory bone named like an FK bone)
        //                                          \ legL(13, a second bone named legL: an FK bone too)
        var names = new List<string> { "root", "hips", "spine", "neck", "head", "hairA", "hairA_tip", "armL", "handL", "handL_s", "legL", "footL", "neck", "legL" };
        var parents = new List<int> { -1, 0, 1, 2, 3, 4, 5, 2, 7, 8, 1, 10, 11, 11 };
        var fk = new List<int> { 1, 2, 3, 4, 5, 7, 8, 10, 11, 13 };
        // used: the head's vertices weigh on head (4) and hairA_tip (6); the hand's on handL_s (9); an unskinned prop hangs on node 12;
        // the second legL (13) moves a vertex too
        var used = new[] { 4, 6, 9, 12, 13 };
        var kept = BakeFk.Prune(parents, fk, used);
        var expectKept = new List<int> { 4, 5, 8, 11, 13 };
        Check("BM3 pruning keeps the nearest FK bone at or above each used node", kept.SequenceEqual(expectKept), "kept [" + string.Join(", ", kept) + "], expected [" + string.Join(", ", expectKept) + "]");
        int dropped = fk.Count - kept.Count;
        var n2 = new List<string>(names);
        var fkNames = BakeFk.Rename(n2, kept);
        var expectNames = new List<string> { "head", "hairA", "handL", "footL", "legL" };
        Check("BM3 the FK names in list order", fkNames.SequenceEqual(expectNames), string.Join(", ", fkNames));
        // the accessory bone named neck is not FK after pruning (neck 3 was dropped), so nothing renames it; the first legL (10) is not
        // FK either, so it is renamed apart from the FK bone legL (13)
        int changed = 0;
        for (int i = 0; i < names.Count; i++) if (names[i] != n2[i]) changed++;
        Check("BM3 a node named like an FK bone is renamed <name>~<index>", n2[10] == "legL~10" && n2[13] == "legL" && n2[12] == "neck" && n2[3] == "neck", string.Join(", ", n2));
        // two FK bones with one name: the later one is renamed
        var n3 = new List<string> { "root", "b", "b", "c" };
        var f3 = BakeFk.Rename(n3, new List<int> { 1, 2 });
        Check("BM3 two FK bones with one name: the later one becomes <name>~<index>", f3.SequenceEqual(new[] { "b", "b~2" }) && n3[2] == "b~2", string.Join(", ", f3));
        Check("BM3 present: >= 1 bone dropped, >= 1 name changed", dropped >= 1 && changed >= 1, "dropped " + dropped + ", names changed " + changed);
        // own list (V20): weighted nodes whose names occur once, not the root
        var own = BakeFk.OwnList(new List<string> { "rope", "j1", "j2", "j2", "j3" }, new[] { 0, 1, 2, 3, 4 });
        Check("BM3 an FK-less prop's own list: weighted, unique names, not the root", own.SequenceEqual(new[] { 1, 4 }), "[" + string.Join(", ", own) + "]");
    }

    static void Bm4()
    {
        // two triangles of very different areas: the area-weighted centroid lies near the big one; the vertex mean far from it
        var p = new[]
        {
            new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f), new Vector3(0f, 10f, 0f),
            new Vector3(20f, 0f, 0f), new Vector3(20.1f, 0f, 0f), new Vector3(20f, 0.1f, 0f),
        };
        var idx = new[] { new[] { 0, 1, 2, 3, 4, 5 } };
        double sx = 0, sy = 0, sz = 0, sa = 0;
        BakeMeshes.Accumulate(p, idx, new[] { MeshTopology.Triangles }, ref sx, ref sy, ref sz, ref sa);
        double a1 = 50, a2 = 0.005;
        double ex = (a1 * (10.0 / 3) + a2 * (60.1 / 3)) / (a1 + a2), ey = (a1 * (10.0 / 3) + a2 * (0.1 / 3)) / (a1 + a2);
        double cx = sx / sa, cy = sy / sa, cz = sz / sa;
        double err = Math.Sqrt((cx - ex) * (cx - ex) + (cy - ey) * (cy - ey) + cz * cz);
        var mean = Vector3.zero;
        foreach (var v in p) mean += v;
        mean /= p.Length;
        double meanOff = Math.Sqrt((mean.x - ex) * (mean.x - ex) + (mean.y - ey) * (mean.y - ey));
        Check("BM4 the area-weighted centroid <= 1e-5", err <= 1e-5, "error " + F(err, "E3"));
        Check("BM4 present: the vertex mean lies >= 0.1 away", meanOff >= 0.1, F(meanOff));
    }

    static void Bm5()
    {
        // a quad in a scaled bind: mesh units are 0.25 world units (scale 4 in the bind matrix): the inset is 0.01 world = 0.0025 mesh
        var md = new MeshData
        {
            Name = "quad",
            V = new[] { new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(1f, 1f, 0f), new Vector3(0f, 1f, 0f) },
            N = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back },
            T = new[] { new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, -1f), new Vector4(1f, 0f, 0f, 1f) },
            Idx = new[] { new[] { 0, 1, 2, 0, 2, 3 } },
            Topo = new[] { MeshTopology.Triangles },
        };
        var bind = Matrix4x4.TRS(new Vector3(5f, 0f, 0f), Quaternion.Euler(10f, 20f, 30f), new Vector3(4f, 4f, 4f));
        var M = BakeMeshes.D34.Of(bind);
        var copy = BakeInsides.Reversed(md, null, k => M.Dir(md.N[k], false).magnitude);
        bool order = copy.Idx[0].SequenceEqual(new[] { 0, 2, 1, 0, 3, 2 });
        bool normals = true, tangents = true;
        double insetErr = 0;
        for (int k = 0; k < 4; k++)
        {
            normals &= copy.N[k] == -md.N[k];
            tangents &= copy.T[k].w == -md.T[k].w && copy.T[k].x == md.T[k].x;
            // the world offset along the outside's world normal is −0.01
            M.PointD(md.V[k].x, md.V[k].y, md.V[k].z, out double ox, out double oy, out double oz);
            M.PointD(copy.V[k].x, copy.V[k].y, copy.V[k].z, out double ix, out double iy, out double iz);
            double dist = Math.Sqrt((ix - ox) * (ix - ox) + (iy - oy) * (iy - oy) + (iz - oz) * (iz - oz));
            insetErr = Math.Max(insetErr, Math.Abs(dist - BakeInsides.Inset));
        }
        Check("BM5 the triangles are turned round", order, string.Join(",", copy.Idx[0]));
        Check("BM5 normals negated, tangents' w negated", normals && tangents);
        Check("BM5 the inset is 0.01 world through the scaled bind <= 1e-6", insetErr <= 1e-6, "error " + F(insetErr, "E3"));
        Check("BM5 present: the bind's scale is not 1", Math.Abs(bind.lossyScale.x - 1f) > 0.5f, "scale " + F(bind.lossyScale.x));
    }

    // ------------------------------------------------------------ a bake made as data (headless: no capture)

    /// <summary>
    /// A small bake made in code: a chain of three FK bones (j1, j2 bent 30 degrees about z, j3) bending a skinned strip, a box hung on the
    /// root, one HS2Next/Standard material with a 4 x 4 texture of <paramref name="tint"/>. Its texture's data in <paramref name="textures"/>.
    /// </summary>
    internal static BakeData Synthetic(string name, Color32 tint, out List<TexData> textures)
    {
        var b = BakeCapture.NewBake(BakeData.KindItem);
        b.Origin = BakeData.OriginItem;
        b.Source = new BakeData.SourceInfo { Kind = "item", Name = name };
        void Node(string n, int parent, Vector3 p, Quaternion r) => b.Nodes.Add(new BakeData.NodeData { Name = n, Parent = parent, P = p, R = r });
        Node("syn_root", -1, Vector3.zero, Quaternion.identity);
        Node("j1", 0, Vector3.zero, Quaternion.identity);
        Node("j2", 1, new Vector3(0f, 2f, 0f), Quaternion.Euler(0f, 0f, 30f));
        Node("j3", 2, new Vector3(0f, 2f, 0f), Quaternion.identity);
        Node("strip", 0, Vector3.zero, Quaternion.identity);
        Node("box", 0, new Vector3(3f, 0f, 0f), Quaternion.identity);
        // the strip in the bind pose (straight): 2 columns x 7 rows from y 0 to 6, weighted to j1 / j2 / j3 by height
        var v = new List<Vector3>();
        var n0 = new List<Vector3>();
        var w = new List<BoneWeight>();
        var uv = new List<Vector4>();
        for (int y = 0; y <= 6; y++)
            for (int x = 0; x < 2; x++)
            {
                v.Add(new Vector3(x - 0.5f, y, 0f));
                n0.Add(Vector3.back);
                uv.Add(new Vector4(x, y / 6f, 0f, 0f));
                int bone = Math.Min(2, y / 2);
                w.Add(new BoneWeight { boneIndex0 = bone, weight0 = 1f });
            }
        var tris = new List<int>();
        for (int y = 0; y < 6; y++) { int a = 2 * y; tris.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 }); }
        var strip = new MeshData { Name = "syn_strip", V = v.ToArray(), N = n0.ToArray(), W = w.ToArray(), Idx = new[] { tris.ToArray() }, Topo = new[] { MeshTopology.Triangles } };
        strip.Uv[0] = uv.ToArray(); strip.UvDim[0] = 2;
        strip.Bp = new[] { Matrix4x4.identity, Matrix4x4.Translate(new Vector3(0f, -2f, 0f)), Matrix4x4.Translate(new Vector3(0f, -4f, 0f)) };
        var bv = new Vector3[8];
        for (int i = 0; i < 8; i++) bv[i] = new Vector3((i & 1) == 0 ? -0.5f : 0.5f, (i & 2) == 0 ? -0.5f : 0.5f, (i & 4) == 0 ? -0.5f : 0.5f);
        var box = new MeshData
        {
            Name = "syn_box", V = bv, N = bv.Select(x => x.normalized).ToArray(),
            Idx = new[] { new[] { 0, 2, 1, 1, 2, 3, 4, 5, 6, 5, 7, 6, 0, 1, 4, 1, 5, 4, 2, 6, 3, 3, 6, 7, 0, 4, 2, 2, 4, 6, 1, 3, 5, 3, 7, 5 } },
            Topo = new[] { MeshTopology.Triangles },
        };
        b.Meshes.Add(new BakeData.MeshRef { Name = strip.Name, Data = strip });
        b.Meshes.Add(new BakeData.MeshRef { Name = box.Name, Data = box });
        b.Textures.Add(new BakeData.TexRef { Name = "syn_tex", WrapU = 1, WrapV = 1, Filter = 0, Aniso = 1 });
        var mat = new BakeData.MatData { Name = "syn_mat", Shader = "HS2Next/Standard", Queue = 2000 };
        mat.Props.Add(new BakeData.PropData { Name = "_Color", Type = 'c', V = new Vector4(1f, 1f, 1f, 1f) });
        mat.Props.Add(new BakeData.PropData { Name = "_MainTex", Type = 't', Tex = 0 });
        mat.Props.Add(new BakeData.PropData { Name = "_Glossiness", Type = 'f', F = 0.25f });
        b.Materials.Add(mat);
        b.Renderers.Add(new BakeData.RendData
        {
            Node = 4, Skinned = true, Mesh = 0, Materials = new[] { 0 }, Bones = new[] { 1, 2, 3 }, Root = 1,
            Bounds = new Bounds(new Vector3(0f, 3f, 0f), new Vector3(2f, 8f, 2f)), Part = "",
        });
        b.Renderers.Add(new BakeData.RendData { Node = 5, Skinned = false, Mesh = 1, Materials = new[] { 0 }, Part = "" });
        b.Fk.AddRange(new[] { "j1", "j2", "j3" });
        var t = new TexData { W = 4, H = 4, Format = UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_SRGB, SourceFormat = "R8G8B8A8_SRGB" };
        var px = new byte[64];
        for (int i = 0; i < 16; i++) { px[4 * i] = tint.r; px[4 * i + 1] = tint.g; px[4 * i + 2] = tint.b; px[4 * i + 3] = 255; }
        t.Levels.Add(px);
        textures = new List<TexData> { t };
        return b;
    }

    /// <summary>A synthetic bake encoded (its prototype not built: a load builds it from the blobs).</summary>
    internal static BakeJob SyntheticJob(string name, Color32 tint)
    {
        var b = Synthetic(name, tint, out var tx);
        var j = BakeJob.OfData(b, tx);
        j.Finish();
        return j;
    }

    static ItemRealization ItemOf(Host h, string key) => h.Rt.Realizations.OfType<ItemRealization>().FirstOrDefault(x => x.NodeId == key);

    /// <summary>Adds an item node naming a bake whose blobs are (or are not) in the scene.</summary>
    static string AddBakeNode(Host h, ItemBake block, string name)
    {
        var r = h.Session.AddItem(BakeState(block, block.No, name));
        Check("the bake's node is added", r.Ok, r.Code + " " + r.Message);
        h.Rt.ApplyNow();
        return r.NewId;
    }

    /// <summary>Builds an item's content again (what a structural change does).</summary>
    static void Rebuild(Host h, ItemRealization it)
    {
        var m = typeof(SceneRuntime).GetMethod("RebuildContent", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        m.Invoke(h.Rt, new object[] { it });
        h.Rt.ApplyNow();
    }

    // ------------------------------------------------------------ BB2, BB3, BB6 (headless)

    static void Headless_Build()
    {
        Group("BB2", "missing blobs: the store by the bake's id; both missing: the fallback, the block written back byte-equal", Bb2);
        Group("BB3", "the prototype cache: one build for two items, released at a new scene, content never taken by number", Bb3);
        Group("BB6", "FK by name; a bone without a value keeps its baked rotation", Bb6);
    }

    static void Bb2()
    {
        ClearStore();
        var h = NewHost();
        try
        {
            var job = SyntheticJob("bb2", new Color32(200, 40, 40, 255));
            var res = h.Rt.Session.Model.File.Res;
            job.AddTo(res);
            var store = BakeProvider.StoreOf(h.Rt);
            var row = store.Add(job.Row("bb2"), job.AllBlobs());
            // a data blob missing in the scene: the store's row with the same id supplies it
            string tex = BakeBlob.ResName(job.Main.Textures[0].Blob);
            res.Remove(tex);
            string key = AddBakeNode(h, job.Block(row.Number), "bb2");
            var it = ItemOf(h, key);
            Check("BB2 a blob missing in the scene comes from the store (by the bake's id)", it != null && it.Source == "bake" && !it.IsFallback && res.Contains(tex),
                it == null ? "no item" : $"source {it.Source}, fallback {it.IsFallback}, the blob back in res/ {res.Contains(tex)}");
            // both missing: a bake the store does not hold, its blobs not in the scene
            var other = SyntheticJob("bb2b", new Color32(40, 200, 40, 255));
            var block = other.Block(null);
            string before = block.ToJson();
            string k2 = AddBakeNode(h, block, "bb2b");
            var it2 = ItemOf(h, k2);
            var missing = h.Rt.Findings.Where(f => f.NodeId == k2 && f.Code == BakeFindings.Missing).ToList();
            Check("BB2 neither the scene nor the store: the fallback item with bake.missing", it2 != null && it2.IsFallback && missing.Count > 0,
                it2 == null ? "no item" : $"fallback {it2.IsFallback}, source {it2.Source}, findings {missing.Count}");
            var st = (ItemNodeState)h.Rt.Model.Find(k2).State;
            string after = st.Bake?.ToJson();
            var sr = h.Ok(new { cmd = "scene.save", name = "bb2_bake", overwrite = true, thumb = "placeholder" }, "BB2 scene.save");
            var h2 = NewHost();
            try
            {
                h2.Ok(new { cmd = "scene.load", path = (string)sr["path"] }, "BB2 scene.load");
                var st2 = (ItemNodeState)h2.Rt.Model.Find(k2)?.State;
                Check("BB2 the fallback's block is written back byte-equal (in the model and through a save and a load)", after == before && st2?.Bake?.ToJson() == before,
                    $"model {(after == before ? "equal" : "differs")}, saved {(st2?.Bake?.ToJson() == before ? "equal" : "differs")}");
            }
            finally { h2.Dispose(); try { File.Delete((string)sr["path"]); } catch (Exception) { } }
            job.Dispose(); other.Dispose();
        }
        finally { h.Dispose(); ClearStore(); }
    }

    static void Bb3()
    {
        ClearStore();
        var h = NewHost();
        try
        {
            var a = SyntheticJob("bb3a", new Color32(255, 0, 0, 255));
            var res = h.Rt.Session.Model.File.Res;
            a.AddTo(res);
            var cache = BakeCache.Of(h.Rt, true);
            int builds = cache.Builds;
            string k1 = AddBakeNode(h, a.Block(null), "bb3a 1"), k2 = AddBakeNode(h, a.Block(null), "bb3a 2");
            var p1 = BakeCache.PrototypeOf(ItemOf(h, k1));
            var p2 = BakeCache.PrototypeOf(ItemOf(h, k2));
            Check("BB3 two items of one bake: one prototype, built once, two users", p1 != null && p1 == p2 && cache.Builds - builds == 1 && p1.Users == 2,
                $"builds {cache.Builds - builds}, same {p1 == p2}, users {p1?.Users}");
            // another bake under the same store number (another machine's store): the scene's own blobs are built, never the number's
            var b = SyntheticJob("bb3b", new Color32(0, 0, 255, 255));
            b.AddTo(res);
            var store = BakeProvider.StoreOf(h.Rt);
            var rowA = store.Add(a.Row("bb3a"), a.AllBlobs());
            string k3 = AddBakeNode(h, b.Block(rowA.Number), "bb3b under a's number");
            var p3 = BakeCache.PrototypeOf(ItemOf(h, k3));
            bool own = p3 != null && p3 != p1 && p3.Main == b.MainSha;
            Texture tex3 = null;
            var r3 = ItemOf(h, k3)?.Instance?.GetComponentInChildren<MeshRenderer>(true);
            if (r3) tex3 = r3.sharedMaterial ? r3.sharedMaterial.GetTexture("_MainTex") : null;
            Check("BB3 a bake under another bake's number builds its own content (V15)", own && tex3 && tex3.name == "syn_tex", $"prototype {(p3 == null ? "none" : p3.Main == b.MainSha ? "b's" : p3.Main == a.MainSha ? "a's" : "other")}");
            int protos = cache.Count;
            Check("BB3 present: two prototypes existed", protos >= 2, protos + " prototypes");
            h.Ok(new { cmd = "scene.new" }, "BB3 scene.new");
            h.Rt.ApplyNow();
            Check("BB3 a new scene releases the prototypes no item uses", cache.Count == 0 && cache.Releases >= protos, $"left {cache.Count}, released {cache.Releases}");
            a.Dispose(); b.Dispose();
        }
        finally { h.Dispose(); ClearStore(); }
    }

    static void Bb6()
    {
        var h = NewHost();
        try
        {
            var job = SyntheticJob("bb6", new Color32(128, 128, 128, 255));
            job.AddTo(h.Rt.Session.Model.File.Res);
            string key = AddBakeNode(h, job.Block(null), "bb6");
            var it = ItemOf(h, key);
            var j1 = ItemDefaults.FindBone(it.Instance.transform, "j1");
            var j2 = ItemDefaults.FindBone(it.Instance.transform, "j2");
            var baked2 = j2.localRotation;
            var m = ItemMotion.Of(it);
            Check("BB6 the bake's FK list is the item's", m?.Fk != null && m.Fk.Count == 3 && m.Fk.IndexOf("j1") == 0, m?.Fk == null ? "no FK stage" : string.Join(", ", m.Fk.Names));
            h.Ok(new { cmd = "item.set", key, set = new Dictionary<string, object> { ["fk.enabled"] = true, ["fk.bones[\"j1\"]"] = new[] { 0, 0, 40 } } }, "BB6 item.set FK j1");
            h.Frames(2);
            double moved = AngleDeg(j1.localRotation, Quaternion.identity);
            double kept = AngleDeg(j2.localRotation, baked2);
            Check("BB6 present: a bone with a value moved >= 10 degrees", moved >= 10.0, F(moved) + " deg");
            Check("BB6 a bone without a value keeps its baked rotation <= 1e-6 (not 0)", kept <= 1e-6 && AngleDeg(baked2, Quaternion.identity) > 10.0, $"j2 off its baked rotation by {F(kept, "E3")} deg; baked {F(AngleDeg(baked2, Quaternion.identity))} deg from 0");
            job.Dispose();
        }
        finally { h.Dispose(); }
    }

    /// <summary>The angle between two rotations in degrees, in double: 2 atan2(|v|, |w|) of a * b⁻¹ (q and −q the same; Quaternion.Angle reads 0
    /// under 0.16 degrees).</summary>
    internal static double AngleDeg(Quaternion a, Quaternion b)
    {
        double ax = a.x, ay = a.y, az = a.z, aw = a.w, bx = -b.x, by = -b.y, bz = -b.z, bw = b.w;
        double x = aw * bx + ax * bw + ay * bz - az * by;
        double y = aw * by - ax * bz + ay * bw + az * bx;
        double z = aw * bz + ax * by - ay * bx + az * bw;
        double w = aw * bw - ax * bx - ay * by - az * bz;
        return 2.0 * Math.Atan2(Math.Sqrt(x * x + y * y + z * z), Math.Abs(w)) * 180.0 / Math.PI;
    }
}
