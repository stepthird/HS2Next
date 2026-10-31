// The Unity layer's IHairInfo (Phase 4.3; package A): the prefab facts of a
// node's realized hair parts — whether the part has a CmpHair, the seven flags, the acsDefColor count, the bundles
// (limits, base pose, default rates, shown / title) and the prefab name — read from the realization (CharaContext.Hair[k]'s
// CmpHair; the build already used the male variant), so the page shows what is drawn. A node without a built realization
// (a card-backed node, a failed load, not built yet) answers false ("no prefab" for the gates).
// The interface is keyed by node id only, so the instance resolves over the scene runtimes it was shown: Track(rt) (weak,
// newest first; called by the Unity-side callers that have a runtime, e.g. StudioOps.MakerGateHolds) or the static
// TryPart(rt, …), which tracks too. Registered by MakerSetup as MakerServices.Hair (package B).
using System;
using System.Collections.Generic;
using HS2Next.Chara;
using HS2Next.Chara.Edit;
using HS2Next.Save;

namespace HS2Next.Scenes.Runtime
{
    public sealed class HairInfo : IHairInfo
    {
        /// <summary>The instance MakerSetup registers (one per app domain).</summary>
        public static readonly HairInfo Instance = new HairInfo();

        static readonly List<WeakReference<SceneRuntime>> runtimes = new List<WeakReference<SceneRuntime>>();

        /// <summary>Makes <paramref name="rt"/> the first runtime <see cref="TryPart(string, int, out HairPartFacts)"/> looks in
        /// (kept weakly; disposed runtimes are dropped).</summary>
        public static void Track(SceneRuntime rt)
        {
            if (rt == null || rt.IsDisposed) return;
            lock (runtimes)
            {
                for (int i = runtimes.Count - 1; i >= 0; i--)
                    if (!runtimes[i].TryGetTarget(out var r) || r == null || r.IsDisposed || ReferenceEquals(r, rt)) runtimes.RemoveAt(i);
                runtimes.Add(new WeakReference<SceneRuntime>(rt));
            }
        }

        /// <summary>IHairInfo: the facts of part <paramref name="part"/> (0..3) of node <paramref name="nodeId"/>'s built
        /// character in the tracked runtimes (newest first); false when no tracked runtime has it built.</summary>
        public bool TryPart(string nodeId, int part, out HairPartFacts facts)
        {
            facts = HairPartFacts.None();
            if (string.IsNullOrEmpty(nodeId) || part < 0 || part > 3) return false;
            SceneRuntime[] list;
            lock (runtimes)
            {
                var l = new List<SceneRuntime>();
                for (int i = runtimes.Count - 1; i >= 0; i--)
                {
                    if (runtimes[i].TryGetTarget(out var r) && r != null && !r.IsDisposed) l.Add(r);
                    else runtimes.RemoveAt(i);
                }
                list = l.ToArray();
            }
            foreach (var rt in list)
            {
                var ctx = Built(rt, nodeId);
                if (ctx != null) { facts = FactsOf(ctx, part); return true; }
            }
            return false;
        }

        /// <summary>The facts of part <paramref name="part"/> of node <paramref name="nodeId"/> in <paramref name="rt"/> (tracks
        /// the runtime); false when the node is not a built character there.</summary>
        public static bool TryPart(SceneRuntime rt, string nodeId, int part, out HairPartFacts facts)
        {
            facts = HairPartFacts.None();
            Track(rt);
            if (part < 0 || part > 3) return false;
            var ctx = Built(rt, nodeId);
            if (ctx == null) return false;
            facts = FactsOf(ctx, part);
            return true;
        }

        static CharaContext Built(SceneRuntime rt, string nodeId)
        {
            if (rt == null || rt.IsDisposed || nodeId == null) return null;
            var c = rt.FindChara(nodeId);
            if (c == null || c.State != RealizationState.Built || c.InGraveyard || !c.Root) return null;
            return c.Context;
        }

        /// <summary>The facts of one part of a built character: from <see cref="HairReport.PartOf"/>; a part
        /// without a CmpHair (none rows, a failed load) has HasPrefab false and no bundles.</summary>
        public static HairPartFacts FactsOf(CharaContext ctx, int part)
        {
            var p = HairReport.PartOf(ctx, part);
            return p == null ? HairPartFacts.None() : FactsOf(p);
        }

        /// <summary>The Unity-free facts of a part report.</summary>
        public static HairPartFacts FactsOf(HairReport.PartReport p)
        {
            var f = HairPartFacts.None();
            if (p == null || !p.HasCmpHair) return f;
            f.HasPrefab = true;
            f.Item = p.Item;
            f.PrefabName = p.Prefab;
            // the mesh rows follow what the unit draws (HairUnit.MeshDrawn: useMesh, or any live rendHair with the
            // HairMeshforAll rule), not the prefab flag alone
            f.UseTopColor = p.UseTopColor; f.UseUnderColor = p.UseUnderColor; f.UseSameSkinColorButton = p.UseSameSkinColorButton; f.UseMesh = p.Mesh.Written;
            f.UseAcsColor01 = p.UseAcsColor01; f.UseAcsColor02 = p.UseAcsColor02; f.UseAcsColor03 = p.UseAcsColor03;
            f.AcsDefColorCount = p.AcsDefColorCount;
            var info = new List<(bool, Vec3, Vec3, Vec3, Vec3, Vec3, Vec3)>(p.Bundles.Count);
            foreach (var e in p.Bundles) info.Add((e.Shown, V(e.PosMin), V(e.PosMax), V(e.RotMin), V(e.RotMax), V(e.BasePos), V(e.BaseRot)));
            f.Bundles = HairBundleFacts.Number(info);
            return f;
        }

        static Vec3 V(UnityEngine.Vector3 v) => new Vec3(v.x, v.y, v.z);
    }
}
