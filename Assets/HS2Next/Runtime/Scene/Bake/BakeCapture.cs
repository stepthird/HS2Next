// Chara to Item: the capture. What the screen shows at the end of the frame is read into a
// self-contained bake (BakeData): the node tree under the root with its pose and layers, every enabled renderer of the chosen parts
// with a mesh (of a LOD group the nearest level only), its mesh as data (BakeMeshes), its materials through each shader's own property list (BakeMaterials), the textures
// they use (BakeTextures, handed to a sink one at a time), the FK list (BakeFk), the joint-correction categories, the origin.
// Capture reads: it writes no character's or item's material, mesh, texture or transform (the blink held open is put back).
// Nodes are walked depth-first, children in order, never entering a scene node holder (an attached child node).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using HS2Next.Chara;
using HS2Next.Chara.Mosaic;
using HS2Next.Save;
using HS2Next.Scenes.Runtime;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace HS2Next.Scenes.Bake
{
    /// <summary>A bake in memory: the main's members and, while it is being made or built, its meshes and textures.</summary>
    public sealed class BakeData
    {
        public const string KindChara = "chara", KindParts = "parts", KindItem = "item", KindPiece = "piece", KindImported = "imported";
        public const string OriginChara = "chara", OriginCentre = "centre", OriginCut = "cut", OriginItem = "item";
        public const string InsideNone = "none", InsideFill = "fill", InsideSection = "section";

        public string Id;
        public string Kind = KindChara;
        /// <summary>When it was baked (UTC, whole seconds).</summary>
        public DateTime Created = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public SourceInfo Source = new SourceInfo();
        /// <summary>The part keys baked; null: everything that was drawn.</summary>
        public List<string> Parts;
        public string Origin = OriginChara;
        public readonly List<NodeData> Nodes = new List<NodeData>();
        /// <summary>The item's FK bones, names unique in <see cref="Nodes"/>.</summary>
        public readonly List<string> Fk = new List<string>();
        /// <summary>The joint-correction categories (8); null: none.</summary>
        public bool[] Correction;
        public readonly List<MeshRef> Meshes = new List<MeshRef>();
        public readonly List<TexRef> Textures = new List<TexRef>();
        public readonly List<MatData> Materials = new List<MatData>();
        public readonly List<RendData> Renderers = new List<RendData>();
        public InsidesData Insides = new InsidesData();
        /// <summary>The format layer's unknown members of the main, carried through unchanged (opaque here).</summary>
        public object Unknown;

        public sealed class SourceInfo
        {
            public string Kind = KindChara, Name = "";
            public int Sex = -1;
            /// <summary>For an item source: its ref, group and category.</summary>
            public string Ref;
            public int Group, Category;
            public object Unknown;
        }

        public sealed class NodeData
        {
            public string Name;
            public int Parent = -1;
            public Vector3 P;
            public Quaternion R = Quaternion.identity;
            public Vector3 S = Vector3.one;
            public bool Active = true;
            public int Layer;
            public object Unknown;
        }

        public sealed class MeshRef
        {
            /// <summary>The blob's SHA-256 (64 hex), set when encoded.</summary>
            public string Blob;
            public string Name;
            /// <summary>The mesh as data while it is made or built; null once only the blob counts.</summary>
            public MeshData Data;
            public object Unknown;
        }

        public sealed class TexRef
        {
            public string Blob;
            public string Name;
            public int WrapU, WrapV;
            public int Filter = 1, Aniso = 1;
            public float Bias;
            /// <summary>The built texture (the prototype's), when made from the capture or a blob.</summary>
            public Texture Built;
            /// <summary>Capture facts (report only): the route, the source's format, its kind.</summary>
            public string Route, SourceFormat, SourceKind;
            /// <summary>The texture it was read from (a capture's, while it lasts; not stored).</summary>
            public Texture Source;
            public object Unknown;
        }

        public sealed class PropData
        {
            public string Name;
            /// <summary>'f' float or range, 'i' integer, 'c' colour, 'v' vector, 't' texture.</summary>
            public char Type;
            public float F;
            public int I;
            /// <summary>The colour (as stored) or the vector.</summary>
            public Vector4 V;
            /// <summary>Texture index into <see cref="Textures"/>, −1 none.</summary>
            public int Tex = -1;
            /// <summary>Scale x, y, offset x, y.</summary>
            public Vector4 St = new Vector4(1f, 1f, 0f, 0f);
            public object Unknown;
        }

        public sealed class MatData
        {
            public string Name, Shader;
            public int Queue;
            public string[] Keywords = Array.Empty<string>();
            public string[] PassesOff = Array.Empty<string>();
            public string RenderType = "";
            public readonly List<PropData> Props = new List<PropData>();
            /// <summary>The flat colour of a skin material's inside copy (gamma rgb), else null.</summary>
            public Vector3? Inside;
            public object Unknown;

            public PropData Prop(string name)
            {
                foreach (var p in Props) if (p.Name == name) return p;
                return null;
            }
        }

        public sealed class RendData
        {
            public int Node;
            public bool Skinned;
            public int Mesh = -1;
            public int[] Materials = Array.Empty<int>();
            public int[] Bones = Array.Empty<int>();
            public int Root = -1;
            /// <summary>Centre and size of the skinned bounds as the source had them (V6).</summary>
            public Bounds Bounds;
            public int Shadow = 1;
            public bool Receive = true;
            public int LightProbe = 1, ReflProbe = 1;
            public int Anchor = -1;
            public bool UpdateOffscreen;
            public int Quality;
            public string Part = "";
            public bool Member;
            public string Inside = InsideNone;
            public object Unknown;
        }

        public sealed class InsidesData
        {
            public bool Fill;
            public Vector3 Section = new Vector3(0.35f, 0.03f, 0.03f);
            public bool OwnInside = true;
            public object Unknown;
        }

        /// <summary>Index of the first node named <paramref name="name"/>, or −1.</summary>
        public int NodeIndex(string name)
        {
            for (int i = 0; i < Nodes.Count; i++) if (Nodes[i].Name == name) return i;
            return -1;
        }
    }

    /// <summary>One texture read back and handed over: the levels of one texture, largest first.</summary>
    public sealed class TexData
    {
        public int W, H;
        /// <summary>The stored format (what the texture is rebuilt as).</summary>
        public GraphicsFormat Format;
        public string SourceFormat;
        public readonly List<byte[]> Levels = new List<byte[]>();
        public long Bytes { get { long n = 0; foreach (var l in Levels) n += l.Length; return n; } }
    }

    /// <summary>Receives each texture of a capture as soon as it is read back (index into BakeData.Textures); the capture keeps no texels.</summary>
    public interface IBakeTextureSink
    {
        void Texture(BakeData bake, int index, TexData data);
    }

    /// <summary>What a capture takes.</summary>
    public sealed class BakeOptions
    {
        /// <summary>Part keys; null or empty: the whole character.</summary>
        public IReadOnlyCollection<string> Parts;
        /// <summary>The FK groups (Hair, Neck, Breast, Body, RightHand, LeftHand, Skirt); null: all seven.</summary>
        public bool[] FkGroups;
        /// <summary>Hollow parts get inside copies.</summary>
        public bool Insides = true;
        /// <summary>A part bake's origin at its surface centroid.</summary>
        public bool Centre = true;
        /// <summary>The blink is held open while capturing (V7); a test may switch it off to see the blink.</summary>
        public bool HoldBlinkOpen = true;
    }

    /// <summary>The result of a capture: the bake, where its item goes, and what the capture saw.</summary>
    public sealed class BakeCaptureResult
    {
        public BakeData Bake;
        /// <summary>The item's world pose: position, rotation, scale (the root's lossy scale).</summary>
        public Vector3 Position;
        public Quaternion Rotation = Quaternion.identity;
        public Vector3 Scale = Vector3.one;
        /// <summary>The live renderers in the order of BakeData.Renderers (null for none; tests and the Cutter read them).</summary>
        public readonly List<Renderer> Live = new List<Renderer>();
        /// <summary>The live transforms in the order of BakeData.Nodes (null for a node the capture added, as "centre").</summary>
        public readonly List<Transform> LiveNodes = new List<Transform>();
        public readonly FindingList Findings = new FindingList();
        public readonly Dictionary<string, int> Counts = new Dictionary<string, int>(StringComparer.Ordinal);
        public readonly Dictionary<string, double> Ms = new Dictionary<string, double>(StringComparer.Ordinal);
        /// <summary>The blink's open rate when the capture began (−1: no blink running).</summary>
        public float BlinkOpenRateSeen = -1f;
        /// <summary>The blend weights each skinned renderer was baked with (by its index in BakeData.Renderers).</summary>
        public readonly Dictionary<int, float[]> Weights = new Dictionary<int, float[]>();

        public void Inc(string k, int n = 1) { Counts.TryGetValue(k, out var v); Counts[k] = v + n; }
        public int Count(string k) => Counts.TryGetValue(k, out var v) ? v : 0;
        public void AddMs(string k, double ms) { Ms.TryGetValue(k, out var v); Ms[k] = v + ms; }
    }

    public static class BakeCapture
    {
        // ------------------------------------------------------------ parts

        /// <summary>One part present on a character (bake.parts).</summary>
        public sealed class PartInfo
        {
            public string Key, Label;
            public GameObject Root;
            /// <summary>An accessory slot whose materials got the hair shader pass ("(hair)").</summary>
            public bool HairType;
            public int Renderers;
        }

        static readonly string[] HairLabels = { "Hair back", "Hair front", "Hair side", "Hair extension" };
        static readonly string[] ClothesLabels = { "Top", "Bottom", "Inner top", "Inner bottom", "Gloves", "Pantyhose", "Socks", "Shoes" };

        /// <summary>The part roots of a built character, body first.</summary>
        public static List<PartInfo> PartRoots(CharaContext ctx)
        {
            var l = new List<PartInfo>();
            if (ctx == null) return l;
            if (ctx.Body) l.Add(new PartInfo { Key = "body", Label = "Body", Root = ctx.Body });
            if (ctx.Head) l.Add(new PartInfo { Key = "head", Label = "Head", Root = ctx.Head });
            for (int k = 0; k < ctx.Hair.Length && k < 4; k++)
                if (ctx.Hair[k]) l.Add(new PartInfo { Key = "hair" + k, Label = HairLabels[k], Root = ctx.Hair[k] });
            for (int k = 0; k < ctx.Clothes.Length && k < 8; k++)
                if (ctx.Clothes[k]) l.Add(new PartInfo { Key = "clothes" + k, Label = ClothesLabels[k], Root = ctx.Clothes[k] });
            if (ctx.Accessories != null)
                foreach (var a in ctx.Accessories)
                {
                    if (a == null || a.IsEmpty || !a.Object) continue;
                    bool hair = a.HairShaderType >= 0;
                    l.Add(new PartInfo { Key = "acc" + a.Slot, Label = "Accessory " + (a.Slot + 1) + (hair ? " (hair)" : ""), Root = a.Object, HairType = hair });
                }
            return l;
        }

        /// <summary>The parts present on a character with their renderer counts (what would be baked of each).</summary>
        public static List<PartInfo> Parts(CharaRealization c)
        {
            var parts = PartRoots(c?.Context);
            if (c == null || !c.Root) return parts;
            var map = PartMap(parts);
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var (r, _) in Renderers(c.Root.transform))
            {
                string k = PartOf(map, r.transform, c.Root.transform);
                counts.TryGetValue(k, out int n); counts[k] = n + 1;
            }
            foreach (var p in parts) p.Renderers = counts.TryGetValue(p.Key, out int n) ? n : 0;
            if (counts.TryGetValue("other", out int o) && o > 0) parts.Add(new PartInfo { Key = "other", Label = "Other", Renderers = o });
            return parts;
        }

        /// <summary>The part keys of a preset: body, hair, clothes, accessories; null for an unknown name.</summary>
        public static List<string> Preset(CharaContext ctx, string preset)
        {
            var l = new List<string>();
            foreach (var p in PartRoots(ctx))
            {
                bool hairSlot = p.Key.StartsWith("hair", StringComparison.Ordinal);
                bool acc = p.Key.StartsWith("acc", StringComparison.Ordinal);
                switch (preset)
                {
                    case "body": if (p.Key == "body" || p.Key == "head" || hairSlot || (acc && p.HairType)) l.Add(p.Key); break;
                    case "hair": if (hairSlot || (acc && p.HairType)) l.Add(p.Key); break;
                    case "clothes": if (p.Key.StartsWith("clothes", StringComparison.Ordinal)) l.Add(p.Key); break;
                    case "accessories": if (acc && !p.HairType) l.Add(p.Key); break;
                    default: return null;
                }
            }
            return l;
        }

        static Dictionary<Transform, string> PartMap(List<PartInfo> parts)
        {
            var m = new Dictionary<Transform, string>();
            foreach (var p in parts) if (p.Root && !m.ContainsKey(p.Root.transform)) m[p.Root.transform] = p.Key;
            return m;
        }

        /// <summary>The part of a renderer: the part whose root is its nearest ancestor (the transform itself included), else "other".</summary>
        static string PartOf(Dictionary<Transform, string> map, Transform t, Transform root)
        {
            for (var p = t; p; p = p.parent)
            {
                if (map.TryGetValue(p, out var k)) return k;
                if (p == root) break;
            }
            return "other";
        }

        // ------------------------------------------------------------ the walk

        /// <summary>The renderers a capture keeps under <paramref name="root"/>, in walk order, with their mesh: a skinned or
        /// mesh renderer with a mesh, enabled, active in the hierarchy; never inside a scene node holder.</summary>
        public static List<(Renderer r, Mesh mesh)> Renderers(Transform root, Func<Renderer, bool> skip = null) => Renderers(root, skip, out _);

        /// <summary>As above; a LOD group draws one level at a time: the renderers only its further levels hold are not kept
        /// (<paramref name="lodSkipped"/>), its nearest level is.</summary>
        public static List<(Renderer r, Mesh mesh)> Renderers(Transform root, Func<Renderer, bool> skip, out int lodSkipped)
        {
            var l = new List<(Renderer, Mesh)>();
            var further = new HashSet<Renderer>();
            foreach (var g in root.GetComponentsInChildren<LODGroup>(true))
            {
                if (!g.enabled || !g.gameObject.activeInHierarchy) continue;
                var lods = g.GetLODs();
                if (lods.Length <= 1) continue;
                var near = new HashSet<Renderer>();
                foreach (var r in lods[0].renderers) if (r) near.Add(r);
                for (int i = 1; i < lods.Length; i++)
                    foreach (var r in lods[i].renderers) if (r && !near.Contains(r)) further.Add(r);
            }
            lodSkipped = 0;
            var st = new Stack<Transform>();
            st.Push(root);
            var buf = new List<Renderer>();
            while (st.Count > 0)
            {
                var t = st.Pop();
                buf.Clear();
                t.GetComponents(buf);
                foreach (var r in buf)
                {
                    if (!r || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                    var m = MeshOf(r);
                    if (!m) continue;
                    if (skip != null && skip(r)) continue;
                    if (further.Contains(r)) { lodSkipped++; continue; }
                    l.Add((r, m));
                }
                for (int k = t.childCount - 1; k >= 0; k--)
                {
                    var c = t.GetChild(k);
                    if (SceneNodeHolder.IsHolder(c)) continue;
                    st.Push(c);
                }
            }
            return l;
        }

        /// <summary>The mesh a renderer draws: a skinned renderer's shared mesh, a mesh renderer's filter mesh; null for other kinds.</summary>
        public static Mesh MeshOf(Renderer r)
        {
            if (r is SkinnedMeshRenderer s) return s.sharedMesh;
            if (r is MeshRenderer && r.TryGetComponent<MeshFilter>(out var f)) return f.sharedMesh;
            return null;
        }

        // ------------------------------------------------------------ captures

        /// <summary>
        /// A character as drawn now: its parts (<see cref="BakeOptions.Parts"/>, none = the whole character), FK of the chosen groups,
        /// its joint-correction categories, insides when it lacks the body or the head, the origin. Main thread; needs a graphics device
        /// for the skinned meshes and the texture read-back. Textures go to <paramref name="sink"/> one at a time.
        /// </summary>
        public static BakeCaptureResult Chara(CharaRealization c, BakeOptions o, IBakeTextureSink sink)
        {
            if (c == null || c.State != RealizationState.Built || !c.Root || c.Context == null) throw new InvalidOperationException("the character is not built");
            var res = Context(c.Context, c.Root.transform, NameOf(c), c.Sex, FkDriver.TableFor(c), c.Correction, o, sink);
            Films(c, res);
            return res;
        }

        /// <summary>
        /// The wet films over the baked garments (BQ10): a film is a renderer of the runtime's wet root drawn over a garment; it is left out
        /// (bake.filmOmitted) and the stain's colour, which the garment's composite holds, stays.
        /// </summary>
        static void Films(CharaRealization c, BakeCaptureResult res)
        {
            var wet = c.Runtime?.GetPart<WetPart>();
            if (wet == null || !wet.Root) return;
            var meshes = new HashSet<Mesh>();
            foreach (var r in res.Live) if (r is SkinnedMeshRenderer s && s.sharedMesh) meshes.Add(s.sharedMesh);
            var names = new List<string>();
            foreach (var f in wet.Root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (f.enabled && f.gameObject.activeInHierarchy && f.sharedMesh && meshes.Contains(f.sharedMesh)) names.Add(f.sharedMesh.name);
            if (names.Count == 0) return;
            res.Inc("filmsOmitted", names.Count);
            res.Findings.Warning("bake.filmOmitted", string.Join(", ", names), names.Count + " wet film(s) left out; the stain's colour stays");
        }

        /// <summary>A built character by its context (a Maker build without a scene node as well): its FK table and joint correction given
        /// (null: none).</summary>
        public static BakeCaptureResult Context(CharaContext ctx, Transform root, string name, int sex, HS2Next.Motion.Fk.FkBoneTable fk,
            HS2Next.Motion.Corrective.CharaJointCorrection jc, BakeOptions o, IBakeTextureSink sink)
        {
            if (ctx == null || !root) throw new InvalidOperationException("the character is not built");
            o = o ?? new BakeOptions();
            var parts = PartRoots(ctx);
            var map = PartMap(parts);
            HashSet<string> chosen = o.Parts != null && o.Parts.Count > 0 ? new HashSet<string>(o.Parts, StringComparer.Ordinal) : null;
            var res = new BakeCaptureResult();
            var b = res.Bake = NewBake(chosen == null ? BakeData.KindChara : BakeData.KindParts);
            b.Parts = chosen == null ? null : new List<string>(SortParts(chosen, parts));
            b.Source = new BakeData.SourceInfo { Kind = "chara", Name = name ?? root.name, Sex = sex };
            bool centre = chosen != null && o.Centre;
            b.Origin = centre ? BakeData.OriginCentre : BakeData.OriginChara;
            var kept = Renderers(root, r => chosen != null && !chosen.Contains(PartOf(map, r.transform, root)), out int lod);
            if (lod > 0) res.Inc("lod.skipped", lod);
            using (BlinkHold.Open(ctx.Motion, o.HoldBlinkOpen, res))
                Capture(b, res, root, kept, r => PartOf(map, r.transform, root), sink);
            // FK: the character's FK bones of the chosen groups, pruned to what moves the bake, renamed apart
            BakeFk.ForChara(b, res, fk, o.FkGroups);
            // joint correction
            b.Correction = BakeFk.CorrectionOf(jc, b);
            // hide masks: a body, inner or pantyhose baked without the garment over it
            if (chosen != null) res.Inc("masksCleared", BakeMaterials.ClearMasks(b, !chosen.Contains("clothes0"), !chosen.Contains("clothes1")));
            // insides: a bake that lacks the body or the head
            b.Insides.Fill = o.Insides && chosen != null && !(chosen.Contains("body") && chosen.Contains("head"));
            if (b.Insides.Fill) foreach (var r in b.Renderers) if (BakeInsides.Eligible(b, r)) r.Inside = BakeData.InsideFill;
            Place(b, res, root, centre);
            return res;
        }

        /// <summary>
        /// A prop as drawn now: every kept renderer under <paramref name="root"/> (none skipped by part), the FK names given (found
        /// by name, pruned) or, when none, its own list (V20). The item's root pose is its world pose. Used for items, bakes and the
        /// importer's staging roots.
        /// </summary>
        public static BakeCaptureResult Prop(Transform root, IReadOnlyList<string> fkNames, string kind, IBakeTextureSink sink, Func<Renderer, bool> skip = null)
        {
            if (!root) throw new ArgumentNullException(nameof(root));
            var res = new BakeCaptureResult();
            var b = res.Bake = NewBake(kind ?? BakeData.KindItem);
            b.Origin = BakeData.OriginItem;
            b.Source = new BakeData.SourceInfo { Kind = "item", Name = root.name };
            var kept = Renderers(root, skip, out int lod);
            if (lod > 0) res.Inc("lod.skipped", lod);
            Capture(b, res, root, kept, _ => "", sink);
            BakeFk.ForProp(b, res, fkNames);
            res.Position = root.position; res.Rotation = root.rotation; res.Scale = root.lossyScale;
            return res;
        }

        /// <summary>A built item node as drawn: its instance with the writer's materials, its FK pose; the icon is not baked.</summary>
        public static BakeCaptureResult Item(ItemRealization it, IBakeTextureSink sink)
        {
            if (it == null || it.State != RealizationState.Built || !it.Instance) throw new InvalidOperationException("the item is not built");
            var icon = it.Icon;
            IReadOnlyList<string> fk = ItemMotion.Of(it)?.Fk?.Names;
            if (fk == null && it.Features != null) fk = it.Features.FkBones;
            var res = Prop(it.Instance.transform, fk, BakeData.KindItem, sink, r => icon && r == icon);
            var st = it.Node?.State as ItemNodeState;
            var b = res.Bake;
            b.Source = new BakeData.SourceInfo { Kind = BakeCache.PrototypeOf(it) != null ? "bake" : "item", Name = it.Node?.Doc?.Name ?? it.Instance.name, Ref = st?.Ref, Group = st?.Group ?? 0, Category = st?.Category ?? 0 };
            return res;
        }

        static string NameOf(CharaRealization c)
        {
            string n = c.Node?.Doc?.Name;
            if (!string.IsNullOrEmpty(n)) return n;
            return c.Root ? c.Root.name : "";
        }

        static IEnumerable<string> SortParts(HashSet<string> chosen, List<PartInfo> parts)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in parts) if (chosen.Contains(p.Key) && seen.Add(p.Key)) yield return p.Key;
            var rest = new List<string>();
            foreach (var k in chosen) if (!seen.Contains(k)) rest.Add(k);
            rest.Sort(StringComparer.Ordinal);
            foreach (var k in rest) yield return k;
        }

        public static BakeData NewBake(string kind) => new BakeData
        {
            Id = Guid.NewGuid().ToString("N"),
            Kind = kind,
            Created = Seconds(DateTime.UtcNow),
        };

        /// <summary>A UTC time to whole seconds.</summary>
        public static DateTime Seconds(DateTime t) => new DateTime(t.Ticks - t.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);

        // ------------------------------------------------------------ the common part

        /// <summary>Nodes, renderers, meshes, materials and textures of the kept renderers.</summary>
        static void Capture(BakeData b, BakeCaptureResult res, Transform root, List<(Renderer r, Mesh mesh)> kept, Func<Renderer, string> partOf, IBakeTextureSink sink)
        {
            var total = Stopwatch.StartNew();
            var sw = Stopwatch.StartNew();
            var index = new Dictionary<Transform, int>();
            // depth-first pre-order, children in order, never into a holder; the root at identity
            var st = new Stack<(Transform t, int parent)>();
            st.Push((root, -1));
            while (st.Count > 0)
            {
                var (t, par) = st.Pop();
                int me = b.Nodes.Count;
                index[t] = me;
                bool isRoot = t == root;
                t.GetLocalPositionAndRotation(out var lp, out var lr);
                b.Nodes.Add(new BakeData.NodeData
                {
                    Name = t.name, Parent = par,
                    P = isRoot ? Vector3.zero : lp, R = isRoot ? Quaternion.identity : lr, S = isRoot ? Vector3.one : t.localScale,
                    Active = isRoot || t.gameObject.activeSelf, Layer = t.gameObject.layer,
                });
                res.LiveNodes.Add(t);
                for (int k = t.childCount - 1; k >= 0; k--)
                {
                    var c = t.GetChild(k);
                    if (SceneNodeHolder.IsHolder(c)) continue;
                    st.Push((c, me));
                }
            }
            res.AddMs("nodes", sw.Elapsed.TotalMilliseconds);
            var mats = new BakeMaterials.Collector(b, res, sink);
            foreach (var (r, mesh) in kept)
            {
                if (!index.TryGetValue(r.transform, out int node)) continue;
                var smr = r as SkinnedMeshRenderer;
                var rd = new BakeData.RendData
                {
                    Node = node, Skinned = smr, Shadow = (int)r.shadowCastingMode, Receive = r.receiveShadows,
                    LightProbe = (int)r.lightProbeUsage, ReflProbe = (int)r.reflectionProbeUsage,
                    Anchor = r.probeAnchor && index.TryGetValue(r.probeAnchor, out int an) ? an : -1,
                    Part = partOf(r) ?? "", Member = MosaicSet.IsMember(r),
                };
                if (smr)
                {
                    var bones = smr.bones;
                    rd.Bones = new int[bones.Length];
                    // a bone outside the tree (or none) counts as the renderer's own node, as the build would draw it
                    for (int i = 0; i < bones.Length; i++) rd.Bones[i] = bones[i] && index.TryGetValue(bones[i], out int bi) ? bi : node;
                    rd.Root = smr.rootBone && index.TryGetValue(smr.rootBone, out int ri) ? ri : -1;
                    rd.Bounds = smr.localBounds;
                    rd.UpdateOffscreen = smr.updateWhenOffscreen;
                    rd.Quality = (int)smr.quality;
                }
                if (smr && mesh.blendShapeCount > 0)
                {
                    var bw = new float[mesh.blendShapeCount];
                    for (int i = 0; i < bw.Length; i++) bw[i] = smr.GetBlendShapeWeight(i);
                    res.Weights[b.Renderers.Count] = bw;
                }
                sw.Restart();
                var md = BakeMeshes.Capture(r, mesh, res, out bool skinned);
                res.AddMs("meshes", sw.Elapsed.TotalMilliseconds);
                if (md == null) { res.Inc("renderer.noMesh"); res.Weights.Remove(b.Renderers.Count); continue; }
                if (smr && !skinned)
                {
                    // a skinned renderer without weights or bind poses is stored as an unskinned renderer; an unreadable one posed
                    // hangs on its root bone
                    rd.Skinned = false; rd.Bones = Array.Empty<int>(); rd.Root = -1; rd.Bounds = default;
                    if (md.HangOn && index.TryGetValue(md.HangOn, out int hang)) rd.Node = hang;
                    res.Inc("renderer.skinnedAsStatic");
                }
                rd.Mesh = b.Meshes.Count;
                b.Meshes.Add(new BakeData.MeshRef { Name = md.Name, Data = md });
                sw.Restart();
                rd.Materials = mats.Of(r, rd.Member);
                res.AddMs("materials", sw.Elapsed.TotalMilliseconds);
                b.Renderers.Add(rd);
                res.Live.Add(r);
                if (rd.Member) res.Inc("members");
            }
            res.AddMs("capture", total.Elapsed.TotalMilliseconds);
        }

        // ------------------------------------------------------------ placement

        /// <summary>A whole bake at the root's world pose; a part bake with its origin at the area-weighted centroid of its posed surface.</summary>
        static void Place(BakeData b, BakeCaptureResult res, Transform root, bool centre)
        {
            res.Rotation = root.rotation;
            res.Scale = root.lossyScale;
            res.Position = root.position;
            if (!centre) return;
            if (!BakeMeshes.Centroid(b, res, out var c)) { b.Origin = BakeData.OriginChara; return; }
            res.Position = c;
            // a new root "centre" at the centroid, the old root under it at its offset
            var s = res.Scale;
            var off = Quaternion.Inverse(res.Rotation) * (root.position - c);
            off = new Vector3(s.x != 0f ? off.x / s.x : 0f, s.y != 0f ? off.y / s.y : 0f, s.z != 0f ? off.z / s.z : 0f);
            Reroot(b, res, "centre", off, Quaternion.identity);
        }

        /// <summary>Puts a new root node (<paramref name="name"/>, identity) above the bake's root, which gets the local pose given; every
        /// index into the nodes moves by one.</summary>
        public static void Reroot(BakeData b, BakeCaptureResult res, string name, Vector3 p, Quaternion r)
        {
            var old = b.Nodes[0];
            old.P = p; old.R = r;
            foreach (var n in b.Nodes) if (n.Parent >= 0) n.Parent++;
            old.Parent = 0;
            b.Nodes.Insert(0, new BakeData.NodeData { Name = name, Parent = -1, Layer = old.Layer });
            foreach (var rd in b.Renderers)
            {
                rd.Node++;
                if (rd.Root >= 0) rd.Root++;
                if (rd.Anchor >= 0) rd.Anchor++;
                for (int i = 0; i < rd.Bones.Length; i++) if (rd.Bones[i] >= 0) rd.Bones[i]++;
            }
            res?.LiveNodes.Insert(0, null);
        }

        // ------------------------------------------------------------ the blink (V7)

        /// <summary>Holds the blink open while a capture reads the face (BD8): the face is evaluated once with the eyes at the open
        /// value and no time, and evaluated again from its own state afterwards.</summary>
        sealed class BlinkHold : IDisposable
        {
            HS2Next.Motion.Face.ExpressionStage stage;
            float eyes, brow;

            public static IDisposable Open(HS2Next.Motion.CharaMotion motion, bool on, BakeCaptureResult res)
            {
                var h = new BlinkHold();
                var s = motion ? motion.Get<HS2Next.Motion.Face.ExpressionStage>() : null;
                if (s == null || !s.Enabled) return h;
                res.BlinkOpenRateSeen = s.Blink.FixedFlags == 0 ? s.Blink.OpenRate : -1f;
                if (!on || s.Blink.FixedFlags != 0 || s.Blink.OpenRate >= 1f) return h;
                h.stage = s;
                h.eyes = s.Eyes.OpenRate;
                h.brow = s.Eyebrow.OpenRate;
                s.Eyes.OpenRate = 1f;
                if (s.SyncBlink) s.Eyebrow.OpenRate = 1f;
                s.Eyebrow.Calculate(0f);
                s.Eyes.Calculate(0f);
                res.Inc("blink.heldOpen");
                return h;
            }

            public void Dispose()
            {
                if (stage == null) return;
                stage.Eyes.OpenRate = eyes;
                stage.Eyebrow.OpenRate = brow;
                stage.Evaluate();
                stage = null;
            }
        }
    }
}
