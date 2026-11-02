// Studio character animation facade: what the Studio layer calls to
// load a list motion on a character and control it. One component on the character root (CharaAnimation.Of(root)),
// driven by AnimationStage (MotionPhase.Animation) and HandShapeStage (MotionPhase.Shape).
//
//   Load(row, t)          the load steps: controller through the catalog (reassigned only when it differs); an H row
//                         also gets its override controller, breast layer, `motion`, PV flags, `speed` and its sway
//                         table (HSwayTable); option items
//                         then OptionItemsLoaded (the owner's per-object policies, e.g. the URP shader mapping),
//                         Play(state, 0, t), `height1`/`height` = body height, option parameters re-applied,
//                         state hash stored for force loop.
//   Speed / Pattern / ForceLoop / OptionVisible / OptionParam1/2 / NormalizedTime / Play / Restart / Stop
//   Save / Restore         the per-character fields (ids with GUID, speed, pattern, loop, option
//                         visibility, option params, raw normalized time, hand patterns).
// Per step (Tick, called by AnimationStage): evaluate with dt x Speed, restore the shape bones, then the follow-ups
// in the same step, then the sway table of an H
// row: the line of the layer-0 state sets the bust stage's four enables and the per-side bust shape masks (a mask change
// writes that side's bust bones, puts the bone modifiers on top again and the shape-bone guard keeps the new values; all
// masks of a side open again = the bones' values of the build come back exactly).
//
// Deliberate deviations, each behind an AnimationOptions flag (true = HS2Next default):
//   (a) RestoreShapeBones: every shape-engine target bone is re-written to its post-build value right after sampling
//       (f_studio_34 emotion clips bind *_s bones);
//   (b) ResampleOnSetTime: setting the time / loading / restarting re-samples at once, also at speed 0;
//   (c) ReapplyHeightOnCardChange: OnCardChanged() re-applies `height`;
//   (d) SetMotionOnSyncedItemsAtLoad;  (e) SkipMissingOptionParents;
//   (f) PlayRootMotionInPlace: the Animator applies root motion as the game's does (with it off, Unity puts a clip's
//       root-node orientation offset on the skeleton root instead of cf_J_Hips), and p_cf_anim is put back to its rest
//       after every evaluation, so unbaked root motion never moves the character; one warning;
//   (g) ClearStaleHOverride — H playback only.
//   GameFrameDelay (false): force-loop restart and item sync queued for the next step as in the game
//   instead of applied in the same step.
using System;
using System.Collections.Generic;
using HS2Next.Chara;
using HS2Next.Content.Studio;
using HS2Next.IO;
using HS2Next.Motion.DynamicBones;
using UnityEngine;

namespace HS2Next.Motion.Animation
{
    /// <summary>Switches of the animation family (copied per character from <see cref="AnimationSetup.Defaults"/>).</summary>
    [Serializable]
    public sealed class AnimationOptions
    {
        public AnimationEngine Engine = AnimationEngine.Playable;
        public bool RestoreShapeBones = true;
        public bool ResampleOnSetTime = true;
        public bool ReapplyHeightOnCardChange = true;
        public bool SetMotionOnSyncedItemsAtLoad = true;
        public bool SkipMissingOptionParents = true;
        public bool PlayRootMotionInPlace = true;
        public bool ClearStaleHOverride = true;
        /// <summary>Game parity: force-loop restart and synced-item jump are queued and consumed one step later.</summary>
        public bool GameFrameDelay;
        public AnimationOptions Clone() => (AnimationOptions)MemberwiseClone();
    }

