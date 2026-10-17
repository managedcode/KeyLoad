using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal static class TopicRetentionAssertions
{
    private static readonly string[] ProtectedSpaces = ["document", "message-body", "message-meta",
        "queue-counters", "ready", "subscription", "subscription-window", "subscription-completion", "subscription-inbox"];
    internal static string[] ProtectedBytes(TopicRetentionFixture fixture) => fixture.Store.Read(view =>
        ProtectedSpaces.SelectMany(space => view.Scan(KeySpace.Partition(space, fixture.Owner.Partition), 100).Records)
            .Select(item => Convert.ToHexString(item.Key.Span) + ":" + Convert.ToHexString(item.Value.Span)).ToArray());

    internal static string[] ModelBytes(TopicRetentionFixture fixture) => fixture.Store.Read(view =>
        ProtectedSpaces.Concat(new[] { "topic-head", "topic-event", "topic-event-id", "event-sequence" })
            .SelectMany(space => view.Scan(KeySpace.Partition(space, fixture.Owner.Partition), 100).Records)
            .Select(item => Convert.ToHexString(item.Key.Span) + ":" + Convert.ToHexString(item.Value.Span)).ToArray());

    internal static async Task Reject(TopicRetentionFixture fixture, CommandRequest command, ErrorCode expected,
        string principal = "root")
    {
        var before = ModelBytes(fixture);
        var head = fixture.Store.Read(view => view.GetRecord<TopicHead>(
            KeySpace.Partition("topic-head", fixture.Owner.Partition, "topic")));
        var initialPosition = fixture.Store.Position;
        var failed = fixture.Submit(command, principal);
        await Assert.That(failed.Error).IsEqualTo(expected);
        await Assert.That(fixture.Store.Position).IsEqualTo(initialPosition + 1);
        var cut = fixture.Store.Position;
        var all = QueueWholeFlowStorage.Bytes(fixture.Store);
        var replay = fixture.Submit(command, principal);
        await Assert.That(JsonDefaults.Serialize(replay).AsSpan().SequenceEqual(JsonDefaults.Serialize(failed))).IsTrue();
        await Assert.That(fixture.Store.Position).IsEqualTo(cut);
        await Assert.That(QueueWholeFlowStorage.Bytes(fixture.Store)).IsEquivalentTo(all, CollectionOrdering.Matching);
        await Assert.That(ModelBytes(fixture)).IsEquivalentTo(before, CollectionOrdering.Matching);
        await Assert.That(fixture.Store.Read(view => view.GetRecord<TopicHead>(
            KeySpace.Partition("topic-head", fixture.Owner.Partition, "topic")))).IsEqualTo(head);
    }

    internal static async Task RetainedTail(TopicRetentionFixture fixture)
    {
        var page = fixture.Database.ReadEventSource("root", new(fixture.Source, AfterPosition: 2));
        await Assert.That(page.Head).IsEqualTo(new EventSourceHead(3, 3, 1));
        var record = page.Events.Single();
        await Assert.That(record.Source).IsEqualTo(fixture.Source);
        await Assert.That(record.Position).IsEqualTo(3L);
        await Assert.That(record.EventSequence).IsEqualTo(3L);
        await Assert.That(record.RecordedAt).IsEqualTo(fixture.PublicationAt);
        await Assert.That(record.Data).IsEqualTo(new EventData("event3", "Created", "{\"n\":3}"));
        var document = fixture.Database.GetDocument("root", new(fixture.Owner.Partition, "orders", "derived"));
        await Assert.That(document!.Json).IsEqualTo("{\"done\":true}");
        await Assert.That(document.Revision).IsEqualTo(1L);
        var message = fixture.Database.InspectMessage("root", new(fixture.Owner.Partition, "work"), "derived");
        await Assert.That(message!.PayloadJson).IsEqualTo("{\"input\":1}");
        await Assert.That(message.HeadersJson).IsEqualTo("{}");
        await Assert.That(message.Metadata).IsEqualTo(new MessageMetadata("derived", MessageState.Ready, 0, 1, 1, null, null));
        var missing = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Database.ReadEventSource("root", new(fixture.Source)));
        await Assert.That(missing.Code).IsEqualTo(ErrorCode.HistoryUnavailable);
        await Assert.That(missing.Message).IsEqualTo("The requested event history was retained away.");
        var cursorLost = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Database.ReadEventSource("root",
            new(fixture.Source, Cursor: fixture.OriginalCursor)));
        await Assert.That(cursorLost.Code).IsEqualTo(ErrorCode.HistoryUnavailable);
    }
}
