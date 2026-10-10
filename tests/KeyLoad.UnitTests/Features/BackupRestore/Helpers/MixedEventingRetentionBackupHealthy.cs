using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class MixedEventingRetentionBackupHealthy
{
    private const string Healthy = "healthy";

    internal static async Task RunAsync(EventingArtifactFixture fixture, MixedEventingRetentionBackupState state, CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), fixture.Source.Partition,
            [new PublishTopic(EventingArtifactFixture.Topic, [new(Healthy, EventingArtifactFixture.EventType, EventingArtifactFixture.Json)]),
             new EnqueueMessage(EventingArtifactFixture.Queue, Healthy, EventingArtifactFixture.Json)]);
        var receipt = fixture.Apply(fixture.Database, OperationKind.Batch, command, command.CommandId).Get<CommitReceipt>();
        var issuedPublish = fixture.ReadTargetIssuedOperation(command.CommandId);
        await Assert.That(issuedPublish.Id).IsEqualTo(command.CommandId);
        await Assert.That(issuedPublish.Kind).IsEqualTo(OperationKind.Batch);
        await Assert.That(issuedPublish.PrincipalId).IsEqualTo(EventingArtifactFixture.Principal);
        var cut = fixture.Target!.Position;
        await MixedEventingRetentionBackupTopic.EqualAsync(receipt,
            fixture.Apply(fixture.Database, OperationKind.Batch, command, command.CommandId).Get<CommitReceipt>());
        await Assert.That(fixture.Target.Position).IsEqualTo(cut);
        var groupRequest = new ReceiveSubscriptionRequest(Guid.NewGuid(), fixture.Group);
        var group = fixture.Apply(fixture.Database, OperationKind.ReceiveSubscription, groupRequest, groupRequest.RequestId).Get<ReceiveSubscriptionResult>();
        var eventDelivery = await Assert.That(group.Deliveries).HasSingleItem();
        await Assert.That(eventDelivery.Event).IsEqualTo(new SourceEventRecord(fixture.Events, 4,
            MixedEventingRetentionBackupProtocol.HealthySequence,
            new(Healthy, EventingArtifactFixture.EventType, EventingArtifactFixture.Json), issuedPublish.EvaluatedAt));
        var groupAck = new SubscriptionDeliveryCommand(Guid.NewGuid(), fixture.Group, eventDelivery.Token, DeliveryAction.Ack);
        _ = fixture.Apply(fixture.Database, OperationKind.SubscriptionDelivery, groupAck, groupAck.CommandId).Get<CommitReceipt>();
        var receive = new ReceiveRequest(Guid.NewGuid(), fixture.Lane);
        var message = fixture.Apply(fixture.Database, OperationKind.Receive, receive, receive.RequestId).Get<ReceiveResult>();
        var delivery = await Assert.That(message.Deliveries).HasSingleItem();
        var issuedReceive = fixture.ReadTargetIssuedOperation(receive.RequestId);
        await Assert.That(issuedReceive.Id).IsEqualTo(receive.RequestId);
        await Assert.That(issuedReceive.Kind).IsEqualTo(OperationKind.Receive);
        await Assert.That(issuedReceive.PrincipalId).IsEqualTo(EventingArtifactFixture.Principal);
        await Assert.That(delivery.Token).IsNotEmpty();
        await Assert.That(delivery with { Token = string.Empty }).IsEqualTo(new Delivery(Healthy,
            EventingArtifactFixture.Json, EventingArtifactFixture.EmptyJson, string.Empty, 1,
            issuedReceive.EvaluatedAt.AddSeconds(30), 1, 1));
        var ack = new DeliveryCommand(Guid.NewGuid(), fixture.Lane, delivery.Token, DeliveryAction.Ack);
        _ = fixture.Apply(fixture.Database, OperationKind.Delivery, ack, ack.CommandId).Get<CommitReceipt>();
        await EventingArtifactHealthyAssertions.CompletedAfterNaturalExpiryAsync(fixture, fixture.Database);
        await QueueLifecycleHealthy.RunAsync(fixture.Database, state.RestoredQueue, token);
        await QueueLifecycleAccountingAssertions.TerminalAsync(fixture.Target, state.RestoredQueue, true);
        await MixedEventingRetentionBackupInbox.ContinueAsync(fixture, state, token);
        await MixedEventingRetentionBackupTopic.RequireAsync(fixture.Database, state);
    }
}
