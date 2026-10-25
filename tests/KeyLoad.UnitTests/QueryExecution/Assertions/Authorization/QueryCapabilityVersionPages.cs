using System.Text.Json;
using KeyLoad.Query;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Authorization;
using KeyLoad.UnitTests.GraphTraversal;

namespace KeyLoad.UnitTests.QueryExecution;

internal static class QueryCapabilityVersionPages
{
    internal static Task RunAsync(QueryCapabilityVersionFixture fixture, DatabaseEngine database, ZoneTreeStore store, CancellationToken token)
        => GraphLabelUseOperations.ObserveAsync(store, async () =>
        {
            var engine = new QueryEngine(database, UnitExecutionOptions.QueryExecution());
            await RequiredProcessingPolicyAssertions.EqualAsync(QueryCapabilityVersionExpected.Manifest(), engine.Capabilities);
            foreach (var version in new[] { 1 })
            {
                var authored = QueryCapabilityVersionRequests.Ast(fixture.Harness.Partition, version);
                var builder = QueryCapabilityVersionRequests.Builder(fixture.Harness.Partition) with
                {
                    AstVersion = version
                };
                await RequiredProcessingPolicyAssertions.EqualAsync(authored, builder);
                var json = JsonSerializer.Deserialize<AstQueryRequest>(JsonSerializer.Serialize(builder, JsonDefaults.Options), JsonDefaults.Options)!;
                var execution = OrleansSerialization.Deserialize<AstQueryRequest>(OrleansSerialization.Serialize(builder));
                await RequiredProcessingPolicyAssertions.EqualAsync(authored, json);
                await RequiredProcessingPolicyAssertions.EqualAsync(authored, execution);
                foreach (var request in new[] { builder, json, execution })
                {
                    await AstPagesAsync(engine, request, store.Position, token);
                }
                var sql = QueryCapabilityVersionRequests.Sql(fixture.Harness.Partition, version);
                foreach (var (id, body, final) in Rows)
                {
                    var page = engine.Execute(QueryCapabilityVersionRequests.Reader, sql, cancellationToken: token);
                    await PageAsync(page, id, body, store.Position, final);
                    sql = sql with
                    {
                        Cursor = page.Cursor
                    };
                }
                await ExplainAsync(engine, authored, store.Position, fixture.Harness.Partition, token);
            }
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
            var page = engine.ExecuteAst(QueryCapabilityVersionRequests.Reader, request, cancellationToken: token);
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
        var plan = QueryCapabilityVersionRequests.Explain(partition) with
        {
            AstVersion = request.AstVersion
        };
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
            atomicPartition = partition.AtomicPartitionId
        }, JsonDefaults.Options);
        await RequiredProcessingPolicyAssertions.EqualAsync(new QueryPage([new("explain", 0L, body)], null, cut, "bounded-full-scan"),
            engine.ExecuteAst(QueryCapabilityVersionRequests.Reader, plan, cancellationToken: token));
    }
    private static readonly (string Id, string Json, bool Final)[] Rows =
        [("b", """{"title":"Bravo","score":20}""", false), ("d", """{"title":"Delta","score":40}""", true)];
}
