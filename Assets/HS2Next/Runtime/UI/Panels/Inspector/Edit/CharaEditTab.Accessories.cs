// The Accessories page of the Edit tab (decisions D3, XQ3, XQ11–XQ15, XQ20,
// XQ21): one page — the slot list (section `accessories.accessories.slots`: a row per worn slot numbered by the build slot,
// the «＋» row and its add panel: a free slot (`acc-add-slot`, default the lowest), one of the 13 types (`acc-add-type-<k>`)
// and the type's item list; a pick is one StudioOps.AddAccessory into that slot, "Add accessory"), then the selected slot's
// group `acc-slot-group-<NN>` in HS2's tab order Type · Colour or Hair colour · Parent · Adjust (InspectorSchemas.AccessoryRows
// with concrete paths, live sections: an item or type change shows the normal or the hair colour section, the areas, the
// alpha colour, the sets and the parent note of the new prefab after the flush that swaps it), then the Copy section (the
// source is the selected slot; a target slot picker `acc-target-slot`; «複製», «枠へコピー…» with an overwrite prompt for an
// occupied slot, the four adjustment copies and their mirrors, enabled by HS2's rules; «左右の親を変更» is the app option
// maker.accessoryMirrorParent). Slot groups are built on first selection, kept for at most 8 accessories (least recently
// shown dropped) and rebuilt when the accessory's slot or type changes; only the selected one is attached. The page edits
// the primary character only (accessory ids are per character): with more than one character selected the page is disabled
// with the tooltip «アクセサリは 1 体ずつ編集します». Rows drawn here: the type choice (`acc-type-<NN>-<k>`; «なし» asks and then
// removes the accessory, one entry "Remove accessory"; another type is one StudioOps.ChangeItem of the category), the parent
// picker (AttachPointPicker; the head-weighted note under its gate), the transform rows (MakerVectorRow; per set a head with
// its step rate `acc-rate-<NN>-<c>` — app options maker.accessoryStep.1 / .2 — and the handle buttons
// `acc-handle-<NN>-<c>-move` / `-rotate` that select the set's handle in the viewport), «初期カラー»
// (`acc-default-colours-<NN>`, normal rows only), «髪の毛の色を取得» (`acc-hair-fetch-<NN>-<part>`), «初期親»
// (`acc-parent-default`), «初期化» (`acc-reset-move-<NN>-<c>`); the hair colour section's colour and slider rows are named
// `field-<path>@hair` (the normal colour section uses the same member paths). edit.show takes `slot` (the 0-based build slot;
// else the slot a section id names) and answers it. An add, a removal or a copy into a new slot changes the character's
// accessory ids, which rebuilds the whole inspector (its State tab lists them): the selection is view state and survives, a
// new accessory added through «＋» is selected on the rebuilt page.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using HS2Next.Content;
using HS2Next.Content.Maker;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Binding;
using HS2Next.UI.Core;
using HS2Next.UI.Panels.Maker;
using HS2Next.UI.Widgets;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    public sealed partial class CharaEditTab
    {
        public const string AccessoriesNode = "accessories";
        /// <summary>Slot groups kept per page (the rest are dropped, least recently shown first).</summary>
        public const int AccessoryGroupsKept = 8;
        /// <summary>The app options of the two transform sets' step rates (fine / normal / coarse).</summary>
        public const string AccessoryStepOptionPrefix = "maker.accessoryStep.";
        const string AccSlotsKey = "<accslots>";
        static readonly string[] AccRates = { "fine", "normal", "coarse" };
        static readonly double[] AccPositionSteps = { 0.1, 1, 10 }, AccRotationSteps = { 1, 5, 10 }, AccScaleSteps = { 0.01, 0.1, 1 };

        sealed class AccGroup
        {
            public string Id;
            public int Slot, Category;
            public VisualElement Root;
            public Label Title;
            public readonly List<string> Sections = new List<string>();
            public long Used;
        }

        TargetSelector accSel;
        LiveSections accLive;
        AccessorySlotList accList;
        VisualElement accBody, accHost, accAddPanel, accAddListHost;
        AccessorySlotPicker accAddSlot, accTarget;
        SegmentedButtons accAddTypes;
        Label accNoSlots, accMultiNote, accCopySource;
        Toggle accMirror;
        int accAddCategory = -1, accSex = 1;
        long accUse;
        string accSlotsSig, accShownId;
        readonly Dictionary<int, PartList> accAddLists = new Dictionary<int, PartList>();
        readonly Dictionary<string, AccGroup> accGroups = new Dictionary<string, AccGroup>(StringComparer.Ordinal);
        readonly Dictionary<string, string> accSectionIds = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly Dictionary<string, Button> accCopyButtons = new Dictionary<string, Button>(StringComparer.Ordinal);

        /// <summary>The slot list (null until the Accessories node is built).</summary>
        public AccessorySlotList AccessorySlots => accList;
        /// <summary>The accessory id whose slot group is shown (null: none).</summary>
        public string AccessoryShownId => accShownId;
        /// <summary>The build slot of the shown slot group (-1: none).</summary>
        public int AccessoryShownSlot => accShownId != null && accGroups.TryGetValue(accShownId, out var g) ? g.Slot : -1;
        /// <summary>The slot groups kept now (tests: at most <see cref="AccessoryGroupsKept"/>).</summary>
        public int AccessoryGroupCount => accGroups.Count;
        /// <summary>The root of the shown slot group (null: none).</summary>
        public VisualElement AccessoryGroup => accShownId != null && accGroups.TryGetValue(accShownId, out var g) ? g.Root : null;
        /// <summary>Time of the last slot switch (ms; a first selection includes the group build; XU4).</summary>
        public double AccessorySlotSwitchMs { get; private set; }
        /// <summary>Slot groups built so far (tests).</summary>
        public int AccessoryGroupBuilds { get; private set; }
        /// <summary>The add panel, its slot picker and the item list shown in it (null until opened).</summary>
        public VisualElement AccessoryAddPanel => accAddPanel;
        public AccessorySlotPicker AccessoryAddSlot => accAddSlot;
        public PartList AccessoryAddList => accAddCategory >= 0 && accAddLists.TryGetValue(accAddCategory, out var l) ? l : null;
        /// <summary>The copy section's target slot picker and buttons by name (duplicate, copyToSlot, copyMove1 …).</summary>
        public AccessorySlotPicker AccessoryTargetPicker => accTarget;
        public IReadOnlyDictionary<string, Button> AccessoryCopyButtonsByName => accCopyButtons;
        /// <summary>The Accessories page's live sections (null until built).</summary>
        public LiveSections AccessoryLive => accLive;
        /// <summary>Accessory sections still waiting for the flush that realizes a change (tests).</summary>
        public int AccessoryPending => accLive?.PendingCount ?? 0;
        /// <summary>Re-evaluates the Accessories page's marked live sections now (tests).</summary>
        public void EvaluateAccessoriesNow() => accLive?.EvaluateNow();
        /// <summary>The last result of an Accessories page button or pick (tests).</summary>
        public OpResult LastAccessoryResult { get; private set; }
        /// <summary>True while the page is disabled because more than one character is selected.</summary>
        public bool AccessoriesMultiDisabled { get; private set; }

        // ================================================================ page

        VisualElement BuildAccessoriesPage(VisualElement c, List<SchemaSection> sections)
        {
            var sw = Stopwatch.StartNew();
            accSel = TargetSelector.Node(primary);
            var n = ctx.Model.Find(primary);
            if (n?.Appearance?.Stored?.Coord == null) return c;   // the import note of a card-backed node (BuildNode added it)
            int sex = StudioOps.SexOf(n);
            accSex = sex < 0 ? 1 : sex;
            accLive = NewLiveSections();
            accLive.Fill = FillAccessorySection;
            accLive.Extra = AccessoryExtra;
            accLive.AddSignal(AccSlotsKey, new[] { InspectorSchemas.CoordEditPath("accessories") }, () => RefreshAccessorySlots());
            accMultiNote = InspectorUi.Note("maker.acc.multi");
            accMultiNote.name = "acc-multi-note";
            accMultiNote.style.display = DisplayStyle.None;
            c.Add(accMultiNote);
            accBody = new VisualElement { name = "acc-page-body" };
            c.Add(accBody);
            WithGateMemo(() =>
            {
                foreach (var s in sections)
                {
                    if (s.Id == InspectorSchemas.AccessorySlotsSection) BuildAccessorySlotSection(s);
                    else if (s.Id == InspectorSchemas.AccessoryCopySection) BuildAccessoryCopySection(s);
                    else AddSection(accBody, s);
                }
                RefreshAccessorySlots(true);
            });
            accLive.Attach(c);
            c.RegisterCallback<AttachToPanelEvent>(_ => { ctx.SelectionChanged -= OnAccessorySelection; ctx.SelectionChanged += OnAccessorySelection; UpdateAccessoryMulti(); });
            c.RegisterCallback<DetachFromPanelEvent>(_ => ctx.SelectionChanged -= OnAccessorySelection);
            UpdateAccessoryMulti();
            PageBuildMs[AccessoriesNode + ".accessories"] = sw.Elapsed.TotalMilliseconds;
            return c;
        }

        void BuildAccessorySlotSection(SchemaSection s)
        {
            var fo = InspectorUi.Section(s.LabelKey, true, "edit-section-" + s.Id);
            sectionFoldouts[s.Id] = fo;
            accList = new AccessorySlotList(PartThumbnails.Shared(ctx.App?.Catalog));
            accList.SelectedChanged += id => SelectAccessory(id);
            accList.AddClicked += () => OpenAccessoryAdd(accAddPanel == null || accAddPanel.style.display == DisplayStyle.None);
            fo.Add(accList);
            accNoSlots = InspectorUi.Note("maker.acc.noSlots");
            accNoSlots.name = "acc-no-slots";
            fo.Add(accNoSlots);
            accAddPanel = new VisualElement { name = "acc-add-panel" };
            accAddPanel.AddToClassList("hs-acc-add");
            accAddPanel.style.display = DisplayStyle.None;
            accAddSlot = new AccessorySlotPicker("acc-add-slot", "maker.acc.addSlot");
            accAddPanel.Add(accAddSlot);
            var typeLine = new VisualElement();
            typeLine.AddToClassList("hs-maker-choice");
            var tl = new Label(L10n.Get("maker.acc.addType"));
            Loc.Bind(tl, "maker.acc.addType");
            tl.AddToClassList("hs-number__label");
            typeLine.Add(tl);
            var labels = new List<string>();
            for (int k = 1; k < InspectorSchemas.AccessoryTypeNames.Count; k++) labels.Add(L10n.Get(InspectorSchemas.AccessoryTypeLabelKey(k)));
            accAddTypes = new SegmentedButtons(labels);
            accAddTypes.AddToClassList(PartSelector.WrapClass);
            for (int k = 1; k < InspectorSchemas.AccessoryTypeNames.Count; k++)
            {
                var b = accAddTypes.Buttons[k - 1];
                b.name = "acc-add-type-" + k.ToString(CultureInfo.InvariantCulture);
                Loc.Bind(b, InspectorSchemas.AccessoryTypeLabelKey(k));
            }
            accAddTypes.Selected += i => ShowAccessoryAddList(InspectorSchemas.AccessoryCategoryFirst + i);
            typeLine.Add(accAddTypes);
            accAddPanel.Add(typeLine);
            accAddListHost = new VisualElement { name = "acc-add-list-host" };
            accAddPanel.Add(accAddListHost);
            var cancel = InspectorUi.Button("maker.acc.addCancel", () => OpenAccessoryAdd(false), "acc-add-cancel");
            accAddPanel.Add(cancel);
            accAddPanel.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == UnityEngine.KeyCode.Escape) { OpenAccessoryAdd(false); e.StopPropagation(); } });
            fo.Add(accAddPanel);
            accBody.Add(fo);
            accHost = new VisualElement { name = "acc-slot-host" };
            accBody.Add(accHost);
        }

        void BuildAccessoryCopySection(SchemaSection s)
        {
            var fo = InspectorUi.Section(s.LabelKey, true, "edit-section-" + s.Id);
            sectionFoldouts[s.Id] = fo;
            accCopySource = new Label { name = "acc-copy-source" };
            accCopySource.AddToClassList(InspectorUi.Dim);
            fo.Add(accCopySource);
            accTarget = new AccessorySlotPicker("acc-target-slot", "maker.acc.copyTarget");
            accTarget.Changed += _ => RefreshAccessoryCopy();
            fo.Add(accTarget);
            var opt = s.Fields.FirstOrDefault(f => f.Kind == FieldKind.Bool && f.Target == FieldTarget.Option);
            if (opt != null)
            {
                accMirror = new Toggle(L10n.Get(opt.LabelKey)) { name = "acc-copy-mirror" };
                accMirror.AddToClassList("hs-field__toggle");
                Loc.Bind(accMirror, opt.LabelKey);
                accMirror.SetValueWithoutNotify(ctx.Options != null && ctx.Options.Bool(opt.Path));
                accMirror.RegisterValueChangedCallback(e => ctx.Options?.Set(opt.Path, e.newValue));
                fo.Add(accMirror);
            }
            var buttons = new VisualElement { name = "acc-copy-buttons" };
            buttons.AddToClassList("hs-acc-copy__buttons");
            foreach (var cb in InspectorSchemas.AccessoryCopyButtons)
            {
                var spec = s.Fields.FirstOrDefault(f => f.Kind == FieldKind.Button && f.Action == cb.action);
                string nm = cb.name;
                var b = InspectorUi.Button(spec?.LabelKey ?? ("inspector.charaEdit.accessories." + nm), () => CopyAccessorySlot(nm), "acc-copy-" + nm);
                accCopyButtons[nm] = b;
                buttons.Add(b);
            }
            fo.Add(buttons);
            accBody.Add(fo);
        }

        void OnAccessorySelection(SelectionChangedArgs _) => UpdateAccessoryMulti();

        void UpdateAccessoryMulti()
        {
            if (accBody == null) return;
            int charas = 0;
            foreach (var id in ctx.Selection.Ids) if (ctx.Model.Find(id)?.Kind == NodeKind.Chara) charas++;
            AccessoriesMultiDisabled = charas > 1;
            accBody.SetEnabled(!AccessoriesMultiDisabled);
            accBody.tooltip = AccessoriesMultiDisabled ? L10n.Get("maker.acc.multi") : null;
            if (accMultiNote != null) accMultiNote.style.display = AccessoriesMultiDisabled ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ================================================================ slot table and selection

        CoordDocument AccCoord => ctx.Model.Find(primary)?.Appearance?.Stored?.Coord;

        /// <summary>The worn slots of the primary as the slot list shows them (build-slot order).</summary>
        List<AccessorySlotInfo> AccessorySlotInfos(out AccessorySlotRule table)
        {
            var l = new List<AccessorySlotInfo>();
            table = StudioOps.AccessorySlotsOf(ctx.Model.Find(primary));
            if (table == null) return l;
            bool extraOn = MakerServices.MoreAccessoriesLoaded?.Invoke() ?? true;
            var lists = MakerCatalog.For(ctx.App?.Catalog);
            string lang = L10n.Language;
            foreach (var kv in table.EntryBySlot)
            {
                var e = kv.Value;
                if (e == null) continue;
                var info = new AccessorySlotInfo { Slot = kv.Key, Id = e.Id, Category = e.Category, Item = e.Item, NotLoaded = AccessorySlotRule.IsExtraSlot(kv.Key) && !extraOn };
                if (ContentId.TryParse(e.Item, out var key))
                {
                    var row = lists?.Source.Find(e.Category, key);
                    if (row != null)
                    {
                        info.Name = MakerLists.NameOf(row, lang);
                        info.Thumb = key;
                        if (!lists.IsListed(e.Category, accSex, key)) { info.NotListed = true; info.Name = L10n.Format("maker.acc.notListed", info.Name); }
                    }
                    else
                    {
                        info.NotListed = true;
                        info.Name = key.SourceGuid != null && key.SourceGuid != ContentIds.Vanilla ? L10n.Format("maker.acc.missingMod", key.SourceGuid) : L10n.Format("maker.acc.notListed", e.Item);
                    }
                }
                else info.Name = e.Item ?? "";
                info.HairType = AccessoryFactsOf(kv.Key, e, out var f) && f.TypeHair;
                l.Add(info);
            }
            return l;
        }

        /// <summary>The prefab facts of the accessory in build slot <paramref name="slot"/>: the realization's, else the row's.</summary>
        bool AccessoryFactsOf(int slot, AccessoryEntry e, out AccessoryFacts facts)
        {
            facts = null;
            if (MakerServices.Accessories != null && MakerServices.Accessories.TrySlot(primary, slot, out facts) && facts != null) return true;
            return e != null && MakerServices.Defaults != null && MakerServices.Defaults.TryAccessoryFacts(e.Category, e.Item, out facts) && facts != null;
        }

        static string AccessorySignature(List<AccessorySlotInfo> infos)
        {
            var sb = new System.Text.StringBuilder(L10n.Language);
            foreach (var s in infos) sb.Append('|').Append(s.Slot).Append(':').Append(s.Id).Append(':').Append(s.Category).Append(':').Append(s.Item).Append(s.HairType ? "h" : "").Append(s.NotLoaded ? "n" : "");
            return sb.ToString();
        }

        /// <summary>The slot list, the selection, the shown group and the pickers after a change of the accessory list.</summary>
        void RefreshAccessorySlots(bool force = false)
        {
            if (accList == null) return;
            var infos = AccessorySlotInfos(out var table);
            string sig = AccessorySignature(infos);
            string want = AccessorySelectorState.Get(primary);
            if (want == null || infos.All(i => i.Id != want))
            {
                // the selected accessory left (removed): the next worn slot after its old slot, else the lowest
                int old = accShownId != null && accGroups.TryGetValue(accShownId, out var og) ? og.Slot : -1;
                want = infos.FirstOrDefault(i => i.Slot > old)?.Id ?? infos.LastOrDefault()?.Id;
                if (old < 0) want = infos.FirstOrDefault()?.Id;
                AccessorySelectorState.Set(primary, want);
            }
            if (force || sig != accSlotsSig)
            {
                accSlotsSig = sig;
                accList.SetSlots(infos, want);
                accNoSlots.style.display = infos.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
                RefreshAccessoryPickers(table, infos);
            }
            else if (accList.SelectedId != want) accList.SetSlots(infos, want);
            ShowAccessorySlot(want);
            foreach (var g in accGroups.Values)
            {
                var i = infos.FirstOrDefault(x => x.Id == g.Id);
                if (i != null) g.Title.text = InspectorSchemas.AccessorySlotLabel(i.Slot) + "  " + i.Name;
            }
            RefreshAccessoryCopy();
        }

        void RefreshAccessoryPickers(AccessorySlotRule table, List<AccessorySlotInfo> infos)
        {
            if (table == null) return;
            var occupied = new Dictionary<int, string>();
            foreach (var i in infos) occupied[i.Slot] = i.Name;
            accAddSlot?.SetSlots(table.FreeSlots(), null, table.LowestFree());
            var all = new List<int>();
            for (int s = 0; s <= table.PickerLast; s++) all.Add(s);
            accTarget?.SetSlots(all, occupied, -1);
        }

        /// <summary>Selects accessory <paramref name="accessoryId"/> of the primary (its slot group is shown; view state, no history).</summary>
        public void SelectAccessory(string accessoryId)
        {
            if (accessoryId == null || AccCoord?.FindAccessory(accessoryId) == null) return;
            AccessorySelectorState.Set(primary, accessoryId);
            if (accList != null && accList.SelectedId != accessoryId) accList.Select(accessoryId);
            ShowAccessorySlot(accessoryId);
            RefreshAccessoryCopy();
        }

        /// <summary>Selects the accessory in build slot <paramref name="slot"/> (0-based); false when the slot holds none.</summary>
        public bool SelectAccessorySlot(int slot)
        {
            var id = StudioOps.AccessorySlotsOf(ctx.Model.Find(primary))?.EntryAt(slot)?.Id;
            if (id == null) return false;
            SelectAccessory(id);
            return true;
        }

        void ShowAccessorySlot(string id)
        {
            if (accHost == null) return;
            var sw = Stopwatch.StartNew();
            var table = StudioOps.AccessorySlotsOf(ctx.Model.Find(primary));
            var entry = id != null ? AccCoord?.FindAccessory(id) : null;
            int slot = entry != null && table != null ? table.SlotOfId(id) : -1;
            if (entry == null || slot < 0)
            {
                accHost.Clear();
                accShownId = null;
                return;
            }
            if (accGroups.TryGetValue(id, out var g) && (g.Slot != slot || g.Category != entry.Category)) { DropAccessoryGroup(g); g = null; }
            if (g == null)
            {
                g = BuildAccessoryGroup(id, slot, entry.Category);
                accGroups[id] = g;
                TrimAccessoryGroups(id);
            }
            g.Used = ++accUse;
            if (accShownId != id || g.Root.parent != accHost)
            {
                accHost.Clear();
                accHost.Add(g.Root);
                // the handle of another accessory of this character is no longer the sub-selection
                if (StudioOps.TryParseAccessorySub(ctx.SubSelection, out var cid, out var aid, out _) && cid == primary && aid != id) ctx.SubSelection = null;
            }
            accShownId = id;
            AccessorySlotSwitchMs = sw.Elapsed.TotalMilliseconds;
        }

        AccGroup BuildAccessoryGroup(string id, int slot, int category)
        {
            var sw = Stopwatch.StartNew();
            string nn = InspectorSchemas.AccessorySlotLabel(slot);
            var g = new AccGroup { Id = id, Slot = slot, Category = category, Root = new VisualElement { name = "acc-slot-group-" + nn } };
            g.Title = new Label(nn) { name = "acc-slot-title" };
            g.Title.AddToClassList("hs-acc-set__title");
            g.Root.Add(g.Title);
            WithGateMemo(() =>
            {
                foreach (var s in InspectorSchemas.AccessoryRows(id, slot, category))
                {
                    accSectionIds[s.Id] = id;
                    g.Sections.Add(s.Id);
                    AddLiveSection(accLive, g.Root, s, slot);
                }
            });
            AccessoryGroupBuilds++;
            PageBuildMs[AccessoriesNode + ".group." + nn] = sw.Elapsed.TotalMilliseconds;
            return g;
        }

        void DropAccessoryGroup(AccGroup g)
        {
            foreach (var sid in g.Sections)
            {
                // a rebuilt group of the same slot registers the same section ids again: drop only this group's entries
                if (accSectionIds.TryGetValue(sid, out var owner) && owner != g.Id) continue;
                accLive.Remove(sid);
                sectionFoldouts.Remove(sid);
                accSectionIds.Remove(sid);
            }
            g.Root.RemoveFromHierarchy();
            accGroups.Remove(g.Id);
        }

        void TrimAccessoryGroups(string keep)
        {
            while (accGroups.Count > AccessoryGroupsKept)
            {
                var old = accGroups.Values.Where(x => x.Id != keep && x.Id != accShownId).OrderBy(x => x.Used).FirstOrDefault();
                if (old == null) break;
                DropAccessoryGroup(old);
            }
        }

        // ================================================================ rows of a slot group

        string AccessoryIdOfSection(string sectionId) => sectionId != null && accSectionIds.TryGetValue(sectionId, out var id) ? id : null;

        static string SectionKindOf(string sectionId)
        {
            int dot = sectionId?.LastIndexOf('.') ?? -1;
            return dot < 0 ? sectionId : sectionId.Substring(dot + 1);
        }

        /// <summary>The section signature beyond the row gates: the parent picker follows the worn row's default key and the
        /// head-weighted gate (the note), refilled when they change.</summary>
        string AccessoryExtra(LiveSections.Entry e, FieldSpec f)
        {
            if (f.Kind != FieldKind.AttachPoint) return null;
            int slot = InspectorSchemas.AccessorySlotOf(e.Section.Id);
            string id = AccessoryIdOfSection(e.Section.Id);
            return (GateHolds(SchemaGates.MakerAcc(slot, SchemaGates.AccHead)) ? "h" : "-") + (AccessoryDefaultKey(id) ?? "");
        }

        /// <summary>The worn row's `Parent` key when it is one of the 54 attach points (what a stored "" means), else null.</summary>
        string AccessoryDefaultKey(string accessoryId)
        {
            var e = accessoryId != null ? AccCoord?.FindAccessory(accessoryId) : null;
            if (e == null) return null;
            string p = MakerEdits.AccessoryDefaultParent(MakerServices.Rows, e.Category, e.Item);
            return string.IsNullOrEmpty(p) ? null : p;
        }

        void FillAccessorySection(LiveSections.Entry e)
        {
            int slot = InspectorSchemas.AccessorySlotOf(e.Section.Id);
            string id = AccessoryIdOfSection(e.Section.Id);
            string kind = SectionKindOf(e.Section.Id);
            if (id == null) return;
            if (kind == "parent" && GateHolds(SchemaGates.MakerAcc(slot, SchemaGates.AccHead)))
            {
                var note = InspectorUi.Note(InspectorSchemas.AccessoryHeadWeightedNoteKey);
                note.name = "acc-parent-head-note";
                e.Fold.Add(note);
            }
            int set = -1;
            foreach (var f in e.Section.Fields)
            {
                if (!GateHolds(f.Gate)) continue;
                if (kind == "adjust" && f.Kind == FieldKind.Vec3 && f.Path.EndsWith(".position", StringComparison.Ordinal))
                {
                    set = f.Path.Contains("moves[1]") ? 2 : 1;
                    e.Fold.Add(AccessorySetHead(slot, id, set));
                }
                var row = BuildAccessoryRow(f, slot, id, kind);
                if (row != null) e.Fold.Add(row);
            }
        }

        VisualElement BuildAccessoryRow(FieldSpec f, int slot, string id, string kind)
        {
            string nn = InspectorSchemas.AccessorySlotLabel(slot);
            switch (f.Kind)
            {
                case FieldKind.Enum when f.Path.EndsWith(".category", StringComparison.Ordinal):
                    return AccessoryTypeRow(f, slot, id);
                case FieldKind.PartList:
                    return new PartList(ctx, f, accSel, f.Path);
                case FieldKind.Color:
                case FieldKind.ColorHdr:
                {
                    var r = new MakerColorRow(ctx, f, accSel, f.Path);
                    if (kind == "hairColour") r.name += "@hair";
                    return r;
                }
                case FieldKind.Float when f.Scale != 1:
                {
                    var r = new MakerSliderRow(ctx, f, accSel, f.Path);
                    if (kind == "hairColour") r.name += "@hair";
                    return r;
                }
                case FieldKind.Bool:
                    return new MakerToggleRow(ctx, f, accSel, f.Path);
                case FieldKind.Vec3:
                {
                    int set = f.Path.Contains("moves[1]") ? 2 : 1;
                    string member = f.Path.Substring(f.Path.LastIndexOf('.') + 1);
                    return new MakerVectorRow(ctx, f, accSel, f.Path, () => AccessoryStep(set, member));
                }
                case FieldKind.AttachPoint:
                    return new AttachPointPicker(ctx, f, accSel, f.Path, () => AccessoryDefaultKey(id));
                case FieldKind.Button:
                    return AccessoryButton(f, nn, id);
                default:
                    return InspectorUi.Row(ctx, f, accSel);
            }
        }

        /// <summary>The step size of transform set <paramref name="set"/> (1, 2) for member position / rotation / scale.</summary>
        public double AccessoryStep(int set, string member)
        {
            int r = AccessoryRate(set);
            return member == "rotation" ? AccRotationSteps[r] : member == "scale" ? AccScaleSteps[r] : AccPositionSteps[r];
        }

        /// <summary>The rate (0 fine, 1 normal, 2 coarse) of transform set <paramref name="set"/> (app option maker.accessoryStep.&lt;set&gt;).</summary>
        public int AccessoryRate(int set)
        {
            int i = Array.IndexOf(AccRates, ctx.Options?.String(AccessoryStepOptionPrefix + set.ToString(CultureInfo.InvariantCulture)));
            return i < 0 ? 1 : i;
        }

        /// <summary>A transform set's head: its title, the step rate choice and the handle buttons.</summary>
        VisualElement AccessorySetHead(int slot, string id, int set)
        {
            string nn = InspectorSchemas.AccessorySlotLabel(slot), c = set.ToString(CultureInfo.InvariantCulture);
            var head = new VisualElement { name = "acc-set-" + nn + "-" + c };
            head.AddToClassList("hs-acc-set__head");
            var title = new Label(L10n.Get(InspectorSchemas.AccessoryMoveLabelKey(set)));
            Loc.Bind(title, InspectorSchemas.AccessoryMoveLabelKey(set));
            title.AddToClassList("hs-acc-set__title");
            head.Add(title);
            var rate = new SegmentedButtons(AccRates.Select(r => L10n.Get("maker.acc.rate." + r)).ToList()) { name = "acc-rate-" + nn + "-" + c };
            for (int i = 0; i < AccRates.Length; i++)
            {
                rate.Buttons[i].name = rate.name + "-" + AccRates[i];
                Loc.Bind(rate.Buttons[i], "maker.acc.rate." + AccRates[i]);
                rate.Buttons[i].tooltip = L10n.Format("maker.acc.rateTip", NumberText.Of(AccPositionSteps[i]), NumberText.Of(AccRotationSteps[i]), NumberText.Of(AccScaleSteps[i]));
            }
            rate.SetDisplay(AccessoryRate(set), false);
            rate.Selected += i =>
            {
                ctx.Options?.Set(AccessoryStepOptionPrefix + c, AccRates[i]);
                rate.SetDisplay(AccessoryRate(set), false);
            };
            head.Add(rate);
            foreach (var (tool, key) in new[] { (GizmoTool.Move, "move"), (GizmoTool.Rotate, "rotate") })
            {
                var t = tool;
                var b = InspectorUi.Button(key == "move" ? "maker.acc.handleMove" : "maker.acc.handleRotate", () => SelectAccessoryHandle(id, set, t), "acc-handle-" + nn + "-" + c + "-" + key);
                Loc.Tooltip(b, "maker.acc.handleTip");
                head.Add(b);
            }
            return head;
        }

        /// <summary>Shows the move / rotate handle of transform set <paramref name="set"/> (1, 2) of accessory
        /// <paramref name="accessoryId"/>: the gizmo tool and the sub-selection `&lt;id&gt;/accmove/&lt;accessoryId&gt;/&lt;set − 1&gt;`.</summary>
        public void SelectAccessoryHandle(string accessoryId, int set, GizmoTool tool)
        {
            if (accessoryId == null || set < 1 || set > 2) return;
            ctx.Tool = tool;
            ctx.SubSelection = StudioOps.AccessorySub(primary, accessoryId, set - 1);
        }

        /// <summary>The type choice of a slot (14 types; «なし» asks, then removes).</summary>
        VisualElement AccessoryTypeRow(FieldSpec f, int slot, string id)
        {
            string nn = InspectorSchemas.AccessorySlotLabel(slot);
            var row = new VisualElement { name = "field-" + f.Path };
            row.AddToClassList(FieldRow.UssClass);
            row.AddToClassList(MakerChoiceRow.UssClass);
            var label = new Label(L10n.Get(f.LabelKey));
            Loc.Bind(label, f.LabelKey);
            label.AddToClassList("hs-number__label");
            row.Add(label);
            var labels = new List<string>();
            for (int k = 0; k < InspectorSchemas.AccessoryTypeNames.Count; k++) labels.Add(L10n.Get(InspectorSchemas.AccessoryTypeLabelKey(k)));
            var seg = new SegmentedButtons(labels);
            seg.AddToClassList(PartSelector.WrapClass);
            for (int k = 0; k < labels.Count; k++)
            {
                seg.Buttons[k].name = "acc-type-" + nn + "-" + k.ToString(CultureInfo.InvariantCulture);
                Loc.Bind(seg.Buttons[k], InspectorSchemas.AccessoryTypeLabelKey(k));
            }
            int current = -1;
            seg.Selected += k =>
            {
                if (k == current) return;
                if (k == 0) ConfirmRemoveAccessory(id);
                else ChangeAccessoryType(id, InspectorSchemas.AccessoryCategoryNone + k);
                seg.SetDisplay(current, false);   // the binding shows the stored type
            };
            row.Add(seg);
            ctx.Bindings.Bind(row, new FieldBinding
            {
                Selector = accSel, Path = f.Path, Spec = f,
                Apply = (el, v) =>
                {
                    current = v.Mixed || v.Value == null ? -1 : (int)FieldValues.ToDouble(v.Value) - InspectorSchemas.AccessoryCategoryNone;
                    seg.SetDisplay(current, v.Mixed);
                },
            });
            return row;
        }

        /// <summary>A type change of accessory <paramref name="accessoryId"/> (category 351..363): one StudioOps.ChangeItem of the
        /// category, "Change item" (HS2's consequences: row 0, the row's parent, sets identity, defaults; tests).</summary>
        public OpResult ChangeAccessoryType(string accessoryId, int category)
        {
            var path = InspectorSchemas.AccessoryEditPath(accessoryId, "category");
            return LastAccessoryResult = ctx.ShowResult(ctx.Ops.ChangeItem(new[] { primary }, path, category.ToString(CultureInfo.InvariantCulture), UiContext.User(OpLabels.ChangeItem)));
        }

        /// <summary>«なし»: asks «スロット NN のアクセサリを外しますか», then removes (one entry "Remove accessory"); without a dialog layer
        /// it removes at once.</summary>
        public void ConfirmRemoveAccessory(string accessoryId)
        {
            int slot = StudioOps.AccessorySlotOf(ctx.Model.Find(primary), accessoryId);
            if (ctx.Dialogs == null) { RemoveAccessoryNow(accessoryId); return; }
            ctx.Dialogs.Confirm("maker.acc.removeTitle", "maker.acc.removeMessage", ok => { if (ok) RemoveAccessoryNow(accessoryId); }, InspectorSchemas.AccessorySlotLabel(slot));
        }

        /// <summary>Removes accessory <paramref name="accessoryId"/> of the primary (one entry "Remove accessory"; tests).</summary>
        public OpResult RemoveAccessoryNow(string accessoryId) =>
            LastAccessoryResult = ctx.ShowResult(ctx.Ops.RemoveAccessory(primary, accessoryId, UiContext.User(OpLabels.RemoveAccessory)));

        /// <summary>The buttons of a slot group: «初期カラー», «髪の毛の色を取得» ×4, «初期親», «初期化» ×2.</summary>
        VisualElement AccessoryButton(FieldSpec f, string nn, string id)
        {
            string a = f.Action;
            if (IsActionOf(f, InspectorSchemas.ActionResetAccessoryColoursPrefix))
                return InspectorUi.Button(f.LabelKey, () => LastAccessoryResult = ctx.ShowResult(ctx.Ops.ResetAccessoryColours(primary, id, UiContext.User(OpLabels.EditAccessory))), "acc-default-colours-" + nn);
            if (IsActionOf(f, InspectorSchemas.ActionMatchAccessoryHairColourPrefix))
            {
                InspectorSchemas.AccessoryIdOfAction(a, InspectorSchemas.ActionMatchAccessoryHairColourPrefix, out int part);
                if (part < 0) return null;
                var b = InspectorUi.Button(f.LabelKey, () => LastAccessoryResult = ctx.ShowResult(ctx.Ops.MatchAccessoryHairColour(primary, id, part, UiContext.User(OpLabels.EditAccessory))),
                    "acc-hair-fetch-" + nn + "-" + InspectorSchemas.HairParts[part]);
                Loc.Tooltip(b, "inspector.charaEdit.accessories.hairFetch");
                return b;
            }
            if (IsActionOf(f, InspectorSchemas.ActionResetAccessoryParentPrefix))
                return InspectorUi.Button(f.LabelKey, () => LastAccessoryResult = ctx.ShowResult(ctx.Ops.ResetAccessoryParent(primary, id, UiContext.User(OpLabels.EditAccessory))), "acc-parent-default");
            if (IsActionOf(f, InspectorSchemas.ActionResetAccessoryMovePrefix))
            {
                InspectorSchemas.AccessoryIdOfAction(a, InspectorSchemas.ActionResetAccessoryMovePrefix, out int set);
                if (set < 1) return null;
                return InspectorUi.Button(f.LabelKey, () => ResetAccessoryMove(id, set), "acc-reset-move-" + nn + "-" + set.ToString(CultureInfo.InvariantCulture));
            }
            return InspectorUi.Row(ctx, f, accSel);
        }

        /// <summary>«初期化» of transform set <paramref name="set"/> (1, 2): position 0, rotation 0, scale 1 as one entry "Edit accessory".</summary>
        public OpResult ResetAccessoryMove(string accessoryId, int set)
        {
            var path = InspectorSchemas.AccessoryEditPath(accessoryId, "moves[" + (set - 1).ToString(CultureInfo.InvariantCulture) + "]");
            return LastAccessoryResult = ctx.ShowResult(ctx.Ops.EditAppearance(new[] { primary }, new List<(string, object)> { (path, new AccessoryMove()) }, UiContext.User(OpLabels.EditAccessory)));
        }

        // ================================================================ «＋»

        /// <summary>Opens (or closes) the add panel: the free slots (the lowest chosen), the 13 types, the chosen type's list.</summary>
        public void OpenAccessoryAdd(bool open)
        {
            if (accAddPanel == null) return;
            accAddPanel.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            if (!open) return;
            var table = StudioOps.AccessorySlotsOf(ctx.Model.Find(primary));
            if (table != null) accAddSlot.SetSlots(table.FreeSlots(), null, table.LowestFree());
            if (accAddCategory < 0) accAddTypes.SetDisplay(-1, false);
            accAddPanel.Focus();
        }

        /// <summary>Shows the item list of <paramref name="category"/> (351..363) in the add panel (built on first use, kept).</summary>
        public PartList ShowAccessoryAddList(int category)
        {
            if (accAddListHost == null || category < InspectorSchemas.AccessoryCategoryFirst || category > InspectorSchemas.AccessoryCategoryLast) return null;
            accAddCategory = category;
            accAddTypes.SetDisplay(category - InspectorSchemas.AccessoryCategoryFirst, false);
            if (!accAddLists.TryGetValue(category, out var list))
            {
                var spec = new FieldSpec
                {
                    Path = InspectorSchemas.AccessoryEditPath("+" + category.ToString(CultureInfo.InvariantCulture), "item"), Kind = FieldKind.PartList,
                    LabelKey = "maker.acc.addItem", CategoryFemale = category, CategoryMale = category, ListContext = FieldSpec.ListContextAccessory,
                    OpLabel = OpLabels.AddAccessory,
                };
                list = new PartList(ctx, spec, accSel, spec.Path) { name = "acc-add-list" };
                int cat = category;
                list.PickOverride = reference => AddAccessoryNow(cat, reference, accAddSlot.Selected);
                accAddLists[category] = list;
            }
            accAddListHost.Clear();
            accAddListHost.Add(list);
            return list;
        }

        /// <summary>Adds item <paramref name="reference"/> of <paramref name="category"/> in build slot <paramref name="slot"/> (-1 = the
        /// lowest free) as the add panel's pick does: one entry "Add accessory"; the new accessory is selected and the panel closes.</summary>
        public OpResult AddAccessoryNow(int category, string reference, int slot)
        {
            var r = ctx.ShowResult(ctx.Ops.AddAccessory(primary, category, reference, null, null, UiContext.User(OpLabels.AddAccessory), slot >= 0 ? slot : (int?)null));
            LastAccessoryResult = r;
            if (r.Ok && r.Value is MakerResult mr && mr.NewAccessoryIds.Count > 0)
            {
                OpenAccessoryAdd(false);
                AccessorySelectorState.Set(primary, mr.NewAccessoryIds[0]);
                // a new accessory id changes the inspector's shape: the inspector was rebuilt during the op, so the Edit tab shown
                // now (when it is another one) selects the new accessory too
                var shown = Current != null && Current != this && Current.primary == primary ? Current : this;
                shown.RefreshAccessorySlots(true);
            }
            return r;
        }

        // ================================================================ copy section

        void RefreshAccessoryCopy()
        {
            if (accCopyButtons.Count == 0) return;
            int src = AccessoryShownSlot, tgt = accTarget?.Selected ?? -1;
            if (accCopySource != null) accCopySource.text = src >= 0 ? L10n.Format("maker.acc.copySource", InspectorSchemas.AccessorySlotLabel(src)) : "";
            if (accMirror != null && ctx.Options != null) accMirror.SetValueWithoutNotify(ctx.Options.Bool(InspectorSchemas.AccessoryMirrorParentOption));
            foreach (var cb in InspectorSchemas.AccessoryCopyButtons)
            {
                if (!accCopyButtons.TryGetValue(cb.name, out var b)) continue;
                string why = CopyBlocked(cb.set, cb.target, src, tgt);
                b.SetEnabled(why == null);
                b.tooltip = why != null ? L10n.Get(why) : null;
            }
        }

        // the reason a copy button is disabled (a string key), or null
        string CopyBlocked(int set, bool target, int src, int tgt)
        {
            if (src < 0) return "maker.acc.noSlots";
            if (!target) return null;
            if (tgt < 0) return "maker.acc.copyNoTarget";
            if (tgt == src) return "maker.acc.copySameSlot";
            if (set == 0) return null;
            if (accTarget == null || !accTarget.IsOccupied(tgt)) return "maker.acc.copyNeedsOccupied";
            string m = SchemaGates.AccMove(set);
            return GateHolds(SchemaGates.MakerAcc(src, m)) && GateHolds(SchemaGates.MakerAcc(tgt, m)) ? null : "maker.acc.copyNeedsSet";
        }

        /// <summary>Runs copy button <paramref name="name"/> (duplicate, copyToSlot, copyMove1 …) from the selected slot to the
        /// picker's target (an occupied target of a slot copy asks first unless <paramref name="confirmed"/>).</summary>
        public OpResult CopyAccessorySlot(string name, bool confirmed = false)
        {
            var cb = InspectorSchemas.AccessoryCopyButtons.FirstOrDefault(x => x.name == name);
            if (cb.name == null || accShownId == null) return LastAccessoryResult = OpResult.NothingToDo();
            int src = AccessoryShownSlot, tgt = accTarget?.Selected ?? -1;
            if (CopyBlocked(cb.set, cb.target, src, tgt) != null) return LastAccessoryResult = OpResult.NothingToDo();
            var parts = cb.set == 0 ? AccessoryCopyParts.Slot : cb.set == 1 ? AccessoryCopyParts.Move1 : AccessoryCopyParts.Move2;
            var mirror = cb.mirror == "leftRight" ? MirrorAxis.LeftRight : cb.mirror == "upDown" ? MirrorAxis.UpDown : MirrorAxis.None;
            bool mirrorParent = cb.set == 0 && ctx.Options != null && ctx.Options.Bool(InspectorSchemas.AccessoryMirrorParentOption);
            int? to = cb.target ? tgt : (int?)null;
            string from = accShownId;
            OpResult Run() => LastAccessoryResult = ctx.ShowResult(ctx.Ops.CopyAccessory(primary, from, to, null, parts, mirrorParent, mirror, UiContext.User(OpLabels.CopyAccessory)));
            if (cb.set == 0 && cb.target && !confirmed && accTarget.IsOccupied(tgt) && ctx.Dialogs != null)
            {
                var occupant = accList?.Slots.FirstOrDefault(s => s.Slot == tgt);
                ctx.Dialogs.Confirm("maker.acc.overwriteTitle", "maker.acc.overwriteMessage", ok => { if (ok) Run(); }, InspectorSchemas.AccessorySlotLabel(tgt), occupant?.Name ?? "");
                return LastAccessoryResult = OpResult.NothingToDo();
            }
            return Run();
        }

        // ================================================================ handles and edit.show

        /// <summary>
        /// The accessory whose transform-set handles the viewport shows: the primary's selected slot while this tab shows the
        /// Accessories page with its Adjust section open, one character selected and the app option maker.accessoryHandles on.
        /// </summary>
        public bool AccessoryHandleTarget(out string charaId, out string accessoryId, out int slot)
        {
            charaId = primary; accessoryId = null; slot = -1;
            if (panel == null || CurrentNode != AccessoriesNode || accShownId == null || AccessoriesMultiDisabled) return false;
            if (ctx.Options != null && !ctx.Options.Bool("maker.accessoryHandles")) return false;
            if (ctx.Selection.Primary != primary) return false;
            slot = AccessoryShownSlot;
            if (slot < 0 || !sectionFoldouts.TryGetValue(InspectorSchemas.AccessorySectionId(slot, "adjust"), out var fo) || !fo.value || fo.panel == null) return false;
            for (VisualElement p = fo; p != null; p = p.parent)
                if (p.resolvedStyle.display == DisplayStyle.None) return false;
            accessoryId = accShownId;
            return true;
        }

        /// <summary>edit.show on the Accessories node: the slot to show (the 0-based argument, else the slot a section id names;
        /// -1 = none); its accessory becomes the selection before the page is built.</summary>
        int PrepareAccessoryReveal(string node, string section, int slot)
        {
            if (node != AccessoriesNode) return -1;
            int s = slot >= 0 ? slot : InspectorSchemas.AccessorySlotOf(section);
            if (s < 0) return -1;
            var id = StudioOps.AccessorySlotsOf(ctx.Model.Find(primary))?.EntryAt(s)?.Id;
            if (id == null) return -1;
            AccessorySelectorState.Set(primary, id);
            return s;
        }

        static int IntArg(object v)
        {
            if (v == null) return -1;
            if (v is string s) return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n >= 0 ? n : -1;
            try { int n = Convert.ToInt32(v, CultureInfo.InvariantCulture); return n >= 0 ? n : -1; }
            catch (Exception) { return -1; }
        }
    }
}
