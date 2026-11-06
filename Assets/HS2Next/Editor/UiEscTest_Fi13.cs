// UiEscTest.play, FI13: a hair bundle's and an accessory set's handle on a transform that holds a bone
// correction: the handle stands on the corrected transform, the stored value is the pose under it. A press that moves nothing changes nothing, and a
// drag leaves the transform under the pointer, the correction neither added again nor lost. A part file of UiEscTest.
using System;
using System.Collections;
using System.Linq;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI;
using HS2Next.UI.Binding;
using HS2Next.UI.Core;
using HS2Next.UI.Gizmos;
using HS2Next.UI.Panels;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

public static partial class UiEscTest
{
    static readonly Vector3 Fi13Pos = new Vector3(0.02f, 0.03f, -0.01f), Fi13Rot = new Vector3(3f, 4f, 5f);

    /// <summary>The closest point on an axis to a ray, as the gizmo takes it.</summary>
    static float AxisT(Ray r, Vector3 p0, Vector3 a)
    {
        var w = r.origin - p0;
        float b = Vector3.Dot(r.direction, a), d = Vector3.Dot(w, a), e = Vector3.Dot(w, r.direction);
        float den = 1 - b * b;
        return Mathf.Abs(den) < 1e-6f ? d : (d - b * e) / den;
    }

    static bool RayPlane(Ray r, Vector3 p0, Vector3 n, out Vector3 hit)
    {
        var pl = new Plane(n, p0);
        if (pl.Raycast(r, out float t)) { hit = r.GetPoint(t); return true; }
        hit = p0;
        return false;
    }

    /// <summary>An arm of the shown move handle the hit test finds, not along the view; its index.</summary>
    static bool MoveGrab(out Vector2 p, out Vector2 dir, out int axis)
    {
        var gz = Gizmo;
        var vb = Ui.Viewport.Element.worldBound;
        Vector2 c = gz.ToPanel(gz.PivotWorld);
        p = default; dir = default; axis = -1;
        for (int i = 0; i < 3; i++)
        {
            var a = gz.Axes * (i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward);
            foreach (float f in new[] { 0.9f, 0.75f, 0.6f, 0.45f, 0.3f })
            {
                Vector2 q = gz.ToPanel(gz.PivotWorld + a * (gz.LengthWorld * f));
                var h = gz.HitTest(q);
                if (h.Kind != HandleKind.MoveAxis || h.Axis != i || !vb.Contains(q) || (q - c).magnitude < 12) continue;
                if (Mathf.Abs(Vector3.Dot(Ui.Viewport.RayAt(q).direction, a)) > 0.95f) continue;
                p = q; dir = (q - c).normalized; axis = i;
                return true;
            }
        }
        return false;
    }

    /// <summary>A point of a ring of the shown handle the hit test finds, not edge-on; its index.</summary>
    static bool RotateGrab(out Vector2 p, out Vector2 dir, out int axis)
    {
        var gz = Gizmo;
        var vb = Ui.Viewport.Element.worldBound;
        var fwd = Ui.Viewport.ViewCamera.transform.forward;
        p = default; dir = default; axis = -1;
        for (int i = 0; i < 3; i++)
        {
            var n = i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;
            if (Mathf.Abs(Vector3.Dot(fwd, gz.Axes * n)) < 0.2f) continue;
            var u = i == 0 ? Vector3.up : Vector3.right;
            var v = Vector3.Cross(n, u);
            for (int s = 0; s < 32; s++)
            {
                float ang = s * Mathf.PI * 2 / 32;
                var w0 = gz.PivotWorld + gz.Axes * (u * Mathf.Cos(ang) + v * Mathf.Sin(ang)) * gz.LengthWorld;
                var w1 = gz.PivotWorld + gz.Axes * (u * Mathf.Cos(ang + 0.1f) + v * Mathf.Sin(ang + 0.1f)) * gz.LengthWorld;
                Vector2 q = gz.ToPanel(w0), q1 = gz.ToPanel(w1);
                var h = gz.HitTest(q);
                if (h.Kind != HandleKind.RotateRing || h.Axis != i || !vb.Contains(q) || (q1 - q).magnitude < 1) continue;
                p = q; dir = (q1 - q).normalized; axis = i;
                return true;
            }
        }
        return false;
    }

