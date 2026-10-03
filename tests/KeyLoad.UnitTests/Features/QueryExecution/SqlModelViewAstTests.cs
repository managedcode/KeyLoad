using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class SqlModelViewAstTests
{
    [Test]
    public async Task AcSqlView001AstSourcesRequireFullScanAndRejectContinuationBeforeExecution()
    {
        using var database = SqlModelViewTestSupport.Create();
        var query = new SelectQuery(SqlModelViewTestSupport.Queue, null, [new("*", "*")], null, [], 10,
            ModelSource: new(ModelQuerySourceKind.QueueMessages, SqlModelViewTestSupport.Queue));
        var engine = new QueryEngine(database.Database);
        var noConsent = Assert.ThrowsExactly<KeyLoadException>(() => engine.ExecuteAst("root",
            new(database.Partition, query)));
        var cursor = Assert.ThrowsExactly<KeyLoadException>(() => engine.ExecuteAst("root",
            new(database.Partition, query, AllowFullScan: true, Cursor: "not-a-token")));
        var invalidKind = Assert.ThrowsExactly<KeyLoadException>(() => engine.ExecuteAst("root",
            new(database.Partition, query with { ModelSource = new((ModelQuerySourceKind)99,
                SqlModelViewTestSupport.Queue) }, AllowFullScan: true)));
        var mismatchedQueue = Assert.ThrowsExactly<KeyLoadException>(() => engine.ExecuteAst("root",
            new(database.Partition, query with { ModelSource = new(ModelQuerySourceKind.QueueMessages, "other") },
                AllowFullScan: true)));

        await Assert.That(noConsent.Code).IsEqualTo(ErrorCode.UnsupportedCapability);
        await Assert.That(cursor.Code).IsEqualTo(ErrorCode.UnsupportedCapability);
        await Assert.That(invalidKind.Code).IsEqualTo(ErrorCode.UnsupportedCapability);
        await Assert.That(mismatchedQueue.Code).IsEqualTo(ErrorCode.Validation);
    }

    [Test]
    public async Task AcSqlView001ExplainSupportsBothSourcesWithoutScanningBodies()
    {
        using var database = SqlModelViewTestSupport.Create();
        SqlModelViewTestSupport.Seed(database);
        var engine = new QueryEngine(database.Database);
        var queue = engine.Execute("root", SqlModelViewTestSupport.Request(database,
            "EXPLAIN SELECT * FROM QUEUE_MESSAGES('jobs')"));
        var events = engine.Execute("root", SqlModelViewTestSupport.Request(database,
            "EXPLAIN SELECT * FROM EVENTS('events', 'stream-a')"));

        await Assert.That(queue.AccessPath).IsEqualTo("model-scan:queue-messages");
        await Assert.That(events.AccessPath).IsEqualTo("model-scan:events");
        await Assert.That(queue.Rows.Single().Json).DoesNotContain(SqlModelViewTestSupport.Secret);
        await Assert.That(queue.Rows.Single().Json).DoesNotContain("lease");
        await Assert.That(events.Rows.Single().Json).DoesNotContain(SqlModelViewTestSupport.Secret);
    }
}
