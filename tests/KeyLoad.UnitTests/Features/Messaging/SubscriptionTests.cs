using TUnit.Assertions.Enums;

using static KeyLoad.UnitTests.Features.Messaging.SubscriptionTestActions;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class SubscriptionTestActions
{
    internal static EventSourceRef Topic(TestDatabase db) => new(db.Partition, "topic", EventSourceKind.Topic);
    internal static SubscriptionRef Configure(TestDatabase db, string group = "group", SubscriptionPolicy? policy = null,
        SubscriptionStart start = SubscriptionStart.FromBeginning, string? cursor = null, string[]? types = null, string dataPrincipal = "root")
    {
        var subscription = new SubscriptionRef(Topic(db), group);
        var id = Guid.NewGuid();
        db.Submit(OperationKind.ConfigureSubscription, new ConfigureSubscriptionRequest(id, subscription,
            new(dataPrincipal) { Policy = policy ?? new(), EventTypes = [.. (types ?? [])] }, start, cursor), id: id).Get<SubscriptionInfo>();
        return subscription;
    }
    internal static void Publish(TestDatabase db, int count = 3, string prefix = "event", string type = "Created")
        => db.Commit(new PublishTopic("topic", [.. Enumerable.Range(1, count).Select(i => new EventData(prefix + i, type, "{\"n\":" + i + "}"))]));
    internal static ReceiveSubscriptionResult Receive(TestDatabase db, SubscriptionRef subscription, int count = 3,
        int lease = 30, DateTimeOffset? time = null, string principal = "root")
    {
        var id = Guid.NewGuid();
        return db.Submit(OperationKind.ReceiveSubscription,
            new ReceiveSubscriptionRequest(id, subscription, count, LeaseSeconds: lease), id: id, time: time, principal: principal).Get<ReceiveSubscriptionResult>();
    }
    internal static CommitReceipt Complete(TestDatabase db, SubscriptionRef subscription, SubscriptionDelivery delivery,
        DeliveryAction action = DeliveryAction.Ack, DateTimeOffset? time = null)
    {
        var id = Guid.NewGuid();
        return db.Submit(OperationKind.SubscriptionDelivery,
            new SubscriptionDeliveryCommand(id, subscription, delivery.Token, action), id: id, time: time).Get<CommitReceipt>();
    }
}

