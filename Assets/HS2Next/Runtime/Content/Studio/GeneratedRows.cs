// Generated item groups: item groups of the Studio list whose rows come from a provider (the app, or a plugin) instead of a list
// file, and whose row objects a function of the provider makes for each item build. One registry per Studio list database
// (StudioDb.Generated).
//
// A group has a number, a name, an owner, categories (number -> name) and rows. A row is a StudioItem with its group, category,
// key "<guid>#<id>" (a plugin's rows carry its manifest GUID; the app's rows hs2next.bake or hs2next.cut), name and FK bone names,
// and no bundle. The rows are a table of their own, not part of StudioDb.Items (as the accessory rows): the group and its
// categories join the item group space, so ItemGroups, CategoriesOf, ItemsIn, item.search and the lookups by (guid, group, category,
// id) list and find them. A group sorts after every list group, groups among themselves in registration order.
//
// A group number that the lists define (a group, or a category or a row naming it), the accessory group or another provider holds
// is refused (rows.groupTaken). Adding, replacing or refreshing rows touches only this registry: Revision changes (the list browser
// reads its rows again) and Changed names the rows whose items are built again (rows that are new or changed, a refreshed row); a
// removed group or row changes nothing that is built. Main thread only.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using HS2Next.Save;
using HS2Next.Scenes.Bake;
using UnityEngine;

namespace HS2Next.Content.Studio
{
    /// <summary>What a generated group's function is asked for: the object of one row for one item build.</summary>
    public sealed class GeneratedRowRequest
    {
        public GeneratedRowRequest(StudioItem row, string nodeId)
        {
            Row = row;
            NodeId = nodeId;
        }

        /// <summary>The row.</summary>
        public StudioItem Row { get; }
        /// <summary>The GUID of the row's key.</summary>
        public string Guid => Row?.Key.SourceGuid;
        public int Group => Row?.Group ?? 0;
        public int Category => Row?.Category ?? 0;
        /// <summary>The id of the row's key.</summary>
        public int Id => Row?.Key.Id ?? 0;
        /// <summary>The scene node being built.</summary>
        public string NodeId { get; }
    }

    /// <summary>One row a provider gives. <see cref="Guid"/> null: the group's row GUID.</summary>
    public struct GeneratedRowSpec
    {
        public string Guid;
        public int Category;
        public int Id;
        public string Name;
        public IReadOnlyList<string> FkBones;

        public GeneratedRowSpec(int category, int id, string name, IReadOnlyList<string> fkBones = null, string guid = null)
        {
            Guid = guid;
            Category = category;
            Id = id;
            Name = name;
            FkBones = fkBones;
        }
    }

    /// <summary>What changed in a generated group.</summary>
    public enum GeneratedChangeKind
    {
        /// <summary>The group was added (Keys: every row).</summary>
        Added,
        /// <summary>Rows were set (Keys: the rows that are new or whose name, category or FK bones changed).</summary>
        Rows,
        /// <summary>A row's items are to be built again (Keys: that row).</summary>
        Refresh,
        /// <summary>The group was removed (Keys: empty). Items built from it keep their objects until their next build.</summary>
        Removed,
    }

    /// <summary>One change of a generated group: the rows whose scene items are built again.</summary>
    public sealed class GeneratedChange
    {
        public GeneratedChange(int group, GeneratedChangeKind kind, IReadOnlyCollection<ContentId> keys)
        {
            Group = group;
            Kind = kind;
            Keys = keys ?? (IReadOnlyCollection<ContentId>)Array.Empty<ContentId>();
        }

        public int Group { get; }
        public GeneratedChangeKind Kind { get; }
        /// <summary>The row keys whose items are built again.</summary>
        public IReadOnlyCollection<ContentId> Keys { get; }
    }

    /// <summary>A generated item group (see the file header).</summary>
    public sealed class GeneratedGroup
    {
        readonly GeneratedRows registry;
        readonly Func<GeneratedRowRequest, GameObject> make;
        // the rows by category (category order), each category's rows in the given order; one index by key
        readonly SortedDictionary<int, List<StudioItem>> byCategory = new SortedDictionary<int, List<StudioItem>>();
        readonly Dictionary<ContentId, StudioItem> byKey = new Dictionary<ContentId, StudioItem>();
        readonly List<StudioItem> rows = new List<StudioItem>();

