// A bullet through the ragdoll world and what it does to a body.
//   the ray     every collider up to 3,000 units, nearest first. A wall or the ground stops it, seen from inside the room; an outer face lets it pass. A part or a prop takes a share of what
//               is left, once per rigid body; a body takes the hit reaction, blood when the option is on. Under 8 % of the momentum the bullet stops 1.5 units past the last part
//   reaction    the part hit and the rest of its body lose strength, regained by the second; what hits add up to, the stagger, takes a body down; with kill the first body stays limp
//   a stain     asked for by the caller: the ray of the first body hit starts one, at most one a character per half a second of the world and four a character; the rest are counted
//   no world    a shot still draws its tracer and its flash: nothing in the room can be hit, and a shot makes no world
//   numbers     WetMath.Rng on the world's seed and its count of shots, so a fresh world repeats a run; the effects have a stream of their own, so showing blood changes no body
// Effects stand beside the world, not in it: they outlive a body and stop with the scene clock. This part joins the runtime at the first shot, so a scene that never fired has none of it.
using System;
using System.Collections.Generic;
using HS2Next.Save;
using HS2Next.Scenes.Wet;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>What a ray met, as the bullet's arithmetic reads it.</summary>
    public enum ShotHitKind
    {
        /// <summary>A wall, the ground, an obstacle.</summary>
        Static,
        Part,
        Prop,
        /// <summary>Something a bullet does not touch (a pusher).</summary>
        None,
    }

    /// <summary>One thing on a bullet's way, nearest first.</summary>
    public struct ShotHit
    {
        public ShotHitKind Kind;
        public float Distance, Mass;
        /// <summary>The first hit of this rigid body: a body is taken once.</summary>
        public int First;
        /// <summary>A surface seen from outside the room.</summary>
        public bool Outer;
    }

    /// <summary>What a part or a prop took from a bullet: its share, and the impulse after the cap.</summary>
    public struct ShotBlow
    {
        /// <summary>The index of the hit.</summary>
        public int Hit;
        public float J, Applied;
        /// <summary>The body and its part; null on a prop.</summary>
        public RagdollBodyPart Part;
    }

    /// <summary>What the last shot did. The object is reused by the next shot.</summary>
    public sealed class ShotResult
    {
        /// <summary>The bodies hit, in order.</summary>
        public readonly List<RagdollBody> Bodies = new List<RagdollBody>();
        /// <summary>Every part and prop hit, in order.</summary>
        public readonly List<ShotBlow> Blows = new List<ShotBlow>();
        /// <summary>The bodies hit.</summary>
        public int Hits => Bodies.Count;
        public Vector3 Muzzle, End;
        /// <summary>True when something stopped the bullet: a wall, or it ran out.</summary>
        public bool Stopped;
        /// <summary>True when the bullet ended on a wall or the ground.</summary>
        public bool OnWall;
        /// <summary>A stain was started, or asked for and skipped by a limit or a ray that met no skin.</summary>
        public bool Stained, StainSkipped;
    }

    public sealed class RagdollShots : ISceneRuntimePart, ISceneTickPart, IDisposable
    {
        public const float Reach = 3000f, FreeTracer = 600f, FlashAhead = .3f, StopShare = .08f, StopPast = 1.5f, PropTransfer = .5f;
        public const float HeadStagger = 1.5f, NeighbourShare = .5f, RestShare = .15f;
        public const float StainEvery = .5f;
        public const int MaxHits = 64, StainsPerCharacter = 4;
        const string StainPrefix = "shot";

        SceneRuntime rt;
        RagdollPart ragdoll;
        readonly RaycastHit[] rays = new RaycastHit[MaxHits];
        readonly ShotHit[] hits = new ShotHit[MaxHits];
        readonly ShotBlow[] blows = new ShotBlow[MaxHits];
        readonly ShotResult result = new ShotResult();
        readonly Dictionary<string, float> stainAt = new Dictionary<string, float>(StringComparer.Ordinal);
        readonly Func<float, float, float> ground;
        RagdollFx fx;
        long loose;
        float clock;
        bool weak;

        RagdollShots() { ground = HeightAt; }

        /// <summary>The shots of a runtime, made at the first call.</summary>
        public static RagdollShots For(SceneRuntime rt)
        {
            var s = rt.GetPart<RagdollShots>();
            if (s != null) return s;
            s = new RagdollShots();
            rt.AddPart(s, s.Dispose);
            return s;
        }

        /// <summary>The shots of a runtime, or null before the first shot.</summary>
        public static RagdollShots Of(SceneRuntime rt) => rt.GetPart<RagdollShots>();

        RagdollWorld World => (ragdoll ?? (ragdoll = rt.GetPart<RagdollPart>()))?.World;

        /// <summary>The effects; null before the first shot and after a clear.</summary>
        public RagdollFx Fx => fx;
        /// <summary>Shots' stains left out by a limit or by a ray that met no skin.</summary>
        public int StainsSkipped { get; private set; }

        // ------------------------------------------------------------ the arithmetic

        /// <summary>
        /// What a bullet of <paramref name="momentum"/> does to <paramref name="count"/> hits, nearest first: the blows in <paramref name="blows"/>, how many returned;
        /// <paramref name="end"/> where it ended, infinite when nothing stopped it; <paramref name="wall"/> the hit it ended on, or -1. A function of its arguments.
        /// </summary>
        public static int Walk(ShotHit[] hits, int count, float momentum, float transfer, float maxPartDv, ShotBlow[] blows, out float end, out int wall)
        {
            float left = momentum;
            int n = 0;
            end = float.PositiveInfinity;
            wall = -1;
            for (int i = 0; i < count; i++)
            {
                switch (hits[i].Kind)
                {
                    case ShotHitKind.None:
                        continue;
                    case ShotHitKind.Static:
                        if (hits[i].Outer) continue;
                        end = hits[i].Distance;
                        wall = i;
                        return n;
                }
                if (hits[i].First != i) continue;
                float j = left * (hits[i].Kind == ShotHitKind.Prop ? PropTransfer : transfer);
                blows[n++] = new ShotBlow { Hit = i, J = j, Applied = Mathf.Min(j, maxPartDv * hits[i].Mass) };
                left -= j;
                if (left < StopShare * momentum)
                {
                    end = hits[i].Distance + StopPast;
                    return n;
                }
            }
            return n;
        }

        /// <summary>A hit of share <paramref name="j"/> on <paramref name="hit"/>: the part loses the most, its parent and children half of that, the rest of the body a little; at most 1.</summary>
        public static void Weaken(RagdollBody body, RagdollBodyPart hit, float j, float hitWeaken)
        {
            float amount = j / Mathf.Max(1f, hitWeaken);
            var parts = body.Parts;
            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                float share = p == hit ? 1f : p.Parent == hit || hit.Parent == p ? NeighbourShare : RestShare;
                p.Weakness = Mathf.Min(1f, p.Weakness + amount * share);
            }
        }

        /// <summary><paramref name="dt"/> seconds of the world: weakness regained, stagger decayed. True while a part is still weak or the body staggers.</summary>
        public static bool Regain(RagdollBody body, float dt, float regain)
        {
            bool left = false;
            var parts = body.Parts;
            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                p.Weakness = Mathf.Max(0f, p.Weakness - regain * dt);
                if (p.Weakness > 0f) left = true;
            }
            body.Stagger *= Mathf.Exp(-dt);
            if (body.Stagger < 1e-3f) body.Stagger = 0f;
            return left || body.Stagger > 0f;
        }

        // ------------------------------------------------------------ a shot

        /// <summary>A direction turned by a random offset in a disc of <paramref name="degrees"/>, by the next shot's numbers.</summary>
        public Vector3 Spread(Vector3 dir, float degrees)
        {
            if (degrees <= 0f) return dir;
            var w = World;
            int seed = w?.Seed ?? 0;
            long shot = (w?.ShotCounter ?? loose) * 2;
            var z = dir.normalized;
            var x = Vector3.Cross(z, Mathf.Abs(z.y) < .9f ? Vector3.up : Vector3.right).normalized;
            var y = Vector3.Cross(z, x);
            float r = Mathf.Sqrt((float)WetMath.Rng(seed, shot)) * Mathf.Tan(degrees * Mathf.Deg2Rad), a = (float)WetMath.Rng(seed, shot + 1) * 2f * Mathf.PI;
            return (z + r * (Mathf.Cos(a) * x + Mathf.Sin(a) * y)).normalized;
        }

        /// <summary>
        /// One bullet from <paramref name="origin"/> along <paramref name="dir"/>, its tracer from <paramref name="muzzle"/>. With <paramref name="kill"/> the first body hit goes limp;
        /// with <paramref name="stain"/> the first body hit takes a stain. The result is reused by the next shot.
        /// </summary>
        public ShotResult Fire(Vector3 origin, Vector3 dir, Vector3 muzzle, float momentum, bool kill, bool stain = false)
        {
            var w = World;
            dir = dir.normalized;
            if (fx == null) fx = new RagdollFx(rt.Root.transform);
            fx.BeginShot(w?.Seed ?? 0, w != null ? w.ShotCounter++ : loose++);
            var r = result;
            r.Bodies.Clear();
            r.Blows.Clear();
            r.Muzzle = muzzle;
            r.Stopped = r.OnWall = r.Stained = r.StainSkipped = false;
            float end = float.PositiveInfinity;
            if (w != null)
            {
                int n = Gather(w, origin, dir);
                int b = Walk(hits, n, momentum, w.Values.Transfer, w.Values.MaxPartDv, blows, out end, out int wall);
                for (int i = 0; i < b; i++) Strike(w, ref blows[i], dir, kill, r);
                if (wall >= 0)
                {
                    fx.DustAt(rays[wall].point, rays[wall].normal, dir);
                    r.OnWall = true;
                }
                if (stain && r.Hits > 0) Stain(r.Bodies[0].NodeId, new Ray(origin, dir), r);
            }
            r.Stopped = !float.IsInfinity(end);
            r.End = r.Stopped ? origin + dir * end : muzzle + dir * FreeTracer;
            fx.TracerTo(muzzle, r.End);
            fx.FlashAt(muzzle + FlashAhead * dir, dir);
            return r;
        }

        // the ray's hits in order of distance, each as the arithmetic reads it
        int Gather(RagdollWorld w, Vector3 origin, Vector3 dir)
        {
            int n = w.Physics.Raycast(origin, dir, rays, Reach, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 1; i < n; i++)
            {
                var h = rays[i];
                int k = i - 1;
                for (; k >= 0 && rays[k].distance > h.distance; k--) rays[k + 1] = rays[k];
                rays[k + 1] = h;
            }
            var centre = new Vector3(w.RoomCenter.x, w.FloorY + w.RoomSize.y * .5f, w.RoomCenter.y);
            for (int i = 0; i < n; i++)
            {
                var rb = rays[i].rigidbody;
                ref var h = ref hits[i];
                h = new ShotHit { Distance = rays[i].distance, First = i };
                if (!rb)
                {
                    h.Kind = ShotHitKind.Static;
                    h.Outer = Vector3.Dot(rays[i].normal, rays[i].point - centre) > 0f;
                    continue;
                }
                h.Kind = w.PartOf(rb) != null ? ShotHitKind.Part : w.IsProp(rb) ? ShotHitKind.Prop : ShotHitKind.None;
                h.Mass = rb.mass;
                for (int k = 0; k < i; k++) if (rays[k].rigidbody == rb) { h.First = k; break; }
            }
            return n;
        }

        void Strike(RagdollWorld w, ref ShotBlow blow, Vector3 dir, bool kill, ShotResult r)
        {
            ref var ray = ref rays[blow.Hit];
            var rb = ray.rigidbody;
            rb.AddForceAtPosition(dir * blow.Applied, ray.point, ForceMode.Impulse);
            var p = w.PartOf(rb);
            blow.Part = p;
            r.Blows.Add(blow);
            if (p == null) return;
            var body = p.Body;
            bool head = p.Key == RagdollParts.Head, first = r.Bodies.Count == 0;
            if (!r.Bodies.Contains(body)) r.Bodies.Add(body);
            Weaken(body, p, blow.J, w.Values.HitWeaken);
            weak = true;
            body.Stagger += blow.J * (head ? HeadStagger : 1f);
            if (kill && first) body.GoLimp();
            else if (body.Stagger >= w.Values.StaggerAt && body.State == RagdollState.Up)
            {
                body.Stagger = 0f;
                body.Knock();
            }
            body.WakeAll();
            fx.Blood(ray.point, dir, blow.J, head);
        }

        void Stain(string nodeId, Ray ray, ShotResult r)
        {
            if (!RagdollFx.ShowBlood) return;
            var wet = rt.GetPart<WetPart>();
            if (wet == null) return;
            r.StainSkipped = true;
            StainsSkipped++;
            if (stainAt.TryGetValue(nodeId, out float at) && clock - at < StainEvery) return;
            var st = rt.Model.Find(nodeId)?.State as CharaNodeState;
            if (st == null || st.Wet.Stains.Count >= WetLimits.MaxStains) return;
            int mine = 0, next = 1;
            foreach (var s in st.Wet.Stains)
            {
                if (s.Name == null || !s.Name.StartsWith(StainPrefix, StringComparison.Ordinal) || !int.TryParse(s.Name.Substring(StainPrefix.Length), out int number)) continue;
                mine++;
                next = Math.Max(next, number + 1);
            }
            if (mine >= StainsPerCharacter) return;
            var done = wet.AddRay(nodeId, ray, WetPreset.Blood, StainPrefix + next, ExecuteOptions.NotRecorded, (b, s) => WetEdits.Start(s));
            if (done.Status != CommandStatus.Ok) return;
            stainAt[nodeId] = clock;
            r.StainSkipped = false;
            StainsSkipped--;
            r.Stained = true;
        }

        // ------------------------------------------------------------ the clock

        /// <summary>The ground of a world at a place: what a droplet lands on.</summary>
        public static float GroundAt(RagdollWorld w, float x, float z) => w.Floor.HeightAt(x, z);

        float HeightAt(float x, float z) => GroundAt(World, x, z);

        // ------------------------------------------------------------ the part

        public void Attach(SceneRuntime r) => rt = r;
        public void OnChanged(SceneChangeBatch b) { }
        public void Apply() { }
        public void OnLoadedRealized() { }

        /// <summary>The effects of the moment into their mesh.</summary>
        public void LateApply() => fx?.Draw();

        /// <summary><paramref name="dt"/> scene seconds: strength comes back, the effects move.</summary>
        public void Tick(float dt)
        {
            var w = World;
            float wdt = dt * (w?.Values.TimeScale ?? 1f);
            clock += wdt;
            if (weak && w != null)
            {
                bool left = false;
                var bodies = w.Bodies;
                for (int i = 0; i < bodies.Count; i++)
                    if (Regain(bodies[i], wdt, w.Values.WeakenRegain)) left = true;
                weak = left;
            }
            if (fx == null || !fx.Any) return;
            fx.Step(wdt, w?.Values.Gravity ?? DefaultGravity, w != null ? ground : null);
        }

        const float DefaultGravity = 98.1f;

        /// <summary>Every effect goes, and what a stain's limits remember.</summary>
        public void Clear()
        {
            fx?.Dispose();
            fx = null;
            stainAt.Clear();
            weak = false;
        }

        public void Dispose() => Clear();
    }
}
