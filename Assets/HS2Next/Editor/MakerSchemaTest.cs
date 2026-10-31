// The schema-driven acceptance row of the Edit tab (Phase 4.6 package E): for BR-Chan and HERO107 in an edit-mode scene
// (an AppCore test host as BridgeCoreTest's, frames pumped by the in-process bridge), every row of every tree node of
// InspectorSchemas.CharaEdit as the page shows it for that character — the gates evaluated on the character as the page
// evaluates them (InspectorGates.Holds), the lists the sex lacks dropped, the templates expanded (face / body paint p0 / p1,
// the eye side left / right, the four hair parts, the clothes kinds of the sex, every worn accessory's AccessoryRows, the
// hair bundles of each part) — is driven through the bridge: a value row gets a value that differs from the current one
// (a slider current ± 0.1 inside the typed range, a colour a fixed other colour, a toggle the opposite, a list the first
// other row of chara.edit.list, a choice the next option, a vector component one grid step, a text a suffix) through
// chara.edit.set / .item, a push row its first effective cell (.preset, .layout, .swatch), a button its command (.hairSkin,
// .hairBundle, .clothesDefaults, .accessoryDefaults, .accessoryHairColour, .accessory, chara.edit.load, chara.edit.coordinate;
// a reset button after a preparing edit, so that it has something to reset); then the history must have grown by exactly
// one entry and one undo must give both documents back as they were (chara.edit.get, canonical text). Rows that are not
// edits (read-only lines, view-state options, saves, the preview) and rows without a bridge command are listed with the
// reason. Pass = no failure and no row without a bridge path or a recorded reason. Numbers and documents only.
// Run: -executeMethod MakerSchemaTest.Run (headless; -nographics is fine). Report Spike/out/maker/maker_schema_report.txt, the
// rows Spike/out/maker/maker_schema_rows.tsv; test files under Spike/out/maker/schema/.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.App;
using HS2Next.Content;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Core;
using HS2Next.UI.Panels;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static MakerTestKit;

public static class MakerSchemaTest
{
    public const string ReportFile = "maker_schema_report.txt", RowsFile = "maker_schema_rows.tsv";
    const string Clock = "2026-10-26T00:00:00Z";

    enum Outcome { Done, Excluded, Failed }

    /// <summary>One row of a page as shown for the character: the spec, its expanded path, the section and tree node.</summary>
    sealed class Row
    {
        public string Node, Section, Path, Rel;
        public FieldSpec Spec;
        /// <summary>A hair bundle row of the HairBundleList (Bundle ≥ 0): the bundle index, "move" / "rotate" / "noShake", the
        /// component (0..2) and the bundle's effective values on the setup state.</summary>
        public int Bundle = -1, BundleComponent = -1;
        public string BundleMember;
        public JObject BundleEffective;
        public string Id => Bundle >= 0 ? Rel + "[" + Bundle + "]." + BundleMember + (BundleComponent >= 0 ? "#" + BundleComponent : "")
            : Spec.Kind == FieldKind.Button ? "button:" + Spec.Action : Spec.Kind == FieldKind.Push ? "push:" + Spec.PushSource + ":" + Rel : Rel + (Spec.Component >= 0 ? "#" + Spec.Component : "");
    }

    /// <summary>Per tree node: rows shown, done, excluded (by reason), failed, gated off, milliseconds.</summary>
    sealed class NodeStats
    {
        public int Rows, Done, Excluded, Failed, Gated, Unchanged;
        public double Ms;
        public readonly SortedDictionary<string, int> Reasons = new SortedDictionary<string, int>(StringComparer.Ordinal);
    }

    /// <summary>The character under test and the bridge host.</summary>
    sealed class Ctx
    {
        public AppCore App;
        public string Key, SexName, SourceChara, SourceCoord;
        public int Sex;
        public (string chara, string coord) Base;
        public int BaseCount;
        public readonly Dictionary<string, NodeStats> Stats = new Dictionary<string, NodeStats>(StringComparer.Ordinal);
        public readonly StringBuilder Tsv = new StringBuilder();
        public readonly List<string> LabelMismatch = new List<string>();
        public string LastText;
        public int Calls;

        public JObject Call(object request)
        {
            Calls++;
            LastText = App.Call(request is string s ? s : JsonConvert.SerializeObject(request));
            return JObject.Parse(LastText);
        }

        public bool Ok(JObject r) => r.Value<bool?>("ok") == true;

        public int UndoCount() => (Call(new { cmd = "history" })["undo"] as JArray)?.Count ?? -1;
        public string TopLabel() => (string)(Call(new { cmd = "history" })["undo"] as JArray)?.FirstOrDefault();

        public (string chara, string coord) Docs()
        {
            var v = Call(new { cmd = "chara.edit.get", key = Key })["values"];
            return (v?["chara"]?.ToString(Formatting.None), v?["coord"]?.ToString(Formatting.None));
        }

        public JToken Get(string rel) => Call(new { cmd = "chara.edit.get", key = Key, paths = new[] { rel } })["values"]?[rel];

        /// <summary>Takes the current state as the baseline (after a failure, so that later rows are judged on their own).</summary>
        public void Rebase()
        {
            Base = Docs();
            BaseCount = UndoCount();
        }
    }

    static string Rel(string full) => AppearancePaths.ToRelative(full) ?? full;

    // ================================================================ run

