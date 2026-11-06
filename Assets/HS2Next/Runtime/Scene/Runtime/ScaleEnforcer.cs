// World-scale enforcement of `inheritScale` false (vanilla "calculate scale"): the
// holder's world scale must equal the node's `scale` ((1, 1, 1) for kinds without a scale channel), so its local
// scale is scale / parent.lossyScale component-wise. Roots get it when their transform changes; attached nodes
// get it every LateApply, after the motion stages moved the attach points (bones carry shape scale), in model
// pre-order so a parent's correction is in place before its children are computed. A parent component near
// zero keeps the previous local value and is reported once (`scene.zeroParentScale`). lossyScale is approximate
// under rotated non-uniform parents (skew), accepted as in vanilla (D9). A winning `transform.scale` slot of the runtime
// override layer stands for the node's `scale` (Phase 6.2).
using System.Collections.Generic;
using HS2Next.Save;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    public sealed class ScaleEnforcer
    {
        const float Zero = 1e-6f;
        readonly SceneRuntime runtime;
        readonly List<NodeRealization> attached = new List<NodeRealization>();

        public ScaleEnforcer(SceneRuntime runtime) { this.runtime = runtime; }

        /// <summary>Attached nodes (non-root, <c>inheritScale</c> false) enforced every LateApply.</summary>
        public int Count => attached.Count;

        internal void Clear() => attached.Clear();

        /// <summary>Re-collects the attached nodes in pre-order (after structural or `inheritScale` changes).</summary>
        internal void Rebuild(IReadOnlyList<TreeNode> preOrder, IReadOnlyDictionary<string, NodeRealization> reals)
        {
            attached.Clear();
            foreach (var n in preOrder)
            {
                if (n.Parent == null || n.Doc.Transform == null || n.Doc.Transform.InheritScale) continue;
                if (reals.TryGetValue(n.Id, out var r) && r.HasHolder) attached.Add(r);
            }
        }

        /// <summary>Applies the world scale of every attached node (LateApply).</summary>
        public void Enforce()
        {
            for (int i = 0; i < attached.Count; i++)
            {
                var r = attached[i];
                if (!r.HasHolder || r.Node?.Doc?.Transform == null) continue;
                Apply(r);
            }
        }

        /// <summary>Local scale of one holder (both `inheritScale` modes).</summary>
        public void Apply(NodeRealization r)
        {
            var h = r.Holder;
            if (!h) return;
            var t = r.Node.Doc.Transform;
            var s = r.HasScaleChannel ? ScaleOf(r, t) : Vector3.one;
            if (t.InheritScale)
            {
                if (h.localScale != s) h.localScale = s;
                return;
            }
            var p = h.parent ? h.parent.lossyScale : Vector3.one;
            var cur = h.localScale;
            bool zero = false;
            var l = new Vector3(
                Mathf.Abs(p.x) < Zero ? Keep(cur.x, ref zero) : s.x / p.x,
                Mathf.Abs(p.y) < Zero ? Keep(cur.y, ref zero) : s.y / p.y,
                Mathf.Abs(p.z) < Zero ? Keep(cur.z, ref zero) : s.z / p.z);
            if (zero) runtime.ReportOnce(FindingSeverity.Warning, "scene.zeroParentScale", r.NodeId, "parent lossy scale " + p + " has a component near 0; the previous local scale is kept");
            if (cur.x != l.x || cur.y != l.y || cur.z != l.z) h.localScale = l;
        }

        Vector3 ScaleOf(NodeRealization r, NodeTransform t)
        {
            var w = runtime.Overrides.WinnerOf(r.NodeId, HS2Next.Scenes.Overrides.OverrideChannel.TransformScale);
            return w != null ? new Vector3((float)w[0], (float)w[1], (float)w[2]) : UnityObjects.V(t.Scale);
        }

        static float Keep(float v, ref bool zero)
        {
            zero = true;
            return v;
        }
    }
}
