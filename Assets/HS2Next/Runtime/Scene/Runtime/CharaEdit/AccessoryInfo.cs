// The Unity layer's accessory facts (the accessory pages of the Maker; package A): per node and build slot the facts of the
// realized slot object — whether it has a CmpAccessory, hair-type, the colour areas, the alpha renderers, the two transform
// sets, the row's Parent column, dynamic bones and colliders — read from the realization (CharaContext.Accessories through
// AccessoryReport), so the page shows what is drawn. A node without a built realization (a card-backed node, a failed load,
// not built yet) has no facts. The node is resolved over the scene runtimes this class was shown: Track(rt) (weak, newest
// first; called by the callers that have a runtime, e.g. StudioOps.MakerGateHolds) or the static TrySlotReport(rt, …),
// which tracks too. The facts of a character are cached until its editor flushes again (CachedFacts); the reports are not.
// Implements IAccessoryInfo (MakerServices.Accessories) with the Unity-free AccessoryFacts.
using System;
using System.Collections.Generic;
using HS2Next.Chara;
using HS2Next.Chara.Edit;
using HS2Next.Save;

namespace HS2Next.Scenes.Runtime
{
    public sealed class AccessoryInfo : IAccessoryInfo
    {
        /// <summary>The instance MakerSetup registers (one per app domain).</summary>
        public static readonly AccessoryInfo Instance = new AccessoryInfo();

        /// <summary>IAccessoryInfo: the facts of build slot <paramref name="slot"/> (0-based) of node <paramref name="nodeId"/>'s
        /// built character in the tracked runtimes (newest first) — the realized slot object's CmpAccessory, the row's Parent
        /// column and the prefab's default colours; false when not built, the slot holds no accessory or its object is not loaded
        /// (an unresolved row, a MoreAccessories slot while MoreAccessories is off).</summary>
        public bool TrySlot(string nodeId, int slot, out AccessoryFacts facts)
        {
            facts = null;
            if (string.IsNullOrEmpty(nodeId) || slot < 0) return false;
            foreach (var rt in Tracked())
            {
                var ctx = Built(rt, nodeId);
                if (ctx != null) { facts = CachedAccessoryFacts(ctx, slot); return facts != null; }
            }
            return false;
        }

        /// <summary>IAccessoryInfo: the build slot and facts of accessory <paramref name="accessoryId"/> (as <see cref="TrySlot"/>);
        /// the slot is set (−1 when the build's table has none) also when the facts are not there.</summary>
        public bool TryAccessory(string nodeId, string accessoryId, out int slot, out AccessoryFacts facts)
        {
            slot = -1; facts = null;
            if (string.IsNullOrEmpty(nodeId) || accessoryId == null) return false;
            foreach (var rt in Tracked())
            {
                var ctx = Built(rt, nodeId);
                if (ctx == null) continue;
                slot = AccessoryReport.SlotOfAccessory(ctx, accessoryId);
                if (slot < 0) return false;
                facts = CachedAccessoryFacts(ctx, slot);
                return facts != null;
            }
            return false;
        }

        /// <summary>The facts of build slot <paramref name="slot"/> of node <paramref name="nodeId"/>'s built character in the
        /// tracked runtimes (newest first); false when no tracked runtime has it built or the slot holds no accessory.</summary>
        public bool TrySlotFacts(string nodeId, int slot, out AccessoryReport.PrefabFacts facts)
        {
            facts = null;
            if (string.IsNullOrEmpty(nodeId) || slot < 0) return false;
            foreach (var rt in Tracked())
            {
                var ctx = Built(rt, nodeId);
                if (ctx != null) { facts = CachedFacts(ctx, slot); return facts != null; }
            }
            return false;
        }

        /// <summary>The build slot and facts of accessory <paramref name="accessoryId"/> of node <paramref name="nodeId"/>'s built
        /// character (tracked runtimes, newest first); false when not built or the build's table has no slot for the id.</summary>
        public bool TryAccessoryFacts(string nodeId, string accessoryId, out int slot, out AccessoryReport.PrefabFacts facts)
        {
            slot = -1; facts = null;
            if (string.IsNullOrEmpty(nodeId) || accessoryId == null) return false;
            foreach (var rt in Tracked())
            {
                var ctx = Built(rt, nodeId);
                if (ctx == null) continue;
                slot = AccessoryReport.SlotOfAccessory(ctx, accessoryId);
                if (slot < 0) return false;
                facts = CachedFacts(ctx, slot);
                return facts != null;
            }
            return false;
        }

