// MosaicTest, the rule of advanced pose (Phase 6.3 package J): advanced pose never moves a mosaic
// member off the skin it covers and never changes a member's shape. Numbers only: no image is made, opened or written.
//   AM0  census: the bones a body-surface member skins (weight above 0) that the body renderer does not, on the visual targets and on
//        the two vanilla body prefabs; the guard's set equals it. The list is in the report.
//   AM1  pose.bones.set on a guarded transform of a synthetic accessory with a member renderer, on a guarded bone of a test fixture:
//        unsupported, nothing stored; the shared bones stay allowed.
//   AM2  a scene file holding such entries and a member's blend override: loads, kept in the block, not applied,
//        advancedPose.mosaicProtected, written back.
//   AM3  corrections on shared bones: CPU-skinned patch vertices against the body vertices: the distances against the rest values
//        (bars: median + 0.005, max + 0.02 units); chara.mosaic unchanged.
//   AM4  blend overrides on a member and on the body renderer under a body-surface member: unsupported; pose.blendShapes lists them protected.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next;
using HS2Next.App;
using HS2Next.Chara;
using HS2Next.Chara.Edit.Units;
using HS2Next.Chara.Mosaic;
using HS2Next.Content;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using static MakerTestKit;
using Object = UnityEngine.Object;

public static partial class MosaicTest
{
    static void AdvancedPoseGroups(MakerCoreTest.Harness h)
    {
        // NewAmHost seeds the face for repeatable hosts; a batch's next member must find the switch as it was
        var faceSeed = HS2Next.Motion.Face.FaceSetup.Seed;
        try
        {
            if (Want("AM0")) Guard("AM0 census of the bones only a body-surface member skins", () => Am0(h));
            if (Want("AM1")) Guard("AM1 corrections refused", () => Am1());
            if (Want("AM2")) Guard("AM2 a scene file's entries", () => Am2());
            if (Want("AM3")) Guard("AM3 corrections on shared bones", () => Am3());
            if (Want("AM4")) Guard("AM4 blend overrides refused", () => Am4());
        }
        finally { HS2Next.Motion.Face.FaceSetup.Seed = faceSeed; }
    }

    // ------------------------------------------------------------ the host

    sealed class AmHost : IDisposable
    {
        public AppCore App;
        public SceneRuntime Rt => App.Runtime;
        public SceneSession Session => App.Session;
        public JObject Call(object q) => JObject.Parse(App.Call(q is string s ? s : JsonConvert.SerializeObject(q)));
        public JObject Ok(object q, string what)
        {
            var r = Call(q);
            Check("AM " + what, r.Value<bool?>("ok") == true, r.Value<bool?>("ok") == true ? "" : Short(r.ToString()));
            return r;
        }
        public bool Refused(object q, string code, string what)
        {
            var r = Call(q);
            return Check("AM " + what + " -> " + code, r.Value<bool?>("ok") == false && r.Value<string>("code") == code, Short(r.ToString()));
        }
        public void Dispose() { try { App?.Dispose(); } catch (Exception e) { Info("host dispose threw: " + e.Message); } }
    }

    static RuntimeServices amServices;
    static string AmDir => Path.Combine(OutDir, "mosaic_advpose");

    static AmHost NewAmHost()
    {
        amServices = amServices ?? SceneRuntimeTest.NewServices(new StringBuilder());
        HS2Next.Motion.Face.FaceSetup.Seed = 0x5eed;
        var s = AppSettings.Defaults(Array.Empty<string>());
        s.Content = CardResolveTest.Settings;
        s.UserData = Path.Combine(AmDir, "userdata");
        s.Bridge.OutputDir = Path.Combine(AmDir, "out");
        var o = amServices.NewSessionOptions();
        o.Ids = new CountingIdSource(0x63ad0a5eedUL);
        return new AmHost { App = AppCore.ForTests(amServices, s, null, o) };
    }

