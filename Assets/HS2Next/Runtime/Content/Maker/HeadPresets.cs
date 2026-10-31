// Face presets of the head rows: which `<headPresetInfo>` entry belongs to a head, and how
// the preset's local ids become `ref`s. Sideloader semantics (read for behaviour only): when a face preset is loaded, the
// entry is the first one (in zipmod load order) whose headID equals the head's id inside its GUID, whose headGUID equals
// the head's GUID (none for a vanilla head) and whose `preset` equals the head row's `Preset` column; each non-blank GUID
// element names the GUID the preset's local id of that property belongs to and the id is resolved there (a GUID whose row
// does not exist leaves the raw id, i.e. a vanilla id); properties without a GUID keep the raw id (vanilla).
// The preset TextAsset itself (a serialized ChaFileFace in the head row's MainAB) is loaded and decoded by the Unity layer
// (Unity/FacePresetLoader.cs); this file is Unity-free.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace HS2Next.Content.Maker
{
    /// <summary>One face property a preset carries: its `headPresetInfo` element, Sideloader's property name, the document member and categories.</summary>
    public sealed class PresetProperty
    {
        /// <summary>`headPresetInfo` element naming the GUID ("headGUID", "skinGUID", …).</summary>
        public readonly string Element;
        /// <summary>The property name in Sideloader's face / makeup tables (`headId`, `Eye1`, `PaintLayoutID2`, …).</summary>
        public readonly string Property;
        /// <summary>The `chara.json` member, e.g. `face.eyes.left.iris.item`.</summary>
        public readonly string Member;
        public readonly int FemaleCategory, MaleCategory;

        public PresetProperty(string element, string property, string member, int female, int male)
        {
            Element = element; Property = property; Member = member; FemaleCategory = female; MaleCategory = male;
        }

        public int CategoryFor(int sex) => sex == 0 ? MaleCategory : FemaleCategory;
        public override string ToString() => $"{Element} -> {Member}";
    }

    public static class HeadPresets
    {
        /// <summary>The GUID elements of `headPresetInfo` besides headGUID, in a fixed order (the cache column order).</summary>
        public static readonly string[] Elements =
        {
            "skinGUID", "detailGUID", "eyebrowGUID", "pupil1GUID", "pupil2GUID", "black1GUID", "black2GUID", "hlGUID",
            "eyelashesGUID", "moleGUID", "eyeshadowGUID", "cheekGUID", "lipGUID", "paint1GUID", "paint2GUID", "layout1GUID",
            "layout2GUID",
        };

        /// <summary>Every property a preset resolves (Sideloader's `StructReference` face and makeup tables).</summary>
        public static readonly PresetProperty[] Properties =
        {
            new PresetProperty("headGUID", "headId", "face.head", 210, 110),
            new PresetProperty("skinGUID", "skinId", "face.skin", 211, 111),
            new PresetProperty("detailGUID", "detailId", "face.detail.item", 212, 112),
            new PresetProperty("eyebrowGUID", "eyebrowId", "face.eyebrow.item", 314, 314),
            new PresetProperty("pupil1GUID", "Eye1", "face.eyes.left.iris.item", 317, 317),
            new PresetProperty("pupil2GUID", "Eye2", "face.eyes.right.iris.item", 317, 317),
            new PresetProperty("black1GUID", "EyeBlack1", "face.eyes.left.pupil.item", 318, 318),
            new PresetProperty("black2GUID", "EyeBlack2", "face.eyes.right.pupil.item", 318, 318),
            new PresetProperty("hlGUID", "hlId", "face.eyes.highlight.item", 319, 319),
            new PresetProperty("eyelashesGUID", "eyelashesId", "face.eyelashes.item", 315, 315),
            new PresetProperty("moleGUID", "moleId", "face.mole.item", 323, 323),
            new PresetProperty("eyeshadowGUID", "eyeshadowId", "face.makeup.eyeshadow.item", 316, 316),
            new PresetProperty("cheekGUID", "cheekId", "face.makeup.cheek.item", 320, 320),
            new PresetProperty("lipGUID", "lipId", "face.makeup.lip.item", 322, 322),
            new PresetProperty("paint1GUID", "PaintID1", "face.makeup.paint[0].item", 313, 313),
            new PresetProperty("paint2GUID", "PaintID2", "face.makeup.paint[1].item", 313, 313),
            new PresetProperty("layout1GUID", "PaintLayoutID1", "face.makeup.paint[0].layoutItem", 7, 7),
            new PresetProperty("layout2GUID", "PaintLayoutID2", "face.makeup.paint[1].layoutItem", 7, 7),
        };

        /// <summary>The property of a document member, or null.</summary>
        public static PresetProperty ByMember(string member)
        {
            foreach (var p in Properties) if (p.Member == member) return p;
            return null;
        }

        /// <summary>The head row's preset name (`Preset` column), or null when it has none ("", "0").</summary>
        public static string PresetName(ListRow head)
        {
            var s = head?["Preset"];
            return string.IsNullOrEmpty(s) || s == "0" ? null : s;
        }

        /// <summary>The `headPresetInfo` entry Sideloader uses for the preset of <paramref name="head"/>, or null.</summary>
        public static HeadPresetEntry FindEntry(IListRows rows, ListRow head)
        {
            var preset = PresetName(head);
            if (preset == null || rows == null) return null;
            string guid = head.IsVanilla ? null : head.Key.SourceGuid;
            foreach (var z in rows.InfosInLoadOrder)
                foreach (var e in z.HeadPresets)
                    if (e.HeadId == head.Key.Id && e.Preset == preset
                        && (guid == null ? e.HeadGuid == null : string.Equals(e.HeadGuid, guid, StringComparison.OrdinalIgnoreCase)))
                        return e;
            return null;
        }

        /// <summary>The GUID an entry names for a property (headGUID included), or null.</summary>
        public static string GuidOf(HeadPresetEntry e, PresetProperty p) =>
            e == null ? null : p.Element == "headGUID" ? e.HeadGuid : e.Guid(p.Element);

        /// <summary>
        /// The reference of one preset id: the row (GUID named by the entry, <paramref name="localId"/>) when the entry names a
        /// GUID and the row exists in the property's category for the sex; else the vanilla id. <paramref name="unresolved"/> is
        /// true when the entry named a GUID whose row is missing (finding `edit.presetUnresolved`).
        /// </summary>
        public static ContentId Resolve(IListRows rows, HeadPresetEntry e, PresetProperty p, int localId, int sex, out bool unresolved)
        {
            unresolved = false;
            var guid = GuidOf(e, p);
            if (guid == null) return ContentId.Vanilla(localId);
            var key = new ContentId(guid, localId);
            if (rows != null && rows.Find(p.CategoryFor(sex), key) != null) return key;
            unresolved = true;
            return ContentId.Vanilla(localId);
        }

        /// <summary>
        /// Resolves the preset ids of <paramref name="head"/>'s preset (<paramref name="localIds"/>: document member → the preset's
        /// local id) to references by member. Unresolved GUID references are reported in <paramref name="findings"/>.
        /// </summary>
        public static Dictionary<string, ContentId> ResolveAll(IListRows rows, ListRow head, int sex, IReadOnlyDictionary<string, int> localIds, List<string> findings = null)
        {
            var e = FindEntry(rows, head);
            var r = new Dictionary<string, ContentId>(StringComparer.Ordinal);
            foreach (var p in Properties)
            {
                if (localIds == null || !localIds.TryGetValue(p.Member, out int id)) continue;
                r[p.Member] = Resolve(rows, e, p, id, sex, out bool bad);
                if (bad) findings?.Add($"edit.presetUnresolved {p.Member}: {GuidOf(e, p)}#{id.ToString(CultureInfo.InvariantCulture)} not installed in category {p.CategoryFor(sex)}; kept as vanilla id");
            }
            return r;
        }
    }
}
