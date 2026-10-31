// The UI's view of the app: one per app, created by UiHost. It holds the app, session,
// runtime and operations layer, the selection (the session's SelectionModel) and the sub-selection (StudioOps holds it;
// this wraps it, no second copy), the view state the UI owns (tool, space, pivot, snap), localisation, keymap, options,
// findings, the registries (panels, actions, inspector sections), the binding hub, the shell (regions, dialogs, toasts)
// and the viewport. It subscribes to the session's events once and fans them out (SceneChanged, SceneCleared,
// SceneLoaded, HistoryChanged, SelectionChanged, SubSelectionChanged, NodeForgotten, ViewStateChanged).
using System;
using System.Collections.Generic;
using HS2Next.App;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Binding;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;
using UnityEngine;

namespace HS2Next.UI
{
    public enum GizmoTool { Select, Move, Rotate, Scale }
    /// <summary>Auto = vanilla (move in world space, rotate / scale in local space).</summary>
    public enum GizmoSpace { Auto, World, Local, Parent }
    public enum GizmoPivot { Individual, SelectionCentre }

    public sealed class UiContext
    {
        public AppCore App { get; }
        public SceneSession Session => App.Session;
        public SceneRuntime Runtime => App.Runtime;
        public StudioOps Ops => App.Ops;
        public SceneModel Model => Session.Model;
        /// <summary>The node selection (ordered ids, primary first; view state).</summary>
        public SelectionModel Selection => Session.Selection;

        /// <summary>The sub-object address ("&lt;id&gt;/fk/&lt;bone&gt;", "&lt;id&gt;/ik/&lt;Target&gt;", "&lt;id&gt;/look") or null.
        /// Read / written through StudioOps (the bridge's `select sub` shares it); setting one makes its character the sole
        /// node selection, null clears it.</summary>
        public string SubSelection
        {
            get => Ops?.SubSelection;
            set
            {
                if (Ops == null) return;
                if (value == null) { if (Ops.SubSelection != null) Ops.Select(Selection.Ids, false, null); }
                else if (StudioOps.TryParseSub(value, out var id, out _, out _)) ShowResult(Ops.Select(new[] { id }, false, value));
            }
        }

        // ---------------- view state owned by the UI (not in history)

        GizmoTool tool = GizmoTool.Move;
        GizmoSpace space = GizmoSpace.Auto;
        GizmoPivot pivot = GizmoPivot.Individual;
        bool snap;

        public GizmoTool Tool { get => tool; set { if (tool != value) { tool = value; RaiseView("tool"); } } }
        public GizmoSpace Space { get => space; set { if (space != value) { space = value; RaiseView("space"); } } }
        public GizmoPivot Pivot { get => pivot; set { if (pivot != value) { pivot = value; RaiseView("pivot"); } } }
        /// <summary>The toolbar snap toggle (Ctrl during a drag inverts it for that drag).</summary>
        public bool Snap { get => snap; set { if (snap != value) { snap = value; RaiseView("snap"); } } }

        /// <summary>A view-state member changed ("tool", "space", "pivot", "snap", "language", "preset", "options", "layout", …).</summary>
        public event Action<string> ViewStateChanged;
        internal void RaiseView(string what) => Guard(() => ViewStateChanged?.Invoke(what));

        // ---------------- services

        public Localizer L10n => HS2Next.UI.Core.L10n.Default;
        public UiOptions Options { get; }
        public UiKeymap Keys { get; }
        public UiRegistry Registry { get; } = new UiRegistry();
        public BindingHub Bindings { get; }
        public Dialogs Dialogs { get; internal set; }
        public Toasts Toasts { get; internal set; }
        public UiShell Shell { get; internal set; }
        /// <summary>Popup menus of the shell (null before it is built).</summary>
        public PopupMenus Menus => Shell?.Menus;
        public UiHost Host { get; internal set; }
        public UiFonts Fonts { get; internal set; }
        /// <summary>The viewport (element, camera controller, picking); null before the shell is built.</summary>
        public HS2Next.UI.Viewport.ViewportHost Viewport { get; internal set; }
        /// <summary>The scene file the session was loaded from or last saved to (null = untitled); the file flows set it.</summary>
        public string CurrentFile { get => currentFile; set { if (currentFile != value) { currentFile = value; RaiseView("file"); } } }
        string currentFile;
        /// <summary>The open UI edit (a drag transaction of a widget or the gizmo), or null; Esc cancels it.</summary>
        public UiEdit ActiveEdit { get; internal set; }

        /// <summary>UI findings (`ui.*` codes) go into the session's findings list (status bar, findings panel).</summary>
        public SceneFindings Findings => Session.Findings;

        // ---------------- fan-out of the session events

