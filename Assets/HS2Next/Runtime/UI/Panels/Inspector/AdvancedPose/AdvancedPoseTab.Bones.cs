// The Bones section of the Advanced pose tab: a part's bones as a virtualised tree (a character has about 780 transforms), the selected
// bone's three rows, Reset, Reset all and the entries this character lacks.
//   the tree      indent by depth; a mark for an FK row, for a bone the IK solve owns, for a correction; a warning tint when another stage
//                 hides the correction now (its tooltip says which); a part that keeps the mosaic in place is greyed and cannot be
//                 selected. Open: the way to cf_J_Hips and its first level, the way to cf_J_FaceRoot, the top of any other part. A search
//                 or Corrected only shows the matches with their ancestors.
//   selection     a row selected here and the sub-selection "<id>/bone/<address>" are the same thing: the bone's handle works on it, and a
//                 pick in the view brings its row into sight
//   the pick      off by default, off when the tab closes; the gizmo asks PickRows only while it is on
using System;
using System.Collections.Generic;
using System.Text;
using HS2Next.Chara;
using HS2Next.Motion.AdvancedPose;
using HS2Next.Motion.Corrective;
using HS2Next.Motion.DynamicBones;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Binding;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;
using UnityEngine;
using UnityEngine.UIElements;
using UiField = HS2Next.UI.Core.FieldSpec;

namespace HS2Next.UI.Panels
{
    /// <summary>A sphere of the pick: a bone that can be selected in the view.</summary>
    public readonly struct BonePick
    {
        public readonly string Address;
        public readonly Transform Bone;
        public readonly bool Corrected;
        public BonePick(string address, Transform bone, bool corrected) { Address = address; Bone = bone; Corrected = corrected; }
    }

    public sealed partial class AdvancedPoseTab
    {
        /// <summary>The spheres of the pick at most.</summary>
        public const int MaxPicks = 256;
        const float TreeRowHeight = 22f, TreeHeight = 264f, TreeIndent = 8f;

        /// <summary>One bone of the current part.</summary>
        public sealed class TreeRow
        {
            public PoseBoneRow Row;
            public string Key;
            public int Parent = -1;
            public bool HasChildren, Fk, Protected;
        }

        sealed class TreeLine
        {
            public int Index = -1;
            public Label Arrow, Name, FkMark, IkMark, Dot;
        }

        readonly List<TreeRow> rows = new List<TreeRow>();
        readonly Dictionary<string, int> rowOf = new Dictionary<string, int>(StringComparer.Ordinal);
        readonly List<int> visible = new List<int>();
        readonly HashSet<string> corrected = new HashSet<string>(StringComparer.Ordinal);
        readonly Dictionary<string, string> hidden = new Dictionary<string, string>(StringComparer.Ordinal);   // address -> why
        readonly HashSet<Transform> ikOwned = new HashSet<Transform>();
        readonly List<string> partKeys = new List<string>();
        readonly List<BonePick> picks = new List<BonePick>();
        ListView tree;
        DropdownField partField;
        TextField searchField;
        Toggle pickToggle, correctedToggle;
        VisualElement editor;
        Label selectNote, boneName;
        Button boneReset, resetAll;
        BoneChannelRow posRow, rotRow, scaleRow;
        StaleList boneStale;
        SearchQuery query = SearchQuery.Empty;
        bool picksDirty = true;

        /// <summary>The address of the selected bone, or null.</summary>
        public string SelectedBone { get; private set; }
        /// <summary>The bones of the current part, listed or not.</summary>
        public IReadOnlyList<TreeRow> PartRows => rows;
        /// <summary>The rows the tree lists now, as indices into <see cref="PartRows"/>.</summary>
        public IReadOnlyList<int> VisibleRows => visible;
        /// <summary>The bones a search or Corrected only matches now (ancestors not counted); every bone of the part without either.</summary>
        public int Matches { get; private set; }
        public string Part => view.Part;
        public ListView Tree => tree;
        public bool PickOn { get; private set; }
        public IReadOnlyList<string> PartChoices => partKeys;
        public BoneChannelRow PosRow => posRow;
        public BoneChannelRow RotRow => rotRow;
        public BoneChannelRow ScaleRow => scaleRow;
        public StaleList BoneStale => boneStale;

