using static KeyLoad.FunctionalTests.EventStreams.EventQueueStateEnvelopeCommands;
using static KeyLoad.FunctionalTests.EventStreams.EventQueueStateEnvelopeProtocol;

namespace KeyLoad.FunctionalTests.EventStreams;

internal static class EventQueueStateEnvelopeProducer
{
    private const string DocumentMutation = "putDocument";
    private const string EdgeMutation = "upsertEdge";
    private const string AppendMutation = "appendEvents";
    private const string EnqueueMutation = "enqueue";
    private const string PublishMutation = "publishTopic";
    private const string TopicEventFamily = "topic-event";

    internal static async Task SeedAsync(EventQueueStateEnvelopeFixture fixture, EventQueueStateEnvelopeState state,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        _ = await CommitAsync(fixture, state,
            [new PutDocument(Documents, Item, EmptyObject, NoUsage), new PutDocument(Documents, Target, EmptyObject, NoUsage)],
            [new(DocumentMutation, Documents, Item, InitialRevision), new(DocumentMutation, Documents, Target, InitialRevision)], fixture.At);
        _ = await CommitAsync(fixture, state,
            [new UpsertEdge(Graph, Item, new(fixture.Partition, Documents, Item),
                new(fixture.Partition, Documents, Target), Label)],
            [new(EdgeMutation, Graph, Item, InitialRevision)], fixture.At);
        state.GraphBytes = Required(fixture, KeySpace.Partition(EdgeFamily, fixture.Partition, Graph, Item));
        await EventQueueStateEnvelopeGraphAssertions.CurrentAsync(fixture, state, token);
        _ = await CommitAsync(fixture, state,
            [new AppendEvents(Streams, Item, [new(Item, EventType, Payload, Headers)], ExpectedStreamRevision.NoStream),
                new PublishTopic(Topic, [new(Item, EventType, Payload, Headers)]),
                Message(fixture, scheduled: false), Message(fixture, scheduled: true)],
            [new(AppendMutation, Streams, Item, InitialRevision), new(PublishMutation, Topic, Item, InitialRevision),
                new(EnqueueMutation, Queue, Item, InitialRevision),
                new(EnqueueMutation, Queue, Scheduled, InitialRevision)], fixture.At);
        foreach (var key in new[]
        {
            KeySpace.Partition(BodyFamily, fixture.Partition, Queue, Item),
            KeySpace.Partition(BodyFamily, fixture.Partition, Queue, Scheduled),
            KeySpace.Partition(EventFamily, fixture.Partition, Streams, Item, InitialGeneration, InitialRevision),
            KeySpace.Partition(TopicEventFamily, fixture.Partition, Topic, InitialGeneration, InitialRevision)
        })
        {
            state.Bodies.Add(Convert.ToHexString(key), Required(fixture, key));
        }
        await EventQueueStateEnvelopeSourceAssertions.CurrentAsync(fixture, state);
        await EventQueueStateEnvelopeGraphAssertions.CurrentAsync(fixture, state, token);
    }

    internal static EnqueueMessage Message(EventQueueStateEnvelopeFixture fixture, bool scheduled)
    {
        return new(Queue, scheduled ? Scheduled : Item, Payload, Headers,
            NotBefore: scheduled ? fixture.ScheduledAt : null);
    }
}
