// BO1: Unity-free tests of bundle ownership (Runtime/Content/BundleOwners.cs, BundleLru.cs) on a fake bundle set, plain Mono:
// Tools/mono_tests.ps1 -Suite content -Groups Bundle. Scopes and their nesting, generations, pins, the thumbnails' owner,
// release into the LRU, budget ordering, the never-evict rules (busy, opened in this tick, owned, pinned), the reload count
// after an eviction, forgetting and the numbers of Stats.
using System;
using System.Collections.Generic;
using System.Linq;

namespace HS2Next.Content.Tests
{
    using HS2Next.Save.Tests;

    public static class BundleLruTests
    {
        public static void Run(TestRunner t)
        {
            var l = new BundleLru();
            l.AddHead("a", 10); l.AddHead("b", 20); l.AddHead("c", 30);
            t.Equal(60L, l.Bytes, "BO1 lru: bytes are the sum");
            t.Equal("a,b,c", string.Join(",", l.FromTail()), "BO1 lru: the tail is the oldest");
            l.AddHead("a", 5);
            t.Equal("b,c,a", string.Join(",", l.FromTail()), "BO1 lru: a key added again moves to the head");
            t.Equal(55L, l.Bytes, "BO1 lru: its bytes are replaced");
            l.SetBytes("b", 1);
            t.Equal(36L, l.Bytes, "BO1 lru: SetBytes keeps the place, changes the sum");
            t.Equal("b,c,a", string.Join(",", l.FromTail()), "BO1 lru: SetBytes keeps the order");
            t.Check(l.Remove("c") && !l.Remove("c") && !l.Contains("c"), "BO1 lru: remove once");
            t.Equal(6L, l.Bytes, "BO1 lru: bytes after the remove");
            t.Check(l.Contains("A"), "BO1 lru: keys compare without case");
            l.SetBytes("zz", 99);
            t.Equal(2, l.Count, "BO1 lru: SetBytes of an absent key adds nothing");
            l.AddHead("n", -5);
            t.Equal(6L, l.Bytes, "BO1 lru: negative bytes count as 0");
            l.Clear();
            t.Check(l.Count == 0 && l.Bytes == 0, "BO1 lru: clear");
        }
    }

    public static class BundleOwnershipTests
    {
        public static void Run(TestRunner t)
        {
            Scopes(t);
            Release(t);
            Generations(t);
            Budget(t);
            NeverEvict(t);
            Thumbnails(t);
            Reloads(t);
            ForgetAndStats(t);
            Measuring(t);
            Determinism(t);
        }

        static void Measuring(TestRunner t)
        {
            var g = new BundleLedger { BudgetBytes = 950 };
            var calls = new List<string>();
            g.Measure = (k, loaded) => { calls.Add(k + (loaded ? "+" : "-")); return (k == "big" ? 900 : 100, loaded ? 50 : 0); };
            var o = g.NewOwner("item:m");
            g.Opened("big"); g.Use("big", o);
            g.Opened("small"); g.Use("small", o); g.AssetLoaded("small", "tex");
            t.Equal(0, calls.Count, "BO1 measure: nothing measured while owned");
            g.NextTick();
            g.Release(o);
            t.Equal("big-,small+", string.Join(",", calls), "BO1 measure: each bundle measured once when it enters the LRU (objects once an asset was loaded)");
            t.Equal(1000L, g.Lru.Bytes, "BO1 measure: the LRU weighs the measured resident bytes (the 50 object bytes not)");
            t.Equal(0, g.TakeEvictions(1020).Count, "BO1 measure: object bytes do not push the LRU over a budget (1000 resident <= 1020)");
            t.Equal("big", string.Join(",", g.TakeEvictions()), "BO1 measure: the measured resident bytes decide the eviction");
            var o2 = g.NewOwner("map");
            g.Opened("late"); g.Use("late", o2);
            var st = g.Stats();
            t.Check(calls.Count == 3 && calls[2] == "late-" && st.OwnedBytes == 100, "BO1 measure: Stats measures owned bundles once");
            g.AssetLoaded("late", "p");
            st = g.Stats();
            t.Check(calls.Count == 4 && calls[3] == "late+" && st.OwnedBytes == 150, "BO1 measure: the first asset load measures again (objects counted)");
            g.Stats();
            t.Equal(4, calls.Count, "BO1 measure: no further measuring");
        }

