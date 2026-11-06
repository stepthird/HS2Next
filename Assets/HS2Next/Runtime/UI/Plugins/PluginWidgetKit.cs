// The host widgets for plugins, built on value callbacks instead of model bindings: the number slider, vector field,
// toggle, choice buttons / dropdown, colour swatch with its picker, reference and file pickers, text field, button,
// attach-point picker, the Maker thumbnail list and push grid. The plugins' own elements (IUiWidgets) and the declarative
// forms are drawn by these builders, so both look and behave as the host's inspector rows. Phases: a drag reports Preview
// values and ends with Commit, or with Cancel (the value before the drag) when Esc is pressed while it runs (the drag takes
// Esc before the shortcut router); a typed value, a reset, a toggle, a choice and a pick report one Commit; the colour
// picker previews while it moves and commits on OK, Enter or a click outside, and cancels on Esc or Cancel. The Maker list
// and push grid hand their pick to the callback and change no character; the attach-point picker's keys do the same.
// Element names: the widget "<name>-widget" (a list, grid or picker: "<name>-widget" too), its label "<name>-label", and
// "<name>-field", "-slider", "-reset" (numbers), "-c0".."-c3" (vectors), "-toggle", "-seg-<i>" / "-dropdown" (choices),
// "-swatch" (colours; the tooltip is the colour as #RRGGBBAA), "-ref", "-file" / "-browse" / "-clear", "-text", "-button",
// "-current" (attach point, list).
using System;
using System.Collections.Generic;
using System.Globalization;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Binding;
using HS2Next.UI.Core;
using HS2Next.UI.Panels;
using HS2Next.UI.Widgets;
using UnityEngine;
using UnityEngine.UIElements;
using ApiPhase = HS2Next.Api.Ui.EditPhase;
using HostPhase = HS2Next.UI.Widgets.EditPhase;

namespace HS2Next.UI.Plugins
{
    /// <summary>A colour with an HDR intensity (ColorHdr rows).</summary>
    public struct HdrColor
    {
        public Color Color;
        public float Intensity;
        public HdrColor(Color c, float i) { Color = c; Intensity = i; }
    }

    /// <summary>A host widget made for a plugin (IUiWidget): its element, a display setter that calls nothing back, its enabled state.</summary>
    public sealed class HostWidget : HS2Next.Api.Unity.IUiWidget
    {
        readonly Action<object, bool> show;

        public HostWidget(VisualElement element, Action<object, bool> show)
        {
            Element = element;
            this.show = show;
        }

        public VisualElement Element { get; }

        /// <summary>The control that holds the value (the slider row, the toggle, the list), for tests.</summary>
        public VisualElement Control { get; internal set; }

        public void SetValue(object value, bool mixed = false) => show?.Invoke(value, mixed);

        public bool Enabled
        {
            get => Element.enabledSelf;
            set => Element.SetEnabled(value);
        }
    }

    /// <summary>The builders of the host widgets on value callbacks.</summary>
    public static class PluginWidgetKit
    {
        public const string WidgetClass = "hs-plugin-widget";

        /// <summary>Esc during a drag: the drag takes the key (trickle-down on the panel root) and reports its cancel.</summary>
        sealed class Drag
        {
            VisualElement root;
            EventCallback<KeyDownEvent> esc;
            public bool Active, Cancelled;

            public void Begin(VisualElement owner, Action cancel)
            {
                End();
                Active = true;
                Cancelled = false;
                root = owner?.panel?.visualTree;
                if (root == null) return;
                esc = e =>
                {
                    if (e.keyCode != KeyCode.Escape || !Active || Cancelled) return;
                    Cancelled = true;
                    e.StopPropagation();
                    cancel();
                };
                root.RegisterCallback(esc, TrickleDown.TrickleDown);
            }

            public void End()
            {
                if (root != null && esc != null) root.UnregisterCallback(esc, TrickleDown.TrickleDown);
                root = null;
                esc = null;
                Active = false;
            }
        }

        /// <summary>The label text of a key (null key: the fallback text).</summary>
        public static string Text(string key, string fallback = "") => key != null ? L10n.Get(key) : fallback ?? "";

        static void Bind(Label l, string key)
        {
            if (key != null) Loc.Bind(l, key);
        }

        static double ToDouble(object v) => v == null ? 0 : Convert.ToDouble(v, CultureInfo.InvariantCulture);

        // ------------------------------------------------------------ numbers

