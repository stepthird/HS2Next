// chara.json: character appearance in HS2's raw semantics (normalised sliders,
// sRGB colours as entered, no clamping). Every member starts at its "absent" value = HS2's MemberInit default,
// so a fresh object behaves like a fresh HS2 object. Body: CharaBody.cs; hair: CharaHair.cs; MaterialEditor
// items: MaterialOverride.cs. Categories female / male in the comments (the sex is profile.sex).
using System.Collections.Generic;

namespace HS2Next.Save
{
    public sealed class CharaDocument : DocObject
    {
        public const string EntryName = "chara.json";

        public Profile Profile = new Profile();
        public FaceData Face = new FaceData();
        public BodyData Body = new BodyData();
        public HairData Hair = new HairData();
        /// <summary>MaterialEditor items with ObjectType Character (body and head renderers), in apply order.</summary>
        public List<MaterialOverride> MaterialOverrides = new List<MaterialOverride>();
        public StudioOverlaySwitches StudioOverlays = new StudioOverlaySwitches();
        /// <summary>Inline ext of the chara scope.</summary>
        public ExtInline Ext = new ExtInline();

        /// <summary>Reads a document from a reader over its root object.</summary>
        public static CharaDocument Read(DocReader r)
        {
            var d = new CharaDocument();
            d.ReadFrom(r);
            return d;
        }

        /// <summary>Writes the root object (the caller finishes the document).</summary>
        public void Write(CanonicalJsonWriter w) => WriteTo(w);

        internal override ExtInline InlineExt => Ext;

        internal override void ReadMembers(DocReader r)
        {
            Profile.ReadFrom(r.Obj("profile"));
            Face.ReadFrom(r.Obj("face"));
            Body.ReadFrom(r.Obj("body"));
            Hair.ReadFrom(r.Obj("hair"));
            MaterialOverrides = MaterialOverride.ReadList(r, "materialOverrides");
            StudioOverlays.ReadFrom(r.Obj("studioOverlays"));
            Ext = r.Ext();
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            Profile.WriteTo(w, "profile");
            Face.WriteTo(w, "face");
            Body.WriteTo(w, "body");
            Hair.WriteTo(w, "hair");
            MaterialOverride.WriteList(w, "materialOverrides", MaterialOverrides);
            StudioOverlays.WriteTo(w, "studioOverlays");
        }
    }

    /// <summary>`profile`.</summary>
    public sealed class Profile : DocObject
    {
        /// <summary>Display name (`Parameter.fullname`).</summary>
        public string Name = "";
        /// <summary>0 male, 1 female: selects the build path and the categories of every `ref`.</summary>
        public int Sex;
        /// <summary>Female with male parts (UncensorSelector display).</summary>
        public bool Futanari;
        /// <summary>Imported game parameters, shown read-only and never used; null for characters made in HS2Next.</summary>
        public Hs2Profile Hs2;

        internal override void ReadMembers(DocReader r)
        {
            Name = r.Str("name", Name);
            Sex = r.Int("sex", Sex);
            Futanari = r.Bool("futanari", Futanari);
            Hs2 = ReadNew<Hs2Profile>(r.ObjN("hs2"));
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            ReqStr(w, "name", Name);
            w.Int("sex", Sex);
            w.Bool("futanari", Futanari);
            WriteN(w, "hs2", Hs2);
        }
    }

    /// <summary>`profile.hs2`: imported HS2 game parameters.</summary>
    public sealed class Hs2Profile : DocObject
    {
        public int Personality;
        public int BirthMonth = 1, BirthDay = 1;
        public float VoiceRate = 0.5f;
        /// <summary>`Parameter.hsWish`; written ascending.</summary>
        public List<int> Wishes = new List<int>();
        /// <summary>Null when the card had no `Parameter2` block.</summary>
        public Hs2Parameter2 Parameter2;

        internal override void ReadMembers(DocReader r)
        {
            Personality = r.Int("personality", Personality);
            BirthMonth = r.Int("birthMonth", BirthMonth);
            BirthDay = r.Int("birthDay", BirthDay);
            VoiceRate = r.F32("voiceRate", VoiceRate);
            Wishes = new List<int>(r.IntArray("wishes", Wishes.ToArray()));
            Parameter2 = ReadNew<Hs2Parameter2>(r.ObjN("parameter2"));
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Int("personality", Personality);
            w.Int("birthMonth", BirthMonth);
            w.Int("birthDay", BirthDay);
            w.F32("voiceRate", VoiceRate);
            var wishes = Wishes.ToArray();
            System.Array.Sort(wishes);
            w.IntArray("wishes", wishes);
            WriteN(w, "parameter2", Parameter2);
        }
    }

