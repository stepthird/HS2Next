// The channels of the runtime override layer: the model field paths a slot may stand for, in their canonical text, with
// the shape of the value, the node kinds that have the path and the model path whose changes touch the channel.
//   node channels   transform.pos, transform.rot, transform.scale, chara.fk.bones["<bone>"], chara.anime.ref,
//                   chara.anime.speed, chara.anime.time, chara.face.eyes, chara.face.mouth, chara.face.eyesOpen,
//                   chara.face.mouthOpen, item.colors[i].main (i 0-3), light.color,
//                   chara.bone["<address>"].pos / .rot / .scale (a bone by its canonical address),
//                   chara.ik.targets.<Name>.pos / .rot (rot on hands and feet only)
//   scene channels  camera (the view; model path camera.current), effects.depthOfField
// Unity-free (the scene suite compiles it with the model).
using System;
using System.Collections.Generic;
using HS2Next.Api.Plugins;
using HS2Next.Save;

namespace HS2Next.Scenes.Overrides
{
    /// <summary>What a slot stands for (one value of the scene model).</summary>
    public enum OverrideChannel
    {
        TransformPos, TransformRot, TransformScale, FkBone, AnimeRef, AnimeSpeed, AnimeTime,
        FaceEyes, FaceMouth, FaceEyesOpen, FaceMouthOpen, ItemColor, LightColor, Camera, DepthOfField,
        BonePos, BoneRot, BoneScale, IkPos, IkRot,
    }

    /// <summary>A parsed override path (immutable; one instance per canonical text).</summary>
    public sealed class OverridePath
    {
        /// <summary>The number of channels a node keeps in its fixed table (every channel but the FK bones and the item colours' extra slots).</summary>
        internal const int FixedChannels = 15;

        OverridePath(string text, OverrideChannel channel, OverrideValueKind kind, string bone, int index, string modelPath)
        {
            Text = text;
            Channel = channel;
            Kind = kind;
            Bone = bone;
            Index = index;
            ModelPath = modelPath ?? text;
        }

        /// <summary>The canonical text (a bone name as a JSON string, the colour index in decimal).</summary>
        public string Text { get; }
        public OverrideChannel Channel { get; }
        public OverrideValueKind Kind { get; }
        /// <summary>The FK bone's transform name (FkBone), a bone's canonical address (BonePos, BoneRot, BoneScale), else null.</summary>
        public string Bone { get; }
        /// <summary>The item colour slot 0-3 (ItemColor), the target's index in IkTargets.Names (IkPos, IkRot), else 0.</summary>
        public int Index { get; }
        /// <summary>camera and effects.depthOfField have no node.</summary>
        public bool SceneWide => Channel == OverrideChannel.Camera || Channel == OverrideChannel.DepthOfField;
        /// <summary>The model path whose changes touch the channel (node paths: the text; camera: camera.current; scene settings path).</summary>
        public string ModelPath { get; }
        /// <summary>A bone's channel (position, rotation or scale): it stands for no model path, and the channel lives under its text.</summary>
        public bool IsBone => Channel == OverrideChannel.BonePos || Channel == OverrideChannel.BoneRot || Channel == OverrideChannel.BoneScale;
        /// <summary>An IK target's channel.</summary>
        public bool IsIkTarget => Channel == OverrideChannel.IkPos || Channel == OverrideChannel.IkRot;

        /// <summary>True when a node of kind <paramref name="k"/> has the path.</summary>
        public bool Accepts(NodeKind k)
        {
            switch (Channel)
            {
                case OverrideChannel.TransformPos:
                case OverrideChannel.TransformRot:
                    return k == NodeKind.Chara || k == NodeKind.Item || k == NodeKind.Light || k == NodeKind.Folder || k == NodeKind.Route || k == NodeKind.Camera;
                case OverrideChannel.TransformScale:
                    return k == NodeKind.Chara || k == NodeKind.Item;
                case OverrideChannel.ItemColor:
                    return k == NodeKind.Item;
                case OverrideChannel.LightColor:
                    return k == NodeKind.Light;
                case OverrideChannel.Camera:
                case OverrideChannel.DepthOfField:
                    return false;
                default:
                    return k == NodeKind.Chara;
            }
        }