        /// <summary>
        /// A number slider with a field (a slider only when both ends are set and max &gt; min), the reset button when
        /// <paramref name="reset"/> gives a value; <paramref name="changed"/> gets (value, phase, typed).
        /// </summary>
        public static HostWidget Number(string name, string labelKey, double? min, double? max, bool whole, string format, Func<double?> reset,
            Action<double, ApiPhase, bool> changed)
        {
            var ns = new NumberSlider(Text(labelKey), min, max) { Whole = whole, Format = format ?? (whole ? "0" : "0.###"), DefaultValue = reset };
            ns.name = name + "-widget";
            ns.AddToClassList(WidgetClass);
            ns.LabelElement.name = name + "-label";
            Bind(ns.LabelElement, labelKey);
            if (ns.Slider != null) ns.Slider.name = name + "-slider";
            ns.Field.name = name + "-field";
            ns.ResetButton.name = name + "-reset";
            var drag = new Drag();
            double start = 0, last = 0;
            ns.Edited += (phase, v) =>
            {
                switch (phase)
                {
                    case HostPhase.Begin:
                        start = last = v;
                        drag.Begin(ns, () =>
                        {
                            changed(start, ApiPhase.Cancel, false);
                            ns.SetDisplay(start, false);
                        });
                        break;
                    case HostPhase.Preview:
                        if (!drag.Active) { changed(v, ApiPhase.Commit, false); break; }
                        if (drag.Cancelled) break;
                        last = v;
                        changed(v, ApiPhase.Preview, false);
                        break;
                    case HostPhase.End:
                        if (!drag.Active) break;
                        bool cancelled = drag.Cancelled;
                        drag.End();
                        if (cancelled) ns.SetDisplay(start, false);
                        else changed(last, ApiPhase.Commit, false);
                        break;
                    case HostPhase.Commit:
                        changed(v, ApiPhase.Commit, false);
                        break;
                    case HostPhase.Typed:
                        changed(v, ApiPhase.Commit, true);
                        break;
                }
            };
            ns.RegisterCallback<DetachFromPanelEvent>(_ => drag.End());
            return new HostWidget(ns, (v, mixed) => ns.SetDisplay(ToDouble(v), mixed)) { Control = ns };
        }

        /// <summary>A vector field of 2–4 components; <paramref name="changed"/> gets (value, phase, typed).</summary>
        public static HostWidget Vector(string name, string labelKey, int components, string format, Action<Vector4, ApiPhase, bool> changed)
        {
            var vf = new VectorField(Text(labelKey), components) { Format = format ?? "0.#####" };
            vf.name = name + "-widget";
            vf.AddToClassList(WidgetClass);
            vf.LabelElement.name = name + "-label";
            Bind(vf.LabelElement, labelKey);
            for (int i = 0; i < vf.Fields.Length; i++) vf.Fields[i].name = name + "-c" + i;
            var drag = new Drag();
            Vector4 start = default, last = default;
            vf.Edited += (phase, v) =>
            {
                switch (phase)
                {
                    case HostPhase.Begin:
                        start = last = v;
                        drag.Begin(vf, () =>
                        {
                            changed(start, ApiPhase.Cancel, false);
                            vf.SetDisplay(start, null);
                        });
                        break;
                    case HostPhase.Preview:
                        if (!drag.Active) { changed(v, ApiPhase.Commit, false); break; }
                        if (drag.Cancelled) break;
                        last = v;
                        changed(v, ApiPhase.Preview, false);
                        break;
                    case HostPhase.End:
                        if (!drag.Active) break;
                        bool cancelled = drag.Cancelled;
                        drag.End();
                        if (cancelled) vf.SetDisplay(start, null);
                        else changed(last, ApiPhase.Commit, false);
                        break;
                    default:
                        changed(v, ApiPhase.Commit, phase == HostPhase.Typed);
                        break;
                }
            };
            vf.RegisterCallback<DetachFromPanelEvent>(_ => drag.End());
            return new HostWidget(vf, (v, mixed) =>
            {
                var vec = v is Vector4 v4 ? v4 : v is Vector3 v3 ? (Vector4)v3 : v is Vector2 v2 ? (Vector4)v2 : Vector4.zero;
                vf.SetDisplay(vec, mixed ? new[] { true, true, true, true } : null);
            }) { Control = vf };
        }

        // ------------------------------------------------------------ toggles, choices, text, buttons

