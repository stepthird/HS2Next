// chara.json `body`: sliders, skin, paints, nipples, nails, and the typed
// plugin sections UncensorSelector, KSOX and ABMX body modifiers.
using System.Collections.Generic;

namespace HS2Next.Save
{
    /// <summary>`body`. Categories female / male in the comments.</summary>
    public sealed class BodyData : DocObject
    {
        public const int ShapeCount = 33;

        /// <summary>BodyShapeIdx order; index 32 (NipStand) applies as Lerp(0, 0.8, v); length as read (33).</summary>
        public float[] Shape = DefaultShape();
        public BodyBust Bust = new BodyBust();
        /// <summary>Item 231 / 131; colour and gloss also drive the face.</summary>
        public BodySkin Skin = new BodySkin();
        /// <summary>Item 232 / 132, power 0.5.</summary>
        public ItemPower Detail = new ItemPower(0.5f);
        /// <summary>Item 233 / 133.</summary>
        public ItemColor Sunburn = new ItemColor(Color4.White);
        /// <summary>Exactly 2; `layoutItem` category 8.</summary>
        public Paint[] Paint = { new Paint(), new Paint() };
        /// <summary>Item 334.</summary>
        public BodyNipple Nipple = new BodyNipple();
        /// <summary>Item 335.</summary>
        public ItemColor Underhair = new ItemColor(new Color4(0.05f, 0.05f, 0.05f, 1f));
        public BodyNail Nail = new BodyNail();
        /// <summary>KK_InvisibleBody: false hides body, head and hair renderers (clothes and accessories stay).</summary>
        public bool Visible = true;
        /// <summary>UncensorSelector; null = none stored.</summary>
        public UncensorData Uncensor;
        /// <summary>KSOX layers; at most one per target, written in TexType value order (unknown names last, ordinal).</summary>
        public List<SkinOverlay> SkinOverlays = new List<SkinOverlay>();
        /// <summary>ABMX modifiers not aimed at an accessory.</summary>
        public List<BodyBoneMod> BoneMods = new List<BodyBoneMod>();

        public static float[] DefaultShape()
        {
            var a = DocDefaults.Fill(ShapeCount, 0.5f);
            a[ShapeCount - 1] = 0f;
            return a;
        }

        internal override void ReadMembers(DocReader r)
        {
            Shape = r.F32Array("shape", Shape);
            Bust.ReadFrom(r.Obj("bust"));
            Skin.ReadFrom(r.Obj("skin"));
            Detail.ReadFrom(r.Obj("detail"));
            Sunburn.ReadFrom(r.Obj("sunburn"));
            ReadFixed(r, "paint", Paint, i => new Paint());
            Nipple.ReadFrom(r.Obj("nipple"));
            Underhair.ReadFrom(r.Obj("underhair"));
            Nail.ReadFrom(r.Obj("nail"));
            Visible = r.Bool("visible", Visible);
            Uncensor = ReadNew<UncensorData>(r.ObjN("uncensor"));
            SkinOverlays = ReadList<SkinOverlay>(r, "skinOverlays");
            BoneMods = ReadList<BodyBoneMod>(r, "boneMods");
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            F32s(w, "shape", Shape);
            Bust.WriteTo(w, "bust");
            Skin.WriteTo(w, "skin");
            Detail.WriteTo(w, "detail");
            Sunburn.WriteTo(w, "sunburn");
            WriteFixed(w, "paint", Paint, 2);
            Nipple.WriteTo(w, "nipple");
            Underhair.WriteTo(w, "underhair");
            Nail.WriteTo(w, "nail");
            w.Bool("visible", Visible);
            WriteN(w, "uncensor", Uncensor);
            WriteList(w, "skinOverlays", SkinOverlay.InWriteOrder(SkinOverlays));
            WriteList(w, "boneMods", BoneMods);
        }
    }

    /// <summary>`body.bust`: bust physics.</summary>
    public sealed class BodyBust : DocObject
    {
        public float Softness = 0.5f, Weight = 0.5f;

