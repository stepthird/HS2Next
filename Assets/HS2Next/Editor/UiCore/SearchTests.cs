// SearchTests and ListIndexTests: normalisation (NFKC, case, kana, width), AND
// tokens, `guid#id` match, the 25k-row filter timing (reported; a warning line above 5 ms); stable sorts (name ja-JP /
// ordinal, date), facets, minimal refresh.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace HS2Next.UI.Core.Tests
{
    using HS2Next.Save.Tests;
    using HS2Next.UI.Core;

    public static class SearchTests
    {
        public static void Run(TestRunner t)
        {
            Normalise(t);
            Queries(t);
            Timing(t);
        }

        static void Normalise(TestRunner t)
        {
            var cases = new (string input, string want, string what)[]
            {
                ("ＡＢＣ１２３", "abc123", "full-width ASCII → half-width, lower case"),
                ("Chair", "chair", "lower case"),
                ("カタカナ", "かたかな", "katakana → hiragana"),
                ("ｶﾀｶﾅ", "かたかな", "half-width katakana → full-width → hiragana"),
                ("ガギグ", "がぎぐ", "voiced katakana"),
                ("ﾊﾟﾝﾂ", "ぱんつ", "half-width voiced mark composes"),
                ("ヴ", "ゔ", "vu"),
                ("ヽヾ", "ゝゞ", "iteration marks"),
                ("ー", "ー", "long vowel mark kept"),
                ("机　椅子", "机 椅子", "ideographic space → space"),
                ("①", "1", "circled digit (NFKC)"),
                ("ｷｬﾗ", "きゃら", "small kana"),
                ("", "", "empty"),
                (null, "", "null"),
            };
            foreach (var c in cases) t.Equal(c.want, SearchText.Normalize(c.input), "normalise: " + c.what);
        }

        static void Queries(TestRunner t)
        {
            string key = SearchText.Normalize("椅子 チェア 家具 Furniture vanilla");
            t.Check(SearchQuery.Parse("ちぇあ").Matches(key), "query: hiragana finds katakana");
            t.Check(SearchQuery.Parse("ﾁｪｱ").Matches(key), "query: half-width katakana finds katakana");
            t.Check(SearchQuery.Parse("FURN").Matches(key), "query: case-insensitive substring");
            t.Check(SearchQuery.Parse("ｆｕｒｎ").Matches(key), "query: full-width query");
            t.Check(SearchQuery.Parse("椅子 家具").Matches(key), "query: AND of two tokens");
            t.Check(!SearchQuery.Parse("椅子 机").Matches(key), "query: AND fails when one token is absent");
            t.Check(SearchQuery.Parse("家具　椅子").Matches(key), "query: ideographic space separates tokens");
            t.Check(SearchQuery.Parse("   ").IsEmpty && SearchQuery.Parse("").Matches("anything"), "query: blank = everything");
            t.Equal(2, SearchQuery.Parse("a a b").Tokens.Count, "query: duplicate tokens collapse");
            SearchQuery.SplitRef("0a1b2c3d4e5f60718293a4b5c6d7e8f9#120", out var g, out var id);
            t.Check(g == "0a1b2c3d4e5f60718293a4b5c6d7e8f9" && id == "120", "ref: split");
            var q = SearchQuery.Parse("0a1b#120");
            t.Check(q.Refs.Count == 1 && q.Matches(key, g, id), "ref: guid prefix + exact id");
            t.Check(!SearchQuery.Parse("0a1b#12").Matches(key, g, id), "ref: id is exact (#12 ≠ #120)");
            t.Check(SearchQuery.Parse("#120").Matches(key, g, id), "ref: empty guid part = any guid");
            t.Check(SearchQuery.Parse("0A1B#").Matches(key, g, id), "ref: trailing # = any id, guid case-insensitive");
            t.Check(!SearchQuery.Parse("ffff#120").Matches(key, g, id), "ref: other guid");
            t.Check(SearchQuery.Parse("椅子 #120").Matches(key, g, id) && !SearchQuery.Parse("机 #120").Matches(key, g, id), "ref: ANDed with text tokens");
            t.Check(SearchQuery.Parse("c#").Matches(SearchText.Normalize("Script C# tools")), "ref: rows without a ref match '#' tokens as text");
        }

        sealed class Row
        {
            public string Name, Category, Group, Source, Ref;
            public int Order;
        }

        static List<Row> Synthetic(int n)
        {
            var rnd = new Random(1234);
            string[] kana = { "椅子", "チェア", "テーブル", "机", "ソファ", "ベッド", "ランプ", "花瓶", "本棚", "時計", "ﾊﾟｲﾌﾟ", "Chair", "Table", "Lamp", "Box" };
            string[] cats = { "家具", "照明", "小物", "建物", "自然", "エフェクト" };
            string[] groups = { "基本", "Mod A", "Mod B", "Mod C" };
            var l = new List<Row>(n);
            for (int i = 0; i < n; i++)
            {
                l.Add(new Row
                {
                    Name = kana[rnd.Next(kana.Length)] + " " + kana[rnd.Next(kana.Length)] + " " + i,
                    Category = cats[rnd.Next(cats.Length)],
                    Group = groups[rnd.Next(groups.Length)],
                    Source = i < 1285 ? "vanilla" : "mod" + (i % 97),
                    Ref = (i % 50).ToString("x32") + "#" + i,
                    Order = i,
                });
            }
            return l;
        }

        static void Timing(TestRunner t)
        {
            var rows = Synthetic(25079);
            var sw = Stopwatch.StartNew();
            var ix = new ListIndex<Row>(rows, r => r.Name + " " + r.Category + " " + r.Group + " " + r.Source, r => r.Ref);
            ix.AddFacet("group", r => r.Group);
            long build = sw.ElapsedMilliseconds;
            ix.Refresh();
            var queries = new[] { "ち", "ちぇあ", "椅子 家具", "chair", "ﾗﾝﾌﾟ", "mod1", "#120", "zzz", "テーブル 照明 mod a", "e" };
            // warm up, then time 3 rounds of the 10 queries
            foreach (var q in queries) { ix.SetQuery(q); ix.Refresh(); }
            var times = new List<double>();
            for (int round = 0; round < 3; round++)
                foreach (var q in queries)
                {
                    ix.SetQuery(q + (round % 2 == 0 ? "" : " "));   // a changed text with the same tokens still filters (dirty)
                    ix.SetQuery("");
                    ix.SetQuery(q);
                    var s2 = Stopwatch.StartNew();
                    ix.Refresh();
                    times.Add(s2.Elapsed.TotalMilliseconds);
                }
            times.Sort();
            double median = times[times.Count / 2], max = times[times.Count - 1];
            t.Info(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "25,079-row filter: median {0:0.00} ms, max {1:0.00} ms over {2} filters (key build {3} ms) [U-M2 mono]", median, max, times.Count, build));
            if (median > 5) t.Info("WARNING: 25k-row filter median above the 5 ms target");
            t.Check(median < 50, "timing: 25k-row filter well under 50 ms (median " + median.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " ms)");
            ix.SetQuery("椅子 家具");
            ix.Refresh();
            int expected = rows.Count(r => SearchText.Normalize(r.Name + " " + r.Category).Contains("椅子") && r.Category == "家具");
            t.Equal(expected, ix.Indices.Count, "timing: filter result equals a brute-force count");
            ix.SetFacet("group", "基本");
            ix.Refresh();
            t.Check(ix.Indices.All(i => rows[i].Group == "基本") && ix.Indices.Count > 0, "timing: facet + query");
        }
    }

    public static class ListIndexTests
    {
        sealed class R
        {
            public string Name, Cat, File;
            public long Date;
            public override string ToString() => Name;
        }

        public static void Run(TestRunner t)
        {
            var rows = new List<R>
            {
                new R { Name = "さくら", Cat = "a", File = "b.png", Date = 3 },
                new R { Name = "Alice", Cat = "b", File = "a.png", Date = 1 },
                new R { Name = "アキラ", Cat = "a", File = "c.png", Date = 2 },
                new R { Name = "bob", Cat = "b", File = "d.png", Date = 2 },
                new R { Name = "Alice", Cat = "a", File = "e.png", Date = 5 },
                new R { Name = "あきら", Cat = "c", File = "f.png", Date = 2 },
            };
            var ix = new ListIndex<R>(rows, r => r.Name + " " + r.File);
            ix.AddFacet("cat", r => r.Cat);
            ix.AddSort("name", (a, b) => ListSorts.CompareJa(a.Name, b.Name));
            ix.AddSort("ordinal", (a, b) => ListSorts.CompareOrdinal(a.Name, b.Name));
            ix.AddSort("date", (a, b) => a.Date.CompareTo(b.Date));
            t.Equal(ListChange.Changed, ix.Refresh(), "refresh: the first refresh fills");
            t.Check(ix.Indices.SequenceEqual(new[] { 0, 1, 2, 3, 4, 5 }), "refresh: source order without a sort");
            t.Equal(ListChange.Unchanged, ix.Refresh(), "refresh: nothing changed = Unchanged");
            ix.SetQuery("");
            t.Equal(ListChange.Unchanged, ix.Refresh(), "refresh: same query = Unchanged");

            ix.SetSort("date");
            t.Equal(ListChange.Reordered, ix.Refresh(), "refresh: a new sort = Reordered");
            t.Check(ix.Indices.SequenceEqual(new[] { 1, 2, 3, 5, 0, 4 }), "sort: date ascending, ties in source order (" + string.Join(",", ix.Indices) + ")");
            ix.SetSort("date", true);
            ix.Refresh();
            t.Check(ix.Indices.SequenceEqual(new[] { 4, 0, 2, 3, 5, 1 }), "sort: date descending, ties still in source order (stable) (" + string.Join(",", ix.Indices) + ")");
            ix.ToggleSort("date");
            t.Check(!ix.Descending, "sort: clicking the active key flips the direction");
            ix.ToggleSort("name");
            t.Check(!ix.Descending && ix.SortName == "name", "sort: clicking another key sorts it ascending");
            ix.Refresh();
            var names = ix.Indices.Select(i => rows[i].Name).ToList();
            t.Check(names.IndexOf("Alice") < names.IndexOf("bob") && ix.Indices.IndexOf(1) < ix.Indices.IndexOf(4), "sort: ja-JP name: Latin first, equal names keep source order (" + string.Join(",", names) + ")");
            int ak = names.IndexOf("アキラ"), ah = names.IndexOf("あきら");
            t.Check(Math.Abs(ak - ah) == 1, "sort: ja-JP name: katakana and hiragana of one word sort together (" + string.Join(",", names) + ")");
            ix.SetSort("ordinal");
            ix.Refresh();
            var ord = ix.Indices.Select(i => rows[i].Name).ToList();
            t.Check(ord.SequenceEqual(new[] { "Alice", "Alice", "bob", "あきら", "さくら", "アキラ" }), "sort: ordinal (" + string.Join(",", ord) + ")");

            ix.SetSort(null);
            ix.SetFacet("cat", "a");
            t.Equal(ListChange.Changed, ix.Refresh(), "facet: rows removed = Changed");
            t.Check(ix.Indices.SequenceEqual(new[] { 0, 2, 4 }), "facet: cat a");
            var counts = ix.FacetCounts("cat");
            t.Check(counts["a"] == 3 && counts["b"] == 2 && counts["c"] == 1, "facet: counts ignore the facet's own selection");
            ix.SetQuery("alice");
            ix.Refresh();
            t.Check(ix.Indices.SequenceEqual(new[] { 4 }), "facet + query");
            counts = ix.FacetCounts("cat");
            t.Check(counts["a"] == 1 && counts["b"] == 1 && !counts.ContainsKey("c"), "facet: counts follow the query");
            ix.SetQuery("alice ");   // same tokens, different text: filters again, same result
            t.Equal(ListChange.Unchanged, ix.Refresh(), "refresh: a re-filter with the same result = Unchanged");
            ix.SetFacet("cat", null);
            ix.Refresh();
            t.Check(ix.Indices.SequenceEqual(new[] { 1, 4 }), "facet: null = all");
            // in-place row change
            rows[3].Name = "Alice Cooper";
            ix.RowsChanged(new[] { 3 });
            t.Equal(ListChange.Changed, ix.Refresh(), "rows changed: the key is recomputed");
            t.Check(ix.Indices.SequenceEqual(new[] { 1, 3, 4 }), "rows changed: the row now matches");
            t.Equal(1, ix.PositionOf(3), "position of a row");
            t.Equal(-1, ix.PositionOf(0), "position of a filtered row");
            var same = ix.Indices;
            ix.SetRows(rows.Take(2).ToList());
            ix.Refresh();
            t.Check(ReferenceEquals(same, ix.Indices) && ix.Indices.SequenceEqual(new[] { 1 }), "set rows: the same Indices instance (the view's itemsSource)");
            t.Throws<ArgumentException>(() => ix.SetSort("nope"), "sort: unknown name");

            // nested facets (the browsers' group / category, v0.2 "(0)" counts): the group counts ignore the category, which
            // only exists inside its group; with a query, the values without matches count 0 (the browser hides them)
            var nested = new List<R>
            {
                new R { Name = "heart 1", Cat = "g0/c0" }, new R { Name = "heart 2", Cat = "g0/c1" },
                new R { Name = "star", Cat = "g0/c0" }, new R { Name = "heart necklace", Cat = "g1/c5" }, new R { Name = "chair", Cat = "g2/c0" },
            };
            var nx = new ListIndex<R>(nested, r => r.Name);
            nx.AddFacet("group", r => r.Cat.Split('/')[0]);
            nx.AddFacet("category", r => r.Cat);
            nx.SetFacet("group", "g0");
            nx.SetFacet("category", "g0/c1");
            nx.Refresh();
            var gc = nx.FacetCounts("group", "category");
            t.Check(gc["g0"] == 3 && gc["g1"] == 1 && gc["g2"] == 1, "nested facet: group counts ignore the selected category (" + string.Join(",", gc.Select(kv => kv.Key + "=" + kv.Value)) + ")");
            var old = nx.FacetCounts("group");
            t.Check(old["g0"] == 1 && !old.ContainsKey("g1"), "nested facet: without alsoIgnore the category selection still applies (old behaviour)");
            nx.SetQuery("heart");
            nx.Refresh();
            gc = nx.FacetCounts("group", "category");
            t.Check(gc["g0"] == 2 && gc["g1"] == 1 && !gc.ContainsKey("g2"), "nested facet: group counts follow the query (" + string.Join(",", gc.Select(kv => kv.Key + "=" + kv.Value)) + ")");
            var cc = nx.FacetCounts("category");
            t.Check(cc["g0/c0"] == 1 && cc["g0/c1"] == 1 && !cc.ContainsKey("g1/c5"), "nested facet: category counts stay inside the selected group");
        }
    }
}
