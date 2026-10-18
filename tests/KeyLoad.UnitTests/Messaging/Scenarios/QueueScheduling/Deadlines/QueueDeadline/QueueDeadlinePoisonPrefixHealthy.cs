using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueDeadlinePoisonPrefixHealthy
{
    internal static async Task RunAsync(DatabaseEngine database, ZoneTreeStore store, string directory, QueueLaneRef lane,
        DateTimeOffset due, bool leased, List<(Command<DatabaseCommand> Operation, Result<object> Result)> outcomes,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        MessageMetadata[] rows = [new(QueueDeadlinePoisonPrefixProtocol.ScheduledMessage, MessageState.Ready, QueueDeadlinePoisonPrefixProtocol.Initial, QueueDeadlinePoisonPrefixProtocol.Second, leased ? QueueDeadlinePoisonPrefixProtocol.FirstAdvancedReadySequence : QueueDeadlinePoisonPrefixProtocol.FinalReadySequence, null, null),
            new(QueueDeadlinePoisonPrefixProtocol.LeasedMessage, MessageState.Ready, QueueDeadlinePoisonPrefixProtocol.First, QueueDeadlinePoisonPrefixProtocol.ExpiredLeaseStateVersion, leased ? QueueDeadlinePoisonPrefixProtocol.FinalReadySequence : QueueDeadlinePoisonPrefixProtocol.FirstAdvancedReadySequence, null, null, LeaseVersion: QueueDeadlinePoisonPrefixProtocol.First),
            new(QueueDeadlinePoisonPrefixProtocol.ExpiringMessage, MessageState.Expired, QueueDeadlinePoisonPrefixProtocol.Initial, QueueDeadlinePoisonPrefixProtocol.Second, QueueDeadlinePoisonPrefixProtocol.Second, null, due)];
        var claims = await QueueDeadlinePoisonPrefixClaims.ReceiveAsync(database, store, lane,
            rows.Take(QueueDeadlinePoisonPrefixProtocol.Second).ToArray(), QueueDeadlineNativeProtocol.Worker, due, token);
        rows = rows.Select(value => value.State == MessageState.Ready ? value with
        {
            State = MessageState.Leased,
            StateVersion = value.StateVersion + QueueDeadlinePoisonPrefixProtocol.First,
            Attempts = value.Attempts + QueueDeadlinePoisonPrefixProtocol.First,
            LeaseVersion = value.LeaseVersion + QueueDeadlinePoisonPrefixProtocol.First,
            LeaseOwner = QueueDeadlineNativeProtocol.Worker,
            LeaseUntil = due.AddSeconds(QueueDeadlinePoisonPrefixProtocol.LeaseSeconds)
        } : value).ToArray();
        await QueueDeadlinePoisonPrefixImage.RequireAsync(database, lane, rows, due, QueueDeadlinePoisonPrefixProtocol.FinalReadySequence);
        foreach (var delivery in claims.Deliveries)
        {
            token.ThrowIfCancellationRequested();
            var metadata = rows.Single(value => value.Id == delivery.Id);
            var ack = new DeliveryCommand(Guid.NewGuid(), lane, delivery.Token, DeliveryAction.Ack);
            var expected = new CommitReceipt(ack.CommandId, QueueDeadlinePoisonPrefixClaims.NextToken(database, store, lane), [new(QueueDeadlinePoisonPrefixProtocol.AckReceipt, lane.Queue, metadata.Id, metadata.StateVersion + QueueDeadlinePoisonPrefixProtocol.First)], DurabilityProfile.ProcessDurable, 1);
            var operation = QueueDeadlinePoisonPrefixClaims.Operation(database, OperationKind.Delivery, ack.CommandId,
                ack, QueueDeadlineNativeProtocol.Worker, due);
            var result = database.Apply(operation);
            await NativeReplayResultAssertions.Same<CommitReceipt>(result, Result<object>.Succeed(expected));
            outcomes.Add((operation, result));
            rows = rows.Select(value => value.Id == metadata.Id ? value with
            {
                State = MessageState.Acked,
                StateVersion = value.StateVersion + QueueDeadlinePoisonPrefixProtocol.First,
                LeaseOwner = null,
                LeaseUntil = null
            } : value).ToArray();
            await QueueDeadlinePoisonPrefixImage.RequireAsync(database, lane, rows, due, QueueDeadlinePoisonPrefixProtocol.FinalReadySequence);
        }
        await FinalColdAsync(store, directory, lane, due, rows, outcomes, token);
    }

    private static async Task FinalColdAsync(ZoneTreeStore store, string directory, QueueLaneRef lane, DateTimeOffset due,
        MessageMetadata[] rows, List<(Command<DatabaseCommand> Operation, Result<object> Result)> outcomes, CancellationToken token)
    {
        var image = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = NativeSerialization.Serialize(store.Identity);
        store.Dispose();
        token.ThrowIfCancellationRequested();
        using var cold = new ZoneTreeStore(new(directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var database = QueueWholeFlowStorage.Open(cold);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(cold, image, position, identity);
        foreach (var item in outcomes)
        {
            token.ThrowIfCancellationRequested();
            await NativeReplayResultAssertions.Same<CommitReceipt>(database.Apply(item.Operation), item.Result);
            await NativeReplayResultAssertions.Same<CommitReceipt>(database.ResolveOutcome(item.Operation), item.Result);
        }
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(cold, image, position, identity);
        await QueueDeadlinePoisonPrefixImage.RequireAsync(database, lane, rows, due, QueueDeadlinePoisonPrefixProtocol.FinalReadySequence);
    }
}
