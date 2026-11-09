// The one scene command that changes a character's wet block (CharaNodeState.Wet): it replaces the whole block of a node. It holds the
// before and the after as canonical JSON, so nothing is shared with the model or with the caller, applies by reading the after into a
// new block and reverts by reading the before; a block in the model is never changed in place by an edit, so a new object means a
// change. One change record NodeChanged(id, "chara.wet") per apply and revert. The bus treats it as a property set: it merges with the
// next edit of the same node when both carry one merge key (a value dragged) and with `undo: false` it applies without touching the
// history. Every action of a user or a command is one of these, also a start and a dry, so undo brings a dried stain back; the running
// clock is not: it changes runtime values, which the capture writes into the model before such an edit is made. Edits raise the
// scene's version like any change. History size: twice the JSON length of before and after.
using System;
using System.Collections.Generic;
using HS2Next.Save;

namespace HS2Next.Scenes
{
    public sealed class WetEditCommand : ISceneCommand
    {
        /// <summary>The path of the change record.</summary>
        public const string ChangePath = "chara." + CharaWet.MemberName;

        // History labels.
        public const string LabelAdd = "Add stain";
        public const string LabelRemove = "Remove stain";
        public const string LabelEdit = "Stain";
        public const string LabelKeep = "Keep layer";
        public const string LabelStart = "Start stain";
        public const string LabelDry = "Dry stain";
        public const string LabelClearPuddles = "Clear puddles";
        public const string LabelFloor = "Floor";
        public const string LabelOrigin = "Move origin";

        /// <summary>The size of an entry without its values.</summary>
        public const long BaseBytes = 256;

        readonly string nodeId;
        readonly string refused;
        string afterJson, beforeJson;

        /// <summary>
        /// Replaces the block of character <paramref name="nodeId"/> with a copy of <paramref name="after"/> as it stands now (null: an
        /// empty block); the caller may go on using its object. Refused when the node is missing (not_found) or is not a character
        /// (wrong_kind), and (bad_args) when the block cannot be written, when its ids break <see cref="CharaWet.IdProblem"/> or when it
        /// breaks a limit of <see cref="CharaWet.Problem"/> the block before it did not.
        /// </summary>
        public WetEditCommand(string nodeId, CharaWet after, string label = LabelEdit)
        {
            this.nodeId = nodeId;
            Label = label ?? LabelEdit;
            try
            {
                var block = after ?? new CharaWet();
                refused = block.IdProblem();
                afterJson = block.ToJson();
            }
            catch (SaveFormatException e) { refused = e.Message; }
        }

        public string Label { get; }
        /// <summary>The character node.</summary>
        public string NodeId => nodeId;
        /// <summary>A new copy of the block this edit puts in place; null when it cannot be written.</summary>
        public CharaWet After => afterJson == null ? null : CharaWet.Parse(afterJson);

        static CharaNodeState StateOf(SceneModel m, string id)
        {
            var n = m.Find(id) ?? throw new CommandException(CommandCodes.NotFound, "node " + id + " is not in the scene");
            return n.State as CharaNodeState ?? throw new CommandException(CommandCodes.WrongKind, "node " + id + " is not a character");
        }

        public void Validate(SceneModel m)
        {
            var st = StateOf(m, nodeId);
            if (refused != null) throw new CommandException(CommandCodes.BadArgs, "wet block: " + refused);
            string p = CharaWet.Parse(afterJson).Problem(st.Wet);
            if (p != null) throw new CommandException(CommandCodes.BadArgs, "wet block: " + p);
        }

        public void Apply(SceneModel m, ChangeSink s)
        {
            var st = StateOf(m, nodeId);
            beforeJson = st.Wet.ToJson();
            if (beforeJson == afterJson) return;
            st.Wet = CharaWet.Parse(afterJson);
            s.NodeChanged(nodeId, ChangePath);
        }

        public void Revert(SceneModel m, ChangeSink s)
        {
            StateOf(m, nodeId).Wet = CharaWet.Parse(beforeJson);
            s.NodeChanged(nodeId, ChangePath);
        }

        /// <summary>Merges the next edit of the same node: this entry keeps its before and takes the next one's after.</summary>
        public bool TryMerge(ISceneCommand next)
        {
            if (!(next is WetEditCommand n) || !string.Equals(n.nodeId, nodeId, StringComparison.Ordinal)) return false;
            afterJson = n.afterJson;
            return true;
        }

        public long EstimatedBytes => BaseBytes + 2L * ((beforeJson?.Length ?? 0) + (afterJson?.Length ?? 0));

        public IEnumerable<TreeNode> HeldNodes => Array.Empty<TreeNode>();

        public override string ToString() => "wet edit \"" + Label + "\" of " + nodeId;
    }
}
