// Character inspector tabs: Animation, State, Face, Look, Hands, FK / IK, Pose,
// Correction, Character. Ranges: scene model A.1 ("character state", "kinematic", "anime"); the look patterns,
// correction categories and FK / IK. All rows apply
// to every selected character (U25) unless marked Primary.
using System.Collections.Generic;

namespace HS2Next.UI.Core
{
    public static partial class InspectorSchemas
    {
        /// <summary>Clothes kinds 0..7 (label suffixes; HS2 order top, bottom, bra, shorts, gloves, pantyhose, socks, shoes).</summary>
        public static readonly IReadOnlyList<string> ClothesKinds = new[] { "top", "bottom", "bra", "shorts", "gloves", "pantyhose", "socks", "shoes" };
        /// <summary>Clothes states 0 on, 1 half, 2 off.</summary>
        public static readonly IReadOnlyList<string> ClothesStates = new[] { "on", "half", "off" };
        /// <summary>Liquid parts 0..4 (scene model A.2 button order: face, breast, belly, back, hip) and levels 0..2.</summary>
        public static readonly IReadOnlyList<string> LiquidParts = new[] { "face", "breast", "belly", "back", "hip" };
        public static readonly IReadOnlyList<string> LiquidLevels = new[] { "none", "some", "much" };
        /// <summary>Eye look patterns 0..4 and the neck buttons {0, 1, 3, 4}.</summary>
        public static readonly IReadOnlyList<string> EyeLookPatterns = new[] { "front", "camera", "away", "fixed", "target" };
        public static readonly IReadOnlyList<string> NeckLookButtons = new[] { "front", "camera", "animation", "fixed" };
        public static readonly IReadOnlyList<int> NeckLookValues = new[] { 0, 1, 3, 4 };
        /// <summary>FK groups (FkGroups members, panel order) and IK groups (IkGroups members, activeIK order).</summary>
        public static readonly IReadOnlyList<string> FkGroups = new[] { "hair", "neck", "breast", "body", "rightHand", "leftHand", "skirt" };
        public static readonly IReadOnlyList<string> IkGroups = new[] { "body", "rightLeg", "leftLeg", "rightArm", "leftArm" };
        /// <summary>IK target names (IkTargets.Names order).</summary>
        public static readonly IReadOnlyList<string> IkTargets = new[]
        {
            "Body", "LeftShoulder", "LeftArmChain", "LeftHand", "RightShoulder", "RightArmChain", "RightHand",
            "LeftThigh", "LeftLegChain", "LeftFoot", "RightThigh", "RightLegChain", "RightFoot",
        };
        /// <summary>Joint-correction categories 0..7.</summary>
        public static readonly IReadOnlyList<string> CorrectionCategories = new[]
            { "leftArm", "rightArm", "leftKnee", "rightKnee", "leftForearm", "rightForearm", "leftThigh", "rightThigh" };

