// A bus transaction: steps apply at once (events with origin Preview, not
// recorded); Commit records one composite entry (property sets on the same target and path collapse to first
// old → last new); Cancel, or Dispose without Commit, reverts every step in reverse order (origin Cancel).
// Collapsing never crosses a barrier: a step that is not a property set, or a set on an overlapping path of
// the same target, keeps the later set as its own step. Commit delivers one batch (origin Command) with the
// field changes of the recorded steps; the structural changes were already delivered as Preview.
// CommandBus.BeginOrJoin hands out joined handles of the open transaction: their steps
// are steps of the open one, their Commit records nothing (the outer Commit records one entry for everything),
// their Cancel reverts and removes only the steps added through them. A join is a collapse barrier: no step added
// after it merges into a step before it, so its revert is exact. Joins nest; closing the outer closes them all.
using System;
using System.Collections.Generic;

namespace HS2Next.Scenes
{
    public sealed class Transaction : IDisposable
    {
        readonly CommandBus bus;
        readonly List<ISceneCommand> steps = new List<ISceneCommand>();
        readonly List<SceneChange> fieldChanges = new List<SceneChange>();
        string[] selectionBefore;
        bool open;
        // Joined handles: the open transaction they joined, and its step / field-change counts at the join.
        readonly Transaction root;
        int joinStart, joinFieldStart;
        // Root transactions: the open joins, innermost last (the last one's start is the collapse barrier).
        readonly List<Transaction> joins = new List<Transaction>();

        internal Transaction(CommandBus bus, string label, ExecuteOptions options, CommandResult failure)
        {
            this.bus = bus;
            Label = label ?? "Edit";
            Options = options;
            Failure = failure;
        }

        Transaction(Transaction root, string label, ExecuteOptions options)
        {
            bus = root.bus;
            this.root = root;
            Label = label ?? "Edit";
            Options = options;
        }

        public string Label { get; }
        public ExecuteOptions Options { get; }
        /// <summary>True until Commit or Cancel (false at once when Begin failed; for a joined handle also once the
        /// transaction it joined closed).</summary>
        public bool IsOpen
        {
            get => open && (root == null || root.open);
            internal set => open = value;
        }
        /// <summary>Why Begin failed (`busy`), else null.</summary>
        public CommandResult Failure { get; }
        /// <summary>True for a handle of <see cref="CommandBus.BeginOrJoin"/> that joined an open transaction.</summary>
        public bool IsJoined => root != null;
        /// <summary>Steps applied so far (after collapsing); for a joined handle the steps added through it (and
        /// through joins nested in it).</summary>
        public int StepCount => root == null ? steps.Count : IsOpen ? root.steps.Count - joinStart : 0;

        internal void Open(string[] selection)
        {
            selectionBefore = selection;
            IsOpen = true;
        }

        /// <summary>A joined handle of this (open, root) transaction; pushes a collapse barrier.</summary>
        internal Transaction Join(string label, ExecuteOptions options)
        {
            var j = new Transaction(this, label, options)
            {
                joinStart = steps.Count, joinFieldStart = fieldChanges.Count, selectionBefore = bus.SelectionSnapshot(),
            };
            j.open = true;
            joins.Add(j);
            return j;
        }

        /// <summary>Closes join <paramref name="j"/> and every join nested in it (their steps stay).</summary>
        void PopJoin(Transaction j)
        {
            int k = joins.IndexOf(j);
            if (k < 0) { j.open = false; return; }
            for (int i = joins.Count - 1; i >= k; i--)
            {
                joins[i].open = false;
                joins.RemoveAt(i);
            }
        }

        void CloseJoins()
        {
            foreach (var j in joins) j.open = false;
            joins.Clear();
        }

        CommandResult Closed() => Failure ?? CommandResult.Error(CommandCodes.Busy, "the transaction is closed");

        /// <summary>Sets a field inside the transaction (view-state paths apply at once and are never part of it).</summary>
        public CommandResult Set(string nodeId, string path, object value) => Execute(bus.MakeSet(nodeId, path, value));

        /// <summary>Executes any command inside the transaction.</summary>
        public CommandResult Execute(ISceneCommand cmd)
        {
            if (!IsOpen) return Closed();
            return bus.Execute(cmd, new ExecuteOptions { Record = HistoryMode.Record, Source = Options.Source });
        }

        internal bool Holds(TreeNode n)
        {
            foreach (var s in steps)
                foreach (var h in s.HeldNodes)
                    if (h == n) return true;
            return false;
        }

        /// <summary>One step (called by the bus for recorded commands while this transaction is open).</summary>
        internal CommandResult ExecuteStep(ISceneCommand cmd)
        {
            var selBefore = bus.SelectionSnapshot();
            var sink = new ChangeSink();
            if (!bus.TryApply(cmd, sink, out var error)) return error;
            if (sink.Count == 0) return CommandResult.NoOp(cmd);
            bus.Version++;
            Collapse(cmd);
            foreach (var c in sink.Changes)
                if (c.Kind == SceneChangeKind.NodeChanged || c.Kind == SceneChangeKind.SceneChanged || c.Kind == SceneChangeKind.AppearanceChanged)
                    fieldChanges.Add(c);
            var newIds = bus.SelectAfter(cmd, Options.Source);
            bus.Deliver(ChangeOrigin.Preview, Options.Source, sink.Changes, selBefore, bus.SelectionSnapshot(), null, false);
            return CommandResult.Success(cmd, newIds);
        }

