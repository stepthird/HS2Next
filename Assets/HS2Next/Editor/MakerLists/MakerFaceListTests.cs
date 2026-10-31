// Maker list additions of Phase 4.1 (UF4),
// plain Mono (Tools/mono_tests.ps1 -Suite maker):
//   synthetic rows: LayoutValues by id (Scale, PosX, PosY; unknown, hidden and unparsable rows refused), HasMoreThanOne,
//   Warm / IsWarm;
//   the install (skips without the fixture): MakerLists.Warm(211, 111) builds the face-skin head indices (UF4: its cost,
//   target ≤ 10 ms once) and the first Rows of a skin list afterwards meets the 2 ms target; LayoutValues of lists 6 / 7 =
//   the census rows; the male head list has 10 rows (the maker.headList gate holds, F3); the preset grid cells
//   (PresetHeads) are 367 female / 9 male (F4).
using System;
using System.Diagnostics;
using System.Linq;

namespace HS2Next.Content.Maker.Tests
{
    using HS2Next.Save.Tests;

    public static class MakerFaceListTests
    {
        public static void Run(TestRunner t)
        {
            Synthetic(t);
            Install(t);
        }

        static void Synthetic(TestRunner t)
        {
            var m = new MemRows();
            m.Add(6, null, 0, null, false, "Name", "a", "Scale", "0.14", "PosX", "0.6", "PosY", "0.18");
            m.Add(6, null, 4, null, false, "Name", "b", "Scale", "0.33", "PosX", "0.75", "PosY", "0.52");
            m.Add(6, null, 5, null, false, "Name", "hidden", "Possess", "99", "Scale", "0.3", "PosX", "0.1", "PosY", "0.1");
            m.Add(6, null, 6, null, false, "Name", "bad", "Scale", "x", "PosX", "0.1", "PosY", "0.1");
            m.Add(7, null, 2, null, false, "Name", "c", "Scale", "0.5", "PosX", "0.48", "PosY", "0.7");
            m.Add(110, null, 0, null, false, "Name", "male head");
            m.Add(210, null, 0, null, false, "Name", "f0");
            m.Add(210, null, 1, null, false, "Name", "f1");
            var L = new MakerLists(m);
            t.Check(L.LayoutValues(6, ContentId.Vanilla(4), out float s, out float x, out float y) && s == 0.33f && x == 0.75f && y == 0.52f, "layout: row 4 of list 6 by id = (0.33, 0.75, 0.52)");
            t.Check(L.LayoutValues(7, ContentId.Vanilla(2), out s, out x, out y) && s == 0.5f && x == 0.48f && y == 0.7f, "layout: row 2 of list 7");
            t.Check(!L.LayoutValues(6, ContentId.Vanilla(1), out _, out _, out _), "layout: an unknown id → false (by id, not position)");
            t.Check(!L.LayoutValues(6, ContentId.Vanilla(5), out _, out _, out _), "layout: a hidden row (Possess 99) → false");
            t.Check(!L.LayoutValues(6, ContentId.Vanilla(6), out _, out _, out _), "layout: an unparsable column → false");
            t.Check(!L.LayoutValues(7, ContentId.Vanilla(4), out _, out _, out _), "layout: a row of the other list → false");
            t.Check(!L.HasMoreThanOne(110, 0) && L.HasMoreThanOne(210, 1) && !L.HasMoreThanOne(210, 0), "gate: one male head → false; two female heads → true; a list of the other sex → false");
            t.Check(!L.IsWarm(211) && !L.IsWarm(6), "warm: nothing built before");
            L.Warm(211, 6);
            t.Check(L.IsWarm(211) && L.IsWarm(6) && !L.IsWarm(111), "warm: Warm builds the named categories only");
            L.Warm(null);
            L.Invalidate();
            t.Check(!L.IsWarm(211), "warm: Invalidate forgets them");
        }