    static string AmLoad(AmHost h, bool male)
    {
        var t = TestTargets.Load().visual;
        string rel = male ? t.male : t.female;
        foreach (var pre in new[] { "chara/female/", "chara/male/" })
            if (rel.StartsWith(pre, StringComparison.OrdinalIgnoreCase)) rel = rel.Substring(pre.Length);
        var r = h.Ok(new { cmd = "chara.load", path = rel, sex = male ? "male" : "female" }, "chara.load");
        string key = (string)r["key"] ?? (string)r["added"]?[0]?["key"];
        h.Ok(new { cmd = "chara.anime", key, group = 19, category = 0, no = 0, time = 0.5f, speed = 0f }, "chara.anime idle");
        h.Ok(new { cmd = "chara.motion", key, autoStep = false, physics = false, reset = true }, "chara.motion by hand");
        h.Rt.ApplyNow();
        h.Rt.FindChara(key).Motion.Step(1f / 60f);
        return key;
    }

    static CharaAdvancedPose AmBlock(AmHost h, string id) => (h.Session.Model.Find(id)?.State as CharaNodeState)?.AdvancedPose;

    static readonly float[] AmRot = { 10f, 20f, 30f };

    static Transform AmChild(Transform parent, string name, bool renderer = false)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        if (renderer) go.AddComponent<MeshRenderer>();
        return go.transform;
    }

    // a small skinned renderer on one bone, with one shape
    static SkinnedMeshRenderer AmSkinned(Transform parent, string name, Transform bone, List<Object> trash)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var mesh = new Mesh { name = name };
        mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
        mesh.triangles = new[] { 0, 1, 2 };
        mesh.bindposes = new[] { Matrix4x4.identity };
        mesh.boneWeights = new[] { new BoneWeight { weight0 = 1f }, new BoneWeight { weight0 = 1f }, new BoneWeight { weight0 = 1f } };
        var d = new[] { Vector3.forward, Vector3.forward, Vector3.forward };
        mesh.AddBlendShapeFrame("am_shape", 100f, d, null, null);
        var s = go.AddComponent<SkinnedMeshRenderer>();
        s.sharedMesh = mesh;
        s.bones = new[] { bone };
        trash.Add(mesh);
        trash.Add(go);
        return s;
    }

    // ------------------------------------------------------------ AM0

    // the bone indices with a weight above 0, read from the mesh's own weights
    static HashSet<int> AmWeighted(Mesh m)
    {
        var used = new HashSet<int>();
        foreach (var w in m.boneWeights)
        {
            if (w.weight0 > 0f) used.Add(w.boneIndex0);
            if (w.weight1 > 0f) used.Add(w.boneIndex1);
            if (w.weight2 > 0f) used.Add(w.boneIndex2);
            if (w.weight3 > 0f) used.Add(w.boneIndex3);
        }
        return used;
    }

    static List<string> AmBones(SkinnedMeshRenderer s)
    {
        var b = s.bones;
        return AmWeighted(s.sharedMesh).Where(i => i >= 0 && i < b.Length && b[i]).Select(i => b[i].name).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
    }

    static void Am0(MakerCoreTest.Harness h)
    {
        foreach (var (label, file) in new[] { ("BR-Chan", FemaleCard), ("HERO107", MaleCard) })
            using (var f = new Disposer(h.Build(file)))
            {
                if (!Check($"AM0 {label} builds", f.B.Ok, f.B.Error ?? "")) continue;
                var ctx = f.B.Context;
                var body = ctx.BodyRenderer;
                var bodyBones = new HashSet<string>(AmBones(body), StringComparer.Ordinal);
                var surface = MosaicSet.SurfaceMembers(ctx);
                Info($"AM0 {label}: body {body.name}: {bodyBones.Count} bones with a weight of {body.bones.Length} listed; {surface.Count} body-surface members");
                var census = new HashSet<Transform>();
                foreach (var s in surface)
                {
                    var names = AmBones(s);
                    var only = names.Where(n => !bodyBones.Contains(n)).ToList();
                    Info($"AM0 {label}: {s.name}: bones with a weight: {string.Join(", ", names)}");
                    Info($"AM0 {label}: {s.name}: not skinned by the body: [{string.Join(", ", only)}]");
                    var b = s.bones;
                    foreach (int i in AmWeighted(s.sharedMesh))
                        if (i >= 0 && i < b.Length && b[i] && only.Contains(b[i].name)) census.Add(b[i]);
                }
                var guard = MosaicSet.SurfaceOnlyBones(ctx);
                Check($"AM0 {label}: the guard's set equals the census", guard.SetEquals(census), $"census {census.Count}, guard {guard.Count}: [{string.Join(", ", census.Select(x => x.name))}]");
                Check(label == "HERO107" ? "AM0 HERO107: no body-surface member" : "AM0 BR-Chan: the two patches are body-surface members",
                    label == "HERO107" ? surface.Count == 0 : surface.Count == 2 && surface.Any(x => x.name == "o_mnpa") && surface.Any(x => x.name == "o_mnpb"), string.Join(", ", surface.Select(x => x.name)));
                Info($"AM0 {label} RESULT: {guard.Count} bones only a body-surface member skins");
                foreach (var n in new[] { "cf_J_Kosi02", "cf_J_Kokan", "cf_J_LegUp00_L", "cm_J_dan100_00" })
                    if (ctx.BodyBones.TryGetValue(n, out var t) && t)
                        Check($"AM0 {label}: {n} is not guarded", !MosaicSet.GuardsBone(ctx, t));
            }

        // the two vanilla body prefabs (mesh and weights as shipped), by bone name
        foreach (var name in new[] { "p_cf_body_00", "p_cm_body_00" })
        {
            var prefab = h.Catalog.LoadAsset<GameObject>("chara/oo_base.unity3d", name);
            if (!Check($"AM0 prefab {name} loads", prefab)) continue;
            var all = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var body = all.FirstOrDefault(x => x.name.StartsWith("o_body_", StringComparison.Ordinal));
            if (!Check($"AM0 prefab {name}: a readable body mesh", body && body.sharedMesh && body.sharedMesh.isReadable)) continue;
            var bodyBones = new HashSet<string>(AmBones(body), StringComparer.Ordinal);
            int n = 0;
            foreach (var s in all)
            {
                if (s.name != "o_mnpa" && s.name != "o_mnpb") continue;
                n++;
                var names = AmBones(s);
                var only = names.Where(x => !bodyBones.Contains(x)).ToList();
                Info($"AM0 prefab {name}: {s.name}: {names.Count} bones with a weight: {string.Join(", ", names)}; not skinned by the body: [{string.Join(", ", only)}]");
            }
            Info($"AM0 prefab {name}: {n} patches, body {body.name} {bodyBones.Count} bones with a weight");
        }
    }

    // ------------------------------------------------------------ AM1

    static void Am1()
    {
        var trash = new List<Object>();
        using (var h = NewAmHost())
        {
            string key = AmLoad(h, male: false);
            var r = h.Rt.FindChara(key);
            var ctx = r.Context;
            var slot = ctx.Accessories.FirstOrDefault(x => x != null && x.Object && r.AccessoryIdOf(x) != null);
            if (!Check("AM1 an accessory on the target", slot != null)) return;
            string id = r.AccessoryIdOf(slot);
            var top = slot.Object.transform;

            // a synthetic part: am_part/o_gomu (a member by name), am_part/am_shader (a member by its material), am_arm a bone of a skinned member, am_side nothing of the kind
            var part = AmChild(top, "am_part");
            var member = AmChild(part, "o_gomu", renderer: true);
            var viaShader = AmChild(part, "am_shader", renderer: true);
            var mat = new Material(Shader.Find(MosaicSet.ShaderName));
            trash.Add(mat);
            viaShader.GetComponent<MeshRenderer>().sharedMaterial = mat;
            var arm = AmChild(top, "am_arm");
            var skinned = AmSkinned(top, "o_dan_f", arm, trash);
            var side = AmChild(top, "am_side");
            string A(string n) => "acc:" + id + "/" + n;
            string json0 = AmBlock(h, key).ToJson();
            foreach (var n in new[] { "o_gomu", "am_shader", "am_part", "o_dan_f", "am_arm", top.name })
            {
                h.Refused(new { cmd = "pose.bones.set", key, bone = A(n), rot = AmRot }, "unsupported", "pose.bones.set " + (n == top.name ? "the slot object" : n));
                Check("AM1 " + (n == top.name ? "the slot object" : n) + ": nothing stored", AmBlock(h, key).ToJson() == json0);
            }
            var ok = h.Ok(new { cmd = "pose.bones.set", key, bone = A("am_side"), rot = AmRot, keepMissing = true }, "pose.bones.set a sibling without a member");
            // am_side lies under the slot object beside the members: not an ancestor, not a bone of one
            Check("AM1 the sibling is stored", AmBlock(h, key).Bones.ContainsKey(A("am_side")), Short(ok.ToString()));
            h.Ok(new { cmd = "pose.bones.clear", key, all = true }, "pose.bones.clear");

            // the body's shared bones and the separate part's bones stay allowed
            foreach (var n in new[] { "cf_J_Kosi02", "cf_J_Kokan", "cf_J_LegUp00_L", "cm_J_dan100_00" })
            {
                Check($"AM1 {n}: allowed", !MosaicSet.GuardsBone(ctx, ctx.BodyBones.TryGetValue(n, out var b) ? b : null));
                h.Ok(new { cmd = "pose.bones.set", key, bone = n, rot = AmRot }, "pose.bones.set " + n);
                h.Ok(new { cmd = "pose.bones.clear", key, bone = n }, "pose.bones.clear " + n);
            }

            // a bone the census finds none of on this body: a fixture adds one, skinned by a patch-named member alone
            if (Check("AM1 the census's set is empty on the vanilla body", MosaicSet.SurfaceOnlyBones(ctx).Count == 0, MosaicSet.SurfaceOnlyBones(ctx).Count + " bones"))
            {
                var kosi = ctx.BodyBones["cf_J_Kosi02"];
                var extra = AmChild(kosi, "am_surface_only");
                ctx.BodyBones["am_surface_only"] = extra;
                var nm = HS2Next.Chara.Edit.EditContext.Find(ctx.Body.transform, "N_mnpb");
                var patch = AmSkinned(nm ? nm : ctx.Body.transform, "o_mnpa", extra, trash);
                RunStage(ctx);
                Check("AM1 the fixture's bone is in the guard's set", MosaicSet.SurfaceOnlyBones(ctx).Contains(extra) && MosaicSet.GuardsBone(ctx, extra));
                string json1 = AmBlock(h, key).ToJson();
                h.Refused(new { cmd = "pose.bones.set", key, bone = "am_surface_only", rot = AmRot }, "unsupported", "pose.bones.set a bone only a patch skins");
                Check("AM1 nothing stored", AmBlock(h, key).ToJson() == json1);
                Object.DestroyImmediate(patch.gameObject);
                ctx.BodyBones.Remove("am_surface_only");
                Object.DestroyImmediate(extra.gameObject);
                RunStage(ctx);
                Check("AM1 the fixture gone: empty again", MosaicSet.SurfaceOnlyBones(ctx).Count == 0);
            }
            foreach (var o in trash) if (o) Object.DestroyImmediate(o);
            foreach (var t in new[] { part, side, arm }) if (t) Object.DestroyImmediate(t.gameObject);
        }
    }

    // ------------------------------------------------------------ AM2

    static void Am2()
    {
        var trash = new List<Object>();
        using (var h = NewAmHost())
        {
            string key = AmLoad(h, male: false);
            var r = h.Rt.FindChara(key);
            var slot = r.Context.Accessories.FirstOrDefault(x => x != null && x.Object && r.AccessoryIdOf(x) != null);
            if (!Check("AM2 an accessory on the target", slot != null)) return;
            string id = r.AccessoryIdOf(slot);
            string guarded = "acc:" + id + "/am_part", plain = "cf_J_Hand_Index02_L";
            var c = new BoneCorrection(new Vec3(0f, 0f, 0f), new Vec3(10f, 20f, 30f), new Vec3(1f, 1f, 1f));
            var bones = new SortedDictionary<string, BoneCorrection>(StringComparer.Ordinal) { [guarded] = c, [plain] = c };
            var blend = new SortedDictionary<string, BlendValues>(StringComparer.Ordinal);
            string memberAddr = "body/o_mnpb", bodyAddr = "body/" + r.Context.BodyRenderer.name;
            foreach (var a in new[] { memberAddr, bodyAddr })
            {
                var v = new BlendValues();
                v.Weights["am_shape"] = 50f;
                blend[a] = v;
            }
            Check("AM2 the block set directly (a file's way)", h.Session.Set(key, "chara.advancedPose.bones", bones).Ok && h.Session.Set(key, "chara.advancedPose.blendShapes", blend).Ok);
            h.Rt.ApplyNow();
            string json0 = AmBlock(h, key).ToJson();
            var save = h.Ok(new { cmd = "scene.save", name = "am2", overwrite = true, thumb = "placeholder", clock = "2026-11-04T00:00:00Z" }, "scene.save");
            string path = (string)save["path"];
            h.Ok(new { cmd = "scene.new" }, "scene.new");
            h.Rt.ApplyNow();
            var load = h.Ok(new { cmd = "scene.load", path }, "scene.load");
            h.Rt.ApplyNow();
            r = h.Rt.FindChara(key);
            if (!Check("AM2 the character after the load", r != null && r.Context != null)) return;
            h.Ok(new { cmd = "chara.motion", key, autoStep = false, physics = false }, "chara.motion by hand");
            Check("AM2 kept in the block", AmBlock(h, key).ToJson() == json0, Short(AmBlock(h, key).ToJson()));
            var st = AdvancedPoseDriver.Of(r);
            Check("AM2 the blend overrides are kept and not applied", st != null && st.ProtectedRenderers.Contains(memberAddr) && st.ProtectedRenderers.Contains(bodyAddr) && !r.Motion.Stages.Any(x => x.Name == "pose.blend"),
                st == null ? "no state" : string.Join(",", st.ProtectedRenderers));
            int Protected() { int n = 0; foreach (var x in h.Rt.Findings) if (x.Code == AdvancedPoseFindings.MosaicProtected && x.NodeId == key) n++; return n; }
            Check("AM2 advancedPose.mosaicProtected once", Protected() == 1, "" + Protected());
            var loadText = load.ToString();
            Check("AM2 the entry whose part is not there is stale, the other acts", st.StaleBones.Contains(guarded) && st.Set.Of(plain) != null, string.Join(",", st.StaleBones));

            // the part comes back with a member: the entry stays kept and not applied
            slot = r.Context.Accessories.FirstOrDefault(x => x != null && x.Object && r.AccessoryIdOf(x) == id);
            var part = AmChild(slot.Object.transform, "am_part");
            var member = AmChild(part, "o_gomu", renderer: true);
            var local0 = part.localRotation;
            AdvancedPoseDriver.Reresolve(r);
            st = AdvancedPoseDriver.Of(r);
            r.Motion.Step(1f / 60f);
            Check("AM2 a transform that holds a member: kept, not applied", st.ProtectedBones.Contains(guarded) && st.Set.Of(guarded) == null && RotationCompare.Deg(part.localRotation, local0) == 0, string.Join(",", st.ProtectedBones));
            Check("AM2 still one finding", Protected() == 1, "" + Protected());

            // written back
            var save2 = h.Ok(new { cmd = "scene.doc", capture = false }, "scene.doc");
            string text = (string)save2["scene"];
            Check("AM2 written back", text != null && text.IndexOf(guarded, StringComparison.Ordinal) >= 0 && text.IndexOf(memberAddr, StringComparison.Ordinal) >= 0 && text.IndexOf(bodyAddr, StringComparison.Ordinal) >= 0);
            Check("AM2 the block after all that", AmBlock(h, key).ToJson() == json0);
            Object.DestroyImmediate(part.gameObject);
        }
    }

    // ------------------------------------------------------------ AM3

    // world positions skinned on the CPU (the way of MakerAccessoriesTest); null when the mesh is not readable
    static Vector3[] AmSkinnedVertices(SkinnedMeshRenderer r)
    {
        var m = r.sharedMesh;
        if (!m || !m.isReadable) return null;
        var v = m.vertices; var bw = m.boneWeights; var bp = m.bindposes; var b = r.bones;
        if (bw.Length != v.Length || bp.Length != b.Length) return null;
        var mats = new Matrix4x4[b.Length];
        for (int i = 0; i < b.Length; i++) mats[i] = b[i] ? b[i].localToWorldMatrix * bp[i] : Matrix4x4.zero;
        var o = new Vector3[v.Length];
        for (int k = 0; k < v.Length; k++)
        {
            var w = bw[k];
            Vector3 p = Vector3.zero;
            if (w.weight0 > 0f) p += w.weight0 * mats[w.boneIndex0].MultiplyPoint3x4(v[k]);
            if (w.weight1 > 0f) p += w.weight1 * mats[w.boneIndex1].MultiplyPoint3x4(v[k]);
            if (w.weight2 > 0f) p += w.weight2 * mats[w.boneIndex2].MultiplyPoint3x4(v[k]);
            if (w.weight3 > 0f) p += w.weight3 * mats[w.boneIndex3].MultiplyPoint3x4(v[k]);
            o[k] = p;
        }
        return o;
    }

    // for each member vertex: the distance to the nearest body vertex; the median and the largest
    static (double median, double max) AmDistances(Vector3[] member, Vector3[] body)
    {
        var d = new double[member.Length];
        for (int i = 0; i < member.Length; i++)
        {
            double best = double.MaxValue;
            foreach (var b in body)
            {
                double dx = member[i].x - b.x, dy = member[i].y - b.y, dz = member[i].z - b.z;
                double q = dx * dx + dy * dy + dz * dz;
                if (q < best) best = q;
            }
            d[i] = Math.Sqrt(best);
        }
        Array.Sort(d);
        return (d[d.Length / 2], d[d.Length - 1]);
    }

    static void Am3()
    {
        using (var h = NewAmHost())
        {
            string key = AmLoad(h, male: false);
            var r = h.Rt.FindChara(key);
            var ctx = r.Context;
            var body = ctx.BodyRenderer;
            var patches = MosaicSet.SurfaceMembers(ctx);
            if (!Check("AM3 the patches", patches.Count == 2, patches.Count + " members")) return;
            var rest = new Dictionary<string, (double median, double max)>();
            var restV = new Dictionary<string, Vector3[]>();
            var bodyV = AmSkinnedVertices(body);
            if (!Check("AM3 readable meshes", bodyV != null && patches.All(p => AmSkinnedVertices(p) != null))) return;
            foreach (var p in patches)
            {
                restV[p.name] = AmSkinnedVertices(p);
                rest[p.name] = AmDistances(restV[p.name], bodyV);
                Info($"AM3 rest {p.name}: median {rest[p.name].median:F4}, max {rest[p.name].max:F4} units");
            }
            string mosaic0 = h.Call(new { cmd = "chara.mosaic", key }).ToString();
            var cases = new (string label, object[] sets)[]
            {
                ("cf_J_Kosi02 scale 1.2", new object[] { new { cmd = "pose.bones.set", key, bone = "cf_J_Kosi02", scale = new[] { 1.2f, 1.2f, 1.2f } } }),
                ("cf_J_LegUp00_L rotation 40 x", new object[] { new { cmd = "pose.bones.set", key, bone = "cf_J_LegUp00_L", rot = new[] { 40f, 0f, 0f } } }),
                ("cf_J_LegUp00_L rotation 40 y", new object[] { new { cmd = "pose.bones.set", key, bone = "cf_J_LegUp00_L", rot = new[] { 0f, 40f, 0f } } }),
                ("cf_J_LegUp00_L rotation 40 z", new object[] { new { cmd = "pose.bones.set", key, bone = "cf_J_LegUp00_L", rot = new[] { 0f, 0f, 40f } } }),
                ("cf_J_Kokan position 0.05 x", new object[] { new { cmd = "pose.bones.set", key, bone = "cf_J_Kokan", pos = new[] { 0.05f, 0f, 0f } } }),
                ("cf_J_Kokan position 0.05 y", new object[] { new { cmd = "pose.bones.set", key, bone = "cf_J_Kokan", pos = new[] { 0f, 0.05f, 0f } } }),
                ("cf_J_Kokan position 0.05 z", new object[] { new { cmd = "pose.bones.set", key, bone = "cf_J_Kokan", pos = new[] { 0f, 0f, 0.05f } } }),
            };
            double worstMedian = 0, worstMax = 0, worstMoved = 0;
            void One(string label, IEnumerable<object> sets, bool bars)
            {
                foreach (var s in sets) h.Ok(s, label);
                h.Rt.ApplyNow();
                for (int i = 0; i < 3; i++) r.Motion.Step(1f / 60f);
                var bv = AmSkinnedVertices(body);
                foreach (var p in patches)
                {
                    var v = AmSkinnedVertices(p);
                    double moved = Enumerable.Range(0, v.Length).Max(i => (double)(v[i] - restV[p.name][i]).magnitude);
                    var d = AmDistances(v, bv);
                    double dm = d.median - rest[p.name].median, dx = d.max - rest[p.name].max;
                    if (bars) { worstMedian = Math.Max(worstMedian, dm); worstMax = Math.Max(worstMax, dx); worstMoved = Math.Max(worstMoved, moved); }
                    Info($"AM3 {label}, {p.name}: the patch moved up to {moved:F4}; median {d.median:F4} ({dm:+0.0000;-0.0000}), max {d.max:F4} ({dx:+0.0000;-0.0000}) units");
                    if (bars) Check($"AM3 {label}, {p.name}: within the bars (median + 0.005, max + 0.02)", dm <= 0.005 && dx <= 0.02, $"median {dm:+0.0000;-0.0000}, max {dx:+0.0000;-0.0000}");
                }
                Check($"AM3 {label}: chara.mosaic unchanged", h.Call(new { cmd = "chara.mosaic", key }).ToString() == mosaic0);
                h.Ok(new { cmd = "pose.bones.clear", key, all = true }, "pose.bones.clear");
                h.Rt.ApplyNow();
                r.Motion.Step(1f / 60f);
            }
            foreach (var (label, sets) in cases) One(label, sets, bars: true);
            Info($"AM3 RESULT: the largest growth over the rest value: median {worstMedian:F4}, max {worstMax:F4} units, the patches moved up to {worstMoved:F4} (1 unit = 10 cm)");
            // the bones the patches are skinned to directly: reported, no bar
            foreach (var (label, sets) in new (string, object[])[]
            {
                ("cf_J_Kokan rotation 40 (no bar)", new object[] { new { cmd = "pose.bones.set", key, bone = "cf_J_Kokan", rot = new[] { 40f, 0f, 0f } } }),
                ("cf_J_Kokan scale 1.3 (no bar)", new object[] { new { cmd = "pose.bones.set", key, bone = "cf_J_Kokan", scale = new[] { 1.3f, 1.3f, 1.3f } } }),
                ("cf_J_Kosi02_s scale 1.2 (no bar)", new object[] { new { cmd = "pose.bones.set", key, bone = "cf_J_Kosi02_s", scale = new[] { 1.2f, 1.2f, 1.2f } } }),
                ("cf_J_LegUp01_s_L rotation 40 (no bar)", new object[] { new { cmd = "pose.bones.set", key, bone = "cf_J_LegUp01_s_L", rot = new[] { 40f, 0f, 0f } } }),
                ("cf_J_Ana position 0.05 (no bar)", new object[] { new { cmd = "pose.bones.set", key, bone = "cf_J_Ana", pos = new[] { 0f, 0.05f, 0f } } }),
            })
                One(label, sets, bars: false);
        }
    }

    // ------------------------------------------------------------ AM4

    static void Am4()
    {
        var trash = new List<Object>();
        using (var h = NewAmHost())
        {
            string key = AmLoad(h, male: false);
            var r = h.Rt.FindChara(key);
            var ctx = r.Context;
            string bodyAddr = "body/" + ctx.BodyRenderer.name;
            var o_mnpb = AdvancedPoseParts.Renderer(r, "body/o_mnpb");
            Info($"AM4 shapes: o_mnpb {(o_mnpb && o_mnpb.sharedMesh ? o_mnpb.sharedMesh.blendShapeCount : -1)}, {ctx.BodyRenderer.name} {ctx.BodyRenderer.sharedMesh.blendShapeCount}");
            Check("AM4 the guard: the patch and the body under it", MosaicSet.GuardsBlend(ctx, o_mnpb) && MosaicSet.GuardsBlend(ctx, ctx.BodyRenderer));
            string json0 = AmBlock(h, key).ToJson();
            foreach (var a in new[] { "body/o_mnpb", bodyAddr })
            {
                h.Refused(new { cmd = "pose.blendShapes.set", key, renderer = a, shape = "am_shape", weight = 50f }, "unsupported", "pose.blendShapes.set " + a);
                Check("AM4 nothing stored: " + a, AmBlock(h, key).ToJson() == json0);
            }
            var slot = ctx.Accessories.FirstOrDefault(x => x != null && x.Object && r.AccessoryIdOf(x) != null);
            if (Check("AM4 an accessory on the target", slot != null))
            {
                string id = r.AccessoryIdOf(slot);
                var s = AmSkinned(slot.Object.transform, "o_gomu", slot.Object.transform, trash);
                var plain = AmSkinned(slot.Object.transform, "am_plain", slot.Object.transform, trash);
                string a = "acc:" + id + "/o_gomu", ap = "acc:" + id + "/am_plain";
                h.Refused(new { cmd = "pose.blendShapes.set", key, renderer = a, shape = "am_shape", weight = 50f }, "unsupported", "pose.blendShapes.set a member with shapes");
                Check("AM4 its weight unchanged", s.GetBlendShapeWeight(0) == 0f);
                var list = h.Ok(new { cmd = "pose.blendShapes", key, part = "acc:" + id }, "pose.blendShapes");
                var row = (list["renderers"] as JArray)?.OfType<JObject>().FirstOrDefault(x => (string)x["renderer"] == a);
                Check("AM4 listed protected", row != null && (bool?)row["protected"] == true, Short(row?.ToString()));
                var rowp = (list["renderers"] as JArray)?.OfType<JObject>().FirstOrDefault(x => (string)x["renderer"] == ap);
                Check("AM4 another renderer: not protected", rowp != null && (bool?)rowp["protected"] == false, Short(rowp?.ToString()));
                h.Ok(new { cmd = "pose.blendShapes.set", key, renderer = ap, shape = "am_shape", weight = 50f }, "pose.blendShapes.set another renderer");
                Check("AM4 another renderer: it acts", plain.GetBlendShapeWeight(0) == 50f, "" + plain.GetBlendShapeWeight(0));
                h.Ok(new { cmd = "pose.blendShapes.clear", key, all = true }, "pose.blendShapes.clear");
                foreach (var o in trash) if (o) Object.DestroyImmediate(o);
                trash.Clear();
            }
            var all = h.Ok(new { cmd = "pose.blendShapes", key }, "pose.blendShapes all");
            var rows = (all["renderers"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
            foreach (var x in rows.Where(y => (string)y["renderer"] == "body/o_mnpb" || (string)y["renderer"] == bodyAddr || (string)y["renderer"] == "body/o_mnpa"))
                Check("AM4 " + (string)x["renderer"] + " listed protected", (bool?)x["protected"] == true);
            var head = rows.FirstOrDefault(y => (string)y["renderer"] == "head/o_head");
            Check("AM4 o_head is not protected", head != null && (bool?)head["protected"] == false);
        }
        // the male body has no body-surface member: its renderer is not guarded
        using (var h = NewAmHost())
        {
            string key = AmLoad(h, male: true);
            var ctx = h.Rt.FindChara(key).Context;
            Check("AM4 male: the body renderer is not guarded", !MosaicSet.GuardsBlend(ctx, ctx.BodyRenderer));
        }
    }
}