        void BuildBones(VisualElement into)
        {
            partField = new DropdownField(new List<string>(), 0) { name = "adv-part" };
            partField.AddToClassList("hs-grow");
            Loc.Bind(partField, "inspector.chara.adv.part", (e, s) => ((DropdownField)e).label = s);
            partField.RegisterValueChangedCallback(e =>
            {
                int i = partField.index;
                if (i >= 0 && i < partKeys.Count && partKeys[i] != view.Part) SetPart(partKeys[i]);
            });
            into.Add(partField);

            searchField = PanelKit.SearchField("adv-search", SetQuery);
            searchField.textEdition.placeholder = L10n.Get("inspector.chara.adv.search");
            Loc.Bind(searchField, "inspector.chara.adv.search", (e, t) => ((TextField)e).textEdition.placeholder = t);
            searchField.SetValueWithoutNotify(view.Query);
            query = SearchQuery.Parse(view.Query);
            into.Add(searchField);

            pickToggle = new Toggle { name = "adv-pick" };
            pickToggle.AddToClassList("hs-field__toggle");
            Loc.Bind(pickToggle, "inspector.chara.adv.pick", (e, s) => ((Toggle)e).label = s);
            pickToggle.RegisterValueChangedCallback(e => SetPick(e.newValue));
            correctedToggle = new Toggle { name = "adv-corrected", value = view.CorrectedOnly };
            correctedToggle.AddToClassList("hs-field__toggle");
            Loc.Bind(correctedToggle, "inspector.chara.adv.corrected", (e, s) => ((Toggle)e).label = s);
            correctedToggle.RegisterValueChangedCallback(e => SetCorrectedOnly(e.newValue));
            var toggles = InspectorUi.Line(pickToggle, correctedToggle);
            toggles.style.flexWrap = Wrap.Wrap;
            into.Add(toggles);

            tree = new ListView
            {
                name = "adv-tree", fixedItemHeight = TreeRowHeight, virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                selectionType = SelectionType.Single, itemsSource = visible, makeItem = MakeTreeLine, bindItem = BindTreeLine,
            };
            tree.style.height = TreeHeight;
            tree.selectionChanged += _ => TreeSelectionChanged();
            into.Add(tree);

            selectNote = InspectorUi.Note("inspector.chara.adv.selectBone");
            selectNote.name = "adv-select-note";
            into.Add(selectNote);
            editor = new VisualElement { name = "adv-bone-editor" };
            boneName = new Label { name = "adv-bone-name" };
            boneName.AddToClassList("hs-section__title");
            editor.Add(boneName);
            posRow = new BoneChannelRow(this, Row(InspectorSchemas.AdvBonePos), 0);
            rotRow = new BoneChannelRow(this, Row(InspectorSchemas.AdvBoneRot), 1);
            scaleRow = new BoneChannelRow(this, Row(InspectorSchemas.AdvBoneScale), 2);
            editor.Add(posRow);
            editor.Add(rotRow);
            editor.Add(scaleRow);
            boneReset = InspectorUi.Button("inspector.chara.adv.reset", ResetSelected, "adv-bone-reset");
            editor.Add(boneReset);
            into.Add(editor);

            resetAll = InspectorUi.Button("inspector.chara.adv.resetAll", ResetAll, "adv-reset-all");
            into.Add(resetAll);
            boneStale = new StaleList("adv-stale", RemoveStaleBone);
            into.Add(boneStale);
        }

        // ------------------------------------------------------------ the part

        void BonesStructure(CharaRealization r)
        {
            if (tree == null) return;
            partKeys.Clear();
            var labels = new List<string>();
            if (r != null)
            {
                partKeys.Add(AdvancedPoseResolver.BodyPart);
                partKeys.Add(AdvancedPoseResolver.HeadPart);
                var cc = r.Context;
                for (int k = 0; k < cc.Hair.Length; k++)
                    if (cc.Hair[k]) partKeys.Add(PoseAddressGrammar.PartText(PosePart.Hair, k, null));
                for (int k = 0; k < cc.Clothes.Length; k++)
                    if (cc.Clothes[k]) partKeys.Add(PoseAddressGrammar.PartText(PosePart.Clothes, k, null));
                if (cc.Accessories != null)
                    foreach (var s in cc.Accessories)
                    {
                        if (s == null || !s.Object) continue;
                        string id = r.AccessoryIdOf(s);
                        if (id != null) partKeys.Add(PoseAddressGrammar.PartText(PosePart.Accessory, -1, id));
                    }
                foreach (var p in partKeys) labels.Add(PartLabel(r, p));
            }
            partField.choices = labels;
            if (!partKeys.Contains(view.Part)) view.Part = AdvancedPoseResolver.BodyPart;
            int i = partKeys.IndexOf(view.Part);
            partField.SetValueWithoutNotify(i >= 0 ? labels[i] : "");
            ReadPart(r);
        }