        // ---- the facts cache: per built character, valid while its editor's flush count is unchanged (every change of what the
        // facts read — an item, category or parent change, a slot add / remove / move, a coordinate apply, an undo — goes through
        // the character's units and so through a flush that counts; a rebuild makes a new character context).

        sealed class Cached
        {
            public int Flushes = -1;
            public readonly Dictionary<int, Entry> Slots = new Dictionary<int, Entry>();
        }

        /// <summary>One slot's facts in both forms (null members: the slot holds no accessory / its object is not loaded).</summary>
        sealed class Entry
        {
            public AccessoryReport.PrefabFacts Prefab;
            public AccessoryFacts Facts;
        }

        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CharaContext, Cached> cache = new System.Runtime.CompilerServices.ConditionalWeakTable<CharaContext, Cached>();

        /// <summary>Diagnostic: false = every facts request builds the slot report again. Default true.</summary>
        public static bool UseCache = true;
        /// <summary>Facts built from a report / handed out from the cache since the start (diagnostics, tests).</summary>
        public static int FactsBuilt { get; private set; }
        public static int FactsFromCache { get; private set; }

        /// <summary>The facts of build slot <paramref name="slot"/> of a built character (a copy), from the cache while the
        /// character's editor has not flushed since they were read; null when the slot holds no accessory.</summary>
        public static AccessoryReport.PrefabFacts CachedFacts(CharaContext ctx, int slot) => CachedEntry(ctx, slot)?.Prefab?.Copy();

        /// <summary>The facts of build slot <paramref name="slot"/> as the Unity-free AccessoryFacts (a copy, cached as
        /// <see cref="CachedFacts"/>); null when the slot holds no accessory or its object is not loaded.</summary>
        public static AccessoryFacts CachedAccessoryFacts(CharaContext ctx, int slot) => CachedEntry(ctx, slot)?.Facts?.Clone();

        static Entry CachedEntry(CharaContext ctx, int slot)
        {
            if (ctx == null || slot < 0) return null;
            var ed = ctx.Editor;
            if (ed == null || !UseCache)
            {
                FactsBuilt++;
                return EntryOf(ctx, slot);
            }
            var c = cache.GetValue(ctx, _ => new Cached());
            if (c.Flushes != ed.Flushes)
            {
                c.Slots.Clear();
                c.Flushes = ed.Flushes;
            }
            if (!c.Slots.TryGetValue(slot, out var f))
            {
                f = c.Slots[slot] = EntryOf(ctx, slot);
                FactsBuilt++;
            }
            else FactsFromCache++;
            return f;
        }

        static Entry EntryOf(CharaContext ctx, int slot)
        {
            var r = AccessoryReport.SlotOf(ctx, slot, false, false);
            if (r == null || r.Category < 351 || r.Category > 363) return new Entry();
            return new Entry { Prefab = r.Facts, Facts = r.Loaded ? ToFacts(r) : null };
        }

        /// <summary>The Unity-free facts of a slot report (the realized slot object's CmpAccessory, the row's columns and the
        /// prefab's default colours).</summary>
        public static AccessoryFacts ToFacts(AccessoryReport.SlotReport r)
        {
            var f = AccessoryFacts.None();
            if (r == null) return f;
            f.Category = r.Category;
            f.Item = r.Item;
            f.PrefabName = r.Columns.TryGetValue("MainData", out var md) ? md : null;
            f.ParentColumn = r.Facts.ParentColumn;
            f.HasCmpAccessory = r.Facts.HasCmpAccessory;
            f.DynamicBones = r.Facts.DynamicBones;
            f.Colliders = r.Facts.Colliders;
            if (!r.Facts.HasCmpAccessory) return f;
            f.TypeHair = r.Facts.TypeHair;
            f.UseColor = (bool[])r.Facts.UseColor.Clone();
            f.AlphaRenderers = r.Facts.AlphaRenderers;
            f.Move1 = r.Facts.Move1;
            f.Move2 = r.Facts.Move2;
            f.DefaultColors = r.Defaults;
            return f;
        }

