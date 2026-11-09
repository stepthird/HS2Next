// The stained image of one target (a renderer and a material slot) and the per-renderer override that shows it. Nothing is written to
// a material: a MaterialPropertyBlock binds the composite as _MainTex of that renderer and slot (and _BaseMap on a URP/Lit fallback);
// a dry target has no override and no render texture. The composite reads the material's current _MainTex, so it lies above every
// material edit, and is a pure function of that image and the stains' maps: the same inputs give the same bytes.
//   Wet    the base's size up to the cap, the base's wrap, filter, anisotropy and mips; sRGB when the base is sRGB data, so sampling the
//          composite outside a stain gives what sampling the base gave.
//   Mask   the film's mask (R8, the first map's size): the largest amount of the stains.
// A stain with a radius of 0 draws nothing. Other parts that draw the same image follow the override (Follow).
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Object = UnityEngine.Object;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>One stain on one target at one moment.</summary>
    public struct WetLayer
    {
        /// <summary>The stain's map of this target.</summary>
        public Texture Map;
        /// <summary>The radius now, feather, and what value 254 of the map stands for.</summary>
        public float Radius, Feather, Reach;
        /// <summary>The look: saturate, tint (rgb), stain colour (rgb) and amount.</summary>
        public float Saturate, Amount;
        public Color Tint, Stain;
        /// <summary>The kind's factor: 1 on clothes and hair, the look's skin value on skin and face.</summary>
        public float Skin;
    }

    public sealed class WetCompose : IDisposable
    {
        public const string ShaderName = "HS2Next/Hidden/WetCompose";
        /// <summary>The longest side of a composite: clothes and hair, skin and face.</summary>
        public const int ClothesCap = 2048, SkinCap = 4096;
        const int PassColour = 0, PassMask = 1;

        static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        static readonly int MapId = Shader.PropertyToID("_WetMap");
        static readonly int SpreadId = Shader.PropertyToID("_WetSpread");
        static readonly int TintId = Shader.PropertyToID("_WetTint");
        static readonly int StainId = Shader.PropertyToID("_WetStain");
        static readonly int EncodedId = Shader.PropertyToID("_WetEncoded");

        static Material material;
        static Material Mat
        {
            get
            {
                if (material) return material;
                var s = Shader.Find(ShaderName);
                if (s) material = new Material(s) { name = "HS2Next wet compose", hideFlags = HideFlags.HideAndDontSave };
                return material;
            }
        }

        readonly int cap;
        readonly bool wantMask;
        readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        readonly List<(Renderer r, int slot)> followers = new List<(Renderer, int)>();
        readonly List<Material> materials = new List<Material>();
        bool bound, suspended, disposed;

        /// <summary>The target.</summary>
        public Renderer Renderer { get; }
        public int Slot { get; }
        /// <summary>The composite and the film's mask; null while dry.</summary>
        public RenderTexture Wet { get; private set; }
        public RenderTexture Mask { get; private set; }
        /// <summary>The override is on the renderer now.</summary>
        public bool Bound => bound;
        /// <summary>Changes when <see cref="Wet"/> or <see cref="Mask"/> becomes another object.</summary>
        public int Version { get; private set; }

        /// <param name="cap"><see cref="ClothesCap"/> or <see cref="SkinCap"/>.</param>
        /// <param name="mask">The target has a film.</param>
        public WetCompose(Renderer renderer, int slot, int cap, bool mask)
        {
            Renderer = renderer; Slot = slot; this.cap = Math.Max(1, cap); wantMask = mask;
        }

        /// <summary>The material of a renderer's slot now, or null.</summary>
        public Material MaterialNow()
        {
            if (!Renderer) return null;
            Renderer.GetSharedMaterials(materials);
            return Slot < materials.Count ? materials[Slot] : null;
        }

        /// <summary>The image a material draws as its main texture; null: white.</summary>
        public static Texture BaseOf(Material m)
        {
            if (!m) return null;
            if (ShaderMap.IsFallback(m) && m.HasProperty(BaseMapId)) return m.GetTexture(BaseMapId);
            return m.HasProperty(MainTexId) ? m.GetTexture(MainTexId) : null;
        }

        static bool IsSrgb(Texture t) => t is RenderTexture rt ? rt.sRGB : t.isDataSRGB;
        static bool HasMips(Texture t) => t is RenderTexture rt ? rt.useMipMap : t.mipmapCount > 1;

        /// <summary>
        /// Draws the layers in order over the material's current image and binds the override. With no layer that draws, the target goes
        /// dry: no override, no render texture. False when the target has no composite after the call.
        /// </summary>
        public bool Compose(IReadOnlyList<WetLayer> layers)
        {
            if (disposed || !Mat) return false;
            int active = 0;
            Texture firstMap = null;
            if (layers != null)
                for (int i = 0; i < layers.Count; i++)
                    if (layers[i].Map && layers[i].Radius > 0f) { active++; firstMap ??= layers[i].Map; }
            var m = MaterialNow();
            if (active == 0 || !m) { Dry(); return false; }
            var input = BaseOf(m);
            if (!input) input = Texture2D.whiteTexture;
            Ensure(input, firstMap);
            Mat.SetFloat(EncodedId, Wet.sRGB ? 1f : 0f);

            Texture current = input;
            RenderTexture held = null;
            var desc = Wet.descriptor;
            desc.useMipMap = false; desc.autoGenerateMips = false;
            for (int i = 0, left = active; i < layers.Count; i++)
            {
                var l = layers[i];
                if (!l.Map || !(l.Radius > 0f)) continue;
                left--;
                Set(l);
                var dst = left == 0 ? Wet : RenderTexture.GetTemporary(desc);
                Graphics.Blit(current, dst, Mat, PassColour);
                if (held) RenderTexture.ReleaseTemporary(held);
                held = left == 0 ? null : dst;
                current = dst;
            }
            if (Wet.useMipMap) Wet.GenerateMips();

            if (Mask)
            {
                var prev = RenderTexture.active;
                RenderTexture.active = Mask;
                GL.Clear(false, true, Color.clear);
                RenderTexture.active = prev;
                for (int i = 0; i < layers.Count; i++)
                {
                    var l = layers[i];
                    if (!l.Map || !(l.Radius > 0f)) continue;
                    Set(l);
                    Graphics.Blit(Texture2D.blackTexture, Mask, Mat, PassMask);
                }
                Mask.GenerateMips();
            }
            Mat.SetTexture(MapId, null);
            if (!suspended) Bind(m);
            return true;
        }

        static void Set(in WetLayer l)
        {
            Mat.SetTexture(MapId, l.Map);
            Mat.SetVector(SpreadId, new Vector4(l.Radius, Mathf.Max(l.Feather, 1e-4f), l.Reach, l.Skin));
            Mat.SetVector(TintId, new Vector4(l.Tint.r, l.Tint.g, l.Tint.b, l.Saturate));
            Mat.SetVector(StainId, new Vector4(l.Stain.r, l.Stain.g, l.Stain.b, l.Amount));
        }

        void Ensure(Texture input, Texture firstMap)
        {
            int w = Math.Max(1, input.width), h = Math.Max(1, input.height);
            while (w > cap || h > cap) { w = Math.Max(1, w / 2); h = Math.Max(1, h / 2); }
            var format = IsSrgb(input) ? GraphicsFormat.R8G8B8A8_SRGB : GraphicsFormat.R8G8B8A8_UNorm;
            bool mips = HasMips(input);
            if (!Wet || Wet.width != w || Wet.height != h || Wet.graphicsFormat != format || Wet.useMipMap != mips)
            {
                Free(Wet);
                Wet = new RenderTexture(new RenderTextureDescriptor(w, h, format, 0) { useMipMap = mips, autoGenerateMips = false })
                {
                    name = "HS2Next wet composite", hideFlags = HideFlags.HideAndDontSave,
                };
                Wet.Create();
                Version++;
            }
            Wet.wrapMode = input.wrapMode;
            Wet.filterMode = input.filterMode;
            Wet.anisoLevel = input.anisoLevel;
            Wet.mipMapBias = input.mipMapBias;
            if (!wantMask) return;
            if (!Mask || Mask.width != firstMap.width || Mask.height != firstMap.height)
            {
                Free(Mask);
                Mask = new RenderTexture(new RenderTextureDescriptor(firstMap.width, firstMap.height, GraphicsFormat.R8_UNorm, 0) { useMipMap = true, autoGenerateMips = false })
                {
                    name = "HS2Next wet mask", hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Trilinear,
                };
                Mask.Create();
                Version++;
            }
            Mask.wrapMode = input.wrapMode;
        }

        void Bind(Material m)
        {
            bool fallback = ShaderMap.IsFallback(m);
            Apply(Renderer, Slot, fallback);
            foreach (var (r, s) in followers) if (r) Apply(r, s, fallback);
            bound = true;
        }

        void Apply(Renderer r, int slot, bool fallback)
        {
            r.GetPropertyBlock(block, slot);
            block.SetTexture(MainTexId, Wet);
            if (fallback) block.SetTexture(BaseMapId, Wet);
            r.SetPropertyBlock(block, slot);
        }

        void Unbind()
        {
            if (!bound) return;
            if (Renderer) Renderer.SetPropertyBlock(null, Slot);
            foreach (var (r, s) in followers) if (r) r.SetPropertyBlock(null, s);
            bound = false;
        }

        /// <summary>A part that draws the same image shows the composite too (it has no map of its own).</summary>
        public void Follow(Renderer r, int slot)
        {
            if (!r || followers.Contains((r, slot))) return;
            followers.Add((r, slot));
            if (bound) Apply(r, slot, ShaderMap.IsFallback(MaterialNow()));
        }

        /// <summary>Takes every following part back to its own image.</summary>
        public void ClearFollowers()
        {
            foreach (var (r, s) in followers) if (bound && r) r.SetPropertyBlock(null, s);
            followers.Clear();
        }

        /// <summary>The override off while on is true (a thumbnail shows the character dry); the composite stays.</summary>
        public void Suspend(bool on)
        {
            if (suspended == on) return;
            suspended = on;
            if (on) Unbind();
            else if (Wet) { var m = MaterialNow(); if (m) Bind(m); }
        }

        /// <summary>No override, no render texture.</summary>
        public void Dry()
        {
            Unbind();
            if (Wet || Mask) Version++;
            Free(Wet); Wet = null;
            Free(Mask); Mask = null;
        }

        public void Dispose()
        {
            if (disposed) return;
            Dry();
            followers.Clear();
            disposed = true;
        }

        static void Free(RenderTexture t)
        {
            if (!t) return;
            t.Release();
            if (Application.isPlaying) Object.Destroy(t); else Object.DestroyImmediate(t);
        }
    }
}
