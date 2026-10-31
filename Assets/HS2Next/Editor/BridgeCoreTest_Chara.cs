// Package W2's checks of BridgeCoreTest (C10–C17): characters, animation and face, motion,
// determinism, FK and poses, the HS2 shape of the dumps, number forms and timings — every request through
// AppCore.Call (B's in-process dispatcher), facades only to check results. W1's commands (scene, scene.doc, scene.new,
// scene.save / load, undo, history, app.options, item.add, obj.transform, camera.state) are used when they are
// registered; until then the checks that need them fall back to the session (WARN) or are skipped (WARN).
// The HS2-shape helpers (ShapeObject, Kind, the member orders of Appendix B) are shared with BridgeTest_Motion.cs (H1–H5).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.App;
using HS2Next.App.Commands;
using HS2Next.Content;
using HS2Next.Import.Pose;
using HS2Next.Motion;
using HS2Next.Motion.DynamicBones;
using HS2Next.Motion.Face;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using static BridgeCoreTest.W2Shape;

public static partial class BridgeCoreTest
{
    /// <summary>Package W2's shared test material (the HS2-shape helpers, fixture constants), also used by BridgeTest_Motion.cs.</summary>
    public static class W2Shape
    {
        /// <summary>Face random seed of the determinism checks (C13, S5): two characters draw the same blink / mouth sequence.</summary>
        public const int FaceSeed = HS2Next.App.BridgeAcceptance.FaceSeed;   // 0x2a4b, shared with the runtime S5
        /// <summary>Compare case 1 of Tools/compare_poses.json (a plain vanilla file, FK Neck + Body).</summary>
        public const string PoseCase1 = "STANDING/Floor/Pose 1.dat";
        public static string CompareDir => Path.Combine(ContentSettings.ProjectRoot, "Cache", "compare");
        public static string FemaleRecordings => Path.Combine(CompareDir, "BR_Chan_HS2_Rev2");
        public static string PoseRoot => Path.Combine(CardResolveTest.GameRoot, "UserData", "studio", "pose");

        // ================================================================ HS2 shape (C15, H1–H5)

        public static readonly string[] DumpOrder = { "bodyCount", "headRoot", "headRootParent", "root", "rootParent", "anim", "count", "frame", "updatePending", "bones" };
        public static readonly string[] BoneOrder = { "name", "path", "lp", "lq", "le", "ls" };
        public static readonly string[] FaceOrder = { "head", "renderers", "expression", "fbs", "eyeLook", "neckLook", "frame" };
        public static readonly string[] RendererOrder = { "name", "path", "enabled", "active", "mesh", "blendShapes", "weights", "materials" };
        public static readonly string[] MaterialOrder = { "name", "shader", "_NamidaScale", "_Texture4Scale", "_Smoothness", "_HohoAkaRate" };
        public static readonly string[] ExpressionOrder =
        {
            "eyebrow", "eyes", "eyebrowId", "eyesId", "mouth", "eyesOpen", "mouthOpen", "blink", "eyesLook", "neckLook", "tears", "blush",
            "eyebrowPtn", "eyesPtn", "eyesOpenMax", "eyebrowOpenMax", "blinkFlag", "mouthPtn", "mouthOpenMax", "mouthFixed", "tearsRate",
            "hohoAkaRate", "lookEyesPtn", "lookEyesRate", "lookNeckPtn", "lookNeckRate", "studioMouthOpen",
        };
        public static readonly string[] FkOrder = { "fkEnabled", "enableFK", "enableIK", "activeFK", "activeIK", "expression", "bones" };
        public static readonly string[] FkBoneOrder = { "bone", "key", "group", "groupFlags", "level", "boneWeight", "rot", "pos", "scale" };
        public static readonly string[] CameraOrder =
        {
            "name", "position", "rotation", "euler", "forward", "up", "fov", "aspect", "near", "far", "orthographic", "pixelWidth", "pixelHeight",
            "screen", "clearFlags", "background", "cullingMask", "allowHDR", "allowMSAA", "renderingPath", "frame", "studio",
        };
        public static readonly string[] StudioCameraOrder = { "target", "rotate", "distance", "fov", "cameraControlOnMain", "basePosition", "baseRotation" };
        public static readonly string[] DescribeOrder = { "key", "name", "kind", "pos", "rot", "scale", "worldPos", "parentName", "fullname", "sex", "anime" };
        public static readonly string[] AnimeOrder = { "group", "category", "no", "name", "speed", "length", "normalizedTime" };
        /// <summary>Envelope members of a reply (not part of the command's result).</summary>
        public static readonly string[] Envelope = { "ok", "cmd", "id", "warnings" };

        /// <summary>JSON kind for the shape comparison: number (integers and floats alike), string, bool, array, object, null.</summary>
        public static string Kind(JToken t)
        {
            if (t == null) return "absent";
            switch (t.Type)
            {
                case JTokenType.Integer:
                case JTokenType.Float: return "number";
                case JTokenType.String: return "string";
                case JTokenType.Boolean: return "bool";
                case JTokenType.Array: return "array";
                case JTokenType.Object: return "object";
                case JTokenType.Null: return "null";
                default: return t.Type.ToString();
            }
        }

        /// <summary>Same JSON kind; a non-finite number (HS2 Infinity / NaN) and null are the same.</summary>
        public static bool SameKind(JToken a, JToken b)
        {
            string ka = Kind(a), kb = Kind(b);
            if (ka == kb) return true;
            return (ka == "null" && kb == "number") || (ka == "number" && kb == "null");
        }

        /// <summary>A copy of a reply without the envelope members.</summary>
        public static JObject Result(JObject reply)
        {
            var o = (JObject)reply.DeepClone();
            foreach (var e in Envelope) o.Remove(e);
            return o;
        }

        /// <summary>
        /// The shape differences of one object: HS2's members (<paramref name="hs2Order"/>) that HS2Next writes keep HS2's
        /// relative order and come before every HS2Next addition; a member the recording <paramref name="rec"/> has must be
        /// present unless listed in <paramref name="optional"/> (or <paramref name="subset"/>: HS2Next writes a subset); kinds agree
        /// with the recording (numbers alike, null ~ non-finite) and fixed-size number arrays have the recording's length.
        /// </summary>
        public static void ShapeObject(List<string> diffs, string where, IList<string> hs2Order, JObject rec, JObject next,
            ICollection<string> optional = null, bool subset = false)
        {
            if (next == null) { diffs.Add(where + ": missing"); return; }
            var names = next.Properties().Select(p => p.Name).ToList();
            int last = -1;
            foreach (var m in hs2Order)
            {
                int i = names.IndexOf(m);
                if (i < 0)
                {
                    if (rec != null && rec[m] != null && !subset && (optional == null || !optional.Contains(m))) diffs.Add(where + ": HS2 member '" + m + "' missing");
                    continue;
                }
                if (i < last) diffs.Add(where + ": '" + m + "' out of HS2's order");
                last = Math.Max(last, i);
                if (rec == null || !rec.TryGetValue(m, out var rv)) continue;
                var nv = next[m];
                if (!SameKind(rv, nv)) diffs.Add(where + "." + m + ": kind " + Kind(rv) + " (HS2) vs " + Kind(nv));
                else if (rv is JArray ra && nv is JArray na && ra.Count > 0 && ra.Count <= 4 && ra.All(x => Kind(x) == "number" || Kind(x) == "null") && ra.Count != na.Count)
                    diffs.Add(where + "." + m + ": " + ra.Count + " numbers (HS2) vs " + na.Count);
            }
            for (int i = 0; i < names.Count; i++)
                if (!hs2Order.Contains(names[i]) && i < last) diffs.Add(where + ": addition '" + names[i] + "' before HS2's members");
            if (rec != null && !subset)
                foreach (var p in rec.Properties())
                    if (!hs2Order.Contains(p.Name) && (optional == null || !optional.Contains(p.Name))) diffs.Add(where + ": recorded member '" + p.Name + "' not in HS2's list");
        }

        /// <summary>The order of a recorded object's members (for reflection dumps: HS2Next writes a subset in that order).</summary>
        public static string[] OrderOf(JObject rec) => rec == null ? new string[0] : rec.Properties().Select(p => p.Name).ToArray();

        /// <summary>
        /// Transforms of the reference install that plugins add and HS2Next does not build (BetterPenetration: cm_J_dan*,
        /// cm_J_vdan*, k_f_dan*; ClothColliders: HS2_Colliders_*; CompareTest lists the same as "not found"): when absent
        /// they are listed and left out of the path coverage.
        /// </summary>
        public static readonly string[] Hs2OnlyPrefixes = { "cm_J_dan", "cm_J_vdan", "k_f_dan", "HS2_Colliders_" };

        /// <summary>A '/'-path with the legacy hair names normalised (c_J_hairFCa_01 → c_J_hair_FCa_01: HS2 writes the new form).</summary>
        public static string NormPath(string p) => p == null ? null : string.Join("/", p.Split('/').Select(NormHair));

        static string NormHair(string s) =>
            s != null && s.Length > 9 && s.StartsWith("c_J_hair", StringComparison.Ordinal) && (s[8] == 'F' || s[8] == 'B') ? "c_J_hair_" + s.Substring(8) : s;

        /// <summary>bones.dump all vs a recorded dump: top level, the entries, and the path sets (≥ 99 % of HS2's body and head paths).</summary>
        public static void ShapeDump(List<string> diffs, List<string> info, JObject rec, JObject next)
        {
            ShapeObject(diffs, "bones.dump", DumpOrder, rec, next, new[] { "headRootParent" });
            var rb = rec?["bones"] as JArray;
            var nb = next?["bones"] as JArray;
            if (rb == null || nb == null || nb.Count == 0) { diffs.Add("bones.dump.bones: missing"); return; }
            ShapeObject(diffs, "bones.dump.bones[0]", BoneOrder, (JObject)rb[0], (JObject)nb[0]);
            int recBody = rec.Value<int?>("bodyCount") ?? rb.Count;
            var paths = new HashSet<string>(nb.OfType<JObject>().Select(b => NormPath((string)b["path"])), StringComparer.Ordinal);
            int body = 0, heads = 0, bodyFound = 0, headFound = 0;
            var missing = new List<string>();
            var hs2Only = new List<string>();
            for (int i = 0; i < rb.Count; i++)
            {
                string p = (string)rb[i]["path"], name = (string)rb[i]["name"];
                bool found = paths.Contains(NormPath(p));
                if (!found && Hs2OnlyPrefixes.Any(x => name.StartsWith(x, StringComparison.Ordinal))) { hs2Only.Add(name); continue; }
                if (i < recBody) { body++; if (found) bodyFound++; } else { heads++; if (found) headFound++; }
                if (!found) missing.Add((i < recBody ? "body " : "head ") + name);
            }
            double fb = body > 0 ? (double)bodyFound / body : 1, fh = heads > 0 ? (double)headFound / heads : 1;
            info.Add($"bones.dump paths (hair names normalised): body {bodyFound}/{body} ({fb:P2}), head {headFound}/{heads} ({fh:P2}); HS2Next {nb.Count} entries" +
                     (hs2Only.Count > 0 ? "; HS2-only plugin transforms absent, not counted (" + hs2Only.Count + "): " + string.Join(", ", hs2Only) : "") +
                     (missing.Count > 0 ? "; not found (" + missing.Count + "): " + string.Join(", ", missing) : ""));
            if (fb < 0.99) diffs.Add($"bones.dump: only {fb:P2} of HS2's body paths found");
            if (fh < 0.99) diffs.Add($"bones.dump: only {fh:P2} of HS2's head paths found");
        }