internal sealed class SubscriptionTests
{
    [Test]
    public async Task IndependentGroupsRetainOnePayloadAndAckOnlyAContiguousPrefix()
    {
        using var db = new TestDatabase();
        db.Configure("topic", ResourceKind.Topic);
        Publish(db);
        var a = Configure(db, "a");
        var b = Configure(db, "b");
        var first = Receive(db, a);
        var second = Receive(db, b);
        await Assert.That(first.Deliveries.Select(d => d.Event.Position)).IsEquivalentTo(new long[] { 1, 2, 3 }, CollectionOrdering.Matching);
        await Assert.That(second.Deliveries.Select(d => d.Event.Data.EventId)).IsEquivalentTo(first.Deliveries.Select(d => d.Event.Data.EventId), CollectionOrdering.Matching);
        Complete(db, a, first.Deliveries[0]);
        Complete(db, a, first.Deliveries[2]);
        await Assert.That(db.Database.GetSubscription("root", a).Checkpoint).IsEqualTo(1);
        await Assert.That(db.Database.GetSubscription("root", b).Checkpoint).IsEqualTo(0);
        Complete(db, a, first.Deliveries[1]);
        await Assert.That(db.Database.GetSubscription("root", a).Checkpoint).IsEqualTo(3);
        await Assert.That(db.Database.ReadEventSource("root", new(Topic(db))).Events.Length).IsEqualTo(3);
        await Assert.That(Receive(db, a).Deliveries).IsEmpty();
    }
    [Test]
    public async Task GapWindowStopsNewClaimsUntilTheMissingPrefixIsAcknowledged()
    {
        using var db = new TestDatabase();
        db.Configure("topic", ResourceKind.Topic);
        Publish(db, 5);
        var subscription = Configure(db, policy: new() { MaxWindow = 2 });
        var received = Receive(db, subscription);
        await Assert.That(received.Deliveries.Length).IsEqualTo(2);
        Complete(db, subscription, received.Deliveries[1]);
        await Assert.That(Receive(db, subscription).Deliveries).IsEmpty();
        Complete(db, subscription, received.Deliveries[0]);
        await Assert.That(Receive(db, subscription).Deliveries.Select(d => d.Event.Position)).IsEquivalentTo(new long[] { 3, 4 }, CollectionOrdering.Matching);
    }
    [Test]
    public async Task FromNowAndTailCursorIncludeEveryLaterAppend()
    {
        using var db = new TestDatabase();
        db.Configure("topic", ResourceKind.Topic);
        Publish(db, 2);
        var atTail = db.Database.ReadEventSource("root", new(Topic(db)));
        await Assert.That(atTail.HasMore).IsFalse();
        var subscription = Configure(db, start: SubscriptionStart.FromNow);
        Publish(db, 1, "later");
        var after = db.Database.ReadEventSource("root", new(Topic(db), Cursor: atTail.Cursor));
        await Assert.That(System.Linq.Enumerable.Single(after.Events).Data.EventId).IsEqualTo("later1");
        await Assert.That(System.Linq.Enumerable.Single(Receive(db, subscription).Deliveries).Event.Data.EventId).IsEqualTo("later1");
        await Assert.That(after.HasMore).IsFalse();
    }
    [Test]
    public async Task CursorStartAndDeterministicFilterDoNotSkipPendingEvents()
    {
        using var db = new TestDatabase();
        db.Configure("topic", ResourceKind.Topic);
        Publish(db, 1, "before", "Other");
        var cursor = db.Database.ReadEventSource("root", new(Topic(db))).Cursor;
        Publish(db, 1, "after", "Created");
        Publish(db, 1, "filtered", "Other");
        Publish(db, 1, "last", "Created");
        var subscription = Configure(db, start: SubscriptionStart.FromCursor, cursor: cursor, types: ["Created"]);
        var received = Receive(db, subscription);
        await Assert.That(received.Deliveries.Select(d => d.Event.Position)).IsEquivalentTo(new long[] { 2, 4 }, CollectionOrdering.Matching);
        Complete(db, subscription, received.Deliveries[1]);
        await Assert.That(db.Database.GetSubscription("root", subscription).Checkpoint).IsEqualTo(1);
        Complete(db, subscription, received.Deliveries[0]);
        await Assert.That(db.Database.GetSubscription("root", subscription).Checkpoint).IsEqualTo(4);
    }
    [Test]
    public async Task ExpiredLeaseCannotAcknowledgeANewerClaimAndRetriesParkTheGap()
    {
        using var db = new TestDatabase();
        db.Configure("topic", ResourceKind.Topic);
        Publish(db, 1);
        var subscription = Configure(db, policy: new() { MaxAttempts = 2, MaxLeaseSeconds = 1 });
        var time = TimeProvider.System.GetUtcNow();
        var first = await Assert.That(Receive(db, subscription, lease: 1, time: time).Deliveries).HasSingleItem();
        var second = await Assert.That(Receive(db, subscription, lease: 1, time: time.AddSeconds(2)).Deliveries).HasSingleItem();
        var id = Guid.NewGuid();
        await Assert.That(db.Submit(OperationKind.SubscriptionDelivery,
            new SubscriptionDeliveryCommand(id, subscription, first.Token, DeliveryAction.Ack), id: id, time: time.AddSeconds(2)).Error).IsEqualTo(ErrorCode.StaleLease);
        await Assert.That(second.LeaseVersion).IsEqualTo(first.LeaseVersion + 1);
        var parked = Receive(db, subscription, lease: 1, time: time.AddSeconds(4));
        await Assert.That(parked.Deliveries).IsEmpty();
        await Assert.That(parked.Status.Paused).IsTrue();
        await Assert.That(parked.Status.SafeFailureCode).IsEqualTo("AttemptsExhausted");
        await Assert.That(parked.Status.Checkpoint).IsEqualTo(0);
    }
    [Test]
    public async Task ProcessingEffectsAndAcknowledgementRollBackTogetherAndInboxSurvivesReplay()
    {
        using var db = new TestDatabase();
        db.Configure("topic", ResourceKind.Topic);
        db.Configure("orders", ResourceKind.Collection);
        Publish(db, 1);
        var subscription = Configure(db);
        var delivery = await Assert.That(Receive(db, subscription).Deliveries).HasSingleItem();
        var failedId = Guid.NewGuid();
        var failed = new SubscriptionProcessingRequest(failedId, subscription, delivery.Token, "worker", 1, [new PutDocument("orders", "o1", "{}", 9)]);
        await Assert.That(db.Submit(OperationKind.SubscriptionProcessing, failed, id: failedId).Error).IsEqualTo(ErrorCode.RevisionConflict);
        await Assert.That(db.Database.GetSubscription("root", subscription).Checkpoint).IsEqualTo(0);
        var id = Guid.NewGuid();
        var request = failed with { CommandId = id, Effects = [new PutDocument("orders", "o1", "{}", 0)] };
        var result = db.Submit(OperationKind.SubscriptionProcessing, request, id: id).Get<SubscriptionProcessingResult>();
        await Assert.That(result.AlreadyProcessed).IsFalse();
        await Assert.That(db.Database.GetSubscription("root", subscription).Checkpoint).IsEqualTo(1);
        var replayId = Guid.NewGuid();
        var replay = db.Submit(OperationKind.SubscriptionProcessing, request with { CommandId = replayId }, id: replayId).Get<SubscriptionProcessingResult>();
        await Assert.That(replay.AlreadyProcessed).IsTrue();
        await Assert.That(replay.OriginalEffectsToken).IsEqualTo(result.OriginalEffectsToken);
        var seekId = Guid.NewGuid();
        var sought = db.Submit(OperationKind.SeekSubscription,
            new SeekSubscriptionRequest(seekId, subscription, 1, SubscriptionStart.FromBeginning), id: seekId).Get<SubscriptionInfo>();
        await Assert.That(sought.Paused).IsTrue();
        var resumeId = Guid.NewGuid();
        db.Submit(OperationKind.SetSubscriptionPaused, new SetSubscriptionPausedRequest(resumeId, subscription, sought.Generation, false), id: resumeId).Get<SubscriptionInfo>();
        var repeated = await Assert.That(Receive(db, subscription).Deliveries).HasSingleItem();
        var processingId = Guid.NewGuid();
        var processed = db.Submit(OperationKind.SubscriptionProcessing, request with { CommandId = processingId, Token = repeated.Token }, id: processingId).Get<SubscriptionProcessingResult>();
        await Assert.That(processed.AlreadyProcessed).IsTrue();
        await Assert.That(processed.OriginalEffectsToken).IsEqualTo(result.OriginalEffectsToken);
        await Assert.That(db.Database.GetSubscription("root", subscription).Checkpoint).IsEqualTo(1);
        await Assert.That(db.Database.GetDocument("root", new(db.Partition, "orders", "o1"))!.Revision).IsEqualTo(1);
    }
    [Test]
    public async Task SeekFencesOldTokensAndRequiresExplicitResume()
    {
        using var db = new TestDatabase();
        db.Configure("topic", ResourceKind.Topic);
        Publish(db, 1);
        var subscription = Configure(db);
        var delivery = await Assert.That(Receive(db, subscription).Deliveries).HasSingleItem();
        var id = Guid.NewGuid();
        var status = db.Submit(OperationKind.SeekSubscription,
            new SeekSubscriptionRequest(id, subscription, 1, SubscriptionStart.FromBeginning), id: id).Get<SubscriptionInfo>();
        await Assert.That(status.Generation).IsEqualTo(2);
        await Assert.That(status.OwnershipEpoch).IsEqualTo(2);
        await Assert.That(status.Paused).IsTrue();
        var ackId = Guid.NewGuid();
        await Assert.That(db.Submit(OperationKind.SubscriptionDelivery,
            new SubscriptionDeliveryCommand(ackId, subscription, delivery.Token, DeliveryAction.Ack), id: ackId).Error).IsEqualTo(ErrorCode.TokenInvalidated);
        var receiveId = Guid.NewGuid();
        await Assert.That(db.Submit(OperationKind.ReceiveSubscription,
            new ReceiveSubscriptionRequest(receiveId, subscription), id: receiveId).Error).IsEqualTo(ErrorCode.DispatchPaused);
    }
    [Test]
    public async Task TopicQuotaFailureRollsBackTheProducerDocumentAndEventCounter()
    {
        using var db = new TestDatabase();
        db.Configure("orders", ResourceKind.Collection);
        var definition = new ResourceDefinition("topic", ResourceKind.Topic, db.Partition.TransactionDomainId)
        { EventRetention = new() { MaxEvents = 1 } };
        db.Submit(OperationKind.ConfigureResource, new ConfigureResourceRequest(db.Partition.TenantId, db.Partition.DatabaseId, definition)).Get<ResourceDefinition>();
        Publish(db, 1);
        var id = Guid.NewGuid();
        await Assert.That(db.Submit(OperationKind.Batch, new CommandRequest(id, db.Partition,
            [new PutDocument("orders", "o1", "{}", 0), new PublishTopic("topic", [new("overflow", "Created", "{}")])]), id: id).Error).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(db.Database.GetDocument("root", new(db.Partition, "orders", "o1"))).IsNull();
        await Assert.That(db.Database.ReadEventSource("root", new(Topic(db))).Head.TailPosition).IsEqualTo(1);
    }
}
