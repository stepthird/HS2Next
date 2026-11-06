// Hair bundle handles (decisions J4, J5, J14; Phase 4.3 package W), a part of the transform gizmo
// (TransformTool; the view camera's gizmo pass: never in captures):
//   Markers   while the Edit tab shows the Hair page of the primary with the selected part's Bundles section open
//             (HairBundleList.Current shown), the app option maker.hairHandles is on (default on) and the bundle's own handle
//             toggle is on: a small octahedron (the FK marker shape, the hair group colour) at each shown bundle's correction
//             transform (CmpHair.boneInfo[i].trfCorrect of the realized part). Picking one (≤ 8 logical px, the 3.5 rule) sets
//             the sub-selection `<id>/hair/<part>/<index>` (the bundle list reveals its row).
//   Handle    the sub-selected bundle shows the move or the rotate handle (the 3.5 tool state; the scale tool shows move) in
//             the correction transform's parent space (forced: no view ring, no trackball). A drag is one transaction "Edit
//             hair" (pointer down → UiEdit; each move = StudioOps.EditHairBundle on the primary; up commits one entry; Esc
//             cancels). Move: the new parent-local position, clamped per dragged axis to the limits [min(posMin, posMax),
//             max(posMin, posMax)], gives rate_a = InverseLerp(posMin_a, posMax_a, p_a) for the dragged axes (axis: one,
//             plane: two, free: three); rotate: the ring of axis a gives the signed angle δ since the drag start, e_a =
//             lerp(rotMin_a, rotMax_a, r_a at the start) + δ clamped to the limits → rate_a = InverseLerp(rotMin_a, rotMax_a,
//             e_a); the other components keep their values (the sliders' per-axis semantics; HS2's world-Euler reading is not
//             reproduced, J4). A rate outside 0..1 is clamped into 0..1 on the first move. «髪の調整をまとめて行う» applies as
//             for the sliders (the op's rule, J5). The FK hair group does not affect the handle.
//   State     HairHandleStates(): the markers' and the shown handle's axis tips' panel positions (ui.state gizmo.handles).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using AIChara;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Core;
using HS2Next.UI.Panels;
using UnityEngine;

namespace HS2Next.UI.Gizmos
{
    public sealed partial class TransformTool
    {
        /// <summary>The hair bundle marker colour (the FK palette's hair group colour).</summary>
        public static readonly Color HairMarker = new Color32(170, 100, 230, 255);
        public const string HairSubKind = "hair";

        /// <summary>Markers drawn in the last frame (tests, UH8).</summary>
        public int HairMarkersDrawn { get; private set; }
        /// <summary>Time of the last HairHit (ms; UH8: the markers' hit test per frame).</summary>
        public double HairHitMs { get; private set; }
        /// <summary>Time of the last drag step of a bundle handle (op + flush, ms; UH8).</summary>
        public double HairDragMs { get; private set; }

        // drag state of a bundle handle
        int hairPart = -1, hairIndex = -1;
        HairBundleFacts hairFacts;
        Vec3 hairStartMove, hairStartRotate;
        float hairAngle;

        // ================================================================ markers

        /// <summary>The bundle list whose handles are shown (the primary's, attached, open), or null.</summary>
        HairBundleList HairList()
        {
            var l = HairBundleList.Current;
            if (l == null || ctx.Runtime == null || l.Primary == null || l.Primary != ctx.Selection.Primary) return null;
            return l;
        }

        /// <summary>The correction transform of bundle <paramref name="index"/> of part <paramref name="part"/> of a built character.</summary>
        Transform HairTransform(string id, int part, int index)
        {
            var r = ctx.Runtime?.FindChara(id);
            if (r == null || r.State != RealizationState.Built || !r.Root) return null;
            var hair = r.Context?.Hair;
            if (hair == null || part < 0 || part >= hair.Length || !hair[part]) return null;
            var cmp = hair[part].GetComponent<CmpHair>();
            if (cmp == null || cmp.boneInfo == null || index < 0 || index >= cmp.boneInfo.Count || cmp.boneInfo[index] == null) return null;
            var t = cmp.boneInfo[index].trfCorrect;
            return t ? t : null;
        }

        /// <summary>The markers shown now: (address, boneInfo index, correction transform).</summary>
        IEnumerable<(string addr, int index, Transform trf)> HairMarkers()
        {
            var l = HairList();
            if (l == null) yield break;
            foreach (int i in l.HandleIndices())
            {
                var t = HairTransform(l.Primary, l.Part, i);
                if (t) yield return (StudioOps.HairSub(l.Primary, l.Part, i), i, t);
            }
        }

        /// <summary>The handle target of a hair sub-selection; handled (true) with ok false while its marker is not shown.</summary>
        bool HairTarget(string sub, ref Target t, out bool ok)
        {
            ok = false;
            if (!StudioOps.TryParseHairSub(sub, out var id, out int p, out int i)) return false;
            var l = HairList();
            if (l == null || l.Primary != id || l.Part != p || !l.HandleIndices().Contains(i)) return true;
            var trf = HairTransform(id, p, i);
            if (!trf) return true;
            var parent = trf.parent;
            t = new Target
            {
                NodeId = id, Sub = sub, SubKind = HairSubKind, SubName = InspectorSchemas.HairParts[p] + "/" + i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Holder = trf, Pos = trf.position, Rot = parent ? parent.rotation : Quaternion.identity,
            };
            ok = true;
            return true;
        }