        /// <summary>The node kinds that have the path, for messages.</summary>
        public string KindsText
        {
            get
            {
                switch (Channel)
                {
                    case OverrideChannel.TransformPos:
                    case OverrideChannel.TransformRot: return "a node with a transform (chara, item, light, folder, route, camera)";
                    case OverrideChannel.TransformScale: return "a chara or an item";
                    case OverrideChannel.ItemColor: return "an item";
                    case OverrideChannel.LightColor: return "a light";
                    case OverrideChannel.Camera:
                    case OverrideChannel.DepthOfField: return "the scene (no node id)";
                    default: return "a chara";
                }
            }
        }

        /// <summary>The value forms of the kind, for messages.</summary>
        public static string FormsOf(OverrideValueKind k)
        {
            switch (k)
            {
                case OverrideValueKind.Number: return "a number: Set(v), [v] or a JSON number";
                case OverrideValueKind.Integer: return "a whole number: Set(v), [v] or a JSON number";
                case OverrideValueKind.Vector3: return "x, y, z: Set(x, y, z), [x, y, z]";
                case OverrideValueKind.Rotation: return "Euler degrees Set(x, y, z) / [x, y, z], or a quaternion Set(x, y, z, w) / [x, y, z, w]";
                case OverrideValueKind.Color: return "r, g, b(, a): Set(r, g, b), Set(r, g, b, a), [r, g, b(, a)]";
                case OverrideValueKind.Camera: return "{\"target\": [x, y, z], \"rot\": [x, y, z], \"dist\": d, \"fov\": f} or 8 numbers (target, rot, dist, fov)";
                case OverrideValueKind.DepthOfField: return "{\"enabled\", \"focus\", \"focalSize\", \"aperture\", \"blur\"?, \"focusTo\"?, \"focusBlend\"?} or 7 numbers (enabled, focalSize, aperture, blur, focus point x, y, z)";
                case OverrideValueKind.Animation: return "{\"source\"?, \"group\", \"category\", \"no\"} or Set(group, category, no)";
                default: return k.ToString();
            }
        }

        // ------------------------------------------------------------ parse

        static readonly Dictionary<string, OverridePath> cache = new Dictionary<string, OverridePath>(StringComparer.Ordinal);
        static readonly object gate = new object();

        static readonly string[] Simple =
        {
            "transform.pos", "transform.rot", "transform.scale", "chara.anime.ref", "chara.anime.speed", "chara.anime.time",
            "chara.face.eyes", "chara.face.mouth", "chara.face.eyesOpen", "chara.face.mouthOpen", "light.color", "camera", "effects.depthOfField",
        };

        /// <summary>The paths of 6.2 as their canonical texts (FK bones and item colours as patterns).</summary>
        public static readonly string[] Known =
        {
            "transform.pos", "transform.rot", "transform.scale", "chara.fk.bones[\"<bone>\"]", "chara.anime.ref", "chara.anime.speed",
            "chara.anime.time", "chara.face.eyes", "chara.face.mouth", "chara.face.eyesOpen", "chara.face.mouthOpen", "item.colors[0..3].main",
            "light.color", "camera", "effects.depthOfField", "chara.bone[\"<address>\"].pos", "chara.bone[\"<address>\"].rot", "chara.bone[\"<address>\"].scale",
            "chara.ik.targets.<Name>.pos", "chara.ik.targets.<Name>.rot",
        };

        /// <summary>The path text of a bone's channel: <paramref name="member"/> is pos, rot or scale.</summary>
        public static string BoneText(string address, string member) => "chara.bone[" + FieldPath.Quote(address) + "]." + member;

        /// <summary>The path text of an IK target's channel: <paramref name="member"/> is pos or rot.</summary>
        public static string IkText(string name, string member) => "chara.ik.targets." + name + "." + member;

