// HS2 character card / coordinate card → HS2Next chara and coord documents. Pipeline
//: validate, build the vanilla objects with MemberInit values and fixed-length arrays, HS2's
// sub-version migrations, decode the plugin data (Phase 1 decoders), decide the sexes, build the accessory
// list, resolve every reference, map the plugin sections, copy the raw plugin data, write the
// provenance and the report. Main thread (the catalog is). The importer never writes HS2 files.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using HS2Next.Content;
using HS2Next.Data;
using HS2Next.Data.Ext;
using HS2Next.Save;
using HS2Next.Scenes;

namespace HS2Next.Import
{
    /// <summary>A character card import: the in-memory character file (null when the source was rejected) and the report.</summary>
    public sealed class CharaImportResult
    {
        public CharaContainer Container;
        public ImportReport Report;
        /// <summary>The parsed card (for tests and the option); null when the source was rejected.</summary>
        public Card Card;
        /// <summary>The decoded plugin data the typed sections came from.</summary>
        public CardExtData Ext;
        public bool Ok => Container != null;
    }

    /// <summary>A coordinate card import.</summary>
    public sealed class CoordImportResult
    {
        public CoordContainer Container;
        public ImportReport Report;
        public CoordCard Card;
        public CardExtData Ext;
        public bool Ok => Container != null;
    }

    public sealed class Hs2Importer : ICharaImporter
    {
        /// <summary>Revision of the mapping; part of `import.importer` (`&lt;app&gt;+imp.&lt;revision&gt;`). Bump on every mapping change.</summary>
        public const int Revision = 1;

        public ResolveCatalog Resolve { get; }
        public ImportOptions Options { get; }

        /// <param name="catalog">A catalog with its lists loaded (vanilla and zipmods).</param>
        public Hs2Importer(ContentCatalog catalog, ImportOptions options = null)
            : this(new ResolveCatalog(catalog), options) { }

        public Hs2Importer(ResolveCatalog resolve, ImportOptions options = null)
        {
            Resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
            Options = options ?? new ImportOptions();
        }

        public string ImporterVersion => (Options.AppVersion ?? SaveFormats.AppVersion) + "+imp." + Revision;

        /// <summary>The kind of an HS2 source (no validation beyond the marker).</summary>
        public static Hs2SourceKind Detect(byte[] data) => Hs2Header.Detect(data);

        /// <summary>
        /// R(v, P, c) for one raw id over the resolve entries <paramref name="entries"/> (null property = a field
        /// without a Sideloader property); findings go to <paramref name="report"/> with the path "ref". For tests and tools.
        /// </summary>
        public string ResolveRef(int raw, string property, int category, List<Card.ResolveInfo> entries, ImportReport report = null) =>
            new RefResolver(Resolve, entries, report ?? new ImportReport()).Resolve(raw, property, category, "ref");

        // ===================================================================================== character cards

        public CharaImportResult ImportChara(string path) => ImportChara(File.ReadAllBytes(path), Path.GetFileName(path), path);

        /// <summary>Never throws for bad input: a rejected source or an unexpected failure gives a result without a container
        /// and an error finding (`card.*`, or `import.exception` for an importer fault).</summary>
        /// <param name="fileName">Original file name without folder (`import.fileName`).</param>
        /// <param name="sourcePath">For the report only (defaults to the file name).</param>
        public CharaImportResult ImportChara(byte[] data, string fileName, string sourcePath = null)
        {
            var sw = Stopwatch.StartNew();
            try { return ImportCharaCore(data, fileName, sourcePath, sw); }
            catch (Exception e) when (!(e is OutOfMemoryException))
            {
                return new CharaImportResult { Report = Fault("chara", fileName, sourcePath, e, sw) };
            }
        }

