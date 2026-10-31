// Widgets of the first-run guide, also used by the Options panel's Folders section: the in-app folder picker (drives, the
// folders of the current one, a path field, Up; no OS dialog, so keyboard focus and fullscreen keep working) shown as a modal
// dialog, and the HS2 folder box (the suggestions, a path field, Browse…, the install check's line updated on every change, the
// zipmod count on a worker thread, "Did you mean …?").
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HS2Next.App.Setup;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;
using UnityEngine.UIElements;

namespace HS2Next.UI.Setup
{
    /// <summary>The in-app folder picker (the content of a modal dialog).</summary>
    public sealed class FolderPicker : VisualElement
    {
        public const string DialogId = "folder-picker";
        /// <summary>At most this many subfolders are listed (a note says when more exist).</summary>
        public const int MaxItems = 2000;

        readonly TextField path;
        readonly ScrollView list;
        readonly Label status;
        public string Current { get; private set; }

        FolderPicker(string start)
        {
            name = "folderpicker";
            AddToClassList("hs-setup-picker");
            var drives = new VisualElement { name = "folderpicker.drives" };
            drives.AddToClassList("hs-setup-row");
            int n = 0;
            foreach (var d in Drives())
            {
                string root = d;
                var b = new Button(() => Go(root)) { name = "folderpicker.drive." + (n++), text = root.TrimEnd('\\') };
                drives.Add(b);
            }
            Add(drives);
            var row = new VisualElement();
            row.AddToClassList("hs-setup-row");
            var up = Loc.Button("setup.picker.up", Up);
            up.name = "folderpicker.up";
            row.Add(up);
            path = new TextField { name = "folderpicker.path", isDelayed = true };
            path.AddToClassList("hs-grow");
            path.RegisterValueChangedCallback(e => Go(e.newValue));
            row.Add(path);
            Add(row);
            list = new ScrollView { name = "folderpicker.list" };
            list.AddToClassList("hs-setup-picker__list");
            Add(list);
            status = new Label { name = "folderpicker.status" };
            status.AddToClassList("hs-hint");
            Add(status);
            Go(string.IsNullOrEmpty(start) || !Directory.Exists(start) ? Drives().FirstOrDefault() : start);
        }

        /// <summary>The ready drives (fixed, removable, network).</summary>
        public static List<string> Drives()
        {
            var l = new List<string>();
            try
            {
                foreach (var d in DriveInfo.GetDrives())
                {
                    try { if (d.IsReady && (d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable || d.DriveType == DriveType.Network)) l.Add(d.RootDirectory.FullName); }
                    catch (Exception) { }
                }
            }
            catch (Exception) { }
            return l;
        }

        void Up()
        {
            if (string.IsNullOrEmpty(Current)) return;
            var p = Path.GetDirectoryName(Current.TrimEnd('\\'));
            if (!string.IsNullOrEmpty(p)) Go(p);
            else if (Current.Length > 3) Go(Path.GetPathRoot(Current));
        }

        /// <summary>Shows a folder (the path field and its subfolders); a folder that cannot be read keeps the last one.</summary>
        public void Go(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return;
            string full;
            try { full = Path.GetFullPath(dir.Trim().Trim('"')); }
            catch (Exception) { status.text = L10n.Get("setup.picker.unreadable"); return; }
            List<string> subs;
            try
            {
                subs = Directory.EnumerateDirectories(full).Where(d =>
                {
                    try { var a = File.GetAttributes(d); return (a & FileAttributes.Hidden) == 0 || (a & FileAttributes.System) == 0; }
                    catch (Exception) { return false; }
                }).Take(MaxItems + 1).ToList();
            }
            catch (Exception)
            {
                status.text = L10n.Get("setup.picker.unreadable");
                path.SetValueWithoutNotify(Current ?? "");
                return;
            }
            subs.Sort(StringComparer.OrdinalIgnoreCase);
            Current = full;
            path.SetValueWithoutNotify(full);
            list.Clear();
            int i = 0;
            foreach (var s in subs.Take(MaxItems))
            {
                string target = s;
                var b = new Button(() => Go(target)) { name = "folderpicker.item." + (i++), text = Path.GetFileName(s) };
                b.AddToClassList("hs-setup-picker__item");
                list.Add(b);
            }
            status.text = subs.Count > MaxItems ? L10n.Format("setup.picker.more", MaxItems) : subs.Count == 0 ? L10n.Get("setup.picker.empty") : "";
        }

