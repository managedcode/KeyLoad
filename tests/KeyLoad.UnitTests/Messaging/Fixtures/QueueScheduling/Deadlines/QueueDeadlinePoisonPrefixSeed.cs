using System.Collections.Immutable;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ResourceExecution;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueDeadlinePoisonPrefixSeed
{
    internal static async Task<List<(Command<DatabaseCommand> Operation, Result<object> Result)>> RunAsync(DatabaseEngine database,
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

    private static async Task<(Command<DatabaseCommand> Operation, Result<object> Result)> ApplyAsync(DatabaseEngine database,
        ZoneTreeStore store, CommandRequest command, DateTimeOffset time)
    {
        var placement = database.ReadAtomicPartitionPlacement(QueueDeadlinePoisonPrefixProtocol.Root, command.Partition);
        var expected = new CommitReceipt(command.CommandId, new(placement.Incarnation,
            command.Partition.AtomicPartitionId, checked(store.Position + QueueDeadlinePoisonPrefixProtocol.First)), command.Mutations.Select(value => new MutationReceipt(QueueDeadlinePoisonPrefixProtocol.EnqueueReceipt, ((EnqueueMessage)value).Queue,
                ((EnqueueMessage)value).MessageId, QueueDeadlinePoisonPrefixProtocol.First)).ToImmutableArray(), DurabilityProfile.ProcessDurable, 1);
        var operation = database.PrepareQueueRetryOperation(database.OwnCommand(database.CreateCommand(OperationKind.Batch, command.CommandId, QueueDeadlinePoisonPrefixProtocol.Root, time, System.Text.Json.JsonSerializer.Serialize(command, JsonDefaults.Options))));
        var result = database.Apply(operation);
        await Assert.That(Errors.Code(result.Problem)).IsNull();

        await ReplayResultAssertions.Same<CommitReceipt>(result, Result<object>.Succeed(expected));
        return (operation, result);
    }
}
