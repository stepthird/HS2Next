// One slot of the runtime override layer: an owner's value for one channel (a node id or none, a path), with a priority
// and the order of its acquisition. The host hands this object to plugins as their IOverrideSlot; the core Timeline uses
// the typed setters below. Values are kept in double: Number and Integer 1, Vector3 3, Rotation 4 (a normalised
// quaternion x, y, z, w), Color 4 (r, g, b, a as stored), Camera 9 (target x, y, z, rotation quaternion x, y, z, w,
// distance, field of view), Animation 3 (group, category, number) plus its source, DepthOfField an OverrideDof.
// A setter whose value is equal bit for bit to the current one changes nothing; every setter returns false once the slot
// is released. Setters run on the layer's thread (InvalidOperationException otherwise); the read members answer anywhere.
using System;
using System.Collections.Generic;
using HS2Next.Api.Plugins;
using Newtonsoft.Json.Linq;

namespace HS2Next.Scenes.Overrides
{
    public sealed class OverrideSlot : IOverrideSlot
    {
        readonly OverrideLayer layer;
        readonly double[] values = new double[9];
        string source;
        OverrideDof dof;
        int priority;
        bool hasValue;
        volatile bool released;

        internal OverrideSlot(OverrideLayer layer, OverrideChannelState channel, string owner, int priority, long acquisition)
        {
            this.layer = layer;
            Channel = channel;
            Owner = owner;
            this.priority = priority;
            Acquisition = acquisition;
        }

        /// <summary>The channel this slot belongs to.</summary>
        public OverrideChannelState Channel { get; }
        /// <summary>"timeline" for the core Timeline, a plugin's GUID, or another core owner.</summary>
        public string Owner { get; }
        /// <summary>The order of acquisition (among equal priorities the higher wins).</summary>
        public long Acquisition { get; }
        public OverridePath ParsedPath => Channel.Path;

        // ------------------------------------------------------------ IOverrideSlot: read members (any thread)

        public string NodeId => Channel.NodeId;
        public string Path => Channel.Path.Text;
        public OverrideValueKind Kind => Channel.Path.Kind;
        public int Priority => priority;
        public bool IsReleased => released;
        public bool HasValue => hasValue && !released;
        public bool IsWinning => !released && Channel.Winner == this;
        public bool TargetExists => layer.TargetExists(Channel.NodeId);

        /// <summary>True when the slot wins and its value takes effect now (the host decides for FK bones and foci).</summary>
        public bool IsEffective => IsWinning && layer.IsEffective(this);

        // ------------------------------------------------------------ the value (read)

        /// <summary>The number of doubles of the value (0 without a value).</summary>
        public int ValueCount => hasValue ? CountOf(Kind) : 0;
        /// <summary>Value i (see the file header for the layout).</summary>
        public double this[int i] => values[i];
        /// <summary>The animation row's source (Animation), null when the row is given without one.</summary>
        public string AnimeSource => source;
        /// <summary>The depth-of-field value (DepthOfField), else null.</summary>
        public OverrideDof Dof => hasValue ? dof : null;

        static int CountOf(OverrideValueKind k)
        {
            switch (k)
            {
                case OverrideValueKind.Number:
                case OverrideValueKind.Integer: return 1;
                case OverrideValueKind.Vector3:
                case OverrideValueKind.Animation: return 3;
                case OverrideValueKind.Rotation:
                case OverrideValueKind.Color: return 4;
                case OverrideValueKind.Camera: return 9;
                default: return 0;
            }
        }

        /// <summary>The value as JSON (overrides.list, logs): the forms of the setters, a rotation as a quaternion [x, y, z, w].</summary>
        public string ValueJson()
        {
            if (!hasValue) return "null";
            var c = System.Globalization.CultureInfo.InvariantCulture;
            string N(int i) => values[i].ToString("R", c);
            switch (Kind)
            {
                case OverrideValueKind.Number:
                case OverrideValueKind.Integer: return N(0);
                case OverrideValueKind.Vector3: return "[" + N(0) + "," + N(1) + "," + N(2) + "]";
                case OverrideValueKind.Rotation:
                case OverrideValueKind.Color: return "[" + N(0) + "," + N(1) + "," + N(2) + "," + N(3) + "]";
                case OverrideValueKind.Camera:
                    return "{\"target\":[" + N(0) + "," + N(1) + "," + N(2) + "],\"rotation\":[" + N(3) + "," + N(4) + "," + N(5) + "," + N(6) + "],\"dist\":" + N(7) + ",\"fov\":" + N(8) + "}";
                case OverrideValueKind.Animation:
                    return "{" + (source != null ? "\"source\":" + Newtonsoft.Json.JsonConvert.ToString(source) + "," : "") + "\"group\":" + N(0) + ",\"category\":" + N(1) + ",\"no\":" + N(2) + "}";
                case OverrideValueKind.DepthOfField: return dof?.ToString() ?? "null";
                default: return "null";
            }
        }

