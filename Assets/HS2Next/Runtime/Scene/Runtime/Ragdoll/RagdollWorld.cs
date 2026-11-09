// The physics world of a scene's ragdolls: one local physics scene, stepped by hand on the scene clock. It is created with the first body, prop or pusher and destroyed when the
// last of them goes: a physics scene that held bodies does not repeat a run from the third generation on, a fresh one does.
//   objects   plain objects in that scene; every root moves there at creation, before a collider or a rigid body: a new object lands in the active scene, which is the map's
//             while a map is loaded, and would go with the map
//   gravity   by hand, as an acceleration per substep: the engine's gravity is global and stays as it is
//   a tick    what follows moves (pushers from their bones, pins from what they follow, members), the drives and the states, then the substeps, then the members' effects
//   edit mode MakeScene and FreeScene are the doors: a scene of this kind can only be made in play mode, so the tests' setup sets them
// What lives here beside the bodies comes in by IRagdollWorldMember; the floor under a body by IRagdollGround, with the room's plain floor when there is none. The partial
// methods at the end are where another file of this class attaches them.
using System;
using System.Collections.Generic;
using HS2Next.Save;
using HS2Next.Scenes.Ragdoll;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>What lives in a world beside the bodies: props, obstacles, effects. Every call comes from the world's tick; none runs with no world.</summary>
    public interface IRagdollWorldMember
    {
        /// <summary>True while this member needs the world to stay (a prop, an obstacle). Effects do not.</summary>
        bool HoldsWorld { get; }
        /// <summary>The start of a tick: what follows something moves, reading last frame's transforms.</summary>
        void BeforeSteps(float dt);
        /// <summary>Before each substep of <paramref name="h"/> seconds: forces and moves.</summary>
        void BeforeSubstep(float h);
        /// <summary>The end of a tick, with the world's time of the frame.</summary>
        void AfterSteps(float dt);
        /// <summary>The world goes: everything of this member goes with it.</summary>
        void WorldClosing();
    }

    /// <summary>The ground under the bodies. Without one the room's floor is all there is.</summary>
    public interface IRagdollGround
    {
        /// <summary>The height of the room's floor for a body about to be placed, and where it comes from; false leaves the choice to the plain rules (feet, fixed).</summary>
        bool FloorFor(RagdollBody body, out float height, out string source);
        /// <summary>A body is built; patches may start.</summary>
        void BodyBuilt(RagdollBody body);
        /// <summary>A body goes.</summary>
        void BodyGone(RagdollBody body);
        /// <summary>The start of a tick, after the members moved.</summary>
        void BeforeSteps(float dt);
        /// <summary>The scene's block changed (null: the scene has none): the obstacles and whatever else lives in it.</summary>
        void BlockChanged(SceneRagdollWorld block);
        /// <summary>The world goes.</summary>
        void WorldClosing();
    }

    /// <summary>Where a floor's height comes from, as the status says it.</summary>
    public static class RagdollFloorSources
    {
        public const string Map = "map", Feet = "feet", Fixed = "fixed";
    }

    public sealed partial class RagdollWorld
    {
        /// <summary>Makes the world's scene. The Editor's test setup replaces it in edit mode.</summary>
        public static Func<Scene> MakeScene = DefaultScene;
        /// <summary>Frees the world's scene.</summary>
        public static Action<Scene> FreeScene = DefaultFree;

        static int counter;

        static Scene DefaultScene() => SceneManager.CreateScene("HS2Next Ragdoll " + (++counter), new CreateSceneParameters(LocalPhysicsMode.Physics3D));

        static void DefaultFree(Scene s)
        {
            if (s.IsValid() && s.isLoaded) SceneManager.UnloadSceneAsync(s);
        }

        const float WallThickness = 10f;

        readonly List<RagdollBody> bodies = new List<RagdollBody>();
        readonly List<IRagdollWorldMember> members = new List<IRagdollWorldMember>();
        readonly List<RagdollLivePusher> pushers = new List<RagdollLivePusher>();
        readonly Dictionary<Rigidbody, RagdollBodyPart> partOf = new Dictionary<Rigidbody, RagdollBodyPart>();
        readonly Dictionary<Rigidbody, float> grabMass = new Dictionary<Rigidbody, float>();
        readonly RaycastHit[] pickHits = new RaycastHit[64];
        readonly List<RagdollGrab> grabs = new List<RagdollGrab>();
        GameObject room;
        double accum;

        public RagdollWorld(SceneRuntime runtime)
        {
            Runtime = runtime;
            Scene = MakeScene();
            Physics = Scene.GetPhysicsScene();
            Material = new PhysicsMaterial("Ragdoll")
            {
                dynamicFriction = 0.6f, staticFriction = 0.7f, bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Average, bounceCombine = PhysicsMaterialCombine.Minimum,
            };
            AttachGround();
            AttachProps();
            AttachEffects();
        }

        public SceneRuntime Runtime { get; }
        public Scene Scene { get; }
        public PhysicsScene Physics { get; }
        public PhysicsMaterial Material { get; }
        /// <summary>The values of the world as they act (clamped), from the scene's block.</summary>
        public RagdollWorldValues Values { get; internal set; } = new RagdollWorldValues();
        /// <summary>The floor under the bodies; null with the room's floor alone.</summary>
        public IRagdollGround Ground { get; set; }
        public IReadOnlyList<RagdollBody> Bodies => bodies;
        public IReadOnlyList<RagdollLivePusher> Pushers => pushers;
        public IReadOnlyList<IRagdollWorldMember> Members => members;
        /// <summary>The grabs and pins of this world, the ones of props included.</summary>
        public IReadOnlyList<RagdollGrab> Grabs => grabs;
        /// <summary>Substeps run in this world.</summary>
        public long Steps { get; private set; }
        /// <summary>The substeps of the last tick.</summary>
        public int LastSubsteps { get; private set; }
        /// <summary>A frame's shots count on from 0 in a fresh world: what a member's random numbers take with <see cref="Seed"/>.</summary>
        public long ShotCounter;
        /// <summary>The seed of the world's random numbers.</summary>
        public int Seed;
        /// <summary>The height of the room's floor now.</summary>
        public float FloorY { get; private set; }
        /// <summary>Where <see cref="FloorY"/> comes from.</summary>
        public string FloorSource { get; private set; } = RagdollFloorSources.Feet;
        /// <summary>True once a room stands.</summary>
        public bool HasRoom => room;
        public Vector2 RoomCenter { get; private set; }
        public Vector3 RoomSize { get; private set; }
        /// <summary>The room's root, null before the first room.</summary>
        public GameObject RoomObject => room;
        /// <summary>True when nothing keeps this world: it should go.</summary>
        public bool Empty
        {
            get
            {
                if (bodies.Count > 0 || pushers.Count > 0) return false;
                for (int i = 0; i < members.Count; i++) if (members[i].HoldsWorld) return false;
                return true;
            }
        }

        partial void AttachGround();
        partial void AttachProps();
        partial void AttachEffects();

        /// <summary>A new root object of this world: in the world's scene before anything physical is added to it.</summary>
        public GameObject NewRoot(string name)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, Scene);
            return go;
        }

        public void AddMember(IRagdollWorldMember m)
        {
            if (m != null && !members.Contains(m)) members.Add(m);
        }

        // ------------------------------------------------------------ the room

        /// <summary>The room stands at this place, floor height and size: six thick walls that do not show. Rebuilt in place.</summary>
        public void SetRoom(Vector2 center, Vector3 size, float floorY, string source)
        {
            RoomCenter = center;
            RoomSize = size;
            FloorY = floorY;
            FloorSource = source;
            if (room) Object.DestroyImmediate(room);
            room = NewRoot("Room");
            room.transform.position = new Vector3(center.x, floorY, center.y);
            float w = size.x, h = size.y, d = size.z, t = WallThickness;
            Wall("Floor", new Vector3(0f, -t / 2f, 0f), new Vector3(w + 2f * t, t, d + 2f * t));
            Wall("Ceiling", new Vector3(0f, h + t / 2f, 0f), new Vector3(w + 2f * t, t, d + 2f * t));
            Wall("WallRight", new Vector3(w / 2f + t / 2f, h / 2f, 0f), new Vector3(t, h, d + 2f * t));
            Wall("WallLeft", new Vector3(-w / 2f - t / 2f, h / 2f, 0f), new Vector3(t, h, d + 2f * t));
            Wall("WallFront", new Vector3(0f, h / 2f, d / 2f + t / 2f), new Vector3(w, h, t));
            Wall("WallBack", new Vector3(0f, h / 2f, -d / 2f - t / 2f), new Vector3(w, h, t));
        }

        void Wall(string name, Vector3 at, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(room.transform, false);
            go.transform.localPosition = at;
            var c = go.AddComponent<BoxCollider>();
            c.size = size;
            c.sharedMaterial = Material;
        }

        /// <summary>True for a collider of the room or the ground: what a bullet stops at.</summary>
        public bool IsStatic(Collider c) => c && !c.attachedRigidbody;

        // ------------------------------------------------------------ bodies

        internal void Add(RagdollBody b)
        {
            bodies.Add(b);
            for (int i = 0; i < b.Parts.Count; i++) partOf[b.Parts[i].Rb] = b.Parts[i];
        }

        internal void Remove(RagdollBody b)
        {
            bodies.Remove(b);
            Forget(b);
        }

        internal void Forget(RagdollBody b)
        {
            for (int i = 0; i < b.Parts.Count; i++) if (b.Parts[i].Rb) partOf.Remove(b.Parts[i].Rb);
        }

        internal void Remember(RagdollBody b)
        {
            for (int i = 0; i < b.Parts.Count; i++) partOf[b.Parts[i].Rb] = b.Parts[i];
        }

        /// <summary>The body of a character node, or null.</summary>
        public RagdollBody BodyOf(string nodeId)
        {
            for (int i = 0; i < bodies.Count; i++) if (bodies[i].NodeId == nodeId) return bodies[i];
            return null;
        }

        /// <summary>The part behind a rigid body, or null (a prop, an anchor).</summary>
        public RagdollBodyPart PartOf(Rigidbody rb) => rb != null && partOf.TryGetValue(rb, out var p) ? p : null;

        /// <summary>Tells the world that a rigid body that is no part can be grabbed, with the mass a grab counts for it.</summary>
        public void Grabbable(Rigidbody rb, float mass) { if (rb) grabMass[rb] = mass; }
        /// <summary>That rigid body cannot be grabbed any more.</summary>
        public void NotGrabbable(Rigidbody rb) { if (!ReferenceEquals(rb, null)) grabMass.Remove(rb); }

        /// <summary>
        /// The nearest part or prop under a ray; walls and anything else are looked through, so a camera outside the room still reaches a body. False when it meets none.
        /// </summary>
        public bool Pick(Ray ray, out RaycastHit hit)
        {
            hit = default;
            int n = Physics.Raycast(ray.origin, ray.direction, pickHits, 10000f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.PositiveInfinity;
            bool found = false;
            for (int i = 0; i < n; i++)
            {
                var h = pickHits[i];
                var rb = h.rigidbody;
                if (!rb || h.distance >= best) continue;
                if (!partOf.ContainsKey(rb) && !grabMass.ContainsKey(rb)) continue;
                best = h.distance;
                hit = h;
                found = true;
            }
            return found;
        }

        // ------------------------------------------------------------ grabs and pushers

        /// <summary>A grab of a part or a prop at <paramref name="point"/>, with the grab's spring of the world's values; null when the rigid body can be grabbed by nothing.</summary>
        public RagdollGrab Grab(Rigidbody rb, Vector3 point) => Grab(rb, point, Values.GrabStiffness, Values.GrabMax);

        internal RagdollGrab Grab(Rigidbody rb, Vector3 point, float stiffness, float maxW)
        {
            if (!rb) return null;
            float mass;
            var part = PartOf(rb);
            if (part != null) mass = part.Body.Mass;
            else if (!grabMass.TryGetValue(rb, out mass)) return null;
            var g = new RagdollGrab(this, part, rb, point, mass, stiffness, maxW);
            grabs.Add(g);
            return g;
        }

        internal void Forget(RagdollGrab g) => grabs.Remove(g);

        internal void Add(RagdollLivePusher p) => pushers.Add(p);

        internal void Remove(RagdollLivePusher p)
        {
            pushers.Remove(p);
            p.Destroy();
        }

        // ------------------------------------------------------------ the tick

        /// <summary>One frame of the scene clock. With <paramref name="keepAll"/> (a step, a recording) no substep is dropped.</summary>
        internal void Tick(float dt, bool keepAll)
        {
            var v = Values;
            int hz = (int)v.Hz;
            float wdt = dt * v.TimeScale;
            // 2. what follows moves, from the transforms as the last frame left them
            for (int i = pushers.Count - 1; i >= 0; i--) pushers[i].Read(this);
            for (int i = 0; i < bodies.Count; i++) bodies[i].FollowPins();
            for (int i = 0; i < members.Count; i++) members[i].BeforeSteps(dt);
            Ground?.BeforeSteps(dt);
            // 3. the states and the drives
            for (int i = 0; i < bodies.Count; i++) bodies[i].BeforeSteps(wdt);
            // 4. the substeps
            int n = RagdollMath.Substeps(ref accum, dt, v.TimeScale, hz, RagdollMath.MaxSubsteps, keepAll);
            LastSubsteps = n;
            float h = 1f / hz;
            var g = new Vector3(0f, -v.Gravity, 0f);
            for (int s = 0; s < n; s++)
            {
                for (int i = 0; i < bodies.Count; i++) bodies[i].Forces(g);
                for (int i = 0; i < members.Count; i++) members[i].BeforeSubstep(h);
                for (int i = 0; i < grabs.Count; i++) grabs[i].Move();
                float t = (s + 1f) / n;
                for (int i = 0; i < pushers.Count; i++) pushers[i].Move(t);
                Physics.Simulate(h);
                Steps++;
            }
            for (int i = 0; i < bodies.Count; i++) bodies[i].AfterSteps(wdt);
            // 5. effects
            for (int i = 0; i < members.Count; i++) members[i].AfterSteps(wdt);
        }

        // ------------------------------------------------------------ the end

        /// <summary>Everything in this world goes, and its scene with it.</summary>
        internal void Close()
        {
            for (int i = members.Count - 1; i >= 0; i--) members[i].WorldClosing();
            members.Clear();
            Ground?.WorldClosing();
            Ground = null;
            for (int i = pushers.Count - 1; i >= 0; i--) pushers[i].Destroy();
            pushers.Clear();
            for (int i = grabs.Count - 1; i >= 0; i--) grabs[i].Drop();
            grabs.Clear();
            partOf.Clear();
            grabMass.Clear();
            bodies.Clear();
            if (room) Object.DestroyImmediate(room);
            room = null;
            if (Material) Object.DestroyImmediate(Material);
            FreeScene(Scene);
        }
    }
}
