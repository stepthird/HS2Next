// The Cutter's viewport tool, begun by the panel's «Place on surface». With a tear or hole marker selected, a press on a surface moves the marker to the point the press hits, its Y
// along the surface's normal and its X along the view's right laid on the surface; while the press is held the marker follows the pointer over the surface and the wheel turns it
// about the normal, 15 degrees a notch; the release ends the move in one history entry «Move marker». Markers are not surfaces here: a press on a marker finds the surface behind
// it. A press that hits no surface, a press with a modifier or another button, and every press with no tear or hole marker selected are let through, so the camera, a click and
// the shortcuts go on as without the tool. Esc during a press puts the marker back where the press found it; Esc after that ends the tool. The tool draws nothing of its own.
using System;
using HS2Next.Api.Unity;
using HS2Next.Save;
using HS2Next.Scenes.Cut;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Binding;
using HS2Next.UI.Core;
using UnityEngine;

namespace HS2Next.UI.Panels
{
    public sealed class CutTool : ViewportTool
    {
        /// <summary>The key of the tool's hint in the status line.</summary>
        public const string HintKey = "cut.tool.hint";
        /// <summary>What a wheel notch turns the marker about the normal, in degrees.</summary>
        public const float NotchDegrees = 15f;

        readonly UiContext ctx;
        readonly Action<CutTool> ended;
        UiEdit edit;
        string moving;
        Vector3 point, normal, right;
        float twist;
        static int presses;

        public CutTool(UiContext ctx, Action<CutTool> ended = null)
        {
            this.ctx = ctx;
            this.ended = ended;
        }

        /// <summary>The marker the held press moves, or null.</summary>
        public string Moving => edit != null && edit.IsOpen ? moving : null;
        /// <summary>The turn about the normal the press has given, in degrees.</summary>
        public float Twist => twist;
        /// <summary>The last surface point and normal the press found.</summary>
        public Vector3 Point => point;
        public Vector3 Normal => normal;
        /// <summary>Presses the tool took since it began.</summary>
        public int Taken { get; private set; }

        /// <summary>The selected tear or hole marker, or null.</summary>
        string SelectedMarker()
        {
            string id = ctx.Selection.Primary;
            if (id == null || ctx.Selection.Count != 1) return null;
            var k = CutMarkers.KindOf(ctx.Model.Find(id)?.State as ItemNodeState);
            return k == CutKind.Tear || k == CutKind.Hole ? id : null;
        }

        /// <summary>The surface under the pointer with the markers left out: a hit with a triangle (Precise), on a node.</summary>
        static bool Surface(in ToolPointer p, out Vector3 at, out Vector3 n)
        {
            ToolPick hit;
            using (CutMarkers.HideForCapture()) hit = p.Hit;   // the pick runs here, with every marker switched off
            at = hit.Point;
            n = hit.Normal;
            return hit.Hit && hit.Precise && !hit.OnMap && hit.NodeId != null && n.sqrMagnitude > 0.5f;
        }

        public override bool PointerDown(in ToolPointer p)
        {
            if (p.Button != 0 || p.Mods != ToolMods.None) return false;
            string id = SelectedMarker();
            if (id == null || !Surface(in p, out var at, out var n)) return false;
            var e = ctx.BeginEdit(OpLabels.CutMoveMarker, "ui.cut/marker/" + id + "/" + ++presses);
            if (!e.IsOpen) return false;
            edit = e;
            moving = id;
            twist = 0f;
            var cam = ctx.Viewport?.ViewCamera;
            right = cam ? cam.transform.right : Vector3.right;
            Taken++;
            Place(at, n);
            return true;
        }

        public override void PointerMove(in ToolPointer p)
        {
            if (Moving == null) return;
            if (Surface(in p, out var at, out var n)) Place(at, n);
        }

        public override bool Wheel(float notches, in ToolPointer p)
        {
            if (Moving == null) return false;
            twist += NotchDegrees * notches;
            Place(point, normal);
            return true;
        }

        public override void PointerUp(in ToolPointer p)
        {
            var e = edit;
            edit = null;
            if (e != null && e.IsOpen) ctx.ShowResult(e.Commit());
        }

        public override void PointerCancel()
        {
            var e = edit;
            edit = null;
            if (e != null && e.IsOpen) e.Cancel();
        }

        /// <summary>The marker's rotation at a surface point: Y the normal, X the view's right laid on the surface, turned about the normal by the press's twist.</summary>
        public static Quaternion RotationAt(Vector3 normal, Vector3 viewRight, float twistDegrees)
        {
            var y = normal.normalized;
            var x = viewRight - Vector3.Dot(viewRight, y) * y;
            if (x.sqrMagnitude < 1e-8f) x = Vector3.Cross(y, Mathf.Abs(y.z) < 0.9f ? Vector3.forward : Vector3.right);
            x.Normalize();
            var z = Vector3.Cross(x, y);
            return Quaternion.AngleAxis(twistDegrees, y) * Quaternion.LookRotation(z, y);
        }

        // the marker to the point, in its parent's space, inside the press's edit
        void Place(Vector3 at, Vector3 n)
        {
            point = at;
            normal = n;
            if (Moving == null) return;
            var rot = RotationAt(n, right, twist);
            var node = ctx.Model.Find(moving);
            var rt = ctx.Runtime;
            if (node == null || rt == null) return;
            var parent = rt.AttachTransformFor(node.Parent?.Id, node.Point);
            var lp = parent ? parent.InverseTransformPoint(at) : at;
            var lq = (parent ? Quaternion.Inverse(parent.rotation) : Quaternion.identity) * rot;
            var r = ctx.Ops.SetTransform(new[] { moving }, lp, lq.eulerAngles, null, UiContext.User(OpLabels.CutMoveMarker, "ui.cut/marker/" + moving + "/" + presses));
            if (!r.Ok) ctx.ShowResult(r);
        }

        public override void Ended(ToolEnd reason)
        {
            PointerCancel();
            ended?.Invoke(this);
        }
    }
}
