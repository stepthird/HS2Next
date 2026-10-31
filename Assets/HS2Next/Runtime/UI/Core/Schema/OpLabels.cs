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
        };
    }
}
