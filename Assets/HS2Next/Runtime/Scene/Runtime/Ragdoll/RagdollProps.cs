// What stands or flies in a ragdoll world beside the bodies: thrown props, and the scene's items that act as obstacles.
//   a prop      a ball, a box or a heavy ball with a rigid body, thrown along a ray at the prop speed. It starts where the ray enters the room, inset by a margin, or close ahead of a
//               camera inside the room or a ray that misses it. At most 40, the oldest goes; each lives 20 seconds of the world. Its gravity is by hand, as a body's. It collides with
//               speculative continuous detection, and it can be grabbed. It is drawn, so it shows in a capture, and it is never saved
//   a hit       from a prop's first touch of a body the impulses of a sixtieth of a second add up: a contact is made a substep before the solver acts on it. Over the body's mass that is
//               the hit's change of speed: the status shows the last one, and from the body's knock hit up it takes a body that stands down. Contacts are read through the engine's
//               contact event, only while a prop exists
//   an obstacle an item of the block's list, as static colliders that follow its node: a mesh collider per readable static mesh under it, one box from the bounds of the rest in the
//               item's space. A skinned part counts by its bounds. An obstacle keeps no world alive and makes none: it joins the world there is
using System;
using System.Collections.Generic;
using HS2Next.Save;
using HS2Next.Scenes.Wet;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace HS2Next.Scenes.Runtime
{
    public sealed partial class RagdollWorld
    {
        /// <summary>The thrown props of this world.</summary>
        public RagdollProps Props { get; private set; }
        /// <summary>The items that act as obstacles in this world.</summary>
        public RagdollObstacles Obstacles { get; private set; }

        /// <summary>True for the rigid body of a prop.</summary>
        public bool IsProp(Rigidbody rb) => Props != null && Props.Find(rb) != null;

        partial void AttachProps()
        {
            Props = new RagdollProps(this);
            Obstacles = new RagdollObstacles(this);
            AddMember(Props);
            AddMember(Obstacles);
        }
    }

    /// <summary>One thrown prop.</summary>
    public sealed class RagdollProp
    {
        internal RagdollProp(string kind, GameObject root, Rigidbody rb, float mass, float diameter)
        {
            Kind = kind;
            Root = root;
            Rb = rb;
            Mass = mass;
            Diameter = diameter;
        }

        /// <summary>One of the kinds of <see cref="RagdollWorldValues.PropKinds"/>.</summary>
        public string Kind { get; }
        public GameObject Root { get; }
        public Rigidbody Rb { get; }
        public float Mass { get; }
        /// <summary>The sphere's diameter or the cube's side.</summary>
        public float Diameter { get; }
        /// <summary>Seconds of the world since the throw.</summary>
        public float Age { get; internal set; }
    }

    public sealed class RagdollProps : IRagdollWorldMember
    {
        /// <summary>The most props of a world.</summary>
        public const int MaxProps = 40;
        /// <summary>Seconds of the world a prop lives.</summary>
        public const float Lifetime = 20f;
        /// <summary>How far ahead a prop starts on a ray that misses the room, and at most inside it.</summary>
        public const float Ahead = 2f;
        /// <summary>A heavy prop's mass against the prop mass.</summary>
        public const float HeavyMass = 10f;
        /// <summary>Seconds of the world the impulses of one hit add up over.</summary>
        public const float HitWindow = 1f / 60f;
        const string ColorProperty = "_Color";
        const string ShaderName = "HS2Next/Unlit/Color";
        // the stream of a prop's random numbers, apart from the shots'
        const int Stream = 0x70726f70;

        static Mesh sphere, cube;

        sealed class OpenHit
        {
            public RagdollProp Prop;
            public RagdollBody Body;
            public float Impulse;
            public int SubstepsLeft;
        }

        readonly RagdollWorld world;
        readonly List<RagdollProp> props = new List<RagdollProp>();
        readonly List<OpenHit> hits = new List<OpenHit>();
        readonly Dictionary<string, Material> materials = new Dictionary<string, Material>(StringComparer.Ordinal);
        readonly Physics.ContactEventDelegate onContacts;
        bool listening;
        long thrown;

        internal RagdollProps(RagdollWorld world)
        {
            this.world = world;
            onContacts = OnContacts;
        }

        public IReadOnlyList<RagdollProp> All => props;
        public int Count => props.Count;
        /// <summary>Props thrown in this world.</summary>
        public long Thrown => thrown;
        /// <summary>The change of speed of the last hit on a body, units per second; 0 before the first.</summary>
        public float LastHit { get; private set; }
        /// <summary>The largest change of speed of a hit in this world: a prop that hit often grazes a body afterwards.</summary>
        public float StrongestHit { get; private set; }
        /// <summary>The node of the body the last hit landed on.</summary>
        public string LastHitNode { get; private set; }
        /// <summary>Hits on bodies in this world.</summary>
        public int Hits { get; private set; }

        /// <summary>The prop of a rigid body, or null.</summary>
        public RagdollProp Find(Rigidbody rb)
        {
            for (int i = 0; i < props.Count; i++) if (props[i].Rb == rb) return props[i];
            return null;
        }

        // ------------------------------------------------------------ the throw

        /// <summary>The diameter of a kind for a size: a ball's, a box's side, a heavy ball's.</summary>
        public static float DiameterOf(string kind, float size) =>
            kind == RagdollWorldValues.PropBox ? 1.5f * size : kind == RagdollWorldValues.PropHeavy ? 2.5f * size : size;

        /// <summary>The margin a prop keeps inside the room's face it enters by.</summary>
        public static float MarginOf(string kind, float size) => (kind == RagdollWorldValues.PropHeavy ? 2.5f : 1.5f) * size;

        /// <summary>
        /// Where a prop starts on a ray: where the ray enters the box from <paramref name="min"/> to <paramref name="max"/>, <paramref name="margin"/> further, and at most half way through it;
        /// from inside the box <see cref="Ahead"/> ahead or half the way to the wall when that is nearer; on a ray that misses the box <see cref="Ahead"/> ahead.
        /// </summary>
        public static Vector3 StartOf(Ray ray, Vector3 min, Vector3 max, float margin)
        {
            var o = ray.origin;
            var d = ray.direction;
            double near = double.NegativeInfinity, far = double.PositiveInfinity;
            for (int a = 0; a < 3; a++)
            {
                if (Math.Abs(d[a]) < 1e-9)
                {
                    if (o[a] < min[a] || o[a] > max[a]) return o + d * Ahead;
                    continue;
                }
                double t0 = (min[a] - o[a]) / (double)d[a], t1 = (max[a] - o[a]) / (double)d[a];
                if (t0 > t1) (t0, t1) = (t1, t0);
                near = Math.Max(near, t0);
                far = Math.Min(far, t1);
            }
            if (near > far || far < 0.0) return o + d * Ahead;
            double t = near <= 0.0 ? Math.Min(Ahead, far * 0.5) : Math.Min(near + margin, (near + far) * 0.5);
            return o + d * (float)t;
        }

        /// <summary>
        /// A prop along <paramref name="ray"/>, its kind, mass, speed and size by the world's values unless given. A world with no room yet gets one first. The oldest prop goes when there
        /// are <see cref="MaxProps"/>.
        /// </summary>
        public RagdollProp Throw(Ray ray, string kind = null, float? mass = null, float? speed = null, float? size = null)
        {
            var v = world.Values;
            if (kind != RagdollWorldValues.PropBall && kind != RagdollWorldValues.PropBox && kind != RagdollWorldValues.PropHeavy) kind = v.PropKind;
            float s = RagdollRanges.PropSize.Clamp(size ?? v.PropSize);
            float m = RagdollRanges.PropMass.Clamp(mass ?? v.PropMass) * (kind == RagdollWorldValues.PropHeavy ? HeavyMass : 1f);
            float at = RagdollRanges.PropSpeed.Clamp(speed ?? v.PropSpeed);
            float diameter = DiameterOf(kind, s);
            world.Floor.EnsureRoom(ray.origin + ray.direction * Ahead);
            while (props.Count >= MaxProps) Remove(0);

            var c = world.RoomCenter;
            var r = world.RoomSize;
            var start = StartOf(ray, new Vector3(c.x - r.x / 2f, world.FloorY, c.y - r.z / 2f), new Vector3(c.x + r.x / 2f, world.FloorY + r.y, c.y + r.z / 2f), MarginOf(kind, s));
            var turn = Quaternion.Euler(Turn(0), Turn(1), Turn(2));
            thrown++;

            var go = world.NewRoot("Prop " + kind);
            go.transform.SetPositionAndRotation(start, turn);
            if (kind == RagdollWorldValues.PropBox)
            {
                var b = go.AddComponent<BoxCollider>();
                b.size = Vector3.one * diameter;
                b.sharedMaterial = world.Material;
                b.providesContacts = true;
            }
            else
            {
                var b = go.AddComponent<SphereCollider>();
                b.radius = diameter * 0.5f;
                b.sharedMaterial = world.Material;
                b.providesContacts = true;
            }
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = m;
            rb.useGravity = false;
            rb.solverIterations = 20;
            rb.solverVelocityIterations = 8;
            rb.interpolation = RigidbodyInterpolation.None;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.position = start;
            rb.rotation = turn;
            rb.linearVelocity = ray.direction * at;
            Draw(go, kind, diameter);

            var p = new RagdollProp(kind, go, rb, m, diameter);
            props.Add(p);
            world.Grabbable(rb, m);
            Listen(true);
            return p;
        }

        float Turn(int axis) => (float)(360.0 * WetMath.Rng(world.Seed ^ Stream, thrown * 3L + axis));

        /// <summary>Every prop goes.</summary>
        public void Clear()
        {
            for (int i = props.Count - 1; i >= 0; i--) Remove(i);
        }

        void Remove(int index)
        {
            var p = props[index];
            props.RemoveAt(index);
            var grabs = world.Grabs;
            for (int i = grabs.Count - 1; i >= 0; i--) if (grabs[i].Target == p.Rb) grabs[i].Drop();
            world.NotGrabbable(p.Rb);
            if (p.Root) Object.DestroyImmediate(p.Root);
            if (props.Count == 0) Listen(false);
        }

        // ------------------------------------------------------------ the drawing

        void Draw(GameObject root, string kind, float diameter)
        {
            var go = new GameObject("Shape");
            go.transform.SetParent(root.transform, false);
            go.transform.localScale = Vector3.one * diameter;
            go.AddComponent<MeshFilter>().sharedMesh = kind == RagdollWorldValues.PropBox ? Primitive(PrimitiveType.Cube, ref cube) : Primitive(PrimitiveType.Sphere, ref sphere);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = MaterialOf(kind);
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        // the engine's own mesh of a primitive; the object it comes on is gone at once, before anything is stepped
        static Mesh Primitive(PrimitiveType type, ref Mesh slot)
        {
            if (slot) return slot;
            var g = GameObject.CreatePrimitive(type);
            slot = g.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(g);
            return slot;
        }

        Material MaterialOf(string kind)
        {
            if (materials.TryGetValue(kind, out var m) && m) return m;
            var sh = Shader.Find(ShaderName);
            if (!sh) return null;
            m = new Material(sh) { name = "Prop " + kind + " (owned)" };
            m.SetColor(ColorProperty, ColorOf(kind));
            materials[kind] = m;
            return m;
        }

        /// <summary>A kind's colour.</summary>
        public static Color ColorOf(string kind) =>
            kind == RagdollWorldValues.PropBox ? new Color(0.8f, 0.65f, 0.4f) : kind == RagdollWorldValues.PropHeavy ? new Color(0.3f, 0.3f, 0.35f) : new Color(1f, 0.55f, 0.15f);

        // ------------------------------------------------------------ hits

        void Listen(bool on)
        {
            if (on == listening) return;
            listening = on;
            if (on) Physics.ContactEvent += onContacts;
            else Physics.ContactEvent -= onContacts;
        }

        // comes at the end of a substep, from the engine: nothing physical is changed here
        void OnContacts(PhysicsScene scene, NativeArray<ContactPairHeader>.ReadOnly headers)
        {
            if (scene != world.Physics) return;
            for (int i = 0; i < headers.Length; i++)
            {
                var h = headers[i];
                var a = h.Body as Rigidbody;
                var b = h.OtherBody as Rigidbody;
                if (!a || !b) continue;
                var part = world.PartOf(a) ?? world.PartOf(b);
                if (part == null) continue;
                var prop = Find(a) ?? Find(b);
                if (prop == null) continue;
                var sum = Vector3.zero;
                bool first = false;
                for (int k = 0; k < h.PairCount; k++)
                {
                    ref readonly var pair = ref h.GetContactPair(k);
                    first |= pair.IsCollisionEnter;
                    sum += pair.ImpulseSum;
                }
                var open = OpenOf(prop, part.Body);
                if (open != null) open.Impulse += sum.magnitude;
                else if (first) hits.Add(new OpenHit { Prop = prop, Body = part.Body, Impulse = sum.magnitude, SubstepsLeft = Mathf.CeilToInt(HitWindow * world.Values.Hz) });
            }
        }

        OpenHit OpenOf(RagdollProp prop, RagdollBody body)
        {
            for (int i = 0; i < hits.Count; i++) if (hits[i].Prop == prop && hits[i].Body == body) return hits[i];
            return null;
        }

        // a hit whose time is up counts
        void TakeHits()
        {
            for (int i = 0; i < hits.Count; i++)
            {
                var o = hits[i];
                if (o.SubstepsLeft > 0) continue;
                hits.RemoveAt(i--);
                var body = o.Body;
                if (body.Parts.Count == 0) continue;
                float dv = o.Impulse / Mathf.Max(1f, body.Mass);
                LastHit = dv;
                StrongestHit = Mathf.Max(StrongestHit, dv);
                LastHitNode = body.NodeId;
                Hits++;
                float bar = body.Values.KnockHit;
                if (bar > 0f && dv >= bar && body.State == RagdollState.Up) body.Knock();
            }
        }

        // ------------------------------------------------------------ IRagdollWorldMember

        public bool HoldsWorld => props.Count > 0;

        public void BeforeSteps(float dt) { }

        public void BeforeSubstep(float h)
        {
            var g = new Vector3(0f, -world.Values.Gravity, 0f);
            for (int i = 0; i < props.Count; i++)
            {
                var rb = props[i].Rb;
                if (!rb.IsSleeping()) rb.AddForce(g, ForceMode.Acceleration);
            }
            for (int i = 0; i < hits.Count; i++) hits[i].SubstepsLeft--;
        }

        public void AfterSteps(float dt)
        {
            TakeHits();
            for (int i = props.Count - 1; i >= 0; i--)
            {
                var p = props[i];
                p.Age += dt;
                if (p.Age >= Lifetime) Remove(i);
            }
        }

        public void WorldClosing()
        {
            Clear();
            Listen(false);
            hits.Clear();
            foreach (var m in materials.Values) if (m) Object.DestroyImmediate(m);
            materials.Clear();
        }
    }

    /// <summary>An item at work as an obstacle.</summary>
    public sealed class RagdollObstacle
    {
        readonly RagdollWorld world;
        readonly List<(Transform source, Transform collider)> pieces = new List<(Transform, Transform)>();
        GameObject root, boxObject;
        GameObject instance;
        Vector3 at, scale;
        Quaternion rot;

        internal RagdollObstacle(RagdollWorld world, string nodeId)
        {
            this.world = world;
            NodeId = nodeId;
        }

        public string NodeId { get; }
        /// <summary>Mesh colliders of this item.</summary>
        public int Meshes => pieces.Count;
        /// <summary>The box of what has no readable mesh; null with none.</summary>
        public BoxCollider Box { get; private set; }
        /// <summary>The root of the item's colliders, null until the item is built.</summary>
        public GameObject Root => root;

        /// <summary>The item as the last frame left it; true when a collider moved.</summary>
        internal bool Follow()
        {
            var r = world.Runtime.FindItem(NodeId);
            var inst = r != null && r.State == RealizationState.Built ? r.Instance : null;
            var holder = inst ? r.Holder : null;
            if (!holder || !holder.gameObject.activeInHierarchy)
            {
                if (root) root.SetActive(false);
                return false;
            }
            if (inst != instance || scale != holder.lossyScale)
            {
                Make(r);
                return true;
            }
            if (!root.activeSelf) root.SetActive(true);
            bool moved = false;
            holder.GetPositionAndRotation(out var p, out var q);
            if (p != at || q != rot)
            {
                at = p;
                rot = q;
                if (boxObject) boxObject.transform.SetPositionAndRotation(p, q);
                moved = Box != null;
            }
            for (int i = 0; i < pieces.Count; i++)
            {
                var (source, collider) = pieces[i];
                if (!source)
                {
                    instance = null;
                    return moved;
                }
                source.GetPositionAndRotation(out var sp, out var sq);
                collider.GetPositionAndRotation(out var cp, out var cq);
                var ss = source.lossyScale;
                if (sp == cp && sq == cq && ss == collider.localScale) continue;
                collider.SetPositionAndRotation(sp, sq);
                collider.localScale = ss;
                moved = true;
            }
            return moved;
        }

        void Make(ItemRealization r)
        {
            Destroy();
            instance = r.Instance;
            var holder = r.Holder;
            root = world.NewRoot("Obstacle " + NodeId);
            scale = holder.lossyScale;
            holder.GetPositionAndRotation(out at, out rot);
            var toItem = Matrix4x4.TRS(at, rot, Vector3.one).inverse;
            var min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            var max = -min;
            bool any = false;
            foreach (var renderer in VisibilityApplier.OwnRenderers(holder))
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (renderer is MeshRenderer)
                {
                    var mesh = renderer.TryGetComponent<MeshFilter>(out var filter) ? filter.sharedMesh : null;
                    if (!mesh) continue;
                    if (mesh.isReadable && mesh.vertexCount > 0 && mesh.GetTopology(0) == MeshTopology.Triangles)
                    {
                        Piece(renderer.transform, mesh);
                        continue;
                    }
                }
                else if (!(renderer is SkinnedMeshRenderer)) continue;
                var space = renderer is SkinnedMeshRenderer s && s.rootBone ? s.rootBone : renderer.transform;
                var b = renderer.localBounds;
                var e = b.extents;
                for (int k = 0; k < 8; k++)
                {
                    var c = b.center + new Vector3((k & 1) != 0 ? e.x : -e.x, (k & 2) != 0 ? e.y : -e.y, (k & 4) != 0 ? e.z : -e.z);
                    var w = toItem.MultiplyPoint3x4(space.TransformPoint(c));
                    min = Vector3.Min(min, w);
                    max = Vector3.Max(max, w);
                }
                any = true;
            }
            if (!any) return;
            boxObject = new GameObject("Box");
            boxObject.transform.SetParent(root.transform, false);
            boxObject.transform.SetPositionAndRotation(at, rot);
            Box = boxObject.AddComponent<BoxCollider>();
            Box.center = (min + max) * 0.5f;
            Box.size = Vector3.Max(max - min, Vector3.one * 0.01f);
            Box.sharedMaterial = world.Material;
        }

        void Piece(Transform source, Mesh mesh)
        {
            var go = new GameObject("Mesh");
            go.transform.SetParent(root.transform, false);
            source.GetPositionAndRotation(out var p, out var q);
            go.transform.SetPositionAndRotation(p, q);
            go.transform.localScale = source.lossyScale;
            var c = go.AddComponent<MeshCollider>();
            c.sharedMaterial = world.Material;
            c.sharedMesh = mesh;
            pieces.Add((source, go.transform));
        }

        internal void Destroy()
        {
            if (root) Object.DestroyImmediate(root);
            root = boxObject = null;
            Box = null;
            pieces.Clear();
            instance = null;
        }
    }

    public sealed class RagdollObstacles : IRagdollWorldMember
    {
        readonly RagdollWorld world;
        readonly List<RagdollObstacle> live = new List<RagdollObstacle>();
        readonly List<string> seen = new List<string>();

        internal RagdollObstacles(RagdollWorld world) { this.world = world; }

        public IReadOnlyList<RagdollObstacle> All => live;
        /// <summary>The obstacle of a node, or null.</summary>
        public RagdollObstacle Of(string nodeId)
        {
            for (int i = 0; i < live.Count; i++) if (live[i].NodeId == nodeId) return live[i];
            return null;
        }

        /// <summary>
        /// An item is an obstacle or stops being one: an edit of the scene's block, one history entry. <paramref name="count"/> is how many there are then. What is no item is refused.
        /// </summary>
        public static CommandResult Set(SceneRuntime rt, string id, bool on, out int count, ExecuteOptions o = default)
        {
            var block = rt.Model.Ragdoll?.Clone() ?? new SceneRagdollWorld();
            count = block.Obstacles.Count;
            if (rt.Model.Find(id)?.State as ItemNodeState == null) return CommandResult.Error(CommandCodes.BadArgs, "no item " + id);
            if (on == block.Obstacles.Contains(id)) return CommandResult.Success(null);
            if (on) block.Obstacles.Add(id);
            else block.Obstacles.Remove(id);
            var res = rt.Session.Commands.Execute(new RagdollWorldEditCommand(block, RagdollWorldEditCommand.LabelObstacle), o);
            if (res.Ok)
            {
                count = block.Obstacles.Count;
                rt.ApplyNow();
            }
            return res;
        }

        /// <summary>The block's list at work: what left it goes, what came is made in the next tick.</summary>
        internal void Sync()
        {
            var doc = world.Runtime.Model.Ragdoll?.Obstacles;
            int n = doc?.Count ?? 0;
            bool same = n == seen.Count;
            for (int i = 0; same && i < n; i++) same = ReferenceEquals(doc[i], seen[i]) || doc[i] == seen[i];
            if (same) return;
            seen.Clear();
            if (doc != null) seen.AddRange(doc);
            var want = new List<string>();
            if (doc != null) want.AddRange(world.Runtime.Model.Ragdoll.AppliedObstacles());
            for (int i = live.Count - 1; i >= 0; i--)
            {
                if (want.Contains(live[i].NodeId)) continue;
                live[i].Destroy();
                live.RemoveAt(i);
            }
            // in the block's order: the order of a world's colliders is part of what repeats
            for (int i = 0; i < want.Count; i++) if (Of(want[i]) == null) live.Add(new RagdollObstacle(world, want[i]));
        }

        // ------------------------------------------------------------ IRagdollWorldMember

        public bool HoldsWorld => false;

        public void BeforeSteps(float dt)
        {
            Sync();
            bool moved = false;
            for (int i = 0; i < live.Count; i++) moved |= live[i].Follow();
            if (!moved) return;
            Physics.SyncTransforms();
            var bodies = world.Bodies;
            for (int i = 0; i < bodies.Count; i++) if (!bodies[i].Buried) bodies[i].WakeAll();
        }

        public void BeforeSubstep(float h) { }

        public void AfterSteps(float dt) { }

        public void WorldClosing()
        {
            for (int i = 0; i < live.Count; i++) live[i].Destroy();
            live.Clear();
            seen.Clear();
        }
    }
}
