using System.Text.Json;
using KeyLoad.Storage;
using static KeyLoad.UnitTests.Features.EventStreams.EventQueueStateEnvelopeCommands;
using static KeyLoad.UnitTests.Features.EventStreams.EventQueueStateEnvelopeProtocol;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal static class EventQueueStateEnvelopeSourceAssertions
{
    private const string HeadFamily = "stream-head";
    private const string FeedFamily = "event-feed";
    private const string IdentityFamily = "event-id";
    private const string SequenceFamily = "event-sequence";
    private const string TopicEventFamily = "topic-event";
    private const string TopicHeadFamily = "topic-head";
    private const string TopicIdentityFamily = "topic-event-id";

    internal static async Task CurrentAsync(EventQueueStateEnvelopeFixture fixture, EventQueueStateEnvelopeState state)
    {
        var record = new EventRecord(fixture.Stream, InitialRevision, InitialRevision,
            new(Item, EventType, Payload, Headers), fixture.At);
        await EqualAsync(fixture.Store.Read(view => view.GetRecord<StreamHead>(
            KeySpace.Partition(HeadFamily, fixture.Partition, Streams, Item))),
            new StreamHead(InitialRevision, InitialRevision, InitialGeneration));
        foreach (var key in new[]
        {
            KeySpace.Partition(EventFamily, fixture.Partition, Streams, Item, InitialGeneration, InitialRevision),
            KeySpace.Partition(FeedFamily, fixture.Partition, InitialRevision)
        })
        {
            await EqualAsync(OrleansSerialization.Deserialize<EventRecord>(Required(fixture, key)), record);
        }
        await EqualAsync(fixture.Store.Read(view => view.GetRecord<EventIdentity>(KeySpace.Partition(
            IdentityFamily, fixture.Partition, Streams, Item, InitialGeneration, Item))),
            new EventIdentity(Item, InitialGeneration, InitialRevision));
        await Assert.That(OrleansSerialization.Deserialize<long>(Required(fixture,
            KeySpace.Partition(SequenceFamily, fixture.Partition)))).IsEqualTo(TopicEventSequence);
        var expectedPage = new StreamPage(fixture.Stream, new(InitialRevision, InitialRevision, InitialGeneration),
            [record], fixture.Database.LastApplied, false, null, fixture.Database.LastApplied);
        await EqualAsync(fixture.Database.ReadStream(Root, fixture.Stream, limit: SingleResult), expectedPage);
        foreach (var family in new[] { HeadFamily, EventFamily, FeedFamily, IdentityFamily, SequenceFamily })
        {
            var page = fixture.Store.Read(view => view.Scan(KeySpace.Partition(family, fixture.Partition), ScanBound));
            await Assert.That(page.HasMore).IsFalse();
            await Assert.That(page.Records.Length).IsEqualTo(SingleResult);
        }
        await TopicAsync(fixture);
        await ClassificationAsync(fixture);
        await RetainSourcesAsync(fixture, state);
    }

    private static async Task RetainSourcesAsync(EventQueueStateEnvelopeFixture fixture, EventQueueStateEnvelopeState state)
    {
        var keys = new[]
        {
            KeySpace.Partition(HeadFamily, fixture.Partition, Streams, Item),
            KeySpace.Partition(EventFamily, fixture.Partition, Streams, Item, InitialGeneration, InitialRevision),
            KeySpace.Partition(FeedFamily, fixture.Partition, InitialRevision),
            KeySpace.Partition(IdentityFamily, fixture.Partition, Streams, Item, InitialGeneration, Item),
            KeySpace.Partition(SequenceFamily, fixture.Partition),
            KeySpace.Partition(TopicEventFamily, fixture.Partition, Topic, InitialGeneration, InitialRevision),
            KeySpace.Partition(TopicHeadFamily, fixture.Partition, Topic),
            KeySpace.Partition(TopicIdentityFamily, fixture.Partition, Topic, InitialGeneration, Item),
            KeySpace.Resource(fixture.Partition.TenantId, fixture.Partition.DatabaseId, Streams),
            KeySpace.Resource(fixture.Partition.TenantId, fixture.Partition.DatabaseId, Topic),
            KeySpace.Resource(fixture.Partition.TenantId, fixture.Partition.DatabaseId, Queue)
        };
        foreach (var key in keys)
        {
            var bytes = Required(fixture, key);
            var identity = Convert.ToHexString(key);
            if (state.SourceRows.TryGetValue(identity, out var original))
            {
                await Assert.That(bytes.AsSpan().SequenceEqual(original)).IsTrue();
            }
            else
            {
                state.SourceRows.Add(identity, bytes);
            }
        }
    }

    private static async Task TopicAsync(EventQueueStateEnvelopeFixture fixture)
    {
        var key = KeySpace.Partition(TopicEventFamily, fixture.Partition, Topic, InitialGeneration, InitialRevision);
        var body = Required(fixture, key);
        await EqualAsync(OrleansSerialization.Deserialize<SourceEventRecord>(body),
            new SourceEventRecord(fixture.TopicSource, InitialRevision, TopicEventSequence,
                new(Item, EventType, Payload, Headers), fixture.At));
        await EqualAsync(fixture.Store.Read(view => view.GetRecord<TopicHead>(
            KeySpace.Partition(TopicHeadFamily, fixture.Partition, Topic))),
            new TopicHead(InitialRevision, InitialRevision, InitialGeneration, body.LongLength));
        await Assert.That(OrleansSerialization.Deserialize<long>(Required(fixture, KeySpace.Partition(
            TopicIdentityFamily, fixture.Partition, Topic, InitialGeneration, Item)))).IsEqualTo(InitialRevision);
        foreach (var family in new[] { TopicEventFamily, TopicHeadFamily, TopicIdentityFamily })
        {
            var page = fixture.Store.Read(view => view.Scan(KeySpace.Partition(family, fixture.Partition, Topic), ScanBound));
            await Assert.That(page.HasMore).IsFalse();
            await Assert.That(page.Records.Length).IsEqualTo(SingleResult);
        }
    }

    private static async Task ClassificationAsync(EventQueueStateEnvelopeFixture fixture)
    {
        foreach (var pair in new[] { (Streams, ResourceKind.StreamSet), (Queue, ResourceKind.WorkQueue), (Topic, ResourceKind.Topic) })
        {
            var expected = new ResourceDefinition(pair.Item1, pair.Item2, fixture.Partition.TransactionDomainId)
            {
                FieldPolicies = [new(SecretPath, Classification)],
                HeaderPolicies = [new(SecretPath, Classification)]
            };
            var key = KeySpace.Resource(fixture.Partition.TenantId, fixture.Partition.DatabaseId, pair.Item1);
            await EqualAsync(fixture.Store.Read(view => view.GetRecord<ResourceDefinition>(key)), expected);
        }
    }

    internal static async Task EqualAsync<T>(T actual, T expected)
    {
        await Assert.That(JsonSerializer.Serialize(actual, JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(expected, JsonDefaults.Options));
    }
}
