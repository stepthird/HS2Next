// The canonical character snapshot of the Maker core tests (package S of Phase 4.0).
// A text dump of a built character, one line per entry, "<section>|<key>|<values>", compared by SHA-256 of the text
// (byte identity) and diffed line by line (by section and key) on failure. Sections, in this order:
//   head      format version (the snapshot's own version, not the save format's)
//   obj       every transform under the root in hierarchy order: name path (k-th same-named sibling as "name[k]"),
//             active self, sibling index, component type names (sorted: the order of AddComponent is not semantic)
//   bone      local position / rotation / scale of every transform as float32 bits (hex)
//   rend      every renderer: type, enabled, forceRenderingOff, layer, rendering layer mask, shadow modes, shared mesh name +
//             vertex count, bone count + SHA-256 of the bone paths, root bone, local bounds
//   mat       per renderer slot: origin (copy = a run-time material; bundle = a material the catalog recorded at its load,
//             whose properties are NOT read), name, shader, keywords (sorted), render queue, every property the shader
//             declares (floats, colours, vectors as bits; ints; textures by name + size + format, scale / offset as bits;
//             render textures by size, format, mip flags and, with SnapshotOptions.ReadBack, SHA-256 of the mip-0 read-back)
//   creator   the skin creators of the context (body / face colour + detail): the create material as above, the main
//             texture, the layers drawn under / over (KSOX, uncensor), the output render texture
//   motion    stage list (type, name, phase, enabled), dynamic-bone collider table, chains (category, kind, slot, bundle,
//             flags, root / owner, per-particle parameters and transforms, collider references), bust stage values and
//             chains, CharaAnimation's BodyHeight / BodyBust
//   ctx       Missing (sorted), ExtStats.Summary(), the accessory slot table, part objects (head, hair, clothes with their
//             rows and states), the resolver's registered parts with their rows (sorted), bone modifiers (ABMX / AAC, sorted)
// Rules: keywords of every material are read before any property is read (the Editor's property drawers may switch
// keywords on a first property read), so a material shared by several slots reads the same as per-slot copies;
// nothing that is not build output (accessory ids, instance ids, timings, ownership) is in the hashed text. Ownership
// (the G1 check of S1) is counted beside the text (CharaSnapshot.G1*), not hashed. Values are not rounded.
// API (for packages A, C, T): CharaSnapshot.Take(root, ctx [, options]) → snapshot; .Sha256, .Lines, .Text;
// snapshot.Write(path) (gzip) / CharaSnapshot.Load(path); CharaSnapshot.Compare(a, b, maxLines) → SnapshotDiff (.Equal,
// .Lines (the first maxLines differences, token-level), .Counts per section, ToString()). Sources of the readers are
// grouped in the region "readers of the build" so a moved member is changed in one place.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using HS2Next.Chara;
using HS2Next.Chara.Documents;
using HS2Next.Chara.Ext;
using HS2Next.Content;
using HS2Next.Motion;
using HS2Next.Motion.Animation;
using HS2Next.Motion.DynamicBones;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace HS2Next.Tests.MakerCore
{
    public sealed class SnapshotOptions
    {
        /// <summary>One CharaMotion.Step(0) before the dump (after Flush and one Step(0)). False for a live character
        /// whose caller has already stepped it.</summary>
        public bool StepZero = true;
        /// <summary>SHA-256 of the mip-0 read-back of every render texture (graphics runs only; ignored without a device).</summary>
        public bool ReadBack;
    }

    public sealed class SnapshotDiff
    {
        public bool Equal;
        /// <summary>The first differences in snapshot order: "~ section|key: token a <> token b; …", "- line" (only in a),
        /// "+ line" (only in b).</summary>
        public readonly List<string> Lines = new List<string>();
        /// <summary>Differing entries per section (all, not only the listed ones).</summary>
        public readonly SortedDictionary<string, int> Counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public int Total;
        public override string ToString() => Equal ? "equal" :
            $"{Total} differing entries ({string.Join(", ", Counts.Select(kv => kv.Key + " " + kv.Value))})" + (Lines.Count > 0 ? ":\n  " + string.Join("\n  ", Lines) : "");
    }

    public sealed class CharaSnapshot
    {
        public const int FormatVersion = 1;

        public readonly List<string> Lines = new List<string>();
        string sha;

        /// <summary>The canonical text (lines joined by "\n", final "\n").</summary>
        public string Text => string.Join("\n", Lines) + "\n";
        /// <summary>SHA-256 (lower-case hex) of the UTF-8 text.</summary>
        public string Sha256 => sha ??= Hash(Encoding.UTF8.GetBytes(Text));

        // ---- the G1 check of S1 (not hashed): materials drawn = OwnedMaterials, 0 unowned slots, 0 dead materials
        /// <summary>Renderer slots with a material (under the root).</summary>
        public int G1Slots;
        /// <summary>Slots whose material is not in CharaContext.OwnedMaterials.</summary>
        public int G1UnownedSlots;
        /// <summary>Slots holding a recorded bundle material (never acceptable).</summary>
        public int G1BundleSlots;
        /// <summary>Distinct materials drawn by the renderers.</summary>
        public int G1Drawn;
        /// <summary>CharaContext.OwnedMaterials count.</summary>
        public int G1Owned;
        /// <summary>Owned materials no renderer draws (the 3.5 "dead materials").</summary>
        public int G1Dead;
        /// <summary>Context.BodyDraw / FaceDraw drawn by a renderer (false = dead or absent).</summary>
        public bool G1BodyDrawLive, G1FaceDrawLive;
        public bool G1Pass => G1UnownedSlots == 0 && G1Dead == 0 && G1BundleSlots == 0 && G1BodyDrawLive && G1FaceDrawLive;
        public string G1Text => $"slots {G1Slots}, unowned slots {G1UnownedSlots}, bundle slots {G1BundleSlots}, drawn {G1Drawn}, owned {G1Owned}, dead {G1Dead}, body draw {(G1BodyDrawLive ? "live" : "dead")}, face draw {(G1FaceDrawLive ? "live" : "dead")}";

        // ---- read-back statistics (U8)
        public int ReadBackTextures;
        public long ReadBackBytes;
        public double ReadBackMs;
        /// <summary>Time of Take without the read-back (ms).</summary>
        public double TakeMs;

        // ================================================================ take

        public static CharaSnapshot Take(GameObject root, CharaContext ctx, SnapshotOptions options = null)
        {
            if (!root) throw new ArgumentNullException(nameof(root));
            options ??= new SnapshotOptions();
            var sw = Stopwatch.StartNew();
            if (options.StepZero) ctx?.Motion?.Step(0f);
            var w = new Writer(root.transform, options.ReadBack && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null);
            var s = new CharaSnapshot();
            w.Out = s.Lines;
            w.Add("head", "format", "charasnapshot " + FormatVersion);
            w.Objects();
            w.Bones();
            w.RenderersAndMaterials(ctx, s);
            w.Creators(ctx);
            w.MotionSection(ctx, root);
            w.ContextSection(ctx);
            s.ReadBackTextures = w.ReadBackCount; s.ReadBackBytes = w.ReadBackBytes; s.ReadBackMs = w.ReadBackMs;
            s.TakeMs = sw.Elapsed.TotalMilliseconds - w.ReadBackMs;
            return s;
        }

        // ================================================================ files

        /// <summary>Writes the text gzip-compressed (the baselines are ~1 MB of text per character).</summary>
        public void Write(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            var bytes = Encoding.UTF8.GetBytes(Text);
            string tmp = path + ".tmp";
            using (var f = File.Create(tmp))
            using (var z = new GZipStream(f, System.IO.Compression.CompressionLevel.Optimal))
                z.Write(bytes, 0, bytes.Length);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        /// <summary>Reads a snapshot written by <see cref="Write"/> (gzip) or a plain text file.</summary>
        public static CharaSnapshot Load(string path)
        {
            var raw = File.ReadAllBytes(path);
            string text;
            if (raw.Length >= 2 && raw[0] == 0x1f && raw[1] == 0x8b)
                using (var z = new GZipStream(new MemoryStream(raw), CompressionMode.Decompress))
                using (var r = new StreamReader(z, new UTF8Encoding(false))) text = r.ReadToEnd();
            else text = new UTF8Encoding(false).GetString(raw);
            var s = new CharaSnapshot();
            if (text.EndsWith("\n", StringComparison.Ordinal)) text = text.Substring(0, text.Length - 1);
            if (text.Length > 0) s.Lines.AddRange(text.Split('\n'));
            return s;
        }

        public static string Hash(byte[] b)
        {
            using (var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(b)).Replace("-", "").ToLowerInvariant();
        }

        // ================================================================ compare

        /// <summary>
        /// Compares two snapshots: equal when the SHA-256 of the texts are equal; otherwise the entries are matched by
        /// "section|key" (a key that repeats is numbered by occurrence) and the first <paramref name="maxLines"/> differences
        /// are listed in a's order (then the entries only in b), changed entries with the differing tokens only.
        /// </summary>
        public static SnapshotDiff Compare(CharaSnapshot a, CharaSnapshot b, int maxLines = 20)
        {
            var d = new SnapshotDiff();
            if (a.Sha256 == b.Sha256) { d.Equal = true; return d; }
            var ka = Keyed(a.Lines); var kb = Keyed(b.Lines);
            var bIndex = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (k, l) in kb) bIndex[k] = l;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            void Count(string key) { string sec = key.Substring(0, Math.Max(0, key.IndexOf('|'))); d.Counts[sec] = d.Counts.TryGetValue(sec, out var n) ? n + 1 : 1; d.Total++; }
            foreach (var (k, l) in ka)
            {
                seen.Add(k);
                if (!bIndex.TryGetValue(k, out var lb)) { Count(k); if (d.Lines.Count < maxLines) d.Lines.Add("- " + Short(l)); continue; }
                if (lb == l) continue;
                Count(k);
                if (d.Lines.Count < maxLines) d.Lines.Add("~ " + TokenDiff(k, l, lb));
            }
            foreach (var (k, l) in kb)
                if (!seen.Contains(k)) { Count(k); if (d.Lines.Count < maxLines) d.Lines.Add("+ " + Short(l)); }
            if (d.Total == 0) d.Lines.Add("same entries in a different order");
            return d;
        }

        static List<(string key, string line)> Keyed(List<string> lines)
        {
            var l = new List<(string, string)>(lines.Count);
            var n = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var x in lines)
            {
                int p1 = x.IndexOf('|'); int p2 = p1 < 0 ? -1 : x.IndexOf('|', p1 + 1);
                string k = p2 < 0 ? x : x.Substring(0, p2);
                int c = n.TryGetValue(k, out var v) ? v + 1 : 0;
                n[k] = c;
                l.Add((c == 0 ? k : k + "#" + c, x));
            }
            return l;
        }

        static string TokenDiff(string key, string a, string b)
        {
            string va = a.Substring(Math.Min(a.Length, key.Length + 1)), vb = b.Substring(Math.Min(b.Length, key.Length + 1));
            var ta = va.Split(' '); var tb = vb.Split(' ');
            if (ta.Length != tb.Length) return Short(key + ": " + va) + "  <>  " + Short(vb);
            var d = new List<string>();
            for (int i = 0; i < ta.Length && d.Count < 6; i++) if (ta[i] != tb[i]) d.Add(Short(ta[i], 160) + " <> " + Short(tb[i], 160));
            return Short(key, 200) + ": " + string.Join("; ", d);
        }

        static string Short(string s, int n = 400) => s.Length > n ? s.Substring(0, n) + "…" : s;

        // ================================================================ writer

        sealed class Writer
        {
            public List<string> Out;
            readonly Transform root;
            readonly bool readBack;
            readonly Dictionary<Transform, string> paths = new Dictionary<Transform, string>();
            readonly List<Transform> order = new List<Transform>();
            readonly Dictionary<RenderTexture, string> rtHash = new Dictionary<RenderTexture, string>();
            public int ReadBackCount; public long ReadBackBytes; public double ReadBackMs;

            public Writer(Transform root, bool readBack)
            {
                this.root = root; this.readBack = readBack;
                Walk(root, root.name);
            }

            void Walk(Transform t, string path)
            {
                paths[t] = path; order.Add(t);
                var count = new Dictionary<string, int>(StringComparer.Ordinal);
                for (int i = 0; i < t.childCount; i++)
                {
                    var c = t.GetChild(i);
                    int k = count.TryGetValue(c.name, out var v) ? v + 1 : 0;
                    count[c.name] = k;
                    Walk(c, path + "/" + c.name + (k == 0 ? "" : "[" + k + "]"));
                }
            }

            public void Add(string section, string key, string values) => Out.Add(section + "|" + key + "|" + values);

            public string P(Transform t) => !t ? "null" : paths.TryGetValue(t, out var p) ? p : "outside:" + OutsidePath(t);
            public string P(Component c) => c ? P(c.transform) : "null";
            public string P(GameObject g) => g ? P(g.transform) : "null";

            static string OutsidePath(Transform t)
            {
                var l = new List<string>();
                for (var x = t; x; x = x.parent) l.Add(x.name);
                l.Reverse();
                return string.Join("/", l);
            }

            // ---------------------------------------------------------------- numbers

            public static string F(float f) => BitConverter.SingleToInt32Bits(f).ToString("x8");
            public static string F(Vector2 v) => F(v.x) + "," + F(v.y);
            public static string F(Vector3 v) => F(v.x) + "," + F(v.y) + "," + F(v.z);
            public static string F(Vector4 v) => F(v.x) + "," + F(v.y) + "," + F(v.z) + "," + F(v.w);
            public static string F(Quaternion q) => F(q.x) + "," + F(q.y) + "," + F(q.z) + "," + F(q.w);
            public static string F(Color c) => F(c.r) + "," + F(c.g) + "," + F(c.b) + "," + F(c.a);
            static string I(int i) => i.ToString(CultureInfo.InvariantCulture);
            static string B(bool b) => b ? "1" : "0";
            static string N(string s) => s == null ? "null" : s.Replace('|', '/').Replace(' ', '_').Replace('\n', '_');
            static string Row(ListEntry e) => e == null ? "null" : e.Category + ":" + e.Key;

            // ---------------------------------------------------------------- objects, bones

            public void Objects()
            {
                foreach (var t in order)
                {
                    var comps = t.GetComponents<Component>().Select(c => c == null ? "Missing" : c.GetType().Name).Where(n => n != "Transform")
                        .OrderBy(n => n, StringComparer.Ordinal);
                    Add("obj", paths[t], "active=" + B(t.gameObject.activeSelf) + " sib=" + I(t.GetSiblingIndex()) + " comps=" + string.Join(",", comps));
                }
            }

            public void Bones()
            {
                foreach (var t in order)
                    Add("bone", paths[t], F(t.localPosition) + " " + F(t.localRotation) + " " + F(t.localScale));
            }

            // ---------------------------------------------------------------- renderers, materials

            public void RenderersAndMaterials(CharaContext ctx, CharaSnapshot s)
            {
                var renderers = root.GetComponentsInChildren<Renderer>(true);
                // keywords first (before any property read of any material)
                var keywords = new Dictionary<Material, string>();
                foreach (var r in renderers)
                    foreach (var m in r.sharedMaterials)
                        if (m && !keywords.ContainsKey(m))
                            keywords[m] = string.Join(",", m.shaderKeywords.OrderBy(k => k, StringComparer.Ordinal));
                if (ctx != null)
                    foreach (var c in CreatorsOf(ctx))
                        if (c.creator?.Mat && !keywords.ContainsKey(c.creator.Mat))
                            keywords[c.creator.Mat] = string.Join(",", c.creator.Mat.shaderKeywords.OrderBy(k => k, StringComparer.Ordinal));
                creatorKeywords = keywords;

                var drawn = new HashSet<Material>();
                var owned = ctx?.OwnedMaterials;
                foreach (var r in renderers)
                {
                    string rp = P(r.transform);
                    var sb = new StringBuilder(r.GetType().Name);
                    sb.Append(" enabled=").Append(B(r.enabled)).Append(" off=").Append(B(r.forceRenderingOff))
                      .Append(" layer=").Append(I(r.gameObject.layer)).Append(" rlm=").Append(r.renderingLayerMask.ToString("x8"))
                      .Append(" shadows=").Append((int)r.shadowCastingMode).Append(B(r.receiveShadows));
                    Mesh mesh = null;
                    if (r is SkinnedMeshRenderer smr)
                    {
                        mesh = smr.sharedMesh;
                        var bones = smr.bones;
                        sb.Append(" bones=").Append(I(bones.Length)).Append(":").Append(Hash(Encoding.UTF8.GetBytes(string.Join(";", bones.Select(P)))).Substring(0, 16));
                        sb.Append(" root=").Append(N(P(smr.rootBone))).Append(" quality=").Append((int)smr.quality).Append(" offscreen=").Append(B(smr.updateWhenOffscreen));
                    }
                    else
                    {
                        var mf = r.GetComponent<MeshFilter>();
                        mesh = mf ? mf.sharedMesh : null;
                    }
                    sb.Append(" mesh=").Append(mesh ? N(mesh.name) + "/" + I(mesh.vertexCount) : "null");
                    var lb = r.localBounds;
                    sb.Append(" bounds=").Append(F(lb.center)).Append("/").Append(F(lb.extents));
                    var mats = r.sharedMaterials;
                    sb.Append(" slots=").Append(I(mats.Length));
                    Add("rend", rp, sb.ToString());
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var m = mats[i];
                        string key = rp + "[" + I(i) + "]";
                        if (!m) { Add("mat", key, "null"); continue; }
                        s.G1Slots++;
                        drawn.Add(m);
                        if (owned == null || !owned.Contains(m)) s.G1UnownedSlots++;
                        if (MaterialCopies.IsRecorded(m)) { s.G1BundleSlots++; Add("mat", key, "origin=bundle name=" + N(m.name) + " shader=" + N(m.shader ? m.shader.name : "none")); continue; }
                        Add("mat", key, "origin=copy " + MaterialText(m, keywords[m]));
                    }
                }
                s.G1Drawn = drawn.Count;
                s.G1Owned = owned?.Count ?? 0;
                s.G1Dead = owned == null ? 0 : owned.Count(m => m && !drawn.Contains(m));
                s.G1BodyDrawLive = ctx?.BodyDraw && drawn.Contains(ctx.BodyDraw);
                s.G1FaceDrawLive = ctx?.FaceDraw && drawn.Contains(ctx.FaceDraw);
            }

            Dictionary<Material, string> creatorKeywords;

            const string FogModesProperty = "_HS2FogModes";

            string MaterialText(Material m, string kw)
            {
                var sb = new StringBuilder();
                var sh = m.shader;
                sb.Append("name=").Append(N(m.name)).Append(" shader=").Append(N(sh ? sh.name : "none")).Append(" queue=").Append(I(m.renderQueue))
                  .Append(" kw=").Append(kw.Length == 0 ? "-" : kw);
                if (!sh) return sb.ToString();
                int n = sh.GetPropertyCount();
                for (int p = 0; p < n; p++)
                {
                    string name = sh.GetPropertyName(p);
                    // a per-material fog-mode stamp that does not draw while the scene's fog is off: left out of the dump
                    if (name == FogModesProperty) continue;
                    sb.Append(' ').Append(name).Append('=');
                    switch (sh.GetPropertyType(p))
                    {
                        case ShaderPropertyType.Float:
                        case ShaderPropertyType.Range: sb.Append(F(m.GetFloat(name))); break;
                        case ShaderPropertyType.Int: sb.Append(I(m.GetInteger(name))); break;
                        case ShaderPropertyType.Color: sb.Append(F(m.GetColor(name))); break;
                        case ShaderPropertyType.Vector: sb.Append(F(m.GetVector(name))); break;
                        case ShaderPropertyType.Texture:
                            sb.Append(Tex(m.GetTexture(name)));
                            if ((sh.GetPropertyFlags(p) & ShaderPropertyFlags.NoScaleOffset) == 0)
                                sb.Append('@').Append(F(m.GetTextureScale(name))).Append('+').Append(F(m.GetTextureOffset(name)));
                            break;
                    }
                }
                return sb.ToString();
            }

            string Tex(Texture t)
            {
                if (!t) return "null";
                if (t is RenderTexture rt)
                    return "rt:" + N(rt.name) + "(" + I(rt.width) + "x" + I(rt.height) + " " + rt.graphicsFormat + " mip=" + B(rt.useMipMap) + B(rt.autoGenerateMips) + " srgb=" + B(rt.sRGB)
                           + (readBack ? " sha=" + ReadBack(rt) : "") + ")";
                return N(t.name) + "(" + I(t.width) + "x" + I(t.height) + " " + t.graphicsFormat + " mips=" + I(t.mipmapCount) + ")";
            }

            string ReadBack(RenderTexture rt)
            {
                if (rtHash.TryGetValue(rt, out var h)) return h;
                var sw = Stopwatch.StartNew();
                try
                {
                    if (!rt.IsCreated()) h = "notcreated";
                    else
                    {
                        var req = AsyncGPUReadback.Request(rt, 0);
                        req.WaitForCompletion();
                        if (req.hasError) h = "error";
                        else
                        {
                            var data = req.GetData<byte>();
                            var bytes = data.ToArray();
                            ReadBackBytes += bytes.Length;
                            h = Hash(bytes).Substring(0, 32);
                        }
                    }
                }
                catch (Exception e) { h = "exception:" + e.GetType().Name; }
                ReadBackMs += sw.Elapsed.TotalMilliseconds;
                ReadBackCount++;
                rtHash[rt] = h;
                return h;
            }

            // ---------------------------------------------------------------- creators

            // ---- readers of the build: the composite creators (today the four skin creators of the context)
            public static IEnumerable<(string name, TexCreator creator)> CreatorsOf(CharaContext ctx)
            {
                if (ctx == null) yield break;
                yield return ("body.skin", ctx.BodySkin);
                yield return ("body.detail", ctx.BodyDetail);
                yield return ("face.skin", ctx.FaceSkin);
                yield return ("face.detail", ctx.FaceDetail);
            }

            public void Creators(CharaContext ctx)
            {
                foreach (var (name, c) in CreatorsOf(ctx))
                {
                    if (c == null) { Add("creator", name, "null"); continue; }
                    var m = c.Mat;
                    string mat = !m ? "mat=null" : MaterialCopies.IsRecorded(m) ? "mat=bundle:" + N(m.name)
                        : MaterialText(m, creatorKeywords != null && creatorKeywords.TryGetValue(m, out var kw) ? kw : string.Join(",", m.shaderKeywords.OrderBy(k => k, StringComparer.Ordinal)));
                    Add("creator", name, "main=" + Tex(c.Main) + " target=" + Tex(c.Target) + " " + mat);
                    for (int i = 0; i < c.Under.Count; i++) Add("creator", name + ".under[" + I(i) + "]", Layer(c.Under[i]));
                    for (int i = 0; i < c.Over.Count; i++) Add("creator", name + ".over[" + I(i) + "]", Layer(c.Over[i]));
                }
            }

            string Layer(SkinLayer l)
            {
                if (l == null) return "null";
                string size = l.TrySize(out int w, out int h) ? I(w) + "x" + I(h) : "?";
                return "type=" + I(l.TexType) + " source=" + N(l.Source) + " tex=" + (l.Texture ? Tex(l.Texture) : "null") + " image=" + (l.Image != null ? Hash(l.Image).Substring(0, 16) : "null") + " size=" + size;
            }

            // ---------------------------------------------------------------- motion

            public void MotionSection(CharaContext ctx, GameObject rootGo)
            {
                var motion = ctx?.Motion;
                if (motion == null) { Add("motion", "stages", "none"); }
                else
                {
                    var st = motion.Stages;
                    for (int i = 0; i < st.Count; i++)
                        Add("motion", "stage[" + I(i) + "]", st[i].GetType().Name + " name=" + N(st[i].Name) + " phase=" + st[i].Phase + " enabled=" + B(st[i].Enabled));
                    var dyn = motion.Get<DynamicBoneStage>();
                    if (dyn != null) DynBones(dyn);
                    var bust = motion.Get<BustStage>();
                    if (bust != null) Bust(bust);
                }
                var anim = CharaAnimation.Of(rootGo);
                Add("motion", "anim", anim ? "BodyHeight=" + F(anim.BodyHeight) + " BodyBust=" + F(anim.BodyBust) : "none");
            }

            void DynBones(DynamicBoneStage d)
            {
                Add("motion", "dynbone", "enabled=" + B(d.Enabled) + " chains=" + I(d.Chains.Count) + " colliders=" + I(d.Colliders.Count) + " hairFk=" + B(d.HairFkActive) + " skirtFk=" + B(d.SkirtFkActive)
                    + " clothes=" + B(d.ClothesEnabled) + " acc=" + B(d.AccessoriesEnabled) + " other=" + B(d.OtherEnabled));
                for (int i = 0; i < d.Colliders.Count; i++)
                {
                    var src = d.Colliders.Source(i);
                    var sh = d.Colliders[i];
                    Add("motion", "collider[" + I(i) + "]", (src ? src.GetType().Name + " " + N(P(src)) : "null") + " path=" + d.Colliders.Path(i) + " kind=" + sh.Kind + " inside=" + B(sh.Inside) + " r=" + F(sh.Radius));
                }
                for (int c = 0; c < d.Chains.Count; c++)
                {
                    var ch = d.Chains[c];
                    var sb = new StringBuilder();
                    sb.Append(ch.Category).Append(" kind=").Append(I(ch.Kind)).Append(" slot=").Append(I(ch.Slot)).Append(" bundle=").Append(I(ch.Bundle))
                      .Append(" skirt=").Append(B(ch.IsSkirt)).Append(" noShake=").Append(B(ch.NoShake)).Append(" enabled=").Append(B(ch.Enabled)).Append(" active=").Append(B(ch.Active))
                      .Append(" far=").Append(B(ch.DistantDisabled))
                      .Append(" owner=").Append(N(P(ch.Owner))).Append(" root=").Append(N(P(ch.Root))).Append(" n=").Append(I(ch.ParticleCount)).Append(" tips=").Append(I(ch.TipCount))
                      .Append(" len=").Append(F(ch.TotalLength)).Append(" w=").Append(F(ch.Weight)).Append(" objScale=").Append(F(ch.ObjScale));
                    sb.Append(" colliders=");
                    for (int k = 0; k < ch.ColliderCount; k++) sb.Append(k == 0 ? "" : ",").Append(I(ch.ColliderIndex(k)));
                    Add("motion", "chain[" + I(c) + "]", sb.ToString());
                    for (int i = 0; i < ch.ParticleCount; i++)
                    {
                        var t = ch.ParticleTransform(i);
                        Add("motion", "chain[" + I(c) + "].p[" + I(i) + "]", (t ? N(P(t)) : "tip") + " parent=" + I(ch.Parent(i)) + " bl=" + F(ch.BoneLength(i)) + " d=" + F(ch.Damping(i)) + " e=" + F(ch.Elasticity(i))
                            + " s=" + F(ch.Stiffness(i)) + " i=" + F(ch.Inert(i)) + " r=" + F(ch.Radius(i)) + " end=" + F(ch.EndOffset(i)));
                    }
                }
            }

            void Bust(BustStage b)
            {
                Add("motion", "bust", "enabled=" + B(b.Enabled) + " male=" + B(b.IsMale) + " softness=" + F(b.BustSoftness) + " weight=" + F(b.BustWeight) + " size=" + F(b.BustSize)
                    + " fkBreast=" + B(b.FkBreastActive) + " animeBust=" + B(b.AnimeBustL) + B(b.AnimeBustR) + " hip=" + B(b.HipL) + B(b.HipR) + " chains=" + I(b.All.Count));
                for (int c = 0; c < b.All.Count; c++)
                {
                    var ch = b.All[c];
                    if (ch == null) { Add("motion", "bust[" + I(c) + "]", "null"); continue; }
                    Add("motion", "bust[" + I(c) + "]", "kind=" + I(ch.Kind) + " comment=" + N(ch.Comment) + " root=" + N(P(ch.Root)) + " n=" + I(ch.ParticleCount) + " gravity=" + F(ch.Gravity)
                        + " ptn=" + I(ch.PtnNo) + " active=" + B(ch.Active) + " end=" + F(ch.EndOffset));
                    for (int i = 0; i < ch.ParticleCount; i++)
                    {
                        var t = ch.ParticleTransform(i);
                        Add("motion", "bust[" + I(c) + "].p[" + I(i) + "]", (t ? N(P(t)) : "tip") + " d=" + F(ch.Damping(i)) + " e=" + F(ch.Elasticity(i)) + " s=" + F(ch.Stiffness(i))
                            + " i=" + F(ch.Inert(i)) + " nbl=" + F(ch.NextBoneLength(i)) + " keep=" + F(ch.KeepMin(i)) + "," + F(ch.KeepMax(i)) + " lp=" + F(ch.LocalPosition(i)));
                    }
                }
            }

            // ---------------------------------------------------------------- context

            public void ContextSection(CharaContext ctx)
            {
                if (ctx == null) { Add("ctx", "context", "none"); return; }
                var missing = ctx.Missing.Select(m => m?.ToString() ?? "null").OrderBy(x => x, StringComparer.Ordinal).ToList();
                for (int i = 0; i < missing.Count; i++) Add("ctx", "missing[" + I(i) + "]", N(missing[i]));
                Add("ctx", "ext", N(ctx.Stats?.Summary()));
                Add("ctx", "sex", ctx.IsMale ? "male" : "female");
                Add("ctx", "uncensor", N(ctx.ReplacedBodyMesh));   // the body mesh a build hook put in place (key kept from 4.0)
                foreach (var a in ctx.Accessories)
                {
                    if (a == null) continue;
                    var sb = new StringBuilder();
                    sb.Append("type=").Append(I(a.Type)).Append(" id=").Append(I(a.Id)).Append(" parent=").Append(N(a.ParentKey)).Append(" row=").Append(Row(a.Entry))
                      .Append(" obj=").Append(N(P(a.Object))).Append(" show=").Append(B(a.Show)).Append(" extra=").Append(B(a.Extra)).Append(" noShake=").Append(B(a.NoShake))
                      .Append(" hide=").Append(I(a.HideCategory)).Append(",").Append(I(a.HideTiming)).Append(" prop=").Append(N(a.Property));
                    sb.Append(" moves=");
                    for (int i = 0; i < 2; i++) for (int j = 0; j < 3; j++) sb.Append(i + j == 0 ? "" : ";").Append(F(a.AddMove[i, j]));
                    sb.Append(" colors=");
                    for (int i = 0; i < a.Color.Length; i++) sb.Append(i == 0 ? "" : ";").Append(F(a.Color[i])).Append("/").Append(F(a.Gloss[i])).Append("/").Append(F(a.Metallic[i]));
                    Add("ctx", "acc[" + I(a.Slot) + "]", sb.ToString());
                }
                Add("ctx", "part.body", N(P(ctx.Body)) + " renderer=" + N(P(ctx.BodyRenderer)));
                Add("ctx", "part.head", N(P(ctx.Head)) + " bone=" + N(P(ctx.HeadBone)));
                for (int k = 0; k < ctx.Hair.Length; k++) Add("ctx", "part.hair[" + I(k) + "]", N(P(ctx.Hair[k])));
                for (int k = 0; k < ctx.Clothes.Length; k++)
                    Add("ctx", "part.clothes[" + I(k) + "]", N(P(ctx.Clothes[k])) + " row=" + Row(ctx.ClothesEntries[k]) + " state=" + I(ctx.ClothesState[k]));
                if (ctx.Input?.Parts is DocumentPartResolver r && ctx.Catalog != null)
                    foreach (var p in r.Parts.Values.OrderBy(x => x.Key, StringComparer.Ordinal))
                    {
                        ListEntry row = null;
                        if (ContentId.TryParse(p.Ref, out var id) && !id.IsEmpty) row = ctx.Catalog.GetEntry(p.Category, id);
                        Add("ctx", "ref." + N(p.Key), "cat=" + I(p.Category) + " ref=" + N(p.Ref) + " row=" + Row(row));
                    }
                var bones = ctx.Bones ? ctx.Bones : ctx.Root ? ctx.Root.GetComponent<BoneModifierApplier>() : null;
                if (bones)
                {
                    var mods = bones.Modifiers.Select(m => N(P(m.Target)) + " " + N(m.Source) + " " + N(m.Bone) + " loc=" + I(m.Location) + " scale=" + F(m.Scale) + " len=" + F(m.Length)
                        + " pos=" + F(m.Position) + " rot=" + F(m.Rotation)).OrderBy(x => x, StringComparer.Ordinal).ToList();
                    for (int i = 0; i < mods.Count; i++) Add("ctx", "bonemod[" + I(i) + "]", mods[i]);
                }
            }
        }
    }
}
