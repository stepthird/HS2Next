// The setter layer of one built character: document changes mark edit units dirty
// (MarkDirty, cheap and idempotent), and Flush runs the dirty units once, in the fixed step order (parts, masks,
// composites, materials, plugin textures, shapes, plugin final, ownership, motion; scene state through the scene's
// hooks). A creator (composite) is rebuilt at most once per flush. A change of a rebuild-only member (uncensor, Studio
// overlay switches, sex, Character MaterialEditor items) makes the editor request a rebuild of the character instead
// (RebuildRequested; the flush then runs nothing). CharaAssembler.Build is MarkAll + Flush with the build order.
// A unit that throws is logged (chara.editFailed) and the flush goes on with the next unit.
// A unit that follows every earlier step (EditUnit.FollowsEarlierSteps: the mosaic stage) is marked whenever a unit of a
// lower rank ran in the flush.
// Phase 4.1: the editor keeps per unit the member paths that marked it since its last
// run (EditUnit.DirtyPaths / DirtyAll; the skin units rebuild only the creators those paths feed), and a Preview flush (a
// drag: an open transaction in play mode) moves the deferrable step-9 work (anim.refresh, the light dynamic-bone
// re-derivation, the body-shape bust reset) to the editor's Deferred set; the next Normal flush runs it, so the state
// after the commit equals a fresh build.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace HS2Next.Chara.Edit
{
    /// <summary>How a flush runs.</summary>
    public enum FlushMode
    {
        /// <summary>Every dirty unit, then the units deferred by earlier preview flushes.</summary>
        Normal = 0,
        /// <summary>A drag frame: the deferrable step-9 work (anim.refresh, the light dynamic-bone re-derivation, the
        /// body-shape bust reset) is not run but kept in <see cref="CharaEditor.Deferred"/> for the next Normal flush.</summary>
        Preview = 1,
        /// <summary>The build's own flush (CharaAssembler.Build: steps 9 and 10 skipped, exceptions rethrown).</summary>
        Build = 2,
    }

    /// <summary>What one flush did (bridge chara.edit.info, tests, the scene's step-10 hooks).</summary>
    public sealed class FlushReport
    {
        /// <summary>Unit ids run, in run order.</summary>
        public readonly List<string> Units = new List<string>();
        /// <summary>Compositing passes rebuilt (TexCreator.Rebuild calls).</summary>
        public int Creators;
        /// <summary>Names of the compositing passes rebuilt, in rebuild order ("create_skin_face", ...).</summary>
        public readonly List<string> CreatorsRebuilt = new List<string>();
        /// <summary>The mode this flush ran in.</summary>
        public FlushMode Mode;
        /// <summary>The body-composite cost weight of this flush when it started (Σ over the
        /// body creators it rebuilds of 1 + their skin layers; 0 = no body composite; <see cref="CharaEditor.PendingBodyWeight"/>).</summary>
        public int ThrottleWeight;
        /// <summary>Unit ids the editor holds deferred after this flush (non-empty only after a Preview flush that deferred
        /// work; the next Normal flush runs them), in flush order.</summary>
        public readonly List<string> Deferred = new List<string>();
        /// <summary>Phase 4.3: the dynamic-bone chain step this flush ran —
        /// "none", "light" (the chains re-derived from the current bones, the hair bundle corrections re-applied after the
        /// restore) or "full" (rebuilt in full-build order: part swaps, noShake). A light re-derivation a Preview flush deferred
        /// shows as "none" with "dynbone" in <see cref="Deferred"/>.</summary>
        public string DynBone = "none";
        public double Milliseconds;
        /// <summary>"unit: Exception: message" of units that threw (chara.editFailed).</summary>
        public readonly List<string> Errors = new List<string>();
        /// <summary>True when the flush ran nothing because a rebuild of the character was requested.</summary>
        public bool RebuildRequested;
        /// <summary>True for the build's own flush (CharaAssembler.Build: steps 9 and 10 skipped).</summary>
        public bool Build;

        // ---- what changed (for the scene's step-10 work: stage re-attach, FK rediscovery, state, visibility, layers)
        /// <summary>The head part was replaced (face / look stages must be re-attached, then chara.face re-applied).</summary>
        public bool HeadSwapped;
        /// <summary>Hair parts replaced, by kind 0..3 (FK hair bones rediscovered).</summary>
        public readonly bool[] HairSwapped = new bool[4];
        /// <summary>Clothes kinds replaced (or loaded / removed), 0..7 (state types, masks, skirt FK).</summary>
        public readonly bool[] ClothesSwapped = new bool[8];
        /// <summary>Clothes kinds whose part unit ran without a swap (an option-part toggle) and so put the def / half objects
        /// back to their dressed build values, 0..7: the scene re-applies the stored clothes state as after a swap.</summary>
        public readonly bool[] ClothesObjectsReset = new bool[8];
        /// <summary>The clothes compositing passes this flush rebuilt, one entry per kind and colour group that rebuilt
        /// anything: its colour creator, its detail creator or both (a colour edit rebuilds only the creators it feeds).</summary>
        public readonly List<ClothesCreatorRun> ClothesCreatorsRebuilt = new List<ClothesCreatorRun>();
        /// <summary>Build slots of accessories whose object was replaced, loaded or removed.</summary>
        public readonly List<int> AccessorySlotsSwapped = new List<int>();
        /// <summary>Phase 4.5: build slots of hair-type accessories whose materials got the
        /// hair shader pass in this flush (a load, or a draw-type change re-shading the loaded slot), in run order.</summary>
        public readonly List<int> AccessoryHairShaded = new List<int>();
        /// <summary>The mask unit ran (CharaStateRealizer re-applies the state's mask values).</summary>
        public bool MasksChanged;
        /// <summary>The body draw material was rewritten (CharaStateRealizer's gloss state re-applies).</summary>
        public bool BodyDrawChanged;
        /// <summary>The face draw material was rewritten by draw.face (CharaStateRealizer's gloss state re-applies on the
        /// face).</summary>
        public bool FaceDrawChanged;
        /// <summary>Eye materials were rewritten (FaceMaterials.Refresh).</summary>
        public bool EyesChanged;
        public bool FaceShapeChanged, BodyShapeChanged;
        /// <summary>The plugin final stage ran (AAC startup pass: scene state.accessories re-applied after it).</summary>
        public bool ExtFinalRan;
        /// <summary>Roots of the subtrees this flush created (layers, node visibility, forceRenderingOff of the scene).</summary>
        public readonly List<UnityEngine.GameObject> NewObjects = new List<UnityEngine.GameObject>();

        public bool AnySwap => HeadSwapped || HairSwapped.Any(b => b) || ClothesSwapped.Any(b => b) || AccessorySlotsSwapped.Count > 0;

        public override string ToString() =>
            RebuildRequested ? "rebuild requested" :
            $"{Units.Count} units ({string.Join(", ", Units)}), {Creators} creators, {Milliseconds:F2} ms" +
            (Mode == FlushMode.Preview ? ", preview" : "") + (Deferred.Count > 0 ? ", deferred " + string.Join(", ", Deferred) : "") +
            (Errors.Count > 0 ? ", errors: " + string.Join("; ", Errors) : "");
    }

    /// <summary>One clothes colour group's creators rebuilt by a flush (<see cref="FlushReport.ClothesCreatorsRebuilt"/>).</summary>
    public struct ClothesCreatorRun
    {
        /// <summary>Clothes kind 0..7 and colour group 0..2.</summary>
        public int Kind, Group;
        /// <summary>The colour creator ("create_clothes k.j") and / or the detail creator ("create_clothes detail k.j") ran.</summary>
        public bool Colour, Detail;

        public ClothesCreatorRun(int kind, int group, bool colour, bool detail) { Kind = kind; Group = group; Colour = colour; Detail = detail; }

        public override string ToString() => "clothes " + Kind + "." + Group + (Colour && Detail ? " colour+detail" : Colour ? " colour" : Detail ? " detail" : " none");
    }

    /// <summary>The scene side of a flush (package C): step 10 work and the stage re-attach after a head swap.</summary>
    public interface ICharaEditSceneHooks
    {
        /// <summary>Called at the end of every flush that ran units (not for the build's own flush).</summary>
        void AfterFlush(CharaEditor editor, FlushReport report);
    }

    public sealed class CharaEditor
    {
        readonly Dictionary<string, EditUnit> units = new Dictionary<string, EditUnit>(StringComparer.Ordinal);
        readonly List<EditUnit> order = new List<EditUnit>();
        readonly HashSet<string> dirty = new HashSet<string>(StringComparer.Ordinal);
        /// <summary>Per dirty unit id: the member paths that marked it (null = "all": MarkUnit, MarkAll, deferred work).</summary>
        readonly Dictionary<string, HashSet<string>> dirtyPaths = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        /// <summary>Units a Preview flush deferred (run by the next Normal flush).</summary>
        readonly HashSet<string> deferred = new HashSet<string>(StringComparer.Ordinal);
        readonly List<string> rebuildPaths = new List<string>();

        /// <summary>The build state the units work on (null before the build has made it).</summary>
        public EditContext Context { get; }
        /// <summary>Creates the unit of an id that is not registered (accessories: "acc:&lt;id&gt;"); null = none.</summary>
        public Func<string, EditUnit> Factory;
        /// <summary>The scene's step-10 hooks (package C); null = none.</summary>
        public ICharaEditSceneHooks SceneHooks;
        /// <summary>The last flush (bridge chara.edit.info).</summary>
        public FlushReport LastFlush { get; private set; }
        /// <summary>Flushes that ran units since the build.</summary>
        public int Flushes { get; private set; }

        public CharaEditor(EditContext context) { Context = context; }

        // ---------------------------------------------------------------- bundle owners

        // Per unit: the generations of its bundle owner; a run that loads replaces the previous generation, which is released.
        readonly Dictionary<string, HS2Next.Content.OwnerGenerations> unitOwners = new Dictionary<string, HS2Next.Content.OwnerGenerations>(StringComparer.Ordinal);

        /// <summary>The label prefix of the units' bundle owners ("chara:&lt;node id 8&gt;"); taken from the build's scope when unset.</summary>
        public string OwnerPrefix;

        /// <summary>Releases the bundles of every unit's current generation (the character's content is gone).</summary>
        public void ReleaseBundleOwners()
        {
            foreach (var g in unitOwners.Values) g.ReleaseAll();
            unitOwners.Clear();
        }

        HS2Next.Content.OwnerGenerations UnitOwners(EditUnit u, EditContext x)
        {
            var cat = x?.Catalog;
            if (cat == null) return null;
            if (unitOwners.TryGetValue(u.Id, out var g)) return g;
            if (OwnerPrefix == null)
            {
                string cur = cat.Ledger.Current?.Label;
                int slash = cur?.IndexOf('/') ?? -1;
                OwnerPrefix = slash > 0 ? cur.Substring(0, slash) : "chara";
            }
            return unitOwners[u.Id] = new HS2Next.Content.OwnerGenerations(cat.Ledger, OwnerPrefix + "/" + u.Id);
        }

        // ---------------------------------------------------------------- units

        public void Register(EditUnit u)
        {
            if (u == null) return;
            if (units.TryGetValue(u.Id, out var old)) order.Remove(old);
            units[u.Id] = u;
            int i = order.Count;
            while (i > 0 && Rank(order[i - 1]) > Rank(u)) i--;
            order.Insert(i, u);
        }

        public bool Unregister(string id)
        {
            if (id == null || !units.TryGetValue(id, out var u)) return false;
            if (unitOwners.TryGetValue(id, out var owners)) { owners.ReleaseAll(); unitOwners.Remove(id); }
            units.Remove(id);
            order.Remove(u);
            dirty.Remove(id);
            dirtyPaths.Remove(id);
            deferred.Remove(id);
            return true;
        }

        static long Rank(EditUnit u) => (long)u.Step * 1000000 + u.Order;

        public EditUnit Unit(string id) => id != null && units.TryGetValue(id, out var u) ? u : null;
        /// <summary>Registered units in flush order.</summary>
        public IReadOnlyList<EditUnit> Units => order;

        // ---------------------------------------------------------------- marking

        /// <summary>Unit ids marked since the last flush.</summary>
        public IReadOnlyCollection<string> Dirty => dirty;
        public bool IsDirty => dirty.Count > 0 || rebuildPaths.Count > 0;
        /// <summary>A rebuild-only member changed: the realization rebuilds the character (SceneRuntime.RebuildContent) instead of flushing.</summary>
        public bool RebuildRequested => rebuildPaths.Count > 0;
        public IReadOnlyList<string> RebuildPaths => rebuildPaths;
        /// <summary>Time of the last MarkDirty (UnityEngine.Time.realtimeSinceStartupAsDouble; edit session expiry).</summary>
        public double LastEdit { get; private set; }

        /// <summary>
        /// Marks the units a changed document path reads (longest-prefix overlap in both directions). Accepts
        /// "face.shape[12]" (member path inside its document), "chara.face.shape[12]" and "appearance.chara.face.shape[12]".
        /// Returns true when a unit or a rebuild was marked (false: the member is stored only, or the path is not an
        /// appearance path).
        /// </summary>
        public bool MarkDirty(string documentPath)
        {
            if (documentPath == null) return false;
            var marked = new List<string>();
            bool rebuild = UnitMap.Map(documentPath, marked, out string why, AccessoryIds());
            LastEdit = Now();
            if (rebuild)
            {
                if (!rebuildPaths.Contains(why)) rebuildPaths.Add(why);
                return true;
            }
            string member = UnitMap.Member(documentPath, out _) ?? "";
            foreach (var u in marked) { dirty.Add(u); Note(u, member); }
            if (Context != null && UnitMap.IsPluginSection(documentPath, out bool ksox))
            {
                Context.ExtDirty = true;
                if (ksox) { Context.KsoxDirty = true; dirty.Add(UnitMap.ExtTextures); Note(UnitMap.ExtTextures, null); }
            }
            return marked.Count > 0;
        }

        /// <summary>Marks one unit by id ("shape.face", "acc:&lt;id&gt;"); its DirtyAll is set.</summary>
        public void MarkUnit(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            dirty.Add(id);
            Note(id, null);
        }

        /// <summary>Marks every registered unit (the build); every unit runs with DirtyAll.</summary>
        public void MarkAll()
        {
            foreach (var u in order) { dirty.Add(u.Id); Note(u.Id, null); }
        }

        /// <summary>Records what marked a unit (null = all).</summary>
        void Note(string id, string member)
        {
            if (!dirtyPaths.TryGetValue(id, out var s))
                dirtyPaths[id] = member == null ? null : new HashSet<string>(StringComparer.Ordinal) { member };
            else if (s != null)
            {
                if (member == null) dirtyPaths[id] = null;
                else s.Add(member);
            }
        }

        /// <summary>The member paths that marked a dirty unit since its last run; null = all (or not dirty).</summary>
        public IReadOnlyCollection<string> DirtyPathsOf(string id) => id != null && dirtyPaths.TryGetValue(id, out var s) ? s : null;

        /// <summary>Units deferred by Preview flushes, waiting for the next Normal flush (bridge chara.edit.info, tests).</summary>
        public IReadOnlyCollection<string> Deferred => deferred;
        public bool HasDeferred => deferred.Count > 0;

        /// <summary>True when a flush in <paramref name="mode"/> would run something (dirty units or a rebuild request; in a
        /// Normal flush also the deferred units).</summary>
        public bool NeedsFlush(FlushMode mode) => IsDirty || (mode != FlushMode.Preview && deferred.Count > 0);

        /// <summary>Work a Preview flush moves to the next Normal flush: the animation
        /// refresh, the light dynamic-bone re-derivation (a full rebuild after a part swap or a noShake change is
        /// never deferred) and the body-shape bust reset.</summary>
        static bool Deferrable(EditUnit u, EditContext x)
        {
            if (x == null) return false;
            switch (u.Id)
            {
                case UnitMap.AnimRefresh: return true;
                case UnitMap.DynBone: return !x.DynBoneFull;
                case UnitMap.Bust: return x.BustRederive;
                // Phase 4.2: the breast normals blend live in drags unless UB4 measured
                // it above 0.5 ms (then it joins the deferred set and runs on the commit)
                case UnitMap.BustNormals: return !HS2Next.Chara.Edit.Units.BustNormalsUnit.LiveInDrags;
                default: return false;
            }
        }

        /// <summary>The cost weight of the body composite the next flush would run (
        /// <see cref="UnitMap.BodyCompositeWeight"/> from the member paths that marked skin.body and the layers registered on
        /// the body creators now): 0 when skin.body is not dirty. CPU only (the C5 throttle predicate of CharaEditFlush).</summary>
        public int PendingBodyWeight()
        {
            if (!dirty.Contains(UnitMap.SkinBody)) return 0;
            dirtyPaths.TryGetValue(UnitMap.SkinBody, out var paths);
            var x = Context;
            bool all = paths == null || x == null || x.BodySkin == null;
            return UnitMap.BodyCompositeWeight(paths, all, LayersOf(x?.BodySkin), LayersOf(x?.BodyDetail));
        }

        static int LayersOf(CompositeInputs c) => c?.Creator == null ? 0 : c.Creator.Under.Count + c.Creator.Over.Count;

        /// <summary>Forgets a rebuild request (the realization rebuilt the character).</summary>
        public void ClearRebuildRequest() => rebuildPaths.Clear();

        IEnumerable<string> AccessoryIds()
        {
            var coord = Context?.Coord;
            if (coord?.Accessories == null) yield break;
            foreach (var a in coord.Accessories) if (a?.Id != null) yield return a.Id;
        }

        // ---------------------------------------------------------------- flush

        /// <summary>Runs the dirty units once in the step order. With a rebuild request nothing runs (the report
        /// says so). <paramref name="build"/>: the build's own flush (no scene hooks).</summary>
        public FlushReport Flush(bool build = false) => Flush(build ? FlushMode.Build : FlushMode.Normal);

        /// <summary>Runs the dirty units once in the step order: a
        /// Normal flush first adds the units earlier Preview flushes deferred; a Preview flush defers the deferrable step-9
        /// work. With a rebuild request nothing runs (the report says so).</summary>
        public FlushReport Flush(FlushMode mode)
        {
            bool build = mode == FlushMode.Build;
            var r = new FlushReport { Build = build, Mode = mode, ThrottleWeight = build ? 0 : PendingBodyWeight() };
            if (rebuildPaths.Count > 0)
            {
                r.RebuildRequested = true;
                FillDeferred(r);
                LastFlush = r;
                return r;
            }
            if (mode != FlushMode.Preview && deferred.Count > 0)
            {
                // the commit: the deferred work runs now (in step order with whatever else is dirty)
                foreach (var id in deferred) { dirty.Add(id); Note(id, null); }
                deferred.Clear();
            }
            if (dirty.Count == 0) { FillDeferred(r); LastFlush = r; return r; }
            var sw = Stopwatch.StartNew();
            // Units created on demand (accessories by id).
            foreach (var id in dirty.ToList())
                if (!units.ContainsKey(id) && Factory != null)
                {
                    var u = Factory(id);
                    if (u != null) Register(u);
                }
            // Without units for every dirty id (the seam before the units exist): today's behaviour, a rebuild.
            foreach (var id in dirty)
                if (!units.ContainsKey(id))
                {
                    rebuildPaths.Add("unit:" + id);
                    dirty.Clear();
                    dirtyPaths.Clear();
                    r.RebuildRequested = true;
                    LastFlush = r;
                    return r;
                }
            var x = Context;
            if (x != null) x.Begin(r, build);
            try
            {
                // Units may mark further units of later steps (or of the same step after themselves) while they run.
                for (int i = 0; i < order.Count; i++)
                {
                    var u = order[i];
                    if (!dirty.Remove(u.Id)) continue;
                    dirtyPaths.TryGetValue(u.Id, out var paths);
                    dirtyPaths.Remove(u.Id);
                    if (mode == FlushMode.Preview && Deferrable(u, x))
                    {
                        deferred.Add(u.Id);
                        continue;
                    }
                    deferred.Remove(u.Id);   // (a full dynamic-bone rebuild in a preview flush covers a deferred light one)
                    u.DirtyPaths.Clear();
                    if (paths != null) u.DirtyPaths.UnionWith(paths);
                    u.DirtyAll = paths == null;
                    r.Units.Add(u.Id);
                    if (x != null)
                    {
                        x.DrainBadMip(null);
                        if (u.LoadsAllEachRun) x.ForgetMissing(u.Id);
                        x.CurrentUnit = u.Id;
                    }
                    var gen = UnitOwners(u, x);
                    var next = gen?.Begin();
                    try
                    {
                        using (next != null ? x.Catalog.Own(next) : default) u.Apply(x);
                        x?.DrainBadMip(u.Id);
                    }
                    catch (Exception e)
                    {
                        string msg = u.Id + ": " + e.GetType().Name + ": " + e.Message;
                        r.Errors.Add(msg);
                        x?.Log?.Add("chara.editFailed " + msg);
                        // the build keeps the pre-4.0 failure semantics: an exception of the base assembly fails the build
                        if (build) { dirty.Clear(); dirtyPaths.Clear(); throw; }
                        UnityEngine.Debug.LogWarning("chara.editFailed " + msg + "\n" + e.StackTrace);
                    }
                    finally { u.DirtyPaths.Clear(); u.DirtyAll = false; gen?.End(next, u.LoadsAllEachRun); }
                    // units that follow every earlier step (the mosaic stage) run after anything before them ran
                    foreach (var f in order)
                        if (f.FollowsEarlierSteps && f != u && Rank(f) > Rank(u)) { dirty.Add(f.Id); Note(f.Id, null); }
                    // a unit registered or re-ranked during the run (part swaps) keeps the loop valid: restart at its rank
                    if (i >= order.Count || order[i] != u) i = Math.Max(-1, order.IndexOf(u));
                }
                // Ids that lost their unit while running (an accessory removed by acc.list) are dropped; marks of an earlier
                // step made by a later unit stay for the next flush.
                dirty.RemoveWhere(id => !units.ContainsKey(id));
                foreach (var id in dirtyPaths.Keys.Where(id => !dirty.Contains(id)).ToList()) dirtyPaths.Remove(id);
                deferred.RemoveWhere(id => !units.ContainsKey(id));
            }
            finally { x?.End(); }
            r.Milliseconds = sw.Elapsed.TotalMilliseconds;
            FillDeferred(r);
            LastFlush = r;
            if (!build)
            {
                Flushes++;
                try { SceneHooks?.AfterFlush(this, r); }
                catch (Exception e) { r.Errors.Add("scene hooks: " + e.GetType().Name + ": " + e.Message); UnityEngine.Debug.LogWarning("chara.editFailed scene hooks: " + e); }
            }
            return r;
        }

        void FillDeferred(FlushReport r)
        {
            r.Deferred.Clear();
            foreach (var u in order) if (deferred.Contains(u.Id)) r.Deferred.Add(u.Id);
        }

        // ---------------------------------------------------------------- edit session

        /// <summary>True while the Edit tab shows the character (or a bridge client asked for a session): decoded skin layers
        /// and resolved composite inputs are kept between flushes.</summary>
        public bool InSession { get; private set; }

        public void BeginSession()
        {
            InSession = true;
            LastEdit = Now();
        }

        public void EndSession()
        {
            if (!InSession) return;
            InSession = false;
            Context?.ReleaseSession();
        }

        /// <summary>Ends the session after <paramref name="seconds"/> without an edit (60 s for bridge sessions, C13).</summary>
        public bool ExpireSession(double seconds = 60)
        {
            if (!InSession || Now() - LastEdit < seconds) return false;
            EndSession();
            return true;
        }

        static double Now()
        {
            try { return UnityEngine.Time.realtimeSinceStartupAsDouble; }
            catch (Exception) { return 0; }
        }
    }
}