        public static HostWidget Toggle(string name, string labelKey, Action<bool> changed)
        {
            var t = new Toggle(Text(labelKey)) { name = name + "-toggle" };
            t.AddToClassList("hs-field__toggle");
            t.AddToClassList(WidgetClass);
            if (labelKey != null) Loc.Bind(t, labelKey);
            t.RegisterValueChangedCallback(e => changed(e.newValue));
            return new HostWidget(t, (v, mixed) =>
            {
                t.SetValueWithoutNotify(v is bool b && b);
                t.showMixedValue = mixed;
            }) { Control = t };
        }

        /// <summary>
        /// Segmented buttons (up to four choices) or a dropdown over <paramref name="labels"/> (key, text: the key is bound
        /// when given, else the text is shown); <paramref name="changed"/> gets the picked index.
        /// </summary>
        public static HostWidget Choice(string name, string labelKey, IReadOnlyList<(string key, string text)> labels, Action<int> changed)
        {
            var texts = new List<string>();
            foreach (var l in labels) texts.Add(l.key != null ? L10n.Get(l.key) : l.text ?? "");
            if (texts.Count <= 4)
            {
                var wrap = new VisualElement { name = name + "-widget" };
                wrap.AddToClassList("hs-field__enum");
                wrap.AddToClassList(WidgetClass);
                var lab = new Label(Text(labelKey)) { name = name + "-label" };
                lab.AddToClassList("hs-field__label");
                Bind(lab, labelKey);
                wrap.Add(lab);
                var sb = new SegmentedButtons(texts);
                sb.name = name + "-choices";
                for (int i = 0; i < sb.Buttons.Count; i++)
                {
                    sb.Buttons[i].name = name + "-seg-" + i;
                    if (labels[i].key != null) Loc.Bind(sb.Buttons[i], labels[i].key);
                }
                sb.Selected += i => { sb.SetDisplay(i, false); changed(i); };
                wrap.Add(sb);
                return new HostWidget(wrap, (v, mixed) => sb.SetDisplay(v == null ? -1 : (int)ToDouble(v), mixed)) { Control = sb };
            }
            var dd = new DropdownField(Text(labelKey), texts, 0) { name = name + "-dropdown" };
            dd.AddToClassList(WidgetClass);
            if (labelKey != null) Loc.Bind(dd, labelKey, (e, s) => ((DropdownField)e).label = s);
            dd.RegisterValueChangedCallback(e => { if (dd.index >= 0) changed(dd.index); });
            return new HostWidget(dd, (v, mixed) =>
            {
                int i = v == null ? -1 : (int)ToDouble(v);
                dd.SetValueWithoutNotify(mixed ? "—" : i >= 0 && i < texts.Count ? texts[i] : "");
            }) { Control = dd };
        }

        /// <summary>A text field committing on Enter or when it loses focus (empty = null when <paramref name="nullable"/>).</summary>
        public static HostWidget Text(string name, string labelKey, bool nullable, Action<string> changed)
        {
            var tf = new TextField(Text(labelKey)) { isDelayed = true, name = name + "-text" };
            tf.AddToClassList(WidgetClass);
            if (labelKey != null) Loc.Bind(tf, labelKey, (e, s) => ((TextField)e).label = s);
            tf.RegisterValueChangedCallback(e => changed(string.IsNullOrEmpty(e.newValue) && nullable ? null : e.newValue));
            return new HostWidget(tf, (v, mixed) =>
            {
                if (tf.panel?.focusController?.focusedElement != tf) tf.SetValueWithoutNotify(mixed ? "" : v as string ?? "");
                tf.textEdition.placeholder = mixed ? "—" : "";
            }) { Control = tf };
        }

        public static HostWidget Button(string name, string labelKey, Action clicked)
        {
            var b = new Button(clicked) { text = Text(labelKey), name = name + "-button" };
            b.AddToClassList("hs-field__button");
            b.AddToClassList(WidgetClass);
            if (labelKey != null) Loc.Bind(b, labelKey);
            return new HostWidget(b, null) { Control = b };
        }

        // ------------------------------------------------------------ colours

