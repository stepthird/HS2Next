// coord.json accessories: one entry per non-empty slot, addressed by a stable
// id (scene state keys accessory visibility by it), with the per-accessory plugin data: ABMX,
// MaterialEditor, DynamicBoneEditor and AdditionalAccessoryControls.
using System.Collections.Generic;

namespace HS2Next.Save
{
    /// <summary>`Accessory`.</summary>
    public sealed class AccessoryEntry : DocObject
    {
        public const int MoveCount = 2, ColorCount = 4;

        /// <summary>Stable identity; required.</summary>
        public string Id;
        /// <summary>351..363 (ChaListDefine.CategoryNo of the accessory list); required.</summary>
        public int Category;
        /// <summary>Category = <see cref="Category"/>; required.</summary>
        public string Item;
        /// <summary>Attach point, an AccessoryParentKey name such as N_Hair_pony.</summary>
        public string Parent = "";
        /// <summary>Exactly 2: the two HS2 corrections, raw `addMove` values (the assembler scales the position by 0.1).</summary>
        public AccessoryMove[] Moves = { new AccessoryMove(), new AccessoryMove() };
        /// <summary>Exactly 4: [0..2] colour areas, [3] alpha/specular colour.</summary>
        public AccessoryColor[] Colors = { new AccessoryColor(), new AccessoryColor(), new AccessoryColor(), new AccessoryColor() };
        /// <summary>H-scene hide group (stored, not used by Studio).</summary>
        public int HideCategory;
        /// <summary>As read (MoreAccessories writes the hideCategory value here).</summary>
        public int HideTiming = 1;
        /// <summary>Accessory physics off.</summary>
        public bool NoShake;
        /// <summary>Absolute HS2 slot at import (diagnostic only, never used to address the entry); null = none.</summary>
        public int? ImportSlot;
        /// <summary>ABMX modifiers aimed at this accessory (locations 10..29).</summary>
        public List<AccessoryBoneMod> BoneMods = new List<AccessoryBoneMod>();
        /// <summary>MaterialEditor items with ObjectType Accessory.</summary>
        public List<MaterialOverride> MaterialOverrides = new List<MaterialOverride>();
        /// <summary>DynamicBoneEditor overrides.</summary>
        public List<DynamicBoneOverride> DynamicBones = new List<DynamicBoneOverride>();
        /// <summary>AdditionalAccessoryControls data; null = none.</summary>
        public AccessoryRules Rules;
        /// <summary>Accessory-level inline ext of the coord scope; written only when non-empty.</summary>
        public ExtInline Ext = new ExtInline();

        internal override ExtInline InlineExt => Ext;

        internal override void ReadMembers(DocReader r)
        {
            Id = r.Id("id");
            Category = r.Int("category");
            Item = r.Ref("item");
            Parent = r.Str("parent", Parent);
            ReadFixed(r, "moves", Moves, i => new AccessoryMove());
            ReadFixed(r, "colors", Colors, i => new AccessoryColor());
            HideCategory = r.Int("hideCategory", HideCategory);
            HideTiming = r.Int("hideTiming", HideTiming);
            NoShake = r.Bool("noShake", NoShake);
            ImportSlot = r.IntN("importSlot");
            BoneMods = ReadList<AccessoryBoneMod>(r, "boneMods");
            MaterialOverrides = MaterialOverride.ReadList(r, "materialOverrides");
            DynamicBones = ReadList<DynamicBoneOverride>(r, "dynamicBones");
            Rules = ReadNew<AccessoryRules>(r.ObjN("rules"));
            Ext = r.Ext();
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            ReqId(w, "id", Id);
            w.Int("category", Category);
            ReqRef(w, "item", Item);
            ReqStr(w, "parent", Parent);
            WriteFixed(w, "moves", Moves, MoveCount);
            WriteFixed(w, "colors", Colors, ColorCount);
            w.Int("hideCategory", HideCategory);
            w.Int("hideTiming", HideTiming);
            w.Bool("noShake", NoShake);
            w.IntN("importSlot", ImportSlot);
            WriteList(w, "boneMods", BoneMods);
            MaterialOverride.WriteList(w, "materialOverrides", MaterialOverrides);
            WriteList(w, "dynamicBones", DynamicBones);
            WriteN(w, "rules", Rules);
        }
    }

    /// <summary>`Move`: one HS2 accessory correction (raw `addMove` values).</summary>
    public sealed class AccessoryMove : DocObject
    {
        public Vec3 Position = Vec3.Zero;
        /// <summary>Euler degrees.</summary>
        public Vec3 Rotation = Vec3.Zero;
        public Vec3 Scale = Vec3.One;

        internal override void ReadMembers(DocReader r)
        {
            Position = r.V3("position", Position);
            Rotation = r.Euler("rotation", Rotation);
            Scale = r.V3("scale", Scale);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.V3("position", Position);
            w.Euler("rotation", Rotation);
            w.V3("scale", Scale);
        }
    }

    /// <summary>`AccColor`.</summary>
    public sealed class AccessoryColor : DocObject
    {
        public Color4 Color = Color4.White;
        public float Gloss = 0.5f, Metallic = 0.5f, Smoothness = 0.5f;

        internal override void ReadMembers(DocReader r)
        {
            Color = r.Col("color", Color);
            Gloss = r.F32("gloss", Gloss);
            Metallic = r.F32("metallic", Metallic);
            Smoothness = r.F32("smoothness", Smoothness);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Col("color", Color);
            w.F32("gloss", Gloss);
            w.F32("metallic", Metallic);
            w.F32("smoothness", Smoothness);
        }
    }

