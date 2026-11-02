// Bone modifiers on top of the current pose (ABMX, plus rule "bone effects" such
// as AdditionalAccessoryControls' body-part rules). Per bone, all modifiers are combined (scale and length
// multiplied, position and Euler rotation added, as ABMX combines effects) and written each LateUpdate:
//   scale     localScale    = base ⊙ scale
//   length    localPosition = base * length + position   (else, when position ≠ 0: base + position)
//   rotation  localRotation = base * Euler(rotation)
// "base" is the transform's value before this component wrote it: when the value is still the one written last
// time (nothing animated or re-evaluated the bone), the previous base is reused; otherwise (animator, shape
// re-evaluation) the new value becomes the base. This equals ABMX's baseline method for a still frame and applies
// rotation/position as deltas on the animated pose (the deliberate deviation).
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
            public float Length = 1;
            public bool HasScale, HasLength, HasPosition, HasRotation;
            public bool HasBase;
            public Vector3 BaseScale, BasePos, WScale, WPos;
            public Quaternion BaseRot, WRot;
        }

        readonly List<Modifier> modifiers = new List<Modifier>();
        readonly List<BoneState> bones = new List<BoneState>();
        bool dirty;

        public IReadOnlyList<Modifier> Modifiers => modifiers;
        /// <summary>Number of distinct transforms written.</summary>
        public int BoneCount { get { if (dirty) Rebuild(); return bones.Count; } }

        public void Add(Modifier m) { if (m?.Target == null) return; modifiers.Add(m); dirty = true; }
        public void AddRange(IEnumerable<Modifier> ms) { foreach (var m in ms) Add(m); }

        /// <summary>Removes the modifiers of one source (null = all) and restores those bones to their base once.</summary>
        public void Remove(string source)
        {
            modifiers.RemoveAll(m => source == null || m.Source == source);
            dirty = true;
        }

        /// <summary>One pass over all bones (also used headless, where no LateUpdate runs).</summary>
        public void Apply()
        {
            if (dirty) Rebuild();
            foreach (var b in bones) Write(b);
        }

        void LateUpdate() => Apply();

        /// <summary>Every bone this component wrote back to its base, unconditionally (an
        /// edit re-evaluates the shapes from the bases; a shape row that writes one axis keeps the others, which must not
        /// carry a modifier then). The next pass re-detects the bases.</summary>
        public void RestoreBases()
        {
            foreach (var b in bones)
            {
                if (!b.T || !b.HasBase) continue;
                b.T.localScale = b.BaseScale; b.T.localPosition = b.BasePos; b.T.localRotation = b.BaseRot;
                b.HasBase = false;
            }
        }

        /// <summary>What one pass writes on <paramref name="t"/> from the base (lp, lr, ls), as the pass combines that bone's
        /// modifiers; false (the base returned) when no modifier names it. For another latch-style writer of the same bone (the
        /// heels' foot joints): a bone that holds this value was not rewritten since.</summary>
        public bool Predict(Transform t, Vector3 lp, Quaternion lr, Vector3 ls, out Vector3 p, out Quaternion r, out Vector3 s)
        {
            p = lp; r = lr; s = ls;
            if (!t) return false;
            if (dirty) Rebuild();
            foreach (var b in bones)
            {
                if (b.T != t) continue;
                s = b.HasScale ? Vector3.Scale(ls, b.Scale) : ls;
                p = b.HasLength ? lp * b.Length + b.Position : b.HasPosition ? lp + b.Position : lp;
                r = b.HasRotation ? lr * Quaternion.Euler(b.Rotation) : lr;
                return true;
            }
            return false;
        }

        /// <summary>One pass now (edit-mode flushes, where no LateUpdate runs, see the
        /// state play mode reaches; the base is re-detected for bones a shape re-evaluation wrote).</summary>
        public void ApplyNow() => Apply();

        void Rebuild()
        {
            dirty = false;
            var old = new Dictionary<Transform, BoneState>();
            foreach (var b in bones) if (b.T) old[b.T] = b;
            bones.Clear();
            var byT = new Dictionary<Transform, BoneState>();
            foreach (var m in modifiers)
            {
                if (!m.Target) continue;
                if (!byT.TryGetValue(m.Target, out var b))
                {
                    b = old.TryGetValue(m.Target, out var o) ? o : new BoneState { T = m.Target };
                    b.Scale = Vector3.one; b.Length = 1; b.Position = Vector3.zero; b.Rotation = Vector3.zero;
                    b.HasScale = b.HasLength = b.HasPosition = b.HasRotation = false;
                    byT[m.Target] = b;
                    bones.Add(b);
                    old.Remove(m.Target);
                }
                if (m.Scale.x != 1 ||m.Scale.y != 1 || m.Scale.z != 1) { b.Scale = Vector3.Scale(b.Scale, m.Scale); b.HasScale = true; }
                if (m.Length != 1) { b.Length *= m.Length; b.HasLength = true; }
                if (m.Position.x != 0 || m.Position.y != 0 || m.Position.z != 0) { b.Position += m.Position; b.HasPosition = true; }
                if (m.Rotation.x != 0 || m.Rotation.y != 0 || m.Rotation.z != 0) { b.Rotation += m.Rotation; b.HasRotation = true; }
            }
            // Bones that lost all modifiers: back to their base once (when nothing else wrote them meanwhile).
            foreach (var b in old.Values) Restore(b);
        }

        static void Restore(BoneState b)
        {
            if (!b.T || !b.HasBase) return;
            if (Same(b.T.localScale, b.WScale)) b.T.localScale = b.BaseScale;
            if (Same(b.T.localPosition, b.WPos)) b.T.localPosition = b.BasePos;
            if (Same(b.T.localRotation, b.WRot)) b.T.localRotation = b.BaseRot;
        }

        static void Write(BoneState b)
        {
            var t = b.T;
            if (!t) return;
            var ls = t.localScale; var lp = t.localPosition; var lr = t.localRotation;
            if (!b.HasBase || !Same(ls, b.WScale)) b.BaseScale = ls;
            if (!b.HasBase || !Same(lp, b.WPos)) b.BasePos = lp;
            if (!b.HasBase || !Same(lr, b.WRot)) b.BaseRot = lr;
            b.HasBase = true;
            var s = b.HasScale ? Vector3.Scale(b.BaseScale, b.Scale) : b.BaseScale;
            var p = b.HasLength ? b.BasePos * b.Length + b.Position : b.HasPosition ? b.BasePos + b.Position : b.BasePos;
            var r = b.HasRotation ? b.BaseRot * Quaternion.Euler(b.Rotation) : b.BaseRot;
            t.localScale = s; t.localPosition = p; t.localRotation = r;
            b.WScale = t.localScale; b.WPos = t.localPosition; b.WRot = t.localRotation;   // as stored
        }

        static bool Same(Vector3 a, Vector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;
        static bool Same(Quaternion a, Quaternion b) => a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;
    }
}
