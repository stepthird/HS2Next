// Whether a folder is a Honey Select 2 install HS2Next can read (Unity-free, System.IO only; the first-run guide's HS2 page,
// app.setup, the setup stage's decision). Levels: error (the folder cannot be read; no abdata\abdata bundle manifest; no base
// character data abdata\chara\oo_base.unity3d or no list bundle in abdata\list\characustom), warning (all of that present but
// neither HoneySelect2_Data nor StudioNEOV2_Data: another game of the same engine family may share the layout), ok. A picked
// abdata folder or an exe file stands for its parent folder; when a picked folder fails but exactly one direct subfolder
// passes, that subfolder is proposed. The install is only read. The zipmod count (*.zipmod under <folder>\mods, recursive) is
// a separate call so that a page can run it on a worker.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace HS2Next.App.Setup
{
    public enum InstallLevel { Error = 0, Warning = 1, Ok = 2 }

    /// <summary>The verdict on one folder.</summary>
    public sealed class InstallVerdict
    {
        /// <summary>The text as given.</summary>
        public string Input;
        /// <summary>The folder checked (normalised: full path, no trailing separator; an abdata folder or an exe → its parent).</summary>
        public string Path;
        public InstallLevel Level;
        /// <summary>unreadable | noAbdata | incomplete | notHs2 | ok (the string table key is "setup.hs2.status." + Code).</summary>
        public string Code;
        /// <summary>The line in English (the bridge reply; the UI shows the localised line of <see cref="Code"/>).</summary>
        public string Message;
        /// <summary>*.zipmod files under &lt;Path&gt;\mods, or -1 when not counted.</summary>
        public int Zipmods = -1;
        /// <summary>The one direct subfolder that passes when this folder fails, else null.</summary>
        public string DidYouMean;
        /// <summary>Time of the check in milliseconds (without the zipmod count).</summary>
        public double Ms;

        public bool Usable => Level != InstallLevel.Error;
        public string LevelName => Level == InstallLevel.Ok ? "ok" : Level == InstallLevel.Warning ? "warning" : "error";
    }

    public static class Hs2InstallCheck
    {
        public static readonly string[] DataFolders = { "HoneySelect2_Data", "StudioNEOV2_Data" };
        /// <summary>How many direct subfolders the "did you mean" search looks at, at most.</summary>
        public const int MaxSubfolders = 64;

        /// <summary>The folder a picked path stands for (null for an empty text): quotes and blanks trimmed, full path, no
        /// trailing separator, an abdata folder or a file (the game's exe) replaced by its parent folder.</summary>
        public static string Normalise(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;
            string p = input.Trim().Trim('"').Trim();
            if (p.Length == 0) return null;
            try { p = System.IO.Path.GetFullPath(p); }
            catch (Exception) { return p; }
            p = TrimSeparators(p);
            try
            {
                if (File.Exists(p)) p = System.IO.Path.GetDirectoryName(p) ?? p;
            }
            catch (Exception) { }
            if (string.Equals(System.IO.Path.GetFileName(p), "abdata", StringComparison.OrdinalIgnoreCase))
            {
                var parent = System.IO.Path.GetDirectoryName(p);
                if (!string.IsNullOrEmpty(parent)) p = parent;
            }
            return TrimSeparators(p);
        }

        static string TrimSeparators(string p)
        {
            if (string.IsNullOrEmpty(p)) return p;
            string root = System.IO.Path.GetPathRoot(p) ?? "";
            while (p.Length > root.Length && (p.EndsWith("\\", StringComparison.Ordinal) || p.EndsWith("/", StringComparison.Ordinal))) p = p.Substring(0, p.Length - 1);
            return p;
        }

        /// <summary>Checks a folder (the zipmod count with <paramref name="countZipmods"/>; the "did you mean" search with
        /// <paramref name="propose"/>).</summary>
        public static InstallVerdict Check(string input, bool countZipmods = false, bool propose = true)
        {
            var sw = Stopwatch.StartNew();
            var v = Judge(input, Normalise(input));
            if (propose && v.Level == InstallLevel.Error && v.Code != "unreadable" && v.Path != null) v.DidYouMean = Propose(v.Path);
            v.Ms = sw.Elapsed.TotalMilliseconds;
            if (countZipmods && v.Usable) v.Zipmods = CountZipmods(System.IO.Path.Combine(v.Path, "mods"));
            if (v.Level == InstallLevel.Ok) v.Message = "Honey Select 2 found - " + (v.Zipmods >= 0 ? v.Zipmods.ToString() : "?") + " mods in mods\\";
            return v;
        }

        static InstallVerdict Judge(string input, string dir)
        {
            var v = new InstallVerdict { Input = input, Path = dir };
            if (string.IsNullOrEmpty(dir) || !Readable(dir)) return Set(v, InstallLevel.Error, "unreadable", "This folder cannot be read.");
            string ab = System.IO.Path.Combine(dir, "abdata");
            if (!SafeFile(System.IO.Path.Combine(ab, "abdata"))) return Set(v, InstallLevel.Error, "noAbdata", "This is not a game folder (no abdata).");
            if (!SafeFile(System.IO.Path.Combine(ab, "chara", "oo_base.unity3d")) || !HasBundle(System.IO.Path.Combine(ab, "list", "characustom")))
                return Set(v, InstallLevel.Error, "incomplete", "The HS2 data is incomplete (base character data missing).");
            if (!DataFolders.Any(f => SafeDir(System.IO.Path.Combine(dir, f))))
                return Set(v, InstallLevel.Warning, "notHs2", "abdata found, but this may not be Honey Select 2.");
            return Set(v, InstallLevel.Ok, "ok", "Honey Select 2 found");
        }

        static InstallVerdict Set(InstallVerdict v, InstallLevel l, string code, string message)
        {
            v.Level = l;
            v.Code = code;
            v.Message = message;
            return v;
        }

        /// <summary>The one direct subfolder of <paramref name="dir"/> that passes (ok or warning), or null (none or several).</summary>
        public static string Propose(string dir)
        {
            string found = null;
            try
            {
                int n = 0;
                foreach (var sub in Directory.EnumerateDirectories(dir))
                {
                    if (++n > MaxSubfolders) break;
                    if (Judge(sub, sub).Usable)
                    {
                        if (found != null) return null;
                        found = sub;
                    }
                }
            }
            catch (Exception) { return null; }
            return found;
        }

        /// <summary>The *.zipmod files under a folder (recursive; unreadable subfolders skipped; 0 when absent). Not bounded by
        /// time: run it on a worker (about 6,000 files on the reference install).</summary>
        public static int CountZipmods(string dir)
        {
            if (!SafeDir(dir)) return 0;
            int n = 0;
            var stack = new Stack<string>();
            stack.Push(dir);
            while (stack.Count > 0)
            {
                string d = stack.Pop();
                try
                {
                    foreach (var f in Directory.EnumerateFiles(d, "*.zipmod")) n++;
                    foreach (var s in Directory.EnumerateDirectories(d))
                    {
                        try { if ((File.GetAttributes(s) & FileAttributes.ReparsePoint) != 0) continue; }
                        catch (Exception) { continue; }
                        stack.Push(s);
                    }
                }
                catch (Exception) { }
            }
            return n;
        }

        /// <summary>A folder exists and its entries can be listed.</summary>
        public static bool Readable(string dir)
        {
            try
            {
                if (!Directory.Exists(dir)) return false;
                using (var e = Directory.EnumerateFileSystemEntries(dir).GetEnumerator()) e.MoveNext();
                return true;
            }
            catch (Exception) { return false; }
        }

        static bool HasBundle(string dir)
        {
            try { return Directory.Exists(dir) && Directory.EnumerateFiles(dir, "*.unity3d").Any(); }
            catch (Exception) { return false; }
        }

        static bool SafeFile(string f) { try { return File.Exists(f); } catch (Exception) { return false; } }
        static bool SafeDir(string d) { try { return Directory.Exists(d); } catch (Exception) { return false; } }
    }
}