        /// <summary>Shows the bones of another part (<c>body</c>, <c>head</c>, <c>hair:k</c>, <c>clothes:k</c>, <c>acc:id</c>).</summary>
        public void SetPart(string part)
        {
            view.Part = part;
            int i = partKeys.IndexOf(part);
            if (partField != null && i >= 0 && i < partField.choices.Count) partField.SetValueWithoutNotify(partField.choices[i]);
            ReadPart(Real);
        }

        void ReadPart(CharaRealization r)
        {
            rows.Clear();
            rowOf.Clear();
            if (r != null)
            {
                var list = AdvancedPoseResolver.Bones(r, view.Part);
                var fk = HS2Next.Scenes.Runtime.FkDriver.TableFor(r);
                var fkBones = new HashSet<Transform>();
                if (fk != null)
                    foreach (var b in fk.Bones)
                    {
                        if (b == null) continue;
                        if (b.Bone) fkBones.Add(b.Bone);
                        if (b.Partner) fkBones.Add(b.Partner);
                    }
                foreach (var row in list)
                {
                    var t = new TreeRow { Row = row, Key = SearchText.Normalize(row.Bone.name), Fk = fkBones.Contains(row.Bone), Protected = AdvancedPoseGuard.Bone(r, row.Bone) };
                    if (row.Parent != null && rowOf.TryGetValue(row.Parent, out int p))
                    {
                        t.Parent = p;
                        rows[p].HasChildren = true;
                    }
                    rowOf[row.Address] = rows.Count;
                    rows.Add(t);
                }
            }
            DefaultExpanded();
            Flatten();
        }

        HashSet<string> ExpandedSet
        {
            get
            {
                if (!view.Expanded.TryGetValue(view.Part, out var set)) view.Expanded[view.Part] = set = new HashSet<string>(StringComparer.Ordinal);
                return set;
            }
        }

        // the first time a part is listed
        void DefaultExpanded()
        {
            if (rows.Count == 0 || view.Expanded.ContainsKey(view.Part)) return;
            var set = ExpandedSet;
            string target = view.Part == AdvancedPoseResolver.BodyPart ? "cf_J_Hips" : view.Part == AdvancedPoseResolver.HeadPart ? "cf_J_FaceRoot" : null;
            if (target == null) { set.Add(rows[0].Row.Address); return; }
            if (!rowOf.TryGetValue(target, out int i)) { set.Add(rows[0].Row.Address); return; }
            for (int k = i; k >= 0; k = rows[k].Parent) set.Add(rows[k].Row.Address);
        }

        void Flatten()
        {
            visible.Clear();
            bool filter = !query.IsEmpty || view.CorrectedOnly;
            if (filter)
            {
                var keep = new bool[rows.Count];
                int matches = 0;
                for (int i = 0; i < rows.Count; i++)
                {
                    if (!query.Matches(rows[i].Key)) continue;
                    if (view.CorrectedOnly && !corrected.Contains(rows[i].Row.Address)) continue;
                    keep[i] = true;
                    matches++;
                }
                for (int i = rows.Count - 1; i >= 0; i--)
                    if (keep[i] && rows[i].Parent >= 0) keep[rows[i].Parent] = true;
                for (int i = 0; i < rows.Count; i++) if (keep[i]) visible.Add(i);
                Matches = matches;
            }
            else
            {
                var open = ExpandedSet;
                int closedDepth = int.MaxValue;
                for (int i = 0; i < rows.Count; i++)
                {
                    int depth = rows[i].Row.Depth;
                    if (depth > closedDepth) continue;
                    closedDepth = int.MaxValue;
                    visible.Add(i);
                    if (rows[i].HasChildren && !open.Contains(rows[i].Row.Address)) closedDepth = depth;
                }
                Matches = rows.Count;
            }
            picksDirty = true;
            if (tree == null) return;
            tree.RefreshItems();
            SyncTreeSelection();
        }

        /// <summary>Searches the part's bones by name.</summary>
        public void SetQuery(string q)
        {
            view.Query = q ?? "";
            query = SearchQuery.Parse(view.Query);
            Flatten();
        }

