// The file browser of one sex shared by the Edit tab's coordinate dialog, Studio's Costume tab and (Phase 4.6) the File page's
// character load dialog; the kind decides the folders:
//   Coordinate: the HS2 install's `UserData/coordinate/<female|male>` (*.png, recursively) and HS2Next's
//     `<UserData>/coordinate/<female|male>` (*.hs2ncoord, recursively);
//   Character: the HS2 install's `UserData/chara/<female|male>` (*.png, recursively) and HS2Next's `<UserData>/chara`
//     (*.hs2nchara, recursively) — only the files whose manifest names that sex (a file whose sex cannot be read stays listed).
// A search over the file names, sort by name (Japanese order) or date with a direction button, a refresh button, the 3.5
// thumbnail grid (cells `coord-cell-<file name>` / `chara-file-<file name>`; HS2 cards and HS2Next files carry their own
// image, which is drawn and counted, never inspected) and a status line. The file lists are scanned off the main thread once
// per kind, sex and app run; the refresh button, and a save into one of the folders (Invalidate), scan again and every open
// browser of that kind and sex shows the new list. CoordinateDialog wraps the coordinate kind in a modal dialog with HS2's
// three load modes «服読込み» (clothes) / «装飾読込み» (accessories) / «全て読込み» (all) and Cancel; a mode button without a
// chosen card keeps the dialog open with a note.
// v1.2: the view state (sort, direction, search, «★» filter, the scroll and, for the
// Costume tab, the chosen file) is kept per kind and sex for the app run, so a browser built again — the Costume tab of a
// rebuilt inspector (owner item 6), a dialog opened again — shows the list as it was; favourites (a star on the cell, the «★» filter button); a
// right-click menu on the cells (and the menu key / Shift+F10: favourite, details, Explorer, copy the path, delete to the
// recycle bin — in a dialog the menu and its own dialogs stand above it); a deleted file leaves every browser in place.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    /// <summary>What a <see cref="CoordinateBrowser"/> lists.</summary>
    public enum FileBrowserKind { Coordinate, Character }

    public sealed class CoordinateBrowser : VisualElement
    {
        public const string UssClass = "hs-coord-browser";
        // one loader for every browser grid (cards 252 × 352 after the 3.5 shrink; 40 MB LRU)
        static readonly ThumbnailLoader loader = new ThumbnailLoader { CapBytes = 40L * 1024 * 1024 };
        // the scanned files per kind and sex ([kind * 2 + sex], sex 0 male, 1 female), kept while the app runs
        static readonly CardLibrary[] libraries = new CardLibrary[4];
        static readonly Task<CardLibrary>[] scans = new Task<CardLibrary>[4];

        /// <summary>The view state of one kind and sex, kept for the app run (v1.2: a rebuilt browser shows the list as it was).</summary>
        public sealed class ViewState
        {
            public string Sort = "name";
            public bool Descending, FavoritesOnly;
            public string Query = "";
            /// <summary>The chosen file (full path) and the grid's scroll (px), its first shown position and columns then.</summary>
            public string Chosen;
            public float ScrollY;
            public int FirstPos, Columns;
        }

        static readonly ViewState[] states = { new ViewState(), new ViewState(), new ViewState(), new ViewState() };

        /// <summary>The view state of a kind and sex (tests).</summary>
        public static ViewState StateOf(FileBrowserKind kind, int sex) => states[SlotOf(kind, sex)];

        static CoordinateBrowser()
        {
            // a file that went to the recycle bin leaves every kept list (the open browsers refresh themselves)
            BrowserFiles.Deleted += full =>
            {
                foreach (var lib in libraries) lib?.Entries.RemoveAll(e => string.Equals(e.FullPath, full, StringComparison.OrdinalIgnoreCase));
                loader.Forget(full);
            };
        }

        readonly UiContext ctx;
        readonly int sex;
        readonly FileBrowserKind kind;
        readonly CardGrid<CardEntry> grid;
        readonly TextField search;
        readonly DropdownField sortField;
        readonly Button dirButton, favButton;
        readonly Label status;
        readonly FavoriteStore favorites;
        readonly ViewState state;
        ListIndex<CardEntry> index;
        CardLibrary shownLibrary;
        bool descending;
        // the scroll to restore once the grid is laid out (-1 = none), and the frames tried
        float restoreScroll = -1;
        int restoreTries, restoreWatch;
        string chosenPath;
        readonly bool keepChoice;
        readonly List<CardEntry> shownOrder = new List<CardEntry>();

        /// <summary>The sex whose files are listed (0 male, 1 female).</summary>
        public int Sex => sex;
        /// <summary>Coordinates or characters.</summary>
        public FileBrowserKind Kind => kind;
        public CardGrid<CardEntry> Grid => grid;
        public ListIndex<CardEntry> Index => index;
        /// <summary>True once the files of the sex are listed.</summary>
        public bool Ready => index != null;
        /// <summary>True once the kept scroll was put back (or there was none).</summary>
        public bool ScrollRestored => restoreScroll < 0;
        /// <summary>The chosen file (null = none).</summary>
        public CardEntry Selected => grid.Selected;
        /// <summary>Double click / Enter on a cell.</summary>
        public event Action<CardEntry> Activated;
        public event Action<CardEntry> SelectionChanged;
        /// <summary>A new file list is shown (after the first scan and after a rescan).</summary>
        public event Action Listed;
        public static ThumbnailLoader Loader => loader;
        /// <summary>The grid's scroll view (tests).</summary>
        public ScrollView Scroll => grid.List.Q<ScrollView>();

        int Slot => SlotOf(kind, sex);
        static int SlotOf(FileBrowserKind k, int s) => (int)k * 2 + (s == 0 ? 0 : 1);
        BrowserFileKind FileKind => kind == FileBrowserKind.Coordinate ? BrowserFileKind.Coordinate : BrowserFileKind.Chara;

        /// <param name="keepChoice">Choose the kept file again when built (the Costume tab); a dialog starts with none chosen.</param>
        public CoordinateBrowser(UiContext ctx, int sex, string elementName = "coord-browser", FileBrowserKind kind = FileBrowserKind.Coordinate, bool keepChoice = false)
        {
            this.keepChoice = keepChoice;
            this.ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            this.sex = sex == 0 ? 0 : 1;
            this.kind = kind;
            favorites = Favorites.Of(ctx);
            state = states[Slot];
            descending = state.Descending;
            string p = kind == FileBrowserKind.Coordinate ? "coord" : "chara-load";
            name = elementName;
            AddToClassList(UssClass);
            var header = new VisualElement { name = p + "-header" };
            header.AddToClassList("hs-coord-browser__header");
            search = PanelKit.SearchField(p + "-search", q => { state.Query = q ?? ""; index?.SetQuery(q ?? ""); Refresh(); });
            search.SetValueWithoutNotify(state.Query ?? "");
            search.AddToClassList("hs-grow");
            header.Add(search);
            sortField = new DropdownField { name = p + "-sort", choices = new List<string> { "name", "date" }, index = state.Sort == "date" ? 1 : 0 };
            sortField.formatListItemCallback = s => L10n.Get("sort." + s);
            sortField.formatSelectedValueCallback = s => L10n.Get("sort." + s);
            sortField.RegisterValueChangedCallback(e => { state.Sort = e.newValue; ApplySort(); Refresh(); });
            header.Add(sortField);
            dirButton = new Button(() => { descending = !descending; state.Descending = descending; ApplySort(); Refresh(); }) { name = p + "-sort-dir", text = descending ? "▼" : "▲" };
            dirButton.AddToClassList("hs-icon-button");
            Loc.Tooltip(dirButton, descending ? "sort.descending" : "sort.ascending");
            header.Add(dirButton);
            favButton = new Button(() => { state.FavoritesOnly = !state.FavoritesOnly; ApplyFavFilter(); Refresh(); }) { name = p + "-fav-only", text = "★" };
            favButton.AddToClassList("hs-icon-button");
            favButton.AddToClassList("hs-seg");
            Loc.Tooltip(favButton, "browser.favOnly");
            header.Add(favButton);
            var refresh = new Button(() => Rescan()) { name = p + "-refresh", text = "↻" };
            refresh.AddToClassList("hs-icon-button");
            Loc.Tooltip(refresh, kind == FileBrowserKind.Coordinate ? "maker.coord.refresh" : "inspector.charaEdit.file.refresh");
            header.Add(refresh);
            Add(header);
            string cell = kind == FileBrowserKind.Coordinate ? "coord-cell-" : "chara-file-";
            grid = new CardGrid<CardEntry>(p + "-grid", loader, e => e.FullPath, e => IsFavorite(e) ? BrowserViews.Star + e.FileName : e.FileName, _ => true)
            {
                CellName = (e, pos) => cell + e.FileName,
                Tooltip = e => e.FileName + "\n" + e.FolderKey,
            };
            grid.AddToClassList("hs-coord-browser__grid");
            grid.Activated += e => Activated?.Invoke(e);
            grid.SelectionChanged += e => { chosenPath = e?.FullPath; if (e != null) state.Chosen = e.FullPath; SelectionChanged?.Invoke(e); };
            grid.ContextRequested += (e, at) => ShowMenu(e, at);
            Add(grid);
            status = new Label { name = p + "-status" };
            status.AddToClassList("hs-status-text");
            Add(status);
            if (state.ScrollY > 0.5f) restoreScroll = state.ScrollY;
            var sv = Scroll;
            if (sv != null) sv.verticalScroller.valueChanged += OnScrolled;
            Action<string> onFav = OnFavoritesChanged;
            Action<string> onDel = OnDeleted;
            RegisterCallback<AttachToPanelEvent>(_ => { Favorites.Changed -= onFav; Favorites.Changed += onFav; BrowserFiles.Deleted -= onDel; BrowserFiles.Deleted += onDel; });
            RegisterCallback<DetachFromPanelEvent>(_ => { Favorites.Changed -= onFav; BrowserFiles.Deleted -= onDel; });
            UpdateFavButton();
            PanelKit.EveryFrame(this, Tick);
            StartScan(false);
            Tick();
        }

        /// <summary>The HS2 install's coordinate folder of a sex (read only), or null without an install.</summary>
        public static string Hs2Dir(UiContext ctx, int sex)
        {
            string ud = ctx?.App?.Paths?.Hs2UserData;
            return ud == null ? null : Path.Combine(ud, "coordinate", sex == 0 ? "male" : "female");
        }

        /// <summary>HS2Next's coordinate folder of a sex.</summary>
        public static string OwnDir(UiContext ctx, int sex)
        {
            string cd = ctx?.App?.Paths?.CoordDir;
            return cd == null ? null : Path.Combine(cd, sex == 0 ? "male" : "female");
        }

        /// <summary>The HS2 install's folder of a kind and sex (coordinate cards, character cards), or null without an install.</summary>
        public static string Hs2DirOf(UiContext ctx, FileBrowserKind kind, int sex) =>
            kind == FileBrowserKind.Coordinate ? Hs2Dir(ctx, sex) : ctx?.App?.Paths?.Hs2CharaDir(sex == 0 ? 0 : 1);

        /// <summary>HS2Next's folder of a kind (the coordinate folder of the sex; the one character folder of both sexes).</summary>
        public static string OwnDirOf(UiContext ctx, FileBrowserKind kind, int sex) =>
            kind == FileBrowserKind.Coordinate ? OwnDir(ctx, sex) : ctx?.App?.Paths?.CharaDir;

        /// <summary>Tests: the folders a kind and sex lists instead of the app's (hs2, own; null = the app's). Takes effect at the
        /// next scan (Invalidate / the refresh button).</summary>
        public static readonly Dictionary<int, (string hs2, string own)> TestFolders = new Dictionary<int, (string, string)>();

        /// <summary>Tests: the slot of a kind and sex for <see cref="TestFolders"/>.</summary>
        public static int SlotFor(FileBrowserKind kind, int sex) => SlotOf(kind, sex);

        static Task<CardLibrary> ScanTask(UiContext ctx, FileBrowserKind kind, int sex)
        {
            string hs2 = Hs2DirOf(ctx, kind, sex), own = OwnDirOf(ctx, kind, sex);
            if (TestFolders.TryGetValue(SlotOf(kind, sex), out var t)) { hs2 = t.hs2; own = t.own; }
            int s = sex == 0 ? 0 : 1;
            return kind == FileBrowserKind.Coordinate ? Task.Run(() => ScanCoordinates(hs2, own)) : Task.Run(() => ScanCharacters(hs2, own, s));
        }

        void StartScan(bool force)
        {
            if (!force && (libraries[Slot] != null || scans[Slot] != null)) return;
            scans[Slot] = ScanTask(ctx, kind, sex);
            status.text = L10n.Get("browser.scanning");
        }

        /// <summary>Lists the files again (the refresh button).</summary>
        public void Rescan()
        {
            libraries[Slot] = null;
            StartScan(true);
        }

        /// <summary>A file of <paramref name="kind"/> and <paramref name="sex"/> was written: the list is scanned again now, and every
        /// open browser of that kind and sex shows the new list when the scan ends (the next browser opened waits for it).</summary>
        public static void Invalidate(UiContext ctx, FileBrowserKind kind, int sex)
        {
            if (ctx == null) return;
            int i = SlotOf(kind, sex);
            libraries[i] = null;
            scans[i] = ScanTask(ctx, kind, sex);
        }

        /// <summary>The HS2 coordinate cards (*.png) and HS2Next's coordinate files (*.hs2ncoord) under the two folders.</summary>
        static CardLibrary ScanCoordinates(string hs2Dir, string ownDir)
        {
            var lib = CardLibrary.Scan(hs2Dir, null);
            if (!string.IsNullOrEmpty(ownDir) && Directory.Exists(ownDir))
            {
                string full = Path.GetFullPath(ownDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                lib.Roots.Add((CardLibrary.RootHs2Next, full));
                try
                {
                    foreach (var f in Directory.GetFiles(full, "*" + SaveFormats.CoordExtension, SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
                    {
                        if (!f.EndsWith(SaveFormats.CoordExtension, StringComparison.OrdinalIgnoreCase)) continue;
                        var fi = new FileInfo(f);
                        string rel = Path.GetDirectoryName(f).Substring(full.Length).TrimStart('\\', '/').Replace('\\', '/');
                        lib.Entries.Add(new CardEntry
                        {
                            FullPath = f, FileName = Path.GetFileNameWithoutExtension(f), Root = CardLibrary.RootHs2Next, Folder = rel,
                            Size = fi.Length, ModifiedUtc = fi.LastWriteTimeUtc, CreatedUtc = fi.CreationTimeUtc, IsHs2n = true,
                        });
                    }
                }
                catch (Exception e) { lib.Problems.Add(full + ": " + e.Message); }
            }
            return lib;
        }

        /// <summary>The HS2 character cards of the sex's folder (*.png) and HS2Next's character files (*.hs2nchara) whose manifest
        /// names <paramref name="sex"/> (the manifest is read here, off the main thread; an unreadable sex keeps the file).</summary>
        static CardLibrary ScanCharacters(string hs2Dir, string ownDir, int sex)
        {
            var lib = CardLibrary.Scan(hs2Dir, ownDir);
            lib.Entries.RemoveAll(e =>
            {
                if (!e.IsHs2n) return false;
                var info = CardHeaderReader.Read(e.FullPath);
                e.HeaderSex = info.Sex;
                e.CharaName = info.Name;
                e.Unreadable = info.Name == null;
                return info.Sex >= 0 && info.Sex != sex;
            });
            return lib;
        }

        void Tick()
        {
            var t = scans[Slot];
            if (t != null && t.IsCompleted)
            {
                scans[Slot] = null;
                if (t.IsFaulted) status.text = t.Exception?.GetBaseException().Message;
                else
                {
                    libraries[Slot] = t.Result;
                    foreach (var p in t.Result.Problems) UnityEngine.Debug.LogWarning("[ui] " + (kind == FileBrowserKind.Coordinate ? "coordinate" : "character file") + " browser: " + p);
                }
            }
            var lib = libraries[Slot];
            if (lib != null && !ReferenceEquals(lib, shownLibrary)) Show(lib);
            loader.Update(4);
            if (restoreScroll >= 0) TryRestoreScroll();
        }

        void Show(CardLibrary lib)
        {
            // a rescan keeps the chosen file when it is still listed; a browser built again takes the kept one (keepChoice)
            string chosen = grid.Selected?.FullPath ?? (keepChoice ? state.Chosen : null);
            shownLibrary = lib;
            index = new ListIndex<CardEntry>(lib.Entries, e => e.FileName);
            index.AddSort("name", (a, b) => ListSorts.CompareJa(a.FileName, b.FileName));
            index.AddSort("date", (a, b) => a.ModifiedUtc.CompareTo(b.ModifiedUtc));
            index.AddFacet("fav", e => IsFavorite(e) ? "y" : "n");
            index.SetQuery(search.value ?? "");
            ApplySort();
            ApplyFavFilter();
            Refresh();
            if (chosen != null) SelectPath(chosen, restoreScroll < 0);
            Listed?.Invoke();
        }

        void ApplySort()
        {
            if (index == null) return;
            index.SetSort(sortField.value ?? "name", descending);
            dirButton.text = descending ? "▼" : "▲";
            Loc.Tooltip(dirButton, descending ? "sort.descending" : "sort.ascending");
        }

        void ApplyFavFilter()
        {
            index?.SetFacet("fav", state.FavoritesOnly ? "y" : null);
            UpdateFavButton();
        }

        void UpdateFavButton() => favButton.EnableInClassList("hs-seg--on", state.FavoritesOnly);

        /// <summary>Re-applies the search and sort to the grid.</summary>
        public void Refresh()
        {
            if (index == null) return;
            index.Refresh();
            grid.SetItems(index.Rows, index.Indices);
            shownOrder.Clear();
            foreach (int i in index.Indices) shownOrder.Add(index.Rows[i]);
            status.text = L10n.Format("search.results", index.Indices.Count, index.Rows.Count);
        }

        /// <summary>True when <paramref name="fullPath"/> is listed (search ignored; tests).</summary>
        public bool Lists(string fullPath) =>
            index != null && fullPath != null && index.Rows.Any(e => string.Equals(Path.GetFullPath(e.FullPath), Path.GetFullPath(fullPath), StringComparison.OrdinalIgnoreCase));

        /// <summary>Chooses the file <paramref name="fullPath"/> (search cleared when it hides it); false when it is not listed (tests).</summary>
        public bool Choose(string fullPath)
        {
            if (index == null || fullPath == null) return false;
            int pos = grid.PositionOf(e => string.Equals(Path.GetFullPath(e.FullPath), Path.GetFullPath(fullPath), StringComparison.OrdinalIgnoreCase));
            if (pos < 0 && (!string.IsNullOrEmpty(search.value) || state.FavoritesOnly))
            {
                search.SetValueWithoutNotify("");
                state.Query = "";
                index.SetQuery("");
                state.FavoritesOnly = false;
                ApplyFavFilter();
                Refresh();
                pos = grid.PositionOf(e => string.Equals(Path.GetFullPath(e.FullPath), Path.GetFullPath(fullPath), StringComparison.OrdinalIgnoreCase));
            }
            if (pos < 0) return false;
            grid.Select(pos);
            grid.ScrollToPos(pos);
            return true;
        }

        /// <summary>Selects a listed file without touching the search; scrolls to it only when asked (a kept scroll wins).</summary>
        void SelectPath(string fullPath, bool scroll)
        {
            int pos = grid.PositionOf(e => string.Equals(e.FullPath, fullPath, StringComparison.OrdinalIgnoreCase));
            if (pos < 0) return;
            var sv = Scroll;
            var keep = sv?.scrollOffset ?? Vector2.zero;
            grid.Select(pos);
            if (!scroll && sv != null) sv.scrollOffset = keep;
        }

        // ------------------------------------------------------------------ the kept scroll (v1.2)

        void OnScrolled(float y)
        {
            // a new browser starts at the top: its first scroll events must not overwrite the kept value before it is put back
            if (restoreScroll >= 0 || panel == null || index == null || index.Indices.Count == 0 || grid.List.resolvedStyle.height <= 1) return;
            state.ScrollY = y;
            state.Columns = grid.Columns;
            state.FirstPos = (int)(y / Math.Max(1f, grid.RowHeight)) * Math.Max(1, grid.Columns);
        }

        void TryRestoreScroll()
        {
            var sv = Scroll;
            if (sv == null || index == null) { if (++restoreTries > 120) EndRestore(); return; }
            float h = grid.List.resolvedStyle.height, w = grid.List.resolvedStyle.width;
            // wait for the grid's own layout: its columns follow its real width (before it, a default width gives other columns)
            bool laidOut = !float.IsNaN(h) && h > 1 && !float.IsNaN(w) && w > 1 && grid.Columns == grid.ColumnsFor(w);
            if (!laidOut || index.Indices.Count == 0) { if (++restoreTries > 120) EndRestore(); return; }
            float rh = Math.Max(1f, grid.RowHeight);
            int cols = Math.Max(1, grid.Columns);
            // the same columns: the same pixels; else the row that holds the first entry that was shown (and the same part of it)
            float target = state.Columns > 0 && state.Columns != cols ? state.FirstPos / cols * rh + restoreScroll % rh : restoreScroll;
            float high = sv.verticalScroller.highValue;
            if (high + 0.5f < target)
            {
                // the content is not laid out that far yet
                if (++restoreTries > 120) { sv.scrollOffset = new Vector2(sv.scrollOffset.x, Mathf.Max(0, high)); EndRestore(); }
                return;
            }
            if (Math.Abs(sv.scrollOffset.y - target) > 0.5f) sv.scrollOffset = new Vector2(sv.scrollOffset.x, target);
            // the region may settle its width a few frames later (other columns): keep the place for half a second
            if (restoreWatch == 0) restoreWatch = 30;
            if (--restoreWatch <= 0) EndRestore();
        }

        void EndRestore() { restoreScroll = -1; restoreWatch = 0; }

        // ------------------------------------------------------------------ favourites, menu, delete (v1.2)

        bool IsFavorite(CardEntry e)
        {
            if (favorites.FilesOf(BrowserFiles.RootOf(e, FileKind, sex)).Count == 0) return false;
            return BrowserFiles.IsFavorite(favorites, e, FileKind, sex);
        }

        void OnFavoritesChanged(string what)
        {
            if (index == null) return;
            index.AddFacet("fav", e => IsFavorite(e) ? "y" : "n");
            ApplyFavFilter();
            Refresh();
            grid.RefreshVisible();
        }

        /// <summary>The context menu of a file at a panel position.</summary>
        public void ShowMenu(CardEntry e, Vector2 at)
        {
            if (e == null) return;
            ListMenus.ShowFile(ctx, BrowserFiles.Of(e, FileKind, sex), at, (kind == FileBrowserKind.Coordinate ? "coord" : "chara-load") + "-context");
        }

        /// <summary>A file went to the recycle bin: its cell leaves the grid in place (order and scroll kept; the neighbour chosen
        /// when it was chosen).</summary>
        void OnDeleted(string full)
        {
            if (index == null || shownLibrary == null) return;
            int was = shownOrder.FindIndex(x => string.Equals(x.FullPath, full, StringComparison.OrdinalIgnoreCase));
            bool listedHere = was >= 0 || shownLibrary.Entries.Any(x => string.Equals(x.FullPath, full, StringComparison.OrdinalIgnoreCase));
            if (!listedHere && was < 0) return;
            bool chosen = string.Equals(chosenPath, full, StringComparison.OrdinalIgnoreCase);
            var sv = Scroll;
            var offset = sv?.scrollOffset ?? Vector2.zero;
            shownLibrary.Entries.RemoveAll(x => string.Equals(x.FullPath, full, StringComparison.OrdinalIgnoreCase));
            index.SetRows(shownLibrary.Entries);
            Refresh();
            if (sv != null) sv.scrollOffset = offset;
            int n = index.Indices.Count;
            if (chosen)
            {
                if (was >= 0 && n > 0) grid.Select(Math.Min(was, n - 1));
                else grid.Select(-1);
                if (sv != null) sv.scrollOffset = offset;
            }
        }
    }

    /// <summary>The Edit tab's coordinate dialog: the outfit-card browser of a sex and HS2's three load modes.</summary>
    public sealed class CoordinateDialog
    {
        public const string DialogId = "coordinate-load";
        /// <summary>The dialog open now (null when none; tests).</summary>
        public static CoordinateDialog Current { get; private set; }

        public CoordinateBrowser Browser { get; private set; }
        public Dialog Dialog { get; private set; }
        /// <summary>The result of the last load (null until a mode was pressed with a card chosen).</summary>
        public OpResult LastResult { get; private set; }
        Label note;
        Dialogs dialogs;
        Func<string, CoordinateApplyMode, OpResult> apply;

        /// <summary>Opens the dialog; <paramref name="apply"/> loads (file, mode) when a mode button is pressed with a card chosen.</summary>
        public static CoordinateDialog Open(UiContext ctx, int sex, Func<string, CoordinateApplyMode, OpResult> apply)
        {
            if (ctx?.Dialogs == null) return null;
            var d = new CoordinateDialog { apply = apply, dialogs = ctx.Dialogs };
            var content = new VisualElement { name = "coordinate-dialog-content" };
            content.AddToClassList("hs-coord-dialog");
            d.Browser = new CoordinateBrowser(ctx, sex, "coordinate-dialog-browser");
            d.Browser.Grid.HintKey = "browser.file.hint";
            content.Add(d.Browser);
            d.note = new Label { name = "coordinate-dialog-note" };
            d.note.AddToClassList("hs-text--dim");
            content.Add(d.note);
            var spec = new DialogSpec { Id = DialogId, Title = L10n.Get("inspector.charaEdit.clothes.loadCoordinate"), Message = L10n.Get("maker.coord.message"), Content = content };
            foreach (var mode in InspectorSchemas.CoordinateModes)
                spec.Buttons.Add(DialogButton.Of(mode, InspectorSchemas.CoordinateModeLabelKey(mode), mode == "all"));
            spec.Buttons.Add(DialogButton.Cancel());
            spec.CanClose = id => id == "cancel" || id == "closed" || d.Load(id);
            Current = d;
            d.Dialog = ctx.Dialogs.Show(spec, _ => { if (Current == d) Current = null; });
            return d;
        }

        /// <summary>A mode button (clothes / accessories / all): loads the chosen card; false (the dialog stays open) without one.</summary>
        public bool Load(string mode)
        {
            if (!TryMode(mode, out var m)) return true;
            var e = Browser?.Selected;
            if (e == null) { note.text = L10n.Get("maker.coord.noSelection"); return false; }
            LastResult = apply?.Invoke(e.FullPath, m);
            return true;
        }

        /// <summary>Presses a button of the dialog as a click would (tests): a mode id, or "cancel".</summary>
        public void Press(string id) { if (Dialog != null && Dialog.IsOpen && dialogs?.Current == Dialog) dialogs.Press(id); }

        static bool TryMode(string id, out CoordinateApplyMode m)
        {
            switch (id)
            {
                case "clothes": m = CoordinateApplyMode.Clothes; return true;
                case "accessories": m = CoordinateApplyMode.Accessories; return true;
                case "all": m = CoordinateApplyMode.All; return true;
                default: m = CoordinateApplyMode.All; return false;
            }
        }
    }
}