        static void Scopes(TestRunner t)
        {
            var g = new BundleLedger();
            t.Check(g.Current == null && g.OwnerForUse(null) == g.Unscoped, "BO1 scopes: outside every scope the owner is unscoped");
            var map = g.NewOwner("map");
            var item = g.NewOwner("item:0011aabb");
            using (g.Own(map))
            {
                t.Check(g.Current == map, "BO1 scopes: the scope's owner is current");
                using (g.Own(item))
                {
                    t.Check(g.Current == item && g.Depth == 2, "BO1 scopes: the innermost scope wins");
                    g.Use("x", g.OwnerForUse(null));
                }
                t.Check(g.Current == map, "BO1 scopes: the outer owner again after the inner scope");
                g.Use("y", g.OwnerForUse(null));
                t.Check(g.OwnerForUse("abdata") == g.Pin("abdata") && g.Pin("abdata").Kind == BundleOwnerKind.Pin, "BO1 scopes: a pinned path takes its pin owner inside a scope too");
            }
            t.Check(g.Current == null && g.Depth == 0, "BO1 scopes: all popped");
            t.Check(g.IsOwnedBy("x", item) && !g.IsOwnedBy("x", map) && g.IsOwnedBy("y", map), "BO1 scopes: uses recorded under the innermost owner");
            // out of order disposal removes only its own entry
            var s1 = g.Own(map); var s2 = g.Own(item);
            s1.Dispose();
            t.Check(g.Current == item && g.Depth == 1, "BO1 scopes: a scope disposed out of order removes its own entry only");
            s2.Dispose();
            t.Check(g.Depth == 0, "BO1 scopes: empty again");
            // a released owner's scope records under unscoped
            g.Release(item);
            using (g.Own(item))
            {
                t.Check(g.OwnerForUse(null) == g.Unscoped, "BO1 scopes: inside a released owner's scope the use goes to unscoped");
                g.Use("z", g.OwnerForUse(null));
            }
            t.Equal(1, g.UnscopedLoads, "BO1 scopes: one unscoped load counted");
            g.Use("z", item);
            t.Check(!item.Live && g.IsOwnedBy("z", g.Unscoped) && !g.IsOwnedBy("z", item), "BO1 scopes: Use with a released owner records unscoped");
            t.Equal(2, g.UnscopedLoads, "BO1 scopes: and counts it");
            t.Equal("unscoped", g.StateOf("z"), "BO1 scopes: state unscoped");
        }

        static void Release(TestRunner t)
        {
            var g = new BundleLedger();
            var a = g.NewOwner("item:a"); var b = g.NewOwner("item:b");
            foreach (var k in new[] { "k1", "k2", "k3" }) { g.Opened(k); g.Use(k, a); g.SetBytes(k, 100, 0); }
            g.Use("k2", b);
            t.Equal(0, g.Lru.Count, "BO1 release: owned bundles are not in the LRU");
            int n = g.Release(a);
            t.Equal(2, n, "BO1 release: the bundles without another live owner enter the LRU");
            t.Equal("k3,k1", string.Join(",", g.Lru.FromHead()), "BO1 release: in the owner's order of first use (the last at the head)");
            t.Check(!a.Live && a.Count == 0, "BO1 release: the owner is dead and owns nothing");
            t.Equal("owned", g.StateOf("k2"), "BO1 release: a bundle another owner holds stays owned");
            t.Equal(0, g.Release(a), "BO1 release: releasing twice does nothing");
            g.Release(b);
            t.Equal("k2,k3,k1", string.Join(",", g.Lru.FromHead()), "BO1 release: the second owner's bundle at the head");
            t.Equal(300L, g.Lru.Bytes, "BO1 release: LRU bytes");
            // a use from the LRU takes it out again
            var c = g.NewOwner("map");
            g.Use("k3", c);
            t.Check(!g.Lru.Contains("k3") && g.Lru.Bytes == 200, "BO1 release: a use takes a bundle out of the LRU");
            // pins, unscoped and the thumbnails are never released
            g.Use("p", g.Pin("abdata")); g.Use("u", g.Unscoped); g.Use("th", g.Thumbs);
            t.Check(g.Release(g.Pin("abdata")) == 0 && g.Release(g.Unscoped) == 0 && g.Release(g.Thumbs) == 0
                    && g.StateOf("p") == "pinned" && g.StateOf("u") == "unscoped" && g.StateOf("th") == "thumbnail", "BO1 release: pins, unscoped and the thumbnails are not released");
        }

