// Maker list additions of Phase 4.3, plain
// Mono (Tools/mono_tests.ps1 -Suite maker):
//   synthetic rows: HairColourPreset reads the Base / Top / Under / Specular HSV columns and Metallic / Smoothness of 305 by
//   reference (unknown, hidden, unparsable and other-list rows refused); ColourPreset(305) keeps Base / Sample;
//   the install (skips without the fixture): the 7 rows of 305 = the census (names, columns, HSVToRGB within 1e-4, the
//   swatch Sample colours), no mod rows and no thumbnails; the 12 mesh patterns of 306 (row 0 «なし» MainAB "0", rows 1–11
//   mesh_t_NN with thumbnails, no mods); the style lists 300–303 for both sexes with the census row counts (the
//   none rows and the default rows); the hair categories offered to both sexes (the maker.list:<c> gate predicate) and the
//   list members of the hair paths (CategoryOfPath).
using System;
using System.Globalization;
using System.Linq;

namespace HS2Next.Content.Maker.Tests
{
    using HS2Next.Save.Tests;

    public static class MakerHairListTests
    {
        public static void Run(TestRunner t)
        {
            Synthetic(t);
            Categories(t);
            Install(t);
        }

        static string[] Cols(float[] b, float[] top, float[] u, float[] s, float m, float sm) => new[]
        {
            "BaseH", S(b[0]), "BaseS", S(b[1]), "BaseV", S(b[2]), "TopH", S(top[0]), "TopS", S(top[1]), "TopV", S(top[2]),
            "UnderH", S(u[0]), "UnderS", S(u[1]), "UnderV", S(u[2]), "SpecularH", S(s[0]), "SpecularS", S(s[1]), "SpecularV", S(s[2]),
            "Metallic", S(m), "Smoothness", S(sm), "SampleH", S(b[0]), "SampleS", S(b[1]), "SampleV", S(b[2]),
        };

        static string S(float v) => v.ToString("R", CultureInfo.InvariantCulture);

        static void Synthetic(TestRunner t)
        {
            var m = new MemRows();
            m.Add(305, null, 0, null, false, new[] { "Name", "a" }.Concat(Cols(new[] { 0f, 0f, 0.09f }, new[] { 0f, 0f, 0.113f }, new[] { 0f, 0f, 0.491f }, new[] { 0f, 0.02f, 0.56f }, 0.03f, 0f)).ToArray());
            m.Add(305, null, 2, null, false, new[] { "Name", "b", "Possess", "99" }.Concat(Cols(new[] { 0.1f, 0.1f, 0.1f }, new[] { 0f, 0f, 0f }, new[] { 0f, 0f, 0f }, new[] { 0f, 0f, 0f }, 0f, 0f)).ToArray());
            m.Add(305, null, 3, null, false, "Name", "no top", "BaseH", "0", "BaseS", "0", "BaseV", "0", "SampleH", "0", "SampleS", "0", "SampleV", "0");
            var badCols = new[] { "Name", "bad" }.Concat(Cols(new[] { 0f, 0f, 0f }, new[] { 0f, 0f, 0f }, new[] { 0f, 0f, 0f }, new[] { 0f, 0f, 0f }, 0f, 0f)).ToArray();
            badCols[Array.IndexOf(badCols, "Metallic") + 1] = "x";
            m.Add(305, null, 4, null, false, badCols);
            m.Add(336, null, 5, null, false, new[] { "Name", "skin" }.Concat(Cols(new[] { 0f, 0f, 0.5f }, new[] { 0f, 0f, 0f }, new[] { 0f, 0f, 0f }, new[] { 0f, 0f, 0f }, 0f, 0f)).ToArray());
            var L = new MakerLists(m);
            t.Check(L.HairColourPreset(ContentId.Vanilla(0), out var v) && v.Base.V == 0.09f && v.Top.V == 0.113f && v.Under.V == 0.491f && v.Specular.S == 0.02f && v.Specular.V == 0.56f &&
                    v.Metallic == 0.03f && v.Smoothness == 0f, "hair presets: row 0 of 305 by id = its Base / Top / Under / Specular HSV columns, Metallic, Smoothness");
            t.Check(!L.HairColourPreset(ContentId.Vanilla(1), out _), "hair presets: an unknown id → false");
            t.Check(!L.HairColourPreset(ContentId.Vanilla(2), out _), "hair presets: a hidden row (Possess 99) → false");
            t.Check(!L.HairColourPreset(ContentId.Vanilla(3), out _), "hair presets: a row without the Top / Under / Specular columns → false");
            t.Check(!L.HairColourPreset(ContentId.Vanilla(4), out _), "hair presets: an unparsable Metallic → false");
            t.Check(!L.HairColourPreset(ContentId.Vanilla(5), out _), "hair presets: a row of another list (336) → false (305 only)");
            t.Check(L.ColourPreset(305, ContentId.Vanilla(0), out var b, out var sw) && b.V == 0.09f && sw.V == 0.09f && L.ColourPreset(305, ContentId.Vanilla(3), out _, out _),
                "hair presets: ColourPreset(305) reads Base / Sample (the chips; 4.2's reader)");
            t.Check(MakerLists.HairColourCategory == 305 && v.ToString().StartsWith("HSV(0, 0, 0.09)", StringComparison.Ordinal), "hair presets: the category and the printed columns");
        }