        internal override void ReadMembers(DocReader r)
        {
            Softness = r.F32("softness", Softness);
            Weight = r.F32("weight", Weight);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.F32("softness", Softness);
            w.F32("weight", Weight);
        }
    }

    /// <summary>`body.skin`.</summary>
    public sealed class BodySkin : DocObject
    {
        public string Item = DocDefaults.NoneRef;
        public Color4 Color = new Color4(0.8f, 0.7f, 0.64f, 1f);
        public float Gloss = 0.7f, Metallic;

        internal override void ReadMembers(DocReader r)
        {
            Item = r.Ref("item", Item);
            Color = r.Col("color", Color);
            Gloss = r.F32("gloss", Gloss);
            Metallic = r.F32("metallic", Metallic);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            ReqRef(w, "item", Item);
            w.Col("color", Color);
            w.F32("gloss", Gloss);
            w.F32("metallic", Metallic);
        }
    }

    /// <summary>`body.nipple`.</summary>
    public sealed class BodyNipple : DocObject
    {
        public string Item = DocDefaults.NoneRef;
        public Color4 Color = new Color4(0.76f, 0.52f, 0.52f, 1f);
        public float Gloss = 0.6f, Areola = 0.7f;

        internal override void ReadMembers(DocReader r)
        {
            Item = r.Ref("item", Item);
            Color = r.Col("color", Color);
            Gloss = r.F32("gloss", Gloss);
            Areola = r.F32("areola", Areola);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            ReqRef(w, "item", Item);
            w.Col("color", Color);
            w.F32("gloss", Gloss);
            w.F32("areola", Areola);
        }
    }

    /// <summary>`body.nail`.</summary>
    public sealed class BodyNail : DocObject
    {
        public Color4 Color = new Color4(1f, 0.92f, 0.92f, 1f);
        public float Gloss = 0.6f;

        internal override void ReadMembers(DocReader r)
        {
            Color = r.Col("color", Color);
            Gloss = r.F32("gloss", Gloss);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Col("color", Color);
            w.F32("gloss", Gloss);
        }
    }

    /// <summary>`body.uncensor`: UncensorSelector GUIDs from zipmod manifests; null = the configured default.
    /// Kept when not installed.</summary>
    public sealed class UncensorData : DocObject
    {
        public string Body, Penis, Balls;
        /// <summary>Null = sex default (male true, female false).</summary>
        public bool? DisplayBalls;
        /// <summary>Version-1 combined `UncensorGUID`, resolved through manifest migration entries at run time.</summary>
        public string Legacy;

        internal override void ReadMembers(DocReader r)
        {
            Body = r.StrN("body");
            Penis = r.StrN("penis");
            Balls = r.StrN("balls");
            DisplayBalls = r.BoolN("displayBalls");
            Legacy = r.StrN("legacy");
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Str("body", Body);
            w.Str("penis", Penis);
            w.Str("balls", Balls);
            w.BoolN("displayBalls", DisplayBalls);
            w.Str("legacy", Legacy);
        }
    }

    /// <summary>KSOX `TexType` names that `SkinOverlay.target` may hold, with their TexType values (write order).</summary>
    public enum SkinOverlayTarget
    {
        BodyOver = 1, FaceOver = 2, BodyUnder = 3, FaceUnder = 4, EyeUnderL = 7, EyeOverL = 8, EyeUnderR = 9, EyeOverR = 10,
        EyebrowUnder = 20, EyelineUnder = 30, BodyDetailOver = 41, FaceDetailOver = 42, BodyDetailUnder = 43, FaceDetailUnder = 44,
    }

    /// <summary>`SkinOverlay` (KSOX).</summary>
    public sealed class SkinOverlay : DocObject
    {
        /// <summary>TexType name (enum string; an unknown name is kept as read). Required.</summary>
        public string Target;
        /// <summary>`res/&lt;sha256&gt;.png`. Required.</summary>
        public string Image;