        static void Generations(TestRunner t)
        {
            var g = new BundleLedger();
            var gen = new OwnerGenerations(g, "chara:0a0b0c0d/clothes.top");
            var r1 = gen.Begin();
            using (g.Own(r1)) { g.Use("top_a", g.OwnerForUse(null)); g.Use("shared", g.OwnerForUse(null)); }
            t.Check(gen.End(r1) && gen.Current == r1 && gen.Generation == 1 && r1.Label == "chara:0a0b0c0d/clothes.top#1", "BO1 generations: a run that loads becomes generation 1");
            var r2 = gen.Begin();
            using (g.Own(r2)) { /* a colour change: nothing loaded */ }
            t.Check(!gen.End(r2) && gen.Current == r1 && gen.Generation == 1 && !r2.Live, "BO1 generations: a run that loads nothing keeps the previous generation");
            t.Equal(0, g.Lru.Count, "BO1 generations: nothing released by an empty run");
            var r3 = gen.Begin();
            using (g.Own(r3)) { g.Use("top_b", g.OwnerForUse(null)); g.Use("shared", g.OwnerForUse(null)); }
            t.Check(gen.End(r3) && gen.Current == r3 && gen.Generation == 2 && !r1.Live, "BO1 generations: the next loading run replaces the generation");
            t.Equal("top_a", string.Join(",", g.Lru.FromHead()), "BO1 generations: only the bundle the new run did not use is released");
            t.Check(g.IsOwnedBy("shared", r3) && g.StateOf("shared") == "owned", "BO1 generations: a bundle both runs used moved to the new generation");
            // a unit that loads everything it uses each run: an empty run releases the previous generation too
            var full = new OwnerGenerations(g, "chara:0a0b0c0d/masks");
            var m1 = full.Begin();
            using (g.Own(m1)) g.Use("mask_a", g.OwnerForUse(null));
            full.End(m1, true);
            var m2 = full.Begin();
            t.Check(!full.End(m2, true) && full.Current == null && g.Lru.Contains("mask_a"), "BO1 generations: an empty run of a load-everything unit releases the previous generation");
            gen.ReleaseAll();
            t.Check(gen.Current == null && g.Lru.Contains("top_b") && g.Lru.Contains("shared"), "BO1 generations: ReleaseAll releases the current generation");
            var st = g.Stats();
            t.Equal(0, st.Owners, "BO1 generations: no live owner left");
        }

