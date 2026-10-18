using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
using TUnit.Assertions.Enums;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class QueueClaimAckRecovery
{
    internal static async Task VerifyAsync(string root, CommitStage stage, bool ack, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var store = new ZoneTreeStore(new(root), RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
        var database = QueueLifecycleRecoveryAssertions.Open(store);
        var operation = NativeSerialization.Deserialize<Command<DatabaseCommand>>(await File.ReadAllBytesAsync(
            Path.Combine(root, QueueClaimAckCrashProtocol.OperationFile), token));
        var original = ack ? NativeSerialization.Deserialize<Delivery>(await File.ReadAllBytesAsync(
            Path.Combine(root, QueueClaimAckCrashProtocol.DeliveryFile), token)) : null;
        var target = NativeSerialization.Deserialize<CommitToken>(await File.ReadAllBytesAsync(
            Path.Combine(root, QueueClaimAckCrashProtocol.TargetFile), token));
        var baseline = NativeSerialization.Deserialize<string[]>(await File.ReadAllBytesAsync(
            Path.Combine(root, QueueClaimAckCrashProtocol.BaselineFile), token));
        await AssertOriginalAsync(database, operation, target, ack, original);
        var recovered = await RecoveredCutAsync(database, store, operation, stage, ack, original, target, baseline, token);
        var image = QueueClaimAckRecoveryAssertions.Capture(store);
        var position = store.Position;
        var identity = store.Identity;
        token.ThrowIfCancellationRequested();
        store.Dispose();
        using var cold = new ZoneTreeStore(new(root), RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
        await QueueClaimAckRecoveryAssertions.EqualAsync(cold.Identity, identity);
        await Assert.That(cold.Position).IsEqualTo(position);
        await Assert.That(QueueClaimAckRecoveryAssertions.Capture(cold)).IsEquivalentTo(image, CollectionOrdering.Matching);
        await QueueClaimAckRecoveryContinuation.ColdAsync(root, QueueLifecycleRecoveryAssertions.Open(cold), cold,
            operation, ack, recovered.Result, recovered.Delivery, token);
    }

    private static async Task<(Result<object> Result, Delivery? Delivery)> RecoveredCutAsync(DatabaseEngine database,
        ZoneTreeStore store, Command<DatabaseCommand> operation, CommitStage stage, bool ack, Delivery? original,
        CommitToken target, string[] baseline, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var outcome = OutcomeStoreOracle.ReadPartition(store, QueueClaimAckCrashProtocol.Partition,
            CrashFixtureValues.Principal, operation.CommandId);
        await Assert.That(store.Position).IsEqualTo(outcome is null
            ? target.Position - QueueClaimAckCrashProtocol.AdjacentPosition : target.Position);
        if (outcome is null)
        {
            await Assert.That(QueueClaimAckRecoveryAssertions.Capture(store)).IsEquivalentTo(baseline, CollectionOrdering.Matching);
        }
        if (stage >= CommitStage.JournalFlushed)
        {
            await Assert.That(outcome).IsNotNull();
        }
        var delivery = original;
        if (!ack && outcome is not null)
        {
            delivery = outcome.Value.Get<ReceiveResult>().Deliveries.Single();
        }
        await QueueClaimAckRecoveryAssertions.ImageAsync(database, QueueClaimAckCrashProtocol.Message,
            RecoveredState(ack, outcome is not null), delivery, 1);
        token.ThrowIfCancellationRequested();
        var result = database.Apply(operation);
        await Assert.That(store.Position).IsEqualTo(target.Position);
        if (outcome is { } retained)
        {
            await QueueClaimAckRecoveryAssertions.EqualAsync(result, retained);
        }
        if (ack)
        {
            await ReceiptAsync(result.Get<CommitReceipt>(), operation.CommandId, QueueClaimAckCrashProtocol.Message, target);
        }
        else
        {
            await QueueClaimAckRecoveryAssertions.DeliveryAsync(database, result.Get<ReceiveResult>(), operation.CommandId,
                new DateTimeOffset(operation.Timestamp), QueueClaimAckCrashProtocol.Message, target);
            delivery = result.Get<ReceiveResult>().Deliveries.Single();
        }
        await QueueClaimAckRecoveryAssertions.ImageAsync(database, QueueClaimAckCrashProtocol.Message,
            RecoveredState(ack, true), delivery, 1);
        await ReplayAsync(database, store, operation, result, token);
        return (result, delivery);
    }

    internal static MessageState RecoveredState(bool ack, bool committed)
    {
        if (ack)
        {
            return committed ? MessageState.Acked : MessageState.Leased;
        }
        return committed ? MessageState.Leased : MessageState.Ready;
    }

    internal static async Task ReplayAsync(DatabaseEngine database, ZoneTreeStore store, Command<DatabaseCommand> operation,
        Result<object> result, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var image = QueueClaimAckRecoveryAssertions.Capture(store);
        var position = store.Position;
        await QueueClaimAckRecoveryAssertions.EqualAsync(database.Apply(operation), result);
        await QueueClaimAckRecoveryAssertions.EqualAsync(database.ResolveOutcome(operation), result);
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(QueueClaimAckRecoveryAssertions.Capture(store)).IsEquivalentTo(image, CollectionOrdering.Matching);
    }

    internal static async Task ReceiptAsync(CommitReceipt result, Guid id, string message, CommitToken expectedToken)
    {
        await QueueClaimAckRecoveryAssertions.EqualAsync(result, new CommitReceipt(id, expectedToken, [new("Ack", QueueClaimAckCrashProtocol.Queue, message, 3)], DurabilityProfile.ProcessDurable, 1));
    }

    internal static CommitToken NextToken(DatabaseEngine database, ZoneTreeStore store)
    {
        var placement = database.ReadAtomicPartitionPlacement(CrashFixtureValues.Principal,
            QueueClaimAckCrashProtocol.Partition);
        return new(placement.Incarnation, QueueClaimAckCrashProtocol.Partition.AtomicPartitionId,
            checked(store.Position + QueueClaimAckCrashProtocol.AdjacentPosition));
    }

    private static async Task AssertOriginalAsync(DatabaseEngine database, Command<DatabaseCommand> operation,
        CommitToken target, bool ack, Delivery? delivery)
    {
        await Assert.That(operation.CommandId).IsEqualTo(QueueClaimAckCrashProtocol.OperationId);
        await Assert.That(operation.UserId!).IsEqualTo(CrashFixtureValues.Principal);
        await Assert.That(operation.Value!.Kind).IsEqualTo(ack ? OperationKind.Delivery : OperationKind.Receive);
        await QueueClaimAckRecoveryAssertions.EqualAsync(database.OwnCommand(operation), operation);
        var placement = database.ReadAtomicPartitionPlacement(CrashFixtureValues.Principal,
            QueueClaimAckCrashProtocol.Partition);
        await Assert.That(target.Incarnation).IsEqualTo(placement.Incarnation);
        await Assert.That(target.AtomicPartitionId).IsEqualTo(QueueClaimAckCrashProtocol.Partition.AtomicPartitionId);
        await Assert.That(target.Position).IsGreaterThan(0L);
        if (ack)
        {
            await QueueClaimAckRecoveryAssertions.EqualAsync(NativeSerialization.Deserialize<DeliveryCommand>(operation.Value!.Input.Span, NativeValidationProfile.PublicInputElements),
                new DeliveryCommand(operation.CommandId, QueueClaimAckCrashProtocol.Lane, delivery!.Token, DeliveryAction.Ack));
        }
        else
        {
            await QueueClaimAckRecoveryAssertions.EqualAsync(NativeSerialization.Deserialize<ReceiveRequest>(operation.Value!.Input.Span, NativeValidationProfile.PublicInputElements), new ReceiveRequest(operation.CommandId,
                QueueClaimAckCrashProtocol.Lane, MaxMessages: 1, LeaseSeconds: QueueClaimAckCrashProtocol.LeaseSeconds));
        }
    }
}