    /// <summary>A correction on the transform, through the command; false when no address of the part names it.</summary>
    static IEnumerator Correct(string part, Transform trf, bool[] ok)
    {
        ok[0] = false;
        yield return Call(new { cmd = "pose.bones", key = charaA, part, limit = 5000 });
        var r = Real(charaA);
        string address = (last?["bones"] as JArray)?.OfType<JObject>().Select(b => (string)b["bone"]).FirstOrDefault(a => AdvancedPoseResolver.Resolve(r, a) == trf);
        if (!Check("the transform's address in " + part, address != null, address ?? Short(lastText))) yield break;
        yield return OkCall(new { cmd = "pose.bones.set", key = charaA, bone = address, pos = new[] { Fi13Pos.x, Fi13Pos.y, Fi13Pos.z }, rot = new[] { Fi13Rot.x, Fi13Rot.y, Fi13Rot.z } }, "a correction on it");
        Ui.Runtime?.ApplyNow();
        yield return Frames(3);
        ok[0] = LastOk;
    }

    static IEnumerator FI13()
    {
        yield return OkCall(new { cmd = "scene.pause", paused = true }, "the global pause");
        yield return Fi13Hair();
        yield return Fi13Accessory();
        Ui.SubSelection = null;
        Ui.Tool = GizmoTool.Move;
        yield return OkCall(new { cmd = "pose.bones.clear", key = charaA, all = true }, "clear the corrections");
        yield return OkCall(new { cmd = "select", keys = new[] { charaA } }, "select the character alone");
        yield return OkCall(new { cmd = "scene.pause", paused = false }, "the pause off");
    }

    static IEnumerator Fi13Hair()
    {
        var tab = OpenEdit(charaA, "hair");
        yield return Frames(4);
        if (!Check("the Hair page", tab != null && tab.panel != null)) yield break;
        var fold = tab.Query<Foldout>("edit-section-hair.hair.back.bundles").ToList().FirstOrDefault();
        if (fold != null) fold.value = true;
        yield return Frames(3);
        var list = HairBundleList.Current;
        int index = list != null ? list.HandleIndices().DefaultIfEmpty(-1).First() : -1;
        if (!Check("a bundle with a handle", index >= 0)) yield break;
        int part = list.Part;
        var facts = list.FactsOfBundle(index);
        var trf = Real(charaA).Context.Hair[part].GetComponent<AIChara.CmpHair>().boneInfo[index].trfCorrect;
        var done = new bool[1];
        yield return Correct(PoseAddressGrammar.PartText(PosePart.Hair, part, null), trf, done);
        if (!done[0]) yield break;
        Vector3 V(Vec3 v) => new Vector3(v.X, v.Y, v.Z);
        Vector3 Lo() => V(facts.PosMin);
        Vector3 Hi() => V(facts.PosMax);
        Vector3 Move() => V(list.EffectiveOf(charaA)[index].Move);
        Vector3 Rotate() => V(list.EffectiveOf(charaA)[index].Rotate);
        Vector3 BaseOf(Vector3 rate) => new Vector3(Mathf.LerpUnclamped(Lo().x, Hi().x, rate.x), Mathf.LerpUnclamped(Lo().y, Hi().y, rate.y), Mathf.LerpUnclamped(Lo().z, Hi().z, rate.z));
        Ui.SubSelection = StudioOps.HairSub(charaA, part, index);
        yield return SetTool(GizmoTool.Move);
        yield return CentreView(10f);
        Info("the bundle's limits " + Lo().ToString("F4") + " .. " + Hi().ToString("F4") + ", rate " + Move().ToString("F4") + ", shown " + trf.localPosition.ToString("F4"));
        Check("the transform shows the pose + the correction", (trf.localPosition - (BaseOf(Move()) + Fi13Pos)).magnitude <= 1e-3f, (trf.localPosition - (BaseOf(Move()) + Fi13Pos)).ToString("F5"));
        if (!Check("a move arm can be grabbed", MoveGrab(out var p, out var d, out int axis))) yield break;

        // a press that moves nothing
        var move0 = Move();
        int u0 = Undos;
        yield return Input(Pt(p, "down"), Pt(p, "up"));
        yield return Frames(2);
        Check("hair, a press that moves nothing: the stored value stays (the correction is not added)", (BaseOf(Move()) - BaseOf(move0)).magnitude <= 1e-4f, BaseOf(move0).ToString("F5") + " -> " + BaseOf(Move()).ToString("F5"));
        while (Undos > u0) { Ui.Session.Commands.Undo(); Ui.Runtime?.ApplyNow(); }
        yield return Frames(2);

        // a drag on one arm: inside the limits, whichever way that is
        BuildGizmo();
        var gz = Gizmo;
        var a = gz.Axes * (axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward);
        var pivot = gz.PivotWorld;
        var parent = trf.parent;
        var shown0 = trf.localPosition;
        var base0 = BaseOf(Move());
        Vector2 q = p;
        Vector3 want = default, localDelta = default;
        bool inside = false;
        foreach (float px in new[] { 10f, -10f, 5f, -5f, 2f, -2f })
        {
            q = p + d * px;
            float dt = AxisT(Ui.Viewport.RayAt(q), pivot, a) - AxisT(Ui.Viewport.RayAt(p), pivot, a);
            localDelta = parent.InverseTransformVector(a * dt);
            want = base0;
            want[axis] = shown0[axis] + localDelta[axis] - Fi13Pos[axis];
            if (want[axis] >= Mathf.Min(Lo()[axis], Hi()[axis]) && want[axis] <= Mathf.Max(Lo()[axis], Hi()[axis])) { inside = true; break; }
        }
        if (Check("hair: a drag inside the limits", inside, want.ToString("F4")))
        {
            u0 = Undos;
            yield return Input(Pt(p, "down"), Pt(q, "move"), Pt(q, "up"));
            yield return Frames(2);
            Check("hair, a drag: one entry", Undos == u0 + 1, (Undos - u0).ToString());
            Check("hair, a drag: stored = the math without the correction (1e-4)", (BaseOf(Move()) - want).magnitude <= 1e-4f, BaseOf(Move()).ToString("F5") + " / " + want.ToString("F5"));
            Check("hair, a drag: the transform stays under the pointer (1e-4)", Mathf.Abs(trf.localPosition[axis] - (shown0[axis] + localDelta[axis])) <= 1e-4f, trf.localPosition[axis] + " / " + (shown0[axis] + localDelta[axis]));
            while (Undos > u0) { Ui.Session.Commands.Undo(); Ui.Runtime?.ApplyNow(); }
            yield return Frames(2);
        }

        // a ring: the stored rotation is the start + the angle; the correction's rotation plays no part
        yield return SetTool(GizmoTool.Rotate);
        BuildGizmo();
        if (Check("hair: a ring can be grabbed, not edge-on", RotateGrab(out var rp, out var rd, out int ring)))
        {
            var rlo = V(facts.RotMin);
            var rhi = V(facts.RotMax);
            var n = gz.Axes * (ring == 0 ? Vector3.right : ring == 1 ? Vector3.up : Vector3.forward);
            var rate0 = Rotate();
            float e0 = Mathf.LerpUnclamped(rlo[ring], rhi[ring], rate0[ring]);
            Vector2 rq = rp + rd * 8;
            RayPlane(Ui.Viewport.RayAt(rp), gz.PivotWorld, n, out var h0);
            RayPlane(Ui.Viewport.RayAt(rq), gz.PivotWorld, n, out var h1);
            float ang = Vector3.SignedAngle(h0 - gz.PivotWorld, h1 - gz.PivotWorld, n);
            float wantE = Mathf.Clamp(e0 + ang, Mathf.Min(rlo[ring], rhi[ring]), Mathf.Max(rlo[ring], rhi[ring]));
            u0 = Undos;
            yield return Input(Pt(rp, "down"), Pt(rq, "move"), Pt(rq, "up"));
            yield return Frames(2);
            float e1 = Mathf.LerpUnclamped(rlo[ring], rhi[ring], Rotate()[ring]);
            Check("hair, a ring: stored = start + angle (0.01 deg)", Mathf.Abs(Mathf.DeltaAngle(e1, wantE)) <= 0.01f, e0 + " + " + ang + " -> " + e1 + " / " + wantE);
            while (Undos > u0) { Ui.Session.Commands.Undo(); Ui.Runtime?.ApplyNow(); }
            yield return Frames(2);
        }
        Ui.SubSelection = null;
        yield return OkCall(new { cmd = "pose.bones.clear", key = charaA, all = true }, "clear the correction");
        yield return Frames(2);
    }