        // ------------------------------------------------------------ IOverrideSlot: setters

        public bool Set(double value)
        {
            if (!Begin()) return false;
            switch (Kind)
            {
                case OverrideValueKind.Number: return Store1(Number(value));
                case OverrideValueKind.Integer: return Store1(Integer(value));
                default: throw Form("Set(value)");
            }
        }

        public bool Set(double x, double y, double z)
        {
            if (!Begin()) return false;
            switch (Kind)
            {
                case OverrideValueKind.Vector3: return SetVectorCore(x, y, z);
                case OverrideValueKind.Rotation: return SetEulerCore(x, y, z);
                case OverrideValueKind.Color: return SetColorCore(x, y, z, 1);
                case OverrideValueKind.Animation: return SetAnimationCore(null, Whole(x, "group"), Whole(y, "category"), Whole(z, "no"));
                default: throw Form("Set(x, y, z)");
            }
        }

        public bool Set(double x, double y, double z, double w)
        {
            if (!Begin()) return false;
            switch (Kind)
            {
                case OverrideValueKind.Rotation: return SetQuaternionCore(x, y, z, w);
                case OverrideValueKind.Color: return SetColorCore(x, y, z, w);
                default: throw Form("Set(x, y, z, w)");
            }
        }

        public bool Set(double[] v)
        {
            if (v == null) throw new ArgumentNullException(nameof(v));
            if (!Begin()) return false;
            switch (Kind)
            {
                case OverrideValueKind.Number when v.Length == 1: return Store1(Number(v[0]));
                case OverrideValueKind.Integer when v.Length == 1: return Store1(Integer(v[0]));
                case OverrideValueKind.Vector3 when v.Length == 3: return SetVectorCore(v[0], v[1], v[2]);
                case OverrideValueKind.Rotation when v.Length == 3: return SetEulerCore(v[0], v[1], v[2]);
                case OverrideValueKind.Rotation when v.Length == 4: return SetQuaternionCore(v[0], v[1], v[2], v[3]);
                case OverrideValueKind.Color when v.Length == 3: return SetColorCore(v[0], v[1], v[2], 1);
                case OverrideValueKind.Color when v.Length == 4: return SetColorCore(v[0], v[1], v[2], v[3]);
                case OverrideValueKind.Animation when v.Length == 3: return SetAnimationCore(null, Whole(v[0], "group"), Whole(v[1], "category"), Whole(v[2], "no"));
                case OverrideValueKind.Camera when v.Length == 8:
                {
                    Bits.Euler(Finite(v[3]), Finite(v[4]), Finite(v[5]), out double qx, out double qy, out double qz, out double qw);
                    return SetCameraCore(Finite(v[0]), Finite(v[1]), Finite(v[2]), qx, qy, qz, qw, Finite(v[6]), Finite(v[7]));
                }
                case OverrideValueKind.DepthOfField when v.Length == 7:
                    return SetDofCore(new OverrideDof(Finite(v[0]) != 0, Finite(v[1]), Finite(v[2]), Finite(v[3]), OverrideFocus.OfPoint(Finite(v[4]), Finite(v[5]), Finite(v[6]))));
                default: throw Form(v.Length + " numbers");
            }
        }

