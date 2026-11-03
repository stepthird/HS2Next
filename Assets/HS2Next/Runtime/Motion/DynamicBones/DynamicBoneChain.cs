// One DynamicBone component simulated as a position-based Verlet chain.
// Built from the component's fields only (the compat class is a data holder): chain construction
// with the IllusionFixes or the vanilla variant, per-particle parameters, frame schedule, one step:
// integration with the rest-pose gravity cancellation, elasticity, stiffness cone, colliders and the
// hard length; write-back, lifecycle / reset semantics, distance disable.
//
// The game resets the chain bones to their Start pose in the Update phase (before the Animator) and
// simulates in LateUpdate. HS2Next has no Update hook, so the reset is emulated at the start of each
// frame: a bone whose local pose is still what the previous write-back left there (nobody else wrote
// it since) gets its initial local pose back; a bone that another writer (Animator, FK, ...) changed
// keeps that pose, which is what the game sees after its Animator pass. Between two frames the bones therefore still
// hold the write-back; ResetBeforeUpdate does the reset early for what reads them there (the Animator binding a
// controller takes them as its defaults).
using System.Collections.Generic;
using UnityEngine;

namespace HS2Next.Motion.DynamicBones
{
    /// <summary>Studio grouping of a chain.</summary>
    public enum DynamicBoneCategory { Hair, Clothes, Accessory, Other }

    public sealed class DynamicBoneChain
    {
        public readonly global::DynamicBone Source;
        readonly DynamicBoneOptions opt;
        readonly ColliderTable table;
        readonly IList<MonoBehaviour> colliderOverride;

        // ---- metadata (set by the stage / setup)
        public DynamicBoneCategory Category = DynamicBoneCategory.Other;
        /// <summary>Hair kind 0..3 or clothes kind 0..7 (−1 otherwise).</summary>
        public int Kind = -1;
        /// <summary>Accessory slot (−1 otherwise).</summary>
        public int Slot = -1;
        /// <summary>Hair bundle index (CmpHair.boneInfo) the component belongs to (−1: none).</summary>
        public int Bundle = -1;
        /// <summary>Top / bottom clothes chain whose root subtree holds skirt bones.</summary>
        public bool IsSkirt;
        /// <summary>Card noShake (hair bundle or accessory slot).</summary>
        public bool NoShake;
        /// <summary>Per-chain user switch (Studio). The effective state also needs the category switch, !NoShake and an active, enabled component.</summary>
        public bool Enabled = true;
        /// <summary>Applied state: true while the chain simulates (OnEnable / OnDisable semantics on change).</summary>
        public bool Active { get; private set; }

        // ---- particles
        int n, tips;
        Transform[] tr;
        bool[] hasT, modified, oneChild, direct;
        bool allDirect;
        int[] parent;
        float[] damping, elasticity, stiffness, inert, radius, boneLength;
        Vector3[] x, p, endOffset, initLocalPos, wPos;
        Quaternion[] initLocalRot, wRot;
        Vector3[] sPos, sLocal;
        Matrix4x4[] sM;
        // Fast path (all particle transforms are direct children of their parent particle's transform): world
        // matrices are composed from local poses, and the write-back sets local poses (no O(depth) world access).
        Vector3[] sScale, curLP;
        Quaternion[] curLR, rw;
        Matrix4x4[] mw;
        int[] colliders = new int[0], colActive = new int[0];
        float broadBound;
        float[] restLen, pathLen;
        bool written;

        // ---- chain state
        Transform owner, root;
        Vector3 localGravity, objPrevPos, objMove;
        float objScale = 1f, weight = 1f, acc;
        int updateMode, freezeAxis;
        float updateRate;
        bool far;

        public int ParticleCount => n;
        public int TipCount => tips;
        public int ColliderCount => colliders.Length;
        public float TotalLength { get; private set; }
        public float Weight => weight;
        public Transform Root => root;
        public Transform Owner => owner;
        public bool DistantDisabled => far;
        /// <summary>Steps simulated in the last frame (0 = skip update).</summary>
        public int StepsLastFrame { get; private set; }
        public string RootName => root ? root.name : "-";

        public Vector3 Position(int i) => x[i];
        public Vector3 PrevPosition(int i) => p[i];
        public Transform ParticleTransform(int i) => hasT[i] ? tr[i] : null;
        public int Parent(int i) => parent[i];
        public float BoneLength(int i) => boneLength[i];
        public float Damping(int i) => damping[i];
        public float Elasticity(int i) => elasticity[i];
        public float Stiffness(int i) => stiffness[i];
        public float Inert(int i) => inert[i];
        public float Radius(int i) => radius[i];
        public Vector3 EndOffset(int i) => endOffset[i];
        public int ColliderIndex(int k) => colliders[k];
        public float ObjScale => objScale;

