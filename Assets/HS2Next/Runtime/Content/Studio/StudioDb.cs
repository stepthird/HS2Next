// The Studio list database: every Studio
// list kind from the vanilla abdata/studio/info bundles and the zipmod Studio CSVs, keyed without
// renumbering. Built by ContentCatalog.LoadStudioLists() (after LoadLists()) through StudioLoader.
//
// Keys:
//   items, maps, lights, BGM, filters, probes, skies, accessory point groups, bones: (GUID, local id)
//   anime/HAnime (one shared key space) and voices: (GUID, group, category, no), plus a global
//     (group, category, no) index holding the game's last-wins winner (scenes store mod anime
//     without a GUID); the losers stay reachable.
//   groups and categories: ONE global number space (last wins for name/sort).
// Display order: groups/categories by sort then first insertion; records by first
// insertion (load order); never by id. Main thread only for building (it reads AssetBundles);
// the finished DB is plain data.
using System;
using System.Collections.Generic;
using System.Linq;

namespace HS2Next.Content.Studio
{
    /// <summary>Switches for <see cref="ContentCatalog.LoadStudioLists"/>.</summary>
    public sealed class StudioLoadOptions
    {
        /// <summary>
        /// Emulate a machine without AI-Shoujo (the game's registry gate): gated records are
        /// loaded but marked <see cref="StudioRecord.AiContent"/> and hidden from the default
        /// enumerations; gated rows never replace non-gated ones; <see cref="StudioDb.ResolveItem"/>
        /// treats them as missing. Default true (the game's behaviour on this kind of install).
        /// </summary>
        public bool EmulateAIInstallGate = true;

        /// <summary>The game's `IsInstallAdd` (manifest add50 present). Null = detect from abdata/add50.</summary>
        public bool? IsInstallAdd;

        /// <summary>
        /// Also read single-list CSVs whose name suffix differs from their folder (the game never reads
        /// them), and never treat an integer row of a CSV as a header row. Default false (parity).
        /// </summary>
        public bool Lenient;

        /// <summary>
        /// Reproduce the game's order-dependent category drop (a category file is ignored when its group
        /// is defined only by a later bundle). Default false: categories attach after
        /// everything is read (two-phase resolution).
        /// </summary>
        public bool EmulateLoadOrderDrops;

        /// <summary>
        /// Offer the character accessories as Studio items: item group <see cref="StudioDb.AccessoryGroup"/> with one category
        /// per accessory kind (a behaviour of the loader HS2Next imitates, on by default). Off: the group is absent; stored
        /// items of the group stay unresolved and are written back unchanged.
        /// </summary>
        public bool AccessoryItems = true;
    }

    /// <summary>One kind of record in display order, with a (GUID, local id) index.</summary>
    public sealed class StudioTable<T> where T : StudioRecord
    {
        internal readonly List<T> all = new List<T>();
        internal readonly Dictionary<ContentId, T> byId = new Dictionary<ContentId, T>();

        /// <summary>All non-shadowed records in display order, hidden ones included.</summary>
        public IReadOnlyList<T> All => all;

        /// <summary>Records in display order; hidden ones only when <paramref name="includeHidden"/>.</summary>
        public IEnumerable<T> Enumerate(bool includeHidden = false) => includeHidden ? all : all.Where(r => r.Visible);

        /// <summary>Number of non-shadowed records (hidden included).</summary>
        public int Count => all.Count;

        /// <summary>Number of visible records.</summary>
        public int VisibleCount => all.Count(r => r.Visible);

        /// <summary>
        /// The record for (GUID, local id). Vanilla: the last definition. Zipmods: every row is its own
        /// record (the game renumbers them), and this index returns the FIRST row with that id in load
        /// order (Sideloader's first-match resolution); later duplicates are listed in the diagnostics.
        /// For Anime/HAnime/Voice use the dedicated lookups (the id alone is not unique).
        /// </summary>
        public bool TryGet(ContentId id, out T record) => byId.TryGetValue(id, out record);