        public bool SetJson(string json)
        {
            if (!Begin()) return false;
            JToken t;
            try { t = JToken.Parse(json ?? "null"); }
            catch (Exception e) { throw new ArgumentException("path " + Path + ": the value is not JSON (" + e.Message + "); expected " + OverridePath.FormsOf(Kind), "json"); }
            switch (Kind)
            {
                case OverrideValueKind.Number: return Store1(Number(JNum(t, "value")));
                case OverrideValueKind.Integer: return Store1(Integer(JNum(t, "value")));
                case OverrideValueKind.Vector3: { var a = JNums(t, 3, 3, "value"); return SetVectorCore(a[0], a[1], a[2]); }
                case OverrideValueKind.Rotation:
                {
                    var a = JNums(t, 3, 4, "value");
                    return a.Length == 3 ? SetEulerCore(a[0], a[1], a[2]) : SetQuaternionCore(a[0], a[1], a[2], a[3]);
                }
                case OverrideValueKind.Color: { var a = JNums(t, 3, 4, "value"); return SetColorCore(a[0], a[1], a[2], a.Length == 4 ? a[3] : 1); }
                case OverrideValueKind.Animation:
                {
                    var o = JObj(t, "value", "source", "group", "category", "no");
                    var st = o["source"];
                    string src = st == null || st.Type == JTokenType.Null ? null : st.Type == JTokenType.String ? (string)st : throw Form("a source string");
                    return SetAnimationCore(src, Whole(JNum(Req(o, "group"), "group"), "group"), Whole(JNum(Req(o, "category"), "category"), "category"), Whole(JNum(Req(o, "no"), "no"), "no"));
                }
                case OverrideValueKind.Camera:
                {
                    var o = JObj(t, "value", "target", "rot", "dist", "fov");
                    var tg = JNums(Req(o, "target"), 3, 3, "target");
                    var r = JNums(Req(o, "rot"), 3, 3, "rot");
                    Bits.Euler(r[0], r[1], r[2], out double qx, out double qy, out double qz, out double qw);
                    return SetCameraCore(tg[0], tg[1], tg[2], qx, qy, qz, qw, JNum(Req(o, "dist"), "dist"), JNum(Req(o, "fov"), "fov"));
                }
                case OverrideValueKind.DepthOfField:
                {
                    var o = JObj(t, "value", "enabled", "focus", "focalSize", "aperture", "blur", "focusTo", "focusBlend");
                    var en = Req(o, "enabled");
                    bool enabled = en.Type == JTokenType.Boolean ? (bool)en : en.Type == JTokenType.Integer || en.Type == JTokenType.Float ? JNum(en, "enabled") != 0 : throw Form("enabled true / false");
                    var blur = o["blur"];
                    double b = blur == null || blur.Type == JTokenType.Null ? 0 : JNum(blur, "blur");
                    var focus = Focus(o["focus"], "focus");
                    var ft = o["focusTo"];
                    OverrideFocus to = ft == null ? null : Focus(ft, "focusTo");
                    var fb = o["focusBlend"];
                    double blend = fb == null || fb.Type == JTokenType.Null ? 0 : JNum(fb, "focusBlend");
                    if (blend < 0 || blend > 1) throw Form("focusBlend from 0 to 1");
                    return SetDofCore(new OverrideDof(enabled, JNum(Req(o, "focalSize"), "focalSize"), JNum(Req(o, "aperture"), "aperture"), b, focus, to, blend));
                }
                default: throw Form("JSON");
            }
        }

        public bool Clear()
        {
            if (!Begin()) return false;
            if (!hasValue) return true;
            hasValue = false;
            dof = null;
            source = null;
            layer.ValueChanged(this, hadValue: true);
            return true;
        }

        public void Release()
        {
            if (released) return;
            layer.CheckThread();
            layer.Release(this);
        }

        public void Dispose() => Release();

        // ------------------------------------------------------------ typed setters (the core Timeline, host code)

        /// <summary>Vector3: x, y, z.</summary>
        public bool SetVector(double x, double y, double z)
        {
            if (!Begin()) return false;
            if (Kind != OverrideValueKind.Vector3) throw Form("a vector");
            return SetVectorCore(x, y, z);
        }

        /// <summary>Rotation: a quaternion (normalised here; ArgumentException for length 0).</summary>
        public bool SetQuaternion(double x, double y, double z, double w)
        {
            if (!Begin()) return false;
            if (Kind != OverrideValueKind.Rotation) throw Form("a quaternion");
            return SetQuaternionCore(x, y, z, w);
        }

        /// <summary>Number or Integer.</summary>
        public bool SetNumber(double v) => Set(v);

        /// <summary>Color: r, g, b, a as stored (sRGB-encoded, straight alpha, not clamped).</summary>
        public bool SetColor(double r, double g, double b, double a)
        {
            if (!Begin()) return false;
            if (Kind != OverrideValueKind.Color) throw Form("a colour");
            return SetColorCore(r, g, b, a);
        }

        /// <summary>Camera: the target, the rotation as a quaternion, the distance and the field of view.</summary>
        public bool SetCamera(double tx, double ty, double tz, double qx, double qy, double qz, double qw, double dist, double fov)
        {
            if (!Begin()) return false;
            if (Kind != OverrideValueKind.Camera) throw Form("a camera");
            return SetCameraCore(Finite(tx), Finite(ty), Finite(tz), qx, qy, qz, qw, Finite(dist), Finite(fov));
        }

