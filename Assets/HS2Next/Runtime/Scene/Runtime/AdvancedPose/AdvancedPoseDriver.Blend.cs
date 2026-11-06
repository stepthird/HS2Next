// The blend-shape overrides of the advanced-pose driver: the block's weights on the built character, resolved by renderer address and
// shape name once per read; the stage pose.blend writes them after the face stage and exists only while one acts. A renderer or shape
// this character lacks stays in the model and in the file and does nothing (stale); a mesh swap or a head swap resolves again by name.
// The mosaic rule: a member, and the body under a body-surface member, are left alone: such weights are kept and not applied.
using System;
using System.Collections.Generic;
using HS2Next.Chara.Mosaic;
using HS2Next.Motion.AdvancedPose;
using HS2Next.Save;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    public sealed partial class AdvancedPoseCharacter
    {
        /// <summary>The renderer addresses this character lacks, in key order (keys that are no address included).</summary>
        public readonly List<string> StaleRenderers = new List<string>();
        /// <summary>"&lt;renderer address&gt;:&lt;shape&gt;" for a renderer that exists without that shape.</summary>
        public readonly List<string> StaleShapes = new List<string>();
        /// <summary>The addresses of the renderers that keep the mosaic in place: their weights are kept and not applied.</summary>
        public readonly List<string> ProtectedRenderers = new List<string>();
        internal BlendShapeOverrideStage BlendStage;
    }

    public sealed partial class AdvancedPoseDriver
    {
        partial void ReadBlend(AdvancedPoseCharacter c)
        {
            c.StaleRenderers.Clear();
            c.StaleShapes.Clear();
            c.ProtectedRenderers.Clear();
            var ap = c.Block;
            if ((ap == null || ap.BlendShapes.Count == 0) && c.BlendStage == null) return;
            var r = c.Realization;
            var next = new List<BlendOverride>();
            if (ap != null && ap.BlendShapes.Count > 0)
            {
                string address = null;
                SkinnedMeshRenderer renderer = null;
                Mesh mesh = null;
                bool skip = false;
                foreach (var w in ap.AppliedWeights())
                {
                    if (w.Renderer != address)
                    {
                        address = w.Renderer;
                        renderer = AdvancedPoseParts.Renderer(r, address);
                        mesh = renderer ? renderer.sharedMesh : null;
                        skip = true;
                        if (!mesh) c.StaleRenderers.Add(address);
                        else if (MosaicSet.GuardsBlend(r.Context, renderer)) c.ProtectedRenderers.Add(address);
                        else skip = false;
                    }
                    if (skip) continue;
                    int index = mesh.GetBlendShapeIndex(w.Shape);
                    if (index < 0) { c.StaleShapes.Add(address + ":" + w.Shape); continue; }
                    next.Add(new BlendOverride(renderer, mesh, index, address, w.Shape, w.Weight));
                }
                foreach (var k in ap.BlendShapes.Keys)
                    if (!PoseAddress.IsCanonical(PoseAddressForm.Blend, k)) c.StaleRenderers.Add(k);
                c.StaleRenderers.Sort(StringComparer.Ordinal);
            }
            if (next.Count > 0 && c.BlendStage == null && r.Motion)
            {
                c.BlendStage = new BlendShapeOverrideStage { Enabled = !c.Suspended };
                r.Motion.Add(c.BlendStage);
            }
            if (c.BlendStage != null)
            {
                c.BlendStage.Replace(next);
                if (next.Count == 0)
                {
                    if (r.Motion) r.Motion.Remove(c.BlendStage);
                    c.BlendStage = null;
                }
                else if (!c.Suspended) c.BlendStage.Apply();
            }
            NoteStale(c);
            Note(c, FindingSeverity.Warning, AdvancedPoseFindings.MosaicProtected, c.ProtectedRenderers, "blend override");
        }

        partial void SuspendExtras(AdvancedPoseCharacter c, bool suspended)
        {
            var s = c.BlendStage;
            if (s == null) return;
            if (suspended) s.Restore();
            s.Enabled = !suspended;
        }

        partial void GuardsBone(AdvancedPoseCharacter c, Transform t, ref bool guarded) => guarded = MosaicSet.GuardsBone(c.Realization.Context, t);
    }
}
