namespace KeyLoad.FunctionalTests.QueryExecution;

internal sealed class SqlCommentBudgetTests
{
    [Test]
    public async Task AcSqlc004_CancellationIsObservedBeforeTheNextTriviaChunk()
    {
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            using var database = new TestDatabase();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                SqlOffsetFixture.Seed(database);
                await SqlOffsetFixture.PolicyAsync(database, SqlOffsetProtocol.GrantEpoch, true);
                var sql = SqlCommentTestData.LongCommentPrefix
                    + new string(SqlCommentTestData.LongCommentCharacter, SqlCommentTestData.LongCommentSize)
                    + SqlCommentTestData.LongCommentSuffix + ScriptDomSelectPages.WindowSql;
                var engine = new global::KeyLoad.Query.QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
                await ScriptDomParsingCancellation.RequireAsync(database, engine, sql);
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}
