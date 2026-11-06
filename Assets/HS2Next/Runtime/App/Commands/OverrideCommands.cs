// The diagnostic command of the runtime override layer (Phase 6.2; group "timeline", owner core, Since 1.2.0, experimental):
//   overrides.list  every channel with a slot of any owner (the core Timeline, plugins): {key, path, owner, priority, hasValue,
//                   winning, effective, targetExists, value} in the order scene channels, then the nodes in the model's
//                   pre-order (deleted nodes last), then path, then priority. Read-only; nothing of it is saved.
// CoreCommands registers it in the group "timeline" with the Timeline's commands (Register below).
using HS2Next.Api.Bridge;
using HS2Next.Scenes.Overrides;
using Newtonsoft.Json.Linq;

namespace HS2Next.App.Commands
{
    public static class OverrideCommands
    {
        /// <summary>The commands of this file (group "timeline", Since 1.2.0).</summary>
        public static readonly string[] Names = { "overrides.list" };

        /// <summary>CommandSpec.Since of the Phase 6.2 commands.</summary>
        public const string Since62 = "1.2.0";

        public static void Register(ICommandRegistry r, AppCore app)
        {
            CommandKit.Register(r, app, CommandKit.Spec("overrides.list", "The runtime override slots of every owner (the Timeline, plugins): values laid over the scene for " +
                    "display, never saved. Per slot its node, path, owner, priority, whether it has a value, wins, takes effect and its target exists.", "timeline")
                .WithSince(Since62).WithFlags(CommandFlags.ReadOnly | CommandFlags.Experimental)
                .Arg("key", ArgTypes.Node, "only the slots of this node (default: every slot, the scene's camera and depth of field first)")
                .Return("overrides", ArgTypes.ArrayOf(ArgTypes.Object), "{key, path, owner, priority, hasValue, winning, effective, targetExists, value}")
                .Example("{\"cmd\":\"overrides.list\"}"),
                List);
        }

        static object List(CoreContext c)
        {
            var node = CommandKit.OptionalNode(c, "key");
            var arr = new JArray();
            var layer = c.Runtime?.Overrides;
            if (layer != null)
                foreach (var s in layer.Slots(node?.Id))
                    arr.Add(new JObject
                    {
                        ["key"] = s.NodeId, ["path"] = s.Path, ["owner"] = s.Owner, ["priority"] = s.Priority, ["hasValue"] = s.HasValue,
                        ["winning"] = s.IsWinning, ["effective"] = s.IsEffective, ["targetExists"] = s.TargetExists,
                        ["value"] = s.HasValue ? JToken.Parse(s.ValueJson()) : JValue.CreateNull(),
                    });
            return new JObject { ["overrides"] = arr };
        }
    }
}
