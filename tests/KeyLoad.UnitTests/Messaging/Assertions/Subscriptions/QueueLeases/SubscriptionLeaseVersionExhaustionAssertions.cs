using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Messaging;

internal static class SubscriptionLeaseVersionExhaustionAssertions
{
    internal const string Exhausted = "The subscription delivery lease version is exhausted.";
    private const string Subject = "root";
    private const long NextPosition = 1;

    internal static async Task<Result<object>> FailAsync(DatabaseEngine db, ZoneTreeStore store,
        ReceiveSubscriptionRequest request, DateTimeOffset due, string detail)
    {
        var image = Domain(store, request);
        var position = store.Position;
        var canonical = db.LastApplied;
        var result = QueueWholeFlowStorage.Apply(db, OperationKind.ReceiveSubscription, request, request.RequestId, due);
        await Assert.That(Errors.Code(result.Problem).Value).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(result.Problem?.Detail).IsEqualTo(detail);

        await Assert.That(result.Value).IsNull();
        await Assert.That(store.Position).IsEqualTo(position + NextPosition);
        await Assert.That(db.LastApplied).IsEqualTo(canonical + NextPosition);
        await RequireLocatorAsync(store, request);
        await Assert.That(Domain(store, request)).IsEquivalentTo(image, CollectionOrdering.Matching);
        await Assert.That(QueueWholeFlowStorage.Clock(store)).IsEqualTo(due);
        await Assert.That(OutcomeStoreOracle.ReadPartition(store, request.Subscription.Source.Partition, Subject, request.RequestId))
            .IsEqualTo(result);
        await ReplayAsync(db, store, request, result, due);
        return result;
    }

    internal static async Task ReplayAsync(DatabaseEngine db, ZoneTreeStore store,
        ReceiveSubscriptionRequest request, Result<object> original, DateTimeOffset due)
    {
        var image = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = OrleansSerialization.Serialize(store.Identity);
        var actual = QueueWholeFlowStorage.Apply(db, OperationKind.ReceiveSubscription, request, request.RequestId, due);
        await Assert.That(actual).IsEqualTo(original);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, image, position, identity);
    }

    private static string[] Domain(ZoneTreeStore store, ReceiveSubscriptionRequest request)
    {
        var outcome = Convert.ToHexString(KeySpace.PartitionOutcome(request.Subscription.Source.Partition, Subject, request.RequestId));
        var locator = Convert.ToHexString(KeySpace.OutcomeLocator(request.Subscription.Source.Partition, Subject, request.RequestId));
        var clock = Convert.ToHexString(KeySpace.ClockBytes);
        var applied = Convert.ToHexString(KeySpace.AppliedBytes);
        var lastCommand = Convert.ToHexString(KeySpace.LastCommandBytes);
        return QueueWholeFlowStorage.Bytes(store).Where(row => !row.StartsWith(outcome + ":", StringComparison.Ordinal)
            && !row.StartsWith(locator + ":", StringComparison.Ordinal)
            && !row.StartsWith(clock + ":", StringComparison.Ordinal)
            && !row.StartsWith(applied + ":", StringComparison.Ordinal)
            && !row.StartsWith(lastCommand + ":", StringComparison.Ordinal)).ToArray();
    }

    private static async Task RequireLocatorAsync(ZoneTreeStore store, ReceiveSubscriptionRequest request)
    {
        var partition = request.Subscription.Source.Partition;
        var key = KeySpace.OutcomeLocator(partition, Subject, request.RequestId);
        var expected = KeySpace.PartitionOutcome(partition, Subject, request.RequestId);
        var actual = store.Read(view => view.ReadOwnedValue(key));
        await Assert.That(actual).IsNotNull();
        await Assert.That(actual!.AsSpan().SequenceEqual(expected)).IsTrue();
    }
}
