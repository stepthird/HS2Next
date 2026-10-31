// The image file browser behind FilePicker (
// image, backgrounds up to 7680 × 4320 → thumbnails shrink on the GPU): a modal dialog with a thumbnail grid of
// the image files (*.png, *.jpg) of the folders of a kind — background: HS2 DefaultData/bg, UserData/bg, <UserData>/bg;
// frame: UserData/frame, <UserData>/frame; pattern: UserData/pattern, <UserData>/pattern; image (panel images and anything
// else): all of these plus <UserData>/image. Search by file name; OK returns the absolute path. Never an OS dialog.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    public sealed class ImageEntry
    {
        public string FullPath, FileName, Folder;
        public string SearchText => FileName + " " + Folder;
    }

    public sealed class ImageBrowser : VisualElement
    {
        public const string DialogId = "imageBrowser";
        static readonly ThumbnailLoader loader = new ThumbnailLoader { CapBytes = 40L * 1024 * 1024, MaxInFlight = 2 };

        readonly CardGrid<ImageEntry> grid;
        readonly ListIndex<ImageEntry> index;
        public static ImageBrowser Open { get; private set; }
        public ListIndex<ImageEntry> Index => index;
        public CardGrid<ImageEntry> Grid => grid;

        /// <summary>The folders of a picker kind (existing ones only).</summary>
        public static List<string> FoldersOf(UiContext ctx, string kind)
        {
            var p = ctx.App.Paths;
            var l = new List<string>();
            void Add(params string[] parts)
            {
                if (parts.Any(x => x == null)) return;
                string d = Path.Combine(parts);
                if (Directory.Exists(d) && !l.Contains(d, StringComparer.OrdinalIgnoreCase)) l.Add(d);
            }
            bool all = kind != "background" && kind != "frame" && kind != "pattern";
            if (kind == "background" || all) { Add(p?.Hs2Root, "DefaultData", "bg"); Add(p?.Hs2UserData, "bg"); Add(p?.UserData, "bg"); }
            if (kind == "frame" || all) { Add(p?.Hs2UserData, "frame"); Add(p?.UserData, "frame"); }
            if (kind == "pattern" || all) { Add(p?.Hs2UserData, "pattern"); Add(p?.UserData, "pattern"); }
            if (all) Add(p?.UserData, "image");
            return l;
        }

        public static List<ImageEntry> Scan(IEnumerable<string> folders)
        {
            var l = new List<ImageEntry>();
            foreach (var d in folders)
            {
                string[] files;
                try { files = Directory.GetFiles(d, "*.*", SearchOption.AllDirectories); } catch (Exception) { continue; }
                Array.Sort(files, StringComparer.Ordinal);
                foreach (var f in files)
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext != ".png" && ext != ".jpg" && ext != ".jpeg") continue;
                    l.Add(new ImageEntry { FullPath = f, FileName = Path.GetFileNameWithoutExtension(f), Folder = Path.GetFileName(d) });
                }
            }
            return l;
        }

        ImageBrowser(UiContext ctx, string kind)
        {
            name = "image-browser";
            PanelKit.Style(this);
            AddToClassList("hs-scene-browser");
            index = new ListIndex<ImageEntry>(Scan(FoldersOf(ctx, kind)), e => e.SearchText);
            var header = new VisualElement();
            header.AddToClassList("hs-panel__header");
            var search = PanelKit.SearchField("image-search", q => { index.SetQuery(q); Refresh(); });
            search.AddToClassList("hs-grow");
            header.Add(search);
            Add(header);
            grid = new CardGrid<ImageEntry>("image-grid", loader, e => e.FullPath, e => e.FileName, null, 9f / 16f);
            grid.AddToClassList("hs-grow");
            Add(grid);
            PanelKit.EveryFrame(this, () => loader.Update(4));
            Refresh();
        }

        void Refresh()
        {
            index.Refresh();
            grid.SetItems(index.Rows, index.Indices);
        }

        /// <summary>Shows the browser; <paramref name="done"/> gets the chosen path or null.</summary>
        public static ImageBrowser Show(UiContext ctx, string kind, string current, Action<string> done)
        {
            if (ctx.Dialogs == null) { done?.Invoke(null); return null; }
            var b = new ImageBrowser(ctx, kind ?? "image");
            if (current != null)
                for (int i = 0; i < b.index.Indices.Count; i++)
                    if (string.Equals(b.index.Rows[b.index.Indices[i]].FullPath, current, StringComparison.OrdinalIgnoreCase)) { b.grid.Select(i); break; }
            b.grid.HintKey = "browser.image.hint";
            b.grid.Activated += _ => ctx.Dialogs.Press("ok");
            var spec = new DialogSpec { Id = DialogId, Title = L10n.Get("files.chooseImage"), Content = b };
            spec.Buttons.Add(DialogButton.Ok());
            spec.Buttons.Add(DialogButton.Cancel());
            spec.CanClose = id => id != "ok" || b.grid.Selected != null;
            Open = b;
            ctx.Dialogs.Show(spec, id => { if (Open == b) Open = null; done?.Invoke(id == "ok" ? b.grid.Selected?.FullPath : null); });
            return b;
        }
    }
}
