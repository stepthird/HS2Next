// MaterialEditTest.corpus, the all-card rows (integrator T). One process, cards by hash, numbers and shader
// property names only: nothing is shown, no image is opened.
//   MY1  MaterialEditTest_Corpus.Run: the import of every file against the record taken on 1.2.0's import code.
//   MY2  every file with items: on each array, the first float, colour, texture with an offset, renderer Enabled and shader with a queue get a
//        changed value through MaterialEdits.Set and the array's field path, as the op does. The documents' text then differs from the
//        original's in one line, the named member of that element; every other element is the same object; Set back with the old value gives
//        the array's bytes; undo gives the documents' bytes.
//   MY3  every character card with items, built: what each item does now, by type (the current form 3.3's counts), and each stored
//        original against the baseline: how many differ, by how much, per property. Numbers only, no bar.
//   MY4  RunHairCorpus (MJ5).
//   MY5  two coordinates wearing one part, by hash, in both orders on a fresh node: the second has a queue-only item, so going from one to the other changes
//        what the URP mapping reads with no part swap. The row of every coordinate keeps the folder's order and reaches one direction only.
// -only MY2+MY3 picks groups; -limit N takes the first N files of each folder for MY2 and N cards for MY3 (a try-out, not a result).
// The row passes -suite, so a failed group does not end the process before the others. Report Spike/out/maker/material_edit_corpus_report.txt.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next;
using HS2Next.Chara;
using HS2Next.Chara.Ext;
using HS2Next.Content;
using HS2Next.Import;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json.Linq;
using UnityEngine;
using static MakerTestKit;
using Debug = UnityEngine.Debug;
using OT = HS2Next.Data.Ext.MaterialEditorData.ObjectType;

public static partial class MaterialEditTest
{
    public static void RunCorpus()
    {
        var sw = Stopwatch.StartNew();
        Directory.CreateDirectory(OutDir);
        string report = Path.Combine(OutDir, "material_edit_corpus_report.txt");
        File.WriteAllText(report, "SUMMARY: 0 PASS, 1 FAIL (the corpus row did not finish)\n", new UTF8Encoding(false));
        string my1 = null, my4 = null;
        if (Want("MY1"))
        {
            MaterialEditTest_Corpus.StayOpen = true;
            MaterialEditTest_Corpus.Run();
            MaterialEditTest_Corpus.StayOpen = false;
            my1 = FirstLine(Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "materialedit", "corpus_report.txt"));
        }
        if (Want("MY4"))
        {
            RunHairCorpus();
            my4 = FirstLine(Path.Combine(OutDir, "material_edit_hair_corpus_report.txt"));
        }
        Reset();
        if (my1 != null) Check("MY1 the import against the record", my1.Contains(" 0 FAIL") && !my1.StartsWith("SUMMARY: 0 PASS", StringComparison.Ordinal), my1);
        if (my4 != null) Check("MY4 RunHairCorpus (MJ5)", my4.Contains(" 0 FAIL") && !my4.StartsWith("SUMMARY: 0 PASS", StringComparison.Ordinal), my4);
        int limit = int.Parse(Arg("-limit", int.MaxValue.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);
        if (Want("MY2") || Want("MY3"))
        {
            var head = new StringBuilder();
            var seed = HS2Next.Motion.Face.FaceSetup.Seed;
            HS2Next.Motion.Face.FaceSetup.Seed = 12345;
            try
            {
                using (var h = MakerCoreTest.Harness.Open(head))
                {
                    if (Want("MY2")) Guard("MY2 editing one item leaves the rest", () => My2(h, limit));
                    if (Want("MY3")) Guard("MY3 what the cards' items do", () => My3(h, limit));
                }
            }
            finally { HS2Next.Motion.Face.FaceSetup.Seed = seed; }
        }
        if (Want("MY5")) Guard("MY5 the fixed pair", My5);
        int failed = Fails;
        Finish("material_edit_corpus_report.txt", "MaterialEditTest.corpus: MY1-MY5", sw, "MATERIAL EDIT TEST");
        if (failed > 0 && Application.isBatchMode) TestExit.Exit(1);
    }

    static string FirstLine(string file) => File.Exists(file) ? File.ReadLines(file).FirstOrDefault() ?? "(empty)" : "(no report)";

