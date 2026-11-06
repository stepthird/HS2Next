// Accessory transform-set handles (decision XQ15; Phase 4.5 package W), a part of the
// transform gizmo (TransformTool; the view camera's gizmo pass: never in captures):
//   Markers   while the Edit tab shows the Accessories page of the primary with the selected slot's Adjust section open and the
//             app option maker.accessoryHandles is on (default on; CharaEditTab.AccessoryHandleTarget): a small octahedron (the
//             FK marker shape, the accessory colour) at trfMove01 and trfMove02 of the selected slot's object (those the prefab
//             has). Picking one (≤ 8 logical px, the 3.5 rule) or a set's handle button sets the sub-selection
//             `<id>/accmove/<accessoryId>/<set>` (set 0 = trfMove01, 1 = trfMove02).
//   Handle    the sub-selected set shows the move or the rotate handle (the 3.5 tool state; the scale tool shows move, HS2's
//             gizmo has no scale) in the parent space of the set's transform (forced: no view ring, no trackball). A drag is one
//             transaction "Edit accessory" (pointer down → UiEdit; each move = StudioOps.EditAppearance of the set's position or
//             rotation on the primary; up commits one entry; Esc cancels). Move: the new parent-local position × 10 per dragged
//             axis, clamped to −100..100 and rounded to 0.1 (stored tenths; axis: one component, plane: two, free: three); rotate:
//             the ring of axis a gives the signed angle δ since the drag start, the new local Euler component = the start's + δ,
//             truncated to an integer and wrapped into [0, 360); the other components keep their values.
//   State     AccessoryHandleStates(): the markers' and the shown handle's axis tips' panel positions (ui.state gizmo.handles,
//             listed with the hair bundle handles; the two never show together).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using AIChara;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Binding;
using HS2Next.UI.Core;
using HS2Next.UI.Panels;
using UnityEngine;

namespace HS2Next.UI.Gizmos
{
    public sealed partial class TransformTool
    {
        /// <summary>The accessory set marker colour.</summary>
        public static readonly Color AccessoryMarker = new Color32(240, 150, 50, 255);
        public const string AccessorySubKind = StudioOps.AccessorySubKind;

        /// <summary>Markers drawn in the last frame, the time of the last marker hit test and of the last drag step (op + flush,
        /// ms; XU7).</summary>
        public int AccessoryMarkersDrawn { get; private set; }
        public double AccessoryHitMs { get; private set; }
        public double AccessoryDragMs { get; private set; }

        // drag state of a set handle
        int accSet = -1;
        string accId;
        Vector3 accStartPos, accStartRot;

        /// <summary>The (character, accessory, slot) whose set handles are shown, or false.</summary>
        bool AccessoryShown(out string charaId, out string accessoryId, out int slot)
        {
            charaId = accessoryId = null; slot = -1;
            var tab = CharaEditTab.Current;
            if (tab == null || ctx.Runtime == null || !tab.AccessoryHandleTarget(out charaId, out accessoryId, out slot)) return false;
            return charaId == ctx.Selection.Primary;
        }

        /// <summary>trfMove01 (set 0) / trfMove02 (set 1) of the object in build slot <paramref name="slot"/> of a built character.</summary>
        Transform AccessorySetTransform(string id, int slot, int set)
        {
            var r = ctx.Runtime?.FindChara(id);
            if (r == null || r.State != RealizationState.Built || !r.Root || r.Context == null) return null;
            var go = r.Context.AccessoryObject(slot);
            if (!go) return null;
            var cmp = go.GetComponent<CmpAccessory>();
            if (cmp == null) return null;
            var t = set == 0 ? cmp.trfMove01 : cmp.trfMove02;
            return t ? t : null;
        }

        /// <summary>The markers shown now: (address, set, transform).</summary>
        IEnumerable<(string addr, int set, Transform trf)> AccessoryMarkers()
        {
            if (!AccessoryShown(out var cid, out var aid, out int slot)) yield break;
            for (int s = 0; s < 2; s++)
            {
                var t = AccessorySetTransform(cid, slot, s);
                if (t) yield return (StudioOps.AccessorySub(cid, aid, s), s, t);
            }
        }

