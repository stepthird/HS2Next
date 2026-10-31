// scene.json kind sections: the per-kind state of character, item, light,
// folder, route and camera nodes, and OpaqueNode for a kind this reader does not know. Members marked (R) in
// the spec may change in Phase 2.2 until the v1 freeze. Maps (FK bones, neck rotations, accessory visibility)
// are SortedDictionary with ordinal keys, the order they are written in.
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace HS2Next.Save
{
    /// <summary>The kind section of a scene node; the subclass decides the `kind` string (also the section's member name).</summary>
    public abstract class NodeState : DocObject
    {
        public abstract string Kind { get; }
        /// <summary>Lights and cameras take no children; every other kind does (characters through attach points).</summary>
        public virtual bool TakesChildren => true;

        /// <summary>A new section for a known kind, or null.</summary>
        public static NodeState Create(string kind)
        {
            switch (kind)
            {
                case SceneNode.KindChara: return new CharaNodeState();
                case SceneNode.KindItem: return new ItemNodeState();
                case SceneNode.KindLight: return new LightNodeState();
                case SceneNode.KindFolder: return new FolderNodeState();
                case SceneNode.KindRoute: return new RouteNodeState();
                case SceneNode.KindCamera: return new CameraNodeState();
                default: return null;
            }
        }

        internal static SortedDictionary<string, T> NewMap<T>() => new SortedDictionary<string, T>(StringComparer.Ordinal);
    }

    // ---------------- character ----------------

    /// <summary>Section `chara`. The appearance is the subtree charas/&lt;nodeId&gt;/.</summary>
    public sealed class CharaNodeState : NodeState
    {
        public override string Kind => SceneNode.KindChara;

        public CharaAnimeState Anime = new CharaAnimeState();
        /// <summary>Exactly 2, [left, right]: 0 = from the animation, n = hand pattern n − 1.</summary>
        public int[] Hands = { 0, 0 };
        public CharaFaceState Face = new CharaFaceState();
        public CharaLookState Look = new CharaLookState();
        public CharaFkState Fk = new CharaFkState();
        public CharaIkState Ik = new CharaIkState();
        /// <summary>Exactly 8 joint-correction categories (`OICharInfo.expression`).</summary>
        public bool[] Correction = new bool[8];
        public CharaState State = new CharaState();
        /// <summary>(R)</summary>
        public CharaVoiceState Voice = new CharaVoiceState();

        internal override void ReadMembers(DocReader r)
        {
            Anime.ReadFrom(r.Obj("anime"));
            Hands = r.IntArray("hands", Hands, 2);
            Face.ReadFrom(r.Obj("face"));
            Look.ReadFrom(r.Obj("look"));
            Fk.ReadFrom(r.Obj("fk"));
            Ik.ReadFrom(r.Obj("ik"));
            Correction = r.BoolArray("correction", Correction, 8);
            State.ReadFrom(r.Obj("state"));
            Voice.ReadFrom(r.Obj("voice"));
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            Anime.WriteTo(w, "anime");
            Ints(w, "hands", Hands, 2);
            Face.WriteTo(w, "face");
            Look.WriteTo(w, "look");
            Fk.WriteTo(w, "fk");
            Ik.WriteTo(w, "ik");
            Bools(w, "correction", Correction, 8);
            State.WriteTo(w, "state");
            Voice.WriteTo(w, "voice");
        }
    }

    /// <summary>`anime`.</summary>
    public sealed class CharaAnimeState : DocObject
    {
        /// <summary>Null = no animation. Absent = (illusion.hs2, 0, 0, 0).</summary>
        public AnimeRef Ref = new AnimeRef();
        /// <summary>UI 0–3.</summary>
        public float Speed = 1f;
        /// <summary>UI 0–1.</summary>
        public float Pattern;
        /// <summary>Layer-0 normalized time captured at save.</summary>
        public float Time;
        public bool ForceLoop;
        public bool OptionVisible = true;
        /// <summary>Exactly 2, UI 0–1.</summary>
        public float[] Params = { 0f, 0f };

        internal override void ReadMembers(DocReader r)
        {
            if (r.Has("ref")) Ref = ReadNew<AnimeRef>(r.ObjN("ref"));
            else r.Consume("ref");
            Speed = r.F32("speed", Speed);
            Pattern = r.F32("pattern", Pattern);
            Time = r.F32("time", Time);
            ForceLoop = r.Bool("forceLoop", ForceLoop);
            OptionVisible = r.Bool("optionVisible", OptionVisible);
            Params = r.F32Array("params", Params, 2);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            WriteN(w, "ref", Ref);
            w.F32("speed", Speed);
            w.F32("pattern", Pattern);
            w.F32("time", Time);
            w.Bool("forceLoop", ForceLoop);
            w.Bool("optionVisible", OptionVisible);
            F32s(w, "params", Params, 2);
        }
    }

    /// <summary>`face`.</summary>
    public sealed class CharaFaceState : DocObject
    {
        /// <summary>Pattern ids of the face pattern lists.</summary>
        public int Eyebrow, Eyes;
        /// <summary>`eyesOpenMax`.</summary>
        public float EyesOpen = 1f;
        public bool Blink = true;
        public int Mouth;
        /// <summary>Studio mouth-open slider.</summary>
        public float MouthOpen;
        public bool LipSync = true;
        /// <summary>`tearsRate`, `hohoAkaRate`.</summary>
        public float Tears, Cheek;
        /// <summary>Not exposed by vanilla Studio.</summary>
        public float EyebrowOpenMax = 1f, MouthOpenMin, MouthOpenMax = 1f;
        /// <summary>Not exposed by vanilla Studio.</summary>
        public bool MouthFixed, MouthAdjustWidth = true, HideHighlight;

        internal override void ReadMembers(DocReader r)
        {
            Eyebrow = r.Int("eyebrow", Eyebrow);
            Eyes = r.Int("eyes", Eyes);
            EyesOpen = r.F32("eyesOpen", EyesOpen);
            Blink = r.Bool("blink", Blink);
            Mouth = r.Int("mouth", Mouth);
            MouthOpen = r.F32("mouthOpen", MouthOpen);
            LipSync = r.Bool("lipSync", LipSync);
            Tears = r.F32("tears", Tears);
            Cheek = r.F32("cheek", Cheek);
            EyebrowOpenMax = r.F32("eyebrowOpenMax", EyebrowOpenMax);
            MouthOpenMin = r.F32("mouthOpenMin", MouthOpenMin);
            MouthOpenMax = r.F32("mouthOpenMax", MouthOpenMax);
            MouthFixed = r.Bool("mouthFixed", MouthFixed);
            MouthAdjustWidth = r.Bool("mouthAdjustWidth", MouthAdjustWidth);
            HideHighlight = r.Bool("hideHighlight", HideHighlight);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Int("eyebrow", Eyebrow);
            w.Int("eyes", Eyes);
            w.F32("eyesOpen", EyesOpen);
            w.Bool("blink", Blink);
            w.Int("mouth", Mouth);
            w.F32("mouthOpen", MouthOpen);
            w.Bool("lipSync", LipSync);
            w.F32("tears", Tears);
            w.F32("cheek", Cheek);
            w.F32("eyebrowOpenMax", EyebrowOpenMax);
            w.F32("mouthOpenMin", MouthOpenMin);
            w.F32("mouthOpenMax", MouthOpenMax);
            w.Bool("mouthFixed", MouthFixed);
            w.Bool("mouthAdjustWidth", MouthAdjustWidth);
            w.Bool("hideHighlight", HideHighlight);
        }
    }

    /// <summary>`look`.</summary>
    public sealed class CharaLookState : DocObject
    {
        /// <summary>Eye pattern 0..4 (4 = look at the look target).</summary>
        public int Eyes;
        /// <summary>Neck pattern 0 forward, 1 camera, 2 away, 3 animation, 4 fix.</summary>
        public int Neck = 3;
        /// <summary>Look-target position local to cf_J_Head.</summary>
        public Vec3 Target = new Vec3(0f, 0f, 0.25f);
        /// <summary>Captured per-bone fixed neck rotations by bone name.</summary>
        public SortedDictionary<string, Quat4> NeckFix = NodeState.NewMap<Quat4>();
        /// <summary>Captured eye local rotations; null = none.</summary>
        public EyeRotations EyeRotations;

        internal override void ReadMembers(DocReader r)
        {
            Eyes = r.Int("eyes", Eyes);
            Neck = r.Int("neck", Neck);
            Target = r.V3("target", Target);
            NeckFix = r.Map("neckFix", v => v.AsQuat());
            EyeRotations = ReadNew<EyeRotations>(r.ObjN("eyeRotations"));
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Int("eyes", Eyes);
            w.Int("neck", Neck);
            w.V3("target", Target);
            w.Map("neckFix", NeckFix, (ww, q) => ww.Quat(q));
            WriteN(w, "eyeRotations", EyeRotations);
        }
    }

    /// <summary>`look.eyeRotations`: both members required (no absent value in the schema).</summary>
    public sealed class EyeRotations : DocObject
    {
        public Quat4 Left = Quat4.Identity, Right = Quat4.Identity;

        internal override void ReadMembers(DocReader r)
        {
            Left = r.Quat("left");
            Right = r.Quat("right");
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Quat("left", Left);
            w.Quat("right", Right);
        }
    }

    /// <summary>`fk`: bones keyed by Bone-list transform name, local Euler degrees; values of inactive groups kept.
    /// `hipsOffset` (2.3): the HS2_FKHeightAdjustUI hips offset added to the `cf_J_Hips`
    /// local position (y, frame of `cf_N_height`, scene units) while `enabled` and `groups.body`; absent = 0.
    /// `hipsPosition` (3.4): the `cf_J_Hips` local position (frame of `cf_N_height`) that
    /// replaces the clip's value under the same conditions (the offset is added on top); null = none, absent = null.</summary>
    public sealed class CharaFkState : DocObject
    {
        public bool Enabled;
        public FkGroups Groups = new FkGroups();
        public SortedDictionary<string, Vec3> Bones = NodeState.NewMap<Vec3>();
        public float HipsOffset;
        /// <summary>Null = no stored hips position (the clip's value is kept).</summary>
        public Vec3? HipsPosition;

        internal override void ReadMembers(DocReader r)
        {
            Enabled = r.Bool("enabled", Enabled);
            Groups.ReadFrom(r.Obj("groups"));
            Bones = r.Map("bones", v => v.AsV3());
            HipsOffset = r.F32("hipsOffset", HipsOffset);
            HipsPosition = r.V3N("hipsPosition");
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Bool("enabled", Enabled);
            Groups.WriteTo(w, "groups");
            w.Map("bones", Bones, (ww, e) => ww.Euler(e));
            w.F32("hipsOffset", HipsOffset);
            w.V3N("hipsPosition", HipsPosition);
        }
    }

    /// <summary>`fk.groups` (`activeFK[0..6]`).</summary>
    public sealed class FkGroups : DocObject
    {
        public bool Hair, Neck = true, Breast, Body = true, RightHand, LeftHand, Skirt;

        internal override void ReadMembers(DocReader r)
        {
            Hair = r.Bool("hair", Hair);
            Neck = r.Bool("neck", Neck);
            Breast = r.Bool("breast", Breast);
            Body = r.Bool("body", Body);
            RightHand = r.Bool("rightHand", RightHand);
            LeftHand = r.Bool("leftHand", LeftHand);
            Skirt = r.Bool("skirt", Skirt);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Bool("hair", Hair);
            w.Bool("neck", Neck);
            w.Bool("breast", Breast);
            w.Bool("body", Body);
            w.Bool("rightHand", RightHand);
            w.Bool("leftHand", LeftHand);
            w.Bool("skirt", Skirt);
        }
    }

    /// <summary>`ik`. `fk.enabled` and `ik.enabled` are independent.</summary>
    public sealed class CharaIkState : DocObject
    {
        public bool Enabled;
        public IkGroups Groups = new IkGroups();
        public IkTargets Targets = new IkTargets();

        internal override void ReadMembers(DocReader r)
        {
            Enabled = r.Bool("enabled", Enabled);
            Groups.ReadFrom(r.Obj("groups"));
            Targets.ReadFrom(r.Obj("targets"));
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Bool("enabled", Enabled);
            Groups.WriteTo(w, "groups");
            Targets.WriteTo(w, "targets");
        }
    }

    /// <summary>`ik.groups`.</summary>
    public sealed class IkGroups : DocObject
    {
        public bool Body = true, RightLeg = true, LeftLeg = true, RightArm = true, LeftArm = true;

        internal override void ReadMembers(DocReader r)
        {
            Body = r.Bool("body", Body);
            RightLeg = r.Bool("rightLeg", RightLeg);
            LeftLeg = r.Bool("leftLeg", LeftLeg);
            RightArm = r.Bool("rightArm", RightArm);
            LeftArm = r.Bool("leftArm", LeftArm);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Bool("body", Body);
            w.Bool("rightLeg", RightLeg);
            w.Bool("leftLeg", LeftLeg);
            w.Bool("rightArm", RightArm);
            w.Bool("leftArm", LeftArm);
        }
    }

    /// <summary>`ik.targets`: the 13 targets in IKTargetEN order. A missing target (null) has no JSON form and is not
    /// written; at run time it is filled by the capture rule or a runtime-only snapshot.</summary>
    public sealed class IkTargets : DocObject
    {
        public const int Count = 13;
        public static readonly string[] Names =
        {
            "Body", "LeftShoulder", "LeftArmChain", "LeftHand", "RightShoulder", "RightArmChain", "RightHand",
            "LeftThigh", "LeftLegChain", "LeftFoot", "RightThigh", "RightLegChain", "RightFoot",
        };

        /// <summary>Indexed as <see cref="Names"/>; null = missing.</summary>
        public readonly IkTarget[] Targets = new IkTarget[Count];

        public IkTarget this[int i] { get => Targets[i]; set => Targets[i] = value; }

        internal override void ReadMembers(DocReader r)
        {
            for (int i = 0; i < Count; i++)
                Targets[i] = r.Has(Names[i]) ? ReadNew<IkTarget>(r.ObjReq(Names[i])) : null;
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            for (int i = 0; i < Count; i++)
                if (Targets[i] != null) Targets[i].WriteTo(w, Names[i]);
        }
    }

    /// <summary>One IK target in character-root space; `rot` is used by the four hand and foot targets only (0 elsewhere).</summary>
    public sealed class IkTarget : DocObject
    {
        public Vec3 Pos = Vec3.Zero;
        /// <summary>Euler degrees.</summary>
        public Vec3 Rot = Vec3.Zero;

        internal override void ReadMembers(DocReader r)
        {
            Pos = r.V3("pos", Pos);
            Rot = r.Euler("rot", Rot);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.V3("pos", Pos);
            w.Euler("rot", Rot);
        }
    }

    /// <summary>`state`.</summary>
    public sealed class CharaState : DocObject
    {
        /// <summary>Exactly 8, per ClothesKind: 0 on, 1 half, 2 off.</summary>
        public int[] Clothes = new int[8];
        /// <summary>Accessory id → visible; a missing id is visible. Writers write every accessory of the worn
        /// coordinate (the scene container fills and prunes this map against the coordinate).</summary>
        public SortedDictionary<string, bool> Accessories = NodeState.NewMap<bool>();
        /// <summary>Exactly 5: 0..2 per SiruParts index.</summary>
        public int[] Liquid = new int[5];
        /// <summary>Nipple stand rate.</summary>
        public float Nipple;
        /// <summary>`wetRate`, `skinTuyaRate`.</summary>
        public float Wet, Gloss;
        public CharaSonState Son = new CharaSonState();
        public CharaSilhouette Silhouette = new CharaSilhouette();

        internal override void ReadMembers(DocReader r)
        {
            Clothes = r.IntArray("clothes", Clothes, 8);
            Accessories = r.Map("accessories", v => v.AsBool());
            Liquid = r.IntArray("liquid", Liquid, 5);
            Nipple = r.F32("nipple", Nipple);
            Wet = r.F32("wet", Wet);
            Gloss = r.F32("gloss", Gloss);
            Son.ReadFrom(r.Obj("son"));
            Silhouette.ReadFrom(r.Obj("silhouette"));
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            Ints(w, "clothes", Clothes, 8);
            w.Map("accessories", Accessories, (ww, b) => ww.Bool(b));
            Ints(w, "liquid", Liquid, 5);
            w.F32("nipple", Nipple);
            w.F32("wet", Wet);
            w.F32("gloss", Gloss);
            Son.WriteTo(w, "son");
            Silhouette.WriteTo(w, "silhouette");
        }
    }

    /// <summary>`state.son`.</summary>
    public sealed class CharaSonState : DocObject
    {
        public bool Visible;
        public float Length = 1f;

        internal override void ReadMembers(DocReader r)
        {
            Visible = r.Bool("visible", Visible);
            Length = r.F32("length", Length);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Bool("visible", Visible);
            w.F32("length", Length);
        }
    }

    /// <summary>`state.silhouette`.</summary>
    public sealed class CharaSilhouette : DocObject
    {
        public bool Enabled;
        public Color4 Color = new Color4(0f, 0f, 1f, 1f);

        internal override void ReadMembers(DocReader r)
        {
            Enabled = r.Bool("enabled", Enabled);
            Color = r.Col("color", Color);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Bool("enabled", Enabled);
            w.Col("color", Color);
        }
    }

    /// <summary>Voice repeat modes.</summary>
    public enum VoiceRepeat { None, All, Select }

    /// <summary>`voice` (R): Voice list rows and the repeat mode.</summary>
    public sealed class CharaVoiceState : DocObject
    {
        public List<AnimeRef> List = new List<AnimeRef>();
        /// <summary>Enum string `none`/`all`/`select`.</summary>
        public string Repeat = "none";

        /// <summary>Typed view of <see cref="Repeat"/>; null for an unknown string.</summary>
        public VoiceRepeat? RepeatType { get => DocEnum.ParseCamel<VoiceRepeat>(Repeat); set => Repeat = DocEnum.Camel(value); }

        internal override void ReadMembers(DocReader r)
        {
            List = ReadList<AnimeRef>(r, "list");
            Repeat = r.EnumStr("repeat", Repeat, DocEnum.CamelNamesOf<VoiceRepeat>());
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            WriteList(w, "list", List);
            ReqStr(w, "repeat", Repeat);
        }
    }

    // ---------------- item ----------------

    /// <summary>Section `item`.</summary>
    public sealed class ItemNodeState : NodeState
    {
        public const int ColorCount = 4;
        public override string Kind => SceneNode.KindItem;

        /// <summary>Item list row (GUID, local id); required.</summary>
        public string Ref;
        /// <summary>List group/category at save (only used to pick among duplicate ids inside one mod).</summary>
        public int Group, Category;
        public ItemAnime Anime = new ItemAnime();
        /// <summary>Exactly 4: [0..2] colour slots 1–3, [3] glass colour (its pattern is unused).</summary>
        public ItemColorSlot[] Colors = { new ItemColorSlot(), new ItemColorSlot(), new ItemColorSlot(), new ItemColorSlot() };
        public float Alpha = 1f;
        /// <summary>(R)</summary>
        public ItemEmission Emission = new ItemEmission();
        public float LightCancel;
        /// <summary>Panel image; null = none.</summary>
        public FileRef Panel;
        public ItemFk Fk = new ItemFk();
        /// <summary>Effective while FK is off.</summary>
        public bool DynamicBones = true;
        /// <summary>One per ItemComponent.optionInfos entry.</summary>
        public List<bool> Options = new List<bool>();

        internal override void ReadMembers(DocReader r)
        {
            Ref = r.Ref("ref");
            Group = r.Int("group", Group);
            Category = r.Int("category", Category);
            Anime.ReadFrom(r.Obj("anime"));
            ReadFixed(r, "colors", Colors, i => new ItemColorSlot());
            Alpha = r.F32("alpha", Alpha);
            Emission.ReadFrom(r.Obj("emission"));
            LightCancel = r.F32("lightCancel", LightCancel);
            Panel = ReadNew<FileRef>(r.ObjN("panel"));
            Fk.ReadFrom(r.Obj("fk"));
            DynamicBones = r.Bool("dynamicBones", DynamicBones);
            Options = new List<bool>(r.BoolArray("options", Options.ToArray()));
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            ReqRef(w, "ref", Ref);
            w.Int("group", Group);
            w.Int("category", Category);
            Anime.WriteTo(w, "anime");
            WriteFixed(w, "colors", Colors, ColorCount);
            w.F32("alpha", Alpha);
            Emission.WriteTo(w, "emission");
            w.F32("lightCancel", LightCancel);
            WriteN(w, "panel", Panel);
            Fk.WriteTo(w, "fk");
            w.Bool("dynamicBones", DynamicBones);
            Bools(w, "options", Options);
        }
    }

    /// <summary>`item.anime`.</summary>
    public sealed class ItemAnime : DocObject
    {
        public int Pattern;
        public float Speed = 1f, Time;

        internal override void ReadMembers(DocReader r)
        {
            Pattern = r.Int("pattern", Pattern);
            Speed = r.F32("speed", Speed);
            Time = r.F32("time", Time);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Int("pattern", Pattern);
            w.F32("speed", Speed);
            w.F32("time", Time);
        }
    }

    /// <summary>`ItemColor` of an item node.</summary>
    public sealed class ItemColorSlot : DocObject
    {
        public Color4 Main = Color4.White;
        public float Metallic, Glossiness;
        public ItemPattern Pattern = new ItemPattern();

        internal override void ReadMembers(DocReader r)
        {
            Main = r.Col("main", Main);
            Metallic = r.F32("metallic", Metallic);
            Glossiness = r.F32("glossiness", Glossiness);
            Pattern.ReadFrom(r.Obj("pattern"));
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Col("main", Main);
            w.F32("metallic", Metallic);
            w.F32("glossiness", Glossiness);
            Pattern.WriteTo(w, "pattern");
        }
    }

    /// <summary>Pattern of an item colour; `item` and `file` both null = no pattern.</summary>
    public sealed class ItemPattern : DocObject
    {
        /// <summary>Category 348; null = none.</summary>
        public string Item;
        /// <summary>User pattern image; null = none.</summary>
        public FileRef File;
        public Color4 Color = Color4.White;
        public bool Clamp = true;
        /// <summary>(R) HS2's vector (x = US, y = VS, z = UT, w = VT by the UI labels).</summary>
        public Vec4 Uv = new Vec4(0f, 0f, 1f, 1f);
        public float Rot;

        internal override void ReadMembers(DocReader r)
        {
            Item = r.RefN("item");
            File = ReadNew<FileRef>(r.ObjN("file"));
            Color = r.Col("color", Color);
            Clamp = r.Bool("clamp", Clamp);
            Uv = r.V4("uv", Uv);
            Rot = r.F32("rot", Rot);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Ref("item", Item);
            WriteN(w, "file", File);
            w.Col("color", Color);
            w.Bool("clamp", Clamp);
            w.V4("uv", Uv);
            w.F32("rot", Rot);
        }
    }

    /// <summary>`item.emission` (R): LDR colour as picked, HDR intensity in stops (rendered colour = color × 2^intensity),
    /// `power` = _EmissionStrength.</summary>
    public sealed class ItemEmission : DocObject
    {
        public Color4 Color = Color4.White;
        public float Intensity, Power;

        internal override void ReadMembers(DocReader r)
        {
            Color = r.Col("color", Color);
            Intensity = r.F32("intensity", Intensity);
            Power = r.F32("power", Power);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Col("color", Color);
            w.F32("intensity", Intensity);
            w.F32("power", Power);
        }
    }

    /// <summary>`item.fk`: bones keyed by item bone name, local Euler degrees.</summary>
    public sealed class ItemFk : DocObject
    {
        public bool Enabled;
        public SortedDictionary<string, Vec3> Bones = NodeState.NewMap<Vec3>();

        internal override void ReadMembers(DocReader r)
        {
            Enabled = r.Bool("enabled", Enabled);
            Bones = r.Map("bones", v => v.AsV3());
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Bool("enabled", Enabled);
            w.Map("bones", Bones, (ww, e) => ww.Euler(e));
        }
    }

    // ---------------- light, folder, camera ----------------

    /// <summary>Section `light`. Type and target come from the list prefab; the node's `visible` has no effect (HS2).</summary>
    public sealed class LightNodeState : NodeState
    {
        public override string Kind => SceneNode.KindLight;
        public override bool TakesChildren => false;

        /// <summary>Light list row; required.</summary>
        public string Ref;
        public Color4 Color = Color4.White;
        public float Intensity = 1f, Range = 10f, SpotAngle = 30f;
        public bool Shadow = true, Enabled = true, ShowTarget = true;

        internal override void ReadMembers(DocReader r)
        {
            Ref = r.Ref("ref");
            Color = r.Col("color", Color);
            Intensity = r.F32("intensity", Intensity);
            Range = r.F32("range", Range);
            SpotAngle = r.F32("spotAngle", SpotAngle);
            Shadow = r.Bool("shadow", Shadow);
            Enabled = r.Bool("enabled", Enabled);
            ShowTarget = r.Bool("showTarget", ShowTarget);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            ReqRef(w, "ref", Ref);
            w.Col("color", Color);
            w.F32("intensity", Intensity);
            w.F32("range", Range);
            w.F32("spotAngle", SpotAngle);
            w.Bool("shadow", Shadow);
            w.Bool("enabled", Enabled);
            w.Bool("showTarget", ShowTarget);
        }
    }

    /// <summary>Section `folder`: `{}`; its name is the node name.</summary>
    public sealed class FolderNodeState : NodeState
    {
        public override string Kind => SceneNode.KindFolder;
        internal override void ReadMembers(DocReader r) { }
        internal override void WriteMembers(CanonicalJsonWriter w) { }
    }

    /// <summary>Section `camera`. At most one is active; on load the last active one in array order wins.</summary>
    public sealed class CameraNodeState : NodeState
    {
        public override string Kind => SceneNode.KindCamera;
        public override bool TakesChildren => false;

        public bool Active;
        /// <summary>(R) null = keep the current field of view.</summary>
        public float? Fov;

        internal override void ReadMembers(DocReader r)
        {
            Active = r.Bool("active", Active);
            Fov = r.F32N("fov");
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Bool("active", Active);
            w.F32N("fov", Fov);
        }
    }

    // ---------------- route ----------------

    public enum RouteOrient { None, Xy, Y }
    public enum RouteConnection { Line, Curve }

    /// <summary>StudioTween.EaseType member names allowed in `RoutePoint.ease`.</summary>
    public enum RouteEase
    {
        easeInQuad, easeOutQuad, easeInOutQuad, easeInCubic, easeOutCubic, easeInOutCubic, easeInQuart, easeOutQuart, easeInOutQuart,
        easeInQuint, easeOutQuint, easeInOutQuint, easeInSine, easeOutSine, easeInOutSine, easeInExpo, easeOutExpo, easeInOutExpo,
        easeInCirc, easeOutCirc, easeInOutCirc, linear, spring, easeInBounce, easeOutBounce, easeInOutBounce, easeInBack, easeOutBack,
        easeInOutBack, easeInElastic, easeOutElastic, easeInOutElastic,
    }

    /// <summary>Section `route`. At least one point (the start point): a reader adds one at the origin when the list is
    /// empty, with the id <see cref="StartPointId"/> of the route node (so reading the same file twice gives the same document).</summary>
    public sealed class RouteNodeState : NodeState
    {
        public override string Kind => SceneNode.KindRoute;

        public bool Playing, Loop = true, ShowLine = true;
        /// <summary>Enum string `none`, `xy` (orient to path), `y` (about Y only).</summary>
        public string Orient = "none";
        public Color4 Color = new Color4(0f, 0f, 1f, 1f);
        public List<RoutePoint> Points = new List<RoutePoint>();

        /// <summary>Id of the route node being read (set by <see cref="SceneNode"/> before reading); not stored.</summary>
        internal string NodeId;

        /// <summary>Typed view of <see cref="Orient"/>; null for an unknown string.</summary>
        public RouteOrient? OrientType { get => DocEnum.ParseCamel<RouteOrient>(Orient); set => Orient = DocEnum.Camel(value); }

        /// <summary>
        /// Id of the start point a reader adds to a route without points: derived from the route node's id,
        /// <c>JsonText.DerivedId("&lt;routeNodeId&gt;/route/start")</c>, so it is the same on every read.
        /// </summary>
        public static string StartPointId(string routeNodeId) => JsonText.DerivedId((routeNodeId ?? "") + "/route/start");

        internal override void ReadMembers(DocReader r)
        {
            Playing = r.Bool("playing", Playing);
            Loop = r.Bool("loop", Loop);
            ShowLine = r.Bool("showLine", ShowLine);
            Orient = r.EnumStr("orient", Orient, DocEnum.CamelNamesOf<RouteOrient>());
            Color = r.Col("color", Color);
            Points = ReadList<RoutePoint>(r, "points");
            if (Points.Count == 0)
            {
                Points.Add(new RoutePoint { Id = StartPointId(NodeId) });
                r.Report(FindingSeverity.Info, "scene.routeEmpty", "points", "route without points: a start point at the origin was added");
            }
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Bool("playing", Playing);
            w.Bool("loop", Loop);
            w.Bool("showLine", ShowLine);
            ReqStr(w, "orient", Orient);
            w.Col("color", Color);
            WriteList(w, "points", Points);
        }
    }

    /// <summary>`RoutePoint`: the start point (index 0) uses `rot` only, the others `pos` only (rot 0).</summary>
    public sealed class RoutePoint : DocObject
    {
        /// <summary>Required.</summary>
        public string Id;
        public Vec3 Pos = Vec3.Zero;
        /// <summary>Euler degrees.</summary>
        public Vec3 Rot = Vec3.Zero;
        /// <summary>Segment speed = speed × 10 tween units per second.</summary>
        public float Speed = 2f;
        /// <summary>Enum string (<see cref="RouteEase"/> names).</summary>
        public string Ease = "linear";
        /// <summary>Enum string `line` or `curve`.</summary>
        public string Connection = "line";
        public bool Link;
        /// <summary>Curve handle; null = none.</summary>
        public RouteAid Aid;

        public RouteEase? EaseType { get => DocEnum.Parse<RouteEase>(Ease); set => Ease = value?.ToString(); }
        public RouteConnection? ConnectionType { get => DocEnum.ParseCamel<RouteConnection>(Connection); set => Connection = DocEnum.Camel(value); }

        internal override void ReadMembers(DocReader r)
        {
            Id = r.Id("id");
            Pos = r.V3("pos", Pos);
            Rot = r.Euler("rot", Rot);
            Speed = r.F32("speed", Speed);
            Ease = r.EnumStr("ease", Ease, DocEnum.NamesOf<RouteEase>());
            Connection = r.EnumStr("connection", Connection, DocEnum.CamelNamesOf<RouteConnection>());
            Link = r.Bool("link", Link);
            Aid = ReadNew<RouteAid>(r.ObjN("aid"));
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            ReqId(w, "id", Id);
            w.V3("pos", Pos);
            w.Euler("rot", Rot);
            w.F32("speed", Speed);
            ReqStr(w, "ease", Ease);
            ReqStr(w, "connection", Connection);
            w.Bool("link", Link);
            WriteN(w, "aid", Aid);
        }
    }

    /// <summary>`aid`: the curve handle of a route point.</summary>
    public sealed class RouteAid : DocObject
    {
        /// <summary>Local to the point.</summary>
        public Vec3 Pos = Vec3.Zero;
        public bool Init;

        internal override void ReadMembers(DocReader r)
        {
            Pos = r.V3("pos", Pos);
            Init = r.Bool("init", Init);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.V3("pos", Pos);
            w.Bool("init", Init);
        }
    }

    // ---------------- unknown kind ----------------

    /// <summary>
    /// A node whose `kind` this reader does not know (newer writer): the node object is kept as read and written
    /// back unchanged in place; only `parent` and `order` follow the reader's tree repair and renumbering. Its children
    /// are ordinary nodes and are kept; the runtime builds none of them.
    /// </summary>
    public sealed class OpaqueNode : NodeState
    {
        public string KindName;
        /// <summary>Every member of the node object except `ext` (held by the node's <see cref="SceneNode.Ext"/>), in read order.</summary>
        public JObject Raw;
        public override string Kind => KindName;

        internal override void ReadMembers(DocReader r) => throw new InvalidOperationException("OpaqueNode is read by SceneNode");
        internal override void WriteMembers(CanonicalJsonWriter w) => throw new InvalidOperationException("OpaqueNode is written by SceneNode");
    }
}
