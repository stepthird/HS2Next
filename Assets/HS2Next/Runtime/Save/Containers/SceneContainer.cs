// .hs2nscene: scene.json, one character subtree per
// character node under charas/<nodeId>/ (no nested ZIPs), node-scope ext files under
// nodes/<nodeId>/ext/, scene-scope ext under ext/, and one res/ shared by everything. Character
// provenance lives in manifest.charas[<nodeId>].import. Subtrees without a character node and node
// ext data without a node are orphans: reported at load, dropped at save.
using System;
using System.Collections.Generic;
using System.IO;
using HS2Next.Api.Save;

namespace HS2Next.Save
{
    public sealed class SceneContainer : ContainerBase
    {
        /// <summary>The item group of the character accessories offered as Studio items (an item node of it stores the
        /// accessory row's reference with this group and the accessory category).</summary>
        public const int AccessoryItemGroup = 647158;

        /// <summary>The named requirement a scene with an item of <see cref="AccessoryItemGroup"/> lists in `requires` (an older
        /// reader would resolve such a reference against the item list).</summary>
        public const string AccessoryItemsRequirement = "hs2next.accessoryItems";

        public SceneDocument Scene = new SceneDocument();
        /// <summary>Scene title (manifest.title, authoritative for scenes).</summary>
        public string Title = "";
        public ExtScopeData SceneExt = new ExtScopeData(ExtScope.Scene);
        /// <summary>File-backed node-scope ext data by node id.</summary>
        public readonly Dictionary<string, ExtScopeData> NodeExt = new Dictionary<string, ExtScopeData>(StringComparer.Ordinal);
        /// <summary>Character subtrees by character node id.</summary>
        public readonly Dictionary<string, CharaSubtree> Charas = new Dictionary<string, CharaSubtree>(StringComparer.Ordinal);

        public override string Format => SaveFormats.Scene;
        internal override string MainDocument => SceneDocument.EntryName;
        internal override string ManifestTitle => Title;
        internal override int? ManifestSex => null;

        public static string CharaRoot(string nodeId) => "charas/" + nodeId + "/";
        public static string NodeRoot(string nodeId) => "nodes/" + nodeId + "/";

        public static LoadResult<SceneContainer> Load(string path, LoadOptions options = null) =>
            Run(() => ContainerReader.Read(path), options, SaveFormats.Scene, Bind);

        public static LoadResult<SceneContainer> Load(byte[] bytes, LoadOptions options = null) =>
            Run(() => ContainerReader.Read(bytes), options, SaveFormats.Scene, Bind);

        public static LoadResult<SceneContainer> Load(Stream stream, LoadOptions options = null) =>
            Run(() => ContainerReader.Read(stream), options, SaveFormats.Scene, Bind);

        static bool IsOpaque(SceneNode n) => n.State is OpaqueNode;

        static SceneContainer Bind(LoadSession s)
        {
            s.Doc(SceneDocument.EntryName, true);
            // Every charas/<id>/ subtree present (its node is only known after binding; migrations need all documents).
            var subtreeIds = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var e in s.Raw.Entries)
            {
                if (!e.Name.StartsWith("charas/", StringComparison.Ordinal) || !e.Name.EndsWith("/" + CharaDocument.EntryName, StringComparison.Ordinal)) continue;
                string id = e.Name.Substring(7, e.Name.Length - 7 - CharaDocument.EntryName.Length - 1);
                if (id.Length > 0 && id.IndexOf('/') < 0) subtreeIds.Add(id);
            }
            foreach (var id in subtreeIds) CharaSubtree.ParseDocs(s, CharaRoot(id), false);
            s.Prepare();

            var c = new SceneContainer
            {
                Scene = SceneDocument.Read(s.Reader(SceneDocument.EntryName)),
                Title = s.Manifest.Title ?? "",
            };
            c.SceneExt = s.BuildScope(ExtScope.Scene, "scene", "", "", c.Scene.Ext, null);
            foreach (var n in c.Scene.Nodes)
                c.NodeExt[n.Id] = s.BuildScope(ExtScope.Node, "node", "", NodeRoot(n.Id), n.Ext, null);
            foreach (var n in c.Scene.Nodes)
            {
                if (n.Kind != SceneNode.KindChara) continue;
                s.Manifest.Charas.TryGetValue(n.Id, out var mc);
                if (!subtreeIds.Contains(n.Id))
                {
                    s.Findings.Error("scene.charaMissing", "nodes:" + n.Id, "character node without its charas/" + n.Id + "/ subtree; kept, not built");
                    continue;
                }
                var t = CharaSubtree.Bind(s, CharaRoot(n.Id), mc?.Import);
                t.ManifestCharaUnknown = mc?.Unknown;
                c.Charas[n.Id] = t;
            }
            foreach (var id in subtreeIds)
                if (!c.Charas.ContainsKey(id))
                    s.Findings.Warning("scene.orphanSubtree", CharaRoot(id), "character subtree without a character node; dropped on save");
            c.TakeCommon(s);
            s.Dispatch(c.Instances());
            s.Finish(c, ContainerAreas.IsCore);
            return c;
        }

