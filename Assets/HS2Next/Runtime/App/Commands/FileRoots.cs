// File arguments of the Core commands: writes go under one of Core's folders (scenes,
// characters) through the same guard as the bridge output root (package B's OutputRoot, through AppCore.RootFor):
// names and relative paths only, the extension appended when missing; rooted, drive-relative, '..', ':' and device
// names, paths outside the root, and an existing file without `overwrite` answer io_denied. Reads take a path relative
// to the root (guarded the same way) or an absolute path of an existing file with an expected extension (bridge prep
// card, pose and scene files only); a missing file is not_found. Reply paths are absolute.
using System;
using System.IO;
using HS2Next.Api.Bridge;

namespace HS2Next.App.Commands
{
    public static class FileRoots
    {
        /// <summary>
        /// The write target of arguments `name` (a file name) / `path` (a relative path, subfolders created) — exactly one —
        /// under <paramref name="root"/>, with <paramref name="ext"/> appended unless present (ignoring case).
        /// <paramref name="relative"/> = the path relative to the root with '/' separators.
        /// </summary>
        public static string ForWrite(CoreContext c, string root, string ext, bool overwrite, out string relative)
        {
            string arg = OneOf(c, "name", "path");
            string text = c.Args.Get<string>(arg);
            if (arg == "name" && (text.IndexOf('/') >= 0 || text.IndexOf('\\') >= 0))
                throw CommandKit.BadArgs("name", "a file name", "name is a file name; give subfolders with path");
            relative = WithExt(text, ext).Replace('\\', '/');
            try { return c.App.RootFor(root).Resolve(relative, overwrite, createDirectory: true); }
            catch (BridgeException e) when (e.Code == BridgeCodes.IoDenied || e.Code == BridgeCodes.BadArgs)
            {
                throw new BridgeException(e.Code, e.Error.Message, new BridgeErrorDetail { Arg = e.Error.Detail?.Arg == "overwrite" ? "overwrite" : arg });
            }
        }

        /// <summary>
        /// The existing file of argument <paramref name="arg"/>: an absolute path (extension one of <paramref name="exts"/>),
        /// or a path relative to <paramref name="root"/> (guarded; <paramref name="appendExt"/> appended when it has none of
        /// <paramref name="exts"/>). not_found when the file does not exist.
        /// </summary>
        public static string ForRead(CoreContext c, string arg, string text, string root, string[] exts, string appendExt)
        {
            if (string.IsNullOrWhiteSpace(text)) throw CommandKit.BadArgs(arg, ArgTypes.Path, "empty path");
            string full;
            if (Path.IsPathRooted(text) && !text.StartsWith("\\", StringComparison.Ordinal) && !text.StartsWith("/", StringComparison.Ordinal))
            {
                full = Path.GetFullPath(text);
                if (!HasExt(full, exts)) throw new BridgeException(BridgeError.IoDenied("absolute paths are accepted only for " + string.Join(" / ", exts) + " files", arg));
            }
            else
            {
                if (root == null) throw new BridgeException(BridgeError.NotFound("no folder for relative paths (no HS2 install configured?)", arg));
                string rel = HasExt(text, exts) || appendExt == null ? text : text + appendExt;
                try { full = c.App.RootFor(root).Resolve(rel, overwrite: true, createDirectory: false); }
                catch (BridgeException e) when (e.Code == BridgeCodes.IoDenied || e.Code == BridgeCodes.BadArgs)
                {
                    throw new BridgeException(e.Code, e.Error.Message, new BridgeErrorDetail { Arg = arg });
                }
            }
            if (!File.Exists(full)) throw new BridgeException(BridgeError.NotFound("file not found: " + full, arg));
            return full;
        }

        /// <summary>The one of <paramref name="a"/> / <paramref name="b"/> that is given (bad_args for none or both).</summary>
        public static string OneOf(CoreContext c, string a, string b)
        {
            bool ha = c.Args.Has(a), hb = c.Args.Has(b);
            if (ha == hb) throw CommandKit.BadArgs(a, "one of " + a + ", " + b, ha ? "give " + a + " or " + b + ", not both" : "missing argument '" + a + "' or '" + b + "'");
            return ha ? a : b;
        }

        public static string WithExt(string name, string ext) =>
            ext == null || name.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ? name : name + ext;

        static bool HasExt(string path, string[] exts)
        {
            foreach (var e in exts) if (path.EndsWith(e, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary><paramref name="full"/> relative to <paramref name="root"/> with '/' separators (the full path when outside).</summary>
        public static string Relative(string full, string root)
        {
            var r = Path.GetFullPath(root).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            var f = Path.GetFullPath(full);
            return (f.StartsWith(r, StringComparison.OrdinalIgnoreCase) ? f.Substring(r.Length) : f).Replace('\\', '/');
        }
    }
}
