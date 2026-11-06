// Which edit units a changed document path marks. Paths are member paths
// inside their document ("face.shape[12]", "clothes.top.colors[0].base", "accessories[\"<id>\"].parent", "accessories");
// the top-level members of chara.json and coord.json do not overlap, so no document prefix is needed. A change marks
// every unit whose path prefix overlaps it in either direction (a whole-object set such as "face" or
// "accessories[\"id\"]" marks every unit below it). Some members mark no unit (stored, not realised), some request a
// rebuild of the character instead (C8). Unity-free. A path at or under one of the material arrays is told apart first
// (MaterialObject): the editor asks what changed in it before any of this applies.
using System;
using System.Collections.Generic;
using System.Linq;

namespace HS2Next.Chara.Edit
{
    public static class UnitMap
    {
        public const string Head = "head", SkinFace = "skin.face", SkinBody = "skin.body", DrawFace = "draw.face", DrawBody = "draw.body";
        public const string Eyes = "eyes", ShapeFace = "shape.face", ShapeBody = "shape.body", Bust = "bust", HairShader = "hair.shader";
        public const string Masks = "masks", AccList = "acc.list", Profile = "profile", DynBone = "dynbone", ExtFinal = "ext.final";
        public const string ExtTextures = "ext.textures", Ownership = "ownership", AnimRefresh = "anim.refresh";
        /// <summary>The breast normals of the female body (Phase 4.2): step 6 after shape.body,
        /// marked by every body.shape change and by the build.</summary>
        public const string BustNormals = "bust.normals";
        /// <summary>The body's materials on the parts' body-material targets (after 1.0.0):
        /// step 8 after ownership, marked by the part loader and following every earlier step while there are targets.</summary>
        public const string BodyMaterialCopy = "copy.bodyMaterial";
        /// <summary>The body cuts of the clothes parts (after 1.0.0): step 8 after
        /// copy.bodyMaterial, marked by the part loader and following every earlier step while there are cuts.</summary>
        public const string BodyCut = "body.cut";
        public const string AccPrefix = "acc:";

        /// <summary>Hair part names in HS2 order (back, front, side, option) and their unit ids.</summary>
        public static readonly string[] HairParts = { "back", "front", "side", "option" };
        public static readonly string[] HairUnits = { "hair.back", "hair.front", "hair.side", "hair.option" };
        /// <summary>Clothes kind names in HS2 order (0..7) and their unit ids.</summary>
        public static readonly string[] ClothesKinds = { "top", "bottom", "innerTop", "innerBottom", "gloves", "pantyhose", "socks", "shoes" };
        public static readonly string[] ClothesUnits = { "clothes.top", "clothes.bottom", "clothes.innerTop", "clothes.innerBottom", "clothes.gloves", "clothes.pantyhose", "clothes.socks", "clothes.shoes" };

        public static string Accessory(string id) => AccPrefix + (id ?? "");
        public static bool IsAccessory(string unit, out string id)
        {
            id = unit != null && unit.StartsWith(AccPrefix, StringComparison.Ordinal) ? unit.Substring(AccPrefix.Length) : null;
            return id != null;
        }

        // chara.json member prefixes → units
        static readonly (string path, string[] units)[] charaMap = BuildChara();
        // coord.json member prefixes → units (accessories are handled by id)
        static readonly (string path, string[] units)[] coordMap = BuildCoord();

        /// <summary>Members whose change rebuilds the character: uncensor, Studio overlay switches, the sex,
        /// the Character-object MaterialEditor items when their structure changes (a value change takes the editor's
        /// value pass and does not come here).</summary>
        public static readonly string[] CharaRebuild = { "body.uncensor", "studioOverlays", "profile.sex", "materialOverrides" };
        public static readonly string[] CoordRebuild = { "sex" };

