// chara.json `hair`. HS2's `hair.kind` is not stored: HS2 copies it from the
// back hair's list row on every load, so it is derived.
using System;
using System.Collections.Generic;

namespace HS2Next.Save
{
    /// <summary>`hair`.</summary>
    public sealed class HairData : DocObject
    {
        /// <summary>JSON member names of the parts in HairKind order.</summary>
        public static readonly string[] PartNames = { "back", "front", "side", "option" };

        /// <summary>0 hair_dithering, 1 hair_cutout.</summary>
        public int ShaderType;
        public HairEditorSwitches Editor = new HairEditorSwitches();
        /// <summary>HairKind 0..3 (back, front, side, option); items 300 / 301 / 302 / 303.</summary>
        public readonly HairPart[] Parts = { new HairPart(), new HairPart(), new HairPart(), new HairPart() };

        public HairPart Back { get => Parts[0]; set => Parts[0] = value; }
        public HairPart Front { get => Parts[1]; set => Parts[1] = value; }
        public HairPart Side { get => Parts[2]; set => Parts[2] = value; }
        public HairPart Option { get => Parts[3]; set => Parts[3] = value; }

        internal override void ReadMembers(DocReader r)
        {
            ShaderType = r.Int("shaderType", ShaderType);
            Editor.ReadFrom(r.Obj("editor"));
            for (int k = 0; k < Parts.Length; k++) Parts[k].ReadFrom(r.Obj(PartNames[k]));
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Int("shaderType", ShaderType);
            Editor.WriteTo(w, "editor");
            for (int k = 0; k < Parts.Length; k++) Parts[k].WriteTo(w, PartNames[k]);
        }
    }

    /// <summary>`hair.editor`: Maker switches (colour sync, auto colour, edit together).</summary>
    public sealed class HairEditorSwitches : DocObject
    {
        public bool SameSetting = true, AutoSetting = true, CtrlTogether;

        internal override void ReadMembers(DocReader r)
        {
            SameSetting = r.Bool("sameSetting", SameSetting);
            AutoSetting = r.Bool("autoSetting", AutoSetting);
            CtrlTogether = r.Bool("ctrlTogether", CtrlTogether);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Bool("sameSetting", SameSetting);
            w.Bool("autoSetting", AutoSetting);
            w.Bool("ctrlTogether", CtrlTogether);
        }
    }

    /// <summary>`HairPart`.</summary>
    public sealed class HairPart : DocObject
    {
        public const int AccessoryColorCount = 4;

        public string Item = DocDefaults.NoneRef;
        /// <summary>`_Color`.</summary>
        public Color4 BaseColor = new Color4(0.2f, 0.2f, 0.2f, 1f);
        /// <summary>`_TopColor`.</summary>
        public Color4 TopColor = new Color4(0.039f, 0.039f, 0.039f, 1f);
        /// <summary>`_UnderColor`.</summary>
        public Color4 UnderColor = new Color4(0.565f, 0.565f, 0.565f, 1f);
        public Color4 Specular = new Color4(0.3f, 0.3f, 0.3f, 1f);
        public float Metallic, Smoothness;
        /// <summary>Exactly 4: colours of hair-accessory renderers (index 3 unused by HS2).</summary>
        public Color4[] AccessoryColors = { Color4.White, Color4.White, Color4.White, Color4.White };
        /// <summary>Strand corrections for <see cref="Item"/>, written sorted by index. An editor that changes the item
        /// MUST clear them; when their count differs from the prefab's bundle count the prefab defaults apply.</summary>
        public List<HairBundle> Bundles = new List<HairBundle>();
        /// <summary>Hair mesh pattern, item 306.</summary>
        public HairMesh Mesh = new HairMesh();
        /// <summary>MaterialEditor items with ObjectType Hair and Slot = this part.</summary>
        public List<MaterialOverride> MaterialOverrides = new List<MaterialOverride>();

        internal override void ReadMembers(DocReader r)
        {
            Item = r.Ref("item", Item);
            BaseColor = r.Col("baseColor", BaseColor);
            TopColor = r.Col("topColor", TopColor);
            UnderColor = r.Col("underColor", UnderColor);
            Specular = r.Col("specular", Specular);
            Metallic = r.F32("metallic", Metallic);
            Smoothness = r.F32("smoothness", Smoothness);
            var colors = r.Array("accessoryColors", v => v.AsCol(), null, AccessoryColorCount);
            if (colors.Count == AccessoryColorCount) AccessoryColors = colors.ToArray();
            Bundles = ReadList<HairBundle>(r, "bundles");
            Mesh.ReadFrom(r.Obj("mesh"));
            MaterialOverrides = MaterialOverride.ReadList(r, "materialOverrides");
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            ReqRef(w, "item", Item);
            w.Col("baseColor", BaseColor);
            w.Col("topColor", TopColor);
            w.Col("underColor", UnderColor);
            w.Col("specular", Specular);
            w.F32("metallic", Metallic);
            w.F32("smoothness", Smoothness);
            WriteColors(w, "accessoryColors", AccessoryColors, AccessoryColorCount);
            var bundles = new List<HairBundle>(Bundles);
            StableSort(bundles, (a, b) => a.Index.CompareTo(b.Index));
            WriteList(w, "bundles", bundles);
            Mesh.WriteTo(w, "mesh");
            MaterialOverride.WriteList(w, "materialOverrides", MaterialOverrides);
        }

        static void StableSort<T>(List<T> list, Comparison<T> cmp)
        {
            var order = new List<KeyValuePair<int, T>>(list.Count);
            for (int i = 0; i < list.Count; i++) order.Add(new KeyValuePair<int, T>(i, list[i]));
            order.Sort((a, b) => { int c = cmp(a.Value, b.Value); return c != 0 ? c : a.Key.CompareTo(b.Key); });
            for (int i = 0; i < list.Count; i++) list[i] = order[i].Value;
        }
    }

    /// <summary>`Bundle` of a hair part: `move`/`rotate` are normalised Lerp factors between the prefab's min/max.</summary>
    public sealed class HairBundle : DocObject
    {
        /// <summary>Required.</summary>
        public int Index;
        public Vec3 Move = Vec3.Zero, Rotate = Vec3.Zero;
        public bool NoShake;

        internal override void ReadMembers(DocReader r)
        {
            Index = r.Int("index");
            Move = r.V3("move", Move);
            Rotate = r.V3("rotate", Rotate);
            NoShake = r.Bool("noShake", NoShake);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Int("index", Index);
            w.V3("move", Move);
            w.V3("rotate", Rotate);
            w.Bool("noShake", NoShake);
        }
    }

    /// <summary>`mesh` of a hair part.</summary>
    public sealed class HairMesh : DocObject
    {
        public string Item = DocDefaults.NoneRef;
        public Color4 Color = Color4.White;
        public Vec4 Layout = new Vec4(1f, 1f, 0f, 0f);

        internal override void ReadMembers(DocReader r)
        {
            Item = r.Ref("item", Item);
            Color = r.Col("color", Color);
            Layout = r.V4("layout", Layout);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            ReqRef(w, "item", Item);
            w.Col("color", Color);
            w.V4("layout", Layout);
        }
    }
}
