// coord.json: one outfit — clothes (the parts' inline ext of
// format 2), accessories (CoordAccessory.cs) and the AdditionalAccessoryControls coordinate rules. The same
// schema serves a .hs2ncoord root and the worn coordinate of a character (coord/coord.json).
using System.Collections.Generic;

namespace HS2Next.Save
{
    public sealed class CoordDocument : DocObject
    {
        public const string EntryName = "coord.json";

        /// <summary>`coordinateName`; "" for the coordinate of an imported character card.</summary>
        public string Name = "";
        /// <summary>The sex the coordinate was authored for (0 male, 1 female): decides the categories of `clothes.*.item`.</summary>
        public int Sex = 1;
        public ClothesSet Clothes = new ClothesSet();
        /// <summary>One list in slot order (vanilla 0..19 and MoreAccessories ≥ 20 merged; empty slots not stored).</summary>
        public List<AccessoryEntry> Accessories = new List<AccessoryEntry>();
        /// <summary>AdditionalAccessoryControls coordinate rules; null = none.</summary>
        public AacRuleOverrides RuleOverrides;
        /// <summary>Inline ext of the coord scope.</summary>
        public ExtInline Ext = new ExtInline();

        /// <summary>Reads a document from a reader over its root object. A duplicate accessory id gets a new id on the
        /// later entry (reported).</summary>
        public static CoordDocument Read(DocReader r)
        {
            var d = new CoordDocument();
            d.ReadFrom(r);
            return d;
        }

        /// <summary>Writes the root object (the caller finishes the document).</summary>
        public void Write(CanonicalJsonWriter w) => WriteTo(w);

        internal override ExtInline InlineExt => Ext;

        internal override void ReadMembers(DocReader r)
        {
            Name = r.Str("name", Name);
            Sex = r.Int("sex", Sex);
            Clothes.ReadFrom(r.Obj("clothes"));
            Accessories = ReadList<AccessoryEntry>(r, "accessories");
            RuleOverrides = ReadNew<AacRuleOverrides>(r.ObjN("ruleOverrides"));
            Ext = r.Ext();

            // A later duplicate gets an id derived from the duplicated id and its list index, so reading the same
            // file twice gives the same document; an id already in the list is never produced.
            var taken = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (var a in Accessories) taken.Add(a.Id);
            var seen = new HashSet<string>(System.StringComparer.Ordinal);
            for (int i = 0; i < Accessories.Count; i++)
            {
                var a = Accessories[i];
                if (seen.Add(a.Id)) continue;
                string old = a.Id;
                a.Id = DuplicateId(old, i, taken);
                taken.Add(a.Id);
                seen.Add(a.Id);
                r.Report(FindingSeverity.Warning, "coord.duplicateAccessoryId", "accessories[" + i + "].id", "duplicate accessory id " + old + " replaced by " + a.Id);
            }
        }

        /// <summary>
        /// The id a reader gives the duplicate accessory at list index <paramref name="index"/>:
        /// <c>JsonText.DerivedId("&lt;old id&gt;/dup/&lt;index&gt;")</c>; should that clash with an id in
        /// <paramref name="taken"/>, "/&lt;n&gt;" is appended to the text (n = 1, 2, …) until it does not.
        /// </summary>
        public static string DuplicateId(string oldId, int index, ICollection<string> taken = null)
        {
            string id = JsonText.DerivedId(oldId + "/dup/" + index);
            for (int n = 1; taken != null && taken.Contains(id); n++)
                id = JsonText.DerivedId(oldId + "/dup/" + index + "/" + n);
            return id;
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            ReqStr(w, "name", Name);
            w.Int("sex", Sex);
            Clothes.WriteTo(w, "clothes");
            WriteList(w, "accessories", Accessories);
            WriteN(w, "ruleOverrides", RuleOverrides);
        }

        /// <summary>The accessory with id <paramref name="id"/>, or null.</summary>
        public AccessoryEntry FindAccessory(string id) => Accessories.Find(a => a.Id == id);
    }

