// The value rows of the Ragdoll panel: one member of a block each, by its name, as StudioOps.RagdollValue takes it. A number's drag is one UI edit with a merge key: each step writes the value,
// the release commits, Esc gives it up and the value is back. A row's default button goes back to the class's default and is greyed there. The size of the room, the floor's offset and height
// are written when the slider stops, since each write moves the room in place: a drag holds its value in the panel until 0.3 s of real time after the last change.
using HS2Next.Save;
using HS2Next.Scenes.Ragdoll;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Binding;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    /// <summary>A row that shows one value of the panel's character or of the scene. Element name: ragdoll-field-&lt;name&gt;.</summary>
    public abstract class RagdollRow : VisualElement
    {
        protected readonly UiContext Ctx;
        protected readonly RagdollPanel Panel;
        /// <summary>The value's name (`muscle`, `gravity`, `room.size`).</summary>
        public readonly string Value;
        /// <summary>A value of the character: it shows and edits with a character only.</summary>
        public readonly bool OfBody;

        protected RagdollRow(UiContext ctx, RagdollPanel panel, string value)
        {
            Ctx = ctx;
            Panel = panel;
            Value = value;
            OfBody = RagdollEdits.BlockOf(value) == RagdollValueBlock.Body;
            name = "ragdoll-field-" + value;
            AddToClassList("hs-ragdoll__row");
        }

        protected object Default => OfBody ? RagdollBodyValues.DefaultOf(Value) : RagdollWorldValues.DefaultOf(Value);

        protected object Now(RagdollBodyValues body, RagdollWorldValues world) => OfBody ? body.Get(Value) : world.Get(Value);

        protected OpResult Write(JToken value, string mergeKey = null)
        {
            var r = Ctx.ShowResult(Ctx.Ops.RagdollValue(OfBody ? Panel.Node : null, Value, value, UiContext.User(OpLabels.RagdollSettings, mergeKey)));
            Panel.MarkDirty();
            return r;
        }

        /// <summary>Shows the value; <paramref name="body"/> is null with no character.</summary>
        public abstract void Show(RagdollBodyValues body, RagdollWorldValues world, SceneRagdollWorld block);
    }

    /// <summary>A number of a block's values, a slider over the member's range.</summary>
    public sealed class RagdollNumberRow : RagdollRow
    {
        static int drags;

        readonly double fallback;
        UiEdit edit;
        string key;

        public NumberSlider Slider { get; }

        public RagdollNumberRow(UiContext ctx, RagdollPanel panel, string value, string labelKey) : base(ctx, panel, value)
        {
            var range = OfBody ? RagdollBodyValues.RangeOf(value) : RagdollWorldValues.RangeOf(value);
            Slider = new NumberSlider(L10n.Get(labelKey), range?.Min, range?.Max) { TypedMin = range?.Min, TypedMax = range?.Max };
            Loc.Bind(Slider.LabelElement, labelKey);
            fallback = Default is float f ? f : 0;
            Slider.DefaultValue = () => fallback;
            Slider.Edited += Edited;
            Add(Slider);
        }

        void Edited(EditPhase phase, double v)
        {
            switch (phase)
            {
                case EditPhase.Begin:
                    Panel.Flush();
                    key = "ui.ragdoll/" + Panel.Node + "/" + Value + "/" + ++drags;
                    edit = Ctx.BeginEdit(OpLabels.RagdollSettings, key);
                    edit.Ended += _ => Panel.MarkDirty();
                    break;
                case EditPhase.Preview:
                    if (edit == null || !edit.IsOpen) return;   // Esc took the drag back: a late step writes nothing
                    Write(new JValue((float)v), key);
                    break;
                case EditPhase.End:
                    var e = edit;
                    edit = null;
                    if (e != null && e.IsOpen) Ctx.ShowResult(e.Commit());
                    Panel.MarkDirty();   // also after Esc: the slider shows the document again
                    break;
                case EditPhase.Typed:
                    Write(new JValue((float)v), "ui.typed/" + Panel.Node + "/" + Value);
                    break;
                default:
                    Write(new JValue((float)v));
                    break;
            }
        }

        public override void Show(RagdollBodyValues body, RagdollWorldValues world, SceneRagdollWorld block)
        {
            double v = Now(body, world) is float f ? f : fallback;
            if (!Slider.Dragging) Slider.SetDisplay(v, false);
            Slider.ResetButton.SetEnabled(Mathf.Abs((float)(v - fallback)) > 1e-6f);
        }
    }

    /// <summary>A switch of a block's values, with its default button.</summary>
    public sealed class RagdollSwitchRow : RagdollRow
    {
        readonly Button reset;

        public Toggle Toggle { get; }

        public RagdollSwitchRow(UiContext ctx, RagdollPanel panel, string value, string labelKey) : base(ctx, panel, value)
        {
            style.flexDirection = FlexDirection.Row;
            Toggle = new Toggle { name = "toggle" };
            Loc.Bind(Toggle, labelKey, (e, s) => ((Toggle)e).label = s);
            Toggle.AddToClassList("hs-ragdoll__grow");
            Toggle.RegisterValueChangedCallback(e => { if (e.target == Toggle) Write(new JValue(e.newValue)); });
            Add(Toggle);
            reset = RagdollPanel.ResetButton(() => Write(new JValue((bool)Default)));
            Add(reset);
        }

        public override void Show(RagdollBodyValues body, RagdollWorldValues world, SceneRagdollWorld block)
        {
            bool v = Now(body, world) is bool b && b;
            Toggle.SetValueWithoutNotify(v);
            reset.SetEnabled(!Equals(v, Default));
        }
    }

    /// <summary>A choice of a few words as a segmented control: the prop's kind, the floor's mode.</summary>
    public sealed class RagdollChoiceRow : RagdollRow
    {
        readonly System.Collections.Generic.IReadOnlyList<string> choices;

        public SegmentedButtons Buttons { get; }

        public RagdollChoiceRow(UiContext ctx, RagdollPanel panel, string value, System.Collections.Generic.IReadOnlyList<string> choices, string keyPrefix) : base(ctx, panel, value)
        {
            this.choices = choices;
            var labels = new string[choices.Count];
            for (int i = 0; i < labels.Length; i++) labels[i] = L10n.Get(keyPrefix + choices[i]);
            Buttons = new SegmentedButtons(labels);
            for (int i = 0; i < labels.Length; i++)
                Loc.Bind(Buttons.Buttons[i], keyPrefix + choices[i], (e, s) => { var x = (Button)e; x.text = x.tooltip = s; });
            Buttons.Selected += i => Write(new JValue(this.choices[i]));
            Add(Buttons);
        }

        public override void Show(RagdollBodyValues body, RagdollWorldValues world, SceneRagdollWorld block)
        {
            string now = Value == RagdollEdits.FloorMode ? block?.Floor.AppliedMode ?? RagdollFloorValues.ModeMap : world.AppliedPropKind;
            int at = -1;
            for (int i = 0; i < choices.Count; i++) if (choices[i] == now) at = i;
            Buttons.SetDisplay(at, false);
        }
    }

    /// <summary>A number of the room or the floor, written when the slider stops: a component of room.size, floor.offset, floor.height.</summary>
    public sealed class RagdollPlaceRow : RagdollRow
    {
        static int drags;

        readonly int component;
        readonly double fallback;
        UiEdit edit;

        public NumberSlider Slider { get; }

        public RagdollPlaceRow(UiContext ctx, RagdollPanel panel, string value, string labelKey, WetRange? range, double fallback, int component = -1) : base(ctx, panel, value)
        {
            this.component = component;
            this.fallback = fallback;
            if (component >= 0) name += "-" + component;
            Slider = new NumberSlider(L10n.Get(labelKey), range?.Min, range?.Max) { TypedMin = range?.Min, TypedMax = range?.Max };
            Loc.Bind(Slider.LabelElement, labelKey);
            Slider.DefaultValue = () => fallback;
            Slider.Edited += Edited;
            Add(Slider);
        }

        void Edited(EditPhase phase, double v)
        {
            switch (phase)
            {
                case EditPhase.Begin:
                    Panel.Flush();
                    Panel.DelayKey = "ui.ragdoll/place/" + ++drags;
                    edit = Ctx.BeginEdit(OpLabels.RagdollSettings, Panel.DelayKey);
                    edit.Ended += committed => { if (!committed) Panel.DropDelayed(); };
                    break;
                case EditPhase.Preview:
                    if (edit == null || !edit.IsOpen) return;
                    Panel.Delay(Value, component, (float)v);
                    break;
                case EditPhase.End:
                    var e = edit;
                    edit = null;
                    if (e != null && e.IsOpen) Ctx.ShowResult(e.Commit());   // what the drag has not written yet follows at its time, under the same key
                    Panel.MarkDirty();
                    break;
                case EditPhase.Typed:
                    Now("ui.typed/ragdoll/" + Value, v);
                    break;
                default:
                    Now(null, v);
                    break;
            }
        }

        // a typed value and the default button write at once, each under its own key
        void Now(string key, double v)
        {
            Panel.Flush();
            Panel.DelayKey = key;
            Panel.Delay(Value, component, (float)v);
            Panel.Flush();
        }

        public override void Show(RagdollBodyValues body, RagdollWorldValues world, SceneRagdollWorld block)
        {
            double v = Panel.PlaceNow(Value, component, block);
            if (!Slider.Dragging && !Panel.Delayed) Slider.SetDisplay(v, false);
            Slider.ResetButton.SetEnabled(Mathf.Abs((float)(v - fallback)) > 1e-6f);
        }
    }
}
