// The Unity application of the runtime override layer (Phase 6.2): the scene runtime's
// IOverrideHost. When a channel's winner or winning value changes the value reaches Unity through the same write the model's
// apply makes:
//   transform.pos / rot / scale   SceneRuntime.ApplyTransform at once (it reads the winning slots; ScaleEnforcer the scale)
//   chara.fk.bones["b"]           the bone (FkBone, resolved by name on the character's FK table, again after every build or
//                                 rediscovery) carries the override; the FK stage writes it instead of the model value where it
//                                 writes the bone (FK on, the group on, the bone used, IK not owning it); written at once when
//                                 it applies; released: the model value at once (WriteIfApplied)
//   chara.anime.ref               loaded at the end of the next Apply (after the tick parts)
//   chara.anime.speed / .time     at once (CharaRealization.Overrides)
//   chara.face.*                  the face applied again at once with the overridden members
//   item.colors[i].main           the item's colour write at once (ItemRealization.WriteColors)
//   light.color                   the light's value write at once (LightRealization.ApplyValues)
//   camera                        SceneViewCamera.LateApply every late apply (released: the view returns)
//   effects.depthOfField          SceneEffects.ResolveDof every late apply
//   chara.bone["<address>"].*     the driven bone of that address (DrivenPoseDriver.Bind: resolved by name, the mosaic rule asked) carries the
//                                 value; nothing reaches Unity at Set: stage bones.driven writes it in the next step; released: the next step's
//                                 restore
//   chara.ik.targets.<Name>.*     IkStage.TargetOverrides at once, which the next solve reads; released: the target's own value at the next solve
// A character build re-applies its slots (CharaBuilt); every Apply re-binds the FK slots whose character got a new FK table, and a flush that
// re-resolved its parts re-binds the bone channels. The end of an Apply drops the driven-bone state that no value needs any more.
using System;
using System.Collections.Generic;
using HS2Next.Motion.AdvancedPose;
using HS2Next.Motion.Fk;
using HS2Next.Motion.Ik;
using HS2Next.Scenes.Overrides;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    public sealed class OverrideApplication : IOverrideHost
    {
        readonly SceneRuntime rt;
        readonly List<OverrideChannelState> fkChannels = new List<OverrideChannelState>();
        readonly List<CharaRealization> pendingRows = new List<CharaRealization>();
        readonly List<OverrideChannelState> boneChannels = new List<OverrideChannelState>();
        readonly List<DrivenCharacter> driven = new List<DrivenCharacter>();
        bool bonesStale;

        // a bone channel's binding; Entry null: bound to nothing (the character lacks the bone, or the mosaic rule names it)
        sealed class BoneBinding
        {
            public CharaRealization Chara;
            public DrivenBone Entry;
        }

        sealed class FkBinding
        {
            public CharaRealization Chara;
            public FkCharacter Fk;
            public FkBone Bone;
        }

        internal OverrideApplication(SceneRuntime runtime) { rt = runtime; }

        OverrideLayer Layer => rt.Overrides;

        // ------------------------------------------------------------ IOverrideHost

        public bool TryKindOf(string nodeId, out NodeKind kind)
        {
            var n = rt.Model.Find(nodeId);
            if (n != null) { kind = n.Kind; return true; }
            var r = rt.Find(nodeId);   // a deleted node waiting in the graveyard (an undo brings it back)
            if (r?.Node != null) { kind = r.Node.Kind; return true; }
            kind = default;
            return false;
        }

        public bool TargetExists(string nodeId)
        {
            var r = rt.Find(nodeId);
            return r != null && r.State == RealizationState.Built && r.HasHolder && rt.IsInModel(r) && !r.InGraveyard;
        }

        public IEnumerable<string> NodeOrder()
        {
            foreach (var n in rt.Model.PreOrder()) yield return n.Id;
        }

        public bool IsEffective(OverrideSlot w)
        {
            switch (w.ParsedPath.Channel)
            {
                case OverrideChannel.FkBone: return FkEffective(w.Channel);
                case OverrideChannel.BonePos:
                case OverrideChannel.BoneRot:
                case OverrideChannel.BoneScale: return BoneEffective(w.Channel);
                case OverrideChannel.IkPos:
                case OverrideChannel.IkRot: return IkEffective(w.Channel);
                case OverrideChannel.AnimeRef: return rt.FindChara(w.NodeId)?.AnimeRowEffective(w) ?? false;
                case OverrideChannel.DepthOfField: return rt.GetPart<SceneEffects>()?.DofOverrideResolved ?? true;
                default: return true;
            }
        }

        public void Apply(OverrideChannelState ch)
        {
            var p = ch.Path;
            switch (p.Channel)
            {
                case OverrideChannel.TransformPos:
                case OverrideChannel.TransformRot:
                case OverrideChannel.TransformScale:
                {
                    var r = rt.Find(ch.NodeId);
                    if (r != null && r.HasHolder && rt.IsInModel(r)) rt.ApplyTransform(r);
                    break;
                }
                case OverrideChannel.FkBone:
                    ApplyFk(ch);
                    break;
                case OverrideChannel.AnimeRef:
                    QueueRow(Chara(ch.NodeId));
                    break;
                case OverrideChannel.AnimeSpeed:
                    Chara(ch.NodeId)?.ApplySpeedOverride();
                    break;
                case OverrideChannel.AnimeTime:
                    Chara(ch.NodeId)?.ApplyTimeOverride();
                    break;
                case OverrideChannel.FaceEyes:
                case OverrideChannel.FaceMouth:
                case OverrideChannel.FaceEyesOpen:
                case OverrideChannel.FaceMouthOpen:
                    Chara(ch.NodeId)?.ApplyFaceOverride();
                    break;
                case OverrideChannel.ItemColor:
                {
                    var it = rt.FindItem(ch.NodeId);
                    if (it != null && rt.IsInModel(it)) it.ApplyColorOverride();
                    break;
                }
                case OverrideChannel.LightColor:
                {
                    if (rt.Find(ch.NodeId) is LightRealization l && rt.IsInModel(l)) l.ApplyColorOverride();
                    break;
                }
                case OverrideChannel.Camera:
                    rt.GetPart<SceneViewCamera>()?.OverrideChanged();
                    break;
                case OverrideChannel.DepthOfField:
                    break;   // every late apply reads it
                case OverrideChannel.BonePos:
                case OverrideChannel.BoneRot:
                case OverrideChannel.BoneScale:
                    ApplyBone(ch);
                    break;
                case OverrideChannel.IkPos:
                case OverrideChannel.IkRot:
                    ApplyIk(ch);
                    break;
            }
        }

        CharaRealization Chara(string id)
        {
            var c = rt.FindChara(id);
            return c != null && c.State == RealizationState.Built && rt.IsInModel(c) ? c : null;
        }

        // ------------------------------------------------------------ the scene runtime's calls

        /// <summary>The end of an Apply (and of a step's tick): FK slots re-bound to a new FK table, the asked-for rows loaded.</summary>
        internal void AfterApply()
        {
            for (int i = fkChannels.Count - 1; i >= 0; i--)
            {
                var ch = fkChannels[i];
                if (ch.Winner == null) { Unbind(ch); fkChannels.RemoveAt(i); continue; }
                if (!(ch.Tag is FkBinding b) || !Valid(b)) ApplyFk(ch);
            }
            if (bonesStale) RebindBones();
            for (int i = driven.Count - 1; i >= 0; i--)
                if (!driven[i].Root || DrivenPoseDriver.Sweep(driven[i])) driven.RemoveAt(i);
            if (pendingRows.Count == 0) return;
            var l = pendingRows.ToArray();
            pendingRows.Clear();
            foreach (var c in l)
                if (c.State == RealizationState.Built && rt.IsInModel(c)) c.ApplyAnimeRefOverride();
        }

        /// <summary>A character was built or rebuilt (its model state applied): its slots apply again on the new body.</summary>
        internal void CharaBuilt(CharaRealization c)
        {
            c.OverridesRebuilt();
            if (!Layer.HasNode(c.NodeId)) return;
            bool face = false;
            foreach (var ch in Layer.ChannelsOf(c.NodeId))
            {
                if (ch.Winner == null) continue;
                switch (ch.Path.Channel)
                {
                    case OverrideChannel.FkBone: Unbind(ch); ApplyFk(ch); break;
                    case OverrideChannel.BonePos:
                    case OverrideChannel.BoneRot:
                    case OverrideChannel.BoneScale: ch.Tag = null; ApplyBone(ch); break;
                    case OverrideChannel.IkPos:
                    case OverrideChannel.IkRot: ch.Tag = null; ApplyIk(ch); break;
                    case OverrideChannel.AnimeRef: QueueRow(c); break;
                    case OverrideChannel.AnimeSpeed: c.ApplySpeedOverride(); break;
                    case OverrideChannel.AnimeTime: c.ApplyTimeOverride(); break;
                    case OverrideChannel.FaceEyes:
                    case OverrideChannel.FaceMouth:
                    case OverrideChannel.FaceEyesOpen:
                    case OverrideChannel.FaceMouthOpen: face = true; break;
                }
            }
            if (face) c.ApplyFaceOverride();
        }

        /// <summary>The realization was cleared (new, load, dispose): bindings and pending rows go.</summary>
        internal void Clear()
        {
            fkChannels.Clear();
            pendingRows.Clear();
            boneChannels.Clear();
            driven.Clear();
            bonesStale = false;
        }

        /// <summary>A flush replaced parts of a character: its addresses resolve again at the end of this Apply.</summary>
        internal void Reresolved() => bonesStale = boneChannels.Count > 0;

        /// <summary>A character's driven-bone state was made: the end of an Apply looks after it from now on.</summary>
        internal void Track(DrivenCharacter c)
        {
            if (c != null && !driven.Contains(c)) driven.Add(c);
        }

        void QueueRow(CharaRealization c)
        {
            if (c != null && !pendingRows.Contains(c)) pendingRows.Add(c);
        }

        // ------------------------------------------------------------ FK bones

        void ApplyFk(OverrideChannelState ch)
        {
            var w = ch.Winner;
            if (w == null)
            {
                Unbind(ch);
                fkChannels.Remove(ch);
                return;
            }
            if (!(ch.Tag is FkBinding b) || !Valid(b))
            {
                Unbind(ch);
                b = Bind(ch);
            }
            if (!fkChannels.Contains(ch)) fkChannels.Add(ch);
            if (b?.Bone == null) return;
            b.Bone.HasOverride = true;
            b.Bone.Override = new Quaternion((float)w[0], (float)w[1], (float)w[2], (float)w[3]);
            b.Fk.Stage.WriteIfApplied(b.Bone);
        }

        FkBinding Bind(OverrideChannelState ch)
        {
            var c = Chara(ch.NodeId);
            var fk = c != null ? FkDriver.Of(c) : null;
            var b = new FkBinding { Chara = c, Fk = fk, Bone = fk?.Table.FindAny(ch.Path.Bone) };
            ch.Tag = b;
            return b;
        }

        bool Valid(FkBinding b) =>
            b.Chara != null && b.Fk != null && b.Chara.State == RealizationState.Built && FkDriver.Of(b.Chara) == b.Fk && rt.Find(b.Chara.NodeId) == b.Chara;

        void Unbind(OverrideChannelState ch)
        {
            if (ch.Tag is FkBinding b && b.Bone != null && b.Bone.HasOverride)
            {
                b.Bone.HasOverride = false;
                if (Valid(b)) b.Fk.Stage.WriteIfApplied(b.Bone);   // the model value at once (the next step writes it anyway)
            }
            ch.Tag = null;
        }

        // ------------------------------------------------------------ bones and IK targets

        void ApplyBone(OverrideChannelState ch)
        {
            var w = ch.Winner;
            var b = ch.Tag as BoneBinding;
            if (w == null)
            {
                if (b?.Entry != null) b.Entry.Clear(ChannelOf(ch.Path.Channel));
                ch.Tag = null;
                boneChannels.Remove(ch);
                return;
            }
            if (b == null || (b.Entry != null && !b.Entry.Bone))
            {
                var c = Chara(ch.NodeId);
                b = new BoneBinding { Chara = c, Entry = DrivenPoseDriver.Bind(c, ch.Path.Bone) };
                ch.Tag = b;
                if (!boneChannels.Contains(ch)) boneChannels.Add(ch);
            }
            var e = b.Entry;
            if (e == null) return;
            switch (ch.Path.Channel)
            {
                case OverrideChannel.BonePos: e.SetPos(new Vector3((float)w[0], (float)w[1], (float)w[2])); break;
                case OverrideChannel.BoneRot: e.SetRot(new Quaternion((float)w[0], (float)w[1], (float)w[2], (float)w[3])); break;
                default: e.SetScale(new Vector3((float)w[0], (float)w[1], (float)w[2])); break;
            }
        }

        static DrivenChannels ChannelOf(OverrideChannel c) =>
            c == OverrideChannel.BonePos ? DrivenChannels.Position : c == OverrideChannel.BoneRot ? DrivenChannels.Rotation : DrivenChannels.Scale;

        void RebindBones()
        {
            bonesStale = false;
            for (int i = boneChannels.Count - 1; i >= 0; i--)
            {
                var ch = boneChannels[i];
                ch.Tag = null;
                ApplyBone(ch);
            }
        }

        bool BoneEffective(OverrideChannelState ch)
        {
            if (!(ch.Tag is BoneBinding b) || b.Entry == null || !b.Entry.Bone || !TargetExists(ch.NodeId)) return false;
            var corrected = AdvancedPoseDriver.Of(b.Chara)?.Set.Of(b.Entry.Bone);
            return DrivenBones.Shown(b.Entry, ChannelOf(ch.Path.Channel), corrected);
        }

        void ApplyIk(OverrideChannelState ch)
        {
            var stage = ch.Tag as IkStage;
            if (stage == null || !stage.Root)
            {
                var c = Chara(ch.NodeId);
                stage = c != null ? IkDriver.Of(c)?.Stage : null;
                ch.Tag = stage;
                if (stage == null) return;
            }
            var w = ch.Winner;
            var t = (IkTargetId)ch.Path.Index;
            if (ch.Path.Channel == OverrideChannel.IkPos) stage.SetOverridePos(t, w != null, w != null ? new Vector3((float)w[0], (float)w[1], (float)w[2]) : default);
            else stage.SetOverrideRot(t, w != null, w != null ? new Quaternion((float)w[0], (float)w[1], (float)w[2], (float)w[3]) : Quaternion.identity);
            if (w == null) ch.Tag = null;
        }

        bool IkEffective(OverrideChannelState ch)
        {
            var c = Chara(ch.NodeId);
            var ik = c != null ? IkDriver.Of(c) : null;
            return ik != null && !ik.Stage.Suspended && ik.Stage.IsApplied(IkGroupInfo.GroupOf((IkTargetId)ch.Path.Index));
        }

        bool FkEffective(OverrideChannelState ch)
        {
            if (!(ch.Tag is FkBinding b) || !Valid(b) || b.Bone == null) return false;
            var bone = b.Bone;
            var st = b.Fk.Stage;
            return bone.Used && bone.Bone && st.IsApplied(bone.Group) && !st.Suspended && !st.Owned.Contains(bone);
        }

        // ------------------------------------------------------------ reports

        /// <summary>The owner of a channel's winning slot (null when none): camera.get / camera.state, effects.state.</summary>
        public string WinningOwner(string nodeId, OverrideChannel ch) => Layer.WinnerOf(nodeId, ch)?.Owner;

        /// <summary>The camera as a winning slot shows it (target, rotation, distance, field of view), or false.</summary>
        public bool TryCamera(out Vector3 target, out Quaternion rot, out float dist, out float fov)
        {
            var w = Layer.WinnerOf(null, OverrideChannel.Camera);
            if (w == null)
            {
                target = default; rot = Quaternion.identity; dist = 0; fov = 0;
                return false;
            }
            target = new Vector3((float)w[0], (float)w[1], (float)w[2]);
            rot = new Quaternion((float)w[3], (float)w[4], (float)w[5], (float)w[6]);
            dist = (float)w[7];
            fov = (float)w[8];
            return true;
        }
    }
}
