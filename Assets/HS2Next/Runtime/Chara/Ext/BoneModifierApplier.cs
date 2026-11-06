// A card's bone modifiers and rule effects on top of the current pose. Per bone all modifiers combine (scale and length multiply,
// position and Euler rotation add). Only a channel that has a value is read or written:
//   scale     localScale    = base x scale per axis
//   position  localPosition = base * length + position   (without a length: base + position)
//   rotation  localRotation = base * Euler(rotation)
// The pass runs in the motion stage bones.modifiers (Runtime/Motion/Modifiers/BoneModifierStages.cs), after the animation, the hand
// patterns and the heel lift and before everything else that writes bones: so a modifier acts on this frame's input and the later
// stages see the modified pose. Without motion stages the pass of the build stands.
// What the base is, per channel: this frame's value from the pose sources. A channel holding the pass's last write (W) or the value
// the last frame ended on (E, recorded by the stage bones.modifiers.end) has not been rewritten by a pose source, so its base stays;
// any other value is new input and becomes the base. E covers every later writer of the last frame; W covers a writer that went
// back to what the pass left. So nothing accumulates, a later absolute writer does not become the base, and when it stops the
// channel shows base and modifier at once.
// What reads a bone to store a base (a controller's bind, the shape guard, the rest pose) reads it without the modifiers: ToBase, Bare.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace HS2Next.Chara.Ext
{
    [DisallowMultipleComponent]
    public sealed class BoneModifierApplier : MonoBehaviour
    {
        public sealed class Modifier
        {
            public Transform Target;
            public string Bone;
            /// <summary>"abmx", "aac", ...</summary>
            public string Source;
            /// <summary>Where it was found: ABMX BoneLocation (1 BodyTop, 10..29 accessory slot + 10).</summary>
            public int Location;
            public Vector3 Scale = Vector3.one;
            public float Length = 1;
            public Vector3 Position = Vector3.zero;
            /// <summary>Euler degrees.</summary>
            public Vector3 Rotation = Vector3.zero;
            public override string ToString() => $"{Source}:{Bone}@{Location} scale {Scale} length {Length} pos {Position} rot {Rotation}";
        }

        sealed class BoneState
        {
            public Transform T;
            public Vector3 Scale = Vector3.one, Position, Rotation;
            public Quaternion Rq = Quaternion.identity;
            public float Length = 1;
            public bool HasScale, HasLength, HasPosition, HasRotation;
            public bool HasPos => HasLength || HasPosition;
            // per channel: the base, the last write as stored, the value the last frame ended on
            public bool BaseP, BaseR, BaseS;
            public Vector3 BaseScale, BasePos, WScale, WPos, EScale, EPos;
            public Quaternion BaseRot, WRot, ERot;
            // what a bare read took off
            public Vector3 SPos, SScale;
            public Quaternion SRot;
            public bool SP, SR, SS;
        }

        const float Tolerance = 1e-6f;

        readonly List<Modifier> modifiers = new List<Modifier>();
        readonly List<BoneState> bones = new List<BoneState>();
        readonly Dictionary<Transform, BoneState> byTransform = new Dictionary<Transform, BoneState>();
        readonly HashSet<Transform> suppressed = new HashSet<Transform>();
        bool dirty, inBare;

        /// <summary>
        /// After 1.1.0 (flat nipples under clothes): the modifiers of these bones are left out (their bones go back to their base
        /// once) until the set changes; null or empty = every modifier applies (the default). False when nothing changed.
        /// </summary>
        public bool SetSuppressed(IEnumerable<Transform> ts)
        {
            var next = new HashSet<Transform>();
            if (ts != null) foreach (var t in ts) if (t) next.Add(t);
            if (next.SetEquals(suppressed)) return false;
            suppressed.Clear();
            suppressed.UnionWith(next);
            dirty = true;
            return true;
        }

        /// <summary>The bones whose modifiers are left out now.</summary>
        public int SuppressedCount => suppressed.Count;

        public IReadOnlyList<Modifier> Modifiers => modifiers;
        /// <summary>Number of distinct transforms written.</summary>
        public int BoneCount { get { if (dirty) Rebuild(); return bones.Count; } }

        public void Add(Modifier m) { if (m?.Target == null) return; modifiers.Add(m); dirty = true; }
        public void AddRange(IEnumerable<Modifier> ms) { foreach (var m in ms) Add(m); }

        /// <summary>Removes the modifiers of one source (null = all); their channels go back to their base at the next pass.</summary>
        public void Remove(string source)
        {
            modifiers.RemoveAll(m => source == null || m.Source == source);
            dirty = true;
        }

        // ------------------------------------------------------------ the pass

        /// <summary>One pass with the state kept (a build, a flush, a test).</summary>
        public void Apply() => Pass(true);

        /// <summary>One pass now (an edit's flush, where the next step may be far off); the base is detected again for what the edit wrote.</summary>
        public void ApplyNow() => Pass(true);

        /// <summary>One pass over every channel with a value. Without <paramref name="commit"/> (inside a probe, whose end puts the
        /// transforms back) the base, the last write and the end value stay as they are.</summary>
        public void Pass(bool commit)
        {
            if (dirty) Rebuild();
            for (int i = 0; i < bones.Count; i++) Write(bones[i], commit, false);
        }

        /// <summary>A pass over the channels something rewrote since (a sample outside a step): they show their modifier at once. A
        /// channel that holds the pass's write or the last frame's value stays as it stands, a later writer's value included.</summary>
        public void ApplyNew()
        {
            if (dirty) Rebuild();
            for (int i = 0; i < bones.Count; i++) Write(bones[i], true, true);
        }

        /// <summary>The frame's end (the stage bones.modifiers.end): what each channel with a value shows now.</summary>
        public void End()
        {
            for (int i = 0; i < bones.Count; i++)
            {
                var b = bones[i];
                var t = b.T;
                if (!t) continue;
                if (b.BaseP) b.EPos = t.localPosition;
                if (b.BaseR) b.ERot = t.localRotation;
                if (b.BaseS) b.EScale = t.localScale;
            }
        }

        /// <summary>A writer outside a step (the hips offset shown at once) or after the end stage: what the bone holds now counts as the frame's end, so the next
        /// pass does not take that write for new input.</summary>
        public void Forget(Transform t)
        {
            if (dirty) Rebuild();
            if (!t || !byTransform.TryGetValue(t, out var b)) return;
            if (b.BaseP) b.EPos = t.localPosition;
            if (b.BaseR) b.ERot = t.localRotation;
            if (b.BaseS) b.EScale = t.localScale;
        }

        /// <summary>Before a controller's bind or a revive, and before the second read of its defaults: every channel that still holds the modifier's result back to its base.
        /// The state stays: the next pass finds another value there and takes it as the base.</summary>
        public void ToBase()
        {
            if (dirty) Rebuild();
            for (int i = 0; i < bones.Count; i++)
            {
                var b = bones[i];
                var t = b.T;
                if (!t) continue;
                if (b.BaseP && Held(t.localPosition, b.WPos, b.EPos)) t.localPosition = b.BasePos;
                if (b.BaseR && Held(t.localRotation, b.WRot, b.ERot)) t.localRotation = b.BaseRot;
                if (b.BaseS && Held(t.localScale, b.WScale, b.EScale)) t.localScale = b.BaseScale;
            }
        }

        /// <summary>Runs <paramref name="read"/> with the bones as they are without the modifiers and puts the shown values back; no state
        /// changes. For a reader that stores a base (the shape guard, the rest pose).</summary>
        public void Bare(Action read)
        {
            if (inBare) { read(); return; }
            if (dirty) Rebuild();
            inBare = true;
            for (int i = 0; i < bones.Count; i++)
            {
                var b = bones[i];
                b.SP = b.SR = b.SS = false;
                var t = b.T;
                if (!t) continue;
                if (b.BaseP)
                {
                    var c = t.localPosition;
                    if (Held(c, b.WPos, b.EPos)) { b.SPos = c; b.SP = true; t.localPosition = b.BasePos; }
                }
                if (b.BaseR)
                {
                    var c = t.localRotation;
                    if (Held(c, b.WRot, b.ERot)) { b.SRot = c; b.SR = true; t.localRotation = b.BaseRot; }
                }
                if (b.BaseS)
                {
                    var c = t.localScale;
                    if (Held(c, b.WScale, b.EScale)) { b.SScale = c; b.SS = true; t.localScale = b.BaseScale; }
                }
            }
            try { read(); }
            finally
            {
                inBare = false;
                for (int i = 0; i < bones.Count; i++)
                {
                    var b = bones[i];
                    var t = b.T;
                    if (!t) continue;
                    if (b.SP) t.localPosition = b.SPos;
                    if (b.SR) t.localRotation = b.SRot;
                    if (b.SS) t.localScale = b.SScale;
                }
            }
        }

        /// <summary>Every channel with a value back to its base, unconditionally (an edit evaluates the shapes again from the bases; a
        /// shape row that writes one axis keeps the others, which must not carry a modifier then). The next pass detects the bases again.</summary>
        public void RestoreBases()
        {
            for (int i = 0; i < bones.Count; i++)
            {
                var b = bones[i];
                var t = b.T;
                if (!t) continue;
                if (b.BaseP) { t.localPosition = b.BasePos; b.BaseP = false; }
                if (b.BaseR) { t.localRotation = b.BaseRot; b.BaseR = false; }
                if (b.BaseS) { t.localScale = b.BaseScale; b.BaseS = false; }
            }
        }

        // ------------------------------------------------------------ what other writers ask

        /// <summary>True when a modifier gives <paramref name="t"/> a scale value (the pass owns that channel).</summary>
        public bool HasScale(Transform t)
        {
            if (dirty) Rebuild();
            return t && byTransform.TryGetValue(t, out var b) && b.HasScale;
        }

        /// <summary>The channels of <paramref name="t"/> that have a value, and the combined scale; false when no modifier names it.</summary>
        public bool ChannelsOf(Transform t, out bool hasPos, out bool hasRot, out bool hasScale, out Vector3 scale)
        {
            if (dirty) Rebuild();
            hasPos = hasRot = hasScale = false;
            scale = Vector3.one;
            if (!t || !byTransform.TryGetValue(t, out var b)) return false;
            hasPos = b.HasPos; hasRot = b.HasRotation; hasScale = b.HasScale;
            scale = b.Scale;
            return hasPos || hasRot || hasScale;
        }

        /// <summary>The bone's local position without the modifier's result (its value when it was rewritten since the pass).</summary>
        public Vector3 BarePosition(Transform t)
        {
            if (dirty) Rebuild();
            var c = t.localPosition;
            return byTransform.TryGetValue(t, out var b) && b.BaseP && Held(c, b.WPos, b.EPos) ? b.BasePos : c;
        }

        // ------------------------------------------------------------ inside

        void Rebuild()
        {
            dirty = false;
            var old = new Dictionary<Transform, BoneState>();
            foreach (var b in bones) if (b.T) old[b.T] = b;
            bones.Clear();
            byTransform.Clear();
            foreach (var m in modifiers)
            {
                if (!m.Target || (suppressed.Count > 0 && suppressed.Contains(m.Target))) continue;
                if (!byTransform.TryGetValue(m.Target, out var b))
                {
                    b = old.TryGetValue(m.Target, out var o) ? o : new BoneState { T = m.Target };
                    b.Scale = Vector3.one; b.Length = 1; b.Position = Vector3.zero; b.Rotation = Vector3.zero;
                    b.HasScale = b.HasLength = b.HasPosition = b.HasRotation = false;
                    byTransform[m.Target] = b;
                    bones.Add(b);
                    old.Remove(m.Target);
                }
                if (m.Scale.x != 1 || m.Scale.y != 1 || m.Scale.z != 1) { b.Scale = Vector3.Scale(b.Scale, m.Scale); b.HasScale = true; }
                if (m.Length != 1) { b.Length *= m.Length; b.HasLength = true; }
                if (m.Position.x != 0 || m.Position.y != 0 || m.Position.z != 0) { b.Position += m.Position; b.HasPosition = true; }
                if (m.Rotation.x != 0 || m.Rotation.y != 0 || m.Rotation.z != 0) { b.Rotation += m.Rotation; b.HasRotation = true; }
            }
            foreach (var b in bones) b.Rq = b.HasRotation ? Quaternion.Euler(b.Rotation) : Quaternion.identity;
            // a channel that lost its value: back to its base once (when nothing else wrote it meanwhile), and nothing touches it after that
            foreach (var b in bones) Lost(b);
            foreach (var b in old.Values)
            {
                b.HasScale = b.HasLength = b.HasPosition = b.HasRotation = false;
                Lost(b);
            }
        }

        static void Lost(BoneState b)
        {
            var t = b.T;
            if (b.BaseP && !b.HasPos) { if (t && Held(t.localPosition, b.WPos, b.EPos)) t.localPosition = b.BasePos; b.BaseP = false; }
            if (b.BaseR && !b.HasRotation) { if (t && Held(t.localRotation, b.WRot, b.ERot)) t.localRotation = b.BaseRot; b.BaseR = false; }
            if (b.BaseS && !b.HasScale) { if (t && Held(t.localScale, b.WScale, b.EScale)) t.localScale = b.BaseScale; b.BaseS = false; }
        }

        static void Write(BoneState b, bool commit, bool onlyNew)
        {
            var t = b.T;
            if (!t) return;
            if (b.HasPos)
            {
                var cur = t.localPosition;
                bool held = b.BaseP && Held(cur, b.WPos, b.EPos);
                if (!(onlyNew && held))
                {
                    var bas = held ? b.BasePos : cur;
                    t.localPosition = b.HasLength ? bas * b.Length + b.Position : bas + b.Position;
                    if (commit)
                    {
                        b.BasePos = bas;
                        b.WPos = t.localPosition;   // as stored
                        if (!b.BaseP) { b.EPos = b.WPos; b.BaseP = true; }
                    }
                }
            }
            if (b.HasRotation)
            {
                var cur = t.localRotation;
                bool held = b.BaseR && Held(cur, b.WRot, b.ERot);
                if (!(onlyNew && held))
                {
                    var bas = held ? b.BaseRot : cur;
                    t.localRotation = bas * b.Rq;
                    if (commit)
                    {
                        b.BaseRot = bas;
                        b.WRot = t.localRotation;
                        if (!b.BaseR) { b.ERot = b.WRot; b.BaseR = true; }
                    }
                }
            }
            if (b.HasScale)
            {
                var cur = t.localScale;
                bool held = b.BaseS && Held(cur, b.WScale, b.EScale);
                if (!(onlyNew && held))
                {
                    var bas = held ? b.BaseScale : cur;
                    t.localScale = Vector3.Scale(bas, b.Scale);
                    if (commit)
                    {
                        b.BaseScale = bas;
                        b.WScale = t.localScale;
                        if (!b.BaseS) { b.EScale = b.WScale; b.BaseS = true; }
                    }
                }
            }
        }

        static bool Held(in Vector3 cur, in Vector3 w, in Vector3 e) => Near(cur, w) || Near(cur, e);
        static bool Held(in Quaternion cur, in Quaternion w, in Quaternion e) => Near(cur, w) || Near(cur, e);

        static bool Near(in Vector3 a, in Vector3 b) =>
            Mathf.Abs(a.x - b.x) <= Tolerance && Mathf.Abs(a.y - b.y) <= Tolerance && Mathf.Abs(a.z - b.z) <= Tolerance;

        static bool Near(in Quaternion a, in Quaternion b)
        {
            bool q = Mathf.Abs(a.x - b.x) <= Tolerance && Mathf.Abs(a.y - b.y) <= Tolerance && Mathf.Abs(a.z - b.z) <= Tolerance && Mathf.Abs(a.w - b.w) <= Tolerance;
            return q || (Mathf.Abs(a.x + b.x) <= Tolerance && Mathf.Abs(a.y + b.y) <= Tolerance && Mathf.Abs(a.z + b.z) <= Tolerance && Mathf.Abs(a.w + b.w) <= Tolerance);
        }
    }
}
