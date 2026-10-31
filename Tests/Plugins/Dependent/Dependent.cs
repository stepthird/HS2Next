// A test plugin of the loader (CC0-1.0): it depends on the probe plugin and asks to load after the faulty one, so the
// load order and the reverse unload order show in the shared test log.
using System;
using System.Collections.Generic;
using HS2Next.Api.Bridge;
using HS2Next.Api.Plugins;

namespace Stp3.Test.Dependent
{
    public sealed class DependentPlugin : IPlugin, IPluginSceneLoaded
    {
        IPluginContext ctx;

        public void Load(IPluginContext context)
        {
            ctx = context;
            Log("load " + context.Guid);
            context.Commands.Register(new CommandSpec("dependent.ping", "Answers with the probe's presence.").WithFlags(CommandFlags.ReadOnly),
                _ => new Dictionary<string, object> { ["probe"] = context.Commands.Contains("probe.ping") });
        }

        public void Unload() => Log("unload " + ctx?.Guid);

        public void SceneLoaded(SceneLoadInfo info) => Log("sceneLoaded " + ctx.Guid + " " + info.Kind);

        static void Log(string line)
        {
            var l = AppDomain.CurrentDomain.GetData("stp3.test.lifecycle") as List<string>;
            if (l == null) AppDomain.CurrentDomain.SetData("stp3.test.lifecycle", l = new List<string>());
            lock (l) l.Add(line);
        }
    }
}