    /// <summary>ClothesKind 0..7.</summary>
    public enum ClothesKind { Top, Bottom, InnerTop, InnerBottom, Gloves, Pantyhose, Socks, Shoes }

    /// <summary>`clothes`: one ClothesPart per ClothesKind.</summary>
    public sealed class ClothesSet : DocObject
    {
        public const int Count = 8;
        /// <summary>JSON member names in ClothesKind order.</summary>
        public static readonly string[] Names = { "top", "bottom", "innerTop", "innerBottom", "gloves", "pantyhose", "socks", "shoes" };

        /// <summary>The ClothesKind index of a member name (ordinal), or -1.</summary>
        public static int IndexOf(string name)
        {
            for (int k = 0; k < Names.Length; k++) if (string.Equals(Names[k], name, System.StringComparison.Ordinal)) return k;
            return -1;
        }

        /// <summary>True when any part carries inline ext (the file then needs format 2).</summary>
        public bool HasPartExt()
        {
            foreach (var p in Parts) if (p != null && p.Ext != null && !p.Ext.IsEmpty) return true;
            return false;
        }
        /// <summary>Categories per kind for female / male coordinates (males use the female inner, pantyhose and socks lists).</summary>
        public static readonly int[] FemaleCategories = { 240, 241, 242, 243, 244, 245, 246, 247 };
        public static readonly int[] MaleCategories = { 140, 141, 242, 243, 144, 245, 246, 147 };

        /// <summary>Indexed by ClothesKind.</summary>
        public readonly ClothesPart[] Parts = { new ClothesPart(ClothesKind.Top), new ClothesPart(ClothesKind.Bottom),
            new ClothesPart(ClothesKind.InnerTop), new ClothesPart(ClothesKind.InnerBottom), new ClothesPart(ClothesKind.Gloves),
            new ClothesPart(ClothesKind.Pantyhose), new ClothesPart(ClothesKind.Socks), new ClothesPart(ClothesKind.Shoes) };

        public ClothesPart this[ClothesKind kind] { get => Parts[(int)kind]; set => Parts[(int)kind] = value; }

        internal override void ReadMembers(DocReader r)
        {
            for (int k = 0; k < Count; k++) Parts[k].ReadFrom(r.Obj(Names[k]));
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            for (int k = 0; k < Count; k++) Parts[k].WriteTo(w, Names[k]);
        }
    }

    /// <summary>`ClothesPart`.</summary>
    public sealed class ClothesPart : DocObject
    {
        public const int ColorCount = 3;

        /// <summary>Category by kind and `coord.sex`; id 0 is the "none" row.</summary>
        public string Item = DocDefaults.NoneRef;
        /// <summary>Exactly 3 colour areas.</summary>
        public ClothesColor[] Colors = { new ClothesColor(), new ClothesColor(), new ClothesColor() };
        /// <summary>Torn-clothes mask amount.</summary>
        public float BreakRate;
        /// <summary>Exactly 2: hide option parts objOpt01/02.</summary>
        public bool[] HideOptions = { false, false };
        /// <summary>KCOX overlays; null = none.</summary>
        public ClothesOverlaySet Overlays;
        /// <summary>MaterialEditor items with ObjectType Clothing and Slot = kind.</summary>
        public List<MaterialOverride> MaterialOverrides = new List<MaterialOverride>();
        /// <summary>Only `top` (plugin "Top" set) and `innerTop` ("Bra" set) carry it; null = none.</summary>
        public PushUpSettings PushUp;
        /// <summary>Part-level inline ext of the coord scope (plugin data of this clothes slot, addressed by the kind name);
        /// written last, only when non-empty. It travels with the part like its typed sections.</summary>
        public ExtInline Ext = new ExtInline();

        /// <summary>The kind of the slot (decides the absent values of <see cref="PushUp"/>); not stored.</summary>
        public readonly ClothesKind Kind;

        public ClothesPart(ClothesKind kind) { Kind = kind; }