        internal GeneratedGroup(GeneratedRows registry, int number, string name, string owner, string rowGuid, Func<GeneratedRowRequest, GameObject> make, long position)
        {
            this.registry = registry;
            this.make = make;
            Number = number;
            Name = string.IsNullOrEmpty(name) ? number.ToString(CultureInfo.InvariantCulture) : name;
            Owner = owner ?? "";
            RowGuid = rowGuid;
            ListGroup = new StudioGroup { Kind = StudioListKind.ItemGroup, Number = number, Sort = GeneratedRows.GroupSort, Name = Name, Position = position };
            ListGroup.Names.Add(Name);
            ListGroup.DeclaredBy.Add(Owner);
        }

        /// <summary>The group number (one number space with the list groups).</summary>
        public int Number { get; }
        /// <summary>The group's name as data (the list's own name of a group).</summary>
        public string Name { get; }
        /// <summary>The provider: the app's owner name or a plugin's GUID.</summary>
        public string Owner { get; }
        /// <summary>The GUID of every row's key when the rows share one (a plugin's group); null when each row names its own.</summary>
        public string RowGuid { get; }
        /// <summary>The group as the item group space lists it.</summary>
        public StudioGroup ListGroup { get; }
        /// <summary>True once the group left the registry.</summary>
        public bool Removed { get; internal set; }
        /// <summary>Every row, category by category (category order), each category in the order given.</summary>
        public IReadOnlyList<StudioItem> Rows => rows;
        /// <summary>The categories (category order), named or holding rows.</summary>
        public IReadOnlyList<StudioCategory> Categories => ListGroup.Categories;

        /// <summary>The row of <paramref name="key"/> in this group.</summary>
        public bool TryGetRow(ContentId key, out StudioItem row)
        {
            row = null;
            return !key.IsEmpty && byKey.TryGetValue(key, out row);
        }

        /// <summary>The row with id <paramref name="id"/>, in <paramref name="category"/> when it is given (ids are unique in a group), the first in
        /// list order.</summary>
        public bool TryGetRowById(int id, int? category, out StudioItem row)
        {
            foreach (var r in rows)
                if (r.Key.Id == id && (category == null || r.Category == category.Value)) { row = r; return true; }
            row = null;
            return false;
        }

        /// <summary>The object of a row for one item build: what the provider's function returns (null: none). Exceptions pass.</summary>
        public GameObject Make(GeneratedRowRequest request) => make?.Invoke(request);

        /// <summary>Names a category (it is listed once it holds rows).</summary>
        public void SetCategory(int category, string name)
        {
            CheckLive();
            var c = Category(category, true);
            string n = string.IsNullOrEmpty(name) ? category.ToString(CultureInfo.InvariantCulture) : name;
            if (c.Name == n) return;
            c.Name = n;
            c.Names.Clear();
            c.Names.Add(n);
            registry.Touched(this, GeneratedChangeKind.Rows, null);
        }

        /// <summary>Replaces every row of the group. ArgumentException: two rows with one key, or a row without a GUID.</summary>
        public void SetRows(IEnumerable<GeneratedRowSpec> specs) => Replace(specs, null);

        /// <summary>Replaces the rows of one category; the other categories keep theirs. ArgumentException as <see cref="SetRows(IEnumerable{GeneratedRowSpec})"/>
        /// (also a row of another category).</summary>
        public void SetRows(int category, IEnumerable<GeneratedRowSpec> specs) => Replace(specs, category);

        /// <summary>The scene's items of this row are built again (at the next apply of each scene runtime).</summary>
        public void Refresh(ContentId key)
        {
            CheckLive();
            registry.Touched(this, GeneratedChangeKind.Refresh, new[] { key });
        }

        /// <summary>Removes the group and its rows from the registry. Idempotent.</summary>
        public void Remove() => registry.Remove(this);

        void CheckLive()
        {
            if (Removed) throw new ObjectDisposedException("generated item group " + Number + " was removed");
        }

