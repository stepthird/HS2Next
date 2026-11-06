// The one scene command that changes the scene's Timeline (SceneModel.Timeline): it replaces either the whole document
// (build, set, events, clear, a track added or removed; null = the scene has no Timeline) or one track by id (a key or
// track edit). It holds the before and the after of what it replaces, applies by replacing and reverts by restoring;
// nothing else writes the Timeline. One change record SceneChanged("timeline") per apply and revert, which the playback
// and the panel listen to. The bus treats it as a property set: it merges with the next edit of the same kind and target
// when both carry one merge key (a key dragged or a value typed twice), and with `undo: false` it applies without touching
// the history. Edits raise the scene's version like any change. History size: twice the JSON length of before and after.
using System;
using System.Collections.Generic;
using HS2Next.Save;

namespace HS2Next.Scenes
{
    public sealed class TimelineEditCommand : ISceneCommand
    {
        /// <summary>The path of the change record (a scene-wide change).</summary>
        public const string ChangePath = "timeline";

        // History labels.
        public const string LabelTimeline = "Timeline";
        public const string LabelBuild = "Build Timeline";
        public const string LabelEvents = "Timeline events";
        public const string LabelClear = "Clear Timeline";
        public const string LabelAddTrack = "Add track";
        public const string LabelRemoveTrack = "Remove track";
        public const string LabelTrack = "Track";
        public const string LabelKey = "Key";
        public const string LabelDeleteKeys = "Delete keys";
        public const string LabelMoveKeys = "Move keys";
        public const string LabelBake = "Bake Timeline pose";

        /// <summary>The size of an entry without its values.</summary>
        public const long BaseBytes = 256;

        readonly bool trackEdit;
        readonly string trackId;
        TimelineDocument afterDoc, beforeDoc;
        TimelineTrack afterTrack, beforeTrack;
        int beforeLength, afterLength;

        TimelineEditCommand(string label, bool trackEdit, string trackId)
        {
            Label = label ?? (trackEdit ? LabelTrack : LabelTimeline);
            this.trackEdit = trackEdit;
            this.trackId = trackId;
        }

        /// <summary>
        /// Replaces the whole Timeline with <paramref name="after"/> (null removes it: the scene then has no Timeline). The
        /// command takes the object (its keys and events are sorted by time); the caller must not change it afterwards.
        /// Refused (bad_args) when the result breaks a rule of <see cref="TimelineDocument.Problem"/> compared with the
        /// Timeline before it (limits, ids, keys at one time, a second track on a channel).
        /// </summary>
        public static TimelineEditCommand ReplaceDocument(TimelineDocument after, string label = LabelTimeline)
        {
            after?.Normalize();
            return new TimelineEditCommand(label, false, null) { afterDoc = after };
        }

        /// <summary>Removes the Timeline (label «Clear Timeline»).</summary>
        public static TimelineEditCommand Clear() => ReplaceDocument(null, LabelClear);

        /// <summary>
        /// Replaces the track whose id is <paramref name="after"/>'s with <paramref name="after"/> (its keys sorted by time).
        /// The command takes the object. Refused when the scene has no Timeline or no such track (not_found), when the track
        /// breaks a rule of <see cref="TimelineTrack.Problem"/>, when the keys of every track would exceed the limit, or when
        /// the track moves onto a channel another track drives (bad_args).
        /// </summary>
        public static TimelineEditCommand ReplaceTrack(TimelineTrack after, string label = LabelTrack)
        {
            if (after == null) throw new ArgumentNullException(nameof(after));
            after.SortKeys();
            return new TimelineEditCommand(label, true, after.Id) { afterTrack = after };
        }

        public string Label { get; }
        /// <summary>True for a one-track edit, false for a whole-document edit.</summary>
        public bool IsTrackEdit => trackEdit;
        /// <summary>The id of the edited track (a one-track edit), else null.</summary>
        public string TrackId => trackId;
        /// <summary>The document a whole-document edit puts in place (null = no Timeline); null for a track edit.</summary>
        public TimelineDocument AfterDocument => afterDoc;
        /// <summary>The track a one-track edit puts in place; null for a document edit.</summary>
        public TimelineTrack AfterTrack => afterTrack;