        /// <summary>Accessory members that mark no unit (stored only).</summary>
        static readonly string[] accessoryStored = { "hideCategory", "hideTiming", "id" };
        /// <summary>Accessory members whose change re-runs the slot table (the preferred slot can move the entry to another
        /// build slot; Phase 4.5).</summary>
        static readonly string[] accessoryTable = { "importSlot" };
        /// <summary>Character members whose change also marks every accessory unit (the draw type re-shades the hair-type
        /// accessories; a non-hair accessory's run only rewrites its values; Phase 4.5).</summary>
        static readonly string[] charaAccessories = { "hair.shaderType" };
        /// <summary>Accessory members whose change only re-runs the plugin final stage.</summary>
        static readonly string[] accessoryFinal = { "boneMods", "rules" };

        static (string, string[])[] BuildChara()
        {
            var l = new List<(string, string[])>
            {
                ("face.head", new[] { Head }),
                ("face.skin", new[] { SkinFace }), ("face.makeup", new[] { SkinFace }), ("face.mole", new[] { SkinFace }),
                ("body.skin.color", new[] { SkinFace, SkinBody }),
                ("body.skin.item", new[] { SkinBody }), ("body.sunburn", new[] { SkinBody }), ("body.paint", new[] { SkinBody }),
                ("face.detail", new[] { DrawFace }), ("face.eyebrow", new[] { DrawFace }), ("face.beard", new[] { DrawFace }),
                ("body.skin.gloss", new[] { DrawFace, DrawBody }), ("body.skin.metallic", new[] { DrawFace, DrawBody }),
                ("body.detail", new[] { DrawBody }), ("body.nipple", new[] { DrawBody }), ("body.underhair", new[] { DrawBody }), ("body.nail", new[] { DrawBody }),
                ("face.eyes", new[] { Eyes }), ("face.eyelashes", new[] { Eyes }),
                ("face.shape", new[] { ShapeFace }),
                ("body.shape", new[] { ShapeBody, Bust, BustNormals }),
                ("body.bust", new[] { Bust }),
                ("hair.shaderType", HairUnits),   // the shader swap is part of every hair part's materials (the hair.shader group)
                ("profile.name", new[] { Profile }),
                ("body.boneMods", new[] { ExtFinal }), ("body.visible", new[] { ExtFinal }),
                ("body.skinOverlays", new[] { SkinBody, SkinFace }),
            };
            for (int k = 0; k < HairParts.Length; k++) l.Add(("hair." + HairParts[k], new[] { HairUnits[k] }));
            return l.ToArray();
        }

        static (string, string[])[] BuildCoord()
        {
            var l = new List<(string, string[])> { ("ruleOverrides", new[] { ExtFinal }) };
            for (int k = 0; k < ClothesKinds.Length; k++)
            {
                string p = "clothes." + ClothesKinds[k], u = ClothesUnits[k];
                // the part unit swaps on an item / plugin-section change and marks the passes; colours and the break rate
                // have their own step-3 / step-4 units (pushUp is stored only)
                l.Add((p + ".item", new[] { u }));
                l.Add((p + ".hideOptions", new[] { u }));
                l.Add((p + ".overlays", new[] { u }));
                l.Add((p + ".materialOverrides", new[] { u }));
                // the colour pass rebuilds only the creators the changed members feed (ClothesCreators)
                l.Add((p + ".colors", new[] { u + "#colors" }));
                l.Add((p + ".breakRate", new[] { u + "#break", Masks }));
            }
            return l.ToArray();
        }

        /// <summary>Which document a member path belongs to: 'c' chara.json, 'k' coord.json, '\0' unknown / whole.</summary>
        public static char DocumentOf(string member)
        {
            if (string.IsNullOrEmpty(member)) return '\0';
            string top = Top(member);
            switch (top)
            {
                case "profile": case "face": case "body": case "hair": case "materialOverrides": case "studioOverlays": return 'c';
                case "name": case "sex": case "clothes": case "accessories": case "ruleOverrides": return 'k';
                default: return '\0';
            }
        }

