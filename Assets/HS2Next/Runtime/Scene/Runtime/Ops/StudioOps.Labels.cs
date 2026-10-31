// The history labels of StudioOps, one table used by the UI and the bridge handlers.
// Unity-free and self-contained: package P mirrors these constants for the mono schema check, so the
// values here are the contract: the labels of the FK / IK / pose operations and the item
// labels (the item.set default per first path).
namespace HS2Next.Scenes.Runtime
{
    public sealed partial class StudioOps
    {
        /// <summary>History labels.</summary>
        public static class Labels
        {
            // tree
            public const string AddCharacter = "Add character";
            public const string AddItem = "Add item";
            public const string AddLight = "Add light";
            public const string AddFolder = "Add folder";
            public const string AddCamera = "Add camera";
            public const string Delete = "Delete";
            public const string Duplicate = "Duplicate";
            public const string Parent = "Parent";
            public const string Reorder = "Reorder";
            public const string Visibility = "Visibility";
            public const string Rename = "Rename";
            public const string CopyTransform = "Copy transform";
            // transform
            public const string Transform = "Transform";
            public const string ResetTransform = "Reset transform";
            public const string MoveToPivot = "Move to pivot";
            // chara
            public const string Animation = "Animation";
            public const string AnimationSpeed = "Animation speed";
            public const string AnimationOptions = "Animation options";
            public const string Face = "Face";
            public const string Hands = "Hands";
            public const string Look = "Look";
            public const string LookTarget = "Look target";
            public const string Clothes = "Clothes";
            public const string Accessories = "Accessories";
            public const string CharacterState = "Character state";
            public const string JointCorrection = "Joint correction";
            public const string ChangeCharacter = "Change character";
            public const string UseCardState = "Use card state";
            // FK / IK / pose
            public const string Fk = "FK";
            public const string FkBones = "FK bones";
            public const string FkFromAnimation = "FK from animation";
            public const string FkFromPose = "FK from pose";
            public const string Ik = "IK";
            public const string IkTargets = "IK targets";
            public const string IkFromAnimation = "IK from animation";
            public const string IkFromPose = "IK from pose";
            public const string ConvertIk = "Convert IK";
            public const string BakeIk = "Bake IK";
            public const string LoadPose = "Load pose";
            // item (item.set's default by the first path)
            public const string Item = "Item";
            public const string ItemColour = "Item colour";
            public const string ItemPattern = "Item pattern";
            public const string ItemAlpha = "Item alpha";
            public const string ItemEmission = "Item emission";
            public const string ItemPanel = "Item panel";
            public const string ItemFk = "Item FK";
            public const string ItemDynamicBones = "Item dynamic bones";
            public const string ItemOptions = "Item options";
            public const string ItemAnimation = "Item animation";
            // light / camera
            public const string Light = "Light";
            public const string CharaLight = "Chara light";
            public const string CameraSlot = "Camera slot";
            public const string ActiveCamera = "Active camera";
            public const string ReleaseCamera = "Release camera";
            public const string Camera = "Camera";
            // scene
            public const string Map = "Map";
            public const string MapPose = "Map pose";
            public const string MapOptions = "Map options";
            public const string MapLights = "Map lights";
            public const string Effects = "Effects";
            public const string ResetEffects = "Reset effects";
            public const string Sky = "Sky";
            public const string Sound = "Sound";
            public const string Title = "Title";
            // scene images (Phase 3.6 package C)
            public const string Background = "Background";
            public const string Frame = "Frame";
            // routes (Phase 3.6 package R)
            public const string AddRoute = "Add route";
            public const string AddRoutePoint = "Add route point";
            public const string DeleteRoutePoint = "Delete route point";
            public const string Route = "Route";
            public const string RoutePoint = "Route point";
            // files
            public const string ImportScene = "Import scene";
            // Maker edits (Phase 4.0 package B; StudioOps.Maker)
            public const string EditFace = "Edit face";
            public const string EditBody = "Edit body";
            public const string EditHair = "Edit hair";
            public const string EditClothes = "Edit clothes";
            public const string EditAccessory = "Edit accessory";
            public const string RenameCharacter = "Rename character";
            public const string EditCharacter = "Edit character";
            public const string ChangeItem = "Change item";
            public const string FacePreset = "Face preset";
            public const string AddAccessory = "Add accessory";
            public const string RemoveAccessory = "Remove accessory";
            public const string ReorderAccessories = "Reorder accessories";
            public const string CopyAccessory = "Copy accessory";
            // Phase 4.1 package B: the per-row reset to the reference value (StudioOps.ResetAppearance)
            public const string ResetValue = "Reset value";
            // Phase 4.4 package B: a coordinate worn from a coordinate card or file (StudioOps.ApplyCoordinate, every mode)
            public const string LoadCoordinate = "Load coordinate";
            // Phase 4.5 package B: an accessory moved to another build slot (StudioOps.MoveAccessoryToSlot, bridge only)
            public const string MoveAccessory = "Move accessory";
            // Phase 4.6 package B: a character load with the five flags (StudioOps.LoadCharacter; all five = Change character's swap)
            public const string LoadCharacter = "Load character";