        public void Validate(SceneModel m)
        {
            if (!trackEdit)
            {
                string p = afterDoc?.Problem(m.Timeline);
                if (p != null) throw new CommandException(CommandCodes.BadArgs, "Timeline: " + p);
                return;
            }
            var doc = m.Timeline ?? throw new CommandException(CommandCodes.NotFound, "the scene has no Timeline");
            int i = doc.IndexOfTrack(trackId);
            if (i < 0) throw new CommandException(CommandCodes.NotFound, "the Timeline has no track " + trackId);
            string tp = afterTrack.Problem();
            if (tp != null) throw new CommandException(CommandCodes.BadArgs, "track " + trackId + ": " + tp);
            var before = doc.Tracks[i];
            int keys = doc.KeyCount - (before?.Keys?.Count ?? 0) + afterTrack.Keys.Count;
            if (keys > TimelineLimits.MaxKeys) throw new CommandException(CommandCodes.BadArgs, "more than " + TimelineLimits.MaxKeys + " keys in all");
            string ch = afterTrack.ChannelKey;
            if (ch != null && ch != before?.ChannelKey)
                for (int k = 0; k < doc.Tracks.Count; k++)
                    if (k != i && doc.Tracks[k]?.ChannelKey == ch)
                        throw new CommandException(CommandCodes.BadArgs, "the channel has a track (" + doc.Tracks[k].Describe() + ")");
        }

        public void Apply(SceneModel m, ChangeSink s)
        {
            if (!trackEdit)
            {
                beforeDoc = m.Timeline;
                string b = beforeDoc?.ToJson(), a = afterDoc?.ToJson();
                beforeLength = b?.Length ?? 0;
                afterLength = a?.Length ?? 0;
                if (b == a) return;
                m.Timeline = afterDoc;
                s.SceneChanged(ChangePath);
                return;
            }
            var doc = m.Timeline;
            int i = doc.IndexOfTrack(trackId);
            beforeTrack = doc.Tracks[i];
            string bt = beforeTrack.ToJson(), at = afterTrack.ToJson();
            beforeLength = bt.Length;
            afterLength = at.Length;
            if (bt == at) return;
            doc.Tracks[i] = afterTrack;
            s.SceneChanged(ChangePath);
        }

        public void Revert(SceneModel m, ChangeSink s)
        {
            if (!trackEdit)
            {
                m.Timeline = beforeDoc;
                s.SceneChanged(ChangePath);
                return;
            }
            var doc = m.Timeline;
            int i = doc?.IndexOfTrack(trackId) ?? -1;
            if (i < 0) throw new InvalidOperationException("Timeline edit revert: track " + trackId + " is not in the Timeline");
            doc.Tracks[i] = beforeTrack;
            s.SceneChanged(ChangePath);
        }

        /// <summary>Merges the next edit of the same kind (a document edit, or an edit of the same track): this entry keeps
        /// its before and takes the next one's after.</summary>
        public bool TryMerge(ISceneCommand next)
        {
            if (!(next is TimelineEditCommand n) || n.trackEdit != trackEdit) return false;
            if (trackEdit)
            {
                if (!string.Equals(n.trackId, trackId, StringComparison.Ordinal)) return false;
                afterTrack = n.afterTrack;
            }
            else afterDoc = n.afterDoc;
            afterLength = n.afterLength;
            return true;
        }

        public long EstimatedBytes => BaseBytes + 2L * (beforeLength + afterLength);

        public IEnumerable<TreeNode> HeldNodes => Array.Empty<TreeNode>();

        public override string ToString() => "timeline edit \"" + Label + "\"" + (trackEdit ? " track " + trackId : "");
    }
}
