// ItemTest, part 2: the expectation file of package C (Cache/items/expect_<install
// hash>.json.gz, Tools/survey/item_expect.py) and the checks that read it — M2 (the colour write per slot against the
// file's `writes` table), M5 (ItemDefaults.FromPrefab against the file's `state` for every vanilla row), M9
// (RunCorpus: every vanilla and mod row built, set, toggled, stepped and deleted) — plus M10 (save round trip) and M12
// (build parts, features, findings, icons, sea, child root, fallback).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using HS2Next;
using HS2Next.Content;
using HS2Next.Content.Studio;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static partial class ItemTest
{
    // ================================================================ expectation file

    static JObject expect;
    static string expectPath;
    static readonly Dictionary<string, List<JObject>> expectByKey = new Dictionary<string, List<JObject>>(StringComparer.Ordinal);

    static string KeyText(string guid, int group, int category, int no) => (guid ?? "").ToLowerInvariant() + "|" + group + "|" + category + "|" + no;

    /// <summary>Loads Cache/items/expect_&lt;PathHash(hs2Root)&gt;.json.gz once; false (with a WARN) when it does not exist.</summary>
    static bool LoadExpect()
    {
        if (expect != null) return true;
        var root = CardResolveTest.GameRoot;
        expectPath = Path.Combine(ContentSettings.ProjectRoot, "Cache", "items", "expect_" + ZipmodSourceBase.PathHash(root) + ".json.gz");
        if (!File.Exists(expectPath)) { Info("expectation file " + expectPath + " not found (package C, Tools/survey/item_expect.py)"); return false; }
        var sw = Stopwatch.StartNew();
        using (var fs = File.OpenRead(expectPath))
        using (var gz = new GZipStream(fs, CompressionMode.Decompress))
        using (var tr = new StreamReader(gz, Encoding.UTF8))
        using (var jr = new JsonTextReader(tr) { FloatParseHandling = FloatParseHandling.Double })
            expect = JObject.Load(jr);
        expectByKey.Clear();
        foreach (JObject it in (JArray)expect["items"])
        {
            var k = (JObject)it["key"];
            if (k == null) continue;
            string kt = KeyText((string)k["guid"], (int)k["group"], (int)k["category"], (int)k["no"]);
            if (!expectByKey.TryGetValue(kt, out var l)) expectByKey[kt] = l = new List<JObject>();
            l.Add(it);
        }
        Info($"expectation file {Path.GetFileName(expectPath)}: {((JArray)expect["items"]).Count} rows, written {expect["meta"]?["written"]}, read in {sw.ElapsedMilliseconds} ms");
        return true;
    }

    /// <summary>The expectation record of a list row (several rows with one key: the one naming the row's prefab).</summary>
    static JObject ExpectOf(StudioItem row)
    {
        if (row == null || expect == null) return null;
        if (!expectByKey.TryGetValue(KeyText(row.Key.SourceGuid, row.Group, row.Category, row.No), out var l)) return null;
        if (l.Count == 1) return l[0];
        bool Bundle(JObject x) => string.Equals(((string)x["key"]?["bundle"])?.Replace('\\', '/'), row.BundlePath?.Replace('\\', '/'),StringComparison.OrdinalIgnoreCase);
        var byBundle = l.Where(x => Bundle(x) && (string)x["key"]?["file"] == row.FileName).ToList();
        if (byBundle.Count > 0) return byBundle[byBundle.Count - 1];
        return l.FirstOrDefault(x => (string)x["key"]?["file"] == row.FileName && (string)x["key"]?["name"] == row.Name)
            ?? l.FirstOrDefault(x => (string)x["key"]?["file"] == row.FileName) ?? l[l.Count - 1];
    }

    static string Target(string name) => (string)expect?["meta"]?["targets"]?[name];

    static StudioItem RowOf(RuntimeServices sv, string reference)
    {
        if (reference == null || !ContentId.TryParse(reference, out var id) || id.IsEmpty) return null;
        return sv.Db.TryGetItem(id, out var r) ? r : null;
    }

    // ---------------------------------------------------------------- JSON comparison

    /// <summary>Tolerance of FK Euler triples compared as rotations (Unity's eulerAngles near a gimbal lock: about 0.02 deg).</summary>
    public const float EulerToleranceDeg = 0.05f;
    /// <summary>Prefix of a difference at a gimbal lock of the expectation's Euler triple (reported, not failed).</summary>
    public const string GimbalTag = "[gimbal] ";

    /// <summary>Differences of <paramref name="actual"/> from <paramref name="expected"/> (members of expected only).</summary>
    static void Diff(JToken expected, JToken actual, string path, List<string> into)
    {
        if (into.Count > 20) return;
        if (expected == null || expected.Type == JTokenType.Null)
        {
            if (actual != null && actual.Type != JTokenType.Null) into.Add(path + ": expected null, got " + actual.ToString(Formatting.None));
            return;
        }
        if (actual == null || actual.Type == JTokenType.Null) { into.Add(path + ": missing (expected " + expected.ToString(Formatting.None) + ")"); return; }
        switch (expected.Type)
        {
            case JTokenType.Object:
                if (!(actual is JObject ao)) { into.Add(path + ": not an object"); return; }
                foreach (var p in ((JObject)expected).Properties()) Diff(p.Value, ao[p.Name], path + "." + p.Name, into);
                if (path.EndsWith(".bones", StringComparison.Ordinal))
                    foreach (var p in ao.Properties()) if (expected[p.Name] == null) into.Add(path + "." + p.Name + ": unexpected");
                return;
            case JTokenType.Array:
                var ea = (JArray)expected;
                if (!(actual is JArray aa) || aa.Count != ea.Count) { into.Add(path + ": array " + actual.ToString(Formatting.None) + " vs " + expected.ToString(Formatting.None)); return; }
                if (path.Contains(".bones.") && ea.Count == 3 && ea.All(x => x.Type != JTokenType.Object))
                {
                    // FK Euler angles compare as rotations: near a gimbal lock Unity's eulerAngles (and the expectation's
                    // decomposition) pick other but equivalent triples and lose a few hundredths of a degree
                    var qe = Quaternion.Euler((float)(double)ea[0], (float)(double)ea[1], (float)(double)ea[2]);
                    var qa = Quaternion.Euler((float)(double)aa[0], (float)(double)aa[1], (float)(double)aa[2]);
                    double ang = RotationCompare.Deg(qe, qa);
                    if (ang > EulerToleranceDeg)
                    {
                        // at x = 90 / 270 (gimbal lock) only y ± z is defined: the expectation's own decomposition can pick
                        // another rotation than Unity's eulerAngles (HS2Next's value comes from Unity, as HS2's does)
                        double x = (((double)ea[0] % 360.0) + 360.0) % 360.0;
                        bool gimbal = Math.Abs(x - 90.0) < 0.01 || Math.Abs(x - 270.0) < 0.01;
                        into.Add((gimbal ? GimbalTag : "") + path + ": " + aa.ToString(Formatting.None) + " vs " + ea.ToString(Formatting.None) + " (" + ang.ToString("G4") + " deg)");
                    }
                    return;
                }
                for (int i = 0; i < ea.Count; i++)
                {
                    Diff(ea[i], aa[i], path + "[" + i + "]", into);
                }
                return;
            case JTokenType.Float:
            case JTokenType.Integer:
                if (actual.Type != JTokenType.Float && actual.Type != JTokenType.Integer) { into.Add(path + ": not a number"); return; }
                double e = (double)expected, a = (double)actual;
                // re-read colours (glass, emission) come through Material.GetColor, whose linear storage round trip differs
                // from the serialized value by float32 ulps (HS2's recorded values equal HS2Next's, the file holds the saved one)
                bool reread = path.StartsWith("item.colors[3].main", StringComparison.Ordinal) || path.StartsWith("item.emission.color", StringComparison.Ordinal);
                bool ok = path.EndsWith(".intensity", StringComparison.Ordinal) ? Math.Abs(e - a) <= 1e-5
                    : reread ? Math.Abs(e - a) <= ColorTolerance * Math.Max(1.0, Math.Abs(e)) : (float)e == (float)a;
                if (!ok) into.Add(path + ": " + a.ToString("R") + " vs " + e.ToString("R"));
                return;
            default:
                if (!JToken.DeepEquals(expected, actual)) into.Add(path + ": " + actual.ToString(Formatting.None) + " vs " + expected.ToString(Formatting.None));
                return;
        }
    }

    /// <summary>Differences of ItemDefaults.FromPrefab from the expectation's `state` (empty = equal), or null without a record.</summary>
    static List<string> DefaultsDiff(RuntimeServices sv, StudioItem row, GameObject prefab, out JObject rec)
    {
        rec = ExpectOf(row);
        if (rec == null || (string)rec["status"] != "ok" || rec["state"] == null) return null;
        var st = ItemDefaults.FromPrefab(prefab, row);
        var actual = FieldJson.ToJson(st);
        var d = new List<string>();
        Diff(rec["state"], actual, "item", d);
        return d;
    }

    // ================================================================ M2 colour write

    // HS2's write order (a′, a″, b, c, d): later fields overwrite earlier ones on the same property
    static readonly string[] WriteOrder =
    {
        "colors[0].main", "colors[0].metallic", "colors[0].glossiness", "colors[0].pattern.color", "colors[0].pattern.uv", "colors[0].pattern.rot", "colors[0].pattern.clamp",
        "colors[1].main", "colors[1].metallic", "colors[1].glossiness", "colors[1].pattern.color", "colors[1].pattern.uv", "colors[1].pattern.rot", "colors[1].pattern.clamp",
        "colors[2].main", "colors[2].metallic", "colors[2].glossiness", "colors[2].pattern.color", "colors[2].pattern.uv", "colors[2].pattern.rot", "colors[2].pattern.clamp",
        "alpha", "emission.color", "emission.power", "lightCancel", "colors[3].main",
    };

    /// <summary>Result of one writes check.</summary>
    public sealed class WriteCheck
    {
        public int Written, Wrong, Unchanged, Changed, Undeclared, UnmatchedRenderers, ParticleWrong;
        public readonly List<string> Notes = new List<string>();
        public bool Ok => Wrong == 0 && Changed == 0 && UnmatchedRenderers == 0 && ParticleWrong == 0;
        public override string ToString() => $"written {Written}, wrong {Wrong}, unchanged {Unchanged}, changed unexpectedly {Changed}, not declared by the URP target {Undeclared}, unmatched renderers {UnmatchedRenderers}, particle colours wrong {ParticleWrong}" + (Notes.Count > 0 ? "; " + string.Join("; ", Notes.Take(6)) : "");
    }

    /// <summary>
    /// Sets every field to its test value and compares every Studio property of every slot with the expectation's `writes`
    /// table: written properties = the test value (bit-equal float32), every other Studio property unchanged. The glass
    /// colour into _Metallic4 / _Glossiness4 (":color") is not reproduced (T4, Q5) and textures are M3 / M4's.
    /// </summary>
    public static WriteCheck CheckWrites(Scene s, string id, JObject rec)
    {
        var w = new WriteCheck();
        var it = s.Item(id);
        var before = Snapshot(it);
        var r = SetAll(s, id);
        if (r.Status == CommandStatus.Error) { w.Wrong++; w.Notes.Add("set failed: " + r); return w; }
        var after = Snapshot(it);
        var expected = new Dictionary<(int, int, string), string>();   // (renderer, slot, prop) → field (last in HS2's order)
        var rs = it.Writer.Renderers;
        var used = new HashSet<int>();
        foreach (JObject er in (JArray)rec["renderers"] ?? new JArray())
        {
            string path = (string)er["path"] ?? "";
            // same-named siblings: prefer the unused renderer whose slot material names match, else the first unused one
            var names = ((JArray)er["slots"] ?? new JArray()).Select(x => (string)x["material"]).ToList();
            var flags = ((JArray)er["slots"] ?? new JArray()).Select(x => ((string)x["flags"] ?? "").Replace("-", "")).ToList();
            int ri = -1;
            for (int i = 0; i < rs.Count && ri < 0; i++)
                if (!used.Contains(i) && rs[i].Path == path && rs[i].Slots.Select(x => x?.MaterialName).SequenceEqual(names) &&
                    Enumerable.Range(0, rs[i].Slots.Length).Select(k => ItemMaterialWriter.FlagsOf(it.Component, rs[i].Renderer, k) ?? "").SequenceEqual(flags)) ri = i;
            for (int i = 0; i < rs.Count && ri < 0; i++)
                if (!used.Contains(i) && rs[i].Path == path && rs[i].Slots.Select(x => x?.MaterialName).SequenceEqual(names)) ri = i;
            for (int i = 0; i < rs.Count && ri < 0; i++) if (!used.Contains(i) && rs[i].Path == path) ri = i;
            if (ri < 0) { w.UnmatchedRenderers++; if (w.Notes.Count < 6) w.Notes.Add("renderer \"" + path + "\" not found"); continue; }
            used.Add(ri);
            foreach (JObject es in (JArray)er["slots"] ?? new JArray())
            {
                var writes = es["writes"] as JObject;
                if (writes == null) continue;
                int si = (int)es["index"];
                foreach (var field in WriteOrder)
                {
                    if (!(writes[field] is JArray props)) continue;
                    foreach (var p in props.Select(x => (string)x))
                        if (!p.EndsWith(":color", StringComparison.Ordinal)) expected[(ri, si, p)] = field;
                }
            }
        }
        foreach (var kv in expected)
        {
            var (ri, si, p) = kv.Key;
            var m = rs[ri].Slots.Length > si ? rs[ri].Slots[si]?.Material : null;
            if (!m) continue;
            if (!after.TryGetValue(kv.Key, out var v)) { w.Undeclared++; continue; }
            var e = Expected(kv.Value);
            if (Same(m, p, v, e)) w.Written++;
            else { w.Wrong++; if (w.Notes.Count < 6) w.Notes.Add($"{rs[ri].Path}[{si}].{p} ({kv.Value}) = {V(v)} expected {V(e)}"); }
        }
        foreach (var kv in before)
        {
            if (expected.ContainsKey(kv.Key)) continue;
            var m = rs[kv.Key.Item1].Slots[kv.Key.Item2].Material;
            if (after.TryGetValue(kv.Key, out var v) && Same(m, kv.Key.Item3, v, kv.Value)) w.Unchanged++;
            else { w.Changed++; if (w.Notes.Count < 6) w.Notes.Add($"{rs[kv.Key.Item1].Path}[{kv.Key.Item2}].{kv.Key.Item3} changed {kv.Value} -> {v}"); }
        }
        var pc = it.ParticleComponent;
        if (pc?.particleColor1 != null)
        {
            var c0 = Expected("colors[0].main");
            foreach (var ps in pc.particleColor1)
            {
                if (!ps) continue;
                var c = ps.main.startColor.color;
                if (c.r != c0.x || c.g != c0.y || c.b != c0.z || c.a != c0.w) w.ParticleWrong++;
            }
        }
        return w;
    }

    static string V(Vector4 v) => $"({v.x:G9}, {v.y:G9}, {v.z:G9}, {v.w:G9})";

    static void M2(RuntimeServices sv)
    {
        MaxColorError = 0f;
        if (expect == null) { Warn("expectation file absent: M2 skipped"); return; }
        var targets = new List<(string label, StudioItem row)>();
        foreach (var no in Vanilla) targets.Add(("vanilla " + no, RowOf(sv, ContentId.Vanilla(no).ToString())));
        foreach (var p in ((JObject)expect["meta"]["targets"]).Properties())
            if (p.Name.StartsWith("shader:", StringComparison.Ordinal) || p.Name == "glassOnly" || p.Name == "nullRenderer")
                targets.Add((p.Name + " " + (string)p.Value, RowOf(sv, (string)p.Value)));
        using (var s = new Scene(sv))
        {
            foreach (var (label, row) in targets)
            {
                if (row == null) { Warn(label + ": not installed"); continue; }
                var rec = ExpectOf(row);
                if (rec == null || rec["renderers"] == null) { Warn(label + ": no expectation record"); continue; }
                var id = s.Add(row.Key);
                if (id == null) continue;
                var w = CheckWrites(s, id, rec);
                Check($"{label} ({row.Name}): flagged slots = the test values, everything else unchanged", w.Ok, w.ToString());
                var lc = s.Item(id).Writer.Renderers.SelectMany(e => e.Slots).Where(x => x?.Material && x.Material.shader.FindPropertyIndex("_LightCancel") >= 0).ToList();
                if (lc.Count > 0) Check($"{label}: _LightCancel never written", lc.All(x => x.Material.GetFloat("_LightCancel") != 0.4f));
                s.Runtime.Ops.Delete(new[] { id });
                s.Runtime.Apply();
            }
        }
        Info($"largest colour deviation through the material's linear storage: {MaxColorError:G6}");
    }

    // ================================================================ M5 defaults (vanilla)

    static void M5(RuntimeServices sv)
    {
        if (expect == null) { Warn("expectation file absent: M5 skipped"); return; }
        int ok = 0, bad = 0, noRecord = 0, noPrefab = 0;
        var notes = new List<string>();
        var sw = Stopwatch.StartNew();
        foreach (var row in sv.Db.Items.All.Where(r => r.IsVanilla))
        {
            GameObject prefab = null;
            try { prefab = sv.Catalog.LoadAsset<GameObject>(row); } catch (Exception) { }
            if (!prefab) { noPrefab++; continue; }
            var d = DefaultsDiff(sv, row, prefab, out _);
            if (d == null) { noRecord++; continue; }
            if (d.Count == 0) ok++;
            else { bad++; if (notes.Count < 10) notes.Add(row.Key + " " + row.Name + ": " + string.Join("; ", d.Take(3))); }
        }
        Info($"{ok + bad} vanilla rows compared in {sw.Elapsed.TotalSeconds:F0} s; {noRecord} without a record, {noPrefab} prefabs not loadable");
        Check("ItemDefaults.FromPrefab = the expectation's state for every vanilla row", bad == 0 && ok > 1900, $"{ok} equal, {bad} differ: " + string.Join(" | ", notes));
        Check("every vanilla row has a record and a prefab", noRecord == 0 && noPrefab == 0, $"{noRecord} / {noPrefab}");

        // T2: the glass-only targets keep their glass colour (HS2: white, hs2Diff)
        var g = RowOf(sv, Target("glassOnly"));
        if (g == null) { Warn("glass-only target not installed"); return; }
        var gp = sv.Catalog.LoadAsset<GameObject>(g);
        var gd = DefaultsDiff(sv, g, gp, out var grec);
        Check($"glass-only {g.Key} ({g.Name}): defaults = expectation (T2), HS2's white glass listed in hs2Diff", gd != null && gd.Count == 0 && grec["hs2Diff"] != null && grec["hs2Diff"].HasValues,
            gd == null ? "no record" : string.Join("; ", gd) + " hs2Diff " + grec["hs2Diff"]?.ToString(Formatting.None));
    }

    // ================================================================ M10 save round trip

    /// <summary>A fixed save time (the manifest's modifiedUtc would otherwise differ across a second boundary).</summary>
    static readonly DateTime FixedClock = new DateTime(2026, 10, 17, 0, 0, 0, DateTimeKind.Utc);

    static void M10(RuntimeServices sv)
    {
        byte[] first, second;
        Dictionary<string, Dictionary<(int, int, string), Vector4>> before;
        var ids = new List<string>();
        using (var s = new Scene(sv))
        {
            foreach (var no in new[] { Bed, Tent, 590, Lamp, 842, 975, Snow, Bonfire }) { var id = s.Add(no); if (id != null) { ids.Add(id); SetAll(s, id); } }
            var bed = ids[0];
            var png = SmallPng(new Color32(200, 40, 90, 255));
            var res = s.Session.Model.File.Res.Add(png);
            s.Set(bed, "item.colors[0].pattern.file", FileRef.FromRes(res));
            s.Set(ids[1], "item.colors[1].pattern.item", sv.Catalog.EntriesOf(ItemTextures.PatternCategory).First(e => e.IsVanilla).Key.ToString());
            var dir = Path.Combine(sv.Hs2Root, "DefaultData", "bg");
            var bg = Directory.GetFiles(dir, "*.png").OrderBy(x => x, StringComparer.OrdinalIgnoreCase).First();
            s.Set(ids[5], "item.panel", FileRef.FromHs2("DefaultData/bg/" + Path.GetFileName(bg)));
            s.Set(ids[7], "item.options[0]", false);
            s.Set(ids[0], "item.anime.speed", 0.5f);
            s.Set(ids[0], "item.dynamicBones", false);
            var fkRow = sv.Db.Items.All.FirstOrDefault(r => r.IsVanilla && r.Bones != null && r.Bones.Count > 0);
            if (fkRow != null)
            {
                var fid = s.Add(fkRow.Key);
                ids.Add(fid);
                var b0 = fkRow.Bones[0];
                s.Set(fid, "item.fk.bones[" + FieldPath.Quote(b0) + "]", new Vec3(10f, 20f, 30f));
                s.Set(fid, "item.fk.enabled", true);
                Info("FK item " + fkRow.Key + " " + fkRow.Name + ", bone " + b0);
            }
            before = ids.ToDictionary(x => x, x => Snapshot(s.Item(x)));
            first = s.Session.ToBytes(new SaveOptions { Clock = () => FixedClock });
        }
        using (var s2 = new Scene(sv))
        {
            var lr = s2.Session.Load(first);
            Check("the saved scene loads", lr.Ok, lr.Error?.Message);
            if (!lr.Ok) return;
            s2.Runtime.Apply();
            second = s2.Session.ToBytes(new SaveOptions { Clock = () => FixedClock });
            bool same = first.SequenceEqual(second);
            Check("save → load → save byte-identical", same, first.Length + " / " + second.Length + " bytes");
            if (!same)
            {
                File.WriteAllBytes(Path.Combine(OutDir, "m10_first.hs2nscene"), first);
                File.WriteAllBytes(Path.Combine(OutDir, "m10_second.hs2nscene"), second);
            }
            int diff = 0, total = 0;
            var notes = new List<string>();
            foreach (var id in ids)
            {
                var it = s2.Item(id);
                if (it == null) { diff++; notes.Add(id + " not realized"); continue; }
                var after = Snapshot(it);
                foreach (var kv in before[id])
                {
                    total++;
                    var m = it.Writer.Renderers[kv.Key.Item1].Slots[kv.Key.Item2].Material;
                    if (!after.TryGetValue(kv.Key, out var v) || !Same(m, kv.Key.Item3, v, kv.Value)) { diff++; if (notes.Count < 5) notes.Add(it.Row?.No + ":" + kv.Key); }
                }
            }
            Check("realized Studio property values after the load equal the values before the save", diff == 0 && total > 0, $"{total - diff} of {total}; " + string.Join(", ", notes));
            var bedIt = s2.Item(ids[0]);
            Check("the res pattern is realized after the load", Mask(bedIt, 0) != null && bedIt.PatternSources[0] != null, bedIt.PatternSources[0]);
        }
    }

    // ================================================================ M12 parts, features, findings

    static void M12(RuntimeServices sv)
    {
        using (var s = new Scene(sv, standardParts: true))
        {
            int f0 = s.Session.Findings.Count;
            var ids = Vanilla.ToDictionary(no => no, no => s.Add(no));
            foreach (var no in Vanilla)
            {
                var it = s.Item(ids[no]);
                Info($"{no}: {it?.Row?.Key} {it?.Row?.Name} fallback {it?.IsFallback} instance {(it?.Instance ? it.Instance.name : "none")} state {it?.State} {it?.Detail}");
            }
            foreach (var f in s.Session.Findings.Skip(f0)) Info("finding " + f.Severity + " " + f.Code + " " + f.NodeId + ": " + f.Message);
            var parts = s.Session.Findings.Skip(f0).Where(f => f.Code == "item.partFailed").ToList();
            Check("no build part failed on the vanilla targets", parts.Count == 0, string.Join(" | ", parts.Select(f => f.Message).Take(3)));
            ItemFeatures F(int no) => s.Item(ids[no])?.Features;
            Check(Bed + ": colours 1-3 and patterns 1-3 offered", F(Bed) != null && F(Bed).Color.All(x => x) && F(Bed).Pattern.All(x => x));
            Check("590: alpha offered", F(590)?.Alpha == true);
            Check(Lamp + ": emission offered (colour / power by the original shader)", F(Lamp)?.Emission == true && (F(Lamp).EmissionColor || F(Lamp).EmissionPower));
            Check("842: glass offered", F(842)?.Glass == true);
            Check("975: panel offered", F(975)?.Panel == true);
            Check(Snow + ": particle colour offered (no ItemComponent)", F(Snow)?.Particle == true && F(Snow).Color[0] && s.Item(ids[Snow]).Component == null);
            Check(Bonfire + ": options, lights and particles", F(Bonfire)?.Options > 0 && F(Bonfire).Lights > 0 && s.Item(ids[Bonfire]).Particles.Count > 0);
            Check("no target offers light cancel (dead in HS2)", Vanilla.All(no => F(no) == null || !F(no).LightCancel));
            var g = s.Item(ids[957]);
            Check("957: icon registered and active", g?.Icon && g.Icon.gameObject.activeSelf && ItemIcons.Renderers.Contains(g.Icon));
            ItemIcons.Visible = false;
            Check("items.gimmickIcons off → icon inactive", g?.Icon && !g.Icon.gameObject.activeSelf);
            ItemIcons.Visible = true;
            s.Set(ids[957], "visible", false);
            Check("hidden node → icon inactive", g?.Icon && !g.Icon.gameObject.activeSelf);
            s.Set(ids[957], "visible", true);
            bool was = g.Icon.forceRenderingOff;
            using (ItemIcons.HideForCapture()) Check("HideForCapture hides the icon renderer", g.Icon.forceRenderingOff);
            Check("… and restores it", g.Icon.forceRenderingOff == was);
            s.Runtime.LateApply();
            var cam = s.Runtime.GetPart<SceneViewCamera>()?.Camera;
            Check("the icon faces the view camera after LateApply", cam && RotationCompare.Deg(g.Icon.transform.rotation, cam.transform.rotation) < 1e-3f);
            var sea = s.Item(ids[557]);
            var seaRs = sea?.Component?.renderersSea?.Where(r => r).ToList() ?? new List<Renderer>();
            Check("557: sea renderers lose USINGWATERVOLUME on their copies", seaRs.Count > 0 && seaRs.All(r => r.sharedMaterials.Where(m => m).All(m => !m.IsKeywordEnabled("USINGWATERVOLUME"))), seaRs.Count + " sea renderers");
            var rot = s.Item(ids[935]);
            Check("935: children attach to ItemComponent.childRoot", rot?.Component?.childRoot && rot.Attach == rot.Component.childRoot);
            var fb = s.Item(ids[399]);
            Check("399 (the fallback item itself) builds", fb?.State == RealizationState.Built && !fb.IsFallback);
            double ms = Vanilla.Select(no => s.Item(ids[no])?.BuildMilliseconds ?? 0).Average();
            Info($"mean build time of the vanilla targets {ms:F1} ms (headless)");

            // a stored row whose prefab does not load builds the fallback (HS2) — the expectation's "prefab not found" rows
            if (expect != null)
            {
                var missing = ((JArray)expect["items"]).Cast<JObject>().FirstOrDefault(x => (string)x["status"] == "prefab not found");
                var mrow = missing != null ? RowOf(sv, (string)missing["ref"]) : null;
                if (mrow != null)
                {
                    var st = new ItemNodeState { Ref = mrow.Key.ToString(), Group = mrow.Group, Category = mrow.Category };
                    int f1 = s.Session.Findings.Count;
                    var r = s.Session.AddItem(st);
                    s.Runtime.Apply();
                    var mi = s.Item(r.NewId);
                    Check($"{mrow.Key} (prefab not found): the fallback 399 is built, item.missing, the ref kept",
                        mi?.State == RealizationState.Built && mi.IsFallback && s.State(r.NewId).Ref == mrow.Key.ToString() && s.Session.Findings.Skip(f1).Any(f => f.Code == "item.missing"));
                }
                var nr = RowOf(sv, Target("nullRenderer"));
                if (nr != null)
                {
                    int f2 = s.Session.Findings.Count;
                    var nid = s.Add(nr.Key);
                    var ni = s.Item(nid);
                    Check($"{nr.Key} ({nr.Name}): a renderer entry without a renderer is skipped with item.nullRenderer", ni?.State == RealizationState.Built && ni.Writer.NullRendererEntries > 0 &&
                        s.Session.Findings.Skip(f2).Any(f => f.Code == "item.nullRenderer") && !s.Session.Findings.Skip(f2).Any(f => f.Code == "item.partFailed"));
                }
            }
        }
    }
}
