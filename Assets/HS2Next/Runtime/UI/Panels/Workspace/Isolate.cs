// view.isolate: "show only the selection" as a view-only filter that is not
// stored. Renderers whose nearest node holder belongs to a node outside the isolated set (the isolated nodes and what lies
// under them) get Renderer.forceRenderingOff; the realization never touches that flag (it writes enabled / active), so the
// document and the realized visibility stay as they are. The map is left alone. Re-applied after scene changes while on.
// Captures render through their own cameras but use the same renderers, so an isolated view also isolates captures (the
// user sees what will be captured; toggled off by the same action).
using System;
using System.Collections.Generic;
using HS2Next.Scenes;
using UnityEngine;

namespace HS2Next.UI.Panels
{
    public static class Isolate
    {
        static readonly List<Renderer> hidden = new List<Renderer>();

        public static int HiddenCount => hidden.Count;

        public static void Apply(UiContext ctx, HashSet<string> keep)
        {
            Clear(ctx);
            var rt = ctx.Runtime;
            if (rt?.NodesRoot == null) return;
            // holder transform → node id, and the set of ids shown (isolated nodes and their subtrees)
            var holders = new Dictionary<Transform, string>();
            var show = new HashSet<string>(StringComparer.Ordinal);
            foreach (var n in ctx.Model.PreOrder())
            {
                var h = rt.HolderOf(n.Id);
                if (h != null) holders[h] = n.Id;
                for (var p = n; p != null; p = p.Parent) if (keep.Contains(p.Id)) { show.Add(n.Id); break; }
            }
            foreach (var r in rt.NodesRoot.GetComponentsInChildren<Renderer>(true))
            {
                string owner = null;
                for (var t = r.transform; t != null && t != rt.NodesRoot; t = t.parent)
                    if (holders.TryGetValue(t, out owner)) break;
                if (owner == null || show.Contains(owner) || r.forceRenderingOff) continue;
                r.forceRenderingOff = true;
                hidden.Add(r);
            }
        }

        public static void Clear(UiContext ctx)
        {
            foreach (var r in hidden) if (r != null) r.forceRenderingOff = false;
            hidden.Clear();
        }
    }
}
