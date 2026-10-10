using KeyLoad.Core;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class SubscriptionFilterContinuation
{
    internal static async Task RunAsync(DatabaseEngine db, SubscriptionRef group, SubscriptionRef independent,
        ReceiveSubscriptionResult old, CancellationToken token)
    {
        var ack = new SubscriptionDeliveryCommand(Guid.NewGuid(), group, old.Deliveries[1].Token, DeliveryAction.Ack);
        await Assert.That(SubscriptionFilterColdTrial.Apply(db, OperationKind.SubscriptionDelivery, ack,
            ack.CommandId, token).Error).IsEqualTo(ErrorCode.TokenInvalidated);
        var paused = new ReceiveSubscriptionRequest(Guid.NewGuid(), group);
        await Assert.That(SubscriptionFilterColdTrial.Apply(db, OperationKind.ReceiveSubscription, paused,
            paused.RequestId, token).Error).IsEqualTo(ErrorCode.DispatchPaused);
        var resume = new SetSubscriptionPausedRequest(Guid.NewGuid(), group, SubscriptionFilterProtocol.NextGeneration, false);
        SubscriptionFilterColdTrial.Apply(db, OperationKind.SetSubscriptionPaused, resume, resume.CommandId, token).Get<SubscriptionInfo>();
        var receive = new ReceiveSubscriptionRequest(Guid.NewGuid(), group, SubscriptionFilterProtocol.WindowSize);
        var result = SubscriptionFilterColdTrial.Apply(db, OperationKind.ReceiveSubscription, receive, receive.RequestId, token).Get<ReceiveSubscriptionResult>();
        await Assert.That(result.Deliveries.Select(x => x.Event.Position)).IsEquivalentTo(new long[] { 2 }, CollectionOrdering.Matching);
        ack = new(Guid.NewGuid(), group, result.Deliveries.Single().Token, DeliveryAction.Ack);
        SubscriptionFilterColdTrial.Apply(db, OperationKind.SubscriptionDelivery, ack, ack.CommandId, token).Get<CommitReceipt>();
        await SubscriptionFilterNativeAssertions.StatusAsync(db, group, SubscriptionFilterProtocol.NextGeneration, SubscriptionFilterProtocol.WindowSize, false);
        await SubscriptionFilterNativeAssertions.StatusAsync(db, independent, SubscriptionFilterProtocol.FirstGeneration, 0, false);
        await Assert.That(db.ReadEventSource(SubscriptionFilterProtocol.Root, new(group.Source), token).Events.Length).IsEqualTo(SubscriptionFilterProtocol.WindowSize);
    }

    internal static async Task HealthyAsync(DatabaseEngine db, SubscriptionRef group, CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), group.Source.Partition,
            [new PublishTopic(SubscriptionFilterProtocol.Topic, [new(SubscriptionFilterProtocol.HealthyEvent, SubscriptionFilterProtocol.NewType, SubscriptionFilterProtocol.Payload)])]);
        SubscriptionFilterColdTrial.Apply(db, OperationKind.Batch, command, command.CommandId, token).Get<CommitReceipt>();
        var receive = new ReceiveSubscriptionRequest(Guid.NewGuid(), group);
        var actual = SubscriptionFilterColdTrial.Apply(db, OperationKind.ReceiveSubscription, receive, receive.RequestId, token).Get<ReceiveSubscriptionResult>();
        await Assert.That(actual.Deliveries.Single().Event.Data.EventId).IsEqualTo(SubscriptionFilterProtocol.HealthyEvent);
        var ack = new SubscriptionDeliveryCommand(Guid.NewGuid(), group, actual.Deliveries.Single().Token, DeliveryAction.Ack);
        SubscriptionFilterColdTrial.Apply(db, OperationKind.SubscriptionDelivery, ack, ack.CommandId, token).Get<CommitReceipt>();
        await Assert.That(db.GetSubscription(SubscriptionFilterProtocol.Root, group).Checkpoint).IsEqualTo(4);
    }
}
