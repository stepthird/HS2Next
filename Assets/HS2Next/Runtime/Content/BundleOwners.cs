// Bundle ownership (engine-free bookkeeping; the catalog holds one ledger and does the unloading). Every bundle the catalog
// hands out or opens while an owner's scope is active is recorded under that owner: a map, an item node, a character's base
// build, an edit unit's generation, the anime, the thumbnails, a pin. A load outside every scope is recorded under the
// "unscoped" owner, which is never released. When an owner is released, each of its bundles that has no other live owner
// goes to the head of an LRU of unowned bundles; while the LRU holds more resident bytes than the budget, the catalog evicts
// from its tail. The LRU weighs resident bytes only: an eviction frees only those, and the objects loaded from a bundle are
// freed by UnloadUnusedAssets whether the bundle is open or not (their estimated bytes are reported and trigger that call;
// weighing them would evict large shared bundles that free nothing and duplicate their objects when they are reopened).
// Eviction never destroys a live object (the catalog unloads without destroying loaded objects), so a missed owner
// costs memory, not correctness. Never evicted: a pinned bundle, a bundle with an open, header read or asset request in
// flight, and a bundle opened in the current tick. Main thread only.
using System;
using System.Collections.Generic;

namespace HS2Next.Content
{
    /// <summary>What an owner is; pins, the thumbnails and "unscoped" are never released by the ledger.</summary>
    public enum BundleOwnerKind { Normal, Pin, Thumbs, Unscoped }

    /// <summary>An owner of bundles (a token with a label and a live flag).</summary>
    public sealed class BundleOwner
    {
        internal BundleOwner(string label, BundleOwnerKind kind) { Label = label; Kind = kind; Live = true; }

        /// <summary>"map", "item:&lt;node id 8&gt;", "chara:&lt;node id 8&gt;/&lt;unit&gt;#&lt;generation&gt;", "pin:&lt;reason&gt;", ...</summary>
        public string Label { get; }
        public BundleOwnerKind Kind { get; }
        /// <summary>False once released (a released owner records nothing more).</summary>
        public bool Live { get; internal set; }
        /// <summary>The bundles it owns now.</summary>
        public int Count => keys.Count;

        // the keys it owns, in the order of first use (a release moves them to the LRU in this order)
        readonly HashSet<string> keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly List<string> order = new List<string>();
        internal void AddKey(string key) { if (keys.Add(key)) order.Add(key); }
        internal void RemoveKey(string key) { if (keys.Remove(key)) order.Remove(key); }
        internal List<string> KeysInOrder() => new List<string>(order);
        internal void ClearKeys() { keys.Clear(); order.Clear(); }

        public override string ToString() => Label + (Live ? "" : " (released)");
    }

    /// <summary>A snapshot of the ledger's numbers.</summary>
    public sealed class BundleLedgerStats
    {
        public int Open, Owned, Lru, Pinned, ThumbnailOnly, Unscoped, Busy, Owners;
        public long OwnedBytes, LruBytes, BudgetBytes;
        public int Evictions, ReloadedAssets, UnscopedLoads, Releases;
    }

    public sealed class BundleLedger
    {
        public const long MiB = 1024L * 1024L;

        sealed class Slot
        {
            public string Key;
            public readonly List<BundleOwner> Owners = new List<BundleOwner>(2);   // live owners
            public bool EverOwned;          // had an owner other than the thumbnails
            public long Resident, Objects;
            public int Busy;
            public long OpenedTick = -1;
            public HashSet<string> Assets;  // asset names loaded from it (for the reload count after an eviction)
            public bool Measured;           // Resident / Objects set (by SetBytes or the Measure callback)
            public long Bytes => Resident + Objects;   // reports; the LRU weighs Resident
        }

        readonly Dictionary<string, Slot> slots = new Dictionary<string, Slot>(StringComparer.OrdinalIgnoreCase);
        readonly BundleLru lru = new BundleLru();
        readonly List<BundleOwner> stack = new List<BundleOwner>();
        readonly Dictionary<string, BundleOwner> pins = new Dictionary<string, BundleOwner>(StringComparer.OrdinalIgnoreCase);
        // asset names loaded from bundles before they were evicted: key -> names (a later load of one counts as a reload)
        readonly Dictionary<string, HashSet<string>> evictedAssets = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        int liveOwners;

