// The appearance writes of one Studio item:
//
//   Own        every slot of every Renderer under the instance gets its own copy (MaterialCopies.Copy: the loaded
//              keywords), one per slot, also when one renderer uses a material twice or renderers share it — HS2's
//              `renderer.materials` (T1). Before the URP swap each slot records its original shader name, keywords and
//              declarations (_Color4, _Color, _EmissionColor, _EmissionStrength, _LightCancel: the gates and the
//              UI flags are evaluated on the ORIGINAL shader); the defaults of HS2's default buttons are re-read
//              (MaterialDefaults); the sea renderers' copies lose USINGWATERVOLUME; then the owned copies are swapped to
//              URP in place. Null slots stay null.
//   WriteColors  HS2's ItemComponent / ParticleComponent / PanelComponent writes on the owned copies,
//              in HS2's order and conditions:
//                a  "normal" entry (a slot isColor1/2/3 or isEmission): isColor<k> slots → _Color/_Color2/_Color3 = main;
//                   with info[k-1].useMetallic → _Metallic*/_Glossiness*; with isPattern<k> too → _Color<k>_2,
//                   _patternuv<k> (the stored Vector4), _patternuv<k>Rotator, _patternclamp<k> (1 / 0)
//                b  entry with an isAlpha slot: isAlpha slots → _alpha
//                c  normal or alpha entry: isEmission slots → _EmissionColor (rgb × 2^intensity, alpha as stored),
//                   _EmissionStrength, _LightCancel — each only when the ORIGINAL shader declares it (light cancel: no item
//                   shader does, T3)
//                d  entry with an isGlass slot: isGlass slots → _Color4 if the original shader declares it, else _Color if
//                   declared; HS2's colour write into the Range properties _Metallic4 / _Glossiness4 is reproduced only
//                   when GlassMetallicColorWrite is set (T4, R1's Q5)
//              a slot beyond the MaterialInfo list is skipped; a null renderer entry is skipped (item.nullRenderer);
//              materials of generic fallback targets (URP/Lit; R2's Q10 table) get _Color → _BaseColor (rgb, the
//              material's alpha kept), _Glossiness → _Smoothness, _EmissionColor with the keyword _EMISSION (on when the
//              colour is not black); _Metallic has the same name; alpha, patterns and colours 2 / 3 have no equivalent.
//   Patterns   texture k → _PatternMask<k+1> on every isPattern<k+1> slot of the normal entries (no isColor condition).
//   Panel      _MainTex of slot 0 of every PanelComponent renderer (a fallback target: also _BaseMap).
//   Release    destroys the owned copies (the realization's ReleaseContent).
using System;
using System.Collections.Generic;
using System.Text;
using HS2Next.Save;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HS2Next.Scenes.Runtime
{
    public sealed class ItemMaterialWriter
    {
        /// <summary>T4 (Q5): reproduce HS2's SetColor of the glass colour into _Metallic4 / _Glossiness4.</summary>
        public static bool GlassMetallicColorWrite = false;

        public const string FallbackShader = "Universal Render Pipeline/Lit";

        public sealed class Slot
        {
            public int Index;
            public Material Material;
            /// <summary>The bundle material's name (the copy keeps it).</summary>
            public string MaterialName;
            public string ShaderOriginal;
            /// <summary>The keywords of the bundle material as loaded (MaterialCopies.KeywordsOf).</summary>
            public string[] KeywordsOriginal;
            public bool HasColor4, HasColor, HasEmissionColor, HasEmissionStrength, HasLightCancel;
            /// <summary>The URP target is the generic fallback (not a fitted replacement keeping the names).</summary>
            public bool Fallback;
        }

        public sealed class RendererEntry
        {
            public Renderer Renderer;
            /// <summary>Path from the instance root ("" = the root), '/' separated.</summary>
            public string Path;
            public Slot[] Slots;
        }

        readonly List<RendererEntry> renderers = new List<RendererEntry>();
        readonly Dictionary<Renderer, RendererEntry> byRenderer = new Dictionary<Renderer, RendererEntry>();

        public IReadOnlyList<RendererEntry> Renderers => renderers;
        /// <summary>Owned material copies (non-null slots).</summary>
        public int OwnedMaterials { get; private set; }
        /// <summary>Slots whose shader fell back to the generic URP target, by original shader name.</summary>
        public readonly Dictionary<string, int> FallbackShaders = new Dictionary<string, int>(StringComparer.Ordinal);
        /// <summary>The re-read glass / emission defaults (HS2's SetGlass / SetEmission at every build).</summary>
        public MaterialDefaults Defaults { get; private set; }
        public bool HasDefaults { get; private set; }
        /// <summary>Renderer entries of the ItemComponent without a renderer (skipped).</summary>
        public int NullRendererEntries { get; private set; }
        /// <summary>Renderer entries whose slot count differs from their MaterialInfo count.</summary>
        public int MismatchedEntries { get; private set; }

        public RendererEntry EntryOf(Renderer r) => r && byRenderer.TryGetValue(r, out var e) ? e : null;
        public Slot SlotOf(Renderer r, int i)
        {
            var e = EntryOf(r);
            return e != null && i >= 0 && i < e.Slots.Length ? e.Slots[i] : null;
        }

        // ------------------------------------------------------------ ownership

        /// <summary>Copies, records, sea keyword, URP swap (see the file comment). Returns the number of owned copies.</summary>
        public int Own(GameObject instance, Studio.ItemComponent ic)
        {
            Release();
            if (!instance) return 0;
            var root = instance.transform;
            foreach (var r in instance.GetComponentsInChildren<Renderer>(true))
            {
                var shared = r.sharedMaterials;
                var e = new RendererEntry { Renderer = r, Path = PathOf(root, r.transform), Slots = new Slot[shared.Length] };
                var copies = new Material[shared.Length];
                for (int i = 0; i < shared.Length; i++)
                {
                    var src = shared[i];
                    if (!src) continue;
                    var c = MaterialCopies.Copy(src);
                    copies[i] = c;
                    var sh = src.shader;
                    e.Slots[i] = new Slot
                    {
                        Index = i, Material = c, MaterialName = src.name, ShaderOriginal = sh ? sh.name : null,
                        KeywordsOriginal = MaterialCopies.KeywordsOf(src),
                        HasColor4 = Declares(sh, "_Color4"), HasColor = Declares(sh, "_Color"),
                        HasEmissionColor = Declares(sh, "_EmissionColor"), HasEmissionStrength = Declares(sh, "_EmissionStrength"),
                        HasLightCancel = Declares(sh, "_LightCancel"),
                    };
                    OwnedMaterials++;
                }
                r.sharedMaterials = copies;
                renderers.Add(e);
                byRenderer[r] = e;
            }
            if (ic != null)
            {
                Defaults = MaterialDefaults.Read(ic, (r, i) => SlotOf(r, i)?.Material);
                HasDefaults = true;
                // reading values may run the Editor's property drawers on the copies: restore the loaded keywords
                foreach (var e in renderers)
                    foreach (var s in e.Slots)
                        if (s != null && MaterialCopies.KeepSerializedKeywords && s.KeywordsOriginal != null) s.Material.shaderKeywords = s.KeywordsOriginal;
                if (ic.renderersSea != null)
                    foreach (var r in ic.renderersSea)
                    {
                        var e = EntryOf(r);
                        if (e == null) continue;
                        foreach (var s in e.Slots) if (s != null) s.Material.DisableKeyword("USINGWATERVOLUME");
                    }
                if (ic.rendererInfos != null)
                    foreach (var ri in ic.rendererInfos)
                    {
                        if (ri == null) continue;
                        int n = MaterialDefaults.SlotCount(ri.renderer), m = ri.materials?.Count ?? 0;
                        if (n < 0) { NullRendererEntries++; continue; }
                        if (m != n) MismatchedEntries++;
                    }
            }
            Swap(instance);
            return OwnedMaterials;
        }

        void Swap(GameObject instance)
        {
            // the owned copies are swapped in place
            ShaderMap.ApplyUrp(instance, copy: false);
            foreach (var e in renderers)
                foreach (var s in e.Slots)
                {
                    if (s?.Material == null || !s.Material.shader) continue;
                    s.Fallback = s.Material.shader.name == FallbackShader && s.ShaderOriginal != FallbackShader;
                    if (s.Fallback)
                    {
                        string k = s.ShaderOriginal ?? "(null)";
                        FallbackShaders.TryGetValue(k, out int c);
                        FallbackShaders[k] = c + 1;
                    }
                }
        }

        /// <summary>Destroys the owned copies (renderers still alive keep null slots).</summary>
        public void Release()
        {
            foreach (var e in renderers)
                foreach (var s in e.Slots)
                    if (s?.Material) Object.DestroyImmediate(s.Material);
            renderers.Clear();
            byRenderer.Clear();
            FallbackShaders.Clear();
            OwnedMaterials = 0;
            NullRendererEntries = MismatchedEntries = 0;
            HasDefaults = false;
            Defaults = default;
        }

        static bool Declares(Shader sh, string p) => sh && sh.FindPropertyIndex(p) >= 0;

        static string PathOf(Transform root, Transform t)
        {
            if (t == root) return "";
            var sb = new StringBuilder(t.name);
            for (var p = t.parent; p && p != root; p = p.parent) sb.Insert(0, p.name + "/");
            return sb.ToString();
        }

        // ------------------------------------------------------------ the colour write

        static readonly string[] ColorNames = { "_Color", "_Color2", "_Color3" };
        static readonly string[] MetallicNames = { "_Metallic", "_Metallic2", "_Metallic3" };
        static readonly string[] GlossNames = { "_Glossiness", "_Glossiness2", "_Glossiness3" };
        static readonly string[] PatternColorNames = { "_Color1_2", "_Color2_2", "_Color3_2" };
        static readonly string[] PatternUvNames = { "_patternuv1", "_patternuv2", "_patternuv3" };
        static readonly string[] PatternRotNames = { "_patternuv1Rotator", "_patternuv2Rotator", "_patternuv3Rotator" };
        static readonly string[] PatternClampNames = { "_patternclamp1", "_patternclamp2", "_patternclamp3" };
        public static readonly string[] PatternMaskNames = { "_PatternMask1", "_PatternMask2", "_PatternMask3" };

        /// <summary>Every Studio property name the writes use (the props of item.materials).</summary>
        public static readonly string[] StudioProps =
        {
            "_Color", "_Color2", "_Color3", "_Metallic", "_Metallic2", "_Metallic3", "_Glossiness", "_Glossiness2", "_Glossiness3",
            "_Color1_2", "_Color2_2", "_Color3_2", "_patternuv1", "_patternuv2", "_patternuv3",
            "_patternuv1Rotator", "_patternuv2Rotator", "_patternuv3Rotator", "_patternclamp1", "_patternclamp2", "_patternclamp3",
            "_alpha", "_EmissionColor", "_EmissionStrength", "_LightCancel", "_Color4", "_Metallic4", "_Glossiness4",
        };

        /// <summary>The texture properties the writes use.</summary>
        public static readonly string[] StudioTextures = { "_PatternMask1", "_PatternMask2", "_PatternMask3", "_MainTex" };

        static bool IsColor(Studio.ItemComponent.MaterialInfo_S mi, int k) => k == 0 ? mi.isColor1 != 0 : k == 1 ? mi.isColor2 != 0 : mi.isColor3 != 0;
        static bool IsPattern(Studio.ItemComponent.MaterialInfo_S mi, int k) => k == 0 ? mi.isPattern1 != 0 : k == 1 ? mi.isPattern2 != 0 : mi.isPattern3 != 0;

        static bool Any(List<Studio.ItemComponent.MaterialInfo_S> l, Func<Studio.ItemComponent.MaterialInfo_S, bool> f)
        {
            if (l == null) return false;
            foreach (var mi in l) if (mi != null && f(mi)) return true;
            return false;
        }

        static bool IsNormal(Studio.ItemComponent.RendererInfo_S ri) =>
            Any(ri.materials, mi => mi.isColor1 != 0 || mi.isColor2 != 0 || mi.isColor3 != 0 || mi.isEmission != 0);

        /// <summary>
        /// The full write of OCIItem.UpdateColor: the ItemComponent part when it exists and (check or checkGlass), the
        /// particle start colour, the panel colour. Returns the number of property writes.
        /// </summary>
        public int WriteColors(ItemNodeState s, Studio.ItemComponent ic, Studio.ParticleComponent pc, Studio.PanelComponent panel)
        {
            int n = 0;
            if (s == null) return 0;
            if (ic != null && ic.rendererInfos != null && (ItemDefaults.Check(ic) || ItemDefaults.CheckGlass(ic)))
                foreach (var ri in ic.rendererInfos)
                {
                    if (ri == null || !ri.renderer || ri.materials == null) continue;
                    var e = EntryOf(ri.renderer);
                    if (e == null) continue;
                    bool normal = IsNormal(ri), alpha = Any(ri.materials, mi => mi.isAlpha != 0), glass = Any(ri.materials, mi => mi.isGlass != 0);
                    for (int i = 0; i < e.Slots.Length && i < ri.materials.Count; i++)
                    {
                        var slot = e.Slots[i];
                        var mi = ri.materials[i];
                        if (slot?.Material == null || mi == null) continue;
                        var m = slot.Material;
                        if (normal)
                            for (int k = 0; k < 3; k++)
                            {
                                if (!IsColor(mi, k)) continue;
                                var c = s.Colors[k];
                                SetColor(slot, ColorNames[k], UnityObjects.C(c.Main)); n++;
                                var info = ic.info != null && k < ic.info.Count ? ic.info[k] : null;
                                if (info != null && info.useMetallic != 0)
                                {
                                    m.SetFloat(MetallicNames[k], c.Metallic);
                                    m.SetFloat(GlossNames[k], c.Glossiness);
                                    if (k == 0 && slot.Fallback) m.SetFloat("_Smoothness", c.Glossiness);
                                    n += 2;
                                }
                                if (IsPattern(mi, k))
                                {
                                    m.SetColor(PatternColorNames[k], UnityObjects.C(c.Pattern.Color));
                                    m.SetVector(PatternUvNames[k], new Vector4(c.Pattern.Uv.X, c.Pattern.Uv.Y, c.Pattern.Uv.Z, c.Pattern.Uv.W));
                                    m.SetFloat(PatternRotNames[k], c.Pattern.Rot);
                                    m.SetFloat(PatternClampNames[k], c.Pattern.Clamp ? 1f : 0f);
                                    n += 4;
                                }
                            }
                        if (alpha && mi.isAlpha != 0) { m.SetFloat("_alpha", s.Alpha); n++; }
                        if ((normal || alpha) && mi.isEmission != 0)
                        {
                            if (slot.HasEmissionColor)
                            {
                                var ec = EmissionColor(s.Emission);
                                m.SetColor("_EmissionColor", ec);
                                if (slot.Fallback)
                                {
                                    if (ec.r > 0f || ec.g > 0f || ec.b > 0f) m.EnableKeyword("_EMISSION"); else m.DisableKeyword("_EMISSION");
                                }
                                n++;
                            }
                            if (slot.HasEmissionStrength) { m.SetFloat("_EmissionStrength", s.Emission.Power); n++; }
                            if (slot.HasLightCancel) { m.SetFloat("_LightCancel", s.LightCancel); n++; }
                        }
                        if (glass && mi.isGlass != 0)
                        {
                            var g = UnityObjects.C(s.Colors[3].Main);
                            if (slot.HasColor4) { m.SetColor("_Color4", g); n++; }
                            else if (slot.HasColor) { SetColor(slot, "_Color", g); n++; }
                            if (GlassMetallicColorWrite)
                            {
                                m.SetColor("_Metallic4", g);
                                m.SetColor("_Glossiness4", g);
                                n += 2;
                            }
                        }
                    }
                }
            if (pc != null && pc.particleColor1 != null)
            {
                var c0 = UnityObjects.C(s.Colors[0].Main);
                foreach (var ps in pc.particleColor1)
                {
                    if (!ps) continue;
                    var main = ps.main;
                    main.startColor = c0;
                    n++;
                }
            }
            if (panel != null && panel.renderer != null)
            {
                var c0 = s.Colors[0];
                foreach (var r in panel.renderer)
                {
                    var slot = SlotOf(r, 0);
                    if (slot?.Material == null) continue;
                    var m = slot.Material;
                    SetColor(slot, "_Color", UnityObjects.C(c0.Main));
                    m.SetVector("_patternuv1", new Vector4(c0.Pattern.Uv.X, c0.Pattern.Uv.Y, c0.Pattern.Uv.Z, c0.Pattern.Uv.W));
                    m.SetFloat("_patternuv1Rotator", c0.Pattern.Rot);
                    m.SetFloat("_patternclamp1", c0.Pattern.Clamp ? 1f : 0f);
                    n += 4;
                }
            }
            return n;
        }

        /// <summary>_Color (and the fallback target's _BaseColor rgb, alpha kept) or another colour name.</summary>
        static void SetColor(Slot slot, string name, Color c)
        {
            var m = slot.Material;
            m.SetColor(name, c);
            if (slot.Fallback && name == "_Color" && m.HasProperty("_BaseColor"))
            {
                var b = m.GetColor("_BaseColor");
                m.SetColor("_BaseColor", new Color(c.r, c.g, c.b, b.a));
            }
        }

        /// <summary>The rendered emission colour of the stored split value: rgb × 2^intensity, alpha as stored.</summary>
        public static Color EmissionColor(ItemEmission e)
        {
            float f = Mathf.Pow(2f, e.Intensity);
            return new Color(e.Color.R * f, e.Color.G * f, e.Color.B * f, e.Color.A);
        }

        // ------------------------------------------------------------ textures

        /// <summary>Pattern texture k (0..2) → _PatternMask&lt;k+1&gt; on every isPattern&lt;k+1&gt; slot of the normal entries.</summary>
        public int WritePattern(Studio.ItemComponent ic, int k, Texture t)
        {
            int n = 0;
            if (ic?.rendererInfos == null || k < 0 || k > 2) return 0;
            foreach (var ri in ic.rendererInfos)
            {
                if (ri == null || !ri.renderer || ri.materials == null || !IsNormal(ri)) continue;
                var e = EntryOf(ri.renderer);
                if (e == null) continue;
                for (int i = 0; i < e.Slots.Length && i < ri.materials.Count; i++)
                {
                    var mi = ri.materials[i];
                    var slot = e.Slots[i];
                    if (mi == null || slot?.Material == null || !IsPattern(mi, k)) continue;
                    slot.Material.SetTexture(PatternMaskNames[k], t);
                    n++;
                }
            }
            return n;
        }

        /// <summary>_MainTex of slot 0 of every panel renderer.</summary>
        public int WritePanel(Studio.PanelComponent panel, Texture t)
        {
            int n = 0;
            if (panel?.renderer == null) return 0;
            foreach (var r in panel.renderer)
            {
                var slot = SlotOf(r, 0);
                if (slot?.Material == null) continue;
                slot.Material.SetTexture("_MainTex", t);
                // a fallback target draws _BaseMap (ShaderMap.Fallback carries the texture there at the swap)
                if (slot.Fallback && slot.Material.HasProperty("_BaseMap")) slot.Material.SetTexture("_BaseMap", t);
                n++;
            }
            return n;
        }

        // ------------------------------------------------------------ dump (item.materials)

        /// <summary>HS2's MaterialInfo flags of (renderer, slot) as a census string (1 2 3 a b c E A G; "" none set), OR-ed over
        /// every renderer entry naming the renderer; null when no entry has a MaterialInfo for the slot.</summary>
        public static string FlagsOf(Studio.ItemComponent ic, Renderer r, int slot)
        {
            if (ic?.rendererInfos == null || !r) return null;
            bool found = false;
            bool c1 = false, c2 = false, c3 = false, p1 = false, p2 = false, p3 = false, em = false, al = false, gl = false;
            foreach (var ri in ic.rendererInfos)
            {
                if (ri == null || ri.renderer != r || ri.materials == null || slot >= ri.materials.Count) continue;
                var mi = ri.materials[slot];
                if (mi == null) continue;
                found = true;
                c1 |= mi.isColor1 != 0; c2 |= mi.isColor2 != 0; c3 |= mi.isColor3 != 0;
                p1 |= mi.isPattern1 != 0; p2 |= mi.isPattern2 != 0; p3 |= mi.isPattern3 != 0;
                em |= mi.isEmission != 0; al |= mi.isAlpha != 0; gl |= mi.isGlass != 0;
            }
            if (!found) return null;
            var sb = new StringBuilder();
            if (c1) sb.Append('1'); if (c2) sb.Append('2'); if (c3) sb.Append('3');
            if (p1) sb.Append('a'); if (p2) sb.Append('b'); if (p3) sb.Append('c');
            if (em) sb.Append('E'); if (al) sb.Append('A'); if (gl) sb.Append('G');
            return sb.ToString();
        }

        /// <summary>The value of a material property in reply form by the CURRENT shader's type (colour / vector → 4 doubles,
        /// float / range / int → double, texture → name or null); <paramref name="declared"/> false when the shader lacks it.</summary>
        public static object PropValue(Material m, string name, out bool declared)
        {
            declared = false;
            if (!m || !m.shader) return null;
            var sh = m.shader;
            int i = sh.FindPropertyIndex(name);
            if (i < 0) return null;
            declared = true;
            switch (sh.GetPropertyType(i))
            {
                case UnityEngine.Rendering.ShaderPropertyType.Color:
                {
                    var c = m.GetColor(name);
                    return new double[] { c.r, c.g, c.b, c.a };
                }
                case UnityEngine.Rendering.ShaderPropertyType.Vector:
                {
                    var v = m.GetVector(name);
                    return new double[] { v.x, v.y, v.z, v.w };
                }
                case UnityEngine.Rendering.ShaderPropertyType.Texture:
                {
                    var t = m.GetTexture(name);
                    return t ? t.name : null;
                }
                case UnityEngine.Rendering.ShaderPropertyType.Int: return (double)m.GetInteger(name);
                default: return (double)m.GetFloat(name);
            }
        }
    }
}
