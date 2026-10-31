// The services, facts and option / report types of the Maker edits (the hair facts of
// 3): the interfaces the Unity layer implements (IPartDefaults, IHairInfo, IClothesInfo,
// IAppearanceRows, IFacePresets, IEditSessionHost), the static registry MakerServices, MakerEditOptions, MakerEditReport and
// AppearanceSet. Split out of MakerEdits.cs (Step 0 of Phase 4.4); 4.4 adds the clothes facts (ClothesKindFacts, the
// realization's IClothesInfo, the prefab's IPartDefaults.TryClothesFacts); 4.5 the accessory facts (AccessoryFacts, the
// realization's IAccessoryInfo by build slot, the prefab's IPartDefaults.TryAccessoryFacts) and the accessory switches; 4.6
// the head names of the partial character load (IHeadNames, HeadNames with the vanilla names and the face / body rule).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using HS2Next.Content;
using HS2Next.Save;

namespace HS2Next.Scenes
{
    /// <summary>Prefab default values of a part row (implemented by the Unity layer's
    /// PartDefaults, which reads CmpClothes / CmpAccessory / CmpHair of the loaded prefab without instantiating it).</summary>
    public interface IPartDefaults
    {
        /// <summary>The three colour sets a clothes item starts with (`defMainColor0n`, `defPatternColor0n`, `defPtnIndex0n` as
        /// a pattern ref, `defGloss0n`, `defMetallic0n`, `defLayout0n`, `defRotation0n`) as document values; false when the row
        /// or its prefab is unknown.</summary>
        bool TryClothesColors(int category, string itemRef, out ClothesColor[] colors);
        /// <summary>The four colour sets an accessory item starts with (slots 0–2 and 3 = the alpha renderers).</summary>
        bool TryAccessoryColors(int category, string itemRef, out AccessoryColor[] colors);
        /// <summary>The four decoration colours of a hair item (`acsDefColor`).</summary>
        bool TryHairAccessoryColors(int category, string itemRef, out Color4[] colors);
        /// <summary>
        /// The bundles of a hair item's prefab (`CmpHair.boneInfo` in order, one entry per
        /// boneInfo with or without a correction transform): limits, base pose and the default rates. The prefab is the row's
        /// MainAB / MainData, MainData02 for males when set (<paramref name="sex"/> 0 male, 1 female; as the build). False when
        /// the row, its prefab or its CmpHair is unknown (the none rows' p_dummy).
        /// </summary>
        bool TryHairBundles(int category, string itemRef, int sex, out HairBundleFacts[] facts);
        /// <summary>
        /// The facts of a clothes row's prefab without a realization (the gates of a row that is not worn, the bridge, plain
        /// Mono fixtures): its CmpClothes flags, option parts, groups (renderers and material slots; no texture sizes and no
        /// creators), the defaults of the three colour sets as document values (as <see cref="TryClothesColors"/>) and the row's
        /// `Coordinate`, `NotBra` and `StateType` columns. The prefab is the row's MainAB / MainData, MainData02 for the male
        /// lists (1xx) when set, as the build loads it (<paramref name="sex"/> 0 male, 1 female). False when the row, its prefab
        /// or its CmpClothes is unknown (the none rows' p_dummy).
        /// </summary>
        bool TryClothesFacts(int category, string itemRef, int sex, out ClothesKindFacts facts);
        /// <summary>
        /// The facts of an accessory row's prefab without a realization (the gates of a slot that is not loaded, «初期カラー», the
        /// colour rule of an item change, the bridge): its CmpAccessory (`typeHair`, `useColor01..03`, the alpha renderers,
        /// `trfMove01` / `trfMove02`), the dynamic bones and colliders inside it, the row's `Parent` column and the prefab
        /// defaults of the four colours (as <see cref="TryAccessoryColors"/>). The prefab is the row's MainAB / MainData as the
        /// build loads it (Sideloader's served copy). False when the row or its prefab is unknown; a prefab without a
        /// CmpAccessory gives true with <see cref="AccessoryFacts.HasCmpAccessory"/> false.
        /// </summary>
        bool TryAccessoryFacts(int category, string itemRef, out AccessoryFacts facts);
    }

    /// <summary>
    /// The facts of one accessory row's prefab (the Accessories page's gates, the bridge, the colour rules of a type or item
    /// change, «初期カラー», «髪の毛の色を取得»): whether the prefab has a CmpAccessory, its `typeHair`, which of the three colour
    /// areas it uses (`useColor01..03`), its alpha renderers (`rendAlpha`, the fourth colour), its two transform sets
    /// (`trfMove01` / `trfMove02`), the dynamic-bone chains and colliders inside it, the row's `Parent` column ("0" fixed,
    /// "null" head-weighted, else a key or another text) and the prefab defaults of the four colours as document values.
    /// A prefab without a CmpAccessory, or no realization, has <see cref="HasCmpAccessory"/> false and every flag off.
    /// </summary>
    public sealed class AccessoryFacts
    {
        public bool HasCmpAccessory;
        /// <summary>The category (351..363) and the row reference ("guid#id") the facts are of; -1 / null when unknown.</summary>
        public int Category = -1;
        public string Item;
        /// <summary>The prefab asset name (the row's MainData).</summary>
        public string PrefabName;
        /// <summary>`typeHair`: the item is drawn with the hair shader and the hair colours.</summary>
        public bool TypeHair;
        /// <summary>`useColor01` … `useColor03` (index 0..2).</summary>
        public bool[] UseColor = new bool[3];
        /// <summary>Renderers in `rendAlpha` (the fourth colour; its alpha switches them).</summary>
        public int AlphaRenderers;
        /// <summary>`trfMove01` / `trfMove02` exist (the transform sets «調整01» / «調整02»).</summary>
        public bool Move1, Move2;
        /// <summary>The row's `Parent` column as written (null when unknown).</summary>
        public string ParentColumn;
        /// <summary>Dynamic-bone chains and colliders inside the prefab.</summary>
        public int DynamicBones, Colliders;
        /// <summary>The four colours the prefab starts with (areas 1..3 and the alpha colour) as document values; null when unknown.</summary>
        public AccessoryColor[] DefaultColors;

