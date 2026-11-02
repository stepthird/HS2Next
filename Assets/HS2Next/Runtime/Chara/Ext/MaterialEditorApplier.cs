// MaterialEditor, character side, applied after the
// base assembly and the skin/clothes overlays, in the order of MaterialEditorData.AllItems(): material copy,
// rename, shader + render queue, renderer properties, float, keyword, colour, vector, texture (+ offset, scale),
// cubemap, projector.
// Target object by ObjectType + Slot: Clothing = clothes kind 0..7, Accessory = absolute slot (>= 20 = MoreAccessories),
// Hair = kind 0..3, Character = the body-part renderers found depth-first from the root without entering the clothes
// objects (o_body_cf, o_head, o_eyebase_L/R, o_tooth, ...). Clothing/Accessory items need CoordinateIndex 0. Every
// renderer of the object, inactive ones included; property edits select every material whose name ("(Instance)" and
// " Instance" removed) equals MaterialName, and are written only where the material's shader has the property.
// Materials are made per-character first (bundle materials are shared between characters): a material the build did not
// make (CharaContext.OwnedMaterials, by identity; a bundle material's name may well end in "(Instance)") is replaced by a
// copy on that renderer (MaterialCopies.Copy: the loaded keywords) and the copy is added to the owned set.
// Shaders (through ShaderMap): the copy a requested name stands for comes from MaterialEditor's registry
// (MaterialEditorShaders: the copy a zipmod manifest declares, loaded through the catalog; none for the plugin's own game
// shaders and for undeclared names), never from the shaders loaded in the process, so the result does not depend on what
// was built before. The replacement is ShaderMap's for that copy (the copy rules decide between several replacements of
// one name; without a copy, the name's main replacement). It is applied as that declared copy when no render pipeline is
// active (ShaderMap.ApplyUrp then maps it), else as the replacement itself; the render queue is kept and the stored
// RenderQueue written after. Names without a replacement for their copy (Sideloader shaders such as Hanmen/Next-Gen Body)
// and names that neither the plugin nor a loaded manifest declares keep the current shader: the item is skipped and
// recorded as MissingContent "me.shader". A swap to one of the plugin's own copies (PluginCopies) keeps on the material only
// the keywords that copy declares. The registry's per-shader default values (ME's XML) are not known here.
// Textures: TextureDictionary entries decoded once per character and id through ExtTextures (identical bytes
// shared, mipmapped, sRGB like ME, so normal maps are used as stored; CharaExtOptions.MaterialEditorLinearNormals
// decodes Bump/Normal properties linear instead); wrap/filter/aniso copied from the first texture replaced. A
// Character MainTex edit replaces the KSOX composite on the draw material; TexCreator.Rebuild keeps the binding, so it
// survives later skin rebuilds (body and face).
// Edits after the build (Phase 4.6): the build applies the items before ShaderMap.ApplyUrp, whose URP/Lit fallback then
// takes _MainTex, _Color / _BaseColor from the original and forces _Smoothness 0.2 and, for a cutout original, _Cutoff 0.5.
// A value item re-applied later onto a material the fallback already replaced (ShaderMap.IsFallback) goes in as the
// fallback would have taken it, so an edit equals a fresh build: _Cutoff and _Smoothness stay as the fallback set them,
// _Color and _MainTex (texture, offset, scale) are mirrored onto _BaseColor (α 1, when the fallback took it from _Color)
// and _BaseMap, _BaseColor is written with α 1 (only when the original declared it).
// Mosaic-set members (o_mnpa, o_mnpb, cm_o_dan00, ... and every renderer with HS2's mosaic material; Runtime/Chara/Mosaic):
// items that target them are kept in the documents but not applied (skip reason "mosaicProtected"; the mosaic read model
// lists them as me.mosaicProtected); an item on a material shared with other renderers applies to those only.
// Not implemented (no card on this install uses them): projector edits, cubemaps, texture animation (the first
// texture is shown), ME's tongue "CorrectTongue" re-write and the ChangeAlphaMask copy to ME material copies.
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HS2Next.Chara.Mosaic;
using HS2Next.Data.Ext;
using UnityEngine;
using UnityEngine.Rendering;
using OT = HS2Next.Data.Ext.MaterialEditorData.ObjectType;

namespace HS2Next.Chara.Ext
{
    public static class MaterialEditorApplier
    {
        /// <summary>Renderer names that make up the Character object.</summary>
        public static readonly HashSet<string> BodyParts = new HashSet<string>
        {
            "o_eyebase_L", "o_eyebase_R", "o_eyelashes", "o_eyeshadow", "o_head", "o_namida", "o_tang", "o_tooth",
            "o_body_cf", "o_body_cm", "o_mnpa", "o_mnpb", "cm_o_dan00", "o_silhouette_cf",
        };
        static readonly HashSet<string> ClothesRoots = new HashSet<string>(ClothesOverlayData.MainClothesNames);

