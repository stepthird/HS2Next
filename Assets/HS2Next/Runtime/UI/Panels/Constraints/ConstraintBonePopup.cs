// The popup of a constraint's bone field, opened through the popup menus, so it closes as a menu does: Esc, a click outside, another menu. A
// character lists the tree of one part at a time, as the Advanced pose tab; an item lists the names of its transforms. The list is
// virtualised; a search shows the matches with their ancestors. A target's list greys what the mosaic rule names, and a click on such a row
// does nothing; a source may read any bone. «Pick in view» lets the pointer through to the viewport and shows the spheres of the rows
// the list shows: a click on one chooses it. The pick ends with the popup. Element names: constraints-bone-popup, constraints-bone-part,
// constraints-bone-search, constraints-bone-pick, constraints-bone-list, constraints-bone-row-<address>, constraints-bone-none.
using System;
using System.Collections.Generic;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Core;
using HS2Next.UI.Input;
using HS2Next.UI.Widgets;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    /// <summary>The spheres of a constraint's bone pick, on a node that need not be selected; off while <see cref="Node"/> is null.</summary>
    public static class ConstraintBonePick
    {
        static readonly List<BonePick> rows = new List<BonePick>();
        static Action<string> take;

        /// <summary>The node whose bones are offered, or null.</summary>
        public static string Node { get; private set; }
        public static bool On => Node != null;
        public static IReadOnlyList<BonePick> Rows => rows;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Stop();

        internal static void Start(string node, Action<string> picked)
        {
            Node = node;
            take = picked;
        }

        internal static void Stop()
        {
            Node = null;
            take = null;
            rows.Clear();
        }

        internal static void Set(List<BonePick> shown)
        {
            rows.Clear();
            rows.AddRange(shown);
        }

        /// <summary>A click on the sphere with this sub-object address: true when the pick took it.</summary>
        public static bool Take(string sub)
        {
            if (!On || sub == null) return false;
            string prefix = Node + "/" + StudioOps.BoneSubKind + "/";
            if (!sub.StartsWith(prefix, StringComparison.Ordinal)) return false;
            var t = take;
            string address = sub.Substring(prefix.Length);
            Stop();
            t?.Invoke(address);
            return true;
        }
    }

    public sealed class ConstraintBonePopup : VisualElement
    {
        public const string BoxName = "constraints-bone-popup";
        const float RowHeight = 22f, Indent = 8f, Width = 320f, Height = 340f;

        /// <summary>One bone of the list.</summary>
        public sealed class Row
        {
            public string Address, Name, Key;
            public Transform Bone;
            public int Depth, Parent = -1;
            public bool HasChildren, Guarded;
        }

        sealed class LineRefs
        {
            public int Index = -1;
            public Label Arrow, Text;
        }

        /// <summary>The popup open now, or null.</summary>
        public static ConstraintBonePopup Current { get; private set; }

        readonly UiContext ctx;
        readonly string node;
        readonly bool target;
        readonly Action<string> picked;
        readonly List<Row> rows = new List<Row>();
        readonly Dictionary<string, int> rowOf = new Dictionary<string, int>(StringComparer.Ordinal);
        readonly List<int> visible = new List<int>();
        readonly List<string> partKeys = new List<string>();
        readonly Dictionary<string, HashSet<string>> expanded = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        readonly DropdownField partField;
        readonly TextField searchField;
        readonly Toggle pickToggle;
        readonly ListView list;
        readonly Label none;
        SearchQuery query = SearchQuery.Empty;
        string part = AdvancedPoseResolver.BodyPart;

        public IReadOnlyList<Row> AllRows => rows;
        /// <summary>The rows the list shows now, as indices into <see cref="AllRows"/>.</summary>
        public IReadOnlyList<int> VisibleRows => visible;
        public string Part => part;
        public IReadOnlyList<string> PartChoices => partKeys;
        public ListView List => list;
        public Toggle PickToggle => pickToggle;

        /// <summary>Opens the popup of <paramref name="anchor"/> for the bones of a node. <paramref name="target"/>: the guarded rows are greyed.</summary>
        public static ConstraintBonePopup Open(UiContext ctx, VisualElement anchor, string node, bool target, Action<string> picked)
        {
            if (ctx.Menus == null) return null;
            var box = ctx.Menus.Show(Array.Empty<MenuItem>(), anchor, null, BoxName);
            var p = new ConstraintBonePopup(ctx, node, target, picked);
            box.Add(p);
            ctx.Menus.Closed += p.OnMenusClosed;
            Current = p;
            UiDefer.Frames(2, p.FocusSearch);
            return p;
        }

        ConstraintBonePopup(UiContext ctx, string node, bool target, Action<string> picked)
        {
            this.ctx = ctx;
            this.node = node;
            this.target = target;
            this.picked = picked;
            name = "constraints-bone-content";
            ConstraintsUiModule.Style(this);
            AddToClassList("hs-constraints__popup");
            style.width = Width;
            style.height = Height;
            partField = new DropdownField(new List<string>(), 0) { name = "constraints-bone-part" };
            Loc.Bind(partField, "inspector.chara.adv.part", (e, s) => ((DropdownField)e).label = s);
            partField.RegisterValueChangedCallback(OnPartPicked);
            Add(partField);
            searchField = PanelKit.SearchField("constraints-bone-search", SetQuery);
            searchField.AddToClassList(ShortcutRouter.OwnEscapeClass);
            Loc.Bind(searchField, "inspector.chara.adv.search", (e, t) => ((TextField)e).textEdition.placeholder = t);
            Add(searchField);
            pickToggle = new Toggle { name = "constraints-bone-pick" };
            pickToggle.AddToClassList("hs-field__toggle");
            Loc.Bind(pickToggle, "constraints.pickInView", (e, s) => ((Toggle)e).label = s);
            pickToggle.RegisterValueChangedCallback(OnPickToggled);
            Add(pickToggle);
            list = new ListView
            {
                name = "constraints-bone-list", fixedItemHeight = RowHeight, virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                selectionType = SelectionType.None, itemsSource = visible, makeItem = MakeLine, bindItem = BindLine,
            };
            list.style.flexGrow = 1;
            Add(list);
            none = InspectorUi.Note("inspector.notBuilt");
            none.name = "constraints-bone-none";
            Add(none);
            // the search field keeps Esc from the router: the text field must not eat it, so the popup takes it on the way down
            RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            ReadNode();
        }

        void OnKeyDown(KeyDownEvent e)
        {
            if (e.keyCode != KeyCode.Escape || e.modifiers != EventModifiers.None) return;
            e.StopPropagation();
            panel?.focusController?.IgnoreEvent(e);
            Close();
        }

        void FocusSearch()
        {
            if (searchField.panel != null) searchField.Focus();
        }

        void OnMenusClosed()
        {
            ctx.Menus.Closed -= OnMenusClosed;
            ConstraintBonePick.Stop();
            if (Current == this) Current = null;
        }

        /// <summary>Closes the popup.</summary>
        public void Close() => ctx.Menus?.CloseAll();

        // ------------------------------------------------------------ the rows

        CharaRealization Chara
        {
            get
            {
                var r = ctx.Runtime?.FindChara(node);
                return r != null && r.State == RealizationState.Built && !r.InGraveyard && r.Root ? r : null;
            }
        }

        ItemRealization Item
        {
            get
            {
                var r = ctx.Runtime?.FindItem(node);
                return r != null && r.State == RealizationState.Built && r.Instance ? r : null;
            }
        }

        void ReadNode()
        {
            var r = Chara;
            partField.style.display = r != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (r != null) ReadParts(r);
            ReadRows();
        }

        void ReadParts(CharaRealization r)
        {
            partKeys.Clear();
            partKeys.Add(AdvancedPoseResolver.BodyPart);
            partKeys.Add(AdvancedPoseResolver.HeadPart);
            var cc = r.Context;
            for (int k = 0; k < cc.Hair.Length; k++)
                if (cc.Hair[k]) partKeys.Add(HS2Next.Save.PoseAddressGrammar.PartText(HS2Next.Save.PosePart.Hair, k, null));
            for (int k = 0; k < cc.Clothes.Length; k++)
                if (cc.Clothes[k]) partKeys.Add(HS2Next.Save.PoseAddressGrammar.PartText(HS2Next.Save.PosePart.Clothes, k, null));
            if (cc.Accessories != null)
                foreach (var s in cc.Accessories)
                {
                    if (s == null || !s.Object) continue;
                    string acc = r.AccessoryIdOf(s);
                    if (acc != null) partKeys.Add(HS2Next.Save.PoseAddressGrammar.PartText(HS2Next.Save.PosePart.Accessory, -1, acc));
                }
            var labels = new List<string>();
            foreach (var p in partKeys) labels.Add(AdvancedPoseTab.PartLabel(r, p));
            partField.choices = labels;
            partField.SetValueWithoutNotify(labels[0]);
        }

        void OnPartPicked(ChangeEvent<string> e)
        {
            if (e.target != partField) return;
            int i = partField.index;
            if (i >= 0 && i < partKeys.Count) SetPart(partKeys[i]);
        }

        /// <summary>Shows another part of the character.</summary>
        public void SetPart(string key)
        {
            part = key;
            int i = partKeys.IndexOf(key);
            if (i >= 0 && i < partField.choices.Count) partField.SetValueWithoutNotify(partField.choices[i]);
            ReadRows();
        }

        void ReadRows()
        {
            rows.Clear();
            rowOf.Clear();
            var r = Chara;
            var it = r == null ? Item : null;
            if (r != null)
                foreach (var b in AdvancedPoseResolver.Bones(r, part))
                {
                    var row = new Row { Address = b.Address, Name = b.Bone.name, Key = SearchText.Normalize(b.Bone.name), Bone = b.Bone, Depth = b.Depth, Guarded = target && AdvancedPoseGuard.Bone(r, b.Bone) };
                    if (b.Parent != null && rowOf.TryGetValue(b.Parent, out int p))
                    {
                        row.Parent = p;
                        rows[p].HasChildren = true;
                    }
                    rowOf[b.Address] = rows.Count;
                    rows.Add(row);
                }
            else if (it != null) ItemRows(it.Instance.transform, 0, -1, new Dictionary<string, int>(StringComparer.Ordinal));
            none.style.display = r == null && it == null ? DisplayStyle.Flex : DisplayStyle.None;
            DefaultExpanded();
            Flatten();
        }

        // depth first from the instance, never into another node's holder; the n-th transform of a name is name#n
        void ItemRows(Transform t, int depth, int parent, Dictionary<string, int> seen)
        {
            seen.TryGetValue(t.name, out int n);
            seen[t.name] = n + 1;
            string address = n == 0 ? t.name : t.name + "#" + n;
            int me = rows.Count;
            rows.Add(new Row { Address = address, Name = t.name, Key = SearchText.Normalize(t.name), Bone = t, Depth = depth, Parent = parent });
            rowOf[address] = me;
            if (parent >= 0) rows[parent].HasChildren = true;
            for (int i = 0; i < t.childCount; i++)
            {
                var c = t.GetChild(i);
                if (!SceneNodeHolder.IsHolder(c)) ItemRows(c, depth + 1, me, seen);
            }
        }

        HashSet<string> Open_
        {
            get
            {
                if (!expanded.TryGetValue(part, out var set)) expanded[part] = set = new HashSet<string>(StringComparer.Ordinal);
                return set;
            }
        }

        // the first time a part is listed: the way to the hips or the face, else the top
        void DefaultExpanded()
        {
            if (rows.Count == 0 || expanded.ContainsKey(part)) return;
            var set = Open_;
            string way = Chara == null ? null : part == AdvancedPoseResolver.BodyPart ? "cf_J_Hips" : part == AdvancedPoseResolver.HeadPart ? "cf_J_FaceRoot" : null;
            if (way == null || !rowOf.TryGetValue(way, out int i)) { set.Add(rows[0].Address); return; }
            for (int k = i; k >= 0; k = rows[k].Parent) set.Add(rows[k].Address);
        }

        void Flatten()
        {
            visible.Clear();
            if (!query.IsEmpty)
            {
                var keep = new bool[rows.Count];
                for (int i = 0; i < rows.Count; i++) keep[i] = query.Matches(rows[i].Key);
                for (int i = rows.Count - 1; i >= 0; i--)
                    if (keep[i] && rows[i].Parent >= 0) keep[rows[i].Parent] = true;
                for (int i = 0; i < rows.Count; i++) if (keep[i]) visible.Add(i);
            }
            else
            {
                var open = Open_;
                int closedDepth = int.MaxValue;
                for (int i = 0; i < rows.Count; i++)
                {
                    int depth = rows[i].Depth;
                    if (depth > closedDepth) continue;
                    closedDepth = int.MaxValue;
                    visible.Add(i);
                    if (rows[i].HasChildren && !open.Contains(rows[i].Address)) closedDepth = depth;
                }
            }
            list.RefreshItems();
            UpdatePicks();
        }

        /// <summary>Searches the rows by name.</summary>
        public void SetQuery(string q)
        {
            query = SearchQuery.Parse(q ?? "");
            Flatten();
        }

        /// <summary>Opens or closes a row.</summary>
        public void SetExpanded(string address, bool open)
        {
            var set = Open_;
            if (open ? set.Add(address) : set.Remove(address)) Flatten();
        }

        // ------------------------------------------------------------ the lines

        VisualElement MakeLine()
        {
            var line = new VisualElement();
            line.AddToClassList(InspectorUi.RowClass);
            line.style.flexDirection = FlexDirection.Row;
            line.style.alignItems = Align.Center;
            var refs = new LineRefs { Arrow = new Label { name = "constraints-bone-arrow" }, Text = new Label { name = "constraints-bone-text" } };
            refs.Arrow.style.width = 14;
            refs.Arrow.style.flexShrink = 0;
            refs.Text.AddToClassList("hs-row__label");
            refs.Text.style.flexGrow = 1;
            refs.Text.style.flexShrink = 1;
            line.Add(refs.Arrow);
            line.Add(refs.Text);
            line.userData = refs;
            line.RegisterCallback<PointerDownEvent>(OnLineDown);
            return line;
        }

        void OnLineDown(PointerDownEvent e)
        {
            if (e.button != 0 || !(e.currentTarget is VisualElement line) || !(line.userData is LineRefs refs) || refs.Index < 0 || refs.Index >= rows.Count) return;
            e.StopPropagation();
            var row = rows[refs.Index];
            bool onArrow = e.target == refs.Arrow && refs.Arrow.text.Length > 0;
            if (onArrow) { SetExpanded(row.Address, !Open_.Contains(row.Address)); return; }
            Choose(row);
        }

        void BindLine(VisualElement line, int i)
        {
            var refs = (LineRefs)line.userData;
            if (i < 0 || i >= visible.Count) return;
            int index = visible[i];
            var row = rows[index];
            refs.Index = index;
            line.name = "constraints-bone-row-" + row.Address;
            line.style.paddingLeft = row.Depth * Indent;
            refs.Arrow.text = !row.HasChildren || !query.IsEmpty ? "" : Open_.Contains(row.Address) ? "▾" : "▸";
            refs.Text.text = row.Name;
            refs.Text.EnableInClassList(InspectorUi.Dim, row.Guarded);
            line.tooltip = row.Guarded ? L10n.Get("constraints.state.protected") : "";
        }

        /// <summary>A click on a row: the bone is chosen and the popup closes; a greyed row does nothing. True when it was chosen.</summary>
        public bool Choose(Row row)
        {
            if (row == null || row.Guarded) return false;
            Close();
            picked?.Invoke(row.Address);
            return true;
        }

        // ------------------------------------------------------------ the pick

        void OnPickToggled(ChangeEvent<bool> e)
        {
            if (e.target == pickToggle) SetPick(e.newValue);
        }

        /// <summary>Turns the pick in the view on or off.</summary>
        public void SetPick(bool on)
        {
            pickToggle.SetValueWithoutNotify(on);
            ctx.Menus?.SetBlocking(!on);
            if (on) ConstraintBonePick.Start(node, TakePick);
            else ConstraintBonePick.Stop();
            UpdatePicks();
        }

        void TakePick(string address)
        {
            Close();
            picked?.Invoke(address);
        }

        void UpdatePicks()
        {
            if (!ConstraintBonePick.On) return;
            var l = new List<BonePick>();
            foreach (int i in visible)
            {
                if (l.Count >= AdvancedPoseTab.MaxPicks) break;
                var row = rows[i];
                if (!row.Guarded && row.Bone) l.Add(new BonePick(row.Address, row.Bone, false));
            }
            ConstraintBonePick.Set(l);
        }
    }
}