        /// <summary>The picker inline (inside an open dialog, where a second modal dialog would wait for the first to close), with
        /// its own Select / Cancel buttons; it removes itself and calls <paramref name="done"/> with the folder or null.</summary>
        public static VisualElement Inline(string start, Action<string> done)
        {
            var box = new VisualElement { name = "folderpicker.inline" };
            box.AddToClassList("hs-setup-box");
            var p = new FolderPicker(start);
            p.style.minHeight = 260;
            box.Add(p);
            var row = new VisualElement();
            row.AddToClassList("hs-setup-row");
            void End(string r) { box.RemoveFromHierarchy(); done?.Invoke(r); }
            var sel = Loc.Button("setup.picker.select", () => End(p.Current));
            sel.name = "folderpicker.select";
            var cancel = Loc.Button("common.cancel", () => End(null));
            cancel.name = "folderpicker.cancel";
            row.Add(sel);
            row.Add(cancel);
            box.Add(row);
            return box;
        }

        /// <summary>Opens the picker as a modal dialog of <paramref name="dialogs"/>; <paramref name="done"/> gets the folder or null.</summary>
        public static FolderPicker Open(Dialogs dialogs, string titleKey, string start, Action<string> done)
        {
            if (dialogs == null) { done?.Invoke(null); return null; }
            var p = new FolderPicker(start);
            var spec = new DialogSpec { Id = DialogId, Title = L10n.Get(titleKey), Content = p, BoxClass = "hs-setup-picker-box", FocusOnOpen = p.path };
            spec.Buttons.Add(DialogButton.Of("select", "setup.picker.select", isDefault: true));
            spec.Buttons.Add(DialogButton.Cancel());
            dialogs.Show(spec, id => done?.Invoke(id == "select" ? p.Current : null));
            return p;
        }
    }

    /// <summary>The HS2 folder box: suggestions, the path, Browse…, the install check's line.</summary>
    public sealed class Hs2FolderBox : VisualElement
    {
        readonly Func<Dialogs> dialogs;
        readonly VisualElement suggestions;
        readonly Label suggestionsNote;
        public readonly TextField PathField;
        readonly Label status;
        readonly Button didYouMean;
        Task<SuggestionResult> gather;
        Task<KeyValuePair<string, int>> count;
        string countFor;
        int countResult = -1;

        /// <summary>The verdict on the current path (null before a path was given).</summary>
        public InstallVerdict Verdict { get; private set; }
        /// <summary>The zipmod count of the current folder (-1 while it is counted or unknown).</summary>
        public int Zipmods => Verdict != null && countFor == Verdict.Path ? countResult : -1;
        public event Action Changed;
        public IReadOnlyList<InstallSuggestion> Suggestions { get; private set; } = new List<InstallSuggestion>();

        public Hs2FolderBox(Func<Dialogs> dialogs, string current)
        {
            this.dialogs = dialogs;
            name = "setup.hs2";
            var sl = Loc.Label("setup.hs2.suggestions");
            sl.AddToClassList("hs-setup-subtitle");
            Add(sl);
            suggestions = new VisualElement { name = "setup.hs2.suggestions" };
            suggestions.AddToClassList("hs-setup-list");
            Add(suggestions);
            suggestionsNote = new Label { name = "setup.hs2.suggestions.note", text = L10n.Get("setup.hs2.searching") };
            suggestionsNote.AddToClassList("hs-hint");
            Add(suggestionsNote);
            var row = new VisualElement();
            row.AddToClassList("hs-setup-row");
            PathField = new TextField { name = "setup.hs2.path" };
            Loc.Bind(PathField, "setup.hs2.pathLabel", (e, t) => ((TextField)e).label = t);
            PathField.AddToClassList("hs-grow");
            PathField.RegisterValueChangedCallback(e => Validate(e.newValue));
            row.Add(PathField);
            Button browse = null;
            browse = Loc.Button("setup.browse", () =>
            {
                var dl = dialogs();
                string start = Verdict?.Path ?? PathField.value;
                if (dl != null && dl.IsModalOpen)
                {
                    // inside a dialog (the Options panel's HS2 folder): the picker opens inline below the path
                    if (this.Q("folderpicker.inline") != null) return;
                    Insert(IndexOf(row) + 1, FolderPicker.Inline(start, p => { if (p != null) PathField.value = p; }));
                }
                else FolderPicker.Open(dl, "setup.hs2.pickTitle", start, p => { if (p != null) PathField.value = p; });
            });
            browse.name = "setup.hs2.browse";
            row.Add(browse);
            Add(row);
            status = new Label { name = "setup.hs2.status" };
            status.AddToClassList("hs-setup-status");
            Add(status);
            didYouMean = new Button(() => { if (Verdict?.DidYouMean != null) PathField.value = Verdict.DidYouMean; }) { name = "setup.hs2.didyoumean" };
            didYouMean.style.display = DisplayStyle.None;
            Add(didYouMean);
            var note = Loc.Label("setup.hs2.readOnlyNote");
            note.AddToClassList("hs-hint");
            Add(note);
            schedule.Execute(Poll).Every(100);
            // a folder given at the start (the current hs2Root of a re-run guide, a resumed choice) is checked at once: the field is
            // not in a panel yet, so setting its value would send no change event and the verdict (and the guide's Next) would wait
            // for a keystroke
            PathField.SetValueWithoutNotify(current ?? "");
            Validate(current ?? "");
            gather = Task.Run(() => InstallSuggestions.Gather(current));
        }

