// The handle of a bone of advanced pose and the pick in the view, a part of the transform gizmo (TransformTool; the view camera's gizmo
// pass: never in captures). The sub-selection "<id>/bone/<address>" names any bone of a character.
//   Handle    the Move, Rotate and Scale tools at the bone, in the space the toolbar says (the bone's axes for Scale: a correction scales
//             along them). A drag is one UI edit, label «Bone correction», one history entry; Esc cancels. Each step, with the base the
//             driver holds, or the bone's local pose while it has no correction:
//             rotate  rot = Euler(Inverse(base) * Inverse(parent's world rotation) * world rotation)
//             move    pos = parent.InverseTransformPoint(world position) - base position
//             scale   scale = scale at the start * factor per axis, within the limits of a correction
//             A press that moves nothing writes nothing. A part that keeps the mosaic in place is refused at the press.
//   Pick      while the tab's «Pick in view» is on: small spheres, drawn on top, at the bones of AdvancedPoseTab.PickRows (grey, corrected
//             yellow, selected the hover colour). A click selects, after every FK, IK and look handle: those keep their priority. While
//             the pick is off nothing here draws and nothing is hit-tested. The option view.kinematicHandles hides the spheres too.
//             The popup of a constraint's bone runs the same pick on the node it names, selected or not, and takes the click itself.
//   State     BoneHandleStates: the spheres' and the shown handle's axis tips' panel positions (ui.state gizmo.handles).
using System.Collections.Generic;
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
        public const string BoneSubKind = StudioOps.BoneSubKind;
        /// <summary>The colours of the pick's spheres (the selected one takes <see cref="HoverColor"/>).</summary>
        public static readonly Color PickColor = new Color32(170, 170, 170, 255), PickCorrected = new Color32(236, 226, 90, 255);
        /// <summary>The pick's spheres against the size of an FK handle.</summary>
        public const float PickSizeFactor = 0.6f;

        /// <summary>Spheres drawn in the last frame, hit tests of the pick so far (tests: both stay put while the pick is off).</summary>
        public int BonePicksDrawn { get; private set; }
        public int BonePickHitTests { get; private set; }

        // drag state of a bone handle
        Transform boneDrag;
        BoneCorrection boneStart;

        CharaRealization BoneChara(string id)
        {
            var r = ctx.Runtime?.FindChara(id);
            return r != null && r.State == RealizationState.Built && !r.InGraveyard && r.Root ? r : null;
        }

        /// <summary>The handle target of a bone sub-selection; handled (true) with ok false while this character lacks the bone.</summary>
        bool BoneTarget(string sub, ref Target t, out bool ok)
        {
            ok = false;
            if (!StudioOps.TryParseSub(sub, out var id, out var kind, out var name) || kind != BoneSubKind) return false;
            var r = BoneChara(id);
            var bone = r != null ? AdvancedPoseResolver.Resolve(r, name) : null;
            if (!bone) return true;
            t = new Target
            {
                NodeId = id, Sub = sub, SubKind = BoneSubKind, SubName = PoseAddress.Canonical(PoseAddressForm.Bone, name) ?? name,
                Holder = bone, Pos = bone.position, Rot = bone.rotation,
            };
            ok = true;
            return true;
        }

        /// <summary>False, with the refusal shown, when the mosaic rule keeps the target's bone out of a correction.</summary>
        bool BoneDragAllowed(in Target t)
        {
            var r = BoneChara(t.NodeId);
            if (r == null || !t.Holder) return false;
            if (!AdvancedPoseGuard.Bone(r, t.Holder)) return true;
            ctx.ShowResult(OpResult.Unsupported(AdvancedPoseGuard.Refusal));
            return false;
        }

        /// <summary>Pointer down on the handle: the bone and its correction at the start.</summary>
        void BoneBegin(in Target t)
        {
            boneDrag = t.Holder;
            var block = (ctx.Model.Find(t.NodeId)?.State as CharaNodeState)?.AdvancedPose;
            boneStart = block != null && block.Bones.TryGetValue(t.SubName, out var c) ? new BoneCorrection(c.Pos, c.Rot, c.Scale) : new BoneCorrection();
        }

        OpResult BoneWrite(Vec3? pos, Vec3? rot, Vec3? scale)
        {
            var t = dragTarget;
            var block = (ctx.Model.Find(t.NodeId)?.State as CharaNodeState)?.AdvancedPose;
            if (block == null || subEdit == null || !subEdit.IsOpen) return OpResult.NothingToDo();
            // past the limit of the block the operation says so
            if (!block.Bones.ContainsKey(t.SubName) && block.Bones.Count >= AdvancedPoseLimits.MaxBones)
                return ctx.Ops.SetBoneCorrection(t.NodeId, t.SubName, pos, rot, scale, false, UiContext.User(OpLabels.BoneCorrection));
            return AdvancedPoseTab.DragWrite(ctx, t.NodeId, AdvancedPoseEdits.SetBone(block, t.SubName, pos, rot, scale), OpLabels.BoneCorrection);
        }

        // a value that is a rounding error away from nothing is nothing, and an offset reads best around 0
        static float CleanAngle(float a)
        {
            float d = Mathf.DeltaAngle(0f, a);
            return Mathf.Abs(d) < 1e-4f ? 0f : d;
        }

        static float CleanOffset(float v) => Mathf.Abs(v) < 1e-6f ? 0f : v;

        /// <summary>A step of a move or rotate drag.</summary>
        OpResult BoneApply(Vector3? worldPos, Quaternion? worldRot)
        {
            var bone = boneDrag;
            if (!bone) return OpResult.NothingToDo();
            var parent = bone.parent;
            var e = AdvancedPoseDriver.Of(BoneChara(dragTarget.NodeId))?.Set.Of(bone);
            if (worldPos.HasValue)
            {
                if (worldPos.Value.Equals(subStartPos)) return BoneWrite(boneStart.Pos, null, null);
                var bp = e != null ? e.BasePos : bone.localPosition;
                var d = (parent ? parent.InverseTransformPoint(worldPos.Value) : worldPos.Value) - bp;
                return BoneWrite(AdvancedPoseRanges.ClampPos(new Vec3(CleanOffset(d.x), CleanOffset(d.y), CleanOffset(d.z))), null, null);
            }
            if (worldRot.HasValue)
            {
                if (worldRot.Value.Equals(subStartRot)) return BoneWrite(null, boneStart.Rot, null);
                var br = e != null ? e.BaseRot : bone.localRotation;
                var pr = parent ? parent.rotation : Quaternion.identity;
                var a = (Quaternion.Inverse(br) * Quaternion.Inverse(pr) * worldRot.Value).eulerAngles;
                return BoneWrite(null, new Vec3(CleanAngle(a.x), CleanAngle(a.y), CleanAngle(a.z)), null);
            }
            return OpResult.NothingToDo();
        }

        /// <summary>A step of a scale drag: the factor on one axis of the bone, or on all three.</summary>
        void BoneApplyScale(int axis, float f)
        {
            var s = new Vector3(boneStart.Scale.X, boneStart.Scale.Y, boneStart.Scale.Z);
            if (axis < 0) s *= f; else s[axis] *= f;
            var r = BoneWrite(null, null, AdvancedPoseRanges.ClampScale(new Vec3(s.x, s.y, s.z)));
            if (r != null && !r.Ok) ctx.ShowResult(r);
            ctx.Runtime?.ApplyNow();
        }

        // ================================================================ the pick

        /// <summary>The spheres shown now, or null while the pick is off.</summary>
        IReadOnlyList<BonePick> BonePicks()
        {
            if (ctx.Tools.Blocks) return null;   // Phase 6.6: a viewport tool in its default mode
            if (ConstraintBonePick.On) return ConstraintBonePick.Rows;   // Phase 6.5
            var tab = AdvancedPoseTab.Current;
            if (tab == null || !tab.PickOn || ctx.Runtime == null || tab.Primary != ctx.Selection.Primary) return null;
            return ctx.Options.Bool(OptionDefs.ViewKinematicHandles) ? tab.PickRows : null;
        }

        string BonePickAddress(string address) => (ConstraintBonePick.Node ?? ctx.Selection.Primary) + "/" + BoneSubKind + "/" + address;

        /// <summary>The nearest sphere within 8 logical px, as a Sub handle.</summary>
        bool BonePickHit(Vector2 p, out GizmoHandle h)
        {
            h = new GizmoHandle { Kind = HandleKind.None, Axis = -1 };
            var picks = BonePicks();
            if (picks == null || !Cam) return false;
            BonePickHitTests++;
            float best = SubPx;
            string hit = null;
            for (int i = 0; i < picks.Count; i++)
            {
                var b = picks[i];
                if (!b.Bone) continue;
                var sp = ToPanel(b.Bone.position);
                if (sp.z <= 0) continue;
                float d = ((Vector2)sp - p).magnitude;
                if (d <= best) { best = d; hit = b.Address; }
            }
            if (hit == null) return false;
            h = new GizmoHandle { Kind = HandleKind.Sub, Axis = -1, Sub = BonePickAddress(hit) };
            return true;
        }

        void BoneDraws(GizmoPass pass, Camera cam)
        {
            int n = 0;
            var picks = BonePicks();
            if (picks != null)
            {
                string sub = ctx.SubSelection;
                float k = Mathf.Lerp(0.5f, 1.5f, ctx.Options.Float(OptionDefs.GizmoFkSize)) * PickSizeFactor;
                for (int i = 0; i < picks.Count; i++)
                {
                    var b = picks[i];
                    if (!b.Bone) continue;
                    var pos = b.Bone.position;
                    string addr = BonePickAddress(b.Address);
                    bool on = addr == sub || (HoverHandle.Kind == HandleKind.Sub && HoverHandle.Sub == addr);
                    pass.Add(new GizmoDraw { Mesh = GizmoMeshes.Sphere, Matrix = Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one * (8 * WorldPerPx(pos) * k)), Color = on ? HoverColor : b.Corrected ? PickCorrected : PickColor });
                    n++;
                }
            }
            BonePicksDrawn = n;
        }

        // ================================================================ state (ui.state gizmo.handles)

        /// <summary>
        /// Adds the panel positions of the shown spheres ({sub, kind "bone", panel [x, y]}) and, for a sub-selected bone, of its handle's axis
        /// tips ({sub, kind "axis", axis x / y / z, tool move / rotate / scale, panel}), so that a client can pick and drag through ui.input.
        /// </summary>
        public void BoneHandleStates(List<Dictionary<string, object>> l)
        {
            if (ctx.Runtime == null || !ctx.Options.Bool(OptionDefs.ViewGizmos)) return;
            var picks = BonePicks();
            if (picks != null)
                for (int i = 0; i < picks.Count; i++)
                {
                    var b = picks[i];
                    if (!b.Bone) continue;
                    var sp = ToPanel(b.Bone.position);
                    if (sp.z > 0) l.Add(new Dictionary<string, object> { ["sub"] = BonePickAddress(b.Address), ["kind"] = "bone", ["panel"] = P2(sp) });
                }
            if (!Cam || !TryTarget(out var t) || t.SubKind != BoneSubKind) return;
            var tool = ToolFor(t);
            if (tool == GizmoTool.Select) return;
            var axes = AxesFor(t, tool);
            float L = ctx.Options.Int(OptionDefs.GizmoSize) * WorldPerPx(t.Pos);
            for (int i = 0; i < 3; i++)
            {
                var w = tool == GizmoTool.Rotate ? RingPoint(t.Pos, axes, i, L, Cam) : t.Pos + axes * AxisVec(i) * L * 0.9f;
                var sp = ToPanel(w);
                if (sp.z <= 0) continue;
                l.Add(new Dictionary<string, object> { ["sub"] = t.Sub, ["kind"] = "axis", ["axis"] = "xyz"[i].ToString(), ["tool"] = tool.ToString().ToLowerInvariant(), ["panel"] = P2(sp) });
            }
        }
    }
}
