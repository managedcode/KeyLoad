using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class SubscriptionWindowBudgetWholeFlow
{
    private const string Topic = "topic";
    private const int Window = 3;
    private const int LastDelivery = 2;

    internal static async Task RunAsync(SubscriptionWindowBudgetFault fault, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var fixture = new TestDatabase();
        fixture.Configure(Topic, ResourceKind.Topic);
        SubscriptionTestActions.Publish(fixture);
        var group = SubscriptionTestActions.Configure(fixture, policy: new()
        {
            MaxWindow = Window
        });
        var request = new ReceiveSubscriptionRequest(Guid.NewGuid(), group, MaxEvents: Window);
        var expected = SubscriptionGroupProgressAuthorityAssertions.NextToken(fixture.Database, fixture.Store, group);
        var original = SubscriptionCheckpointAuthorityFlow.Apply(fixture.Database,
            OperationKind.ReceiveSubscription, request, request.RequestId, token);
        var claim = original.Get<ReceiveSubscriptionResult>();
        await SubscriptionGroupProgressAuthorityAssertions.ClaimAsync(claim, request, expected);
        await SubscriptionWindowBudgetAssertions.RefuseAsync(fixture.Store, group, request, original, fault, token);
        var image = QueueWholeFlowStorage.Bytes(fixture.Store);
        var position = fixture.Store.Position;
        var identity = OrleansSerialization.Serialize(fixture.Store.Identity);
        fixture.Store.Dispose();
        using var first = new ZoneTreeStore(new(fixture.Directory), UnitExecutionOptions.StorageExecution(),
            UnitExecutionOptions.PointCacheExecution());
        var db = QueueWholeFlowStorage.Open(first);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(first, image, position, identity);
        await SubscriptionWindowBudgetAssertions.RefuseAsync(first, group, request, original, fault, token);
        await ReplayResultAssertions.Same<ReceiveSubscriptionResult>(SubscriptionCheckpointAuthorityFlow.Apply(
            db, OperationKind.ReceiveSubscription, request, request.RequestId, token), original);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(first, image, position, identity);
        var fresh = new ReceiveSubscriptionRequest(Guid.NewGuid(), group);
        var ack = new SubscriptionDeliveryCommand(Guid.NewGuid(), group,
            claim.Deliveries[LastDelivery].Token, DeliveryAction.Ack);
        await SubscriptionGroupProgressAuthorityContinuation.RunAsync(db, first, fixture.Directory,
            group, fresh, ack, claim, token);
    }
}
