// The Advanced pose tab of the character inspector: scene values of the primary selected character, in three sections. A section is
// built when it is first open, and the tab reads nothing of the character while it is closed: its bindings and its refresh (at most
// twice a second) live only while the tab is shown.
//   Bones          a virtualised tree of the bones of one part (search, corrected only, a pick in the view), the selected bone's three
//                  rows, the entries this character lacks with their Remove (AdvancedPoseTab.Bones.cs); the Move, Rotate and Scale tools
//                  work on the selected bone (BoneHandles.cs)
//   Jiggle         the swing chains, the values of the selected one, the two breast and two hip chains (AdvancedPoseTab.Jiggle.cs)
//   Blend shapes   one renderer's shapes as sliders, the current weight shown (AdvancedPoseTab.Blend.cs)
// A typed value, a toggle or a reset goes through StudioOps, one history entry; a drag is one UI edit whose steps are the block's own
// field writes, so its values collapse to the first old and the last new.
using System;
using System.Collections.Generic;
using System.Text;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Binding;
using HS2Next.UI.Core;
using UnityEngine.UIElements;
using UiField = HS2Next.UI.Core.FieldSpec;

namespace HS2Next.UI.Panels
{
    public sealed partial class AdvancedPoseTab : VisualElement
    {
        public const string TabId = InspectorSchemas.AdvancedPoseTabId;
        /// <summary>The refresh of weights and marks while the tab is shown.</summary>
        public const int RefreshMs = 500;

        /// <summary>The tab shown now, or null (the gizmo's pick asks it).</summary>
        public static AdvancedPoseTab Current { get; private set; }

        /// <summary>What the tab keeps across rebuilds of the inspector while the same character stays selected.</summary>
        sealed class TabView
        {
            public string Node;
            public string Part = AdvancedPoseResolver.BodyPart;
            public string Query = "";
            public bool CorrectedOnly;
            public readonly Dictionary<string, HashSet<string>> Expanded = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            public string Chain;
            public string Renderer;
            public string BlendQuery = "";
            public bool BonesOpen = true, JiggleOpen, BlendOpen;
        }

        static TabView view;

        readonly UiContext ctx;
        bool built;
        string structure;
        Label primaryOnly, notBuilt;
        Foldout bonesFold, jiggleFold, blendFold;

        public string Primary { get; }
        /// <summary>Refreshes so far (tests: none while the tab is closed).</summary>
        public int Refreshes { get; private set; }

        public AdvancedPoseTab(UiContext ctx, string primary)
        {
            this.ctx = ctx;
            Primary = primary;
            name = "adv-tab";
            if (view == null || view.Node != primary) view = new TabView { Node = primary };
            RegisterCallback<AttachToPanelEvent>(_ => Opened());
            RegisterCallback<DetachFromPanelEvent>(_ => Closed());
        }

        void Opened()
        {
            Current = this;
            if (!built) Build();
            ctx.SubSelectionChanged += OnSub;
            Refresh();
            OnSub(ctx.SubSelection);
        }

        void Closed()
        {
            if (Current == this) Current = null;
            ctx.SubSelectionChanged -= OnSub;
            SetPick(false);
            EndEdits();
        }

        void Build()
        {
            built = true;
            primaryOnly = InspectorUi.Note("inspector.chara.adv.primaryOnly");
            primaryOnly.name = "adv-primary-only";
            Add(primaryOnly);
            notBuilt = InspectorUi.Note("inspector.notBuilt");
            notBuilt.name = "adv-not-built";
            Add(notBuilt);
            bonesFold = Section("inspector.chara.adv.bones", view.BonesOpen, "adv-section-bones", BuildBones, v => view.BonesOpen = v);
            jiggleFold = Section("inspector.chara.adv.jiggle", view.JiggleOpen, "adv-section-jiggle", BuildJiggle, v => view.JiggleOpen = v);
            blendFold = Section("inspector.chara.adv.blend", view.BlendOpen, "adv-section-blend", BuildBlend, v => view.BlendOpen = v);
            schedule.Execute(Refresh).Every(RefreshMs);
            ctx.Bindings.Bind(this, TargetSelector.Node(Primary), AdvancedPosePaths.Root, (el, v) => BlockChanged());
        }

