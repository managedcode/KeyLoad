using System.Numerics;
using KeyLoad.Query;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class VectorMetricGoldenTests
{
    private const string Collection = "vector-metrics";
    private const string VectorField = "/embedding";
    private const string SpaceId = "portable-metric-space";
    private const string Model = "unit-test-model";
    private const string Version = "1";
    private const string DocumentJson = "{}";
    private const string BestId = "best";
    private const string MiddleId = "middle";
    private const string LastId = "last";
    private const string Principal = "root";
    private const int VectorVersion = 1;
    private const int MaximumDimension = 4_096;
    private const int FusionConstant = 60;
    private const double CosineTolerance = 0.0000000000005;
    private static readonly VectorSpace GoldenSpace = new(SpaceId, 1, DistanceMetric.DotProduct, Model, Version);

    [Test]
    public async Task AcSearch001SelectedMetricGoldensHoldAtRuntimeWidthsBlocksTailsAndMaximumDimension()
    {
        var width = Vector<float>.Count;
        var lengths = new[] { 1, width, width + 1, (2 * width) + 1, MaximumDimension };

        foreach (var length in lengths)
        {
            var query = Enumerable.Repeat(1f, length).ToArray();
            var candidate = Enumerable.Repeat(2f, length).ToArray();
            await Assert.That(SearchEngine.Similarity(query, candidate, DistanceMetric.DotProduct)).IsEqualTo(2d * length);
            await Assert.That(SearchEngine.Similarity(query, candidate, DistanceMetric.Euclidean)).IsEqualTo(-Math.Sqrt(length));
            await Assert.That(SearchEngine.Similarity(query, query, DistanceMetric.Cosine)).IsEqualTo(1d).Within(CosineTolerance);
        }
    }

    [Test]
    public async Task AcSearch001ZeroSignedZeroAndFiniteExtremesKeepTheirMetricResults()
    {
        var zero = new float[1];
        var negativeZero = new[] { -0f };
        var positiveZero = new[] { 0f };
        var maximum = Enumerable.Repeat(float.MaxValue, Vector<float>.Count + 1).ToArray();
        var oppositeMaximum = Enumerable.Repeat(-float.MaxValue, maximum.Length).ToArray();

        await Assert.That(SearchEngine.Similarity(zero, zero, DistanceMetric.Cosine)).IsEqualTo(0d);
        await Assert.That(SearchEngine.Similarity([1f], zero, DistanceMetric.Cosine)).IsEqualTo(0d);
        await Assert.That(SearchEngine.Similarity(negativeZero, positiveZero, DistanceMetric.DotProduct)).IsEqualTo(0d);
        await Assert.That(BitConverter.DoubleToInt64Bits(SearchEngine.Similarity(negativeZero, positiveZero, DistanceMetric.Euclidean)))
            .IsEqualTo(long.MinValue);
        await Assert.That(double.IsFinite(SearchEngine.Similarity(maximum, maximum, DistanceMetric.DotProduct))).IsTrue();
        await Assert.That(double.IsFinite(SearchEngine.Similarity(maximum, oppositeMaximum, DistanceMetric.Euclidean))).IsTrue();
        await Assert.That(SearchEngine.Similarity(maximum, maximum, DistanceMetric.Cosine)).IsEqualTo(1d).Within(CosineTolerance);
    }

    [Test]
    public async Task AcSearch001PersistedWideTailSearchRanksByPublicSimilarityAndKeepsFusionScores()
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        var width = Vector<float>.Count;
        var query = Enumerable.Repeat(1f, width + 1).ToArray();
        var best = Enumerable.Repeat(2f, query.Length).ToArray();
        var middle = Enumerable.Repeat(0.5f, query.Length).ToArray();
        var last = Enumerable.Repeat(-1f, query.Length).ToArray();
        var space = GoldenSpace with { Dimension = query.Length };
        database.Commit(
            new PutDocument(Collection, BestId, DocumentJson),
            new PutDocument(Collection, MiddleId, DocumentJson),
            new PutDocument(Collection, LastId, DocumentJson),
            new PutVector(Collection, BestId, VectorField, [.. best], space, VectorVersion),
            new PutVector(Collection, MiddleId, VectorField, [.. middle], space, VectorVersion),
            new PutVector(Collection, LastId, VectorField, [.. last], space, VectorVersion));

        var results = new SearchEngine(database.Database).Search(Principal,
            new(database.Partition, Collection, VectorField: VectorField, Vector: [.. query], Space: space),
            TestContext.Current!.Execution.CancellationToken);
        await Assert.That(results.Select(result => result.Document.Reference.Id))
            .IsEquivalentTo(new[] { BestId, MiddleId, LastId }, CollectionOrdering.Matching);
        await Assert.That(SearchEngine.Similarity(query, best, DistanceMetric.DotProduct))
            .IsEqualTo(2d * query.Length);
        await Assert.That(SearchEngine.Similarity(query, middle, DistanceMetric.DotProduct))
            .IsEqualTo(0.5d * query.Length);
        await Assert.That(SearchEngine.Similarity(query, last, DistanceMetric.DotProduct))
            .IsEqualTo(-1d * query.Length);
        await Assert.That(results.Select(result => result.Score)).IsEquivalentTo(new[]
        {
            1d / (FusionConstant + 1),
            1d / (FusionConstant + 2),
            1d / (FusionConstant + 3)
        }, CollectionOrdering.Matching);
    }
}
