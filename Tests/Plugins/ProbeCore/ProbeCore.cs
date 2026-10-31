// The loader probe that references the app's own assembly (CC0-1.0): such a reference is unsupported (no compatibility
// promise), but it must load and run. It reads the build-hook registry's count and answers it as a command.
using System.Collections.Generic;
using HS2Next.Api.Bridge;
using HS2Next.Api.Plugins;

namespace Stp3.Test.ProbeCore
{
    public sealed class ProbeCorePlugin : IPlugin
    {
        IPluginContext ctx;

        public void Load(IPluginContext context)
        {
            ctx = context;
            context.Commands.Register(new CommandSpec("probecore.hooks", "The number of registered build hooks, read from the app's assembly.")
                .WithFlags(CommandFlags.ReadOnly), c => new Dictionary<string, object>
            {
                ["hooks"] = HS2Next.Chara.Ext.CharaBuildHooks.Registered.Count,
                ["appAssembly"] = typeof(HS2Next.Chara.Ext.CharaBuildHooks).Assembly.GetName().Name,
            });
            context.Log.Info("probecore loaded; build hooks " + HS2Next.Chara.Ext.CharaBuildHooks.Registered.Count);
        }

        public void Unload() => ctx?.Log.Info("probecore unloaded");
    }
}