        /// <summary><see cref="TryGet"/> or null.</summary>
        public T Get(ContentId id) => byId.TryGetValue(id, out var r) ? r : null;
    }

    /// <summary>Groups and categories of one browser (items, anime or voices).</summary>
    internal sealed class GroupSpace
    {
        public readonly Dictionary<int, StudioGroup> Groups = new Dictionary<int, StudioGroup>();
        public readonly Dictionary<(int, int), StudioCategory> Categories = new Dictionary<(int, int), StudioCategory>();
        public readonly List<StudioGroup> Ordered = new List<StudioGroup>();
    }

    /// <summary>The Studio list database. See the file header for keys and ordering.</summary>
    public sealed class StudioDb
    {
        /// <summary>The fallback item the game uses for unknown items: (illusion.hs2, 399) = `スフィア(通常）`.</summary>
        public static readonly ContentId FallbackItem = ContentId.Vanilla(399);

        /// <summary>
        /// The item group of the character accessories offered as Studio items. Its categories are the accessory list
        /// categories <see cref="AccessoryFirstCategory"/>..<see cref="AccessoryLastCategory"/>; an item of the group stores the
        /// accessory row's reference with this group and the accessory category.
        /// </summary>
        public const int AccessoryGroup = 647158;
        public const int AccessoryFirstCategory = 351, AccessoryLastCategory = 363;

        /// <summary>True for the accessory list categories the accessory group offers.</summary>
        public static bool IsAccessoryCategory(int category) => category >= AccessoryFirstCategory && category <= AccessoryLastCategory;

        public StudioLoadOptions Options { get; }

        /// <summary>The add50 flag used for loading.</summary>
        public bool IsInstallAdd { get; internal set; }

        /// <summary>Every parity drop, malformed row, duplicate and conflict, one line each.</summary>
        public readonly List<string> Diagnostics = new List<string>();

        /// <summary>Named counters (rows read, drops by reason, redefinitions, collisions, ...), see README.</summary>
        public readonly Dictionary<string, int> Counters = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Phase timings of the build in milliseconds (vanilla, mods, resolve, total).</summary>
        public readonly SortedDictionary<string, long> TimingsMs = new SortedDictionary<string, long>(StringComparer.Ordinal);

        public StudioTable<StudioItem> Items { get; } = new StudioTable<StudioItem>();
        /// <summary>Per-(GUID, group) anime and HAnime records (per-GUID winners); see <see cref="TryGetAnime"/>, <see cref="FindAnime"/>.</summary>
        public StudioTable<StudioAnime> Anime { get; } = new StudioTable<StudioAnime>();
        public StudioTable<StudioVoice> Voices { get; } = new StudioTable<StudioVoice>();
        public StudioTable<StudioMap> Maps { get; } = new StudioTable<StudioMap>();
        public StudioTable<StudioLight> Lights { get; } = new StudioTable<StudioLight>();
        public StudioTable<StudioBgm> Bgm { get; } = new StudioTable<StudioBgm>();
        public StudioTable<StudioFilter> Filters { get; } = new StudioTable<StudioFilter>();
        public StudioTable<StudioProbe> Probes { get; } = new StudioTable<StudioProbe>();
        public StudioTable<StudioSky> Skies { get; } = new StudioTable<StudioSky>();
        /// <summary>Character FK bones.</summary>
        public StudioTable<StudioBone> Bones { get; } = new StudioTable<StudioBone>();
        public StudioTable<StudioAccessoryPointGroup> AccessoryPointGroups { get; } = new StudioTable<StudioAccessoryPointGroup>();

        /// <summary>Definitions replaced by a later one with the same key (last wins), in load order.</summary>
        public IReadOnlyList<StudioRecord> Shadowed => shadowed;

        /// <summary>ItemBoneList rows that were attached to an item (later rows of the same item replace earlier ones).</summary>
        public IReadOnlyList<StudioItemBoneList> ItemBoneLists => boneLists;

        /// <summary>ItemData colour records keyed (group, category, id) (vanilla only).</summary>
        public IReadOnlyDictionary<(int group, int category, int id), StudioItemColor> ItemColors => itemColors;