        static List<SchemaSection> BuildChara()
        {
            var tabs = new List<SchemaSection>();

            // Animation (A.1 anime: speed 0–3 (1), pattern 0–1, params 0–1)
            var anime = new SchemaSection("anime", "inspector.tab.anime");
            var a = new B(anime, "inspector.chara.anime.", OpLabels.AnimationOptions);
            a.Ref("chara.anime.ref", "clip").OpLabel = OpLabels.Animation;
            a.FloatClamped("chara.anime.speed", "speed", 0, 3, 1.0).OpLabel = OpLabels.AnimationSpeed;
            a.FloatClamped("chara.anime.pattern", "pattern", 0, 1).OpLabel = OpLabels.AnimationSpeed;   // StudioOps.SetAnimePattern's label (chara.speed)
            a.FloatClamped("chara.anime.params[0]", "param1", 0, 1).Gate = SchemaGates.CharaAnimeParams;
            a.FloatClamped("chara.anime.params[1]", "param2", 0, 1).Gate = SchemaGates.CharaAnimeParams;
            a.Bool("chara.anime.optionVisible", "optionVisible");
            a.Bool("chara.anime.forceLoop", "forceLoop");
            a.Button("ops.RestartAnimation", "restart", "");
            a.Button("ops.RestartAnimation:all", "restartAll", "");
            a.Button("ui.anime.copy", "copy", "");
            a.Button("ops.SetAnimation:paste", "paste", OpLabels.Animation);
            tabs.Add(anime);

            // State (A.1 character state; A.2 buttons)
            var state = new SchemaSection("state", "inspector.tab.state");
            var s = new B(state, "inspector.chara.state.", OpLabels.CharacterState);
            for (int st = 0; st < 3; st++)
                s.Button("ops.SetClothesAll:" + st, "clothesAll." + ClothesStates[st], OpLabels.Clothes).Group = "state.clothes";
            for (int k = 0; k < 8; k++)
            {
                var f = s.Enum("chara.state.clothes[" + k + "]", "clothes." + ClothesKinds[k], ToArray(ClothesStates), "enum.clothesState.");
                f.Gate = SchemaGates.CharaClothes(k);
                f.OpLabel = OpLabels.Clothes;
                f.Group = "state.clothes";
            }
            var acc = s.Bool("chara.state.accessories[\"{slot}\"]", "accessory");
            acc.OpLabel = OpLabels.Accessories;
            acc.Group = "state.accessories";
            s.Button("ops.SetAccessoriesAll:true", "accessoriesAllOn", OpLabels.Accessories).Group = "state.accessories";
            s.Button("ops.SetAccessoriesAll:false", "accessoriesAllOff", OpLabels.Accessories).Group = "state.accessories";
            for (int p = 0; p < 5; p++)
            {
                var f = s.Enum("chara.state.liquid[" + p + "]", "liquid." + LiquidParts[p], ToArray(LiquidLevels), "enum.liquidLevel.");
                f.Gate = SchemaGates.CharaFemale;
                f.Group = "state.liquid";
            }
            s.FloatClamped("chara.face.tears", "tears", 0, 1).Group = "state.other";
            s.FloatClamped("chara.face.cheek", "cheek", 0, 1).Group = "state.other";
            var nip = s.FloatClamped("chara.state.nipple", "nipple", 0, 1);
            nip.Gate = SchemaGates.CharaFemale;
            nip.Group = "state.other";
            // after 1.1.0: whether the nipple shape shows on a top or a bra that is on (off: flat nipples while one covers the chest)
            var nipCover = s.Bool("chara.state.nippleThroughClothes", "nippleThroughClothes");
            nipCover.Gate = SchemaGates.CharaFemale;
            nipCover.Group = "state.other";
            s.FloatClamped("chara.state.gloss", "gloss", 0, 1).Group = "state.other";
            s.FloatClamped("chara.state.wet", "wet", 0, 1).Group = "state.other";
            s.Bool("chara.state.silhouette.enabled", "silhouette").Group = "state.other";
            s.Color("chara.state.silhouette.color", "silhouetteColor", true).Group = "state.other";
            s.Bool("chara.state.son.visible", "son").Group = "state.other";
            s.FloatClamped("chara.state.son.length", "sonLength", 0.5, 3, 1.0).Group = "state.other";
            tabs.Add(state);

            // Face (eyebrow / eyes by list position, mouth by id; A.1 eyes open 0–1, mouth open 0–1)
            var face = new SchemaSection("face", "inspector.tab.face");
            var fa = new B(face, "inspector.chara.face.", OpLabels.Face);
            fa.Index("chara.face.eyebrow", "eyebrow");
            fa.Index("chara.face.eyes", "eyes");
            fa.FloatClamped("chara.face.eyesOpen", "eyesOpen", 0, 1);
            fa.Bool("chara.face.blink", "blink");
            fa.Index("chara.face.mouth", "mouth");
            fa.FloatClamped("chara.face.mouthOpen", "mouthOpen", 0, 1);
            fa.Bool("chara.face.lipSync", "lipSync");
            tabs.Add(face);

            // Look (eye buttons 0..4, neck buttons {0, 1, 3, 4}, disabled while neck FK applies)
            var look = new SchemaSection("look", "inspector.tab.look");
            var lo = new B(look, "inspector.chara.look.", OpLabels.Look);
            lo.Enum("chara.look.eyes", "eyes", ToArray(EyeLookPatterns), "enum.eyeLook.");
            var neck = lo.Enum("chara.look.neck", "neck", ToArray(NeckLookButtons), "enum.neckLook.");
            neck.ChoiceValues = NeckLookValues;
            neck.Gate = SchemaGates.CharaNeckEditable;
            var tgt = lo.Vec3("chara.look.target", "target", new[] { 0.0, 0.0, 0.25 });
            tgt.OpLabel = OpLabels.LookTarget;
            tgt.Gate = SchemaGates.CharaLookTarget;
            var lsz = lo.Add(new FieldSpec { Path = OptionDefs.GizmoLookSize, Kind = FieldKind.Float, Target = FieldTarget.Option, LabelKey = "inspector.chara.look.size", Min = 0.1, Max = 1, TypedMin = 0.1, TypedMax = 1, Default = 0.5 });
            lsz.Gate = SchemaGates.CharaLookTarget;
            tabs.Add(look);

            // Hands (0 = from the animation, n = shape n−1)
            var hands = new SchemaSection("hands", "inspector.tab.hands");
            var h = new B(hands, "inspector.chara.hands.", OpLabels.Hands);
            h.Index("chara.hands[0]", "left");
            h.Index("chara.hands[1]", "right");
            tabs.Add(hands);

            // FK / IK
            var kin = new SchemaSection("kinematics", "inspector.tab.kinematics");
            var fk = new B(kin, "inspector.chara.fk.", OpLabels.Fk);
            fk.Bool("chara.fk.enabled", "enabled").Group = "kinematics.fk";
            foreach (var g in FkGroups) fk.Bool("chara.fk.groups." + g, "group." + g).Group = "kinematics.fk";
            var ho = fk.Float("chara.fk.hipsOffset", "hipsOffset", -2, 2, 0.0);
            ho.Group = "kinematics.fk";
            ho.OpLabel = OpLabels.FkBones;
            var hp = fk.Vec3("chara.fk.hipsPosition", "hipsPosition");
            hp.Nullable = true;
            hp.OpLabel = OpLabels.FkBones;
            hp.Group = "kinematics.fk";
            foreach (var g in new[] { "all", "body", "neck", "rightHand", "leftHand" })
                fk.Button("ops.FkFromAnimation:" + g, "fromAnimation." + g, OpLabels.FkFromAnimation).Group = "kinematics.fk";
            fk.Button("ops.IkFromPose", "toIk", OpLabels.IkFromPose).Group = "kinematics.fk";
            fk.Button("ops.FkInit:hair", "initHair", OpLabels.FkBones).Group = "kinematics.fk";
            fk.Button("ops.FkInit:skirt", "initSkirt", OpLabels.FkBones).Group = "kinematics.fk";
            fk.Add(new FieldSpec { Path = OptionDefs.GizmoFkSize, Kind = FieldKind.Float, Target = FieldTarget.Option, LabelKey = "inspector.chara.fk.size", Min = 0.1, Max = 1, TypedMin = 0.1, TypedMax = 1, Default = 0.5, Group = "kinematics.fk" });
            var bone = fk.Vec3("chara.fk.bones[\"{bone}\"]", "bone");
            bone.OpLabel = OpLabels.FkBones;
            bone.Group = "kinematics.fk.bones";
            var ik = new B(kin, "inspector.chara.ik.", OpLabels.Ik);
            ik.Bool("chara.ik.enabled", "enabled").Group = "kinematics.ik";
            ik.Button("ops.SetIkGroups:all", "allGroups", OpLabels.Ik).Group = "kinematics.ik";
            foreach (var g in IkGroups) ik.Bool("chara.ik.groups." + g, "group." + g).Group = "kinematics.ik";
            foreach (var g in new[] { "all" }) ik.Button("ops.IkFromAnimation:" + g, "fromAnimation." + g, OpLabels.IkFromAnimation).Group = "kinematics.ik";
            foreach (var g in IkGroups) ik.Button("ops.IkFromAnimation:" + g, "fromAnimation." + g, OpLabels.IkFromAnimation).Group = "kinematics.ik";
            ik.Button("ops.FkFromPose", "toFk", OpLabels.FkFromPose).Group = "kinematics.ik";
            ik.Button("ops.BakeIk", "bake", OpLabels.BakeIk).Group = "kinematics.ik";
            var conv = ik.Button("ops.ConvertIk", "convert", OpLabels.ConvertIk);
            conv.Gate = SchemaGates.CharaIkImport;
            conv.Group = "kinematics.ik";
            ik.Add(new FieldSpec { Path = OptionDefs.GizmoIkSize, Kind = FieldKind.Float, Target = FieldTarget.Option, LabelKey = "inspector.chara.ik.size", Min = 0.1, Max = 1, TypedMin = 0.1, TypedMax = 1, Default = 0.5, Group = "kinematics.ik" });
            var tpos = ik.Vec3("chara.ik.targets.{target}.pos", "targetPos");
            tpos.OpLabel = OpLabels.IkTargets;
            tpos.Group = "kinematics.ik.targets";
            var trot = ik.Vec3("chara.ik.targets.{target}.rot", "targetRot");
            trot.OpLabel = OpLabels.IkTargets;
            trot.Group = "kinematics.ik.targets";
            tabs.Add(kin);

            // Pose (the pose browser filtered to the sex + Save; load = one entry)
            var pose = new SchemaSection("pose", "inspector.tab.pose");
            var po = new B(pose, "inspector.chara.pose.", OpLabels.LoadPose);
            po.Button("ui.pose.browse", "browse", "");
            po.Button("ops.LoadPose", "load", OpLabels.LoadPose);
            po.Button("ops.SavePose", "save", "");
            tabs.Add(pose);

            // Correction (U33: all 8 stored categories)
            var corr = new SchemaSection("correction", "inspector.tab.correction");
            var co = new B(corr, "inspector.chara.correction.", OpLabels.JointCorrection);
            for (int i = 0; i < 8; i++) co.Bool("chara.correction[" + i + "]", CorrectionCategories[i]);
            tabs.Add(corr);

            // Character (file info, change character, use card state, save character)
            var ch = new SchemaSection("character", "inspector.tab.character");
            var cb = new B(ch, "inspector.chara.character.", OpLabels.ChangeCharacter);
            cb.Button("ui.chara.change", "change", OpLabels.ChangeCharacter);
            cb.Button("ops.UseCardState", "useCardState", OpLabels.UseCardState);
            cb.Button("ui.chara.save", "save", "");
            tabs.Add(ch);
            return tabs;
        }

        static string[] ToArray(IReadOnlyList<string> l)
        {
            var a = new string[l.Count];
            for (int i = 0; i < a.Length; i++) a[i] = l[i];
            return a;
        }
    }
}