        public static string Fmt(string n) => n == null ? "" : n.Replace("(Instance)", "").Replace(" Instance", "").Trim();

        sealed class State
        {
            public CharaContext Ctx;
            public MaterialEditorData Data;
            public CharaExtOptions O;
            public ExtStats St;
            public readonly Dictionary<(OT, int), List<Renderer>> Objects = new Dictionary<(OT, int), List<Renderer>>();
            public readonly Dictionary<Renderer, Material[]> Mats = new Dictionary<Renderer, Material[]>();
            public readonly HashSet<(OT, int)> Reported = new HashSet<(OT, int)>();
            public Cache C;
            public (OT, int) Current;
        }

        /// <summary>What a character keeps between applications (CharaContext.MaterialEditorCache): decoded textures (shared
        /// by identical ids), the texture settings done, the shaders reported missing, and per object the textures it uses
        /// (MeTexturesDecoded / MeTextureBytes = the distinct textures in use).</summary>
        public sealed class Cache
        {
            internal readonly Dictionary<(int, bool), Texture2D> Tex = new Dictionary<(int, bool), Texture2D>();
            internal readonly Dictionary<(int, bool), byte[]> Source = new Dictionary<(int, bool), byte[]>();
            internal readonly HashSet<(int, bool)> TexFailed = new HashSet<(int, bool)>();
            internal readonly HashSet<Texture> TexSettingsDone = new HashSet<Texture>();
            internal readonly HashSet<string> MissingShaders = new HashSet<string>();
            internal readonly Dictionary<(OT, int), HashSet<(int, bool)>> Used = new Dictionary<(OT, int), HashSet<(int, bool)>>();
            internal CharaExtResources Res;
            internal readonly Dictionary<(int, bool), string> Keys = new Dictionary<(int, bool), string>();
        }

        static Cache CacheOf(CharaContext ctx) => ctx.MaterialEditorCache ?? (ctx.MaterialEditorCache = new Cache());

        /// <summary>Copy, rename and renderer items (applied once per object; the value items may be applied again).</summary>
        public static bool IsStructural(MaterialEditorData.Item i) =>
            i is MaterialEditorData.MaterialCopy || i is MaterialEditorData.NameProperty || i is MaterialEditorData.RendererProperty;

        public static void Apply(CharaContext ctx, MaterialEditorData me, CharaExtOptions o) => Apply(ctx, me, o, null, true, true);

        /// <summary>The items of the given objects only (type, slot; Character slot 0): after an edit wrote those objects'
        /// materials or replaced them. <paramref name="structural"/>: copy, rename and renderer
        /// items; <paramref name="values"/>: shader, render queue and property items.</summary>
        public static void ApplyObjects(CharaContext ctx, MaterialEditorData me, CharaExtOptions o, ICollection<(OT, int)> objects, bool structural = true, bool values = true) =>
            Apply(ctx, me, o, objects, structural, values);

        /// <summary>MeTexturesDecoded / MeTextureBytes from the textures the objects use now.</summary>
        public static void UpdateTextureStats(CharaContext ctx)
        {
            var c = CacheOf(ctx);
            var all = new HashSet<(int, bool)>();
            foreach (var u in c.Used.Values) all.UnionWith(u);
            ctx.Stats.MeTexturesDecoded = all.Count;
            long bytes = 0;
            foreach (var k in all) if (c.Source.TryGetValue(k, out var b) && b != null) bytes += b.Length;
            ctx.Stats.MeTextureBytes = bytes;
            // decoded textures no object uses any more are released (a fresh build would not have decoded them), and with
            // the last one the resource holder goes (a fresh build without textures has none)
            foreach (var k in c.Tex.Keys.Where(k => !all.Contains(k)).ToList())
            {
                if (c.Keys.TryGetValue(k, out var key))
                {
                    if (c.Res) c.Res.TextureKeys.Remove(key);
                    ExtTextures.Release(key);
                    c.Keys.Remove(k);
                }
                c.Tex.Remove(k); c.Source.Remove(k); c.TexSettingsDone.RemoveWhere(t => !t);
            }
            if (c.Res && c.Res.TextureKeys.Count == 0 && all.Count == 0) { Object.DestroyImmediate(c.Res); c.Res = null; }
        }

        /// <summary>An object has no items any more: the textures it used are no longer counted.</summary>
        public static void ForgetObject(CharaContext ctx, (OT, int) key) => CacheOf(ctx).Used.Remove(key);

