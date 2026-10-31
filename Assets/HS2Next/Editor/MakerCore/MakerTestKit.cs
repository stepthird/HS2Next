// The shared harness of the Maker page tests: the report kit, the per-test helpers for building, flushing and comparing a
// character, the value mutators of the family tables, and the GPU fence and timing judge of the graphics rows. A test
// imports it with `using static MakerTestKit;` and calls Check / Warn / Info / Finish, Arg / Want / Median / Short, NextRow
// / HasTex, Alt*, Fence / GpuMs / Target / TimeOp as if they were its own members.
//   Report: one report at a time per Unity session (static state). Reset() starts it; Finish(file, title, sw, tag) writes
//     Spike/out/maker/<file> with the first line "SUMMARY: N PASS, M FAIL[, K WARN] (s)" and ends Unity with exit code 1 on
//     any FAIL, except inside a suite run (-suite on the command line), where the suite runner ends Unity.
//   RunSuite: -executeMethod MakerTestKit.RunSuite -suite "A.Run,B.RunU,..." runs several static parameterless test
//     methods in one Unity session (fewer sessions while other users wait for the runner mutex); exit code 1 when any of
//     them threw, was not found or reported a FAIL through this kit.
//   Timing: Target(what, ms, target[, reason]) = PASS at or below the target, WARN up to 2x (or above with a recorded
//     reason), FAIL above 2x; GpuMs times a draw between two 1x1 fences (a fence that reads back a region of a large RT
//     allocates a full-size staging buffer per request and inflates the time). Never run a render row and a timing row in
//     the same Unity process.
// Numbers only: nothing is rendered for viewing; the targets are the SFW visual targets of Tools/targets.json.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.Chara.Edit;
using HS2Next.Content;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.Tests.MakerCore;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static class MakerTestKit
{
    // ================================================================ report

    public static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "maker");
    static StringBuilder rep = new StringBuilder();
    static int passes, fails, warns;
    static bool inSuite;
    static int suiteFails;

    /// <summary>The report being written (for sections and free text).</summary>
    public static StringBuilder Rep => rep;
    public static int Passes => passes;
    public static int Fails => fails;
    public static int Warns => warns;

    public static void Reset() { rep = new StringBuilder(); passes = fails = warns = 0; }

    public static bool Check(string name, bool ok, string detail = "")
    {
        rep.AppendLine($"{(ok ? "PASS" : "FAIL")}  {name}{(string.IsNullOrEmpty(detail) ? "" : "  " + detail)}");
        if (ok) passes++; else fails++;
        return ok;
    }

    public static void Warn(string name, string detail) { rep.AppendLine($"WARN  {name}  {detail}"); warns++; }

    public static void Info(string s) => rep.AppendLine("   " + s);

    /// <summary>A section header, then <paramref name="a"/>; an exception is one FAIL of the section, not the end of the run.</summary>
    public static void Guard(string id, Action a)
    {
        rep.AppendLine();
        rep.AppendLine("== " + id);
        var t = Flag("-partTimes") ? Stopwatch.StartNew() : null;
        try { a(); }
        catch (Exception e) { Check(id + ": no exception", false, e.GetType().Name + ": " + e.Message + " " + Short(e.StackTrace, 1200)); }
        if (t != null) rep.AppendLine($"   part time: {t.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)} s (-partTimes)");
    }

    /// <summary>Writes the report; outside a suite run a FAIL ends Unity with exit code 1.</summary>
    public static void Finish(string file, string title, Stopwatch sw, string tag = "MAKER TEST")
    {
        rep.Insert(0, $"SUMMARY: {passes} PASS, {fails} FAIL{(warns > 0 ? ", " + warns + " WARN" : "")} ({sw.Elapsed.TotalSeconds:F0} s)\n{title}\n");
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(Path.Combine(OutDir, file), rep.ToString().Replace("\r\n", "\n"), new UTF8Encoding(false));
        Debug.Log($"{tag} DONE ({file}): {passes} PASS, {fails} FAIL, {warns} WARN");
        suiteFails += fails;
        if (fails > 0 && !inSuite && !Flag("-suite")) TestExit.Exit(1);
    }

    /// <summary>Runs the static parameterless methods named by -suite "Class.Method,..." in one Unity session, then ends
    /// Unity: exit code 1 when a method was not found, threw, or reported a FAIL through this kit.</summary>
    public static void RunSuite()
    {
        inSuite = true; suiteFails = 0;
        try
        {
            foreach (var name in (Arg("-suite") ?? "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0))
            {
                int dot = name.LastIndexOf('.');
                var type = dot > 0 ? AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name.Substring(0, dot))).FirstOrDefault(t => t != null) : null;
                var m = type?.GetMethod(name.Substring(dot + 1), System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, null, Type.EmptyTypes, null);
                Debug.Log("MakerTestKit.RunSuite: " + name + (m == null ? " (not found)" : ""));
                if (m == null) { suiteFails++; continue; }
                try { m.Invoke(null, null); }
                catch (Exception e) { suiteFails++; Debug.LogError("MakerTestKit.RunSuite: " + name + " threw " + (e.InnerException ?? e)); }
            }
        }
        finally { inSuite = false; }
        Debug.Log("MakerTestKit.RunSuite: done, failures " + suiteFails);
        TestExit.Exit(suiteFails > 0 ? 1 : 0);
    }

    // ================================================================ arguments and small formatters

    /// <summary>The command-line value after <paramref name="name"/>, else <paramref name="def"/>.</summary>
    public static string Arg(string name, string def = null)
    {
        var a = TestArgs.Get();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : def;
    }

    public static bool Flag(string name) => TestArgs.Get().Contains(name);

    /// <summary>True without -only, else when <paramref name="id"/> is one of its comma / plus separated ids.</summary>
    public static bool Want(string id)
    {
        var o = Arg("-only");
        if (o != null && !o.Split(',', '+').Select(x => x.Trim()).Contains(id)) return false;
        // -skip <ids>: the -Quick variant of a row leaves its sweep-like parts to the -Full row (which runs without it)
        var k = Arg("-skip");
        if (k != null && k.Split(',', '+').Select(x => x.Trim()).Contains(id)) { rep.AppendLine(); rep.AppendLine($"== {id} skipped (-skip)"); return false; }
        return true;
    }

    /// <summary>The -reps value (default <paramref name="def"/>).</summary>
    public static int Reps(int def = 20) => int.Parse(Arg("-reps", def.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);

    public static double Median(List<double> l)
    {
        if (l == null || l.Count == 0) return double.NaN;
        var s = l.OrderBy(x => x).ToList();
        return s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2;
    }

    public static string F(double v) => v.ToString("F3", CultureInfo.InvariantCulture);
    public static string G(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    public static string Rel(string f) => Path.GetFileName(f);
    public static string Short(string s, int n = 400) => s == null ? "" : s.Length > n ? s.Substring(0, n) + "…" : s;
    public static int Bits(float f) => BitConverter.SingleToInt32Bits(f);

    public static bool BitsEqual(float[] a, float[] b) =>
        a != null && b != null && a.Length == b.Length && a.Select(BitConverter.SingleToInt32Bits).SequenceEqual(b.Select(BitConverter.SingleToInt32Bits));

    /// <summary>The user as the source of scene operations (undoable, as from the UI).</summary>
    public static readonly ExecuteOptions User = new ExecuteOptions { Source = CommandSource.User };

    // ================================================================ targets and characters

    public static string FemaleCard => TestTargets.Abs(TestTargets.Load().visual.female);
    public static string MaleCard => TestTargets.Abs(TestTargets.Load().visual.male);

    /// <summary>The four skin creators (colour and detail of face and body).</summary>
    public const string FaceC = "create_skin_face", FaceD = "create_skin detail_face", BodyC = "create_skin_body", BodyD = "create_skin detail_body";
    public static readonly string[] SkinCreators = { FaceC, FaceD, BodyC, BodyD };

    /// <summary>Disposes a built character at the end of a using block.</summary>
    public sealed class Disposer : IDisposable
    {
        public readonly MakerCoreTest.Built B;
        public Disposer(MakerCoreTest.Built b) { B = b; }
        public void Dispose() => B?.Dispose();
    }

    /// <summary>A container of the live character's current documents (and the resources they reference).</summary>
    public static CharaContainer ContainerOf(MakerCoreTest.Built live)
    {
        var container = new CharaContainer { Subtree = live.Result.Stored.Clone() };
        container.Res.CopyFrom(live.Container.Res, container.Subtree.ReferencedResources());
        return container;
    }

    /// <summary>The live character's snapshot vs a fresh build of its current documents; null when the fresh build failed
    /// (<paramref name="error"/> set). <paramref name="extra"/> sees both characters after their snapshots (each stepped once
    /// by 0, so a fresh build's hair chains are active as the live ones are).</summary>
    public static SnapshotDiff VsFresh(MakerCoreTest.Harness h, MakerCoreTest.Built live, out string error, Action<MakerCoreTest.Built, MakerCoreTest.Built> extra = null)
    {
        error = null;
        var sA = live.Snapshot();
        using (var fresh = new Disposer(h.Build(ContainerOf(live), "fresh")))
        {
            if (!fresh.B.Ok) { error = fresh.B.Error; return null; }
            var sB = fresh.B.Snapshot();
            extra?.Invoke(live, fresh.B);
            return CharaSnapshot.Compare(sB, sA, 12);
        }
    }

    /// <summary>A scene with one character node imported from <paramref name="file"/>; throws when the import or the build fails.</summary>
    public static MakerEditTest.Node NodeOf(MakerCoreTest.Harness h, string file)
    {
        var c = MakerEditTest.Import(h, file, out string err);
        if (c == null) throw new Exception("import " + Rel(file) + ": " + err);
        var n = MakerEditTest.NewNode(h, c, Rel(file));
        if (!n.Ok) { n.Dispose(); throw new Exception("node " + Rel(file) + ": " + n.Error); }
        return n;
    }

    /// <summary>The live node's snapshot vs a fresh node of its current documents.</summary>
    public static SnapshotDiff VsFresh(MakerCoreTest.Harness h, MakerEditTest.Node n)
    {
        var s = n.Snapshot();
        using (var fresh = MakerEditTest.NewNode(h, n.Session.Model.File.ExtractCharacter(n.Id), n.Label + " (fresh)"))
        {
            if (!fresh.Ok) throw new Exception("fresh node: " + fresh.Error);
            return CharaSnapshot.Compare(fresh.Snapshot(), s, 12);
        }
    }

    /// <summary>Sets a member of the live documents, marks its path and flushes the editor.</summary>
    public static FlushReport Set(MakerCoreTest.Built live, string path, Action set)
    {
        set();
        live.Context.Editor.MarkDirty(path);
        return live.Context.Editor.Flush();
    }

    // ================================================================ families and value mutators

    /// <summary>One member of a family table: how to read, write and change it, and the creators its flush must rebuild.</summary>
    public sealed class Family
    {
        public string Name, Path;
        public Func<object> Get;
        public Action<object> Set;
        public Func<object, object> Alt;
        /// <summary>Creator names the flush must rebuild (exactly).</summary>
        public string[] Expect;
    }

    /// <summary>Writes the old value back, marks the member's path and flushes.</summary>
    public static void SetBack(Family fam, object old, CharaEditor ed)
    {
        fam.Set(old);
        ed.MarkDirty(fam.Path);
        ed.Flush();
    }

    /// <summary>Rebuild counters of the named creators (default: the four skin creators).</summary>
    public static Dictionary<string, int> RebuildCounts(EditContext x, IEnumerable<string> names = null)
    {
        var set = new HashSet<string>(names ?? SkinCreators, StringComparer.Ordinal);
        return x.Composites().Where(c => set.Contains(c.Name)).ToDictionary(c => c.Name, c => c.Rebuilds);
    }

    /// <summary>Another row of the category: the first vanilla row other than <paramref name="current"/> that passes
    /// <paramref name="ok"/>, else the first such mod row; null when there is none.</summary>
    public static string NextRow(ContentCatalog cat, int category, string current, Func<ListEntry, bool> ok = null)
    {
        var rows = cat.EntriesOf(category);
        ContentId.TryParse(current, out var cur);
        foreach (var r in rows) if (r.IsVanilla && r.Key != cur && (ok == null || ok(r))) return r.Key.ToString();
        foreach (var r in rows) if (r.Key != cur && (ok == null || ok(r))) return r.Key.ToString();
        return null;
    }

    /// <summary>The list column names a texture (not empty, not "0").</summary>
    public static bool HasTex(ListEntry r, string col) => !string.IsNullOrEmpty(r[col]) && r[col] != "0";

    public static float Fl(object v) => (float)v;
    /// <summary>A different float inside 0..1 for values inside 0..1.</summary>
    public static object AltF(object v) { float f = (float)v; return f < 0.6f ? f + 0.37f : f - 0.41f; }
    /// <summary>A different colour with the same alpha.</summary>
    public static object AltC(object v) { var c = (Color4)v; return new Color4(c.R * 0.5f + 0.13f, c.G * 0.25f + 0.31f, 1 - c.B * 0.5f, c.A); }
    /// <summary>A different layout vector.</summary>
    public static object AltV4(object v) { var c = (Vec4)v; return new Vec4(c.X * 0.5f + 0.1f, c.Y * 0.75f + 0.2f, 1 - c.Z, c.W * 0.5f + 0.3f); }
    /// <summary>A different integer inside 0..<paramref name="count"/>-1.</summary>
    public static object AltI(object v, int count) { int i = (int)v; return count <= 1 ? (object)i : (i + 1) % count; }
    public static object AltB(object v) => !(bool)v;

    // ================================================================ timing (graphics rows; their own Unity session)

    /// <summary>PASS at or below the target, WARN up to 2x the target (or above it with a recorded reason), FAIL above 2x;
    /// NaN is reported as not measured.</summary>
    public static void Target(string what, double ms, double target, string reason = null)
    {
        string d = $"{F(ms)} ms (target {F(target)})";
        if (double.IsNaN(ms)) { Info(what + ": not measured" + (reason != null ? " (" + reason + ")" : "")); return; }
        if (ms <= target) Check(what, true, d);
        else if (ms <= 2 * target || reason != null) Warn(what, d + (reason != null ? "; " + reason : ""));
        else Check(what, false, d + " (above 2× the target)");
    }

    static RenderTexture tinyFence;

    /// <summary>Waits for the GPU work submitted so far: one texel of <paramref name="rt"/> copied into a 1x1 RT, and that
    /// RT read back (a region read-back of a large RT allocates a full-size staging buffer per request).</summary>
    public static void Fence(RenderTexture rt)
    {
        if (!rt) return;
        if (!tinyFence || tinyFence.graphicsFormat != rt.graphicsFormat)
        {
            ReleaseFence();
            var d = rt.descriptor;
            d.width = d.height = 1; d.useMipMap = false; d.autoGenerateMips = false; d.depthBufferBits = 0; d.msaaSamples = 1;
            tinyFence = new RenderTexture(d) { name = "MakerTestKit fence" };
            tinyFence.Create();
        }
        Graphics.CopyTexture(rt, 0, 0, 0, 0, 1, 1, tinyFence, 0, 0, 0, 0);
        AsyncGPUReadback.Request(tinyFence).WaitForCompletion();
    }

    /// <summary>Releases the fence RT (at the end of a timing run).</summary>
    public static void ReleaseFence()
    {
        if (!tinyFence) return;
        tinyFence.Release();
        Object.DestroyImmediate(tinyFence);
        tinyFence = null;
    }

    /// <summary>Median wall time of <paramref name="draw"/> between two fences on <paramref name="rt"/> over
    /// <paramref name="reps"/> repetitions; <paramref name="cpu"/> collects the CPU part (until the draw returned).</summary>
    public static double GpuMs(Action draw, RenderTexture rt, int reps, List<double> cpu = null)
    {
        var l = new List<double>();
        for (int i = 0; i < reps; i++)
        {
            Fence(rt);
            var t = Stopwatch.StartNew();
            draw();
            double c = t.Elapsed.TotalMilliseconds;
            Fence(rt);
            l.Add(t.Elapsed.TotalMilliseconds);
            cpu?.Add(c);
        }
        return Median(l);
    }

    /// <summary>Time of one scene operation and the realization's apply that follows it.</summary>
    public static double TimeOp(MakerEditTest.Node n, Func<OpResult> op, out OpResult res)
    {
        var t = Stopwatch.StartNew();
        res = op();
        n.Apply();
        return t.Elapsed.TotalMilliseconds;
    }

    /// <summary>Median time of <paramref name="reps"/> runs of <paramref name="a"/> (CPU, no fence).</summary>
    public static double CpuMs(Action a, int reps)
    {
        var l = new List<double>();
        for (int i = 0; i < reps; i++)
        {
            var t = Stopwatch.StartNew();
            a();
            l.Add(t.Elapsed.TotalMilliseconds);
        }
        return Median(l);
    }

    /// <summary>A synthetic noise texture with mips (inputs of composite timings; never shown).</summary>
    public static Texture2D Noise(int w, int h, int seed)
    {
        var rnd = new System.Random(seed);
        var t = new Texture2D(w, h, TextureFormat.RGBA32, true);
        var px = new Color32[w * h];
        for (int i = 0; i < px.Length; i++) px[i] = new Color32((byte)rnd.Next(256), (byte)rnd.Next(256), (byte)rnd.Next(256), (byte)rnd.Next(256));
        t.SetPixels32(px);
        t.Apply(true);
        return t;
    }
}
