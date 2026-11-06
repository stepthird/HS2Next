// The character inspector: eleven tabs from InspectorSchemas.Chara (Animation, State, Face,
// Look, Hands, FK / IK, Pose, Correction, Advanced pose, Materials, Character) on every selected character (U25; Advanced pose and Materials: the primary). Rows from A's factory except the
// ones drawn here: the current clip (+ the animation browser), clothes rows with the supported states, accessories per
// worn slot, the face / hands index rows, the FK bone list of the applied groups and the 13 IK targets (synchronised with
// the sub-selection), the character file info. The open tab is kept when another character becomes primary. The
// accessory rows live in one container that follows the document, so a change of the accessories does not rebuild the inspector.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using HS2Next.Content;
using HS2Next.Motion.Face;
using HS2Next.Motion.Fk;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Binding;
using HS2Next.UI.Core;
using UnityEngine.UIElements;
using UiField = HS2Next.UI.Core.FieldSpec;

namespace HS2Next.UI.Panels
{
    public sealed class CharaInspector : VisualElement
    {
        /// <summary>The tab shown last (kept across characters while the panel lives).</summary>
        public static string LastTab = "anime";

        readonly UiContext ctx;
        readonly string primary;
        readonly TargetSelector sel = TargetSelector.Selection(NodeKind.Chara);
        readonly VisualElement strip, body;
        readonly Dictionary<string, Button> tabButtons = new Dictionary<string, Button>(StringComparer.Ordinal);
        readonly Dictionary<string, VisualElement> tabBodies = new Dictionary<string, VisualElement>(StringComparer.Ordinal);
        readonly Dictionary<string, VisualElement> subRows = new Dictionary<string, VisualElement>(StringComparer.Ordinal);

        public string CurrentTab { get; private set; }
        public IReadOnlyDictionary<string, VisualElement> TabBodies => tabBodies;

        public CharaInspector(UiContext ctx, string primaryId)
        {
            this.ctx = ctx;
            primary = primaryId;
            name = "inspector-chara";
            strip = new VisualElement { name = "tabs" };
            strip.AddToClassList("hs-inspector__tabs");
            strip.style.flexDirection = FlexDirection.Row;
            strip.style.flexWrap = Wrap.Wrap;
            body = new VisualElement { name = "tab-body" };
            var me = MeShaderLine();
            if (me != null) Add(me);
            Add(strip);
            Add(body);
            foreach (var tab in InspectorSchemas.Chara)
            {
                string id = tab.Id;
                var b = new Button(() => ShowTab(id)) { name = "tab-" + id };
                b.AddToClassList("hs-inspector__tab");
                Loc.Bind(b, tab.LabelKey);
                strip.Add(b);
                tabButtons[id] = b;
                var c = new VisualElement { name = "tab-" + id + "-body" };
                InspectorUi.AddSection(ctx, tab, sel, c, f => Custom(tab.Id, f));
                if (id == "character") AddCharacterInfo(c);
                if (id == MaterialEditView.TabId) c.Add(new MaterialEditView(ctx, new CharaMaterialTarget(ctx, primary), MaterialEditView.Place.Character));   // Phase 6.4: reads nothing until shown
                if (id == "kinematics") c.Add(new ConstraintSection(ctx, primary));   // Phase 6.5: reads nothing until opened
                tabBodies[id] = c;
            }
            CharaCostumeTab.AddTo(ctx, primary, strip, tabButtons, tabBodies, ShowTab);   // Phase 4.4 package W: the Costume tab (outfit card load)
            CharaEditTab.AddTo(ctx, primary, strip, tabButtons, tabBodies, ShowTab);   // Phase 4.0 package C: the Edit tab
            ShowTab(tabBodies.ContainsKey(LastTab) ? LastTab : "anime");
            Highlight(ctx.SubSelection);
        }

        public void ShowTab(string id)
        {
            if (!tabBodies.TryGetValue(id, out var c)) return;
            CurrentTab = LastTab = id;
            body.Clear();
            body.Add(c);
            foreach (var kv in tabButtons) kv.Value.EnableInClassList("hs-inspector__tab--active", kv.Key == id);
        }

