// Building blocks of the inspectors and the scene panel: sections (foldouts
// with localised titles), a schema-section builder over A's widget factory (sub-groups as nested foldouts, the colour
// default button, a hook for rows the inspector draws itself), the index row (◀ ▶ + dropdown with wrap),
// the clothes-state row (segmented buttons with only the supported states enabled) and small helpers. Every write
// goes through InspectorWrites (StudioOps).
using System;
using System.Collections.Generic;
using System.Linq;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Binding;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;
using UnityEngine.UIElements;
using UiField = HS2Next.UI.Core.FieldSpec;

namespace HS2Next.UI.Panels
{
    public static class InspectorUi
    {
        public const string SectionClass = "hs-section", RowClass = "hs-row", RowSelected = "hs-row--selected", Dim = "hs-text--dim";

        /// <summary>A foldout section with a localised title (class hs-section).</summary>
        public static Foldout Section(string titleKey, bool open = true, string name = null)
        {
            var f = new Foldout { value = open, name = name ?? "section-" + titleKey };
            f.AddToClassList(SectionClass);
            if (titleKey != null) Loc.Bind(f, titleKey, (e, s) => ((Foldout)e).text = s);
            return f;
        }

        /// <summary>A dimmed note line (placeholders, "not built", pending ops).</summary>
        public static Label Note(string key, params object[] args)
        {
            var l = args != null && args.Length > 0 ? new Label(L10n.Format(key, args)) : Loc.Label(key);
            l.AddToClassList(Dim);
            l.AddToClassList("hs-inspector__note");
            return l;
        }

        /// <summary>
        /// Adds the rows of a schema section to <paramref name="parent"/>: consecutive rows of another group (FieldSpec.Group
        /// ≠ section id) go into a nested foldout titled "inspector.group.&lt;group&gt;"; <paramref name="custom"/> may return the
        /// element of a row (or an empty list to skip it); else A's factory builds it with <paramref name="selector"/>.
        /// </summary>
        public static void AddSection(UiContext ctx, SchemaSection section, TargetSelector selector, VisualElement parent,
            Func<UiField, IEnumerable<VisualElement>> custom = null, FieldContext proto = null)
        {
            string group = null;
            VisualElement into = parent;
            var groups = new Dictionary<string, VisualElement>(StringComparer.Ordinal);
            foreach (var f in section.Fields)
            {
                string g = f.Group ?? section.Id;
                if (g != group)
                {
                    group = g;
                    if (g == section.Id) into = parent;
                    else if (!groups.TryGetValue(g, out into))
                    {
                        // a nested group (kinematics.fk.bones inside kinematics.fk, …)
                        var host = parent;
                        foreach (var kv in groups) if (g.StartsWith(kv.Key + ".", StringComparison.Ordinal)) host = kv.Value;
                        var fo = Section("inspector.group." + g, true, "group-" + g);
                        host.Add(fo);
                        groups[g] = into = fo;
                    }
                }
                var made = custom?.Invoke(f);
                if (made != null) { foreach (var e in made) if (e != null) into.Add(e); continue; }
                if (f.IsTemplate) continue;   // templates are expanded by the caller's custom hook
                into.Add(Row(ctx, f, selector, proto));
            }
        }

        /// <summary>One row from A's factory (+ the default button of colour rows, which the factory does not draw).</summary>
        public static VisualElement Row(UiContext ctx, UiField f, TargetSelector selector, FieldContext proto = null, string placeholder = null, string label = null)
        {
            var fc = new FieldContext
            {
                Selector = f.Target == FieldTarget.Scene ? TargetSelector.Scene : selector,
                Placeholder = placeholder ?? proto?.Placeholder,
                Label = label ?? proto?.Label,
                Count = proto?.Count,
                RefChoices = proto?.RefChoices,
                Writer = proto?.Writer,
            };
            var row = FieldWidgets.Create(ctx, f, fc);
            if ((f.Kind == FieldKind.Color || f.Kind == FieldKind.ColorHdr) && f.Default != null)
            {
                var b = new Button(() => InspectorWrites.WriteDefault(ctx, f, placeholder, row.Targets)) { text = "↺", name = "default" };
                b.AddToClassList("hs-field__default");
                Loc.Tooltip(b, "common.default");
                row.Add(b);
            }
            return row;
        }