        static void Budget(TestRunner t)
        {
            var g = new BundleLedger { BudgetBytes = 250 };
            var o = g.NewOwner("item:o");
            foreach (var (k, bytes) in new[] { ("b1", 100L), ("b2", 100L), ("b3", 100L), ("b4", 100L) }) { g.Opened(k); g.Use(k, o); g.SetBytes(k, bytes, 0); }
            g.NextTick();
            g.Release(o);   // LRU head b4 .. tail b1
            t.Equal(400L, g.Lru.Bytes, "BO1 budget: 400 bytes unowned");
            var ev = g.TakeEvictions();
            t.Equal("b1,b2", string.Join(",", ev), "BO1 budget: evicted from the tail until the bytes fit (400 -> 200 <= 250)");
            t.Check(g.Lru.Bytes == 200 && g.Evictions == 2 && !g.Knows("b1") && !g.Knows("b2"), "BO1 budget: the evicted bundles leave the ledger");
            t.Equal(0, g.TakeEvictions().Count, "BO1 budget: nothing more under the budget");
            g.SetBytes("b3", 100, 2000);
            t.Check(g.Lru.Bytes == 200 && g.TakeEvictions().Count == 0, "BO1 budget: object bytes do not weigh in the LRU (2000 object bytes, nothing evicted)");
            g.SetBytes("b3", 300, 0);
            t.Equal(400L, g.Lru.Bytes, "BO1 budget: resident bytes do");
            t.Equal("b3", string.Join(",", g.TakeEvictions()), "BO1 budget: a larger tail bundle evicted first (oldest first, not largest)");
            t.Equal("b4", string.Join(",", g.TakeEvictions(0)), "BO1 budget: a budget of 0 empties the LRU");
            g.Opened("z0"); var o2 = g.NewOwner("map"); g.Use("z0", o2); g.NextTick(); g.Release(o2);   // unknown size: 0 bytes
            t.Check(g.Lru.Bytes == 0 && g.TakeEvictions().Count == 0 && g.TakeEvictions(0).Count == 1, "BO1 budget: a 0-byte bundle stays under a budget > 0, goes with a budget of 0");
        }

        static void NeverEvict(TestRunner t)
        {
            var g = new BundleLedger { BudgetBytes = 0 };
            var o = g.NewOwner("item:o");
            foreach (var k in new[] { "busy", "fresh", "plain", "owned" }) { g.Opened(k); g.Use(k, o); g.SetBytes(k, 10, 0); }
            var keep = g.NewOwner("item:keep");
            g.Use("owned", keep);
            g.Use("pinned", g.Pin("list"));
            g.SetBytes("pinned", 10, 0);
            g.NextTick();
            g.Opened("fresh");   // opened again in this tick
            g.Busy("busy", 1);
            g.Release(o);
            var ev = g.TakeEvictions();
            t.Equal("plain", string.Join(",", ev), "BO1 never-evict: only the plain bundle goes (busy, opened in this tick, owned and pinned stay)");
            t.Check(g.Lru.Contains("busy") && g.Lru.Contains("fresh") && !g.Lru.Contains("owned") && !g.Lru.Contains("pinned"), "BO1 never-evict: busy and fresh wait in the LRU; owned and pinned are not in it");
            g.NextTick();
            g.Busy("busy", -1);
            t.Equal("busy,fresh", string.Join(",", g.TakeEvictions().OrderBy(x => x, StringComparer.Ordinal)), "BO1 never-evict: the next tick, no longer busy: both go");
            g.Busy("x", -1);
            t.Check(g.Knows("x"), "BO1 never-evict: Busy below 0 is clamped");
        }

        static void Thumbnails(TestRunner t)
        {
            var g = new BundleLedger();
            g.Opened("th"); g.Use("th", g.Thumbs);
            t.Check(g.StateOf("th") == "thumbnail" && !g.EverOwned("th"), "BO1 thumbs: a thumbnail open is thumbnail-only, never owned");
            var b = g.NewOwner("chara:00000001/base");
            g.Use("th", b); g.Disown("th", g.Thumbs);
            t.Check(g.StateOf("th") == "owned" && g.EverOwned("th") && !g.IsOwnedBy("th", g.Thumbs), "BO1 thumbs: a synchronous use clears the thumbnail mark; owned");
            g.Release(b);
            t.Check(g.Lru.Contains("th"), "BO1 thumbs: released, it enters the LRU like any bundle");
            g.Opened("th2"); g.Use("th2", g.Thumbs); g.Disown("th2", g.Thumbs);
            t.Check(g.Lru.Contains("th2"), "BO1 thumbs: a thumbnail-only bundle disowned with no other owner enters the LRU");
        }

        static void Reloads(TestRunner t)
        {
            var g = new BundleLedger();
            var o = g.NewOwner("item:r");
            g.Opened("r"); g.Use("r", o); g.AssetLoaded("r", "p_item"); g.AssetLoaded("r", "tex_a");
            g.NextTick(); g.Release(o);
            g.TakeEvictions(0);
            var o2 = g.NewOwner("item:r2");
            g.Opened("r"); g.Use("r", o2);
            g.AssetLoaded("r", "tex_b");
            t.Equal(0, g.ReloadedAssets, "BO1 reloads: a name not loaded before the eviction is no reload");
            g.AssetLoaded("r", "P_ITEM");
            g.AssetLoaded("r", "p_item");
            t.Equal(1, g.ReloadedAssets, "BO1 reloads: a name loaded before the eviction counts once");
        }

