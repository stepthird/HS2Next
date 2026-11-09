// Chara to Item: materials. A material is stored as its shader's name and every
// property that shader declares, read through the shader's own property list (Shader.GetPropertyCount / GetPropertyName /
// GetPropertyType), typed; its keywords, render queue, disabled passes and RenderType tag. A renderer's property blocks (the
// renderer's and the slot's) override the material: for each declared property a block has, the block's value is stored, and such a
// renderer gets a material of its own. A member's blocks are not stored. Rebuilt with new Material(Shader.Find(name)); a shader not
// found falls back to URP/Lit with _MainTex and _Color copied (bake.shaderMissing, once per name). Hide masks: _alpha_a and _alpha_b
// go to 0 without the top, _alpha_d without the bottom.
using System;
using System.Collections.Generic;
using HS2Next.Save;
using UnityEngine;
using UnityEngine.Rendering;

namespace HS2Next.Scenes.Bake
{
    public static class BakeMaterials
    {
        public const string FallbackShader = "Universal Render Pipeline/Lit";
        /// <summary>The flat colour of a skin inside: the texture's mean × _Color × this.</summary>
        public const float InsideDarken = 0.6f;

        // ------------------------------------------------------------ capture

        /// <summary>Collects the materials and textures of a capture: one entry per (material) or, with a property block, per (material,
        /// renderer, slot); one texture entry per texture object, handed to the sink as it is read.</summary>
        public sealed class Collector
        {
            readonly BakeData b;
            readonly BakeCaptureResult res;
            readonly IBakeTextureSink sink;
            readonly Dictionary<Texture, int> texIndex = new Dictionary<Texture, int>();
            readonly Dictionary<Material, int> plain = new Dictionary<Material, int>();
            readonly List<Vector3?> means = new List<Vector3?>();
            readonly MaterialPropertyBlock rb = new MaterialPropertyBlock(), sb = new MaterialPropertyBlock();

            public Collector(BakeData b, BakeCaptureResult res, IBakeTextureSink sink) { this.b = b; this.res = res; this.sink = sink; }

            /// <summary>The material indices of a renderer's slots (−1 for an empty slot).</summary>
            public int[] Of(Renderer r, bool member)
            {
                var ms = r.sharedMaterials;
                var o = new int[ms.Length];
                bool anyBlock = !member && r.HasPropertyBlock();
                MaterialPropertyBlock rendererBlock = null;
                if (anyBlock) { r.GetPropertyBlock(rb); if (!rb.isEmpty) rendererBlock = rb; }
                for (int k = 0; k < ms.Length; k++)
                {
                    var m = ms[k];
                    if (!m || !m.shader) { o[k] = -1; continue; }
                    MaterialPropertyBlock slotBlock = null;
                    if (anyBlock) { r.GetPropertyBlock(sb, k); if (!sb.isEmpty) slotBlock = sb; }
                    if (rendererBlock == null && slotBlock == null)
                    {
                        if (!plain.TryGetValue(m, out int i)) { i = Add(m, null, null); plain[m] = i; }
                        o[k] = i;
                    }
                    else
                    {
                        o[k] = Add(m, rendererBlock, slotBlock);
                        res?.Inc("mat.withBlock");
                    }
                }
                return o;
            }

            int Add(Material m, MaterialPropertyBlock rendererBlock, MaterialPropertyBlock slotBlock)
            {
                var d = Capture(m, rendererBlock, slotBlock, TexIndex);
                Inside(d);
                b.Materials.Add(d);
                return b.Materials.Count - 1;
            }

            int TexIndex(Texture t)
            {
                if (!t) return -1;
                if (texIndex.TryGetValue(t, out int i)) return i;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var data = BakeTextures.Capture(t, res, out string route, out var mean);
                if (data == null) { texIndex[t] = -1; return -1; }
                var tr = new BakeData.TexRef { Route = route, SourceFormat = data.SourceFormat, SourceKind = t is RenderTexture ? "render" : "texture2d", Source = t };
                BakeTextures.Sampler(t, tr);
                i = b.Textures.Count;
                b.Textures.Add(tr);
                means.Add(mean);
                texIndex[t] = i;
                res?.AddMs("textures", sw.Elapsed.TotalMilliseconds);
                res?.Inc("tex.bytesRaw", (int)Math.Min(int.MaxValue, data.Bytes / 1024));
                sink?.Texture(b, i, data);
                return i;
            }

