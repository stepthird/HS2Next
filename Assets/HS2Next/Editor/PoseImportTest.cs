// Pose import tests (I1–I5; package F).
// Run: -executeMethod PoseImportTest.Run [-only I1,I3] [-skip I3] (headless, -nographics, edit mode, ManualStepping).
//   -skip leaves the named groups out (the -Quick row skips I3, the whole-library plan; the -Full row runs it).
//   I1  the poses of Tools/compare_poses.json (checked against the table first) into the compare card (BR-Chan, a fresh
//       character per pose): values, hips offset, groups, correction, face, anime, report counts, the hs2.pose hash; time
//   I2  undo / redo restore the canonical node bytes (inline ext included)
//   I3  corpus: PoseLibrary.Scan of the whole library planned against a female and a male name set (no character per
//       file): exceptions, key coverage, hips offsets, both-modes files, Skirt forced off, anime
//       resolution, time
//   I4  single files: AI family (remap on / off), sex mismatch, IK only and both modes, a PNG pose, PoseToolsData with
//       and without skirt values (SkirtNeedsData both ways), node keys on a vanilla body, gameName on a synthetic trailer
//   I5  save → load after an import; the hs2.pose block through save, duplicate and delete → undo
//   I6  (3.4) corpus IK plan: every enableIK payload converted against BR-Chan's lengths
//       (scale classes measured, no exception, body shift ≤ 0.2 for all-groups files, time per file); U5: the files
//       applied one after another to one BR-Chan and solved: |Spine01 − the file's Body target|
//   I7  the 8 comparison poses and the 2 HS2Next-only poses applied: report fields, hs2.pose.ik scale /
//       bodyShift, targets = file × factor (Body: − bodyShift), both-modes Body FK off, IK-only FK off, one entry
//   I8  ik.copy from:"import" (KinematicCopies.IkFromImport) on a 2.3-style node = the import's targets bit for bit
// Pose files are read from <HS2>/UserData/studio/pose (read-only); nothing is rendered. Report:
// Spike/out/pose/pose_import_report.txt, first line "SUMMARY: N PASS, M FAIL"; EditorApplication.Exit(1) on any FAIL.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using HS2Next.Content;
using HS2Next.Import.Pose;
using HS2Next.Motion.Fk;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