        /// <summary>New plugin data (an edit regenerated it): decoded textures whose bytes changed are forgotten.</summary>
        public static void KeepTextures(CharaContext ctx, MaterialEditorData me)
        {
            var c = CacheOf(ctx);
            foreach (var k in c.Source.Keys.ToList())
            {
                byte[] now = null;
                me?.Textures.TryGetValue(k.Item1, out now);
                if (now != null && now.SequenceEqual(c.Source[k])) continue;
                if (c.Keys.TryGetValue(k, out var key))
                {
                    if (c.Res) c.Res.TextureKeys.Remove(key);
                    ExtTextures.Release(key);
                    c.Keys.Remove(k);
                }
                c.Tex.Remove(k); c.Source.Remove(k);
                foreach (var u in c.Used.Values) u.Remove(k);
            }
            c.TexFailed.Clear();
        }

        static void Apply(CharaContext ctx, MaterialEditorData me, CharaExtOptions o, ICollection<(OT, int)> only, bool structural, bool values)
        {
            if (me == null || !me.HasEdits) return;
            var s = new State { Ctx = ctx, Data = me, O = o, St = ctx.Stats, C = CacheOf(ctx) };
            var st = ctx.Stats;
            int applied0 = st.MeApplied;
            if (values && only != null) foreach (var k in only) s.C.Used.Remove(k);
            foreach (var item in me.AllItems())
            {
                var key = (item.ObjectType, item.ObjectType == OT.Character ? 0 : item.Slot);
                if (only != null && !only.Contains(key)) continue;
                if (IsStructural(item) ? !structural : !values) continue;
                s.Current = key;
                st.MeItems++;
                string why;
                try { why = One(s, item); }
                catch (System.Exception e) { why = "error " + e.GetType().Name; st.Errors.Add($"materialEditor: {Describe(item)}: {e.GetType().Name}: {e.Message}"); }
                if (why == null) st.MeApplied++;
                else ExtStats.Inc(st.MeSkipped, why);
            }
            var byType = me.AllItems().GroupBy(i => i.ObjectType).Select(g => $"{g.Key} {g.Count()}");
            ctx.Log.Add($"materialEditor: {me.ItemCount} items ({string.Join(", ", byType)}), applied {st.MeApplied - applied0}, textures {s.C.Tex.Count}/{me.Textures.Count}" +
                        (s.C.MissingShaders.Count > 0 ? "; shaders kept (no fitted replacement): " + string.Join(", ", s.C.MissingShaders) : ""));
        }

        static string Describe(MaterialEditorData.Item i)
        {
            string what = i is MaterialEditorData.MaterialPropertyItem p ? $"{p.MaterialName}._{p.Property}"
                : i is MaterialEditorData.MaterialItem m ? m.MaterialName : i is MaterialEditorData.RendererProperty r ? r.RendererName : "";
            return $"{i.GetType().Name} {i.ObjectType} {i.Slot} {what}";
        }