        /// <summary>
        /// A colour swatch opening the host's picker (with the HDR intensity row when <paramref name="hdr"/>); <paramref name="changed"/>
        /// gets (colour, intensity, phase). Without <paramref name="alpha"/> the reported alpha is the shown one (1 for HDR rows).
        /// </summary>
        public static HostWidget Color(Func<UiContext> ctx, string name, string labelKey, bool alpha, bool hdr, Action<Color, float, ApiPhase> changed)
        {
            var sw = new ColorSwatch(Text(labelKey)) { Alpha = alpha };
            sw.name = name + "-widget";
            sw.AddToClassList(WidgetClass);
            sw.LabelElement.name = name + "-label";
            Bind(sw.LabelElement, labelKey);
            sw.Button.name = name + "-swatch";
            float intensity = 0;
            void ShowTip(Color c, float i) => sw.Button.tooltip = "#" + ColorUtility.ToHtmlStringRGBA(c) + (hdr ? " ×" + i.ToString("0.###", CultureInfo.InvariantCulture) : "");
            sw.Clicked += () =>
            {
                var layer = ctx?.Invoke()?.Shell?.Overlay ?? sw.panel?.visualTree;
                if (layer == null) return;
                Color start = sw.Value, last = start;
                float startI = intensity, lastI = intensity;
                var picker = new ColorPicker(start, alpha, hdr, intensity);
                picker.Changed += (c, i) =>
                {
                    if (!alpha) c.a = hdr ? 1 : start.a;
                    last = c;
                    lastI = i;
                    sw.SetDisplay(c, false);
                    changed(c, i, ApiPhase.Preview);
                };
                var blocker = new VisualElement { name = "color-blocker" };
                blocker.style.position = Position.Absolute;
                blocker.style.left = 0; blocker.style.top = 0; blocker.style.right = 0; blocker.style.bottom = 0;
                blocker.RegisterCallback<PointerDownEvent>(e => { picker.Close(true); e.StopPropagation(); });
                picker.Closed += commit =>
                {
                    blocker.RemoveFromHierarchy();
                    if (commit)
                    {
                        intensity = lastI;
                        changed(last, lastI, ApiPhase.Commit);
                    }
                    else
                    {
                        sw.SetDisplay(start, false);
                        intensity = startI;
                        changed(start, startI, ApiPhase.Cancel);
                    }
                };
                picker.style.position = Position.Absolute;
                var wb = sw.Button.worldBound;
                picker.style.left = Mathf.Max(0, wb.xMin - 260);
                picker.style.top = wb.yMax;
                picker.RegisterCallback<GeometryChangedEvent>(_ =>
                {
                    float maxY = layer.layout.height - picker.layout.height - 2;
                    if (maxY > 0 && picker.layout.y > maxY + 0.5f) picker.style.top = maxY;
                });
                layer.Add(blocker);
                layer.Add(picker);
                UiDefer.Frames(2, () => { if (picker.panel != null) picker.Focus(); });
            };
            return new HostWidget(sw, (v, mixed) =>
            {
                Color c = UnityEngine.Color.white;
                if (v is HdrColor h) { c = h.Color; intensity = h.Intensity; }
                else if (v is Color col) c = col;
                sw.SetDisplay(c, mixed);
                if (mixed) sw.Button.tooltip = "—";
                else ShowTip(c, intensity);
            }) { Control = sw };
        }

        // ------------------------------------------------------------ pickers

        /// <summary>A reference picker: <paramref name="choices"/> lists (value, label) when the list opens; <paramref name="labelOf"/>
        /// gives the shown label of a value (null: the value).</summary>
        public static HostWidget Ref(string name, string labelKey, Func<IReadOnlyList<(string value, string label)>> choices, Func<string, string> labelOf,
            Action<string> changed)
        {
            var rp = new RefPicker(Text(labelKey)) { Choices = choices };
            rp.name = name + "-widget";
            rp.AddToClassList(WidgetClass);
            rp.LabelElement.name = name + "-label";
            Bind(rp.LabelElement, labelKey);
            rp.Button.name = name + "-ref";
            rp.Picked += v => changed(v);
            return new HostWidget(rp, (v, mixed) =>
            {
                string s = v as string;
                rp.SetDisplay(s, s == null ? null : labelOf?.Invoke(s) ?? s, mixed);
                rp.Button.tooltip = s ?? "";
            }) { Control = rp };
        }

        /// <summary>A file picker (the app's image browser; × clears to null).</summary>
        public static HostWidget File(string name, string labelKey, Action<string> changed)
        {
            var fp = new FilePicker(Text(labelKey));
            fp.name = name + "-widget";
            fp.AddToClassList(WidgetClass);
            fp.LabelElement.name = name + "-label";
            Bind(fp.LabelElement, labelKey);
            fp.FileLabel.name = name + "-file";
            fp.BrowseButton.name = name + "-browse";
            fp.ClearButton.name = name + "-clear";
            fp.Picked += p => changed(p);
            return new HostWidget(fp, (v, mixed) => fp.SetDisplay(v as string, mixed)) { Control = fp };
        }

