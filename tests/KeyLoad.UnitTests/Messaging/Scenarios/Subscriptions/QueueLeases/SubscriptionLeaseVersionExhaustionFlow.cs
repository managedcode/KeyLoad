using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using static KeyLoad.UnitTests.Messaging.SubscriptionContinuationProtocol;

namespace KeyLoad.UnitTests.Messaging;

internal static class SubscriptionLeaseVersionExhaustionFlow
{
    private const string Topic = "topic";
    private const int Window = 3;
    private const long FirstPosition = 1;
    private const int FirstDelivery = 0;
    private const int SmallByteBudget = 1;

    internal static async Task RunAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var fixture = new TestDatabase();
        fixture.Configure(Topic, ResourceKind.Topic);
        SubscriptionTestActions.Publish(fixture);
        var group = SubscriptionTestActions.Configure(fixture, policy: new()
        {
            MaxWindow = Window
        });
        var claim = SubscriptionTestActions.Receive(fixture, group);
        var nack = new SubscriptionDeliveryCommand(Guid.NewGuid(), group,
            claim.Deliveries[FirstDelivery].Token, DeliveryAction.Nack);
        var expectedNack = SubscriptionGroupProgressAuthorityAssertions.NextToken(fixture.Database, group);
        var nacked = SubscriptionCheckpointAuthorityFlow.Apply(fixture.Database,
            OperationKind.SubscriptionDelivery, nack, nack.CommandId, token);
        await SubscriptionPendingCounterAuthorityAssertions.NackAsync(nacked, nack, expectedNack);
        var key = SubscriptionCheckpointAuthorityFlow.WindowKey(group, FirstPosition);
        var saved = fixture.Store.Read(view => view.ReadOwnedValue(key))
            ?? throw new InvalidOperationException(MissingPending);
        var pending = OrleansSerialization.Deserialize<GroupDelivery>(saved);
        await Assert.That(pending.State).IsEqualTo(GroupDeliveryState.Pending);
        var due = pending.AvailableAt!.Value;
        fixture.Store.Commit((tx, _) =>
        {
            tx.PutRecord(key, pending with
            {
                LeaseVersion = long.MaxValue
            });
            return true;
        });
        var byteRequest = new ReceiveSubscriptionRequest(Guid.NewGuid(), group, MaxEvents: SingleEventCount, MaxBytes: SmallByteBudget);
        await SubscriptionLeaseVersionExhaustionAssertions.FailAsync(fixture.Database, fixture.Store, byteRequest, due,
            ByteBudgetDetail);
        var request = new ReceiveSubscriptionRequest(Guid.NewGuid(), group, MaxEvents: SingleEventCount);
        var failed = await SubscriptionLeaseVersionExhaustionAssertions.FailAsync(fixture.Database, fixture.Store, request,
            due, SubscriptionLeaseVersionExhaustionAssertions.Exhausted);
        await ColdAsync(fixture, group, claim, request, failed, byteRequest, due, key, saved, nack, nacked, token);
    }

    private static async Task ColdAsync(TestDatabase fixture, SubscriptionRef group, ReceiveSubscriptionResult claim,
        ReceiveSubscriptionRequest request, Result<object> failed, ReceiveSubscriptionRequest byteRequest,
        DateTimeOffset due, byte[] key, byte[] saved, SubscriptionDeliveryCommand nack,
        Result<object> nacked, CancellationToken token)
    {
        var image = QueueWholeFlowStorage.Bytes(fixture.Store);
        var position = fixture.Store.Position;
        var identity = OrleansSerialization.Serialize(fixture.Store.Identity);
        fixture.Store.Dispose();
        using var cold = new ZoneTreeStore(new(fixture.Directory), UnitExecutionOptions.StorageExecution(),
            UnitExecutionOptions.PointCacheExecution());
        var db = QueueWholeFlowStorage.Open(cold);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(cold, image, position, identity);
        await SubscriptionLeaseVersionExhaustionAssertions.ReplayAsync(db, cold, request, failed, due);
        cold.Commit((tx, _) =>
        {
            tx.Put(key, saved);
            return true;
        });
        await SubscriptionLeaseVersionExhaustionAssertions.ReplayAsync(db, cold, request, failed, due);
        var byteOutcome = OutcomeStoreOracle.ReadPartition(cold, group.Source.Partition, Subject, byteRequest.RequestId)
            ?? throw new InvalidOperationException(MissingByteOutcome);
        await SubscriptionLeaseVersionExhaustionAssertions.ReplayAsync(db, cold, byteRequest, byteOutcome, due);
        var healthy = request with
        {
            RequestId = Guid.NewGuid()
        };
        await SubscriptionPendingCounterAuthorityContinuation.RunAsync(db, cold, fixture.Directory,
            group, claim, healthy, due,
            [new(OperationKind.SubscriptionDelivery, nack, nack.CommandId, nacked),
                new(OperationKind.ReceiveSubscription, request, request.RequestId, failed),
                new(OperationKind.ReceiveSubscription, byteRequest, byteRequest.RequestId, byteOutcome)], token);
    }
}