        StudioCategory Category(int number, bool create)
        {
            foreach (var c in ListGroup.Categories) if (c.Number == number) return c;
            if (!create) return null;
            var nc = new StudioCategory
            {
                Kind = StudioListKind.ItemCategory, Group = Number, Number = number, Sort = number, Name = number.ToString(CultureInfo.InvariantCulture),
                Position = registry.NextPosition(), HasGroup = true,
            };
            nc.Names.Add(nc.Name);
            ListGroup.Categories.Add(nc);
            ListGroup.Categories.Sort((a, b) => a.Number.CompareTo(b.Number));
            registry.Db.itemSpace.Categories[(Number, number)] = nc;
            if (!byCategory.ContainsKey(number)) byCategory[number] = new List<StudioItem>();
            registry.Db.itemsByCategory[(Number, number)] = byCategory[number];
            return nc;
        }

        void Replace(IEnumerable<GeneratedRowSpec> specs, int? only)
        {
            CheckLive();
            if (specs == null) throw new ArgumentNullException(nameof(specs));
            // checked whole before anything changes
            var list = specs.ToList();
            var keys = new HashSet<ContentId>();
            foreach (var s in list)
            {
                string guid = string.IsNullOrEmpty(s.Guid) ? RowGuid : s.Guid;
                if (string.IsNullOrEmpty(guid)) throw new ArgumentException("rows.noGuid: row " + s.Id + " of group " + Number + " names no GUID", nameof(specs));
                if (only.HasValue && s.Category != only.Value) throw new ArgumentException("rows.category: row " + s.Id + " is in category " + s.Category + ", not " + only.Value, nameof(specs));
                if (!keys.Add(new ContentId(guid, s.Id))) throw new ArgumentException("rows.duplicateId: two rows of group " + Number + " are " + guid + "#" + s.Id, nameof(specs));
            }
            if (only.HasValue)
                foreach (var k in keys)
                    if (byKey.TryGetValue(k, out var other) && other.Category != only.Value)
                        throw new ArgumentException("rows.duplicateId: " + k + " is a row of another category of group " + Number, nameof(specs));
            var old = new Dictionary<ContentId, StudioItem>(byKey);
            // the rows that stay (other categories when one is replaced)
            if (only.HasValue)
            {
                if (byCategory.TryGetValue(only.Value, out var gone))
                {
                    foreach (var r in gone) byKey.Remove(r.Key);
                    gone.Clear();
                }
            }
            else
            {
                byKey.Clear();
                foreach (var kv in byCategory) kv.Value.Clear();
            }
            var changed = new List<ContentId>();
            int index = 0;
            foreach (var s in list)
            {
                string guid = string.IsNullOrEmpty(s.Guid) ? RowGuid : s.Guid;
                var key = new ContentId(guid, s.Id);
                var it = new StudioItem
                {
                    Key = key, Group = Number, Category = s.Category, Name = s.Name ?? "", BundlePath = "", FileName = "", Manifest = "",
                    ListName = Name, ListBundle = "", Row = index++,
                };
                it.Order = it.Position = registry.NextPosition();
                it.Bones = s.FkBones != null ? s.FkBones.Where(b => !string.IsNullOrEmpty(b)).ToArray() : null;
                it.Visible = true;
                Category(s.Category, true);
                byCategory[s.Category].Add(it);
                byKey[key] = it;
                if (!old.TryGetValue(key, out var was) || !Same(was, it)) changed.Add(key);
            }
            rows.Clear();
            foreach (var kv in byCategory) rows.AddRange(kv.Value);
            foreach (var c in ListGroup.Categories) c.Visible = byCategory.TryGetValue(c.Number, out var l) && l.Count > 0;
            ListGroup.Visible = ListGroup.Categories.Exists(c => c.Visible);
            registry.Touched(this, GeneratedChangeKind.Rows, changed);
        }

        static bool Same(StudioItem a, StudioItem b)
        {
            if (a.Name != b.Name || a.Category != b.Category) return false;
            var x = a.Bones ?? Array.Empty<string>();
            var y = b.Bones ?? Array.Empty<string>();
            return x.SequenceEqual(y, StringComparer.Ordinal);
        }

        internal void Detach()
        {
            var db = registry.Db;
            db.itemSpace.Groups.Remove(Number);
            db.itemSpace.Ordered.Remove(ListGroup);
            foreach (var c in ListGroup.Categories)
            {
                db.itemSpace.Categories.Remove((Number, c.Number));
                db.itemsByCategory.Remove((Number, c.Number));
            }
        }

