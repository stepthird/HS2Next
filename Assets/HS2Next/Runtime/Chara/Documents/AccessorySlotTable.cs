// Build slots of a coordinate's accessory list: the HS2 slot layout the assembler
// reads (slots 0..19 in Accessory.parts, ≥ 20 through MoreAccessories) from the unbounded list of the coord document.
// The rule itself is HS2Next.Save.AccessorySlotRule (Unity-free, shared with the model layer, the bridge and the Studio
// State tab): an entry keeps its importSlot (its preferred slot) when that slot is free; every other entry takes the
// lowest free slot in list order. This class is the character layer's view of it with the findings of the build.
using System.Collections.Generic;
using HS2Next.Save;

namespace HS2Next.Chara.Documents
{
    public sealed class AccessorySlotTable
    {
        /// <summary>Build slot → list entry, ascending by slot.</summary>
        public readonly SortedDictionary<int, AccessoryEntry> EntryBySlot;
        /// <summary>Build slot → accessory id (CharaBuildInput.AccessoryIds).</summary>
        public readonly Dictionary<int, string> IdBySlot;
        /// <summary>Accessory entry → its build slot (by reference).</summary>
        public readonly Dictionary<AccessoryEntry, int> SlotOf;
        /// <summary>Accessory id → build slot (first entry of an id).</summary>
        public readonly Dictionary<string, int> SlotById;
        /// <summary>The shared rule the table was made from.</summary>
        public readonly AccessorySlotRule Rule;

        /// <summary>The highest used slot, or -1.</summary>
        public int MaxSlot => Rule.MaxSlot;

        AccessorySlotTable(AccessorySlotRule rule)
        {
            Rule = rule;
            EntryBySlot = rule.EntryBySlot;
            IdBySlot = rule.IdBySlot;
            SlotOf = rule.SlotOf;
            SlotById = rule.SlotById;
        }

        /// <summary>The build slots of <paramref name="list"/> (AccessorySlotRule.Assign; an entry whose preferred slot was taken
        /// is reported as the info `build.slotReassigned`).</summary>
        public static AccessorySlotTable Assign(IReadOnlyList<AccessoryEntry> list, FindingList f) => new AccessorySlotTable(AccessorySlotRule.Assign(list, f));
    }
}
