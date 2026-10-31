// Overlay Mods: KSOX skin overlays (character cards) and KCOX clothes overlays
// (character and coordinate cards). Images are complete PNG files; they are not decoded here.
using System;
using System.Collections.Generic;
using System.Globalization;
using HS2Next.IO;

namespace HS2Next.Data.Ext
{
    /// <summary>KSOX TexType: the skin texture an overlay targets.</summary>
    public enum SkinTexType
    {
        Unknown = 0, BodyOver = 1, FaceOver = 2, BodyUnder = 3, FaceUnder = 4, EyeUnder = 5, EyeOver = 6,
        EyeUnderL = 7, EyeOverL = 8, EyeUnderR = 9, EyeOverR = 10, EyebrowUnder = 20, EyelineUnder = 30,
        BodyDetailOver = 41, FaceDetailOver = 42, BodyDetailUnder = 43, FaceDetailUnder = 44,
    }

    /// <summary>KSOX skin overlays. Versions 1 (keys = TexType names) and 2 (Lookup + _TextureID_&lt;id&gt; store).</summary>
    public class SkinOverlayData
    {
        public const int MaxVersion = 2;

        public int Version;
        /// <summary>TexType -> PNG bytes. Legacy EyeUnder/EyeOver are expanded to the L and R types.</summary>
        public Dictionary<SkinTexType, byte[]> Textures = new Dictionary<SkinTexType, byte[]>();
        /// <summary>v2: TexType -> texture store id (several types may share one id); empty for v1.</summary>
        public Dictionary<SkinTexType, int> TextureIds = new Dictionary<SkinTexType, int>();
        /// <summary>Studio "Skin overlays" (types 1-4, 41-44) and "Eye overlays" (types 5-30) switches.</summary>
        public bool EnableInStudioSkin = true, EnableInStudioIris = true;
        /// <summary>True when LOCAL_/DEDUPED_ keys (non-bundled textures) are present; those are not resolved.</summary>
        public bool HasNonBundledTextures;

        public bool HasTextures => Textures.Count > 0;

        public static bool IsSkinType(SkinTexType t) => (t >= SkinTexType.BodyOver && t <= SkinTexType.FaceUnder) || (t >= SkinTexType.BodyDetailOver && t <= SkinTexType.FaceDetailUnder);

        public static SkinOverlayData Decode(int version, object data, ExtContext ctx)
        {
            const string P = PluginIds.Ksox;
            if (version > MaxVersion || version < 1) { ctx.Warn(P, $"unsupported data version {version}; ignored"); return null; }
            var r = new SkinOverlayData { Version = version };
            if (!(data is Dictionary<object, object> map)) return r;
            if (version == 1)
            {
                foreach (var kv in map)
                {
                    if (!(kv.Key is string key) || !(kv.Value is byte[] png) || png.Length == 0) continue;
                    if (!Enum.TryParse(key, false, out SkinTexType t) || t == SkinTexType.Unknown) { ctx.Warn(P, "v1: unknown key " + key); continue; }
                    if (t == SkinTexType.EyeUnder) { r.Textures[SkinTexType.EyeUnderL] = png; r.Textures[SkinTexType.EyeUnderR] = png; }
                    else if (t == SkinTexType.EyeOver) { r.Textures[SkinTexType.EyeOverL] = png; r.Textures[SkinTexType.EyeOverR] = png; }
                    else r.Textures[t] = png;
                }
                return r;
            }
            r.EnableInStudioSkin = V.B(map.Get("EnableInStudioSkin"), true);
            r.EnableInStudioIris = V.B(map.Get("EnableInStudioIris"), true);
            foreach (var k in map.Keys)
                if (k is string s && (s.StartsWith("LOCAL_", StringComparison.Ordinal) || s.StartsWith("DEDUPED_", StringComparison.Ordinal)))
                {
                    r.HasNonBundledTextures = true;
                    ctx.Warn(P, s + ": non-bundled textures are not resolved");
                }
            var lookup = ctx.Nested(map.Get("Lookup"), P, "Lookup");
            if (!(lookup is Dictionary<object, object> byCoord)) return r;
            foreach (var ck in byCoord)
            {
                if (ck.Key.Int(-1) != 0) { ctx.Warn(P, $"Lookup: coordinate key {ck.Key} ignored (HS2 has only 0)"); continue; }
                if (!(ck.Value is Dictionary<object, object> types)) continue;
                foreach (var tk in types)
                {
                    var t = (SkinTexType)tk.Key.Int(0);
                    int id = tk.Value.Int(-1);
                    if (map.Get("_TextureID_" + id.ToString(CultureInfo.InvariantCulture)) is byte[] png && png.Length > 0)
                    {
                        r.Textures[t] = png;
                        r.TextureIds[t] = id;
                    }
                    else if (!r.HasNonBundledTextures) ctx.Warn(P, $"Lookup {t} -> missing texture id {id}; dropped");
                }
            }
            return r;
        }
    }

