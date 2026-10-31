// The Studio lights of a realized scene as one part: registers the
// `studio` MainLight source (the Lights of every built LightRealization in the model, pre-order) and refreshes the
// main-light rule in Apply after a Studio light was built, rebuilt, destroyed or changed (LightRealization calls
// Changed), after node additions / removals and after a clear or a load. Transform changes need no refresh (the
// direction does not enter the rule). After a refresh, a shadowed, enabled Studio directional light that is not the main
// light gets the info finding `light.unshadowedDirectional` (once per light until the realization is cleared): URP
// shadows one directional light. Registered by SceneRuntime.AddStandardParts after SceneCharaLight.
using System.Collections.Generic;
using HS2Next.Save;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    public sealed class StudioLights : ISceneRuntimePart
    {
        public const string SourceKind = "studio";

        SceneRuntime runtime;
        bool dirty = true;

        /// <summary>Main-light refreshes done by this part (tests).</summary>
        public int Refreshes { get; private set; }

        /// <summary>A Studio light changed: the part of <paramref name="r"/> refreshes the main light at its next Apply.</summary>
        internal static void Changed(SceneRuntime r)
        {
            var p = r?.GetPart<StudioLights>();
            if (p != null) p.dirty = true;
        }

        /// <summary>The built Studio light realizations of the model (pre-order; graveyard excluded).</summary>
        public IEnumerable<LightRealization> Realizations()
        {
            if (runtime == null) yield break;
            foreach (var n in runtime.Model.PreOrder())
                if (n.Kind == NodeKind.Light && n.InModel && runtime.Find(n.Id) is LightRealization l && !l.InGraveyard && l.Light)
                    yield return l;
        }

        IEnumerable<Light> Lights()
        {
            foreach (var l in Realizations()) yield return l.Light;
        }

        /// <summary>Applies the main-light rule now (also called by Apply when dirty).</summary>
        public void Refresh()
        {
            dirty = false;
            Refreshes++;
            var main = MainLight.Refresh();
            if (runtime == null) return;
            foreach (var l in Realizations())
            {
                var li = l.Light;
                if (li.type != LightType.Directional || !MainLight.IsCandidate(li) || li == main) continue;
                runtime.ReportOnce(FindingSeverity.Info, "light.unshadowedDirectional", l.NodeId,
                    "Studio directional light " + l.NodeId + " has shadows on but is not the main light; URP shadows one directional light (" + (main ? main.name : "none") + ")");
            }
        }

        // ------------------------------------------------------------------ ISceneRuntimePart

        public void Attach(SceneRuntime r)
        {
            runtime = r;
            MainLight.SetSource(this, SourceKind, Lights);
            dirty = true;
        }

        public void OnChanged(SceneChangeBatch b)
        {
            foreach (var c in b.Changes)
                if (c.Kind == SceneChangeKind.NodeAdded || c.Kind == SceneChangeKind.NodeRemoved) { dirty = true; return; }
        }

        public void Apply()
        {
            if (dirty) Refresh();
        }

        public void LateApply() { }

        public void Clear() => dirty = true;

        public void OnLoadedRealized() => dirty = true;

        /// <summary>Leaves the MainLight sources (runtime disposal).</summary>
        public void Destroy()
        {
            MainLight.RemoveSource(this);
            MainLight.Refresh();
            runtime = null;
        }
    }
}
