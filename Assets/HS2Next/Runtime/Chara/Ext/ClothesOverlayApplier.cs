// KCOX clothes overlays, applied after the clothes colour compositing
// (the ChangeCustomClothes postfix of the plugin). Per main overlay id (ct_clothesTop .. ct_shoes = kind 0..7):
// targets are the first renderer of rendNormal01 (or, when that group is empty, of rendNormal03) and the first of
// rendNormal02; the target's material _MainTex must be the colour RenderTexture (items without a colour creator are
// skipped). Override clears that RT to transparent black first ([nil, true] = cleared, nothing drawn); a texture is
// then composited with its Override flag and BlendingMode (Default alpha over / LinearAlpha). The RT is the shared
// creator output, so every renderer of the group shows the result; slot 2 gets the same image in its own UV layout.
// Size: an overlay (or TextureSizeOverride) larger than the colour pass's base size (its main texture)
// grows the RT (at most 2x with the default OriginalX2). The growth happens inside the creator that owns the RT: it gets
// an output of the larger size and runs its colour pass again at that size (as the plugin does), and every material of
// the item that used the old RT is rebound; so the creator's RT stays the drawn one and later colour passes draw into
// it at the grown size (a re-application finds the size already reached and only counts it). An RT no creator owns
// (no edit context) is grown by scaling its content into the larger RT, as before 4.4.
// Studio switch: EnableInStudio = false shows no clothes overlay. Colormask_/Pattern_/Override_ ids (none on this
// install) are counted as skipped. The composite is not repeated when the clothes are rebuilt (no such rebuild here).
using System.Collections.Generic;
using System.Linq;
using AIChara;
using HS2Next.Data.Ext;
using UnityEngine;

namespace HS2Next.Chara.Ext
{
    public static class ClothesOverlayApplier
    {
        public static void Apply(CharaContext ctx, ClothesOverlayData data, CharaExtOptions o) => Apply(ctx, data, o, -1);

        /// <summary>The overlays of one clothes kind (0..7) only: after an edit's colour pass of that kind.</summary>
        public static void ApplyKind(CharaContext ctx, ClothesOverlayData data, CharaExtOptions o, int kind) => Apply(ctx, data, o, kind);

        static void Apply(CharaContext ctx, ClothesOverlayData data, CharaExtOptions o, int onlyKind)
        {
            if (data == null || data.Overlays.Count == 0) return;
            var st = ctx.Stats;
            var log = new List<string>();
            foreach (var kv in data.Overlays.OrderBy(k => k.Key, System.StringComparer.Ordinal))
            {
                if (onlyKind >= 0 && ClothesOverlayData.KindOf(kv.Key) != onlyKind) continue;
                st.KcoxEntries++;
                string why = One(ctx, data, o, kv.Key, kv.Value, log);
                if (why != null) { ExtStats.Inc(st.KcoxSkipped, why); log.Add($"{kv.Key} skipped ({why})"); }
            }
            ctx.Log.Add("kcox: " + string.Join("; ", log) + (OverlayCompositor.HasGpu ? "" : " (no graphics device: decoded, not drawn)"));
        }

        static string One(CharaContext ctx, ClothesOverlayData data, CharaExtOptions o, string id, ClothesTexData e, List<string> log)
        {
            var st = ctx.Stats;
            if (o.OverlaysStudio && !data.EnableInStudio) return "Studio switch off";
            int kind = ClothesOverlayData.KindOf(id);
            if (kind < 0) return "id not supported (colour mask / pattern / override source)";
            var go = ctx.Clothes[kind];
            if (!go) return "no clothes in the slot";
            var cmp = go.GetComponent<CmpClothes>();
            if (cmp == null) return "no CmpClothes";
            Renderer First(List<Renderer> g) => g?.FirstOrDefault(r => r);
            var t1 = cmp.rendNormal01 != null && cmp.rendNormal01.Count > 0 ? First(cmp.rendNormal01) : First(cmp.rendNormal03);
            var targets = new[] { t1, First(cmp.rendNormal02) }.Where(r => r).ToList();
            if (targets.Count == 0) return "no target renderer";

            Texture2D tex = null; string key = null;
            if (e.TextureBytes != null)
            {
                tex = ExtTextures.Acquire(e.TextureBytes, false, false, "kcox " + id, out key);
                if (!tex) return "texture not decodable";
            }
            try
            {
                int n = 0;
                var done = new HashSet<RenderTexture>();
                foreach (var r in targets)
                {
                    var m = r.sharedMaterial;
                    var rt = m && m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") as RenderTexture : null;
                    if (!rt) { if (n == 0) return "_MainTex is not a colour RenderTexture"; break; }
                    if (!done.Add(rt)) continue;
                    rt = Grow(ctx, o, go, rt, e, tex, data.TextureSizeOverride.TryGetValue(id, out int so) ? so : 0, log, id);
                    if (e.Override) OverlayCompositor.Clear(rt, new Color(0, 0, 0, 0));
                    if (tex) OverlayCompositor.ApplyOverlay(rt, tex, e.Override, e.BlendingMode == 1 ? OverlayCompositor.ModeLinearAlpha : OverlayCompositor.ModeDefault);
                    n++;
                }
                if (e.Override) st.KcoxOverrides++;
                if (tex) st.KcoxOverlays++;
                if (e.Override && !tex) st.KcoxCleared++;
                st.KcoxApplied++;
                log.Add($"{id} {(tex ? $"{tex.width}x{tex.height}" : "no image")}{(e.Override ? " override" : "")}{(e.BlendingMode == 1 ? " linear" : "")} on {n} RT{(n == 1 ? "" : "s")}");
                return null;
            }
            finally { ExtTextures.Release(key); }
        }

