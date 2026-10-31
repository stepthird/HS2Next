// IkSolverTest, fix-round measurements of Phase 3.4 package A (3.4 impl notes):
//   R8  the FK compare poses (Tools/compare_poses.json) → FK → "IK from pose" → FK off, IK on (IkDriverTest D8's protocol)
//       with diagnostics of the hips turn (thigh roots) and the limb local angles per limb orientation mode
//   R2  HS2's recordings of the IK comparison (Cache/compare/ik: IK2 and P1–P8, read as data): the recorded targets / pose files
//       solved on BR-Chan, limb bone rotations (cf_J_Root space, twist about the bone axis, local) against HS2's dump per mode
// Run with -fix (adds both to IkSolverTest.Run; the scene IK driver is on for them). Info only, except the checks named below.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HS2Next.Content;
using HS2Next.Motion;
using HS2Next.Motion.DynamicBones;
using HS2Next.Motion.Ik;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class IkSolverTest
{
    static readonly IkLimbOrientation[] Modes = { IkLimbOrientation.SwingOnly, IkLimbOrientation.BendPlane };
    static readonly string[] LimbBones = { "cf_J_ArmUp00_L", "cf_J_ArmLow01_L", "cf_J_Hand_L", "cf_J_ArmUp00_R", "cf_J_ArmLow01_R", "cf_J_Hand_R",
                                           "cf_J_LegUp00_L", "cf_J_LegLow01_L", "cf_J_Foot01_L", "cf_J_LegUp00_R", "cf_J_LegLow01_R", "cf_J_Foot01_R" };

    sealed class OpsEnv : IDisposable
    {
        public readonly SceneSession S;
        public readonly SceneRuntime Rt;
        public readonly IkOps Ik;
        public readonly FkOps Fk;
        public readonly KinematicCopies Copies;
        public OpsEnv()
        {
            S = SceneRuntimeTest.NewSession(services, 0x2d2d2d2dUL);
            Rt = new SceneRuntime(S, services, null, false);
            Ik = new IkOps(Rt); Fk = new FkOps(Rt); Copies = new KinematicCopies(Rt);
        }
        public string AddStill(AnimeRef anime)
        {
            var r = Rt.Ops.AddCharaFromCard(femaleCard);
            if (r == null || !r.Ok) throw new Exception("add: " + r);
            Rt.ApplyNow();
            var c = Rt.FindChara(r.NewId);
            foreach (var d in c.Motion.GetAll<DynamicBoneStage>()) d.Enabled = false;
            foreach (var b in c.Motion.GetAll<BustStage>()) b.Enabled = false;
            S.SetAnimation(r.NewId, anime);
            Set(r.NewId, "chara.anime.speed", 0f);
            Set(r.NewId, "chara.look.neck", 4);
            Rt.ApplyNow();
            Step(2);
            return r.NewId;
        }
        public void Set(string id, string path, object v)
        {
            var r = S.Set(id, path, v);
            if (r == null || !r.Ok) throw new Exception($"set {path}: {r}");
        }
        public CharaRealization C(string id) => Rt.FindChara(id);
        public void Step(int n = 1) { for (int i = 0; i < n; i++) Rt.Tick(Dt); }
        public void Dispose() => Rt.Dispose();
    }

    static Transform BoneOf(CharaRealization r, string n) => r.Context.BodyBones.TryGetValue(n, out var t) ? t : null;
    static float Median(List<float> l) => l.Count == 0 ? float.NaN : l.OrderBy(x => x).ElementAt(l.Count / 2);
    static string Mx(List<float> l) => l.Count == 0 ? "-" : $"median {G(Median(l))}, p95 {G(Pct(l, 0.95))}, max {G(l.Max())}";

    // ================================================================ R8

    static void R8()
    {
        Section("R8", "fix round: FK compare poses → FK → IK from pose → FK off, IK on (D8 protocol) per limb orientation");
        string json = Path.Combine(ContentSettings.ProjectRoot, "Tools", "compare_poses.json");
        string root = Path.Combine(services.Hs2Root, "UserData", "studio", "pose");
        var poses = (JArray)JObject.Parse(File.ReadAllText(json))["poses"];
        var ang = Modes.ToDictionary(m => m, m => new List<float>());
        var perBone = Modes.ToDictionary(m => m, m => LimbBones.ToDictionary(b => b, b => new List<float>()));
        var thighErr = new List<float>(); var hipsErr = new List<float>();
        var mode0 = IkOptions.LimbOrientation;
        try
        {
            foreach (var p in poses)
            {
                var path = Path.Combine(root, ((string)p["path"]).Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path)) { Info("missing " + p["path"]); continue; }
                using (var e = new OpsEnv())
                {
                    IkOptions.LimbOrientation = IkLimbOrientation.SwingOnly;
                    var id = e.AddStill(Idle);
                    PoseImport.ApplyFile(e.Rt, id, path);
                    e.Set(id, "chara.anime.speed", 0f); e.Rt.ApplyNow();
                    e.Step(2);
                    var r = e.C(id);
                    var rt = r.Root.transform;
                    var before = LimbBones.Select(x => BoneOf(r, x).localRotation).ToArray();
                    var kosiFk = new[] { "cf_J_Hips", "cf_J_Kosi01", "cf_J_Kosi02" }.Select(x => BoneOf(r, x).localRotation).ToArray();
                    var thighFk = new[] { "cf_J_LegUp00_L", "cf_J_LegUp00_R" }.Select(x => rt.InverseTransformPoint(BoneOf(r, x).position)).ToArray();
                    string bends = "";
                    {
                        var bb = IkBones.Discover(r.Context.BodyBones, rt);
                        foreach (var g in new[] { IkGroup.LeftArm, IkGroup.RightArm, IkGroup.LeftLeg, IkGroup.RightLeg })
                        {
                            var l = bb.Limb(g);
                            Vector3 A = l.Root.position, M = l.Mid.position, E = l.End.position;
                            float beta = 180f - AngleV(A - M, E - M);
                            var u = (E - A).normalized;
                            float side = Vector3.Dot(Reject(M - A, u), l.Root.parent.rotation * st0RestBend(r, g));
                            bends += $" {g} {G(beta)}°{(side >= 0 ? "+" : "-")}";
                        }
                    }
                    e.Copies.IkFromCurrent(id);
                    e.Fk.SetEnabled(id, false);
                    e.Ik.SetEnabled(id, true);
                    e.Step(1);
                    var ikc = IkDriver.Of(r);
                    var st = r.Motion.Get<IkStage>();
                    // hips-turn diagnostics (independent of the limb mode)
                    var thighNow = new[] { "cf_J_LegUp00_L", "cf_J_LegUp00_R" }.Select(x => rt.InverseTransformPoint(BoneOf(r, x).position)).ToArray();
                    float te = Mathf.Max((thighNow[0] - thighFk[0]).magnitude, (thighNow[1] - thighFk[1]).magnitude);
                    thighErr.Add(te);
                    float he = (rt.InverseTransformPoint(BoneOf(r, "cf_J_Hips").position) - st.Targets[0].Pos).magnitude;
                    hipsErr.Add(he);
                    var kosiNow = new[] { "cf_J_Hips", "cf_J_Kosi01", "cf_J_Kosi02" }.Select(x => BoneOf(r, x).localRotation).ToArray();
                    // frame residual: thigh frame of the bones vs the targets
                    var hp = BoneOf(r, "cf_J_Hips").position;
                    IkSolver.Frame(BoneOf(r, "cf_J_LegUp00_R").position - BoneOf(r, "cf_J_LegUp00_L").position, hp - (BoneOf(r, "cf_J_LegUp00_R").position + BoneOf(r, "cf_J_LegUp00_L").position) * 0.5f, out var fa);
                    var pl = rt.TransformPoint(st.Targets[(int)IkTargetId.LeftThigh].Pos); var pr = rt.TransformPoint(st.Targets[(int)IkTargetId.RightThigh].Pos);
                    IkSolver.Frame(pr - pl, rt.TransformPoint(st.Targets[0].Pos) - (pl + pr) * 0.5f, out var ft);
                    var ls = st.LastSolve;
                    string sinfo = $"[solve valid {ls.Valid}, applied {string.Join("", st.Applied.Select(x => x ? "1" : "0"))}, body degenerate {ls.BodyDegenerate}, torso {G(ls.TorsoResidual)}°, " +
                                   $"thigh targets L {F(st.Targets[(int)IkTargetId.LeftThigh].Pos)} R {F(st.Targets[(int)IkTargetId.RightThigh].Pos)} body {F(st.Targets[0].Pos)}; bones L {F(thighNow[0])} R {F(thighNow[1])}]";
                    var line = $"case {p["case"]} {p["path"]}: FK bends{bends}; thigh roots off {G(te)}, hips off {G(he)}, thigh frame {G(IkSolver.AngleBetween(fa, ft))}° {(te > 0.2f ? sinfo : "")}, " +
                               $"local change hips {G(Angle(kosiFk[0], kosiNow[0]))}° Kosi01 {G(Angle(kosiFk[1], kosiNow[1]))}° Kosi02 {G(Angle(kosiFk[2], kosiNow[2]))}°";
                    foreach (var m in Modes)
                    {
                        IkOptions.LimbOrientation = m;
                        e.Step(1);
                        var a = LimbBones.Select((x, i) => Angle(BoneOf(r, x).localRotation, before[i])).ToList();
                        ang[m].AddRange(a);
                        for (int i = 0; i < LimbBones.Length; i++) perBone[m][LimbBones[i]].Add(a[i]);
                        line += $"; {m} median {G(Median(a))}° max {G(a.Max())}°";
                        if (Has("-perbone")) line += " [" + string.Join(" ", LimbBones.Select((x, i) => x.Replace("cf_J_", "") + " " + G(a[i]))) + "]";
                    }
                    var groupsOff = Enumerable.Range(0, 5).Where(g => !st.Applied[g]).Select(g => ((IkGroup)g).ToString()).ToList();
                    if (groupsOff.Count > 0)
                    {
                        foreach (var g in IkGroupInfo.MemberNames) e.Set(id, "chara.ik.groups." + g, true);
                        e.Rt.ApplyNow();
                        e.Step(1);
                        var tn = new[] { "cf_J_LegUp00_L", "cf_J_LegUp00_R" }.Select(x => rt.InverseTransformPoint(BoneOf(r, x).position)).ToArray();
                        line += $"; IK groups off in the file: {string.Join(",", groupsOff)} - with every group on: thigh roots off {G(Mathf.Max((tn[0] - thighFk[0]).magnitude, (tn[1] - thighFk[1]).magnitude))}";
                    }
                    Info(line);
                }
            }
        }
        finally { IkOptions.LimbOrientation = mode0; }
        Info("thigh roots vs the FK pose: " + Mx(thighErr) + "; hips vs Body target: " + Mx(hipsErr));
        foreach (var m in Modes)
        {
            Info($"{m}: limb local angles vs the FK pose {Mx(ang[m])}");
            foreach (var b in LimbBones) Info($"   {b}: {Mx(perBone[m][b])}");
        }
    }

    /// <summary>The rest bend direction (root's parent frame) of the scene driver's IkBones (discovered at build).</summary>
    static Vector3 st0RestBend(CharaRealization r, IkGroup g)
    {
        var st = r.Motion.Get<IkStage>();
        return st != null ? st.Bones.Limb(g).RestBend : Vector3.zero;
    }

    // ================================================================ R2

    static Quaternion Q(JToken a) => new Quaternion((float)a[0], (float)a[1], (float)a[2], (float)a[3]);
    static Vector3 V3(JToken a) => new Vector3((float)a[0], (float)a[1], (float)a[2]);

    /// <summary>cf_J_Root-space rotations of HS2's dump by bone name (first occurrence).</summary>
    static Dictionary<string, Quaternion> Hs2RootSpace(JObject dump)
    {
        var byPath = new Dictionary<string, Quaternion>(StringComparer.Ordinal);
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var b in (JArray)dump["bones"])
        {
            string path = (string)b["path"];
            byPath[path] = Q(b["lq"]);
            if (!names.ContainsKey((string)b["name"])) names[(string)b["name"]] = path;
        }
        var res = new Dictionary<string, Quaternion>(StringComparer.Ordinal);
        foreach (var kv in names)
        {
            var q = Quaternion.identity;
            if (kv.Value.Length > 0)
            {
                var parts = kv.Value.Split('/');
                string pre = "";
                foreach (var part in parts)
                {
                    pre = pre.Length == 0 ? part : pre + "/" + part;
                    if (byPath.TryGetValue(pre, out var lq)) q = q * lq;
                }
            }
            res[kv.Key] = q;
        }
        return res;
    }

    static float TwistAbout(Quaternion a, Quaternion b, Vector3 axisLocal)
    {
        var d = Quaternion.Inverse(a) * b;
        var ax = axisLocal.normalized;
        float proj = d.x * ax.x + d.y * ax.y + d.z * ax.z;
        float t = (float)(2 * Math.Atan2(proj, d.w) * 180 / Math.PI);
        if (t > 180) t -= 360; if (t < -180) t += 360;
        return Mathf.Abs(t);
    }

    static void R2()
    {
        Section("R2", "fix round: HS2's IK recordings (IK2, P1–P8) solved on BR-Chan per limb orientation, limb bones vs HS2's dump");
        string dir = Path.Combine(ContentSettings.ProjectRoot, "Cache", "compare", "ik");
        string root = Path.Combine(services.Hs2Root, "UserData", "studio", "pose");
        if (!Directory.Exists(dir)) { Info("no recordings"); return; }
        var cases = new[] { "IK2", "IK3", "IK5", "IK7", "P1", "P2", "P3", "P4", "P5", "P6", "P7", "P8" };
        var upper = new[] { "cf_J_ArmUp00_L", "cf_J_ArmUp00_R", "cf_J_LegUp00_L", "cf_J_LegUp00_R" };
        var lower = new[] { "cf_J_ArmLow01_L", "cf_J_ArmLow01_R", "cf_J_LegLow01_L", "cf_J_LegLow01_R" };
        var twistUp = Modes.ToDictionary(m => m, m => new List<float>());
        var twistLow = Modes.ToDictionary(m => m, m => new List<float>());
        var totalLimb = Modes.ToDictionary(m => m, m => new List<float>());
        var localLimb = Modes.ToDictionary(m => m, m => new List<float>());
        var mode0 = IkOptions.LimbOrientation;
        try
        {
            foreach (var cs in cases)
            {
                var f = Path.Combine(dir, cs + ".json");
                if (!File.Exists(f)) { Info("missing " + cs); continue; }
                var rec = JObject.Parse(File.ReadAllText(f));
                var fin = (JObject)rec["final"];
                var hs2 = Hs2RootSpace((JObject)fin["dump"]);
                var hs2Local = new Dictionary<string, Quaternion>(StringComparer.Ordinal);
                foreach (var b in (JArray)fin["dump"]["bones"]) if (!hs2Local.ContainsKey((string)b["name"])) hs2Local[(string)b["name"]] = Q(b["lq"]);
                using (var e = new OpsEnv())
                {
                    IkOptions.LimbOrientation = IkLimbOrientation.SwingOnly;
                    var clip = rec["clip"];
                    string id;
                    if (cs.StartsWith("P"))
                    {
                        id = e.AddStill(Idle);
                        var rel = (string)rec["steps"][0]["path"];
                        PoseImport.ApplyFile(e.Rt, id, Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)));
                        e.Set(id, "chara.anime.speed", 0f); e.Rt.ApplyNow();
                        e.Step(2);
                    }
                    else
                    {
                        id = e.AddStill(new AnimeRef(ContentIds.Vanilla, (int)clip["group"], (int)clip["category"], (int)clip["no"]));
                        e.Set(id, "chara.anime.time", (float)clip["normalizedTime"]);
                        foreach (var t in (JArray)fin["ik"]["targets"])
                        {
                            var pos = V3(t["pos"]); var rot = V3(t["rot"]);
                            e.Set(id, "chara.ik.targets." + (string)t["name"], new IkTarget { Pos = new Vec3(pos.x, pos.y, pos.z), Rot = new Vec3(rot.x, rot.y, rot.z) });
                        }
                        var act = (JArray)fin["ik"]["activeIK"];
                        for (int g = 0; g < 5; g++) e.Set(id, "chara.ik.groups." + IkGroupInfo.MemberNames[g], (bool)act[g]);
                        e.Set(id, "chara.fk.enabled", false);
                        e.Set(id, "chara.ik.enabled", true);
                        e.Rt.ApplyNow();
                        e.Step(2);
                    }
                    var r = e.C(id);
                    var cfRoot = BoneOf(r, "cf_J_Root");
                    var line = $"{cs}:";
                    foreach (var m in Modes)
                    {
                        IkOptions.LimbOrientation = m;
                        e.Step(1);
                        var tu = new List<float>(); var tl = new List<float>(); var tot = new List<float>(); var loc = new List<float>();
                        foreach (var n in upper.Concat(lower))
                        {
                            var b = BoneOf(r, n);
                            if (!b || !hs2.TryGetValue(n, out var qh)) continue;
                            var qn = Quaternion.Inverse(cfRoot.rotation) * b.rotation;
                            var child = b.GetChild(0);
                            foreach (Transform c in b) if (c.name.StartsWith("cf_J_ArmLow01") || c.name.StartsWith("cf_J_LegLow01") || c.name.StartsWith("cf_J_Hand_") || c.name.StartsWith("cf_J_LegLowRoll")) { child = c; break; }
                            float tw = TwistAbout(qh, qn, child.localPosition);
                            (upper.Contains(n) ? tu : tl).Add(tw);
                            tot.Add(Angle(qh, qn));
                            if (hs2Local.TryGetValue(n, out var lq)) loc.Add(Angle(lq, b.localRotation));
                        }
                        twistUp[m].AddRange(tu); twistLow[m].AddRange(tl); totalLimb[m].AddRange(tot); localLimb[m].AddRange(loc);
                        line += $" {m}: twist up {G(Median(tu))}/{G(tu.Max())}°, low {G(Median(tl))}/{G(tl.Max())}°, local {G(Median(loc))}/{G(loc.Max())}°;";
                    }
                    Info(line);
                }
            }
        }
        finally { IkOptions.LimbOrientation = mode0; }
        foreach (var m in Modes)
            Info($"{m}: upper-bone twist vs HS2 {Mx(twistUp[m])}; lower-bone twist {Mx(twistLow[m])}; limb bones (cf_J_Root space) {Mx(totalLimb[m])}; local {Mx(localLimb[m])}");
    }

    // ================================================================ RC clavicle guard (fix round 2)

    static void RC()
    {
        Section("RC", "fix round 2: clavicle aim guard variants vs HS2's recordings (clavicle and arm bones, cf_J_Root space)");
        string dir = Path.Combine(ContentSettings.ProjectRoot, "Cache", "compare", "ik");
        string root = Path.Combine(services.Hs2Root, "UserData", "studio", "pose");
        var variants = new (string name, float fmin, float fmax, float cap)[]
        {
            ("none", -1f, 0f, 180f), ("fade 0.25-0.5", 0.25f, 0.5f, 180f), ("fade 0.5-0.9", 0.5f, 0.9f, 180f),
            ("fade 0.4-0.7", 0.4f, 0.7f, 180f), ("fade 0.45-0.7", 0.45f, 0.7f, 180f), ("fade 0.5-0.7", 0.5f, 0.7f, 180f),
            ("cap 45", -1f, 0f, 45f), ("fade 0.5-0.9 cap 45", 0.5f, 0.9f, 45f),
        };
        var cases = new[] { "IK2", "IK3", "IK4", "IK5", "IK6", "IK7", "IK8", "IK9", "P1", "P2", "P3", "P4", "P5", "P6", "P7", "P8" };
        var clav = variants.ToDictionary(v => v.name, v => new List<float>());
        var arm = variants.ToDictionary(v => v.name, v => new List<float>());
        float f0 = IkSolver.ClavicleFadeMin, f1 = IkSolver.ClavicleFadeMax, c0 = IkSolver.ClavicleMaxAngle;
        try
        {
            foreach (var cs in cases)
            {
                var f = Path.Combine(dir, cs + ".json");
                if (!File.Exists(f)) continue;
                var rec = JObject.Parse(File.ReadAllText(f));
                var fin = (JObject)rec["final"];
                var hs2 = Hs2RootSpace((JObject)fin["dump"]);
                using (var e = new OpsEnv())
                {
                    var clip = rec["clip"];
                    string id;
                    if (cs.StartsWith("P"))
                    {
                        id = e.AddStill(Idle);
                        var rel = (string)rec["steps"][0]["path"];
                        PoseImport.ApplyFile(e.Rt, id, Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)));
                        e.Set(id, "chara.anime.speed", 0f); e.Rt.ApplyNow();
                    }
                    else
                    {
                        id = e.AddStill(new AnimeRef(ContentIds.Vanilla, (int)clip["group"], (int)clip["category"], (int)clip["no"]));
                        e.Set(id, "chara.anime.time", (float)clip["normalizedTime"]);
                        foreach (var t in (JArray)fin["ik"]["targets"])
                        {
                            var pos = V3(t["pos"]); var rot = V3(t["rot"]);
                            e.Set(id, "chara.ik.targets." + (string)t["name"], new IkTarget { Pos = new Vec3(pos.x, pos.y, pos.z), Rot = new Vec3(rot.x, rot.y, rot.z) });
                        }
                        var act = (JArray)fin["ik"]["activeIK"];
                        for (int g = 0; g < 5; g++) e.Set(id, "chara.ik.groups." + IkGroupInfo.MemberNames[g], (bool)act[g]);
                        e.Set(id, "chara.fk.enabled", false);
                        e.Set(id, "chara.ik.enabled", true);
                        e.Rt.ApplyNow();
                    }
                    e.Step(2);
                    var r = e.C(id);
                    var cfRoot = BoneOf(r, "cf_J_Root");
                    var st = r.Motion.Get<IkStage>();
                    string line = cs + ":";
                    foreach (var side in new[] { ("L", IkTargetId.LeftShoulder), ("R", IkTargetId.RightShoulder) })
                    {
                        var c = BoneOf(r, "cf_J_Shoulder_" + side.Item1).position; var up = BoneOf(r, "cf_J_ArmUp00_" + side.Item1).position;
                        var tw = r.Root.transform.TransformPoint(st.Targets[(int)side.Item2].Pos);
                        line += $" ratio {side.Item1} {G((tw - c).magnitude / (up - c).magnitude)}";
                    }
                    foreach (var v in variants)
                    {
                        IkSolver.ClavicleFadeMin = v.fmin; IkSolver.ClavicleFadeMax = v.fmax; IkSolver.ClavicleMaxAngle = v.cap;
                        e.Step(1);
                        var ca = new List<float>(); var aa = new List<float>();
                        foreach (var n in new[] { "cf_J_Shoulder_L", "cf_J_Shoulder_R" })
                            if (hs2.TryGetValue(n, out var qh)) ca.Add(Angle(qh, Quaternion.Inverse(cfRoot.rotation) * BoneOf(r, n).rotation));
                        foreach (var n in new[] { "cf_J_ArmUp00_L", "cf_J_ArmUp00_R", "cf_J_ArmLow01_L", "cf_J_ArmLow01_R", "cf_J_Hand_L", "cf_J_Hand_R" })
                            if (hs2.TryGetValue(n, out var qh)) aa.Add(Angle(qh, Quaternion.Inverse(cfRoot.rotation) * BoneOf(r, n).rotation));
                        clav[v.name].AddRange(ca); arm[v.name].AddRange(aa);
                        line += $"; {v.name}: clav {G(ca[0])}/{G(ca[1])}° arm max {G(aa.Max())}°";
                    }
                    Info(line);
                }
            }
        }
        finally { IkSolver.ClavicleFadeMin = f0; IkSolver.ClavicleFadeMax = f1; IkSolver.ClavicleMaxAngle = c0; }
        foreach (var v in variants) Info($"{v.name}: clavicles vs HS2 {Mx(clav[v.name])}; arm bones {Mx(arm[v.name])}");
    }
}
