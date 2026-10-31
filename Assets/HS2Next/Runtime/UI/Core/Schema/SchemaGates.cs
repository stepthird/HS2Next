// Gate names of the inspector schema: feature predicates the inspector (package I)
// evaluates per node from the realized state (ItemFeatures, CharaRealization, the light type, the map's blocks, the
// animation clip). A row with a gate is shown / enabled only when the predicate holds for the primary (for "All" rows the
// edit is still written only to nodes where it holds, as for items whose colour k is in use).
using System.Collections.Generic;

namespace HS2Next.UI.Core
{
    public static class SchemaGates
    {
        // character
        public const string CharaFemale = "chara.female", CharaMale = "chara.male";
        /// <summary>"chara.clothes[k]": clothes kind k (0..7) has states (CharaRealization.ClothesStates).</summary>
        public static string CharaClothes(int k) => "chara.clothes[" + k + "]";
        public const string CharaAnimeParams = "chara.anime.params", CharaNeckEditable = "chara.neck.editable",
            CharaLookTarget = "chara.look.target", CharaIkImport = "chara.ik.import", CharaFkOn = "chara.fk.on", CharaIkOn = "chara.ik.on";
        // item
        public static string ItemColor(int k) => "item.color[" + k + "]";
        public static string ItemMetallic(int k) => "item.metallic[" + k + "]";
        public static string ItemPattern(int k) => "item.pattern[" + k + "]";
        public const string ItemGlass = "item.glass", ItemAlpha = "item.alpha", ItemEmission = "item.emission",
            ItemEmissionColor = "item.emissionColor", ItemEmissionPower = "item.emissionPower", ItemLightCancel = "item.lightCancel",
            ItemPanel = "item.panel", ItemFk = "item.fk", ItemDynamicBones = "item.dynamicBones", ItemFkOrDynamicBones = "item.fkOrDynamicBones",
            ItemOptions = "item.options", ItemAnimePatterns = "item.animePatterns", ItemAnimated = "item.animated";
        // light
        public const string LightPoint = "light.point", LightSpot = "light.spot", LightDirectional = "light.directional",
            LightHasRange = "light.range";
        // scene
        public const string MapLoaded = "map.loaded", MapOptions = "map.options", MapLights = "map.lights";
        // route (Phase 3.6; evaluated by package R from the route and its sub-selection; the point rows
        // need a sub-selected point or aid: RoutePoint holds then, the narrower gates imply it)
        public const string RouteCanPlay = "route.canPlay", RouteCanAddPoint = "route.canAddPoint", RoutePoint = "route.point",
            RoutePointStart = "route.point.start", RoutePointOther = "route.point.other", RoutePointCurve = "route.point.curve",
            RoutePointLinkable = "route.point.linkable", RoutePointUnlinked = "route.point.unlinked";

