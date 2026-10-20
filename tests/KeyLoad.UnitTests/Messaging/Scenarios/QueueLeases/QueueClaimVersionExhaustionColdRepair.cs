using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueClaimVersionExhaustionColdRepair
{
    internal static async Task RunAsync(DatabaseEngine database, ZoneTreeStore store, string root,
        QueueClaimVersionExhaustionState state, Result<object> failed, byte[] identity, CancellationToken token)
    {
        await QueueClaimVersionExhaustionAssertions.ReadyAsync(database, store, state);
        await QueueClaimVersionExhaustionAssertions.ReplayAsync(database, store, state, failed);
        store.Commit((tx, _) =>
        {
            tx.Put(state.MetadataKey, state.OriginalMetadata);
            return true;
        });
        await Assert.That(database.InspectMessage(QueueClaimVersionExhaustionProtocol.Root, state.Lane, state.Body.Id))
            .IsEqualTo(new MessageInspection(new(state.Body.Id, MessageState.Ready, 0, 1, 1, null, null), state.Body.PayloadJson, state.Body.HeadersJson));
        await QueueClaimVersionExhaustionAssertions.ReplayAsync(database, store, state, failed);
        var fresh = new ReceiveRequest(Guid.NewGuid(), state.Lane, MaxMessages: 1, LeaseSeconds: QueueClaimVersionExhaustionProtocol.LeaseSeconds);
        var operation = database.OwnCommand(QueueLeaseAuthoritySetup.Create(database, OperationKind.Receive, fresh, fresh.RequestId, state.Time));
        var result = database.Apply(operation);
        var delivery = await Assert.That(result.Get<ReceiveResult>().Deliveries).HasSingleItem();
        await QueueClaimVersionExhaustionAssertions.EqualAsync(delivery, new Delivery(state.Body.Id,
            state.Body.PayloadJson, state.Body.HeadersJson, delivery.Token, 1, state.Time.AddSeconds(QueueClaimVersionExhaustionProtocol.LeaseSeconds), 1, 1));
        await Assert.That(delivery.Token).IsNotEqualTo(string.Empty);
        await Assert.That(database.InspectMessage(QueueClaimVersionExhaustionProtocol.Root, state.Lane, state.Body.Id))
            .IsEqualTo(new MessageInspection(new(state.Body.Id, MessageState.Leased, 1, 2, 1, null, null,
                QueueClaimVersionExhaustionProtocol.Root, 1, delivery.LeaseUntil), state.Body.PayloadJson, state.Body.HeadersJson));
        await QueueClaimVersionExhaustionAssertions.EqualAsync(database.Apply(operation), result);
        await QueueClaimVersionExhaustionAssertions.ReplayAsync(database, store, state, failed);
        await QueueReadyAuthorityColdContinuation.RunAsync(database, store, root, state.Lane,
            result.Get<ReceiveResult>(), false, operation, state.Producer, state.Produced, identity, token);
        using var final = new ZoneTreeStore(new(root), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var completed = QueueWholeFlowStorage.Open(final);
        await QueueClaimVersionExhaustionAssertions.ReplayAsync(completed, final, state, failed);
        await Assert.That(completed.InspectMessage(QueueClaimVersionExhaustionProtocol.Root, state.Lane, state.Body.Id))
            .IsEqualTo(new MessageInspection(new(state.Body.Id, MessageState.Acked, 1, 3, 1, null, null, LeaseVersion: 1), null, null));
        await Assert.That(final.Read(view => view.GetRecord<QueueCounters>(KeySpace.Partition(QueueClaimVersionExhaustionProtocol.CounterSpace,
            state.Lane.Partition, state.Lane.Queue)))).IsEqualTo(new QueueCounters(0, 0, 0, 0, 2));
    }
}
