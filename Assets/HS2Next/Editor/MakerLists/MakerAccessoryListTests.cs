// Maker list facts of Phase 4.5 (the accessory pages), plain Mono (Tools/mono_tests.ps1 -Suite maker):
//   categories: the accessory lists 350..363 are offered to both sexes (the maker.list:<c> predicate); CategoryOfPath of an
//   accessory item takes the accessory's category;
//   the install (skips without the fixtures): per list 351..363 the vanilla and mod rows = the accessory census (vanilla rows
//   with the gates open and Possess 99 hidden, mod rows of the zipmod set), row 0 of each category (the item a type change
//   picks) with its name and `Parent`, and the classes of the `Parent` column (one of the 54 attach points / "0" / "null" /
//   empty / another text) per category; 350 is the one row with "0".
using System;
using System.Collections.Generic;
using System.Linq;

namespace HS2Next.Content.Maker.Tests
{
    using HS2Next.Save;
    using HS2Next.Save.Tests;

    public static class MakerAccessoryListTests
    {
        public static void Run(TestRunner t)
        {
            Categories(t);
            Install(t);
        }

        static void Categories(TestRunner t)
        {
            int bad = 0;
            for (int c = 350; c <= 363; c++) if (!MakerLists.HasList(c, 0) || !MakerLists.HasList(c, 1)) { bad++; t.Check(false, "lists: " + c + " is offered to both sexes"); }
            t.Equal(0, bad, "lists: 350..363 are offered to both sexes (the maker.list:<c> predicate)");
            t.Check(MakerLists.CategoryOfPath("coord.accessories[\"a\"].item", 1, 355) == 355 && MakerLists.CategoryOfPath("appearance.coord.accessories[\"a\"].item", 0, 361) == 361 &&
                    MakerLists.CategoryOfPath("coord.accessories[\"a\"].item", 1, 120) == -1 && MakerLists.CategoryOfPath("coord.accessories[\"a\"].parent", 1, 355) == -1,
                "lists: an accessory item's list is the accessory's own category (350..363); other members are not list members");
        }

        // the accessory census: category, vanilla rows, mod rows,
        // Parent classes (key / "0" / "null" / empty / other), row 0's name and Parent
        static readonly (int cat, int vanilla, int mods, int key, int zero, int nul, int empty, int other, string row0, string parent0)[] Census =
        {
            (351, 151, 824, 971, 0, 3, 0, 1, "ヘアピン", "N_Hair_pin_L"),
            (352, 9, 77, 86, 0, 0, 0, 0, "スター", "N_Earring_L"),
            (353, 23, 44, 67, 0, 0, 0, 0, "スクエア", "N_Megane"),
            (354, 15, 103, 116, 0, 1, 0, 1, "ピエロの鼻", "N_Nose"),
            (355, 55, 251, 304, 0, 0, 0, 2, "ハートネックレス", "N_Chest"),
            (356, 18, 24, 41, 0, 0, 0, 1, "トートバッグ", "N_Shoulder_L"),
            (357, 8, 43, 51, 0, 0, 0, 0, "シンプルリング", "N_Tikubi_L"),
            (358, 29, 197, 226, 0, 0, 0, 0, "ウエストバッグ", "N_Waist"),
            (359, 20, 89, 107, 0, 0, 1, 1, "リュック", "N_Back"),
            (360, 27, 90, 117, 0, 0, 0, 0, "腕章A", "N_Arm_L"),
            (361, 92, 173, 264, 0, 0, 0, 1, "シンプル指輪", "N_Index_L"),
            (362, 15, 73, 88, 0, 0, 0, 0, "レースガーターリング左", "N_Leg_L"),
            (363, 3, 57, 60, 0, 0, 0, 0, "ディルドー", "N_Kokan"),
        };

        static string ClassOf(string p) =>
            string.IsNullOrEmpty(p) ? "empty" : p == "0" ? "zero" : p == "null" ? "null" : DocEnum.AttachPointValue(p) > 0 ? "key" : "other";

        static void Install(TestRunner t)
        {
            var rows = InstallFixture.Get(t);
            if (rows == null) { t.Info("SKIP: " + InstallFixture.Reason); return; }
            if (!rows.HasVanilla) { t.Info("SKIP: no vanilla fixture Cache/maker/vanilla_lists.tsv"); return; }
            var L = new MakerLists(rows) { ShowGated = true };
            var others = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var c in Census)
            {
                foreach (int sex in new[] { 1, 0 })
                {
                    var l = L.Rows(c.cat, sex);
                    int v = l.Count(r => r.IsVanilla), mo = l.Count - v;
                    var cls = l.GroupBy(r => ClassOf(r["Parent"])).ToDictionary(g => g.Key, g => g.Count());
                    int Get(string k) => cls.TryGetValue(k, out int n) ? n : 0;
                    var row0 = l.Count > 0 ? l[0] : null;
                    t.Check(v == c.vanilla && mo == c.mods && Get("key") == c.key && Get("zero") == c.zero && Get("null") == c.nul && Get("empty") == c.empty && Get("other") == c.other,
                        "lists: " + c.cat + " (" + (sex == 0 ? "male" : "female") + ") " + v + " vanilla + " + mo + " mod rows; Parent key / 0 / null / empty / other = " +
                        Get("key") + " / " + Get("zero") + " / " + Get("null") + " / " + Get("empty") + " / " + Get("other") + " (census " + c.vanilla + " + " + c.mods + "; " +
                        c.key + " / " + c.zero + " / " + c.nul + " / " + c.empty + " / " + c.other + ")");
                    t.Check(row0 != null && row0.Key == ContentId.Vanilla(0) && row0["Name"] == c.row0 && row0["Parent"] == c.parent0,
                        "lists: " + c.cat + " (" + (sex == 0 ? "male" : "female") + ") row 0 = «" + row0?["Name"] + "» (Parent " + row0?["Parent"] + "), the census: «" + c.row0 + "» (" + c.parent0 + ")");
                    if (sex == 1) foreach (var r in l) if (ClassOf(r["Parent"]) == "other") others[r["Parent"]] = (others.TryGetValue(r["Parent"], out int n) ? n : 0) + 1;
                }
            }
            t.Check(others.Keys.SequenceEqual(new[] { "clothes_corneo_diff", "N_Hand", "N_head", "N_neck", "N_Shoulder", "N_Tikubi", "p_acs_kuchiate" }.OrderBy(x => x, StringComparer.Ordinal)) &&
                    others.Values.All(n => n == 1),
                "lists: the Parent texts outside the 54 keys are the census's seven (" + string.Join(", ", others.Select(kv => kv.Key + " " + kv.Value)) + ")");
            var none = L.Rows(350, 1);
            t.Check(none.Count == 1 && none[0]["Parent"] == "0" && none[0].IsVanilla, "lists: 350 has one row, the only one with the Parent \"0\" (fixed)");
            // the vanilla head-weighted rows (Parent "null") of the census: 351#202 «袋頭巾» and #214
            Func<int, string> p351 = id => rows.Find(351, ContentId.Vanilla(id))?["Parent"];
            t.Check(p351(202) == "null" && p351(214) == "null" && rows.Find(351, ContentId.Vanilla(202))?["Name"] == "袋頭巾", "lists: 351#202 «袋頭巾» and #214 are head-weighted (Parent \"null\")");
        }
    }
}