        /// <summary>face.dump (reflect true) vs a recorded face.dump: top level, renderers, materials, expression, the look subsets.</summary>
        public static void ShapeFace(List<string> diffs, JObject rec, JObject next)
        {
            ShapeObject(diffs, "face.dump", FaceOrder, rec, next, new[] { "fbs" });
            var rr = rec?["renderers"] as JArray;
            var nr = next?["renderers"] as JArray;
            if (rr != null && rr.Count > 0 && nr != null && nr.Count > 0)
            {
                ShapeObject(diffs, "face.dump.renderers[0]", RendererOrder, (JObject)rr[0], (JObject)nr[0]);
                var rm = rr.OfType<JObject>().SelectMany(x => (x["materials"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>()).FirstOrDefault();
                var nm = nr.OfType<JObject>().SelectMany(x => (x["materials"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>()).FirstOrDefault();
                if (rm != null && nm != null) ShapeObject(diffs, "face.dump.renderers[].materials[]", MaterialOrder, null, nm);
                var names = new HashSet<string>(nr.OfType<JObject>().Select(x => (string)x["name"]), StringComparer.Ordinal);
                foreach (var x in rr.OfType<JObject>())
                    if (!names.Contains((string)x["name"]) && ((x["blendShapes"] as JArray)?.Count ?? 0) > 0) diffs.Add("face.dump: HS2 renderer " + (string)x["name"] + " (with blend shapes) missing");
            }
            else diffs.Add("face.dump.renderers: empty");
            ShapeObject(diffs, "face.dump.expression", ExpressionOrder, rec?["expression"] as JObject, next?["expression"] as JObject);
            var re = rec?["eyeLook"] as JObject;
            var ne = next?["eyeLook"] as JObject;
            if (re != null)
            {
                ShapeObject(diffs, "face.dump.eyeLook", OrderOf(re), re, ne, subset: true);
                var rs = re["eyeLookScript"] as JObject;
                ShapeObject(diffs, "face.dump.eyeLook.eyeLookScript", OrderOf(rs), rs, ne?["eyeLookScript"] as JObject, subset: true);
                var ro = (rs?["eyeObjs"] as JArray)?.FirstOrDefault() as JObject;
                var no = (ne?["eyeLookScript"]?["eyeObjs"] as JArray)?.FirstOrDefault() as JObject;
                if (ro != null) ShapeObject(diffs, "face.dump.eyeLook.eyeLookScript.eyeObjs[0]", OrderOf(ro), ro, no, subset: true);
            }
            var rn = rec?["neckLook"] as JObject;
            var nn = next?["neckLook"] as JObject;
            if (rn != null)
            {
                ShapeObject(diffs, "face.dump.neckLook", OrderOf(rn), rn, nn, subset: true);
                var rs = rn["neckLookScript"] as JObject;
                ShapeObject(diffs, "face.dump.neckLook.neckLookScript", OrderOf(rs), rs, nn?["neckLookScript"] as JObject, subset: true);
                var ro = (rs?["aBones"] as JArray)?.FirstOrDefault() as JObject;
                var no = (nn?["neckLookScript"]?["aBones"] as JArray)?.FirstOrDefault() as JObject;
                if (ro != null) ShapeObject(diffs, "face.dump.neckLook.neckLookScript.aBones[0]", OrderOf(ro), ro, no, subset: true);
            }
        }

        /// <summary>fk.list exact vs a recorded fk.list exact.</summary>
        public static void ShapeFk(List<string> diffs, JObject rec, JObject next)
        {
            ShapeObject(diffs, "fk.list", FkOrder, rec, next);
            var rb = (rec?["bones"] as JArray)?.FirstOrDefault() as JObject;
            var nb = (next?["bones"] as JArray)?.FirstOrDefault() as JObject;
            if (rb == null || nb == null) { diffs.Add("fk.list.bones: empty"); return; }
            ShapeObject(diffs, "fk.list.bones[0]", FkBoneOrder, rb, nb);
        }

        /// <summary>camera.state's camera vs a recorded one.</summary>
        public static void ShapeCamera(List<string> diffs, JObject rec, JObject next)
        {
            ShapeObject(diffs, "camera.state.camera", CameraOrder, rec, next);
            ShapeObject(diffs, "camera.state.camera.studio", StudioCameraOrder, rec?["studio"] as JObject, next?["studio"] as JObject, new[] { "basePosition", "baseRotation" });
        }

        /// <summary>One scene object vs the recorder's subset (anime, worldPos, pos, rot, scale).</summary>
        public static void ShapeSceneObject(List<string> diffs, JObject recState, JObject next)
        {
            ShapeObject(diffs, "scene.objects[]", DescribeOrder, null, next);
            foreach (var m in new[] { "worldPos", "pos", "rot", "scale" })
                if (recState?[m] != null && !SameKind(recState[m], next?[m])) diffs.Add("scene.objects[]." + m + ": kind " + Kind(recState[m]) + " vs " + Kind(next?[m]));
            ShapeObject(diffs, "scene.objects[].anime", AnimeOrder, recState?["anime"] as JObject, next?["anime"] as JObject);
        }
    }

    // ================================================================ helpers

    static bool W2Has(Ctx c, string cmd) => c.App?.BridgeServices?.Commands?.Find(cmd) != null;

    static string W2AddedKey(JObject r) => (string)r?["added"]?[0]?["key"];

    /// <summary>Loads the female target (relative path, sex female) and returns its node id (FAIL and null when it does not build).</summary>
    static string W2LoadFemale(Ctx c, string what = "chara.load BR-Chan")
    {
        var r = c.Ok(new { cmd = "chara.load", path = c.FemaleRel, sex = "female" }, what);
        var a = r["added"]?[0] as JObject;
        if (a == null || (string)a["state"] != "Built") { c.Check(false, what + ": built", Ctx.Short(c.LastText)); return null; }
        return (string)a["key"];
    }

    static CharaRealization W2Real(Ctx c, string id) => c.App.Runtime.FindChara(id);

    static double W2D(JToken t) => t == null || t.Type == JTokenType.Null ? double.NaN : t.Value<double>();

    static float W2Angle(Quaternion a, Quaternion b) => Quaternion.Angle(a, b);

    static Quaternion W2Q(JToken lq) => new Quaternion((float)W2D(lq[0]), (float)W2D(lq[1]), (float)W2D(lq[2]), (float)W2D(lq[3]));

    static JObject W2Bone(JObject dump, string name) => (dump?["bones"] as JArray)?.OfType<JObject>().FirstOrDefault(b => (string)b["name"] == name);

    /// <summary>Path → transform of a bones.dump walk (body from cf_J_Root without the head bone root, head from p_cf_head_bone; holders skipped).</summary>
    static Dictionary<string, Transform> W2DumpPaths(CharaRealization r)
    {
        var d = new Dictionary<string, Transform>(StringComparer.Ordinal);
        var body = HS2Next.Chara.CharaAssembler.Find(r.Context.Anim.transform, "cf_J_Root");
        var head = r.Context.HeadBone ? r.Context.HeadBone.transform : null;
        void Walk(Transform t, string path, Transform skip)
        {
            if (t == skip) return;
            if (!d.ContainsKey(path)) d[path] = t;
            for (int i = 0; i < t.childCount; i++)
            {
                var ch = t.GetChild(i);
                if (SceneNodeHolder.IsHolder(ch)) continue;
                Walk(ch, path.Length == 0 ? ch.name : path + "/" + ch.name, skip);
            }
        }
        if (body) Walk(body, "", head);
        if (head) Walk(head, head.name, null);
        return d;
    }

    static JObject W2ReadJson(string file)
    {
        using (var r = new JsonTextReader(new StringReader(File.ReadAllText(file))) { DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Double })
            return JObject.Load(r);
    }

    /// <summary>The handler time of a request: wall time of Call minus one frame of the same scene (median of <paramref name="n"/>).</summary>
    static double W2TimeCall(Ctx c, object request, int n, out JObject last)
    {
        var frames = new List<double>();
        var calls = new List<double>();
        last = null;
        for (int i = 0; i < n; i++)
        {
            var sw = Stopwatch.StartNew();
            c.App.Frame(Dt);
            frames.Add(sw.Elapsed.TotalMilliseconds);
            sw.Restart();
            last = c.Call(request);
            calls.Add(sw.Elapsed.TotalMilliseconds);
        }
        frames.Sort(); calls.Sort();
        double f = frames[frames.Count / 2], k = calls[calls.Count / 2];
        c.Info($"{JObject.FromObject(request).Value<string>("cmd")}: call {k:F2} ms, frame {f:F2} ms (median of {n})");
        return Math.Max(0, k - f);
    }

    static void W2Timing(Ctx c, string what, double ms, double target)
    {
        bool within = ms <= target;
        c.Check(ms <= 2 * target, $"{what}: {ms:F2} ms (target {target} ms; FAIL above {2 * target} ms)");
        if (!within) c.Warn($"{what}: {ms:F2} ms is above the target {target} ms");
    }

    static void W2ReportShape(Ctx c, string what, List<string> diffs)
    {
        c.Check(diffs.Count == 0, what, diffs.Count == 0 ? "" : diffs.Count + " differences: " + string.Join("; ", diffs.Take(12)));
    }

    // ================================================================ C10 characters

    /// <summary>C10: chara.load of both targets, a missing card, chara.save → .hs2nchara that chara.load adds again (documents equal).</summary>
    static void C10(Ctx c)
    {
        c.Host();
        var f = c.Ok(new { cmd = "chara.load", path = c.FemaleRel, sex = "female" }, "chara.load BR-Chan (relative, sex \"female\")");
        var fa = f["added"]?[0] as JObject;
        c.Check(fa != null && (string)fa["kind"] == "chara" && (int?)fa["sex"] == 1 && (bool?)f["imported"] == true && (string)fa["state"] == "Built",
            "added[0]: kind chara, sex 1, imported, state Built", Ctx.Short(fa?.ToString(Formatting.None)));
        c.Check(f["buildMs"] != null && f["findings"] is JArray, "reply has buildMs and findings", Ctx.Short(c.LastText));
        var m = c.Ok(new { cmd = "chara.load", path = c.MaleRel, sex = "male" }, "chara.load HERO107 (relative, sex \"male\")");
        var ma = m["added"]?[0] as JObject;
        c.Check(ma != null && (string)ma["kind"] == "chara" && (int?)ma["sex"] == 0 && (bool?)m["imported"] == true && (string)ma["state"] == "Built",
            "added[0]: kind chara, sex 0, imported, state Built", Ctx.Short(ma?.ToString(Formatting.None)));
        int before = c.App.Session.Model.Count;
        c.Fails(new { cmd = "chara.load", path = "w2_no_such_card.png", sex = "female" }, "not_found", "a missing card → not_found");
        c.Fails(new { cmd = "chara.load", path = c.FemaleRel, sex = "both" }, "bad_args", "sex \"both\" → bad_args");
        c.Fails(new { cmd = "chara.load", path = "../female/x.png", sex = "female" }, "io_denied", "a relative path with '..' → io_denied");
        c.Check(c.App.Session.Model.Count == before, "failed loads add nothing", "" + c.App.Session.Model.Count);
        if (fa == null) return;
        string fid = (string)fa["key"];

        var s = c.Ok(new { cmd = "chara.save", key = fid, name = "w2_c10_female", overwrite = true }, "chara.save {name} → .hs2nchara");
        string path = (string)s["path"];
        string charaDir = Path.Combine(c.UserDataDir, "chara");
        c.Check(path != null && File.Exists(path) && path.EndsWith(".hs2nchara", StringComparison.Ordinal) &&
                Path.GetFullPath(path).StartsWith(Path.GetFullPath(charaDir), StringComparison.OrdinalIgnoreCase) && (long?)s["bytes"] > 0,
            "the file is in userdata/chara", path + " " + (long?)s["bytes"] + " bytes, thumbnail " + (string)s["thumbnail"]);
        c.Fails(new { cmd = "chara.save", key = fid, name = "w2_c10_female" }, "io_denied", "chara.save again without overwrite → io_denied");
        c.Fails(new { cmd = "chara.save", key = fid, path = "../w2_c10_x" }, "io_denied", "chara.save path \"../x\" → io_denied");
        c.Fails(new { cmd = "chara.save", key = fid, path = "C:/w2_c10_x" }, "io_denied", "chara.save path \"C:/x\" → io_denied");
        c.Fails(new { cmd = "chara.save", key = fid }, "bad_args", "chara.save without name / path → bad_args");
        var sub = c.Ok(new { cmd = "chara.save", key = fid, path = "w2/c10_sub", overwrite = true }, "chara.save {path} with a subfolder");
        c.Check(File.Exists((string)sub["path"]), "the subfolder file exists", (string)sub["path"]);

        var l = c.Ok(new { cmd = "chara.load", path = "w2_c10_female.hs2nchara" }, "chara.load of the saved .hs2nchara (relative, userdata/chara)");
        string lid = W2AddedKey(l);
        c.Check(lid != null && (string)l["added"][0]["state"] == "Built" && (bool?)l["imported"] == true && (int?)l["added"][0]["sex"] == 1,
            "the reloaded character is built, imported, sex 1", Ctx.Short(c.LastText));
        if (lid == null) return;
        string fc, fo, lc, lo;
        if (W2Has(c, "scene.doc"))
        {
            var d1 = c.Ok(new { cmd = "scene.doc", key = fid }, "scene.doc {key} of the original");
            var d2 = c.Ok(new { cmd = "scene.doc", key = lid }, "scene.doc {key} of the reloaded character");
            fc = (string)d1["chara"]; fo = (string)d1["coord"]; lc = (string)d2["chara"]; lo = (string)d2["coord"];
        }
        else
        {
            c.Warn("scene.doc is not registered yet (package W1): the documents are compared through the session");
            string Doc(string id, bool chara)
            {
                var t = c.App.Session.Model.Find(id)?.Appearance?.Stored;
                if (t == null) return null;
                var b = chara ? CanonicalJsonWriter.ToBytes(CharaDocument.EntryName, t.Chara.Write) : CanonicalJsonWriter.ToBytes(CoordDocument.EntryName, t.Coord.Write);
                return Encoding.UTF8.GetString(b);
            }
            fc = Doc(fid, true); fo = Doc(fid, false); lc = Doc(lid, true); lo = Doc(lid, false);
        }
        c.Check(fc != null && fc == lc, "chara.json of the reloaded character = the original's", fc == null ? "no document" : fc.Length + " / " + (lc?.Length ?? -1) + " chars");
        c.Check(fo != null && fo == lo, "coord.json of the reloaded character = the original's", fo == null ? "no document" : fo.Length + " / " + (lo?.Length ?? -1) + " chars");
        c.Fails(new { cmd = "chara.save", key = fid, name = "w2_c10_female", overwrite = true, thumb = "keep" }, "bad_args", "chara.save thumb \"keep\" → bad_args");
    }

    // ================================================================ C11 animation and face

    /// <summary>C11: anime.groups / anime.search, chara.anime at a time, an H row played (numbers only), chara.face and face.dump.</summary>
    static void C11(Ctx c)
    {
        c.Host();
        string id = W2LoadFemale(c);
        if (id == null) return;
        var g = c.Ok(new { cmd = "anime.groups" }, "anime.groups");
        var g19 = (g["groups"] as JArray)?.OfType<JObject>().FirstOrDefault(x => (int?)x["group"] == 19);
        c.Check(g19 != null && (g19["categories"] as JArray)?.OfType<JObject>().Any(x => (int?)x["category"] == 0) == true, "anime.groups contains 19 / 0",
            g19 == null ? "no group 19" : g19.ToString(Formatting.None).Substring(0, Math.Min(200, g19.ToString(Formatting.None).Length)));
        // the lists name the rows in Japanese (19/0/0 is "立ち1"; "Idle_00" is its clip): filter by group and category, then
        // search the row's own name
        var s = c.Ok(new { cmd = "anime.search", group = 19, category = 0 }, "anime.search {group 19, category 0}");
        var idle = (s["anime"] as JArray)?.OfType<JObject>().FirstOrDefault(x => (int?)x["group"] == 19 && (int?)x["category"] == 0 && (int?)x["no"] == 0);
        c.Check(idle != null && (string)idle["guid"] == ContentIds.Vanilla && (string)idle["id"] == ContentIds.Vanilla + "#0" && (bool?)idle["isH"] == false &&
                (s["anime"] as JArray).OfType<JObject>().All(x => (int)x["group"] == 19 && (int)x["category"] == 0),
            "anime.search lists 19/0/0 with guid illusion.hs2 (only 19/0 rows)", Ctx.Short(idle?.ToString(Formatting.None) ?? c.LastText));
        if (idle != null)
        {
            string nm = (string)idle["name"];
            var q = c.Ok(new { cmd = "anime.search", q = nm, limit = 10000 }, "anime.search {q: \"" + nm + "\"}");
            c.Check((q["anime"] as JArray)?.OfType<JObject>().Any(x => (int)x["group"] == 19 && (int)x["category"] == 0 && (int)x["no"] == 0) == true &&
                    (q["anime"] as JArray).OfType<JObject>().All(x => ((string)x["groupName"] + " / " + (string)x["categoryName"] + " / " + (string)x["name"]).IndexOf(nm, StringComparison.OrdinalIgnoreCase) >= 0),
                "the text search finds 19/0/0 by its name, and only rows whose \"group / category / name\" contains it");
            var lim = c.Ok(new { cmd = "anime.search", limit = 3 }, "anime.search {limit 3}");
            c.Check((lim["anime"] as JArray)?.Count == 3 && (bool?)lim["truncated"] == true, "limit 3: 3 rows and truncated");
        }
        var all = c.Ok(new { cmd = "anime.search", limit = 10000 }, "anime.search (all listed rows)");
        c.Check((all["anime"] as JArray)?.OfType<JObject>().All(x => (bool?)x["isH"] == false) == true, "without includeH no H row is listed");
        var a = c.Ok(new { cmd = "chara.anime", key = id, group = 19, category = 0, no = 0, time = 0.5 }, "chara.anime 19/0/0 time 0.5");
        double nt = W2D(a["object"]?["anime"]?["normalizedTime"]);
        c.Check(Math.Abs(nt - 0.5) <= 1e-4, "object.anime.normalizedTime = 0.5 ± 1e-4", nt.ToString("R"));
        c.Check((int?)a["object"]?["anime"]?["group"] == 19 && (string)a["object"]?["anime"]?["guid"] == ContentIds.Vanilla, "object.anime is 19/0/0 of illusion.hs2", Ctx.Short(a["object"]?["anime"]?.ToString(Formatting.None)));
        // the clip starts at `time` like HS2's LoadAnime, also when the row and the time equal the model's (package F; W3's rest case)
        c.Frames(30);
        var a2 = c.Ok(new { cmd = "chara.anime", key = id, group = 19, category = 0, no = 0, time = 0.5 }, "chara.anime 19/0/0 time 0.5 again after 30 frames (row and time unchanged in the model)");
        double nt2 = W2D(a2["object"]?["anime"]?["normalizedTime"]);
        c.Check(Math.Abs(nt2 - 0.5) <= 1e-4, "the clip restarted at normalizedTime 0.5 ± 1e-4", nt2.ToString("R"));
        c.Ok(new { cmd = "chara.anime", key = id, group = 0, category = 0, no = 0, time = 0, speed = 1 }, "chara.anime 0/0/0 (T-pose) time 0 speed 1");
        c.Frames(30);
        int undoTp = c.App.Session.Commands.UndoList.Count;
        var tp = c.Ok(new { cmd = "chara.anime", key = id, group = 0, category = 0, no = 0, time = 0, speed = 0 }, "chara.anime 0/0/0 time 0 speed 0 after 30 frames (only the speed changes in the model)");
        double ntp = W2D(tp["object"]?["anime"]?["normalizedTime"]);
        c.Check(Math.Abs(ntp) <= 1e-4 && W2D(tp["object"]?["anime"]?["speed"]) == 0, "the T-pose restarted at normalizedTime 0 ± 1e-4, speed 0", Ctx.Short(tp["object"]?["anime"]?.ToString(Formatting.None)));
        c.Check(c.App.Session.Commands.UndoList.Count == undoTp + 1, "one history entry (the speed)", (c.App.Session.Commands.UndoList.Count - undoTp) + " entries");
        c.Frames(10);
        var tp2 = c.Ok(new { cmd = "scene" }, "scene after 10 frames at speed 0");
        var tpo = (tp2["objects"] as JArray)?.OfType<JObject>().FirstOrDefault(x => (string)x["key"] == id);
        c.Check(tpo != null && Math.Abs(W2D(tpo["anime"]?["normalizedTime"])) <= 1e-4, "speed 0 holds the restarted time", Ctx.Short(tpo?["anime"]?.ToString(Formatting.None)));
        // H rows play on the clothed target (judged by numbers only: the realized state, no capture)
        var hs = c.Ok(new { cmd = "anime.search", includeH = true, limit = 10000 }, "anime.search {includeH: true}");
        var h = (hs["anime"] as JArray)?.OfType<JObject>().FirstOrDefault(x => (bool?)x["isH"] == true);
        c.Check(h != null, "anime.search {includeH: true} lists H rows");
        if (h != null)
        {
            int undoH = c.App.Session.Commands.UndoList.Count;
            var ha = c.Ok(new { cmd = "chara.anime", key = id, guid = (string)h["guid"], group = (int)h["group"], category = (int)h["category"], no = (int)h["no"] },
                "chara.anime of an H row (" + (int)h["group"] + "/" + (int)h["category"] + "/" + (int)h["no"] + ") → ok");
            c.Check((int?)ha["object"]?["anime"]?["group"] == (int)h["group"] && (int?)ha["object"]?["anime"]?["no"] == (int)h["no"] &&
                    c.App.Session.Commands.UndoList.Count == undoH + 1, "object.anime is the H row; one history entry", Ctx.Short(ha["object"]?["anime"]?.ToString(Formatting.None)));
            var hst = c.Ok(new { cmd = "chara.animeState", key = id }, "chara.animeState of the H row");
            var hl = hst["animator"]?["layers"] as JArray;
            c.Check((bool?)hst["built"] == true && (bool?)hst["isHAnime"] == true && (int?)hst["row"]?["no"] == (int)h["no"] && (hst["pv"] as JArray)?.Count == 8 &&
                    hl != null && hl.Count >= 1 && (bool?)hl[0]["isRowClip"] == true && (hst["animator"]?["parameters"] as JArray) != null,
                "chara.animeState: built, isHAnime, the row, 8 PV flags, layer 0 plays the row's clip, parameters",
                Ctx.Short(hst.ToString(Formatting.None)));
            var hy = c.Ok(new { cmd = "chara.yure", key = id }, "chara.yure of the H row");
            c.Check((hy["active"] as JArray)?.Count == 4 && (hy["shapeL"]?["breast"] as JArray)?.Count == 7 && hy["shapeR"]?["nip"] != null &&
                    (hy["disableBustShapeMask"] as JArray)?.Count == 2 && hy["matchedLine"] != null,
                "chara.yure: the four enables, 7 + 1 shape flags per side, the masks of both sides, the matched line", Ctx.Short(hy.ToString(Formatting.None)));
            c.Ok(new { cmd = "chara.anime", key = id, group = 19, category = 0, no = 0, time = 0 }, "chara.anime 19/0/0 after the H row");
            var pst = c.Ok(new { cmd = "chara.animeState", key = id }, "chara.animeState after the plain row");
            var py = c.Ok(new { cmd = "chara.yure", key = id }, "chara.yure after the plain row");
            c.Check((bool?)pst["isHAnime"] == false && (bool?)py["isInit"] == (bool?)hy["isInit"] && (int?)py["lineCount"] == (int?)hy["lineCount"] && (int?)py["matchedLine"] == -1 &&
                    (py["active"] as JArray)?.All(x => (bool)x) == true && (py["disableBustShapeMask"] as JArray)?.All(sd => sd.All(x => !(bool)x)) == true,
                "a plain row keeps the sway table (as HS2) and matches no line: everything enabled, no mask", Ctx.Short(py.ToString(Formatting.None)));
        }
        c.Fails(new { cmd = "chara.anime", key = id, group = 19, category = 0, no = 99999 }, "not_found", "chara.anime of a missing row → not_found");

        var r = W2Real(c, id);
        var lists = r?.Expression?.Lists;
        var fr = c.Ok(new { cmd = "chara.face", key = id, eyebrow = 1, eyes = 1, mouth = 3, mouthOpen = 0.5, blink = false, tears = 0.5 }, "chara.face {eyebrow 1, eyes 1, mouth 3, mouthOpen 0.5, blink false, tears 0.5}");
        var face = fr["face"] as JObject;
        bool ids = lists != null && face != null && (int?)face["eyebrowId"] == lists.Eyebrow[1] && (int?)face["eyesId"] == lists.Eyes[1] &&
                   (int?)face["eyebrow"] == 1 && (int?)face["eyes"] == 1 && (int?)face["mouth"] == 3;
        c.Check(ids, "face ids = the ids at list positions 1 (eyebrow " + (lists != null ? lists.Eyebrow[1].ToString() : "?") + ", eyes " + (lists != null ? lists.Eyes[1].ToString() : "?") + "), mouth 3",
            Ctx.Short(face?.ToString(Formatting.None)));
        c.Check(face != null && W2D(face["mouthOpen"]) == 0.5 && (bool?)face["blink"] == false && W2D(face["tears"]) == 0.5, "mouthOpen 0.5, blink false, tears 0.5", Ctx.Short(face?.ToString(Formatting.None)));
        c.Check(face != null && face.Properties().Select(p => p.Name).SequenceEqual(ExpressionOrder.Take(12)), "face members in HS2's order", string.Join(",", face?.Properties().Select(p => p.Name) ?? new string[0]));
        c.Fails(new { cmd = "chara.face", key = id, eyesLook = 7 }, "bad_args", "eyesLook 7 → bad_args");
        var kw = W2HeadKeywords(r);
        c.Frames(20);
        var fd = c.Ok(new { cmd = "face.dump", key = id }, "face.dump after 20 frames");
        var x2 = fd["expression"] as JObject;
        var diff = new List<string>();
        foreach (var k in ExpressionOrder.Take(12))
            if (x2 == null || face == null || !JToken.DeepEquals(W2Num(x2[k]), W2Num(face[k]))) diff.Add(k + " " + x2?[k] + " vs model " + face?[k]);
        c.Check(diff.Count == 0, "face.dump expression = the model's face", string.Join("; ", diff));
        c.Check(x2 != null && (int?)x2["mouthPtn"] == 3 && W2D(x2["studioMouthOpen"]) == 0.5 && (bool?)x2["blinkFlag"] == false && W2D(x2["tearsRate"]) == 0.5,
            "face.dump getters: mouthPtn 3, studioMouthOpen 0.5, blinkFlag false, tearsRate 0.5", Ctx.Short(x2?.ToString(Formatting.None)));
        int shapes = (fd["renderers"] as JArray)?.OfType<JObject>().Sum(x => (x["blendShapes"] as JArray)?.Count ?? 0) ?? 0;
        c.Check(shapes > 0 && fd["eyeLook"] is JObject && fd["neckLook"] is JObject && fd["stages"] is JObject, "face.dump: renderers with blend shapes, eyeLook, neckLook, stages", shapes + " shapes");
        var kw2 = W2HeadKeywords(r);
        var changed = kw.Keys.Where(m => !kw2.TryGetValue(m, out var v) || v != kw[m]).ToList();
        c.Check(changed.Count == 0, "face.dump leaves the head materials' keywords unchanged (V5)", kw.Count + " materials; changed: " + string.Join(", ", changed.Take(5)));
        // the model's mouth open reaches the face (the Studio mouth open decides FixedRate last)
        FMouthOpenShown(c, id, 0.5f, 3, "after chara.face");

        // chara.speed, chara.hands, look.target, chara.animeOption: replies and one entry each
        var bus = c.App.Session.Commands;
        var sp = c.Ok(new { cmd = "chara.speed", key = id, speed = 0.5, loop = true }, "chara.speed {speed 0.5, loop true}");
        c.Check(W2D(sp["speed"]) == 0.5 && (bool?)sp["loop"] == true && bus.UndoList[0].Label == "Animation speed", "reply {speed 0.5, loop true}, one entry \"Animation speed\"", Ctx.Short(c.LastText));
        var hd = c.Ok(new { cmd = "chara.hands", key = id, left = 2 }, "chara.hands {left 2}");
        c.Check((hd["hands"] as JArray)?.Select(x => (int)x).SequenceEqual(new[] { 2, 0 }) == true && bus.UndoList[0].Label == "Hands", "reply hands [2, 0], one entry \"Hands\"", Ctx.Short(c.LastText));
        var lt0 = c.Ok(new { cmd = "look.target", key = id }, "look.target (read)");
        c.Check(lt0["position"] is JArray && lt0["local"] is JArray && (string)lt0["parent"] != null && lt0["eyesLookPtn"] != null, "reply {position, local, parent, active, eyesLookPtn}", Ctx.Short(c.LastText));
        var lt = c.Ok(new { cmd = "look.target", key = id, pos = new[] { 0f, 15f, 5f } }, "look.target {pos [0, 15, 5]}");
        var lp = lt["position"];
        c.Check(lp != null && Math.Abs(W2D(lp[0])) <= 1e-4 && Math.Abs(W2D(lp[1]) - 15) <= 1e-4 && Math.Abs(W2D(lp[2]) - 5) <= 1e-4 && bus.UndoList[0].Label == "Look target",
            "the target is at the requested world position (± 1e-4), one entry \"Look target\"", Ctx.Short(c.LastText));
        var rifle = c.Targets.animation?.FirstOrDefault(x => x.name == "attack_01");
        if (rifle != null)
        {
            c.Ok(new { cmd = "chara.anime", key = id, group = rifle.group, category = rifle.category, no = rifle.no }, $"chara.anime {rifle.group}/{rifle.category}/{rifle.no} ({rifle.name}, an option item)");
            var op = c.Ok(new { cmd = "chara.animeOption", key = id, visible = false }, "chara.animeOption {visible false}");
            var items = op["items"] as JArray;
            c.Check((bool?)op["visible"] == false && items != null && items.Count > 0 && items.OfType<JObject>().All(x => (string)x["bone"] != null && x["pos"] is JArray && x["lossyScale"] is JArray) &&
                    bus.UndoList[0].Label == "Animation options", "reply {visible false, items with bone and transforms}, one entry \"Animation options\"", Ctx.Short(c.LastText));
        }
        else c.Warn("no attack_01 row in Tools/targets.json: chara.animeOption items not checked");

        // chara.mosaic (5.M package R): the read-only mosaic record; app.options render.mosaicWide
        var mo = c.Ok(new { cmd = "chara.mosaic", key = id }, "chara.mosaic");
        var mm = (mo["members"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
        c.Check(mm.Any(x => ((string)x["path"])?.EndsWith("o_mnpb") == true && (string)x["mode"] == "texture" && (string)x["sourceKind"] == "body" && (bool?)x["bodySurface"] == true) &&
                mm.Any(x => ((string)x["path"])?.EndsWith("cm_o_dan00") == true && (string)x["mode"] == "procedural") &&
                mm.All(x => (x["materials"] as JArray)?.OfType<JObject>().All(z => (string)z["shader"] == "HS2Next/Mosaic" && (int?)z["queue"] == 2500) == true) &&
                mo["protected"] is JArray && mo["findings"] is JArray && mo["wide"]?.Type == JTokenType.Boolean,
            "chara.mosaic: o_mnpb texture mode from the body, cm_o_dan00 procedural, every slot HS2Next/Mosaic at queue 2500; protected, findings, wide", Ctx.Short(c.LastText));
        bool wide0 = HS2Next.MosaicWide.Enabled;
        var aw = c.Ok(new { cmd = "app.options", render = new { mosaicWide = false } }, "app.options {render: {mosaicWide: false}}");
        c.Check((bool?)aw["render"]?["mosaicWide"] == false && !HS2Next.MosaicWide.Enabled, "app.options render.mosaicWide false reaches MosaicWide and the reply", Ctx.Short(c.LastText));
        c.Fails(new { cmd = "app.options", render = new { mosaicWide = 1 } }, "bad_args", "app.options render.mosaicWide 1 → bad_args");
        c.Ok(new { cmd = "app.options", render = new { mosaicWide = wide0 } }, "app.options {render: {mosaicWide}} back");

        // the mouth open of a document build (scene load) reaches the face as well
        if (W2Has(c, "scene.save") && W2Has(c, "scene.load"))
        {
            c.Ok(new { cmd = "scene.save", name = "f_c11_face", overwrite = true, thumb = "placeholder" }, "scene.save (mouth open 0.5)");
            c.Ok(new { cmd = "scene.new" }, "scene.new");
            c.Ok(new { cmd = "scene.load", name = "f_c11_face" }, "scene.load");
            c.Frames(20);
            FMouthOpenShown(c, id, 0.5f, 3, "after scene.save → scene.load");
        }
        else c.Warn("scene.save / scene.load are not registered: the mouth open after a scene load is not checked");
    }

    /// <summary>
    /// The face of a built character shows the Studio mouth open <paramref name="open"/> on mouth pattern <paramref name="ptn"/>:
    /// the mouth control's FixedRate is the value, and every mouth target with a close and an open shape for the pattern holds
    /// them at 100 − n and n (n = ⌊open × 100⌋). Before package F's fix FixedRate was −1 (closed).
    /// </summary>
    static void FMouthOpenShown(Ctx c, string id, float open, int ptn, string what)
    {
        var m = W2Real(c, id)?.Expression?.Stage?.Mouth;
        if (m == null) { c.Check(false, what + ": the character has a mouth control"); return; }
        c.Check(m.FixedRate == open, what + ": mouth FixedRate = the Studio mouth open " + open, "FixedRate " + m.FixedRate.ToString("R"));
        int n = (int)(open * 100f), targets = 0;
        var bad = new List<string>();
        for (int i = 0; i < m.TargetCount; i++)
        {
            var t = m.TargetData(i);
            if (t == null || !t.Renderer || ptn >= t.PatternCount) continue;
            int cl = t.Close[ptn], op = t.Open[ptn];
            if (cl < 0 || op < 0 || cl == op || cl >= t.BlendShapeCount || op >= t.BlendShapeCount) continue;
            targets++;
            float wc = t.Renderer.GetBlendShapeWeight(cl), wo = t.Renderer.GetBlendShapeWeight(op);
            if (wc != 100 - n || wo != n) bad.Add($"{t.Name} close {wc} open {wo}");
        }
        c.Check(targets > 0 && bad.Count == 0, $"{what}: the mouth targets show pattern {ptn} open {open} (close {100 - n}, open {n})",
            targets + " targets" + (bad.Count > 0 ? "; " + string.Join(", ", bad) : ""));
    }

    static JToken W2Num(JToken t) => t != null && (t.Type == JTokenType.Integer || t.Type == JTokenType.Float) ? new JValue(t.Value<double>()) : t;

    static Dictionary<string, string> W2HeadKeywords(CharaRealization r)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        var head = r?.Context?.Head;
        if (!head) return d;
        int i = 0;
        foreach (var s in head.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            foreach (var m in s.sharedMaterials)
                if (m) d[(i++) + ":" + m.name] = string.Join(" ", m.shaderKeywords);
        return d;
    }

    // ================================================================ C12 motion

    static JObject W2MotionCall(Ctx c, object req, string what) => c.Ok(req, what);

    /// <summary>C12: paused, time scale, autoStep, step, FK-owned stages, overrides across a rebuild and scene.new.</summary>
    static void C12(Ctx c)
    {
        c.Host();
        string id = W2LoadFemale(c);
        if (id == null) return;
        var p0 = W2MotionCall(c, new { cmd = "chara.motion", key = id, paused = true, reset = true }, "chara.motion {paused, reset}");
        c.Frames(10);
        var p1 = W2MotionCall(c, new { cmd = "chara.motion", key = id }, "chara.motion (read)");
        c.Check(W2D(p0["time"]) == W2D(p1["time"]) && (int)p1["stepCount"] - (int)p0["stepCount"] >= 10, "paused: time unchanged over 10 frames, steps run",
            $"time {p0["time"]} → {p1["time"]}, steps {p0["stepCount"]} → {p1["stepCount"]}");
        var h0 = W2MotionCall(c, new { cmd = "chara.motion", key = id, paused = false, timeScale = 0.5 }, "chara.motion {paused false, timeScale 0.5}");
        c.Frames(10);
        var h1 = W2MotionCall(c, new { cmd = "chara.motion", key = id }, "chara.motion (read)");
        int ds = (int)h1["stepCount"] - (int)h0["stepCount"];
        double dt = W2D(h1["time"]) - W2D(h0["time"]);
        c.Check(ds >= 10 && Math.Abs(dt - 0.5 * ds / 60.0) <= 1e-6, "timeScale 0.5: Δtime = 0.5 × Δsteps / 60 ± 1e-6", $"Δsteps {ds}, Δtime {dt:R} (expected {0.5 * ds / 60.0:R})");
        c.Check(W2D(h1["timeScale"]) == 0.5 && (bool?)h1["paused"] == false, "the reply shows timeScale 0.5, not paused");
        var a0 = W2MotionCall(c, new { cmd = "chara.motion", key = id, autoStep = false }, "chara.motion {autoStep false}");
        c.Frames(10);
        var a1 = W2MotionCall(c, new { cmd = "chara.motion", key = id }, "chara.motion (read)");
        c.Check((int)a1["stepCount"] == (int)a0["stepCount"] && (bool?)a1["autoStep"] == false, "autoStep false: stepCount unchanged over 10 frames", $"{a0["stepCount"]} → {a1["stepCount"]}");
        var s0 = W2MotionCall(c, new { cmd = "chara.motion", key = id, step = new { dt = Dt, n = 30 } }, "chara.motion {step {dt 1/60, n 30}}");
        c.Check((int)s0["stepCount"] == (int)a1["stepCount"] + 30, "step n 30: stepCount + 30", $"{a1["stepCount"]} → {s0["stepCount"]}");
        c.Check(!(s0["warnings"] is JArray w) || w.Count == 0, "no warning with autoStep false", Ctx.Short(s0["warnings"]?.ToString(Formatting.None)));
        c.Fails(new { cmd = "chara.motion", key = id, stages = new { fk = false } }, "bad_args", "stages {fk} (FK-owned) → bad_args");
        var bad = c.Fails(new { cmd = "chara.motion", key = id, stages = new { nosuchstage = false } }, "bad_args", "an unknown stage → bad_args");
        c.Check((bad["detail"]?["candidates"] as JArray)?.Count > 0, "with candidates", Ctx.Short(c.LastText));
        c.Fails(new { cmd = "chara.motion", key = id, step = new { dt = 0, n = 1 } }, "bad_args", "step dt 0 → bad_args");
        c.Fails(new { cmd = "chara.motion", key = id, timeScale = -1 }, "bad_args", "timeScale −1 → bad_args");
        var ph = W2MotionCall(c, new { cmd = "chara.motion", key = id, physics = false, look = false }, "chara.motion {physics false, look false}");
        var st = (ph["stages"] as JArray)?.OfType<JObject>().ToDictionary(x => (string)x["name"], x => x) ?? new Dictionary<string, JObject>();
        c.Check((bool?)ph["physics"] == false && (bool?)ph["look"] == false && st.TryGetValue("bust", out var b) && (bool?)b["enabled"] == false &&
                st.TryGetValue("dynbone", out var dy) && (bool?)dy["enabled"] == false && st.TryGetValue("bustKinematic", out var bk) && (string)bk["owner"] == "bridge" &&
                st.TryGetValue("eyes", out var ey) && (bool?)ey["enabled"] == false && st.TryGetValue("fk", out var fk) && (string)fk["owner"] == "fk",
            "physics / look off: bust, dynbone, eyes off; bustKinematic (bridge) present; fk owned by fk", Ctx.Short(ph["stages"]?.ToString(Formatting.None)));
        int undo = c.App.Session.Commands.UndoList.Count;
        var cf = W2MotionCall(c, new { cmd = "chara.motion", key = id, correction = new { flags = new[] { true, true, true, true, false, false, false, false } } }, "chara.motion {correction {flags}}");
        c.Check(c.App.Session.Commands.UndoList.Count == undo + 1 && c.App.Session.Commands.UndoList[0].Label == "Joint correction" &&
                (cf["correction"]?["flags"] as JArray)?.Select(x => (bool)x).SequenceEqual(new[] { true, true, true, true, false, false, false, false }) == true,
            "correction.flags: the model written, one entry \"Joint correction\"", Ctx.Short(cf["correction"]?.ToString(Formatting.None)));
        c.Check(c.App.Session.Commands.UndoList.Count == undo + 1, "the switches themselves record nothing");

        // overrides survive a rebuild of the same node id and are cleared by scene.new
        W2MotionCall(c, new { cmd = "chara.motion", key = id, paused = true }, "chara.motion {paused true}");
        var motionBefore = W2Real(c, id) != null ? W2Real(c, id).Motion : null;
        if (W2Has(c, "app.options")) c.Ok(new { cmd = "app.options", rebuild = true }, "app.options {rebuild: true}");
        else
        {
            c.Warn("app.options is not registered yet (package W1): rebuilt through Runtime.RealizeAll");
            c.App.Runtime.RealizeAll();
            c.App.Runtime.ApplyNow();
        }
        var rb = W2MotionCall(c, new { cmd = "chara.motion", key = id }, "chara.motion after the rebuild");
        var motionAfter = W2Real(c, id) != null ? W2Real(c, id).Motion : null;
        c.Check(motionAfter != null && motionAfter != motionBefore, "the character was rebuilt (a new CharaMotion)");
        c.Check((bool?)rb["paused"] == true && (bool?)rb["autoStep"] == false && (bool?)rb["physics"] == false && (bool?)rb["look"] == false,
            "the overrides survive the rebuild (paused, autoStep false, physics and look off)", Ctx.Short(c.LastText));
        var ov = MotionOverrides.For(c.App.Runtime);
        c.Check(ov.Get(id) != null, "the node has an override record", ov.ToString());
        if (W2Has(c, "scene.new")) c.Ok(new { cmd = "scene.new" }, "scene.new");
        else { c.Warn("scene.new is not registered yet (package W1): Session.NewScene"); c.App.Session.NewScene(); }
        c.Check(ov.Count == 0, "scene.new clears the overrides", ov.ToString());

        FChainsWithoutHistory(c);
    }

    /// <summary>
    /// Physics off does not depend on what the chains did before (package F; the A13 finding): character A simulates 10 frames
    /// in the T-pose and then gets Idle_00 (the order of the acceptance's A4–A7), character B gets Idle_00 in the batch that
    /// adds it (no frame in the T-pose); after 30 frames both are paused with physics off and set to the same animation time.
    /// Every dynamic-bone particle bone of A equals B's (≤ 1e-5, ≤ 0.01°). Without the reset before the Animator's bind,
    /// f_studio_34's defaults of cf_J_Legsk_05_00/01 (bound, not animated by Idle_00) were A's simulated T-pose there.
    /// </summary>
    static void FChainsWithoutHistory(Ctx c)
    {
        string ka = W2LoadFemale(c, "A: chara.load (T-pose)");
        if (ka == null) return;
        c.Frames(10);
        c.Ok(new { cmd = "chara.anime", key = ka, group = 19, category = 0, no = 0, speed = 1 }, "A: chara.anime Idle_00 after 10 frames in the T-pose");
        var b = c.Ok(new
        {
            cmd = "batch",
            cmds = new object[]
            {
                new Dictionary<string, object> { ["cmd"] = "chara.load", ["path"] = c.FemaleRel, ["sex"] = "female", ["as"] = "b" },
                new Dictionary<string, object> { ["cmd"] = "chara.anime", ["key"] = "$b.added.0.key", ["group"] = 19, ["category"] = 0, ["no"] = 0, ["speed"] = 1 },
            },
        }, "B: batch [chara.load, chara.anime Idle_00] (no frame in the T-pose)");
        string kb = (string)b["results"]?[0]?["added"]?[0]?["key"];
        if (kb == null) { c.Check(false, "B added", Ctx.Short(c.LastText)); return; }
        c.Frames(30);
        foreach (var k in new[] { ka, kb })
        {
            c.Ok(new { cmd = "chara.motion", key = k, paused = true, physics = false }, "chara.motion {paused, physics:false} " + k.Substring(0, 8));
            c.Ok(new { cmd = "chara.anime", key = k, group = 19, category = 0, no = 0, time = 0.25 }, "chara.anime Idle_00 time 0.25 " + k.Substring(0, 8));
        }
        c.Frames(2);
        var ra = W2Real(c, ka);
        var rb = W2Real(c, kb);
        var pathA = W2DumpPaths(ra).ToDictionary(x => x.Value, x => x.Key);
        var byPathB = W2DumpPaths(rb);
        int n = 0, bad = 0;
        double maxAng = 0, maxPos = 0;
        var worst = new List<string>();
        foreach (var ch in ra.Motion.Get<DynamicBoneStage>().Chains)
            for (int i = 0; i < ch.ParticleCount; i++)
            {
                var t = ch.ParticleTransform(i);
                if (!t || !pathA.TryGetValue(t, out var p)) continue;
                n++;
                if (!byPathB.TryGetValue(p, out var u)) { bad++; worst.Add("missing in B: " + p); continue; }
                double ang = FAngle(t.localRotation, u.localRotation), dp = (t.localPosition - u.localPosition).magnitude;
                maxAng = Math.Max(maxAng, ang); maxPos = Math.Max(maxPos, dp);
                if (ang > 0.01 || dp > 1e-5) { bad++; if (worst.Count < 6) worst.Add($"{t.name} {ang:F3}°"); }
            }
        c.Check(n > 0 && bad == 0, "physics off: every dynamic-bone particle bone of A (T-pose frames before Idle_00) equals B's (Idle_00 before any frame)",
            $"{n} bones, max {maxAng:G3}° / {maxPos:G3}; {bad} over" + (worst.Count > 0 ? ": " + string.Join(", ", worst) : ""));
    }

    /// <summary>The angle between two rotations in degrees, in doubles (Quaternion.Angle answers 0 below about 0.16°).</summary>
    static double FAngle(Quaternion a, Quaternion b)
    {
        double dot = (double)a.x * b.x + (double)a.y * b.y + (double)a.z * b.z + (double)a.w * b.w;
        double na = (double)a.x * a.x + (double)a.y * a.y + (double)a.z * a.z + (double)a.w * a.w;
        double nb = (double)b.x * b.x + (double)b.y * b.y + (double)b.z * b.z + (double)b.w * b.w;
        return 2 * Math.Acos(Math.Min(1, Math.Abs(dot) / Math.Sqrt(na * nb))) * 180 / Math.PI;
    }

    // ================================================================ C13 determinism

    /// <summary>C13: two characters of the same card, equal switches, animation and hand steps → bones.dump all equal value for value.</summary>
    static void C13(Ctx c)
    {
        int? seed = FaceSetup.Seed;
        try
        {
            FaceSetup.Seed = FaceSeed;
            c.Host();
            var batch = c.Ok(new
            {
                cmd = "batch",
                cmds = new object[]
                {
                    new Dictionary<string, object> { ["cmd"] = "chara.load", ["path"] = c.FemaleRel, ["sex"] = "female", ["as"] = "a" },
                    new Dictionary<string, object> { ["cmd"] = "chara.motion", ["key"] = "$a.added.0.key", ["autoStep"] = false, ["physics"] = false },
                    new Dictionary<string, object> { ["cmd"] = "chara.load", ["path"] = c.FemaleRel, ["sex"] = "female", ["as"] = "b" },
                    new Dictionary<string, object> { ["cmd"] = "chara.motion", ["key"] = "$b.added.0.key", ["autoStep"] = false, ["physics"] = false },
                },
            }, "batch: two BR-Chan at the origin, autoStep and physics off before any frame");
            string ka = (string)batch["results"]?[0]?["added"]?[0]?["key"], kb = (string)batch["results"]?[2]?["added"]?[0]?["key"];
            if (ka == null || kb == null) { c.Check(false, "both characters added", Ctx.Short(c.LastText)); return; }
            foreach (var k in new[] { ka, kb })
            {
                c.Ok(new { cmd = "chara.anime", key = k, group = 19, category = 0, no = 0, time = 0, speed = 1 }, "chara.anime Idle_00 " + k.Substring(0, 8));
                // reset drops the earlier overrides: the switches go with it in the same call
                c.Ok(new { cmd = "chara.motion", key = k, autoStep = false, physics = false, reset = true }, "chara.motion {autoStep false, physics false, reset} " + k.Substring(0, 8));
            }
            var sa = c.Ok(new { cmd = "chara.motion", key = ka, step = new { dt = Dt, n = 60 } }, "A: step n 60");
            var sb = c.Ok(new { cmd = "chara.motion", key = kb, step = new { dt = Dt, n = 60 } }, "B: step n 60");
            c.Check((int)sa["stepCount"] == 60 && (int)sb["stepCount"] == 60 && W2D(sa["time"]) == W2D(sb["time"]), "both stepped 60 times from the reset", $"{sa["stepCount"]} / {sb["stepCount"]}");
            var da = c.Ok(new { cmd = "bones.dump", key = ka, root = "all" }, "bones.dump all A");
            var db = c.Ok(new { cmd = "bones.dump", key = kb, root = "all" }, "bones.dump all B");
            var ba = da["bones"] as JArray;
            var bb = db["bones"] as JArray;
            int n = Math.Min(ba?.Count ?? 0, bb?.Count ?? 0), different = 0;
            var first = new List<string>();
            for (int i = 0; i < n; i++)
            {
                if (ba[i].ToString(Formatting.None) == bb[i].ToString(Formatting.None)) continue;
                different++;
                if (first.Count < 6) first.Add((string)ba[i]["path"]);
            }
            c.Check(ba != null && bb != null && ba.Count == bb.Count && ba.Count > 700 && different == 0, "bones.dump all of A = B value for value",
                $"{ba?.Count} / {bb?.Count} entries, {different} different" + (first.Count > 0 ? ": " + string.Join(", ", first) : ""));
        }
        finally { FaceSetup.Seed = seed; }
    }

    // ================================================================ C14 FK and poses

    /// <summary>C14: fk.list, fk.enable capture and undo, fk.set, pose.load of compare case 1, hs2.pose through save / load, pose.list.</summary>
    static void C14(Ctx c)
    {
        c.Host();
        string id = W2LoadFemale(c);
        if (id == null) return;
        c.Ok(new { cmd = "chara.motion", key = id, autoStep = false }, "chara.motion {autoStep false} (a still pose)");
        var fl = c.Ok(new { cmd = "fk.list", key = id, exact = true }, "fk.list exact");
        var keys = (fl["bones"] as JArray)?.OfType<JObject>().Select(x => (int)x["key"]).ToList() ?? new List<int>();
        bool sorted = keys.Count > 0 && keys.Zip(keys.Skip(1), (x, y) => x <= y).All(v => v);
        var groups = new HashSet<string>((fl["bones"] as JArray)?.OfType<JObject>().Select(x => (string)x["group"]) ?? new string[0]);
        c.Check(sorted && groups.Contains("3") && groups.Contains("Body") && groups.Contains("Neck"), "entries sorted by key; group texts include \"3\", \"Body\", \"Neck\"",
            keys.Count + " entries, groups " + string.Join(",", groups.OrderBy(x => x)));
        var hips = (fl["bones"] as JArray)?.OfType<JObject>().FirstOrDefault(x => (int)x["key"] == 0);
        c.Check(hips != null && (string)hips["bone"] == "cf_J_Hips" && (string)hips["group"] == "Body" && (int)hips["groupFlags"] == 1 && hips["scale"] is JArray,
            "key 0 is cf_J_Hips, Body, flags 1, with scale (exact)", Ctx.Short(hips?.ToString(Formatting.None)));
        c.Info("hair entries: " + string.Join(", ", (fl["bones"] as JArray)?.OfType<JObject>().Where(x => (string)x["group"] == "Hair")
            .Select(x => x["key"] + " " + x["bone"] + (x["name"] != null ? " (" + x["name"] + ")" : "")) ?? new string[0]));
        var listed = new HashSet<int>(keys);
        var partners = HS2Next.Scenes.Runtime.FkDriver.TableFor(W2Real(c, id))?.Bones.Where(b => b.Partner).Select(b => b.PartnerKey).ToList() ?? new List<string>();
        var partnerRows = HS2Next.Scenes.Runtime.FkRows.From(c.Services.Db).Where(x => x.SyncNo >= 0 && listed.Contains(x.SyncNo) && listed.Contains(x.No) &&
                                                                                  partners.Contains(HS2Next.Motion.Fk.FkBoneTable.NormaliseHairName(x.Name))).Select(x => x.No).ToList();
        c.Check(partnerRows.Count == 0, "no sync partner of a listed bone has an entry of its own (Studio's listBones)", string.Join(", ", partnerRows));
        var limb = c.Ok(new { cmd = "fk.list", key = id, group = "3" }, "fk.list {group \"3\"}");
        c.Check((limb["bones"] as JArray)?.OfType<JObject>().All(x => ((string)x["group"]).Contains("3")) == true && (limb["bones"] as JArray)?.Count > 0 && !(((JObject)limb["bones"][0])["scale"] is JArray),
            "the group filter keeps \"3\" entries; no scale without exact");
        c.Check((bool?)fl["fkEnabled"] == false && (fl["activeFK"] as JArray)?.Select(x => (bool)x).SequenceEqual(new[] { false, true, false, true, false, false, false }) == true,
            "a fresh character: FK off, Neck and Body flagged", Ctx.Short(fl["activeFK"]?.ToString(Formatting.None)));

        // fk.enable captures the missing values (one entry; undo removes them)
        var dump0 = c.Ok(new { cmd = "bones.dump", key = id }, "bones.dump body (before fk.enable)");
        var sp0 = W2Bone(dump0, "cf_J_Spine01");
        var le0 = sp0 != null ? new Vector3((float)W2D(sp0["le"][0]), (float)W2D(sp0["le"][1]), (float)W2D(sp0["le"][2])) : Vector3.zero;
        int undo0 = c.App.Session.Commands.UndoList.Count;
        var en = c.Ok(new { cmd = "fk.enable", key = id }, "fk.enable {} (FK on)");
        c.Check((bool?)en["fkEnabled"] == true && (bool?)en["ikEnabled"] == false && en["activeFK"] is JArray, "reply {fkEnabled true, ikEnabled false, activeFK}", Ctx.Short(c.LastText));
        var st = (CharaNodeState)c.App.Session.Model.Find(id).State;
        bool has = st.Fk.Bones.TryGetValue("cf_J_Spine01", out var v);
        float ang = has ? W2Angle(Quaternion.Euler(v.X, v.Y, v.Z), Quaternion.Euler(le0)) : float.NaN;
        c.Check(has && ang <= 1e-4f, "the model rot of cf_J_Spine01 = its localEulerAngles before the call ± 1e-4°", has ? $"({v.X}, {v.Y}, {v.Z}) vs {le0}, {ang:G3}°" : "no value");
        c.Check(c.App.Session.Commands.UndoList.Count == undo0 + 1 && c.App.Session.Commands.UndoList[0].Label == "FK", "one history entry \"FK\"",
            string.Join(", ", c.App.Session.Commands.UndoList.Take(3).Select(x => x.Label)));
        if (W2Has(c, "undo")) c.Ok(new { cmd = "undo" }, "undo");
        else { c.Warn("undo is not registered yet (package W1): Session.Commands.Undo"); c.App.Session.Commands.Undo(); c.App.Runtime.ApplyNow(); }
        st = (CharaNodeState)c.App.Session.Model.Find(id).State;
        c.Check(!st.Fk.Enabled && !st.Fk.Bones.ContainsKey("cf_J_Spine01"), "undo removes the captured values and FK again", st.Fk.Bones.Count + " values");

        // fk.set (FK on again)
        c.Ok(new { cmd = "fk.enable", key = id, on = true }, "fk.enable {on: true}");
        var set = c.Ok(new { cmd = "fk.set", key = id, bones = new Dictionary<string, object> { ["cf_J_Spine01"] = new[] { 10f, 0f, 0f }, ["no_such_bone"] = new[] { 1f, 2f, 3f } } },
            "fk.set {cf_J_Spine01: [10, 0, 0], no_such_bone}");
        c.Check((int?)set["set"] == 1 && (set["missing"] as JArray)?.Select(x => (string)x).SequenceEqual(new[] { "no_such_bone" }) == true, "set 1, missing [no_such_bone]", Ctx.Short(c.LastText));
        var dump1 = c.Ok(new { cmd = "bones.dump", key = id }, "bones.dump body (after fk.set)");
        var sp1 = W2Bone(dump1, "cf_J_Spine01");
        float a1 = sp1 != null ? W2Angle(W2Q(sp1["lq"]), Quaternion.Euler(10, 0, 0)) : float.NaN;
        c.Check(a1 <= 1e-4f, "bones.dump local rotation of cf_J_Spine01 = Euler(10, 0, 0) ± 1e-4°", a1.ToString("G3") + "°");
        var fs = c.Fails(new { cmd = "fk.enable", key = id, groups = new[] { "Tail" } }, "bad_args", "fk.enable {groups: [\"Tail\"]} → bad_args");
        c.Check((fs["detail"]?["candidates"] as JArray)?.Count == 7, "with the 7 group names as candidates", Ctx.Short(c.LastText));
        var gr = c.Ok(new { cmd = "fk.enable", key = id, groups = new[] { "righthand", "LeftHand" }, on = true }, "fk.enable {groups [righthand, LeftHand]}");
        c.Check((gr["activeFK"] as JArray)?.Select(x => (bool)x).SequenceEqual(new[] { false, true, false, true, true, true, false }) == true, "the hand groups on (names ignoring case)", Ctx.Short(gr["activeFK"]?.ToString(Formatting.None)));
        var ik = c.Ok(new { cmd = "ik.enable", key = id, on = true }, "ik.enable {on: true}");
        c.Check((bool?)ik["ikEnabled"] == true && (bool?)ik["fkEnabled"] == true && (ik["warnings"] as JArray)?.Any(x => ((string)x).Contains("3.4")) != true,
            "ik.enable switches IK on without the 2.4 warning (Phase 3.4 solver); FK stays on (kinematics.exclusive off)", Ctx.Short(c.LastText));
        c.Ok(new { cmd = "ik.enable", key = id, on = false }, "ik.enable {on: false}");

        // fk.capture
        var cap = c.Ok(new { cmd = "fk.capture", key = id }, "fk.capture");
        st = (CharaNodeState)c.App.Session.Model.Find(id).State;
        c.Check((bool?)cap["fkEnabled"] == true && st.Fk.Groups.Body && st.Fk.Groups.Neck && st.Anime.Speed == 0f && c.App.Session.Commands.UndoList[0].Label == "FK from pose",
            "fk.capture: FK on, Body + Neck, speed 0, one entry \"FK from pose\"", st.Anime.Speed + ", " + c.App.Session.Commands.UndoList[0].Label);

        // pose.load of compare case 1
        string poseFull = Path.Combine(PoseRoot, PoseCase1.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(poseFull)) { c.Warn("pose file of compare case 1 not found: " + PoseCase1); return; }
        int undo1 = c.App.Session.Commands.UndoList.Count;
        var pl = c.Ok(new { cmd = "pose.load", key = id, path = PoseCase1 }, "pose.load compare case 1");
        c.Check((bool?)pl["loaded"] == true && (bool?)pl["sexMatches"] == true && (string)pl["name"] == "Pose 1" && pl["object"] is JObject && (int?)pl["keys"] == 64,
            "loaded, sexMatches, name \"Pose 1\", object, 64 keys", Ctx.Short(c.LastText));
        c.Check(c.App.Session.Commands.UndoList.Count == undo1 + 1 && c.App.Session.Commands.UndoList[0].Label == "Load pose", "one history entry \"Load pose\"");
        var fl2 = c.Ok(new { cmd = "fk.list", key = id, exact = true }, "fk.list exact after the pose");
        var pose = PoseFile.ReadFile(poseFull);
        int cmp = 0, bad = 0;
        var badKeys = new List<string>();
        foreach (var e in (fl2["bones"] as JArray).OfType<JObject>())
        {
            var pe = pose.FindFk((int)e["key"]);
            if (pe == null) continue;
            cmp++;
            var rot = e["rot"];
            if ((float)W2D(rot[0]) != pe.Rot.X || (float)W2D(rot[1]) != pe.Rot.Y || (float)W2D(rot[2]) != pe.Rot.Z) { bad++; if (badKeys.Count < 6) badKeys.Add(e["key"] + " " + rot.ToString(Formatting.None) + " vs " + pe.Rot); }
        }
        c.Check(cmp >= 60 && bad == 0, "fk.list exact rot = the pose file's values (float ==)", $"{cmp} keys compared, {bad} different " + string.Join("; ", badKeys));
        // the hs2.pose block survives scene.save → scene.load
        W2PoseBlockRoundTrip(c, id);
        // pose.list
        var pls = c.Ok(new { cmd = "pose.list", dir = "STANDING/Floor", q = "Pose 1" }, "pose.list {dir STANDING/Floor, q \"Pose 1\"}");
        c.Check((pls["poses"] as JArray)?.OfType<JObject>().Any(x => (string)x["path"] == PoseCase1 && (string)x["kind"] == "dat") == true && (int?)pls["total"] >= 1,
            "pose.list lists " + PoseCase1 + " (path relative to the pose root)", Ctx.Short(c.LastText));
        c.Fails(new { cmd = "pose.list", dir = "../x" }, "io_denied", "pose.list dir \"../x\" → io_denied");
        c.Fails(new { cmd = "pose.load", key = id, path = "no/such/pose.dat" }, "not_found", "pose.load of a missing file → not_found");
    }

    static void W2PoseBlockRoundTrip(Ctx c, string id)
    {
        string nodeBefore, nodeAfter;
        if (W2Has(c, "scene.doc") && W2Has(c, "scene.save") && W2Has(c, "scene.load"))
        {
            var d1 = c.Ok(new { cmd = "scene.doc" }, "scene.doc");
            nodeBefore = (string)c.Ok(new { cmd = "scene.doc", key = id }, "scene.doc {key}")["node"];
            c.Check(((string)d1["scene"])?.Contains("hs2.pose") == true, "the hs2.pose block is in the scene.doc text");
            c.Ok(new { cmd = "scene.save", name = "w2_c14_pose", overwrite = true, thumb = "placeholder" }, "scene.save");
            // scene.load drops the motion overrides: the still pose is switched on again in the same pump (node ids are kept)
            c.Ok(new
            {
                cmd = "batch",
                cmds = new object[] { new { cmd = "scene.load", name = "w2_c14_pose" }, new { cmd = "chara.motion", key = id, autoStep = false } },
            }, "batch [scene.load, chara.motion {autoStep false}]");
            nodeAfter = (string)c.Ok(new { cmd = "scene.doc", key = id }, "scene.doc {key} after the reload")["node"];
            c.Check(nodeBefore != null && nodeBefore == nodeAfter, "the whole node document is identical after save → load (still pose)",
                (nodeBefore?.Length ?? -1) + " / " + (nodeAfter?.Length ?? -1) + " chars");
        }
        else
        {
            c.Warn("scene.doc / scene.save / scene.load are not registered yet (package W1): the hs2.pose round trip goes through the session");
            nodeBefore = c.App.Session.NodeJson(id);
            string file = Path.Combine(c.UserDataDir, "scene", "w2_c14_pose.hs2nscene");
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            var sr = c.App.Runtime.Ops.Save(file, new SaveOptions { Thumbnail = ThumbnailMode.Placeholder });
            c.Check(sr.Ok, "session save", sr.ToString());
            var lr = c.App.Runtime.Ops.Load(file);
            c.Check(lr.Ok, "session load", lr.Error?.Message ?? "");
            nodeAfter = c.App.Session.NodeJson(id);
        }
        string b1 = W2PoseBlock(nodeBefore), b2 = W2PoseBlock(nodeAfter);
        c.Check(b1 != null, "the node's document holds the hs2.pose block", b1 == null ? Ctx.Short(nodeBefore) : b1.Length + " chars");
        c.Check(b1 != null && b1 == b2, "the hs2.pose block is identical after save → load", (b1?.Length ?? -1) + " / " + (b2?.Length ?? -1) + " chars");
        if (nodeBefore != nodeAfter)
        {
            int i = 0;
            while (nodeBefore != null && nodeAfter != null && i < nodeBefore.Length && i < nodeAfter.Length && nodeBefore[i] == nodeAfter[i]) i++;
            c.Info($"the node documents differ from char {i} ({nodeBefore?.Length} / {nodeAfter?.Length} chars): …{Ctx.Short(nodeBefore?.Substring(Math.Max(0, i - 40)))} | …{Ctx.Short(nodeAfter?.Substring(Math.Max(0, i - 40)))}");
            File.WriteAllText(Path.Combine(c.Out, "w2_c14_node_before.json"), nodeBefore ?? "");
            File.WriteAllText(Path.Combine(c.Out, "w2_c14_node_after.json"), nodeAfter ?? "");
        }
    }

    /// <summary>The canonical text of the `hs2.pose` member anywhere in a node document, or null.</summary>
    static string W2PoseBlock(string nodeJson)
    {
        if (string.IsNullOrEmpty(nodeJson)) return null;
        JObject o;
        try { o = JObject.Parse(nodeJson); } catch (Exception) { return null; }
        var p = o.Descendants().OfType<JProperty>().FirstOrDefault(x => x.Name == "hs2.pose");
        return p?.Value.ToString(Formatting.None);
    }

    // ================================================================ C15 dumps vs HS2 shape

    /// <summary>C15: the replies of bones.dump all, face.dump, fk.list exact, camera.state and scene vs the HS2 recordings.</summary>
    static void C15(Ctx c)
    {
        string rest = Path.Combine(FemaleRecordings, "rest.json"), pose1 = Path.Combine(FemaleRecordings, "pose_1.json");
        if (!File.Exists(rest) || !File.Exists(pose1)) { c.Warn("Cache/compare recordings of BR-Chan not found: the HS2-shape check is skipped"); return; }
        var recRest = W2ReadJson(rest);
        var recPose = W2ReadJson(pose1);
        c.Host();
        string id = W2LoadFemale(c);
        if (id == null) return;
        var info = new List<string>();
        var diffs = new List<string>();
        ShapeDump(diffs, info, recRest["dump"] as JObject, Result(c.Ok(new { cmd = "bones.dump", key = id, root = "all" }, "bones.dump all")));
        foreach (var l in info) c.Info(l);
        W2ReportShape(c, "bones.dump all: HS2's members, order, kinds, ≥ 99 % of the body and head paths", diffs);
        diffs = new List<string>();
        ShapeFace(diffs, recRest["face"] as JObject, Result(c.Ok(new { cmd = "face.dump", key = id }, "face.dump")));
        W2ReportShape(c, "face.dump: HS2's members, order and kinds (fbs absent by design)", diffs);
        c.Ok(new { cmd = "pose.load", key = id, path = PoseCase1 }, "pose.load compare case 1");
        diffs = new List<string>();
        ShapeFk(diffs, recPose["fk"] as JObject, Result(c.Ok(new { cmd = "fk.list", key = id, exact = true }, "fk.list exact")));
        W2ReportShape(c, "fk.list exact: HS2's members, order and kinds", diffs);
        if (W2Has(c, "camera.state"))
        {
            diffs = new List<string>();
            ShapeCamera(diffs, recRest["visual"]?["face"]?["camera"] as JObject, c.Ok(new { cmd = "camera.state" }, "camera.state")["camera"] as JObject);
            W2ReportShape(c, "camera.state: HS2's members, order and kinds", diffs);
        }
        else c.Warn("camera.state is not registered yet (package W1): skipped");
        if (W2Has(c, "scene"))
        {
            var sc = c.Ok(new { cmd = "scene" }, "scene");
            var o = (sc["objects"] as JArray)?.OfType<JObject>().FirstOrDefault(x => (string)x["key"] == id);
            diffs = new List<string>();
            ShapeSceneObject(diffs, recRest["state"] as JObject, o);
            W2ReportShape(c, "scene.objects[]: HS2's members, order and kinds (anime)", diffs);
        }
        else c.Warn("scene is not registered yet (package W1): skipped");
    }

    // ================================================================ C16 number forms

    /// <summary>C16: bones.dump lp = (double) of the floats bit for bit; R4 values = Math.Round((double)f, 4); fk.list rounded / exact.</summary>
    static void C16(Ctx c)
    {
        c.Host();
        string id = W2LoadFemale(c);
        if (id == null) return;
        c.Ok(new { cmd = "chara.motion", key = id, autoStep = false }, "chara.motion {autoStep false} (transforms frozen)");
        c.Ok(new { cmd = "pose.load", key = id, path = PoseCase1 }, "pose.load compare case 1 (non-trivial FK values)");
        var r = W2Real(c, id);
        var dump = c.Ok(new { cmd = "bones.dump", key = id, root = "all" }, "bones.dump all");
        string raw = c.LastText;
        var paths = W2DumpPaths(r);
        int n = 0, bad = 0, values = 0, zeroSign = 0;
        var badList = new List<string>();
        foreach (var b in (dump["bones"] as JArray).OfType<JObject>())
        {
            if (!paths.TryGetValue((string)b["path"], out var t)) continue;
            var p = t.localPosition; var q = t.localRotation; var e = t.localEulerAngles; var s = t.localScale;
            var want = new[] { p.x, p.y, p.z, q.x, q.y, q.z, q.w, e.x, e.y, e.z, s.x, s.y, s.z };
            var got = new[] { b["lp"][0], b["lp"][1], b["lp"][2], b["lq"][0], b["lq"][1], b["lq"][2], b["lq"][3], b["le"][0], b["le"][1], b["le"][2], b["ls"][0], b["ls"][1], b["ls"][2] };
            bool ok = true;
            for (int i = 0; i < want.Length; i++)
            {
                values++;
                double g = W2D(got[i]), w = want[i];
                if (BitConverter.DoubleToInt64Bits(g) == BitConverter.DoubleToInt64Bits(w)) continue;
                // Mono's double.Parse drops the sign of -0 ("-0.0" → +0): equal values with zeros are a reader artifact
                if (g == w && w == 0) { zeroSign++; continue; }
                ok = false;
                if (badList.Count < 4)
                {
                    int at = raw.IndexOf("\"path\":\"" + (string)b["path"] + "\"", StringComparison.Ordinal);
                    badList.Add($"{(string)b["path"]}[{i}]: reply {g:R} vs (double)f {w:R} (float {want[i]:R}); text …{(at >= 0 ? raw.Substring(at, Math.Min(260, raw.Length - at)) : "?")}");
                }
            }
            n++;
            if (!ok) bad++;
        }
        c.Check(n > 700 && bad == 0, "bones.dump lp / lq / le / ls = (double) of the transform floats, bit for bit", $"{n} entries ({values} values; {zeroSign} zeros read back without their sign), {bad} different " + string.Join(" || ", badList));

        var names = new[] { "cf_J_Head", "cf_J_Hand_R", "cf_J_Foot01_L" };
        var cb = c.Ok(new { cmd = "chara.bones", key = id, names = names.Concat(new[] { "no_such_bone" }).ToArray() }, "chara.bones");
        int rn = 0, rbad = 0;
        foreach (var nm in names)
        {
            var t = r.Context.BodyBones.TryGetValue(nm, out var bt) ? bt : null;
            var e = cb["bones"]?[nm];
            if (!t || e == null) { rbad++; continue; }
            rn++;
            var pos = t.position; var fw = t.forward;
            if (W2D(e["pos"][0]) != Math.Round((double)pos.x, 4) || W2D(e["pos"][1]) != Math.Round((double)pos.y, 4) || W2D(e["pos"][2]) != Math.Round((double)pos.z, 4) ||
                W2D(e["forward"][0]) != Math.Round((double)fw.x, 4) || W2D(e["forward"][2]) != Math.Round((double)fw.z, 4)) rbad++;
        }
        c.Check(rn == 3 && rbad == 0 && cb["bones"]?["no_such_bone"] == null && cb["time"] != null, "chara.bones pos / forward = Math.Round((double)f, 4); unknown names left out", Ctx.Short(c.LastText));

        var st = (CharaNodeState)c.App.Session.Model.Find(id).State;
        var round = c.Ok(new { cmd = "fk.list", key = id }, "fk.list (rounded)");
        var exact = c.Ok(new { cmd = "fk.list", key = id, exact = true }, "fk.list exact");
        int fn = 0, fbad = 0;
        foreach (var e in (round["bones"] as JArray).OfType<JObject>())
        {
            string key = (string)e["name"] ?? (string)e["bone"];
            if (!st.Fk.Bones.TryGetValue(key, out var v)) continue;
            var x = (exact["bones"] as JArray).OfType<JObject>().FirstOrDefault(y => (int)y["key"] == (int)e["key"] && (string)y["bone"] == (string)e["bone"]);
            fn++;
            if (W2D(e["rot"][0]) != Math.Round((double)v.X, 4) || W2D(e["rot"][1]) != Math.Round((double)v.Y, 4) || W2D(e["rot"][2]) != Math.Round((double)v.Z, 4)) fbad++;
            else if (x == null || W2D(x["rot"][0]) != (double)v.X || W2D(x["rot"][1]) != (double)v.Y || W2D(x["rot"][2]) != (double)v.Z) fbad++;
        }
        c.Check(fn >= 50 && fbad == 0, "fk.list rot: rounded to 4 decimals without exact, the exact double with exact", $"{fn} values, {fbad} different");
        if (W2Has(c, "obj.transform") && W2Has(c, "scene"))
        {
            c.Ok(new { cmd = "obj.transform", key = id, pos = new[] { 1.23456789f, -0.00005f, 2.71828f } }, "obj.transform {pos}");
            var o = (c.Ok(new { cmd = "scene" }, "scene")["objects"] as JArray)?.OfType<JObject>().FirstOrDefault(x => (string)x["key"] == id);
            var p = o?["pos"];
            c.Check(p != null && W2D(p[0]) == Math.Round((double)1.23456789f, 4) && W2D(p[1]) == Math.Round((double)-0.00005f, 4) && W2D(p[2]) == Math.Round((double)2.71828f, 4),
                "scene positions = Math.Round((double)f, 4)", Ctx.Short(p?.ToString(Formatting.None)));
        }
        else c.Warn("obj.transform / scene are not registered yet (package W1): the scene position form is skipped");
    }

    // ================================================================ C17 timings

    /// <summary>C17: handler times of scene (10 characters + 200 items), bones.dump all, scene.doc, obj.transform (FAIL at 2× the target).</summary>
    static void C17(Ctx c)
    {
        c.Host();
        string id = W2LoadFemale(c);
        if (id == null) return;
        W2Timing(c, "bones.dump all", W2TimeCall(c, new { cmd = "bones.dump", key = id, root = "all" }, 7, out _), 30);
        W2Timing(c, "face.dump", W2TimeCall(c, new { cmd = "face.dump", key = id }, 5, out _), 30);
        W2Timing(c, "fk.list exact", W2TimeCall(c, new { cmd = "fk.list", key = id, exact = true }, 5, out _), 20);
        W2Timing(c, "chara.motion (read)", W2TimeCall(c, new { cmd = "chara.motion", key = id }, 7, out _), 2);
        if (W2Has(c, "scene.doc")) W2Timing(c, "scene.doc of the acceptance scene (one character)", W2TimeCall(c, new { cmd = "scene.doc" }, 5, out _), 50);
        else c.Warn("scene.doc is not registered yet (package W1): skipped");
        if (W2Has(c, "obj.transform")) W2Timing(c, "obj.transform", W2TimeCall(c, new { cmd = "obj.transform", key = id, pos = new[] { 0.5f, 0f, 0f } }, 7, out _), 2);
        else c.Warn("obj.transform is not registered yet (package W1): skipped");
        if (W2Has(c, "scene") && W2Has(c, "item.add"))
        {
            for (int i = 1; i < 10; i++) c.Ok(new { cmd = "chara.load", path = (i % 2 == 0 ? c.FemaleRel : c.MaleRel), sex = i % 2 == 0 ? "female" : "male" }, "chara.load " + (i + 1) + " of 10");
            int items = 0;
            for (int i = 0; i < 200; i++) if (c.Call(new { cmd = "item.add", no = 399 }).Value<bool?>("ok") == true) items++;
            c.Check(items == 200, "200 items added", "" + items);
            W2Timing(c, "scene of 10 characters + 200 items", W2TimeCall(c, new { cmd = "scene" }, 5, out var sc), 20);
            c.Info("scene objects: " + ((sc?["objects"] as JArray)?.Count ?? -1));
        }
        else c.Warn("scene / item.add are not registered yet (package W1): the 10 characters + 200 items timing is skipped");
    }
}
