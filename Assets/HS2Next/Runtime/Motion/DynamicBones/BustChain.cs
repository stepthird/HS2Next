// One DynamicBone_Ver02 component (bust / hip chain) simulated from its fields.
// Patterns and derived presets, Awake / InitTransforms / InitLocalPosition / setPtn / ResetPosition /
// ResetParticlesPosition / SetWeight and the parameter setters the game uses, the LateUpdate schedule
// with the 60 Hz accumulator and the HeavyLoopMaxCount cap, one step (integration without rest
// cancellation, elasticity / stiffness cone, colliders, keep-length window, move limit), the vanilla skip update
// or the IllusionFixes hold on frames without a step and the write-back with the aim lever and the
// crush scale.
using System.Collections.Generic;
using UnityEngine;

namespace HS2Next.Motion.DynamicBones
{
    public sealed class BustChain
    {
        /// <summary>Per-bone (or tip) parameter set of one pattern.</summary>
        sealed class Preset
        {
            public float Damping, Elasticity, Stiffness, Inert, NextBoneLength = 1f, Radius;
            public bool RotationCalc, MoveLimit, Crush;
            public Vector3 MoveMin, MoveMax;
            public float KeepMin, KeepMax, CrushMin, CrushMax, CrushAddMin, CrushAddMax;
            public Transform Ref;
            public Vector3 InitLocalPos, InitLocalScale = Vector3.one, LocalPosition;
            public Quaternion InitLocalRot = Quaternion.identity;
            public void CopyTo(BustChain c, int i)
            {
                c.damping[i] = Damping; c.elasticity[i] = Elasticity; c.stiffness[i] = Stiffness; c.inert[i] = Inert;
                c.nbl[i] = NextBoneLength; c.radius[i] = Radius; c.rotCalc[i] = RotationCalc; c.moveLimit[i] = MoveLimit;
                c.moveMin[i] = MoveMin; c.moveMax[i] = MoveMax; c.keepMin[i] = KeepMin; c.keepMax[i] = KeepMax;
                c.crush[i] = Crush; c.crushMin[i] = CrushMin; c.crushMax[i] = CrushMax; c.crushAddMin[i] = CrushAddMin; c.crushAddMax[i] = CrushAddMax;
                c.refT[i] = Ref; c.initLP[i] = InitLocalPos; c.initLR[i] = InitLocalRot; c.initLS[i] = InitLocalScale; c.localPos[i] = LocalPosition;
            }
        }

        public readonly global::DynamicBone_Ver02 Source;
        readonly BustOptions opt;
        /// <summary>ChaControlDefine.DynamicBoneKind: 0 BreastL, 1 BreastR, 2 HipL, 3 HipR (−1 unknown Comment).</summary>
        public readonly int Kind;
        public string Comment => Source ? Source.Comment : "";

        Preset[][] presets;          // [pattern][particle] (null when the pattern's Params count differs from Bones)
        Vector3[] patGravity, patEndOffset;
        int n;                       // Bones.Count + 1
        Transform[] tr;
        bool[] hasT, rotCalc, moveLimit, crush;
        float[] damping, elasticity, stiffness, inert, nbl, radius, keepMin, keepMax, crushMin, crushMax, crushAddMin, crushAddMax;
        Vector3[] moveMin, moveMax, initLP, initLS, localPos, x, p;
        Quaternion[] initLR;
        Transform[] refT;
        Vector3 endOffset;
        readonly List<global::DynamicBoneCollider> colliders = new List<global::DynamicBoneCollider>();

        Transform root;
        Vector3 objPrevPos, objMove;
        float objScale = 1f, acc, weight = 1f;

