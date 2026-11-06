// Bridge dumps of a built character in HS2's reply shape (Appendix B):
//   bones.dump   every transform under cf_J_Root (body: without the head bone root), under p_cf_head_bone (head), or
//                both (all: body, then head with paths that start at the head root's name), depth-first in child
//                order; name, '/'-path from the root, local position / rotation / Euler / scale as exact doubles (D)
//   face.dump    the head's skinned renderers (blend-shape weights, the face material values), the realized expression
//                and look state under HS2's names, and (reflect) the look angles CompareTest reads and the stages' switches
//   chara.bones  world position and forward of named bones (rounded, R4) and Time.time (F)
// None of them enters a SceneNodeHolder subtree: scene objects attached at N_* points are not part of the character
// (decision B12). bones.dump also takes a built Studio item (Phase 3.1): the walk starts at
// the item's instance root (path "" = the root), stops at scene node holders (child scene objects) and ignores `root`. Read-only: the handlers call Runtime.ApplyNow first, so a dump in the same request as a change sees it.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using HS2Next.Api.Bridge;
using HS2Next.Chara;
using HS2Next.Motion;
using HS2Next.Motion.Face;
using HS2Next.Motion.Look;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json;
using UnityEngine;

namespace HS2Next.App.Commands
{
    public static class DumpCommands
    {
        /// <summary>Face material properties HS2's face.dump reports when a material has them (in this order).</summary>
        public static readonly string[] FaceProperties = { "_NamidaScale", "_Texture4Scale", "_Smoothness", "_HohoAkaRate" };

        public static void Register(ICommandRegistry r, AppCore app)
        {
            CommandKit.Register(r, app, CommandKit.Spec("bones.dump", "Every bone transform of a built character (or every transform of a built item) in HS2's form: local position / rotation / Euler / scale as exact doubles, '/'-paths from the root.", "dumps")
                    .Arg("key", ArgTypes.Node, "a built character or item", true)
                    .Arg("root", ArgTypes.Enum("body", "head", "all"), "body: cf_J_Root without the head bone root; head: p_cf_head_bone; all: body, then head (paths start at the head root's name); ignored for an item (its instance root)", false, "body")
                    .Return("bones", ArgTypes.ArrayOf(ArgTypes.Object), "{name, path, lp, lq, le, ls}")
                    .WithFlags(CommandFlags.ReadOnly).WithTimeout(120),
                BonesDump);
            CommandKit.Register(r, app, CommandKit.Spec("face.dump", "The head's skinned renderers (blend shapes, weights, face materials), the realized expression and look state of a built character (HS2's form).", "dumps")
                    .Arg("key", ArgTypes.Node, "a built character", true)
                    .Arg("reflect", ArgTypes.Bool, "also the look angles (eyeLook, neckLook under HS2's names) and the stage switches", false, true)
                    .Return("renderers", ArgTypes.ArrayOf(ArgTypes.Object)).Return("expression", ArgTypes.Object)
                    .WithFlags(CommandFlags.ReadOnly | CommandFlags.Experimental),
                FaceDump);
            CommandKit.Register(r, app, CommandKit.Spec("chara.bones", "World position and forward of named bones of a built character (HS2: rounded to 4 decimals); unknown names are left out.", "dumps")
                    .Arg("key", ArgTypes.Node, "a built character", true)
                    .Arg("names", ArgTypes.ArrayOf(ArgTypes.String), "bone transform names: the body skeleton, the head skeleton, then a search under the character", true)
                    .Return("bones", ArgTypes.Object, "{<name>: {pos, forward}}").Return("time", ArgTypes.Float, "Time.time")
                    .WithFlags(CommandFlags.ReadOnly),
                CharaBones);
        }

        // ------------------------------------------------------------ bones.dump

        static object BonesDump(CoreContext c)
        {
            var node = CommandKit.Node(c, "key");
            if (node.Kind == HS2Next.Scenes.NodeKind.Item) return BonesDump(ItemMotionCommands.BuiltItem(c, node), c.Frames.Frame);
            var r = CommandKit.Built(c);
            return BonesDump(r, c.Args.Get("root", "body"), c.Frames.Frame);
        }

