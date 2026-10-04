using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class SqlModelViewBudgetTests
{
    [Test]
    public async Task AcSqlView005FullScanCandidateOverflowRejectsTheWholeEventOrQueueRequest()
    {
        using var database = SqlModelViewTestSupport.Create(new() { MaxScanRecords = 1 });
        SqlModelViewTestSupport.Seed(database);
        var engine = new QueryEngine(database.Database);
        var exactLimit = engine.Execute("root",
            SqlModelViewTestSupport.Request(database, "SELECT * FROM QUEUE_MESSAGES('jobs')"));
        await Assert.That(exactLimit.Rows).HasSingleItem();
        database.Commit(
            new EnqueueMessage(SqlModelViewTestSupport.Queue, "message-b", "{}"),
            new AppendEvents(SqlModelViewTestSupport.StreamSet, SqlModelViewTestSupport.StreamId,
                [new("event-b", "Updated", "{}")], ExpectedStreamRevision.Any));
        var eventOverflow = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute("root",
            SqlModelViewTestSupport.Request(database, "SELECT * FROM EVENTS('events', 'stream-a')")));
        var queueOverflow = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute("root",
            SqlModelViewTestSupport.Request(database, "SELECT * FROM QUEUE_MESSAGES('jobs')")));

        await Assert.That(eventOverflow.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(queueOverflow.Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }

    [Test]
    public async Task AcSqlView005CancellationReleasesAdmissionAndLeavesTheNextReadHealthy()
    {
        using var database = SqlModelViewTestSupport.Create();
        SqlModelViewTestSupport.Seed(database);
        var engine = new QueryEngine(database.Database);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        Assert.ThrowsExactly<OperationCanceledException>(() => engine.Execute("root",
            SqlModelViewTestSupport.Request(database, "SELECT * FROM QUEUE_MESSAGES('jobs')"),
            cancellationToken: cancelled.Token));
        var healthy = engine.Execute("root",
            SqlModelViewTestSupport.Request(database, "SELECT * FROM QUEUE_MESSAGES('jobs')"));

        await Assert.That(healthy.Rows).HasSingleItem();
        await Assert.That(healthy.Cursor).IsNull();
    }
}
