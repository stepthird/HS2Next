// Maker list facts of Phase 4.4 (the clothes page), plain Mono (Tools/mono_tests.ps1 -Suite maker):
//   categories: the clothes lists 240–247 are offered to females, 140 / 141 / 144 / 147 to males, the pattern list 348 to
//   both (the maker.list:<c> predicate); MakerLists.ClothesCategory per kind and sex (no male list for the inner, pantyhose
//   and socks kinds); CategoryOfPath of the item and pattern members of the eight kinds per sex;
//   the install (skips without the fixtures): per list the vanilla and mod rows, the none rows (MainData p_dummy) and row 0
//   «なし» = the clothes census (vanilla rows with the gates open and Possess 99 hidden, mod rows of the catalog's zipmod per
//   GUID), the one-piece rows of the vanilla examples (Coordinate 1) and the pattern rows' textures.
using System;
using System.Globalization;
using System.Linq;

namespace HS2Next.Content.Maker.Tests
{
    using HS2Next.Save.Tests;

    public static class MakerClothesListTests
    {
        public static void Run(TestRunner t)
        {
            Categories(t);
            Install(t);
        }

        static readonly string[] Kinds = { "top", "bottom", "innerTop", "innerBottom", "gloves", "pantyhose", "socks", "shoes" };

        static void Categories(TestRunner t)
        {
            int bad = 0;
            for (int c = 240; c <= 247; c++) if (!MakerLists.HasList(c, 1) || MakerLists.HasList(c, 0)) { bad++; t.Check(false, "lists: " + c + " is female-only"); }
            foreach (int c in new[] { 140, 141, 144, 147 }) if (!MakerLists.HasList(c, 0) || MakerLists.HasList(c, 1)) { bad++; t.Check(false, "lists: " + c + " is male-only"); }
            if (!MakerLists.HasList(348, 0) || !MakerLists.HasList(348, 1)) { bad++; t.Check(false, "lists: 348 is offered to both sexes"); }
            t.Equal(0, bad, "lists: 240–247 female, 140 / 141 / 144 / 147 male, 348 both (the maker.list:<c> predicate)");
            var male = Enumerable.Range(0, 8).Select(k => MakerLists.ClothesCategory(k, 0)).ToArray();
            var female = Enumerable.Range(0, 8).Select(k => MakerLists.ClothesCategory(k, 1)).ToArray();
            t.Check(male.SequenceEqual(new[] { 140, 141, -1, -1, 144, -1, -1, 147 }) && female.SequenceEqual(Enumerable.Range(240, 8)) && MakerLists.ClothesCategory(8, 1) == -1,
                "lists: ClothesCategory per kind — female 240–247, male 140 / 141 / – / – / 144 / – / – / 147");
            bool ok = true;
            for (int k = 0; k < 8; k++)
                foreach (int sex in new[] { 0, 1 })
                {
                    int cat = sex == 0 ? male[k] : female[k];
                    ok &= MakerLists.CategoryOfPath("coord.clothes." + Kinds[k] + ".item", sex) == cat && MakerLists.CategoryOfPath("appearance.coord.clothes." + Kinds[k] + ".item", sex) == cat;
                    for (int j = 0; j < 3; j++) ok &= MakerLists.CategoryOfPath("coord.clothes." + Kinds[k] + ".colors[" + j + "].pattern", sex) == (cat < 0 ? -1 : 348);
                }
            t.Check(ok && MakerLists.CategoryOfPath("coord.clothes.top.colors[0].base", 1) == -1 && MakerLists.CategoryOfPath("coord.clothes.top.breakRate", 1) == -1,
                "lists: CategoryOfPath of the 8 item and 24 pattern members per sex (none for the male kinds without a list); other members are not list members");
        }

        // the census of the clothes lists: category, sex, vanilla rows, mod rows, none rows (MainData p_dummy)
        static readonly (int cat, int sex, int vanilla, int mods, int none)[] Census =
        {
            (240, 1, 102, 808, 1), (241, 1, 50, 467, 1), (242, 1, 55, 425, 1), (243, 1, 37, 268, 1),
            (244, 1, 30, 217, 1), (245, 1, 25, 272, 3), (246, 1, 30, 177, 2), (247, 1, 50, 412, 1),
            (140, 0, 13, 132, 1), (141, 0, 11, 89, 2), (144, 0, 4, 35, 1), (147, 0, 12, 76, 1),
            (348, 1, 61, 350, 0), (348, 0, 61, 350, 0),
        };

        static void Install(TestRunner t)
        {
            var rows = InstallFixture.Get(t);
            if (rows == null) { t.Info("SKIP: " + InstallFixture.Reason); return; }
            if (!rows.HasVanilla) { t.Info("SKIP: no vanilla fixture Cache/maker/vanilla_lists.tsv"); return; }
            var L = new MakerLists(rows) { ShowGated = true };
            foreach (var c in Census)
            {
                var l = L.Rows(c.cat, c.sex);
                int v = l.Count(r => r.IsVanilla), mo = l.Count - v, none = l.Count(r => r["MainData"] == "p_dummy");
                var row0 = l.Count > 0 ? l[0] : null;
                t.Check(v == c.vanilla && mo == c.mods && none == c.none && row0 != null && row0.Key == ContentId.Vanilla(0) && row0["Name"] == "なし",
                    "lists: " + c.cat + " (" + (c.sex == 0 ? "male" : "female") + ") " + v + " vanilla + " + mo + " mod rows, " + none + " none rows, row 0 «" + row0?["Name"] + "» (census: " +
                    c.vanilla + " + " + c.mods + ", " + c.none + ")");
            }
            // the vanilla one-piece examples (Coordinate 1) and a two-piece one
            Func<int, int, string> col = (cat, id) => rows.Find(cat, ContentId.Vanilla(id))?["Coordinate"];
            t.Check(col(240, 1) == "1" && col(242, 15) == "1" && col(240, 2) == "0" && rows.Find(240, ContentId.Vanilla(1))?["Name"] == "ワンピース",
                "lists: 240#1 «ワンピース» and 242#15 are one-pieces (Coordinate 1), 240#2 is not");
            int oneTop = L.Rows(240, 1).Count(r => r["Coordinate"] == "1"), oneInner = L.Rows(242, 1).Count(r => r["Coordinate"] == "1");
            t.Check(oneTop == 92 && oneInner == 72, "lists: one-piece rows 240: " + oneTop + " (census 92), 242: " + oneInner + " (census 72)");
            // the pattern list: row 0 has no texture, the vanilla rows 1..60 name one
            var pat = L.Rows(348, 1).Where(r => r.IsVanilla).ToList();
            bool tex = pat.Skip(1).All(r => !string.IsNullOrEmpty(r["MainTex"]) && r["MainTex"] != "0");
            t.Check(pat.Count == 61 && tex && (string.IsNullOrEmpty(pat[0]["MainTex"]) || pat[0]["MainTex"] == "0"),
                "lists: 348's vanilla rows: row 0 without a texture, rows 1–60 with one (MainTex)");
        }
    }
}
