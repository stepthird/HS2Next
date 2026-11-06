// The jiggle values of the advanced-pose driver: the block's chains and bust values on the built character. A chain reads its
// override in front of the part's and the card's values, which nothing here writes; taking it off gives them back. A chain whose address
// this character lacks stays in the model and in the file and does nothing (stale); a part swap makes new chains and Reresolve finds
// them by address. No particle reset and no pose restore, unless a switch or a weight crossing 0 asks for it.
using System;
using System.Collections.Generic;
using HS2Next.Motion.AdvancedPose;
using HS2Next.Motion.DynamicBones;
using HS2Next.Save;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    public sealed partial class AdvancedPoseCharacter
    {
        /// <summary>The addresses of the chain entries this character lacks, in key order (keys that are no address included).</summary>
        public readonly List<string> StaleChains = new List<string>();
        internal readonly HashSet<DynamicBoneChain> OverriddenChains = new HashSet<DynamicBoneChain>();
    }

    public sealed partial class AdvancedPoseDriver
    {
        partial void ReadChains(AdvancedPoseCharacter c)
        {
            c.StaleChains.Clear();
            var ap = c.Block;
            if ((ap == null || ap.Chains.Count == 0) && c.OverriddenChains.Count == 0) return;
            var r = c.Realization;
            bool switches = false;
            var next = new HashSet<DynamicBoneChain>();
            if (ap != null && ap.Chains.Count > 0)
            {
                var rows = new Dictionary<string, DynamicBoneChain>(StringComparer.Ordinal);
                foreach (var row in AdvancedPoseParts.Chains(r)) rows[row.Address] = row.Chain;
                foreach (var kv in ap.AppliedChains())
                {
                    if (!rows.TryGetValue(kv.Key, out var chain)) { c.StaleChains.Add(kv.Key); continue; }
                    var o = ToOverride(kv.Value);
                    switches |= Take(chain, o);
                    if (o != null) next.Add(chain);
                }
                foreach (var k in ap.Chains.Keys)
                    if (!PoseAddress.IsCanonical(PoseAddressForm.Chain, k)) c.StaleChains.Add(k);
                c.StaleChains.Sort(StringComparer.Ordinal);
            }
            foreach (var chain in c.OverriddenChains)
                if (!next.Contains(chain) && chain.Source) switches |= Take(chain, null);
            c.OverriddenChains.Clear();
            c.OverriddenChains.UnionWith(next);
            if (switches)
            {
                if (r.Motion) r.Motion.Get<DynamicBoneStage>()?.ApplySwitches();
                RefreshNow(r);
            }
            NoteStale(c);
        }

        // true when the chain's bones may have returned to the rest pose (the switch, a weight crossing 0)
        static bool Take(DynamicBoneChain chain, ChainOverride o)
        {
            bool was = chain.Enabled;
            float w = chain.Weight;
            chain.Override = o;
            chain.ApplyOverride();
            return chain.Enabled != was || (chain.Weight == 0f) != (w == 0f);
        }

        partial void ReadBust(AdvancedPoseCharacter c)
        {
            var r = c.Realization;
            var stage = r.Motion ? r.Motion.Get<BustStage>() : null;
            if (stage == null) return;
            var bust = c.Block?.Bust;
            bool switches = false;
            for (int k = 0; k < stage.Chains.Length; k++)
            {
                var chain = stage.Chains[k];
                if (chain == null) continue;
                var o = bust != null ? ToOverride(bust[k]) : null;
                if (o == null && chain.Override == null) continue;
                switches |= (chain.Override?.Enabled == false) != (o?.Enabled == false);
                chain.SetOverride(o);
            }
            if (switches)
            {
                stage.ApplySwitches();
                RefreshNow(r);
            }
        }

        static ChainOverride ToOverride(ChainValues v)
        {
            if (v == null) return null;
            var a = v.Applied();
            var o = new ChainOverride
            {
                Enabled = a.Enabled,
                Weight = a.Weight,
                Damping = a.Damping,
                Elasticity = a.Elasticity,
                Stiffness = a.Stiffness,
                Inert = a.Inert,
                Radius = a.Radius,
                Gravity = V(a.Gravity),
                Force = V(a.Force),
                FreezeAxis = a.FreezeAxis,
            };
            return o.IsEmpty ? null : o;
        }

        static BustOverride ToOverride(BustValues v)
        {
            if (v == null) return null;
            var a = v.Applied();
            var o = new BustOverride
            {
                Enabled = a.Enabled,
                Gravity = V(a.Gravity),
                Force = V(a.Force),
                Damping = a.Damping,
                Elasticity = a.Elasticity,
                Stiffness = a.Stiffness,
                Inert = a.Inert,
            };
            return o.IsEmpty ? null : o;
        }

        static Vector3? V(Vec3? v) => v.HasValue ? UnityObjects.V(v.Value) : (Vector3?)null;
    }
}
