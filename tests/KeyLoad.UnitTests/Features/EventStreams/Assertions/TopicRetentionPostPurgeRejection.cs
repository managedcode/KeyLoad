using KeyLoad.UnitTests.Features.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal static class TopicRetentionPostPurgeRejection
{
    internal static async Task RejectAsync(TopicRetentionFixture fixture, CommandRequest command, ErrorCode code,
        string principal = "root")
    {
        var protectedState = TopicRetentionAssertions.ModelBytes(fixture);
        var tail = fixture.Database.ReadEventSource("root", new(fixture.Source, AfterPosition: 2));
        var position = fixture.Store.Position;
        var failed = fixture.Submit(command, principal);
        await Assert.That(failed.Error).IsEqualTo(code);
        await Assert.That(fixture.Store.Position).IsEqualTo(position + 1);
        var cut = fixture.Store.Position;
        var state = QueueWholeFlowStorage.Bytes(fixture.Store);
        var replay = fixture.Submit(command, principal);
        await Assert.That(JsonDefaults.Serialize(replay).AsSpan().SequenceEqual(JsonDefaults.Serialize(failed))).IsTrue();
        await Assert.That(fixture.Store.Position).IsEqualTo(cut);
        await Assert.That(QueueWholeFlowStorage.Bytes(fixture.Store)).IsEquivalentTo(state, CollectionOrdering.Matching);
        await Assert.That(TopicRetentionAssertions.ModelBytes(fixture)).IsEquivalentTo(protectedState, CollectionOrdering.Matching);
        var after = fixture.Database.ReadEventSource("root", new(fixture.Source, AfterPosition: 2));
        await Assert.That(after.Head).IsEqualTo(tail.Head);
        await Assert.That(JsonDefaults.Serialize(after.Events).AsSpan().SequenceEqual(JsonDefaults.Serialize(tail.Events))).IsTrue();
    }
}
