// The history probe (CC0-1.0): a test plugin of the Api 1.1 host points - the request helpers, the plugin's undo entries
// (both forms, merges, a throwing callback, a record refused inside an event handler or an undo callback), a frame action
// scheduled ahead, the capture events (counted; one of its own objects hidden for the render), a no-op build hook, an ext
// block of the scene and the lifecycle of runtime disable / enable (instances, loads, SceneLoaded, AppReady, IsActive).
// Every reply is ids and numbers.
using System;
using System.Collections.Generic;
using HS2Next.Api.Bridge;
using HS2Next.Api.Plugins;
using HS2Next.Api.Save;
using HS2Next.Api.Unity;
using UnityEngine;

namespace Stp3.Test.HistoryProbe
{
    public sealed class HistoryProbePlugin : IPlugin, IPluginSceneLoaded
    {
        /// <summary>Instances created in this process (a runtime enable makes a new one).</summary>
        public static int Instances;
        /// <summary>The loaded instance (null after Unload).</summary>
        public static HistoryProbePlugin Current { get; private set; }

        IPluginContext ctx;
        readonly int instance;
        int value, loads, unloads, appReady, frameRuns, hookRuns, extReads, extWrites;
        int capStarting, capRendered, capFinished, capOrder;
        string capSequence = "";
        CaptureResultInfo lastResult;
        CaptureInfo lastStarting;
        bool hiddenInRender, shownAtRendered;
        string requestInCapture;
        readonly List<string> sceneLoads = new List<string>();
        string armed;
        string armedResult;
        GameObject overlay;

        public HistoryProbePlugin() { instance = ++Instances; }

        public void Load(IPluginContext context)
        {
            ctx = context;
            loads++;
            Current = this;
            var c = ctx.Commands;
            c.Register(new CommandSpec("historyprobe.state", "The probe's counters.").WithFlags(CommandFlags.ReadOnly), State);
            c.Register(new CommandSpec("historyprobe.set", "Sets the probe's value and records one undo entry.")
                .Arg("value", ArgTypes.Int, "the new value", required: true)
                .Arg("form", ArgTypes.Enum("callback", "state", "throw"), "the entry form", defaultValue: "callback")
                .Arg("mergeKey", ArgTypes.String, "the merge key")
                .Arg("label", ArgTypes.String, "the entry label", defaultValue: "Probe value"), Set);
            c.Register(new CommandSpec("historyprobe.arm", "Arms one test action: recordInEvent, recordInUndo, requestInCapture.")
                .Arg("what", ArgTypes.String, "the action", required: true), Arm);
            c.Register(new CommandSpec("historyprobe.schedule", "Schedules a frame action that counts when it runs.")
                .Arg("frames", ArgTypes.Int, "frames ahead", defaultValue: 5), Schedule);
            c.Register(new CommandSpec("historyprobe.self", "Calls plugin.disable on the probe itself from its own command."), Self);
            ctx.Events.AppReady += () => appReady++;
            ctx.Events.NodesAdded += OnNodesAdded;
            ctx.Events.CaptureStarting += OnCaptureStarting;
            ctx.Events.CaptureRendered += OnCaptureRendered;
            ctx.Events.CaptureFinished += OnCaptureFinished;
            ctx.Ext.Register(new ValueBlock(this));
            var u = ctx.Unity();
            if (u != null) u.BuildHooks.Register("noop", BuildStage.Final, a => hookRuns++);
        }

        public void Unload()
        {
            unloads++;
            if (overlay != null) UnityEngine.Object.Destroy(overlay);
            overlay = null;
            if (Current == this) Current = null;
        }

        public void SceneLoaded(SceneLoadInfo info) => sceneLoads.Add(info.Kind + "|" + (info.Path ?? "") + "|" + info.Nodes);

        object State(ICommandContext c) => new BridgeReply()
            .With("instance", instance).With("instances", Instances).With("value", value).With("loads", loads).With("unloads", unloads)
            .With("appReady", appReady).With("sceneLoads", sceneLoads).With("frameRuns", frameRuns).With("hookRuns", hookRuns)
            .With("extReads", extReads).With("extWrites", extWrites).With("history", ctx.History.Count).With("canRecord", ctx.History.CanRecord)
            .With("isActive", ctx.IsActive).With("armed", armed).With("armedResult", armedResult)
            .With("capture", new BridgeReply().With("starting", capStarting).With("rendered", capRendered).With("finished", capFinished)
                .With("sequence", capSequence).With("hiddenInRender", hiddenInRender).With("shownAtRendered", shownAtRendered)
                .With("requestInCapture", requestInCapture)
                .With("id", lastStarting?.Id ?? 0).With("kind", lastStarting?.Kind.ToString()).With("origin", lastStarting?.Origin.ToString())
                .With("width", lastStarting?.Width ?? 0).With("height", lastStarting?.Height ?? 0).With("supersample", lastStarting?.Supersample ?? 0)
                .With("renderWidth", lastStarting?.RenderWidth ?? 0).With("renderHeight", lastStarting?.RenderHeight ?? 0)
                .With("drawsFrame", lastStarting?.DrawsFrame ?? false).With("frameNumber", lastStarting?.FrameNumber ?? 0).With("path", lastStarting?.Path)
                .With("ok", lastResult?.Ok ?? false).With("resultPath", lastResult?.Path).With("bytes", lastResult?.Bytes ?? 0)
                .With("error", lastResult?.Error).With("sameInstance", lastResult != null && ReferenceEquals(lastResult.Capture, lastStarting)));