        CharaImportResult ImportCharaCore(byte[] data, string fileName, string sourcePath, Stopwatch sw)
        {
            var report = new ImportReport { SourcePath = sourcePath ?? fileName, Kind = "chara" };
            var res = new CharaImportResult { Report = report };
            var h = Hs2Header.Read(data);
            if (Reject(h, Hs2SourceKind.Chara, report)) { report.Milliseconds = sw.Elapsed.TotalMilliseconds; return res; }
            Card card;
            try { card = Card.Load(data, sourcePath ?? fileName); }
            catch (Exception e)
            {
                report.Error("card.truncated", "", "card not readable (" + e.GetType().Name + ": " + e.Message + ")");
                report.Milliseconds = sw.Elapsed.TotalMilliseconds;
                return res;
            }
            res.Card = card;
            bool esf = card.KKEx == null || card.KKExVersion == "3";
            var ext = CardExtensions.Decode(card);
            res.Ext = ext;

            var c = new CharaContainer();
            var x = NewContext(report, data, c.Res);
            x.Outfit = true;
            x.CoordPath = CharaSubtree.CoordDir;

            // Blocks: a newer block version is skipped (MemberInit values), as HS2 does.
            var skipped = new HashSet<string>(StringComparer.Ordinal);
            var present = new HashSet<string>(StringComparer.Ordinal);
            foreach (var kv in h.Blocks)
            {
                present.Add(kv.Key);
                if (!Hs2Header.CurrentBlockVersions.TryGetValue(kv.Key, out var cur)) continue;
                if (!System.Version.TryParse(kv.Value, out var v) || v > cur)
                {
                    skipped.Add(kv.Key);
                    report.Warning("card.blockSkipped", kv.Key, $"block version \"{kv.Value}\" is newer than {cur} (or unreadable); MemberInit values used");
                }
            }
            foreach (var b in new[] { "Custom", "Coordinate", "Parameter" })
                if (!present.Contains(b)) report.Warning("card.blockMissing", b, "block absent; MemberInit values used");
            bool custom = !skipped.Contains("Custom"), coordinate = !skipped.Contains("Coordinate");
            object face = custom ? card.Face : null, body = custom ? card.Body : null, hair = custom ? card.Hair : null;
            object clothes = coordinate ? card.Clothes : null, accessory = coordinate ? card.Accessory : null;
            object param = skipped.Contains("Parameter") ? null : card.Parameter;
            object param2 = !skipped.Contains("Parameter2") && card.Blocks.TryGetValue("Parameter2", out var p2) ? p2 : null;

            var chara = c.Subtree.Chara;
            var coord = c.Subtree.Coord;
            VanillaImport.Profile(x, param, param2, chara.Profile);
            int sex = chara.Profile.Sex;
            VanillaImport.Face(x, face, chara.Face, sex);
            VanillaImport.Body(x, body, chara.Body, sex);
            var bundleIds = VanillaImport.Hair(x, hair, chara.Hair);
            // a character card's coordinate takes the character's sex (Sideloader resolves it with Parameter.sex).
            coord.Name = "";
            coord.Sex = sex;
            VanillaImport.Clothes(x, clothes, coord.Clothes, sex);
            VanillaImport.Accessories(x, accessory, coord);
            VanillaImport.MoreAccessories(x, esf ? ext.MoreAccessories : null, coord);

            var resolver = new RefResolver(Resolve, esf ? card.Resolve : new List<Card.ResolveInfo>(), report);
            foreach (var f in x.Refs) resolver.Resolve(f);
            resolver.ReportUnused("");
            DropForeignBundles(x, chara.Hair, bundleIds);

            if (esf) PluginImport.Apply(x, ext, chara, coord, false);

            // Raw copies: KKEx entries by scope, per-object ExtendedSaveData, or the whole block of another version.
            var charaStore = KkexImport.NewStore("chara", card.KKExVersion);
            var coordStore = KkexImport.NewStore("chara", card.KKExVersion);
            if (!esf) KkexImport.CopyBlock(x, card.KKExRaw, "chara", charaStore);
            else KkexImport.CopyKkex(x, ext, "chara", charaStore, coordStore);
            KkexImport.CopyObjects(x, face, "ChaFileFace", "chara", charaStore);
            KkexImport.CopyObjects(x, body, "ChaFileBody", "chara", charaStore);
            KkexImport.CopyObjects(x, hair, "ChaFileHair", "chara", charaStore);
            KkexImport.CopyObjects(x, param, "ChaFileParameter", "chara", charaStore);
            KkexImport.CopyObjects(x, param2, "ChaFileParameter2", "chara", charaStore);
            foreach (var (block, name) in new[] { ("Status", "ChaFileStatus"), ("GameInfo", "ChaFileGameInfo"), ("GameInfo2", "ChaFileGameInfo2") })
                if (!skipped.Contains(block) && card.Blocks.TryGetValue(block, out var bo)) KkexImport.CopyObjects(x, bo, name, "chara", charaStore);
            KkexImport.CopyObjects(x, clothes, "ChaFileClothes", "chara", coordStore);
            KkexImport.CopyObjects(x, accessory, "ChaFileAccessory", "chara", coordStore);
            if (!charaStore.IsEmpty) c.Subtree.CharaExt.Kkex = charaStore;
            if (!coordStore.IsEmpty) c.Subtree.CoordExt.Kkex = coordStore;

            NegativeZeros(x, face, body, hair, clothes, accessory);
            var now = Options.Now;
            c.Subtree.Import = Provenance("hs2.chara", fileName, data, now, card.Version, "character", card.Resolve);
            c.CreatedUtc = now;
            c.LoadedManifest = RequiresSeed(x, card.Resolve);
            report.Count("accessory.slots", coord.Accessories.Count);
            if (Options.Thumbnails != null) c.Thumbnail = Thumbnail(report, () => Options.Thumbnails.RenderChara(c));
            res.Container = c;
            report.Milliseconds = sw.Elapsed.TotalMilliseconds;
            return res;
        }