        // Returns null when the item was applied, else the reason it was skipped.
        static string One(State s, MaterialEditorData.Item item)
        {
            if (item.ObjectType == OT.Unknown) return "unknown object type";
            if ((item.ObjectType == OT.Clothing || item.ObjectType == OT.Accessory) && item.CoordinateIndex != 0) return "coordinate index not 0";
            if (item is MaterialEditorData.ProjectorProperty) return "projector edits not implemented";
            if (item is MaterialEditorData.CubemapProperty) return "cubemaps not implemented";
            var rends = Renderers(s, item.ObjectType, item.Slot);
            if (rends == null) return "object missing";
            // members of the mosaic set: the item stays in the documents but is not applied to them (an item that reaches
            // only members is skipped as a whole; finding me.mosaicProtected in the mosaic read model)
            if (MosaicReason(item, rends) is string mosaic) return mosaic;
            if (rends.Any(MosaicSet.IsMember)) rends = rends.Where(r => !MosaicSet.IsMember(r)).ToList();
            switch (item)
            {
                case MaterialEditorData.RendererProperty rp: return RendererProp(rends, rp);
                case MaterialEditorData.NameProperty np:
                {
                    int n = 0;
                    foreach (var r in rends.Where(r => Fmt(r.name) == np.Renderer))
                        foreach (var m in Mats(s, r)) if (m && Fmt(m.name) == np.MaterialName) { m.name = np.Value; n++; }
                    return n > 0 ? null : "material not found";
                }
                case MaterialEditorData.MaterialCopy mc: return Copy(s, rends, mc);
            }
            var mi = (MaterialEditorData.MaterialItem)item;
            var mats = new List<Material>();
            foreach (var r in rends) foreach (var m in Mats(s, r)) if (m && Fmt(m.name) == mi.MaterialName && !mats.Contains(m)) mats.Add(m);
            if (mats.Count == 0)
            {
                ExtStats.Inc(s.St.MeMissingMaterials, $"{item.ObjectType} {mi.MaterialName}");
                if (s.Reported.Add((item.ObjectType, item.Slot)))
                    s.Ctx.Log.Add($"materialEditor: {item.ObjectType} {item.Slot}: no material {mi.MaterialName}; the object has " +
                                  string.Join(", ", rends.SelectMany(r => Mats(s, r)).Where(m => m).Select(m => Fmt(m.name)).Distinct().Take(12)));
                return "material not found";
            }
            if (item is MaterialEditorData.ShaderEdit se) return ApplyShader(s, mats, se);

            var pi = (MaterialEditorData.MaterialPropertyItem)item;
            string prop = "_" + pi.Property;
            if (item is MaterialEditorData.KeywordProperty kp)
            {
                foreach (var m in mats) { if (kp.Value) m.EnableKeyword(prop); else m.DisableKeyword(prop); }
                return null;
            }
            // a fallback material (an edit after the build): the properties the fallback fixed keep its values
            var with = mats.Where(m => m.HasProperty(prop) && !FallbackFixed(m, prop)).ToList();
            if (with.Count == 0 && mats.Any(m => m.HasProperty(prop))) return null;
            if (with.Count == 0)
            {
                ExtStats.Inc(s.St.MeAbsentProperties, $"{(mats[0].shader ? mats[0].shader.name : "?")} {prop}");
                return "property not on the shader";
            }
            foreach (var m in with) NoteFitted(s, m, prop);
            switch (item)
            {
                case MaterialEditorData.FloatProperty fp:
                    if (!float.TryParse(fp.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) return "value not a float";
                    foreach (var m in with) m.SetFloat(prop, f);
                    return null;
                case MaterialEditorData.ColorProperty cp:
                    foreach (var m in with) SetColorAsFallback(m, prop, cp.Value);
                    return null;
                case MaterialEditorData.VectorProperty vp:
                    foreach (var m in with) m.SetVector(prop, vp.Value);
                    return null;
                case MaterialEditorData.TextureProperty tp: return ApplyTexture(s, with, prop, tp);
            }
            return "list not supported";
        }

        // ---- the mosaic set (MaterialEditor items on members are kept, not applied)

        /// <summary>The skip reason when <paramref name="item"/> targets only mosaic-set members (by renderer, or a
        /// material that only members hold, or HS2's mosaic material by name); null otherwise.</summary>
        static string MosaicReason(MaterialEditorData.Item item, List<Renderer> rends)
        {
            switch (item)
            {
                case MaterialEditorData.RendererProperty rp:
                    return MosaicSet.IsMemberName(rp.RendererName) || rends.Any(r => Fmt(r.name) == rp.RendererName && MosaicSet.IsMember(r)) ? MosaicSet.MeSkipReason : null;
                case MaterialEditorData.NameProperty np:
                    return MosaicSet.IsMemberName(np.Renderer) || Fmt(np.MaterialName) == MosaicSet.OriginalMaterial ||
                           rends.Any(r => Fmt(r.name) == np.Renderer && MosaicSet.IsMember(r)) ? MosaicSet.MeSkipReason : null;
                case MaterialEditorData.MaterialItem mi:
                {
                    if (Fmt(mi.MaterialName) == MosaicSet.OriginalMaterial) return MosaicSet.MeSkipReason;
                    var holders = rends.Where(r => r.sharedMaterials.Any(m => m && Fmt(m.name) == mi.MaterialName)).ToList();
                    return holders.Count > 0 && holders.All(MosaicSet.IsMember) ? MosaicSet.MeSkipReason : null;
                }
            }
            return null;
        }

        /// <summary>The character's MaterialEditor items that are kept but not applied because they target mosaic-set
        /// members (a function of the plugin data and the members now).</summary>
        public static List<MosaicProtectedItem> MosaicProtected(CharaContext ctx)
        {
            var l = new List<MosaicProtectedItem>();
            var me = ctx?.Ext?.MaterialEditor;
            if (me == null || !me.HasEdits) return l;
            var s = new State { Ctx = ctx, Data = me, St = ctx.Stats };   // renderer lookups only (no cache, no copies)
            foreach (var item in me.AllItems())
            {
                if (item.ObjectType == OT.Unknown) continue;
                var rends = Renderers(s, item.ObjectType, item.Slot);
                if (rends == null || MosaicReason(item, rends) == null) continue;
                string target = item is MaterialEditorData.RendererProperty rp ? rp.RendererName
                    : item is MaterialEditorData.NameProperty np ? np.Renderer + "/" + np.MaterialName
                    : item is MaterialEditorData.MaterialItem mi ? mi.MaterialName : "";
                l.Add(new MosaicProtectedItem
                {
                    Kind = item.GetType().Name,
                    Object = $"{item.ObjectType} {(item.ObjectType == OT.Character ? 0 : item.Slot)}",
                    Target = target,
                    Property = item is MaterialEditorData.MaterialPropertyItem p ? p.Property
                        : item is MaterialEditorData.RendererProperty r2 ? r2.Property.ToString() : null,
                });
            }
            return l;
        }

        // ---- objects and materials
        static List<Renderer> Renderers(State s, OT type, int slot)
        {
            if (s.Objects.TryGetValue((type, slot), out var l)) return l;
            var ctx = s.Ctx;
            GameObject go = null;
            switch (type)
            {
                case OT.Clothing: go = slot >= 0 && slot < ctx.Clothes.Length ? ctx.Clothes[slot] : null; break;
                case OT.Accessory: go = ctx.AccessoryObject(slot); break;
                case OT.Hair: go = slot >= 0 && slot < ctx.Hair.Length ? ctx.Hair[slot] : null; break;
            }
            if (type == OT.Character)
            {
                l = new List<Renderer>();
                if (ctx.Root) CollectBody(ctx.Root.transform, l);
                if (l.Count == 0) l = null;
            }
            else l = go ? go.GetComponentsInChildren<Renderer>(true).ToList() : null;
            return s.Objects[(type, slot)] = l;
        }

        static void CollectBody(Transform t, List<Renderer> l)
        {
            if (ClothesRoots.Contains(t.name)) return;
            if (BodyParts.Contains(t.name))
            {
                var r = t.GetComponent<Renderer>();
                if (r) l.Add(r);
            }
            for (int i = 0; i < t.childCount; i++) CollectBody(t.GetChild(i), l);
        }

        // The renderer's materials, made per-character (copies of shared bundle materials) on first use. A renderer that draws
        // the body's materials has none of its own (after 1.0.0): items addressed to it apply
        // to nothing.
        static Material[] Mats(State s, Renderer r)
        {
            if (s.Ctx.DrawsBodyMaterials(r) || s.Ctx.DrawsBodySkin(r)) return new Material[0];
            if (s.Mats.TryGetValue(r, out var a)) return a;
            var mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (!m || Owned(s, m)) continue;
                var c = MaterialCopies.Copy(m);
                s.Ctx.OwnedMaterials.Add(c);
                mats[i] = c;
                changed = true;
            }
            if (changed) r.sharedMaterials = mats;
            return s.Mats[r] = mats;
        }