        public Vector3 Gravity;
        public int PtnNo { get; private set; }
        public bool Active { get; private set; } = true;
        public int ParticleCount => n;
        public int StepsLastFrame { get; private set; }
        public Transform Root => root;
        public Vector3 Position(int i) => x[i];
        public Vector3 PrevPosition(int i) => p[i];
        public Transform ParticleTransform(int i) => hasT[i] ? tr[i] : null;
        public float Damping(int i) => damping[i];
        public float Elasticity(int i) => elasticity[i];
        public float Stiffness(int i) => stiffness[i];
        public float Inert(int i) => inert[i];
        public float NextBoneLength(int i) => nbl[i];
        public float KeepMin(int i) => keepMin[i];
        public float KeepMax(int i) => keepMax[i];
        public Vector3 LocalPosition(int i) => localPos[i];
        public Vector3 EndOffset => endOffset;
        public int PatternCount => presets?.Length ?? 0;

        /// <summary>Awake: presets for all patterns, particles from pattern 0, InitLocalPosition, setPtn(0, force), InitTransforms.</summary>
        public BustChain(global::DynamicBone_Ver02 source, BustOptions options)
        {
            Source = source;
            opt = options ?? new BustOptions();
            Kind = KindOf(source ? source.Comment : null);
            Build();
        }

        public static int KindOf(string comment)
        {
            switch (comment)
            {
                case "Mune_L": return 0;
                case "Mune_R": return 1;
                case "Siri_L": return 2;
                case "Siri_R": return 3;
                default: return -1;
            }
        }

        /// <summary>Re-derives everything from the component's fields (as Awake). The caller re-applies card values.</summary>
        public void Build()
        {
            var c = Source;
            var bones = c && c.Bones != null ? c.Bones : new List<Transform>();
            n = bones.Count + 1;
            tr = new Transform[n]; hasT = new bool[n]; rotCalc = new bool[n]; moveLimit = new bool[n]; crush = new bool[n];
            damping = new float[n]; elasticity = new float[n]; stiffness = new float[n]; inert = new float[n]; nbl = new float[n]; radius = new float[n];
            keepMin = new float[n]; keepMax = new float[n]; crushMin = new float[n]; crushMax = new float[n]; crushAddMin = new float[n]; crushAddMax = new float[n];
            moveMin = new Vector3[n]; moveMax = new Vector3[n]; initLP = new Vector3[n]; initLS = new Vector3[n]; localPos = new Vector3[n];
            x = new Vector3[n]; p = new Vector3[n]; initLR = new Quaternion[n]; refT = new Transform[n];
            for (int i = 0; i < n - 1; i++)
            {
                tr[i] = bones[i]; hasT[i] = bones[i] != null;
                if (hasT[i]) { initLP[i] = bones[i].localPosition; initLR[i] = bones[i].localRotation; initLS[i] = bones[i].localScale; }
                else { initLR[i] = Quaternion.identity; initLS[i] = Vector3.one; }
                nbl[i] = 1f;
            }
            initLR[n - 1] = Quaternion.identity; initLS[n - 1] = Vector3.one; nbl[n - 1] = 1f;
            root = c ? (c.Root ? c.Root : (bones.Count > 0 ? bones[0] : null)) : null;
            Gravity = c ? c.Gravity : Vector3.zero;
            colliders.Clear();
            if (c && c.Colliders != null) colliders.AddRange(c.Colliders);

            // presets
            var pats = c ? c.Patterns : null;
            int pc = pats?.Count ?? 0;
            presets = new Preset[pc][];
            patGravity = new Vector3[pc]; patEndOffset = new Vector3[pc];
            for (int k = 0; k < pc; k++)
            {
                var pt = pats[k];
                if (pt == null) continue;
                patGravity[k] = pt.Gravity; patEndOffset[k] = pt.EndOffset;
                if (pt.Params == null || pt.Params.Count != bones.Count) continue;
                var row = new Preset[n];
                for (int i = 0; i < bones.Count; i++)
                {
                    var b = pt.Params[i];
                    row[i] = new Preset
                    {
                        Damping = Mathf.Clamp01(b.Damping), Elasticity = Mathf.Clamp01(b.Elasticity), Stiffness = Mathf.Clamp01(b.Stiffness),
                        Inert = Mathf.Clamp01(b.Inert), NextBoneLength = Mathf.Max(b.NextBoneLength, 0f), Radius = Mathf.Max(b.CollisionRadius, 0f),
                        RotationCalc = b.IsRotationCalc != 0, MoveLimit = b.IsMoveLimit != 0, MoveMin = b.MoveLimitMin, MoveMax = b.MoveLimitMax,
                        KeepMin = b.KeepLengthLimitMin, KeepMax = b.KeepLengthLimitMax, Crush = b.IsCrush != 0,
                        CrushMin = b.CrushMoveAreaMin, CrushMax = b.CrushMoveAreaMax, CrushAddMin = b.CrushAddXYMin, CrushAddMax = b.CrushAddXYMax,
                        Ref = b.RefTransform,
                        InitLocalPos = initLP[i], InitLocalRot = initLR[i], InitLocalScale = initLS[i],
                    };
                }
                row[n - 1] = new Preset
                {
                    Damping = Mathf.Clamp01(pt.EndOffsetDamping), Elasticity = Mathf.Clamp01(pt.EndOffsetElasticity),
                    Stiffness = Mathf.Clamp01(pt.EndOffsetStiffness), Inert = Mathf.Clamp01(pt.EndOffsetInert),
                };
                presets[k] = row;
            }

            // Particles from pattern 0, LocalPosition = position in the parent bone's local space.
            PtnNo = c ? c.PtnNo : 0;
            if (pc > 0 && presets[0] != null) for (int i = 0; i < n; i++) presets[0][i].CopyTo(this, i);
            endOffset = pc > 0 ? patEndOffset[0] : Vector3.zero;
            for (int i = 1; i < n - 1; i++)
                if (hasT[i] && hasT[i - 1]) localPos[i] = tr[i - 1].InverseTransformPoint(tr[i].position);
            ResetParticlesPosition();
            InitLocalPosition();
            bool allRefs = pc > 0;
            for (int k = 0; k < pc && allRefs; k++)
            {
                if (presets[k] == null) continue;
                for (int i = 0; i < n - 1; i++) if (!presets[k][i].Ref) { allRefs = false; break; }
            }
            if (allRefs) SetPattern(0, true);
            InitTransforms();
        }

