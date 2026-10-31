// Light probes outside the probe hull (Phase 3.3 follow-up of package A). HS2's Unity 2018
// extrapolates a map's light probes for a renderer outside their tetrahedral hull (the outer cells continue the hull
// faces); Unity 6 gives such a renderer the ambient probe instead (measured on hooh.minimap.bushitsu, whose 771 probes start
// 1.46 above the floor: a renderer at the origin got the ambient probe — 1.07 with the saved probe in Custom mode, 0.19 in
// Skybox mode after any environment update — where HS2 gives 0.0266). Characters' feet, items on the floor and the map's
// own probe-lit renderers near its edges are outside.
// While a map with light probes is loaded, every probe-lit renderer (BlendProbes, not lightmapped) of the loaded scenes is
// checked about once a second and when it moved: when its probe position (anchor, else bounds centre) lies outside the
// hull — the interpolated probe there equals the ambient probe — it gets LightProbeUsage.CustomProvided with the probe on
// the hull (bisection along the line to the nearest probe; bushitsu: 0.0248 at the origin, HS2 0.0266); inside again it
// returns to BlendProbes. Restored when the map is dropped. The SH goes through the renderer's MaterialPropertyBlock
// (read, modified, written back, so other users of the block keep their values).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace HS2Next.Scenes.Runtime
{
    public sealed class MapProbeFallback
    {
        sealed class Entry
        {
            public Vector3 Pos;
            public bool Outside;
            /// <summary>A renderer of the map scene: checked once (maps do not animate their probe-lit renderers much).</summary>
            public bool Static, Checked;
        }

        readonly Dictionary<Renderer, Entry> entries = new Dictionary<Renderer, Entry>();
        readonly List<Renderer> scratch = new List<Renderer>();
        Vector3[] probePositions;
        UnityEngine.SceneManagement.Scene mapScene;
        int lastScanFrame = -1000;
        bool active;
        static readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        static readonly SphericalHarmonicsL2[] one = new SphericalHarmonicsL2[1];

        /// <summary>Frames between full scans for new renderers (moved known renderers are rechecked every frame).</summary>
        public const int ScanFrames = 60;
        /// <summary>Renderers currently given the hull probe.</summary>
        public int Overridden { get; private set; }
        public bool Active => active;

        /// <summary>Starts for the active scene's light probes (call after SetActiveScene); false when there are none.</summary>
        public bool Begin(UnityEngine.SceneManagement.Scene mapScene = default)
        {
            End();
            this.mapScene = mapScene;
            var lp = LightmapSettings.lightProbes;
            if (lp == null || lp.count == 0) return false;
            probePositions = lp.positions;
            active = true;
            lastScanFrame = -1000;
            return true;
        }

        /// <summary>Restores every overridden renderer.</summary>
        public void End()
        {
            foreach (var kv in entries)
                if (kv.Key && kv.Value.Outside) Restore(kv.Key);
            entries.Clear();
            Overridden = 0;
            active = false;
            probePositions = null;
        }

        /// <summary>Once per frame: rechecks moved renderers, rescans the loaded scenes every <see cref="ScanFrames"/>.</summary>
        public void Update()
        {
            if (!active) return;
            if (Time.frameCount - lastScanFrame >= ScanFrames || !Application.isPlaying)
            {
                lastScanFrame = Time.frameCount;
                foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
                    if (!entries.ContainsKey(r) && Eligible(r))
                        entries[r] = new Entry { Pos = new Vector3(float.NaN, 0, 0), Static = mapScene.IsValid() && r.gameObject.scene == mapScene };
            }
            scratch.Clear();
            foreach (var kv in entries)
            {
                var r = kv.Key;
                if (!r) { scratch.Add(r); continue; }
                if (kv.Value.Static && kv.Value.Checked) continue;
                kv.Value.Checked = true;
                var p = ProbePosition(r);
                if ((p - kv.Value.Pos).sqrMagnitude < 1e-6f) continue;
                kv.Value.Pos = p;
                bool outside = Apply(r, p, probePositions);
                if (outside != kv.Value.Outside) Overridden += outside ? 1 : -1;
                kv.Value.Outside = outside;
            }
            foreach (var r in scratch) entries.Remove(r);
        }

        static bool Eligible(Renderer r) =>
            r && r.enabled && (r.lightProbeUsage == LightProbeUsage.BlendProbes || r.lightProbeUsage == LightProbeUsage.CustomProvided) && !ShaderMap.IsLightmapped(r);

        static Vector3 ProbePosition(Renderer r) => r.probeAnchor ? r.probeAnchor.position : r.bounds.center;

        /// <summary>True when the interpolated probe at <paramref name="p"/> is the ambient probe (outside the hull).</summary>
        public static bool OutsideHull(Vector3 p)
        {
            LightProbes.GetInterpolatedProbe(p, null, out var sh);
            return Same(sh, RenderSettings.ambientProbe);
        }

        static bool Same(SphericalHarmonicsL2 a, SphericalHarmonicsL2 b)
        {
            for (int c = 0; c < 3; c++)
                for (int k = 0; k < 9; k++)
                    if (Mathf.Abs(a[c, k] - b[c, k]) > 1e-6f) return false;
            return true;
        }

        /// <summary>The probe on the hull for a position outside it: bisection along the line to the nearest probe.</summary>
        public static SphericalHarmonicsL2 HullProbe(Vector3 p, Vector3[] probes)
        {
            Vector3 target = p;
            float best = float.MaxValue;
            foreach (var q in probes)
            {
                float d = (q - p).sqrMagnitude;
                if (d < best) { best = d; target = q; }
            }
            Vector3 outside = p, inside = target;
            for (int i = 0; i < 14; i++)
            {
                var mid = (outside + inside) * 0.5f;
                if (OutsideHull(mid)) outside = mid; else inside = mid;
            }
            LightProbes.GetInterpolatedProbe(inside, null, out var sh);
            return sh;
        }

        /// <summary>Gives <paramref name="r"/> the hull probe when its probe position is outside the hull, else restores
        /// BlendProbes. Returns true when it is outside. Public for tests.</summary>
        public static bool Apply(Renderer r, Vector3 p, Vector3[] probes)
        {
            if (probes == null || probes.Length == 0 || !OutsideHull(p))
            {
                if (r.lightProbeUsage == LightProbeUsage.CustomProvided) Restore(r);
                return false;
            }
            one[0] = HullProbe(p, probes);
            r.GetPropertyBlock(mpb);
            mpb.CopySHCoefficientArraysFrom(one);
            r.SetPropertyBlock(mpb);
            r.lightProbeUsage = LightProbeUsage.CustomProvided;
            return true;
        }

        /// <summary><see cref="Apply"/> at the renderer's own probe position with the active scene's probes (tests).</summary>
        public static bool Apply(Renderer r)
        {
            var lp = LightmapSettings.lightProbes;
            return r && lp != null && lp.count > 0 && Apply(r, ProbePosition(r), lp.positions);
        }

        static void Restore(Renderer r)
        {
            r.lightProbeUsage = LightProbeUsage.BlendProbes;
            r.GetPropertyBlock(mpb);
            if (!mpb.isEmpty)
            {
                // the SH arrays cannot be removed one by one: write the probe Unity would give (harmless under BlendProbes)
                LightProbes.GetInterpolatedProbe(ProbePosition(r), r, out var sh);
                one[0] = sh;
                mpb.CopySHCoefficientArraysFrom(one);
                r.SetPropertyBlock(mpb);
            }
        }
    }
}
