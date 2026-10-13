using System.Text.Json;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class RestoreResumedTokenHealthy
{
    private const string HealthyId = "resumed-token-healthy";
    private const long NextPosition = 5;

    internal static async Task RunAsync(EventingArtifactFixture fixture)
    {
        var id = Guid.NewGuid();
        var operation = fixture.Operation(OperationKind.Batch, new CommandRequest(id, fixture.Source.Partition,
            [new PublishTopic(EventingArtifactFixture.Topic,
                [new(HealthyId, EventingArtifactFixture.EventType, EventingArtifactFixture.Json)]),
                new EnqueueMessage(EventingArtifactFixture.Queue, HealthyId, EventingArtifactFixture.Json)]), id);
        var result = fixture.Database.Apply(operation);
        await Assert.That(result.Error).IsNull();
        await Assert.That(result.Get<CommitReceipt>().CommandId).IsEqualTo(id);
        var bytes = EventingArtifactState.FullBytes(fixture.Database.Store);
        var cut = fixture.Database.Store.Position;
        await EventingArtifactState.SameResultAsync(result, fixture.Database.Apply(operation));
        await Assert.That(fixture.Database.Store.Position).IsEqualTo(cut);
        await Assert.That(EventingArtifactState.FullBytes(fixture.Database.Store))
            .IsEquivalentTo(bytes, CollectionOrdering.Matching);
        id = Guid.NewGuid();
        var group = fixture.Apply(fixture.Database, OperationKind.ReceiveSubscription,
            new ReceiveSubscriptionRequest(id, fixture.Group), id).Get<ReceiveSubscriptionResult>();
        await Assert.That(group.Deliveries.Length).IsEqualTo(1);
        await Assert.That(group.Deliveries[0].Event).IsEqualTo(new SourceEventRecord(fixture.Events,
            NextPosition, NextPosition, new(HealthyId, EventingArtifactFixture.EventType,
                EventingArtifactFixture.Json), fixture.Time));
        id = Guid.NewGuid();
        fixture.Apply(fixture.Database, OperationKind.SubscriptionDelivery,
            new SubscriptionDeliveryCommand(id, fixture.Group, group.Deliveries[0].Token,
                DeliveryAction.Ack), id).Get<CommitReceipt>();
        id = Guid.NewGuid();
        var queue = fixture.Apply(fixture.Database, OperationKind.Receive,
            new ReceiveRequest(id, fixture.Lane), id).Get<ReceiveResult>();
        await Assert.That(queue.Deliveries.Length).IsEqualTo(1);
        await Assert.That(queue.Deliveries[0] with { Token = string.Empty }).IsEqualTo(new Delivery(HealthyId,
            EventingArtifactFixture.Json, EventingArtifactFixture.EmptyJson, string.Empty,
            1, fixture.Time.AddSeconds(30), 1, 1));
        await Assert.That(queue.Deliveries[0].Token).IsNotEmpty();
        id = Guid.NewGuid();
        fixture.Apply(fixture.Database, OperationKind.Delivery,
            new DeliveryCommand(id, fixture.Lane, queue.Deliveries[0].Token, DeliveryAction.Ack), id).Get<CommitReceipt>();
        await Assert.That(fixture.Database.GetSubscription(EventingArtifactFixture.Principal, fixture.Group).Checkpoint)
            .IsEqualTo(NextPosition);
        var actual = fixture.Database.InspectMessage(EventingArtifactFixture.Principal, fixture.Lane, HealthyId);
        await Assert.That(JsonSerializer.Serialize(actual, JsonDefaults.Options)).IsEqualTo(
            JsonSerializer.Serialize(new MessageInspection(new(HealthyId, MessageState.Acked,
                1, 3, 2, null, null, LeaseVersion: 1), null, null), JsonDefaults.Options));
    }
}
