// Generated pose thumbnails: the stick figures of the HS2
// pose files without an image. Headless (-nographics), edit mode; nothing is rendered by a camera. Pose files are read from
// <HS2>/UserData/studio/pose (read only); the figures and the rig cache go to a test cache folder
// (Spike/out/pose_thumbs/cache: the test removes only its own files there, `poses/stick/<16 hex>.png` and `rig_[fm].tsv`).
// Run: -executeMethod PoseThumbTest.Run with -NoQuit (the test ends Unity) [-sample N (60)] [-all] [-dump <joints.tsv>] [-only T1,T2]
//   T1  rigs: both sexes built from the install (rest data written to the cache), again from the cache (same data, no
//       bundle); FK groups and IK bones found; rest heights (male taller), head unit
//   T2  a sample of N files spread over the library: posed, drawn, encoded without an exception; every figure complete,
//       finite, non-empty and inside the image's margin; time per pose (pose, draw, encode); two contact sheets (dark and
//       light backgrounds) of the sample in Spike/out/pose_thumbs/ (line drawings only)
//   T3  determinism: one fixed pose drawn twice on fresh rigs: the same pixels and PNG bytes; the PNG decodes to the pixels
//   T4  FK: the rig's figure = the double-precision forward kinematics of the rest data with the same values (FK files)
//   T5  IK: every file with enableIK: posed; for a converted file each applied limb's end lands on its target (unless the
//       import reports it unreachable); the hands of IK files move away from the rest pose
//   T6  the generator (PoseStickThumbs): requests → textures (256²) through Provide, PNGs in the disk cache, a second
//       generator finds them (CachedFile) without drawing; main-thread time per frame within the 4 ms budget; a missing
//       file falls back (Withdraw, counted once)
//   T7  (-all) every file without an image: total time, failures, managed memory
//   T8  through a ThumbnailLoader (the browser's path): figures drawn into its LRU (256² = 256 KB each), then a second
//       loader and generator (the next session) read every one from the disk cache without drawing; a pose that cannot be
//       drawn is withdrawn without the error tile. The cached files are decoded by UnityWebRequest, which completes only
//       between editor updates: with -NoQuit (the row's form) the test waits for them on EditorApplication.update and ends
//       Unity itself; under -quit (or in a batch) it checks that the requests started on the cached files and says so.
// Report Spike/out/pose_thumbs/pose_thumb_report.txt, first line "SUMMARY: N PASS, M FAIL"; exit 1 on a FAIL.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using HS2Next.Content;
using HS2Next.Import.Pose;
using HS2Next.Motion.Fk;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Core;
using HS2Next.UI.Panels;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using PoseEntry = HS2Next.UI.Panels.PoseEntry;

