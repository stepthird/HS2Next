// One open scene document: owns the model, the command bus, the selection,
// the events, the findings and the file carrier. New/Load/Bind replace the model and clear history; Save and
// ToBytes convert the model back into its container. The high-level operations are each one bus
// operation (one history entry). Silent writes (captures, normalisations) bypass history and arrive as
// one batch with their origin. Unity-free: the realization (HS2Next.Scenes.Runtime) follows the events.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using HS2Next.Save;
using HS2Next.Scenes.Constraints;

namespace HS2Next.Scenes
{
    /// <summary>Where a new root node is placed (vanilla option `initialPosition`).</summary>
    public enum NewNodePlacement
    {
        /// <summary>At the origin.</summary>
        Origin,
        /// <summary>At <c>scene.camera.current.pivot</c> (<see cref="SessionOptions.DefaultPivot"/> when there is no stored view).</summary>
        CameraPivot,
    }

    public sealed class SessionOptions
    {
        /// <summary>Source of new ids (default: GUIDs).</summary>
        public IIdSource Ids = new GuidIdSource();
        public AttachPointTable AttachPoints = AttachPointTable.Vanilla;
        public TreeRules Rules = new TreeRules();
        /// <summary>History bounds.</summary>
        public HistoryLimits History = new HistoryLimits();
        public NewNodePlacement NewNodePlacement = NewNodePlacement.Origin;
        /// <summary>The view's pivot while no view is stored (the UI sets SceneViewCamera.DefaultPivot, so a node added at the
        /// pivot lands where the view looks; Phase 3.6).</summary>
        public Vec3 DefaultPivot = Vec3.Zero;
        /// <summary>`correction` of a new character (8 categories); the Unity layer passes all true when
        /// <c>CorrectSetup.InitialCategoriesOn</c> is set.</summary>
        public bool[] CorrectionDefault = new bool[8];
        /// <summary>Default labels; the Unity layer injects one that knows list row names.</summary>
        public ILabelProvider LabelProvider = DefaultLabelProvider.Instance;
        /// <summary>
        /// The card importer of this session when <see cref="CharaImport.Importer"/> is not registered (the Unity layer sets the
        /// catalog's importer, Phase 4.6): the first edit of a card-backed character and a save import with
        /// <c>CharaImport.Importer ?? Importer</c> (<see cref="SceneSession.Importer"/>). Null = none.
        /// </summary>
        public ICharaImporter Importer;
    }

    /// <summary>One node to add: its document, place and appearance; children are inserted after it in pre-order.</summary>
    public sealed class NodeSpec
    {
        /// <summary>Envelope and kind section; <c>Doc.Id</c> null = a new id is assigned. Doc.Parent and Doc.Order are
        /// written by the model.</summary>
        public SceneNode Doc;
        /// <summary>Parent of a top spec (null = root); ignored for <see cref="Children"/> (their parent is the enclosing spec).</summary>
        public string ParentId;
        /// <summary>Attach point under a character parent, else null.</summary>
        public string Point;
        /// <summary>Final index in the sibling group; -1 = end.</summary>
        public int Index = -1;
        /// <summary>Character appearance (characters only; null = missing).</summary>
        public CharaAppearance Appearance;
        /// <summary>Node-scope file-backed ext data, or null.</summary>
        public ExtScopeData FileExt;
        public NodeSpec[] Children;
    }

    /// <summary>Field writes of a silent group (<see cref="SceneSession.WriteSilently"/>).</summary>
    public sealed class SilentWriter
    {
        readonly SceneSession session;
        internal readonly ChangeSink Sink = new ChangeSink();

        internal SilentWriter(SceneSession session) { this.session = session; }

        /// <summary>Writes one field with the value rules of a property set but without history; NoOp when equal.</summary>
        public CommandResult Set(string nodeId, string path, object value) => session.Commands.SilentSet(Sink, nodeId, path, value);
    }

    public sealed class SceneSession
    {
        static SceneSession()
        {
            // The pose-import provenance block, core node scope.
            PoseProvenanceExt.EnsureRegistered(ExtRegistry.Default);
        }

        // The session of a model (the Unity-free commands reach the session's importer through their model).
        static readonly ConditionalWeakTable<SceneModel, SceneSession> sessionsByModel = new ConditionalWeakTable<SceneModel, SceneSession>();

        readonly int threadId = Thread.CurrentThread.ManagedThreadId;
        int cameraCounter;

        public SceneSession(SessionOptions options = null)
        {
            Options = options ?? new SessionOptions();
            Events = new SceneEvents();
            Findings = new SceneFindings();
            Selection = new SelectionModel(this);
            Model = new SceneModel(Options.AttachPoints, Options.Rules, Options.LabelProvider);
            Commands = new CommandBus(this, Options.History);
            Model.Selection = Selection;
            Model.HeldLookup = id => Commands.History.HeldWithId(id);
            sessionsByModel.Add(Model, this);
        }

