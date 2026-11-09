// The advanced-pose driver behind CharaPoseDrivers.AdvancedPose: the character node's `chara.advancedPose` block on the built
// character. When the block is empty nothing is created: no stage, no component, no write. Otherwise the per-character state
// (AdvancedPoseCharacter) resolves the entries by name and owns what they need.
//   Apply(Build)     after the FK driver at a build: resolves the block, reports stale entries once per build
//   OnChanged        a changed section (bones, chains, bust, blend shapes): re-read by key; undo and redo come this way
//   Reresolve        after a Maker flush that replaced parts: every address resolved again
//   Suspend          character thumbnails: the bones back, the stages off, the overrides off until the handle is disposed
// The model is the truth: nothing here writes it. An entry whose bone this character lacks stays in the model and in the file and
// does nothing (stale); it acts again when the bone returns.
// What reads a bone to store a base must see it without the corrections: Bare (a capture), RestoreNow (a flush, a controller bind).
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using HS2Next.Motion;
using HS2Next.Motion.AdvancedPose;
using HS2Next.Motion.Animation;
using HS2Next.Motion.DynamicBones;
using HS2Next.Motion.Face;
using HS2Next.Save;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>The advanced-pose state of one built character. It lives while the block is not empty and dies with the root.</summary>
    public sealed partial class AdvancedPoseCharacter
    {
        public readonly CharaRealization Realization;
        public readonly GameObject Root;
        /// <summary>The corrected bones (empty when the block has none that act here).</summary>
        public readonly BoneCorrections Set;
        /// <summary>The addresses of the bone entries this character lacks, in key order.</summary>
        public readonly List<string> StaleBones = new List<string>();
        /// <summary>The addresses of the bone entries that lie on a part keeping the mosaic in place: kept, not applied.</summary>
        public readonly List<string> ProtectedBones = new List<string>();
        public bool Suspended { get; internal set; }

        internal BoneRestoreStage RestoreStage;
        internal BoneApplyStage ApplyStage;
        internal BoneEndStage EndStage;
        internal Action<CharaAnimation> BeforeBindHandler;
        internal readonly HashSet<string> Reported = new HashSet<string>(StringComparer.Ordinal);
        internal HS2Next.Scenes.SceneFinding StaleFinding;

        internal AdvancedPoseCharacter(CharaRealization r)
        {
            Realization = r;
            Root = r.Root;
            Set = new BoneCorrections(r.Motion);
        }

        /// <summary>The model's block of this character.</summary>
        public CharaAdvancedPose Block => (Realization.Node?.State as CharaNodeState)?.AdvancedPose;

        /// <summary>True while the three stages are on the character's motion.</summary>
        public bool HasStages => RestoreStage != null;
    }

    public sealed partial class AdvancedPoseDriver : ICharaPoseDriver
    {
        public static readonly AdvancedPoseDriver Instance = new AdvancedPoseDriver();

        /// <summary>The block's path in the character's node.</summary>
        public const string BlockPath = "chara.advancedPose";
        const string BonesPath = BlockPath + ".bones", ChainsPath = BlockPath + ".chains", BustPath = BlockPath + ".bust", BlendPath = BlockPath + ".blendShapes";

        static readonly ConditionalWeakTable<GameObject, AdvancedPoseCharacter> states = new ConditionalWeakTable<GameObject, AdvancedPoseCharacter>();

        /// <summary>The state of a built character, or null (no block, not built).</summary>
        public static AdvancedPoseCharacter Of(CharaRealization r)
        {
            var root = r?.Root;
            if (!root || !states.TryGetValue(root, out var c)) return null;
            return c.Realization == r ? c : null;
        }

        static AdvancedPoseCharacter OfRoot(GameObject root) => root && states.TryGetValue(root, out var c) ? c : null;

        // ------------------------------------------------------------ the driver

        public void Apply(CharaRealization r, CharaNodeState state, PoseApplyReason reason)
        {
            if (r == null || r.State != RealizationState.Built || r.Context == null || !r.Root || state == null) return;
            var ap = state.AdvancedPose;
            var c = Of(r);
            if (c == null)
            {
                if (ap == null || ap.IsEmpty) return;
                c = Create(r);
            }
            ReadBones(c);
            ReadChains(c);
            ReadBust(c);
            ReadBlend(c);
            DropIfUnused(c);
        }

        public void OnChanged(CharaRealization r, string path)
        {
            if (r == null || r.State != RealizationState.Built) return;
            var c = Of(r);
            if (c == null) { Apply(r, r.Node?.State as CharaNodeState, PoseApplyReason.Changed); return; }
            if (FieldPath.Overlaps(path, BonesPath)) ReadBones(c);
            if (FieldPath.Overlaps(path, ChainsPath)) ReadChains(c);
            if (FieldPath.Overlaps(path, BustPath)) ReadBust(c);
            if (FieldPath.Overlaps(path, BlendPath)) ReadBlend(c);
            DropIfUnused(c);
        }

        public IDisposable Suspend(GameObject charaRoot)
        {
            var c = OfRoot(charaRoot);
            if (c == null || c.Suspended) return null;
            c.Suspended = true;
            c.Set.Restore();
            c.Set.Suspended = true;
            SetStagesEnabled(c, false);
            SuspendExtras(c, true);
            return new Resume(this, c);
        }

        sealed class Resume : IDisposable
        {
            readonly AdvancedPoseDriver d;
            AdvancedPoseCharacter c;
            public Resume(AdvancedPoseDriver d, AdvancedPoseCharacter c) { this.d = d; this.c = c; }
            public void Dispose()
            {
                var x = c;
                c = null;
                if (x == null || !x.Root) return;
                x.Suspended = false;
                x.Set.Suspended = false;
                SetStagesEnabled(x, true);
                d.SuspendExtras(x, false);
            }
        }

        // The jiggle values and the blend-shape overrides live in their partial files.
        partial void ReadChains(AdvancedPoseCharacter c);
        partial void ReadBust(AdvancedPoseCharacter c);
        partial void ReadBlend(AdvancedPoseCharacter c);
        partial void SuspendExtras(AdvancedPoseCharacter c, bool suspended);
        /// <summary>Sets <paramref name="guarded"/> when the mosaic rule keeps this transform out of a correction.</summary>
        partial void GuardsBone(AdvancedPoseCharacter c, Transform t, ref bool guarded);

        // ------------------------------------------------------------ the state

        AdvancedPoseCharacter Create(CharaRealization r)
        {
            ClearFindings(r);
            var c = new AdvancedPoseCharacter(r);
            states.Remove(r.Root);
            states.Add(r.Root, c);
            if (r.Animation)
            {
                c.BeforeBindHandler = _ => c.Set.Restore();
                r.Animation.BeforeBind += c.BeforeBindHandler;
            }
            return c;
        }

        void DropIfUnused(AdvancedPoseCharacter c)
        {
            var ap = c.Block;
            if (c.HasStages || (ap != null && !ap.IsEmpty)) return;
            if (c.BeforeBindHandler != null && c.Realization.Animation) c.Realization.Animation.BeforeBind -= c.BeforeBindHandler;
            states.Remove(c.Root);
            ClearFindings(c.Realization);
        }

        static void ClearFindings(CharaRealization r)
        {
            r.Runtime.Findings.Remove(AdvancedPoseFindings.Stale, r.NodeId);
            r.Runtime.Findings.Remove(AdvancedPoseFindings.MosaicProtected, r.NodeId);
        }

        static void SetStagesEnabled(AdvancedPoseCharacter c, bool on)
        {
            if (!c.HasStages) return;
            c.RestoreStage.Enabled = on;
            c.ApplyStage.Enabled = on;
            c.EndStage.Enabled = on;
        }

        static void EnsureStages(AdvancedPoseCharacter c)
        {
            if (c.HasStages) return;
            var m = c.Realization.Motion;
            if (!m) return;
            c.RestoreStage = new BoneRestoreStage(c.Set);
            c.ApplyStage = new BoneApplyStage(c.Set);
            c.EndStage = new BoneEndStage(c.Set);
            if (c.Suspended) SetStagesEnabled(c, false);
            m.Add(c.RestoreStage);
            m.Add(c.ApplyStage);
            m.Add(c.EndStage);
        }

        static void RemoveStages(AdvancedPoseCharacter c)
        {
            if (!c.HasStages) return;
            var m = c.Realization.Motion;
            if (m)
            {
                m.Remove(c.RestoreStage);
                m.Remove(c.ApplyStage);
                m.Remove(c.EndStage);
            }
            c.RestoreStage = null; c.ApplyStage = null; c.EndStage = null;
        }

        // ------------------------------------------------------------ bones

        /// <summary>Re-reads the bone entries by key, applied at once; the stages come with the first entry and go with the last.</summary>
        void ReadBones(AdvancedPoseCharacter c)
        {
            var ap = c.Block;
            var r = c.Realization;
            var set = c.Set;
            set.Restore();
            c.StaleBones.Clear();
            c.ProtectedBones.Clear();
            var next = new List<CorrectedBone>();
            bool newChainBone = false;
            HashSet<Transform> chain = null;
            if (ap != null)
            {
                // an entry that does not act (not an address, identity) is simply not here; the limit counts the entries that act
                foreach (var kv in ap.AppliedBones())
                {
                    if (!PoseAddress.TryParse(PoseAddressForm.Bone, kv.Key, out var a, out _)) continue;
                    var t = AdvancedPoseResolver.Resolve(r, a);
                    if (!t) { c.StaleBones.Add(kv.Key); continue; }
                    bool guarded = false;
                    GuardsBone(c, t, ref guarded);
                    if (guarded) { c.ProtectedBones.Add(kv.Key); continue; }
                    chain = chain ?? AdvancedPoseResolver.ChainBones(r);
                    bool onChain = chain.Contains(t);
                    var e = set.Of(kv.Key);
                    if (e == null || e.Bone != t)
                    {
                        e = new CorrectedBone(kv.Key, t);
                        newChainBone |= onChain;
                    }
                    e.ChainBone = onChain;
                    var v = kv.Value;
                    e.Set(UnityObjects.V(v.AppliedPos), UnityObjects.V(v.Rot), UnityObjects.V(v.AppliedScale));
                    e.WidthStage = null;
                    if (e.HasScale && r.Motion)
                        foreach (var s in r.Motion.GetAll<ExpressionStage>())
                            if (s.MouthWidthBone == t) { e.WidthStage = s; break; }
                    e.EyeStage = null;
                    var eyes = r.Look?.Eyes;
                    if (e.HasRot && eyes?.Eyes != null)
                        foreach (var x in eyes.Eyes)
                            if (x != null && x.Transform == t) { e.EyeStage = eyes; break; }
                    next.Add(e);
                }
                // a key that is no address is kept in the file and does nothing
                foreach (var k in ap.Bones.Keys)
                    if (!PoseAddress.IsCanonical(PoseAddressForm.Bone, k)) c.StaleBones.Add(k);
                c.StaleBones.Sort(StringComparer.Ordinal);
            }
            // the chain bones' base is their rest pose, not the last write-back
            if (newChainBone && r.Motion) r.Motion.Get<DynamicBoneStage>()?.ResetBeforeUpdate();
            set.Replace(next);
            if (next.Count > 0) EnsureStages(c); else RemoveStages(c);
            if (next.Count > 0) set.Apply();
            NoteStale(c);
            Note(c, FindingSeverity.Warning, AdvancedPoseFindings.MosaicProtected, c.ProtectedBones, "bone correction");
        }

        /// <summary>The one stale finding of a node and build: it names every list that has an entry, and its count is their sum. A later read of another
        /// list adds to it; a list that empties leaves it as it stands until the next build.</summary>
        internal static void NoteStale(AdvancedPoseCharacter c)
        {
            int n = c.StaleBones.Count + c.StaleChains.Count + c.StaleRenderers.Count + c.StaleShapes.Count;
            if (n == 0) return;
            var sb = new StringBuilder();
            sb.Append(n).Append(n == 1 ? " entry is" : " entries are").Append(" kept without effect (not found on this character):");
            void Part(string what, List<string> a, List<string> b = null)
            {
                int k = a.Count + (b?.Count ?? 0);
                if (k == 0) return;
                sb.Append(' ').Append(k).Append(' ').Append(what).Append(k == 1 ? ": " : "s: ");
                int shown = 0;
                foreach (var list in new[] { a, b })
                {
                    if (list == null) continue;
                    foreach (var x in list)
                    {
                        if (shown == 8) { sb.Append(", ..."); goto done; }
                        sb.Append(shown > 0 ? ", " : "").Append(x);
                        shown++;
                    }
                }
                done:
                sb.Append(';');
            }
            Part("bone correction", c.StaleBones);
            Part("chain value", c.StaleChains);
            Part("blend override", c.StaleRenderers, c.StaleShapes);
            var r = c.Realization;
            var findings = r.Runtime.Findings;
            if (c.StaleFinding == null || !findings.Has(AdvancedPoseFindings.Stale, r.NodeId))
                c.StaleFinding = findings.Add(FindingSeverity.Info, AdvancedPoseFindings.Stale, r.NodeId, sb.ToString());
            c.StaleFinding.Message = sb.ToString();
            c.StaleFinding.Count = n;
        }

        /// <summary>One finding per node and build for a non-empty list; <paramref name="what"/> names the kind of entry.</summary>
        internal static void Note(AdvancedPoseCharacter c, FindingSeverity severity, string code, IReadOnlyList<string> list, string what)
        {
            if (list == null || list.Count == 0 || !c.Reported.Add(code)) return;
            var sb = new StringBuilder();
            sb.Append(list.Count).Append(' ').Append(what).Append(list.Count == 1 ? " is" : "s are");
            sb.Append(" kept and not applied (a part that keeps the mosaic in place): ");
            for (int i = 0; i < list.Count && i < 8; i++) sb.Append(i > 0 ? ", " : "").Append(list[i]);
            if (list.Count > 8) sb.Append(", ...");
            var r = c.Realization;
            var f = r.Runtime.Findings.Add(severity, code, r.NodeId, sb.ToString());
            f.Count = list.Count;
        }

        // ------------------------------------------------------------ one-line calls of the other classes (no state: nothing happens)

        /// <summary>The changed paths of one apply: every changed section re-read once, and a FK or IK change shows at once under the
        /// corrections, so a dump in the same request sees it.</summary>
        public static void AfterChanges(CharaRealization r, IReadOnlyCollection<string> paths)
        {
            bool bones = false, chains = false, bust = false, blend = false, pose = false;
            foreach (var p in paths)
            {
                if (FieldPath.Overlaps(p, BonesPath)) bones = true;
                if (FieldPath.Overlaps(p, ChainsPath)) chains = true;
                if (FieldPath.Overlaps(p, BustPath)) bust = true;
                if (FieldPath.Overlaps(p, BlendPath)) blend = true;
                if (FieldPath.Overlaps(p, "chara.fk") || FieldPath.Overlaps(p, "chara.ik")) pose = true;
            }
            var d = CharaPoseDrivers.AdvancedPose;
            if (d != null)
            {
                if (bones) d.OnChanged(r, BonesPath);
                if (chains) d.OnChanged(r, ChainsPath);
                if (bust) d.OnChanged(r, BustPath);
                if (blend) d.OnChanged(r, BlendPath);
            }
            if (pose && !bones) RefreshNow(r);
            ApplyIfRestored(r);   // a controller bound in this apply took the corrections off
        }

        /// <summary>Every address again, after a flush that replaced parts.</summary>
        public static void Reresolve(CharaRealization r)
        {
            var c = Of(r);
            if (c == null) return;
            var d = Instance;
            d.ReadBones(c);
            d.ReadChains(c);
            d.ReadBust(c);
            d.ReadBlend(c);
            d.DropIfUnused(c);
        }

        /// <summary>The bones back to what the pose sources left; the next step (or <see cref="RefreshNow"/>) applies again.</summary>
        public static void RestoreNow(CharaRealization r) => Of(r)?.Set.Restore();

        /// <summary>The corrections off and on again now, from the bones as they stand.</summary>
        public static void RefreshNow(CharaRealization r)
        {
            var c = Of(r);
            if (c == null || c.Set.Entries.Count == 0) return;
            c.Set.Restore();
            c.Set.Apply();
        }

        /// <summary>Applies again when a restore took the corrections off and no step ran since.</summary>
        public static void ApplyIfRestored(CharaRealization r)
        {
            var c = Of(r);
            if (c != null && !c.Set.Applied && c.Set.Entries.Count > 0) c.Set.Apply();
        }

        /// <summary>Runs <paramref name="read"/> with the bones as they are without the corrections (a capture that stores a base).</summary>
        public static void Bare(CharaRealization r, Action read)
        {
            var c = Of(r);
            if (c != null && c.Set.Entries.Count > 0) c.Set.Bare(() => DrivenPoseDriver.Bare(r, read));   // the driven values, and the body under them, come off inside
            else DrivenPoseDriver.Bare(r, read);
        }

        /// <summary>For a writer after the last pose stage: what it wrote on <paramref name="t"/> does not become the next base.</summary>
        public static void ForgetFinal(CharaRealization r, Transform t) => Of(r)?.Set.ForgetFinal(t);

        /// <summary>A position read from a corrected bone without the correction's delta (a handle of another feature on that bone).</summary>
        public static Vector3 WithoutPosition(CharaRealization r, Transform bone, Vector3 local)
        {
            var e = Of(r)?.Set.Of(bone);
            return e != null && e.HasPos ? local - e.D_pos : local;
        }

        /// <summary>A rotation read from a corrected bone without the correction's delta (a handle of another feature on that bone).</summary>
        public static Quaternion WithoutRotation(CharaRealization r, Transform bone, Quaternion local)
        {
            var e = Of(r)?.Set.Of(bone);
            return e != null && e.RotOn ? local * Quaternion.Inverse(e.Dq) : local;
        }
    }
}
