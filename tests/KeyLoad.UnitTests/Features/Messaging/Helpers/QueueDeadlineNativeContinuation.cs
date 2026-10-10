using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ResourceExecution;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueDeadlineNativeContinuation
{
    internal static async Task RunAsync(DatabaseEngine database, ZoneTreeStore first, string directory,
        QueueLaneRef lane, CommandRequest command, OperationResult seeded, CommandRequest seed,
        MessageMetadata metadata, DateTimeOffset due, CancellationToken token)
    {
        QueueDeadlineNativeOperations.ConfigureWorker(database, lane.Partition, due, Capability.QueueInspect,
            QueueDeadlineNativeProtocol.Second);
        await QueueDeadlineNativeAssertions.RefusedAsync(database, first, lane, command, due, ErrorCode.PermissionDenied);
        QueueDeadlineNativeOperations.ConfigureWorker(database, lane.Partition, due,
            Capability.QueueConsume | Capability.QueueInspect | Capability.QueueAck, QueueDeadlineNativeProtocol.Third);
        command = command with { CommandId = Guid.NewGuid() };
        var result = QueueDeadlineNativeOperations.Batch(database, command, due);
        var receipt = result.Get<CommitReceipt>();
        var expectedReceipt = new MutationReceipt(KeyLoad.Core.Features.Messaging.QueueDeadlineProtocol.ReceiptKind, lane.Queue,
            metadata.Id, metadata.StateVersion + QueueDeadlineNativeProtocol.First);
        await Assert.That(NativeSerialization.Serialize(receipt.Mutations.Single()).AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(expectedReceipt))).IsTrue();
        var kind = ((AdvanceQueueDeadline)command.Mutations.Single()).Kind;
        var expected = metadata with
        {
            State = kind == QueueDeadlineKind.ExpireMessage ? MessageState.Expired : MessageState.Ready,
            StateVersion = metadata.StateVersion + QueueDeadlineNativeProtocol.First,
            ReadySequence = kind == QueueDeadlineKind.ExpireMessage ? metadata.ReadySequence
                : kind == QueueDeadlineKind.ExpireLease ? QueueDeadlineNativeProtocol.Second : QueueDeadlineNativeProtocol.First,
            NotBefore = kind == QueueDeadlineKind.ExpireMessage ? metadata.NotBefore : null,
            LeaseOwner = null,
            LeaseUntil = null
        };
        await QueueDeadlineNativeAssertions.OriginalAsync(database, lane, expected);
        var bytes = QueueRetryColdAssertions.LaneBytes(first, lane);
        first.Dispose();
        token.ThrowIfCancellationRequested();
        using var second = new ZoneTreeStore(new(directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var cold = QueueWholeFlowStorage.Open(second);
        await NativeReplayResultAssertions.Same<CommitReceipt>(QueueDeadlineNativeOperations.Batch(cold, command, due), result);
        await NativeReplayResultAssertions.Same<CommitReceipt>(QueueDeadlineNativeOperations.Batch(cold, seed, due,
            QueueDeadlineNativeProtocol.Root), seeded);
        await Assert.That(QueueRetryColdAssertions.LaneBytes(second, lane)).IsEquivalentTo(bytes, CollectionOrdering.Matching);
        await QueueDeadlineNativeAssertions.OriginalAsync(cold, lane, expected);
        await QueueDeadlineNativeHealthy.RunAsync(cold, lane, due,
            kind == QueueDeadlineKind.ExpireLease ? QueueDeadlineNativeProtocol.Second : QueueDeadlineNativeProtocol.First);
    }
}