        /// <summary>Builds the chain from the component's fields and the current pose.</summary>
        /// <param name="colliderList">Collider list to use instead of the component's m_Colliders (null = m_Colliders).</param>
        public DynamicBoneChain(global::DynamicBone source, DynamicBoneOptions options, ColliderTable colliderTable, IList<MonoBehaviour> colliderList = null)
        {
            Source = source;
            opt = options ?? new DynamicBoneOptions();
            table = colliderTable ?? new ColliderTable();
            colliderOverride = colliderList;
            Build();
        }

        // ------------------------------------------------------------ construction
        readonly List<Transform> bT = new List<Transform>();
        readonly List<int> bParent = new List<int>();
        readonly List<float> bLen = new List<float>();
        readonly List<Vector3> bPos = new List<Vector3>(), bEnd = new List<Vector3>(), bLp = new List<Vector3>();
        readonly List<Quaternion> bLr = new List<Quaternion>();
        List<Transform> excl, notRolls;
        bool tipConfigured;
        float endLength;
        Vector3 endOffsetField;

        void Build()
        {
            var c = Source;
            owner = c ? c.transform : null;
            root = c ? c.m_Root : null;
            n = 0; tips = 0; TotalLength = 0; written = false; acc = 0; far = false;
            bT.Clear(); bParent.Clear(); bLen.Clear(); bPos.Clear(); bEnd.Clear(); bLp.Clear(); bLr.Clear();
            if (!c || !root) { Allocate(0); colliders = new int[0]; return; }
            updateMode = c.m_UpdateMode;
            updateRate = c.m_UpdateRate;
            freezeAxis = c.m_FreezeAxis;
            excl = c.m_Exclusions;
            notRolls = c.m_notRolls;   // null (older layout) = empty
            endLength = c.m_EndLength;
            endOffsetField = c.m_EndOffset;
            tipConfigured = endLength > 0f || endOffsetField != Vector3.zero;

            Append(root, -1, 0f);
            Allocate(bT.Count);
            for (int i = 0; i < n; i++)
            {
                tr[i] = bT[i]; hasT[i] = bT[i] != null; parent[i] = bParent[i]; boneLength[i] = bLen[i];
                x[i] = p[i] = bPos[i]; endOffset[i] = bEnd[i]; initLocalPos[i] = bLp[i]; initLocalRot[i] = bLr[i];
                if (!hasT[i]) tips++;
            }
            // the parent is rotated only when its transform has at most one child (cached; Rebuild refreshes).
            for (int i = 0; i < n; i++) oneChild[i] = hasT[i] && tr[i].childCount <= 1;
            allDirect = true;
            for (int i = 1; i < n; i++)
            {
                direct[i] = !hasT[i] || tr[i].parent == tr[parent[i]];
                if (!direct[i]) allDirect = false;
            }
            localGravity = root.InverseTransformDirection(c.m_Gravity);
            objScale = Mathf.Abs(owner.lossyScale.x);
            objPrevPos = owner.position;
            objMove = Vector3.zero;
            UpdateParameters();

            // Collider list: one record per entry, null / disabled entries included (job path).
            var list = colliderOverride ?? (IList<MonoBehaviour>)c.m_Colliders;
            var path = opt.IntendedBehaviour ? ColliderPath.MainThread : ColliderPath.Job;
            var idx = new List<int>();
            if (list != null)
                foreach (var m in list)
                {
                    if (path != ColliderPath.Job && m == null) continue;
                    idx.Add(table.IndexOf(m, path));
                }
            colliders = idx.ToArray();
            colActive = new int[colliders.Length];
            bT.Clear(); bPos.Clear();
        }

        void Allocate(int count)
        {
            n = count;
            tr = new Transform[n]; hasT = new bool[n]; modified = new bool[n]; oneChild = new bool[n]; direct = new bool[n]; parent = new int[n];
            damping = new float[n]; elasticity = new float[n]; stiffness = new float[n]; inert = new float[n]; radius = new float[n]; boneLength = new float[n];
            x = new Vector3[n]; p = new Vector3[n]; endOffset = new Vector3[n]; initLocalPos = new Vector3[n]; wPos = new Vector3[n];
            initLocalRot = new Quaternion[n]; wRot = new Quaternion[n];
            sPos = new Vector3[n]; sLocal = new Vector3[n]; sM = new Matrix4x4[n];
            restLen = new float[n]; pathLen = new float[n];
            sScale = new Vector3[n]; curLP = new Vector3[n]; curLR = new Quaternion[n]; rw = new Quaternion[n]; mw = new Matrix4x4[n];
            hRot = new Quaternion[n]; hPos = new Vector3[n]; hRotSet = new bool[n]; hLocalSet = new bool[n];
        }

        bool IsExcluded(Transform t) => excl != null && excl.Contains(t);
        bool IsNotRoll(Transform t) => notRolls != null && notRolls.Contains(t);