        /// <summary>Move or rotate (the scale tool shows move; Select keeps the markers only).</summary>
        static GizmoTool HairTool(GizmoTool tool) => tool == GizmoTool.Select ? GizmoTool.Select : tool == GizmoTool.Rotate ? GizmoTool.Rotate : GizmoTool.Move;

        /// <summary>The nearest shown marker within 8 logical px, as a Sub handle.</summary>
        bool HairHit(Vector2 p, out GizmoHandle h)
        {
            var sw = Stopwatch.StartNew();
            h = new GizmoHandle { Kind = HandleKind.None, Axis = -1 };
            string hit = null;
            if (Cam)
            {
                float best = SubPx;
                foreach (var (addr, _, trf) in HairMarkers())
                {
                    var sp = ToPanel(trf.position);
                    if (sp.z <= 0) continue;
                    float d = ((Vector2)sp - p).magnitude;
                    if (d <= best) { best = d; hit = addr; }
                }
            }
            HairHitMs = sw.Elapsed.TotalMilliseconds;
            if (hit == null) return false;
            h = new GizmoHandle { Kind = HandleKind.Sub, Axis = -1, Sub = hit };
            return true;
        }

        void HairDraws(GizmoPass pass, Camera cam)
        {
            int n = 0;
            string sub = ctx.SubSelection;
            float k = Mathf.Lerp(0.5f, 1.5f, ctx.Options.Float(OptionDefs.GizmoFkSize));
            foreach (var (addr, _, trf) in HairMarkers())
            {
                var pos = trf.position;
                float size = 8 * WorldPerPx(pos) * k;
                bool hov = HoverHandle.Kind == HandleKind.Sub && HoverHandle.Sub == addr;
                pass.Add(new GizmoDraw { Mesh = GizmoMeshes.Octahedron, Matrix = Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one * size), Color = addr == sub || hov ? HoverColor : HairMarker });
                n++;
            }
            HairMarkersDrawn = n;
        }

        // ================================================================ drag

        /// <summary>Pointer down on the handle: the bundle's facts and its effective rates at the start.</summary>
        void HairBegin(in Target t)
        {
            hairFacts = null;
            if (!StudioOps.TryParseHairSub(t.Sub, out var id, out hairPart, out hairIndex)) return;
            var l = HairList();
            var f = l?.FactsOfBundle(hairIndex);
            var eff = l?.EffectiveOf(id);
            if (f == null || eff == null || hairIndex >= eff.Count) return;
            hairFacts = f;
            hairStartMove = eff[hairIndex].Move;
            hairStartRotate = eff[hairIndex].Rotate;
            hairAngle = 0;
        }

        static float C(Vec3 v, int a) => a == 0 ? v.X : a == 1 ? v.Y : v.Z;

        /// <summary>The dragged components of a move handle (axis: one; plane: the two other than its normal; free: all).</summary>
        static bool[] MovedAxes(GizmoHandle h)
        {
            var r = new bool[3];
            if (h.Kind == HandleKind.MoveAxis && h.Axis >= 0) r[h.Axis] = true;
            else if (h.Kind == HandleKind.MovePlane && h.Axis >= 0) { r[(h.Axis + 1) % 3] = true; r[(h.Axis + 2) % 3] = true; }
            else if (h.Kind == HandleKind.MoveFree) r[0] = r[1] = r[2] = true;
            return r;
        }

        /// <summary>A drag step of a bundle handle: the rates of the dragged components (clamped to the limits) through
        /// StudioOps.EditHairBundle inside the drag's edit (the flush follows in ApplySub).</summary>
        OpResult HairApply(Vector3? worldPos, Quaternion? worldRot)
        {
            var sw = Stopwatch.StartNew();
            var t = dragTarget;
            var f = hairFacts;
            var trf = f != null ? HairTransform(t.NodeId, hairPart, hairIndex) : null;
            if (f == null || !trf) return OpResult.NothingToDo();
            Vec3? move = null, rot = null;
            var h = ActiveHandle;
            if (worldPos.HasValue)
            {
                var parent = trf.parent;
                var local = parent ? parent.InverseTransformPoint(worldPos.Value) : worldPos.Value;
                // the handle stands on the corrected transform; the rate is the pose under the correction
                local = AdvancedPoseDriver.WithoutPosition(ctx.Runtime?.FindChara(t.NodeId), trf, local);
                var axes = MovedAxes(h);
                float[] r = { hairStartMove.X, hairStartMove.Y, hairStartMove.Z };
                for (int a = 0; a < 3; a++)
                {
                    if (!axes[a]) continue;
                    float lo = C(f.PosMin, a), hi = C(f.PosMax, a);
                    float v = Mathf.Clamp(local[a], Mathf.Min(lo, hi), Mathf.Max(lo, hi));
                    r[a] = MakerEdits.InverseLerp(lo, hi, v);
                }
                move = new Vec3(r[0], r[1], r[2]);
            }
            else if (worldRot.HasValue && h.Kind == HandleKind.RotateRing && h.Axis >= 0)
            {
                int a = h.Axis;
                float lo = C(f.RotMin, a), hi = C(f.RotMax, a);
                float start = Mathf.LerpUnclamped(lo, hi, C(hairStartRotate, a));
                float e = Mathf.Clamp(start + hairAngle, Mathf.Min(lo, hi), Mathf.Max(lo, hi));
                float[] r = { hairStartRotate.X, hairStartRotate.Y, hairStartRotate.Z };
                r[a] = MakerEdits.InverseLerp(lo, hi, e);
                rot = new Vec3(r[0], r[1], r[2]);
            }
            if (move == null && rot == null) return OpResult.NothingToDo();
            var res = ctx.Ops.EditHairBundle(new[] { t.NodeId }, hairPart, hairIndex, move, rot, null, false, -1, UiContext.User(OpLabels.EditHair));
            HairDragMs = sw.Elapsed.TotalMilliseconds;
            return res;
        }

