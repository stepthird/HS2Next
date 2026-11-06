// The writes of material edits, shared by characters, items and the map: every write of a
// float, colour, vector, keyword, texture, offset, scale, render queue, shader or the renderer's enabled flag, with the object's
// MaterialBaseline around it (null: no baseline). Object resolution, skip reasons and statistics stay with the caller.
//   URP/Lit fallback    a value re-applied onto a material ShaderMap's fallback replaced goes in as the fallback would have taken
//                       it, so an edit equals a fresh build: _Cutoff and _Smoothness stay as it set them (FallbackFixed), _Color and
//                       _MainTex (texture, offset, scale) are mirrored onto _BaseColor (alpha 1, when the fallback took it from
//                       _Color) and _BaseMap, _BaseColor is written with alpha 1 (only when the original declared it).
//   Shader swaps        through ShaderMap.Swap. A name under HS2Next/ is the shader itself: keywords it does not declare go, and it
//                       takes its own default queue unless the item names one. Any other name keeps Swap's rule.
// One and StatusOf serve items and the map, whose arrays stay documents: One writes an item, StatusOf says what it would do and
// writes nothing. A character's items go through MaterialEditorApplier, which calls the same writes.
using System;
using System.Collections.Generic;
using System.Globalization;
using HS2Next.Chara.Ext;
using HS2Next.Chara.Mosaic;
using HS2Next.Content;
using HS2Next.Save;
using UnityEngine;

namespace HS2Next
{
    /// <summary>Why an item of a material array acts or not.</summary>
    public static class MaterialStatus
    {
        public const string Applied = "applied", NotOnShader = "notOnShader", ShaderUnavailable = "shaderUnavailable", MaterialMissing = "materialMissing",
            ObjectMissing = "objectMissing", MosaicProtected = "mosaicProtected", NotImplemented = "notImplemented", FallbackFixed = "fallbackFixed",
            Driven = "driven", BadValue = "badValue", Undecodable = "undecodable";
        /// <summary>The note beside materialMissing when the renderer draws the body's material.</summary>
        public const string DrawsBody = "drawsBody";
    }

    /// <summary>What one item of an array does now: a status of <see cref="MaterialStatus"/> and a note (null, or DrawsBody).</summary>
    public struct MaterialItemState
    {
        public string Status, Note;
        public MaterialItemState(string status, string note = null) { Status = status; Note = note; }
        public bool Applied => Status == MaterialStatus.Applied;
    }

    /// <summary>What <see cref="MaterialWrites.One"/> needs of a target that is not a character: how names reach materials and
    /// renderers, and how a stored image becomes a texture. An item, the map and the status read of a character each give one.</summary>
    public abstract class MaterialWriteTarget
    {
        /// <summary>The target's baseline (null in a status read).</summary>
        public MaterialBaseline Baseline;
        public ContentCatalog Catalog;
        /// <summary>Decode Bump and Normal properties linear (CharaExtOptions.MaterialEditorLinearNormals; default off).</summary>
        public bool LinearNormals;
        /// <summary>Decoded images whose wrap, filter and aniso were taken from the texture they replaced.</summary>
        public readonly HashSet<Texture> TextureSettingsDone = new HashSet<Texture>();

        /// <summary>Fills <paramref name="into"/> with the materials of that formatted name on the target's renderers, those of mosaic
        /// members left out; <paramref name="heldByMember"/> tells that a member holds the name. <paramref name="forWrite"/>: a write
        /// follows, so a slot the target does not own gets its copy first; false: nothing may change. <paramref name="note"/>: why
        /// the name reaches nothing (null or <see cref="MaterialStatus.DrawsBody"/>).</summary>
        public abstract void Select(string material, List<Material> into, bool forWrite, out bool heldByMember, out string note);
        /// <summary>Fills <paramref name="into"/> with the renderers the name or path of a `renderer` item reaches.</summary>
        public abstract void SelectRenderers(string renderer, List<Renderer> into);
        /// <summary>The decoded image of a `res` name; null when it has no bytes or does not decode.</summary>
        public abstract Texture2D Image(string res, bool linear);
        /// <summary>The image in a status read: no decode. True when it has bytes and a readable header of an allowed size.</summary>
        public abstract bool ImageReadable(string res);
        /// <summary>A map script or mirror rewrites this member of a texture property of <paramref name="m"/>: the offset or scale of a
        /// scrolled material, the image of a reflection. <paramref name="member"/>: Texture, Offset or Scale. Such a member is not
        /// written and the item says so; the other members of the item are.</summary>
        public virtual bool Driven(Material m, string property, MaterialMember member) => false;
    }

