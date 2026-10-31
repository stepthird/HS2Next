# HS2Next.Content

HS2Next ships no game assets, so assets are read at run time from content sources.
A catalog can hold several sources at once.

## IDs

Every part reference is a `ContentId`: the namespace GUID of the source that defines the part plus the part's ID inside that namespace. IDs are never renumbered at load time.

- Base-game rows: `ContentId.Vanilla(id)` = (`illusion.hs2`, original HS2 ID).
- Zipmod rows: (manifest GUID, the ID in the zipmod's own list CSV). This is what a card's Sideloader resolve info stores as `GUID` and `Slot`.
- Pack rows (future): (pack GUID, ID).

The GUID compares case-insensitively (ordinal). The string form is `<guid>#<id>` (`ToString`, `Parse`, `TryParse`; the last `#` separates the ID). Use it in JSON and other text formats. `default(ContentId)` is the empty reference (`IsEmpty`); it is not a vanilla ID. The type depends on nothing else in HS2Next or Unity. `ContentId` and `ContentIds` live in the `HS2Next.Api` assembly (`Assets/HS2Next/Api`, the stable API; namespace unchanged).

## Types

- `IContentSource` is one provider. It has a stable `Id` (`hs2-install`, `mods:<folder>`) and a `DisplayName`. It provides:
  - its own list rows (`ReadLists`). Base-game rows have `Key = ContentId.Vanilla(id)`.
  - its zipmods (`EnumerateZipmodFiles`, `ScanZipmods`, then `Zipmods`). Each `ZipmodInfo` holds the manifest GUID, the CSV list rows (keys in that GUID's namespace) and every abdata path the zip ships (`BundlePaths`). The character list CSVs (`abdata/list/characustom**/*.csv`) are read like Sideloader by `CharaListCsv`: the category and distributionNo are the first comma field of lines 1 and 2 (Excel-style trailing commas are fine), rows run to the first line without a `,`, fields are kept verbatim, the id is column 0, `Possess` is forced to 1, and mod rows are not gated by distributionNo.
  - bundles from its own store by abdata-relative path (`HasBundle`, `OpenBundle`). Dependencies from the abdata manifest are loaded first.
  - the abdata-relative paths of its own Studio list bundles (`EnumerateStudioInfoBundles`, every `*.unity3d` under `abdata/studio/info`).
- `Hs2InstallSource(root)` covers `abdata` (manifest dependencies, `list/characustom` MessagePack lists) and the zipmods under `<root>/mods`.
- `ModsFolderSource(dir)` covers zipmods only. It works without an HS2 install.
- `IPackSource` is a placeholder for the future HS2Next pack format. It has no implementation yet.
- `ContentCatalog` composes the sources. The first one added has the highest priority. The catalog owns:
  - the list DB: one table per category, keyed by `ContentId` (see "List DB" below).
  - the bundle cache (`IBundleCache`) and `UnloadAll()`.
  - bundle lookup for list entries (`LoadBundle`, `LoadAsset`). The order is: the entry's own zipmod, then the first zipmod that ships the path (`FindZipmodShipping`), then abdata.
  - asset loads through the texture guard (`Runtime/Chara/TextureGuard.cs`). Every load of character or Studio content goes through one of `LoadAsset` (list entry columns, abdata path, `IStudioAsset`), `LoadAssetAt` (an entry's bundle at another path; a Studio-style bundle by zipmod, path and manifest) or `LoadAssetFrom` (a bundle the caller opened through the catalog in its own order). Only list, manifest and Studio list data are read from bundles directly.
  - one zipmod scan cache per source, `<cacheDir>/modlists-<id>-<hash>.tsv`, keyed by path + size + mtime (see "Scan cache rows").
  - the Studio list DB (`LoadStudioLists`, `StudioLists`; see "Studio lists").
- `ContentSettings` is the local configuration, read from `Config/settings.local.json` (gitignored). To create it, copy `Config/settings.example.json`. Command-line overrides are `-settings`, `-gameRoot`, `-modsFolder` (repeatable) and `-cacheDir`. `ContentCatalog.FromSettings` builds the default catalog: the HS2 install first, then the extra mods folders.

- `ListEntry` is one list row: `Category`, `Key` (`ContentId`), `IsVanilla`, `Source`, `Zipmod`/`ZipPath`, the column `Values` and `entry["Column"]`.

## List DB

`LoadVanillaLists()` adds the sources' own rows, `LoadModLists()` scans the zipmods (`LoadLists()` does both). For the same (category, key), the row from the higher-priority source wins. Within one source, zipmod rows override the source's own rows, a later zipmod overrides an earlier one, and in one file a later row overrides an earlier one. Zipmods whose GUID is `illusion.hs2` are ignored (reserved namespace).

- `TryGetEntry(category, key, out entry)` and `GetEntry(category, key)` (null if absent).
- `EntriesOf(category)`: all rows of a category. Vanilla rows come first, then the others by source priority, then the source's own lists before its zipmods (in scan order), then file order.
- `EntriesOf(category, guid)`: the rows of one namespace, in the same order.
- `Categories`, `Count`, `VanillaCount`, `ModCount`, `CountOf(category)` count distinct rows. `ModRowsLoaded` counts the zipmod rows read by the last `LoadModLists()` before de-duplication (zipmods that share a GUID can repeat rows).
- `ZipmodsByGuid`: the zipmod used for each GUID. The higher-priority source wins; within a source, the last file wins.
- Cards: `KeyFor(card, category, id, property)` returns the reference the card means: (resolve GUID, resolve Slot) when the card has Sideloader resolve info for that property and category, else `ContentId.Vanilla(id)`. It does not check the DB, so importers can keep references to mods that are not installed. `Resolve(card, category, id, property)` returns the row for that key. If the mod row is missing, it logs `missing mod ...` and falls back to the vanilla row of `id`.

## Typical use

```csharp
var catalog = ContentCatalog.FromSettings(ContentSettings.LoadDefault());
catalog.LoadLists();                                   // vanilla lists + zipmod rows
var head = catalog.Resolve(card, 210, headId, "ChaFileFace.headId");
var key = catalog.KeyFor(card, 210, headId, "ChaFileFace.headId");   // e.g. "illusion.hs2#0"
var same = catalog.GetEntry(210, ContentId.Parse(key.ToString()));
var prefab = catalog.LoadAsset<GameObject>(head, "MainAB", "MainData");
catalog.UnloadAll();
```

Everything that touches Unity objects runs on the main thread.

## Studio lists

`catalog.LoadStudioLists(options)` builds a `StudioDb` (namespace `HS2Next.Content.Studio`, folder `Studio/`) and keeps it in `catalog.StudioLists`. Call it after `LoadLists()` (or `LoadModLists()`), because the zipmod CSV rows come from the zipmod scan. The DB reproduces the game's own list loading, with the parity switches below.

The DB has two inputs:

- the vanilla lists: every `*.unity3d` under `abdata/studio/info` of the first source that has any (`IContentSource.EnumerateStudioInfoBundles`). `ExcelData` and `Studio.ItemColorData` are read from them, then the bundles are unloaded again.
- the zipmod lists: the Studio CSVs of the zipmods in `ZipmodsByGuid` (`ZipmodInfo.StudioCsvs`, GUID order). They are merged into Sideloader virtual bundles (`StudioCsvFile.VirtualBundle`).

All bundles (and the zipmod GUIDs) are sorted with `StudioCsv.LoadOrder`, an ordinal comparison of the lower-cased strings, and parsed in the game's per-bundle kind order. That gives every definition a load-order number. A second pass then applies last wins per key, builds the global indexes, attaches categories, bone lists and colour flags, and computes visibility.

`StudioCsv.LoadOrder` is used instead of .NET `OrdinalIgnoreCase` on purpose. `OrdinalIgnoreCase` upper-cases, which sorts `[` and `_` after letters. The culture order puts them before letters, and with `OrdinalIgnoreCase`, 99 categories of one merge mod would land before their groups.

```csharp
var catalog = ContentCatalog.FromSettings(ContentSettings.LoadDefault());
catalog.LoadLists();
var db = catalog.LoadStudioLists();                      // StudioLoadOptions: defaults = game parity
foreach (var g in db.ItemGroups())
    foreach (var c in db.CategoriesOf(g))
        foreach (var item in db.ItemsIn(c)) { /* browser */ }
var it = db.ResolveItem(ContentId.Parse("illusion.hs2#590"));
var prefab = catalog.LoadAsset<GameObject>(it);           // dependency manifests preloaded
var anime = db.FindAnime(11, 14, 0);                      // the game's global winner for a raw key
```

### Types

- `StudioRecord` is the base of every row. It has `Key` (`ContentId`), `Guid`, `IsVanilla`, `Zipmod`, `ListName`, `ListBundle`, `Row`, `Order` (load order) and `Position` (first insertion of the key = display order). Three flags complete it: `AiContent`, `Shadowed` and `Visible`.
- The record classes are `StudioItem`, `StudioAnime` (Anime and HAnime, `IsH`, with `StudioAnimeOption`s), `StudioVoice`, `StudioMap`, `StudioLight`, `StudioBgm`, `StudioFilter`, `StudioProbe`, `StudioSky`, `StudioBone` (character FK), `StudioAccessoryPointGroup` and `StudioItemBoneList`. Their fields follow the column names of the game's lists.
- `StudioItemColor` holds the ItemData flags (vanilla, key (group, category, id)). It is attached to vanilla items as `item.Color`.
- `StudioGroup` and `StudioCategory` cover items, anime and voices. A record has `Number`, `Sort`, `Name`, `Position`, `DeclaredBy` (GUIDs) and `Names` (all names seen). A category also has `AiContent`, `DroppedInGame` and `HasGroup`. A group's categories are in display order.
- `IStudioAsset` is implemented by items, maps, lights, anime, voices, BGM, filters, probes and skies. It exposes `Zipmod`, `BundlePath`, `AssetName` (the scene name for maps) and `Manifest`.

### Keys

- Items, maps, lights, BGM, filters, probes, skies and accessory point groups use (GUID, local id). For vanilla rows the local id is the original number, and a later definition replaces an earlier one (last wins) while keeping the earlier position. Sideloader renumbers mod rows, so every mod row stays its own record, as in the game. `TryGet` on (GUID, id) returns the first such row in load order. Duplicate ids inside one mod are reported in `Diagnostics`. For items there is also a secondary index on (GUID, group, category, id).
- Anime/HAnime (one key space) and voices use (GUID, group, category, no), last wins per GUID. A second, global index over the raw (group, category, no) holds the last definition over all GUIDs. That is the game's winner, and the key that scenes store for mod anime. The other definitions are its candidates.
- FK bones use (GUID, no) plus a global `FindBone(no)`, last wins.
- Groups and categories form one global number space. Name and sort come from the last definition, and conflicting names are listed in `Diagnostics`. Categories are attached after everything has been read.
- Item FK bone lists attach to the item (same GUID, id). Rows without such an item are dropped and logged.

### Queries

- Browsers:
  - `ItemGroups()`, `AnimeGroups()`, `VoiceGroups()` list groups by sort, then by first insertion.
  - `CategoriesOf(group)` lists the categories of a group.
  - `ItemsIn(category)`, `AnimeIn(category)` (global winners only) and `VoicesIn(category)` list records by first insertion.
  - Hidden entries (empty, gated, orphaned) are skipped unless you pass `includeHidden: true`.
- Lookups: `TryGetItem(ContentId)`, `TryGetItem(guid, group, category, id)`, `ResolveItem(ContentId)`, `TryGetAnime(guid, g, c, no)`, `FindAnime(g, c, no)`, `AnimeCandidates(g, c, no)`, `TryGetVoice` / `FindVoice`, `TryGetMap` / `Light` / `Bgm` / `Filter` / `Probe` / `Sky` / `Bone` / `AccessoryPointGroup`, `FindBone(no)`, `TryGetItemColor`, and `TryGetItemGroup` / `Category` (also for anime and voices).
  - `ResolveItem` returns the scene fallback `StudioDb.FallbackItem` (illusion.hs2#399) when the item is missing, or when the item is AI-gated and the gate is on.
- Each kind is a `StudioTable<T>` (`db.Items`, `db.Maps`, ...) with `All` (display order, hidden included), `Enumerate(includeHidden)`, `Count`, `VisibleCount` and `TryGet`/`Get`. Replaced definitions are in `db.Shadowed`.
- Reporting:
  - `Counters` holds named counts: rows read, drops by reason, redefinitions, collisions, parity counts.
  - `Diagnostics` has one line per drop, malformed row, duplicate or conflict.
  - `TimingsMs` and `Summary()` give timings and totals.
- Assets:
  - `catalog.LoadAsset<T>(IStudioAsset)` and `catalog.LoadBundle(zipmod, bundlePath, manifest)` load in this order: the real abdata file, then the record's own zipmod, then the first zipmod shipping the path.
  - When the manifest column names a dependency manifest declared by `<hs2-scene-dependency manifest="X">` (`ZipmodInfo.SceneDependencies`, `catalog.ZipmodsDeclaringManifest(X)`), `abdata/X.unity3d` is read as an `AssetBundleManifest` and the bundle's dependencies are loaded first.
  - `catalog.BundleExists(path)` checks one namespace over abdata and all zipmods. `catalog.UnloadBundle(bundle)` releases a single bundle.

### Parity switches (`StudioLoadOptions`)

- `EmulateAIInstallGate` (default true): content the game shows only with the AI-Shoujo install is loaded but marked `AiContent` and hidden. Gated rows never replace non-gated rows.
- `IsInstallAdd` (null = detect `abdata/add50`): when false, bundles numbered 50 or higher and list files with bundle number 50 are skipped.
- `Lenient` (default false) does two things:
  - It also reads single-list CSVs whose name suffix differs from their folder. The game never reads them; 50 ItemGroup CSVs, which hide 5 items.
  - It never swallows an integer CSV row as a header row.
- `EmulateLoadOrderDrops` (default false) drops category files read before their group exists, like the game does. Otherwise categories are attached in the second pass.

These rules always apply, with a counter and a diagnostic:

- Malformed rows (non-integer keys, short rows, `bool.Parse` failures) are skipped, not thrown.
- Anime rows whose controller bundle exists nowhere are skipped.
- The Sideloader CSV rules apply: the header rule, the stop at a line without a comma, two-field group/category rows becoming `[id, id, name]`, and non-integer rows removed after merging.
- CSVs are decoded like .NET's `StreamReader` with UTF-8 (`ZipIndex.DecodeText`): a UTF-8, UTF-16 or UTF-32 byte-order mark is honoured and removed, otherwise UTF-8 with U+FFFD for invalid bytes.

### Scan cache rows

The per-source zipmod scan cache (`modlists-<id>-<hash>.tsv`) is at version `\t5` (5: character lists read like Sideloader). Changing the version re-scans every zipmod once. The cache is rewritten only when a zipmod changed, appeared or disappeared. Next to `Z`, `E` (one character list row; columns with an empty key are left out) and `B` it stores these rows:

- `S <entry path> <header count>`: starts a Studio list CSV (`abdata/studio/info*/**.csv`).
- `C <field> <field> ...`: one row of that CSV after Sideloader's line rules. A tab inside a field is stored as U+001F.
- `M <manifest> [<bundle> <asset>]...`: one `<hs2-scene-dependency>` declaration with its `<dependency>` children.
- `T [<name>]...`: the names of the manifest root's child elements, distinct, in manifest order (version 8; `ZipmodInfo.ManifestElements`). Any element's XML is read from the zip on demand (`ZipmodInfo.ReadManifestElement(s)`), so a reader of one plugin's manifest declarations needs no row of its own. A version 6 or 7 cache is upgraded by reading each manifest alone (the version 7 declaration rows `U` are dropped; `ZipmodScanResult.ElementsRead`).

The vanilla lists have their own cache, `<cacheDir>/studiolists-<id>-<hash of abdata>.tsv`. It stays valid while every info bundle keeps its path, size and mtime. Its rows are:

- `F <path> <size:ticks>`: the validity stamp of one bundle.
- `B <path>`: starts a bundle.
- `L <file> <name>`: starts an ExcelData list.
- `R <cell> ...`: one row of that list, with `\`, tab, CR and LF escaped.
- `I <asset> <count or -1>`: an ItemData asset.
- `c <group> <category> <id> <flags>`: one ItemData record.

With both caches warm, the first `LoadStudioLists()` of a process takes about 0.7 s, of which roughly 0.25 s is first-call JIT; a repeated call takes about 0.45 s.

## Adding a source type

1. Implement `IContentSource`. If the source has a zipmod folder, derive from `ZipmodSourceBase` instead.
2. Return the source's own rows from `ReadLists`, with `Key` in the source's namespace (`new ContentId(guid, id)`). Only the HS2 install uses `ContentIds.Vanilla`.
3. Serve bundles through `HasBundle` and `OpenBundle`. Open every bundle through the given `IBundleCache`, with keys prefixed by `Id`, so that nothing is loaded twice and `UnloadAll` can release everything.
4. Register the source with `catalog.AddSource(...)`, or add a settings field and wire it in `FromSettings`.
