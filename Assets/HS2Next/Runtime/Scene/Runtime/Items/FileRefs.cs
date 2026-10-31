// One converter from a picked image file to a document `file` reference (R18; Phase 3.6
// package C), used by every image picker (scene background and frame, item panel, item pattern) and by the bridge's
// scene.images. It also fixes the 3.5 pickers: they stored FileRef.FromUser(<absolute path>), which ItemTextures'
// relative-path guard rejects (':'), so a picked panel or pattern image never resolved.
//   picked     an absolute path, a path relative to HS2Next's UserData, or "hs2:" + a path relative to the HS2 root
//   rule       a file under the HS2 root's DefaultData → {hs2: "DefaultData/…"} (Illusion files are referenced, never
//              embedded); any other file → embedded {res: "res/<sha256>.<ext>"} in the scene's res store
//              (the scene stays portable). With images.embed false: a file under HS2Next's UserData → {user: …}, under
//              the HS2 root → {hs2: …}; a file elsewhere is embedded anyway (no root to refer to).
//   errors     a missing file, a file that is neither PNG nor JPEG (embedding sniffs the bytes), no res store, an
//              empty path: TryFromPicked returns false with the reason (FromPicked throws ArgumentException).
// Paths in references use '/' separators and no "..". The context (roots, the embed setting, the res store) comes from
// the caller or from FileRefContext.Provider (the app sets it when its commands are registered).
using System;
using System.IO;
using HS2Next.Save;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>Where picked files are resolved and embedded.</summary>
    public sealed class FileRefContext
    {
        /// <summary>The HS2 install root (absolute), or null.</summary>
        public string Hs2Root;
        /// <summary>HS2Next's UserData (absolute), or null.</summary>
        public string UserData;
        /// <summary>The option images.embed (default true, R18).</summary>
        public bool Embed = true;
        /// <summary>The scene container's res store (embedding), or null.</summary>
        public ResStore Res;

        /// <summary>The app's context (set by the app; null = none).</summary>
        public static Func<FileRefContext> Provider;

        /// <summary>The context of a runtime (its services' roots, its scene's res store), embed as given.</summary>
        public static FileRefContext Of(SceneRuntime rt, bool embed = true) => new FileRefContext
        {
            Hs2Root = rt?.Services?.Hs2Root,
            UserData = rt?.Services?.UserData,
            Res = rt?.Session?.Model?.File?.Res,
            Embed = embed,
        };
    }

    public static class FileRefs
    {
        public const string Hs2Prefix = "hs2:";

        /// <summary>The picked file as a reference; throws <see cref="ArgumentException"/> with the reason.</summary>
        public static FileRef FromPicked(string picked, string kind = null, FileRefContext ctx = null)
        {
            if (TryFromPicked(picked, kind, ctx, out var f, out var why)) return f;
            throw new ArgumentException(why);
        }

        /// <summary>The picked file as a reference (see the file header); false with <paramref name="error"/> otherwise.</summary>
        public static bool TryFromPicked(string picked, string kind, FileRefContext ctx, out FileRef result, out string error)
        {
            result = null;
            error = null;
            ctx ??= FileRefContext.Provider?.Invoke() ?? new FileRefContext();
            string full = Resolve(picked, ctx, out error);
            if (full == null) return false;
            if (!File.Exists(full)) { error = "file not found: " + full; return false; }
            if (Under(full, ctx.Hs2Root, out var relHs2) && relHs2.StartsWith("DefaultData/", StringComparison.OrdinalIgnoreCase))
            {
                result = FileRef.FromHs2(relHs2);
                return true;
            }
            if (!ctx.Embed)
            {
                if (Under(full, ctx.UserData, out var relUser)) { result = FileRef.FromUser(relUser); return true; }
                if (relHs2 != null) { result = FileRef.FromHs2(relHs2); return true; }
            }
            if (ctx.Res == null) { error = "no scene to embed " + Path.GetFileName(full) + " into"; return false; }
            byte[] bytes;
            try { bytes = File.ReadAllBytes(full); }
            catch (Exception e) { error = "cannot read " + full + ": " + e.Message; return false; }
            if (ResStore.Sniff(bytes) == ResMediaType.Other) { error = Path.GetFileName(full) + " is neither a PNG nor a JPEG"; return false; }
            try { result = FileRef.FromRes(ctx.Res.Add(bytes)); }
            catch (Exception e) { error = "cannot embed " + Path.GetFileName(full) + ": " + e.Message; return false; }
            return true;
        }

        /// <summary>The absolute path of a picked string ("hs2:" + relative, rooted, or relative to UserData); null with the reason.</summary>
        public static string Resolve(string picked, FileRefContext ctx, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(picked)) { error = "empty path"; return null; }
            try
            {
                if (picked.StartsWith(Hs2Prefix, StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(ctx?.Hs2Root)) { error = "no HS2 install for " + picked; return null; }
                    string rel = picked.Substring(Hs2Prefix.Length);
                    if (!ItemTextures.SafeRelative(rel)) { error = "\"" + rel + "\" is not a plain relative path"; return null; }
                    return Path.GetFullPath(Path.Combine(ctx.Hs2Root, rel));
                }
                if (Path.IsPathRooted(picked)) return Path.GetFullPath(picked);
                if (string.IsNullOrEmpty(ctx?.UserData)) { error = "no HS2Next UserData for the relative path " + picked; return null; }
                if (!ItemTextures.SafeRelative(picked)) { error = "\"" + picked + "\" is not a plain relative path"; return null; }
                return Path.GetFullPath(Path.Combine(ctx.UserData, picked));
            }
            catch (Exception e) { error = "bad path " + picked + ": " + e.Message; return null; }
        }

        /// <summary>True when <paramref name="full"/> lies under <paramref name="root"/>; <paramref name="rel"/> = the '/'-separated relative path.</summary>
        public static bool Under(string full, string root, out string rel)
        {
            rel = null;
            if (string.IsNullOrEmpty(full) || string.IsNullOrEmpty(root)) return false;
            string r;
            try { r = Path.GetFullPath(root).TrimEnd('\\', '/') + Path.DirectorySeparatorChar; }
            catch (Exception) { return false; }
            if (!full.StartsWith(r, StringComparison.OrdinalIgnoreCase)) return false;
            rel = full.Substring(r.Length).Replace('\\', '/');
            return ItemTextures.SafeRelative(rel);
        }

        /// <summary>The absolute path a hs2 / user reference names (null for res, none or a bad path).</summary>
        public static string PathOf(FileRef f, string hs2Root, string userData)
        {
            if (f == null || f.Value == null) return null;
            string root = f.Kind == FileRef.Kinds.Hs2 ? hs2Root : f.Kind == FileRef.Kinds.User ? userData : null;
            if (string.IsNullOrEmpty(root) || !ItemTextures.SafeRelative(f.Value)) return null;
            try { return Path.GetFullPath(Path.Combine(root, f.Value)); }
            catch (Exception) { return null; }
        }

        /// <summary>"res:…", "hs2:…", "user:…" or null (reports, tooltips).</summary>
        public static string Describe(FileRef f)
        {
            if (f == null) return null;
            switch (f.Kind)
            {
                case FileRef.Kinds.Res: return "res:" + f.Value;
                case FileRef.Kinds.Hs2: return "hs2:" + f.Value;
                case FileRef.Kinds.User: return "user:" + f.Value;
                default: return "unknown";
            }
        }
    }
}