        Foldout Section(string key, bool open, string id, Action<VisualElement> build, Action<bool> remember)
        {
            var fo = InspectorUi.Section(key, open, id);
            bool done = false;
            void Ensure()
            {
                if (done || !fo.value) return;
                done = true;
                build(fo);
                structure = null;
            }
            // a change of a toggle inside the section bubbles up to here as well
            fo.RegisterValueChangedCallback(e =>
            {
                if (e.target != fo) return;
                remember(e.newValue);
                Ensure();
                if (panel != null) Refresh();
            });
            Add(fo);
            Ensure();
            return fo;
        }

        // ------------------------------------------------------------ what the sections share

        internal UiContext Ctx => ctx;

        internal CharaRealization Real
        {
            get
            {
                var r = ctx.Runtime?.FindChara(Primary);
                return r != null && r.State == RealizationState.Built && !r.InGraveyard && r.Root ? r : null;
            }
        }

        internal CharaAdvancedPose Block => (ctx.Model.Find(Primary)?.State as CharaNodeState)?.AdvancedPose;

        internal static UiField Row(string path) => InspectorSchemas.Find(InspectorSchemas.Chara, path);

        /// <summary>A step of a drag: one field write inside the open UI edit, shown at once. The edit's commit records one entry.</summary>
        internal OpResult DragWrite(AdvancedPoseWrite? w, string label) => DragWrite(ctx, Primary, w, label);

        /// <summary>A step of a drag on the character <paramref name="id"/> (the gizmo's too).</summary>
        public static OpResult DragWrite(UiContext ctx, string id, AdvancedPoseWrite? w, string label)
        {
            if (w == null) return OpResult.NothingToDo();
            var c = ctx.Session.Commands.Set(id, w.Value.Path, w.Value.Value, UiContext.User(label));
            ctx.Runtime?.ApplyNow();
            return ctx.ShowResult(OpResult.From(c));
        }

        bool ShownNow()
        {
            if (panel == null) return false;
            for (VisualElement e = this; e != null; e = e.hierarchy.parent)
                if (e.resolvedStyle.display == DisplayStyle.None) return false;
            return true;
        }

        /// <summary>Reads weights and marks now; a part that changed rebuilds what lists it. Runs only while the tab is shown.</summary>
        public void Refresh()
        {
            if (!built || !ShownNow()) return;
            Refreshes++;
            var r = Real;
            primaryOnly.style.display = ctx.Selection.Count > 1 ? DisplayStyle.Flex : DisplayStyle.None;
            notBuilt.style.display = r == null ? DisplayStyle.Flex : DisplayStyle.None;
            string key = StructureOf(r);
            if (key != structure)
            {
                structure = key;
                BonesStructure(r);
                JiggleStructure(r);
                BlendStructure(r);
            }
            BonesRefresh(r);
            JiggleRefresh(r);
            BlendRefresh(r);
        }

        // the parts a character has now: the root and each part's object
        static string StructureOf(CharaRealization r)
        {
            var cc = r?.Context;
            if (cc == null) return "";
            var sb = new StringBuilder();
            sb.Append(r.Root.GetHashCode()).Append('|').Append(cc.Head ? cc.Head.GetHashCode() : 0).Append('|');
            foreach (var h in cc.Hair) sb.Append(h ? h.GetHashCode() : 0).Append(',');
            sb.Append('|');
            foreach (var c in cc.Clothes) sb.Append(c ? c.GetHashCode() : 0).Append(',');
            sb.Append('|');
            if (cc.Accessories != null)
                foreach (var s in cc.Accessories) sb.Append(s != null && s.Object ? s.Object.GetHashCode() : 0).Append(',');
            return sb.ToString();
        }

        /// <summary>The model's block changed (an edit, an undo, a load): the rows, marks and lists follow.</summary>
        internal void BlockChanged()
        {
            if (!built) return;
            var r = Real;
            BonesBlockChanged(r);
            JiggleBlockChanged(r);
            BlendBlockChanged(r);
        }

