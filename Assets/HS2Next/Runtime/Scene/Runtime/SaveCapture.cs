// Captures of runtime-owned values into the model: before Save, Duplicate,
// Delete and on request, after Apply() flushed pending model changes to Unity, as one silent batch (origin
// Capture, not recorded):
//   chara.anime.time        CharaAnimation.NormalizedTime (raw)
//   chara.look.neckFix      CharaLook.SaveNeck, keyed by the neck bone names
//   chara.look.eyeRotations CharaLook.SaveEyes
//   item.anime.time         layer-0 normalized time of the item's animation (ItemAnimationStage of its ItemMotion,
//                           13), when it advanced since the stored time was applied or captured
// A character is captured only when its motion advanced since its animation / look state was last applied from the
// model (or captured): unadvanced values stay exactly as stored, so save → load → save is byte-identical without
// stepping.
// Runtime overrides (Phase 6.2): no captured value depends on a slot of the override layer: a character with a winning
// chara.anime.ref, .speed or .time slot keeps its stored chara.anime.time, and one with any winning slot on an FK bone, a bone or an
// IK target keeps its stored chara.look.neckFix and eyeRotations; its applied mark is set as for any captured character.
using System;
using System.Collections.Generic;
using HS2Next.Save;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    public sealed class SaveCapture
    {
        readonly SceneRuntime runtime;

        public SaveCapture(SceneRuntime runtime) { this.runtime = runtime; }

        /// <summary>Values written by the last capture and by all captures so far (tests, diagnostics).</summary>
        public int LastWrites { get; private set; }
        public int TotalWrites { get; private set; }

        /// <summary>Captures every realized character and item of the model.</summary>
        public void CaptureAll() => Capture(runtime.Model.PreOrder());

        /// <summary>Captures the subtrees of <paramref name="ids"/> (delete, duplicate).</summary>
        public void CaptureSubtrees(IEnumerable<string> ids)
        {
            var nodes = new List<TreeNode>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in ids ?? Array.Empty<string>())
            {
                var n = runtime.Model.Find(id);
                if (n == null) continue;
                foreach (var m in runtime.Model.Subtree(n)) if (seen.Add(m.Id)) nodes.Add(m);
            }
            Capture(nodes);
        }

        void Capture(IReadOnlyList<TreeNode> nodes)
        {
            LastWrites = 0;
            var writes = new List<(string id, string path, object value, Action after)>();
            foreach (var n in nodes)
            {
                var r = runtime.Find(n.Id);
                if (r == null || r.Node != n || !r.HasHolder || r.State != RealizationState.Built) continue;
                if (r is CharaRealization c) CollectChara(c, writes);
                else if (r is ItemRealization it) CollectItem(it, writes);
            }
            if (writes.Count == 0) return;
            runtime.Session.WriteSilently(ChangeOrigin.Capture, w =>
            {
                foreach (var (id, path, value, _) in writes)
                {
                    if (path == null) continue;
                    var res = w.Set(id, path, value);
                    if (res.Status == CommandStatus.Ok) { LastWrites++; TotalWrites++; }
                    else if (res.Status == CommandStatus.Error)
                        runtime.ReportOnce(FindingSeverity.Warning, "scene.captureFailed", id, path + ": " + res);
                }
            });
            foreach (var w in writes) w.after?.Invoke();
        }

        static void CollectChara(CharaRealization c, List<(string, string, object, Action)> writes)
        {
            if (!c.MotionAdvanced) return;
            var st = c.Node.State as CharaNodeState;
            if (st == null) return;
            string id = c.NodeId;
            var ov = c.Runtime.Overrides;
            bool animeSlot = ov.HasNode(id) && (ov.WinnerOf(id, HS2Next.Scenes.Overrides.OverrideChannel.AnimeRef) != null ||
                                                ov.WinnerOf(id, HS2Next.Scenes.Overrides.OverrideChannel.AnimeSpeed) != null ||
                                                ov.WinnerOf(id, HS2Next.Scenes.Overrides.OverrideChannel.AnimeTime) != null);
            if (c.Animation && c.Animation.IsLoaded && st.Anime.Ref != null && !animeSlot)
                writes.Add((id, "chara.anime.time", c.Animation.NormalizedTime, null));
            var look = c.Look;
            if (look != null && !HasBoneSlot(ov, id))
            {
                var bones = look.Neck?.Bones;
                if (bones != null && bones.Length > 0)
                {
                    var q = new Quaternion[bones.Length];
                    look.SaveNeck(q);
                    var map = new SortedDictionary<string, Quat4>(StringComparer.Ordinal);
                    for (int i = 0; i < bones.Length; i++)
                    {
                        string key = CharaRealization.NeckKey(bones[i]);
                        if (key.Length > 0 && !map.ContainsKey(key)) map[key] = UnityObjects.Q(q[i]);
                    }
                    writes.Add((id, "chara.look.neckFix", map, null));
                }
                if (look.Eyes?.Eyes != null && look.Eyes.Eyes.Length >= 2)
                {
                    var e = new Quaternion[2];
                    look.SaveEyes(e);
                    writes.Add((id, "chara.look.eyeRotations", new EyeRotations { Left = UnityObjects.Q(e[0]), Right = UnityObjects.Q(e[1]) }, null));
                }
            }
            writes.Add((id, null, null, c.MarkApplied));
        }

        /// <summary>True when a slot on an FK bone, a bone or an IK target of the character wins (its look capture would depend on it).</summary>
        static bool HasBoneSlot(HS2Next.Scenes.Overrides.OverrideLayer ov, string id)
        {
            if (!ov.HasNode(id)) return false;
            foreach (var ch in ov.ChannelsOf(id))
                if ((ch.Path.Channel == HS2Next.Scenes.Overrides.OverrideChannel.FkBone || ch.Path.IsBone || ch.Path.IsIkTarget) && ch.Winner != null) return true;
            return false;
        }

        static void CollectItem(ItemRealization it, List<(string, string, object, Action)> writes)
        {
            if (it.Motion is ItemMotion m)
            {
                var a = m.Animation;
                if (a == null || !a.Advanced) return;
                float at = a.NormalizedTime;
                if (float.IsNaN(at) || float.IsInfinity(at)) return;
                writes.Add((it.NodeId, "item.anime.time", at, a.MarkApplied));
                return;
            }
            // no item motion (handlers not registered): the item Animator's own time
            float t = it.CurrentAnimeTime();
            if (float.IsNaN(t) || t == it.appliedAnimeTime) return;
            writes.Add((it.NodeId, "item.anime.time", t, () => it.appliedAnimeTime = t));
        }
    }
}
