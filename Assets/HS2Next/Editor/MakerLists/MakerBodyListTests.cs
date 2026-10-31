// Maker list additions of Phase 4.2,
// plain Mono (Tools/mono_tests.ps1 -Suite maker):
//   synthetic rows: ColourPreset reads (BaseH, BaseS, BaseV) and (SampleH, SampleS, SampleV) by reference (unknown, hidden and
//   unparsable rows refused);
//   the install (skips without the fixture): the 7 rows of 336 = the census values (HSV columns exactly, the
//   HSVToRGB value and swatch colours within 1e-4), no mod rows and no thumbnails in 336; the paint regions of 8 (20 vanilla
//   rows + the mod row 20) with their columns as listed; every body list present for the right sex with the census row counts
//   (the maker.list:<c> gate predicate MakerLists.HasList).
using System;
using System.Linq;

namespace HS2Next.Content.Maker.Tests
{
    using HS2Next.Save.Tests;

    public static class MakerBodyListTests
    {
        public static void Run(TestRunner t)
        {
            Synthetic(t);
            Categories(t);
            Install(t);
        }

        static void Synthetic(TestRunner t)
        {
            var m = new MemRows();
            m.Add(336, null, 0, null, false, "Name", "a", "SampleH", "0.063", "SampleS", "0.14", "SampleV", "0.88", "BaseH", "0.063", "BaseS", "0.2", "BaseV", "0.78");
            m.Add(336, null, 3, null, false, "Name", "b", "SampleH", "0.058", "SampleS", "0.26", "SampleV", "0.7", "BaseH", "0.058", "BaseS", "0.21", "BaseV", "0.7");
            m.Add(336, null, 4, null, false, "Name", "hidden", "Possess", "99", "SampleH", "0", "SampleS", "0", "SampleV", "0", "BaseH", "0", "BaseS", "0", "BaseV", "0");
            m.Add(336, null, 5, null, false, "Name", "bad", "SampleH", "0", "SampleS", "0", "SampleV", "0", "BaseH", "x", "BaseS", "0", "BaseV", "0");
            m.Add(336, null, 6, null, false, "Name", "no sample", "BaseH", "0.1", "BaseS", "0.1", "BaseV", "0.1");
            m.Add(305, null, 0, null, false, "Name", "hair", "SampleH", "0", "SampleS", "0", "SampleV", "0.09", "BaseH", "0", "BaseS", "0", "BaseV", "0.09", "TopH", "0");
            var L = new MakerLists(m);
            t.Check(L.ColourPreset(336, ContentId.Vanilla(3), out var v, out var s) && v.H == 0.058f && v.S == 0.21f && v.V == 0.7f && s.H == 0.058f && s.S == 0.26f && s.V == 0.7f,
                "colour: row 3 of 336 by id = Base (0.058, 0.21, 0.7), Sample (0.058, 0.26, 0.7)");
            t.Check(!L.ColourPreset(336, ContentId.Vanilla(1), out _, out _), "colour: an unknown id → false (by id, not position)");
            t.Check(!L.ColourPreset(336, ContentId.Vanilla(4), out _, out _), "colour: a hidden row (Possess 99) → false");
            t.Check(!L.ColourPreset(336, ContentId.Vanilla(5), out _, out _), "colour: an unparsable column → false");
            t.Check(!L.ColourPreset(336, ContentId.Vanilla(6), out _, out _), "colour: a row without the Sample columns → false");
            t.Check(L.ColourPreset(305, ContentId.Vanilla(0), out var hv, out _) && hv.V == 0.09f, "colour: the hair presets (305) have the same Base / Sample columns (4.3)");
            t.Check(!L.ColourPreset(305, ContentId.Vanilla(3), out _, out _), "colour: a row of the other list → false");
            t.Check(L.ColourPreset(336, ContentId.Vanilla(0), out var c0, out _) && c0.ToString().StartsWith("HSV(0.063", StringComparison.Ordinal), "colour: HsvColour prints its values");
        }