    /// <summary>`profile.hs2.parameter2`.</summary>
    public sealed class Hs2Parameter2 : DocObject
    {
        public int Personality;
        public float VoiceRate = 0.5f;
        public int Trait, Mind, HAttribute;

        internal override void ReadMembers(DocReader r)
        {
            Personality = r.Int("personality", Personality);
            VoiceRate = r.F32("voiceRate", VoiceRate);
            Trait = r.Int("trait", Trait);
            Mind = r.Int("mind", Mind);
            HAttribute = r.Int("hAttribute", HAttribute);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Int("personality", Personality);
            w.F32("voiceRate", VoiceRate);
            w.Int("trait", Trait);
            w.Int("mind", Mind);
            w.Int("hAttribute", HAttribute);
        }
    }

    /// <summary>`face`.</summary>
    public sealed class FaceData : DocObject
    {
        public const int ShapeCount = 59;

        /// <summary>210 / 110.</summary>
        public string Head = DocDefaults.NoneRef;
        /// <summary>211 / 111 (a face skin row is tied to a head by its HeadID column).</summary>
        public string Skin = DocDefaults.NoneRef;
        /// <summary>Item 212 / 112, power 1.0.</summary>
        public ItemPower Detail = new ItemPower(1f);
        /// <summary>Face sliders in FaceShapeIdx order; length as read (59 on every card).</summary>
        public float[] Shape = DocDefaults.Fill(ShapeCount, 0.5f);
        /// <summary>Item 314.</summary>
        public ItemLayout Eyebrow = new ItemLayout(new Color4(0.05f, 0.05f, 0.05f, 1f), new Vec4(0.5f, 0.375f, 0.666f, 0.666f), true);
        public EyeData Eyes = new EyeData();
        /// <summary>Item 315.</summary>
        public ItemColor Eyelashes = new ItemColor(new Color4(0.19f, 0.19f, 0.19f, 1f));
        /// <summary>Item 323.</summary>
        public ItemLayout Mole = new ItemLayout(new Color4(0f, 0f, 0f, 1f), new Vec4(0.5f, 0.5f, 0.25f, 0.5f), false);
        public MakeupData Makeup = new MakeupData();
        /// <summary>Item 121 (the male category is used for both sexes); applied to males only, kept as read for females.</summary>
        public ItemColor Beard = new ItemColor(new Color4(0.19f, 0.19f, 0.19f, 1f));

        internal override void ReadMembers(DocReader r)
        {
            Head = r.Ref("head", Head);
            Skin = r.Ref("skin", Skin);
            Detail.ReadFrom(r.Obj("detail"));
            Shape = r.F32Array("shape", Shape);
            Eyebrow.ReadFrom(r.Obj("eyebrow"));
            Eyes.ReadFrom(r.Obj("eyes"));
            Eyelashes.ReadFrom(r.Obj("eyelashes"));
            Mole.ReadFrom(r.Obj("mole"));
            Makeup.ReadFrom(r.Obj("makeup"));
            Beard.ReadFrom(r.Obj("beard"));
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            ReqRef(w, "head", Head);
            ReqRef(w, "skin", Skin);
            Detail.WriteTo(w, "detail");
            F32s(w, "shape", Shape);
            Eyebrow.WriteTo(w, "eyebrow");
            Eyes.WriteTo(w, "eyes");
            Eyelashes.WriteTo(w, "eyelashes");
            Mole.WriteTo(w, "mole");
            Makeup.WriteTo(w, "makeup");
            Beard.WriteTo(w, "beard");
        }
    }

    /// <summary>`face.eyes`.</summary>
    public sealed class EyeData : DocObject
    {
        /// <summary>Maker "edit both eyes together" (`pupilSameSetting`).</summary>
        public bool Linked = true;
        /// <summary>Vertical offset of the eye textures (`pupilY`).</summary>
        public float OffsetY = 0.5f;
        /// <summary>`whiteShadowScale`.</summary>
        public float ShadowScale = 0.5f;
        /// <summary>HS2 `pupil[0]` (o_eyebase_L).</summary>
        public Eye Left = new Eye();
        /// <summary>HS2 `pupil[1]`.</summary>
        public Eye Right = new Eye();
        /// <summary>Item 319.</summary>
        public ItemLayout Highlight = new ItemLayout(Color4.White, new Vec4(0.5f, 0.5f, 0.5f, 0.5f), true);

