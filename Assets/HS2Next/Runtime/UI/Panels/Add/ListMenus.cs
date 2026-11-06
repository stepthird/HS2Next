// The shared parts of the browsers' favourites, «最近追加» (recently added) and right-click menus (
// 28): the view keys, the favourites store of the app run, the creation times of the zipmods (read off the main thread
// when a view first needs them, one stat per file), the file rows' keys and their delete (to the recycle bin only), the menu
// of a file row (favourite, details, show in Explorer, copy the path, delete) and of a catalog row (favourite, details, show
// in Explorer, copy the path or the id), the details text, and a dialog that can stand above an open modal dialog (the
// coordinate dialog and the character load dialog hold browsers). Card images are never read for the details: the name index,
// the card's plugin data block and the file system give them.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HS2Next.Content;
using HS2Next.Content.Studio;
using HS2Next.Save;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    /// <summary>The two views every browser offers above its folders or groups.</summary>
    public static class BrowserViews
    {
        /// <summary>The view keys (in a folder tree or a group facet list beside the real keys; never a folder or group).</summary>
        public const string Favorites = "\u0002fav", Recent = "\u0002recent";
        /// <summary>The star of a favourite's cell or row.</summary>
        public const string Star = "★ ";

        public static bool Is(string key) => key != null && key.Length > 1 && key[0] == '\u0002';
        public static string LabelKey(string view) => view == Favorites ? "browser.view.favorites" : "browser.view.recent";
        /// <summary>The tooltip of a view: what it lists (files or catalog rows).</summary>
        public static string TooltipKey(string view, bool files) =>
            view == Favorites ? "browser.view.favorites.tip" : files ? "browser.view.recent.tipFiles" : "browser.view.recent.tipRows";
    }

    /// <summary>The favourites store of the app run: &lt;UserData&gt;/config/favorites.json, loaded on first use; an unreadable
    /// file is reported once as a toast. One instance for the app run (browsers keep it): another file or a reload reads into it.</summary>
    public static class Favorites
    {
        static FavoriteStore store;
        static string storePath;

        /// <summary>Tests: the file the store uses (null = the app's config folder). Changing it reloads the store.</summary>
        public static string FileOverride;
        /// <summary>A favourite was added or removed in the store of the app run (the list kind or root id; null = reloaded).</summary>
        public static event Action<string> Changed;

        public static FavoriteStore Of(UiContext ctx)
        {
            string path = FileOverride ?? (ctx?.Options?.ConfigDir != null ? Path.Combine(ctx.Options.ConfigDir, FavoriteStore.FileName) : null);
            if (store == null)
            {
                store = FavoriteStore.Load(path);
                storePath = path;
                store.Changed += k => UiContext.Guard(() => Changed?.Invoke(k));
            }
            else if (!string.Equals(storePath, path, StringComparison.OrdinalIgnoreCase))
            {
                storePath = path;
                store.Reload(path);
            }
            if (store.Problem != null && !store.ProblemShown && ctx?.Toasts != null)
            {
                store.ProblemShown = true;
                string msg = store.BackupPath != null ? L10n.Format("list.fav.corrupt", Path.GetFileName(store.BackupPath)) : L10n.Format("list.fav.problem", store.Problem);
                ctx.Toasts.Show(msg, ToastKind.Warning, 12f);
                Debug.LogWarning("[ui] favourites: " + store.Problem);
            }
            return store;
        }

        /// <summary>Tests: reads the file again into the store of the app run (the browsers keep the same instance) and tells them.</summary>
        public static void Reload()
        {
            if (store == null) { UiContext.Guard(() => Changed?.Invoke(null)); return; }
            store.Reload();
        }
    }

    /// <summary>The creation times of the zipmod files (the date a zipmod was placed), read once per file and app run off the
    /// main thread (the scan cache keeps sizes and write times only: a new member would force a slow rescan at the first start).</summary>
    public static class ZipmodDates
    {
        static readonly ConcurrentDictionary<string, long> created = new ConcurrentDictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        static int stats;

        /// <summary>Files asked so far (one stat each).</summary>
        public static int Stats => Volatile.Read(ref stats);
        /// <summary>Tests: the creation time of a zipmod path (null = unknown) instead of the file system.</summary>
        public static Func<string, DateTime?> TestProvider;

        public static bool Known(ZipmodInfo z) => z?.Path != null && created.ContainsKey(z.Path);

        /// <summary>The creation time (UTC ticks) of the zipmod's file; 0 when unknown or not read yet.</summary>
        public static long Ticks(ZipmodInfo z) => z?.Path != null && created.TryGetValue(z.Path, out long t) ? t : 0;

        /// <summary>Reads the creation times of the zipmods not known yet on a worker; the task ends when every one is known.</summary>
        public static Task Ensure(IEnumerable<ZipmodInfo> zipmods)
        {
            var todo = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var z in zipmods)
                if (z?.Path != null && !created.ContainsKey(z.Path) && seen.Add(z.Path)) todo.Add(z.Path);
            if (todo.Count == 0) return Task.CompletedTask;
            var provider = TestProvider;
            return Task.Run(() =>
            {
                foreach (var p in todo)
                {
                    long t = 0;
                    try
                    {
                        var d = provider != null ? provider(p) : File.Exists(p) ? File.GetCreationTimeUtc(p) : (DateTime?)null;
                        if (d.HasValue && d.Value.Year >= 1980) t = d.Value.ToUniversalTime().Ticks;
                    }
                    catch (Exception) { t = 0; }
                    created[p] = t;
                    Interlocked.Increment(ref stats);
                }
            });
        }

        /// <summary>Tests: forgets every time read.</summary>
        public static void Clear() { created.Clear(); Interlocked.Exchange(ref stats, 0); }
    }

    /// <summary>What a file browser lists.</summary>
    public enum BrowserFileKind { Chara, Coordinate, Pose }

    /// <summary>A file row of a browser, as the shared menu sees it.</summary>
    public sealed class BrowserFile
    {
        public string FullPath;
        public BrowserFileKind Kind;
        /// <summary>A file of the HS2 install's folders (else HS2Next's own).</summary>
        public bool FromHs2;
        /// <summary>The favourites key: the root id and the path relative to it.</summary>
        public string RootId, RelPath;
        public string DisplayName;
        /// <summary>The character's name from the name index, or a pose's name (null = not known yet).</summary>
        public string CharaName;
        /// <summary>0 male, 1 female, -1 unknown.</summary>
        public int Sex = -1;
        public long Size;
        public DateTime CreatedUtc, ModifiedUtc;
        public string FileName => Path.GetFileName(FullPath ?? "");
    }

    /// <summary>The file rows' favourites keys and their delete.</summary>
    public static class BrowserFiles
    {
        /// <summary>The favourites root id of a folder: "hs2.chara.female", "hs2next.chara", "hs2.coord.male", "hs2next.pose", ….</summary>
        public static string RootId(BrowserFileKind kind, bool hs2, int sex)
        {
            string s = sex == 0 ? "male" : "female";
            switch (kind)
            {
                case BrowserFileKind.Chara: return hs2 ? "hs2.chara." + s : "hs2next.chara";
                case BrowserFileKind.Coordinate: return (hs2 ? "hs2" : "hs2next") + ".coord." + s;
                default: return hs2 ? "hs2.pose" : "hs2next.pose";
            }
        }

        /// <summary>The path of a file relative to its root ('/' separators) from its folder key part and full path.</summary>
        public static string Rel(string folder, string fullPath)
        {
            string name = Path.GetFileName(fullPath ?? "");
            return FavoriteStore.Rel(string.IsNullOrEmpty(folder) ? name : folder + "/" + name);
        }

        public static string RootOf(CardEntry e, BrowserFileKind kind, int sex) => RootId(kind, e.Root == CardLibrary.RootHs2, sex);
        public static bool IsFavorite(FavoriteStore s, CardEntry e, BrowserFileKind kind, int sex) => s.IsFile(RootOf(e, kind, sex), Rel(e.Folder, e.FullPath));
        public static bool IsFavorite(FavoriteStore s, PoseEntry e) => s.IsFile(RootId(BrowserFileKind.Pose, !e.IsHs2n, 0), Rel(e.Folder, e.FullPath));

        public static BrowserFile Of(CardEntry e, BrowserFileKind kind, int sex) => e == null ? null : new BrowserFile
        {
            FullPath = e.FullPath, Kind = kind, FromHs2 = e.Root == CardLibrary.RootHs2, RootId = RootOf(e, kind, sex), RelPath = Rel(e.Folder, e.FullPath),
            DisplayName = e.DisplayName, CharaName = string.IsNullOrEmpty(e.CharaName) ? null : e.CharaName,
            Sex = kind == BrowserFileKind.Coordinate ? sex : e.HeaderSex >= 0 ? e.HeaderSex : e.IsHs2n ? -1 : sex,
            Size = e.Size, CreatedUtc = e.CreatedUtc, ModifiedUtc = e.ModifiedUtc,
        };

        public static BrowserFile Of(PoseEntry e) => e == null ? null : new BrowserFile
        {
            FullPath = e.FullPath, Kind = BrowserFileKind.Pose, FromHs2 = !e.IsHs2n, RootId = RootId(BrowserFileKind.Pose, !e.IsHs2n, 0),
            RelPath = Rel(e.Folder, e.FullPath), DisplayName = e.DisplayName, CharaName = e.Name, Sex = e.Sex,
            Size = e.Size, CreatedUtc = e.CreatedUtc, ModifiedUtc = e.ModifiedUtc,
        };

        /// <summary>Tests: when set, a delete of a file outside this folder is refused before anything is touched.</summary>
        public static string TestSandbox;
        /// <summary>Files moved to the recycle bin by the browsers so far (tests).</summary>
        public static int DeletesDone { get; private set; }
        /// <summary>Why the last delete was refused (null = it went to the recycle bin).</summary>
        public static string LastProblem { get; private set; }
        /// <summary>A file went to the recycle bin (its full path): every browser that lists it drops it.</summary>
        public static event Action<string> Deleted;

        static bool Under(string full, string dir)
        {
            string d = Path.GetFullPath(dir).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            return full.StartsWith(d, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Moves the file to the recycle bin (never a permanent delete; a refusal leaves the file and shows a toast), then forgets
        /// it: the favourite, the name index's entry, every browser's row. Not a history entry. Returns null or the problem.
        /// </summary>
        public static string Delete(UiContext ctx, BrowserFile f)
        {
            string full = null, err = null;
            if (f == null || string.IsNullOrEmpty(f.FullPath)) err = "no file";
            else
            {
                try { full = Path.GetFullPath(f.FullPath); }
                catch (Exception e) { err = e.Message; }
            }
            if (err == null && TestSandbox != null && !Under(full, TestSandbox)) err = "outside the test sandbox";
            if (err == null && !File.Exists(full)) err = "file not found";
            bool bin = false;
            if (err == null)
            {
                bin = RecycleBin.Unavailable(full) != null;
                err = RecycleBin.Recycle(full);
                bin |= err != null && err.StartsWith("the recycle bin", StringComparison.Ordinal);
            }
            LastProblem = err;
            if (err != null)
            {
                string why = bin ? L10n.Get("list.delete.noBin") + " (" + err + ")" : err;
                ctx?.Toasts?.Show(L10n.Format("list.delete.refused", f?.FileName ?? "", why), ToastKind.Error, 8f);
                Debug.LogWarning("[ui] delete refused: " + (f?.FileName ?? "") + ": " + err);
                return err;
            }
            DeletesDone++;
            if (ctx != null) Favorites.Of(ctx).SetFile(f.RootId, f.RelPath, false);
            CardNameIndex.Forget(full);
            UiContext.Guard(() => Deleted?.Invoke(full));
            ctx?.Shell?.Overlay?.BringToFront();
            ctx?.Toasts?.Show(L10n.Format("list.delete.done", f.FileName), ToastKind.Success);
            return null;
        }
    }

    /// <summary>A catalog row of a list browser (item, animation, map, light), as the shared menu sees it.</summary>
    public sealed class CatalogRow
    {
        /// <summary>The list kind (FavoriteStore.Items …) and the content id ("guid#id").</summary>
        public string Kind, Ref;
        public string Name, Group, Category;
        public StudioRecord Record;
        public ZipmodInfo Zipmod => Record?.Zipmod;
    }

    /// <summary>The right-click menus of the browsers' rows and cells, their details text and the delete confirmation.</summary>
    public static class ListMenus
    {
        /// <summary>The items of the last menu shown, and its element name (tests; the menu itself is in the shell's overlay).</summary>
        public static List<MenuItem> LastItems { get; private set; }
        public static string LastMenuName { get; private set; }
        /// <summary>The last text put on the clipboard by a menu (tests).</summary>
        public static string LastCopied { get; private set; }
        /// <summary>Tests: called instead of starting Explorer.</summary>
        public static Action<string> TestReveal;
        /// <summary>The last details text shown (tests).</summary>
        public static string LastDetails { get; private set; }

        /// <summary>True for the keys that open the menu of the focused cell or row: the menu key or Shift+F10.</summary>
        public static bool IsMenuKey(KeyDownEvent e) => e.keyCode == KeyCode.Menu || (e.keyCode == KeyCode.F10 && e.shiftKey && !e.ctrlKey && !e.altKey);

        static MenuItem Item(string id, string key, Action run, bool disabled = false, bool check = false) =>
            new MenuItem { Id = id, Text = L10n.Get(key), Run = run, Disabled = disabled, Checked = check };

        /// <summary>The menu of a file row: favourite, details, Explorer, copy the path, delete (to the recycle bin).</summary>
        public static List<MenuItem> FileItems(UiContext ctx, BrowserFile f)
        {
            var store = Favorites.Of(ctx);
            bool fav = store.IsFile(f.RootId, f.RelPath);
            return new List<MenuItem>
            {
                Item("list-favorite", "list.menu.favorite", () => store.SetFile(f.RootId, f.RelPath, !fav), false, fav),
                MenuItem.Sep(),
                Item("list-details", "list.menu.details", () => FileDetails(ctx, f)),
                Item("list-reveal", "list.menu.reveal", () => Reveal(f.FullPath), !File.Exists(f.FullPath)),
                Item("list-copy-path", "list.menu.copyPath", () => Copy(ctx, f.FullPath)),
                MenuItem.Sep(),
                Item("list-delete", "list.menu.delete", () => AskDelete(ctx, f)),
            };
        }

        /// <summary>The menu of a catalog row: favourite, details, Explorer (the zipmod or the abdata bundle), copy the path and the id.</summary>
        public static List<MenuItem> RowItems(UiContext ctx, CatalogRow r)
        {
            var store = Favorites.Of(ctx);
            bool fav = store.IsRow(r.Kind, r.Ref);
            string src = SourcePath(ctx, r);
            return new List<MenuItem>
            {
                Item("list-favorite", "list.menu.favorite", () => store.SetRow(r.Kind, r.Ref, !fav), false, fav),
                MenuItem.Sep(),
                Item("list-details", "list.menu.details", () => RowDetails(ctx, r)),
                Item("list-reveal", "list.menu.reveal", () => Reveal(src), src == null || !File.Exists(src)),
                Item("list-copy-path", "list.menu.copyPath", () => Copy(ctx, src), src == null),
                Item("list-copy-id", "list.menu.copyId", () => Copy(ctx, r.Ref)),
            };
        }

        /// <summary>The menu of a Maker part cell: favourite and copy the id.</summary>
        public static List<MenuItem> PartItems(UiContext ctx, string kind, string reference)
        {
            var store = Favorites.Of(ctx);
            bool fav = store.IsRow(kind, reference);
            return new List<MenuItem>
            {
                Item("list-favorite", "list.menu.favorite", () => store.SetRow(kind, reference, !fav), false, fav),
                MenuItem.Sep(),
                Item("list-copy-id", "list.menu.copyId", () => Copy(ctx, reference)),
            };
        }

        public static void ShowPart(UiContext ctx, string kind, string reference, Vector2 at, string name = "partlist-context")
        {
            if (ctx == null || reference == null) return;
            Show(ctx, PartItems(ctx, kind, reference), at, name);
        }

        public static void ShowFile(UiContext ctx, BrowserFile f, Vector2 at, string name = "list-context")
        {
            if (ctx == null || f == null) return;
            Show(ctx, FileItems(ctx, f), at, name);
        }

        public static void ShowRow(UiContext ctx, CatalogRow r, Vector2 at, string name = "list-context")
        {
            if (ctx == null || r?.Ref == null) return;
            Show(ctx, RowItems(ctx, r), at, name);
        }

        static void Show(UiContext ctx, List<MenuItem> items, Vector2 at, string name)
        {
            LastItems = items;
            LastMenuName = name;
            lastMenus = ctx.Shell?.Menus;
            if (ctx.Shell?.Menus == null) return;
            // the dialog layer lies above the menus' overlay: a menu of a browser inside a dialog is brought above it
            if (ctx.Dialogs != null && ctx.Dialogs.IsModalOpen) ctx.Shell.Overlay?.BringToFront();
            ctx.Shell.Menus.Show(items, null, at, name);
        }

        static PopupMenus lastMenus;

        /// <summary>Runs the item <paramref name="id"/> of the last menu (tests; as a click on it does: the menu closes first).</summary>
        public static bool Run(string id)
        {
            var it = LastItems?.FirstOrDefault(i => i.Id == id);
            if (it == null || it.Disabled) return false;
            lastMenus?.CloseAll();
            it.Run?.Invoke();
            return true;
        }

        // ---------------------------------------------------------------- paths, clipboard, Explorer

        /// <summary>The file a catalog row comes from: its zipmod, else the abdata bundle of a base-game row (null = not known).</summary>
        public static string SourcePath(UiContext ctx, CatalogRow r)
        {
            if (r?.Zipmod?.Path != null) return r.Zipmod.Path;
            return AbdataPath(ctx, r?.Record as IStudioAsset);
        }

        static string AbdataPath(UiContext ctx, IStudioAsset a)
        {
            string root = ctx?.App?.Paths?.Hs2Root;
            if (a == null || string.IsNullOrEmpty(a.BundlePath) || root == null) return null;
            try { return Path.Combine(root, "abdata", a.BundlePath.Replace('/', Path.DirectorySeparatorChar)); }
            catch (Exception) { return null; }
        }

        public static void Copy(UiContext ctx, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            LastCopied = text;
            try { GUIUtility.systemCopyBuffer = text; }
            catch (Exception e) { Debug.LogWarning("[ui] copy failed: " + e.Message); }
            ctx?.Toasts?.Show(L10n.Format("list.copied", text), ToastKind.Info, 3f);
        }

        /// <summary>Opens Explorer with the file selected.</summary>
        public static void Reveal(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            if (TestReveal != null) { TestReveal(path); return; }
            try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + path + "\""); }
            catch (Exception e) { Debug.LogWarning("[ui] reveal failed: " + e.Message); }
        }

        // ---------------------------------------------------------------- details

        static string Date(DateTime utc) => utc.Ticks <= 0 || utc.Year < 1980 ? "—" : utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

        static string Size(long bytes) =>
            bytes < 1024 * 1024 ? (bytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KB (" + bytes.ToString("N0", CultureInfo.InvariantCulture) + " B)"
                : (bytes / (1024.0 * 1024.0)).ToString("0.00", CultureInfo.InvariantCulture) + " MB (" + bytes.ToString("N0", CultureInfo.InvariantCulture) + " B)";

        static string SexText(int sex) => L10n.Get(sex == 0 ? "list.sex.male" : sex == 1 ? "list.sex.female" : "list.sex.unknown");

        static string KindKey(BrowserFile f)
        {
            string p = f.FullPath ?? "";
            bool own = p.EndsWith(SaveFormats.CharaExtension, StringComparison.OrdinalIgnoreCase) || p.EndsWith(SaveFormats.CoordExtension, StringComparison.OrdinalIgnoreCase)
                || p.EndsWith(SaveFormats.PoseExtension, StringComparison.OrdinalIgnoreCase);
            switch (f.Kind)
            {
                case BrowserFileKind.Chara: return own ? "list.kind.hs2nChara" : "list.kind.hs2Card";
                case BrowserFileKind.Coordinate: return own ? "list.kind.hs2nCoord" : "list.kind.hs2Coord";
                default: return own ? "list.kind.hs2nPose" : "list.kind.hs2Pose";
            }
        }

        static void Line(StringBuilder sb, string key, string value) => sb.Append(L10n.Get(key)).Append(": ").Append(string.IsNullOrEmpty(value) ? "—" : value).Append('\n');

        /// <summary>The details of a file row; <paramref name="extra"/> = the lines read from the file (null = still reading).</summary>
        public static string FileDetailsText(BrowserFile f, FileExtras extra)
        {
            var sb = new StringBuilder();
            Line(sb, "list.d.file", f.FileName);
            if (f.Kind == BrowserFileKind.Pose) Line(sb, "list.d.poseName", f.CharaName);
            else if (f.Kind == BrowserFileKind.Chara) Line(sb, "list.d.name", extra?.Name ?? f.CharaName);
            Line(sb, "list.d.sex", SexText(f.Sex >= 0 ? f.Sex : extra?.Sex ?? -1));
            Line(sb, "list.d.kind", L10n.Get(KindKey(f)));
            Line(sb, "list.d.path", f.FullPath);
            Line(sb, "list.d.size", Size(f.Size));
            Line(sb, "list.d.created", Date(f.CreatedUtc));
            Line(sb, "list.d.modified", Date(f.ModifiedUtc));
            if (f.Kind != BrowserFileKind.Pose)
            {
                if (extra == null) sb.Append(L10n.Get("list.d.reading")).Append('\n');
                else
                {
                    Line(sb, "list.d.plugins", extra.Plugins.Count == 0 ? L10n.Get("list.d.none") : string.Join(", ", extra.Plugins));
                    Line(sb, "list.d.missing", extra.Missing.Count == 0 ? L10n.Get("list.d.none") : string.Join(", ", extra.Missing));
                    if (extra.Problem != null) Line(sb, "list.d.problem", extra.Problem);
                }
            }
            return sb.ToString().TrimEnd('\n');
        }

        /// <summary>What the details read from a character or coordinate file: the name and sex, the plugin data it carries and
        /// the mods it needs that are not installed.</summary>
        public sealed class FileExtras
        {
            public string Name;
            public int Sex = -1;
            public readonly List<string> Plugins = new List<string>();
            public readonly List<string> Missing = new List<string>();
            public string Problem;
        }

        /// <summary>Reads the plugin data names, the needed mods and the name of a file (a worker; never throws). HS2 cards: the
        /// plugin data block and its mod references (the image is not decoded); HS2Next files: the manifest.</summary>
        public static FileExtras ReadExtras(BrowserFile f, HashSet<string> installedGuids)
        {
            var x = new FileExtras();
            try
            {
                string p = f.FullPath;
                var needed = new List<(string guid, string name)>();
                if (p.EndsWith(SaveFormats.CharaExtension, StringComparison.OrdinalIgnoreCase) || p.EndsWith(SaveFormats.CoordExtension, StringComparison.OrdinalIgnoreCase))
                {
                    var r = ContainerReader.Read(p, null, n => n == EntryNames.Manifest);
                    var m = r.ReadManifest();
                    x.Name = m.Title;
                    x.Sex = m.Sex ?? -1;
                    foreach (var e in m.Ext) if (!string.IsNullOrEmpty(e.Ns) && !x.Plugins.Contains(e.Ns)) x.Plugins.Add(e.Ns);
                    foreach (var q in m.Requires) if (!string.IsNullOrEmpty(q.Guid)) needed.Add((q.Guid, q.Name));
                }
                else if (f.Kind == BrowserFileKind.Chara)
                {
                    var c = HS2Next.Data.Card.Load(p);
                    x.Name = c.Name;
                    var h = CardHeaderReader.Read(p);
                    x.Sex = h.Sex;
                    AddPlugins(x, c.KKEx);
                    foreach (var r in c.Resolve) if (!string.IsNullOrEmpty(r.GUID)) needed.Add((r.GUID, r.Name));
                }
                else if (f.Kind == BrowserFileKind.Coordinate)
                {
                    var c = HS2Next.Data.Ext.CoordCard.Load(p);
                    AddPlugins(x, c.KKEx);
                    foreach (var r in c.Resolve) if (!string.IsNullOrEmpty(r.GUID)) needed.Add((r.GUID, r.Name));
                }
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (guid, name) in needed)
                {
                    if (installedGuids != null && installedGuids.Contains(guid)) continue;
                    if (!seen.Add(guid)) continue;
                    x.Missing.Add(string.IsNullOrEmpty(name) ? guid : guid + " (" + name + ")");
                }
                x.Plugins.Sort(StringComparer.OrdinalIgnoreCase);
                x.Missing.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception e) { x.Problem = e.GetType().Name + ": " + e.Message; }
            return x;
        }

        static void AddPlugins(FileExtras x, object kkex)
        {
            if (kkex is IDictionary<object, object> d)
                foreach (var k in d.Keys)
                    if (k is string s && s.Length > 0 && !x.Plugins.Contains(s)) x.Plugins.Add(s);
        }

        /// <summary>The details dialog of a file row (selectable text; «すべてコピー» copies it all). The plugin data and the needed
        /// mods are read off the main thread and filled in when known.</summary>
        public static void FileDetails(UiContext ctx, BrowserFile f)
        {
            if (ctx == null || f == null) return;
            var label = DetailsLabel(FileDetailsText(f, null));
            if (f.Kind != BrowserFileKind.Pose)
            {
                var installed = new HashSet<string>(ctx.App?.Catalog?.ZipmodsByGuid?.Keys ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
                var task = Task.Run(() => ReadExtras(f, installed));
                Action poll = null;
                poll = () =>
                {
                    if (!task.IsCompleted) return;
                    PanelTicker.Remove(poll);
                    label.text = FileDetailsText(f, task.IsFaulted ? new FileExtras { Problem = task.Exception?.GetBaseException().Message } : task.Result);
                    LastDetails = label.text;
                };
                PanelKit.EveryFrame(label, poll);
            }
            ShowDetails(ctx, label);
        }

        /// <summary>The details of a catalog row.</summary>
        public static string RowDetailsText(UiContext ctx, CatalogRow r)
        {
            var sb = new StringBuilder();
            Line(sb, "list.d.rowName", r.Name);
            Line(sb, "list.d.id", r.Ref);
            if (r.Group != null) Line(sb, "list.d.group", r.Group);
            if (r.Category != null) Line(sb, "list.d.category", r.Category);
            var z = r.Zipmod;
            if (z != null)
            {
                Line(sb, "list.d.source", z.Path);
                Line(sb, "list.d.guid", z.Guid);
                Line(sb, "list.d.modName", z.Name);
                Line(sb, "list.d.version", z.Version);
                Line(sb, "list.d.author", z.Author);
                Line(sb, "list.d.website", z.Website);
                Line(sb, "list.d.zipmodCreated", ZipmodDates.Known(z) ? Date(new DateTime(ZipmodDates.Ticks(z), DateTimeKind.Utc)) : FileDate(z.Path));
            }
            else Line(sb, "list.d.source", L10n.Get("list.d.baseGame") + " · " + (AbdataPath(ctx, r.Record as IStudioAsset) ?? "—"));
            if (r.Record is IStudioAsset a)
            {
                Line(sb, "list.d.bundle", a.BundlePath);
                Line(sb, "list.d.asset", a.AssetName);
            }
            return sb.ToString().TrimEnd('\n');
        }

        static string FileDate(string path)
        {
            try { return path != null && File.Exists(path) ? Date(File.GetCreationTimeUtc(path)) : "—"; }
            catch (Exception) { return "—"; }
        }

        public static void RowDetails(UiContext ctx, CatalogRow r)
        {
            if (ctx == null || r == null) return;
            ShowDetails(ctx, DetailsLabel(RowDetailsText(ctx, r)));
        }

        static Label DetailsLabel(string text)
        {
            var l = new Label(text) { name = "list-details-text" };
            l.AddToClassList("hs-list-details__text");
            l.selection.isSelectable = true;
            l.style.whiteSpace = WhiteSpace.Normal;
            LastDetails = text;
            return l;
        }

        static void ShowDetails(UiContext ctx, Label label)
        {
            var scroll = new ScrollView(ScrollViewMode.Vertical) { name = "list-details-scroll" };
            scroll.style.maxHeight = 420;
            scroll.style.minWidth = 420;
            scroll.Add(label);
            var spec = new DialogSpec { Id = "list-details", Title = L10n.Get("list.details.title"), Content = scroll };
            spec.Buttons.Add(DialogButton.Of("copy", "list.details.copy"));
            spec.Buttons.Add(DialogButton.Of("close", "common.close", true, true));
            spec.CanClose = id => { if (id == "copy") { Copy(ctx, label.text); return false; } return true; };
            SheetDialog.Show(ctx, spec, null);
        }

        // ---------------------------------------------------------------- delete

        /// <summary>The delete confirmation: names the file (an HS2 file also leaves HS2), then the recycle bin.</summary>
        public static void AskDelete(UiContext ctx, BrowserFile f)
        {
            if (ctx == null || f == null) return;
            string msg = L10n.Format(f.FromHs2 ? "list.delete.messageHs2" : "list.delete.message", f.FileName);
            var spec = new DialogSpec { Id = "list-delete", Title = L10n.Get("list.delete.title"), Message = msg };
            spec.Buttons.Add(DialogButton.Of("delete", "list.delete.button", true));
            spec.Buttons.Add(DialogButton.Cancel());
            SheetDialog.Show(ctx, spec, id => { if (id == "delete") BrowserFiles.Delete(ctx, f); });
        }

        /// <summary>Presses a button of the open confirmation or details (the sheet above a dialog, else the app's dialog; tests).</summary>
        public static void PressDialog(UiContext ctx, string id)
        {
            if (SheetDialog.Current != null) SheetDialog.Current.Press(id);
            else ctx?.Dialogs?.Press(id);
        }
    }

    /// <summary>
    /// A dialog through the app's dialogs when none is open, else a sheet above the open modal dialog (Dialogs queue a second
    /// one behind the first): the same box and classes in its own layer at the top of the UI root, buttons named
    /// "sheet-button-&lt;id&gt;"; Enter presses the default button and Esc the cancel button while the focus is in the sheet.
    /// </summary>
    public sealed class SheetDialog
    {
        /// <summary>The sheet open now (null = none).</summary>
        public static SheetDialog Current { get; private set; }

        public DialogSpec Spec { get; }
        /// <summary>The box (named "sheet-&lt;id&gt;").</summary>
        public VisualElement Element { get; private set; }
        public bool IsOpen => layer != null;
        readonly Action<string> onClose;
        VisualElement layer;

        SheetDialog(DialogSpec spec, Action<string> onClose) { Spec = spec; this.onClose = onClose; }

        /// <summary>Shows <paramref name="spec"/>: an app dialog when no modal dialog is open, else a sheet above it.</summary>
        public static void Show(UiContext ctx, DialogSpec spec, Action<string> onClose)
        {
            if (ctx?.Dialogs == null || spec == null) return;
            var root = ctx.Shell?.Root;
            if (!ctx.Dialogs.IsModalOpen || root == null) { ctx.Dialogs.Show(spec, onClose); return; }
            Current?.Close("closed");
            if (spec.Buttons.Count == 0) spec.Buttons.Add(DialogButton.Ok());
            var s = new SheetDialog(spec, onClose);
            s.Open(root);
            Current = s;
        }

        void Open(VisualElement root)
        {
            layer = new VisualElement { name = "sheet-layer" };
            layer.AddToClassList(Dialogs.LayerClass);
            layer.pickingMode = PickingMode.Position;
            layer.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
            var box = new VisualElement { name = "sheet-" + Spec.Id };
            box.AddToClassList(Dialogs.DialogClass);
            if (!string.IsNullOrEmpty(Spec.BoxClass)) box.AddToClassList(Spec.BoxClass);
            if (!string.IsNullOrEmpty(Spec.Title)) { var t = new Label(Spec.Title); t.AddToClassList("hs-dialog__title"); box.Add(t); }
            if (!string.IsNullOrEmpty(Spec.Message)) { var m = new Label(Spec.Message) { name = "sheet-message" }; m.AddToClassList("hs-dialog__message"); box.Add(m); }
            if (Spec.Content != null) { Spec.Content.AddToClassList(Dialogs.ContentClass); box.Add(Spec.Content); }
            var row = new VisualElement();
            row.AddToClassList("hs-dialog__buttons");
            Button first = null, def = null;
            foreach (var b in Spec.Buttons)
            {
                string bid = b.Id;
                var btn = new Button(() => Press(bid)) { name = "sheet-button-" + bid, text = b.Text ?? L10n.Get(b.LabelKey) };
                btn.AddToClassList("hs-dialog__button");
                if (b.IsDefault) { btn.AddToClassList("hs-dialog__button--default"); def = def ?? btn; }
                first = first ?? btn;
                row.Add(btn);
            }
            box.Add(row);
            box.RegisterCallback<KeyDownEvent>(OnKey);
            Element = box;
            layer.Add(box);
            root.Add(layer);
            layer.BringToFront();
            var focus = def ?? first;
            UiDefer.Frames(2, () => { if (IsOpen) focus?.Focus(); });
        }

        void OnKey(KeyDownEvent e)
        {
            string id = null;
            if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                id = (Spec.Buttons.FirstOrDefault(b => b.IsDefault) ?? Spec.Buttons[0]).Id;
            else if (e.keyCode == KeyCode.Escape)
                id = Spec.Buttons.FirstOrDefault(b => b.IsCancel)?.Id ?? "cancel";
            if (id == null) return;
            e.StopPropagation();
            Element?.panel?.focusController?.IgnoreEvent(e);
            Press(id);
        }

        /// <summary>Presses a button (as a click does): closes unless the spec's CanClose keeps it open.</summary>
        public void Press(string id)
        {
            if (!IsOpen) return;
            if (Spec.CanClose != null && !Spec.CanClose(id)) return;
            Close(id);
        }

        void Close(string id)
        {
            if (!IsOpen) return;
            layer.RemoveFromHierarchy();
            layer = null;
            if (Current == this) Current = null;
            try { onClose?.Invoke(id); }
            catch (Exception e) { Debug.LogError("[ui] sheet " + Spec.Id + " callback threw: " + e); }
        }
    }
}
