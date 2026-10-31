// Test helper: HS2 card bytes → importer → character file bytes
// (fixed clock, placeholder thumbnail) → loaded again → document build input. Used by CardImportTest -source document,
// CompareTest -source document|both and CharaDocBuildTest, so every test builds from what a saved file holds.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using HS2Next.Content;
using HS2Next.Save;
using HS2Next.Scenes;

namespace HS2Next.Chara.Documents
{
    public sealed class DocumentRoundTripResult
    {
        /// <summary>The container the importer returned (null when the import failed).</summary>
        public CharaContainer Imported;
        /// <summary>The character file bytes written from <see cref="Imported"/>.</summary>
        public byte[] Bytes;
        /// <summary>The file loaded from <see cref="Bytes"/>; the documents the build reads.</summary>
        public CharaContainer Reloaded;
        /// <summary>The build input of <see cref="Reloaded"/>.</summary>
        public CharaBuildInput Input;
        /// <summary>Importer findings (paths prefixed with the card file name), load findings and adapter findings.</summary>
        public readonly FindingList ImportFindings = new FindingList(), LoadFindings = new FindingList(), BuildFindings = new FindingList();
        public double ImportMs, SaveMs, LoadMs, AdapterMs;
        /// <summary>Why no input was produced; null on success.</summary>
        public string Error;
        public bool Ok => Input != null && Error == null;
    }

    public static class DocumentRoundTrip
    {
        /// <summary>The clock of the round trip's save (manifest times do not depend on the run).</summary>
        public static readonly DateTime FixedTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>Card file → importer → file bytes → load → build input.</summary>
        public static DocumentRoundTripResult Run(ICharaImporter importer, string cardPath, ContentCatalog catalog, Action<CharaContainer> edit = null) =>
            Run(importer, File.ReadAllBytes(cardPath), cardPath, catalog, edit);

        /// <param name="edit">Optional change of the imported container before it is saved (tests, e.g. emptied hs2.kkex stores).</param>
        public static DocumentRoundTripResult Run(ICharaImporter importer, byte[] cardBytes, string cardPath, ContentCatalog catalog, Action<CharaContainer> edit = null)
        {
            if (importer == null) throw new ArgumentNullException(nameof(importer));
            var r = new DocumentRoundTripResult();
            var src = new CardAppearanceSource
            {
                FullPath = cardPath, Sha256 = ResStore.Sha256Hex(cardBytes), ReadBytes = () => cardBytes,
                Name = Path.GetFileNameWithoutExtension(cardPath ?? "card"),
            };
            var sw = Stopwatch.StartNew();
            try { r.Imported = importer.Import(src, r.ImportFindings); }
            catch (Exception e) { r.Error = "import threw " + e.GetType().Name + ": " + e.Message; return r; }
            r.ImportMs = sw.Elapsed.TotalMilliseconds;
            if (r.Imported == null) { r.Error = "the importer returned nothing"; return r; }
            try
            {
                edit?.Invoke(r.Imported);
                sw.Restart();
                r.Bytes = r.Imported.ToBytes(new SaveOptions { Clock = () => FixedTime, Thumbnail = ThumbnailMode.Placeholder });
                r.SaveMs = sw.Elapsed.TotalMilliseconds;
                sw.Restart();
                var lr = CharaContainer.Load(r.Bytes);
                r.LoadMs = sw.Elapsed.TotalMilliseconds;
                r.LoadFindings.AddRange(lr.Findings);
                if (!lr.Ok) { r.Error = "reload failed: " + lr.Error.Message; return r; }
                r.Reloaded = lr.Value;
                sw.Restart();
                r.Input = CharaDocumentInput.From(r.Reloaded, catalog, r.BuildFindings);
                r.AdapterMs = sw.Elapsed.TotalMilliseconds;
            }
            catch (Exception e) { r.Error = "round trip threw " + e.GetType().Name + ": " + e.Message; }
            return r;
        }

        /// <summary>Empties the hs2.kkex stores of both scopes (P2: a build must not depend on them).</summary>
        public static void StripKkex(CharaContainer c)
        {
            if (c?.Subtree == null) return;
            c.Subtree.CharaExt.Kkex = null;
            c.Subtree.CoordExt.Kkex = null;
        }
    }
}