        /// <summary>Typed view of <see cref="Target"/>; null for an unknown name.</summary>
        public SkinOverlayTarget? TargetType
        {
            get => DocEnum.Parse<SkinOverlayTarget>(Target);
            set => Target = value?.ToString();
        }

        internal override void ReadMembers(DocReader r)
        {
            Target = r.Str("target");
            if (DocEnum.Parse<SkinOverlayTarget>(Target) == null)
                r.Report(FindingSeverity.Warning, "json.unknownEnum", "target", "unknown value \"" + Target + "\" kept");
            Image = r.Res("image");
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            ReqStr(w, "target", Target);
            ReqRes(w, "image", Image);
        }

        /// <summary>Stable order by TexType value; unknown names after the known ones, ordinal.</summary>
        internal static List<SkinOverlay> InWriteOrder(List<SkinOverlay> list)
        {
            var sorted = new List<SkinOverlay>(list);
            var index = new Dictionary<SkinOverlay, int>();
            for (int i = 0; i < sorted.Count; i++) index[sorted[i]] = i;
            sorted.Sort((a, b) =>
            {
                var ta = a.TargetType;
                var tb = b.TargetType;
                int c;
                if (ta.HasValue && tb.HasValue) c = ((int)ta.Value).CompareTo((int)tb.Value);
                else if (ta.HasValue != tb.HasValue) c = ta.HasValue ? -1 : 1;
                else c = string.CompareOrdinal(a.Target, b.Target);
                return c != 0 ? c : index[a].CompareTo(index[b]);
            });
            return sorted;
        }
    }

    /// <summary>ABMX `location` of a body modifier.</summary>
    public enum BoneModLocation { Unknown, BodyTop }

    /// <summary>ABMX modifier fields shared by body and accessory modifiers.</summary>
    public abstract class BoneModBase : DocObject
    {
        /// <summary>Required.</summary>
        public string Bone;
        public Vec3 Scale = Vec3.One;
        public float Length = 1f;
        public Vec3 Position = Vec3.Zero;
        /// <summary>Euler degrees.</summary>
        public Vec3 Rotation = Vec3.Zero;

        protected void ReadValues(DocReader r)
        {
            Scale = r.V3("scale", Scale);
            Length = r.F32("length", Length);
            Position = r.V3("position", Position);
            Rotation = r.Euler("rotation", Rotation);
        }

        protected void WriteValues(CanonicalJsonWriter w)
        {
            w.V3("scale", Scale);
            w.F32("length", Length);
            w.V3("position", Position);
            w.Euler("rotation", Rotation);
        }
    }

    /// <summary>`BodyBoneMod`: `location` `unknown` (ABMX location 0: body first, then accessories 0..19) or
    /// `bodyTop` (1).</summary>
    public sealed class BodyBoneMod : BoneModBase
    {
        public const string LocationUnknown = "unknown", LocationBodyTop = "bodyTop";
        static readonly string[] LocationNames = { LocationUnknown, LocationBodyTop };

        /// <summary>Enum string; absent = `unknown`.</summary>
        public string Location = LocationUnknown;

        /// <summary>Typed view of <see cref="Location"/>; null for an unknown string.</summary>
        public BoneModLocation? LocationType
        {
            get => Location == LocationUnknown ? BoneModLocation.Unknown : Location == LocationBodyTop ? BoneModLocation.BodyTop : (BoneModLocation?)null;
            set => Location = value == BoneModLocation.BodyTop ? LocationBodyTop : value == BoneModLocation.Unknown ? LocationUnknown : null;
        }

        internal override void ReadMembers(DocReader r)
        {
            Bone = r.Str("bone");
            Location = r.EnumStr("location", Location, LocationNames);
            ReadValues(r);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            ReqStr(w, "bone", Bone);
            ReqStr(w, "location", Location);
            WriteValues(w);
        }
    }
}
