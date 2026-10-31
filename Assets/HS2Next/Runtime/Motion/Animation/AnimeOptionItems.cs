// Option items of an anime row (Studio `OptionItemCtrl`): weapons and props that a row
// instantiates, parents to named character bones, optionally animates with its own controller (same state name
// as the character) and, when synced, keeps on the character's layer-0 state and time.
//   Loading (prefab through the catalog, Animator + base/override controller, `height`, parenting pairs, scale),
//   one-shot counter-scale, sync, visibility, release.
// Deviations: (d) synced items get `motion` = pattern at load (AnimationOptions.SetMotionOnSyncedItemsAtLoad);
// (e) an entry whose parent bone is missing is skipped and logged (AnimationOptions.SkipMissingOptionParents;
// false = the game's behaviour of aborting the remaining blocks). Sync runs right after the body sample in the
// same step (Play + Evaluate(0): the item shows the body's time of this frame); AnimationOptions.GameFrameDelay
// reproduces the game's one-frame queue instead.
using System.Collections.Generic;
using HS2Next.Chara;
using HS2Next.Content;
using HS2Next.Content.Studio;
using UnityEngine;

namespace HS2Next.Motion.Animation
{
    /// <summary>One instantiated option item.</summary>
    public sealed class AnimeOptionItem
    {
        public StudioAnimeOption Block;
        /// <summary>The instantiated prefab root.</summary>
        public GameObject Root;
        /// <summary>The prefab root's original local scale (s0).</summary>
        public Vector3 RootScale;
        /// <summary>Objects moved under character bones: (object, new parent). The root itself when the entry has no child.</summary>
        public readonly List<(Transform moved, Transform parent)> Moved = new List<(Transform, Transform)>();
        /// <summary>First Animator of the prefab (self or children), or null.</summary>
        public Animator Animator;
        /// <summary>The item's playback (null without Animator or controller).</summary>
        public AnimationDriver Driver;
        public bool Synced;
        public bool CounterScalePending;
        /// <summary>Renderers enabled in the prefab (root and moved children).</summary>
        public readonly List<Renderer> Renderers = new List<Renderer>();
        public override string ToString() => $"{(Root ? Root.name : "-")} ({Moved.Count} moved, animator {(Animator ? "yes" : "no")}, driver {(Driver != null ? "yes" : "no")}, synced {Synced})";
    }

    /// <summary>The option items of a character's current row.</summary>
    public sealed class AnimeOptionItems
    {
        readonly List<AnimeOptionItem> items = new List<AnimeOptionItem>();
        public IReadOnlyList<AnimeOptionItem> Items => items;
        public int Count => items.Count;
        /// <summary>Lines from the last load (skipped blocks, missing parents, controllers).</summary>
        public readonly List<string> Log = new List<string>();

        /// <summary>
        /// The objects of the current items: each item root and each child an item moved under a character bone, skipping
        /// any that lies inside an object already listed. The hierarchies are disjoint and together hold every renderer
        /// of the items.
        /// </summary>
        public List<GameObject> Objects()
        {
            var r = new List<GameObject>();
            void Add(Transform t)
            {
                if (!t) return;
                foreach (var o in r) if (t.IsChildOf(o.transform)) return;
                r.Add(t.gameObject);
            }
            foreach (var it in items)
            {
                if (it.Root) Add(it.Root.transform);
                foreach (var (moved, _) in it.Moved) Add(moved);
            }
            return r;
        }