        // (female category, male category) of the body lists and the categories both sexes share
        static readonly (int f, int m)[] PerSex = { (231, 131), (232, 132), (233, 133) };
        static readonly int[] Shared = { 334, 335, 313, 8, 336 };

        static void Categories(TestRunner t)
        {
            int bad = 0;
            foreach (var (f, m) in PerSex)
            {
                if (!MakerLists.HasList(f, 1) || MakerLists.HasList(f, 0)) { bad++; t.Check(false, "lists: " + f + " is female only"); }
                if (!MakerLists.HasList(m, 0) || MakerLists.HasList(m, 1)) { bad++; t.Check(false, "lists: " + m + " is male only"); }
            }
            foreach (int c in Shared)
                if (!MakerLists.HasList(c, 0) || !MakerLists.HasList(c, 1)) { bad++; t.Check(false, "lists: " + c + " is offered to both sexes"); }
            t.Equal(0, bad, "lists: the body categories per sex (the maker.list:<c> predicate): skin / detail / sunburn 231 / 131, 232 / 132, 233 / 133; shared 334, 335, 313, 8, 336");
            var paths = new (string path, int f, int m)[]
            {
                ("chara.body.skin.item", 231, 131), ("chara.body.detail.item", 232, 132), ("chara.body.sunburn.item", 233, 133), ("chara.body.nipple.item", 334, 334),
                ("chara.body.underhair.item", 335, 335), ("chara.body.paint[0].item", 313, 313), ("chara.body.paint[1].layoutItem", 8, 8),
            };
            t.Check(paths.All(p => MakerLists.CategoryOfPath(p.path, 1) == p.f && MakerLists.CategoryOfPath("appearance." + p.path, 0) == p.m),
                "lists: CategoryOfPath of the 7 body list members per sex");
            t.Equal(-1, MakerLists.CategoryOfPath("chara.body.skin.color", 1), "lists: the skin colour is not a list member (the swatches push a colour)");
        }

        // id, Base (H, S, V), Sample (H, S, V), Value (r, g, b), Swatch (r, g, b)
        static readonly (string name, float[] b, float[] s, float[] value, float[] swatch)[] Skin =
        {
            ("ライト", new[] { 0.063f, 0.2f, 0.78f }, new[] { 0.063f, 0.14f, 0.88f }, new[] { 0.78f, 0.683f, 0.624f }, new[] { 0.88f, 0.8034f, 0.7568f }),
            ("ベージュ", new[] { 0.066f, 0.2f, 0.74f }, new[] { 0.066f, 0.23f, 0.81f }, new[] { 0.74f, 0.6506f, 0.592f }, new[] { 0.81f, 0.6975f, 0.6237f }),
            ("ピンク", new[] { 0.053f, 0.21f, 0.75f }, new[] { 0.053f, 0.27f, 0.8f }, new[] { 0.75f, 0.6426f, 0.5925f }, new[] { 0.8f, 0.6527f, 0.584f }),
            ("タン", new[] { 0.058f, 0.21f, 0.7f }, new[] { 0.058f, 0.26f, 0.7f }, new[] { 0.7f, 0.6042f, 0.553f }, new[] { 0.7f, 0.5813f, 0.518f }),
            ("ブラウン", new[] { 0.061f, 0.21f, 0.65f }, new[] { 0.061f, 0.24f, 0.6f }, new[] { 0.65f, 0.5635f, 0.5135f }, new[] { 0.6f, 0.5087f, 0.456f }),
            ("ダーク", new[] { 0.061f, 0.21f, 0.6f }, new[] { 0.061f, 0.23f, 0.5f }, new[] { 0.6f, 0.5201f, 0.474f }, new[] { 0.5f, 0.4271f, 0.385f }),
            ("ブルー", new[] { 0.055f, 0.16f, 0.74f }, new[] { 0.055f, 0.06f, 0.85f }, new[] { 0.74f, 0.6607f, 0.6216f }, new[] { 0.85f, 0.8158f, 0.799f }),
        };

