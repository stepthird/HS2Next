// Fonts of the UI (spike S2b / S2c): the four Noto Sans CJK region subsets
// (JP / SC, Regular / Bold) as Editor-made dynamic FontAssets in Resources/UI/Fonts (Editor/UiFontAssets.cs: SDFAA,
// sampling 42, padding 4, 2048² multi-atlas, data cleared; the player fills them at run time). Chains: en and ja — JP →
// SC → Segoe UI Symbol → Segoe UI Emoji; zh-Hans — SC → JP → the same OS fonts. The other region in a chain is a
// run-time asset of the same font file (an asset cannot be its own fallback's fallback). Bold = fontWeightTable[7] of
// the primary (the region's Bold asset). The root's unityFontDefinition is the language's primary (ATG on the root).
// A missing bundled font → finding ui.fontMissing (error) and the OS fonts carry the text.
using System.Collections.Generic;
using HS2Next.UI.Core;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace HS2Next.UI
{
    public sealed class UiFonts
    {
        public const string ResourceDir = "UI/Fonts/";
        public static readonly string[] AssetNames = { "NotoSansJP-Regular SDF", "NotoSansJP-Bold SDF", "NotoSansSC-Regular SDF", "NotoSansSC-Bold SDF" };

        public FontAsset JpRegular { get; private set; }
        public FontAsset JpBold { get; private set; }
        public FontAsset ScRegular { get; private set; }
        public FontAsset ScBold { get; private set; }
        FontAsset jpFallback, scFallback, osSymbol, osEmoji, osJp, osSc;
        /// <summary>The primary of the current language.</summary>
        public FontAsset Primary { get; private set; }
        public bool Loaded => JpRegular != null && ScRegular != null;
        public string Problem { get; private set; }

        public static UiFonts Load()
        {
            var f = new UiFonts();
            // run-time copies of the bundled assets' fonts: the dynamic atlases fill without touching the Editor assets
            // (an Editor play session would otherwise write its glyphs into Resources/UI/Fonts)
            f.JpRegular = Copy(Resources.Load<FontAsset>(ResourceDir + AssetNames[0]));
            f.JpBold = Copy(Resources.Load<FontAsset>(ResourceDir + AssetNames[1]));
            f.ScRegular = Copy(Resources.Load<FontAsset>(ResourceDir + AssetNames[2]));
            f.ScBold = Copy(Resources.Load<FontAsset>(ResourceDir + AssetNames[3]));
            f.osSymbol = Os("Segoe UI Symbol");
            f.osEmoji = Os("Segoe UI Emoji");
            if (!f.Loaded)
            {
                f.Problem = "bundled Noto Sans CJK font assets not found under Resources/" + ResourceDir + " (run UiFontAssets.Build)";
                f.osJp = Os("Yu Gothic UI");
                f.osSc = Os("Microsoft YaHei UI");
                return f;
            }
            f.jpFallback = Runtime(f.JpRegular);
            f.scFallback = Runtime(f.ScRegular);
            Chain(f.JpRegular, f.scFallback, f.osSymbol, f.osEmoji);
            Chain(f.ScRegular, f.jpFallback, f.osSymbol, f.osEmoji);
            if (f.JpBold) Chain(f.JpBold, f.scFallback, f.osSymbol, f.osEmoji);
            if (f.ScBold) Chain(f.ScBold, f.jpFallback, f.osSymbol, f.osEmoji);
            Bold(f.JpRegular, f.JpBold);
            Bold(f.ScRegular, f.ScBold);
            return f;
        }

        static FontAsset Os(string family)
        {
            try { return FontAsset.CreateFontAsset(family, "Regular", 42); }
            catch (System.Exception) { return null; }
        }

        static FontAsset Copy(FontAsset a)
        {
            if (!a) return null;
            var c = Runtime(a);
            if (!c) return a;
            c.name = a.name;
            return c;
        }

        static FontAsset Runtime(FontAsset a)
        {
            var src = a ? a.sourceFontFile : null;
            if (!src) return null;
            return FontAsset.CreateFontAsset(src, 42, 4, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
        }

        static void Chain(FontAsset primary, params FontAsset[] rest)
        {
            var l = new List<FontAsset>();
            foreach (var r in rest) if (r) l.Add(r);
            primary.fallbackFontAssetTable = l;
        }

        static void Bold(FontAsset regular, FontAsset bold)
        {
            var t = regular ? regular.fontWeightTable : null;
            if (t != null && t.Length > 7 && bold) t[7].regularTypeface = bold;
        }

        /// <summary>The primary font asset of a language (zh-Hans → SC, else JP; OS fonts when not bundled).</summary>
        public FontAsset PrimaryFor(string language)
        {
            bool zh = language == L10nTable.ZhHans;
            if (Loaded) return zh ? ScRegular : JpRegular;
            return zh ? (osSc ?? osJp) : (osJp ?? osSc);
        }

        /// <summary>Sets the root's font for a language.</summary>
        public void Apply(VisualElement root, string language)
        {
            Primary = PrimaryFor(language);
            if (root == null) return;
            root.style.unityTextGenerator = TextGeneratorType.Advanced;
            if (Primary) root.style.unityFontDefinition = FontDefinition.FromSDFFont(Primary);
        }

        /// <summary>Atlas memory of a font asset's pages (bytes; Alpha8 = 1 byte per texel).</summary>
        public static long AtlasBytes(FontAsset a)
        {
            long n = 0;
            if (!a || a.atlasTextures == null) return 0;
            foreach (var t in a.atlasTextures)
                if (t && t.width > 1) n += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t);
            return n;
        }
    }
}
