using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class SubscriptionOldCutReset
{
    internal static async Task RunAsync(DatabaseEngine database, ZoneTreeStore store, SubscriptionOldCutState state)
    {
        var id = Guid.NewGuid();
        state.Apply(database, OperationKind.SetDispatch, false, id).Get<bool>();
        var seek = new SeekSubscriptionRequest(Guid.NewGuid(), state.Group, 1, SubscriptionStart.FromBeginning);
        state.Apply(database, OperationKind.SeekSubscription, seek, seek.CommandId).Get<SubscriptionInfo>();
        await SubscriptionOldCutAssertions.StatusAsync(database, state, 2, 0, 0, 3, true);
        var policy = new ConfigurePrincipalRequest(state.Principal(2, false));
        state.Apply(database, OperationKind.ConfigurePrincipal, policy, Guid.NewGuid()).Get<PrincipalRecord>();
        var resume = new SetSubscriptionPausedRequest(Guid.NewGuid(), state.Group, 2, false);
        state.Apply(database, OperationKind.SetSubscriptionPaused, resume, resume.CommandId).Get<SubscriptionInfo>();
        var first = Receive(database, state);
        await ClaimsAsync(first, state, 1, SubscriptionOldCutState.Epoch.AddSeconds(30));
        var denied = state.Processing(first.Deliveries[1].Token);
        await SubscriptionOldCutSavedFailure.RunAsync(database, store, state, state.Create(database, OperationKind.SubscriptionProcessing,
            denied, denied.CommandId, SubscriptionOldCutState.Worker), ErrorCode.PermissionDenied,
            "The principal cannot perform this operation in this scope.");
        await SubscriptionOldCutAssertions.StatusAsync(database, state, 2, 0, 3, 3);
        state.Apply(database, OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(state.Principal(3, true)),
            Guid.NewGuid()).Get<PrincipalRecord>();
        await SubscriptionOldCutSavedFailure.RetainedAsync(database, store, state);
        state.Clock.Advance(TimeSpan.FromSeconds(31));
        var claim = Receive(database, state);
        await ClaimsAsync(claim, state, 2, SubscriptionOldCutState.Epoch.AddSeconds(61));
        await ReuseInboxAsync(database, store, state, claim);
    }
    private static ReceiveSubscriptionResult Receive(DatabaseEngine database, SubscriptionOldCutState state)
    {
        var request = new ReceiveSubscriptionRequest(Guid.NewGuid(), state.Group, 3);
        return state.Apply(database, OperationKind.ReceiveSubscription, request, request.RequestId,
            SubscriptionOldCutState.Worker).Get<ReceiveSubscriptionResult>();
    }
    private static async Task ClaimsAsync(ReceiveSubscriptionResult claim, SubscriptionOldCutState state,
        int attempt, DateTimeOffset deadline)
    {
        await Assert.That(claim.Deliveries.Length).IsEqualTo(3);
        for (var index = 0; index < claim.Deliveries.Length; index++)
        {
            var delivery = claim.Deliveries[index];
            await Assert.That(delivery.Event).IsEqualTo(state.OriginalClaim.Deliveries[index].Event);
            await Assert.That(delivery.Attempt).IsEqualTo(attempt);
            await Assert.That(delivery.LeaseVersion).IsEqualTo((long)attempt);
            await Assert.That(delivery.LeaseUntil).IsEqualTo(deadline);
            await Assert.That(string.IsNullOrEmpty(delivery.Token)).IsFalse();
        }
    }
    private static async Task ReuseInboxAsync(DatabaseEngine database, ZoneTreeStore store,
        SubscriptionOldCutState state, ReceiveSubscriptionResult claim)
    {
        var request = state.Processing(claim.Deliveries[1].Token);
        var position = database.LastApplied;
        var completed = state.Apply(database, OperationKind.SubscriptionProcessing, request, request.CommandId,
            SubscriptionOldCutState.Worker).Get<SubscriptionProcessingResult>();
        await Assert.That(completed.AlreadyProcessed).IsTrue();
        await Assert.That(completed.OriginalEffectsToken).IsEqualTo(state.OriginalProcessing.OriginalEffectsToken);
        await SubscriptionOldCutAssertions.ReceiptAsync(completed.Receipt, request.CommandId, store,
            state.Group.Source.Partition, position + 1, new MutationReceipt("Ack", SubscriptionOldCutState.Topic, "2", 2));
        await SubscriptionOldCutAssertions.StatusAsync(database, state, 2, 0, 3, 3);
        Ack(database, state, claim.Deliveries[2]);
        await SubscriptionOldCutAssertions.StatusAsync(database, state, 2, 0, 3, 3);
        Ack(database, state, claim.Deliveries[0]);
        await SubscriptionOldCutAssertions.StatusAsync(database, state, 2, 3, 3, 3);
    }
    internal static void Ack(DatabaseEngine database, SubscriptionOldCutState state, SubscriptionDelivery delivery)
    {
        var request = new SubscriptionDeliveryCommand(Guid.NewGuid(), state.Group, delivery.Token, DeliveryAction.Ack);
        state.Apply(database, OperationKind.SubscriptionDelivery, request, request.CommandId,
            SubscriptionOldCutState.Worker).Get<CommitReceipt>();
    }
}