        // ------------------------------------------------------------ operations
        /// <summary>Copies the reference bones' local TRS onto the chain bones (or the stored initial pose when unset).</summary>
        public void InitTransforms()
        {
            for (int i = 0; i < n - 1; i++)
            {
                if (!hasT[i]) continue;
                var t = tr[i];
                var r = refT[i];
                if (r) { t.localPosition = r.localPosition; t.localRotation = r.localRotation; t.localScale = r.localScale; }
                else { t.localPosition = initLP[i]; t.localRotation = initLR[i]; t.localScale = initLS[i]; }
            }
        }

        /// <summary>Re-captures the rest geometry (LocalPosition) and the reference bones' local TRS from the current pose.</summary>
        public void InitLocalPosition()
        {
            int nb = n - 1;
            var lp = new Vector3[nb]; var lr = new Quaternion[nb]; var ls = new Vector3[nb];
            for (int i = 0; i < nb; i++) if (hasT[i]) { lp[i] = tr[i].localPosition; lr[i] = tr[i].localRotation; ls[i] = tr[i].localScale; }
            for (int k = 0; k < presets.Length; k++)
            {
                var row = presets[k];
                if (row == null) continue;
                for (int i = 0; i < nb; i++)
                {
                    var r = row[i].Ref;
                    if (!r) continue;
                    row[i].InitLocalPos = r.localPosition; row[i].InitLocalRot = r.localRotation; row[i].InitLocalScale = r.localScale;
                }
                for (int i = 0; i < nb; i++)
                {
                    var r = row[i].Ref;
                    if (r && hasT[i]) { tr[i].localPosition = r.localPosition; tr[i].localRotation = r.localRotation; tr[i].localScale = r.localScale; }
                }
                for (int i = 1; i < nb; i++)
                {
                    var r0 = row[i - 1].Ref; var r1 = row[i].Ref;
                    if (r0 && r1) row[i].LocalPosition = r0.InverseTransformPoint(r1.position);
                }
            }
            for (int i = 0; i < nb; i++) if (hasT[i]) { tr[i].localPosition = lp[i]; tr[i].localRotation = lr[i]; tr[i].localScale = ls[i]; }
        }