        /// <summary>
        /// The bones.dump reply of a built item (also used by the tests): every transform under the instance root, depth-first in
        /// child order, without child scene objects; HS2's members (root, rootParent, anim null, count, frame, updatePending).
        /// </summary>
        public static BridgeReply BonesDump(ItemRealization r, int frame)
        {
            var root = r.Instance ? r.Instance.transform : null;
            if (!root) throw CommandKit.NotRealized("item " + r.NodeId + " has no instance", "key", r.State.ToString());
            var sw = new StringWriter(CultureInfo.InvariantCulture);
            int count = 0;
            using (var w = new JsonTextWriter(sw) { Formatting = Formatting.None })
            {
                w.WriteStartArray();
                Walk(w, root, "", null, ref count);
                w.WriteEndArray();
            }
            return new BridgeReply
            {
                ["root"] = root.name,
                ["rootParent"] = root.parent ? root.parent.name : null,
                ["anim"] = null,
                ["count"] = count,
                ["frame"] = frame,
                ["updatePending"] = false,
                ["bones"] = new RawJson(sw.ToString()),
            };
        }

        /// <summary>The bones.dump reply of a built character (also used by the tests).</summary>
        public static BridgeReply BonesDump(CharaRealization r, string which, int frame)
        {
            var cc = r.Context;
            var body = cc?.Anim ? CharaAssembler.Find(cc.Anim.transform, "cf_J_Root") : null;
            var head = cc?.HeadBone ? cc.HeadBone.transform : null;
            Transform root, skip = null;
            switch (which)
            {
                case "body":
                    if (!body) throw Unavailable(r, "no body bones (cf_J_Root)");
                    root = body; skip = head;
                    break;
                case "head":
                    if (!head) throw Unavailable(r, "no head bones (p_cf_head_bone)");
                    root = head;
                    break;
                case "all":
                    if (!body || !head) throw Unavailable(r, "no body or head bones");
                    root = body; skip = head;
                    break;
                default:
                    throw new BridgeException(BridgeError.BadArgs("root", ArgTypes.Enum("body", "head", "all")));
            }
            // the entries are written straight into JSON text (the same writer and number forms as the bridge's replies:
            // D doubles in round-trip form, non-finite as null): ~800 entries as objects would cost more than the walk
            var sw = new StringWriter(CultureInfo.InvariantCulture);
            int count = 0, bodyCount = 0;
            using (var w = new JsonTextWriter(sw) { Formatting = Formatting.None })
            {
                w.WriteStartArray();
                Walk(w, root, "", skip, ref count);
                if (which == "all")
                {
                    bodyCount = count;
                    Walk(w, head, head.name, null, ref count);
                }
                w.WriteEndArray();
            }
            var rep = new BridgeReply();
            if (which == "all")
            {
                rep["bodyCount"] = bodyCount;
                rep["headRoot"] = head.name;
                rep["headRootParent"] = head.parent ? head.parent.name : null;
            }
            rep["root"] = root.name;
            rep["rootParent"] = root.parent ? root.parent.name : null;
            rep["anim"] = cc.Anim ? cc.Anim.name : null;
            rep["count"] = count;
            rep["frame"] = frame;
            rep["updatePending"] = false;   // shapes apply at once in HS2Next
            rep["bones"] = new RawJson(sw.ToString());
            return rep;
        }

        // depth-first in child order (HS2's DumpWalk), never entering the skipped transform or a scene node holder
        static void Walk(JsonTextWriter w, Transform t, string path, Transform skip, ref int count)
        {
            if (t == skip) return;
            w.WriteStartObject();
            w.WritePropertyName("name"); w.WriteValue(t.name);
            w.WritePropertyName("path"); w.WriteValue(path);
            var p = t.localPosition; var q = t.localRotation; var e = t.localEulerAngles; var s = t.localScale;
            w.WritePropertyName("lp"); Nums(w, p.x, p.y, p.z);
            w.WritePropertyName("lq"); Nums(w, q.x, q.y, q.z, q.w);
            w.WritePropertyName("le"); Nums(w, e.x, e.y, e.z);
            w.WritePropertyName("ls"); Nums(w, s.x, s.y, s.z);
            w.WriteEndObject();
            count++;
            for (int i = 0; i < t.childCount; i++)
            {
                var c = t.GetChild(i);
                if (SceneNodeHolder.IsHolder(c)) continue;
                Walk(w, c, path.Length == 0 ? c.name : path + "/" + c.name, skip, ref count);
            }
        }

        // D form: the exact double of each float; NaN / infinity as null (the bridge writer's rule)
        static void Nums(JsonTextWriter w, params float[] v)
        {
            w.WriteStartArray();
            foreach (var f in v)
            {
                if (float.IsNaN(f) || float.IsInfinity(f)) w.WriteNull();
                else w.WriteValue((double)f);
            }
            w.WriteEndArray();
        }

        static BridgeException Unavailable(CharaRealization r, string message) => CommandKit.NotRealized(message, "key", r?.State.ToString() ?? "NotRealized");

