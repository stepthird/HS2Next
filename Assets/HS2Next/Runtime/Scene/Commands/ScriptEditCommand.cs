// The one scene command that changes the scene's scripts (SceneModel.Scripts) and, when a script is loaded or its edits are written back, the
// scene's Timeline (SceneModel.Timeline) in the same entry. It holds the before and the after of what it replaces, applies by replacing and
// reverts by restoring; nothing else writes the scripts. A list in the model is never changed in place, so callers build fresh objects
// (Clone, With, Adding, Without) and hand them over; the command takes them. One change record SceneChanged("scripts") and, when the
// Timeline changes, SceneChanged("timeline") per apply and revert. The bus treats it as a property set: it merges with the next edit of the
// same kind when both carry one merge key, and with `undo: false` it applies without touching the history. Edits raise the scene's
// version like any change. History size: 256 bytes and twice the JSON length of before and after.
using System;
using System.Collections.Generic;
using HS2Next.Save;

namespace HS2Next.Scenes
{
    public sealed class ScriptEditCommand : ISceneCommand
    {
        /// <summary>The path of the change record of the scripts (a scene-wide change).</summary>
        public const string ChangePath = SceneScriptList.MemberName;
        /// <summary>The path of the change record of the Timeline (the Timeline's own).</summary>
        public const string TimelinePath = TimelineEditCommand.ChangePath;

        // History labels.
        public const string LabelAdd = "Add script";
        public const string LabelEdit = "Edit script";
        public const string LabelRemove = "Remove script";
        public const string LabelCast = "Cast roles";
        public const string LabelLoad = "Load script";
        public const string LabelPrepare = "Prepare script";
        public const string LabelCapture = "Save edits into script";
        public const string LabelBorrow = "Borrow";

        /// <summary>The size of an entry without its values.</summary>
        public const long BaseBytes = 256;

        readonly bool touchesTimeline;
        SceneScriptList afterScripts, beforeScripts;
        TimelineDocument afterTimeline, beforeTimeline;
        long beforeLength, afterLength;
        bool applied;

        ScriptEditCommand(string label, bool touchesTimeline)
        {
            Label = label ?? LabelEdit;
            this.touchesTimeline = touchesTimeline;
        }

        /// <summary>
        /// Replaces the scene's scripts with <paramref name="after"/> (null or empty: the scene has none). The command takes the list; the caller must
        /// not change it or its scripts afterwards. Refused (bad_args) when the list breaks a rule of <see cref="SceneScriptList.Problem"/> compared with
        /// the list before it (the limits, ids, a script's own rules).
        /// </summary>
        public static ScriptEditCommand ReplaceScripts(SceneScriptList after, string label = LabelEdit) =>
            new ScriptEditCommand(label, false) { afterScripts = Normalize(after) };

        /// <summary>
        /// Replaces the scene's scripts with <paramref name="after"/> and the scene's Timeline with <paramref name="timeline"/> (null removes it) in one
        /// entry: a script loaded into the Timeline with its casts written («Load script»), the Timeline's edits written back into its script («Save
        /// edits into script»). The command takes both objects. Refused (bad_args) as <see cref="ReplaceScripts"/> and when the Timeline breaks a rule of
        /// <see cref="TimelineDocument.Problem"/> compared with the Timeline before it, or is the Timeline of a script (role keys).
        /// </summary>
        public static ScriptEditCommand ReplaceScriptsAndTimeline(SceneScriptList after, TimelineDocument timeline, string label = LabelLoad)
        {
            timeline?.Normalize();
            return new ScriptEditCommand(label, true) { afterScripts = Normalize(after), afterTimeline = timeline };
        }

        static SceneScriptList Normalize(SceneScriptList l) => l != null && l.Count > 0 ? l : null;

        public string Label { get; }
        /// <summary>True when the command replaces the Timeline as well.</summary>
        public bool TouchesTimeline => touchesTimeline;
        /// <summary>The list the command puts in place (null = no scripts).</summary>
        public SceneScriptList AfterScripts => afterScripts;
        /// <summary>The Timeline the command puts in place when it touches the Timeline (null = no Timeline).</summary>
        public TimelineDocument AfterTimeline => afterTimeline;

        public void Validate(SceneModel m)
        {
            string p = afterScripts?.Problem(m.Scripts);
            if (p != null) throw new CommandException(CommandCodes.BadArgs, "scripts: " + p);
            if (!touchesTimeline || afterTimeline == null) return;
            if (afterTimeline.RoleKeys) throw new CommandException(CommandCodes.BadArgs, "Timeline: the Timeline of a script (role keys) is not a scene's Timeline");
            string tp = afterTimeline.Problem(m.Timeline);
            if (tp != null) throw new CommandException(CommandCodes.BadArgs, "Timeline: " + tp);
        }

        public void Apply(SceneModel m, ChangeSink s)
        {
            beforeScripts = m.Scripts;
            string bs = beforeScripts?.ToJson(), as_ = afterScripts?.ToJson();
            beforeLength = bs?.Length ?? 0;
            afterLength = as_?.Length ?? 0;
            applied = true;
            if (bs != as_)
            {
                m.Scripts = afterScripts;
                s.SceneChanged(ChangePath);
            }
            if (!touchesTimeline) return;
            beforeTimeline = m.Timeline;
            string bt = beforeTimeline?.ToJson(), at = afterTimeline?.ToJson();
            beforeLength += bt?.Length ?? 0;
            afterLength += at?.Length ?? 0;
            if (bt != at)
            {
                m.Timeline = afterTimeline;
                s.SceneChanged(TimelinePath);
            }
        }

        public void Revert(SceneModel m, ChangeSink s)
        {
            if (!applied) return;
            if (!ReferenceEquals(m.Scripts, beforeScripts))
            {
                m.Scripts = beforeScripts;
                s.SceneChanged(ChangePath);
            }
            if (touchesTimeline && !ReferenceEquals(m.Timeline, beforeTimeline))
            {
                m.Timeline = beforeTimeline;
                s.SceneChanged(TimelinePath);
            }
        }

        /// <summary>Merges the next edit of the same kind (scripts alone, or scripts and Timeline): this entry keeps its before and takes the next one's
        /// after.</summary>
        public bool TryMerge(ISceneCommand next)
        {
            if (!(next is ScriptEditCommand n) || n.touchesTimeline != touchesTimeline) return false;
            afterScripts = n.afterScripts;
            afterTimeline = n.afterTimeline;
            afterLength = n.afterLength;
            return true;
        }

        public long EstimatedBytes => BaseBytes + 2L * (beforeLength + afterLength);

        public IEnumerable<TreeNode> HeldNodes => Array.Empty<TreeNode>();

        public override string ToString() => "script edit \"" + Label + "\"" + (touchesTimeline ? " with the Timeline" : "");
    }
}
