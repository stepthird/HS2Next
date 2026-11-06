// The Timeline playback's value read-outs: the value a key made now would store (the winning override's value of the track's
// channel when one shows, else the scene's value), the bake plan (the values the Timeline's slots show, as model writes), and the
// marks of a bone track (the bone missing on the built character, the FK pose not writing it now).
using System;
using System.Collections.Generic;
using HS2Next.Save;
using HS2Next.Scenes.Timeline;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>One model write of the bake: (node or null for the scene, field path, value); <see cref="IsView"/> for the camera
    /// (view state, written without history); <see cref="Values"/> the Timeline values it carries.</summary>
    public sealed class TimelineBakeWrite
    {
        public string NodeId;
        public string Path;
        public object Value;
        public bool IsView;
        public int Values = 1;

        public override string ToString() => (NodeId ?? "scene") + " " + Path;
    }

    public sealed partial class TimelinePlayback
    {
        // ------------------------------------------------------------ the shown value

        /// <summary>
        /// A key (time 0, ease inout) holding the value <paramref name="track"/>'s channel shows now: the winning override's value
        /// when one shows (the Timeline's or another owner's), else the scene's value. Null with <paramref name="why"/> when it
        /// cannot be read (a target not in the scene, a bone without a value).
        /// </summary>
        public TimelineKey ShownKey(TimelineTrack track, out string why)
        {
            why = null;
            if (track == null || !track.IsKnownType)
            {
                why = "the track has no known type";
                return null;
            }
            var type = TimelineTrackTypes.FromStored(track.Type, track.Bone);
            string path = TimelineTrackTypes.ChannelPath(type, track.Bone, track.Slot);
            TreeNode n = null;
            if (!TimelineTrackTypes.IsSceneWide(type))
            {
                n = rt?.Model.Find(track.Key);
                if (n == null)
                {
                    why = "its node is not in the scene";
                    return null;
                }
                if (!TimelineTrackTypes.Takes(type, n.Kind, track.Slot.HasValue))
                {
                    why = "its node is of another kind";
                    return null;
                }
            }
            if (TryWinnerKey(track.Key, path, type, out var won)) return won;
            return ModelKey(type, track, n, out why);
        }

        TimelineKey ModelKey(TimelineTrackType type, TimelineTrack track, TreeNode n, out string why)
        {
            why = null;
            var model = rt.Model;
            var tf = n?.Doc.Transform ?? new NodeTransform();
            var chara = n?.State as CharaNodeState;
            switch (type)
            {
                case TimelineTrackType.Pos: return TimelineKey.OfVector(0f, tf.Pos);
                case TimelineTrackType.Rot: return TimelineKey.OfVector(0f, tf.Rot);
                case TimelineTrackType.Scale: return TimelineKey.OfVector(0f, tf.Scale);
                case TimelineTrackType.Bone:
                {
                    if (chara != null && chara.Fk.Bones.TryGetValue(track.Bone, out var e)) return TimelineKey.OfVector(0f, e);
                    var b = FkBoneOf(n.Id, track.Bone);
                    if (b != null && b.Bone)
                    {
                        var le = b.Bone.localEulerAngles;
                        return TimelineKey.OfVector(0f, new Vec3(le.x, le.y, le.z));
                    }
                    why = "the bone " + track.Bone + " has no FK value and the character shows no such bone";
                    return null;
                }
                case TimelineTrackType.Anime:
                {
                    var a = chara?.Anime.Ref;
                    if (a == null)
                    {
                        why = "the character has no animation row";
                        return null;
                    }
                    return TimelineKey.OfAnime(0f, new AnimeRef(a.Source, a.Group, a.Category, a.No), TimelineEases.Step);
                }
                case TimelineTrackType.AnimeSpeed: return TimelineKey.OfNumber(0f, chara?.Anime.Speed ?? 1f);
                case TimelineTrackType.AnimeTime:
                {
                    var r = rt.FindChara(n.Id);
                    var anim = r != null && r.State == RealizationState.Built && !r.InGraveyard ? r.Animation : null;
                    float t = anim && anim.IsLoaded ? anim.NormalizedTime : chara?.Anime.Time ?? 0f;
                    return TimelineKey.OfNumber(0f, t);
                }
                case TimelineTrackType.Eyes: return TimelineKey.OfInteger(0f, chara?.Face.Eyes ?? 0);
                case TimelineTrackType.Mouth: return TimelineKey.OfInteger(0f, chara?.Face.Mouth ?? 0);
                case TimelineTrackType.EyesOpen: return TimelineKey.OfNumber(0f, chara?.Face.EyesOpen ?? 1f);
                case TimelineTrackType.MouthOpen: return TimelineKey.OfNumber(0f, chara?.Face.MouthOpen ?? 0f);
                case TimelineTrackType.Color:
                {
                    if (n.State is ItemNodeState item)
                    {
                        int s = track.Slot ?? 0;
                        if (s < 0 || s >= item.Colors.Length || item.Colors[s] == null)
                        {
                            why = "the item has no colour slot " + s;
                            return null;
                        }
                        return TimelineKey.OfColor(0f, item.Colors[s].Main);
                    }
                    if (n.State is LightNodeState light) return TimelineKey.OfColor(0f, light.Color);
                    why = "its node has no colour";
                    return null;
                }
                case TimelineTrackType.Camera:
                {
                    var v = model.Settings.Camera.Current;
                    var cam = v != null
                        ? new TimelineCamera(v.Pivot, v.Rot, v.Distance, v.Fov)
                        : new TimelineCamera(V(SceneViewCamera.DefaultPivot), V(SceneViewCamera.DefaultRot), SceneViewCamera.DefaultDistance, SceneViewCamera.DefaultFov);
                    return TimelineKey.OfCamera(0f, cam);
                }
                case TimelineTrackType.Dof:
                {
                    var d = model.Settings.Effects.DepthOfField;
                    var dof = new TimelineDof
                    {
                        On = d.Enabled,
                        Focus = d.Focus != null && JsonText.IsId(d.Focus) ? TimelineFocus.OfNode(d.Focus) : null,
                        Size = d.FocalSize,
                        Aperture = d.Aperture,
                        Blur = null,
                    };
                    return TimelineKey.OfDof(0f, dof);
                }
                default:
                    why = "the track type has no value";
                    return null;
            }
        }

        static Vec3 V(Vector3 v) => new Vec3(v.x, v.y, v.z);
        static Vec3 V(in TimelineVec3 v) => new Vec3((float)v.X, (float)v.Y, (float)v.Z);

        /// <summary>Unity's Euler angles (eulerAngles) of a quaternion.</summary>
        internal static Vec3 Euler(double x, double y, double z, double w)
        {
            var e = new Quaternion((float)x, (float)y, (float)z, (float)w).eulerAngles;
            return new Vec3(e.x, e.y, e.z);
        }

        static Color4 C(double r, double g, double b, double a) => new Color4((float)r, (float)g, (float)b, (float)a);

        /// <summary>The value of a channel as a key of its type (time 0).</summary>
        internal static TimelineKey KeyOf(TimelineTrackType type, Held h)
        {
            switch (type)
            {
                case TimelineTrackType.Pos:
                case TimelineTrackType.Scale: return TimelineKey.OfVector(0f, new Vec3((float)h.X, (float)h.Y, (float)h.Z));
                case TimelineTrackType.Rot:
                case TimelineTrackType.Bone: return TimelineKey.OfVector(0f, Euler(h.X, h.Y, h.Z, h.W));
                case TimelineTrackType.Anime: return TimelineKey.OfAnime(0f, new AnimeRef(h.Anime.Source, h.Anime.Group, h.Anime.Category, h.Anime.No), TimelineEases.Step);
                case TimelineTrackType.AnimeSpeed:
                case TimelineTrackType.AnimeTime:
                case TimelineTrackType.EyesOpen:
                case TimelineTrackType.MouthOpen: return TimelineKey.OfNumber(0f, (float)h.X);
                case TimelineTrackType.Eyes:
                case TimelineTrackType.Mouth: return TimelineKey.OfInteger(0f, (int)Math.Round(h.X));
                case TimelineTrackType.Color: return TimelineKey.OfColor(0f, C(h.X, h.Y, h.Z, h.W));
                case TimelineTrackType.Camera:
                {
                    var c = h.Camera;
                    var q = c.Rotation;
                    return TimelineKey.OfCamera(0f, new TimelineCamera(V(c.Target), Euler(q.X, q.Y, q.Z, q.W), (float)c.Dist, (float)c.Fov));
                }
                case TimelineTrackType.Dof:
                {
                    var d = h.Dof;
                    return TimelineKey.OfDof(0f, new TimelineDof
                    {
                        On = d.Enabled,
                        Focus = StoredFocus(d.Focus),
                        Size = (float)d.FocalSize,
                        Aperture = (float)d.Aperture,
                        Blur = d.HasBlur ? (float)d.Blur : (float?)null,
                    });
                }
                default: return null;
            }
        }

        static TimelineFocus StoredFocus(DofFocus f)
        {
            if (f == null) return null;
            switch (f.Kind)
            {
                case DofFocusKind.Node: return TimelineFocus.OfNode(f.NodeId, f.Bone, f.Offset.HasValue ? V(f.Offset.Value) : (Vec3?)null);
                case DofFocusKind.Point: return TimelineFocus.OfPoint(V(f.Point));
                case DofFocusKind.Dist: return TimelineFocus.OfDist((float)f.Dist);
                default: return null;
            }
        }

        // ------------------------------------------------------------ the bake

        /// <summary>
        /// The model writes of a bake: every Timeline channel that shows a value, as the scene's value (rotations as Unity's Euler
        /// angles; FK bones into the character's fk.bones; an animation row with the animation's normalized time now; the depth of
        /// field's node focus, other forms as the camera pivot with a warning, its blur dropped with a warning; the camera as the
        /// current view).
        /// </summary>
        public List<TimelineBakeWrite> BakePlan(List<string> warnings)
        {
            var plan = new List<TimelineBakeWrite>();
            var tl = player.Timeline;
            if (tl == null || rt == null) return plan;
            var bones = new Dictionary<string, SortedDictionary<string, Vec3>>(StringComparer.Ordinal);
            var boneCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            var boneOrder = new List<string>();
            foreach (var t in tl.Tracks)
            {
                var ch = t.Channel;
                if (ch == null || !ch.HasValue || !(ch.Tag is Held h) || !h.Has) continue;
                string id = t.NodeId;
                switch (t.Type)
                {
                    case TimelineTrackType.Pos: plan.Add(W(id, "transform.pos", new Vec3((float)h.X, (float)h.Y, (float)h.Z))); break;
                    case TimelineTrackType.Scale: plan.Add(W(id, "transform.scale", new Vec3((float)h.X, (float)h.Y, (float)h.Z))); break;
                    case TimelineTrackType.Rot: plan.Add(W(id, "transform.rot", Euler(h.X, h.Y, h.Z, h.W))); break;
                    case TimelineTrackType.Bone:
                    {
                        if (!bones.TryGetValue(id, out var map))
                        {
                            var st = rt.Model.Find(id)?.State as CharaNodeState;
                            map = st != null ? new SortedDictionary<string, Vec3>(st.Fk.Bones, StringComparer.Ordinal) : new SortedDictionary<string, Vec3>(StringComparer.Ordinal);
                            bones[id] = map;
                            boneCounts[id] = 0;
                            boneOrder.Add(id);
                        }
                        map[t.Bone] = Euler(h.X, h.Y, h.Z, h.W);
                        boneCounts[id]++;
                        break;
                    }
                    case TimelineTrackType.Anime:
                    {
                        plan.Add(W(id, "chara.anime.ref", new AnimeRef(h.Anime.Source, h.Anime.Group, h.Anime.Category, h.Anime.No)));
                        var r = rt.FindChara(id);
                        var anim = r != null && r.State == RealizationState.Built && !r.InGraveyard ? r.Animation : null;
                        if (anim && anim.IsLoaded) plan.Add(new TimelineBakeWrite { NodeId = id, Path = "chara.anime.time", Value = anim.NormalizedTime, Values = 0 });
                        break;
                    }
                    case TimelineTrackType.AnimeSpeed: plan.Add(W(id, "chara.anime.speed", (float)h.X)); break;
                    case TimelineTrackType.AnimeTime: plan.Add(W(id, "chara.anime.time", (float)h.X)); break;
                    case TimelineTrackType.Eyes: plan.Add(W(id, "chara.face.eyes", (int)Math.Round(h.X))); break;
                    case TimelineTrackType.Mouth: plan.Add(W(id, "chara.face.mouth", (int)Math.Round(h.X))); break;
                    case TimelineTrackType.EyesOpen: plan.Add(W(id, "chara.face.eyesOpen", (float)h.X)); break;
                    case TimelineTrackType.MouthOpen: plan.Add(W(id, "chara.face.mouthOpen", (float)h.X)); break;
                    case TimelineTrackType.Color:
                    {
                        var col = C(h.X, h.Y, h.Z, h.W);
                        var kind = rt.Model.Find(id)?.Kind;
                        if (kind == NodeKind.Item) plan.Add(W(id, "item.colors[" + (t.Slot ?? 0) + "].main", col));
                        else if (kind == NodeKind.Light) plan.Add(W(id, "light.color", col));
                        break;
                    }
                    case TimelineTrackType.Dof:
                    {
                        var d = h.Dof;
                        string focus = null;
                        if (d.FocusTo != null && !Equals(d.FocusTo, d.Focus))
                            warnings.Add("dof focus blend has no model form: the camera pivot is kept");
                        else if (d.Focus != null && d.Focus.Kind == DofFocusKind.Node && d.Focus.Bone == null && !d.Focus.Offset.HasValue) focus = d.Focus.NodeId;
                        else if (d.Focus != null && d.Focus.Kind != DofFocusKind.Pivot)
                            warnings.Add("dof focus " + d.Focus.ToJson() + " has no model form: the camera pivot is kept");
                        if (d.HasBlur) warnings.Add("dof blur has no scene member: it is dropped");
                        plan.Add(W(null, "effects.depthOfField.enabled", d.Enabled));
                        plan.Add(new TimelineBakeWrite { Path = "effects.depthOfField.focalSize", Value = (float)d.FocalSize, Values = 0 });
                        plan.Add(new TimelineBakeWrite { Path = "effects.depthOfField.aperture", Value = (float)d.Aperture, Values = 0 });
                        plan.Add(new TimelineBakeWrite { Path = "effects.depthOfField.focus", Value = focus, Values = 0 });
                        break;
                    }
                    case TimelineTrackType.Camera:
                    {
                        var c = h.Camera;
                        var q = c.Rotation;
                        var view = new CameraView { Pivot = V(c.Target), Rot = Euler(q.X, q.Y, q.Z, q.W), Distance = (float)c.Dist, Fov = (float)c.Fov };
                        plan.Add(new TimelineBakeWrite { Path = "camera.current", Value = view, IsView = true });
                        break;
                    }
                }
            }
            foreach (var id in boneOrder) plan.Add(new TimelineBakeWrite { NodeId = id, Path = "chara.fk.bones", Value = bones[id], Values = boneCounts[id] });
            return plan;
        }

        static TimelineBakeWrite W(string id, string path, object value) => new TimelineBakeWrite { NodeId = id, Path = path, Value = value };

        // ------------------------------------------------------------ bone marks

        HS2Next.Motion.Fk.FkBone FkBoneOf(string nodeId, string bone)
        {
            var r = rt?.FindChara(nodeId);
            if (r == null || r.State != RealizationState.Built || r.InGraveyard) return null;
            return FkDriver.Of(r)?.Table?.Find(bone);
        }

        /// <summary>
        /// A bone track's marks: <paramref name="missing"/> when the built character has no FK bone of that name; <paramref name="fkOff"/>
        /// when the FK pose does not write it now (FK off, or the bone's group off). Both false for a character that is not built.
        /// </summary>
        public void BoneMarks(string nodeId, string bone, out bool missing, out bool fkOff)
        {
            missing = fkOff = false;
            var r = rt?.FindChara(nodeId);
            if (r == null || r.State != RealizationState.Built || r.InGraveyard) return;
            var fk = FkDriver.Of(r);
            var b = fk?.Table?.Find(bone);
            if (b == null)
            {
                missing = true;
                return;
            }
            fkOff = !fk.Enabled || !fk.Applied(b.Group);
        }
    }
}