        // ================================================================ state (ui.state gizmo.handles)

        /// <summary>
        /// The panel positions of the shown markers ({sub, kind "marker", panel [x, y]}) and, for a sub-selected bundle, of its
        /// handle's axis tips ({sub, kind "axis", axis x / y / z, tool move / rotate, panel}: a point on the arrow's cone, or a
        /// front point of the ring away from the other rings) so that a client can pick and drag through ui.input pointer.
        /// </summary>
        public List<Dictionary<string, object>> HairHandleStates()
        {
            var l = AccessoryHandleStates();   // Phase 4.5 package W: the accessory set handles (never shown with the hair bundles)
            BoneHandleStates(l);   // Phase 6.3 package U: the pick's spheres and a bone's handle
            if (ctx.Runtime == null || !ctx.Options.Bool(OptionDefs.ViewGizmos)) return l;
            foreach (var (addr, i, trf) in HairMarkers())
            {
                var sp = ToPanel(trf.position);
                if (sp.z <= 0) continue;
                l.Add(new Dictionary<string, object> { ["sub"] = addr, ["kind"] = "marker", ["index"] = i, ["panel"] = P2(sp) });
            }
            if (!Cam || !TryTarget(out var t) || t.SubKind != HairSubKind) return l;
            var tool = ToolFor(t);
            if (tool == GizmoTool.Select) return l;
            var axes = AxesFor(t, tool);
            float L = ctx.Options.Int(OptionDefs.GizmoSize) * WorldPerPx(t.Pos);
            var cam = Cam;
            for (int i = 0; i < 3; i++)
            {
                var a = axes * AxisVec(i);
                Vector3 w;
                if (tool == GizmoTool.Move) w = t.Pos + a * L * 0.9f;
                else w = RingPoint(t.Pos, axes, i, L, cam);
                var sp = ToPanel(w);
                if (sp.z <= 0) continue;
                l.Add(new Dictionary<string, object> { ["sub"] = t.Sub, ["kind"] = "axis", ["axis"] = "xyz"[i].ToString(), ["tool"] = tool == GizmoTool.Move ? "move" : "rotate", ["panel"] = P2(sp) });
            }
            return l;
        }

        static double[] P2(Vector3 v) => new double[] { Math.Round(v.x, 2), Math.Round(v.y, 2) };

        /// <summary>A front point of ring <paramref name="i"/> (camera side) farthest in panel px from the other two rings.</summary>
        Vector3 RingPoint(Vector3 c, Quaternion axes, int i, float r, Camera cam)
        {
            var axis = axes * AxisVec(i);
            Vector3 u = Vector3.Cross(axis, Mathf.Abs(Vector3.Dot(axis, Vector3.up)) > 0.9f ? Vector3.right : Vector3.up).normalized;
            Vector3 v = Vector3.Cross(axis, u);
            var camPos = cam.transform.position;
            var toCam = (camPos - c).normalized;
            Vector3 best = c + u * r;
            float bestScore = float.MinValue;
            // points clearly on the camera side first (the hit test prefers the front half); an edge-on ring takes any front point
            for (int pass = 0; pass < 2 && bestScore == float.MinValue; pass++)
                for (int s = 0; s < 32; s++)
                {
                    float ang = s * Mathf.PI * 2 / 32;
                    var w = c + (u * Mathf.Cos(ang) + v * Mathf.Sin(ang)) * r;
                    float front = Vector3.Dot(w - c, toCam);
                    if (front < (pass == 0 ? 0.2f * r : 0f)) continue;
                    var sp = ToPanel(w);
                    if (sp.z <= 0) continue;
                    float score = float.MaxValue;
                    for (int j = 0; j < 3; j++)
                    {
                        if (j == i) continue;
                        score = Mathf.Min(score, RingDist(sp, c, axes * AxisVec(j), r, camPos, true));
                    }
                    if (score > bestScore) { bestScore = score; best = w; }
                }
            return best;
        }
    }
}