        // append(b, q, L); b == null is a tip particle.
        void Append(Transform b, int q, float L)
        {
            int index = bT.Count;
            Vector3 pos, eo = Vector3.zero, lp = Vector3.zero;
            Quaternion lr = Quaternion.identity;
            if (b != null)
            {
                pos = b.position; lp = b.localPosition; lr = b.localRotation;
            }
            else
            {
                var t = bT[q];
                if (endLength > 0f)
                {
                    var tp = t.parent;
                    eo = tp != null ? t.InverseTransformPoint(2f * t.position - tp.position) * endLength : new Vector3(endLength, 0, 0);
                }
                else eo = t.InverseTransformPoint(t.position + owner.rotation * endOffsetField);
                pos = t.TransformPoint(eo);
            }
            float len = 0f;
            if (q >= 0)
            {
                L += (bT[q].position - pos).magnitude;
                len = L;
                TotalLength = Mathf.Max(TotalLength, L);
            }
            bT.Add(b); bParent.Add(q); bLen.Add(len); bPos.Add(pos); bEnd.Add(eo); bLp.Add(lp); bLr.Add(lr);
            if (b == null) return;

            if (opt.Variant == ChainVariant.Vanilla)
            {
                Transform notRoll = null;
                for (int k = 0; k < b.childCount; k++)
                {
                    var ch = b.GetChild(k);
                    bool skip = false;
                    if (IsExcluded(ch)) skip = true;
                    else if (IsNotRoll(ch)) { skip = true; notRoll = ch; }
                    if (!skip) Append(ch, index, L);
                    else if (tipConfigured) Append(null, index, L);
                }
                if (notRoll != null)
                    for (int k = 0; k < notRoll.childCount; k++)
                    {
                        var g = notRoll.GetChild(k);
                        if (!IsExcluded(g) && !IsNotRoll(g)) Append(g, index, L);
                    }
                if (b.childCount == 0 && tipConfigured) Append(null, index, L);
            }
            else
            {
                // IllusionFixes: notRoll bones are skipped through any depth; one tip per leaf only.
                var cur = b;
                bool appended = false;
                while (cur.childCount > 0)
                {
                    int last = -1; bool lastNotRoll = false;
                    for (int k = 0; k < cur.childCount; k++)
                    {
                        var ch = cur.GetChild(k);
                        if (IsExcluded(ch)) continue;
                        bool nr = IsNotRoll(ch);
                        last = k; lastNotRoll = nr;
                        if (!nr) { Append(ch, index, L); appended = true; }
                    }
                    if (last >= 0 && lastNotRoll) cur = cur.GetChild(last);
                    else break;
                }
                if (!appended && tipConfigured) Append(null, index, L);
            }
        }

        /// <summary>per-particle damping / elasticity / stiffness / inert / radius from the fields and distribution curves.</summary>
        public void UpdateParameters()
        {
            var c = Source;
            if (!c) return;
            for (int i = 0; i < n; i++)
            {
                float d = c.m_Damping, e = c.m_Elasticity, s = c.m_Stiffness, it = c.m_Inert, r = c.m_Radius;
                if (TotalLength > 0f)
                {
                    float u = boneLength[i] / TotalLength;
                    d *= Curve(c.m_DampingDistrib, u, 1f);
                    e *= Curve(c.m_ElasticityDistrib, u, 1f);
                    s *= Curve(c.m_StiffnessDistrib, u, 1f);
                    it *= Curve(c.m_InertDistrib, u, 1f);
                    r *= Curve(c.m_RadiusDistrib, u, 1f);
                }
                damping[i] = Mathf.Clamp01(d); elasticity[i] = Mathf.Clamp01(e); stiffness[i] = Mathf.Clamp01(s);
                inert[i] = Mathf.Clamp01(it); radius[i] = Mathf.Max(r, 0f);
            }
        }

        static float Curve(AnimationCurve c, float u, float def) => c != null && c.length > 0 ? c.Evaluate(u) : def;

        // ------------------------------------------------------------ lifecycle
        /// <summary>Applies the effective switch state: on = OnEnable (reset particles), off = OnDisable (restore the Start pose).</summary>
        public void SetActive(bool on)
        {
            if (on == Active) return;
            Active = on;
            if (on) ResetParticles(); else RestorePose();
        }

        /// <summary>SetWeight: 0 restores the Start pose, leaving 0 resets particles; between 0 and 1 only the stiffness changes.</summary>
        public void SetWeight(float w)
        {
            if (w == weight) return;
            if (w == 0f) RestorePose();
            else if (weight == 0f) ResetParticles();
            weight = w;
        }

        /// <summary>
        /// Restores every chain bone to the local pose captured at build (OnDisable). With <paramref name="keepHeld"/> (Rebuild)
        /// a bone that still holds that pose bit for bit is not written: Unity normalises a written rotation, so writing a
        /// prefab's slightly unnormalised rotation back would move the bone by an ulp (a chain re-derived by an edit would then
        /// differ from a fresh build, which never writes it).
        /// </summary>
        public void RestorePose(bool keepHeld = false)
        {
            for (int i = 0; i < n; i++)
            {
                if (!hasT[i] || !tr[i]) continue;
                if (keepHeld)
                {
                    tr[i].GetLocalPositionAndRotation(out Vector3 lp, out Quaternion lr);
                    if (Bits(lp, initLocalPos[i]) && Bits(lr, initLocalRot[i])) continue;
                }
                tr[i].localPosition = initLocalPos[i]; tr[i].localRotation = initLocalRot[i];
            }
            written = false;
        }