        internal readonly List<StudioRecord> shadowed = new List<StudioRecord>();
        internal readonly List<StudioItemBoneList> boneLists = new List<StudioItemBoneList>();
        internal readonly Dictionary<(int, int, int), StudioItemColor> itemColors = new Dictionary<(int, int, int), StudioItemColor>();
        internal readonly GroupSpace itemSpace = new GroupSpace(), animeSpace = new GroupSpace(), voiceSpace = new GroupSpace();
        internal readonly Dictionary<(ContentId, int, int), StudioItem> itemsByFullKey = new Dictionary<(ContentId, int, int), StudioItem>();
        internal readonly Dictionary<(int, int), List<StudioItem>> itemsByCategory = new Dictionary<(int, int), List<StudioItem>>();
        internal readonly Dictionary<(ContentId, int, int), StudioAnime> animeByFullKey = new Dictionary<(ContentId, int, int), StudioAnime>();
        internal readonly Dictionary<(int, int, int), List<StudioAnime>> animeGlobal = new Dictionary<(int, int, int), List<StudioAnime>>();
        internal readonly Dictionary<(int, int), List<StudioAnime>> animeByCategory = new Dictionary<(int, int), List<StudioAnime>>();
        internal readonly Dictionary<(ContentId, int, int), StudioVoice> voiceByFullKey = new Dictionary<(ContentId, int, int), StudioVoice>();
        internal readonly Dictionary<(int, int, int), List<StudioVoice>> voiceGlobal = new Dictionary<(int, int, int), List<StudioVoice>>();
        internal readonly Dictionary<(int, int), List<StudioVoice>> voiceByCategory = new Dictionary<(int, int), List<StudioVoice>>();
        internal readonly Dictionary<int, StudioBone> boneGlobal = new Dictionary<int, StudioBone>();
        internal readonly Dictionary<(int, ContentId), StudioItem> accessoryByKey = new Dictionary<(int, ContentId), StudioItem>();
        internal readonly List<StudioItem> accessoryAll = new List<StudioItem>();

        internal StudioDb(StudioLoadOptions options) { Options = options; }

        internal void Count(string key, int n = 1) => Counters[key] = (Counters.TryGetValue(key, out var v) ? v : 0) + n;

        /// <summary>Counter value, 0 if absent.</summary>
        public int Counter(string key) => Counters.TryGetValue(key, out var v) ? v : 0;

        bool Hidden(StudioRecord r) => Options.EmulateAIInstallGate && r.AiContent;

        // ------------------------------------------------------------ groups / categories

        static IEnumerable<StudioGroup> GroupsOf(GroupSpace s, bool includeHidden) => s.Ordered.Where(g => includeHidden || g.Visible);

        /// <summary>Item groups in display order (sort, then first insertion); empty groups only when <paramref name="includeHidden"/>.</summary>
        public IEnumerable<StudioGroup> ItemGroups(bool includeHidden = false) => GroupsOf(itemSpace, includeHidden);
        /// <summary>Anime groups (Anime and HAnime share them) in display order.</summary>
        public IEnumerable<StudioGroup> AnimeGroups(bool includeHidden = false) => GroupsOf(animeSpace, includeHidden);
        /// <summary>Voice groups in display order.</summary>
        public IEnumerable<StudioGroup> VoiceGroups(bool includeHidden = false) => GroupsOf(voiceSpace, includeHidden);

        /// <summary>The categories of a group in display order; empty or gated ones only when <paramref name="includeHidden"/>.</summary>
        public IEnumerable<StudioCategory> CategoriesOf(StudioGroup group, bool includeHidden = false) =>
            group == null ? Enumerable.Empty<StudioCategory>() : group.Categories.Where(c => includeHidden || c.Visible);