        /// <summary>Lists only corrected bones, with their ancestors.</summary>
        public void SetCorrectedOnly(bool on)
        {
            view.CorrectedOnly = on;
            correctedToggle?.SetValueWithoutNotify(on);
            Flatten();
        }

        /// <summary>Opens or closes a row of the part.</summary>
        public void SetExpanded(string address, bool open)
        {
            var set = ExpandedSet;
            if (open ? set.Add(address) : set.Remove(address)) Flatten();
        }

        public bool IsExpanded(string address) => ExpandedSet.Contains(address);

        // ------------------------------------------------------------ the rows of the tree

        VisualElement MakeTreeLine()
        {
            var line = new VisualElement();
            line.AddToClassList(InspectorUi.RowClass);
            line.style.flexDirection = FlexDirection.Row;
            line.style.alignItems = Align.Center;
            var refs = new TreeLine { Arrow = new Label { name = "adv-row-arrow" }, Name = new Label { name = "adv-row-text" } };
            refs.Arrow.style.width = 14;
            refs.Arrow.style.flexShrink = 0;
            refs.Arrow.RegisterCallback<PointerDownEvent>(e =>
            {
                // only an arrow that shows takes the press: on a leaf, or under a search, the row selects
                if (e.button != 0 || refs.Arrow.text.Length == 0 || refs.Index < 0 || refs.Index >= rows.Count) return;
                e.StopPropagation();
                string a = rows[refs.Index].Row.Address;
                SetExpanded(a, !IsExpanded(a));
            });
            refs.Name.AddToClassList("hs-row__label");
            refs.Name.style.flexGrow = 1;
            refs.Name.style.flexShrink = 1;
            refs.Name.style.overflow = Overflow.Hidden;
            refs.Name.style.textOverflow = TextOverflow.Ellipsis;
            refs.FkMark = Loc.Label("inspector.chara.adv.fkMark");
            refs.FkMark.name = "adv-row-fk";
            refs.FkMark.AddToClassList("hs-row__badge");
            Loc.Tooltip(refs.FkMark, "inspector.chara.adv.fkTip");
            refs.IkMark = Loc.Label("inspector.chara.fk.ikMark");
            refs.IkMark.name = "adv-row-ik";
            refs.IkMark.AddToClassList("hs-row__badge");
            Loc.Tooltip(refs.IkMark, "inspector.chara.adv.ikTip");
            refs.Dot = new Label("●") { name = "adv-row-corrected" };
            refs.Dot.AddToClassList("hs-row__badge");
            line.Add(refs.Arrow);
            line.Add(refs.Name);
            line.Add(refs.FkMark);
            line.Add(refs.IkMark);
            line.Add(refs.Dot);
            line.userData = refs;
            return line;
        }

        void BindTreeLine(VisualElement line, int i)
        {
            var refs = (TreeLine)line.userData;
            if (i < 0 || i >= visible.Count) return;
            int index = visible[i];
            var row = rows[index];
            refs.Index = index;
            string address = row.Row.Address;
            line.name = "adv-row-" + address;
            line.style.paddingLeft = row.Row.Depth * TreeIndent;
            bool filtering = !query.IsEmpty || view.CorrectedOnly;
            refs.Arrow.text = !row.HasChildren ? "" : filtering ? "" : IsExpanded(address) ? "▾" : "▸";
            refs.Name.text = row.Row.Bone ? row.Row.Bone.name : address;
            refs.Name.EnableInClassList(InspectorUi.Dim, row.Protected);
            line.tooltip = row.Protected ? L10n.Get("inspector.chara.adv.protectedTip") : "";
            refs.FkMark.style.display = row.Fk ? DisplayStyle.Flex : DisplayStyle.None;
            refs.IkMark.style.display = row.Row.Bone && ikOwned.Contains(row.Row.Bone) ? DisplayStyle.Flex : DisplayStyle.None;
            bool on = corrected.Contains(address);
            refs.Dot.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            bool isHidden = hidden.TryGetValue(address, out string why);
            refs.Dot.EnableInClassList("hs-row__badge--warning", isHidden);
            refs.Name.EnableInClassList("hs-text--warning", isHidden);
            refs.Dot.tooltip = isHidden ? L10n.Format("inspector.chara.adv.notShownTip", why) : "";
            refs.Name.tooltip = isHidden ? refs.Dot.tooltip : "";
        }