        /// <summary>The row's `Parent` column is "null": the mesh follows the head bones and the stored key is not used.</summary>
        public bool HeadWeighted => ParentColumn == "null";
        /// <summary>The row's `Parent` column is "0": the accessory cannot be re-parented.</summary>
        public bool Fixed => ParentColumn == "0";

        /// <summary>Colour area n (1..3) is shown on the normal colour section: not hair-type and `useColor0n`.</summary>
        public bool AreaShown(int n) => HasCmpAccessory && !TypeHair && n >= 1 && n <= 3 && UseColor != null && n <= UseColor.Length && UseColor[n - 1];
        /// <summary>The fourth colour (alpha renderers) is shown: not hair-type and at least one alpha renderer.</summary>
        public bool AlphaShown => HasCmpAccessory && !TypeHair && AlphaRenderers > 0;
        /// <summary>The normal colour section is shown: an area or the alpha colour.</summary>
        public bool ColourShown => AreaShown(1) || AreaShown(2) || AreaShown(3) || AlphaShown;
        /// <summary>The hair colour section is shown: `typeHair`.</summary>
        public bool HairShown => HasCmpAccessory && TypeHair;
        /// <summary>Hair colour n (1..3: base, top, under) is used: hair-type and `useColor0n` (the specular is always used).</summary>
        public bool HairAreaShown(int n) => HairShown && n >= 1 && n <= 3 && UseColor != null && n <= UseColor.Length && UseColor[n - 1];

        /// <summary>"no prefab": the facts of a slot without a realization or a prefab without a CmpAccessory.</summary>
        public static AccessoryFacts None() => new AccessoryFacts();

        /// <summary>A copy (arrays copied too): the facts are handed out from caches.</summary>
        public AccessoryFacts Clone()
        {
            var c = (AccessoryFacts)MemberwiseClone();
            c.UseColor = UseColor == null ? new bool[3] : (bool[])UseColor.Clone();
            if (DefaultColors != null)
            {
                c.DefaultColors = new AccessoryColor[DefaultColors.Length];
                for (int i = 0; i < DefaultColors.Length; i++) c.DefaultColors[i] = DefaultColors[i] == null ? null : DocClone.Clone(DefaultColors[i]);
            }
            return c;
        }

        public override string ToString() =>
            (HasCmpAccessory ? "" : "no CmpAccessory ") + (TypeHair ? "hair-type " : "") + "areas " + (UseColor != null && UseColor.Length > 2 ? (UseColor[0] ? "1" : "-") + (UseColor[1] ? "2" : "-") + (UseColor[2] ? "3" : "-") : "?") +
            " alpha " + AlphaRenderers + " sets " + (Move1 ? "1" : "-") + (Move2 ? "2" : "-") + " parent " + (ParentColumn ?? "?");
    }

    /// <summary>
    /// The prefab facts of a node's realized accessories by build slot (implemented by the Unity layer's AccessoryInfo over the
    /// realization; registered as MakerServices.Accessories). False when the node or slot has no realization (a card-backed
    /// node, a failed load, a MoreAccessories slot while MoreAccessories is off, not built yet).
    /// </summary>
    public interface IAccessoryInfo
    {
        /// <summary>The facts of build slot <paramref name="slot"/> (0-based) of node <paramref name="nodeId"/>.</summary>
        bool TrySlot(string nodeId, int slot, out AccessoryFacts facts);
        /// <summary>The build slot and the facts of accessory <paramref name="accessoryId"/> of node <paramref name="nodeId"/>.</summary>
        bool TryAccessory(string nodeId, string accessoryId, out int slot, out AccessoryFacts facts);
    }

    /// <summary>
    /// One colour group of a clothes prefab (group j = 1..3: the renderers of `rendNormal0j`, composited from `MainTex`,
    /// `MainTex02`, `MainTex03`): renderer and material slot counts, the main texture's size and whether the realization made
    /// the group's two creators (colour and detail). Sizes and creators are known from a realization only (0 / false from
    /// <see cref="IPartDefaults.TryClothesFacts"/>).
    /// </summary>
    public sealed class ClothesGroupFacts
    {
        /// <summary>1..3.</summary>
        public int Group;
        public int Renderers, Slots;
        public int MainTexWidth, MainTexHeight;
        public bool HasCreators;
    }