        static string Top(string m)
        {
            int i = 0;
            while (i < m.Length && m[i] != '.' && m[i] != '[') i++;
            return m.Substring(0, i);
        }

        /// <summary>
        /// Normalises a path: "appearance.chara.face.shape[3]", "chara.face.shape[3]" (relative to `appearance.`) and
        /// "face.shape[3]" all give "face.shape[3]"; <paramref name="doc"/> is 'c' / 'k', or '\0' for a whole document
        /// ("appearance.chara" → doc 'c', member "").
        /// </summary>
        public static string Member(string path, out char doc)
        {
            doc = '\0';
            if (path == null) return null;
            string p = path;
            if (IsPrefixOf("appearance", p)) p = p.Length > 10 ? p.Substring(11) : "";
            if (IsPrefixOf("chara", p)) { doc = 'c'; p = p.Length > 5 ? p.Substring(6) : ""; return p; }
            if (IsPrefixOf("coord", p)) { doc = 'k'; p = p.Length > 5 ? p.Substring(6) : ""; return p; }
            doc = DocumentOf(p);
            return p;
        }

        /// <summary>
        /// The units a change of <paramref name="path"/> marks (added to <paramref name="units"/>); true when the change
        /// needs a rebuild of the whole character instead (<paramref name="rebuild"/> then names the member).
        /// <paramref name="accessoryIds"/> lists the ids of the current accessories (a whole-document or whole-list change
        /// marks each of them).
        /// </summary>
        public static bool Map(string path, ICollection<string> units, out string rebuild, IEnumerable<string> accessoryIds = null)
        {
            rebuild = null;
            string m = Member(path, out char doc);
            if (m == null) return false;
            bool chara = doc != 'k', coord = doc != 'c';   // '\0' (unknown or whole) touches both
            if (chara)
                foreach (var r in CharaRebuild)
                    if (Overlaps(r, m)) { rebuild = "chara:" + r; return true; }
            if (coord)
                foreach (var r in CoordRebuild)
                    if (Overlaps(r, m)) { rebuild = "coord:" + r; return true; }
            if (chara) foreach (var (p, us) in charaMap) if (Overlaps(p, m)) foreach (var u in us) units.Add(u);
            if (chara && accessoryIds != null)
                foreach (var p in charaAccessories)
                    if (Overlaps(p, m)) { foreach (var a in accessoryIds) units.Add(Accessory(a)); break; }
            if (coord)
            {
                foreach (var (p, us) in coordMap) if (Overlaps(p, m)) foreach (var u in us) units.Add(u);
                if (Overlaps("accessories", m)) MapAccessory(m, units, accessoryIds);
            }
            return false;
        }

        static void MapAccessory(string m, ICollection<string> units, IEnumerable<string> ids)
        {
            // "accessories" (the list itself or a whole-document change): the slot table and every accessory.
            if (m.Length <= "accessories".Length)
            {
                units.Add(AccList);
                if (ids != null) foreach (var a in ids) units.Add(Accessory(a));
                return;
            }
            string rest = m.Substring("accessories".Length);
            if (!TryKey(rest, out string id, out string member))
            {
                // an index form or anything else under the list: treat as a list change
                units.Add(AccList);
                if (ids != null) foreach (var i in ids) units.Add(Accessory(i));
                return;
            }
            foreach (var s in accessoryStored) if (IsPrefixOf(s, member)) return;
            foreach (var s in accessoryTable) if (IsPrefixOf(s, member)) { units.Add(AccList); return; }
            foreach (var s in accessoryFinal) if (IsPrefixOf(s, member)) { units.Add(ExtFinal); return; }
            units.Add(Accessory(id));
            if (member.Length == 0 || IsPrefixOf("dynamicBones", member)) units.Add(ExtFinal);
        }

        static readonly string[] charaPlugin = { "body.skinOverlays", "body.boneMods", "body.visible", "hair.back.materialOverrides", "hair.front.materialOverrides", "hair.side.materialOverrides", "hair.option.materialOverrides" };