        static readonly List<WeakReference<SceneRuntime>> runtimes = new List<WeakReference<SceneRuntime>>();

        /// <summary>Makes <paramref name="rt"/> the first runtime the node lookups look in (kept weakly; disposed runtimes are
        /// dropped).</summary>
        public static void Track(SceneRuntime rt)
        {
            if (rt == null || rt.IsDisposed) return;
            lock (runtimes)
            {
                for (int i = runtimes.Count - 1; i >= 0; i--)
                    if (!runtimes[i].TryGetTarget(out var r) || r == null || r.IsDisposed || ReferenceEquals(r, rt)) runtimes.RemoveAt(i);
                runtimes.Add(new WeakReference<SceneRuntime>(rt));
            }
        }

        /// <summary>The report of build slot <paramref name="slot"/> of node <paramref name="nodeId"/> in the tracked runtimes
        /// (newest first); false when no tracked runtime has it built.</summary>
        public bool TrySlotReport(string nodeId, int slot, out AccessoryReport.SlotReport report, bool served = false)
        {
            report = null;
            if (string.IsNullOrEmpty(nodeId) || slot < 0) return false;
            foreach (var rt in Tracked())
            {
                var ctx = Built(rt, nodeId);
                if (ctx != null) { report = AccessoryReport.SlotOf(ctx, slot, served); return report != null; }
            }
            return false;
        }

        /// <summary>The report of build slot <paramref name="slot"/> of node <paramref name="nodeId"/> in <paramref name="rt"/>
        /// (tracks the runtime); false when the node is not a built character there.</summary>
        public static bool TrySlotReport(SceneRuntime rt, string nodeId, int slot, out AccessoryReport.SlotReport report, bool served = false)
        {
            report = null;
            Track(rt);
            if (slot < 0) return false;
            var ctx = Built(rt, nodeId);
            if (ctx == null) return false;
            report = AccessoryReport.SlotOf(ctx, slot, served);
            return report != null;
        }

        /// <summary>The report of the slot holding accessory <paramref name="accessoryId"/> of node <paramref name="nodeId"/> in
        /// <paramref name="rt"/> (tracks the runtime); false when not built there or the id has no build slot.</summary>
        public static bool TryAccessoryReport(SceneRuntime rt, string nodeId, string accessoryId, out AccessoryReport.SlotReport report, bool served = false)
        {
            report = null;
            Track(rt);
            var ctx = Built(rt, nodeId);
            if (ctx == null) return false;
            report = AccessoryReport.AccessoryOf(ctx, accessoryId, served);
            return report != null;
        }

        /// <summary>The whole accessory report of node <paramref name="nodeId"/> in <paramref name="rt"/>; null when the node is
        /// not a built character there.</summary>
        public static AccessoryReport ReportOf(SceneRuntime rt, string nodeId, bool served = false)
        {
            var ctx = Built(rt, nodeId);
            return ctx == null ? null : AccessoryReport.Of(ctx, served);
        }

        /// <summary>The built character of node <paramref name="nodeId"/> in <paramref name="rt"/> (null when not built there).</summary>
        public static CharaContext ContextOf(SceneRuntime rt, string nodeId) => Built(rt, nodeId);

        static SceneRuntime[] Tracked()
        {
            lock (runtimes)
            {
                var l = new List<SceneRuntime>();
                for (int i = runtimes.Count - 1; i >= 0; i--)
                {
                    if (runtimes[i].TryGetTarget(out var r) && r != null && !r.IsDisposed) l.Add(r);
                    else runtimes.RemoveAt(i);
                }
                return l.ToArray();
            }
        }

        static CharaContext Built(SceneRuntime rt, string nodeId)
        {
            if (rt == null || rt.IsDisposed || nodeId == null) return null;
            var c = rt.FindChara(nodeId);
            if (c == null || c.State != RealizationState.Built || c.InGraveyard || !c.Root) return null;
            return c.Context;
        }
    }
}