    /// <summary>
    /// The prefab facts of one clothes kind (the Clothes page's gates, the bridge, «初期カラー»): whether the kind has a part with
    /// a CmpClothes, the row and prefab, the colour set flags `useColorN01..03` / `useColorA01..03`, `useBreak`, the option part
    /// object counts of `objOpt01` / `objOpt02`, the colour groups, the def / half objects, the row's `Coordinate` (not "0" on a
    /// one-piece top or inner top), `NotBra` and `StateType`, whether the kind was skipped under a one-piece, and the prefab
    /// defaults of the three colour sets as document values. A kind without a realization or without a CmpClothes has
    /// <see cref="HasPrefab"/> false and every flag off.
    /// </summary>
    public sealed class ClothesKindFacts
    {
        public bool HasPrefab;
        /// <summary>The row reference the part was built from ("guid#id"), null when none.</summary>
        public string Item;
        /// <summary>The prefab asset name (the row's MainData, MainData02 for a male variant).</summary>
        public string PrefabName;
        public bool UseColorN01, UseColorN02, UseColorN03, UseColorA01, UseColorA02, UseColorA03;
        public bool UseBreak;
        /// <summary>Objects in `objOpt01` / `objOpt02` (null entries not counted).</summary>
        public int Option01Count, Option02Count;
        /// <summary>The row's `Coordinate` column as written ("0" two-piece, "1" one-piece; null when unknown).</summary>
        public string Coordinate;
        /// <summary>The row's `NotBra` column is "1".</summary>
        public bool NotBra;
        /// <summary>The row's `StateType` column (0 on / half / off, 1 on / off, 2 on only; -1 when unknown).</summary>
        public int StateType = -1;
        /// <summary>The kind (bottom, inner bottom) was not realized because the top (inner top) is a one-piece.</summary>
        public bool Skipped;
        public bool HasTopDef, HasTopHalf, HasBotDef, HasBotHalf;
        /// <summary>The colour groups (1..3) whose `rendNormal0j` is not empty.</summary>
        public ClothesGroupFacts[] Groups = Array.Empty<ClothesGroupFacts>();
        /// <summary>The three colour sets the prefab starts with, as document values (as IPartDefaults.TryClothesColors); null
        /// when unknown.</summary>
        public ClothesColor[] Defaults;

        /// <summary>Colour set n (1..3) is shown: `useColorN0n` or `useColorA0n`.</summary>
        public bool SetShown(int n) => HasPrefab && (n == 1 ? UseColorN01 || UseColorA01 : n == 2 ? UseColorN02 || UseColorA02 : n == 3 && (UseColorN03 || UseColorA03));

        /// <summary>The main colour of set n (1..3) has alpha: `useColorA0n`.</summary>
        public bool AlphaShown(int n) => HasPrefab && (n == 1 ? UseColorA01 : n == 2 ? UseColorA02 : n == 3 && UseColorA03);

        /// <summary>Option part n (1, 2) exists.</summary>
        public bool OptionShown(int n) => HasPrefab && (n == 1 ? Option01Count > 0 : n == 2 && Option02Count > 0);

        /// <summary>The row is a one-piece (`Coordinate` not "0"): on the top it disables the bottom kind, on the inner top the
        /// inner-bottom kind. An unknown or empty column counts as two-piece.</summary>
        public bool OnePiece => !string.IsNullOrEmpty(Coordinate) && Coordinate != "0";

        /// <summary>"no prefab": the facts of a kind without a realization or without a CmpClothes.</summary>
        public static ClothesKindFacts None() => new ClothesKindFacts();

        /// <summary>A copy (groups and defaults copied too): the facts are handed out from caches.</summary>
        public ClothesKindFacts Clone()
        {
            var c = (ClothesKindFacts)MemberwiseClone();
            c.Groups = new ClothesGroupFacts[Groups?.Length ?? 0];
            for (int i = 0; i < c.Groups.Length; i++)
            {
                var g = Groups[i];
                c.Groups[i] = g == null ? null : new ClothesGroupFacts { Group = g.Group, Renderers = g.Renderers, Slots = g.Slots, MainTexWidth = g.MainTexWidth, MainTexHeight = g.MainTexHeight, HasCreators = g.HasCreators };
            }
            if (Defaults != null)
            {
                c.Defaults = new ClothesColor[Defaults.Length];
                for (int i = 0; i < Defaults.Length; i++) c.Defaults[i] = Defaults[i] == null ? null : DocClone.Clone(Defaults[i]);
            }
            return c;
        }
    }

    /// <summary>
    /// The prefab facts of a node's realized clothes kinds (implemented by the Unity layer's ClothesInfo over the realization;
    /// registered as MakerServices.Clothes). False when the node has no realization (a card-backed node, a failed load, not
    /// built yet): the gates treat that as "no prefab".
    /// </summary>
    public interface IClothesInfo
    {
        bool TryKind(string nodeId, int kind, out ClothesKindFacts facts);
    }