        void OnSub(string address)
        {
            string bone = StudioOps.TryParseSub(address, out var id, out var kind, out var nm) && id == Primary && kind == StudioOps.BoneSubKind ? nm : null;
            SelectBone(bone);
        }

        void EndEdits()
        {
            BonesEndEdits();
            JiggleEndEdits();
            BlendEndEdits();
        }

        /// <summary>The text of a part (<c>body</c>, <c>hair:1</c>, <c>acc:id</c> ...) in the current language.</summary>
        internal static string PartLabel(CharaRealization r, string part)
        {
            if (part == AdvancedPoseResolver.BodyPart) return L10n.Get("inspector.chara.adv.part.body");
            if (part == AdvancedPoseResolver.HeadPart) return L10n.Get("inspector.chara.adv.part.head");
            if (part == PoseAddressGrammar.OtherPart) return L10n.Get("inspector.chara.adv.part.other");
            if (!PoseAddressGrammar.TryParsePart(part, out var kind, out int slot, out string acc, out _)) return part;
            switch (kind)
            {
                case PosePart.Hair: return L10n.Format("inspector.chara.adv.part.hair", L10n.Get(InspectorSchemas.HairPartLabelKey(slot)));
                case PosePart.Clothes: return L10n.Format("inspector.chara.adv.part.clothes", L10n.Get("inspector.chara.state.clothes." + InspectorSchemas.ClothesKinds[slot]));
                case PosePart.Accessory:
                    var slots = r?.Context?.Accessories;
                    if (slots != null)
                        foreach (var s in slots)
                            if (s != null && r.AccessoryIdOf(s) == acc) return L10n.Format("inspector.chara.adv.part.acc", InspectorSchemas.AccessorySlotLabel(s.Slot));
                    return L10n.Format("inspector.chara.adv.part.acc", acc);
                default: return part;
            }
        }

        /// <summary>
        /// The entries of a section that act on nothing on this character: greyed, each with its Remove. The keys are shown as they stand
        /// in the map (a file's key that is no address too), and Remove takes them as they stand.
        /// </summary>
        public sealed class StaleList : VisualElement
        {
            readonly string id;
            readonly Action<string> remove;
            readonly Label title;
            readonly VisualElement list;
            readonly List<string> shown = new List<string>();

            public StaleList(string id, Action<string> remove)
            {
                this.id = id;
                this.remove = remove;
                name = id;
                title = new Label { name = id + "-title" };
                title.AddToClassList(InspectorUi.Dim);
                Add(title);
                list = new VisualElement();
                Add(list);
                style.display = DisplayStyle.None;
            }

            public IReadOnlyList<string> Shown => shown;

            public void Set(IEnumerable<string> keys)
            {
                var next = new List<string>();
                if (keys != null) next.AddRange(keys);
                if (Same(next, shown)) { Title(); return; }
                shown.Clear();
                shown.AddRange(next);
                list.Clear();
                for (int i = 0; i < next.Count; i++)
                {
                    string key = next[i];
                    var l = new Label(key) { tooltip = key };
                    l.AddToClassList(InspectorUi.Dim);
                    l.AddToClassList("hs-row__label");
                    l.style.flexGrow = 1;
                    l.style.flexShrink = 1;
                    l.style.overflow = Overflow.Hidden;
                    l.style.textOverflow = TextOverflow.Ellipsis;
                    var b = InspectorUi.Button("inspector.chara.adv.remove", () => remove(key), id + "-remove-" + i);
                    var line = InspectorUi.Line(l, b);
                    line.name = id + "-" + i;
                    list.Add(line);
                }
                Title();
                style.display = next.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }

            void Title() => title.text = L10n.Format("inspector.chara.adv.stale", shown.Count);

            static bool Same(List<string> a, List<string> b)
            {
                if (a.Count != b.Count) return false;
                for (int i = 0; i < a.Count; i++) if (!string.Equals(a[i], b[i], StringComparison.Ordinal)) return false;
                return true;
            }
        }
    }
}
