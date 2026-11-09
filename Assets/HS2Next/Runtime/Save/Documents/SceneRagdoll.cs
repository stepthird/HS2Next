// The ragdoll blocks of a scene file. `chara.ragdoll`, the member of a character node's section after `wet`: whether the character is a body now, its values, its pins, ties
// and holds and the last settled pose. `ragdoll`, the top-level member of scene.json after `constraints` and before `ext`: the values of the world, the room, the floor, the
// pushers and the obstacles. Each is written only when it is not empty, and a scene with either is format revision 5: the members of this phase join that revision, with no
// revision 6 and no new migration. Unity-free save-layer classes: a node keeps one block (never null, empty by default) and field paths reach every member
// (`chara.ragdoll.pose`); the scene keeps one world block (SceneModel.Ragdoll, null = the scene has none, never an empty one). Only the two ragdoll edit commands replace a block; the
// settled pose is written by the capture before a save. Nothing here writes a card, a coordinate or a pose file, and nothing of a moving body is here: no velocity, no contact,
// no state of the body, no weakness, no prop, no effect.
//
// `values` holds only what differs from the default: a value set back to its default leaves the file, and a default written in a file reads as the default and is not written back.
// `pins`, `ties`, `holds` and `pose` are written when they hold something; `on` and `limp` always, in a block that is written at all.
//
// The reader binds strictly (a wrong type fails the load as for every core member, a JSON null included) and repairs nothing away: a value out of range and the excess over a
// limit stay in the document and are written back, each is reported, and the Applied members below say what acts. A part key that is no part of a body is kept and inactive. An
// unknown member is kept at every level. A repeated pin or pusher id is the one thing the reader changes: the later entry gets a derived id.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Newtonsoft.Json.Linq;

namespace HS2Next.Save
{
    /// <summary>The limits of a scene. A command beyond one is refused; a file's excess is kept in the document, reported (<see cref="RagdollFindings.Limit"/>) and,
    /// past a block's own limit, not applied.</summary>
    public static class RagdollLimits
    {
        /// <summary>Pins of all the characters of a scene.</summary>
        public const int MaxPins = 16;
        /// <summary>Ties of all the characters of a scene.</summary>
        public const int MaxTies = 16;
        public const int MaxPushers = 32;
        public const int MaxObstacles = 64;

        /// <summary>
        /// Reports (<see cref="RagdollFindings.Limit"/>) the pins and the ties a whole scene holds over the limit when no single block is over it: a block that is has
        /// reported itself. The blocks stay as they are.
        /// </summary>
        internal static void ReportScene(IEnumerable<SceneNode> nodes, DocReader r)
        {
            int pins = 0, ties = 0, mostPins = 0, mostTies = 0;
            foreach (var n in nodes)
            {
                if (!(n.State is CharaNodeState c)) continue;
                pins += c.Ragdoll.Pins.Count;
                ties += c.Ragdoll.Ties.Count;
                mostPins = Math.Max(mostPins, c.Ragdoll.Pins.Count);
                mostTies = Math.Max(mostTies, c.Ragdoll.Ties.Count);
            }
            if (pins > MaxPins && mostPins <= MaxPins) r.Report(FindingSeverity.Warning, RagdollFindings.Limit, "nodes[*].chara.ragdoll.pins", pins + " pins in the scene are over the limit of " + MaxPins + "; kept");
            if (ties > MaxTies && mostTies <= MaxTies) r.Report(FindingSeverity.Warning, RagdollFindings.Limit, "nodes[*].chara.ragdoll.ties", ties + " ties in the scene are over the limit of " + MaxTies + "; kept");
        }
    }

    /// <summary>Finding codes of the ragdoll.</summary>
    public static class RagdollFindings
    {
        /// <summary>Warning (reader): entries over a limit, a second hold on a part; kept, not applied. Also the word a refused command gives.</summary>
        public const string Limit = "ragdoll.limit";
        /// <summary>Info (reader): a value out of range, kept and clamped when applied; a rotation of no length, kept and not applied.</summary>
        public const string Range = "ragdoll.range";
        /// <summary>Warning (reader): a part key that is no part of a body; kept, inactive.</summary>
        public const string BadPart = "ragdoll.badPart";
        /// <summary>Info (reader): a pin or pusher without an id, or with the id of an earlier one; it takes a derived id.</summary>
        public const string Id = "ragdoll.id";
        /// <summary>Warning (runtime): a character without the bones of a body gets none; the block stays.</summary>
        public const string NoBones = "ragdoll.noBones";
        /// <summary>Warning (runtime): no rest pose to size a body from; the current pose is used.</summary>
        public const string NoRest = "ragdoll.noRest";
        /// <summary>Warning (runtime): the map gives no ground; the floor falls to the body's feet.</summary>
        public const string NoGround = "ragdoll.noGround";
        /// <summary>Warning (runtime): a part drives nothing, its bone is not written.</summary>
        public const string Protected = "ragdoll.protected";
    }

    /// <summary>One part of a body: its key, the bone it writes, its parent part and its share of the body's mass.</summary>
    public sealed class RagdollPartInfo
    {
        public readonly string Key, Bone, Parent;
        public readonly float Share;

        internal RagdollPartInfo(string key, string bone, string parent, float share)
        {
            Key = key;
            Bone = bone;
            Parent = parent;
            Share = share;
        }

        public override string ToString() => Key;
    }

    /// <summary>The 16 parts of a body, parents before their children: the order a body is built, read and written in.</summary>
    public static class RagdollParts
    {
        public const string Pelvis = "pelvis", Abdomen = "abdomen", Chest = "chest", Head = "head";

        static readonly RagdollPartInfo[] rows =
        {
            new RagdollPartInfo(Pelvis, "cf_J_Hips", null, 0.14f),
            new RagdollPartInfo(Abdomen, "cf_J_Spine01", Pelvis, 0.12f),
            new RagdollPartInfo(Chest, "cf_J_Spine02", Abdomen, 0.2f),
            new RagdollPartInfo(Head, "cf_J_Neck", Chest, 0.08f),
            new RagdollPartInfo("upperarm_L", "cf_J_ArmUp00_L", Chest, 0.03f),
            new RagdollPartInfo("upperarm_R", "cf_J_ArmUp00_R", Chest, 0.03f),
            new RagdollPartInfo("forearm_L", "cf_J_ArmLow01_L", "upperarm_L", 0.018f),
            new RagdollPartInfo("forearm_R", "cf_J_ArmLow01_R", "upperarm_R", 0.018f),
            new RagdollPartInfo("hand_L", "cf_J_Hand_L", "forearm_L", 0.007f),
            new RagdollPartInfo("hand_R", "cf_J_Hand_R", "forearm_R", 0.007f),
            new RagdollPartInfo("thigh_L", "cf_J_LegUp00_L", Pelvis, 0.1f),
            new RagdollPartInfo("thigh_R", "cf_J_LegUp00_R", Pelvis, 0.1f),
            new RagdollPartInfo("shin_L", "cf_J_LegLow01_L", "thigh_L", 0.045f),
            new RagdollPartInfo("shin_R", "cf_J_LegLow01_R", "thigh_R", 0.045f),
            new RagdollPartInfo("foot_L", "cf_J_Foot01_L", "shin_L", 0.015f),
            new RagdollPartInfo("foot_R", "cf_J_Foot01_R", "shin_R", 0.015f),
        };

