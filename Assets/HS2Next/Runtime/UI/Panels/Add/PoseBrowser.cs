// The pose browser: folder tree + grid (PoseTools .png and
// .hs2npose thumbnails; .dat files without an image show a text tile) or list, search by pose name and file name.
// Load applies the pose on the primary character (HS2 .dat / .png through StudioOps.LoadPose, .hs2npose through
// StudioOps.LoadPoseFragment; one entry "Load pose"); Save writes a .hs2npose of the primary (name prompt, the bridge's
// pose.save path: a file, not a document change); Delete is offered for HS2Next files only (recycle bin when available).
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
        readonly FolderTree folders;
        readonly CardGrid<PoseEntry> grid;
        readonly MultiColumnListView listView;
        readonly Label status;
        readonly Button loadButton, saveButton, deleteButton, gridButton, listButton;
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
            folders.Selected += k => { folderKey = k; if (index != null) { index.AddFacet("folder", e => PoseFiles.InFolder(e, folderKey) ? "in" : "out"); index.SetFacet("folder", k == null ? null : "in"); Refresh(); } };
            var right = new VisualElement();
            right.AddToClassList("hs-grow");
            grid = new CardGrid<PoseEntry>("pose-grid", loader, e => e.FullPath, e => e.DisplayName, e => e.HasImage, 1f);
            grid.AddToClassList("hs-grow");
            grid.HintKey = "browser.pose.hint";
            grid.Activated += e => ctx.ShowResult(Load(e));
            grid.SelectionChanged += _ => UpdateButtons();
            grid.DragPayloadOf = e => new ApplyPayload(e.DisplayName, NodeKind.Chara, id => Load(e, id));
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
            listView.itemsChosen += _ => ctx.ShowResult(Load(SelectedEntry));
            listView.selectionChanged += _ => UpdateButtons();
            right.Add(listView);
            var body = new AdaptiveSplit("pose-body", folders, right, 200, 0.25f, 110);
            body.AddToClassList("hs-grow");
            Body = body;
            Add(body);

            var footer = new VisualElement();
            footer.AddToClassList("hs-panel__footer");
            loadButton = Loc.Button("browser.loadPose", () => ctx.ShowResult(Load(SelectedEntry))); loadButton.name = "pose-load";
            saveButton = Loc.Button("browser.savePose", SavePrompt); saveButton.name = "pose-save";
            deleteButton = Loc.Button("browser.delete", DeleteSelected); deleteButton.name = "pose-delete";
            status = new Label { name = "pose-status" };
            status.AddToClassList("hs-status-text");
            footer.Add(loadButton); footer.Add(saveButton); footer.Add(deleteButton); footer.Add(status);
            Add(footer);

            ctx.SelectionChanged += _ => UpdateButtons();
            RegisterCallback<GeometryChangedEvent>(_ => StartScan());
            PanelKit.EveryFrame(this, Tick);
            SetListMode(false);
            UpdateButtons();
        }

        void AddColumn(string id, string titleKey, float w, Func<PoseEntry, string> text)
        {
            var c = new Column { name = id, title = L10n.Get(titleKey), width = w, stretchable = id == "name" };
            c.makeCell = () =>
            {
                var l = new Label();
                l.AddToClassList("hs-list-cell");
                DragController.MakeSource(l, _ => l.userData is PoseEntry e ? new ApplyPayload(e.DisplayName, NodeKind.Chara, id2 => Load(e, id2)) : null);
                return l;
            };
            c.bindCell = (el, i) => { var e = index.Rows[index.Indices[i]]; ((Label)el).text = text(e); el.userData = e; };
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
            loader.Update(4);
            if (scan != null)
            {
                if (!scan.IsCompleted) return;   // a rescan: the old list stays until the new one is there
                ShowList();
            }
            if (files == null || index == null || readFinished) return;
            // names read since the last fill, four times a second (each fill refreshes the grid once)
            double now = scanClock.Elapsed.TotalMilliseconds;
            bool done = files.ReadDone && (read == null || read.IsCompleted);
            if (!done && now - lastFill < 250) return;
            if (!files.HasResults && !done) return;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var changed = files.TakeResults();
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
            index.AddFacet("listed", e => e.Listed ? "y" : "n");
            index.AddFacet("folder", e => PoseFiles.InFolder(e, folderKey) ? "in" : "out");
            index.AddFacet("sex", e => e.Sex.ToString());
            index.SetFacet("listed", "y");
            if (folderKey != null) index.SetFacet("folder", "in");
            if (sexFilter != null) index.SetFacet("sex", sexFilter.Value.ToString());
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
            status.text = readFinished || files == null ? L10n.Format("search.results", index.Indices.Count, listed)
                : L10n.Format("browser.pose.reading", index.Indices.Count, listed, files.Unread);
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
            };
        }

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

        void DeleteSelected()
        {
            var e = SelectedEntry;
            if (e == null || !e.CanDelete) return;
            ctx.Dialogs.Confirm("dialog.deletePose.title", "dialog.deletePose.message", ok =>
            {
                if (!ok) return;
                string err = RecycleBin.Delete(e.FullPath);
                if (err != null) ctx.Toasts?.Show(err, ToastKind.Error);
                loader.Forget(e.FullPath);
                Rescan();
            }, e.DisplayName);
        }
    }
}