        public BundleLedger()
        {
            Unscoped = new BundleOwner("unscoped", BundleOwnerKind.Unscoped);
            Thumbs = new BundleOwner("thumbs", BundleOwnerKind.Thumbs);
        }

        /// <summary>The owner of loads outside every scope (never released).</summary>
        public BundleOwner Unscoped { get; }
        /// <summary>The owner of bundles opened by thumbnail requests and not used by anything else (the thumbnail LRU unloads them).</summary>
        public BundleOwner Thumbs { get; }

        /// <summary>The byte budget of the unowned bundles (Σ resident bytes over the LRU; object bytes do not count).</summary>
        public long BudgetBytes { get; set; } = 768 * MiB;

        /// <summary>The current tick (the catalog advances it once per frame); a bundle opened in this tick is never evicted.</summary>
        public long Tick { get; private set; }

        public int Evictions { get; private set; }
        public int ReloadedAssets { get; private set; }
        public int UnscopedLoads { get; private set; }
        /// <summary>Releases of owners that owned at least one bundle.</summary>
        public int Releases { get; private set; }

        /// <summary>The LRU of unowned bundles (read-only use).</summary>
        public BundleLru Lru => lru;

        /// <summary>
        /// Measures a bundle's resident and object bytes (key, whether an asset was loaded from it), called once per bundle when it
        /// enters the LRU or a report asks for its bytes, and again after its first asset load; null = sizes come from
        /// <see cref="SetBytes"/> only.
        /// </summary>
        public Func<string, bool, (long Resident, long Objects)> Measure;

        void EnsureMeasured(Slot s)
        {
            if (s.Measured || Measure == null) return;
            s.Measured = true;
            var (r, o) = Measure(s.Key, s.Assets != null && s.Assets.Count > 0);
            s.Resident = Math.Max(0, r);
            s.Objects = Math.Max(0, o);
        }

        void EnterLru(Slot s)
        {
            EnsureMeasured(s);
            ReleasedObjectBytes += s.Objects;
            lru.AddHead(s.Key, s.Resident);
        }

        /// <summary>The object bytes of every bundle that entered the LRU so far (a trigger of UnloadUnusedAssets).</summary>
        public long ReleasedObjectBytes { get; private set; }

        public void NextTick() => Tick++;

        // ------------------------------------------------------------ owners and scopes

        /// <summary>A new live owner.</summary>
        public BundleOwner NewOwner(string label)
        {
            liveOwners++;
            return new BundleOwner(label ?? "owner", BundleOwnerKind.Normal);
        }

        /// <summary>The pin owner of <paramref name="reason"/> (one per reason; never released).</summary>
        public BundleOwner Pin(string reason)
        {
            reason = reason ?? "";
            if (!pins.TryGetValue(reason, out var p)) pins[reason] = p = new BundleOwner("pin:" + reason, BundleOwnerKind.Pin);
            return p;
        }

        /// <summary>The innermost active scope's owner, or null outside every scope.</summary>
        public BundleOwner Current => stack.Count > 0 ? stack[stack.Count - 1] : null;

        /// <summary>The number of active scopes.</summary>
        public int Depth => stack.Count;

        /// <summary>Makes <paramref name="owner"/> the owner of what is handed out until the scope is disposed (scopes nest; the
        /// innermost wins). A null or released owner still pushes a scope: its loads go to "unscoped".</summary>
        public Scope Own(BundleOwner owner)
        {
            stack.Add(owner);
            return new Scope(this, owner, stack.Count);
        }

        public readonly struct Scope : IDisposable
        {
            readonly BundleLedger ledger;
            readonly BundleOwner owner;
            readonly int depth;
            internal Scope(BundleLedger l, BundleOwner o, int d) { ledger = l; owner = o; depth = d; }
            public void Dispose() => ledger?.Pop(owner, depth);
        }

        void Pop(BundleOwner owner, int depth)
        {
            // normally the top; a scope disposed out of order removes its own entry only
            if (stack.Count == depth && ReferenceEquals(stack[depth - 1], owner)) { stack.RemoveAt(depth - 1); return; }
            for (int i = stack.Count - 1; i >= 0; i--)
                if (ReferenceEquals(stack[i], owner)) { stack.RemoveAt(i); return; }
        }

