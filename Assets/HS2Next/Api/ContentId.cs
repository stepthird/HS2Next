// Collision-free part reference: the namespace GUID of the source that
// defines a part plus the part's number inside that namespace. Vanilla rows use
// ContentIds.Vanilla ("illusion.hs2") and their original integer IDs; zipmod rows use the
// manifest GUID and the ID from the zipmod's own list (the Sideloader resolve "Slot"); packs use
// their own GUID. IDs are never renumbered at load time.
//
// Kept free of Unity and other HS2Next types so list tables, importers and save files can share
// it. The string form "<guid>#<id>" is the serialized form (JSON etc.). Part of the stable API
// (assembly HS2Next.Api).
using System;
using System.Globalization;

namespace HS2Next.Content
{
    /// <summary>
    /// A part reference: (source GUID, local ID). The GUID compares case-insensitively (ordinal).
    /// <c>default(ContentId)</c> is the empty reference (<see cref="IsEmpty"/>), not a vanilla ID.
    /// </summary>
    public readonly struct ContentId : IEquatable<ContentId>, IComparable<ContentId>
    {
        /// <summary>Separator between GUID and ID in the string form.</summary>
        public const char Separator = '#';

        static readonly StringComparer GuidComparer = StringComparer.OrdinalIgnoreCase;

        /// <summary>Namespace: <see cref="ContentIds.Vanilla"/> for base-game rows, else a zipmod or pack GUID. Null only for the empty reference.</summary>
        public readonly string SourceGuid;

        /// <summary>The ID inside the namespace (for zipmods: the ID from the zipmod's own list).</summary>
        public readonly int Id;

        /// <param name="sourceGuid">Non-empty namespace GUID.</param>
        /// <param name="id">ID inside that namespace.</param>
        /// <exception cref="ArgumentException">The GUID is null or empty.</exception>
        public ContentId(string sourceGuid, int id)
        {
            if (string.IsNullOrEmpty(sourceGuid)) throw new ArgumentException("ContentId needs a source GUID", nameof(sourceGuid));
            SourceGuid = sourceGuid;
            Id = id;
        }

        /// <summary>The base-game reference for an original HS2 ID.</summary>
        public static ContentId Vanilla(int id) => new ContentId(ContentIds.Vanilla, id);

        /// <summary>True for <c>default(ContentId)</c>.</summary>
        public bool IsEmpty => SourceGuid == null;

        /// <summary>True if the reference is in the base-game namespace.</summary>
        public bool IsVanilla => SourceGuid != null && GuidComparer.Equals(SourceGuid, ContentIds.Vanilla);

        /// <summary>The GUID for mod/pack references, or null for vanilla and empty references.</summary>
        public string ModGuid => IsVanilla ? null : SourceGuid;

        public bool Equals(ContentId other) => Id == other.Id && GuidComparer.Equals(SourceGuid, other.SourceGuid);

        public override bool Equals(object obj) => obj is ContentId o && Equals(o);

        public override int GetHashCode() =>
            unchecked((SourceGuid == null ? 0 : GuidComparer.GetHashCode(SourceGuid)) * 397 ^ Id);

        public static bool operator ==(ContentId a, ContentId b) => a.Equals(b);
        public static bool operator !=(ContentId a, ContentId b) => !a.Equals(b);

        /// <summary>Orders by GUID (ordinal, case-insensitive; empty first), then by ID.</summary>
        public int CompareTo(ContentId other)
        {
            int c = GuidComparer.Compare(SourceGuid, other.SourceGuid);
            return c != 0 ? c : Id.CompareTo(other.Id);
        }

        /// <summary>"&lt;guid&gt;#&lt;id&gt;" (invariant culture), or "" for the empty reference. Round-trips through <see cref="Parse"/>.</summary>
        public override string ToString() =>
            IsEmpty ? "" : SourceGuid + Separator + Id.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// Parses "&lt;guid&gt;#&lt;id&gt;". The last '#' separates the ID, so GUIDs may contain '#'.
        /// The ID is a decimal integer with an optional leading '-'. "" parses to the empty reference.
        /// </summary>
        public static bool TryParse(string s, out ContentId result)
        {
            result = default;
            if (s == null) return false;
            if (s.Length == 0) return true;
            int i = s.LastIndexOf(Separator);
            if (i <= 0 || i == s.Length - 1) return false;
            if (!int.TryParse(s.Substring(i + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int id)) return false;
            result = new ContentId(s.Substring(0, i), id);
            return true;
        }

        /// <summary>Like <see cref="TryParse"/> but throws <see cref="FormatException"/> on bad input.</summary>
        public static ContentId Parse(string s) =>
            TryParse(s, out var r) ? r : throw new FormatException($"not a ContentId: \"{s}\" (expected \"<guid>#<id>\")");
    }
}
