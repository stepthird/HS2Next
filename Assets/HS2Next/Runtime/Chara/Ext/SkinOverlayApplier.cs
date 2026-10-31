// Skin layers: the card's KSOX images and
// the layers other modules registered on CharaContext.SkinLayers (the uncensor overlay/underlay) are
// composited into the four skin passes, card layer first, then the registered ones (KSOX "additional textures"):
//   body colour  (BodySkin,   draw _MainTex)       under BodyUnder 3        over BodyOver 1
//   body detail  (BodyDetail, draw _DetailMainTex) under BodyDetailUnder 43 over BodyDetailOver 41
//   face colour  (FaceSkin,   draw _MainTex)       under FaceUnder 4        over FaceOver 2
//   face detail  (FaceDetail, draw _DetailMainTex) under FaceDetailUnder 44 over FaceDetailOver 42
// Under-layers are alpha-composited onto a copy of the pass input (so the skin colour tints them and paints, makeup
// and sunburn lie on top), over-layers onto the finished RT (not tinted); both in Default mode (alpha over). The
// layers stay on the TexCreator, so every later Rebuild() re-applies them, as KSOX does inside each skin rebuild.
// Size (KSOX "Texture size limit", default OriginalX2): an overlay larger than the RT grows the RT to the
// overlay size, at most 2x (CharaExtOptions.OverlaySizeLimit). Card images are kept encoded on the layer and decoded
// only for the duration of a rebuild (ExtTextures; nothing stays resident). Studio switches: with
// EnableInStudioSkin = false the card's skin types are not shown (registered layers still are).
// Not implemented: eye / eyebrow / eyeline types (5..30; no card on this install has one), counted as skipped.
using System.Collections.Generic;
using System.Linq;
using HS2Next.Data.Ext;
using UnityEngine;

namespace HS2Next.Chara.Ext
{
    public static class SkinOverlayApplier
    {
        public const string CardSource = "ksox";

        public static void Apply(CharaContext ctx, SkinOverlayData data, CharaExtOptions o)
        {
            var st = ctx.Stats;
            ctx.SkinLayers.RemoveAll(l => l.Source == CardSource);
            if (data != null && data.HasTextures)
            {
                var card = new List<SkinLayer>();
                foreach (var kv in data.Textures.OrderBy(k => (int)k.Key))
                {
                    bool skin = SkinOverlayData.IsSkinType(kv.Key);
                    if (o.OverlaysStudio && !(skin ? data.EnableInStudioSkin : data.EnableInStudioIris)) { ExtStats.Inc(st.SkinLayersSkipped, kv.Key + " (Studio switch off)"); continue; }
                    if (!skin) { ExtStats.Inc(st.SkinLayersSkipped, kv.Key + " (eye/eyebrow/eyeline types not implemented)"); continue; }
                    card.Add(new SkinLayer { TexType = (int)kv.Key, Image = kv.Value, Source = CardSource });
                }
                ctx.SkinLayers.InsertRange(0, card);
            }
            if (ctx.SkinLayers.Count == 0) return;

            var log = new List<string>();
            Pass(ctx, o, ctx.BodySkin, ctx.BodyDraw, "_MainTex", SkinTexType.BodyUnder, SkinTexType.BodyOver, "body", log);
            Pass(ctx, o, ctx.BodyDetail, ctx.BodyDraw, "_DetailMainTex", SkinTexType.BodyDetailUnder, SkinTexType.BodyDetailOver, "body detail", log);
            Pass(ctx, o, ctx.FaceSkin, ctx.FaceDraw, "_MainTex", SkinTexType.FaceUnder, SkinTexType.FaceOver, "face", log);
            Pass(ctx, o, ctx.FaceDetail, ctx.FaceDraw, "_DetailMainTex", SkinTexType.FaceDetailUnder, SkinTexType.FaceDetailOver, "face detail", log);
            var handled = new HashSet<int> { 1, 2, 3, 4, 41, 42, 43, 44 };
            foreach (var l in ctx.SkinLayers.Where(l => !handled.Contains(l.TexType)))
                ExtStats.Inc(st.SkinLayersSkipped, (SkinTexType)l.TexType + " (no pass)");
            ctx.Log.Add("ksox: " + (log.Count == 0 ? "no skin pass has layers" : string.Join("; ", log)) + (OverlayCompositor.HasGpu ? "" : " (no graphics device: decoded, not drawn)"));
        }

        static void Pass(CharaContext ctx, CharaExtOptions o, TexCreator c, Material draw, string prop, SkinTexType underType, SkinTexType overType, string label, List<string> log)
        {
            var st = ctx.Stats;
            var under = ctx.SkinLayers.Where(l => l.TexType == (int)underType).ToList();
            var over = ctx.SkinLayers.Where(l => l.TexType == (int)overType).ToList();
            if (under.Count + over.Count == 0) return;
            if (c == null)
            {
                foreach (var l in under.Concat(over)) ExtStats.Inc(st.SkinLayersSkipped, (SkinTexType)l.TexType + " (no " + label + " pass)");
                return;
            }
            c.Under.Clear(); c.Under.AddRange(under);
            c.Over.Clear(); c.Over.AddRange(over);

            string size = "";
            if (o.OverlaySizeLimit != OverlaySizeLimit.Original && c.Target)
            {
                int bw = c.Target.width, bh = c.Target.height;
                float ratio = 1;
                foreach (var l in under.Concat(over))
                    if (l.TrySize(out int w, out int h)) ratio = Mathf.Max(ratio, Mathf.Max(h / (float)bh, w / (float)bw));
                if (ratio > 1)
                {
                    if (o.OverlaySizeLimit == OverlaySizeLimit.OriginalX2) ratio = Mathf.Min(ratio, 2);
                    int max = OverlayCompositor.MaxSize;
                    int nw = Mathf.Min(Mathf.RoundToInt(bw * ratio), max), nh = Mathf.Min(Mathf.RoundToInt(bh * ratio), max);
                    if ((nw != bw || nh != bh) && !OverlayCompositor.CanCreate(nw, nh))
                    {
                        size = $" RT {bw}x{bh} -> {nw}x{nh} not created (no graphics device)";
                        st.SkinResized.Add($"{label} {nw}x{nh} (not created)");
                    }
                    else if (nw != bw || nh != bh)
                    {
                        var old = c.Resize(nw, nh);
                        if (draw && draw.GetTexture(prop) == old) draw.SetTexture(prop, c.Target);
                        old.Release();
                        Object.DestroyImmediate(old);
                        size = $" RT {bw}x{bh} -> {nw}x{nh}";
                        st.SkinResized.Add($"{label} {nw}x{nh}");
                    }
                }
            }
            var rt = c.Rebuild();
            int drawn = c.LayersDrawn;
            if (draw) draw.SetTexture(prop, rt);
            st.SkinPassesRebuilt++;
            foreach (var l in under.Concat(over)) ExtStats.Inc(st.SkinLayersComposited, $"{(SkinTexType)l.TexType} {(l.Source == CardSource ? "card" : l.Source.Split(':')[0])}");
            if (drawn < under.Count + over.Count) ExtStats.Inc(st.SkinLayersSkipped, $"{label}: {under.Count + over.Count - drawn} undecodable");
            log.Add($"{label} under {under.Count} over {over.Count} ({string.Join(", ", under.Concat(over).Select(Describe))}){size}");
        }

        static string Describe(SkinLayer l) => $"{(SkinTexType)l.TexType} {l.Source}{(l.TrySize(out int w, out int h) ? $" {w}x{h}" : "")}";
    }
}
