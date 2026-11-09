// The transform gizmo (U6–U10, U13): handles on the primary (or the sub-selection) at its
// pivot, size constant on screen (gizmo.size logical px, U7), colours X / Y / Z (220, 60, 60) (90, 200, 70) (60, 120, 230),
// hover yellow; channels the target lacks are not drawn (the first enabled tool is shown instead, the global tool stays).
// Space: Auto (move world, rotate / scale local; IK targets in the character root's space), World, Local, Parent; pivot
// Individual / Selection centre. Hit testing analytic in panel px on this frame's projected handles: arrows ≤ 8 px (cone
// ≤ 10), plane squares point-in-quad, rings ≤ 6 px (front half preferred), centre ≤ 10 px; ties centre > axis > plane >
// ring; then sub-object handles (FK octahedra, IK spheres, look disc ≤ 8 px).
// Drag math (U6): move axis = closest point between the pick ray and the axis line through the drag start (|dot| > 0.98:
// screen delta on the projected axis); plane / free = ray–plane hits; ring = signed angle in the ring plane (edge-on
// |dot| < 0.1: 0.5 °/px along the tangent), view ring about the view axis, trackball 0.5 °/px about camera up / right;
// scale factor = 1 + screen distance along the projected axis / its screen length (multiplicative, clamped to
// [0.01, 9,999,999]). Ctrl inverts the snap toggle (move 0.1, rotate 15°, scale 0.1, relative to the drag start), Shift ×0.1;
// hs2studio: V held = vanilla V-snap (absolute rounding of the moved coordinate, step gizmo.hs2SnapStep).
// Multi-selection (U10): the same world delta for each top-most node (descendants of selected nodes skipped), the same
// rotation about each origin (Individual) or about the selection centre, the same factor; kinds without the channel skipped.
// Writes: pointer down → StudioOps.BeginTransformDrag (one bus transaction "Transform"); FK: "FK bones" (Ops.SetFkBones),
// IK: "IK targets" (Ops.SetIkTargets, world space), look: "Look target" (Ops.SetLookTarget), each inside one UiEdit; each move
// sets the new values (previews: the inspector follows); pointer up → commit (one entry); Esc / lost capture → cancel.
// Route points and aids (Phase 3.6 package R): RouteHandles.cs (this partial class) — one-line hooks below.
// Hair bundle handles (Phase 4.3 package W): HairBundleHandles.cs (this partial class) — hooks marked.
// Accessory transform-set handles (Phase 4.5 package W): AccessoryHandles.cs — hooks marked.
// The handle of a bone of advanced pose and the pick in the view (Phase 6.3 package U): BoneHandles.cs — hooks marked.
using System;
using System.Collections.Generic;
using System.Linq;
using HS2Next.Motion.Fk;
using HS2Next.Motion.Ik;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Binding;
using HS2Next.UI.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Gizmos
{
    public enum HandleKind { None, MoveAxis, MovePlane, MoveFree, RotateRing, RotateView, RotateTrackball, ScaleAxis, ScaleUniform, Sub }

    public struct GizmoHandle
    {
        public HandleKind Kind;
        /// <summary>0 X, 1 Y, 2 Z (planes: the normal axis); -1 none.</summary>
        public int Axis;
        /// <summary>Sub-object address for Sub handles.</summary>
        public string Sub;
        public override string ToString() => Kind + (Axis >= 0 ? "[" + "XYZ"[Axis] + "]" : "") + (Sub != null ? " " + Sub : "");
    }

    public sealed partial class TransformTool
    {
        public static readonly Color AxisX = new Color32(220, 60, 60, 255), AxisY = new Color32(90, 200, 70, 255), AxisZ = new Color32(60, 120, 230, 255);
        public static readonly Color HoverColor = new Color32(255, 210, 60, 255), ViewRing = new Color(1, 1, 1, 0.8f);
        public static readonly Color IkEffector = new Color32(230, 60, 60, 255), IkBend = new Color32(60, 110, 230, 255), LookColor = new Color32(220, 60, 220, 255);
        static readonly Color[] FkPalette =
        {
            new Color32(170, 100, 230, 255), new Color32(240, 150, 50, 255), new Color32(240, 120, 180, 255), new Color32(70, 200, 220, 255),
            new Color32(90, 200, 90, 255), new Color32(90, 200, 90, 255), new Color32(230, 210, 60, 255),
        };
        const float ArrowPx = 8, ConePx = 10, RingPx = 6, CentrePx = 10, SubPx = 8;
        public const float MinScale = 0.01f, MaxScale = 9999999f;

        readonly UiContext ctx;
        public bool Dragging { get; private set; }
        public GizmoHandle HoverHandle { get; private set; }
        public GizmoHandle ActiveHandle { get; private set; }
        /// <summary>Last frame's pivot in world space and handle length in world units (tests, ui.state).</summary>
        public Vector3 PivotWorld { get; private set; }
        public float LengthWorld { get; private set; }
        public Quaternion Axes { get; private set; } = Quaternion.identity;
        public bool Visible { get; private set; }
        /// <summary>The tool drawn (the global tool, or the first channel the primary has).</summary>
        public GizmoTool ShownTool { get; private set; }
        /// <summary>Drags committed / cancelled so far (tests).</summary>
        public int Commits { get; private set; }
        public int Cancels { get; private set; }

        public TransformTool(UiContext ctx) { this.ctx = ctx; }

        Camera Cam => ctx.Viewport?.ViewCamera;
        float PanelScale => ctx.Viewport?.PanelScale ?? 1f;

        // ================================================================ target and frame

        struct Target
        {
            public string NodeId;          // the primary (or the sub-object's character)
            public string Sub;             // sub-object address or null
            public string SubKind, SubName;
            public Transform Holder;       // node holder or sub-object transform
            public Vector3 Pos;
            public Quaternion Rot;
            public bool HasScale;
        }

        bool TryTarget(out Target t)
        {
            t = default;
            if (ctx.Runtime == null || !ctx.Options.Bool(OptionDefs.ViewGizmos)) return false;
            string sub = ctx.SubSelection;
            if (sub != null && RouteTarget(sub, ref t, out bool routeOk)) return routeOk;   // Phase 3.6 package R
            if (sub != null && HairTarget(sub, ref t, out bool hairOk)) { if (hairOk) return true; sub = null; }   // Phase 4.3 package W (hidden: the primary)
            if (sub != null && AccessoryTarget(sub, ref t, out bool accOk)) { if (accOk) return true; sub = null; }   // Phase 4.5 package W (hidden: the primary)
            if (sub != null && BoneTarget(sub, ref t, out bool boneOk)) { if (boneOk) return true; sub = null; }   // Phase 6.3 package U (a bone this character lacks: the primary)
            if (sub != null && StudioOps.TryParseSub(sub, out var cid, out var kind, out var name))
            {
                if (!SubTransform(cid, kind, name, out var pos, out var rot)) return false;
                t = new Target { NodeId = cid, Sub = sub, SubKind = kind, SubName = name, Pos = pos, Rot = rot };
                return true;
            }
            string id = ctx.Selection.Primary;
            var n = id != null ? ctx.Model.Find(id) : null;
            var h = id != null ? ctx.Runtime.HolderOf(id) : null;
            if (n == null || !h) return false;
            t = new Target { NodeId = id, Holder = h, Pos = h.position, Rot = h.rotation, HasScale = StudioOps.HasScaleChannel(n) };
            if (ctx.Pivot == GizmoPivot.SelectionCentre && ctx.Selection.Count > 1) t.Pos = SelectionCentre();
            return true;
        }

        Vector3 SelectionCentre()
        {
            var sum = Vector3.zero; int k = 0;
            foreach (var id in TopMost())
            {
                var h = ctx.Runtime.HolderOf(id);
                if (h) { sum += h.position; k++; }
            }
            return k > 0 ? sum / k : Vector3.zero;
        }

        /// <summary>The selected nodes whose ancestors are not selected (U10), primary first.</summary>
        public List<string> TopMost()
        {
            var sel = new HashSet<string>(ctx.Selection.Ids);
            var l = new List<string>();
            foreach (var id in ctx.Selection.Ids)
            {
                var n = ctx.Model.Find(id);
                if (n == null) continue;
                bool under = false;
                for (var p = n.Parent; p != null; p = p.Parent) if (sel.Contains(p.Id)) { under = true; break; }
                if (!under) l.Add(id);
            }
            return l;
        }

        bool SubTransform(string id, string kind, string name, out Vector3 pos, out Quaternion rot)
        {
            pos = default; rot = Quaternion.identity;
            var r = ctx.Runtime.FindChara(id);
            if (r == null || r.State != RealizationState.Built || !r.Root) return false;
            if (kind == "fk")
            {
                var fk = FkDriver.Of(r);
                var b = fk?.Table?.Find(name);
                if (b == null || !b.Bone) return false;
                pos = b.Bone.position; rot = b.Bone.rotation;
                return true;
            }
            if (kind == "ik")
            {
                var ik = IkDriver.Of(r);
                if (ik == null || !Enum.TryParse(name, out IkTargetId tid)) return false;
                var v = ik.Shown(tid);   // Phase 6.5: the handle stands where the solve takes the target
                var root = r.Root.transform;
                pos = root.TransformPoint(v.Pos);
                rot = root.rotation * Quaternion.Euler(v.Rot);
                return true;
            }
            if (kind == "look")
            {
                var lt = r.Look?.LookAtTarget;
                if (!lt) return false;
                pos = lt.position; rot = lt.rotation;
                return true;
            }
            return false;
        }

        /// <summary>The tool to show for the target: the global tool, or the first channel it has.</summary>
        GizmoTool ToolFor(in Target t)
        {
            var tool = ctx.Tool;
            if (tool == GizmoTool.Select) return GizmoTool.Select;
            if (t.Sub != null)
            {
                if (t.SubKind == "route") return RouteTool(t, tool);   // Phase 3.6 package R
                if (t.SubKind == HairSubKind) return HairTool(tool);   // Phase 4.3 package W
                if (t.SubKind == AccessorySubKind) return AccessoryTool(tool);   // Phase 4.5 package W
                if (t.SubKind == BoneSubKind) return tool;   // Phase 6.3 package U: move, rotate and scale
                if (t.SubKind == "fk") return GizmoTool.Rotate;
                if (t.SubKind == "look") return GizmoTool.Move;
                bool rotatable = t.SubName == nameof(IkTargetId.LeftHand) || t.SubName == nameof(IkTargetId.RightHand) || t.SubName == nameof(IkTargetId.LeftFoot) || t.SubName == nameof(IkTargetId.RightFoot);
                return tool == GizmoTool.Rotate && rotatable ? GizmoTool.Rotate : GizmoTool.Move;
            }
            if (tool == GizmoTool.Scale && !t.HasScale) return GizmoTool.Move;
            return tool;
        }

        Quaternion AxesFor(in Target t, GizmoTool tool)
        {
            var sp = ctx.Space;
            if (t.Sub != null)
            {
                if (t.SubKind == "route") return RouteAxes(t, sp);   // Phase 3.6 package R
                if (t.SubKind == HairSubKind) return t.Rot;   // Phase 4.3 package W: the bundle parent's space (forced)
                if (t.SubKind == AccessorySubKind) return t.Rot;   // Phase 4.5 package W: the set transform's parent space (forced)
                if (t.SubKind == BoneSubKind && tool == GizmoTool.Scale) return t.Rot;   // Phase 6.3 package U: a correction scales along the bone's axes
                if (sp == GizmoSpace.World) return Quaternion.identity;
                if (t.SubKind == "ik" || (sp == GizmoSpace.Auto && t.SubKind == "look"))
                {
                    var r = ctx.Runtime.FindChara(t.NodeId);
                    return r != null && r.Root ? r.Root.transform.rotation : Quaternion.identity;
                }
                return t.Rot;
            }
            switch (sp)
            {
                case GizmoSpace.World: return Quaternion.identity;
                case GizmoSpace.Local: return t.Holder ? t.Holder.rotation : Quaternion.identity;
                case GizmoSpace.Parent: return t.Holder && t.Holder.parent ? t.Holder.parent.rotation : Quaternion.identity;
                default: return tool == GizmoTool.Move ? Quaternion.identity : t.Holder ? t.Holder.rotation : Quaternion.identity;
            }
        }

        static Vector3 AxisVec(int i) => i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;
        static Color AxisColor(int i) => i == 0 ? AxisX : i == 1 ? AxisY : AxisZ;

        /// <summary>World length of a screen length (logical px) at a depth.</summary>
        public float WorldPerPx(Vector3 at)
        {
            var cam = Cam;
            if (!cam) return 0.01f;
            float d = Mathf.Max(0.01f, Vector3.Dot(at - cam.transform.position, cam.transform.forward));
            float pxPerUnit = cam.pixelHeight / (2f * d * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad));
            return PanelScale / Mathf.Max(1e-4f, pxPerUnit);
        }

        /// <summary>Panel point of a world point (z = depth; negative = behind the camera).</summary>
        public Vector3 ToPanel(Vector3 w)
        {
            var cam = Cam;
            if (!cam) return new Vector3(-1e6f, -1e6f, -1);
            var sp = cam.WorldToScreenPoint(w);
            float s = PanelScale;
            return new Vector3(sp.x / s, (Screen.height - sp.y) / s, sp.z);
        }

        Ray RayAt(Vector2 panel) => ctx.Viewport.RayAt(panel);

        // ================================================================ hit testing

        public GizmoHandle HitTest(Vector2 p)
        {
            var best = new GizmoHandle { Kind = HandleKind.None, Axis = -1 };
            if (HS2Next.UI.Panels.ConstraintBonePick.On && BonePickHit(p, out var cp)) return cp;   // Phase 6.5: on a node that need not be selected
            if (!TryTarget(out var t) || !Cam) return best;
            var tool = ToolFor(t);
            if (tool == GizmoTool.Select) return SubHit(p, t.NodeId);
            var axes = AxesFor(t, tool);
            float L = ctx.Options.Int(OptionDefs.GizmoSize) * WorldPerPx(t.Pos);
            var c = ToPanel(t.Pos);
            if (c.z <= 0) return best;
            float bestD = float.MaxValue; int bestRank = 9;
            void Offer(GizmoHandle h, float d, float limit, int rank)
            {
                if (d > limit) return;
                if (d < bestD - 0.5f || (Mathf.Abs(d - bestD) <= 0.5f && rank < bestRank)) { best = h; bestD = d; bestRank = rank; }
            }
            Vector2 c2 = c;
            if (tool == GizmoTool.Move)
            {
                if (ctx.Options.Bool(OptionDefs.ViewCentre)) Offer(new GizmoHandle { Kind = HandleKind.MoveFree, Axis = -1 }, (p - c2).magnitude, CentrePx, 0);
                for (int i = 0; i < 3; i++)
                {
                    var a = axes * AxisVec(i);
                    Vector2 tip = ToPanel(t.Pos + a * L), cone0 = ToPanel(t.Pos + a * L * 0.8f);
                    Offer(new GizmoHandle { Kind = HandleKind.MoveAxis, Axis = i }, SegDist(p, c2, cone0), ArrowPx, 1);
                    Offer(new GizmoHandle { Kind = HandleKind.MoveAxis, Axis = i }, SegDist(p, cone0, tip), ConePx, 1);
                }
                if (ctx.Options.Bool(OptionDefs.ViewPlanes))
                    for (int i = 0; i < 3; i++)
                    {
                        var u = axes * AxisVec((i + 1) % 3); var v = axes * AxisVec((i + 2) % 3);
                        var q = new Vector2[] { ToPanel(t.Pos + (u * .25f + v * .25f) * L), ToPanel(t.Pos + (u * .45f + v * .25f) * L), ToPanel(t.Pos + (u * .45f + v * .45f) * L), ToPanel(t.Pos + (u * .25f + v * .45f) * L) };
                        if (InQuad(p, q)) Offer(new GizmoHandle { Kind = HandleKind.MovePlane, Axis = i }, 0.5f, 1, 2);
                    }
            }
            else if (tool == GizmoTool.Rotate)
            {
                var cam = Cam;
                for (int i = 0; i < 3; i++)
                {
                    var a = axes * AxisVec(i);
                    Offer(new GizmoHandle { Kind = HandleKind.RotateRing, Axis = i }, RingDist(p, t.Pos, a, L, cam.transform.position, true), RingPx, 3);
                }
                bool hairSub = t.SubKind == HairSubKind || t.SubKind == AccessorySubKind;   // Phase 4.3 / 4.5 package W: no view ring, no trackball on hair bundles and accessory sets
                if (!hairSub) Offer(new GizmoHandle { Kind = HandleKind.RotateView, Axis = -1 }, RingDist(p, t.Pos, cam.transform.forward, L * 1.15f, cam.transform.position, false), RingPx, 3);
                float r = (p - c2).magnitude, rs = ((Vector2)ToPanel(t.Pos + cam.transform.right * L) - c2).magnitude;
                if (!hairSub && best.Kind == HandleKind.None && r < rs * 0.95f) { best = new GizmoHandle { Kind = HandleKind.RotateTrackball, Axis = -1 }; bestD = r; }
            }
            else if (tool == GizmoTool.Scale)
            {
                Offer(new GizmoHandle { Kind = HandleKind.ScaleUniform, Axis = -1 }, (p - c2).magnitude, CentrePx, 0);
                for (int i = 0; i < 3; i++)
                {
                    var a = axes * AxisVec(i);
                    Vector2 tip = ToPanel(t.Pos + a * L);
                    Offer(new GizmoHandle { Kind = HandleKind.ScaleAxis, Axis = i }, SegDist(p, c2, tip), ArrowPx, 1);
                    Offer(new GizmoHandle { Kind = HandleKind.ScaleAxis, Axis = i }, (p - tip).magnitude, ConePx, 1);
                }
            }
            if (best.Kind == HandleKind.None) return SubHit(p, t.NodeId);
            return best;
        }

        GizmoHandle SubHit(Vector2 p, string charaId)
        {
            var none = new GizmoHandle { Kind = HandleKind.None, Axis = -1 };
            if (RouteHit(p, out var rh)) return rh;   // Phase 3.6 package R: route point / aid markers
            if (HairHit(p, out var hh)) return hh;   // Phase 4.3 package W: hair bundle markers
            if (AccessoryHit(p, out var ah)) return ah;   // Phase 4.5 package W: accessory set markers
            if (!ctx.Options.Bool(OptionDefs.ViewKinematicHandles)) return none;
            float best = SubPx; string hit = null;
            foreach (var (addr, pos, _, _) in SubHandles())
            {
                var sp = ToPanel(pos);
                if (sp.z <= 0) continue;
                float d = ((Vector2)sp - p).magnitude;
                if (d <= best) { best = d; hit = addr; }
            }
            if (hit == null && BonePickHit(p, out var bh)) return bh;   // Phase 6.3 package U: the pick's spheres, after FK, IK and look
            return hit == null ? none : new GizmoHandle { Kind = HandleKind.Sub, Axis = -1, Sub = hit };
        }

        static float SegDist(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = ab.sqrMagnitude < 1e-6f ? 0 : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return (p - (a + ab * t)).magnitude;
        }

        static bool InQuad(Vector2 p, Vector2[] q)
        {
            bool inside = false;
            for (int i = 0, j = q.Length - 1; i < q.Length; j = i++)
                if ((q[i].y > p.y) != (q[j].y > p.y) && p.x < (q[j].x - q[i].x) * (p.y - q[i].y) / (q[j].y - q[i].y + 1e-9f) + q[i].x) inside = !inside;
            return inside;
        }

        float RingDist(Vector2 p, Vector3 c, Vector3 axis, float r, Vector3 camPos, bool frontOnly)
        {
            Vector3 u = Vector3.Cross(axis, Mathf.Abs(Vector3.Dot(axis, Vector3.up)) > 0.9f ? Vector3.right : Vector3.up).normalized;
            Vector3 v = Vector3.Cross(axis, u);
            float best = float.MaxValue;
            const int n = 64;
            Vector2 prev = default; bool havePrev = false;
            for (int i = 0; i <= n; i++)
            {
                float a = i * Mathf.PI * 2 / n;
                var w = c + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * r;
                bool front = Vector3.Dot(w - c, camPos - c) >= -0.05f * r;
                var sp = ToPanel(w);
                if (havePrev && (!frontOnly || front) && sp.z > 0) best = Mathf.Min(best, SegDist(p, prev, sp));
                prev = sp; havePrev = sp.z > 0;
            }
            return best;
        }

        // ================================================================ hover and drag

        public void Hover(Vector2 p) { if (!Dragging) HoverHandle = HitTest(p); }

        // drag state
        Vector2 startPanel;
        Ray startRay;
        Target dragTarget;
        GizmoTool dragTool;
        Quaternion dragAxes;
        float dragL;
        float startT;
        Vector3 startHit;
        bool startHitOk;
        readonly List<(string id, Transform h, Vector3 pos, Quaternion rot, Vector3 scale, bool hasScale)> starts = new List<(string, Transform, Vector3, Quaternion, Vector3, bool)>();
        StudioOps.TransformDrag drag;
        UiEdit subEdit;
        Vector3 subStartPos;
        Quaternion subStartRot;
        Quaternion subParentRot;

        /// <summary>Pointer down: starts a handle drag (true) when a handle is hit; a sub-object handle is selected (true, no drag).</summary>
        public bool TryBegin(Vector2 p, EventModifiers mods)
        {
            if (Dragging) return true;
            var h = HitTest(p);
            if (h.Kind == HandleKind.None) return false;
            if (h.Kind == HandleKind.Sub)
            {
                if (HS2Next.UI.Panels.ConstraintBonePick.Take(h.Sub)) return true;   // Phase 6.5: the selection stays
                ctx.SubSelection = h.Sub;
                ctx.Shell?.AutoShowInspector();   // a handle picked in the viewport shows the inspector tab (shared region)
                return true;
            }
            if (!TryTarget(out var t)) return false;
            // a channel the Timeline drives while it plays, or a constraint drives, refuses the drag (a toast; a paused or ended Timeline lets it)
            if (HS2Next.UI.Panels.TimelineOverrideUi.RefusesDrag(ctx, t.SubKind == "fk" ? t.NodeId : null, t.SubKind == "fk" ? t.SubName : null, t.Sub == null ? TopMost() : null, h.Kind, t.SubKind == "ik" ? t.NodeId : null, t.SubKind == "ik" ? t.SubName : null)) return true;
            if (t.SubKind == BoneSubKind && !BoneDragAllowed(t)) return true;   // Phase 6.3 package U: the mosaic rule, at the press
            dragTarget = t;
            dragTool = ToolFor(t);
            dragAxes = AxesFor(t, dragTool);
            dragL = ctx.Options.Int(OptionDefs.GizmoSize) * WorldPerPx(t.Pos);
            startPanel = p;
            startRay = RayAt(p);
            ActiveHandle = h;
            Begin(h, t);
            if (!Dragging) return false;
            Drag(p, mods);
            return true;
        }

        void Begin(GizmoHandle h, in Target t)
        {
            starts.Clear();
            if (t.Sub != null)
            {
                subEdit = ctx.BeginEdit(t.SubKind == HairSubKind ? OpLabels.EditHair : t.SubKind == AccessorySubKind ? OpLabels.EditAccessory : t.SubKind == "route" ? OpLabels.RoutePoint : t.SubKind == BoneSubKind ? OpLabels.BoneCorrection : t.SubKind == "fk" ? OpLabels.FkBones : t.SubKind == "ik" ? OpLabels.IkTargets : OpLabels.LookTarget);
                if (!subEdit.IsOpen) { ctx.ShowResult(OpResult.From(subEdit.Failure)); subEdit = null; return; }
                // an edit cancelled from elsewhere (another edit opening) ends the drag with it: the pointer's last moves write nothing
                subEdit.Ended += committed => { if (!committed && Dragging) Cancel(); };
                subStartPos = t.Pos;
                subStartRot = t.Rot;
                subParentRot = Quaternion.identity;
                if (t.SubKind == HairSubKind) HairBegin(t);   // Phase 4.3 package W
                if (t.SubKind == AccessorySubKind) AccessoryBegin(t);   // Phase 4.5 package W
                if (t.SubKind == BoneSubKind) BoneBegin(t);   // Phase 6.3 package U
                if (t.SubKind == "fk")
                {
                    var b = FkDriver.Of(ctx.Runtime.FindChara(t.NodeId))?.Table?.Find(t.SubName);
                    if (b?.Bone && b.Bone.parent) subParentRot = b.Bone.parent.rotation;
                }
            }
            else
            {
                var ids = TopMost();
                foreach (var id in ids)
                {
                    var holder = ctx.Runtime.HolderOf(id);
                    var n = ctx.Model.Find(id);
                    if (!holder || n == null) continue;
                    var tr = n.Doc.Transform;
                    starts.Add((id, holder, holder.position, holder.rotation, new Vector3(tr.Scale.X, tr.Scale.Y, tr.Scale.Z), StudioOps.HasScaleChannel(n)));
                }
                drag = ctx.Ops.BeginTransformDrag(starts.Select(s => s.id), out var fail, UiContext.User(OpLabels.Transform));
                if (drag == null || !drag.IsOpen) { ctx.ShowResult(fail); drag = null; return; }
            }
            startHitOk = false;
            var a = dragAxes * AxisVec(Math.Max(0, h.Axis));
            switch (h.Kind)
            {
                case HandleKind.MoveAxis: startT = AxisParam(startRay, t.Pos, a); break;
                case HandleKind.MovePlane: startHitOk = PlaneHit(startRay, t.Pos, a, out startHit); break;
                case HandleKind.MoveFree: startHitOk = PlaneHit(startRay, t.Pos, Cam.transform.forward, out startHit); break;
                case HandleKind.RotateRing: startHitOk = PlaneHit(startRay, t.Pos, a, out startHit); break;
                case HandleKind.RotateView: startHitOk = PlaneHit(startRay, t.Pos, Cam.transform.forward, out startHit); break;
            }
            Dragging = true;
        }

        static float AxisParam(Ray r, Vector3 p0, Vector3 a)
        {
            var w = r.origin - p0;
            float b = Vector3.Dot(r.direction, a), d = Vector3.Dot(w, a), e = Vector3.Dot(w, r.direction);
            float den = 1 - b * b;
            if (Mathf.Abs(den) < 1e-6f) return d;
            return (d - b * e) / den;
        }

        static bool PlaneHit(Ray r, Vector3 p0, Vector3 n, out Vector3 hit)
        {
            var pl = new Plane(n, p0);
            if (pl.Raycast(r, out float t)) { hit = r.GetPoint(t); return true; }
            hit = p0;
            return false;
        }

        bool SnapOn(EventModifiers mods) => ctx.Snap ^ ((mods & EventModifiers.Control) != 0);
        static bool Precise(EventModifiers mods) => (mods & EventModifiers.Shift) != 0;
        static float Round(float v, float step) => step <= 0 ? v : Mathf.Round(v / step) * step;

        bool VSnap()
        {
            var router = ctx.Host?.Router;
            return router != null && ctx.Keys.Resolver != null && ctx.Keys.Resolver.IsModifierHeld(ActionIds.GizmoSnapHold, router.Source.GetKey(KeyCode.V) ? new[] { "V" } : Array.Empty<string>(), KeyMods.None)
                   && ctx.Keys.PresetName == Keymap.Hs2StudioPreset;
        }

        public void Drag(Vector2 p, EventModifiers mods)
        {
            if (!Dragging) return;
            var ray = RayAt(p);
            var h = ActiveHandle;
            var t = dragTarget;
            var a = dragAxes * AxisVec(Math.Max(0, h.Axis));
            bool snap = SnapOn(mods), fine = Precise(mods);
            Vector2 dp = p - startPanel;
            switch (h.Kind)
            {
                case HandleKind.MoveAxis:
                case HandleKind.MovePlane:
                case HandleKind.MoveFree:
                {
                    Vector3 delta;
                    if (h.Kind == HandleKind.MoveAxis)
                    {
                        float d;
                        if (Mathf.Abs(Vector3.Dot(ray.direction, a)) > 0.98f)
                        {
                            Vector2 sa = (Vector2)ToPanel(t.Pos + a * dragL) - (Vector2)ToPanel(t.Pos);
                            d = sa.sqrMagnitude < 1e-6f ? 0 : Vector2.Dot(dp, sa.normalized) / sa.magnitude * dragL;
                        }
                        else d = AxisParam(ray, t.Pos, a) - startT;
                        if (fine) d *= 0.1f;
                        if (snap) d = Round(d, ctx.Options.Float(OptionDefs.GizmoSnapMove));
                        delta = a * d;
                    }
                    else
                    {
                        var n = h.Kind == HandleKind.MovePlane ? a : Cam.transform.forward;
                        if (!startHitOk || !PlaneHit(ray, t.Pos, n, out var hit)) return;
                        delta = hit - startHit;
                        if (fine) delta *= 0.1f;
                        if (snap)
                        {
                            var local = Quaternion.Inverse(dragAxes) * delta;
                            float st = ctx.Options.Float(OptionDefs.GizmoSnapMove);
                            local = new Vector3(Round(local.x, st), Round(local.y, st), Round(local.z, st));
                            delta = dragAxes * local;
                        }
                    }
                    ApplyMove(delta);
                    break;
                }
                case HandleKind.RotateRing:
                case HandleKind.RotateView:
                case HandleKind.RotateTrackball:
                {
                    Quaternion q;
                    if (h.Kind == HandleKind.RotateTrackball)
                    {
                        float k = fine ? 0.05f : 0.5f;
                        q = Quaternion.AngleAxis(dp.x * k, Cam.transform.up) * Quaternion.AngleAxis(dp.y * k, Cam.transform.right);
                    }
                    else
                    {
                        var axis = h.Kind == HandleKind.RotateView ? Cam.transform.forward : a;
                        float ang;
                        if (h.Kind == HandleKind.RotateRing && Mathf.Abs(Vector3.Dot(Cam.transform.forward, axis)) < 0.1f || !startHitOk)
                        {
                            var v0 = (startHitOk ? startHit : t.Pos + dragL * Cam.transform.up) - t.Pos;
                            var tan = Vector3.Cross(axis, v0).normalized;
                            Vector2 ts = (Vector2)ToPanel(t.Pos + v0 + tan * dragL * 0.1f) - (Vector2)ToPanel(t.Pos + v0);
                            ang = ts.sqrMagnitude < 1e-6f ? 0 : Vector2.Dot(dp, ts.normalized) * 0.5f;
                        }
                        else
                        {
                            if (!PlaneHit(ray, t.Pos, axis, out var hit)) return;
                            ang = Vector3.SignedAngle(startHit - t.Pos, hit - t.Pos, axis);
                        }
                        if (fine) ang *= 0.1f;
                        if (snap) ang = Round(ang, ctx.Options.Float(OptionDefs.GizmoSnapRotate));
                        q = Quaternion.AngleAxis(ang, axis);
                        hairAngle = ang;   // Phase 4.3 / 4.5 package W: a bundle or accessory-set ring writes its own Euler component
                    }
                    ApplyRotate(q);
                    break;
                }
                case HandleKind.ScaleAxis:
                case HandleKind.ScaleUniform:
                {
                    float f;
                    if (h.Kind == HandleKind.ScaleAxis)
                    {
                        Vector2 sa = (Vector2)ToPanel(t.Pos + a * dragL) - (Vector2)ToPanel(t.Pos);
                        f = sa.sqrMagnitude < 1e-6f ? 1 : 1 + Vector2.Dot(dp, sa.normalized) / sa.magnitude;
                    }
                    else
                    {
                        float len = ((Vector2)ToPanel(t.Pos + Cam.transform.right * dragL) - (Vector2)ToPanel(t.Pos)).magnitude;
                        f = 1 + (dp.x - dp.y) / Mathf.Max(1f, len);
                    }
                    if (fine) f = 1 + (f - 1) * 0.1f;
                    if (snap) f = 1 + Round(f - 1, ctx.Options.Float(OptionDefs.GizmoSnapScale));
                    ApplyScale(h.Kind == HandleKind.ScaleAxis ? h.Axis : -1, f);
                    break;
                }
            }
        }

        void ApplyMove(Vector3 delta)
        {
            var t = dragTarget;
            if (t.Sub != null)
            {
                var w = subStartPos + delta;
                if (VSnap()) w = VSnapped(w);
                ApplySub(w, null);
                return;
            }
            bool vs = VSnap();
            foreach (var s in starts)
            {
                var w = s.pos + delta;
                if (vs) w = VSnapped(w);
                var parent = s.h.parent;
                var local = parent ? parent.InverseTransformPoint(w) : w;
                ctx.ShowResult(drag.Set(s.id, local, null, null));
            }
        }

        Vector3 VSnapped(Vector3 w)
        {
            float st = 0.1f;
            float.TryParse(ctx.Options.String(OptionDefs.GizmoHs2SnapStep), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out st);
            return new Vector3(Round(w.x, st), Round(w.y, st), Round(w.z, st));
        }

        void ApplyRotate(Quaternion q)
        {
            var t = dragTarget;
            if (t.Sub != null) { ApplySub(null, q * subStartRot); return; }
            bool centre = ctx.Pivot == GizmoPivot.SelectionCentre && starts.Count > 1;
            foreach (var s in starts)
            {
                var wr = q * s.rot;
                var parent = s.h.parent;
                var lr = parent ? Quaternion.Inverse(parent.rotation) * wr : wr;
                Vector3? lp = null;
                if (centre)
                {
                    var wp = t.Pos + q * (s.pos - t.Pos);
                    lp = parent ? parent.InverseTransformPoint(wp) : wp;
                }
                ctx.ShowResult(drag.Set(s.id, lp, lr.eulerAngles, null));
            }
        }

        void ApplyScale(int axis, float f)
        {
            if (dragTarget.SubKind == BoneSubKind) { BoneApplyScale(axis, f); return; }   // Phase 6.3 package U
            foreach (var s in starts)
            {
                if (!s.hasScale) continue;
                var sc = s.scale;
                if (axis < 0) sc *= f; else sc[axis] *= f;
                for (int i = 0; i < 3; i++) sc[i] = Mathf.Clamp(sc[i], MinScale, MaxScale);
                ctx.ShowResult(drag.Set(s.id, null, null, sc));
            }
        }

        void ApplySub(Vector3? worldPos, Quaternion? worldRot)
        {
            var t = dragTarget;
            OpResult r = null;
            if (t.SubKind == HairSubKind) r = HairApply(worldPos, worldRot);   // Phase 4.3 package W
            else if (t.SubKind == AccessorySubKind) r = AccessoryApply(worldPos, worldRot);   // Phase 4.5 package W
            else if (t.SubKind == "route") r = RouteApply(t, worldPos, worldRot);   // Phase 3.6 package R
            else if (t.SubKind == BoneSubKind) r = BoneApply(worldPos, worldRot);   // Phase 6.3 package U
            else if (t.SubKind == "fk" && worldRot.HasValue)
            {
                var fkChara = ctx.Runtime.FindChara(t.NodeId);
                // Phase 6.3 package U: the handle shows the bone with its correction; the FK value is the pose under it
                var local = AdvancedPoseDriver.WithoutRotation(fkChara, FkDriver.Of(fkChara)?.Table?.Find(t.SubName)?.Bone, Quaternion.Inverse(subParentRot) * worldRot.Value);
                var e = local.eulerAngles;
                r = ctx.Ops.SetFkBones(t.NodeId, new Dictionary<string, Vec3> { [t.SubName] = new Vec3(e.x, e.y, e.z) }, UiContext.User(OpLabels.FkBones));
            }
            else if (t.SubKind == "ik" && Enum.TryParse(t.SubName, out IkTargetId tid))
            {
                Vector3? rot = worldRot.HasValue ? (worldRot.Value).eulerAngles : (Vector3?)null;
                r = ctx.Ops.SetIkTargets(t.NodeId, new Dictionary<IkTargetId, IkTargetEdit> { [tid] = new IkTargetEdit(worldPos, rot) }, IkSpace.World, UiContext.User(OpLabels.IkTargets));
            }
            else if (t.SubKind == "look" && worldPos.HasValue)
                r = ctx.Ops.SetLookTarget(t.NodeId, worldPos.Value, UiContext.User(OpLabels.LookTarget));
            if (r != null && !r.Ok) ctx.ShowResult(r);
            ctx.Runtime?.ApplyNow();
        }

        public void End()
        {
            if (!Dragging) return;
            Dragging = false;
            OpResult r;
            if (subEdit != null) { r = subEdit.Commit(); subEdit = null; }
            else { r = drag != null ? drag.Commit() : OpResult.NothingToDo(); drag = null; }
            Commits++;
            ctx.ShowResult(r);
            ActiveHandle = default;
        }

        public void Cancel()
        {
            if (!Dragging) return;
            Dragging = false;
            if (subEdit != null) { subEdit.Cancel(); subEdit = null; }
            else { drag?.Cancel(); drag = null; }
            Cancels++;
            ActiveHandle = default;
        }

        // ================================================================ drawing

        Mesh lineMesh, helperMesh;
        readonly List<Vector3> pairs = new List<Vector3>();

        /// <summary>Fills the pass for this frame (UiHost's beginCameraRendering for the view camera).</summary>
        public void Build(GizmoPass pass, Camera cam)
        {
            pass.Clear();
            Visible = false;
            RouteDraws(pass, cam);   // Phase 3.6 package R: route lines and markers (also with gizmos hidden, as HS2's route layer)
            ctx.Tools.Draw(pass, cam);   // Phase 6.6: what a viewport tool shows (also with gizmos hidden)
            if (!ctx.Options.Bool(OptionDefs.ViewGizmos)) return;
            pairs.Clear();
            Helpers(pass, cam);
            bool rest = ctx.Tools.Blocks;   // Phase 6.6: a viewport tool in its default mode: no handle shows
            if (!rest && TryTarget(out var t))
            {
                var tool = ToolFor(t);
                ShownTool = tool;
                var axes = AxesFor(t, tool);
                Axes = axes;
                float wpp = WorldPerPx(t.Pos);
                float L = ctx.Options.Int(OptionDefs.GizmoSize) * wpp;
                PivotWorld = t.Pos;
                LengthWorld = L;
                Visible = tool != GizmoTool.Select;
                var hov = Dragging ? ActiveHandle : HoverHandle;
                Color C(HandleKind k, int axis, Color baseColor) => hov.Kind == k && hov.Axis == axis ? HoverColor : baseColor;
                float th = 1.5f * wpp;
                if (tool == GizmoTool.Move)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        var rot = axes * Quaternion.FromToRotation(Vector3.up, AxisVec(i));
                        var col = C(HandleKind.MoveAxis, i, AxisColor(i));
                        pass.Add(new GizmoDraw { Mesh = GizmoMeshes.Shaft, Matrix = Matrix4x4.TRS(t.Pos, rot, new Vector3(th, L * 0.8f, th)), Color = col });
                        pass.Add(new GizmoDraw { Mesh = GizmoMeshes.Cone, Matrix = Matrix4x4.TRS(t.Pos + rot * Vector3.up * L * 0.8f, rot, new Vector3(L * 0.06f, L * 0.2f, L * 0.06f)), Color = col });
                    }
                    if (ctx.Options.Bool(OptionDefs.ViewPlanes))
                        for (int i = 0; i < 3; i++)
                        {
                            var u = axes * AxisVec((i + 1) % 3); var v = axes * AxisVec((i + 2) % 3);
                            var col = C(HandleKind.MovePlane, i, AxisColor(i)); col.a = hov.Kind == HandleKind.MovePlane && hov.Axis == i ? 0.7f : 0.35f;
                            var m = Matrix4x4.TRS(t.Pos + (u + v) * L * 0.35f, Quaternion.LookRotation(Vector3.Cross(u, v), v), new Vector3(L * 0.2f, L * 0.2f, 1));
                            pass.Add(new GizmoDraw { Mesh = GizmoMeshes.Quad, Matrix = m, Color = col });
                        }
                    if (ctx.Options.Bool(OptionDefs.ViewCentre))
                        pass.Add(new GizmoDraw { Mesh = GizmoMeshes.Quad, Matrix = Matrix4x4.TRS(t.Pos, cam.transform.rotation, Vector3.one * 10 * wpp), Color = C(HandleKind.MoveFree, -1, ViewRing) });
                }
                else if (tool == GizmoTool.Rotate)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        var rot = axes * Quaternion.FromToRotation(Vector3.up, AxisVec(i));
                        pass.Add(new GizmoDraw { Mesh = GizmoMeshes.Ring, Matrix = Matrix4x4.TRS(t.Pos, rot, Vector3.one * L), Color = C(HandleKind.RotateRing, i, AxisColor(i)) });
                    }
                    if (t.SubKind != HairSubKind && t.SubKind != AccessorySubKind) pass.Add(new GizmoDraw { Mesh = GizmoMeshes.Ring, Matrix = Matrix4x4.TRS(t.Pos, Quaternion.FromToRotation(Vector3.up, cam.transform.forward), Vector3.one * L * 1.15f), Color = C(HandleKind.RotateView, -1, ViewRing) });
                }
                else if (tool == GizmoTool.Scale)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        var rot = axes * Quaternion.FromToRotation(Vector3.up, AxisVec(i));
                        var col = C(HandleKind.ScaleAxis, i, AxisColor(i));
                        pass.Add(new GizmoDraw { Mesh = GizmoMeshes.Shaft, Matrix = Matrix4x4.TRS(t.Pos, rot, new Vector3(th, L, th)), Color = col });
                        pass.Add(new GizmoDraw { Mesh = GizmoMeshes.Cube, Matrix = Matrix4x4.TRS(t.Pos + rot * Vector3.up * L, rot, Vector3.one * L * 0.09f), Color = col });
                    }
                    pass.Add(new GizmoDraw { Mesh = GizmoMeshes.Cube, Matrix = Matrix4x4.TRS(t.Pos, axes, Vector3.one * L * 0.12f), Color = C(HandleKind.ScaleUniform, -1, ViewRing) });
                }
            }
            if (!rest)
            {
                SubHandleDraws(pass, cam);
                HairDraws(pass, cam);   // Phase 4.3 package W: hair bundle markers
                AccessoryDraws(pass, cam);   // Phase 4.5 package W: accessory set markers
            }
            BoneDraws(pass, cam);   // Phase 6.3 package U: the pick's spheres
            if (pairs.Count > 0)
            {
                lineMesh = GizmoMeshes.Lines(lineMesh, pairs);
                pass.Add(new GizmoDraw { Mesh = lineMesh, Matrix = Matrix4x4.identity, Color = new Color(1, 1, 1, 0.7f), DepthTest = true });
            }
        }

        /// <summary>FK / IK / look handles of the characters in the selection (or the sub-selection's character).</summary>
        IEnumerable<(string addr, Vector3 pos, Color color, Mesh mesh)> SubHandles()
        {
            if (!ctx.Options.Bool(OptionDefs.ViewKinematicHandles) || ctx.Runtime == null) yield break;
            bool hs2White = ctx.Options.String(OptionDefs.FkColors) == "hs2";
            foreach (var id in ctx.Selection.Ids.Take(4))
            {
                var r = ctx.Runtime.FindChara(id);
                if (r == null || r.State != RealizationState.Built || !r.Root) continue;
                var fk = FkDriver.Of(r);
                if (fk != null && fk.Enabled)
                    foreach (var b in fk.Table.Bones)
                        if (b.Bone && fk.Stage.IsApplied(b.Group))
                            yield return (id + "/fk/" + b.Key, b.Bone.position, hs2White ? Color.white : FkPalette[(int)b.Group % FkPalette.Length], GizmoMeshes.Octahedron);
                var ik = IkDriver.Of(r);
                if (ik != null && ik.Enabled)
                {
                    var root = r.Root.transform;
                    for (int i = 0; i < IkGroupInfo.TargetCount; i++)
                    {
                        var tid = (IkTargetId)i;
                        var v = ik.Shown(tid);   // Phase 6.5
                        if (!v.Has) continue;
                        bool bend = tid == IkTargetId.LeftArmChain || tid == IkTargetId.RightArmChain || tid == IkTargetId.LeftLegChain || tid == IkTargetId.RightLegChain;
                        yield return (id + "/ik/" + tid, root.TransformPoint(v.Pos), bend ? IkBend : IkEffector, GizmoMeshes.Sphere);
                    }
                }
                if (r.Look != null && r.Look.LookTargetSizeInteractable && r.Look.LookAtTarget)
                    yield return (id + "/look", r.Look.LookAtTarget.position, LookColor, GizmoMeshes.Disc);
            }
        }

        void SubHandleDraws(GizmoPass pass, Camera cam)
        {
            string sub = ctx.SubSelection;
            foreach (var (addr, pos, color, mesh) in SubHandles())
            {
                float wpp = WorldPerPx(pos);
                float size = 8 * wpp * Mathf.Lerp(0.5f, 1.5f, ctx.Options.Float(OptionDefs.GizmoFkSize));
                var c = addr == sub ? HoverColor : color;
                var rot = mesh == GizmoMeshes.Disc ? cam.transform.rotation : Quaternion.identity;
                pass.Add(new GizmoDraw { Mesh = mesh, Matrix = Matrix4x4.TRS(pos, rot, Vector3.one * size), Color = c });
            }
        }

        /// <summary>Light helpers, camera icons and the pivot marker.</summary>
        void Helpers(GizmoPass pass, Camera cam)
        {
            if (ctx.Runtime == null) return;
            string lightMode = ctx.Options.String(OptionDefs.ViewLightHelpers);
            bool icons = ctx.Options.Bool(OptionDefs.ViewCameraIcons);
            string active = ctx.Runtime.GetPart<SceneViewCamera>()?.ActiveCameraId;
            foreach (var n in ctx.Model.PreOrder())
            {
                if (!n.EffectiveVisible) continue;
                var h = ctx.Runtime.HolderOf(n.Id);
                if (!h) continue;
                if (n.Kind == NodeKind.Camera && icons && n.Id != active)
                {
                    float s = 24 * WorldPerPx(h.position);
                    pass.Add(new GizmoDraw { Mesh = GizmoMeshes.Frustum, Matrix = Matrix4x4.TRS(h.position, h.rotation, Vector3.one * s), Color = ctx.Selection.Contains(n.Id) ? HoverColor : Color.white, DepthTest = true });
                }
                if (n.Kind == NodeKind.Light && lightMode != "none" && (lightMode == "all" || ctx.Selection.Contains(n.Id)))
                {
                    var light = h.GetComponentInChildren<Light>();
                    if (!light) continue;
                    var p = light.transform.position;
                    var f = light.transform.forward;
                    if (light.type == LightType.Point) Circle(p, Vector3.up, light.range);
                    else if (light.type == LightType.Spot)
                    {
                        float r = Mathf.Tan(light.spotAngle * 0.5f * Mathf.Deg2Rad) * light.range;
                        var endC = p + f * light.range;
                        Circle(endC, f, r);
                        var u = Vector3.Cross(f, Vector3.up).sqrMagnitude < 1e-4f ? Vector3.right : Vector3.Cross(f, Vector3.up).normalized;
                        var v = Vector3.Cross(f, u);
                        foreach (var d in new[] { u, -u, v, -v }) { pairs.Add(p); pairs.Add(endC + d * r); }
                    }
                    else { float s = 40 * WorldPerPx(p); pairs.Add(p); pairs.Add(p + f * s); }
                }
            }
            if (ctx.Viewport != null && ctx.Viewport.Camera.Dragging && ctx.Options.Bool(OptionDefs.ViewPivotMarker))
            {
                var pv = ctx.Viewport.Camera.Pivot;
                pass.Add(new GizmoDraw { Mesh = GizmoMeshes.Disc, Matrix = Matrix4x4.TRS(pv, cam.transform.rotation, Vector3.one * 5 * WorldPerPx(pv)), Color = new Color(1, 1, 1, 0.8f) });
            }
        }

        void Circle(Vector3 c, Vector3 n, float r)
        {
            var u = Vector3.Cross(n, Mathf.Abs(Vector3.Dot(n, Vector3.up)) > 0.9f ? Vector3.right : Vector3.up).normalized;
            var v = Vector3.Cross(n, u);
            const int k = 48;
            for (int i = 0; i < k; i++)
            {
                float a0 = i * Mathf.PI * 2 / k, a1 = (i + 1) * Mathf.PI * 2 / k;
                pairs.Add(c + (u * Mathf.Cos(a0) + v * Mathf.Sin(a0)) * r);
                pairs.Add(c + (u * Mathf.Cos(a1) + v * Mathf.Sin(a1)) * r);
            }
        }
    }
}
