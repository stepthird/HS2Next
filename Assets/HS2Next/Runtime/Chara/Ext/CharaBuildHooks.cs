// Generic build hooks of a character (the extension points of a character build that row 5.1 promotes into the plugin
// API). Code registers a hook for a stage; CharaAssembler and the edit units run the registered hooks at that point of
// every build and, for the later stages, again in the edit flushes that redo that point:
//   BodyMesh     after the body prefab is spawned and rebound, before the skin is composited and before any edit unit
//                runs (builds only). A hook may replace body / part meshes, bones and part materials here (it keeps the
//                body draw material), implant bones (ImplantBone), register skin layers (CharaContext.SkinLayers: the
//                texture stage composites them under / over the skin), and set CharaContext.ReplacedBodyMesh /
//                BodyNormals when it replaced the body mesh.
//   Textures     after the plugin texture stage (KSOX, KCOX, MaterialEditor): in the build, and in every edit flush that
//                ran a unit of the steps up to the texture stage (parts, masks, composites, materials, plugin textures).
//   AfterMosaic  right after the mosaic stage (same runs as Textures). The mosaic stage itself is not a hook: it is the
//                fixed unit MosaicStageUnit below, which forces the mosaic material on the mosaic set's members.
//   Final        after the plugin final stage (pushUp, AAC, ABMX, DBE, InvisibleBody): in the build and whenever that
//                stage runs again in an edit.
// Hooks run in (order, registration) order; an exception is caught, logged and counted (the build goes on). Each run
// replaces the hook's entry in ExtStats.Hooks (ran, renderers changed, errors), so an edit leaves the statistics of a
// fresh build. Hooks must be idempotent for the stages that re-run (Textures, AfterMosaic, Final): their output must be a
// function of the character's state. Main thread only.
// The registry also holds FK node providers: rows of FK node keys beyond the Bone lists (RegisterFkNodes / FkNodes), which
// the FK writer joins for a character whose body mesh a hook replaced and the pose import joins for keys the Bone lists
// lack. Content a hook reads: the catalog (ContentCatalog.LoadBundle / LoadAssetFrom, through the texture guard), the
// zipmods' manifest elements (ZipmodInfo.ManifestElements / ReadManifestElement), the character's documents and decoded
// plugin data (CharaContext.Chara / Coord / Ext).
// Hooks a plugin registers through the plugin Api are owned registrations (RegisterOwned): never at AfterMosaic, removed
// together when the plugin unloads (UnregisterOwner), and after each owned hook at Final the mosaic is re-asserted
// (MosaicUnit.Reassert: every member slot gets its mosaic material back; warning api.mosaicProtected naming the hook and the
// member; the owner is told). Core registrations are not re-asserted.
// The official build registers no hook.
using System;
using System.Collections.Generic;
using System.Linq;
using HS2Next.Chara.Edit;
using HS2Next.Content;
using HS2Next.Motion.Fk;
using HS2Next.Save;
using UnityEngine;

namespace HS2Next.Chara.Ext
{
    /// <summary>The points of a character build where registered hooks run (see the file header).</summary>
    public enum HookStage { BodyMesh = 0, Textures = 1, AfterMosaic = 2, Final = 3 }

    /// <summary>What a hook gets for one run.</summary>
    public sealed class HookArgs
    {
        /// <summary>The registration's id (log lines and ExtStats.Hooks name it).</summary>
        public string Id { get; internal set; }
        public HookStage Stage { get; internal set; }
        /// <summary>The character being built or edited: objects, renderers (Body, BodyRenderer, Clothes, Hair, Accessories),
        /// bones (BodyBones, HeadBones), materials (BodyDraw, FaceDraw, OwnedMaterials), skin layers, documents, decoded
        /// plugin data, the catalog.</summary>
        public CharaContext Chara { get; internal set; }
        public ContentCatalog Catalog => Chara?.Catalog;
        /// <summary>True during the build (BodyMesh is always a build stage).</summary>
        public bool InBuild { get; internal set; }
        /// <summary>The edit flush running now (what it changed), or null (BodyMesh, or outside a flush).</summary>
        public FlushReport Report { get; internal set; }

        internal int Renderers;

        /// <summary>Counts renderers whose mesh, bones or materials the hook changed (ExtStats.Hooks).</summary>
        public void RenderersChanged(int n = 1) { if (n > 0) Renderers += n; }