        static bool Owned(State s, Material m) => s.Ctx.OwnedMaterials.Contains(m) || m == s.Ctx.BodyDraw || m == s.Ctx.FaceDraw;

        // ---- list kinds
        static string RendererProp(List<Renderer> rends, MaterialEditorData.RendererProperty rp)
        {
            var hit = rends.Where(r => Fmt(r.name) == rp.RendererName).ToList();
            if (hit.Count == 0) return "renderer not found";
            if (!int.TryParse(rp.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)) return "value not an int";
            foreach (var r in hit)
                switch (rp.Property)
                {
                    case MaterialEditorData.RendererPropertyType.Enabled: r.enabled = v != 0; break;
                    case MaterialEditorData.RendererPropertyType.ShadowCastingMode: r.shadowCastingMode = (ShadowCastingMode)v; break;
                    case MaterialEditorData.RendererPropertyType.ReceiveShadows: r.receiveShadows = v != 0; break;
                    case MaterialEditorData.RendererPropertyType.UpdateWhenOffscreen: if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = v != 0; break;
                    case MaterialEditorData.RendererPropertyType.RecalculateNormals: if (v == 1) RecalculateNormals(r); break;
                    default: return "renderer property not supported";
                }
            return null;
        }

        // bake the skinned mesh, recalculate its normals and give the renderer a copy of the shared mesh with them.
        static void RecalculateNormals(Renderer r)
        {
            if (r is SkinnedMeshRenderer smr && smr.sharedMesh)
            {
                var baked = new Mesh();
                smr.BakeMesh(baked);
                baked.RecalculateNormals();
                var copy = Object.Instantiate(smr.sharedMesh);
                copy.name = smr.sharedMesh.name;
                if (baked.vertexCount == copy.vertexCount) copy.normals = baked.normals;
                Object.DestroyImmediate(baked);
                smr.sharedMesh = copy;
            }
            else
            {
                var mf = r.GetComponent<MeshFilter>();
                if (!mf || !mf.sharedMesh) return;
                var copy = Object.Instantiate(mf.sharedMesh);
                copy.name = mf.sharedMesh.name;
                copy.RecalculateNormals();
                mf.sharedMesh = copy;
            }
        }

