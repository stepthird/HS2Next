// Item node realization. Build order
// (HS2's AddObjectItem.Load):
//   1 resolve the row (a missing or gated row, or a row whose prefab does not load, loads the fallback illusion.hs2#399,
//     `item.missing`; the stored ref is never changed), instantiate under the holder at local identity (the prefab root's
//     own pose is replaced, as in vanilla)
//   2 collect (HS2's rules): the root ItemComponent and helpers (Particle / Icon / Panel / SE component), the playing
//     particle systems of the active hierarchy, every DynamicBone of the active hierarchy, the first Animator of the
//     active hierarchy; the root MeshCollider disabled, network components destroyed, cameras and audio listeners
//     disabled (`item.componentDisabled`), prefab AudioSources stopped and disabled and VideoPlayer audio off
//     (`item.audioSuppressed`; audio is postponed to 3.3, Q8)
//   3 materials: owned per-slot copies, the original-shader record, the sea keyword, the URP swap (ItemMaterialWriter)
//   4 lights: URP light data and the colour × intensity folding (BundleLights); icon: registered, active per the app
//     option and the visibility
//   5 attach transform (ItemComponent.childRoot, else the root); ItemMotionHooks.Built (package B: animation, FK base
//     pose, dynamic bones from the stored state); without a motion handler the 2.2 behaviour: the Animator enabled iff
//     isAnime with `anime.speed`
//   6 pattern textures, panel image, the colour write
//   7 material edits (ItemMaterialEdits: on top of the colours), options, particle play-on-load (play mode)
// An exception in one part of 2–7 becomes `item.partFailed` (warning) and the item stays Built. Changes route by path
//: ref / group / category rebuild; colours (except the pattern textures), alpha, emission, light cancel → the full
// colour write; colors[k].pattern.item / file → pattern texture k; panel → the panel image; options → options; fk,
// dynamicBones, anime → ItemMotionHooks.Changed; materialOverrides → a pass over the array, a rebuild when a shader name or another
// structural member changed; material edits run again after every colour, pattern and panel write. Visibility: forceRenderingOff on every own renderer (T7), particles
// played / paused (play mode), the icon object; lights stay on.
// Runtime overrides (Phase 6.2): a winning `item.colors[i].main` slot of the override layer replaces that main colour in
// every colour write (a view of the state is written; the model is not touched); a slot's change writes the colours at once.
using System;
using System.Collections.Generic;
using HS2Next.Content;
using HS2Next.Content.Studio;
using HS2Next.Save;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HS2Next.Scenes.Runtime
{
    public sealed class ItemRealization : NodeRealization
    {
        internal ItemRealization(SceneRuntime runtime, TreeNode node) : base(runtime, node) { MaterialEdits = new ItemMaterialEdits(this, WriteColors); }

        /// <summary>The prefab instance under the holder; null unless built.</summary>
        public GameObject Instance { get; private set; }
        /// <summary>The list row that was loaded (the fallback row when the stored one is missing).</summary>
        public StudioItem Row { get; private set; }
        /// <summary>The fallback item was loaded instead of the stored row.</summary>
        public bool IsFallback { get; private set; }
        public Studio.ItemComponent Component { get; private set; }
        public Studio.ParticleComponent ParticleComponent { get; private set; }
        public Studio.PanelComponent PanelComponent { get; private set; }
        public Studio.IconComponent IconComponent { get; private set; }
        public Studio.Sound.SEComponent SoundComponent { get; private set; }
        /// <summary>The first Animator of the active hierarchy; enabled by package A only while no motion hook exists.</summary>
        public Animator Animator { get; private set; }
        /// <summary>Every DynamicBone of the active hierarchy, enabled or not; empty unless built.</summary>
        public IReadOnlyList<DynamicBone> DynamicBones { get; private set; } = Array.Empty<DynamicBone>();
        /// <summary>The particle systems of the active hierarchy playing (or set to play on awake) after instantiation.</summary>
        public IReadOnlyList<ParticleSystem> Particles { get; private set; } = Array.Empty<ParticleSystem>();
        /// <summary>Free for the motion package (ItemMotionHooks.Built sets it); cleared when the content is released.</summary>
        public object Motion { get; set; }
        /// <summary>Where children attach: ItemComponent.childRoot, else the instance root.</summary>
        public Transform Attach { get; private set; }
        public double BuildMilliseconds { get; private set; }
        /// <summary>The owned materials and the appearance writes.</summary>
        public ItemMaterialWriter Writer { get; } = new ItemMaterialWriter();
        /// <summary>The material edits of `item.materialOverrides`.</summary>
        public ItemMaterialEdits MaterialEdits { get; }
        /// <summary>What the item offers (UI gates) and its default-button values; null unless built.</summary>
        public ItemFeatures Features { get; private set; }
        /// <summary>The icon renderer (IconComponent.renderer), or null.</summary>
        public Renderer Icon { get; private set; }
        /// <summary>Prefab AudioSources stopped and disabled at the build (Q8).</summary>
        public int AudioSuppressed { get; private set; }
        /// <summary>Cameras and audio listeners disabled at the build.</summary>
        public int ComponentsDisabled { get; private set; }
        /// <summary>Lights prepared at the build (BundleLights).</summary>
        public BundleLights.Result LightsPrepared { get; private set; }
        /// <summary>Sources of the current pattern textures (0..2) and the panel image, for reports; null = none.</summary>
        public readonly string[] PatternSources = new string[3];
        public string PanelSource { get; private set; }
        /// <summary>Layer-0 normalized time of the item Animator when the state was applied or captured.</summary>
        internal float appliedAnimeTime = float.NaN;

        readonly ItemTextures.Handle[] patternTex = new ItemTextures.Handle[3];
        ItemTextures.Handle panelTex;
        bool released = true;
        bool visibleNow = true;

        ItemNodeState St => Node.State as ItemNodeState;

        /// <summary>
        /// The row of a stored item: the (GUID, group, category, id) row when it exists and is not gated, else
        /// <c>StudioDb.ResolveItem</c> (the row of (GUID, id) or the fallback 399); an accessory offered as an item resolves only
        /// to its accessory row (else it is missing). <paramref name="fallback"/> = the row is not the stored one.
        /// </summary>
        public static StudioItem ResolveRow(StudioDb db, ItemNodeState st, out bool fallback)
        {
            fallback = false;
            if (db == null || st == null) { fallback = true; return null; }
            if (!ContentId.TryParse(st.Ref, out var id) || id.IsEmpty)
            {
                fallback = true;
                return db.Items.Get(StudioDb.FallbackItem);
            }
            return db.ResolveItem(id, st.Group, st.Category, out fallback);
        }

        // ------------------------------------------------------------ build

        // The bundles of this item's content (prefab, pattern textures, panel image): released with the content.
        HS2Next.Content.BundleOwner bundleOwner;

        HS2Next.Content.BundleLedger.Scope OwnBundles()
        {
            var cat = Services.Catalog;
            if (cat == null) return default;
            if (bundleOwner == null || !bundleOwner.Live) bundleOwner = cat.NewOwner(BundleOwnerLabel("item"));
            return cat.Own(bundleOwner);
        }

        protected internal override void BuildContent()
        {
            using (OwnBundles()) BuildContentCore();
        }

        void BuildContentCore()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var st = St;
            Row = ResolveRow(Services.Db, st, out bool fallback);
            IsFallback = fallback;
            if (fallback) ReportOnce(FindingSeverity.Warning, "item.missing", "item " + st?.Ref + " (group " + st?.Group + ", category " + st?.Category + ") is missing or gated: fallback " + StudioDb.FallbackItem + " loaded; the stored ref is kept");
            if (Row == null)
            {
                State = RealizationState.Failed;
                Detail = "no item row (not even the fallback)";
                Report(FindingSeverity.Error, "item.loadFailed", Detail);
                return;
            }
            GameObject prefab = null;
            try { prefab = Services.Catalog.LoadAsset<GameObject>(Row); }
            catch (Exception e) { Debug.LogWarning("item " + NodeId + ": loading " + Row + " threw: " + e.Message); }
            if (!prefab && !IsFallback)
            {
                // HS2 (AddObjectItem.Load): a row whose prefab does not load builds the fallback item (0, 0, 399)
                var fb = Services.Db.Items.Get(StudioDb.FallbackItem);
                if (fb != null && fb != Row)
                {
                    ReportOnce(FindingSeverity.Warning, "item.missing", "prefab " + Row.BundlePath + "/" + Row.FileName + " of item " + st?.Ref + " not loadable: fallback " + StudioDb.FallbackItem + " loaded; the stored ref is kept");
                    Row = fb;
                    IsFallback = true;
                    try { prefab = Services.Catalog.LoadAsset<GameObject>(Row); } catch (Exception) { prefab = null; }
                }
            }
            if (!prefab)
            {
                State = RealizationState.Failed;
                Detail = "prefab " + Row.BundlePath + "/" + Row.FileName + " not loadable";
                Report(FindingSeverity.Error, "item.loadFailed", Detail);
                return;
            }
            var go = Object.Instantiate(prefab, Holder, false);
            go.name = prefab.name;
            var t = go.transform;
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;
            Instance = go;
            released = false;
            Component = go.GetComponent<Studio.ItemComponent>();
            ParticleComponent = go.GetComponent<Studio.ParticleComponent>();
            PanelComponent = go.GetComponent<Studio.PanelComponent>();
            IconComponent = go.GetComponent<Studio.IconComponent>();
            SoundComponent = go.GetComponent<Studio.Sound.SEComponent>();

            Part("collect", () => Collect(go));
            Part("materials", () =>
            {
                Writer.Own(go, Component);
                if (Writer.NullRendererEntries > 0)
                    ReportOnce(FindingSeverity.Warning, "item.nullRenderer", Writer.NullRendererEntries + " renderer entr" + (Writer.NullRendererEntries == 1 ? "y" : "ies") + " of the ItemComponent without a renderer (skipped)");
                if (Writer.MismatchedEntries > 0)
                    ReportOnce(FindingSeverity.Info, "item.materialInfoMismatch", Writer.MismatchedEntries + " renderer entr" + (Writer.MismatchedEntries == 1 ? "y has" : "ies have") + " a slot count other than the MaterialInfo count (extra slots unwritten)");
            });
            Part("lights", () => LightsPrepared = BundleLights.Prepare(go));
            Part("icon", () =>
            {
                Icon = IconComponent != null && IconComponent.renderer ? IconComponent.renderer : null;
                if (Icon) ItemIcons.Register(Icon, this);
            });
            Attach = Component != null && Component.childRoot ? Component.childRoot : t;
            if (Animator && ItemMotionHooks.Built == null)
            {
                Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                Animator.enabled = Component != null && Component.isAnime != 0;
                Animator.speed = st != null ? st.Anime.Speed : 1f;
            }
            Part("features", () => Features = ItemFeatures.Compute(this, Writer));
            if (Features != null && Features.FkMissing.Count > 0)
                ReportOnce(FindingSeverity.Info, "item.fkBoneMissing", Features.FkMissing.Count + " bone(s) of the item's bone list not found: " + string.Join(", ", Features.FkMissing));
            State = RealizationState.Built;
            if (ItemMotionHooks.Built != null) Part("motion", () => ItemMotionHooks.Built(this));
            for (int k = 0; k < 3; k++)
            {
                int kk = k;
                Part("pattern" + (k + 1), () => LoadPattern(kk));
            }
            Part("panel", LoadPanel);
            Part("colors", WriteColors);
            Part("materialEdits", MaterialEdits.Apply);
            Part("options", ApplyOptions);
            Part("particles", PlayOnLoad);
            appliedAnimeTime = CurrentAnimeTime();
            BuildMilliseconds = sw.Elapsed.TotalMilliseconds;
        }

        void Part(string name, Action a)
        {
            try { a(); }
            catch (Exception e)
            {
                ReportOnce(FindingSeverity.Warning, "item.partFailed", "build part " + name + " threw " + e.GetType().Name + ": " + e.Message);
                Debug.LogWarning("item " + NodeId + " (" + Row + "): build part " + name + " threw: " + e);
            }
        }

        bool ActiveUnder(Component c) => c && c.gameObject.activeSelf && (c.transform == Instance.transform || IsActiveChain(c.transform.parent));

        bool IsActiveChain(Transform t)
        {
            for (var p = t; p; p = p.parent)
            {
                if (!p.gameObject.activeSelf) return false;
                if (p == Instance.transform) return true;
            }
            return true;
        }

        void Collect(GameObject go)
        {
            var t = go.transform;
            var mc = go.GetComponent<MeshCollider>();
            if (mc) mc.enabled = false;
            // the first Animator / every DynamicBone / the playing particle systems of the ACTIVE hierarchy (relative to the
            // instance, so a build under an inactive parent collects as HS2 does at the scene root)
            Animator = null;
            foreach (var a in go.GetComponentsInChildren<Animator>(true)) if (ActiveUnder(a)) { Animator = a; break; }
            var dbs = new List<DynamicBone>();
            foreach (var d in go.GetComponentsInChildren<DynamicBone>(true)) if (ActiveUnder(d)) dbs.Add(d);
            DynamicBones = dbs;
            var ps = new List<ParticleSystem>();
            foreach (var p in go.GetComponentsInChildren<ParticleSystem>(true))
                if (ActiveUnder(p) && (p.isPlaying || p.main.playOnAwake)) ps.Add(p);
            Particles = ps;
            // network components (HS2 destroys the first NetworkProximityChecker and the first NetworkIdentity)
            var prox = go.GetComponentInChildren<UnityEngine.Networking.NetworkProximityChecker>(true);
            if (prox) Object.DestroyImmediate(prox);
            var nid = go.GetComponentInChildren<UnityEngine.Networking.NetworkIdentity>(true);
            if (nid) Object.DestroyImmediate(nid);
            // cameras and audio listeners (as maps; T20), prefab audio (Q8, T9)
            ComponentsDisabled = AudioSuppressed = 0;
            foreach (var c in go.GetComponentsInChildren<Camera>(true)) if (c.enabled) { c.enabled = false; ComponentsDisabled++; }
            foreach (var l in go.GetComponentsInChildren<AudioListener>(true)) if (l.enabled) { l.enabled = false; ComponentsDisabled++; }
            foreach (var a in go.GetComponentsInChildren<AudioSource>(true))
            {
                if (a.isPlaying) a.Stop();
                if (a.enabled) { a.enabled = false; AudioSuppressed++; }
            }
            foreach (var v in go.GetComponentsInChildren<UnityEngine.Video.VideoPlayer>(true))
                v.audioOutputMode = UnityEngine.Video.VideoAudioOutputMode.None;
            if (ComponentsDisabled > 0) ReportOnce(FindingSeverity.Info, "item.componentDisabled", ComponentsDisabled + " camera / audio listener component(s) of the prefab disabled");
            if (AudioSuppressed > 0) ReportOnce(FindingSeverity.Info, "item.audioSuppressed", AudioSuppressed + " prefab AudioSource(s) stopped and disabled (item sound is not played)");
        }

        // ------------------------------------------------------------ appearance

        ResStore Res => Runtime.Session?.Model?.File?.Res;

        void WriteColors() => Writer.WriteColors(ColorsShown(), Component, ParticleComponent, PanelComponent);

        /// <summary>The state the colour write reads: the model's, or a view of it with the winning colour slots' main colours.</summary>
        ItemNodeState ColorsShown()
        {
            var st = St;
            var ov = Runtime.Overrides;
            if (st == null || !ov.HasNode(NodeId)) return st;
            ItemColorSlot[] colors = null;
            for (int k = 0; k < ItemNodeState.ColorCount && k < st.Colors.Length; k++)
            {
                var w = ov.WinnerOf(NodeId, HS2Next.Scenes.Overrides.OverrideChannel.ItemColor, k);
                if (w == null) continue;
                colors ??= (ItemColorSlot[])st.Colors.Clone();
                var c = st.Colors[k];
                colors[k] = new ItemColorSlot { Main = new Color4((float)w[0], (float)w[1], (float)w[2], (float)w[3]), Metallic = c.Metallic, Glossiness = c.Glossiness, Pattern = c.Pattern };
            }
            if (colors == null) return st;
            return new ItemNodeState
            {
                Ref = st.Ref, Group = st.Group, Category = st.Category, Anime = st.Anime, Colors = colors, Alpha = st.Alpha, Emission = st.Emission,
                LightCancel = st.LightCancel, Panel = st.Panel, Fk = st.Fk, DynamicBones = st.DynamicBones, Options = st.Options,
            };
        }

        /// <summary>A colour slot of the override layer changed or went: the colours are written again at once.</summary>
        internal void ApplyColorOverride()
        {
            if (State != RealizationState.Built || !Instance) return;
            Part("colors", WriteColors);
            Part("materialEdits", MaterialEdits.Apply);
        }

        void LoadPattern(int k)
        {
            var st = St;
            if (st == null) return;
            var h = ItemTextures.Pattern(Services, Res, st.Colors[k].Pattern);
            ItemTextures.Release(ref patternTex[k]);
            patternTex[k] = h;
            PatternSources[k] = h.Source;
            if (h.Error != null) ReportOnce(FindingSeverity.Warning, "item.patternMissing", "pattern " + (k + 1) + ": " + h.Error + " (no pattern drawn)");
            Writer.WritePattern(Component, k, h.Texture);
        }

        void LoadPanel()
        {
            if (PanelComponent == null) return;
            var st = St;
            if (st?.Panel == null)
            {
                ItemTextures.Release(ref panelTex);
                PanelSource = null;
                Writer.WritePanel(PanelComponent, null);
                return;
            }
            var h = ItemTextures.File(Services, Res, st.Panel);
            if (h.Error != null)
            {
                // HS2: an unreadable file keeps the texture the panel has (the prefab's, or the previous one)
                ReportOnce(FindingSeverity.Warning, "item.panelMissing", "panel image: " + h.Error + " (the previous image stays)");
                return;
            }
            ItemTextures.Release(ref panelTex);
            panelTex = h;
            PanelSource = h.Source;
            Writer.WritePanel(PanelComponent, h.Texture);
        }

        void ApplyOptions()
        {
            var ic = Component;
            var st = St;
            if (ic?.optionInfos == null || st?.Options == null) return;
            for (int i = 0; i < st.Options.Count && i < ic.optionInfos.Count; i++)
            {
                var o = ic.optionInfos[i];
                if (o == null) continue;
                bool on = st.Options[i];
                SetActive(on ? o.objectsOff : o.objectsOn, false);
                SetActive(on ? o.objectsOn : o.objectsOff, true);
            }
        }

        static void SetActive(List<GameObject> l, bool v)
        {
            if (l == null) return;
            foreach (var g in l) if (g && g.activeSelf != v) g.SetActive(v);
        }

        void PlayOnLoad()
        {
            var pc = ParticleComponent;
            if (pc == null || pc.playOnLoad == 0 || pc.particlesPlay == null || !Application.isPlaying) return;
            foreach (var p in pc.particlesPlay) if (p) p.Play();
        }

        // ------------------------------------------------------------ lifecycle

        protected internal override void ReleaseContent()
        {
            if (released) return;
            released = true;
            if (Instance && State == RealizationState.Built && ItemMotionHooks.Releasing != null)
            {
                try { ItemMotionHooks.Releasing(this); }
                catch (Exception e) { Debug.LogWarning("item " + NodeId + ": ItemMotionHooks.Releasing threw: " + e); }
            }
            Motion = null;
            for (int k = 0; k < 3; k++) { ItemTextures.Release(ref patternTex[k]); PatternSources[k] = null; }
            ItemTextures.Release(ref panelTex);
            PanelSource = null;
            if (bundleOwner != null && Services.Catalog != null) { Services.Catalog.Release(bundleOwner); Services.Catalog.NoteRealizationDestroyed(); }
            bundleOwner = null;
            if (Icon is object) ItemIcons.Unregister(Icon);
            MaterialEdits.Release();
            Writer.Release();
        }

        protected internal override void DestroyContent()
        {
            ReleaseContent();
            if (Instance) UnityObjects.Destroy(Instance);
            Instance = null; Component = null; Animator = null; Attach = null; Row = null; Motion = null;
            ParticleComponent = null; PanelComponent = null; IconComponent = null; SoundComponent = null; Icon = null; Features = null;
            DynamicBones = Array.Empty<DynamicBone>();
            Particles = Array.Empty<ParticleSystem>();
        }

        protected internal override Transform ChildAttach(string point, out AttachNote note)
        {
            note = point != null ? new AttachNote(FindingSeverity.Info, "scene.pointIgnored", "attach point \"" + point + "\" under an item is ignored") : default;
            return State == RealizationState.Built && Attach ? Attach : Holder;
        }

        // ------------------------------------------------------------ changes

        static readonly string[] PatternTexPaths =
        {
            "item.colors[0].pattern.item", "item.colors[0].pattern.file",
            "item.colors[1].pattern.item", "item.colors[1].pattern.file",
            "item.colors[2].pattern.item", "item.colors[2].pattern.file",
        };

        protected internal override void ApplyChanges(IReadOnlyCollection<string> paths)
        {
            if (Touches(paths, "item.ref") || Touches(paths, "item.group") || Touches(paths, "item.category"))
            {
                Runtime.RebuildContent(this);
                return;
            }
            if (State != RealizationState.Built || !Instance) return;
            if (Touches(paths, "item.materialOverrides") && MaterialEdits.NeedsRebuild)
            {
                Runtime.RebuildContent(this);
                return;
            }
            using (OwnBundles()) ApplyLoadedChanges(paths);
        }

        void ApplyLoadedChanges(IReadOnlyCollection<string> paths)
        {
            bool colors = false, panel = false, options = false, edits = false;
            var patterns = new bool[3];
            foreach (var p in paths)
            {
                if (FieldPath.Overlaps(p, "item.colors"))
                {
                    bool underTex = false;
                    for (int i = 0; i < PatternTexPaths.Length; i++)
                    {
                        if (FieldPath.Overlaps(p, PatternTexPaths[i])) patterns[i / 2] = true;
                        if (FieldPath.IsPrefixOf(PatternTexPaths[i], p)) underTex = true;
                    }
                    // colors[3].pattern.* is stored only (the glass slot's pattern is unused); the full write is harmless
                    if (!underTex) colors = true;
                }
                if (FieldPath.Overlaps(p, "item.alpha") || FieldPath.Overlaps(p, "item.emission") || FieldPath.Overlaps(p, "item.lightCancel")) colors = true;
                if (FieldPath.Overlaps(p, "item.panel")) panel = true;
                if (FieldPath.Overlaps(p, "item.options")) options = true;
                if (FieldPath.Overlaps(p, "item.materialOverrides")) edits = true;
            }
            for (int k = 0; k < 3; k++)
            {
                int kk = k;
                if (patterns[k]) Part("pattern" + (k + 1), () => LoadPattern(kk));
            }
            if (panel) Part("panel", LoadPanel);
            if (colors) Part("colors", WriteColors);
            if (edits || colors || panel || patterns[0] || patterns[1] || patterns[2]) Part("materialEdits", MaterialEdits.Apply);
            if (options) Part("options", ApplyOptions);

            if (ItemMotionHooks.Changed == null)
            {
                if (Animator && Touches(paths, "item.anime.speed")) Animator.speed = St.Anime.Speed;
                return;
            }
            List<string> motion = null;
            foreach (var p in paths)
                foreach (var pre in ItemMotionHooks.MotionPrefixes)
                    if (FieldPath.Overlaps(p, pre)) { (motion ??= new List<string>()).Add(p); break; }
            if (motion != null)
            {
                try { ItemMotionHooks.Changed(this, motion); }
                catch (Exception e)
                {
                    ReportOnce(FindingSeverity.Warning, "item.partFailed", "ItemMotionHooks.Changed threw " + e.GetType().Name + ": " + e.Message);
                    Debug.LogWarning("item " + NodeId + ": ItemMotionHooks.Changed threw: " + e);
                }
            }
        }

        // ------------------------------------------------------------ visibility

        protected internal override void ApplyVisibility(bool visible)
        {
            visibleNow = visible;
            VisibilityApplier.SetOwn(Holder, visible);
            if (Application.isPlaying)
                foreach (var p in Particles)
                {
                    if (!p) continue;
                    if (visible) p.Play(); else p.Pause();
                }
            RefreshIcon();
        }

        /// <summary>The icon object's active state: the app option items.gimmickIcons and the node's visibility.</summary>
        internal void RefreshIcon()
        {
            if (!Icon) return;
            bool on = ItemIcons.Visible && visibleNow;
            if (Icon.gameObject.activeSelf != on) Icon.gameObject.SetActive(on);
        }

        /// <summary>Layer-0 normalized time of an enabled item Animator, else NaN.</summary>
        internal float CurrentAnimeTime()
        {
            if (!Animator || !Animator.enabled || !Animator.isActiveAndEnabled || Animator.runtimeAnimatorController == null) return float.NaN;
            return Animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
        }
    }
}
