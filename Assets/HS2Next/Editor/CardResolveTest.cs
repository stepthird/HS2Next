// Batch test: read cards, build the list DB (vanilla + all zipmods) and resolve every part.
// Run: -executeMethod CardResolveTest.Run [-cards <dir>] [-limit N]
// Paths come from Config/settings.local.json; overrides: -gameRoot <dir> -modsFolder <dir> -cacheDir <dir> -settings <file>.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.Content;
using HS2Next.Data;
using HS2Next.IO;
using UnityEngine;
using Debug = UnityEngine.Debug;

public static class CardResolveTest
{
    /// <summary>Settings for batch tests (settings file + command-line overrides), read on each access.</summary>
    public static ContentSettings Settings => ContentSettings.LoadDefault(TestArgs.Get());
    public static string GameRoot => ContentSettings.Resolve(Settings.hs2Root);
    public static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "chara");

    // Category numbers (from the list files' categoryNo).
    public static readonly (string prop, int cat, string label)[] ClothesSlots =
    {
        ("outfit.ChaFileClothes.ClothesTop", 240, "top"), ("outfit.ChaFileClothes.ClothesBot", 241, "bot"),
        ("outfit.ChaFileClothes.ClothesInnerT", 242, "inner_t"), ("outfit.ChaFileClothes.ClothesInnerB", 243, "inner_b"),
        ("outfit.ChaFileClothes.ClothesGloves", 244, "gloves"), ("outfit.ChaFileClothes.ClothesPants", 245, "panst"),
        ("outfit.ChaFileClothes.ClothesSocks", 246, "socks"), ("outfit.ChaFileClothes.ClothesShoes", 247, "shoes"),
    };

    static string Arg(string name, string def)
    {
        var a = TestArgs.Get();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : def;
    }

    /// <summary>The list DB of a test (vanilla + all zipmods); inside a HeadlessBatch the batch's warm one (TestCatalog).</summary>
    public static ContentCatalog BuildDb(StringBuilder sb) => TestCatalog.Get(sb, BuildDbFresh);

    static ContentCatalog BuildDbFresh(StringBuilder sb)
    {
        var sw = Stopwatch.StartNew();
        var gd = ContentCatalog.FromSettings(Settings);
        gd.LoadVanillaLists();
        long t1 = sw.ElapsedMilliseconds;
        gd.LoadModLists();
        sb?.AppendLine($"lists: vanilla {t1} ms, mods {sw.ElapsedMilliseconds - t1} ms; {string.Join("; ", gd.Log.Take(3))}");
        return gd;
    }

    public static void Run()
    {
        string dir = Arg("-cards", Path.Combine(GameRoot, @"UserData\chara\female"));
        int limit = int.Parse(Arg("-limit", "30"));
        var sb = new StringBuilder();
        var gd = BuildDb(sb);
        int ok = 0, miss = 0, cards = 0;
        foreach (var f in Directory.GetFiles(dir, "*.png").Take(limit))
        {
            Card c;
            try { c = Card.Load(f); } catch (Exception e) { sb.AppendLine($"CARD FAIL {Path.GetFileName(f)}: {e.Message}"); continue; }
            cards++;
            sb.AppendLine($"== {c.Name}  ({Path.GetFileName(f)})  resolve entries {c.Resolve.Count}");
            void One(string label, int cat, int id, string prop)
            {
                var e = gd.Resolve(c, cat, id, prop);
                if (e == null) { miss++; sb.AppendLine($"   {label,-8} cat {cat} id {id}  -> MISSING"); return; }
                ok++;
                sb.AppendLine($"   {label,-8} cat {cat} id {id}  -> {(e.IsVanilla ? "vanilla" : e.Key.SourceGuid)} : {e["MainAB"]} / {e["MainData"] ?? e["MainTex"]}");
            }
            One("head", 210, c.Face.Get("headId").Int(), "ChaFileFace.headId");
            One("faceskin", 211, c.Face.Get("skinId").Int(), "ChaFileFace.skinId");
            One("bodyskin", 231, c.Body.Get("skinId").Int(), "ChaFileBody.skinId");
            var hairCats = new[] { 300, 301, 302, 303 };
            var hairNames = new[] { "ChaFileHair.HairBack", "ChaFileHair.HairFront", "ChaFileHair.HairSide", "ChaFileHair.HairOption" };
            for (int i = 0; i < 4; i++) One("hair" + i, hairCats[i], c.Hair.Get("parts").At(i).Get("id").Int(), hairNames[i]);
            for (int i = 0; i < 8; i++)
            {
                var (prop, cat, label) = ClothesSlots[i];
                One(label, cat, c.Clothes.Get("parts").At(i).Get("id").Int(), prop);
            }
            int ai = 0;
            foreach (var a in c.Accessory.Get("parts").Items())
            {
                int type = a.Get("type").Int();
                if (type != 350) One("acc" + ai, type, a.Get("id").Int(), $"outfit.accessory{ai}.ChaFileAccessory.PartsInfo.id");
                ai++;
            }
        }
        sb.Insert(0, $"cards {cards}, resolved {ok}, missing {miss}\n");
        foreach (var l in gd.Log.Where(l => l.StartsWith("missing"))) sb.AppendLine(l);
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(Path.Combine(OutDir, "resolve.txt"), sb.ToString());
        gd.UnloadAll();
        Debug.Log("CARD TEST DONE");
    }
}
