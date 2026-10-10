using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ResourceExecution;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueRetryColdTrial
{
    internal static async Task RunAsync(CancellationToken token)
    {
        using var fixture = new TestDatabase();
        fixture.Configure(QueueRetryColdProtocol.Collection, ResourceKind.Collection);
        fixture.Configure(QueueRetryColdProtocol.Queue, ResourceKind.WorkQueue, queuePolicy: new()
        {
            MaxAttempts = QueueRetryColdProtocol.MaximumAttempts,
            MaxStoredMessages = QueueRetryColdProtocol.StoredCapacity,
            RetryBaseMilliseconds = QueueRetryColdProtocol.BaseDelayMilliseconds,
            RetryMaxMilliseconds = QueueRetryColdProtocol.MaximumDelayMilliseconds
        });
        fixture.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new(
            QueueRetryColdProtocol.Inspector, fixture.Partition.TenantId,
            [new(fixture.Partition.DatabaseId, QueueRetryColdProtocol.Queue, Capability.QueueInspect)], []))).Get<PrincipalRecord>();
        var state = await SeedAsync(fixture, token);
        await QueueRetryColdPhases.NackAsync(fixture.Database, state, state.FirstDelivery, state.Start,
            state.Start.AddMilliseconds(QueueRetryColdProtocol.BaseDelayMilliseconds), QueueRetryColdProtocol.FirstRetryVersion, QueueRetryColdProtocol.FirstSequence);
        await QueueRetryColdPhases.EmptyAsync(fixture.Database, state, state.Start.AddMilliseconds(QueueRetryColdProtocol.BeforeDueMilliseconds));
        var scheduled = QueueRetryColdAssertions.LaneBytes(fixture.Store, state.Lane);
        fixture.Store.Dispose();
        using var reopened = new ZoneTreeStore(new(fixture.Directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var database = QueueWholeFlowStorage.Open(reopened);
        await Assert.That(QueueRetryColdAssertions.LaneBytes(reopened, state.Lane)).IsEquivalentTo(scheduled, CollectionOrdering.Matching);
        await ReplayAsync(database, state, state.Start.AddMilliseconds(QueueRetryColdProtocol.BeforeDueMilliseconds));
        await QueueRetryColdPhases.ExhaustAsync(database, state, token);
        await QueueRetryColdRefusals.RunAsync(database, state, token);
        await QueueRetryColdPhases.ExpireAndContinueAsync(database, state, token);
        var terminal = QueueRetryColdAssertions.LaneBytes(reopened, state.Lane);
        reopened.Dispose();
        using var cold = new ZoneTreeStore(new(fixture.Directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var restored = QueueWholeFlowStorage.Open(cold);
        await Assert.That(QueueRetryColdAssertions.LaneBytes(cold, state.Lane)).IsEquivalentTo(terminal, CollectionOrdering.Matching);
        await ReplayAsync(restored, state, state.FinalTime);
        await QueueRetryColdAssertions.TerminalAsync(restored, state, true);
        await QueueRetryColdRefusals.StaleClaimReplayAsync(restored, state);
        await QueueRetryColdPhases.EmptyAsync(restored, state, state.FinalTime);
        await Assert.That(QueueRetryColdAssertions.LaneBytes(cold, state.Lane)).IsEquivalentTo(terminal, CollectionOrdering.Matching);
    }

    private static async Task<QueueRetryColdState> SeedAsync(TestDatabase fixture, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var start = QueueWholeFlowStorage.Clock(fixture.Store);
        var lane = new QueueLaneRef(fixture.Partition, QueueRetryColdProtocol.Queue);
        var command = new CommandRequest(Guid.NewGuid(), fixture.Partition,
            [new EnqueueMessage(lane.Queue, QueueRetryColdProtocol.Retry, QueueRetryColdProtocol.Payload,
                QueueRetryColdProtocol.Headers, OrderingKey: QueueRetryColdProtocol.OrderingKey),
             new EnqueueMessage(lane.Queue, QueueRetryColdProtocol.Expiry, QueueRetryColdProtocol.ExpiryPayload,
                QueueRetryColdProtocol.Headers, start.AddSeconds(QueueRetryColdProtocol.ScheduledSeconds),
                start.AddSeconds(QueueRetryColdProtocol.ExpirySeconds))]);
        var enqueued = QueueWholeFlowStorage.Apply(fixture.Database, OperationKind.Batch, command, command.CommandId, start);
        enqueued.Get<CommitReceipt>();
        var claim = new ReceiveRequest(Guid.NewGuid(), lane, LeaseSeconds: QueueRetryColdProtocol.LeaseSeconds);
        var claimed = QueueWholeFlowStorage.Apply(fixture.Database, OperationKind.Receive, claim, claim.RequestId, start);
        var received = claimed.Get<ReceiveResult>();
        await Assert.That(received.RequestId).IsEqualTo(claim.RequestId);
        var delivery = await Assert.That(received.Deliveries).HasSingleItem();
        var state = new QueueRetryColdState(lane, start, command, enqueued, claim, claimed, delivery);
        await QueueRetryColdAssertions.DeliveryAsync(fixture.Database, delivery, state, QueueRetryColdProtocol.One,
            QueueRetryColdProtocol.FirstSequence, QueueRetryColdProtocol.FirstClaimVersion, start);
        return state;
    }

    private static async Task ReplayAsync(DatabaseEngine database, QueueRetryColdState state, DateTimeOffset time)
    {
        await NativeReplayResultAssertions.Same<CommitReceipt>(QueueWholeFlowStorage.Apply(database, OperationKind.Batch,
            state.Enqueue, state.Enqueue.CommandId, time), state.Enqueued);
        if (state.Healthy is { } healthy)
        {
            await NativeReplayResultAssertions.Same<CommitReceipt>(QueueWholeFlowStorage.Apply(database, OperationKind.Batch,
                healthy.Command, healthy.Command.CommandId, time), healthy.Result);
        }
        if (state.Rejected is { } rejected)
        {
            var actual = QueueWholeFlowStorage.Apply(database, OperationKind.Batch, rejected.Command,
                rejected.Command.CommandId, time);
            await Assert.That(actual.Error).IsEqualTo(rejected.Result.Error);
            await Assert.That(actual.SafeDetail).IsEqualTo(rejected.Result.SafeDetail);
            await Assert.That(actual.Json).IsEqualTo(rejected.Result.Json);
            await Assert.That(actual.NativeValue).IsNull();
        }
        foreach (var original in state.Completed)
        {
            await NativeReplayResultAssertions.Same<CommitReceipt>(QueueWholeFlowStorage.Apply(database, OperationKind.Delivery,
                original.Command, original.Command.CommandId, time), original.Result);
        }
    }
}
