using System.Text.Json;
using KeyLoad.Core;

using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueRetryLeaseRenewFirst
{
    internal static async Task RunAsync(DatabaseEngine database, QueueOrderedRetryState state, Delivery original, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var policy = database.Store.Read(view => view.GetRecord<ResourceDefinition>(KeySpace.Resource(
            state.Partition.TenantId, state.Partition.DatabaseId, state.Lane.Queue)))!.QueuePolicy;
        var retry = new DeliveryCommand(Guid.NewGuid(), state.Lane, original.Token, DeliveryAction.Nack);
        var prepared = database.PrepareQueueRetryOperation(database.NormalizeOperation(new(retry.CommandId, OperationKind.Delivery,
            QueueOrderedRetryProtocol.Root, state.Time, JsonSerializer.Serialize(retry, JsonDefaults.Options))));
        var renewal = new DeliveryCommand(Guid.NewGuid(), state.Lane, original.Token, DeliveryAction.Renew, policy.MaxLeaseSeconds);
        state.Execute(database, OperationKind.Delivery, renewal, renewal.CommandId).Get<CommitReceipt>();
        var until = state.Time.AddSeconds(policy.MaxLeaseSeconds);
        var expected = new MessageMetadata(QueueOrderedRetryProtocol.First, MessageState.Leased, QueueOrderedRetryProtocol.One,
            QueueOrderedRetryProtocol.Three, QueueOrderedRetryProtocol.One, null, null, LeaseOwner: QueueOrderedRetryProtocol.Root,
            LeaseVersion: QueueOrderedRetryProtocol.One, LeaseUntil: until,
            EnqueueSequence: QueueOrderedRetryProtocol.One, ActiveOrderSequence: QueueOrderedRetryProtocol.One);
        await QueueOrderedRetryAssertions.MessageAsync(database, state, expected, stored: true);
        await QueueRetryPreparedLeaseRefusal.RunAsync(database, state, prepared);
        state.Time = original.LeaseUntil;
        var before = QueueOrderedRetryImage.Capture(database.Store, state.Lane);
        await Assert.That(QueueOrderedRetryOperations.Receive(database, state).Deliveries).IsEmpty();
        await QueueOrderedRetryImage.SameAsync(database.Store, state.Lane, before);
        await QueueOrderedRetryAssertions.MessageAsync(database, state, expected, stored: true);
        var old = database.Store.Read(view => view.ReadOwnedValue(KeySpace.Partition("lease", state.Partition,
            state.Lane.Queue, original.LeaseUntil, original.Id)));
        var current = database.Store.Read(view => view.GetRecord<string>(KeySpace.Partition("lease", state.Partition,
            state.Lane.Queue, until, original.Id)));
        await Assert.That(old).IsNull();
        await Assert.That(current).IsEqualTo(QueueOrderedRetryProtocol.First);
        QueueOrderedRetryOperations.Complete(database, state, original, DeliveryAction.Ack).Get<CommitReceipt>();
    }
}