    /// <summary>Per-character animation state as a scene stores it (HS2Next keeps the fields, not the layout).</summary>
    [Serializable]
    public sealed class AnimationSceneState
    {
        /// <summary>Source GUID of the row; null/empty = resolve the raw key like the game.</summary>
        public string Guid;
        public int Group, Category, No;
        public float Speed = 1f, Pattern, NormalizedTime;
        public bool ForceLoop, OptionVisible = true;
        public float OptionParam1, OptionParam2;
        public int HandLeft, HandRight;
        public override string ToString() => $"{Guid}:{Group}/{Category}/{No} t {NormalizedTime:F4} speed {Speed} pattern {Pattern} loop {ForceLoop} options {OptionVisible} params ({OptionParam1}, {OptionParam2}) hands ({HandLeft}, {HandRight})";
    }

    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class CharaAnimation : MonoBehaviour
    {
        public static CharaAnimation Of(GameObject chara) => chara ? chara.GetComponent<CharaAnimation>() : null;

        public AnimationOptions Options { get; private set; } = new AnimationOptions();
        public CharaContext Context { get; private set; }
        public Animator Animator { get; private set; }
        public AnimationStage Stage { get; private set; }
        public HandShapeStage Hands { get; internal set; }
        public readonly AnimeOptionItems Items = new AnimeOptionItems();
        /// <summary>The sway table of the H row playing (empty for plain rows: everything enabled).</summary>
        public readonly HSwayTable Sway = new HSwayTable();
        /// <summary>One line per load / warning (also forwarded to the character log while building).</summary>
        public readonly List<string> Log = new List<string>();

        /// <summary>
        /// Raised by <see cref="Load(StudioAnime, float)"/> right after the row's option items were (re)instantiated,
        /// parented and given their visibility, before the motion plays. The option items are created after the
        /// character's own setup (URP shader mapping and other per-object policies of the owner), so the owner applies
        /// those policies to the new objects here. Arguments: this component and the new objects
        /// (<see cref="AnimeOptionItems.Objects"/>: each item root plus each child an item moved under a character bone;
        /// disjoint hierarchies that together hold every new renderer). Not raised when the row has no option items.
        /// A handler that throws is logged and does not stop the load.
        /// </summary>
        public event Action<CharaAnimation, IReadOnlyList<GameObject>> OptionItemsLoaded;

        /// <summary>
        /// Raised right before a controller is bound to the Animator (a load with another controller, an H override). When it
        /// binds, the Animator takes the current value of every property the controller animates anywhere as that property's
        /// default, and writes the default on every evaluation where the playing state does not animate it (write defaults;
        /// measured: f_studio_34 binds cf_J_Legsk_05_00/01, which its Idle_00 does not animate). Stages that leave their
        /// writes on the bones between frames put the bones the game would have reset by now back first, so the defaults do
        /// not depend on the character's history (the dynamic-bone chains, DynamicBoneSetup.Attach). A handler that throws is
        /// logged and does not stop the load.
        /// </summary>
        public event Action<CharaAnimation> BeforeBind;

        AnimationDriver driver;
        RuntimeAnimatorController baseController;
        (string, string)? overrideKey;
        int stateHash, prevExtraLayer;
        string heightParam = "", breastParam = "";
        float speed = 1f, pattern, param1, param2;
        bool optionVisible = true, characterVisible = true, rootWarned;
        ShapeBoneGuard shapeGuard;
        // the shape-bone guard's build values no longer match the shapes (a shape edit while a bust mask was on)
        bool shapeBuildStale;
        // override controllers built by this component (released with the base controller, at Stop and on destroy)
        readonly List<AnimatorOverrideController> ownedOverrides = new List<AnimatorOverrideController>();
        Transform[] restBones;
        // p_cf_anim's local pose at attach (kept by PlayRootMotionInPlace)
        Vector3 animRestPos;
        Quaternion animRestRot = Quaternion.identity;
        Vector3[] restPos, restScale;
        Quaternion[] restRot;

        // ------------------------------------------------------------ state

        /// <summary>The row last requested (plain or H); what Save stores.</summary>
        public StudioAnime Current { get; private set; }
        /// <summary>The row that drives the Animator (null = no motion).</summary>
        public StudioAnime Playing { get; private set; }
        public bool IsLoaded => driver != null && driver.Valid;
        /// <summary>The engine of the current motion (null when none).</summary>
        public AnimationDriver Driver => driver;
        /// <summary>An H row is playing.</summary>
        public bool IsHPlaying { get; private set; }
        /// <summary>The base controller of the current motion (null when none).</summary>
        public RuntimeAnimatorController BaseController => baseController;
        /// <summary>The override controller pair last applied by an H row (null when none is remembered).</summary>
        public string OverrideBundle => overrideKey?.Item1;
        public string OverrideFile => overrideKey?.Item2;
        /// <summary>The extra layer an H row set to weight 1 (0 = none / the base layer).</summary>
        public int ExtraLayer => prevExtraLayer;
        /// <summary>The breast layer column of the last H row loaded (kept through plain rows, as HS2 keeps it; 0 at first).</summary>
        public int LastHBreastLayer { get; private set; }
        /// <summary>`isAnimeMotion` (H rows with `isMotion`).</summary>
        public bool IsAnimeMotion { get; private set; }
        /// <summary>Eight PV (IK target copy) flags, plain rows enable 0-3, H rows copy the row.</summary>
        public readonly bool[] PvFlags = new bool[8];
        /// <summary>Body height slider value used for `height` (card shapeValueBody[0]; males 0.75).</summary>
        public float BodyHeight { get; set; }
        /// <summary>Bust size slider value (shapeValueBody[1]) used for the H breast parameter.</summary>
        public float BodyBust { get; set; }
        /// <summary>Name of the parameter that gets the body height ("" = none).</summary>
        public string HeightParameter => heightParam;
        /// <summary>H breast parameter name ("" = none).</summary>
        public string BreastParameter => breastParam;
        /// <summary>Hash of the state that force loop restarts (`nameHadh`).</summary>
        public int StateHash => stateHash;
        /// <summary>Unbaked root motion was seen with the current controller (played in place, deviation f).</summary>
        public bool UnbakedRootMotion { get; private set; }
        public int ShapeBoneCount => shapeGuard?.Count ?? 0;
        /// <summary>The bones the shape guard re-writes (deviation a).</summary>
        public IReadOnlyList<Transform> ShapeBones => shapeGuard != null ? shapeGuard.Bones : (IReadOnlyList<Transform>)Array.Empty<Transform>();

        /// <summary>`animeSpeed`: ≥ 0, 0 = pause; survives motion changes.</summary>
        public float Speed { get => speed; set => speed = float.IsNaN(value) ? 0f : Mathf.Max(0f, value); }

        /// <summary>`animePattern`, 0..1.</summary>
        public float Pattern
        {
            get => pattern;
            set
            {
                pattern = Mathf.Clamp01(value);
                if (IsLoaded && IsHPlaying && IsAnimeMotion && driver.HasParameter("motion")) driver.SetFloat("motion", pattern);
                Items.SetMotion(pattern);
            }
        }

        /// <summary>`isAnimeForceLoop`.</summary>
        public bool ForceLoop { get; set; }

        /// <summary>`animeOptionVisible`.</summary>
        public bool OptionVisible { get => optionVisible; set { optionVisible = value; Items.ApplyVisibility(optionVisible && characterVisible); } }
        /// <summary>The character's visibility in the object tree (items are shown only when both are true).</summary>
        public bool CharacterVisible { get => characterVisible; set { characterVisible = value; Items.ApplyVisibility(optionVisible && characterVisible); } }

        /// <summary>`animeOptionParam[0]`: written to the Animator only while an H row plays.</summary>
        public float OptionParam1 { get => param1; set { param1 = value; if (IsLoaded && IsHPlaying && heightParam.Length > 0) driver.SetFloat(heightParam, value); } }
        /// <summary>`animeOptionParam[1]`: written to the Animator only while an H row plays.</summary>
        public float OptionParam2 { get => param2; set { param2 = value; if (IsLoaded && IsHPlaying && breastParam.Length > 0) driver.SetFloat(breastParam, value); } }

        /// <summary>Raw layer-0 normalized time. Setting it jumps the current state (Timeline-style) and re-samples at once (deviation b).</summary>
        public float NormalizedTime
        {
            get => IsLoaded ? driver.State(0).normalizedTime : 0f;
            set
            {
                if (!IsLoaded) return;
                driver.Play(stateHash, 0, value);
                if (Options.ResampleOnSetTime) Resample();
            }
        }

        /// <summary>Layer-0 state info of the body (default when nothing is loaded).</summary>
        public AnimatorStateInfo StateInfo => IsLoaded ? driver.State(0) : default;

        // ------------------------------------------------------------ setup

        internal void Bind(CharaContext ctx, Animator animator, AnimationOptions options)
        {
            Context = ctx;
            Animator = animator;
            if (animator) { animRestPos = animator.transform.localPosition; animRestRot = animator.transform.localRotation; }
            Options = options ?? new AnimationOptions();
            Stage = new AnimationStage(this);
            ReadBodyShape(ctx);
            shapeGuard = ShapeBoneGuard.Capture(ctx);
            CaptureRest(ctx);
            for (int i = 0; i < 4; i++) PvFlags[i] = true;
        }

        // BodyHeight / BodyBust from the character's documents (card builds carry documents
        // converted from the card, with the values the card tree held).
        void ReadBodyShape(CharaContext ctx)
        {
            var bv = ctx.Chara?.Body?.Shape;
            if (bv == null) { try { bv = ctx.Card?.Body?.Get("shapeValueBody")?.Floats(); } catch (Exception) { } }
            BodyHeight = ctx.IsMale ? 0.75f : bv != null && bv.Length > 0 ? bv[0] : 0.5f;
            BodyBust = bv != null && bv.Length > 1 ? bv[1] : 0.5f;
        }

        /// <summary>
        /// After a body-shape edit: the animator parameters BodyHeight / BodyBust from
        /// the documents (written to a loaded controller at once), the shape-bone guard and the rest pose of the shape-map
        /// bones re-captured from the bones as the shape engine left them.
        /// </summary>
        public void RefreshBodyShape(CharaContext ctx)
        {
            if (ctx == null) return;
            ReadBodyShape(ctx);
            if (IsLoaded && driver != null && driver.Valid)
            {
                if (heightParam.Length > 0 && !IsHPlaying) driver.SetFloat(heightParam, BodyHeight);
                if (breastParam.Length > 0 && !IsHPlaying) driver.SetFloat(breastParam, BodyBust);
            }
            // the captures hold the body's own cf_N_height: the heels' lift off for them and back at once, so the later units of
            // the flush (the bust chains) see the body as it stands
            var lift = ctx.Motion?.Get<Heels.HeelsLift>();
            lift?.Unlift();
            shapeGuard?.Recapture();
            NoteShapeRecapture();
            if (shapeGuard != null && restBones != null)
            {
                var shapeSet = new HashSet<Transform>(shapeGuard.Bones);
                for (int i = 0; i < restBones.Length; i++)
                    if (restBones[i] && shapeSet.Contains(restBones[i]))
                    { restPos[i] = restBones[i].localPosition; restRot[i] = restBones[i].localRotation; restScale[i] = restBones[i].localScale; }
            }
            lift?.Apply();
        }

        void CaptureRest(CharaContext ctx)
        {
            // The p_cf_anim skeleton (the bones a body controller can bind), by name from the prefab, values from the
            // built character (post-shape): what Stop() returns to.
            GameObject prefab = null;
            try
            {
                prefab = ctx.Catalog?.LoadAsset<GameObject>("studio/base/00.unity3d", "p_cf_anim")
                         ?? ctx.Catalog?.LoadAsset<GameObject>("chara/oo_base.unity3d", "p_cf_anim");
            }
            catch (Exception) { }
            var list = new List<Transform>();
            if (prefab && ctx.BodyBones != null)
            {
                var seen = new HashSet<string>();
                foreach (var t in prefab.GetComponentsInChildren<Transform>(true))
                    if (seen.Add(t.name) && ctx.BodyBones.TryGetValue(t.name, out var b) && b) list.Add(b);
            }
            restBones = list.ToArray();
            restPos = new Vector3[restBones.Length]; restScale = new Vector3[restBones.Length]; restRot = new Quaternion[restBones.Length];
            for (int i = 0; i < restBones.Length; i++) { restPos[i] = restBones[i].localPosition; restRot[i] = restBones[i].localRotation; restScale[i] = restBones[i].localScale; }
        }

        /// <summary>Re-capture the shape-bone values after a body shape edit (otherwise deviation (a) would restore the old shape).</summary>
        public void RecaptureShapeBones()
        {
            var lift = Context?.Motion?.Get<Heels.HeelsLift>();   // as RefreshBodyShape: the capture without the heels' lift
            lift?.Unlift();
            shapeGuard?.Recapture();
            NoteShapeRecapture();
            lift?.Apply();
        }

        // A capture taken while a bust mask is on holds the masked values: opening the masks then evaluates the shapes again.
        void NoteShapeRecapture()
        {
            var shape = Context?.BodyShape;
            if (shape != null && (shape.BustMask(0) != 0 || shape.BustMask(1) != 0)) shapeBuildStale = true;
        }

        // ------------------------------------------------------------ load

        /// <summary>Resolves a (GUID, group, category, no) reference: the GUID's own row, else the game's global winner.</summary>
        public static StudioAnime Resolve(StudioDb db, string guid, int group, int category, int no)
        {
            if (db == null) return null;
            if (!string.IsNullOrEmpty(guid) && db.TryGetAnime(guid, group, category, no, out var a)) return a;
            return db.FindAnime(group, category, no);
        }

        /// <summary>The label of the bundle owner of the motion and its option items ("chara:&lt;node id 8&gt;/anime").</summary>
        public string BundleOwnerLabel = "chara/anime";

        // One generation per motion load that opened bundles; the previous one is released (Runtime/Content/BundleOwners.cs).
        HS2Next.Content.OwnerGenerations bundleOwners;

        /// <summary>Releases the bundles of the current motion (the character is gone).</summary>
        public void ReleaseBundleOwners() => bundleOwners?.ReleaseAll();

        /// <summary>`LoadAnime`. False when nothing was played (missing row or controller).</summary>
        public bool Load(StudioAnime row, float normalizedTime = 0f)
        {
            if (row == null || Context == null) return false;
            var cat = Context.Catalog;
            if (cat == null) return LoadCore(row, normalizedTime);
            bundleOwners ??= new HS2Next.Content.OwnerGenerations(cat.Ledger, BundleOwnerLabel);
            var next = bundleOwners.Begin();
            try { using (cat.Own(next)) return LoadCore(row, normalizedTime); }
            finally { bundleOwners.End(next); }
        }

        bool LoadCore(StudioAnime row, float normalizedTime)
        {
            RuntimeAnimatorController ctrl = null;
            try { ctrl = Context.Catalog?.LoadAsset<RuntimeAnimatorController>(row); }
            catch (Exception e) { Note($"anim: controller {row.BundlePath}/{row.FileName} failed ({e.GetType().Name}: {e.Message})"); }
            if (!ctrl) { Note($"anim: controller {row.BundlePath}/{row.FileName} not found; motion unchanged"); return false; }

            // step 2: base controller (rebind only when it differs, or when the engine was switched)
            if (driver == null || !driver.Valid || ctrl != baseController || EngineOf(driver) != Options.Engine)
            {
                SetController(ctrl);
                ReleaseOverrides();
                baseController = ctrl;
                overrideKey = null;
                prevExtraLayer = 0;
            }

            // step 3
            breastParam = "";
            if (row.IsH) LoadHPart(row, normalizedTime);
            else
            {
                overrideKey = null;              // bookkeeping only; an applied override stays
                for (int i = 0; i < 4; i++) PvFlags[i] = true;
                IsAnimeMotion = false;
                IsHPlaying = false;
                Sway.Invalidate();               // the table stays loaded (HS2); no line matches a plain row's state
            }

            // step 4: option items
            Items.Load(row, Context, BodyHeight, pattern, Options);
            foreach (var l in Items.Log) Note("anim: " + l);
            Items.ApplyVisibility(optionVisible && characterVisible);
            RaiseOptionItemsLoaded();

            // step 5
            stateHash = Animator.StringToHash(row.Clip ?? "");
            if (!driver.HasState(0, stateHash)) Note($"anim: state \"{row.Clip}\" not on layer 0 of {ctrl.name}");
            driver.Play(stateHash, 0, normalizedTime);
            // step 6
            heightParam = driver.FirstParameter("height1", "height");
            if (heightParam.Length > 0) driver.SetFloat(heightParam, BodyHeight);
            // step 7
            OptionParam1 = param1;
            OptionParam2 = param2;
            // step 8
            Current = Playing = row;
            if (Options.ResampleOnSetTime) Resample();
            return true;
        }

        /// <summary>Load by reference.</summary>
        public bool Load(StudioDb db, string guid, int group, int category, int no, float normalizedTime = 0f) =>
            Load(Resolve(db, guid, group, category, no), normalizedTime);

        void RaiseOptionItemsLoaded()
        {
            var handlers = OptionItemsLoaded;
            if (handlers == null || Items.Count == 0) return;
            var objects = Items.Objects();
            if (objects.Count == 0) return;
            foreach (Action<CharaAnimation, IReadOnlyList<GameObject>> h in handlers.GetInvocationList())
            {
                try { h(this, objects); }
                catch (Exception e) { Note($"anim: OptionItemsLoaded handler failed ({e.GetType().Name}: {e.Message})"); }
            }
        }

        void RaiseBeforeBind()
        {
            var handlers = BeforeBind;
            if (handlers == null) return;
            foreach (Action<CharaAnimation> h in handlers.GetInvocationList())
            {
                try { h(this); }
                catch (Exception e) { Note($"anim: BeforeBind handler failed ({e.GetType().Name}: {e.Message})"); }
            }
        }

        void LoadHPart(StudioAnime row, float t)
        {
            // The H part of a load: the override controller on the current one (by clip name), the breast layer, `motion`,
            // the PV flags, `speed` = 1, the sway table.
            bool hasOverride = !string.IsNullOrEmpty(row.OverrideBundle) && !string.IsNullOrEmpty(row.OverrideFile);
            if (hasOverride)
            {
                var key = (row.OverrideBundle, row.OverrideFile);
                if (overrideKey != key)
                {
                    RuntimeAnimatorController ov = null;
                    try { ov = Context.Catalog?.LoadAssetAt<RuntimeAnimatorController>(row.Zipmod, row.OverrideBundle, "", row.OverrideFile); }
                    catch (Exception e) { Note($"anim: override {row.OverrideBundle}/{row.OverrideFile} failed ({e.GetType().Name}: {e.Message})"); }
                    if (ov)
                    {
                        var aoc = (AnimatorOverrideController)BuildOverride(driver.Controller, ov);   // stacks on an applied override
                        ownedOverrides.Add(aoc);
                        SetController(aoc);
                    }
                    else Note($"anim: override {row.OverrideBundle}/{row.OverrideFile} not found; base controller kept");
                    overrideKey = key;
                }
            }
            else
            {
                overrideKey = null;
                if (Options.ClearStaleHOverride && driver.Controller != baseController) { SetController(baseController); ReleaseOverrides(); }   // deviation (g)
            }
            IsAnimeMotion = row.IsMotion;
            IsHPlaying = true;
            LastHBreastLayer = row.BreastLayer;
            breastParam = driver.FirstParameter("Breast1", "Breast", "breast");
            int layers = driver.LayerCount;
            if (prevExtraLayer > 0 && prevExtraLayer < layers) driver.SetLayerWeight(prevExtraLayer, 0f);
            int bl = row.BreastLayer;
            if (bl > 0 && bl < layers) driver.SetLayerWeight(bl, 1f);
            if (bl >= 0 && bl < layers) driver.Play(Animator.StringToHash(row.Clip ?? ""), bl, t != 0f ? t : float.NegativeInfinity);
            prevExtraLayer = bl;
            if (IsAnimeMotion && driver.HasParameter("motion")) driver.SetFloat("motion", pattern);
            for (int i = 0; i < 8; i++) PvFlags[i] = row.PvFlags[i];
            if (breastParam.Length > 0) driver.SetFloat(breastParam, BodyBust);
            if (driver.HasParameter("speed")) driver.SetFloat("speed", 1f);
            // the sway table: loaded with the row, or cleared (everything enabled); applied by the next sway step
            if (!string.IsNullOrEmpty(row.YureBundle) && !string.IsNullOrEmpty(row.YureFile))
            {
                if (!Sway.Load(Context.Catalog, row, out var note)) Note("anim: " + note);
            }
            else Sway.Clear();
        }

        void ReleaseOverrides()
        {
            foreach (var o in ownedOverrides)
            {
                if (!o) continue;
                if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
            }
            ownedOverrides.Clear();
        }

        void SetController(RuntimeAnimatorController ctrl)
        {
            RaiseBeforeBind();   // the new driver binds the current pose as the defaults (see BeforeBind)
            driver?.Dispose();
            Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            Animator.applyRootMotion = true;   // as the game's Animator: the root-node orientation offset stays on cf_J_Hips
            driver = AnimationDriver.Create(Options.Engine, Animator, ctrl, "anim:" + (Context.Root ? Context.Root.name : name));
            rootWarned = false;
            UnbakedRootMotion = false;
        }

        static AnimationEngine EngineOf(AnimationDriver d) => d is AnimatorEngineDriver ? AnimationEngine.Animator : AnimationEngine.Playable;

        /// <summary>
        /// An AnimatorOverrideController on <paramref name="current"/> replacing every clip whose name matches a clip
        /// of <paramref name="overrideCtrl"/>. Wrapping an override controller again stacks (game quirk). It is named
        /// after the override controller, as HS2 names it.
        /// </summary>
        public static RuntimeAnimatorController BuildOverride(RuntimeAnimatorController current, RuntimeAnimatorController overrideCtrl)
        {
            var byName = new Dictionary<string, AnimationClip>(StringComparer.Ordinal);
            foreach (var c in overrideCtrl.animationClips) if (c && !byName.ContainsKey(c.name)) byName[c.name] = c;
            var aoc = new AnimatorOverrideController(current) { name = overrideCtrl.name };
            var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>(aoc.overridesCount);
            aoc.GetOverrides(pairs);
            for (int i = 0; i < pairs.Count; i++)
                if (pairs[i].Key && byName.TryGetValue(pairs[i].Key.name, out var rep)) pairs[i] = new KeyValuePair<AnimationClip, AnimationClip>(pairs[i].Key, rep);
            aoc.ApplyOverrides(pairs);
            return aoc;
        }

        // ------------------------------------------------------------ control

        /// <summary>Jump to another state of the loaded controller (layer 0) at a normalized time; it becomes the force-loop state.</summary>
        public void Play(string state, float normalizedTime = 0f)
        {
            if (!IsLoaded || string.IsNullOrEmpty(state)) return;
            stateHash = Animator.StringToHash(state);
            driver.Play(stateHash, 0, normalizedTime);
            if (Options.ResampleOnSetTime) Resample();
        }

        /// <summary>Restart button: every layer's current state and every animated item's state at time 0.</summary>
        public void Restart()
        {
            if (!IsLoaded) return;
            for (int i = 0; i < driver.LayerCount; i++) driver.Play(driver.State(i).shortNameHash, i, 0f);
            Items.Restart();
            if (Options.ResampleOnSetTime) { driver.Evaluate(0f); KeepInPlace(); AfterSample(); for (int i = 0; i < Items.Count; i++) Items.Items[i].Driver?.Evaluate(0f); }
        }

        /// <summary>
        /// HS2Next addition (Studio has no stop; a new character shows 0/0/0): release the motion and its option items and
        /// put the p_cf_anim skeleton back to the post-build pose captured at attach. Speed, pattern, loop and option
        /// settings are kept; the Animator is disabled again (as built).
        /// </summary>
        public void Stop()
        {
            Items.Release();
            bundleOwners?.ReleaseAll();
            driver?.Dispose();
            driver = null;
            ReleaseOverrides();
            Sway.Clear();
            if (Sway.ResetApplied()) ApplySway();
            baseController = null;
            overrideKey = null;
            Playing = Current = null;
            IsHPlaying = IsAnimeMotion = false;
            heightParam = breastParam = "";
            if (Animator) { Animator.runtimeAnimatorController = null; Animator.enabled = false; }
            if (restBones != null)
                for (int i = 0; i < restBones.Length; i++)
                    if (restBones[i]) { restBones[i].localPosition = restPos[i]; restBones[i].localRotation = restRot[i]; restBones[i].localScale = restScale[i]; }
        }

        /// <summary>Card replaced: re-apply `height` (deviation c), re-arm counter-scale, re-capture shape bones.</summary>
        public void OnCardChanged()
        {
            RecaptureShapeBones();
            if (Options.ReapplyHeightOnCardChange && IsLoaded && !IsHPlaying && heightParam.Length > 0) driver.SetFloat(heightParam, BodyHeight);
            Items.RearmCounterScale();
        }

        /// <summary>Character guide scale changed: item roots back to their prefab scale.</summary>
        public void OnCharacterScaleChanged() => Items.ResetScales();

        // ------------------------------------------------------------ scene

        public AnimationSceneState Save() => new AnimationSceneState
        {
            Guid = Current?.Guid, Group = Current?.Group ?? 0, Category = Current?.Category ?? 0, No = Current?.No ?? 0,
            Speed = speed, Pattern = pattern, NormalizedTime = NormalizedTime, ForceLoop = ForceLoop, OptionVisible = optionVisible,
            OptionParam1 = param1, OptionParam2 = param2, HandLeft = Hands?[0] ?? 0, HandRight = Hands?[1] ?? 0,
        };

        /// <summary>Scene load: motion at the saved raw time, speed, option params, then loop, visibility, pattern and hand patterns (left, right).</summary>
        public bool Restore(StudioDb db, AnimationSceneState s)
        {
            if (s == null) return false;
            ForceLoop = s.ForceLoop;
            optionVisible = s.OptionVisible;
            param1 = s.OptionParam1; param2 = s.OptionParam2;
            pattern = Mathf.Clamp01(s.Pattern);
            bool ok = Load(Resolve(db, s.Guid, s.Group, s.Category, s.No), s.NormalizedTime);
            Speed = s.Speed;
            OptionParam1 = s.OptionParam1; OptionParam2 = s.OptionParam2;
            Pattern = s.Pattern;
            OptionVisible = s.OptionVisible;
            if (Hands != null) { Hands.SetPattern(0, s.HandLeft); Hands.SetPattern(1, s.HandRight); }
            return ok;
        }

        // ------------------------------------------------------------ per step

        /// <summary>Stage A (see AnimationStage). No allocations.</summary>
        internal void Tick(in MotionFrame frame)
        {
            if (driver == null || !driver.Valid) return;
            driver.Evaluate(frame.Dt * speed);
            KeepInPlace();
            AfterSample();
            if (!rootWarned && Animator && Animator.deltaPosition.sqrMagnitude > 1e-10f)
            {
                rootWarned = true; UnbakedRootMotion = true;
                Note($"anim: unbaked root motion in {driver.Controller.name} ({(Options.PlayRootMotionInPlace ? "played in place" : "applied to p_cf_anim")})");
            }
            var st = driver.State(0);
            if (ForceLoop && !st.loop && st.normalizedTime >= 1f)
            {
                driver.Play(stateHash, 0, 0f);
                if (!Options.GameFrameDelay) { driver.Evaluate(0f); KeepInPlace(); AfterSample(); st = driver.State(0); }
            }
            Items.Step(frame.Dt, st.shortNameHash, st.normalizedTime, true, Options.GameFrameDelay);
            if (Sway.Step(st.shortNameHash)) ApplySway();
        }

        void Resample()
        {
            driver.Evaluate(0f);
            KeepInPlace();
            AfterSample();
            var st = driver.State(0);
            Items.SyncNow(st.shortNameHash, st.normalizedTime);
            if (Sway.Step(st.shortNameHash)) ApplySway();
        }

        // ------------------------------------------------------------ sway table

        /// <summary>The bust stage of this character (null when the character has none).</summary>
        public BustStage BustStage => Context?.Motion?.Get<BustStage>();

        // The sway table's applied state on the character: the bust stage's four enables, then the bust shape masks.
        void ApplySway()
        {
            var bust = BustStage;
            bust?.SetAnimeSwitches(Sway.BustL, Sway.BustR, Sway.HipL, Sway.HipR);
            if (ApplyShapeMasks(Sway.MaskL, Sway.MaskR)) bust?.ResetPosition();
        }

        /// <summary>
        /// Sets the body's bust shape masks (bit k = the k-th id of ShapeEngine.BustMaskIds evaluated at 0.5 on that side).
        /// A side whose mask changed is written and the bone modifiers go on top again; the shape-bone guard keeps the new
        /// values. A side whose masks all open again gets its build values back exactly (unless a shape edit happened while
        /// masked: then the shapes are evaluated again). True when a mask changed.
        /// </summary>
        public bool ApplyShapeMasks(int maskL, int maskR)
        {
            var shape = Context?.BodyShape;
            if (shape == null || !shape.HasBustMasks) return false;
            bool any = false, wroteL = false, wroteR = false;
            for (int side = 0; side < 2; side++)
            {
                int m = side == 0 ? maskL : maskR;
                if (shape.BustMask(side) == m) continue;
                any = true;
                bool back = m == 0 && !shapeBuildStale && shapeGuard != null;
                shape.SetBustMask(side, m, !back);
                if (back) shapeGuard.RestoreBuild(shape.BustMaskBones(side));
                else if (side == 0) wroteL = true; else wroteR = true;
            }
            if (!any) return false;
            if (wroteL || wroteR)
            {
                var mods = Context.Bones;
                if (mods) mods.Apply();   // the bone modifiers on top of the new shape values, as after a shape edit
                if (wroteL) shapeGuard?.Adopt(shape.BustMaskBones(0));
                if (wroteR) shapeGuard?.Adopt(shape.BustMaskBones(1));
            }
            if (shapeBuildStale && shape.BustMask(0) == 0 && shape.BustMask(1) == 0)
            {
                shapeGuard?.Recapture();
                shapeBuildStale = false;
            }
            return true;
        }

        // Deviation (f): unbaked root motion moved p_cf_anim; it goes back to its rest (the motion plays in place).
        void KeepInPlace()
        {
            if (!Options.PlayRootMotionInPlace || !Animator) return;
            var t = Animator.transform;
            var p = t.localPosition; var q = t.localRotation;
            if (p.x != animRestPos.x || p.y != animRestPos.y || p.z != animRestPos.z) t.localPosition = animRestPos;
            if (q.x != animRestRot.x || q.y != animRestRot.y || q.z != animRestRot.z || q.w != animRestRot.w) t.localRotation = animRestRot;
        }

        void AfterSample()
        {
            if (Options.RestoreShapeBones) shapeGuard?.Restore();
        }

        void Note(string s)
        {
            Log.Add(s);
            Context?.Log?.Add(s);
        }

        void OnDestroy()
        {
            Items.Release();
            driver?.Dispose();
            driver = null;
            ReleaseOverrides();
        }
    }

