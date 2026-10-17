using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class CoreNativeAccountingTests
{
    private const string Queue = "native-queue";
    private const string Topic = "native-topic";
    private const string Collection = "native-documents";
    private const string RecordId = "native-id";
    private const string EventType = "NativeEvent";
    private const string Json = "{\"text\":\"界λ\"}";
    private const string QueueBodySpace = "message-body";
    private const string QueueCountersSpace = "queue-counters";
    private const string TopicHeadSpace = "topic-head";
    private const string TopicEventSpace = "topic-event";
    private const string OutboxSpace = "outbox";
    private const string RootPrincipal = "root";

    [Test]
    public async Task AcIs006StoredBodyAndTopicCountersMatchActualNativeBytes()
    {
        using var database = new TestDatabase();
        database.Configure(Queue, ResourceKind.WorkQueue);
        database.Configure(Topic, ResourceKind.Topic);
        database.Commit(new EnqueueMessage(Queue, RecordId, Json),
            new PublishTopic(Topic, [new(RecordId, EventType, Json)]));
        var bodyKey = KeySpace.Partition(QueueBodySpace, database.Partition, Queue, RecordId);
        var bodyBytes = database.Store.Read(view => view.ReadOwnedValue(bodyKey))!;
        var body = NativeSerialization.Deserialize<MessageBody>(bodyBytes);
        var counters = database.Store.Read(view => view.GetRecord<QueueCounters>(
            KeySpace.Partition(QueueCountersSpace, database.Partition, Queue)))!;
        await Assert.That(counters.StoredBytes).IsEqualTo(bodyBytes.LongLength);
        await Assert.That(NativeSerialization.Measure(body)).IsEqualTo(bodyBytes.LongLength);
        var topicKey = KeySpace.Partition(TopicEventSpace, database.Partition, Topic, 1L, 1L);
        var topicBytes = database.Store.Read(view => view.ReadOwnedValue(topicKey))!;
        var sourceEvent = NativeSerialization.Deserialize<SourceEventRecord>(topicBytes);
        var head = database.Store.Read(view => view.GetRecord<TopicHead>(
            KeySpace.Partition(TopicHeadSpace, database.Partition, Topic)))!;
        await Assert.That(head.StoredBytes).IsEqualTo(topicBytes.LongLength);
        await Assert.That(NativeSerialization.Measure(sourceEvent)).IsEqualTo(topicBytes.LongLength);
    }

    [Test]
    public async Task AcIs006OutboxCountsExactNativeEntries()
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        database.Commit(new PutDocument(Collection, RecordId, Json));
        var bytes = database.Store.Read(view => view.ReadOwnedValue(
            KeySpace.Partition(OutboxSpace, database.Partition, 1L)))!;
        var entry = NativeSerialization.Deserialize<OutboxEntry>(bytes);
        var status = database.Database.GetOutboxStatus(RootPrincipal, database.Partition);
        await Assert.That(status.Head.StoredRecords).IsEqualTo(1L);
        await Assert.That(status.Head.StoredBytes).IsEqualTo(bytes.LongLength);
        await Assert.That(NativeSerialization.Measure(entry)).IsEqualTo(bytes.LongLength);
    }
}