        // grows the colour RT to the overlay (or size override) size; returns the RT to draw on.
        static RenderTexture Grow(CharaContext ctx, CharaExtOptions o, GameObject item, RenderTexture rt, ClothesTexData e, Texture2D tex, int sizeOverride, List<string> log, string id)
        {
            // the colour pass that owns the RT: its base size is its main texture's (the RT may have grown already)
            var owner = OwnerOf(ctx, rt);
            int bw = rt.width, bh = rt.height;
            if (owner != null && owner.Creator.Main) { bw = owner.Creator.Main.width; bh = owner.Creator.Main.height; }
            int nw = bw, nh = bh;
            if (o.OverlaySizeLimit != OverlaySizeLimit.Original)
            {
                float ratio = 1;
                if (tex) ratio = Mathf.Max(ratio, Mathf.Max(tex.width / (float)bw, tex.height / (float)bh));
                if (sizeOverride > 0) ratio = Mathf.Max(ratio, Mathf.Max(sizeOverride / (float)bw, sizeOverride / (float)bh));
                if (ratio > 1)
                {
                    if (o.OverlaySizeLimit == OverlaySizeLimit.OriginalX2) ratio = Mathf.Min(ratio, 2);
                    int max = OverlayCompositor.MaxSize;
                    nw = Mathf.Min(Mathf.RoundToInt(bw * ratio), max); nh = Mathf.Min(Mathf.RoundToInt(bh * ratio), max);
                }
            }
            bool grown = nw != bw || nh != bh;
            if (rt.width == nw && rt.height == nh)
            {
                // already at the size (a re-application after a colour pass of the grown creator): counted as a fresh build counts it
                if (grown) { ctx.Stats.KcoxResized++; log.Add($"{id} RT {bw}x{bh} -> {nw}x{nh} (kept)"); }
                return rt;
            }
            if (!OverlayCompositor.CanCreate(nw, nh)) { log.Add($"{id} RT {rt.width}x{rt.height} -> {nw}x{nh} not created (no graphics device)"); return rt; }
            RenderTexture nrt;
            if (owner != null)
            {
                var old = owner.Creator.Resize(nw, nh);
                owner.Creator.Rebuild();
                nrt = owner.Creator.Target;
                if (!ReferenceEquals(old, rt)) Debug.LogWarning("kcox: the creator's RT changed during the resize of " + id);
            }
            else nrt = OverlayCompositor.Resized(rt, nw, nh, copy: !e.Override);
            int rebound = 0;
            foreach (var r in item.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m && m.HasProperty("_MainTex") && m.GetTexture("_MainTex") == rt) { m.SetTexture("_MainTex", nrt); rebound++; }
            if (RenderTexture.active == rt) RenderTexture.active = null;
            rt.Release();
            Object.DestroyImmediate(rt);
            if (grown) ctx.Stats.KcoxResized++;
            log.Add($"{id} RT {bw}x{bh} -> {nw}x{nh} ({rebound} materials{(owner != null ? ", colour pass re-run" : "")})");
            return nrt;
        }

        /// <summary>The clothes colour pass whose output is <paramref name="rt"/> (null when none: no edit context, or an RT
        /// the creators do not own).</summary>
        static HS2Next.Chara.Edit.CompositeInputs OwnerOf(CharaContext ctx, RenderTexture rt)
        {
            var x = ctx?.Edit;
            if (x == null || !rt) return null;
            for (int k = 0; k < 8; k++)
                for (int j = 0; j < 3; j++)
                {
                    var c = x.ClothesColor[k, j];
                    if (c?.Creator != null && c.Creator.Target == rt) return c;
                }
            return null;
        }
    }
}