        static string Copy(State s, List<Renderer> rends, MaterialEditorData.MaterialCopy mc)
        {
            int n = 0;
            foreach (var r in rends)
            {
                var mats = Mats(s, r);
                var src = mats.FirstOrDefault(m => m && Fmt(m.name) == mc.MaterialName);
                if (!src) continue;
                n++;
                if (mats.Any(m => m && Fmt(m.name) == mc.MaterialCopyName)) continue;
                var c = MaterialCopies.Copy(src, mc.MaterialCopyName);
                s.Ctx.OwnedMaterials.Add(c);
                var list = mats.ToList();
                list.Add(c);
                r.sharedMaterials = s.Mats[r] = list.ToArray();
            }
            return n > 0 ? null : "material not found";
        }

        static string ApplyShader(State s, List<Material> mats, MaterialEditorData.ShaderEdit se)
        {
            if (se.ShaderName != null)
            {
                var sh = ResolveShader(se.ShaderName, s.Ctx.Catalog, out bool direct);
                if (!sh)
                {
                    ExtStats.Inc(s.St.MeUnknownShaders, se.ShaderName);
                    if (s.C.MissingShaders.Add(se.ShaderName))
                        s.Ctx.Missing.Add(new MissingContent { Kind = "me.shader", Guid = se.ShaderName, Detail = "no fitted URP shader; current shader kept" });
                    return "shader without a fitted replacement";
                }
                var pluginKeywords = PluginCopyKeywords(se.ShaderName, s.Ctx.Catalog);
                foreach (var m in mats)
                {
                    if (m.shader != sh) ShaderMap.Swap(m, sh);
                    if (pluginKeywords != null) KeepKeywords(m, pluginKeywords);
                }
                s.St.MeShaderSwaps++;
                if (direct) s.St.MeShaderSwapsToFitted++;
            }
            if (se.RenderQueue.HasValue) foreach (var m in mats) m.renderQueue = se.RenderQueue.Value;
            return null;
        }

        /// <summary>The shader to use for an ME shader name: the registry's copy of the name (MaterialEditorShaders of
        /// <paramref name="catalog"/>; Built-in, ShaderMap.ApplyUrp maps it later) when no render pipeline is active, else, or
        /// when the registry has no copy, the replacement itself (<paramref name="direct"/>); null when ShaderMap has no
        /// replacement for the name's copy. Never looks at the shaders loaded in the process.</summary>
        public static Shader ResolveShader(string name, HS2Next.Content.ContentCatalog catalog, out bool direct)
        {
            direct = false;
            // The registry's copy decides the replacement when several bundle copies share the name (ShaderMap.CopyRoutes /
            // CopyAccepts); without one, the name's main replacement.
            if (!ShaderMap.Fitted.ContainsKey(name) && !ShaderMap.CopyRoutes.ContainsKey(name)) return null;
            var registry = MaterialEditorShaders.Of(catalog);
            // A name that neither the plugin itself nor a loaded manifest declares is not in the plugin's registry: the edit is
            // skipped and the current shader kept, as in the game.
            if (registry != null && !PluginCopies.ContainsKey(name) && registry.Find(name) == null) return null;
            var orig = registry?.Original(name);
            if (!ShaderMap.TryResolve(name, orig, out var fittedName)) return null;
            var fitted = UnityEngine.Shader.Find(fittedName);
            if (GraphicsSettings.currentRenderPipeline == null && orig) return orig;
            direct = fitted;
            return fitted;
        }

        static readonly string[] NoKeywords = new string[0];

