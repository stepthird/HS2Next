// The FK writer behind CharaPoseDrivers.Fk. Apply(Build) discovers the
// character's FK bones, installs the stages and applies the stored `chara.fk` block; OnChanged updates what
// a changed path touches; Suspend stops the writes for StudioDefaultPoseScope. Per-character state is an
// FkCharacter reachable from the character's CharaMotion, so nothing is held after the character is destroyed.
// Findings (Appendix A): fk.boneNotFound, fk.rowNoGroup, fk.skirtWeightsUnreadable (info, once per node and build),
// fk.nodeConflict (warning, from the node tables).
using System;
using System.Linq;
using HS2Next.Motion;
using HS2Next.Motion.Fk;
using HS2Next.Save;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    public sealed class FkDriver : ICharaPoseDriver
    {
        public static readonly FkDriver Instance = new FkDriver();

        /// <summary>The FK state of a built character, or null.</summary>
        public static FkCharacter Of(CharaRealization r)
        {
            if (r == null || r.State != RealizationState.Built || !r.Motion) return null;
            var c = r.Motion.Get<FkStage>()?.Owner as FkCharacter;
            return c != null && c.Realization == r ? c : null;
        }

        /// <summary>The FK state of the character built under <paramref name="charaRoot"/>, or null.</summary>
        public static FkCharacter Of(GameObject charaRoot)
        {
            var m = charaRoot ? charaRoot.GetComponent<CharaMotion>() : null;
            return m ? m.Get<FkStage>()?.Owner as FkCharacter : null;
        }

        public void Apply(CharaRealization r, CharaNodeState state, PoseApplyReason reason)
        {
            if (r == null || r.State != RealizationState.Built || r.Context == null || !r.Root) return;
            var c = Of(r) ?? Create(r);
            c.ReadAll(reason == PoseApplyReason.Build);
        }

        public void OnChanged(CharaRealization r, string path)
        {
            var c = Of(r);
            if (c == null) { Apply(r, r?.Node?.State as CharaNodeState, PoseApplyReason.Changed); return; }
            c.OnChanged(path);
        }

        public IDisposable Suspend(GameObject charaRoot) => Of(charaRoot)?.Suspend();

        /// <summary>
        /// FK bone rediscovery after a part swap (a hair part or the clothes kinds 0 / 1
        /// carry the hair and skirt bones): the character's FK stages are replaced by a fresh discovery over the current
        /// parts and the stored `chara.fk` block is applied as at a build (the new parts' bones start from the prefab; groups
        /// that are on switch their side effects on again). Bones the new parts lack keep their stored values in the model
        /// (U27). Nothing happens when the driver is not attached to the character.
        /// </summary>
        public static void Rediscover(CharaRealization r)
        {
            var old = Of(r);
            if (old == null) return;
            var m = r.Motion;
            bool suspended = old.Suspended;
            m.Remove(old.Hips);
            m.Remove(old.Gate);
            m.Remove(old.Stage);
            var c = Create(r);
            c.ReadAll(true);
            if (suspended) c.Log.Add("rediscovered while suspended: the new stages write (the suspension belonged to the old ones)");
        }

        /// <summary>The bone table of a built character: its FkCharacter's, or a fresh discovery when the driver is not
        /// attached (nothing is installed then).</summary>
        public static FkBoneTable TableFor(CharaRealization r)
        {
            var c = Of(r);
            if (c != null) return c.Table;
            if (r == null || r.State != RealizationState.Built || r.Context == null) return null;
            return FkBoneTable.Discover(FkCharacter.DiscoveryOf(r), null);
        }

        static FkCharacter Create(CharaRealization r)
        {
            var log = new System.Collections.Generic.List<string>();
            var table = FkBoneTable.Discover(FkCharacter.DiscoveryOf(r), log);
            var c = new FkCharacter(r, table);
            c.Log.AddRange(log);
            var st = table.Stats;
            var rt = r.Runtime;
            int missing = st.NotFound.Sum();
            if (missing > 0)
                rt.ReportOnce(FindingSeverity.Info, "fk.boneNotFound", r.NodeId,
                    $"{missing} FK rows without a transform on this character (" + string.Join(", ", Enumerable.Range(0, FkGroupInfo.Count).Where(g => st.NotFound[g] > 0).Select(g => (FkGroup)g + " " + st.NotFound[g])) + ")");
            if (st.NoGroup > 0) rt.ReportOnce(FindingSeverity.Info, "fk.rowNoGroup", r.NodeId, $"{st.NoGroup} FK rows with a group column outside 0–13 are never applied");
            if (st.UnreadableMeshes > 0) rt.ReportOnce(FindingSeverity.Info, "fk.skirtWeightsUnreadable", r.NodeId, $"{st.UnreadableMeshes} clothes meshes are not readable: every skirt bone counts as used");
            // the first FK application of a character whose body mesh a build hook replaced joins the FK node rows of the
            // build hooks' providers (none in the official build)
            if (!string.IsNullOrEmpty(r.Context.ReplacedBodyMesh))
            {
                try { c.JoinNodes(HS2Next.Chara.Ext.CharaBuildHooks.FkNodes(r.Runtime?.Services?.Catalog)); }
                catch (Exception e) { c.Log.Add("FK node rows: " + e.GetType().Name + ": " + e.Message); }
            }
            return c;
        }
    }
}
