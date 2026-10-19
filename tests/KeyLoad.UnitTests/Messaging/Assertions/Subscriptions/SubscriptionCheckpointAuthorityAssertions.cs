using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ResourceExecution;
using ManagedCode.Communication;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class SubscriptionCheckpointAuthorityAssertions
{
    internal static string[] SourceImage(ZoneTreeStore store, PartitionRef partition)
    {
        string[] families = ["topic-event", "topic-head", "topic-event-id"];
        var prefixes = families.Select(f => Convert.ToHexString(KeySpace.Partition(f, partition))).ToArray();
        return QueueWholeFlowStorage.Bytes(store).Where(row => prefixes.Any(prefix =>
            row.StartsWith(prefix, StringComparison.Ordinal))).ToArray();
    }

    internal static async Task SourceAsync(DatabaseEngine db, ZoneTreeStore store, EventSourceRef source, string[] original)
    {
        await Assert.That(SourceImage(store, source.Partition)).IsEquivalentTo(original, CollectionOrdering.Matching);
        var events = db.ReadEventSource("root", new(source)).Events;
        await Assert.That(events.Select(e => e.Position)).IsEquivalentTo(new long[] { 1, 2, 3 }, CollectionOrdering.Matching);
        await Assert.That(events.Select(e => e.Source)).IsEquivalentTo(new[] { source, source, source }, CollectionOrdering.Matching);
        await Assert.That(events.Select(e => e.Data)).IsEquivalentTo(new EventData[]
        { new("event1", "Created", "{\"n\":1}"), new("event2", "Created", "{\"n\":2}"),
          new("event3", "Created", "{\"n\":3}") }, CollectionOrdering.Matching);
    }

    internal static async Task StatusAsync(DatabaseEngine db, SubscriptionRef group,
        long checkpoint, long issued, long tail)
    {
        var definition = new SubscriptionDefinition("root") { Policy = new() { MaxWindow = 3 } };
        await Assert.That(OrleansSerialization.Serialize(db.GetSubscription("root", group)).AsSpan()
            .SequenceEqual(OrleansSerialization.Serialize(new SubscriptionInfo(
                group, definition, 1, 1, checkpoint, issued, tail, false, null)))).IsTrue();
    }

    internal static async Task ReceiptAsync(Result<object> result, SubscriptionDeliveryCommand command, long position)
    {
        await Assert.That(Errors.Code(result.Problem)).IsNull();
        var receipt = result.Get<CommitReceipt>();
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Mutations).IsEquivalentTo(new MutationReceipt[]
        { new("Ack", "topic", position.ToString(System.Globalization.CultureInfo.InvariantCulture), 1) },
            CollectionOrdering.Matching);
    }

    internal static async Task CorruptionAsync(DatabaseEngine db, ZoneTreeStore store,
        SubscriptionDeliveryCommand command, CancellationToken token)
    {
        var bytes = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = OrleansSerialization.Serialize(store.Identity);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => SubscriptionCheckpointAuthorityFlow.Apply(
                db, OperationKind.SubscriptionDelivery, command, command.CommandId, token));
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
            await Assert.That(failure.Message).IsEqualTo("The subscription delivery window is inconsistent.");
            await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, bytes, position, identity);
        }
    }

    internal static async Task NoWindowAsync(ZoneTreeStore store, SubscriptionRef group)
    {
        var page = store.Read(view => view.Scan(SubscriptionCheckpointAuthorityFlow.WindowKey(group, null), 4));
        await Assert.That(page.Records).IsEmpty();
    }

    internal static async Task ReplayAsync(DatabaseEngine db, ZoneTreeStore store,
        SubscriptionDeliveryCommand command, Result<object> original, CancellationToken token)
    {
        var bytes = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = OrleansSerialization.Serialize(store.Identity);
        var result = SubscriptionCheckpointAuthorityFlow.Apply(db, OperationKind.SubscriptionDelivery,
            command, command.CommandId, token);
        await ReplayResultAssertions.Same<CommitReceipt>(result, original);
        await Assert.That(OrleansSerialization.Serialize(result).AsSpan()
            .SequenceEqual(OrleansSerialization.Serialize(original))).IsTrue();
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, bytes, position, identity);
    }

    internal static async Task SettledClaimAsync(DatabaseEngine db, ZoneTreeStore store,
        ReceiveSubscriptionRequest request, Result<object> original, CancellationToken token)
    {
        var key = KeySpace.PartitionOutcome(request.Subscription.Source.Partition, "root", request.RequestId);
        var saved = store.Read(view => view.ReadOwnedValue(key))
            ?? throw new InvalidOperationException("The genuine original subscription Receive outcome is missing.");
        await ReplayResultAssertions.Same<ReceiveSubscriptionResult>(
            OrleansSerialization.Deserialize<StoredOutcome>(saved).Result, original);
        var bytes = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = OrleansSerialization.Serialize(store.Identity);
        var result = SubscriptionCheckpointAuthorityFlow.Apply(db, OperationKind.ReceiveSubscription,
            request, request.RequestId, token);
        await RefusalAsync(result);
        var after = store.Read(view => view.ReadOwnedValue(key));
        await Assert.That(after is not null && after.AsSpan().SequenceEqual(saved)).IsTrue();
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, bytes, position, identity);
    }

    internal static async Task StaleAsync(DatabaseEngine db, ZoneTreeStore store,
        SubscriptionDeliveryCommand command, CancellationToken token)
    {
        var domain = SubscriptionFilterAssertions.Image(store, command.Subscription.Source.Partition);
        var result = SubscriptionCheckpointAuthorityFlow.Apply(db, OperationKind.SubscriptionDelivery,
            command, command.CommandId, token);
        await RefusalAsync(result);
        await Assert.That(SubscriptionFilterAssertions.Image(store, command.Subscription.Source.Partition))
            .IsEquivalentTo(domain, CollectionOrdering.Matching);
        var bytes = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = OrleansSerialization.Serialize(store.Identity);
        var replay = SubscriptionCheckpointAuthorityFlow.Apply(db, OperationKind.SubscriptionDelivery,
            command, command.CommandId, token);
        await RefusalAsync(replay);
        await Assert.That(OrleansSerialization.Serialize(replay).AsSpan()
            .SequenceEqual(OrleansSerialization.Serialize(result))).IsTrue();
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, bytes, position, identity);
    }

    private static async Task RefusalAsync(Result<object> result)
    {
        await Assert.That(Errors.Code(result.Problem)).IsEqualTo(ErrorCode.StaleLease);
        await Assert.That(result.Problem?.Detail).IsEqualTo("The subscription delivery lease is stale.");

        await Assert.That(result.Value).IsNull();
    }
}