        /// <summary>Parses <paramref name="text"/>; false with the reason for a text that is no override path.</summary>
        public static bool TryParse(string text, out OverridePath path, out string error)
        {
            path = null;
            error = null;
            if (text == null) { error = "the path is null"; return false; }
            lock (gate)
                if (cache.TryGetValue(text, out path)) return true;
            if (!FieldPath.TryParse(text, out var fp, out string fe))
            {
                error = "\"" + text + "\" is not a field path (" + fe + ")";
                return false;
            }
            string c = fp.Text;
            OverridePath p = null;
            switch (c)
            {
                case "transform.pos": p = new OverridePath(c, OverrideChannel.TransformPos, OverrideValueKind.Vector3, null, 0, null); break;
                case "transform.rot": p = new OverridePath(c, OverrideChannel.TransformRot, OverrideValueKind.Rotation, null, 0, null); break;
                case "transform.scale": p = new OverridePath(c, OverrideChannel.TransformScale, OverrideValueKind.Vector3, null, 0, null); break;
                case "chara.anime.ref": p = new OverridePath(c, OverrideChannel.AnimeRef, OverrideValueKind.Animation, null, 0, null); break;
                case "chara.anime.speed": p = new OverridePath(c, OverrideChannel.AnimeSpeed, OverrideValueKind.Number, null, 0, null); break;
                case "chara.anime.time": p = new OverridePath(c, OverrideChannel.AnimeTime, OverrideValueKind.Number, null, 0, null); break;
                case "chara.face.eyes": p = new OverridePath(c, OverrideChannel.FaceEyes, OverrideValueKind.Integer, null, 0, null); break;
                case "chara.face.mouth": p = new OverridePath(c, OverrideChannel.FaceMouth, OverrideValueKind.Integer, null, 0, null); break;
                case "chara.face.eyesOpen": p = new OverridePath(c, OverrideChannel.FaceEyesOpen, OverrideValueKind.Number, null, 0, null); break;
                case "chara.face.mouthOpen": p = new OverridePath(c, OverrideChannel.FaceMouthOpen, OverrideValueKind.Number, null, 0, null); break;
                case "light.color": p = new OverridePath(c, OverrideChannel.LightColor, OverrideValueKind.Color, null, 0, null); break;
                case "camera": p = new OverridePath(c, OverrideChannel.Camera, OverrideValueKind.Camera, null, 0, "camera.current"); break;
                case "effects.depthOfField": p = new OverridePath(c, OverrideChannel.DepthOfField, OverrideValueKind.DepthOfField, null, 0, null); break;
                default:
                    // chara.fk.bones["<bone>"]
                    if (fp.Count == 4 && fp[0].Kind == FieldStepKind.Name && fp[0].Name == "chara" && fp[1].Kind == FieldStepKind.Name && fp[1].Name == "fk" &&
                        fp[2].Kind == FieldStepKind.Name && fp[2].Name == "bones" && fp[3].Kind == FieldStepKind.Key)
                    {
                        if (string.IsNullOrEmpty(fp[3].Name)) { error = "an FK bone path needs a bone name: chara.fk.bones[\"<bone>\"]"; return false; }
                        p = new OverridePath(c, OverrideChannel.FkBone, OverrideValueKind.Rotation, fp[3].Name, 0, null);
                    }
                    // item.colors[i].main
                    else if (fp.Count == 4 && fp[0].Kind == FieldStepKind.Name && fp[0].Name == "item" && fp[1].Kind == FieldStepKind.Name && fp[1].Name == "colors" &&
                             fp[2].Kind == FieldStepKind.Index && fp[3].Kind == FieldStepKind.Name && fp[3].Name == "main")
                    {
                        if (fp[2].Index > 3) { error = "item colour slots are 0 to 3: item.colors[i].main"; return false; }
                        p = new OverridePath(c, OverrideChannel.ItemColor, OverrideValueKind.Color, null, fp[2].Index, null);
                    }
                    // chara.bone["<address>"].pos / .rot / .scale
                    else if (fp.Count == 4 && fp[0].Kind == FieldStepKind.Name && fp[0].Name == "chara" && fp[1].Kind == FieldStepKind.Name && fp[1].Name == "bone" &&
                             fp[2].Kind == FieldStepKind.Key && fp[3].Kind == FieldStepKind.Name)
                    {
                        if (!PoseAddressGrammar.TryParse(PoseAddressForm.Bone, fp[2].Name, out _, out _, out _, out _, out _, out string ae) || !PoseAddress.IsCanonical(PoseAddressForm.Bone, fp[2].Name))
                        {
                            error = "\"" + fp[2].Name + "\" is not a canonical bone address" + (ae != null ? " (" + ae + ")" : "");
                            return false;
                        }
                        switch (fp[3].Name)
                        {
                            case "pos": p = new OverridePath(c, OverrideChannel.BonePos, OverrideValueKind.Vector3, fp[2].Name, 0, null); break;
                            case "rot": p = new OverridePath(c, OverrideChannel.BoneRot, OverrideValueKind.Rotation, fp[2].Name, 0, null); break;
                            case "scale": p = new OverridePath(c, OverrideChannel.BoneScale, OverrideValueKind.Vector3, fp[2].Name, 0, null); break;
                        }
                    }
                    // chara.ik.targets.<Name>.pos / .rot
                    else if (fp.Count == 5 && fp[0].Kind == FieldStepKind.Name && fp[0].Name == "chara" && fp[1].Kind == FieldStepKind.Name && fp[1].Name == "ik" &&
                             fp[2].Kind == FieldStepKind.Name && fp[2].Name == "targets" && fp[3].Kind == FieldStepKind.Name && fp[4].Kind == FieldStepKind.Name)
                    {
                        int target = Array.IndexOf(IkTargets.Names, fp[3].Name);
                        if (target < 0) { error = "\"" + fp[3].Name + "\" is not an IK target (" + string.Join(", ", IkTargets.Names) + ")"; return false; }
                        if (fp[4].Name == "pos") p = new OverridePath(c, OverrideChannel.IkPos, OverrideValueKind.Vector3, null, target, null);
                        else if (fp[4].Name == "rot")
                        {
                            if (!ConstraintEntry.IkTakesRotation(fp[3].Name)) { error = "IK target " + fp[3].Name + " has no rotation: hands and feet only"; return false; }
                            p = new OverridePath(c, OverrideChannel.IkRot, OverrideValueKind.Rotation, null, target, null);
                        }
                    }
                    break;
            }
            if (p == null)
            {
                error = "\"" + text + "\" is not a runtime override path (" + string.Join(", ", Known) + ")";
                return false;
            }
            lock (gate)
            {
                if (cache.TryGetValue(c, out var have)) p = have;
                else if (cache.Count < 4096) cache[c] = p;
                if (cache.Count < 4096) cache[text] = p;
            }
            path = p;
            return true;
        }

