// One entry of the list, the same row in the panel and in the section: a switch, the label or "source → target", the note of a declared lag, a
// state mark, the weight as a compact slider, Up, Down, Remove. A row with a broken end is greyed and keeps its buttons; what the state, the
// lag and the notes have to say stands in the tooltip of the text and of the mark. A weight drag is one edit (ConstraintDrag).
using System;
using HS2Next.Motion.AdvancedPose;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    public sealed class ConstraintLine : VisualElement
    {
        readonly UiContext ctx;
        readonly string prefix;
        readonly Action<string> clicked;
        readonly Action changed;
        readonly ConstraintDrag drag;
        int index;

        public readonly Toggle On;
        public readonly Label Text, Lag, Mark;
        public readonly NumberSlider Weight;
        public readonly Button Up, Down, Remove;

        /// <summary>The entry shown.</summary>
        public string Id { get; private set; }
        /// <summary>The weight's drag (tests).</summary>
        public ConstraintDrag Drag => drag;

        public ConstraintLine(UiContext ctx, string prefix, Action<string> clicked, Action changed)
        {
            this.ctx = ctx;
            this.prefix = prefix;
            this.clicked = clicked;
            this.changed = changed;
            drag = new ConstraintDrag(ctx, changed);
            AddToClassList("hs-constraints__line");
            On = new Toggle();
            On.AddToClassList("hs-constraints__on");
            On.RegisterValueChangedCallback(OnSwitched);
            Add(On);
            Text = new Label { displayTooltipWhenElided = false };   // a cut label would put its text in place of the state's tooltip
            Text.AddToClassList("hs-constraints__text");
            Text.RegisterCallback<PointerDownEvent>(OnTextDown);
            Add(Text);
            Lag = Loc.Label("constraints.lag");
            Lag.AddToClassList("hs-constraints__lag");
            Loc.Tooltip(Lag, "constraints.lagTip");
            Add(Lag);
            Mark = new Label();
            Mark.AddToClassList("hs-constraints__mark");
            Add(Mark);
            Weight = new NumberSlider(null, 0, 1);
            Weight.AddToClassList("hs-constraints__weight");
            Weight.LabelElement.style.display = DisplayStyle.None;
            Weight.Field.style.display = DisplayStyle.None;
            Weight.Edited += OnWeight;
            Add(Weight);
            Up = Button("▲", "constraints.moveUp", MoveUp);
            Down = Button("▼", "constraints.moveDown", MoveDown);
            Remove = Button("×", "constraints.remove", RemoveEntry);
        }

        Button Button(string glyph, string tip, Action click)
        {
            var b = new Button(click) { text = glyph };
            b.AddToClassList("hs-constraints__btn");
            Loc.Tooltip(b, tip);
            Add(b);
            return b;
        }

        void Names(string row)
        {
            name = row;
            On.name = row + "-on";
            Text.name = row + "-text";
            Lag.name = row + "-lag";
            Mark.name = row + "-mark";
            Weight.name = row + "-weight";
            Up.name = row + "-up";
            Down.name = row + "-down";
            Remove.name = row + "-remove";
        }

        /// <summary>The row stands for no entry any more (a virtualised list takes it back): it answers to no name and writes nothing.</summary>
        public void Unbind()
        {
            Id = null;
            Names(prefix + "free");
        }

        /// <summary>Shows an entry: its place in the list of <paramref name="count"/>, selected or not, and whether it may be edited here.</summary>
        public void Bind(ConstraintInfo info, int listIndex, int count, int rowIndex, bool selected, bool editable)
        {
            var e = info.Entry;
            Id = e.Id;
            index = listIndex;
            Names(prefix + rowIndex);
            EnableInClassList("hs-constraints__line--selected", selected);
            On.SetValueWithoutNotify(e.On);
            On.SetEnabled(editable);
            Text.text = ConstraintResolver.RowText(ctx.Runtime, e);
            bool broken = ConstraintText.Broken(info);
            Text.EnableInClassList(InspectorUi.Dim, broken || !e.On || info.State == ConstraintStates.Off);
            Text.EnableInClassList("hs-constraints__text--broken", broken);
            string tip = ConstraintText.Tip(info, OnFkBone(e));
            Text.tooltip = tip.Length > 0 ? Text.text + "\n" + tip : Text.text;
            Lag.style.display = info.Lag != 0 ? DisplayStyle.Flex : DisplayStyle.None;
            Mark.text = ConstraintText.Mark(info);
            Mark.tooltip = tip;
            Mark.EnableInClassList("hs-constraints__mark--warning", Mark.text == "!");
            Mark.style.display = Mark.text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (!Weight.Dragging && !drag.Open) Weight.SetDisplay(e.AppliedWeight, false);
            Weight.tooltip = L10n.Get("constraints.weight") + " " + NumberText.Of(e.AppliedWeight, "0.##");
            Weight.SetEnabled(editable);
            Up.SetEnabled(editable && listIndex > 0);
            Down.SetEnabled(editable && listIndex < count - 1);
            Remove.SetEnabled(editable);
        }

        // the bone a target names is one of the character's FK bones
        bool OnFkBone(ConstraintEntry e)
        {
            if (e.Target?.Bone == null) return false;
            var r = ctx.Runtime?.FindChara(e.Target.Node);
            if (r == null || r.State != RealizationState.Built) return false;
            var bone = AdvancedPoseResolver.Resolve(r, e.Target.Bone);
            var table = FkDriver.TableFor(r);
            if (!bone || table == null) return false;
            foreach (var b in table.Bones)
                if (b != null && b.Bone == bone) return true;
            return false;
        }

        void OnSwitched(ChangeEvent<bool> e)
        {
            if (e.target != On || Id == null) return;
            ctx.ShowResult(ctx.Ops.SetConstraint(Id, new ConstraintEdit { On = e.newValue }, UiContext.User(OpLabels.Constraint)));
            changed();
        }

        void OnTextDown(PointerDownEvent e)
        {
            if (e.button == 0 && Id != null) clicked?.Invoke(Id);
        }

        void OnWeight(EditPhase phase, double v)
        {
            switch (phase)
            {
                case EditPhase.Begin: drag.Begin(Id); break;
                case EditPhase.End: drag.End(); break;
                case EditPhase.Preview: drag.Step(new ConstraintEdit { Weight = (float)v }); break;
                default:
                    ctx.ShowResult(ctx.Ops.SetConstraint(Id, new ConstraintEdit { Weight = (float)v }, UiContext.User(OpLabels.Constraint, "ui.typed/constraint/" + Id + "/weight")));
                    changed();
                    break;
            }
        }

        void MoveUp() => Move(index - 1);
        void MoveDown() => Move(index + 1);

        void Move(int to)
        {
            if (Id == null) return;
            ctx.ShowResult(ctx.Ops.MoveConstraint(Id, to, UiContext.User(OpLabels.ConstraintReorder)));
            changed();
        }

        void RemoveEntry()
        {
            if (Id == null) return;
            ctx.ShowResult(ctx.Ops.RemoveConstraint(Id, UiContext.User(OpLabels.ConstraintRemove)));
            changed();
        }

        /// <summary>Ends what the row holds open.</summary>
        public void EndEdit() => drag.Cancel();
    }
}
