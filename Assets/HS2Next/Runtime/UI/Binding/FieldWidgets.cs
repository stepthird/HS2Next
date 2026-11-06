// The widget factory from the inspector schema: FieldWidgets.Create(ctx, spec, fc) turns
// a FieldSpec row into a FieldRow — NumberSlider (Float / Int: slider range, typed clamp, ↺ default), Toggle,
// ColorSwatch + ColorPicker (Color, ColorHdr with the intensity path), VectorField (Vec2 / Vec3, or one component),
// SegmentedButtons / DropdownField (Enum), RefPicker (Ref, Node), FilePicker (File), TextField (Text), Button — bound to
// the model through a FieldBinding on the row's targets and writing through FieldWriters (StudioOps). Interaction
// (U5, U29): a drag = one transaction ("begin on pointer capture, previews inside, commit on release, Esc cancels");
// typed values commit on Enter / blur with the schema clamp and the merge key ui.typed/<sorted ids>/<path>; toggles,
// dropdowns, buttons = one entry each. Mixed values show "—"; a mixed vector component is left as stored unless typed.
//
//   var row = FieldWidgets.Create(ctx, spec, new FieldContext { Selector = TargetSelector.Selection(NodeKind.Light) });
//   section.Add(row);    // row.Binding is live while the row is in a panel
using System;
using System.Collections.Generic;
using System.Linq;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Binding
{
    /// <summary>Where a schema row is used.</summary>
    public sealed class FieldContext
    {
        /// <summary>The targets (default: every selected node of any kind; scene rows: the scene).</summary>
        public TargetSelector Selector;
        /// <summary>The value of the row's placeholder ({slot}, {bone}, {target}, {k}).</summary>
        public string Placeholder;
        /// <summary>A writer instead of FieldWriters' routing.</summary>
        public FieldWriter Writer;
        /// <summary>Gate predicate (SchemaGates names; null = rows always shown).</summary>
        public Func<string, IReadOnlyList<string>, bool> Gate;
        /// <summary>Per-node defaults ("@name" rows): (name, target id) → value or null.</summary>
        public Func<string, string, object> NamedDefault;
        /// <summary>Choices of Ref / Node rows (value, label).</summary>
        public Func<FieldSpec, IReadOnlyList<(string value, string label)>> RefChoices;
        /// <summary>Label text instead of L10n.Get(spec.LabelKey) (e.g. a bone name).</summary>
        public string Label;
        /// <summary>Index rows with Max null: the runtime count of choices (eyebrow positions, hands, item patterns).</summary>
        public Func<FieldSpec, IReadOnlyList<string>, int?> Count;
    }

    /// <summary>A schema row bound to the model.</summary>
    public sealed class FieldRow : VisualElement
    {
        public const string UssClass = "hs-field";
        public FieldSpec Spec { get; internal set; }
        public string Path { get; internal set; }
        public FieldBinding Binding { get; internal set; }
        /// <summary>The control (NumberSlider, Toggle, ColorSwatch, VectorField, SegmentedButtons, DropdownField, RefPicker, FilePicker, TextField, Button).</summary>
        public VisualElement Control { get; internal set; }
        internal FieldContext Fc;
        internal UiContext Ctx;
        internal UiEdit Edit;
        /// <summary>The last write's result (tests).</summary>
        public OpResult LastResult { get; internal set; }
        /// <summary>The values shown last.</summary>
        public BoundValues Shown { get; internal set; }

        public FieldRow() { AddToClassList(UssClass); }

        public IReadOnlyList<string> Targets => (Binding?.Selector ?? Fc?.Selector)?.Resolve(Ctx) ?? (IReadOnlyList<string>)Array.Empty<string>();
    }

    public static class FieldWidgets
    {
        /// <summary>The global gate evaluator (package I sets it from SchemaGates and the runtime's features); null = all shown.</summary>
        public static Func<UiContext, string, IReadOnlyList<string>, bool> GateEvaluator;
        /// <summary>The global per-node default resolver ("@name", target id) (package I sets it from ItemFeatures.defaults).</summary>
        public static Func<UiContext, string, string, object> DefaultResolver;
        /// <summary>The global choice provider for Ref / Node rows (package I / C set it).</summary>
        public static Func<UiContext, FieldSpec, IReadOnlyList<(string value, string label)>> RefProvider;

        public static FieldRow Create(UiContext ctx, FieldSpec spec, FieldContext fc = null)
        {
            if (spec == null) throw new ArgumentNullException(nameof(spec));
            fc ??= new FieldContext();
            var row = new FieldRow { Spec = spec, Ctx = ctx, Fc = fc, name = "field-" + (spec.Path ?? spec.Action ?? spec.LabelKey) };
            row.Path = spec.IsTemplate ? spec.Expand(fc.Placeholder ?? "") : spec.Path;
            var selector = fc.Selector ?? (spec.Target == FieldTarget.Scene ? TargetSelector.Scene : TargetSelector.Selection());
            string label = fc.Label ?? (spec.LabelKey != null ? L10n.Get(spec.LabelKey) : row.Path);
            switch (spec.Kind)
            {
                case FieldKind.Float:
                case FieldKind.Int: Number(row, label); break;
                case FieldKind.Bool: Bool(row, label); break;
                case FieldKind.Color:
                case FieldKind.ColorHdr: ColorRow(row, label); break;
                case FieldKind.Vec2:
                case FieldKind.Vec3: Vector(row, label); break;
                case FieldKind.Enum: EnumRow(row, label); break;
                case FieldKind.Ref:
                case FieldKind.Node: RefRow(row, label); break;
                case FieldKind.File: FileRow(row, label); break;
                case FieldKind.Text: TextRow(row, label); break;
                case FieldKind.Button: ButtonRow(row, label); break;
            }
            if (spec.LabelKey != null && fc.Label == null && row.Control != null) LocLabel(row);
            if (spec.Kind != FieldKind.Button && row.Path != null && spec.Target != FieldTarget.Option)
            {
                var b = new FieldBinding { Selector = selector, Path = row.Path, Spec = spec, Apply = (el, v) => Show(row, v) };
                row.Binding = b;
                ctx.Bindings.Bind(row, b);
            }
            else if (spec.Target == FieldTarget.Option && spec.Kind != FieldKind.Button && !string.IsNullOrEmpty(row.Path))
            {
                // an option row shows its option; a button of an option table (the screenshot dialog's Take) has no option
                ShowOption(row);
                // listened to only while the row is on a panel: a row dropped with its inspector must not stay on the options' event
                void OnOption(string k) { if (k == row.Path) ShowOption(row); }
                row.RegisterCallback<AttachToPanelEvent>(_ => { ctx.Options.Changed -= OnOption; ctx.Options.Changed += OnOption; ShowOption(row); });
                row.RegisterCallback<DetachFromPanelEvent>(_ => ctx.Options.Changed -= OnOption);
            }
            else row.Binding = new FieldBinding { Selector = selector };
            return row;
        }

        // ---------------- display

        static void LocLabel(FieldRow row)
        {
            var lbl = row.Control.Q<Label>(className: "hs-number__label") ?? row.Control.Q<Label>(className: "hs-vector__label")
                ?? row.Control.Q<Label>(className: "hs-color__label") ?? row.Control.Q<Label>(className: "hs-ref__label")
                ?? row.Control.Q<Label>(className: "hs-file__label") ?? row.Control.Q<Label>(className: "hs-field__label");
            if (lbl != null) Loc.Bind(lbl, row.Spec.LabelKey);
            else if (row.Control is Button b) Loc.Bind(b, row.Spec.LabelKey);
            else if (row.Control is Toggle t) Loc.Bind(t, row.Spec.LabelKey);
            else if (row.Control is DropdownField d) Loc.Bind(d, row.Spec.LabelKey, (e, s) => ((DropdownField)e).label = s);
            else if (row.Control is TextField tf) Loc.Bind(tf, row.Spec.LabelKey, (e, s) => ((TextField)e).label = s);
        }

        static void ShowOption(FieldRow row)
        {
            var v = row.Ctx.Options.Get(row.Path);
            Show(row, new BoundValues { Targets = new string[] { null }, Values = new[] { v }, Value = v, Mixed = false, Empty = v == null });
        }

        static void Show(FieldRow row, BoundValues v)
        {
            row.Shown = v;
            var spec = row.Spec;
            bool gated = true;
            var gate = row.Fc.Gate ?? (GateEvaluator == null ? null : (Func<string, IReadOnlyList<string>, bool>)((g, t) => GateEvaluator(row.Ctx, g, t)));
            if (spec.Gate != null && gate != null) gated = gate(spec.Gate, v.Targets);
            row.style.display = gated && !(v.Empty && spec.Target != FieldTarget.Option && v.Targets.Count == 0) ? DisplayStyle.Flex : DisplayStyle.None;
            if (spec.DisabledBy != null && v.Targets.Count > 0)
            {
                bool off = v.Targets.Any(id => FieldAccessor.TryGet(row.Ctx.Model, id, spec.DisabledBy, out var dv, out _) && dv is bool db && db);
                row.SetEnabled(!off);
            }
            switch (row.Control)
            {
                case NumberSlider ns:
                    if (spec.Max == null && spec.Kind == FieldKind.Int && row.Fc.Count != null)
                    {
                        int? n = row.Fc.Count(spec, v.Targets);
                        if (n.HasValue && ns.Slider != null) ns.Slider.highValue = Math.Max(0, n.Value - 1);
                    }
                    ns.SetDisplay(FieldValues.ToDouble(v.Value), v.Mixed);
                    break;
                case Toggle t:
                    bool bv = v.Value is bool b && b;
                    t.SetValueWithoutNotify(spec.Inverted ? !bv : bv);
                    t.showMixedValue = v.Mixed;
                    break;
                case ColorSwatch cs:
                    cs.SetDisplay(FieldValues.ToColor(v.Value) ?? Color.white, v.Mixed);
                    break;
                case VectorField vf:
                    FieldValues.TryVector(v.Value, out var vec, out _);
                    var mixed = new bool[4];
                    if (v.Mixed)
                        for (int c = 0; c < 4; c++)
                        {
                            object first = null; bool any = false;
                            foreach (var x in v.Values)
                            {
                                if (!FieldValues.TryVector(x, out var xv, out _)) continue;
                                if (!any) { first = xv[c]; any = true; }
                                else if (Math.Abs((float)first - xv[c]) > 1e-6f) { mixed[c] = true; break; }
                            }
                        }
                    vf.SetDisplay(vec, mixed);
                    break;
                case SegmentedButtons sb:
                    sb.SetDisplay(ChoiceIndex(spec, v.Value), v.Mixed);
                    break;
                case DropdownField dd:
                    int ci = ChoiceIndex(spec, v.Value);
                    dd.SetValueWithoutNotify(v.Mixed ? "—" : ci >= 0 && ci < dd.choices.Count ? dd.choices[ci] : (v.Value as string ?? ""));
                    break;
                case RefPicker rp:
                    string s = v.Value as string;
                    var lab = s;
                    var choices = Choices(row);
                    foreach (var c in choices) if (c.value == s) { lab = c.label; break; }
                    rp.SetDisplay(s, lab, v.Mixed);
                    break;
                case FilePicker fp:
                    fp.SetDisplay(FileText(v.Value), v.Mixed);
                    break;
                case TextField tf:
                    if (tf.panel?.focusController?.focusedElement != tf) tf.SetValueWithoutNotify(v.Mixed ? "" : v.Value as string ?? "");
                    tf.textEdition.placeholder = v.Mixed ? "—" : spec.Nullable ? L10n.Get("common.default") : "";
                    break;
            }
        }

        static string FileText(object v)
        {
            if (v == null) return null;
            if (v is string s) return s;
            // a scene file reference (FileRef): the referenced path (hs2 / user), or "embedded" for a res copy
            if (v is HS2Next.Save.FileRef fr)
                return fr.Kind == HS2Next.Save.FileRef.Kinds.Res ? L10n.Get("file.embedded") : fr.Value;
            var f = v.GetType().GetField("Path") ?? v.GetType().GetField("File");
            return f?.GetValue(v) as string ?? v.ToString();
        }

        static int ChoiceIndex(FieldSpec spec, object v)
        {
            if (spec.Choices == null || v == null) return -1;
            if (v is string s) { for (int i = 0; i < spec.Choices.Count; i++) if (spec.Choices[i] == s) return i; return -1; }
            int iv = (int)Math.Round(FieldValues.ToDouble(v));
            if (spec.ChoiceValues != null) { for (int i = 0; i < spec.ChoiceValues.Count; i++) if (spec.ChoiceValues[i] == iv) return i; return -1; }
            return iv;
        }

        static IReadOnlyList<(string value, string label)> Choices(FieldRow row) =>
            row.Fc.RefChoices?.Invoke(row.Spec) ?? RefProvider?.Invoke(row.Ctx, row.Spec) ?? (IReadOnlyList<(string, string)>)Array.Empty<(string, string)>();

        // ---------------- writes

        /// <summary>Writes a widget value to every target (converted to the model type; clamps are the caller's).</summary>
        public static OpResult Write(FieldRow row, object widgetValue, EditPhase phase)
        {
            var ctx = row.Ctx;
            var spec = row.Spec;
            var targets = row.Targets;
            if (spec.Target != FieldTarget.Option && targets.Count == 0) return OpResult.NothingToDo();
            // Esc took the drag back: a late preview writes nothing
            if (phase == EditPhase.Preview && row.Edit != null && !row.Edit.IsOpen) return OpResult.NothingToDo();
            // Begin / End of a drag: the transaction
            if (phase == EditPhase.Begin)
            {
                row.Edit?.Cancel();
                row.Edit = ctx.BeginEdit(spec.OpLabel ?? "Edit");
                return OpResult.Success();
            }
            if (phase == EditPhase.End)
            {
                var e = row.Edit;
                row.Edit = null;
                var r = e != null ? e.Commit() : OpResult.NothingToDo();
                row.Binding?.MarkDirty();
                return row.LastResult = ctx.ShowResult(r);
            }
            string mergeKey = phase == EditPhase.Typed ? "ui.typed/" + string.Join(",", targets.Where(t => t != null).OrderBy(t => t, StringComparer.Ordinal)) + "/" + row.Path : null;
            var o = UiContext.User(string.IsNullOrEmpty(spec.OpLabel) ? null : spec.OpLabel, mergeKey);
            OpResult res;
            if (spec.Target == FieldTarget.Option)
            {
                res = FieldWriters.Write(ctx, new FieldWrite { Spec = spec, Path = row.Path, Targets = new string[] { null }, Value = widgetValue, Options = o });
                return row.LastResult = res;
            }
            // one value per target when a component or a mixed vector keeps the other components
            bool perTarget = spec.Component >= 0 || row.Control is VectorField;
            if (!perTarget)
            {
                var leaf = LeafType(ctx, targets[0], row.Path);
                res = Route(row, targets, FieldValues.ToModel(widgetValue, leaf), o);
            }
            else
            {
                using (var edit = row.Edit == null ? ctx.BeginEdit(spec.OpLabel ?? "Edit", mergeKey) : null)
                {
                    res = OpResult.NothingToDo();
                    foreach (var id in targets)
                    {
                        if (!FieldAccessor.TryGet(ctx.Model, id, row.Path, out var cur, out _)) continue;
                        object nv;
                        if (spec.Component >= 0) nv = FieldValues.WithComponent(cur, spec.Component, FieldValues.ToDouble(widgetValue));
                        else
                        {
                            var vf = (VectorField)row.Control;
                            nv = cur;
                            int k = vf.LastComponent;
                            if (k >= 0) nv = FieldValues.WithComponent(cur, k, ((Vector4)widgetValue)[k]);
                            else nv = FieldValues.ToModel(widgetValue, cur?.GetType());
                        }
                        res = Route(row, new[] { id }, nv, o);
                        if (!res.Ok) break;
                    }
                    if (edit != null) { if (res.Ok) res = edit.Commit(); else edit.Cancel(); }
                }
            }
            if (row.Edit == null) row.Binding?.MarkDirty();
            return row.LastResult = ctx.ShowResult(res);
        }

        static OpResult Route(FieldRow row, IReadOnlyList<string> targets, object value, ExecuteOptions o)
        {
            var w = new FieldWrite { Spec = row.Spec, Path = row.Path, Targets = targets, Value = value, Options = o };
            return row.Fc.Writer != null ? row.Fc.Writer(row.Ctx, w) : FieldWriters.Write(row.Ctx, w);
        }

        static Type LeafType(UiContext ctx, string id, string path)
        {
            try { return FieldAccessor.LeafType(ctx.Model, id, path); }
            catch (Exception) { return null; }
        }

        static object DefaultOf(FieldRow row, string target)
        {
            var d = row.Spec.Default;
            if (d is string s && s.StartsWith("@", StringComparison.Ordinal))
                return row.Fc.NamedDefault?.Invoke(s, target) ?? DefaultResolver?.Invoke(row.Ctx, s, target);
            return d;
        }

        // ---------------- controls

        static void Number(FieldRow row, string label)
        {
            var spec = row.Spec;
            var ns = new NumberSlider(label, spec.Min, spec.Max)
            {
                TypedMin = spec.TypedMin, TypedMax = spec.TypedMax, Whole = spec.Whole || spec.Kind == FieldKind.Int,
                Format = spec.Format ?? (spec.Kind == FieldKind.Int ? "0" : "0.###"),
            };
            if (spec.Default != null)
                ns.DefaultValue = () =>
                {
                    var t = row.Targets;
                    var d = DefaultOf(row, t.Count > 0 ? t[0] : null);
                    if (d is double[] arr && spec.Component >= 0 && spec.Component < arr.Length) return arr[spec.Component];
                    return d == null || d is double[] ? (double?)null : FieldValues.ToDouble(d);
                };
            ns.Edited += (phase, v) => Write(row, v, phase);
            row.Control = ns;
            row.Add(ns);
        }

        static void Bool(FieldRow row, string label)
        {
            var t = new Toggle(label) { name = "toggle" };
            t.AddToClassList("hs-field__toggle");
            t.RegisterValueChangedCallback(e => Write(row, row.Spec.Inverted ? !e.newValue : e.newValue, EditPhase.Commit));
            row.Control = t;
            row.Add(t);
        }

        static void ColorRow(FieldRow row, string label)
        {
            var spec = row.Spec;
            var sw = new ColorSwatch(label) { Alpha = spec.ColorAlpha };
            sw.Clicked += () => OpenPicker(row, sw);
            row.Control = sw;
            row.Add(sw);
        }

        static void OpenPicker(FieldRow row, ColorSwatch sw)
        {
            var ctx = row.Ctx;
            var spec = row.Spec;
            float intensity = 0;
            var targets = row.Targets;
            if (spec.Kind == FieldKind.ColorHdr && spec.IntensityPath != null && targets.Count > 0 &&
                FieldAccessor.TryGet(ctx.Model, targets[0], spec.IntensityPath, out var iv, out _)) intensity = (float)FieldValues.ToDouble(iv);
            var picker = new ColorPicker(sw.Value, spec.ColorAlpha, spec.Kind == FieldKind.ColorHdr, intensity);
            var layer = ctx.Shell?.Overlay ?? row.panel?.visualTree;
            if (layer == null) return;
            var edit = ctx.BeginEdit(spec.OpLabel ?? "Edit");
            row.Edit = edit;
            picker.Changed += (c, inten) =>
            {
                if (!spec.ColorAlpha) c.a = spec.Kind == FieldKind.ColorHdr ? 1 : sw.Value.a;
                var r = Route(row, row.Targets, new HS2Next.Save.Color4(c.r, c.g, c.b, c.a), UiContext.User(spec.OpLabel));
                if (spec.Kind == FieldKind.ColorHdr && spec.IntensityPath != null && r.Ok)
                    Route(new FieldRow { Spec = spec, Path = spec.IntensityPath, Ctx = ctx, Fc = row.Fc }, row.Targets, inten, UiContext.User(spec.OpLabel));
            };
            picker.Closed += commit =>
            {
                row.Edit = null;
                if (commit) ctx.ShowResult(edit.Commit()); else edit.Cancel();
                row.Binding?.MarkDirty();
            };
            // click outside = commit
            var blocker = new VisualElement { name = "color-blocker" };
            blocker.style.position = Position.Absolute;
            blocker.style.left = 0; blocker.style.top = 0; blocker.style.right = 0; blocker.style.bottom = 0;
            blocker.RegisterCallback<PointerDownEvent>(e => { picker.Close(true); e.StopPropagation(); });
            picker.Closed += _ => blocker.RemoveFromHierarchy();
            picker.style.position = Position.Absolute;
            var wb = sw.Button.worldBound;
            picker.style.left = Mathf.Max(0, wb.xMin - 260);
            picker.style.top = wb.yMax;
            // keep inside the layer once laid out, as PopupMenu does (v0.2: below a swatch in the lower half of a 1280 x 720 panel
            // the picker's fields and OK button were below the window)
            picker.RegisterCallback<GeometryChangedEvent>(_ => { float maxY = layer.layout.height - picker.layout.height - 2; if (maxY > 0 && picker.layout.y > maxY + 0.5f) picker.style.top = maxY; });
            layer.Add(blocker);
            layer.Add(picker);
            UiDefer.Frames(2, () => { if (picker.panel != null) picker.Focus(); });
        }

        static void Vector(FieldRow row, string label)
        {
            var spec = row.Spec;
            if (spec.Component >= 0)
            {
                Number(row, label);
                return;
            }
            var vf = new VectorField(label, spec.Kind == FieldKind.Vec2 ? 2 : 3) { Format = spec.Format ?? "0.#####" };
            vf.Edited += (phase, v) =>
            {
                if (phase == EditPhase.Typed && (spec.TypedMin != null || spec.TypedMax != null || spec.Wrap360))
                    for (int i = 0; i < 4; i++) v[i] = (float)spec.ClampTyped(v[i]);
                Write(row, v, phase);
            };
            row.Control = vf;
            row.Add(vf);
        }

        static void EnumRow(FieldRow row, string label)
        {
            var spec = row.Spec;
            var labels = new List<string>();
            for (int i = 0; i < (spec.Choices?.Count ?? 0); i++) labels.Add(L10n.Get(spec.ChoiceLabelKey(i)));
            bool stringEnum = false;
            if (labels.Count <= 4)
            {
                var wrap = new VisualElement();
                wrap.AddToClassList("hs-field__enum");
                var l = new Label(label);
                l.AddToClassList("hs-field__label");
                wrap.Add(l);
                var sb = new SegmentedButtons(labels);
                sb.Selected += i => Write(row, ChoiceValue(row, i, ref stringEnum), EditPhase.Commit);
                wrap.Add(sb);
                row.Add(wrap);
                row.Control = sb;
                for (int i = 0; i < sb.Buttons.Count; i++) Loc.Bind(sb.Buttons[i], spec.ChoiceLabelKey(i));
                Loc.Bind(l, spec.LabelKey ?? "");
            }
            else
            {
                var dd = new DropdownField(label, labels, 0) { name = "dropdown" };
                dd.RegisterValueChangedCallback(e => { int i = dd.index; if (i >= 0) Write(row, ChoiceValue(row, i, ref stringEnum), EditPhase.Commit); });
                row.Control = dd;
                row.Add(dd);
            }
        }

        static object ChoiceValue(FieldRow row, int i, ref bool stringEnum)
        {
            // option rows store the choice string (the screenshot dialog's size mode / supersampling; Phase 3.6, found by UI2)
            if (row.Spec.Target == FieldTarget.Option) { stringEnum = true; return row.Spec.ChoiceValue(i, true); }
            var t = row.Targets;
            Type leaf = t.Count > 0 ? LeafType(row.Ctx, t[0], row.Path) : null;
            stringEnum = leaf == typeof(string);
            return row.Spec.ChoiceValue(i, stringEnum);
        }

        static void RefRow(FieldRow row, string label)
        {
            var rp = new RefPicker(label) { Choices = () => Choices(row) };
            rp.Picked += v => Write(row, v, EditPhase.Commit);
            row.Control = rp;
            row.Add(rp);
        }

        static void FileRow(FieldRow row, string label)
        {
            var fp = new FilePicker(label);
            fp.Picked += p =>
            {
                // FileRef rows: a path string is converted by the op layer's setter when the slot is a string;
                // otherwise package I registers a writer for the path
                Write(row, p, EditPhase.Commit);
            };
            row.Control = fp;
            row.Add(fp);
        }

        static void TextRow(FieldRow row, string label)
        {
            var tf = new TextField(label) { isDelayed = true, name = "text" };
            tf.RegisterValueChangedCallback(e => Write(row, string.IsNullOrEmpty(e.newValue) && row.Spec.Nullable ? null : e.newValue, EditPhase.Commit));
            row.Control = tf;
            row.Add(tf);
        }

        static void ButtonRow(FieldRow row, string label)
        {
            var b = new Button(() => row.LastResult = row.Ctx.ShowResult(ButtonActions.Run(row.Ctx, row.Spec.Action, row.Targets))) { text = label, name = "button" };
            b.AddToClassList("hs-field__button");
            row.Control = b;
            row.Add(b);
        }
    }
}