        /// <summary>The handle target of an accessory sub-selection; handled (true) with ok false while its marker is not shown.</summary>
        bool AccessoryTarget(string sub, ref Target t, out bool ok)
        {
            ok = false;
            if (!StudioOps.TryParseAccessorySub(sub, out var id, out var aid, out int set)) return false;
            if (!AccessoryShown(out var cid, out var shownId, out int slot) || cid != id || shownId != aid) return true;
            var trf = AccessorySetTransform(id, slot, set);
            if (!trf) return true;
            var parent = trf.parent;
            t = new Target
            {
                NodeId = id, Sub = sub, SubKind = AccessorySubKind, SubName = aid + "/" + set.ToString(CultureInfo.InvariantCulture),
                Holder = trf, Pos = trf.position, Rot = parent ? parent.rotation : Quaternion.identity,
            };
            ok = true;
            return true;
        }

        /// <summary>Move or rotate (the scale tool shows move; Select keeps the markers only).</summary>
        static GizmoTool AccessoryTool(GizmoTool tool) => tool == GizmoTool.Select ? GizmoTool.Select : tool == GizmoTool.Rotate ? GizmoTool.Rotate : GizmoTool.Move;

        /// <summary>The nearest shown marker within 8 logical px, as a Sub handle.</summary>
        bool AccessoryHit(Vector2 p, out GizmoHandle h)
        {
            var sw = Stopwatch.StartNew();
            h = new GizmoHandle { Kind = HandleKind.None, Axis = -1 };
            string hit = null;
            if (Cam)
            {
                float best = SubPx;
                foreach (var (addr, _, trf) in AccessoryMarkers())
                {
                    var sp = ToPanel(trf.position);
                    if (sp.z <= 0) continue;
                    float d = ((Vector2)sp - p).magnitude;
                    if (d <= best) { best = d; hit = addr; }
                }
            }
            AccessoryHitMs = sw.Elapsed.TotalMilliseconds;
            if (hit == null) return false;
            h = new GizmoHandle { Kind = HandleKind.Sub, Axis = -1, Sub = hit };
            return true;
        }

        void AccessoryDraws(GizmoPass pass, Camera cam)
        {
            int n = 0;
            string sub = ctx.SubSelection;
            float k = Mathf.Lerp(0.5f, 1.5f, ctx.Options.Float(OptionDefs.GizmoFkSize));
            foreach (var (addr, _, trf) in AccessoryMarkers())
            {
                var pos = trf.position;
                float size = 8 * WorldPerPx(pos) * k;
                bool hov = HoverHandle.Kind == HandleKind.Sub && HoverHandle.Sub == addr;
                pass.Add(new GizmoDraw { Mesh = GizmoMeshes.Octahedron, Matrix = Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one * size), Color = addr == sub || hov ? HoverColor : AccessoryMarker });
                n++;
            }
            AccessoryMarkersDrawn = n;
        }

        // ================================================================ drag

        string AccessoryPath(string member) => InspectorSchemas.AccessoryEditPath(accId, "moves[" + accSet.ToString(CultureInfo.InvariantCulture) + "]." + member);

        /// <summary>Pointer down on the handle: the set's stored position and rotation at the start.</summary>
        void AccessoryBegin(in Target t)
        {
            accSet = -1;
            if (!StudioOps.TryParseAccessorySub(t.Sub, out var cid, out accId, out int set)) return;
            accSet = set;
            accStartPos = ReadVec(cid, AccessoryPath("position"), Vector3.zero);
            accStartRot = ReadVec(cid, AccessoryPath("rotation"), Vector3.zero);
        }

        Vector3 ReadVec(string id, string path, Vector3 fallback) =>
            FieldAccessor.TryGet(ctx.Model, id, path, out var v, out _) && FieldValues.ToVector3(v) is Vector3 r ? r : fallback;

