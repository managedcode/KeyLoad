using KeyLoad.UnitTests.Features.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal sealed class TopicRetentionOperationTests
{
    [Test]
    public async Task AcEventRetention001PinsAtomicPurgeReplayAndNativeReopenKeepEveryOtherModel()
    {
        using var fixture = new TopicRetentionFixture();
        await Assert.That(fixture.Database.GetSubscription("root", fixture.First).Checkpoint).IsEqualTo(1L);
        await Assert.That(fixture.Database.GetSubscription("root", fixture.Second).Paused).IsTrue();
        await TopicRetentionAssertions.Reject(fixture, fixture.Purge(), ErrorCode.ResourceExhausted);
        SubscriptionTestActions.Complete(fixture.Owner, fixture.First, fixture.Claims.Deliveries[1]);
        await TopicRetentionAssertions.Reject(fixture, fixture.Purge(), ErrorCode.ResourceExhausted);
        var seek = Guid.NewGuid();
        fixture.Owner.Submit(OperationKind.SeekSubscription,
            new SeekSubscriptionRequest(seek, fixture.Second, 1, SubscriptionStart.FromNow), id: seek).Get<SubscriptionInfo>();
        var protectedState = TopicRetentionAssertions.ProtectedBytes(fixture);
        var command = fixture.Purge();
        var receipt = fixture.Submit(command).Get<CommitReceipt>();
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Mutations.Single()).IsEqualTo(new MutationReceipt("purgeTopic", "topic", "2", 2));
        await Assert.That(TopicRetentionAssertions.ProtectedBytes(fixture)).IsEquivalentTo(protectedState, CollectionOrdering.Matching);
        await TopicRetentionAssertions.RetainedTail(fixture);
        await TopicRetentionNativeState.VerifyAsync(fixture);
        var cut = fixture.Store.Position;
        var snapshot = QueueWholeFlowStorage.Bytes(fixture.Store);
        var replay = fixture.Submit(command).Get<CommitReceipt>();
        await Assert.That(JsonDefaults.Serialize(replay).AsSpan().SequenceEqual(JsonDefaults.Serialize(receipt))).IsTrue();
        await Assert.That(fixture.Store.Position).IsEqualTo(cut);
        await TopicRetentionCommandConflict.VerifyAsync(fixture, command, receipt);
        fixture.Reopen();
        await Assert.That(QueueWholeFlowStorage.Bytes(fixture.Store)).IsEquivalentTo(snapshot, CollectionOrdering.Matching);
        await TopicRetentionAssertions.RetainedTail(fixture);
        await TopicRetentionNativeState.VerifyAsync(fixture);
        await Assert.That(fixture.Database.GetSubscription("root", fixture.First).Checkpoint).IsEqualTo(3L);
        await Assert.That(fixture.Database.GetSubscription("root", fixture.Second).Checkpoint).IsEqualTo(3L);
        var reopenedReplay = fixture.Submit(command).Get<CommitReceipt>();
        await Assert.That(JsonDefaults.Serialize(reopenedReplay).AsSpan().SequenceEqual(JsonDefaults.Serialize(receipt))).IsTrue();
        await TopicRetentionIdentityFlow.VerifyAsync(fixture);
    }
}
