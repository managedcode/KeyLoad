using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class EventingArtifactState
{
    private const int MaximumRecords = 4_096;
    private const string Overflow = "The bounded eventing backup fixture overflowed.";
    private const string Separator = ":";
    private static readonly string[] Families = ["topic-head", "topic-event", "topic-event-id", "event-sequence",
        "subscription", "subscription-window", "subscription-completion", "subscription-inbox", "document", "document-epoch",
        "message-body", "message-meta", "queue-counters", "ready", "lease", "outbox", "outbox-head"];
    private static readonly string[] Effects = ["document", "document-epoch", "message-body", "message-meta", "queue-counters", "ready", "lease"];

    internal static async Task AssertSeedAsync(EventingArtifactFixture fixture, DatabaseEngine database)
    {
        var page = database.ReadEventSource(EventingArtifactFixture.Principal, new(fixture.Events));
        await Assert.That(page.Head).IsEqualTo(new EventSourceHead(3, 1, 1));
        await Assert.That(page.Events.Select(item => item.Position)).IsEquivalentTo(new long[] { 1, 2, 3 }, CollectionOrdering.Matching);
        await Assert.That(page.Events.Select(item => item.EventSequence)).IsEquivalentTo(new long[] { 1, 2, 3 }, CollectionOrdering.Matching);
        await Assert.That(page.Events.Select(item => item.Data.EventId)).IsEquivalentTo(new[] { EventingArtifactFixture.FirstEvent, EventingArtifactFixture.SecondEvent, EventingArtifactFixture.ThirdEvent }, CollectionOrdering.Matching);
        await Assert.That(page.Events.All(item => item.Data.PayloadJson == EventingArtifactFixture.Json && item.RecordedAt == fixture.Time)).IsTrue();
        await Assert.That(page.HasMore).IsFalse();
        await Assert.That(page.CutPosition).IsEqualTo(database.Store.Position);
        var group = database.GetSubscription(EventingArtifactFixture.Principal, fixture.Group);
        var expectedGroup = new SubscriptionInfo(fixture.Group, new(EventingArtifactFixture.Principal), 1, 1, 1, 3, 3, false, null);
        await Assert.That(JsonSerializer.Serialize(group, JsonDefaults.Options)).IsEqualTo(JsonSerializer.Serialize(expectedGroup, JsonDefaults.Options));
        var other = database.GetSubscription(EventingArtifactFixture.Principal, fixture.Other);
        var expectedOther = new SubscriptionInfo(fixture.Other, new(EventingArtifactFixture.Principal), 1, 1, 0, 1, 3, false, null);
        await Assert.That(JsonSerializer.Serialize(other, JsonDefaults.Options)).IsEqualTo(JsonSerializer.Serialize(expectedOther, JsonDefaults.Options));
        var document = database.GetDocument(EventingArtifactFixture.Principal,
            new(fixture.Source.Partition, EventingArtifactFixture.Documents, EventingArtifactFixture.DocumentId));
        await Assert.That(document!.Reference).IsEqualTo(new EntityRef(fixture.Source.Partition, EventingArtifactFixture.Documents, EventingArtifactFixture.DocumentId));
        await Assert.That(document.Redacted).IsFalse();
        await Assert.That(document.RedactedFields).IsEmpty();
        await Assert.That(document.Revision).IsEqualTo(1L);
        await Assert.That(document.Json).IsEqualTo(EventingArtifactFixture.Json);
        var message = database.InspectMessage(EventingArtifactFixture.Principal, fixture.Lane, EventingArtifactFixture.MessageId);
        var expectedMessage = new MessageInspection(new(EventingArtifactFixture.MessageId, MessageState.Leased, 1, 2, 1,
            null, null, EventingArtifactFixture.Principal, 1, fixture.Time.AddSeconds(30)), EventingArtifactFixture.Json, EventingArtifactFixture.EmptyJson);
        await Assert.That(JsonSerializer.Serialize(message, JsonDefaults.Options)).IsEqualTo(JsonSerializer.Serialize(expectedMessage, JsonDefaults.Options));
        await Assert.That(database.GetOutboxStatus(EventingArtifactFixture.Principal, fixture.Source.Partition).Head.Tail).IsEqualTo(3L);

    }

    internal static string[] Bytes(IAtomicStore store, PartitionRef partition, bool effectsOnly = false)
        => store.Read(view => (effectsOnly ? Effects : Families).SelectMany(family =>
        {
            var page = view.Scan(KeySpace.Partition(family, partition), MaximumRecords);
            if (page.HasMore)
            { throw new InvalidOperationException(Overflow); }
            return page.Records.Select(item => Convert.ToHexString(item.Key.Span) + Separator + Convert.ToHexString(item.Value.Span));
        }).ToArray());
    internal static async Task SameAsync(string[] expected, IAtomicStore store, PartitionRef partition, bool effectsOnly = false)
        => await Assert.That(Bytes(store, partition, effectsOnly)).IsEquivalentTo(expected, CollectionOrdering.Matching);
    internal static string[] FullBytes(IAtomicStore store) => store.Read(view =>
    {
        var page = view.Scan([], MaximumRecords);
        if (page.HasMore)
        { throw new InvalidOperationException(Overflow); }
        return page.Records.Select(item => Convert.ToHexString(item.Key.Span) + Separator + Convert.ToHexString(item.Value.Span)).ToArray();
    });
    internal static async Task SameResultAsync(OperationResult expected, OperationResult actual)
        => await Assert.That(Convert.ToHexString(NativeSerialization.Serialize(actual)))
            .IsEqualTo(Convert.ToHexString(NativeSerialization.Serialize(expected)));
}
