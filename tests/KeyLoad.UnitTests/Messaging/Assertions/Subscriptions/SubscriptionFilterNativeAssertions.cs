using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class SubscriptionFilterNativeAssertions
{
    internal static string[] Image(ZoneTreeStore store, PartitionRef partition)
    {
        string[] families = [SubscriptionFilterProtocol.State, SubscriptionFilterProtocol.Window,
            SubscriptionFilterProtocol.Completion, SubscriptionFilterProtocol.TopicRecords, SubscriptionFilterProtocol.TopicHead, SubscriptionFilterProtocol.TopicIdentities];
        var prefixes = families.Select(family => Convert.ToHexString(KeySpace.Partition(family, partition))).ToArray();
        return QueueWholeFlowStorage.Bytes(store).Where(row => prefixes.Any(prefix =>
            row.StartsWith(prefix, StringComparison.Ordinal))).ToArray();
    }

    internal static async Task RefusedAsync(DatabaseEngine db, ZoneTreeStore store,
        ConfigureSubscriptionRequest request, ErrorCode expected, CancellationToken token)
    {
        var before = Image(store, request.Subscription.Source.Partition);
        if (expected == ErrorCode.Corruption)
        {
            await CorruptionAsync(db, store, request, before, token);
            return;
        }
        var result = SubscriptionFilterColdTrial.Apply(db, OperationKind.ConfigureSubscription, request,
            request.CommandId, token);
        await Assert.That(result.Error).IsEqualTo(expected);
        await Assert.That(result.Json).IsNull();
        await Assert.That(Image(store, request.Subscription.Source.Partition)).IsEquivalentTo(before, CollectionOrdering.Matching);
        var replay = SubscriptionFilterColdTrial.Apply(db, OperationKind.ConfigureSubscription, request,
            request.CommandId, token);
        await Assert.That(replay.Error).IsEqualTo(expected);
        await Assert.That(replay.SafeDetail).IsEqualTo(result.SafeDetail);
    }

    private static async Task CorruptionAsync(DatabaseEngine db, ZoneTreeStore store,
        ConfigureSubscriptionRequest request, string[] before, CancellationToken token)
    {
        var position = store.Position;
        var original = QueueWholeFlowStorage.Bytes(store);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => SubscriptionFilterColdTrial.Apply(
                db, OperationKind.ConfigureSubscription, request, request.CommandId, token));
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
            await Assert.That(failure.Message).IsEqualTo("The subscription delivery window is inconsistent.");
            await Assert.That(store.Position).IsEqualTo(position);
            await Assert.That(QueueWholeFlowStorage.Bytes(store).SequenceEqual(original)).IsTrue();
            await Assert.That(Image(store, request.Subscription.Source.Partition))
                .IsEquivalentTo(before, CollectionOrdering.Matching);
        }
    }

    internal static async Task StatusAsync(DatabaseEngine db, SubscriptionRef subscription,
        long generation, long checkpoint, bool paused)
    {
        var actual = db.GetSubscription(SubscriptionFilterProtocol.Root, subscription);
        await Assert.That(actual.Generation).IsEqualTo(generation);
        await Assert.That(actual.OwnershipEpoch).IsEqualTo(generation);
        await Assert.That(actual.Checkpoint).IsEqualTo(checkpoint);
        await Assert.That(actual.Paused).IsEqualTo(paused);
    }
}
