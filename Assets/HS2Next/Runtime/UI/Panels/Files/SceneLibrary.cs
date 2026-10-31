// The scene browser's files: every *.hs2nscene under <UserData>/scene recursively with its
// manifest (title, created / modified time, the characters it holds) read without the documents (ContainerReader inflates
// only manifest.json); the thumbnail is the PNG prefix of the file (5:7), decoded by the ThumbnailLoader.
// Runs off the main thread. Unity-free.
using System;
using System.Collections.Generic;
using System.IO;
using HS2Next.Save;

namespace HS2Next.UI.Panels
{
    public sealed class SceneFileEntry
    {
        public string FullPath;
        /// <summary>Relative to the scene folder, '/' separators, with the extension (what scene.load's path takes).</summary>
        public string RelativePath;
        public string FileName;
        public long Size;
        public DateTime FileModifiedUtc;
        /// <summary>Manifest values (null / default when the manifest is unreadable).</summary>
        public string Title;
        public DateTime CreatedUtc, ModifiedUtc;
        public int Characters;
        public int? ThumbWidth, ThumbHeight;
        /// <summary>Why the manifest could not be read, or null.</summary>
        public string Problem;

        /// <summary>Title or file name.</summary>
        public string DisplayName => string.IsNullOrEmpty(Title) ? FileName : Title;
        public string SearchText => FileName + " " + (Title ?? "");
        /// <summary>The time the browser sorts and shows: the manifest's modified time, else the file's.</summary>
        public DateTime When => ModifiedUtc != default ? ModifiedUtc : FileModifiedUtc;
    }

    public static class SceneLibrary
    {
        /// <summary>Lists the scene files under <paramref name="sceneDir"/> (missing folder = empty list).</summary>
        public static List<SceneFileEntry> Scan(string sceneDir, List<string> problems = null)
        {
            var l = new List<SceneFileEntry>();
            if (string.IsNullOrEmpty(sceneDir) || !Directory.Exists(sceneDir)) return l;
            string root = Path.GetFullPath(sceneDir).TrimEnd('\\', '/');
            string[] files;
            try { files = Directory.GetFiles(root, "*" + SaveFormats.SceneExtension, SearchOption.AllDirectories); }
            catch (Exception e) { problems?.Add(e.Message); return l; }
            Array.Sort(files, StringComparer.Ordinal);
            foreach (var f in files)
            {
                if (!f.EndsWith(SaveFormats.SceneExtension, StringComparison.OrdinalIgnoreCase)) continue;
                l.Add(Read(f, root));
            }
            return l;
        }

        /// <summary>One entry (file facts + manifest).</summary>
        public static SceneFileEntry Read(string fullPath, string root)
        {
            var e = new SceneFileEntry
            {
                FullPath = fullPath,
                FileName = Path.GetFileNameWithoutExtension(fullPath),
                RelativePath = root != null && fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                    ? fullPath.Substring(root.Length).TrimStart('\\', '/').Replace('\\', '/') : Path.GetFileName(fullPath),
            };
            try
            {
                var fi = new FileInfo(fullPath);
                e.Size = fi.Length;
                e.FileModifiedUtc = fi.LastWriteTimeUtc;
                var r = ContainerReader.Read(fullPath, null, n => n == EntryNames.Manifest);
                var m = r.ReadManifest();
                e.Title = m.Title;
                e.CreatedUtc = m.CreatedUtc;
                e.ModifiedUtc = m.ModifiedUtc;
                e.Characters = m.Charas?.Count ?? 0;
                e.ThumbWidth = r.ThumbnailHeader?.Width;
                e.ThumbHeight = r.ThumbnailHeader?.Height;
            }
            catch (Exception ex) { e.Problem = ex.GetType().Name + ": " + ex.Message; }
            return e;
        }

        /// <summary>The default Save As name: yyyy_MMdd_HHmm_ss (local time).</summary>
        public static string DefaultName(DateTime local) => local.ToString("yyyy_MMdd_HHmm_ss", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>
        /// The absolute path for a Save As name typed by the user: trimmed, the extension appended unless present, kept under
        /// <paramref name="sceneDir"/> ('/' or '\' make subfolders; "..", rooted names and invalid characters are refused → null).
        /// </summary>
        public static string ResolveSaveName(string sceneDir, string name, out string problem)
        {
            problem = null;
            name = (name ?? "").Trim();
            if (name.Length == 0) { problem = "empty"; return null; }
            if (!name.EndsWith(SaveFormats.SceneExtension, StringComparison.OrdinalIgnoreCase)) name += SaveFormats.SceneExtension;
            if (Path.IsPathRooted(name)) { problem = "rooted"; return null; }
            foreach (var part in name.Split('/', '\\'))
            {
                if (part.Length == 0 || part == "." || part == "..") { problem = "segment"; return null; }
                if (part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) { problem = "chars"; return null; }
            }
            string root = Path.GetFullPath(sceneDir);
            string full = Path.GetFullPath(Path.Combine(root, name));
            if (!full.StartsWith(root.TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) { problem = "outside"; return null; }
            return full;
        }
    }
}