        /// <summary>
        /// Overrides the LocalPosition of every pattern with values measured on another hierarchy (the p_cf_anim
        /// prefab, i.e. the bind pose at Awake, hips are never re-set-up). <paramref name="bindPose"/> is the
        /// matching component of that hierarchy; reference bones are matched by pattern / bone index.
        /// </summary>
        public bool CaptureLocalPositionFrom(global::DynamicBone_Ver02 bindPose)
        {
            if (!bindPose || bindPose.Patterns == null) return false;
            bool any = false;
            for (int k = 0; k < presets.Length && k < bindPose.Patterns.Count; k++)
            {
                var row = presets[k];
                var src = bindPose.Patterns[k]?.Params;
                if (row == null || src == null || src.Count != n - 1) continue;
                for (int i = 1; i < n - 1; i++)
                {
                    var r0 = src[i - 1].RefTransform; var r1 = src[i].RefTransform;
                    if (r0 && r1) { row[i].LocalPosition = r0.InverseTransformPoint(r1.position); any = true; }
                }
            }
            if (any && PtnNo >= 0 && PtnNo < presets.Length && presets[PtnNo] != null)
                for (int i = 1; i < n - 1; i++) localPos[i] = presets[PtnNo][i].LocalPosition;
            return any;
        }

        /// <summary>setPtn(n, force).</summary>
        public bool SetPattern(int ptn, bool force)
        {
            if (ptn < 0 || ptn >= presets.Length || presets[ptn] == null || presets[ptn].Length != n) return false;
            if (ptn == PtnNo && !force) return false;
            PtnNo = ptn;
            Gravity = patGravity[ptn];
            for (int i = 0; i < n; i++) presets[ptn][i].CopyTo(this, i);
            endOffset = patEndOffset[ptn];
            return true;
        }

        /// <summary>ResetPosition: rest offsets follow the current body shape.</summary>
        public void ResetPosition()
        {
            InitLocalPosition();
            SetPattern(PtnNo, true);
            if (Active) InitTransforms();
        }

        /// <summary>ResetParticlesPosition.</summary>
        public void ResetParticlesPosition()
        {
            objPrevPos = root ? root.position : (Source ? Source.transform.position : Vector3.zero);
            objMove = Vector3.zero;
            for (int i = 0; i < n - 1; i++) if (hasT[i]) x[i] = p[i] = tr[i].position;
            var last = n >= 2 ? tr[n - 2] : null;
            Vector3 tip = last ? last.TransformPoint(endOffset) : Vector3.zero;
            x[n - 1] = p[n - 1] = tip;
        }

        /// <summary>Enable / disable with OnEnable (reset particles) / OnDisable (InitTransforms) semantics.</summary>
        public void SetActive(bool on)
        {
            if (on == Active) return;
            Active = on;
            if (on) ResetParticlesPosition(); else InitTransforms();
        }

        /// <summary>SetWeight.</summary>
        public void SetWeight(float w)
        {
            if (w == weight) return;
            if (w == 0f) InitTransforms();
            else if (weight == 0f) ResetParticlesPosition();
            weight = w;
        }

        /// <summary>setGravity(n, g): live gravity and pattern n's (n &lt; 0: all patterns).</summary>
        public void SetGravity(int ptn, Vector3 g, bool live = true)
        {
            if (live) Gravity = g;
            for (int k = 0; k < patGravity.Length; k++) if (ptn < 0 || k == ptn) patGravity[k] = g;
        }

