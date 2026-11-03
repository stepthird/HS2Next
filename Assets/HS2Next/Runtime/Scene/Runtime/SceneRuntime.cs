// The Unity realization of one scene session: a plain class that follows the
// session's events, records what is dirty, and does the work in its apply phase. It owns one root GameObject:
//
//   HS2Next Scene        runtime root (never in a map scene)
//     Nodes              parent of root holders; children hang under their parent's attach transform
//     Graveyard          inactive; deleted holders kept for undo
//     View               Package C's SceneViewCamera
//
//   Apply()      structural pass (bury removed holders, revive / build / reparent in model pre-order, evict), then
//                changed field paths → kind appliers, then transforms, then visibility, then the registered parts
//   LateApply()  world-scale enforcement of attached nodes, then the parts (camera follow, view)
//   Tick(dt)     with ManualStepping BeginFrame(dt); Apply; with ManualStepping every built character's
//                CharaMotion.Step(Clock.FrameDt) in pre-order, then every built item's motion (StepItems,
//                12); LateApply
//   RealizeAll() clears the realization and schedules every node (the Loaded handler)
//
// Scene clock and global pause (Phase 3.6): one SceneClock (Clock) is the time of every
// scene consumer. BeginFrame(rawDt) starts a clock frame (FrameDt = 0 while paused); the Apply that follows ticks the parts
// that implement ISceneTickPart once with FrameDt; scene characters read FrameDt through CharaMotion.DeltaSource, items
// and map scripts are stepped with it. SetPaused switches the pause (never Time.timeScale); Step(n, dt) runs n frames of
// dt while paused, synchronously, in the frame order (Apply, tick parts, characters, items, map scripts, LateApply). New
// and load reset the clock (running, time 0; the pause is not saved).
//
// Play mode and the player add SceneRuntimeDriver (Update, order -5000: BeginFrame, Apply), ItemMotionDriver (LateUpdate,
// order 1050: the items after CharaMotion 1000) and SceneRuntimeLateDriver (LateUpdate, order 1100) to the root; Editor
// batch tests call Apply / Tick by hand. The constructor registers the item motion handlers (ItemMotion.Register). Event
// handlers never call the bus.
using System;
using System.Collections.Generic;
using HS2Next.Save;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HS2Next.Scenes.Runtime
{
    public sealed class SceneRuntime : IDisposable
    {
        public const string RootName = "HS2Next Scene";

        readonly Dictionary<string, NodeRealization> reals = new Dictionary<string, NodeRealization>(StringComparer.Ordinal);
        readonly List<ISceneRuntimePart> parts = new List<ISceneRuntimePart>();
        readonly Dictionary<ISceneRuntimePart, Action> partDisposers = new Dictionary<ISceneRuntimePart, Action>();
        Dictionary<string, HashSet<string>> dirtyPaths = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        readonly HashSet<string> transformDirty = new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> visibilityDirty = new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> nameDirty = new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> builtThisApply = new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> reported = new HashSet<string>(StringComparer.Ordinal);
        readonly List<CharaRealization> pendingBuilt = new List<CharaRealization>();
        // characters changed while paused whose physics settle once no drag is open (after 1.1.0)
        readonly List<CharaRealization> pendingSettle = new List<CharaRealization>();
        readonly HashSet<string> partFaults = new HashSet<string>(StringComparer.Ordinal);
        bool structureDirty, scaleListDirty, applying, disposed;

        public SceneRuntime(SceneSession session, RuntimeServices services, Transform parent = null, bool? addDrivers = null)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            Services = services ?? throw new ArgumentNullException(nameof(services));
            Root = new GameObject(RootName);
            if (parent) Root.transform.SetParent(parent, false);
            NodesRoot = Child(Root.transform, "Nodes");
            GraveyardRoot = Child(Root.transform, "Graveyard");
            GraveyardRoot.gameObject.SetActive(false);
            ViewRoot = Child(Root.transform, "View");
            Graveyard = new Graveyard(GraveyardRoot);
            Scale = new ScaleEnforcer(this);
            Ops = new SceneOps(this);
            Capture = new SaveCapture(this);
            ItemMotion.Register();
            if (addDrivers ?? Application.isPlaying)
            {
                Root.AddComponent<SceneRuntimeDriver>().Runtime = this;
                Root.AddComponent<ItemMotionDriver>().Runtime = this;
                Root.AddComponent<SceneRuntimeLateDriver>().Runtime = this;
            }
            if (SceneEvents.Log == null) SceneEvents.Log = m => Debug.LogWarning(m);
            SceneShaderTime.Source = Clock;   // the latest runtime's clock drives the shader time (one app runtime)
            var ev = session.Events;
            ev.Changed += OnChanged;
            ev.Cleared += OnCleared;
            ev.Loaded += OnLoaded;
            ev.NodeForgotten += OnForgotten;
            ev.Saving += OnSaving;
            if (session.Model.Count > 0) RealizeAll();
        }

        static Transform Child(Transform parent, string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        public SceneSession Session { get; }
        public RuntimeServices Services { get; }
        public SceneRuntimeSettings Settings => Services.Settings;
        public SceneModel Model => Session.Model;
        public SceneFindings Findings => Session.Findings;

        /// <summary>The runtime root ("HS2Next Scene").</summary>
        public GameObject Root { get; }
        public Transform NodesRoot { get; }
        public Transform GraveyardRoot { get; }
        /// <summary>Parent of the view camera (Package C).</summary>
        public Transform ViewRoot { get; }
        public Graveyard Graveyard { get; }
        public ScaleEnforcer Scale { get; }
        /// <summary>Operations that need Unity around a bus operation.</summary>
        public SceneOps Ops { get; }
        /// <summary>Runtime-owned values written into the model before save, duplicate and delete.</summary>
        public SaveCapture Capture { get; }
        public bool IsDisposed => disposed;

        /// <summary>Raised once per character build or rebuild after its children were attached (the 5.1 plugin event).</summary>
        public event Action<CharaRealization> CharaBuilt;

        /// <summary>Counters of the last <see cref="Apply"/> (tests, diagnostics).</summary>
        public int LastBuilt { get; private set; }
        public int LastRevived { get; private set; }
        public int LastBuried { get; private set; }
        public int LastEvicted { get; private set; }

        // ------------------------------------------------------------ scene clock (Phase 3.6)

        /// <summary>The scene clock: every scene time consumer takes its delta from <see cref="SceneClock.FrameDt"/>.</summary>
        public SceneClock Clock { get; } = new SceneClock();
        bool tickPending, stepping;
        Func<float> frameDtSource;

        /// <summary>The delta source of the scene's characters (CharaMotion.DeltaSource): the clock's FrameDt.</summary>
        public Func<float> FrameDtSource => frameDtSource ??= () => Clock.FrameDt;
        /// <summary>The global pause (not saved; new / load reset it).</summary>
        public bool Paused => Clock.Paused;
        /// <summary>True while <see cref="Step"/> runs its frames.</summary>
        public bool Stepping => stepping;
        /// <summary>Steps run since the clock was last reset (scene.pause / scene.step replies).</summary>
        public long StepsTaken => Clock.Steps;
        /// <summary>The dt the map scripts take in LateApply: the clock's FrameDt, 0 inside a step (the step steps them).</summary>
        public float LateScriptDt => stepping ? 0f : Clock.FrameDt;
        /// <summary>The longest step call: n ≤ 10,000 (as chara.motion step).</summary>
        public const int MaxSteps = 10000;

        /// <summary>Raised after the global pause changed (the new value).</summary>
        public event Action<bool> PausedChanged;
        /// <summary>Raised after a <see cref="Step"/> call (n, dt).</summary>
        public event Action<int, float> Stepped;

        /// <summary>
        /// Begins a clock frame (once per player frame, before its Apply: the play-mode driver; Tick with ManualStepping): the
        /// clock's FrameDt becomes <paramref name="rawDt"/> (0 while paused) and the next Apply ticks the tick parts once.
        /// </summary>
        public void BeginFrame(float rawDt)
        {
            if (disposed) return;
            Clock.BeginFrame(rawDt);
            tickPending = true;
        }

        /// <summary>Switches the global pause (Time.timeScale is never touched). False when nothing changed.</summary>
        public bool SetPaused(bool paused)
        {
            if (disposed || Clock.Paused == paused) return false;
            Clock.Paused = paused;
            for (int i = 0; i < parts.Count; i++)
                if (parts[i] is IScenePauseListener l)
                {
                    var p = parts[i];
                    try { l.PausedChanged(paused); }
                    catch (Exception e)
                    {
                        if (partFaults.Add(p.GetType().Name + ".PausedChanged|" + e.GetType().Name))
                            Debug.LogWarning("scene runtime: part " + p.GetType().Name + ".PausedChanged threw: " + e);
                    }
                }
            RaisePausedChanged(paused);
            return true;
        }

        void RaisePausedChanged(bool paused)
        {
            var h = PausedChanged;
            if (h == null) return;
            foreach (Action<bool> d in h.GetInvocationList())
            {
                try { d(paused); }
                catch (Exception e) { Debug.LogWarning("scene runtime: PausedChanged handler threw: " + e); }
            }
        }

        /// <summary>
        /// Runs <paramref name="n"/> frames of <paramref name="dt"/> while paused, synchronously, in the frame order: Apply, the
        /// tick parts, every built character (all with ManualStepping, else those with AutoStep) in pre-order, the items, the
        /// map scripts (not with ManualStepping, as a Tick), LateApply. Refused (false) while running, for n outside
        /// 1…<see cref="MaxSteps"/> or dt outside (0, 1].
        /// </summary>
        public bool Step(int n, float dt)
        {
            if (disposed || !Clock.Paused || stepping || n < 1 || n > MaxSteps || !(dt > 0f) || dt > 1f) return false;
            stepping = true;
            try { Clock.Step(n, dt, StepOnce); }
            finally { stepping = false; }
            var h = Stepped;
            if (h != null)
                foreach (Action<int, float> d in h.GetInvocationList())
                {
                    try { d(n, dt); }
                    catch (Exception e) { Debug.LogWarning("scene runtime: Stepped handler threw: " + e); }
                }
            return true;
        }

        void StepOnce(float dt)
        {
            Apply();
            TickParts(dt);
            bool manual = Settings.ManualStepping;
            foreach (var c in Charas())
                if (c.State == RealizationState.Built && c.Motion && !c.InGraveyard && (manual || c.Motion.AutoStep)) c.Motion.Step(dt);
            StepItems(dt);
            if (!manual) GetPart<MapRealization>()?.StepScripts(dt);
            LateApply();
        }

        void TickParts(float dt)
        {
            for (int i = 0; i < parts.Count; i++)
            {
                if (!(parts[i] is ISceneTickPart t)) continue;
                var p = parts[i];
                try { t.Tick(dt); }
                catch (Exception e)
                {
                    if (partFaults.Add(p.GetType().Name + ".Tick|" + e.GetType().Name))
                        Debug.LogWarning("scene runtime: part " + p.GetType().Name + ".Tick threw: " + e);
                }
            }
        }

        /// <summary>New / load: the clock back to running at time 0 (the pause is runtime state, R20).</summary>
        void ResetClock()
        {
            bool was = Clock.Paused;
            if (was) SetPaused(false);
            Clock.Reset();
            tickPending = false;
        }

        // ------------------------------------------------------------ parts

        public IReadOnlyList<ISceneRuntimePart> Parts => parts;

        /// <summary>
        /// Registers a part (called after the node work in Apply / LateApply, in registration order). <paramref name="onDispose"/>
        /// (or the part's own <see cref="IDisposable.Dispose"/>) runs when the runtime is disposed, after every part's Clear, in
        /// reverse registration order: for objects or global state a part owns outside the runtime root.
        /// </summary>
        public void AddPart(ISceneRuntimePart p, Action onDispose = null)
        {
            if (p == null || parts.Contains(p)) return;
            parts.Add(p);
            if (onDispose != null) partDisposers[p] = onDispose;
            p.Attach(this);
        }

        /// <summary>Unregisters a part; the caller owns it afterwards (nothing is cleared or disposed).</summary>
        public bool RemovePart(ISceneRuntimePart p)
        {
            partDisposers.Remove(p);
            return parts.Remove(p);
        }

        /// <summary>
        /// Adds the scene-wide parts of 2.2 in their required order: MapRealization, SceneViewCamera,
        /// SceneCharaLight (the light follows the view camera, so it comes after it). The camera and the light are disposed
        /// with the runtime (the light also when it sits under an adopted app camera). Returns this runtime.
        /// </summary>
        public SceneRuntime AddStandardParts()
        {
            if (GetPart<MapRealization>() == null) AddPart(new MapRealization());
            if (GetPart<SceneViewCamera>() == null)
            {
                var cam = new SceneViewCamera();
                AddPart(cam, cam.Destroy);
            }
            if (GetPart<SceneCharaLight>() == null)
            {
                var light = new SceneCharaLight();
                AddPart(light, light.Destroy);
            }
            // Phase 3.3 parts, one line each, in this order:
            if (GetPart<StudioLights>() == null) { var sl = new StudioLights(); AddPart(sl, sl.Destroy); }
            if (GetPart<SceneEffects>() == null) { var fx = new SceneEffects(); AddPart(fx, fx.Dispose); }
            if (GetPart<SceneSky>() == null) { var sky = new SceneSky(); AddPart(sky, sky.Dispose); }   // with AmbientProbe (package S)
            if (GetPart<SceneSound>() == null) AddPart(new SceneSound());
            // Phase 3.6 tick parts, one line each: K's freeze of the Unity-time consumers;
            // package R adds its RoutePlayback line right below (shared-file rule)
            if (GetPart<SceneTimeFreeze>() == null) AddPart(new SceneTimeFreeze());
            if (GetPart<RoutePlayback>() == null) { var rp = new RoutePlayback(); AddPart(rp, rp.Dispose); }   // Phase 3.6 package R: route playback
            if (GetPart<RuntimeUpkeep>() == null) AddPart(new RuntimeUpkeep());   // the settle stamp, the catalog's eviction and UnloadUnusedAssets, frame times (Phase 5.2)
            if (GetPart<SceneImages>() == null) { var im = new SceneImages(); AddPart(im, im.Dispose); }   // Phase 3.6 package C (after SceneViewCamera)
            return this;
        }

        /// <summary>
        /// The runtime an application uses: <see cref="AddStandardParts"/> added, drivers in play mode and the player, and the
        /// HS2 card importer registered (cards added to the scene are imported at once).
        /// </summary>
        public static SceneRuntime CreateDefault(SceneSession session, RuntimeServices services, Transform parent = null)
        {
            ImportSetup.Register(services);
            return new SceneRuntime(session, services, parent).AddStandardParts();
        }

        public T GetPart<T>() where T : class, ISceneRuntimePart
        {
            foreach (var p in parts) if (p is T t) return t;
            return null;
        }

        void ForParts(Action<ISceneRuntimePart> a, string what)
        {
            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                try { a(p); }
                catch (Exception e)
                {
                    if (partFaults.Add(p.GetType().Name + "." + what + "|" + e.GetType().Name))
                        Debug.LogWarning("scene runtime: part " + p.GetType().Name + "." + what + " threw: " + e);
                }
            }
        }

        // ------------------------------------------------------------ lookup

        /// <summary>The realization of a node (in the model or buried in the graveyard), or null.</summary>
        public NodeRealization Find(string id) => id != null && reals.TryGetValue(id, out var r) ? r : null;
        public CharaRealization FindChara(string id) => Find(id) as CharaRealization;
        public ItemRealization FindItem(string id) => Find(id) as ItemRealization;
        public IEnumerable<NodeRealization> Realizations => reals.Values;
        public int RealizationCount => reals.Count;

        /// <summary>The holder transform of a realized node, or null.</summary>
        public Transform HolderOf(string id) => Find(id)?.Holder;

        /// <summary>Built characters of the model in pre-order.</summary>
        public IEnumerable<CharaRealization> Charas()
        {
            foreach (var n in Model.PreOrder())
                if (reals.TryGetValue(n.Id, out var r) && r is CharaRealization c) yield return c;
        }

        /// <summary>Built items of the model in pre-order (outside the graveyard).</summary>
        public IEnumerable<ItemRealization> Items()
        {
            foreach (var n in Model.PreOrder())
                if (reals.TryGetValue(n.Id, out var r) && r is ItemRealization it && it.Node == n && it.State == RealizationState.Built && !it.InGraveyard) yield return it;
        }

        /// <summary>True when the realization's node is the node of that id in the model.</summary>
        public bool IsInModel(NodeRealization r) => r != null && r.Node != null && r.Node.InModel && Model.Find(r.NodeId) == r.Node;

        /// <summary>
        /// The transform a child of <paramref name="parentId"/> at <paramref name="point"/> hangs under (null parent = the
        /// Nodes root); the fallbacks apply. Used by MoveKeepWorld.
        /// </summary>
        public Transform AttachTransformFor(string parentId, string point)
        {
            if (parentId == null) return NodesRoot;
            var pr = Find(parentId);
            if (pr == null || !pr.HasHolder) return NodesRoot;
            return pr.ChildAttach(point, out _) ?? pr.Holder;
        }

        // ------------------------------------------------------------ findings

        public void Report(FindingSeverity s, string code, string nodeId, string message) => Findings.Add(s, code, nodeId, message);

        /// <summary>Reports (code, node) once until the realization is cleared.</summary>
        public void ReportOnce(FindingSeverity s, string code, string nodeId, string message)
        {
            if (reported.Add(code + "|" + nodeId)) Findings.Add(s, code, nodeId, message);
        }

        // ------------------------------------------------------------ events

        void OnChanged(SceneChangeBatch b)
        {
            if (disposed) return;
            ForParts(p => p.OnChanged(b), "OnChanged");
            bool capture = b.Origin == ChangeOrigin.Capture;
            foreach (var c in b.Changes)
            {
                switch (c.Kind)
                {
                    case SceneChangeKind.NodeAdded:
                    case SceneChangeKind.NodeRemoved:
                        structureDirty = true;
                        break;
                    case SceneChangeKind.NodeMoved:
                        structureDirty = true;
                        transformDirty.Add(c.NodeId);
                        visibilityDirty.Add(c.NodeId);
                        break;
                    case SceneChangeKind.NodeChanged:
                        if (capture || c.Path == null) break;   // captured values come from Unity: nothing to apply back
                        NodeChanged(c.NodeId, c.Path);
                        break;
                    case SceneChangeKind.AppearanceChanged:
                        if (capture) break;   // representation change only (import cache)
                        Dirty(c.NodeId, "appearance");
                        break;
                }
            }
        }

        void NodeChanged(string id, string path)
        {
            if (FieldPath.Overlaps(path, "transform"))
            {
                transformDirty.Add(id);
                if (FieldPath.Overlaps(path, "transform.inheritScale")) scaleListDirty = true;
            }
            if (FieldPath.Overlaps(path, "visible")) visibilityDirty.Add(id);
            if (FieldPath.Overlaps(path, "name")) nameDirty.Add(id);
            if (FieldPath.IsPrefixOf("ui", path) || FieldPath.IsPrefixOf("transform", path) || FieldPath.IsPrefixOf("visible", path) || FieldPath.IsPrefixOf("name", path)) return;
            Dirty(id, path);
        }

        void Dirty(string id, string path)
        {
            if (!dirtyPaths.TryGetValue(id, out var set)) dirtyPaths[id] = set = new HashSet<string>(StringComparer.Ordinal);
            set.Add(path);
        }

        void OnCleared()
        {
            if (disposed) return;
            ResetClock();
            ClearRealization();
            Services.Cards.Clear();
        }

        void OnLoaded(SceneLoadedArgs a)
        {
            if (disposed) return;
            ResetClock();
            RealizeAll();
        }

        void OnForgotten(string id)
        {
            if (disposed) return;
            Services.Cards.Unreference(id);
            if (!reals.TryGetValue(id, out var r) || IsInModel(r)) return;
            var e = Graveyard.TakeTop(r);
            if (e != null) { DestroyEntry(e); return; }
            if (r.HasHolder) r.DestroyHolder();
            reals.Remove(id);
        }

        void OnSaving()
        {
            if (disposed) return;
            try
            {
                Apply();
                Capture.CaptureAll();
            }
            catch (Exception e) { Debug.LogWarning("scene runtime: capture before save failed: " + e); }
        }

        // ------------------------------------------------------------ realize all / clear

        /// <summary>Clears the previous realization and schedules every node and the scene parts.</summary>
        public void RealizeAll()
        {
            ClearRealization();
            structureDirty = true;
            scaleListDirty = true;
            ForParts(p => p.OnLoadedRealized(), "OnLoadedRealized");
        }

        void ClearRealization()
        {
            foreach (var r in reals.Values)
                try { r.ReleaseContent(); } catch (Exception e) { Debug.LogWarning("scene runtime: release failed: " + e.Message); }
            for (int i = NodesRoot.childCount - 1; i >= 0; i--) UnityObjects.Destroy(NodesRoot.GetChild(i).gameObject);
            for (int i = GraveyardRoot.childCount - 1; i >= 0; i--) UnityObjects.Destroy(GraveyardRoot.GetChild(i).gameObject);
            // holders parked at the runtime root by an interrupted rebuild
            for (int i = Root.transform.childCount - 1; i >= 0; i--)
            {
                var c = Root.transform.GetChild(i);
                if (c.GetComponent<SceneNodeHolder>() != null) UnityObjects.Destroy(c.gameObject);
            }
            reals.Clear();
            dirtyPaths.Clear();
            transformDirty.Clear();
            visibilityDirty.Clear();
            nameDirty.Clear();
            pendingBuilt.Clear();
            pendingSettle.Clear();
            reported.Clear();
            Graveyard.Clear();
            Scale.Clear();
            structureDirty = false;
            ForParts(p => p.Clear(), "Clear");
        }

        // ------------------------------------------------------------ apply

        /// <summary>Processes pending work. Cheap when nothing is dirty.</summary>
        public void Apply()
        {
            if (disposed || applying) return;
            applying = true;
            try
            {
                builtThisApply.Clear();
                LastBuilt = LastRevived = LastBuried = LastEvicted = 0;
                if (structureDirty) Reconcile();
                if (dirtyPaths.Count > 0) ApplyDirtyPaths();
                CharaEditFlush.Run(this);   // Phase 4.0 package C: the Maker edit flush, model pre-order
                if (transformDirty.Count > 0) ApplyTransforms();
                if (visibilityDirty.Count > 0) ApplyVisibilityDirty();
                if (nameDirty.Count > 0) ApplyNames();
                if (scaleListDirty)
                {
                    scaleListDirty = false;
                    Scale.Rebuild(Model.PreOrder(), reals);
                }
                RaiseCharaBuilt();
                if (pendingSettle.Count > 0) RequestSettles();
            }
            finally { applying = false; }
            ForParts(p => p.Apply(), "Apply");
            // the frame's tick (once per clock frame; a step ticks its parts itself)
            if (tickPending && !stepping)
            {
                tickPending = false;
                TickParts(Clock.FrameDt);
            }
        }

        /// <summary><see cref="Apply"/> for callers that read realized state right after a command (bridge handlers).</summary>
        public void ApplyNow() => Apply();

        /// <summary>World-scale enforcement, then the parts.</summary>
        public void LateApply()
        {
            if (disposed) return;
            Scale.Enforce();
            ForParts(p => p.LateApply(), "LateApply");
        }

        /// <summary>
        /// Batch helper: with ManualStepping begin a clock frame of <paramref name="dt"/> (<see cref="BeginFrame"/>); Apply (the
        /// tick parts with the clock's FrameDt); with ManualStepping step every built character by FrameDt in pre-order, then
        /// every built item (<see cref="StepItems"/>); LateApply. Not paused, FrameDt = dt (the 2.2 behaviour); paused, the
        /// stages run with dt 0 (edits applied, nothing advances).
        /// </summary>
        public void Tick(float dt)
        {
            bool manual = Settings.ManualStepping;
            if (manual) BeginFrame(dt);
            Apply();
            if (manual)
            {
                float fdt = Clock.FrameDt;
                foreach (var c in Charas())
                    if (c.State == RealizationState.Built && c.Motion && !c.InGraveyard) c.Motion.Step(fdt);
                StepItems(fdt);
            }
            LateApply();
        }

        /// <summary>
        /// Steps the motion (animation, item FK, dynamic bones) of every built item outside the graveyard in model pre-order (a
        /// parent item before its children), after the characters. Called by ItemMotionDriver,
        /// by <see cref="Tick"/> with ManualStepping and by test hosts' frames. Items without motion are skipped.
        /// </summary>
        public void StepItems(float dt)
        {
            if (disposed) return;
            foreach (var n in Model.PreOrder())
            {
                if (!reals.TryGetValue(n.Id, out var r) || !(r is ItemRealization it) || it.Node != n) continue;
                if (it.State != RealizationState.Built || !(it.Motion is ItemMotion m) || it.InGraveyard) continue;
                try { m.Step(dt); }
                catch (Exception e)
                {
                    if (partFaults.Add("item.step|" + n.Id + "|" + e.GetType().Name))
                        Debug.LogWarning("scene runtime: stepping item " + n.Id + " threw: " + e);
                }
            }
        }

        /// <summary>Switches manual stepping for every built character (CharaMotion.AutoStep = !manual).</summary>
        public void SetManualStepping(bool manual)
        {
            Settings.ManualStepping = manual;
            foreach (var r in reals.Values)
                if (r is CharaRealization c && c.Motion) c.Motion.AutoStep = !manual;
        }

        /// <summary>Writes the runtime-owned values of every character and item into the model.</summary>
        public void CaptureNow()
        {
            Apply();
            Capture.CaptureAll();
        }

        // ------------------------------------------------------------ structural pass

        void Reconcile()
        {
            structureDirty = false;
            scaleListDirty = true;
            var model = Model;

            // 1. holders of nodes that left the model go to the graveyard (top-most of each removed subtree)
            List<NodeRealization> removed = null;
            foreach (var r in reals.Values) if (!IsInModel(r)) (removed ??= new List<NodeRealization>()).Add(r);
            if (removed != null)
            {
                var removedSet = new HashSet<NodeRealization>(removed);
                foreach (var r in removed)
                {
                    if (!r.HasHolder) { reals.Remove(r.NodeId); continue; }
                    if (r.InGraveyard) continue;
                    bool top = true;
                    for (var p = r.Holder.parent; p && p != NodesRoot && p != Root.transform; p = p.parent)
                    {
                        var h = p.GetComponent<SceneNodeHolder>();
                        if (h != null && h.NodeId != null && reals.TryGetValue(h.NodeId, out var pr) && pr.Holder == p && removedSet.Contains(pr)) { top = false; break; }
                    }
                    if (!top) continue;
                    Graveyard.Bury(r, reals);
                    LastBuried++;
                }
            }

            // 2. model pre-order: build new nodes, revive buried ones, move holders whose attach transform changed
            var notBuilt = new HashSet<string>(StringComparer.Ordinal);
            foreach (var n in model.PreOrder())
            {
                string id = n.Id;
                reals.TryGetValue(id, out var r);
                bool skip = n.Kind == NodeKind.Opaque || (n.Parent != null && notBuilt.Contains(n.Parent.Id));
                if (skip)
                {
                    notBuilt.Add(id);
                    if (r != null && (!(r is NotBuiltRealization) || r.Node != n)) { DestroyRealization(r); r = null; }
                    if (r == null) reals[id] = new NotBuiltRealization(this, n);
                    if (n.Kind == NodeKind.Opaque && (n.Parent == null || !notBuilt.Contains(n.Parent.Id)))
                        ReportOnce(FindingSeverity.Info, "scene.unknownKindSubtree", id, "node kind \"" + n.Doc.Kind + "\" is not known: it and its subtree are not built");
                    continue;
                }
                if (r != null && (r.Node != n || r is NotBuiltRealization || !r.HasHolder)) { DestroyRealization(r); r = null; }
                if (r == null) { BuildNode(n); continue; }
                bool buried = r.InGraveyard;
                if (buried)
                {
                    BeforeRevive(r);
                    Graveyard.Unbury(r);
                }
                var expected = AttachParentFor(n);
                if (r.Holder.parent != expected)
                {
                    r.Holder.SetParent(expected, false);
                    transformDirty.Add(id);
                    visibilityDirty.Add(id);
                    r.OnReparented();
                }
                if (buried)
                {
                    transformDirty.Add(id);
                    visibilityDirty.Add(id);
                    LastRevived++;
                }
            }

            // 3. graveyard limits
            foreach (var e in Graveyard.TakeOverLimit(Settings.GraveyardMaxCharas, Settings.GraveyardMaxNodes))
            {
                DestroyEntry(e);
                LastEvicted++;
            }
        }

        /// <summary>
        /// A buried subtree is about to be re-activated (undo of delete, redo of add): its characters' dynamic-bone chain bones get
        /// their rest pose back first (DynamicBoneChain.ResetBeforeUpdate), because re-activating the holder re-enables the Animator,
        /// which binds the current local values of the bones it drives as its defaults; bones left at the last physics write-back
        /// would become the chain's rest pose (verified by UiOpsTest O12).
        /// </summary>
        void BeforeRevive(NodeRealization top)
        {
            if (!top.HasHolder) return;
            foreach (var h in top.Holder.GetComponentsInChildren<SceneNodeHolder>(true))
                if (h.NodeId != null && reals.TryGetValue(h.NodeId, out var m) && m.Holder == h.transform && m is CharaRealization c)
                    c.BeforeRevive();
        }

        void DestroyRealization(NodeRealization r)
        {
            if (r.HasHolder)
            {
                Graveyard.TakeTop(r);
                r.DestroyHolder();
            }
            if (reals.TryGetValue(r.NodeId, out var cur) && cur == r) reals.Remove(r.NodeId);
        }

        void DestroyEntry(Graveyard.Entry e)
        {
            var top = e.Top;
            if (top.HasHolder)
                foreach (var h in top.Holder.GetComponentsInChildren<SceneNodeHolder>(true))
                {
                    if (h.NodeId == null || !reals.TryGetValue(h.NodeId, out var r) || r.Holder != h.transform || IsInModel(r)) continue;
                    try { r.ReleaseContent(); } catch (Exception ex) { Debug.LogWarning("scene runtime: release failed: " + ex.Message); }
                    if (r != top) reals.Remove(r.NodeId);
                }
            top.DestroyHolder();
            if (reals.TryGetValue(top.NodeId, out var cur) && cur == top) reals.Remove(top.NodeId);
        }

        NodeRealization Create(TreeNode n)
        {
            switch (n.Kind)
            {
                case NodeKind.Chara: return new CharaRealization(this, n);
                case NodeKind.Item: return new ItemRealization(this, n);
                case NodeKind.Light: return new LightRealization(this, n);
                case NodeKind.Folder: return new FolderRealization(this, n);
                case NodeKind.Camera: return new CameraRealization(this, n);
                case NodeKind.Route: return new RouteRealization(this, n);
                default: return new NotBuiltRealization(this, n);
            }
        }

        NodeRealization BuildNode(TreeNode n)
        {
            var r = Create(n);
            reals[n.Id] = r;
            r.CreateHolder(AttachParentFor(n));
            ApplyTransform(r);
            BuildContentGuarded(r);
            r.ApplyVisibility(n.EffectiveVisible);
            builtThisApply.Add(n.Id);
            LastBuilt++;
            return r;
        }

        void BuildContentGuarded(NodeRealization r)
        {
            try { r.BuildContent(); }
            catch (Exception e)
            {
                r.State = RealizationState.Failed;
                r.Detail = e.GetType().Name + ": " + e.Message;
                string code = r.Kind == NodeKind.Chara ? "chara.buildFailed" : r.Kind == NodeKind.Item ? "item.loadFailed" : r.Kind == NodeKind.Light ? "light.loadFailed" : "scene.realizeFailed";
                Report(FindingSeverity.Error, code, r.NodeId, "realization threw: " + r.Detail);
                Debug.LogException(e);
            }
        }

        /// <summary>The holder parent of a node: its parent's attach transform (fallbacks reported), or the Nodes root.</summary>
        Transform AttachParentFor(TreeNode n)
        {
            if (n.Parent == null) return NodesRoot;
            if (!reals.TryGetValue(n.Parent.Id, out var pr) || !pr.HasHolder) return NodesRoot;
            var t = pr.ChildAttach(n.Point, out var note);
            if (!note.IsEmpty) ReportOnce(note.Severity, note.Code, n.Id, note.Message);
            return t ? t : pr.Holder;
        }

        /// <summary>Rebuild protocol: park the children, rebuild the content, re-attach.</summary>
        internal void RebuildContent(NodeRealization r)
        {
            if (!r.HasHolder) return;
            var kids = r.Node.Children;
            foreach (var k in kids)
                if (reals.TryGetValue(k.Id, out var kr) && kr.HasHolder) kr.Holder.SetParent(Root.transform, true);
            try { r.ReleaseContent(); } catch (Exception e) { Debug.LogWarning("scene runtime: release failed: " + e.Message); }
            r.DestroyContent();
            r.State = RealizationState.Pending;
            r.Detail = null;
            BuildContentGuarded(r);
            r.ApplyVisibility(r.Node.EffectiveVisible);
            foreach (var k in kids)
                if (reals.TryGetValue(k.Id, out var kr) && kr.HasHolder)
                {
                    kr.Holder.SetParent(AttachParentFor(k), false);
                    transformDirty.Add(k.Id);
                    visibilityDirty.Add(k.Id);
                }
            scaleListDirty = true;
        }

        internal void QueueCharaBuilt(CharaRealization c)
        {
            if (!pendingBuilt.Contains(c)) pendingBuilt.Add(c);
        }

        /// <summary>A character changed while paused (CharaRealization.WantSettle): its motion settles once no drag is open.</summary>
        internal void QueueSettle(CharaRealization c)
        {
            if (c != null && !pendingSettle.Contains(c)) pendingSettle.Add(c);
        }

        /// <summary>Characters waiting for a settle (tests).</summary>
        public int PendingSettles => pendingSettle.Count;

        // During a drag (an open bus transaction) the requests wait: the drag's frames carry the chains as they are and the
        // physics settle once, after the commit (the next Apply without an open transaction).
        void RequestSettles()
        {
            if (Session?.Commands?.ActiveTransaction != null) return;
            foreach (var c in pendingSettle)
                if (c.State == RealizationState.Built && c.Motion && !c.InGraveyard && IsInModel(c)) c.Motion.RequestSettle();
            pendingSettle.Clear();
        }

        void RaiseCharaBuilt()
        {
            if (pendingBuilt.Count == 0) return;
            var l = pendingBuilt.ToArray();
            pendingBuilt.Clear();
            var h = CharaBuilt;
            if (h == null) return;
            foreach (var c in l)
                foreach (Action<CharaRealization> d in h.GetInvocationList())
                {
                    try { d(c); }
                    catch (Exception e) { Debug.LogWarning("scene runtime: CharaBuilt handler threw: " + e); }
                }
        }

        // ------------------------------------------------------------ dirty paths, transforms, visibility, names

        void ApplyDirtyPaths()
        {
            var batch = dirtyPaths;
            dirtyPaths = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var kv in batch)
            {
                if (builtThisApply.Contains(kv.Key)) continue;
                if (!reals.TryGetValue(kv.Key, out var r) || !r.HasHolder || !IsInModel(r)) continue;
                try { r.ApplyChanges(kv.Value); }
                catch (Exception e)
                {
                    Report(FindingSeverity.Error, "scene.realizeFailed", r.NodeId, "applying " + string.Join(", ", kv.Value) + " threw: " + e.GetType().Name + ": " + e.Message);
                    Debug.LogException(e);
                }
            }
        }

        void ApplyTransforms()
        {
            foreach (var n in Model.PreOrder())
            {
                if (transformDirty.Count == 0) break;
                if (!transformDirty.Remove(n.Id)) continue;
                if (reals.TryGetValue(n.Id, out var r) && r.HasHolder) ApplyTransform(r);
            }
            transformDirty.Clear();
        }

        /// <summary>pos, rot and scale of the holder from the model.</summary>
        internal void ApplyTransform(NodeRealization r)
        {
            var h = r.Holder;
            var t = r.Node.Doc.Transform;
            if (!h || t == null) return;
            h.localPosition = UnityObjects.V(t.Pos);
            h.localRotation = Quaternion.Euler(UnityObjects.V(t.Rot));
            Scale.Apply(r);
            if (r is CharaRealization c) c.OnTransformApplied();
        }

        void ApplyVisibilityDirty()
        {
            var done = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in visibilityDirty)
            {
                var n = Model.Find(id);
                if (n == null) continue;
                foreach (var m in Model.Subtree(n))
                {
                    if (!done.Add(m.Id)) continue;
                    if (reals.TryGetValue(m.Id, out var r) && r.HasHolder && r.Node == m) r.ApplyVisibility(m.EffectiveVisible);
                }
            }
            visibilityDirty.Clear();
        }

        void ApplyNames()
        {
            foreach (var id in nameDirty)
                if (reals.TryGetValue(id, out var r) && r.HasHolder) r.HolderObject.name = r.HolderName();
            nameDirty.Clear();
        }

        // ------------------------------------------------------------ dispose

        public void Dispose()
        {
            if (disposed) return;
            var ev = Session.Events;
            ev.Changed -= OnChanged;
            ev.Cleared -= OnCleared;
            ev.Loaded -= OnLoaded;
            ev.NodeForgotten -= OnForgotten;
            ev.Saving -= OnSaving;
            ClearRealization();
            if (SceneShaderTime.Source == Clock) SceneShaderTime.Source = null;
            disposed = true;
            for (int i = parts.Count - 1; i >= 0; i--)
            {
                var p = parts[i];
                try
                {
                    if (partDisposers.TryGetValue(p, out var d)) d();
                    else if (p is IDisposable disposable) disposable.Dispose();
                }
                catch (Exception e) { Debug.LogWarning("scene runtime: disposing part " + p.GetType().Name + " threw: " + e); }
            }
            partDisposers.Clear();
            if (Root) Object.DestroyImmediate(Root);
        }
    }
}
