using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ResourceExecution;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class SubscriptionPausedDrainAssertions
{
    internal static Task StatusAsync(DatabaseEngine db, SubscriptionRef group, long checkpoint, long tail, bool paused)
        => SubscriptionParkedRecoveryAssertions.StatusAsync(db, group, new() { MaxWindow = 3 },
            1, checkpoint, tail, tail, paused, null);

    internal static async Task RefuseAsync(DatabaseEngine db, ZoneTreeStore store, SubscriptionRef group, ReplicatedOperation operation,
        List<(ReplicatedOperation Operation, OperationResult Result, byte[] Saved)> history)
    {
        var domain = SubscriptionProcessingAtomicAssertions.DomainImage(store, group, operation.Id);
        var position = store.Position;
        var failure = db.Apply(operation);
        await FailureAsync(failure);
        await SubscriptionProcessingAtomicAssertions.OwnOutcomeOnlyAsync(store, group, operation, domain, position + 1);
        history.Add((operation, failure, SubscriptionProcessingAtomicAssertions.OutcomeBytes(store, group, operation.Id)));
        await ReplayAsync(db, store, group, history);
    }

    internal static async Task ReplayAsync(DatabaseEngine db, ZoneTreeStore store, SubscriptionRef group,
        List<(ReplicatedOperation Operation, OperationResult Result, byte[] Saved)> history)
    {
        var bytes = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = NativeSerialization.Serialize(store.Identity);
        foreach (var entry in history)
        {
            var replay = db.Apply(entry.Operation);
            if (entry.Result.Error is not null)
            {
                await FailureAsync(replay);
                await Assert.That(JsonSerializer.Serialize(replay, JsonDefaults.Options))
                    .IsEqualTo(JsonSerializer.Serialize(entry.Result, JsonDefaults.Options));
            }
            else if (entry.Result.NativeValue is SubscriptionInfo)
            { await NativeReplayResultAssertions.Same<SubscriptionInfo>(replay, entry.Result); }
            else
            { await NativeReplayResultAssertions.Same<CommitReceipt>(replay, entry.Result); }
            var saved = SubscriptionProcessingAtomicAssertions.OutcomeBytes(store, group, entry.Operation.Id);
            await Assert.That(saved.AsSpan().SequenceEqual(entry.Saved)).IsTrue();
            await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, bytes, position, identity);
        }
    }

    internal static async Task OriginalClaimAsync(DatabaseEngine db, ZoneTreeStore store, ReceiveSubscriptionRequest request,
        OperationResult original, byte[] saved, CancellationToken token)
    {
        var retained = SubscriptionProcessingAtomicAssertions.OutcomeBytes(store, request.Subscription, request.RequestId);
        await Assert.That(retained.AsSpan().SequenceEqual(saved)).IsTrue();
        await SubscriptionCheckpointAuthorityAssertions.SettledClaimAsync(db, store, request, original, token);
    }

    internal static async Task ReceiptAsync(OperationResult result, SubscriptionDeliveryCommand command, long position)
    {
        await Assert.That(result.Error).IsNull();
        var receipt = result.Get<CommitReceipt>();
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Mutations).IsEquivalentTo(
            new MutationReceipt[] { new(command.Action.ToString(), command.Subscription.Source.Resource,
                position.ToString(global::System.Globalization.CultureInfo.InvariantCulture), 1L) }, CollectionOrdering.Matching);
    }

    private static async Task FailureAsync(OperationResult result)
    {
        await Assert.That(result.Error).IsEqualTo(ErrorCode.DispatchPaused);
        await Assert.That(result.SafeDetail).IsEqualTo("Subscription delivery is paused.");
        await Assert.That(result.Json).IsNull();
        await Assert.That(result.NativeValue).IsNull();
    }
}