        /// <summary>Opens FK / IK and marks the row of a sub-object address of this character (null clears).</summary>
        public void Highlight(string address)
        {
            foreach (var r in subRows.Values) r.RemoveFromClassList(InspectorUi.RowSelected);
            if (!StudioOps.TryParseSub(address, out var id, out var kind, out var nm) || id != primary) return;
            if (kind == "hair") return;   // Phase 4.3 package W: hair bundle handles keep the Edit tab
            if (kind == StudioOps.AccessorySubKind) return;   // Phase 4.5 package W: accessory set handles keep the Edit tab
            if (kind == StudioOps.BoneSubKind) { if (CurrentTab != AdvancedPoseTab.TabId) ShowTab(AdvancedPoseTab.TabId); return; }   // Phase 6.3 package U: the tab brings the row into sight
            string key = kind == "look" ? "look" : kind + "/" + nm;
            if (kind == "look") { ShowTab("look"); return; }
            ShowTab("kinematics");
            if (subRows.TryGetValue(key, out var row))
            {
                row.AddToClassList(InspectorUi.RowSelected);
                // open the foldouts on the way and scroll to the row
                for (var p = row.parent; p != null; p = p.parent) if (p is Foldout fo) fo.value = true;
                row.schedule.Execute(() => row.GetFirstAncestorOfType<ScrollView>()?.ScrollTo(row)).ExecuteLater(30);
            }
        }

        // ------------------------------------------------------------ custom rows

        TreeNode Node => ctx.Model.Find(primary);
        CharaNodeState State => Node?.State as CharaNodeState;
        CharaRealization Real
        {
            get
            {
                var r = ctx.Runtime?.FindChara(primary);
                return r != null && r.State == RealizationState.Built ? r : null;
            }
        }

        AdvancedPoseTab advancedPose;

        // Phase 6.3 package U: the tab draws its rows itself, from the schema's, when it is first shown
        IEnumerable<VisualElement> AdvancedPose()
        {
            if (advancedPose != null) return Array.Empty<VisualElement>();
            advancedPose = new AdvancedPoseTab(ctx, primary);
            return new VisualElement[] { advancedPose };
        }

        IEnumerable<VisualElement> Custom(string tab, UiField f)
        {
            if (tab == AdvancedPoseTab.TabId) return AdvancedPose();
            string p = f.Path;
            if (p == null) return null;
            if (p == "chara.anime.ref") return new[] { ClipRow(f) };
            if (p.StartsWith("chara.state.clothes[", StringComparison.Ordinal))
            {
                var clothes = new ClothesRow(ctx, f, sel, int.Parse(p.Substring(20, 1)));
                clothesRows.Add(clothes);
                return new VisualElement[] { clothes };
            }
            if (p.StartsWith("chara.state.accessories[", StringComparison.Ordinal)) return Accessories(f);
            switch (p)
            {
                case "chara.face.eyebrow": return new[] { new IndexRow(ctx, f, sel, id => ListsOf(id).Eyebrow.Length) };
                case "chara.face.eyes": return new[] { new IndexRow(ctx, f, sel, id => ListsOf(id).Eyes.Length) };
                case "chara.face.mouth": return new[] { new IndexRow(ctx, f, sel, MouthCount) };
                case "chara.hands[0]":
                case "chara.hands[1]": return new[] { new IndexRow(ctx, f, sel, HandCount, (id, i) => i == 0 ? L10n.Get("inspector.chara.hands.fromAnime") : (i - 1).ToString()) };
                case "chara.fk.hipsPosition": return new VisualElement[] { new HipsPositionRow(ctx, f, sel) };
                case "chara.ik.targets.{target}.pos": return IkTargets(f);
                case "chara.ik.targets.{target}.rot": return Array.Empty<VisualElement>();
            }
            if (p == "chara.fk.bones[\"{bone}\"]") return FkBones(f);
            return null;
        }