        /// <summary>A drag step of a set handle: the dragged components of the set's position (× 10, −100..100, 0.1) or the ring's
        /// Euler component (start + δ, integer, [0, 360)) through StudioOps.EditAppearance inside the drag's edit.</summary>
        OpResult AccessoryApply(Vector3? worldPos, Quaternion? worldRot)
        {
            var sw = Stopwatch.StartNew();
            var t = dragTarget;
            if (accSet < 0 || accId == null || !t.Holder) return OpResult.NothingToDo();
            var trf = t.Holder;
            var h = ActiveHandle;
            string path;
            Vector3 v;
            if (worldPos.HasValue)
            {
                var parent = trf.parent;
                var local = parent ? parent.InverseTransformPoint(worldPos.Value) : worldPos.Value;
                // the handle stands on the corrected transform; the stored position is the pose under the correction
                local = AdvancedPoseDriver.WithoutPosition(ctx.Runtime?.FindChara(t.NodeId), trf, local);
                var axes = MovedAxes(h);
                v = accStartPos;
                for (int a = 0; a < 3; a++)
                {
                    if (!axes[a]) continue;
                    double stored = Math.Max(-InspectorSchemas.AccessoryPositionLimit, Math.Min(InspectorSchemas.AccessoryPositionLimit, local[a] * 10.0));
                    v[a] = (float)Math.Round(stored, 1, MidpointRounding.AwayFromZero);
                }
                path = AccessoryPath("position");
            }
            else if (worldRot.HasValue && h.Kind == HandleKind.RotateRing && h.Axis >= 0)
            {
                int a = h.Axis;
                v = accStartRot;
                double e = Math.Truncate(accStartRot[a] + (double)hairAngle) % 360.0;
                if (e < 0) e += 360.0;
                v[a] = (float)e;
                path = AccessoryPath("rotation");
            }
            else return OpResult.NothingToDo();
            if (!FieldAccessor.TryGet(ctx.Model, t.NodeId, path, out var cur, out _) || cur == null) return OpResult.NothingToDo();
            object nv = cur;
            for (int a = 0; a < 3; a++) nv = FieldValues.WithComponent(nv, a, v[a]);
            var res = ctx.Ops.EditAppearance(new[] { t.NodeId }, new List<(string, object)> { (path, nv) }, UiContext.User(OpLabels.EditAccessory));
            AccessoryDragMs = sw.Elapsed.TotalMilliseconds;
            return res;
        }

        // ================================================================ state (ui.state gizmo.handles)

        /// <summary>
        /// The panel positions of the shown set markers ({sub, kind "marker", set, panel [x, y]}) and, for a sub-selected set, of
        /// its handle's axis tips ({sub, kind "axis", axis x / y / z, tool move / rotate, panel}) so that a client can pick and
        /// drag through ui.input pointer.
        /// </summary>
        public List<Dictionary<string, object>> AccessoryHandleStates()
        {
            var l = new List<Dictionary<string, object>>();
            if (ctx.Runtime == null || !ctx.Options.Bool(OptionDefs.ViewGizmos)) return l;
            foreach (var (addr, s, trf) in AccessoryMarkers())
            {
                var sp = ToPanel(trf.position);
                if (sp.z <= 0) continue;
                l.Add(new Dictionary<string, object> { ["sub"] = addr, ["kind"] = "marker", ["set"] = s, ["panel"] = P2(sp) });
            }
            if (!Cam || !TryTarget(out var t) || t.SubKind != AccessorySubKind) return l;
            var tool = ToolFor(t);
            if (tool == GizmoTool.Select) return l;
            var axes = AxesFor(t, tool);
            float L = ctx.Options.Int(OptionDefs.GizmoSize) * WorldPerPx(t.Pos);
            var cam = Cam;
            for (int i = 0; i < 3; i++)
            {
                var a = axes * AxisVec(i);
                Vector3 w = tool == GizmoTool.Move ? t.Pos + a * L * 0.9f : RingPoint(t.Pos, axes, i, L, cam);
                var sp = ToPanel(w);
                if (sp.z <= 0) continue;
                l.Add(new Dictionary<string, object> { ["sub"] = t.Sub, ["kind"] = "axis", ["axis"] = "xyz"[i].ToString(), ["tool"] = tool == GizmoTool.Move ? "move" : "rotate", ["panel"] = P2(sp) });
            }
            return l;
        }
    }
}