        /// <summary>The card importer of this session: <see cref="CharaImport.Importer"/>, else <see cref="SessionOptions.Importer"/>;
        /// null when neither is set.</summary>
        public ICharaImporter Importer => CharaImport.Importer ?? Options.Importer;

        /// <summary>The card importer of the session that owns <paramref name="model"/> (<see cref="Importer"/>); for a model
        /// without a session <see cref="CharaImport.Importer"/>.</summary>
        public static ICharaImporter ImporterOf(SceneModel model) =>
            model != null && sessionsByModel.TryGetValue(model, out var s) ? s.Importer : CharaImport.Importer;

        public SessionOptions Options { get; }
        /// <summary>The model; the same object for the life of the session (New/Load/Bind reset its content).</summary>
        public SceneModel Model { get; }
        public CommandBus Commands { get; }
        public SelectionModel Selection { get; }
        public SceneEvents Events { get; }
        /// <summary>Findings about the open scene (bind, save, and what the realization reports).</summary>
        public SceneFindings Findings { get; }

        /// <summary>A new id from <see cref="SessionOptions.Ids"/> (e.g. for a new route point).</summary>
        public string NewId() => Options.Ids.NewId();

        // ---------- Maker references (Phase 4.2 package B) ----------

        EditReferences makerReferences;

        /// <summary>
        /// The reference documents of the Maker's per-row reset for this session's characters:
        /// captured by the first edit session of a character (StudioOps.BeginEditSession), read by StudioOps.ResetAppearance;
        /// created on first use and bound to this session's events for its life (a removed node forgets its reference, new /
        /// open clears them); never saved.
        /// </summary>
        public EditReferences MakerReferences => makerReferences ?? (makerReferences = new EditReferences(this));