    // ---- MY5

    static readonly string[] QueuePair = { "9864dedc12f5", "ebd599a75147" };

    static void My5()
    {
        string dir = Path.Combine(TestTargets.UserData, "coordinate", "female");
        var byHash = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(dir, "*.png", SearchOption.AllDirectories))
        {
            string rel = path.Substring(TestTargets.UserData.Length).TrimStart('\\', '/').Replace('\\', '/');
            string hash = MaterialEditTest_Corpus.FileHash(rel);
            if (QueuePair.Contains(hash)) byHash[hash] = path.Substring(dir.Length).TrimStart('\\', '/').Replace('\\', '/');
        }
        if (byHash.Count != QueuePair.Length) { Warn("MY5", $"{byHash.Count} of {QueuePair.Length} coordinates on this install: not run"); return; }
        var a = byHash[QueuePair[0]];
        var b = byHash[QueuePair[1]];
        int seed = Passes + Fails;
        using (var h = MakerCoreTest.Harness.Open(new StringBuilder()))
            MakerFileTest.CoordGroupsFemale(h, new List<List<string>> { new List<string> { a, b }, new List<string> { b, a } });
        Check("MY5 both orders ran", Passes + Fails > seed);
    }

    // ---- MY2

    sealed class Pick
    {
        public string Type, Member;
        public MaterialEdit To, Back;
    }

    // A value that differs from the stored one in every case: a large number does not move by a fixed step.
    static float Moved(float v) => Math.Abs(v) > 1e6f ? v * 0.5f : v + 0.25f;

    static IEnumerable<Pick> Picks(List<MaterialOverride> list)
    {
        var f = list.OfType<MeFloat>().FirstOrDefault(x => x.ValueText == null);
        if (f != null)
            yield return new Pick { Type = "float", Member = "value", To = MaterialEdit.Float(f.Material, f.Property, Moved(f.Value)), Back = MaterialEdit.Float(f.Material, f.Property, f.Value) };
        var c = list.OfType<MeColor>().FirstOrDefault();
        if (c != null)
        {
            var v = c.Value;
            yield return new Pick { Type = "color", Member = "value", To = MaterialEdit.ColorOf(c.Material, c.Property, new Color4(Moved(v.R), v.G, v.B, v.A)), Back = MaterialEdit.ColorOf(c.Material, c.Property, v) };
        }
        var t = list.OfType<MeTexture>().FirstOrDefault(x => x.Offset.HasValue);
        if (t != null)
        {
            var o = t.Offset.Value;
            yield return new Pick { Type = "texture", Member = "offset", To = MaterialEdit.Texture(t.Material, t.Property).WithOffset(new Vec2(Moved(o.X), o.Y)), Back = MaterialEdit.Texture(t.Material, t.Property).WithOffset(o) };
        }
        var r = list.OfType<MeRenderer>().FirstOrDefault(x => x.ValueText == null && x.Property == MeKey.EnabledProperty && (x.Value == 0 || x.Value == 1));
        if (r != null)
            yield return new Pick { Type = "renderer", Member = "value", To = MaterialEdit.RendererEnabled(r.Renderer, 1 - r.Value), Back = MaterialEdit.RendererEnabled(r.Renderer, r.Value) };
        var s = list.OfType<MeShader>().FirstOrDefault(x => x.RenderQueue.HasValue);
        if (s != null)
        {
            int q = s.RenderQueue.Value;
            yield return new Pick { Type = "shader", Member = "renderQueue", To = MaterialEdit.ShaderItem(s.Material).WithQueue(q >= MaterialEdits.MaxQueue ? q - 1 : q + 1), Back = MaterialEdit.ShaderItem(s.Material).WithQueue(q) };
        }
    }

    static string DocumentsText(CharaSubtree s) =>
        Encoding.UTF8.GetString(CanonicalJsonWriter.ToBytes(CharaDocument.EntryName, s.Chara.Write)) + Encoding.UTF8.GetString(CanonicalJsonWriter.ToBytes(CoordDocument.EntryName, s.Coord.Write));

    static List<MaterialOverride> ArrayAt(CharaSubtree s, string path) => Arrays(s).First(x => x.path == path).list;

    static void My2(MakerCoreTest.Harness h, int limit)
    {
        var sw = Stopwatch.StartNew();
        var importer = new Hs2Importer(h.Catalog, new ImportOptions { Clock = () => new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc) });
        int files = 0, withItems = 0, arrays = 0, edited = 0, edits = 0, differences = 0, refused = 0, items = 0;
        var byType = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var bad = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var shown = new List<string>();
        void Differs(string hash, string path, Pick p, string why)
        {
            differences++;
            bad[why] = bad.TryGetValue(why, out int n) ? n + 1 : 1;
            if (shown.Count < 30) shown.Add($"{hash} {path} {p.Type}: {why}");
        }
        MaterialEditTest_Corpus.Walk(importer, limit, f =>
        {
            files++;
            var sub = f.Subtree ?? new CharaSubtree { Coord = f.Coord };
            if (!Arrays(sub).Any(x => x.list.Count > 0)) return;
            withItems++;
            var session = SceneRuntimeTest.NewSession(h.Services);
            var added = session.AddCharaFromContainer(new CharaContainer { Subtree = sub, Res = f.Res });
            if (added.Status != CommandStatus.Ok) { Check("MY2 the file takes a node: " + f.Hash, false, added.Message); return; }
            string id = session.Model.PreOrder().Where(x => x.Kind == NodeKind.Chara).Select(x => x.Id).Last();
            var stored = session.Model.Find(id).Appearance.Stored;
            foreach (var path in Arrays(stored).Where(x => x.list.Count > 0).Select(x => x.path).ToList())
            {
                arrays++;
                items += ArrayAt(stored, path).Count;
                var picks = Picks(ArrayAt(stored, path)).ToList();
                if (picks.Count > 0) edited++;
                foreach (var p in picks)
                {
                    edits++;
                    byType[p.Type] = byType.TryGetValue(p.Type, out int n) ? n + 1 : 1;
                    string before = DocumentsText(stored);
                    var old = ArrayAt(stored, path);
                    string oldText = FieldJson.ToText(old);
                    int i = MaterialEdits.LastIndex(old, p.To.Key);
                    var changed = MaterialEdits.Set(old, p.To);
                    if (ReferenceEquals(changed, old)) { Differs(f.Hash, path, p, "the value did not change"); continue; }
                    if (changed.Count != old.Count) { Differs(f.Hash, path, p, "the count changed"); continue; }
                    for (int k = 0; k < old.Count; k++)
                        if (k != i && !ReferenceEquals(old[k], changed[k])) { Differs(f.Hash, path, p, "another element is not the same object"); break; }
                    CommandResult r;
                    using (var tx = session.Commands.BeginOrJoin("Material edit"))
                    {
                        r = tx.Set(id, path, changed);
                        if (r.Status != CommandStatus.Error) r = tx.Commit();
                    }
                    if (r.Status != CommandStatus.Ok) { Differs(f.Hash, path, p, "the field path refused it: " + r.Code); continue; }
                    string after = DocumentsText(stored);
                    string why = OneLine(before, after, p.Member);
                    if (why != null) Differs(f.Hash, path, p, why);
                    why = OneMember(old[i], ArrayAt(stored, path)[i], p.Member);
                    if (why != null) Differs(f.Hash, path, p, why);
                    // back: on the edited array, and on a copy of the old one
                    if (FieldJson.ToText(MaterialEdits.Set(ArrayAt(stored, path), p.Back)) != oldText) Differs(f.Hash, path, p, "Set back on the edited array is not byte-equal");
                    if (FieldJson.ToText(MaterialEdits.Set(new List<MaterialOverride>(old), p.Back)) != oldText) Differs(f.Hash, path, p, "Set back on a copy of the old array is not byte-equal");
                    var u = session.Commands.Undo();
                    if (u.Status != CommandStatus.Ok) { Differs(f.Hash, path, p, "undo: " + u.Code); continue; }
                    if (DocumentsText(stored) != before) Differs(f.Hash, path, p, "undo is not byte-equal");
                }
            }
            if (withItems % 50 == 0) Debug.Log($"MaterialEditTest corpus: MY2 {withItems} files, {edits} edits, {sw.Elapsed.TotalSeconds:F0} s");
        }, (hash, why) => { refused++; Info("MY2 refused " + hash + " " + why); });
        foreach (var x in shown) Info("MY2 " + x);
        foreach (var kv in bad) Info($"MY2 class {kv.Key}: {kv.Value}");
        Info($"MY2 files read {files}, refused {refused}, with items {withItems}, arrays {arrays}, items {items}, arrays with an edit {edited}, edits {edits} (" +
             string.Join(", ", byType.Select(kv => kv.Key + " " + kv.Value)) + $"), differences {differences}, {sw.Elapsed.TotalSeconds:F0} s");
        Check("MY2 the documents differ in one member of one element, nothing else", differences == 0 && edits > 0, $"{edits} edits on {edited} arrays of {withItems} files, {differences} differences");
    }

    // The two texts have the same lines but one, and that one is the named member's (indented, one line per member).
    static string OneLine(string before, string after, string member)
    {
        var a = before.Split('\n');
        var b = after.Split('\n');
        if (a.Length != b.Length) return "the documents' line count changed";
        int at = -1;
        for (int k = 0; k < a.Length; k++)
        {
            if (a[k] == b[k]) continue;
            if (at >= 0) return "more than one line differs";
            at = k;
        }
        if (at < 0) return "no line differs";
        string name = "\"" + member + "\":";
        return a[at].TrimStart().StartsWith(name, StringComparison.Ordinal) && b[at].TrimStart().StartsWith(name, StringComparison.Ordinal) ? null : "the line is not the member's";
    }

    static string OneMember(MaterialOverride was, MaterialOverride now, string member)
    {
        var x = JObject.Parse(FieldJson.ToText(was));
        var y = JObject.Parse(FieldJson.ToText(now));
        if (!x.Properties().Select(q => q.Name).SequenceEqual(y.Properties().Select(q => q.Name))) return "the element's members changed or moved";
        var differ = x.Properties().Where(q => !JToken.DeepEquals(q.Value, y[q.Name])).Select(q => q.Name).ToList();
        return differ.Count == 1 && differ[0] == member ? null : "members that differ: " + string.Join(", ", differ);
    }

    // ---- MY3

    sealed class Distance
    {
        public int Compared, Differ;
        public double Largest;
        public readonly List<double> Sizes = new List<double>();
        public void Add(double d)
        {
            Compared++;
            if (d == 0) return;
            Differ++;
            Sizes.Add(d);
            if (d > Largest) Largest = d;
        }
    }

    static readonly double[] Bins = { 1e-6, 1e-4, 1e-2, 0.1, 1 };

    static string Sized(Distance d)
    {
        string text = $"{d.Compared} compared, {d.Differ} differ";
        if (d.Differ > 0) text += $", median {Median(d.Sizes):G4}, largest {d.Largest:G4}";
        return text;
    }

    static void My3(MakerCoreTest.Harness h, int limit)
    {
        var sw = Stopwatch.StartNew();
        var t = TestTargets.Load();
        var status = new SortedDictionary<string, SortedDictionary<string, int>>(StringComparer.Ordinal);
        var perProperty = new SortedDictionary<string, Distance>(StringComparer.Ordinal);
        var perType = new SortedDictionary<string, Distance>(StringComparer.Ordinal);
        var skipped = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int cards = 0, failed = 0, items = 0, noOriginal = 0, noBaseline = 0, statItems = 0, statApplied = 0, statSwaps = 0, shaderOriginals = 0;
        int onRouted = 0, onFallback = 0, onWater = 0, onUnreal = 0;
        void Note(string type, string state)
        {
            if (!status.TryGetValue(type, out var row)) status[type] = row = new SortedDictionary<string, int>(StringComparer.Ordinal);
            row[state] = row.TryGetValue(state, out int n) ? n + 1 : 1;
        }
        void Add(string type, string property, double d)
        {
            if (!perType.TryGetValue(type, out var a)) perType[type] = a = new Distance();
            a.Add(d);
            string k = type + " " + property;
            if (!perProperty.TryGetValue(k, out var b)) perProperty[k] = b = new Distance();
            b.Add(d);
        }
        foreach (var dir in new[] { TestTargets.Abs(t.cards.female), TestTargets.Abs(t.cards.male) })
            foreach (var path in ImportTest.FilesOf(dir, int.MaxValue))
            {
                if (cards >= limit) break;
                string rel = path.Substring(TestTargets.UserData.Length).TrimStart('\\', '/').Replace('\\', '/');
                string hash = MaterialEditTest_Corpus.FileHash(rel);
                var c = MakerEditTest.Import(h, path, out _);
                if (c == null || !Arrays(c.Subtree).Any(x => x.list.Count > 0)) continue;
                cards++;
                var a = MakerEditTest.NewNode(h, c, hash);
                try
                {
                    if (!a.Ok) { failed++; Check("MY3 builds: " + hash, false, a.Error ?? ""); continue; }
                    var ctx = a.Real.Context;
                    statItems += ctx.Stats.MeItems;
                    statApplied += ctx.Stats.MeApplied;
                    statSwaps += ctx.Stats.MeShaderSwaps;
                    foreach (var kv in ctx.Stats.MeSkipped) skipped[kv.Key] = (skipped.TryGetValue(kv.Key, out int n) ? n : 0) + kv.Value;
                    foreach (var target in a.Ops.MaterialTargetsOf(a.Id))
                    {
                        var list = a.Ops.MaterialArray(target);
                        if (list.Count == 0) continue;
                        var read = a.Ops.MaterialStatusRead(target, out var fail);
                        if (read == null) { Check("MY3 a status read: " + hash, false, fail?.Message ?? ""); continue; }
                        var obj = ObjectOf(target, ctx);
                        foreach (var item in list)
                        {
                            items++;
                            var state = read(item);
                            Note(item.Type, state.Note != null ? state.Status + "/" + state.Note : state.Status);
                            Originals(ctx, obj, item, Add, ref noOriginal, ref noBaseline, ref shaderOriginals);
                            if (item is MeMaterialItem mi)
                                foreach (var m in MaterialEditorApplier.MaterialsNamed(ctx, obj, mi.Material).Take(1))
                                {
                                    var from = ShaderMap.MappedFrom(m);
                                    if (ShaderMap.IsFallback(m)) onFallback++;
                                    if (from && ShaderMap.StandardRouted.Contains(from.name)) onRouted++;
                                    if (from && from.name.StartsWith("Unreal/", StringComparison.Ordinal)) onUnreal++;
                                    if (m.shader && m.shader.name == ShaderMap.WaterShader) onWater++;
                                }
                        }
                    }
                }
                finally { a.Dispose(); }
                if (cards % 10 == 0) { h.ReleaseBundles(); Debug.Log($"MaterialEditTest corpus: MY3 {cards} cards, {sw.Elapsed.TotalSeconds:F0} s"); }
            }
        var totals = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in status)
        {
            foreach (var kv in row.Value) totals[kv.Key] = (totals.TryGetValue(kv.Key, out int n) ? n : 0) + kv.Value;
            Info($"MY3 {row.Key}: {row.Value.Values.Sum()} items: " + string.Join(", ", row.Value.Select(kv => kv.Key + " " + kv.Value)));
        }
        Info($"MY3 all: {cards} cards, {items} items: " + string.Join(", ", totals.Select(kv => kv.Key + " " + kv.Value)));
        Info($"MY3 a build's statistics, summed: items {statItems}, applied {statApplied}, shader swaps {statSwaps}, skipped: " + string.Join(", ", skipped.Select(kv => kv.Key + " " + kv.Value)));
        Info($"MY3 originals: no stored original {noOriginal}, no baseline to compare {noBaseline}, shader names kept {shaderOriginals} (HS2's names, not compared)");
        Info($"MY3 items on materials the mapping treats apart: routed {onRouted}, URP/Lit fallback {onFallback}, from Unreal/ {onUnreal}, water {onWater}");
        foreach (var kv in perType)
        {
            var bins = new int[Bins.Length + 1];
            foreach (var d in kv.Value.Sizes)
            {
                int b = 0;
                while (b < Bins.Length && d > Bins[b]) b++;
                bins[b]++;
            }
            Info($"MY3 original against baseline, {kv.Key}: {Sized(kv.Value)}; sizes up to 1e-6 {bins[0]}, 1e-4 {bins[1]}, 0.01 {bins[2]}, 0.1 {bins[3]}, 1 {bins[4]}, above {bins[5]}");
        }
        foreach (var kv in perProperty.Where(x => x.Value.Differ > 0).OrderByDescending(x => x.Value.Differ).ThenBy(x => x.Key, StringComparer.Ordinal))
            Info($"MY3 {kv.Key}: {Sized(kv.Value)}");
        Info($"MY3 properties compared {perProperty.Count}, with a difference {perProperty.Count(x => x.Value.Differ > 0)}; {sw.Elapsed.TotalSeconds:F0} s");
        Check("MY3 every card with items built and read", cards > 0 && failed == 0 && items > 0, $"{cards} cards, {failed} failed, {items} items");
    }

    static (OT, int) ObjectOf(MaterialEditTarget target, CharaContext ctx)
    {
        switch (target.Kind)
        {
            case MaterialTargetKind.Hair: return (OT.Hair, target.Slot);
            case MaterialTargetKind.Clothes: return (OT.Clothing, target.Slot);
            case MaterialTargetKind.Accessory:
                return (OT.Accessory, ctx.Input?.Slots != null && ctx.Input.Slots.SlotById.TryGetValue(target.AccessoryId, out int slot) ? slot : -1);
            default: return (OT.Character, 0);
        }
    }

    delegate void AddDistance(string type, string property, double d);

    static void Originals(CharaContext ctx, (OT, int) obj, MaterialOverride item, AddDistance add, ref int noOriginal, ref int noBaseline, ref int shaderOriginals)
    {
        MaterialBaseline.Value v;
        switch (item)
        {
            case MeFloat f:
                if (!f.Original.HasValue) noOriginal++;
                else if (!MaterialEditorApplier.TryBaseline(ctx, obj, f.Material, MaterialMember.Float, "_" + f.Property, out v)) noBaseline++;
                else add("float", f.Property, Math.Abs((double)f.Original.Value - v.Float));
                break;
            case MeColor c:
                if (!c.Original.HasValue) noOriginal++;
                else if (!MaterialEditorApplier.TryBaseline(ctx, obj, c.Material, MaterialMember.Color, "_" + c.Property, out v)) noBaseline++;
                else
                {
                    var o = c.Original.Value;
                    var b = v.Color;
                    add("color", c.Property, Math.Max(Math.Max(Math.Abs((double)o.R - b.r), Math.Abs((double)o.G - b.g)), Math.Max(Math.Abs((double)o.B - b.b), Math.Abs((double)o.A - b.a))));
                }
                break;
            case MeRenderer r when r.Property == MeKey.EnabledProperty:
                if (!r.Original.HasValue) noOriginal++;
                else if (!MaterialEditorApplier.TryBaselineEnabled(ctx, obj, r.Renderer, out v)) noBaseline++;
                else add("renderer", r.Property, Math.Abs(r.Original.Value - (v.Flag ? 1 : 0)));
                break;
            case MeTexture t:
                if (t.Offset.HasValue)
                {
                    if (!t.OffsetOriginal.HasValue) noOriginal++;
                    else if (!MaterialEditorApplier.TryBaseline(ctx, obj, t.Material, MaterialMember.Offset, "_" + t.Property, out v)) noBaseline++;
                    else add("texture offset", t.Property, Math.Max(Math.Abs((double)t.OffsetOriginal.Value.X - v.Vector2.x), Math.Abs((double)t.OffsetOriginal.Value.Y - v.Vector2.y)));
                }
                if (t.Scale.HasValue)
                {
                    if (!t.ScaleOriginal.HasValue) noOriginal++;
                    else if (!MaterialEditorApplier.TryBaseline(ctx, obj, t.Material, MaterialMember.Scale, "_" + t.Property, out v)) noBaseline++;
                    else add("texture scale", t.Property, Math.Max(Math.Abs((double)t.ScaleOriginal.Value.X - v.Vector2.x), Math.Abs((double)t.ScaleOriginal.Value.Y - v.Vector2.y)));
                }
                break;
            case MeShader s:
                if (s.ShaderOriginal != null) shaderOriginals++;
                if (s.RenderQueue.HasValue)
                {
                    if (!s.RenderQueueOriginal.HasValue) noOriginal++;
                    else if (!MaterialEditorApplier.TryBaseline(ctx, obj, s.Material, MaterialMember.Queue, "", out v)) noBaseline++;
                    else add("shader queue", "renderQueue", Math.Abs(s.RenderQueueOriginal.Value - v.Queue));
                }
                break;
        }
    }
}