        /// <summary>Re-runs the check (a path set from outside, a language switch).</summary>
        public void Validate(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                Verdict = null;
                status.text = L10n.Get("setup.hs2.status.none");
                SetLevel(null);
                didYouMean.style.display = DisplayStyle.None;
                Changed?.Invoke();
                return;
            }
            Verdict = Hs2InstallCheck.Check(text, countZipmods: false);
            if (Verdict.Usable && countFor != Verdict.Path)
            {
                countFor = Verdict.Path;
                countResult = -1;
                string folder = Verdict.Path;
                count = Task.Run(() => new KeyValuePair<string, int>(folder, Hs2InstallCheck.CountZipmods(Path.Combine(folder, "mods"))));
            }
            Refresh();
            Changed?.Invoke();
        }

        void Refresh()
        {
            var v = Verdict;
            if (v == null) { status.text = L10n.Get("setup.hs2.status.none"); return; }
            string line = L10n.Get("setup.hs2.status." + v.Code);
            if (v.Level == InstallLevel.Ok) line = L10n.Format("setup.hs2.status.ok", Zipmods >= 0 ? Zipmods.ToString() : "…");
            else if (v.Level == InstallLevel.Warning && Zipmods >= 0) line += " " + L10n.Format("setup.hs2.zipmods", Zipmods);
            status.text = line;
            SetLevel(v.LevelName);
            if (v.DidYouMean != null)
            {
                didYouMean.text = L10n.Format("setup.hs2.didYouMean", v.DidYouMean);
                didYouMean.style.display = DisplayStyle.Flex;
            }
            else didYouMean.style.display = DisplayStyle.None;
        }

        void SetLevel(string level)
        {
            foreach (var c in new[] { "ok", "warning", "error" }) status.EnableInClassList("hs-setup-status--" + c, c == level);
        }

        void Poll()
        {
            if (gather != null && gather.IsCompleted)
            {
                var r = gather.IsFaulted ? new SuggestionResult() : gather.Result;
                gather = null;
                Suggestions = r.Items;
                suggestions.Clear();
                int i = 0;
                foreach (var s in r.Items)
                {
                    var sug = s;
                    var b = new Button(() => PathField.value = sug.Path) { name = "setup.hs2.suggestion." + i, text = sug.Path + "  (" + L10n.Get("setup.hs2.from." + sug.From) + ")" };
                    b.AddToClassList("hs-setup-list__item");
                    suggestions.Add(b);
                    i++;
                }
                suggestionsNote.text = r.Items.Count == 0 ? L10n.Get("setup.hs2.noSuggestions") : "";
                suggestionsNote.style.display = r.Items.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
                Changed?.Invoke();
            }
            if (count != null && count.IsCompleted)
            {
                if (!count.IsFaulted && count.Result.Key == countFor) countResult = count.Result.Value;
                count = null;
                if (countFor != null && countResult < 0 && Verdict != null && Verdict.Usable && Verdict.Path == countFor)
                {
                    string folder = countFor;
                    count = Task.Run(() => new KeyValuePair<string, int>(folder, Hs2InstallCheck.CountZipmods(Path.Combine(folder, "mods"))));
                }
                Refresh();
                Changed?.Invoke();
            }
        }

        /// <summary>The suggestions are still being gathered.</summary>
        public bool Searching => gather != null;
    }
}
