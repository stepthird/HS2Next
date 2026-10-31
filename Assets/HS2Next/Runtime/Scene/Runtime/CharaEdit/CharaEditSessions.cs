// Edit sessions of characters (C13): while the Edit tab shows a character, or a bridge
// client asked for a session (chara.edit.info {session: true}), the character's editor keeps its decoded skin layers and
// resolved composite inputs (CharaEditor.InSession). StudioOps.Begin/EndEditSession reach this host through
// MakerServices.Sessions; the Edit tab opens its session inside FromUi(), so a tab session lasts until the tab closes,
// while a bridge session also ends 60 s after the character's last edit (CharaEditor.ExpireSession). Sessions are kept by
// node id and applied to the editor of every runtime's built character of that id at each flush point (Tick), so a
// rebuilt character (new editor) gets its session back.
using System;
using System.Collections.Generic;
using HS2Next.Chara.Edit;

namespace HS2Next.Scenes.Runtime
{
    public sealed class CharaEditSessions : IEditSessionHost
    {
        public static readonly CharaEditSessions Instance = new CharaEditSessions();

        /// <summary>Seconds without an edit after which a bridge session ends (C13).</summary>
        public static double BridgeExpirySeconds = 60;

        sealed class Entry { public bool Ui, Bridge; }

        readonly Dictionary<string, Entry> active = new Dictionary<string, Entry>(StringComparer.Ordinal);
        readonly HashSet<CharaRealization> begun = new HashSet<CharaRealization>();
        [ThreadStatic] static int uiDepth;

        /// <summary>Begin / End calls inside the returned scope are the Edit tab's (no expiry).</summary>
        public static IDisposable FromUi()
        {
            uiDepth++;
            return new UiScope();
        }

        sealed class UiScope : IDisposable
        {
            bool done;
            public void Dispose() { if (!done) { done = true; uiDepth--; } }
        }

        public void Begin(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId)) return;
            if (!active.TryGetValue(nodeId, out var e)) active[nodeId] = e = new Entry();
            if (uiDepth > 0) e.Ui = true; else e.Bridge = true;
        }

        public void End(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId) || !active.TryGetValue(nodeId, out var e)) return;
            if (uiDepth > 0) e.Ui = false; else e.Bridge = false;
            if (!e.Ui && !e.Bridge) active.Remove(nodeId);
        }

        /// <summary>True while a session of node <paramref name="nodeId"/> is open (tab or bridge).</summary>
        public bool IsActive(string nodeId) => nodeId != null && active.ContainsKey(nodeId);
        /// <summary>True while the Edit tab holds the session of <paramref name="nodeId"/>.</summary>
        public bool IsUi(string nodeId) => nodeId != null && active.TryGetValue(nodeId, out var e) && e.Ui;
        public int Count => active.Count;

        /// <summary>Starts the session of a freshly built character when its node has one (CharaRealization.AttachEditor).</summary>
        internal static void Sync(CharaRealization c)
        {
            var ed = c?.Editor;
            if (ed == null || !Instance.IsActive(c.NodeId)) return;
            if (!ed.InSession) ed.BeginSession();
            Instance.begun.Add(c);
        }

        /// <summary>Per flush point: open sessions reach the editors, bridge sessions expire, closed ones end.</summary>
        internal static void Tick(SceneRuntime rt)
        {
            var me = Instance;
            if (me.active.Count == 0 && me.begun.Count == 0) return;
            List<string> expired = null;
            foreach (var kv in me.active)
            {
                var c = rt.FindChara(kv.Key);
                var ed = c?.Editor;
                if (ed == null) continue;
                if (!ed.InSession) ed.BeginSession();
                me.begun.Add(c);
                if (!kv.Value.Ui && kv.Value.Bridge && ed.ExpireSession(BridgeExpirySeconds)) (expired ??= new List<string>()).Add(kv.Key);
            }
            if (expired != null) foreach (var id in expired) me.active.Remove(id);
            List<CharaRealization> ended = null;
            foreach (var c in me.begun)
            {
                // released or destroyed characters (any runtime) are forgotten; live ones of other runtimes wait for theirs
                if (c.State != RealizationState.Built || !c.Root) { (ended ??= new List<CharaRealization>()).Add(c); continue; }
                if (c.Runtime != rt) continue;
                var ed = c.Editor;
                if (ed != null && me.active.ContainsKey(c.NodeId) && rt.FindChara(c.NodeId) == c) continue;
                if (ed != null && ed.InSession) ed.EndSession();
                (ended ??= new List<CharaRealization>()).Add(c);
            }
            if (ended != null) foreach (var c in ended) me.begun.Remove(c);
        }
    }
}