        /// <summary>The owner a use records now: the pin when <paramref name="pin"/> names one, else the innermost live scope owner,
        /// else "unscoped".</summary>
        public BundleOwner OwnerForUse(string pin)
        {
            if (pin != null) return Pin(pin);
            var c = Current;
            return c != null && c.Live ? c : Unscoped;
        }

        // ------------------------------------------------------------ bundles

        Slot SlotOf(string key)
        {
            if (!slots.TryGetValue(key, out var s)) slots[key] = s = new Slot { Key = key };
            return s;
        }

        /// <summary>True when the ledger knows <paramref name="key"/> (an open bundle).</summary>
        public bool Knows(string key) => key != null && slots.ContainsKey(key);

        /// <summary>A bundle was opened now (it cannot be evicted in this tick).</summary>
        public void Opened(string key)
        {
            if (key == null) return;
            SlotOf(key).OpenedTick = Tick;
        }

        /// <summary>
        /// A bundle handed out (or opened) for <paramref name="owner"/>: recorded under it (a bundle in the LRU leaves it). A use by
        /// "unscoped" counts in <see cref="UnscopedLoads"/>. A released owner records nothing (its use goes to "unscoped").
        /// </summary>
        public void Use(string key, BundleOwner owner)
        {
            if (key == null) return;
            if (owner == null || !owner.Live) owner = Unscoped;
            var s = SlotOf(key);
            if (owner.Kind == BundleOwnerKind.Unscoped) UnscopedLoads++;
            if (owner.Kind != BundleOwnerKind.Thumbs) s.EverOwned = true;
            if (!s.Owners.Contains(owner)) s.Owners.Add(owner);
            owner.AddKey(key);
            lru.Remove(key);
        }

        /// <summary>Removes <paramref name="owner"/> from one bundle (e.g. the thumbnails' mark cleared by a synchronous use); a bundle
        /// left without a live owner enters the LRU.</summary>
        public void Disown(string key, BundleOwner owner)
        {
            if (key == null || owner == null || !slots.TryGetValue(key, out var s)) return;
            if (s.Owners.Remove(owner)) owner.RemoveKey(key);
            if (s.Owners.Count == 0) EnterLru(s);
        }

        /// <summary>True when the bundle had an owner other than the thumbnails (the CAB rule's "handed out").</summary>
        public bool EverOwned(string key) => key != null && slots.TryGetValue(key, out var s) && s.EverOwned;

        /// <summary>True when one of the bundle's live owners is <paramref name="owner"/>.</summary>
        public bool IsOwnedBy(string key, BundleOwner owner) => key != null && slots.TryGetValue(key, out var s) && s.Owners.Contains(owner);

        /// <summary>The labels of the bundle's live owners.</summary>
        public List<string> OwnersOf(string key)
        {
            var l = new List<string>();
            if (key != null && slots.TryGetValue(key, out var s)) foreach (var o in s.Owners) l.Add(o.Label);
            return l;
        }

        /// <summary>The bundle's state: "pinned", "owned", "thumbnail", "unscoped", "lru" or "unowned"; null when unknown.</summary>
        public string StateOf(string key)
        {
            if (key == null || !slots.TryGetValue(key, out var s)) return null;
            if (lru.Contains(key)) return "lru";
            bool pin = false, normal = false, thumbs = false, unscoped = false;
            foreach (var o in s.Owners)
                switch (o.Kind)
                {
                    case BundleOwnerKind.Pin: pin = true; break;
                    case BundleOwnerKind.Normal: normal = true; break;
                    case BundleOwnerKind.Thumbs: thumbs = true; break;
                    default: unscoped = true; break;
                }
            return pin ? "pinned" : normal ? "owned" : unscoped ? "unscoped" : thumbs ? "thumbnail" : "unowned";
        }

        /// <summary>Sets the resident and object bytes of an open bundle (the resident bytes are its weight in the LRU).</summary>
        public void SetBytes(string key, long resident, long objects)
        {
            if (key == null) return;
            var s = SlotOf(key);
            s.Resident = Math.Max(0, resident);
            s.Objects = Math.Max(0, objects);
            s.Measured = true;
            lru.SetBytes(key, s.Resident);
        }

