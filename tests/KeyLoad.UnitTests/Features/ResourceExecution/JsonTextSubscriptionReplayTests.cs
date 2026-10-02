using System.Text.Json;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class JsonTextSubscriptionReplayTests
{
    private const string TopicName = "topic";
    private const string GroupName = "worker";
    private const string RootPrincipal = "root";
    private const string EventType = "Created";
    private const string EventBody = "{}";
    private const string FirstEventId = "first";
    private const string SecondEventId = "second";
    private const string ThirdEventId = "third";
    private const string RawSpellingSuffix = " ";
    private const int MultiDeliveryCount = 3;

    [Test]
    public async Task AcMp006MultiSubscriptionReplayRetainsDeliveryIdentityAndLeaseFencing()
    {
        using var db = new TestDatabase();
        db.Configure(TopicName, ResourceKind.Topic);
        db.Commit(new PublishTopic(TopicName,
            [new(FirstEventId, EventType, EventBody), new(SecondEventId, EventType, EventBody), new(ThirdEventId, EventType, EventBody)]));
        var subscription = Configure(db);
        var now = TimeProvider.System.GetUtcNow().AddSeconds(1);
        var id = Guid.NewGuid();
        var request = new ReceiveSubscriptionRequest(id, subscription, MaxEvents: MultiDeliveryCount);
        var original = db.Submit(OperationKind.ReceiveSubscription, request, id: id, time: now);
        var replay = db.Submit(OperationKind.ReceiveSubscription, request, id: id, time: now);
        var deliveries = original.Get<ReceiveSubscriptionResult>().Deliveries;
        var changedSpelling = JsonSerializer.Serialize(request, JsonDefaults.Options) + RawSpellingSuffix;
        var conflict = db.Database.Apply(new(id, OperationKind.ReceiveSubscription,
            RootPrincipal, now, changedSpelling));

        await Assert.That(deliveries.Length).IsEqualTo(MultiDeliveryCount);
        await Assert.That(deliveries.Select(delivery => delivery.Event.Data.EventId))
            .IsEquivalentTo(new[] { FirstEventId, SecondEventId, ThirdEventId }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(replay).IsEqualTo(original);
        await Assert.That(conflict.Error).IsEqualTo(ErrorCode.Conflict);

        var ack = Guid.NewGuid();
        db.Submit(OperationKind.SubscriptionDelivery,
            new SubscriptionDeliveryCommand(ack, subscription, deliveries[0].Token, DeliveryAction.Ack),
            id: ack, time: now).Get<CommitReceipt>();
        await Assert.That(db.Submit(OperationKind.ReceiveSubscription, request, id: id, time: now).Error)
            .IsEqualTo(ErrorCode.StaleLease);
    }

    [Test]
    public async Task AcMp006EmptySubscriptionReplayRechecksGenerationBeforeReturningCachedResult()
    {
        using var db = new TestDatabase();
        db.Configure(TopicName, ResourceKind.Topic);
        var subscription = Configure(db);
        var now = TimeProvider.System.GetUtcNow().AddSeconds(1);
        var id = Guid.NewGuid();
        var request = new ReceiveSubscriptionRequest(id, subscription);
        var original = db.Submit(OperationKind.ReceiveSubscription, request, id: id, time: now);
        var replay = db.Submit(OperationKind.ReceiveSubscription, request, id: id, time: now);

        await Assert.That(original.Get<ReceiveSubscriptionResult>().Deliveries).IsEmpty();
        await Assert.That(replay).IsEqualTo(original);

        var seek = Guid.NewGuid();
        db.Submit(OperationKind.SeekSubscription,
            new SeekSubscriptionRequest(seek, subscription, 1, SubscriptionStart.FromBeginning),
            id: seek, time: now.AddSeconds(1)).Get<SubscriptionInfo>();
        await Assert.That(db.Submit(OperationKind.ReceiveSubscription, request, id: id,
            time: now.AddSeconds(1)).Error).IsEqualTo(ErrorCode.TokenInvalidated);
    }

    private static SubscriptionRef Configure(TestDatabase db)
    {
        var subscription = new SubscriptionRef(new(db.Partition, TopicName, EventSourceKind.Topic), GroupName);
        var id = Guid.NewGuid();
        db.Submit(OperationKind.ConfigureSubscription,
            new ConfigureSubscriptionRequest(id, subscription, new(RootPrincipal)), id: id).Get<SubscriptionInfo>();
        return subscription;
    }
}
