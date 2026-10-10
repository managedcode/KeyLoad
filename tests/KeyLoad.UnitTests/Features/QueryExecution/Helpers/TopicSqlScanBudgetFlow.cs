using System.Globalization;
using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class TopicSqlScanBudgetFlow
{
    private const int ScanRecords = 32;
    private const int TotalRecords = ScanRecords + TopicSqlProtocol.FirstPosition;
    private const int PurgeThrough = 8;
    private const int FirstAdditionalPosition = TopicSqlProtocol.SecondPosition + TopicSqlProtocol.FirstPosition;
    private const int AdditionalRecords = TotalRecords - TopicSqlProtocol.SecondPosition;
    private const int RetainedRecords = TotalRecords - PurgeThrough;
    private const string EventPrefix = "topic-budget-";

    internal static async Task RunAsync()
    {
        using var database = TopicSqlNativeSeed.Create(new() { MaxScanRecords = ScanRecords });
        var command = TopicSqlNativeSeed.Command(database);
        var receipt = TopicSqlNativeSeed.Publish(database, command);
        var append = new CommandRequest(Guid.NewGuid(), database.Partition,
            [new PublishTopic(TopicSqlProtocol.Topic,
                [.. Enumerable.Range(FirstAdditionalPosition, AdditionalRecords).Select(Event)])]);
        var appended = TopicSqlNativeSeed.Publish(database, append);
        var engine = TopicSqlNativeSeed.Engine(database);
        var bytes = QueueWholeFlowStorage.Bytes(database.Store);
        var position = database.Store.Position;
        var rejected = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(TopicSqlProtocol.Root,
            TopicSqlNativeSeed.Request(database, TopicSqlProtocol.LimitedSql)));
        await Assert.That(rejected.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await TopicSqlNativeAssertions.UnchangedAsync(database, bytes, position);
        var purge = new CommandRequest(Guid.NewGuid(), database.Partition,
            [new PurgeTopic(TopicSqlProtocol.Topic, PurgeThrough)]);
        var purged = database.Submit(OperationKind.Batch, purge, id: purge.CommandId).Get<CommitReceipt>();
        bytes = QueueWholeFlowStorage.Bytes(database.Store);
        position = database.Store.Position;
        var unavailable = Assert.ThrowsExactly<KeyLoadException>(() => TopicSqlNativeSeed.Read(database));
        await Assert.That(unavailable.Code).IsEqualTo(ErrorCode.HistoryUnavailable);
        await TopicSqlNativeAssertions.UnchangedAsync(database, bytes, position);
        var native = TopicSqlNativeSeed.Read(database, PurgeThrough);
        await Assert.That(native.Head.FirstAvailablePosition).IsEqualTo((long)PurgeThrough + TopicSqlProtocol.FirstPosition);
        await Assert.That(native.Head.TailPosition).IsEqualTo((long)TotalRecords);
        await Assert.That(native.Source).IsEqualTo(new EventSourceRef(database.Partition, TopicSqlProtocol.Topic, EventSourceKind.Topic));
        await Assert.That(native.HasMore).IsFalse();
        await Assert.That(native.Events.Select(value => value.Position)).IsEquivalentTo(
            Enumerable.Range(PurgeThrough + TopicSqlProtocol.FirstPosition, RetainedRecords).Select(value => (long)value),
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(native.Events.Select(value => value.Data)).IsEquivalentTo(
            Enumerable.Range(PurgeThrough + TopicSqlProtocol.FirstPosition, RetainedRecords).Select(Event),
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await TopicSqlNativeAssertions.RecordsAsync(native,
            engine.Execute(TopicSqlProtocol.Root, TopicSqlNativeSeed.Request(database)));
        foreach (var (original, observed) in new[] { (command, receipt), (append, appended), (purge, purged) })
        {
            var replay = database.Submit(OperationKind.Batch, original, id: original.CommandId).Get<CommitReceipt>();
            await Assert.That(JsonDefaults.Serialize(replay).AsSpan().SequenceEqual(JsonDefaults.Serialize(observed))).IsTrue();
        }
        await TopicSqlNativeAssertions.UnchangedAsync(database, bytes, position);
    }

    private static EventData Event(int position) => new(
        EventPrefix + position.ToString(CultureInfo.InvariantCulture), TopicSqlProtocol.Updated,
        TopicSqlProtocol.NextPayload, TopicSqlProtocol.Headers);
}
