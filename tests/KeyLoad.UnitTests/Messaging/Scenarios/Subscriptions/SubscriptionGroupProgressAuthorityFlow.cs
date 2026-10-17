using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class SubscriptionGroupProgressAuthorityFlow
{
    private const string Topic = "topic";
    private const int Window = 3;
    private const int LastOriginalIndex = 2;
    private const long InvalidNegativeProgress = -1;
    private const long FuturePosition = 4;
    private const long TruncatedIssuedPosition = 2;
    private const int CorruptWindow = 2;

    internal static async Task RunAsync(SubscriptionGroupProgressFault fault, CancellationToken token)
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
        var expectedClaim = SubscriptionGroupProgressAuthorityAssertions.NextToken(fixture.Database, fixture.Store, group);
        var original = SubscriptionCheckpointAuthorityFlow.Apply(fixture.Database,
            OperationKind.ReceiveSubscription, request, request.RequestId, token);
        var claim = original.Get<ReceiveSubscriptionResult>();
        await SubscriptionGroupProgressAuthorityAssertions.ClaimAsync(claim, request, expectedClaim);
        var ack = new SubscriptionDeliveryCommand(Guid.NewGuid(), group, claim.Deliveries[LastOriginalIndex].Token, DeliveryAction.Ack);
        var emptyRequest = new ReceiveSubscriptionRequest(Guid.NewGuid(), group);
        var emptyResult = SubscriptionCheckpointAuthorityFlow.Apply(fixture.Database,
            OperationKind.ReceiveSubscription, emptyRequest, emptyRequest.RequestId, token);
        await Assert.That(emptyResult.Get<ReceiveSubscriptionResult>().Deliveries).IsEmpty();
        var key = Key(group);
        var saved = fixture.Store.Read(view => view.ReadOwnedValue(key))
            ?? throw new InvalidOperationException("The actual configured subscription group is missing.");
        var state = NativeSerialization.Deserialize<GroupState>(saved);
        fixture.Store.Commit((tx, _) =>
        {
            tx.PutRecord(key, Inject(state, fault));
            return true;
        });
        var fresh = new ReceiveSubscriptionRequest(Guid.NewGuid(), group);
        await SubscriptionGroupProgressAuthorityAssertions.RefuseAsync(fixture.Database, fixture.Store, fresh, request, emptyRequest, ack, token);
        var image = QueueWholeFlowStorage.Bytes(fixture.Store);
        var position = fixture.Store.Position;
        var identity = NativeSerialization.Serialize(fixture.Store.Identity);
        fixture.Store.Dispose();
        using var first = new ZoneTreeStore(new(fixture.Directory), UnitExecutionOptions.StorageExecution(),
            UnitExecutionOptions.PointCacheExecution());
        var db = QueueWholeFlowStorage.Open(first);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(first, image, position, identity);
        await SubscriptionGroupProgressAuthorityAssertions.RefuseAsync(db, first, fresh, request, emptyRequest, ack, token);
        first.Commit((tx, _) =>
        {
            tx.Put(key, saved);
            return true;
        });
        await NativeReplayResultAssertions.Same<ReceiveSubscriptionResult>(SubscriptionCheckpointAuthorityFlow.Apply(
            db, OperationKind.ReceiveSubscription, request, request.RequestId, token), original);
        await NativeReplayResultAssertions.Same<ReceiveSubscriptionResult>(SubscriptionCheckpointAuthorityFlow.Apply(
            db, OperationKind.ReceiveSubscription, emptyRequest, emptyRequest.RequestId, token), emptyResult);
        await SubscriptionGroupProgressAuthorityContinuation.RunAsync(db, first, fixture.Directory,
            group, fresh, ack, claim, token);
    }

    private static GroupState Inject(GroupState state, SubscriptionGroupProgressFault fault)
        => fault switch
        {
            SubscriptionGroupProgressFault.NegativeCheckpoint => state with { Checkpoint = InvalidNegativeProgress, IssuedPosition = InvalidNegativeProgress },
            SubscriptionGroupProgressFault.NegativeIssuedPosition => state with { IssuedPosition = InvalidNegativeProgress },
            SubscriptionGroupProgressFault.SignedOverflow => state with { Checkpoint = long.MinValue, IssuedPosition = long.MaxValue },
            SubscriptionGroupProgressFault.IssuedBeforeExistingRow => state with { IssuedPosition = TruncatedIssuedPosition },
            SubscriptionGroupProgressFault.FutureCheckpoint => state with { Checkpoint = FuturePosition, IssuedPosition = FuturePosition },
            SubscriptionGroupProgressFault.IssuedBeforeCheckpoint => state with { Checkpoint = 1, IssuedPosition = 0 },
            SubscriptionGroupProgressFault.IssuedBeyondTail => state with { IssuedPosition = FuturePosition },
            SubscriptionGroupProgressFault.WindowExceeded => state with
            {
                Definition = state.Definition with
                {
                    Policy = state.Definition.Policy with
                    {
                        MaxWindow = CorruptWindow
                    }
                }
            },
            _ => throw new ArgumentOutOfRangeException(nameof(fault))
        };

    private static byte[] Key(SubscriptionRef group)
        => KeySpace.Partition("subscription", group.Source.Partition, group.Source.Resource,
            group.Source.Kind.ToString(), group.Source.StreamId, group.Source.Generation, group.GroupId);
}