        internal override ExtInline InlineExt => Ext;

        internal override void ReadMembers(DocReader r)
        {
            Item = r.Ref("item", Item);
            ReadFixed(r, "colors", Colors, i => new ClothesColor());
            BreakRate = r.F32("breakRate", BreakRate);
            HideOptions = r.BoolArray("hideOptions", HideOptions, 2);
            Overlays = ReadNew<ClothesOverlaySet>(r.ObjN("overlays"));
            MaterialOverrides = MaterialOverride.ReadList(r, "materialOverrides");
            var p = r.ObjN("pushUp");
            if (p == null) PushUp = null;
            else
            {
                PushUp = PushUpSettings.DefaultsFor(Kind);
                PushUp.ReadFrom(p);
            }
            Ext = r.Ext();
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            ReqRef(w, "item", Item);
            WriteFixed(w, "colors", Colors, ColorCount);
            w.F32("breakRate", BreakRate);
            Bools(w, "hideOptions", HideOptions, 2);
            WriteN(w, "overlays", Overlays);
            MaterialOverride.WriteList(w, "materialOverrides", MaterialOverrides);
            WriteN(w, "pushUp", PushUp);
        }
    }

    /// <summary>`ClothesColor`: one colour area. The alpha of <see cref="Base"/> is used only when the prefab allows it.</summary>
    public sealed class ClothesColor : DocObject
    {
        public Color4 Base = Color4.White;
        /// <summary>Category 348; `hs2.kcox#58947543` takes its image from the overlay `patterns[k]`.</summary>
        public string Pattern = DocDefaults.NoneRef;
        public Color4 PatternColor = Color4.White;
        public Vec4 Layout = new Vec4(1f, 1f, 0f, 0f);
        public float Rotation = 0.5f, Gloss = 0.5f, Metallic;

        internal override void ReadMembers(DocReader r)
        {
            Base = r.Col("base", Base);
            Pattern = r.Ref("pattern", Pattern);
            PatternColor = r.Col("patternColor", PatternColor);
            Layout = r.V4("layout", Layout);
            Rotation = r.F32("rotation", Rotation);
            Gloss = r.F32("gloss", Gloss);
            Metallic = r.F32("metallic", Metallic);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Col("base", Base);
            ReqRef(w, "pattern", Pattern);
            w.Col("patternColor", PatternColor);
            w.V4("layout", Layout);
            w.F32("rotation", Rotation);
            w.F32("gloss", Gloss);
            w.F32("metallic", Metallic);
        }
    }

    /// <summary>`overlays` (KCOX) of one clothes kind.</summary>
    public sealed class ClothesOverlaySet : DocObject
    {
        public const int PatternCount = 3;

        public OverlayEntry Main, ColorMask;
        /// <summary>Exactly 3 (nullable entries).</summary>
        public OverlayEntry[] Patterns = new OverlayEntry[PatternCount];
        public OverlayEntry SourceReplacement;
        /// <summary>`TextureSizeOverride`; null = none.</summary>
        public int? TextureSize;

        internal override void ReadMembers(DocReader r)
        {
            Main = ReadNew<OverlayEntry>(r.ObjN("main"));
            ColorMask = ReadNew<OverlayEntry>(r.ObjN("colorMask"));
            var p = r.Array("patterns", v => v.AsObjN(), null, PatternCount);
            if (p.Count == PatternCount)
                for (int i = 0; i < PatternCount; i++) Patterns[i] = ReadNew<OverlayEntry>(p[i]);
            SourceReplacement = ReadNew<OverlayEntry>(r.ObjN("sourceReplacement"));
            TextureSize = r.IntN("textureSize");
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            OverlayEntry.Write(w, "main", Main);
            OverlayEntry.Write(w, "colorMask", ColorMask);
            w.Name("patterns");
            CheckLength(w, Patterns, PatternCount);
            w.StartArray();
            foreach (var o in Patterns) OverlayEntry.Write(w, null, o);
            w.EndArray();
            OverlayEntry.Write(w, "sourceReplacement", SourceReplacement);
            w.IntN("textureSize", TextureSize);
        }
    }