        internal override void ReadMembers(DocReader r)
        {
            Linked = r.Bool("linked", Linked);
            OffsetY = r.F32("offsetY", OffsetY);
            ShadowScale = r.F32("shadowScale", ShadowScale);
            Left.ReadFrom(r.Obj("left"));
            Right.ReadFrom(r.Obj("right"));
            Highlight.ReadFrom(r.Obj("highlight"));
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Bool("linked", Linked);
            w.F32("offsetY", OffsetY);
            w.F32("shadowScale", ShadowScale);
            Left.WriteTo(w, "left");
            Right.WriteTo(w, "right");
            Highlight.WriteTo(w, "highlight");
        }
    }

    /// <summary>One eye of `face.eyes`.</summary>
    public sealed class Eye : DocObject
    {
        public Color4 White = new Color4(0.846f, 0.846f, 0.846f, 1f);
        public EyeIris Iris = new EyeIris();
        public EyePupil Pupil = new EyePupil();

        internal override void ReadMembers(DocReader r)
        {
            White = r.Col("white", White);
            Iris.ReadFrom(r.Obj("iris"));
            Pupil.ReadFrom(r.Obj("pupil"));
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Col("white", White);
            Iris.WriteTo(w, "iris");
            Pupil.WriteTo(w, "pupil");
        }
    }

    /// <summary>`iris` of an eye: item 317.</summary>
    public sealed class EyeIris : DocObject
    {
        public string Item = DocDefaults.NoneRef;
        public Color4 Color = new Color4(0.33f, 0.33f, 0.33f, 1f);
        public Vec2 Size = new Vec2(0.666f, 0.666f);
        public float Emission;

        internal override void ReadMembers(DocReader r)
        {
            Item = r.Ref("item", Item);
            Color = r.Col("color", Color);
            Size = r.V2("size", Size);
            Emission = r.F32("emission", Emission);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            ReqRef(w, "item", Item);
            w.Col("color", Color);
            w.V2("size", Size);
            w.F32("emission", Emission);
        }
    }

    /// <summary>`pupil` of an eye: item 318.</summary>
    public sealed class EyePupil : DocObject
    {
        public string Item = DocDefaults.NoneRef;
        public Color4 Color = new Color4(0f, 0f, 0f, 1f);
        public Vec2 Size = new Vec2(0.8333f, 0.8333f);

        internal override void ReadMembers(DocReader r)
        {
            Item = r.Ref("item", Item);
            Color = r.Col("color", Color);
            Size = r.V2("size", Size);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            ReqRef(w, "item", Item);
            w.Col("color", Color);
            w.V2("size", Size);
        }
    }

    /// <summary>`face.makeup`. The gloss members have no MemberInit value in HS2 and default to 0.</summary>
    public sealed class MakeupData : DocObject
    {
        public const int PaintCount = 2;

        /// <summary>Item 316.</summary>
        public MakeupLayer Eyeshadow = new MakeupLayer();
        /// <summary>Item 320.</summary>
        public MakeupLayer Cheek = new MakeupLayer();
        /// <summary>Item 322.</summary>
        public MakeupLayer Lip = new MakeupLayer();
        /// <summary>Exactly 2; `layoutItem` category 7 (always 0 on the cards; not applied).</summary>
        public Paint[] Paint = { new Paint(), new Paint() };

        internal override void ReadMembers(DocReader r)
        {
            Eyeshadow.ReadFrom(r.Obj("eyeshadow"));
            Cheek.ReadFrom(r.Obj("cheek"));
            Lip.ReadFrom(r.Obj("lip"));
            ReadFixed(r, "paint", Paint, i => new Paint());
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            Eyeshadow.WriteTo(w, "eyeshadow");
            Cheek.WriteTo(w, "cheek");
            Lip.WriteTo(w, "lip");
            WriteFixed(w, "paint", Paint, PaintCount);
        }
    }

    /// <summary>`{ item, color, gloss }` of `face.makeup`.</summary>
    public sealed class MakeupLayer : DocObject
    {
        public string Item = DocDefaults.NoneRef;
        public Color4 Color = Color4.White;
        public float Gloss;