        // ------------------------------------------------------------ face.dump

        static object FaceDump(CoreContext c)
        {
            var r = CommandKit.Built(c);
            return FaceDump(c.App, r, c.Args.Get("reflect", true), c.Frames.Frame);
        }

        /// <summary>The face.dump reply of a built character (also used by the tests).</summary>
        public static BridgeReply FaceDump(AppCore app, CharaRealization r, bool reflect, int frame)
        {
            var cc = r.Context;
            var head = cc?.Head ? cc.Head.transform : null;
            if (!head) throw Unavailable(r, "no head object");
            var rends = new List<object>();
            foreach (var s in head.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!s || UnderHolder(s.transform, head)) continue;
                var e = new BridgeReply
                {
                    ["name"] = s.name,
                    ["path"] = RelPath(s.transform, head),
                    ["enabled"] = s.enabled,
                    ["active"] = s.gameObject.activeInHierarchy,
                };
                var mesh = s.sharedMesh;
                e["mesh"] = mesh ? mesh.name : null;
                var names = new List<object>();
                var w = new List<object>();
                if (mesh)
                    for (int i = 0; i < mesh.blendShapeCount; i++)
                    {
                        names.Add(mesh.GetBlendShapeName(i));
                        w.Add((double)s.GetBlendShapeWeight(i));
                    }
                e["blendShapes"] = names;
                e["weights"] = w;
                var mats = new List<object>();
                foreach (var mt in s.sharedMaterials) mats.Add(MaterialEntry(mt));
                e["materials"] = mats;
                rends.Add(e);
            }
            var rep = new BridgeReply
            {
                ["head"] = head.name,
                ["renderers"] = rends,
                ["expression"] = RealizedExpression(app, r),
            };
            if (reflect)
            {
                rep["eyeLook"] = EyeLook(r.Look);
                rep["neckLook"] = NeckLook(r.Look);
            }
            rep["frame"] = frame;
            // HS2Next's addition after HS2's members
            if (reflect) rep["stages"] = Stages(r.Motion);
            return rep;
        }

        static bool UnderHolder(Transform t, Transform root)
        {
            for (var p = t; p && p != root; p = p.parent)
                if (SceneNodeHolder.IsHolder(p)) return true;
            return false;
        }

        // HS2's CmpRelPath: names below the root, '/'-separated ("" for the root itself)
        static string RelPath(Transform t, Transform root)
        {
            if (t == root) return "";
            string p = t.name;
            for (var c = t.parent; c && c != root; c = c.parent) p = c.name + "/" + p;
            return p;
        }

        static object MaterialEntry(Material mt)
        {
            if (!mt) return null;
#if UNITY_EDITOR
            // Editor property drawers may rewrite keywords on the first property access: keep them as they were.
            var keywords = mt.shaderKeywords;
            try
            {
#endif
                var md = new BridgeReply { ["name"] = mt.name, ["shader"] = mt.shader ? mt.shader.name : null };
                foreach (var p in FaceProperties)
                    if (mt.HasProperty(p)) md[p] = (double)mt.GetFloat(p);
                return md;
#if UNITY_EDITOR
            }
            finally
            {
                var now = mt.shaderKeywords;
                if (!SameKeywords(keywords, now)) mt.shaderKeywords = keywords;
            }
#endif
        }

