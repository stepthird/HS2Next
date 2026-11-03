// The pose browser: folder tree + grid (PoseTools .png and
// .hs2npose thumbnails; .dat files without an image show a stick figure drawn from the pose, made on request by
// PoseStickThumbs as the loader's generator and cached on disk: the text tile while it is not there yet or when the pose
// cannot be drawn) or list, search by pose name and file name.
// Load applies the pose on the primary character (HS2 .dat / .png through StudioOps.LoadPose, .hs2npose through
// StudioOps.LoadPoseFragment; one entry "Load pose"); Save writes a .hs2npose of the primary (name prompt, the bridge's
// pose.save path: a file, not a document change). v1.2: the views «★ お気に入り» and «最近追加»
// above the folders (every folder's favourites; every file newest first by its creation time, whatever the sort), a star on a
// favourite's cell and row, a right-click menu on the cells and rows (and the menu key / Shift+F10: favourite, details,
// Explorer, copy the path, delete); Delete (the footer button and the menu) takes any file of the list, HS2's too, to the recycle
// bin only after a confirmation, and the entry leaves the list in place (order, scroll, the neighbour selected).
// The scan (4,399 files here) starts when the panel is first shown and runs off the main thread in two steps (PoseFiles):
// the folder walk with the pose index gives the list at once (file names until a header is known), the files the index does
// not know are read meanwhile and their names and sexes fill in a few times a second; a PNG shows once it is known to hold a
// pose. An optional sex filter is used by the character inspector's Pose tab (package I) through <see cref="SexFilter"/>.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    public sealed class PoseBrowser : VisualElement
    {
        public const string PanelId = "add.pose";

        readonly UiContext ctx;
        readonly ThumbnailLoader loader = new ThumbnailLoader();
        readonly PoseStickThumbs stick;
        bool stickRefresh;
        /// <summary>Main-thread milliseconds per frame for the thumbnails (the loader and the stick figures together).</summary>
        public const double ThumbBudgetMs = 4;
        /// <summary>Headers taken into the list per fill step at most (about 35 ms of main-thread time per 1,000 here): a warm
        /// disk cache reads the whole library within the first quarter second, and one step for every header took 150 ms.
        /// While more wait, the next step follows in the next frame.</summary>
        public const int FillBatch = 1000;
        bool backlog;
        readonly FolderTree folders;
        readonly CardGrid<PoseEntry> grid;
        readonly MultiColumnListView listView;
        readonly Label status;
        readonly Button loadButton, saveButton, deleteButton, gridButton, listButton;
        readonly FavoriteStore favorites;
        string view;
        Task<PoseFiles> scan;
        Task read;
        PoseFiles files;
        ListIndex<PoseEntry> index;
        string folderKey, query = "";
        bool listMode, started, readFinished;
        int? sexFilter;
        readonly System.Diagnostics.Stopwatch scanClock = new System.Diagnostics.Stopwatch();
        double lastFill;

        public PoseFiles Files => files;
        public ListIndex<PoseEntry> Index => index;
        /// <summary>The list is shown (names may still be filling in; see <see cref="Complete"/>).</summary>
        public bool Ready => index != null;
        /// <summary>Every header is known and shown.</summary>
        public bool Complete => index != null && readFinished && scan == null;
        /// <summary>Milliseconds from the start of the scan to the first list shown / to every header shown (-1 = not yet).</summary>
        public double FirstListMs { get; private set; } = -1;
        public double CompleteMs { get; private set; } = -1;
        /// <summary>The pose index file (null = the app's cache folder; tests set their own or "" for none).</summary>
        public string IndexFile;
        /// <summary>Main-thread milliseconds of the longest single fill step (index build or a batch of names).</summary>
        public double MaxStepMs { get; private set; }
        public CardGrid<PoseEntry> Grid => grid;
        /// <summary>The thumbnail loader and the stick-figure generator of the grid (tests, ui.state).</summary>
        public ThumbnailLoader Loader => loader;
        public PoseStickThumbs Stick => stick;
        /// <summary>The list view (list mode), the folder tree, the view shown (BrowserViews key or null = a folder / all).</summary>
        public MultiColumnListView List => listView;
        public FolderTree Folders => folders;
        public string View => view;
        /// <summary>The favourites among the listed files and the files of «最近追加» (the views' counts).</summary>
        public int FavoriteCount { get; private set; }
        public int RecentCount { get; private set; }
        /// <summary>The folder tree and the grid / list (side by side or stacked by the region's shape).</summary>
        public AdaptiveSplit Body { get; }

        /// <summary>Show only poses of a sex (0 / 1) or all (null).</summary>
        public int? SexFilter
        {
            get => sexFilter;
            set { sexFilter = value; if (index != null) { index.SetFacet("sex", value == null ? null : value.Value.ToString()); Refresh(); } }
        }

        public PoseBrowser(UiContext ctx)
        {
            this.ctx = ctx;
            favorites = Favorites.Of(ctx);
            name = "pose-browser"; PanelKit.Style(this);
            AddToClassList("hs-panel");
            AddToClassList("hs-browser");

            var header = new VisualElement();
            header.AddToClassList("hs-panel__header");
            var search = PanelKit.SearchField("pose-search", q => { query = q; index?.SetQuery(q); Refresh(); });
            search.AddToClassList("hs-grow");
            header.Add(search);
            gridButton = Loc.Button("browser.grid", () => SetListMode(false)); gridButton.name = "pose-grid-mode"; gridButton.AddToClassList("hs-seg");
            listButton = Loc.Button("browser.list", () => SetListMode(true)); listButton.name = "pose-list-mode"; listButton.AddToClassList("hs-seg");
            header.Add(gridButton);
            header.Add(listButton);
            Add(header);

            // folders | grid / list in a wide region, folders above the grid / list in a tall one (a sidebar)
            folders = new FolderTree("pose-folders", k => L10n.Get(k == PoseFiles.RootHs2 ? "browser.root.hs2" : "browser.root.hs2next"));
            folders.SetViews(BrowserViews.Favorites, BrowserViews.Recent);
            folders.Selected += k =>
            {
                view = BrowserViews.Is(k) ? k : null;
                folderKey = view != null ? null : k;
                if (index != null) { index.AddFacet("folder", e => PoseFiles.InFolder(e, folderKey) ? "in" : "out"); index.SetFacet("folder", folderKey == null ? null : "in"); ApplyView(); Refresh(); }
            };
            var right = new VisualElement();
            right.AddToClassList("hs-grow");
            // files with an image show it; the HS2 pose files without one get a stick figure (a generated key of the loader)
            stick = new PoseStickThumbs(() => ctx.App?.Catalog, () => ctx.App?.Db, () => ctx.App?.Catalog?.CacheDir);
            stick.Attach(loader);
            stick.Failed += _ => stickRefresh = true;   // the cell goes back to the text tile
            grid = new CardGrid<PoseEntry>("pose-grid", loader, e => e.HasImage ? e.FullPath : stick.KeyOf(e), e => IsFavorite(e) ? BrowserViews.Star + e.DisplayName : e.DisplayName,
                e => e.HasImage || stick.Draws(e), 1f);
            grid.AddToClassList("hs-grow");
            grid.HintKey = "browser.pose.hint";
            grid.Activated += e => ctx.ShowResult(Load(e));
            grid.SelectionChanged += _ => UpdateButtons();
            grid.DragPayloadOf = e => new ApplyPayload(e.DisplayName, NodeKind.Chara, id => Load(e, id));
            grid.ContextRequested += (e, at) => ShowMenu(e, at);
            right.Add(grid);
            listView = new MultiColumnListView
            {
                name = "pose-list", fixedItemHeight = 22, virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                selectionType = SelectionType.Single, showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly,
            };
            listView.AddToClassList("hs-grow");
            AddColumn("name", "browser.col.name", 240, e => e.DisplayName);
            AddColumn("file", "sort.file", 220, e => e.FileName);
            AddColumn("folder", "browser.col.folder", 220, e => e.FolderKey);
            PanelKit.EllipsizeHeaders(listView);   // a narrow sidebar cuts the column titles (ellipsis, the full title as tooltip)
            L10n.Default.LanguageChanged += _ => { foreach (var (col, key) in columnTitles) col.title = L10n.Get(key); PanelKit.FitHeaders(listView); };
            listView.itemsChosen += _ => ctx.ShowResult(Load(SelectedEntry));
            listView.selectionChanged += _ => UpdateButtons();
            listView.RegisterCallback<KeyDownEvent>(e =>
            {
                if (!ListMenus.IsMenuKey(e) || SelectedEntry == null) return;
                var row = listView.GetRootElementForIndex(listView.selectedIndex);
                var wb = row != null ? row.worldBound : listView.worldBound;
                ShowMenu(SelectedEntry, new Vector2(wb.xMin + 24, row != null ? wb.yMax : wb.yMin + 24));
                e.StopPropagation();
            });
            right.Add(listView);
            var body = new AdaptiveSplit("pose-body", folders, right, 200, 0.25f, 110);
            body.AddToClassList("hs-grow");
            Body = body;
            Add(body);

            var footer = new VisualElement { name = "pose-footer" };
            footer.AddToClassList("hs-panel__footer");
            loadButton = Loc.Button("browser.loadPose", () => ctx.ShowResult(Load(SelectedEntry))); loadButton.name = "pose-load";
            saveButton = Loc.Button("browser.savePose", SavePrompt); saveButton.name = "pose-save";
            deleteButton = Loc.Button("browser.delete", DeleteSelected); deleteButton.name = "pose-delete";
            status = new Label { name = "pose-status" };
            status.AddToClassList("hs-grow");
            status.AddToClassList("hs-status-text");
            PanelKit.StatusLine(status);   // a narrow sidebar puts the status on a line of its own, where it wraps
            footer.Add(loadButton); footer.Add(saveButton); footer.Add(deleteButton); footer.Add(status);
            Add(footer);

            ctx.SelectionChanged += _ => UpdateButtons();
            Favorites.Changed += OnFavoritesChanged;
            BrowserFiles.Deleted += OnDeleted;
            RegisterCallback<GeometryChangedEvent>(_ => StartScan());
            PanelKit.EveryFrame(this, Tick);
            SetListMode(false);
            UpdateButtons();
        }

        // the list's column titles follow the UI language
        readonly List<(Column column, string key)> columnTitles = new List<(Column, string)>();

        void AddColumn(string id, string titleKey, float w, Func<PoseEntry, string> text)
        {
            // every column shrinks in proportion down to its minimum, as in the list browsers (with only the name column
            // stretchable, a narrow sidebar left it 34 px while the file column kept 218)
            var c = new Column { name = id, title = L10n.Get(titleKey), width = w, minWidth = id == "name" ? 140 : 64, stretchable = true };
            columnTitles.Add((c, titleKey));
            c.makeCell = () =>
            {
                var l = new Label();
                l.AddToClassList("hs-list-cell");
                DragController.MakeSource(l, _ => l.userData is PoseEntry e ? new ApplyPayload(e.DisplayName, NodeKind.Chara, id2 => Load(e, id2)) : null);
                // a right click selects the row; its menu opens on the release
                l.RegisterCallback<PointerDownEvent>(ev => { if (ev.button == 1 && l.userData is PoseEntry e) { SelectInList(e); ev.StopPropagation(); } });
                l.RegisterCallback<PointerUpEvent>(ev => { if (ev.button == 1 && l.userData is PoseEntry e) { ShowMenu(e, ev.position); ev.StopPropagation(); } });
                return l;
            };
            c.bindCell = (el, i) =>
            {
                var e = index.Rows[index.Indices[i]];
                var l = (Label)el;
                l.text = id == "name" && IsFavorite(e) ? BrowserViews.Star + text(e) : text(e);
                l.tooltip = id == "name" && view == BrowserViews.Recent ? L10n.Format("browser.added", Added(e.CreatedUtc)) : null;
                el.userData = e;
            };
            listView.columns.Add(c);
        }

        /// <summary>The folders scanned (null = the app's HS2 pose folder and &lt;UserData&gt;/pose; tests narrow them).</summary>
        public string Hs2Dir, OwnDir;
        public bool UseAppFolders = true;

        /// <summary>Starts the folder scan (first show; tests call it directly).</summary>
        public void StartScan()
        {
            if (started) return;
            started = true;
            readFinished = false;
            backlog = false;
            string hs2 = UseAppFolders ? ctx.App.Paths?.Hs2PoseDir : Hs2Dir, own = UseAppFolders ? ctx.App.Paths?.PoseDir : OwnDir;
            string idx = IndexFile ?? PoseFiles.IndexPath(ctx.App?.Catalog?.CacheDir);
            if (idx == "") idx = null;
            scanClock.Restart();
            FirstListMs = CompleteMs = -1;
            MaxStepMs = MaxTakeMs = MaxFolderMs = MaxRefreshMs = 0;
            FillSteps = 0;
            scan = Task.Run(() => PoseFiles.List(hs2, own, idx));
            status.text = L10n.Get("browser.scanning");
        }

        /// <summary>Scans again (after a save or delete): the index makes it a folder walk.</summary>
        public void Rescan()
        {
            started = false;
            StartScan();
        }

        /// <summary>One frame of work (the panel scheduler calls it; tests may pump it).</summary>
        public void Pump() => Tick();

        void Tick()
        {
            var frame = System.Diagnostics.Stopwatch.StartNew();
            loader.Update(ThumbBudgetMs);
            stick.Pump(Math.Max(0, ThumbBudgetMs - frame.Elapsed.TotalMilliseconds));
            if (stickRefresh) { stickRefresh = false; grid.RefreshVisible(); }
            if (scan != null)
            {
                if (!scan.IsCompleted) return;   // a rescan: the old list stays until the new one is there
                ShowList();
            }
            if (files == null || index == null || readFinished) return;
            // names read since the last fill, four times a second (each fill refreshes the grid once)
            double now = scanClock.Elapsed.TotalMilliseconds;
            bool done = files.ReadDone && (read == null || read.IsCompleted);
            if (!done && !backlog && now - lastFill < 250) return;
            if (!files.HasResults && !done) return;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var changed = files.TakeResults(FillBatch);
            backlog = changed.Count >= FillBatch;
            if (changed.Count > 0) index.RowsChanged(changed);
            double take = sw.Elapsed.TotalMilliseconds;
            if (done && !files.HasResults)
            {
                readFinished = true;
                var before = new List<string>(files.Folders);
                files.Recount();
                // a folder of PNG poses appears once they are read; otherwise the tree keeps its expansion and selection
                if (!before.SequenceEqual(files.Folders))
                {
                    string keep = folders.SelectedKey;
                    folders.SetFolders(files.Folders, files.ListedFolderKeys());
                    if (keep != null) folders.Select(keep);
                }
                else folders.SetCounts(files.ListedFolderKeys());
                CompleteMs = scanClock.Elapsed.TotalMilliseconds;
            }
            double fold = sw.Elapsed.TotalMilliseconds;
            lastFill = now;
            Refresh();
            double all = sw.Elapsed.TotalMilliseconds;
            MaxStepMs = Math.Max(MaxStepMs, all);
            MaxTakeMs = Math.Max(MaxTakeMs, take);
            MaxFolderMs = Math.Max(MaxFolderMs, fold - take);
            MaxRefreshMs = Math.Max(MaxRefreshMs, all - fold);
            FillSteps++;
        }

        /// <summary>Parts of the longest fill steps (taking the names, the folder tree, the list refresh), ms, and the number of
        /// fill steps (tests).</summary>
        public double MaxTakeMs { get; private set; }
        public double MaxFolderMs { get; private set; }
        public double MaxRefreshMs { get; private set; }
        public int FillSteps { get; private set; }

        void ShowList()
        {
            var t = scan;
            scan = null;
            if (t.IsFaulted) { status.text = t.Exception?.GetBaseException().Message; return; }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            files = t.Result;
            index = new ListIndex<PoseEntry>(files.Entries, e => e.SearchText);
            index.AddSort("name", (a, b) => ListSorts.CompareJa(a.DisplayName, b.DisplayName));
            index.AddSort("created", (a, b) => a.CreatedUtc.CompareTo(b.CreatedUtc));
            index.AddFacet("fav", e => IsFavorite(e) ? "y" : "n");
            index.AddFacet("listed", e => e.Listed ? "y" : "n");
            index.AddFacet("folder", e => PoseFiles.InFolder(e, folderKey) ? "in" : "out");
            index.AddFacet("sex", e => e.Sex.ToString());
            index.SetFacet("listed", "y");
            if (folderKey != null) index.SetFacet("folder", "in");
            if (sexFilter != null) index.SetFacet("sex", sexFilter.Value.ToString());
            ApplyView();
            index.SetQuery(query);
            folders.SetFolders(files.Folders, files.ListedFolderKeys());
            readFinished = files.ReadDone;
            if (!readFinished)
            {
                var f = files;
                read = Task.Run(() => f.Read());
            }
            lastFill = scanClock.Elapsed.TotalMilliseconds;
            Refresh();
            FirstListMs = scanClock.Elapsed.TotalMilliseconds;
            if (readFinished) CompleteMs = FirstListMs;
            MaxStepMs = Math.Max(MaxStepMs, sw.Elapsed.TotalMilliseconds);
        }

        public void Refresh()
        {
            if (index == null) return;
            index.Refresh();
            if (listMode) { if (listView.itemsSource != index.Indices) listView.itemsSource = index.Indices; listView.RefreshItems(); }
            else grid.SetItems(index.Rows, index.Indices);
            int listed = files?.ListedCount ?? index.Rows.Count;
            status.text = !readFinished && files != null ? L10n.Format("browser.pose.reading", index.Indices.Count, listed, files.Unread)
                : view == BrowserViews.Recent ? L10n.Format("browser.view.recent.status", index.Indices.Count, listed)
                : L10n.Format("search.results", index.Indices.Count, listed);
            CountViews(listed);
            UpdateButtons();
        }

        // ------------------------------------------------------------------ views, favourites, menu (v1.2)

        bool IsFavorite(PoseEntry e) => favorites.FilesOf(e.IsHs2n ? "hs2next.pose" : "hs2.pose").Count > 0 && BrowserFiles.IsFavorite(favorites, e);

        static string Added(DateTime utc) => utc.Year < 1980 ? "-" : utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>The view's facet and order: «★» = the favourites of every folder; «最近追加» = every file, newest first by its
        /// creation time (ties in folder order) whatever the sort; else the folder and the source order.</summary>
        void ApplyView()
        {
            if (index == null) return;
            index.SetFacet("fav", view == BrowserViews.Favorites ? "y" : null);
            index.SetSort(view == BrowserViews.Recent ? "created" : null, view == BrowserViews.Recent);
        }

        void CountViews(int listed)
        {
            int fav = 0;
            if (files != null && (favorites.FilesOf("hs2.pose").Count > 0 || favorites.FilesOf("hs2next.pose").Count > 0))
                foreach (var e in files.Entries) if (e.Listed && BrowserFiles.IsFavorite(favorites, e)) fav++;
            FavoriteCount = fav;
            RecentCount = listed;
            folders.SetViewCounts(fav, listed);
        }

        /// <summary>Shows a view (BrowserViews key) or a folder (null = all) as a click in the folder tree does (tests).</summary>
        public void ShowView(string key) => folders.Select(key);

        void OnFavoritesChanged(string what)
        {
            if (index == null || what != null && what != "hs2.pose" && what != "hs2next.pose") return;
            index.AddFacet("fav", e => IsFavorite(e) ? "y" : "n");   // the values changed: the facet is evaluated again
            ApplyView();
            Refresh();
            if (listMode) listView.RefreshItems(); else grid.RefreshVisible();
        }

        void SelectInList(PoseEntry e)
        {
            if (index == null) return;
            int row = files.Entries.IndexOf(e), pos = row < 0 ? -1 : index.Indices.IndexOf(row);
            if (pos >= 0 && listView.selectedIndex != pos) listView.SetSelection(pos);
        }

        /// <summary>The context menu of a pose file at a panel position.</summary>
        public void ShowMenu(PoseEntry e, Vector2 at)
        {
            if (e == null) return;
            ListMenus.ShowFile(ctx, BrowserFiles.Of(e), at, "pose-context");
        }

        /// <summary>A file went to the recycle bin: its entry leaves the list in place (order and scroll kept); the neighbour
        /// (the entry that takes its place, else the one before) is selected when it was selected.</summary>
        void OnDeleted(string full)
        {
            if (files == null || index == null) return;
            PoseEntry e = null;
            foreach (var x in files.Entries) if (!x.Removed && string.Equals(x.FullPath, full, StringComparison.OrdinalIgnoreCase)) { e = x; break; }
            if (e == null) return;
            int row = files.Entries.IndexOf(e);
            int was = index.Indices.IndexOf(row);
            bool selected = ReferenceEquals(SelectedEntry, e);
            var sv = (listMode ? (VisualElement)listView : grid.List).Q<ScrollView>();
            var offset = sv?.scrollOffset ?? Vector2.zero;
            files.Remove(e);
            index.RowsChanged(new[] { row });
            loader.Forget(e.FullPath);
            loader.Forget(PoseStickThumbs.KeyPrefix + e.FullPath);
            stick.Forget(e.FullPath);
            folders.SetCounts(files.ListedFolderKeys());
            Refresh();
            if (sv != null) sv.scrollOffset = offset;
            int n = index.Indices.Count;
            if (selected && was >= 0 && n > 0)
            {
                int pos = Math.Min(was, n - 1);
                if (listMode) listView.SetSelection(pos); else grid.Select(pos);
            }
            else if (selected && listMode) listView.ClearSelection();
            UpdateButtons();
        }

        /// <summary>The browser's numbers for ui.state (null before the first show).</summary>
        public static Dictionary<string, object> UiState()
        {
            var b = AddModule.Poses;
            if (b == null || !b.started) return null;
            var f = b.files;
            return new Dictionary<string, object>
            {
                ["ready"] = b.Ready, ["complete"] = b.Complete,
                ["firstListMs"] = Math.Round(b.FirstListMs, 1), ["completeMs"] = Math.Round(b.CompleteMs, 1), ["maxStepMs"] = Math.Round(b.MaxStepMs, 2), ["fillSteps"] = b.FillSteps,
                ["listMs"] = f != null ? Math.Round(f.ListMs, 1) : (double?)null, ["entries"] = f?.Entries.Count, ["listed"] = f?.ListedCount,
                ["shown"] = b.index?.Indices.Count, ["unread"] = f?.Unread, ["indexHits"] = f?.IndexHits, ["filesRead"] = f?.FilesRead,
                ["indexWritten"] = f?.IndexWritten, ["folders"] = f?.Folders.Count, ["notAPose"] = f?.NotAPose, ["errors"] = f?.Errors,
                ["view"] = b.view == null ? null : b.view == BrowserViews.Favorites ? "favorites" : "recent", ["favorites"] = b.FavoriteCount,
                ["stick"] = b.StickState(),
            };
        }

        /// <summary>The stick figures' numbers: the generator's and the loader's (figures taken in, read from the disk cache,
        /// withdrawn) and the loader's bytes.</summary>
        public Dictionary<string, object> StickState()
        {
            var d = stick.State();
            d["provided"] = loader.GeneratedCount;
            d["fromCache"] = loader.GeneratedFromCache;
            d["withdrawn"] = loader.Withdrawn;
            d["loaderBytes"] = loader.CurrentBytes;
            return d;
        }

        /// <summary>Grid (false) or list (true) mode, as the header's buttons switch it (tests).</summary>
        public void UseListMode(bool on) => SetListMode(on);

        void SetListMode(bool on)
        {
            listMode = on;
            grid.style.display = on ? DisplayStyle.None : DisplayStyle.Flex;
            listView.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            gridButton.EnableInClassList("hs-seg--on", !on);
            listButton.EnableInClassList("hs-seg--on", on);
            Refresh();
        }

        public PoseEntry SelectedEntry => listMode
            ? (index != null && listView.selectedIndex >= 0 && listView.selectedIndex < index.Indices.Count ? index.Rows[index.Indices[listView.selectedIndex]] : null)
            : grid.Selected;

        string PrimaryChara => ctx.Model.Find(ctx.Selection.Primary)?.Kind == NodeKind.Chara ? ctx.Selection.Primary : null;

        void UpdateButtons()
        {
            var s = SelectedEntry;
            loadButton.SetEnabled(s != null && PrimaryChara != null);
            saveButton.SetEnabled(PrimaryChara != null);
            deleteButton.SetEnabled(s != null && s.CanDelete);
        }

        /// <summary>Loads a pose on a character (default: the primary).</summary>
        public OpResult Load(PoseEntry e, string charaId = null)
        {
            if (e == null) return null;
            charaId = charaId ?? PrimaryChara;
            if (charaId == null) return OpResult.Fail(CommandCodes.BadArgs, L10n.Get("browser.needChara"), "key");
            if (e.IsHs2n)
            {
                var r = ctx.Ops.LoadPoseFragment(charaId, e.FullPath, null, UiContext.User());
                if (r.Value is PoseFragmentLoadResult fr && fr.Error != null) return OpResult.Fail(fr.Code ?? CommandCodes.BadArgs, fr.Error, "path");
                return r;
            }
            var r2 = ctx.Ops.LoadPose(charaId, e.FullPath, null, UiContext.User());
            if (r2.Value is PoseImportReport rep && rep.Error != null) return OpResult.Fail(CommandCodes.BadArgs, rep.Error, "path");
            return r2;
        }

        void SavePrompt()
        {
            string id = PrimaryChara;
            if (id == null) return;
            ctx.Dialogs.Prompt("browser.savePoseTitle", ctx.Model.Find(id)?.Label ?? "pose", name =>
            {
                if (string.IsNullOrWhiteSpace(name)) return;
                var res = Save(id, name.Trim(), false);
                if (res == "exists")
                    ctx.Dialogs.Confirm("dialog.overwrite.title", "dialog.overwrite.message", ok => { if (ok) Report(Save(id, name.Trim(), true)); }, name.Trim());
                else Report(res);
            });
        }

        void Report(string res)
        {
            if (res == null) { ctx.Toasts?.Show(L10n.Get("browser.poseSaved"), ToastKind.Success); Rescan(); }
            else ctx.Toasts?.Show(L10n.Format("msg.saveFailed", res), ToastKind.Error);
        }

        /// <summary>
        /// Saves the character's pose as &lt;UserData&gt;/pose/&lt;name&gt;.hs2npose with PoseFragmentOps.Save (what the bridge's
        /// pose.save calls; a file write, not a document change, no history entry). Returns null on success, "exists", or the
        /// error message.
        /// </summary>
        public string Save(string charaId, string name, bool overwrite)
        {
            string file = string.Concat(name.Split(Path.GetInvalidFileNameChars())).Trim();
            if (file.Length == 0 || file == "." || file == "..") return "name";
            string dir = ctx.App.Paths?.PoseDir;
            if (dir == null) return "no pose folder";
            string full = Path.Combine(dir, file + HS2Next.Save.SaveFormats.PoseExtension);
            if (File.Exists(full) && !overwrite) return "exists";
            try
            {
                Directory.CreateDirectory(dir);
                ctx.Runtime.ApplyNow();
                var res = PoseFragmentOps.Save(ctx.Runtime, charaId, full, name, null, overwrite);
                return res.Ok ? null : (res.Error ?? res.Code ?? "save failed");
            }
            catch (Exception e) { return e.Message; }
        }

        /// <summary>The footer's Delete: the confirmation of the shared delete (the recycle bin only; HS2's files too).</summary>
        void DeleteSelected()
        {
            var e = SelectedEntry;
            if (e == null || !e.CanDelete) return;
            ListMenus.AskDelete(ctx, BrowserFiles.Of(e));
        }
    }
}
