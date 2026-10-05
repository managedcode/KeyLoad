using System.Text.Json;
using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class QdrantVectorContractTests
{
    [Test]
    public async Task AcVq002ExactAndHnswNativeRequestsCarryActualMethodAndServerPredicate()
    {
        var query = new float[128];
        using var exact = JsonSerializer.SerializeToDocument(QdrantVectorQueries.Request(query, 10,
            VectorIndexKind.Exact, VectorQueryMode.Filtered));
        using var hnsw = JsonSerializer.SerializeToDocument(QdrantVectorQueries.Request(query, 10,
            VectorIndexKind.Hnsw, VectorQueryMode.Mixed));
        await Assert.That(exact.RootElement.GetProperty("params").GetProperty("exact").GetBoolean()).IsTrue();
        await Assert.That(hnsw.RootElement.GetProperty("params").GetProperty("indexed_only").GetBoolean()).IsTrue();
        await Assert.That(hnsw.RootElement.GetProperty("params").GetProperty("hnsw_ef").GetInt32()).IsEqualTo(200);
        await Assert.That(exact.RootElement.GetProperty("filter").GetProperty("must")[0].GetProperty("key").GetString()).IsEqualTo("filtered");
        await Assert.That(hnsw.RootElement.GetProperty("filter").GetProperty("must")[0].GetProperty("key").GetString()).IsEqualTo("mixed");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => QdrantVectorQueries.Request(query, 10,
            VectorIndexKind.IvfFlat, VectorQueryMode.Plain));
    }

    [Test]
    public async Task AcVq001NativeReadbackValidatesIdentityFilterMetadataAndObservedVectorBytes()
    {
        var corpus = new VectorComparisonCorpus(VectorComparisonProfile.Parse("vector-100k-exact-filtered-c16"));
        var input = corpus.Create(100);
        using var native = JsonSerializer.SerializeToDocument(new
        {
            id = 101, vector = input.Embedding.ToArray(),
            payload = new { id = input.Id, number = input.Number, document = input.Payload, filtered = true, mixed = true }
        });
        var actual = QdrantVectorReadback.Parse(native.RootElement);
        await Assert.That(actual.Id).IsEqualTo("v000000100");
        await Assert.That(actual.Number).IsEqualTo(100);
        await Assert.That(actual.VectorSha256).IsEqualTo(VectorComparisonCorpus.HashVector(input.Embedding.Span));
        using var wrongIdentity = JsonSerializer.SerializeToDocument(new
        {
            id = 102, vector = input.Embedding.ToArray(),
            payload = new { id = input.Id, number = input.Number, document = input.Payload, filtered = true, mixed = true }
        });
        Assert.ThrowsExactly<ComparisonFailureException>(() => QdrantVectorReadback.Parse(wrongIdentity.RootElement));
    }

    [Test]
    public async Task AcVq002NativeIndexAdmissionRequiresCompleteHnswAndActualCosineSettings()
    {
        var profile = VectorComparisonProfile.Parse("vector-100k-hnsw-plain-c16");
        using var complete = NativeState(100_000, 16, "Cosine");
        using var incomplete = NativeState(99_999, 16, "Cosine");
        using var exact = NativeState(100_000, 0, "Cosine");
        using var wrongMetric = NativeState(100_000, 16, "Dot");
        await Assert.That(QdrantVectorIndexValidation.Ready(complete.RootElement, profile)).IsTrue();
        await Assert.That(QdrantVectorIndexValidation.Ready(incomplete.RootElement, profile)).IsFalse();
        Assert.ThrowsExactly<ComparisonFailureException>(() => QdrantVectorIndexValidation.Ready(exact.RootElement, profile));
        Assert.ThrowsExactly<ComparisonFailureException>(() => QdrantVectorIndexValidation.Ready(wrongMetric.RootElement, profile));
    }

    private static JsonDocument NativeState(int indexed, int m, string metric)
        => JsonSerializer.SerializeToDocument(new
        {
            status = "green", optimizer_status = "ok", points_count = 100_000, indexed_vectors_count = indexed,
            config = new { @params = new { vectors = new { size = 128, distance = metric } },
                hnsw_config = new { m, ef_construct = 200, full_scan_threshold = 0 } }
        });
}
