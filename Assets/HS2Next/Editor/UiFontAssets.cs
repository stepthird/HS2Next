// Editor setup of the Studio UI's assets (spike S2b / S3): rerunnable.
//   1. Fonts: the Noto Sans CJK region subsets (Sans2.004, upstream names NotoSansJP-Regular.otf, -Bold, NotoSansSC-Regular,
//      -Bold; SIL OFL 1.1) live in Assets/HS2Next/Fonts/NotoSansCJK/ (copied there from -fontsDir <folder> when missing;
//      SHA-256 checked against Fonts/NotoSansCJK/SOURCE.md), imported as dynamic fonts with their data.
//   2. Font assets: one dynamic FontAsset per font in Resources/UI/Fonts/ (SDFAA, sampling 42, padding 4, 2048² pages,
//      multi-atlas; a glyph test, then ClearFontAssetData so the player fills them at run time; atlas texture and material
//      as sub-assets). Existing assets are kept unless -rebuildFonts.
//   3. Resources/UI/HS2NextPanelSettings.asset: an Editor-made PanelSettings (it carries the ICU data ATG needs; a run-time
//      CreateInstance has none) with the theme Resources/UI/HS2NextDark.tss; UiHost instantiates it.
//   4. Resources/UI/HS2NextGizmo.mat: the gizmo pass's material (shader Shaders/UI/Gizmo.shader, shipped through Resources).
//   5. Reimports Resources/UI/L10n/strings.tsv (UiTsvImporter makes it a TextAsset).
// Run: & Tools\unity_batch.ps1 -Method UiFontAssets.Build -LogName ui_assets [-MethodArgs @('-fontsDir','<folder>')]
// Report: Spike/out/ui/ui_assets.txt.
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

public static class UiFontAssets
{
    public const string FontDir = "Assets/HS2Next/Fonts/NotoSansCJK";
    public const string AssetDir = "Assets/HS2Next/Resources/UI/Fonts";
    public const string PanelSettingsPath = "Assets/HS2Next/Resources/UI/HS2NextPanelSettings.asset";
    public const string ThemePath = "Assets/HS2Next/Resources/UI/HS2NextDark.tss";
    public const string StringsPath = "Assets/HS2Next/Resources/UI/L10n/strings.tsv";
    public const string GizmoShaderPath = "Assets/HS2Next/Shaders/UI/Gizmo.shader";
    public const string GizmoMaterialPath = "Assets/HS2Next/Resources/UI/HS2NextGizmo.mat";
    public static readonly string[] FontFiles = { "NotoSansJP-Regular.otf", "NotoSansJP-Bold.otf", "NotoSansSC-Regular.otf", "NotoSansSC-Bold.otf" };

    static string Arg(string name, string def)
    {
        var a = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : def;
    }

    static string Project => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

