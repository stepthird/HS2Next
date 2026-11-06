// A plugin's runtime overrides (IPluginContext.Overrides, experimental): the plugin's view of the scene runtime's override
// layer, with the plugin's GUID as the owner of its slots. Available while the app has a scene runtime; Acquire returns the
// layer's slot object itself (its setters run no plugin code, so they cannot fault). The host releases the plugin's slots
// with its other registrations when it leaves loaded (disable, fault, unload at quit: PluginHost.RemoveRegistrations), the
// layer every slot when the scene is cleared and a node's slots when the node is forgotten. Main thread only, except
// Available; once the plugin is no longer loaded Acquire throws ObjectDisposedException (its context already does).
using System;
using System.Collections.Generic;
using System.Threading;
using HS2Next.Api.Plugins;
using HS2Next.Scenes.Overrides;
using HS2Next.Scenes.Runtime;

namespace HS2Next.App.Plugins
{
    public sealed class PluginOverrides : IRuntimeOverrides
    {
        readonly PluginHost host;
        readonly PluginEntry entry;
        readonly Func<bool> valid;
        readonly int mainThread = Thread.CurrentThread.ManagedThreadId;

        internal PluginOverrides(PluginHost host, PluginEntry entry, Func<bool> valid)
        {
            this.host = host;
            this.entry = entry;
            this.valid = valid;
        }

        SceneRuntime Runtime
        {
            get
            {
                var rt = host.App?.Runtime;
                return rt != null && !rt.IsDisposed ? rt : null;
            }
        }

        /// <summary>The scene runtime's layer, or null (a host without one).</summary>
        internal OverrideLayer Layer => Runtime?.Overrides;

        public bool Available => Runtime != null;

        public IOverrideSlot Acquire(string nodeId, string path, int priority = 0)
        {
            Check();
            var layer = Layer;
            if (layer == null) return null;
            layer.SetOwnerName(entry.Guid, entry.Manifest?.Name ?? entry.Guid);
            return layer.Acquire(entry.Guid, nodeId, path, priority);
        }

        public IReadOnlyList<OverrideInfo> List(string nodeId = null)
        {
            Check();
            return Layer?.List(nodeId) ?? Array.Empty<OverrideInfo>();
        }

        public int ReleaseAll()
        {
            Check();
            return Layer?.ReleaseOwner(entry.Guid) ?? 0;
        }

        void Check()
        {
            if (!valid()) throw new ObjectDisposedException("plugin context " + entry.Guid, "the plugin has unloaded");
            if (Thread.CurrentThread.ManagedThreadId != mainThread) throw new InvalidOperationException("runtime overrides are used on the main thread only");
        }

        public override string ToString() => "runtime overrides of " + entry.Guid;
    }
}
