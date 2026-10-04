using System.Collections.Immutable;
using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class OpenSearchNativeVectorEdges
{
    private const string Projection = "{\"nested\":{\"number\":1.25},\"values\":[true,null,\"text\"]}";
    private const string MissingVectorId = "missing-vector", MissingProjectionId = "0";
    private static readonly ImmutableArray<float> Query = [1, 0];
    private static readonly (string Id, ImmutableArray<float> Vector)[] Candidates = [("b", [1, 0]), ("a", [1, 0]), ("c", [-1, 0]), ("e", [0, 1])];

    /// <summary>AC-BC-FAIL-010: genuine empty, exact ties, negative scores, excluded vectors and missing projection use the same native API.</summary>
    internal static async Task VerifyAsync(Uri endpoint, int nodeCount, CancellationToken token)
    {
        await using var fixture = new OpenSearchNativeVectorIndex(endpoint, nodeCount);
        await fixture.CreateAsync(token);
        await Assert.That(await fixture.SearchAsync(Query, 10, token)).IsEmpty();
        await SeedAsync(fixture, token);
        var rows = await fixture.SearchAsync(Query, 10, token);
        await Assert.That(rows.Select(row => row.Id).SequenceEqual(["a", "b", "e", "c"], StringComparer.Ordinal)).IsTrue();
        await Assert.That(rows.All(row => BenchmarkDataset.SameJson(row.Json, Projection))).IsTrue();
        var one = await fixture.SearchAsync(Query, 1, token);
        await Assert.That(one.Length).IsEqualTo(1);
        await Assert.That(one[0].Id).IsEqualTo("a");
        await Assert.That(async () =>
        {
            using var response = await fixture.RawSearchAsync([0, 0], token);
        }).Throws<HttpRequestException>();
        token.ThrowIfCancellationRequested();
        await Assert.That(async () =>
        {
            using var response = await fixture.RawSearchAsync([1, 0, 0], token);
        }).Throws<HttpRequestException>();
        await VerifyMissingProjectionAsync(fixture, token);
    }

    private static async Task SeedAsync(OpenSearchNativeVectorIndex fixture, CancellationToken token)
    {
        foreach (var (id, vector) in Candidates)
        {
            await fixture.WriteAsync(id, OpenSearchDocument.Create(id, Projection, vector), token);
        }
        await fixture.WriteAsync(MissingVectorId, OpenSearchDocument.CreateWithoutVector(MissingVectorId, Projection), token);
        await fixture.RefreshAsync(token);
    }

    private static async Task VerifyMissingProjectionAsync(OpenSearchNativeVectorIndex fixture, CancellationToken token)
    {
        var source = new Dictionary<string, object>
        {
            [OpenSearchNames.Id] = MissingProjectionId,
            [OpenSearchNames.Vector] = Query
        };
        await fixture.WriteAsync(MissingProjectionId, source, token);
        await fixture.RefreshAsync(token);
        await Assert.That(async () =>
        {
            using var response = await fixture.RawSearchAsync(Query, token);
        }).Throws<HttpRequestException>();
        token.ThrowIfCancellationRequested();
    }
}
