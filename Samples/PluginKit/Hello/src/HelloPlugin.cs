// Sample "hello" of the HS2Next plugin kit (CC0-1.0: dedicated to the public domain).
// An engine-free plugin: Load registers the command hello.greet and a SceneLoaded handler. The command reads the scene
// through a call helper (ctx.Request) and answers a greeting with the number of objects in the scene.
using System.Collections.Generic;
using HS2Next.Api.Bridge;
using HS2Next.Api.Plugins;

namespace Stp3.Samples.Hello
{
    public sealed class HelloPlugin : IPlugin
    {
        IPluginContext ctx;

        public void Load(IPluginContext context)
        {
            ctx = context;
            ctx.Commands.Register(new CommandSpec("hello.greet", "Greets and counts the scene's objects.")
                .Arg("name", ArgTypes.String, "who to greet", defaultValue: "world")
                .Return("text", ArgTypes.String, "the greeting")
                .Return("objects", ArgTypes.Int, "objects in the scene")
                .WithFlags(CommandFlags.ReadOnly), Greet);
            ctx.Events.SceneLoaded += info => ctx.Log.Info("scene " + info.Kind + " with " + info.Nodes + " nodes");
        }

        object Greet(ICommandContext c)
        {
            // the scene command answers {map, camera, objects: [...]}; Request runs it now and returns its reply
            var scene = ctx.Request("scene");
            int n = scene.Ok ? scene.Get<List<object>>("objects", new List<object>()).Count : 0;
            return new BridgeReply().With("text", "Hello, " + c.Args.Get("name", "world") + "!").With("objects", n);
        }

        // Nothing to undo here: the host removes the command and the event handler itself. A plugin that created objects,
        // started threads or set static fields cleans them up in Unload (the plugin may be enabled again later).
        public void Unload() { }
    }
}
