using KeyLoad.UnitTests.Features.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal static class TopicRetentionCorruptionAssertions
{
    private const int OriginalAndRepeatedAttempts = 2;
    private const int CommitAdvance = 1;
    private const int PurgedRecords = 2;
    private const string CorruptionDetail = "The requested event history was retained away.";
    private const string PurgeOperation = "purgeTopic";
    private const string Topic = "topic";
    private const string ThroughPosition = "2";

    internal static async Task RejectAsync(TopicRetentionFixture fixture, CommandRequest command)
    {
        var all = QueueWholeFlowStorage.Bytes(fixture.Store);
        var position = fixture.Store.Position;
        for (var attempt = 0; attempt < OriginalAndRepeatedAttempts; attempt++)
        {
            var error = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Submit(command));
            await Assert.That(error.Code).IsEqualTo(ErrorCode.Corruption);
            await Assert.That(error.Message).IsEqualTo(CorruptionDetail);
            await Assert.That(fixture.Store.Position).IsEqualTo(position);
            await Assert.That(QueueWholeFlowStorage.Bytes(fixture.Store)).IsEquivalentTo(all, CollectionOrdering.Matching);
        }
    }

    internal static async Task PurgeRepairedAsync(TopicRetentionFixture fixture, CommandRequest original)
    {
        var protectedState = TopicRetentionAssertions.ProtectedBytes(fixture);
        var position = fixture.Store.Position;
        var receipt = fixture.Submit(original).Get<CommitReceipt>();
        await Assert.That(receipt.CommandId).IsEqualTo(original.CommandId);
        await Assert.That(receipt.Token.Position).IsEqualTo(position + CommitAdvance);
        await Assert.That(Convert.ToHexString(NativeSerialization.Serialize(receipt.Mutations.Single())))
            .IsEqualTo(Convert.ToHexString(NativeSerialization.Serialize(new MutationReceipt(PurgeOperation, Topic, ThroughPosition, PurgedRecords))));
        await Assert.That(TopicRetentionAssertions.ProtectedBytes(fixture)).IsEquivalentTo(protectedState, CollectionOrdering.Matching);
        await TopicRetentionNativeState.VerifyAsync(fixture);
        var all = QueueWholeFlowStorage.Bytes(fixture.Store);
        var committed = fixture.Store.Position;
        var replay = fixture.Submit(original).Get<CommitReceipt>();
        await Assert.That(JsonDefaults.Serialize(replay).AsSpan().SequenceEqual(JsonDefaults.Serialize(receipt))).IsTrue();
        await Assert.That(fixture.Store.Position).IsEqualTo(committed);
        await Assert.That(QueueWholeFlowStorage.Bytes(fixture.Store)).IsEquivalentTo(all, CollectionOrdering.Matching);
    }
}
