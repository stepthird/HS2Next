// The Maker edits' flush point of a scene: SceneRuntime.Apply calls Run after the
// per-node appliers (dirty paths) and before transforms / visibility, so a rebuild a flush asks for gets its children
// re-attached in the same pass. Characters whose editor was marked (CharaRealization.ApplyChanges, or Request) are
// flushed once, in model pre-order; nothing runs when nothing is queued (cheap per frame). Run also registers the Unity
// layer's Maker services for the runtime's catalog (MakerSetup) and keeps the edit sessions (CharaEditSessions).
// Phase 4.1: during a drag (an open bus transaction in play mode, or ForcePreview in tests)
// the flushes run in Preview mode (the deferrable step-9 work waits); characters holding deferred work stay queued until a
// flush point without an open transaction runs it. The face composite is not throttled (F8: UF1 measured one face creator
// well below 8 ms).
// Phase 4.2 (GB3, Y4): the C5 throttle of the body composite is judged by the cost weight
// of the pending body rebuild (CharaEditor.PendingBodyWeight: Σ over the body creators it rebuilds of 1 + their skin
// layers). Mode BodyThrottle (app option maker.bodyThrottle, applied by the UI): Auto = a drag frame waits when W ≥ 2 and
// the body was recomposited on the previous frame (two 4096² creators, or one with a KSOX / uncensor layer); Always = 4.0's
// rule (any body composite, W ≥ 1); Off = never. Commit, cancel and undo always flush; play mode only.
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>The C5 body-composite throttle during drags (app option maker.bodyThrottle).</summary>
    public enum BodyThrottleMode
    {
        /// <summary>Throttle a drag frame whose body rebuild has weight ≥ 2 (default).</summary>
        Auto = 0,
        /// <summary>Throttle every body composite of a drag (4.0's rule, weight ≥ 1): for slower GPUs.</summary>
        Always = 1,
        /// <summary>Never throttle.</summary>
        Off = 2,
    }

    public static class CharaEditFlush
    {
        /// <summary>The throttle mode (view state, never history). The UI applies the app option maker.bodyThrottle here (as it
        /// applies maker.keepColours); tests set it directly. Default Auto.</summary>
        public static BodyThrottleMode BodyThrottle = BodyThrottleMode.Auto;

        /// <summary>The option's text form: "auto", "always", "off".</summary>
        public static string BodyThrottleName(BodyThrottleMode m) => m == BodyThrottleMode.Always ? "always" : m == BodyThrottleMode.Off ? "off" : "auto";

        /// <summary>Parses "auto" / "always" / "off" (case-insensitive); false (and Auto) for anything else.</summary>
        public static bool TryParseBodyThrottle(string s, out BodyThrottleMode mode)
        {
            switch ((s ?? "").Trim().ToLowerInvariant())
            {
                case "auto": mode = BodyThrottleMode.Auto; return true;
                case "always": mode = BodyThrottleMode.Always; return true;
                case "off": mode = BodyThrottleMode.Off; return true;
                default: mode = BodyThrottleMode.Auto; return false;
            }
        }

        /// <summary>The throttle rule for a pending body rebuild of weight <paramref name="weight"/> (true = the drag frame
        /// waits when the body was recomposited on the previous frame): Auto W ≥ 2, Always W ≥ 1, Off never.</summary>
        public static bool ThrottlesWeight(BodyThrottleMode mode, int weight) =>
            mode == BodyThrottleMode.Off ? false : mode == BodyThrottleMode.Always ? weight >= 1 : weight >= 2;

        sealed class Pending { public HashSet<CharaRealization> Set = new HashSet<CharaRealization>(); }
        static readonly ConditionalWeakTable<SceneRuntime, Pending> pending = new ConditionalWeakTable<SceneRuntime, Pending>();

        /// <summary>
        /// C5 master switch: during a drag (a bus transaction is open) a character whose pending body composite
        /// throttles by <see cref="BodyThrottle"/> (Phase 4.2: by weight) and was recomposited on the previous frame waits one
        /// frame; the commit / cancel (transaction closed) always flushes. Play mode only (edit-mode batch runs have no
        /// frames). False = no throttle whatever the mode.
        /// </summary>
        public static bool ThrottleBodyDuringDrags = true;
        /// <summary>Flushes deferred by the C5 throttle (diagnostics).</summary>
        public static int Throttled { get; private set; }

        /// <summary>Flushes run by Run so far (all runtimes; tests).</summary>
        public static int Flushed { get; private set; }

        /// <summary>
        /// Test switch: edit-mode runs have no play-mode drags; with it on, a flush
        /// point inside an open bus transaction flushes in Preview mode as a drag frame in play mode does (the C5 throttle
        /// stays play-mode only). Off by default.
        /// </summary>
        public static bool ForcePreview;
        /// <summary>Preview flushes run by Run so far (all runtimes; tests).</summary>
        public static int PreviewFlushes { get; private set; }

        /// <summary>The flush mode of <paramref name="rt"/>'s flush point now: Preview during a drag (an open bus transaction,
        /// in play mode or with <see cref="ForcePreview"/>), else Normal.</summary>
        public static HS2Next.Chara.Edit.FlushMode ModeOf(SceneRuntime rt) =>
            rt?.Session?.Commands?.ActiveTransaction != null && (ForcePreview || UnityEngine.Application.isPlaying)
                ? HS2Next.Chara.Edit.FlushMode.Preview : HS2Next.Chara.Edit.FlushMode.Normal;

        internal static void Queue(SceneRuntime rt, CharaRealization c)
        {
            if (rt == null || c == null) return;
            pending.GetOrCreateValue(rt).Set.Add(c);
        }

        /// <summary>Queues a character whose editor was marked directly (tests, the bridge): its next flush point flushes it.</summary>
        public static void Request(CharaRealization c) => Queue(c?.Runtime, c);

        /// <summary>True when a character of <paramref name="rt"/> waits for its flush.</summary>
        public static bool AnyPending(SceneRuntime rt) => rt != null && pending.TryGetValue(rt, out var p) && p.Set.Count > 0;

        /// <summary>True when a drag frame of <paramref name="c"/> would wait now: the mode's rule on the editor's
        /// pending body weight, the body recomposited on frame <paramref name="frame"/> − 1, no rebuild requested.</summary>
        static bool Throttle(CharaRealization c, HS2Next.Chara.Edit.CharaEditor ed, int frame)
        {
            if (ed == null || ed.RebuildRequested || c.LastBodyCompositeFrame != frame - 1) return false;
            int w = ed.PendingBodyWeight();
            c.LastPendingBodyWeight = w;
            return ThrottlesWeight(BodyThrottle, w);
        }

        /// <summary>The flush point (SceneRuntime.Apply).</summary>
        public static void Run(SceneRuntime rt)
        {
            if (rt == null) return;
            MakerSetup.Ensure(rt.Services);
            CharaEditSessions.Tick(rt);
            if (!pending.TryGetValue(rt, out var p) || p.Set.Count == 0) return;
            var set = p.Set;
            p.Set = new HashSet<CharaRealization>();
            var mode = ModeOf(rt);
            bool dragging = ThrottleBodyDuringDrags && UnityEngine.Application.isPlaying && rt.Session?.Commands?.ActiveTransaction != null;
            int frame = UnityEngine.Time.frameCount;
            foreach (var n in rt.Model.PreOrder())
            {
                var c = rt.FindChara(n.Id);
                if (c == null || !set.Remove(c)) continue;
                if (c.Node != n || c.InGraveyard || !c.HasHolder) continue;
                var ed = c.Editor;
                if (dragging && Throttle(c, ed, frame))
                {
                    p.Set.Add(c);   // next frame
                    Throttled++;
                    c.EditThrottled++;
                    continue;
                }
                var r = c.FlushEdit(mode);
                if (r != null)
                {
                    Flushed++;
                    if (r.Mode == HS2Next.Chara.Edit.FlushMode.Preview) PreviewFlushes++;
                    if (r.Units.Contains(HS2Next.Chara.Edit.UnitMap.SkinBody)) c.LastBodyCompositeFrame = frame;
                }
                // a character with deferred work stays queued until a flush point without an open transaction
                // (commit, cancel, undo, the end of a bridge batch) runs it
                var ed2 = c.Editor;
                if (ed2 != null && ed2.HasDeferred) p.Set.Add(c);
                if (set.Count == 0) break;
            }
        }
    }
}