    /// <summary>KCOX entry: array [TextureBytes (PNG or nil), Override, BlendingMode?].</summary>
    public class ClothesTexData
    {
        public byte[] TextureBytes;
        /// <summary>Clear the target clothes texture to transparent before drawing.</summary>
        public bool Override;
        /// <summary>0 Default (alpha over), 1 LinearAlpha; 0 when absent.</summary>
        public int BlendingMode;
        /// <summary>Dropped on save: no texture and no override. ([nil, true] is NOT empty: it clears the texture.)</summary>
        public bool IsEmpty => TextureBytes == null && !Override;
    }

    /// <summary>KCOX clothes overlays. Character cards: one level more ({CoordinateType 0 -> {id -> data}}); coordinates: {id -> data}.</summary>
    public class ClothesOverlayData
    {
        public const int MaxVersion = 2;
        /// <summary>Main overlay ids for clothes kind 0..7 (= clothes GameObject names). Other ids: Colormask_&lt;id&gt;, Pattern_&lt;k&gt;_&lt;id&gt;, Override_&lt;id&gt;.</summary>
        public static readonly string[] MainClothesNames = { "ct_clothesTop", "ct_clothesBot", "ct_inner_t", "ct_inner_b", "ct_gloves", "ct_panst", "ct_socks", "ct_shoes" };

        public int Version;
        /// <summary>Clothes id -> overlay; empty entries are dropped as the plugin does.</summary>
        public Dictionary<string, ClothesTexData> Overlays = new Dictionary<string, ClothesTexData>(StringComparer.Ordinal);
        /// <summary>Clothes id -> forced render texture size (0/absent = none).</summary>
        public Dictionary<string, int> TextureSizeOverride = new Dictionary<string, int>(StringComparer.Ordinal);
        /// <summary>Studio "Clothes overlays" switch (character cards).</summary>
        public bool EnableInStudio = true;
        public bool HasNonBundledTextures;

        public int TextureCount { get { int n = 0; foreach (var o in Overlays.Values) if (o.TextureBytes != null) n++; return n; } }

        /// <summary>Clothes kind 0..7 of a main overlay id, else -1.</summary>
        public static int KindOf(string id) => Array.IndexOf(MainClothesNames, id);

        public static ClothesOverlayData Decode(int version, object data, ExtContext ctx)
        {
            const string P = PluginIds.Kcox;
            if (version > MaxVersion || version < 1) { ctx.Warn(P, $"unsupported data version {version}; ignored"); return null; }
            var r = new ClothesOverlayData { Version = version };
            if (!(data is Dictionary<object, object> map)) return r;
            r.EnableInStudio = V.B(map.Get("EnableInStudio"), true);
            foreach (var k in map.Keys)
                if (k is string s && (s.StartsWith("LOCAL", StringComparison.Ordinal) || s.StartsWith("DEDUPED", StringComparison.Ordinal)))
                {
                    r.HasNonBundledTextures = true;
                    ctx.Warn(P, s + ": non-bundled textures are not resolved");
                }
            foreach (var kv in Level(ctx.Nested(map.Get("Overlays"), P, "Overlays"), ctx))
            {
                var a = kv.Value as List<object>;
                if (a == null) { ctx.Warn(P, $"{kv.Key}: entry is not an array"); continue; }
                var e = new ClothesTexData
                {
                    TextureBytes = a.Count > 0 && a[0] is byte[] b && b.Length > 0 ? b : null,
                    Override = a.Count > 1 && V.B(a[1], false),
                    BlendingMode = a.Count > 2 ? V.I(a[2], 0) : 0,
                };
                if (!e.IsEmpty) r.Overlays[kv.Key] = e;
            }
            foreach (var kv in Level(ctx.Nested(map.Get("TextureSizeOverride"), P, "TextureSizeOverride"), ctx))
            {
                int size = V.I(kv.Value, 0);
                if (size > 0) r.TextureSizeOverride[kv.Key] = size;
            }
            return r;
        }

        // Accepts both shapes: {string id -> v} (coordinate) and {int coordinateType -> {string id -> v}} (character card).
        static IEnumerable<KeyValuePair<string, object>> Level(object o, ExtContext ctx)
        {
            if (!(o is Dictionary<object, object> m)) yield break;
            foreach (var kv in m)
            {
                if (kv.Key is string id) yield return new KeyValuePair<string, object>(id, kv.Value);
                else if (kv.Value is Dictionary<object, object> inner)
                {
                    if (kv.Key.Int(-1) != 0) { ctx.Warn(PluginIds.Kcox, $"coordinate key {kv.Key} ignored (HS2 has only 0)"); continue; }
                    foreach (var ikv in inner)
                        if (ikv.Key is string iid) yield return new KeyValuePair<string, object>(iid, ikv.Value);
                }
            }
        }
    }
}