        /// <summary>A line in the character's build log, prefixed with the hook id.</summary>
        public void Log(string line) => Chara?.Log?.Add("hook " + Id + ": " + line);
    }

    /// <summary>One run of one hook registration (ExtStats.Hooks, keyed "&lt;id&gt;@&lt;stage&gt;"): the last run's values.</summary>
    public sealed class HookRun
    {
        public bool Ran;
        public int Renderers, Errors;
        public string LastError;

        public HookRun Clone() => new HookRun { Ran = Ran, Renderers = Renderers, Errors = Errors, LastError = LastError };

        public override string ToString() => (Ran ? "ran" : "not run") + (Renderers > 0 ? " renderers " + Renderers : "") + (Errors > 0 ? " errors " + Errors : "");
    }

    /// <summary>FK node rows a provider supplies (rows of node keys beyond the Bone lists) and what merging them found.</summary>
    public sealed class FkNodeSet
    {
        public IReadOnlyList<FkRow> Rows = Array.Empty<FkRow>();
        public readonly FindingList Findings = new FindingList();
    }

    public static class CharaBuildHooks
    {
        /// <summary>One registered hook.</summary>
        public sealed class Registration
        {
            public string Id;
            public HookStage Stage;
            public int Order;
            public Action<HookArgs> Run;
            /// <summary>The plugin GUID of an owned registration (RegisterOwned), else null (Core).</summary>
            public string Owner;
            /// <summary>Owned registrations: told of each member path the re-assert after this hook restored.</summary>
            public Action<string> OnMosaicProtected;
            internal long Seq;
            public override string ToString() => Id + "@" + Stage + " (order " + Order + ")";
        }

        sealed class NodeProvider { public string Id; public string Owner; public Func<ContentCatalog, FkNodeSet> Get; public long Seq; }

        static readonly List<Registration> hooks = new List<Registration>();
        static readonly List<NodeProvider> nodeProviders = new List<NodeProvider>();
        static long seq;

        /// <summary>The registered hooks in run order (stage, order, registration).</summary>
        public static IReadOnlyList<Registration> Registered =>
            hooks.OrderBy(h => (int)h.Stage).ThenBy(h => h.Order).ThenBy(h => h.Seq).ToList();

        /// <summary>The ids of the registered FK node providers, in registration order.</summary>
        public static IReadOnlyList<string> FkNodeProviders => nodeProviders.OrderBy(p => p.Seq).Select(p => p.Id).ToList();

        /// <summary>Registers <paramref name="run"/> for <paramref name="stage"/> under <paramref name="id"/> (a second
        /// registration of the same id and stage replaces the first). Lower <paramref name="order"/> runs first.</summary>
        public static void Register(string id, HookStage stage, Action<HookArgs> run, int order = 0)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("a hook needs an id", nameof(id));
            if (run == null) throw new ArgumentNullException(nameof(run));
            hooks.RemoveAll(h => h.Id == id && h.Stage == stage);
            hooks.Add(new Registration { Id = id, Stage = stage, Order = order, Run = run, Seq = ++seq });
        }

        /// <summary>
        /// Registers a hook on behalf of a plugin (<paramref name="owner"/> = its GUID): as <see cref="Register"/>, but never
        /// at AfterMosaic (ArgumentException), removed by <see cref="UnregisterOwner"/>, and after each of its runs at Final
        /// the mosaic is re-asserted, each restored member reported to <paramref name="onMosaicProtected"/> (member path).
        /// A registration of the same id and stage replaces the earlier one (of any owner).
        /// </summary>
        public static void RegisterOwned(string owner, string id, HookStage stage, Action<HookArgs> run, int order, Action<string> onMosaicProtected)
        {
            if (string.IsNullOrEmpty(owner)) throw new ArgumentException("an owned hook needs its owner", nameof(owner));
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("a hook needs an id", nameof(id));
            if (run == null) throw new ArgumentNullException(nameof(run));
            if (stage != HookStage.BodyMesh && stage != HookStage.Textures && stage != HookStage.Final)
                throw new ArgumentException("a plugin hook runs at BodyMesh, Textures or Final, not " + stage, nameof(stage));
            hooks.RemoveAll(h => h.Id == id && h.Stage == stage);
            hooks.Add(new Registration { Id = id, Stage = stage, Order = order, Run = run, Owner = owner, OnMosaicProtected = onMosaicProtected, Seq = ++seq });
        }

