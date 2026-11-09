// Character node realization: build through the builder registry (stored documents
// first, then the HS2 card), keep the CharaContext, re-apply URP to option items through CharaAnimation.OptionItemsLoaded (reparenting
// parentless item roots under the holder's OptionItems container), resolve attach points, and apply the stored
// scene state in the documented order:
//   7a anime + hands (CharaAnimation.Restore / Stop)   7b IK driver   7c FK driver   7d joint correction
//   7e face (ApplyStudioState + the non-Studio members, then the Studio mouth open again)   7f look (eye pattern, eye rotations, neck fix angles,
//   neck pattern, look target)   7g accessory visibility   7h clothes states and the other `state` members
//   (CharaStateRealizer).
// The model is never written back from these calls; runtime-owned values reach it only through captures.
// After the build, changed field paths map to single actions; `appearance.` member
// paths go to the character's editor first (Phase 4.0; CharaEdit/CharaRealization.Edit.cs).
// Runtime overrides (Phase 6.2; Overrides/CharaRealization.Overrides.cs): the face, the animation speed and time, and the
// animation row show the winning slot's value wherever the model's value is applied; without a slot nothing changes.
using System;
using System.Collections.Generic;
using HS2Next.Chara;
using HS2Next.Content;
using HS2Next.Motion;
using HS2Next.Motion.Animation;
using HS2Next.Motion.Corrective;
using HS2Next.Motion.Face;
using HS2Next.Motion.Look;
using HS2Next.Save;
using HS2Next.Scenes.Overrides;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    public sealed partial class CharaRealization : NodeRealization
    {
        internal CharaRealization(SceneRuntime runtime, TreeNode node) : base(runtime, node) { }

        /// <summary>The character root (CharaAssembler root) under the holder; null unless built.</summary>
        public GameObject Root { get; private set; }
        public CharaContext Context { get; private set; }
        public CharaAssembler Assembler { get; private set; }
        public ICharaBuilder Builder { get; private set; }
        /// <summary>The card the character was built from (card builder), else null.</summary>
        public CardAppearanceSource Card { get; private set; }
        /// <summary>The stored subtree the character was built from (document builder), else null.</summary>
        public CharaSubtree Stored { get; private set; }
        /// <summary>What the character was built from (the subtree or the card source); a different object rebuilds it.</summary>
        public object BuiltFrom { get; private set; }
        /// <summary>Build slot → accessory id from the builder (documents), else null (the card formula).</summary>
        public IReadOnlyDictionary<int, string> AccessoryIds { get; private set; }
        public CharaMotion Motion => Context?.Motion;
        public CharaAnimation Animation { get; private set; }
        public CharaExpression Expression { get; private set; }
        public CharaLook Look { get; private set; }
        public CharaJointCorrection Correction { get; private set; }
        /// <summary>Container under the holder for option item roots the animation left parentless.</summary>
        public Transform OptionItems { get; private set; }
        public AttachPointResolver AttachPoints { get; private set; }
        /// <summary>0 male, 1 female; -1 unless built.</summary>
        public int Sex => Context == null ? -1 : Context.IsMale ? 0 : 1;
        public double BuildMilliseconds { get; private set; }
        /// <summary>Option-item loads handled through the hook (diagnostics).</summary>
        public int OptionItemLoads { get; private set; }
        public IReadOnlyList<string> BuildLog => Assembler?.Log;
        /// <summary>The realization of `chara.state` (clothes states, wet, gloss, liquid, nipple, son, silhouette); null unless built.</summary>
        public CharaStateRealizer StateRealizer { get; private set; }
        /// <summary>The clothes state types of the worn outfit (kind → supported states); Unknown unless built.</summary>
        public ClothesStateSet ClothesStates => StateRealizer?.States ?? ClothesStateSet.Unknown;

        CharaNodeState St => Node.State as CharaNodeState;
        float appliedMotionTime;
        Vector3 lastScale = new Vector3(float.NaN, float.NaN, float.NaN);

        // ------------------------------------------------------------ build

        // The bundles of the build outside the edit units' runs and the motion (skeleton, body, head base, list bundles): released
        // with the content. The units' generations live in the editor, the motion's in CharaAnimation.
        BundleOwner baseOwner;

        protected internal override void BuildContent()
        {
            var cat = Services.Catalog;
            if (cat == null) { BuildContentCore(); return; }
            if (baseOwner == null || !baseOwner.Live) baseOwner = cat.NewOwner(BundleOwnerLabel("chara") + "/base");
            using (cat.Own(baseOwner)) BuildContentCore();
        }

        void BuildContentCore()
        {
            // the previous build's MaterialEditor shader report goes with it (raised again below when the new build has one)
            UnavailableMeShaders = Array.Empty<string>();
            BrokenInBundleSlots = 0;
            Runtime.Findings.Remove(MeShaderUnavailableCode, NodeId);
            Runtime.Findings.Remove(BrokenInBundleCode, NodeId);
            var a = Node.Appearance;
            if (a == null || a.IsMissing)
            {
                State = RealizationState.Missing;
                Detail = "no appearance";
                return;
            }
            var b = CharaBuilders.For(a);
            if (b == null)
            {
                State = RealizationState.Unbuildable;
                Detail = "no builder for the stored character documents";
                ReportOnce(FindingSeverity.Info, "chara.unbuildable", "stored character documents are kept and saved unchanged; no registered builder accepts them (the subtree builder is registered by default)");
                return;
            }
            // the skin layers plugins set on this node outside builds join the build after its BodyMesh hooks (none officially)
            var outer = HS2Next.Chara.Ext.CharaBuildHooks.BuildingRecord;
            HS2Next.Chara.Ext.CharaBuildHooks.BuildingRecord = PluginLayers.Layers.Count > 0 ? PluginLayers : null;
            CharaBuildResult res;
            try { res = b.Build(a, new BuildContext { Runtime = Runtime, Services = Services, Node = Node, Holder = Holder }); }
            finally { HS2Next.Chara.Ext.CharaBuildHooks.BuildingRecord = outer; }
            BuildMilliseconds = res.Milliseconds;
            // build.* and part.* findings of the builder, once per (code, node) and realization.
            if (res.Findings != null)
                foreach (var f in res.Findings)
                    ReportOnce(f.Severity, f.Code, (string.IsNullOrEmpty(f.Path) ? "" : f.Path + ": ") + f.Message);
            if (!res.Ok)
            {
                State = RealizationState.Failed;
                Detail = res.Error;
                Report(FindingSeverity.Error, "chara.buildFailed", res.Error + (res.Destroyed > 0 ? " (" + res.Destroyed + " partial objects destroyed)" : ""));
                return;
            }
            Builder = b;
            Root = res.Root;
            Context = res.Context;
            Assembler = res.Assembler;
            Card = res.Card;
            Stored = res.Stored;
            BuiltFrom = CharaBuilders.SourceOf(b, a);
            AccessoryIds = res.AccessoryIds;
            if (Card != null) Services.Cards.Reference(NodeId, Card.Sha256);
            var oi = new GameObject("OptionItems").transform;
            oi.SetParent(Holder, false);
            OptionItems = oi;
            if (Motion)
            {
                Motion.AutoStep = !Runtime.Settings.ManualStepping;
                Motion.DeltaSource = Runtime.FrameDtSource;   // the scene clock (Phase 3.6; kept on revive, set again on rebuild)
                Motion.Scheduled = Runtime.Pass != null;      // built while a motion pass is active: the pass steps it
            }
            Animation = CharaAnimation.Of(Root);
            if (Animation) Animation.BundleOwnerLabel = BundleOwnerLabel("chara") + "/anime";
            if (Context?.Editor != null) Context.Editor.OwnerPrefix = BundleOwnerLabel("chara");
            Expression = CharaExpression.Of(Root);
            Look = CharaLook.Of(Root);
            Correction = CharaJointCorrection.Of(Root);
            AttachPoints = new AttachPointResolver(Context);
            StateRealizer = new CharaStateRealizer(this);
            if (Animation) Animation.OptionItemsLoaded += OnOptionItemsLoaded;   // before 7a: the first load is covered
            State = RealizationState.Built;
            ReportUnavailableMeShaders();
            ReportBrokenInBundle();
            AttachEditor();
            lastScale = Holder.lossyScale;
            ApplyAll();
            Runtime.QueueCharaBuilt(this);
            // built while paused: the chains were set up where the assembler built the character (the origin), so they settle
            // on the placed body at the next step
            WantSettle();
        }

        // ------------------------------------------------------------ the paused scene (after 1.1.0)

        /// <summary>
        /// Switch (default on): a change that builds or reshapes the character while the scene is paused settles its physics at
        /// once (CharaMotion.RequestSettle through the runtime, after the current drag). False = the behaviour before (tests).
        /// </summary>
        public static bool SettleWhilePaused = true;

        /// <summary>
        /// The character changed in a way the physics must follow (a build, a revive or reparent, a Maker edit that reshapes or
        /// swaps parts, a pose, an animation frame, clothes or nipple state): while no time passes for it (the scene is paused,
        /// or the character itself through its motion switches: paused or time scale 0) the runtime asks its motion for a settle
        /// once no drag is open. Running, nothing happens (the physics follow by themselves).
        /// </summary>
        internal void WantSettle()
        {
            if (!SettleWhilePaused || State != RealizationState.Built || !Motion || !StandsStill) return;
            Runtime.QueueSettle(this);
        }

        /// <summary>True while no time passes for this character: the global pause, or its motion paused or at time scale 0.</summary>
        public bool StandsStill => Runtime.Paused || (Motion && (Motion.Paused || Motion.TimeScale <= 0f));

        /// <summary>The finding code of a character whose MaterialEditor shader swaps name shaders HS2Next cannot draw.</summary>
        public const string MeShaderUnavailableCode = "me.shaderUnavailable";

        /// <summary>
        /// The shader names of this build's MaterialEditor swaps that were skipped (no URP shader for them; the part keeps its
        /// standard shader), ordinal order; empty when none or not built.
        /// </summary>
        public IReadOnlyList<string> UnavailableMeShaders { get; private set; } = Array.Empty<string>();

        // One warning per build: the swaps the MaterialEditor stage skipped because no URP shader exists for the requested
        // name (the build context's "me.shader" records), its count the skipped swaps, its message the distinct names.
        void ReportUnavailableMeShaders()
        {
            if (Context == null) return;
            foreach (var m in Context.Missing)
                if (m != null && m.Kind == HS2Next.Chara.Ext.MaterialEditorApplier.TextureTooLargeKind)
                    Runtime.Findings.Add(FindingSeverity.Warning, HS2Next.Chara.Ext.MaterialEditorApplier.TextureTooLargeKind, NodeId, "an image of a material edit claims more pixels than can be read: " + m.Detail);
            var names = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var m in Context.Missing)
                if (m != null && m.Kind == "me.shader" && !string.IsNullOrEmpty(m.Guid)) names.Add(m.Guid);
            if (names.Count == 0) return;
            int swaps = 0;
            var counts = Context.Stats?.MeUnknownShaders;
            foreach (var n in names) swaps += counts != null && counts.TryGetValue(n, out int c) && c > 0 ? c : 1;
            UnavailableMeShaders = new List<string>(names);
            var f = Runtime.Findings.Add(FindingSeverity.Warning, MeShaderUnavailableCode, NodeId,
                swaps + " material shader swap" + (swaps == 1 ? "" : "s") + " kept on the standard skin (no URP shader in HS2Next): " +
                string.Join(", ", names));
            f.Count = swaps;
        }

        /// <summary>The finding code of a character with material slots that arrived with the engine's error shader.</summary>
        public const string BrokenInBundleCode = "shader.brokenInBundle";

        /// <summary>Renderer slots of this build drawn with the URP/Lit fallback because their material arrived with the engine's
        /// error shader (ShaderMap.IsBrokenInBundle); 0 when none or not built.</summary>
        public int BrokenInBundleSlots { get; private set; }

        /// <summary>The renderer slots under <paramref name="root"/> whose material ShaderMap marked as arrived with the engine's
        /// error shader (inactive renderers included).</summary>
        public static int CountBrokenInBundle(GameObject root)
        {
            if (!root) return 0;
            int n = 0;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (ShaderMap.IsBrokenInBundle(m)) n++;
            return n;
        }

        // One info finding per build: the slots whose material arrived with the engine's error shader (its own shader did not
        // load from the bundle, or the slot had no material and got the error material) and are drawn with the URP/Lit
        // fallback; its count the slots.
        void ReportBrokenInBundle()
        {
            int n = CountBrokenInBundle(Root);
            BrokenInBundleSlots = n;
            if (n == 0) return;
            var f = Runtime.Findings.Add(FindingSeverity.Info, BrokenInBundleCode, NodeId,
                n + " material slot" + (n == 1 ? "" : "s") + " arrived with the engine's error shader (a shader that did not load from its bundle, " +
                "or a slot without a material) and " + (n == 1 ? "is" : "are") + " drawn with the URP/Lit fallback");
            f.Count = n;
        }

        protected internal override void ReleaseContent()
        {
            // the bundles of the build, the units and the motion wait in the catalog's LRU from now on
            var cat = Services.Catalog;
            if (Animation) Animation.ReleaseBundleOwners();
            Context?.Editor?.ReleaseBundleOwners();
            if (baseOwner != null && cat != null) { cat.Release(baseOwner); cat.NoteRealizationDestroyed(); }
            baseOwner = null;
            if (Animation) Animation.OptionItemsLoaded -= OnOptionItemsLoaded;
            StateRealizer?.Release();
            if (Context?.Editor != null) { Context.Editor.SceneHooks = null; Context.Editor.EndSession(); }
            if (Root) UnityObjects.ReleaseRenderTextures(Root.transform);
            // the character's own material copies go with it (before 4.0 they waited for
            // UnloadUnusedAssets)
            if (Context != null)
            {
                foreach (var m in Context.OwnedMaterials) if (m) UnityObjects.Destroy(m);
                Context.OwnedMaterials.Clear();
                Context.ReleaseOwnedMeshes();   // the breast-normal mesh copies (Phase 4.2)
                // the compositing passes (skin and clothes creators): their create-material copies belong to no renderer and
                // their output RTs may be bound to no renderer under the root any more (Phase 4.4)
                if (Context.Edit != null)
                    foreach (var c in new List<HS2Next.Chara.Edit.CompositeInputs>(Context.Edit.Composites()))
                    {
                        var cr = c?.Creator;
                        if (cr == null) continue;
                        var rt = cr.Target;
                        if (rt) { if (RenderTexture.active == rt) RenderTexture.active = null; rt.Release(); UnityObjects.Destroy(rt); }
                        if (cr.Mat) UnityObjects.Destroy(cr.Mat);
                    }
            }
        }

        protected internal override void DestroyContent()
        {
            if (Root) UnityObjects.Destroy(Root);
            if (OptionItems) UnityObjects.Destroy(OptionItems.gameObject);
            Root = null; OptionItems = null;
            Context = null; Assembler = null; Builder = null; Card = null; Stored = null; BuiltFrom = null; AccessoryIds = null;
            Animation = null; Expression = null; Look = null; Correction = null; AttachPoints = null; StateRealizer = null;
        }

        void OnOptionItemsLoaded(CharaAnimation a, IReadOnlyList<GameObject> objects)
        {
            OptionItemLoads++;
            bool visible = Node != null && Node.EffectiveVisible;
            foreach (var o in objects)
            {
                if (!o) continue;
                ShaderMap.ApplyUrp(o);
                if (o.transform.parent == null && OptionItems) o.transform.SetParent(OptionItems, true);
                VisibilityApplier.SetOwn(o.transform, visible);
            }
        }

        // ------------------------------------------------------------ attach points, visibility, scale

        protected internal override Transform ChildAttach(string point, out AttachNote note)
        {
            note = default;
            if (point == null)
            {
                note = new AttachNote(FindingSeverity.Warning, "scene.charaChildNoPoint", "child of a character without an attach point: attached at the character holder");
                return Holder;
            }
            if (!Runtime.Model.AttachPoints.IsKnown(point))
            {
                note = new AttachNote(FindingSeverity.Warning, "scene.unknownPoint", "attach point \"" + point + "\" is not known: attached at the character holder");
                return Holder;
            }
            if (State != RealizationState.Built || AttachPoints == null)
            {
                note = new AttachNote(FindingSeverity.Info, "scene.attachPointUnavailable", "character not built (" + State + "): attached at its holder");
                return Holder;
            }
            var t = AttachPoints[point];
            if (!t)
            {
                note = new AttachNote(FindingSeverity.Warning, "scene.attachPointMissing", "attach point \"" + point + "\" not found on this body: attached at the character holder");
                return Holder;
            }
            return t;
        }

        protected internal override void ApplyVisibility(bool visible)
        {
            VisibilityApplier.SetOwn(Holder, visible);
            if (Animation && Animation.CharacterVisible != visible) Animation.CharacterVisible = visible;
        }

        /// <summary>
        /// The holder moved to another parent (a revive from the graveyard by undo of delete / redo of add, or a reparent): the
        /// physics history (dynamic bones, bust) is dropped once the holder transform is applied in the same pass, so the chains
        /// start from the bones as after a fresh build (the reset before re-binding after a graveyard restore;
        /// UiOpsTest O12).
        /// </summary>
        protected internal override void OnReparented() => physicsResetPending = true;

        /// <summary>
        /// Before the buried holder is re-activated (SceneRuntime.Reconcile): the dynamic-bone chain bones back to their rest pose, so
        /// the Animator re-enabled with the holder binds the rest pose as its defaults, not the last physics write-back (O12).
        /// </summary>
        internal void BeforeRevive()
        {
            AdvancedPoseDriver.RestoreNow(this);   // the Animator re-enabled with the holder binds the pose without the corrections
            DrivenPoseDriver.RestoreNow(this);     // and without driven values
            RagdollDriver.RestoreNow(this);        // and without a body
            if (Context != null && Context.Bones) Context.Bones.ToBase();   // and without the card's modifiers
            if (Animation) Animation.ExpectBindRead();
            if (State != RealizationState.Built || !Motion || !PhysicsResetOnReparent || !RestPoseBeforeRevive) return;
            Step("revive reset", () => Motion.Get<HS2Next.Motion.DynamicBones.DynamicBoneStage>()?.ResetBeforeUpdate());
        }

        /// <summary>The rest pose before a revive (default on; off only for UiOpsTest O12's investigation variant).</summary>
        public static bool RestPoseBeforeRevive = true;

        bool physicsResetPending;
        /// <summary>The physics reset after a reparent / revive (default on; off only for UiOpsTest O12's comparison).</summary>
        public static bool PhysicsResetOnReparent = true;

        void ResetPhysicsIfPending()
        {
            if (!physicsResetPending || !PhysicsResetOnReparent || State != RealizationState.Built || !Motion) return;
            physicsResetPending = false;
            Step("physics reset", () =>
            {
                var db = Motion.Get<HS2Next.Motion.DynamicBones.DynamicBoneStage>();
                if (db != null)
                {
                    db.Reset();
                    // chains switched off while buried come back on later in the pass (their OnEnable only re-reads the
                    // particles): their bones get the frame reset now as well
                    foreach (var c in db.Chains) if (!c.Active) c.ResetAll();
                }
                foreach (var b in Motion.GetAll<HS2Next.Motion.DynamicBones.BustStage>()) b.Reset();
            });
            WantSettle();
        }

        /// <summary>After the holder transform was applied: a scale change re-arms the option item scales.</summary>
        internal void OnTransformApplied()
        {
            if (!Holder) return;
            ResetPhysicsIfPending();
            if (!Runtime.QuietTransforms) WantSettle();   // moved or turned while paused: the chains come to rest on the body as it now stands
            var s = Holder.lossyScale;
            if (s == lastScale) return;
            bool first = float.IsNaN(lastScale.x);
            lastScale = s;
            if (!first && Animation) Animation.OnCharacterScaleChanged();
        }

        // ------------------------------------------------------------ state (7a-7h)

        void ApplyAll()
        {
            var st = St;
            if (st == null) return;
            Step("anime", () => ApplyAnime(st));
            Step("ik", () => CharaPoseDrivers.Ik?.Apply(this, st, PoseApplyReason.Build));
            Step("fk", () => CharaPoseDrivers.Fk?.Apply(this, st, PoseApplyReason.Build));
            Step("advancedPose", () => CharaPoseDrivers.AdvancedPose?.Apply(this, st, PoseApplyReason.Build));
            Step("correction", () => ApplyCorrection(st));
            Step("face", () => ApplyFace(st.Face));
            Step("look", () => ApplyLook(st.Look));
            Step("accessories", () => ApplyAccessories(st.State));
            Step("state", () => StateRealizer.OnBuilt(st.State));
            MarkApplied();
        }

        void Step(string what, Action a)
        {
            try { a(); }
            catch (Exception e)
            {
                Report(FindingSeverity.Error, "scene.realizeFailed", "chara " + what + ": " + e.GetType().Name + ": " + e.Message);
                Debug.LogException(e);
            }
        }

        void ApplyAnime(CharaNodeState st)
        {
            if (!Animation) return;
            var an = st.Anime;
            float p0 = an.Params != null && an.Params.Length > 0 ? an.Params[0] : 0f;
            float p1 = an.Params != null && an.Params.Length > 1 ? an.Params[1] : 0f;
            int hl = st.Hands != null && st.Hands.Length > 0 ? st.Hands[0] : 0;
            int hr = st.Hands != null && st.Hands.Length > 1 ? st.Hands[1] : 0;
            if (an.Ref == null)
            {
                Animation.Stop();
                Animation.Speed = an.Speed;
                Animation.Pattern = an.Pattern;
                Animation.ForceLoop = an.ForceLoop;
                Animation.OptionVisible = an.OptionVisible;
                Animation.OptionParam1 = p0;
                Animation.OptionParam2 = p1;
                Animation.Hands?.SetPattern(0, hl);
                Animation.Hands?.SetPattern(1, hr);
                return;
            }
            bool ok = Animation.Restore(Services.Db, new AnimationSceneState
            {
                Guid = an.Ref.Source, Group = an.Ref.Group, Category = an.Ref.Category, No = an.Ref.No,
                NormalizedTime = an.Time, Speed = an.Speed, Pattern = an.Pattern, ForceLoop = an.ForceLoop,
                OptionVisible = an.OptionVisible, OptionParam1 = p0, OptionParam2 = p1, HandLeft = hl, HandRight = hr,
            });
            if (!ok) ReportOnce(FindingSeverity.Info, "chara.animeNotPlayed", "animation " + an.Ref.Source + " " + an.Ref.Group + "/" + an.Ref.Category + "/" + an.Ref.No + " not played (row missing, H row, or controller not found); the reference is kept");
        }

        void ApplyCorrection(CharaNodeState st)
        {
            if (Correction != null && st.Correction != null) Correction.ApplySceneFlags(st.Correction);
        }

        void ApplyFace(CharaFaceState f)
        {
            if (Expression == null || f == null) return;
            var s = new FaceStatus
            {
                EyebrowPtn = f.Eyebrow, EyesPtn = FaceShown(OverrideChannel.FaceEyes, f.Eyes), EyesOpenMax = FaceShown(OverrideChannel.FaceEyesOpen, f.EyesOpen),
                EyesBlink = f.Blink, MouthPtn = FaceShown(OverrideChannel.FaceMouth, f.Mouth),
                TearsRate = f.Tears, HohoAkaRate = f.Cheek, MouthAdjustWidth = f.MouthAdjustWidth,
                EyebrowOpenMax = f.EyebrowOpenMax, MouthOpenMin = f.MouthOpenMin, MouthOpenMax = f.MouthOpenMax,
                MouthFixed = f.MouthFixed, HideEyesHighlight = f.HideHighlight,
            };
            Expression.ApplyStudioState(s, FaceShown(OverrideChannel.FaceMouthOpen, f.MouthOpen), f.LipSync);
            Expression.ChangeEyebrowOpenMax(f.EyebrowOpenMax);
            Expression.ChangeMouthOpenMin(f.MouthOpenMin);
            Expression.ChangeMouthOpenMax(f.MouthOpenMax);
            Expression.ChangeMouthFixed(f.MouthFixed);
            Expression.HideEyeHighlight(f.HideHighlight);
            // Studio's mouth open decides FixedRate after the status; the three calls above rewrote it.
            Expression.ApplyStudioMouthOpen();
        }

        void ApplyLook(CharaLookState l)
        {
            if (Look == null || l == null) return;
            Look.SetEyePattern(l.Eyes, force: true);
            if (l.EyeRotations != null) Look.LoadEyes(new[] { UnityObjects.Q(l.EyeRotations.Left), UnityObjects.Q(l.EyeRotations.Right) });
            var bones = Look.Neck?.Bones;
            if (l.NeckFix != null && l.NeckFix.Count > 0 && bones != null && bones.Length > 0)
            {
                var q = new Quaternion[bones.Length];
                Look.SaveNeck(q);   // current fix angles: names missing from the map keep them
                for (int i = 0; i < bones.Length; i++)
                    if (l.NeckFix.TryGetValue(NeckKey(bones[i]), out var v)) q[i] = UnityObjects.Q(v);
                Look.LoadNeck(l.Neck, q);
            }
            Look.SetNeckPattern(l.Neck);
            ApplyLookTarget(l);
        }

        void ApplyLookTarget(CharaLookState l)
        {
            var t = Look?.Targets?.LookAtTarget;
            if (t) t.localPosition = UnityObjects.V(l.Target);
        }

        /// <summary>The `look.neckFix` key of a neck bone: its NeckLookCalc name, else the bone's transform name.</summary>
        internal static string NeckKey(NeckLookStage.Bone b) =>
            !string.IsNullOrEmpty(b.Name) ? b.Name : b.NeckBone ? b.NeckBone.name : "";

        void ApplyAccessories(CharaState s, string onlyId = null)
        {
            if (Context?.Accessories == null || s == null) return;
            foreach (var slot in Context.Accessories)
            {
                if (slot == null || !slot.Object || slot.IsEmpty) continue;
                string id = AccessoryIdOf(slot);
                if (id == null || (onlyId != null && id != onlyId)) continue;
                bool visible = (!s.Accessories.TryGetValue(id, out var v) || v) && !(StateRealizer != null && StateRealizer.HidesAccessories);
                if (slot.Object.activeSelf != visible) slot.Object.SetActive(visible);
            }
        }

        /// <summary>The scene id of a built accessory slot: the builder's table (documents), else the importer's derived id of a
        /// card source; null when unknown.</summary>
        public string AccessoryIdOf(AccessorySlot slot)
        {
            if (slot == null) return null;
            // the edit units keep the slot table current (acc.list); the build's table otherwise
            var ids = (IReadOnlyDictionary<int, string>)Context?.Edit?.Input?.Slots?.IdBySlot ?? AccessoryIds;
            if (ids != null) return ids.TryGetValue(slot.Slot, out var id) ? id : null;
            return Card != null ? CardAppearanceSource.AccessoryId(Card.Sha256, slot.Slot) : null;
        }

        /// <summary>Re-applies accessory visibility from the model (after the silhouette is switched off).</summary>
        internal void ReapplyAccessories()
        {
            var st = St;
            if (st != null) Step("accessories", () => ApplyAccessories(st.State));
        }

        // ------------------------------------------------------------ capture bookkeeping

        /// <summary>The motion advanced (scaled time accumulated) since the animation / look state was last applied from the model or captured.</summary>
        public bool MotionAdvanced => Motion && Motion.Time != appliedMotionTime;

        internal void MarkApplied() => appliedMotionTime = Motion ? Motion.Time : 0f;

        // ------------------------------------------------------------ changes after the build

        protected internal override void ApplyChanges(IReadOnlyCollection<string> paths)
        {
            ForwardAppearance(paths);   // before the identity check: field edits keep Appearance.Stored (G12)
            if (Touches(paths, "appearance"))
            {
                var a = Node.Appearance;
                var b = CharaBuilders.For(a);
                bool same = State == RealizationState.Built && b == Builder && ReferenceEquals(CharaBuilders.SourceOf(b, a), BuiltFrom);
                bool stillUnbuilt = State != RealizationState.Built && b == null && (a == null || a.IsMissing) == (State == RealizationState.Missing);
                if (!same && !stillUnbuilt)
                {
                    Runtime.RebuildContent(this);
                    return;
                }
            }
            if (State != RealizationState.Built) return;
            var st = St;
            if (st == null) return;
            bool applied = false;
            if (Touches(paths, "chara.anime.ref"))
            {
                // a winning chara.anime.ref slot keeps its row (the model's shows when it is released)
                if (!AnimeRowOverridden) Step("anime", () =>
                {
                    if (st.Anime.Ref == null) Animation.Stop();
                    else
                    {
                        var row = CharaAnimation.Resolve(Services.Db, st.Anime.Ref.Source, st.Anime.Ref.Group, st.Anime.Ref.Category, st.Anime.Ref.No);
                        if (!Animation.Load(row, TimeShown(st.Anime.Time)))
                            ReportOnce(FindingSeverity.Info, "chara.animeNotPlayed", "animation " + st.Anime.Ref.Source + " " + st.Anime.Ref.Group + "/" + st.Anime.Ref.Category + "/" + st.Anime.Ref.No + " not played; the reference is kept");
                    }
                });
                applied = true;
            }
            else if (Touches(paths, "chara.anime.time"))
            {
                Step("anime.time", () => Animation.NormalizedTime = TimeShown(st.Anime.Time));
                applied = true;
            }
            if (Animation)
            {
                var an = st.Anime;
                if (Touches(paths, "chara.anime.speed")) Animation.Speed = SpeedShown(an.Speed);
                if (Touches(paths, "chara.anime.pattern")) Animation.Pattern = an.Pattern;
                if (Touches(paths, "chara.anime.forceLoop")) Animation.ForceLoop = an.ForceLoop;
                if (Touches(paths, "chara.anime.optionVisible")) Animation.OptionVisible = an.OptionVisible;
                if (Touches(paths, "chara.anime.params"))
                {
                    Animation.OptionParam1 = an.Params[0];
                    Animation.OptionParam2 = an.Params[1];
                }
                if (Touches(paths, "chara.hands"))
                {
                    Animation.Hands?.SetPattern(0, st.Hands[0]);
                    Animation.Hands?.SetPattern(1, st.Hands[1]);
                }
            }
            if (Touches(paths, "chara.face")) Step("face", () => ApplyFace(st.Face));
            if (Look != null)
            {
                if (Touches(paths, "chara.look.eyes")) { Look.SetEyePattern(st.Look.Eyes); applied = true; }
                if (Touches(paths, "chara.look.neck")) { Look.SetNeckPattern(st.Look.Neck); applied = true; }
                if (Touches(paths, "chara.look.target")) ApplyLookTarget(st.Look);
            }
            if (Touches(paths, "chara.correction")) Step("correction", () => ApplyCorrection(st));
            if (Touches(paths, "chara.state.accessories")) Step("accessories", () => ApplyAccessories(st.State));
            if (Touches(paths, "chara.state") && StateRealizer != null) Step("state", () => StateRealizer.ApplyChanged(st.State, p => Touches(paths, p)));
            foreach (var p in paths)
            {
                if (CharaPoseDrivers.Fk != null && FieldPath.Overlaps(p, "chara.fk")) Step("fk", () => CharaPoseDrivers.Fk.OnChanged(this, p));
                if (CharaPoseDrivers.Ik != null && FieldPath.Overlaps(p, "chara.ik")) Step("ik", () => CharaPoseDrivers.Ik.OnChanged(this, p));
            }
            Step("advancedPose", () => AdvancedPoseDriver.AfterChanges(this, paths));
            if (applied) MarkApplied();
            foreach (var p in SettlePaths)
                if (Touches(paths, p)) { WantSettle(); break; }
        }

        /// <summary>The scene state that moves the body or reshapes it (a settle while paused, WantSettle).</summary>
        static readonly string[] SettlePaths =
        {
            "chara.anime.ref", "chara.anime.time", "chara.anime.pattern", "chara.fk", "chara.ik",
            "chara.state.clothes", "chara.state.nipple", "chara.state.silhouette", "chara.state.nippleThroughClothes", "chara.advancedPose",
        };
    }
}
