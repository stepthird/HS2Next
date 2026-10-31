// The target display of a Studio light (decision E11): while the node's
// `showTarget` is true the view shows the directional prefab's arrow (`Model`, tinted with the light's colour as HS2's
// LightColor does) and, for point and spot lights, HS2's DrawLightLine geometry in the light's colour: three circles of
// radius `range` for a point light (local XY, XZ, YZ planes); for a spot light four lines along the cone edges to `range`
// and the disc at their ends. Everything sits on the helper layer (ViewSetup.HelperLayer 28), which the view adds to its
// mask and captures remove (ViewSetup.ApplyTo forCapture); HideForCapture() also switches the renderers off for capture
// paths that do not use ViewSetup. The guide object (gizmo) is Phase 3.5. Material: HS2Next/Unlit/Color (owned copy).
using System;
using System.Collections.Generic;
using HS2Next.Save;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace HS2Next.Scenes.Runtime
{
    public sealed class LightHelpers : IDisposable
    {
        public const int CircleSegments = 64;
        public const float LineWidth = 0.03f;
        static readonly HashSet<LightHelpers> alive = new HashSet<LightHelpers>();
        static int hiddenDepth;

        readonly LightRealization owner;
        readonly List<Renderer> arrow = new List<Renderer>();
        readonly List<LineRenderer> lines = new List<LineRenderer>();
        GameObject root;
        Material material;
        bool shown;

        /// <summary>The light's type when built.</summary>
        public LightType Type { get; }
        /// <summary>The helper renderers (arrow and lines).</summary>
        public IEnumerable<Renderer> Renderers
        {
            get
            {
                foreach (var r in arrow) if (r) yield return r;
                foreach (var l in lines) if (l) yield return l;
            }
        }
        /// <summary>The state's showTarget as last applied.</summary>
        public bool Shown => shown;
        /// <summary>True when any helper renderer draws now (shown and not inside a capture scope).</summary>
        public bool Visible
        {
            get
            {
                foreach (var r in Renderers) if (r.enabled && !r.forceRenderingOff && r.gameObject.activeInHierarchy) return true;
                return false;
            }
        }
        /// <summary>The line renderers (point: 3 circles; spot: 4 edges + the disc).</summary>
        public IReadOnlyList<LineRenderer> Lines => lines;

        internal LightHelpers(LightRealization owner)
        {
            this.owner = owner;
            var light = owner.Light;
            Type = light.type;
            if (owner.Instance)
                foreach (var r in owner.Instance.GetComponentsInChildren<Renderer>(true))
                    if (r && r.gameObject.layer == ViewSetup.HelperLayer) arrow.Add(r);
            if (Type == LightType.Point || Type == LightType.Spot)
            {
                root = new GameObject("LightHelpers") { layer = ViewSetup.HelperLayer };
                root.transform.SetParent(light.transform, false);
                var sh = Shader.Find("HS2Next/Unlit/Color");
                if (sh) material = new Material(sh) { name = "LightHelper (owned)" };
                int n = Type == LightType.Point ? 3 : 5;
                for (int i = 0; i < n; i++)
                {
                    var go = new GameObject(Type == LightType.Point ? "circle" + i : i < 4 ? "edge" + i : "disc") { layer = ViewSetup.HelperLayer };
                    go.transform.SetParent(root.transform, false);
                    var lr = go.AddComponent<LineRenderer>();
                    lr.useWorldSpace = false;
                    lr.widthMultiplier = LineWidth;
                    lr.shadowCastingMode = ShadowCastingMode.Off;
                    lr.receiveShadows = false;
                    lr.lightProbeUsage = LightProbeUsage.Off;
                    lr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    if (material) lr.sharedMaterial = material;
                    lines.Add(lr);
                }
            }
            alive.Add(this);
        }

        /// <summary>Applies colour, geometry (range, spot angle) and showTarget.</summary>
        internal void Apply(LightNodeState st)
        {
            if (st == null) return;
            var col = new Color(st.Color.R, st.Color.G, st.Color.B, 1f);
            foreach (var r in arrow)
            {
                if (!r) continue;
                foreach (var m in r.sharedMaterials)
                    if (m && m.HasProperty("_Color")) m.SetColor("_Color", col);
            }
            if (material) material.SetColor("_Color", col);
            float range = Mathf.Max(0f, st.Range);
            if (Type == LightType.Point)
            {
                for (int i = 0; i < 3 && i < lines.Count; i++) Circle(lines[i], i, range, 0f);
            }
            else if (Type == LightType.Spot && lines.Count == 5)
            {
                float half = Mathf.Clamp(st.SpotAngle, 0f, 179.9f) * 0.5f * Mathf.Deg2Rad;
                float s = Mathf.Sin(half) * range, z = Mathf.Cos(half) * range;
                var ends = new[] { new Vector3(s, 0, z), new Vector3(-s, 0, z), new Vector3(0, s, z), new Vector3(0, -s, z) };
                for (int i = 0; i < 4; i++)
                {
                    lines[i].loop = false;
                    lines[i].positionCount = 2;
                    lines[i].SetPosition(0, Vector3.zero);
                    lines[i].SetPosition(1, ends[i]);
                }
                Circle(lines[4], 0, s, z);
            }
            shown = st.ShowTarget;
            foreach (var r in Renderers)
            {
                r.enabled = shown;
                r.forceRenderingOff = hiddenDepth > 0;
            }
        }

        /// <summary>A circle of <paramref name="radius"/> in plane <paramref name="plane"/> (0 XY at z = <paramref name="z"/>, 1 XZ, 2 YZ).</summary>
        static void Circle(LineRenderer lr, int plane, float radius, float z)
        {
            lr.loop = true;
            lr.positionCount = CircleSegments;
            for (int k = 0; k < CircleSegments; k++)
            {
                float a = k * Mathf.PI * 2f / CircleSegments;
                float u = Mathf.Cos(a) * radius, v = Mathf.Sin(a) * radius;
                lr.SetPosition(k, plane == 0 ? new Vector3(u, v, z) : plane == 1 ? new Vector3(u, 0f, v) : new Vector3(0f, u, v));
            }
        }

        public void Dispose()
        {
            alive.Remove(this);
            if (root) UnityObjects.Destroy(root);
            if (material) UnityObjects.Destroy(material);
            root = null;
            material = null;
            lines.Clear();
            arrow.Clear();
        }

        /// <summary>Every helper alive (all runtimes).</summary>
        public static int Count
        {
            get
            {
                alive.RemoveWhere(h => h.owner == null || !h.owner.Light);
                return alive.Count;
            }
        }

        /// <summary>
        /// Switches every helper renderer off (forceRenderingOff) until disposed (captures, thumbnails); nests.
        /// </summary>
        public static IDisposable HideForCapture() => new Hidden();

        static void SetForced(bool off)
        {
            foreach (var h in alive)
                foreach (var r in h.Renderers) r.forceRenderingOff = off;
        }

        sealed class Hidden : IDisposable
        {
            bool done;
            public Hidden()
            {
                if (hiddenDepth++ == 0) SetForced(true);
            }

            public void Dispose()
            {
                if (done) return;
                done = true;
                if (--hiddenDepth == 0) SetForced(false);
            }
        }
    }
}
