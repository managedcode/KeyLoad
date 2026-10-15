using KeyLoad.CrashHost;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class QueueEnqueueProcessRecovery
{
    internal static async Task VerifyAsync(string root, CommitStage stage, CancellationToken token)
    {
        using var store = new ZoneTreeStore(new(root), RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
        var owner = QueueLifecycleRecoveryAssertions.Open(store);
        var operation = NativeSerialization.Deserialize<ReplicatedOperation>(await File.ReadAllBytesAsync(Path.Combine(root, QueueEnqueueCrashProtocol.OperationFile), token));
        var target = NativeSerialization.Deserialize<CommitToken>(await File.ReadAllBytesAsync(Path.Combine(root, QueueEnqueueCrashProtocol.TargetFile), token));
        var baseline = NativeSerialization.Deserialize<string[]>(await File.ReadAllBytesAsync(Path.Combine(root, QueueEnqueueCrashProtocol.BaselineFile), token));
        var command = JsonDefaults.Deserialize<CommandRequest>(operation.PayloadJson);
        var due = command.Mutations.OfType<EnqueueMessage>().Single(message => message.MessageId == QueueEnqueueCrashProtocol.Scheduled).NotBefore;
        await QueueEnqueueProcessValueAssertions.EqualAsync(command, new CommandRequest(QueueEnqueueCrashProtocol.OperationId,
            QueueEnqueueCrashProtocol.Partition,
            [new PutDocument(QueueEnqueueCrashProtocol.Collection, QueueEnqueueCrashProtocol.Ready, QueueEnqueueCrashProtocol.Payload),
                new EnqueueMessage(QueueEnqueueCrashProtocol.Queue, QueueEnqueueCrashProtocol.Ready, QueueEnqueueCrashProtocol.Payload, QueueEnqueueCrashProtocol.Headers),
                new EnqueueMessage(QueueEnqueueCrashProtocol.Queue, QueueEnqueueCrashProtocol.Scheduled, QueueEnqueueCrashProtocol.Payload,
                    QueueEnqueueCrashProtocol.Headers, NotBefore: due)]));
        await Assert.That(due!.Value).IsGreaterThan(operation.EvaluatedAt);
        await Assert.That(operation.Id).IsEqualTo(QueueEnqueueCrashProtocol.OperationId);
        await Assert.That(operation.PrincipalId).IsEqualTo(CrashFixtureValues.Principal);
        await Assert.That(operation.Kind).IsEqualTo(OperationKind.Batch);
        await QueueClaimAckRecoveryAssertions.EqualAsync(owner.NormalizeOperation(operation), operation);
        var placement = owner.ReadAtomicPartitionPlacement(CrashFixtureValues.Principal, new(QueueEnqueueCrashProtocol.First, command.Partition));
        await Assert.That(target.Incarnation).IsEqualTo(placement.Incarnation);
        await Assert.That(target.OwnershipEpoch).IsEqualTo(placement.PlacementEpoch);
        await Assert.That(target.AtomicPartitionId).IsEqualTo(command.Partition.AtomicPartitionId);
        var outcome = OutcomeStoreOracle.ReadPartition(store, command.Partition, CrashFixtureValues.Principal, operation.Id);
        await Assert.That(store.Position).IsEqualTo(outcome is null ? target.Position - QueueEnqueueCrashProtocol.First : target.Position);
        if (outcome is null)
        {
            await Assert.That(QueueClaimAckRecoveryAssertions.Capture(store)).IsEquivalentTo(baseline, CollectionOrdering.Matching);
        }
        else
        {
            await QueueEnqueueProcessAssertions.ReceiptAsync(outcome, command, target);
            await QueueEnqueueProcessAssertions.InitialAsync(owner, store, command);
        }
        if (stage >= CommitStage.JournalFlushed)
        {
            await Assert.That(outcome).IsNotNull();
        }
        var result = owner.Apply(operation);
        await Assert.That(store.Position).IsEqualTo(target.Position);
        await QueueEnqueueProcessAssertions.ReceiptAsync(result, command, target);
        await QueueEnqueueProcessAssertions.InitialAsync(owner, store, command);
        if (outcome is not null)
        {
            await QueueClaimAckRecoveryAssertions.EqualAsync(result, outcome);
        }
        await QueueClaimAckRecovery.ReplayAsync(owner, store, operation, result, token);
        await ColdAsync(root, store, operation, result, command, token);
    }

    private static async Task ColdAsync(string root, ZoneTreeStore store, ReplicatedOperation operation,
        OperationResult result, CommandRequest command, CancellationToken token)
    {
        var image = QueueClaimAckRecoveryAssertions.Capture(store);
        var position = store.Position;
        var identity = store.Identity;
        store.Dispose();
        using var cold = new ZoneTreeStore(new(root), RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
        var owner = QueueLifecycleRecoveryAssertions.Open(cold);
        await QueueEnqueueProcessValueAssertions.IdentityAsync(cold.Identity, identity);
        await Assert.That(cold.Position).IsEqualTo(position);
        await Assert.That(QueueClaimAckRecoveryAssertions.Capture(cold)).IsEquivalentTo(image, CollectionOrdering.Matching);
        await QueueEnqueueProcessAssertions.InitialAsync(owner, cold, command);
        await QueueClaimAckRecovery.ReplayAsync(owner, cold, operation, result, token);
        await QueueEnqueueProcessContinuation.RunAsync(root, owner, cold, operation, result, command, token);
    }
}
