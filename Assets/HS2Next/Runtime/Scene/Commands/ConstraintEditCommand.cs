// The one scene command that changes the scene's constraints (SceneModel.Constraints): it replaces either the whole list (add, remove,
// clear, move; null or empty = the scene has none) or one entry by id (a switch, a weight, an offset, an end). It holds the before and
// the after of what it replaces, applies by replacing and reverts by restoring; nothing else writes the list, and a list in the model
// is never changed in place, so a reader that holds one sees it unchanged and a new object means a change. One change record
// SceneChanged("constraints") per apply and revert. The bus treats it as a property set: it merges with the next edit of the same kind
// and target when both carry one merge key (a weight dragged, an offset typed twice), and with `undo: false` it applies without touching
// the history. Edits raise the scene's version like any change. History size: twice the JSON length of before and after. Whether an
// edit closes a loop needs the built objects, so that rule is the operation's, not this command's.
using System;
using System.Collections.Generic;
using HS2Next.Save;

namespace HS2Next.Scenes
{
    public sealed class ConstraintEditCommand : ISceneCommand
    {
        /// <summary>The path of the change record (a scene-wide change).</summary>
        public const string ChangePath = "constraints";

        // History labels.
        public const string LabelAdd = "Add constraint";
        public const string LabelEdit = "Constraint";
        public const string LabelRemove = "Remove constraint";
        public const string LabelClear = "Clear constraints";
        public const string LabelReorder = "Reorder constraints";
        public const string LabelTakeOffset = "Take offset";

        /// <summary>The size of an entry without its values.</summary>
        public const long BaseBytes = 256;

        readonly bool entryEdit;
        readonly string entryId;
        ConstraintList afterList, beforeList;
        ConstraintEntry afterEntry, beforeEntry;
        int beforeLength, afterLength;

        ConstraintEditCommand(string label, bool entryEdit, string entryId)
        {
            Label = label ?? LabelEdit;
            this.entryEdit = entryEdit;
            this.entryId = entryId;
        }

        /// <summary>
        /// Replaces the whole list with <paramref name="after"/> (null or empty removes it: the scene then has no constraints). Each entry
        /// is stored in its normal form (<see cref="ConstraintEntry.Normalized"/>); an entry that is already in it and that the list before
        /// holds may be shared, so a move builds a new list of the same objects. Refused (bad_args) when the result breaks a rule of
        /// <see cref="ConstraintList.Problem"/> compared with the list before it (the limit, ids, an entry's own rules).
        /// </summary>
        public static ConstraintEditCommand ReplaceList(ConstraintList after, string label = LabelEdit)
        {
            ConstraintList stored = null;
            if (after != null && after.Count > 0)
            {
                stored = new ConstraintList();
                foreach (var e in after.Entries) stored.Entries.Add(e?.Normalized());
            }
            return new ConstraintEditCommand(label, false, null) { afterList = stored };
        }

        /// <summary>Removes every entry (label «Clear constraints»).</summary>
        public static ConstraintEditCommand Clear() => ReplaceList(null, LabelClear);

        /// <summary>
        /// Replaces the entry whose id is <paramref name="after"/>'s with <paramref name="after"/>, in its normal form. Build it fresh
        /// (<see cref="ConstraintEntry.Clone"/>, then change it). Refused when the list has no such entry (not_found) or when the entry
        /// breaks a rule of <see cref="ConstraintEntry.Problem"/> it did not break before (bad_args).
        /// </summary>
        public static ConstraintEditCommand ReplaceEntry(ConstraintEntry after, string label = LabelEdit)
        {
            if (after == null) throw new ArgumentNullException(nameof(after));
            return new ConstraintEditCommand(label, true, after.Id) { afterEntry = after.Normalized() };
        }

        public string Label { get; }
        /// <summary>True for a one-entry edit, false for a whole-list edit.</summary>
        public bool IsEntryEdit => entryEdit;
        /// <summary>The id of the edited entry (a one-entry edit), else null.</summary>
        public string EntryId => entryId;
        /// <summary>The list a whole-list edit puts in place (null = none); null for an entry edit.</summary>
        public ConstraintList AfterList => afterList;
        /// <summary>The entry a one-entry edit puts in place; null for a list edit.</summary>
        public ConstraintEntry AfterEntry => afterEntry;

        public void Validate(SceneModel m)
        {
            if (!entryEdit)
            {
                string p = afterList?.Problem(m.Constraints);
                if (p != null) throw new CommandException(CommandCodes.BadArgs, "Constraints: " + p);
                return;
            }
            var list = m.Constraints ?? throw new CommandException(CommandCodes.NotFound, "the scene has no constraints");
            var before = list.Find(entryId) ?? throw new CommandException(CommandCodes.NotFound, "no constraint " + entryId);
            string ep = afterEntry.Problem();
            if (ep != null && ep != before.Problem()) throw new CommandException(CommandCodes.BadArgs, "constraint " + entryId + ": " + ep);
        }

        public void Apply(SceneModel m, ChangeSink s)
        {
            if (!entryEdit)
            {
                beforeList = m.Constraints;
                string b = beforeList?.ToJson(), a = afterList?.ToJson();
                beforeLength = b?.Length ?? 0;
                afterLength = a?.Length ?? 0;
                if (b == a) return;
                m.Constraints = afterList;
                s.SceneChanged(ChangePath);
                return;
            }
            var list = m.Constraints;
            int i = list.IndexOf(entryId);
            beforeEntry = list.Entries[i];
            string be = beforeEntry.ToJson(), ae = afterEntry.ToJson();
            beforeLength = be.Length;
            afterLength = ae.Length;
            if (be == ae) return;
            m.Constraints = list.With(i, afterEntry);
            s.SceneChanged(ChangePath);
        }

        public void Revert(SceneModel m, ChangeSink s)
        {
            if (!entryEdit)
            {
                m.Constraints = beforeList;
                s.SceneChanged(ChangePath);
                return;
            }
            // By id, so an edit made with `undo: false` on another entry stays. An entry such an edit removed has nothing to restore.
            var list = m.Constraints;
            int i = list?.IndexOf(entryId) ?? -1;
            if (i < 0) return;
            m.Constraints = list.With(i, beforeEntry);
            s.SceneChanged(ChangePath);
        }

        /// <summary>Merges the next edit of the same kind (a list edit, or an edit of the same entry): this entry keeps its before and
        /// takes the next one's after.</summary>
        public bool TryMerge(ISceneCommand next)
        {
            if (!(next is ConstraintEditCommand n) || n.entryEdit != entryEdit) return false;
            if (entryEdit)
            {
                if (!string.Equals(n.entryId, entryId, StringComparison.Ordinal)) return false;
                afterEntry = n.afterEntry;
            }
            else afterList = n.afterList;
            afterLength = n.afterLength;
            return true;
        }

        public long EstimatedBytes => BaseBytes + 2L * (beforeLength + afterLength);

        public IEnumerable<TreeNode> HeldNodes => Array.Empty<TreeNode>();

        public override string ToString() => "constraint edit \"" + Label + "\"" + (entryEdit ? " entry " + entryId : "");
    }
}
