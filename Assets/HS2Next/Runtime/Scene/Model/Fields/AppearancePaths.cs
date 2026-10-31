// Field paths under the node root `appearance`: the character documents of a node's stored
// subtree. `appearance.chara.<chara.json member path>` and `appearance.coord.<coord.json member path>`; accessories by id
// (`appearance.coord.accessories["<id>"]`). A set on such a path is a NodeChanged event with the path (canonical text), so
// the realization learns which document members changed from the ordinary scene events; this class splits
// those paths into the document and the member path the edit units are keyed by. Unity-free.
using System;
using HS2Next.Save;

namespace HS2Next.Scenes
{
    /// <summary>Which character document an appearance path addresses.</summary>
    public enum AppearanceDocument { None, Chara, Coord }

    public static class AppearancePaths
    {
        public const string Root = "appearance";
        /// <summary>The document segments.</summary>
        public const string CharaDoc = "chara", CoordDoc = "coord";
        /// <summary>Path prefixes of the two documents.</summary>
        public const string CharaPrefix = "appearance.chara", CoordPrefix = "appearance.coord";
        /// <summary>The accessory list (inserts, removals and moves report this path).</summary>
        public const string AccessoryList = "appearance.coord.accessories";

        /// <summary>The full path of a chara.json member path ("face.shape[12]" → "appearance.chara.face.shape[12]").</summary>
        public static string Chara(string member) => string.IsNullOrEmpty(member) ? CharaPrefix : CharaPrefix + "." + member;
        /// <summary>The full path of a coord.json member path ("clothes.top.item" → "appearance.coord.clothes.top.item").</summary>
        public static string Coord(string member) => string.IsNullOrEmpty(member) ? CoordPrefix : CoordPrefix + "." + member;
        /// <summary>The path of an accessory (or of a member inside it: "colors[0].color").</summary>
        public static string Accessory(string id, string member = null) =>
            AccessoryList + "[" + FieldPath.Quote(id ?? "") + "]" + (string.IsNullOrEmpty(member) ? "" : (member[0] == '[' ? member : "." + member));

        /// <summary>The full path of a path relative to `appearance.` (the bridge form "chara.face.shape[3]",
        /// "coord.accessories[\"&lt;id&gt;\"].parent"); a path that already starts with `appearance` is returned as is.</summary>
        public static string FromRelative(string rel)
        {
            if (rel == null) return null;
            if (FieldPath.IsPrefixOf(Root, rel)) return rel;
            return Root + "." + rel;
        }

        /// <summary>The path relative to `appearance.` ("chara.face.shape[3]"), or null when it is not an appearance path.</summary>
        public static string ToRelative(string path) =>
            path != null && path.Length > Root.Length + 1 && FieldPath.IsPrefixOf(Root, path) ? path.Substring(Root.Length + 1) : null;

        /// <summary>True for any path under the node root `appearance`.</summary>
        public static bool IsAppearance(string path) => path != null && FieldPath.IsPrefixOf(Root, path);

        /// <summary>
        /// Splits an appearance path into its document and the member path inside it ("" for the whole document):
        /// "appearance.chara.face.shape[12]" → (Chara, "face.shape[12]"). False for other paths.
        /// </summary>
        public static bool TrySplit(string path, out AppearanceDocument doc, out string member)
        {
            doc = AppearanceDocument.None;
            member = null;
            if (path == null) return false;
            string rest;
            if (FieldPath.IsPrefixOf(CharaPrefix, path)) { doc = AppearanceDocument.Chara; rest = path.Substring(CharaPrefix.Length); }
            else if (FieldPath.IsPrefixOf(CoordPrefix, path)) { doc = AppearanceDocument.Coord; rest = path.Substring(CoordPrefix.Length); }
            else return false;
            member = rest.Length > 0 && rest[0] == '.' ? rest.Substring(1) : rest;
            return true;
        }

        /// <summary>
        /// The member path inside its document ("face.shape[12]", "clothes.top.colors[0].base", "accessories[\"id\"].parent"),
        /// or null for a path outside the two documents. The top-level members of chara.json (profile, face, body, hair,
        /// materialOverrides, studioOverlays) and coord.json (name, sex, clothes, accessories, ruleOverrides) do not overlap,
        /// so the member path alone names the document; "" = a whole document.
        /// </summary>
        public static string DocumentPath(string path) => TrySplit(path, out _, out string m) ? m : null;

