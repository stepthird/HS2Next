// Errors and findings of the save-format code. A
// SaveFormatException aborts a load or save with one of the SaveErrorCode values; everything that
// does not abort is a Finding (severity, code, path, message, count) collected in a FindingList,
// which merges repeats of one (code, path) into a count.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace HS2Next.Save
{
    /// <summary>Error codes plus <c>NonFinite</c>, <c>TooLarge</c> and <c>IoFailure</c>.</summary>
    public enum SaveErrorCode
    {
        NotAContainer,
        Hs2Card,
        Hs2Coordinate,
        NotHs2Next,
        CorruptContainer,
        UnsupportedZip,
        UnsafeEntryName,
        DuplicateEntry,
        EntryCorrupt,
        TooLarge,
        ReaderTooOld,
        BadManifest,
        BadDocument,
        MissingDocument,
        NonFinite,
        IoFailure,
        MissingRequiredExt,
    }

    /// <summary>A load or save failure. <see cref="Entry"/> and <see cref="JsonPath"/> locate it when known.</summary>
    public class SaveFormatException : Exception
    {
        public readonly SaveErrorCode Code;
        /// <summary>Entry name inside the container ("chara.json", "res/…"), or null.</summary>
        public readonly string Entry;
        /// <summary>JSON path inside <see cref="Entry"/> ("face.shape[3]"), or null.</summary>
        public readonly string JsonPath;
        /// <summary>1-based line in the JSON text, or 0 when unknown.</summary>
        public readonly int Line;
        /// <summary>The bare message without code and location.</summary>
        public readonly string Detail;

        public SaveFormatException(SaveErrorCode code, string detail, string entry = null, string jsonPath = null, int line = 0, Exception inner = null)
            : base(Format(code, detail, entry, jsonPath, line), inner)
        {
            Code = code;
            Detail = detail;
            Entry = entry;
            JsonPath = jsonPath;
            Line = line;
        }

        static string Format(SaveErrorCode code, string detail, string entry, string jsonPath, int line)
        {
            var sb = new StringBuilder(code.ToString());
            string where = Location(entry, jsonPath);
            if (where.Length > 0) sb.Append(" at ").Append(where);
            if (line > 0) sb.Append(" (line ").Append(line).Append(')');
            sb.Append(": ").Append(detail);
            return sb.ToString();
        }

        /// <summary>"entry:path", "entry", "path" or "".</summary>
        public static string Location(string entry, string jsonPath)
        {
            bool e = !string.IsNullOrEmpty(entry), p = !string.IsNullOrEmpty(jsonPath);
            return e && p ? entry + ":" + jsonPath : e ? entry : p ? jsonPath : "";
        }
    }

    public enum FindingSeverity { Info, Warning, Error }

    /// <summary>One reported condition. Repeats of one code and path are merged into <see cref="Count"/>.</summary>
    public sealed class Finding
    {
        public FindingSeverity Severity;
        public string Code;
        /// <summary>"entry:jsonPath", an entry name, a JSON path, or "".</summary>
        public string Path;
        public string Message;
        public int Count = 1;

        public override string ToString() =>
            Severity.ToString().ToLowerInvariant() + " " + Code + (string.IsNullOrEmpty(Path) ? "" : " [" + Path + "]") +
            (Count > 1 ? " x" + Count : "") + ": " + Message;
    }

    /// <summary>Ordered findings; a repeat of (code, path) increments the first one's count (severity = the highest seen).</summary>
    public sealed class FindingList : IReadOnlyList<Finding>
    {
        readonly List<Finding> items = new List<Finding>();
        readonly Dictionary<string, Finding> byKey = new Dictionary<string, Finding>(StringComparer.Ordinal);

        public int Count => items.Count;
        public Finding this[int index] => items[index];
        public IEnumerator<Finding> GetEnumerator() => items.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => items.GetEnumerator();

        public Finding Add(FindingSeverity severity, string code, string path, string message)
        {
            string key = code + "\u0000" + (path ?? "");
            if (byKey.TryGetValue(key, out var f))
            {
                f.Count++;
                if (severity > f.Severity) f.Severity = severity;
                return f;
            }
            f = new Finding { Severity = severity, Code = code, Path = path ?? "", Message = message ?? "" };
            items.Add(f);
            byKey[key] = f;
            return f;
        }

        public Finding Info(string code, string path, string message) => Add(FindingSeverity.Info, code, path, message);
        public Finding Warning(string code, string path, string message) => Add(FindingSeverity.Warning, code, path, message);
        public Finding Error(string code, string path, string message) => Add(FindingSeverity.Error, code, path, message);

        /// <summary>Appends every finding of <paramref name="other"/> (merging as <see cref="Add"/>).</summary>
        public void AddRange(IEnumerable<Finding> other)
        {
            foreach (var f in other)
            {
                var g = Add(f.Severity, f.Code, f.Path, f.Message);
                if (g != f) g.Count += f.Count - 1;
            }
        }

        public bool Has(string code) => items.Exists(f => f.Code == code);

        /// <summary>Sum of the counts of every finding with this code.</summary>
        public int CountOf(string code)
        {
            int n = 0;
            foreach (var f in items) if (f.Code == code) n += f.Count;
            return n;
        }

        public bool HasErrors => items.Exists(f => f.Severity == FindingSeverity.Error);

        public override string ToString() => string.Join("\n", items);
    }
}
