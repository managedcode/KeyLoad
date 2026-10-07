using KeyLoad.UnitTests.Features.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal static class TopicRetentionCommandConflict
{
    internal static async Task VerifyAsync(TopicRetentionFixture fixture, CommandRequest command, CommitReceipt original)
    {
        var cut = fixture.Store.Position;
        var state = QueueWholeFlowStorage.Bytes(fixture.Store);
        var reused = command with { Mutations = [new PurgeTopic("topic", 1)] };
        var conflict = fixture.Submit(reused);
        await Assert.That(conflict.Error).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(fixture.Store.Position).IsEqualTo(cut);
        await Assert.That(QueueWholeFlowStorage.Bytes(fixture.Store)).IsEquivalentTo(state, CollectionOrdering.Matching);
        var replay = fixture.Submit(command).Get<CommitReceipt>();
        await Assert.That(JsonDefaults.Serialize(replay).AsSpan().SequenceEqual(JsonDefaults.Serialize(original))).IsTrue();
        await Assert.That(fixture.Store.Position).IsEqualTo(cut);
        await Assert.That(QueueWholeFlowStorage.Bytes(fixture.Store)).IsEquivalentTo(state, CollectionOrdering.Matching);
    }
}