        void TreeSelectionChanged()
        {
            int v = tree.selectedIndex;
            if (v < 0 || v >= visible.Count) return;
            var row = rows[visible[v]];
            if (row.Protected) { SyncTreeSelection(); return; }
            ctx.SubSelection = Primary + "/" + StudioOps.BoneSubKind + "/" + row.Row.Address;
        }

        void SyncTreeSelection()
        {
            if (tree == null) return;
            int v = SelectedBone != null && rowOf.TryGetValue(SelectedBone, out int i) ? visible.IndexOf(i) : -1;
            if (v >= 0) { if (tree.selectedIndex != v) tree.SetSelectionWithoutNotify(new[] { v }); }
            else if (tree.selectedIndex >= 0) tree.SetSelectionWithoutNotify(Array.Empty<int>());
        }

        // ------------------------------------------------------------ the selected bone

        void SelectBone(string address)
        {
            bool changed = !string.Equals(address, SelectedBone, StringComparison.Ordinal);
            SelectedBone = address;
            if (address != null && changed) Reveal(address);
            ShowSelected();
            picksDirty = true;
        }

        // the row of a bone selected elsewhere (a pick in the view, a command): its part, its ancestors open, in sight
        void Reveal(string address)
        {
            if (tree == null) return;
            var r = Real;
            if (r == null) return;
            string part = PartOf(r, address);
            if (part != null && part != view.Part) SetPart(part);
            if (!rowOf.TryGetValue(address, out int i)) return;
            var open = ExpandedSet;
            bool opened = false;
            for (int p = rows[i].Parent; p >= 0; p = rows[p].Parent) opened |= open.Add(rows[p].Row.Address);
            if (opened) Flatten();
            SyncTreeSelection();
            int v = visible.IndexOf(i);
            if (v >= 0) tree.schedule.Execute(() => { if (v < visible.Count) tree.ScrollToItem(v); }).ExecuteLater(30);
        }

        static string PartOf(CharaRealization r, string address)
        {
            if (!PoseAddress.TryParse(PoseAddressForm.Bone, address, out var a, out _)) return null;
            if (a.Part != PosePart.Named) return a.PartText;
            var t = AdvancedPoseResolver.Resolve(r, a);
            var head = r.Context?.HeadBone;
            return t && head && t.IsChildOf(head.transform) ? AdvancedPoseResolver.HeadPart : AdvancedPoseResolver.BodyPart;
        }

        /// <summary>True when the selected bone lies on a part that keeps the mosaic in place: its rows are off.</summary>
        public bool SelectedGuarded { get; private set; }

        internal BoneCorrection SelectedValue
        {
            get
            {
                var block = Block;
                return SelectedBone != null && block != null && block.Bones.TryGetValue(SelectedBone, out var c) ? c : null;
            }
        }

        void ShowSelected()
        {
            if (editor == null) return;
            bool any = SelectedBone != null;
            editor.style.display = any ? DisplayStyle.Flex : DisplayStyle.None;
            selectNote.style.display = any ? DisplayStyle.None : DisplayStyle.Flex;
            if (!any) return;
            var r = Real;
            var t = r != null ? AdvancedPoseResolver.Resolve(r, SelectedBone) : null;
            boneName.text = t ? t.name : SelectedBone;
            boneName.tooltip = t && r != null ? PathOf(t, r.Root.transform) : SelectedBone;
            // a row cannot select such a bone; a command can
            SelectedGuarded = t && r != null && AdvancedPoseGuard.Bone(r, t);
            if (SelectedGuarded) boneName.tooltip = L10n.Get("inspector.chara.adv.protectedTip");
            posRow.SetEnabled(!SelectedGuarded);
            rotRow.SetEnabled(!SelectedGuarded);
            scaleRow.SetEnabled(!SelectedGuarded);
            var v = SelectedValue;
            posRow.Show(v?.Pos ?? Vec3.Zero);
            rotRow.Show(v?.Rot ?? Vec3.Zero);
            scaleRow.Show(v?.Scale ?? Vec3.One);
            boneReset.SetEnabled(v != null);
        }

        static string PathOf(Transform t, Transform root)
        {
            var sb = new StringBuilder(t.name);
            for (var p = t.parent; p && p != root; p = p.parent) sb.Insert(0, p.name + "/");
            return sb.ToString();
        }

        void ResetSelected()
        {
            if (SelectedBone != null) ctx.ShowResult(ctx.Ops.ClearBoneCorrections(Primary, new[] { SelectedBone }, UiContext.User(OpLabels.ClearBoneCorrections)));
        }

