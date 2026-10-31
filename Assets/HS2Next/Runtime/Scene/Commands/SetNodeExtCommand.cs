// Sets or removes one inline ext value of a node: the only writer of the core
// node-scope blocks that are scene data (hs2.pose, the provenance of the last pose import). Core namespaces only
// (hs2.* and hs2next.*, not hs2.kkex); plugin namespaces keep the rule "ext changes are not undoable".
// Undo restores the previous value; the change is reported as NodeChanged("ext"), which the realization ignores.
using System;
using System.Collections.Generic;
using HS2Next.Save;
using Newtonsoft.Json.Linq;

namespace HS2Next.Scenes
{
    public sealed class SetNodeExtCommand : ISceneCommand
    {
        public const string ChangePath = "ext";

        readonly string id, ns;
        readonly JToken value;
        readonly string parseError;
        JToken old;
        bool changed;

        /// <param name="json">One JSON value, or null to remove the block.</param>
        public SetNodeExtCommand(string nodeId, string ns, string json, string label = "Node ext")
        {
            id = nodeId;
            this.ns = ns;
            Label = label;
            if (json != null)
            {
                value = DocReader.TryParseValue(json, out var err);
                if (value == null) parseError = err ?? "not a JSON value";
            }
        }

        public string Label { get; }

        /// <summary>True for the namespaces this command may write: core (hs2.*, hs2next.*) and not hs2.kkex.</summary>
        public static bool IsCoreNamespace(string ns) =>
            ExtNamespaces.IsValid(ns) && !string.Equals(ns, ExtNamespaces.Kkex, StringComparison.OrdinalIgnoreCase)
            && (ExtNamespaces.IsOwnedBy(ns, "hs2") || ExtNamespaces.IsOwnedBy(ns, "hs2next"))
            && !string.Equals(ns, "hs2", StringComparison.OrdinalIgnoreCase) && !string.Equals(ns, "hs2next", StringComparison.OrdinalIgnoreCase);

        public void Validate(SceneModel m)
        {
            var n = m.Find(id) ?? throw new CommandException(CommandCodes.NotFound, "node " + id + " is not in the scene");
            if (!IsCoreNamespace(ns)) throw new CommandException(CommandCodes.BadArgs, "\"" + ns + "\" is not a core ext namespace (hs2.* or hs2next.*, not hs2.kkex)");
            if (parseError != null) throw new CommandException(CommandCodes.BadArgs, "ext value of " + ns + " is not valid JSON: " + parseError);
            if (value != null && ExtInline.CanonicalSize(value) > ExtInline.MaxBlockBytes)
                throw new CommandException(CommandCodes.BadArgs, "ext value of " + ns + " is over the 1 MiB inline limit");
            foreach (var k in n.Doc.Ext.Namespaces)
                if (k != ns && string.Equals(k, ns, StringComparison.OrdinalIgnoreCase))
                    throw new CommandException(CommandCodes.BadArgs, "ext namespace " + ns + " differs only in case from " + k);
        }

        public void Apply(SceneModel m, ChangeSink s)
        {
            var n = m.Find(id);
            old = n.Doc.Ext.Get(ns);
            changed = !Same(old, value);
            if (!changed) return;
            n.Doc.Ext.Set(ns, value?.DeepClone());
            s.NodeChanged(id, ChangePath);
        }

        public void Revert(SceneModel m, ChangeSink s)
        {
            if (!changed) return;
            var n = m.Find(id);
            n.Doc.Ext.Set(ns, old);
            s.NodeChanged(id, ChangePath);
        }

        static bool Same(JToken a, JToken b)
        {
            if (a == null || b == null) return a == null && b == null;
            return CanonicalJsonWriter.ToText(a) == CanonicalJsonWriter.ToText(b);
        }

        public bool TryMerge(ISceneCommand next) => false;
        public long EstimatedBytes => 128 + ExtInline.CanonicalSize(value) + ExtInline.CanonicalSize(old);
        public IEnumerable<TreeNode> HeldNodes => Array.Empty<TreeNode>();
    }
}