    public static void Run()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        var head = new StringBuilder();
        RuntimeServices services = null;
        AppCore app = null;
        var ctxs = new List<Ctx>();
        string dir = Path.Combine(OutDir, "schema");
        try
        {
            services = SceneRuntimeTest.NewServices(head);
            Rep.Append(head);
            var targets = TestTargets.Load();
            var s = AppSettings.Defaults(Array.Empty<string>());
            s.Content = CardResolveTest.Settings;
            s.UserData = Path.Combine(dir, "userdata");
            s.Bridge.OutputDir = Path.Combine(dir, "out");
            var o = services.NewSessionOptions();
            o.Ids = new CountingIdSource(0x5c4e3a7e57UL);
            app = AppCore.ForTests(services, s, null, o);
            Guard("setup", () =>
            {
                var h = new Ctx { App = app };
                Check("scene.new", h.Ok(h.Call(new { cmd = "scene.new" })), Short(h.LastText));
                foreach (var (card, sex, name) in new[] { (TestTargets.Abs(targets.visual.female), 1, "female"), (TestTargets.Abs(targets.visual.male), 0, "male") })
                {
                    var r = h.Call(new { cmd = "chara.load", path = card, sex = name });
                    string key = (string)r["added"]?[0]?["key"];
                    bool built = (string)r["added"]?[0]?["state"] == "Built";
                    if (!Check("chara.load " + Path.GetFileName(card) + " (built)", h.Ok(r) && key != null && built, Short(h.LastText))) continue;
                    h.Call(new { cmd = "chara.motion", key, paused = true, physics = false });
                    ctxs.Add(new Ctx { App = app, Key = key, Sex = sex, SexName = name });
                }
            });
            foreach (var x in ctxs) Guard("character " + x.SexName, () => RunCharacter(x));
            Guard("summary (VU6)", () => Summary(ctxs, sw));
        }
        catch (Exception e) { Check("run: no exception", false, e.GetType().Name + ": " + e.Message + " " + Short(e.StackTrace, 1200)); }
        finally
        {
            try { app?.Dispose(); } catch (Exception e) { Info("host dispose threw: " + e.Message); }
            services?.Catalog?.UnloadAll();
            try
            {
                Directory.CreateDirectory(OutDir);
                var tsv = new StringBuilder("character\tnode\tsection\trow\tkind\toutcome\tdetail\n");
                foreach (var x in ctxs) tsv.Append(x.Tsv);
                File.WriteAllText(Path.Combine(OutDir, RowsFile), tsv.ToString(), new UTF8Encoding(false));
            }
            catch (Exception e) { Info("rows file not written: " + e.Message); }
        }
        Finish(ReportFile, "MakerSchemaTest: every Edit-tab row through the bridge = one history entry, undo = the documents before (BR-Chan, HERO107)", sw, "MAKER SCHEMA TEST");
    }

    static void Summary(List<Ctx> ctxs, Stopwatch sw)
    {
        foreach (var x in ctxs)
        {
            Info("== " + x.SexName + " (" + x.Calls + " bridge calls)");
            Info(string.Format(CultureInfo.InvariantCulture, "{0,-12} {1,6} {2,6} {3,8} {4,6} {5,7} {6,9} {7,9}", "node", "rows", "done", "excluded", "failed", "gated", "unchanged", "seconds"));
            foreach (var node in InspectorSchemas.CharaEditTree)
            {
                if (!x.Stats.TryGetValue(node, out var st)) continue;
                Info(string.Format(CultureInfo.InvariantCulture, "{0,-12} {1,6} {2,6} {3,8} {4,6} {5,7} {6,9} {7,9:F1}", node, st.Rows, st.Done, st.Excluded, st.Failed, st.Gated, st.Unchanged, st.Ms / 1000));
                foreach (var kv in st.Reasons) Info("      excluded " + kv.Value + " x: " + kv.Key);
                Check(x.SexName + " " + node + ": " + st.Done + " of " + st.Rows + " rows driven, " + st.Excluded + " excluded with a reason, 0 failed",
                    st.Failed == 0 && st.Done + st.Excluded == st.Rows, st.Failed + " failed");
            }
            if (x.LabelMismatch.Count > 0)
                Info("history labels other than the row's label (" + x.LabelMismatch.Count + ", first 15): " + string.Join("; ", x.LabelMismatch.Take(15)));
        }
        Info("VU6: " + sw.Elapsed.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture) + " s in all (expected 3-6 min)");
    }

    // ================================================================ one character

    static void RunCharacter(Ctx x)
    {
        var ops = StudioOps.For(x.App.Runtime);
        var node = x.App.Session.Model.Find(x.Key);
        // the File node's sources (not judged): the character changed in every section saved as a .hs2nchara; its coordinate
        // with a changed top, an added accessory and another name saved as a .hs2ncoord; every preparing edit undone
        x.Rebase();
        PrepareSources(x);
        x.Rebase();
        Info(x.SexName + ": sources " + Path.GetFileName(x.SourceChara) + ", " + Path.GetFileName(x.SourceCoord) + "; history " + x.BaseCount);
        foreach (var tree in InspectorSchemas.CharaEditTree)
        {
            var st = x.Stats[tree] = new NodeStats();
            var t0 = Stopwatch.StartNew();
            // the node's rows as the page shows them on the setup state (gates evaluated before any row is driven)
            var rows = RowsOf(ops, node, x, tree, st).ToList();
            foreach (var row in rows)
            {
                st.Rows++;
                string detail;
                var outcome = Drive(x, ops, row, out detail);
                if (outcome == Outcome.Done) { st.Done++; if (detail == "unchanged") st.Unchanged++; }
                else if (outcome == Outcome.Excluded) { st.Excluded++; st.Reasons.TryGetValue(detail, out int k); st.Reasons[detail] = k + 1; }
                else
                {
                    st.Failed++;
                    Check(x.SexName + " " + row.Section + " " + row.Id + " (" + row.Spec.Kind + ")", false, detail);
                    x.Rebase();
                }
                x.Tsv.Append(x.SexName).Append('\t').Append(tree).Append('\t').Append(row.Section).Append('\t').Append(row.Id).Append('\t').Append(row.Spec.Kind)
                    .Append('\t').Append(outcome).Append('\t').Append((detail ?? "").Replace('\t', ' ').Replace('\n', ' ')).Append('\n');
            }
            st.Ms = t0.Elapsed.TotalMilliseconds;
        }
        var end = x.Docs();
        Check(x.SexName + ": after every row the documents are the setup's", end.Equals(x.Base));
    }

    static void PrepareSources(Ctx x)
    {
        int n0 = x.UndoCount();
        var set = new Dictionary<string, object>
        {
            ["chara.face.shape[3]"] = 0.83, ["chara.body.shape[1]"] = 0.77, ["chara.hair.back.baseColor"] = new[] { 0.2, 0.4, 0.6, 1.0 },
            ["coord.clothes.top.colors[0].base"] = new[] { 0.15, 0.25, 0.35, 1.0 }, ["chara.profile.name"] = "Schema Source",
        };
        Check(x.SexName + ": source edit", x.Ok(x.Call(new { cmd = "chara.edit.set", key = x.Key, set })), Short(x.LastText));
        var r = x.Call(new { cmd = "chara.save", key = x.Key, path = "schema/" + x.SexName + "_source", overwrite = true, thumb = "placeholder", clock = Clock });
        Check(x.SexName + ": source saved", x.Ok(r), Short(x.LastText));
        x.SourceChara = (string)r["path"];
        while (x.UndoCount() > n0) x.Call(new { cmd = "undo" });
        var a = x.Call(new { cmd = "chara.edit.accessory", key = x.Key, op = "add", category = 353, @ref = "illusion.hs2#0" });
        Check(x.SexName + ": source accessory added", x.Ok(a), Short(x.LastText));
        x.Call(new { cmd = "chara.edit.set", key = x.Key, set = new Dictionary<string, object> { ["coord.clothes.top.colors[0].base"] = new[] { 0.65, 0.15, 0.25, 1.0 } } });
        var cr = x.Call(new { cmd = "coord.save", key = x.Key, path = "schema/" + x.SexName + "_coord", coordName = "Schema Coord", overwrite = true, thumb = "placeholder", clock = Clock });
        Check(x.SexName + ": source coordinate saved", x.Ok(cr), Short(x.LastText));
        x.SourceCoord = (string)cr["path"];
        while (x.UndoCount() > n0) x.Call(new { cmd = "undo" });
        Check(x.SexName + ": the preparing edits are undone", x.Docs().Equals(x.Base));
    }

    // ================================================================ enumeration

    /// <summary>The rows of tree node <paramref name="tree"/> as the page shows them for the character (gates evaluated on it).</summary>
    static IEnumerable<Row> RowsOf(StudioOps ops, TreeNode n, Ctx x, string tree, NodeStats st)
    {
        var sections = InspectorSchemas.CharaEdit.Where(s => InspectorSchemas.CharaEditTreeOf(s) == tree).ToList();
        if (tree == "accessories")
        {
            // the page-level sections (slot list, copy section), then the slot groups of the worn accessories in slot order
            foreach (var s in sections)
                foreach (var r in SectionRows(ops, x, tree, s, null, s.Id, st)) yield return r;
            var table = StudioOps.AccessorySlotsOf(n);
            if (table != null)
                foreach (var kv in table.EntryBySlot)
                    foreach (var s in InspectorSchemas.AccessoryRows(table.IdBySlot[kv.Key], kv.Key, kv.Value.Category))
                        foreach (var r in SectionRows(ops, x, tree, s, null, s.Id, st)) yield return r;
            yield break;
        }
        foreach (var s in sections)
        {
            if (tree == "clothes")
            {
                int kind = InspectorSchemas.ClothesKindOf(s.Id);
                if (kind >= 0 && !InspectorSchemas.ClothesKindShown(kind, x.Sex)) continue;
            }
            if (s.IsRepeated)
            {
                foreach (var inst in s.Instances)
                    foreach (var r in SectionRows(ops, x, tree, s, inst.Value, inst.Id, st)) yield return r;
                continue;
            }
            bool sided = s.Fields.Any(f => f.Placeholder == InspectorSchemas.SidePlaceholder);
            if (!sided)
            {
                foreach (var r in SectionRows(ops, x, tree, s, null, s.Id, st)) yield return r;
                continue;
            }
            // the eye side selector: the side's rows for left and right, the other rows once
            bool first = true;
            foreach (var side in InspectorSchemas.EyeSides)
            {
                foreach (var r in SectionRows(ops, x, tree, s, side, s.Id, st, sidesOnly: !first)) yield return r;
                first = false;
            }
        }
    }

    static IEnumerable<Row> SectionRows(StudioOps ops, Ctx x, string tree, SchemaSection s, string instance, string sectionId, NodeStats st, bool sidesOnly = false)
    {
        foreach (var f in s.Fields)
        {
            bool sideRow = f.Placeholder == InspectorSchemas.SidePlaceholder;
            if (sidesOnly && !sideRow) continue;
            if (!InspectorGates.Holds(ops, f.Gate, x.Key)) { st.Gated++; continue; }
            if (f.Kind == FieldKind.PartList && f.CategoryFor(x.Sex) < 0) { st.Gated++; continue; }
            if (f.IsTemplate && instance == null) { st.Gated++; continue; }
            string path = f.IsTemplate ? f.Expand(instance) : f.Path;
            string rel = path == null ? null : Rel(path);
            if (f.Kind == FieldKind.HairBundles)
            {
                // the HairBundleList's rows: per shown bundle of the part the three move and three rotate sliders and «揺らさない»
                bool any = false;
                foreach (var b in HairBundlesOf(x, HairPartOf(rel)))
                {
                    int index = (int?)b["index"] ?? -1;
                    var eff = b["effective"] as JObject;
                    if (index < 0 || eff == null) continue;
                    any = true;
                    foreach (var member in new[] { "move", "rotate" })
                        for (int c = 0; c < 3; c++)
                            yield return new Row { Node = tree, Section = sectionId, Path = path, Rel = rel, Spec = f, Bundle = index, BundleMember = member, BundleComponent = c, BundleEffective = eff };
                    yield return new Row { Node = tree, Section = sectionId, Path = path, Rel = rel, Spec = f, Bundle = index, BundleMember = "noShake", BundleEffective = eff };
                }
                if (any) continue;
            }
            yield return new Row { Node = tree, Section = sectionId, Path = path, Rel = rel, Spec = f };
        }
    }

    /// <summary>The bundles the HairBundleList shows for a hair part (chara.edit.hair: those marked shown).</summary>
    static IEnumerable<JObject> HairBundlesOf(Ctx x, string part)
    {
        if (part == null) yield break;
        var r = x.Call(new { cmd = "chara.edit.hair", key = x.Key, part });
        var bundles = r["parts"]?[0]?["bundles"] as JArray;
        if (bundles == null) yield break;
        foreach (var b in bundles.OfType<JObject>())
            if ((bool?)b["shown"] != false) yield return b;
    }

    // ================================================================ driving one row

    static Outcome Drive(Ctx x, StudioOps ops, Row row, out string detail)
    {
        var f = row.Spec;
        detail = null;
        if (f.ReadOnly) { detail = "read-only line (no input element, never written)"; return Outcome.Excluded; }
        if (f.Target == FieldTarget.Option) { detail = "view-state option " + f.Path + " (not an edit; no history)"; return Outcome.Excluded; }
        if (f.Target != FieldTarget.Node) { detail = "not a node row (" + f.Target + ")"; return Outcome.Failed; }
        switch (f.Kind)
        {
            case FieldKind.Button: return Button(x, ops, row, out detail);
            case FieldKind.PartList: return PartList(x, row, out detail);
            case FieldKind.Push: return Push(x, row, out detail);
            case FieldKind.HairBundles: return HairBundles(x, row, out detail);
            case FieldKind.AttachPoint: return AttachPoint(x, row, out detail);
            case FieldKind.Float:
            case FieldKind.Int:
            case FieldKind.Bool:
            case FieldKind.Color:
            case FieldKind.ColorHdr:
            case FieldKind.Vec2:
            case FieldKind.Vec3:
            case FieldKind.Enum:
            case FieldKind.Text:
                return Value(x, row, out detail);
            default:
                detail = "no bridge path for a row of kind " + f.Kind;
                return Outcome.Failed;
        }
    }

    /// <summary>
    /// Sends <paramref name="request"/> (one Maker edit) and judges it: ok, exactly one new history entry, then one undo gives the
    /// documents back as <paramref name="before"/>. Returns null when it passed, "noentry" when the call was ok but added no entry
    /// (the caller may try another value), else the failure text.
    /// </summary>
    static string EditUndo(Ctx x, object request, (string chara, string coord) before, int count, string label, out bool changed)
    {
        changed = false;
        var r = x.Call(request);
        if (!x.Ok(r)) return "refused: " + Short(x.LastText, 300);
        int n1 = x.UndoCount();
        if (n1 == count) return "noentry";
        string top = x.TopLabel();
        var after = x.Docs();
        changed = !after.Equals(before);
        if (n1 != count + 1)
        {
            while (x.UndoCount() > count) x.Call(new { cmd = "undo" });
            return "history " + count + " -> " + n1 + " (want one entry)";
        }
        if (label != null && top != label && x.LabelMismatch.Count < 200) x.LabelMismatch.Add(label + " -> \"" + top + "\"");
        if (!x.Ok(x.Call(new { cmd = "undo" }))) return "undo refused: " + Short(x.LastText, 300);
        x.App.Runtime.ApplyNow();
        var back = x.Docs();
        if (!back.Equals(before))
            return "undo did not restore the documents (" + (back.chara != before.chara ? "chara" : "") + (back.coord != before.coord ? " coord" : "") + ")";
        if (x.UndoCount() != count) return "undo left the history at another count";
        return null;
    }

    /// <summary>Tries the candidate requests in order until one adds an entry; judges that one (see <see cref="EditUndo"/>).</summary>
    static Outcome Try(Ctx x, Row row, IEnumerable<object> candidates, out string detail)
    {
        int tries = 0;
        foreach (var req in candidates)
        {
            if (req == null) continue;
            tries++;
            string e = EditUndo(x, req, x.Base, x.BaseCount, row.Spec.OpLabel, out bool changed);
            if (e == "noentry") { if (tries >= 6) break; continue; }
            if (e != null) { detail = e + " [" + Short(JsonConvert.SerializeObject(req), 200) + "]"; return Outcome.Failed; }
            detail = changed ? null : "unchanged";
            return Outcome.Done;
        }
        detail = tries == 0 ? "no candidate value" : "no candidate added a history entry (" + tries + " tried)";
        return Outcome.Failed;
    }

    /// <summary>A button whose action resets or derives a value: a preparing edit first (one entry, kept), then the action must
    /// add exactly one entry whose undo gives the prepared documents back; then the preparing edit is undone.</summary>
    static Outcome Prepared(Ctx x, Row row, object prepare, object action, out string detail)
    {
        var p = x.Call(prepare);
        if (!x.Ok(p)) { detail = "preparing edit refused: " + Short(x.LastText, 300); return Outcome.Failed; }
        int n1 = x.UndoCount();
        if (n1 != x.BaseCount + 1) { detail = "preparing edit: history " + x.BaseCount + " -> " + n1; return Outcome.Failed; }
        var mid = x.Docs();
        string e = EditUndo(x, action, mid, n1, row.Spec.OpLabel, out bool changed);
        x.Call(new { cmd = "undo" });
        x.App.Runtime.ApplyNow();
        bool back = x.Docs().Equals(x.Base) && x.UndoCount() == x.BaseCount;
        if (e == "noentry") { detail = "the action added no history entry after the preparing edit [" + Short(JsonConvert.SerializeObject(action), 200) + "]"; return Outcome.Failed; }
        if (e != null) { detail = e + " [" + Short(JsonConvert.SerializeObject(action), 200) + "]"; return Outcome.Failed; }
        if (!back) { detail = "undoing the preparing edit did not restore the setup documents"; return Outcome.Failed; }
        detail = changed ? null : "unchanged";
        return Outcome.Done;
    }

    /// <summary>Several sub-steps of one row (the load flags, the coordinate modes); every one must pass.</summary>
    static Outcome Steps(Ctx x, Row row, IEnumerable<(string what, object request)> steps, out string detail)
    {
        var bad = new List<string>();
        int n = 0;
        foreach (var (what, req) in steps)
        {
            n++;
            string e = EditUndo(x, req, x.Base, x.BaseCount, row.Spec.OpLabel, out _);
            if (e == "noentry") e = "no history entry";
            if (e != null) { bad.Add(what + ": " + e); x.Rebase(); }
        }
        detail = bad.Count == 0 ? null : string.Join("; ", bad);
        return bad.Count == 0 ? Outcome.Done : Outcome.Failed;
    }

    static object Set(Ctx x, string rel, object value) =>
        new { cmd = "chara.edit.set", key = x.Key, set = new Dictionary<string, object> { [rel] = value }, rebuild = true };

    // ---------------------------------------------------------------- value rows (chara.edit.set; an accessory's type: chara.edit.item)

    static Outcome Value(Ctx x, Row row, out string detail)
    {
        var f = row.Spec;
        var cur = x.Get(row.Rel);
        if (cur == null) { detail = "chara.edit.get gave no value for " + row.Rel + ": " + Short(x.LastText, 200); return Outcome.Failed; }
        if (f.Kind == FieldKind.Enum && row.Rel.EndsWith(".category", StringComparison.Ordinal) && row.Rel.StartsWith("coord.accessories[", StringComparison.Ordinal))
        {
            // the accessory type: the item list of another category (350 = remove is not tried: it removes the slot group)
            int c0 = cur.Type == JTokenType.Integer ? (int)cur : -1;
            var types = (f.ChoiceValues ?? Enumerable.Range(0, f.Choices?.Count ?? 0).ToList()).Where(v => v != c0 && v != InspectorSchemas.AccessoryCategoryNone);
            return Try(x, row, types.Select(t => (object)new { cmd = "chara.edit.item", key = x.Key, path = row.Rel, @ref = t }), out detail);
        }
        return Try(x, row, Candidates(f, cur).Select(v => Set(x, row.Rel, v)), out detail);
    }

    /// <summary>Values that differ from <paramref name="cur"/> for the row's kind (the first is normally taken).</summary>
    static IEnumerable<object> Candidates(FieldSpec f, JToken cur)
    {
        switch (f.Kind)
        {
            case FieldKind.Bool:
                if (cur.Type == JTokenType.Boolean) yield return !(bool)cur;
                yield break;
            case FieldKind.Text:
                yield return ((string)cur ?? "") + " x";
                yield break;
            case FieldKind.Enum:
            {
                int n = f.Choices?.Count ?? 0;
                if (cur.Type == JTokenType.String)
                {
                    int i = f.Choices?.ToList().IndexOf((string)cur) ?? -1;
                    for (int k = 1; k <= n; k++) { string v = f.Choices[(Math.Max(i, 0) + k) % n]; if (v != (string)cur) yield return v; }
                }
                else
                {
                    var values = f.ChoiceValues ?? Enumerable.Range(0, n).ToList();
                    int c = cur.Type == JTokenType.Integer ? (int)cur : int.MinValue;
                    int i = values.ToList().IndexOf(c);
                    for (int k = 1; k <= values.Count; k++) { int v = values[(Math.Max(i, 0) + k) % values.Count]; if (v != c) yield return v; }
                }
                yield break;
            }
            case FieldKind.Float:
            case FieldKind.Int:
                if (f.Component >= 0)
                {
                    if (!(cur is JArray a) || f.Component >= a.Count) yield break;
                    foreach (double v in Bumps(f, (double)a[f.Component])) { var b = (JArray)a.DeepClone(); b[f.Component] = v; yield return b; }
                }
                else if (cur.Type == JTokenType.Float || cur.Type == JTokenType.Integer)
                    foreach (double v in Bumps(f, (double)cur)) yield return f.Kind == FieldKind.Int ? (object)(long)Math.Round(v) : v;
                yield break;
            case FieldKind.Color:
            case FieldKind.ColorHdr:
            {
                if (!(cur is JArray a) || a.Count < 3) yield break;
                double alpha = a.Count > 3 ? (double)a[3] : 1;
                if (f.Component >= 0)
                {
                    foreach (double v in Bumps(f, (double)a[f.Component])) { var b = (JArray)a.DeepClone(); b[f.Component] = v; yield return b; }
                    yield break;
                }
                foreach (var c in new[] { new[] { 0.123, 0.456, 0.789 }, new[] { 0.789, 0.321, 0.654 } })
                    if (Math.Abs((double)a[0] - c[0]) > 1e-6) yield return new[] { c[0], c[1], c[2], alpha };
                yield break;
            }
            case FieldKind.Vec2:
            case FieldKind.Vec3:
            {
                if (!(cur is JArray a) || a.Count == 0) yield break;
                int k = f.Component >= 0 ? f.Component : 0;
                if (k >= a.Count) yield break;
                foreach (double v in Bumps(f, (double)a[k])) { var b = (JArray)a.DeepClone(); b[k] = v; yield return b; }
                yield break;
            }
        }
    }

    /// <summary>A value one step away from <paramref name="v"/> inside the row's typed range (± 0.1 for a Maker slider, ± 1 for
    /// a whole row, one grid step for a grid row; a 360° row wraps), then the other direction.</summary>
    static IEnumerable<double> Bumps(FieldSpec f, double v)
    {
        double step = f.Whole || f.Kind == FieldKind.Int ? 1 : f.Grid ?? 0.1;
        double hi = f.TypedMax ?? f.Max ?? double.MaxValue, lo = f.TypedMin ?? f.Min ?? double.MinValue;
        foreach (double s in new[] { step, -step })
        {
            double w = v + s;
            if (f.Wrap360) { w %= 360; if (w < 0) w += 360; yield return Math.Round(w, 6); continue; }
            if (w > hi || w < lo) continue;
            yield return Math.Round(w, 6);
        }
    }

    // ---------------------------------------------------------------- lists, pushes, bundles, attach points

    static Outcome PartList(Ctx x, Row row, out string detail)
    {
        var cur = x.Get(row.Rel);
        string current = cur?.Type == JTokenType.String ? (string)cur : null;
        var l = x.Call(new { cmd = "chara.edit.list", key = x.Key, path = row.Rel, limit = 10000 });
        if (!x.Ok(l)) { detail = "chara.edit.list refused: " + Short(x.LastText, 300); return Outcome.Failed; }
        var refs = (l["rows"] as JArray ?? new JArray()).OfType<JObject>().Where(r => (bool?)r["gated"] != true).Select(r => (string)r["ref"]).Where(r => r != null && r != current).ToList();
        // vanilla rows first (the targets' items are vanilla or installed mods; a vanilla row always loads)
        var order = refs.Where(r => r.StartsWith("illusion.hs2#", StringComparison.Ordinal)).Concat(refs.Where(r => !r.StartsWith("illusion.hs2#", StringComparison.Ordinal)));
        return Try(x, row, order.Select(r => (object)new { cmd = "chara.edit.item", key = x.Key, path = row.Rel, @ref = r }), out detail);
    }

    static Outcome Push(Ctx x, Row row, out string detail)
    {
        var f = row.Spec;
        string src = f.PushSource ?? "";
        if (src == PushSources.FacePreset)
        {
            var l = x.Call(new { cmd = "chara.edit.list", key = x.Key, path = "chara.face.head", limit = 10000 });
            var heads = (l["rows"] as JArray ?? new JArray()).OfType<JObject>().Where(r => (bool?)r["preset"] == true && (bool?)r["gated"] != true).Select(r => (string)r["ref"]).ToList();
            return Try(x, row, heads.Select(h => (object)new { cmd = "chara.edit.preset", key = x.Key, head = h }), out detail);
        }
        int lc = PushSources.LayoutCategory(src);
        if (lc >= 0)
        {
            var l = x.Call(new { cmd = "chara.edit.list", category = lc, sex = x.Sex, limit = 10000 });
            var refs = (l["rows"] as JArray ?? new JArray()).OfType<JObject>().Select(r => (string)r["ref"]).Where(r => r != null).ToList();
            return Try(x, row, refs.Select(r => (object)new { cmd = "chara.edit.layout", key = x.Key, path = row.Rel, @ref = r }), out detail);
        }
        int cc = PushSources.ColourCategory(src);
        if (cc >= 0)
        {
            int n = PushSources.ColourPresetIds(cc);
            return Try(x, row, Enumerable.Range(0, n < 0 ? 7 : n).Select(i => (object)new { cmd = "chara.edit.swatch", key = x.Key, path = row.Rel, @ref = "illusion.hs2#" + i }), out detail);
        }
        detail = "no bridge path for push source " + src;
        return Outcome.Failed;
    }

    static string HairPartOf(string rel)
    {
        // chara.hair.<part>.…
        var p = rel.Split('.');
        return p.Length >= 3 && p[0] == "chara" && p[1] == "hair" ? p[2] : null;
    }

    static Outcome HairBundles(Ctx x, Row row, out string detail)
    {
        string part = HairPartOf(row.Rel);
        if (part == null) { detail = "no hair part in " + row.Rel; return Outcome.Failed; }
        if (row.Bundle >= 0)
        {
            var eff = row.BundleEffective;
            if (row.BundleMember == "noShake")
            {
                bool ns = (bool?)eff["noShake"] ?? false;
                return Try(x, row, new object[] { new { cmd = "chara.edit.hairBundle", key = x.Key, part, index = row.Bundle, noShake = !ns } }, out detail);
            }
            var v = eff[row.BundleMember] as JArray;
            if (v == null || v.Count < 3) { detail = "no effective " + row.BundleMember + " for bundle " + row.Bundle; return Outcome.Failed; }
            double cur = (double)v[row.BundleComponent];
            var cands = new List<object>();
            foreach (double d in new[] { 0.1, -0.1, 0.05 - cur })
            {
                double w = Math.Round(cur + d, 6);
                if (w < 0 || w > 1 || w == cur) continue;
                var arr = new[] { (double)v[0], (double)v[1], (double)v[2] };
                arr[row.BundleComponent] = w;
                cands.Add(row.BundleMember == "move"
                    ? (object)new { cmd = "chara.edit.hairBundle", key = x.Key, part, index = row.Bundle, move = arr }
                    : new { cmd = "chara.edit.hairBundle", key = x.Key, part, index = row.Bundle, rotate = arr });
            }
            return Try(x, row, cands, out detail);
        }
        return Try(x, row, new object[]
        {
            new { cmd = "chara.edit.hairBundle", key = x.Key, part, index = 0, move = new[] { 0.123, 0.456, 0.789 } },
            new { cmd = "chara.edit.hairBundle", key = x.Key, part, index = 0, rotate = new[] { 0.789, 0.321, 0.654 } },
        }, out detail);
    }

    static readonly string[] OtherParents = { "N_Head_top", "N_Neck", "N_Chest_f", "N_Hand_L" };

    static Outcome AttachPoint(Ctx x, Row row, out string detail)
    {
        string cur = (string)x.Get(row.Rel);
        return Try(x, row, OtherParents.Where(p => p != cur).Select(p => Set(x, row.Rel, p)), out detail);
    }

    // ---------------------------------------------------------------- buttons

    static Outcome Button(Ctx x, StudioOps ops, Row row, out string detail)
    {
        string a = row.Spec.Action ?? "";
        detail = null;
        if (a == InspectorSchemas.ActionSaveCharacter || a == InspectorSchemas.ActionSaveCoordinate) { detail = "file save (not an edit; never recorded)"; return Outcome.Excluded; }
        if (a == InspectorSchemas.ActionRetakeThumbnail) { detail = "thumbnail preview (not an edit)"; return Outcome.Excluded; }
        if (a == InspectorSchemas.ActionCopyEyeLeftToRight || a == InspectorSchemas.ActionCopyEyeRightToLeft) { detail = "eye copy «左右コピー»: a UI op without a bridge command (StudioOps.CopyEye)"; return Outcome.Excluded; }
        if (a == InspectorSchemas.ActionLoadCharacter)
        {
            var steps = new List<(string, object)> { ("all five", new { cmd = "chara.edit.load", key = x.Key, path = x.SourceChara, flags = "all" }) };
            foreach (var flag in LoadFlagNames.Names) steps.Add((flag, new { cmd = "chara.edit.load", key = x.Key, path = x.SourceChara, flags = new[] { flag } }));
            return Steps(x, row, steps, out detail);
        }
        if (a == InspectorSchemas.ActionApplyCoordinate)
            return Steps(x, row, InspectorSchemas.CoordinateModes.Select(m => (m, (object)new { cmd = "chara.edit.coordinate", key = x.Key, file = x.SourceCoord, mode = m })), out detail);
        int part = InspectorSchemas.HairPartOfAction(a, InspectorSchemas.ActionMatchHairToSkinPrefix);
        if (part >= 0)
        {
            string pn = InspectorSchemas.HairParts[part];
            return Prepared(x, row, Set(x, "chara.hair." + pn + ".underColor", new[] { 0.9, 0.05, 0.05, 1.0 }), new { cmd = "chara.edit.hairSkin", key = x.Key, part = pn }, out detail);
        }
        part = InspectorSchemas.HairPartOfAction(a, InspectorSchemas.ActionResetHairBundlesPrefix);
        if (part >= 0)
        {
            string pn = InspectorSchemas.HairParts[part];
            return Prepared(x, row, new { cmd = "chara.edit.hairBundle", key = x.Key, part = pn, index = 0, move = new[] { 0.123, 0.456, 0.789 } },
                new { cmd = "chara.edit.hairBundle", key = x.Key, part = pn, all = true, reset = true }, out detail);
        }
        int kind = InspectorSchemas.ClothesKindOfAction(a);
        if (kind >= 0)
        {
            string kn = InspectorSchemas.ClothesEditKinds[kind];
            var set = new Dictionary<string, object>();
            for (int k = 0; k < 3; k++) set["coord.clothes." + kn + ".colors[" + k + "].base"] = new[] { 0.9, 0.05, 0.05, 1.0 };
            return Prepared(x, row, new { cmd = "chara.edit.set", key = x.Key, set, rebuild = true }, new { cmd = "chara.edit.clothesDefaults", key = x.Key, kind = kn }, out detail);
        }
        string acc = InspectorSchemas.AccessoryIdOfAction(a, InspectorSchemas.ActionResetAccessoryColoursPrefix, out _);
        if (acc != null)
        {
            var set = new Dictionary<string, object>();
            for (int k = 0; k < 4; k++) set[InspectorSchemas.AccessoryEditPath(acc, "colors[" + k + "].color").Substring("appearance.".Length)] = new[] { 0.9, 0.05, 0.05, 1.0 };
            return Prepared(x, row, new { cmd = "chara.edit.set", key = x.Key, set, rebuild = true }, new { cmd = "chara.edit.accessoryDefaults", key = x.Key, accessory = acc }, out detail);
        }
        acc = InspectorSchemas.AccessoryIdOfAction(a, InspectorSchemas.ActionMatchAccessoryHairColourPrefix, out int hp);
        if (acc != null)
            return Prepared(x, row, Set(x, AccRel(acc, "colors[0].color"), new[] { 0.9, 0.05, 0.05, 1.0 }),
                new { cmd = "chara.edit.accessoryHairColour", key = x.Key, accessory = acc, part = InspectorSchemas.HairParts[hp] }, out detail);
        acc = InspectorSchemas.AccessoryIdOfAction(a, InspectorSchemas.ActionResetAccessoryParentPrefix, out _);
        if (acc != null)
        {
            // «初期親» writes the row's default parent (MakerEdits.AccessoryDefaultParent): chara.edit.set of that value after a move
            var e = x.App.Session.Model.Find(x.Key)?.Appearance?.Stored?.Coord?.FindAccessory(acc);
            if (e == null) { detail = "no accessory " + acc; return Outcome.Failed; }
            string def = MakerEdits.AccessoryDefaultParent(MakerServices.Rows, e.Category, e.Item);
            string other = OtherParents.First(p => p != def && p != e.Parent);
            return Prepared(x, row, Set(x, AccRel(acc, "parent"), other), Set(x, AccRel(acc, "parent"), def ?? ""), out detail);
        }
        acc = InspectorSchemas.AccessoryIdOfAction(a, InspectorSchemas.ActionResetAccessoryMovePrefix, out int set1);
        if (acc != null)
        {
            // «初期化» of a transform set: the whole set ← position 0, rotation 0, scale 1 (as the page writes it)
            string m = "moves[" + (set1 - 1) + "]";
            return Prepared(x, row, Set(x, AccRel(acc, m + ".position"), new[] { 1.1, 2.2, 3.3 }),
                Set(x, AccRel(acc, m), new Dictionary<string, object> { ["position"] = new[] { 0.0, 0.0, 0.0 }, ["rotation"] = new[] { 0.0, 0.0, 0.0 }, ["scale"] = new[] { 1.0, 1.0, 1.0 } }), out detail);
        }
        var copy = InspectorSchemas.AccessoryCopyButtons.FirstOrDefault(b => b.action == a);
        if (copy.action != null) return Copy(x, ops, row, copy, out detail);
        detail = "no bridge path for action " + a;
        return Outcome.Failed;
    }

    static string AccRel(string acc, string member) => Rel(InspectorSchemas.AccessoryEditPath(acc, member));

    /// <summary>The copy section's buttons: the source is the lowest worn slot; «複製» copies to the lowest free slot, «枠へコピー…»
    /// to the next free slot, the transform set copies (after a preparing move of the source's set) onto another worn accessory
    /// whose prefab has that set (the page disables them otherwise).</summary>
    static Outcome Copy(Ctx x, StudioOps ops, Row row, (string name, string action, int set, string mirror, bool target) b, out string detail)
    {
        var table = StudioOps.AccessorySlotsOf(x.App.Session.Model.Find(x.Key));
        if (table == null || table.Count == 0) { detail = "copy: the character wears no accessory (the section has no source)"; return Outcome.Excluded; }
        int src = table.EntryBySlot.Keys.First();
        string from = table.IdBySlot[src];
        if (b.set == 0)
        {
            if (!b.target) return Try(x, row, new object[] { new { cmd = "chara.edit.accessory", key = x.Key, op = "copy", from } }, out detail);
            var free = table.FreeSlots();
            int to = free.Count > 1 ? free[1] : free.Count > 0 ? free[0] : -1;
            if (to < 0) { detail = "copy to a slot: no free slot"; return Outcome.Failed; }
            return Try(x, row, new object[] { new { cmd = "chara.edit.accessory", key = x.Key, op = "copy", from, toSlot = to } }, out detail);
        }
        string gateFeature = SchemaGates.AccMove(b.set);
        var withSet = table.EntryBySlot.Keys.Where(s => InspectorGates.Holds(ops, SchemaGates.MakerAcc(s, gateFeature), x.Key)).ToList();
        if (withSet.Count < 2) { detail = "copy of transform set " + b.set + ": fewer than two worn accessories have the set (the page disables the button)"; return Outcome.Excluded; }
        string f2 = table.IdBySlot[withSet[0]], t2 = table.IdBySlot[withSet[1]];
        string m = "moves[" + (b.set - 1) + "]";
        return Prepared(x, row, Set(x, AccRel(f2, m + ".position"), new[] { 1.1, 2.2, 3.3 }),
            new { cmd = "chara.edit.accessory", key = x.Key, op = "copy", from = f2, to = t2, parts = new[] { "move" + b.set }, mirror = b.mirror }, out detail);
    }
}