        static void ForgetAndStats(TestRunner t)
        {
            var g = new BundleLedger { BudgetBytes = 1000 };
            var a = g.NewOwner("map");
            g.Opened("m1"); g.Use("m1", a); g.SetBytes("m1", 300, 50);
            g.Opened("m2"); g.Use("m2", a); g.SetBytes("m2", 20, 0);
            g.Opened("p1"); g.Use("p1", g.Pin("abdata")); g.SetBytes("p1", 5, 0);
            g.Opened("t1"); g.Use("t1", g.Thumbs); g.SetBytes("t1", 7, 0);
            g.Opened("u1"); g.Use("u1", g.Unscoped); g.SetBytes("u1", 1, 0);
            var s = g.Stats();
            t.Check(s.Open == 5 && s.Owned == 2 && s.Pinned == 1 && s.ThumbnailOnly == 1 && s.Unscoped == 1 && s.Lru == 0 && s.OwnedBytes == 376 && s.Owners == 1,
                    $"BO1 stats: open 5, owned 2, pinned 1, thumbnail 1, unscoped 1, owned bytes 376, 1 live owner (got open {s.Open}, owned {s.Owned}, pinned {s.Pinned}, thumbs {s.ThumbnailOnly}, unscoped {s.Unscoped}, bytes {s.OwnedBytes}, owners {s.Owners})");
            g.Release(a);
            s = g.Stats();
            t.Check(s.Lru == 2 && s.LruBytes == 320 && s.Owned == 0 && s.Owners == 0 && s.Releases == 1, "BO1 stats: after the release 2 in the LRU (320 resident bytes)");
            var top = g.Largest(2);
            t.Check(top.Count == 2 && top[0].Key == "m1" && top[0].State == "lru" && top[0].Resident == 300 && top[0].Objects == 50 && top[1].Key == "m2",
                    "BO1 stats: the largest bundles first, with state and bytes");
            g.Forget("m1");
            t.Check(!g.Knows("m1") && !g.Lru.Contains("m1") && g.Evictions == 0 && g.Lru.Bytes == 20, "BO1 forget: an unload by another rule is not an eviction");
            g.ForgetAll();
            s = g.Stats();
            t.Check(s.Open == 0 && s.Lru == 0 && g.Pin("abdata").Count == 0 && g.Thumbs.Count == 0, "BO1 forget: ForgetAll leaves no bundle; the pins own nothing");
        }

        static void Determinism(TestRunner t)
        {
            string Run()
            {
                var g = new BundleLedger { BudgetBytes = 350 };
                var owners = Enumerable.Range(0, 6).Select(i => g.NewOwner("item:" + i)).ToList();
                var rnd = new Random(1234);
                var log = new List<string>();
                for (int step = 0; step < 400; step++)
                {
                    var o = owners[rnd.Next(owners.Count)];
                    if (!o.Live) { owners[owners.IndexOf(o)] = o = g.NewOwner(o.Label + "'"); }
                    string k = "k" + rnd.Next(40);
                    if (!g.Knows(k)) g.Opened(k);
                    g.Use(k, o);
                    g.SetBytes(k, 10 + rnd.Next(90), rnd.Next(3) == 0 ? rnd.Next(200) : 0);
                    if (rnd.Next(7) == 0) g.Release(o);
                    if (rnd.Next(3) == 0) { g.NextTick(); log.AddRange(g.TakeEvictions()); }
                }
                return string.Join(",", log) + "|" + g.Evictions + "|" + g.Lru.Bytes;
            }
            string a = Run(), b = Run();
            t.Check(a == b && a.Length > 10, "BO1 determinism: the same uses and releases evict the same bundles in the same order (" + a.Split('|')[1] + " evictions)");
        }
    }
}
