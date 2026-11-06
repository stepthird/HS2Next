// The plugin contract: the entry type a plugin implements and the optional interfaces of its lifecycle. A plugin is a
// folder <app>/Plugins/<guid>/ with manifest.json and the assemblies it lists; the host creates the entry type (its public
// parameterless constructor), calls Load with the plugin's context when the app starts, SceneLoaded after every new or
// loaded scene (and once for the initial scene), Update once per frame, ScenePost once per frame and once per scene step
// after all motion (experimental), and Unload when the app quits, when the plugin faults, or when it is disabled while the
// app runs. Enabling a disabled plugin while the app runs creates a new instance
// of the entry type from the assembly already loaded and calls its Load, then SceneLoaded (Initial, the open scene) and
// AppReady. Every call comes on the main thread. Part of the stable API (assembly HS2Next.Api).
namespace HS2Next.Api.Plugins
{
    /// <summary>
    /// The entry type of a plugin. Implemented by plugins. Load is called once per instance: when the app starts (and per
    /// Editor play session with a domain reload), and on a new instance after the plugin was disabled and enabled again
    /// while the app runs. Every registration (commands, ext handlers, event handlers, hooks, forms) belongs there, and
    /// everything registered through the context is removed by the host after Unload returns. The assembly, its static
    /// state and the objects the plugin created stay in the process across a disable, so Unload must undo every effect
    /// outside the context (see <see cref="Unload"/>); a replaced assembly loads only after a restart.
    /// </summary>
    public interface IPlugin
    {
        /// <summary>Registers the plugin's commands, ext handlers and event handlers. A throw marks the plugin failed and removes what it registered.</summary>
        void Load(IPluginContext context);

        /// <summary>
        /// Called once before the host removes the plugin's registrations: at quit, at the end of an Editor play session,
        /// when the plugin becomes faulted, or when it is disabled while the app runs. The context's members (except Guid,
        /// Log and IsActive) stop working after it returns. Undo here every effect outside the context: static fields,
        /// objects and components the plugin created, engine callbacks it subscribed, threads and timers, files it holds
        /// open, deferred replies it still owes; a re-enabled plugin starts from the same loaded assembly.
        /// </summary>
        void Unload();
    }

    /// <summary>Per-frame work. Implemented by plugins (optional, on the entry type).</summary>
    public interface IPluginUpdate
    {
        /// <summary>
        /// Once per frame, in plugin load order, after the bridge has run this frame's requests and before the scene applies
        /// its changes: writes made here through <see cref="IPluginContext.Call"/> are realized in the same frame.
        /// </summary>
        void Update(in PluginFrame frame);
    }

    /// <summary>Scene loads. Implemented by plugins (optional, on the entry type).</summary>
    public interface IPluginSceneLoaded
    {
        /// <summary>
        /// After every new or loaded scene, and once with <see cref="SceneLoadKind.Initial"/> right after all plugins loaded and
        /// right after a runtime enable of this plugin (with the open scene's path and node count); the ext blocks of the scene
        /// were read before. The same notification as <see cref="IAppEvents.SceneLoaded"/>.
        /// </summary>
        void SceneLoaded(SceneLoadInfo info);
    }

    /// <summary>The scene post stage: a call after all motion. Implemented by plugins (optional, on the entry type).</summary>
    [Experimental("the scene post stage may change in any minor release")]
    public interface IPluginScenePost
    {
        /// <summary>
        /// Once per frame and once per scene step, in plugin load order, after every character, every item and the constraints
        /// have moved, before world-scale enforcement, the camera and the depth of field: what a plugin reads here is the final
        /// pose. In a step <see cref="PluginFrame.SceneDt"/> is the step's delta and <see cref="PluginFrame.Paused"/> is true.
        /// A runtime override slot of a node set here shows in this frame, one of a bone or an IK target at the next motion
        /// step; a bone written directly here is reported with <c>IUnityPluginServices.BoneWritten</c>. Calls through the context work
        /// here, and a scene edit made here is an edit like any other. A step started from inside this call runs no ScenePost.
        /// </summary>
        void ScenePost(in PluginFrame frame);
    }

    /// <summary>The frame a plugin's Update or ScenePost runs in.</summary>
    public readonly struct PluginFrame
    {
        /// <summary>Creates the frame information (the host does; plugins read it).</summary>
        public PluginFrame(int frame, float unscaledDt, float sceneDt, double sceneTime, bool paused)
        {
            Frame = frame;
            UnscaledDt = unscaledDt;
            SceneDt = sceneDt;
            SceneTime = sceneTime;
            Paused = paused;
        }

        /// <summary>The frame number (Unity's frame count in play mode and players).</summary>
        public int Frame { get; }
        /// <summary>Real seconds since the previous frame.</summary>
        public float UnscaledDt { get; }
        /// <summary>The seconds the scene clock advances in this frame: 0 while the scene is paused.</summary>
        public float SceneDt { get; }
        /// <summary>The scene clock's time before this frame, seconds.</summary>
        public double SceneTime { get; }
        /// <summary>The scene is paused (steps while paused arrive as <see cref="IAppEvents.SceneStepped"/>, not as Update calls).</summary>
        public bool Paused { get; }

        /// <summary>"frame n (scene dt d)", for logs.</summary>
        public override string ToString() => "frame " + Frame + " (scene dt " + SceneDt + (Paused ? ", paused" : "") + ")";
    }

    /// <summary>How a scene came to be.</summary>
    public enum SceneLoadKind
    {
        /// <summary>
        /// The scene the app started with, reported once right after the plugins loaded; also the open scene, reported to a
        /// plugin enabled while the app runs.
        /// </summary>
        Initial,
        /// <summary>A new empty scene.</summary>
        New,
        /// <summary>A scene loaded from a file or from bytes.</summary>
        Load,
    }

    /// <summary>A new or loaded scene.</summary>
    public sealed class SceneLoadInfo
    {
        /// <summary>Creates the notification (the host does; plugins read it).</summary>
        public SceneLoadInfo(SceneLoadKind kind, string path, int findings, int nodes)
        {
            Kind = kind;
            Path = path;
            Findings = findings;
            Nodes = nodes;
        }

        /// <summary>How the scene came to be.</summary>
        public SceneLoadKind Kind { get; }
        /// <summary>The file: relative to the scene folder when inside it, else absolute; null for a new or initial scene and for bytes.</summary>
        public string Path { get; }
        /// <summary>Findings of the load and of binding the model.</summary>
        public int Findings { get; }
        /// <summary>Nodes in the scene.</summary>
        public int Nodes { get; }

        /// <summary>"Kind path (n nodes, m findings)", for logs.</summary>
        public override string ToString() => Kind + (Path != null ? " " + Path : "") + " (" + Nodes + " nodes, " + Findings + " findings)";
    }
}