    /// <summary>
    /// One bundle of a hair prefab (`CmpHair.boneInfo[Index]`): the limits
    /// of the rates (`posMin` … `rotMax`, local position / Euler degrees of the correction transform), its base pose and the
    /// default rates move = InverseLerp(posMin, posMax, basePos), rotate = InverseLerp(rotMin, rotMax, baseRot) per axis (Unity's
    /// clamped Mathf.InverseLerp; equal limits → 0). Shown = the correction transform exists (HS2 lists only those, titled
    /// «調整NN» with Title = 1.. among the shown; 0 when not shown). Immutable.
    /// </summary>
    public sealed class HairBundleFacts
    {
        public readonly int Index, Title;
        public readonly bool Shown;
        public readonly Vec3 PosMin, PosMax, RotMin, RotMax, BasePos, BaseRot, DefaultMove, DefaultRotate;

        public HairBundleFacts(int index, bool shown, int title, Vec3 posMin, Vec3 posMax, Vec3 rotMin, Vec3 rotMax, Vec3 basePos, Vec3 baseRot)
        {
            Index = index;
            Shown = shown;
            Title = shown ? title : 0;
            PosMin = posMin;
            PosMax = posMax;
            RotMin = rotMin;
            RotMax = rotMax;
            BasePos = basePos;
            BaseRot = baseRot;
            DefaultMove = MakerEdits.InverseLerp(posMin, posMax, basePos);
            DefaultRotate = MakerEdits.InverseLerp(rotMin, rotMax, baseRot);
        }

        /// <summary>The facts of a boneInfo list in order: Title numbers the shown bundles 1.. (HS2's «調整NN»).</summary>
        public static HairBundleFacts[] Number(IReadOnlyList<(bool shown, Vec3 posMin, Vec3 posMax, Vec3 rotMin, Vec3 rotMax, Vec3 basePos, Vec3 baseRot)> boneInfo)
        {
            var r = new HairBundleFacts[boneInfo?.Count ?? 0];
            int title = 0;
            for (int i = 0; i < r.Length; i++)
            {
                var b = boneInfo[i];
                if (b.shown) title++;
                r[i] = new HairBundleFacts(i, b.shown, b.shown ? title : 0, b.posMin, b.posMax, b.rotMin, b.rotMax, b.basePos, b.baseRot);
            }
            return r;
        }

        /// <summary>The bundle as a document entry with the default rates (`noShake` false).</summary>
        public HairBundle DefaultBundle() => new HairBundle { Index = Index, Move = DefaultMove, Rotate = DefaultRotate, NoShake = false };

        public override string ToString() => "bundle " + Index + (Shown ? " «" + Title + "»" : " (no correction transform)") + " default " + FieldJson.ToText(DefaultMove) + " / " + FieldJson.ToText(DefaultRotate);
    }

    /// <summary>
    /// The prefab facts of one realized hair part (IHairInfo, read by the gates of
    /// the bundle list and the bridge): whether the part has a CmpHair, the seven flags, the
    /// `acsDefColor` count, the bundles and the prefab name. A part without a realization or without a CmpHair has
    /// <see cref="HasPrefab"/> false and every flag off.
    /// </summary>
    public sealed class HairPartFacts
    {
        public bool HasPrefab;
        /// <summary>The row reference the realization built ("guid#id"; the sex's default row when the stored one did not
        /// resolve), null when none.</summary>
        public string Item;
        /// <summary>The prefab asset name (the row's MainData, MainData02 for the male variant).</summary>
        public string PrefabName;
        public bool UseTopColor, UseUnderColor, UseSameSkinColorButton, UseMesh;
        /// <summary>`useAcsColor01` … `useAcsColor03`.</summary>
        public bool UseAcsColor01, UseAcsColor02, UseAcsColor03;
        /// <summary>`acsDefColor.Count` (a decoration colour N is shown and applied only for N ≤ this count, GH5).</summary>
        public int AcsDefColorCount;
        /// <summary>The prefab's bundles (`boneInfo` order; empty when none).</summary>
        public HairBundleFacts[] Bundles = Array.Empty<HairBundleFacts>();

        /// <summary>«カラー0N» (n = 1..3) is used: `useAcsColor0N` and n ≤ <see cref="AcsDefColorCount"/>.</summary>
        public bool AcsShown(int n) => HasPrefab && n >= 1 && n <= 3 && n <= AcsDefColorCount && (n == 1 ? UseAcsColor01 : n == 2 ? UseAcsColor02 : UseAcsColor03);

        /// <summary>At least one bundle has a correction transform (the «調整» tab).</summary>
        public bool AnyBundleShown
        {
            get
            {
                if (!HasPrefab || Bundles == null) return false;
                foreach (var b in Bundles) if (b != null && b.Shown) return true;
                return false;
            }
        }

        /// <summary>"no prefab": the facts of a part without a realization or without a CmpHair.</summary>
        public static HairPartFacts None() => new HairPartFacts();
    }

    /// <summary>
    /// The prefab facts of a node's realized hair parts (implemented by the Unity
    /// layer's HairInfo over the realization, `CharaContext.Hair[k]`'s CmpHair; registered as MakerServices.Hair). False when
    /// the node or part has no realization (a card-backed node, a failed load): the gates treat that as "no prefab".
    /// </summary>
    public interface IHairInfo
    {
        bool TryPart(string nodeId, int part, out HairPartFacts facts);
    }

