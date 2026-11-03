// The bones-only skeleton the pose stick figures are posed on: one per sex, hidden,
// transforms only (no mesh, no texture, no renderer, never drawn). Its rest pose is the skeleton prefab of the user's
// install (the one every character is built on) after the default body shape of that sex (all sliders 0.5; males at
// height 0.75, as the game evaluates them), read once and cached under the app's Cache folder as plain data
// (StickRest, `poses/stick/rig_<f|m>.tsv`, keyed by the source files' stamps); later sessions build the rig from the cache
// without opening a bundle. Nothing of the install is shipped.
//
// A pose is applied as the app applies a pose file to a character, through the same code: the import plan (key mapping,
// AI remap, modes and groups, the IK conversion on this skeleton), the FK writer (the applied groups' local rotations,
// sync partners included) and the IK stage on that FK base (body, torso, two-bone limbs, end rotations). Not reproduced:
// the animation of the file's anime record (97 % of the files without an image apply the Body FK group, which overwrites
// every drawn bone; the others are posed from the rest pose), the hips height offset (a vertical shift the view's fit
// removes), the breast, hair and skirt groups (not drawn). The rig is reset to its rest pose before every pose. Main thread.
using System;
using System.Collections.Generic;
using System.IO;
using HS2Next.Content;
using HS2Next.Chara;
using HS2Next.Content.Studio;
using HS2Next.Import.Pose;
using HS2Next.Motion;
using HS2Next.Motion.Fk;
using HS2Next.Motion.Ik;
using HS2Next.Save;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Core;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HS2Next.UI.Panels
{
    /// <summary>What applying one pose did (tests, diagnostics).</summary>
    public struct PoseStickApplyInfo
    {
        public bool FkApplied, IkApplied;
        /// <summary>The IK conversion's scale (1, 10; 0 = rejected / none).</summary>
        public int IkScale;
        /// <summary>FK values written (applied groups only).</summary>
        public int FkWritten;
        /// <summary>The import's IK conversion on this skeleton (null when the file has no enableIK).</summary>
        public PoseIkResult Ik;
        /// <summary>The IK step's solve (reach clamps, residuals), or null.</summary>
        public IkSolveInfo Solve;
        public double PlanMs, ApplyMs;
    }

    public sealed class PoseStickRig : IDisposable
    {
        /// <summary>The version of the rig's rest data (part of the rig cache stamp).</summary>
        public const int RestVersion = 1;
        public const string PrefabBundle = "studio/base/00.unity3d", PrefabFallback = "chara/oo_base.unity3d", PrefabName = "p_cf_anim";
        public const string ShapeBundle = "list/customshape.unity3d";

        public int Sex { get; }
        public StickRest Rest { get; }
        /// <summary>"cache" (read from the cache file) or "install" (read from the prefab and written to the cache).</summary>
        public string Source { get; private set; }
        public GameObject Root { get; private set; }
        public FkBoneTable Fk { get; private set; }
        public IkBones IkBones { get; private set; }
        /// <summary>Milliseconds to build (rest data and transforms).</summary>
        public double BuildMs { get; private set; }

        readonly Transform[] transforms;
        readonly Dictionary<string, Transform> bones = new Dictionary<string, Transform>(StringComparer.Ordinal);
        readonly Transform[] jointBones = new Transform[StickJoints.Count];
        readonly FkStage fkStage;
        readonly PoseIkBody ikBody;
        readonly PoseKeyMap map;
        readonly IReadOnlyList<FkRow> rows;
        readonly double headUnit, restHeight;
        readonly List<string> log = new List<string>();

        public IReadOnlyList<string> Log => log;

        PoseStickRig(int sex, StickRest rest, IReadOnlyList<FkRow> fkRows)
        {
            Sex = sex;
            Rest = rest;
            rows = fkRows ?? new FkRow[0];
            // the transforms from the rest data, under a hidden root standing for the character root
            Root = new GameObject("HS2Next pose stick rig " + (sex == 0 ? "m" : "f")) { hideFlags = HideFlags.HideAndDontSave };
            transforms = new Transform[rest.Count];
            for (int i = 0; i < rest.Count; i++)
            {
                var go = new GameObject(rest.Names[i]) { hideFlags = HideFlags.HideAndDontSave };
                var t = go.transform;
                int p = rest.Parents[i];
                t.SetParent(p < 0 ? Root.transform : transforms[p], false);
                transforms[i] = t;
                if (!bones.ContainsKey(rest.Names[i])) bones[rest.Names[i]] = t;
            }
            ResetToRest();
            for (int j = 0; j < StickJoints.Count; j++)
                if (StickJoints.Bones[j] != null) bones.TryGetValue(StickJoints.Bones[j], out jointBones[j]);
            Fk = FkBoneTable.Discover(new FkDiscovery { Root = Root.transform, BodyBones = bones, Female = sex != 0, Rows = rows }, log);
            fkStage = new FkStage(Fk) { Enabled = true };
            IkBones = IkBones.Discover(bones, Root.transform);
            ikBody = BodyOf();
            map = PoseImport.MapFor(rows, null, new PoseImportOptions().AiBreastRemap);
            var rp = Read();
            headUnit = rp.HeadUnit;
            restHeight = rp.RestHeight;
        }

        /// <summary>
        /// The rig of one sex: rest data from the cache when its stamp matches the install, else from the install's prefab
        /// (written to the cache). Null when neither is available (no install, no cache); <paramref name="problem"/> says why.
        /// </summary>
        public static PoseStickRig Create(ContentCatalog catalog, StudioDb db, int sex, string cacheDir, out string problem)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            problem = null;
            sex = sex == 0 ? 0 : 1;
            string stamp = catalog != null ? StampOf(catalog, sex) : null;
            string file = RigFile(cacheDir, sex);
            StickRest rest = null;
            string source = null;
            if (file != null)
            {
                var cached = StickRest.Read(file, out _);
                if (cached != null && cached.Sex == sex && (stamp == null || cached.Stamp == stamp)) { rest = cached; source = "cache"; }
            }
            if (rest == null && catalog != null)
            {
                rest = FromInstall(catalog, sex, stamp, out problem);
                if (rest != null)
                {
                    source = "install";
                    if (file != null)
                    {
                        string err = rest.Write(file);
                        if (err != null) problem = "rig cache not written: " + err;
                    }
                }
            }
            if (rest == null) { problem = problem ?? "no skeleton (no install and no cached rig)"; return null; }
            var rig = new PoseStickRig(sex, rest, FkRows.From(db)) { Source = source };
            rig.BuildMs = sw.Elapsed.TotalMilliseconds;
            return rig;
        }

        /// <summary>The rig cache file of a sex under a cache folder (null without one).</summary>
        public static string RigFile(string cacheDir, int sex) =>
            string.IsNullOrEmpty(cacheDir) ? null : Path.Combine(cacheDir, "poses", "stick", sex == 0 ? "rig_m.tsv" : "rig_f.tsv");

        /// <summary>The stamp of the rest data: the prefab's and the shape lists' source files, the shape map, the version.</summary>
        public static string StampOf(ContentCatalog catalog, int sex)
        {
            string prefab = catalog.SourceStampOf(null, PrefabBundle, PrefabName) ?? catalog.SourceStampOf(null, PrefabFallback, PrefabName);
            string shape = catalog.SourceStampOf(null, ShapeBundle, "cf_anmShapeBody");
            if (mapHash == null) mapHash = PoseThumbKey.Hash(Resources.Load<TextAsset>("shape_body_map")?.text ?? "");
            return $"v{RestVersion}|sex {sex}|{prefab ?? "-"}|{shape ?? "-"}|map {mapHash}";
        }

        // the shape map is part of the build: hashed once per process
        static string mapHash;

        // The prefab's transforms copied into a bare tree, the default shape applied there, read back as data.
        static StickRest FromInstall(ContentCatalog catalog, int sex, string stamp, out string problem)
        {
            problem = null;
            var prefab = catalog.LoadAsset<GameObject>(PrefabBundle, PrefabName) ?? catalog.LoadAsset<GameObject>(PrefabFallback, PrefabName);
            if (!prefab) { problem = "no skeleton prefab in the install"; return null; }
            var temp = new GameObject("HS2Next pose stick rest") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var copy = new List<Transform>();
                var parents = new List<int>();
                void Walk(Transform src, Transform dstParent, int parentIndex)
                {
                    var go = new GameObject(src.name) { hideFlags = HideFlags.HideAndDontSave };
                    var t = go.transform;
                    t.SetParent(dstParent, false);
                    t.localPosition = src.localPosition;
                    t.localRotation = src.localRotation;
                    t.localScale = src.localScale;
                    copy.Add(t);
                    parents.Add(parentIndex);
                    int me = copy.Count - 1;
                    for (int i = 0; i < src.childCount; i++) Walk(src.GetChild(i), t, me);
                }
                Walk(prefab.transform, temp.transform, -1);
                // the default body shape of the sex (the body unit's values at build: 0.5 each, the nipple slider 0; males at height 0.75)
                Transform jRoot = null;
                foreach (var t in copy) if (t.name == "cf_J_Root") { jRoot = t; break; }
                var keys = catalog.LoadAsset<TextAsset>(ShapeBundle, "cf_anmShapeBody");
                var info = catalog.LoadAsset<TextAsset>(ShapeBundle, "cf_custombody");
                if (jRoot && keys && info)
                {
                    var shape = ShapeEngine.Body(jRoot, keys.bytes, info.text);
                    var v = BodyData.DefaultShape();
                    if (sex == 0 && v.Length > 0) v[0] = 0.75f;
                    for (int i = 0; i < v.Length; i++) shape.SetValue(i, i == 32 ? Mathf.Lerp(0, 0.8f, v[i]) : v[i]);
                    shape.Apply();
                }
                else problem = "body shape lists missing: the rig keeps the prefab's proportions";
                var rest = new StickRest { Sex = sex, Stamp = stamp ?? "" };
                for (int i = 0; i < copy.Count; i++)
                {
                    var t = copy[i];
                    var p = t.localPosition; var q = t.localRotation; var s = t.localScale;
                    rest.Add(t.name, parents[i], new[] { p.x, p.y, p.z }, new[] { q.x, q.y, q.z, q.w }, new[] { s.x, s.y, s.z });
                }
                return rest;
            }
            finally { Kill(temp); }
        }

        /// <summary>Every transform back to its rest values.</summary>
        public void ResetToRest()
        {
            for (int i = 0; i < transforms.Length; i++)
            {
                var t = transforms[i];
                var p = Rest.Pos[i]; var q = Rest.Rot[i]; var s = Rest.Scale[i];
                t.localPosition = new Vector3(p[0], p[1], p[2]);
                t.localRotation = new Quaternion(q[0], q[1], q[2], q[3]);
                t.localScale = new Vector3(s[0], s[1], s[2]);
            }
        }

        // the character geometry the IK conversion of the import reads (as PoseIkBody.Of reads a built character), at rest
        PoseIkBody BodyOf()
        {
            var b = new PoseIkBody { Complete = true };
            var root = Root.transform;
            Vector3 P(string name)
            {
                if (bones.TryGetValue(name, out var t) && t) return root.InverseTransformPoint(t.position);
                b.Complete = false;
                return Vector3.zero;
            }
            string[,] limbs =
            {
                { "cf_J_ArmUp00_L", "cf_J_ArmLow01_L", "cf_J_Hand_L" }, { "cf_J_ArmUp00_R", "cf_J_ArmLow01_R", "cf_J_Hand_R" },
                { "cf_J_LegUp00_L", "cf_J_LegLow01_L", "cf_J_Foot01_L" }, { "cf_J_LegUp00_R", "cf_J_LegLow01_R", "cf_J_Foot01_R" },
            };
            for (int k = 0; k < 4; k++)
            {
                var a = P(limbs[k, 0]); var m = P(limbs[k, 1]); var e = P(limbs[k, 2]);
                b.L1[k] = (m - a).magnitude;
                b.L2[k] = (e - m).magnitude;
            }
            b.Hips = P("cf_J_Hips");
            b.Spine01 = P("cf_J_Spine01");
            b.ThighL = P("cf_J_LegUp00_L");
            b.ThighR = P("cf_J_LegUp00_R");
            return b;
        }

        static readonly PoseImportOptions PlanOptions = new PoseImportOptions
        {
            Anime = false, Expression = false, Correction = false, Provenance = false, CaptureMissing = false, HipsOffset = false,
        };

        /// <summary>Poses the rig as the app poses a character with this file and returns the figure.</summary>
        public StickPose Apply(PoseFile pose, out PoseStickApplyInfo info)
        {
            info = default;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            ResetToRest();
            var plan = PoseImport.Plan(new PosePlanInput
            {
                Pose = pose, Rows = rows, Map = map, Sex = Sex, Current = new CharaNodeState(), Options = PlanOptions,
                IkBody = pose.EnableIK ? ikBody : null,
            });
            info.PlanMs = sw.Elapsed.TotalMilliseconds;
            if (plan.Report.Error != null) throw new InvalidOperationException(plan.Report.Error);
            // FK: the values of every table bone (partner keys as the FK layer reads them), the applied groups written
            var values = plan.Bones;
            foreach (var b in Fk.Bones)
            {
                if (values != null && (values.TryGetValue(b.Key, out var v) || (b.PartnerKey != null && values.TryGetValue(b.PartnerKey, out v))))
                    b.Set(new Vector3(v.X, v.Y, v.Z));
                else b.Clear();
            }
            bool anyFk = false;
            for (int g = 0; g < FkGroupInfo.Count; g++)
            {
                fkStage.Applied[g] = plan.FkEnabled && plan.FkGroups[g];
                anyFk |= fkStage.Applied[g];
            }
            if (anyFk)
            {
                fkStage.WriteApplied();
                info.FkApplied = true;
                for (int g = 0; g < FkGroupInfo.Count; g++)
                    if (fkStage.Applied[g]) foreach (var b in Fk.Of((FkGroup)g)) if (b.HasValue && b.Used && b.Bone) info.FkWritten++;
            }
            // IK on the FK base: the stage of the app (latch, snapshots of missing targets, the solver), one step
            if (plan.IkEnabled)
            {
                var ik = new IkStage(Root.transform, IkBones, null);
                for (int g = 0; g < IkGroupInfo.Count; g++) ik.Applied[g] = plan.IkGroups[g];
                if (plan.IkTargets != null)
                    for (int i = 0; i < IkGroupInfo.TargetCount && i < plan.IkTargets.Length; i++)
                    {
                        var t = plan.IkTargets[i];
                        if (t == null) ik.ClearTarget((IkTargetId)i);
                        else ik.SetTarget((IkTargetId)i, new Vector3(t.Pos.X, t.Pos.Y, t.Pos.Z),
                            IkGroupInfo.UsesRotation((IkTargetId)i) ? new Vector3(t.Rot.X, t.Rot.Y, t.Rot.Z) : Vector3.zero);
                    }
                ik.Refresh();
                if (ik.Enabled)
                {
                    ik.Step(new MotionFrame(0f, 0f, 0f, 0));
                    info.IkApplied = ik.LastSolve.Valid;
                    info.Solve = new IkSolveInfo();
                    info.Solve.CopyFrom(ik.LastSolve);
                }
                info.IkScale = plan.Ik?.Scale ?? 0;
                info.Ik = plan.Ik;
            }
            var res = Read();
            info.ApplyMs = sw.Elapsed.TotalMilliseconds;
            return res;
        }

        /// <summary>The figure of the rig's current pose (root space).</summary>
        public StickPose Read()
        {
            var pose = new StickPose();
            var root = Root.transform;
            for (int j = 0; j < StickJoints.Count; j++)
            {
                var t = jointBones[j];
                if (!t) continue;
                var p = root.InverseTransformPoint(t.position);
                pose.Set(j, new StickVec(p.x, p.y, p.z));
            }
            double unit = headUnit;
            var head = jointBones[StickJoints.Head];
            var neck = jointBones[StickJoints.Neck];
            if (unit <= 0 && head && neck) unit = (head.position - neck.position).magnitude / Mathf.Max(1e-6f, root.lossyScale.y);
            pose.HeadUnit = unit;
            if (head && unit > 0)
            {
                // a direction in the head bone's frame (rotation only) scaled by the rest neck → head distance
                var q = Quaternion.Inverse(root.rotation) * head.rotation;
                var o = root.InverseTransformPoint(head.position);
                StickVec At(StickVec l)
                {
                    var d = q * new Vector3((float)l.X, (float)l.Y, (float)l.Z);
                    return new StickVec(o.x + d.x * unit, o.y + d.y * unit, o.z + d.z * unit);
                }
                pose.Set(StickJoints.HeadCentre, At(StickJoints.HeadCentreLocal));
                pose.Set(StickJoints.HeadTop, At(StickJoints.HeadTopLocal));
                pose.Set(StickJoints.Face, At(StickJoints.FaceLocal));
            }
            if (restHeight > 0) pose.RestHeight = restHeight;
            else
            {
                double top = double.MinValue, bottom = double.MaxValue;
                for (int j = 0; j < StickJoints.Count; j++)
                {
                    if (!pose.Has[j]) continue;
                    top = Math.Max(top, pose.P[j].Y);
                    bottom = Math.Min(bottom, pose.P[j].Y);
                }
                pose.RestHeight = top > bottom ? top - bottom : 0;
            }
            return pose;
        }

        static void Kill(Object o)
        {
            if (!o) return;
            if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o);
        }

        public void Dispose()
        {
            Kill(Root);
            Root = null;
        }
    }
}
