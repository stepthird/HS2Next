// Accessory units (acc:<id>): the slot table (build slot numbers of the
// coordinate's accessory list, MoreAccessories numbering for slots >= 20) and one unit per accessory id. A slot is
// loaded with the pre-4.0 loader (list row, prefab ca_slotNN, anchor, moves
// ×0.1, colours, alpha renderers); an item, category or parent change reloads the slot (part swap, same slot number and
// sibling index), a move or colour change is written on the loaded object. Alpha renderers are active exactly when
// colors[3].color.a != 0 (two-way, G5 fixed). Card builds read their slots from the card (CardDocuments.Accessories and
// the card's MoreAccessories data).
// Phase 4.5: hair-type rows get the hair shader of hair.shaderType, the row's
// TexD / TexC and the hair colours (a change of hair.shaderType marks every accessory unit; a non-hair slot's run only
// rewrites its values); MoreAccessories' show=false no longer hides the object at the build (the import folds it into
// state.accessories); smoothness is carried for the hair-type rows.
using System.Collections.Generic;
using System.Linq;
using AIChara;
using HS2Next.Chara.Documents;
using HS2Next.Chara.Ext;
using HS2Next.Content;
using HS2Next.Data;
using HS2Next.Data.Ext;
using HS2Next.Save;
using UnityEngine;
using EM = HS2Next.Chara.Edit.EditMaterials;

namespace HS2Next.Chara.Edit.Units
{
    public static class AccessorySlots
    {
        /// <summary>The build slot of a document accessory as the pre-4.0 adapter + loader read it (VanillaTrees for slots
        /// 0..19, ExtSections and the MoreAccessories part reading for slots >= 20).</summary>
        public static AccessorySlot FromEntry(AccessoryEntry e, int slot)
        {
            var acc = new AccessorySlot
            {
                Slot = slot, Type = e.Category, Id = EditContext.RawId(e.Item), ParentKey = e.Parent ?? "",
                HideCategory = e.HideCategory, HideTiming = e.HideTiming, NoShake = e.NoShake,
                Property = slot < MoreAccessoriesData.FirstSlot ? $"outfit.accessory{slot}.ChaFileAccessory.PartsInfo.id" : Card.ResolveInfo.AccessoryProperty(slot, true),
                Extra = slot >= MoreAccessoriesData.FirstSlot,
            };
            for (int j = 0; j < 2; j++)
            {
                var mv = e.Moves != null && j < e.Moves.Length && e.Moves[j] != null ? e.Moves[j] : new AccessoryMove();
                acc.AddMove[j, 0] = EditContext.U(mv.Position);
                acc.AddMove[j, 1] = EditContext.U(mv.Rotation);
                acc.AddMove[j, 2] = EditContext.U(mv.Scale);
            }
            for (int l = 0; l < 4; l++)
            {
                var c = e.Colors != null && l < e.Colors.Length && e.Colors[l] != null ? e.Colors[l] : new AccessoryColor();
                acc.Color[l] = EditContext.U(c.Color);
                acc.Gloss[l] = c.Gloss;
                acc.Metallic[l] = c.Metallic;
                acc.Smoothness[l] = c.Smoothness;
            }
            return acc;
        }

        /// <summary>A MoreAccessories part (slot >= 20) as a build slot.</summary>
        public static AccessorySlot FromMore(MoreAccessoriesData.AccessoryPart p, bool isCoordinate)
        {
            var acc = new AccessorySlot
            {
                Slot = p.Slot, Type = p.Type, Id = p.Id, ParentKey = p.ParentKey ?? "",
                HideCategory = p.HideCategory, HideTiming = p.HideTiming, NoShake = p.NoShake, Show = p.Show ?? true, Extra = true,
                Property = Card.ResolveInfo.AccessoryProperty(p.Slot, !isCoordinate),
            };
            for (int j = 0; j < 2; j++)
                for (int k = 0; k < 3; k++) acc.AddMove[j, k] = p.AddMove[j, k];
            for (int l = 0; l < 4; l++) { acc.Color[l] = p.Color[l]; acc.Gloss[l] = p.GlossPower[l]; acc.Metallic[l] = p.MetallicPower[l]; acc.Smoothness[l] = p.SmoothnessPower[l]; }
            return acc;
        }

        /// <summary>An empty slot 0..19 (the adapter's type 350 part) or >= 20 (MoreAccessories' empty part).</summary>
        public static AccessorySlot Empty(int slot)
        {
            if (slot >= MoreAccessoriesData.FirstSlot)
                return FromMore(new MoreAccessoriesData.AccessoryPart { Slot = slot }, false);
            var acc = new AccessorySlot { Slot = slot, Type = 350, Id = 0, ParentKey = "", HideCategory = 0, HideTiming = 1, NoShake = false, Property = $"outfit.accessory{slot}.ChaFileAccessory.PartsInfo.id" };
            for (int j = 0; j < 2; j++) { acc.AddMove[j, 0] = Vector3.zero; acc.AddMove[j, 1] = Vector3.zero; acc.AddMove[j, 2] = Vector3.one; }
            for (int l = 0; l < 4; l++) { acc.Color[l] = Color.white; acc.Gloss[l] = 0.5f; acc.Metallic[l] = 0.5f; }
            return acc;
        }