        // id → (CenterX, MoveX, CenterY, MoveY, CenterScale, AddScale)
        static readonly float[][] Regions =
        {
            new[] { 0.375f, 0.125f, -0.375f, 0.125f, 13f, 10f }, new[] { 0.375f, 0.125f, -0.23f, 0.05f, 13f, 10f }, new[] { 0.125f, 0.125f, -0.375f, 0.125f, 13f, 10f },
            new[] { 0.125f, 0.125f, -0.23f, 0.05f, 13f, 10f },
            new[] { 0.06f, 0.06f, 0f, 0.08f, 13f, 10f }, new[] { 0.06f, 0.06f, 0.2f, 0.1f, 15f, 10f }, new[] { 0.18f, 0.06f, 0f, 0.08f, 13f, 10f }, new[] { 0.18f, 0.06f, 0.2f, 0.1f, 15f, 10f },
            new[] { 0.44f, 0.06f, 0f, 0.08f, 13f, 10f }, new[] { 0.31f, 0.06f, 0f, 0.08f, 13f, 10f }, new[] { 0.44f, 0.06f, 0.2f, 0.1f, 15f, 10f }, new[] { 0.31f, 0.06f, 0.2f, 0.1f, 15f, 10f },
            new[] { 0.45f, 0.05f, 0.38f, 0.05f, 12f, 10f }, new[] { 0.3f, 0.05f, 0.38f, 0.05f, 12f, 10f },
            new[] { 0.18f, 0.06f, -0.12f, 0.06f, 20f, 10f }, new[] { 0.06f, 0.06f, -0.12f, 0.06f, 20f, 10f },
            new[] { -0.08f, 0.08f, 0.41f, 0.06f, 20f, 12f }, new[] { -0.3f, 0.1f, 0.4f, 0.06f, 20f, 12f }, new[] { -0.41f, 0.08f, 0.25f, 0.06f, 20f, 12f }, new[] { -0.2f, 0.1f, 0.24f, 0.06f, 20f, 12f },
        };
        static readonly string[] RegionColumns = { "CenterX", "MoveX", "CenterY", "MoveY", "CenterScale", "AddScale" };
        static readonly ContentId ModRegion = new ContentId("com.toshiaki.bodypaint_layout_abdomen", 20);
        static readonly float[] ModRegionValues = { 0.374f, 0.125f, -0.1f, 0.05f, 13f, 10f };

        // category → census rows (vanilla + mod)
        static readonly (int cat, int sex, int vanilla, int mods)[] Census =
        {
            (231, 1, 4, 46), (131, 0, 5, 10), (232, 1, 5, 62), (132, 0, 5, 10), (233, 1, 15, 67), (133, 0, 8, 20),
            (334, 1, 5, 91), (335, 1, 15, 154), (313, 1, 49, 1367), (8, 1, 20, 1), (336, 1, 7, 0),
        };

