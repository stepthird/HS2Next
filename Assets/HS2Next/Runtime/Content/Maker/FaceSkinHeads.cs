// The head a face-skin row is listed under, with Sideloader's `faceSkinInfo` semantics
// (behaviour only): a mod skin row of 211 / 111 whose zipmod manifest has a faceSkinInfo with skinID = the row's id (the
// first such line) is listed under the head (headGUID, headID) of 210 / 110, unless that head is not installed or the
// entry's headID differs from the row's HeadID (Sideloader logs an error and keeps the raw value); every other row keeps
// its raw HeadID, which names a vanilla head. Sideloader binds an entry to the skin row by (GUID, id) in 211 first, then
// 111, so a male skin row whose GUID also has a female skin row of the same id gets no entry. A skin id defined several
// times inside one zipmod is one row per definition in Sideloader (H3, measured): the entry re-points only the first
// definition, the others keep their raw HeadID (MakerLists lists the reference under every such head). Unity-free.
using System.Globalization;

namespace HS2Next.Content.Maker
{
    public static class FaceSkinHeads
    {
        /// <summary>How the head of a skin row was decided.</summary>
        public enum How
        {
            /// <summary>A vanilla skin row: its HeadID.</summary>
            Vanilla,
            /// <summary>A mod skin row re-pointed by its faceSkinInfo entry.</summary>
            FaceSkinInfo,
            /// <summary>A mod skin row without an entry: the raw HeadID (a vanilla head).</summary>
            Raw,
            /// <summary>An entry whose headID differs from the row's HeadID: ignored, raw HeadID (finding `list.faceSkinMismatch`).</summary>
            Mismatch,
            /// <summary>An entry naming a head that is not installed: ignored, raw HeadID.</summary>
            HeadMissing,
            /// <summary>The row has no integer HeadID: listed under no head.</summary>
            NoHeadId,
        }

        public static bool IsSkinCategory(int category) => category == 211 || category == 111;

        /// <summary>The head category of a face-skin category (211 → 210, 111 → 110), or -1.</summary>
        public static int HeadCategoryOf(int skinCategory) => skinCategory == 211 ? 210 : skinCategory == 111 ? 110 : -1;

        /// <summary>The head key of <paramref name="skin"/> (empty when it names none) and how it was decided.</summary>
        public static ContentId HeadOf(ListRow skin, IListRows rows, out How how, out FaceSkinEntry entry) =>
            HeadOf(skin, rows, skin == null || skin.IsVanilla ? null : rows?.InfoOf(skin.ZipPath), out how, out entry);

        /// <summary>As above with the zipmod infos of the row's zipmod given by the caller (<paramref name="info"/>; null = none).</summary>
        public static ContentId HeadOf(ListRow skin, IListRows rows, ZipMakerInfo info, out How how, out FaceSkinEntry entry)
        {
            entry = null;
            if (!int.TryParse(skin?["HeadID"], NumberStyles.Integer, CultureInfo.InvariantCulture, out int raw)) { how = How.NoHeadId; return default; }
            var vanilla = ContentId.Vanilla(raw);
            if (skin.IsVanilla) { how = How.Vanilla; return vanilla; }
            entry = info?.FaceSkinFor(skin.Key.Id);
            if (entry != null && skin.Category == 111 && rows.Find(211, skin.Key) != null) entry = null;
            if (entry == null) { how = How.Raw; return vanilla; }
            var head = new ContentId(entry.HeadGuid, entry.HeadId);
            if (rows.Find(HeadCategoryOf(skin.Category), head) == null) { how = How.HeadMissing; return vanilla; }
            if (entry.HeadId != raw) { how = How.Mismatch; return vanilla; }
            how = How.FaceSkinInfo;
            return head;
        }

        public static ContentId HeadOf(ListRow skin, IListRows rows) => HeadOf(skin, rows, out _, out _);
    }
}
