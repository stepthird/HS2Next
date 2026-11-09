// History labels of the operations layer: the Unity-free
// mirror of StudioOps.Labels (package B) used by the inspector schema and checked by the mono SchemaTests. One table:
// the strings here and in StudioOps.Labels are the same (see the P ↔ B note in the questions file).
using System.Collections.Generic;

namespace HS2Next.UI.Core
{
    public static class OpLabels
    {
        // tree
        public const string AddCharacter = "Add character", AddItem = "Add item", AddLight = "Add light", AddFolder = "Add folder",
            AddCamera = "Add camera", Delete = "Delete", Duplicate = "Duplicate", Parent = "Parent", Reorder = "Reorder",
            Visibility = "Visibility", Rename = "Rename", CopyTransform = "Copy transform";
        // transform
        public const string Transform = "Transform", ResetTransform = "Reset transform", MoveToPivot = "Move to pivot";
        // chara
        public const string Animation = "Animation", AnimationSpeed = "Animation speed", AnimationOptions = "Animation options",
            Face = "Face", Hands = "Hands", Look = "Look", LookTarget = "Look target", Clothes = "Clothes", Accessories = "Accessories",
            CharacterState = "Character state", JointCorrection = "Joint correction", ChangeCharacter = "Change character", UseCardState = "Use card state";
        // FK / IK / pose
        public const string Fk = "FK", FkBones = "FK bones", FkFromAnimation = "FK from animation", FkFromPose = "FK from pose",
            Ik = "IK", IkTargets = "IK targets", IkFromAnimation = "IK from animation", IkFromPose = "IK from pose",
            ConvertIk = "Convert IK", BakeIk = "Bake IK", LoadPose = "Load pose";
        // item
        public const string Item = "Item", ItemColour = "Item colour", ItemPattern = "Item pattern", ItemAlpha = "Item alpha",
            ItemEmission = "Item emission", ItemPanel = "Item panel", ItemFk = "Item FK", ItemDynamicBones = "Item dynamic bones",
            ItemOptions = "Item options", ItemAnimation = "Item animation";
        // light / camera
        public const string Light = "Light", CharaLight = "Chara light", CameraSlot = "Camera slot", ActiveCamera = "Active camera",
            ReleaseCamera = "Release camera", Camera = "Camera";
        // scene
        public const string Map = "Map", MapPose = "Map pose", MapOptions = "Map options", MapLights = "Map lights",
            Effects = "Effects", ResetEffects = "Reset effects", Sky = "Sky", Sound = "Sound", Title = "Title";
        // files
        public const string ImportScene = "Import scene";
        // routes (Phase 3.6; StudioOps.Routes, package R)
        public const string AddRoute = "Add route", AddRoutePoint = "Add route point", DeleteRoutePoint = "Delete route point",
            Route = "Route", RoutePoint = "Route point";
        // scene images (Phase 3.6; StudioOps.SceneImages, package C)
        public const string Background = "Background", Frame = "Frame";
        // Maker edits (Phase 4.0 package B; StudioOps.Maker)
        public const string EditFace = "Edit face", EditBody = "Edit body", EditHair = "Edit hair", EditClothes = "Edit clothes",
            EditAccessory = "Edit accessory", RenameCharacter = "Rename character", EditCharacter = "Edit character", ChangeItem = "Change item",
            FacePreset = "Face preset", AddAccessory = "Add accessory", RemoveAccessory = "Remove accessory",
            ReorderAccessories = "Reorder accessories", CopyAccessory = "Copy accessory";
        // Phase 4.1 package B: the per-row reset to the reference value
        public const string ResetValue = "Reset value";
        // Phase 4.4 package B: a coordinate worn from a coordinate card or file (StudioOps.ApplyCoordinate, every mode)
        public const string LoadCoordinate = "Load coordinate";
        // Phase 4.5 package B: an accessory moved to another build slot (StudioOps.MoveAccessoryToSlot, bridge only)
        public const string MoveAccessory = "Move accessory";
        // Phase 4.6 package B: a character load with the five flags (StudioOps.LoadCharacter)
        public const string LoadCharacter = "Load character";
        // Phase 6.3 package C: advanced pose (StudioOps.AdvancedPose)
        public const string BoneCorrection = "Bone correction", ClearBoneCorrections = "Clear bone corrections", JiggleValues = "Jiggle values",
            ClearJiggleValues = "Clear jiggle values", BlendShapes = "Blend shapes", ClearBlendShapes = "Clear blend shapes";
        // Phase 6.4 package C: material edits (StudioOps.Materials)
        public const string MaterialEdit = "Material edit", MaterialReset = "Reset material edit", MaterialClear = "Clear material edits",
            MaterialTexture = "Material texture", MaterialShader = "Material shader", HairBoneAdjust = "Hair bone adjustment";
        // Phase 6.5: constraints
        public const string ConstraintAdd = "Add constraint", Constraint = "Constraint", ConstraintRemove = "Remove constraint", ConstraintClear = "Clear constraints",
            ConstraintReorder = "Reorder constraints", ConstraintTakeOffset = "Take offset";
        // Phase 6.6: stains
        public const string WetAdd = "Add stain", WetRemove = "Remove stain", WetEdit = "Stain", WetKeep = "Keep layer", WetStart = "Start stain", WetDry = "Dry stain",
            WetClearPuddles = "Clear puddles", WetFloor = "Floor", WetOrigin = "Move origin";
        // Phase 6.7: ragdolls
        public const string RagdollBuild = "Ragdoll", RagdollRelease = "Release ragdoll", RagdollPin = "Pin", RagdollUnpin = "Unpin", RagdollTie = "Tie", RagdollHold = "Hold",
            RagdollSettings = "Ragdoll settings", RagdollPusher = "Pusher", RagdollObstacle = "Obstacle", RagdollBake = "Bake pose to FK", RagdollFollower = "Follower";
        // Phase 6.8: bakes and cuts
        public const string BakeMake = "Bake to item", BakeRename = "Rename bake", CutMake = "Cut", CutEdit = "Edit cut", CutMoveMarker = "Move marker";
        // Phase 6.9: scene scripts
        public const string ScriptAdd = "Add script", ScriptEdit = "Edit script", ScriptRemove = "Remove script", ScriptCast = "Cast roles", ScriptLoad = "Load script",
            ScriptPrepare = "Prepare script", ScriptCapture = "Save edits into script", ScriptBorrow = "Borrow";

