// The command of a plugin's entry in the scene's undo history (Phase 6.1): the plugin has already changed its own state,
// so the bus records the entry without applying it (CommandBus.RecordExternal). Revert (undo) and Apply (redo) call the
// plugin's callbacks through the runner the host gives (the plugin's fault wrapper) and report no change to the model.
// Two forms: callbacks (undo, redo) or states (before, after and one apply that receives either JSON value). A
// callback that throws is reported by the runner; the command keeps the step's warning for the bus, which moves the
// entry to the other list as usual and never clears the history for it. Merging (same owner and merge key, newest entry
// not sealed, nothing to redo) keeps the first undo or before state and takes the new redo or after state (and the new
// apply). Size estimate: 512 bytes, plus twice the lengths of the two states. Runtime only: never saved. Unity-free.
using System;
using System.Collections.Generic;

namespace HS2Next.Scenes
{
    /// <summary>Runs one callback of an external party; returns the exception it threw (already counted as the party's fault), or null.</summary>
    public delegate Exception ExternalCallRunner(string step, Action callback);

    public sealed class PluginHistoryCommand : ISceneCommand
    {
        /// <summary>The estimate of an entry without states.</summary>
        public const long BaseBytes = 512;

        readonly ExternalCallRunner run;
        Action undo, redo;
        string before, after;
        Action<string> apply;
        string failure;

        /// <summary>The callback form: <paramref name="undo"/> restores the state before the change, <paramref name="redo"/> the state after it.</summary>
        public PluginHistoryCommand(string owner, string label, Action undo, Action redo, ExternalCallRunner run = null)
        {
            Owner = owner ?? throw new ArgumentNullException(nameof(owner));
            Label = label ?? "";
            this.undo = undo ?? throw new ArgumentNullException(nameof(undo));
            this.redo = redo ?? throw new ArgumentNullException(nameof(redo));
            this.run = run;
        }

        /// <summary>The state form: undo calls <paramref name="apply"/> with <paramref name="beforeJson"/>, redo with <paramref name="afterJson"/>.</summary>
        public PluginHistoryCommand(string owner, string label, string beforeJson, string afterJson, Action<string> apply, ExternalCallRunner run = null)
        {
            Owner = owner ?? throw new ArgumentNullException(nameof(owner));
            Label = label ?? "";
            before = beforeJson ?? throw new ArgumentNullException(nameof(beforeJson));
            after = afterJson ?? throw new ArgumentNullException(nameof(afterJson));
            this.apply = apply ?? throw new ArgumentNullException(nameof(apply));
            this.run = run;
        }

        /// <summary>The party that recorded the entry (a plugin's GUID).</summary>
        public string Owner { get; }
        public string Label { get; }
        /// <summary>True for the state form.</summary>
        public bool HasStates => apply != null;
        /// <summary>The state form's before and after values (null for the callback form).</summary>
        public string BeforeJson => before;
        public string AfterJson => after;
        /// <summary>Undo and redo calls so far (each counted whether or not the callback threw).</summary>
        public int Undos { get; private set; }
        public int Redos { get; private set; }

        public void Validate(SceneModel m) { }

        /// <summary>Redo: the redo callback, or apply(after).</summary>
        public void Apply(SceneModel m, ChangeSink s)
        {
            Redos++;
            Call("redo", apply != null ? (Action)(() => apply(after)) : redo);
        }

        /// <summary>Undo: the undo callback, or apply(before).</summary>
        public void Revert(SceneModel m, ChangeSink s)
        {
            Undos++;
            Call("undo", apply != null ? (Action)(() => apply(before)) : undo);
        }

        void Call(string step, Action a)
        {
            Exception e;
            if (run != null) e = run(step, a);
            else
            {
                e = null;
                try { a(); }
                catch (Exception x) { e = x; }
            }
            if (e != null) failure = "plugin " + Owner + ": " + step + " failed (" + e.GetType().Name + ")";
        }

        /// <summary>The warning of the last undo or redo whose callback threw, or null; cleared.</summary>
        public string TakeFailure()
        {
            var f = failure;
            failure = null;
            return f;
        }

        /// <summary>Merges a later entry of the same owner and form: the first undo (or before state) stays, the new redo (or after state and apply) is taken.</summary>
        public bool TryMerge(ISceneCommand next)
        {
            if (!(next is PluginHistoryCommand n) || !string.Equals(n.Owner, Owner, StringComparison.Ordinal) || n.HasStates != HasStates) return false;
            if (HasStates)
            {
                after = n.after;
                apply = n.apply;
            }
            else redo = n.redo;
            return true;
        }

        public long EstimatedBytes => BaseBytes + (HasStates ? 2L * ((before?.Length ?? 0) + (after?.Length ?? 0)) : 0);

        public IEnumerable<TreeNode> HeldNodes => Array.Empty<TreeNode>();

        public override string ToString() => "plugin entry " + Owner + " \"" + Label + "\"";
    }
}