        internal override void ReadMembers(DocReader r)
        {
            Item = r.Ref("item", Item);
            Color = r.Col("color", Color);
            Gloss = r.F32("gloss", Gloss);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            ReqRef(w, "item", Item);
            w.Col("color", Color);
            w.F32("gloss", Gloss);
        }
    }

    /// <summary>`Paint`: face makeup and body paint. Item 313; `layoutItem` body 8 / face 7.</summary>
    public sealed class Paint : DocObject
    {
        public string Item = DocDefaults.NoneRef;
        public Color4 Color = new Color4(1f, 0f, 0f, 1f);
        public float Gloss = 0.5f, Metallic = 0.5f;
        public string LayoutItem = DocDefaults.NoneRef;
        /// <summary>Scale x/y, offset x/y.</summary>
        public Vec4 Layout = new Vec4(0.5f, 0.5f, 0.5f, 0.5f);
        public float Rotation = 0.5f;

        internal override void ReadMembers(DocReader r)
        {
            Item = r.Ref("item", Item);
            Color = r.Col("color", Color);
            Gloss = r.F32("gloss", Gloss);
            Metallic = r.F32("metallic", Metallic);
            LayoutItem = r.Ref("layoutItem", LayoutItem);
            Layout = r.V4("layout", Layout);
            Rotation = r.F32("rotation", Rotation);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            ReqRef(w, "item", Item);
            w.Col("color", Color);
            w.F32("gloss", Gloss);
            w.F32("metallic", Metallic);
            ReqRef(w, "layoutItem", LayoutItem);
            w.V4("layout", Layout);
            w.F32("rotation", Rotation);
        }
    }

    /// <summary>`{ item, power }` (face and body detail; the power default differs: face 1.0, body 0.5).</summary>
    public sealed class ItemPower : DocObject
    {
        public string Item = DocDefaults.NoneRef;
        public float Power;
        public ItemPower(float power) { Power = power; }

        internal override void ReadMembers(DocReader r)
        {
            Item = r.Ref("item", Item);
            Power = r.F32("power", Power);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            ReqRef(w, "item", Item);
            w.F32("power", Power);
        }
    }

    /// <summary>`{ item, color }` (eyelashes, beard, sunburn, underhair).</summary>
    public sealed class ItemColor : DocObject
    {
        public string Item = DocDefaults.NoneRef;
        public Color4 Color;
        public ItemColor(Color4 color) { Color = color; }

        internal override void ReadMembers(DocReader r)
        {
            Item = r.Ref("item", Item);
            Color = r.Col("color", Color);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            ReqRef(w, "item", Item);
            w.Col("color", Color);
        }
    }

    /// <summary>`{ item, color, layout[, tilt] }`: eyebrow and eye highlight have `tilt`, the mole has not.</summary>
    public sealed class ItemLayout : DocObject
    {
        public string Item = DocDefaults.NoneRef;
        public Color4 Color;
        public Vec4 Layout;
        /// <summary>Only meaningful (and only written) when <see cref="HasTilt"/>.</summary>
        public float Tilt = 0.5f;
        /// <summary>Schema shape, fixed by the member's place: true for eyebrow and highlight.</summary>
        public readonly bool HasTilt;
        public ItemLayout(Color4 color, Vec4 layout, bool hasTilt) { Color = color; Layout = layout; HasTilt = hasTilt; }

        internal override void ReadMembers(DocReader r)
        {
            Item = r.Ref("item", Item);
            Color = r.Col("color", Color);
            Layout = r.V4("layout", Layout);
            if (HasTilt) Tilt = r.F32("tilt", Tilt);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            ReqRef(w, "item", Item);
            w.Col("color", Color);
            w.V4("layout", Layout);
            if (HasTilt) w.F32("tilt", Tilt);
        }
    }

    /// <summary>`studioOverlays`: KSOX EnableInStudioSkin / EnableInStudioIris, KCOX EnableInStudio.</summary>
    public sealed class StudioOverlaySwitches : DocObject
    {
        public bool Skin = true, Eyes = true, Clothes = true;

        internal override void ReadMembers(DocReader r)
        {
            Skin = r.Bool("skin", Skin);
            Eyes = r.Bool("eyes", Eyes);
            Clothes = r.Bool("clothes", Clothes);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Bool("skin", Skin);
            w.Bool("eyes", Eyes);
            w.Bool("clothes", Clothes);
        }
    }
}