        /// <summary>setSoftParams(n, b, d, e, s). b = −1: every particle incl. root and tip. No clamping.</summary>
        public void SetSoftParams(int ptn, int bone, float d, float e, float s, bool live = true)
        {
            for (int i = 0; i < n; i++)
            {
                if (bone >= 0 && i != bone) continue;
                if (live) { damping[i] = d; elasticity[i] = e; stiffness[i] = s; }
                for (int k = 0; k < presets.Length; k++)
                    if ((ptn < 0 || k == ptn) && presets[k] != null) { presets[k][i].Damping = d; presets[k][i].Elasticity = e; presets[k][i].Stiffness = s; }
            }
        }

        /// <summary>setSoftParamsEx(n, b, inert).</summary>
        public void SetSoftParamsEx(int ptn, int bone, float inertValue, bool live = true)
        {
            for (int i = 0; i < n; i++)
            {
                if (bone >= 0 && i != bone) continue;
                if (live) inert[i] = inertValue;
                for (int k = 0; k < presets.Length; k++)
                    if ((ptn < 0 || k == ptn) && presets[k] != null) presets[k][i].Inert = inertValue;
            }
        }

        // ------------------------------------------------------------ frame
        /// <summary>Step count for this frame.</summary>
        public int Accumulate(float dt)
        {
            float rate = Source.UpdateRate;
            if (rate <= 0f) return 1;
            float h = 1f / rate;
            acc += dt;
            int k = 0;
            int cap = Source.HeavyLoopMaxCount;
            while (acc >= h)
            {
                acc -= h;
                k++;
                if (k >= cap) { acc = 0f; break; }
            }
            return k;
        }

        /// <summary>One LateUpdate.</summary>
        public void Frame(float dt)
        {
            StepsLastFrame = 0;
            if (weight <= 0f || n < 2) return;
            InitTransforms();
            if (!root) return;
            objScale = Mathf.Abs(root.lossyScale.x);
            Vector3 rp = root.position;
            objMove = rp - objPrevPos;
            objPrevPos = rp;
            int steps = Accumulate(dt);
            bool hasPatterns = presets.Length > 0;
            if (steps > 0)
            {
                for (int s = 0; s < steps; s++)
                {
                    if (hasPatterns) Integrate();
                    Constrain();
                    objMove = Vector3.zero;
                }
            }
            else if (!opt.HoldOnSkipFrames) SkipUpdate();
            StepsLastFrame = steps;
            WriteBack();
        }

        void Integrate()
        {
            Vector3 f = (Gravity + Source.Force) * objScale;
            float reflect = Source.ReflectSpeed;
            p[0] = x[0];
            x[0] = hasT[0] ? tr[0].position : x[0];
            for (int i = 1; i < n; i++)
            {
                Vector3 v = (x[i] - p[i]) * reflect;
                Vector3 c = objMove * inert[i];
                p[i] = x[i] + c;
                x[i] = x[i] + v * (1f - damping[i]) + f + c;
            }
        }

        void RestFrame(int i, out float len, out Vector3 r)
        {
            int j = i - 1;
            var tj = tr[j];
            Vector3 q;
            if (hasT[i]) { len = (tj.position - tr[i].position).magnitude; q = localPos[i]; }
            else { len = endOffset.magnitude * objScale; q = endOffset; }
            r = tj.localToWorldMatrix.MultiplyVector(q) + x[j];
        }

        void Constrain()
        {
            for (int i = 1; i < n; i++)
            {
                int j = i - 1;
                if (!hasT[j]) continue;
                RestFrame(i, out float len, out Vector3 r);
                Vector3 xi = x[i];
                float s = Mathf.Lerp(1f, stiffness[i], weight);
                if (s > 0f || elasticity[i] > 0f)
                {
                    xi += (r - xi) * elasticity[i];
                    if (s > 0f) Cone(ref xi, r, len, s);
                }
                if (colliders.Count > 0)
                {
                    float pr = radius[i] * objScale;
                    for (int k = 0; k < colliders.Count; k++)
                    {
                        var c = colliders[k];
                        if (c == null) continue;
                        var shape = DynamicBoneColliders.Evaluate(c, ColliderPath.Ver02, false);
                        DynamicBoneColliders.Collide(shape, ref xi, pr);
                    }
                }
                KeepLength(i, ref xi, len);
                if (hasT[i] && moveLimit[i]) MoveLimit(i, ref xi, r);
                x[i] = xi;
            }
        }