        ExpressionLists ListsOf(string id)
        {
            var n = ctx.Model.Find(id);
            return n != null ? ctx.Ops.ListsOf(n) : new ExpressionLists();
        }

        int MouthCount(string id)
        {
            var r = ctx.Runtime?.FindChara(id);
            int n = r != null && r.State == RealizationState.Built && r.Expression != null ? r.Expression.MouthPtnCount : 0;
            return n > 0 ? n : 26;
        }

        int HandCount(string id)
        {
            var r = ctx.Runtime?.FindChara(id);
            int n = r != null && r.State == RealizationState.Built && r.Animation && r.Animation.Hands != null ? r.Animation.Hands.PatternCount : 0;
            return (n > 0 ? n : 21) + 1;
        }

        VisualElement ClipRow(UiField f)
        {
            var name = new Label { name = "clip-name" };
            name.AddToClassList("hs-row__label");
            name.style.flexGrow = 1;
            var lab = Loc.Label(f.LabelKey);
            lab.AddToClassList("hs-field__label");
            var choose = InspectorUi.Button("inspector.chara.anime.choose", () => ctx.ShowResult(InspectorFlows.BrowseAnime(ctx)), "choose");
            var row = InspectorUi.Line(lab, name, choose);
            row.name = "field-chara.anime.ref";
            ctx.Bindings.Bind(name, sel, "chara.anime.ref", (el, v) =>
            {
                var a = v.Value as AnimeRef;
                var r = InspectorGates.AnimeRow(ctx.Ops, a);
                ((Label)el).text = v.Mixed ? "—" : r != null ? r.Name : a == null ? L10n.Get("common.none") : a.Group + "/" + a.Category + "/" + a.No;
            });
            return row;
        }

        VisualElement accessoryRows;
        UiField accessorySpec;
        string accessoriesShown, clothesStatesShown;
        readonly List<ClothesRow> clothesRows = new List<ClothesRow>();

