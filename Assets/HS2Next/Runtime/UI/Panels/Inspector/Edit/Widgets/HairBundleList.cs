// The bundle list of a hair part (row kind FieldKind.HairBundles): one foldout per
// bundle the part's prefab shows (a correction transform present; the facts of the realized part, MakerServices.Hair /
// HairInfo), titled «調整NN» (1.. among the shown, HS2), `hair-bundle-<part>-<index>`, holding six component slider rows on
// the effective rates (`field-appearance.chara.hair.<part>.bundles[<index>].move#0..2`, `…rotate#0..2`; display 0–100, typed
// −100..200, the stored value shown as it is), the «揺らさない» toggle (`…bundles[<index>].noShake`) and the bundle's handle
// toggle (`hair-handle-<part>-<index>`, view state, default on). The values are the effective bundles (the stored list when
// it is complete for the prefab, else the prefab defaults; MakerEdits.EffectiveBundles). Writes go through
// StudioOps.EditHairBundle — a slider drag is one transaction (per frame each selected character's own effective vector
// with the component replaced; the first frame materializes the list; «髪の調整をまとめて行う» is the op's rule), a typed
// value merges with the previous typed edit of the row, ↺ / a double click = the prefab default of that component (one entry
// "Reset value"), the toggle one entry "Edit hair". Footer: «全て初期化» (`hair-bundles-reset-<part>`, ResetHairBundles), the
// all-handles toggle (`hair-handles`, app option maker.hairHandles, default on, J14) and move / rotate (`hair-handle-move`,
// `hair-handle-rotate`: the 3.5 tool state). Foldouts beyond the first four start collapsed and build their rows when first
// opened (mods have up to 37 bundles). Picking a bundle's marker in the viewport (sub-selection `<id>/hair/<part>/<index>`)
// opens and reveals its foldout. The list rebuilds when the part's prefab changes (the page's live gates).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Binding;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    public sealed class HairBundleList : VisualElement
    {
        public const string UssClass = "hs-hair-bundles", GroupClass = "hs-hair-bundle", SelectedClass = "hs-hair-bundle--selected";
        /// <summary>The app option of the bundle handles (all handles; default on, J14).</summary>
        public const string HandlesOption = "maker.hairHandles";
        /// <summary>Foldouts of the first bundles open at build; the rest start collapsed.</summary>
        public const int OpenGroups = 4;
        const string Labels = "inspector.charaEdit.hair.";

        // the per-bundle handle toggles turned off ("<part>/<index>"; view state kept while the app runs)
        static readonly HashSet<string> handleOff = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>The bundle list attached to a panel (the Hair page's shown part), or null.</summary>
        public static HairBundleList Current { get; private set; }

        /// <summary>Whether bundle <paramref name="index"/>'s own handle toggle of part <paramref name="part"/> is on (HX8).</summary>
        public static bool HandleOn(int part, int index) => !handleOff.Contains(Key(part, index));
        public static void SetHandle(int part, int index, bool on) { if (on) handleOff.Remove(Key(part, index)); else handleOff.Add(Key(part, index)); }
        static string Key(int part, int index) => InspectorSchemas.HairParts[part] + "/" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);

        readonly UiContext ctx;
        readonly TargetSelector sel;
        readonly VisualElement groupsHost, footer;
        readonly List<Group> groups = new List<Group>();
        readonly Toggle handlesToggle;
        readonly SegmentedButtons toolButtons;

        public FieldSpec Spec { get; }
        /// <summary>0 back, 1 front, 2 side, 3 extension.</summary>
        public int Part { get; }
        /// <summary>The character whose prefab facts the list shows (the Edit tab's primary).</summary>
        public string Primary { get; }
        /// <summary>`appearance.chara.hair.&lt;part&gt;.bundles`.</summary>
        public string Path { get; }
        /// <summary>The part's prefab facts the groups were built from (null: no realization).</summary>
        public HairPartFacts Facts { get; private set; }
        /// <summary>The prefab and the shown bundle indices (the page rebuilds the list when it changes).</summary>
        public string Structure { get; private set; }
        public FieldBinding Binding { get; }
        public Button ResetAll { get; }
        /// <summary>Build time of the groups (ms; UH5).</summary>
        public double LastBuildMs { get; private set; }
        /// <summary>The last write's result (tests).</summary>
        public OpResult LastResult { get; private set; }
        /// <summary>The boneInfo indices of the shown bundles, in order.</summary>
        public IReadOnlyList<int> ShownIndices => groups.Select(g => g.F.Index).ToList();

        sealed class Group
        {
            public HairBundleFacts F;
            public Foldout Fold;
            public readonly BundleSlider[] Sliders = new BundleSlider[6];
            public Toggle NoShake, Handle;
            public bool Built;
        }

        public HairBundleList(UiContext ctx, FieldSpec spec, TargetSelector selector, string primary, int part, string expandedPath = null)
        {
            this.ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            Spec = spec ?? throw new ArgumentNullException(nameof(spec));
            sel = selector;
            Primary = primary;
            Part = part;
            Path = expandedPath ?? spec.Path;
            name = "field-" + Path;
            AddToClassList(FieldRow.UssClass);
            AddToClassList(UssClass);
            groupsHost = new VisualElement { name = "hair-bundles-" + InspectorSchemas.HairParts[part] };
            Add(groupsHost);

            footer = new VisualElement { name = "hair-bundles-footer" };
            footer.AddToClassList("hs-hair-bundles__footer");
            ResetAll = InspectorUi.Button(Labels + "bundlesReset", () => ResetAllBundles(), "hair-bundles-reset-" + InspectorSchemas.HairParts[part]);
            footer.Add(ResetAll);
            handlesToggle = new Toggle(L10n.Get("maker.hair.handles")) { name = "hair-handles" };
            handlesToggle.AddToClassList("hs-field__toggle");
            Loc.Bind(handlesToggle, "maker.hair.handles");
            handlesToggle.RegisterValueChangedCallback(e => ctx.Options?.Set(HandlesOption, e.newValue));
            footer.Add(handlesToggle);
            toolButtons = new SegmentedButtons(new[] { L10n.Get("maker.hair.handleMove"), L10n.Get("maker.hair.handleRotate") });
            toolButtons.Buttons[0].name = "hair-handle-move";
            toolButtons.Buttons[1].name = "hair-handle-rotate";
            Loc.Bind(toolButtons.Buttons[0], "maker.hair.handleMove");
            Loc.Bind(toolButtons.Buttons[1], "maker.hair.handleRotate");
            toolButtons.Selected += i => ctx.Tool = i == 1 ? GizmoTool.Rotate : GizmoTool.Move;
            footer.Add(toolButtons);
            Add(footer);

            Rebuild();
            Binding = ctx.Bindings.Bind(this, new FieldBinding { Selector = sel, Path = Path, Apply = (el, v) => RefreshValues() });
            ShowFooter();
            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                Current = this;
                ctx.SubSelectionChanged += OnSub;
                ctx.ViewStateChanged += OnView;
                if (ctx.Options != null) ctx.Options.Changed += OnOption;
                ShowFooter();
                OnSub(ctx.SubSelection);
            });
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                if (Current == this) Current = null;
                ctx.SubSelectionChanged -= OnSub;
                ctx.ViewStateChanged -= OnView;
                if (ctx.Options != null) ctx.Options.Changed -= OnOption;
            });
        }

        void OnView(string what) { if (what == "tool" || what == "options") ShowFooter(); }
        void OnOption(string key) { if (key == HandlesOption) ShowFooter(); }

        void ShowFooter()
        {
            handlesToggle.SetValueWithoutNotify(ctx.Options == null || ctx.Options.Bool(HandlesOption));
            var t = ctx.Tool;
            toolButtons.SetDisplay(t == GizmoTool.Rotate ? 1 : t == GizmoTool.Select ? -1 : 0, false);
        }

        // ---------------------------------------------------------------- facts and structure

        /// <summary>The prefab facts of <paramref name="id"/>'s part (the realization; false when it is not built).</summary>
        bool FactsOf(string id, out HairPartFacts f) => HairInfo.TryPart(ctx.Runtime, id, Part, out f) && f != null && f.HasPrefab;

        /// <summary>The structure string of a part's facts (prefab, shown bundle indices; "-" without facts).</summary>
        public static string StructureOf(HairPartFacts f)
        {
            if (f == null || !f.HasPrefab) return "-";
            var sb = new System.Text.StringBuilder(f.PrefabName ?? "").Append('|').Append(f.Item).Append('|').Append(f.Bundles?.Length ?? 0).Append(':');
            if (f.Bundles != null) foreach (var b in f.Bundles) if (b.Shown) sb.Append(b.Index).Append(',');
            return sb.ToString();
        }

        /// <summary>The structure of node <paramref name="id"/>'s part <paramref name="part"/> now (the page's live gate signature).</summary>
        public static string StructureOf(UiContext ctx, string id, int part) =>
            HairInfo.TryPart(ctx?.Runtime, id, part, out var f) ? StructureOf(f) : "-";

        /// <summary>Re-reads the facts and builds the groups (shown bundles; the first four open).</summary>
        public void Rebuild()
        {
            var sw = Stopwatch.StartNew();
            groupsHost.Clear();
            groups.Clear();
            Facts = FactsOf(Primary, out var f) ? f : null;
            Structure = StructureOf(Facts);
            if (Facts?.Bundles != null)
                foreach (var b in Facts.Bundles)
                {
                    if (b == null || !b.Shown) continue;
                    var g = new Group { F = b };
                    string title = L10n.Format(Labels + InspectorSchemas.HairBundleTitleName, b.Title.ToString("00", System.Globalization.CultureInfo.InvariantCulture));
                    g.Fold = new Foldout { text = title, value = groups.Count < OpenGroups, name = "hair-bundle-" + InspectorSchemas.HairParts[Part] + "-" + b.Index.ToString(System.Globalization.CultureInfo.InvariantCulture) };
                    g.Fold.AddToClassList(GroupClass);
                    var gg = g;
                    g.Fold.RegisterValueChangedCallback(e => { if (e.target == gg.Fold && e.newValue && !gg.Built) { BuildGroup(gg); RefreshValues(); } });
                    if (g.Fold.value) BuildGroup(g);
                    groups.Add(g);
                    groupsHost.Add(g.Fold);
                }
            ResetAll.style.display = groups.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            LastBuildMs = sw.Elapsed.TotalMilliseconds;
            RefreshValues();
            MarkSelected(ctx.SubSelection);
        }

        void BuildGroup(Group g)
        {
            if (g.Built) return;
            g.Built = true;
            string idx = g.F.Index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string basePath = Path + "[" + idx + "]";
            for (int k = 0; k < 6; k++)
            {
                bool rot = k >= 3;
                int c = k % 3;
                var s = new BundleSlider(this, g.F.Index, rot, c, Labels + InspectorSchemas.HairBundleComponentNames[k], basePath + (rot ? ".rotate" : ".move"));
                g.Sliders[k] = s;
                g.Fold.Add(s);
            }
            var nsRow = new VisualElement { name = "field-" + basePath + ".noShake" };
            nsRow.AddToClassList(FieldRow.UssClass);
            nsRow.AddToClassList(MakerToggleRow.UssClass);
            g.NoShake = new Toggle(L10n.Get(Labels + InspectorSchemas.HairBundleNoShakeName)) { name = "toggle" };
            g.NoShake.AddToClassList("hs-field__toggle");
            Loc.Bind(g.NoShake, Labels + InspectorSchemas.HairBundleNoShakeName);
            int index = g.F.Index;
            g.NoShake.RegisterValueChangedCallback(e => WriteNoShake(index, e.newValue));
            nsRow.Add(g.NoShake);
            g.Fold.Add(nsRow);
            g.Handle = new Toggle(L10n.Get("maker.hair.handle")) { name = "hair-handle-" + InspectorSchemas.HairParts[Part] + "-" + idx };
            g.Handle.AddToClassList("hs-field__toggle");
            Loc.Bind(g.Handle, "maker.hair.handle");
            g.Handle.SetValueWithoutNotify(HandleOn(Part, index));
            g.Handle.RegisterValueChangedCallback(e => SetHandle(Part, index, e.newValue));
            g.Fold.Add(g.Handle);
        }

        // ---------------------------------------------------------------- values

        IReadOnlyList<string> Targets => MakerRowKit.Writable(ctx, Spec, sel);

        /// <summary>The effective bundles of node <paramref name="id"/>'s part (stored when complete, else the prefab defaults), or null.</summary>
        public List<HairBundle> EffectiveOf(string id)
        {
            HairPartFacts f;
            if (id == Primary && Facts != null) f = Facts;
            else if (!FactsOf(id, out f)) return null;
            var stored = ctx.Model.Find(id)?.Appearance?.Stored?.Chara?.Hair?.Parts[Part]?.Bundles;
            return MakerEdits.EffectiveBundles(stored, f.Bundles, out _);
        }

        static float Comp(Vec3 v, int c) => c == 0 ? v.X : c == 1 ? v.Y : v.Z;
        static Vec3 With(Vec3 v, int c, float x) => c == 0 ? new Vec3(x, v.Y, v.Z) : c == 1 ? new Vec3(v.X, x, v.Z) : new Vec3(v.X, v.Y, x);

        /// <summary>The effective rate of the primary's bundle <paramref name="index"/> (component <paramref name="c"/> of move or rotate).</summary>
        public bool TryValue(int index, bool rotate, int c, out float value)
        {
            value = 0;
            var l = EffectiveOf(Primary);
            if (l == null || index < 0 || index >= l.Count) return false;
            value = Comp(rotate ? l[index].Rotate : l[index].Move, c);
            return true;
        }

        void RefreshValues()
        {
            var targets = sel?.Resolve(ctx) ?? new List<string>();
            var lists = new List<List<HairBundle>>();
            var own = EffectiveOf(Primary);
            foreach (var id in targets) if (id != Primary) { var l = EffectiveOf(id); if (l != null) lists.Add(l); }
            foreach (var g in groups)
            {
                if (!g.Built) continue;
                int i = g.F.Index;
                var b = own != null && i < own.Count ? own[i] : null;
                for (int k = 0; k < 6; k++)
                {
                    bool rot = k >= 3;
                    int c = k % 3;
                    float v = b == null ? 0 : Comp(rot ? b.Rotate : b.Move, c);
                    bool mixed = false;
                    foreach (var l in lists)
                        if (i < l.Count && Math.Abs(Comp(rot ? l[i].Rotate : l[i].Move, c) - v) > 1e-6f) { mixed = true; break; }
                    g.Sliders[k].Show(v, mixed);
                }
                bool ns = b != null && b.NoShake;
                g.NoShake.SetValueWithoutNotify(ns);
                g.NoShake.showMixedValue = lists.Any(l => i < l.Count && l[i].NoShake != ns);
                g.Handle.SetValueWithoutNotify(HandleOn(Part, i));
            }
        }

        // ---------------------------------------------------------------- writes

        /// <summary>
        /// Sets component <paramref name="c"/> of bundle <paramref name="index"/>'s move (or rotate) rate to <paramref name="value"/>
        /// on every writable selected character — each its own effective vector with that component replaced — through
        /// StudioOps.EditHairBundle (inside the open drag edit, else one entry; <paramref name="mergeKey"/> merges typed edits).
        /// </summary>
        public OpResult WriteComponent(int index, bool rotate, int c, double value, string mergeKey = null, UiEdit drag = null)
        {
            var targets = Targets;
            if (targets.Count == 0) return LastResult = OpResult.NothingToDo();
            var o = UiContext.User(OpLabels.EditHair, mergeKey);
            OpResult r = OpResult.NothingToDo();
            using (var own = drag == null ? ctx.BeginEdit(OpLabels.EditHair, mergeKey) : null)
            {
                foreach (var id in targets)
                {
                    var l = EffectiveOf(id);
                    if (l == null || index < 0 || index >= l.Count) continue;
                    var v = With(rotate ? l[index].Rotate : l[index].Move, c, (float)value);
                    r = ctx.Ops.EditHairBundle(new[] { id }, Part, index, rotate ? (Vec3?)null : v, rotate ? v : (Vec3?)null, null, false, -1, o);
                    if (!r.Ok) break;
                }
                if (own != null) { if (r.Ok) r = own.Commit(); else own.Cancel(); }
            }
            LastResult = r;
            if (drag == null) { ctx.ShowResult(r); Binding?.MarkDirty(); }
            else if (!r.Ok) ctx.ShowResult(r);
            return r;
        }

        /// <summary>↺ of one component: the prefab default (StudioOps.EditHairBundle reset with the axis; one entry "Reset value").</summary>
        public OpResult ResetComponent(int index, bool rotate, int c)
        {
            var targets = Targets;
            if (targets.Count == 0) return LastResult = OpResult.NothingToDo();
            LastResult = ctx.ShowResult(ctx.Ops.EditHairBundle(targets, Part, index, rotate ? (Vec3?)null : Vec3.Zero, rotate ? Vec3.Zero : (Vec3?)null, null, true, c, UiContext.User(OpLabels.ResetValue)));
            Binding?.MarkDirty();
            return LastResult;
        }

        /// <summary>«揺らさない» of bundle <paramref name="index"/> (one entry "Edit hair").</summary>
        public OpResult WriteNoShake(int index, bool on)
        {
            var targets = Targets;
            if (targets.Count == 0) { RefreshValues(); return LastResult = OpResult.NothingToDo(); }
            LastResult = ctx.ShowResult(ctx.Ops.EditHairBundle(targets, Part, index, null, null, on, false, -1, UiContext.User(OpLabels.EditHair)));
            Binding?.MarkDirty();
            return LastResult;
        }

        /// <summary>«全て初期化»: every bundle of the part to the prefab defaults, noShake kept (one entry "Edit hair").</summary>
        public OpResult ResetAllBundles()
        {
            var targets = Targets;
            if (targets.Count == 0) return LastResult = OpResult.NothingToDo();
            LastResult = ctx.ShowResult(ctx.Ops.ResetHairBundles(targets, Part, UiContext.User(OpLabels.EditHair)));
            Binding?.MarkDirty();
            return LastResult;
        }

        // ---------------------------------------------------------------- sub-selection

        void OnSub(string s)
        {
            MarkSelected(s);
            if (!StudioOps.TryParseHairSub(s, out var id, out int p, out int index) || id != Primary || p != Part) return;
            var g = groups.FirstOrDefault(x => x.F.Index == index);
            if (g == null) return;
            if (!g.Fold.value) g.Fold.value = true;
            var scroll = GetFirstAncestorOfType<ScrollView>();
            var target = g.Fold;
            if (scroll != null) schedule.Execute(() => { if (target.panel != null) scroll.ScrollTo(target); }).ExecuteLater(30);
        }

        void MarkSelected(string s)
        {
            int index = StudioOps.TryParseHairSub(s, out var id, out int p, out int i) && id == Primary && p == Part ? i : -1;
            foreach (var g in groups) g.Fold.EnableInClassList(SelectedClass, g.F.Index == index);
        }

        /// <summary>The foldout of bundle <paramref name="index"/> (null when not shown).</summary>
        public Foldout GroupOf(int index) => groups.FirstOrDefault(g => g.F.Index == index)?.Fold;

        /// <summary>The slider row of bundle <paramref name="index"/>'s component (built on first open; null when not shown).</summary>
        public BundleSlider SliderOf(int index, bool rotate, int c)
        {
            var g = groups.FirstOrDefault(x => x.F.Index == index);
            if (g == null) return null;
            if (!g.Built) { BuildGroup(g); RefreshValues(); }
            return g.Sliders[(rotate ? 3 : 0) + c];
        }

        /// <summary>True while the list is shown: attached, every foldout above it open and nothing above it hidden.</summary>
        public bool IsShown()
        {
            if (panel == null || resolvedStyle.display == DisplayStyle.None) return false;
            for (VisualElement p = parent; p != null; p = p.parent)
            {
                if (p is Foldout fo && !fo.value) return false;
                if (p.resolvedStyle.display == DisplayStyle.None) return false;
            }
            return true;
        }

        /// <summary>The bundles whose handles the viewport draws now (shown list, option on, own toggle on), boneInfo indices.</summary>
        public IEnumerable<int> HandleIndices()
        {
            if (!IsShown() || (ctx.Options != null && !ctx.Options.Bool(HandlesOption))) yield break;
            foreach (var g in groups) if (HandleOn(Part, g.F.Index)) yield return g.F.Index;
        }

        /// <summary>The facts of shown bundle <paramref name="index"/> (null when not shown).</summary>
        public HairBundleFacts FactsOfBundle(int index) => groups.FirstOrDefault(g => g.F.Index == index)?.F;

        // ---------------------------------------------------------------- the slider row of one component

        public sealed class BundleSlider : VisualElement
        {
            readonly HairBundleList list;
            UiEdit edit;
            public int Index { get; }
            public bool Rotate { get; }
            public int Component { get; }
            public NumberSlider Control { get; }
            public bool Dragging => edit != null && edit.IsOpen;

            internal BundleSlider(HairBundleList list, int index, bool rotate, int component, string labelKey, string vectorPath)
            {
                this.list = list;
                Index = index;
                Rotate = rotate;
                Component = component;
                name = "field-" + vectorPath + "#" + component.ToString(System.Globalization.CultureInfo.InvariantCulture);
                AddToClassList(FieldRow.UssClass);
                AddToClassList(MakerSliderRow.UssClass);
                double k = InspectorSchemas.MakerSliderScale;
                Control = new NumberSlider(L10n.Get(labelKey), 0, k)
                {
                    TypedMin = InspectorSchemas.MakerTypedMin * k, TypedMax = InspectorSchemas.MakerTypedMax * k, Whole = false, Format = "0",
                };
                Control.DefaultValue = () => 0;   // ↺ is always offered (the prefab default comes from the op)
                Control.Edited += OnEdited;
                Loc.Bind(Control.LabelElement, labelKey);
                Loc.Tooltip(Control.ResetButton, "maker.reset.tooltip");
                Add(Control);
            }

            internal void Show(float stored, bool mixed) => Control.SetDisplay(stored * InspectorSchemas.MakerSliderScale, mixed);

            string MergeKey()
            {
                var t = list.Targets.Where(x => x != null).OrderBy(x => x, StringComparer.Ordinal);
                return "ui.typed/" + string.Join(",", t) + "/" + name.Substring("field-".Length);
            }

            void OnEdited(EditPhase phase, double shown)
            {
                double k = InspectorSchemas.MakerSliderScale;
                switch (phase)
                {
                    case EditPhase.Begin:
                        edit?.Cancel();
                        edit = list.ctx.BeginEdit(OpLabels.EditHair);
                        var mine = edit;
                        mine.Ended += committed => { if (edit == mine) edit = null; if (!committed) list.Binding?.MarkDirty(); };
                        return;
                    case EditPhase.End:
                    {
                        var e = edit;
                        edit = null;
                        list.LastResult = list.ctx.ShowResult(e != null ? e.Commit() : OpResult.NothingToDo());
                        list.Binding?.MarkDirty();
                        return;
                    }
                    case EditPhase.Preview:
                        list.WriteComponent(Index, Rotate, Component, Math.Max(0, Math.Min(1, shown / k)), null, edit);
                        return;
                    case EditPhase.Typed:
                        list.WriteComponent(Index, Rotate, Component, shown / k, MergeKey());
                        return;
                    case EditPhase.Commit:
                        if (edit != null && edit.IsOpen) { edit.Cancel(); edit = null; }
                        list.ResetComponent(Index, Rotate, Component);
                        return;
                }
            }
        }
    }
}
