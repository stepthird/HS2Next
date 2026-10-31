// The Unity-free row source of the Maker lists: one character-list row with its columns,
// and IListRows, the interface MakerLists is written against. The Unity layer implements it over ContentCatalog
// (Unity/CatalogListRows.cs); the plain-Mono tests over the zipmod scan cache and a vanilla list fixture
// (Editor/MakerLists/MakerFixture.cs).
using System;
using System.Collections.Generic;
using System.Globalization;

namespace HS2Next.Content.Maker
{
    /// <summary>One character-list row (vanilla or zipmod) as the Maker lists see it.</summary>
    public sealed class ListRow
    {
        static readonly IReadOnlyDictionary<string, string> NoValues = new Dictionary<string, string>();

        /// <summary>List category (`ChaListDefine.CategoryNo`).</summary>
        public readonly int Category;
        /// <summary>The row's reference (`illusion.hs2#id` for vanilla rows, `guid#id` for zipmod rows).</summary>
        public readonly ContentId Key;
        /// <summary>Column name to value, as in the list file (shared with the catalog row, not copied).</summary>
        public readonly IReadOnlyDictionary<string, string> Values;
        /// <summary>Path of the zipmod that defines the row, or null for a source's own (vanilla) rows.</summary>
        public readonly string ZipPath;
        /// <summary>A vanilla row the base game's `distributionNo` gate drops on this install; false for mod rows.</summary>
        public readonly bool Gated;

        public ListRow(int category, ContentId key, IReadOnlyDictionary<string, string> values, string zipPath = null, bool gated = false)
        {
            Category = category; Key = key; Values = values ?? NoValues; ZipPath = zipPath; Gated = gated;
        }

        /// <summary>Column value, or null when the row has no such column.</summary>
        public string this[string column] => column != null && Values.TryGetValue(column, out var v) ? v : null;

        public bool IsVanilla => Key.IsVanilla;

        /// <summary>The `Possess` column as an integer (99 = hidden from the Maker lists), or 0.</summary>
        public int Possess => int.TryParse(this["Possess"], NumberStyles.Integer, CultureInfo.InvariantCulture, out int p) ? p : 0;

        public override string ToString() => $"cat {Category} {Key} {this["Name"]}";
    }

    /// <summary>
    /// Rows and zipmod manifest infos for <see cref="MakerLists"/>. Rows of a category come in catalog order (vanilla first in
    /// bundle order, then zipmods in the catalog's load order) after the first-definition-wins rule; <see cref="ListRow.Gated"/>
    /// rows are included when the source has them (the Maker lists hide them unless the gate is open).
    /// </summary>
    public interface IListRows
    {
        /// <summary>The rows of one category (empty when none).</summary>
        IReadOnlyList<ListRow> RowsOf(int category);

        /// <summary>The row of (category, key), or null.</summary>
        ListRow Find(int category, ContentId key);

        /// <summary>
        /// The references of <paramref name="category"/> defined several times inside their zipmod, with every definition in file
        /// order (the first is the row of <see cref="Find"/>; the base game and the catalog keep only it, Sideloader keeps each
        /// as a row of its own); null or empty when there are none. The Maker's face-skin lists use them (H3).
        /// </summary>
        IReadOnlyDictionary<ContentId, IReadOnlyList<ListRow>> RepeatedDefinitions(int category);

        /// <summary>The `faceSkinInfo` / `headPresetInfo` elements of the zipmod at <paramref name="zipPath"/>, or null.</summary>
        ZipMakerInfo InfoOf(string zipPath);

        /// <summary>Every zipmod's infos in Sideloader's load order (GUID order), for the head-preset lookup.</summary>
        IReadOnlyList<ZipMakerInfo> InfosInLoadOrder { get; }
    }

    /// <summary>Sideloader's zipmod load order: GUIDs compared as lower-case ordinal strings (as `StudioCsv.LoadOrder`).</summary>
    public sealed class GuidLoadOrder : IComparer<string>
    {
        public static readonly GuidLoadOrder Instance = new GuidLoadOrder();
        public int Compare(string x, string y) => string.CompareOrdinal(x?.ToLowerInvariant(), y?.ToLowerInvariant());
    }
}