        static void Categories(TestRunner t)
        {
            int bad = 0;
            foreach (int c in new[] { 300, 301, 302, 303, 305, 306 })
                if (!MakerLists.HasList(c, 0) || !MakerLists.HasList(c, 1)) { bad++; t.Check(false, "lists: " + c + " is offered to both sexes"); }
            t.Equal(0, bad, "lists: 300–303, 305, 306 are offered to both sexes (the maker.list:<c> predicate; no sex rule)");
            string[] parts = { "back", "front", "side", "option" };
            bool ok = true;
            for (int k = 0; k < 4; k++)
                foreach (int sex in new[] { 0, 1 })
                    ok &= MakerLists.CategoryOfPath("chara.hair." + parts[k] + ".item", sex) == 300 + k && MakerLists.CategoryOfPath("appearance.chara.hair." + parts[k] + ".mesh.item", sex) == 306;
            t.Check(ok && MakerLists.CategoryOfPath("chara.hair.back.baseColor", 1) == -1 && MakerLists.CategoryOfPath("chara.hair.back.bundles", 1) == -1,
                "lists: CategoryOfPath of the 8 hair list members per sex (style 300 + k, mesh 306); colours and bundles are not list members");
        }

        // name, Base, Top, Under, Specular (r, g, b), Metallic, Swatch (r, g, b)
        static readonly (string name, float[] b, float[] top, float[] u, float[] s, float m, float[] sw)[] Hair =
        {
            ("黒髪", new[] { 0.09f, 0.09f, 0.09f }, new[] { 0.113f, 0.113f, 0.113f }, new[] { 0.491f, 0.491f, 0.491f }, new[] { 0.56f, 0.5488f, 0.5488f }, 0.03f, new[] { 0.09f, 0.09f, 0.09f }),
            ("ダークブラウン", new[] { 0.294f, 0.2374f, 0.2234f }, new[] { 0.19f, 0.1541f, 0.1349f }, new[] { 0.604f, 0.5634f, 0.5388f }, new[] { 0.557f, 0.4647f, 0.4127f }, 0.314f, new[] { 0.294f, 0.2374f, 0.2234f }),
            ("茶髪", new[] { 0.311f, 0.2521f, 0.1863f }, new[] { 0.387f, 0.3233f, 0.2794f }, new[] { 0.708f, 0.6295f, 0.5374f }, new[] { 0.726f, 0.6268f, 0.5104f }, 0f, new[] { 0.376f, 0.3039f, 0.2271f }),
            ("赤毛", new[] { 0.585f, 0.4322f, 0.3393f }, new[] { 0.62f, 0.451f, 0.3534f }, new[] { 0.83f, 0.6722f, 0.5835f }, new[] { 0.962f, 0.7078f, 0.5493f }, 0.299f, new[] { 0.89f, 0.5402f, 0.3382f }),
            ("xブロンズ", new[] { 0.623f, 0.5532f, 0.4672f }, new[] { 0.594f, 0.5224f, 0.4342f }, new[] { 0.821f, 0.7211f, 0.6469f }, new[] { 0.764f, 0.6574f, 0.5516f }, 0.185f, new[] { 0.623f, 0.5532f, 0.4672f }),
            ("金髪", new[] { 0.75f, 0.684f, 0.585f }, new[] { 0.783f, 0.7674f, 0.7642f }, new[] { 0.925f, 0.8321f, 0.7631f }, new[] { 0.84f, 0.745f, 0.6552f }, 0f, new[] { 0.92f, 0.839f, 0.7176f }),
            ("白髪", new[] { 0.78f, 0.7673f, 0.7254f }, new[] { 0.78f, 0.7675f, 0.7215f }, new[] { 1.0f, 0.985f, 0.93f }, new[] { 0.75f, 0.7465f, 0.72f }, 0f, new[] { 0.92f, 0.9062f, 0.8556f }),
        };

        // category → census rows (vanilla + mod), none row, row 0 name, default rows (female / male) with names
        static readonly (int cat, int vanilla, int mods, string row0, int defF, string defFName, int defM, string defMName)[] Census =
        {
            (300, 49, 943, "ストレートロング", 0, "ストレートロング", 0, "ストレートロング"),
            (301, 39, 302, "なし", 1, "ロングヘア", 2, "くせっ毛ショート"),
            (302, 8, 126, "なし", 0, "なし", 0, "なし"),
            (303, 8, 139, "なし", 0, "なし", 0, "なし"),
        };

