using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.Messaging;

internal static class SubscriptionOldCutHealthy
{
    internal static async Task RunAsync(DatabaseEngine database, ZoneTreeStore store, SubscriptionOldCutState state)
    {
        var id = Guid.NewGuid();
        state.FreshProducer = state.Apply(database, OperationKind.Batch, new CommandRequest(id, state.Group.Source.Partition,
            [new PublishTopic(SubscriptionOldCutState.Topic, [new("event4", "Created", "{\"n\":4}")])]), id).Get<CommitReceipt>();
        var request = new ReceiveSubscriptionRequest(Guid.NewGuid(), state.Group, 3);
        var claimed = state.Apply(database, OperationKind.ReceiveSubscription, request, request.RequestId,
            SubscriptionOldCutState.Worker).Get<ReceiveSubscriptionResult>();
        var delivery = await Assert.That(claimed.Deliveries).HasSingleItem();
        await Assert.That(delivery.Event.Position).IsEqualTo(4);
        await Assert.That(delivery.Event.Data).IsEqualTo(new EventData("event4", "Created", "{\"n\":4}"));
        await Assert.That(delivery.Attempt).IsEqualTo(1);
        await Assert.That(delivery.LeaseVersion).IsEqualTo(1L);
        await Assert.That(delivery.LeaseUntil).IsEqualTo(SubscriptionOldCutState.Epoch.AddSeconds(61));
        var processing = state.Processing(delivery.Token, "input4");
        var position = database.LastApplied;
        var completed = state.Apply(database, OperationKind.SubscriptionProcessing, processing,
            processing.CommandId, SubscriptionOldCutState.Worker).Get<SubscriptionProcessingResult>();
        state.FreshProcessing = completed.Receipt;
        await Assert.That(completed.AlreadyProcessed).IsFalse();
        await Assert.That(completed.OriginalEffectsToken).IsEqualTo(completed.Receipt.Token);
        await SubscriptionOldCutAssertions.ReceiptAsync(completed.Receipt, processing.CommandId, store,
            state.Group.Source.Partition, position + 1, new("putDocument", SubscriptionOldCutState.Collection, "input4", 1),
            new("Ack", SubscriptionOldCutState.Topic, "4", 1));
        await FinalAsync(database, state);
        await SubscriptionOldCutReplay.ProcessingAsync(database, store, state);
        await SubscriptionOldCutSavedFailure.RetainedAsync(database, store, state);
    }
    internal static async Task FinalAsync(DatabaseEngine database, SubscriptionOldCutState state)
    {
        await SubscriptionOldCutAssertions.StatusAsync(database, state, 2, 4, 4, 4);
        await SubscriptionOldCutOutbox.RequireAsync(database, state);
        await SubscriptionOldCutAssertions.SameAsync(database.GetDocument("root", new(state.Group.Source.Partition,
            SubscriptionOldCutState.Collection, "input4")), new DocumentResult(
                new(state.Group.Source.Partition, SubscriptionOldCutState.Collection, "input4") { Lifetime = state.DocumentLifetime },
                1, SubscriptionOldCutState.EffectJson, false, []));
    }
}
