// A test plugin of the loader (CC0-1.0): Load registers a command, a scene ext block handler and an event handler, then
// throws. The loader must mark the plugin failed and remove what it registered.
using System;
using HS2Next.Api.Bridge;
using HS2Next.Api.Plugins;
using HS2Next.Api.Save;

namespace Stp3.Test.LoadThrows
{
    public sealed class LoadThrowsPlugin : IPlugin
    {
        public void Load(IPluginContext context)
        {
            context.Commands.Register(new CommandSpec("loadthrows.ping", "Registered before Load throws.").WithFlags(CommandFlags.ReadOnly), _ => 1);
            context.Ext.Register(new Block());
            context.Events.SceneLoaded += _ => { };
            throw new InvalidOperationException("load throws on purpose");
        }

        public void Unload() { }

        sealed class Block : IExtBlockHandler
        {
            public string Namespace => "stp3.test.loadthrows";
            public int Version => 1;
            public ExtScope Scopes => ExtScope.Scene;
            public bool RequiredByDefault => false;
            public void Read(IExtReadContext c) { }
            public void Write(IExtWriteContext c) { }
        }
    }
}
