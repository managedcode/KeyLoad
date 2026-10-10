using KeyLoad.Core;
using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class TopicSqlNativeRecoveryTests
{
    [Test]
    public async Task ActualTopicMissingAndChangedRetainedRecordRefuseWithoutMutationThenExactRepairReadsHealthy()
    {
        using var database = TopicSqlNativeSeed.Create();
        var command = TopicSqlNativeSeed.Command(database);
        var receipt = TopicSqlNativeSeed.Publish(database, command);
        var key = KeySpace.Partition(TopicSqlProtocol.TopicEventSpace, database.Partition,
            TopicSqlProtocol.Topic, (long)TopicSqlProtocol.FirstPosition, (long)TopicSqlProtocol.SecondPosition);
        var original = database.Store.Read(view => view.ReadOwnedValue(key))!;
        database.Store.Commit((transaction, _) => { transaction.Delete(key); return true; });
        await RefuseAsync(database, ErrorCode.HistoryUnavailable);
        database.Store.Commit((transaction, _) => { transaction.Put(key, original); return true; });
        var record = NativeSerialization.Deserialize<SourceEventRecord>(original);
        var changed = NativeSerialization.Serialize(record with { Position = TopicSqlProtocol.FirstPosition });
        database.Store.Commit((transaction, _) => { transaction.Put(key, changed); return true; });
        await RefuseAsync(database, ErrorCode.Corruption);
        database.Store.Commit((transaction, _) => { transaction.Put(key, original); return true; });
        await TopicSqlNativeAssertions.RecordsAsync(TopicSqlNativeSeed.Read(database),
            TopicSqlNativeSeed.Engine(database).Execute(TopicSqlProtocol.Root, TopicSqlNativeSeed.Request(database)));
        await Assert.That(JsonDefaults.Serialize(TopicSqlNativeSeed.Publish(database, command)).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(receipt))).IsTrue();
    }

    [Test]
    public async Task ActualTopicCompleteHistoryScanOverflowAndOriginalCancellationRejectThenPurgeMakesReadHealthy()
    {
        using var database = TopicSqlNativeSeed.Create(new() { MaxScanRecords = TopicSqlProtocol.FirstPosition });
        TopicSqlNativeSeed.Publish(database, TopicSqlNativeSeed.Command(database));
        await RefuseAsync(database, ErrorCode.BudgetExceeded);
        var engine = TopicSqlNativeSeed.Engine(database);
        var bytes = QueueWholeFlowStorage.Bytes(database.Store);
        var position = database.Store.Position;
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        Assert.ThrowsExactly<OperationCanceledException>(() => engine.Execute(TopicSqlProtocol.Root,
            TopicSqlNativeSeed.Request(database), cancellationToken: cancelled.Token));
        await TopicSqlNativeAssertions.UnchangedAsync(database, bytes, position);
        await TopicSqlScanBudgetFlow.RunAsync();
        await TopicSqlNativeAssertions.UnchangedAsync(database, bytes, position);
    }

    private static async Task RefuseAsync(TestDatabase database, ErrorCode code)
    {
        var bytes = QueueWholeFlowStorage.Bytes(database.Store);
        var position = database.Store.Position;
        var error = Assert.ThrowsExactly<KeyLoadException>(() => TopicSqlNativeSeed.Engine(database).Execute(
            TopicSqlProtocol.Root, TopicSqlNativeSeed.Request(database, TopicSqlProtocol.LimitedSql)));
        await Assert.That(error.Code).IsEqualTo(code);
        await TopicSqlNativeAssertions.UnchangedAsync(database, bytes, position);
    }
}