        object Set(ICommandContext c)
        {
            int to = c.Args.Get("value", 0);
            string form = c.Args.Get("form", "callback");
            string key = c.Args.Get<string>("mergeKey");
            string label = c.Args.Get("label", "Probe value");
            int from = value;
            value = to;
            HistoryRecordResult r;
            if (form == "state")
                r = ctx.History.Record(label, "{\"value\":" + from + "}", "{\"value\":" + to + "}", j => value = ParseValue(j), key);
            else if (form == "throw")
                r = ctx.History.Record(label, () => throw new InvalidOperationException("undo throws on purpose"), () => throw new FormatException("redo throws on purpose"), key);
            else
                r = ctx.History.Record(label, () => { value = from; Undone(); }, () => value = to, key);
            if (r == HistoryRecordResult.Busy || r == HistoryRecordResult.NoScene) value = from;
            return new BridgeReply().With("result", r.ToString()).With("value", value).With("history", ctx.History.Count);
        }

        void Undone()
        {
            if (armed != "recordInUndo") return;
            armed = null;
            armedResult = ctx.History.Record("inside undo", () => { }, () => { }).ToString();
        }

        static int ParseValue(string json)
        {
            int i = json.IndexOf(':');
            int j = json.IndexOf('}');
            return i > 0 && j > i && int.TryParse(json.Substring(i + 1, j - i - 1).Trim(), out int v) ? v : 0;
        }

        object Arm(ICommandContext c)
        {
            armed = c.Args.Get<string>("what");
            armedResult = null;
            requestInCapture = null;
            if (armed == "requestInCapture" && overlay == null)
            {
                overlay = GameObject.CreatePrimitive(PrimitiveType.Cube);
                overlay.name = "historyprobe_overlay";
                overlay.hideFlags = HideFlags.DontSave;
            }
            return new BridgeReply().With("armed", armed);
        }

        void OnNodesAdded(NodeChangeInfo info)
        {
            if (armed != "recordInEvent") return;
            armed = null;
            armedResult = ctx.History.Record("inside an event", () => { }, () => { }).ToString();
        }

        object Schedule(ICommandContext c)
        {
            ctx.Frames.AtUpdate(Math.Max(1, c.Args.Get("frames", 5)), () => frameRuns++);
            return new BridgeReply().With("scheduled", true);
        }

        object Self(ICommandContext c)
        {
            var r = ctx.Request("plugin.disable", new { guid = ctx.Guid });
            return new BridgeReply().With("selfOk", r.Ok).With("selfCode", r.Code).With("isActive", ctx.IsActive);
        }

        void OnCaptureStarting(CaptureInfo i)
        {
            capStarting++;
            capSequence += "S";
            lastStarting = i;
            lastResult = null;
            var r = overlay != null ? overlay.GetComponent<Renderer>() : null;
            if (r != null) r.enabled = false;
            hiddenInRender = r != null && !r.enabled;
            if (armed == "requestInCapture")
            {
                armed = null;
                var reply = ctx.Request("obj.transform", new { key = "none", pos = new[] { 0, 0, 0 } });
                requestInCapture = reply.Ok ? "ok" : reply.Code;
            }
        }

        void OnCaptureRendered(CaptureInfo i)
        {
            capRendered++;
            capSequence += "R";
            var r = overlay != null ? overlay.GetComponent<Renderer>() : null;
            bool wasHidden = r != null && !r.enabled;
            if (r != null) r.enabled = true;
            shownAtRendered = wasHidden && r != null && r.enabled && ReferenceEquals(i, lastStarting);
        }

        void OnCaptureFinished(CaptureResultInfo res)
        {
            capFinished++;
            capSequence += "F";
            capOrder++;
            lastResult = res;
        }

        /// <summary>The probe's value in the scene's ext block (inline JSON {"value": n}).</summary>
        sealed class ValueBlock : IExtBlockHandler
        {
            readonly HistoryProbePlugin p;
            public ValueBlock(HistoryProbePlugin p) { this.p = p; }
            public string Namespace => p.ctx.Guid;
            public int Version => 1;
            public ExtScope Scopes => ExtScope.Scene;
            public bool RequiredByDefault => false;

            public void Read(IExtReadContext c)
            {
                p.extReads++;
                if (c.Json != null) p.value = ParseValue(c.Json);
            }

            public void Write(IExtWriteContext c)
            {
                p.extWrites++;
                if (p.value != 0) c.SetJson("{\"value\":" + p.value + "}");
            }
        }
    }
}