        // ===================================================================================== coordinate cards

        public CoordImportResult ImportCoord(string path) => ImportCoord(File.ReadAllBytes(path), Path.GetFileName(path), path);

        /// <summary>Never throws for bad input (see <see cref="ImportChara(byte[], string, string)"/>).</summary>
        public CoordImportResult ImportCoord(byte[] data, string fileName, string sourcePath = null) => ImportCoord(data, fileName, sourcePath, false, null);

        /// <summary>
        /// The import of a coordinate card a character is about to wear (the coordinate load of the Edit tab, Studio's Costume
        /// tab, the bridge; Phase 4.4): as <see cref="ImportCoord(string)"/>, but a card without sex evidence (no CoordinateSex
        /// block, no resolve entries) takes the sex HS2's coordinate lists give its file — the folder `coordinate/male` or
        /// `coordinate/female` it lies in (MakerEdits.CoordinateSexFromPath) — else <paramref name="targetSex"/> with the warning
        /// `coord.sexAssumed`, else female. Evidence in the card always wins.
        /// </summary>
        public CoordImportResult ImportCoordToWear(string path, int? targetSex) => ImportCoord(File.ReadAllBytes(path), Path.GetFileName(path), path, true, targetSex);

        CoordImportResult ImportCoord(byte[] data, string fileName, string sourcePath, bool toWear, int? targetSex)
        {
            var sw = Stopwatch.StartNew();
            try { return ImportCoordCore(data, fileName, sourcePath, sw, toWear, targetSex); }
            catch (Exception e) when (!(e is OutOfMemoryException))
            {
                return new CoordImportResult { Report = Fault("coord", fileName, sourcePath, e, sw) };
            }
        }