        /// <summary>Removes every hook and FK node provider registered for <paramref name="owner"/>; returns how many.</summary>
        public static int UnregisterOwner(string owner) =>
            string.IsNullOrEmpty(owner) ? 0 : hooks.RemoveAll(h => h.Owner == owner) + nodeProviders.RemoveAll(p => p.Owner == owner);

        /// <summary>The owned registrations of <paramref name="owner"/> in run order.</summary>
        public static IReadOnlyList<Registration> OwnedBy(string owner) => Registered.Where(h => h.Owner == owner && owner != null).ToList();

        /// <summary>Removes the hook <paramref name="id"/> of <paramref name="stage"/>; true when one was registered.</summary>
        public static bool Unregister(string id, HookStage stage) => hooks.RemoveAll(h => h.Id == id && h.Stage == stage) > 0;

        /// <summary>Removes every hook and FK node provider registered under <paramref name="id"/>; returns how many.</summary>
        public static int UnregisterAll(string id) => hooks.RemoveAll(h => h.Id == id) + nodeProviders.RemoveAll(p => p.Id == id);

        /// <summary>True when a hook is registered for <paramref name="stage"/>.</summary>
        public static bool Any(HookStage stage) => hooks.Any(h => h.Stage == stage);

        /// <summary>
        /// Runs the hooks of <paramref name="stage"/> on <paramref name="ctx"/> (CharaExtApplier for BodyMesh and Final, the
        /// mosaic stage unit for Textures and AfterMosaic). Each run replaces its ExtStats.Hooks entry; an exception is logged
        /// ("hook error ...") and counted in the entry.
        /// </summary>
        public static void Run(HookStage stage, CharaContext ctx, bool inBuild, FlushReport report)
        {
            if (ctx == null || hooks.Count == 0) return;
            var list = hooks.Where(h => h.Stage == stage).OrderBy(h => h.Order).ThenBy(h => h.Seq).ToList();
            foreach (var h in list)
            {
                var a = new HookArgs { Id = h.Id, Stage = stage, Chara = ctx, InBuild = inBuild, Report = report };
                var run = new HookRun { Ran = true };
                try { h.Run(a); }
                catch (Exception e)
                {
                    run.Errors++;
                    run.LastError = e.GetType().Name + ": " + e.Message;
                    ctx.Log?.Add("hook error " + h.Id + "@" + stage + ": " + run.LastError);
                    Debug.LogWarning("hook error " + h.Id + "@" + stage + ": " + e);
                }
                run.Renderers = a.Renderers;
                if (ctx.Stats != null) ctx.Stats.Hooks[h.Id + "@" + stage] = run;
                if (h.Owner != null && stage == HookStage.Final) Reassert(h, ctx);
            }
        }

        /// <summary>The mosaic after an owned Final hook: every member slot gets its mosaic material back; the owner is told
        /// of each member restored (a throw from it is ignored).</summary>
        static void Reassert(Registration h, CharaContext ctx)
        {
            List<string> restored;
            try { restored = HS2Next.Chara.Mosaic.MosaicUnit.Reassert(ctx, h.Id); }
            catch (Exception e)
            {
                Debug.LogWarning("mosaic re-assert after hook " + h.Id + ": " + e);
                return;
            }
            if (h.OnMosaicProtected == null) return;
            foreach (var path in restored)
            {
                try { h.OnMosaicProtected(path); }
                catch (Exception) { }
            }
        }

        // ---------------------------------------------------------------- bones

        /// <summary>
        /// Adds a bone named <paramref name="name"/> under <paramref name="parent"/> with the given local pose and records it in
        /// CharaContext.BodyBones (first occurrence of a name wins there, as for the skeleton's own bones); returns the existing
        /// body bone of that name when there is one. Bones added so are destroyed with the character.
        /// </summary>
        public static Transform ImplantBone(CharaContext ctx, string name, Transform parent, Vector3 localPosition, Quaternion localRotation, Vector3 localScale)
        {
            if (ctx == null || string.IsNullOrEmpty(name) || !parent) return null;
            if (ctx.BodyBones != null && ctx.BodyBones.TryGetValue(name, out var have) && have) return have;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;
            go.transform.localScale = localScale;
            if (ctx.BodyBones != null) ctx.BodyBones[name] = go.transform;
            return go.transform;
        }

        // ---------------------------------------------------------------- FK node rows

