// Viewport picking (U21): CPU, on click only. Candidates = the realized renderers of the
// scene's nodes whose world AABB the pick ray hits (hidden renderers skipped; maps are not nodes, so never picked);
// the owner of a renderer is the deepest node whose holder contains it (an item attached to a character resolves to
// the item; clothes and accessories to the character). The nearest 8 by AABB entry distance get a triangle raycast in
// world space on MeshFilter.sharedMesh (readable meshes) or a SkinnedMeshRenderer.BakeMesh copy (cached for the click);
// a candidate whose mesh cannot be read falls back to its AABB distance. The nearest hit wins. Selection rules of
// (viewport column): click selects the hit node (empty space clears), Ctrl toggles, Shift adds; the hs2studio
// click modifiers (P / P+Ctrl / O / O+Ctrl / X+Ctrl) go through StudioOps.Move / CopyTransform.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Viewport
{
    public struct PickHit
    {
        public string NodeId;
        public float Distance;
        public Vector3 Point;
        public string Renderer;
        /// <summary>Time of the pick (ms).</summary>
        public double Ms;
        public int Candidates, Tested;
        public bool Precise;
    }

    public sealed class ViewportPicker
    {
        public const int MaxPrecise = 8;
        readonly UiContext ctx;
        /// <summary>The last pick (ui.state, timing lines).</summary>
        public PickHit Last { get; private set; }
        readonly List<double> times = new List<double>();
        public IReadOnlyList<double> Times => times;

        public ViewportPicker(UiContext ctx) { this.ctx = ctx; }

        public PickHit Pick(ViewportHost vp, Vector2 panelPoint) => PickRay(vp.RayAt(panelPoint));

        public PickHit PickRay(Ray ray)
        {
            var sw = Stopwatch.StartNew();
            var hit = new PickHit { Distance = float.PositiveInfinity };
            var rt = ctx.Runtime;
            if (rt == null) return hit;
            var holders = new Dictionary<Transform, string>();
            foreach (var r in rt.Realizations)
                if (r.Holder && rt.IsInModel(r)) holders[r.Holder] = r.NodeId;
            var cands = new List<(float d, Renderer r, string id)>();
            foreach (var kv in holders)
            {
                foreach (var r in kv.Key.GetComponentsInChildren<Renderer>(false))
                {
                    if (!r.enabled || !r.gameObject.activeInHierarchy || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                    if (!r.bounds.IntersectRay(ray, out float d)) continue;
                    string owner = OwnerOf(r.transform, holders);
                    if (owner != kv.Value) continue;   // counted once, for its deepest holder
                    cands.Add((Mathf.Max(0, d), r, owner));
                }
            }
            hit.Candidates = cands.Count;
            cands.Sort((a, b) => a.d.CompareTo(b.d));
            Mesh baked = null;
            // precise (triangle) hits win; candidates whose meshes cannot be read fall back to their AABB: the nearest
            // entry, ties (entries within 0.5 units) to the smaller box (the more specific object)
            float fallbackD = float.PositiveInfinity, fallbackVol = float.PositiveInfinity;
            string fallbackId = null, fallbackName = null;
            for (int i = 0; i < cands.Count; i++)
            {
                var (d, r, id) = cands[i];
                if (d >= hit.Distance) break;
                if (i < MaxPrecise)
                {
                    hit.Tested++;
                    if (Raycast(r, ray, ref baked, out float td, out bool readable))
                    {
                        if (td < hit.Distance) { hit.Distance = td; hit.NodeId = id; hit.Renderer = r.name; hit.Precise = true; }
                        continue;
                    }
                    if (readable) continue;   // a mesh that was read and missed
                }
                if (r.bounds.Contains(ray.origin)) continue;   // the camera inside a box says nothing
                var sz = r.bounds.size;
                float vol = Mathf.Max(1e-6f, sz.x * sz.y * sz.z);
                if (d < fallbackD - 0.5f || (d < fallbackD + 0.5f && vol < fallbackVol)) { fallbackD = d; fallbackVol = vol; fallbackId = id; fallbackName = r.name; }
            }
            if (hit.NodeId == null && fallbackId != null) { hit.Distance = fallbackD; hit.NodeId = fallbackId; hit.Renderer = fallbackName; hit.Precise = false; }
            if (baked) UnityEngine.Object.Destroy(baked);
            if (hit.NodeId != null) hit.Point = ray.GetPoint(hit.Distance);
            hit.Ms = sw.Elapsed.TotalMilliseconds;
            times.Add(hit.Ms);
            if (times.Count > 200) times.RemoveAt(0);
            Last = hit;
            return hit;
        }

        static string OwnerOf(Transform t, Dictionary<Transform, string> holders)
        {
            for (var x = t; x != null; x = x.parent)
                if (holders.TryGetValue(x, out var id)) return id;
            return null;
        }

        /// <summary>Triangle raycast in world space; readable = the mesh could be read (a miss is then a real miss).</summary>
        static bool Raycast(Renderer r, Ray ray, ref Mesh baked, out float dist, out bool readable)
        {
            dist = float.PositiveInfinity;
            readable = false;
            Mesh mesh = null;
            Matrix4x4 m;
            if (r is SkinnedMeshRenderer smr)
            {
                if (!smr.sharedMesh) return false;
                if (!baked) baked = new Mesh { name = "pick bake" };
                try { smr.BakeMesh(baked, true); }
                catch (Exception) { return false; }
                mesh = baked;
                m = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
            }
            else
            {
                var mf = r.GetComponent<MeshFilter>();
                if (!mf || !mf.sharedMesh || !mf.sharedMesh.isReadable) return false;
                mesh = mf.sharedMesh;
                m = r.transform.localToWorldMatrix;
            }
            Vector3[] v;
            int[] tris;
            try { v = mesh.vertices; tris = mesh.triangles; }
            catch (Exception) { return false; }
            if (v == null || v.Length == 0 || tris == null || tris.Length == 0) return false;
            readable = true;
            // ray into mesh space (no inverse-transpose needed: distances are measured back in world space)
            var inv = m.inverse;
            var o = inv.MultiplyPoint3x4(ray.origin);
            var dir = inv.MultiplyVector(ray.direction);
            float best = float.PositiveInfinity;
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                if (Tri(o, dir, v[tris[i]], v[tris[i + 1]], v[tris[i + 2]], out float t) && t < best) best = t;
            }
            if (float.IsPositiveInfinity(best)) return false;
            var p = m.MultiplyPoint3x4(o + dir * best);
            dist = Vector3.Distance(ray.origin, p);
            return true;
        }

        /// <summary>Möller–Trumbore, both faces.</summary>
        static bool Tri(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c, out float t)
        {
            t = 0;
            var e1 = b - a; var e2 = c - a;
            var p = Vector3.Cross(d, e2);
            float det = Vector3.Dot(e1, p);
            if (Mathf.Abs(det) < 1e-12f) return false;
            float inv = 1f / det;
            var s = o - a;
            float u = Vector3.Dot(s, p) * inv;
            if (u < 0 || u > 1) return false;
            var q = Vector3.Cross(s, e1);
            float w = Vector3.Dot(d, q) * inv;
            if (w < 0 || u + w > 1) return false;
            t = Vector3.Dot(e2, q) * inv;
            return t > 0;
        }

        // ---------------- click rules

        public void Click(ViewportHost vp, Vector2 panelPoint, EventModifiers mods)
        {
            var hit = Pick(vp, panelPoint);
            Apply(hit.NodeId, Input.ShortcutRouter.Mods(mods));
        }

        /// <summary>The selection effect of a click on <paramref name="id"/> (null = empty space). A click that leaves a
        /// node selected shows the inspector tab when the workspace and the inspector share a region.</summary>
        public void Apply(string id, KeyMods mods)
        {
            ApplySelection(id, mods);
            if (id != null && ctx.Selection.Count > 0) ctx.Shell?.AutoShowInspector();
        }

        void ApplySelection(string id, KeyMods mods)
        {
            var sel = ctx.Selection;
            var keys = ctx.Keys;
            var router = ctx.Host?.Router;
            if (keys.Resolver != null && router != null && id != null && sel.Primary != null)
            {
                var down = HeldKeys(router);
                string primary = sel.Primary;
                if (keys.Resolver.IsModifierHeld(ActionIds.ClickCopyTransform, down, mods))
                {
                    ctx.ShowResult(ctx.Ops.CopyTransform(primary, new[] { id }, UiContext.User()));
                    ctx.Ops.Select(new[] { id });
                    return;
                }
                if (keys.Resolver.IsModifierHeld(ActionIds.ClickParentAll, down, mods))
                {
                    using (var e = ctx.BeginEdit(OpLabels.Parent))
                    {
                        foreach (var other in new List<string>(sel.Ids)) if (other != id) ctx.Ops.Move(other, id, null, -1, true, UiContext.User());
                        ctx.ShowResult(e.Commit());
                    }
                    ctx.Ops.Select(new[] { id });
                    return;
                }
                if (keys.Resolver.IsModifierHeld(ActionIds.ClickParent, down, mods))
                {
                    if (primary != id) ctx.ShowResult(ctx.Ops.Move(primary, id, null, -1, true, UiContext.User()));
                    ctx.Ops.Select(new[] { id });
                    return;
                }
            }
            if (keys.Resolver != null && router != null && id != null)
            {
                var down = HeldKeys(router);
                if (keys.Resolver.IsModifierHeld(ActionIds.ClickDetachAll, down, mods))
                {
                    using (var e = ctx.BeginEdit(OpLabels.Parent))
                    {
                        foreach (var s in new List<string>(sel.Ids)) ctx.Ops.Move(s, null, null, -1, true, UiContext.User());
                        ctx.ShowResult(e.Commit());
                    }
                    return;
                }
                if (keys.Resolver.IsModifierHeld(ActionIds.ClickDetach, down, mods))
                {
                    ctx.ShowResult(ctx.Ops.Move(id, null, null, -1, true, UiContext.User()));
                    ctx.Ops.Select(new[] { id });
                    return;
                }
            }
            if (id == null)
            {
                if (mods == KeyMods.None) ctx.Ops.Select(Array.Empty<string>());
                return;
            }
            if ((mods & KeyMods.Ctrl) != 0)
            {
                var l = new List<string>(sel.Ids);
                if (l.Contains(id)) l.Remove(id); else l.Add(id);
                ctx.Ops.Select(l);
            }
            else if ((mods & KeyMods.Shift) != 0) ctx.Ops.Select(new[] { id }, add: true);
            else ctx.Ops.Select(new[] { id });
        }

        static List<string> HeldKeys(Input.ShortcutRouter router)
        {
            var l = new List<string>();
            foreach (var k in new[] { KeyCode.P, KeyCode.O, KeyCode.X, KeyCode.V, KeyCode.B })
                if (router.Source.GetKey(k)) l.Add(k.ToString());
            return l;
        }
    }
}