    /// <summary>The values a hair colour preset row writes (list 305): base, top, under and
    /// specular = HSVToRGB of the row's Base / Top / Under / Specular H, S, V (Unity's Color.HSVToRGB, alpha 1), metallic and
    /// smoothness = the row's columns.</summary>
    public struct HairColourPresetValues
    {
        public Color4 Base, Top, Under, Specular;
        public float Metallic, Smoothness;
        public override string ToString() => FieldJson.ToText(Base) + " " + FieldJson.ToText(Top) + " " + FieldJson.ToText(Under) + " " + FieldJson.ToText(Specular) + " m " + Metallic + " s " + Smoothness;
    }

    /// <summary>List facts the ops check against (implemented over MakerLists by the Unity layer).</summary>
    public interface IAppearanceRows
    {
        /// <summary>True when <paramref name="reference"/> names a listed row of <paramref name="category"/> for the sex (0 male, 1 female).</summary>
        bool HasRow(int category, int sex, string reference);
        /// <summary>The first listed row of a category for the sex (HS2's id 0 rule → the first listed row), or null.</summary>
        string FirstRow(int category, int sex);
        /// <summary>The face skin rows listed for a head, in list order; null when unknown.</summary>
        IReadOnlyList<string> SkinsForHead(int sex, string headRef);
        /// <summary>The `Parent` column of an accessory row (an AccessoryParentKey name), or null when unknown.</summary>
        string AccessoryParent(int category, string reference);
        /// <summary>The `Scale`, `PosX`, `PosY` columns of a layout row (category 6 mole, 7 face paint; looked up by id, Q25
        /// quirk 2 fixed); false when the row is unknown.</summary>
        bool LayoutValues(int category, string reference, out float scale, out float posX, out float posY);
        /// <summary>True when the list of <paramref name="category"/> offers more than one row to the sex (the `maker.headList`
        /// gate of the male head list).</summary>
        bool ListHasMoreThanOne(int category, int sex);
        /// <summary>
        /// A colour preset row (336 skin colours, 4.3: 305 hair colours), looked up by its
        /// reference: <paramref name="value"/> = HSVToRGB(BaseH, BaseS, BaseV), <paramref name="swatch"/> = HSVToRGB(SampleH,
        /// SampleS, SampleV), both Unity's Color.HSVToRGB (<see cref="MakerEdits.HsvToRgb"/>) with alpha 1; false when the row is
        /// unknown or hidden or its columns do not parse.
        /// </summary>
        bool ColourPreset(int category, string reference, out Color4 value, out Color4 swatch);
        /// <summary>
        /// A hair colour preset row of 305, looked up by its reference: the four colours
        /// (HSVToRGB of the Base / Top / Under / Specular columns, <see cref="MakerEdits.HsvToRgb"/>, alpha 1), Metallic and
        /// Smoothness; false when the row is unknown or hidden or a column does not parse. The chip colour is
        /// <see cref="ColourPreset"/>(305, …)'s swatch (Sample).
        /// </summary>
        bool HairColourPreset(string reference, out HairColourPresetValues values);
    }

    /// <summary>Face presets of head rows (implemented by the Unity layer: TextAsset → face decoder → importer mapping).</summary>
    public interface IFacePresets
    {
        /// <summary>The face block of the preset of head row <paramref name="headRef"/> (its `head` included); false when the head
        /// has no preset. Unresolvable ids are reported through <paramref name="warnings"/> (`edit.presetUnresolved`).</summary>
        bool TryLoad(int sex, string headRef, out FaceData face, IList<string> warnings);
    }

    /// <summary>The edit session of a character: kept decoded layers and resolved inputs while the Edit tab shows it.</summary>
    public interface IEditSessionHost
    {
        void Begin(string nodeId);
        void End(string nodeId);
    }

    /// <summary>
    /// The names that make a piece of plugin data part of the face on a partial character load (Phase 4.6): the transforms,
    /// renderers and materials (a `_create` suffix removed) of a head row's prefab plus the transforms of the head bone
    /// object `p_cf_head_bone`. Implemented by the Unity layer from the catalog (cached per head ref); the Unity-free default
    /// is <see cref="HeadNames.Vanilla"/>.
    /// </summary>
    public interface IHeadNames
    {
        /// <summary>The head names of head row <paramref name="headRef"/> (`face.head`) for the sex (0 male, 1 female); never
        /// null (the vanilla names when the row or its prefab is unknown).</summary>
        IReadOnlyCollection<string> Of(int sex, string headRef);
    }

    /// <summary>
    /// The class rule of plugin data on a partial character load (Phase 4.6, «顔» / «体»): a skin overlay belongs to the face
    /// by its target group (face, eye, eyebrow and eyeline targets), a bone modifier when its bone is a head name of its own
    /// character's head, a MaterialEditor character item when its renderer or material name is a head name or its material
    /// belongs to the head-skin family `c[fm]_m_skin_head…`; everything else (unknown names, projector items) goes with the
    /// body. <see cref="Vanilla"/> holds the names of the vanilla heads (the head bone object's 92 transforms, the five
    /// vanilla head prefabs' transforms, renderers and materials) for every head ref.
    /// </summary>
    public sealed class HeadNames : IHeadNames
    {
        readonly HashSet<string> names;

        public HeadNames(IEnumerable<string> names)
        {
            this.names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var n in names ?? Array.Empty<string>()) if (!string.IsNullOrEmpty(n)) this.names.Add(StripCreate(n));
        }

        /// <summary>The same names for every head ref.</summary>
        public IReadOnlyCollection<string> Of(int sex, string headRef) => names;

