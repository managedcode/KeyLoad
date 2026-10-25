using KeyLoad.Storage;

namespace KeyLoad.FunctionalTests.InternalSerialization;

internal sealed class CoreAccountingTests
{
    private const string Queue = "execution-queue";
    private const string Topic = "execution-topic";
    private const string Collection = "execution-documents";
    private const string RecordId = "execution-id";
    private const string EventType = "RuntimeEvent";
    private const string Json = "{\"text\":\"界λ\"}";
    private const string QueueBodySpace = "message-body";
    private const string QueueCountersSpace = "queue-counters";
    private const string TopicHeadSpace = "topic-head";
    private const string TopicEventSpace = "topic-event";
    private const string OutboxSpace = "outbox";
    private const string RootPrincipal = "root";

    [Test]
    public async Task AcIs006StoredBodyAndTopicCountersMatchActualBytes()
    {
        using var database = new TestDatabase();
        database.Configure(Queue, ResourceKind.WorkQueue);
        database.Configure(Topic, ResourceKind.Topic);
        database.Commit(new EnqueueMessage(Queue, RecordId, Json),
            new PublishTopic(Topic, [new(RecordId, EventType, Json)]));
        var bodyKey = KeySpace.Partition(QueueBodySpace, database.Partition, Queue, RecordId);
        var bodyBytes = database.Store.Read(view => view.ReadOwnedValue(bodyKey))!;
        var body = OrleansSerialization.Deserialize<MessageBody>(bodyBytes);
        var counters = database.Store.Read(view => view.GetRecord<QueueCounters>(
            KeySpace.Partition(QueueCountersSpace, database.Partition, Queue)))!;
        await Assert.That(counters.StoredBytes).IsEqualTo(bodyBytes.LongLength);
        await Assert.That(OrleansSerialization.Measure(body)).IsEqualTo(bodyBytes.LongLength);
        var topicKey = KeySpace.Partition(TopicEventSpace, database.Partition, Topic, 1L, 1L);
        var topicBytes = database.Store.Read(view => view.ReadOwnedValue(topicKey))!;
        var sourceEvent = OrleansSerialization.Deserialize<SourceEventRecord>(topicBytes);
        var head = database.Store.Read(view => view.GetRecord<TopicHead>(
            KeySpace.Partition(TopicHeadSpace, database.Partition, Topic)))!;
        await Assert.That(head.StoredBytes).IsEqualTo(topicBytes.LongLength);
        await Assert.That(OrleansSerialization.Measure(sourceEvent)).IsEqualTo(topicBytes.LongLength);
    }

    [Test]
    public async Task AcIs006OutboxCountsExactEntries()
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        database.Commit(new PutDocument(Collection, RecordId, Json));
        var bytes = database.Store.Read(view => view.ReadOwnedValue(
            KeySpace.Partition(OutboxSpace, database.Partition, 1L)))!;
        var entry = OrleansSerialization.Deserialize<OutboxEntry>(bytes);
        var status = database.Database.GetOutboxStatus(RootPrincipal, database.Partition);
        await Assert.That(status.Head.StoredRecords).IsEqualTo(1L);
        await Assert.That(status.Head.StoredBytes).IsEqualTo(bytes.LongLength);
        await Assert.That(OrleansSerialization.Measure(entry)).IsEqualTo(bytes.LongLength);
    }
}
