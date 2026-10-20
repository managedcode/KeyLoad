using KeyLoad.UnitTests.Features.ResourceExecution;
using KeyLoad.UnitTests.Features.StorageRecovery.Assertions;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class SubscriptionOldCutSeed
{
    internal static async Task RunAsync(TestDatabase fixture, SubscriptionOldCutState state)
    {
        fixture.Configure(SubscriptionOldCutState.Topic, ResourceKind.Topic);
        fixture.Configure(SubscriptionOldCutState.Collection, ResourceKind.Collection);
        state.DocumentLifetime = fixture.ExpectedReference(new(fixture.Partition, SubscriptionOldCutState.Collection, "input2")).Lifetime!;
        fixture.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(state.Principal(1, true)))
            .Get<PrincipalRecord>();
        var producerIndex = checked(fixture.ReadCanonicalCut().AppliedPosition + 1L);
        state.Producer = fixture.Commit(new PublishTopic(SubscriptionOldCutState.Topic,
            [new("event1", "Created", "{\"n\":1}"), new("event2", "Created", "{\"n\":2}"), new("event3", "Created", "{\"n\":3}")]));
        await SubscriptionOldCutAssertions.ReceiptAsync(state.Producer, state.Producer.CommandId, fixture.Store,
            fixture.Partition, producerIndex, new MutationReceipt("publishTopic", SubscriptionOldCutState.Topic, "event3", 3));
        var configure = new ConfigureSubscriptionRequest(Guid.NewGuid(), state.Group, state.Definition);
        state.Apply(fixture.Database, OperationKind.ConfigureSubscription, configure, configure.CommandId).Get<SubscriptionInfo>();
        state.OriginalReceive = new(Guid.NewGuid(), state.Group, 3);
        state.OriginalClaim = state.Apply(fixture.Database, OperationKind.ReceiveSubscription,
            state.OriginalReceive, state.OriginalReceive.RequestId).Get<ReceiveSubscriptionResult>();
        await ClaimAsync(fixture, state);
        var ack = new SubscriptionDeliveryCommand(Guid.NewGuid(), state.Group, state.OriginalClaim.Deliveries[2].Token, DeliveryAction.Ack);
        state.Apply(fixture.Database, OperationKind.SubscriptionDelivery, ack, ack.CommandId).Get<CommitReceipt>();
        var request = state.Processing(state.OriginalClaim.Deliveries[1].Token);
        state.OriginalProcessingOperation = state.Create(fixture.Database, OperationKind.SubscriptionProcessing, request, request.CommandId);
        var cut = fixture.Database.LastApplied;
        var result = fixture.Database.Apply(state.OriginalProcessingOperation);
        state.OriginalProcessing = result.Get<SubscriptionProcessingResult>();
        state.Successes.Add(state.OriginalProcessingOperation, result);
        await Assert.That(state.OriginalProcessing.AlreadyProcessed).IsFalse();
        await Assert.That(state.OriginalProcessing.OriginalEffectsToken).IsEqualTo(state.OriginalProcessing.Receipt.Token);
        await SubscriptionOldCutAssertions.ReceiptAsync(state.OriginalProcessing.Receipt, request.CommandId,
            fixture.Store, fixture.Partition, cut + 1, new("putDocument", SubscriptionOldCutState.Collection, "input2", 1),
            new("Ack", SubscriptionOldCutState.Topic, "2", 1));
        await SubscriptionOldCutAssertions.StatusAsync(fixture.Database, state, 1, 0, 3, 3);
    }
    private static async Task ClaimAsync(TestDatabase fixture, SubscriptionOldCutState state)
    {
        var claim = state.OriginalClaim;
        await Assert.That(claim.RequestId).IsEqualTo(state.OriginalReceive.RequestId);
        await SubscriptionOldCutAssertions.SameAsync(claim.Status, new SubscriptionInfo(state.Group, state.Definition,
            1, 1, 0, 3, 3, false, null));
        await Assert.That(claim.Token).IsEqualTo(new CommitToken(fixture.Store.Identity.Incarnation,
            fixture.Partition.AtomicPartitionId, fixture.Database.LastApplied));
        await Assert.That(claim.Deliveries.Length).IsEqualTo(3);
        for (var index = 0; index < claim.Deliveries.Length; index++)
        {
            var delivery = claim.Deliveries[index];
            var position = index + 1;
            await Assert.That(delivery.Event).IsEqualTo(new SourceEventRecord(state.Group.Source, position, position,
                new("event" + position, "Created", "{\"n\":" + position + "}"), SubscriptionOldCutState.Epoch));
            await Assert.That(delivery.LeaseUntil).IsEqualTo(SubscriptionOldCutState.Epoch.AddSeconds(30));
            await Assert.That(delivery.Attempt).IsEqualTo(1);
            await Assert.That(delivery.LeaseVersion).IsEqualTo(1L);
            await Assert.That(fixture.Database.Verify<GroupClaims>(delivery.Token)).IsEqualTo(new GroupClaims(
                "subscription-delivery", fixture.Store.Identity.Incarnation, state.Group, 1, 1, position, 1, "root", 1, 1));
        }
        var bytes = QueueWholeFlowStorage.Bytes(fixture.Store);
        var cut = fixture.Store.Position;
        var identity = ReadCutStoreStateAssertions.SnapshotIdentity(fixture.Store);
        var original = state.Successes.Single(pair => pair.Key.Value!.Kind == OperationKind.ReceiveSubscription);
        await ReplayResultAssertions.Same<ReceiveSubscriptionResult>(fixture.Database.Apply(original.Key), original.Value);
        await SubscriptionOldCutAssertions.ConservedAsync(fixture.Store, bytes, cut, identity);
    }

}