        /// <summary>A plain button (class hs-field__button) with a localised text.</summary>
        public static Button Button(string key, Action click, string name = null)
        {
            var b = Loc.Button(key, click);
            b.AddToClassList("hs-field__button");
            if (name != null) b.name = name;
            return b;
        }

        public static VisualElement Line(params VisualElement[] items)
        {
            var row = new VisualElement();
            row.AddToClassList(RowClass);
            row.style.flexDirection = FlexDirection.Row;
            foreach (var i in items) if (i != null) row.Add(i);
            return row;
        }
    }

    /// <summary>
    /// An index row (Kind Int, Max null): ◀ ▶ with wrap and a dropdown of the positions (names when the list has them). The
    /// value is the list position for face eyebrow / eyes (InspectorValues.Shown), the raw value otherwise.
    /// </summary>
    public sealed class IndexRow : VisualElement
    {
        readonly UiContext ctx;
        readonly UiField spec;
        readonly TargetSelector selector;
        readonly Func<string, int> count;
        readonly Func<string, int, string> nameOf;
        public readonly Label LabelElement;
        public readonly Button Prev, Next;
        public readonly DropdownField Dropdown;
        int shown = -1, n = 0;
        bool mixed;
        public FieldBinding Binding { get; }
        public OpResult LastResult { get; private set; }
        public int Value => shown;

        public IndexRow(UiContext ctx, UiField spec, TargetSelector selector, Func<string, int> count, Func<string, int, string> nameOf = null)
        {
            this.ctx = ctx;
            this.spec = spec;
            this.selector = selector;
            this.count = count;
            this.nameOf = nameOf;
            name = "field-" + spec.Path;
            AddToClassList("hs-field");
            AddToClassList("hs-index");
            style.flexDirection = FlexDirection.Row;
            LabelElement = Loc.Label(spec.LabelKey);
            LabelElement.AddToClassList("hs-field__label");
            Add(LabelElement);
            Prev = new Button(() => Step(-1)) { text = "◀", name = "prev" };
            Next = new Button(() => Step(+1)) { text = "▶", name = "next" };
            Dropdown = new DropdownField(new List<string>(), 0) { name = "dropdown" };
            Dropdown.style.flexGrow = 1;
            Dropdown.RegisterValueChangedCallback(e => { if (Dropdown.index >= 0 && Dropdown.index != shown) Set(Dropdown.index); });
            Add(Prev);
            Add(Dropdown);
            Add(Next);
            Binding = ctx.Bindings.Bind(this, selector, spec.Path, (el, v) => Show(v), spec);
        }

        void Show(BoundValues v)
        {
            string primary = v.Targets.Count > 0 ? v.Targets[0] : null;
            n = primary != null ? Math.Max(1, count(primary)) : 1;
            mixed = false;
            int first = -1;
            foreach (var id in v.Targets)
            {
                var s = InspectorValues.Shown(ctx.Ops, spec, spec.Path, id);
                int p = s == null ? 0 : InspectorValues.I(s);
                if (first < 0) first = p;
                else if (p != first) mixed = true;
            }
            shown = Math.Max(0, first);
            var labels = new List<string>(n);
            for (int i = 0; i < n; i++) labels.Add(nameOf != null && primary != null ? (nameOf(primary, i) ?? i.ToString()) : i.ToString());
            Dropdown.choices = labels;
            Dropdown.SetValueWithoutNotify(mixed ? "—" : shown < labels.Count ? labels[shown] : shown.ToString());
            bool gated = spec.Gate == null || (primary != null && InspectorGates.Holds(ctx.Ops, spec.Gate, primary));
            style.display = v.Targets.Count > 0 && gated ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>◀ / ▶ with wrap (vanilla: position 0 ◀ = last; ▶ modulo the length).</summary>
        public void Step(int d)
        {
            if (n <= 0) return;
            Set(((shown + d) % n + n) % n);
        }

        public void Set(int position)
        {
            LastResult = InspectorWrites.Write(ctx, spec, null, selector.Resolve(ctx), position);
            Show(Binding.Read(ctx));   // at once: a second ◀ / ▶ before the binding update steps from the new value
            Binding.MarkDirty();
        }
    }