        CoordImportResult ImportCoordCore(byte[] data, string fileName, string sourcePath, Stopwatch sw, bool toWear, int? targetSex)
        {
            var report = new ImportReport { SourcePath = sourcePath ?? fileName, Kind = "coord" };
            var res = new CoordImportResult { Report = report };
            var h = Hs2Header.Read(data);
            if (Reject(h, Hs2SourceKind.Coord, report)) { report.Milliseconds = sw.Elapsed.TotalMilliseconds; return res; }
            CoordCard card;
            try { card = CoordCard.Load(data, sourcePath ?? fileName); }
            catch (Exception e)
            {
                report.Error("card.truncated", "", "coordinate not readable (" + e.GetType().Name + ": " + e.Message + ")");
                report.Milliseconds = sw.Elapsed.TotalMilliseconds;
                return res;
            }
            res.Card = card;
            var ext = CardExtensions.Decode(card);
            res.Ext = ext;

            var c = new CoordContainer();
            var x = NewContext(report, data, c.Res);
            var coord = c.Coord;
            coord.Name = card.Name ?? "";
            string how;
            coord.Sex = CoordinateSex(x, card, out how, toWear, sourcePath, targetSex);
            VanillaImport.Clothes(x, card.Clothes, coord.Clothes, coord.Sex);
            VanillaImport.Accessories(x, card.Accessory, coord);
            VanillaImport.MoreAccessories(x, ext.MoreAccessories, coord);

            var resolver = new RefResolver(Resolve, card.Resolve, report);
            foreach (var f in x.Refs) resolver.Resolve(f);
            resolver.ReportUnused("");

            PluginImport.Apply(x, ext, null, coord, true);

            string blockVersion = card.KKExVersion >= 0 ? card.KKExVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : h.TrailerVersion >= 0 ? h.TrailerVersion.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
            var store = KkexImport.NewStore("coord", blockVersion);
            if (h.OtherTrailer != null) KkexImport.CopyBlock(x, h.OtherTrailer, "coord", store);
            else KkexImport.CopyKkex(x, ext, "coord", store, store);
            KkexImport.CopyObjects(x, card.Clothes, "ChaFileClothes", "coord", store);
            KkexImport.CopyObjects(x, card.Accessory, "ChaFileAccessory", "coord", store);
            if (!store.IsEmpty) c.CoordExt.Kkex = store;

            NegativeZeros(x, null, null, null, card.Clothes, card.Accessory);
            var now = Options.Now;
            c.Import = Provenance("hs2.coord", fileName, data, now, card.Version, how, card.Resolve);
            c.CreatedUtc = now;
            c.LoadedManifest = RequiresSeed(x, card.Resolve);
            report.Count("accessory.slots", coord.Accessories.Count);
            if (Options.Thumbnails != null) c.Thumbnail = Thumbnail(report, () => Options.Thumbnails.RenderCoord(c));
            res.Container = c;
            report.Milliseconds = sw.Elapsed.TotalMilliseconds;
            return res;
        }

        // ===================================================================================== either kind

        /// <summary>Imports a character or coordinate card by its marker (the other marker routes to the other importer).
        /// Exactly one of the two results is set; a file that is no card gives a character result with the error.</summary>
        public (CharaImportResult chara, CoordImportResult coord) ImportFile(string path)
        {
            var data = File.ReadAllBytes(path);
            return Hs2Header.Detect(data) == Hs2SourceKind.Coord
                ? ((CharaImportResult)null, ImportCoord(data, Path.GetFileName(path), path))
                : (ImportChara(data, Path.GetFileName(path), path), (CoordImportResult)null);
        }

        // ===================================================================================== scene hook (2.2 CharaImport)

        /// <summary>
        /// <see cref="ICharaImporter"/> for the scene model: imports the card of <paramref name="source"/> (its cached bytes, else
        /// the file) and appends the report's findings to <paramref name="findings"/> (same codes, paths prefixed with the file name).
        /// Returns null when the card was rejected.
        /// </summary>
        public CharaContainer Import(CardAppearanceSource source, FindingList findings)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            byte[] data = source.ReadBytes?.Invoke() ?? (source.FullPath != null ? File.ReadAllBytes(source.FullPath) : null);
            if (data == null) throw new InvalidOperationException("the card source has no bytes and no path");
            string name = Path.GetFileName((source.Hs2Path ?? source.FullPath ?? "card.png").Replace('/', Path.DirectorySeparatorChar));
            var r = ImportChara(data, name, source.FullPath ?? source.Hs2Path);
            if (findings != null)
            {
                foreach (var f in r.Report.Findings)
                {
                    var g = findings.Add(f.Severity, f.Code, name + ": " + f.Path, f.Message);
                    g.Count += f.Count - 1;
                }
                if (r.Container != null && source.Sha256 != null && r.Container.Subtree.Import.Sha256 != source.Sha256)
                    findings.Warning("import.sourceChanged", name, "the card bytes differ from the source's recorded SHA-256; accessory ids follow the bytes imported");
            }
            return r.Container;
        }

        // ===================================================================================== helpers

        static ImportReport Fault(string kind, string fileName, string sourcePath, Exception e, Stopwatch sw)
        {
            var r = new ImportReport { SourcePath = sourcePath ?? fileName, Kind = kind };
            r.Error("import.exception", "", "importer fault " + e.GetType().Name + ": " + e.Message);
            r.Milliseconds = sw.Elapsed.TotalMilliseconds;
            return r;
        }

