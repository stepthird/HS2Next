// The Timeline's track types: their stored names, the node kinds they take, the kind of value they hold, whether their
// values blend, the runtime channel they drive (a model field path of the node, or a scene-wide channel) and the model
// path whose edits suspend that channel while the Timeline is paused.
using System;

namespace HS2Next.Scenes.Timeline
{
    /// <summary>
    /// Track types. <see cref="Bone"/> is a stored <c>rot</c> track with a bone (an FK bone rotation); <see cref="Color"/>
    /// drives an item's colour slot when the track has a slot, else a light's colour.
    /// </summary>
    public enum TimelineTrackType
    {
        Unknown = 0, Camera, Dof, Pos, Rot, Bone, Scale, Anime, AnimeSpeed, AnimeTime, Eyes, Mouth, EyesOpen, MouthOpen, Color,
    }

    /// <summary>The value a track hands to its channel (numbered as the plugin interface's override value kinds).</summary>
    public enum TimelineValueKind { Number = 0, Integer = 1, Vector3 = 2, Rotation = 3, Color = 4, Camera = 5, DepthOfField = 6, Animation = 7 }

    public static class TimelineTrackTypes
    {
        /// <summary>The stored type names (a bone rotation is stored as <c>rot</c> with a bone).</summary>
        public static readonly string[] StoredNames =
        {
            "camera", "dof", "pos", "rot", "scale", "anime", "animeSpeed", "animeTime", "eyes", "mouth", "eyesOpen", "mouthOpen", "color",
        };

        /// <summary>Highest item colour slot a <c>color</c> track may name.</summary>
        public const int MaxColorSlot = 3;

        /// <summary>The type of a stored name and bone; <see cref="TimelineTrackType.Unknown"/> for another name.</summary>
        public static TimelineTrackType FromStored(string type, string bone)
        {
            switch (type)
            {
                case "camera": return TimelineTrackType.Camera;
                case "dof": return TimelineTrackType.Dof;
                case "pos": return TimelineTrackType.Pos;
                case "rot": return bone != null ? TimelineTrackType.Bone : TimelineTrackType.Rot;
                case "scale": return TimelineTrackType.Scale;
                case "anime": return TimelineTrackType.Anime;
                case "animeSpeed": return TimelineTrackType.AnimeSpeed;
                case "animeTime": return TimelineTrackType.AnimeTime;
                case "eyes": return TimelineTrackType.Eyes;
                case "mouth": return TimelineTrackType.Mouth;
                case "eyesOpen": return TimelineTrackType.EyesOpen;
                case "mouthOpen": return TimelineTrackType.MouthOpen;
                case "color": return TimelineTrackType.Color;
                default: return TimelineTrackType.Unknown;
            }
        }

        /// <summary>True for a stored type name of this version (with or without a bone).</summary>
        public static bool IsKnownName(string type) => Array.IndexOf(StoredNames, type) >= 0;

        /// <summary>The stored name of a type (<c>rot</c> for a bone rotation; null for an unknown type).</summary>
        public static string StoredName(TimelineTrackType t)
        {
            switch (t)
            {
                case TimelineTrackType.Camera: return "camera";
                case TimelineTrackType.Dof: return "dof";
                case TimelineTrackType.Pos: return "pos";
                case TimelineTrackType.Rot:
                case TimelineTrackType.Bone: return "rot";
                case TimelineTrackType.Scale: return "scale";
                case TimelineTrackType.Anime: return "anime";
                case TimelineTrackType.AnimeSpeed: return "animeSpeed";
                case TimelineTrackType.AnimeTime: return "animeTime";
                case TimelineTrackType.Eyes: return "eyes";
                case TimelineTrackType.Mouth: return "mouth";
                case TimelineTrackType.EyesOpen: return "eyesOpen";
                case TimelineTrackType.MouthOpen: return "mouthOpen";
                case TimelineTrackType.Color: return "color";
                default: return null;
            }
        }

        public static TimelineValueKind ValueKind(TimelineTrackType t)
        {
            switch (t)
            {
                case TimelineTrackType.Camera: return TimelineValueKind.Camera;
                case TimelineTrackType.Dof: return TimelineValueKind.DepthOfField;
                case TimelineTrackType.Pos:
                case TimelineTrackType.Scale: return TimelineValueKind.Vector3;
                case TimelineTrackType.Rot:
                case TimelineTrackType.Bone: return TimelineValueKind.Rotation;
                case TimelineTrackType.Anime: return TimelineValueKind.Animation;
                case TimelineTrackType.Eyes:
                case TimelineTrackType.Mouth: return TimelineValueKind.Integer;
                case TimelineTrackType.Color: return TimelineValueKind.Color;
                default: return TimelineValueKind.Number;
            }
        }

