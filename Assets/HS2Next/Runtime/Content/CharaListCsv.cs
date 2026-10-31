// Sideloader-style character list CSVs inside zipmods (abdata/list/characustom**/*.csv,
// 4): which zip entries are character lists and how their text is
// read. Parsing happens at zipmod scan time; the rows are stored in the per-source scan cache.
//
// Read like Sideloader's list loader: lines by CRLF/LF/CR; line 1 = category (first comma field,
// trimmed), line 2 = distributionNo (same), line 3 = file path (ignored), line 4 = column keys (the
// line trimmed, then split on every ','; keys are not trimmed one by one); then one row per line,
// each line trimmed and split on every ',' (no quoting; fields are not trimmed), until the first
// line without a ','. Excel-style trailing commas ("144,,,,") are fine. Unlike the base game's own
// lists, mod rows are not gated by distributionNo, and `Possess` is forced to 1.
using System;
using System.Collections.Generic;
using HS2Next.Content.Studio;

namespace HS2Next.Content
{
    /// <summary>One character list CSV of a zipmod, read the way Sideloader reads it.</summary>
    public sealed class CharaListCsvFile
    {
        /// <summary>List category (`ChaListDefine.CategoryNo`), from line 1.</summary>
        public int Category;
        /// <summary>distributionNo from line 2. Kept for information: Sideloader does not gate mod rows on it.</summary>
        public int DistributionNo;
        /// <summary>Column keys (line 4), as written; empty keys come from trailing commas.</summary>
        public string[] Keys = Array.Empty<string>();
        /// <summary>
        /// Rows in file order: the local id (column 0, the value Sideloader keeps as the resolve "Slot"
        /// that cards store) and the fields, aligned with <see cref="Keys"/>.
        /// </summary>
        public readonly List<(int id, string[] fields)> Rows = new List<(int, string[])>();
        /// <summary>Rows dropped because column 0 is not an integer (Sideloader fails on them).</summary>
        public int BadIdRows;
    }

    /// <summary>Reader for Sideloader character list CSV text.</summary>
    public static class CharaListCsv
    {
        /// <summary>
        /// True for zip entries that are character lists: end with ".csv" and start with
        /// "abdata/list/characustom" (case-insensitive; like Sideloader, without a trailing '/').
        /// </summary>
        public static bool IsCharaListEntry(string entryName) =>
            entryName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
            && entryName.StartsWith("abdata/list/characustom", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Parses decoded CSV text (see the file header for the rules). Returns null with a reason when
        /// Sideloader would reject the whole file: fewer than 4 lines, a non-integer category or
        /// distributionNo, or a row too short for the `Possess` column.
        /// </summary>
        public static CharaListCsvFile Parse(string text, out string error)
        {
            error = null;
            var lines = new List<string>(StudioCsv.Lines(text ?? ""));
            if (lines.Count < 4) { error = "fewer than 4 lines"; return null; }
            var f = new CharaListCsvFile();
            if (!StudioCsv.TryInt(FirstField(lines[0]), out f.Category)) { error = "line 1 (category) is not an integer"; return null; }
            if (!StudioCsv.TryInt(FirstField(lines[1]), out f.DistributionNo)) { error = "line 2 (distributionNo) is not an integer"; return null; }
            f.Keys = lines[3].Trim().Split(',');
            int possess = Array.IndexOf(f.Keys, "Possess");
            for (int i = 4; i < lines.Count; i++)
            {
                var line = lines[i].Trim();
                if (line.IndexOf(',') < 0) break;
                var fields = line.Split(',');
                if (possess >= 0)
                {
                    // Sideloader sets Possess = 1 ("owned") on every mod row; a row without that column fails the file.
                    if (possess >= fields.Length) { error = $"row {i + 1} has no Possess column"; return null; }
                    fields[possess] = "1";
                }
                // Sideloader resolves the row by column 0 and fails the zipmod on a non-integer; such rows are dropped here.
                if (!StudioCsv.TryInt(fields[0], out int id)) { f.BadIdRows++; continue; }
                f.Rows.Add((id, fields));
            }
            return f;
        }

        static string FirstField(string line)
        {
            int c = line.IndexOf(',');
            return (c < 0 ? line : line.Substring(0, c)).Trim();
        }
    }
}