        /// <summary>
        /// True when a change of <paramref name="path"/> changes plugin data the units apply:
        /// KSOX skin overlays (<paramref name="ksox"/>), ABMX, InvisibleBody, MaterialEditor items of hair, clothes and
        /// accessories, KCOX overlays, pushUp, DBE, AAC rules and overrides, and the accessory list itself (slot numbers).
        /// </summary>
        public static bool IsPluginSection(string path, out bool ksox)
        {
            ksox = false;
            string m = Member(path, out char doc);
            if (m == null) return false;
            bool any = false;
            if (doc != 'k')
                foreach (var p in charaPlugin)
                    if (Overlaps(p, m)) { any = true; if (p == "body.skinOverlays") ksox = true; }
            if (doc != 'c')
            {
                if (Overlaps("ruleOverrides", m)) any = true;
                foreach (var k in ClothesKinds)
                    foreach (var s in new[] { ".overlays", ".materialOverrides", ".pushUp" })
                        if (Overlaps("clothes." + k + s, m)) any = true;
                if (Overlaps("accessories", m))
                {
                    string rest = m.Length > "accessories".Length ? m.Substring("accessories".Length) : "";
                    if (rest.Length == 0 || !TryKey(rest, out _, out string member) || member.Length == 0) any = true;
                    else foreach (var s in new[] { "boneMods", "materialOverrides", "dynamicBones", "rules" })
                            if (IsPrefixOf(s, member)) any = true;
                }
            }
            return any;
        }