        /// <summary>Every label (for the schema test and B's parity check).</summary>
        public static readonly IReadOnlyList<string> All = new[]
        {
            AddCharacter, AddItem, AddLight, AddFolder, AddCamera, Delete, Duplicate, Parent, Reorder, Visibility, Rename, CopyTransform,
            Transform, ResetTransform, MoveToPivot,
            Animation, AnimationSpeed, AnimationOptions, Face, Hands, Look, LookTarget, Clothes, Accessories, CharacterState,
            JointCorrection, ChangeCharacter, UseCardState,
            Fk, FkBones, FkFromAnimation, FkFromPose, Ik, IkTargets, IkFromAnimation, IkFromPose, ConvertIk, BakeIk, LoadPose,
            Item, ItemColour, ItemPattern, ItemAlpha, ItemEmission, ItemPanel, ItemFk, ItemDynamicBones, ItemOptions, ItemAnimation,
            Light, CharaLight, CameraSlot, ActiveCamera, ReleaseCamera, Camera,
            Map, MapPose, MapOptions, MapLights, Effects, ResetEffects, Sky, Sound, Title,
            ImportScene,
            AddRoute, AddRoutePoint, DeleteRoutePoint, Route, RoutePoint, Background, Frame,
            EditFace, EditBody, EditHair, EditClothes, EditAccessory, RenameCharacter, EditCharacter, ChangeItem, FacePreset,
            AddAccessory, RemoveAccessory, ReorderAccessories, CopyAccessory,
            ResetValue, LoadCoordinate, MoveAccessory, LoadCharacter,
            BoneCorrection, ClearBoneCorrections, JiggleValues, ClearJiggleValues, BlendShapes, ClearBlendShapes,
            MaterialEdit, MaterialReset, MaterialClear, MaterialTexture, MaterialShader, HairBoneAdjust,
            ConstraintAdd, Constraint, ConstraintRemove, ConstraintClear, ConstraintReorder, ConstraintTakeOffset,
            WetAdd, WetRemove, WetEdit, WetKeep, WetStart, WetDry, WetClearPuddles, WetFloor, WetOrigin,
            RagdollBuild, RagdollRelease, RagdollPin, RagdollUnpin, RagdollTie, RagdollHold, RagdollSettings, RagdollPusher, RagdollObstacle, RagdollBake, RagdollFollower,
            BakeMake, BakeRename, CutMake, CutEdit, CutMoveMarker,
            ScriptAdd, ScriptEdit, ScriptRemove, ScriptCast, ScriptLoad, ScriptPrepare, ScriptCapture, ScriptBorrow,
        };
    }
}
