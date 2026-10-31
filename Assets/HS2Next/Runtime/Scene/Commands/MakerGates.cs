// The Maker gates of the Edit tab (
// item 3): the gate value MakerGate with the one parser of the "maker.<family>[:<part>[:
// <feature>]]" names, the evaluation context GateContext, GateHolds and the hair gates' rules. Unity-free. The names are
// HS2Next.UI.Core.SchemaGates' (the scene layer does not reference the UI core; SchemaTests_CharaEditHair checks that both
// parse the hair gates alike). A new family (4.4: `maker.clothes:<k>:<feature>`, the
// accessory slots keyed by accessory id) adds one entry to MakerGate's family table and one case to GateHolds(MakerGate,
// GateContext), and its facts to GateContext.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using HS2Next.Content;
using HS2Next.Save;

namespace HS2Next.Scenes
{
    /// <summary>
    /// A parsed Maker gate "maker.&lt;family&gt;[:&lt;part&gt;[:&lt;feature&gt;]]" (immutable): `maker.headList`,
    /// `maker.bodyHeight` (no part), `maker.list:&lt;c&gt;` (part = the category, digits only), `maker.hair:&lt;p&gt;:&lt;f&gt;` (part =
    /// a hair part name of HairData.PartNames, feature = one of <see cref="MakerEdits.HairGateFeatures"/>). default(MakerGate) is
    /// no gate (<see cref="IsValid"/> false).
    /// </summary>
    public readonly struct MakerGate : IEquatable<MakerGate>
    {
        public const string Prefix = "maker.";
        /// <summary>The families (the text between "maker." and the first ':').</summary>
        public const string HeadList = "headList", BodyHeight = "bodyHeight", List = "list", Hair = "hair", Clothes = "clothes", Acc = "acc";

        /// <summary>The family (<see cref="HeadList"/>, <see cref="BodyHeight"/>, <see cref="List"/>, <see cref="Hair"/>); null for
        /// default(MakerGate).</summary>
        public readonly string Family;
        /// <summary>The part as written (the category of a list gate, the hair part name); null for a family without parts.</summary>
        public readonly string Part;
        /// <summary>The part's index: the category of a list gate, the hair part 0..3 (back, front, side, option); -1 without one.</summary>
        public readonly int Index;
        /// <summary>The feature of a three-component family; null otherwise.</summary>
        public readonly string Feature;

        MakerGate(string family, string part, int index, string feature)
        {
            Family = family;
            Part = part;
            Index = index;
            Feature = feature;
        }

        public bool IsValid => Family != null;

        /// <summary>The gate name ("maker.hair:back:acs1"); null for default(MakerGate).</summary>
        public string Name => Family == null ? null : Prefix + Family + (Part != null ? ":" + Part : "") + (Feature != null ? ":" + Feature : "");

        public override string ToString() => Name ?? "(no gate)";
        public bool Equals(MakerGate o) => Family == o.Family && Part == o.Part && Feature == o.Feature;
        public override bool Equals(object o) => o is MakerGate g && Equals(g);
        public override int GetHashCode() => (Name ?? "").GetHashCode();

        // ---- the family table (the extension point: 4.4 adds the clothes family, 4.5 the accessory slots)

        delegate bool PartParser(string part, out int index);

        sealed class FamilySpec
        {
            public readonly string Name, Head;
            /// <summary>0 = the exact name, 1 = "maker.&lt;f&gt;:&lt;part&gt;", 2 = "maker.&lt;f&gt;:&lt;part&gt;:&lt;feature&gt;".</summary>
            public readonly int Arity;
            public readonly PartParser ParsePart;
            /// <summary>Arity 2: the features in their fixed order.</summary>
            public readonly IReadOnlyList<string> Features;

            public FamilySpec(string name, int arity, PartParser parsePart = null, IReadOnlyList<string> features = null)
            {
                Name = name;
                Arity = arity;
                Head = Prefix + name + (arity > 0 ? ":" : "");
                ParsePart = parsePart;
                Features = features;
            }

            /// <summary>The gate uses this family's syntax (parsed or not): the exact name, or the "maker.&lt;f&gt;:" prefix.</summary>
            public bool Claims(string gate) => Arity == 0 ? gate == Head : gate.StartsWith(Head, StringComparison.Ordinal);
        }

        static readonly FamilySpec[] Families =
        {
            new FamilySpec(HeadList, 0),
            new FamilySpec(BodyHeight, 0),
            new FamilySpec(List, 1, ListCategory),
            new FamilySpec(Hair, 2, HairPart, MakerEdits.HairGateFeatures),
            new FamilySpec(Clothes, 2, MakerEdits.TryClothesKind, MakerEdits.ClothesGateFeatures),
            new FamilySpec(Acc, 2, MakerEdits.TryAccessorySlotPart, MakerEdits.AccessoryGateFeatures),
        };

        static bool ListCategory(string part, out int index) => int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out index);

        static bool HairPart(string part, out int index)
        {
            index = MakerEdits.HairPartIndex(part);
            return index >= 0;
        }