        public long ResidentOf(string key) => key != null && slots.TryGetValue(key, out var s) ? s.Resident : 0;
        public long ObjectsOf(string key) => key != null && slots.TryGetValue(key, out var s) ? s.Objects : 0;

        /// <summary>An open, a header read or an asset request of the bundle started (+1) or ended (-1): never evicted meanwhile.</summary>
        public void Busy(string key, int delta)
        {
            if (key == null) return;
            var s = SlotOf(key);
            s.Busy = Math.Max(0, s.Busy + delta);
        }

        /// <summary>An asset was loaded from the bundle: remembered for the reload count; a name loaded before the bundle was
        /// evicted counts once in <see cref="ReloadedAssets"/>.</summary>
        public void AssetLoaded(string key, string asset)
        {
            if (key == null || asset == null) return;
            var s = SlotOf(key);
            if (s.Assets == null || s.Assets.Count == 0) s.Measured = false;   // its objects count from now on
            (s.Assets ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase)).Add(asset);
            if (evictedAssets.TryGetValue(key, out var gone) && gone.Remove(asset))
            {
                ReloadedAssets++;
                if (gone.Count == 0) evictedAssets.Remove(key);
            }
        }

        /// <summary>
        /// Releases <paramref name="owner"/>: it records nothing more, and each of its bundles without another live owner goes to
        /// the head of the LRU (in the owner's order of first use). Pins, the thumbnails and "unscoped" are not released.
        /// Returns the number of bundles that entered the LRU.
        /// </summary>
        public int Release(BundleOwner owner)
        {
            if (owner == null || !owner.Live || owner.Kind != BundleOwnerKind.Normal) return 0;
            owner.Live = false;
            liveOwners--;
            if (owner.Count > 0) Releases++;
            int n = 0;
            foreach (var key in owner.KeysInOrder())
            {
                if (!slots.TryGetValue(key, out var s)) continue;
                s.Owners.Remove(owner);
                if (s.Owners.Count == 0) { EnterLru(s); n++; }
            }
            owner.ClearKeys();
            return n;
        }

        /// <summary>
        /// The bundles to evict now: from the LRU's tail while its resident bytes exceed the budget, skipping busy bundles and bundles opened
        /// in this tick. They leave the ledger (the caller unloads each without destroying its objects) and count in
        /// <see cref="Evictions"/>. <paramref name="budget"/> overrides <see cref="BudgetBytes"/> when not negative.
        /// </summary>
        public List<string> TakeEvictions(long budget = -1)
        {
            if (budget < 0) budget = BudgetBytes;
            var out_ = new List<string>();
            // a budget of 0 empties the LRU (bundles of unknown size weigh 0 bytes)
            bool Over() => budget == 0 ? lru.Count > 0 : lru.Bytes > budget;
            if (!Over()) return out_;
            foreach (var key in lru.FromTail())
            {
                if (!Over()) break;
                if (!slots.TryGetValue(key, out var s)) { lru.Remove(key); continue; }
                if (s.Busy > 0 || s.OpenedTick == Tick) continue;
                lru.Remove(key);
                Drop(s);
                Evictions++;
                out_.Add(key);
            }
            return out_;
        }

        void Drop(Slot s)
        {
            slots.Remove(s.Key);
            foreach (var o in s.Owners) o.RemoveKey(s.Key);
            if (s.Assets != null && s.Assets.Count > 0)
            {
                if (!evictedAssets.TryGetValue(s.Key, out var gone)) evictedAssets[s.Key] = gone = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                gone.UnionWith(s.Assets);
            }
        }

        /// <summary>The bundle was unloaded by another rule (its objects destroyed with it): forgotten everywhere, not an eviction.</summary>
        public void Forget(string key)
        {
            if (key == null || !slots.TryGetValue(key, out var s)) return;
            lru.Remove(key);
            slots.Remove(key);
            foreach (var o in s.Owners) o.RemoveKey(key);
        }

