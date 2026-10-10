using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class SubscriptionFilterTailFlow
{
    private const string TailGroup = "tail-group";
    private const string TailEvent = "tail-event";
    private const long OriginalTail = 4;
    private const long NextTail = 5;

    internal static async Task RunAsync(DatabaseEngine db, EventSourceRef source, CancellationToken token)
    {
        var group = new SubscriptionRef(source, TailGroup);
        var configure = new ConfigureSubscriptionRequest(Guid.NewGuid(), group,
            new(SubscriptionFilterProtocol.Root), SubscriptionStart.FromNow);
        var initial = SubscriptionFilterColdTrial.Apply(db, OperationKind.ConfigureSubscription,
            configure, configure.CommandId, token).Get<SubscriptionInfo>();
        await Assert.That(initial.Checkpoint).IsEqualTo(OriginalTail);
        var pause = new SetSubscriptionPausedRequest(Guid.NewGuid(), group, SubscriptionFilterProtocol.FirstGeneration, true);
        SubscriptionFilterColdTrial.Apply(db, OperationKind.SetSubscriptionPaused, pause, pause.CommandId, token).Get<SubscriptionInfo>();
        var change = configure with
        {
            CommandId = Guid.NewGuid(),
            Start = SubscriptionStart.FromBeginning,
            ExpectedGeneration = SubscriptionFilterProtocol.FirstGeneration,
            Definition = configure.Definition with { EventTypes = [SubscriptionFilterProtocol.OldType] }
        };
        var updated = SubscriptionFilterColdTrial.Apply(db, OperationKind.ConfigureSubscription, change, change.CommandId, token).Get<SubscriptionInfo>();
        await Assert.That(updated.Checkpoint).IsEqualTo(OriginalTail);
        await Assert.That(updated.IssuedPosition).IsEqualTo(OriginalTail);
        pause = new(Guid.NewGuid(), group, SubscriptionFilterProtocol.NextGeneration, false);
        SubscriptionFilterColdTrial.Apply(db, OperationKind.SetSubscriptionPaused, pause, pause.CommandId, token).Get<SubscriptionInfo>();
        var publish = new CommandRequest(Guid.NewGuid(), source.Partition,
            [new PublishTopic(source.Resource, [new(TailEvent, SubscriptionFilterProtocol.OldType, SubscriptionFilterProtocol.Payload)])]);
        SubscriptionFilterColdTrial.Apply(db, OperationKind.Batch, publish, publish.CommandId, token).Get<CommitReceipt>();
        var receive = new ReceiveSubscriptionRequest(Guid.NewGuid(), group);
        var delivery = SubscriptionFilterColdTrial.Apply(db, OperationKind.ReceiveSubscription, receive, receive.RequestId, token).Get<ReceiveSubscriptionResult>().Deliveries.Single();
        await Assert.That(delivery.Event.Position).IsEqualTo(NextTail);
        var ack = new SubscriptionDeliveryCommand(Guid.NewGuid(), group, delivery.Token, DeliveryAction.Ack);
        SubscriptionFilterColdTrial.Apply(db, OperationKind.SubscriptionDelivery, ack, ack.CommandId, token).Get<CommitReceipt>();
        await Assert.That(db.GetSubscription(SubscriptionFilterProtocol.Root, group).Checkpoint).IsEqualTo(NextTail);
    }
}
