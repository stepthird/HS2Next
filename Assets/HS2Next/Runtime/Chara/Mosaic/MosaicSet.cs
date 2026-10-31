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
// Nothing here turns the mosaic off, changes its block or its method: there is no such switch (spec E1-E9 table). The
// typed overrides refuse every member and every material with the mosaic shader (OverrideResult.Protected, finding
// api.mosaicProtected); MaterialEditor items on members are kept in the documents but not applied (me.mosaicProtected,
// MaterialEditorApplier).
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
        static readonly object Mark = new object();

        /// <summary>True when the renderer is a member (name, a slot's material, or added).</summary>
        public static bool IsMember(Renderer r)
        {
            if (!r) return false;
            if (IsMemberName(r.name) || added.TryGetValue(r, out _)) return true;
            foreach (var m in r.sharedMaterials) if (IsMosaicMaterial(m)) return true;
            return false;
        }

        /// <summary>True when the renderer was added through <see cref="Add"/>.</summary>
        public static bool IsAdded(Renderer r) => r && added.TryGetValue(r, out _);

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
            if (!r || standalone.Exists(m => m.Renderer == r)) return;
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