    public static class MaterialWrites
    {
        // ---------------------------------------------------------------- the URP/Lit fallback

        /// <summary>A property the fallback set itself: _Cutoff (0.5 for a cutout original; an original without it had no such
        /// property to edit) and _Smoothness (0.2).</summary>
        public static bool FallbackFixed(Material m, string prop) => (prop == "_Cutoff" || prop == "_Smoothness") && ShaderMap.IsFallback(m);

        public static bool MirrorsBaseMap(Material m, string prop) => prop == "_MainTex" && ShaderMap.IsFallback(m) && m.HasProperty("_BaseMap");

        static void SetColorAsFallback(Material m, string prop, Color c)
        {
            if (!ShaderMap.IsFallback(m)) { m.SetColor(prop, c); return; }
            if (prop == "_Color")
            {
                m.SetColor("_Color", c);
                var r = m.GetColor("_Color");
                m.SetColor("_Color", r);   // the fallback wrote _Color back from the value it read
                if (ShaderMap.FallbackColorFromColor(m)) m.SetColor("_BaseColor", new Color(r.r, r.g, r.b, 1));
                return;
            }
            if (prop == "_BaseColor")
            {
                // the fallback took _BaseColor from the original's own _BaseColor (alpha 1); without one, from _Color
                if (!ShaderMap.FallbackColorFromColor(m)) m.SetColor("_BaseColor", new Color(c.r, c.g, c.b, 1));
                return;
            }
            m.SetColor(prop, c);
        }

        // ---------------------------------------------------------------- one member each

        public static void SetFloat(Material m, string prop, float value, MaterialBaseline b)
        {
            b?.Before(m, MaterialMember.Float, prop);
            m.SetFloat(prop, value);
            b?.After(m, MaterialMember.Float, prop);
        }

        public static void SetColor(Material m, string prop, Color value, MaterialBaseline b)
        {
            b?.Before(m, MaterialMember.Color, prop);
            SetColorAsFallback(m, prop, value);
            b?.After(m, MaterialMember.Color, prop);
        }

        public static void SetVector(Material m, string prop, Vector4 value, MaterialBaseline b)
        {
            b?.Before(m, MaterialMember.Vector, prop);
            m.SetVector(prop, value);
            b?.After(m, MaterialMember.Vector, prop);
        }

        public static void SetKeyword(Material m, string keyword, bool on, MaterialBaseline b)
        {
            b?.Before(m, MaterialMember.Keyword, keyword);
            if (on) m.EnableKeyword(keyword); else m.DisableKeyword(keyword);
            b?.After(m, MaterialMember.Keyword, keyword);
        }

        /// <summary>The image of a texture item on one material. The first material an image lands on gives it the wrap, filter and
        /// aniso of the texture it replaces.</summary>
        public static void SetTexture(Material m, string prop, Texture2D tex, HashSet<Texture> settingsDone, MaterialBaseline b)
        {
            if (settingsDone.Add(tex))
            {
                var old = m.GetTexture(prop);
                tex.anisoLevel = old ? old.anisoLevel : 1;
                tex.filterMode = old ? old.filterMode : FilterMode.Bilinear;
                tex.wrapMode = old ? old.wrapMode : TextureWrapMode.Repeat;
            }
            b?.Before(m, MaterialMember.Texture, prop);
            m.SetTexture(prop, tex);
            if (MirrorsBaseMap(m, prop)) m.SetTexture("_BaseMap", tex);
            b?.After(m, MaterialMember.Texture, prop);
        }