            /// <summary>A skin material's inside colour: the mean of its _MainTex (linear, back to gamma) × _Color × 0.6.</summary>
            void Inside(BakeData.MatData d)
            {
                if (d.Shader == null || d.Shader.IndexOf("Skin", StringComparison.Ordinal) < 0) return;
                var c = d.Prop("_Color");
                Vector3 col = c != null && c.Type == 'c' ? new Vector3(c.V.x, c.V.y, c.V.z) : Vector3.one;
                var mt = d.Prop("_MainTex");
                Vector3 mean = Vector3.one;
                if (mt != null && mt.Tex >= 0 && mt.Tex < means.Count && means[mt.Tex] is Vector3 lin)
                    mean = new Vector3(Mathf.LinearToGammaSpace(lin.x), Mathf.LinearToGammaSpace(lin.y), Mathf.LinearToGammaSpace(lin.z));
                d.Inside = Vector3.Scale(mean, col) * InsideDarken;
            }
        }

        /// <summary>One material with the values a renderer draws it with (the slot's block over the renderer's over the material).</summary>
        public static BakeData.MatData Capture(Material m, MaterialPropertyBlock rendererBlock, MaterialPropertyBlock slotBlock, Func<Texture, int> texIndex)
        {
            var sh = m.shader;
            var d = new BakeData.MatData
            {
                Name = m.name, Shader = sh ? sh.name : "", Queue = m.renderQueue,
                Keywords = m.shaderKeywords ?? Array.Empty<string>(), RenderType = m.GetTag("RenderType", false, "") ?? "",
            };
            d.PassesOff = PassesOff(m);
            int n = sh ? sh.GetPropertyCount() : 0;
            for (int i = 0; i < n; i++)
            {
                string name = sh.GetPropertyName(i);
                var p = new BakeData.PropData { Name = name };
                switch (sh.GetPropertyType(i))
                {
                    case ShaderPropertyType.Float:
                    case ShaderPropertyType.Range:
                        p.Type = 'f';
                        p.F = Has(slotBlock, rendererBlock, name, (bk, x) => bk.HasFloat(x), out var fb) ? fb.GetFloat(name) : m.GetFloat(name);
                        break;
                    case ShaderPropertyType.Int:
                        p.Type = 'i';
                        p.I = Has(slotBlock, rendererBlock, name, (bk, x) => bk.HasInteger(x), out var ib) ? ib.GetInteger(name) : m.GetInteger(name);
                        break;
                    case ShaderPropertyType.Color:
                        p.Type = 'c';
                        p.V = Has(slotBlock, rendererBlock, name, (bk, x) => bk.HasColor(x), out var cb) ? (Vector4)cb.GetColor(name) : (Vector4)m.GetColor(name);
                        break;
                    case ShaderPropertyType.Vector:
                        p.Type = 'v';
                        p.V = Has(slotBlock, rendererBlock, name, (bk, x) => bk.HasVector(x), out var vb) ? vb.GetVector(name) : m.GetVector(name);
                        break;
                    case ShaderPropertyType.Texture:
                    {
                        p.Type = 't';
                        var t = Has(slotBlock, rendererBlock, name, (bk, x) => bk.HasTexture(x), out var tb) ? tb.GetTexture(name) : m.GetTexture(name);
                        var sc = m.GetTextureScale(name);
                        var of = m.GetTextureOffset(name);
                        p.St = new Vector4(sc.x, sc.y, of.x, of.y);
                        if (Has(slotBlock, rendererBlock, name + "_ST", (bk, x) => bk.HasVector(x), out var stb)) p.St = stb.GetVector(name + "_ST");
                        p.Tex = t ? texIndex(t) : -1;
                        break;
                    }
                    default:
                        continue;
                }
                d.Props.Add(p);
            }
            return d;
        }

        static bool Has(MaterialPropertyBlock slot, MaterialPropertyBlock renderer, string name, Func<MaterialPropertyBlock, string, bool> has, out MaterialPropertyBlock which)
        {
            if (slot != null && has(slot, name)) { which = slot; return true; }
            if (renderer != null && has(renderer, name)) { which = renderer; return true; }
            which = null;
            return false;
        }

        /// <summary>The disabled passes by their LightMode name (what SetShaderPassEnabled takes), else by pass name.</summary>
        static string[] PassesOff(Material m)
        {
            var off = new List<string>();
            var sh = m.shader;
            if (!sh) return off.ToArray();
            int n = m.passCount;
            var lightMode = new ShaderTagId("LightMode");
            for (int i = 0; i < n; i++)
            {
                string name = null;
                try { name = sh.FindPassTagValue(i, lightMode).name; } catch (Exception) { }
                if (string.IsNullOrEmpty(name)) name = m.GetPassName(i);
                if (string.IsNullOrEmpty(name) || off.Contains(name)) continue;
                if (!m.GetShaderPassEnabled(name)) off.Add(name);
            }
            return off.ToArray();
        }

