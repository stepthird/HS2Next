// Batch test of the shape-slider map tables (ShapeEngine) against black-box measurements of the game (Phase 1.7,
// 8). The measured sets are Cache/shapemeasure/<set>_samples.csv + <set>_bones.csv
// (female_body, female_face, male_body, male_face; format in Tools/shape_fit.py).
// Run: -executeMethod ShapeMapTest.Run [-set female_body|female_face|male_body|male_face|all] [-map <file>]
//        [-bodymap <file>] [-facemap <file>] [-limit N] [-data <dir>] [-remap game|assembler|none] [-ext] [-label <name>]
//        [-allow <file>] [-nocorrect]
//   -set           one set, several joined by '+', or all (default) (the four 0..1 sets; the _oor sets by name)
//   -map           map file for the set's kind (body or face) instead of Resources/shape_*_map (ShapeEngine.*MapOverride);
//                  with -set all use -bodymap / -facemap
//   -limit N       first N samples of each set (default: all)
//   -remap         slider value handling before SetValue:
//                    game (default): as measured: body slider 32 as Lerp(0, 0.8, v) (Tools/survey/shape/shape_remaps.fitted.txt),
//                      male values through ShapeEngine.CardValue (clamped to 0..1 only with SliderRange = Clamp) and
//                      face cut to 59 values; the male height slider is NOT pinned
//                      (the measurement ran with the HS2_UnlockPlayerHeight patch, Cache/shapemeasure/README.md)
//                    assembler: as CharaAssembler.ApplyShapes (game + male body slider 0 pinned at 0.75)
//                    none: raw values
//   -ext           keep the card plugin data (ABMX etc.; off by default so only the shape stage moves the bones)
//   -nocorrect     body sets: do not run the joint correction after each sample (by default all 8 categories run once
//                  after the shape stage, as in the measured Studio, where the HS2US plugin switches them on; the
//                  correction's LookAt rows make cf_J_Hand_dam_* depend on the hand shape)
//   -label         run name for the report section (default: derived from the map files)
//   -allow         allow list of known differences (default Tools/shape_allowlist.tsv; format in its header)
// Builds the measured character headless (BR-Chan HS2 Rev2 for female sets, the first male card of
// <game>/UserData/chara/male for male sets), then for every sample sets all sliders through the character's
// ShapeEngine (SetValue, Apply) and compares each Dst bone (local TRS varies over the samples by more than 1e-5,
// Src bone names excluded) with the dump: position / scale component error, rotation as quaternion angle.
// PASS per set: max position error < 1e-4, angle < 0.05 deg, scale < 1e-4 on every Dst bone, except the known differences
// of the allow list (Tools/shape_allowlist.tsv: set pattern, bone, joint-correction mode, a tolerance per quantity, reason):
// a matching bone over a threshold but within its tolerances is allowed (reported with its values, left out of the
// set's maxima and means); over its tolerance it FAILs. The set line and the RESULT line give "N known differences
// allowed"; matching entries that the run did not need (the bone is within the thresholds, or not a Dst bone) are listed.
// Out-of-range sets (v0.2): <set>_oor (female_body_oor, ...; Tools/shape_measure.py --oor)
// hold sweeps at -1, -0.5, 0, 0.5, 1, 1.5, 2 and random vectors in [-1, 2), measured with the slider extender of the
// reference install. They run with ShapeEngine.SliderRange = Extrapolate (the default) and the thresholds above; the allow
// list matches them under their base set name. Each _oor set also checks the Clamp switch: every sample set with
// SliderRange = Clamp must give, bit for bit, the transforms of the same sample clamped to 0..1 before SetValue (the
// behaviour before v0.2). Male values reach SetValue through ShapeEngine.CardValue (the range check only with Clamp).
// Output: Spike/out/shape/runs/<label>.txt; Spike/out/shape/shape_report.txt = all runs concatenated.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.Chara;
using HS2Next.Content;
using HS2Next.Data;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static class ShapeMapTest
{
    const float TolPos = 1e-4f, TolAngle = 0.05f, TolScale = 1e-4f, VaryTol = 1e-5f;

    // One known difference of the allow list (Tools/shape_allowlist.tsv): measured differences the shape map does not produce.
    sealed class Allowed
    {
        public string Set, Bone, Correction, Reason;
        public float Pos, Angle, Scale;    // tolerances (the test thresholds where the file says '-')
        public int Line;

        public bool Applies(string set, bool correctionOn) =>
            Glob(Set, set) && (Correction == "any" || (Correction == "on") == correctionOn);

        static bool Glob(string pattern, string s) =>
            System.Text.RegularExpressions.Regex.IsMatch(s, "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*") + "$");
    }

    static string AllowFile => Path.Combine(ContentSettings.ProjectRoot, "Tools", "shape_allowlist.tsv");

    // Reads the allow list; null with an error message when a line is malformed (the run then FAILs).
    static List<Allowed> ReadAllowList(string path, out string error)
    {
        error = null;
        var list = new List<Allowed>();
        if (!File.Exists(path)) return list;
        int n = 0;
        foreach (var raw in File.ReadAllLines(path))
        {
            n++;
            var line = raw.TrimEnd('\r');
            if (line.Trim().Length == 0 || line.TrimStart().StartsWith("#")) continue;
            var c = line.Split('\t');
            float Tol(string s, float def) => s.Trim() == "-" ? def : float.Parse(s.Trim(), NumberStyles.Float, Inv);
            try
            {
                if (c.Length < 7) throw new FormatException("7 tab-separated columns expected");
                var e = new Allowed { Set = c[0].Trim(), Bone = c[1].Trim(), Correction = c[2].Trim(), Reason = c[6].Trim(), Line = n };
                if (e.Correction != "on" && e.Correction != "off" && e.Correction != "any") throw new FormatException("correction must be on, off or any");
                e.Pos = Tol(c[3], TolPos); e.Angle = Tol(c[4], TolAngle); e.Scale = Tol(c[5], TolScale);
                list.Add(e);
            }
            catch (Exception ex) { error = $"{Path.GetFileName(path)} line {n}: {ex.Message}"; return null; }
        }
        return list;
    }

    static readonly string[] AllSets = { "female_body", "female_face", "male_body", "male_face" };
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "shape");

    static string Arg(string name, string def)
    {
        var a = TestArgs.Get();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : def;
    }

    static bool Flag(string name) => TestArgs.Get().Contains(name);

    sealed class Sample { public string Id, Kind; public float[] V; }

    sealed class BoneRec { public string Name, Path; public Vector3[] P; public Quaternion[] Q; public Vector3[] S; }

    public static void Run()
    {
        string set = Arg("-set", "all");
        string data = Arg("-data", Path.Combine(ContentSettings.ProjectRoot, "Cache", "shapemeasure"));
        int limit = int.Parse(Arg("-limit", "0"), Inv);
        string remap = Arg("-remap", "game");
        bool ext = Flag("-ext");
        string map = Arg("-map", null), bodyMap = Arg("-bodymap", null), faceMap = Arg("-facemap", null);
        var sets = set == "all" ? AllSets.ToList() : set.Split(',', '+').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        if (map != null)
        {
            if (sets.Count != 1) throw new ArgumentException("-map needs a single -set (use -bodymap / -facemap otherwise)");
            if (sets[0].EndsWith("_body")) bodyMap = map; else faceMap = map;
        }
        string mapName = (bodyMap ?? faceMap) is string m ? Path.GetFileNameWithoutExtension(m) : "resources";
        string label = Arg("-label", null) ?? $"{string.Join("+", sets)}__{mapName}";
        label = new string(label.Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '+' || c == '.' ? c : '_').ToArray());

        var rep = new StringBuilder();
        rep.AppendLine($"== run {label}  ({DateTime.Now:yyyy-MM-dd HH:mm})");
        rep.AppendLine($"   body map: {(bodyMap != null ? Path.GetFileName(bodyMap) : "Resources/shape_body_map")}; face map: {(faceMap != null ? Path.GetFileName(faceMap) : "Resources/shape_face_map")}; " +
                       $"remap {remap}; card plugin data {(ext ? "on" : "off")}; limit {(limit > 0 ? limit.ToString() : "all")}");
        rep.AppendLine($"   thresholds: position {TolPos}, angle {TolAngle} deg, scale {TolScale}");
        string allowPath = Arg("-allow", AllowFile);
        var allow = ReadAllowList(allowPath, out string allowError);
        rep.AppendLine(allow == null ? $"   allow list: ERROR {allowError}"
            : $"   allow list: {(File.Exists(allowPath) ? Path.GetFileName(allowPath) : "none (" + Path.GetFileName(allowPath) + " not found)")}, {allow.Count} entries");
        var head = new StringBuilder();
        var gd = CardResolveTest.BuildDb(head);
        int fails = allow == null ? 1 : 0, ran = 0, allowed = 0;
        try
        {
            ShapeEngine.BodyMapOverride = bodyMap != null ? File.ReadAllText(bodyMap) : null;
            ShapeEngine.FaceMapOverride = faceMap != null ? File.ReadAllText(faceMap) : null;
            foreach (var s in allow == null ? new List<string>() : sets)
            {
                try
                {
                    var r = RunSet(gd, s, data, limit, remap, ext, allow, rep, out int nAllowed);
                    allowed += nAllowed;
                    if (r == null) continue;
                    ran++;
                    if (r == false) fails++;
                }
                catch (Exception e)
                {
                    fails++;
                    rep.AppendLine($"-- {s}: EXCEPTION {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
                }
            }
        }
        finally
        {
            ShapeEngine.BodyMapOverride = null;
            ShapeEngine.FaceMapOverride = null;
            gd.UnloadAll();
        }
        rep.AppendLine($"   RESULT {label}: {ran} sets, {fails} FAIL, {allowed} known difference{(allowed == 1 ? "" : "s")} allowed");
        var runs = Path.Combine(OutDir, "runs");
        Directory.CreateDirectory(runs);
        File.WriteAllText(Path.Combine(runs, label + ".txt"), rep.ToString());
        var all = new StringBuilder("Shape map verification (Editor/ShapeMapTest.cs): one section per run, newest file last.\n\n");
        foreach (var f in Directory.GetFiles(runs, "*.txt").OrderBy(f => File.GetLastWriteTimeUtc(f)))
            all.AppendLine(File.ReadAllText(f));
        File.WriteAllText(Path.Combine(OutDir, "shape_report.txt"), all.ToString());
        Debug.Log($"SHAPE MAP TEST DONE ({label}): {ran} sets, {fails} FAIL, {allowed} known differences allowed\n{rep}");
        if (fails > 0) TestExit.Exit(1);
    }

    // null = skipped (no data), true = PASS, false = FAIL; allowedCount = bones allowed by the allow list
    static bool? RunSet(ContentCatalog gd, string set, string dataDir, int limit, string remap, bool ext, List<Allowed> allow, StringBuilder rep, out int allowedCount)
    {
        allowedCount = 0;
        string sp = Path.Combine(dataDir, set + "_samples.csv"), bp = Path.Combine(dataDir, set + "_bones.csv");
        if (!File.Exists(sp) || !File.Exists(bp)) { rep.AppendLine($"-- {set}: no measurement files, skipped"); return null; }
        bool male = set.StartsWith("male"), body = set.Contains("_body"), oor = set.EndsWith("_oor");
        string baseSet = oor ? set.Substring(0, set.Length - 4) : set;   // allow-list name
        string card = male ? FirstMaleCard() : Path.Combine(CardResolveTest.GameRoot, @"UserData\chara\female\BR-Chan HS2 Rev2.png");
        if (card == null || !File.Exists(card)) { rep.AppendLine($"-- {set}: card not found"); return false; }

        var samples = ReadSamples(sp);
        if (limit > 0 && samples.Count > limit) samples = samples.Take(limit).ToList();
        var bones = ReadBones(bp, samples);

        var asm = new CharaAssembler(gd);
        asm.ExtOptions.Enabled = ext;
        var chara = asm.BuildFromDocuments(card);
        try
        {
            if (asm.Context?.Motion != null) asm.Context.Motion.AutoStep = false;
            var corr = body && !Flag("-nocorrect") ? asm.Context?.Motion?.Get<HS2Next.Motion.Corrective.JointCorrectionStage>() : null;
            corr?.EnableAll(true);
            var engine = body ? asm.BodyShape : asm.FaceShape;
            Transform root = body ? CharaAssembler.Find(asm.Context.Anim.transform, "cf_J_Root") : asm.Context.HeadBone ? asm.Context.HeadBone.transform : null;
            rep.AppendLine($"-- {set}: card {Path.GetFileName(card)} ({(asm.IsMale ? "male" : "female")}), {samples.Count} samples, {bones.Count} bones dumped");
            if (engine == null || root == null) { rep.AppendLine("   no ShapeEngine / dump root on the built character"); return false; }
            rep.AppendLine($"   engine: {engine.Summary}; joint correction {(corr != null ? "all 8 categories, once after each sample" : "off")}");

            var srcNames = SrcNames(gd, body, male);   // Src bones of the slider list (not transforms of the dump)
            // Dst bones: local TRS varies over the samples
            var dst = bones.Values.Where(b => !srcNames.Contains(b.Name) && Varies(b)).OrderBy(b => b.Path, StringComparer.Ordinal).ToList();
            var tf = new Dictionary<BoneRec, Transform>();
            var missing = new List<string>();
            foreach (var b in dst)
            {
                var t = Resolve(root, b);
                if (t) tf[b] = t; else missing.Add(b.Name);
            }
            rep.AppendLine($"   Dst bones (vary > {VaryTol}): {dst.Count}; not found on the HS2Next character: {missing.Count}{(missing.Count > 0 ? " (" + string.Join(", ", missing.Take(10)) + ")" : "")}");

            var errP = new Dictionary<BoneRec, (float max, double sum)>();
            var errA = new Dictionary<BoneRec, (float max, double sum)>();
            var errS = new Dictionary<BoneRec, (float max, double sum)>();
            foreach (var b in tf.Keys) { errP[b] = (0, 0); errA[b] = (0, 0); errS[b] = (0, 0); }
            var kindMax = new Dictionary<BoneRec, Dictionary<string, float[]>>();   // per bone: sample kind → max position / angle / scale
            foreach (var b in tf.Keys) kindMax[b] = new Dictionary<string, float[]>();
            for (int si = 0; si < samples.Count; si++)
            {
                var v = Remap(samples[si].V, remap, male, body);
                for (int i = 0; i < v.Length; i++) engine.SetValue(i, v[i]);
                engine.Apply();
                corr?.Refresh();
                foreach (var kv in tf)
                {
                    var b = kv.Key; var t = kv.Value;
                    if (float.IsNaN(b.P[si].x)) continue;
                    float dp = MaxAbs(t.localPosition - b.P[si]);
                    float da = Angle(t.localRotation, b.Q[si]);
                    float ds = MaxAbs(t.localScale - b.S[si]);
                    errP[b] = (Mathf.Max(errP[b].max, dp), errP[b].sum + dp);
                    errA[b] = (Mathf.Max(errA[b].max, da), errA[b].sum + da);
                    errS[b] = (Mathf.Max(errS[b].max, ds), errS[b].sum + ds);
                    if (!kindMax[b].TryGetValue(samples[si].Kind, out var km)) kindMax[b][samples[si].Kind] = km = new float[3];
                    km[0] = Mathf.Max(km[0], dp); km[1] = Mathf.Max(km[1], da); km[2] = Mathf.Max(km[2], ds);
                }
            }

            // known differences (allow list): a matching bone over a threshold is allowed within its tolerances, else it FAILs
            bool Over(BoneRec b) => errP[b].max >= TolPos || errA[b].max >= TolAngle || errS[b].max >= TolScale;
            var entries = allow.Where(e => e.Applies(baseSet, corr != null)).ToList();
            var byName = new Dictionary<string, Allowed>();
            foreach (var e in entries) if (!byName.ContainsKey(e.Bone)) byName[e.Bone] = e;
            var allowedBones = new List<BoneRec>();
            int overTolerance = 0;
            var notNeeded = new List<string>();
            foreach (var b in tf.Keys.OrderBy(b => b.Name, StringComparer.Ordinal).ToList())
            {
                if (!byName.TryGetValue(b.Name, out var e)) continue;
                if (!Over(b)) { notNeeded.Add($"{b.Name} (within the thresholds: pos {errP[b].max:G3} angle {errA[b].max:G3} scale {errS[b].max:G3})"); continue; }
                bool within = errP[b].max <= e.Pos && errA[b].max <= e.Angle && errS[b].max <= e.Scale;
                string vals = $"pos {errP[b].max:G3} (≤ {e.Pos:G3}) angle {errA[b].max:G3} deg (≤ {e.Angle:G3}) scale {errS[b].max:G3} (≤ {e.Scale:G3})";
                if (within)
                {
                    allowedBones.Add(b);
                    rep.AppendLine($"   known difference allowed (allow list line {e.Line}): {b.Name} {vals}: {e.Reason}");
                }
                else
                {
                    overTolerance++;
                    rep.AppendLine($"   FAIL known difference over its tolerance (allow list line {e.Line}): {b.Name} {vals}: {e.Reason}");
                }
            }
            foreach (var e in entries)
                if (!tf.Keys.Any(b => b.Name == e.Bone)) notNeeded.Add($"{e.Bone} (not a Dst bone of this set)");
            if (notNeeded.Count > 0) rep.AppendLine($"   allow-list entries not needed in this run: {string.Join("; ", notNeeded)}");
            foreach (var b in allowedBones) { tf.Remove(b); errP.Remove(b); errA.Remove(b); errS.Remove(b); kindMax.Remove(b); }
            allowedCount = allowedBones.Count;

            int n = Math.Max(1, samples.Count), nb = Math.Max(1, tf.Count);
            float mp = tf.Count > 0 ? errP.Values.Max(e => e.max) : 0, ma = tf.Count > 0 ? errA.Values.Max(e => e.max) : 0, ms = tf.Count > 0 ? errS.Values.Max(e => e.max) : 0;
            double meanP = errP.Values.Sum(e => e.sum) / (n * nb), meanA = errA.Values.Sum(e => e.sum) / (n * nb), meanS = errS.Values.Sum(e => e.sum) / (n * nb);
            bool pass = mp < TolPos && ma < TolAngle && ms < TolScale && missing.Count == 0 && overTolerance == 0;
            rep.AppendLine($"   {(pass ? "PASS" : "FAIL")}  position max {mp:G4} mean {meanP:G4}; angle max {ma:G4} deg mean {meanA:G4}; scale max {ms:G4} mean {meanS:G4}; " +
                           $"{allowedCount} known difference{(allowedCount == 1 ? "" : "s")} allowed{(overTolerance > 0 ? $", {overTolerance} over their tolerance" : "")}");
            var kinds = new SortedDictionary<string, float[]>(StringComparer.Ordinal);
            foreach (var d in kindMax.Values)
                foreach (var kv in d)
                {
                    if (!kinds.TryGetValue(kv.Key, out var k)) kinds[kv.Key] = k = new float[3];
                    for (int i = 0; i < 3; i++) k[i] = Mathf.Max(k[i], kv.Value[i]);
                }
            rep.AppendLine("   per sample kind (max position / angle / scale): " + string.Join("; ", kinds.Select(k => $"{k.Key} {k.Value[0]:G3} / {k.Value[1]:G3} / {k.Value[2]:G3}")));
            if (oor)
            {
                // Clamp switch = the behaviour before v0.2, bit for bit (values clamped to 0..1 before SetValue)
                var ts = tf.Values.ToList();
                int diff = 0; string first = null;
                var old = ShapeEngine.SliderRange;
                try
                {
                    for (int si = 0; si < samples.Count; si++)
                    {
                        var v = Remap(samples[si].V, remap, male, body);
                        ShapeEngine.SliderRange = ShapeEngine.Range.Clamp;
                        for (int i = 0; i < v.Length; i++) engine.SetValue(i, v[i]);
                        engine.Apply(); corr?.Refresh();
                        var a = ts.Select(t => (t.localPosition, t.localRotation, t.localScale)).ToList();
                        ShapeEngine.SliderRange = ShapeEngine.Range.Extrapolate;
                        for (int i = 0; i < v.Length; i++) engine.SetValue(i, Mathf.Clamp01(v[i]));
                        engine.Apply(); corr?.Refresh();
                        for (int k = 0; k < ts.Count; k++)
                        {
                            var t = ts[k];
                            if (Bits(a[k].localPosition, t.localPosition) && Bits(a[k].localRotation, t.localRotation) && Bits(a[k].localScale, t.localScale)) continue;
                            diff++;
                            first ??= $"sample {samples[si].Id} {t.name}";
                        }
                    }
                }
                finally { ShapeEngine.SliderRange = old; }
                bool same = diff == 0;
                if (!same) pass = false;
                rep.AppendLine($"   {(same ? "PASS" : "FAIL")}  Clamp switch = values clamped to 0..1 (the behaviour before v0.2), bit for bit: {samples.Count} samples x {ts.Count} bones, {diff} differing{(first != null ? " (first: " + first + ")" : "")}");
            }
            int bad = tf.Keys.Count(b => errP[b].max >= TolPos || errA[b].max >= TolAngle || errS[b].max >= TolScale);
            rep.AppendLine($"   bones over a threshold: {bad} of {tf.Count}");
            var worst = tf.Keys.OrderByDescending(b => Math.Max(errP[b].max / TolPos, Math.Max(errA[b].max / TolAngle, errS[b].max / TolScale))).Take(12);
            foreach (var b in worst)
                rep.AppendLine($"     {b.Name,-34} pos {errP[b].max:G3}  angle {errA[b].max:G3}  scale {errS[b].max:G3}");
            return pass;
        }
        finally { Object.DestroyImmediate(chara); }
    }

    // Slider values as the measured game received them (see -remap in the header).
    static float[] Remap(float[] src, string remap, bool male, bool body)
    {
        var v = (float[])src.Clone();
        if (remap == "none") return v;
        if (male)
        {
            for (int i = 0; i < v.Length; i++) v[i] = ShapeEngine.CardValue(v[i], true);
            if (!body && v.Length > 59) Array.Resize(ref v, 59);
            if (remap == "assembler" && body && v.Length > 0) v[0] = 0.75f;
        }
        if (body && v.Length > 32) v[32] = Mathf.Lerp(0, 0.8f, v[32]);
        return v;
    }

    static bool Bits(Vector3 a, Vector3 b) => BitConverter.SingleToInt32Bits(a.x) == BitConverter.SingleToInt32Bits(b.x)
        && BitConverter.SingleToInt32Bits(a.y) == BitConverter.SingleToInt32Bits(b.y) && BitConverter.SingleToInt32Bits(a.z) == BitConverter.SingleToInt32Bits(b.z);
    static bool Bits(Quaternion a, Quaternion b) => Bits(new Vector3(a.x, a.y, a.z), new Vector3(b.x, b.y, b.z)) && BitConverter.SingleToInt32Bits(a.w) == BitConverter.SingleToInt32Bits(b.w);

    // Quaternion.Angle reports 0 below about 0.1 deg (its dot-product epsilon); this keeps small angles.
    static float Angle(Quaternion a, Quaternion b)
    {
        var d = Quaternion.Inverse(a) * b;
        double v = Math.Sqrt((double)d.x * d.x + (double)d.y * d.y + (double)d.z * d.z);
        return (float)(2 * Math.Atan2(v, Math.Abs((double)d.w)) * 180 / Math.PI);
    }

    static float MaxAbs(Vector3 d) => Mathf.Max(Mathf.Abs(d.x), Mathf.Max(Mathf.Abs(d.y), Mathf.Abs(d.z)));

    static bool Varies(BoneRec b)
    {
        Vector3 pmin = Vector3.positiveInfinity, pmax = Vector3.negativeInfinity, smin = Vector3.positiveInfinity, smax = Vector3.negativeInfinity;
        Quaternion q0 = default; bool first = true; float qd = 0;
        for (int i = 0; i < b.P.Length; i++)
        {
            if (float.IsNaN(b.P[i].x)) continue;
            pmin = Vector3.Min(pmin, b.P[i]); pmax = Vector3.Max(pmax, b.P[i]);
            smin = Vector3.Min(smin, b.S[i]); smax = Vector3.Max(smax, b.S[i]);
            var q = b.Q[i];
            if (first) { q0 = q; first = false; continue; }
            if (Quaternion.Dot(q0, q) < 0) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
            qd = Mathf.Max(qd, Mathf.Max(Mathf.Max(Mathf.Abs(q.x - q0.x), Mathf.Abs(q.y - q0.y)), Mathf.Max(Mathf.Abs(q.z - q0.z), Mathf.Abs(q.w - q0.w))));
        }
        return MaxAbs(pmax - pmin) > VaryTol || MaxAbs(smax - smin) > VaryTol || qd > VaryTol;
    }

    static Transform Resolve(Transform root, BoneRec b)
    {
        var p = b.Path ?? "";
        if (p.StartsWith(root.name + "/")) p = p.Substring(root.name.Length + 1);
        else if (p == root.name) return root;
        Transform t = p.Length > 0 ? root.Find(p) : null;
        if (t && t.name == b.Name) return t;
        return CharaAssembler.Find(root, b.Name);
    }

    static HashSet<string> SrcNames(ContentCatalog gd, bool body, bool male)
    {
        var names = new HashSet<string>();
        var info = gd.LoadAsset<TextAsset>("list/customshape.unity3d", body ? "cf_custombody" : male ? "cm_customhead" : "cf_customhead");
        if (info)
            foreach (var line in info.text.Split('\n'))
            {
                var c = line.TrimEnd('\r').Split('\t');
                if (c.Length >= 11 && int.TryParse(c[0], out _)) names.Add(c[1]);
            }
        return names;
    }

    static string FirstMaleCard()
    {
        string maleDir = Path.Combine(CardResolveTest.GameRoot, @"UserData\chara\male");
        return Directory.Exists(maleDir)
            ? Directory.GetFiles(maleDir, "*.png", SearchOption.AllDirectories).OrderBy(f => f.Substring(maleDir.Length).Replace('\\', '/'), StringComparer.Ordinal).FirstOrDefault()
            : null;
    }

    // ---------------------------------------------------------------- CSV
    static List<string> SplitCsv(string line)
    {
        var r = new List<string>(); var sb = new StringBuilder(); bool q = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (q)
            {
                if (c == '"') { if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; } else q = false; }
                else sb.Append(c);
            }
            else if (c == '"') q = true;
            else if (c == ',') { r.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        r.Add(sb.ToString());
        return r;
    }

    static float F(string s) => float.Parse(s, NumberStyles.Float, Inv);

    static List<Sample> ReadSamples(string path)
    {
        var list = new List<Sample>();
        foreach (var line in File.ReadLines(path).Skip(1))
        {
            if (line.Trim().Length == 0) continue;
            var c = SplitCsv(line);
            int n = int.Parse(c[4], Inv);
            var v = new float[n];
            for (int i = 0; i < n; i++) v[i] = F(c[5 + i]);
            list.Add(new Sample { Id = c[0], Kind = c[1].Trim().ToLowerInvariant(), V = v });
        }
        return list;
    }

    static Dictionary<string, BoneRec> ReadBones(string path, List<Sample> samples)
    {
        var idx = new Dictionary<string, int>();
        for (int i = 0; i < samples.Count; i++) idx[samples[i].Id] = i;
        var bones = new Dictionary<string, BoneRec>();
        Dictionary<string, int> col = null;
        foreach (var line in File.ReadLines(path))
        {
            if (line.Trim().Length == 0) continue;
            var c = SplitCsv(line);
            if (col == null) { col = new Dictionary<string, int>(); for (int i = 0; i < c.Count; i++) col[c[i].Trim().TrimStart('﻿')] = i; continue; }
            if (!idx.TryGetValue(c[col["sample"]], out int si)) continue;
            string name = c[col["bone"]], p = c[col["path"]], key = p.Length > 0 ? p : name;
            if (!bones.TryGetValue(key, out var b))
            {
                bones[key] = b = new BoneRec { Name = name, Path = p, P = new Vector3[samples.Count], Q = new Quaternion[samples.Count], S = new Vector3[samples.Count] };
                for (int i = 0; i < samples.Count; i++) b.P[i] = new Vector3(float.NaN, 0, 0);
            }
            b.P[si] = new Vector3(F(c[col["px"]]), F(c[col["py"]]), F(c[col["pz"]]));
            b.Q[si] = new Quaternion(F(c[col["qx"]]), F(c[col["qy"]]), F(c[col["qz"]]), F(c[col["qw"]]));
            b.S[si] = new Vector3(F(c[col["sx"]]), F(c[col["sy"]]), F(c[col["sz"]]));
        }
        return bones;
    }
}
