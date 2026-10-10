using KeyLoad.Query;
using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class TopicSqlNativeBoundary
{
    internal static async Task RunAsync(TestDatabase database, QueryEngine engine)
    {
        database.Configure(TopicSqlProtocol.QuotedCollection, ResourceKind.Collection);
        database.Commit(new PutDocument(TopicSqlProtocol.QuotedCollection, TopicSqlProtocol.OrdinaryRow, TopicSqlProtocol.EmptyJson));
        var before = QueueWholeFlowStorage.Bytes(database.Store);
        var position = database.Store.Position;
        foreach (var sql in new[] { TopicSqlProtocol.InvalidGenerationSql, TopicSqlProtocol.ExtraArgumentSql })
        {
            var error = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(TopicSqlProtocol.Root, TopicSqlNativeSeed.Request(database, sql)));
            await Assert.That(error.Code).IsEqualTo(ErrorCode.Validation);
        }
        var noConsent = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(TopicSqlProtocol.Root,
            TopicSqlNativeSeed.Request(database) with { AllowFullScan = false }));
        var cursor = Assert.ThrowsExactly<KeyLoadException>(() => engine.ExecuteAst(TopicSqlProtocol.Root,
            TopicSqlNativeSeed.Ast(database) with { Cursor = TopicSqlProtocol.InvalidCursor }));
        await Assert.That(noConsent.Code).IsEqualTo(ErrorCode.UnsupportedCapability);
        await Assert.That(cursor.Code).IsEqualTo(ErrorCode.UnsupportedCapability);
        var ordinary = engine.Execute(TopicSqlProtocol.Root, TopicSqlNativeSeed.Request(database, TopicSqlProtocol.OrdinarySql));
        var row = await Assert.That(ordinary.Rows).HasSingleItem();
        await Assert.That(row.EntityId).IsEqualTo(TopicSqlProtocol.OrdinaryRow);
        await Assert.That(row.Json).IsEqualTo(TopicSqlProtocol.EmptyJson);
        await TopicSqlNativeAssertions.UnchangedAsync(database, before, position);
        await TopicSqlNativeAssertions.RecordsAsync(TopicSqlNativeSeed.Read(database),
            engine.Execute(TopicSqlProtocol.Root, TopicSqlNativeSeed.Request(database)));
    }
}
