// The one scene command that changes an item's bake and cut blocks (ItemNodeState.Bake, ItemNodeState.Cut): it replaces both blocks of a node
// together, each with a copy or with none. It holds the before and the after as canonical JSON, so nothing is shared with the model or with the
// caller, applies by reading the after into new blocks and reverts by reading the before; a block in the model is never changed in place by an edit,
// so a new object means a change. One change record NodeChanged(id, "item.bake") and one NodeChanged(id, "item.cut") per apply and revert, for each
// block whose text changed. The bus treats it as a property set: it merges with the next edit of the same node when both carry one merge key and with
// `undo: false` it applies without touching the history. Edits raise the scene's version like any change. History size: twice the JSON length of
// before and after.
using System;
using System.Collections.Generic;
using HS2Next.Save;

namespace HS2Next.Scenes
{
    public sealed class BakeEditCommand : ISceneCommand
    {
        /// <summary>The path of the change record of the bake block.</summary>
        public const string BakePath = "item." + ItemBake.MemberName;
        /// <summary>The path of the change record of the cut block.</summary>
        public const string CutPath = "item." + ItemCut.MemberName;

        // History labels.
        public const string LabelEditCut = "Edit cut";

        /// <summary>The size of an entry without its values.</summary>
        public const long BaseBytes = 256;

        readonly string nodeId;
        readonly string refused;
        string bakeAfter, cutAfter, bakeBefore, cutBefore;
        bool applied;

        /// <summary>
        /// Replaces the blocks of item <paramref name="nodeId"/> with copies of <paramref name="bake"/> and <paramref name="cut"/> as they stand now (null:
        /// the item has none); the caller may go on using its objects. Refused when the node is missing (not_found) or is not an item (wrong_kind), and
        /// (bad_args) when a block cannot be written or when it passes a limit of <see cref="BakeLimits"/> further than the block before it did.
        /// </summary>
        public BakeEditCommand(string nodeId, ItemBake bake, ItemCut cut, string label = LabelEditCut)
        {
            this.nodeId = nodeId;
            Label = label ?? LabelEditCut;
            try
            {
                bakeAfter = bake?.ToJson();
                cutAfter = cut?.ToJson();
            }
            catch (SaveFormatException e) { refused = e.Message; }
        }

        public string Label { get; }
        /// <summary>The item node.</summary>
        public string NodeId => nodeId;
        /// <summary>A new copy of the bake block this edit puts in place; null when it leaves none or cannot be written.</summary>
        public ItemBake Bake => bakeAfter == null ? null : ItemBake.Parse(bakeAfter);
        /// <summary>A new copy of the cut block this edit puts in place; null when it leaves none or cannot be written.</summary>
        public ItemCut Cut => cutAfter == null ? null : ItemCut.Parse(cutAfter);

        static ItemNodeState StateOf(SceneModel m, string id)
        {
            var n = m.Find(id) ?? throw new CommandException(CommandCodes.NotFound, "node " + id + " is not in the scene");
            return n.State as ItemNodeState ?? throw new CommandException(CommandCodes.WrongKind, "node " + id + " is not an item");
        }

        public void Validate(SceneModel m)
        {
            var st = StateOf(m, nodeId);
            if (refused != null) throw new CommandException(CommandCodes.BadArgs, "bake block: " + refused);
            var b = Bake;
            int before = st.Bake?.Data.Count ?? 0;
            if (b != null && b.Data.Count > BakeLimits.MaxData && b.Data.Count > before)
                throw new CommandException(CommandCodes.BadArgs, BakeFindings.Limit + ": " + b.Data.Count + " data entries are over the limit of " + BakeLimits.MaxData);
            var c = Cut;
            int beforeSteps = st.Cut?.Steps.Count ?? 0;
            if (c != null && c.Steps.Count > BakeLimits.MaxSteps && c.Steps.Count > beforeSteps)
                throw new CommandException(CommandCodes.BadArgs, BakeFindings.CutLimit + ": " + c.Steps.Count + " steps are over the limit of " + BakeLimits.MaxSteps);
            int beforeSource = st.Cut?.Source?.Data.Count ?? 0;
            if (c?.Source != null && c.Source.Data.Count > BakeLimits.MaxData && c.Source.Data.Count > beforeSource)
                throw new CommandException(CommandCodes.BadArgs, BakeFindings.Limit + ": the source's " + c.Source.Data.Count + " data entries are over the limit of " + BakeLimits.MaxData);
        }

        public void Apply(SceneModel m, ChangeSink s)
        {
            var st = StateOf(m, nodeId);
            bakeBefore = st.Bake?.ToJson();
            cutBefore = st.Cut?.ToJson();
            applied = true;
            if (bakeBefore != bakeAfter)
            {
                st.Bake = bakeAfter == null ? null : ItemBake.Parse(bakeAfter);
                s.NodeChanged(nodeId, BakePath);
            }
            if (cutBefore != cutAfter)
            {
                st.Cut = cutAfter == null ? null : ItemCut.Parse(cutAfter);
                s.NodeChanged(nodeId, CutPath);
            }
        }

        public void Revert(SceneModel m, ChangeSink s)
        {
            if (!applied) return;
            var st = StateOf(m, nodeId);
            if (bakeBefore != bakeAfter)
            {
                st.Bake = bakeBefore == null ? null : ItemBake.Parse(bakeBefore);
                s.NodeChanged(nodeId, BakePath);
            }
            if (cutBefore != cutAfter)
            {
                st.Cut = cutBefore == null ? null : ItemCut.Parse(cutBefore);
                s.NodeChanged(nodeId, CutPath);
            }
        }

        /// <summary>Merges the next edit of the same node: this entry keeps its before and takes the next one's after.</summary>
        public bool TryMerge(ISceneCommand next)
        {
            if (!(next is BakeEditCommand n) || !string.Equals(n.nodeId, nodeId, StringComparison.Ordinal) || n.refused != null) return false;
            bakeAfter = n.bakeAfter;
            cutAfter = n.cutAfter;
            return true;
        }

        public long EstimatedBytes => BaseBytes + 2L * ((bakeBefore?.Length ?? 0) + (cutBefore?.Length ?? 0) + (bakeAfter?.Length ?? 0) + (cutAfter?.Length ?? 0));

        public IEnumerable<TreeNode> HeldNodes => Array.Empty<TreeNode>();

        public override string ToString() => "bake edit \"" + Label + "\" of " + nodeId;
    }
}
