// Effective visibility on Unity objects: Renderer.forceRenderingOff on a node's
// OWN renderers, found by a walk from its holder that stops at every other SceneNodeHolder (child nodes keep
// their own state, so a hidden child under a visible parent stays hidden and vice versa). forceRenderingOff
// composes with renderer.enabled, which other systems keep using (option visibility, clothes states, item
// options), and also suppresses shadows. The scene layer never changes renderer.enabled for visibility.
using System.Collections.Generic;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    public static class VisibilityApplier
    {
        [System.ThreadStatic] static List<Renderer> scratch;

        /// <summary>forceRenderingOff = !visible on the own renderers of the holder (or of any object: the walk starts at
        /// <paramref name="start"/> and stops at holders below it). Returns the number of renderers touched.</summary>
        public static int SetOwn(Transform start, bool visible)
        {
            if (!start) return 0;
            var l = scratch ??= new List<Renderer>(256);
            l.Clear();
            CollectOwn(start, l);
            bool off = !visible;
            for (int i = 0; i < l.Count; i++) if (l[i].forceRenderingOff != off) l[i].forceRenderingOff = off;
            int n = l.Count;
            l.Clear();
            return n;
        }

        /// <summary>The own renderers of a holder: every renderer under <paramref name="start"/> (inactive included) not inside
        /// another node's holder.</summary>
        public static List<Renderer> OwnRenderers(Transform start)
        {
            var l = new List<Renderer>();
            if (start) CollectOwn(start, l);
            return l;
        }

        /// <summary>Appends the own renderers of <paramref name="start"/> to <paramref name="into"/>.</summary>
        public static void CollectOwn(Transform start, List<Renderer> into)
        {
            if (!start) return;
            Walk(start, into, true);
        }

        static readonly List<Renderer> perObject = new List<Renderer>(4);

        static void Walk(Transform t, List<Renderer> into, bool isStart)
        {
            if (!isStart && t.GetComponent<SceneNodeHolder>() != null) return;
            t.GetComponents(perObject);
            for (int i = 0; i < perObject.Count; i++) into.Add(perObject[i]);
            perObject.Clear();
            for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i), into, false);
        }
    }
}
