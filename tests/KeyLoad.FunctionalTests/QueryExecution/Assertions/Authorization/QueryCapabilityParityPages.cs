using System.Text.Json;
using KeyLoad.FunctionalTests.Authorization;
using KeyLoad.FunctionalTests.GraphTraversal;
using KeyLoad.Query;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class QueryCapabilityParityPages
{
    internal static Task RunAsync(QueryCapabilityParityFixture fixture, DatabaseEngine database, ZoneTreeStore store, CancellationToken token)
        => GraphLabelUseOperations.ObserveAsync(store, async () =>
        {
            var engine = new QueryEngine(database, UnitExecutionOptions.QueryExecution());
            await RequiredProcessingPolicyAssertions.EqualAsync(QueryCapabilityParityExpected.Manifest(), engine.Capabilities);
            var authored = QueryCapabilityParityRequests.Ast(fixture.Harness.Partition);
            var builder = QueryCapabilityParityRequests.Builder(fixture.Harness.Partition);
            await RequiredProcessingPolicyAssertions.EqualAsync(authored, builder);
            var json = JsonSerializer.Deserialize<AstQueryRequest>(JsonSerializer.Serialize(builder, JsonDefaults.Options), JsonDefaults.Options)!;
            var execution = OrleansSerialization.Deserialize<AstQueryRequest>(OrleansSerialization.Serialize(builder));
            await RequiredProcessingPolicyAssertions.EqualAsync(authored, json);
            await RequiredProcessingPolicyAssertions.EqualAsync(authored, execution);
            foreach (var request in new[] { builder, json, execution })
            {
                await AstPagesAsync(engine, request, store.Position, token);
            }
            var sql = QueryCapabilityParityRequests.Sql(fixture.Harness.Partition);
            foreach (var (id, body, final) in Rows)
            {
                var page = engine.Execute(QueryCapabilityParityRequests.Reader, sql, cancellationToken: token);
                await PageAsync(page, id, body, store.Position, final);
                sql = sql with
                {
                    Cursor = page.Cursor
                };
            }
            await ExplainAsync(engine, authored, store.Position, fixture.Harness.Partition, token);
            await GraphLabelUseOperations.ReplayAsync(database, store, fixture.Seed, fixture.Receipt, fixture.Outcome);
            foreach (var input in fixture.Seed.Mutations.Cast<PutDocument>())
            {
                var reference = new EntityRef(fixture.Harness.Partition, input.Collection, input.Id)
                {
                    Lifetime = new(fixture.Creation.Incarnation.Value)
                };
                await RequiredProcessingPolicyAssertions.EqualAsync(new DocumentResult(reference, 1L, input.Json, false, []),
                    database.GetDocument("root", reference, cancellationToken: token) ?? throw new InvalidOperationException("The original query example row is absent."));
            }
        });
    private static async Task AstPagesAsync(QueryEngine engine, AstQueryRequest request, long cut, CancellationToken token)
    {
        foreach (var (id, body, final) in Rows)
        {
            var page = engine.ExecuteAst(QueryCapabilityParityRequests.Reader, request, cancellationToken: token);
            await PageAsync(page, id, body, cut, final);
            request = request with
            {
                Cursor = page.Cursor
            };
        }
    }
    internal static async Task PageAsync(QueryPage page, string id, string body, long cut, bool final)
    {
        await Assert.That(page.Cursor is null).IsEqualTo(final);
        await RequiredProcessingPolicyAssertions.EqualAsync(new QueryPage([new(id, 1L, body, false, [])], page.Cursor, cut, "bounded-full-scan"), page);
    }
    private static async Task ExplainAsync(QueryEngine engine, AstQueryRequest request, long cut, PartitionRef partition, CancellationToken token)
    {
        var plan = QueryCapabilityParityRequests.Explain(partition);
        var expected = request with
        {
            Query = request.Query with
            {
                Explain = true
            }
        };
        await RequiredProcessingPolicyAssertions.EqualAsync(expected, plan);
        var body = JsonSerializer.Serialize(new
        {
            accessPath = "bounded-full-scan",
            atomicPartition = partition.AtomicPartitionId,
            ReadCut = cut,
            Scan = "bounded-full-scan",
            Index = (string?)null,
            SeekFields = Array.Empty<string>(),
            HasLowerBound = false,
            HasUpperBound = false,
            ResidualFields = new[] { "/active", "/score" },
            Order = new Ordering[] { new("/order_id", false) },
            RetainedRows = 1L,
            Limit = 1,
            Offset = 0,
            Projection = new Selection[] { new("/title", "title"), new("/score", "score") },
            Operators = new[] { "bounded-full-scan", "row-authorization", "residual-filter", "top-n-heap", "projection", "page" }
        }, JsonDefaults.Options);
        await RequiredProcessingPolicyAssertions.EqualAsync(new QueryPage([new("explain", 0L, body)], null, cut, "bounded-full-scan"),
            engine.ExecuteAst(QueryCapabilityParityRequests.Reader, plan, cancellationToken: token));
    }
    private static readonly (string Id, string Json, bool Final)[] Rows =
        [("b", """{"title":"Bravo","score":20}""", false), ("d", """{"title":"Delta","score":40}""", true)];
}
