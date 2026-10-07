using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class EventingArtifactContinuation
{
    private const string DuplicateDetail = "The event ID is already retained in this source generation.";
    private const string HealthyId = "healthy";

    internal static async Task RunAsync(EventingArtifactFixture fixture)
    {
        var database = fixture.Database;
        var id = Guid.NewGuid();
        var sought = fixture.Apply(database, OperationKind.SeekSubscription,
            new SeekSubscriptionRequest(id, fixture.Group, 1, SubscriptionStart.FromBeginning), id).Get<SubscriptionInfo>();
        await Assert.That(sought.Generation).IsEqualTo(2L);
        await Assert.That(sought.Checkpoint).IsEqualTo(0L);
        await Assert.That(sought.Paused).IsTrue();
        id = Guid.NewGuid();
        fixture.Apply(database, OperationKind.SetSubscriptionPaused,
            new SetSubscriptionPausedRequest(id, fixture.Group, 2, false), id).Get<SubscriptionInfo>();
        id = Guid.NewGuid();
        await Assert.That(fixture.Apply(database, OperationKind.SetDispatch, false, id).Get<bool>()).IsTrue();
        await VerifyDedupAsync(fixture);
        id = Guid.NewGuid();
        var received = fixture.Apply(database, OperationKind.ReceiveSubscription,
            new ReceiveSubscriptionRequest(id, fixture.Group, MaxEvents: 3), id).Get<ReceiveSubscriptionResult>();
        await Assert.That(received.Deliveries.Select(item => item.Event.Position))
            .IsEquivalentTo(new long[] { 1, 2, 3 }, CollectionOrdering.Matching);
        var before = EventingArtifactState.Bytes(database.Store, fixture.Source.Partition, effectsOnly: true);
        id = Guid.NewGuid();
        var operation = fixture.Operation(OperationKind.SubscriptionProcessing,
            new SubscriptionProcessingRequest(id, fixture.Group, received.Deliveries[0].Token,
                EventingArtifactFixture.Handler, 1, [.. EventingArtifactFixture.Effects]), id);
        var result = database.Apply(operation);
        var replay = result.Get<SubscriptionProcessingResult>();
        await Assert.That(replay.AlreadyProcessed).IsTrue();
        await Assert.That(replay.OriginalEffectsToken).IsEqualTo(fixture.Processed.OriginalEffectsToken);
        await EventingArtifactState.SameAsync(before, database.Store, fixture.Source.Partition, effectsOnly: true);
        var position = database.Store.Position;
        await EventingArtifactState.SameResultAsync(result, database.Apply(operation));
        await Assert.That(database.Store.Position).IsEqualTo(position);
        foreach (var delivery in received.Deliveries.Skip(1))
        {
            id = Guid.NewGuid();
            fixture.Apply(database, OperationKind.SubscriptionDelivery,
                new SubscriptionDeliveryCommand(id, fixture.Group, delivery.Token, DeliveryAction.Ack), id).Get<CommitReceipt>();
        }
        await Assert.That(database.GetSubscription(EventingArtifactFixture.Principal, fixture.Group).Checkpoint).IsEqualTo(3L);
        await HealthyAsync(fixture);
    }

    private static async Task VerifyDedupAsync(EventingArtifactFixture fixture)
    {
        var before = EventingArtifactState.Bytes(fixture.Database.Store, fixture.Source.Partition);
        var id = Guid.NewGuid();
        await EventingArtifactFences.FailedAsync(fixture, OperationKind.Batch,
            new CommandRequest(id, fixture.Source.Partition,
                [new PublishTopic(EventingArtifactFixture.Topic, [new(EventingArtifactFixture.FirstEvent, EventingArtifactFixture.EventType, EventingArtifactFixture.Json)])]),
            id, ErrorCode.DuplicateEventId, DuplicateDetail);
        await EventingArtifactState.SameAsync(before, fixture.Database.Store, fixture.Source.Partition);
    }

    private static async Task HealthyAsync(EventingArtifactFixture fixture)
    {
        var database = fixture.Database;
        var id = Guid.NewGuid();
        fixture.Apply(database, OperationKind.Batch, new CommandRequest(id, fixture.Source.Partition,
            [new PublishTopic(EventingArtifactFixture.Topic, [new(HealthyId, EventingArtifactFixture.EventType, EventingArtifactFixture.Json)]),
                new EnqueueMessage(EventingArtifactFixture.Queue, HealthyId, EventingArtifactFixture.Json)]), id).Get<CommitReceipt>();
        id = Guid.NewGuid();
        var next = fixture.Apply(database, OperationKind.ReceiveSubscription,
            new ReceiveSubscriptionRequest(id, fixture.Group), id).Get<ReceiveSubscriptionResult>();
        await Assert.That(next.Deliveries.Length).IsEqualTo(1);
        await Assert.That(next.Deliveries[0].Event.Position).IsEqualTo(4L);
        await Assert.That(next.Deliveries[0].Event.Data.EventId).IsEqualTo(HealthyId);
        await EventingArtifactHealthyAssertions.EventAsync(fixture, next.Deliveries[0].Event);
        id = Guid.NewGuid();
        fixture.Apply(database, OperationKind.SubscriptionDelivery,
            new SubscriptionDeliveryCommand(id, fixture.Group, next.Deliveries[0].Token, DeliveryAction.Ack), id).Get<CommitReceipt>();
        id = Guid.NewGuid();
        var queue = fixture.Apply(database, OperationKind.Receive, new ReceiveRequest(id, fixture.Lane), id).Get<ReceiveResult>();
        await Assert.That(queue.Deliveries.Length).IsEqualTo(1);
        await Assert.That(queue.Deliveries[0].Id).IsEqualTo(HealthyId);
        await EventingArtifactHealthyAssertions.DeliveryAsync(fixture, queue.Deliveries[0]);
        id = Guid.NewGuid();
        fixture.Apply(database, OperationKind.Delivery,
            new DeliveryCommand(id, fixture.Lane, queue.Deliveries[0].Token, DeliveryAction.Ack), id).Get<CommitReceipt>();
        await Assert.That(database.GetSubscription(EventingArtifactFixture.Principal, fixture.Group).Checkpoint).IsEqualTo(4L);
        await Assert.That(database.InspectMessage(EventingArtifactFixture.Principal, fixture.Lane, HealthyId)!.Metadata.State)
            .IsEqualTo(MessageState.Acked);
        await EventingArtifactHealthyAssertions.CompletedAsync(fixture, database);
    }
}
