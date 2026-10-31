// The build slot of every accessory of a coordinate (
// 5): the slot number the user sees («01» … «20» for slots 0..19, «21» … for the MoreAccessories slots ≥ 20), the slot
// the assembler loads the accessory into, and the slot the bridge and the Studio State tab name. Two steps over the flat
// accessory list: in list order an entry keeps its preferred slot (`importSlot`, written by the importer and by the slot
// ops add / copy / move-to-slot) when that slot is still free; then every other entry takes the lowest free slot, in list
// order. Imported characters therefore keep exactly the card's slots. Deterministic: a function of the list only. Unity-free
// (the plain-Mono suites compile it with the save format); AccessorySlotTable of the character layer wraps it.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace HS2Next.Save
{
    public sealed class AccessorySlotRule
    {
        /// <summary>HS2's own accessory slots (0..19); slots from this number on are MoreAccessories slots.</summary>
        public const int BaseSlots = 20;
        /// <summary>How far the slot pickers reach past the highest used slot (an extra slot exists while it holds an accessory).</summary>
        public const int PickerReach = 10;

        /// <summary>Build slot → list entry, ascending by slot.</summary>
        public readonly SortedDictionary<int, AccessoryEntry> EntryBySlot = new SortedDictionary<int, AccessoryEntry>();
        /// <summary>Build slot → accessory id.</summary>
        public readonly Dictionary<int, string> IdBySlot = new Dictionary<int, string>();
        /// <summary>Accessory entry → its build slot (by reference).</summary>
        public readonly Dictionary<AccessoryEntry, int> SlotOf = new Dictionary<AccessoryEntry, int>(ReferenceComparer.Instance);
        /// <summary>Accessory id → build slot (the first entry of an id).</summary>
        public readonly Dictionary<string, int> SlotById = new Dictionary<string, int>(StringComparer.Ordinal);
        /// <summary>Entries whose preferred slot was taken by an earlier entry: (entry, preferred slot, slot used), in list order.</summary>
        public readonly List<(AccessoryEntry entry, int preferred, int used)> Reassigned = new List<(AccessoryEntry, int, int)>();

        /// <summary>The highest used slot, or -1.</summary>
        public int MaxSlot { get; private set; } = -1;

        /// <summary>The number of slotted entries.</summary>
        public int Count => EntryBySlot.Count;

        /// <summary>
        /// The slots of <paramref name="list"/> (null entries skipped): step 1, in list order, an entry whose `importSlot` is ≥ 0
        /// and not taken yet gets it; step 2, in list order, every other entry gets the lowest free slot ≥ 0. An entry whose
        /// preferred slot was taken is listed in <see cref="Reassigned"/> and, with <paramref name="findings"/>, reported as the
        /// info `build.slotReassigned`.
        /// </summary>
        public static AccessorySlotRule Assign(IReadOnlyList<AccessoryEntry> list, FindingList findings = null)
        {
            var t = new AccessorySlotRule();
            var taken = new HashSet<int>();
            var deferred = new List<AccessoryEntry>();
            if (list == null) return t;
            foreach (var e in list)
            {
                if (e == null) continue;
                if (e.ImportSlot is int s && s >= 0 && taken.Add(s)) t.Put(s, e);
                else deferred.Add(e);
            }
            int next = 0;
            foreach (var e in deferred)
            {
                while (taken.Contains(next)) next++;
                taken.Add(next);
                t.Put(next, e);
                if (e.ImportSlot is int had && had >= 0)
                {
                    t.Reassigned.Add((e, had, next));
                    findings?.Info("build.slotReassigned", "coord.accessories[" + (e.Id ?? "?") + "]",
                        "importSlot " + had.ToString(CultureInfo.InvariantCulture) + " was taken; slot " + next.ToString(CultureInfo.InvariantCulture) + " used");
                }
            }
            return t;
        }

        void Put(int slot, AccessoryEntry e)
        {
            EntryBySlot[slot] = e;
            SlotOf[e] = slot;
            if (e.Id != null)
            {
                IdBySlot[slot] = e.Id;
                if (!SlotById.ContainsKey(e.Id)) SlotById[e.Id] = slot;
            }
            if (slot > MaxSlot) MaxSlot = slot;
        }

        /// <summary>The entry in build slot <paramref name="slot"/>, or null.</summary>
        public AccessoryEntry EntryAt(int slot) => EntryBySlot.TryGetValue(slot, out var e) ? e : null;

        /// <summary>The build slot of accessory <paramref name="id"/>, or -1.</summary>
        public int SlotOfId(string id) => id != null && SlotById.TryGetValue(id, out int s) ? s : -1;

        /// <summary>Slot <paramref name="slot"/> (≥ 0) holds no accessory.</summary>
        public bool IsFree(int slot) => slot >= 0 && !EntryBySlot.ContainsKey(slot);

        /// <summary>The lowest free slot (≥ 0): a free slot of 0..19 first, else the first free slot after them.</summary>
        public int LowestFree()
        {
            int s = 0;
            while (EntryBySlot.ContainsKey(s)) s++;
            return s;
        }

        /// <summary>The last slot the slot pickers offer: the highest used slot + <see cref="PickerReach"/>, at least slot 19.</summary>
        public int PickerLast => Math.Max(BaseSlots - 1, MaxSlot + PickerReach);

        /// <summary>
        /// The free slots in picker order: the free slots of 0..19 ascending, then the free MoreAccessories slots 20, 21, … up to
        /// <paramref name="last"/> (default <see cref="PickerLast"/>). Slots ≥ 20 are offered whether MoreAccessories is on or
        /// not (an accessory there is data; the realization loads it only with MoreAccessories).
        /// </summary>
        public List<int> FreeSlots(int last = -1)
        {
            if (last < 0) last = PickerLast;
            var l = new List<int>();
            for (int s = 0; s <= last; s++) if (!EntryBySlot.ContainsKey(s)) l.Add(s);
            return l;
        }

        /// <summary>A slot is a MoreAccessories slot (≥ 20).</summary>
        public static bool IsExtraSlot(int slot) => slot >= BaseSlots;

        /// <summary>The number shown for build slot <paramref name="slot"/>: slot + 1 with at least two digits ("01", "20", "21").</summary>
        public static string Label(int slot) => (slot + 1).ToString("00", CultureInfo.InvariantCulture);

        sealed class ReferenceComparer : IEqualityComparer<AccessoryEntry>
        {
            public static readonly ReferenceComparer Instance = new ReferenceComparer();
            public bool Equals(AccessoryEntry a, AccessoryEntry b) => ReferenceEquals(a, b);
            public int GetHashCode(AccessoryEntry e) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(e);
        }
    }
}