        static FamilySpec Claim(string gate)
        {
            if (gate == null || !gate.StartsWith(Prefix, StringComparison.Ordinal)) return null;
            foreach (var f in Families) if (f.Claims(gate)) return f;
            return null;
        }

        /// <summary>
        /// The one parser of the Maker gate names: true for a well-formed gate of a known family (a known part and, for a
        /// three-component family, a known feature; the feature is everything after the part's ':'); false for null, a gate
        /// outside "maker.", an unknown family, or a malformed gate of a known family (<see cref="UnparsedHolds"/> tells them
        /// apart).
        /// </summary>
        public static bool TryParse(string gate, out MakerGate g)
        {
            g = default;
            var f = Claim(gate);
            if (f == null) return false;
            if (f.Arity == 0) { g = new MakerGate(f.Name, null, -1, null); return true; }
            string rest = gate.Substring(f.Head.Length), part = rest, feature = null;
            if (f.Arity == 2)
            {
                int colon = rest.IndexOf(':');
                if (colon < 0) return false;
                part = rest.Substring(0, colon);
                feature = rest.Substring(colon + 1);
            }
            if (!f.ParsePart(part, out int index)) return false;
            if (f.Arity == 2 && !((IList<string>)f.Features).Contains(feature)) return false;
            g = new MakerGate(f.Name, part, index, feature);
            return true;
        }

        /// <summary>The rule of a gate <see cref="TryParse"/> refused: a malformed gate of a family with parts
        /// ("maker.list:x", "maker.hair:back:nonsense") does not hold; any other gate (null, empty, not a Maker gate, an unknown
        /// family) holds.</summary>
        public static bool UnparsedHolds(string gate) => Claim(gate) == null;

