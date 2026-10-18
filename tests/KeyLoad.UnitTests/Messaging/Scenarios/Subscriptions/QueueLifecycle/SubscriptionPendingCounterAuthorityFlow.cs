using ManagedCode.Communication;
using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using static KeyLoad.UnitTests.Features.Messaging.SubscriptionContinuationProtocol;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class SubscriptionPendingCounterAuthorityFlow
{
    private const string Topic = "topic";
    private const int Window = 3;
    private const int FirstDelivery = 0;
    private const long FirstPosition = 1;
    private const int InvalidAttempts = -2;
    private const long InvalidVersion = -1;

    internal static async Task RunAsync(SubscriptionPendingCounterFault fault, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var fixture = new TestDatabase();
        fixture.Configure(Topic, ResourceKind.Topic);
        SubscriptionTestActions.Publish(fixture);
        var group = SubscriptionTestActions.Configure(fixture, policy: new()
        {
            MaxWindow = Window
        });
        var received = SubscriptionTestActions.Receive(fixture, group);
        var emptyRequest = new ReceiveSubscriptionRequest(Guid.NewGuid(), group);
        var empty = SubscriptionCheckpointAuthorityFlow.Apply(fixture.Database,
            OperationKind.ReceiveSubscription, emptyRequest, emptyRequest.RequestId, token);
        await Assert.That(empty.Get<ReceiveSubscriptionResult>().Deliveries).IsEmpty();
        var nack = new SubscriptionDeliveryCommand(Guid.NewGuid(), group,
            received.Deliveries[FirstDelivery].Token, DeliveryAction.Nack);
        var expected = SubscriptionGroupProgressAuthorityAssertions.NextToken(fixture.Database, fixture.Store, group);
        var nacked = SubscriptionCheckpointAuthorityFlow.Apply(fixture.Database,
            OperationKind.SubscriptionDelivery, nack, nack.CommandId, token);
        await SubscriptionPendingCounterAuthorityAssertions.NackAsync(nacked, nack, expected);
        var key = SubscriptionCheckpointAuthorityFlow.WindowKey(group, FirstPosition);
        var saved = fixture.Store.Read(view => view.ReadOwnedValue(key))
            ?? throw new InvalidOperationException("The genuine native NACKed pending row is missing.");
        var pending = NativeSerialization.Deserialize<GroupDelivery>(saved);
        await Assert.That(pending.State).IsEqualTo(GroupDeliveryState.Pending);
        var due = pending.AvailableAt!.Value;
        fixture.Store.Commit((tx, _) =>
        {
            tx.PutRecord(key, fault == SubscriptionPendingCounterFault.NegativeAttempts
                ? pending with
                {
                    Attempts = InvalidAttempts
                }
                : pending with
                {
                    LeaseVersion = InvalidVersion
                });
            return true;
        });
        var fresh = new ReceiveSubscriptionRequest(Guid.NewGuid(), group, MaxEvents: SingleEventCount);
        await SubscriptionPendingCounterAuthorityAssertions.RefuseAsync(fixture.Database, fixture.Store,
            fresh, emptyRequest, due, token);
        await ColdAsync(fixture, group, received, nack, nacked, fresh, emptyRequest, empty,
            due, key, saved, token);
    }

    private static async Task ColdAsync(TestDatabase fixture, SubscriptionRef group, ReceiveSubscriptionResult received,
        SubscriptionDeliveryCommand nack, Result<object> nacked, ReceiveSubscriptionRequest fresh,
        ReceiveSubscriptionRequest emptyRequest, Result<object> empty, DateTimeOffset due,
        byte[] key, byte[] saved, CancellationToken token)
    {
        var image = QueueWholeFlowStorage.Bytes(fixture.Store);
        var position = fixture.Store.Position;
        var identity = NativeSerialization.Serialize(fixture.Store.Identity);
        fixture.Store.Dispose();
        using var cold = new ZoneTreeStore(new(fixture.Directory), UnitExecutionOptions.StorageExecution(),
            UnitExecutionOptions.PointCacheExecution());
        var db = QueueWholeFlowStorage.Open(cold);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(cold, image, position, identity);
        await SubscriptionPendingCounterAuthorityAssertions.RefuseAsync(db, cold, fresh, emptyRequest, due, token);
        cold.Commit((tx, _) =>
        {
            tx.Put(key, saved);
            return true;
        });
        await SubscriptionContinuationAssertions.ReplayAsync(db, cold,
            new(OperationKind.SubscriptionDelivery, nack, nack.CommandId, nacked), due, token);
        await SubscriptionContinuationAssertions.ReplayAsync(db, cold,
            new(OperationKind.ReceiveSubscription, emptyRequest, emptyRequest.RequestId, empty), due, token);
        var originalRequest = new ReceiveSubscriptionRequest(received.RequestId, group, MaxEvents: Window);
        var stable = QueueWholeFlowStorage.Bytes(cold);
        var stablePosition = cold.Position;
        var stale = QueueWholeFlowStorage.Apply(db, OperationKind.ReceiveSubscription,
            originalRequest, originalRequest.RequestId, due);
        await Assert.That(Errors.Code(stale.Problem)).IsEqualTo(ErrorCode.StaleLease);
        await Assert.That(stale.Problem?.Detail).IsEqualTo(StaleDetail);
        
        await Assert.That(stale.Value).IsNull();
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(cold, stable, stablePosition, identity);
        await SubscriptionPendingCounterAuthorityContinuation.RunAsync(db, cold, fixture.Directory,
            group, received, fresh, due,
            [new(OperationKind.SubscriptionDelivery, nack, nack.CommandId, nacked),
                new(OperationKind.ReceiveSubscription, emptyRequest, emptyRequest.RequestId, empty)], token);
    }
}
