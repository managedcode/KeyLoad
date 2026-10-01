using KeyLoad.Core;
using KeyLoad.Security;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests;

public sealed class SubscriptionTests
{
    private static EventSourceRef Topic(TestDatabase db) => new(db.Partition, "topic", EventSourceKind.Topic);
    private static SubscriptionRef Configure(TestDatabase db, string group = "group", SubscriptionPolicy? policy = null,
        SubscriptionStart start = SubscriptionStart.FromBeginning, string? cursor = null, string[]? types = null, string dataPrincipal = "root")
    {
        var subscription = new SubscriptionRef(Topic(db), group); var id = Guid.NewGuid();
        db.Submit(OperationKind.ConfigureSubscription, new ConfigureSubscriptionRequest(id, subscription,
            new(dataPrincipal) { Policy = policy ?? new(), EventTypes = types ?? [] }, start, cursor), id: id).Get<SubscriptionInfo>();
        return subscription;
    }
    private static void Publish(TestDatabase db, int count = 3, string prefix = "event", string type = "Created")
        => db.Commit(new PublishTopic("topic", Enumerable.Range(1, count).Select(i => new EventData(prefix + i, type, "{\"n\":" + i + "}")).ToArray()));
    private static ReceiveSubscriptionResult Receive(TestDatabase db, SubscriptionRef subscription, int count = 3,
        int lease = 30, DateTimeOffset? time = null, string principal = "root")
    {
        var id = Guid.NewGuid(); return db.Submit(OperationKind.ReceiveSubscription,
            new ReceiveSubscriptionRequest(id, subscription, count, LeaseSeconds: lease), id: id, time: time, principal: principal).Get<ReceiveSubscriptionResult>();
    }
    private static CommitReceipt Complete(TestDatabase db, SubscriptionRef subscription, SubscriptionDelivery delivery,
        DeliveryAction action = DeliveryAction.Ack, DateTimeOffset? time = null)
    {
        var id = Guid.NewGuid(); return db.Submit(OperationKind.SubscriptionDelivery,
            new SubscriptionDeliveryCommand(id, subscription, delivery.Token, action), id: id, time: time).Get<CommitReceipt>();
    }
    [Fact]
    public void IndependentGroupsRetainOnePayloadAndAckOnlyAContiguousPrefix()
    {
        using var db = new TestDatabase(); db.Configure("topic", ResourceKind.Topic); Publish(db);
        var a = Configure(db, "a"); var b = Configure(db, "b");
        var first = Receive(db, a); var second = Receive(db, b);
        Assert.Equal(new long[] { 1, 2, 3 }, first.Deliveries.Select(d => d.Event.Position));
        Assert.Equal(first.Deliveries.Select(d => d.Event.Data.EventId), second.Deliveries.Select(d => d.Event.Data.EventId));
        Complete(db, a, first.Deliveries[0]); Complete(db, a, first.Deliveries[2]);
        Assert.Equal(1, db.Database.GetSubscription("root", a).Checkpoint);
        Assert.Equal(0, db.Database.GetSubscription("root", b).Checkpoint);
        Complete(db, a, first.Deliveries[1]); Assert.Equal(3, db.Database.GetSubscription("root", a).Checkpoint);
        Assert.Equal(3, db.Database.ReadEventSource("root", new(Topic(db))).Events.Length);
        Assert.Empty(Receive(db, a).Deliveries);
    }
    [Fact]
    public void GapWindowStopsNewClaimsUntilTheMissingPrefixIsAcknowledged()
    {
        using var db = new TestDatabase(); db.Configure("topic", ResourceKind.Topic); Publish(db, 5);
        var subscription = Configure(db, policy: new() { MaxWindow = 2 }); var received = Receive(db, subscription);
        Assert.Equal(2, received.Deliveries.Length); Complete(db, subscription, received.Deliveries[1]);
        Assert.Empty(Receive(db, subscription).Deliveries);
        Complete(db, subscription, received.Deliveries[0]);
        Assert.Equal(new long[] { 3, 4 }, Receive(db, subscription).Deliveries.Select(d => d.Event.Position));
    }
    [Fact]
    public void FromNowAndTailCursorIncludeEveryLaterAppend()
    {
        using var db = new TestDatabase(); db.Configure("topic", ResourceKind.Topic); Publish(db, 2);
        var atTail = db.Database.ReadEventSource("root", new(Topic(db)));
        Assert.False(atTail.HasMore); var subscription = Configure(db, start: SubscriptionStart.FromNow);
        Publish(db, 1, "later");
        var after = db.Database.ReadEventSource("root", new(Topic(db), Cursor: atTail.Cursor));
        Assert.Equal("later1", Assert.Single(after.Events).Data.EventId);
        Assert.Equal("later1", Assert.Single(Receive(db, subscription).Deliveries).Event.Data.EventId);
        Assert.False(after.HasMore);
    }
    [Fact]
    public void CursorStartAndDeterministicFilterDoNotSkipPendingEvents()
    {
        using var db = new TestDatabase(); db.Configure("topic", ResourceKind.Topic); Publish(db, 1, "before", "Other");
        var cursor = db.Database.ReadEventSource("root", new(Topic(db))).Cursor;
        Publish(db, 1, "after", "Created"); Publish(db, 1, "filtered", "Other"); Publish(db, 1, "last", "Created");
        var subscription = Configure(db, start: SubscriptionStart.FromCursor, cursor: cursor, types: ["Created"]);
        var received = Receive(db, subscription);
        Assert.Equal(new long[] { 2, 4 }, received.Deliveries.Select(d => d.Event.Position));
        Complete(db, subscription, received.Deliveries[1]); Assert.Equal(1, db.Database.GetSubscription("root", subscription).Checkpoint);
        Complete(db, subscription, received.Deliveries[0]); Assert.Equal(4, db.Database.GetSubscription("root", subscription).Checkpoint);
    }
    [Fact]
    public void ExpiredLeaseCannotAcknowledgeANewerClaimAndRetriesParkTheGap()
    {
        using var db = new TestDatabase(); db.Configure("topic", ResourceKind.Topic); Publish(db, 1);
        var subscription = Configure(db, policy: new() { MaxAttempts = 2, MaxLeaseSeconds = 1 }); var time = DateTimeOffset.UtcNow;
        var first = Assert.Single(Receive(db, subscription, lease: 1, time: time).Deliveries);
        var second = Assert.Single(Receive(db, subscription, lease: 1, time: time.AddSeconds(2)).Deliveries);
        var id = Guid.NewGuid();
        Assert.Equal(ErrorCode.StaleLease, db.Submit(OperationKind.SubscriptionDelivery,
            new SubscriptionDeliveryCommand(id, subscription, first.Token, DeliveryAction.Ack), id: id, time: time.AddSeconds(2)).Error);
        Assert.Equal(first.LeaseVersion + 1, second.LeaseVersion);
        var parked = Receive(db, subscription, lease: 1, time: time.AddSeconds(4));
        Assert.Empty(parked.Deliveries); Assert.True(parked.Status.Paused); Assert.Equal("AttemptsExhausted", parked.Status.SafeFailureCode);
        Assert.Equal(0, parked.Status.Checkpoint);
    }
    [Fact]
    public void ProcessingEffectsAndAcknowledgementRollBackTogetherAndInboxSurvivesReplay()
    {
        using var db = new TestDatabase(); db.Configure("topic", ResourceKind.Topic); db.Configure("orders", ResourceKind.Collection); Publish(db, 1);
        var subscription = Configure(db); var delivery = Assert.Single(Receive(db, subscription).Deliveries);
        var failedId = Guid.NewGuid();
        var failed = new SubscriptionProcessingRequest(failedId, subscription, delivery.Token, "worker", 1, [new PutDocument("orders", "o1", "{}", 9)]);
        Assert.Equal(ErrorCode.RevisionConflict, db.Submit(OperationKind.SubscriptionProcessing, failed, id: failedId).Error);
        Assert.Equal(0, db.Database.GetSubscription("root", subscription).Checkpoint);
        var id = Guid.NewGuid(); var request = failed with { CommandId = id, Effects = [new PutDocument("orders", "o1", "{}", 0)] };
        var result = db.Submit(OperationKind.SubscriptionProcessing, request, id: id).Get<SubscriptionProcessingResult>();
        Assert.False(result.AlreadyProcessed); Assert.Equal(1, db.Database.GetSubscription("root", subscription).Checkpoint);
        var replayId = Guid.NewGuid(); var replay = db.Submit(OperationKind.SubscriptionProcessing, request with { CommandId = replayId }, id: replayId).Get<SubscriptionProcessingResult>();
        Assert.True(replay.AlreadyProcessed); Assert.Equal(result.OriginalEffectsToken, replay.OriginalEffectsToken);
        var seekId = Guid.NewGuid(); var sought = db.Submit(OperationKind.SeekSubscription,
            new SeekSubscriptionRequest(seekId, subscription, 1, SubscriptionStart.FromBeginning), id: seekId).Get<SubscriptionInfo>();
        Assert.True(sought.Paused); var resumeId = Guid.NewGuid();
        db.Submit(OperationKind.SetSubscriptionPaused, new SetSubscriptionPausedRequest(resumeId, subscription, sought.Generation, false), id: resumeId).Get<SubscriptionInfo>();
        var repeated = Assert.Single(Receive(db, subscription).Deliveries); var processingId = Guid.NewGuid();
        var processed = db.Submit(OperationKind.SubscriptionProcessing, request with { CommandId = processingId, Token = repeated.Token }, id: processingId).Get<SubscriptionProcessingResult>();
        Assert.True(processed.AlreadyProcessed); Assert.Equal(result.OriginalEffectsToken, processed.OriginalEffectsToken);
        Assert.Equal(1, db.Database.GetSubscription("root", subscription).Checkpoint);
        Assert.Equal(1, db.Database.GetDocument("root", new(db.Partition, "orders", "o1"))!.Revision);
    }
    [Fact]
    public void SeekFencesOldTokensAndRequiresExplicitResume()
    {
        using var db = new TestDatabase(); db.Configure("topic", ResourceKind.Topic); Publish(db, 1);
        var subscription = Configure(db); var delivery = Assert.Single(Receive(db, subscription).Deliveries);
        var id = Guid.NewGuid(); var status = db.Submit(OperationKind.SeekSubscription,
            new SeekSubscriptionRequest(id, subscription, 1, SubscriptionStart.FromBeginning), id: id).Get<SubscriptionInfo>();
        Assert.Equal(2, status.Generation); Assert.Equal(2, status.OwnershipEpoch); Assert.True(status.Paused);
        var ackId = Guid.NewGuid(); Assert.Equal(ErrorCode.TokenInvalidated, db.Submit(OperationKind.SubscriptionDelivery,
            new SubscriptionDeliveryCommand(ackId, subscription, delivery.Token, DeliveryAction.Ack), id: ackId).Error);
        var receiveId = Guid.NewGuid(); Assert.Equal(ErrorCode.DispatchPaused, db.Submit(OperationKind.ReceiveSubscription,
            new ReceiveSubscriptionRequest(receiveId, subscription), id: receiveId).Error);
    }
    [Fact]
    public void TopicQuotaFailureRollsBackTheProducerDocumentAndEventCounter()
    {
        using var db = new TestDatabase(); db.Configure("orders", ResourceKind.Collection);
        var definition = new ResourceDefinition("topic", ResourceKind.Topic, db.Partition.TransactionDomainId)
            { EventRetention = new() { MaxEvents = 1 } };
        db.Submit(OperationKind.ConfigureResource, new ConfigureResourceRequest(db.Partition.TenantId, db.Partition.DatabaseId, definition)).Get<ResourceDefinition>();
        Publish(db, 1); var id = Guid.NewGuid();
        Assert.Equal(ErrorCode.ResourceExhausted, db.Submit(OperationKind.Batch, new CommandRequest(id, db.Partition,
            [new PutDocument("orders", "o1", "{}", 0), new PublishTopic("topic", [new("overflow", "Created", "{}")])]), id: id).Error);
        Assert.Null(db.Database.GetDocument("root", new(db.Partition, "orders", "o1")));
        Assert.Equal(1, db.Database.ReadEventSource("root", new(Topic(db))).Head.TailPosition);
    }
    [Fact]
    public void EventIdsAreScopedToTheStreamGenerationAndStreamsCanBeSubscribed()
    {
        using var db = new TestDatabase(); db.Configure("streams", ResourceKind.StreamSet);
        db.Commit(new AppendEvents("streams", "a", [new("same", "Created", "{}")], ExpectedStreamRevision.NoStream));
        db.Commit(new AppendEvents("streams", "b", [new("same", "Created", "{}")], ExpectedStreamRevision.NoStream));
        var source = new EventSourceRef(db.Partition, "streams", EventSourceKind.Stream, "a"); var subscription = new SubscriptionRef(source, "worker");
        var id = Guid.NewGuid(); db.Submit(OperationKind.ConfigureSubscription, new ConfigureSubscriptionRequest(id, subscription, new("root")), id: id).Get<SubscriptionInfo>();
        Assert.Equal("a", Assert.Single(Receive(db, subscription).Deliveries).Event.Source.StreamId);
        var duplicate = Guid.NewGuid(); Assert.Equal(ErrorCode.DuplicateEventId, db.Submit(OperationKind.Batch,
            new CommandRequest(duplicate, db.Partition, [new AppendEvents("streams", "a", [new("same", "Created", "{}")], ExpectedStreamRevision.Any)]), id: duplicate).Error);
    }
    [Fact]
    public void DeliveryStateAndAcknowledgementGapsSurviveStoreReopen()
    {
        using var db = new TestDatabase(); db.Configure("topic", ResourceKind.Topic); Publish(db);
        var subscription = Configure(db); var received = Receive(db, subscription); Complete(db, subscription, received.Deliveries[2]);
        db.Store.Dispose();
        using var store = new ZoneTreeStore(new(db.Directory)); var database = new DatabaseEngine(store, new AuthorizationPolicy());
        Assert.Equal(0, database.GetSubscription("root", subscription).Checkpoint);
        foreach (var delivery in received.Deliveries.Take(2))
        {
            var id = Guid.NewGuid(); var request = new SubscriptionDeliveryCommand(id, subscription, delivery.Token, DeliveryAction.Ack);
            database.Apply(new(id, OperationKind.SubscriptionDelivery, "root", DateTimeOffset.UtcNow,
                System.Text.Json.JsonSerializer.Serialize(request, JsonDefaults.Options))).Get<CommitReceipt>();
        }
        Assert.Equal(3, database.GetSubscription("root", subscription).Checkpoint);
    }
    [Fact]
    public void MissingRequiredWorkerInputStopsDeliveryWithoutAdvancingTheCheckpoint()
    {
        using var db = new TestDatabase();
        db.Configure("topic", ResourceKind.Topic, fields: [new("/secret", "pii", RequiredForProcessing: true)]);
        db.Commit(new PublishTopic("topic", [new("event", "Created", "{\"secret\":\"protected\"}")]));
        var worker = new PrincipalRecord("worker", "tenant", [new("database", "topic", Capability.TopicsRead | Capability.SubscriptionsConsume | Capability.SubscriptionsAck)], []);
        db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(worker)).Get<PrincipalRecord>();
        var subscription = Configure(db); var id = Guid.NewGuid();
        Assert.Equal(ErrorCode.PermissionDenied, db.Submit(OperationKind.ReceiveSubscription,
            new ReceiveSubscriptionRequest(id, subscription), principal: "worker", id: id).Error);
        Assert.Equal(0, db.Database.GetSubscription("root", subscription).Checkpoint);
        Assert.DoesNotContain("protected", Assert.Single(db.Database.ReadEventSource("worker", new(Topic(db))).Events).Data.PayloadJson);
    }
    [Fact]
    public void DataPrincipalPolicyChangeBlocksOldReceivePayloadAndAcknowledgement()
    {
        using var db = new TestDatabase(); db.Configure("topic", ResourceKind.Topic); Publish(db, 1);
        var service = new PrincipalRecord("service", "tenant", [new("database", "topic", Capability.TopicsRead)], []);
        db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(service)).Get<PrincipalRecord>();
        var subscription = Configure(db, dataPrincipal: "service"); var id = Guid.NewGuid(); var request = new ReceiveSubscriptionRequest(id, subscription);
        var received = db.Submit(OperationKind.ReceiveSubscription, request, id: id).Get<ReceiveSubscriptionResult>();
        db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(service with { PolicyEpoch = 2 })).Get<PrincipalRecord>();
        Assert.Equal(ErrorCode.PermissionDenied, db.Submit(OperationKind.ReceiveSubscription, request, id: id).Error);
        var ack = Guid.NewGuid(); Assert.Equal(ErrorCode.PermissionDenied, db.Submit(OperationKind.SubscriptionDelivery,
            new SubscriptionDeliveryCommand(ack, subscription, received.Deliveries[0].Token, DeliveryAction.Ack), id: ack).Error);
        Assert.Equal(0, db.Database.GetSubscription("root", subscription).Checkpoint);
        db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(service with { PolicyEpoch = 3, Revoked = true })).Get<PrincipalRecord>();
        var claim = Guid.NewGuid(); Assert.Equal(ErrorCode.Unauthenticated, db.Submit(OperationKind.ReceiveSubscription,
            new ReceiveSubscriptionRequest(claim, subscription), id: claim).Error);
    }
    [Fact]
    public void EmptyReceiveRetryCannotReplayStatusFromBeforeASeek()
    {
        using var db = new TestDatabase(); db.Configure("topic", ResourceKind.Topic); var subscription = Configure(db);
        var id = Guid.NewGuid(); var request = new ReceiveSubscriptionRequest(id, subscription);
        Assert.Empty(db.Submit(OperationKind.ReceiveSubscription, request, id: id).Get<ReceiveSubscriptionResult>().Deliveries);
        var seek = Guid.NewGuid(); db.Submit(OperationKind.SeekSubscription,
            new SeekSubscriptionRequest(seek, subscription, 1, SubscriptionStart.FromBeginning), id: seek).Get<SubscriptionInfo>();
        Assert.Equal(ErrorCode.TokenInvalidated, db.Submit(OperationKind.ReceiveSubscription, request, id: id).Error);
    }
    [Fact]
    public void AnotherWorkerReusesTheInboxOnlyAfterCurrentEffectPermissionsAreChecked()
    {
        using var db = new TestDatabase(); db.Configure("topic", ResourceKind.Topic); db.Configure("orders", ResourceKind.Collection); Publish(db, 1);
        var subscription = Configure(db); var first = Assert.Single(Receive(db, subscription).Deliveries);
        Mutation[] effects = [new PutDocument("orders", "protected", "{}", 0, new("private"))];
        var id = Guid.NewGuid(); var completed = db.Submit(OperationKind.SubscriptionProcessing,
            new SubscriptionProcessingRequest(id, subscription, first.Token, "handler", 1, effects), id: id).Get<SubscriptionProcessingResult>();
        var seek = Guid.NewGuid(); var sought = db.Submit(OperationKind.SeekSubscription,
            new SeekSubscriptionRequest(seek, subscription, 1, SubscriptionStart.FromBeginning), id: seek).Get<SubscriptionInfo>();
        var resume = Guid.NewGuid(); db.Submit(OperationKind.SetSubscriptionPaused,
            new SetSubscriptionPausedRequest(resume, subscription, sought.Generation, false), id: resume).Get<SubscriptionInfo>();
        var worker = new PrincipalRecord("worker", "tenant", [new("database", "*", Capability.TopicsRead | Capability.SubscriptionsConsume
            | Capability.SubscriptionsAck | Capability.DocumentsWrite)], []) { RestrictRows = true, OwnerId = "other" };
        db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(worker)).Get<PrincipalRecord>();
        var now = DateTimeOffset.UtcNow; var deniedLease = Assert.Single(Receive(db, subscription, time: now, principal: "worker").Deliveries);
        var deniedId = Guid.NewGuid(); Assert.Equal(ErrorCode.PermissionDenied, db.Submit(OperationKind.SubscriptionProcessing,
            new SubscriptionProcessingRequest(deniedId, subscription, deniedLease.Token, "handler", 1, effects), id: deniedId, principal: "worker", time: now).Error);
        Assert.Equal(0, db.Database.GetSubscription("root", subscription).Checkpoint);
        db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(worker with { OwnerId = "private", PolicyEpoch = 2 })).Get<PrincipalRecord>();
        var allowedLease = Assert.Single(Receive(db, subscription, time: now.AddSeconds(31), principal: "worker").Deliveries);
        var allowedId = Guid.NewGuid(); var replay = db.Submit(OperationKind.SubscriptionProcessing,
            new SubscriptionProcessingRequest(allowedId, subscription, allowedLease.Token, "handler", 1, effects), id: allowedId,
            principal: "worker", time: now.AddSeconds(31)).Get<SubscriptionProcessingResult>();
        Assert.True(replay.AlreadyProcessed); Assert.Equal(completed.OriginalEffectsToken, replay.OriginalEffectsToken);
        Assert.Equal(1, db.Database.GetSubscription("root", subscription).Checkpoint);
        Assert.Equal(1, db.Database.GetDocument("root", new(db.Partition, "orders", "protected"))!.Revision);
    }
    [Fact]
    public void InboxCanAcknowledgeAHandlerWithNoAdditionalMutations()
    {
        using var db = new TestDatabase(); db.Configure("topic", ResourceKind.Topic); Publish(db, 1);
        var subscription = Configure(db); var delivery = Assert.Single(Receive(db, subscription).Deliveries); var id = Guid.NewGuid();
        var result = db.Submit(OperationKind.SubscriptionProcessing,
            new SubscriptionProcessingRequest(id, subscription, delivery.Token, "handler", 1, []), id: id).Get<SubscriptionProcessingResult>();
        Assert.False(result.AlreadyProcessed); Assert.Equal(1, db.Database.GetSubscription("root", subscription).Checkpoint);
        Assert.Equal("Ack", Assert.Single(result.Receipt.Mutations).Kind);
    }
    [Fact]
    public void RetainedEventIdRejectsDifferentContentAndPreservesDedupForTheInitialStoreFormat()
    {
        using var db = new TestDatabase(); db.Configure("streams", ResourceKind.StreamSet); db.Configure("topic", ResourceKind.Topic);
        db.Commit(new AppendEvents("streams", "a", [new("same", "Created", "{}")], ExpectedStreamRevision.NoStream));
        db.Store.Commit((tx, _) =>
        {
            tx.Delete(KeySpace.Partition("event-id", db.Partition, "streams", "a", 1L, "same"));
            tx.PutRecord(KeySpace.Partition("event-id", db.Partition, "streams", "same"), new { StreamId = "a", Generation = 1L, Revision = 1L });
            return true;
        });
        var duplicate = Guid.NewGuid(); Assert.Equal(ErrorCode.DuplicateEventId, db.Submit(OperationKind.Batch,
            new CommandRequest(duplicate, db.Partition, [new AppendEvents("streams", "a", [new("same", "Created", "{}")], ExpectedStreamRevision.Any)]), id: duplicate).Error);
        var changed = Guid.NewGuid(); Assert.Equal(ErrorCode.Conflict, db.Submit(OperationKind.Batch,
            new CommandRequest(changed, db.Partition, [new AppendEvents("streams", "a", [new("same", "Created", "{\"different\":true}")], ExpectedStreamRevision.Any)]), id: changed).Error);
        db.Commit(new AppendEvents("streams", "b", [new("same", "Created", "{}")], ExpectedStreamRevision.NoStream));
        db.Commit(new PublishTopic("topic", [new("same", "Created", "{}") ]));
        var topic = Guid.NewGuid(); Assert.Equal(ErrorCode.Conflict, db.Submit(OperationKind.Batch,
            new CommandRequest(topic, db.Partition, [new PublishTopic("topic", [new("same", "Other", "{}")])]), id: topic).Error);
        Assert.Equal(1, db.Database.ReadEventSource("root", new(Topic(db))).Head.TailPosition);
    }
}