        /// <summary>
        /// The accessory attach-point picker (the 54 points in their groups) with the value handed to <paramref name="changed"/>:
        /// its keys pick into the callback (re-picking the shown key does nothing); null = no point.
        /// </summary>
        public static HostWidget AttachPoint(UiContext ctx, string name, string labelKey, Action<string> changed)
        {
            var box = new VisualElement { name = name + "-widget" };
            box.AddToClassList(WidgetClass);
            box.AddToClassList(FieldRow.UssClass);
            var lab = new Label(Text(labelKey)) { name = name + "-label" };
            lab.AddToClassList("hs-field__label");
            Bind(lab, labelKey);
            box.Add(lab);
            string current = null;
            bool mixed = false;
            var spec = new FieldSpec { Kind = FieldKind.AttachPoint, LabelKey = labelKey };
            var picker = new AttachPointPicker(ctx, spec, TargetSelector.Node(null), "plugin/" + name, () => null) { name = name + "-picker" };
            var cur = picker.Q<Label>("acc-parent-current");
            if (cur != null) cur.name = name + "-current";
            picker.Binding.Path = null;
            var apply = picker.Binding.Apply;
            picker.Binding.Apply = (el, bv) => apply(el, new BoundValues
            {
                Targets = new string[] { null }, Values = new object[] { current }, Value = current ?? "", Mixed = mixed, Empty = false,
            });
            foreach (var kv in picker.Keys)
            {
                string k = kv.Key;
                kv.Value.clickable = new Clickable(() => { if (mixed || current != k) changed(k); });
            }
            box.Add(picker);
            void Show(object v, bool m)
            {
                current = v as string;
                mixed = m;
                picker.Binding.Apply(picker, default);
            }
            Show(null, false);
            return new HostWidget(box, Show) { Control = picker };
        }

        /// <summary>
        /// The Maker thumbnail list of the list categories (by the sex of <paramref name="sexOf"/>'s node, female when none) with
        /// its pick handed to <paramref name="changed"/> (a content reference "guid#id"); the shown value marks its cell.
        /// </summary>
        public static HostWidget PartList(UiContext ctx, string name, string labelKey, int categoryFemale, int categoryMale, TargetSelector sexOf,
            Action<string> changed)
        {
            var spec = new FieldSpec { Kind = FieldKind.PartList, LabelKey = labelKey, CategoryFemale = categoryFemale, CategoryMale = categoryMale };
            var list = new PartList(ctx, spec, sexOf, "plugin/" + name) { name = name + "-widget" };
            list.AddToClassList(WidgetClass);
            var cur = list.Q<Label>("partlist-current");
            if (cur != null) cur.name = name + "-current";
            string current = null;
            bool mixed = false;
            list.Binding.Path = null;
            var apply = list.Binding.Apply;
            list.Binding.Apply = (el, bv) => apply(el, new BoundValues
            {
                Targets = bv.Targets != null && bv.Targets.Count > 0 ? bv.Targets : new string[] { null },
                Values = new object[] { current }, Value = current, Mixed = mixed, Empty = current == null,
            });
            list.PickOverride = r =>
            {
                changed(r);
                return OpResult.Success();
            };
            void Show(object v, bool m)
            {
                current = v as string;
                mixed = m;
                list.Binding.Apply(list, list.Binding.Read(ctx));
            }
            Show(null, false);
            return new HostWidget(list, Show) { Control = list };
        }

        /// <summary>The Maker push grid of a host push source with its push handed to <paramref name="pushed"/> (the cell's reference).</summary>
        public static HostWidget Push(UiContext ctx, string name, string labelKey, string source, TargetSelector sexOf, Action<string> pushed)
        {
            var spec = new FieldSpec { Kind = FieldKind.Push, LabelKey = labelKey, PushSource = source };
            var grid = new PushGrid(ctx, spec, sexOf, "plugin/" + name) { name = name + "-widget" };
            grid.AddToClassList(WidgetClass);
            grid.PushOverride = r =>
            {
                pushed(r);
                return OpResult.Success();
            };
            return new HostWidget(grid, null) { Control = grid };
        }
    }
}