public static class PoseThumbTest
{
    static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "pose_thumbs");
    static StringBuilder rep;
    static int passes, fails;
    static string current = "";
    static readonly Dictionary<string, (int pass, int fail)> perTest = new Dictionary<string, (int, int)>();
    static readonly List<string> measurements = new List<string>();
    static string[] only;
    static RuntimeServices services;
    static string cacheDir, poseRoot;
    static List<PoseEntry> drawable;

    static string Arg(string name)
    {
        var a = TestArgs.Get();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
    }

    static bool Flag(string name) => Array.IndexOf(TestArgs.Get(), name) >= 0;

    static void Section(string id, string title) { current = id; rep.AppendLine(); rep.AppendLine($"== {id} {title}"); }
    static void Info(string s) => rep.AppendLine("   " + s);
    static void Check(string name, bool ok, string detail = "")
    {
        rep.AppendLine($"{(ok ? "PASS" : "FAIL")}  [{current}] {name}{(string.IsNullOrEmpty(detail) ? "" : "  " + detail)}");
        if (ok) passes++; else fails++;
        perTest.TryGetValue(current, out var v);
        perTest[current] = ok ? (v.pass + 1, v.fail) : (v.pass, v.fail + 1);
    }

    static void Guard(string id, Action a)
    {
        if (only != null && !only.Contains(id)) return;
        current = id;
        try { a(); }
        catch (Exception e) { Check("exception", false, e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); }
    }

    public static void Run()
    {
        rep = new StringBuilder(); passes = fails = 0; perTest.Clear(); measurements.Clear(); deferred = null;
        var o = Arg("-only");
        only = o == null ? null : o.Split(',', '+').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
        Directory.CreateDirectory(OutDir);
        var sw = Stopwatch.StartNew();
        var head = new StringBuilder();
        try
        {
            services = SceneRuntimeTest.NewServices(head);
            poseRoot = Path.Combine(services.Hs2Root, "UserData", "studio", "pose");
            cacheDir = Path.Combine(OutDir, "cache");
            CleanOwnCache(cacheDir);
            rep.AppendLine($"pose thumbnail test (Unity {Application.unityVersion}); pose library {(Directory.Exists(poseRoot) ? "found" : "missing")}");
            rep.Append(head);
            var scan = Stopwatch.StartNew();
            var files = PoseFiles.Scan(poseRoot, null, null);
            drawable = files.Entries.Where(e => e.Status == PoseEntryStatus.Pose && !e.HasImage && !e.IsHs2n).ToList();
            Info($"library: {files.Entries.Count} files, {files.ListedCount} listed, {drawable.Count} without an image (drawn), scan {scan.Elapsed.TotalSeconds:0.0} s");
            Guard("T1", T1);
            Guard("T2", T2);
            Guard("T3", T3);
            Guard("T4", T4);
            Guard("T5", T5);
            Guard("T6", T6);
            if (Flag("-all")) Guard("T7", T7);
            Guard("T8", T8);
        }
        catch (Exception e) { current = "run"; Check("run (exception)", false, e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); }
        clock = sw;
        if (deferred != null)
        {
            EditorApplication.update += Tick;
            return;
        }
        Finish();
    }

    static Stopwatch clock;
    // the next-session half of T8: a step per editor update, true when done
    static Func<bool> deferred;
    static bool CanDefer => !HeadlessBatch.InMember && Array.IndexOf(Environment.GetCommandLineArgs(), "-quit") < 0;

    static void Tick()
    {
        bool done;
        try { done = deferred == null || deferred(); }
        catch (Exception e) { current = "T8"; Check("exception (next session)", false, e.GetType().Name + ": " + e.Message); done = true; }
        if (!done) return;
        EditorApplication.update -= Tick;
        deferred = null;
        Finish();
    }

    static void Finish()
    {
        foreach (var r in rigs) r?.Dispose(); rigs.Clear(); PoseStickThumbs.DisposeShared();
        var sw = clock;
        var sum = new StringBuilder();
        sum.AppendLine($"SUMMARY: {passes} PASS, {fails} FAIL ({sw.Elapsed.TotalSeconds:F0} s)");
        foreach (var kv in perTest.OrderBy(k => k.Key, StringComparer.Ordinal)) sum.AppendLine($"  {kv.Key}: {kv.Value.pass} pass, {kv.Value.fail} fail");
        if (measurements.Count > 0) { sum.AppendLine("measurements:"); foreach (var m in measurements) sum.AppendLine("  " + m); }
        rep.Insert(0, sum.ToString());
        File.WriteAllText(Path.Combine(OutDir, "pose_thumb_report.txt"), rep.ToString());
        services?.Catalog?.UnloadAll();
        Debug.Log($"POSE THUMB TEST DONE: {passes} PASS, {fails} FAIL");
        if (fails > 0) TestExit.Exit(1);
        else if (CanDefer) TestExit.Exit(0);   // -NoQuit: the test ends Unity
    }

    // only the test's own files, in its own folder (never recursive)
    static void CleanOwnCache(string dir)
    {
        string stick = Path.Combine(dir, "poses", "stick");
        if (!Directory.Exists(stick)) return;
        var own = new Regex(@"^([0-9a-f]{16}\.png|rig_[fm]\.tsv)$");
        foreach (var f in Directory.GetFiles(stick))
            if (own.IsMatch(Path.GetFileName(f))) File.Delete(f);
    }

    static readonly List<PoseStickRig> rigs = new List<PoseStickRig>();
    static PoseStickRig[] main = new PoseStickRig[2];

    static PoseStickRig NewRig(int sex, string dir, out string problem)
    {
        var r = PoseStickRig.Create(services.Catalog, services.Db, sex, dir, out problem);
        if (r != null) rigs.Add(r);
        return r;
    }

    static string Stats(IEnumerable<double> xs)
    {
        var a = xs.OrderBy(x => x).ToArray();
        if (a.Length == 0) return "none";
        double P(double q) => a[Math.Min(a.Length - 1, (int)Math.Floor(q * (a.Length - 1) + 0.5))];
        return string.Format(CultureInfo.InvariantCulture, "n {0}, median {1:0.###}, p95 {2:0.###}, max {3:0.###}, sum {4:0.#}", a.Length, P(0.5), P(0.95), a[a.Length - 1], a.Sum());
    }

    // ------------------------------------------------------------ T1 rigs

    static void T1()
    {
        Section("T1", "rigs from the install and from the cache");
        var t = Stopwatch.StartNew();
        for (int sex = 0; sex < 2; sex++)
        {
            string label = sex == 0 ? "male" : "female";
            var r = NewRig(sex, cacheDir, out var problem);
            Check($"{label}: rig built", r != null, problem ?? "");
            if (r == null) continue;
            main[sex] = r;
            Check($"{label}: rest data read from the install", r.Source == "install", $"source {r.Source}, {r.Rest.Count} transforms, {r.BuildMs:0.0} ms");
            Check($"{label}: rig cache written", File.Exists(PoseStickRig.RigFile(cacheDir, sex)), PoseStickRig.RigFile(cacheDir, sex) != null ? "" : "no file");
            var st = r.Fk.Stats;
            Info($"{label}: FK {st}");
            Check($"{label}: FK groups found (Neck 2, Body, both hands 15)", st.Found[(int)FkGroup.Neck] == 2 && st.Found[(int)FkGroup.Body] >= 22 && st.Found[(int)FkGroup.RightHand] == 15 && st.Found[(int)FkGroup.LeftHand] == 15,
                $"neck {st.Found[(int)FkGroup.Neck]}, body {st.Found[(int)FkGroup.Body]}, hands {st.Found[(int)FkGroup.RightHand]} / {st.Found[(int)FkGroup.LeftHand]}");
            Check($"{label}: IK bones complete", r.IkBones.Missing.Count == 0 && r.IkBones.GroupAvailable.All(x => x), string.Join(", ", r.IkBones.Missing));
            var p = r.Read();
            Check($"{label}: every joint at rest", p.Complete && p.Finite, string.Join(", ", Enumerable.Range(0, StickJoints.Count).Where(j => !p.Has[j]).Select(j => StickJoints.Bones[j] ?? ("point " + j))));
            Info(string.Format(CultureInfo.InvariantCulture, "{0}: rest height {1:0.###}, head unit {2:0.###}, hips {3}, head {4}, head top {5}, left hand {6}, left foot {7}",
                label, p.RestHeight, p.HeadUnit, p.P[StickJoints.Hips], p.P[StickJoints.Head], p.P[StickJoints.HeadTop], p.P[StickJoints.HandL], p.P[StickJoints.FootL]));
            measurements.Add($"rig {label}: {r.Rest.Count} transforms, build from the install {r.BuildMs:0.0} ms, rest height {p.RestHeight:0.###}");
        }
        if (main[0] != null && main[1] != null)
            Check("the male rest figure is taller than the female", main[0].Read().RestHeight > main[1].Read().RestHeight * 1.02,
                $"{main[0].Read().RestHeight:0.###} / {main[1].Read().RestHeight:0.###}");
        for (int sex = 0; sex < 2; sex++)
        {
            if (main[sex] == null) continue;
            var t2 = Stopwatch.StartNew();
            var again = NewRig(sex, cacheDir, out var problem);
            Check($"{(sex == 0 ? "male" : "female")}: second rig from the cache", again != null && again.Source == "cache" && again.Rest.ToText() == main[sex].Rest.ToText(),
                $"source {again?.Source}, {t2.Elapsed.TotalMilliseconds:0.0} ms {problem}");
            if (again != null) measurements.Add($"rig {(sex == 0 ? "male" : "female")} from the cache {again.BuildMs:0.0} ms");
        }
        Info($"T1 {t.Elapsed.TotalSeconds:0.0} s");
    }

    // ------------------------------------------------------------ T2 sample

    static List<PoseEntry> Sample(int n)
    {
        if (drawable.Count <= n) return drawable.ToList();
        var l = new List<PoseEntry>();
        for (int i = 0; i < n; i++) l.Add(drawable[(int)((long)i * drawable.Count / n)]);
        return l;
    }

    static PoseStickRig RigFor(PoseEntry e) => main[e.Sex == 0 ? 0 : 1];

    static void T2()
    {
        Section("T2", "a sample: posed, drawn, encoded");
        int n = int.TryParse(Arg("-sample"), out int k) && k > 0 ? k : 60;
        var sample = Sample(n);
        var poseMs = new List<double>(); var drawMs = new List<double>(); var encMs = new List<double>(); var pngBytes = new List<double>();
        int errors = 0, incomplete = 0, empty = 0, outside = 0;
        var imgs = new List<StickRaster>();
        var names = new List<string>();
        string dumpFile = Arg("-dump");
        var dump = dumpFile != null ? new StringBuilder() : null;
        IEnumerable<PoseEntry> set = dumpFile != null ? drawable : sample;
        int margin = (int)Math.Floor(StickView.Margin * PoseStickThumbs.Size) - 2;
        foreach (var e in set)
        {
            bool inSample = sample.Contains(e);
            try
            {
                var rig = RigFor(e);
                if (rig == null) { errors++; continue; }
                var pose = PoseFile.ReadFile(e.FullPath);
                var t = Stopwatch.StartNew();
                var fig = rig.Apply(pose, out var info);
                double pm = t.Elapsed.TotalMilliseconds;
                if (dump != null) DumpLine(dump, e, pose, fig, info);
                if (!inSample) continue;
                poseMs.Add(pm);
                if (!fig.Complete || !fig.Finite) incomplete++;
                t.Restart();
                var img = StickDrawing.Draw(fig, PoseStickThumbs.Size, PoseStickThumbs.Size);
                drawMs.Add(t.Elapsed.TotalMilliseconds);
                if (img == null) { empty++; continue; }
                t.Restart();
                var png = StickPng.Encode(img.Pixels, img.Width, img.Height);
                encMs.Add(t.Elapsed.TotalMilliseconds);
                pngBytes.Add(png.Length);
                int cov = img.Covered(8, out int x0, out int y0, out int x1, out int y1);
                if (cov < 300) empty++;
                if (x0 < margin || y0 < margin || x1 >= img.Width - margin || y1 >= img.Height - margin) outside++;
                imgs.Add(img);
                names.Add(Rel(e.FullPath));
            }
            catch (Exception x) { errors++; if (errors <= 5) Info($"error {Rel(e.FullPath)}: {x.GetType().Name}: {x.Message}"); }
        }
        Check($"{sample.Count} sample poses without an exception", errors == 0, $"{errors} errors");
        Check("every figure complete and finite", incomplete == 0, $"{incomplete} incomplete");
        Check("every image holds a figure", empty == 0, $"{empty} empty");
        Check("every figure inside the margin", outside == 0, $"{outside} outside");
        measurements.Add("T2 pose (rig) ms: " + Stats(poseMs));
        measurements.Add("T2 draw ms: " + Stats(drawMs));
        measurements.Add("T2 PNG encode ms: " + Stats(encMs));
        measurements.Add("T2 PNG bytes: " + Stats(pngBytes));
        if (imgs.Count > 0)
        {
            WriteSheet(Path.Combine(OutDir, "sheet_dark.png"), imgs, new byte[] { 41, 43, 50 });
            WriteSheet(Path.Combine(OutDir, "sheet_light.png"), imgs, new byte[] { 238, 239, 242 });
            File.WriteAllLines(Path.Combine(OutDir, "sheet_index.txt"), names.Select((s, i) => $"{i / 10},{i % 10}\t{s}"));
            Info($"contact sheets: {imgs.Count} figures, 10 per row (sheet_dark.png, sheet_light.png, sheet_index.txt)");
        }
        if (dump != null) { File.WriteAllText(dumpFile, dump.ToString()); Info($"joints of {drawable.Count} files dumped"); }
    }

    static string Rel(string full) => full.StartsWith(poseRoot, StringComparison.OrdinalIgnoreCase) ? full.Substring(poseRoot.Length).TrimStart('\\', '/').Replace('\\', '/') : Path.GetFileName(full);

    static void DumpLine(StringBuilder sb, PoseEntry e, PoseFile pose, StickPose fig, PoseStickApplyInfo info)
    {
        sb.Append(Rel(e.FullPath)).Append('\t').Append(e.Sex).Append('\t').Append(pose.EnableFK ? 1 : 0).Append('\t').Append(pose.EnableIK ? 1 : 0)
          .Append('\t').Append(info.IkScale).Append('\t').Append(fig.HeadUnit.ToString("R", CultureInfo.InvariantCulture)).Append('\t').Append(fig.RestHeight.ToString("R", CultureInfo.InvariantCulture));
        for (int j = 0; j < StickJoints.Count; j++)
            sb.Append('\t').Append(fig.P[j].X.ToString("R", CultureInfo.InvariantCulture)).Append(' ').Append(fig.P[j].Y.ToString("R", CultureInfo.InvariantCulture)).Append(' ').Append(fig.P[j].Z.ToString("R", CultureInfo.InvariantCulture));
        sb.Append('\n');
    }

    static void WriteSheet(string file, List<StickRaster> imgs, byte[] bg)
    {
        int cols = 10, size = PoseStickThumbs.Size, gap = 4;
        int rows = (imgs.Count + cols - 1) / cols;
        int w = cols * (size + gap) + gap, h = rows * (size + gap) + gap;
        var px = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++) { px[i * 4] = bg[0]; px[i * 4 + 1] = bg[1]; px[i * 4 + 2] = bg[2]; px[i * 4 + 3] = 255; }
        for (int n = 0; n < imgs.Count; n++)
        {
            int cx = gap + (n % cols) * (size + gap);
            int cy = h - gap - size - (n / cols) * (size + gap);   // rows bottom-up: the first figure at the top left
            var src = imgs[n].Pixels;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int s = (y * size + x) * 4, d = ((cy + y) * w + cx + x) * 4;
                    float a = src[s + 3] / 255f;
                    for (int c = 0; c < 3; c++) px[d + c] = (byte)(src[s + c] * a + px[d + c] * (1 - a) + 0.5f);
                }
        }
        File.WriteAllBytes(file, StickPng.Encode(px, w, h));
    }

    // ------------------------------------------------------------ T3 determinism

    static void T3()
    {
        Section("T3", "determinism");
        var e = drawable.FirstOrDefault(x => x.Sex == 1);
        if (e == null) { Check("a female pose to draw", false); return; }
        var pose = PoseFile.ReadFile(e.FullPath);
        string a = null, b = null; byte[] pa = null, pb = null; StickRaster ia = null;
        for (int round = 0; round < 2; round++)
        {
            var rig = NewRig(1, cacheDir, out _);
            var img = StickDrawing.Draw(rig.Apply(pose, out _), PoseStickThumbs.Size, PoseStickThumbs.Size);
            var png = StickPng.Encode(img.Pixels, img.Width, img.Height);
            string h = PoseThumbKey.Hash(Convert.ToBase64String(img.Pixels));
            if (round == 0) { a = h; pa = png; ia = img; } else { b = h; pb = png; }
        }
        Check("the same pixels twice", a == b, $"{a} / {b}");
        Check("the same PNG bytes twice", pa.SequenceEqual(pb), $"{pa.Length} / {pb.Length} bytes");
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            bool ok = tex.LoadImage(pa, false);
            var back = tex.GetPixels32();
            int differ = 0;
            for (int i = 0; i < back.Length && ok; i++)
            {
                int s = i * 4;
                if (back[i].r != ia.Pixels[s] || back[i].g != ia.Pixels[s + 1] || back[i].b != ia.Pixels[s + 2] || back[i].a != ia.Pixels[s + 3]) differ++;
            }
            Check("the PNG decodes (engine decoder) to the drawn pixels", ok && tex.width == ia.Width && tex.height == ia.Height && differ == 0, $"{tex.width} × {tex.height}, {differ} pixels differ");
        }
        finally { UnityEngine.Object.DestroyImmediate(tex); }
        measurements.Add($"T3 fixed pose: pixels {a}, PNG {pa.Length} bytes");
    }

    // ------------------------------------------------------------ T4 FK math vs rig

    static void T4()
    {
        Section("T4", "the rig's FK figure = the forward kinematics of the rest data");
        double worst = 0; string worstAt = null; int n = 0;
        foreach (var e in Sample(80))
        {
            var pose = PoseFile.ReadFile(e.FullPath);
            if (pose.EnableIK || !pose.EnableFK) continue;
            var rig = RigFor(e);
            var fig = rig.Apply(pose, out _);
            var values = new Dictionary<string, StickVec>(StringComparer.Ordinal);
            for (int g = 0; g < FkGroupInfo.Count; g++)
            {
                bool applied = pose.EnableFK && g < pose.ActiveFK.Length && pose.ActiveFK[g] && g != (int)FkGroup.Skirt;
                if (!applied) continue;
                foreach (var b in rig.Fk.Of((FkGroup)g))
                {
                    if (!b.HasValue || !b.Bone) continue;
                    values[b.Bone.name] = new StickVec(b.Value.x, b.Value.y, b.Value.z);
                    if (b.Partner) values[b.Partner.name] = new StickVec(b.Value.x, b.Value.y, b.Value.z);
                }
            }
            var math = new StickSkeleton(rig.Rest).Evaluate(values);
            for (int j = 0; j < StickJoints.Count; j++)
            {
                double d = (fig.P[j] - math.P[j]).Length;
                if (d > worst) { worst = d; worstAt = Rel(e.FullPath) + " joint " + j; }
            }
            n++;
        }
        Check($"{n} FK files: the rig = the math within 1e-3 units", n > 0 && worst < 1e-3, $"worst {worst:G4} at {worstAt}");
        measurements.Add($"T4 FK rig vs math: {n} files, worst {worst:G4} units");
    }

    // ------------------------------------------------------------ T5 IK

    static void T5()
    {
        Section("T5", "IK files");
        var ik = drawable.Where(e => { try { return PoseFile.ReadFile(e.FullPath).EnableIK; } catch { return false; } }).ToList();
        int converted = 0, rejected = 0, ends = 0, endsOff = 0, moved = 0, errors = 0, clamped = 0;
        double worst = 0; string worstAt = null;
        var t = Stopwatch.StartNew();
        string[] endJoint = { null, "RightFoot", "LeftFoot", "RightHand", "LeftHand" };
        int[] endIdx = { -1, StickJoints.FootR, StickJoints.FootL, StickJoints.HandR, StickJoints.HandL };
        int[] endTarget = { -1, 12, 9, 6, 3 };
        foreach (var e in ik)
        {
            try
            {
                var pose = PoseFile.ReadFile(e.FullPath);
                var rig = RigFor(e);
                var rest = rig.Read();
                var fig = rig.Apply(pose, out var info);
                if (info.Ik == null || info.Ik.Targets == null) { rejected++; continue; }
                converted++;
                bool any = false;
                for (int g = 1; g < 5; g++)
                {
                    if (!pose.ActiveIK[g]) continue;
                    var tg = info.Ik.Targets[endTarget[g]];
                    if (tg == null || info.Ik.Unreachable.Contains(HS2Next.Save.IkTargets.Names[endTarget[g]])) continue;
                    // the solver clamps the reach from where the limb's root really is (the clavicle aim may stop short of the
                    // shoulder target): such an end stops on the line toward its target
                    if (info.Solve != null && info.Solve.Clamped[endTarget[g]]) { clamped++; continue; }
                    var p = fig.P[endIdx[g]];
                    double d = (p - new StickVec(tg.Pos.X, tg.Pos.Y, tg.Pos.Z)).Length;
                    ends++;
                    if (d > 1e-3) endsOff++;
                    if (d > worst) { worst = d; worstAt = Rel(e.FullPath) + " " + endJoint[g]; }
                    if ((p - rest.P[endIdx[g]]).Length > 0.5) any = true;
                }
                if (any) moved++;
            }
            catch (Exception x) { errors++; if (errors <= 5) Info($"error {Rel(e.FullPath)}: {x.GetType().Name}: {x.Message}"); }
        }
        Info($"{ik.Count} IK files: {converted} converted, {rejected} not converted (unknown scale: drawn on the FK base / rest pose), {t.Elapsed.TotalMilliseconds:0} ms");
        Check("IK files posed without an exception", errors == 0, $"{errors} errors");
        Check("applied limb ends land on their targets (1e-3)", ends > 0 && endsOff == 0, $"{ends} ends, {endsOff} off, worst {worst:G4} at {worstAt}; {clamped} ends clamped by the solver left out");
        Check("IK moves a hand or foot of most converted files away from the rest pose", converted == 0 || moved >= converted * 0.8, $"{moved} / {converted}");
        measurements.Add($"T5 IK: {ik.Count} files, {converted} converted, {ends} ends on target (worst {worst:G4}), {clamped} clamped");
    }

    // ------------------------------------------------------------ T6 generator

    static void T6()
    {
        Section("T6", "the generator: textures, disk cache, budget, fallback");
        var sample = Sample(40);
        var got = new Dictionary<string, Texture2D>();
        var withdrawn = new List<string>();
        var failedEntries = new List<PoseEntry>();
        var gen = new PoseStickThumbs(() => services.Catalog, () => services.Db, () => cacheDir)
        {
            Provide = (k, tx) => got[k] = tx,
            Withdraw = k => withdrawn.Add(k),
        };
        gen.Failed += e => failedEntries.Add(e);
        var frames = new List<double>();
        try
        {
            // a file that does not exist: falls back once
            var ghost = new PoseEntry { FullPath = Path.Combine(OutDir, "no such pose.dat"), FileName = "no such pose", Root = "hs2", Folder = "", Status = PoseEntryStatus.Pose, Sex = 1 };
            foreach (var e in sample) gen.Want(gen.KeyOf(e));
            gen.Want(gen.KeyOf(ghost));
            gen.Want(gen.KeyOf(ghost));   // twice: one job
            // the rigs are built on the first pump (not counted in the frame budget)
            gen.RigOf(0); gen.RigOf(1);
            var t = Stopwatch.StartNew();
            while (gen.Pending > 0 && t.Elapsed.TotalSeconds < 120)
            {
                var f = Stopwatch.StartNew();
                gen.Pump(4);
                frames.Add(f.Elapsed.TotalMilliseconds);
                System.Threading.Thread.Sleep(2);
            }
            Check("every request answered", gen.Pending == 0, $"{gen.Pending} pending after {t.Elapsed.TotalSeconds:0.0} s");
            Check($"{sample.Count} textures of {PoseStickThumbs.Size}²", got.Count == sample.Count && got.Values.All(x => x && x.width == PoseStickThumbs.Size && x.height == PoseStickThumbs.Size),
                $"{got.Count} textures, generated {gen.Generated}");
            Check("the missing file falls back once (withdrawn, counted, Failed raised once)", gen.FailedCount == 1 && failedEntries.Count == 1 && withdrawn.Count >= 1, $"failed {gen.FailedCount}, events {failedEntries.Count}, withdrawn {withdrawn.Count}; {string.Join("; ", gen.Problems)}");
            Check("the failed file no longer asks for a figure (text tile)", !gen.Draws(ghost));
            int files = sample.Count(e => gen.CachedFile(gen.KeyOf(e)) != null);
            Check("every figure written to the disk cache", files == sample.Count && gen.DiskWritten == sample.Count, $"{files} files, {gen.DiskWritten} written");
            // the main-thread budget: pumps beyond 4 ms by more than one step (a slow step finishes) are counted
            double maxStep = gen.MainMs.Count > 0 ? gen.MainMs.Max() : 0;
            int over = frames.Count(f => f > 4 + maxStep);
            Check("main-thread time per pump within the 4 ms budget (+ one step)", over == 0, $"{frames.Count} pumps, max {frames.DefaultIfEmpty(0).Max():0.###} ms, longest step {maxStep:0.###} ms, {over} over");
            measurements.Add("T6 main-thread ms per pose (pose or texture step): " + Stats(gen.MainMs));
            measurements.Add("T6 pumps ms: " + Stats(frames));
            measurements.Add("T6 request → texture ms: " + Stats(gen.TotalMs));
            measurements.Add("T6 read / pose / draw / encode ms medians: " + string.Join(" / ", new[] { gen.ReadMs, gen.PoseMs, gen.DrawMs, gen.EncodeMs }.Select(l => l.Count == 0 ? "-" : l.OrderBy(x => x).ElementAt(l.Count / 2).ToString("0.###", CultureInfo.InvariantCulture))));
            measurements.Add($"T6 rigs built in {gen.RigBuildMs:0.0} ms (from the cache)");
            // a second generator (another session): the cached files are found, nothing is drawn
            var gen2 = new PoseStickThumbs(() => services.Catalog, () => services.Db, () => cacheDir) { Provide = (k, tx) => UnityEngine.Object.DestroyImmediate(tx) };
            int hits = sample.Count(e => gen2.CachedFile(gen2.KeyOf(e)) != null);
            Check("a second generator finds every cached figure without drawing", hits == sample.Count && gen2.Generated == 0, $"{hits} hits");
            // the cached PNG is the drawn image
            var first = sample[0];
            var png = File.ReadAllBytes(gen2.CachedFile(gen2.KeyOf(first)));
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                tex.LoadImage(png, false);
                var img = StickDrawing.Draw(RigFor(first).Apply(PoseFile.ReadFile(first.FullPath), out _), PoseStickThumbs.Size, PoseStickThumbs.Size);
                var back = tex.GetPixels32();
                int differ = 0;
                for (int i = 0; i < back.Length; i++) if (back[i].a != img.Pixels[i * 4 + 3] || back[i].r != img.Pixels[i * 4]) differ++;
                Check("the cached PNG holds the drawn figure", differ == 0, $"{differ} pixels differ");
            }
            finally { UnityEngine.Object.DestroyImmediate(tex); }
            gen2.Dispose();
            // the key changes with the file's time (another drawing)
            var moved = new PoseEntry { FullPath = first.FullPath, Sex = first.Sex, Size = first.Size, ModifiedUtc = first.ModifiedUtc.AddSeconds(1), Status = PoseEntryStatus.Pose };
            Check("another modification time → another cache file", gen.CacheFileOf(moved) != gen.CacheFileOf(first));
        }
        finally
        {
            foreach (var tx in got.Values) if (tx) UnityEngine.Object.DestroyImmediate(tx);
            gen.Dispose();
        }
    }

    // ------------------------------------------------------------ T8 the loader path

    static void T8()
    {
        Section("T8", "through the thumbnail loader");
        // files T6 has not drawn (T6 wrote the first sample's PNGs)
        var t6 = new HashSet<PoseEntry>(Sample(40));
        var set = drawable.Where(e => !t6.Contains(e)).Where((e, i) => i % 157 == 3).Take(24).ToList();
        var ghost = new PoseEntry { FullPath = Path.Combine(OutDir, "no such pose 2.dat"), FileName = "no such pose 2", Root = "hs2", Folder = "", Status = PoseEntryStatus.Pose, Sex = 0 };
        var owner = new object();
        // the first session: drawn into the loader
        var loader = new ThumbnailLoader();
        var gen = new PoseStickThumbs(() => services.Catalog, () => services.Db, () => cacheDir);
        gen.Attach(loader);
        try
        {
            var keys = set.Select(e => gen.KeyOf(e)).ToList();
            foreach (var k in keys) loader.Request(k, owner);
            string gk = gen.KeyOf(ghost);
            loader.Request(gk, owner);
            var t = Stopwatch.StartNew();
            int frames = 0;
            double worst = 0;
            while (t.Elapsed.TotalSeconds < 60 && (keys.Any(k => loader.TryGet(k) == null) || gen.Pending > 0))
            {
                var f = Stopwatch.StartNew();
                loader.Update(4);
                gen.Pump(Math.Max(0, 4 - f.Elapsed.TotalMilliseconds));
                worst = Math.Max(worst, f.Elapsed.TotalMilliseconds);
                frames++;
                System.Threading.Thread.Sleep(2);
            }
            int loaded = keys.Count(k => loader.TryGet(k) != null);
            Check($"first session: {keys.Count} figures in the loader", loaded == keys.Count && keys.All(k => loader.TryGet(k).width == PoseStickThumbs.Size),
                $"{loaded} loaded in {t.Elapsed.TotalMilliseconds:0} ms over {frames} frames (longest frame {worst:0.##} ms)");
            Check("first session: every figure drawn (none from the cache)", loader.GeneratedCount == keys.Count && loader.GeneratedFromCache == 0, $"generated {loader.GeneratedCount}, from the cache {loader.GeneratedFromCache}");
            Check("first session: 256 KB per figure in the LRU", loader.CurrentBytes == (long)keys.Count * PoseStickThumbs.Size * PoseStickThumbs.Size * 4, $"{loader.CurrentBytes} bytes");
            Check("a pose that cannot be drawn is withdrawn, not an error tile", loader.Withdrawn >= 1 && !loader.IsFailed(gk) && !gen.Draws(ghost) && gen.FailedCount == 1, $"withdrawn {loader.Withdrawn}, failed {gen.FailedCount}");
            measurements.Add($"T8 first session: {keys.Count} figures drawn through the loader in {t.Elapsed.TotalMilliseconds:0} ms ({frames} frames, longest {worst:0.##} ms)");
        }
        finally { loader.Clear(); gen.Dispose(); }
        // the next session: a fresh loader and generator; the requests start on the cached files
        var loader2 = new ThumbnailLoader();
        var gen2 = new PoseStickThumbs(() => services.Catalog, () => services.Db, () => cacheDir);
        gen2.Attach(loader2);
        var keys2 = set.Select(e => gen2.KeyOf(e)).ToList();
        foreach (var k in keys2) loader2.Request(k, owner);
        loader2.Update(4);
        Check("next session: the requests start on the cached files, nothing is drawn", loader2.InFlight > 0 && loader2.InFlight + loader2.Waiting == keys2.Count && gen2.Pending == 0 && gen2.Generated == 0,
            $"in flight {loader2.InFlight}, waiting {loader2.Waiting}, generator pending {gen2.Pending}");
        if (!CanDefer)
        {
            Info("next session: the cached files' decode is not waited for (it completes between editor updates; run with -NoQuit)");
            loader2.Clear(); gen2.Dispose();
            return;
        }
        var t2 = Stopwatch.StartNew();
        int ticks = 0;
        deferred = () =>
        {
            ticks++;
            current = "T8";
            loader2.Update(4);
            gen2.Pump(4);
            bool all = keys2.All(k => loader2.TryGet(k) != null);
            if (!all && t2.Elapsed.TotalSeconds < 60) return false;
            int loaded = keys2.Count(k => loader2.TryGet(k) != null);
            Check($"next session: {keys2.Count} figures in the loader", loaded == keys2.Count && keys2.All(k => loader2.TryGet(k)?.width == PoseStickThumbs.Size),
                $"{loaded} loaded in {t2.Elapsed.TotalMilliseconds:0} ms over {ticks} editor updates");
            Check("next session: every figure read from the disk cache, none drawn", loader2.GeneratedFromCache == keys2.Count && gen2.Generated == 0 && loader2.GeneratedCount == 0,
                $"from the cache {loader2.GeneratedFromCache}, drawn {gen2.Generated}");
            measurements.Add($"T8 next session: {keys2.Count} figures from the disk cache in {t2.Elapsed.TotalMilliseconds:0} ms ({ticks} editor updates)");
            loader2.Clear(); gen2.Dispose();
            return true;
        };
    }

    // ------------------------------------------------------------ T7 every file

    static void T7()
    {
        Section("T7", "every file without an image");
        long mem0 = GC.GetTotalMemory(true);
        var t = Stopwatch.StartNew();
        var poseMs = new List<double>(); var drawMs = new List<double>(); var encMs = new List<double>();
        int errors = 0; long bytes = 0;
        foreach (var e in drawable)
        {
            try
            {
                var rig = RigFor(e);
                var pose = PoseFile.ReadFile(e.FullPath);
                var s = Stopwatch.StartNew();
                var fig = rig.Apply(pose, out _);
                poseMs.Add(s.Elapsed.TotalMilliseconds);
                s.Restart();
                var img = StickDrawing.Draw(fig, PoseStickThumbs.Size, PoseStickThumbs.Size);
                drawMs.Add(s.Elapsed.TotalMilliseconds);
                s.Restart();
                bytes += StickPng.Encode(img.Pixels, img.Width, img.Height).Length;
                encMs.Add(s.Elapsed.TotalMilliseconds);
            }
            catch (Exception x) { errors++; if (errors <= 5) Info($"error {Rel(e.FullPath)}: {x.GetType().Name}: {x.Message}"); }
        }
        long mem1 = GC.GetTotalMemory(false);
        Check($"{drawable.Count} files without an exception", errors == 0, $"{errors} errors");
        measurements.Add($"T7 every file ({drawable.Count}) one after another on one thread: {t.Elapsed.TotalSeconds:0.0} s; PNGs {bytes / 1048576.0:0.0} MB in all ({(drawable.Count > 0 ? bytes / drawable.Count : 0)} bytes each)");
        measurements.Add("T7 pose ms: " + Stats(poseMs));
        measurements.Add("T7 draw ms: " + Stats(drawMs));
        measurements.Add("T7 encode ms: " + Stats(encMs));
        measurements.Add($"T7 managed heap {mem0 / 1048576.0:0.0} → {mem1 / 1048576.0:0.0} MB (before a collection)");
    }
}
