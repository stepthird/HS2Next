// Pose library scan: recursive walk of a pose folder (Studio's is
// `UserData/studio/pose`), every `.dat` and `.png` (extension case-insensitive) read with PoseFile; other files are
// counted as ignored. A PNG without a pose payload is "not a pose" (counted, not an error). Entries carry the path
// relative to the root ('/' separators), container kind, status, sex, name and version; nothing is filtered by sex.
// Order: ordinal by relative path. Unreadable directories are listed in Problems. Never throws for file contents.
// Unity-free (System only).
using System;
using System.Collections.Generic;
using System.IO;

namespace HS2Next.Import.Pose
{
    public enum PoseScanStatus
    {
        /// <summary>A readable pose.</summary>
        Pose,
        /// <summary>A PNG without a pose payload (a plain image).</summary>
        NotAPose,
        /// <summary>A `.dat` / `.png` that failed to read (see Error).</summary>
        Error,
    }

    public sealed class PoseLibraryEntry
    {
        /// <summary>Path relative to the scan root, '/' separators.</summary>
        public string RelativePath;
        public string FullPath;
        /// <summary>Container by extension (`.dat` → Dat, `.png` → Png).</summary>
        public PoseContainerKind Kind;
        public PoseScanStatus Status;
        public long Size;
        /// <summary>Header fields of a readable pose (else 0 / null / 0).</summary>
        public int Sex;
        public string Name;
        public int Version;
        public bool HasTrailer;
        public PoseFileFlags Flags;
        /// <summary>The error of a NotAPose / Error entry.</summary>
        public PoseFormatException Error;

        /// <summary>Reads the pose again (full parse).</summary>
        public PoseFile Load() => PoseFile.ReadFile(FullPath);

        public override string ToString() => $"{RelativePath} [{Kind}] {Status}" + (Status == PoseScanStatus.Pose ? $" sex {Sex} \"{Name}\"" : " " + Error?.Message);
    }

    public sealed class PoseLibraryScan
    {
        public string Root;
        public readonly List<PoseLibraryEntry> Entries = new List<PoseLibraryEntry>();
        public int DatFiles, PngFiles, Poses, DatPoses, PngPoses, NotAPose, Errors, IgnoredFiles, Directories;
        /// <summary>Directories that could not be listed, and similar scan-level notes.</summary>
        public readonly List<string> Problems = new List<string>();
    }

    public static class PoseLibrary
    {
        /// <summary>
        /// Scans <paramref name="root"/> recursively. <paramref name="visit"/> (optional) receives every `.dat`/`.png`
        /// entry with the file bytes and the parsed pose (null unless Status is Pose); the corpus tests use it to avoid
        /// a second read.
        /// </summary>
        public static PoseLibraryScan Scan(string root, Action<PoseLibraryEntry, byte[], PoseFile> visit = null)
        {
            var scan = new PoseLibraryScan { Root = root };
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) { scan.Problems.Add("root does not exist"); return scan; }
            string full = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var files = new List<string>();
            Walk(full, full, files, scan);
            var rel = new List<KeyValuePair<string, string>>(files.Count);
            foreach (var f in files) rel.Add(new KeyValuePair<string, string>(f.Substring(full.Length).TrimStart('\\', '/').Replace('\\', '/'), f));
            rel.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            foreach (var kv in rel)
            {
                string ext = Path.GetExtension(kv.Value);
                bool dat = string.Equals(ext, ".dat", StringComparison.OrdinalIgnoreCase);
                bool png = string.Equals(ext, ".png", StringComparison.OrdinalIgnoreCase);
                if (!dat && !png) { scan.IgnoredFiles++; continue; }
                if (dat) scan.DatFiles++; else scan.PngFiles++;
                var e = new PoseLibraryEntry { RelativePath = kv.Key, FullPath = kv.Value, Kind = dat ? PoseContainerKind.Dat : PoseContainerKind.Png };
                byte[] bytes = null;
                PoseFile pose = null;
                try
                {
                    bytes = File.ReadAllBytes(kv.Value);
                    e.Size = bytes.Length;
                    pose = PoseFile.Read(bytes);
                }
                catch (PoseFormatException x) { e.Error = x; }
                catch (Exception x) when (x is IOException || x is UnauthorizedAccessException || x is NotSupportedException || x is System.Security.SecurityException)
                {
                    e.Error = new PoseFormatException(PoseErrorCode.Io, -1, x.GetType().Name + ": " + x.Message, x);
                }
                if (pose != null)
                {
                    e.Status = PoseScanStatus.Pose;
                    e.Sex = pose.Sex;
                    e.Name = pose.Name;
                    e.Version = pose.Version;
                    e.HasTrailer = pose.Trailer != null;
                    e.Flags = pose.Flags;
                    scan.Poses++;
                    if (pose.Kind == PoseContainerKind.Png) scan.PngPoses++; else scan.DatPoses++;
                }
                else if (png && e.Error != null && e.Error.Code == PoseErrorCode.NoPayload)
                {
                    e.Status = PoseScanStatus.NotAPose;
                    scan.NotAPose++;
                }
                else
                {
                    e.Status = PoseScanStatus.Error;
                    scan.Errors++;
                }
                scan.Entries.Add(e);
                visit?.Invoke(e, bytes, pose);
            }
            return scan;
        }

        static void Walk(string root, string dir, List<string> files, PoseLibraryScan scan)
        {
            scan.Directories++;
            try
            {
                foreach (var f in Directory.GetFiles(dir)) files.Add(f);
                foreach (var d in Directory.GetDirectories(dir))
                {
                    FileAttributes a;
                    try { a = File.GetAttributes(d); } catch (Exception) { continue; }
                    if ((a & FileAttributes.ReparsePoint) != 0) { scan.Problems.Add("skipped a linked directory: " + Rel(root, d)); continue; }
                    Walk(root, d, files, scan);
                }
            }
            catch (Exception x) when (x is IOException || x is UnauthorizedAccessException || x is System.Security.SecurityException)
            {
                scan.Problems.Add("cannot list " + (dir.Length > root.Length ? Rel(root, dir) : "the root") + ": " + x.GetType().Name);
            }
        }

        static string Rel(string root, string path) => path.Substring(root.Length).TrimStart('\\', '/').Replace('\\', '/');
    }
}