        /// <summary>Throws when called from another thread than the one that created the session.</summary>
        internal void CheckThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != threadId)
                throw new InvalidOperationException("the scene session is used from another thread than the one that created it");
        }

        void CheckIdle(string what)
        {
            CheckThread();
            if (Commands.Busy) throw new InvalidOperationException(what + " is not allowed while a command applies or an event handler runs");
        }

        // ---------- document ----------

        /// <summary>An empty model with default settings, empty title and a fresh container; history cleared; Cleared, then Loaded.</summary>
        public void NewScene()
        {
            CheckIdle("NewScene");
            Replace(() => { }, null);
            Events.RaiseLoaded(new SceneLoadedArgs(new FindingList(), isNew: true));
        }

        /// <summary>Raises Cleared, clears history, model, selection and findings, then runs <paramref name="fill"/> on the empty model.</summary>
        void Replace(Action fill, SceneContainer file)
        {
            var oldSel = Selection.Snapshot();
            var forgotten = new List<TreeNode>();
            bool hadHistory = Commands.History.Undo.Count + Commands.History.Redo.Count > 0;
            Events.RaiseCleared();
            Commands.Reset(forgotten);
            // Forgotten ids belong to the old model: raised before the new one exists (it may reuse an id).
            foreach (var n in forgotten) Events.RaiseNodeForgotten(n.Id);
            Model.Reset(file);
            Selection.SetSilently(null);
            Findings.Clear();
            cameraCounter = 0;
            fill();
            Commands.Version++;
            Commands.SavedVersion = Commands.Version;
            if (oldSel.Length > 0) Events.RaiseSelectionChanged(oldSel, Selection.Snapshot());
            if (hadHistory) Commands.RaiseHistoryChanged();
        }

        /// <summary>Loads a scene file; on success binds it. On failure the current scene stays untouched.</summary>
        public LoadResult<SceneContainer> Load(string path, LoadOptions options = null) => LoadCore(o => SceneContainer.Load(path, o), options, path);

        public LoadResult<SceneContainer> Load(byte[] bytes, LoadOptions options = null) => LoadCore(o => SceneContainer.Load(bytes, o), options, null);

        LoadResult<SceneContainer> LoadCore(Func<LoadOptions, LoadResult<SceneContainer>> load, LoadOptions options, string path)
        {
            CheckIdle("Load");
            var o = options ?? new LoadOptions();
            PoseProvenanceExt.EnsureRegistered(o.Registry ?? ExtRegistry.Default);
            var r = load(o);
            if (!r.Ok) return r;
            var bf = BindCore(r.Value, r.Findings, path);
            r.Findings.AddRange(bf);
            return r;
        }

        /// <summary>Replaces the model with the container's content (history cleared); returns the bind findings.</summary>
        public FindingList Bind(SceneContainer container) => BindCore(container, null, null);

        FindingList BindCore(SceneContainer c, FindingList loadFindings, string path)
        {
            CheckIdle("Bind");
            SceneDocumentConverter.CheckTree(c);
            var bf = new FindingList();
            Replace(() => SceneDocumentConverter.Bind(c, Model, bf), c);
            NormalizeAfterBind(bf);
            if (loadFindings != null) Findings.AddRange(loadFindings);
            CopyFindings(bf);
            var all = new FindingList();
            if (loadFindings != null) all.AddRange(loadFindings);
            all.AddRange(bf);
            Events.RaiseLoaded(new SceneLoadedArgs(all, path: path));
            return bf;
        }

        // Bind normalisations: silent, before Loaded; each bumps the version (the model then differs from the file).
        void NormalizeAfterBind(FindingList bf)
        {
            CameraNodeState last = null;
            int active = 0;
            foreach (var n in Model.PreOrder())
                if (n.Doc.State is CameraNodeState cam && cam.Active) { last = cam; active++; }
            if (active > 1)
            {
                foreach (var n in Model.PreOrder())
                    if (n.Doc.State is CameraNodeState cam && cam.Active && cam != last) cam.Active = false;
                bf.Info("scene.multipleActiveCameras", "scene.json", active + " active camera nodes; the last one in array order stays active");
                Commands.Version++;
            }
            foreach (var n in Model.PreOrder())
            {
                if (n.Kind != NodeKind.Chara || !AccessoryNormalizer.Normalize(n)) continue;
                bf.Info("chara.accessoriesNormalized", "nodes:" + n.Id, "state.accessories filled and pruned against the worn coordinate");
                Commands.Version++;
            }
        }

        static string NodeOf(string findingPath) =>
            findingPath != null && findingPath.StartsWith("nodes:", StringComparison.Ordinal) && findingPath.Length >= 38 ? findingPath.Substring(6, 32) : null;

        void CopyFindings(FindingList list)
        {
            foreach (var f in list)
                for (int i = 0; i < Math.Max(1, f.Count); i++) Findings.Add(f.Severity, f.Code, NodeOf(f.Path), f.Message);
        }

        static void Report(FindingList f, FindingSeverity severity, string code, string nodeId, string message) =>
            f.Add(severity, code, nodeId == null ? "" : "nodes:" + nodeId, message);

        /// <summary>
        /// The ext instances of the current model as a save hands them to the dispatcher (scene, the nodes in pre-order, then
        /// each character's chara and coord scopes in node order), without converting the model into its container: the
        /// scene scope's file data with the scene's inline holder, each node's file data (a node without any gets a detached
        /// one) with its inline holder, and the stored subtree of each character (a card-backed character not imported yet has
        /// none). Unknown kinds are left out, as a save leaves them. A read: nothing in the model or the container changes.
        /// </summary>
        public IEnumerable<ExtInstance> ExtInstances()
        {
            CheckThread();
            string format = SaveFormats.Scene;
            yield return new ExtInstance { Scope = HS2Next.Api.Save.ExtScope.Scene, ContainerFormat = format, Root = "", Data = Model.File?.SceneExt ?? new ExtScopeData(HS2Next.Api.Save.ExtScope.Scene), Main = Model.SceneExt };
            var charas = new List<TreeNode>();
            foreach (var n in Model.PreOrder())
            {
                if (n.Kind == NodeKind.Opaque) continue;
                yield return new ExtInstance { Scope = HS2Next.Api.Save.ExtScope.Node, OwnerId = n.Id, ContainerFormat = format, Root = "", Data = n.FileExt ?? new ExtScopeData(HS2Next.Api.Save.ExtScope.Node), Main = n.Doc.Ext };
                if (n.Kind == NodeKind.Chara && n.Appearance?.Stored != null) charas.Add(n);
            }
            foreach (var n in charas)
                foreach (var i in n.Appearance.Stored.Instances(format, SceneContainer.CharaRoot(n.Id), n.Id)) yield return i;
        }

        /// <summary>
        /// Offers the open scene's blocks of <paramref name="namespaces"/> to their handlers again (a plugin enabled while the
        /// app runs: its new instance reads the scene's data, so a later save writes it back instead of removing the blocks);
        /// blocks of these namespaces kept as unknown are decided again. Returns the number of blocks a handler read; the
        /// findings of the pass go to <paramref name="findings"/>.
        /// </summary>
        public int ReadExtAgain(ICollection<string> namespaces, ExtRegistry registry = null, FindingList findings = null)
        {
            CheckThread();
            return ExtDispatcher.ReadAgain(ExtInstances(), registry ?? ExtRegistry.Default, namespaces, findings ?? new FindingList());
        }

        /// <summary>Converts the model into its container and saves atomically. Never throws; Saving before, Saved after.</summary>
        public SaveResult Save(string path, SaveOptions options = null)
        {
            CheckThread();
            var o = options ?? new SaveOptions();
            if (Commands.Applying || Events.IsDeliveringChanged)
                return new SaveResult { Error = new SaveFormatException(SaveErrorCode.BadDocument, "the scene is busy (save called from a running command or a Changed handler)") };
            Events.RaiseSaving();
            var mf = new FindingList();
            SaveResult r;
            try
            {
                Prepare(mf);
                PoseProvenanceExt.EnsureRegistered(o.Registry ?? ExtRegistry.Default);
                r = Model.File.Save(path, o);
            }
            catch (Exception e)
            {
                r = new SaveResult { Error = e as SaveFormatException ?? new SaveFormatException(SaveErrorCode.BadDocument, "unexpected " + e.GetType().Name + ": " + e.Message, null, null, 0, e) };
            }
            var all = new FindingList();
            all.AddRange(mf);
            all.AddRange(r.Findings);
            r.Findings = all;
            if (r.Ok)
            {
                SceneDocumentConverter.Adopt(Model);
                Commands.MarkSaved();
            }
            CopyFindings(mf);
            Events.RaiseSaved(r);
            return r;
        }

        /// <summary>As <see cref="Save"/> without touching the disk (the version is not marked saved). Throws
        /// <see cref="SaveFormatException"/> as <c>ContainerBase.ToBytes</c> does.</summary>
        public byte[] ToBytes(SaveOptions options = null)
        {
            CheckThread();
            if (Commands.Applying || Events.IsDeliveringChanged) throw new InvalidOperationException("ToBytes is not allowed while a command applies or Changed handlers run");
            var o = options ?? new SaveOptions();
            Events.RaiseSaving();
            var mf = new FindingList();
            byte[] bytes;
            try
            {
                Prepare(mf);
                PoseProvenanceExt.EnsureRegistered(o.Registry ?? ExtRegistry.Default);
                bytes = Model.File.ToBytes(o);
            }
            catch (Exception e)
            {
                CopyFindings(mf);
                Events.RaiseSaved(new SaveResult { Findings = mf, Error = e as SaveFormatException ?? new SaveFormatException(SaveErrorCode.BadDocument, e.Message, null, null, 0, e) });
                throw;
            }
            SceneDocumentConverter.Adopt(Model);
            CopyFindings(mf);
            Events.RaiseSaved(new SaveResult { Findings = mf, Size = bytes.Length });
            return bytes;
        }

        // after Saving: import card-backed characters, normalise the accessory maps as a guard, fill the container. A
        // card-backed character that cannot be imported (no importer, or the import fails) is an error `chara.notImported`
        // and is saved without a subtree: it loads as a missing character (`scene.charaMissing`).
        void Prepare(FindingList mf)
        {
            var cached = new ChangeSink();
            foreach (var n in Model.PreOrder())
            {
                if (n.Kind != NodeKind.Chara) continue;
                var a = n.Appearance;
                if (a.Stored != null || a.Card == null) continue;
                var importer = Importer;
                string why;
                if (importer == null) why = "no card importer";
                else
                {
                    try
                    {
                        var cc = importer.Import(a.Card, mf);
                        if (cc?.Subtree != null)
                        {
                            Model.File.PlaceCharacter(n.Id, cc);
                            a.Stored = Model.File.Charas[n.Id];
                            cached.AppearanceChanged(n.Id);
                            continue;
                        }
                        why = "the importer returned nothing";
                    }
                    catch (Exception e) { why = "the importer threw " + e.GetType().Name + ": " + e.Message; }
                }
                Report(mf, FindingSeverity.Error, "chara.notImported", n.Id, "the card could not be imported (" + why + "): saved without a character subtree");
            }
            Commands.Silent(ChangeOrigin.Capture, cached);
            NormalizeAll();
            SceneDocumentConverter.Fill(Model);
        }

        // Accessory maps as a guard, silent with origin Normalize.
        void NormalizeAll()
        {
            List<KeyValuePair<string, SortedDictionary<string, bool>>> todo = null;
            foreach (var n in Model.PreOrder())
            {
                if (n.Kind != NodeKind.Chara) continue;
                var map = AccessoryNormalizer.Normalized(((CharaNodeState)n.Doc.State).State.Accessories, AccessoryNormalizer.KnownIds(n));
                if (map != null) (todo ??= new List<KeyValuePair<string, SortedDictionary<string, bool>>>()).Add(new KeyValuePair<string, SortedDictionary<string, bool>>(n.Id, map));
            }
            if (todo == null) return;
            WriteSilently(ChangeOrigin.Normalize, w => { foreach (var kv in todo) w.Set(kv.Key, "chara.state.accessories", kv.Value); });
        }

        /// <summary>
        /// The current scene.json model as a save would write it: node objects shared, not copied, except characters whose
        /// accessory map the save guard would normalise, which appear as normalised copies. A read: the model, its
        /// version and its events are untouched.
        /// </summary>
        public SceneDocument ExportDocument()
        {
            CheckThread();
            var doc = SceneDocumentConverter.ToDocument(Model);
            for (int i = 0; i < doc.Nodes.Count; i++)
            {
                var n = Model.Find(doc.Nodes[i].Id);
                if (n != null && n.Kind == NodeKind.Chara) doc.Nodes[i] = Exported(n);
            }
            return doc;
        }

        /// <summary>A node's document as written: a normalised copy when the accessory guard would change it.</summary>
        SceneNode Exported(TreeNode n)
        {
            if (n.Kind != NodeKind.Chara) return n.Doc;
            var map = AccessoryNormalizer.Normalized(((CharaNodeState)n.Doc.State).State.Accessories, AccessoryNormalizer.KnownIds(n));
            if (map == null) return n.Doc;
            var copy = DocClone.Clone(n.Doc);
            ((CharaNodeState)copy.State).State.Accessories = map;
            return copy;
        }

        /// <summary>One node's canonical JSON as a save would write it (bridge `scene.doc`), or null when the id is not in the model.</summary>
        public string NodeJson(string id)
        {
            var n = Model.Find(id);
            if (n == null) return null;
            return new System.Text.UTF8Encoding(false).GetString(DocClone.Bytes(Exported(n))).TrimEnd('\n');
        }

        // ---------- operations (each one bus operation) ----------

        Vec3 Placement(string parentId) =>
            parentId == null && Options.NewNodePlacement == NewNodePlacement.CameraPivot ? Model.Settings.Camera.Current?.Pivot ?? Options.DefaultPivot : Vec3.Zero;

        CommandResult AddOne(SceneNode doc, CharaAppearance app, string parentId, string point, int index, ExecuteOptions o, string label)
        {
            var spec = new NodeSpec { Doc = doc, ParentId = parentId, Point = point, Index = index, Appearance = app };
            PrepareSpec(spec);
            return Commands.Execute(new AddNodesCommand(new[] { spec }, label), o);
        }

        // New ids for specs without one; a character's accessory map filled from its known coordinate.
        void PrepareSpec(NodeSpec s)
        {
            if (s?.Doc == null) return;
            if (s.Doc.Id == null) s.Doc.Id = NewId();
            if (s.Doc.State is CharaNodeState cs)
            {
                var map = AccessoryNormalizer.Normalized(cs.State.Accessories, s.Appearance?.WornAccessoryIds());
                if (map != null) cs.State.Accessories = map;
            }
            if (s.Children != null) foreach (var c in s.Children) PrepareSpec(c);
        }

        public CommandResult AddFolder(string parentId = null, string point = null, int index = -1, ExecuteOptions o = default)
        {
            CheckThread();
            return AddOne(NodeDefaults.Folder(NewId(), Placement(parentId)), null, parentId, point, index, o, "Add folder");
        }

        public CommandResult AddCamera(string parentId = null, string point = null, int index = -1, ExecuteOptions o = default)
        {
            CheckThread();
            int before = cameraCounter;
            // after a load the scene's "Camera N" names stay: a new camera takes the next number, never a name already there
            int n = Math.Max(++cameraCounter, NextCameraNumber());
            cameraCounter = n;
            var r = AddOne(NodeDefaults.Camera(NewId(), n, Placement(parentId)), null, parentId, point, index, o, "Add camera");
            if (r.Status != CommandStatus.Ok) cameraCounter = before;
            return r;
        }

        /// <summary>1 + the highest N of the camera nodes named "Camera N" (1 without any).</summary>
        int NextCameraNumber()
        {
            int max = 0;
            string prefix = NodeDefaults.CameraNamePrefix;
            foreach (var t in Model.PreOrder())
            {
                string name = t.Kind == NodeKind.Camera ? t.Doc.Name : null;
                if (name != null && name.StartsWith(prefix, StringComparison.Ordinal)
                    && int.TryParse(name.Substring(prefix.Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int k)
                    && k > max) max = k;
            }
            return max + 1;
        }

        public CommandResult AddRoute(string parentId = null, string point = null, int index = -1, ExecuteOptions o = default)
        {
            CheckThread();
            return AddOne(NodeDefaults.Route(NewId(), NewId(), Placement(parentId)), null, parentId, point, index, o, "Add route");
        }

        /// <summary>A light node of list row <paramref name="lightRef"/> with the absent values of `light`.</summary>
        public CommandResult AddLight(string lightRef, string parentId = null, string point = null, int index = -1, ExecuteOptions o = default)
        {
            CheckThread();
            if (!JsonText.IsRef(lightRef)) return CommandResult.Error(CommandCodes.BadArgs, "not a light ref: \"" + lightRef + "\"");
            return AddOne(NodeDefaults.Light(NewId(), lightRef, Placement(parentId)), null, parentId, point, index, o, "Add light");
        }

        /// <summary>An item node with the caller's state (the Unity layer computes the prefab defaults); the state is copied.</summary>
        public CommandResult AddItem(ItemNodeState initial, string parentId = null, string point = null, int index = -1, ExecuteOptions o = default)
        {
            CheckThread();
            if (initial == null) return CommandResult.Error(CommandCodes.BadArgs, "no item state");
            ItemNodeState state;
            try { state = DocClone.Clone(initial); }
            catch (Exception e) { return CommandResult.Error(CommandCodes.BadArgs, "item state cannot be saved: " + e.Message); }
            return AddOne(NodeDefaults.Item(NewId(), state, Placement(parentId)), null, parentId, point, index, o, "Add item");
        }

        public CommandResult AddChara(CharaAppearance appearance, string parentId = null, string point = null, int index = -1, ExecuteOptions o = default)
        {
            CheckThread();
            var doc = NodeDefaults.Chara(NewId(), Options.CorrectionDefault, Placement(parentId));
            // a card's accessory hides of a Studio load, folded once when it is added (Phase 4.5; the normalisation keeps them)
            if (appearance?.Card?.HiddenAccessoryIds != null && doc.State is CharaNodeState cs) foreach (var h in appearance.Card.HiddenAccessoryIds) cs.State.Accessories[h] = false;
            return AddOne(doc, appearance ?? new CharaAppearance(), parentId, point, index, o, "Add character");
        }

        /// <summary>
        /// A character node built from a character file's content: the subtree, its provenance and
        /// the resources it references are copied into the scene carrier under the new node id, then added as one history
        /// entry ("Add character"; `state.accessories` normalised from the stored coordinate). A failed add leaves no
        /// <c>Charas</c> entry (the copied resources stay; a save writes only the reachable ones).
        /// </summary>
        public CommandResult AddCharaFromContainer(CharaContainer c, string parentId = null, string point = null, int index = -1, ExecuteOptions o = default)
        {
            CheckThread();
            if (c?.Subtree == null) return CommandResult.Error(CommandCodes.BadArgs, "no character content");
            if (Commands.Busy) return CommandResult.Error(CommandCodes.Busy, "the bus is busy");
            string id = NewId();
            try { Model.File.PlaceCharacter(id, c); }
            catch (Exception e) { return CommandResult.Error(CommandCodes.BadArgs, "character content cannot be placed: " + e.GetType().Name + ": " + e.Message); }
            var r = AddOne(NodeDefaults.Chara(id, Options.CorrectionDefault, Placement(parentId)), new CharaAppearance { Stored = Model.File.Charas[id] },
                parentId, point, index, o, "Add character");
            if (r.Status != CommandStatus.Ok) Model.File.Charas.Remove(id);
            return r;
        }

        public CommandResult AddNodes(IReadOnlyList<NodeSpec> specs, ExecuteOptions o = default)
        {
            CheckThread();
            if (specs != null) foreach (var s in specs) PrepareSpec(s);
            return Commands.Execute(new AddNodesCommand(specs, "Add"), o);
        }

        /// <summary>Deletes nodes with their subtrees; references to them are cleared in the same command.</summary>
        public CommandResult Delete(IEnumerable<string> ids, ExecuteOptions o = default)
        {
            CheckThread();
            return Commands.Execute(new RemoveNodesCommand(ids), o);
        }

        /// <summary>Copies the top-most selected subtrees right after their originals; the copies become the selection. The constraints
        /// with both ends among the copied nodes are copied too, in the same history entry; a scene with no such entry runs the plain add.</summary>
        public CommandResult Duplicate(IEnumerable<string> ids, ExecuteOptions o = default)
        {
            CheckThread();
            if (Commands.Busy) return CommandResult.Error(CommandCodes.Busy, "the bus is busy");
            var selected = new HashSet<TreeNode>();
            foreach (var id in ids ?? Array.Empty<string>())
            {
                var n = Model.Find(id);
                if (n == null) return CommandResult.Error(CommandCodes.NotFound, "node " + id + " is not in the scene");
                selected.Add(n);
            }
            if (selected.Count == 0) return CommandResult.Error(CommandCodes.BadArgs, "nothing to duplicate");
            var tops = new List<TreeNode>();
            foreach (var n in Model.PreOrder())
            {
                if (!selected.Contains(n)) continue;
                bool covered = false;
                for (var p = n.Parent; p != null; p = p.Parent) if (selected.Contains(p)) { covered = true; break; }
                if (!covered) tops.Add(n);
            }
            foreach (var t in tops)
                foreach (var x in Model.Subtree(t))
                    if (x.Kind == NodeKind.Opaque) return CommandResult.Error(CommandCodes.Unsupported, "the subtree of " + t.Id + " holds a node of an unknown kind");
            // Pre-order visits each sibling group in index order: copy k of a group lands after its original, shifted by
            // the k copies placed before it.
            var placed = new Dictionary<string, int>(StringComparer.Ordinal);
            var specs = new List<NodeSpec>();
            var copied = Model.Constraints != null ? new Dictionary<string, string>(StringComparer.Ordinal) : null;
            try
            {
                foreach (var t in tops)
                {
                    string key = (t.Parent?.Id ?? "") + "\u0000" + (t.Point ?? "\u0001");
                    placed.TryGetValue(key, out int k);
                    var spec = CopySpec(t, copied);
                    spec.ParentId = t.Parent?.Id;
                    spec.Point = t.Point;
                    spec.Index = t.Index + 1 + k;
                    placed[key] = k + 1;
                    specs.Add(spec);
                }
            }
            catch (Exception e) { return CommandResult.Error(CommandCodes.BadArgs, "cannot copy: " + e.Message); }
            var add = new AddNodesCommand(specs, "Duplicate", true);
            var after = CarryConstraints(Model.Constraints, copied, out _, out int refused);
            ConstraintsLeftOut = refused;
            return after != null ? AddWithConstraints(add, after, o) : Commands.Execute(add, o);
        }

        /// <summary>The constraints the last <see cref="Duplicate"/> could not carry though both their ends were copied.</summary>
        public int ConstraintsLeftOut { get; private set; }

        /// <summary>
        /// The list the scene holds once the entries of <paramref name="from"/> whose two ends are keys of <paramref name="map"/> (old node id to
        /// new) are copied after its own: new ids, ends on the copies. Null when none comes along. An entry with an end outside counts in
        /// <paramref name="outside"/>; one the list cannot take, because it is full or the entry breaks a rule an edit may not break, counts in
        /// <paramref name="refused"/>: nodes are never refused because of a constraint, so such an entry stays behind.
        /// </summary>
        public ConstraintList CarryConstraints(ConstraintList from, IReadOnlyDictionary<string, string> map, out int outside, out int refused)
        {
            outside = refused = 0;
            if (from == null || map == null) return null;
            var copies = ConstraintRemap.Copy(from.Entries, map, NewId, out outside);
            int room = ConstraintLimits.MaxEntries - (Model.Constraints?.Count ?? 0);
            var kept = new List<ConstraintEntry>(copies.Count);
            foreach (var e in copies)
            {
                if (kept.Count < room && e.Problem() == null) kept.Add(e);
                else refused++;
            }
            return ConstraintRemap.Appended(Model.Constraints, kept);
        }

        // The add and the list as one history entry, or as steps of the transaction that is open.
        CommandResult AddWithConstraints(AddNodesCommand add, ConstraintList after, ExecuteOptions o)
        {
            string label = o.Label ?? add.Label;
            var tx = Commands.BeginOrJoin(label, o);
            if (!tx.IsOpen) return tx.Failure ?? CommandResult.Error(CommandCodes.Busy, "the bus is busy");
            using (tx)
            {
                var added = tx.Execute(add);
                if (added.Status == CommandStatus.Error) return added;
                var edit = tx.Execute(ConstraintEditCommand.ReplaceList(after, label));
                if (edit.Status == CommandStatus.Error) return edit;
                var done = tx.Commit();
                return done.Status == CommandStatus.Error ? done : CommandResult.Success(done.Command, added.NewIds);
            }
        }

        // A deep copy of a subtree with new ids (nodes and route points); a copied camera is inactive. `copied`, when given, collects old id to new.
        NodeSpec CopySpec(TreeNode n, Dictionary<string, string> copied)
        {
            var doc = (SceneNode)DocClone.Clone(n.Doc, out _, null);
            doc.Id = NewId();
            if (copied != null) copied[n.Id] = doc.Id;
            if (doc.State is RouteNodeState r) foreach (var p in r.Points) p.Id = NewId();
            if (doc.State is CameraNodeState cam) cam.Active = false;
            if (doc.State is CharaNodeState chara) { HS2Next.Scenes.Wet.WetCopy.Prepare(chara.Wet); chara.Ragdoll.PrepareCopy(); }
            var spec = new NodeSpec { Doc = doc, Appearance = n.Appearance?.Clone(), FileExt = n.FileExt?.Clone() };
            var kids = n.Children;
            if (kids.Count > 0)
            {
                spec.Children = new NodeSpec[kids.Count];
                for (int i = 0; i < kids.Count; i++)
                {
                    var c = CopySpec(kids[i], copied);
                    c.Point = kids[i].Point;
                    c.Index = kids[i].Index;
                    spec.Children[i] = c;
                }
            }
            return spec;
        }

        /// <summary>Reparent / reorder; <paramref name="newTransform"/> null keeps the stored transform.</summary>
        public CommandResult Move(string id, string parentId, string point, int index, NodeTransform newTransform = null, ExecuteOptions o = default)
        {
            CheckThread();
            return Commands.Execute(new MoveNodeCommand(id, parentId, point, index, newTransform), o);
        }

        public CommandResult Reorder(string id, int index, ExecuteOptions o = default)
        {
            CheckThread();
            var n = Model.Find(id);
            if (n == null) return CommandResult.Error(CommandCodes.NotFound, "node " + id + " is not in the scene");
            return Commands.Execute(new MoveNodeCommand(id, n.Parent?.Id, n.Point, index, null, "Reorder"), o);
        }

        /// <summary>Sets one field; <paramref name="id"/> null = the scene.</summary>
        public CommandResult Set(string id, string path, object value, ExecuteOptions o = default) => Commands.Set(id, path, value, o);

        /// <summary>Sets one field from JSON text in the form of <see cref="FieldJson"/> (bridge, plugins).</summary>
        public CommandResult SetJson(string id, string path, string json, ExecuteOptions o = default)
        {
            CheckThread();
            object v;
            try { v = FieldJson.FromText(json, FieldAccessor.LeafType(Model, id, path)); }
            catch (CommandException e) { return CommandResult.Error(e.Code, e.Message); }
            return Commands.Set(id, path, v, o);
        }

        /// <summary>`chara.anime.ref` = <paramref name="animeRef"/> and `chara.anime.time` = 0 (one entry).</summary>
        public CommandResult SetAnimation(string id, AnimeRef animeRef, ExecuteOptions o = default)
        {
            CheckThread();
            return Commands.Execute(new CompositeCommand("Animation", new ISceneCommand[]
            {
                new SetPropertyCommand(id, "chara.anime.ref", animeRef),
                new SetPropertyCommand(id, "chara.anime.time", 0f),
            }), o);
        }

        /// <summary>`map.ref` and a reset map pose (one entry). Material edits name the materials of one map, so another map (or none)
        /// starts without them, in the same entry.</summary>
        public CommandResult SetMap(string mapRef, ExecuteOptions o = default)
        {
            CheckThread();
            var steps = new List<ISceneCommand>
            {
                new SetPropertyCommand(null, "map.ref", mapRef),
                new SetPropertyCommand(null, "map.pos", Vec3.Zero),
                new SetPropertyCommand(null, "map.rot", Vec3.Zero),
            };
            var map = Model.Settings.Map;
            if (map.MaterialOverrides.Count > 0 && !string.Equals(map.Ref, mapRef, StringComparison.Ordinal))
                steps.Add(new SetPropertyCommand(null, "map.materialOverrides", new List<MaterialOverride>()));
            return Commands.Execute(new CompositeCommand("Map", steps), o);
        }

        /// <summary>Activates one camera node and deactivates the others; null deactivates all.</summary>
        public CommandResult SetActiveCamera(string id, ExecuteOptions o = default)
        {
            CheckThread();
            return Commands.Execute(ActiveCameraCommand(id), o);
        }

        internal ISceneCommand ActiveCameraCommand(string id)
        {
            var steps = new List<ISceneCommand>();
            foreach (var n in Model.PreOrder())
                if (n.Id != id && n.Doc.State is CameraNodeState cam && cam.Active) steps.Add(new SetPropertyCommand(n.Id, "camera.active", false));
            if (id != null) steps.Add(new SetPropertyCommand(id, "camera.active", true));
            return new CompositeCommand("Active camera", steps);
        }

        /// <summary>Replaces a character's appearance (re-import 2.3, coordinate load 4.x); normalises `state.accessories`.</summary>
        public CommandResult SetAppearance(string id, CharaAppearance appearance, ExecuteOptions o = default)
        {
            CheckThread();
            return Commands.Execute(new SetAppearanceCommand(id, appearance), o);
        }

        // ---------- silent writes ----------

        /// <summary>
        /// Writes runtime-owned or derived values without history (origin <see cref="ChangeOrigin.Capture"/> or
        /// <see cref="ChangeOrigin.Normalize"/>): one batch when anything changed; bumps the version. Not allowed while a
        /// bus operation applies or while Changed handlers run (InvalidOperationException).
        /// </summary>
        public void WriteSilently(ChangeOrigin origin, Action<SilentWriter> body) => Commands.WriteSilently(origin, body);

        /// <summary>Fills and prunes a character's `state.accessories` against the worn coordinate when it is known,
        /// silently (origin Normalize). True when the map changed.</summary>
        public bool NormalizeAccessories(string id)
        {
            CheckThread();
            var n = Model.Find(id);
            if (n == null || n.Kind != NodeKind.Chara) return false;
            var map = AccessoryNormalizer.Normalized(((CharaNodeState)n.Doc.State).State.Accessories, AccessoryNormalizer.KnownIds(n));
            if (map == null) return false;
            WriteSilently(ChangeOrigin.Normalize, w => w.Set(id, "chara.state.accessories", map));
            return true;
        }
    }
}
