// The Unity layer's clothes facts (the clothes pages of the Maker; package A): per node and clothes kind the facts of the
// realized part — whether it has a CmpClothes, the row and prefab, the colour sets it uses, the main colour's alpha, the
// break, the option parts, the def / half objects, one-piece and NotBra, the state type, the colour groups and the
// prefab's default colour sets — read from the realization (CharaContext.Clothes[k]'s CmpClothes through ClothesReport), so
// the page shows what is drawn. A node without a built realization (a card-backed node, a failed load, not built yet) has
// no facts ("no prefab" for the gates). The node is resolved over the scene runtimes this class was shown: Track(rt) (weak,
// newest first; called by the callers that have a runtime, e.g. StudioOps.MakerGateHolds) or the static TryReport(rt, …),
// which tracks too. The facts of a character are cached until its editor flushes again (CachedFacts); the reports are not.
using System;
using System.Collections.Generic;
using HS2Next.Chara;
using HS2Next.Chara.Edit;

namespace HS2Next.Scenes.Runtime
{
    public sealed class ClothesInfo : IClothesInfo
    {
        /// <summary>The instance MakerSetup registers (one per app domain).</summary>
        public static readonly ClothesInfo Instance = new ClothesInfo();

        /// <summary>IClothesInfo: the facts of kind <paramref name="kind"/> (0..7) of node <paramref name="nodeId"/>'s built
        /// character in the tracked runtimes (newest first); false when no tracked runtime has it built.</summary>
        public bool TryKind(string nodeId, int kind, out ClothesKindFacts facts)
        {
            facts = ClothesKindFacts.None();
            if (string.IsNullOrEmpty(nodeId) || kind < 0 || kind > 7) return false;
            foreach (var rt in Tracked())
            {
                var ctx = Built(rt, nodeId);
                if (ctx != null) { facts = CachedFacts(ctx, kind); return true; }
            }
            return false;
        }

        /// <summary>The facts of kind <paramref name="kind"/> of node <paramref name="nodeId"/> in <paramref name="rt"/> (tracks the
        /// runtime); false when the node is not a built character there.</summary>
        public static bool TryKind(SceneRuntime rt, string nodeId, int kind, out ClothesKindFacts facts)
        {
            facts = ClothesKindFacts.None();
            Track(rt);
            if (kind < 0 || kind > 7) return false;
            var ctx = Built(rt, nodeId);
            if (ctx == null) return false;
            facts = CachedFacts(ctx, kind);
            return true;
        }

        // ---- the facts cache: per built character, valid while its editor's flush count is unchanged. Every change of what the
        // facts read goes through the character's units — an item change, an option toggle, a one-piece swap, a coordinate apply,
        // an undo or redo — and so through a flush that counts (CharaEditor.Flushes); a rebuild makes a new character context
        // (a new cache entry) and a release takes the node out of the built state. A page's gate checks (a few dozen per first
        // show) then build each kind's report once.

        sealed class Cached
        {
            public int Flushes = -1;
            public readonly ClothesKindFacts[] Kinds = new ClothesKindFacts[8];
        }

        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CharaContext, Cached> cache = new System.Runtime.CompilerServices.ConditionalWeakTable<CharaContext, Cached>();

        /// <summary>Diagnostic (MakerClothesTest): false = every facts request builds the report again (the behaviour before
        /// the cache). Default true.</summary>
        public static bool UseCache = true;
        /// <summary>Facts built from a report / handed out from the cache since the start (diagnostics, tests).</summary>
        public static int FactsBuilt { get; private set; }
        public static int FactsFromCache { get; private set; }

        /// <summary>The facts of kind <paramref name="kind"/> of a built character, from the cache while the character's editor
        /// has not flushed since they were read (a copy: callers may keep or change it).</summary>
        public static ClothesKindFacts CachedFacts(CharaContext ctx, int kind)
        {
            var ed = ctx?.Editor;
            if (ed == null || !UseCache)
            {
                FactsBuilt++;
                return FactsOf(ClothesReport.KindOf(ctx, kind, false, false));
            }
            var c = cache.GetValue(ctx, _ => new Cached());
            if (c.Flushes != ed.Flushes)
            {
                Array.Clear(c.Kinds, 0, c.Kinds.Length);
                c.Flushes = ed.Flushes;
            }
            var f = c.Kinds[kind];
            if (f == null)
            {
                f = c.Kinds[kind] = FactsOf(ClothesReport.KindOf(ctx, kind, false, false));
                FactsBuilt++;
            }
            else FactsFromCache++;
            return Copy(f);
        }

