// Numeric widgets: NumberSlider = label, slider (when the row has a range), numeric field
// and the ↺ default button (double-click on the slider does the same); one transaction per drag (slider, label drag),
// typed commit on Enter / blur with the schema clamp; mixed values show "—". VectorField = 2–4 FloatFields with X / Y /
// Z / W coloured labels (label drag per component, mixed per component); the fields sit in their own line (`axes`) that
// follows the label when the row has room for the label and every field at its minimum width (a value like -123.456 shown
// whole) and goes below the label otherwise, its fields wrapping onto further lines when even that line is too narrow
// (v0.3 trial 2: in the 360 px sidebar the three fields beside the label showed about one digit). SegmentedButtons = one
// button per choice (clothes states). The widgets only raise edit events; FieldRow (Binding) turns them into StudioOps calls.
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Widgets
{
    /// <summary>How a widget value change came about.</summary>
    public enum EditPhase
    {
        /// <summary>A drag started (open the transaction).</summary>
        Begin,
        /// <summary>A value during a drag (preview, inside the transaction).</summary>
        Preview,
        /// <summary>The drag ended (commit).</summary>
        End,
        /// <summary>A complete edit on its own (typed value, click): one entry.</summary>
        Commit,
        /// <summary>A typed value (merges with the previous typed edit of the same field).</summary>
        Typed,
    }

    public sealed class NumberSlider : VisualElement
    {
        public const string UssClass = "hs-number";
        public readonly Label LabelElement;
        public readonly Slider Slider;
        public readonly FloatField Field;
        public readonly Button ResetButton;
        public double? Min, Max, TypedMin, TypedMax;
        public bool Whole;
        public string Format = "0.###";
        /// <summary>Value of ↺ (null hides the button).</summary>
        public Func<double?> DefaultValue;
        /// <summary>(phase, value).</summary>
        public event Action<EditPhase, double> Edited;
        public bool Dragging { get; private set; }
        double value;
        bool mixed;

        public NumberSlider(string label, double? min, double? max)
        {
            AddToClassList(UssClass);
            Min = min; Max = max;
            LabelElement = new Label(label ?? "");
            LabelElement.AddToClassList("hs-number__label");
            Add(LabelElement);
            if (min.HasValue && max.HasValue && max.Value > min.Value)
            {
                Slider = new Slider((float)min.Value, (float)max.Value) { name = "slider" };
                Slider.AddToClassList("hs-number__slider");
                Slider.RegisterValueChangedCallback(e =>
                {
                    if (!Dragging) { Begin(); Raise(EditPhase.Preview, e.newValue); End(); }
                    else Raise(EditPhase.Preview, e.newValue);
                });
                // the press opens the transaction: the slider may move the value on the press itself (before its pointer
                // capture), which would otherwise be a history entry of its own before the drag's (Phase 4.1 UI4)
                Slider.RegisterCallback<PointerDownEvent>(e => { if (e.button == 0 && e.clickCount == 1) Begin(); }, TrickleDown.TrickleDown);
                Slider.RegisterCallback<PointerCaptureEvent>(_ => Begin(), TrickleDown.TrickleDown);
                Slider.RegisterCallback<PointerCaptureOutEvent>(_ => End(), TrickleDown.TrickleDown);
                Slider.RegisterCallback<PointerUpEvent>(_ => End());   // a press without a capture still ends its transaction
                Slider.RegisterCallback<PointerDownEvent>(e => { if (e.clickCount == 2) ResetToDefault(); }, TrickleDown.TrickleDown);
                Add(Slider);
            }
            Field = new FloatField { name = "field", isDelayed = true, formatString = Format };
            Field.AddToClassList("hs-number__field");
            Field.RegisterValueChangedCallback(e =>
            {
                if (Dragging) return;
                double v = e.newValue;
                if (TypedMin.HasValue && v < TypedMin.Value) v = TypedMin.Value;
                if (TypedMax.HasValue && v > TypedMax.Value) v = TypedMax.Value;
                if (Whole) v = Math.Round(v, MidpointRounding.AwayFromZero);
                Raise(EditPhase.Typed, v);
            });
            Add(Field);
            // label drag edits like a slider (one transaction)
            LabelElement.AddManipulator(new LabelDragger(this));
            ResetButton = new Button(ResetToDefault) { name = "reset", text = "↺" };
            ResetButton.AddToClassList("hs-number__reset");
            Add(ResetButton);
            ResetButton.style.display = DisplayStyle.None;
        }

        public double Value => value;
        public bool Mixed => mixed;

        /// <summary>Shows a value without raising Edited.</summary>
        public void SetDisplay(double v, bool isMixed)
        {
            value = v;
            mixed = isMixed;
            Slider?.SetValueWithoutNotify((float)v);
            if (Slider != null) Slider.showMixedValue = isMixed;
            Field.formatString = Format;
            Field.SetValueWithoutNotify((float)v);
            Field.showMixedValue = isMixed;
            ResetButton.style.display = DefaultValue != null && DefaultValue() != null ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void ResetToDefault()
        {
            var d = DefaultValue?.Invoke();
            if (d == null) return;
            Raise(EditPhase.Commit, d.Value);
        }

        public void Begin()
        {
            if (Dragging) return;
            Dragging = true;
            Raise(EditPhase.Begin, value);
        }

        public void End()
        {
            if (!Dragging) return;
            Dragging = false;
            Raise(EditPhase.End, value);
        }

        public void Raise(EditPhase p, double v)
        {
            if (p == EditPhase.Preview || p == EditPhase.Commit || p == EditPhase.Typed)
            {
                if (Min.HasValue && Max.HasValue && p == EditPhase.Preview) v = Math.Max(Min.Value, Math.Min(Max.Value, v));
                if (Whole) v = Math.Round(v, MidpointRounding.AwayFromZero);
                value = v;
                mixed = false;
            }
            Edited?.Invoke(p, v);
        }

        /// <summary>Horizontal drag on the label: the value changes by (range / 300 or 0.01) per logical pixel, Shift ×0.1.</summary>
        sealed class LabelDragger : PointerManipulator
        {
            readonly NumberSlider s;
            bool active;
            float startX;
            double startV;
            public LabelDragger(NumberSlider s) { this.s = s; activators.Add(new ManipulatorActivationFilter { button = MouseButton.LeftMouse }); }
            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<PointerDownEvent>(Down);
                target.RegisterCallback<PointerMoveEvent>(Move);
                target.RegisterCallback<PointerUpEvent>(Up);
                target.RegisterCallback<PointerCaptureOutEvent>(Lost);
            }
            protected override void UnregisterCallbacksFromTarget()
            {
                target.UnregisterCallback<PointerDownEvent>(Down);
                target.UnregisterCallback<PointerMoveEvent>(Move);
                target.UnregisterCallback<PointerUpEvent>(Up);
                target.UnregisterCallback<PointerCaptureOutEvent>(Lost);
            }
            void Down(PointerDownEvent e)
            {
                if (!CanStartManipulation(e)) return;
                active = true; startX = e.position.x; startV = s.value;
                target.CapturePointer(e.pointerId);
                s.Begin();
                e.StopPropagation();
            }
            void Move(PointerMoveEvent e)
            {
                if (!active || !target.HasPointerCapture(e.pointerId)) return;
                double rate = s.Min.HasValue && s.Max.HasValue ? (s.Max.Value - s.Min.Value) / 300.0 : 0.01;
                if (e.shiftKey) rate *= 0.1;
                double v = startV + (e.position.x - startX) * rate;
                if (s.TypedMin.HasValue && v < s.TypedMin.Value) v = s.TypedMin.Value;
                if (s.TypedMax.HasValue && v > s.TypedMax.Value) v = s.TypedMax.Value;
                s.Raise(EditPhase.Preview, v);
                s.SetDisplay(s.value, false);
            }
            void Up(PointerUpEvent e)
            {
                if (!active) return;
                active = false;
                if (target.HasPointerCapture(e.pointerId)) target.ReleasePointer(e.pointerId);
                s.End();
            }
            void Lost(PointerCaptureOutEvent e) { if (active) { active = false; s.End(); } }
        }
    }

    public sealed class VectorField : VisualElement
    {
        public const string UssClass = "hs-vector", AxesClass = "hs-vector__axes", StackedClass = "hs-vector--stacked";
        /// <summary>The width a field takes at least, its margins included (logical px; USS hs-vector__field: 94 + 2 × 2): room for
        /// a value like -123.456 beside its axis label.</summary>
        public const float AxisWidth = 98f;
        /// <summary>True when the fields stand below the label (the row is narrower than the label and every field at its minimum).</summary>
        public bool Stacked => ClassListContains(StackedClass);
        public readonly Label LabelElement;
        public readonly FloatField[] Fields;
        /// <summary>The line holding the fields (beside the label, or below it in a narrow row).</summary>
        public readonly VisualElement Axes;
        public string Format = "0.#####";
        public event Action<EditPhase, Vector4> Edited;
        /// <summary>Which components the last typed edit changed (the others keep their mixed values when applied).</summary>
        public int LastComponent { get; private set; } = -1;
        Vector4 value;
        bool dragging;
        static readonly string[] Names = { "X", "Y", "Z", "W" };

        public VectorField(string label, int components)
        {
            AddToClassList(UssClass);
            LabelElement = new Label(label ?? "");
            LabelElement.AddToClassList("hs-vector__label");
            Add(LabelElement);
            Fields = new FloatField[Mathf.Clamp(components, 2, 4)];
            Axes = new VisualElement { name = "axes" };
            Axes.AddToClassList(AxesClass);
            Add(Axes);
            // the fields go below the label when the row is narrower than the label and every field at its minimum width (decided
            // from the row's own width, which its parent sets: no layout loop)
            RegisterCallback<GeometryChangedEvent>(_ => UpdateStacked());
            for (int i = 0; i < Fields.Length; i++)
            {
                int k = i;
                var f = new FloatField(Names[i]) { name = "c" + i, isDelayed = true };
                f.AddToClassList("hs-vector__field");
                f.AddToClassList("hs-vector__field--" + Names[i].ToLowerInvariant());
                f.RegisterValueChangedCallback(e =>
                {
                    value[k] = e.newValue;
                    LastComponent = k;
                    Edited?.Invoke(dragging ? EditPhase.Preview : EditPhase.Typed, value);
                });
                // the FloatField's own label dragger captures the pointer on the label: one transaction per drag
                f.RegisterCallback<PointerCaptureEvent>(_ => { if (!dragging) { dragging = true; f.isDelayed = false; LastComponent = k; Edited?.Invoke(EditPhase.Begin, value); } }, TrickleDown.TrickleDown);
                f.RegisterCallback<PointerCaptureOutEvent>(_ => { if (dragging) { dragging = false; f.isDelayed = true; Edited?.Invoke(EditPhase.End, value); } }, TrickleDown.TrickleDown);
                Fields[i] = f;
                Axes.Add(f);
            }
        }

        public Vector4 Value => value;

        void UpdateStacked()
        {
            float w = resolvedStyle.width;
            if (float.IsNaN(w) || w <= 0) return;
            var ls = LabelElement.resolvedStyle;
            float label = ls.display == DisplayStyle.None ? 0 : ls.width + ls.marginLeft + ls.marginRight;
            bool stacked = w + 0.5f < label + Fields.Length * AxisWidth;
            if (stacked != Stacked) EnableInClassList(StackedClass, stacked);
        }

        /// <summary>Shows a value; mixed[i] shows "—" in component i.</summary>
        public void SetDisplay(Vector4 v, bool[] mixed)
        {
            value = v;
            for (int i = 0; i < Fields.Length; i++)
            {
                Fields[i].formatString = Format;
                Fields[i].SetValueWithoutNotify(v[i]);
                Fields[i].showMixedValue = mixed != null && i < mixed.Length && mixed[i];
            }
        }
    }

    public sealed class SegmentedButtons : VisualElement
    {
        public const string UssClass = "hs-segmented";
        readonly List<Button> buttons = new List<Button>();
        public event Action<int> Selected;
        public int Index { get; private set; } = -1;

        public SegmentedButtons(IReadOnlyList<string> labels)
        {
            AddToClassList(UssClass);
            for (int i = 0; i < labels.Count; i++)
            {
                int k = i;
                var b = new Button(() => { Selected?.Invoke(k); }) { text = labels[i], tooltip = labels[i], name = "seg-" + i };   // the text may be cut (ellipsis)
                b.AddToClassList("hs-segmented__button");
                buttons.Add(b);
                Add(b);
            }
        }

        public IReadOnlyList<Button> Buttons => buttons;

        public void SetLabels(IReadOnlyList<string> labels)
        {
            for (int i = 0; i < buttons.Count && i < labels.Count; i++) buttons[i].text = buttons[i].tooltip = labels[i];
        }

        /// <summary>Highlights a button (-1 = none, e.g. mixed); enabled[i] false disables a choice (unsupported state).</summary>
        public void SetDisplay(int index, bool mixed, bool[] enabled = null)
        {
            Index = index;
            for (int i = 0; i < buttons.Count; i++)
            {
                buttons[i].EnableInClassList("hs-segmented__button--on", !mixed && i == index);
                buttons[i].EnableInClassList("hs-segmented__button--mixed", mixed);
                buttons[i].SetEnabled(enabled == null || i >= enabled.Length || enabled[i]);
            }
        }
    }

    public static class NumberText
    {
        public static string Of(double d, string format = "0.###") => d.ToString(format, CultureInfo.InvariantCulture);
    }
}
