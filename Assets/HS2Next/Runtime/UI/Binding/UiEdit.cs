// A UI edit transaction: a slider drag, picker drag, numeric label
// drag or a batch of ops = one history entry. Begin opens a bus transaction (BeginOrJoin, Source User); every StudioOps
// call made meanwhile joins it (Preview batches: the inspectors and the viewport follow); Commit records one entry (none
// when nothing changed); Cancel (Esc, lost pointer capture) reverts. One open edit at a time: UiContext.ActiveEdit (the
// shortcut router cancels it on Esc).
using System;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;

namespace HS2Next.UI.Binding
{
    public sealed class UiEdit : IDisposable
    {
        readonly UiContext ctx;
        readonly Transaction tx;

        public string Label { get; }
        public bool IsOpen => tx != null && tx.IsOpen;
        /// <summary>Why Begin failed (`busy`: another transaction is open), else null.</summary>
        public CommandResult Failure => tx?.Failure;
        /// <summary>Raised once when the edit ends (true = committed, false = cancelled).</summary>
        public event Action<bool> Ended;

        UiEdit(UiContext ctx, string label, Transaction tx)
        {
            this.ctx = ctx;
            Label = label;
            this.tx = tx;
        }

        public static UiEdit Begin(UiContext ctx, string label, string mergeKey = null)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (ctx.ActiveEdit != null && ctx.ActiveEdit.IsOpen) ctx.ActiveEdit.Cancel();
            var tx = ctx.Session.Commands.BeginOrJoin(label, UiContext.User(label, mergeKey));
            var e = new UiEdit(ctx, label, tx);
            if (e.IsOpen) ctx.ActiveEdit = e;
            return e;
        }

        /// <summary>Records one entry for everything done since Begin. Returns the bus result (NoOp when nothing changed).</summary>
        public OpResult Commit()
        {
            if (!IsOpen) return OpResult.NothingToDo();
            var r = OpResult.From(tx.Commit());
            ctx.Runtime?.ApplyNow();
            End(true);
            return r;
        }

        /// <summary>Reverts everything done since Begin.</summary>
        public void Cancel()
        {
            if (!IsOpen) { End(false); return; }
            tx.Cancel();
            ctx.Runtime?.ApplyNow();
            End(false);
        }

        void End(bool committed)
        {
            if (ctx.ActiveEdit == this) ctx.ActiveEdit = null;
            var h = Ended;
            Ended = null;
            h?.Invoke(committed);
        }

        public void Dispose() { if (IsOpen) Cancel(); }
    }
}