        public static void SetTextureOffset(Material m, string prop, Vector2 value, MaterialBaseline b)
        {
            b?.Before(m, MaterialMember.Offset, prop);
            m.SetTextureOffset(prop, value);
            if (MirrorsBaseMap(m, prop)) m.SetTextureOffset("_BaseMap", value);
            b?.After(m, MaterialMember.Offset, prop);
        }

        public static void SetTextureScale(Material m, string prop, Vector2 value, MaterialBaseline b)
        {
            b?.Before(m, MaterialMember.Scale, prop);
            m.SetTextureScale(prop, value);
            if (MirrorsBaseMap(m, prop)) m.SetTextureScale("_BaseMap", value);
            b?.After(m, MaterialMember.Scale, prop);
        }

        public static void SetQueue(Material m, int queue, MaterialBaseline b)
        {
            b?.Before(m, MaterialMember.Queue, "");
            m.renderQueue = queue;
            b?.After(m, MaterialMember.Queue, "");
            FollowQueue(m);
        }

        /// <summary>
        /// A queue written on a material past the URP mapping: what ShaderMap.Swap leaves at that queue. One meaning of a queue on every
        /// target; on a character a crossing reloads the part (MeStructure.OfMapped), so this acts on items, the map and names under HS2Next/.
        /// The fog stamp: a mapped material takes it from the shader the mapping replaced. A name under HS2Next/ has none, so its fog does
        /// not follow. The shadow switch: above 2500 a shader that declares it has it on; at or under 2500 it is off, unless the shader the
        /// mapping replaced declares it too: then the value is the bundle's. Only under HS2Next/: a build's items come before the mapping, on
        /// the bundle's shader, where Swap decides, and URP/Lit's switch is a shader feature a player may not hold. Routed and fallback
        /// alpha clip do not follow, on purpose: at the mapping they are an inference about the original from the original's queue.
        /// </summary>
        public static void FollowQueue(Material m, bool fog = true, bool shadows = true)
        {
            var sh = m.shader;
            if (!sh) return;
            if (fog && ShaderMap.MappedFrom(m) is Shader original) HS2FogFeature.StampModes(m, original, m.renderQueue);
            if (!shadows || !MaterialShaders.IsDirectName(sh.name) || !sh.keywordSpace.FindKeyword(ShaderMap.ReceiveShadowsOff).isValid) return;
            if (m.renderQueue > 2500) { m.EnableKeyword(ShaderMap.ReceiveShadowsOff); return; }
            var from = ShaderMap.MappedFrom(m);
            if (!from || !from.keywordSpace.FindKeyword(ShaderMap.ReceiveShadowsOff).isValid) m.DisableKeyword(ShaderMap.ReceiveShadowsOff);
        }

        public static void SetEnabled(Renderer r, bool on, MaterialBaseline b)
        {
            b?.Before(r);
            r.enabled = on;
            b?.After(r);
        }

        /// <summary>
        /// A shader item's shader on one material (<paramref name="name"/> as stored, <paramref name="sh"/> what it resolved to).
        /// <paramref name="keep"/>: the keywords of the plugin's own copy of an old name, else null. A name under HS2Next/ drops the
        /// keywords the shader does not declare and takes its default queue; the item's queue, when it has one, is written after this.
        /// </summary>
        public static void SetShader(Material m, Shader sh, string name, string[] keep)
        {
            // (without a render pipeline a name resolves to the bundle's copy: a material the mapping took from it has it already)
            if (m.shader != sh && ShaderMap.MappedFrom(m) != sh)
            {
                ShaderMap.Swap(m, sh);
                if (MaterialShaders.IsDirectName(name)) TakeShaderDefaults(m, sh);
            }
            if (keep != null) MaterialEditorApplier.KeepKeywords(m, keep);
        }

