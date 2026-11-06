// The test plugin of skin layers and body-part replacement (CC0-1.0). It does nothing until bodyprobe.config turns its
// parts on; then its BodyMesh hook sets a PNG layer and a texture layer on the body and replaces the body renderer with an
// owned copy of the character's own body mesh whose hand vertices are scaled by 1.05 around their bone (bones by name, an
// optional normals source), and with checks on it tries the refusals: an unknown part, a mesh with one bone more and no
// names, a bone name that does not bind, bytes that are no image, removing an id it never set; its Textures and Final hooks
// try the BodyMesh points at their stage. bodyprobe.layer sets and removes layers outside builds, bodyprobe.rebuild asks for
// a rebuild. Replies are ids and numbers only; the mesh copies are shared with in-process tests through the AppDomain data
// "stp3.test.bodyprobe.meshes" (node id -> mesh). Unload destroys what the plugin made.
using System;
using System.Collections.Generic;
using System.Linq;
using HS2Next.Api.Bridge;
using HS2Next.Api.Plugins;
using HS2Next.Api.Unity;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Stp3.Test.BodyProbe
{
    public sealed class BodyProbePlugin : IPlugin
    {
        /// <summary>A 32 x 32 RGBA PNG: transparent with one small opaque dark mark.</summary>
        const string Mark = "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAAOElEQVR42u3TMQ0AMAwEsYcTEOVPK4XQsUrk4XYvl6qTnwVgIqAfAewHuAAAAAAAAAAAAABgF+ACZipP/3LKDa0AAAAASUVORK5CYII=";
        const float HandScale = 1.05f;
        const string MeshesKey = "stp3.test.bodyprobe.meshes";

        IPluginContext ctx;
        IUnityPluginServices u;
        byte[] png;
        Texture2D tex;

        bool usePng, useTexture, useBody, useChecks;
        string normals = "keep";
        string fkBone;
        bool supplyMaterials;
        int bodyMeshRuns, texturesRuns, finalRuns;
        readonly Dictionary<string, int> builds = new Dictionary<string, int>(StringComparer.Ordinal), rebuilds = new Dictionary<string, int>(StringComparer.Ordinal);
        readonly Dictionary<string, Dictionary<string, object>> last = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
        readonly Dictionary<string, Mesh> copies = new Dictionary<string, Mesh>(StringComparer.Ordinal);
        readonly List<string> stageChecks = new List<string>();

        public void Load(IPluginContext context)
        {
            ctx = context;
            u = context.Unity();
            png = Convert.FromBase64String(Mark);
            tex = new Texture2D(32, 32, TextureFormat.RGBA32, false) { name = "bodyprobe mark", hideFlags = HideFlags.DontSave };
            var px = new Color32[32 * 32];
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                    px[y * 32 + x] = x >= 4 && x < 8 && y >= 20 && y < 24 ? new Color32(30, 50, 40, 255) : new Color32(0, 0, 0, 0);
            tex.SetPixels32(px);
            tex.Apply(false, false);
            AppDomain.CurrentDomain.SetData(MeshesKey, new Dictionary<string, Mesh>(StringComparer.Ordinal));

            var c = context.Commands;
            c.Register(new CommandSpec("bodyprobe.config", "Turns the probe's parts on or off (all off at load).")
                .Arg("png", ArgTypes.Bool, "a PNG layer (BodyOver, id mark)", defaultValue: false)
                .Arg("texture", ArgTypes.Bool, "a texture layer (BodyOver, id tex)", defaultValue: false)
                .Arg("body", ArgTypes.Bool, "replace the body renderer with the hand-scaled copy", defaultValue: false)
                .Arg("normals", ArgTypes.String, "keep, asset (the game's body normals) or none", defaultValue: "keep")
                .Arg("checks", ArgTypes.Bool, "try the refusals and the stage rules", defaultValue: false)
                .Arg("materials", ArgTypes.Bool, "the replacement also supplies the body renderer's own materials", defaultValue: false)
                .Arg("fkBone", ArgTypes.String, "a body bone the probe's FK node row names (null: no row)"), Config);
            c.Register(new CommandSpec("bodyprobe.state", "What the probe's hooks did (numbers and ids).").WithFlags(CommandFlags.ReadOnly), _ => State());
            c.Register(new CommandSpec("bodyprobe.layer", "Sets, removes or lists the probe's skin layers on a built character outside builds.")
                .Arg("node", ArgTypes.String, "the character's node id", required: true)
                .Arg("op", ArgTypes.String, "set, remove or ids", defaultValue: "ids")
                .Arg("layer", ArgTypes.String, "the layer id", defaultValue: "outside")
                .Arg("kind", ArgTypes.Int, "the SkinLayerKind number", defaultValue: 1)
                .Arg("form", ArgTypes.String, "png, texture, gone (a texture destroyed right after the set) or bad (bytes that are no image)", defaultValue: "png"), Layer);
            c.Register(new CommandSpec("bodyprobe.rebuild", "Asks for a rebuild of a character at the next apply (times requests in this call).")
                .Arg("node", ArgTypes.String, "the character's node id", required: true)
                .Arg("times", ArgTypes.Int, "how many requests", defaultValue: 1), Rebuild);

            var hooks = u.BuildHooks;
            hooks.Register("parts", BuildStage.BodyMesh, BodyMesh);
            hooks.Register("late", BuildStage.Textures, a => { texturesRuns++; if (useChecks) TryOutside(a); });
            hooks.Register("final", BuildStage.Final, a => { finalRuns++; if (useChecks) TryOutside(a); });
            hooks.RegisterFkNodes("rows", () => fkBone == null ? new FkNodeRow[0] : new[] { new FkNodeRow(19901, fkBone, 5, 1) });
            context.Events.CharaBuilt += i =>
            {
                var d = i.Rebuild ? rebuilds : builds;
                d.TryGetValue(i.NodeId ?? "", out int n);
                d[i.NodeId ?? ""] = n + 1;
            };
        }

        public void Unload()
        {
            // the host removes the hooks, commands, handlers and the layers set outside builds; what the plugin made goes here
            foreach (var m in copies.Values) Drop(m);
            copies.Clear();
            Drop(tex);
            AppDomain.CurrentDomain.SetData(MeshesKey, null);
        }

        object Config(ICommandContext c)
        {
            usePng = c.Args.Get("png", false);
            useTexture = c.Args.Get("texture", false);
            useBody = c.Args.Get("body", false);
            useChecks = c.Args.Get("checks", false);
            normals = c.Args.Get("normals", "keep");
            supplyMaterials = c.Args.Get("materials", false);
            fkBone = c.Args.Get<string>("fkBone", null);
            bodyMeshRuns = texturesRuns = finalRuns = 0;
            builds.Clear();
            rebuilds.Clear();
            last.Clear();
            stageChecks.Clear();
            return new Dictionary<string, object> { ["png"] = usePng, ["texture"] = useTexture, ["body"] = useBody, ["normals"] = normals, ["checks"] = useChecks };
        }

        Dictionary<string, object> State() => new Dictionary<string, object>
        {
            ["bodyMeshRuns"] = bodyMeshRuns,
            ["texturesRuns"] = texturesRuns,
            ["finalRuns"] = finalRuns,
            ["builds"] = new Dictionary<string, object>(builds.ToDictionary(k => k.Key, k => (object)k.Value)),
            ["rebuilds"] = new Dictionary<string, object>(rebuilds.ToDictionary(k => k.Key, k => (object)k.Value)),
            ["last"] = last.ToDictionary(k => k.Key, k => (object)k.Value),
            ["stageChecks"] = stageChecks.ToArray(),
        };

        void BodyMesh(IBuildHookArgs a)
        {
            bodyMeshRuns++;
            string node = a.NodeId ?? "";
            var r = new Dictionary<string, object> { ["male"] = a.IsMale, ["parts"] = a.BodyParts.ToArray() };
            last[node] = r;
            if (usePng) r["png"] = a.SetSkinLayer("mark", SkinLayerKind.BodyOver, png);
            if (useTexture) r["texture"] = a.SetSkinLayer("tex", SkinLayerKind.BodyOver, tex);
            string bodyName = a.IsMale ? "o_body_cm" : "o_body_cf";
            var smr = a.Renderers.OfType<SkinnedMeshRenderer>().FirstOrDefault(x => x.name == bodyName);
            if ((useBody || useChecks) && smr && smr.sharedMesh)
            {
                var prefab = smr.sharedMesh;
                if (useChecks)
                {
                    var extra = Object.Instantiate(prefab);
                    var bind = extra.bindposes.ToList();
                    bind.Add(Matrix4x4.identity);
                    extra.bindposes = bind.ToArray();
                    r["extraBone"] = a.ReplaceBodyPart(bodyName, new BodyPart { Mesh = extra }).ToString();
                    var names = smr.bones.Select(b => b ? b.name : null).ToArray();
                    names[0] = "probe_no_such_bone";
                    r["unknownBone"] = a.ReplaceBodyPart(bodyName, new BodyPart { Mesh = prefab, BoneNames = names }).ToString();
                    r["unchanged"] = smr.sharedMesh == prefab;
                    Drop(extra);
                    r["nosuch"] = a.ReplaceBodyPart("nosuch", new BodyPart { Mesh = prefab }).ToString();
                    r["badBytes"] = a.SetSkinLayer("bad", SkinLayerKind.BodyOver, new byte[32]);
                    r["removeOther"] = a.RemoveSkinLayer("never-set");
                    bool nullThrew = false;
                    try { a.ReplaceBodyPart(bodyName, null); } catch (ArgumentNullException) { nullThrew = true; }
                    r["nullThrew"] = nullThrew;
                }
                if (useBody)
                {
                    var copy = HandScaled(smr, out int scaled);
                    if (copies.TryGetValue(node, out var old)) Drop(old);
                    copies[node] = copy;
                    if (AppDomain.CurrentDomain.GetData(MeshesKey) is Dictionary<string, Mesh> shared) shared[node] = copy;
                    var part = new BodyPart { Mesh = copy, BoneNames = smr.bones.Select(b => b ? b.name : null).ToArray() };
                    if (supplyMaterials) part.Materials = smr.sharedMaterials;
                    if (normals == "asset") part.Normals = BodyNormals.FromAsset(null, "chara/oo_base.unity3d", "p_cf_body_00_Nml");
                    else if (normals == "none") part.Normals = BodyNormals.None;
                    r["replace"] = a.ReplaceBodyPart(bodyName, part).ToString();
                    r["scaled"] = scaled;
                    r["vertices"] = copy.vertexCount;
                }
            }
            r["layers"] = a.SkinLayers.ToArray();
            a.Log("bodyprobe: " + string.Join(", ", r.Keys));
        }

        /// <summary>The BodyMesh points at another stage: each must throw InvalidOperationException.</summary>
        void TryOutside(IBuildHookArgs a)
        {
            string stage = a.Stage.ToString();
            bool Throws(Action f)
            {
                try { f(); return false; }
                catch (InvalidOperationException) { return true; }
            }
            if (Throws(() => a.SetSkinLayer("mark", SkinLayerKind.BodyOver, png))) stageChecks.Add(stage + " SetSkinLayer");
            if (Throws(() => a.RemoveSkinLayer("mark"))) stageChecks.Add(stage + " RemoveSkinLayer");
            if (Throws(() => a.ReplaceBodyPart("o_body_cf", new BodyPart { Mesh = null }))) stageChecks.Add(stage + " ReplaceBodyPart");
        }

        /// <summary>An owned copy of the renderer's mesh with every vertex whose largest weight is a hand bone (cf_J_Hand*)
        /// scaled by 1.05 around that bone's bind position.</summary>
        static Mesh HandScaled(SkinnedMeshRenderer smr, out int scaled)
        {
            var src = smr.sharedMesh;
            var copy = Object.Instantiate(src);
            copy.name = src.name;
            var bones = smr.bones;
            var bind = src.bindposes;
            var hand = new bool[bind.Length];
            var centre = new Vector3[bind.Length];
            for (int b = 0; b < bind.Length; b++)
            {
                hand[b] = b < bones.Length && bones[b] && bones[b].name.StartsWith("cf_J_Hand", StringComparison.Ordinal);
                centre[b] = bind[b].inverse.MultiplyPoint3x4(Vector3.zero);
            }
            var v = src.vertices;
            var w = src.boneWeights;
            scaled = 0;
            for (int i = 0; i < v.Length && i < w.Length; i++)
            {
                int b = Dominant(w[i]);
                if (b < 0 || b >= hand.Length || !hand[b]) continue;
                v[i] = centre[b] + (v[i] - centre[b]) * HandScale;
                scaled++;
            }
            copy.vertices = v;
            return copy;
        }

        static void Drop(Object o)
        {
            if (!o) return;
            if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o);
        }

        static int Dominant(BoneWeight w)
        {
            int b = w.boneIndex0;
            float m = w.weight0;
            if (w.weight1 > m) { m = w.weight1; b = w.boneIndex1; }
            if (w.weight2 > m) { m = w.weight2; b = w.boneIndex2; }
            if (w.weight3 > m) { b = w.boneIndex3; }
            return b;
        }

        object Rebuild(ICommandContext c)
        {
            string node = c.Args.Get("node", "");
            var queued = new List<object>();
            for (int i = 0; i < Math.Max(1, c.Args.Get("times", 1)); i++) queued.Add(u.RequestRebuild(node));
            return new Dictionary<string, object> { ["queued"] = queued[0], ["each"] = queued.ToArray() };
        }

        object Layer(ICommandContext c)
        {
            string node = c.Args.Get("node", "");
            var layers = u.SkinLayers(node);
            if (layers == null) return new Dictionary<string, object> { ["available"] = false };
            string op = c.Args.Get("op", "ids"), id = c.Args.Get("layer", "outside"), form = c.Args.Get("form", "png");
            var kind = (SkinLayerKind)c.Args.Get("kind", 1);
            object result = null;
            if (op == "set" && form == "gone")
            {
                // a texture of the probe's that it destroys right after the set (the host skips it at the composite)
                var t = new Texture2D(8, 8, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave };
                result = layers.Set(id, kind, t);
                Drop(t);
            }
            else if (op == "set") result = form == "texture" ? layers.Set(id, kind, tex) : layers.Set(id, kind, form == "bad" ? new byte[32] : png);
            else if (op == "remove") result = layers.Remove(id);
            return new Dictionary<string, object> { ["available"] = true, ["result"] = result, ["ids"] = layers.Ids.ToArray() };
        }
    }
}