        /// <summary>Registers the reference of a slot's entry (the adapter's key acc:&lt;slot&gt;).</summary>
        public static void Register(EditContext x, AccessoryEntry e, int slot) =>
            x.Register(PartKeys.Accessory(slot), e.Item, e.Category, "coord.accessories[" + (e.Id ?? "?") + "].item");

        /// <summary>The two transform sets are equal value for value (float bits).</summary>
        public static bool SameMoves(Vector3[,] a, Vector3[,] b)
        {
            if (a == null || b == null) return a == b;
            for (int j = 0; j < 2; j++)
                for (int k = 0; k < 3; k++)
                {
                    Vector3 p = a[j, k], q = b[j, k];
                    if (System.BitConverter.SingleToInt32Bits(p.x) != System.BitConverter.SingleToInt32Bits(q.x) || System.BitConverter.SingleToInt32Bits(p.y) != System.BitConverter.SingleToInt32Bits(q.y)
                        || System.BitConverter.SingleToInt32Bits(p.z) != System.BitConverter.SingleToInt32Bits(q.z)) return false;
                }
            return true;
        }

        /// <summary>Equal for the purpose of a reload: type, row, anchor.</summary>
        public static bool SamePart(AccessorySlot a, AccessorySlot b) => a != null && b != null && a.Type == b.Type && a.Id == b.Id && a.ParentKey == b.ParentKey && a.Property == b.Property;
    }

    /// <summary>The slot table and, in the build, every slot (step 1, after the clothes).</summary>
    public sealed class AccessoryListUnit : UnitBase
    {
        public AccessoryListUnit() : base(UnitMap.AccList, EditStep.Parts, 40, "accessories") { }

        public override void Apply(EditContext x)
        {
            if (x.InBuild) { Build(x); return; }
            // An edit of the list (insert, remove, move): new slot numbers; every slot whose entry changed is reloaded (an
            // empty slot that stays empty is left alone, so the flush reports only the slots it changed).
            var coord = x.CoordDoc;
            var table = AccessorySlotTable.Assign(coord.Accessories, null);
            var old = x.Input.Slots;
            int max = System.Math.Max(table.MaxSlot, x.Ctx.Accessories.Count - 1);
            bool extOn = x.Ctx.Ext != null && x.Ctx.Options != null && x.Ctx.Options.Enabled && x.Ctx.Options.MoreAccessories;
            int last = extOn ? table.MaxSlot : System.Math.Min(table.MaxSlot, MoreAccessoriesData.FirstSlot - 1);
            int count = System.Math.Max(MoreAccessoriesData.FirstSlot, last + 1);
            for (int s = 0; s <= max; s++)
            {
                table.EntryBySlot.TryGetValue(s, out var ne);
                AccessoryEntry oe = null;
                old?.EntryBySlot.TryGetValue(s, out oe);
                if (s >= count)
                {
                    // the slot no longer exists
                    if (s < x.Ctx.Accessories.Count) x.UnloadAccessory(s);
                    x.UnregisterPart(PartKeys.Accessory(s));
                    continue;
                }
                if (ne != null && ReferenceEquals(ne, oe) && s < x.Ctx.Accessories.Count) continue;   // same entry at the same slot
                // an empty slot that stays empty and holds no object: nothing to reload or report
                if (ne == null && oe == null && s < x.Ctx.Accessories.Count && x.Ctx.Accessories[s] is AccessorySlot cur && cur.IsEmpty && !cur.Object) continue;
                if (ne != null) AccessorySlots.Register(x, ne, s); else x.UnregisterPart(PartKeys.Accessory(s));
                var view = ne != null ? AccessorySlots.FromEntry(ne, s) : AccessorySlots.Empty(s);
                x.ReloadAccessory(view, ne?.Id);
            }
            while (x.Ctx.Accessories.Count > count) x.Ctx.Accessories.RemoveAt(x.Ctx.Accessories.Count - 1);
            x.Input.Slots = table;
            x.UpdateExtraSlotStats();
            // ids no longer in the list lose their units
            var ids = new HashSet<string>(coord.Accessories.Where(a => a?.Id != null).Select(a => a.Id));
            foreach (var u in x.Editor.Units.ToList())
                if (UnitMap.IsAccessory(u.Id, out string id) && !ids.Contains(id)) x.Editor.Unregister(u.Id);
            x.MarkDynBone(true);
            x.Editor.MarkUnit(UnitMap.ExtFinal);
        }

