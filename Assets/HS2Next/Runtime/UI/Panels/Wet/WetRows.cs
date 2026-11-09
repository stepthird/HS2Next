// The value rows of the Wet and stains panel: one member of the block each, by its path. Every edit goes through StudioOps. A number's drag is one UI edit with a merge key: each
// step is WetDrag, which shows a value the maps hold at once and previews one that needs a new bake, the release is WetSet with the last value, Esc gives it up and the character
// shows its document again. A row's default button goes back to the class's default and is greyed there. A kept stain takes no edit: its rows are greyed.
using System;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Wet;
using HS2Next.UI.Binding;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    /// <summary>A row that shows one member of the block of the panel's character. Element name: wet-field-&lt;path&gt;, with -&lt;component&gt; on a part of a vector.</summary>
    public abstract class WetRow : VisualElement
    {
        protected readonly UiContext Ctx;
        protected readonly WetPanel Panel;
        /// <summary>The member's path (`look.amount`, `film.gloss`).</summary>
        public readonly string Path;

        protected WetRow(UiContext ctx, WetPanel panel, string path)
        {
            Ctx = ctx;
            Panel = panel;
            Path = path;
            name = "wet-field-" + path;
            AddToClassList("hs-wet__row");
        }

        protected string Label => WetEdits.IsFloor(Path) ? OpLabels.WetFloor : WetEdits.IsOrigin(Path) ? OpLabels.WetOrigin : OpLabels.WetEdit;

        protected void Write(object value, string mergeKey = null)
        {
            Ctx.ShowResult(Ctx.Ops.WetSet(Panel.Node, Panel.StainId, Path, value, UiContext.User(Label, mergeKey)));
            Panel.MarkDirty();
        }

        /// <summary>Shows the block's value; a stain's row of a kept stain is greyed.</summary>
        public void Read(CharaWet block, WetStain stain)
        {
            SetEnabled(!(stain.Kept && WetEdits.OnStain(Path)));
            Show(block, stain);
        }

        protected abstract void Show(CharaWet block, WetStain stain);
    }

    /// <summary>A number: a slider where the member has a range, a part of a vector with <c>component</c> 0 to 2.</summary>
    public sealed class WetNumberRow : WetRow
    {
        static int drags;

        readonly int component;
        readonly double fallback;
        UiEdit edit;
        string key;
        double last;

        public NumberSlider Slider { get; }

        public WetNumberRow(UiContext ctx, WetPanel panel, string path, string labelKey, WetRange? range, int component = -1) : base(ctx, panel, path)
        {
            this.component = component;
            if (component >= 0) name += "-" + component;
            Slider = new NumberSlider(L10n.Get(labelKey), range?.Min, range?.Max) { TypedMin = range?.Min, TypedMax = range?.Max };
            Loc.Bind(Slider.LabelElement, labelKey);
            fallback = Number(WetEdits.Default(path));
            Slider.DefaultValue = () => fallback;
            Slider.Edited += Edited;
            Add(Slider);
        }

        double Number(object v)
        {
            if (v is Vec3 x) return component == 0 ? x.X : component == 1 ? x.Y : x.Z;
            return v is float f ? f : 0;
        }

        object Value(double v)
        {
            if (component < 0) return (float)v;
            var c = (Vec3)WetEdits.Get(Ctx.Ops.WetBlock(Panel.Node), Panel.Stain, Path);
            return new Vec3(component == 0 ? (float)v : c.X, component == 1 ? (float)v : c.Y, component == 2 ? (float)v : c.Z);
        }

        void Edited(EditPhase phase, double v)
        {
            switch (phase)
            {
                case EditPhase.Begin:
                    last = v;
                    key = "ui.wet/" + Panel.Node + "/" + Path + "/" + ++drags;
                    edit = Ctx.BeginEdit(Label, key);
                    string node = Panel.Node;
                    edit.Ended += committed => { if (!committed) { Ctx.Ops.WetDragCancel(node); Panel.MarkDirty(); } };
                    break;
                case EditPhase.Preview:
                    if (edit == null || !edit.IsOpen) return;   // Esc took the drag back: a late step writes nothing
                    last = v;
                    var step = Ctx.Ops.WetDrag(Panel.Node, Panel.StainId, Path, Value(v), key, UiContext.User(Label, key));
                    if (!step.Ok) Ctx.ShowResult(step);
                    break;
                case EditPhase.End:
                    var e = edit;
                    edit = null;
                    if (e == null || !e.IsOpen) return;
                    var r = Ctx.Ops.WetSet(Panel.Node, Panel.StainId, Path, Value(last), UiContext.User(Label, key));
                    if (r.Ok) Ctx.ShowResult(e.Commit());
                    else { e.Cancel(); Ctx.ShowResult(r); }
                    Panel.MarkDirty();
                    break;
                case EditPhase.Typed:
                    Write(Value(v), "ui.typed/" + Panel.Node + "/" + Path);
                    break;
                default:
                    Write(Value(v));
                    break;
            }
        }

        protected override void Show(CharaWet block, WetStain stain)
        {
            double v = Number(WetEdits.Get(block, stain, Path));
            if (!Slider.Dragging) Slider.SetDisplay(v, false);
            Slider.ResetButton.SetEnabled(Mathf.Abs((float)(v - fallback)) > 1e-6f);
        }
    }

    /// <summary>A switch with its default button.</summary>
    public sealed class WetSwitchRow : WetRow
    {
        readonly Button reset;

        public Toggle Toggle { get; }

        public WetSwitchRow(UiContext ctx, WetPanel panel, string path, string labelKey) : base(ctx, panel, path)
        {
            style.flexDirection = FlexDirection.Row;
            Toggle = new Toggle { name = "toggle" };
            Loc.Bind(Toggle, labelKey, (e, s) => ((Toggle)e).label = s);
            Toggle.AddToClassList("hs-wet__grow");
            Toggle.RegisterValueChangedCallback(e => { if (e.target == Toggle) Write(e.newValue); });
            Add(Toggle);
            reset = WetPanel.ResetButton(() => Write(WetEdits.Default(Path)));
            Add(reset);
        }

        protected override void Show(CharaWet block, WetStain stain)
        {
            bool v = WetEdits.Get(block, stain, Path) is bool b && b;
            Toggle.SetValueWithoutNotify(v);
            reset.SetEnabled(!Equals(v, WetEdits.Default(Path)));
        }
    }

    /// <summary>A colour, of three or four components: the swatch opens the picker; while it is open the colour shows at once, closing it is one entry, its cancel takes it back.</summary>
    public sealed class WetColourRow : WetRow
    {
        static int edits;

        readonly bool alpha;
        readonly Button reset;

        public ColorSwatch Swatch { get; }

        public WetColourRow(UiContext ctx, WetPanel panel, string path, string labelKey) : base(ctx, panel, path)
        {
            style.flexDirection = FlexDirection.Row;
            alpha = WetEdits.KindOf(path) == WetValueKind.Colour;
            Swatch = new ColorSwatch(L10n.Get(labelKey)) { Alpha = alpha };
            Loc.Bind(Swatch.LabelElement, labelKey);
            Swatch.AddToClassList("hs-wet__grow");
            Swatch.Clicked += Open;
            Add(Swatch);
            reset = WetPanel.ResetButton(() => Write(WetEdits.Default(Path)));
            Add(reset);
        }

        object Model(Color c) => alpha ? (object)new Color4(c.r, c.g, c.b, c.a) : new Vec3(c.r, c.g, c.b);

        static Color Shown(object v) => v is Color4 c ? new Color(c.R, c.G, c.B, c.A) : v is Vec3 x ? new Color(x.X, x.Y, x.Z, 1) : Color.white;

        void Open()
        {
            var layer = Ctx.Shell?.Overlay ?? panel?.visualTree;
            if (layer == null) return;
            var picker = new ColorPicker(Swatch.Value, alpha, false, 0);
            string key = "ui.wet/" + Panel.Node + "/" + Path + "/c" + ++edits;
            var edit = Ctx.BeginEdit(Label, key);
            picker.Changed += (c, _) =>
            {
                if (!edit.IsOpen) return;
                var r = Ctx.Ops.WetDrag(Panel.Node, Panel.StainId, Path, Model(c), key, UiContext.User(Label, key));
                if (!r.Ok) Ctx.ShowResult(r);
            };
            picker.Closed += commit =>
            {
                if (commit) Ctx.ShowResult(edit.Commit()); else edit.Cancel();
                Panel.MarkDirty();
            };
            var blocker = new VisualElement { name = "color-blocker" };
            blocker.style.position = Position.Absolute;
            blocker.style.left = 0; blocker.style.top = 0; blocker.style.right = 0; blocker.style.bottom = 0;
            blocker.RegisterCallback<PointerDownEvent>(e => { picker.Close(true); e.StopPropagation(); });
            picker.Closed += _ => blocker.RemoveFromHierarchy();
            picker.style.position = Position.Absolute;
            var wb = Swatch.Button.worldBound;
            picker.style.left = Mathf.Max(0, wb.xMin - 260);
            picker.style.top = wb.yMax;
            picker.RegisterCallback<GeometryChangedEvent>(_ => { float maxY = layer.layout.height - picker.layout.height - 2; if (maxY > 0 && picker.layout.y > maxY + 0.5f) picker.style.top = maxY; });
            layer.Add(blocker);
            layer.Add(picker);
            UiDefer.Frames(2, () => { if (picker.panel != null) picker.Focus(); });
        }

        protected override void Show(CharaWet block, WetStain stain)
        {
            var v = WetEdits.Get(block, stain, Path);
            Swatch.SetDisplay(Shown(v), false);
            reset.SetEnabled(!Equals(v, WetEdits.Default(Path)));
        }
    }

    /// <summary>The floor's mode as a segmented control.</summary>
    public sealed class WetFloorModeRow : WetRow
    {
        public SegmentedButtons Buttons { get; }

        public WetFloorModeRow(UiContext ctx, WetPanel panel) : base(ctx, panel, "floor.mode")
        {
            Buttons = new SegmentedButtons(new[] { L10n.Get("wet.floor.map"), L10n.Get("wet.floor.feet"), L10n.Get("wet.floor.fixed") });
            var keys = new[] { "wet.floor.map", "wet.floor.feet", "wet.floor.fixed" };
            for (int i = 0; i < keys.Length; i++)
            {
                var b = Buttons.Buttons[i];
                Loc.Bind(b, keys[i], (e, s) => { var x = (Button)e; x.text = x.tooltip = s; });
            }
            Buttons.Selected += i =>
            {
                Ctx.ShowResult(Ctx.Ops.WetFloorSet(Panel.Node, WetFloorValues.Modes[i], null, null, UiContext.User(OpLabels.WetFloor)));
                Panel.MarkDirty();
            };
            Add(Buttons);
        }

        protected override void Show(CharaWet block, WetStain stain)
        {
            int i = -1;
            for (int k = 0; k < WetFloorValues.Modes.Count; k++) if (WetFloorValues.Modes[k] == block.Floor.AppliedMode) i = k;
            Buttons.SetDisplay(i, false);
        }
    }
}
