// The Maker lists: the rows a character's editing pages offer, Unity-free over
// an IListRows source (ContentCatalog in the app: Unity/CatalogListRows.cs; the zipmod cache and a vanilla fixture in the
// plain-Mono tests).
//   - Rows(category, sex, ctx): the category's rows in HS2's Maker order: vanilla rows first in the source's order (bundle
//     order), then the zipmod rows ordered by their GUID with the invariant culture's string order (Sideloader's load order:
//     it sorts the zipmods by GUID with the default, culture-sensitive comparer), each zipmod's rows in file order; this
//     reproduces the order of all 61 lists HS2 shows on this install (H1 data, (4.0 impl)); first definition wins; without
//     vanilla rows of Possess 99 (Sideloader forces Possess 1 on mod rows, so their column is ignored) and without
//     distributionNo-gated rows unless ShowGated (decided by H1 / C9);
//   - per-sex categories: a category of the other sex has no list; male clothes kinds 2, 3, 5, 6 have none (the data
//     names the female 242 / 243 / 245 / 246, the Maker shows no list);
//   - face skins (211 / 111) filtered by the character's head (ListContext.Head) through FaceSkinHeads (faceSkinInfo); a
//     reference defined several times inside its zipmod is listed under the head of every definition (Sideloader keeps each
//     definition as a row: H3), at its first definition's place; faceSkinInfo re-points only the first definition;
//   - head presets: the heads whose Preset column is set, and their headPresetInfo entries (HeadPresets).
//   - colour presets: 336's and 305's Base / Sample columns (ColourPreset), 305's Top / Under / Specular / Metallic /
//     Smoothness columns (HairColourPreset).
// Results are cached per category (and per head for the skin lists) until Invalidate(); building the skin index of 3,192
// rows is the cold cost of the first call. Main-thread use (no locking).
using System;
using System.Collections.Generic;
using System.Globalization;

namespace HS2Next.Content.Maker
{
    /// <summary>What a list depends on besides category and sex.</summary>
    public sealed class ListContext
    {
        /// <summary>The character's head (`face.head`): filters the face-skin lists. Empty = no filter.</summary>
        public ContentId Head;

        public static ListContext ForHead(ContentId head) => new ListContext { Head = head };
    }