        /// <summary>
        /// releases the previous items, then loads every option block of the row in order. clipState = the
        /// character's state name; bodyHeight = body height slider; pattern = the scene pattern (deviation d).
        /// </summary>
        public void Load(StudioAnime row, CharaContext ctx, float bodyHeight, float pattern, AnimationOptions opt)
        {
            Release();
            Log.Clear();
            if (row == null || ctx == null) return;
            var gd = ctx.Catalog;
            int clipHash = Animator.StringToHash(row.Clip ?? "");
            for (int bi = 0; bi < row.Options.Count; bi++)
            {
                var b = row.Options[bi];
                // 1. prefab
                GameObject prefab = null;
                try { prefab = gd?.LoadAssetAt<GameObject>(row.Zipmod, b.Bundle, b.Manifest, b.Asset); }   // through the texture guard
                catch (System.Exception e) { Log.Add($"option {bi}: {b.Bundle}/{b.Asset} failed ({e.GetType().Name})"); }
                if (!prefab) { Log.Add($"option {bi}: prefab {b.Bundle}/{b.Asset} not found, skipped"); continue; }
                var go = Object.Instantiate(prefab);
                go.name = prefab.name;
                var it = new AnimeOptionItem { Block = b, Root = go, RootScale = go.transform.localScale };
                // renderers enabled in the prefab (collected before anything is reparented)
                foreach (var r in go.GetComponentsInChildren<Renderer>(true)) if (r.enabled) it.Renderers.Add(r);
                // 2.-3. animator, controller, height, sync
                it.Animator = go.GetComponentInChildren<Animator>(true);
                if (it.Animator)
                {
                    if (!string.IsNullOrEmpty(b.ControllerBundle) && !string.IsNullOrEmpty(b.ControllerAsset))
                    {
                        var ctrl = LoadController(gd, row.Zipmod, b.ControllerBundle, b.ControllerAsset);
                        if (ctrl && !string.IsNullOrEmpty(b.OverrideBundle) && !string.IsNullOrEmpty(b.OverrideAsset))
                        {
                            var ov = LoadController(gd, row.Zipmod, b.OverrideBundle, b.OverrideAsset);
                            if (ov) ctrl = CharaAnimation.BuildOverride(ctrl, ov);
                        }
                        if (ctrl)
                        {
                            it.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                            it.Driver = AnimationDriver.Create(opt.Engine, it.Animator, ctrl, "anim-item:" + go.name);
                            it.Driver.Play(clipHash, -1, float.NegativeInfinity);
                        }
                        else Log.Add($"option {bi}: controller {b.ControllerBundle}/{b.ControllerAsset} not found");
                    }
                    if (it.Driver != null && it.Driver.HasParameter("height", AnimatorControllerParameterType.Float)) it.Driver.SetFloat("height", bodyHeight);
                    it.Synced = b.IsAnimeSync;
                    if (it.Synced && opt.SetMotionOnSyncedItemsAtLoad && it.Driver != null && it.Driver.HasParameter("motion", AnimatorControllerParameterType.Float))
                        it.Driver.SetFloat("motion", pattern);
                }
                // 4. parenting
                bool abort = false;
                if (string.IsNullOrEmpty(b.Parentage))
                {
                    var p = ctx.Root.transform;
                    go.transform.SetParent(p, false);
                    go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity;
                    it.Moved.Add((go.transform, p));
                }
                else
                {
                    foreach (var (parentName, childName) in b.ParentagePairs())
                    {
                        var parent = FindDeep(ctx.Root.transform, parentName);
                        if (!parent)
                        {
                            Log.Add($"option {bi}: parent \"{parentName}\" not found in the character{(opt.SkipMissingOptionParents ? ", entry skipped" : ", remaining blocks aborted (parity)")}");
                            if (!opt.SkipMissingOptionParents) { abort = true; break; }
                            continue;
                        }
                        var moved = string.IsNullOrEmpty(childName) ? go.transform : FindDeep(go.transform, childName);
                        if (!moved) { Log.Add($"option {bi}: child \"{childName}\" not in the prefab, entry skipped"); continue; }
                        moved.SetParent(parent, false);
                        moved.localPosition = Vector3.zero; moved.localRotation = Quaternion.identity;
                        it.Moved.Add((moved, parent));
                    }
                }
                // 5. scale
                if (b.CounterScale) it.CounterScalePending = true;
                else foreach (var (moved, _) in it.Moved) moved.localScale = it.RootScale;   // also for a child (quirk)
                items.Add(it);
                if (abort) break;
            }
        }