        // The rows of the worn accessories: a container refilled when what its rows show changes (an add, a removal, a copy into a
        // new slot, a slot or item change, a coordinate load, their undo), while the State tab is shown; attaching it compares again.
        IEnumerable<VisualElement> Accessories(UiField f)
        {
            accessorySpec = f;
            accessoryRows = new VisualElement { name = "state-accessory-rows" };
            FollowAccessories();
            accessoryRows.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                ctx.SceneChanged -= OnAccessoriesChanged;
                ctx.SceneChanged += OnAccessoriesChanged;
                FollowAccessories();
            });
            accessoryRows.RegisterCallback<DetachFromPanelEvent>(_ => ctx.SceneChanged -= OnAccessoriesChanged);
            accessoryRows.schedule.Execute(FollowClothesStates).Every(500);
            return new[] { accessoryRows };
        }

        // While the State tab is shown its clothes rows follow the states the worn outfit supports, known after the flush that
        // swaps the parts (a coordinate load, an item change, their undo); their binding only hears the stored states.
        void FollowClothesStates()
        {
            string s = ctx.Runtime?.FindChara(primary)?.ClothesStates.ToString();
            if (s == clothesStatesShown) return;
            clothesStatesShown = s;
            foreach (var r in clothesRows) r.Binding.MarkDirty();
        }

        void OnAccessoriesChanged(SceneChangeBatch b)
        {
            if (b != null && b.Touches(primary, InspectorSchemas.CoordEditPath("accessories"))) FollowAccessories();
        }

        // Phase 4.5 package W (REVIEW F1): rows in build-slot order, labelled with the
        // build slot of the slot table (the number the Maker's slot list and the bridge show); card-backed nodes as before.
        void FollowAccessories()
        {
            var n = Node;
            var ids = n != null ? StudioOps.AccessoryIdsOf(n) : null;
            var coord = n?.Appearance?.Stored?.Coord;
            var table = coord != null ? StudioOps.AccessorySlotsOf(n) : null;
            var order = new List<(int slot, string id, HS2Next.Save.AccessoryEntry entry)>();
            for (int i = 0; ids != null && i < ids.Count; i++)
            {
                string id = ids[i];
                int slot = i;
                var a = coord?.FindAccessory(id);
                if (a != null && table != null)
                {
                    int built = table.SlotOfId(id);
                    if (built >= 0) slot = built;
                }
                order.Add((slot, id, a));
            }
            if (table != null) order.Sort((x, y) => x.slot.CompareTo(y.slot));
            var sig = new StringBuilder();
            foreach (var (slot, id, a) in order) sig.Append(slot).Append(':').Append(id).Append(':').Append(a?.Category).Append(':').Append(a?.Item).Append('|');
            string s = sig.ToString();
            if (s == accessoriesShown) return;
            accessoriesShown = s;
            accessoryRows.Clear();
            if (order.Count == 0)
            {
                var none = InspectorUi.Note("inspector.chara.state.noAccessories");
                none.name = "state-no-accessories";
                accessoryRows.Add(none);
                return;
            }
            var cat = ctx.App.Catalog;
            foreach (var (slot, id, a) in order)
            {
                string part = null;
                if (a != null)
                {
                    if (cat != null && a.Item != null && ContentId.TryParse(a.Item, out var key)) part = cat.GetEntry(a.Category, key)?["Name"];
                    part ??= a.Item;
                }
                string label = L10n.Format("inspector.chara.state.accessorySlot", InspectorSchemas.AccessorySlotLabel(slot), part ?? id);
                var row = InspectorUi.Row(ctx, accessorySpec, sel, null, id, label);
                row.name = "accessory-" + id;
                accessoryRows.Add(row);
            }
        }

        IEnumerable<VisualElement> FkBones(UiField f)
        {
            var st = State;
            var real = Real;
            if (st == null || !st.Fk.Enabled) { yield return InspectorUi.Note("inspector.chara.fk.noBones"); yield break; }
            if (real == null) { yield return InspectorUi.Note("inspector.notBuilt"); yield break; }
            var table = FkDriver.TableFor(real);
            if (table == null) yield break;
            // bones the IK solve owns show "IK-driven" (IkCharacter.Owned; only while IK is on)
            var owned = st.Ik.Enabled ? IkDriver.Of(real)?.Owned : null;
            foreach (FkGroup g in Enum.GetValues(typeof(FkGroup)))
            {
                if (!Applied(st.Fk.Groups, g)) continue;
                string gname = InspectorSchemas.FkGroups[(int)g];
                var fo = InspectorUi.Section("inspector.chara.fk.group." + gname, false, "fk-group-" + gname);
                foreach (var b in table.Of(g))
                {
                    if (b?.Key == null) continue;
                    string key = b.Key;
                    var row = InspectorUi.Row(ctx, f, sel, null, key, key);
                    row.name = "fk-" + key;
                    if (owned != null && b.Bone && Contains(owned, b.Bone))
                    {
                        var mark = Loc.Label("inspector.chara.fk.ikMark");
                        mark.AddToClassList("hs-row__badge");
                        Loc.Tooltip(mark, "inspector.chara.fk.overriddenByIk");
                        row.Add(mark);
                    }
                    row.RegisterCallback<PointerDownEvent>(e => { if (e.button == 0 && e.target is Label) ctx.SubSelection = primary + "/fk/" + key; });
                    subRows["fk/" + key] = row;
                    fo.Add(row);
                }
                yield return fo;
            }
        }

        static bool Contains(IReadOnlyCollection<UnityEngine.Transform> set, UnityEngine.Transform t)
        {
            if (set is ICollection<UnityEngine.Transform> c) return c.Contains(t);
            foreach (var x in set) if (x == t) return true;
            return false;
        }

        static bool Applied(FkGroups g, FkGroup x)
        {
            switch (x)
            {
                case FkGroup.Hair: return g.Hair;
                case FkGroup.Neck: return g.Neck;
                case FkGroup.Breast: return g.Breast;
                case FkGroup.Body: return g.Body;
                case FkGroup.RightHand: return g.RightHand;
                case FkGroup.LeftHand: return g.LeftHand;
                default: return g.Skirt;
            }
        }

        IEnumerable<VisualElement> IkTargets(UiField posSpec)
        {
            UiField rotSpec = null;
            foreach (var s in InspectorSchemas.Chara)
                foreach (var x in s.Fields)
                    if (x.Path == "chara.ik.targets.{target}.rot") rotSpec = x;
            foreach (var t in InspectorSchemas.IkTargets)
            {
                string target = t;
                var box = new VisualElement { name = "ik-" + target };
                box.AddToClassList(InspectorUi.RowClass);
                var title = Loc.Label("ik.target." + target);
                title.AddToClassList("hs-row__label");
                title.RegisterCallback<PointerDownEvent>(e => { if (e.button == 0) ctx.SubSelection = primary + "/ik/" + target; });
                box.Add(title);
                box.Add(new IkTargetRow(ctx, posSpec, sel, target));
                if (rotSpec != null)
                {
                    var rot = new IkTargetRow(ctx, rotSpec, sel, target);
                    // only hands and feet use a rotation (the others store 0)
                    rot.SetEnabled(target.EndsWith("Hand", StringComparison.Ordinal) || target.EndsWith("Foot", StringComparison.Ordinal));
                    box.Add(rot);
                }
                subRows["ik/" + target] = box;
                yield return box;
            }
        }

        // The MaterialEditor shaders this character's swaps name and HS2Next cannot draw (finding me.shaderUnavailable): one
        // line with the names above the tabs; its tooltip names the way out (a plugin can supply a URP shader for a name).
        VisualElement MeShaderLine()
        {
            var names = Real?.UnavailableMeShaders;
            if (names == null || names.Count == 0) return null;
            var l = InspectorUi.Note("inspector.chara.meShaderUnavailable", string.Join(", ", names));
            l.name = "me-shader-unavailable";
            l.RemoveFromClassList(InspectorUi.Dim);
            l.AddToClassList("hs-text--warning");
            l.style.whiteSpace = WhiteSpace.Normal;
            Loc.Tooltip(l, "inspector.chara.meShaderUnavailable.tip");
            return l;
        }

        void AddCharacterInfo(VisualElement c)
        {
            var n = Node;
            var info = new VisualElement { name = "chara-info" };
            var ap = n?.Appearance;
            string src = ap?.Card != null ? Path.GetFileName(ap.Card.FullPath ?? "") : ap?.Stored != null ? L10n.Get("inspector.chara.character.stored") : L10n.Get("common.none");
            info.Add(InspectorUi.Note("inspector.chara.character.source", src));
            var real = ctx.Runtime?.FindChara(primary);
            if (real != null && real.State != RealizationState.Built)
                info.Add(InspectorUi.Note("inspector.notBuiltState", real.State.ToString() + (real.Detail != null ? ": " + real.Detail : "")));
            c.Insert(0, info);
        }

        /// <summary>The part of the selection's state that changes which rows exist (rebuild when it differs).</summary>
        public static string ShapeOf(UiContext ctx, string id)
        {
            var n = ctx.Model.Find(id);
            var st = n?.State as CharaNodeState;
            if (st == null) return "";
            var sb = new StringBuilder();
            var r = ctx.Runtime?.FindChara(id);
            sb.Append(r?.State.ToString()).Append('|').Append(StudioOps.SexOf(n)).Append('|');
            sb.Append(st.Fk.Enabled).Append(st.Ik.Enabled).Append('|');
            // the IK-owned bones (the FK rows' mark) are known one solve after IK turns on
            if (st.Fk.Enabled && st.Ik.Enabled && r != null && r.State == RealizationState.Built) sb.Append(IkDriver.Of(r)?.Owned.Count ?? 0).Append('|');
            var g = st.Fk.Groups;
            sb.Append(g.Hair).Append(g.Neck).Append(g.Breast).Append(g.Body).Append(g.RightHand).Append(g.LeftHand).Append(g.Skirt).Append('|');
            sb.Append(n.Appearance?.Card?.FullPath).Append(n.Appearance?.Stored != null);
            return sb.ToString();
        }
    }
}
