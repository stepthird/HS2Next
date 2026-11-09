// The one scene command that changes a character's ragdoll block (CharaNodeState.Ragdoll): it replaces the whole block of a node. It holds the before and the after as canonical
// JSON, so nothing is shared with the model or with the caller, applies by reading the after into a new block and reverts by reading the before; a block in the model is never
// changed in place by an edit, so a new object means a change. One change record NodeChanged(id, "chara.ragdoll") per apply and revert. The bus treats it as a property set: it
// merges with the next edit of the same node when both carry one merge key (a value dragged) and with `undo: false` it applies without touching the history. Every operation of
// a user or a command on the block is one of these. What a running clock does to a body is not: it changes no model value, and the capture writes the settled pose into the model
// before such an edit is made, so an undo brings back what was at rest. Edits raise the scene's version like any change. History size: twice the JSON length of before and after.
using System;
using System.Collections.Generic;
using HS2Next.Save;

namespace HS2Next.Scenes
{
    public sealed class RagdollEditCommand : ISceneCommand
    {
        /// <summary>The path of the change record.</summary>
        public const string ChangePath = "chara." + CharaRagdoll.MemberName;

        // History labels.
        public const string LabelRagdoll = "Ragdoll";
        public const string LabelRelease = "Release ragdoll";
        public const string LabelPin = "Pin";
        public const string LabelUnpin = "Unpin";
        public const string LabelTie = "Tie";
        public const string LabelHold = "Hold";
        public const string LabelSettings = "Ragdoll settings";
        public const string LabelBake = "Bake pose to FK";

        /// <summary>The size of an entry without its values.</summary>
        public const long BaseBytes = 256;

        readonly string nodeId;
        readonly string refused;
        string afterJson, beforeJson;

        /// <summary>
        /// Replaces the block of character <paramref name="nodeId"/> with a copy of <paramref name="after"/> as it stands now (null: an empty block); the caller may go on using its
        /// object. Refused when the node is missing (not_found) or is not a character (wrong_kind), and (bad_args) when the block cannot be written, when its ids break
        /// <see cref="CharaRagdoll.IdProblem"/>, when it breaks a limit of <see cref="CharaRagdoll.Problem"/> the block before it did not, or when it makes the pins or the ties of
        /// the whole scene grow past their limit.
        /// </summary>
        public RagdollEditCommand(string nodeId, CharaRagdoll after, string label = LabelSettings)
        {
            this.nodeId = nodeId;
            Label = label ?? LabelSettings;
            try
            {
                var block = after ?? new CharaRagdoll();
                refused = block.IdProblem();
                afterJson = block.ToJson();
            }
            catch (SaveFormatException e) { refused = e.Message; }
        }

        public string Label { get; }
        /// <summary>The character node.</summary>
        public string NodeId => nodeId;
        /// <summary>A new copy of the block this edit puts in place; null when it cannot be written.</summary>
        public CharaRagdoll After => afterJson == null ? null : CharaRagdoll.Parse(afterJson);

        static CharaNodeState StateOf(SceneModel m, string id)
        {
            var n = m.Find(id) ?? throw new CommandException(CommandCodes.NotFound, "node " + id + " is not in the scene");
            return n.State as CharaNodeState ?? throw new CommandException(CommandCodes.WrongKind, "node " + id + " is not a character");
        }

        public void Validate(SceneModel m)
        {
            var st = StateOf(m, nodeId);
            if (refused != null) throw new CommandException(CommandCodes.BadArgs, "ragdoll block: " + refused);
            var after = CharaRagdoll.Parse(afterJson);
            string p = after.Problem(st.Ragdoll) ?? SceneProblem(m, st.Ragdoll, after);
            if (p != null) throw new CommandException(CommandCodes.BadArgs, "ragdoll block: " + p);
        }

        // The pins and the ties count over the whole scene: the other characters' and this block's.
        static string SceneProblem(SceneModel m, CharaRagdoll before, CharaRagdoll after)
        {
            if (after.Pins.Count <= before.Pins.Count && after.Ties.Count <= before.Ties.Count) return null;
            int pins = after.Pins.Count - before.Pins.Count, ties = after.Ties.Count - before.Ties.Count;
            foreach (var n in m.PreOrder())
            {
                if (!(n.State is CharaNodeState c)) continue;
                pins += c.Ragdoll.Pins.Count;
                ties += c.Ragdoll.Ties.Count;
            }
            if (after.Pins.Count > before.Pins.Count && pins > RagdollLimits.MaxPins) return RagdollFindings.Limit + ": more than " + RagdollLimits.MaxPins + " pins in the scene";
            if (after.Ties.Count > before.Ties.Count && ties > RagdollLimits.MaxTies) return RagdollFindings.Limit + ": more than " + RagdollLimits.MaxTies + " ties in the scene";
            return null;
        }

        public void Apply(SceneModel m, ChangeSink s)
        {
            var st = StateOf(m, nodeId);
            beforeJson = st.Ragdoll.ToJson();
            if (beforeJson == afterJson) return;
            st.Ragdoll = CharaRagdoll.Parse(afterJson);
            s.NodeChanged(nodeId, ChangePath);
        }

        public void Revert(SceneModel m, ChangeSink s)
        {
            StateOf(m, nodeId).Ragdoll = CharaRagdoll.Parse(beforeJson);
            s.NodeChanged(nodeId, ChangePath);
        }

        /// <summary>Merges the next edit of the same node: this entry keeps its before and takes the next one's after.</summary>
        public bool TryMerge(ISceneCommand next)
        {
            if (!(next is RagdollEditCommand n) || !string.Equals(n.nodeId, nodeId, StringComparison.Ordinal)) return false;
            afterJson = n.afterJson;
            return true;
        }

        public long EstimatedBytes => BaseBytes + 2L * ((beforeJson?.Length ?? 0) + (afterJson?.Length ?? 0));

        public IEnumerable<TreeNode> HeldNodes => Array.Empty<TreeNode>();

        public override string ToString() => "ragdoll edit \"" + Label + "\" of " + nodeId;
    }
}
