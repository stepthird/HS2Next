// Operations that need Unity before or around a bus operation; each one is still
// one history entry:
//   AddCharaFromCard   card source (file read once, hashed, parsed) → the runtime's importer (ImportSetup.ImporterOf:
//                      the registered one, else the catalog's) → session.AddCharaFromContainer,
//                      import findings with the node id → ApplyNow; a failed import fails the op and adds nothing
//                      (Phase 4.6: no card-backed fallback)
//   AddCharaFromFile   .hs2nchara → session.AddCharaFromContainer → ApplyNow
//   AddItem            row → prefab → ItemDefaults.FromPrefab (the full initial state) → session.AddItem → ApplyNow
//   AddLight           session.AddLight(ref) → ApplyNow
//   MoveKeepWorld      ApplyNow, the destination transform that keeps the world pose, session.Move
//   Delete, Duplicate  ApplyNow, capture of the affected subtrees, the session operation, ApplyNow
//   Save, ToBytes      ApplyNow, capture, the scene thumbnail renderer when a graphics device exists, the app's
//                      save options (AppSaveOptions: app.version, named requires), session
//   Load               session.Load (→ Loaded → RealizeAll), ApplyNow
using System;
using System.Collections.Generic;
using System.IO;
using HS2Next.Content;
using HS2Next.Content.Studio;
using HS2Next.Save;
using UnityEngine;
using UnityEngine.Rendering;

namespace HS2Next.Scenes.Runtime
{
    public sealed class SceneOps
    {
        readonly SceneRuntime runtime;

        public SceneOps(SceneRuntime runtime) { this.runtime = runtime; }

        SceneSession Session => runtime.Session;

        /// <summary>
        /// Renders the scene thumbnail for a save (504 × 704 PNG) when a graphics device exists; set by the view camera
        /// part (Package C). Null = no renderer: the save keeps the previous thumbnail or writes the placeholder.
        /// </summary>
        public Func<byte[]> ThumbnailRenderer;

        /// <summary>
        /// Adds a character from an HS2 card: the card is imported at once with the runtime's importer
        /// (the registered one, else the catalog's) and the node stores the subtree; the import's warnings and errors go to the
        /// scene findings with the node id (info findings only to <paramref name="importFindings"/>, which receives the whole
        /// report). A failed import fails the op (bad_args with the importer's message; error `chara.importFailed` in
        /// <paramref name="importFindings"/>) and adds nothing.
        /// </summary>
        public CommandResult AddCharaFromCard(string fullPath, string parentId = null, string point = null, int index = -1, ExecuteOptions o = default,
            FindingList importFindings = null)
        {
            CardAppearanceSource src;
            try { src = runtime.Services.CardSources.FromFile(fullPath); }
            catch (Exception e) { return CommandResult.Error(CommandCodes.BadArgs, "card not readable: " + e.GetType().Name + ": " + e.Message); }
            var importer = ImportSetup.ImporterOf(runtime.Services);
            if (importer == null)
            {
                runtime.Services.Cards.ReleaseIfUnreferenced(src.Sha256);
                return CommandResult.Error(CommandCodes.Unsupported, "no card importer is available (no content catalog)");
            }
            string failure = null;
            var f = new FindingList();
            CharaContainer cc = null;
            try
            {
                cc = importer.Import(src, f);
                if (cc == null) failure = "the importer returned nothing";
            }
            catch (Exception e) { failure = "the importer threw " + e.GetType().Name + ": " + e.Message; }
            importFindings?.AddRange(f);
            if (cc == null)
            {
                string first = null;
                foreach (var x in f) if (x.Severity == FindingSeverity.Error) { first = x.Code + ": " + x.Message; break; }
                string msg = "import of the card failed (" + failure + (first != null ? "; " + first : "") + ")";
                importFindings?.Error("chara.importFailed", System.IO.Path.GetFileName(fullPath), msg);
                runtime.Services.Cards.ReleaseIfUnreferenced(src.Sha256);
                return CommandResult.Error(CommandCodes.BadArgs, msg);
            }
            var r = Session.AddCharaFromContainer(cc, parentId, point, index, o);
            if (r.Status == CommandStatus.Ok && r.NewId != null)
                foreach (var x in f)
                    if (x.Severity != FindingSeverity.Info)
                        for (int i = 0; i < Math.Max(1, x.Count); i++)
                            Session.Findings.Add(x.Severity, x.Code, r.NewId, (string.IsNullOrEmpty(x.Path) ? "" : x.Path + ": ") + x.Message);
            runtime.Services.Cards.ReleaseIfUnreferenced(src.Sha256);
            runtime.ApplyNow();
            return r;
        }

        /// <summary>A character from a character file (.hs2nchara; bridge `chara.load`). Load errors → bad_args.</summary>
        public CommandResult AddCharaFromFile(string hs2ncharaPath, string parentId = null, string point = null, int index = -1, ExecuteOptions o = default,
            FindingList loadFindings = null)
        {
            LoadResult<CharaContainer> lr;
            try { lr = CharaContainer.Load(hs2ncharaPath); }
            catch (Exception e) { return CommandResult.Error(CommandCodes.BadArgs, "character file not readable: " + e.GetType().Name + ": " + e.Message); }
            loadFindings?.AddRange(lr.Findings);
            if (!lr.Ok) return CommandResult.Error(CommandCodes.BadArgs, "character file not readable (" + Path.GetFileName(hs2ncharaPath ?? "") + "): " + lr.Error.Message);
            var r = Session.AddCharaFromContainer(lr.Value, parentId, point, index, o);
            runtime.ApplyNow();
            return r;
        }