        public event Action<SceneChangeBatch> SceneChanged;
        public event Action SceneCleared;
        public event Action<SceneLoadedArgs> SceneLoaded;
        public event Action<HistoryChangedArgs> HistoryChanged;
        public event Action<SelectionChangedArgs> SelectionChanged;
        public event Action<string> SubSelectionChanged;
        public event Action<string> NodeForgotten;
        public event Action<SceneFinding> FindingAdded;

        bool disposed;

        internal UiContext(AppCore app)
        {
            App = app ?? throw new ArgumentNullException(nameof(app));
            Options = new UiOptions(this);
            Keys = new UiKeymap(this);
            Bindings = new BindingHub(this);
            var ev = Session.Events;
            ev.Changed += OnChanged;
            ev.Cleared += OnCleared;
            ev.Loaded += OnLoaded;
            ev.HistoryChanged += OnHistory;
            ev.SelectionChanged += OnSelection;
            ev.NodeForgotten += OnForgotten;
            Session.Findings.Added += OnFinding;
            Session.Findings.Removed += OnFinding;   // a removed finding (a rebuild's report replaced): the same refresh
            if (Ops != null) Ops.SubSelectionChanged += OnSub;
        }

        void OnChanged(SceneChangeBatch b) { Bindings.OnChanged(b); Guard(() => SceneChanged?.Invoke(b)); }
        void OnCleared() { Bindings.DirtyAll(); Guard(() => SceneCleared?.Invoke()); }
        void OnLoaded(SceneLoadedArgs a) { Bindings.DirtyAll(); Guard(() => SceneLoaded?.Invoke(a)); }
        void OnHistory(HistoryChangedArgs a) => Guard(() => HistoryChanged?.Invoke(a));
        void OnSelection(SelectionChangedArgs a) { Bindings.OnSelection(); Guard(() => SelectionChanged?.Invoke(a)); }
        void OnSub(string s) { Bindings.OnSelection(); Guard(() => SubSelectionChanged?.Invoke(s)); }
        void OnForgotten(string id) => Guard(() => NodeForgotten?.Invoke(id));
        void OnFinding(SceneFinding f) => Guard(() => FindingAdded?.Invoke(f));

        /// <summary>Runs a UI handler, logging (not throwing) its exception: a broken panel must not stop the others.</summary>
        public static void Guard(Action a)
        {
            if (a == null) return;
            foreach (var d in a.GetInvocationList())
            {
                try { ((Action)d)(); }
                catch (Exception e) { Debug.LogError("[ui] handler " + d.Method.DeclaringType?.Name + "." + d.Method.Name + " threw: " + e); }
            }
        }

        internal void Dispose()
        {
            if (disposed) return;
            disposed = true;
            var ev = Session?.Events;
            if (ev != null)
            {
                ev.Changed -= OnChanged;
                ev.Cleared -= OnCleared;
                ev.Loaded -= OnLoaded;
                ev.HistoryChanged -= OnHistory;
                ev.SelectionChanged -= OnSelection;
                ev.NodeForgotten -= OnForgotten;
            }
            if (Session != null) { Session.Findings.Added -= OnFinding; Session.Findings.Removed -= OnFinding; }
            if (Ops != null) Ops.SubSelectionChanged -= OnSub;
        }

        // ---------------- helpers for panels

        /// <summary>Options of a UI operation (Source User; label null = the op's own).</summary>
        public static ExecuteOptions User(string label = null, string mergeKey = null) =>
            new ExecuteOptions { Source = CommandSource.User, Label = label, MergeKey = mergeKey };

        /// <summary>Shows an operation's outcome: an error as an error toast (code and message), warnings as warning toasts.
        /// Returns r.</summary>
        public OpResult ShowResult(OpResult r)
        {
            if (r == null) return null;
            if (Toasts != null)
            {
                foreach (var w in r.Warnings) Toasts.Show(w, ToastKind.Warning);
                if (!r.Ok) Toasts.Show(L10n.Format("msg.opFailed", r.Code ?? "error", r.Message ?? ""), ToastKind.Error);
            }
            return r;
        }

        /// <summary>The ids of the selected nodes of a kind (null = any kind), primary first.</summary>
        public List<string> SelectedOfKind(NodeKind? kind)
        {
            var l = new List<string>();
            foreach (var id in Selection.Ids)
            {
                var n = Model.Find(id);
                if (n != null && (kind == null || n.Kind == kind.Value)) l.Add(id);
            }
            return l;
        }

        /// <summary>Starts a drag / batch edit (one history entry for everything the ops do until Commit; Esc cancels).</summary>
        public UiEdit BeginEdit(string label, string mergeKey = null) => UiEdit.Begin(this, label, mergeKey);
    }
}
