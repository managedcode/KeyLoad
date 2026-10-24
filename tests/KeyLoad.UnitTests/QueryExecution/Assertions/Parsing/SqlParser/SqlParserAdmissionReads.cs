using KeyLoad.Query;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Authorization;
using KeyLoad.UnitTests.GraphTraversal;

namespace KeyLoad.UnitTests.QueryExecution;

internal static class SqlParserAdmissionReads
{
    private const string PrivateParameter = "private";
    internal static Task HealthyAsync(SqlParserAdmissionFixture fixture, DatabaseEngine database, ZoneTreeStore store, CancellationToken token)
        => GraphLabelUseOperations.ObserveAsync(store, async () =>
        {
            var engine = new QueryEngine(database, UnitExecutionOptions.QueryExecution());
            var sql = new QueryRequest(fixture.Harness.Partition, "SELECT TOP ((1)) title FROM offsetleft ORDER BY order_id", AllowFullScan: true);
            var ast = new AstQueryRequest(fixture.Harness.Partition,
                new(SqlOffsetProtocol.Left, null, [new("/title", "title")], null, [new("/order_id", false)], 1), AllowFullScan: true);
            foreach (var (id, json) in new[] { ("a", """{"title":"first"}"""), ("b", """{"title":"second"}"""),
                ("c", """{"title":"third"}"""), ("d", """{"title":"fourth"}""") })
            {
                var page = engine.Execute(SqlOffsetProtocol.Reader, sql, cancellationToken: token);
                var typed = engine.ExecuteAst(SqlOffsetProtocol.Reader, ast, cancellationToken: token);
                await PageAsync(page, id, json, store.Position);
                await PageAsync(typed, id, json, store.Position);
                sql = sql with
                {
                    Cursor = page.Cursor
                };
                ast = ast with
                {
                    Cursor = typed.Cursor
                };
            }
            foreach (var input in fixture.Seed.Mutations.Cast<PutDocument>())
            {
                var reference = fixture.Harness.ExpectedReference(new(fixture.Harness.Partition, input.Collection, input.Id));
                await RequiredProcessingPolicyAssertions.EqualAsync(new DocumentResult(reference, 1L, input.Json, false, []),
                    database.GetDocument("root", reference, cancellationToken: token)
                        ?? throw new InvalidOperationException("The original literal parser source must exist."));
            }
            await SqlParserAdmissionModelPages.RunAsync(fixture, database, store, token);
            await GraphLabelUseOperations.ReplayAsync(database, store, fixture.Seed, fixture.Receipt, fixture.Outcome);
        });
    private static async Task PageAsync(QueryPage page, string id, string json, long cut)
    {
        await Assert.That(page.Cursor is null).IsEqualTo(id == "d");
        await RequiredProcessingPolicyAssertions.EqualAsync(new QueryPage([new(id, 1L, json, false, [])],
            page.Cursor, cut, "bounded-full-scan"), page);
    }
    internal static Task RefuseAsync(SqlParserAdmissionFixture fixture, DatabaseEngine database, ZoneTreeStore store,
        string sql, ErrorCode code, string detail, CancellationToken token)
        => GraphLabelUseOperations.ObserveAsync(store, async () =>
        {
            var engine = new QueryEngine(database, UnitExecutionOptions.QueryExecution());
            QueryPage? partial = null;
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => partial = engine.Execute(SqlOffsetProtocol.Reader,
                new(fixture.Harness.Partition, sql, Parameters: new()
                {
                    [PrivateParameter] = global::System.Text.Json.JsonSerializer.SerializeToElement(SqlParserAdmissionInputs.Secret, JsonDefaults.Options)
                },
                    AllowFullScan: true), cancellationToken: token));
            await Assert.That(partial).IsNull();
            await Assert.That(failure.Code).IsEqualTo(code);
            await Assert.That(failure.Message).IsEqualTo(detail);
            await Assert.That(failure.Message.Contains(SqlParserAdmissionInputs.Secret, StringComparison.Ordinal)).IsFalse();
        });
}