        static readonly (float s, float x, float y)[] Mole =
        {
            (0.14f, 0.6f, 0.18f), (0.14f, 0.4f, 0.18f), (0.14f, 0.56f, 0.29f), (0.14f, 0.44f, 0.29f),
            (0.33f, 0.75f, 0.52f), (0.33f, 0.25f, 0.52f), (0.71f, 0.5f, 0.75f), (0.7f, 0.5f, 0.48f),
        };
        static readonly (float s, float x, float y)[] Paint =
            { (0.5f, 0.76f, 0.35f), (0.5f, 0.24f, 0.35f), (0.5f, 0.48f, 0.7f), (0.5f, 0.48f, 0.44f), (0.5f, 0.48f, 0.195f), (0.5f, 0.48f, 0.07f) };

        static void Install(TestRunner t)
        {
            var rows = InstallFixture.Get(t);
            if (rows == null) { t.Info("SKIP: " + InstallFixture.Reason); return; }
            if (!rows.HasVanilla) { t.Info("SKIP: no vanilla fixture Cache/maker/vanilla_lists.tsv"); return; }
            // UF4: the warm cost and the first Rows after it (median of 5 fresh MakerLists; the first run also JITs)
            var warm = new double[5];
            var first = new double[5];
            ContentId femaleHead = ContentId.Vanilla(3), maleHead = ContentId.Vanilla(0);
            for (int i = 0; i < warm.Length; i++)
            {
                var L = new MakerLists(rows) { ShowGated = true };
                var sw = Stopwatch.StartNew();
                L.Warm(211, 111);
                warm[i] = sw.Elapsed.TotalMilliseconds;
                sw.Restart();
                int n = L.Rows(211, 1, ListContext.ForHead(femaleHead)).Count + L.Rows(111, 0, ListContext.ForHead(maleHead)).Count;
                first[i] = sw.Elapsed.TotalMilliseconds;
                if (i == 0) t.Check(n > 0 && L.IsWarm(211) && L.IsWarm(111), "warm: the skin head indices of 211 and 111 are built (" + n + " skins of heads f3 / m0)");
            }
            double w = Median(warm), f = Median(first);
            t.Info($"UF4 (plain Mono): MakerLists.Warm(211, 111) median {w:0.00} ms (first {warm[0]:0.00}); first Rows of both skin lists after it median {f:0.000} ms");
            t.Check(w <= 20, $"warm: MakerLists.Warm(211, 111) {w:0.00} ms (target 10 ms once, fail above 2x)");
            t.Check(f <= 2, $"warm: the first Rows of a skin list after Warm {f:0.000} ms (target 2 ms)");
            // layout rows by id
            var M = new MakerLists(rows) { ShowGated = true };
            int ok = 0;
            for (int i = 0; i < Mole.Length; i++)
                if (M.LayoutValues(6, ContentId.Vanilla(i), out float s, out float x, out float y) && s == Mole[i].s && x == Mole[i].x && y == Mole[i].y) ok++;
            for (int i = 0; i < Paint.Length; i++)
                if (M.LayoutValues(7, ContentId.Vanilla(i), out float s, out float x, out float y) && s == Paint[i].s && x == Paint[i].x && y == Paint[i].y) ok++;
            t.Equal(14, ok, "layout: the 8 mole and 6 face-paint rows of the install as listed");
            t.Check(M.Rows(6, 1).Count == 8 && M.Rows(7, 0).Count == 6, "layout: lists 6 / 7 have 8 / 6 rows for both sexes");
            // the male head list (F3) and the preset grid (F4)
            t.Equal(10, M.Rows(110, 0).Count, "gate: the male head list has 10 rows (1 vanilla + 9 mod)");
            t.Check(M.HasMoreThanOne(110, 0), "gate: maker.headList holds for males on this install");
            int pf = M.PresetHeads(1).Count, pm = M.PresetHeads(0).Count;
            t.Check(pf == 367 && pm == 9, $"presets: the preset grid has {pf} female / {pm} male cells (367 / 9)");
        }

        static double Median(double[] a)
        {
            var s = a.OrderBy(v => v).ToArray();
            return s[s.Length / 2];
        }
    }
}