        /// <summary>DepthOfField.</summary>
        public bool SetDof(OverrideDof value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (!Begin()) return false;
            if (Kind != OverrideValueKind.DepthOfField) throw Form("a depth of field");
            return SetDofCore(value);
        }

        /// <summary>Animation: a row (the source may be null: resolved as the chara.anime command resolves a row without one).</summary>
        public bool SetAnimation(string animeSource, int group, int category, int no)
        {
            if (!Begin()) return false;
            if (Kind != OverrideValueKind.Animation) throw Form("an animation row");
            return SetAnimationCore(animeSource, group, category, no);
        }

        // ------------------------------------------------------------ internals

        bool Begin()
        {
            if (released) return false;
            layer.CheckThread();
            return !released;
        }

        internal void MarkReleased() { released = true; }
        internal void SetPriority(int p) => priority = p;

        ArgumentException Form(string given) =>
            new ArgumentException("path " + Path + " (" + Kind + ") does not take " + given + "; expected " + OverridePath.FormsOf(Kind), "value");

        double Finite(double v) => Bits.Finite(v) ? v : throw new ArgumentException("path " + Path + ": a value is not a finite number", "value");

        double Number(double v)
        {
            // the animation speed: NaN and negative values (minus infinity included) count as 0, as the model's setter; +infinity throws
            if (Channel.Path.Channel == OverrideChannel.AnimeSpeed && (double.IsNaN(v) || v < 0)) return 0;
            return Finite(v);
        }

        double Integer(double v) => Math.Round(Finite(v), MidpointRounding.AwayFromZero);

        int Whole(double v, string what)
        {
            v = Finite(v);
            double r = Math.Round(v, MidpointRounding.AwayFromZero);
            if (r < int.MinValue || r > int.MaxValue) throw new ArgumentException("path " + Path + ": " + what + " out of range", "value");
            return (int)r;
        }

        bool Store1(double v)
        {
            if (hasValue && Bits.Same(values[0], v)) return true;
            values[0] = v;
            return Commit();
        }

        bool SetVectorCore(double x, double y, double z)
        {
            x = Finite(x); y = Finite(y); z = Finite(z);
            if (hasValue && Bits.Same(values[0], x) && Bits.Same(values[1], y) && Bits.Same(values[2], z)) return true;
            values[0] = x; values[1] = y; values[2] = z;
            return Commit();
        }

        bool SetEulerCore(double x, double y, double z)
        {
            Bits.Euler(Finite(x), Finite(y), Finite(z), out double qx, out double qy, out double qz, out double qw);
            return SetQuaternionCore(qx, qy, qz, qw);
        }

        bool SetQuaternionCore(double x, double y, double z, double w)
        {
            x = Finite(x); y = Finite(y); z = Finite(z); w = Finite(w);
            if (!Bits.Normalise(ref x, ref y, ref z, ref w)) throw new ArgumentException("path " + Path + ": a quaternion of length 0", "value");
            if (hasValue && Bits.Same(values[0], x) && Bits.Same(values[1], y) && Bits.Same(values[2], z) && Bits.Same(values[3], w)) return true;
            values[0] = x; values[1] = y; values[2] = z; values[3] = w;
            return Commit();
        }

        bool SetColorCore(double r, double g, double b, double a)
        {
            r = Finite(r); g = Finite(g); b = Finite(b); a = Finite(a);
            if (hasValue && Bits.Same(values[0], r) && Bits.Same(values[1], g) && Bits.Same(values[2], b) && Bits.Same(values[3], a)) return true;
            values[0] = r; values[1] = g; values[2] = b; values[3] = a;
            return Commit();
        }

        bool SetCameraCore(double tx, double ty, double tz, double qx, double qy, double qz, double qw, double dist, double fov)
        {
            qx = Finite(qx); qy = Finite(qy); qz = Finite(qz); qw = Finite(qw);
            if (!Bits.Normalise(ref qx, ref qy, ref qz, ref qw)) throw new ArgumentException("path " + Path + ": a rotation of length 0", "value");
            if (hasValue && Bits.Same(values[0], tx) && Bits.Same(values[1], ty) && Bits.Same(values[2], tz) && Bits.Same(values[3], qx) &&
                Bits.Same(values[4], qy) && Bits.Same(values[5], qz) && Bits.Same(values[6], qw) && Bits.Same(values[7], dist) && Bits.Same(values[8], fov)) return true;
            values[0] = tx; values[1] = ty; values[2] = tz; values[3] = qx; values[4] = qy; values[5] = qz; values[6] = qw; values[7] = dist; values[8] = fov;
            return Commit();
        }

