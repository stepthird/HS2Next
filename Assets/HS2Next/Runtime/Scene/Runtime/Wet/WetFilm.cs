// The gloss of a wet garment: a hidden SkinnedMeshRenderer with the garment's mesh and bones and HS2Next/Hidden/WetFilm, blended over it.
// It lives under the runtime's wet root, never under a holder, so pickers, the mosaic set, part lists and thumbnails do not see it.
// One slot draws the film, the other submeshes HS2Next/Hidden/NoDraw. The mesh is not copied; a blend shape weight of the garment follows
// when it changes. The film's material is its own: the garment's values and keywords by name, then the film's. Its composite and mask
// come through its own property block. A garment of another shader family has no film.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>The garment shaders a film can follow.</summary>
    public enum WetFilmFamily { None, True, Clothes, ClothesAlpha, TrueAlpha }

    public sealed class WetFilm : IDisposable
    {
        public const string ShaderName = "HS2Next/Hidden/WetFilm";
        const string NoDrawName = "HS2Next/Hidden/NoDraw";
        const int MinQueue = 2650;
        const float FogAllModes = 7f;

        static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        static readonly int MaskId = Shader.PropertyToID("_WetMask");
        static readonly int ExGlossId = Shader.PropertyToID("_ExGloss");
        static readonly int SmoothId = Shader.PropertyToID("_WetFilmSmooth");
        static readonly int OpacityId = Shader.PropertyToID("_WetFilmOpacity");
        static readonly int FogId = Shader.PropertyToID("_WetFilmFog");
        static readonly int FogModesId = Shader.PropertyToID(HS2FogFeature.ModesProperty);
        static readonly string[] Keywords = { "_DITHERSWITCH_ON", "_EMISSIONCOLORTEXBASE_ON" };

        static Material noDraw;
        static Material NoDraw
        {
            get
            {
                if (noDraw) return noDraw;
                var s = Shader.Find(NoDrawName);
                if (s) noDraw = new Material(s) { name = "HS2Next wet no draw", hideFlags = HideFlags.HideAndDontSave };
                return noDraw;
            }
        }

        readonly SkinnedMeshRenderer source;
        readonly WetCompose compose;
        readonly Mesh mesh;
        readonly SkinnedMeshRenderer film;
        readonly Material material;
        readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        readonly WetFilmFamily family;
        readonly int submesh;
        readonly float[] weights;
        float gloss = 0.9f, smooth = 0.8f, opacity = 0.85f;
        int boundVersion = -1;
        bool suspended, disposed;

        /// <summary>The hidden renderer (tests).</summary>
        public SkinnedMeshRenderer Renderer => film;
        /// <summary>The film's material (tests).</summary>
        public Material Material => material;
        public WetFilmFamily Family => family;

        /// <summary>The family of a garment's material; None: no film.</summary>
        public static WetFilmFamily FamilyOf(Material m)
        {
            if (!m || !m.shader) return WetFilmFamily.None;
            switch (m.shader.name)
            {
                case "HS2Next/AIT Clothes True": return WetFilmFamily.True;
                case "HS2Next/AIT Clothes": return WetFilmFamily.Clothes;
                case "HS2Next/AIT Clothes Alpha": return WetFilmFamily.ClothesAlpha;
                case "HS2Next/AIT Clothes Alpha True":
                case "HS2Next/AIT Clothes Alpha True BackCull": return WetFilmFamily.TrueAlpha;
                default: return WetFilmFamily.None;
            }
        }

        /// <summary>The film of a target, under <paramref name="root"/>; null when its garment has none (a shader of another family, not
        /// a skinned part, no mesh, no shader).</summary>
        public static WetFilm Create(Transform root, WetCompose target)
        {
            var src = target?.Renderer as SkinnedMeshRenderer;
            var garment = target?.MaterialNow();
            var family = FamilyOf(garment);
            var shader = Shader.Find(ShaderName);
            if (!src || !src.sharedMesh || family == WetFilmFamily.None || !shader || !NoDraw) return null;
            return new WetFilm(root, src, target, garment, family, shader);
        }

        WetFilm(Transform root, SkinnedMeshRenderer src, WetCompose target, Material garment, WetFilmFamily family, Shader shader)
        {
            source = src; compose = target; this.family = family; mesh = src.sharedMesh;
            submesh = Math.Min(target.Slot, Math.Max(0, mesh.subMeshCount - 1));
            var go = new GameObject("wet film " + src.name) { hideFlags = HideFlags.HideAndDontSave, layer = src.gameObject.layer };
            go.transform.SetParent(root, false);
            film = go.AddComponent<SkinnedMeshRenderer>();
            film.sharedMesh = mesh;
            film.bones = src.bones;
            film.rootBone = src.rootBone;
            film.localBounds = src.localBounds;
            film.quality = src.quality;
            film.updateWhenOffscreen = src.updateWhenOffscreen;
            film.probeAnchor = src.probeAnchor;
            film.lightProbeUsage = src.lightProbeUsage;
            film.reflectionProbeUsage = src.reflectionProbeUsage;
            film.receiveShadows = src.receiveShadows;
            film.renderingLayerMask = src.renderingLayerMask;
            film.shadowCastingMode = ShadowCastingMode.Off;
            film.skinnedMotionVectors = false;
            film.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            material = new Material(shader) { name = "HS2Next wet film", hideFlags = HideFlags.HideAndDontSave };
            var mats = new Material[Math.Max(1, mesh.subMeshCount)];
            for (int i = 0; i < mats.Length; i++) mats[i] = i == submesh ? material : NoDraw;
            film.sharedMaterials = mats;
            weights = new float[mesh.blendShapeCount];
            for (int i = 0; i < weights.Length; i++) weights[i] = float.NaN;
            Refresh();
            Follow();
        }

        /// <summary>The film's own values.</summary>
        public void Set(float filmGloss, float filmSmooth, float filmOpacity)
        {
            gloss = filmGloss; smooth = filmSmooth; opacity = filmOpacity;
            Own();
        }

        void Own()
        {
            material.SetFloat(ExGlossId, gloss);
            material.SetFloat(SmoothId, smooth);
            material.SetFloat(OpacityId, opacity);
        }

        /// <summary>The garment's values and keywords again (after a flush of the character or a change of its state), then the film's.</summary>
        public void Refresh()
        {
            var garment = compose.MaterialNow();
            if (!garment) return;
            material.CopyMatchingPropertiesFromMaterial(garment);
            foreach (var k in Keywords)
                if (garment.IsKeywordEnabled(k)) material.EnableKeyword(k); else material.DisableKeyword(k);
            Switch(material, "_WETFILM_ITEM", family == WetFilmFamily.Clothes);
            Switch(material, "_WETFILM_CALPHA", family == WetFilmFamily.ClothesAlpha);
            Switch(material, "_WETFILM_TRUEALPHA", family == WetFilmFamily.TrueAlpha);
            material.renderQueue = Math.Max(garment.renderQueue + 1, MinQueue);
            // an opaque garment takes the fog pass, which runs before the film: the film carries the term of every mode; a blended one its own modes
            bool blended = family == WetFilmFamily.ClothesAlpha || family == WetFilmFamily.TrueAlpha;
            material.SetFloat(FogId, blended ? (garment.HasProperty(FogModesId) ? garment.GetFloat(FogModesId) : 0f) : FogAllModes);
            Own();
        }

        static void Switch(Material m, string keyword, bool on)
        {
            if (on) m.EnableKeyword(keyword); else m.DisableKeyword(keyword);
        }

        /// <summary>
        /// Per frame: the film follows the garment's visibility and blend shapes, and draws the current composite. False when the garment is
        /// gone or draws another mesh: the target must be resolved again.
        /// </summary>
        public bool LateApply()
        {
            if (disposed) return false;
            if (!source || source.sharedMesh != mesh) return false;
            Follow();
            return true;
        }

        void Follow()
        {
            bool show = !suspended && compose.Wet && compose.Mask && source.enabled && source.gameObject.activeInHierarchy;
            if (film.enabled != show) film.enabled = show;
            if (film.forceRenderingOff != source.forceRenderingOff) film.forceRenderingOff = source.forceRenderingOff;
            if (!show) return;
            if (boundVersion != compose.Version)
            {
                film.GetPropertyBlock(block, submesh);
                block.SetTexture(MainTexId, compose.Wet);
                block.SetTexture(MaskId, compose.Mask);
                film.SetPropertyBlock(block, submesh);
                boundVersion = compose.Version;
            }
            for (int i = 0; i < weights.Length; i++)
            {
                float w = source.GetBlendShapeWeight(i);
                if (w != weights[i]) { weights[i] = w; film.SetBlendShapeWeight(i, w); }
            }
        }

        /// <summary>The film off while on is true (a thumbnail shows the character dry).</summary>
        public void Suspend(bool on)
        {
            suspended = on;
            if (!disposed && source) Follow();
            else if (!disposed && film) film.enabled = false;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Destroy(material);
            if (film) Destroy(film.gameObject);
        }

        static void Destroy(Object o)
        {
            if (!o) return;
            if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o);
        }
    }
}