    /// <summary>A clothes kind row: segmented on / half / off, only the supported states enabled.</summary>
    public sealed class ClothesRow : VisualElement
    {
        readonly UiContext ctx;
        readonly UiField spec;
        readonly TargetSelector selector;
        readonly int kind;
        public readonly SegmentedButtons Buttons;
        public FieldBinding Binding { get; }
        public OpResult LastResult { get; private set; }

        public ClothesRow(UiContext ctx, UiField spec, TargetSelector selector, int kind)
        {
            this.ctx = ctx;
            this.spec = spec;
            this.selector = selector;
            this.kind = kind;
            name = "field-" + spec.Path;
            AddToClassList("hs-field");
            AddToClassList("hs-field__enum");
            style.flexDirection = FlexDirection.Row;
            var l = Loc.Label(spec.LabelKey);
            l.AddToClassList("hs-field__label");
            Add(l);
            var labels = new List<string>();
            for (int i = 0; i < spec.Choices.Count; i++) labels.Add(L10n.Get(spec.ChoiceLabelKey(i)));
            Buttons = new SegmentedButtons(labels);
            for (int i = 0; i < Buttons.Buttons.Count; i++) Loc.Bind(Buttons.Buttons[i], spec.ChoiceLabelKey(i));
            Buttons.Selected += i => Set(i);
            Add(Buttons);
            Binding = ctx.Bindings.Bind(this, selector, "chara.state.clothes", (el, v) => Show(v));
        }

        void Show(BoundValues v)
        {
            string primary = v.Targets.Count > 0 ? v.Targets[0] : null;
            int first = -1;
            bool mixed = false;
            foreach (var x in v.Values)
            {
                int s = x is int[] a && kind < a.Length ? a[kind] : 0;
                if (first < 0) first = s; else if (s != first) mixed = true;
            }
            var set = primary != null ? ctx.Ops.ClothesStatesOf(primary) : ClothesStateSet.Unknown;
            var enabled = new bool[3];
            for (int s = 0; s < 3; s++) enabled[s] = !set.Known || set.Supports(kind, s);
            // the realization shows the nearest supported state of an unsupported stored value (chara.clothesStateAdjusted)
            int display = first < 0 ? 0 : set.Known ? set.Effective(kind, first) : first;
            Buttons.SetDisplay(display, mixed, enabled);
            bool has = !set.Known || set.IsStateKind(kind);
            style.display = primary != null && has ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void Set(int state)
        {
            LastResult = InspectorWrites.Write(ctx, spec, null, selector.Resolve(ctx), state);
            Binding.MarkDirty();
        }
    }
}

namespace HS2Next.UI.Panels
{
    /// <summary>
    /// One IK target channel (pos or rot, root space) of the selected characters: a VectorField showing the stored target, or
    /// the solver's effective target while none is stored, writing through StudioOps.SetIkTargets (the op
    /// captures the missing targets). A typed component keeps each character's other components; a label drag is one entry.
    /// </summary>
    public sealed class IkTargetRow : VisualElement
    {
        readonly UiContext ctx;
        readonly HS2Next.UI.Core.FieldSpec spec;
        readonly TargetSelector selector;
        readonly string target, path;
        readonly bool rot;
        UiEdit edit;
        public readonly VectorField Field;
        public FieldBinding Binding { get; }
        public OpResult LastResult { get; private set; }

