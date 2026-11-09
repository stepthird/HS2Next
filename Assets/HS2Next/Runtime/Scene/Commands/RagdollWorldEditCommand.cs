// The one scene command that changes the scene's ragdoll block (SceneModel.Ragdoll): it replaces the whole block, and an empty one leaves the scene with none. It holds the before and
// the after as canonical JSON, so nothing is shared with the model or with the caller; a block in the model is never changed in place, so a new object means a change. One change
// record SceneChanged("ragdoll") per apply and revert. The bus treats it as a property set: it merges with the next such edit when both carry one merge key (a value dragged) and
// with `undo: false` it applies without touching the history. Edits raise the scene's version like any change. History size: twice the JSON length of before and after.
using System;
using System.Collections.Generic;
using HS2Next.Save;

namespace HS2Next.Scenes
{
    public sealed class RagdollWorldEditCommand : ISceneCommand
    {
        /// <summary>The path of the change record (a scene-wide change).</summary>
        public const string ChangePath = SceneRagdollWorld.MemberName;

        // History labels.
        public const string LabelSettings = RagdollEditCommand.LabelSettings;
        public const string LabelPusher = "Pusher";
        public const string LabelObstacle = "Obstacle";

        /// <summary>The size of an entry without its values.</summary>
        public const long BaseBytes = 256;

        readonly string refused;
        string afterJson, beforeJson;

        /// <summary>
        /// Replaces the scene's block with a copy of <paramref name="after"/> as it stands now (null or an empty block: the scene has none); the caller may go on using its object.
        /// Refused (bad_args) when the block cannot be written, when its ids break <see cref="SceneRagdollWorld.IdProblem"/> or when it breaks a limit of
        /// <see cref="SceneRagdollWorld.Problem"/> the block before it did not.
        /// </summary>
        public RagdollWorldEditCommand(SceneRagdollWorld after, string label = LabelSettings)
        {
            Label = label ?? LabelSettings;
            try
            {
                if (after == null) return;
                refused = after.IdProblem();
                if (!after.IsEmpty) afterJson = after.ToJson();
            }
            catch (SaveFormatException e) { refused = e.Message; }
        }

        public string Label { get; }
        /// <summary>A new copy of the block this edit puts in place; null when it leaves the scene with none.</summary>
        public SceneRagdollWorld After => Read(afterJson);

        static SceneRagdollWorld Read(string json) => json == null ? null : SceneRagdollWorld.Parse(json);

        public void Validate(SceneModel m)
        {
            if (refused != null) throw new CommandException(CommandCodes.BadArgs, "ragdoll block: " + refused);
            string p = Read(afterJson)?.Problem(m.Ragdoll);
            if (p != null) throw new CommandException(CommandCodes.BadArgs, "ragdoll block: " + p);
        }

        public void Apply(SceneModel m, ChangeSink s)
        {
            beforeJson = m.Ragdoll?.ToJson();
            if (beforeJson == afterJson) return;
            m.Ragdoll = Read(afterJson);
            s.SceneChanged(ChangePath);
        }

        public void Revert(SceneModel m, ChangeSink s)
        {
            m.Ragdoll = Read(beforeJson);
            s.SceneChanged(ChangePath);
        }

        /// <summary>Merges the next edit: this entry keeps its before and takes the next one's after.</summary>
        public bool TryMerge(ISceneCommand next)
        {
            if (!(next is RagdollWorldEditCommand n)) return false;
            afterJson = n.afterJson;
            return true;
        }

        public long EstimatedBytes => BaseBytes + 2L * ((beforeJson?.Length ?? 0) + (afterJson?.Length ?? 0));

        public IEnumerable<TreeNode> HeldNodes => Array.Empty<TreeNode>();

        public override string ToString() => "ragdoll world edit \"" + Label + "\"";
    }
}
