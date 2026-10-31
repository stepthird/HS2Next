// ItemMotionTest N8: plane colliders and FreezeAxis are inert
// with the default options (HS2's job path) and work with DynamicBoneOptions.IntendedBehaviour. Synthetic chains (an owner
// with a three-bone strand and an end offset, pushed sideways by m_Force and moved along x / z) and the census' FreezeAxis
// mod items (hooh.props.minimod_april #1, #4) built as Studio items.
namespace HS2Next.Scenes.Runtime.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using HS2Next.Content;
    using HS2Next.Motion;
    using HS2Next.Motion.DynamicBones;
    using HS2Next.Save;
    using UnityEngine;
    using Object = UnityEngine.Object;

    public static partial class ItemMotionTest
    {
        sealed class Rig : IDisposable
        {
            public GameObject Owner, PlaneObject;
            public global::DynamicBone Db;
            public DynamicBoneStage Stage;
            public DynamicBoneChain Chain;
            public float PlaneY;

            public Rig(string name, Vector3 at, bool plane, int freezeAxis, bool intended)
            {
                Owner = new GameObject(name);
                Owner.transform.position = at;
                var b0 = new GameObject("b0").transform; b0.SetParent(Owner.transform, false); b0.localRotation = Quaternion.Euler(20f, 0f, 0f);
                var b1 = new GameObject("b1").transform; b1.SetParent(b0, false); b1.localPosition = new Vector3(0f, -0.6f, 0f);
                var b2 = new GameObject("b2").transform; b2.SetParent(b1, false); b2.localPosition = new Vector3(0f, -0.6f, 0f);
                PlaneY = at.y - 1.0f;
                MonoBehaviour col = null;
                if (plane)
                {
                    PlaneObject = new GameObject(name + "_plane");
                    PlaneObject.transform.position = new Vector3(at.x, PlaneY, at.z);
                    var pc = PlaneObject.AddComponent<global::DynamicBonePlaneCollider>();
                    pc.m_Direction = 1; pc.m_Center = Vector3.zero; pc.m_Bound = 0;
                    col = pc;
                }
                Db = Owner.AddComponent<global::DynamicBone>();
                Db.m_Root = b0; Db.m_UpdateRate = 60; Db.m_UpdateMode = 0;
                Db.m_Damping = 0.1f; Db.m_Elasticity = 0.05f; Db.m_Stiffness = 0.1f; Db.m_Inert = 0.4f; Db.m_Radius = 0.05f;
                Db.m_EndLength = 0f; Db.m_EndOffset = new Vector3(0f, -0.5f, 0f);
                Db.m_Gravity = new Vector3(0f, -0.05f, 0f); Db.m_Force = new Vector3(0.02f, 0f, 0f);
                Db.m_Colliders = new List<MonoBehaviour> { col };   // null entry when there is no plane: the same record count
                Db.m_Exclusions = new List<Transform>(); Db.m_notRolls = null;
                Db.m_FreezeAxis = freezeAxis;
                Db.m_DistantDisable = 0; Db.m_DistanceToObject = 20f;
                var opt = DynamicBoneSetup.Options.Clone();
                opt.IntendedBehaviour = intended;
                Stage = new DynamicBoneStage(opt);
                Chain = Stage.Add(Db, DynamicBoneCategory.Other);
                Stage.ApplySwitches();
            }

            public void Frame(int k, float dt)
            {
                var p = Owner.transform.position;
                Owner.transform.position = new Vector3(p.x + 0.03f * Mathf.Sin(0.2f * k), p.y, p.z + 0.02f * Mathf.Cos(0.15f * k));
                Stage.Step(new MotionFrame(dt, dt, k * dt, k));
            }

            public Vector3[] Particles() => Enumerable.Range(0, Chain.ParticleCount).Select(i => Chain.Position(i)).ToArray();

            public void Dispose()
            {
                if (Owner) Object.DestroyImmediate(Owner);
                if (PlaneObject) Object.DestroyImmediate(PlaneObject);
            }
        }

        static bool SameAll(Vector3[] a, Vector3[] b) => a.Length == b.Length && a.Zip(b, (x, y) => x.x == y.x && x.y == y.y && x.z == y.z).All(v => v);

        static void N8(RuntimeServices sv)
        {
            const int frames = 120;
            const float dt = 1f / 60f;
            var at = new Vector3(3f, 10f, 2f);
            Check("default options: IntendedBehaviour off (HS2's job path, T15)", !DynamicBoneSetup.Options.IntendedBehaviour);

            // ---- plane collider, default options: bit-equal to a null entry
            using (var withPlane = new Rig("n8_plane", at, true, 0, false))
            using (var without = new Rig("n8_noplane", at, false, 0, false))
            {
                Check("synthetic chain: 4 particles (3 bones + end offset)", withPlane.Chain.ParticleCount == 4, withPlane.Chain.ToString());
                int diff = 0;
                float minY = float.MaxValue;
                for (int k = 0; k < frames; k++)
                {
                    withPlane.Frame(k, dt); without.Frame(k, dt);
                    var a = withPlane.Particles();
                    if (!SameAll(a, without.Particles())) diff++;
                    minY = Mathf.Min(minY, a.Min(v => v.y) - withPlane.PlaneY);
                }
                Check("plane collider with default options: every particle bit-equal to the run without it (120 frames)", diff == 0, $"{diff} frames differ");
                Check("…although particles go below the plane (it would act)", minY < -0.1f, "min signed distance " + F(minY));
            }
            // ---- plane collider, intended behaviour: the particles are pushed to the positive side
            using (var withPlane = new Rig("n8_plane_i", at, true, 0, true))
            using (var without = new Rig("n8_noplane_i", at, false, 0, true))
            {
                float minWith = float.MaxValue, minWithout = float.MaxValue;
                for (int k = 0; k < frames; k++)
                {
                    withPlane.Frame(k, dt); without.Frame(k, dt);
                    minWith = Mathf.Min(minWith, withPlane.Particles().Skip(1).Min(v => v.y) - withPlane.PlaneY);
                    minWithout = Mathf.Min(minWithout, without.Particles().Skip(1).Min(v => v.y) - without.PlaneY);
                }
                // the response moves a particle out of the plane before the hard length constraint, which can pull it back in part
                Check("IntendedBehaviour: the plane pushes the particles to its positive side (deepest penetration ≤ 25 % of the run without it)",
                      minWithout < -0.1f && minWith > 0.25f * minWithout, $"min signed distance with the plane {F(minWith)}, without {F(minWithout)}");
            }
            // ---- FreezeAxis X
            using (var freeze = new Rig("n8_freeze", at, false, 1, false))
            using (var none = new Rig("n8_nofreeze", at, false, 0, false))
            {
                int diff = 0;
                float dev = 0f;
                for (int k = 0; k < frames; k++)
                {
                    freeze.Frame(k, dt); none.Frame(k, dt);
                    var a = freeze.Particles();
                    if (!SameAll(a, none.Particles())) diff++;
                    dev = Mathf.Max(dev, a.Max(v => Mathf.Abs(v.x - a[0].x)));
                }
                Check("FreezeAxis X with default options: bit-equal to FreezeAxis 0 (120 frames)", diff == 0, $"{diff} frames differ");
                Check("…although the strand leaves the parent's plane (it would act)", dev > 1e-3f, "max |x_i − x_0| " + F(dev));
            }
            using (var freeze = new Rig("n8_freeze_i", at, false, 1, true))
            using (var none = new Rig("n8_nofreeze_i", at, false, 0, true))
            {
                float devF = 0f, devN = 0f;
                for (int k = 0; k < frames; k++)
                {
                    freeze.Frame(k, dt); none.Frame(k, dt);
                    var a = freeze.Particles(); var b = none.Particles();
                    devF = Mathf.Max(devF, a.Max(v => Mathf.Abs(v.x - a[0].x)));
                    devN = Mathf.Max(devN, b.Max(v => Mathf.Abs(v.x - b[0].x)));
                }
                Check("IntendedBehaviour: FreezeAxis X keeps the strand in the parent's plane (|x_i − x_0| ≤ 1e-4)", devF <= 1e-4f && devN > 1e-3f,
                      $"max with FreezeAxis {devF:E2}, without {F(devN)}");
            }

            // ---- the census' FreezeAxis items as Studio items: bit-equal to a twin with FreezeAxis 0
            foreach (var r in FreezeAxisRefs)
            {
                using (var s = new Scene(sv, 0xf4ee2eUL))
                {
                    string a = s.Add(r), b = s.Add(r);
                    var ma = s.Motion(a); var mb = s.Motion(b);
                    if (!Check(r + ": two instances with dynamic-bone chains", ma?.Dynamic != null && mb?.Dynamic != null, $"A {ma?.Dynamic}, B {mb?.Dynamic}")) continue;
                    int frozen = ma.Components.Count(c => c.m_FreezeAxis != 0);
                    Info($"{r}: {s.Item(a)?.Row}; {ma.Dynamic}; components with FreezeAxis {frozen}");
                    foreach (var c in mb.Components) c.m_FreezeAxis = 0;
                    ma.Dynamic.Rebuild(); mb.Dynamic.Rebuild();
                    ma.ApplyDynamic(); mb.ApplyDynamic();
                    for (int k = 0; k < 90; k++)
                    {
                        var p = new Vec3(0.02f * Mathf.Sin(0.1f * k), 0.01f * k, 0.03f * Mathf.Cos(0.13f * k));
                        var rot = new Vec3(0f, 0f, 2f * k);
                        s.Set(a, "transform.pos", p, false); s.Set(b, "transform.pos", p, false);
                        s.Set(a, "transform.rot", rot, false); s.Set(b, "transform.rot", rot, false);
                        s.Tick(1);
                    }
                    var wa = Walk(s.Item(a).Instance.transform);
                    var wb = Walk(s.Item(b).Instance.transform);
                    int diff = 0;
                    for (int i = 0; i < Math.Min(wa.Count, wb.Count); i++)
                        if (!Same(wa[i].p, wb[i].p) || !Same(wa[i].r, wb[i].r)) diff++;
                    Check($"{r}: FreezeAxis ({frozen} components) inert with default options: every transform bit-equal to the FreezeAxis-0 twin after 90 moving frames",
                          frozen > 0 && wa.Count == wb.Count && diff == 0 && ma.Dynamic.ActiveChains > 0, $"{wa.Count} transforms, {diff} differ, active chains {ma.Dynamic.ActiveChains}");
                }
            }
        }
    }
}