        /// <summary>Registers a provider of FK node rows (called with the catalog; may cache per catalog; null = none).</summary>
        public static void RegisterFkNodes(string id, Func<ContentCatalog, FkNodeSet> provider) => RegisterFkNodesOwned(null, id, provider);

        /// <summary>As <see cref="RegisterFkNodes"/> on behalf of a plugin (removed by <see cref="UnregisterOwner"/>).</summary>
        public static void RegisterFkNodesOwned(string owner, string id, Func<ContentCatalog, FkNodeSet> provider)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("a provider needs an id", nameof(id));
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            nodeProviders.RemoveAll(p => p.Id == id);
            nodeProviders.Add(new NodeProvider { Id = id, Owner = owner, Get = provider, Seq = ++seq });
        }

        /// <summary>
        /// The FK node rows of every provider for <paramref name="catalog"/>, merged in registration order (the first row of a
        /// key wins; a later different definition is a warning fk.nodeConflict), or null when no provider is registered.
        /// </summary>
        public static FkNodeSet FkNodes(ContentCatalog catalog)
        {
            if (nodeProviders.Count == 0) return null;
            var set = new FkNodeSet();
            var rows = new List<FkRow>();
            var byKey = new Dictionary<int, FkRow>();
            foreach (var p in nodeProviders.OrderBy(p => p.Seq))
            {
                FkNodeSet s;
                try { s = p.Get(catalog); }
                catch (Exception e)
                {
                    set.Findings.Warning("fk.nodeProvider", p.Id, p.Id + ": " + e.GetType().Name + ": " + e.Message);
                    continue;
                }
                if (s == null) continue;
                set.Findings.AddRange(s.Findings);
                foreach (var r in s.Rows ?? Array.Empty<FkRow>())
                {
                    if (byKey.TryGetValue(r.No, out var had))
                    {
                        if (had.Name != r.Name || had.GroupColumn != r.GroupColumn || had.Level != r.Level)
                            set.Findings.Warning("fk.nodeConflict", r.No.ToString(System.Globalization.CultureInfo.InvariantCulture),
                                $"node {r.No}: {p.Id} defines {r.Name} (col {r.GroupColumn}, level {r.Level}); kept {had.Name} (col {had.GroupColumn}, level {had.Level})");
                        continue;
                    }
                    byKey[r.No] = r;
                    rows.Add(r);
                }
            }
            set.Rows = rows;
            return set;
        }
    }
}

namespace HS2Next.Chara.Edit.Units
{
    /// <summary>
    /// The mosaic stage slot (step 5, after the plugin texture stage ext.textures): the Textures hooks, the mosaic of the
    /// character's mosaic-set members (<c>ApplyMosaic</c>, implemented by the mosaic code in its own file of this partial
    /// class), then the AfterMosaic hooks. It runs in the build and in every edit flush that ran a unit of the steps before
    /// it (<see cref="EditUnit.FollowsEarlierSteps"/>): a change of the body composite, a part, a coordinate, a material or
    /// MaterialEditor data re-runs it, so an edited character equals a fresh build. The body mesh is replaced only before
    /// the build's flush (stage BodyMesh); the breast-normal unit (step 6) later swaps the drawn body mesh for an owned copy
    /// with the same vertices and UVs.
    /// </summary>
    public sealed partial class MosaicStageUnit : UnitBase
    {
        /// <summary>The unit id ("mosaic").</summary>
        public const string UnitId = "mosaic";
        /// <summary>Its order inside step 5 (ext.textures has 0).</summary>
        public const int StageOrder = 100;

        public MosaicStageUnit() : base(UnitId, EditStep.PluginTextures, StageOrder) { }

        public override bool FollowsEarlierSteps => true;

        public override void Apply(EditContext x)
        {
            if (x?.Ctx == null) return;
            HS2Next.Chara.Ext.CharaBuildHooks.Run(HS2Next.Chara.Ext.HookStage.Textures, x.Ctx, x.InBuild, x.Report);
            ApplyMosaic(x);
            HS2Next.Chara.Ext.CharaBuildHooks.Run(HS2Next.Chara.Ext.HookStage.AfterMosaic, x.Ctx, x.InBuild, x.Report);
        }

        /// <summary>The mosaic of the mosaic-set members (implemented in the mosaic code's part of this class).</summary>
        partial void ApplyMosaic(EditContext x);
    }
}