        public bool TryGetItemGroup(int number, out StudioGroup g) => itemSpace.Groups.TryGetValue(number, out g);
        public bool TryGetAnimeGroup(int number, out StudioGroup g) => animeSpace.Groups.TryGetValue(number, out g);
        public bool TryGetVoiceGroup(int number, out StudioGroup g) => voiceSpace.Groups.TryGetValue(number, out g);
        public bool TryGetItemCategory(int group, int number, out StudioCategory c) => itemSpace.Categories.TryGetValue((group, number), out c);
        public bool TryGetAnimeCategory(int group, int number, out StudioCategory c) => animeSpace.Categories.TryGetValue((group, number), out c);
        public bool TryGetVoiceCategory(int group, int number, out StudioCategory c) => voiceSpace.Categories.TryGetValue((group, number), out c);

        /// <summary>All item categories (attached or not), unordered.</summary>
        public IEnumerable<StudioCategory> AllItemCategories => itemSpace.Categories.Values;
        public IEnumerable<StudioCategory> AllAnimeCategories => animeSpace.Categories.Values;
        public IEnumerable<StudioCategory> AllVoiceCategories => voiceSpace.Categories.Values;

        // ------------------------------------------------------------ items

        /// <summary>Items of a category in display order (first insertion); hidden ones only when asked.</summary>
        public IEnumerable<StudioItem> ItemsIn(StudioCategory category, bool includeHidden = false) =>
            category != null && itemsByCategory.TryGetValue((category.Group, category.Number), out var l)
                ? l.Where(r => includeHidden || r.Visible) : Enumerable.Empty<StudioItem>();

        /// <summary>The item for (GUID, local id) (see <see cref="StudioTable{T}.TryGet"/>), hidden or not.</summary>
        public bool TryGetItem(ContentId id, out StudioItem item) => Items.TryGet(id, out item);

        /// <summary>
        /// Secondary index for the duplicate ids inside one zipmod: (GUID, group, category, local id), first row wins. For
        /// <see cref="AccessoryGroup"/> the accessory row of (category, GUID#id).
        /// </summary>
        public bool TryGetItem(string guid, int group, int category, int id, out StudioItem item)
        {
            item = null;
            if (string.IsNullOrEmpty(guid)) return false;
            if (group == AccessoryGroup) return TryGetAccessoryItem(category, new ContentId(guid, id), out item);
            return itemsByFullKey.TryGetValue((new ContentId(guid, id), group, category), out item);
        }

        /// <summary>
        /// The row an item reference names together with its stored group and category: for <see cref="AccessoryGroup"/> the
        /// accessory row of (category, id), else the (GUID, group, category, id) row and then the (GUID, id) row.
        /// </summary>
        public bool TryGetItem(ContentId id, int group, int category, out StudioItem item)
        {
            item = null;
            if (id.IsEmpty) return false;
            if (group == AccessoryGroup) return TryGetAccessoryItem(category, id, out item);
            if (TryGetItem(id.SourceGuid, group, category, id.Id, out item) && item != null) return true;
            return Items.TryGet(id, out item);
        }

        /// <summary>
        /// Scene-load resolution: the item, or the fallback item (illusion.hs2, 399) when it is missing
        /// or (with <see cref="StudioLoadOptions.EmulateAIInstallGate"/>) AI-gated, like the game.
        /// </summary>
        public StudioItem ResolveItem(ContentId id)
        {
            if (Items.TryGet(id, out var it) && !Hidden(it)) return it;
            return Items.Get(FallbackItem);
        }

        /// <summary>
        /// Scene-load resolution of a stored item (its reference, group and category): the (GUID, group, category, id) row when
        /// it exists and is not gated, else <see cref="ResolveItem(ContentId)"/>. An item of <see cref="AccessoryGroup"/>
        /// resolves only to its accessory row; without it (the group switched off, the row gone) it is missing and the
        /// fallback item stands in. <paramref name="fallback"/> = the row is not the stored one.
        /// </summary>
        public StudioItem ResolveItem(ContentId id, int group, int category, out bool fallback)
        {
            fallback = false;
            if (id.IsEmpty)
            {
                fallback = true;
                return Items.Get(FallbackItem);
            }
            if (group == AccessoryGroup)
            {
                if (TryGetAccessoryItem(category, id, out var acc)) return acc;
                fallback = true;
                return Items.Get(FallbackItem);
            }
            if (TryGetItem(id.SourceGuid, group, category, id.Id, out var full) && full != null && !Hidden(full)) return full;
            var r = ResolveItem(id);
            fallback = r == null || r.Key != id;
            return r;
        }