    /// <summary>ABMX modifier aimed at the accessory that holds it (no `location`).</summary>
    public sealed class AccessoryBoneMod : BoneModBase
    {
        internal override void ReadMembers(DocReader r)
        {
            Bone = r.Str("bone");
            ReadValues(r);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            ReqStr(w, "bone", Bone);
            WriteValues(w);
        }
    }

    /// <summary>The seven overridable DynamicBone values; null = not overridden. Also the `original` object.</summary>
    public class DynamicBoneValues : DocObject
    {
        /// <summary>0 None, 1 X, 2 Y, 3 Z.</summary>
        public int? FreezeAxis;
        public float? Weight, Damping, Elasticity, Stiffness, Inertia, Radius;

        internal override void ReadMembers(DocReader r) => ReadValues(r);
        internal override void WriteMembers(CanonicalJsonWriter w) => WriteValues(w);

        protected void ReadValues(DocReader r)
        {
            FreezeAxis = r.IntN("freezeAxis");
            Weight = r.F32N("weight");
            Damping = r.F32N("damping");
            Elasticity = r.F32N("elasticity");
            Stiffness = r.F32N("stiffness");
            Inertia = r.F32N("inertia");
            Radius = r.F32N("radius");
        }

        protected void WriteValues(CanonicalJsonWriter w)
        {
            w.IntN("freezeAxis", FreezeAxis);
            w.F32N("weight", Weight);
            w.F32N("damping", Damping);
            w.F32N("elasticity", Elasticity);
            w.F32N("stiffness", Stiffness);
            w.F32N("inertia", Inertia);
            w.F32N("radius", Radius);
        }
    }

    /// <summary>`Dbe` (DynamicBoneEditor).</summary>
    public sealed class DynamicBoneOverride : DynamicBoneValues
    {
        /// <summary>Name of the DynamicBone's root transform (selects the component); required.</summary>
        public string Root;
        /// <summary>Prefab values at first edit (all nullable), for UI reset only.</summary>
        public DynamicBoneValues Original = new DynamicBoneValues();

        internal override void ReadMembers(DocReader r)
        {
            Root = r.Str("root");
            ReadValues(r);
            Original.ReadFrom(r.Obj("original"));
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            ReqStr(w, "root", Root);
            WriteValues(w);
            Original.WriteTo(w, "original");
        }
    }

    /// <summary>`Accessory.rules` (AdditionalAccessoryControls).</summary>
    public sealed class AccessoryRules : DocObject
    {
        /// <summary>In source order.</summary>
        public List<AacRule> Visibility = new List<AacRule>();
        /// <summary>Keep this accessory when another coordinate is applied.</summary>
        public bool CharacterAccessory;
        /// <summary>`AutoMatchBackHairColor`.</summary>
        public bool MatchHairColor;
        /// <summary>`&lt;transform path&gt;` or `&lt;path&gt;|v0|v1|…` (skinned vertex), as stored; null = none.</summary>
        public string AdvancedParent;
        /// <summary>Display name stored by the plugin (informational); null = none.</summary>
        public string Name;

        internal override void ReadMembers(DocReader r)
        {
            Visibility = ReadList<AacRule>(r, "visibility");
            CharacterAccessory = r.Bool("characterAccessory", CharacterAccessory);
            MatchHairColor = r.Bool("matchHairColor", MatchHairColor);
            AdvancedParent = r.StrN("advancedParent");
            Name = r.StrN("name");
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            WriteList(w, "visibility", Visibility);
            w.Bool("characterAccessory", CharacterAccessory);
            w.Bool("matchHairColor", MatchHairColor);
            w.Str("advancedParent", AdvancedParent);
            w.Str("name", Name);
        }
    }

    /// <summary>`AacRule`: `rule` is an AdditionalAccessoryVisibilityRules member name; ACCESSORY_LINK and
    /// ACCESSORY_INVERSE_LINK use <see cref="Link"/> (an accessory id), every other rule <see cref="Modifier"/>
    /// (a Modifier member name, or the decimal text as read). The names are the plugin's and are kept as read.</summary>
    public sealed class AacRule : DocObject
    {
        /// <summary>Required.</summary>
        public string Rule;
        public string Modifier;
        public string Link;

        internal override void ReadMembers(DocReader r)
        {
            Rule = r.Str("rule");
            Modifier = r.StrN("modifier");
            Link = r.IdN("link");
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            ReqStr(w, "rule", Rule);
            w.Str("modifier", Modifier);
            w.Id("link", Link);
        }
    }

    /// <summary>`ruleOverrides`.</summary>
    public sealed class AacRuleOverrides : DocObject
    {
        public List<AacOverride> Suppressed = new List<AacOverride>();
        public List<AacOverride> Overrides = new List<AacOverride>();

        internal override void ReadMembers(DocReader r)
        {
            Suppressed = ReadList<AacOverride>(r, "suppressed");
            Overrides = ReadList<AacOverride>(r, "overrides");
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            WriteList(w, "suppressed", Suppressed);
            WriteList(w, "overrides", Overrides);
        }
    }

    /// <summary>`AacOverride` (same naming as <see cref="AacRule"/>).</summary>
    public sealed class AacOverride : DocObject
    {
        /// <summary>Required.</summary>
        public string Rule;
        public string Modifier;
        /// <summary>Required.</summary>
        public string OverrideRule;
        public string OverrideModifier;

        internal override void ReadMembers(DocReader r)
        {
            Rule = r.Str("rule");
            Modifier = r.StrN("modifier");
            OverrideRule = r.Str("overrideRule");
            OverrideModifier = r.StrN("overrideModifier");
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            ReqStr(w, "rule", Rule);
            w.Str("modifier", Modifier);
            ReqStr(w, "overrideRule", OverrideRule);
            w.Str("overrideModifier", OverrideModifier);
        }
    }
}