        static bool Bits(float a, float b) => System.BitConverter.SingleToInt32Bits(a) == System.BitConverter.SingleToInt32Bits(b);
        static bool Bits(Vector3 a, Vector3 b) => Bits(a.x, b.x) && Bits(a.y, b.y) && Bits(a.z, b.z);
        static bool Bits(Quaternion a, Quaternion b) => Bits(a.x, b.x) && Bits(a.y, b.y) && Bits(a.z, b.z) && Bits(a.w, b.w);

        /// <summary>
        /// After 1.1.0: the particles (and the object position the inertia reads) moved by <paramref name="d"/>, a rigid move of the
        /// whole body (the build placing a character assembled at the origin). A chain that has written its bones keeps its
        /// snapshots (they hold local poses or are taken again at its next frame).
        /// </summary>
        public void CarryRigid(Matrix4x4 d)
        {
            if (x == null) return;
            for (int i = 0; i < n; i++)
            {
                x[i] = d.MultiplyPoint3x4(x[i]);
                p[i] = d.MultiplyPoint3x4(p[i]);
            }
            objPrevPos = d.MultiplyPoint3x4(objPrevPos);
        }

        /// <summary>Particles at the current bone positions, no velocity (OnEnable / ResetParticlesPosition).</summary>
        public void ResetParticles()
        {
            for (int i = 0; i < n; i++)
            {
                Vector3 v = hasT[i] ? tr[i].position : tr[parent[i]].TransformPoint(endOffset[i]);
                x[i] = p[i] = v;
            }
            if (owner) objPrevPos = owner.position;
            objMove = Vector3.zero;
        }

        /// <summary>Drops all history: the frame's bone reset, then particles from the bones (Studio / CharaMotion.ResetAll).</summary>
        public void ResetAll()
        {
            PreUpdateReset();
            ResetParticles();
            acc = 0f;
        }

        /// <summary>
        /// The game's Update-phase bone reset, done now instead of at the start of the next frame: the bones the
        /// last write-back left untouched by other writers get their Start pose back; particles and timing are kept, so the next
        /// frame simulates as it would have. Skipped like the game's reset while distant-disabled or at weight 0. HS2's reset
        /// runs at Update −16000, before every other Update, so anything that reads the pose during the Update phase (the
        /// Animator binding a new controller) sees the Start pose there; AnimatePhysics chains are reset too (their FixedUpdate
        /// reset precedes Update in a frame with a fixed step), so such a reader never depends on the frame timing.
        /// </summary>
        public void ResetBeforeUpdate()
        {
            if (n == 0 || weight <= 0f || far) return;
            PreUpdateReset();
        }

        /// <summary>Re-reads the component's fields and rebuilds the chain from the Start pose (after DBE / Studio edits).</summary>
        public void Rebuild()
        {
            bool was = Active;
            if (written || was) RestorePose(keepHeld: true);
            Build();
            Active = was && n > 0;
        }

        // ---- Phase 4.3: Rebuild in two halves, so a step between the restore and
        // the capture (the hair bundle corrections re-applied, DynamicBoneStage.Rebuild(afterRestore)) lands in the captured
        // rest pose; and the captured rest pose read back (the digest's chains section, HairReport).
        /// <summary>The first half of <see cref="Rebuild"/>: the chain bones back to the captured rest pose (as Rebuild does).
        /// Returns the chain's active state for <see cref="BuildAfterRestore"/>.</summary>
        public bool RestoreForRebuild()
        {
            bool was = Active;
            if (written || was) RestorePose(keepHeld: true);
            return was;
        }

        /// <summary>The second half of <see cref="Rebuild"/>: re-reads the component's fields and captures the current pose
        /// (no second restore).</summary>
        public void BuildAfterRestore(bool wasActive)
        {
            Build();
            Active = wasActive && n > 0;
        }

        /// <summary>The local position particle <paramref name="i"/>'s transform had when the chain was built (the rest pose the
        /// game's Update-phase reset restores; zero for a tip).</summary>
        public Vector3 RestLocalPosition(int i) => initLocalPos[i];
        /// <summary>The local rotation of the rest pose (identity for a tip).</summary>
        public Quaternion RestLocalRotation(int i) => initLocalRot[i];

