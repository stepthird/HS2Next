// The character browser (U24): female / male
// switch, folder tree, a thumbnail grid (126 × 176 at zoom 1, 0.5–1.5) or a list (name, file, date), search over the file
// name and the lazily indexed character name, sort name (default ascending, ja-JP) / date / file. Add (double click, Enter,
// the Add button, or a drag onto the viewport / a tree row) through StudioOps.AddChara with the option "use card state";
// Change character (enabled when every selected node is a character) through StudioOps.ChangeCharacter. Card images are
// loaded and counted, never inspected. v0.3 trial 2: the thumbnail size slider is the cell width in px (63–189, default 126)
// kept in the view-state option view.thumbSize.chara; the footer's first line says what a click and a double click do and
// names the selected card; the cells' tooltips end with the same hint.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    public sealed class CharaBrowser : VisualElement
    {
        public const string PanelId = "add.chara";

        readonly UiContext ctx;
        readonly ThumbnailLoader loader;
        readonly CardNameIndex names;
        readonly FolderTree folders;
        readonly CardGrid<CardEntry> grid;
        readonly MultiColumnListView listView;
        readonly Label status, hint;
        readonly Button addButton, changeButton;
        readonly Toggle useCardState;
        readonly Button femaleButton, maleButton, gridButton, listButton, dirButton;
        readonly DropdownField sortField;
        readonly Slider zoom;
        readonly CardLibrary[] libs = new CardLibrary[2];
        readonly ListIndex<CardEntry>[] indexes = new ListIndex<CardEntry>[2];
        readonly string[] queries = { "", "" }, folderSel = new string[2];
        readonly List<CardEntry> drained = new List<CardEntry>();
        readonly Task<CardLibrary>[] scans = new Task<CardLibrary>[2];
        int sex = 1;   // 1 female, 0 male (vanilla list per sex)
        bool listMode;
        string folderKey;

        public static CharaBrowser Current { get; private set; }
        public ThumbnailLoader Loader => loader;
        public CardNameIndex Names => names;
        public CardGrid<CardEntry> Grid => grid;
        public int Sex => sex;
        public CardLibrary Library => libs[sex];
        public ListIndex<CardEntry> Index => indexes[sex];
        public bool Ready => indexes[sex] != null;
        /// <summary>The folder tree and the grid / list (side by side or stacked by the region's shape).</summary>
        public AdaptiveSplit Body { get; }

        public CharaBrowser(UiContext ctx)
        {
            this.ctx = ctx;
            Current = this;
            name = "chara-browser"; PanelKit.Style(this);
            AddToClassList("hs-panel");
            AddToClassList("hs-browser");
            loader = new ThumbnailLoader();
            loader.Unreadable += p => ctx.Findings.Info("ui.thumbnailUnreadable", null, L10n.Format("msg.thumbnailUnreadable", System.IO.Path.GetFileName(p)));
            names = new CardNameIndex();

            // header: sex, search, sort, view mode, zoom
            var header = new VisualElement { name = "chara-header" };
            header.AddToClassList("hs-panel__header");
            femaleButton = Seg("chara-female", "action.add.female", () => SetSex(1));
            maleButton = Seg("chara-male", "action.add.male", () => SetSex(0));
            header.Add(femaleButton);
            header.Add(maleButton);
            var search = PanelKit.SearchField("chara-search", q => { queries[sex] = q; Index?.SetQuery(q); Refresh(); });
            search.AddToClassList("hs-grow");
            header.Add(search);
            sortField = new DropdownField { name = "chara-sort", choices = new List<string> { "name", "date", "file" }, index = 0 };
            sortField.formatListItemCallback = s => L10n.Get("sort." + s);
            sortField.formatSelectedValueCallback = s => L10n.Get("sort." + s);
            sortField.RegisterValueChangedCallback(e => { ApplySort(); Refresh(); });
            header.Add(sortField);
            dirButton = new Button(() => { descending = !descending; ApplySort(); Refresh(); }) { name = "chara-sort-dir", text = "▲" };
            dirButton.AddToClassList("hs-icon-button");
            Loc.Tooltip(dirButton, "sort.ascending");
            header.Add(dirButton);
            gridButton = Seg("chara-grid-mode", "browser.grid", () => SetListMode(false));
            listButton = Seg("chara-list-mode", "browser.list", () => SetListMode(true));
            header.Add(gridButton);
            header.Add(listButton);
            zoom = new Slider(OptionDefs.CharaThumbMin, OptionDefs.CharaThumbMax) { name = "chara-zoom", value = ThumbSize };
            zoom.AddToClassList("hs-zoom");
            Loc.Tooltip(zoom, "browser.zoom");
            zoom.RegisterValueChangedCallback(e => SetThumbSize(e.newValue));
            header.Add(zoom);
            Add(header);

            // body: folders | grid / list in a wide region, folders above the grid / list in a tall one (a sidebar)
            folders = new FolderTree("chara-folders", k => L10n.Get(k == CardLibrary.RootHs2 ? "browser.root.hs2" : "browser.root.hs2next"));
            folders.Selected += k => { folderKey = k; folderSel[sex] = k; Index?.AddFacet("folder", e => CardLibrary.InFolder(e, folderKey) ? "in" : "out"); Index?.SetFacet("folder", folderKey == null ? null : "in"); Refresh(); };
            var right = new VisualElement();
            right.AddToClassList("hs-grow");
            grid = new CardGrid<CardEntry>("chara-grid", loader, e => e.FullPath, e => e.DisplayName, e => !e.IsHs2n || true);
            grid.AddToClassList("hs-grow");
            grid.Zoom = ThumbSize / CardGrid<CardEntry>.BaseWidth;
            grid.HintKey = HintKey;
            grid.Activated += e => AddCard(e, null, null);
            grid.SelectionChanged += _ => UpdateButtons();
            grid.DragPayloadOf = e => new AddPayload(e.DisplayName, OpLabels.AddCharacter, (parent, point) => AddCard(e, parent, point));
            right.Add(grid);
            listView = MakeList();
            right.Add(listView);
            var body = new AdaptiveSplit("chara-body", folders, right, 180, 0.25f, 110);
            body.AddToClassList("hs-grow");
            Body = body;
            Add(body);

            // footer
            var footer = new VisualElement { name = "chara-footer" };
            footer.AddToClassList("hs-panel__footer");
            addButton = Loc.Button("browser.add", () => { var s = SelectedEntry; if (s != null) AddCard(s, null, null); });
            addButton.name = "chara-add";
            changeButton = Loc.Button("browser.changeChara", () => { var s = SelectedEntry; if (s != null) ChangeTo(s); });
            changeButton.name = "chara-change";
            useCardState = new Toggle { name = "chara-use-card-state", value = ctx.Options.Bool(OptionDefs.AddUseCardState) };
            Loc.Bind(useCardState, "option.add.useCardState", (e, t) => ((Toggle)e).text = t);
            useCardState.RegisterValueChangedCallback(e => ctx.Options.Set(OptionDefs.AddUseCardState, e.newValue));
            status = new Label { name = "chara-status" };
            status.AddToClassList("hs-grow");
            status.AddToClassList("hs-status-text");
            hint = new Label { name = "chara-hint" };
            hint.AddToClassList("hs-browser__hint");
            footer.Add(hint);
            footer.Add(addButton);
            footer.Add(changeButton);
            footer.Add(useCardState);
            footer.Add(status);
            Add(footer);

            ctx.SelectionChanged += _ => UpdateButtons();
            ctx.Options.Changed += k =>
            {
                if (k == OptionDefs.AddUseCardState) useCardState.SetValueWithoutNotify(ctx.Options.Bool(k));
                // the size set elsewhere (the options panel's row, its reset)
                if (k == OptionDefs.ThumbSizeChara && Math.Abs(grid.CellWidth - ThumbSize) > 0.01f) { zoom.SetValueWithoutNotify(ThumbSize); grid.Zoom = ThumbSize / CardGrid<CardEntry>.BaseWidth; }
            };
            L10n.Default.LanguageChanged += _ => UpdateHint();
            PanelKit.EveryFrame(this, Tick);
            RegisterCallback<DetachFromPanelEvent>(_ => { });
            SetListMode(false);
            SetSex(1);
            UpdateHint();
        }

        bool descending;

        /// <summary>The string key of the cells' and the footer's hint (what a click / double click / Enter does).</summary>
        public const string HintKey = "browser.chara.hint";
        /// <summary>The footer's hint line (tests).</summary>
        public Label HintLabel => hint;
        /// <summary>The thumbnail size slider (cell width in px; tests).</summary>
        public Slider ZoomSlider => zoom;

        /// <summary>The cell width of the option view.thumbSize.chara (px).</summary>
        float ThumbSize => ctx.Options.Int(OptionDefs.ThumbSizeChara) is int px && px > 0 ? px : OptionDefs.CharaThumbDefault;

        /// <summary>Sets the cell width (px; the slider): the grid and the view-state option.</summary>
        public void SetThumbSize(float px)
        {
            px = Mathf.Round(Mathf.Clamp(px, OptionDefs.CharaThumbMin, OptionDefs.CharaThumbMax));   // whole px: the option's unit
            grid.Zoom = px / CardGrid<CardEntry>.BaseWidth;
            if (Math.Abs(zoom.value - px) > 0.01f) zoom.SetValueWithoutNotify(px);
            int stored = Mathf.RoundToInt(px);
            if (ctx.Options.Int(OptionDefs.ThumbSizeChara) != stored) ctx.Options.Set(OptionDefs.ThumbSizeChara, stored);
        }

        void UpdateHint()
        {
            var s = SelectedEntry;
            hint.text = s != null ? L10n.Format("browser.chara.selected", s.DisplayName) : L10n.Get(HintKey);
        }

        Button Seg(string id, string key, Action a)
        {
            var b = Loc.Button(key, a);
            b.name = id;
            b.AddToClassList("hs-seg");
            return b;
        }

        public CardEntry SelectedEntry => listMode
            ? (listView.selectedIndex >= 0 && Index != null && listView.selectedIndex < Index.Indices.Count ? Index.Rows[Index.Indices[listView.selectedIndex]] : null)
            : grid.Selected;

        MultiColumnListView MakeList()
        {
            var lv = new MultiColumnListView
            {
                name = "chara-list",
                fixedItemHeight = 22,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                selectionType = SelectionType.Single,
                showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly,
            };
            lv.AddToClassList("hs-grow");
            Column(lv, "name", "sort.name", 240, e => e.DisplayName);
            Column(lv, "file", "sort.file", 220, e => e.FileName);
            Column(lv, "date", "sort.date", 140, e => e.ModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
            lv.itemsChosen += items => { var s = SelectedEntry; if (s != null) AddCard(s, null, null); };
            lv.selectionChanged += _ => UpdateButtons();
            return lv;
        }

        void Column(MultiColumnListView lv, string id, string titleKey, float width, Func<CardEntry, string> text)
        {
            var c = new Column { name = id, title = L10n.Get(titleKey), width = width, stretchable = id == "name" };
            c.makeCell = () =>
            {
                var l = new Label();
                l.AddToClassList("hs-list-cell");
                DragController.MakeSource(l, _ => l.userData is CardEntry e ? new AddPayload(e.DisplayName, OpLabels.AddCharacter, (p, pt) => AddCard(e, p, pt)) : null);
                return l;
            };
            c.bindCell = (el, i) =>
            {
                var e = Index.Rows[Index.Indices[i]];
                ((Label)el).text = text(e);
                el.userData = e;
            };
            lv.columns.Add(c);
        }

        // ------------------------------------------------------------------ data

        void SetSex(int s)
        {
            sex = s;
            femaleButton.EnableInClassList("hs-seg--on", s == 1);
            maleButton.EnableInClassList("hs-seg--on", s == 0);
            if (libs[s] == null && scans[s] == null)
            {
                string hs2 = ctx.App.Paths?.Hs2CharaDir(s), own = ctx.App.Paths?.CharaDir;
                scans[s] = Task.Run(() => CardLibrary.Scan(hs2, own));
                status.text = L10n.Get("browser.scanning");
            }
            folderKey = folderSel[s];
            Show();
        }

        /// <summary>The scan of the current sex finished (polled each frame).</summary>
        void Show()
        {
            var lib = libs[sex];
            if (lib == null) { grid.SetItems(Array.Empty<CardEntry>(), Array.Empty<int>()); return; }
            folders.SetFolders(lib.Folders, lib.Entries.Select(e => e.FolderKey));
            folders.Select(folderKey);
            Refresh();
        }

        ListIndex<CardEntry> MakeIndex(CardLibrary lib, int s)
        {
            var idx = new ListIndex<CardEntry>(lib.Entries.Where(e => !e.IsHs2n || e.HeaderSex < 0 || e.HeaderSex == s).ToList(), e => e.SearchText);
            idx.AddSort("name", (a, b) => ListSorts.CompareJa(a.DisplayName, b.DisplayName));
            idx.AddSort("date", (a, b) => a.ModifiedUtc.CompareTo(b.ModifiedUtc));
            idx.AddSort("file", (a, b) => ListSorts.CompareOrdinalIgnoreCase(a.FileName, b.FileName));
            idx.SetSort("name", false);
            idx.AddFacet("folder", e => CardLibrary.InFolder(e, folderKey) ? "in" : "out");
            return idx;
        }

        void ApplySort()
        {
            if (Index == null) return;
            Index.SetSort(sortField.value, descending);
            dirButton.text = descending ? "▼" : "▲";
            Loc.Tooltip(dirButton, descending ? "sort.descending" : "sort.ascending");
        }

        /// <summary>Refreshes the index and the view (after query / facet / sort / name changes).</summary>
        public ListChange Refresh()
        {
            var idx = Index;
            if (idx == null) return ListChange.Unchanged;
            var ch = idx.Refresh();
            if (listMode)
            {
                if (listView.itemsSource != idx.Indices) listView.itemsSource = idx.Indices;
                listView.RefreshItems();
            }
            else grid.SetItems(idx.Rows, idx.Indices);
            UpdateStatus();
            return ch;
        }

        void SetListMode(bool on)
        {
            listMode = on;
            grid.style.display = on ? DisplayStyle.None : DisplayStyle.Flex;
            listView.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            zoom.style.display = on ? DisplayStyle.None : DisplayStyle.Flex;
            gridButton.EnableInClassList("hs-seg--on", !on);
            listButton.EnableInClassList("hs-seg--on", on);
            Refresh();
        }

        double nextNameRefresh;

        /// <summary>One frame of work (the panel scheduler calls it; tests may pump it).</summary>
        public void Pump() => Tick();

        void Tick()
        {
            for (int s = 0; s < 2; s++)
            {
                var t = scans[s];
                if (t == null || !t.IsCompleted) continue;
                scans[s] = null;
                if (t.IsFaulted) { status.text = t.Exception?.GetBaseException().Message; continue; }
                libs[s] = t.Result;
                indexes[s] = MakeIndex(libs[s], s);
                indexes[s].SetQuery(queries[s]);
                foreach (var p in libs[s].Problems) Debug.LogWarning("[ui] chara browser: " + p);
                names.Enqueue(libs[s].Entries);
                if (s == sex) { ApplySort(); Show(); }
            }
            loader.Update(4);
            drained.Clear();
            if (names.Drain(drained, 2000) > 0 && Index != null)
            {
                // character names arrive in batches: re-key the rows (search text, name sort) at most twice a second
                pendingNames.AddRange(drained);
            }
            if (pendingNames.Count > 0 && Time.realtimeSinceStartupAsDouble >= nextNameRefresh)
            {
                nextNameRefresh = Time.realtimeSinceStartupAsDouble + 0.5;
                for (int s = 0; s < 2; s++)
                {
                    var idx = indexes[s];
                    if (idx == null) continue;
                    var pos = new List<int>();
                    var set = new HashSet<CardEntry>(pendingNames);
                    for (int i = 0; i < idx.Rows.Count; i++) if (set.Contains(idx.Rows[i])) pos.Add(i);
                    if (pos.Count > 0) idx.RowsChanged(pos);
                }
                pendingNames.Clear();
                var ch = Refresh();
                if (ch == ListChange.Unchanged) { if (listMode) listView.RefreshItems(); else grid.RefreshVisible(); }
            }
        }

        readonly List<CardEntry> pendingNames = new List<CardEntry>();

        void UpdateStatus()
        {
            var idx = Index;
            if (idx == null) return;
            string n = names.Pending > 0 ? "  " + L10n.Format("browser.indexing", names.Pending) : "";
            status.text = L10n.Format("search.results", idx.Indices.Count, idx.Rows.Count) + n;
        }

        void UpdateButtons()
        {
            var s = SelectedEntry;
            addButton.SetEnabled(s != null);
            var sel = ctx.Selection.Ids;
            bool allChara = sel.Count > 0 && sel.All(id => ctx.Model.Find(id)?.Kind == NodeKind.Chara);
            changeButton.SetEnabled(s != null && allChara);
            UpdateHint();
        }

        // ------------------------------------------------------------------ ops

        /// <summary>Adds a character card (root unless a drag said otherwise), selected afterwards (U14).</summary>
        public OpResult AddCard(CardEntry e, string parentId, string point)
        {
            PanelKit.ApplyPlacement(ctx);
            var opt = new CardAddOptions { UseCardState = ctx.Options.Bool(OptionDefs.AddUseCardState) };
            var r = ctx.Ops.AddChara(e.FullPath, parentId, point, -1, opt, UiContext.User());
            return ctx.ShowResult(r);
        }

        /// <summary>Change character for every selected character.</summary>
        public OpResult ChangeTo(CardEntry e)
        {
            var ids = ctx.SelectedOfKind(NodeKind.Chara);
            if (ids.Count == 0) return null;
            return ctx.ShowResult(ctx.Ops.ChangeCharacter(ids, e.FullPath, null, UiContext.User()));
        }

        /// <summary>Tests: switches the sex and waits for nothing (the scan runs; poll <see cref="Ready"/>).</summary>
        public void ShowSex(int s) => SetSex(s);
        public void UseListMode(bool on) => SetListMode(on);
    }
}