        /// <summary>Dispatcher order: scene, nodes in array order, then per character subtree in node order its chara and coord scopes.</summary>
        IEnumerable<ExtInstance> Instances()
        {
            yield return new ExtInstance { Scope = ExtScope.Scene, ContainerFormat = Format, Root = "", Data = SceneExt, Main = Scene.Ext };
            foreach (var n in Scene.Nodes)
            {
                if (IsOpaque(n)) continue;   // unknown kind: kept as is, not built
                if (!NodeExt.TryGetValue(n.Id, out var d)) NodeExt[n.Id] = d = new ExtScopeData(ExtScope.Node);
                yield return new ExtInstance { Scope = ExtScope.Node, OwnerId = n.Id, ContainerFormat = Format, Root = "", Data = d, Main = n.Ext };
            }
            foreach (var n in Scene.Nodes)
            {
                if (n.Kind != SceneNode.KindChara || !Charas.TryGetValue(n.Id, out var t)) continue;
                foreach (var i in t.Instances(Format, CharaRoot(n.Id), n.Id)) yield return i;
            }
        }

        internal override void Build(SaveSession s)
        {
            ExtDispatcher.Write(Instances(), s.Options.RegistryOrDefault, s.Findings);
            foreach (var n in Scene.Nodes) s.Covers(n);
            s.Covers(Scene.Timeline);
            s.Covers(Scene.Constraints);
            s.Covers(Scene.Scene.Map);
            s.AddDoc(SceneDocument.EntryName, Scene.Write);
            foreach (var n in Scene.Nodes)
                if (n.State is ItemNodeState item && item.Group == AccessoryItemGroup) { s.AddRequirement(AccessoryItemsRequirement); break; }
            s.AddExt(SceneExt, "scene", "", "", Scene.Ext, null);
            foreach (var n in Scene.Nodes)
            {
                if (NodeExt.TryGetValue(n.Id, out var d)) s.AddExt(d, "node", "", NodeRoot(n.Id), n.Ext, null);
                else if (!n.Ext.IsEmpty) s.AddExt(new ExtScopeData(ExtScope.Node), "node", "", NodeRoot(n.Id), n.Ext, null);
            }
            foreach (var n in Scene.Nodes)
            {
                if (n.Kind != SceneNode.KindChara || !Charas.TryGetValue(n.Id, out var t)) continue;
                t.Save(s, CharaRoot(n.Id));
                s.Charas[n.Id] = new ManifestChara { Import = t.Import, Unknown = t.ManifestCharaUnknown };
            }
        }

        // ---------- moving characters between files ----------

        /// <summary>
        /// Puts a copy of <paramref name="src"/>'s character subtree under character node <paramref name="nodeId"/>
        /// (the node itself is the scene model's business) and copies the resources it references. The import
        /// provenance moves to manifest.charas[nodeId]; ext data is re-rooted with the subtree.
        /// </summary>
        public void PlaceCharacter(string nodeId, CharaContainer src)
        {
            if (!JsonText.IsId(nodeId)) throw new ArgumentException("not a node id: " + nodeId, nameof(nodeId));
            var t = src.Subtree.Clone();
            t.ManifestCharaUnknown = null;
            Charas[nodeId] = t;
            Res.CopyFrom(src.Res, t.ReferencedResources());
        }

        /// <summary>The character subtree of node <paramref name="nodeId"/> as a character file (provenance and ext data included), or null.</summary>
        public CharaContainer ExtractCharacter(string nodeId)
        {
            if (!Charas.TryGetValue(nodeId, out var t)) return null;
            var c = new CharaContainer { Subtree = t.Clone() };
            c.Subtree.ManifestCharaUnknown = null;
            c.Res.CopyFrom(Res, c.Subtree.ReferencedResources());
            return c;
        }
    }
}