        // ------------------------------------------------------------ accessories as items

        /// <summary>
        /// The accessory rows offered as items (group <see cref="AccessoryGroup"/>), category by category in the list order:
        /// the base game's rows first as the list holds them, then the mods' rows by GUID in the loader's order, each mod's
        /// rows in file order. Not part of <see cref="Items"/> (vanilla accessory ids restart per category and repeat item ids).
        /// Empty when <see cref="StudioLoadOptions.AccessoryItems"/> is off.
        /// </summary>
        public IReadOnlyList<StudioItem> AccessoryItems => accessoryAll;

        /// <summary>The accessory row of (accessory category, reference) offered as an item.</summary>
        public bool TryGetAccessoryItem(int category, ContentId id, out StudioItem item) => accessoryByKey.TryGetValue((category, id), out item);

        /// <summary>ItemData colour flags for (group, category, id), vanilla numbering.</summary>
        public bool TryGetItemColor(int group, int category, int id, out StudioItemColor c) => itemColors.TryGetValue((group, category, id), out c);

        // ------------------------------------------------------------ anime

        /// <summary>Anime/HAnime of a category in display order: the global winners only (what the game lists).</summary>
        public IEnumerable<StudioAnime> AnimeIn(StudioCategory category, bool includeHidden = false) =>
            category != null && animeByCategory.TryGetValue((category.Group, category.Number), out var l)
                ? l.Where(r => includeHidden || r.Visible) : Enumerable.Empty<StudioAnime>();

        /// <summary>The Anime/HAnime record of one GUID for (group, category, no), hidden or globally shadowed included.</summary>
        public bool TryGetAnime(string guid, int group, int category, int no, out StudioAnime anime)
        {
            anime = null;
            return !string.IsNullOrEmpty(guid) && animeByFullKey.TryGetValue((new ContentId(guid, no), group, category), out anime);
        }

        /// <summary>
        /// The game's record for a raw (group, category, no): the last definition in load order over
        /// vanilla and all mods (this is how scenes reference mod anime). With the AI gate on, gated
        /// rows only win when nothing else defines the key; check <see cref="StudioRecord.Visible"/>.
        /// </summary>
        public StudioAnime FindAnime(int group, int category, int no) =>
            animeGlobal.TryGetValue((group, category, no), out var l) ? l[0] : null;

        /// <summary>Every per-GUID record of a raw key, the global winner first, then the others in load order.</summary>
        public IReadOnlyList<StudioAnime> AnimeCandidates(int group, int category, int no) =>
            animeGlobal.TryGetValue((group, category, no), out var l) ? l : (IReadOnlyList<StudioAnime>)Array.Empty<StudioAnime>();

        // ------------------------------------------------------------ voices

        public IEnumerable<StudioVoice> VoicesIn(StudioCategory category, bool includeHidden = false) =>
            category != null && voiceByCategory.TryGetValue((category.Group, category.Number), out var l)
                ? l.Where(r => includeHidden || r.Visible) : Enumerable.Empty<StudioVoice>();

        public bool TryGetVoice(string guid, int group, int category, int no, out StudioVoice voice)
        {
            voice = null;
            return !string.IsNullOrEmpty(guid) && voiceByFullKey.TryGetValue((new ContentId(guid, no), group, category), out voice);
        }

        /// <summary>Last-wins record for a raw (group, category, no) over all GUIDs (as for anime).</summary>
        public StudioVoice FindVoice(int group, int category, int no) =>
            voiceGlobal.TryGetValue((group, category, no), out var l) ? l[0] : null;

        // ------------------------------------------------------------ single-key kinds

