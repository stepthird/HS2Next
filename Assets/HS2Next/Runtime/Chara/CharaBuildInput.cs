// What CharaAssembler.Build reads: the documents of a stored character subtree, the HS2 MessagePack node trees the
// document adapter derives from them (Card), the typed plugin data and the resolver that hands the assembler the list row
// of each reference - all filled by the document adapter (Runtime/Chara/Documents, CharaDocumentInput.From). The
// assembler asks every list row through ICharaPartResolver with a part key, the save-format path of the reference. A card
// is always imported into documents first; the earlier build straight from a card is retired.
using System.Collections.Generic;
using System.Globalization;
using HS2Next.Content;
using HS2Next.Data;
using HS2Next.Data.Ext;

namespace HS2Next.Chara
{
    public sealed class CharaBuildInput
    {
        /// <summary>HS2 blocks as MessagePack node trees (HS2 keys); the root name comes from Parameter.fullname.</summary>
        public Card Card;
        /// <summary>Typed plugin data; null = CardExtensions.Decode(Card) as in Phase 1.</summary>
        public CardExtData Ext;
        /// <summary>Resolver of the list rows (DocumentPartResolver).</summary>
        public ICharaPartResolver Parts;
        /// <summary>Build slot → accessory id (documents); null for cards.</summary>
        public IReadOnlyDictionary<int, string> AccessoryIds;
        /// <summary>Log text: "card &lt;path&gt;" or "document &lt;name&gt;".</summary>
        public string Source;
        /// <summary>The stored documents of a document build (live objects; the edit units read them
        /// 1); null for a card build.</summary>
        public HS2Next.Save.CharaDocument Chara;
        public HS2Next.Save.CoordDocument Coord;
        /// <summary>Resources of the documents' plugin images (null for cards).</summary>
        public HS2Next.Save.ResStore Res;
        /// <summary>Accessory id ↔ build slot of the documents (null for cards).</summary>
        public HS2Next.Chara.Documents.AccessorySlotTable Slots;

    }

    public interface ICharaPartResolver
    {
        /// <summary>
        /// The list row of one reference. <paramref name="part"/> = a <see cref="PartKeys"/> value, or null for a fixed
        /// default row of the assembler (e.g. the head 0 fallback); <paramref name="property"/> = the assembler's Sideloader
        /// property (read by no resolver since the build straight from a card was retired).
        /// </summary>
        ListEntry Resolve(int category, int rawId, string property, string part);
    }

    /// <summary>
    /// Part keys of the assembler's list lookups: the save-format paths of the references
    ///, prefixed by the document ("chara:", "coord:") or "acc:&lt;build slot&gt;" for accessories.
    /// </summary>
    public static class PartKeys
    {
        public const string CharaPrefix = "chara:", CoordPrefix = "coord:", AccessoryPrefix = "acc:";

        public const string BodySkin = "chara:body.skin.item", BodyDetail = "chara:body.detail.item", BodySunburn = "chara:body.sunburn.item";
        public const string BodyNipple = "chara:body.nipple.item", BodyUnderhair = "chara:body.underhair.item";
        public const string FaceHead = "chara:face.head", FaceSkin = "chara:face.skin", FaceDetail = "chara:face.detail.item";
        public const string FaceEyeshadow = "chara:face.makeup.eyeshadow.item", FaceCheek = "chara:face.makeup.cheek.item", FaceLip = "chara:face.makeup.lip.item";
        public const string FaceMole = "chara:face.mole.item", FaceBeard = "chara:face.beard.item", FaceEyebrow = "chara:face.eyebrow.item";
        public const string FaceEyelashes = "chara:face.eyelashes.item", FaceHighlight = "chara:face.eyes.highlight.item";

        /// <summary>Hair part names in HS2 order (back, front, side, option).</summary>
        public static readonly string[] HairNames = { "back", "front", "side", "option" };
        /// <summary>Clothes kind names in HS2 order (`ClothesKind` 0..7).</summary>
        public static readonly string[] ClothesNames = { "top", "bottom", "innerTop", "innerBottom", "gloves", "pantyhose", "socks", "shoes" };

        static readonly string[] facePaint = { "chara:face.makeup.paint[0].item", "chara:face.makeup.paint[1].item" };
        static readonly string[] bodyPaint = { "chara:body.paint[0].item", "chara:body.paint[1].item" };
        static readonly string[] bodyPaintLayout = { "chara:body.paint[0].layoutItem", "chara:body.paint[1].layoutItem" };
        static readonly string[] iris = { "chara:face.eyes.left.iris.item", "chara:face.eyes.right.iris.item" };
        static readonly string[] pupil = { "chara:face.eyes.left.pupil.item", "chara:face.eyes.right.pupil.item" };
        static readonly string[] hair = Build(HairNames, n => "chara:hair." + n + ".item");
        static readonly string[] clothes = Build(ClothesNames, n => "coord:clothes." + n + ".item");
        static readonly string[,] pattern = BuildPatterns();

        public static string FacePaint(int i) => facePaint[i];
        public static string BodyPaint(int i) => bodyPaint[i];
        public static string BodyPaintLayout(int i) => bodyPaintLayout[i];
        /// <summary>Iris of eye <paramref name="eye"/> (0 left = HS2 pupil[0], 1 right).</summary>
        public static string EyeIris(int eye) => iris[eye];
        public static string EyePupil(int eye) => pupil[eye];
        public static string Hair(int k) => hair[k];
        public static string Clothes(int kind) => clothes[kind];
        public static string ClothesPattern(int kind, int j) => pattern[kind, j];
        /// <summary>The accessory of build slot <paramref name="slot"/> (slots 0..19 and the MoreAccessories slots ≥ 20).</summary>
        public static string Accessory(int slot) => AccessoryPrefix + slot.ToString(CultureInfo.InvariantCulture);

        /// <summary>The build slot of an accessory part key, or -1.</summary>
        public static int AccessorySlot(string part) =>
            part != null && part.StartsWith(AccessoryPrefix, System.StringComparison.Ordinal)
            && int.TryParse(part.Substring(AccessoryPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int s) ? s : -1;

        static string[] Build(string[] names, System.Func<string, string> f)
        {
            var r = new string[names.Length];
            for (int i = 0; i < r.Length; i++) r[i] = f(names[i]);
            return r;
        }

        static string[,] BuildPatterns()
        {
            var r = new string[ClothesNames.Length, 3];
            for (int k = 0; k < ClothesNames.Length; k++)
                for (int j = 0; j < 3; j++) r[k, j] = "coord:clothes." + ClothesNames[k] + ".colors[" + j.ToString(CultureInfo.InvariantCulture) + "].pattern";
            return r;
        }
    }
}
