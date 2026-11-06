// The right side of the Constraints panel: the selected entry, or the form of a new one.
//   Source, Target   the kind, a node picker with «Use selection», the bone field (it opens the bone popup), for a target the 13 IK targets
//   Components       five switches; what cannot stand with those on, or what the target cannot take, is off
//   Offset           a row for each offset the components use, «Take offset», «Clear offset»
//   Weight, Label
// A new entry: the first complete pair creates it, with the offset that keeps the target where it shows, one entry «Add constraint». An
// end that is being changed is held here until it is complete; a refusal (a loop, a mosaic part, a bone the node lacks) stands under
// the pickers, nothing changes and the end stays as chosen, so it can be corrected. A node or a bone that is gone shows its stored name in the
// warning colour; the row stays and can be pointed elsewhere. Element names: constraints-editor, constraints-hint, constraints-title,
// constraints-source-kind, -node, -use, -bone, the same for constraints-target-, constraints-target-ik, constraints-error, constraints-comp-<name>,
// constraints-offset, constraints-offset-pos, -rot, -scale, constraints-take-offset, constraints-clear-offset, constraints-weight, constraints-label.
using System;
using System.Collections.Generic;
using HS2Next.Motion.Ik;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Constraints;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    public sealed class ConstraintEditor : ScrollView
    {
        const int KindNode = 0, KindBone = 1, KindIk = 2;
        const string Warning = "hs-text--warning";
        /// <summary>How the operation's refusal of a loop starts; the rows follow.</summary>
        const string LoopRefusal = "constraint cycle: ";

        /// <summary>One end as it stands in the form.</summary>
        sealed class EndForm
        {
            public int Kind;
            public string Node, Bone, Ik;
            /// <summary>The user changed it and it is not stored.</summary>
            public bool Dirty;
            public bool Complete => Node != null && (Kind == KindNode || (Kind == KindBone && !string.IsNullOrEmpty(Bone)) || (Kind == KindIk && Ik != null));
            public ConstraintEnd End() => new ConstraintEnd(Node, Kind == KindBone ? Bone : null, Kind == KindIk ? Ik : null);

            public void From(ConstraintEnd e)
            {
                Node = e?.Node;
                Bone = e?.Bone;
                Ik = e?.Ik;
                Kind = Bone != null ? KindBone : Ik != null ? KindIk : KindNode;
                Dirty = false;
            }

            public bool Same(ConstraintEnd e) => e != null && e.SameAs(End());
        }

        /// <summary>The widgets of one end.</summary>
        sealed class EndUi
        {
            public EndForm Form;
            public bool IsTarget;
            public SegmentedButtons Kind;
            public RefPicker Node;
            public Button Use, Bone;
        }

        static readonly string[] DefaultComponents = { ConstraintComponents.Position, ConstraintComponents.Rotation };

        readonly UiContext ctx;
        readonly ConstraintsPanel owner;
        readonly Label hint, title, error, adding;
        readonly VisualElement body, offsets, bottom;
        readonly EndUi source, target;
        readonly DropdownField ikField;
        readonly Dictionary<string, Toggle> toggles = new Dictionary<string, Toggle>(StringComparer.Ordinal);
        readonly ConstraintOffsetRow posRow, rotRow, scaleRow;
        readonly Button takeOffset, clearOffset;
        readonly NumberSlider weight;
        readonly TextField label;
        readonly ConstraintDrag weightDrag;
        readonly List<string> draft = new List<string>();
        string id;
        bool isNew;

        /// <summary>The entry shown, or null.</summary>
        public string Id => id;
        /// <summary>The form of a new entry is open.</summary>
        public bool Adding => isNew;
        /// <summary>The text under the pickers ("" = none).</summary>
        public string ErrorText => error.text;
        public ConstraintOffsetRow PosRow => posRow;
        public ConstraintOffsetRow RotRow => rotRow;
        public ConstraintOffsetRow ScaleRow => scaleRow;
        public NumberSlider Weight => weight;
        public ConstraintDrag WeightDrag => weightDrag;
        /// <summary>The last write's result (tests).</summary>
        public OpResult LastResult { get; private set; }

        internal UiContext Ctx => ctx;
        internal ConstraintEntry Entry => id != null ? ctx.Model.Constraints?.Find(id) : null;

        public ConstraintEditor(UiContext ctx, ConstraintsPanel owner) : base(ScrollViewMode.Vertical)
        {
            this.ctx = ctx;
            this.owner = owner;
            name = "constraints-editor";
            horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            AddToClassList("hs-constraints__editor");
            hint = InspectorUi.Note("constraints.selectHint");
            hint.name = "constraints-hint";
            hint.style.whiteSpace = WhiteSpace.Normal;
            Add(hint);
            body = new VisualElement { name = "constraints-editor-body" };
            Add(body);
            title = new Label { name = "constraints-title" };
            title.AddToClassList("hs-section__title");
            body.Add(title);
            adding = InspectorUi.Note("constraints.newHint");
            adding.name = "constraints-new-hint";
            adding.style.whiteSpace = WhiteSpace.Normal;
            body.Add(adding);

            source = End("source", "constraints.source", false, new[] { "constraints.kind.node", "constraints.kind.bone" });
            target = End("target", "constraints.target", true, new[] { "constraints.kind.node", "constraints.kind.bone", "constraints.kind.ik" });
            ikField = new DropdownField(new List<string>(), -1) { name = "constraints-target-ik" };
            Loc.Bind(ikField, "constraints.kind.ik", (e, s) => ((DropdownField)e).label = s);
            ikField.RegisterValueChangedCallback(OnIkPicked);
            body.Add(ikField);
            error = new Label { name = "constraints-error" };
            error.AddToClassList("hs-constraints__error");
            body.Add(error);

            var compTitle = Loc.Label("constraints.components");
            compTitle.AddToClassList("hs-section__title");
            body.Add(compTitle);
            var comps = new VisualElement { name = "constraints-components" };
            comps.AddToClassList("hs-constraints__comps");
            foreach (var c in ConstraintComponents.All)
            {
                string component = c;
                var t = new Toggle { name = "constraints-comp-" + c };
                t.AddToClassList("hs-field__toggle");
                Loc.Bind(t, "constraints.comp." + c, (e, s) => ((Toggle)e).label = s);
                if (c == ConstraintComponents.Parent) Loc.Tooltip(t, "constraints.comp.parentTip");
                t.RegisterValueChangedCallback(e => { if (e.target == t) SetComponent(component, e.newValue); });
                comps.Add(t);
                toggles[c] = t;
            }
            body.Add(comps);

            offsets = new VisualElement { name = "constraints-offset" };
            var offTitle = Loc.Label("constraints.offset");
            offTitle.AddToClassList("hs-section__title");
            offsets.Add(offTitle);
            posRow = new ConstraintOffsetRow(this, "pos", "constraints.comp.position", 0);
            rotRow = new ConstraintOffsetRow(this, "rot", "constraints.comp.rotation", 1);
            scaleRow = new ConstraintOffsetRow(this, "scale", "constraints.comp.scale", 2);
            offsets.Add(posRow);
            offsets.Add(rotRow);
            offsets.Add(scaleRow);
            takeOffset = InspectorUi.Button("constraints.takeOffset", TakeOffset, "constraints-take-offset");
            clearOffset = InspectorUi.Button("constraints.clearOffset", ClearOffset, "constraints-clear-offset");
            var offLine = InspectorUi.Line(takeOffset, clearOffset);
            offLine.style.flexWrap = Wrap.Wrap;
            offsets.Add(offLine);
            body.Add(offsets);

            bottom = new VisualElement { name = "constraints-values" };
            weightDrag = new ConstraintDrag(ctx, Changed);
            weight = new NumberSlider(L10n.Get("constraints.weight"), 0, 1) { name = "constraints-weight", TypedMin = 0, TypedMax = 1 };
            Loc.Bind(weight.LabelElement, "constraints.weight");
            weight.Edited += OnWeight;
            bottom.Add(weight);
            label = new TextField { name = "constraints-label", isDelayed = true, maxLength = ConstraintLimits.MaxLabel };
            Loc.Bind(label, "constraints.label", (e, s) => ((TextField)e).label = s);
            label.RegisterValueChangedCallback(OnLabel);
            bottom.Add(label);
            body.Add(bottom);
            Edit(null);
        }

        EndUi End(string key, string titleKey, bool isTarget, string[] kinds)
        {
            var u = new EndUi { Form = new EndForm(), IsTarget = isTarget };
            var head = Loc.Label(titleKey);
            head.AddToClassList("hs-section__title");
            body.Add(head);
            var labels = new List<string>();
            foreach (var k in kinds) labels.Add(L10n.Get(k));
            u.Kind = new SegmentedButtons(labels) { name = "constraints-" + key + "-kind" };
            for (int i = 0; i < kinds.Length; i++) Loc.Bind(u.Kind.Buttons[i], kinds[i]);
            u.Kind.Selected += k => SetKind(u, k);
            body.Add(u.Kind);
            u.Node = new RefPicker(L10n.Get("constraints.kind.node")) { name = "constraints-" + key + "-node", Choices = () => NodeChoices(u) };
            Loc.Bind(u.Node.LabelElement, "constraints.kind.node");
            u.Node.style.flexGrow = 1;
            u.Node.style.flexShrink = 1;
            u.Node.Picked += n => SetNode(u, n);
            u.Use = InspectorUi.Button("constraints.useSelection", () => UseSelection(u), "constraints-" + key + "-use");
            var line = InspectorUi.Line(u.Node, u.Use);
            line.style.flexWrap = Wrap.Wrap;
            body.Add(line);
            var boneLabel = Loc.Label("constraints.kind.bone");
            boneLabel.AddToClassList("hs-ref__label");
            u.Bone = new Button(() => OpenBones(u)) { name = "constraints-" + key + "-bone" };
            u.Bone.AddToClassList("hs-ref__button");
            u.Bone.style.flexGrow = 1;
            u.Bone.style.flexShrink = 1;
            var boneLine = InspectorUi.Line(boneLabel, u.Bone);
            boneLine.name = "constraints-" + key + "-bone-line";
            body.Add(boneLine);
            return u;
        }

        // ------------------------------------------------------------ what is shown

        /// <summary>Shows an entry (null: none).</summary>
        public void Edit(string entry)
        {
            id = entry;
            isNew = false;
            var e = Entry;
            source.Form.From(e?.Source);
            target.Form.From(e?.Target);
            SetError(null);
            Read();
        }

        /// <summary>Opens the form of a new entry.</summary>
        public void BeginAdd(ConstraintEnd preset)
        {
            id = null;
            isNew = true;
            source.Form.From(null);
            target.Form.From(preset);
            draft.Clear();
            draft.AddRange(DefaultComponents);
            SetError(null);
            Read();
        }

        /// <summary>Reads the entry and shows it; a value being dragged stays as it is.</summary>
        public void Read()
        {
            var e = Entry;
            if (id != null && e == null) { id = null; isNew = false; }
            bool any = id != null || isNew;
            hint.style.display = any ? DisplayStyle.None : DisplayStyle.Flex;
            body.style.display = any ? DisplayStyle.Flex : DisplayStyle.None;
            if (!any) return;
            if (e != null)
            {
                if (!source.Form.Dirty) source.Form.From(e.Source);
                if (!target.Form.Dirty) target.Form.From(e.Target);
                title.text = ConstraintResolver.RowText(ctx.Runtime, e);
            }
            else title.text = L10n.Get("constraints.add");
            adding.style.display = isNew ? DisplayStyle.Flex : DisplayStyle.None;
            ShowEnd(source);
            ShowEnd(target);
            ShowIk();
            ShowComponents();
            offsets.style.display = isNew ? DisplayStyle.None : DisplayStyle.Flex;
            bottom.style.display = isNew ? DisplayStyle.None : DisplayStyle.Flex;
            if (e == null) return;
            bool pos = e.Has(ConstraintComponents.Position) || e.Has(ConstraintComponents.Parent);
            bool rot = e.Has(ConstraintComponents.Rotation) || e.Has(ConstraintComponents.LookAt) || e.Has(ConstraintComponents.Parent);
            bool scale = e.Has(ConstraintComponents.Scale);
            posRow.style.display = pos ? DisplayStyle.Flex : DisplayStyle.None;
            rotRow.style.display = rot ? DisplayStyle.Flex : DisplayStyle.None;
            scaleRow.style.display = scale ? DisplayStyle.Flex : DisplayStyle.None;
            posRow.Show(e.Offset.Pos);
            rotRow.Show(e.Offset.Rot);
            scaleRow.Show(e.Offset.Scale);
            if (!weight.Dragging && !weightDrag.Open) weight.SetDisplay(e.AppliedWeight, false);
            if (label.panel == null || label.focusController?.focusedElement != label) label.SetValueWithoutNotify(e.Label ?? "");
        }

        bool EndLost(EndUi u)
        {
            var f = u.Form;
            if (f.Kind != KindBone || string.IsNullOrEmpty(f.Bone) || f.Node == null) return false;
            var end = f.End();
            var state = u.IsTarget ? ConstraintResolver.Target(ctx.Runtime, end, ConstraintParts.Position).State : ConstraintResolver.Source(ctx.Runtime, end).State;
            return state == ConstraintEndState.Missing;
        }

        void ShowEnd(EndUi u)
        {
            var f = u.Form;
            var n = f.Node != null ? ctx.Model.Find(f.Node) : null;
            u.Kind.SetDisplay(f.Kind, false);
            u.Node.SetDisplay(f.Node, n != null ? n.Label : f.Node, false);
            u.Node.Button.EnableInClassList(Warning, f.Node != null && n == null);
            u.Bone.parent.style.display = f.Kind == KindBone ? DisplayStyle.Flex : DisplayStyle.None;
            u.Bone.text = string.IsNullOrEmpty(f.Bone) ? L10n.Get("constraints.pickBone") : f.Bone;
            u.Bone.EnableInClassList(Warning, EndLost(u));
            u.Bone.SetEnabled(n != null);
        }

        void ShowIk()
        {
            var f = target.Form;
            ikField.style.display = f.Kind == KindIk ? DisplayStyle.Flex : DisplayStyle.None;
            if (f.Kind != KindIk) return;
            var names = new List<string>();
            foreach (var t in IkTargets.Names) names.Add(L10n.Get("ik.target." + t));
            ikField.choices = names;
            int i = f.Ik != null ? Array.IndexOf(IkTargets.Names, f.Ik) : -1;
            ikField.SetValueWithoutNotify(i >= 0 ? names[i] : "");
        }

        IReadOnlyList<string> Components
        {
            get
            {
                var e = Entry;
                return e != null ? e.Components : (IReadOnlyList<string>)draft;
            }
        }

        // what the target as it stands in the form can take
        bool Takes(string component)
        {
            var f = target.Form;
            if (f.Kind == KindIk)
            {
                if (component == ConstraintComponents.Scale) return false;
                if (component != ConstraintComponents.Rotation && component != ConstraintComponents.LookAt) return true;
                return f.Ik == null || !Enum.TryParse(f.Ik, out IkTargetId t) || IkGroupInfo.UsesRotation(t);
            }
            if (f.Kind == KindNode && component == ConstraintComponents.Scale && f.Node != null)
            {
                var n = ctx.Model.Find(f.Node);
                return n == null || StudioOps.HasScaleChannel(n);
            }
            return true;
        }

        void ShowComponents()
        {
            var on = Components;
            int count = on?.Count ?? 0;
            foreach (var kv in toggles)
            {
                bool has = on != null && Contains(on, kv.Key);
                kv.Value.SetValueWithoutNotify(has);
                bool refused = false;
                if (on != null)
                    foreach (var other in on)
                        if (other != kv.Key && ConstraintComponents.Refused(kv.Key, other)) refused = true;
                kv.Value.SetEnabled(has ? count > 1 : !refused && Takes(kv.Key));
            }
        }

        static bool Contains(IReadOnlyList<string> l, string s)
        {
            for (int i = 0; i < l.Count; i++) if (l[i] == s) return true;
            return false;
        }

        void SetError(string text)
        {
            error.text = text ?? "";
            error.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        // ------------------------------------------------------------ the ends

        static bool Fits(TreeNode n, int kind, bool isTarget)
        {
            if (n.Kind == NodeKind.Opaque) return false;
            if (kind == KindBone) return n.Kind == NodeKind.Chara || (!isTarget && n.Kind == NodeKind.Item);
            return kind != KindIk || n.Kind == NodeKind.Chara;
        }

        IReadOnlyList<(string value, string label)> NodeChoices(EndUi u)
        {
            var l = new List<(string, string)>();
            foreach (var n in ctx.Model.PreOrder())
                if (Fits(n, u.Form.Kind, u.IsTarget)) l.Add((n.Id, n.Label));
            return l;
        }

        void SetKind(EndUi u, int kind)
        {
            var f = u.Form;
            if (f.Kind == kind) return;
            f.Kind = kind;
            if (kind != KindBone) f.Bone = null;
            if (kind != KindIk) f.Ik = null;
            var n = f.Node != null ? ctx.Model.Find(f.Node) : null;
            if (n != null && !Fits(n, kind, u.IsTarget)) f.Node = null;
            Touched(f);
        }

        void SetNode(EndUi u, string node)
        {
            u.Form.Node = node;
            Touched(u.Form);
        }

        /// <summary>The source's or the target's kind as its buttons set it: 0 node, 1 bone, 2 IK target.</summary>
        public void PickKind(bool isTarget, int kind) => SetKind(isTarget ? target : source, kind);
        /// <summary>A pick in the node list of the source or the target.</summary>
        public void PickNode(bool isTarget, string node) => SetNode(isTarget ? target : source, node);
        /// <summary>A bone chosen in the popup of the source or the target.</summary>
        public void PickBone(bool isTarget, string address)
        {
            var f = (isTarget ? target : source).Form;
            f.Bone = address;
            Touched(f);
        }

        void UseSelection(EndUi u)
        {
            string p = ctx.Selection.Primary;
            if (p == null) return;
            var f = u.Form;
            f.Node = p;
            if (f.Kind == KindBone && StudioOps.TryParseSub(ctx.SubSelection, out var who, out var kind, out var bone) && who == p && kind == StudioOps.BoneSubKind) f.Bone = bone;
            Touched(f);
        }

        void OpenBones(EndUi u)
        {
            var f = u.Form;
            if (f.Node == null) return;
            ConstraintBonePopup.Open(ctx, u.Bone, f.Node, u.IsTarget, a =>
            {
                f.Bone = a;
                Touched(f);
            });
        }

        void OnIkPicked(ChangeEvent<string> e)
        {
            if (e.target != ikField) return;
            int i = ikField.index;
            if (i < 0 || i >= IkTargets.Names.Length) return;
            target.Form.Ik = IkTargets.Names[i];
            Touched(target.Form);
        }

        // an end changed in the form: stored when it is complete
        void Touched(EndForm f)
        {
            f.Dirty = true;
            SetError(null);
            var e = Entry;
            if (e != null && f.Complete && f.Same(f == source.Form ? e.Source : e.Target)) f.Dirty = false;
            if (isNew) TryAdd();
            else if (e != null) TrySet();
            Read();
        }

        void TryAdd()
        {
            if (!source.Form.Complete || !target.Form.Complete) return;
            var set = new List<string>();
            foreach (var c in draft) if (Takes(c)) set.Add(c);
            if (set.Count == 0) set.Add(ConstraintComponents.Position);
            var r = ctx.Ops.AddConstraint(new ConstraintAddRequest { Source = source.Form.End(), Target = target.Form.End(), Components = set, KeepOffset = true }, UiContext.User(OpLabels.ConstraintAdd));
            LastResult = r;
            if (!r.Ok) { SetError(RefusalText(r)); return; }
            foreach (var w in r.Warnings) ctx.Toasts?.Show(w, ToastKind.Warning);
            isNew = false;
            source.Form.Dirty = target.Form.Dirty = false;
            owner.MarkDirty();
            owner.Select((r.Value as ConstraintInfo)?.Entry?.Id);
        }

        void TrySet()
        {
            var edit = new ConstraintEdit();
            bool changes = false;
            if (source.Form.Dirty && source.Form.Complete) { edit.Source = source.Form.End(); changes = true; }
            if (target.Form.Dirty && target.Form.Complete) { edit.Target = target.Form.End(); changes = true; }
            if (!changes) return;
            var r = Write(edit);
            if (r.Ok) source.Form.Dirty = target.Form.Dirty = false;
        }

        // ------------------------------------------------------------ the rest

        OpResult Write(ConstraintEdit edit, string label = OpLabels.Constraint, string mergeKey = null)
        {
            var r = ctx.Ops.SetConstraint(id, edit, UiContext.User(label, mergeKey));
            LastResult = r;
            SetError(r.Ok ? null : RefusalText(r));
            Changed();
            return r;
        }

        // A refusal under the pickers, in the UI's language: a loop with the rows the operation names, the mosaic's rule. The rest as the
        // operation says it. The command's own text does not change.
        static string RefusalText(OpResult r)
        {
            string m = r.Message ?? "";
            if (m == AdvancedPoseGuard.Refusal) return L10n.Get("constraints.state.protected");
            return m.StartsWith(LoopRefusal, StringComparison.Ordinal) ? L10n.Format("constraints.cycleRefused", m.Substring(LoopRefusal.Length)) : m;
        }

        void Changed() => owner.MarkDirty();

        void SetComponent(string component, bool on)
        {
            var next = new List<string>(Components ?? Array.Empty<string>());
            if (on) { if (!next.Contains(component)) next.Add(component); }
            else next.Remove(component);
            if (isNew)
            {
                draft.Clear();
                draft.AddRange(next);
                ShowComponents();
                return;
            }
            Write(new ConstraintEdit { Components = next });
            Read();
        }

        void TakeOffset()
        {
            if (id != null) Write(new ConstraintEdit { TakeOffset = true }, OpLabels.ConstraintTakeOffset);
        }

        void ClearOffset()
        {
            if (id != null) Write(new ConstraintEdit { OffsetPos = Vec3.Zero, OffsetRot = Vec3.Zero, OffsetScale = Vec3.One });
        }

        void OnWeight(EditPhase phase, double v)
        {
            if (id == null) return;
            switch (phase)
            {
                case EditPhase.Begin: weightDrag.Begin(id); break;
                case EditPhase.End: weightDrag.End(); break;
                case EditPhase.Preview: weightDrag.Step(new ConstraintEdit { Weight = (float)v }); break;
                default: Write(new ConstraintEdit { Weight = (float)v }, OpLabels.Constraint, "ui.typed/constraint/" + id + "/weight"); break;
            }
        }

        void OnLabel(ChangeEvent<string> e)
        {
            if (e.target == label && id != null) Write(new ConstraintEdit { Label = e.newValue ?? "" }, OpLabels.Constraint, "ui.typed/constraint/" + id + "/label");
        }

        /// <summary>Ends what the editor holds open.</summary>
        public void EndEdits()
        {
            weightDrag.Cancel();
            posRow.EndEdit();
            rotRow.EndEdit();
            scaleRow.EndEdit();
        }

        internal OpResult TypedOffset(string member, Vec3 v)
        {
            var edit = new ConstraintEdit();
            Assign(edit, member, v);
            return Write(edit, OpLabels.Constraint, "ui.typed/constraint/" + id + "/" + member);
        }

        internal static void Assign(ConstraintEdit edit, string member, Vec3 v)
        {
            if (member == "pos") edit.OffsetPos = v;
            else if (member == "rot") edit.OffsetRot = v;
            else edit.OffsetScale = v;
        }
    }

    /// <summary>One offset of the entry: a vector row whose label drag is one history entry; typed values chain.</summary>
    public sealed class ConstraintOffsetRow : VisualElement
    {
        readonly ConstraintEditor editor;
        readonly string member;
        readonly ConstraintDrag drag;

        public readonly VectorField Field;
        /// <summary>The drag (tests).</summary>
        public ConstraintDrag Drag => drag;
        /// <summary>The last write's result (tests).</summary>
        public OpResult LastResult { get; private set; }

        internal ConstraintOffsetRow(ConstraintEditor editor, string member, string labelKey, int channel)
        {
            this.editor = editor;
            this.member = member;
            name = "constraints-offset-" + member;
            AddToClassList("hs-field");
            drag = new ConstraintDrag(editor.Ctx, Ended);
            Field = new VectorField(L10n.Get(labelKey), 3) { Format = channel == 1 ? "0.###" : "0.#####" };
            Loc.Bind(Field.LabelElement, labelKey);
            Field.Edited += OnEdited;
            Add(Field);
        }

        void Ended() => editor.Read();

        internal void Show(Vec3 v)
        {
            if (!Field.Dragging && !drag.Open) Field.SetDisplay(new Vector4(v.X, v.Y, v.Z, 0), null);
        }

        internal void EndEdit() => drag.Cancel();

        void OnEdited(EditPhase phase, Vector4 v)
        {
            string id = editor.Id;
            if (id == null) return;
            if (phase == EditPhase.Begin) { drag.Begin(id); return; }
            if (phase == EditPhase.End) { LastResult = drag.End(); return; }
            var value = new Vec3(v.x, v.y, v.z);
            if (phase == EditPhase.Preview)
            {
                if (!drag.Open) return;   // Esc took the drag back: its last moves write nothing
                var edit = new ConstraintEdit();
                ConstraintEditor.Assign(edit, member, value);
                drag.Step(edit);
                return;
            }
            LastResult = editor.TypedOffset(member, value);
        }
    }
}