        /// <summary>
        /// The plugin's own shader copies (the game shaders it declares itself, carried in the plugin) by name, with the material
        /// keywords each copy declares (its compiled variants'; the pipeline's light, shadow and lightmap keywords left out). A
        /// swap to such a copy keeps only those keywords on the material, as in the game: the copy has no variant for the others.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string[]> PluginCopies = new Dictionary<string, string[]>(System.StringComparer.Ordinal)
        {
            { "Standard", new[] { "_ALPHAPREMULTIPLY_ON" } },
            { "Standard_culloff", new[] { "_ALPHAPREMULTIPLY_ON" } },
            { "AIT/Clothes True", new[] { "_DITHERSWITCH_ON", "_VERTEXCOLOR_ON", "_YABUREDITHERSWITCH_ON" } },
            { "AIT/Clothes Alpha", NoKeywords },
            { "AIT/Clothes Alpha True", NoKeywords },
            { "AIT/Clothes", NoKeywords },
            { "Shader Forge/hair_07", NoKeywords },
            { "Shader Forge/hair_08", NoKeywords },
            { "AIT/Item", NoKeywords },
            { "AIT/Skin True", NoKeywords },
            { "KriptoFX/ME/Distortion", new[] { "SOFTPARTICLES_ON", "USE_ALPHA_CLIPING", "USE_SOFT_PARTICLES" } },
            { "KriptoFX/ME/GlowCutoutGradient", NoKeywords },
            { "KriptoFX/ME/Particle", new[] { "SOFTPARTICLES_ON" } },
            { "AIT/Eye Translucency", NoKeywords },
            { "AIT/eyelashes", NoKeywords },
            { "AIT/Item Meka studio", new[] { "_EMISSIONCOLORTEXBASE_ON" } },
            { "AIT/Item studio", new[] { "_DITHERSWITCH_ON", "_EMISSIONCOLORTEXBASE_ON" } },
            { "AIT/Liquid", new[] { "_ALPHAPREMULTIPLY_ON" } },
            { "AIT/main eyeshadow lambert", NoKeywords },
            { "AIT/main Item Glass", NoKeywords },
            { "Shader Forge/main_item_studio_add", NoKeywords },
            { "AIT/main Light", NoKeywords },
            { "AIT/main namida", new[] { "_ALPHAPREMULTIPLY_ON" } },
            { "AIT/main texture studio unlit", NoKeywords },
            { "AIT/mnpb", NoKeywords },
            { "Custom/Silhouette", NoKeywords },
            { "AIT/Skin Translucency", NoKeywords },
            { "AIT/Skin Translucency simple", NoKeywords },
            { "AIT/Skin True Face", NoKeywords },
            { "Shader Forge/studio_hair_07", NoKeywords },
            { "Unlit/Color", NoKeywords },
            { "AIT/Additive", NoKeywords },
            { "AIT/main standard2", new[] { "_DITHERSWITCH_ON", "_EMISSIONMAINCOLORBASE_ON", "_EMISSION_ON", "_UV" } },
            { "AIT/main standard2 studio", new[] { "_DITHERSWITCH_ON", "_UV" } },
            { "AIT/main standard2 studio alpha", new[] { "_UV" } },
            { "Shader Forge/main_alpha", NoKeywords },
            { "Shader Forge/main_emblem", NoKeywords },
            { "Shader Forge/main_emblem_clothes", NoKeywords },
            { "Shader Forge/main_hair", NoKeywords },
            { "Shader Forge/main_hair_front", NoKeywords },
            { "Shader Forge/main_item", NoKeywords },
            { "Shader Forge/main_item_ditherd", NoKeywords },
            { "Shader Forge/main_item_emission", NoKeywords },
            { "Shader Forge/main_item_studio", NoKeywords },
            { "Shader Forge/main_item_studio_alpha", NoKeywords },
            { "Shader Forge/main_opaque", NoKeywords },
            { "Shader Forge/main_opaque2", NoKeywords },
            { "Shader Forge/main_skin", NoKeywords },
            { "Shader Forge/main_StandardMDK_studio", NoKeywords },
            { "Shader Forge/main_texture", NoKeywords },
            { "Shader Forge/mnpb", NoKeywords },
            { "Shader Forge/toon_eye_lod0", NoKeywords },
            { "Shader Forge/toon_eyew_lod0", NoKeywords },
            { "Shader Forge/toon_nose_lod0", NoKeywords },
            { "Shader Forge/toon_glasses_lod0", NoKeywords },
            { "Shader Forge/toon_textureanimation", NoKeywords },
            { "Shader Forge/main_color", NoKeywords },
        };

        /// <summary>The keywords of the plugin's own copy of <paramref name="name"/> when that copy is the one in effect (the
        /// plugin declares the name and no loaded manifest declares it again); else null.</summary>
        public static string[] PluginCopyKeywords(string name, HS2Next.Content.ContentCatalog catalog)
        {
            if (name == null || !PluginCopies.TryGetValue(name, out var kws)) return null;
            var registry = MaterialEditorShaders.Of(catalog);
            return registry != null && registry.Find(name) != null ? null : kws;
        }

        /// <summary>Disables the material keywords outside <paramref name="keep"/> (the replacement's own shadow switch that
        /// ShaderMap.Swap sets stays).</summary>
        public static void KeepKeywords(Material m, string[] keep)
        {
            foreach (var k in m.shaderKeywords)
                if (k != ShaderMap.ReceiveShadowsOff && System.Array.IndexOf(keep, k) < 0) m.DisableKeyword(k);
        }

        static readonly Dictionary<(string, string), bool> fittedHas = new Dictionary<(string, string), bool>();

