// The item rows of one plugin (the plugin's view of the generated item groups of the Studio list): a group the plugin adds is held
// in the list database's registry with the plugin's GUID as its owner and as the GUID of every row's key. The plugin's function runs
// behind its fault wrapper: a throw, or no object, is counted as a fault of the plugin and the item loads the fallback item. When the
// plugin unloads, is disabled or faults every group of it leaves the registry (items already built keep their objects until they are
// built again) and this view refuses further groups. Main thread only.
using System;
using System.Collections.Generic;
using HS2Next.Api.Unity;
using HS2Next.Content;
using HS2Next.Content.Studio;
using HS2Next.Scenes.Runtime;
using UnityEngine;

namespace HS2Next.App.Plugins.Engine
{
    /// <summary>One plugin's view of the generated item groups.</summary>
    public sealed class PluginItemRows : IItemRows
    {
        readonly PluginGuard guard;
        readonly Func<SceneRuntime> runtime;
        readonly List<RowGroup> groups = new List<RowGroup>();
        bool closed;

        /// <param name="guard">The plugin's fault wrapper.</param>
        /// <param name="runtime">The current scene runtime (its services hold the Studio lists), or null.</param>
        public PluginItemRows(PluginGuard guard, Func<SceneRuntime> runtime)
        {
            this.guard = guard ?? throw new ArgumentNullException(nameof(guard));
            this.runtime = runtime ?? (() => null);
        }

        /// <summary>The plugin's groups that are in the registry.</summary>
        public int Count
        {
            get
            {
                int n = 0;
                foreach (var g in groups) if (!g.Inner.Removed) n++;
                return n;
            }
        }

        public IItemRowGroup AddGroup(int group, string name, Func<ItemRowRequest, GameObject> make)
        {
            if (closed) throw new ObjectDisposedException("the plugin " + guard.Guid + " is unloaded");
            if (make == null) throw new ArgumentNullException(nameof(make));
            var db = runtime()?.Services?.Db;
            if (db == null) throw new InvalidOperationException("the Studio lists are not built");
            string hook = "item rows " + group;
            GameObject Make(GeneratedRowRequest r)
            {
                if (closed || guard.Faulted) return null;
                var req = new ItemRowRequest(r.Guid, r.Group, r.Category, r.Id, r.NodeId);
                int before = guard.Total;
                var go = guard.Run(hook, () => make(req), null);
                if (!go && guard.Total == before) guard.Count(hook, new InvalidOperationException("make returned no object for " + r.Guid + "#" + r.Id));
                return go ? go : null;
            }
            var g = db.Generated.Add(group, name, guard.Guid, guard.Guid, Make);
            var h = new RowGroup(this, g);
            groups.Add(h);
            return h;
        }

        /// <summary>The plugin leaves: its groups leave the registry, later AddGroup calls throw. Idempotent.</summary>
        public void Close()
        {
            closed = true;
            foreach (var g in groups) g.Inner.Remove();
            groups.Clear();
        }

        sealed class RowGroup : IItemRowGroup
        {
            readonly PluginItemRows owner;

            public RowGroup(PluginItemRows owner, GeneratedGroup inner)
            {
                this.owner = owner;
                Inner = inner;
            }

            public GeneratedGroup Inner { get; }

            public int Group => Inner.Number;

            void Check()
            {
                if (Inner.Removed) throw new ObjectDisposedException("item group " + Inner.Number + " of the plugin " + owner.guard.Guid + " was removed");
            }

            public void SetCategory(int category, string name)
            {
                Check();
                Inner.SetCategory(category, name);
            }

            public void SetRows(IEnumerable<ItemRow> rows)
            {
                Check();
                if (rows == null) throw new ArgumentNullException(nameof(rows));
                var specs = new List<GeneratedRowSpec>();
                foreach (var r in rows)
                {
                    if (r == null) throw new ArgumentException("a row is null", nameof(rows));
                    specs.Add(new GeneratedRowSpec(r.Category, r.Id, r.Name, r.FkBones));
                }
                Inner.SetRows(specs);
            }

            public void Refresh(int id)
            {
                Check();
                Inner.Refresh(new ContentId(owner.guard.Guid, id));
            }

            public void Dispose() => Inner.Remove();
        }
    }
}
