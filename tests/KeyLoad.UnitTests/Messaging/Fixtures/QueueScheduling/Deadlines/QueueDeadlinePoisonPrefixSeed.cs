using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueDeadlinePoisonPrefixSeed
{
    internal static async Task<List<(ReplicatedOperation Operation, OperationResult Result)>> RunAsync(DatabaseEngine database,
        ZoneTreeStore store, QueueLaneRef lane, DateTimeOffset start, DateTimeOffset due, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var first = new CommandRequest(Guid.NewGuid(), lane.Partition,
            [new EnqueueMessage(lane.Queue, QueueDeadlinePoisonPrefixProtocol.LeasedMessage, QueueDeadlinePoisonPrefixProtocol.Json, QueueDeadlinePoisonPrefixProtocol.Json)]);
        var original = await ApplyAsync(database, store, first, start);
        var originalClaim = await QueueDeadlinePoisonPrefixClaims.ReceiveAsync(database, store, lane,
            [new(QueueDeadlinePoisonPrefixProtocol.LeasedMessage, MessageState.Ready, QueueDeadlinePoisonPrefixProtocol.Initial, QueueDeadlinePoisonPrefixProtocol.First, QueueDeadlinePoisonPrefixProtocol.First, null, null)], QueueDeadlinePoisonPrefixProtocol.Root, start, token);
        await QueueDeadlinePoisonPrefixImage.RequireAsync(database, lane,
            [new(QueueDeadlinePoisonPrefixProtocol.LeasedMessage, MessageState.Leased, QueueDeadlinePoisonPrefixProtocol.First, QueueDeadlinePoisonPrefixProtocol.Second, QueueDeadlinePoisonPrefixProtocol.First, null, null, QueueDeadlinePoisonPrefixProtocol.Root, QueueDeadlinePoisonPrefixProtocol.First, due)], due, QueueDeadlinePoisonPrefixProtocol.First);
        token.ThrowIfCancellationRequested();
        var second = new CommandRequest(Guid.NewGuid(), lane.Partition,
            [new EnqueueMessage(lane.Queue, QueueDeadlinePoisonPrefixProtocol.ScheduledMessage, QueueDeadlinePoisonPrefixProtocol.Json, QueueDeadlinePoisonPrefixProtocol.Json, NotBefore: due),
                new EnqueueMessage(lane.Queue, QueueDeadlinePoisonPrefixProtocol.ExpiringMessage, QueueDeadlinePoisonPrefixProtocol.Json, QueueDeadlinePoisonPrefixProtocol.Json, ExpiresAt: due)]);
        var additional = await ApplyAsync(database, store, second, start);
        await QueueDeadlinePoisonPrefixClaims.CurrentClaimAsync(database, store, lane, originalClaim,
            QueueDeadlinePoisonPrefixProtocol.Root, start, token);
        return [original, additional];
    }

    private static async Task<(ReplicatedOperation Operation, OperationResult Result)> ApplyAsync(DatabaseEngine database,
        ZoneTreeStore store, CommandRequest command, DateTimeOffset time)
    {
        var placement = database.ReadAtomicPartitionPlacement(QueueDeadlinePoisonPrefixProtocol.Root, new(QueueDeadlinePoisonPrefixProtocol.First, command.Partition));
        var expected = new CommitReceipt(command.CommandId, new(placement.Incarnation,
            command.Partition.AtomicPartitionId, checked(store.Position + QueueDeadlinePoisonPrefixProtocol.First), placement.PlacementEpoch),
            command.Mutations.Select(value => new MutationReceipt(QueueDeadlinePoisonPrefixProtocol.EnqueueReceipt, ((EnqueueMessage)value).Queue,
                ((EnqueueMessage)value).MessageId, QueueDeadlinePoisonPrefixProtocol.First)).ToImmutableArray(), DurabilityProfile.ProcessDurable);
        var operation = database.PrepareQueueRetryOperation(database.NormalizeOperation(new(command.CommandId,
            OperationKind.Batch, QueueDeadlinePoisonPrefixProtocol.Root, time, System.Text.Json.JsonSerializer.Serialize(command, JsonDefaults.Options))));
        var result = database.Apply(operation);
        await Assert.That(result.Error).IsNull();
        await Assert.That(result.Json).IsNull();
        await NativeReplayResultAssertions.Same<CommitReceipt>(result, new(null)
        {
            NativeValue = expected
        });
        return (operation, result);
    }
}