        /// <summary>The thumbnail hook's PNG; a failing renderer costs the thumbnail only (`thumbnail.renderFailed`).</summary>
        static byte[] Thumbnail(ImportReport report, Func<byte[]> render)
        {
            try { return render(); }
            catch (Exception e)
            {
                report.Warning("thumbnail.renderFailed", "", "thumbnail renderer threw " + e.GetType().Name + ": " + e.Message + "; no thumbnail (the save writes the placeholder)");
                return null;
            }
        }

        ImportContext NewContext(ImportReport report, byte[] data, ResStore res) => new ImportContext
        {
            Report = report, Raw = new RawReader(report), Catalog = Resolve, Res = res, Sha256 = ResStore.Sha256Hex(data),
        };

        static bool Reject(Hs2Header h, Hs2SourceKind want, ImportReport report)
        {
            if (h.Error != null) { report.Error(h.Error, "", h.ErrorMessage); return true; }
            if (h.Kind != want)
            {
                report.Error("card.wrongKind", "", h.Kind == Hs2SourceKind.Coord ? "a coordinate card: use the coordinate importer" : "a character card: use the character importer");
                return true;
            }
            return false;
        }

        /// <summary>the authored sex of a coordinate card and how it was decided. Without evidence a card to be worn
        /// (<paramref name="toWear"/>) takes its folder's sex, else the target character's (MakerEdits.CoordinateSexOf).</summary>
        static int CoordinateSex(ImportContext x, CoordCard card, out string how, bool toWear = false, string sourcePath = null, int? targetSex = null)
        {
            var s = KkexImport.CoordinateSex(card.Clothes);
            if (s == 0 || s == 1)
            {
                how = "coordinateSex";
                x.Report.Info("coord.sex", "coord.sex", "CoordinateSex " + s);
                x.Report.Count("coord.sex.coordinateSex");
                return s.Value;
            }
            bool male = false, female = false;
            foreach (var e in card.Resolve)
            {
                var p = e.Property ?? "";
                if (p.EndsWith("ChaFileClothes.ClothesTopM", StringComparison.Ordinal) || p.EndsWith("ChaFileClothes.ClothesBotM", StringComparison.Ordinal)
                    || p.EndsWith("ChaFileClothes.ClothesGlovesM", StringComparison.Ordinal) || p.EndsWith("ChaFileClothes.ClothesShoesM", StringComparison.Ordinal)) male = true;
                else if (p.EndsWith("ChaFileClothes.ClothesTop", StringComparison.Ordinal) || p.EndsWith("ChaFileClothes.ClothesBot", StringComparison.Ordinal)
                    || p.EndsWith("ChaFileClothes.ClothesGloves", StringComparison.Ordinal) || p.EndsWith("ChaFileClothes.ClothesShoes", StringComparison.Ordinal)) female = true;
            }
            if (male || female)
            {
                how = "resolveInfo";
                if (male && female) x.Report.Warning("coord.sexConflict", "coord.sex", "resolve entries of both the male and the female clothes tables; female assumed");
                x.Report.Info("coord.sex", "coord.sex", "from the resolve entries");
                x.Report.Count("coord.sex.resolveInfo");
                return male && !female ? 0 : 1;
            }
            how = "default";
            x.Report.Count("coord.sex.default");
            if (toWear)
            {
                // a card worn by a character: HS2 lists coordinates per sex folder and loads any file into the current
                // character, so the folder decides, else the character (Phase 4.4, the coordinate load path)
                int sex = HS2Next.Scenes.MakerEdits.CoordinateSexOf(null, sourcePath, targetSex, out string from);
                string name = sex == 0 ? "male" : "female";
                if (from == HS2Next.Scenes.MakerEdits.CoordinateSexFromFolder)
                {
                    x.Report.Info("coord.sex", "coord.sex", "no evidence; " + name + " from the file's folder coordinate/" + name);
                    return sex;
                }
                if (from == HS2Next.Scenes.MakerEdits.CoordinateSexFromCharacter)
                {
                    x.Report.Warning("coord.sexAssumed", "coord.sex", "no sex evidence in the card and no coordinate/male or coordinate/female folder: " + name + " assumed (the character's sex)");
                    return sex;
                }
            }
            x.Report.Info("coord.sex", "coord.sex", "no evidence; female assumed");
            return 1;
        }

