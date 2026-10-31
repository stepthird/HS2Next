// A bridge batch is one undo entry: package B calls BatchBegin
// before the first member and BatchEnd after the last member that ran, on the main thread, with no other request in
// between. BatchBegin opens one model transaction "Batch" (Source Bridge) when none is open and keeps it in the batch's
// Items["core.tx"]; the members' own transactions join it (CommandBus.BeginOrJoin). BatchEnd commits it when it is
// still open — also when a member failed: the members that succeeded stay applied, as their replies said (open point
// B3). A batch whose members change nothing records nothing; scene.new / scene.load inside a batch close the
// transaction without reverting it (the session resets the bus), so later members record one entry each.
using HS2Next.Api.Bridge;
using HS2Next.Scenes;
using UnityEngine;

namespace HS2Next.App.Commands
{
    public sealed class BatchTransactionHook : IBatchHook
    {
        public const string ItemKey = "core.tx";
        public const string Label = "Batch";

        readonly AppCore app;

        public BatchTransactionHook(AppCore app) { this.app = app; }

        public void BatchBegin(IBatchScope batch)
        {
            var bus = app.Session?.Commands;
            if (bus == null || bus.ActiveTransaction != null) return;
            var tx = bus.Begin(Label, new ExecuteOptions { Source = CommandSource.Bridge, Label = Label });
            if (tx.IsOpen) batch.Items[ItemKey] = tx;
        }

        public void BatchEnd(IBatchScope batch, bool ok, int failedAt)
        {
            if (!batch.Items.TryGetValue(ItemKey, out var o) || !(o is Transaction tx)) return;
            batch.Items.Remove(ItemKey);
            if (!tx.IsOpen) return;
            var r = tx.Commit();
            if (r.Status == CommandStatus.Error)
            {
                // Never leave the bus wedged with an open transaction.
                Debug.LogError("[app] batch transaction commit failed (" + r + "); reverting the batch");
                tx.Cancel();
            }
        }
    }
}
