namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class EventingArtifactSeed
{
    internal static async Task RunAsync(EventingArtifactFixture fixture)
    {
        var source = fixture.Source;
        source.Configure(EventingArtifactFixture.Topic, ResourceKind.Topic);
        source.Configure(EventingArtifactFixture.Queue, ResourceKind.WorkQueue);
        source.Configure(EventingArtifactFixture.Documents, ResourceKind.Collection);
        fixture.InitializeTime();
        var id = Guid.NewGuid();
        var command = new CommandRequest(id, source.Partition,
            [new PublishTopic(EventingArtifactFixture.Topic,
                [new(EventingArtifactFixture.FirstEvent, EventingArtifactFixture.EventType, EventingArtifactFixture.Json), new(EventingArtifactFixture.SecondEvent, EventingArtifactFixture.EventType, EventingArtifactFixture.Json),
                    new(EventingArtifactFixture.ThirdEvent, EventingArtifactFixture.EventType, EventingArtifactFixture.Json)])]);
        fixture.Original = fixture.Operation(OperationKind.Batch, command, id);
        var originalResult = fixture.SubmitIssued(source.Database, fixture.Original);
        originalResult.Get<CommitReceipt>();
        var position = source.Store.Position;
        var bytes = EventingArtifactState.FullBytes(source.Store);
        await EventingArtifactState.SameResultAsync(originalResult, fixture.SubmitIssued(source.Database, fixture.Original));
        await Assert.That(source.Store.Position).IsEqualTo(position);
        await Assert.That(EventingArtifactState.FullBytes(source.Store)).IsEquivalentTo(bytes, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        foreach (var group in new[] { fixture.Group, fixture.Other })
        {
            id = Guid.NewGuid();
            fixture.Apply(source.Database, OperationKind.ConfigureSubscription,
                new ConfigureSubscriptionRequest(id, group, new(EventingArtifactFixture.Principal)), id).Get<SubscriptionInfo>();
        }
        id = Guid.NewGuid();
        fixture.Received = fixture.Apply(source.Database, OperationKind.ReceiveSubscription,
            new ReceiveSubscriptionRequest(id, fixture.Group, MaxEvents: 3), id).Get<ReceiveSubscriptionResult>();
        await Assert.That(fixture.Received.Deliveries.Length).IsEqualTo(3);
        id = Guid.NewGuid();
        fixture.Processed = fixture.Apply(source.Database, OperationKind.SubscriptionProcessing,
            new SubscriptionProcessingRequest(id, fixture.Group, fixture.Received.Deliveries[0].Token,
                EventingArtifactFixture.Handler, 1, [.. EventingArtifactFixture.Effects]), id).Get<SubscriptionProcessingResult>();
        id = Guid.NewGuid();
        fixture.Apply(source.Database, OperationKind.SubscriptionDelivery,
            new SubscriptionDeliveryCommand(id, fixture.Group, fixture.Received.Deliveries[2].Token, DeliveryAction.Ack), id).Get<CommitReceipt>();
        id = Guid.NewGuid();
        fixture.Apply(source.Database, OperationKind.ReceiveSubscription,
            new ReceiveSubscriptionRequest(id, fixture.Other), id).Get<ReceiveSubscriptionResult>();
        id = Guid.NewGuid();
        fixture.QueueClaim = fixture.Apply(source.Database, OperationKind.Receive,
            new ReceiveRequest(id, fixture.Lane), id).Get<ReceiveResult>();
        await Assert.That(fixture.QueueClaim.Deliveries.Length).IsEqualTo(1);
        fixture.Cursor = source.Database.ReadEventSource(EventingArtifactFixture.Principal, new(fixture.Events)).Cursor;
        await EventingArtifactState.AssertSeedAsync(fixture, source.Database);
    }
}