        void ResetAll() => ctx.ShowResult(ctx.Ops.ClearBoneCorrections(Primary, null, UiContext.User(OpLabels.ClearBoneCorrections)));

        void RemoveStaleBone(string key) => ctx.ShowResult(ctx.Ops.ClearBoneCorrections(Primary, new[] { key }, UiContext.User(OpLabels.ClearBoneCorrections)));

        // ------------------------------------------------------------ marks

        void BonesBlockChanged(CharaRealization r)
        {
            if (tree == null) return;
            var block = Block;
            bool same = block != null ? corrected.SetEquals(block.Bones.Keys) : corrected.Count == 0;
            if (!same)
            {
                corrected.Clear();
                if (block != null) corrected.UnionWith(block.Bones.Keys);
                picksDirty = true;
                if (view.CorrectedOnly) Flatten();
                else tree.RefreshItems();
            }
            ShowSelected();
            boneStale.Set(AdvancedPoseDriver.Of(r)?.StaleBones);
            resetAll.SetEnabled(block != null && block.Bones.Count > 0);
        }

        void BonesRefresh(CharaRealization r)
        {
            if (tree == null) return;
            BonesBlockChanged(r);
            bool changed = false;
            // the bones the solve owns (known one solve after IK turns on)
            var owned = r != null && (r.Node?.State as CharaNodeState)?.Ik.Enabled == true ? IkDriver.Of(r)?.Owned : null;
            int count = owned?.Count ?? 0;
            if (count != ikOwned.Count)
            {
                ikOwned.Clear();
                if (owned != null) foreach (var t in owned) ikOwned.Add(t);
                changed = true;
            }
            // where another stage hides a correction now
            var entries = AdvancedPoseDriver.Of(r)?.Set.Entries;
            int stillHidden = 0;
            if (entries != null)
                foreach (var e in entries)
                {
                    if (BoneCorrections.Shown(e)) { changed |= hidden.Remove(e.Address); continue; }
                    stillHidden++;
                    if (hidden.ContainsKey(e.Address)) continue;
                    hidden[e.Address] = WhyHidden(r, e);
                    changed = true;
                }
            if (stillHidden != hidden.Count)
            {
                hidden.Clear();
                if (entries != null)
                    foreach (var e in entries)
                        if (!BoneCorrections.Shown(e)) hidden[e.Address] = WhyHidden(r, e);
                changed = true;
            }
            if (changed) tree.RefreshItems();
        }

        static string WhyHidden(CharaRealization r, CorrectedBone e)
        {
            string key = "other";
            if (e.EyeStage != null) key = "eyeLook";
            else if (e.WidthStage != null) key = "mouthWidth";
            else if (e.ChainBone || OnBust(r, e.Bone)) key = "physics";
            else if (Driven(r, e.Bone)) key = "jointCorrection";
            return L10n.Get("inspector.chara.adv.why." + key);
        }

        static bool OnBust(CharaRealization r, Transform t)
        {
            var stage = r.Motion ? r.Motion.Get<BustStage>() : null;
            if (stage == null) return false;
            foreach (var c in stage.Chains)
            {
                if (c == null) continue;
                for (int i = 0; i < c.ParticleCount; i++)
                    if (c.ParticleTransform(i) == t) return true;
            }
            return false;
        }

        static bool Driven(CharaRealization r, Transform t)
        {
            var stage = r.Motion ? r.Motion.Get<JointCorrectionStage>() : null;
            if (stage == null) return false;
            for (int i = 0; i < stage.DrivenCount; i++)
                if (stage.DrivenOf(i) == t) return true;
            return false;
        }

        void BonesEndEdits()
        {
            posRow?.EndEdit();
            rotRow?.EndEdit();
            scaleRow?.EndEdit();
        }

        // ------------------------------------------------------------ the pick

        /// <summary>Turns the pick on or off; it never stays on past the tab.</summary>
        public void SetPick(bool on)
        {
            PickOn = on;
            pickToggle?.SetValueWithoutNotify(on);
            picksDirty = true;
        }

