// The pre-upload texture check (Phase 5.2): every bundle the catalog opens gets a texture verdict before anything is loaded
// from it, computed from the bundle's bytes without Unity. Some mod textures (built with older Unity versions) cannot be
// uploaded by Unity 6: with a graphics device the upload removes the device or crashes the process before the load returns,
// so a check after the load is too late. The verdict reads every Texture2D object through the bundle's type tree (size,
// format, mip count, complete image size, the image data's length or its streamed range; pixels are never read) and
// classifies each texture:
//   * mip count < 1, more mips than the size allows, a complete image size below the declared chain, data shorter than the
//     chain, a block-compressed size that is not a multiple of four, a format a desktop GPU cannot sample, streamed data
//     outside the bundle, a readable texture whose data are streamed (Unity 6 loads its CPU copy as empty: "unexpected size
//     (expected N, got 0)"; it is made not readable, as a texture nothing reads on the CPU), an empty size (a dynamic font's
//     texture: classified but left as it is).
// A texture without one of these classes stays as it is, also when its data hold more mip levels than its count says
// (Unity reads the declared chain).
// A defective texture is patched with the rules of the repair tool (the mip count set to the chain the data holds; a
// block-compressed size padded to a multiple of four, dropping the mip levels whose block counts change; a texture without
// usable data replaced by a 4 x 4 mid-grey RGBA32 one, a flat normal for normal maps). The bundle's verdict is the worst of
// its textures: ok (opened as it is), patch (field patches only), patch-grey (an object replaced), malformed (a node outside
// the data, also after reading past a node name that runs beyond its first zero byte: not opened) or unreadable (a layout
// this reader does not know: opened as before, guarded after the load).
// Mesh objects are read through their type trees too: a mesh whose bytes end exactly where one of its last fields begins (a
// writer of an older layout left the streamed-data reference out) is completed with zero bytes, which read as "no streamed
// data" (the mesh holds its vertices itself); a mesh cut inside a field ends the process when Unity 6 loads it, so the
// assets whose preload range needs it are refused.
// Patch() writes the patched bundle uncompressed (the catalog opens it from memory and stores it in the repair cache, so the
// next session opens it from the file). Thread-safe, no Unity API: the checks run on workers.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace HS2Next.Content
{
    public enum TextureVerdictKind { Ok, Patch, PatchGrey, Malformed, Unreadable }

    /// <summary>One defective texture of a bundle and what the patch does to it.</summary>
    public sealed class TextureDefect
    {
        public string Texture;
        public long PathId;
        /// <summary>The defect classes ("mip &lt; 1", "block size", ...), comma-separated.</summary>
        public string Class;
        /// <summary>The repair action ("mips 0->1", "pad 512x511->512x512", "grey 4x4", "flat normal 4x4").</summary>
        public string Action;
        public bool Grey;
        public override string ToString() => Texture + "=" + Action + " (" + Class + ")";
    }

    /// <summary>The texture verdict of one bundle (see the file header).</summary>
    public sealed class TextureVerdict
    {
        public string Key, Stamp;
        public TextureVerdictKind Kind;
        public readonly List<TextureDefect> Defects = new List<TextureDefect>();
        public int Textures;
        /// <summary>Sum of the textures' mip-chain sizes (the bytes their loaded objects hold, as declared).</summary>
        public long TextureBytes;
        /// <summary>Why the bundle is malformed or unreadable.</summary>
        public string Error;
        /// <summary>Mesh objects whose data end inside a field of their type tree (loading them kills the process; the patch
        /// cannot complete them).</summary>
        public int TruncatedMeshes;
        /// <summary>The container paths of the assets whose preload range needs such a mesh: loads of them are refused.</summary>
        public readonly List<string> RefusedAssets = new List<string>();
        /// <summary>The node names the patch writes (index = node; null = kept), when the streamed textures name files the
        /// node table does not carry, and the node offsets it moves (index = node; -1 = kept; null = none moved); null
        /// otherwise.</summary>
        public string[] NodeNames;
        public long[] NodeOffsets;
        public bool Lzma;
        public double Ms;
        /// <summary>Answered from the verdict cache (memory or disk).</summary>
        public bool Cached;

        public string KindName => KindText(Kind);
        public bool NeedsPatch => Kind == TextureVerdictKind.Patch || Kind == TextureVerdictKind.PatchGrey;

        /// <summary>The repair index's text of the actions: "name=action|name=action".</summary>
        public string ActionsText
        {
            get
            {
                var sb = new StringBuilder();
                foreach (var d in Defects) { if (sb.Length > 0) sb.Append('|'); sb.Append(Clean(d.Texture)).Append('=').Append(d.Action); }
                return sb.ToString();
            }
        }

        public static string KindText(TextureVerdictKind k) =>
            k == TextureVerdictKind.Ok ? "ok" : k == TextureVerdictKind.Patch ? "patch" : k == TextureVerdictKind.PatchGrey ? "patch-grey" :
            k == TextureVerdictKind.Malformed ? "malformed" : "unreadable";

        public static bool TryKind(string s, out TextureVerdictKind k)
        {
            switch (s)
            {
                case "ok": k = TextureVerdictKind.Ok; return true;
                case "patch": k = TextureVerdictKind.Patch; return true;
                case "patch-grey": k = TextureVerdictKind.PatchGrey; return true;
                case "malformed": k = TextureVerdictKind.Malformed; return true;
                case "unreadable": k = TextureVerdictKind.Unreadable; return true;
            }
            k = TextureVerdictKind.Unreadable;
            return false;
        }

        /// <summary>A texture name as the index and the verdict cache store it (no tab, "|" or line break).</summary>
        public static string Clean(string s) => (s ?? "").Replace('\t', ' ').Replace('|', '/').Replace('\n', ' ').Replace('\r', ' ');

        public override string ToString() => $"{KindName} {Textures} textures" + (Defects.Count > 0 ? ": " + ActionsText : "") +
            (TruncatedMeshes > 0 ? $"; {TruncatedMeshes} truncated meshes, {RefusedAssets.Count} assets refused" : "") + (Error != null ? " (" + Error + ")" : "");
    }

    public static class TextureCheck
    {
        public const int ClassTexture2D = 28, ClassMesh = 43, ClassAssetBundle = 142;

        /// <summary>The version of the classes and rules below: the verdict cache drops verdicts of another version.</summary>
        public const int RulesVersion = 6;

        // TextureFormat -> (block width, bytes per block); block width 1 = uncompressed bytes per pixel (TextureHeaders' table)
        static readonly Dictionary<int, (int bw, int bpb)> Formats = new Dictionary<int, (int, int)>
        {
            [1] = (1, 1), [2] = (1, 2), [3] = (1, 3), [4] = (1, 4), [5] = (1, 4), [7] = (1, 2), [9] = (1, 2), [13] = (1, 2), [14] = (1, 4),
            [15] = (1, 2), [16] = (1, 4), [17] = (1, 8), [18] = (1, 4), [19] = (1, 8), [20] = (1, 16), [62] = (1, 2), [63] = (1, 1),
            [10] = (4, 8), [12] = (4, 16), [24] = (4, 16), [25] = (4, 16), [26] = (4, 8), [27] = (4, 16), [28] = (4, 8), [29] = (4, 16),
        };

        static bool Known(int f) => Formats.ContainsKey(f);
        static bool Block(int f) => f == 10 || f == 12 || (f >= 24 && f <= 29);
        static bool Crunched(int f) => f == 28 || f == 29 || f == 64 || f == 65;
        static bool CpuDecoded(int f) => TextureHeaders.IsCpuDecoded(f);

        static long Chain(int w, int h, int f, int mips) => TextureHeaders.ChainSize(w, h, f, mips);
        static int Full(int w, int h) => TextureHeaders.FullChain(w, h);
        static int Blocks(int n) => (n + 3) / 4;

        /// <summary>The fields of one Texture2D the rules read.</summary>
        public sealed class Facts
        {
            public string Name;
            public long PathId;
            public int W, H, Format, Mips = 1, Size, Images = 1, LightmapFormat;
            public bool HasMipCount, HasMipMap, HasImageCount, Readable;
            public int Inline;
            public long InlineStart;
            public long StreamOffset, StreamSize;
            public string StreamPath;
            /// <summary>The streamed range is not inside a node of the bundle.</summary>
            public bool StreamOutside;
            /// <summary>The stream path the texture's own serialized file gives, when only the path's directory part names no
            /// node (see <see cref="StreamDirFix"/>); null otherwise.</summary>
            public string StreamFix;
            public long Data => Inline > 0 ? Inline : StreamSize;
        }

        // ------------------------------------------------------------ reading

        /// <summary>Reads the rule fields of a Texture2D through its type tree; <paramref name="full"/> reads the streamed range
        /// even when the image data is inline (the patch writer needs every field).</summary>
        public static Facts ReadFacts(Hs2TypeNode tree, Hs2ObjectBytes r, bool full)
        {
            var f = Hs2TypeTree.Read(tree, r, full ? null : (Func<Hs2Field, bool>)(x => x.Node.Name == "image data" && x.Count > 0));
            var t = new Facts();
            if (f.TryGetValue("m_Name", out var name)) t.Name = Hs2TypeTree.StringOf(name, r);
            t.W = Int(f, "m_Width");
            t.H = Int(f, "m_Height");
            t.Format = Int(f, "m_TextureFormat");
            t.Size = Int(f, "m_CompleteImageSize");
            t.HasMipCount = f.ContainsKey("m_MipCount");
            t.HasMipMap = f.ContainsKey("m_MipMap");
            t.Mips = t.HasMipCount ? Int(f, "m_MipCount") : 1;
            t.HasImageCount = f.ContainsKey("m_ImageCount");
            t.Images = t.HasImageCount ? Int(f, "m_ImageCount") : 1;
            t.LightmapFormat = Int(f, "m_LightmapFormat");
            t.Readable = Int(f, "m_IsReadable") != 0;
            if (f.TryGetValue("image data", out var img) && img.Count > 0) { t.Inline = img.Count; t.InlineStart = img.DataStart; }
            if (f.TryGetValue("m_StreamData", out var sd) && sd.Fields != null)
            {
                t.StreamOffset = sd["offset"]?.Value ?? 0;
                t.StreamSize = sd["size"]?.Value ?? 0;
                t.StreamPath = sd["path"] != null ? Hs2TypeTree.StringOf(sd["path"], r) : null;
            }
            return t;
        }

        static int Int(Dictionary<string, Hs2Field> f, string name) => f.TryGetValue(name, out var x) && x.Count < 0 && x.Fields == null ? unchecked((int)x.Value) : 0;

        // a streamed range must lie inside a node of the bundle: "archive:/CAB-x/CAB-x.resS" names the node by its file name
        // (names: the node names the patch gives, else the bundle's)
        static bool StreamOutside(Facts t, Hs2BundleFile b, string[] names = null)
        {
            if (t.Inline > 0 || t.StreamSize <= 0) return false;
            string file = FileOf(t.StreamPath);
            for (int i = 0; i < b.Nodes.Count; i++)
                if ((names?[i] ?? b.Nodes[i].Path) == file) return t.StreamOffset < 0 || t.StreamOffset + t.StreamSize > b.Nodes[i].Size;
            return true;
        }

        static string FileOf(string p) { p = p ?? ""; int slash = p.LastIndexOf('/'); return slash >= 0 ? p.Substring(slash + 1) : p; }

        // A bundle whose node table lost the names its textures stream from (a writer that broke the names). Unity 6 opens a
        // streamed texture's data at "archive:/<dir>/<file>": <dir> must be the name of the serialized file, <file> the name
        // of the resource file, else the texture loads without its data ("could not open file ... for read"). With one
        // serialized file and one resource file that do not overlap, every streamed texture naming the same path with its
        // range inside the resource file, and the table's names not those, the patch names the serialized file <dir> and the
        // resource file <file>. Null otherwise.
        static string[] StreamNames(Hs2BundleFile b, List<Facts> streamed) => StreamNames(b, streamed, out _);

        // With the two nodes overlapping, the names are given only when the table's offset of the resource file is the one
        // thing wrong, beyond doubt: two nodes, the serialized file at 0 holding exactly its own bytes (its header's size), the
        // two sizes adding up to the data exactly, and the streamed ranges ending exactly at the resource file's end. Then the
        // resource file is moved to the serialized file's end (offsets: index = node, -1 = kept).
        static string[] StreamNames(Hs2BundleFile b, List<Facts> streamed, out long[] offsets)
        {
            offsets = null;
            if (streamed.Count == 0) return null;
            int ser = -1, res = -1;
            for (int i = 0; i < b.Nodes.Count; i++)
            {
                if (IsSerialized(b, b.Nodes[i])) { if (ser >= 0) return null; ser = i; }
                else if (b.Nodes[i].Size > 0) { if (res >= 0) return null; res = i; }
            }
            if (ser < 0 || res < 0) return null;
            var sn = b.Nodes[ser];
            var rn = b.Nodes[res];
            if (sn.Offset < rn.Offset + rn.Size && rn.Offset < sn.Offset + sn.Size)
            {
                long end = 0;
                foreach (var t in streamed) end = Math.Max(end, t.StreamOffset + t.StreamSize);
                if (b.Nodes.Count != 2 || sn.Offset != 0 || sn.Size + rn.Size != b.DataSize || end != rn.Size) return null;
                offsets = new long[b.Nodes.Count];
                for (int i = 0; i < offsets.Length; i++) offsets[i] = -1;
                offsets[res] = sn.Size;
            }
            string path = streamed[0].StreamPath ?? "";
            const string Archive = "archive:/";
            if (!path.StartsWith(Archive, StringComparison.Ordinal)) return null;
            var parts = path.Substring(Archive.Length).Split(new[] { '/' });
            if (parts.Length != 2 || !CleanName(parts[0]) || !CleanName(parts[1]) || parts[1].IndexOf('.') < 0 || parts[0] == parts[1]) return null;
            foreach (var t in streamed)
                if (t.StreamPath != path || t.StreamOffset < 0 || t.StreamOffset + t.StreamSize > b.Nodes[res].Size) return null;
            if (offsets == null && b.Nodes[ser].Path == parts[0] && b.Nodes[res].Path == parts[1]) return null;   // the names are there
            for (int i = 0; i < b.Nodes.Count; i++)
                if (i != ser && i != res && (b.Nodes[i].Path == parts[0] || b.Nodes[i].Path == parts[1])) return null;
            var names = new string[b.Nodes.Count];
            names[ser] = parts[0];
            names[res] = parts[1];
            if (offsets == null) return names;
            // the moved node must not overlap anything at its place
            for (int i = 0; i < b.Nodes.Count; i++)
                if (i != res && b.Nodes[i].Size > 0 && b.Nodes[i].Offset < offsets[res] + rn.Size && offsets[res] < b.Nodes[i].Offset + b.Nodes[i].Size) { offsets = null; return null; }
            return names;
        }

        // A streamed texture whose path names a file the table carries (the range inside it, the node overlapping no other)
        // but a directory that names no node (a rename missed this path; Unity 6 then cannot open the data): the path the
        // serialized file holding it gives, "archive:/<its name>/<file>". Null otherwise.
        static string StreamDirFix(Facts t, Hs2BundleFile b, int holder)
        {
            if (t.Inline > 0 || t.StreamSize <= 0 || holder < 0 || !CleanName(b.Nodes[holder].Path) || Overlaps(b, holder)) return null;
            if (!SplitArchive(t.StreamPath, out var dir, out var file)) return null;
            int fi = -1;
            for (int i = 0; i < b.Nodes.Count; i++) if (b.Nodes[i].Path == file) { fi = i; break; }
            if (fi < 0 || fi == holder || t.StreamOffset < 0 || t.StreamOffset + t.StreamSize > b.Nodes[fi].Size || Overlaps(b, fi)) return null;
            foreach (var n in b.Nodes)
            {
                int dot = n.Path.IndexOf('.');
                if (n.Path == dir || (dot > 0 && n.Path.Substring(0, dot) == dir)) return null;   // the path resolves
            }
            return "archive:/" + b.Nodes[holder].Path + "/" + file;
        }

        static bool SplitArchive(string path, out string dir, out string file)
        {
            dir = file = null;
            const string Archive = "archive:/";
            if (path == null || !path.StartsWith(Archive, StringComparison.Ordinal)) return false;
            var parts = path.Substring(Archive.Length).Split(new[] { '/' });
            if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0) return false;
            dir = parts[0];
            file = parts[1];
            return true;
        }

        static bool Overlaps(Hs2BundleFile b, int i)
        {
            var n = b.Nodes[i];
            for (int j = 0; j < b.Nodes.Count; j++)
            {
                var m = b.Nodes[j];
                if (j != i && n.Size > 0 && m.Size > 0 && n.Offset < m.Offset + m.Size && m.Offset < n.Offset + n.Size) return true;
            }
            return false;
        }

        static bool CleanName(string s)
        {
            if (s.Length < 5 || s.Length > 128) return false;
            foreach (char c in s) if (c <= ' ' || c > '~' || c == '/' || c == '\\') return false;
            return true;
        }

        // ------------------------------------------------------------ classes and rules (Tools/texguard_repair.py)

        /// <summary>The defect classes of a texture (empty: fine), as the texture censuses name them.</summary>
        public static List<string> Classes(Facts t)
        {
            var c = new List<string>();
            int w = t.W, h = t.H, f = t.Format, mips = t.Mips, images = Math.Max(1, t.Images);
            if (w < 1 || h < 1) { c.Add("empty size"); return c; }
            if (mips < 1) c.Add("mip < 1");
            if (Block(f) && (w % 4 != 0 || h % 4 != 0)) c.Add("block size");
            if (mips > Full(w, h)) c.Add("mips > chain");
            bool chainKnown = Known(f) && !Crunched(f) && mips >= 1 && mips <= Full(w, h);
            if (chainKnown && t.Data < Chain(w, h, f, mips) * images) c.Add("data short");
            if (CpuDecoded(f)) c.Add("cpu format");
            if (chainKnown && t.Size < Chain(w, h, f, mips) * images) c.Add("image size");
            if (t.StreamOutside) c.Add("stream outside");
            else
            {
                if (t.Readable && t.Inline == 0 && t.StreamSize > 0) c.Add("readable streamed");
                if (t.StreamFix != null) c.Add("stream dir");
            }
            return c;
        }

        /// <summary>A repair: the action's text and the fields it sets (paths of <see cref="Hs2TypeTree.Rewrite"/>).</summary>
        public sealed class Plan
        {
            public string Action;
            public bool Grey;
            public readonly Dictionary<string, object> Set = new Dictionary<string, object>(StringComparer.Ordinal);
        }

        /// <summary>The repair of a texture with the given classes (null: none needed).</summary>
        public static Plan PlanFor(Facts t, List<string> classes, Func<int, byte[]> inlineHead = null)
        {
            // an empty size is what a dynamic font's texture looks like (Unity fills it at run time): left as it is
            if (classes.Count == 0 || classes.Contains("empty size")) return null;
            if (classes.Contains("cpu format") || classes.Contains("stream outside")) return Grey(t);
            bool readableStreamed = classes.Contains("readable streamed"), streamDir = classes.Contains("stream dir");
            Plan p;
            if (classes.TrueForAll(c => c == "readable streamed" || c == "stream dir")) p = new Plan { Action = "" };
            else if (classes.Contains("block size")) p = Pad(t, inlineHead) ?? Diagnose(t) ?? Grey(t);
            else p = Diagnose(t) ?? Grey(t);
            // a readable texture whose data are streamed: not readable (a grey replacement is already)
            if (readableStreamed && !p.Grey)
            {
                p.Set["m_IsReadable"] = 0L;
                p.Action = p.Action.Length > 0 ? p.Action + " readable off" : "readable off";
            }
            // a stream path whose directory part names no node: the path the texture's own serialized file gives
            if (streamDir && !p.Grey)
            {
                p.Set["m_StreamData/path"] = t.StreamFix;
                p.Action = p.Action.Length > 0 ? p.Action + " stream path" : "stream path";
            }
            return p;
        }

        // texguard_repair.diagnose: the mip count from the data, valid textures left alone, else null for "grey" decided by the caller
        static Plan Diagnose(Facts t)
        {
            int w = t.W, h = t.H, f = t.Format, mips = t.Mips, size = t.Size, images = Math.Max(1, t.Images);
            long data = t.Data;
            if (w < 1 || h < 1) return null;
            bool okChain = Known(f) && mips >= 1 && mips <= Full(w, h) && Chain(w, h, f, mips) * images == size;
            if (okChain && data >= size && size > 0) return null;
            if (data > 0 && size > 0 && data >= size && Known(f))
            {
                for (int n = 1; n <= Full(w, h); n++)
                    if (Chain(w, h, f, n) * images == size)
                    {
                        if (n != mips)
                        {
                            var p = new Plan { Action = "mips " + mips + "->" + n };
                            p.Set["m_MipCount"] = (long)n;
                            return p;
                        }
                        break;
                    }
            }
            if (!okChain && mips >= 1 && data >= size && size > 0 && !Known(f)) return null;
            if (Known(f) && mips >= 1 && data >= size && size > 0)
            {
                int pw = (w + 3) & ~3, ph = (h + 3) & ~3;
                if (mips <= Full(w, h) && Chain(w, h, f, mips) * images <= size) return null;
                if (Block(f) && mips <= Full(pw, ph) && Chain(pw, ph, f, mips) * images == size) return null;
            }
            return null;   // the caller greys it
        }

        // texguard_repair.pad_block_size: the header padded to a multiple of four (mip levels whose block counts change dropped)
        static Plan Pad(Facts t, Func<int, byte[]> inlineHead)
        {
            int w = t.W, h = t.H, f = t.Format, mips = t.Mips, size = t.Size, images = Math.Max(1, t.Images);
            long data = t.Data;
            int pw = (w + 3) & ~3, ph = (h + 3) & ~3;
            if (Crunched(f) || images != 1 || data < size) return null;
            var p = new Plan();
            if (mips < 1)
            {
                for (int n = 1; n <= Full(pw, ph); n++)
                    if (Chain(pw, ph, f, n) == size)
                    {
                        p.Action = $"pad {w}x{h}->{pw}x{ph} mips {mips}->{n}";
                        p.Set["m_Width"] = (long)pw;
                        p.Set["m_Height"] = (long)ph;
                        p.Set["m_MipCount"] = (long)n;
                        if (t.HasMipMap) p.Set["m_MipMap"] = n > 1 ? 1L : 0L;
                        return p;
                    }
                return null;
            }
            if (mips > Full(pw, ph)) return null;
            if (size == Chain(pw, ph, f, mips))
            {
                p.Action = $"pad {w}x{h}->{pw}x{ph}";
                p.Set["m_Width"] = (long)pw;
                p.Set["m_Height"] = (long)ph;
                return p;
            }
            if (mips > Full(w, h) || size < Chain(w, h, f, mips)) return null;
            int keep = 0;
            while (keep < mips && Blocks(Math.Max(1, w >> keep)) == Blocks(Math.Max(1, pw >> keep)) && Blocks(Math.Max(1, h >> keep)) == Blocks(Math.Max(1, ph >> keep))) keep++;
            if (keep < 1) return null;
            p.Action = $"pad {w}x{h}->{pw}x{ph}";
            p.Set["m_Width"] = (long)pw;
            p.Set["m_Height"] = (long)ph;
            if (keep < mips)
            {
                long kept = Chain(pw, ph, f, keep);
                p.Set["m_MipCount"] = (long)keep;
                p.Set["m_CompleteImageSize"] = kept;
                if (t.Inline > 0)
                {
                    if (inlineHead == null) return null;
                    p.Set["image data"] = inlineHead((int)kept);
                }
                else p.Set["m_StreamData/size"] = kept;
                if (t.HasMipMap) p.Set["m_MipMap"] = keep > 1 ? 1L : 0L;
                p.Action += $" mips {mips}->{keep}";
            }
            return p;
        }

        static readonly string[] NormalSuffixes = { "_n", "norm", "normal", "_nm" };

        // texguard_repair.grey: a 4 x 4 RGBA32 texture, mid-grey or a flat normal, one mip, not readable, no streamed data
        static Plan Grey(Facts t)
        {
            string lower = (t.Name ?? "").ToLowerInvariant();
            bool normal = t.LightmapFormat == 3 || t.LightmapFormat == 4;
            foreach (var s in NormalSuffixes) if (lower.EndsWith(s, StringComparison.Ordinal)) normal = true;
            var px = new byte[64];
            for (int i = 0; i < 16; i++)
            {
                px[4 * i] = 128; px[4 * i + 1] = 128;
                px[4 * i + 2] = (byte)(normal ? 255 : 128); px[4 * i + 3] = (byte)(normal ? 128 : 255);
            }
            var p = new Plan { Action = normal ? "flat normal 4x4" : "grey 4x4", Grey = true };
            p.Set["m_Width"] = 4L;
            p.Set["m_Height"] = 4L;
            p.Set["m_TextureFormat"] = 4L;
            p.Set["m_MipCount"] = 1L;
            p.Set["m_CompleteImageSize"] = 64L;
            p.Set["m_IsReadable"] = 0L;
            p.Set["m_StreamingMipmaps"] = 0L;
            p.Set["image data"] = px;
            p.Set["m_StreamData/offset"] = 0L;
            p.Set["m_StreamData/size"] = 0L;
            p.Set["m_StreamData/path"] = "";
            if (t.HasMipMap) p.Set["m_MipMap"] = 0L;
            if (t.HasImageCount) p.Set["m_ImageCount"] = 1L;
            return p;
        }

        // ------------------------------------------------------------ the verdict

        /// <summary>Reads the verdict of a bundle file (no cache); never throws.</summary>
        public static TextureVerdict Analyze(BundleFileRef f)
        {
            var sw = Stopwatch.StartNew();
            TextureVerdict v;
            try
            {
                var b = f.ReadAll != null ? Hs2BundleFile.FromBytes(f.ReadAll(), f.Key, lzma: true) : Hs2BundleFile.OpenFile(f.File, f.Offset, f.Length, f.Key, lzma: true);
                v = Analyze(b);
            }
            catch (Exception e)
            {
                v = new TextureVerdict { Kind = IsStructural(e) ? TextureVerdictKind.Malformed : TextureVerdictKind.Unreadable, Error = e.GetType().Name + ": " + e.Message };
            }
            v.Key = f.Key;
            v.Stamp = f.Stamp;
            v.Ms = sw.Elapsed.TotalMilliseconds;
            return v;
        }

        /// <summary>The verdict of an opened bundle; never throws.</summary>
        public static TextureVerdict Analyze(Hs2BundleFile b) => Analyze(b, null);

        static TextureVerdict Analyze(Hs2BundleFile b, string[] names)
        {
            var v = new TextureVerdict();
            var streamed = new List<Facts>();
            var sw = Stopwatch.StartNew();
            try
            {
                v.Lzma = b.DataCompression == 1;
                var truncated = new Dictionary<string, HashSet<long>>(StringComparer.OrdinalIgnoreCase);
                var bundleObjects = new List<(Hs2SerializedFile sf, Hs2SerializedObject o, Hs2BundleNode node)>();
                foreach (var n in b.Nodes)
                {
                    if (!IsSerialized(b, n)) continue;
                    var node = n;
                    int holder = IndexOfNode(b, n);
                    var sf = Hs2SerializedFile.FromStream(() => b.OpenNode(node), b.Label + ":" + n.Path);
                    foreach (var o in sf.Objects)
                    {
                        if (o.ClassId == ClassMesh)
                        {
                            // a mesh whose data end before its type tree does: completed when its last fields are missing
                            // whole, else the assets that need it are refused
                            var mt = sf.TypeTreeOf(o);
                            long ms = o.Start;
                            var mr = new Hs2ObjectBytes((p, k) => b.ReadRange(node, ms + p, k), o.Size);
                            int tail = mt != null ? Hs2TypeTree.ZeroTail(mt, mr) : 0;
                            if (tail > 0) v.Defects.Add(MeshDefect(mt, mr, o.PathId, tail));
                            else if (tail < 0)
                            {
                                if (!truncated.TryGetValue(n.Path, out var set)) truncated[n.Path] = set = new HashSet<long>();
                                set.Add(o.PathId);
                                v.TruncatedMeshes++;
                            }
                            continue;
                        }
                        if (o.ClassId == ClassAssetBundle) { bundleObjects.Add((sf, o, node)); continue; }
                        if (o.ClassId != ClassTexture2D) continue;
                        v.Textures++;
                        var tree = sf.TypeTreeOf(o);
                        if (tree == null) throw new Hs2SerializedFileException("no type tree for Texture2D");
                        long start = o.Start;
                        var r = new Hs2ObjectBytes((p, k) => b.ReadRange(node, start + p, k), o.Size);
                        var t = ReadFacts(tree, r, false);
                        t.PathId = o.PathId;
                        t.StreamOutside = StreamOutside(t, b, names);
                        if (names == null && !t.StreamOutside) t.StreamFix = StreamDirFix(t, b, holder);
                        if (t.Inline == 0 && t.StreamSize > 0) streamed.Add(t);
                        long chain = Known(t.Format) && t.Mips >= 1 && t.Mips <= Full(t.W, t.H) && t.W > 0 && t.H > 0 ? Chain(t.W, t.H, t.Format, t.Mips) * Math.Max(1, t.Images) : t.Data;
                        v.TextureBytes += Math.Max(0, chain);
                        var cls = Classes(t);
                        if (cls.Count == 0) continue;
                        // the plan needs no inline bytes to be decided (a dropped-level pad of inline data is known from the sizes)
                        var plan = PlanFor(t, cls, k => new byte[k]);
                        if (plan == null) continue;
                        v.Defects.Add(new TextureDefect { Texture = t.Name, PathId = o.PathId, Class = string.Join(", ", cls), Action = plan.Action, Grey = plan.Grey });
                    }
                }
                if (truncated.Count > 0)
                    foreach (var (sf, o, node) in bundleObjects) RefuseUsers(sf, b.ReadRange(node, o.Start, (int)o.Size), node.Path, truncated, v.RefusedAssets);
                if (names == null && StreamNames(b, streamed, out var moved) is string[] given)
                {
                    // the streamed textures read again with the names the patch gives
                    v = Analyze(b, given);
                    v.NodeNames = given;
                    v.NodeOffsets = moved;
                    v.Defects.Insert(0, NamesDefect(b, given, moved));
                    v.Kind = v.Defects.Exists(d => d.Grey) ? TextureVerdictKind.PatchGrey : TextureVerdictKind.Patch;
                    return v;
                }
                v.Kind = v.Defects.Exists(d => d.Grey) ? TextureVerdictKind.PatchGrey : v.Defects.Count > 0 ? TextureVerdictKind.Patch : TextureVerdictKind.Ok;
            }
            catch (Exception e)
            {
                v.Kind = IsStructural(e) ? TextureVerdictKind.Malformed : TextureVerdictKind.Unreadable;
                v.Error = e.GetType().Name + ": " + e.Message;
                v.Defects.Clear();
            }
            v.Ms = sw.Elapsed.TotalMilliseconds;
            return v;
        }

        /// <summary>
        /// The bundle with every defective texture repaired and every mesh whose last fields are missing completed, uncompressed
        /// (null when nothing needs a repair); <paramref name="actions"/> lists what was done. Throws when the bundle cannot be
        /// read or written.
        /// </summary>
        public static byte[] Patch(Hs2BundleFile b, out List<TextureDefect> actions)
        {
            actions = new List<TextureDefect>();
            var data = b.AllData();
            // the names the streamed textures need (a first pass over the textures' fields)
            var streamed = new List<Facts>();
            foreach (var n in b.Nodes)
            {
                if (!IsSerialized(b, n)) continue;
                var sf = Hs2SerializedFile.FromBytes(data, (int)n.Offset, (int)n.Size, b.Label + ":" + n.Path);
                foreach (var o in sf.Objects)
                {
                    if (o.ClassId != ClassTexture2D || !(sf.TypeTreeOf(o) is Hs2TypeNode tt)) continue;
                    var t = ReadFacts(tt, new Hs2ObjectBytes(data, n.Offset + o.Start, o.Size), false);
                    if (t.Inline == 0 && t.StreamSize > 0) streamed.Add(t);
                }
            }
            var names = StreamNames(b, streamed, out var moved);
            if (names != null) actions.Add(NamesDefect(b, names, moved));
            var replace = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var n in b.Nodes)
            {
                if (!IsSerialized(b, n)) continue;
                int at = (int)n.Offset, holder = IndexOfNode(b, n);
                var sf = Hs2SerializedFile.FromBytes(data, at, (int)n.Size, b.Label + ":" + n.Path);
                var objs = new Dictionary<long, byte[]>();
                foreach (var o in sf.Objects)
                {
                    if (o.ClassId == ClassMesh)
                    {
                        var mt = sf.TypeTreeOf(o);
                        var mr = new Hs2ObjectBytes(data, at + o.Start, o.Size);
                        int tail = mt != null ? Hs2TypeTree.ZeroTail(mt, mr) : 0;
                        if (tail <= 0) continue;
                        var whole = new byte[o.Size + tail];
                        Buffer.BlockCopy(data, (int)(at + o.Start), whole, 0, (int)o.Size);
                        objs[o.PathId] = whole;
                        actions.Add(MeshDefect(mt, mr, o.PathId, tail));
                        continue;
                    }
                    if (o.ClassId != ClassTexture2D) continue;
                    var tree = sf.TypeTreeOf(o) ?? throw new Hs2SerializedFileException("no type tree for Texture2D");
                    var r = new Hs2ObjectBytes(data, at + o.Start, o.Size);
                    var t = ReadFacts(tree, r, true);
                    t.PathId = o.PathId;
                    t.StreamOutside = StreamOutside(t, b, names);
                    if (names == null && !t.StreamOutside) t.StreamFix = StreamDirFix(t, b, holder);
                    var cls = Classes(t);
                    if (cls.Count == 0) continue;
                    long inlineStart = t.InlineStart;
                    var plan = PlanFor(t, cls, k => r.Bytes(inlineStart, k));
                    if (plan == null) continue;
                    objs[o.PathId] = Hs2TypeTree.Rewrite(tree, r, plan.Set);
                    actions.Add(new TextureDefect { Texture = t.Name, PathId = o.PathId, Class = string.Join(", ", cls), Action = plan.Action, Grey = plan.Grey });
                }
                if (objs.Count > 0 && sf.WithObjects(data, at, objs) is byte[] grown) replace[n.Path] = grown;
            }
            return actions.Count == 0 ? null : b.WithNodes(data, replace, names, moved);
        }

        /// <summary>
        /// True when a zip entry holds an asset bundle: a name ending in ".unity3d", else the first bytes are a UnityFS header
        /// (some mods give bundles other extensions; the abdata folder of a zipmod also holds loose textures, lists and sounds,
        /// which the background check skips). False when the entry cannot be read.
        /// </summary>
        public static bool IsBundleEntry(HS2Next.IO.ZipIndex zip, HS2Next.IO.ZipIndex.Entry e, string name)
        {
            if (name.EndsWith(".unity3d", StringComparison.OrdinalIgnoreCase)) return true;
            if (e.Size < 32) return false;
            try
            {
                using (var fs = new FileStream(zip.Path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    fs.Position = e.DataOffset;
                    Stream src = e.Method == 0 ? (Stream)fs : new System.IO.Compression.DeflateStream(fs, System.IO.Compression.CompressionMode.Decompress, true);
                    try
                    {
                        var head = new byte[8];
                        int got = 0;
                        while (got < head.Length) { int k = src.Read(head, got, head.Length - got); if (k <= 0) break; got += k; }
                        return got == 8 && head[0] == (byte)'U' && head[1] == (byte)'n' && head[2] == (byte)'i' && head[3] == (byte)'t' && head[4] == (byte)'y'
                               && head[5] == (byte)'F' && head[6] == (byte)'S' && head[7] == 0;
                    }
                    finally { if (!ReferenceEquals(src, fs)) src.Dispose(); }
                }
            }
            catch (Exception) { return false; }
        }

        static int IndexOfNode(Hs2BundleFile b, Hs2BundleNode n)
        {
            for (int i = 0; i < b.Nodes.Count; i++) if (b.Nodes[i].Offset == n.Offset && b.Nodes[i].Size == n.Size && b.Nodes[i].Path == n.Path) return i;
            return -1;
        }

        // The bundle-level action of the names repair: "node names=names <dir> <file>[ moved <offset>] (stream name)".
        static TextureDefect NamesDefect(Hs2BundleFile b, string[] names, long[] moved = null)
        {
            var given = new List<string>();
            for (int i = 0; i < names.Length; i++) if (names[i] != null) given.Add(names[i]);
            string m = "";
            if (moved != null) for (int i = 0; i < moved.Length; i++) if (moved[i] >= 0) m += " moved " + moved[i].ToString(CultureInfo.InvariantCulture);
            return new TextureDefect { Texture = "node names", PathId = 0, Class = "stream name", Action = "names " + string.Join(" ", given) + m };
        }

        // A mesh completed with zero bytes: its missing last fields read as zeros and empty arrays (for a mesh: no streamed
        // vertex data, so the vertices it holds are used).
        static TextureDefect MeshDefect(Hs2TypeNode tree, Hs2ObjectBytes r, long pathId, int tail)
        {
            string name;
            try { name = Hs2TypeTree.StringOf(Hs2TypeTree.Read(tree, r, f => true).TryGetValue("m_Name", out var f) ? f : null, r) ?? "mesh"; }
            catch (Hs2SerializedFileException) { name = "mesh"; }
            return new TextureDefect { Texture = name, PathId = pathId, Class = "mesh short", Action = "mesh tail +" + tail };
        }

        // The AssetBundle object: m_Name, m_PreloadTable (PPtrs), m_Container (path -> preload index, preload size, asset PPtr).
        // A container entry whose asset or preload range names a truncated object is refused.
        static void RefuseUsers(Hs2SerializedFile sf, byte[] bytes, string nodePath, Dictionary<string, HashSet<long>> truncated, List<string> refused)
        {
            var r = new Hs2FieldReader(bytes, 0);
            bool wide = sf.Version >= 14;
            r.AlignedString();
            int pre = r.I32();
            if (pre < 0 || pre > 10000000) return;
            var table = new (int file, long path)[pre];
            for (int i = 0; i < pre; i++) table[i] = (r.I32(), wide ? r.I64() : r.I32());
            int n = r.I32();
            if (n < 0 || n > 10000000) return;
            bool Bad((int file, long path) p)
            {
                string file = p.file == 0 ? nodePath : (p.file - 1 < sf.Externals.Count ? sf.Externals[p.file - 1].FileName : null);
                return file != null && truncated.TryGetValue(file, out var set) && set.Contains(p.path);
            }
            for (int i = 0; i < n; i++)
            {
                string key = r.AlignedString();
                int index = r.I32(), size = r.I32();
                var asset = (r.I32(), wide ? r.I64() : r.I32());
                bool bad = Bad(asset);
                for (int k = index; !bad && k >= 0 && k < index + size && k < table.Length; k++) bad = Bad(table[k]);
                if (bad && !refused.Contains(key)) refused.Add(key);
            }
        }

        // Serialized files carry node flag 4; older writers leave every node's flags 0, so there a name without an extension
        // counts too. An empty node holds nothing to read.
        static bool IsSerialized(Hs2BundleFile b, Hs2BundleNode n)
        {
            if (n.Size <= 0) return false;
            if ((n.Flags & 4) != 0) return true;
            if (n.Flags != 0 || n.Path.IndexOf('.') >= 0) return false;
            foreach (var x in b.Nodes) if ((x.Flags & 4) != 0) return false;
            return true;
        }

        // A parse error that says the bundle itself is broken (as opposed to a layout this reader does not know).
        static bool IsStructural(Exception e)
        {
            if (!(e is Hs2SerializedFileException)) return false;
            string m = e.Message;
            foreach (var k in new[] { "outside the", "truncated", "past the", "in no block", "header size", "malformed", "lzma: ", "lz4: " })
                if (m.IndexOf(k, StringComparison.Ordinal) >= 0) return true;
            return false;
        }
    }

    /// <summary>
    /// The texture verdicts of this machine: in memory per bundle key and on disk in &lt;cache&gt;/texcheck/verdicts-v1.tsv, one
    /// line per verdict (bundle key, source stamp, verdict, textures, texture bytes, milliseconds, defects as
    /// "name=action (classes)" joined by '|', error). Lines are appended with one write each; at load the last line of a key
    /// wins, a line that does not parse is ignored and the file is rewritten without it; a file written under other rules (its
    /// first line "#texcheck verdicts rules N") is emptied. A verdict whose stamp differs from the bundle's current stamp is
    /// stale (the bundle is checked again and a new line appended). Thread-safe.
    /// </summary>
    public sealed class TextureVerdictCache
    {
        public const string FileName = "verdicts-v1.tsv";
        readonly object gate = new object();
        readonly Dictionary<string, TextureVerdict> byKey = new Dictionary<string, TextureVerdict>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The disk file, or null (memory only).</summary>
        public string FilePath { get; }
        /// <summary>Lines read at <see cref="Load"/>, lines ignored there (and dropped by the rewrite), lines appended since.</summary>
        public int Loaded, Ignored, Appended;
        public int Count { get { lock (gate) return byKey.Count; } }

        /// <param name="cacheRoot">The content cache directory (its texcheck folder holds the file); null = memory only.</param>
        public TextureVerdictCache(string cacheRoot)
        {
            FilePath = string.IsNullOrEmpty(cacheRoot) ? null : Path.Combine(cacheRoot, "texcheck", FileName);
        }

        /// <summary>Reads the disk file (once at boot); a file with lines that do not parse is rewritten without them.</summary>
        public void Load()
        {
            if (FilePath == null) return;
            lock (gate)
            {
                Loaded = Ignored = 0;
                if (!File.Exists(FilePath)) return;
                string[] lines;
                try { lines = File.ReadAllLines(FilePath, Encoding.UTF8); }
                catch (IOException) { return; }
                if (lines.Length == 0 || lines[0] != Header)
                {
                    Ignored = lines.Length;
                    Rewrite();
                    return;
                }
                foreach (var l in lines)
                {
                    if (l.Length == 0 || l[0] == '#') continue;
                    var v = Parse(l);
                    if (v == null) { Ignored++; continue; }
                    byKey[v.Key] = v;
                    Loaded++;
                }
                if (Ignored > 0) Rewrite();
            }
        }

        /// <summary>The cached verdict of <paramref name="key"/> when its stamp equals <paramref name="stamp"/>.</summary>
        public bool TryGet(string key, string stamp, out TextureVerdict v)
        {
            lock (gate)
            {
                if (key != null && byKey.TryGetValue(key, out v) && string.Equals(v.Stamp, stamp, StringComparison.Ordinal)) return true;
            }
            v = null;
            return false;
        }

        /// <summary>Keeps a verdict (memory and one appended line).</summary>
        public void Add(TextureVerdict v)
        {
            if (v?.Key == null) return;
            lock (gate)
            {
                byKey[v.Key] = v;
                if (FilePath == null) return;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                    var bytes = Encoding.UTF8.GetBytes((File.Exists(FilePath) ? "" : Header + "\n") + Format(v) + "\n");
                    using (var fs = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
                        fs.Write(bytes, 0, bytes.Length);
                    Appended++;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        /// <summary>Forgets every verdict in memory (tests; the file stays).</summary>
        public void Clear() { lock (gate) byKey.Clear(); }

        void Rewrite()
        {
            try
            {
                string tmp = FilePath + ".tmp";
                using (var w = new StreamWriter(tmp, false, new UTF8Encoding(false)))
                {
                    w.NewLine = "\n";
                    w.WriteLine(Header);
                    w.WriteLine("#key\tstamp\tverdict\ttextures\ttextureBytes\tms\tdefects\terror\trefused assets");
                    foreach (var v in byKey.Values) w.WriteLine(Format(v));
                }
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(tmp, FilePath);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>The file's first line: the rules the verdicts were made with.</summary>
        public static string Header => "#texcheck verdicts rules " + TextureCheck.RulesVersion.ToString(Inv);

        /// <summary>One line of the file.</summary>
        public static string Format(TextureVerdict v)
        {
            var d = new StringBuilder();
            foreach (var x in v.Defects)
            {
                if (d.Length > 0) d.Append('|');
                d.Append(TextureVerdict.Clean(x.Texture)).Append('=').Append(x.Action).Append(" (").Append(x.Class).Append(')');
            }
            return string.Join("\t", TextureVerdict.Clean(v.Key), TextureVerdict.Clean(v.Stamp), v.KindName, v.Textures.ToString(Inv), v.TextureBytes.ToString(Inv),
                v.Ms.ToString("F1", Inv), d.ToString(), TextureVerdict.Clean(v.Error), string.Join("|", v.RefusedAssets.ConvertAll(TextureVerdict.Clean)));
        }

        /// <summary>A verdict from one line, or null when the line does not parse.</summary>
        public static TextureVerdict Parse(string line)
        {
            var p = line.Split('\t');
            if (p.Length != 9 || p[0].Length == 0 || !TextureVerdict.TryKind(p[2], out var kind)) return null;
            if (!int.TryParse(p[3], NumberStyles.Integer, Inv, out int textures) || !long.TryParse(p[4], NumberStyles.Integer, Inv, out long bytes)
                || !double.TryParse(p[5], NumberStyles.Float, Inv, out double ms)) return null;
            var v = new TextureVerdict { Key = p[0], Stamp = p[1], Kind = kind, Textures = textures, TextureBytes = bytes, Ms = ms, Error = p[7].Length > 0 ? p[7] : null, Cached = true };
            if (p[6].Length > 0)
                foreach (var t in p[6].Split('|'))
                {
                    int eq = t.LastIndexOf('='), open = t.LastIndexOf(" (", StringComparison.Ordinal);
                    if (eq <= 0 || open < eq || !t.EndsWith(")", StringComparison.Ordinal)) return null;
                    string action = t.Substring(eq + 1, open - eq - 1);
                    v.Defects.Add(new TextureDefect { Texture = t.Substring(0, eq), Action = action, Class = t.Substring(open + 2, t.Length - open - 3), Grey = action.EndsWith(" 4x4", StringComparison.Ordinal) });
                }
            if (v.NeedsPatch != (v.Defects.Count > 0)) return null;
            if (p[8].Length > 0) v.RefusedAssets.AddRange(p[8].Split('|'));
            v.TruncatedMeshes = v.RefusedAssets.Count > 0 ? -1 : 0;   // the count is not stored; -1 = some
            return v;
        }
    }

    /// <summary>
    /// The repair cache (shared with Tools/texguard_repair.py): a patched bundle is written once, uncompressed, to
    /// &lt;cache&gt;/texguard/bundles/&lt;sha1(key)[:16]&gt;.unity3d (a new name "_2", "_3"... when that file exists, so a process
    /// that has an older copy open keeps it) and the index &lt;cache&gt;/texguard/repaired.tsv gets the line "key, file, source
    /// label, name=action|..." in place of the key's earlier line. The index is rewritten through a temporary file.
    /// </summary>
    public static class TextureRepairCache
    {
        static readonly object gate = new object();

        public static string Dir(string cacheRoot) => Path.Combine(cacheRoot, "texguard");
        public static string IndexFile(string cacheRoot) => Path.Combine(Dir(cacheRoot), "repaired.tsv");

        /// <summary>The file name stem of a key: the first 16 hex digits of the SHA-1 of its UTF-8 bytes.</summary>
        public static string Stem(string key)
        {
            using (var sha = System.Security.Cryptography.SHA1.Create())
            {
                var h = sha.ComputeHash(Encoding.UTF8.GetBytes(key));
                var sb = new StringBuilder(16);
                for (int i = 0; i < 8; i++) sb.Append(h[i].ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        /// <summary>Writes the patched bundle and its index line; returns the copy's full path (null when it could not be written).</summary>
        public static string Store(string cacheRoot, string key, string sourceLabel, byte[] bytes, string actions)
        {
            if (string.IsNullOrEmpty(cacheRoot) || key == null || bytes == null) return null;
            lock (gate)
            {
                try
                {
                    string dir = Path.Combine(Dir(cacheRoot), "bundles");
                    Directory.CreateDirectory(dir);
                    string stem = Stem(key), name = stem + ".unity3d";
                    for (int n = 2; File.Exists(Path.Combine(dir, name)); n++) name = stem + "_" + n.ToString(CultureInfo.InvariantCulture) + ".unity3d";
                    string rel = "bundles/" + name, full = Path.Combine(dir, name);
                    string part = full + ".part";
                    File.WriteAllBytes(part, bytes);
                    File.Move(part, full);
                    string index = IndexFile(cacheRoot);
                    var lines = new List<string>();
                    if (File.Exists(index))
                        foreach (var l in File.ReadAllLines(index, Encoding.UTF8))
                        {
                            var f = l.TrimStart('﻿').Split('\t');
                            if (l.StartsWith("#", StringComparison.Ordinal) || f.Length < 4 || !string.Equals(f[0], key, StringComparison.OrdinalIgnoreCase)) lines.Add(l);
                        }
                    if (lines.Count == 0 || !lines[0].StartsWith("#", StringComparison.Ordinal)) lines.Insert(0, "# cache key\trepaired file\tsource label\ttextures (name=action)");
                    lines.Add(string.Join("\t", key, rel, TextureVerdict.Clean(sourceLabel), actions));
                    string tmp = index + ".tmp";
                    File.WriteAllText(tmp, string.Join("\n", lines) + "\n", new UTF8Encoding(false));
                    if (File.Exists(index)) File.Replace(tmp, index, null);
                    else File.Move(tmp, index);
                    return full;
                }
                catch (IOException) { return null; }
                catch (UnauthorizedAccessException) { return null; }
            }
        }
    }
}