        // Records properties that the material's fitted URP replacement lacks (written, but without effect under URP).
        static void NoteFitted(State s, Material m, string prop)
        {
            string sn = m.shader ? m.shader.name : "";
            if (sn.StartsWith("HS2Next/") || !ShaderMap.TryResolve(m.shader, out var fn)) return;
            if (!fittedHas.TryGetValue((fn, prop), out bool has))
            {
                var f = UnityEngine.Shader.Find(fn);
                fittedHas[(fn, prop)] = has = !f || f.FindPropertyIndex(prop) >= 0;
            }
            if (!has) ExtStats.Inc(s.St.MeNotOnFitted, $"{fn} {prop}");
        }

        static string ApplyTexture(State s, List<Material> mats, string prop, MaterialEditorData.TextureProperty tp)
        {
            if (!tp.TexID.HasValue && !tp.Offset.HasValue && !tp.Scale.HasValue) return "empty texture edit";
            if (tp.TexID.HasValue)
            {
                bool linear = s.O.MaterialEditorLinearNormals && (prop.Contains("Bump") || prop.Contains("Normal"));
                var tex = Decode(s, tp.TexID.Value, linear);
                if (!tex) return s.Data.Textures.ContainsKey(tp.TexID.Value) ? "texture not decodable" : "texture id missing";
                if (tp.TexAnimationDef != null) ExtStats.Inc(s.St.MeNotes, "texture animation shown as its first texture");
                foreach (var m in mats)
                {
                    if (s.C.TexSettingsDone.Add(tex))
                    {
                        var old = m.GetTexture(prop);
                        tex.anisoLevel = old ? old.anisoLevel : 1;
                        tex.filterMode = old ? old.filterMode : FilterMode.Bilinear;
                        tex.wrapMode = old ? old.wrapMode : TextureWrapMode.Repeat;
                    }
                    m.SetTexture(prop, tex);
                    if (MirrorsBaseMap(m, prop)) m.SetTexture("_BaseMap", tex);
                    s.St.MeTextureAssignments++;
                }
            }
            if (tp.Offset.HasValue)
                foreach (var m in mats) { m.SetTextureOffset(prop, tp.Offset.Value); if (MirrorsBaseMap(m, prop)) m.SetTextureOffset("_BaseMap", tp.Offset.Value); }
            if (tp.Scale.HasValue)
                foreach (var m in mats) { m.SetTextureScale(prop, tp.Scale.Value); if (MirrorsBaseMap(m, prop)) m.SetTextureScale("_BaseMap", tp.Scale.Value); }
            return null;
        }

        // ---- materials the URP/Lit fallback already replaced (edits after the build; see the file comment)

        /// <summary>A property the fallback set itself: _Cutoff (0.5 for a cutout original; an original without it had no
        /// such property to edit) and _Smoothness (0.2).</summary>
        static bool FallbackFixed(Material m, string prop) => (prop == "_Cutoff" || prop == "_Smoothness") && ShaderMap.IsFallback(m);

        static bool MirrorsBaseMap(Material m, string prop) => prop == "_MainTex" && ShaderMap.IsFallback(m) && m.HasProperty("_BaseMap");

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
                // the fallback took _BaseColor from the original's own _BaseColor (α 1); without one, from _Color
                if (!ShaderMap.FallbackColorFromColor(m)) m.SetColor("_BaseColor", new Color(c.r, c.g, c.b, 1));
                return;
            }
            m.SetColor(prop, c);
        }

        static void Use(State s, int id, bool linear)
        {
            if (!s.C.Used.TryGetValue(s.Current, out var u)) s.C.Used[s.Current] = u = new HashSet<(int, bool)>();
            u.Add((id, linear));
        }

        static Texture2D Decode(State s, int id, bool linear)
        {
            var c = s.C;
            if (c.Tex.TryGetValue((id, linear), out var t) && t) { Use(s, id, linear); return t; }
            if (c.TexFailed.Contains((id, linear)) || !s.Data.Textures.TryGetValue(id, out var bytes)) return null;
            t = ExtTextures.Acquire(bytes, linear, true, $"me tex {id}", out string key);
            if (!t) { c.TexFailed.Add((id, linear)); return null; }
            if (c.Res == null) c.Res = CharaExtResources.Of(s.Ctx.Root);
            if (c.Res) c.Res.TextureKeys.Add(key);   // (no root: the reference is kept for the rest of the session)
            c.Keys[(id, linear)] = key;
            s.St.MeTexturesDecoded++;
            s.St.MeTextureBytes += bytes.Length;
            c.Source[(id, linear)] = bytes;
            Use(s, id, linear);
            return c.Tex[(id, linear)] = t;
        }
    }
}