        // Maker (evaluated by StudioOps.MakerGateHolds over MakerServices.Rows / MakerLists)
        /// <summary>"maker.headList": the head list «タイプ» is offered — females always, males when the male head list has more
        /// than one listed row (F3; HS2 hides it for males because vanilla has one male head).</summary>
        public const string MakerHeadList = "maker.headList";
        /// <summary>"maker.bodyHeight" (Y8): the body height row «身長» is offered —
        /// females always, males while CharaAssembler.MaleHeightPin075 is off (the default: the installed
        /// HS2_UnlockPlayerHeight's Maker shows it; with the pin on the engine pins 0.75 and the row would do nothing).</summary>
        public const string MakerBodyHeight = "maker.bodyHeight";
        public const string MakerListPrefix = "maker.list:";
        /// <summary>"maker.list:&lt;category&gt;": the Maker offers that list to the character's sex (MakerLists.HasList).</summary>
        public static string MakerList(int category) => MakerListPrefix + category.ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>The category of a "maker.list:&lt;c&gt;" gate, or -1.</summary>
        public static int MakerListCategory(string gate) =>
            gate != null && gate.StartsWith(MakerListPrefix, System.StringComparison.Ordinal) &&
            int.TryParse(gate.Substring(MakerListPrefix.Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int c) ? c : -1;

        // ---- hair (J13): "maker.hair:<part>:<feature>", evaluated on the primary by
        //      StudioOps.MakerGateHolds through MakerEdits.HairGateHolds — the prefab facts of the realized part
        //      (MakerServices.Hair; plain Mono without the service: every prefab feature holds), «根本と毛先の色を自動設定» and
        //      the mesh item from the primary's document.
        public const string MakerHairPrefix = "maker.hair:";
        /// <summary>The part has a prefab with a CmpHair (every row but the style list).</summary>
        public const string HairPrefab = "prefab";
        /// <summary>«根本カラー»: `useTopColor` and auto off.</summary>
        public const string HairTopRow = "topRow";
        /// <summary>«毛先カラー»: `useUnderColor` and auto off.</summary>
        public const string HairUnderRow = "underRow";
        /// <summary>«ツヤカラー»: auto off.</summary>
        public const string HairGlossRow = "glossRow";
        /// <summary>«肌の色に合わせる»: `useSameSkinColorButton`.</summary>
        public const string HairSkin = "skin";
        /// <summary>The decoration section: any of acs1..acs3.</summary>
        public const string HairAcs = "acs";
        /// <summary>«カラー0N» (N = 1..3): `useAcsColor0N` and N ≤ the prefab's `acsDefColor` count (GH5).</summary>
        public static string HairAcsN(int n) => "acs" + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        /// <summary>The bundle section: at least one bundle with a correction transform.</summary>
        public const string HairBundles = "bundles";
        /// <summary>The mesh section: `useMesh`.</summary>
        public const string HairMesh = "mesh";
        /// <summary>The mesh colour and layout rows: `useMesh` and `hair.&lt;part&gt;.mesh.item` is not the none row.</summary>
        public const string HairMeshSet = "meshSet";

        /// <summary>The features of a hair gate, in a fixed order (tests).</summary>
        public static readonly IReadOnlyList<string> HairFeatures = new[]
        {
            HairPrefab, HairTopRow, HairUnderRow, HairGlossRow, HairSkin, HairAcs, "acs1", "acs2", "acs3", HairBundles, HairMesh, HairMeshSet,
        };

        /// <summary>The hair part names of the gates (the documents' `hair.&lt;part&gt;` names, HairKind order).</summary>
        public static readonly IReadOnlyList<string> HairPartNames = new[] { "back", "front", "side", "option" };

        /// <summary>"maker.hair:&lt;part&gt;:&lt;feature&gt;" for part 0..3 (back, front, side, option).</summary>
        public static string MakerHair(int part, string feature) => MakerHairPrefix + HairPartNames[part] + ":" + feature;

        /// <summary>Splits a hair gate into the part (0..3) and the feature; false for other gates or an unknown part / feature.</summary>
        public static bool TryMakerHair(string gate, out int part, out string feature)
        {
            part = -1;
            feature = null;
            if (gate == null || !gate.StartsWith(MakerHairPrefix, System.StringComparison.Ordinal)) return false;
            int colon = gate.IndexOf(':', MakerHairPrefix.Length);
            if (colon < 0) return false;
            string p = gate.Substring(MakerHairPrefix.Length, colon - MakerHairPrefix.Length), f = gate.Substring(colon + 1);
            for (int k = 0; k < HairPartNames.Count; k++) if (HairPartNames[k] == p) part = k;
            if (part < 0 || !((IList<string>)HairFeatures).Contains(f)) { part = -1; return false; }
            feature = f;
            return true;
        }

        // ---- clothes (Phase 4.4): "maker.clothes:<kind>:<feature>", evaluated on the primary by StudioOps.MakerGateHolds
        //      through the scene layer's MakerGate / GateContext (the scene layer keeps its own copy of these names; the schema
        //      test binds the two parsers): the prefab facts of the realized kind (MakerServices.Clothes; plain Mono without the
        //      service: every prefab feature holds), the pattern of the character's document, the kinds HS2's Maker offers the
        //      sex (males: top, bottom, gloves, shoes) and the one-piece rule of the bottom and inner-bottom kinds.
        public const string MakerClothesPrefix = "maker.clothes:";
        /// <summary>The kind has a prefab with a CmpClothes («初期カラー»).</summary>
        public const string ClothesPrefab = "prefab";
        /// <summary>«カラー①»–«カラー③» (n = 1..3): `useColorN0n` or `useColorA0n` — the colour set's section and its first rows.</summary>
        public static string ClothesSet(int n) => "set" + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        /// <summary>The main colour of set n has alpha: `useColorA0n` (the colour row's <see cref="FieldSpec.ColorAlphaGate"/>).</summary>
        public static string ClothesAlpha(int n) => "alpha" + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        /// <summary>The pattern colour and layout rows of set n: the set is shown and its stored pattern is not the none row.</summary>
        public static string ClothesPattern(int n) => "pattern" + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        /// <summary>«破れ具合»: `useBreak`.</summary>
        public const string ClothesBreak = "break";
        /// <summary>«オプションパーツ0n を表示» (n = 1, 2): `objOpt0n` holds at least one object.</summary>
        public static string ClothesOption(int n) => "opt" + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        /// <summary>«その他»: the break or an option part.</summary>
        public const string ClothesOther = "other";
        /// <summary>The kind selector's button is enabled: HS2's Maker offers the kind to the sex and no one-piece top (inner top)
        /// disables the bottom (inner bottom). Male kinds without a Maker list (inner top, inner bottom, pantyhose, socks) never
        /// hold — every other clothes feature of those kinds does not hold either.</summary>
        public const string ClothesKind = "kind";

        /// <summary>The features of a clothes gate, in a fixed order (tests; the scene layer's copy is in the same order).</summary>
        public static readonly IReadOnlyList<string> ClothesFeatures = new[]
        {
            ClothesPrefab, "set1", "set2", "set3", "alpha1", "alpha2", "alpha3", "pattern1", "pattern2", "pattern3", ClothesBreak, "opt1", "opt2", ClothesOther, ClothesKind,
        };

        /// <summary>The clothes kind names of the gates (the documents' `clothes.&lt;kind&gt;` names, kinds 0..7).</summary>
        public static readonly IReadOnlyList<string> ClothesKindNames = new[] { "top", "bottom", "innerTop", "innerBottom", "gloves", "pantyhose", "socks", "shoes" };

        /// <summary>"maker.clothes:&lt;kind&gt;:&lt;feature&gt;" for kind 0..7 (top … shoes).</summary>
        public static string MakerClothes(int kind, string feature) => MakerClothesPrefix + ClothesKindNames[kind] + ":" + feature;

        /// <summary>Splits a clothes gate into the kind (0..7) and the feature; false for other gates or an unknown kind / feature.</summary>
        public static bool TryMakerClothes(string gate, out int kind, out string feature)
        {
            kind = -1;
            feature = null;
            if (gate == null || !gate.StartsWith(MakerClothesPrefix, System.StringComparison.Ordinal)) return false;
            int colon = gate.IndexOf(':', MakerClothesPrefix.Length);
            if (colon < 0) return false;
            string k = gate.Substring(MakerClothesPrefix.Length, colon - MakerClothesPrefix.Length), f = gate.Substring(colon + 1);
            for (int i = 0; i < ClothesKindNames.Count; i++) if (ClothesKindNames[i] == k) kind = i;
            if (kind < 0 || !((IList<string>)ClothesFeatures).Contains(f)) { kind = -1; return false; }
            feature = f;
            return true;
        }

        // ---- accessories (Phase 4.5): "maker.acc:<slot>:<feature>" with the 0-based build slot as decimal digits, evaluated on the
        //      primary by StudioOps.MakerGateHolds through the scene layer's MakerGate / GateContext (the scene layer keeps its own
        //      copy of these names; the schema test binds the two parsers): the prefab facts of the accessory in that slot (the
        //      realization's, MakerServices.Accessories, else the row's prefab facts; plain Mono without either: every prefab
        //      feature holds except the hair-type ones) and the row's `Parent` column. A slot without an accessory holds nothing.
        public const string MakerAccPrefix = "maker.acc:";
        /// <summary>The slot's prefab has a CmpAccessory.</summary>
        public const string AccPrefab = "prefab";
        /// <summary>The normal colour section: not hair-type and a colour area or alpha renderers.</summary>
        public const string AccColour = "colour";
        /// <summary>Colour area n (1..3) of the normal colour section: not hair-type and `useColor0n`.</summary>
        public static string AccArea(int n) => "area" + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        /// <summary>The fourth colour (alpha renderers): not hair-type and `rendAlpha` not empty.</summary>
        public const string AccAlpha = "alpha";
        /// <summary>The hair colour section: `typeHair`.</summary>
        public const string AccHair = "hair";
        /// <summary>«根本カラー» / «毛先カラー» of the hair colour section: hair-type and `useColor02` / `useColor03`.</summary>
        public const string AccTop = "top", AccUnder = "under";
        /// <summary>The parent section: the row's `Parent` column is not "0" (a fixed row cannot be re-parented).</summary>
        public const string AccParent = "parent";
        /// <summary>The transform set c (1, 2) «調整0c»: the prefab has `trfMove0c`.</summary>
        public static string AccMove(int c) => "move" + c.ToString(System.Globalization.CultureInfo.InvariantCulture);
        /// <summary>The head-weighted note of the parent section: the row's `Parent` column is "null" (the parent is stored only).</summary>
        public const string AccHead = "head";

        /// <summary>The features of an accessory gate, in a fixed order (tests; the scene layer's copy is in the same order).</summary>
        public static readonly IReadOnlyList<string> AccFeatures = new[]
        {
            AccPrefab, AccColour, "area1", "area2", "area3", AccAlpha, AccHair, AccTop, AccUnder, AccParent, "move1", "move2", AccHead,
        };

        /// <summary>"maker.acc:&lt;slot&gt;:&lt;feature&gt;" for build slot <paramref name="slot"/> (0-based).</summary>
        public static string MakerAcc(int slot, string feature) => MakerAccPrefix + slot.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + feature;

        /// <summary>Splits an accessory gate into the build slot (1–4 decimal digits) and the feature; false for other gates, a
        /// malformed slot or an unknown feature.</summary>
        public static bool TryMakerAcc(string gate, out int slot, out string feature)
        {
            slot = -1;
            feature = null;
            if (gate == null || !gate.StartsWith(MakerAccPrefix, System.StringComparison.Ordinal)) return false;
            int colon = gate.IndexOf(':', MakerAccPrefix.Length);
            if (colon < 0) return false;
            string s = gate.Substring(MakerAccPrefix.Length, colon - MakerAccPrefix.Length), f = gate.Substring(colon + 1);
            if (s.Length == 0 || s.Length > 4 || !int.TryParse(s, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int n) ||
                !((IList<string>)AccFeatures).Contains(f)) return false;
            slot = n;
            feature = f;
            return true;
        }

        /// <summary>A gate name this table defines (<see cref="All"/>, a "maker.list:&lt;c&gt;" gate, a "maker.hair:&lt;p&gt;:&lt;f&gt;",
        /// a "maker.clothes:&lt;k&gt;:&lt;f&gt;" or a "maker.acc:&lt;slot&gt;:&lt;f&gt;" gate).</summary>
        public static bool IsKnown(string gate) => gate != null && (((HashSet<string>)All).Contains(gate) || MakerListCategory(gate) >= 0 || TryMakerHair(gate, out _, out _) ||
            TryMakerClothes(gate, out _, out _) || TryMakerAcc(gate, out _, out _));

        /// <summary>Every gate name (templates expanded; "maker.list:&lt;c&gt;" gates are open-ended and the hair and clothes gates
        /// are parsed: <see cref="IsKnown"/>).</summary>
        public static readonly IReadOnlyCollection<string> All = Build();

        static HashSet<string> Build()
        {
            var s = new HashSet<string>
            {
                CharaFemale, CharaMale, CharaAnimeParams, CharaNeckEditable, CharaLookTarget, CharaIkImport, CharaFkOn, CharaIkOn,
                ItemGlass, ItemAlpha, ItemEmission, ItemEmissionColor, ItemEmissionPower, ItemLightCancel, ItemPanel, ItemFk,
                ItemDynamicBones, ItemFkOrDynamicBones, ItemOptions, ItemAnimePatterns, ItemAnimated,
                LightPoint, LightSpot, LightDirectional, LightHasRange, MapLoaded, MapOptions, MapLights,
                RouteCanPlay, RouteCanAddPoint, RoutePoint, RoutePointStart, RoutePointOther, RoutePointCurve, RoutePointLinkable, RoutePointUnlinked,
                MakerHeadList, MakerBodyHeight,
            };
            for (int k = 0; k < 8; k++) s.Add(CharaClothes(k));
            for (int k = 0; k < 3; k++) { s.Add(ItemColor(k)); s.Add(ItemMetallic(k)); s.Add(ItemPattern(k)); }
            return s;
        }
    }
}
