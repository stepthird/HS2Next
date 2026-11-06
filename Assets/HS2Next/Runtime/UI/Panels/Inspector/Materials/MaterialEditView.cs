// The Materials view: the materialOverrides array of one target, edited on the live materials. One class in three places: the Materials tab
// of the character inspector (a selector picks the character, a hair part, a clothes kind or an accessory; the primary only), the section
// Material edits of an item (the primary only), the section Map material edits of the scene panel (shown while a map is loaded).
// The view reads nothing until it is first shown: its constructor stores what it was given, Opened builds, Closed ends the edits and stops
// the refresh. On a panel it listens through one binding of the array's path and a refresh at most twice a second, which returns at once
// while the view is hidden.
//   the list      a virtualised list: renderers, with their materials under an open renderer. The map lists its materials by name first;
//                 its mode Renderers lists nothing until a search of two characters, at most 200 rows, from names read 5,000 renderers a
//                 frame the first time (MaterialEditView.Selected.cs holds what follows).
//   the rows      only for the selected material: its shader, render queue, keywords and one row per property of the current shader. Each
//                 row shows the live value, marks an item with ● and takes it away with ↺.
//   not applied   every item of the array that does nothing now, with the reason and Remove; built when first open.
// Writes go through StudioOps: a refusal shows as a toast and changes nothing, a row a rule keeps from writing is off. A drag is one UI edit
// (MaterialRows.cs). What the view keeps across rebuilds of the inspector is plain text, per place, while the same owner stays selected.
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    /// <summary>One line of the view's list.</summary>
    public sealed class MaterialListRow
    {
        /// <summary>A renderer line (with a material name null), or the renderer a material line stands under.</summary>
        public MaterialRendererEntry Renderer;
        /// <summary>A material line's name.</summary>
        public string Material;
        /// <summary>A material line's shader, as shown.</summary>
        public string Shader;
        /// <summary>A material line's renderer slots on the map (0: not shown).</summary>
        public int Slots;
        public bool Protected;
        /// <summary>A line of text: a string key.</summary>
        public string Note;
        public int Depth;
        public bool IsRenderer => Material == null && Note == null;
    }

    public sealed partial class MaterialEditView : VisualElement
    {
        public const string TabId = InspectorSchemas.MaterialsTabId;
        /// <summary>The refresh while shown.</summary>
        public const int RefreshMs = 500;
        /// <summary>Characters a renderer search of the map needs, the rows it lists at most, the names read in a frame.</summary>
        public const int SearchMin = 2, ResultCap = 200, NamesPerFrame = 5000;
        const float RowHeight = 24f, ListHeight = 220f;

        /// <summary>Where a view stands.</summary>
        public enum Place { Character, Item, Map }

        /// <summary>What a view keeps across rebuilds of the inspector while the same owner stays selected.</summary>
        sealed class ViewState
        {
            public string Owner;
            public string Target = "character";
            public string Query = "";
            public bool EditedOnly, RenderersMode, SectionOpen, NotAppliedOpen;
            public string Material;
            public readonly HashSet<string> Open = new HashSet<string>(StringComparer.Ordinal);
        }

        static readonly ViewState[] states = new ViewState[3];

        static ViewState StateOf(Place place, string owner)
        {
            int i = (int)place;
            if (states[i] == null || states[i].Owner != owner) states[i] = new ViewState { Owner = owner };
            return states[i];
        }

        readonly UiContext ctx;
        readonly Place place;
        readonly ViewState state;
        bool built;
        string structure;
        IVisualElementScheduledItem refresh;
        FieldBinding binding;
        Label primaryOnly, notBuilt, hint, overColour;
        DropdownField targetField;
        List<(string text, string label)> targetChoices = new List<(string, string)>();
        SegmentedButtons modeField;
        TextField searchField;
        Toggle editedField;
        ListView list;
        Button resetAll;
        readonly List<MaterialListRow> rows = new List<MaterialListRow>();
        readonly List<MaterialListLine> lines = new List<MaterialListLine>();
        readonly List<MaterialRendererEntry> renderers = new List<MaterialRendererEntry>();
        readonly List<MaterialNameEntry> materials = new List<MaterialNameEntry>();
        readonly HashSet<string> editedMaterials = new HashSet<string>(StringComparer.Ordinal), editedRenderers = new HashSet<string>(StringComparer.Ordinal);
        // the map's renderer names, normalised for a search; read a slice a frame
        string[] rendererNames;
        int namesRead;
        MaterialTargets namesOf;
        bool rowsPending;

        public IMaterialTarget Target { get; }
        internal UiContext Ctx => ctx;
        internal StudioOps Ops => ctx.Ops;
        /// <summary>The array as it stands: the document's list.</summary>
        internal List<MaterialOverride> Array => ctx.Ops.MaterialArray(Target.Address);

        /// <summary>Refreshes so far (tests: none while hidden).</summary>
        public int Refreshes { get; private set; }
        /// <summary>The time of the first build and refresh, ms; -1 before; the gen-0 collections in that window (tests).</summary>
        public double FirstShowMs { get; private set; } = -1;
        public int FirstShowCollections { get; private set; }
        /// <summary>True once the view built its elements (tests: not before it is shown).</summary>
        public bool Built => built;
        public string TargetText => state.Target;
        public string Query => state.Query;
        public IReadOnlyList<MaterialListRow> Rows => rows;
        public ListView List => list;
        /// <summary>The list was cut at <see cref="ResultCap"/> (tests).</summary>
        public bool Capped { get; private set; }
        /// <summary>Map renderer names read so far, of how many (tests).</summary>
        public int NamesRead => namesRead;
        public int NamesTotal => rendererNames?.Length ?? 0;

        public MaterialEditView(UiContext ctx, IMaterialTarget target, Place place)
        {
            this.ctx = ctx;
            Target = target;
            this.place = place;
            name = "mat-view";
            state = StateOf(place, target.Owner);
            RegisterCallback<AttachToPanelEvent>(_ => Opened());
            RegisterCallback<DetachFromPanelEvent>(_ => Closed());
        }

        void Opened()
        {
            if (built)
            {
                refresh.Resume();
                Refresh();
                return;
            }
            var sw = Stopwatch.StartNew();
            int gcs = GC.CollectionCount(0);
            Build();
            Refresh();
            FirstShowMs = sw.Elapsed.TotalMilliseconds;
            FirstShowCollections = GC.CollectionCount(0) - gcs;
        }

        void Closed()
        {
            refresh?.Pause();
            EndEdits();
        }

        void Build()
        {
            built = true;
            AddToClassList("hs-materials");
            if (place != Place.Map)
            {
                primaryOnly = InspectorUi.Note("inspector.chara.adv.primaryOnly");
                primaryOnly.name = "mat-primary-only";
                Add(primaryOnly);
            }
            notBuilt = InspectorUi.Note("inspector.notBuilt");
            notBuilt.name = "mat-not-built";
            Add(notBuilt);
            if (Target is CharaMaterialTarget chara)
            {
                chara.Show(state.Target);
                targetField = new DropdownField(new List<string>(), 0) { name = "mat-target" };
                Loc.Bind(targetField, "inspector.materials.target", (e, s) => ((DropdownField)e).label = s);
                targetField.RegisterValueChangedCallback(e =>
                {
                    if (e.target != targetField) return;
                    int i = targetField.index;
                    if (i >= 0 && i < targetChoices.Count) SetTarget(targetChoices[i].text);
                });
                Add(targetField);
            }
            if (Target.MaterialsFirst)
            {
                modeField = new SegmentedButtons(new[] { L10n.Get("inspector.materials.mode.materials"), L10n.Get("inspector.materials.mode.renderers") }) { name = "mat-mode" };
                modeField.Selected += i => SetRenderersMode(i == 1);
                modeField.SetDisplay(state.RenderersMode ? 1 : 0, false);
                Add(modeField);
            }
            searchField = PanelKit.SearchField("mat-search", SetQuery);
            Loc.Bind(searchField, "inspector.materials.search", (e, t) => ((TextField)e).textEdition.placeholder = t);
            searchField.SetValueWithoutNotify(state.Query);
            editedField = new Toggle { name = "mat-edited", value = state.EditedOnly };
            editedField.AddToClassList("hs-field__toggle");
            Loc.Bind(editedField, "inspector.materials.editedOnly", (e, s) => ((Toggle)e).label = s);
            editedField.RegisterValueChangedCallback(e => { if (e.target == editedField) SetEditedOnly(e.newValue); });
            Add(searchField);
            Add(editedField);
            hint = InspectorUi.Note("inspector.materials.searchHint");
            hint.name = "mat-search-hint";
            Add(hint);
            list = new ListView
            {
                name = "mat-list", fixedItemHeight = RowHeight, virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                selectionType = SelectionType.None, itemsSource = rows,
                makeItem = () => { var l = new MaterialListLine(this); lines.Add(l); return l; },
                destroyItem = e => lines.Remove((MaterialListLine)e),
                bindItem = (e, i) => { if (i >= 0 && i < rows.Count) ((MaterialListLine)e).Bind(rows[i]); },
            };
            list.style.height = ListHeight;
            Add(list);
            BuildSelected();
            BuildNotApplied();
            if (place == Place.Item)
            {
                overColour = InspectorUi.Note("inspector.materials.overColour");
                overColour.name = "mat-over-colour";
                overColour.style.whiteSpace = WhiteSpace.Normal;
                Add(overColour);
            }
            resetAll = InspectorUi.Button("inspector.materials.resetAll", ResetAllClicked, "mat-reset-all");
            Add(resetAll);
            refresh = schedule.Execute(Refresh).Every(RefreshMs);
            binding = ctx.Bindings.Bind(this, new FieldBinding { Selector = Target.Selector, Path = Target.Address.ArrayPath, Apply = (el, v) => ArrayChanged() });
            if (Target.MaterialsFirst) PanelKit.EveryFrame(this, ReadNames, fromConstruction: false);
        }

        bool ShownNow()
        {
            if (panel == null) return false;
            for (VisualElement e = this; e != null; e = e.hierarchy.parent)
                if (e.resolvedStyle.display == DisplayStyle.None) return false;
            return true;
        }

        bool Editing => ctx.ActiveEdit != null && ctx.ActiveEdit.IsOpen;

        /// <summary>Reads the live values now; a change of the parts rebuilds the list. Runs only while the view is shown.</summary>
        public void Refresh()
        {
            if (!built || !ShownNow() || Editing) return;
            Refreshes++;
            bool ready = Target.Ready;
            if (primaryOnly != null) primaryOnly.style.display = ctx.Selection.Count > 1 ? DisplayStyle.Flex : DisplayStyle.None;
            notBuilt.style.display = ready ? DisplayStyle.None : DisplayStyle.Flex;
            string key = Target.Structure;
            if (key != structure)
            {
                structure = key;
                ReadTargets();
                ReadEdited();
                ReadList();
                statusDirty = true;
            }
            ShowSelected();
            ReadStatus();
            foreach (var l in lines) l.Show();
        }

        /// <summary>The array changed (an edit, an undo, a load): marks, rows and the list follow, once no edit is open.</summary>
        void ArrayChanged()
        {
            if (!built || structure == null) return;
            if (Editing) { rowsPending = true; return; }
            rowsPending = false;
            statusDirty = true;
            ReadEdited();
            if (state.EditedOnly) ReadList();
            ShowSelected();
            ReadStatus();
            foreach (var l in lines) l.Show();
        }

        /// <summary>A row ended its edit or wrote: everything shows the array as it stands.</summary>
        internal void EditEnded()
        {
            if (!built) return;
            ArrayChanged();
        }

        void EndEdits()
        {
            foreach (var r in valueRows) r.CancelEdit();
        }

        // ------------------------------------------------------------ the target

        void ReadTargets()
        {
            if (!(Target is CharaMaterialTarget chara) || targetField == null) return;
            targetChoices = chara.Choices();
            var labels = new List<string>();
            int shown = 0;
            for (int i = 0; i < targetChoices.Count; i++)
            {
                labels.Add(targetChoices[i].label);
                if (targetChoices[i].text == state.Target) shown = i;
            }
            targetField.choices = labels;
            if (labels.Count == 0) return;
            if (targetChoices[shown].text != state.Target)
            {
                state.Target = targetChoices[shown].text;
                chara.Show(state.Target);
                binding.Path = chara.Address.ArrayPath;
            }
            targetField.SetValueWithoutNotify(labels[shown]);
        }

        /// <summary>Shows one array of the character (<c>character</c>, <c>hair:k</c>, <c>clothes:k</c>, <c>acc:id</c>).</summary>
        public void SetTarget(string text)
        {
            if (!(Target is CharaMaterialTarget chara) || text == null) return;
            state.Target = text;
            if (!built) return;
            if (!chara.Show(text)) return;
            EndEdits();
            state.Material = null;
            binding.Path = chara.Address.ArrayPath;
            structure = null;
            Refresh();
        }

        // ------------------------------------------------------------ the list

        void ReadEdited()
        {
            editedMaterials.Clear();
            editedRenderers.Clear();
            foreach (var item in Array)
            {
                if (item is MeMaterialItem m && m.Material != null) editedMaterials.Add(m.Material);
                else if (item is MeRenderer r && r.Renderer != null) editedRenderers.Add(r.Renderer);
            }
        }

        internal bool MaterialEdited(string material) => material != null && editedMaterials.Contains(material);

        static bool Matches(SearchQuery q, string text) => q.IsEmpty || q.Matches(SearchText.Normalize(text ?? ""));

        void ReadList()
        {
            rows.Clear();
            Capped = false;
            renderers.Clear();
            bool waitsForSearch = false;
            if (Target.Ready)
            {
                if (!Target.MaterialsFirst) ReadRenderers(SearchQuery.Parse(state.Query));
                else if (state.RenderersMode) waitsForSearch = !ReadMapRenderers();
                else ReadMaterials();
            }
            hint.style.display = waitsForSearch ? DisplayStyle.Flex : DisplayStyle.None;
            list.RefreshItems();
        }

        void ReadRenderers(SearchQuery q)
        {
            Target.FillRenderers(renderers);
            foreach (var e in renderers)
            {
                bool self = Matches(q, e.Key);
                bool anyMaterial = false;
                foreach (var m in e.Materials)
                    if ((self || Matches(q, m)) && (!state.EditedOnly || editedMaterials.Contains(m))) anyMaterial = true;
                if (state.EditedOnly && !editedRenderers.Contains(e.Key) && !anyMaterial) continue;
                if (!self && !anyMaterial) continue;
                AddRendererLine(e, q, self);
            }
        }

        void AddRendererLine(MaterialRendererEntry e, SearchQuery q, bool self)
        {
            rows.Add(new MaterialListRow { Renderer = e });
            if (!state.Open.Contains(OpenKey(e)) && q.IsEmpty && !state.EditedOnly) return;
            if (e.DrawsBody)
            {
                rows.Add(new MaterialListRow { Renderer = e, Note = "inspector.materials.drawsBody", Depth = 1 });
                return;
            }
            foreach (var m in e.Materials)
            {
                if ((!self && !Matches(q, m)) || (state.EditedOnly && !editedMaterials.Contains(m))) continue;
                var s = Target.Select(m);
                rows.Add(new MaterialListRow { Renderer = e, Material = m, Shader = ShaderLabel(s.Shown), Protected = e.Member || s.Protected, Depth = 1 });
            }
        }

        string OpenKey(MaterialRendererEntry e) => state.Target + "\n" + e.Key;

        static string ShaderLabel(Material m)
        {
            string s = m && m.shader ? m.shader.name : "";
            return s.StartsWith(MaterialShaders.Prefix, StringComparison.Ordinal) ? s.Substring(MaterialShaders.Prefix.Length) : s;
        }

        void ReadMaterials()
        {
            materials.Clear();
            Target.FillMaterials(materials);
            var q = SearchQuery.Parse(state.Query);
            foreach (var m in materials)
            {
                if (state.EditedOnly && !editedMaterials.Contains(m.Name)) continue;
                if (!Matches(q, m.Name)) continue;
                string shader = m.Shader != null && m.Shader.StartsWith(MaterialShaders.Prefix, StringComparison.Ordinal) ? m.Shader.Substring(MaterialShaders.Prefix.Length) : m.Shader;
                rows.Add(new MaterialListRow { Material = m.Name, Shader = shader, Slots = m.Slots, Protected = m.Protected });
            }
        }

        // The map's renderers matching the search. False while there is nothing to search with: too short a text; names still being read
        // list when they are all there.
        bool ReadMapRenderers()
        {
            string text = (state.Query ?? "").Trim();
            if (text.Length < SearchMin) return false;
            var index = ((IndexedMaterialTarget)Target).Index;
            if (index == null) return true;
            if (namesOf != index)
            {
                namesOf = index;
                rendererNames = new string[index.Renderers.Count];
                namesRead = 0;
            }
            if (namesRead < rendererNames.Length) return true;
            var q = SearchQuery.Parse(text);
            var all = index.Renderers;
            for (int i = 0; i < rendererNames.Length; i++)
            {
                if (rendererNames[i] == null || !q.Matches(rendererNames[i])) continue;
                var r = all[i];
                if (!r) continue;
                if (rows.Count >= ResultCap) { Capped = true; break; }
                var e = new MaterialRendererEntry { Renderer = r, Key = index.PathOf(r), Member = HS2Next.Chara.Mosaic.MaterialGuard.Renderer(r) };
                MaterialTargetKit.NamesOf(r, e.Materials);
                renderers.Add(e);
                rows.Add(new MaterialListRow { Renderer = e });
            }
            return true;
        }

        // A slice of the map's renderer names, each frame until all are read.
        void ReadNames()
        {
            if (rendererNames == null || namesRead >= rendererNames.Length) return;
            var index = ((IndexedMaterialTarget)Target).Index;
            if (index != namesOf || index == null) { namesOf = null; rendererNames = null; namesRead = 0; return; }
            var all = index.Renderers;
            int end = Math.Min(namesRead + NamesPerFrame, rendererNames.Length);
            for (; namesRead < end; namesRead++)
            {
                var r = all[namesRead];
                rendererNames[namesRead] = r ? SearchText.Normalize(index.PathOf(r) ?? "") : null;
            }
            if (namesRead == rendererNames.Length) ReadList();
        }

        /// <summary>Filters the list by name.</summary>
        public void SetQuery(string q)
        {
            state.Query = q ?? "";
            if (searchField != null && searchField.value != state.Query) searchField.SetValueWithoutNotify(state.Query);
            if (built) ReadList();
        }

        /// <summary>Lists only what an item names.</summary>
        public void SetEditedOnly(bool on)
        {
            state.EditedOnly = on;
            if (!built) return;
            editedField.SetValueWithoutNotify(on);
            ReadEdited();
            ReadList();
        }

        /// <summary>The map's list: materials, or renderers.</summary>
        public void SetRenderersMode(bool on)
        {
            state.RenderersMode = on;
            if (!built || modeField == null) return;
            modeField.SetDisplay(on ? 1 : 0, false);
            ReadList();
        }

        /// <summary>Opens or closes a renderer's line.</summary>
        internal void Toggle(MaterialRendererEntry e)
        {
            string k = OpenKey(e);
            if (!state.Open.Remove(k)) state.Open.Add(k);
            ReadList();
        }

        /// <summary>The renderer's line is open.</summary>
        public bool IsOpenFor(MaterialRendererEntry e) => state.Open.Contains(OpenKey(e));

        /// <summary>Opens the line of the renderer that holds the material (the rows of the list for it).</summary>
        public void Reveal(string rendererKey)
        {
            foreach (var e in renderers)
                if (e.Key == rendererKey) { state.Open.Add(OpenKey(e)); break; }
            ReadList();
        }

        /// <summary>Stores a renderer's Enabled (one entry; the value it had without an item removes the item).</summary>
        public OpResult SetRendererEnabled(MaterialRendererEntry e, bool on)
        {
            var r = ctx.ShowResult(ctx.Ops.SetMaterial(Target.Address, MaterialEditRequest.RendererEnabled(e.Key, on), UiContext.User(OpLabels.MaterialEdit)));
            ArrayChanged();
            return r;
        }

        /// <summary>Takes a renderer's item away.</summary>
        public OpResult ResetRenderer(MaterialRendererEntry e)
        {
            var r = ctx.ShowResult(ctx.Ops.ClearMaterial(Target.Address, MeKey.Enabled(e.Key), null, UiContext.User(OpLabels.MaterialReset)));
            ArrayChanged();
            return r;
        }

        /// <summary>Takes every editable item of the array away (one entry).</summary>
        public OpResult ResetAll()
        {
            EndEdits();
            var r = ctx.ShowResult(ctx.Ops.ClearMaterial(Target.Address, null, null, UiContext.User(OpLabels.MaterialClear)));
            ArrayChanged();
            return r;
        }

        void ResetAllClicked() => ResetAll();

        // ------------------------------------------------------------ the places

        /// <summary>Shows the Materials tab of the inspector's character on a target's array (the Edit tab's buttons).</summary>
        public static OpResult Open(UiContext ctx, string target)
        {
            ctx.Shell?.FocusPanel("inspector");
            var ip = ctx.Shell?.PanelRoot("inspector") as InspectorPanel ?? ctx.Shell?.PanelRoot("inspector")?.Q<InspectorPanel>();
            var ch = ip?.Chara;
            if (ch == null) return OpResult.Unsupported("no character inspector");
            ch.ShowTab(TabId);
            var view = ch.TabBodies.TryGetValue(TabId, out var b) ? b.Q<MaterialEditView>() : null;
            if (view == null) return OpResult.Unsupported("no Materials tab");
            view.SetTarget(target);
            return OpResult.Success();
        }

        // A foldout whose content is built when first open, and keeps its state while the same owner stays selected.
        static Foldout Section(UiContext ctx, string key, string id, IMaterialTarget target, Place place)
        {
            var st = StateOf(place, target.Owner);
            var fo = InspectorUi.Section(key, st.SectionOpen, id);
            void Ensure()
            {
                if (fo.value && fo.Q<MaterialEditView>() == null) fo.Add(new MaterialEditView(ctx, target, place));
            }
            fo.RegisterValueChangedCallback(e =>
            {
                if (e.target != fo) return;
                st.SectionOpen = e.newValue;
                Ensure();
            });
            Ensure();
            return fo;
        }

        /// <summary>The section Material edits of an item, collapsed until opened.</summary>
        public static Foldout ItemSection(UiContext ctx, string node) =>
            Section(ctx, "inspector.item.section.materialEdit", "section-" + InspectorSchemas.ItemMaterialEditSection, new ItemMaterialTarget(ctx, node), Place.Item);

        /// <summary>The section Map material edits of the scene panel: collapsed until opened, shown while a map is loaded.</summary>
        public static Foldout MapSection(UiContext ctx)
        {
            var fo = Section(ctx, "inspector.scene.section.mapMaterials", "section-" + InspectorSchemas.MapMaterialsSection, new MapMaterialTarget(ctx), Place.Map);
            var map = ctx.Runtime?.GetPart<MapRealization>();
            void Follow(MapRealization _) => fo.schedule.Execute(() => fo.style.display = map.State == MapState.Loaded ? DisplayStyle.Flex : DisplayStyle.None).ExecuteLater(1);
            fo.style.display = map != null && map.State == MapState.Loaded ? DisplayStyle.Flex : DisplayStyle.None;
            if (map != null)
            {
                fo.RegisterCallback<AttachToPanelEvent>(_ =>
                {
                    map.StateChanged -= Follow;
                    map.StateChanged += Follow;
                    Follow(map);
                });
                fo.RegisterCallback<DetachFromPanelEvent>(_ => map.StateChanged -= Follow);
            }
            return fo;
        }
    }

    /// <summary>A line of the list: a renderer with its toggle, or a material under it.</summary>
    public sealed class MaterialListLine : VisualElement
    {
        readonly MaterialEditView view;
        readonly Label arrow, text, detail, mark;
        readonly Toggle check;
        readonly Button reset;
        string checkTip;

        public MaterialListRow Row { get; private set; }
        public Toggle Check => check;
        public Button Reset => reset;

        internal MaterialListLine(MaterialEditView view)
        {
            this.view = view;
            AddToClassList(InspectorUi.RowClass);
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;
            arrow = new Label { name = "mat-arrow" };
            arrow.style.width = 14;
            text = new Label { name = "mat-text" };
            text.AddToClassList("hs-row__label");
            text.style.flexGrow = 1;
            text.style.flexShrink = 1;
            text.style.overflow = Overflow.Hidden;
            text.style.textOverflow = TextOverflow.Ellipsis;
            detail = new Label { name = "mat-detail" };
            detail.AddToClassList(InspectorUi.Dim);
            detail.style.flexShrink = 1;
            detail.style.overflow = Overflow.Hidden;
            detail.style.textOverflow = TextOverflow.Ellipsis;
            detail.style.maxWidth = new Length(45, LengthUnit.Percent);
            check = new Toggle { name = "mat-enabled" };
            check.RegisterValueChangedCallback(e => { if (e.target == check && Row != null && Row.IsRenderer) view.SetRendererEnabled(Row.Renderer, e.newValue); });
            mark = new Label("●") { name = "mat-mark" };
            mark.AddToClassList("hs-row__badge");
            reset = new Button(() => { if (Row != null && Row.IsRenderer) view.ResetRenderer(Row.Renderer); }) { name = "mat-renderer-reset", text = "↺" };
            reset.AddToClassList("hs-field__default");
            Loc.Tooltip(reset, "inspector.chara.adv.reset");
            Add(arrow);
            Add(text);
            Add(detail);
            Add(check);
            Add(mark);
            Add(reset);
            RegisterCallback<PointerDownEvent>(OnPress);
        }

        void OnPress(PointerDownEvent e)
        {
            if (e.button != 0 || Row == null || Row.Note != null) return;
            if (e.target is VisualElement t && (t == check || check.Contains(t) || t == reset || reset.Contains(t))) return;
            if (Row.IsRenderer) view.Toggle(Row.Renderer);
            else view.SelectMaterial(Row.Material);
        }

        internal void Bind(MaterialListRow row)
        {
            Row = row;
            name = row.IsRenderer ? "mat-renderer-" + row.Renderer.Key : row.Note != null ? "mat-note" : "mat-material-" + row.Material;
            style.paddingLeft = row.Depth * 12;
            text.text = row.IsRenderer ? row.Renderer.Key : row.Note != null ? L10n.Get(row.Note) : row.Material;
            text.tooltip = text.text;
            detail.text = row.Slots > 0 ? L10n.Format("inspector.materials.slots", row.Slots) : row.Shader ?? "";
            detail.tooltip = row.Shader ?? "";
            detail.style.display = !row.IsRenderer && row.Note == null ? DisplayStyle.Flex : DisplayStyle.None;
            check.style.display = row.IsRenderer ? DisplayStyle.Flex : DisplayStyle.None;
            check.SetEnabled(row.IsRenderer && !row.Renderer.Member);
            string tip = row.IsRenderer && row.Renderer.Member ? "inspector.materials.mosaic" : "inspector.materials.enabled";
            if (tip != checkTip) Loc.Tooltip(check, checkTip = tip);
            EnableInClassList(InspectorUi.Dim, row.Note != null || row.Protected);
            Show();
        }

        /// <summary>The live state of the bound line.</summary>
        internal void Show()
        {
            var row = Row;
            if (row == null) return;
            if (row.IsRenderer)
            {
                arrow.text = view.IsOpenFor(row.Renderer) ? "▾" : "▸";
                bool on = row.Renderer.Renderer && row.Renderer.Renderer.enabled;
                check.SetValueWithoutNotify(on);
                var item = MaterialEdits.Find(view.Array, MeKey.Enabled(row.Renderer.Key));
                mark.style.display = item != null ? DisplayStyle.Flex : DisplayStyle.None;
                reset.style.display = item != null ? DisplayStyle.Flex : DisplayStyle.None;
            }
            else
            {
                arrow.text = "";
                mark.style.display = view.MaterialEdited(row.Material) ? DisplayStyle.Flex : DisplayStyle.None;
                reset.style.display = DisplayStyle.None;
            }
            EnableInClassList(InspectorUi.RowSelected, row.Material != null && row.Material == view.SelectedMaterial);
        }
    }
}
