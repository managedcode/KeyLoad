using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class AdapterVectorWholeFlow
{
    private const string FirstValue = "first";
    private const string SecondValue = "second";
    private const string VectorParameter = "vector";
    private const string ScopeGraph = "vector-scope";
    private const int RootDepth = 0;
    private const int ScopeVertices = 2;
    private const int ScopeEdges = 1;
    internal const string Collection = "vectors";
    internal const string FirstJson = """{"value":"first"}""";
    internal const string SecondJson = """{"value":"second"}""";
    internal const string Sql = "SEARCH FROM vectors VECTOR embedding MATCH @vector SPACE (\"equivalence\",2,Cosine,\"model\",\"version\") WEIGHT 1 SCOPE GRAPH \"vector-scope\" SEEDS ((vectors,'a'),(vectors,'b')) DEPTH 0 VERTICES 2 EDGES 1 LIMIT 10 FUSION 60";
    internal static VectorSpace Space() => new("equivalence", 2, DistanceMetric.Cosine, "model", "version");
    internal static GraphSearchRequest Typed(TestDatabase database, bool invalid = false)
        => new(1, new(database.Partition, Collection, VectorField: "/embedding", Vector: invalid ? [1] : [1, 0], Space: Space()), Scope: new(new(ScopeGraph,
            [new(database.Partition, Collection, "a"), new(database.Partition, Collection, "b")],
            RootDepth, ScopeVertices, ScopeEdges)));
    internal static SqlGraphSearchRequest SqlRequest(TestDatabase database, bool invalid = false)
        => new(1, new(database.Partition, Sql, new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        { [VectorParameter] = JsonSerializer.SerializeToElement(invalid ? new[] { 1f } : new[] { 1f, 0f }) }, AllowFullScan: true));
    internal static GraphSearchRequest CSharp(TestDatabase database, bool invalid = false)
    {
        var canonical = Typed(database, invalid);
        return KeyLoadQuery.From<VectorAttachmentDocument>(database.Partition, Collection, UnitClientOptions.Translation())
            .Take(canonical.Search.Limit).AttachVector(row => row.Embedding,
                canonical.Search.Vector!.Value, Space(), canonical.Scope!);
    }
    internal static GraphSearchRequest JsonRoundTrip(GraphSearchRequest request)
        => JsonDefaults.Deserialize<GraphSearchRequest>(JsonDefaults.Serialize(request));
    internal static void Seed(TestDatabase database)
    {
        database.Configure(Collection, ResourceKind.Collection);
        database.Configure(ScopeGraph, ResourceKind.Graph);
        var firstJson = JsonSerializer.Serialize(new VectorAttachmentDocument(FirstValue), JsonDefaults.Options);
        var secondJson = JsonSerializer.Serialize(new VectorAttachmentDocument(SecondValue), JsonDefaults.Options);
        if (!string.Equals(firstJson, FirstJson, StringComparison.Ordinal)
            || !string.Equals(secondJson, SecondJson, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The typed native seed must retain the complete literal document bytes.");
        }
        database.Commit(new PutDocument(Collection, "a", firstJson), new PutDocument(Collection, "b", secondJson),
            new PutVector(Collection, "a", "/embedding", [1, 0], Space(), 1), new PutVector(Collection, "b", "/embedding", [0, 1], Space(), 1));
    }
    internal static async Task HealthyAsync(TestDatabase database, QueryEngine sql, SearchEngine typed, CancellationToken token)
    {
        var direct = await typed.GraphSearchAsync("root", Typed(database), token);
        var json = await typed.GraphSearchAsync("root", JsonRoundTrip(Typed(database)), token);
        var loweredRequest = CSharp(database);
        await Assert.That(JsonDefaults.Serialize(loweredRequest).SequenceEqual(JsonDefaults.Serialize(Typed(database)))).IsTrue();
        var lowered = await typed.GraphSearchAsync("root", loweredRequest, token);
        var attached = await sql.SearchSqlAsync("root", SqlRequest(database), token);
        foreach (var result in new[] { direct, json, lowered, attached })
        {
            await Assert.That(JsonDefaults.Serialize(result).AsSpan().SequenceEqual(JsonDefaults.Serialize(direct))).IsTrue();
            await Assert.That(result.Expansion).IsNull();
            await Assert.That(result.Hits.Length).IsEqualTo(2);
            for (var index = 0; index < result.Hits.Length; index++)
            {
                var hit = result.Hits[index];
                await Assert.That(hit.Document.Reference).IsEqualTo(new EntityRef(database.Partition, Collection, index == 0 ? "a" : "b"));
                await Assert.That(hit.Document.Revision).IsEqualTo(1L);
                await Assert.That(hit.Document.Json).IsEqualTo(index == 0 ? FirstJson : SecondJson);
                await Assert.That(hit.Document.Redacted).IsFalse();
                await Assert.That(hit.Document.RedactedFields).IsEmpty();
                await Assert.That(hit.Score).IsEqualTo(index == 0 ? 1d / 61d : 1d / 62d);
            }
        }
    }
}