    /// <summary>`Ov`. An entry with image null and clearTarget true clears the texture and MUST be kept; an
    /// entry with both empty is never written (null instead).</summary>
    public sealed class OverlayEntry : DocObject
    {
        /// <summary>`res/…` entry or null.</summary>
        public string Image;
        /// <summary>KCOX `Override`: clear the target texture to transparent before drawing.</summary>
        public bool ClearTarget;
        /// <summary>`BlendingMode`: 0 Default, 1 LinearAlpha.</summary>
        public int Blend;

        /// <summary>True when the entry has neither an image nor clearTarget (and no unknown members): written as null.</summary>
        public bool IsEmpty => Image == null && !ClearTarget && (Unknown == null || Unknown.IsEmpty);

        internal override void ReadMembers(DocReader r)
        {
            Image = r.ResN("image");
            ClearTarget = r.Bool("clearTarget", ClearTarget);
            Blend = r.Int("blend", Blend);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Res("image", Image);
            w.Bool("clearTarget", ClearTarget);
            w.Int("blend", Blend);
        }

        /// <summary>Writes an `Ov?` (a member when <paramref name="name"/> is set, else an array element).</summary>
        internal static void Write(CanonicalJsonWriter w, string name, OverlayEntry o)
        {
            if (name != null) w.Name(name);
            if (o == null || o.IsEmpty) w.Null();
            else o.WriteTo(w);
        }
    }

    /// <summary>`pushUp`. Absent members take the plugin defaults of the set: Bra (inner top) or Top.</summary>
    public sealed class PushUpSettings : DocObject
    {
        public bool Enabled;
        public float Firmness, Lift, PushTogether, Squeeze, CenterNipples, Corset;
        public bool FlattenNipples, HideNipples, HideAccessories, CorsetHalf;

        /// <summary>Plugin defaults of the inner-top ("Bra") set.</summary>
        public static PushUpSettings BraDefaults() => new PushUpSettings
        {
            Enabled = true, Firmness = 90 / 100f, Lift = 60 / 100f, PushTogether = 65 / 100f, Squeeze = 60 / 100f, CenterNipples = 100 / 100f,
            FlattenNipples = true,
        };

        /// <summary>Plugin defaults of the top ("Top") set: all 0 / false.</summary>
        public static PushUpSettings TopDefaults() => new PushUpSettings();

        /// <summary>The defaults for a clothes kind (the Bra set for the inner top, else the Top set).</summary>
        public static PushUpSettings DefaultsFor(ClothesKind kind) => kind == ClothesKind.InnerTop ? BraDefaults() : TopDefaults();

        internal override void ReadMembers(DocReader r)
        {
            Enabled = r.Bool("enabled", Enabled);
            Firmness = r.F32("firmness", Firmness);
            Lift = r.F32("lift", Lift);
            PushTogether = r.F32("pushTogether", PushTogether);
            Squeeze = r.F32("squeeze", Squeeze);
            CenterNipples = r.F32("centerNipples", CenterNipples);
            Corset = r.F32("corset", Corset);
            FlattenNipples = r.Bool("flattenNipples", FlattenNipples);
            HideNipples = r.Bool("hideNipples", HideNipples);
            HideAccessories = r.Bool("hideAccessories", HideAccessories);
            CorsetHalf = r.Bool("corsetHalf", CorsetHalf);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Bool("enabled", Enabled);
            w.F32("firmness", Firmness);
            w.F32("lift", Lift);
            w.F32("pushTogether", PushTogether);
            w.F32("squeeze", Squeeze);
            w.F32("centerNipples", CenterNipples);
            w.F32("corset", Corset);
            w.Bool("flattenNipples", FlattenNipples);
            w.Bool("hideNipples", HideNipples);
            w.Bool("hideAccessories", HideAccessories);
            w.Bool("corsetHalf", CorsetHalf);
        }
    }
}