        internal void Attach()
        {
            var db = registry.Db;
            db.itemSpace.Groups[Number] = ListGroup;
            var ordered = db.itemSpace.Ordered.Concat(new[] { ListGroup }).OrderBy(x => x.Sort).ThenBy(x => x.Position).ToList();
            db.itemSpace.Ordered.Clear();
            db.itemSpace.Ordered.AddRange(ordered);
        }

        public override string ToString() => "generated group " + Number + " \"" + Name + "\" of " + Owner + ", " + rows.Count + " rows";
    }

    /// <summary>The registry of generated item groups of one Studio list database (see the file header).</summary>
    public sealed class GeneratedRows
    {
        /// <summary>The sort of every generated group: after the list groups.</summary>
        public const int GroupSort = int.MaxValue / 2;
        /// <summary>The code of a refused group number.</summary>
        public const string TakenCode = "rows.groupTaken";

        readonly Dictionary<int, GeneratedGroup> groups = new Dictionary<int, GeneratedGroup>();
        readonly List<GeneratedGroup> order = new List<GeneratedGroup>();
        long position = 1L << 32;

        internal GeneratedRows(StudioDb db) { Db = db; }

        internal StudioDb Db { get; }

        /// <summary>Changes with every change of a group, its categories or its rows (the list browser reads its rows again).</summary>
        public int Revision { get; private set; }

        /// <summary>Raised after a change: the rows whose scene items are built again.</summary>
        public event Action<GeneratedChange> Changed;

        /// <summary>The groups in registration order.</summary>
        public IReadOnlyList<GeneratedGroup> Groups => order;

        /// <summary>True when the registry holds group <paramref name="group"/>.</summary>
        public bool Holds(int group) => groups.Count > 0 && groups.ContainsKey(group);

        /// <summary>The group <paramref name="group"/>, or null.</summary>
        public GeneratedGroup Group(int group) => groups.Count > 0 && groups.TryGetValue(group, out var g) ? g : null;

        /// <summary>The group of a generated row, or null (a list row, a row of a removed group).</summary>
        public GeneratedGroup GroupOf(StudioItem row)
        {
            if (row == null || groups.Count == 0 || !groups.TryGetValue(row.Group, out var g)) return null;
            return g.TryGetRow(row.Key, out var r) && ReferenceEquals(r, row) ? g : null;
        }

        /// <summary>Every row of every group: groups in registration order, each group's rows as <see cref="GeneratedGroup.Rows"/>.</summary>
        public IEnumerable<StudioItem> Rows()
        {
            foreach (var g in order)
                foreach (var r in g.Rows) yield return r;
        }

        /// <summary>The number of rows of every group.</summary>
        public int RowCount
        {
            get
            {
                int n = 0;
                foreach (var g in order) n += g.Rows.Count;
                return n;
            }
        }

        /// <summary>The row of (group, key) when the registry holds the group.</summary>
        public bool TryGetRow(int group, ContentId key, out StudioItem row)
        {
            row = null;
            return groups.Count > 0 && groups.TryGetValue(group, out var g) && g.TryGetRow(key, out row);
        }

        /// <summary>The row of (group, category, id) when the registry holds the group: ids are unique in a group, so the number names the
        /// row's key; <paramref name="category"/> null: any category.</summary>
        public bool TryGetRowById(int group, int? category, int id, out StudioItem row)
        {
            row = null;
            return groups.Count > 0 && groups.TryGetValue(group, out var g) && g.TryGetRowById(id, category, out row);
        }

        /// <summary>The first row with key <paramref name="key"/> of any group (registration order).</summary>
        public bool TryFind(ContentId key, out StudioItem row)
        {
            row = null;
            if (groups.Count == 0 || key.IsEmpty) return false;
            foreach (var g in order)
                if (g.TryGetRow(key, out row)) return true;
            return false;
        }