        public bool TryGetMap(ContentId id, out StudioMap r) => Maps.TryGet(id, out r);
        public bool TryGetLight(ContentId id, out StudioLight r) => Lights.TryGet(id, out r);
        public bool TryGetBgm(ContentId id, out StudioBgm r) => Bgm.TryGet(id, out r);
        public bool TryGetFilter(ContentId id, out StudioFilter r) => Filters.TryGet(id, out r);
        public bool TryGetProbe(ContentId id, out StudioProbe r) => Probes.TryGet(id, out r);
        public bool TryGetSky(ContentId id, out StudioSky r) => Skies.TryGet(id, out r);
        public bool TryGetBone(ContentId id, out StudioBone r) => Bones.TryGet(id, out r);
        public bool TryGetAccessoryPointGroup(ContentId id, out StudioAccessoryPointGroup r) => AccessoryPointGroups.TryGet(id, out r);

        /// <summary>Character FK bone by raw number, last definition over all GUIDs (Bone lists are not renumbered by Sideloader).</summary>
        public StudioBone FindBone(int no) => boneGlobal.TryGetValue(no, out var b) ? b : null;

        List<StudioBone> bonesInOrder;

        /// <summary>
        /// Character FK bone rows for FK discovery: one row per number, the last
        /// definition over all GUIDs (as <see cref="FindBone"/>), ordered by the load-order position of the number's first
        /// definition (the order of Studio's merged Bone dictionary). Built on first use.
        /// </summary>
        public IReadOnlyList<StudioBone> BonesInOrder()
        {
            if (bonesInOrder != null) return bonesInOrder;
            var first = new Dictionary<int, long>();
            void Seen(int no, long p) { if (!first.TryGetValue(no, out var q) || p < q) first[no] = p; }
            foreach (var b in Bones.all) Seen(b.No, Math.Min(b.Position, b.Order));   // winners: Position = first definition of the (GUID, no) key
            foreach (var s in shadowed) if (s is StudioBone sb) Seen(sb.No, sb.Order);
            bonesInOrder = boneGlobal.Values.OrderBy(b => first.TryGetValue(b.No, out var p) ? p : long.MaxValue).ThenBy(b => b.No).ToList();
            return bonesInOrder;
        }

        // ------------------------------------------------------------ summary

        /// <summary>Record counts: "&lt;kind&gt;: visible / total".</summary>
        public IEnumerable<string> Summary()
        {
            string T<T>(string n, StudioTable<T> t) where T : StudioRecord => $"{n}: {t.VisibleCount} visible / {t.Count} records (vanilla {t.All.Count(r => r.IsVanilla && r.Visible)} visible)";
            yield return $"item groups: {itemSpace.Ordered.Count(g => g.Visible)} visible / {itemSpace.Groups.Count}; categories {itemSpace.Categories.Values.Count(c => c.Visible)} visible / {itemSpace.Categories.Count}";
            yield return T("items", Items);
            yield return $"anime groups: {animeSpace.Ordered.Count(g => g.Visible)} visible / {animeSpace.Groups.Count}; categories {animeSpace.Categories.Values.Count(c => c.Visible)} visible / {animeSpace.Categories.Count}";
            yield return T("anime+hanime", Anime) + $"; global keys {animeGlobal.Count}";
            yield return $"voice groups: {voiceSpace.Ordered.Count(g => g.Visible)} visible / {voiceSpace.Groups.Count}; categories {voiceSpace.Categories.Values.Count(c => c.Visible)} visible / {voiceSpace.Categories.Count}";
            yield return T("voices", Voices);
            yield return T("maps", Maps);
            yield return T("lights", Lights);
            yield return T("bgm", Bgm);
            yield return T("filters", Filters);
            yield return T("probes", Probes);
            yield return T("skies", Skies);
            yield return T("bones", Bones);
            yield return T("accessory point groups", AccessoryPointGroups);
            yield return $"item colour records: {itemColors.Count}; bone lists attached: {boneLists.Count}; shadowed definitions: {shadowed.Count}; diagnostics: {Diagnostics.Count}";
        }
    }
}