    /// <summary>
    /// Deviation (a): the post-build local pose of every body shape-engine target (`Resources/shape_body_map`
    /// destinations: the *_s bones, cf_N_height, bust _d/_t helpers, skirt dams, ...) re-written after sampling.
    /// Only values that differ are written, so other writers' "unchanged since I wrote it" checks keep working.
    /// </summary>
    sealed class ShapeBoneGuard
    {
        Transform[] bones;
        Vector3[] pos, scl;
        Quaternion[] rot;
        // the values of the last full capture (the build or a shape edit): what an opened bust mask returns to
        Vector3[] bpos, bscl;
        Quaternion[] brot;
        readonly Dictionary<Transform, int> index = new Dictionary<Transform, int>();
        public int Count => bones.Length;
        public IReadOnlyList<Transform> Bones => bones;

        public static ShapeBoneGuard Capture(CharaContext ctx)
        {
            var g = new ShapeBoneGuard();
            var list = new List<Transform>();
            var map = Resources.Load<TextAsset>("shape_body_map");
            if (map && ctx.BodyBones != null)
            {
                var seen = new HashSet<string>();
                foreach (var raw in map.text.Split('\n'))
                {
                    var c = raw.Split('\t');
                    if (c.Length < 2 || !seen.Add(c[1])) continue;
                    if (ctx.BodyBones.TryGetValue(c[1], out var t) && t) list.Add(t);
                }
            }
            g.bones = list.ToArray();
            g.pos = new Vector3[g.bones.Length]; g.scl = new Vector3[g.bones.Length]; g.rot = new Quaternion[g.bones.Length];
            g.bpos = new Vector3[g.bones.Length]; g.bscl = new Vector3[g.bones.Length]; g.brot = new Quaternion[g.bones.Length];
            for (int i = 0; i < g.bones.Length; i++) g.index[g.bones[i]] = i;
            g.Recapture();
            return g;
        }