    public static void Build()
    {
        var sb = new StringBuilder();
        void Line(string s) { sb.AppendLine(s); Debug.Log("UI ASSETS " + s); }
        int code;
        try { code = Inner(Line); }
        catch (Exception e) { Line("EXCEPTION " + e); code = 1; }
        sb.Insert(0, "RESULT: " + (code == 0 ? "PASS" : "FAIL") + "\n");
        string report = Path.Combine(Project, "Spike", "out", "ui", "ui_assets.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(report));
        File.WriteAllText(report, sb.ToString(), new UTF8Encoding(false));
        if (Application.isBatchMode) EditorApplication.Exit(code);
    }

    static string Sha256(string path)
    {
        using (var s = File.OpenRead(path))
        using (var h = SHA256.Create()) return string.Concat(h.ComputeHash(s).Select(b => b.ToString("x2")));
    }

    static int Inner(Action<string> line)
    {
        line($"UiFontAssets {DateTime.Now:yyyy-MM-dd HH:mm:ss}, Unity {Application.unityVersion}");
        string src = Arg("-fontsDir", "");
        Directory.CreateDirectory(Path.Combine(Project, FontDir));
        Directory.CreateDirectory(Path.Combine(Project, AssetDir));
        string sourceMd = Path.Combine(Project, FontDir, "SOURCE.md");
        string expected = File.Exists(sourceMd) ? File.ReadAllText(sourceMd) : "";
        int fails = 0;
        foreach (var f in FontFiles.Concat(new[] { "OFL.txt" }))
        {
            string dst = Path.Combine(Project, FontDir, f);
            if (!File.Exists(dst))
            {
                string from = Path.Combine(src, f == "OFL.txt" ? "LICENSE" : f);
                if (src.Length == 0 || !File.Exists(from)) { line($"missing {f}: give -fontsDir <folder with the upstream files>"); fails++; continue; }
                File.Copy(from, dst);
                line($"copied {f} ({new FileInfo(dst).Length} bytes)");
            }
            string hash = Sha256(dst);
            bool ok = expected.Length == 0 || expected.Contains(hash);
            line($"{f}: sha256 {hash} {(ok ? "matches SOURCE.md" : "NOT IN SOURCE.md")}");
            if (!ok) fails++;
        }
        if (fails > 0) return 1;
        AssetDatabase.Refresh();
        bool rebuild = Environment.GetCommandLineArgs().Contains("-rebuildFonts");
        for (int i = 0; i < FontFiles.Length; i++)
        {
            string fp = FontDir + "/" + FontFiles[i];
            var imp = AssetImporter.GetAtPath(fp) as TrueTypeFontImporter;
            if (imp == null) { line("no font importer for " + fp); return 1; }
            if (imp.fontTextureCase != FontTextureCase.Dynamic || !imp.includeFontData)
            {
                imp.fontTextureCase = FontTextureCase.Dynamic; imp.includeFontData = true; imp.SaveAndReimport();
            }
            var font = AssetDatabase.LoadAssetAtPath<Font>(fp);
            if (!font) { line("font not loaded: " + fp); return 1; }
            string ap = AssetDir + "/" + Path.GetFileNameWithoutExtension(FontFiles[i]) + " SDF.asset";
            var existing = AssetDatabase.LoadAssetAtPath<FontAsset>(ap);
            if (existing && !rebuild) { line($"font asset {ap}: exists ({existing.atlasRenderMode}, {existing.atlasPopulationMode}, source {(existing.sourceFontFile ? existing.sourceFontFile.name : "none")})"); continue; }
            if (existing) AssetDatabase.DeleteAsset(ap);
            var fa = FontAsset.CreateFontAsset(font, 42, 4, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
            if (!fa) { line("CreateFontAsset failed for " + fp); return 1; }
            bool added = fa.TryAddCharacters("直骨写画あアAa", out string missing);
            line($"font asset {ap}: created; glyph test {added} (missing '{missing}'), face '{fa.faceInfo.familyName}' '{fa.faceInfo.styleName}'");
            fa.ClearFontAssetData(true);
            AssetDatabase.CreateAsset(fa, ap);
            foreach (var t in fa.atlasTextures) if (t) { t.name = fa.name + " Atlas"; AssetDatabase.AddObjectToAsset(t, fa); }
            if (fa.material) { fa.material.name = fa.name + " Material"; AssetDatabase.AddObjectToAsset(fa.material, fa); }
            EditorUtility.SetDirty(fa);
            AssetDatabase.SaveAssets();
        }

        // panel settings with ICU data and the theme
        var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
        if (!theme) { line("theme missing: " + ThemePath); return 1; }
        var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
        if (!ps)
        {
            ps = ScriptableObject.CreateInstance<PanelSettings>();
            AssetDatabase.CreateAsset(ps, PanelSettingsPath);
        }
        ps.themeStyleSheet = theme;
        ps.clearColor = false;
        ps.clearDepthStencil = true;
        ps.scaleMode = PanelScaleMode.ConstantPhysicalSize;
        ps.referenceDpi = 96;
        ps.fallbackDpi = 96;
        ps.sortingOrder = 0;
        var so = new SerializedObject(ps);
        var icu = so.FindProperty("m_ICUDataAsset");
        if (icu != null && !icu.objectReferenceValue)
        {
            icu.objectReferenceValue = AssetDatabase.GetBuiltinExtraResource<UnityEngine.TextAsset>("icudt73l.bytes");
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        EditorUtility.SetDirty(ps);
        AssetDatabase.SaveAssets();
        line($"panel settings {PanelSettingsPath}: theme {theme.name}, ICU {(icu != null && icu.objectReferenceValue ? icu.objectReferenceValue.name : "none")}");

        // the gizmo material (Resources, so the player ships the shader)
        var gs = AssetDatabase.LoadAssetAtPath<Shader>(GizmoShaderPath);
        if (!gs) { line("gizmo shader missing: " + GizmoShaderPath); return 1; }
        var gm = AssetDatabase.LoadAssetAtPath<Material>(GizmoMaterialPath);
        if (!gm) { gm = new Material(gs) { name = "HS2NextGizmo" }; AssetDatabase.CreateAsset(gm, GizmoMaterialPath); }
        else if (gm.shader != gs) gm.shader = gs;
        gm.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
        EditorUtility.SetDirty(gm);
        AssetDatabase.SaveAssets();
        line("gizmo material " + GizmoMaterialPath + ": shader " + gs.name);

        // strings.tsv as a TextAsset (UiTsvImporter)
        AssetDatabase.ImportAsset(StringsPath, ImportAssetOptions.ForceUpdate);
        var ta = AssetDatabase.LoadAssetAtPath<UnityEngine.TextAsset>(StringsPath);
        var importer = AssetImporter.GetAtPath(StringsPath);
        line($"strings.tsv: importer {(importer ? importer.GetType().Name : "none")}, TextAsset {(ta ? ta.text.Length + " chars" : "NOT LOADED")}");
        return ta ? 0 : 1;
    }
}