        static readonly string[] keys = Array.ConvertAll(rows, p => p.Key);

        /// <summary>The parts, parents first.</summary>
        public static IReadOnlyList<RagdollPartInfo> All => rows;
        /// <summary>The part keys, parents first.</summary>
        public static IReadOnlyList<string> Keys => keys;

        /// <summary>The place of <paramref name="key"/> in <see cref="All"/>, or -1.</summary>
        public static int IndexOf(string key) => Array.IndexOf(keys, key);
        public static bool IsKnown(string key) => IndexOf(key) >= 0;
        /// <summary>The part <paramref name="key"/>, or null.</summary>
        public static RagdollPartInfo Find(string key)
        {
            int i = IndexOf(key);
            return i < 0 ? null : rows[i];
        }

        /// <summary>The keys, for a message.</summary>
        public static string List() => string.Join(", ", keys);

        /// <summary>False for a rotation of no length, which cannot act.</summary>
        internal static bool IsRotation(Quat4 q) => (double)q.X * q.X + (double)q.Y * q.Y + (double)q.Z * q.Z + (double)q.W * q.W > 1e-8;
    }

    /// <summary>The ranges of the values: what the panel offers. A command refuses nothing here; a file's value is kept and clamped when applied
    /// (<see cref="RagdollFindings.Range"/>).</summary>
    public static class RagdollRanges
    {
        public static readonly WetRange Unit = new WetRange(0f, 1f);
        public static readonly WetRange Muscle = new WetRange(0f, 60f);
        public static readonly WetRange MuscleDamping = new WetRange(0f, 2f);
        public static readonly WetRange PinFreq = new WetRange(2f, 60f);
        public static readonly WetRange PinFalloff = new WetRange(0f, 20f);
        public static readonly WetRange KnockDistance = new WetRange(0f, 20f);
        public static readonly WetRange KnockHit = new WetRange(0f, 100f);
        public static readonly WetRange MinDown = new WetRange(0f, 10f);
        public static readonly WetRange GetUp = new WetRange(0.2f, 5f);
        public static readonly WetRange Mass = new WetRange(5f, 150f);
        public static readonly WetRange Gravity = new WetRange(0f, 200f);
        public static readonly WetRange Hz = new WetRange(60f, 1000f);
        public static readonly WetRange Friction = new WetRange(0f, 30f);
        public static readonly WetRange AngularDrag = new WetRange(0f, 5f);
        public static readonly WetRange PushSpeed = new WetRange(0f, 100f);
        public static readonly WetRange GrabStiffness = new WetRange(5f, 300f);
        public static readonly WetRange GrabMax = new WetRange(0.5f, 20f);
        public static readonly WetRange FollowStiffness = new WetRange(5f, 2000f);
        public static readonly WetRange FollowMax = new WetRange(0.5f, 60f);
        public static readonly WetRange BlendOut = new WetRange(0f, 2f);
        public static readonly WetRange Transfer = new WetRange(0.05f, 1f);
        public static readonly WetRange MaxPartDv = new WetRange(5f, 200f);
        public static readonly WetRange HitWeaken = new WetRange(20f, 2000f);
        public static readonly WetRange WeakenRegain = new WetRange(0.1f, 5f);
        public static readonly WetRange StaggerAt = new WetRange(50f, 3000f);
        public static readonly WetRange Momentum = new WetRange(20f, 2000f);
        public static readonly WetRange FireRate = new WetRange(1f, 20f);
        public static readonly WetRange PropMass = new WetRange(0.1f, 30f);
        public static readonly WetRange PropSpeed = new WetRange(5f, 300f);
        public static readonly WetRange PropSize = new WetRange(0.3f, 5f);
        public static readonly WetRange RoomWidth = new WetRange(10f, 200f);
        public static readonly WetRange RoomHeight = new WetRange(10f, 100f);
        /// <summary>A pusher's radius: not below 0.</summary>
        public static readonly WetRange Radius = new WetRange(0f, float.MaxValue);
    }

    /// <summary>The presets of a body. A preset writes the values of its row and nothing else.</summary>
    public static class RagdollPreset
    {
        public const string Limp = "limp", Stiff = "stiff", Puppet = "puppet", HipsPin = "hipsPin";

        static readonly string[] names = { Limp, Stiff, Puppet, HipsPin };

        /// <summary>The presets, in the order the panel shows them.</summary>
        public static IReadOnlyList<string> Names => names;

        public static bool IsKnown(string preset) => Array.IndexOf(names, preset) >= 0;

        /// <summary>Writes <paramref name="preset"/>'s values into <paramref name="values"/>; false for an unknown name, and then nothing changes.</summary>
        public static bool ApplyTo(RagdollBodyValues values, string preset)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            switch (preset)
            {
                case Limp:
                    values.Muscle = 0f;
                    values.Pin = 0f;
                    return true;
                case Stiff:
                    values.Muscle = 20f;
                    values.MuscleDamping = 1f;
                    values.Pin = 0f;
                    return true;
                case Puppet:
                    Pinned(values, 12f, 3f, false, 3.5f, 8f);
                    return true;
                case HipsPin:
                    Pinned(values, 15f, 0f, true, 0f, 0f);
                    return true;
                default:
                    return false;
            }
        }

