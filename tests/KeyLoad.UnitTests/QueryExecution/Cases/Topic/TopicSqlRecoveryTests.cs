using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class TopicSqlRecoveryTests
{
    [Test]
    public async Task ActualTopicMissingAndChangedRetainedRecordRefuseWithoutMutationThenExactRepairReadsHealthy()
    {
        using var database = TopicSqlSeed.Create();
        var command = TopicSqlSeed.Command(database);
        var receipt = TopicSqlSeed.Publish(database, command);
        var key = KeySpace.Partition(TopicSqlProtocol.TopicEventSpace, database.Partition,
            TopicSqlProtocol.Topic, (long)TopicSqlProtocol.FirstPosition, (long)TopicSqlProtocol.SecondPosition);
        var original = database.Store.Read(view => view.ReadOwnedValue(key))!;
        database.Store.Commit((transaction, _) => { transaction.Delete(key); return true; });
        await RefuseAsync(database, ErrorCode.HistoryUnavailable);
        database.Store.Commit((transaction, _) => { transaction.Put(key, original); return true; });
        var record = OrleansSerialization.Deserialize<SourceEventRecord>(original);
        var changed = OrleansSerialization.Serialize(record with
        {
            Position = TopicSqlProtocol.FirstPosition
        });
        database.Store.Commit((transaction, _) => { transaction.Put(key, changed); return true; });
        await RefuseAsync(database, ErrorCode.Corruption);
        database.Store.Commit((transaction, _) => { transaction.Put(key, original); return true; });
        await TopicSqlAssertions.RecordsAsync(TopicSqlSeed.Read(database),
            TopicSqlSeed.Engine(database).Execute(TopicSqlProtocol.Root, TopicSqlSeed.Request(database)));
        await Assert.That(JsonDefaults.Serialize(TopicSqlSeed.Publish(database, command)).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(receipt))).IsTrue();
    }

    [Test]
    public async Task ActualTopicCompleteHistoryScanOverflowAndOriginalCancellationRejectThenPurgeMakesReadHealthy()
    {
        using var database = TopicSqlSeed.Create(new()
        {
            MaxScanRecords = TopicSqlProtocol.FirstPosition
        });
        TopicSqlSeed.Publish(database, TopicSqlSeed.Command(database));
        await RefuseAsync(database, ErrorCode.BudgetExceeded);
        var engine = TopicSqlSeed.Engine(database);
        var bytes = QueueWholeFlowStorage.Bytes(database.Store);
        var position = database.Store.Position;
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        Assert.ThrowsExactly<OperationCanceledException>(() => engine.Execute(TopicSqlProtocol.Root,
            TopicSqlSeed.Request(database), cancellationToken: cancelled.Token));
        await TopicSqlAssertions.UnchangedAsync(database, bytes, position);
        await TopicSqlScanBudgetFlow.RunAsync();
        await TopicSqlAssertions.UnchangedAsync(database, bytes, position);
    }

    private static async Task RefuseAsync(TestDatabase database, ErrorCode code)
    {
        var bytes = QueueWholeFlowStorage.Bytes(database.Store);
        var position = database.Store.Position;
        var error = Assert.ThrowsExactly<KeyLoadException>(() => TopicSqlSeed.Engine(database).Execute(
            TopicSqlProtocol.Root, TopicSqlSeed.Request(database, TopicSqlProtocol.LimitedSql)));
        await Assert.That(error.Code).IsEqualTo(code);
        await TopicSqlAssertions.UnchangedAsync(database, bytes, position);
    }
}