        /// <summary>A copy of facts (groups and default colour sets copied member by member: the document clone of
        /// ClothesKindFacts.Clone costs about a tenth of a millisecond per set, which a page's gate checks would pay dozens of
        /// times).</summary>
        public static ClothesKindFacts Copy(ClothesKindFacts f)
        {
            if (f == null) return null;
            var c = new ClothesKindFacts
            {
                HasPrefab = f.HasPrefab, Item = f.Item, PrefabName = f.PrefabName,
                UseColorN01 = f.UseColorN01, UseColorN02 = f.UseColorN02, UseColorN03 = f.UseColorN03,
                UseColorA01 = f.UseColorA01, UseColorA02 = f.UseColorA02, UseColorA03 = f.UseColorA03,
                UseBreak = f.UseBreak, Option01Count = f.Option01Count, Option02Count = f.Option02Count, Coordinate = f.Coordinate,
                NotBra = f.NotBra, StateType = f.StateType, Skipped = f.Skipped,
                HasTopDef = f.HasTopDef, HasTopHalf = f.HasTopHalf, HasBotDef = f.HasBotDef, HasBotHalf = f.HasBotHalf,
            };
            c.Groups = new ClothesGroupFacts[f.Groups?.Length ?? 0];
            for (int i = 0; i < c.Groups.Length; i++)
            {
                var g = f.Groups[i];
                c.Groups[i] = g == null ? null : new ClothesGroupFacts { Group = g.Group, Renderers = g.Renderers, Slots = g.Slots, MainTexWidth = g.MainTexWidth, MainTexHeight = g.MainTexHeight, HasCreators = g.HasCreators };
            }
            if (f.Defaults != null)
            {
                c.Defaults = new HS2Next.Save.ClothesColor[f.Defaults.Length];
                for (int i = 0; i < f.Defaults.Length; i++) c.Defaults[i] = CopySet(f.Defaults[i]);
            }
            return c;
        }

        static HS2Next.Save.ClothesColor CopySet(HS2Next.Save.ClothesColor s) => s == null ? null : new HS2Next.Save.ClothesColor
        {
            Base = s.Base, Pattern = s.Pattern, PatternColor = s.PatternColor, Layout = s.Layout, Rotation = s.Rotation, Gloss = s.Gloss, Metallic = s.Metallic,
        };

        /// <summary>The Unity-free facts of a kind report: the row reference, the prefab, the CmpClothes flags, the option counts,
        /// the row's Coordinate / NotBra / StateType columns, the one-piece skip, the def / half objects, the colour groups whose
        /// rendNormal0j has live renderers, and the prefab defaults. A kind without a CmpClothes has HasPrefab false (its row
        /// columns and the skip are still given).</summary>
        public static ClothesKindFacts FactsOf(ClothesReport.KindReport r)
        {
            var f = ClothesKindFacts.None();
            if (r == null) return f;
            int colon = r.Row != null ? r.Row.IndexOf(':') : -1;
            f.Item = colon > 0 ? r.Row.Substring(colon + 1) : null;
            f.Skipped = r.Skipped;
            f.Coordinate = r.Columns.TryGetValue("Coordinate", out var co) ? co : null;
            f.NotBra = r.NotBra;
            f.StateType = int.TryParse(r.StateType, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int st) ? st : -1;
            if (!r.HasCmpClothes) return f;
            f.HasPrefab = true;
            f.PrefabName = r.Prefab;
            f.UseColorN01 = r.UseColorN[0]; f.UseColorN02 = r.UseColorN[1]; f.UseColorN03 = r.UseColorN[2];
            f.UseColorA01 = r.UseColorA[0]; f.UseColorA02 = r.UseColorA[1]; f.UseColorA03 = r.UseColorA[2];
            f.UseBreak = r.UseBreak;
            f.Option01Count = r.Opt01; f.Option02Count = r.Opt02;
            f.HasTopDef = r.HasTopDef; f.HasTopHalf = r.HasTopHalf; f.HasBotDef = r.HasBotDef; f.HasBotHalf = r.HasBotHalf;
            var groups = new List<ClothesGroupFacts>();
            for (int j = 0; j < r.Groups.Length; j++)
            {
                var g = r.Groups[j];
                if (g == null || g.Renderers == 0) continue;
                groups.Add(new ClothesGroupFacts { Group = j + 1, Renderers = g.Renderers, Slots = g.Slots, MainTexWidth = g.MainWidth, MainTexHeight = g.MainHeight, HasCreators = g.HasCreators });
            }
            f.Groups = groups.ToArray();
            if (r.Defaults != null)
            {
                f.Defaults = new HS2Next.Save.ClothesColor[r.Defaults.Length];
                for (int i = 0; i < r.Defaults.Length; i++) f.Defaults[i] = CopySet(r.Defaults[i]);
            }
            return f;
        }