        // ------------------------------------------------------------ frame
        /// <summary>One rendered frame: distance check, bone reset, 0..k steps, write-back.</summary>
        /// <param name="steps">Steps to simulate (from the stage's time mode).</param>
        /// <param name="fixedTick">A fixed step happened this frame (AnimatePhysics reset).</param>
        public void Frame(int steps, bool fixedTick)
        {
            StepsLastFrame = 0;
            if (n == 0 || weight <= 0f) return;
            if (opt.DistantDisable && Source.m_DistantDisable != 0)
            {
                Transform rt = Source.m_ReferenceObject;
                if (!rt) { var cam = Camera.main; rt = cam ? cam.transform : null; }
                if (rt)
                {
                    float d = Source.m_DistanceToObject;
                    bool f = (rt.position - owner.position).sqrMagnitude > d * d;
                    if (far && !f) { far = false; ResetParticles(); Simulate(steps); return; }
                    far = f;
                }
                if (far) return;
            }
            if (updateMode != 1 || !opt.FaithfulAnimatePhysics || fixedTick) PreUpdateReset();
            Simulate(steps);
        }

        /// <summary>
        /// A frame of no time (dt exactly 0: the global pause or a paused character): the chain
        /// is held in its parent's frame — every chain bone gets the local pose of the last write-back again (only where another
        /// writer, e.g. the Animator's defaults, changed it), so the bones stand bit for bit as after the last simulated frame and
        /// follow their parents rigidly when an edit moves them; the particles are carried to the held bones (velocity kept), so a
        /// resume continues from what is shown. Replaces SkipUpdate's stiffness / length projection at dt 0, which creeps every
        /// frame (HS2 shows a settled chain unchanged at timeScale 0, package H's measurement). Nothing to hold before the first
        /// write-back.
        /// </summary>
        public void Hold()
        {
            StepsLastFrame = 0;
            if (n == 0 || weight <= 0f || far || !written) return;
            // the root's parent frame now vs at the last snapshot (sM[0] = parent × the root's local TRS then)
            Matrix4x4 then = sM[0] * Matrix4x4.TRS(sLocal[0], snapRot0, sScale[0]).inverse;
            Matrix4x4 now = tr[0].parent ? tr[0].parent.localToWorldMatrix : Matrix4x4.identity;
            bool sameParent = true;
            for (int k = 0; k < 16 && sameParent; k++) sameParent = Mathf.Abs(then[k] - now[k]) <= 1e-5f;
            if (sameParent && hFast)
            {
                // replay the last local writes with the same inputs: the same stored values bit for bit
                for (int i = 0; i < n; i++) if (hasT[i] && hLocalSet[i]) tr[i].SetLocalPositionAndRotation(curLP[i], curLR[i]);
            }
            else if (sameParent)
            {
                // replay the last world writes in their order (parent rotation, then the child's position)
                for (int i = 1; i < n; i++)
                {
                    int j = parent[i];
                    if (hRotSet[j]) tr[j].rotation = hRot[j];
                    if (hasT[i]) tr[i].position = hPos[i];
                }
            }
            else
            {
                // the chain's parent moved (an edit while paused): the last local pose, carried rigidly
                for (int i = 0; i < n; i++)
                {
                    if (!hasT[i]) continue;
                    var t = tr[i];
                    t.GetLocalPositionAndRotation(out Vector3 lp, out Quaternion lr);
                    if (lp.x != wPos[i].x || lp.y != wPos[i].y || lp.z != wPos[i].z || lr.x != wRot[i].x || lr.y != wRot[i].y || lr.z != wRot[i].z || lr.w != wRot[i].w)
                        t.SetLocalPositionAndRotation(wPos[i], wRot[i]);
                }
            }
            // the particles follow only when the held chain moved (an edit while paused): an unchanged parent leaves them bit for bit
            if (sameParent) return;
            for (int i = 0; i < n; i++)
            {
                Vector3 cur = hasT[i] ? tr[i].position : tr[parent[i]].TransformPoint(endOffset[i]);
                Vector3 d = cur - x[i];
                x[i] += d;
                p[i] += d;
            }
            if (owner) objPrevPos = owner.position;
            objMove = Vector3.zero;
        }

        Quaternion snapRot0 = Quaternion.identity;
        Quaternion[] hRot = new Quaternion[0];
        Vector3[] hPos = new Vector3[0];
        bool[] hRotSet = new bool[0], hLocalSet = new bool[0];
        bool hFast;

        /// <summary>Fixed-rate accumulator (the asset's intended update rate, at most MaxStepsPerFrame steps).</summary>
        public int Accumulate(float dt)
        {
            if (dt <= 0f) return 0;
            float rate = updateRate > 0f ? updateRate : opt.DefaultUpdateRate;
            if (rate <= 0f) return 1;
            float h = 1f / rate;
            acc += dt;
            int k = 0;
            while (acc >= h)
            {
                acc -= h;
                if (++k >= opt.MaxStepsPerFrame) { acc = 0f; break; }
            }
            return k;
        }