        void Collapse(ISceneCommand cmd)
        {
            if (cmd is SetPropertyCommand set)
            {
                int barrier = joins.Count > 0 ? joins[joins.Count - 1].joinStart : 0;
                for (int i = steps.Count - 1; i >= barrier; i--)
                {
                    if (!(steps[i] is SetPropertyCommand prev)) break;
                    if (!string.Equals(prev.NodeId, set.NodeId, StringComparison.Ordinal)) continue;
                    if (prev.PathText == set.PathText)
                    {
                        if (prev.TryMerge(set)) return;
                        break;
                    }
                    if (FieldPath.Overlaps(prev.PathText, set.PathText)) break;
                }
            }
            steps.Add(cmd);
        }

        /// <summary>Records the steps as one history entry (nothing when their net effect is nothing). A joined handle
        /// records nothing: its steps stay part of the transaction it joined, and the result is Success.</summary>
        public CommandResult Commit()
        {
            if (!IsOpen) return Closed();
            bus.CheckThreadFor(this);
            if (bus.Busy) return CommandResult.Error(CommandCodes.Busy, "the bus is busy");
            if (root != null)
            {
                var mine = root.steps.GetRange(joinStart, root.steps.Count - joinStart);
                root.PopJoin(this);
                return CommandResult.Success(new CompositeCommand(Label, mine));
            }
            IsOpen = false;
            CloseJoins();
            bus.ActiveTransaction = null;
            var kept = new List<ISceneCommand>();
            var held = new List<TreeNode>();
            foreach (var s in steps)
            {
                held.AddRange(s.HeldNodes);
                if (s is SetPropertyCommand p && p.NetNoOp) continue;
                kept.Add(s);
            }
            var forgotten = new List<TreeNode>();
            if (kept.Count == 0)
            {
                bus.Unheld(held, forgotten);
                bus.Deliver(ChangeOrigin.Command, Options.Source, null, null, null, forgotten, false);
                return CommandResult.NoOp(null);
            }
            var comp = new CompositeCommand(Label, kept);
            bool historyChanged = false;
            if (Options.Record == HistoryMode.Record)
            {
                bus.Push(new HistoryEntry
                {
                    Command = comp, Label = Options.Label ?? Label, Source = Options.Source, Sealed = true,
                    SelectionBefore = selectionBefore, SelectionAfter = bus.SelectionSnapshot(),
                }, forgotten);
                historyChanged = true;
            }
            else if (!CommandBus.IsPropertyOnly(comp))
            {
                historyChanged = bus.History.Undo.Count + bus.History.Redo.Count > 0;
                bus.History.ClearAll(forgotten);
            }
            else bus.SealTop();
            bus.Unheld(held, forgotten);
            bus.Deliver(ChangeOrigin.Command, Options.Source, Dedupe(fieldChanges), null, null, forgotten, historyChanged);
            return CommandResult.Success(comp);
        }

        static List<SceneChange> Dedupe(List<SceneChange> changes)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var r = new List<SceneChange>();
            foreach (var c in changes)
                if (seen.Add((int)c.Kind + "\u0000" + c.NodeId + "\u0000" + c.Path)) r.Add(c);
            return r;
        }

        /// <summary>Reverts every step in reverse order (a joined handle: only the steps added through it).</summary>
        public void Cancel()
        {
            if (!IsOpen) return;
            bus.CheckThreadFor(this);
            if (root != null)
            {
                root.RevertJoin(this);
                return;
            }
            IsOpen = false;
            CloseJoins();
            bus.ActiveTransaction = null;
            var sink = new ChangeSink();
            var held = new List<TreeNode>();
            foreach (var s in steps) held.AddRange(s.HeldNodes);
            var selBefore = bus.SelectionSnapshot();
            try
            {
                bus.RevertSteps(steps, sink);
            }
            catch (Exception e)
            {
                bus.Internal(null, e);
                return;
            }
            if (steps.Count > 0) bus.Version++;
            bus.SetSelectionSilently(selectionBefore);
            var forgotten = new List<TreeNode>();
            bus.Unheld(held, forgotten);
            bus.Deliver(ChangeOrigin.Cancel, Options.Source, sink.Changes, selBefore, bus.SelectionSnapshot(), forgotten, false);
            steps.Clear();
        }

        /// <summary>Reverts and removes the steps of join <paramref name="j"/> (origin Cancel); the earlier steps stay.</summary>
        void RevertJoin(Transaction j)
        {
            PopJoin(j);
            int n = steps.Count - j.joinStart;
            var mine = steps.GetRange(j.joinStart, n);
            steps.RemoveRange(j.joinStart, n);
            if (fieldChanges.Count > j.joinFieldStart) fieldChanges.RemoveRange(j.joinFieldStart, fieldChanges.Count - j.joinFieldStart);
            var sink = new ChangeSink();
            var held = new List<TreeNode>();
            foreach (var s in mine) held.AddRange(s.HeldNodes);
            var selBefore = bus.SelectionSnapshot();
            try
            {
                bus.RevertSteps(mine, sink);
            }
            catch (Exception e)
            {
                bus.Internal(null, e);
                return;
            }
            if (n > 0) bus.Version++;
            bus.SetSelectionSilently(j.selectionBefore);
            var forgotten = new List<TreeNode>();
            bus.Unheld(held, forgotten);
            bus.Deliver(ChangeOrigin.Cancel, Options.Source, sink.Changes, selBefore, bus.SelectionSnapshot(), forgotten, false);
        }

        /// <summary>Closes the transaction without reverting (the model was replaced by New/Load/Bind, or an internal failure).</summary>
        internal void Abort()
        {
            IsOpen = false;
            CloseJoins();
            steps.Clear();
            if (bus.ActiveTransaction == this) bus.ActiveTransaction = null;
        }

        /// <summary>Cancels when still open.</summary>
        public void Dispose()
        {
            if (IsOpen) Cancel();
        }
    }
}