        static void TakeShaderDefaults(Material m, Shader sh)
        {
            foreach (var k in m.shaderKeywords)
                if (k != ShaderMap.ReceiveShadowsOff && !sh.keywordSpace.FindKeyword(k).isValid) m.DisableKeyword(k);
            m.renderQueue = sh.renderQueue;
            // as ShaderMap.Swap: the transparent queues receive no realtime shadows on the shaders that declare the switch
            if (sh.keywordSpace.FindKeyword(ShaderMap.ReceiveShadowsOff).isValid)
            {
                if (sh.renderQueue > 2500) m.EnableKeyword(ShaderMap.ReceiveShadowsOff);
                else m.DisableKeyword(ShaderMap.ReceiveShadowsOff);
            }
        }

        /// <summary>True when a new item on this property of a character's material outlives a rebuild: a build applies items before
        /// the URP mapping, so it asks the shader the mapping replaced, and a property only the replacement declares is out of its reach.
        /// A shader item naming the replacement itself puts it within reach. For a new edit: stored items are judged by the pass.</summary>
        public static bool Declared(Material m, string prop)
        {
            var from = ShaderMap.MappedFrom(m);
            return from ? from.FindPropertyIndex(prop) >= 0 : m.HasProperty(prop);
        }

        /// <summary>True when the material's shader, or the replacement the URP mapping will give it, declares the keyword.</summary>
        public static bool Declares(Material m, string keyword)
        {
            var sh = m.shader;
            if (!sh) return false;
            if (sh.keywordSpace.FindKeyword(keyword).isValid) return true;
            if (!ShaderMap.TryResolve(sh, out var fitted)) return false;
            var f = Shader.Find(fitted);
            return f && f.keywordSpace.FindKeyword(keyword).isValid;
        }

        // ---------------------------------------------------------------- colours going back

        /// <summary>The value a colour goes back to. On a fallback whose entry a build recorded (before the mapping) it takes the
        /// fallback's one write; otherwise the value on the Get side must come back as it was read.</summary>
        internal static void RestoreColor(Material m, string prop, Color c, bool fromBuild)
        {
            if (ShaderMap.IsFallback(m) && (prop == "_Color" || prop == "_BaseColor"))
            {
                var opaque = new Color(c.r, c.g, c.b, 1);
                if (prop == "_Color")
                {
                    if (fromBuild) m.SetColor("_Color", c); else WriteColorExactly(m, "_Color", c);
                    if (!ShaderMap.FallbackColorFromColor(m)) return;
                }
                else if (ShaderMap.FallbackColorFromColor(m)) return;
                if (fromBuild) m.SetColor("_BaseColor", opaque); else WriteColorExactly(m, "_BaseColor", opaque);
                return;
            }
            WriteColorExactly(m, prop, c);
        }

        /// <summary>
        /// Writes a colour so that GetColor returns exactly <paramref name="want"/>. A colour is stored linear, so writing back a value
        /// that was read can move it by an ulp; each channel then tries the floats next to it until the read agrees (the original
        /// stored value came from one of them).
        /// </summary>
        public static void WriteColorExactly(Material m, string prop, Color want)
        {
            m.SetColor(prop, want);
            var got = m.GetColor(prop);
            if (Same(got, want)) return;
            var c = want;
            for (int i = 0; i < 4; i++)
            {
                if (Bits(got[i]) == Bits(want[i])) continue;
                int bits = Bits(want[i]);
                bool found = false;
                for (int step = 1; step <= 4 && !found; step++)
                    foreach (int sign in Signs)
                    {
                        c[i] = BitConverter.Int32BitsToSingle(bits + sign * step);
                        m.SetColor(prop, c);
                        got = m.GetColor(prop);
                        if (Bits(got[i]) == bits) { found = true; break; }
                    }
                if (!found) c[i] = want[i];
            }
            m.SetColor(prop, c);
        }