        /// <summary>
        /// Why <paramref name="group"/> cannot be added (rows.groupTaken: the lists define it, it is the accessory group or another
        /// provider holds it), or null.
        /// </summary>
        public string Refusal(int group)
        {
            if (groups.ContainsKey(group)) return TakenCode + ": item group " + group + " is held by " + groups[group].Owner;
            if (group == StudioDb.AccessoryGroup) return TakenCode + ": item group " + group + " is the accessory group";
            if (Db.itemSpace.Groups.ContainsKey(group)) return TakenCode + ": item group " + group + " is defined by the lists";
            foreach (var k in Db.itemSpace.Categories.Keys) if (k.Item1 == group) return TakenCode + ": item group " + group + " has categories in the lists";
            foreach (var k in Db.itemsByCategory.Keys) if (k.Item1 == group) return TakenCode + ": item group " + group + " has rows in the lists";
            return null;
        }

        /// <summary>
        /// Adds a group whose rows' objects come from <paramref name="make"/>; false with <paramref name="refusal"/> when the number is
        /// taken. <paramref name="rowGuid"/>: the GUID of every row's key (a plugin's), or null when each row names its own.
        /// </summary>
        public bool TryAdd(int group, string name, string owner, string rowGuid, Func<GeneratedRowRequest, GameObject> make, out GeneratedGroup added, out string refusal)
        {
            added = null;
            refusal = Refusal(group);
            if (refusal != null) return false;
            added = new GeneratedGroup(this, group, name, owner, rowGuid, make, NextPosition());
            groups[group] = added;
            order.Add(added);
            added.Attach();
            Touched(added, GeneratedChangeKind.Added, added.Rows.Select(r => r.Key).ToArray());
            return true;
        }

        /// <summary><see cref="TryAdd"/>, throwing ArgumentException with the refusal.</summary>
        public GeneratedGroup Add(int group, string name, string owner, string rowGuid, Func<GeneratedRowRequest, GameObject> make)
        {
            if (!TryAdd(group, name, owner, rowGuid, make, out var g, out string refusal)) throw new ArgumentException(refusal, nameof(group));
            return g;
        }

        /// <summary>Removes a group and its rows. False when it is not (or no longer) in the registry.</summary>
        public bool Remove(GeneratedGroup g)
        {
            if (g == null || g.Removed || !groups.TryGetValue(g.Number, out var held) || held != g) return false;
            groups.Remove(g.Number);
            order.Remove(g);
            g.Detach();
            g.Removed = true;
            Touched(g, GeneratedChangeKind.Removed, null);
            return true;
        }

        /// <summary>Removes every group of <paramref name="owner"/>; returns how many.</summary>
        public int RemoveOwner(string owner)
        {
            int n = 0;
            foreach (var g in order.Where(x => x.Owner == owner).ToList()) if (Remove(g)) n++;
            return n;
        }

        internal long NextPosition() => position++;

        internal void Touched(GeneratedGroup g, GeneratedChangeKind kind, IReadOnlyCollection<ContentId> keys)
        {
            Revision++;
            var h = Changed;
            if (h == null) return;
            var c = new GeneratedChange(g.Number, kind, keys);
            foreach (Action<GeneratedChange> one in h.GetInvocationList())
            {
                try { one(c); }
                catch (Exception e) { Debug.LogWarning("generated item rows: a listener of group " + g.Number + " threw: " + e); }
            }
        }
    }

    /// <summary>
    /// The app's own generated group: «Chara to Item», category 0 the live rows of the store (rows hs2next.bake#&lt;number&gt;, FK names from
    /// its index), category 1 the cut markers (rows hs2next.cut#1 plane, #2 box, #3 tear, #4 hole). Registered when the Studio lists are
    /// built, before the scene, with the store's index read once; read again by <see cref="RefreshBakes"/> after each write of the
    /// store. Refused (rows.groupTaken, a warning) when a list file defines its number; bakes load by their own data then. A store row
    /// added as an item gets its bake data when it is added (the bake code); the cut code makes a marker's object.
    /// </summary>
    public static class AppItemRows
    {
        public const int Group = 731406;
        public const int BakesCategory = 0, MarkersCategory = 1;
        public const string Owner = "hs2next", BakeGuid = "hs2next.bake", CutGuid = "hs2next.cut";
        public const int Plane = 1, Box = 2, Tear = 3, Hole = 4;
        /// <summary>The names as data (the list browser shows the strings of the UI).</summary>
        public const string GroupName = "Chara to Item", BakesName = "Baked objects", MarkersName = "Cut tools";
        public static readonly IReadOnlyList<string> MarkerNames = new[] { "Plane", "Box", "Tear", "Hole" };