        /// <summary>Every bundle unloaded (the owners and pins stay, owning nothing).</summary>
        public void ForgetAll()
        {
            foreach (var s in slots.Values) foreach (var o in s.Owners) o.ClearKeys();
            slots.Clear();
            lru.Clear();
            evictedAssets.Clear();
        }

        /// <summary>The numbers now; every open bundle is measured first (see <see cref="Measure"/>).</summary>
        public BundleLedgerStats Stats()
        {
            foreach (var x in slots.Values) if (!x.Measured) { EnsureMeasured(x); if (lru.Contains(x.Key)) lru.SetBytes(x.Key, x.Resident); }
            var st = new BundleLedgerStats { Open = slots.Count, Lru = lru.Count, LruBytes = lru.Bytes, BudgetBytes = BudgetBytes,
                Evictions = Evictions, ReloadedAssets = ReloadedAssets, UnscopedLoads = UnscopedLoads, Releases = Releases, Owners = liveOwners };
            foreach (var s in slots.Values)
            {
                if (s.Busy > 0) st.Busy++;
                switch (StateOf(s.Key))
                {
                    case "pinned": st.Pinned++; st.OwnedBytes += s.Bytes; break;
                    case "owned": st.Owned++; st.OwnedBytes += s.Bytes; break;
                    case "unscoped": st.Unscoped++; st.OwnedBytes += s.Bytes; break;
                    case "thumbnail": st.ThumbnailOnly++; break;
                }
            }
            return st;
        }

        /// <summary>The open bundles with their owners and bytes, largest first (at most <paramref name="top"/>).</summary>
        public List<(string Key, List<string> Owners, long Resident, long Objects, string State)> Largest(int top)
        {
            var l = new List<Slot>(slots.Values);
            foreach (var x in l) if (!x.Measured) { EnsureMeasured(x); if (lru.Contains(x.Key)) lru.SetBytes(x.Key, x.Resident); }
            l.Sort((a, b) => b.Bytes != a.Bytes ? b.Bytes.CompareTo(a.Bytes) : string.CompareOrdinal(a.Key, b.Key));
            var r = new List<(string, List<string>, long, long, string)>();
            for (int i = 0; i < l.Count && i < top; i++) r.Add((l[i].Key, OwnersOf(l[i].Key), l[i].Resident, l[i].Objects, StateOf(l[i].Key)));
            return r;
        }
    }

    /// <summary>
    /// The generations of one owner that changes (a character's edit unit, the anime): each run that may load gets a new
    /// generation; when the run recorded a bundle, the previous generation is released (the bundles the new run used moved to
    /// the new one already); a run that recorded nothing is dropped and the previous generation stays — unless the run loads
    /// everything it uses each time (then it uses no bundle any more and the previous generation is released too).
    /// </summary>
    public sealed class OwnerGenerations
    {
        readonly BundleLedger ledger;
        readonly string label;

        public OwnerGenerations(BundleLedger ledger, string label)
        {
            this.ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            this.label = label ?? "owner";
        }

        /// <summary>The generation that owns the unit's bundles now (null before the first run that loaded).</summary>
        public BundleOwner Current { get; private set; }

        /// <summary>The number of the current generation (0 before the first).</summary>
        public int Generation { get; private set; }

        /// <summary>The owner of the next run (label "&lt;label&gt;#&lt;n&gt;").</summary>
        public BundleOwner Begin() => ledger.NewOwner(label + "#" + (Generation + 1));

        /// <summary>Ends a run begun with <see cref="Begin"/>: true when it became the current generation.
        /// <paramref name="loadsAll"/>: the run loads everything it uses (a run that recorded nothing then releases the
        /// previous generation as well).</summary>
        public bool End(BundleOwner next, bool loadsAll = false)
        {
            if (next == null) return false;
            if (next.Count == 0)
            {
                ledger.Release(next);   // owns nothing: only its live flag goes
                if (loadsAll && Current != null)
                {
                    ledger.Release(Current);
                    Current = null;
                }
                return false;
            }
            var prev = Current;
            Current = next;
            Generation++;
            if (prev != null) ledger.Release(prev);
            return true;
        }

        /// <summary>Releases the current generation (the character is gone).</summary>
        public void ReleaseAll()
        {
            if (Current != null) ledger.Release(Current);
            Current = null;
        }
    }
}