        static void Install(TestRunner t)
        {
            var rows = InstallFixture.Get(t);
            if (rows == null) { t.Info("SKIP: " + InstallFixture.Reason); return; }
            if (!rows.HasVanilla) { t.Info("SKIP: no vanilla fixture Cache/maker/vanilla_lists.tsv"); return; }
            var L = new MakerLists(rows) { ShowGated = true };
            // skin colour presets
            int ok = 0;
            for (int i = 0; i < Skin.Length; i++)
            {
                var (name, b, s, value, swatch) = Skin[i];
                var row = rows.Find(336, ContentId.Vanilla(i));
                bool cols = L.ColourPreset(336, ContentId.Vanilla(i), out var bv, out var sv) && bv.H == b[0] && bv.S == b[1] && bv.V == b[2] && sv.H == s[0] && sv.S == s[1] && sv.V == s[2];
                bool rgb = Near(HsvToRgb(bv), value) && Near(HsvToRgb(sv), swatch);
                if (cols && rgb && row?["Name"] == name) ok++;
                else t.Check(false, "336: row " + i + " " + row?["Name"] + " " + bv + " " + sv);
            }
            t.Equal(7, ok, "336: the 7 skin colour presets as listed (names, HSV columns, the value and swatch colours within 1e-4)");
            var l336 = L.Rows(336, 1);
            t.Check(l336.Count == 7 && l336.All(r => r.IsVanilla) && l336.Select(r => r.Key.Id).SequenceEqual(Enumerable.Range(0, 7)) && L.Rows(336, 0).Count == 7,
                "336: 7 vanilla rows (ids 0..6) for both sexes, no mod rows");
            t.Check(l336.All(r => string.IsNullOrEmpty(r["ThumbAB"]) && string.IsNullOrEmpty(r["ThumbTex"])), "336: the rows carry no thumbnail (the swatches are colours)");
            // paint regions
            int rok = 0;
            for (int i = 0; i < Regions.Length; i++)
                if (Columns(rows.Find(8, ContentId.Vanilla(i)), Regions[i])) rok++;
                else t.Check(false, "8: region " + i + " columns");
            bool mod = Columns(rows.Find(8, ModRegion), ModRegionValues);
            t.Check(rok == 20 && mod, "8: the 20 vanilla regions and the mod region " + ModRegion + " (下腹部) have the columns (" + rok + " / 20, mod " + mod + ")");
            var l8 = L.Rows(8, 0);
            t.Check(l8.Count == 21 && L.Rows(8, 1).Count == 21 && l8.Last().Key == ModRegion && l8.Take(20).All(r => r.IsVanilla) && l8.All(r => !string.IsNullOrEmpty(r["ThumbTex"])),
                "8: 21 regions for both sexes (vanilla first, then the mod row), every row with a thumbnail");
            // the body lists per sex with the census row counts
            foreach (var (cat, sex, vanilla, mods) in Census)
            {
                var l = L.Rows(cat, sex);
                int v = l.Count(r => r.IsVanilla), mo = l.Count - v;
                t.Check(v == vanilla && mo == mods, "lists: " + cat + " (" + (sex == 0 ? "male" : "female") + ") " + v + " vanilla + " + mo + " mod rows (" + vanilla + " + " + mods + ")");
                if (sex == 1 && Shared.Contains(cat)) t.Equal(l.Count, L.Rows(cat, 0).Count, "lists: " + cat + " is the same list for males");
            }
            t.Check(L.Rows(231, 0).Count == 0 && L.Rows(131, 1).Count == 0, "lists: no list of the other sex");
        }

        static bool Columns(ListRow r, float[] e)
        {
            if (r == null) return false;
            for (int c = 0; c < RegionColumns.Length; c++)
                if (!float.TryParse(r[RegionColumns[c]], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x) || x != e[c]) return false;
            return true;
        }

        static bool Near(float[] c, float[] e) => Math.Abs(c[0] - e[0]) <= 1e-4f && Math.Abs(c[1] - e[1]) <= 1e-4f && Math.Abs(c[2] - e[2]) <= 1e-4f;

        // Unity's Color.HSVToRGB (the product path is MakerEdits.HsvToRgb in the scene layer; this assembly has no access to it)
        static float[] HsvToRgb(HsvColour c)
        {
            float h = c.H, s = c.S, v = c.V;
            if (s == 0f) return new[] { v, v, v };
            if (v == 0f) return new[] { 0f, 0f, 0f };
            float h6 = h * 6f;
            int sector = (int)Math.Floor(h6);
            float f = h6 - sector, p = v * (1f - s), q = v * (1f - s * f), u = v * (1f - s * (1f - f));
            switch (sector)
            {
                case 0: case 6: return new[] { v, u, p };
                case 1: return new[] { q, v, p };
                case 2: return new[] { p, v, u };
                case 3: return new[] { p, q, v };
                case 4: return new[] { u, p, v };
                case 5: case -1: return new[] { v, p, q };
            }
            return new[] { v, v, v };
        }
    }
}
