using KeyLoad.Query;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal sealed class SqlModelViewAstTests
{
    [Test]
    public async Task AcSqlView001AstSourcesRequireFullScanAndRejectContinuationBeforeExecution()
    {
        using var database = SqlModelViewTestSupport.Create();
        var query = new SelectQuery(SqlModelViewTestSupport.Queue, null, [new("*", "*")], null, [], 10,
            ModelSource: new(ModelQuerySourceKind.QueueMessages, SqlModelViewTestSupport.Queue));
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var noConsent = Assert.ThrowsExactly<KeyLoadException>(() => engine.ExecuteAst("root",
            new(database.Partition, query)));
        var cursor = Assert.ThrowsExactly<KeyLoadException>(() => engine.ExecuteAst("root",
            new(database.Partition, query, AllowFullScan: true, Cursor: "not-a-token")));
        var invalidKind = Assert.ThrowsExactly<KeyLoadException>(() => engine.ExecuteAst("root",
            new(database.Partition, query with
            {
                ModelSource = new((ModelQuerySourceKind)99,
                SqlModelViewTestSupport.Queue)
            }, AllowFullScan: true)));
        var mismatchedQueue = Assert.ThrowsExactly<KeyLoadException>(() => engine.ExecuteAst("root",
            new(database.Partition, query with
            {
                ModelSource = new(ModelQuerySourceKind.QueueMessages, "other")
            },
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
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var before = global::KeyLoad.FunctionalTests.Messaging.QueueWholeFlowStorage.Bytes(database.Store);
        var position = database.Store.Position;
        var queue = engine.ExecuteAst("root", new(database.Partition,
            new SelectQuery(SqlModelViewTestSupport.Queue, null, [new("*", "*")], null, [], 10, Explain: true,
                ModelSource: new(ModelQuerySourceKind.QueueMessages, SqlModelViewTestSupport.Queue)), AllowFullScan: true));
        var events = engine.ExecuteAst("root", new(database.Partition,
            new SelectQuery(SqlModelViewTestSupport.StreamSet, null, [new("*", "*")], null, [], 10, Explain: true,
                ModelSource: new(ModelQuerySourceKind.Events, SqlModelViewTestSupport.StreamId)), AllowFullScan: true));

        await Assert.That(queue.AccessPath).IsEqualTo("model-scan:queue-messages");
        await Assert.That(events.AccessPath).IsEqualTo("model-scan:events");
        await Assert.That(queue.Rows.Single().Json).DoesNotContain(SqlModelViewTestSupport.Secret);
        await Assert.That(queue.Rows.Single().Json).DoesNotContain("lease");
        await Assert.That(events.Rows.Single().Json).DoesNotContain(SqlModelViewTestSupport.Secret);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(global::KeyLoad.FunctionalTests.Messaging.QueueWholeFlowStorage.Bytes(database.Store)
            .SequenceEqual(before)).IsTrue();
    }
}
