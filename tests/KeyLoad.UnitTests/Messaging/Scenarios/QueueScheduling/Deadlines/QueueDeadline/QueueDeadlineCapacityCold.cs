using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ResourceExecution;
using ManagedCode.Communication;
using static KeyLoad.UnitTests.Features.Messaging.QueueDeadlineCapacityProtocol;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueDeadlineCapacityCold
{
    internal static async Task RunAsync(DatabaseEngine database, ZoneTreeStore store, string directory,
        QueueDeadlineCapacityState state, (EnqueueMessage Input, MessageMetadata Metadata)[] messages,
        long sequence, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await ReplayAsync(database, store, state, token);
        var image = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = OrleansSerialization.Serialize(store.Identity);
        store.Dispose();
        token.ThrowIfCancellationRequested();
        using var cold = new ZoneTreeStore(new(directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var owner = QueueWholeFlowStorage.Open(cold);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(cold, image, position, identity);
        await QueueDeadlineCapacityImage.RequireAsync(owner, state.Lane, messages, sequence,
            QueueDeadlineCapacityOperations.ParkedSequence(state, messages.First().Metadata));
        await ReplayAsync(owner, cold, state, token);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(cold, image, position, identity);
    }

    private static async Task ReplayAsync(DatabaseEngine database, ZoneTreeStore store,
        QueueDeadlineCapacityState state, CancellationToken token)
    {
        var image = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = OrleansSerialization.Serialize(store.Identity);
        foreach (var (operation, result) in state.Outcomes)
        {
            token.ThrowIfCancellationRequested();
            var stored = OutcomeStoreOracle.Read(store, operation)
                ?? throw new InvalidOperationException("The deadline command outcome is absent before replay.");
            await Assert.That(OrleansSerialization.Serialize(stored))
                .IsEquivalentTo(OrleansSerialization.Serialize(result), TUnit.Assertions.Enums.CollectionOrdering.Matching);
            if (result.Value is CommitReceipt receipt)
            {
                await QueueDeadlineCapacityOperations.ReceiptAsync(database.Apply(operation), receipt);
                await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, image, position, identity);
                await QueueDeadlineCapacityOperations.ReceiptAsync(database.ResolveOutcome(operation), receipt);
                await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, image, position, identity);
            }
            if (result.Value is ReceiveResult received)
            {
                if (received.Deliveries.IsEmpty)
                {
                    await ReplayResultAssertions.Same<ReceiveResult>(database.Apply(operation), result);
                    await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, image, position, identity);
                    await ReplayResultAssertions.Same<ReceiveResult>(database.ResolveOutcome(operation), result);
                }
                else
                {
                    await Assert.That(database.Apply(operation)).IsEqualTo(Result<object>.Fail(Errors.Problem(ErrorCode.StaleLease, StaleClaimDetail)));
                    await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, image, position, identity);
                    await Assert.That(database.ResolveOutcome(operation)).IsEqualTo(Result<object>.Fail(Errors.Problem(ErrorCode.StaleLease, StaleClaimDetail)));
                }
                await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, image, position, identity);
                var retained = OutcomeStoreOracle.Read(store, operation)
                    ?? throw new InvalidOperationException("The deadline receive outcome is absent after replay.");
                await Assert.That(OrleansSerialization.Serialize(retained))
                    .IsEquivalentTo(OrleansSerialization.Serialize(result), TUnit.Assertions.Enums.CollectionOrdering.Matching);
            }
            if (Errors.Code(result.Problem) is not null)
            {
                await Assert.That(database.Apply(operation)).IsEqualTo(result);
                await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, image, position, identity);
                await Assert.That(database.ResolveOutcome(operation)).IsEqualTo(result);
                await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, image, position, identity);
            }
            await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, image, position, identity);
        }
    }
}