        static void Build(EditContext x)
        {
            var ctx = x.Ctx;
            ctx.Accessories.Clear();
            List<AccessorySlot> extra = null;
            bool extOn = ctx.Ext != null && ctx.Options != null && ctx.Options.Enabled && ctx.Options.MoreAccessories;
            var slots = x.Input.Slots ?? AccessorySlotTable.Assign(x.CoordDoc.Accessories, null);
            x.Input.Slots = slots;
            for (int s = 0; s < MoreAccessoriesData.FirstSlot; s++)
                ctx.Accessories.Add(slots.EntryBySlot.TryGetValue(s, out var e) ? AccessorySlots.FromEntry(e, s) : AccessorySlots.Empty(s));
            if (extOn && slots.MaxSlot >= MoreAccessoriesData.FirstSlot)
            {
                extra = new List<AccessorySlot>();
                for (int s = MoreAccessoriesData.FirstSlot; s <= slots.MaxSlot; s++)
                    extra.Add(slots.EntryBySlot.TryGetValue(s, out var e) ? AccessorySlots.FromEntry(e, s) : AccessorySlots.Empty(s));
            }
            foreach (var acc in ctx.Accessories.ToList()) x.LoadAccessory(acc);
            if (extra == null) return;
            // MoreAccessories: slots 20+ through the same loader, after slots 0..19.
            var st = ctx.Stats;
            try
            {
                foreach (var acc in extra)
                {
                    while (ctx.Accessories.Count <= acc.Slot) ctx.Accessories.Add(new AccessorySlot { Slot = ctx.Accessories.Count, Type = MoreAccessoriesData.EmptyType });
                    ctx.Accessories[acc.Slot] = acc;
                    if (acc.IsEmpty) continue;
                    st.ExtraSlotsUsed++;
                    var go = x.LoadAccessory(acc);
                    if (!go) continue;
                    st.ExtraSlotsLoaded++;
                    // show=false is read data: the import folds it into the scene's state.accessories, which decides the
                    // object's visibility (the build does not hide it, so the scene's apply cannot undo a hide)
                    if (!acc.Show) st.ExtraSlotsHidden++;
                }
                ctx.Log.Add($"moreAccessories: {st.ExtraSlotsLoaded} of {st.ExtraSlotsUsed} used extra slots loaded ({extra.Count} elements){(st.ExtraSlotsHidden > 0 ? $", {st.ExtraSlotsHidden} with show=false (scene state)" : "")}");
            }
            catch (System.Exception e)
            {
                string msg = $"moreAccessories: {e.GetType().Name}: {e.Message}";
                st.Errors.Add(msg);
                ctx.Log.Add("ext error " + msg);
            }
        }
    }

    /// <summary>One accessory by id (edits; step 1 after the list unit).</summary>
    public sealed class AccessoryUnit : UnitBase
    {
        readonly string accId;
        string appliedReload;

        public AccessoryUnit(string id) : base(UnitMap.Accessory(id), EditStep.Parts, 50, "accessories[\"" + id + "\"]") { accId = id; }

        public override void Apply(EditContext x)
        {
            var e = x.CoordDoc.FindAccessory(accId);
            var slots = x.Input.Slots;
            if (e == null || slots == null || !slots.SlotOf.TryGetValue(e, out int slot)) return;   // removed (acc.list handles it)
            if (slot >= x.Ctx.Accessories.Count) return;   // beyond the loaded slots (MoreAccessories off)
            AccessorySlots.Register(x, e, slot);
            var cur = x.Ctx.Accessories[slot];
            var view = AccessorySlots.FromEntry(e, slot);
            string reload = Signature.Of(e.MaterialOverrides) + "|" + Signature.Of(e.DynamicBones);
            if (appliedReload == null) appliedReload = reload;
            // the row as the loader resolves it (ids of different mods can be equal: compare rows, not raw ids)
            var row = view.IsEmpty ? null : x.Lookup(view.Type, view.Id, view.Property, PartKeys.Accessory(slot));
            if (x.ForceReload.Remove(slot) || row != cur.Entry || !AccessorySlots.SamePart(cur, view) || reload != appliedReload || !cur.Object && !view.IsEmpty)
            {
                appliedReload = reload;
                x.ReloadAccessory(view, accId);
                return;
            }
            // same part: moves, colours, flags on the loaded object
            bool moved = !AccessorySlots.SameMoves(cur.AddMove, view.AddMove);
            cur.AddMove = view.AddMove; cur.Color = view.Color; cur.Gloss = view.Gloss; cur.Metallic = view.Metallic; cur.Smoothness = view.Smoothness;
            cur.HideCategory = view.HideCategory; cur.HideTiming = view.HideTiming;
            bool noShake = cur.NoShake != view.NoShake;
            cur.NoShake = view.NoShake;
            if (cur.Object) x.ApplyAccessoryValues(cur);
            if (noShake) x.MarkDynBone(true);
            // a transform set moves, turns or scales the slot's chain bones: their rest pose and tip offsets are captured
            // again from the new pose (the light re-derivation, deferred during a drag), as a fresh build captures them
            else if (moved && cur.Object && cur.Object.GetComponentInChildren<global::DynamicBone>(true)) x.MarkDynBone(false);
            x.Touched(EditObject.Accessory(slot));
        }
    }
}
