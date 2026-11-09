// What the ragdoll draws over the view, by IToolDraw: never in a capture. The tool draws the line from a held or pinned point to its anchor (DrawGrabs); the panel, while it is shown, draws
// the wire lines of the colliders and of the room by its toggles, with or without the tool (DrawOutlines, through ctx.Tools.ViewDraw). A box is its 12 edges, a capsule two circles of 16
// segments and four lines, a sphere three circles. A part that is awake, asleep and held each has its colour.
using HS2Next.Api.Unity;
using HS2Next.Save;
using HS2Next.Scenes.Runtime;
using UnityEngine;

namespace HS2Next.UI.Panels
{
    public static class RagdollOverlay
    {
        public static readonly Color Awake = new Color(0.3f, 1f, 0.4f), Asleep = new Color(0.4f, 0.4f, 1f), Held = new Color(1f, 0.9f, 0.2f), Room = new Color(0.6f, 0.8f, 1f),
            Prop = new Color(1f, 0.55f, 0.15f), Anchor = new Color(1f, 0.3f, 0.2f);

        const int Segments = 16;

        static readonly Vector2[] circle = MakeCircle();

        static Vector2[] MakeCircle()
        {
            var c = new Vector2[Segments + 1];
            for (int i = 0; i <= Segments; i++)
            {
                float a = i * 2f * Mathf.PI / Segments;
                c[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            }
            return c;
        }

        /// <summary>The anchor lines and, by the two switches, the outlines.</summary>
        public static void Draw(IToolDraw d, RagdollWorld w, bool colliders, bool room)
        {
            DrawGrabs(d, w);
            DrawOutlines(d, w, colliders, room);
        }

        /// <summary>The line from each held or pinned point to its anchor.</summary>
        public static void DrawGrabs(IToolDraw d, RagdollWorld w)
        {
            foreach (var g in w.Grabs)
                if (g.Live) d.Line(g.Point, g.Anchor.position, Anchor);
        }

        /// <summary>The wire lines of the colliders (bodies and thrown props) and of the room, by the two switches.</summary>
        public static void DrawOutlines(IToolDraw d, RagdollWorld w, bool colliders, bool room)
        {
            if (room && w.HasRoom) DrawRoom(d, w);
            if (!colliders) return;
            foreach (var b in w.Bodies)
            {
                if (b.Buried) continue;
                foreach (var p in b.Parts)
                    Wire(d, p.Col, p.Tr, IsHeld(w, p.Rb) ? Held : p.Rb.IsSleeping() ? Asleep : Awake);
            }
            var props = w.Props;
            if (props == null) return;
            foreach (var p in props.All)
            {
                if (!p.Root) continue;
                var t = p.Root.transform;
                if (p.Kind == RagdollWorldValues.PropBox) Box(d, t, Vector3.zero, Vector3.one * p.Diameter, IsHeld(w, p.Rb) ? Held : Prop);
                else Sphere(d, t, Vector3.zero, p.Diameter * 0.5f, IsHeld(w, p.Rb) ? Held : Prop);
            }
        }

        static bool IsHeld(RagdollWorld w, Rigidbody rb)
        {
            foreach (var g in w.Grabs)
                if (g.Target == rb) return true;
            return false;
        }

        static void DrawRoom(IToolDraw d, RagdollWorld w)
        {
            var s = w.RoomSize;
            var low = new Vector3(w.RoomCenter.x - s.x * 0.5f, w.FloorY, w.RoomCenter.y - s.z * 0.5f);
            Edges(d, Matrix4x4.identity, low, low + s, Room);
        }

        /// <summary>The wire lines of one collider.</summary>
        public static void Wire(IToolDraw d, Collider col, Transform t, Color c)
        {
            switch (col)
            {
                case BoxCollider b:
                    Box(d, t, b.center, b.size, c);
                    break;
                case CapsuleCollider k:
                    Capsule(d, t, k, c);
                    break;
                case SphereCollider s:
                    Sphere(d, t, s.center, s.radius, c);
                    break;
            }
        }

        static void Box(IToolDraw d, Transform t, Vector3 center, Vector3 size, Color c) => Edges(d, t.localToWorldMatrix, center - size * 0.5f, center + size * 0.5f, c);

        // the 12 edges of the box from low to high in the space of m
        static void Edges(IToolDraw d, Matrix4x4 m, Vector3 low, Vector3 high, Color c)
        {
            Vector3 P(int i) => m.MultiplyPoint3x4(new Vector3((i & 1) != 0 ? high.x : low.x, (i & 2) != 0 ? high.y : low.y, (i & 4) != 0 ? high.z : low.z));
            for (int i = 0; i < 8; i++)
                for (int bit = 1; bit < 8; bit <<= 1)
                    if ((i & bit) == 0) d.Line(P(i), P(i | bit), c);
        }

        static void Capsule(IToolDraw d, Transform t, CapsuleCollider k, Color c)
        {
            var axis = k.direction == 0 ? Vector3.right : k.direction == 2 ? Vector3.forward : Vector3.up;
            var u = k.direction == 0 ? Vector3.up : Vector3.right;
            var v = Vector3.Cross(axis, u);
            float r = k.radius, half = Mathf.Max(0f, k.height * 0.5f - r);
            var a = t.TransformPoint(k.center + axis * half);
            var b = t.TransformPoint(k.center - axis * half);
            var wu = t.TransformDirection(u);
            var wv = t.TransformDirection(v);
            Ring(d, a, wu, wv, r, c);
            Ring(d, b, wu, wv, r, c);
            d.Line(a + wu * r, b + wu * r, c);
            d.Line(a - wu * r, b - wu * r, c);
            d.Line(a + wv * r, b + wv * r, c);
            d.Line(a - wv * r, b - wv * r, c);
        }

        static void Sphere(IToolDraw d, Transform t, Vector3 center, float r, Color c)
        {
            var p = t.TransformPoint(center);
            var x = t.right;
            var y = t.up;
            var z = t.forward;
            Ring(d, p, x, y, r, c);
            Ring(d, p, y, z, r, c);
            Ring(d, p, z, x, r, c);
        }

        static void Ring(IToolDraw d, Vector3 p, Vector3 u, Vector3 v, float r, Color c)
        {
            var last = p + (u * circle[0].x + v * circle[0].y) * r;
            for (int i = 1; i <= Segments; i++)
            {
                var next = p + (u * circle[i].x + v * circle[i].y) * r;
                d.Line(last, next, c);
                last = next;
            }
        }
    }
}