        static bool SameKeywords(string[] a, string[] b)
        {
            if (a == null || b == null) return a == b;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        /// <summary>HS2's face.dump `expression`: the realized face state (CommandKit.FaceState order) and HS2's getters.</summary>
        public static BridgeReply RealizedExpression(AppCore app, CharaRealization r)
        {
            var x = CommandKit.FaceState(app, r.Node, realized: true);
            var ex = r.Expression;
            var s = ex?.Status ?? new FaceStatus();
            var look = r.Look;
            x["eyebrowPtn"] = s.EyebrowPtn;
            x["eyesPtn"] = s.EyesPtn;
            x["eyesOpenMax"] = (double)s.EyesOpenMax;
            x["eyebrowOpenMax"] = (double)s.EyebrowOpenMax;
            x["blinkFlag"] = s.EyesBlink;
            x["mouthPtn"] = s.MouthPtn;
            x["mouthOpenMax"] = (double)s.MouthOpenMax;
            x["mouthFixed"] = s.MouthFixed;
            x["tearsRate"] = (double)s.TearsRate;
            x["hohoAkaRate"] = (double)s.HohoAkaRate;
            x["lookEyesPtn"] = look ? look.EyePattern : 0;
            x["lookEyesRate"] = (double)(look?.Status?.eyesTargetRate ?? 1f);
            x["lookNeckPtn"] = look ? look.NeckPattern : 0;
            x["lookNeckRate"] = (double)(look ? look.NeckRate : 1f);
            x["studioMouthOpen"] = (double)(ex?.MouthOpen ?? 0f);
            return x;
        }

        // the subset of HS2's reflection dumps that CompareTest reads, under HS2's names
        static object EyeLook(CharaLook look)
        {
            if (!look || look.Eyes == null) return null;
            var eyes = new List<object>();
            int n = look.Eyes.Eyes?.Length ?? 0;
            for (int i = 0; i < n; i++)
            {
                var a = look.EyeAngle(i);
                eyes.Add(new BridgeReply { ["eyeLR"] = i == 0 ? "EYE_L" : "EYE_R", ["angleH"] = (double)a.x, ["angleV"] = (double)a.y });
            }
            return new BridgeReply
            {
                ["eyeLookScript"] = new BridgeReply { ["eyeObjs"] = eyes, ["nowPtnNo"] = look.Eyes.NowPtnNo },
                ["ptnNo"] = look.Eyes.PtnNo,
            };
        }

        static object NeckLook(CharaLook look)
        {
            if (!look || look.Neck == null) return null;
            var bones = new List<object>();
            var b = look.Neck.Bones;
            for (int i = 0; b != null && i < b.Length; i++)
            {
                var a = look.NeckBoneAngle(i);
                bones.Add(new BridgeReply { ["name"] = b[i]?.Name ?? "", ["angleH"] = (double)a.x, ["angleV"] = (double)a.y });
            }
            return new BridgeReply
            {
                ["neckLookScript"] = new BridgeReply { ["aBones"] = bones, ["nowPtnNo"] = look.Neck.NowPtnNo },
                ["ptnNo"] = look.Neck.PtnNo,
            };
        }

        // {<stage name>: {enabled, phase, ...}} in step order; look stages add their pattern numbers
        static object Stages(CharaMotion m)
        {
            var d = new BridgeReply();
            if (!m) return d;
            foreach (var s in m.Stages)
            {
                if (s == null || d.ContainsKey(s.Name)) continue;
                var e = new BridgeReply { ["enabled"] = s.Enabled, ["phase"] = (int)s.Phase };
                if (s is NeckLookStage nk) { e["ptnNo"] = nk.PtnNo; e["nowPtnNo"] = nk.NowPtnNo; e["rate"] = nk.Rate; }
                else if (s is EyeLookStage ey) { e["ptnNo"] = ey.PtnNo; e["nowPtnNo"] = ey.NowPtnNo; e["lookEnabled"] = ey.LookEnabled; }
                d[s.Name] = e;
            }
            return d;
        }

        // ------------------------------------------------------------ chara.bones

        static object CharaBones(CoreContext c)
        {
            var r = CommandKit.Built(c);
            var names = c.Args.Require<List<string>>("names");
            var cc = r.Context;
            var res = new BridgeReply();
            Dictionary<string, Transform> deep = null;
            foreach (var nm in names)
            {
                if (nm == null || res.ContainsKey(nm)) continue;
                Transform t = null;
                if (cc.BodyBones != null && cc.BodyBones.TryGetValue(nm, out var bt) && bt && !UnderHolder(bt, r.Root.transform)) t = bt;
                else if (cc.HeadBones != null && cc.HeadBones.TryGetValue(nm, out var ht) && ht && !UnderHolder(ht, r.Root.transform)) t = ht;
                else
                {
                    deep = deep ?? Index(r.Root.transform);
                    deep.TryGetValue(nm, out t);
                }
                if (!t) continue;
                res[nm] = new BridgeReply { ["pos"] = CommandKit.R4(t.position), ["forward"] = CommandKit.R4(t.forward) };
            }
            return new BridgeReply { ["bones"] = res, ["time"] = Time.time };
        }

        // first transform of each name, depth-first in child order under the character root, scene node holders skipped
        static Dictionary<string, Transform> Index(Transform root)
        {
            var d = new Dictionary<string, Transform>(StringComparer.Ordinal);
            var stack = new Stack<Transform>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var t = stack.Pop();
                if (!t) continue;
                if (t != root && SceneNodeHolder.IsHolder(t)) continue;
                if (!d.ContainsKey(t.name)) d[t.name] = t;
                for (int i = t.childCount - 1; i >= 0; i--) stack.Push(t.GetChild(i));
            }
            return d;
        }
    }

    public static partial class CoreCommands
    {
        static partial void RegisterDumps(ICommandRegistry r, AppCore app) => DumpCommands.Register(r, app);
    }
}
