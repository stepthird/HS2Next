// Gimmick icons of Studio items: the sprite renderer an item's root IconComponent
// names. Its object is active iff the app option `items.gimmickIcons` (Visible, default true; an app setting, not scene
// state) and the node is visible; it is turned to face the view camera every LateApply (a part registered on the runtime
// by the first item with an icon, after the view camera); captures never show it: ViewCapture and ThumbnailCapture
// render inside HideForCapture(), which sets forceRenderingOff on every registered icon renderer and restores it
// (HS2 culls the Studio/Camera layer; HS2Next defines no Studio layers, T10).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    public static class ItemIcons
    {
        static bool visible = true;
        static readonly Dictionary<Renderer, ItemRealization> icons = new Dictionary<Renderer, ItemRealization>();

        /// <summary>The app option items.gimmickIcons (process-wide, never saved). Setting it updates every icon.</summary>
        public static bool Visible
        {
            get => visible;
            set
            {
                if (visible == value) return;
                visible = value;
                foreach (var kv in Snapshot()) kv.Value.RefreshIcon();
            }
        }

        /// <summary>Icon renderers alive (all runtimes).</summary>
        public static int Count
        {
            get
            {
                Prune();
                return icons.Count;
            }
        }

        public static IEnumerable<Renderer> Renderers
        {
            get
            {
                Prune();
                return new List<Renderer>(icons.Keys);
            }
        }

        static List<KeyValuePair<Renderer, ItemRealization>> Snapshot()
        {
            Prune();
            return new List<KeyValuePair<Renderer, ItemRealization>>(icons);
        }

        static void Prune()
        {
            List<Renderer> dead = null;
            foreach (var r in icons.Keys) if (!r) (dead ??= new List<Renderer>()).Add(r);
            if (dead != null) foreach (var r in dead) icons.Remove(r);
        }

        internal static void Register(Renderer r, ItemRealization owner)
        {
            if (!r) return;
            icons[r] = owner;
            var rt = owner.Runtime;
            if (rt.GetPart<IconFacingPart>() == null) rt.AddPart(new IconFacingPart());
        }

        internal static void Unregister(Renderer r)
        {
            if (r is object) icons.Remove(r);
        }

        /// <summary>Hides every icon renderer until disposed (captures and thumbnails).</summary>
        public static IDisposable HideForCapture() => new Hidden();

        sealed class Hidden : IDisposable
        {
            readonly List<(Renderer r, bool was)> saved = new List<(Renderer, bool)>();

            public Hidden()
            {
                foreach (var kv in Snapshot())
                {
                    saved.Add((kv.Key, kv.Key.forceRenderingOff));
                    kv.Key.forceRenderingOff = true;
                }
            }

            public void Dispose()
            {
                foreach (var (r, was) in saved) if (r) r.forceRenderingOff = was;
                saved.Clear();
            }
        }

        /// <summary>Turns the icons of one runtime to face its view camera (LateApply, after the view camera's part).</summary>
        sealed class IconFacingPart : ISceneRuntimePart
        {
            SceneRuntime runtime;
            public void Attach(SceneRuntime r) => runtime = r;
            public void OnChanged(SceneChangeBatch b) { }
            public void Apply() { }
            public void Clear() { }
            public void OnLoadedRealized() { }

            public void LateApply()
            {
                if (icons.Count == 0) return;
                var cam = runtime.GetPart<SceneViewCamera>()?.Camera;
                if (!cam) cam = Camera.main;
                if (!cam) return;
                var rot = cam.transform.rotation;
                foreach (var kv in icons)
                {
                    var r = kv.Key;
                    if (!r || kv.Value.Runtime != runtime || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                    r.transform.rotation = rot;
                }
            }
        }
    }
}