        /// <summary>The accessory id of a path under `appearance.coord.accessories["&lt;id&gt;"]` and the member path inside
        /// the entry ("" for the whole entry); false for other paths (including the list itself).</summary>
        public static bool TryAccessory(string path, out string id, out string member)
        {
            id = null;
            member = null;
            if (path == null || !FieldPath.IsPrefixOf(AccessoryList, path) || path.Length == AccessoryList.Length) return false;
            if (!FieldPath.TryParse(path, out var p, out _) || p.Count < 4 || p[3].Kind != FieldStepKind.Key) return false;
            id = p[3].Name;
            string head = AccessoryList + "[" + FieldPath.Quote(id) + "]";
            string rest = path.Length > head.Length ? path.Substring(head.Length) : "";
            member = rest.Length > 0 && rest[0] == '.' ? rest.Substring(1) : rest;
            return true;
        }

        /// <summary>
        /// Members whose change rebuilds the whole character (C8): `body.uncensor`, `studioOverlays`, and the sex of
        /// either document. True when <paramref name="path"/> is one of them or lies inside one.
        /// </summary>
        public static bool IsRebuildPath(string path)
        {
            if (path == null) return false;
            foreach (var r in RebuildPaths) if (FieldPath.IsPrefixOf(r, path)) return true;
            return false;
        }

        /// <summary>The rebuild-only members as full paths.</summary>
        public static readonly string[] RebuildPaths =
        {
            CharaPrefix + ".body.uncensor", CharaPrefix + ".studioOverlays", CharaPrefix + ".profile.sex", CoordPrefix + ".sex",
        };

        /// <summary>
        /// The edit area of a path for the history label: "face", "body", "hair", "clothes", "accessory", "name"
        /// (profile.name), "character" (everything else), or null for a path outside `appearance`.
        /// </summary>
        public static string Area(string path)
        {
            if (!TrySplit(path, out var doc, out string m)) return null;
            if (doc == AppearanceDocument.Chara)
            {
                if (FieldPath.IsPrefixOf("face", m)) return "face";
                if (FieldPath.IsPrefixOf("body", m)) return "body";
                if (FieldPath.IsPrefixOf("hair", m)) return "hair";
                if (m == "profile.name") return "name";
                return "character";
            }
            if (FieldPath.IsPrefixOf("clothes", m)) return "clothes";
            if (FieldPath.IsPrefixOf("accessories", m)) return "accessory";
            return "character";
        }

        /// <summary>The hair part index of a path under `appearance.chara.hair.&lt;part&gt;` (0 back … 3 option) and the member
        /// path inside the part; -1 for other paths.</summary>
        public static int HairPart(string path, out string member)
        {
            member = null;
            if (!TrySplit(path, out var doc, out string m) || doc != AppearanceDocument.Chara || !FieldPath.IsPrefixOf("hair", m)) return -1;
            for (int k = 0; k < HairData.PartNames.Length; k++)
            {
                string pre = "hair." + HairData.PartNames[k];
                if (!FieldPath.IsPrefixOf(pre, m)) continue;
                string rest = m.Substring(pre.Length);
                member = rest.Length > 0 && rest[0] == '.' ? rest.Substring(1) : rest;
                return k;
            }
            return -1;
        }

        /// <summary>The clothes kind of a path under `appearance.coord.clothes.&lt;kind&gt;` (0 top … 7 shoes) and the member path
        /// inside the part; -1 for other paths.</summary>
        public static int ClothesKindOf(string path, out string member)
        {
            member = null;
            if (!TrySplit(path, out var doc, out string m) || doc != AppearanceDocument.Coord || !FieldPath.IsPrefixOf("clothes", m)) return -1;
            for (int k = 0; k < ClothesSet.Names.Length; k++)
            {
                string pre = "clothes." + ClothesSet.Names[k];
                if (!FieldPath.IsPrefixOf(pre, m)) continue;
                string rest = m.Substring(pre.Length);
                member = rest.Length > 0 && rest[0] == '.' ? rest.Substring(1) : rest;
                return k;
            }
            return -1;
        }
    }
}
