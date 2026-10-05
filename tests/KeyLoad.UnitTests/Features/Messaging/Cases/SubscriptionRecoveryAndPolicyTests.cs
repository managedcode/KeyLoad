using KeyLoad.Core;
using KeyLoad.Security;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

using static KeyLoad.UnitTests.Features.Messaging.SubscriptionTestActions;

namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class SubscriptionRecoveryAndPolicyTests
{
    [Test]
    public async Task EventIdsAreScopedToTheStreamGenerationAndStreamsCanBeSubscribed()
    {
        using var db = new TestDatabase();
        db.Configure("streams", ResourceKind.StreamSet);
        db.Commit(new AppendEvents("streams", "a", [new("same", "Created", "{}")], ExpectedStreamRevision.NoStream));
        db.Commit(new AppendEvents("streams", "b", [new("same", "Created", "{}")], ExpectedStreamRevision.NoStream));
        var source = new EventSourceRef(db.Partition, "streams", EventSourceKind.Stream, "a");
        var subscription = new SubscriptionRef(source, "worker");
        var id = Guid.NewGuid();
        db.Submit(OperationKind.ConfigureSubscription, new ConfigureSubscriptionRequest(id, subscription, new("root")), id: id).Get<SubscriptionInfo>();
        await Assert.That(System.Linq.Enumerable.Single(Receive(db, subscription).Deliveries).Event.Source.StreamId).IsEqualTo("a");
        var duplicate = Guid.NewGuid();
        await Assert.That(db.Submit(OperationKind.Batch,
            new CommandRequest(duplicate, db.Partition, [new AppendEvents("streams", "a", [new("same", "Created", "{}")], ExpectedStreamRevision.Any)]), id: duplicate).Error).IsEqualTo(ErrorCode.DuplicateEventId);
    }
    [Test]
    public async Task DeliveryStateAndAcknowledgementGapsSurviveStoreReopen()
    {
        using var db = new TestDatabase();
        db.Configure("topic", ResourceKind.Topic);
        Publish(db);
        var subscription = Configure(db);
        var received = Receive(db, subscription);
        Complete(db, subscription, received.Deliveries[2]);
        db.Store.Dispose();
        using var store = new ZoneTreeStore(new(db.Directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var database = new DatabaseEngine(store, new AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource());
        await Assert.That(database.GetSubscription("root", subscription).Checkpoint).IsEqualTo(0);
        foreach (var delivery in received.Deliveries.Take(2))
        {
            var id = Guid.NewGuid();
            var request = new SubscriptionDeliveryCommand(id, subscription, delivery.Token, DeliveryAction.Ack);
            database.Apply(new(id, OperationKind.SubscriptionDelivery, "root", TimeProvider.System.GetUtcNow(),
                System.Text.Json.JsonSerializer.Serialize(request, JsonDefaults.Options))).Get<CommitReceipt>();
        }
        await Assert.That(database.GetSubscription("root", subscription).Checkpoint).IsEqualTo(3);
    }
    [Test]
    public async Task MissingRequiredWorkerInputStopsDeliveryWithoutAdvancingTheCheckpoint()
    {
        using var db = new TestDatabase();
        db.Configure("topic", ResourceKind.Topic, fields: [new("/secret", "pii", RequiredForProcessing: true)]);
        db.Commit(new PublishTopic("topic", [new("event", "Created", "{\"secret\":\"protected\"}")]));
        var worker = new PrincipalRecord("worker", "tenant", [new("database", "topic", Capability.TopicsRead | Capability.SubscriptionsConsume | Capability.SubscriptionsAck)], []);
        db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(worker)).Get<PrincipalRecord>();
        var subscription = Configure(db);
        var id = Guid.NewGuid();
        await Assert.That(db.Submit(OperationKind.ReceiveSubscription,
            new ReceiveSubscriptionRequest(id, subscription), principal: "worker", id: id).Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(db.Database.GetSubscription("root", subscription).Checkpoint).IsEqualTo(0);
        await Assert.That(System.Linq.Enumerable.Single(db.Database.ReadEventSource("worker", new(Topic(db))).Events).Data.PayloadJson).DoesNotContain("protected");
    }
    [Test]
    public async Task DataPrincipalPolicyChangeBlocksOldReceivePayloadAndAcknowledgement()
    {
        using var db = new TestDatabase();
        db.Configure("topic", ResourceKind.Topic);
        Publish(db, 1);
        var service = new PrincipalRecord("service", "tenant", [new("database", "topic", Capability.TopicsRead)], []);
        db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(service)).Get<PrincipalRecord>();
        var subscription = Configure(db, dataPrincipal: "service");
        var id = Guid.NewGuid();
        var request = new ReceiveSubscriptionRequest(id, subscription);
        var received = db.Submit(OperationKind.ReceiveSubscription, request, id: id).Get<ReceiveSubscriptionResult>();
        db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(service with { PolicyEpoch = 2 })).Get<PrincipalRecord>();
        await Assert.That(db.Submit(OperationKind.ReceiveSubscription, request, id: id).Error).IsEqualTo(ErrorCode.PermissionDenied);
        var ack = Guid.NewGuid();
        await Assert.That(db.Submit(OperationKind.SubscriptionDelivery,
            new SubscriptionDeliveryCommand(ack, subscription, received.Deliveries[0].Token, DeliveryAction.Ack), id: ack).Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(db.Database.GetSubscription("root", subscription).Checkpoint).IsEqualTo(0);
        db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(service with { PolicyEpoch = 3, Revoked = true })).Get<PrincipalRecord>();
        var claim = Guid.NewGuid();
        await Assert.That(db.Submit(OperationKind.ReceiveSubscription,
            new ReceiveSubscriptionRequest(claim, subscription), id: claim).Error).IsEqualTo(ErrorCode.Unauthenticated);
    }
    [Test]
    public async Task EmptyReceiveRetryCannotReplayStatusFromBeforeASeek()
    {
        using var db = new TestDatabase();
        db.Configure("topic", ResourceKind.Topic);
        var subscription = Configure(db);
        var id = Guid.NewGuid();
        var request = new ReceiveSubscriptionRequest(id, subscription);
        await Assert.That(db.Submit(OperationKind.ReceiveSubscription, request, id: id).Get<ReceiveSubscriptionResult>().Deliveries).IsEmpty();
        var seek = Guid.NewGuid();
        db.Submit(OperationKind.SeekSubscription,
            new SeekSubscriptionRequest(seek, subscription, 1, SubscriptionStart.FromBeginning), id: seek).Get<SubscriptionInfo>();
        await Assert.That(db.Submit(OperationKind.ReceiveSubscription, request, id: id).Error).IsEqualTo(ErrorCode.TokenInvalidated);
    }
    [Test]
    public async Task AnotherWorkerReusesTheInboxOnlyAfterCurrentEffectPermissionsAreChecked()
    {
        using var db = new TestDatabase();
        db.Configure("topic", ResourceKind.Topic);
        db.Configure("orders", ResourceKind.Collection);
        Publish(db, 1);
        var subscription = Configure(db);
        var first = await Assert.That(Receive(db, subscription).Deliveries).HasSingleItem();
        Mutation[] effects = [new PutDocument("orders", "protected", "{}", 0, new("private"))];
        var id = Guid.NewGuid();
        var completed = db.Submit(OperationKind.SubscriptionProcessing,
            new SubscriptionProcessingRequest(id, subscription, first.Token, "handler", 1, [.. effects]), id: id).Get<SubscriptionProcessingResult>();
        var seek = Guid.NewGuid();
        var sought = db.Submit(OperationKind.SeekSubscription,
            new SeekSubscriptionRequest(seek, subscription, 1, SubscriptionStart.FromBeginning), id: seek).Get<SubscriptionInfo>();
        var resume = Guid.NewGuid();
        db.Submit(OperationKind.SetSubscriptionPaused,
            new SetSubscriptionPausedRequest(resume, subscription, sought.Generation, false), id: resume).Get<SubscriptionInfo>();
        var worker = new PrincipalRecord("worker", "tenant", [new("database", "*", Capability.TopicsRead | Capability.SubscriptionsConsume
            | Capability.SubscriptionsAck | Capability.DocumentsWrite)], [])
        { RestrictRows = true, OwnerId = "other" };
        db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(worker)).Get<PrincipalRecord>();
        var now = TimeProvider.System.GetUtcNow();
        var deniedLease = await Assert.That(Receive(db, subscription, time: now, principal: "worker").Deliveries).HasSingleItem();
        var deniedId = Guid.NewGuid();
        await Assert.That(db.Submit(OperationKind.SubscriptionProcessing,
            new SubscriptionProcessingRequest(deniedId, subscription, deniedLease.Token, "handler", 1, [.. effects]), id: deniedId, principal: "worker", time: now).Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(db.Database.GetSubscription("root", subscription).Checkpoint).IsEqualTo(0);
        db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(worker with { OwnerId = "private", PolicyEpoch = 2 })).Get<PrincipalRecord>();
        var allowedLease = await Assert.That(Receive(db, subscription, time: now.AddSeconds(31), principal: "worker").Deliveries).HasSingleItem();
        var allowedId = Guid.NewGuid();
        var replay = db.Submit(OperationKind.SubscriptionProcessing,
            new SubscriptionProcessingRequest(allowedId, subscription, allowedLease.Token, "handler", 1, [.. effects]), id: allowedId,
            principal: "worker", time: now.AddSeconds(31)).Get<SubscriptionProcessingResult>();
        await Assert.That(replay.AlreadyProcessed).IsTrue();
        await Assert.That(replay.OriginalEffectsToken).IsEqualTo(completed.OriginalEffectsToken);
        await Assert.That(db.Database.GetSubscription("root", subscription).Checkpoint).IsEqualTo(1);
        await Assert.That(db.Database.GetDocument("root", new(db.Partition, "orders", "protected"))!.Revision).IsEqualTo(1);
    }
    [Test]
    public async Task InboxCanAcknowledgeAHandlerWithNoAdditionalMutations()
    {
        using var db = new TestDatabase();
        db.Configure("topic", ResourceKind.Topic);
        Publish(db, 1);
        var subscription = Configure(db);
        var delivery = await Assert.That(Receive(db, subscription).Deliveries).HasSingleItem();
        var id = Guid.NewGuid();
        var result = db.Submit(OperationKind.SubscriptionProcessing,
            new SubscriptionProcessingRequest(id, subscription, delivery.Token, "handler", 1, []), id: id).Get<SubscriptionProcessingResult>();
        await Assert.That(result.AlreadyProcessed).IsFalse();
        await Assert.That(db.Database.GetSubscription("root", subscription).Checkpoint).IsEqualTo(1);
        await Assert.That(System.Linq.Enumerable.Single(result.Receipt.Mutations).Kind).IsEqualTo("Ack");
    }
    [Test]
    public async Task RetainedNativeEventIdAtInitialLogicalKeyPreservesDedupAndRejectsDifferentContent()
    {
        using var db = new TestDatabase();
        db.Configure("streams", ResourceKind.StreamSet);
        db.Configure("topic", ResourceKind.Topic);
        db.Commit(new AppendEvents("streams", "a", [new("same", "Created", "{}")], ExpectedStreamRevision.NoStream));
        db.Store.Commit((tx, _) =>
        {
            tx.Delete(KeySpace.Partition("event-id", db.Partition, "streams", "a", 1L, "same"));
            tx.PutRecord(KeySpace.Partition("event-id", db.Partition, "streams", "same"), new EventIdentity("a", 1L, 1L));
            return true;
        });
        var duplicate = Guid.NewGuid();
        await Assert.That(db.Submit(OperationKind.Batch,
            new CommandRequest(duplicate, db.Partition, [new AppendEvents("streams", "a", [new("same", "Created", "{}")], ExpectedStreamRevision.Any)]), id: duplicate).Error).IsEqualTo(ErrorCode.DuplicateEventId);
        var changed = Guid.NewGuid();
        await Assert.That(db.Submit(OperationKind.Batch,
            new CommandRequest(changed, db.Partition, [new AppendEvents("streams", "a", [new("same", "Created", "{\"different\":true}")], ExpectedStreamRevision.Any)]), id: changed).Error).IsEqualTo(ErrorCode.Conflict);
        db.Commit(new AppendEvents("streams", "b", [new("same", "Created", "{}")], ExpectedStreamRevision.NoStream));
        db.Commit(new PublishTopic("topic", [new("same", "Created", "{}")]));
        var topic = Guid.NewGuid();
        await Assert.That(db.Submit(OperationKind.Batch,
            new CommandRequest(topic, db.Partition, [new PublishTopic("topic", [new("same", "Other", "{}")])]), id: topic).Error).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(db.Database.ReadEventSource("root", new(Topic(db))).Head.TailPosition).IsEqualTo(1);
    }
}