        // emulated: undo our last write-back on bones nobody else wrote since.
        void PreUpdateReset()
        {
            if (!written) return;
            written = false;
            for (int i = 0; i < n; i++)
            {
                if (!hasT[i] || !modified[i]) continue;
                var t = tr[i];
                t.GetLocalPositionAndRotation(out Vector3 lp, out Quaternion lr);
                if (lp.x == wPos[i].x && lp.y == wPos[i].y && lp.z == wPos[i].z &&
                    lr.x == wRot[i].x && lr.y == wRot[i].y && lr.z == wRot[i].z && lr.w == wRot[i].w)
                    t.SetLocalPositionAndRotation(initLocalPos[i], initLocalRot[i]);
            }
        }

        void Simulate(int steps)
        {
            objScale = Mathf.Abs(owner.lossyScale.x);
            Vector3 op = owner.position;
            objMove = op - objPrevPos;
            objPrevPos = op;
            Snapshot();
            if (steps > 0)
            {
                for (int s = 0; s < steps; s++)
                {
                    Integrate();
                    Constrain();
                    objMove = Vector3.zero;
                }
            }
            else SkipUpdate();
            objMove = Vector3.zero;
            WriteBack();
            StepsLastFrame = steps;
        }

        bool fast;

        void Snapshot()
        {
            fast = allDirect && opt.LocalWriteBack;
            if (n > 0 && hasT[0]) snapRot0 = tr[0].localRotation;   // Hold: the root's parent frame of this snapshot
            for (int i = 0; i < n; i++)
            {
                if (!hasT[i]) continue;
                var t = tr[i];
                t.GetLocalPositionAndRotation(out sLocal[i], out wRot[i]);   // wRot: pre-write rotation (see WriteBack)
                Matrix4x4 m;
                if (fast && i > 0)
                {
                    var sc = t.localScale;
                    sScale[i] = sc;
                    m = sM[parent[i]] * Matrix4x4.TRS(sLocal[i], wRot[i], sc);
                }
                else
                {
                    m = t.localToWorldMatrix;
                    sScale[i] = t.localScale;
                }
                sM[i] = m;
                sPos[i] = new Vector3(m.m03, m.m13, m.m23);
            }
        }

        //
        void Integrate()
        {
            Vector3 g = Source.m_Gravity;
            Vector3 gEff = Vector3.zero;
            if (g.x != 0f || g.y != 0f || g.z != 0f)
            {
                Vector3 gn = g.normalized;
                Vector3 gRest = root.TransformDirection(localGravity);
                gEff = g - gn * Mathf.Max(0f, Vector3.Dot(gRest, gn));
            }
            Vector3 f = (gEff + Source.m_Force) * objScale;
            for (int i = 0; i < n; i++)
            {
                if (parent[i] < 0) { p[i] = x[i]; x[i] = sPos[i]; continue; }
                Vector3 v = x[i] - p[i];
                Vector3 c = objMove * inert[i];
                p[i] = x[i] + c;
                x[i] = x[i] + v * (1f - damping[i]) + f + c;
            }
        }

        // (A elasticity, B stiffness cone, colliders, [intended: freeze axis], hard length)
        // Collider broad-phase for one step (exact). Before its collider loop a particle lies within
        // B = max(|x_integrated − x0|, path length of rest lengths) of the root particle x0: elasticity and the cone move
        // it towards the rest target, and the rest target lies within the path length of x0. A collider whose grown shape
        // stays outside the ball of radius B cannot push a particle that stays inside the ball, so only the others are
        // tested; a particle that a push carries out of the ball is redone with the full ordered list (Constrain).
        int SelectColliders()
        {
            int nc = colliders.Length;
            if (nc == 0) return 0;
            if (!opt.ColliderBroadPhase)
            {
                for (int k = 0; k < nc; k++) colActive[k] = colliders[k];
                return nc;
            }
            float ox = x[0].x, oy = x[0].y, oz = x[0].z;
            float bound = 0f, maxPr = 0f;
            pathLen[0] = 0f;
            for (int i = 1; i < n; i++)
            {
                float pl = pathLen[parent[i]] + restLen[i];
                pathLen[i] = pl;
                float dx = x[i].x - ox, dy = x[i].y - oy, dz = x[i].z - oz;
                float d2 = dx * dx + dy * dy + dz * dz;
                if (d2 > pl * pl) pl = Mathf.Sqrt(d2);
                if (pl > bound) bound = pl;
                float pr = radius[i] * objScale;
                if (pr > maxPr) maxPr = pr;
            }
            broadBound = bound;
            var shapes = table.Shapes;
            int m = 0;
            for (int k = 0; k < nc; k++)
            {
                ref var sh = ref shapes[colliders[k]];
                bool keep;
                if (sh.Inside || sh.Kind == ColliderShapeKind.Plane) keep = true;
                else if (sh.Kind == ColliderShapeKind.None) keep = false;
                else
                {
                    float cx = sh.A.x, cy = sh.A.y, cz = sh.A.z;
                    if (sh.Kind == ColliderShapeKind.Capsule)
                    {
                        float t = (ox - cx) * sh.U.x + (oy - cy) * sh.U.y + (oz - cz) * sh.U.z;
                        if (t >= sh.UU) { cx = sh.B.x; cy = sh.B.y; cz = sh.B.z; }
                        else if (t > 0f) { t *= sh.InvUU; cx += sh.U.x * t; cy += sh.U.y * t; cz += sh.U.z * t; }
                    }
                    float reach = bound + sh.Radius + maxPr;
                    float ex = ox - cx, ey = oy - cy, ez = oz - cz;
                    keep = ex * ex + ey * ey + ez * ez < reach * reach;
                }
                if (keep) colActive[m++] = colliders[k];
            }
            return m;
        }

