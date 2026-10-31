// Package B's part of the map post-load pipeline: sets MapHooks.Mirrors and
// MapHooks.Terrains at start-up and does, in the mirrors step (11), what the plain ShaderMap swap of the materials step (4)
// cannot do:
//   billboards  SpeedTree billboard materials live in BillboardAssets, not in renderer slots: each distinct asset of a
//               BillboardRenderer gets a tracked copy of its material mapped through ShaderMap (in place on the bundle's
//               asset, which is unloaded with the map's bundle).
//   water       HS2Next/Compatible/Water (M11) takes generic inputs; they are filled from the original material's own properties by a
//               name table per family (tint, normal map, tiling, speed, smoothness).
//   skyboxes    ShaderMap maps "Skybox/*" to Unity's shaders of the same name; Shader.Find may return the bundle's shader of
//               that name once a bundle is loaded, so the targets are resolved once before any bundle loads and slots / the
//               scene skybox still on a bundle copy are switched to them.
//   mirrors     MapMirrors.Attach (planar reflections, M10); the handle is disposed when the map is dropped.
// Terrain templates: TerrainMap.Apply (MapHooks.Terrains, called after the materials step when the map has terrains).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    public static class MapShaderHooks
    {
        /// <summary>Counts of the last attach (MapRenderTest, reports).</summary>
        public struct Result
        {
            public int Billboards, BillboardAssets, BillboardsMapped, Water, Skyboxes, Mirrors;
            public override string ToString() =>
                $"billboards {Billboards} ({BillboardAssets} assets, {BillboardsMapped} mapped), water {Water}, skybox fixes {Skyboxes}, mirrors {Mirrors}";
        }

        public static Result Last { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Install()
        {
            ResolveBuiltins();
            MapHooks.Mirrors = Attach;
            MapHooks.Terrains = TerrainMap.Apply;
            ShaderMap.WaterInputs = WaterInputs;   // 3.7 F3: items mapped to the water get the same inputs as maps
        }

        // ------------------------------------------------------------------ attach (post-load step 11)

        public static IDisposable Attach(MapRealization map)
        {
            var r = new Result();
            var s = map?.Structure;
            if (s == null) return null;
            var mats = map.Materials;
            Billboards(s, mats, ref r);
            foreach (var rd in s.Renderers)
            {
                if (!rd) continue;
                foreach (var m in rd.sharedMaterials)
                {
                    if (!m || !m.shader) continue;
                    if (m.shader.name == WaterShader && mats != null && mats.IsOwned(m) && WaterInputs(m, mats.OriginOf(m)?.Source)) r.Water++;
                    if (FixSkybox(m)) r.Skyboxes++;
                }
            }
            if (RenderSettings.skybox && mats != null && mats.IsOwned(RenderSettings.skybox) && FixSkybox(RenderSettings.skybox)) r.Skyboxes++;
            var handle = MapMirrors.Attach(map);
            r.Mirrors = handle?.Count ?? 0;
            Last = r;
            return handle;
        }

        // ------------------------------------------------------------------ billboards

        static void Billboards(MapStructure s, MapMaterials mats, ref Result r)
        {
            var done = new HashSet<BillboardAsset>();
            foreach (var rd in s.Renderers)
            {
                if (!(rd is BillboardRenderer br) || !br.billboard) continue;
                r.Billboards++;
                var a = br.billboard;
                if (!done.Add(a)) continue;
                r.BillboardAssets++;
                var src = a.material;
                if (!src || !src.shader || mats == null || mats.IsOwned(src)) continue;
                var c = mats.Copy(src);
                if (MapOne(c)) r.BillboardsMapped++;
                a.material = c;
            }
        }

        /// <summary>Maps one material through ShaderMap.TryResolve (Fitted with the per-copy rules; no URP/Lit fallback); true
        /// when it changed shader.</summary>
        public static bool MapOne(Material m)
        {
            if (!m || !m.shader) return false;
            if (!ShaderMap.TryResolve(m.shader, out var target)) return false;
            var s = Resolve(target);
            if (!s || s == m.shader) return false;
            ShaderMap.Swap(m, s);
            return true;
        }

        // ------------------------------------------------------------------ built-in targets of the same name

        static readonly Dictionary<string, Shader> builtins = new Dictionary<string, Shader>(StringComparer.Ordinal);

        /// <summary>The targets whose name equals the original's (Unity's skybox shaders), resolved before any bundle loads.</summary>
        public static readonly string[] SameNameTargets = { "Skybox/Procedural", "Skybox/Cubemap", "Skybox/Panoramic", "Skybox/6 Sided" };

        static void ResolveBuiltins()
        {
            foreach (var n in SameNameTargets)
                if (!builtins.ContainsKey(n) || !builtins[n])
                {
                    var s = Shader.Find(n);
                    if (s) builtins[n] = s;
                }
        }

        /// <summary>Shader.Find, except for the same-name targets, which resolve to the shader found before any bundle loaded.</summary>
        public static Shader Resolve(string name)
        {
            if (builtins.TryGetValue(name, out var s) && s) return s;
            return Shader.Find(name);
        }

        /// <summary>True when <paramref name="m"/> uses a skybox shader of a bundle (same name as Unity's): switched.</summary>
        static bool FixSkybox(Material m)
        {
            if (!m.shader.name.StartsWith("Skybox/", StringComparison.Ordinal)) return false;
            if (builtins.Count == 0) ResolveBuiltins();
            if (!builtins.TryGetValue(m.shader.name, out var b) || !b || b == m.shader) return false;
            ShaderMap.Swap(m, b);
            return true;
        }

        // ------------------------------------------------------------------ water inputs (M11)

        public const string WaterShader = "HS2Next/Compatible/Water";

        // Candidate property names per input, in order (Lux Water, NatureManufacture, AQUAS, Unity's FX/Water, ASE samples, ...).
        static readonly string[] TintNames = { "_HS2WaterTint", "_ShalowColor", "_WaterColor", "_Color", "_BaseColor", "_MainColor", "_DeepColor", "_TintColor", "_horizonColor" };
        static readonly string[] DeepNames = { "_DeepColor", "_DeepWaterColor", "_DepthColor", "_Color" };   // _DepthColor: CalmWater (3.7 F3)
        static readonly string[] NormalNames = { "_BumpMap", "_WaterNormal", "_NormalMap", "_NormalTexture", "_Normal", "_WaveNormal", "_DetailNormal", "_NormalTex" };
        static readonly string[] SmoothNames = { "_Glossiness", "_WaterSmoothness", "_Smoothness", "_Gloss" };
        static readonly string[] NormalScaleNames = { "_NormalScale", "_BumpScale", "_NormalStrength" };

        /// <summary>Fills HS2Next/Compatible/Water's inputs on <paramref name="m"/> from the original material <paramref name="src"/>.</summary>
        public static bool WaterInputs(Material m, Material src)
        {
            if (!m || !src) return false;
            Color tint = new Color(0.25f, 0.42f, 0.5f, 1), deep = tint;
            bool gotTint = TryColor(src, TintNames, ref tint), gotDeep = TryColor(src, DeepNames, ref deep);
            if (gotTint && gotDeep && deep != tint) tint = Color.Lerp(tint, deep, 0.5f);
            if (!gotTint && gotDeep) tint = deep;
            m.SetColor("_HS2WaterTint", new Color(tint.r, tint.g, tint.b, 1));
            Texture nrm = null;
            string nrmName = null;
            foreach (var n in NormalNames)
                if (src.HasProperty(n) && src.GetTexture(n) is Texture t && t) { nrm = t; nrmName = n; break; }
            m.SetTexture("_HS2WaterNormal", nrm);
            float tiling = 0.1f;
            Vector4 speed = new Vector4(0.02f, 0.01f, -0.015f, 0.02f);
            if (src.shader && src.shader.name.StartsWith("Lux Water/", StringComparison.Ordinal))
            {
                // Lux: world-space mapping with _BumpTiling (per metre) and _BumpSpeed
                if (src.HasProperty("_BumpTiling")) { var bt = src.GetVector("_BumpTiling"); tiling = Mathf.Max(0.001f, bt.x); }
                if (src.HasProperty("_BumpSpeed")) { var bs = src.GetVector("_BumpSpeed"); speed = new Vector4(bs.x, bs.y, bs.z, bs.w) * 0.05f; }
            }
            else
            {
                if (src.HasProperty("_GlobalTiling")) tiling = 0.1f * Mathf.Max(0.001f, src.GetFloat("_GlobalTiling"));
                if (src.HasProperty("_WaterMainSpeed")) { var v = src.GetVector("_WaterMainSpeed"); speed = new Vector4(v.x, v.y, -v.y, v.x) * 2f; }
                if (nrmName != null) { var st = src.GetTextureScale(nrmName); if (st.x > 0 && st.x != 1) tiling *= st.x; }
            }
            m.SetFloat("_HS2WaterTiling", tiling);
            m.SetVector("_HS2WaterSpeed", speed);
            float smooth = 0.9f;
            foreach (var n in SmoothNames) if (src.HasProperty(n)) { smooth = Mathf.Clamp01(src.GetFloat(n)); break; }
            if (smooth < 0.5f) smooth = 0.9f;   // water smoothness of zero is a distance blend input in some families
            m.SetFloat("_HS2WaterSmoothness", smooth);
            float ns = 1;
            foreach (var n in NormalScaleNames) if (src.HasProperty(n)) { ns = src.GetFloat(n); break; }
            m.SetFloat("_HS2WaterNormalScale", Mathf.Clamp(Mathf.Abs(ns), 0.1f, 2f));
            return true;
        }

        static bool TryColor(Material src, string[] names, ref Color c)
        {
            foreach (var n in names)
                if (src.HasProperty(n))
                {
                    try { c = src.GetColor(n); return true; }
                    catch (Exception) { }
                }
            return false;
        }
    }
}