        /// <summary>True for the types whose values do not blend (they evaluate as step whatever the key's ease).</summary>
        public static bool StepsOnly(TimelineTrackType t) => t == TimelineTrackType.Anime || t == TimelineTrackType.Eyes || t == TimelineTrackType.Mouth;

        /// <summary>True for the scene-wide types (no node).</summary>
        public static bool IsSceneWide(TimelineTrackType t) => t == TimelineTrackType.Camera || t == TimelineTrackType.Dof;

        /// <summary>
        /// True when a track of type <paramref name="t"/> may drive a node of kind <paramref name="k"/>; for
        /// <see cref="TimelineTrackType.Color"/> an item needs a slot and a light none (<paramref name="hasSlot"/>).
        /// </summary>
        public static bool Takes(TimelineTrackType t, NodeKind k, bool hasSlot = false)
        {
            switch (t)
            {
                case TimelineTrackType.Pos:
                case TimelineTrackType.Rot:
                    return k == NodeKind.Chara || k == NodeKind.Item || k == NodeKind.Light || k == NodeKind.Folder || k == NodeKind.Route || k == NodeKind.Camera;
                case TimelineTrackType.Scale: return k == NodeKind.Chara || k == NodeKind.Item;
                case TimelineTrackType.Color: return hasSlot ? k == NodeKind.Item : k == NodeKind.Light;
                case TimelineTrackType.Bone:
                case TimelineTrackType.Anime:
                case TimelineTrackType.AnimeSpeed:
                case TimelineTrackType.AnimeTime:
                case TimelineTrackType.Eyes:
                case TimelineTrackType.Mouth:
                case TimelineTrackType.EyesOpen:
                case TimelineTrackType.MouthOpen:
                    return k == NodeKind.Chara;
                default: return false;
            }
        }

        /// <summary>The node kinds a type takes, as text for an error reply.</summary>
        public static string KindsText(TimelineTrackType t, bool hasSlot = false)
        {
            switch (t)
            {
                case TimelineTrackType.Pos:
                case TimelineTrackType.Rot: return "a node with a transform (character, item, light, folder, route or camera)";
                case TimelineTrackType.Scale: return "a character or an item";
                case TimelineTrackType.Color: return hasSlot ? "an item (a colour slot 0-3)" : "a light (or an item with a slot 0-3)";
                case TimelineTrackType.Camera:
                case TimelineTrackType.Dof: return "no node";
                default: return "a character";
            }
        }

        /// <summary>
        /// The runtime channel's path: the node's model field path, or <c>camera</c> / <c>effects.depthOfField</c> for the
        /// scene-wide types; null for an unknown type or a missing bone / slot.
        /// </summary>
        public static string ChannelPath(TimelineTrackType t, string bone, int? slot)
        {
            switch (t)
            {
                case TimelineTrackType.Camera: return "camera";
                case TimelineTrackType.Dof: return "effects.depthOfField";
                case TimelineTrackType.Pos: return "transform.pos";
                case TimelineTrackType.Rot: return "transform.rot";
                case TimelineTrackType.Scale: return "transform.scale";
                case TimelineTrackType.Bone: return bone == null ? null : "chara.fk.bones[" + FieldPath.Quote(bone) + "]";
                case TimelineTrackType.Anime: return "chara.anime.ref";
                case TimelineTrackType.AnimeSpeed: return "chara.anime.speed";
                case TimelineTrackType.AnimeTime: return "chara.anime.time";
                case TimelineTrackType.Eyes: return "chara.face.eyes";
                case TimelineTrackType.Mouth: return "chara.face.mouth";
                case TimelineTrackType.EyesOpen: return "chara.face.eyesOpen";
                case TimelineTrackType.MouthOpen: return "chara.face.mouthOpen";
                case TimelineTrackType.Color:
                    if (slot == null) return "light.color";
                    return slot.Value >= 0 && slot.Value <= MaxColorSlot ? "item.colors[" + slot.Value + "].main" : null;
                default: return null;
            }
        }

        /// <summary>
        /// The model path whose edits suspend the channel while the Timeline is paused or ended: the channel's own path
        /// for node channels (an item colour slot as a whole), <c>camera.current</c> and <c>effects.depthOfField</c> of
        /// the scene object for the scene-wide types.
        /// </summary>
        public static string ModelPath(TimelineTrackType t, string bone, int? slot)
        {
            switch (t)
            {
                case TimelineTrackType.Camera: return "camera.current";
                case TimelineTrackType.Color:
                    if (slot == null) return "light.color";
                    return slot.Value >= 0 && slot.Value <= MaxColorSlot ? "item.colors[" + slot.Value + "]" : null;
                default: return ChannelPath(t, bone, slot);
            }
        }
    }
}