        /// <summary>The spheres of the pick: every corrected bone of the character, then the rows the tree lists, at most <see cref="MaxPicks"/>.
        /// A part that keeps the mosaic in place has none.</summary>
        public IReadOnlyList<BonePick> PickRows
        {
            get
            {
                if (!picksDirty) return picks;
                picksDirty = false;
                picks.Clear();
                var r = Real;
                if (r == null) return picks;
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var entries = AdvancedPoseDriver.Of(r)?.Set.Entries;
                if (entries != null)
                    foreach (var e in entries)
                    {
                        if (picks.Count >= MaxPicks) break;
                        if (e.Bone && seen.Add(e.Address)) picks.Add(new BonePick(e.Address, e.Bone, true));
                    }
                foreach (int i in visible)
                {
                    if (picks.Count >= MaxPicks) break;
                    var row = rows[i];
                    if (row.Protected || !row.Row.Bone || !seen.Add(row.Row.Address)) continue;
                    picks.Add(new BonePick(row.Row.Address, row.Row.Bone, corrected.Contains(row.Row.Address)));
                }
                return picks;
            }
        }
    }

    /// <summary>One channel of the selected bone's correction: a vector field whose label drag is one history entry; typed values chain.</summary>
    public sealed class BoneChannelRow : VisualElement
    {
        readonly AdvancedPoseTab tab;
        readonly UiField spec;
        readonly int channel;
        UiEdit edit;
        bool dragging;

        public readonly VectorField Field;
        public OpResult LastResult { get; private set; }
        /// <summary>The override channel of the selected bone, or null.</summary>
        public string Channel => tab.SelectedBone != null ? HS2Next.Scenes.Overrides.OverridePath.BoneText(tab.SelectedBone, channel == 0 ? "pos" : channel == 1 ? "rot" : "scale") : null;
        public string Character => tab.Primary;

        internal BoneChannelRow(AdvancedPoseTab tab, UiField spec, int channel)
        {
            this.tab = tab;
            this.spec = spec;
            this.channel = channel;
            name = "field-" + spec.Path;
            AddToClassList("hs-field");
            Field = new VectorField(L10n.Get(spec.LabelKey), 3) { Format = spec.Format ?? "0.#####" };
            Loc.Bind(Field.LabelElement, spec.LabelKey);
            Field.Edited += OnEdited;
            Add(Field);
        }

        internal void Show(Vec3 v)
        {
            if (!dragging) Field.SetDisplay(new Vector4(v.X, v.Y, v.Z, 0), null);
        }

        internal void EndEdit()
        {
            dragging = false;
            var e = edit;
            edit = null;
            e?.Cancel();
        }

        void OnEdited(EditPhase phase, Vector4 v)
        {
            var ctx = tab.Ctx;
            if (phase == EditPhase.Begin)
            {
                edit?.Cancel();
                edit = ctx.BeginEdit(OpLabels.BoneCorrection);
                dragging = true;
                return;
            }
            if (phase == EditPhase.End)
            {
                dragging = false;
                var e = edit;
                edit = null;
                if (e != null) LastResult = ctx.ShowResult(e.Commit());
                tab.BlockChanged();
                return;
            }
            if (edit != null && !edit.IsOpen) return;   // Esc took the drag back: its last moves write nothing
            Write(v, phase == EditPhase.Preview);
        }

        /// <summary>Writes the channel of the selected bone: one step of an open drag, or one entry whose typed successors chain.</summary>
        public OpResult Write(Vector4 v, bool step = false)
        {
            string address = tab.SelectedBone;
            if (address == null) return OpResult.NothingToDo();
            var ctx = tab.Ctx;
            var value = new Vec3((float)spec.ClampTyped(v.x), (float)spec.ClampTyped(v.y), (float)spec.ClampTyped(v.z));
            Vec3? pos = channel == 0 ? value : (Vec3?)null, rot = channel == 1 ? value : (Vec3?)null, scale = channel == 2 ? value : (Vec3?)null;
            var block = tab.Block;
            if (step && !tab.SelectedGuarded && block != null && (block.Bones.ContainsKey(address) || block.Bones.Count < AdvancedPoseLimits.MaxBones))
                return LastResult = tab.DragWrite(AdvancedPoseEdits.SetBone(block, address, pos, rot, scale), OpLabels.BoneCorrection);
            string mergeKey = "ui.typed/" + tab.Primary + "/" + address + "/" + channel;
            LastResult = ctx.ShowResult(ctx.Ops.SetBoneCorrection(tab.Primary, address, pos, rot, scale, false, UiContext.User(OpLabels.BoneCorrection, mergeKey)));
            tab.BlockChanged();
            return LastResult;
        }
    }
}
