// StudioOps, scene and files area: map load and pose /
// option / light flags (map.set's validation and labels), screen effects sets and block resets (effects.set /
// effects.reset), sky, title; new / load / save, and Import scene (U28): the nodes of another scene file added at the root
// with new ids for every node and route point (parents inside the imported subtree remapped; the other file's `scene`
// settings ignored as in HS2), one entry "Import scene", the imported top nodes selected. Every node brings the images its
// document names (item panels, patterns, material edits), a character also those of its documents, and the file's constraints
// between imported nodes come along.
using System;
using System.Collections.Generic;
using System.IO;
using HS2Next.Save;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    public sealed partial class StudioOps
    {
        // ------------------------------------------------------------ map

        /// <summary>
        /// Loads a map (`map.ref` + a reset pose, one entry "Map"); null clears it. Re-selecting the loaded map loads nothing and
        /// keeps its pose (M3): Value = true then (nothing recorded).
        /// </summary>
        public OpResult LoadMap(string mapRef, ExecuteOptions o = default)
        {
            bool reselect = mapRef != null && string.Equals(mapRef, Model.Settings.Map.Ref, StringComparison.Ordinal);
            var r = reselect ? OpResult.NothingToDo() : OpResult.From(Session.SetMap(mapRef, Labelled(o, Labels.Map)));
            rt.ApplyNow();
            r.Value = reselect;
            return r;
        }

        static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);

        /// <summary>
        /// The map's pose and its option / light flags in one entry (label: the caller's, else "Map pose" when a pose member is
        /// given, "Map options", "Map lights"). Changed / NoOp list the member names (pos, rot, option, light), in that order.
        /// </summary>
        public OpResult SetMap(Vector3? pos, Vector3? rot, bool? option, bool? light, ExecuteOptions o = default)
        {
            if (pos == null && rot == null && option == null && light == null) return OpResult.Fail(CommandCodes.BadArgs, "missing argument 'pos'", "pos", "one of pos, rot, option, light");
            if (pos != null && !Finite(pos.Value)) return OpResult.BadArgs("pos", "vec3", "finite numbers");
            if (rot != null && !Finite(rot.Value)) return OpResult.BadArgs("rot", "euler", "finite numbers");
            string label = pos != null || rot != null ? Labels.MapPose : option != null ? Labels.MapOptions : Labels.MapLights;
            var r = OpResult.NothingToDo();
            rt.ApplyNow();
            var tx = Open(label, o, out var fail);
            if (tx == null) return fail;
            using (tx)
            {
                foreach (var (has, name, path, value) in new (bool, string, string, object)[]
                         {
                             (pos != null, "pos", "map.pos", pos != null ? UnityObjects.V(pos.Value) : default),
                             (rot != null, "rot", "map.rot", rot != null ? UnityObjects.V(rot.Value) : default),
                             (option != null, "option", "map.options", option ?? false),
                             (light != null, "light", "map.lights", light ?? false),
                         })
                {
                    if (!has) continue;
                    var s = tx.Set(null, path, value);
                    if (s.Status == CommandStatus.Error) return OpResult.From(s, name);
                    (s.Status == CommandStatus.NoOp ? r.NoOp : r.Changed).Add(name);
                }
                r = Commit(tx, r);
            }
            rt.ApplyNow();
            return r;
        }

        // ------------------------------------------------------------ effects

        /// <summary>A block of the screen effects with ScreenEffectDefine's defaults (a fresh document object), or null.</summary>
        public static DocObject DefaultEffectBlock(string name)
        {
            var d = new ScreenEffects();
            switch (name)
            {
                case "colorGrading": return d.ColorGrading;
                case "ambientOcclusion": return d.AmbientOcclusion;
                case "bloom": return d.Bloom;
                case "depthOfField": return d.DepthOfField;
                case "vignette": return d.Vignette;
                case "ssr": return d.Ssr;
                case "reflectionProbe": return d.ReflectionProbe;
                case "fog": return d.Fog;
                case "sunShafts": return d.SunShafts;
                case "shadows": return d.Shadows;
                case "ambient": return d.Ambient;
                case "sky": return d.Sky;
                default: return null;
            }
        }

        /// <summary>
        /// Screen-effect values (canonical `effects.` paths under a block) in one entry "Effects"; Changed / NoOp per set, in plan
        /// order (effects.set).
        /// </summary>
        public OpResult SetEffects(IList<(string path, object value)> plan, ExecuteOptions o = default)
        {
            if (plan == null || plan.Count == 0) return OpResult.BadArgs("set", "an object", "set names no field");
            foreach (var (path, _) in plan)
            {
                string block = path != null && path.StartsWith("effects.", StringComparison.Ordinal) ? SceneEffects.SecondSegment(path) : null;
                if (block == null || SceneEffects.BlockIndex(block) < 0)
                    return OpResult.BadArgs("set." + path, "one of " + string.Join(", ", SceneEffects.BlockNames), "\"" + path + "\" names no effect block");
            }
            return SetMany(new string[] { null }, plan, Labels.Effects, o, p => "set." + p.Substring(8), distinct: false);
        }

        /// <summary>Resets blocks to their defaults (all when null), one entry "Reset effects"; Changed = the blocks that changed.</summary>
        public OpResult ResetEffects(IEnumerable<string> blocks = null, ExecuteOptions o = default)
        {
            var list = new List<string>();
            foreach (var b in blocks ?? SceneEffects.BlockNames)
            {
                if (SceneEffects.BlockIndex(b) < 0) return OpResult.BadArgs("blocks", "one of " + string.Join(", ", SceneEffects.BlockNames), "\"" + b + "\" is not an effect block");
                if (!list.Contains(b)) list.Add(b);
            }
            var r = OpResult.NothingToDo();
            rt.ApplyNow();
            var tx = Open(Labels.ResetEffects, o, out var fail);
            if (tx == null) return fail;
            using (tx)
            {
                foreach (var b in list)
                {
                    var s = tx.Set(null, "effects." + b, DefaultEffectBlock(b));
                    if (s.Status == CommandStatus.Error) return OpResult.From(s, "blocks");
                    if (s.Status != CommandStatus.NoOp) r.Changed.Add(b); else r.NoOp.Add(b);
                }
                r = Commit(tx, r);
            }
            rt.ApplyNow();
            return r;
        }

        /// <summary>The sky block's enabled flag and pattern (a content ref), one entry "Sky".</summary>
        public OpResult SetSky(bool? enabled, string patternRef = null, ExecuteOptions o = default)
        {
            var plan = new List<(string, object)>();
            if (enabled != null) plan.Add(("effects.sky.enabled", enabled.Value));
            if (patternRef != null) plan.Add(("effects.sky.pattern", patternRef));
            if (plan.Count == 0) return OpResult.NothingToDo();
            return SetMany(new string[] { null }, plan, Labels.Sky, o, p => p.EndsWith("enabled", StringComparison.Ordinal) ? "enabled" : "pattern");
        }

        /// <summary>The scene title (manifest), one entry "Title".</summary>
        public OpResult SetTitle(string title, ExecuteOptions o = default)
        {
            var r = OpResult.From(Session.Set(null, "title", title ?? "", Labelled(o, Labels.Title)), "title");
            return r;
        }

        // ------------------------------------------------------------ files

        /// <summary>An empty scene (history cleared, not recorded).</summary>
        public OpResult NewScene()
        {
            Session.NewScene();
            rt.ApplyNow();
            return OpResult.Success();
        }

        /// <summary>Loads a scene file (history cleared); Value = the LoadResult. A failed load leaves the scene as it was.</summary>
        public OpResult LoadScene(string path, LoadOptions options = null)
        {
            var lr = Scene.Load(path, options);
            var r = lr.Ok ? OpResult.Success() : OpResult.BadArgs("path", null, "scene not readable (" + Path.GetFileName(path ?? "") + "): " + lr.Error?.Message);
            r.Value = lr;
            return r;
        }

        /// <summary>Saves the scene; Value = the SaveResult (not recorded).</summary>
        public OpResult SaveScene(string path, SaveOptions options = null)
        {
            var sr = Scene.Save(path, options);
            var r = sr.Ok ? OpResult.Success() : OpResult.BadArgs("path", null, "save failed: " + sr.Error?.Message);
            r.Value = sr;
            return r;
        }

        /// <summary>
        /// Import scene (U28): the nodes of the scene file at <paramref name="path"/> added at the root (new ids for nodes and route
        /// points, parents inside the imported subtree remapped, character documents and every node's resources copied, the file's
        /// scene settings ignored), one entry "Import scene"; the imported top nodes become the selection (NewIds). The file's constraints with
        /// both ends among the imported nodes follow, ids remapped; the others stay behind, counted in a warning.
        /// Value = the file's load findings (FindingList).
        /// </summary>
        public OpResult ImportScene(string path, ExecuteOptions o = default)
        {
            LoadResult<SceneContainer> lr;
            try { lr = SceneContainer.Load(path); }
            catch (Exception e) { return OpResult.BadArgs("path", null, "scene not readable: " + e.GetType().Name + ": " + e.Message); }
            if (!lr.Ok) return OpResult.BadArgs("path", null, "scene not readable (" + Path.GetFileName(path ?? "") + "): " + lr.Error?.Message);
            var file = lr.Value;
            var docs = file.Scene.Nodes;
            var byParent = new Dictionary<string, List<SceneNode>>(StringComparer.Ordinal);
            var known = new HashSet<string>(StringComparer.Ordinal);
            foreach (var d in docs) if (d?.Id != null) known.Add(d.Id);
            var tops = new List<SceneNode>();
            foreach (var d in docs)
            {
                if (d?.Id == null) continue;
                string p = d.Parent?.Node;
                if (p == null || !known.Contains(p)) { tops.Add(d); continue; }
                if (!byParent.TryGetValue(p, out var l)) byParent[p] = l = new List<SceneNode>();
                l.Add(d);
            }
            int Order(SceneNode n) => n.Order;
            tops.Sort((a, b) => Order(a).CompareTo(Order(b)));
            foreach (var l in byParent.Values) l.Sort((a, b) => Order(a).CompareTo(Order(b)));
            var imported = file.Scene.Constraints != null ? new Dictionary<string, string>(StringComparer.Ordinal) : null;
            NodeSpec Spec(SceneNode d)
            {
                var doc = DocClone.Clone(d);
                doc.Id = Session.NewId();
                doc.Parent = null;
                if (imported != null) imported[d.Id] = doc.Id;
                if (doc.State is RouteNodeState route) foreach (var pt in route.Points) pt.Id = Session.NewId();
                if (doc.State is CameraNodeState cam) cam.Active = false;
                var spec = new NodeSpec { Doc = doc, Point = d.Parent?.Point };
                var images = new HashSet<string>(StringComparer.Ordinal);
                ResStore.ScanReferences(CanonicalJsonWriter.ToBytes(SceneDocument.EntryName, d.WriteTo), images);
                Model.File.Res.CopyFrom(file.Res, images);
                if (file.NodeExt.TryGetValue(d.Id, out var ext) && ext != null) spec.FileExt = ext.Clone();
                if (d.Kind == SceneNode.KindChara && file.Charas.TryGetValue(d.Id, out var t))
                {
                    var copy = t.Clone();
                    copy.ManifestCharaUnknown = null;
                    Model.File.Res.CopyFrom(file.Res, copy.ReferencedResources());
                    spec.Appearance = new CharaAppearance { Stored = copy };
                }
                if (byParent.TryGetValue(d.Id, out var kids))
                {
                    spec.Children = new NodeSpec[kids.Count];
                    for (int i = 0; i < kids.Count; i++)
                    {
                        spec.Children[i] = Spec(kids[i]);
                        spec.Children[i].Point = kids[i].Parent?.Point;
                        spec.Children[i].Index = i;
                    }
                }
                return spec;
            }
            var specs = new List<NodeSpec>();
            try
            {
                foreach (var d in tops)
                {
                    var s = Spec(d);
                    s.Point = null;
                    specs.Add(s);
                }
            }
            catch (Exception e) { return OpResult.BadArgs("path", null, "cannot import: " + e.GetType().Name + ": " + e.Message); }
            if (specs.Count == 0)
            {
                var empty = OpResult.NothingToDo();
                empty.Value = lr.Findings;
                return empty;
            }
            rt.ApplyNow();
            var tx = Open(Labels.ImportScene, o, out var fail);
            if (tx == null) return fail;
            OpResult r;
            int outside, refused;
            using (tx)
            {
                var o2 = Labelled(o, Labels.ImportScene);
                o2.Record = HistoryMode.Record;
                var res = Session.AddNodes(specs, o2);
                if (res.Status == CommandStatus.Error) return OpResult.From(res, "path");
                r = OpResult.From(res);
                var carried = Session.CarryConstraints(file.Scene.Constraints, imported, out outside, out refused);
                if (carried != null)
                {
                    var edit = tx.Execute(ConstraintEditCommand.ReplaceList(carried, Labels.ImportScene));
                    if (edit.Status == CommandStatus.Error) return OpResult.From(edit, "path");
                }
                var newIds = new List<string>();
                foreach (var s in specs) newIds.Add(s.Doc.Id);
                r.NewIds = newIds;
                r = Commit(tx, r);
                if (r.Ok) r.NewIds = newIds;
            }
            if (r.Ok)
            {
                Session.Selection.Set(r.NewIds);
                rt.ApplyNow();
                if (outside + refused > 0) r.Warnings.Add((outside + refused) + " constraints of the file left out: an end is not among the imported nodes, the list is full, or they cannot act");
            }
            r.Value = lr.Findings;
            return r;
        }
    }
}