        public IkTargetRow(UiContext ctx, HS2Next.UI.Core.FieldSpec spec, TargetSelector selector, string target)
        {
            this.ctx = ctx;
            this.spec = spec;
            this.selector = selector;
            this.target = target;
            path = spec.Expand(target);
            rot = path.EndsWith(".rot", StringComparison.Ordinal);
            name = "field-" + path;
            AddToClassList("hs-field");
            Field = new VectorField(L10n.Get(spec.LabelKey), 3) { Format = spec.Format ?? "0.####" };
            Loc.Bind(Field.LabelElement, spec.LabelKey);
            Field.Edited += OnEdited;
            Add(Field);
            Binding = ctx.Bindings.Bind(this, selector, "chara.ik.targets", (el, v) => Show(v.Targets));
        }

        /// <summary>The value shown for one character: the stored target, else the effective one, else zero.</summary>
        public UnityEngine.Vector3 ValueOf(string id)
        {
            if (FieldAccessor.TryGet(ctx.Model, id, path, out var v, out _) && v is HS2Next.Save.Vec3 s) return new UnityEngine.Vector3(s.X, s.Y, s.Z);
            var real = ctx.Runtime?.FindChara(id);
            var ik = real != null && real.State == RealizationState.Built ? HS2Next.Scenes.Runtime.IkDriver.Of(real) : null;
            if (ik != null && Enum.TryParse(target, false, out HS2Next.Motion.Ik.IkTargetId t))
            {
                var e = ik.Effective(t);
                if (e.Has) return rot ? e.Rot : e.Pos;
            }
            return UnityEngine.Vector3.zero;
        }

        void Show(IReadOnlyList<string> ids)
        {
            if (ids == null || ids.Count == 0) return;
            var first = ValueOf(ids[0]);
            var mixed = new bool[4];
            for (int i = 1; i < ids.Count; i++)
            {
                var o = ValueOf(ids[i]);
                for (int k = 0; k < 3; k++) if (Math.Abs(o[k] - first[k]) > 1e-5f) mixed[k] = true;
            }
            Field.SetDisplay(first, mixed);
        }

        void OnEdited(EditPhase phase, UnityEngine.Vector4 v)
        {
            if (phase == EditPhase.Begin) { edit?.Cancel(); edit = ctx.BeginEdit(spec.OpLabel ?? "IK targets"); return; }
            if (phase == EditPhase.End) { var e = edit; edit = null; if (e != null) ctx.ShowResult(e.Commit()); Binding.MarkDirty(); return; }
            int k = Field.LastComponent;
            Write(v, k, phase == EditPhase.Typed ? "ui.typed/" + string.Join(",", selector.Resolve(ctx)) + "/" + path : null);
        }

        /// <summary>Writes the value (component <paramref name="component"/> only when ≥ 0) to every selected character.</summary>
        public OpResult Write(UnityEngine.Vector4 v, int component = -1, string mergeKey = null)
        {
            var ids = selector.Resolve(ctx);
            var o = UiContext.User(spec.OpLabel, mergeKey);
            OpResult last = OpResult.NothingToDo();
            using (var e = edit == null && ids.Count > 1 ? ctx.BeginEdit(spec.OpLabel ?? "IK targets", mergeKey) : null)
            {
                foreach (var id in ids)
                {
                    var cur = ValueOf(id);
                    var nv = component >= 0 ? cur : new UnityEngine.Vector3(v.x, v.y, v.z);
                    if (component >= 0) nv[component] = v[component];
                    if (!Enum.TryParse(target, false, out HS2Next.Motion.Ik.IkTargetId t)) return OpResult.BadArgs(path, "an IK target");
                    var ed = rot ? new IkTargetEdit(null, nv) : new IkTargetEdit(nv, null);
                    last = ctx.Ops.SetIkTargets(id, new Dictionary<HS2Next.Motion.Ik.IkTargetId, IkTargetEdit> { [t] = ed }, IkSpace.Local, o);
                    if (!last.Ok) break;
                }
                if (e != null) { if (last.Ok) last = e.Commit(); else e.Cancel(); }
            }
            Binding.MarkDirty();
            return LastResult = ctx.ShowResult(last);
        }
    }
}