        static readonly int[] Signs = { 1, -1 };
        static int Bits(float f) => BitConverter.SingleToInt32Bits(f);
        static bool Same(Color a, Color b) => Bits(a.r) == Bits(b.r) && Bits(a.g) == Bits(b.g) && Bits(a.b) == Bits(b.b) && Bits(a.a) == Bits(b.a);

        // ---------------------------------------------------------------- one item, on items and the map

        /// <summary>Applies one stored item to the target; null when it applied, else a status of <see cref="MaterialStatus"/>. Items in
        /// rank order, then the baseline's sweep (ItemMaterialEdits, MapMaterialEdits).</summary>
        public static string One(MaterialOverride item, MaterialWriteTarget t)
        {
            var s = Run(item, t, true);
            return s.Applied ? null : s.Status;
        }

        /// <summary>What the item does now: no write, no copy, no decode.</summary>
        public static MaterialItemState StatusOf(MaterialOverride item, MaterialWriteTarget t) => Run(item, t, false);

        static MaterialItemState Run(MaterialOverride item, MaterialWriteTarget t, bool write)
        {
            switch (item)
            {
                case MeRenderer r: return RunRenderer(r, t, write);
                case MeShader s:
                    if (s.Shader != null && MaterialGuard.Shader(s.Shader)) return new MaterialItemState(MaterialStatus.MosaicProtected);
                    break;
                case MeFloat _: case MeColor _: case MeVector _: case MeKeyword _: case MeTexture _:
                    break;
                default:
                    return new MaterialItemState(MaterialStatus.NotImplemented);
            }
            var mi = (MeMaterialItem)item;
            var mats = new List<Material>();
            t.Select(mi.Material, mats, write, out bool held, out string note);
            if (mats.Count == 0)
                return new MaterialItemState(held ? MaterialStatus.MosaicProtected : MaterialStatus.MaterialMissing, held ? null : note);
            for (int i = mats.Count - 1; i >= 0; i--)
                if (MaterialGuard.Material(mats[i])) mats.RemoveAt(i);
            if (mats.Count == 0) return new MaterialItemState(MaterialStatus.MosaicProtected);
            if (item is MeShader se) return RunShader(se, mats, t, write);
            var mp = (MeMaterialProperty)item;
            string prop = "_" + mp.Property;
            if (item is MeKeyword kw)
            {
                bool any = false;
                foreach (var m in mats)
                    if (Declares(m, prop)) { any = true; if (write) SetKeyword(m, prop, kw.Value, t.Baseline); }
                return new MaterialItemState(any ? MaterialStatus.Applied : MaterialStatus.NotOnShader);
            }
            var with = new List<Material>();
            bool declared = false;
            foreach (var m in mats)
            {
                if (!m.HasProperty(prop)) continue;
                declared = true;
                if (!FallbackFixed(m, prop)) with.Add(m);
            }
            if (with.Count == 0) return new MaterialItemState(declared ? MaterialStatus.FallbackFixed : MaterialStatus.NotOnShader);
            switch (item)
            {
                case MeFloat f:
                {
                    float v = f.Value;
                    if (f.ValueText != null && !float.TryParse(f.ValueText, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                        return new MaterialItemState(MaterialStatus.BadValue);
                    if (write) foreach (var m in with) SetFloat(m, prop, v, t.Baseline);
                    break;
                }
                case MeColor c:
                    if (write) foreach (var m in with) SetColor(m, prop, new Color(c.Value.R, c.Value.G, c.Value.B, c.Value.A), t.Baseline);
                    break;
                case MeVector v:
                    if (write) foreach (var m in with) SetVector(m, prop, new Vector4(v.Value.X, v.Value.Y, v.Value.Z, v.Value.W), t.Baseline);
                    break;
                case MeTexture x: return RunTexture(x, prop, with, t, write);
            }
            return new MaterialItemState(MaterialStatus.Applied);
        }

        static MaterialItemState RunRenderer(MeRenderer r, MaterialWriteTarget t, bool write)
        {
            if (r.Property != "Enabled") return new MaterialItemState(MaterialStatus.NotImplemented);
            var rends = new List<Renderer>();
            t.SelectRenderers(r.Renderer, rends);
            if (rends.Count == 0) return new MaterialItemState(MaterialStatus.ObjectMissing);
            foreach (var x in rends) if (MaterialGuard.Renderer(x)) return new MaterialItemState(MaterialStatus.MosaicProtected);
            int v = r.Value;
            if (r.ValueText != null && !int.TryParse(r.ValueText, NumberStyles.Integer, CultureInfo.InvariantCulture, out v))
                return new MaterialItemState(MaterialStatus.BadValue);
            if (write) foreach (var x in rends) SetEnabled(x, v != 0, t.Baseline);
            return new MaterialItemState(MaterialStatus.Applied);
        }

        static MaterialItemState RunShader(MeShader se, List<Material> mats, MaterialWriteTarget t, bool write)
        {
            if (se.Shader != null)
            {
                var sh = MaterialEditorApplier.ResolveShader(se.Shader, t.Catalog, out _);
                if (!sh) return new MaterialItemState(MaterialStatus.ShaderUnavailable);
                if (write)
                {
                    var keep = MaterialEditorApplier.PluginCopyKeywords(se.Shader, t.Catalog);
                    foreach (var m in mats) SetShader(m, sh, se.Shader, keep);
                }
            }
            if (write && se.RenderQueue.HasValue) foreach (var m in mats) SetQueue(m, se.RenderQueue.Value, t.Baseline);
            return new MaterialItemState(MaterialStatus.Applied);
        }

        // Each member on the materials that do not rewrite it. An item with a member a script or mirror rewrites says driven; the members
        // nothing rewrites are written all the same.
        static MaterialItemState RunTexture(MeTexture x, string prop, List<Material> mats, MaterialWriteTarget t, bool write)
        {
            if (x.Image == null && !x.Offset.HasValue && !x.Scale.HasValue) return new MaterialItemState(MaterialStatus.BadValue);
            bool driven = false;
            if (x.Image != null)
            {
                var free = Free(mats, prop, MaterialMember.Texture, t, ref driven);
                if (free.Count > 0 && !write && !t.ImageReadable(x.Image)) return new MaterialItemState(MaterialStatus.Undecodable);
                if (free.Count > 0 && write)
                {
                    bool linear = t.LinearNormals && (prop.Contains("Bump") || prop.Contains("Normal"));
                    var tex = t.Image(x.Image, linear);
                    if (!tex) return new MaterialItemState(MaterialStatus.Undecodable);
                    foreach (var m in free) SetTexture(m, prop, tex, t.TextureSettingsDone, t.Baseline);
                }
            }
            if (x.Offset.HasValue)
            {
                var free = Free(mats, prop, MaterialMember.Offset, t, ref driven);
                if (write) foreach (var m in free) SetTextureOffset(m, prop, new Vector2(x.Offset.Value.X, x.Offset.Value.Y), t.Baseline);
            }
            if (x.Scale.HasValue)
            {
                var free = Free(mats, prop, MaterialMember.Scale, t, ref driven);
                if (write) foreach (var m in free) SetTextureScale(m, prop, new Vector2(x.Scale.Value.X, x.Scale.Value.Y), t.Baseline);
            }
            return new MaterialItemState(driven ? MaterialStatus.Driven : MaterialStatus.Applied);
        }

        static List<Material> Free(List<Material> mats, string prop, MaterialMember member, MaterialWriteTarget t, ref bool driven)
        {
            var l = new List<Material>(mats.Count);
            foreach (var m in mats)
                if (t.Driven(m, prop, member)) driven = true; else l.Add(m);
            return l;
        }
    }
}