        /// <summary>Parses <paramref name="text"/>; ArgumentException naming the path for one that is not an override path.</summary>
        public static OverridePath Parse(string text) =>
            TryParse(text, out var p, out string e) ? p : throw new ArgumentException(e, "path");

        /// <summary>The path of a channel (FkBone needs the bone, ItemColor the slot).</summary>
        public static OverridePath Of(OverrideChannel ch, string bone = null, int index = 0)
        {
            switch (ch)
            {
                case OverrideChannel.FkBone: return Parse("chara.fk.bones[" + FieldPath.Quote(bone ?? "") + "]");
                case OverrideChannel.ItemColor: return Parse("item.colors[" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + "].main");
                default: return Parse(Simple[SimpleIndex(ch)]);
            }
        }

        static int SimpleIndex(OverrideChannel ch)
        {
            switch (ch)
            {
                case OverrideChannel.TransformPos: return 0;
                case OverrideChannel.TransformRot: return 1;
                case OverrideChannel.TransformScale: return 2;
                case OverrideChannel.AnimeRef: return 3;
                case OverrideChannel.AnimeSpeed: return 4;
                case OverrideChannel.AnimeTime: return 5;
                case OverrideChannel.FaceEyes: return 6;
                case OverrideChannel.FaceMouth: return 7;
                case OverrideChannel.FaceEyesOpen: return 8;
                case OverrideChannel.FaceMouthOpen: return 9;
                case OverrideChannel.LightColor: return 10;
                case OverrideChannel.Camera: return 11;
                case OverrideChannel.DepthOfField: return 12;
                default: throw new ArgumentOutOfRangeException(nameof(ch));
            }
        }

        /// <summary>
        /// True when a model change at <paramref name="changePath"/> (a node path, or for the scene channels a path of the scene
        /// object) touches this channel: one of the two paths is a prefix of the other at a step boundary.
        /// </summary>
        public bool TouchedBy(string changePath) => !IsBone && (changePath == null || FieldPath.Overlaps(changePath, ModelPath));

        public override string ToString() => Text;
    }
}
