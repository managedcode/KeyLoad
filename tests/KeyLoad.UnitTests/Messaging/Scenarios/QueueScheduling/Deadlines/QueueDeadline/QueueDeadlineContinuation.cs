using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ResourceExecution;
using ManagedCode.Communication;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueDeadlineContinuation
{
    internal static async Task RunAsync(DatabaseEngine database, ZoneTreeStore first, string directory,
        QueueLaneRef lane, CommandRequest command, Result<object> seeded, CommandRequest seed,
        MessageMetadata metadata, DateTimeOffset due, CancellationToken token)
    {
        QueueDeadlineOperations.ConfigureWorker(database, lane.Partition, due, Capability.QueueInspect,
            RuntimeQueueDeadlineProtocol.Second);
        await QueueDeadlineAssertions.RefusedAsync(database, first, lane, command, due, ErrorCode.PermissionDenied);
        QueueDeadlineOperations.ConfigureWorker(database, lane.Partition, due,
            Capability.QueueConsume | Capability.QueueInspect | Capability.QueueAck, RuntimeQueueDeadlineProtocol.Third);
        command = command with
        {
            CommandId = Guid.NewGuid()
        };
        var result = QueueDeadlineOperations.Batch(database, command, due);
        var receipt = result.Get<CommitReceipt>();
        var expectedReceipt = new MutationReceipt(KeyLoad.Core.Features.Messaging.QueueDeadlineProtocol.ReceiptKind, lane.Queue,
            metadata.Id, metadata.StateVersion + RuntimeQueueDeadlineProtocol.First);
        await Assert.That(OrleansSerialization.Serialize(receipt.Mutations.Single()).AsSpan()
            .SequenceEqual(OrleansSerialization.Serialize(expectedReceipt))).IsTrue();
        var kind = ((AdvanceQueueDeadline)command.Mutations.Single()).Kind;
        var expected = metadata with
        {
            State = kind == QueueDeadlineKind.ExpireMessage ? MessageState.Expired : MessageState.Ready,
            StateVersion = metadata.StateVersion + RuntimeQueueDeadlineProtocol.First,
            ReadySequence = kind == QueueDeadlineKind.ExpireMessage ? metadata.ReadySequence
                : kind == QueueDeadlineKind.ExpireLease ? RuntimeQueueDeadlineProtocol.Second : RuntimeQueueDeadlineProtocol.First,
            NotBefore = kind == QueueDeadlineKind.ExpireMessage ? metadata.NotBefore : null,
            LeaseOwner = null,
            LeaseUntil = null
        };
        await QueueDeadlineAssertions.OriginalAsync(database, lane, expected);
        var bytes = QueueRetryColdAssertions.LaneBytes(first, lane);
        first.Dispose();
        token.ThrowIfCancellationRequested();
        using var second = new ZoneTreeStore(new(directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var cold = QueueWholeFlowStorage.Open(second);
        await ReplayResultAssertions.Same<CommitReceipt>(QueueDeadlineOperations.Batch(cold, command, due), result);
        await ReplayResultAssertions.Same<CommitReceipt>(QueueDeadlineOperations.Batch(cold, seed, due,
            RuntimeQueueDeadlineProtocol.Root), seeded);
        await Assert.That(QueueRetryColdAssertions.LaneBytes(second, lane)).IsEquivalentTo(bytes, CollectionOrdering.Matching);
        await QueueDeadlineAssertions.OriginalAsync(cold, lane, expected);
        await QueueDeadlineHealthy.RunAsync(cold, lane, due,
            kind == QueueDeadlineKind.ExpireLease ? RuntimeQueueDeadlineProtocol.Second : RuntimeQueueDeadlineProtocol.First);
    }
}
