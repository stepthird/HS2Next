// A test plugin of the loader (CC0-1.0): it keeps a number in a scene ext block of its namespace and, once asked, throws
// in every Update so that the host marks it faulted. Its lifecycle calls go to the shared test log (TestLog).
using System;
using System.Collections.Generic;
using HS2Next.Api.Bridge;
using HS2Next.Api.Plugins;
using HS2Next.Api.Save;
using Newtonsoft.Json.Linq;

namespace Stp3.Test.Faulty
{
    public sealed class FaultyPlugin : IPlugin, IPluginUpdate, IPluginSceneLoaded
    {
        IPluginContext ctx;
        bool throwing;
        internal int? Value;
        internal string ReadJson;

        public void Load(IPluginContext context)
        {
            ctx = context;
            TestLog.Add("load " + context.Guid);
            context.Ext.Register(new Block(this));
            context.Commands.Register(new CommandSpec("faulty.ok", "Answers when the plugin runs.").WithFlags(CommandFlags.ReadOnly), _ => new Dictionary<string, object> { ["running"] = true });
            context.Commands.Register(new CommandSpec("faulty.set", "Sets the number stored in the scene's ext block.").Arg("v", ArgTypes.Int, "the number", required: true),
                c => { Value = c.Args.Require<int>("v"); return new Dictionary<string, object> { ["v"] = Value }; });
            context.Commands.Register(new CommandSpec("faulty.get", "The number and the block text read at the last load.").WithFlags(CommandFlags.ReadOnly),
                _ => new Dictionary<string, object> { ["v"] = Value, ["read"] = ReadJson });
            context.Commands.Register(new CommandSpec("faulty.start", "Makes every later Update throw."), _ => { throwing = true; return new Dictionary<string, object> { ["throwing"] = true }; });
        }

        public void Unload() => TestLog.Add("unload " + ctx?.Guid);

        public void Update(in PluginFrame frame)
        {
            if (throwing) throw new InvalidOperationException("faulty update " + frame.Frame);
        }

        public void SceneLoaded(SceneLoadInfo info) => TestLog.Add("sceneLoaded " + ctx.Guid + " " + info.Kind);

        sealed class Block : IExtBlockHandler
        {
            readonly FaultyPlugin p;
            public Block(FaultyPlugin p) { this.p = p; }
            public string Namespace => "stp3.test.faulty";
            public int Version => 1;
            public ExtScope Scopes => ExtScope.Scene;
            public bool RequiredByDefault => false;

            public void Read(IExtReadContext c)
            {
                p.ReadJson = c.Json;
                p.Value = c.Json != null ? (int?)JObject.Parse(c.Json)["v"] : null;
            }

            public void Write(IExtWriteContext c)
            {
                if (p.Value != null) c.SetJson("{\"v\":" + p.Value.Value + "}");
            }
        }
    }

    /// <summary>The lifecycle log the loader tests read (one list per process, shared by the test plugins).</summary>
    static class TestLog
    {
        public static void Add(string line)
        {
            var l = AppDomain.CurrentDomain.GetData("stp3.test.lifecycle") as List<string>;
            if (l == null) AppDomain.CurrentDomain.SetData("stp3.test.lifecycle", l = new List<string>());
            lock (l) l.Add(line);
        }
    }
}