        void Constrain()
        {
            bool freeze = opt.IntendedBehaviour && freezeAxis != 0;
            for (int i = 1; i < n; i++)
            {
                int j = parent[i];
                restLen[i] = hasT[i] ? (sPos[j] - sPos[i]).magnitude : sM[j].MultiplyVector(endOffset[i]).magnitude;
            }
            int activeCount = SelectColliders();
            var shapes = table.Shapes;
            float b2 = broadBound * broadBound;
            for (int i = 1; i < n; i++)
            {
                int j = parent[i];
                ref Matrix4x4 mj = ref sM[j];
                Vector3 q = hasT[i] ? sLocal[i] : endOffset[i];
                float len = restLen[i];
                Vector3 xj = x[j];
                float px = x[i].x, py = x[i].y, pz = x[i].z;
                float s = Mathf.Lerp(1f, stiffness[i], weight);
                float e = elasticity[i];
                if (s > 0f || e > 0f)
                {
                    // r = M(T_j)[x_j] · q
                    float rx = mj.m00 * q.x + mj.m01 * q.y + mj.m02 * q.z + xj.x;
                    float ry = mj.m10 * q.x + mj.m11 * q.y + mj.m12 * q.z + xj.y;
                    float rz = mj.m20 * q.x + mj.m21 * q.y + mj.m22 * q.z + xj.z;
                    px += (rx - px) * e; py += (ry - py) * e; pz += (rz - pz) * e;
                    if (s > 0f)
                    {
                        float dx = rx - px, dy = ry - py, dz = rz - pz;
                        float dl = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
                        float maxDev = len * (1f - s) * 2f;
                        if (dl > maxDev) { float f = (dl - maxDev) / dl; px += dx * f; py += dy * f; pz += dz * f; }
                    }
                }
                if (activeCount > 0)
                {
                    float pr = radius[i] * objScale;
                    if (activeCount == colliders.Length)
                    {
                        for (int k = 0; k < activeCount; k++)
                            DynamicBoneColliders.CollideFast(ref shapes[colActive[k]], ref px, ref py, ref pz, pr);
                    }
                    else
                    {
                        float sx = px, sy = py, sz = pz, ox = x[0].x, oy = x[0].y, oz = x[0].z;
                        bool left = false;
                        for (int k = 0; k < activeCount && !left; k++)
                        {
                            DynamicBoneColliders.CollideFast(ref shapes[colActive[k]], ref px, ref py, ref pz, pr);
                            float dx = px - ox, dy = py - oy, dz = pz - oz;
                            if (dx * dx + dy * dy + dz * dz >= b2) left = true;
                        }
                        if (left)
                        {
                            px = sx; py = sy; pz = sz;
                            for (int k = 0; k < colliders.Length; k++)
                                DynamicBoneColliders.CollideFast(ref shapes[colliders[k]], ref px, ref py, ref pz, pr);
                        }
                    }
                }
                if (freeze)
                {
                    var tj = tr[j];
                    Vector3 nrm = freezeAxis == 1 ? tj.right : freezeAxis == 2 ? tj.up : tj.forward;
                    float dd = nrm.x * (px - xj.x) + nrm.y * (py - xj.y) + nrm.z * (pz - xj.z);
                    px -= nrm.x * dd; py -= nrm.y * dd; pz -= nrm.z * dd;
                }
                float ex = xj.x - px, ey = xj.y - py, ez = xj.z - pz;
                float el = Mathf.Sqrt(ex * ex + ey * ey + ez * ez);
                if (el > 0f) { float f = (el - len) / el; px += ex * f; py += ey * f; pz += ez * f; }
                x[i] = new Vector3(px, py, pz);
            }
        }

