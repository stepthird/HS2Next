// Output-root guard for file-writing commands (HS2Next.Api.Bridge.IOutputRoot
// "File writes"): arguments are names or relative paths under one configured root. Rooted and drive-relative paths,
// `..` segments, ':' (alternate data streams, drive letters), Windows device names, characters Windows forbids,
// trailing dots or spaces in a segment, and anything whose full path lies outside the root answer io_denied; an existing
// file answers io_denied unless overwrite is allowed. Core uses one instance per root (bridge output, save folders).
using System;
using System.IO;
using HS2Next.Api.Bridge;

namespace HS2Next.Bridge
{
    public sealed class OutputRoot : IOutputRoot
    {
        static readonly string[] Devices =
        {
            "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "CONIN$", "CONOUT$",
        };

        /// <param name="root">Absolute directory; null = no root (every Resolve answers io_denied).</param>
        public OutputRoot(string root)
        {
            Root = string.IsNullOrEmpty(root) ? null : Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        public string Root { get; }

        /// <summary>A guard for another absolute root (Core's save folders) with the same rules.</summary>
        public static OutputRoot For(string absoluteRoot) => new OutputRoot(absoluteRoot);

        public string Resolve(string relativePath, bool overwrite = false, bool createDirectory = true)
        {
            string full = Check(relativePath, "path");
            if (File.Exists(full) && !overwrite)
                throw new BridgeException(BridgeError.IoDenied("the file exists: " + Normalize(relativePath) + " (pass overwrite: true)", "overwrite"));
            if (Directory.Exists(full)) throw new BridgeException(BridgeError.IoDenied("the path is a directory: " + Normalize(relativePath), "path"));
            if (createDirectory)
            {
                var dir = Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            }
            return full;
        }

        /// <summary>The full path of a relative path under the root, after the checks of the class comment (no file-system checks).</summary>
        public string Check(string relativePath, string arg = "path")
        {
            if (Root == null) throw new BridgeException(BridgeError.IoDenied("no output folder is configured", arg));
            if (string.IsNullOrWhiteSpace(relativePath)) throw new BridgeException(BridgeError.BadArgs(arg, ArgTypes.Path, "empty file name"));
            string p = relativePath.Replace('\\', '/');
            // '\' and '/' lead a rooted path; ':' covers drive letters (also drive-relative "C:x") and alternate streams
            if (p.StartsWith("/", StringComparison.Ordinal) || p.IndexOf(':') >= 0)
                throw Denied(relativePath, arg, "absolute paths are not accepted; give a name relative to the output folder");
            foreach (var seg in p.Split('/'))
            {
                if (seg.Length == 0 || seg == ".") continue;
                if (seg == "..") throw Denied(relativePath, arg, "'..' is not accepted");
                if (seg.EndsWith(".", StringComparison.Ordinal) || seg.EndsWith(" ", StringComparison.Ordinal))
                    throw Denied(relativePath, arg, "a path segment ends with a dot or a space");
                if (seg.IndexOfAny(new[] { '<', '>', '"', '|', '?', '*' }) >= 0) throw Denied(relativePath, arg, "invalid characters");
                foreach (char c in seg) if (c < 32) throw Denied(relativePath, arg, "control characters");
                string stem = seg.IndexOf('.') >= 0 ? seg.Substring(0, seg.IndexOf('.')) : seg;
                foreach (var d in Devices) if (string.Equals(stem.TrimEnd(' '), d, StringComparison.OrdinalIgnoreCase)) throw Denied(relativePath, arg, "a device name");
            }
            string full;
            try { full = Path.GetFullPath(Path.Combine(Root, p.Replace('/', Path.DirectorySeparatorChar))); }
            catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException)
            {
                throw Denied(relativePath, arg, e.Message);
            }
            string rootWithSep = Root + Path.DirectorySeparatorChar;
            if (!full.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase)) throw Denied(relativePath, arg, "outside the output folder");
            return full;
        }

        /// <summary>The path relative to the root with '/' separators, for replies.</summary>
        public string Relative(string fullPath)
        {
            if (Root == null || fullPath == null) return fullPath;
            string f = Path.GetFullPath(fullPath);
            return f.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                ? f.Substring(Root.Length + 1).Replace('\\', '/')
                : f;
        }

        static string Normalize(string p) => p.Replace('\\', '/');

        static BridgeException Denied(string path, string arg, string why) =>
            new BridgeException(BridgeError.IoDenied("path '" + path + "' denied: " + why, arg));
    }
}
