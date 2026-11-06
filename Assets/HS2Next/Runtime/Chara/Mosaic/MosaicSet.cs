// The mosaic set (package R of 5.M): which renderers always draw the mosaic, the per-character
// record of what the mosaic unit did (the read model of chara.mosaic), the additive API (E7), the typed material / renderer
// overrides (E6) and their refusal of the set.
// Members (decided at the build and after every flush that re-runs the mosaic stage):
//   - renderers named o_mnpa, o_mnpb (the female patches), cm_o_dan00, cm_o_dan_f (the genital meshes), o_dankon, o_dan_f,
//     o_gomu (the part names of the common uncensor packs);
//   - every renderer of a character, its clothes, accessories or hair, or of a Studio item, with a slot whose material's
//     original shader is AIT/mnpb (abdata or a zipmod; seen as that name, as its replacement HS2Next/Mosaic, or as the
//     material name c_m_mnpb);
//   - renderers added through Add (E7): they add coverage; nothing removes a member.
// A body part a plugin supplied through body-part replacement (MarkSupplied) is outside the first two rules: it is a member
// only when added. Every other renderer follows the rules above.
// Nothing here turns the mosaic off, changes its block or its method: there is no such switch (spec E1-E9 table). The
// typed overrides refuse every member and every material with the mosaic shader (OverrideResult.Protected, finding
// api.mosaicProtected); MaterialEditor items on members are kept in the documents but not applied (me.mosaicProtected,
// MaterialEditorApplier).
// Advanced pose never moves a member off the skin it covers and never changes a member's shape: GuardsBone and GuardsBlend say what
// its corrections and blend overrides leave alone (a command refuses them, a scene file's entries are kept and not applied).
// Material edits ask MaterialGuard: a refused edit makes no change, and a stored item it refuses is kept and not applied.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HS2Next.Save;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HS2Next.Chara.Mosaic
{
    /// <summary>How a member draws its mosaic (the shader's _MosaicMode).</summary>
    public enum MosaicMode { Procedural = 0, Texture = 1 }

    /// <summary>One member renderer of a character (or a standalone one) and what the mosaic unit gave it.</summary>
    public sealed class MosaicMember
    {
        public Renderer Renderer;
        /// <summary>The renderer's path under the character root ("a/b/o_mnpb"; the name for a standalone member).</summary>
        public string Path;
        public MosaicMode Mode;
        /// <summary>The texture drawn in texture mode (its name in the read model), null in procedural mode.</summary>
        public Texture Source;
        /// <summary>"body" (the body composite at transferred body UVs), "own" (the part's own main texture), null.</summary>
        public string SourceKind;
        /// <summary>Every vertex lies on the body (the transferred UVs and the "#bodyuv" mesh copy are used).</summary>
        public bool BodySurface;
        /// <summary>Restored by the patch guard from the vanilla body.</summary>
        public bool Restored;
        /// <summary>Added through <see cref="MosaicSet.Add"/>.</summary>
        public bool Added;
        /// <summary>The mosaic material of each slot (made by the mosaic unit, owned by the character).</summary>
        public Material[] Materials = Array.Empty<Material>();
        /// <summary>The region (centre, size, UV density, skinning bones) in the drawn mesh's space.</summary>
        public MosaicRegion Region;
        /// <summary>The region's bones and bind poses (skinned members; empty: the renderer's own transform).</summary>
        public Transform[] RegionBones = Array.Empty<Transform>();
        public Matrix4x4[] RegionBind = Array.Empty<Matrix4x4>();
        public float[] RegionWeights = Array.Empty<float>();
        /// <summary>The property block the camera step writes (per camera).</summary>
        public MaterialPropertyBlock Block;
        /// <summary>The last camera's values: block (UV), block (pixels along the densest UV direction), cell (pixels).</summary>
        public float BlockUv, BlockPx, CellPx;
        public string LastCamera;
        // this camera's geometry (MosaicCamera; the wide pass reads it)
        internal Vector3 WorldCentre;
        internal float WorldRadius;
        internal bool Drawn;
        internal int Frame = -1;
        internal float PxPerUv, SharedBlockUv;
    }

    /// <summary>A MaterialEditor item that was not applied because it targets a member (finding me.mosaicProtected).</summary>
    public sealed class MosaicProtectedItem
    {
        /// <summary>The item kind ("RendererProperty", "FloatProperty", ...), its object ("Character 0"), the renderer or
        /// material it names and the property.</summary>
        public string Kind, Object, Target, Property;
        public override string ToString() => $"{Kind} {Object} {Target}{(string.IsNullOrEmpty(Property) ? "" : "." + Property)}";
    }

    /// <summary>What the mosaic unit did on one character (the read model of chara.mosaic).</summary>
    public sealed class MosaicState
    {
        public CharaContext Chara;
        public readonly List<MosaicMember> Members = new List<MosaicMember>();
        /// <summary>Patch renderers the guard restored in the last run (0 on this install's bodies).</summary>
        public int Restored;
        /// <summary>Every patch renderer the guard has put on this character (they stay members marked restored).</summary>
        internal readonly HashSet<Renderer> RestoredRenderers = new HashSet<Renderer>();
        public int Runs;
        public double LastMilliseconds;
        /// <summary>Findings of the last run (mosaic.patchesRestored, mosaic.transferUnavailable).</summary>
        public FindingList Findings = new FindingList();
        public bool Alive => Chara != null && Chara.Root;
        // MosaicSet.SurfaceOnlyBones of this run: the members change only when the stage runs or adopts one
        internal HashSet<Transform> SurfaceOnly;
        internal int SurfaceOnlyRuns, SurfaceOnlyMembers;
    }

    /// <summary>The result of a typed override (E6).</summary>
    public enum OverrideResult { Applied = 0, Protected = 1, Missing = 2 }

    public static class MosaicSet
    {
        /// <summary>The replacement shader of HS2's AIT/mnpb and the material of every member slot.</summary>
        public const string ShaderName = "HS2Next/Mosaic";
        /// <summary>HS2's mosaic shader and material names.</summary>
        public const string OriginalShader = "AIT/mnpb", OriginalMaterial = "c_m_mnpb";
        /// <summary>The replacement before 5.M (a flat colour); still recognised as a mosaic shader.</summary>
        public const string LegacyShader = "HS2Next/AIT mnpb";
        /// <summary>The members' render queue (the opaque range).</summary>
        public const int Queue = 2500;
        public const string ProtectedCode = "api.mosaicProtected", MeProtectedCode = "me.mosaicProtected",
            RestoredCode = "mosaic.patchesRestored", TransferCode = "mosaic.transferUnavailable";
        /// <summary>The skip reason MaterialEditor counts for items on members (ExtStats.MeSkipped).</summary>
        public const string MeSkipReason = "mosaicProtected";

        /// <summary>Renderer names that are always members.</summary>
        public static IReadOnlyCollection<string> MemberNames => memberNames;
        static readonly HashSet<string> memberNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "o_mnpa", "o_mnpb", "cm_o_dan00", "cm_o_dan_f", "o_dankon", "o_dan_f", "o_gomu",
        };

        public static bool IsMemberName(string name) => name != null && memberNames.Contains(Trim(name));

        static string Trim(string n) => n.Replace("(Clone)", "").Replace("(Instance)", "").Replace(" Instance", "").Trim();

        /// <summary>AIT/mnpb, its replacement HS2Next/Mosaic or the earlier flat replacement.</summary>
        public static bool IsMosaicShaderName(string name) => name == OriginalShader || name == ShaderName || name == LegacyShader;

        /// <summary>A material whose shader is a mosaic shader or whose name is HS2's mosaic material.</summary>
        public static bool IsMosaicMaterial(Material m) =>
            m && ((m.shader && IsMosaicShaderName(m.shader.name)) || Trim(m.name ?? "") == OriginalMaterial);

        static readonly ConditionalWeakTable<Renderer, object> added = new ConditionalWeakTable<Renderer, object>();
        static readonly ConditionalWeakTable<Renderer, object> supplied = new ConditionalWeakTable<Renderer, object>();
        static readonly object Mark = new object();

        /// <summary>True when the renderer is a member (name, a slot's material, or added; a supplied body part only when
        /// added).</summary>
        public static bool IsMember(Renderer r)
        {
            if (!r) return false;
            if (added.TryGetValue(r, out _)) return true;
            if (supplied.TryGetValue(r, out _)) return false;
            if (IsMemberName(r.name)) return true;
            foreach (var m in r.sharedMaterials) if (IsMosaicMaterial(m)) return true;
            return false;
        }

        /// <summary>True when the renderer was added through <see cref="Add"/>.</summary>
        public static bool IsAdded(Renderer r) => r && added.TryGetValue(r, out _);

        /// <summary>Records <paramref name="r"/> as a body part a plugin supplied in its character's current build
        /// (body-part replacement); a rebuild makes new renderers, which carry no record.</summary>
        public static void MarkSupplied(Renderer r)
        {
            if (r && !supplied.TryGetValue(r, out _)) supplied.Add(r, Mark);
        }

        /// <summary>True when <paramref name="r"/> is a body part a plugin supplied (<see cref="MarkSupplied"/>).</summary>
        public static bool IsSupplied(Renderer r) => r && supplied.TryGetValue(r, out _);

        // ---------------------------------------------------------------- per-character state

        static readonly ConditionalWeakTable<CharaContext, MosaicState> states = new ConditionalWeakTable<CharaContext, MosaicState>();
        static readonly List<WeakReference<MosaicState>> live = new List<WeakReference<MosaicState>>();
        static readonly List<MosaicMember> standalone = new List<MosaicMember>();

        /// <summary>The character's mosaic record (null before the mosaic unit first ran on it).</summary>
        public static MosaicState StateOf(CharaContext ctx) => ctx != null && states.TryGetValue(ctx, out var s) ? s : null;

        internal static MosaicState StateFor(CharaContext ctx)
        {
            if (states.TryGetValue(ctx, out var s)) return s;
            s = new MosaicState { Chara = ctx };
            states.Add(ctx, s);
            live.Add(new WeakReference<MosaicState>(s));
            return s;
        }

        /// <summary>The records of the characters that exist now (destroyed ones are dropped).</summary>
        public static List<MosaicState> LiveStates()
        {
            var l = new List<MosaicState>();
            for (int i = live.Count - 1; i >= 0; i--)
            {
                if (live[i].TryGetTarget(out var s) && s.Alive) l.Add(s);
                else if (!live[i].TryGetTarget(out _)) live.RemoveAt(i);
            }
            l.Reverse();
            return l;
        }

        /// <summary>Members that belong to no character record: renderers added outside a character and mosaic-shader
        /// renderers the shader map swapped (Studio items, the silhouette body).</summary>
        public static List<MosaicMember> Standalone()
        {
            standalone.RemoveAll(m => !m.Renderer);
            return standalone;
        }

        /// <summary>The character record whose root holds <paramref name="r"/>, or null.</summary>
        public static MosaicState OwnerOf(Renderer r)
        {
            if (!r) return null;
            foreach (var s in LiveStates())
                if (r.transform.IsChildOf(s.Chara.Root.transform)) return s;
            return null;
        }

        /// <summary>
        /// Tracks a renderer that drew AIT/mnpb and was mapped to HS2Next/Mosaic outside a character's mosaic unit (the
        /// shader map: Studio items, map parts, a character's silhouette body built later): the camera step gives it the
        /// per-camera cell and the wide mosaic covers it. A renderer of a character with a record joins that record.
        /// </summary>
        public static void Track(Renderer r)
        {
            if (!r || standalone.Exists(m => m.Renderer == r) || (IsSupplied(r) && !IsAdded(r))) return;
            var owner = OwnerOf(r);
            if (owner != null) { MosaicUnit.Adopt(owner, r); return; }
            standalone.Add(MosaicUnit.Standalone(r, added: IsAdded(r)));
        }

        /// <summary>
        /// The additive mosaic (E7): <paramref name="r"/> becomes a member from now on. On a character the mosaic stage runs
        /// again in the next flush (and once now); elsewhere the renderer gets the mosaic material at once. Returns false for
        /// a missing renderer. Nothing removes a member.
        /// </summary>
        public static bool Add(Renderer r)
        {
            if (!r) return false;
            if (!added.TryGetValue(r, out _)) added.Add(r, Mark);
            var owner = OwnerOf(r);
            if (owner != null)
            {
                owner.Chara.Editor?.MarkUnit(HS2Next.Chara.Edit.Units.MosaicStageUnit.UnitId);
                MosaicUnit.ApplyAdded(owner, r);
                return true;
            }
            var m = standalone.Find(x => x.Renderer == r);
            if (m != null) { m.Added = true; return true; }
            standalone.Add(MosaicUnit.Standalone(r, added: true));
            return true;
        }

        // ---------------------------------------------------------------- MaterialEditor

        /// <summary>The character's MaterialEditor items that are kept but not applied (they target a member; a function of
        /// the plugin data and the members, so the same after an edit as after a fresh build).</summary>
        public static List<MosaicProtectedItem> ProtectedItems(CharaContext ctx) => HS2Next.Chara.Ext.MaterialEditorApplier.MosaicProtected(ctx);

        // ---------------------------------------------------------------- E6 typed overrides

        /// <summary>True when the typed overrides refuse <paramref name="r"/> (a member).</summary>
        public static bool Protects(Renderer r) => IsMember(r);

        /// <summary>True when the typed overrides refuse <paramref name="m"/> (a mosaic material).</summary>
        public static bool ProtectsMaterial(Material m) => IsMosaicMaterial(m);

        // ---------------------------------------------------------------- advanced pose

        /// <summary>
        /// True when a bone correction leaves <paramref name="t"/> alone: a member renderer's own transform; inside a part (hair,
        /// clothes, an accessory) an ancestor of a member or a bone it is skinned to, which would move or reshape the member alone; a
        /// bone a body-surface member is skinned to and the body renderer is not, which would move the patch without the skin. A bone
        /// both skin moves skin and patch together, a bone only a separate part is skinned to moves a mesh that draws its own mosaic.
        /// </summary>
        public static bool GuardsBone(CharaContext ctx, Transform t)
        {
            if (ctx == null || !t) return false;
            foreach (var r in t.GetComponents<Renderer>())
                if (IsMember(r)) return true;
            return SurfaceOnlyBones(ctx).Contains(t) || GuardsInPart(ctx, t);
        }

        /// <summary>True when a blend override leaves <paramref name="r"/> alone: a member, whose shape it would change, or the body
        /// renderer under a body-surface member, whose shapes could pull the skin from under the patch.</summary>
        public static bool GuardsBlend(CharaContext ctx, Renderer r)
        {
            if (!r) return false;
            if (IsMember(r)) return true;
            return ctx != null && r == ctx.BodyRenderer && SurfaceMembers(ctx).Count > 0;
        }

        /// <summary>The bones a body-surface member of the character is skinned to with a weight above 0 and the body renderer is not.</summary>
        public static HashSet<Transform> SurfaceOnlyBones(CharaContext ctx)
        {
            var st = StateOf(ctx);
            if (st != null && st.SurfaceOnly != null && st.SurfaceOnlyRuns == st.Runs && st.SurfaceOnlyMembers == st.Members.Count) return st.SurfaceOnly;
            var set = new HashSet<Transform>();
            var members = SurfaceMembers(ctx);
            if (members.Count > 0)
            {
                var body = new HashSet<Transform>(SkinnedBones(ctx.BodyRenderer));
                foreach (var m in members)
                    foreach (var b in SkinnedBones(m))
                        if (!body.Contains(b)) set.Add(b);
            }
            if (st != null) { st.SurfaceOnly = set; st.SurfaceOnlyRuns = st.Runs; st.SurfaceOnlyMembers = st.Members.Count; }
            return set;
        }

        /// <summary>The body-surface members of a character: skinned members of the body object that lie on the skin (the patches).</summary>
        public static List<SkinnedMeshRenderer> SurfaceMembers(CharaContext ctx)
        {
            var l = new List<SkinnedMeshRenderer>();
            if (ctx == null || !ctx.Body || !ctx.BodyRenderer) return l;
            var st = StateOf(ctx);
            foreach (var s in ctx.Body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (s == ctx.BodyRenderer || !IsMember(s)) continue;
                var name = Trim(s.name);
                if (name == "o_mnpa" || name == "o_mnpb" || (st != null && st.Members.Exists(m => m.Renderer == s && m.BodySurface))) l.Add(s);
            }
            return l;
        }

        /// <summary>The transforms a skinned renderer is bound to with a weight above 0. A mesh that cannot be read counts with every
        /// bone it lists.</summary>
        public static List<Transform> SkinnedBones(SkinnedMeshRenderer s)
        {
            var l = new List<Transform>();
            var mesh = s ? s.sharedMesh : null;
            if (!mesh) return l;
            var bones = s.bones;
            var used = WeightedBones(mesh);
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] && (used == null || (i < used.Length && used[i]))) l.Add(bones[i]);
            return l;
        }

        static readonly ConditionalWeakTable<Mesh, bool[]> weighted = new ConditionalWeakTable<Mesh, bool[]>();

        // by bone index: some vertex has a weight above 0 on it; null when the mesh cannot be read
        static bool[] WeightedBones(Mesh mesh)
        {
            if (weighted.TryGetValue(mesh, out var used)) return used;
            if (!mesh.isReadable) return null;
            used = new bool[Math.Max(mesh.bindposeCount, 1)];
            void Mark(int i, float w) { if (w > 0f && i >= 0 && i < used.Length) used[i] = true; }
            foreach (var w in mesh.boneWeights)
            {
                Mark(w.boneIndex0, w.weight0);
                Mark(w.boneIndex1, w.weight1);
                Mark(w.boneIndex2, w.weight2);
                Mark(w.boneIndex3, w.weight3);
            }
            weighted.Add(mesh, used);
            return used;
        }

        // inside a part: an ancestor of a member of that part, or a bone a member of that part is skinned to; attached scene nodes are not part of it
        static bool GuardsInPart(CharaContext ctx, Transform t)
        {
            var top = PartTop(ctx, t);
            if (!top) return false;
            foreach (var r in top.GetComponentsInChildren<Renderer>(true))
            {
                if (!IsMember(r) || UnderHolder(r.transform, top)) continue;
                if (r.transform.IsChildOf(t)) return true;
                if (r is SkinnedMeshRenderer s && SkinnedBones(s).Contains(t)) return true;
            }
            return false;
        }

        static bool UnderHolder(Transform x, Transform top)
        {
            for (; x && x != top; x = x.parent)
                if (HS2Next.Scenes.Runtime.SceneNodeHolder.IsHolder(x)) return true;
            return false;
        }

        static Transform PartTop(CharaContext ctx, Transform t)
        {
            bool In(GameObject o) => o && t.IsChildOf(o.transform);
            foreach (var o in ctx.Hair) if (In(o)) return o.transform;
            foreach (var o in ctx.Clothes) if (In(o)) return o.transform;
            if (ctx.Accessories != null)
                foreach (var a in ctx.Accessories) if (a != null && In(a.Object)) return a.Object.transform;
            return null;
        }
    }

    /// <summary>
    /// The one judge of material edits, for the commands, the writer and the status reads: what MosaicSet protects stays out of
    /// reach of an edit, by these three questions. A character's stored items keep the applier's rule by name.
    /// </summary>
    public static class MaterialGuard
    {
        /// <summary>A member of the mosaic set.</summary>
        public static bool Renderer(UnityEngine.Renderer r) => MosaicSet.Protects(r);

        /// <summary>A mosaic material.</summary>
        public static bool Material(UnityEngine.Material m) => MosaicSet.ProtectsMaterial(m);

        /// <summary>A mosaic shader's name, or a name that resolves to one.</summary>
        public static bool Shader(string name)
        {
            if (MosaicSet.IsMosaicShaderName(name)) return true;
            if (name == null) return false;
            if (ShaderMap.Fitted.TryGetValue(name, out var fitted) && MosaicSet.IsMosaicShaderName(fitted)) return true;
            if (ShaderMap.CopyRoutes.TryGetValue(name, out var routes))
                foreach (var (_, replacement) in routes)
                    if (MosaicSet.IsMosaicShaderName(replacement)) return true;
            return false;
        }
    }

    /// <summary>
    /// Typed material / renderer overrides (E6, the plugin API of row 5.1): change one slot's property, texture or shader, or a
    /// renderer's enabled flag. Every member of the mosaic set and every material with a mosaic shader is refused
    /// (<see cref="OverrideResult.Protected"/>, finding api.mosaicProtected in <paramref name="findings"/>).
    /// </summary>
    public static class MaterialOverrides
    {
        static OverrideResult Refuse(Renderer r, Material m, FindingList findings, string what)
        {
            findings?.Warning(MosaicSet.ProtectedCode, r ? r.name : "", what + " refused: the mosaic set is protected");
            return OverrideResult.Protected;
        }

        static OverrideResult Target(Renderer r, int slot, FindingList findings, string what, out Material m)
        {
            m = null;
            if (!r) return OverrideResult.Missing;
            if (MosaicSet.Protects(r)) return Refuse(r, null, findings, what);
            var mats = r.sharedMaterials;
            if (slot < 0 || slot >= mats.Length || !mats[slot]) return OverrideResult.Missing;
            m = mats[slot];
            if (MosaicSet.ProtectsMaterial(m)) { var x = m; m = null; return Refuse(r, x, findings, what); }
            return OverrideResult.Applied;
        }

        public static OverrideResult SetFloat(Renderer r, int slot, string property, float value, FindingList findings = null)
        {
            var res = Target(r, slot, findings, "float " + property, out var m);
            if (res != OverrideResult.Applied) return res;
            if (!m.HasProperty(property)) return OverrideResult.Missing;
            m.SetFloat(property, value);
            return OverrideResult.Applied;
        }

        public static OverrideResult SetColor(Renderer r, int slot, string property, Color value, FindingList findings = null)
        {
            var res = Target(r, slot, findings, "colour " + property, out var m);
            if (res != OverrideResult.Applied) return res;
            if (!m.HasProperty(property)) return OverrideResult.Missing;
            m.SetColor(property, value);
            return OverrideResult.Applied;
        }

        public static OverrideResult SetTexture(Renderer r, int slot, string property, Texture value, FindingList findings = null)
        {
            var res = Target(r, slot, findings, "texture " + property, out var m);
            if (res != OverrideResult.Applied) return res;
            if (!m.HasProperty(property)) return OverrideResult.Missing;
            m.SetTexture(property, value);
            return OverrideResult.Applied;
        }

        public static OverrideResult SetShader(Renderer r, int slot, Shader shader, FindingList findings = null)
        {
            var res = Target(r, slot, findings, "shader", out var m);
            if (res != OverrideResult.Applied) return res;
            if (!shader) return OverrideResult.Missing;
            int q = m.renderQueue;
            MaterialCopies.SetShader(m, shader);
            m.renderQueue = q;
            return OverrideResult.Applied;
        }

        public static OverrideResult SetEnabled(Renderer r, bool enabled, FindingList findings = null)
        {
            if (!r) return OverrideResult.Missing;
            if (MosaicSet.Protects(r)) return Refuse(r, null, findings, "enabled");
            r.enabled = enabled;
            return OverrideResult.Applied;
        }
    }
}
