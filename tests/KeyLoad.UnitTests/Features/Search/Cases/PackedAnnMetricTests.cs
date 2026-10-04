using KeyLoad.Query;
using KeyLoad.Query.Features.Search;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class PackedAnnMetricTests
{
    private static readonly DistanceMetric[] Metrics = [DistanceMetric.Cosine, DistanceMetric.Euclidean, DistanceMetric.DotProduct];
    private static readonly int[] Dimensions = [1, 4_096];

    [Test]
    public async Task AcAnn002ScoresMatchPublicExactMetricForFiniteBoundariesAndDimensions()
    {
        foreach (var dimension in Dimensions)
        {
            foreach (var metric in Metrics)
            {
                await AssertMetricDimensionAsync(dimension, metric);
            }
        }
    }

    private static async Task AssertMetricDimensionAsync(int dimension, DistanceMetric metric)
    {
        using var database = new TestDatabase();
        PackedAnnTestData.Configure(database);
        var space = PackedAnnTestData.Space(metric, dimension);
        PackedAnnIndexTestSupport.PersistVectors(database, space, PackedAnnTestData.Field(metric), ExtremeVectors(dimension));
        var records = PackedAnnTestData.Load(database, metric);
        var index = PackedAnnIndexTestSupport.Build(database, metric, records);
        if (dimension == 4_096)
        {
            await Assert.That(index.RetainedBytesUpperBound).IsLessThan(1_048_576L);
        }
        foreach (var query in Queries(dimension))
        {
            await AssertQueryMatchesExactAsync(index, database, records, metric, query);
        }
    }

    private static async Task AssertQueryMatchesExactAsync(PackedAnnIndex index, TestDatabase database,
        VectorRecord[] records, DistanceMetric metric, float[] query)
    {
        var result = index.Search(query, records.Length, null, PackedAnnIndexTestSupport.Budget(database));
        var exact = Exact(records, query, metric);
        await Assert.That(result.Mode).IsEqualTo(AnnSearchMode.ExactSmallSet);
        await Assert.That(result.Candidates.Select(candidate => candidate.DocumentId))
            .IsEquivalentTo(exact.Select(candidate => candidate.DocumentId), CollectionOrdering.Matching);
        for (var ordinal = 0; ordinal < exact.Length; ordinal++)
        {
            await Assert.That(result.Candidates[ordinal].Score).IsEqualTo(exact[ordinal].Score);
            await Assert.That(result.Candidates[ordinal].SourceOrdinal).IsEqualTo(exact[ordinal].SourceOrdinal);
            await Assert.That(result.Candidates[ordinal].DocumentRevision)
                .IsEqualTo(PackedAnnTestData.DocumentRevision);
        }
    }

    private static float[][] ExtremeVectors(int dimension)
    {
        var zero = new float[dimension];
        var signedZero = new float[dimension];
        signedZero[0] = -0f;
        var subnormal = new float[dimension];
        subnormal[0] = float.Epsilon;
        if (dimension > 1)
        {
            subnormal[^1] = -float.Epsilon;
        }
        var ordinary = PackedAnnTestData.Vector(1, dimension, PackedAnnTestData.CorpusSeed);
        var maximum = Enumerable.Repeat(float.MaxValue, dimension).ToArray();
        var opposite = Enumerable.Repeat(-float.MaxValue, dimension).ToArray();
        return [zero, signedZero, subnormal, ordinary, maximum, opposite];
    }

    private static float[][] Queries(int dimension)
    {
        var ordinary = PackedAnnTestData.Vector(7, dimension, PackedAnnTestData.CorpusSeed);
        var maximum = Enumerable.Repeat(float.MaxValue, dimension).ToArray();
        var subnormal = new float[dimension];
        subnormal[0] = float.Epsilon;
        return [new float[dimension], ordinary, maximum, subnormal];
    }

    private static AnnCandidate[] Exact(VectorRecord[] records, float[] query, DistanceMetric metric)
        => records.Select((record, ordinal) => new AnnCandidate(ordinal, record.DocumentId, record.DocumentRevision,
                SearchEngine.Similarity(query, record.Values.ToArray(), metric)))
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.DocumentId, StringComparer.Ordinal).ToArray();
}