public static class PoseImportTest
{
    static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "pose");
    static StringBuilder rep;
    static int passes, fails;
    static string current = "";
    static readonly Dictionary<string, (int pass, int fail)> perTest = new Dictionary<string, (int, int)>();
    static string[] only, skip;
    static RuntimeServices services;
    static string femaleCard, maleCard, poseRoot;
    static JObject table;

    static string Arg(string name)
    {
        var a = TestArgs.Get();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
    }

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
        if (skip != null && skip.Contains(id)) { rep.AppendLine(); rep.AppendLine($"== {id} skipped (-skip)"); return; }
        current = id;
        try { a(); }
        catch (Exception e) { Check("exception", false, e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); }
    }

    public static void Run()
    {
        rep = new StringBuilder(); passes = fails = 0; perTest.Clear(); ikMeasurements.Clear();
        var o = Arg("-only");
        only = o == null ? null : o.Split(',', '+').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
        var k = Arg("-skip");
        skip = k == null ? null : k.Split(',', '+').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
        Directory.CreateDirectory(OutDir);
        var sw = Stopwatch.StartNew();
        var oldFk = CharaPoseDrivers.Fk;
        var head = new StringBuilder();
        try
        {
            if (!(CharaPoseDrivers.Fk is FkDriver)) CharaPoseDrivers.Fk = FkDriver.Instance;
            services = SceneRuntimeTest.NewServices(head);
            var t = TestTargets.Load();
            femaleCard = TestTargets.Abs(t.visual.female);
            maleCard = TestTargets.Abs(t.visual.male);
            poseRoot = Path.Combine(services.Hs2Root, "UserData", "studio", "pose");
            table = JObject.Parse(File.ReadAllText(Path.Combine(ContentSettings.ProjectRoot, "Tools", "compare_poses.json")));
            rep.AppendLine($"pose import test (Unity {Application.unityVersion}); FK driver {(oldFk == null ? "attached by the test" : "registered by default")}; pose library {(Directory.Exists(poseRoot) ? "found" : "missing")}");
            rep.Append(head);
            Guard("I1", I1);
            Guard("I2", I2);
            Guard("I3", I3);
            Guard("I4", I4);
            Guard("I5", I5);
            Guard("I6", I6);
            Guard("I7", I7);
            Guard("I8", I8);
        }
        catch (Exception e) { current = "run"; Check("run (exception)", false, e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); }
        finally { CharaPoseDrivers.Fk = oldFk; }
        var sum = new StringBuilder();
        sum.AppendLine($"SUMMARY: {passes} PASS, {fails} FAIL ({sw.Elapsed.TotalSeconds:F0} s)");
        foreach (var kv in perTest.OrderBy(k => k.Key, StringComparer.Ordinal)) sum.AppendLine($"  {kv.Key}: {kv.Value.pass} pass, {kv.Value.fail} fail");
        if (ikMeasurements.Count > 0) { sum.AppendLine("measurements:"); foreach (var m in ikMeasurements) sum.AppendLine("  " + m); }
        rep.Insert(0, sum.ToString());
        File.WriteAllText(Path.Combine(OutDir, "pose_import_report.txt"), rep.ToString());
        services?.Catalog?.UnloadAll();
        Debug.Log($"POSE IMPORT TEST DONE: {passes} PASS, {fails} FAIL");
        if (fails > 0) TestExit.Exit(1);
    }

    // ================================================================ fixture

    sealed class Env : IDisposable
    {
        public readonly SceneSession S;
        public readonly SceneRuntime Rt;

        public Env(ulong seed = 0x9051UL)
        {
            S = SceneRuntimeTest.NewSession(services, seed);
            Rt = new SceneRuntime(S, services, null, false);
        }

        public string Add(string card)
        {
            var r = Rt.Ops.AddCharaFromCard(card);
            if (r == null || !r.Ok) throw new Exception("add " + Path.GetFileName(card) + ": " + r);
            Rt.ApplyNow();
            return r.NewId;
        }

        public CharaNodeState St(string id) => (CharaNodeState)S.Model.Find(id).State;
        public JToken Block(string id) => S.Model.Find(id)?.Doc.Ext.Get(PoseImport.ProvenanceNamespace);
        public void Dispose() => Rt.Dispose();
    }

    static string Sha(byte[] b)
    {
        using (var h = SHA256.Create()) return string.Concat(h.ComputeHash(b).Select(x => x.ToString("x2")));
    }

    static bool Eq(float a, float b) => a == b;   // −0 == +0 (the model normalises −0 on set)
    static bool Eq(Vec3 a, PoseVector3 b) => Eq(a.X, b.X) && Eq(a.Y, b.Y) && Eq(a.Z, b.Z);
    static string Few(List<string> l) => l.Count == 0 ? "" : $"{l.Count}: " + string.Join(", ", l.Take(8)) + (l.Count > 8 ? ", ..." : "");
    static readonly string[] GroupNames = FkGroupInfo.MemberNames;

    static bool[] Groups(FkGroups g) => new[] { g.Hair, g.Neck, g.Breast, g.Body, g.RightHand, g.LeftHand, g.Skirt };
    static string Bits(bool[] a) => string.Concat(a.Select(x => x ? "1" : "0"));

    static IReadOnlyList<FkRow> Rows => FkRows.From(services.Db);
    /// <summary>The build hooks' FK node rows (none in the official build: an empty list).</summary>
    static IReadOnlyList<FkRow> Nodes => HS2Next.Chara.Ext.CharaBuildHooks.FkNodes(services.Catalog)?.Rows ?? new List<FkRow>();

    /// <summary>Expected writes of a pose by the rules, from package B's map (independent of the plan's bookkeeping).</summary>
    static Dictionary<string, PoseVector3> ExpectedValues(PoseFile f, int sex)
    {
        var map = PoseImport.MapFor(Rows, Nodes, true);
        var m = map.MapFk(f, sex);
        var d = new Dictionary<string, PoseVector3>(StringComparer.Ordinal);
        foreach (var e in m.Entries) if (e.Applied) d[e.Bone.TransformName] = e.Entry.Rot;
        int gn = f.GameName ?? 0;
        var tools = f.PoseTools;
        if (tools?.SkirtFK != null && (gn == 3 || gn == 4))
            foreach (var s in tools.SkirtFK) if (map.TryGetBone(s.Key, out var b)) d[b.TransformName] = s.Rot;
        return d;
    }

    // ================================================================ I1 compare poses

    static void I1()
    {
        Section("I1", "the poses of Tools/compare_poses.json into the compare card");
        var times = new List<double>();
        foreach (var entry in (JArray)table["poses"])
        {
            int n = (int)entry["case"];
            string rel = (string)entry["path"];
            string full = Path.Combine(poseRoot, rel.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(full)) { Check($"case {n}: file present", (bool?)entry["optional"] == true, rel); continue; }
            var f = PoseFile.ReadFile(full);
            string sha = Sha(f.WritePayload(true));
            var fileGroups = GroupNames.Where((g, i) => f.ActiveFK[i]).ToList();
            var k0 = f.FindFk(0);
            float h = k0?.Pos.Y ?? 0f;
            string trailer = f.Trailer == null ? "none" : f.PoseTools == null ? "gameName" : f.PoseTools.SkirtFK != null ? "PoseToolsData+SkirtFKData" : "PoseToolsData";
            Check($"case {n}: the file matches compare_poses.json",
                  sha == (string)entry["sha256"] && fileGroups.SequenceEqual(entry["groups"].Select(x => (string)x)) && h == (float)(double)entry["hipsOffset"] &&
                  trailer == (string)entry["trailer"] && f.EnableFK == (bool)entry["enableFK"] && f.EnableIK == (bool)entry["enableIK"] && f.FK.Count == (int)entry["fkKeys"] &&
                  f.AnimeGroup == (int)entry["record"]["group"] && f.AnimeCategory == (int)entry["record"]["category"] && f.AnimeNo == (int)entry["record"]["no"],
                  $"{rel}: sha {sha.Substring(0, 12)}…, groups {string.Join("+", fileGroups)}, h {h:R}, trailer {trailer}");
            using (var e = new Env())
            {
                var id = e.Add(femaleCard);
                var before = (CharaNodeState)DocCloneOf(e.St(id));
                var r = PoseImport.ApplyFile(e.Rt, id, full);
                Check($"case {n}: applied", r.Applied && r.Error == null, r.ToString());
                if (!r.Applied) continue;
                times.Add(r.PlanMilliseconds + r.TransactionMilliseconds);
                var st = e.St(id);
                var exp = ExpectedValues(f, 1);
                var bad = exp.Where(kv => !st.Fk.Bones.TryGetValue(kv.Key, out var v) || !Eq(v, kv.Value)).Select(kv => kv.Key).ToList();
                Check($"case {n}: fk.bones bit-equal to the file's rot for every mapped key ({exp.Count})", bad.Count == 0, Few(bad));
                Check($"case {n}: fk.hipsOffset = key 0 pos.y", st.Fk.HipsOffset == h, $"{st.Fk.HipsOffset:R} vs {h:R}");
                var g = (bool[])f.ActiveFK.Clone();
                if (f.EnableIK && f.EnableFK) g[(int)FkGroup.Body] = false;
                bool skirtData = f.PoseTools?.SkirtFK != null && ((f.GameName ?? 0) == 3 || (f.GameName ?? 0) == 4);
                if (!skirtData) g[(int)FkGroup.Skirt] = false;
                Check($"case {n}: fk.enabled, ik.enabled and the groups", st.Fk.Enabled == f.EnableFK && st.Ik.Enabled == f.EnableIK && Bits(Groups(st.Fk.Groups)) == Bits(g),
                      $"fk {st.Fk.Enabled} ik {st.Ik.Enabled} groups {Bits(Groups(st.Fk.Groups))} expected {Bits(g)}");
                var corr = (bool[])before.Correction.Clone();
                for (int i = 0; i < 4; i++) corr[i] = f.Expression[i];
                var jc = f.PoseTools?.JointCorrection;
                if (jc != null) for (int i = 4; i < 8; i++) corr[i] = jc[i];
                Check($"case {n}: correction", Bits(st.Correction) == Bits(corr), $"{Bits(st.Correction)} expected {Bits(corr)}");
                var tools = f.PoseTools;
                if (tools != null && ((f.GameName ?? 0) == 3 || (f.GameName ?? 0) == 4))
                    Check($"case {n}: face from PoseToolsData", st.Face.Eyebrow == tools.EyebrowPattern && st.Face.Eyes == tools.EyesPattern && st.Face.Mouth == tools.MouthPattern &&
                          st.Face.EyesOpen == tools.EyeOpen && st.Face.MouthOpen == tools.MouthOpen,
                          $"eyebrow {st.Face.Eyebrow}, eyes {st.Face.Eyes}, mouth {st.Face.Mouth}, eyes open {st.Face.EyesOpen:R}, mouth open {st.Face.MouthOpen:R}");
                var row = PoseImport.ResolveAnime(services.Db, f.AnimeGroup, f.AnimeCategory, f.AnimeNo).Row;
                if (row != null)
                    Check($"case {n}: anime ref and time as the file", st.Anime.Ref != null && st.Anime.Ref.Source == row.Guid && st.Anime.Ref.Group == f.AnimeGroup && st.Anime.Ref.Category == f.AnimeCategory &&
                          st.Anime.Ref.No == f.AnimeNo && st.Anime.Time == f.NormalizedTime && r.AnimeResolved != null, $"{r.AnimeResolved}, time {st.Anime.Time:R}");
                else
                    Check($"case {n}: anime unchanged (record missing)", r.AnimeResolved == null && SameRef(st.Anime.Ref, before.Anime.Ref) && st.Anime.Time == before.Anime.Time && r.Findings.Has("pose.animeMissing"),
                          string.Join("; ", r.Findings));
                var unres = r.UnresolvedFamilies.Keys.ToList();
                Check($"case {n}: report counts", r.Keys == f.FK.Count && r.Mapped + r.Unresolved <= r.Keys && unres.All(x => x == nameof(PoseKeyFamily.AiShoujo) || x == nameof(PoseKeyFamily.AdditionalFkNodes)) &&
                      (n != 14 || (r.AiRemapped == 10 && r.Unresolved > 0)),
                      r + "; unresolved " + string.Join(", ", r.UnresolvedFamilies.Select(kv => kv.Key + " " + kv.Value)));
                var block = e.Block(id);
                Check($"case {n}: hs2.pose block: hash of the payload, source path", block != null && (string)block["sha256"] == sha && (string)block["source"] == rel,
                      block == null ? "no block" : $"{(string)block["source"]} {(string)block["sha256"]}");
                Info($"case {n}: {r}; applied bones {r.AppliedBones.Count}, captured {r.Captured.Count}; plan {r.PlanMilliseconds:F2} ms, transaction {r.TransactionMilliseconds:F2} ms; findings: {string.Join(" | ", r.Findings.Select(x => x.Code + (x.Count > 1 ? " x" + x.Count : "")))}");
            }
        }
        if (times.Count > 0)
        {
            times.Sort();
            double med = times[times.Count / 2];
            Check($"pose import (parse + plan + transaction, without the animation load): median {med:F2} ms, max {times.Max():F2} ms (target 5 ms, fail at 10)", med <= 10, "");
        }
    }

    static bool SameRef(AnimeRef a, AnimeRef b) => a == null ? b == null : b != null && a.Source == b.Source && a.Group == b.Group && a.Category == b.Category && a.No == b.No;

    static NodeState DocCloneOf(CharaNodeState st) => DocClone.Clone(st);

    // ================================================================ I2 undo / redo

    static void I2()
    {
        Section("I2", "undo / redo");
        var entry = ((JArray)table["poses"]).First(x => (string)x["trailer"] == "PoseToolsData+SkirtFKData");
        using (var e = new Env())
        {
            var id = e.Add(femaleCard);
            var b0 = e.S.NodeJson(id);
            int undo0 = e.S.Commands.UndoList.Count;
            var r = PoseImport.ApplyFile(e.Rt, id, Path.Combine(poseRoot, ((string)entry["path"]).Replace('/', Path.DirectorySeparatorChar)));
            var b1 = e.S.NodeJson(id);
            Check("import: one history entry labelled \"Load pose\"", r.Applied && e.S.Commands.UndoList.Count == undo0 + 1 && e.S.Commands.UndoList.First().Label == "Load pose" && b1 != b0,
                  $"{undo0} → {e.S.Commands.UndoList.Count} ({e.S.Commands.UndoList.FirstOrDefault()})");
            e.S.Commands.Undo();
            e.Rt.ApplyNow();
            Check("undo restores the canonical node bytes (inline ext included)", e.S.NodeJson(id) == b0 && e.Block(id) == null, Diff(b0, e.S.NodeJson(id)));
            e.S.Commands.Redo();
            e.Rt.ApplyNow();
            Check("redo restores the bytes after the import", e.S.NodeJson(id) == b1 && e.Block(id) != null, Diff(b1, e.S.NodeJson(id)));
        }
    }

    static string Diff(string a, string b)
    {
        if (a == b) return "";
        if (a == null || b == null) return "null";
        int i = 0;
        while (i < a.Length && i < b.Length && a[i] == b[i]) i++;
        return $"first difference at {i}: …{a.Substring(Math.Max(0, i - 40), Math.Min(80, a.Length - Math.Max(0, i - 40)))}… vs …{b.Substring(Math.Max(0, i - 40), Math.Min(80, b.Length - Math.Max(0, i - 40)))}…";
    }

    // ================================================================ I3 corpus

    sealed class Tally
    {
        public int Files, Dat, Png, Exceptions, Remap, Toes, Penis, Hips, Both, BodyOff, SkirtOff, AnimeOk, AnimeMissing, AnimeGated, Anime0211, Warnings, Unresolved;
        public readonly SortedDictionary<string, int> FamilyFiles = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public readonly SortedDictionary<string, int> KeyFiles = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public readonly List<string> Errors = new List<string>();
    }

    static void I3()
    {
        Section("I3", "corpus planning");
        if (!Directory.Exists(poseRoot)) { Check("pose library present", false, poseRoot); return; }
        var rows = Rows; var nodes = Nodes;
        var map = PoseImport.MapFor(rows, nodes, true);
        HashSet<string> female, male;
        using (var e = new Env())
        {
            female = NameSet(e, e.Add(femaleCard));
            male = NameSet(e, e.Add(maleCard));
        }
        Info($"name sets: female {female.Count}, male {male.Count}; Bone-list rows {rows.Count}, node rows {nodes.Count}");
        var cur = new CharaNodeState();
        var tf = new Tally(); var tm = new Tally();
        var sw = Stopwatch.StartNew();
        var scan = PoseLibrary.Scan(poseRoot, (entry, bytes, pose) =>
        {
            if (pose == null) return;
            foreach (var (t, set, sex) in new[] { (tf, female, 1), (tm, male, 0) })
            {
                t.Files++;
                if (entry.Kind == PoseContainerKind.Png) t.Png++; else t.Dat++;
                try
                {
                    var plan = PoseImport.Plan(new PosePlanInput
                    {
                        Pose = pose, Rows = rows, NodeRows = nodes, Map = map, Sex = sex, Present = set, Current = cur,
                        ResolveAnime = (g, c, n) => PoseImport.ResolveAnime(services.Db, g, c, n), Options = new PoseImportOptions(), Source = entry.RelativePath,
                    });
                    var r = plan.Report;
                    if (r.Error != null) { t.Exceptions++; if (t.Errors.Count < 10) t.Errors.Add(entry.RelativePath + ": " + r.Error); continue; }
                    if (r.AiRemapped > 0) t.Remap++;
                    if (plan.Bones.Keys.Any(k => k.StartsWith("cf_J_Toes_", StringComparison.Ordinal) && pose.FK.Any(x => x.Key >= 10000 && x.Key <= 10019))) t.Toes++;
                    if (pose.FK.Any(x => x.Key >= 11000 && x.Key <= 11003) && plan.Bones.ContainsKey("cm_J_dan103_00")) t.Penis++;
                    if (plan.SetHipsOffset && plan.HipsOffset != 0) t.Hips++;
                    if (pose.EnableFK && pose.EnableIK) { t.Both++; if (!plan.FkGroups[(int)FkGroup.Body]) t.BodyOff++; }
                    if (r.SkirtGroupForcedOff) t.SkirtOff++;
                    if (r.AnimeResolved != null) { t.AnimeOk++; if (r.Findings.Has("pose.animeHidden")) t.AnimeGated++; if (pose.AnimeGroup == 0 && pose.AnimeCategory == 2 && pose.AnimeNo == 11) t.Anime0211++; }
                    else t.AnimeMissing++;
                    t.Unresolved += r.Unresolved;
                    if (r.Findings.Any(x => x.Code == "pose.unresolvedKey" && x.Severity == FindingSeverity.Warning)) t.Warnings++;
                    foreach (var fam in r.UnresolvedFamilies.Keys) t.FamilyFiles[fam] = (t.FamilyFiles.TryGetValue(fam, out var k) ? k : 0) + 1;
                    var keys = new HashSet<string>();
                    foreach (var x in pose.FK)
                    {
                        string kg = x.Key == 331 || x.Key == 337 ? "331/337" : x.Key >= 1001 && x.Key <= 1005 ? (x.Key == 1003 || x.Key == 1004 ? "1003/1004" : "1001/1002/1005")
                                  : x.Key >= 386 && x.Key <= 399 ? "386–399" : x.Key == 52 || (x.Key >= 2001 && x.Key <= 2005) ? "52/2001–2005" : x.Key == 900 || x.Key == 901 ? "900/901"
                                  : x.Key >= 460 && x.Key <= 463 ? "460–463" : x.Key >= 12000 && x.Key <= 12025 ? "12000–12025" : null;
                        if (kg != null) keys.Add(kg);
                    }
                    foreach (var kg in keys) t.KeyFiles[kg] = (t.KeyFiles.TryGetValue(kg, out var k2) ? k2 : 0) + 1;
                }
                catch (Exception ex)
                {
                    t.Exceptions++;
                    if (t.Errors.Count < 10) t.Errors.Add(entry.RelativePath + ": " + ex.GetType().Name + ": " + ex.Message);
                }
            }
        });
        sw.Stop();
        Info($"scan: {scan.Poses} payloads ({scan.DatPoses} .dat, {scan.PngPoses} PNG), {scan.Errors} unreadable, {scan.NotAPose} not a pose");
        foreach (var (t, name) in new[] { (tf, "female"), (tm, "male") })
        {
            Info($"{name}: files {t.Files} (dat {t.Dat}, png {t.Png}); remap {t.Remap}; toe nodes {t.Toes}; penis chain {t.Penis}; hips ≠ 0 {t.Hips}; both modes {t.Both} (body off {t.BodyOff}); skirt forced off {t.SkirtOff}; " +
                 $"anime resolved {t.AnimeOk} (list-hidden {t.AnimeGated}, 0/2/11 {t.Anime0211}), missing {t.AnimeMissing}; unresolved keys {t.Unresolved}; files with warnings {t.Warnings}");
            Info($"{name}: unresolved families (files) " + string.Join(", ", t.FamilyFiles.Select(kv => kv.Key + " " + kv.Value)) + "; key groups (files) " + string.Join(", ", t.KeyFiles.Select(kv => kv.Key + " " + kv.Value)));
            foreach (var x in t.Errors) Info("  error " + x);
        }
        Check("4,385 payloads planned against both name sets, 0 exceptions", tf.Files == 4385 && tm.Files == 4385 && tf.Exceptions == 0 && tm.Exceptions == 0, $"{tf.Files} / {tm.Files}; exceptions {tf.Exceptions} / {tm.Exceptions}");
        Check("AI breast remap in 2,042 files", tf.Remap == 2042, tf.Remap.ToString());
        // the node keys map only through the FK node rows of build hooks; the official build has none, so they stay
        // unresolved there (reported as their family, no warning)
        if (Nodes.Count == 0)
            Check("node keys without an FK node provider (the official build): toes 10000–10019 and the chain 11000–11003 map to no bone", tf.Toes == 0 && tf.Penis == 0, $"{tf.Toes} / {tf.Penis}");
        else
            Check("node keys mapped: toes 10000–10019 in 809 files, penis chain 11000–11003 in 1,035", tf.Toes == 809 && tf.Penis == 1035, $"{tf.Toes} / {tf.Penis}");
        int K(string k) => tf.KeyFiles.TryGetValue(k, out var v) ? v : 0;
        Check("unresolved families with their file counts (331/337 2,042; 1001/1002/1005 1,814; 1003/1004 315; 386–399 7; 52/2001–2005 105; 900/901 116)",
              K("331/337") == 2042 && K("1001/1002/1005") == 1814 && K("1003/1004") == 315 && K("386–399") == 7 && K("52/2001–2005") == 105 && K("900/901") == 116,
              string.Join(", ", tf.KeyFiles.Select(kv => kv.Key + " " + kv.Value)));
        Check("0 unresolved keys outside the named families (no pose.unresolvedKey warning)", tf.Warnings == 0 && tm.Warnings == 0, $"{tf.Warnings} / {tm.Warnings} files");
        Check("1,688 files with a non-zero hips offset", tf.Hips == 1688, tf.Hips.ToString());
        Check("113 both-modes files, all with fk.groups.body false", tf.Both == 113 && tf.BodyOff == 113, $"{tf.Both} / {tf.BodyOff}");
        Check("Skirt forced off in 80 files", tf.SkirtOff == 80, tf.SkirtOff.ToString());
        Check("anime resolution: 0/2/11 (1,644 files) played although the lists hide it (as HS2, pose.animeHidden); every file resolved (an H record like a plain one) or reported missing",
              tf.Anime0211 == 1644 && tf.AnimeGated >= 1644 && tf.AnimeOk + tf.AnimeMissing == tf.Files,
              $"resolved {tf.AnimeOk} (list-hidden {tf.AnimeGated}, 0/2/11 {tf.Anime0211}), missing {tf.AnimeMissing}");
        Check($"corpus planning time: {sw.Elapsed.TotalSeconds:F1} s for {tf.Files + tm.Files} plans (target 60 s, fail at 120)", sw.Elapsed.TotalSeconds <= 120, "");
    }

    static HashSet<string> NameSet(Env e, string id)
    {
        var t = FkDriver.TableFor(e.Rt.FindChara(id));
        var s = new HashSet<string>(StringComparer.Ordinal);
        if (t != null) foreach (var b in t.Bones) { s.Add(b.Key); if (b.PartnerKey != null) s.Add(b.PartnerKey); }
        return s;
    }

    // ================================================================ I4 single files

    static string FindPose(Func<PoseLibraryEntry, PoseFile, bool> pick)
    {
        string found = null;
        PoseLibrary.Scan(poseRoot, (entry, bytes, pose) => { if (found == null && pose != null && pick(entry, pose)) found = entry.FullPath; });
        return found;
    }

    static void I4()
    {
        Section("I4", "single files");
        string ai = FindPose((en, p) => p.FK.Any(x => x.Key == 326) && p.Sex == 1 && p.EnableFK && !p.EnableIK);
        string ikOnly = FindPose((en, p) => p.EnableIK && !p.EnableFK);
        string both = FindPose((en, p) => p.EnableIK && p.EnableFK);
        string png = FindPose((en, p) => en.Kind == PoseContainerKind.Png);
        string male = FindPose((en, p) => p.Sex == 0 && p.EnableFK && !p.EnableIK && p.Trailer == null && p.FK.Count == 67);
        string toes = FindPose((en, p) => p.FK.Any(x => x.Key == 10000) && p.Sex == 1);
        string tools = FindPose((en, p) => p.PoseTools != null && p.PoseTools.SkirtFK == null && p.ActiveFK[(int)FkGroup.Skirt] == false);
        string skirtNoData = FindPose((en, p) => p.ActiveFK[(int)FkGroup.Skirt] && p.EnableFK && !p.EnableIK && (p.PoseTools == null || p.PoseTools.SkirtFK == null));
        string skirt = FindPose((en, p) => p.PoseTools?.SkirtFK != null);
        foreach (var (k, v) in new[] { ("AI", ai), ("IK only", ikOnly), ("both modes", both), ("PNG", png), ("male", male), ("node keys", toes), ("PoseToolsData", tools), ("Skirt on without data", skirtNoData), ("SkirtFKData", skirt) })
            Info($"{k}: {(v != null ? Rel(v) : "not found")}");
        using (var e = new Env())
        {
            string F() => e.Add(femaleCard);
            // AI family, remap on / off
            if (ai != null)
            {
                var id = F();
                var r = PoseImport.ApplyFile(e.Rt, id, ai);
                var st = e.St(id);
                var bl = e.Block(id);
                var unres = bl?["unresolved"] as JObject;
                Check("AI file, remap on: 326–330 / 332–336 under the breast names, 331 / 337 kept raw in hs2.pose", r.Applied && r.AiRemapped == 10 && st.Fk.Bones.ContainsKey("cf_J_Mune01_L") &&
                      unres != null && unres["331"] != null && unres["337"] != null && r.Findings.Has("pose.aiBreastRemap"), r + "; unresolved " + string.Join(",", unres?.Properties().Select(x => x.Name) ?? new string[0]));
                var id2 = F();
                var r2 = PoseImport.ApplyFile(e.Rt, id2, ai, new PoseImportOptions { AiBreastRemap = false });
                var st2 = e.St(id2);
                var unres2 = e.Block(id2)?["unresolved"] as JObject;
                Check("AI file, remap off: only keys 0–51 (and other Bone-list keys), 326–330 unresolved", r2.Applied && r2.AiRemapped == 0 && !st2.Fk.Bones.ContainsKey("cf_J_Mune01_L") && unres2?["326"] != null, r2.ToString());
            }
            if (male != null)
            {
                var id = F();
                var r = PoseImport.ApplyFile(e.Rt, id, male);
                Check("sex mismatch: a male pose is imported into a female with pose.sexMismatch", r.Applied && r.Findings.Has("pose.sexMismatch") && r.NotOnCharacter >= 3, r.ToString());
            }
            foreach (var (path, what) in new[] { (ikOnly, "IK only"), (both, "both modes") })
            {
                if (path == null) continue;
                var id = F();
                var f = PoseFile.ReadFile(path);
                var r = PoseImport.ApplyFile(e.Rt, id, path);
                var st = e.St(id);
                var ik = e.Block(id)?["ik"] as JObject;
                var targets = ik?["targets"] as JObject;
                // 3.4: the targets are converted (or rejected with pose.ikScaleUnknown); pose.ikPending is no longer emitted
                bool converted = r.IkApproximated && st.Ik.Targets.Targets.All(x => x != null) && r.Findings.Has("pose.ikConverted");
                bool rejected = !r.IkApproximated && r.IkScale == 0 && r.Findings.Has("pose.ikScaleUnknown");
                Check($"{what}: ik.enabled, hs2.pose.ik with 13 raw targets, targets converted or rejected, no pose.ikPending" + (what == "both modes" ? ", fk.groups.body false" : ", fk.enabled false"),
                      r.Applied && st.Ik.Enabled && !r.IkPending && !r.Findings.Has("pose.ikPending") && (converted || rejected) && targets != null && targets.Count == 13 &&
                      (what == "both modes" ? st.Fk.Enabled && !st.Fk.Groups.Body : !st.Fk.Enabled), $"{r}; targets {targets?.Count}; scale {r.IkScale}");
                var id2 = F();
                var r2 = PoseImport.ApplyFile(e.Rt, id2, path, new PoseImportOptions { Ik = false });
                Check($"{what}, PoseImportOptions.Ik false: raw only, pose.ikPending (2.3 behaviour), no targets", r2.Applied && r2.IkPending && r2.Findings.Has("pose.ikPending") && e.St(id2).Ik.Targets.Targets.All(x => x == null), r2.ToString());
            }
            if (png != null)
            {
                var id = F();
                var r = PoseImport.ApplyFile(e.Rt, id, png);
                var f = PoseFile.ReadFile(png);
                Check("a PNG pose imports; the hs2.pose hash is that of the payload", r.Applied && (string)e.Block(id)?["sha256"] == Sha(f.WritePayload(true)), r.ToString());
            }
            if (tools != null)
            {
                var id = F();
                var f = PoseFile.ReadFile(tools);
                var r = PoseImport.ApplyFile(e.Rt, id, tools);
                var st = e.St(id);
                var t = f.PoseTools;
                Check("PoseToolsData without skirt values: face ids and opens, correction[4..7]", r.Applied && st.Face.Eyebrow == t.EyebrowPattern && st.Face.Mouth == t.MouthPattern && st.Face.EyesOpen == t.EyeOpen &&
                      (t.JointCorrection == null || Enumerable.Range(4, 4).All(i => st.Correction[i] == t.JointCorrection[i])), r.ToString());
            }
            if (skirtNoData != null)
            {
                var id = F();
                var r = PoseImport.ApplyFile(e.Rt, id, skirtNoData);
                Check("Skirt on in the file without skirt values → Skirt off (SkirtNeedsData)", r.Applied && !e.St(id).Fk.Groups.Skirt && r.SkirtGroupForcedOff && r.Findings.Has("pose.skirtGroupOff"), r.ToString());
                var id2 = F();
                var r2 = PoseImport.ApplyFile(e.Rt, id2, skirtNoData, new PoseImportOptions { SkirtNeedsData = false });
                Check("... SkirtNeedsData false (vanilla): the file's flag", r2.Applied && e.St(id2).Fk.Groups.Skirt && !r2.SkirtGroupForcedOff, r2.ToString());
            }
            if (skirt != null)
            {
                var id = F();
                var r = PoseImport.ApplyFile(e.Rt, id, skirt);
                Check("SkirtFKData: 48 skirt values imported, the Skirt group stays on", r.Applied && r.SkirtValues == 48 && e.St(id).Fk.Groups.Skirt && e.St(id).Fk.Bones.ContainsKey("cf_J_sk_00_00"), r.ToString());
            }
            if (toes != null)
            {
                var id = F();
                var r = PoseImport.ApplyFile(e.Rt, id, toes);
                var st = e.St(id);
                if (Nodes.Count == 0)
                    Check("node keys without an FK node provider (the official build): unresolved and reported, the other keys applied", r.Applied && r.Unresolved >= 20 && r.Mapped > 0 && r.Mapped + r.Unresolved <= r.Keys, r.ToString());
                else
                    Check("node keys on a vanilla body: values kept in the model, pose.boneNotOnCharacter", r.Applied && st.Fk.Bones.ContainsKey("cf_J_Toes_Hallux1_R") && r.Findings.Has("pose.boneNotOnCharacter") && r.NotOnCharacter >= 20, r.ToString());
            }
            // hips offset file (case 13 of the table) and gameName handling on a synthetic trailer
            var hipsEntry = ((JArray)table["poses"]).First(x => (double)x["hipsOffset"] < -11);
            string hips = Path.Combine(poseRoot, ((string)hipsEntry["path"]).Replace('/', Path.DirectorySeparatorChar));
            {
                var id = F();
                var r = PoseImport.ApplyFile(e.Rt, id, hips);
                Check("hips-offset file: fk.hipsOffset = key 0 pos.y", r.Applied && e.St(id).Fk.HipsOffset == (float)(double)hipsEntry["hipsOffset"], e.St(id).Fk.HipsOffset.ToString("R"));
            }
            if (skirt != null)
            {
                var src = PoseFile.ReadFile(skirt);
                float h0 = src.FindFk(0)?.Pos.Y ?? 0f;
                var results = new List<string>();
                bool ok = true;
                foreach (int gn in new[] { 0, 1, 2, 3, 4, 5, 6 })
                {
                    var f = PoseFile.ReadFile(skirt);
                    var ext = f.Ext;
                    var es = ext.Find(KKExTrailer.ExtendedSaveGuid);
                    es.SetValue(es.Version ?? 0, GameNameMap(gn));
                    f.Trailer = ext.Encode();
                    var plan = PoseImport.Plan(new PosePlanInput { Pose = f, Rows = Rows, NodeRows = Nodes, Sex = 1, Current = new CharaNodeState(), Options = new PoseImportOptions(),
                                                                   ResolveAnime = (g, c, n) => PoseImport.ResolveAnime(services.Db, g, c, n) });
                    bool x10 = gn == 1 || gn == 2 || gn == 5 || gn == 6, tools34 = gn == 3 || gn == 4;
                    float expectH = x10 ? (float)(h0 * 10f) : h0;
                    bool hipsOk = Math.Abs(plan.HipsOffset - expectH) <= 1e-6f * Math.Abs(expectH);
                    bool exprOk = tools34 ? plan.Eyebrow.HasValue : !plan.Eyebrow.HasValue;
                    bool skirtOk = tools34 ? plan.Report.SkirtValues == 48 : plan.Report.SkirtValues == 0;
                    bool kk = (gn == 1 || gn == 6) == plan.Report.Findings.Has("pose.koikatsuFingers");
                    ok &= f.GameName == gn && hipsOk && exprOk && skirtOk && kk && plan.Correction != null;
                    results.Add($"{gn}: read {f.GameName?.ToString() ?? "null"} / plan {plan.Report.GameName}, h {plan.HipsOffset:G4}{(hipsOk ? "" : " (h?)")}, expression {plan.Eyebrow.HasValue}, skirt {plan.Report.SkirtValues}{(kk ? "" : " koikatsu?")}");
                }
                Check("gameName on a synthetic trailer: ×10 for 1/2/5/6; expression and skirt only for 3/4; Koikatsu finger note for 1/6", ok, string.Join("; ", results));
            }
        }
    }

    static byte[] GameNameMap(int gameName)
    {
        var name = Encoding.UTF8.GetBytes("gameName");
        var l = new List<byte> { 0x81, (byte)(0xa0 | name.Length) };
        l.AddRange(name);
        l.Add((byte)gameName);   // positive fixint
        return l.ToArray();
    }

    static string Rel(string full) => full.Substring(poseRoot.Length).TrimStart('\\', '/').Replace('\\', '/');

    // ================================================================ I5 save → load, provenance survival

    static SaveOptions FixedSave() => new SaveOptions { Clock = () => new DateTime(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc), Thumbnail = ThumbnailMode.Placeholder, AppVersion = "0.1.0" };

    static void I5()
    {
        Section("I5", "save → load after an import; the hs2.pose block");
        var entry = ((JArray)table["poses"]).First(x => (string)x["trailer"] == "PoseToolsData+SkirtFKData");
        string path = Path.Combine(poseRoot, ((string)entry["path"]).Replace('/', Path.DirectorySeparatorChar));
        using (var e = new Env())
        {
            var id = e.Add(femaleCard);
            var r = PoseImport.ApplyFile(e.Rt, id, path);
            var c = FkDriver.Of(e.Rt.FindChara(id));
            var rot = c.Table.Bones.Where(b => b.Bone).ToDictionary(b => b.Key, b => b.Bone.localRotation);
            var hips = e.Rt.FindChara(id).Context.BodyBones["cf_J_Hips"].localPosition;
            var block = e.Block(id)?.ToString(Newtonsoft.Json.Formatting.None);
            var bytes = e.Rt.Ops.ToBytes(FixedSave());
            using (var e2 = new Env(0x5a5aUL))
            {
                var load = e2.Rt.Ops.Load(bytes);
                e2.Rt.ApplyNow();
                var c2 = FkDriver.Of(e2.Rt.FindChara(id));
                var diff = c2 == null ? new List<string> { "no FK state" } : c2.Table.Bones.Where(b => b.Bone && rot.TryGetValue(b.Key, out var q) && !(q.x == b.Bone.localRotation.x && q.y == b.Bone.localRotation.y && q.z == b.Bone.localRotation.z && q.w == b.Bone.localRotation.w)).Select(b => b.Key).ToList();
                var hips2 = e2.Rt.FindChara(id).Context.BodyBones["cf_J_Hips"].localPosition;
                Check("after save → load (no stepping): the FK pose and the hips position equal those before save (bit-equal)", load.Ok && diff.Count == 0 && hips == hips2 && hips.x == hips2.x && hips.y == hips2.y && hips.z == hips2.z,
                      $"{load}; {Few(diff)}; hips {hips:F5} vs {hips2:F5}");
                Check("the hs2.pose block survives save → load byte-identically", e2.Block(id)?.ToString(Newtonsoft.Json.Formatting.None) == block && block != null);
            }
            var dup = e.S.Duplicate(new[] { id });
            e.Rt.ApplyNow();
            Check("duplicate copies the block", dup.Ok && e.Block(dup.NewId)?.ToString(Newtonsoft.Json.Formatting.None) == block, dup.ToString());
            e.S.Delete(new[] { id });
            e.Rt.ApplyNow();
            e.S.Commands.Undo();
            e.Rt.ApplyNow();
            Check("delete → undo keeps the block", e.Block(id)?.ToString(Newtonsoft.Json.Formatting.None) == block);
        }
    }

    // ================================================================ I6–I8 IK import (3.4)

    static readonly string[] IkComparePoses =
    {
        "CHAIRS/Standing next to.dat", "STANDING/Floor/Peace.dat", "STANDING/Floor/Im cute.dat", "STANDING/Floor/Hands up 2.dat",
        "TINNY HOME 6/Lying on floor.dat", "CHAIRS/Sitting 4.dat", "SOFA/Sitting edge.dat", "TINNY HOME 6/Near TV.dat",
    };
    static readonly string[] IkTenthPoses = { "~Duck/Meme/Jojo Pose 3.dat", "~Duck/Combat/Sword&Board 1.dat" };
    static readonly List<string> ikMeasurements = new List<string>();

    static string PosePath(string rel) => Path.Combine(poseRoot, rel.Replace('/', Path.DirectorySeparatorChar));
    static void Measure(string s) { ikMeasurements.Add($"[{current}] {s}"); Info("MEASURE " + s); }

    static void I6()
    {
        Section("I6", "corpus IK plan against BR-Chan's lengths (and U5 on one character)");
        var files = new List<(string rel, PoseFile pose)>();
        PoseLibrary.Scan(poseRoot, (entry, bytes, pose) => { if (pose != null && pose.EnableIK) files.Add((Rel(entry.FullPath), pose)); });
        using (var e = new Env())
        {
            var id = e.Add(femaleCard);
            var r = e.Rt.FindChara(id);
            var body = PoseIkBody.Of(r);
            Info($"BR-Chan L1 {string.Join(" / ", body.L1.Select(x => x.ToString("F4")))}, L2 {string.Join(" / ", body.L2.Select(x => x.ToString("F4")))}");
            int one = 0, ten = 0, rej = 0, exc = 0, bigShift = 0, allGroups = 0, unreach = 0, rotDrop = 0, approx = 0;
            float maxShift = 0, maxPass = 0; string maxShiftFile = "";
            var sw = Stopwatch.StartNew();
            var byMode = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var (rel, pose) in files)
            {
                try
                {
                    var ik = PoseImport.PlanIk(PoseImport.RawIk(pose), pose.ActiveIK, body);
                    string mode = pose.EnableFK ? "both" : "IK only";
                    string cls = ik.Scale == 1 ? "x1" : ik.Scale == 10 ? "x10" : "rejected";
                    byMode[mode + " " + cls] = (byMode.TryGetValue(mode + " " + cls, out var k) ? k : 0) + 1;
                    if (ik.Scale == 1) one++; else if (ik.Scale == 10) ten++; else { rej++; Info($"rejected: {rel} (ratio {ik.Ratio:G4})"); continue; }
                    if (ik.BodyApprox) approx++;
                    if (ik.Unreachable.Count > 0) unreach++;
                    rotDrop += ik.RotDropped;
                    maxPass = Math.Max(maxPass, ik.BodyPassResidual);
                    if (pose.ActiveIK.All(x => x))
                    {
                        allGroups++;
                        float sh = ik.BodyShift.magnitude;
                        if (sh > maxShift) { maxShift = sh; maxShiftFile = rel; }
                        if (sh > 0.2f) bigShift++;
                    }
                }
                catch (Exception ex) { exc++; Info($"exception {rel}: {ex.GetType().Name}: {ex.Message}"); }
            }
            double perFile = files.Count > 0 ? sw.Elapsed.TotalMilliseconds / files.Count : 0;
            Info($"classes by mode: {string.Join(", ", byMode.Select(kv => kv.Key + " " + kv.Value))}");
            Check($"{files.Count} enableIK payloads planned without exception; classes x1 {one}, x10 {ten}, rejected {rej} (measured)", exc == 0 && files.Count > 0 && one + ten + rej == files.Count,
                  $"exceptions {exc}");
            Check($"body shift ≤ 0.2 for every all-groups file ({allGroups})", bigShift == 0, $"max {maxShift:G4} ({maxShiftFile})");
            Check("planning time ≤ 5 ms per file", perFile <= 5.0, $"{perFile:F3} ms");
            Info($"unreachable on BR-Chan: {unreach} files; dropped rotations {rotDrop}; body approx {approx}; third-pass residual max {maxPass:G3}");
            Measure($"U1 import IK plan {perFile:F3} ms per file over {files.Count} files");
            Measure($"I6 classes x1 {one}, x10 {ten}, rejected {rej}; max all-groups body shift {maxShift:G4}; files with an unreachable end target on BR-Chan {unreach}");
            Measure($"U5 (plan) what a third Body pass would move: max {maxPass:G3} units");
            // U5 after a solve: the files applied one after another to this character
            var res = new List<float>();
            string worst = ""; float worstV = 0;
            var c = IkDriver.Of(r);
            var root = r.Root.transform;
            var spine = r.Context.BodyBones["cf_J_Spine01"];
            foreach (var (rel, pose) in files)
            {
                var rp = PoseImport.ApplyFile(e.Rt, id, PosePath(rel));
                if (!rp.Applied || !rp.IkApproximated || !pose.ActiveIK.All(x => x)) continue;
                e.Rt.Tick(1f / 60f); e.Rt.Tick(1f / 60f);
                var raw = PoseImport.RawIk(pose)[0];
                var ts = new Vector3(raw[0], raw[1], raw[2]) * rp.IkScale;
                float d = (spine.position - root.TransformPoint(ts)).magnitude;
                res.Add(d);
                if (d > worstV) { worstV = d; worst = rel; }
            }
            if (res.Count > 0)
            {
                var srt = res.OrderBy(x => x).ToList();
                Measure($"U5 after a solve ({res.Count} all-groups files on one BR-Chan, 2 steps each): |Spine01 - T_s| median {srt[srt.Count / 2]:G3}, p95 {srt[(int)(srt.Count * 0.95)]:G3}, max {worstV:G3} ({worst}); solver {(c.Stage.LastSolve.Valid ? "ran" : "did not run")}");
            }
        }
    }

    static void I7()
    {
        Section("I7", "the comparison poses and the 1/10-scale poses applied");
        foreach (var rel in IkComparePoses.Concat(IkTenthPoses))
        {
            var path = PosePath(rel);
            if (!File.Exists(path)) { Info("missing: " + rel); continue; }
            using (var e = new Env())
            {
                var id = e.Add(femaleCard);
                var pose = PoseFile.ReadFile(path);
                int n0 = e.S.Commands.UndoList.Count;
                var r = PoseImport.ApplyFile(e.Rt, id, path);
                var st = e.St(id);
                var ik = e.Block(id)?["ik"] as JObject;
                int want = IkTenthPoses.Contains(rel) ? 10 : 1;
                var raw = PoseImport.RawIk(pose);
                bool targetsOk = true; string bad = "";
                for (int i = 1; i < 13; i++)
                {
                    var t = st.Ik.Targets[i];
                    var f = raw[i];
                    var p = want == 1 ? new Vec3(f[0], f[1], f[2]) : new Vec3(f[0] * 10f, f[1] * 10f, f[2] * 10f);
                    if (t == null || !(t.Pos.X == p.X && t.Pos.Y == p.Y && t.Pos.Z == p.Z)) { targetsOk = false; bad = IkTargets.Names[i]; }
                    bool rotUsed = i == 3 || i == 6 || i == 9 || i == 12;
                    if (t != null && rotUsed && !(t.Rot.X == f[3] && t.Rot.Y == f[4] && t.Rot.Z == f[5])) { targetsOk = false; bad = IkTargets.Names[i] + " rot"; }
                }
                var b0 = st.Ik.Targets[0];
                var shift = r.IkBodyShift ?? new Vec3(float.NaN, 0, 0);
                var ts = new Vector3(raw[0][0], raw[0][1], raw[0][2]) * want;
                bool bodyOk = b0 != null && (ts - new Vector3(shift.X, shift.Y, shift.Z) - new Vector3(b0.Pos.X, b0.Pos.Y, b0.Pos.Z)).magnitude <= 1e-6f * Math.Max(1f, ts.magnitude);
                var bs = ik?["bodyShift"] as JArray;
                bool blockOk = ik != null && (int?)ik["scale"] == want && bs != null && bs.Count == 3 && Math.Abs((float)bs[0] - shift.X) <= 1e-6f && Math.Abs((float)bs[1] - shift.Y) <= 1e-6f && Math.Abs((float)bs[2] - shift.Z) <= 1e-6f;
                bool modes = pose.EnableFK ? st.Fk.Enabled && !st.Fk.Groups.Body : !st.Fk.Enabled;
                Check($"{rel}: x{want}, targets = file x factor (Body - shift ≤ 1e-6), hs2.pose.ik scale / bodyShift, modes, one entry",
                      r.Applied && r.IkApproximated && r.IkScale == want && !r.IkPending && targetsOk && bodyOk && blockOk && modes && st.Ik.Enabled && e.S.Commands.UndoList.Count == n0 + 1 &&
                      r.Findings.Has("pose.ikConverted") && (want == 10) == r.Findings.Has("pose.ikScaleTenth"),
                      $"{r}; shift {shift}; unreachable [{string.Join(",", r.IkUnreachable)}]; rot dropped {r.IkRotDropped}{(bad != "" ? "; bad " + bad : "")}");
            }
        }
    }

    static void I8()
    {
        Section("I8", "ik.copy from:import on a 2.3-style node = the import's targets");
        var path = PosePath(IkComparePoses[1]);
        if (!File.Exists(path)) { Info("missing " + IkComparePoses[1]); return; }
        using (var e = new Env())
        {
            var id = e.Add(femaleCard);
            var r1 = PoseImport.ApplyFile(e.Rt, id, path, new PoseImportOptions { Ik = false });
            var st = e.St(id);
            Check("2.3-style import: ik.enabled with null targets and the raw block", r1.Applied && r1.IkPending && st.Ik.Enabled && st.Ik.Targets.Targets.All(x => x == null));
            var copies = new KinematicCopies(e.Rt);
            int n0 = e.S.Commands.UndoList.Count;
            var k = copies.IkFromImport(id);
            st = e.St(id);
            var tb = st.Ik.Targets.Targets.Select(x => x == null ? null : new IkTarget { Pos = x.Pos, Rot = x.Rot }).ToArray();
            var blockB = e.Block(id)?["ik"] as JObject;
            Check("IkFromImport: one entry Convert IK, 13 targets, block scale / bodyShift written", k.Ok && e.S.Commands.UndoList.Count == n0 + 1 && e.S.Commands.UndoList[0].Label == "Convert IK" && tb.All(x => x != null) && (int?)blockB?["scale"] == 1 && blockB?["bodyShift"] is JArray, k.ToString());
            var r2 = PoseImport.ApplyFile(e.Rt, id, path);
            st = e.St(id);
            var bad = Enumerable.Range(0, 13).Where(i => st.Ik.Targets[i] == null || tb[i] == null || !st.Ik.Targets[i].Pos.Equals(tb[i].Pos) || !st.Ik.Targets[i].Rot.Equals(tb[i].Rot)).Select(i => IkTargets.Names[i]).ToList();
            Check("the import of the same file on the same pose writes the same targets (bit for bit)", r2.Applied && bad.Count == 0, Few(bad));
            var bare = e.Add(femaleCard);
            var k2 = copies.IkFromImport(bare);
            Check("a node without an imported IK block → bad_args", !k2.Ok && k2.Command.Code == CommandCodes.BadArgs);
        }
    }
}
