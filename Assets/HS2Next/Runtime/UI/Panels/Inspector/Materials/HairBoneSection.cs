// The section Hair bone adjustments of the Edit tab's Hair page: the bone modifiers of the character on the bones of its hair (the hair
// root and what lies under it). A modifier is stored with the character, in body.boneMods, and every other entry of that array stays as it
// is. The section reads nothing until it is first opened. An added bone has a row before it has an entry: a modifier with no effect is
// not stored, so the entry comes with the first value, and goes when the row comes back to no effect. Below the rows: the entries whose bone
// is found nowhere on this character, neither on the body nor, for an entry that may stand there, on an accessory; each with its Remove.
// Steps of a drag start from the array at the press (ArrayDrag).
using System;
using System.Collections.Generic;
using HS2Next.Chara.Ext;
using HS2Next.Chara.Mosaic;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Binding;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    public sealed class HairBoneSection : VisualElement
    {
        public const int RefreshMs = 500;
        const string LabelKey = "inspector.charaEdit.hair.boneMods";

        readonly UiContext ctx;
        readonly string primary;
        readonly List<HairBoneRow> rowList = new List<HairBoneRow>();
        readonly List<string> added = new List<string>();
        VisualElement rowsBox;
        RefPicker addPicker;
        AdvancedPoseTab.StaleList stale;
        IVisualElementScheduledItem refresh;
        string shownKey;
        bool built;

        /// <summary>The rows shown (tests).</summary>
        public IReadOnlyList<HairBoneRow> Rows => rowList;
        public AdvancedPoseTab.StaleList Stale => stale;
        public RefPicker AddPicker => addPicker;
        /// <summary>True once it built its elements (tests: not before it is opened).</summary>
        public bool Built => built;
        /// <summary>Refreshes so far (tests).</summary>
        public int Refreshes { get; private set; }

        /// <summary>The foldout of the section: collapsed, its content built when first open.</summary>
        public static Foldout Section(UiContext ctx, string primary)
        {
            var fo = InspectorUi.Section(LabelKey, false, "edit-section-" + InspectorSchemas.HairBoneModsSection);
            fo.RegisterValueChangedCallback(e =>
            {
                if (e.target != fo || !e.newValue || fo.Q<HairBoneSection>() != null) return;
                fo.Add(new HairBoneSection(ctx, primary));
            });
            return fo;
        }

        HairBoneSection(UiContext ctx, string primary)
        {
            this.ctx = ctx;
            this.primary = primary;
            name = "hair-bones";
            RegisterCallback<AttachToPanelEvent>(_ => Opened());
            RegisterCallback<DetachFromPanelEvent>(_ => Closed());
        }

        internal UiContext Ctx => ctx;
        internal string Primary => primary;
        internal List<BodyBoneMod> Array => ctx.Ops.BoneModArray(primary);

        CharaRealization Real
        {
            get
            {
                var r = ctx.Runtime?.FindChara(primary);
                return r != null && r.State == RealizationState.Built && !r.InGraveyard && r.Root ? r : null;
            }
        }

        void Opened()
        {
            if (!built) Build();
            else refresh.Resume();
            Refresh();
        }

        void Closed()
        {
            refresh?.Pause();
            foreach (var r in rowList) r.CancelEdit();
        }

        void Build()
        {
            built = true;
            var note = InspectorUi.Note(LabelKey + ".note");
            note.name = "hair-bones-note";
            note.style.whiteSpace = WhiteSpace.Normal;
            Add(note);
            rowsBox = new VisualElement { name = "hair-bones-rows" };
            Add(rowsBox);
            addPicker = new RefPicker(L10n.Get(LabelKey + ".add")) { name = "hair-bones-add", Choices = AddChoices };
            Loc.Bind(addPicker.LabelElement, LabelKey + ".add");
            addPicker.Picked += AddBone;
            addPicker.SetDisplay(null, null, false);
            Add(addPicker);
            stale = new AdvancedPoseTab.StaleList("hair-bones-stale", bone => Remove(bone));
            Add(stale);
            refresh = schedule.Execute(Refresh).Every(RefreshMs);
            ctx.Bindings.Bind(this, TargetSelector.Node(primary), BoneModEdits.Path, (el, v) => Changed());
        }

        bool ShownNow()
        {
            if (panel == null) return false;
            for (VisualElement e = this; e != null; e = e.hierarchy.parent)
                if (e.resolvedStyle.display == DisplayStyle.None) return false;
            return true;
        }

        bool Editing => ctx.ActiveEdit != null && ctx.ActiveEdit.IsOpen;

        // The bones the Add list offers: the hair class without a row, without what the mosaic rule guards.
        IReadOnlyList<(string value, string label)> AddChoices()
        {
            var l = new List<(string, string)>();
            var cc = Real?.Context;
            if (cc == null) return l;
            var shown = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in rowList) shown.Add(r.Bone);
            foreach (var t in HairBones.Of(cc))
                if (shown.Add(t.name) && !MosaicSet.GuardsBone(cc, t)) l.Add((t.name, t.name));
            return l;
        }

        /// <summary>A row for a hair bone with no entry yet.</summary>
        public void AddBone(string bone)
        {
            if (string.IsNullOrEmpty(bone) || added.Contains(bone)) return;
            added.Add(bone);
            Show();
        }

        /// <summary>Removes the entries of a bone (one entry «Hair bone adjustment»).</summary>
        public OpResult Remove(string bone)
        {
            added.Remove(bone);
            var r = ctx.ShowResult(ctx.Ops.ClearHairBoneMods(primary, bone, UiContext.User(OpLabels.HairBoneAdjust)));
            Changed();
            return r;
        }

        void Changed()
        {
            if (built && !Editing) Show();
        }

        /// <summary>The array, the bones and the live state now. Runs only while the section is shown.</summary>
        public void Refresh()
        {
            if (!built || !ShownNow() || Editing) return;
            Refreshes++;
            Show();
        }

        internal void EditEnded() => Changed();

        void Show()
        {
            var cc = Real?.Context;
            if (cc == null) return;
            var array = Array;
            var names = HairBones.Names(cc);
            var body = AbmxApplier.BodyDictionary(cc);
            var want = new List<string>();
            var lost = new List<string>();
            Dictionary<string, Transform> onAccessories = null;
            foreach (var m in array)
            {
                if (m.Bone == null) continue;
                if (names.Contains(m.Bone)) { if (!want.Contains(m.Bone)) want.Add(m.Bone); continue; }
                if (AbmxApplier.Find(body, m.Bone)) continue;
                // an entry without a location goes on to the accessories when the body lacks the bone: it acts there
                if (m.Location != BodyBoneMod.LocationBodyTop)
                {
                    onAccessories = onAccessories ?? HairBones.AccessoryBones(cc);
                    if (AbmxApplier.Find(onAccessories, m.Bone)) continue;
                }
                if (!lost.Contains(m.Bone)) lost.Add(m.Bone);
            }
            foreach (var b in added) if (names.Contains(b) && !want.Contains(b)) want.Add(b);
            string key = string.Join("\n", want);
            if (key != shownKey)
            {
                shownKey = key;
                foreach (var row in rowList) row.CancelEdit();
                rowsBox.Clear();
                rowList.Clear();
                foreach (var b in want)
                {
                    var row = new HairBoneRow(this, b);
                    rowList.Add(row);
                    rowsBox.Add(row);
                }
            }
            foreach (var row in rowList)
            {
                int i = BoneModEdits.LastIndex(array, row.Bone);
                var t = HairBones.Find(cc, row.Bone);
                row.Show(i >= 0 ? array[i] : null, t && MosaicSet.GuardsBone(cc, t));
            }
            stale.Set(lost);
        }
    }

    /// <summary>The modifier of one hair bone: scale, length, position, rotation, ↺.</summary>
    public sealed class HairBoneRow : VisualElement
    {
        const string LabelKey = "inspector.charaEdit.hair.boneMods.";

        readonly HairBoneSection section;
        readonly ArrayDrag<BodyBoneMod> drag;
        OpResult lastStored;

        public string Bone { get; }
        public readonly VectorField Scale, Position, Rotation;
        public readonly NumberSlider Length;
        public readonly Button ResetButton;
        /// <summary>The string key of why the row is off, or null.</summary>
        public string OffKey { get; private set; }
        public OpResult LastResult => lastStored ?? drag.LastResult;

        internal HairBoneRow(HairBoneSection section, string bone)
        {
            this.section = section;
            Bone = bone;
            name = "hair-bone-" + bone;
            style.flexDirection = FlexDirection.Column;
            style.marginBottom = 4;
            drag = new ArrayDrag<BodyBoneMod>(section.Ctx, () => section.Array, section.EditEnded);
            var title = new Label(bone) { name = "hair-bone-name", tooltip = bone };
            title.AddToClassList("hs-row__label");
            title.style.flexGrow = 1;
            title.style.flexShrink = 1;
            title.style.overflow = Overflow.Hidden;
            title.style.textOverflow = TextOverflow.Ellipsis;
            ResetButton = new Button(() => section.Remove(Bone)) { name = "hair-bone-reset", text = "↺" };
            ResetButton.AddToClassList("hs-field__default");
            Loc.Tooltip(ResetButton, "inspector.chara.adv.reset");
            Add(InspectorUi.Line(title, ResetButton));
            Scale = Vector("scale", (p, v) => Edit(p, v, 0));
            Length = new NumberSlider(L10n.Get(LabelKey + "length"), 0, 2) { Format = "0.###", name = "hair-bone-length" };
            Loc.Bind(Length.LabelElement, LabelKey + "length");
            Length.Edited += OnLength;
            Add(Length);
            Position = Vector("position", (p, v) => Edit(p, v, 1));
            Rotation = Vector("rotation", (p, v) => Edit(p, v, 2));
        }

        VectorField Vector(string key, Action<EditPhase, Vector4> edited)
        {
            var f = new VectorField(L10n.Get(LabelKey + key), 3) { name = "hair-bone-" + key };
            Loc.Bind(f.LabelElement, LabelKey + key);
            f.Edited += edited;
            Add(f);
            return f;
        }

        internal void CancelEdit() => drag.Cancel();

        internal void Show(BodyBoneMod m, bool guarded)
        {
            if (drag.Open || Length.Dragging || Scale.Dragging || Position.Dragging || Rotation.Dragging) return;
            var scale = m?.Scale ?? Vec3.One;
            var position = m?.Position ?? Vec3.Zero;
            var rotation = m?.Rotation ?? Vec3.Zero;
            Scale.SetDisplay(new Vector4(scale.X, scale.Y, scale.Z, 0), null);
            Position.SetDisplay(new Vector4(position.X, position.Y, position.Z, 0), null);
            Rotation.SetDisplay(new Vector4(rotation.X, rotation.Y, rotation.Z, 0), null);
            Length.SetDisplay(m?.Length ?? 1f, false);
            if (guarded && OffKey == null)
            {
                OffKey = "inspector.materials.mosaic";
                SetEnabled(false);
                Loc.Tooltip(this, OffKey);
            }
        }

        void Write(Vec3? scale, float? length, Vec3? position, Vec3? rotation, bool step)
        {
            var ops = section.Ctx.Ops;
            if (step)
            {
                if (!drag.Open) return;
                lastStored = null;
                drag.Stepped(ops.SetHairBoneMod(section.Primary, Bone, scale, length, position, rotation, UiContext.User(OpLabels.HairBoneAdjust, ArrayDrag<BodyBoneMod>.StepKey), drag.AtPress));
                return;
            }
            string key = "ui.typed/" + section.Primary + "/hairBone/" + Bone;
            lastStored = section.Ctx.ShowResult(ops.SetHairBoneMod(section.Primary, Bone, scale, length, position, rotation, UiContext.User(OpLabels.HairBoneAdjust, key)));
            section.EditEnded();
        }

        void Edit(EditPhase phase, Vector4 v, int channel)
        {
            if (phase == EditPhase.Begin) { drag.Begin(OpLabels.HairBoneAdjust); return; }
            if (phase == EditPhase.End) { drag.End(); return; }
            var a = new Vec3(v.x, v.y, v.z);
            Write(channel == 0 ? a : (Vec3?)null, null, channel == 1 ? a : (Vec3?)null, channel == 2 ? a : (Vec3?)null, phase == EditPhase.Preview);
        }

        void OnLength(EditPhase phase, double v)
        {
            switch (phase)
            {
                case EditPhase.Begin: drag.Begin(OpLabels.HairBoneAdjust); break;
                case EditPhase.End: drag.End(); break;
                case EditPhase.Preview: Write(null, (float)v, null, null, true); break;
                case EditPhase.Typed: Write(null, (float)v, null, null, false); break;
            }
        }
    }
}
