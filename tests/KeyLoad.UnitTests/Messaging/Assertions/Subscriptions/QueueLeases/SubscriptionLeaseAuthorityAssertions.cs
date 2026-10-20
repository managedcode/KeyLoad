using System.Text.Json;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ResourceExecution;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
using TUnit.Assertions.Enums;
namespace KeyLoad.UnitTests.Features.Messaging;

internal static class SubscriptionLeaseAuthorityAssertions
{
    internal static async Task RefusalAsync(DatabaseEngine db, ZoneTreeStore store,
        Command<DatabaseCommand> claim, Result<object> original, Command<DatabaseCommand> attempt)
    {
        var bytes = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = OrleansSerialization.Serialize(store.Identity);
        var outcomeKey = KeySpace.PartitionOutcome(original.Get<ReceiveSubscriptionResult>().Status.Subscription.Source.Partition,
            "root", claim.CommandId);
        var saved = store.Read(view => view.ReadOwnedValue(outcomeKey))
            ?? throw new InvalidOperationException("The genuine original claim history is absent.");
        var subscription = original.Get<ReceiveSubscriptionResult>().Status.Subscription;
        var receive = new ReceiveSubscriptionRequest(Guid.NewGuid(), subscription);
        var freshClaim = db.CreateCommand(OperationKind.ReceiveSubscription, receive.RequestId, "root", new DateTimeOffset(claim.Timestamp), JsonSerializer.Serialize(receive, JsonDefaults.Options));
        for (var repeat = 0; repeat < 2; repeat++)
        {
            foreach (var operation in new[] { claim, attempt, freshClaim })
            {
                var failure = Assert.ThrowsExactly<KeyLoadException>(() => db.Apply(operation));
                await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
                await Assert.That(failure.Message).IsEqualTo("The subscription delivery window is inconsistent.");
                await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, bytes, position, identity);
            }
            await Assert.That(store.Read(view => view.GetRecord<StoredOutcome>(
                KeySpace.PartitionOutcome(subscription.Source.Partition, "root", freshClaim.CommandId)))).IsNull();
            await ReplayResultAssertions.Same<ReceiveSubscriptionResult>(
                OrleansSerialization.Deserialize<StoredOutcome>(saved).Result, original);
            await Assert.That(store.Read(view => view.ReadOwnedValue(outcomeKey))!.AsSpan().SequenceEqual(saved)).IsTrue();
            await Assert.That(store.Read(view => view.GetRecord<StoredOutcome>(
                KeySpace.PartitionOutcome(original.Get<ReceiveSubscriptionResult>().Status.Subscription.Source.Partition,
                    "root", attempt.CommandId)))).IsNull();
        }
    }

    internal static async Task ReceiptAsync(Result<object> result, SubscriptionDeliveryCommand command,
        long position, long leaseVersion)
    {
        await Assert.That(Errors.Code(result.Problem)).IsNull();
        var receipt = result.Get<CommitReceipt>();
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Mutations).IsEquivalentTo(new MutationReceipt[]
        { new(command.Action.ToString(), "topic", position.ToString(System.Globalization.CultureInfo.InvariantCulture),
            leaseVersion) }, CollectionOrdering.Matching);
    }

    internal static async Task SettledAsync(DatabaseEngine db, ZoneTreeStore store, SubscriptionRef group,
        Command<DatabaseCommand> claim, Result<object> original, List<(Command<DatabaseCommand> Operation, Result<object> Result)> receipts,
        Command<DatabaseCommand>? reclaimed, Result<object>? reclaimResult)
    {
        await SubscriptionCheckpointAuthorityAssertions.StatusAsync(db, group, 3, 3, 3);
        await SubscriptionCheckpointAuthorityAssertions.NoWindowAsync(store, group);
        foreach (var receipt in receipts)
        {
            var bytes = QueueWholeFlowStorage.Bytes(store);
            var position = store.Position;
            var identity = OrleansSerialization.Serialize(store.Identity);
            var replay = db.Apply(receipt.Operation);
            await ReplayResultAssertions.Same<CommitReceipt>(replay, receipt.Result);
            await Assert.That(OrleansSerialization.Serialize(replay).AsSpan()
                .SequenceEqual(OrleansSerialization.Serialize(receipt.Result))).IsTrue();
            await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, bytes, position, identity);
        }
        await SettledClaimAsync(db, store, group, claim, original);
        if (reclaimed is not null)
        {
            var saved = reclaimResult ?? throw new InvalidOperationException("The genuine reclaimed operation result is absent.");
            await SettledClaimAsync(db, store, group, reclaimed, saved);
        }
    }

    private static async Task SettledClaimAsync(DatabaseEngine db, ZoneTreeStore store, SubscriptionRef group,
        Command<DatabaseCommand> claim, Result<object> original)
    {
        var key = KeySpace.PartitionOutcome(group.Source.Partition, "root", claim.CommandId);
        var saved = store.Read(view => view.ReadOwnedValue(key))
            ?? throw new InvalidOperationException("The original successful Receive history is absent.");
        await ReplayResultAssertions.Same<ReceiveSubscriptionResult>(
            OrleansSerialization.Deserialize<StoredOutcome>(saved).Result, original);
        var bytes = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = OrleansSerialization.Serialize(store.Identity);
        var replay = db.Apply(claim);
        await Assert.That(Errors.Code(replay.Problem)).IsEqualTo(ErrorCode.StaleLease);
        await Assert.That(replay.Problem?.Detail).IsEqualTo("The subscription delivery lease is stale.");

        await Assert.That(replay.Value).IsNull();
        await Assert.That(store.Read(view => view.ReadOwnedValue(key))!.AsSpan().SequenceEqual(saved)).IsTrue();
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, bytes, position, identity);
    }
}
