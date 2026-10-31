// The scene browser: a modal dialog with a grid of the .hs2nscene thumbnails (5:7)
// under <UserData>/scene recursively, sort newest first (default) or by name, search over title and file name, and a
// preview pane (title, file, date, characters, size; the findings of the last load). Buttons: Load (prompt if dirty),
// Import (merge), Delete (confirmation; recycle bin when available), Reveal in Explorer, Close. File ▸ Import opens the
// same browser with Import as the default button. The scan runs off the main thread.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    public sealed class SceneBrowser : VisualElement
    {
        public const string DialogId = "sceneBrowser";
        static readonly ThumbnailLoader loader = new ThumbnailLoader { CapBytes = 40L * 1024 * 1024 };

        readonly UiContext ctx;
        readonly SceneFiles files;
        readonly CardGrid<SceneFileEntry> grid;
        readonly Label preview, status;
        readonly DropdownField sort;
        ListIndex<SceneFileEntry> index;
        Task<List<SceneFileEntry>> scan;
        string query = "";

        public static SceneBrowser Open { get; private set; }
        public ListIndex<SceneFileEntry> Index => index;
        public bool Ready => index != null;
        public CardGrid<SceneFileEntry> Grid => grid;
        public SceneFileEntry Selected => grid.Selected;

        SceneBrowser(UiContext ctx, SceneFiles files)
        {
            this.ctx = ctx;
            this.files = files;
            name = "scene-browser"; PanelKit.Style(this);
            AddToClassList("hs-scene-browser");
            var header = new VisualElement();
            header.AddToClassList("hs-panel__header");
            var search = PanelKit.SearchField("scene-search", q => { query = q; index?.SetQuery(q); Refresh(); });
            search.AddToClassList("hs-grow");
            header.Add(search);
            sort = new DropdownField { name = "scene-sort", choices = new List<string> { "date", "name" }, index = 0 };
            sort.formatListItemCallback = s => L10n.Get(s == "date" ? "sort.newest" : "sort.name");
            sort.formatSelectedValueCallback = sort.formatListItemCallback;
            sort.RegisterValueChangedCallback(_ => { ApplySort(); Refresh(); });
            header.Add(sort);
            Add(header);

            var body = new VisualElement();
            body.AddToClassList("hs-scene-browser__body");
            grid = new CardGrid<SceneFileEntry>("scene-grid", loader, e => e.FullPath, e => e.DisplayName, null, 1.4f);
            grid.AddToClassList("hs-grow");
            grid.HintKey = "browser.scene.hint";
            grid.SelectionChanged += e => ShowPreview(e);
            grid.Activated += e => Press(files != null && importMode ? "import" : "load");
            body.Add(grid);
            preview = new Label { name = "scene-preview" };
            preview.AddToClassList("hs-scene-browser__preview");
            body.Add(preview);
            Add(body);
            status = new Label { name = "scene-status" };
            status.AddToClassList("hs-status-text");
            Add(status);
            PanelKit.EveryFrame(this, Tick);
            Rescan();
        }

        bool importMode;
        Dialog dialog;

        /// <summary>Shows the browser as a modal dialog.</summary>
        public static SceneBrowser Show(UiContext ctx, SceneFiles files, bool import)
        {
            if (ctx.Dialogs == null) return null;
            var b = new SceneBrowser(ctx, files) { importMode = import };
            var spec = new DialogSpec { Id = DialogId, Title = L10n.Get(import ? "action.file.import" : "action.file.open"), Content = b };
            spec.Buttons.Add(DialogButton.Of("load", "files.load", isDefault: !import));
            spec.Buttons.Add(DialogButton.Of("import", "files.import", isDefault: import));
            spec.Buttons.Add(DialogButton.Of("delete", "browser.delete"));
            spec.Buttons.Add(DialogButton.Of("reveal", "files.reveal"));
            spec.Buttons.Add(DialogButton.Of("close", "common.close", isCancel: true));
            spec.CanClose = b.CanClose;
            Open = b;
            b.dialog = ctx.Dialogs.Show(spec, id => { if (Open == b) Open = null; b.Closed(id); });
            return b;
        }

        string pendingAction;

        bool CanClose(string id)
        {
            var e = Selected;
            switch (id)
            {
                case "load":
                case "import":
                    if (e == null) return false;
                    pendingAction = id;
                    return true;
                case "delete":
                    if (e != null) DeleteAsk(e);
                    return false;
                case "reveal":
                    if (e != null) Reveal(e.FullPath);
                    return false;
                default:
                    return true;
            }
        }

        void Closed(string id)
        {
            var e = Selected;
            string a = pendingAction;
            pendingAction = null;
            if (e == null) return;
            if (a == "load") files.Load(e.FullPath);
            else if (a == "import") files.Import(e.FullPath);
        }

        /// <summary>Presses a dialog button (tests; double click).</summary>
        public void Press(string id)
        {
            if (dialog == null || !dialog.IsOpen) return;
            ctx.Dialogs.Press(id);
        }

        public void Rescan()
        {
            string dir = files?.SceneDir;
            scan = Task.Run(() => SceneLibrary.Scan(dir));
            status.text = L10n.Get("browser.scanning");
        }

        /// <summary>One frame of work (the panel scheduler calls it; tests may pump it).</summary>
        public void Pump() => Tick();

        void Tick()
        {
            loader.Update(4);
            if (scan == null || !scan.IsCompleted) return;
            var t = scan;
            scan = null;
            if (t.IsFaulted) { status.text = t.Exception?.GetBaseException().Message; return; }
            index = new ListIndex<SceneFileEntry>(t.Result, e => e.SearchText);
            index.AddSort("date", (a, b) => a.When.CompareTo(b.When));
            index.AddSort("name", (a, b) => ListSorts.CompareJa(a.DisplayName, b.DisplayName));
            index.SetQuery(query);
            ApplySort();
            Refresh();
            if (index.Indices.Count > 0) grid.Select(0);
        }

        void ApplySort()
        {
            if (index == null) return;
            if (sort.value == "name") index.SetSort("name", false); else index.SetSort("date", true);
        }

        void Refresh()
        {
            if (index == null) return;
            index.Refresh();
            grid.SetItems(index.Rows, index.Indices);
            status.text = L10n.Format("search.results", index.Indices.Count, index.Rows.Count);
            ShowPreview(grid.Selected);
        }

        void ShowPreview(SceneFileEntry e)
        {
            if (e == null) { preview.text = ""; return; }
            var lines = new List<string>
            {
                e.DisplayName,
                e.RelativePath,
                e.When.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                L10n.Format("files.characters", e.Characters),
                L10n.Format("files.size", (e.Size / 1024.0 / 1024.0).ToString("0.0")),
            };
            if (e.Problem != null) lines.Add("⚠ " + e.Problem);
            if (string.Equals(e.FullPath, files?.CurrentPath, StringComparison.OrdinalIgnoreCase))
            {
                lines.Add(L10n.Get("files.current"));
                int n = 0;
                foreach (var f in ctx.Findings) if (f.Severity >= HS2Next.Save.FindingSeverity.Warning && n++ < 8) lines.Add("• " + f.Code + ": " + f.Message);
            }
            preview.text = string.Join("\n", lines);
        }

        VisualElement confirmBar;

        /// <summary>Delete asks inline (a second modal dialog would queue behind this one): "Delete …? [Delete] [Cancel]".</summary>
        void DeleteAsk(SceneFileEntry e)
        {
            confirmBar?.RemoveFromHierarchy();
            confirmBar = new VisualElement { name = "scene-delete-confirm" };
            confirmBar.AddToClassList("hs-confirm-bar");
            confirmBar.Add(new Label(L10n.Format("dialog.deleteScene.message", e.DisplayName)));
            var yes = Loc.Button("browser.delete", () => { confirmBar?.RemoveFromHierarchy(); confirmBar = null; DeleteNow(e); });
            yes.name = "scene-delete-yes";
            var no = Loc.Button("common.cancel", () => { confirmBar?.RemoveFromHierarchy(); confirmBar = null; });
            no.name = "scene-delete-no";
            confirmBar.Add(yes);
            confirmBar.Add(no);
            Add(confirmBar);
        }

        /// <summary>Deletes a scene file (recycle bin when available) and rescans.</summary>
        public string DeleteNow(SceneFileEntry e)
        {
            string err = RecycleBin.Delete(e.FullPath);
            if (err != null) ctx.Toasts?.Show(err, ToastKind.Error);
            else files?.Recent.Remove(e.FullPath);
            loader.Forget(e.FullPath);
            Rescan();
            return err;
        }

        static void Reveal(string path)
        {
            try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + path + "\""); }
            catch (Exception e) { Debug.LogWarning("[ui] reveal failed: " + e.Message); }
        }
    }
}
