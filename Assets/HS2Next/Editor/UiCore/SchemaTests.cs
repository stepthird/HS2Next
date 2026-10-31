// SchemaTests: every FieldSpec.Path resolves on the scene model (FieldAccessor.LeafType on a
// session with one node per kind) with the declared kind; ranges equal the HS2 table (scene model A.1)
// or are deviations listed (items T22); every label key (rows, sections,
// choices) exists in en; op labels are OpLabels (the mirror of StudioOps.Labels); gates are SchemaGates names.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace HS2Next.UI.Core.Tests
{
    using HS2Next.Save;
    using HS2Next.Save.Tests;
    using HS2Next.Scenes;
    using HS2Next.UI.Core;

    public static class SchemaTests
    {
        public static void Run(TestRunner t)
        {
            var s = new SceneSession(new SessionOptions { Ids = new CountingIdSource(7) });
            var ids = AddNodes(t, s);
            if (ids == null) return;
            Paths(t, s, ids);
            Ranges(t);
            Labels(t);
            Structure(t);
        }

        static Dictionary<string, string> AddNodes(TestRunner t, SceneSession s)
        {
            var specs = new[]
            {
                new NodeSpec { Doc = NodeDefaults.Chara(null, null) },
                new NodeSpec { Doc = NodeDefaults.Item(null, new ItemNodeState { Ref = "0123456789abcdef0123456789abcdef#1" }) },
                new NodeSpec { Doc = NodeDefaults.Light(null, "0123456789abcdef0123456789abcdef#2") },
                new NodeSpec { Doc = NodeDefaults.Camera(null, 1) },
                new NodeSpec { Doc = NodeDefaults.Folder(null) },
                new NodeSpec { Doc = NodeDefaults.Route(null, s.NewId()) },
            };
            var r = s.AddNodes(specs);
            t.Check(r.Status == CommandStatus.Ok && r.NewIds.Count == 6, "schema: the test session gets one node per kind (" + r + ")");
            if (r.NewIds.Count != 6) return null;
            var ids = new Dictionary<string, string>
            {
                [InspectorSchemas.KindChara] = r.NewIds[0], [InspectorSchemas.KindItem] = r.NewIds[1], [InspectorSchemas.KindLight] = r.NewIds[2],
                [InspectorSchemas.KindCamera] = r.NewIds[3], [InspectorSchemas.KindFolder] = r.NewIds[4], [InspectorSchemas.KindRoute] = r.NewIds[5],
            };
            t.Check(s.Set(ids[InspectorSchemas.KindRoute], "route.points[0].aid", new RouteAid()).Status != CommandStatus.Error, "schema: a route aid for the walk");
            // objects that are null by default, so paths through them resolve
            string chara = ids[InspectorSchemas.KindChara];
            foreach (var name in InspectorSchemas.IkTargets)
                t.Check(s.Set(chara, "chara.ik.targets." + name, new IkTarget()).Status != CommandStatus.Error, "schema: IK target " + name + " set for the walk");
            t.Check(s.Set(ids[InspectorSchemas.KindItem], "item.options", new List<bool> { true }).Status != CommandStatus.Error, "schema: an item option for the walk");
            return ids;
        }

        static readonly Dictionary<string, string> TemplateSample = new Dictionary<string, string>
        {
            ["slot"] = "1", ["bone"] = "cf_J_Hips", ["k"] = "0", [InspectorSchemas.RoutePointPlaceholder] = "0",
        };

        static IEnumerable<string> Expansions(FieldSpec f)
        {
            if (!f.IsTemplate) { yield return f.Path; yield break; }
            if (f.Placeholder == "target") { foreach (var n in InspectorSchemas.IkTargets) yield return f.Expand(n); yield break; }
            yield return f.Expand(TemplateSample.TryGetValue(f.Placeholder, out var v) ? v : "?");
        }

        static bool KindFits(FieldSpec f, Type leaf)
        {
            if (f.Component >= 0)
            {
                int n = leaf == typeof(Vec4) || leaf == typeof(Color4) || leaf == typeof(Quat4) ? 4 : leaf == typeof(Vec3) || leaf == typeof(Vec3?) ? 3 : leaf == typeof(Vec2) ? 2 : 0;
                return f.Component < n && f.Kind == FieldKind.Float;
            }
            switch (f.Kind)
            {
                case FieldKind.Float: return leaf == typeof(float) || leaf == typeof(float?);
                case FieldKind.Int: return leaf == typeof(int);
                case FieldKind.Bool: return leaf == typeof(bool);
                case FieldKind.Color:
                case FieldKind.ColorHdr: return leaf == typeof(Color4);
                case FieldKind.Vec2: return leaf == typeof(Vec2);
                case FieldKind.Vec3: return leaf == typeof(Vec3) || leaf == typeof(Vec3?);
                case FieldKind.Enum: return leaf == typeof(string) || leaf == typeof(int);
                case FieldKind.Ref: return leaf == typeof(string) || leaf == typeof(AnimeRef);
                case FieldKind.File: return leaf == typeof(FileRef);
                case FieldKind.Text: return leaf == typeof(string);
                case FieldKind.Node: return leaf == typeof(string);
                default: return false;
            }
        }

        static void Paths(TestRunner t, SceneSession s, Dictionary<string, string> ids)
        {
            int rows = 0, resolved = 0, fails = 0, buttons = 0, options = 0;
            foreach (var (owner, section) in InspectorSchemas.AllSections())
            {
                foreach (var f in section.Fields)
                {
                    rows++;
                    if (f.Kind == FieldKind.Button)
                    {
                        buttons++;
                        if (string.IsNullOrEmpty(f.Action)) { fails++; t.Check(false, "schema: button without an action " + f.LabelKey); }
                        continue;
                    }
                    if (f.Path == null) { fails++; t.Check(false, "schema: row without a path " + f.LabelKey); continue; }
                    if (f.Target == FieldTarget.Option)
                    {
                        options++;
                        bool ok = OptionDefs.TryGet(f.Path, out var od) &&
                                  (f.Kind == FieldKind.Float ? od.Kind == OptionKind.Float : f.Kind == FieldKind.Int ? od.Kind == OptionKind.Int : f.Kind == FieldKind.Bool ? od.Kind == OptionKind.Bool : od.Kind == OptionKind.Choice);
                        if (!ok) { fails++; t.Check(false, "schema: option row " + f.Path + " is not an option of kind " + f.Kind); }
                        continue;
                    }
                    var nodeOwners = owner == "node" ? ids.Keys.ToArray() : owner == "scene" ? new string[] { null } : new[] { owner };
                    foreach (var o in nodeOwners)
                    {
                        string nodeId = o == null ? null : ids[o];
                        if (o != null && (f.Target == FieldTarget.Scene)) { fails++; t.Check(false, "schema: node section holds a scene row " + f.Path); continue; }
                        if (o == null && f.Target != FieldTarget.Scene) { fails++; t.Check(false, "schema: scene section holds a node row " + f.Path); continue; }
                        // folders and cameras have no scale channel; the transform rows still resolve (scale = 1)
                        foreach (var path in Expansions(f))
                        {
                            try
                            {
                                var leaf = FieldAccessor.LeafType(s.Model, nodeId, path);
                                if (!KindFits(f, leaf)) { fails++; t.Check(false, "schema: " + (o ?? "scene") + " " + path + " is a " + leaf.Name + ", declared " + f.Kind + (f.Component >= 0 ? "#" + f.Component : "")); }
                                else resolved++;
                                if (f.IntensityPath != null)
                                {
                                    var il = FieldAccessor.LeafType(s.Model, nodeId, f.IntensityPath);
                                    if (il != typeof(float)) { fails++; t.Check(false, "schema: intensity path " + f.IntensityPath + " is a " + il.Name); }
                                }
                                if (f.DisabledBy != null && FieldAccessor.LeafType(s.Model, nodeId, f.DisabledBy) != typeof(bool))
                                { fails++; t.Check(false, "schema: disabledBy " + f.DisabledBy + " is not a bool"); }
                            }
                            catch (CommandException e) { fails++; t.Check(false, "schema: " + (o ?? "scene") + " " + path + " does not resolve: " + e.Code + " " + e.Message); }
                        }
                    }
                }
            }
            t.Check(fails == 0, "schema: every path resolves with its declared kind (" + resolved + " resolutions, " + fails + " failures)");
            t.Info("schema: " + rows + " rows (" + buttons + " buttons, " + options + " option rows), " + resolved + " path resolutions");
            t.Check(rows >= 200, "schema: at least 200 rows (" + rows + ")");
        }

        // HS2 UI ranges (slider min, max; whole) by path (+ "#component"): scene model A.1.
        static readonly Dictionary<string, (double min, double max, bool whole)> Hs2 = BuildHs2();

        static Dictionary<string, (double, double, bool)> BuildHs2()
        {
            var d = new Dictionary<string, (double, double, bool)>();
            void R(string p, double a, double b, bool whole = false) => d[p] = (a, b, whole);
            R("chara.anime.speed", 0, 3); R("chara.anime.pattern", 0, 1); R("chara.anime.params[0]", 0, 1); R("chara.anime.params[1]", 0, 1);
            R("chara.face.tears", 0, 1); R("chara.face.cheek", 0, 1); R("chara.state.nipple", 0, 1); R("chara.state.gloss", 0, 1);
            R("chara.state.wet", 0, 1); R("chara.state.son.length", 0.5, 3); R("chara.face.eyesOpen", 0, 1); R("chara.face.mouthOpen", 0, 1);
            R(OptionDefs.GizmoFkSize, 0.1, 1); R(OptionDefs.GizmoIkSize, 0.1, 1); R(OptionDefs.GizmoLookSize, 0.1, 1);
            for (int k = 0; k < 3; k++)
            {
                R("item.colors[" + k + "].metallic", 0, 1); R("item.colors[" + k + "].glossiness", 0, 1);
                string u = "item.colors[" + k + "].pattern.uv#";
                R(u + 0, 0.01, 20); R(u + 1, 0.01, 20); R(u + 2, -1, 1); R(u + 3, -1, 1);
                R("item.colors[" + k + "].pattern.rot", -1, 1);
            }
            R("item.alpha", 0, 1); R("item.emission.power", 0, 9); R("item.lightCancel", 0, 1); R("item.anime.speed", 0, 3);
            R("light.intensity", 0.1, 2); R("light.spotAngle", 1, 179, true);
            R("light.range@" + SchemaGates.LightPoint, 0.1, 100); R("light.range@" + SchemaGates.LightSpot, 0.5, 100);
            R("camera.fov", 1, 60);
            R("route.points[{point}].speed", 0.1, 30);   // scene model A.1
            R("charaLight.intensity", 0.1, 2); R("charaLight.rot[0]", 0, 359, true); R("charaLight.rot[1]", 0, 359, true);
            R("effects.colorGrading.blend", 0, 1); R("effects.colorGrading.saturation", -100, 100, true);
            R("effects.colorGrading.brightness", -100, 100, true); R("effects.colorGrading.contrast", -100, 100, true);
            R("effects.ambientOcclusion.intensity", 0, 1); R("effects.ambientOcclusion.thickness", 0, 10);
            R("effects.bloom.intensity", 0, 30); R("effects.bloom.threshold", 0, 2); R("effects.bloom.softKnee", 0, 1); R("effects.bloom.diffusion", 1, 10);
            R("effects.depthOfField.focalSize", 0, 2); R("effects.depthOfField.aperture", 0, 1);
            R("effects.vignette.intensity", 0, 1); R("effects.reflectionProbe.intensity", 0, 2);
            R("effects.fog.height", 1, 100, true); R("effects.fog.heightDensity", 0.001, 10); R("effects.fog.density", 0, 0.001);
            R("effects.sunShafts.distanceFalloff", 0.1, 1); R("effects.sunShafts.blurSize", 1, 10, true); R("effects.sunShafts.intensity", 0, 10, true);
            return d;
        }

        // Typed clamps that differ from the slider range in HS2 or by decision (key as Hs2): (typedMin, typedMax), null = open.
        static readonly Dictionary<string, (double? min, double? max, string why)> TypedExceptions = new Dictionary<string, (double?, double?, string)>
        {
            ["light.range@" + SchemaGates.LightPoint] = (0.1, null, "typed lower bound only"),
            ["light.range@" + SchemaGates.LightSpot] = (0.5, null, "typed lower bound only"),
            // item.emission.power: HS2 clamps typed input to 0–1 (inconsistent with the slider); T22 = 0–9
        };

        // Rows with a slider range that HS2 has no UI for (HS2Next additions).
        static readonly Dictionary<string, string> Hs2NextOnly = new Dictionary<string, string>
        {
            ["chara.fk.hipsOffset"] = "FK hips offset slider (HS2 has no control)",
            [OptionDefs.CaptureScale] = "screenshot scale (vanilla capRate 1, no UI)",
            [OptionDefs.CaptureWidth] = "custom screenshot width",
            [OptionDefs.CaptureHeight] = "custom screenshot height",
        };

        static string RangeKey(FieldSpec f) =>
            f.Path + (f.Component >= 0 ? "#" + f.Component : "") + (f.Path == "light.range" ? "@" + f.Gate : "");

        static void Ranges(TestRunner t)
        {
            int checkedRows = 0, fails = 0;
            var seen = new HashSet<string>();
            foreach (var f in InspectorSchemas.AllFields())
            {
                if (f.Kind != FieldKind.Float && f.Kind != FieldKind.Int) continue;
                if (f.Min == null || f.Max == null)
                {
                    // index rows: 0..n−1 from the runtime
                    if (!(f.Kind == FieldKind.Int && f.Min == 0 && f.Max == null)) { fails++; t.Check(false, "ranges: " + f.Id + " has no slider range"); }
                    continue;
                }
                string key = RangeKey(f);
                seen.Add(key);
                if (Hs2NextOnly.ContainsKey(key)) continue;
                if (!Hs2.TryGetValue(key, out var r)) { fails++; t.Check(false, "ranges: " + key + " is not in the HS2 table and not a listed deviation"); continue; }
                checkedRows++;
                if (f.Min != r.min || f.Max != r.max || f.Whole != (r.whole || f.Kind == FieldKind.Int))
                { fails++; t.Check(false, "ranges: " + key + " slider " + f.Min + "–" + f.Max + (f.Whole ? " whole" : "") + ", HS2 " + r.min + "–" + r.max + (r.whole ? " whole" : "")); }
                double? tmin = r.min, tmax = r.max;
                if (TypedExceptions.TryGetValue(key, out var te)) { tmin = te.min; tmax = te.max; }
                if (f.TypedMin != tmin || f.TypedMax != tmax)
                { fails++; t.Check(false, "ranges: " + key + " typed " + f.TypedMin + "–" + f.TypedMax + ", expected " + tmin + "–" + tmax); }
                if (f.Default is double dv && (dv < f.Min || dv > f.Max)) { fails++; t.Check(false, "ranges: " + key + " default " + dv + " outside the slider"); }
            }
            foreach (var k in Hs2.Keys) if (!seen.Contains(k)) { fails++; t.Check(false, "ranges: HS2 slider " + k + " has no schema row"); }
            t.Check(fails == 0, "ranges: " + checkedRows + " rows equal the HS2 table (" + fails + " failures)");

            // single facts (spec text)
            var light = InspectorSchemas.Light;
            t.Check(light.SelectMany(x => x.Fields).Where(x => x.Path == "light.range").All(x => Equals(x.Default, 30.0)), "ranges: light range reset 30 (Q-a)");
            t.Check(Equals(InspectorSchemas.Find(light, "light.spotAngle").Default, 30.0) && Equals(InspectorSchemas.Find(light, "light.intensity").Default, 1.0), "ranges: spot angle reset 30, intensity reset 1");
            var pow = InspectorSchemas.Find(InspectorSchemas.Item, "item.emission.power");
            t.Check(pow.TypedMin == 0 && pow.TypedMax == 9, "ranges: typed emission power 0–9 (T22, HS2 0–1)");
            var son = InspectorSchemas.Find(InspectorSchemas.Chara, "chara.state.son.length");
            t.Check(Equals(son.Default, 1.0), "ranges: son length default 1");
            var spd = InspectorSchemas.Find(InspectorSchemas.Chara, "chara.anime.speed");
            t.Check(Equals(spd.Default, 1.0) && spd.OpLabel == OpLabels.AnimationSpeed, "ranges: animation speed default 1, label");
            t.Check(InspectorSchemas.Find(InspectorSchemas.Scene, "map.rot").Wrap360 && !InspectorSchemas.Transform.Fields.First(x => x.Path == "transform.rot").Wrap360,
                "ranges: map rotation wraps at input, transform rotation does not (U29)");
            var sc = InspectorSchemas.Transform.Fields.First(x => x.Path == "transform.scale");
            t.Check(sc.TypedMin == 0.01 && sc.TypedMax == 9999999, "ranges: scale typed 0.01–9,999,999");
            // effect defaults equal the model defaults (the default buttons reset to them)
            var fx = new ScreenEffects();
            int dfails = 0;
            foreach (var f in InspectorSchemas.Scene.SelectMany(x => x.Fields).Where(x => x.Path != null && x.Path.StartsWith("effects.", StringComparison.Ordinal)))
            {
                if (!(f.Default is double d)) continue;
                var parts = f.Path.Split('.');
                object block = typeof(ScreenEffects).GetField(Up(parts[1])).GetValue(fx);
                object v = block.GetType().GetField(Up(parts[2])).GetValue(block);
                double mv = Convert.ToDouble(v, CultureInfo.InvariantCulture);
                if (Math.Abs(mv - d) > 1e-6) { dfails++; t.Check(false, "ranges: " + f.Path + " default " + d + " ≠ model " + mv); }
            }
            t.Check(dfails == 0, "ranges: effect default buttons = model defaults");
        }

        static string Up(string s) => char.ToUpperInvariant(s[0]) + s.Substring(1);

        static void Labels(TestRunner t)
        {
            L10nTable tb;
            try { tb = UiKit.Strings(t); }
            catch (Exception e) { t.Check(false, "labels: strings.tsv loads: " + e.Message); return; }
            var missing = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var (owner, sec) in InspectorSchemas.AllSections())
            {
                if (tb.Cell(sec.LabelKey, "en") == null) missing.Add(sec.LabelKey);
                foreach (var f in sec.Fields)
                {
                    if (tb.Cell(f.LabelKey, "en") == null) missing.Add(f.LabelKey);
                    if (f.Choices != null)
                        for (int i = 0; i < f.Choices.Count; i++)
                            if (tb.Cell(f.ChoiceLabelKey(i), "en") == null) missing.Add(f.ChoiceLabelKey(i));
                }
            }
            foreach (var g in GroupIds())
                if (tb.Cell("inspector.group." + g, "en") == null) missing.Add("inspector.group." + g);
            foreach (var n in InspectorSchemas.IkTargets)
                if (tb.Cell("ik.target." + n, "en") == null) missing.Add("ik.target." + n);
            foreach (var n in new[] { "point", "spot", "directional" })
                if (tb.Cell("light.type." + n, "en") == null) missing.Add("light.type." + n);
            t.Check(missing.Count == 0, "labels: every schema label key has en (" + missing.Count + " missing: " + string.Join(", ", missing.Take(40)) + ")");
            string dump = Environment.GetEnvironmentVariable("HS2N_UI_MISSING_KEYS");
            if (!string.IsNullOrEmpty(dump)) System.IO.File.WriteAllLines(dump, missing);
            var opBad = InspectorSchemas.AllFields().Where(f => f.Target != FieldTarget.Option && f.OpLabel != "" && !OpLabels.All.Contains(f.OpLabel)).Select(f => f.Id + "=" + f.OpLabel).ToList();
            t.Check(opBad.Count == 0, "labels: every op label is an OpLabels constant (\"\" = not recorded) (" + string.Join(", ", opBad.Take(10)) + ")");
            var optLabel = InspectorSchemas.AllFields().Where(f => f.Target == FieldTarget.Option && f.OpLabel != null).Select(f => f.Id).ToList();
            t.Check(optLabel.Count == 0, "labels: option rows record nothing (view state) (" + string.Join(", ", optLabel) + ")");
            var gateBad = InspectorSchemas.AllFields().Where(f => f.Gate != null && !SchemaGates.All.Contains(f.Gate)).Select(f => f.Id + "=" + f.Gate).ToList();
            t.Check(gateBad.Count == 0, "labels: every gate is a SchemaGates name (" + string.Join(", ", gateBad.Take(10)) + ")");
            t.Equal(OpLabels.All.Count, OpLabels.All.Distinct().Count(), "labels: OpLabels are distinct");
            OpsLabelParity(t);
        }

        /// <summary>The mirror equals StudioOps.Labels (package B's constants, read from the source text: the mono suite does
        /// not compile the Unity-side operations layer).</summary>
        static void OpsLabelParity(TestRunner t)
        {
            string src = System.IO.Path.GetFullPath(System.IO.Path.Combine(UiKit.ResourcesUi(t), "..", "..", "Runtime", "Scene", "Runtime", "Ops", "StudioOps.Labels.cs"));
            if (!System.IO.File.Exists(src)) { t.Info("parity: StudioOps.Labels.cs not found (skipped)"); return; }
            var ops = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(System.IO.File.ReadAllText(src), "const string (\\w+) = \"([^\"]*)\""))
                ops[m.Groups[1].Value] = m.Groups[2].Value;
            var mine = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var f in typeof(OpLabels).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
                if (f.IsLiteral && f.FieldType == typeof(string)) mine[f.Name] = (string)f.GetRawConstantValue();
            var diff = ops.Keys.Union(mine.Keys).Where(k => !ops.TryGetValue(k, out var a) || !mine.TryGetValue(k, out var b) || a != b)
                .Select(k => k + ": ops " + (ops.TryGetValue(k, out var a2) ? "\"" + a2 + "\"" : "—") + ", core " + (mine.TryGetValue(k, out var b2) ? "\"" + b2 + "\"" : "—")).ToList();
            t.Check(ops.Count > 0 && diff.Count == 0, "parity: OpLabels = StudioOps.Labels (" + ops.Count + " constants; " + string.Join("; ", diff) + ")");
        }

        /// <summary>Group ids that are not section ids (sub-groups inside a section: state.clothes, kinematics.fk, …).</summary>
        public static IEnumerable<string> GroupIds()
        {
            var sections = new HashSet<string>(InspectorSchemas.AllSections().Select(x => x.section.Id));
            return InspectorSchemas.AllFields().Select(f => f.Group).Where(g => g != null && !sections.Contains(g)).Distinct().OrderBy(g => g, StringComparer.Ordinal);
        }

        static void Structure(TestRunner t)
        {
            var tabs = InspectorSchemas.Chara.Select(x => x.Id).ToArray();
            t.Check(tabs.SequenceEqual(new[] { "anime", "state", "face", "look", "hands", "kinematics", "pose", "correction", "character" }),
                "structure: character tabs in the order (" + string.Join(", ", tabs) + ")");
            var fx = InspectorSchemas.Scene.Select(x => x.Id).Where(x => x.StartsWith("effects.", StringComparison.Ordinal)).Select(x => x.Substring(8)).ToArray();
            t.Check(fx.SequenceEqual(InspectorSchemas.EffectBlocks), "structure: 12 effect blocks in the vanilla order");
            int clothes = InspectorSchemas.Chara.SelectMany(x => x.Fields).Count(f => f.Path != null && f.Path.StartsWith("chara.state.clothes[", StringComparison.Ordinal));
            t.Equal(8, clothes, "structure: 8 clothes rows");
            int corr = InspectorSchemas.Chara.SelectMany(x => x.Fields).Count(f => f.Path != null && f.Path.StartsWith("chara.correction[", StringComparison.Ordinal));
            t.Equal(8, corr, "structure: 8 correction rows (U33)");
            var neck = InspectorSchemas.Find(InspectorSchemas.Chara, "chara.look.neck");
            t.Check(neck.ChoiceValues != null && neck.ChoiceValues.SequenceEqual(new[] { 0, 1, 3, 4 }), "structure: neck buttons {0, 1, 3, 4}");
            var ids = InspectorSchemas.AllSections().SelectMany(x => x.section.Fields.Select(f => x.owner + "/" + x.section.Id + "/" + f.Id + "/" + f.Gate + "/" + f.LabelKey)).ToList();
            t.Equal(ids.Count, ids.Distinct().Count(), "structure: no duplicate rows");
            var applyPrimary = InspectorSchemas.AllFields().Where(f => f.Applies == Applies.Primary).Select(f => f.Id).ToList();
            // the header name renames the primary (integrator, a question on the schema rows)
            var routePointIds = new HashSet<string>(InspectorSchemas.Route.Where(x => x.Id == "routePoint").SelectMany(x => x.Fields).Select(f => f.Id));
            t.Check(applyPrimary.All(id => id == "node:name" || id.StartsWith("node:camera.active", StringComparison.Ordinal) || id.StartsWith("button:ops.SetActiveCamera", StringComparison.Ordinal)
                    || routePointIds.Contains(id)),
                "structure: only the header name, the active-camera rows and the route point rows (one sub-selection, R25) apply to the primary (U25, T22) (" + string.Join(", ", applyPrimary) + ")");
            Phase36(t);
        }

        /// <summary>The Phase 3.6 rows: route / route point, scene images, the
        /// route list buttons, the capture dialog's options, the routes / time / capture options.</summary>
        static void Phase36(TestRunner t)
        {
            var route = InspectorSchemas.ForKind(InspectorSchemas.KindRoute);
            t.Check(ReferenceEquals(route, InspectorSchemas.Route) && route.Select(x => x.Id).SequenceEqual(new[] { "route", "routePoint" }), "3.6: route inspector sections Route, Route point");
            var rs = route[0].Fields;
            t.Check(rs.Any(f => f.Path == "route.loop" && f.Kind == FieldKind.Bool) && rs.Any(f => f.Path == "route.showLine" && f.Kind == FieldKind.Bool),
                "3.6: loop and show-line rows");
            var col = rs.First(f => f.Path == "route.color");
            t.Check(col.Kind == FieldKind.Color && !col.ColorAlpha && col.OpLabel == OpLabels.Route, "3.6: line colour without alpha, label Route");
            var orient = rs.First(f => f.Path == "route.orient");
            t.Check(orient.Choices.SequenceEqual(DocEnum.CamelNamesOf<RouteOrient>()), "3.6: orient choices = the model's (" + string.Join(", ", orient.Choices) + ")");
            var play = rs.First(f => f.Action == ActionIds.RoutePlay);
            var add = rs.First(f => f.Action == ActionIds.RouteAddPoint);
            t.Check(play.OpLabel == "" && rs.First(f => f.Action == ActionIds.RouteStop).OpLabel == "" && add.OpLabel == OpLabels.AddRoutePoint && add.Gate == SchemaGates.RouteCanAddPoint,
                "3.6: play / stop not recorded (R7), add point = Add route point");
            var ps = route[1].Fields;
            t.Check(ps.Where(f => f.Kind != FieldKind.Button).All(f => f.IsTemplate && f.Placeholder == InspectorSchemas.RoutePointPlaceholder && f.Path.StartsWith("route.points[{point}].", StringComparison.Ordinal)),
                "3.6: point rows are templates over {point}");
            var speed = ps.First(f => f.Path == "route.points[{point}].speed");
            t.Check(speed.Min == 0.1 && speed.Max == 30 && speed.TypedMin == 0.1 && speed.TypedMax == 30 && speed.Format == "0.0" && speed.Gate == SchemaGates.RoutePointUnlinked,
                "3.6: speed 0.1–30, typed clamped, one decimal, greyed on a linked point");
            var ease = ps.First(f => f.Path == "route.points[{point}].ease");
            var names = Enum.GetNames(typeof(RouteEase));
            t.Check(InspectorSchemas.RouteEaseNames.SequenceEqual(names) && InspectorSchemas.RouteEaseNames[21] == "linear", "3.6: the 32 ease names = RouteEase in enum order (linear 21)");
            t.Check(ease.Choices.Count == 32 && ease.Choices.Distinct().Count() == 32 && new HashSet<string>(ease.Choices).SetEquals(names), "3.6: ease choices = the 32 names");
            t.Check(ease.Choices.Take(InspectorSchemas.RouteEaseUiCount).SequenceEqual(new[] { "linear", "easeInQuad", "easeOutQuad", "easeInQuart", "easeOutQuart", "easeOutBounce" }),
                "3.6: the six HS2 entries first, in HS2's order (R10)");
            t.Check(ease.ChoiceLabelKey(5) == "route.ease.easeOutBounce" && InspectorSchemas.RouteEaseLabelKey("spring") == "route.ease.spring", "3.6: ease label keys route.ease.<name>");
            var conn = ps.First(f => f.Path == "route.points[{point}].connection");
            t.Check(conn.Choices.SequenceEqual(DocEnum.CamelNamesOf<RouteConnection>()), "3.6: connection choices = the model's");
            t.Check(ps.First(f => f.Path == "route.points[{point}].link").Gate == SchemaGates.RoutePointLinkable
                && ps.First(f => f.Path == "route.points[{point}].pos").Gate == SchemaGates.RoutePointOther
                && ps.First(f => f.Path == "route.points[{point}].rot").Gate == SchemaGates.RoutePointStart
                && ps.First(f => f.Path == "route.points[{point}].aid.pos").Gate == SchemaGates.RoutePointCurve
                && ps.First(f => f.Action == ActionIds.RouteResetAid).Gate == SchemaGates.RoutePointCurve,
                "3.6: point gates (link on a linkable point, pos not on the start, rot on the start only, aid / reset on a curve point)");
            t.Check(ps.Where(f => f.Kind != FieldKind.Button).All(f => f.OpLabel == OpLabels.RoutePoint) && ps.First(f => f.Action == ActionIds.RouteDeletePoint).OpLabel == OpLabels.DeleteRoutePoint,
                "3.6: point rows record Route point, delete Delete route point");
            // scene images and the route list: separate tables, not in the generic scene list
            var img = InspectorSchemas.SceneImages.Single();
            var bg = img.Fields.First(f => f.Path == "background");
            var fr = img.Fields.First(f => f.Path == "frame");
            t.Check(bg.Kind == FieldKind.File && bg.Target == FieldTarget.Scene && bg.Nullable && bg.OpLabel == OpLabels.Background && fr.Kind == FieldKind.File && fr.OpLabel == OpLabels.Frame,
                "3.6: background / frame file rows (scene, none allowed, labels Background / Frame)");
            t.Check(!InspectorSchemas.Scene.Any(x => x.Id == "images" || x.Id == "routes"), "3.6: images / routes are not in the generic scene list (C and R place them)");
            t.Check(InspectorSchemas.SceneRoutes.Single().Fields.Select(f => f.Action).SequenceEqual(new[] { ActionIds.RoutePlayAll, ActionIds.RouteRestartAll, ActionIds.RouteStopAll })
                && InspectorSchemas.SceneRoutes.Single().Fields.All(f => f.OpLabel == ""), "3.6: route list buttons play all / restart all / stop all, not recorded");
            // capture dialog rows are options
            var cap = InspectorSchemas.Capture.Single().Fields;
            var capKeys = cap.Where(f => f.Kind != FieldKind.Button).Select(f => f.Path).ToList();
            t.Check(cap.Where(f => f.Kind != FieldKind.Button).All(f => f.Target == FieldTarget.Option && f.OpLabel == null)
                && capKeys.SequenceEqual(new[] { OptionDefs.CaptureMode, OptionDefs.CaptureScale, OptionDefs.CaptureWidth, OptionDefs.CaptureHeight, OptionDefs.CaptureAspectLock, OptionDefs.CaptureSupersample, OptionDefs.CaptureFrame }),
                "3.6: capture dialog rows are the capture.* options (" + string.Join(", ", capKeys) + ")");
            foreach (var f in cap.Where(f => f.Kind == FieldKind.Enum))
                t.Check(f.Choices.SequenceEqual(OptionDefs.Get(f.Path).Choices) && f.ChoiceLabelKey(0) == OptionDefs.Get(f.Path).ChoiceLabelKey(f.Choices[0]), "3.6: " + f.Path + " choices and labels = the option's");
            // options
            bool Opt(string key, OptionKind kind, object def, double? min = null, double? max = null) =>
                OptionDefs.TryGet(key, out var d) && d.Kind == kind && Equals(d.Default, def) && d.Min == min && d.Max == max;
            t.Check(Opt(OptionDefs.RoutesPointLimit, OptionKind.Bool, true) && OptionDefs.Get(OptionDefs.RoutesPointLimit).Vanilla == "routePointLimit", "3.6: routes.pointLimit on (R9, vanilla routePointLimit)");
            t.Check(Opt(OptionDefs.RoutesLineWidth, OptionKind.Float, 1.0, 0.1, 4), "3.6: routes.lineWidth 1, 0.1–4 (× 16 px)");
            t.Check(OptionDefs.Get(OptionDefs.RoutesTiming).Choices.SequenceEqual(new[] { "hs2", "length" }) && Equals(OptionDefs.Get(OptionDefs.RoutesTiming).Default, "hs2"), "3.6: routes.timing hs2 (R3) | length");
            t.Check(Opt(OptionDefs.RoutesOrientSmoothing, OptionKind.Bool, true) && Opt(OptionDefs.ViewRoutes, OptionKind.Bool, true), "3.6: orient smoothing on (R6), view.routes on");
            t.Check(Opt(OptionDefs.TimeStepDt, OptionKind.Float, 1.0 / 60.0, 0.001, 1), "3.6: time.stepDt 1/60 (0.001–1)");
            t.Check(Opt(OptionDefs.CaptureMaxPixels, OptionKind.Int, 16588800, 1000000, 33177600), "3.6: capture.maxPixels 16,588,800 up to 33,177,600 (R12)");
            t.Check(Opt(OptionDefs.CaptureScale, OptionKind.Float, 1.0, 1, 4) && Opt(OptionDefs.CaptureFrame, OptionKind.Bool, true) && Opt(OptionDefs.ImagesEmbed, OptionKind.Bool, true)
                && Equals(OptionDefs.Get(OptionDefs.CaptureSupersample).Default, "1") && Equals(OptionDefs.Get(OptionDefs.CaptureMode).Default, "fit"),
                "3.6: capture scale 1, supersample 1, mode fit, frame in (R13, R14, R16), images embedded (R18)");
            t.Check(OptionDefs.Get(OptionDefs.CaptureMaxPixels).Coerce(99999999) is int cm && cm == 33177600, "3.6: the cap setting clamps to 8K pixels");
            var panelSections = new[] { "general", "camera", "gizmo", "add", "view", "kinematics", "rendering" };
            t.Check(OptionDefs.All.All(o => panelSections.Contains(o.Section)), "3.6: options stay in the options panel's existing sections");
            // op labels of the 3.6 ops
            t.Check(new[] { OpLabels.AddRoute, OpLabels.AddRoutePoint, OpLabels.DeleteRoutePoint, OpLabels.Route, OpLabels.RoutePoint, OpLabels.Background, OpLabels.Frame }
                .SequenceEqual(new[] { "Add route", "Add route point", "Delete route point", "Route", "Route point", "Background", "Frame" }) && OpLabels.All.Contains(OpLabels.Frame),
                "3.6: op labels");
        }
    }
}
