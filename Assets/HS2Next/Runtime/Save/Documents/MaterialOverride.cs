// MaterialEditor items (`ME`): one schema for every `materialOverrides` array
// (character, hair parts, clothes slots, accessories). The target object is implied by the array's place.
// Array order = apply order (the 11 types in TypeOrder, source order inside a type); the list is written in
// its own order. `material` has "(Instance)"/" Instance" removed; `property` has no leading underscore. The
// JSON discriminator is `type`; every member after it is required. An unknown type is an unknown enum string
//: the whole object is kept as MeUnknown and written back unchanged.
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace HS2Next.Save
{
    /// <summary>A MaterialEditor item; the subclass decides the `type` string.</summary>
    public abstract class MaterialOverride : DocObject
    {
        /// <summary>The `type` discriminator.</summary>
        public abstract string Type { get; }

        /// <summary>The 11 known types in apply order (`MaterialEditorData.AllItems`).</summary>
        public static readonly string[] TypeOrder = { "copy", "name", "shader", "renderer", "float", "keyword", "color", "vector", "texture", "cubemap", "projector" };

        /// <summary>Apply-order rank of a type string (0..10), or -1 for an unknown type.</summary>
        public static int Rank(string type) => Array.IndexOf(TypeOrder, type);

        /// <summary>A new item of the type named by <paramref name="type"/>, or null for an unknown type.</summary>
        public static MaterialOverride Create(string type)
        {
            switch (type)
            {
                case "copy": return new MeCopy();
                case "name": return new MeName();
                case "shader": return new MeShader();
                case "renderer": return new MeRenderer();
                case "float": return new MeFloat();
                case "keyword": return new MeKeyword();
                case "color": return new MeColor();
                case "vector": return new MeVector();
                case "texture": return new MeTexture();
                case "cubemap": return new MeCubemap();
                case "projector": return new MeProjector();
                default: return null;
            }
        }

        /// <summary>Reads one item (dispatch on `type`).</summary>
        internal static MaterialOverride Read(DocReader r)
        {
            string type = r.Str("type");
            var x = Create(type);
            if (x == null)
            {
                r.Report(FindingSeverity.Warning, "json.unknownEnum", "type", "unknown MaterialEditor type \"" + type + "\" kept");
                return new MeUnknown { TypeName = type, Raw = r.Detach() };
            }
            x.ReadFrom(r);
            return x;
        }

        internal static List<MaterialOverride> ReadList(DocReader r, string name)
        {
            var list = new List<MaterialOverride>();
            foreach (var e in r.ObjArray(name)) list.Add(Read(e));
            return list;
        }

        /// <summary>A `ME[]` in model order; a null list writes `[]`, a null item fails with BadDocument and its path.</summary>
        internal static void WriteList(CanonicalJsonWriter w, string name, List<MaterialOverride> list) =>
            DocObject.WriteList(w, name, list);

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            ReqStr(w, "type", Type);
            WriteItem(w);
        }

        /// <summary>The members after `type`.</summary>
        internal abstract void WriteItem(CanonicalJsonWriter w);

        // A member that holds a number or a string (renderer/float values as MaterialEditor stored them).
        internal static void ReadNumberOrText(DocValue v, bool allowNull, Func<DocValue, bool> setNumber, Action<string> setText)
        {
            if (v.Type == JTokenType.String) { setText(v.AsStr()); return; }
            if (allowNull && v.IsNull) return;
            if (!setNumber(v)) throw v.Error(allowNull ? "a number, a string or null" : "a number or a string");
        }
    }

    /// <summary>Items addressed by material name.</summary>
    public abstract class MeMaterialItem : MaterialOverride
    {
        public string Material;
    }

    /// <summary>Items addressed by material and shader property (name without the leading underscore).</summary>
    public abstract class MeMaterialProperty : MeMaterialItem
    {
        public string Property;

        protected void ReadTarget(DocReader r)
        {
            Material = r.Str("material");
            Property = r.Str("property");
        }

        protected void WriteTarget(CanonicalJsonWriter w)
        {
            ReqStr(w, "material", Material);
            ReqStr(w, "property", Property);
        }
    }

    /// <summary>`copy`: a material clone named `&lt;material&gt;.MECopy&lt;n&gt;`.</summary>
    public sealed class MeCopy : MeMaterialItem
    {
        public override string Type => "copy";
        public string CopyName;

        internal override void ReadMembers(DocReader r)
        {
            r.Consume("type");
            Material = r.Str("material");
            CopyName = r.Str("copyName");
        }

        internal override void WriteItem(CanonicalJsonWriter w)
        {
            ReqStr(w, "material", Material);
            ReqStr(w, "copyName", CopyName);
        }
    }

    /// <summary>`name`: renames a material of a renderer.</summary>
    public sealed class MeName : MaterialOverride
    {
        public override string Type => "name";
        public string Renderer, Material, Value;
        /// <summary>Nullable.</summary>
        public string Original;

        internal override void ReadMembers(DocReader r)
        {
            r.Consume("type");
            Renderer = r.Str("renderer");
            Material = r.Str("material");
            Value = r.Str("value");
            Original = r.ValueReq("original").AsStrN();
        }

        internal override void WriteItem(CanonicalJsonWriter w)
        {
            ReqStr(w, "renderer", Renderer);
            ReqStr(w, "material", Material);
            ReqStr(w, "value", Value);
            w.Str("original", Original);
        }
    }

    /// <summary>`shader`: shader and/or render-queue edit.</summary>
    public sealed class MeShader : MeMaterialItem
    {
        public override string Type => "shader";
        /// <summary>Null = queue-only edit.</summary>
        public string Shader, ShaderOriginal;
        public int? RenderQueue, RenderQueueOriginal;

        internal override void ReadMembers(DocReader r)
        {
            r.Consume("type");
            Material = r.Str("material");
            Shader = r.ValueReq("shader").AsStrN();
            ShaderOriginal = r.ValueReq("shaderOriginal").AsStrN();
            RenderQueue = IntN(r.ValueReq("renderQueue"));
            RenderQueueOriginal = IntN(r.ValueReq("renderQueueOriginal"));
        }

        static int? IntN(DocValue v) => v.IsNull ? (int?)null : v.AsInt();

        internal override void WriteItem(CanonicalJsonWriter w)
        {
            ReqStr(w, "material", Material);
            w.Str("shader", Shader);
            w.Str("shaderOriginal", ShaderOriginal);
            w.IntN("renderQueue", RenderQueue);
            w.IntN("renderQueueOriginal", RenderQueueOriginal);
        }
    }

    /// <summary>Renderer properties of a `renderer` item.</summary>
    public enum MeRendererProperty { Enabled, ShadowCastingMode, ReceiveShadows, RecalculateNormals, UpdateWhenOffscreen }

    /// <summary>`renderer`: a renderer property. MaterialEditor stores the value as text; the importer converts it to an
    /// int and keeps text that does not parse (<see cref="ValueText"/>).</summary>
    public sealed class MeRenderer : MaterialOverride
    {
        public override string Type => "renderer";
        public string Renderer;
        /// <summary>Enum string (<see cref="MeRendererProperty"/> names); an unknown name is kept as read.</summary>
        public string Property;
        /// <summary>The value, unless <see cref="ValueText"/> is set.</summary>
        public int Value;
        /// <summary>Non-null: the value is this text (written as a JSON string).</summary>
        public string ValueText;
        /// <summary>The original value (null = none), unless <see cref="OriginalText"/> is set.</summary>
        public int? Original;
        public string OriginalText;

        public MeRendererProperty? PropertyType { get => DocEnum.Parse<MeRendererProperty>(Property); set => Property = value?.ToString(); }

        internal override void ReadMembers(DocReader r)
        {
            r.Consume("type");
            Renderer = r.Str("renderer");
            Property = r.EnumStr("property", null, DocEnum.NamesOf<MeRendererProperty>());
            if (Property == null) r.ValueReq("property");
            ReadNumberOrText(r.ValueReq("value"), false, v => { Value = v.AsInt(); return true; }, s => ValueText = s);
            ReadNumberOrText(r.ValueReq("original"), true, v => { Original = v.AsInt(); return true; }, s => OriginalText = s);
        }

        internal override void WriteItem(CanonicalJsonWriter w)
        {
            ReqStr(w, "renderer", Renderer);
            ReqStr(w, "property", Property);
            if (ValueText != null) w.Str("value", ValueText); else w.Int("value", Value);
            if (OriginalText != null) w.Str("original", OriginalText); else w.IntN("original", Original);
        }
    }

    /// <summary>`float`: a float shader property; text that did not parse at import stays text (<see cref="ValueText"/>).</summary>
    public sealed class MeFloat : MeMaterialProperty
    {
        public override string Type => "float";
        /// <summary>The value, unless <see cref="ValueText"/> is set.</summary>
        public float Value;
        /// <summary>Non-null: the value is this text (written as a JSON string).</summary>
        public string ValueText;
        /// <summary>The original value (null = none), unless <see cref="OriginalText"/> is set.</summary>
        public float? Original;
        public string OriginalText;

        internal override void ReadMembers(DocReader r)
        {
            r.Consume("type");
            ReadTarget(r);
            ReadNumberOrText(r.ValueReq("value"), false, v => { Value = v.AsF32(); return true; }, s => ValueText = s);
            ReadNumberOrText(r.ValueReq("original"), true, v => { Original = v.AsF32(); return true; }, s => OriginalText = s);
        }

        internal override void WriteItem(CanonicalJsonWriter w)
        {
            WriteTarget(w);
            if (ValueText != null) w.Str("value", ValueText); else w.F32("value", Value);
            if (OriginalText != null) w.Str("original", OriginalText); else w.F32N("original", Original);
        }
    }

    /// <summary>`keyword`: shader keyword `_` + property on or off.</summary>
    public sealed class MeKeyword : MeMaterialProperty
    {
        public override string Type => "keyword";
        public bool Value, Original;

        internal override void ReadMembers(DocReader r)
        {
            r.Consume("type");
            ReadTarget(r);
            Value = r.Bool("value");
            Original = r.Bool("original");
        }

        internal override void WriteItem(CanonicalJsonWriter w)
        {
            WriteTarget(w);
            w.Bool("value", Value);
            w.Bool("original", Original);
        }
    }

    /// <summary>`color`.</summary>
    public sealed class MeColor : MeMaterialProperty
    {
        public override string Type => "color";
        public Color4 Value;
        public Color4? Original;

        internal override void ReadMembers(DocReader r)
        {
            r.Consume("type");
            ReadTarget(r);
            Value = r.Col("value");
            var o = r.ValueReq("original");
            Original = o.IsNull ? (Color4?)null : o.AsCol();
        }

        internal override void WriteItem(CanonicalJsonWriter w)
        {
            WriteTarget(w);
            w.Col("value", Value);
            w.ColN("original", Original);
        }
    }

    /// <summary>`vector`.</summary>
    public sealed class MeVector : MeMaterialProperty
    {
        public override string Type => "vector";
        public Vec4 Value;
        public Vec4? Original;

        internal override void ReadMembers(DocReader r)
        {
            r.Consume("type");
            ReadTarget(r);
            Value = r.V4("value");
            var o = r.ValueReq("original");
            Original = o.IsNull ? (Vec4?)null : o.AsV4();
        }

        internal override void WriteItem(CanonicalJsonWriter w)
        {
            WriteTarget(w);
            w.V4("value", Value);
            w.V4N("original", Original);
        }
    }

    /// <summary>`texture`: image and/or offset/scale.</summary>
    public sealed class MeTexture : MeMaterialProperty
    {
        public override string Type => "texture";
        /// <summary>`res/…` entry; null = only offset/scale edited, or the image was missing at import.</summary>
        public string Image;
        public Vec2? Offset, OffsetOriginal, Scale, ScaleOriginal;

        internal override void ReadMembers(DocReader r)
        {
            r.Consume("type");
            ReadTarget(r);
            Image = r.ValueReq("image").AsResN();
            Offset = V2N(r.ValueReq("offset"));
            OffsetOriginal = V2N(r.ValueReq("offsetOriginal"));
            Scale = V2N(r.ValueReq("scale"));
            ScaleOriginal = V2N(r.ValueReq("scaleOriginal"));
        }

        static Vec2? V2N(DocValue v) => v.IsNull ? (Vec2?)null : v.AsV2();

        internal override void WriteItem(CanonicalJsonWriter w)
        {
            WriteTarget(w);
            w.Res("image", Image);
            w.V2N("offset", Offset);
            w.V2N("offsetOriginal", OffsetOriginal);
            w.V2N("scale", Scale);
            w.V2N("scaleOriginal", ScaleOriginal);
        }
    }

    /// <summary>`cubemap`.</summary>
    public sealed class MeCubemap : MeMaterialProperty
    {
        public override string Type => "cubemap";
        /// <summary>`res/…` entry or null.</summary>
        public string Image;

        internal override void ReadMembers(DocReader r)
        {
            r.Consume("type");
            ReadTarget(r);
            Image = r.ValueReq("image").AsResN();
        }

        internal override void WriteItem(CanonicalJsonWriter w)
        {
            WriteTarget(w);
            w.Res("image", Image);
        }
    }

    /// <summary>Projector properties of a `projector` item.</summary>
    public enum MeProjectorProperty { Enabled, NearClipPlane, FarClipPlane, FieldOfView, AspectRatio, Orthographic, OrthographicSize, IgnoreCharaLayer, IgnoreMapLayer }

    /// <summary>`projector`: values stay text (rare; not applied in v1).</summary>
    public sealed class MeProjector : MaterialOverride
    {
        public override string Type => "projector";
        public string Projector;
        /// <summary>Enum string (<see cref="MeProjectorProperty"/> names); an unknown name is kept as read.</summary>
        public string Property;
        public string Value;
        /// <summary>Nullable.</summary>
        public string Original;

        public MeProjectorProperty? PropertyType { get => DocEnum.Parse<MeProjectorProperty>(Property); set => Property = value?.ToString(); }

        internal override void ReadMembers(DocReader r)
        {
            r.Consume("type");
            Projector = r.Str("projector");
            Property = r.EnumStr("property", null, DocEnum.NamesOf<MeProjectorProperty>());
            if (Property == null) r.ValueReq("property");
            Value = r.Str("value");
            Original = r.ValueReq("original").AsStrN();
        }

        internal override void WriteItem(CanonicalJsonWriter w)
        {
            ReqStr(w, "projector", Projector);
            ReqStr(w, "property", Property);
            ReqStr(w, "value", Value);
            w.Str("original", Original);
        }
    }

    /// <summary>An item whose `type` this reader does not know: the whole object is kept and written back unchanged.</summary>
    public sealed class MeUnknown : MaterialOverride
    {
        /// <summary>The `type` as read.</summary>
        public string TypeName;
        /// <summary>Every member of the object, `type` included, in read order.</summary>
        public JObject Raw;
        public override string Type => TypeName;

        internal override void ReadMembers(DocReader r) => Raw = r.Detach();

        /// <summary>The raw members in read order (`type` among them).</summary>
        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            foreach (var p in Raw.Properties()) w.Token(p.Name, p.Value);
        }

        internal override void WriteItem(CanonicalJsonWriter w) { }
    }
}
