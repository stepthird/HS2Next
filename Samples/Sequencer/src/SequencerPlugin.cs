// Sample sequencer for HS2Next (CC0-1.0: dedicated to the public domain).
// The plugin's entry type: it registers the seq.* commands, the scene's ext block and its event handlers in Load,
// plays the scene's sequence once per frame (inline calls for the tracks, queued calls for the events), adopts the
// stored sequence when a scene has loaded, and follows node removal, undo and the scene's pause and steps. Event
// handlers never change the scene themselves: they record what happened and the next Update acts.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HS2Next.Api.Plugins;

namespace Stp3.Samples.Sequencer
{
    public sealed class SequencerPlugin : IPlugin, IPluginUpdate, IPluginSceneLoaded
    {
        IPluginContext ctx;
        Sequence readSequence;
        int readFrame = int.MinValue;

        internal IPluginContext Context => ctx;
        /// <summary>The scene's sequence player.</summary>
        internal Player Player { get; private set; }
        /// <summary>The running or last measurement (null before the first).</summary>
        internal Measurement Measurement { get; set; }

        public void Load(IPluginContext context)
        {
            ctx = context ?? throw new ArgumentNullException(nameof(context));
            Player = new Player(new ContextHost(ctx));
            ctx.Ext.Register(new SequenceBlock(ctx.Guid, () => Player.Sequence, OnBlockRead));
            SequencerCommands.Register(this, ctx.Commands);
            var ev = ctx.Events;
            ev.AppReady += OnAppReady;
            ev.SceneCleared += OnSceneCleared;
            ev.NodesAdded += OnNodesAdded;
            ev.NodesRemoved += OnNodesRemoved;
            ev.NodeForgotten += OnNodeForgotten;
            ev.SceneStepped += OnSceneStepped;
            ev.Quitting += OnQuitting;
        }

        public void Unload()
        {
            Measurement?.Abort("the plugin unloaded", restore: false);
            ctx?.Log.Info("unloaded");
        }

        public void Update(in PluginFrame frame)
        {
            var m = Measurement;
            if (m != null && m.Running)
            {
                // The scene's sequence waits while a measurement drives the same nodes.
                m.Update(frame);
                return;
            }
            Player.Update(frame.SceneDt);
        }

        /// <summary>
        /// A loaded scene adopts the sequence its block held (read while that file loaded, in the same frame), stopped at
        /// 0 with a pending apply; autoplay starts it. A new scene has none.
        /// </summary>
        public void SceneLoaded(SceneLoadInfo info)
        {
            Sequence s = null;
            if (info != null && info.Kind != SceneLoadKind.New && readSequence != null && readFrame == ctx.Frames.Frame) s = readSequence;
            readSequence = null;
            Player.SetSequence(s);
            Player.ResetCounters();
            if (s != null && s.Autoplay) Player.Play();
        }

        void OnBlockRead(Sequence s)
        {
            readSequence = s;
            readFrame = ctx.Frames.Frame;
        }

        void OnAppReady() => ctx.Log.Info("ready: commands " + string.Join(", ", SequencerCommands.Names));

        void OnSceneCleared()
        {
            Measurement?.Abort("the scene was replaced", restore: false);
            Player.SetSequence(null);
        }

        void OnNodesAdded(NodeChangeInfo info) => Player.NodesAdded(Ids(info));

        void OnNodesRemoved(NodeChangeInfo info) => Player.NodesRemoved(Ids(info));

        void OnNodeForgotten(string id) => Player.NodeForgotten(id);

        void OnSceneStepped(StepInfo step) => Player.Step(step.Count, step.Dt);

        void OnQuitting()
        {
            ctx.Log.Info("quitting: " + Player.StateName(Player.State) + ", events fired " + Player.EventsFired + ", writes " + Player.Writes.Done);
        }

        static IEnumerable<string> Ids(NodeChangeInfo info)
        {
            if (info == null) yield break;
            foreach (var n in info.Nodes) yield return n.Id;
        }
    }

    /// <summary>The player's host on a plugin context: inline and queued bridge calls, the plugin log.</summary>
    sealed class ContextHost : ISequenceHost
    {
        readonly IPluginContext ctx;

        public ContextHost(IPluginContext ctx) { this.ctx = ctx; }

        public string Call(string requestJson) => ctx.Call(requestJson);

        public void CallAsync(string requestJson, Action<string> onReply)
        {
            Task<string> task;
            try { task = ctx.CallAsync(requestJson); }
            catch (Exception e)
            {
                onReply(ErrorReply(e));
                return;
            }
            task.ContinueWith(t => onReply(t.Status == TaskStatus.RanToCompletion ? t.Result : ErrorReply(t.Exception?.InnerException)),
                TaskContinuationOptions.ExecuteSynchronously);
        }

        public void Log(string line) => ctx.Log.Warning(line);

        static string ErrorReply(Exception e) =>
            "{\"ok\":false,\"error\":" + Newtonsoft.Json.JsonConvert.ToString(e?.Message ?? "cancelled") + ",\"code\":\"internal\"}";
    }
}
