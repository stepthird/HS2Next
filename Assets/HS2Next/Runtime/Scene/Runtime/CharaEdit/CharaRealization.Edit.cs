// Maker edits of a built character in the scene (
// 11): the realization forwards every changed `appearance.` member path to the character's editor
// (CharaEditor.MarkDirty) before the appearance-identity check, the scene's flush point (CharaEditFlush.Run at the end of
// SceneRuntime.Apply's per-node appliers) flushes the dirty editors once per frame in model pre-order, and a flush that
// asks for a rebuild (rebuild-only members, or units the editor does not have) rebuilds the character through
// SceneRuntime.RebuildContent (children parked). After every flush that ran units the editor calls
// AfterFlush (ICharaEditSceneHooks, step 10):
//   head swap        → the face stage (FaceSetup) re-attached for this character, the eye look re-attached when its eye
//                      transforms went with the old head (the neck part is bound to body bones and kept), chara.face and
//                      (after a look re-attach) chara.look re-applied
//   hair / clothes 0, 1 swaps → FK bone rediscovery (FkDriver.Rediscover; other drivers get OnChanged("chara.fk"))
//   clothes swaps, masks, body draw, shapes, swaps → CharaStateRealizer.AfterEdit (state types, def / half objects, masks,
//                      gloss over the rewritten body and face draws (4.1, GF1), wet / liquid on new materials)
//   accessory swaps, plugin final, any swap → scene accessory visibility (state.accessories)
//   eye materials    → FaceMaterials.Refresh
//   new subtrees     → rendering layers, the node's visibility (forceRenderingOff), skinning flags of the test views
// The document is the truth: nothing here writes the model.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HS2Next.Chara.Edit;
using HS2Next.Motion;
using HS2Next.Motion.Face;
using HS2Next.Motion.Look;
using HS2Next.Save;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    public sealed partial class CharaRealization : ICharaEditSceneHooks
    {
        /// <summary>The editor of the built character (CharaContext.Editor), or null unless built.</summary>
        public CharaEditor Editor => State == RealizationState.Built ? Context?.Editor : null;
        /// <summary>Flushes that ran on this node's editors (across rebuilds; diagnostics and tests).</summary>
        public int EditFlushes { get; private set; }
        /// <summary>Rebuilds a flush asked for (rebuild-only members, missing units).</summary>
        public int EditRebuilds { get; private set; }
        /// <summary>Why the last edit rebuild happened (the editor's rebuild paths), or null.</summary>
        public string LastEditRebuild { get; private set; }
        /// <summary>The report of the last flush that ran (null before the first).</summary>
        public FlushReport LastEditFlush { get; private set; }
        /// <summary>Milliseconds of the last step-10 pass (AfterFlush).</summary>
        public double LastSceneStepMs { get; private set; }
        /// <summary>Time.frameCount of the last flush that recomposited the body skin (C5 drag throttle).</summary>
        internal int LastBodyCompositeFrame = -10;
        /// <summary>Drag frames of this character the C5 body throttle made wait (Phase 4.2;
        /// chara.edit.info throttle.throttled; across rebuilds).</summary>
        public int EditThrottled { get; internal set; }
        /// <summary>The pending body weight the throttle last judged (CharaEditor.PendingBodyWeight; diagnostics).</summary>
        public int LastPendingBodyWeight { get; internal set; }
        /// <summary>Head swaps whose eye look had to be re-attached (its eye transforms were destroyed with the old head).</summary>
        public int LookReattached { get; private set; }

        /// <summary>Called right after a build: the editor's scene hooks and the edit session.</summary>
        void AttachEditor()
        {
            var ed = Context?.Editor;
            if (ed == null) return;
            ed.SceneHooks = this;
            CharaEditSessions.Sync(this);
            ReportBustNormals();
        }

        /// <summary>Phase 4.2: a female body without its NormalData asset is not blended;
        /// the finding chara.bustNormalsMissing (info) once per character.</summary>
        void ReportBustNormals()
        {
            var st = Context?.BustNormals;
            if (st == null || !st.Missing || st.MissingReported) return;
            st.MissingReported = true;
            ReportOnce(FindingSeverity.Info, "chara.bustNormalsMissing", "the breast normals are not blended: " + st.MissingReason + " (" + st.Source + ")");
        }

        /// <summary>
        /// every changed member path under `appearance.` marks the editor's units (the whole-appearance path
        /// "appearance" of SetAppearanceCommand is not a member: the identity check rebuilds for it). The node is queued for
        /// the frame's flush.
        /// </summary>
        void ForwardAppearance(IReadOnlyCollection<string> paths)
        {
            var ed = Editor;
            if (ed == null) return;
            bool any = false;
            foreach (var p in paths)
            {
                if (p == null || p == AppearancePaths.Root || !AppearancePaths.IsAppearance(p)) continue;
                if (ed.MarkDirty(p)) any = true;
            }
            if (any || ed.IsDirty) CharaEditFlush.Queue(Runtime, this);
        }

        /// <summary>
        /// The flush of this character: a requested rebuild replaces the character (the new build has a fresh
        /// editor), else the dirty units run (AfterFlush runs inside). Returns the report, or null when nothing ran.
        /// </summary>
        internal FlushReport FlushEdit(FlushMode mode = FlushMode.Normal)
        {
            if (TakePluginRebuild()) return null;
            var ed = Editor;
            if (ed == null || !ed.NeedsFlush(mode)) return null;
            if (ed.RebuildRequested)
            {
                RebuildForEdit(ed);
                return null;
            }
            AdvancedPoseDriver.RestoreNow(this);   // shapes and rest poses are captured from the bones without the corrections
            DrivenPoseDriver.RestoreNow(this);     // and without driven values
            var r = ed.Flush(mode);
            if (r.RebuildRequested)
            {
                RebuildForEdit(ed);
                return r;
            }
            AdvancedPoseDriver.ApplyIfRestored(this);
            EditFlushes++;
            LastEditFlush = r;
            if (r.Errors.Count > 0)
                foreach (var e in r.Errors) Report(FindingSeverity.Warning, "chara.editFailed", e);
            return r;
        }

        void RebuildForEdit(CharaEditor ed)
        {
            EditRebuilds++;
            LastEditRebuild = string.Join(", ", ed.RebuildPaths);
            ed.ClearRebuildRequest();
            Runtime.RebuildContent(this);
        }

        // ------------------------------------------------------------ step 10 (ICharaEditSceneHooks)

        void ICharaEditSceneHooks.AfterFlush(CharaEditor editor, FlushReport r)
        {
            if (r == null || r.Build || State != RealizationState.Built || Context == null || editor != Context.Editor) return;
            var sw = Stopwatch.StartNew();
            var st = St;
            if (r.HeadSwapped) Step("edit.head", () => ReattachHeadStages(st));
            bool fk = r.HairSwapped.Any(b => b) || r.ClothesSwapped[0] || r.ClothesSwapped[1];
            if (fk) Step("edit.fk", RediscoverFk);
            if (StateRealizer != null && st != null) Step("edit.state", () => StateRealizer.AfterEdit(st.State, r));
            if (st != null && (r.AccessorySlotsSwapped.Count > 0 || r.ExtFinalRan || r.AnySwap)) Step("edit.accessories", () => ApplyAccessories(st.State));
            if (r.EyesChanged && !r.HeadSwapped) Step("edit.eyes", () => Expression?.Materials?.Refresh());
            if (r.NewObjects.Count > 0) Step("edit.objects", () => PrepareNewObjects(r.NewObjects));
            if (r.AnySwap || r.HeadSwapped || r.AccessorySlotsSwapped.Count > 0 || r.DynBone != "none" || r.ExtFinalRan || r.NewObjects.Count > 0)
            {
                Step("edit.advancedPose", () => AdvancedPoseDriver.Reresolve(this));
                Runtime.NoteReresolved(this);
            }
            // while paused, an edit that reshapes the body or replaces parts settles the physics (after 1.1.0); a drag frame's
            // preview waits for the commit
            if (r.Mode == FlushMode.Normal && (r.BodyShapeChanged || r.FaceShapeChanged || r.AnySwap || r.DynBone != "none" || r.ExtFinalRan ||
                                               r.NewObjects.Count > 0 || r.ClothesObjectsReset.Any(b => b)))
                WantSettle();
            LastSceneStepMs = sw.Elapsed.TotalMilliseconds;
        }

        /// <summary>
        /// the face stage belongs to the head prefab, so it is removed and attached again for this character
        /// (FaceSetup.Attach), then chara.face is re-applied. The eye look is bound to transforms of the head-bone rig; when
        /// the swap destroyed any of them, the look stages are attached again too (finding chara.lookReattached).
        /// </summary>
        void ReattachHeadStages(CharaNodeState st)
        {
            var m = Motion;
            if (!m || !MotionSetup.Enabled) return;
            if (EyesStale()) ReattachLook(st);
            foreach (var s in m.GetAll<ExpressionStage>().ToList()) m.Remove(s);
            FaceSetup.Attach(Context);
            Expression = CharaExpression.Of(Root);
            if (st != null) ApplyFace(st.Face);
        }

        bool EyesStale()
        {
            var eyes = Look?.Eyes;
            if (eyes == null) return Look == null && Context.HeadBone;
            if (eyes.Eyes == null) return false;
            foreach (var e in eyes.Eyes) if (e != null && !e.Transform) return true;
            return false;
        }

        void ReattachLook(CharaNodeState st)
        {
            var m = Motion;
            var old = Look;
            if (old != null)
            {
                if (old.Neck != null) m.Remove(old.Neck);
                if (old.Eyes != null) m.Remove(old.Eyes);
                var t = old.Targets;
                if (t != null)
                    foreach (var o in new[] { t.NeckTargetP, t.EyesTargetP })
                        if (o && o != t.LookAtTarget) UnityObjects.Destroy(o.gameObject);
            }
            LookSetup.Attach(Context);
            Look = CharaLook.Of(Root);
            LookReattached++;
            ReportOnce(FindingSeverity.Info, "chara.lookReattached", "the head swap replaced the eye-look transforms: the neck and eye look stages were attached again");
            if (st != null) ApplyLook(st.Look);
        }

        /// <summary>Hair parts and the clothes kinds 0 / 1 carry FK bones (hair group, skirt): the bone table is discovered again.</summary>
        void RediscoverFk()
        {
            var d = CharaPoseDrivers.Fk;
            if (d == null) return;
            if (d is FkDriver) FkDriver.Rediscover(this);
            else d.OnChanged(this, "chara.fk");
        }

        /// <summary>(scene part): rendering layers, the node's effective visibility and the skinning flag of the views.</summary>
        void PrepareNewObjects(List<GameObject> objects)
        {
            bool visible = Node != null && Node.EffectiveVisible;
            bool perRender = Context.BodyRenderer is SkinnedMeshRenderer body && body.forceMatrixRecalculationPerRender;
            foreach (var o in objects)
            {
                if (!o) continue;
                RenderingLayers.ApplyToRenderers(o);
                VisibilityApplier.SetOwn(o.transform, visible);
                if (perRender)
                    foreach (var s in o.GetComponentsInChildren<SkinnedMeshRenderer>(true)) s.forceMatrixRecalculationPerRender = true;
            }
        }
    }
}