    static IEnumerator Fi13Accessory()
    {
        var tab = OpenEdit(charaA, "accessories");
        yield return Frames(4);
        if (!Check("the Accessories page", tab != null && tab.panel != null)) yield break;
        var r = Real(charaA);
        string acc = null;
        Transform trf = null;
        foreach (var slot in r.Context.Accessories)
        {
            if (slot == null || !slot.Object) continue;
            var cmp = slot.Object.GetComponent<AIChara.CmpAccessory>();
            string aid = r.AccessoryIdOf(slot);
            if (cmp != null && cmp.trfMove01 && aid != null) { acc = aid; trf = cmp.trfMove01; break; }
        }
        if (!Check("an accessory with a transform set", acc != null)) yield break;
        tab.SelectAccessory(acc);
        yield return Frames(3);
        var fold = tab.Query<Foldout>("edit-section-" + InspectorSchemas.AccessorySectionId(tab.AccessoryShownSlot, "adjust")).ToList().FirstOrDefault();
        if (fold != null) fold.value = true;
        yield return Frames(3);
        var done = new bool[1];
        yield return Correct(PoseAddressGrammar.PartText(PosePart.Accessory, -1, acc), trf, done);
        if (!done[0]) yield break;
        string Path(string member) => InspectorSchemas.AccessoryEditPath(acc, "moves[0]." + member);
        Vector3 Stored(string member) { var v = (Vec3)FieldAccessor.Get(Ui.Model, charaA, Path(member)); return new Vector3(v.X, v.Y, v.Z); }
        Ui.SubSelection = StudioOps.AccessorySub(charaA, acc, 0);
        yield return SetTool(GizmoTool.Move);
        yield return CentreView(6f);
        Info("the set: stored " + Stored("position").ToString("F4") + ", shown " + trf.localPosition.ToString("F4"));
        if (!Check("a move arm can be grabbed", MoveGrab(out var p, out var d, out int axis))) yield break;

        // a press that moves nothing: within the 0.1 grid of the stored tenths
        var pos0 = Stored("position");
        int u0 = Undos;
        yield return Input(Pt(p, "down"), Pt(p, "up"));
        yield return Frames(2);
        Check("accessory, a press that moves nothing: the stored value stays (the correction is not added)", (Stored("position") - pos0).magnitude <= 0.05f + 1e-3f, pos0.ToString("F3") + " -> " + Stored("position").ToString("F3"));
        while (Undos > u0) { Ui.Session.Commands.Undo(); Ui.Runtime?.ApplyNow(); }
        yield return Frames(2);

        BuildGizmo();
        var gz = Gizmo;
        var a = gz.Axes * (axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward);
        var pivot = gz.PivotWorld;
        var parent = trf.parent;
        var shown0 = trf.localPosition;
        Vector2 q = p + d * 12;
        float dt = AxisT(Ui.Viewport.RayAt(q), pivot, a) - AxisT(Ui.Viewport.RayAt(p), pivot, a);
        var localDelta = parent.InverseTransformVector(a * dt);
        float want = (shown0[axis] + localDelta[axis] - Fi13Pos[axis]) * 10f;
        u0 = Undos;
        yield return Input(Pt(p, "down"), Pt(q, "move"), Pt(q, "up"));
        yield return Frames(2);
        var pos1 = Stored("position");
        Check("accessory, a drag: one entry", Undos == u0 + 1, (Undos - u0).ToString());
        Check("accessory, a drag: stored tenths = the math without the correction (the 0.1 grid)", Mathf.Abs(pos1[axis] - want) <= 0.05f + 1e-3f, pos1[axis] + " / " + want);
        Check("accessory, a drag: the other components stay", Mathf.Abs(pos1[(axis + 1) % 3] - pos0[(axis + 1) % 3]) <= 1e-4f && Mathf.Abs(pos1[(axis + 2) % 3] - pos0[(axis + 2) % 3]) <= 1e-4f, pos1.ToString("F3"));
        Check("accessory, a drag: the transform stays under the pointer (the grid)", Mathf.Abs(trf.localPosition[axis] - (shown0[axis] + localDelta[axis])) <= 0.005f + 1e-3f, trf.localPosition[axis] + " / " + (shown0[axis] + localDelta[axis]));
        while (Undos > u0) { Ui.Session.Commands.Undo(); Ui.Runtime?.ApplyNow(); }
        yield return Frames(2);

        yield return SetTool(GizmoTool.Rotate);
        BuildGizmo();
        if (Check("accessory: a ring can be grabbed, not edge-on", RotateGrab(out var rp, out var rd, out int ring)))
        {
            var n = gz.Axes * (ring == 0 ? Vector3.right : ring == 1 ? Vector3.up : Vector3.forward);
            var rot0 = Stored("rotation");
            Vector2 rq = rp + rd * 12;
            RayPlane(Ui.Viewport.RayAt(rp), gz.PivotWorld, n, out var h0);
            RayPlane(Ui.Viewport.RayAt(rq), gz.PivotWorld, n, out var h1);
            float ang = Vector3.SignedAngle(h0 - gz.PivotWorld, h1 - gz.PivotWorld, n);
            u0 = Undos;
            yield return Input(Pt(rp, "down"), Pt(rq, "move"), Pt(rq, "up"));
            yield return Frames(2);
            // the stored Euler is truncated to a whole degree
            float wantE = rot0[ring] + ang;
            Check("accessory, a ring: stored = start + angle (a whole degree)", Mathf.Abs(Mathf.DeltaAngle(Stored("rotation")[ring], wantE)) <= 1.01f, rot0[ring] + " + " + ang + " -> " + Stored("rotation")[ring]);
            while (Undos > u0) { Ui.Session.Commands.Undo(); Ui.Runtime?.ApplyNow(); }
            yield return Frames(2);
        }
        Ui.SubSelection = null;
        yield return Frames(2);
    }
}