        /// <summary>Parses `["key"]` (JSON string literal) at the start of <paramref name="s"/>; member = the rest after "." .</summary>
        public static bool TryKey(string s, out string key, out string member)
        {
            key = null; member = null;
            if (s == null || s.Length < 4 || s[0] != '[' || s[1] != '"') return false;
            var sb = new System.Text.StringBuilder();
            int i = 2;
            for (; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length)
                {
                    char n = s[++i];
                    switch (n)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            if (i + 4 < s.Length && int.TryParse(s.Substring(i + 1, 4), System.Globalization.NumberStyles.HexNumber, null, out int cp)) { sb.Append((char)cp); i += 4; }
                            break;
                        default: sb.Append(n); break;
                    }
                    continue;
                }
                if (c == '"') break;
                sb.Append(c);
            }
            if (i + 1 >= s.Length || s[i] != '"' || s[i + 1] != ']') return false;
            key = sb.ToString();
            string rest = s.Substring(i + 2);
            member = rest.Length > 0 && rest[0] == '.' ? rest.Substring(1) : rest;
            return true;
        }

        // ---------------------------------------------------------------- per-creator rebuilds (skin creators; clothes creators)

        static readonly string[] faceColourInputs = SkinInputs(
            new[] { "face.skin", "body.skin.color", "face.mole", "body.skinOverlays" },
            new[] { "face.makeup.eyeshadow", "face.makeup.cheek", "face.makeup.lip" }, new[] { "item", "color" },
            "face.makeup.paint", new[] { "item", "color", "layout", "rotation" });
        static readonly string[] faceDetailInputs = SkinInputs(
            new[] { "face.skin", "body.skinOverlays" },
            new[] { "face.makeup.eyeshadow", "face.makeup.cheek", "face.makeup.lip" }, new[] { "item", "gloss" },
            "face.makeup.paint", new[] { "item", "gloss", "metallic", "layout", "rotation" });
        static readonly string[] bodyColourInputs = SkinInputs(
            new[] { "body.skin.item", "body.skin.color", "body.sunburn", "body.skinOverlays" }, new string[0], new string[0],
            "body.paint", new[] { "item", "color", "layout", "rotation", "layoutItem" });
        static readonly string[] bodyDetailInputs = SkinInputs(
            new[] { "body.skinOverlays" }, new string[0], new string[0],
            "body.paint", new[] { "item", "gloss", "metallic", "layout", "rotation", "layoutItem" });

        static string[] SkinInputs(string[] whole, string[] layers, string[] layerMembers, string paint, string[] paintMembers)
        {
            var l = new List<string>(whole);
            foreach (var a in layers) foreach (var m in layerMembers) l.Add(a + "." + m);
            for (int k = 0; k < 2; k++) foreach (var m in paintMembers) l.Add(paint + "[" + k + "]." + m);
            return l.ToArray();
        }

        /// <summary>
        /// Which creators of a skin unit (<see cref="SkinFace"/> or <see cref="SkinBody"/>) a set of changed member paths
        /// feeds (HS2's per-layer dirty kinds): the colour creator ("create_skin_face" /
        /// "_body") and the detail creator ("create_skin detail_face" / "_body"). <paramref name="all"/> (a build, a head swap,
        /// MarkUnit) selects both; a path overlapping no input (a face paint's layoutItem, stored only) selects neither.
        /// Paths are member paths inside their document (prefix overlap in both directions, so a whole-object set such as
        /// "face.makeup" selects every creator it covers).
        /// </summary>
        public static void SkinCreators(string unit, IEnumerable<string> paths, bool all, out bool colour, out bool detail)
        {
            colour = detail = false;
            // nothing recorded (a unit applied outside a flush) counts as "all"
            if (all || paths == null || !paths.Any()) { colour = detail = true; return; }
            bool face = unit == SkinFace;
            var ci = face ? faceColourInputs : bodyColourInputs;
            var di = face ? faceDetailInputs : bodyDetailInputs;
            foreach (var p in paths)
            {
                if (p == null) continue;
                if (!colour) foreach (var i in ci) if (Overlaps(i, p)) { colour = true; break; }
                if (!detail) foreach (var i in di) if (Overlaps(i, p)) { detail = true; break; }
                if (colour && detail) return;
            }
        }

        /// <summary>The members of a clothes colour set that feed the colour creator (create_clothes) and those that feed
        /// the detail creator (create_clothes detail) of every colour group of the kind.</summary>
        public static readonly string[] ClothesColourMembers = { "base", "patternColor", "pattern", "layout", "rotation" };
        public static readonly string[] ClothesDetailMembers = { "gloss", "metallic" };

        static readonly string[][] clothesColourInputs = ClothesInputs(ClothesColourMembers), clothesDetailInputs = ClothesInputs(ClothesDetailMembers);

        static string[][] ClothesInputs(string[] members)
        {
            var r = new string[ClothesKinds.Length][];
            for (int k = 0; k < r.Length; k++)
            {
                var l = new List<string>();
                for (int c = 0; c < 3; c++) foreach (var m in members) l.Add("clothes." + ClothesKinds[k] + ".colors[" + c + "]." + m);
                r[k] = l.ToArray();
            }
            return r;
        }

        /// <summary>
        /// Which creators of a clothes kind's colour pass (clothes.&lt;kind&gt;#colors) a set of changed member paths feeds
        /// (Phase 4.4): base, pattern colour, pattern, layout and rotation feed the colour creators of every group, gloss and
        /// metallic the detail creators. <paramref name="all"/> (a build, a part swap, a KCOX / MaterialEditor change of the
        /// kind through the part unit, MarkUnit) selects both; so does an empty set (a unit applied outside a flush). Paths are
        /// member paths inside the coordinate document; a whole-object path ("clothes.top", "clothes.top.colors[1]") selects
        /// every creator it covers.
        /// </summary>
        public static void ClothesCreators(int kind, IEnumerable<string> paths, bool all, out bool colour, out bool detail)
        {
            colour = detail = false;
            if (all || paths == null || !paths.Any() || kind < 0 || kind >= ClothesKinds.Length) { colour = detail = true; return; }
            var ci = clothesColourInputs[kind];
            var di = clothesDetailInputs[kind];
            foreach (var p in paths)
            {
                if (p == null) continue;
                if (!colour) foreach (var i in ci) if (Overlaps(i, p)) { colour = true; break; }
                if (!detail) foreach (var i in di) if (Overlaps(i, p)) { detail = true; break; }
                if (colour && detail) return;
            }
        }

        /// <summary>
        /// The cost weight of a skin.body flush (the C5 throttle of Y4): W = Σ over the
        /// body creators the changed paths feed (<see cref="SkinCreators"/>) of (1 + the skin layers registered on that
        /// creator: KSOX and uncensor overlay / underlay). The face creators are not counted (F8). 0 = no body composite.
        /// </summary>
        public static int BodyCompositeWeight(IEnumerable<string> paths, bool all, int colourLayers, int detailLayers)
        {
            SkinCreators(SkinBody, paths, all, out bool colour, out bool detail);
            return (colour ? 1 + Math.Max(0, colourLayers) : 0) + (detail ? 1 + Math.Max(0, detailLayers) : 0);
        }

        // ---------------------------------------------------------------- the material arrays

        /// <summary>Which object a material array belongs to.</summary>
        public enum MaterialArray { None = 0, Character, Hair, Clothes, Accessory }

        /// <summary>
        /// True when <paramref name="path"/> is at or under one of the material arrays: `materialOverrides`,
        /// `hair.&lt;part&gt;.materialOverrides`, `clothes.&lt;kind&gt;.materialOverrides`, `accessories["id"].materialOverrides`.
        /// <paramref name="index"/>: the hair part or clothes kind; <paramref name="id"/>: the accessory; <paramref name="arrayPath"/>:
        /// the array's member path. A wider path (`hair.back`, a whole document) is not one.
        /// </summary>
        public static bool MaterialObject(string path, out MaterialArray kind, out int index, out string id, out string arrayPath)
        {
            kind = MaterialArray.None; index = -1; id = null; arrayPath = null;
            string m = Member(path, out char doc);
            if (string.IsNullOrEmpty(m)) return false;
            if (doc != 'k')
            {
                if (IsPrefixOf("materialOverrides", m)) { kind = MaterialArray.Character; arrayPath = "materialOverrides"; return true; }
                for (int k = 0; k < HairParts.Length; k++)
                {
                    string p = "hair." + HairParts[k] + ".materialOverrides";
                    if (IsPrefixOf(p, m)) { kind = MaterialArray.Hair; index = k; arrayPath = p; return true; }
                }
            }
            if (doc == 'c') return false;
            for (int k = 0; k < ClothesKinds.Length; k++)
            {
                string p = "clothes." + ClothesKinds[k] + ".materialOverrides";
                if (IsPrefixOf(p, m)) { kind = MaterialArray.Clothes; index = k; arrayPath = p; return true; }
            }
            if (m.Length > "accessories".Length && IsPrefixOf("accessories", m) && TryKey(m.Substring("accessories".Length), out string key, out string member) &&
                IsPrefixOf("materialOverrides", member))
            {
                kind = MaterialArray.Accessory; id = key; arrayPath = AccessoryArray(key);
                return true;
            }
            return false;
        }

        /// <summary>The member path of an accessory's material array, by its id.</summary>
        public static string AccessoryArray(string id) => "accessories[\"" + id + "\"].materialOverrides";

        /// <summary>FieldPath prefix rule: "a.b" is a prefix of "a.b", "a.b.c" and "a.b[0]", not of "a.bc".</summary>
        public static bool IsPrefixOf(string prefix, string path)
        {
            if (string.IsNullOrEmpty(prefix)) return true;
            if (path == null || path.Length < prefix.Length || !path.StartsWith(prefix, StringComparison.Ordinal)) return false;
            if (path.Length == prefix.Length) return true;
            char c = path[prefix.Length];
            return c == '.' || c == '[';
        }

        public static bool Overlaps(string a, string b) => IsPrefixOf(a, b) || IsPrefixOf(b, a);
    }
}