        /// <summary>The vanilla head names (data names of the game's head prefabs; read from the installed game data).</summary>
        public static readonly HeadNames Vanilla = new HeadNames(new[]
        {
            "c_m_eye", "c_m_eye_01", "c_m_eye_02", "c_m_eye_03", "c_m_eye_namida", "c_m_eyekage", "c_m_eyekage_cf2",
            "c_m_eyekage_cm", "c_m_eyelashes", "c_m_eyelashes_cm", "c_m_tang", "c_m_tooth", "cf_J_CheekLow_L",
            "cf_J_CheekLow_R", "cf_J_CheekUp_L", "cf_J_CheekUp_R", "cf_J_Chin_rs", "cf_J_ChinLow", "cf_J_ChinTip_s",
            "cf_J_EarBase_s_L", "cf_J_EarBase_s_R", "cf_J_EarLow_L", "cf_J_EarLow_R", "cf_J_EarRing_L", "cf_J_EarRing_R",
            "cf_J_EarUp_L", "cf_J_EarUp_R", "cf_J_Eye01_L", "cf_J_Eye01_R", "cf_J_Eye01_s_L", "cf_J_Eye01_s_R", "cf_J_Eye02_L",
            "cf_J_Eye02_R", "cf_J_Eye02_s_L", "cf_J_Eye02_s_R", "cf_J_Eye03_L", "cf_J_Eye03_R", "cf_J_Eye03_s_L",
            "cf_J_Eye03_s_R", "cf_J_Eye04_L", "cf_J_Eye04_R", "cf_J_Eye04_s_L", "cf_J_Eye04_s_R", "cf_J_Eye_r_L",
            "cf_J_Eye_r_R", "cf_J_eye_rs_L", "cf_J_eye_rs_R", "cf_J_Eye_s_L", "cf_J_Eye_s_R", "cf_J_Eye_t_L", "cf_J_Eye_t_R",
            "cf_J_EyePos_rz_L", "cf_J_EyePos_rz_R", "cf_J_FaceBase", "cf_J_FaceLow_s", "cf_J_FaceLowBase", "cf_J_FaceRoot",
            "cf_J_FaceRoot_s", "cf_J_FaceUp_ty", "cf_J_FaceUp_tz", "cf_J_look_L", "cf_J_look_R", "cf_J_Mayu_L", "cf_J_Mayu_R",
            "cf_J_MayuMid_s_L", "cf_J_MayuMid_s_R", "cf_J_MayuTip_s_L", "cf_J_MayuTip_s_R", "cf_J_megane", "cf_J_Mouth_L",
            "cf_J_Mouth_R", "cf_J_MouthBase_s", "cf_J_MouthBase_tr", "cf_J_MouthCavity", "cf_J_MouthLow", "cf_J_MouthMove",
            "cf_J_Mouthup", "cf_J_Nose_r", "cf_J_Nose_t", "cf_J_Nose_tip", "cf_J_NoseBase_s", "cf_J_NoseBase_trs",
            "cf_J_NoseBridge_s", "cf_J_NoseBridge_t", "cf_J_NoseWing_tx_L", "cf_J_NoseWing_tx_R", "cf_J_pupil_s_L",
            "cf_J_pupil_s_R", "cf_m_skin_head_00", "cf_m_skin_head_01", "cf_m_skin_head_03", "cm_m_skin_head_00",
            "N_Earring_L", "N_Earring_R", "N_Face", "N_Hair_pin_L", "N_Hair_pin_R", "N_Hair_pony", "N_hair_Root",
            "N_Hair_twin_L", "N_Hair_twin_R", "N_Head", "n_head_cf1", "n_head_cf2", "n_head_cf2_c1", "n_head_cf3_hs2",
            "n_head_cm1", "N_Head_top", "N_Hitai", "N_Megane", "N_Mouth", "N_Nose", "o_eyebase_L", "o_eyebase_R",
            "o_eyelashes", "o_eyeshadow", "o_head", "o_namida", "o_tang", "o_tooth", "p_cf_head_00", "p_cf_head_01",
            "p_cf_head_02", "p_cf_head_03", "p_cf_head_bone", "p_cm_head_00",
        });

        /// <summary>The registered names (MakerServices.HeadNames), else <see cref="Vanilla"/>.</summary>
        public static IHeadNames Current => MakerServices.HeadNames ?? Vanilla;

        /// <summary>The skin overlay targets of the face group (KSOX texture types of the face, the eyes, the eyebrows and the
        /// eyeline); every other target, an unknown one included, is of the body.</summary>
        public static IReadOnlyCollection<string> FaceOverlayTargets => faceTargets;

        static readonly HashSet<string> faceTargets = new HashSet<string>(StringComparer.Ordinal)
        {
            "FaceOver", "FaceUnder", "FaceDetailOver", "FaceDetailUnder", "EyeUnderL", "EyeUnderR", "EyeOverL", "EyeOverR",
            "EyebrowUnder", "EyelineUnder",
        };

        /// <summary>A name without the prefab's `_create` suffix (the build draws the skin material without it).</summary>
        public static string StripCreate(string name) =>
            name != null && name.EndsWith("_create", StringComparison.Ordinal) ? name.Substring(0, name.Length - "_create".Length) : name;

