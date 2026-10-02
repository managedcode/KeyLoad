using KeyLoad.Core;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class TopicPublicationTests
{
    private const string TopicName = "topic";
    private const string TopicHeadSpace = "topic-head";
    private const string EventSequenceSpace = "event-sequence";
    private const string EventStorageSpace = "topic-event";
    private const string ResourceName = "orders";
    private const string EventType = "Created";

    [Test]
    public async Task ExactSerializedEventQuotaAcceptsOrderedBatchAndOneByteShortRollsBack()
    {
        var events = new[] { new EventData("exact-a", EventType, "{}"), new EventData("exact-b", EventType, "{}") };
        using var exact = new TestDatabase();
        var publicationTime = SourceReadBusinessTime.Next(exact);
        var exactBytes = ExpectedRawEventBytes(exact, events, publicationTime);
        ConfigureTopic(exact, exactBytes, time: publicationTime);
        var exactId = Guid.NewGuid();
        var receipt = exact.Submit(OperationKind.Batch, new CommandRequest(exactId, exact.Partition,
            [new PublishTopic(TopicName, [.. events])]), id: exactId, time: publicationTime).Get<CommitReceipt>();
        var rawBytes = exact.Store.Read(view => RawEvent(view, exact, 1).LongLength + RawEvent(view, exact, 2).LongLength);
        await Assert.That(rawBytes).IsEqualTo(exactBytes);
        await Assert.That(receipt.Mutations[0].Revision).IsEqualTo(2L);
        var page = exact.Database.ReadEventSource("root", new(new(exact.Partition, TopicName, EventSourceKind.Topic)));
        await Assert.That(page.Events.Select(record => record.Position)).IsEquivalentTo(new long[] { 1, 2 }, CollectionOrdering.Matching);
        await Assert.That(page.Events.Select(record => record.EventSequence)).IsEquivalentTo(new long[] { 1, 2 }, CollectionOrdering.Matching);

        using var shortQuota = new TestDatabase();
        ConfigureTopic(shortQuota, exactBytes - 1, time: publicationTime);
        var shortId = Guid.NewGuid();
        var rejected = shortQuota.Submit(OperationKind.Batch, new CommandRequest(shortId, shortQuota.Partition,
            [new PublishTopic(TopicName, [.. events])]), id: shortId, time: publicationTime);
        await Assert.That(rejected.Error).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(shortQuota.Database.ReadEventSource("root", new(new(shortQuota.Partition, TopicName, EventSourceKind.Topic))).Head.TailPosition).IsEqualTo(0);
        await Assert.That(shortQuota.Store.Read(view => view.ReadOwnedValue(KeySpace.Partition(EventSequenceSpace, shortQuota.Partition)))).IsNull();
    }

    [Test]
    public async Task AbsentHeadUsesRequestedGenerationAndStaleHeadFailsBeforeMutation()
    {
        using var database = new TestDatabase();
        ConfigureTopic(database, long.MaxValue);
        var publicationTime = SourceReadBusinessTime.Next(database);
        var publishId = Guid.NewGuid();
        database.Submit(OperationKind.Batch, new CommandRequest(publishId, database.Partition,
            [new PublishTopic(TopicName, [new("absent", EventType, "{}")])]), id: publishId, time: publicationTime).Get<CommitReceipt>();
        await Assert.That(database.Database.ReadEventSource("root",
            new(new(database.Partition, TopicName, EventSourceKind.Topic))).Head.Generation).IsEqualTo(1);

        using var stale = new TestDatabase();
        ConfigureTopic(stale, long.MaxValue);
        var headKey = KeySpace.Partition(TopicHeadSpace, stale.Partition, TopicName);
        stale.Store.Commit((transaction, _) =>
        {
            transaction.Put(headKey, JsonDefaults.Serialize(new { TailPosition = 0L, FirstAvailablePosition = 1L, Generation = 2L, StoredBytes = 0L }));
            return 0;
        });
        var staleId = Guid.NewGuid();
        await Assert.That(stale.Submit(OperationKind.Batch, new CommandRequest(staleId, stale.Partition,
            [new PublishTopic(TopicName, [new("stale", EventType, "{}")])]), id: staleId,
            time: SourceReadBusinessTime.Next(stale)).Error)
            .IsEqualTo(ErrorCode.TokenInvalidated);
        await Assert.That(stale.Store.Read(view => view.ReadOwnedValue(KeySpace.Partition(EventSequenceSpace, stale.Partition)))).IsNull();
    }

    [Test]
    public async Task DuplicatePausedAndInvalidInputsKeepTheirCallerVisibleErrors()
    {
        using var duplicate = new TestDatabase();
        ConfigureTopic(duplicate, long.MaxValue);
        var duplicateTime = SourceReadBusinessTime.Next(duplicate);
        Publish(duplicate, [new("same", EventType, "{}")], Guid.NewGuid(), duplicateTime);
        var duplicateId = Guid.NewGuid();
        await Assert.That(duplicate.Submit(OperationKind.Batch, new CommandRequest(duplicateId, duplicate.Partition,
            [new PublishTopic(TopicName, [new("same", EventType, "{}")])]), id: duplicateId,
            time: duplicateTime.AddTicks(1)).Error)
            .IsEqualTo(ErrorCode.DuplicateEventId);

        using var paused = new TestDatabase();
        ConfigureTopic(paused, long.MaxValue, paused: true);
        var pausedId = Guid.NewGuid();
        await Assert.That(paused.Submit(OperationKind.Batch, new CommandRequest(pausedId, paused.Partition,
            [new PublishTopic(TopicName, [])]), id: pausedId,
            time: SourceReadBusinessTime.Next(paused)).Error).IsEqualTo(ErrorCode.DispatchPaused);

        using var invalid = new TestDatabase();
        ConfigureTopic(invalid, long.MaxValue);
        var invalidId = Guid.NewGuid();
        await Assert.That(invalid.Submit(OperationKind.Batch, new CommandRequest(invalidId, invalid.Partition,
            [new PublishTopic(TopicName, [new("", EventType, "{}")])]), id: invalidId,
            time: SourceReadBusinessTime.Next(invalid)).Error)
            .IsEqualTo(ErrorCode.Validation);
    }

    [Test]
    public async Task TopicQuotaFailureRollsBackProducerDocumentAndPartitionSequence()
    {
        using var database = new TestDatabase();
        database.Configure(ResourceName, ResourceKind.Collection);
        ConfigureTopic(database, long.MaxValue, maxEvents: 1);
        var publicationTime = SourceReadBusinessTime.Next(database);
        Publish(database, [new("first", EventType, "{}")], Guid.NewGuid(), publicationTime);
        var priorSequence = database.Store.Read(view => JsonDefaults.Deserialize<long>(view.ReadOwnedValue(KeySpace.Partition(EventSequenceSpace, database.Partition))!));
        var failedId = Guid.NewGuid();
        var failed = database.Submit(OperationKind.Batch, new CommandRequest(failedId, database.Partition,
            [new PutDocument(ResourceName, "rolled-back", "{}"), new PublishTopic(TopicName, [new("overflow", EventType, "{}")])]),
            id: failedId, time: publicationTime.AddMinutes(1));
        await Assert.That(failed.Error).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(database.Database.GetDocument("root", new(database.Partition, ResourceName, "rolled-back"))).IsNull();
        await Assert.That(database.Database.ReadEventSource("root", new(new(database.Partition, TopicName, EventSourceKind.Topic))).Head.TailPosition).IsEqualTo(1L);
        var currentSequence = database.Store.Read(view => JsonDefaults.Deserialize<long>(view.ReadOwnedValue(KeySpace.Partition(EventSequenceSpace, database.Partition))!));
        await Assert.That(currentSequence).IsEqualTo(priorSequence);
    }

    private static void ConfigureTopic(TestDatabase database, long maxBytes, long maxEvents = 100_000, bool paused = false,
        DateTimeOffset? time = null)
    {
        var definition = new ResourceDefinition(TopicName, ResourceKind.Topic, database.Partition.TransactionDomainId)
        {
            EventRetention = new() { MaxEvents = maxEvents, MaxBytes = maxBytes },
            Paused = paused
        };
        database.Submit(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(database.Partition.TenantId, database.Partition.DatabaseId, definition),
            time: time ?? SourceReadBusinessTime.Next(database)).Get<ResourceDefinition>();
    }

    private static long ExpectedRawEventBytes(TestDatabase database, EventData[] events, DateTimeOffset publicationTime)
    {
        var source = new EventSourceRef(database.Partition, TopicName, EventSourceKind.Topic);
        var first = JsonDefaults.Serialize(new SourceEventRecord(source, 1, 1, events[0], publicationTime)).LongLength;
        var second = JsonDefaults.Serialize(new SourceEventRecord(source, 2, 2, events[1], publicationTime)).LongLength;
        return first + second;
    }

    private static byte[] RawEvent(KeyLoad.Storage.IKeyValueView view, TestDatabase database, long position)
        => view.ReadOwnedValue(KeySpace.Partition(EventStorageSpace, database.Partition, TopicName, 1L, position))!;

    private static void Publish(TestDatabase database, EventData[] events, Guid commandId, DateTimeOffset publicationTime)
        => database.Submit(OperationKind.Batch, new CommandRequest(commandId, database.Partition,
            [new PublishTopic(TopicName, [.. events])]), id: commandId, time: publicationTime).Get<CommitReceipt>();
}