        /// <summary>The features of a three-component family in their fixed order (the hair features); null for another family.</summary>
        public static IReadOnlyList<string> FeaturesOf(string family)
        {
            foreach (var f in Families) if (f.Name == family) return f.Features;
            return null;
        }
    }

    /// <summary>
    /// What the Maker gates are evaluated against (one character, the primary of the Edit tab): its sex (0 male, 1 female),
    /// the list facts (`maker.headList`), the male height pin (`maker.bodyHeight`), the list predicate (`maker.list:&lt;c&gt;`;
    /// open when null), the node id with its hair document and the hair prefab facts (`maker.hair:*`; MakerServices.Hair, null =
    /// plain Mono: every prefab feature holds). 4.4 adds the clothes facts.
    /// </summary>
    public sealed class GateContext
    {
        public int Sex;
        public IAppearanceRows Rows;
        public bool MaleHeightPinned;
        public Func<int, int, bool> HasList;
        public string NodeId;
        public HairData Hair;
        public IHairInfo HairInfo;
        /// <summary>The character's coordinate document (the clothes gates' stored patterns; null: the format defaults).</summary>
        public CoordDocument Coord;
        /// <summary>The prefab facts of the realized clothes kinds (`maker.clothes:*`; MakerServices.Clothes, null = plain Mono:
        /// every prefab feature holds).</summary>
        public IClothesInfo ClothesInfo;
        /// <summary>The prefab facts of the realized accessories by build slot (`maker.acc:*`; MakerServices.Accessories, null = plain
        /// Mono).</summary>
        public IAccessoryInfo AccessoryInfo;
        /// <summary>The prefab facts of rows without a realization (an accessory slot the realization does not load;
        /// MakerServices.Defaults, null = none).</summary>
        public IPartDefaults Defaults;
    }

    public static partial class MakerEdits
    {
        // ================================================================ gates

        /// <summary>
        /// The Maker gates of the Edit tab for a character of <paramref name="sex"/> (0 male, 1 female,
        /// item 4): `maker.headList` (females; males when the male head list has more than one row),
        /// `maker.bodyHeight` (females; males unless <paramref name="maleHeightPinned"/>), `maker.list:&lt;c&gt;` (the Maker offers
        /// list c to the sex: <paramref name="hasList"/>; true when null). Other gates: true. The names are
        /// HS2Next.UI.Core.SchemaGates' (the scene layer does not reference the UI core). The positional form of
        /// <see cref="GateHolds(string, GateContext)"/>, kept for its callers.
        /// </summary>
        public static bool GateHolds(string gate, int sex, IAppearanceRows rows, bool maleHeightPinned, Func<int, int, bool> hasList = null,
            string nodeId = null, HairData hair = null, IHairInfo hairInfo = null) =>
            GateHolds(gate, new GateContext { Sex = sex, Rows = rows, MaleHeightPinned = maleHeightPinned, HasList = hasList, NodeId = nodeId, Hair = hair, HairInfo = hairInfo });

        /// <summary>A gate by name: null or empty holds; a Maker gate through <see cref="GateHolds(MakerGate, GateContext)"/>;
        /// others by <see cref="MakerGate.UnparsedHolds"/>.</summary>
        public static bool GateHolds(string gate, GateContext ctx)
        {
            if (string.IsNullOrEmpty(gate)) return true;
            return MakerGate.TryParse(gate, out var g) ? GateHolds(g, ctx) : MakerGate.UnparsedHolds(gate);
        }

        /// <summary>A parsed gate in <paramref name="ctx"/> (null: a male without lists, facts or document); default(MakerGate) holds.</summary>
        public static bool GateHolds(MakerGate g, GateContext ctx)
        {
            ctx = ctx ?? new GateContext();
            switch (g.Family)
            {
                case MakerGate.HeadList: return ctx.Sex != 0 || (ctx.Rows?.ListHasMoreThanOne(110, 0) ?? false);
                case MakerGate.BodyHeight: return ctx.Sex != 0 || !ctx.MaleHeightPinned;
                case MakerGate.List: return ctx.HasList?.Invoke(g.Index, ctx.Sex) ?? true;
                case MakerGate.Hair: return HairFeatureHolds(g.Index, g.Feature, ctx.NodeId, ctx.Hair, ctx.HairInfo);
                case MakerGate.Clothes: return ClothesFeatureHolds(g.Index, g.Feature, ctx);
                case MakerGate.Acc: return AccessoryFeatureHolds(g.Index, g.Feature, ctx);
            }
            return true;
        }

        /// <summary>The hair gates' prefix and features (the names of HS2Next.UI.Core.SchemaGates, which the scene layer does not
        /// reference): "maker.hair:&lt;part&gt;:&lt;feature&gt;". The order is fixed (SchemaTests_CharaEditHair.Gates).</summary>
        public const string HairGatePrefix = MakerGate.Prefix + MakerGate.Hair + ":";
        public static readonly IReadOnlyList<string> HairGateFeatures = new[]
        {
            "prefab", "topRow", "underRow", "glossRow", "skin", "acs", "acs1", "acs2", "acs3", "bundles", "mesh", "meshSet",
        };

        /// <summary>The part index (0..3) of a hair part name ("back", "front", "side", "option"), or -1.</summary>
        public static int HairPartIndex(string name) => name == null ? -1 : Array.IndexOf(HairData.PartNames, name);

        /// <summary>Splits "maker.hair:&lt;part&gt;:&lt;feature&gt;" (<see cref="MakerGate.TryParse"/>); false for other gates, an
        /// unknown part or feature.</summary>
        public static bool TryHairGate(string gate, out int part, out string feature)
        {
            if (MakerGate.TryParse(gate, out var g) && g.Family == MakerGate.Hair)
            {
                part = g.Index;
                feature = g.Feature;
                return true;
            }
            part = -1;
            feature = null;
            return false;
        }

        /// <summary>
        /// A hair gate (J13) for node <paramref name="nodeId"/> whose hair document is <paramref name="hair"/>
        /// (null: the format defaults — auto on, no mesh pattern): the prefab features come from <paramref name="info"/>
        /// (MakerServices.Hair; a part it does not know has no prefab: every feature off; <paramref name="info"/> null — plain
        /// Mono — every prefab feature holds), «根本と毛先の色を自動設定» and the mesh item from the document:
        /// prefab = CmpHair; topRow / underRow = useTopColor / useUnderColor and auto off; glossRow = prefab and auto off; skin =
        /// useSameSkinColorButton; acsN = useAcsColor0N and N ≤ the acsDefColor count; acs = any acsN; bundles = a bundle with a
        /// correction transform; mesh = useMesh; meshSet = mesh and `mesh.item` is not the none row. An unknown hair gate does not
        /// hold.
        /// </summary>
        public static bool HairGateHolds(string gate, string nodeId, HairData hair, IHairInfo info) =>
            TryHairGate(gate, out int part, out string feature) && HairFeatureHolds(part, feature, nodeId, hair, info);

        // The rules of HairGateHolds for a parsed gate (part 0..3, a feature of HairGateFeatures).
        static bool HairFeatureHolds(int part, string feature, string nodeId, HairData hair, IHairInfo info)
        {
            HairPartFacts f = null;
            bool known = info != null;
            if (known && (nodeId == null || !info.TryPart(nodeId, part, out f) || f == null)) f = HairPartFacts.None();
            bool auto = hair?.Editor?.AutoSetting ?? true;
            bool prefab = !known || f.HasPrefab;
            switch (feature)
            {
                case "prefab": return prefab;
                case "topRow": return (!known || f.HasPrefab && f.UseTopColor) && !auto;
                case "underRow": return (!known || f.HasPrefab && f.UseUnderColor) && !auto;
                case "glossRow": return prefab && !auto;
                case "skin": return !known || f.HasPrefab && f.UseSameSkinColorButton;
                case "acs": return !known || f.AcsShown(1) || f.AcsShown(2) || f.AcsShown(3);
                case "acs1": return !known || f.AcsShown(1);
                case "acs2": return !known || f.AcsShown(2);
                case "acs3": return !known || f.AcsShown(3);
                case "bundles": return !known || f.AnyBundleShown;
                case "mesh": return !known || f.HasPrefab && f.UseMesh;
                case "meshSet":
                {
                    string item = hair?.Parts[part]?.Mesh?.Item ?? DocDefaults.NoneRef;
                    return (!known || f.HasPrefab && f.UseMesh) && !string.Equals(item, DocDefaults.NoneRef, StringComparison.Ordinal);
                }
            }
            return false;
        }
    }
}
