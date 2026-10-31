// The files of the character browser: the HS2 install's card folder of one sex
// (`UserData/chara/female` or `male`, every *.png recursively; 74 % of the female cards sit in subfolders, so the browser
// shows a folder tree) and HS2Next's own `<UserData>/chara/**/*.hs2nchara` (sex from the manifest, read by the name index).
// The scan lists files and their size / mtime only (no file is opened); it runs off the main thread. Unity-free.
using System;
using System.Collections.Generic;
using System.IO;

namespace HS2Next.UI.Panels
{
    /// <summary>One card or character file of a browser.</summary>
    public sealed class CardEntry
    {
        /// <summary>Absolute path.</summary>
        public string FullPath;
        /// <summary>The file name without extension.</summary>
        public string FileName;
        /// <summary>The root this entry was found under (<see cref="CardLibrary.Roots"/> id).</summary>
        public string Root;
        /// <summary>Folder relative to its root ('/' separators, "" = the root itself).</summary>
        public string Folder;
        public long Size;
        public DateTime ModifiedUtc;
        /// <summary>True for HS2Next .hs2nchara files.</summary>
        public bool IsHs2n;

        // set by CardNameIndex (worker thread, then handed to the main thread by Drain)
        public volatile bool Indexed;
        internal volatile bool Queued;
        /// <summary>Character full name (or the manifest title); null until indexed or when unreadable.</summary>
        public volatile string CharaName;
        /// <summary>Sex in the card / manifest (0 male, 1 female, -1 unknown).</summary>
        public int HeaderSex = -1;
        /// <summary>The header could not be read (not a character card).</summary>
        public bool Unreadable;

        /// <summary>The name the browser shows and sorts by: the character's name once indexed, else the file name.</summary>
        public string DisplayName => string.IsNullOrEmpty(CharaName) ? FileName : CharaName;

        /// <summary>Search text: the file name and (once indexed) the character name.</summary>
        public string SearchText => string.IsNullOrEmpty(CharaName) ? FileName : FileName + " " + CharaName;

        /// <summary>The folder key of the folder tree ("root" or "root/sub/dir").</summary>
        public string FolderKey => Folder.Length == 0 ? Root : Root + "/" + Folder;

        public override string ToString() => FolderKey + "/" + FileName;
    }

    /// <summary>The scan result of one browser (one sex).</summary>
    public sealed class CardLibrary
    {
        public const string RootHs2 = "hs2", RootHs2Next = "hs2next";

        /// <summary>Roots in display order: (id, absolute folder) — missing folders are left out.</summary>
        public readonly List<(string id, string dir)> Roots = new List<(string, string)>();
        public readonly List<CardEntry> Entries = new List<CardEntry>();
        /// <summary>Every folder key that holds entries or has sub-folders with entries (ordinal order, roots first).</summary>
        public readonly List<string> Folders = new List<string>();
        public readonly List<string> Problems = new List<string>();
        public double ScanMs;

        /// <summary>
        /// Scans <paramref name="hs2CardDir"/> (*.png, recursive; may be null) and <paramref name="hs2nCharaDir"/> (*.hs2nchara,
        /// recursive; may be null). Entries are in ordinal path order.
        /// </summary>
        public static CardLibrary Scan(string hs2CardDir, string hs2nCharaDir)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var lib = new CardLibrary();
            lib.Add(RootHs2, hs2CardDir, "*.png", false);
            lib.Add(RootHs2Next, hs2nCharaDir, "*" + HS2Next.Save.SaveFormats.CharaExtension, true);
            var set = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var e in lib.Entries)
            {
                string k = e.FolderKey;
                while (true)
                {
                    set.Add(k);
                    int i = k.LastIndexOf('/');
                    if (i < 0) break;
                    k = k.Substring(0, i);
                }
            }
            foreach (var r in lib.Roots) set.Add(r.id);
            // roots first in Roots order, then the rest ordinally
            foreach (var r in lib.Roots) lib.Folders.Add(r.id);
            foreach (var f in set) if (f.IndexOf('/') >= 0) lib.Folders.Add(f);
            lib.ScanMs = sw.Elapsed.TotalMilliseconds;
            return lib;
        }

        void Add(string id, string dir, string pattern, bool hs2n)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
            string full = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            Roots.Add((id, full));
            var files = new List<string>();
            try { Walk(full, pattern, files); }
            catch (Exception e) { Problems.Add(id + ": " + e.Message); }
            files.Sort(StringComparer.Ordinal);
            string ext = pattern.Substring(1);
            foreach (var f in files)
            {
                if (!f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) continue;   // "*.png" also matches ".pngx" on Windows
                FileInfo fi;
                try { fi = new FileInfo(f); if (!fi.Exists) continue; }
                catch (Exception e) { Problems.Add(f + ": " + e.Message); continue; }
                string rel = Path.GetDirectoryName(f).Substring(full.Length).TrimStart('\\', '/').Replace('\\', '/');
                Entries.Add(new CardEntry
                {
                    FullPath = f, FileName = Path.GetFileNameWithoutExtension(f), Root = id, Folder = rel,
                    Size = fi.Length, ModifiedUtc = fi.LastWriteTimeUtc, IsHs2n = hs2n,
                });
            }
        }

        void Walk(string dir, string pattern, List<string> into)
        {
            try { into.AddRange(Directory.GetFiles(dir, pattern)); }
            catch (Exception e) { Problems.Add(dir + ": " + e.Message); return; }
            string[] subs;
            try { subs = Directory.GetDirectories(dir); }
            catch (Exception e) { Problems.Add(dir + ": " + e.Message); return; }
            foreach (var s in subs) Walk(s, pattern, into);
        }

        /// <summary>True when <paramref name="entry"/> lies in the folder <paramref name="folderKey"/> or below (null = all).</summary>
        public static bool InFolder(CardEntry entry, string folderKey)
        {
            if (string.IsNullOrEmpty(folderKey)) return true;
            string k = entry.FolderKey;
            return k.Length == folderKey.Length ? string.Equals(k, folderKey, StringComparison.Ordinal)
                : k.Length > folderKey.Length && k[folderKey.Length] == '/' && k.StartsWith(folderKey, StringComparison.Ordinal);
        }
    }
}