        static void Install(TestRunner t)
        {
            var rows = InstallFixture.Get(t);
            if (rows == null) { t.Info("SKIP: " + InstallFixture.Reason); return; }
            if (!rows.HasVanilla) { t.Info("SKIP: no vanilla fixture Cache/maker/vanilla_lists.tsv"); return; }
            var L = new MakerLists(rows) { ShowGated = true };
            // hair colour presets
            int ok = 0;
            for (int i = 0; i < Hair.Length; i++)
            {
                var h = Hair[i];
                var row = rows.Find(305, ContentId.Vanilla(i));
                HsvColour bv = default, sv = default;
                bool cols = L.HairColourPreset(ContentId.Vanilla(i), out var c) && L.ColourPreset(305, ContentId.Vanilla(i), out bv, out sv);
                bool rgb = cols && Near(HsvToRgb(c.Base), h.b) && Near(HsvToRgb(c.Top), h.top) && Near(HsvToRgb(c.Under), h.u) && Near(HsvToRgb(c.Specular), h.s) &&
                           c.Metallic == h.m && c.Smoothness == 0f && Near(HsvToRgb(sv), h.sw) && bv.H == c.Base.H && bv.S == c.Base.S && bv.V == c.Base.V;
                if (rgb && row?["Name"] == h.name) ok++;
                else t.Check(false, "305: row " + i + " " + row?["Name"] + " " + c);
            }
            t.Equal(7, ok, "305: the 7 hair colour presets as listed (names, the four colours and the swatch within 1e-4, metallic exact, smoothness 0)");
            var l305 = L.Rows(305, 1);
            t.Check(l305.Count == 7 && l305.All(r => r.IsVanilla) && l305.Select(r => r.Key.Id).SequenceEqual(Enumerable.Range(0, 7)) && L.Rows(305, 0).Count == 7 &&
                    l305.All(r => string.IsNullOrEmpty(r["ThumbAB"]) && string.IsNullOrEmpty(r["ThumbTex"])),
                "305: 7 vanilla rows (ids 0..6) for both sexes, no mod rows, no thumbnails");
            // mesh patterns
            var l306 = L.Rows(306, 1);
            bool none = l306.Count > 0 && l306[0].Key.Id == 0 && l306[0]["MainAB"] == "0" && l306[0]["MainTex"] == "0" && l306[0]["Name"] == "なし";
            bool types = l306.Skip(1).Select((r, i) => r.Key.Id == i + 1 && r["MainAB"] == "chara/30/st_hairmeshptn_30.unity3d" && r["MainTex"] == "mesh_t_" + (i + 1).ToString("00") &&
                r["ThumbTex"] == "thumb_mesh_t_" + (i + 1).ToString("00") && r["Name"] == "タイプ" + (i + 1).ToString("00")).All(x => x);
            t.Check(l306.Count == 12 && l306.All(r => r.IsVanilla) && none && types && L.Rows(306, 0).Count == 12,
                "306: 12 vanilla rows for both sexes — row 0 «なし» (MainAB / MainTex \"0\" → a null mask), rows 1–11 «タイプ01»–«タイプ11» mesh_t_NN with thumbnails, no mods");
            // the style lists per sex
            foreach (var c in Census)
            {
                foreach (int sex in new[] { 1, 0 })
                {
                    var l = L.Rows(c.cat, sex);
                    int v = l.Count(r => r.IsVanilla), mo = l.Count - v;
                    t.Check(v == c.vanilla && mo == c.mods, "lists: " + c.cat + " (" + (sex == 0 ? "male" : "female") + ") " + v + " vanilla + " + mo + " mod rows (" + c.vanilla + " + " + c.mods + ")");
                }
                var row0 = L.Rows(c.cat, 1)[0];
                var df = rows.Find(c.cat, ContentId.Vanilla(c.defF));
                var dm = rows.Find(c.cat, ContentId.Vanilla(c.defM));
                t.Check(row0.Key == ContentId.Vanilla(0) && row0["Name"] == c.row0 && df?["Name"] == c.defFName && dm?["Name"] == c.defMName,
                    "lists: " + c.cat + " row 0 «" + row0["Name"] + "», default rows female #" + c.defF + " «" + df?["Name"] + "» / male #" + c.defM + " «" + dm?["Name"] + "»");
            }
            // the three vanilla back rows with a male prefab (MainData02) and the 3 RingOff rows
            var back = L.Rows(300, 1).Where(r => r.IsVanilla).ToList();
            var male = back.Where(r => !string.IsNullOrEmpty(r["MainData02"]) && r["MainData02"] != "0").Select(r => r.Key.Id).OrderBy(x => x).ToList();
            t.Check(male.SequenceEqual(new[] { 22, 51, 59 }), "lists: the vanilla back rows with MainData02 are #22, #51, #59 (" + string.Join(", ", male) + ")");
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