        static RuntimeAnimatorController LoadController(ContentCatalog gd, ZipmodInfo owner, string bundle, string asset) =>
            gd?.LoadAssetAt<RuntimeAnimatorController>(owner, bundle, "", asset);

        /// <summary>Depth-first search by exact name, the root itself included.</summary>
        public static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var r = FindDeep(t.GetChild(i), name);
                if (r) return r;
            }
            return null;
        }

        /// <summary>renderers on iff visible.</summary>
        public void ApplyVisibility(bool visible)
        {
            for (int i = 0; i < items.Count; i++)
                foreach (var r in items[i].Renderers) if (r) r.enabled = visible;
        }

        /// <summary>
        /// The order-0 follow-up of one step. Default: synced items jump to the body's state and time and
        /// are evaluated at once; unsynced animated items advance on their own clock (speed 1). gameFrameDelay: the
        /// game's queue (the item consumes last step's Play while advancing by dt, then queues this step's).
        /// </summary>
        internal void Step(float dt, int bodyHash, float bodyTime, bool hasBody, bool gameFrameDelay)
        {
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                var d = it.Driver;
                if (d != null)
                {
                    if (it.Synced && hasBody)
                    {
                        if (gameFrameDelay) { d.Evaluate(dt); d.Play(bodyHash, 0, bodyTime); }
                        else { d.Play(bodyHash, 0, bodyTime); d.Evaluate(0f); }
                    }
                    else d.Evaluate(dt);
                }
                if (it.CounterScalePending && it.Root)
                {
                    var t = it.Root.transform;
                    var ls = t.localScale; var w = t.lossyScale; var s0 = it.RootScale;
                    t.localScale = new Vector3(Div(ls.x, w.x) * s0.x, Div(ls.y, w.y) * s0.y, Div(ls.z, w.z) * s0.z);
                    it.CounterScalePending = false;
                }
            }
        }

        static float Div(float a, float b) => b != 0f ? a / b : a;

        /// <summary>Re-sync synced items to the body now (after a time set while paused).</summary>
        internal void SyncNow(int bodyHash, float bodyTime)
        {
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                if (it.Driver == null || !it.Synced) continue;
                it.Driver.Play(bodyHash, 0, bodyTime); it.Driver.Evaluate(0f);
            }
        }

        /// <summary>Pattern for synced animated items.</summary>
        internal void SetMotion(float v)
        {
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                if (it.Driver != null && it.Synced && it.Driver.HasParameter("motion", AnimatorControllerParameterType.Float)) it.Driver.SetFloat("motion", v);
            }
        }

        /// <summary>Restart buttons: every animated item replays its own current state at time 0.</summary>
        internal void Restart()
        {
            for (int i = 0; i < items.Count; i++)
            {
                var d = items[i].Driver;
                if (d == null) continue;
                d.Play(d.State(0).shortNameHash, 0, 0f);
            }
        }

        /// <summary>Character guide scale changed: every item root back to its prefab scale (no new counter-scale; quirk).</summary>
        public void ResetScales()
        {
            for (int i = 0; i < items.Count; i++) if (items[i].Root) items[i].Root.transform.localScale = items[i].RootScale;
        }

        /// <summary>Female card replaced: counter-scale pending again for counter-scaled items.</summary>
        public void RearmCounterScale()
        {
            for (int i = 0; i < items.Count; i++) if (items[i].Block != null && items[i].Block.CounterScale) items[i].CounterScalePending = true;
        }

        /// <summary>destroys every item (root and each reparented child) and its playback.</summary>
        public void Release()
        {
            foreach (var it in items)
            {
                it.Driver?.Dispose();
                foreach (var (moved, _) in it.Moved) if (moved && it.Root && moved != it.Root.transform) DestroyObj(moved.gameObject);
                if (it.Root) DestroyObj(it.Root);
            }
            items.Clear();
        }

        static void DestroyObj(Object o)
        {
            if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o);
        }
    }
}
