// FK writer tests (F1–F13; package F, Phase 3.4 package B).
// Run: -executeMethod FkWriterTest.Run [-only F1,F5] (headless, -nographics, edit mode, ManualStepping).
// Cards: the SFW visual targets of Tools/targets.json (BR-Chan female, HERO107 male); F13 registers test FK node providers
// and a test build hook for the run (removed afterwards). Characters are added through SceneOps.AddCharaFromCard into a session + runtime without drivers; the FK
// driver is attached by the test (CharaPoseDrivers.Fk, StudioDefaultPoseScope.SuspendPoseDrivers) when it is not the
// default yet. Report: Spike/out/pose/fk_report.txt, first line "SUMMARY: N PASS, M FAIL"; EditorApplication.Exit(1)
// on any FAIL.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.Chara.Ext;
using HS2Next.Content;
using HS2Next.Content.Studio;
using HS2Next.Data;
using HS2Next.Data.Ext;
using HS2Next.Motion;
using HS2Next.Motion.DynamicBones;
using HS2Next.Motion.Fk;
using HS2Next.Motion.Look;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static class FkWriterTest
{
    const float Dt = 1f / 60f;
    static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "pose");
    static StringBuilder rep;
    static int passes, fails;
    static string current = "";
    static readonly Dictionary<string, (int pass, int fail)> perTest = new Dictionary<string, (int, int)>();
    static string[] only;
    static RuntimeServices services;
    static string femaleCard, maleCard;
    static readonly AnimeRef Idle = new AnimeRef(ContentIds.Vanilla, 19, 0, 0);

    static string Arg(string name)
    {
        var a = TestArgs.Get();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
    }

    static void Section(string id, string title) { current = id; rep.AppendLine(); rep.AppendLine($"== {id} {title}"); }
    static void Info(string s) => rep.AppendLine("   " + s);
    static void Check(string name, bool ok, string detail = "")
    {
        rep.AppendLine($"{(ok ? "PASS" : "FAIL")}  [{current}] {name}{(string.IsNullOrEmpty(detail) ? "" : "  " + detail)}");
        if (ok) passes++; else fails++;
        perTest.TryGetValue(current, out var v);
        perTest[current] = ok ? (v.pass + 1, v.fail) : (v.pass, v.fail + 1);
    }

    static void Guard(string id, Action a)
    {
        if (only != null && !only.Contains(id)) return;
        current = id;
        var fz = FkOptions.MissingBonesZero; var fi = FkOptions.ResetToIdentity; var fh = FkOptions.HipsOffset;
        try { a(); }
        catch (Exception e) { Check("exception", false, e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); }
        finally { FkOptions.MissingBonesZero = fz; FkOptions.ResetToIdentity = fi; FkOptions.HipsOffset = fh; }
    }

    public static void Run()
    {
        rep = new StringBuilder(); passes = fails = 0; perTest.Clear();
        var o = Arg("-only");
        only = o == null ? null : o.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
        Directory.CreateDirectory(OutDir);
        var sw = Stopwatch.StartNew();
        var oldFk = CharaPoseDrivers.Fk;
        var oldSuspend = StudioDefaultPoseScope.SuspendPoseDrivers;
        var head = new StringBuilder();
        try
        {
            if (!(CharaPoseDrivers.Fk is FkDriver)) CharaPoseDrivers.Fk = FkDriver.Instance;
            if (StudioDefaultPoseScope.SuspendPoseDrivers == null) StudioDefaultPoseScope.SuspendPoseDrivers = CharaPoseDrivers.SuspendAll;
            services = SceneRuntimeTest.NewServices(head);
            var t = TestTargets.Load();
            femaleCard = TestTargets.Abs(t.visual.female);
            maleCard = TestTargets.Abs(t.visual.male);
            rep.AppendLine($"FK writer test (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType}); driver {(oldFk == null ? "attached by the test" : "registered by default")}, scope seam {(oldSuspend == null ? "attached by the test" : "default")}");
            rep.Append(head);
            Guard("F1", F1);
            Guard("F2", F2);
            Guard("F3", F3);
            Guard("F4", F4);
            Guard("F5", F5);
            Guard("F6", F6);
            Guard("F7", F7);
            Guard("F8", F8);
            Guard("F9", F9);
            Guard("F10", F10);
            Guard("F11", F11);
            Guard("F12", F12);
            Guard("F13", F13);
            Guard("F14", F14);
        }
        catch (Exception e) { current = "run"; Check("run (exception)", false, e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); }
        finally
        {
            CharaPoseDrivers.Fk = oldFk;
            StudioDefaultPoseScope.SuspendPoseDrivers = oldSuspend;
            if (probe) Object.DestroyImmediate(probe.gameObject);
            LookTargets.DefaultCamera = null;
        }
        var sum = new StringBuilder();
        sum.AppendLine($"SUMMARY: {passes} PASS, {fails} FAIL ({sw.Elapsed.TotalSeconds:F0} s)");
        foreach (var kv in perTest.OrderBy(k => k.Key, StringComparer.Ordinal)) sum.AppendLine($"  {kv.Key}: {kv.Value.pass} pass, {kv.Value.fail} fail");
        rep.Insert(0, sum.ToString());
        File.WriteAllText(Path.Combine(OutDir, "fk_report.txt"), rep.ToString());
        services?.Catalog?.UnloadAll();
        Debug.Log($"FK WRITER TEST DONE: {passes} PASS, {fails} FAIL");
        if (fails > 0) TestExit.Exit(1);
    }

    // ================================================================ fixture

    sealed class Env : IDisposable
    {
        public readonly SceneSession S;
        public readonly SceneRuntime Rt;
        public readonly FkOps Ops;

        public Env(ulong seed = 0xf4f4f4f4UL)
        {
            S = SceneRuntimeTest.NewSession(services, seed);
            Rt = new SceneRuntime(S, services, null, false);
            Ops = new FkOps(Rt);
        }

        public string Add(string card)
        {
            var r = Rt.Ops.AddCharaFromCard(card);
            if (r == null || !r.Ok) throw new Exception("add " + Path.GetFileName(card) + ": " + r);
            Rt.ApplyNow();
            return r.NewId;
        }

        public CharaRealization C(string id) => Rt.FindChara(id);
        public FkCharacter Fk(string id) => FkDriver.Of(C(id));
        public CharaNodeState St(string id) => (CharaNodeState)S.Model.Find(id).State;
        public void Set(string id, string path, object v)
        {
            var r = S.Set(id, path, v);
            if (r == null || !r.Ok) throw new Exception($"set {path}: {r}");
        }
        public void Step(int n = 1) { for (int i = 0; i < n; i++) Rt.Tick(Dt); }
        public void Dispose() => Rt.Dispose();
    }

    static Vec3 V(Vector3 v) => new Vec3(v.x, v.y, v.z);
    static Vector3 U(Vec3 v) => new Vector3(v.X, v.Y, v.Z);
    static bool Bits(Quaternion a, Quaternion b) => a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;
    static Transform probe;
    /// <summary>What a Transform stores for <paramref name="q"/> (Unity normalises on set, so the readback of Euler(v) can
    /// differ from Quaternion.Euler(v) in the last bit): the reference for "bit for bit".</summary>
    static Quaternion Stored(Quaternion q)
    {
        if (!probe) probe = new GameObject("fk_test_probe") { hideFlags = HideFlags.HideAndDontSave }.transform;
        probe.localRotation = q;
        return probe.localRotation;
    }
    static bool Bits(Vector3 a, Vector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;
    static float Angle(Quaternion a, Quaternion b)
    {
        var d = Quaternion.Inverse(a) * b;
        double v = Math.Sqrt((double)d.x * d.x + (double)d.y * d.y + (double)d.z * d.z);
        return (float)(2 * Math.Atan2(v, Math.Abs((double)d.w)) * 180 / Math.PI);
    }
    static string F(Vector3 v) => $"({v.x:G6}, {v.y:G6}, {v.z:G6})";

    /// <summary>A distinctive deterministic value per key (degrees; never near the rest pose).</summary>
    static Vec3 ValueFor(string key, int salt = 0)
    {
        int h = salt * 7919;
        foreach (char c in key) h = h * 31 + c;
        h &= 0x7fffffff;
        return new Vec3(20f + h % 37, -15f - (h / 37) % 29, 25f + (h / 1073) % 31);
    }

    static SortedDictionary<string, Vec3> Values(IEnumerable<FkBone> bones, int salt = 0)
    {
        var m = new SortedDictionary<string, Vec3>(StringComparer.Ordinal);
        foreach (var b in bones) m[b.Key] = ValueFor(b.Key, salt);
        return m;
    }

    static IEnumerable<FkBone> Group(FkCharacter c, params FkGroup[] g) => g.SelectMany(x => c.Table.Of(x));

    /// <summary>Applied bones whose rotation (and partner) is not bit-equal to Euler(value).</summary>
    static List<string> NotWritten(FkCharacter c, IEnumerable<FkBone> bones)
    {
        var bad = new List<string>();
        foreach (var b in bones)
        {
            if (!b.HasValue || !b.Used || !b.Bone) continue;
            var q = Stored(Quaternion.Euler(b.Value));
            if (!Bits(b.Bone.localRotation, q)) bad.Add($"{b.Key} {Angle(b.Bone.localRotation, q):G3}°");
            else if (b.Partner && !Bits(b.Partner.localRotation, q)) bad.Add($"{b.Key} partner {b.Partner.name}");
        }
        return bad;
    }

    static string Few(List<string> l) => l.Count == 0 ? "" : $"{l.Count}: " + string.Join(", ", l.Take(8)) + (l.Count > 8 ? ", ..." : "");

    static Transform Find(CharaRealization r, string name) => r?.Context?.BodyBones != null && r.Context.BodyBones.TryGetValue(name, out var t) ? t : null;

    /// <summary>Bones of <paramref name="a"/> whose localRotation differs from the same-named bone of <paramref name="b"/>.</summary>
    static List<string> Differ(FkCharacter a, FkCharacter b, IEnumerable<FkBone> bones)
    {
        var bad = new List<string>();
        foreach (var x in bones)
        {
            var y = b.Table.Find(x.Key);
            if (y == null || !x.Bone || !y.Bone) continue;
            if (!Bits(x.Bone.localRotation, y.Bone.localRotation)) bad.Add($"{x.Key} {Angle(x.Bone.localRotation, y.Bone.localRotation):G3}°");
        }
        return bad;
    }

    // ================================================================ F1 discovery

    static void F1()
    {
        Section("F1", "discovery");
        var db = services.Db;
        var rows = FkRows.From(db);
        bool kky = db.FindBone(460) != null;
        Info($"Bone-list rows {rows.Count} (vanilla {rows.Count(r => db.FindBone(r.No)?.IsVanilla == true)}); KKYBonusBones rows {(kky ? "installed" : "not installed")}");
        Check("vanilla Bone-list rows: 271", rows.Count(r => db.FindBone(r.No)?.IsVanilla == true) == 271, rows.Count(r => db.FindBone(r.No)?.IsVanilla == true).ToString());
        using (var e = new Env())
        {
            var f = e.Add(femaleCard);
            var m = e.Add(maleCard);
            // an item attached inside the body: discovery must never enter its holder
            var item = e.Rt.Ops.AddItem(ContentId.Vanilla(399), f, "N_Hand_R");
            e.Rt.ApplyNow();
            foreach (var (id, male) in new[] { (f, false), (m, true) })
            {
                var c = e.Fk(id);
                if (c == null) { Check("FkCharacter after the build", false, id); continue; }
                var t = c.Table; var st = t.Stats;
                string who = male ? "HERO107" : "BR-Chan";
                Info($"{who}: {st}; discovery {st.Milliseconds:F2} ms");
                int Vanilla(FkGroup g) => t.Of(g).Count(b => !b.Additional && db.FindBone(b.Row.No)?.IsVanilla == true);
                Check($"{who}: Neck 2", Vanilla(FkGroup.Neck) == 2, Vanilla(FkGroup.Neck).ToString());
                Check($"{who}: Breast 10", Vanilla(FkGroup.Breast) == 10, Vanilla(FkGroup.Breast).ToString());
                Check($"{who}: Body {(male ? 25 : 22)} (vanilla rows)", Vanilla(FkGroup.Body) == (male ? 25 : 22), Vanilla(FkGroup.Body).ToString());
                Check($"{who}: hands 15 + 15", Vanilla(FkGroup.RightHand) == 15 && Vanilla(FkGroup.LeftHand) == 15, Vanilla(FkGroup.RightHand) + " + " + Vanilla(FkGroup.LeftHand));
                Check($"{who}: Skirt rows 48", st.Rows[(int)FkGroup.Skirt] == 48, $"rows {st.Rows[(int)FkGroup.Skirt]}, found {t.Of(FkGroup.Skirt).Count}, used {t.Of(FkGroup.Skirt).Count(b => b.Used)}{(st.UnreadableMeshes > 0 ? ", unreadable meshes " + st.UnreadableMeshes : "")}");
                Info($"{who}: skirt found {t.Of(FkGroup.Skirt).Count}, used {t.Of(FkGroup.Skirt).Count(b => b.Used)} (clothes renderers {st.Renderers}, unreadable {st.UnreadableMeshes})");
                int hairRows = st.Rows[(int)FkGroup.Hair];
                Info($"{who}: hair rows {hairRows}, FK bones {t.Of(FkGroup.Hair).Count}, mirrors {t.Of(FkGroup.Hair).Count(b => b.Partner)}, normalised matches {st.HairNormalised}, not found {st.NotFound[(int)FkGroup.Hair]}");
                Check($"{who}: hair rows 156 (vanilla)", hairRows >= 156, hairRows.ToString());
                Check($"{who}: hair matches + mirrors + misses = rows", t.Of(FkGroup.Hair).Count + t.Of(FkGroup.Hair).Count(b => b.Partner) + st.NotFound[(int)FkGroup.Hair] == hairRows,
                      $"{t.Of(FkGroup.Hair).Count} + {t.Of(FkGroup.Hair).Count(b => b.Partner)} + {st.NotFound[(int)FkGroup.Hair]} vs {hairRows}");
                Check($"{who}: level 2 rows {(male ? "kept" : "skipped (3)")}", male ? st.Level2Skipped == 0 : st.Level2Skipped == 3, st.Level2Skipped.ToString());
                if (kky)
                {
                    var kk = new[] { 460, 461, 462, 463 }.Select(n => db.FindBone(n)?.TransformName).ToList();
                    int found = kk.Count(n => n != null && t.Find(n) != null && t.Find(n).Row.No >= 460);
                    Check($"{who}: KKYBonusBones rows found (460–463)", found == 4, $"{found}: {string.Join(", ", kk)}");
                }
                // holders: no FK transform inside a scene node holder
                var inHolder = t.Bones.Where(b => InHolder(b.Bone, c.Root.transform) || (b.Partner && InHolder(b.Partner, c.Root.transform))).Select(b => b.Key).ToList();
                Check($"{who}: no FK transform inside a scene node holder", inHolder.Count == 0, Few(inHolder));
                // names: discovery never renames transforms
                var before = c.Root.GetComponentsInChildren<Transform>(true).Select(x => x.name).ToList();
                var again = FkBoneTable.Discover(DiscoveryOf(e.C(id)), null);
                var after = c.Root.GetComponentsInChildren<Transform>(true).Select(x => x.name).ToList();
                Check($"{who}: a second discovery renames nothing and finds the same keys", before.SequenceEqual(after) && again.Bones.Select(b => b.Key).SequenceEqual(t.Bones.Select(b => b.Key)),
                      $"{again.Bones.Count} vs {t.Bones.Count}");
                var renamedHair = t.Of(FkGroup.Hair).Where(b => b.Bone.name != b.Key).Select(b => b.Bone.name + "→" + b.Key).ToList();
                Info($"{who}: hair matched through the legacy name rule: {(renamedHair.Count == 0 ? "none" : string.Join(", ", renamedHair.Take(10)))}");
            }
            Check("item attached inside the body", item.Ok, item.ToString());
        }
        SyntheticDiscovery();
    }

    static FkDiscovery DiscoveryOf(CharaRealization r)
    {
        var m = typeof(FkCharacter).GetMethod("DiscoveryOf", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        return (FkDiscovery)m.Invoke(null, new object[] { r });
    }

    static bool InHolder(Transform t, Transform root)
    {
        for (var p = t; p && p != root; p = p.parent)
            if (p.GetComponent<SceneNodeHolder>()) return true;
        return false;
    }

    // synthetic hierarchy: sync pairs, legacy hair names, holder stop, level 2, unknown group, missing primary
    static void SyntheticDiscovery()
    {
        var root = new GameObject("synthetic_root").transform;
        try
        {
            Transform N(string name, Transform parent) { var t = new GameObject(name).transform; t.SetParent(parent, false); return t; }
            var body = N("cf_J_Root", root);
            var hips = N("cf_J_Hips", body);
            var lv2 = N("cm_J_dan_s", hips);
            var head = N("cf_J_Head_s", hips);
            var hairA = N("c_J_hairFCa_01", head);     // legacy name → c_J_hair_FCa_01
            var hairB = N("c_J_hair_FCb_01", head);    // partner (b layer)
            var lone = N("c_J_hair_BRb_00", head);     // partner whose primary is missing → own bone
            var holder = N("holder", hips);
            holder.gameObject.AddComponent<SceneNodeHolder>();
            var decoy = N("cf_J_Kosi01", holder);      // only inside a holder: must not be found
            var rows = new List<FkRow>
            {
                new FkRow { No = 0, Name = "cf_J_Hips", GroupColumn = 0, Level = 0, SyncNo = -1 },
                new FkRow { No = 1, Name = "cf_J_Kosi01", GroupColumn = 0, Level = 0, SyncNo = -1 },
                new FkRow { No = 2, Name = "cm_J_dan_s", GroupColumn = 0, Level = 2, SyncNo = -1 },
                new FkRow { No = 3, Name = "cf_J_Hips", GroupColumn = 20, Level = 0, SyncNo = -1 },
                new FkRow { No = 100, Name = "c_J_hair_FCa_01", GroupColumn = 7, Level = 1, SyncNo = 101 },
                new FkRow { No = 101, Name = "c_J_hair_FCb_01", GroupColumn = 7, Level = 1, SyncNo = 100 },
                new FkRow { No = 102, Name = "c_J_hair_BRa_00", GroupColumn = 8, Level = 1, SyncNo = 103 },
                new FkRow { No = 103, Name = "c_J_hair_BRb_00", GroupColumn = 8, Level = 1, SyncNo = 102 },
            };
            var holders = new HashSet<Transform> { holder };
            var input = new FkDiscovery { Root = root, BodyBones = null, HeadParent = head, Female = true, Rows = rows, Stop = x => holders.Contains(x) };
            var t = FkBoneTable.Discover(input, null);
            var a = t.Find("c_J_hair_FCa_01");
            Check("synthetic: legacy hair name matched without renaming", a != null && a.Bone == hairA && hairA.name == "c_J_hairFCa_01", a?.ToString());
            Check("synthetic: sync partner mirrored on the first row", a != null && a.Partner == hairB && a.PartnerKey == "c_J_hair_FCb_01" && t.FindAny("c_J_hair_FCb_01") == a);
            var l = t.Find("c_J_hair_BRb_00");
            Check("synthetic: partner of a missing primary is its own FK bone", l != null && l.Bone == lone && l.Partner == null, l?.ToString());
            Check("synthetic: a name only inside a holder is not found", t.Find("cf_J_Kosi01") == null && t.Stats.NotFound[(int)FkGroup.Body] == 1, t.Stats.ToString());
            Check("synthetic: level 2 skipped for a female, unknown group column counted", t.Find("cm_J_dan_s") == null && t.Stats.Level2Skipped == 1 && t.Stats.NoGroup == 1, t.Stats.ToString());
            input.Female = false;
            var tm = FkBoneTable.Discover(input, null);
            Check("synthetic: level 2 kept for a male", tm.Find("cm_J_dan_s") != null);
            tm.JoinNodes(new[] { new FkRow { No = 1, Name = "cf_J_Hips", GroupColumn = 5, Level = 1 }, new FkRow { No = 10000, Name = "cf_J_Kosi01", GroupColumn = 5, Level = 1 }, new FkRow { No = 10001, Name = "cm_J_dan_s", GroupColumn = 6, Level = 0 } }, null);
            Check("synthetic: node rows join after the Bone lists (a Bone-list key keeps its row; holder still stops)",
                  tm.Stats.NodesShadowed == 1 && tm.Find("cm_J_dan_s")?.Additional == false && tm.Stats.NodeRows == 2 && tm.Additional.Count == 0, tm.Stats.ToString());
            Check("hair name normalisation", FkBoneTable.NormaliseHairName("c_J_hairBLa_12") == "c_J_hair_BLa_12" && FkBoneTable.NormaliseHairName("c_J_hairFCUb_") == "c_J_hair_FCUb_" &&
                  FkBoneTable.NormaliseHairName("c_j_hairSa_01") == "c_j_hairSa_01" && FkBoneTable.NormaliseHairName("c_J_hairFXa_01") == "c_J_hairFXa_01");
        }
        finally { Object.DestroyImmediate(root.gameObject); }
    }

    // ================================================================ F2 application

    static void F2()
    {
        Section("F2", "per-step application with Idle_00");
        using (var e = new Env())
        {
            var a = e.Add(femaleCard); var b = e.Add(femaleCard);
            foreach (var id in new[] { a, b }) e.S.SetAnimation(id, Idle);
            e.Rt.ApplyNow();
            var ca = e.Fk(a); var cb = e.Fk(b);
            var vals = Values(Group(ca, FkGroup.Neck, FkGroup.Body, FkGroup.RightHand, FkGroup.LeftHand));
            e.Set(a, "chara.fk.bones", vals);
            e.Set(a, "chara.fk.enabled", true);
            e.Rt.ApplyNow();
            var applied = Group(ca, FkGroup.Neck, FkGroup.Body).ToList();
            var notWrittenAtOnce = NotWritten(ca, applied);
            Check("written at once after ApplyNow (no step)", notWrittenAtOnce.Count == 0, Few(notWrittenAtOnce));
            int badSteps = 0; var first = new List<string>(); int handDiff = 0; var handFirst = new List<string>();
            for (int i = 0; i < 30; i++)
            {
                e.Step();
                var bad = NotWritten(ca, applied);
                if (bad.Count > 0) { badSteps++; if (first.Count == 0) first = bad; }
                var hd = Differ(ca, cb, Group(ca, FkGroup.RightHand, FkGroup.LeftHand));
                if (hd.Count > 0) { handDiff++; if (handFirst.Count == 0) handFirst = hd; }
            }
            Check("every applied bone (and partner) = Euler(value) bit for bit after each of 30 steps", badSteps == 0, badSteps + " steps; " + Few(first));
            Check("hand groups (not applied) follow the clip like a character without FK", handDiff == 0, handDiff + " steps; " + Few(handFirst));
            e.Set(a, "chara.fk.enabled", false);
            e.Rt.ApplyNow();
            int rest = Group(ca, FkGroup.Body).Count(x => Bits(x.Bone.localRotation, x.Rest));
            Check("fk.enabled false: Body bones back to their rest rotation at once", rest == ca.Table.Of(FkGroup.Body).Count, $"{rest}/{ca.Table.Of(FkGroup.Body).Count}");
            e.Step();
            var still = Group(ca, FkGroup.Body).Where(x => x.HasValue && Bits(x.Bone.localRotation, Stored(Quaternion.Euler(x.Value)))).Select(x => x.Key).ToList();
            var clip = Differ(ca, cb, Group(ca, FkGroup.Body));
            Check("fk.enabled false: nothing written (Body follows the clip like the character without FK)", still.Count == 0 && clip.Count == 0, $"FK values still shown {Few(still)}; differing from the reference {Few(clip)}");
            Check("stage disabled while no group is applied", !ca.Stage.Enabled);
        }
    }

    // ================================================================ F3 values of groups not applied

    static void F3()
    {
        Section("F3", "values of a group not applied");
        using (var e = new Env())
        {
            var a = e.Add(femaleCard); var b = e.Add(femaleCard);
            foreach (var id in new[] { a, b }) e.S.SetAnimation(id, Idle);
            e.Rt.ApplyNow();
            var ca = e.Fk(a); var cb = e.Fk(b);
            e.Set(a, "chara.fk.enabled", true);
            e.Set(a, "chara.fk.bones", Values(Group(ca, FkGroup.RightHand)));
            e.Rt.ApplyNow();
            e.Step(3);
            var d = Differ(ca, cb, Group(ca, FkGroup.RightHand));
            Check("RightHand values kept in the model but not written while the group is off", d.Count == 0 && ca.Table.Of(FkGroup.RightHand).All(x => x.HasValue), Few(d));
            e.Set(a, "chara.fk.groups.rightHand", true);
            e.Rt.ApplyNow();
            var bad = NotWritten(ca, Group(ca, FkGroup.RightHand));
            Check("switching the group on applies the kept values at once", bad.Count == 0, Few(bad));
            e.Step(2);
            bad = NotWritten(ca, Group(ca, FkGroup.RightHand));
            Check("... and in the following steps", bad.Count == 0, Few(bad));
        }
    }

    // ================================================================ F4 side effects

    static void F4()
    {
        Section("F4", "group side effects");
        using (var e = new Env())
        {
            var a = e.Add(femaleCard);
            e.S.SetAnimation(a, Idle);
            e.Rt.ApplyNow();
            var ca = e.Fk(a);
            var r = e.C(a);
            var look = r.Look;
            var neck = look.Neck;
            e.Set(a, "chara.look.neck", 1);
            e.Set(a, "chara.fk.enabled", true);
            e.Rt.ApplyNow();
            int notFix = 0;
            for (int i = 0; i < 10; i++)
            {
                if (i == 4) { e.Set(a, "chara.look.neck", 0); e.Rt.ApplyNow(); }
                e.Step();
                if (neck.NowPtnNo != CharaLook.FkNeckPattern) notFix++;
            }
            Check("Neck applied: the neck stage runs FIX (NowPtnNo 4) in every step, also after a look.neck change", notFix == 0, notFix + " of 10 steps");
            Check("model look.neck keeps the user's pattern", e.St(a).Look.Neck == 0, e.St(a).Look.Neck.ToString());
            e.Set(a, "chara.look.neck", 1);
            e.Set(a, "chara.fk.groups.neck", false);
            e.Rt.ApplyNow();
            Check("Neck de-applied: the model's look.neck is restored at once", look.NeckPattern == 1 && neck.PtnNo == 1, $"pattern {look.NeckPattern}, stage {neck.PtnNo}");
            e.Step();
            Check("... and the neck look runs it", neck.NowPtnNo == 1, neck.NowPtnNo.ToString());

            var bust = r.Motion.Get<BustStage>();
            e.Set(a, "chara.fk.groups.breast", true);
            e.Rt.ApplyNow();
            bool on = bust != null && bust.FkBreastActive;
            e.Set(a, "chara.fk.groups.breast", false);
            e.Rt.ApplyNow();
            Check("Breast: BustStage.FkBreastActive follows the group", bust != null && on && !bust.FkBreastActive, bust == null ? "no bust stage" : "");

            var dyn = r.Motion.Get<DynamicBoneStage>();
            var hairChains = dyn?.Chains.Where(x => x.Category == DynamicBoneCategory.Hair).ToList() ?? new List<DynamicBoneChain>();
            int hairActive0 = hairChains.Count(x => x.Active);
            e.Set(a, "chara.fk.bones", Values(Group(ca, FkGroup.Hair, FkGroup.Skirt)));
            e.Step(2);
            e.Set(a, "chara.fk.groups.hair", true);
            e.Rt.ApplyNow();
            int hairActive = hairChains.Count(x => x.Active);
            e.Step();
            var badHair = NotWritten(ca, Group(ca, FkGroup.Hair));
            Check("Hair: hair chains inactive at once and the first step shows the FK values", hairActive == 0 && badHair.Count == 0 && ca.Table.Of(FkGroup.Hair).Count > 0,
                  $"hair chains {hairChains.Count} (active before {hairActive0}, after the switch {hairActive}); FK hair bones {ca.Table.Of(FkGroup.Hair).Count}; not written {Few(badHair)}");
            e.Set(a, "chara.fk.groups.hair", false);
            e.Rt.ApplyNow();
            Check("Hair off: the hair switch is released", dyn != null && !dyn.HairFkActive && hairChains.Count(x => x.Active) == hairChains.Count(x => dyn.WantActive(x)));

            var skirtChains = dyn?.Chains.Where(x => x.IsSkirt).ToList() ?? new List<DynamicBoneChain>();
            e.Set(a, "chara.fk.groups.skirt", true);
            e.Rt.ApplyNow();
            int skirtActive = skirtChains.Count(x => x.Active);
            e.Step();
            var badSkirt = NotWritten(ca, Group(ca, FkGroup.Skirt));
            Check("Skirt: skirt chains inactive at once and the first step shows the FK values (used bones)", skirtActive == 0 && badSkirt.Count == 0,
                  $"skirt chains {skirtChains.Count}, active after the switch {skirtActive}; used skirt bones {ca.Table.Of(FkGroup.Skirt).Count(x => x.Used)}; not written {Few(badSkirt)}");
            e.Set(a, "chara.fk.groups.skirt", false);
            e.Rt.ApplyNow();
            Check("Skirt off: the skirt switch is released", dyn != null && !dyn.SkirtFkActive);
        }
    }

    // ================================================================ F5 missing values

    static void F5()
    {
        Section("F5", "missing values and capture");
        using (var e = new Env())
        {
            var a = e.Add(femaleCard); var b = e.Add(femaleCard);
            foreach (var id in new[] { a, b }) e.S.SetAnimation(id, Idle);
            e.Rt.ApplyNow();
            var ca = e.Fk(a); var cb = e.Fk(b);
            e.Set(a, "chara.fk.enabled", true);   // raw set: no capture
            e.Rt.ApplyNow();
            e.Step(3);
            var d = Differ(ca, cb, Group(ca, FkGroup.Body, FkGroup.Neck).Where(x => x.Group == FkGroup.Body));
            Check("raw Set of fk.enabled: bones without values are not written", e.St(a).Fk.Bones.Count == 0 && d.Count == 0, Few(d));
        }
        using (var e = new Env())
        {
            var a = e.Add(femaleCard);
            e.S.SetAnimation(a, Idle);
            e.Rt.ApplyNow();
            e.Step(3);
            var ca = e.Fk(a);
            var bones = Group(ca, FkGroup.Neck, FkGroup.Body).Where(x => x.Bone).ToList();
            var pre = bones.ToDictionary(x => x.Key, x => x.Bone.localRotation);
            int undo0 = e.S.Commands.UndoList.Count;
            var res = e.Ops.SetEnabled(a, true);
            var st = e.St(a);
            float worst = 0; string worstKey = null;
            foreach (var x in bones)
            {
                float ang = Angle(x.Bone.localRotation, pre[x.Key]);
                if (ang > worst) { worst = ang; worstKey = x.Key; }
            }
            Check("FkOps.SetEnabled captures every missing Neck/Body value", res.Ok && st.Fk.Enabled && bones.All(x => st.Fk.Bones.ContainsKey(x.Key)), $"{res}; {st.Fk.Bones.Count} values for {bones.Count} bones");
            Check("captured values reproduce the pre-command rotation (≤ 1e-4°)", worst <= 1e-4f, $"max {worst:G3}° ({worstKey})");
            Check("one history entry", e.S.Commands.UndoList.Count == undo0 + 1, $"{undo0} → {e.S.Commands.UndoList.Count}");
            e.S.Commands.Undo();
            e.Rt.ApplyNow();
            Check("undo removes the captured values and the flag", e.St(a).Fk.Bones.Count == 0 && !e.St(a).Fk.Enabled, e.St(a).Fk.Bones.Count.ToString());
            // a group switched on while FK is on
            e.Set(a, "chara.fk.enabled", true);
            e.Rt.ApplyNow();
            e.Step();
            var hand = Group(ca, FkGroup.LeftHand).ToList();
            var preHand = hand.ToDictionary(x => x.Key, x => x.Bone.localRotation);
            res = e.Ops.SetGroup(a, FkGroup.LeftHand, true);
            float wh = hand.Max(x => Angle(x.Bone.localRotation, preHand[x.Key]));
            Check("FkOps.SetGroup captures the group's missing values (≤ 1e-4°)", res.Ok && hand.All(x => e.St(a).Fk.Bones.ContainsKey(x.Key)) && wh <= 1e-4f, $"{res}; max {wh:G3}°");
        }
        using (var e = new Env())
        {
            FkOptions.MissingBonesZero = true;
            var a = e.Add(femaleCard);
            e.S.SetAnimation(a, Idle);
            e.Rt.ApplyNow();
            e.Step();
            var res = e.Ops.SetEnabled(a, true);
            var vals = e.St(a).Fk.Bones.Values.ToList();
            Check("MissingBonesZero: the capture writes (0, 0, 0)", res.Ok && vals.Count > 0 && vals.All(v => v.X == 0f && v.Y == 0f && v.Z == 0f), $"{vals.Count} values");
            // raw path with MissingBonesZero: bones without values are written as identity
            e.Set(a, "chara.fk.bones", new SortedDictionary<string, Vec3>(StringComparer.Ordinal));
            e.Rt.ApplyNow();
            e.Step();
            var ca = e.Fk(a);
            int id = Group(ca, FkGroup.Body).Count(x => Bits(x.Bone.localRotation, Quaternion.identity));
            Check("MissingBonesZero: bones without values written as identity", id == ca.Table.Of(FkGroup.Body).Count, $"{id}/{ca.Table.Of(FkGroup.Body).Count}");
        }
    }

    // ================================================================ F6 de-apply

    static void F6()
    {
        Section("F6", "de-apply: rest, identity");
        using (var e = new Env())
        {
            var a = e.Add(femaleCard);
            e.Rt.ApplyNow();
            var ca = e.Fk(a);
            e.Set(a, "chara.fk.bones", Values(Group(ca, FkGroup.Hair, FkGroup.Body, FkGroup.Skirt)));
            e.Set(a, "chara.fk.enabled", true);
            foreach (var g in new[] { "hair", "skirt" }) e.Set(a, "chara.fk.groups." + g, true);
            e.Rt.ApplyNow();
            e.Step(2);
            foreach (var (g, name) in new[] { (FkGroup.Hair, "hair"), (FkGroup.Skirt, "skirt"), (FkGroup.Body, "body") })
            {
                e.Set(a, "chara.fk.groups." + name, false);
                e.Rt.ApplyNow();
                var bones = ca.Table.Of(g).Where(x => x.Used).ToList();
                var notRest = bones.Where(x => !Bits(x.Bone.localRotation, x.Rest) || (x.Partner && !Bits(x.Partner.localRotation, x.PartnerRest))).Select(x => x.Key).ToList();
                Check($"{g} de-applied: bones back to their rest rotation", bones.Count == 0 || notRest.Count == 0, $"{bones.Count} bones; {Few(notRest)}");
            }
            var sk = ca.Table.Find("cf_J_sk_04_02");
            if (sk != null) Info($"cf_J_sk_04_02 rest {F(sk.Rest.eulerAngles)} (vanilla resets it to 0)");
        }
        using (var e = new Env())
        {
            FkOptions.ResetToIdentity = true;
            var a = e.Add(femaleCard);
            var ca = e.Fk(a);
            var init = Group(ca, FkGroup.Hair, FkGroup.Body, FkGroup.Skirt).Where(x => !Bits(x.Bone.localRotation, Quaternion.identity)).Select(x => x.Key).ToList();
            Check("ResetToIdentity: at build every Hair/Body/Skirt bone of the non-applied groups is identity", init.Count == 0, Few(init));
            var sk = ca.Table.Find("cf_J_sk_04_02");
            Info("cf_J_sk_04_02 after the build with ResetToIdentity: " + (sk != null ? F(sk.Bone.localEulerAngles) : "absent"));
            e.Set(a, "chara.fk.bones", Values(Group(ca, FkGroup.Skirt, FkGroup.Body)));
            e.Set(a, "chara.fk.enabled", true);
            e.Set(a, "chara.fk.groups.skirt", true);
            e.Rt.ApplyNow();
            e.Step();
            e.Set(a, "chara.fk.groups.skirt", false);
            e.Set(a, "chara.fk.groups.body", false);
            e.Rt.ApplyNow();
            var bad = Group(ca, FkGroup.Skirt, FkGroup.Body).Where(x => x.Used && !Bits(x.Bone.localRotation, Quaternion.identity)).Select(x => x.Key).ToList();
            Check("ResetToIdentity: de-applied Skirt / Body bones are identity", bad.Count == 0, Few(bad));
        }
    }

    // ================================================================ F7 StudioDefaultPoseScope

    static void F7()
    {
        Section("F7", "StudioDefaultPoseScope suspends the driver");
        using (var e = new Env())
        {
            var a = e.Add(femaleCard);
            e.S.SetAnimation(a, Idle);
            e.Rt.ApplyNow();
            var ca = e.Fk(a);
            e.Set(a, "chara.fk.bones", Values(Group(ca, FkGroup.Neck, FkGroup.Body, FkGroup.Skirt)));
            e.Set(a, "chara.fk.enabled", true);
            e.Set(a, "chara.fk.groups.skirt", true);
            e.Set(a, "chara.fk.hipsOffset", -0.5f);
            e.Rt.ApplyNow();
            e.Step(3);
            var bones = Group(ca, FkGroup.Neck, FkGroup.Body, FkGroup.Skirt).Where(x => x.Used && x.Bone).ToList();
            var before = bones.ToDictionary(x => x.Key, x => x.Bone.localRotation);
            var hips = Find(e.C(a), "cf_J_Hips");
            var hipsBefore = hips.localPosition;
            int written = -1; bool suspended = false, hipsSuspended = false; Vector3 hipsInside = default;
            using (StudioDefaultPoseScope.Enter(e.C(a)))
            {
                suspended = ca.Suspended && ca.Stage.Suspended;
                hipsSuspended = ca.Hips.Suspended;
                hipsInside = hips.localPosition;
                written = bones.Count(x => Bits(x.Bone.localRotation, Stored(x.Rotation)));
            }
            Check("inside the scope the driver is suspended and no bone shows its FK value", suspended && written == 0, $"suspended {suspended}; bones showing the FK value {written}/{bones.Count}");
            Check("inside the scope the hips offset is off", hipsSuspended && Math.Abs(hipsInside.y - hipsBefore.y) > 0.4f, $"inside {F(hipsInside)}, before {F(hipsBefore)}");
            e.Step();
            var diff = bones.Where(x => !Bits(x.Bone.localRotation, before[x.Key])).Select(x => x.Key).ToList();
            Check("after Dispose and one step the FK rotations equal those before (bit-equal)", diff.Count == 0 && !ca.Suspended, Few(diff));
            Check("after Dispose the hips offset applies again", Math.Abs(hips.localPosition.y - hipsBefore.y) < 1e-5f, $"{F(hips.localPosition)} vs {F(hipsBefore)}");
        }
    }

    // ================================================================ F8 OnChanged granularity

    static void F8()
    {
        Section("F8", "OnChanged granularity");
        using (var e = new Env())
        {
            var a = e.Add(femaleCard);
            e.Rt.ApplyNow();
            var ca = e.Fk(a);
            e.Set(a, "chara.fk.bones", Values(Group(ca, FkGroup.Body)));
            e.Set(a, "chara.fk.enabled", true);
            e.Rt.ApplyNow();
            var x = ca.Table.Find("cf_J_ArmUp00_L"); var y = ca.Table.Find("cf_J_ArmUp00_R");
            var sentinel = Quaternion.Euler(1f, 2f, 3f);
            x.Bone.localRotation = sentinel; y.Bone.localRotation = sentinel;
            var nv = new Vec3(11f, 22f, 33f);
            e.Set(a, "chara.fk.bones[\"cf_J_ArmUp00_L\"]", nv);
            e.Rt.ApplyNow();
            Check("one key updates exactly that bone", Bits(x.Bone.localRotation, Stored(Quaternion.Euler(U(nv)))) && Bits(y.Bone.localRotation, Stored(sentinel)));
            var fk = DocClone.Clone(e.St(a).Fk);
            fk.Bones["cf_J_ArmUp00_R"] = new Vec3(5f, 6f, 7f);
            e.Set(a, "chara.fk", fk);
            e.Rt.ApplyNow();
            var bad = NotWritten(ca, Group(ca, FkGroup.Body));
            Check("replacing chara.fk updates every bone", Bits(y.Bone.localRotation, Stored(Quaternion.Euler(5f, 6f, 7f))) && bad.Count == 0, Few(bad));
            e.S.Commands.Undo();
            e.Rt.ApplyNow();
            Check("undo of the chara.fk set restores the value", Bits(y.Bone.localRotation, Stored(Quaternion.Euler(U(ValueFor("cf_J_ArmUp00_R"))))));
            e.Set(a, "chara.fk.groups.body", false);
            e.Rt.ApplyNow();
            Check("a group flag change de-applies (Body back to rest)", Bits(x.Bone.localRotation, x.Rest));
        }
    }

    // ================================================================ F9 determinism

    static void Commands(Env e, string a, int step, FkCharacter c)
    {
        if (step == 0)
        {
            e.S.SetAnimation(a, Idle);
            e.Set(a, "chara.fk.bones", Values(Group(c, FkGroup.Neck, FkGroup.Body, FkGroup.RightHand, FkGroup.Breast, FkGroup.Hair)));
            e.Set(a, "chara.fk.enabled", true);
            e.Set(a, "chara.fk.hipsOffset", -1.25f);
        }
        if (step == 10) { e.Set(a, "chara.fk.groups.rightHand", true); e.Set(a, "chara.fk.groups.hair", true); }
        if (step == 20) { e.Set(a, "chara.fk.groups.body", false); e.Set(a, "chara.fk.groups.breast", true); }
    }

    static List<Transform> AllBones(FkCharacter c) => c.Root.GetComponentsInChildren<Transform>(true).ToList();

    static void F9()
    {
        Section("F9", "determinism");
        using (var e1 = new Env(0x1111UL))
        using (var e2 = new Env(0x1111UL))
        {
            var a1 = e1.Add(femaleCard); var a2 = e2.Add(femaleCard);
            var c1 = e1.Fk(a1); var c2 = e2.Fk(a2);
            var t1 = AllBones(c1); var t2 = AllBones(c2);
            int badSteps = 0, compared = 0; string first = null;
            for (int s = 0; s < 30; s++)
            {
                Commands(e1, a1, s, c1); Commands(e2, a2, s, c2);
                e1.Step(); e2.Step();
                for (int i = 0; i < t1.Count && i < t2.Count; i++)
                {
                    compared++;
                    if (!Bits(t1[i].localRotation, t2[i].localRotation) || !Bits(t1[i].localPosition, t2[i].localPosition))
                    {
                        if (first == null) first = $"step {s} {t1[i].name}";
                        badSteps++;
                        break;
                    }
                }
            }
            Check("two runtimes with the same commands: every transform bit-identical at every step", badSteps == 0 && t1.Count == t2.Count, $"{badSteps} steps differ{(first != null ? ", first " + first : "")}; {t1.Count} transforms");
        }
        using (var e1 = new Env(0x2222UL))
        {
            var a = e1.Add(femaleCard);
            var c = e1.Fk(a);
            Commands(e1, a, 0, c);
            e1.Rt.ApplyNow();
            var bytes = e1.Rt.Ops.ToBytes(FixedSave());
            using (var e2 = new Env(0x3333UL))
            {
                var load = e2.Rt.Ops.Load(bytes);
                e2.Rt.ApplyNow();
                var c2 = e2.Fk(a);
                Check("save → load", load.Ok && c2 != null, load.ToString());
                if (c2 == null) return;
                e1.Step(20); e2.Step(20);
                var fkBones = c.Table.Bones.Where(x => x.Bone && x.Used && c.Stage.IsApplied(x.Group)).ToList();
                var d = Differ(c, c2, fkBones);
                var all1 = AllBones(c); var all2 = AllBones(c2);
                int other = 0;
                for (int i = 0; i < all1.Count && i < all2.Count; i++) if (!Bits(all1[i].localRotation, all2[i].localRotation)) other++;
                var h1 = Find(e1.C(a), "cf_J_Hips").localPosition; var h2 = Find(e2.C(a), "cf_J_Hips").localPosition;
                Check("save → load → 20 steps: FK bones and the hips position equal the original after 20 steps (bit-equal)", d.Count == 0 && Bits(h1, h2), $"{Few(d)}; hips {F(h1)} vs {F(h2)}");
                Info($"other transforms differing after the reload: {other} of {all1.Count}");
            }
        }
    }

    static SaveOptions FixedSave() => new SaveOptions { Clock = () => new DateTime(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc), Thumbnail = ThumbnailMode.Placeholder, AppVersion = "0.1.0" };

    // ================================================================ F10 costs

    static double Median(List<double> l) { l.Sort(); return l.Count == 0 ? 0 : l[l.Count / 2]; }

    static void Cost(string what, double value, double target, string unit)
    {
        Check($"{what}: {value:G4} {unit} (target {target} {unit}, fail at {2 * target})", value <= 2 * target, value <= target ? "" : "over the target, under 2×");
    }

    static void F10()
    {
        Section("F10", "costs");
        using (var e = new Env())
        {
            var a = e.Add(femaleCard);
            e.Rt.ApplyNow();
            var r = e.C(a);
            var ca = e.Fk(a);
            var disc = new List<double>();
            for (int i = 0; i < 9; i++)
            {
                var sw = Stopwatch.StartNew();
                FkBoneTable.Discover(DiscoveryOf(r), null);
                disc.Add(sw.Elapsed.TotalMilliseconds);
            }
            Cost("FK discovery incl. the skirt weight scan (median of 9)", Median(disc), 3, "ms");
            Info($"discovery at build: {ca.Table.Stats.Milliseconds:F2} ms");

            // 64 applied bones: Neck, Breast, Body, both hands
            e.Set(a, "chara.fk.bones", Values(ca.Table.Bones));
            e.Set(a, "chara.fk.enabled", true);
            foreach (var g in new[] { "breast", "rightHand", "leftHand" }) e.Set(a, "chara.fk.groups." + g, true);
            e.Rt.ApplyNow();
            int n64 = Group(ca, FkGroup.Neck, FkGroup.Breast, FkGroup.Body, FkGroup.RightHand, FkGroup.LeftHand).Count();
            Cost($"FkStage.Step, {n64} applied bones (median of 200 × 50)", StepCost(ca.Stage, 50) * 1000, 0.02, "ms");
            foreach (var g in new[] { "hair", "skirt" }) e.Set(a, "chara.fk.groups." + g, true);
            e.Rt.ApplyNow();
            int nAll = ca.Table.Bones.Count(x => x.Used);
            Cost($"FkStage.Step, all seven groups ({nAll} bones on this card)", StepCost(ca.Stage, 50) * 1000, 0.06, "ms");
            Cost("FkStage.Step, a synthetic table of 300 bones in seven groups", SyntheticStepCost(300) * 1000, 0.06, "ms");
            e.Set(a, "chara.fk.hipsOffset", -1f);
            e.Rt.ApplyNow();
            var frame = new MotionFrame(Dt, Dt, 0f, 1);
            var samples = new List<double>();
            for (int k = 0; k < 200; k++)
            {
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < 100; i++) { ca.Hips.Step(frame); ca.Gate.Step(frame); }
                samples.Add(sw.Elapsed.TotalMilliseconds * 1000 / 100);
            }
            Cost("FkHipsStage.Step + FkNeckGate.Step", Median(samples), 2, "µs");
            var st = e.St(a);
            var key = "cf_J_ArmUp00_L";
            string path = "chara.fk.bones[\"" + key + "\"]";
            samples.Clear();
            for (int k = 0; k < 200; k++)
            {
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < 20; i++)
                {
                    st.Fk.Bones[key] = new Vec3(i, k, 1f);
                    FkDriver.Instance.OnChanged(r, path);
                }
                samples.Add(sw.Elapsed.TotalMilliseconds * 1000 / 20);
            }
            Cost("OnChanged of one bone", Median(samples), 5, "µs");
        }
    }

    static double SyntheticStepCost(int n)
    {
        var root = new GameObject("fk_cost_root").transform;
        try
        {
            var rows = new List<FkRow>();
            var bones = new Dictionary<string, Transform>();
            int[] cols = { 0, 5, 6, 7, 10, 11, 13 };
            for (int i = 0; i < n; i++)
            {
                var t = new GameObject("b" + i).transform;
                t.SetParent(root, false);
                bones[t.name] = t;
                rows.Add(new FkRow { No = i, Name = t.name, GroupColumn = cols[i % cols.Length], Level = 0, SyncNo = -1 });
            }
            var table = FkBoneTable.Discover(new FkDiscovery { Root = root, BodyBones = bones, HeadParent = root, Female = true, Rows = rows }, null);
            foreach (var b in table.Bones) { b.Used = true; b.Set(new Vector3(b.Row.No % 30, 5f, -7f)); }
            var st = new FkStage(table) { Enabled = true };
            for (int g = 0; g < FkGroupInfo.Count; g++) st.Applied[g] = true;
            return StepCost(st, 50);
        }
        finally { Object.DestroyImmediate(root.gameObject); }
    }

    static double StepCost(FkStage s, int inner)
    {
        var frame = new MotionFrame(Dt, Dt, 0f, 1);
        var l = new List<double>();
        for (int k = 0; k < 200; k++)
        {
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < inner; i++) s.Step(frame);
            l.Add(sw.Elapsed.TotalMilliseconds / inner);
        }
        return Median(l) / 1000;   // seconds
    }

    // ================================================================ F11 scene round trip

    static void F11()
    {
        Section("F11", "scene round trip");
        using (var e = new Env())
        {
            var a = e.Add(femaleCard);
            e.Rt.ApplyNow();
            var ca = e.Fk(a);
            e.Set(a, "chara.fk.bones", Values(Group(ca, FkGroup.Neck, FkGroup.Body, FkGroup.LeftHand, FkGroup.Skirt), 3));
            e.Set(a, "chara.fk.enabled", true);
            e.Set(a, "chara.fk.groups.leftHand", true);
            e.Set(a, "chara.fk.groups.skirt", true);
            e.Set(a, "chara.fk.hipsOffset", 1.273f);
            e.Rt.ApplyNow();
            var fkBytes = DocClone.Bytes(e.St(a).Fk);
            var bytes = e.Rt.Ops.ToBytes(FixedSave());
            using (var e2 = new Env(0x4444UL))
            {
                var load = e2.Rt.Ops.Load(bytes);
                e2.Rt.ApplyNow();
                var c2 = e2.Fk(a);
                var fk2 = DocClone.Bytes(e2.St(a).Fk);
                Check("fk block bytes identical after save → load", load.Ok && fkBytes.SequenceEqual(fk2), Encoding.UTF8.GetString(fk2).Substring(0, Math.Min(200, fk2.Length)));
                var bad = c2 == null ? new List<string> { "no FkCharacter" } : NotWritten(c2, c2.Table.Bones.Where(x => c2.Stage.IsApplied(x.Group)));
                Check("applied bones equal the values after ApplyNow (no step)", bad.Count == 0, Few(bad));
                var h1 = Find(e.C(a), "cf_J_Hips").localPosition; var h2 = Find(e2.C(a), "cf_J_Hips").localPosition;
                Check("hips offset applied after the load without stepping", Bits(h1, h2), $"{F(h1)} vs {F(h2)}");
            }
        }
    }

    // ================================================================ F12 hips stage

    static void F12()
    {
        Section("F12", "hips stage (HS2_FKHeightAdjustUI)");
        const float h = -0.7f;
        using (var e = new Env())
        {
            var a = e.Add(femaleCard); var b = e.Add(femaleCard);
            foreach (var id in new[] { a, b }) e.S.SetAnimation(id, Idle);
            e.Rt.ApplyNow();
            var ca = e.Fk(a);
            var ha = Find(e.C(a), "cf_J_Hips"); var hb = Find(e.C(b), "cf_J_Hips");
            e.Set(a, "chara.fk.hipsOffset", h);
            e.Rt.ApplyNow();
            e.Step(2);
            Check("FK off: no offset", Bits(ha.localPosition, hb.localPosition), $"{F(ha.localPosition)} vs {F(hb.localPosition)}");
            e.Set(a, "chara.fk.enabled", true);
            e.Rt.ApplyNow();
            Vector3 Expect() => new Vector3(hb.localPosition.x, hb.localPosition.y + h, hb.localPosition.z);
            int bad = 0; string firstBad = null;
            void Run(string what, int n)
            {
                bad = 0; firstBad = null;
                for (int i = 0; i < n; i++)
                {
                    e.Step();
                    if (!Bits(ha.localPosition, Expect())) { bad++; firstBad ??= $"{F(ha.localPosition)} vs {F(Expect())}"; }
                }
                Check($"{what}: cf_J_Hips = clip value + (0, h, 0) after each of {n} steps", bad == 0, bad + " steps" + (firstBad != null ? ", first " + firstBad : ""));
            }
            Run("playing clip", 10);
            foreach (var id in new[] { a, b }) e.Set(id, "chara.anime.speed", 0f);
            e.Rt.ApplyNow();
            Run("paused clip", 10);
            foreach (var id in new[] { a, b }) e.Set(id, "chara.anime.ref", null);
            e.Rt.ApplyNow();
            Run("no motion (no accumulation)", 10);
            Check("idempotence: the stage keeps the base while nothing rewrites the bone", ca.Hips.HasWritten && Bits(ca.Hips.Base, hb.localPosition));
            // condition ends: base written back once
            foreach (var (path, val, undoVal, what) in new (string, object, object, string)[]
            {
                ("chara.fk.groups.body", false, true, "Body off"), ("chara.fk.enabled", false, true, "FK off"), ("chara.fk.hipsOffset", 0f, h, "offset 0"),
            })
            {
                e.Set(a, path, val);
                e.Rt.ApplyNow();
                bool back = Bits(ha.localPosition, hb.localPosition);
                e.Step();
                Check($"{what}: base written back at once, no offset in the next step", back && Bits(ha.localPosition, hb.localPosition), F(ha.localPosition));
                e.Set(a, path, undoVal);
                e.Rt.ApplyNow();
                e.Step();
                Check($"{what} undone: the offset applies again", Bits(ha.localPosition, Expect()), F(ha.localPosition));
            }
            FkOptions.HipsOffset = false;
            e.Set(a, "chara.fk.hipsOffset", h * 2);
            e.Rt.ApplyNow();
            e.Step();
            Check("FkOptions.HipsOffset false (vanilla): no offset", Bits(ha.localPosition, hb.localPosition), F(ha.localPosition));
            FkOptions.HipsOffset = true;
            e.Set(a, "chara.fk.hipsOffset", h);
            e.Rt.ApplyNow();
            e.Step();
            // suspended inside the scope
            Vector3 ia, ib;
            using (StudioDefaultPoseScope.Enter(e.C(a)))
            using (StudioDefaultPoseScope.Enter(e.C(b)))
            {
                ia = ha.localPosition; ib = hb.localPosition;
            }
            Check("suspended inside StudioDefaultPoseScope", Bits(ia, ib), $"{F(ia)} vs {F(ib)}");
            // a card's position modifier on cf_J_Hips and the offset (D11, the real order: the modifiers' stage, then the offset)
            foreach (var id in new[] { a, b }) e.S.SetAnimation(id, Idle);
            e.Rt.ApplyNow();
            var ma = AddHipsModifier(e.C(a), ha); var mb = AddHipsModifier(e.C(b), hb);
            float maxErr = 0, drift = 0; Vector3 first = default;
            for (int i = 0; i < 10; i++)
            {
                e.Step();
                var d = ha.localPosition - Expect();
                maxErr = Mathf.Max(maxErr, Mathf.Max(Mathf.Abs(d.x), Mathf.Max(Mathf.Abs(d.y), Mathf.Abs(d.z))));
            }
            Check("D11: card ABMX position modifier + hips offset sum (playing clip, ≤ 1e-5)", maxErr <= 1e-5f, $"max {maxErr:G3}");
            foreach (var id in new[] { a, b }) e.Set(id, "chara.anime.ref", null);
            e.Rt.ApplyNow();
            for (int i = 0; i < 10; i++)
            {
                e.Step();
                if (i == 0) first = ha.localPosition;
            }
            drift = Mathf.Abs(ha.localPosition.y - first.y);
            var sum = hb.localPosition + new Vector3(0f, h, 0f);
            Check("D11 without motion: the modifier and the offset still sum, no accumulation over 9 steps", drift == 0f && Mathf.Abs(ha.localPosition.y - sum.y) <= 1e-5f,
                  $"drift {drift:G4}; hips {F(ha.localPosition)}, reference + h {F(sum)}");
        }
    }

    static BoneModifierApplier AddHipsModifier(CharaRealization r, Transform hips)
    {
        var m = r.Context.Bones ? r.Context.Bones : (r.Context.Bones = r.Root.AddComponent<BoneModifierApplier>());
        m.Add(new BoneModifierApplier.Modifier { Target = hips, Bone = "cf_J_Hips", Source = "test", Position = new Vector3(0f, 0.3f, 0f) });
        HS2Next.Motion.Modifiers.BoneModifierSetup.Sync(r.Context);
        return m;
    }

    // ================================================================ F13 FK node rows of the build hooks

    static string f13Bone;

    static FkNodeSet F13Set(params FkRow[] rows) => new FkNodeSet { Rows = rows };

    static void F13()
    {
        Section("F13", "FK node rows of the build hooks (CharaBuildHooks.RegisterFkNodes / FkNodes)");
        Check("official build: no FK node provider and no hook registered", CharaBuildHooks.FkNodeProviders.Count == 0 && CharaBuildHooks.Registered.Count == 0
              && CharaBuildHooks.FkNodes(services.Catalog) == null, string.Join(", ", CharaBuildHooks.FkNodeProviders.Concat(CharaBuildHooks.Registered.Select(r => r.ToString()))));
        const string PA = "test.f13a", PB = "test.f13b", HK = "test.f13hook";
        try
        {
            // two providers: merged in registration order, the first row of a key wins, a later different one is reported
            CharaBuildHooks.RegisterFkNodes(PA, _ => F13Set(new FkRow { No = 10000, Name = f13Bone ?? "cf_J_Kosi01", GroupColumn = 5, Level = 1 },
                                                           new FkRow { No = 10001, Name = "cf_J_Toes_B", GroupColumn = 6, Level = 1 }));
            CharaBuildHooks.RegisterFkNodes(PB, _ => F13Set(new FkRow { No = 10000, Name = "cf_J_Toes_X", GroupColumn = 5, Level = 1 },
                                                           new FkRow { No = 10003, Name = "cf_J_Toes_D", GroupColumn = 6, Level = 1 }));
            var set = CharaBuildHooks.FkNodes(services.Catalog);
            Check("two providers: first wins per key, a conflicting later definition is reported (fk.nodeConflict)",
                  set != null && set.Rows.Count == 3 && set.Rows[0].No == 10000 && set.Rows[0].Name != "cf_J_Toes_X" && set.Findings.CountOf("fk.nodeConflict") == 1,
                  set == null ? "null" : string.Join("; ", set.Rows) + " | " + string.Join("; ", set.Findings));

            // a character whose body mesh no hook replaced: nothing joined, the value of a node bone stays in the model
            using (var e = new Env())
            {
                var a = e.Add(femaleCard);
                var ca = e.Fk(a);
                e.Set(a, "chara.fk.enabled", true);
                e.Set(a, "chara.fk.groups.rightHand", true);
                e.Set(a, "chara.fk.bones[\"cf_J_Toes_Hallux1_R\"]", new Vec3(10f, 0f, 0f));
                e.Rt.ApplyNow();
                e.Step(2);
                Check("prefab body: no node rows joined, their value stays in the model", ca.Table.Additional.Count == 0 && !ca.Table.NodesJoined && e.St(a).Fk.Bones.ContainsKey("cf_J_Toes_Hallux1_R"),
                      $"nodes {ca.Table.Additional.Count}, joined {ca.Table.NodesJoined}");
                // a body bone that is no FK row: the node row of the test provider names it
                var ctx = e.C(a).Context;
                f13Bone = ctx.BodyBones.Keys.Where(n => n.StartsWith("cf_J_", StringComparison.Ordinal) && ca.Table.FindAny(n) == null)
                             .OrderBy(n => n, StringComparer.Ordinal).FirstOrDefault();
            }
            Check("a body bone without an FK row exists (the node row's target)", f13Bone != null, f13Bone);
            if (f13Bone == null) return;

            // a BodyMesh hook that marks the body mesh as replaced: the first FK application joins the providers' rows
            CharaBuildHooks.Register(HK, HookStage.BodyMesh, h => h.Chara.ReplacedBodyMesh = "f13");
            using (var e = new Env())
            {
                var a = e.Add(femaleCard);
                var ca = e.Fk(a);
                var node = ca.Table.Additional.FirstOrDefault(x => x.Bone && x.Bone.name == f13Bone);
                Check("replaced body: the node rows joined at the first FK application, the one whose bone exists found in its group",
                      ca.Table.NodesJoined && node != null && node.Group == FkGroup.RightHand && ca.Table.Additional.Count == 1 && e.C(a).Context.Stats.Hooks.ContainsKey(HK + "@BodyMesh"),
                      $"joined {ca.Table.NodesJoined}, nodes {ca.Table.Additional.Count} ({string.Join(", ", ca.Table.Additional.Select(x => x.Key + " " + x.Group))}); {ca.Table.Stats}");
                if (node != null)
                {
                    e.Set(a, "chara.fk.bones", Values(new[] { node }));
                    e.Set(a, "chara.fk.enabled", true);
                    e.Set(a, "chara.fk.groups.rightHand", true);
                    e.Rt.ApplyNow();
                    e.Step(2);
                    var bad = NotWritten(ca, new[] { node });
                    Check("the node bone is written while its group is applied", bad.Count == 0, Few(bad));
                    e.Set(a, "chara.fk.groups.rightHand", false);
                    e.Rt.ApplyNow();
                    Check("the node bone is reset on de-apply of its group", Bits(node.Bone.localRotation, node.Rest));
                }
            }
        }
        finally
        {
            CharaBuildHooks.UnregisterAll(PA);
            CharaBuildHooks.UnregisterAll(PB);
            CharaBuildHooks.UnregisterAll(HK);
            f13Bone = null;
        }
        Check("the test registrations are removed again", CharaBuildHooks.FkNodeProviders.Count == 0 && CharaBuildHooks.Registered.Count == 0);
    }

    // ================================================================ F14 hips position (3.4)

    static void F14()
    {
        Section("F14", "fk.hipsPosition");
        var pos = new Vec3(0.25f, 10.75f, -0.5f);
        const float h = -0.4f;
        using (var e = new Env())
        {
            var a = e.Add(femaleCard); var b = e.Add(femaleCard);
            foreach (var id in new[] { a, b }) e.S.SetAnimation(id, Idle);
            e.Rt.ApplyNow();
            var ha = Find(e.C(a), "cf_J_Hips"); var hb = Find(e.C(b), "cf_J_Hips");
            e.Set(a, "chara.fk.hipsPosition", pos);
            e.Rt.ApplyNow();
            e.Step(2);
            Check("FK off: the position is not applied", Bits(ha.localPosition, hb.localPosition), $"{F(ha.localPosition)} vs {F(hb.localPosition)}");
            e.Set(a, "chara.fk.enabled", true);
            e.Rt.ApplyNow();
            Check("FK + Body on: applied at once (outside a step)", Bits(ha.localPosition, U(pos)), F(ha.localPosition));
            int bad = 0; string first = null;
            void Run(string what, int n, Vector3 expect)
            {
                bad = 0; first = null;
                for (int i = 0; i < n; i++)
                {
                    e.Step();
                    if (!Bits(ha.localPosition, expect)) { bad++; first ??= F(ha.localPosition); }
                }
                Check($"{what}: cf_J_Hips = the position after each of {n} steps", bad == 0, bad + " steps" + (first != null ? ", first " + first : ""));
            }
            Run("playing clip", 10, U(pos));
            e.Set(a, "chara.fk.hipsOffset", h);
            e.Rt.ApplyNow();
            Run("with the offset: position + (0, h, 0)", 5, U(pos) + new Vector3(0, h, 0));
            FkOptions.HipsOffset = false;
            e.Set(a, "chara.fk.hipsOffset", h * 2);
            e.Rt.ApplyNow();
            Run("FkOptions.HipsOffset false: the position alone", 3, U(pos));
            FkOptions.HipsOffset = true;
            e.Set(a, "chara.fk.hipsOffset", 0f);
            foreach (var id in new[] { a, b }) e.Set(id, "chara.anime.ref", null);
            e.Rt.ApplyNow();
            Run("no motion (no accumulation)", 5, U(pos));
            foreach (var id in new[] { a, b }) e.S.SetAnimation(id, Idle);
            e.Rt.ApplyNow();
            e.Step(1);
            // condition ends: the clip's value comes back
            foreach (var (path, val, undoVal, what) in new (string, object, object, string)[]
            {
                ("chara.fk.groups.body", false, true, "Body off"), ("chara.fk.enabled", false, true, "FK off"), ("chara.fk.hipsPosition", null, pos, "position null"),
            })
            {
                e.Set(a, path, val);
                e.Rt.ApplyNow();
                e.Step();
                Check($"{what}: the clip's position after one step", Bits(ha.localPosition, hb.localPosition), $"{F(ha.localPosition)} vs {F(hb.localPosition)}");
                e.Set(a, path, undoVal);
                e.Rt.ApplyNow();
                e.Step();
                Check($"{what} undone: the position applies again", Bits(ha.localPosition, U(pos)), F(ha.localPosition));
            }
            using (StudioDefaultPoseScope.Enter(e.C(a)))
            using (StudioDefaultPoseScope.Enter(e.C(b)))
                Check("suspended inside StudioDefaultPoseScope", Bits(ha.localPosition, hb.localPosition), $"{F(ha.localPosition)} vs {F(hb.localPosition)}");
            e.Step();
            Check("after the scope: the position again", Bits(ha.localPosition, U(pos)), F(ha.localPosition));
        }
    }
}