        static void Cone(ref Vector3 xi, Vector3 r, float len, float s)
        {
            Vector3 d = r - xi;
            float dl = d.magnitude;
            float maxDev = len * (1f - s) * 2f;
            if (dl > maxDev) xi += d * ((dl - maxDev) / dl);
        }

        //
        void KeepLength(int i, ref Vector3 xi, float len)
        {
            Vector3 e = x[i - 1] - xi;
            float d = e.magnitude;
            if (d <= 0f) return;
            float k = (d - len) / d;
            if (keepMin[i] >= k) xi += e * (k - keepMin[i]);
            else if (k >= keepMax[i]) xi += e * (k - keepMax[i]);
        }

        //
        void MoveLimit(int i, ref Vector3 xi, Vector3 r)
        {
            var m = tr[i].localToWorldMatrix;
            m.SetColumn(3, new Vector4(r.x, r.y, r.z, 1f));
            Vector3 y = m.inverse.MultiplyPoint3x4(xi);
            y = new Vector3(Mathf.Clamp(y.x, moveMin[i].x, moveMax[i].x), Mathf.Clamp(y.y, moveMin[i].y, moveMax[i].y), Mathf.Clamp(y.z, moveMin[i].z, moveMax[i].z));
            xi = m.MultiplyPoint3x4(y);
        }

        // vanilla skip update
        void SkipUpdate()
        {
            for (int i = 1; i < n; i++)
            {
                Vector3 c = objMove * inert[i];
                p[i] += c; x[i] += c;
            }
            p[0] = x[0];
            if (hasT[0]) x[0] = tr[0].position;
            for (int i = 1; i < n; i++)
            {
                if (!hasT[i - 1]) continue;
                RestFrame(i, out float len, out Vector3 r);
                Vector3 xi = x[i];
                float s = Mathf.Lerp(1f, stiffness[i], weight);
                if (s > 0f) Cone(ref xi, r, len, s);
                KeepLength(i, ref xi, len);
                if (hasT[i] && moveLimit[i]) MoveLimit(i, ref xi, r);
                x[i] = xi;
            }
        }

        //
        void WriteBack()
        {
            for (int i = 1; i < n; i++)
            {
                int j = i - 1;
                if (!hasT[j]) continue;
                if (rotCalc[j])
                {
                    var tj = tr[j];
                    Vector3 q = hasT[i] ? localPos[i] : endOffset;
                    Vector3 a = tj.TransformDirection(q);
                    Vector3 b = x[i] - x[j];
                    if (q != Vector3.zero) b = x[i] - a * (1f - nbl[j]) - x[j];
                    tj.rotation = Quaternion.FromToRotation(a, b) * tj.rotation;
                }
                if (!hasT[i]) continue;
                var ti = tr[i];
                if (crush[i])
                {
                    float z = ti.InverseTransformPoint(x[i]).z;
                    float add;
                    if (z <= 0f) add = crushAddMin[i] * (1f - Mathf.InverseLerp(crushMin[i], 0f, z));
                    else add = crushAddMax[i] * Mathf.InverseLerp(0f, crushMax[i], z);
                    ti.localScale = initLS[i] + new Vector3(add, add, 0f);
                }
                ti.position = x[i];
            }
        }

        public override string ToString() => $"{Comment} (kind {Kind}): {n} particles, pattern {PtnNo}/{PatternCount}, gravity {Gravity.y:0.######}, rate {Source.UpdateRate} cap {Source.HeavyLoopMaxCount}";
    }
}
