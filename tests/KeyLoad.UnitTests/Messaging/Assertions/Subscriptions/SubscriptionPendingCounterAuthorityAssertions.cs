using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using static KeyLoad.UnitTests.Features.Messaging.SubscriptionContinuationProtocol;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class SubscriptionPendingCounterAuthorityAssertions
{
    private const int Repeats = 2;
    private const string CorruptionDetail = "The subscription delivery window is inconsistent.";

    internal static async Task RefuseAsync(DatabaseEngine db, ZoneTreeStore store,
        ReceiveSubscriptionRequest fresh, ReceiveSubscriptionRequest empty, DateTimeOffset due, CancellationToken token)
    {
        var image = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = OrleansSerialization.Serialize(store.Identity);
        for (var repeat = FirstRepeat; repeat < Repeats; repeat++)
        {
            token.ThrowIfCancellationRequested();
            Failure(() => QueueWholeFlowStorage.Apply(db, OperationKind.ReceiveSubscription, fresh, fresh.RequestId, due));
            await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, image, position, identity);
            Failure(() => QueueWholeFlowStorage.Apply(db, OperationKind.ReceiveSubscription, empty, empty.RequestId, due));
            await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, image, position, identity);
            Failure(() => db.GetSubscription(Subject, fresh.Subscription));
            await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, image, position, identity);
        }
    }

    internal static async Task NackAsync(Result<object> actual, SubscriptionDeliveryCommand request, CommitToken token)
    {
        var expected = new CommitReceipt(request.CommandId, token, [new(nameof(DeliveryAction.Nack), request.Subscription.Source.Resource,
                NackedPosition.ToString(System.Globalization.CultureInfo.InvariantCulture), InitialLeaseVersion)], DurabilityProfile.ProcessDurable, 1);
        await Assert.That(Errors.Code(actual.Problem)).IsNull();
        await Assert.That(OrleansSerialization.Serialize(actual.Get<CommitReceipt>()).AsSpan()
            .SequenceEqual(OrleansSerialization.Serialize(expected))).IsTrue();
    }

    private static void Failure(Action action)
    {
        var error = Assert.ThrowsExactly<KeyLoadException>(action);
        if (error.Code != ErrorCode.Corruption || error.Message != CorruptionDetail)
        {
            throw new InvalidOperationException("The execution pending counter corruption was not refused exactly.", error);
        }
    }
}