    /// <summary>A colour as the list columns store it: hue, saturation, value in 0..1 (Unity's HSV convention).</summary>
    public readonly struct HsvColour
    {
        public readonly float H, S, V;
        public HsvColour(float h, float s, float v) { H = h; S = s; V = v; }
        public override string ToString() => "HSV(" + H.ToString("R", CultureInfo.InvariantCulture) + ", " + S.ToString("R", CultureInfo.InvariantCulture) + ", " + V.ToString("R", CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>The columns of a hair colour preset row (list 305): four HSV colours and the metallic / smoothness values.</summary>
    public readonly struct HairPresetColumns
    {
        public readonly HsvColour Base, Top, Under, Specular;
        public readonly float Metallic, Smoothness;
        public HairPresetColumns(HsvColour b, HsvColour top, HsvColour under, HsvColour specular, float metallic, float smoothness)
        {
            Base = b; Top = top; Under = under; Specular = specular; Metallic = metallic; Smoothness = smoothness;
        }
        public override string ToString() => Base + " " + Top + " " + Under + " " + Specular + " m " + Metallic.ToString("R", CultureInfo.InvariantCulture) + " s " + Smoothness.ToString("R", CultureInfo.InvariantCulture);
    }

    /// <summary>Compares list rows by object identity (the sources hand out one ListRow per row).</summary>
    sealed class RowIdentity : IEqualityComparer<ListRow>
    {
        public static readonly RowIdentity Instance = new RowIdentity();
        public bool Equals(ListRow a, ListRow b) => ReferenceEquals(a, b);
        public int GetHashCode(ListRow r) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(r);
    }

    public sealed class MakerLists
    {
        sealed class SkinIndex
        {
            public readonly Dictionary<ContentId, List<ListRow>> ByHead = new Dictionary<ContentId, List<ListRow>>();
            // by row object (the sources hand out one ListRow per row): the head of the first definition and how it was decided
            public readonly Dictionary<ListRow, (ContentId head, FaceSkinHeads.How how)> OfRow = new Dictionary<ListRow, (ContentId, FaceSkinHeads.How)>(RowIdentity.Instance);
            // (head, row) pairs added by later definitions of a repeated reference (H3)
            public readonly HashSet<(ContentId head, ContentId row)> Extra = new HashSet<(ContentId, ContentId)>();

            public bool Listed(ListRow r, ContentId head) =>
                r != null && (OfRow.TryGetValue(r, out var x) && x.head == head && !head.IsEmpty || Extra.Count > 0 && Extra.Contains((head, r.Key)));
        }

        readonly Dictionary<int, List<ListRow>> shown = new Dictionary<int, List<ListRow>>();
        readonly Dictionary<int, SkinIndex> skins = new Dictionary<int, SkinIndex>();
        bool showGated;
        static readonly IReadOnlyList<ListRow> None = Array.Empty<ListRow>();

        public MakerLists(IListRows rows) { Source = rows ?? throw new ArgumentNullException(nameof(rows)); }

        public IListRows Source { get; }

        /// <summary>
        /// Show the vanilla rows the base game's distributionNo gate drops. Default false (I15: hidden) until H1 decides;
        /// C9: true together with `ContentSettings.aiInstall` when HS2 on this install lists them. Only rows the source has
        /// can be shown (the catalog drops gated rows at load while its AI-Shoujo flags are off).
        /// </summary>
        public bool ShowGated
        {
            get => showGated;
            set { if (showGated != value) { showGated = value; Invalidate(); } }
        }

        /// <summary>Forgets every cached list (after a catalog reload or a gate change).</summary>
        public void Invalidate() { shown.Clear(); skins.Clear(); }

        // ---------- categories per sex ----------

        static readonly HashSet<int> FemaleOnly = new HashSet<int> { 1, 210, 211, 212, 231, 232, 233, 240, 241, 242, 243, 244, 245, 246, 247, 501, 503, 505, 507 };
        static readonly HashSet<int> MaleOnly = new HashSet<int> { 0, 110, 111, 112, 121, 131, 132, 133, 140, 141, 144, 147, 500, 502, 504, 506 };

        /// <summary>True when the Maker offers a list of <paramref name="category"/> to a character of <paramref name="sex"/> (0 male, 1 female).</summary>
        public static bool HasList(int category, int sex) => sex == 0 ? !FemaleOnly.Contains(category) : !MaleOnly.Contains(category);

        /// <summary>Clothes categories by kind (0..7) and sex; -1 = the Maker shows no list (male kinds 2, 3, 5, 6).</summary>
        public static int ClothesCategory(int kind, int sex)
        {
            if (kind < 0 || kind > 7) return -1;
            return sex == 0 ? MaleClothes[kind] : 240 + kind;
        }

        static readonly int[] MaleClothes = { 140, 141, -1, -1, 144, -1, -1, 147 };

        static readonly string[] ClothesNames = { "top", "bottom", "innerTop", "innerBottom", "gloves", "pantyhose", "socks", "shoes" };
        static readonly string[] HairNames = { "back", "front", "side", "option" };

        /// <summary>
        /// The list category of a `ref` member of the documents (paths relative to `chara.json` / `coord.json` with their
        /// document prefix: `chara.face.skin`, `coord.clothes.top.item`, `coord.clothes.top.colors[1].pattern`), or -1. Accessory
        /// items take the accessory's category (<paramref name="accessoryCategory"/>), not decided by the path.
        /// </summary>
        public static int CategoryOfPath(string path, int sex, int accessoryCategory = -1)
        {
            if (string.IsNullOrEmpty(path)) return -1;
            bool m = sex == 0;
            if (path.StartsWith("appearance.", StringComparison.Ordinal)) path = path.Substring("appearance.".Length);
            switch (path)
            {
                case "chara.face.head": return m ? 110 : 210;
                case "chara.face.skin": return m ? 111 : 211;
                case "chara.face.detail.item": return m ? 112 : 212;
                case "chara.face.eyebrow.item": return 314;
                case "chara.face.eyes.left.iris.item": case "chara.face.eyes.right.iris.item": return 317;
                case "chara.face.eyes.left.pupil.item": case "chara.face.eyes.right.pupil.item": return 318;
                case "chara.face.eyes.highlight.item": return 319;
                case "chara.face.eyelashes.item": return 315;
                case "chara.face.mole.item": return 323;
                case "chara.face.makeup.eyeshadow.item": return 316;
                case "chara.face.makeup.cheek.item": return 320;
                case "chara.face.makeup.lip.item": return 322;
                case "chara.face.makeup.paint[0].item": case "chara.face.makeup.paint[1].item": return 313;
                case "chara.face.makeup.paint[0].layoutItem": case "chara.face.makeup.paint[1].layoutItem": return 7;
                case "chara.face.beard.item": return m ? 121 : -1;
                case "chara.body.skin.item": return m ? 131 : 231;
                case "chara.body.detail.item": return m ? 132 : 232;
                case "chara.body.sunburn.item": return m ? 133 : 233;
                case "chara.body.paint[0].item": case "chara.body.paint[1].item": return 313;
                case "chara.body.paint[0].layoutItem": case "chara.body.paint[1].layoutItem": return 8;
                case "chara.body.nipple.item": return 334;
                case "chara.body.underhair.item": return 335;
            }
            for (int k = 0; k < HairNames.Length; k++)
            {
                if (path == "chara.hair." + HairNames[k] + ".item") return 300 + k;
                if (path == "chara.hair." + HairNames[k] + ".mesh.item") return 306;
            }
            for (int k = 0; k < ClothesNames.Length; k++)
            {
                string p = "coord.clothes." + ClothesNames[k];
                if (path == p + ".item") return ClothesCategory(k, sex);
                for (int j = 0; j < 3; j++)
                    if (path == p + ".colors[" + j.ToString(CultureInfo.InvariantCulture) + "].pattern") return ClothesCategory(k, sex) < 0 ? -1 : 348;
            }
            if (path.StartsWith("coord.accessories[", StringComparison.Ordinal) && path.EndsWith("].item", StringComparison.Ordinal))
                return accessoryCategory >= 350 && accessoryCategory <= 363 ? accessoryCategory : -1;
            return -1;
        }

        // ---------- rows ----------

        /// <summary>True when the Maker hides the row from every list (vanilla Possess 99, or gated while the gate is closed).</summary>
        public bool IsHidden(ListRow r) => r == null || (r.IsVanilla && r.Possess == 99) || (r.Gated && !showGated);

        /// <summary>The rows the Maker offers for <paramref name="category"/> to a character of <paramref name="sex"/>; face skins filtered by <paramref name="ctx"/>.Head.</summary>
        public IReadOnlyList<ListRow> Rows(int category, int sex, ListContext ctx = null)
        {
            if (!HasList(category, sex)) return None;
            if (FaceSkinHeads.IsSkinCategory(category) && ctx != null && !ctx.Head.IsEmpty)
                return SkinsOf(category).ByHead.TryGetValue(ctx.Head, out var l) ? l : None;
            return Shown(category);
        }

        /// <summary>True when (category, key) is a row the list offers (the op check).</summary>
        public bool IsListed(int category, int sex, ContentId key, ListContext ctx = null)
        {
            if (!HasList(category, sex)) return false;
            var r = Source.Find(category, key);
            if (r == null || IsHidden(r)) return false;
            if (FaceSkinHeads.IsSkinCategory(category) && ctx != null && !ctx.Head.IsEmpty) return SkinsOf(category).Listed(r, ctx.Head);
            return true;
        }

        List<ListRow> Shown(int category)
        {
            if (shown.TryGetValue(category, out var l)) return l;
            var all = Source.RowsOf(category);
            l = new List<ListRow>(all.Count);
            var mods = new List<(byte[] key, int index, ListRow row)>();
            foreach (var r in all)
            {
                if (IsHidden(r)) continue;
                if (r.IsVanilla) l.Add(r);
                else mods.Add((SortKeyOf(r.Key.SourceGuid), mods.Count, r));
            }
            mods.Sort((a, b) => { int c = CompareKeys(a.key, b.key); return c != 0 ? c : a.index.CompareTo(b.index); });
            foreach (var m in mods) l.Add(m.row);
            shown[category] = l;
            return l;
        }

        // The invariant culture's sort keys of the GUIDs, computed once per GUID (a culture-sensitive Compare per pair is slow).
        static readonly Dictionary<string, byte[]> sortKeys = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        static readonly CompareInfo Invariant = CultureInfo.InvariantCulture.CompareInfo;

        static byte[] SortKeyOf(string guid)
        {
            if (!sortKeys.TryGetValue(guid, out var k)) sortKeys[guid] = k = Invariant.GetSortKey(guid, CompareOptions.None).KeyData;
            return k;
        }

        static int CompareKeys(byte[] a, byte[] b)
        {
            if (ReferenceEquals(a, b)) return 0;
            int n = Math.Min(a.Length, b.Length);
            for (int i = 0; i < n; i++) if (a[i] != b[i]) return a[i] < b[i] ? -1 : 1;
            return a.Length.CompareTo(b.Length);
        }

        /// <summary>Sideloader's zipmod order of two GUIDs (the invariant culture's string order).</summary>
        public static int CompareGuids(string a, string b) => CompareKeys(SortKeyOf(a ?? ""), SortKeyOf(b ?? ""));

        SkinIndex SkinsOf(int category)
        {
            if (skins.TryGetValue(category, out var ix)) return ix;
            ix = new SkinIndex();
            string lastZip = null;
            ZipMakerInfo info = null;
            ContentId lastHead = default;
            List<ListRow> lastList = null;
            foreach (var r in Shown(category))
            {
                // rows of one zipmod are consecutive: look its infos up once per zipmod (path strings are long)
                if (!r.IsVanilla && !ReferenceEquals(r.ZipPath, lastZip)) { lastZip = r.ZipPath; info = Source.InfoOf(lastZip); }
                var h = FaceSkinHeads.HeadOf(r, Source, r.IsVanilla ? null : info, out var how, out _);
                ix.OfRow[r] = (h, how);
                if (!h.IsEmpty)
                {
                    if (lastList == null || h != lastHead)
                    {
                        if (!ix.ByHead.TryGetValue(h, out lastList)) ix.ByHead[h] = lastList = new List<ListRow>();
                        lastHead = h;
                    }
                    lastList.Add(r);
                }
            }
            // later definitions of a repeated reference: their raw HeadID (no faceSkinInfo), listed at the reference's place
            var rep = Source.RepeatedDefinitions(category);
            if (rep != null && rep.Count > 0)
            {
                var shownRows = Shown(category);
                Dictionary<ListRow, int> pos = null;
                var touched = new HashSet<List<ListRow>>();
                foreach (var kv in rep)
                {
                    var r = Source.Find(category, kv.Key);
                    if (r == null || IsHidden(r) || r.IsVanilla) continue;
                    for (int d = 1; d < kv.Value.Count; d++)
                    {
                        var hd = FaceSkinHeads.HeadOf(kv.Value[d], Source, null, out _, out _);
                        if (hd.IsEmpty || ix.Listed(r, hd)) continue;
                        ix.Extra.Add((hd, r.Key));
                        if (!ix.ByHead.TryGetValue(hd, out var l2)) ix.ByHead[hd] = l2 = new List<ListRow>();
                        l2.Add(r);
                        touched.Add(l2);
                    }
                }
                if (touched.Count > 0)
                {
                    pos = new Dictionary<ListRow, int>(shownRows.Count);
                    for (int i = 0; i < shownRows.Count; i++) pos[shownRows[i]] = i;
                    foreach (var l in touched) l.Sort((a, b) => pos[a].CompareTo(pos[b]));
                }
            }
            skins[category] = ix;
            return ix;
        }

        // ---------- warm caches, layout rows ----------

        /// <summary>
        /// Builds the caches of <paramref name="categories"/> now (F15): the shown rows and, for the face-skin categories, the
        /// head index, so the first <see cref="Rows"/> call of a page meets its 2 ms target (the Unity layer warms 211 and 111
        /// when the Maker services register for a catalog). Cheap when already built.
        /// </summary>
        public void Warm(params int[] categories)
        {
            if (categories == null) return;
            foreach (int c in categories)
            {
                Shown(c);
                if (FaceSkinHeads.IsSkinCategory(c)) SkinsOf(c);
            }
        }

        /// <summary>True when the caches of <paramref name="category"/> are built (the skin head index for skin categories).</summary>
        public bool IsWarm(int category) => shown.ContainsKey(category) && (!FaceSkinHeads.IsSkinCategory(category) || skins.ContainsKey(category));

        /// <summary>
        /// The `Scale`, `PosX`, `PosY` columns of a layout row (category 6 mole, 7 face paint, 8 body paint), looked up by id
        /// (Q25 quirk 2 fixed); false for an unknown or hidden row or a column that does not parse.
        /// </summary>
        public bool LayoutValues(int category, ContentId key, out float scale, out float posX, out float posY)
        {
            scale = posX = posY = 0f;
            var r = Source.Find(category, key);
            if (r == null || IsHidden(r)) return false;
            return Num(r["Scale"], out scale) && Num(r["PosX"], out posX) && Num(r["PosY"], out posY);
        }

        static bool Num(string s, out float v) =>
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && !float.IsNaN(v) && !float.IsInfinity(v);

        /// <summary>
        /// The HSV columns of a colour preset row (336 skin colours, 4.3: 305 hair
        /// colours), looked up by reference: <paramref name="value"/> = (BaseH, BaseS, BaseV) — what a push stores, through
        /// Unity's Color.HSVToRGB —, <paramref name="swatch"/> = (SampleH, SampleS, SampleV) — the chip's colour. False for an
        /// unknown or hidden row or a column that does not parse. The RGB conversion is the caller's (IAppearanceRows in the
        /// scene layer: MakerEdits.HsvToRgb), so this assembly stays free of the document types.
        /// </summary>
        public bool ColourPreset(int category, ContentId key, out HsvColour value, out HsvColour swatch)
        {
            value = swatch = default;
            var r = Source.Find(category, key);
            if (r == null || IsHidden(r)) return false;
            return Hsv(r, "Base", out value) && Hsv(r, "Sample", out swatch);
        }

        /// <summary>The hair colour preset list.</summary>
        public const int HairColourCategory = 305;

        /// <summary>
        /// The columns of a hair colour preset row of 305, looked up by reference:
        /// (BaseH, BaseS, BaseV), (TopH, …), (UnderH, …), (SpecularH, …) and Metallic, Smoothness. False for an unknown or hidden
        /// row or a column that does not parse. The chip is <see cref="ColourPreset"/>(305, …)'s Sample; the RGB conversion is the
        /// caller's (IAppearanceRows in the scene layer).
        /// </summary>
        public bool HairColourPreset(ContentId key, out HairPresetColumns values)
        {
            values = default;
            var r = Source.Find(HairColourCategory, key);
            if (r == null || IsHidden(r)) return false;
            if (!Hsv(r, "Base", out var b) || !Hsv(r, "Top", out var t) || !Hsv(r, "Under", out var u) || !Hsv(r, "Specular", out var s)) return false;
            if (!Num(r["Metallic"], out float m) || !Num(r["Smoothness"], out float sm)) return false;
            values = new HairPresetColumns(b, t, u, s, m, sm);
            return true;
        }

        static bool Hsv(ListRow r, string prefix, out HsvColour c)
        {
            c = default;
            if (!Num(r[prefix + "H"], out float h) || !Num(r[prefix + "S"], out float s) || !Num(r[prefix + "V"], out float v)) return false;
            c = new HsvColour(h, s, v);
            return true;
        }

        /// <summary>True when the list of <paramref name="category"/> offers more than one row to the sex (the `maker.headList`
        /// gate: HS2 hides the male head list because vanilla has one male head; 4.1 shows it when more are installed, F3).</summary>
        public bool HasMoreThanOne(int category, int sex) => Rows(category, sex).Count > 1;

        // ---------- face skins and heads ----------

        /// <summary>Every (head, skin row) pair of a face-skin category's lists (the lists of every head), in no particular order.</summary>
        public IEnumerable<(ContentId head, ListRow row)> SkinPairs(int category)
        {
            if (!FaceSkinHeads.IsSkinCategory(category)) yield break;
            foreach (var kv in SkinsOf(category).ByHead)
                foreach (var r in kv.Value) yield return (kv.Key, r);
        }

        /// <summary>True when the skin reference is listed under <paramref name="head"/> (by any of its definitions).</summary>
        public bool IsListedUnder(ListRow skin, ContentId head) =>
            skin != null && FaceSkinHeads.IsSkinCategory(skin.Category) && SkinsOf(skin.Category).Listed(skin, head);

        /// <summary>The head a face-skin row (its first definition) is listed under (empty when none or when the row is not a shown skin row).</summary>
        public ContentId HeadOf(ListRow skin)
        {
            if (skin == null || !FaceSkinHeads.IsSkinCategory(skin.Category)) return default;
            if (SkinsOf(skin.Category).OfRow.TryGetValue(skin, out var x)) return x.head;
            return FaceSkinHeads.HeadOf(skin, Source);
        }

        /// <summary>How the head of a shown skin row was decided (see <see cref="FaceSkinHeads.How"/>).</summary>
        public FaceSkinHeads.How HeadSourceOf(ListRow skin)
        {
            if (skin != null && FaceSkinHeads.IsSkinCategory(skin.Category) && SkinsOf(skin.Category).OfRow.TryGetValue(skin, out var x)) return x.how;
            FaceSkinHeads.HeadOf(skin, Source, out var how, out _);
            return how;
        }

        /// <summary>The first skin listed for <paramref name="head"/> (the head-change snap), or null when the head has none.</summary>
        public ListRow FirstSkinFor(ContentId head, int sex)
        {
            var l = Rows(sex == 0 ? 111 : 211, sex, ListContext.ForHead(head));
            return l.Count > 0 ? l[0] : null;
        }

        /// <summary>
        /// The skin to store after a head change: <paramref name="currentSkin"/> when it is listed for the new head or the
        /// head has no skin row, else the first listed skin.
        /// </summary>
        public ContentId SkinAfterHeadChange(ContentId newHead, ContentId currentSkin, int sex)
        {
            int cat = sex == 0 ? 111 : 211;
            if (IsListed(cat, sex, currentSkin, ListContext.ForHead(newHead))) return currentSkin;
            var first = FirstSkinFor(newHead, sex);
            return first != null ? first.Key : currentSkin;
        }

        /// <summary>The head rows of the sex whose `Preset` column names a preset (the «サンプル» grid), in list order.</summary>
        public IReadOnlyList<ListRow> PresetHeads(int sex)
        {
            var l = new List<ListRow>();
            foreach (var h in Rows(sex == 0 ? 110 : 210, sex)) if (HeadPresets.PresetName(h) != null) l.Add(h);
            return l;
        }

        /// <summary>The headPresetInfo entry of a head's preset, or null (vanilla heads and heads without an entry).</summary>
        public HeadPresetEntry PresetEntryOf(ListRow head) => HeadPresets.FindEntry(Source, head);

        // ---------- names ----------

        /// <summary>
        /// The row's display name for a UI language ("ja", "en", "zh-Hans"; others as "en"): `Name`, `EN_US`, `ZH_CN`; a missing or
        /// "0" localised name falls back to `Name`.
        /// </summary>
        public static string NameOf(ListRow r, string language)
        {
            if (r == null) return null;
            string name = r["Name"];
            string col = language == "ja" ? null : language == "zh-Hans" ? "ZH_CN" : "EN_US";
            if (col == null) return name;
            var loc = r[col];
            return string.IsNullOrEmpty(loc) || loc == "0" ? name : loc;
        }
    }
}