        public void Recapture()
        {
            for (int i = 0; i < bones.Length; i++)
                if (bones[i])
                {
                    pos[i] = bpos[i] = bones[i].localPosition; rot[i] = brot[i] = bones[i].localRotation; scl[i] = bscl[i] = bones[i].localScale;
                }
        }

        /// <summary>The guarded values of these bones become their current values (a bust mask wrote them); the build
        /// values stay.</summary>
        public void Adopt(IReadOnlyList<Transform> ts)
        {
            for (int k = 0; k < ts.Count; k++)
            {
                var t = ts[k];
                if (!t || !index.TryGetValue(t, out int i)) continue;
                pos[i] = t.localPosition; rot[i] = t.localRotation; scl[i] = t.localScale;
            }
        }

        /// <summary>These bones and their guarded values back to the build values (a bust mask opened again).</summary>
        public void RestoreBuild(IReadOnlyList<Transform> ts)
        {
            for (int k = 0; k < ts.Count; k++)
            {
                var t = ts[k];
                if (!t || !index.TryGetValue(t, out int i)) continue;
                pos[i] = bpos[i]; rot[i] = brot[i]; scl[i] = bscl[i];
                t.localPosition = pos[i]; t.localRotation = rot[i]; t.localScale = scl[i];
            }
        }

        public void Restore()
        {
            for (int i = 0; i < bones.Length; i++)
            {
                var t = bones[i];
                if (!t) continue;
                var p = t.localPosition; var q = t.localRotation; var s = t.localScale;
                if (p.x != pos[i].x || p.y != pos[i].y || p.z != pos[i].z) t.localPosition = pos[i];
                if (q.x != rot[i].x || q.y != rot[i].y || q.z != rot[i].z || q.w != rot[i].w) t.localRotation = rot[i];
                if (s.x != scl[i].x || s.y != scl[i].y || s.z != scl[i].z) t.localScale = scl[i];
            }
        }
    }
}