        // Fixed-rate frames without a step (the asset's skip routine): carry object motion, root follows,
        // stiffness cone and hard length only. HS2 never runs it (frame-locked); here it keeps chains attached
        // to animated / moved bones on display frames between two 60 Hz steps.
        void SkipUpdate()
        {
            for (int i = 0; i < n; i++)
            {
                if (parent[i] < 0) { p[i] = x[i]; x[i] = sPos[i]; continue; }
                Vector3 c = objMove * inert[i];
                p[i] += c; x[i] += c;
            }
            for (int i = 1; i < n; i++)
            {
                int j = parent[i];
                Matrix4x4 mj = sM[j];
                Vector3 q;
                float len;
                if (hasT[i]) { q = sLocal[i]; len = (sPos[j] - sPos[i]).magnitude; }
                else { q = endOffset[i]; len = mj.MultiplyVector(q).magnitude; }
                Vector3 xi = x[i];
                float s = Mathf.Lerp(1f, stiffness[i], weight);
                if (s > 0f)
                {
                    Vector3 d = mj.MultiplyVector(q) + x[j] - xi;
                    float dl = d.magnitude;
                    float maxDev = len * (1f - s) * 2f;
                    if (dl > maxDev) xi += d * ((dl - maxDev) / dl);
                }
                Vector3 ev = x[j] - xi;
                float el = ev.magnitude;
                if (el > 0f) xi += ev * ((el - len) / el);
                x[i] = xi;
            }
        }

        //
        void WriteBack()
        {
            for (int i = 0; i < n; i++) hRotSet[i] = false;
            hFast = fast;
            if (fast) WriteBackLocal(); else WriteBackWorld();
            for (int i = 0; i < n; i++)
            {
                if (!hasT[i]) continue;
                tr[i].GetLocalPositionAndRotation(out Vector3 lp, out Quaternion lr);
                modified[i] = lr.x != wRot[i].x || lr.y != wRot[i].y || lr.z != wRot[i].z || lr.w != wRot[i].w
                              || lp.x != sLocal[i].x || lp.y != sLocal[i].y || lp.z != sLocal[i].z;
                wPos[i] = lp; wRot[i] = lr;
            }
            written = true;
        }

        // Literal version on world transforms (chains with notRoll / non-direct parents).
        void WriteBackWorld()
        {
            for (int i = 1; i < n; i++)
            {
                int j = parent[i];
                var tj = tr[j];
                if (oneChild[j])
                {
                    Vector3 q = hasT[i] ? sLocal[i] : endOffset[i];
                    Quaternion rj = tj.rotation;
                    Vector3 a = rj * q;
                    Vector3 b = x[i] - x[j];
                    var nr = Quaternion.FromToRotation(a, b) * rj;
                    tj.rotation = nr;
                    hRot[j] = nr; hRotSet[j] = true;   // Hold replays these writes
                }
                if (hasT[i]) { tr[i].position = x[i]; hPos[i] = x[i]; }
            }
        }

        // Same result on local poses: world rotation / matrix of each particle transform tracked from the root's parent.
        void WriteBackLocal()
        {
            // Root's parent frame from the root's own world rotation / matrix (no access to the parent transform).
            Quaternion r0 = tr[0].rotation;
            Quaternion parentRot = r0 * Quaternion.Inverse(wRot[0]);
            Matrix4x4 parentM = oneChild[0] ? sM[0] * Matrix4x4.TRS(sLocal[0], wRot[0], sScale[0]).inverse : Matrix4x4.identity;
            for (int i = 0; i < n; i++) if (hasT[i]) { curLP[i] = sLocal[i]; curLR[i] = wRot[i]; }
            rw[0] = r0;
            mw[0] = sM[0];
            for (int i = 1; i < n; i++)
            {
                int j = parent[i];
                if (oneChild[j])
                {
                    Vector3 q = hasT[i] ? sLocal[i] : endOffset[i];
                    Vector3 a = rw[j] * q;
                    Vector3 b = x[i] - x[j];
                    Quaternion nr = Quaternion.FromToRotation(a, b) * rw[j];
                    Quaternion pr = j == 0 ? parentRot : rw[parent[j]];
                    curLR[j] = Quaternion.Inverse(pr) * nr;
                    rw[j] = nr;
                    mw[j] = (j == 0 ? parentM : mw[parent[j]]) * Matrix4x4.TRS(curLP[j], curLR[j], sScale[j]);
                }
                if (!hasT[i]) continue;
                curLP[i] = mw[j].inverse.MultiplyPoint3x4(x[i]);
                rw[i] = rw[j] * curLR[i];
                mw[i] = mw[j] * Matrix4x4.TRS(curLP[i], curLR[i], sScale[i]);
            }
            for (int i = 0; i < n; i++)
            {
                if (!hasT[i]) continue;
                var lp = curLP[i]; var lr = curLR[i];
                hLocalSet[i] = false;
                if (lp.x != sLocal[i].x || lp.y != sLocal[i].y || lp.z != sLocal[i].z || lr.x != wRot[i].x || lr.y != wRot[i].y || lr.z != wRot[i].z || lr.w != wRot[i].w)
                {
                    tr[i].SetLocalPositionAndRotation(lp, lr);
                    hLocalSet[i] = true;   // Hold replays these writes (curLP / curLR keep the values)
                }
            }
        }

        public override string ToString() => $"{Category}{(Kind >= 0 ? " " + Kind : "")}{(Slot >= 0 ? " slot " + Slot : "")} {RootName}: {n} particles ({tips} tips), {colliders.Length} colliders{(IsSkirt ? ", skirt" : "")}{(NoShake ? ", noShake" : "")}";
    }
}