        // ------------------------------------------------------------ hide masks

        static readonly string[] TopMasks = { "_alpha_a", "_alpha_b" }, BottomMasks = { "_alpha_d" };

        /// <summary>The hide masks of a bake that lacks the garment over them: _alpha_a, _alpha_b without the top, _alpha_d without the
        /// bottom, set to 0 on every baked material that declares them. Returns how many values changed.</summary>
        public static int ClearMasks(BakeData b, bool noTop, bool noBottom)
        {
            int n = 0;
            foreach (var m in b.Materials)
            {
                if (noTop) foreach (var k in TopMasks) n += Zero(m, k);
                if (noBottom) foreach (var k in BottomMasks) n += Zero(m, k);
            }
            return n;
        }

        static int Zero(BakeData.MatData m, string name)
        {
            var p = m.Prop(name);
            if (p == null) return 0;
            if (p.Type == 'f' && p.F != 0f) { p.F = 0f; return 1; }
            if (p.Type == 'i' && p.I != 0) { p.I = 0; return 1; }
            return 0;
        }

        // ------------------------------------------------------------ build

        /// <summary>A material from its stored values; <paramref name="textures"/> by index. A shader not found: URP/Lit with _MainTex and
        /// _Color copied, finding bake.shaderMissing once per name into <paramref name="missing"/>.</summary>
        public static Material Build(BakeData.MatData d, IReadOnlyList<Texture> textures, ISet<string> missing, FindingList findings)
        {
            var sh = string.IsNullOrEmpty(d.Shader) ? null : Shader.Find(d.Shader);
            if (!sh)
            {
                if (missing != null && missing.Add(d.Shader ?? ""))
                    findings?.Warning("bake.shaderMissing", d.Name, "the shader " + d.Shader + " is not available: drawn with " + FallbackShader);
                var f = new Material(Shader.Find(FallbackShader)) { name = d.Name };
                var mt = d.Prop("_MainTex");
                var c = d.Prop("_Color");
                var tex = mt != null && mt.Tex >= 0 && mt.Tex < textures.Count ? textures[mt.Tex] : null;
                if (tex) { f.SetTexture("_BaseMap", tex); f.SetTexture("_MainTex", tex); }
                if (c != null && c.Type == 'c') { f.SetColor("_BaseColor", c.V); f.SetColor("_Color", c.V); }
                return f;
            }
            var m = new Material(sh) { name = d.Name };
            foreach (var p in d.Props)
            {
                if (!m.HasProperty(p.Name)) continue;
                switch (p.Type)
                {
                    case 'f': m.SetFloat(p.Name, p.F); break;
                    case 'i': m.SetInteger(p.Name, p.I); break;
                    case 'c': MaterialWrites.WriteColorExactly(m, p.Name, p.V); break;   // a colour is held linear: the value read back is the stored one
                    case 'v': m.SetVector(p.Name, p.V); break;
                    case 't':
                        m.SetTexture(p.Name, p.Tex >= 0 && p.Tex < textures.Count ? textures[p.Tex] : null);
                        m.SetTextureScale(p.Name, new Vector2(p.St.x, p.St.y));
                        m.SetTextureOffset(p.Name, new Vector2(p.St.z, p.St.w));
                        break;
                }
            }
            m.shaderKeywords = d.Keywords ?? Array.Empty<string>();
            if (!string.IsNullOrEmpty(d.RenderType)) m.SetOverrideTag("RenderType", d.RenderType);
            if (d.PassesOff != null) foreach (var pn in d.PassesOff) m.SetShaderPassEnabled(pn, false);
            m.renderQueue = d.Queue;
            return m;
        }

        /// <summary>A flat material: HS2Next/Standard with the colour (the inside of skin) or HS2Next/Unlit/Color (a section).</summary>
        public static Material Flat(Vector3 rgb, bool unlit, string name)
        {
            var sh = Shader.Find(unlit ? "HS2Next/Unlit/Color" : "HS2Next/Standard") ?? Shader.Find(FallbackShader);
            var m = new Material(sh) { name = name };
            var c = new Color(rgb.x, rgb.y, rgb.z, 1f);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (!unlit)
            {
                if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.15f);
                if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            }
            return m;
        }
    }
}