        /// <summary>True for the head-skin material family: names starting `cf_m_skin_head` or `cm_m_skin_head`.</summary>
        public static bool IsHeadSkinMaterial(string material) =>
            material != null && material.Length >= 14 && material[0] == 'c' && (material[1] == 'f' || material[1] == 'm') &&
            string.CompareOrdinal(material, 2, "_m_skin_head", 0, 12) == 0;

        static bool Has(IReadOnlyCollection<string> set, string name)
        {
            if (set == null || string.IsNullOrEmpty(name)) return false;
            if (set is ICollection<string> c) return c.Contains(name) || c.Contains(StripCreate(name));
            foreach (var s in set) if (string.Equals(s, name, StringComparison.Ordinal) || string.Equals(s, StripCreate(name), StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>A skin overlay of the face group.</summary>
        public static bool IsFace(SkinOverlay overlay) => overlay?.Target != null && faceTargets.Contains(overlay.Target);

        /// <summary>A bone modifier on a head name of <paramref name="head"/> (the names of its own character's head).</summary>
        public static bool IsFace(BodyBoneMod mod, IReadOnlyCollection<string> head) => mod != null && Has(head, mod.Bone);

        /// <summary>A MaterialEditor character item whose renderer or material is a head name of <paramref name="head"/>, or whose
        /// material is of the head-skin family.</summary>
        public static bool IsFace(MaterialOverride item, IReadOnlyCollection<string> head)
        {
            string renderer = null, material = null;
            switch (item)
            {
                case MeName n: renderer = n.Renderer; material = n.Material; break;
                case MeRenderer r: renderer = r.Renderer; break;
                case MeMaterialItem m: material = m.Material; break;
            }
            return Has(head, renderer) || Has(head, material) || IsHeadSkinMaterial(material);
        }
    }

    /// <summary>The services the Unity layer registers (like CharaImport.Importer); null in plain Mono.</summary>
    public static class MakerServices
    {
        /// <summary>The head names of the partial character load (Phase 4.6; the Unity layer's HeadNameFacts over the catalog).
        /// Null (plain Mono): <see cref="Scenes.HeadNames.Vanilla"/>.</summary>
        public static IHeadNames HeadNames;
        public static IPartDefaults Defaults;
        public static IAppearanceRows Rows;
        public static IFacePresets Presets;
        public static IEditSessionHost Sessions;
        /// <summary>App option "keep colours on item change" (C7, default off): clothes, accessory and hair decoration colours
        /// are kept on an item change instead of taking the new prefab's defaults.</summary>
        public static bool KeepColoursOnItemChange;
        /// <summary>
        /// The app's male height pin (CharaAssembler.MaleHeightPin075, read when a gate is evaluated; the Unity layer's
        /// MakerSetup registers it): true = the engine pins the male height to 0.75 and the `maker.bodyHeight` gate hides the
        /// male height row (Y8). Null (plain Mono) = not pinned: the gate holds.
        /// </summary>
        public static Func<bool> MaleHeightPinned;
        /// <summary>The prefab facts of the realized hair parts (the Unity layer's HairInfo, registered
        /// by MakerSetup). Null (plain Mono): every prefab feature of the hair gates holds.</summary>
        public static IHairInfo Hair;
        /// <summary>The prefab facts of the realized clothes kinds (the Unity layer's ClothesInfo, registered by MakerSetup).
        /// Null (plain Mono): every prefab feature of the clothes gates holds.</summary>
        public static IClothesInfo Clothes;
        /// <summary>The prefab facts of the realized accessories by build slot (the Unity layer's AccessoryInfo, registered by
        /// MakerSetup). Null (plain Mono): every prefab feature of the accessory gates holds except the hair-type ones (or the
        /// facts of IPartDefaults.TryAccessoryFacts when that knows the row).</summary>
        public static IAccessoryInfo Accessories;
        /// <summary>App option `maker.copyAccessoryPluginData` (default off = vanilla): a slot copy also copies the source's
        /// MaterialEditor items, DynamicBoneEditor entries and AdditionalAccessoryControls rules to the target.</summary>
        public static bool CopyAccessoryPluginData;
        /// <summary>The realization loads MoreAccessories slots (≥ 20): the extension switches Enabled and MoreAccessories of the
        /// runtime (registered by MakerSetup). Null (plain Mono): on.</summary>
        public static Func<bool> MoreAccessoriesLoaded;
        /// <summary>The AdditionalAccessoryControls module is on (its coordinate-load features run; registered by MakerSetup).
        /// Null (plain Mono): on.</summary>
        public static Func<bool> AccessoryControlsOn;

        // ---- reference documents of the per-row reset (per scene session since 4.2,
        //      12)

        static WeakReference<SceneSession> lastSession;
        static readonly EditReferences legacy = EditReferences.Forwarding();

        /// <summary>The reference store of a scene session (SceneSession.MakerReferences: captured by its BeginEditSession, read
        /// by its ResetAppearance; cleared when the scene is replaced), or null for no session.</summary>
        public static EditReferences ReferencesOf(SceneSession session) => session?.MakerReferences;

        /// <summary>
        /// The 4.1 accessor kept for callers without a session at hand: the store of the scene session the Maker ops ran on
        /// last (<see cref="UseSession"/>; the app has one session), else an empty store whose Attach(session) selects that
        /// session. New code reads <see cref="ReferencesOf"/> / SceneSession.MakerReferences of its own session.
        /// </summary>
        public static EditReferences References =>
            lastSession != null && lastSession.TryGetTarget(out var s) && s != null ? s.MakerReferences : legacy;

        /// <summary>Makes <paramref name="session"/> the one <see cref="References"/> reads (StudioOps calls it before its Maker
        /// ops; kept weakly, so a disposed session does not stay alive).</summary>
        public static void UseSession(SceneSession session)
        {
            if (session == null) { lastSession = null; return; }
            if (lastSession != null && lastSession.TryGetTarget(out var s) && ReferenceEquals(s, session)) return;
            lastSession = new WeakReference<SceneSession>(session);
        }
    }

    /// <summary>Options of one Maker edit.</summary>
    public sealed class MakerEditOptions
    {
        public IPartDefaults Defaults;
        public IAppearanceRows Rows;
        public IFacePresets Presets;
        public bool KeepColours;
        /// <summary>Allows the rebuild-only paths (body.uncensor, studioOverlays; the bridge's `rebuild: true`).</summary>
        public bool AllowRebuild;
        /// <summary>A set whose row is not listed for this character is skipped with a warning instead of failing the edit
        /// (multi-selection edits).</summary>
        public bool SkipUnlistedRows;
        /// <summary>New accessory ids; the session's id source.</summary>
        public Func<string> NewId;
        /// <summary>The «前髪と後ろ髪の色を統一» consequence is not added: a hair colour set writes its own part only (the skin match
        /// «肌の色に合わせる» writes the shown part only, J12).</summary>
        public bool NoHairLink;
        /// <summary>`maker.copyAccessoryPluginData` (default off = vanilla): a slot copy also copies the source's MaterialEditor
        /// items, DynamicBoneEditor entries and AdditionalAccessoryControls rules (else the target's are cleared, as on an item
        /// change); ABMX modifiers never follow a copy.</summary>
        public bool CopyAccessoryPluginData;
        /// <summary>The realization loads MoreAccessories slots (≥ 20); off: an accessory placed there is data only and the slot
        /// ops warn `acc.slotNotLoaded`.</summary>
        public bool MoreAccessories = true;
        /// <summary>AdditionalAccessoryControls is on: a coordinate load applies its `AutoMatchBackHairColor` flags.</summary>
        public bool AccessoryControls = true;

        /// <summary>A shallow copy (an op that changes one option for its own call).</summary>
        public MakerEditOptions Clone() => (MakerEditOptions)MemberwiseClone();

        /// <summary>The registered services (MakerServices) with the session's ids.</summary>
        public static MakerEditOptions FromServices(SceneSession session) => new MakerEditOptions
        {
            Defaults = MakerServices.Defaults, Rows = MakerServices.Rows, Presets = MakerServices.Presets,
            KeepColours = MakerServices.KeepColoursOnItemChange, NewId = session != null ? (Func<string>)session.NewId : null,
            CopyAccessoryPluginData = MakerServices.CopyAccessoryPluginData,
            MoreAccessories = MakerServices.MoreAccessoriesLoaded?.Invoke() ?? true,
            AccessoryControls = MakerServices.AccessoryControlsOn?.Invoke() ?? true,
        };
    }

    /// <summary>What a Maker edit did: canonical paths that changed / already had the value, the consequence paths (a subset
    /// of Changed or NoOp), warnings (`edit.defaultsUnavailable`, `edit.listsUnavailable`, `edit.rowSkipped`, …).</summary>
    public sealed class MakerEditReport
    {
        public readonly List<string> Changed = new List<string>();
        public readonly List<string> NoOp = new List<string>();
        public readonly List<string> Consequences = new List<string>();
        public readonly List<string> Warnings = new List<string>();
        /// <summary>Per node id: its changed and no-op paths.</summary>
        public readonly Dictionary<string, (List<string> changed, List<string> noop)> PerTarget =
            new Dictionary<string, (List<string>, List<string>)>(StringComparer.Ordinal);
        /// <summary>Ids of accessories the edit created, in order.</summary>
        public readonly List<string> NewAccessoryIds = new List<string>();

        internal void Record(string node, string path, CommandStatus s, bool consequence)
        {
            var list = s == CommandStatus.NoOp ? NoOp : Changed;
            if (!list.Contains(path)) list.Add(path);
            if (consequence && !Consequences.Contains(path)) Consequences.Add(path);
            if (!PerTarget.TryGetValue(node, out var pt)) PerTarget[node] = pt = (new List<string>(), new List<string>());
            (s == CommandStatus.NoOp ? pt.noop : pt.changed).Add(path);
        }

        internal void Warn(string w) { if (!Warnings.Contains(w)) Warnings.Add(w); }
    }

    /// <summary>One planned set.</summary>
    public struct AppearanceSet
    {
        public string Path;
        public object Value;
        /// <summary>A rule consequence, not one of the caller's sets.</summary>
        public bool Consequence;
        public AppearanceSet(string path, object value, bool consequence) { Path = path; Value = value; Consequence = consequence; }
        public override string ToString() => (Consequence ? "+ " : "") + Path + " = " + FieldJson.ToText(Value);
    }
}