        // `bundleId`: HS2 discards the bundles of a base-game hair part when bundleId differs from the raw id (mod parts
        // get bundleId = the resolved id from Sideloader, so theirs are kept).
        static void DropForeignBundles(ImportContext x, HairData hair, int[] bundleIds)
        {
            for (int k = 0; k < 4; k++)
            {
                var part = hair.Parts[k];
                var f = x.Named["hair." + k];
                if (part.Bundles.Count == 0 || !part.Item.StartsWith(ContentIds.Vanilla + "#", StringComparison.OrdinalIgnoreCase) || bundleIds[k] == f.Raw) continue;
                x.Report.Warning("hair.bundlesDropped", "hair." + HairData.PartNames[k] + ".bundles", $"bundles were saved for item {bundleIds[k]}, not {f.Raw}; HS2 discards them");
                x.Report.Count("hair.bundlesDropped");
                part.Bundles.Clear();
            }
        }

        static void NegativeZeros(ImportContext x, params object[] trees)
        {
            int n = 0;
            foreach (var t in trees) n += RawReader.CountNegativeZeros(t);
            x.Report.Count("float.negativeZero.vanilla", n);
            x.Report.Count("float.negativeZero.plugin", x.Raw.PluginNegativeZeros);
            int total = n + x.Raw.PluginNegativeZeros;
            if (total > 0) x.Report.Info("float.negativeZero", "", "−0 values normalised to +0", total);
        }

        ImportProvenance Provenance(string from, string fileName, byte[] data, DateTime now, string hs2Version, string coordSex, List<Card.ResolveInfo> resolve)
        {
            var guids = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var e in resolve)
            {
                var g = e.GUID?.Trim();
                if (!string.IsNullOrEmpty(g) && seen.Add(g)) guids.Add(g);
            }
            guids.Sort(GuidOrder.Instance);
            return new ImportProvenance
            {
                From = from, FileName = fileName ?? "", Sha256 = ResStore.Sha256Hex(data), FileSize = data.LongLength, ImportedUtc = now,
                Importer = ImporterVersion, Hs2Version = hs2Version ?? "", CoordSex = coordSex, SideloaderGuids = guids,
            };
        }

        /// <summary>
        /// `requires` details for every GUID the documents can name: the installed zipmod's manifest, else the resolve
        /// entries' Name/Author/Website. Seeded as the container's loaded manifest, so a save without
        /// <see cref="SaveOptions.RequiresInfo"/> still writes them.
        /// </summary>
        Manifest RequiresSeed(ImportContext x, List<Card.ResolveInfo> resolve)
        {
            var m = new Manifest();
            var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Add(string guid)
            {
                if (string.IsNullOrEmpty(guid) || !done.Add(guid)) return;
                var info = RequiresInfo(guid, resolve);
                if (info != null) m.Requires.Add(info);
            }
            foreach (var f in x.Refs)
            {
                var r = f.Result;
                int i = r?.LastIndexOf('#') ?? -1;
                if (i > 0) Add(r.Substring(0, i));
            }
            foreach (var e in resolve) Add(e.GUID?.Trim());
            return m;
        }

        /// <summary>Name/author/website of a GUID: the installed zipmod's manifest, else the first resolve entry that has them; null when neither knows.</summary>
        public RequiresEntry RequiresInfo(string guid, IEnumerable<Card.ResolveInfo> resolve = null)
        {
            var z = Resolve.Zipmod(guid);
            if (z != null && (z.Name != null || z.Author != null || z.Website != null))
                return new RequiresEntry { Guid = guid, Name = z.Name, Author = z.Author, Website = z.Website };
            if (resolve != null)
                foreach (var e in resolve)
                    if (string.Equals(e.GUID?.Trim(), guid, StringComparison.OrdinalIgnoreCase) && (e.Name != null || e.Author != null || e.Website != null))
                        return new RequiresEntry { Guid = guid, Name = e.Name, Author = e.Author, Website = e.Website };
            return null;
        }
    }
}