            /// <summary>Every label above, for the schema check and UiOpsTest.</summary>
            public static readonly string[] All =
            {
                AddCharacter, AddItem, AddLight, AddFolder, AddCamera, Delete, Duplicate, Parent, Reorder, Visibility, Rename, CopyTransform,
                Transform, ResetTransform, MoveToPivot,
                Animation, AnimationSpeed, AnimationOptions, Face, Hands, Look, LookTarget, Clothes, Accessories, CharacterState,
                JointCorrection, ChangeCharacter, UseCardState,
                Fk, FkBones, FkFromAnimation, FkFromPose, Ik, IkTargets, IkFromAnimation, IkFromPose, ConvertIk, BakeIk, LoadPose,
                Item, ItemColour, ItemPattern, ItemAlpha, ItemEmission, ItemPanel, ItemFk, ItemDynamicBones, ItemOptions, ItemAnimation,
                Light, CharaLight, CameraSlot, ActiveCamera, ReleaseCamera, Camera,
                Map, MapPose, MapOptions, MapLights, Effects, ResetEffects, Sky, Sound, Title,
                Background, Frame,
                AddRoute, AddRoutePoint, DeleteRoutePoint, Route, RoutePoint,
                ImportScene,
                EditFace, EditBody, EditHair, EditClothes, EditAccessory, RenameCharacter, EditCharacter, ChangeItem, FacePreset,
                AddAccessory, RemoveAccessory, ReorderAccessories, CopyAccessory,
                ResetValue, LoadCoordinate, MoveAccessory, LoadCharacter,
            };

            /// <summary>The label of a Maker edit by its area (MakerEdits.AreaOf / AppearancePaths.Area).</summary>
            public static string ForAppearanceArea(string area)
            {
                switch (area)
                {
                    case "face": return EditFace;
                    case "body": return EditBody;
                    case "hair": return EditHair;
                    case "clothes": return EditClothes;
                    case "accessory": return EditAccessory;
                    case "name": return RenameCharacter;
                    default: return EditCharacter;
                }
            }

            /// <summary>item.set's default label by the first item-section path (relative to `item.`).</summary>
            public static string ForItemPath(string rel)
            {
                if (rel == null) return Item;
                if (rel.StartsWith("colors", System.StringComparison.Ordinal)) return rel.Contains(".pattern") ? ItemPattern : ItemColour;
                if (rel.StartsWith("alpha", System.StringComparison.Ordinal)) return ItemAlpha;
                if (rel.StartsWith("emission", System.StringComparison.Ordinal) || rel.StartsWith("lightCancel", System.StringComparison.Ordinal)) return ItemEmission;
                if (rel.StartsWith("panel", System.StringComparison.Ordinal)) return ItemPanel;
                if (rel.StartsWith("fk", System.StringComparison.Ordinal)) return ItemFk;
                if (rel.StartsWith("dynamicBones", System.StringComparison.Ordinal)) return ItemDynamicBones;
                if (rel.StartsWith("options", System.StringComparison.Ordinal)) return ItemOptions;
                if (rel.StartsWith("anime", System.StringComparison.Ordinal)) return ItemAnimation;
                return Item;
            }
        }
    }
}
