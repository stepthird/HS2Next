// One row of the list DB (a part: head, hair, clothes, accessory, ...), keyed by its list
// category and its collision-free ContentId.
using System.Collections.Generic;

namespace HS2Next.Content
{
    public class ListEntry
    {
        /// <summary>List category (the list files' categoryNo).</summary>
        public int Category;

        /// <summary>
        /// The row's reference: (<see cref="ContentIds.Vanilla"/>, original ID) for base-game rows,
        /// else (zipmod or pack GUID, ID inside that GUID).
        /// </summary>
        public ContentId Key;

        /// <summary>True for base-game rows.</summary>
        public bool IsVanilla => Key.IsVanilla;

        /// <summary>The source the row came from.</summary>
        public IContentSource Source;

        /// <summary>The zipmod that defines the row, or null for rows from a source's own lists.</summary>
        public ZipmodInfo Zipmod;

        /// <summary>Path of <see cref="Zipmod"/>, or null.</summary>
        public string ZipPath => Zipmod?.Path;

        /// <summary>Column name to value, as in the list file.</summary>
        public Dictionary<string, string> Values = new Dictionary<string, string>();

        /// <summary>Column value, or null if the row has no such column.</summary>
        public string this[string key] => Values.TryGetValue(key, out var v) ? v : null;

        public override string ToString() => $"cat {Category} {Key} {this["Name"]}";
    }
}