        /// <summary>An item node of list row <paramref name="id"/> with the prefab defaults of a new add.</summary>
        public CommandResult AddItem(ContentId id, string parentId = null, string point = null, int index = -1, ExecuteOptions o = default)
        {
            var db = runtime.Services.Db;
            if (db == null || !db.TryGetItem(id, out StudioItem row) || row == null)
                return CommandResult.Error(CommandCodes.NotFound, "item " + id + " is not in the item list");
            return AddItem(row, parentId, point, index, o);
        }

        /// <summary>An item node of list row <paramref name="row"/> (an item, or an accessory offered as an item: the node keeps the
        /// row's group and category) with the prefab defaults of a new add.</summary>
        public CommandResult AddItem(StudioItem row, string parentId = null, string point = null, int index = -1, ExecuteOptions o = default)
        {
            if (row == null) return CommandResult.Error(CommandCodes.NotFound, "no item row");
            // a row of a generated group has no prefab: the defaults of an item without one, completed by its source
            var made = ItemInstanceSource.NewItem(runtime, row, out string refusal);
            if (refusal != null) return CommandResult.Error(CommandCodes.NotFound, refusal);
            if (made != null)
            {
                var rg = Session.AddItem(made, parentId, point, index, o);
                runtime.ApplyNow();
                return rg;
            }
            GameObject prefab;
            // the defaults read keeps no bundle (the node's build that follows owns what it uses)
            var cat = runtime.Services.Catalog;
            try { prefab = cat.LoadTransient("item:defaults", () => cat.LoadAsset<GameObject>(row)); }
            catch (Exception e) { return CommandResult.Error(CommandCodes.Internal, "prefab not loadable: " + e.GetType().Name + ": " + e.Message); }
            if (!prefab) return CommandResult.Error(CommandCodes.NotFound, "prefab " + row.BundlePath + "/" + row.FileName + " not loadable");
            var state = ItemDefaults.FromPrefab(prefab, row);
            var r = Session.AddItem(state, parentId, point, index, o);
            runtime.ApplyNow();
            return r;
        }

        public CommandResult AddLight(ContentId id, string parentId = null, string point = null, int index = -1, ExecuteOptions o = default)
        {
            var r = Session.AddLight(id.ToString(), parentId, point, index, o);
            runtime.ApplyNow();
            return r;
        }

        /// <summary>
        /// Reparents a node keeping its world pose: pos' = A⁻¹·worldPos, rot' = euler(A.rotation⁻¹·worldRot)
        /// (Unity eulerAngles), scale' unchanged when `inheritScale` is false, else worldLossy / A.lossyScale, for the
        /// destination attach transform A (a fallback when the destination character is not built).
        /// </summary>
        public CommandResult MoveKeepWorld(string id, string parentId, string point, int index = -1, ExecuteOptions o = default)
        {
            runtime.ApplyNow();
            var n = Session.Model.Find(id);
            var r = runtime.Find(id);
            if (n == null) return CommandResult.Error(CommandCodes.NotFound, "node " + id + " is not in the scene");
            if (r == null || !r.HasHolder) return Session.Move(id, parentId, point, index, null, o);
            var a = runtime.AttachTransformFor(parentId, point);
            var h = r.Holder;
            var cur = n.Doc.Transform;
            var t = DocClone.Clone(cur);   // keeps members this build does not know
            t.Pos = UnityObjects.V(a ? a.InverseTransformPoint(h.position) : h.position);
            var q = (a ? Quaternion.Inverse(a.rotation) : Quaternion.identity) * h.rotation;
            t.Rot = UnityObjects.V(q.eulerAngles);
            if (cur.InheritScale && r.HasScaleChannel)
            {
                var w = h.lossyScale;
                var p = a ? a.lossyScale : Vector3.one;
                t.Scale = new Vec3(Div(w.x, p.x, cur.Scale.X), Div(w.y, p.y, cur.Scale.Y), Div(w.z, p.z, cur.Scale.Z));
            }
            var res = Session.Move(id, parentId, point, index, t, o);
            runtime.ApplyNow();
            return res;
        }

        static float Div(float a, float b, float keep) => Mathf.Abs(b) < 1e-6f ? keep : a / b;

        public CommandResult Delete(IEnumerable<string> ids, ExecuteOptions o = default)
        {
            var list = new List<string>(ids ?? Array.Empty<string>());
            runtime.ApplyNow();
            runtime.Capture.CaptureSubtrees(list);
            var r = Session.Delete(list, o);
            runtime.ApplyNow();
            return r;
        }

        public CommandResult Duplicate(IEnumerable<string> ids, ExecuteOptions o = default)
        {
            var list = new List<string>(ids ?? Array.Empty<string>());
            runtime.ApplyNow();
            runtime.Capture.CaptureSubtrees(list);
            var r = Session.Duplicate(list, o);
            runtime.ApplyNow();
            return r;
        }

        public SaveResult Save(string path, SaveOptions options = null)
        {
            options = Prepare(options);
            return Session.Save(path, options);
        }

        public byte[] ToBytes(SaveOptions options = null)
        {
            options = Prepare(options);
            return Session.ToBytes(options);
        }

        SaveOptions Prepare(SaveOptions options)
        {
            options = AppSaveOptions.Complete(options, runtime.Services);
            runtime.ApplyNow();
            runtime.Capture.CaptureAll();
            if (options.Renderer == null && options.Thumbnail == ThumbnailMode.Render && ThumbnailRenderer != null && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
                options.Renderer = ThumbnailRenderer;
            return options;
        }

        public LoadResult<SceneContainer> Load(string path, LoadOptions options = null)
        {
            var r = Session.Load(path, options);
            runtime.ApplyNow();
            return r;
        }

        public LoadResult<SceneContainer> Load(byte[] bytes, LoadOptions options = null)
        {
            var r = Session.Load(bytes, options);
            runtime.ApplyNow();
            return r;
        }
    }
}
