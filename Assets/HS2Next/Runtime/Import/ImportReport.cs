// Import report and import options. The report is returned to the caller (Bridge, UI,
// tests) and logged; it is never stored in the container.
using System;
using System.Collections.Generic;
using System.Text;
using HS2Next.Save;

namespace HS2Next.Import
{
    /// <summary>What an HS2 source file is.</summary>
    public enum Hs2SourceKind
    {
        /// <summary>Neither an HS2 character card nor a coordinate card (or unreadable before the marker).</summary>
        None,
        /// <summary>A character card (marker `【AIS_Chara】`, PNG or a Maker autosave without PNG).</summary>
        Chara,
        /// <summary>A coordinate card (marker `【AIS_Clothes】`).</summary>
        Coord,
    }

    public enum ImportStatus { Ok, Warnings, Failed }

    /// <summary>`ImportReport` = { source path, kind, status, findings, counts }.</summary>
    public sealed class ImportReport
    {
        /// <summary>The file the import read (or the name the caller gave for in-memory bytes).</summary>
        public string SourcePath;
        /// <summary>`chara` or `coord` (the importer that ran; `none` when the file was not a card).</summary>
        public string Kind = "none";
        /// <summary>Findings, merged by (code, path) with a count.</summary>
        public readonly FindingList Findings = new FindingList();
        /// <summary>Counters (entries, items, drops) for the census and the tests; keys are finding-like codes.</summary>
        public readonly SortedDictionary<string, long> Counts = new SortedDictionary<string, long>(StringComparer.Ordinal);
        /// <summary>Import time without thumbnail rendering, in milliseconds.</summary>
        public double Milliseconds;

        /// <summary>`failed` when an error finding exists, `warnings` when a warning exists, else `ok`.</summary>
        public ImportStatus Status
        {
            get
            {
                var s = ImportStatus.Ok;
                foreach (var f in Findings)
                {
                    if (f.Severity == FindingSeverity.Error) return ImportStatus.Failed;
                    if (f.Severity == FindingSeverity.Warning) s = ImportStatus.Warnings;
                }
                return s;
            }
        }

        /// <summary>The status as the spec names it: `ok`, `warnings`, `failed`.</summary>
        public string StatusText => Status == ImportStatus.Ok ? "ok" : Status == ImportStatus.Warnings ? "warnings" : "failed";

        public long CountOf(string key) => Counts.TryGetValue(key, out var n) ? n : 0;

        public void Count(string key, long n = 1)
        {
            if (n == 0) return;
            Counts.TryGetValue(key, out var v);
            Counts[key] = v + n;
        }

        /// <summary>Adds a finding <paramref name="n"/> times (merged into one finding with a count).</summary>
        internal Finding Add(FindingSeverity severity, string code, string path, string message, int n = 1)
        {
            if (n <= 0) return null;
            var f = Findings.Add(severity, code, path, message);
            if (n > 1) f.Count += n - 1;
            return f;
        }

        internal void Info(string code, string path, string message, int n = 1) => Add(FindingSeverity.Info, code, path, message, n);
        internal void Warning(string code, string path, string message, int n = 1) => Add(FindingSeverity.Warning, code, path, message, n);
        internal void Error(string code, string path, string message, int n = 1) => Add(FindingSeverity.Error, code, path, message, n);

        /// <summary>A multi-line text: header, findings, counts.</summary>
        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(Kind).Append(' ').Append(StatusText).Append(' ').Append(SourcePath).AppendLine();
            foreach (var f in Findings) sb.Append("  ").AppendLine(f.ToString());
            foreach (var kv in Counts) sb.Append("  # ").Append(kv.Key).Append(' ').Append(kv.Value).AppendLine();
            return sb.ToString();
        }
    }

    /// <summary>Renders the thumbnail of an imported file. The Unity layer implements it; headless runs leave it null
    /// and the container keeps no thumbnail (the save writes the placeholder or a later renderer's image).</summary>
    public interface IImportThumbnailRenderer
    {
        /// <summary>A 504 × 704 PNG of the imported character, or null when none can be rendered.</summary>
        byte[] RenderChara(CharaContainer chara);
        /// <summary>A 504 × 704 PNG of the imported coordinate worn on a character of <c>coord.sex</c>, or null.</summary>
        byte[] RenderCoord(CoordContainer coord);
    }

    public sealed class ImportOptions
    {
        /// <summary>Time source for `import.importedUtc` and the container's creation time (tests use a fixed clock); null = now.</summary>
        public Func<DateTime> Clock;
        /// <summary>App version in `import.importer` (`&lt;app&gt;+imp.&lt;revision&gt;`); null = <see cref="SaveFormats.AppVersion"/>.</summary>
        public string AppVersion;
        /// <summary>Thumbnail hook; null = no thumbnail at import.</summary>
        public IImportThumbnailRenderer Thumbnails;

        internal DateTime Now => JsonText.TruncateToSeconds(Clock != null ? Clock() : DateTime.UtcNow);
    }
}
