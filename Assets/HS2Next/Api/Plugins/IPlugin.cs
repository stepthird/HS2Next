// The plugin contract: the entry type a plugin implements and the optional interfaces of its lifecycle. A plugin is a
// folder <app>/Plugins/<guid>/ with manifest.json and the assemblies it lists; the host creates the entry type once
// (its public parameterless constructor), calls Load with the plugin's context when the app starts, SceneLoaded after
// every new or loaded scene (and once for the initial scene), Update once per frame, and Unload when the app quits or
// the plugin is disabled after repeated faults. Every call comes on the main thread. Part of the stable API (assembly
// HS2Next.Api).
namespace HS2Next.Api.Plugins
{
    /// <summary>
    /// The entry type of a plugin. Implemented by plugins. Load is called once per process (and per Editor play session
    /// with a domain reload); every registration (commands, ext handlers, event handlers, hooks) belongs there, and
    /// everything registered through the context is removed by the host after Unload returns.
    /// </summary>
    public interface IPlugin
    {
        /// <summary>Registers the plugin's commands, ext handlers and event handlers. A throw marks the plugin failed and removes what it registered.</summary>
        void Load(IPluginContext context);

        /// <summary>
        /// Called once before the host removes the plugin's registrations: at quit, at the end of an Editor play session,
        /// or when the plugin becomes faulted. The context's members (except Guid and Log) stop working after it returns.
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
        /// After every new or loaded scene and once with <see cref="SceneLoadKind.Initial"/> right after all plugins loaded;
        /// the ext blocks of the loaded scene were read before. The same notification as <see cref="IAppEvents.SceneLoaded"/>.
        /// </summary>
        void SceneLoaded(SceneLoadInfo info);
    }

    /// <summary>The frame a plugin's Update runs in.</summary>
    public readonly struct PluginFrame
    {
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

        public override string ToString() => "frame " + Frame + " (scene dt " + SceneDt + (Paused ? ", paused" : "") + ")";
    }

    /// <summary>How a scene came to be.</summary>
    public enum SceneLoadKind
    {
        /// <summary>The scene the app started with, reported once right after the plugins loaded.</summary>
        Initial,
        /// <summary>A new empty scene.</summary>
        New,
        /// <summary>A scene loaded from a file or from bytes.</summary>
        Load,
    }

    /// <summary>A new or loaded scene.</summary>
    public sealed class SceneLoadInfo
    {
        public SceneLoadInfo(SceneLoadKind kind, string path, int findings, int nodes)
        {
            Kind = kind;
            Path = path;
            Findings = findings;
            Nodes = nodes;
        }

        public SceneLoadKind Kind { get; }
        /// <summary>The file: relative to the scene folder when inside it, else absolute; null for a new or initial scene and for bytes.</summary>
        public string Path { get; }
        /// <summary>Findings of the load and of binding the model.</summary>
        public int Findings { get; }
        /// <summary>Nodes in the scene.</summary>
        public int Nodes { get; }

        public override string ToString() => Kind + (Path != null ? " " + Path : "") + " (" + Nodes + " nodes, " + Findings + " findings)";
    }
}