        static readonly List<WeakReference<SceneRuntime>> runtimes = new List<WeakReference<SceneRuntime>>();

        /// <summary>Makes <paramref name="rt"/> the first runtime the node lookups look in (kept weakly; disposed runtimes are
        /// dropped).</summary>
        public static void Track(SceneRuntime rt)
        {
            if (rt == null || rt.IsDisposed) return;
            lock (runtimes)
            {
                for (int i = runtimes.Count - 1; i >= 0; i--)
                    if (!runtimes[i].TryGetTarget(out var r) || r == null || r.IsDisposed || ReferenceEquals(r, rt)) runtimes.RemoveAt(i);
                runtimes.Add(new WeakReference<SceneRuntime>(rt));
            }
        }

        /// <summary>The report of kind <paramref name="kind"/> (0..7) of node <paramref name="nodeId"/>'s built character in the
        /// tracked runtimes (newest first); false when no tracked runtime has it built.</summary>
        public bool TryKindReport(string nodeId, int kind, out ClothesReport.KindReport report)
        {
            report = null;
            if (string.IsNullOrEmpty(nodeId) || kind < 0 || kind > 7) return false;
            foreach (var rt in Tracked())
            {
                var ctx = Built(rt, nodeId);
                if (ctx != null) { report = ClothesReport.KindOf(ctx, kind); return report != null; }
            }
            return false;
        }

        /// <summary>The report of kind <paramref name="kind"/> of node <paramref name="nodeId"/> in <paramref name="rt"/> (tracks
        /// the runtime); false when the node is not a built character there.</summary>
        public static bool TryReport(SceneRuntime rt, string nodeId, int kind, out ClothesReport.KindReport report)
        {
            report = null;
            Track(rt);
            if (kind < 0 || kind > 7) return false;
            var ctx = Built(rt, nodeId);
            if (ctx == null) return false;
            report = ClothesReport.KindOf(ctx, kind);
            return report != null;
        }

        /// <summary>The whole clothes report of node <paramref name="nodeId"/> in <paramref name="rt"/>; null when the node is not
        /// a built character there.</summary>
        public static ClothesReport ReportOf(SceneRuntime rt, string nodeId)
        {
            var ctx = Built(rt, nodeId);
            return ctx == null ? null : ClothesReport.Of(ctx);
        }

        static SceneRuntime[] Tracked()
        {
            lock (runtimes)
            {
                var l = new List<SceneRuntime>();
                for (int i = runtimes.Count - 1; i >= 0; i--)
                {
                    if (runtimes[i].TryGetTarget(out var r) && r != null && !r.IsDisposed) l.Add(r);
                    else runtimes.RemoveAt(i);
                }
                return l.ToArray();
            }
        }

        static CharaContext Built(SceneRuntime rt, string nodeId)
        {
            if (rt == null || rt.IsDisposed || nodeId == null) return null;
            var c = rt.FindChara(nodeId);
            if (c == null || c.State != RealizationState.Built || c.InGraveyard || !c.Root) return null;
            return c.Context;
        }
    }
}
