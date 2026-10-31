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

        readonly UiContext ctx;
        readonly int sex;
        readonly FileBrowserKind kind;
        readonly CardGrid<CardEntry> grid;
        readonly TextField search;
        readonly DropdownField sortField;
        readonly Button dirButton;
        readonly Label status;
        ListIndex<CardEntry> index;
        CardLibrary shownLibrary;
        bool descending;

        /// <summary>The sex whose files are listed (0 male, 1 female).</summary>
        public int Sex => sex;
        /// <summary>Coordinates or characters.</summary>
        public FileBrowserKind Kind => kind;
        public CardGrid<CardEntry> Grid => grid;
        public ListIndex<CardEntry> Index => index;
        /// <summary>True once the files of the sex are listed.</summary>
        public bool Ready => index != null;
        /// <summary>The chosen file (null = none).</summary>
        public CardEntry Selected => grid.Selected;
        /// <summary>Double click / Enter on a cell.</summary>
        public event Action<CardEntry> Activated;
        public event Action<CardEntry> SelectionChanged;
        /// <summary>A new file list is shown (after the first scan and after a rescan).</summary>
        public event Action Listed;
        public static ThumbnailLoader Loader => loader;

        int Slot => SlotOf(kind, sex);
        static int SlotOf(FileBrowserKind k, int s) => (int)k * 2 + (s == 0 ? 0 : 1);

        public CoordinateBrowser(UiContext ctx, int sex, string elementName = "coord-browser", FileBrowserKind kind = FileBrowserKind.Coordinate)
        {
            this.ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            this.sex = sex == 0 ? 0 : 1;
            this.kind = kind;
            string p = kind == FileBrowserKind.Coordinate ? "coord" : "chara-load";
            name = elementName;
            AddToClassList(UssClass);
            var header = new VisualElement { name = p + "-header" };
            header.AddToClassList("hs-coord-browser__header");
            search = PanelKit.SearchField(p + "-search", q => { index?.SetQuery(q ?? ""); Refresh(); });
            search.AddToClassList("hs-grow");
            header.Add(search);
            sortField = new DropdownField { name = p + "-sort", choices = new List<string> { "name", "date" }, index = 0 };
            sortField.formatListItemCallback = s => L10n.Get("sort." + s);
            sortField.formatSelectedValueCallback = s => L10n.Get("sort." + s);
            sortField.RegisterValueChangedCallback(e => { ApplySort(); Refresh(); });
            header.Add(sortField);
            dirButton = new Button(() => { descending = !descending; ApplySort(); Refresh(); }) { name = p + "-sort-dir", text = "▲" };
            dirButton.AddToClassList("hs-icon-button");
            Loc.Tooltip(dirButton, "sort.ascending");
            header.Add(dirButton);
            var refresh = new Button(() => Rescan()) { name = p + "-refresh", text = "↻" };
            refresh.AddToClassList("hs-icon-button");
            Loc.Tooltip(refresh, kind == FileBrowserKind.Coordinate ? "maker.coord.refresh" : "inspector.charaEdit.file.refresh");
            header.Add(refresh);
            Add(header);
            string cell = kind == FileBrowserKind.Coordinate ? "coord-cell-" : "chara-file-";
            grid = new CardGrid<CardEntry>(p + "-grid", loader, e => e.FullPath, e => e.FileName, _ => true)
            {
                CellName = (e, pos) => cell + e.FileName,
                Tooltip = e => e.FileName + "\n" + e.FolderKey,
            };
            grid.AddToClassList("hs-coord-browser__grid");
            grid.Activated += e => Activated?.Invoke(e);
            grid.SelectionChanged += e => SelectionChanged?.Invoke(e);
            Add(grid);
            status = new Label { name = p + "-status" };
            status.AddToClassList("hs-status-text");
            Add(status);
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

        static Task<CardLibrary> ScanTask(UiContext ctx, FileBrowserKind kind, int sex)
        {
            string hs2 = Hs2DirOf(ctx, kind, sex), own = OwnDirOf(ctx, kind, sex);
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
                            Size = fi.Length, ModifiedUtc = fi.LastWriteTimeUtc, IsHs2n = true,
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
        }

        void Show(CardLibrary lib)
        {
            // a rescan keeps the chosen file when it is still listed
            string chosen = grid.Selected?.FullPath;
            shownLibrary = lib;
            index = new ListIndex<CardEntry>(lib.Entries, e => e.FileName);
            index.AddSort("name", (a, b) => ListSorts.CompareJa(a.FileName, b.FileName));
            index.AddSort("date", (a, b) => a.ModifiedUtc.CompareTo(b.ModifiedUtc));
            index.SetQuery(search.value ?? "");
            ApplySort();
            Refresh();
            if (chosen != null) Choose(chosen);
            Listed?.Invoke();
        }

        void ApplySort()
        {
            if (index == null) return;
            index.SetSort(sortField.value ?? "name", descending);
            dirButton.text = descending ? "▼" : "▲";
            Loc.Tooltip(dirButton, descending ? "sort.descending" : "sort.ascending");
        }

        /// <summary>Re-applies the search and sort to the grid.</summary>
        public void Refresh()
        {
            if (index == null) return;
            index.Refresh();
            grid.SetItems(index.Rows, index.Indices);
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
            if (pos < 0 && !string.IsNullOrEmpty(search.value))
            {
                search.SetValueWithoutNotify("");
                index.SetQuery("");
                Refresh();
                pos = grid.PositionOf(e => string.Equals(Path.GetFullPath(e.FullPath), Path.GetFullPath(fullPath), StringComparison.OrdinalIgnoreCase));
            }
            if (pos < 0) return false;
            grid.Select(pos);
            grid.ScrollToPos(pos);
            return true;
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