        static void Pinned(RagdollBodyValues v, float freq, float falloff, bool hipsOnly, float knockDistance, float knockHit)
        {
            v.Muscle = 20f;
            v.MuscleDamping = 1f;
            v.Pin = 1f;
            v.PinFreq = freq;
            v.PinFalloff = falloff;
            v.PinHipsOnly = hipsOnly;
            v.KnockDistance = knockDistance;
            v.KnockHit = knockHit;
        }
    }

    /// <summary>The members of one <c>values</c> object in the order they are written: one table behind its reader, its writer, its clamped copy and access by name.</summary>
    sealed class RagdollValueTable<T> where T : DocObject, new()
    {
        sealed class Row
        {
            public string Name;
            public WetRange Range;
            public Func<T, float> GetF;
            public Action<T, float> SetF;
            public Func<T, bool> GetB;
            public Action<T, bool> SetB;
            public Func<T, string> GetS;
            public Action<T, string> SetS;
            public string[] Known;
        }

        readonly List<Row> rows = new List<Row>();
        readonly List<string> names = new List<string>();
        readonly T defaults = new T();

        void Add(Row r)
        {
            rows.Add(r);
            names.Add(r.Name);
        }

        public RagdollValueTable<T> F(string name, WetRange range, Func<T, float> get, Action<T, float> set)
        {
            Add(new Row { Name = name, Range = range, GetF = get, SetF = set });
            return this;
        }

        public RagdollValueTable<T> B(string name, Func<T, bool> get, Action<T, bool> set)
        {
            Add(new Row { Name = name, GetB = get, SetB = set });
            return this;
        }

        public RagdollValueTable<T> S(string name, string[] known, Func<T, string> get, Action<T, string> set)
        {
            Add(new Row { Name = name, Known = known, GetS = get, SetS = set });
            return this;
        }

        public IReadOnlyList<string> Names => names;

        Row Find(string name)
        {
            int i = names.IndexOf(name);
            return i < 0 ? null : rows[i];
        }

        static bool Same(Row m, T a, T b) =>
            m.GetF != null ? m.GetF(a) == m.GetF(b) : m.GetB != null ? m.GetB(a) == m.GetB(b) : string.Equals(m.GetS(a), m.GetS(b), StringComparison.Ordinal);

        public bool IsDefault(T v)
        {
            foreach (var m in rows) if (!Same(m, v, defaults)) return false;
            return v.Unknown == null || v.Unknown.IsEmpty;
        }

        public void Read(T v, DocReader r)
        {
            foreach (var m in rows)
            {
                if (m.GetF != null)
                {
                    float f = r.F32(m.Name, m.GetF(v));
                    if (!m.Range.Contains(f)) r.Report(FindingSeverity.Info, RagdollFindings.Range, m.Name, WetRange.F(f) + " is outside " + m.Range + "; kept, clamped when applied");
                    m.SetF(v, f);
                }
                else if (m.GetB != null) m.SetB(v, r.Bool(m.Name, m.GetB(v)));
                else m.SetS(v, r.EnumStr(m.Name, m.GetS(v), m.Known));
            }
        }

        public void Write(T v, CanonicalJsonWriter w)
        {
            foreach (var m in rows)
            {
                if (Same(m, v, defaults)) continue;
                if (m.GetF != null) w.F32(m.Name, m.GetF(v));
                else if (m.GetB != null) w.Bool(m.Name, m.GetB(v));
                else DocObject.ReqStr(w, m.Name, m.GetS(v));
            }
        }

        /// <summary>A copy of the known members as they are applied (numbers clamped).</summary>
        public T Applied(T v)
        {
            var c = new T();
            foreach (var m in rows)
            {
                if (m.GetF != null) m.SetF(c, m.Range.Clamp(m.GetF(v)));
                else if (m.GetB != null) m.SetB(c, m.GetB(v));
                else m.SetS(c, m.GetS(v));
            }
            return c;
        }

        public object Get(T v, string name)
        {
            var m = Find(name);
            if (m == null) return null;
            if (m.GetF != null) return m.GetF(v);
            if (m.GetB != null) return m.GetB(v);
            return m.GetS(v);
        }

        public object Default(string name) => Get(defaults, name);

        public WetRange? RangeOf(string name)
        {
            var m = Find(name);
            return m?.GetF != null ? m.Range : (WetRange?)null;
        }

        /// <summary>Sets a member by name from a JSON value; a number is not clamped. The first thing that stops it, or null.</summary>
        public string Set(T v, string name, JToken value)
        {
            var m = Find(name);
            if (m == null) return "no value " + name;
            var type = value?.Type ?? JTokenType.None;
            if (m.GetF != null)
            {
                if (type != JTokenType.Integer && type != JTokenType.Float) return name + " is a number";
                float f = value.Value<float>();
                if (float.IsNaN(f) || float.IsInfinity(f)) return name + " is not a finite number";
                m.SetF(v, f);
            }
            else if (m.GetB != null)
            {
                if (type != JTokenType.Boolean) return name + " is true or false";
                m.SetB(v, value.Value<bool>());
            }
            else
            {
                string s = type == JTokenType.String ? value.Value<string>() : null;
                if (Array.IndexOf(m.Known, s) < 0) return name + " is one of " + string.Join(", ", m.Known);
                m.SetS(v, s);
            }
            return null;
        }
    }

    /// <summary>The values of one body. A new object holds the defaults: the active body that follows its pose until it is hit.</summary>
    public sealed class RagdollBodyValues : DocObject
    {
        static readonly RagdollValueTable<RagdollBodyValues> table = new RagdollValueTable<RagdollBodyValues>()
            .F("muscle", RagdollRanges.Muscle, v => v.Muscle, (v, x) => v.Muscle = x)
            .F("muscleDamping", RagdollRanges.MuscleDamping, v => v.MuscleDamping, (v, x) => v.MuscleDamping = x)
            .F("pin", RagdollRanges.Unit, v => v.Pin, (v, x) => v.Pin = x)
            .F("pinFreq", RagdollRanges.PinFreq, v => v.PinFreq, (v, x) => v.PinFreq = x)
            .F("pinFalloff", RagdollRanges.PinFalloff, v => v.PinFalloff, (v, x) => v.PinFalloff = x)
            .B("pinHipsOnly", v => v.PinHipsOnly, (v, x) => v.PinHipsOnly = x)
            .B("gravityComp", v => v.GravityComp, (v, x) => v.GravityComp = x)
            .F("knockDistance", RagdollRanges.KnockDistance, v => v.KnockDistance, (v, x) => v.KnockDistance = x)
            .F("knockHit", RagdollRanges.KnockHit, v => v.KnockHit, (v, x) => v.KnockHit = x)
            .F("downMuscle", RagdollRanges.Unit, v => v.DownMuscle, (v, x) => v.DownMuscle = x)
            .F("minDown", RagdollRanges.MinDown, v => v.MinDown, (v, x) => v.MinDown = x)
            .F("getUp", RagdollRanges.GetUp, v => v.GetUp, (v, x) => v.GetUp = x)
            .B("relocate", v => v.Relocate, (v, x) => v.Relocate = x)
            .B("faceCamera", v => v.FaceCamera, (v, x) => v.FaceCamera = x)
            .B("legCollision", v => v.LegCollision, (v, x) => v.LegCollision = x)
            .F("mass", RagdollRanges.Mass, v => v.Mass, (v, x) => v.Mass = x);

        public float Muscle = 20f, MuscleDamping = 1f, Pin = 1f, PinFreq = 12f, PinFalloff = 3f;
        public bool PinHipsOnly;
        public bool GravityComp = true;
        public float KnockDistance = 3.5f, KnockHit = 8f, DownMuscle = 0.15f, MinDown = 1.5f, GetUp = 2.5f;
        public bool Relocate = true;
        public bool FaceCamera, LegCollision;
        public float Mass = 50f;

        /// <summary>The member names, in the order they are written.</summary>
        public static IReadOnlyList<string> Names => table.Names;
        /// <summary>The panel's range of number member <paramref name="name"/>; null for any other name.</summary>
        public static WetRange? RangeOf(string name) => table.RangeOf(name);
        /// <summary>The default of member <paramref name="name"/> (a float or a bool), or null.</summary>
        public static object DefaultOf(string name) => table.Default(name);

        /// <summary>True at every default with no unknown member: then the object is not written.</summary>
        public bool IsDefault => table.IsDefault(this);
        /// <summary>A copy of the known members as they are applied (clamped).</summary>
        public RagdollBodyValues Applied() => table.Applied(this);
        /// <summary>Member <paramref name="name"/> (a float or a bool), or null.</summary>
        public object Get(string name) => table.Get(this, name);
        /// <summary>Sets member <paramref name="name"/> from a JSON value, not clamped; returns what stops it, or null when it is set.</summary>
        public string Set(string name, JToken value) => table.Set(this, name, value);

        internal override void ReadMembers(DocReader r) => table.Read(this, r);
        internal override void WriteMembers(CanonicalJsonWriter w) => table.Write(this, w);
    }

    /// <summary>The values of the world: every body, prop and shot of the scene.</summary>
    public sealed class RagdollWorldValues : DocObject
    {
        public const string PropBall = "ball", PropBox = "box", PropHeavy = "heavy";

        static readonly string[] propKinds = { PropBall, PropBox, PropHeavy };

        static readonly RagdollValueTable<RagdollWorldValues> table = new RagdollValueTable<RagdollWorldValues>()
            .F("gravity", RagdollRanges.Gravity, v => v.Gravity, (v, x) => v.Gravity = x)
            .F("timeScale", RagdollRanges.Unit, v => v.TimeScale, (v, x) => v.TimeScale = x)
            .F("hz", RagdollRanges.Hz, v => v.Hz, (v, x) => v.Hz = x)
            .F("friction", RagdollRanges.Friction, v => v.Friction, (v, x) => v.Friction = x)
            .F("angularDrag", RagdollRanges.AngularDrag, v => v.AngularDrag, (v, x) => v.AngularDrag = x)
            .F("pushSpeed", RagdollRanges.PushSpeed, v => v.PushSpeed, (v, x) => v.PushSpeed = x)
            .F("grabStiffness", RagdollRanges.GrabStiffness, v => v.GrabStiffness, (v, x) => v.GrabStiffness = x)
            .F("grabMax", RagdollRanges.GrabMax, v => v.GrabMax, (v, x) => v.GrabMax = x)
            .F("followStiffness", RagdollRanges.FollowStiffness, v => v.FollowStiffness, (v, x) => v.FollowStiffness = x)
            .F("followMax", RagdollRanges.FollowMax, v => v.FollowMax, (v, x) => v.FollowMax = x)
            .F("blendOut", RagdollRanges.BlendOut, v => v.BlendOut, (v, x) => v.BlendOut = x)
            .F("transfer", RagdollRanges.Transfer, v => v.Transfer, (v, x) => v.Transfer = x)
            .F("maxPartDv", RagdollRanges.MaxPartDv, v => v.MaxPartDv, (v, x) => v.MaxPartDv = x)
            .F("hitWeaken", RagdollRanges.HitWeaken, v => v.HitWeaken, (v, x) => v.HitWeaken = x)
            .F("weakenRegain", RagdollRanges.WeakenRegain, v => v.WeakenRegain, (v, x) => v.WeakenRegain = x)
            .F("weakMuscle", RagdollRanges.Unit, v => v.WeakMuscle, (v, x) => v.WeakMuscle = x)
            .F("staggerAt", RagdollRanges.StaggerAt, v => v.StaggerAt, (v, x) => v.StaggerAt = x)
            .F("momentum", RagdollRanges.Momentum, v => v.Momentum, (v, x) => v.Momentum = x)
            .F("fireRate", RagdollRanges.FireRate, v => v.FireRate, (v, x) => v.FireRate = x)
            .S("propKind", propKinds, v => v.PropKind, (v, x) => v.PropKind = x)
            .F("propMass", RagdollRanges.PropMass, v => v.PropMass, (v, x) => v.PropMass = x)
            .F("propSpeed", RagdollRanges.PropSpeed, v => v.PropSpeed, (v, x) => v.PropSpeed = x)
            .F("propSize", RagdollRanges.PropSize, v => v.PropSize, (v, x) => v.PropSize = x);

        /// <summary>Units per second squared; 1 unit = 10 cm.</summary>
        public float Gravity = 98.1f;
        /// <summary>The world's time against the scene clock.</summary>
        public float TimeScale = 1f;
        /// <summary>Substeps per second of world time.</summary>
        public float Hz = 240f;
        public float Friction = 3f, AngularDrag = 0.5f, PushSpeed = 20f, GrabStiffness = 60f, GrabMax = 6f, FollowStiffness = 400f, FollowMax = 20f;
        /// <summary>Seconds of a release's blend.</summary>
        public float BlendOut = 0.3f;
        public float Transfer = 0.18f, MaxPartDv = 40f, HitWeaken = 20f, WeakenRegain = 1.93f, WeakMuscle = 0.27f, StaggerAt = 600f, Momentum = 181f, FireRate = 10f;
        /// <summary>An unknown string is kept and acts as <see cref="PropBall"/>.</summary>
        public string PropKind = PropBall;
        public float PropMass = 2f, PropSpeed = 80f, PropSize = 1.2f;

        /// <summary>The kinds of a prop, in the order the panel shows them.</summary>
        public static IReadOnlyList<string> PropKinds => propKinds;
        /// <summary>The member names, in the order they are written.</summary>
        public static IReadOnlyList<string> Names => table.Names;
        /// <summary>The panel's range of number member <paramref name="name"/>; null for any other name.</summary>
        public static WetRange? RangeOf(string name) => table.RangeOf(name);
        /// <summary>The default of member <paramref name="name"/> (a float or a string), or null.</summary>
        public static object DefaultOf(string name) => table.Default(name);

        /// <summary>True at every default with no unknown member: then the object is not written.</summary>
        public bool IsDefault => table.IsDefault(this);
        /// <summary>The prop kind as it acts.</summary>
        public string AppliedPropKind => Array.IndexOf(propKinds, PropKind) >= 0 ? PropKind : PropBall;
        /// <summary>A copy of the known members as they are applied (clamped; the prop kind as it acts).</summary>
        public RagdollWorldValues Applied()
        {
            var c = table.Applied(this);
            c.PropKind = AppliedPropKind;
            return c;
        }
        /// <summary>Member <paramref name="name"/> (a float or a string), or null.</summary>
        public object Get(string name) => table.Get(this, name);
        /// <summary>Sets member <paramref name="name"/> from a JSON value, not clamped; returns what stops it, or null when it is set.</summary>
        public string Set(string name, JToken value) => table.Set(this, name, value);

        internal override void ReadMembers(DocReader r) => table.Read(this, r);
        internal override void WriteMembers(CanonicalJsonWriter w) => table.Write(this, w);
    }

    /// <summary>The object <c>room</c>: the box a body cannot leave.</summary>
    public sealed class RagdollRoomValues : DocObject
    {
        /// <summary>The size of a room nobody sized.</summary>
        public static readonly Vec3 DefaultSize = new Vec3(40f, 30f, 40f);

        /// <summary>Its centre in x, z; null while nobody placed it: the first body's room stands at that character.</summary>
        public Vec2? Center;
        /// <summary>Width, height, depth of the inside.</summary>
        public Vec3 Size = DefaultSize;

        /// <summary>The size as it is applied (clamped).</summary>
        public Vec3 AppliedSize => new Vec3(RagdollRanges.RoomWidth.Clamp(Size.X), RagdollRanges.RoomHeight.Clamp(Size.Y), RagdollRanges.RoomWidth.Clamp(Size.Z));

        /// <summary>True with no centre, the default size and no unknown member: then the object is not written.</summary>
        public bool IsDefault => !Center.HasValue && Size.X == DefaultSize.X && Size.Y == DefaultSize.Y && Size.Z == DefaultSize.Z && (Unknown == null || Unknown.IsEmpty);

        internal override void ReadMembers(DocReader r)
        {
            Center = r.Has("center") ? r.V2("center", default) : (Vec2?)null;
            Size = r.V3("size", Size);
            if (!RagdollRanges.RoomWidth.Contains(Size.X) || !RagdollRanges.RoomHeight.Contains(Size.Y) || !RagdollRanges.RoomWidth.Contains(Size.Z))
                r.Report(FindingSeverity.Info, RagdollFindings.Range, "size", Size + " is outside " + RagdollRanges.RoomWidth + " by " + RagdollRanges.RoomHeight + " by " + RagdollRanges.RoomWidth + "; kept, clamped when applied");
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            if (Center.HasValue) w.V2("center", Center.Value);
            w.V3("size", Size);
        }
    }

    /// <summary>The object <c>floor</c>: what the room's floor stands on.</summary>
    public sealed class RagdollFloorValues : DocObject
    {
        public const string ModeMap = "map", ModeFeet = "feet", ModeFixed = "fixed";

        static readonly string[] modes = { ModeMap, ModeFeet, ModeFixed };

        /// <summary>The modes, in the order the panel shows them.</summary>
        public static IReadOnlyList<string> Modes => modes;

        /// <summary>An unknown string is kept and acts as <see cref="ModeMap"/>.</summary>
        public string Mode = ModeMap;
        /// <summary>Added to the floor in every mode; no range.</summary>
        public float Offset;
        /// <summary>The floor's height in <see cref="ModeFixed"/>: a world height, with no range.</summary>
        public float Height;

        /// <summary>The mode as it acts.</summary>
        public string AppliedMode => Array.IndexOf(modes, Mode) >= 0 ? Mode : ModeMap;

        /// <summary>True at every default with no unknown member: then the object is not written.</summary>
        public bool IsDefault => string.Equals(Mode, ModeMap, StringComparison.Ordinal) && Offset == 0f && Height == 0f && (Unknown == null || Unknown.IsEmpty);

        internal override void ReadMembers(DocReader r)
        {
            Mode = r.EnumStr("mode", Mode, modes);
            Offset = r.F32("offset", Offset);
            Height = r.F32("height", Height);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            ReqStr(w, "mode", Mode);
            w.F32("offset", Offset);
            w.F32("height", Height);
        }
    }

    /// <summary>One pin of a body: a part held at a point in the world, or following a node or a bone of one. A following pin has <see cref="Follow"/>, optionally
    /// <see cref="Bone"/>, and <see cref="Offset"/>, and no <see cref="At"/>.</summary>
    public sealed class RagdollPin : DocObject
    {
        /// <summary>Unique in the block.</summary>
        public string Id = "";
        /// <summary>The part's key.</summary>
        public string Part = "";
        /// <summary>The held point, in the part's space.</summary>
        public Vec3 Point = Vec3.Zero;
        /// <summary>The anchor in world space; null on a following pin, and on a pin that stays where it is.</summary>
        public Vec3? At;
        /// <summary>The id of the node it follows; empty on a plain pin.</summary>
        public string Follow = "";
        /// <summary>The bone address of that node it follows; empty for the node itself.</summary>
        public string Bone = "";
        /// <summary>The anchor in the space of what it follows.</summary>
        public Vec3? Offset;

        /// <summary>True when it follows a node or a bone.</summary>
        public bool IsFollowing => !string.IsNullOrEmpty(Follow);

        internal override void ReadMembers(DocReader r)
        {
            Id = r.Str("id", Id);
            Part = r.Str("part", Part);
            Point = r.V3("point", Point);
            At = r.Has("at") ? r.V3("at", default) : (Vec3?)null;
            Follow = r.Has("follow") ? r.Id("follow") : "";
            Bone = r.Str("bone", Bone);
            Offset = r.Has("offset") ? r.V3("offset", default) : (Vec3?)null;
            RagdollRead.Part(r, "part", Part);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            ReqStr(w, "id", Id);
            ReqStr(w, "part", Part);
            w.V3("point", Point);
            if (At.HasValue) w.V3("at", At.Value);
            if (IsFollowing) ReqId(w, "follow", Follow);
            if (!string.IsNullOrEmpty(Bone)) w.Str("bone", Bone);
            if (Offset.HasValue) w.V3("offset", Offset.Value);
        }
    }

    /// <summary>Two parts of one body tied where they are.</summary>
    public sealed class RagdollTie : DocObject
    {
        public string A = "", B = "";

        internal override void ReadMembers(DocReader r)
        {
            A = r.Str("a", A);
            B = r.Str("b", B);
            RagdollRead.Part(r, "a", A);
            RagdollRead.Part(r, "b", B);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            ReqStr(w, "a", A);
            ReqStr(w, "b", B);
        }
    }

    /// <summary>A part holding a rotation relative to its parent.</summary>
    public sealed class RagdollHold : DocObject
    {
        public string Part = "";
        /// <summary>Relative to the parent part.</summary>
        public Quat4 Rot = Quat4.Identity;

        internal override void ReadMembers(DocReader r)
        {
            Part = r.Str("part", Part);
            Rot = r.Quat("rot", Rot);
            RagdollRead.Part(r, "part", Part);
            RagdollRead.Rotation(r, "rot", Rot);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            ReqStr(w, "part", Part);
            w.Quat("rot", Rot);
        }
    }

    /// <summary>The last settled pose of a body: rotations and the pelvis origin, in the space of the character's holder. A part's position comes from its parent's joint, so a
    /// pose survives a body shape edit.</summary>
    public sealed class RagdollPose : DocObject
    {
        /// <summary>The pelvis origin.</summary>
        public Vec3 Hips = Vec3.Zero;
        /// <summary>The rotations by part key. Written in part order, then any other key.</summary>
        public SortedDictionary<string, Quat4> Parts = NodeState.NewMap<Quat4>();

        /// <summary>True with no rotation and no unknown member: then the object is not written.</summary>
        public bool IsEmpty => Parts.Count == 0 && (Unknown == null || Unknown.IsEmpty);

        /// <summary>The rotations that act, in part order: known parts, of some length.</summary>
        public IEnumerable<KeyValuePair<string, Quat4>> AppliedParts()
        {
            foreach (var key in RagdollParts.Keys)
                if (Parts.TryGetValue(key, out var q) && RagdollParts.IsRotation(q)) yield return new KeyValuePair<string, Quat4>(key, q);
        }

        internal override void ReadMembers(DocReader r)
        {
            Hips = r.V3("hips", Hips);
            Parts = r.Map("parts", v => v.AsQuat());
            foreach (var kv in Parts)
            {
                RagdollRead.Part(r, "parts", kv.Key);
                RagdollRead.Rotation(r, "parts", kv.Value);
            }
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.V3("hips", Hips);
            w.Name("parts");
            w.StartObject();
            foreach (var key in RagdollParts.Keys)
                if (Parts.TryGetValue(key, out var q)) w.Quat(key, q);
            foreach (var kv in Parts)
                if (!RagdollParts.IsKnown(kv.Key)) w.Quat(kv.Key, kv.Value);
            w.EndObject();
        }
    }

    /// <summary>The block <c>chara.ragdoll</c>: scene-only values of one character. Empty by default and then not written.</summary>
    public sealed class CharaRagdoll : DocObject
    {
        /// <summary>The member name in the section <c>chara</c>.</summary>
        public const string MemberName = "ragdoll";

        /// <summary>A body now; false with other members: its settings are kept.</summary>
        public bool On;
        /// <summary>It stays limp.</summary>
        public bool Limp;
        public RagdollBodyValues Values = new RagdollBodyValues();
        public List<RagdollPin> Pins = new List<RagdollPin>();
        public List<RagdollTie> Ties = new List<RagdollTie>();
        public List<RagdollHold> Holds = new List<RagdollHold>();
        /// <summary>The last settled pose; a body that never settled has an empty one.</summary>
        public RagdollPose Pose = new RagdollPose();

        /// <summary>True when the block holds nothing and no unknown member at any level of its own: it is then not written, and no revision is raised.</summary>
        public bool IsEmpty => !On && !Limp && Values.IsDefault && Pins.Count == 0 && Ties.Count == 0 && Holds.Count == 0 && Pose.IsEmpty && (Unknown == null || Unknown.IsEmpty);

        /// <summary>The pin with <paramref name="id"/>, or null.</summary>
        public RagdollPin Find(string id)
        {
            foreach (var p in Pins) if (string.Equals(p.Id, id, StringComparison.Ordinal)) return p;
            return null;
        }

        /// <summary>The hold of part <paramref name="part"/>, or null.</summary>
        public RagdollHold HoldOf(string part)
        {
            foreach (var h in Holds) if (string.Equals(h.Part, part, StringComparison.Ordinal)) return h;
            return null;
        }

        /// <summary>The first id of the form <c>a</c>, <c>b</c>, … no pin has.</summary>
        public string NextPinId() => RagdollRead.NextId(id => Find(id) != null);

        /// <summary>The pins that act: of known parts, among the first <see cref="RagdollLimits.MaxPins"/>.</summary>
        public IEnumerable<RagdollPin> AppliedPins()
        {
            int n = 0;
            foreach (var p in Pins)
            {
                if (n++ >= RagdollLimits.MaxPins) yield break;
                if (RagdollParts.IsKnown(p.Part)) yield return p;
            }
        }

        /// <summary>The ties that act: between two different known parts, among the first <see cref="RagdollLimits.MaxTies"/>.</summary>
        public IEnumerable<RagdollTie> AppliedTies()
        {
            int n = 0;
            foreach (var t in Ties)
            {
                if (n++ >= RagdollLimits.MaxTies) yield break;
                if (RagdollParts.IsKnown(t.A) && RagdollParts.IsKnown(t.B) && !string.Equals(t.A, t.B, StringComparison.Ordinal)) yield return t;
            }
        }

        /// <summary>The holds that act: the first of each known part, of some length.</summary>
        public IEnumerable<RagdollHold> AppliedHolds()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var h in Holds)
                if (RagdollParts.IsKnown(h.Part) && seen.Add(h.Part) && RagdollParts.IsRotation(h.Rot)) yield return h;
        }

        /// <summary>The first rule the ids break, or null: a pin without an id, an id used twice. A block read from text has none of these, since the reader gives such a
        /// pin a derived id; an edit must check the block it was given.</summary>
        public string IdProblem()
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in Pins)
            {
                if (string.IsNullOrEmpty(p.Id)) return "a pin without an id";
                if (!ids.Add(p.Id)) return "the pin id " + p.Id + " is used twice";
            }
            return null;
        }

        /// <summary>The first limit this block breaks that <paramref name="before"/> did not, or null. What a file brought over a limit may stay or shrink; an edit may not make
        /// it grow. The limit of the whole scene is the edit command's.</summary>
        public string Problem(CharaRagdoll before = null)
        {
            if (Pins.Count > RagdollLimits.MaxPins && Pins.Count > (before?.Pins.Count ?? 0)) return RagdollFindings.Limit + ": more than " + RagdollLimits.MaxPins + " pins";
            if (Ties.Count > RagdollLimits.MaxTies && Ties.Count > (before?.Ties.Count ?? 0)) return RagdollFindings.Limit + ": more than " + RagdollLimits.MaxTies + " ties";
            string twice = SecondHold(Holds, out int repeats);
            if (twice != null)
            {
                int had = 0;
                if (before != null) SecondHold(before.Holds, out had);
                if (repeats > had) return RagdollFindings.Limit + ": a second hold on " + twice;
            }
            return null;
        }

        // The part of the first hold that repeats an earlier one's, or null; how many do.
        static string SecondHold(List<RagdollHold> holds, out int repeats)
        {
            string first = null;
            repeats = 0;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var h in holds)
            {
                if (seen.Add(h.Part)) continue;
                repeats++;
                first = first ?? h.Part;
            }
            return first;
        }

        /// <summary>What a release leaves: no body, no pin, no tie, no hold, no pose; the values stay, so a character at the defaults goes back to no member.</summary>
        public void Release()
        {
            On = false;
            Limp = false;
            Pins.Clear();
            Ties.Clear();
            Holds.Clear();
            Pose = new RagdollPose();
        }

        /// <summary>Makes this block, already a copy, what a copied node holds: no body, so two bodies never start in one place; the rest stays, to be started by hand.</summary>
        public void PrepareCopy()
        {
            On = false;
            Limp = false;
        }

        /// <summary>Follows ids through <paramref name="map"/> (old to new): a following pin on a node the map does not hold becomes a plain pin that stays where it is.</summary>
        public void RemapFollows(IReadOnlyDictionary<string, string> map)
        {
            foreach (var p in Pins)
            {
                if (!p.IsFollowing) continue;
                if (map != null && map.TryGetValue(p.Follow, out var to)) { p.Follow = to; continue; }
                p.Follow = "";
                p.Bone = "";
                p.Offset = null;
            }
        }

        internal override void ReadMembers(DocReader r)
        {
            On = r.Bool("on", On);
            Limp = r.Bool("limp", Limp);
            Values.ReadFrom(r.Obj("values"));
            var items = r.ObjArray("pins");
            Pins = new List<RagdollPin>(items.Count);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < items.Count; i++)
            {
                var p = ReadNew<RagdollPin>(items[i]);
                p.Id = RagdollRead.UniqueId(p.Id, ids, "ragdoll/pin/", i, items[i], "pin");
                Pins.Add(p);
            }
            Ties = ReadList<RagdollTie>(r, "ties");
            Holds = ReadList<RagdollHold>(r, "holds");
            Pose.ReadFrom(r.Obj("pose"));
            RagdollRead.Over(r, "pins", Pins.Count, RagdollLimits.MaxPins, "pins");
            RagdollRead.Over(r, "ties", Ties.Count, RagdollLimits.MaxTies, "ties");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var h in Holds)
                if (!seen.Add(h.Part)) r.Report(FindingSeverity.Warning, RagdollFindings.Limit, "holds", "a second hold on " + h.Part + " is kept and not applied");
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Bool("on", On);
            w.Bool("limp", Limp);
            if (!Values.IsDefault) Values.WriteTo(w, "values");
            if (Pins.Count > 0) WriteList(w, "pins", Pins);
            if (Ties.Count > 0) WriteList(w, "ties", Ties);
            if (Holds.Count > 0) WriteList(w, "holds", Holds);
            if (!Pose.IsEmpty) Pose.WriteTo(w, "pose");
        }

        // ---------- text and copies ----------

        /// <summary>Reads a block from JSON text (one object) with the reader rules; findings go to <paramref name="findings"/>. A syntax or type error throws
        /// <see cref="SaveFormatException"/> (BadDocument; NonFinite for a number that is not finite).</summary>
        public static CharaRagdoll Parse(string json, FindingList findings = null, string entry = MemberName, bool lenient = false) =>
            FromToken(DocReader.ParseText(json ?? "", entry), findings, entry, lenient);

        /// <summary>As <see cref="Parse"/> for a parsed token (an object; anything else throws BadDocument). The token is not changed.</summary>
        public static CharaRagdoll FromToken(JToken token, FindingList findings = null, string entry = MemberName, bool lenient = false)
        {
            if (!(token is JObject o))
                throw new SaveFormatException(SaveErrorCode.BadDocument, "a ragdoll block is an object, found " + (token == null ? "nothing" : DocReader.Describe(token)), entry, token?.Path ?? "");
            var block = new CharaRagdoll();
            block.ReadFrom(new DocReader(o, new DocReadContext(entry, lenient, findings ?? new FindingList())));
            return block;
        }

        /// <summary>The canonical JSON bytes of the block (UTF-8, trailing LF), as scene.json writes it.</summary>
        public byte[] ToBytes() => CanonicalJsonWriter.ToDocumentBytes(MemberName, WriteTo);

        /// <summary>The canonical JSON text of the block (no trailing LF).</summary>
        public string ToJson()
        {
            var b = ToBytes();
            return Encoding.UTF8.GetString(b, 0, b.Length - 1);
        }

        /// <summary>The block as a JSON object.</summary>
        public JObject ToJObject() => DocReader.Parse(ToBytes(), MemberName);

        /// <summary>A deep copy (a canonical write and read: unknown members kept, nothing shared).</summary>
        public CharaRagdoll Clone() => Parse(ToJson());
    }

    /// <summary>A kinematic capsule from one bone of a character to another, with or without a body.</summary>
    public sealed class RagdollPusher : DocObject
    {
        public const string DefaultA = "cf_J_LegLow01_R", DefaultB = "cf_J_Toes01_R";
        public const float DefaultRadius = 0.45f;

        /// <summary>Unique in the block.</summary>
        public string Id = "";
        /// <summary>The character's node.</summary>
        public string Key;
        /// <summary>The bones at its ends.</summary>
        public string A = DefaultA, B = DefaultB;
        public float Radius = DefaultRadius;

        /// <summary>The radius as it is applied (not below 0).</summary>
        public float AppliedRadius => RagdollRanges.Radius.Clamp(Radius);

        internal override void ReadMembers(DocReader r)
        {
            Id = r.Str("id", Id);
            Key = r.Id("key");
            A = r.Str("a", A);
            B = r.Str("b", B);
            Radius = r.F32("radius", Radius);
            if (!RagdollRanges.Radius.Contains(Radius)) r.Report(FindingSeverity.Info, RagdollFindings.Range, "radius", WetRange.F(Radius) + " is below 0; kept, clamped when applied");
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            ReqStr(w, "id", Id);
            ReqId(w, "key", Key);
            ReqStr(w, "a", A);
            ReqStr(w, "b", B);
            w.F32("radius", Radius);
        }
    }

    /// <summary>The top-level block <c>ragdoll</c> of a scene. Empty at all defaults with no pusher and no obstacle, and then not written.</summary>
    public sealed class SceneRagdollWorld : DocObject
    {
        /// <summary>The member name at the top of scene.json.</summary>
        public const string MemberName = "ragdoll";

        public RagdollWorldValues Values = new RagdollWorldValues();
        public RagdollRoomValues Room = new RagdollRoomValues();
        public RagdollFloorValues Floor = new RagdollFloorValues();
        public List<RagdollPusher> Pushers = new List<RagdollPusher>();
        /// <summary>The node ids of the items that take part as static colliders.</summary>
        public List<string> Obstacles = new List<string>();

        /// <summary>True at all defaults with no pusher, no obstacle and no unknown member at any level of its own: it is then not written, and no revision is raised.</summary>
        public bool IsEmpty => Values.IsDefault && Room.IsDefault && Floor.IsDefault && Pushers.Count == 0 && Obstacles.Count == 0 && (Unknown == null || Unknown.IsEmpty);

        /// <summary>The pusher with <paramref name="id"/>, or null.</summary>
        public RagdollPusher Find(string id)
        {
            foreach (var p in Pushers) if (string.Equals(p.Id, id, StringComparison.Ordinal)) return p;
            return null;
        }

        /// <summary>The first id of the form <c>a</c>, <c>b</c>, … no pusher has.</summary>
        public string NextPusherId() => RagdollRead.NextId(id => Find(id) != null);

        /// <summary>The pushers that act: the first <see cref="RagdollLimits.MaxPushers"/>.</summary>
        public IEnumerable<RagdollPusher> AppliedPushers()
        {
            int n = 0;
            foreach (var p in Pushers)
            {
                if (n++ >= RagdollLimits.MaxPushers) yield break;
                yield return p;
            }
        }

        /// <summary>The obstacles that act: the first <see cref="RagdollLimits.MaxObstacles"/>, each node once.</summary>
        public IEnumerable<string> AppliedObstacles()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            int n = 0;
            foreach (var o in Obstacles)
            {
                if (n++ >= RagdollLimits.MaxObstacles) yield break;
                if (seen.Add(o)) yield return o;
            }
        }

        /// <summary>The first rule the ids break, or null: a pusher without an id, an id used twice. An edit must check the block it was given.</summary>
        public string IdProblem()
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in Pushers)
            {
                if (string.IsNullOrEmpty(p.Id)) return "a pusher without an id";
                if (!ids.Add(p.Id)) return "the pusher id " + p.Id + " is used twice";
            }
            return null;
        }

        /// <summary>The first limit this block breaks that <paramref name="before"/> did not, or null. What a file brought over a limit may stay or shrink; an edit may not make it grow.</summary>
        public string Problem(SceneRagdollWorld before = null)
        {
            if (Pushers.Count > RagdollLimits.MaxPushers && Pushers.Count > (before?.Pushers.Count ?? 0)) return RagdollFindings.Limit + ": more than " + RagdollLimits.MaxPushers + " pushers";
            if (Obstacles.Count > RagdollLimits.MaxObstacles && Obstacles.Count > (before?.Obstacles.Count ?? 0)) return RagdollFindings.Limit + ": more than " + RagdollLimits.MaxObstacles + " obstacles";
            return null;
        }

        /// <summary>
        /// The block of a scene that takes in nodes from another one: <paramref name="before"/> (null: the defaults), with the pushers and the obstacles of
        /// <paramref name="from"/> whose node <paramref name="map"/> (old id to new) holds, under the new ids; its values, room and floor stay.
        /// What a limit has no room for stays out and is counted in <paramref name="leftOut"/>. A new object, null when it holds nothing.
        /// </summary>
        public static SceneRagdollWorld Carried(SceneRagdollWorld before, SceneRagdollWorld from, IReadOnlyDictionary<string, string> map, out int leftOut)
        {
            leftOut = 0;
            var b = before?.Clone() ?? new SceneRagdollWorld();
            if (from != null && map != null)
            {
                foreach (var p in from.Pushers)
                {
                    if (!map.TryGetValue(p.Key, out var key)) continue;
                    if (b.Pushers.Count >= RagdollLimits.MaxPushers) { leftOut++; continue; }
                    b.Pushers.Add(new RagdollPusher { Id = b.NextPusherId(), Key = key, A = p.A, B = p.B, Radius = p.Radius, Unknown = p.Unknown });
                }
                foreach (var o in from.Obstacles)
                {
                    if (!map.TryGetValue(o, out var id) || b.Obstacles.Contains(id)) continue;
                    if (b.Obstacles.Count >= RagdollLimits.MaxObstacles) { leftOut++; continue; }
                    b.Obstacles.Add(id);
                }
            }
            return b.IsEmpty ? null : b;
        }

        internal override void ReadMembers(DocReader r)
        {
            Values.ReadFrom(r.Obj("values"));
            Room.ReadFrom(r.Obj("room"));
            Floor.ReadFrom(r.Obj("floor"));
            var items = r.ObjArray("pushers");
            Pushers = new List<RagdollPusher>(items.Count);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < items.Count; i++)
            {
                var p = ReadNew<RagdollPusher>(items[i]);
                p.Id = RagdollRead.UniqueId(p.Id, ids, "ragdoll/pusher/", i, items[i], "pusher");
                Pushers.Add(p);
            }
            Obstacles = r.Array("obstacles", v => v.AsId());
            RagdollRead.Over(r, "pushers", Pushers.Count, RagdollLimits.MaxPushers, "pushers");
            RagdollRead.Over(r, "obstacles", Obstacles.Count, RagdollLimits.MaxObstacles, "obstacles");
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            if (!Values.IsDefault) Values.WriteTo(w, "values");
            if (!Room.IsDefault) Room.WriteTo(w, "room");
            if (!Floor.IsDefault) Floor.WriteTo(w, "floor");
            if (Pushers.Count > 0) WriteList(w, "pushers", Pushers);
            if (Obstacles.Count == 0) return;
            w.Name("obstacles");
            string at = w.Path;
            w.StartArray();
            for (int i = 0; i < Obstacles.Count; i++)
            {
                if (Obstacles[i] == null) throw NullElement(w, at, i);
                w.Id(Obstacles[i]);
            }
            w.EndArray();
        }

        // ---------- text and copies ----------

        /// <summary>Reads the member of the scene document's root; null when absent or empty, so the model never holds an empty block.</summary>
        internal static SceneRagdollWorld Read(DocReader root)
        {
            var b = new SceneRagdollWorld();
            b.ReadFrom(root.Obj(MemberName));
            return b.IsEmpty ? null : b;
        }

        /// <summary>Reads a block from JSON text (one object) with the reader rules; findings go to <paramref name="findings"/>. A syntax or type error throws
        /// <see cref="SaveFormatException"/>. Never null.</summary>
        public static SceneRagdollWorld Parse(string json, FindingList findings = null, string entry = MemberName, bool lenient = false) =>
            FromToken(DocReader.ParseText(json ?? "", entry), findings, entry, lenient);

        /// <summary>As <see cref="Parse"/> for a parsed token (an object; anything else throws BadDocument). The token is not changed.</summary>
        public static SceneRagdollWorld FromToken(JToken token, FindingList findings = null, string entry = MemberName, bool lenient = false)
        {
            if (!(token is JObject o))
                throw new SaveFormatException(SaveErrorCode.BadDocument, "a ragdoll block is an object, found " + (token == null ? "nothing" : DocReader.Describe(token)), entry, token?.Path ?? "");
            var block = new SceneRagdollWorld();
            block.ReadFrom(new DocReader(o, new DocReadContext(entry, lenient, findings ?? new FindingList())));
            return block;
        }

        /// <summary>The canonical JSON bytes of the block (UTF-8, trailing LF), as scene.json writes it.</summary>
        public byte[] ToBytes() => CanonicalJsonWriter.ToDocumentBytes(MemberName, WriteTo);

        /// <summary>The canonical JSON text of the block (no trailing LF).</summary>
        public string ToJson()
        {
            var b = ToBytes();
            return Encoding.UTF8.GetString(b, 0, b.Length - 1);
        }

        /// <summary>The block as a JSON object.</summary>
        public JObject ToJObject() => DocReader.Parse(ToBytes(), MemberName);

        /// <summary>A deep copy (a canonical write and read: unknown members kept, nothing shared).</summary>
        public SceneRagdollWorld Clone() => Parse(ToJson());
    }

    static class RagdollRead
    {
        public static void Part(DocReader r, string member, string key)
        {
            if (!RagdollParts.IsKnown(key))
                r.Report(FindingSeverity.Warning, RagdollFindings.BadPart, member, (key.Length == 0 ? "no part" : "\"" + key + "\" is no part of a body") + "; kept, inactive");
        }

        public static void Rotation(DocReader r, string member, Quat4 q)
        {
            if (!RagdollParts.IsRotation(q)) r.Report(FindingSeverity.Info, RagdollFindings.Range, member, "a rotation of no length; kept, not applied");
        }

        public static void Over(DocReader r, string member, int count, int limit, string what)
        {
            if (count > limit)
                r.Report(FindingSeverity.Warning, RagdollFindings.Limit, member, (count - limit) + " " + what + " over the limit of " + limit + " are kept and not applied");
        }

        /// <summary><paramref name="id"/>, or a derived id when it is empty or <paramref name="ids"/> holds it, reported; the result joins <paramref name="ids"/>.</summary>
        public static string UniqueId(string id, HashSet<string> ids, string prefix, int index, DocReader item, string what)
        {
            if (id.Length > 0 && ids.Add(id)) return id;
            string at = prefix + index.ToString(CultureInfo.InvariantCulture);
            string derived = JsonText.DerivedId(at);
            for (int n = 1; !ids.Add(derived); n++) derived = JsonText.DerivedId(at + "/" + n.ToString(CultureInfo.InvariantCulture));
            item.Report(FindingSeverity.Info, RagdollFindings.Id, "id", (id.Length == 0 ? "a " + what + " without an id" : "the " + what + " id " + id + " repeats an earlier one's") + "; derived id " + derived + " used");
            return derived;
        }

        /// <summary>The first of <c>a</c> … <c>z</c>, <c>aa</c>, <c>ab</c>, … that is not <paramref name="taken"/>.</summary>
        public static string NextId(Func<string, bool> taken)
        {
            for (int n = 1; ; n++)
            {
                var s = "";
                for (int k = n; k > 0; k = (k - 1) / 26) s = (char)('a' + (k - 1) % 26) + s;
                if (!taken(s)) return s;
            }
        }
    }
}