        bool SetDofCore(OverrideDof d)
        {
            if (!Bits.Finite(d.FocalSize) || !Bits.Finite(d.Aperture) || !Bits.Finite(d.Blur) || !Bits.Finite(d.FocusBlend))
                throw new ArgumentException("path " + Path + ": a value is not a finite number", "value");
            if (hasValue && d.Equals(dof)) return true;
            dof = d;
            return Commit();
        }

        bool SetAnimationCore(string src, int group, int category, int no)
        {
            if (hasValue && src == source && values[0] == group && values[1] == category && values[2] == no) return true;
            source = src;
            values[0] = group; values[1] = category; values[2] = no;
            return Commit();
        }

        bool Commit()
        {
            bool had = hasValue;
            hasValue = true;
            layer.ValueChanged(this, had);
            return true;
        }

        static JToken Req(JObject o, string name)
        {
            var t = o[name];
            if (t == null) throw new ArgumentException("the member \"" + name + "\" is required", "json");
            return t;
        }

        JObject JObj(JToken t, string what, params string[] members)
        {
            if (!(t is JObject o)) throw Form("JSON " + t.Type + " for " + what);
            foreach (var p in o.Properties())
                if (Array.IndexOf(members, p.Name) < 0) throw new ArgumentException("path " + Path + ": unknown member \"" + p.Name + "\"; expected " + OverridePath.FormsOf(Kind), "json");
            return o;
        }

        double JNum(JToken t, string what)
        {
            if (t == null || (t.Type != JTokenType.Integer && t.Type != JTokenType.Float)) throw Form("JSON " + (t?.Type.ToString() ?? "nothing") + " for " + what);
            return (double)t;
        }

        double[] JNums(JToken t, int min, int max, string what)
        {
            if (!(t is JArray a) || a.Count < min || a.Count > max) throw Form("JSON " + t.Type + (t is JArray aa ? " of " + aa.Count : "") + " for " + what);
            var r = new double[a.Count];
            for (int i = 0; i < r.Length; i++) r[i] = JNum(a[i], what);
            return r;
        }

        OverrideFocus Focus(JToken t, string what)
        {
            if (t == null || t.Type == JTokenType.Null) return OverrideFocus.Pivot;
            if (t.Type == JTokenType.String && (string)t == "pivot") return OverrideFocus.Pivot;
            if (t is JObject o)
            {
                if (o["point"] != null)
                {
                    JObj(o, what, "point");
                    var p = JNums(o["point"], 3, 3, what + ".point");
                    return OverrideFocus.OfPoint(p[0], p[1], p[2]);
                }
                if (o["dist"] != null)
                {
                    JObj(o, what, "dist");
                    return OverrideFocus.OfDist(JNum(o["dist"], what + ".dist"));
                }
                if (o["node"] != null)
                {
                    JObj(o, what, "node", "bone", "offset");
                    var n = o["node"];
                    if (n.Type != JTokenType.String || string.IsNullOrEmpty((string)n)) throw Form("a node id string for " + what + ".node");
                    var b = o["bone"];
                    if (b == null || b.Type == JTokenType.Null)
                    {
                        if (o["offset"] != null) throw Form(what + ".offset without a bone");
                        return OverrideFocus.OfNode((string)n);
                    }
                    if (b.Type != JTokenType.String || string.IsNullOrEmpty((string)b)) throw Form("a bone name string for " + what + ".bone");
                    var off = o["offset"];
                    var v = off == null || off.Type == JTokenType.Null ? new double[3] : JNums(off, 3, 3, what + ".offset");
                    return OverrideFocus.OfBone((string)n, (string)b, v[0], v[1], v[2]);
                }
            }
            throw new ArgumentException("path " + Path + ": " + what + " must be null, \"pivot\", {\"node\": id}, {\"node\": id, \"bone\": name, \"offset\": [x, y, z]}, {\"point\": [x, y, z]} or {\"dist\": d}", "json");
        }

        public override string ToString() => (NodeId ?? "scene") + " " + Path + " " + Owner + " p" + priority + (released ? " released" : hasValue ? " = " + ValueJson() : " (no value)");
    }
}
