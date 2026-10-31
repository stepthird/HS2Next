// The build slots of a coordinate's accessories (AccessorySlotRule, Phase 4.5: the slot the user sees, the assembler loads
// into and the bridge names): a table of lists (preferred slots first, clashes, the lowest free slot, gaps, MoreAccessories
// slots ≥ 20, entries without a preferred slot, null entries, duplicate ids), the free-slot rule of the pickers, the labels,
// and 200 random lists against the rule as the character layer's AccessorySlotTable had it before 4.5 (kept here as the
// reference: in list order an entry keeps its importSlot when ≥ 0 and free, then every other entry takes the lowest free
// slot in list order; the wrapper calls AccessorySlotRule).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace HS2Next.Save.Tests
{
    public static class AccessorySlotRuleTests
    {
        public static void Run(TestRunner t)
        {
            Table(t);
            Free(t);
            Random200(t);
        }

        static AccessoryEntry E(string id, int? slot) => new AccessoryEntry { Id = id, Category = 351, Item = "illusion.hs2#0", ImportSlot = slot };

        static string Slots(AccessorySlotRule r) => string.Join(" ", r.EntryBySlot.Select(kv => kv.Key.ToString(CultureInfo.InvariantCulture) + ":" + kv.Value.Id));

        static void Table(TestRunner t)
        {
            var cases = new (string what, AccessoryEntry[] list, string want, int reassigned)[]
            {
                ("empty", new AccessoryEntry[0], "", 0),
                ("the card's slots kept, gaps kept", new[] { E("a", 0), E("b", 3), E("c", 7) }, "0:a 3:b 7:c", 0),
                ("list order does not matter for free preferred slots", new[] { E("c", 7), E("a", 0), E("b", 3) }, "0:a 3:b 7:c", 0),
                ("no preferred slot: the lowest free slot in list order", new[] { E("a", null), E("b", null), E("c", 1) }, "0:a 1:c 2:b", 0),
                ("a clash: the first entry in list order keeps the slot, the second takes the lowest free", new[] { E("a", 2), E("b", 2), E("c", null) }, "0:b 1:c 2:a", 1),
                ("a negative preferred slot counts as none", new[] { E("a", -1), E("b", 0) }, "0:b 1:a", 0),
                ("MoreAccessories slots ≥ 20 keep their numbers", new[] { E("a", 20), E("b", 35), E("c", null) }, "0:c 20:a 35:b", 0),
                ("all 20 base slots used: the next free is 20", Enumerable.Range(0, 20).Select(i => E("s" + i, i)).Concat(new[] { E("x", null) }).ToArray(),
                    string.Join(" ", Enumerable.Range(0, 20).Select(i => i + ":s" + i)) + " 20:x", 0),
                ("deferred entries fill the gaps below preferred slots", new[] { E("a", 4), E("b", null), E("c", null), E("d", 1) }, "0:b 1:d 2:c 4:a", 0),
                ("a clash with a slot an entry without a preference would take", new[] { E("a", null), E("b", 0), E("c", 0) }, "0:b 1:a 2:c", 1),
            };
            foreach (var c in cases)
            {
                var r = AccessorySlotRule.Assign(c.list);
                t.Check(Slots(r) == c.want && r.Reassigned.Count == c.reassigned && r.Count == c.list.Length, "slot rule: " + c.what + " (" + Slots(r) + ")");
            }
            var list = new List<AccessoryEntry> { E("a", 5), null, E("b", 5), E(null, null), E("a", 9) };
            var f = new FindingList();
            var rr = AccessorySlotRule.Assign(list, f);
            t.Check(rr.Count == 4 && rr.SlotOfId("a") == 5 && rr.SlotOfId("b") == 0 && rr.IdBySlot.Count == 3 && rr.MaxSlot == 9 && rr.EntryAt(1)?.Id == null && rr.EntryAt(1) != null,
                "slot rule: null entries skipped, an entry without an id slotted (no id map), a duplicate id maps to its first slot");
            t.Check(rr.Reassigned.Count == 1 && rr.Reassigned[0].entry.Id == "b" && rr.Reassigned[0].preferred == 5 && rr.Reassigned[0].used == 0 &&
                    f.Count(x => x.Code == "build.slotReassigned") == 1 && f.All(x => x.Severity == FindingSeverity.Info),
                "slot rule: the taken preferred slot is listed and reported once as the info build.slotReassigned");
            t.Check(rr.SlotOf[list[0]] == 5 && rr.SlotOf[list[2]] == 0 && rr.SlotOf[list[4]] == 9 && !rr.SlotOf.ContainsKey(E("a", 5)),
                "slot rule: SlotOf maps entries by reference");
            t.Check(AccessorySlotRule.Assign(null).Count == 0 && AccessorySlotRule.Assign(null).MaxSlot == -1, "slot rule: a null list has no slots");
        }

        static void Free(TestRunner t)
        {
            var r = AccessorySlotRule.Assign(new[] { E("a", 0), E("b", 2), E("c", 19) });
            t.Check(r.LowestFree() == 1 && r.IsFree(1) && !r.IsFree(2) && !r.IsFree(-1) && r.EntryAt(2).Id == "b" && r.EntryAt(3) == null,
                "slot rule: the lowest free slot, IsFree, EntryAt");
            t.Check(r.PickerLast == 29 && r.FreeSlots().SequenceEqual(Enumerable.Range(0, 30).Where(s => s != 0 && s != 2 && s != 19)),
                "slot rule: the pickers offer the free slots up to the highest used slot + 10 (" + r.PickerLast + ")");
            var low = AccessorySlotRule.Assign(new[] { E("a", 3) });
            t.Check(low.PickerLast == 19 && low.FreeSlots().Count == 19 && low.FreeSlots(25).Count == 25 && AccessorySlotRule.Assign(new AccessoryEntry[0]).LowestFree() == 0,
                "slot rule: at least slots 0..19 are offered; an explicit last slot");
            var full = AccessorySlotRule.Assign(Enumerable.Range(0, 25).Select(i => E("s" + i, i)).ToArray());
            t.Check(full.LowestFree() == 25 && full.FreeSlots().First() == 25 && full.PickerLast == 34, "slot rule: with slots 0..24 used the next free slot is 25");
            t.Check(AccessorySlotRule.Label(0) == "01" && AccessorySlotRule.Label(19) == "20" && AccessorySlotRule.Label(20) == "21" && AccessorySlotRule.Label(99) == "100" &&
                    !AccessorySlotRule.IsExtraSlot(19) && AccessorySlotRule.IsExtraSlot(20) && AccessorySlotRule.BaseSlots == 20,
                "slot rule: labels slot + 1 with two digits; slots ≥ 20 are MoreAccessories slots");
        }

        // The rule as AccessorySlotTable.Assign had it before 4.5 (the reference of the wrapper's behaviour).
        static SortedDictionary<int, AccessoryEntry> Reference(IReadOnlyList<AccessoryEntry> list)
        {
            var bySlot = new SortedDictionary<int, AccessoryEntry>();
            var taken = new HashSet<int>();
            var deferred = new List<AccessoryEntry>();
            foreach (var e in list)
            {
                if (e == null) continue;
                if (e.ImportSlot is int s && s >= 0 && taken.Add(s)) bySlot[s] = e;
                else deferred.Add(e);
            }
            int next = 0;
            foreach (var e in deferred)
            {
                while (taken.Contains(next)) next++;
                taken.Add(next);
                bySlot[next] = e;
            }
            return bySlot;
        }

        static void Random200(TestRunner t)
        {
            int same = 0, ok = 0;
            for (int seed = 1; seed <= 200; seed++)
            {
                var rnd = new Random(seed * 7919);
                int n = rnd.Next(0, 63);
                var list = new List<AccessoryEntry>();
                for (int i = 0; i < n; i++)
                {
                    int k = rnd.Next(10);
                    int? slot = k < 2 ? (int?)null : k < 3 ? -1 : k < 8 ? rnd.Next(0, 20) : rnd.Next(20, 62);
                    list.Add(rnd.Next(40) == 0 ? null : E("id" + i, slot));
                }
                var want = Reference(list);
                var r = AccessorySlotRule.Assign(list);
                if (want.Count == r.EntryBySlot.Count && want.All(kv => r.EntryBySlot.TryGetValue(kv.Key, out var e) && ReferenceEquals(e, kv.Value))) same++;
                // invariants: every entry once, slots distinct, a free preferred slot kept, deferred entries take the lowest free slots
                var ids = r.EntryBySlot.Values.ToList();
                bool inv = ids.Distinct().Count() == ids.Count && ids.Count == list.Count(x => x != null) && r.SlotOf.Count == ids.Count &&
                           r.MaxSlot == (r.Count == 0 ? -1 : r.EntryBySlot.Keys.Max()) && r.EntryBySlot.All(kv => r.SlotOf[kv.Value] == kv.Key);
                if (inv) ok++;
            }
            t.Check(same == 200, "slot rule: 200 random lists (0..62 entries, preferred slots 0..61, clashes, none, -1, null entries) give the reference's slots (" + same + " / 200)");
            t.Check(ok == 200, "slot rule: on the 200 lists every entry has one slot, slots are distinct, the maps agree (" + ok + " / 200)");
        }
    }
}