        /// <summary>The object of a bake row (set by the bake code); null, or a null result: the item builds the fallback item.</summary>
        public static Func<GeneratedRowRequest, GameObject> BakeObject { get; set; }
        /// <summary>The object of a marker row (set by the cut code); null, or a null result: the item builds the fallback item.</summary>
        public static Func<GeneratedRowRequest, GameObject> MarkerObject { get; set; }
        /// <summary>Called once the group is registered, with the group and the UserData folder (the bake code lists the store's rows).</summary>
        public static Action<GeneratedGroup, string> Registered { get; set; }

        /// <summary>Registers the group on <paramref name="db"/>; null when the number is taken (rows.groupTaken: counted, a diagnostic line
        /// and a warning in the log).</summary>
        public static GeneratedGroup Register(StudioDb db, string userData)
        {
            if (db == null) return null;
            if (!db.Generated.TryAdd(Group, GroupName, Owner, null, Make, out var g, out string refusal))
            {
                db.Count(GeneratedRows.TakenCode);
                db.Diagnostics.Add(refusal + "; the app's group is not added (bakes load by their own data)");
                Debug.LogWarning("[app] " + refusal + "; the app's group is not added (bakes load by their own data)");
                return null;
            }
            g.SetCategory(BakesCategory, BakesName);
            g.SetCategory(MarkersCategory, MarkersName);
            var markers = new List<GeneratedRowSpec>();
            for (int i = 0; i < MarkerNames.Count; i++) markers.Add(new GeneratedRowSpec(MarkersCategory, i + 1, MarkerNames[i], null, CutGuid));
            g.SetRows(MarkersCategory, markers);
            RefreshBakes(db, userData);
            var reg = Registered;
            if (reg != null)
            {
                try { reg(g, userData); }
                catch (Exception e) { Debug.LogWarning("[app] the app's item group: filling its rows threw " + e); }
            }
            return g;
        }

        static readonly ConditionalWeakTable<StudioDb, string> userDataOf = new ConditionalWeakTable<StudioDb, string>();

        /// <summary>
        /// Lists the store's live rows in category 0 again (its index read now; a store not made yet is empty and nothing is created): to
        /// call after each write of the store (a bake, a rename, a delete or restore, a purge, an import). <paramref name="userData"/> null:
        /// the folder of the registration. The number of rows listed; -1 when the app's group is not registered on <paramref name="db"/>.
        /// </summary>
        public static int RefreshBakes(StudioDb db, string userData = null)
        {
            var g = db?.Generated.Group(Group);
            if (g == null || g.Owner != Owner) return -1;
            if (userData != null)
            {
                userDataOf.Remove(db);
                userDataOf.Add(db, userData);
            }
            else if (!userDataOf.TryGetValue(db, out userData)) userData = null;
            var specs = new List<GeneratedRowSpec>();
            if (!string.IsNullOrEmpty(userData))
            {
                var findings = new FindingList();
                var store = BakeStore.ForUserData(userData, findings);
                foreach (var f in findings) Debug.LogWarning("[app] " + f.Code + ": " + f.Message);
                var seen = new HashSet<int>();
                foreach (var r in store.LiveRows())
                    if (seen.Add(r.Number)) specs.Add(new GeneratedRowSpec(BakesCategory, r.Number, r.Name, r.Fk, BakeGuid));
            }
            g.SetRows(BakesCategory, specs);
            return specs.Count;
        }

        /// <summary>Removes the app's group from <paramref name="db"/> (tests). False when it is not there.</summary>
        public static bool Remove(StudioDb db)
        {
            var g = db?.Generated.Group(Group);
            return g != null && g.Owner == Owner && db.Generated.Remove(g);
        }

        static GameObject Make(GeneratedRowRequest r)
        {
            var f = string.Equals(r.Guid, CutGuid, StringComparison.OrdinalIgnoreCase) ? MarkerObject
                : string.Equals(r.Guid, BakeGuid, StringComparison.OrdinalIgnoreCase) ? BakeObject : null;
            return f?.Invoke(r);
        }
    }
}
