using KeyLoad.Query.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class PackedAnnOwnedSimilarityTests
{
    private static readonly int[] Dimensions = [1, 4_096];
    private static readonly DistanceMetric[] Metrics =
        [DistanceMetric.Cosine, DistanceMetric.Euclidean, DistanceMetric.DotProduct];

    [Test]
    public async Task AcAnn009CopiedZoneTreeVectorsMatchExactOracleAndRemainOwned()
    {
        foreach (var dimension in Dimensions)
        {
            foreach (var metric in Metrics)
            {
                await AssertMetricAsync(dimension, metric);
            }
        }
    }

    private static async Task AssertMetricAsync(int dimension, DistanceMetric metric)
    {
        using var database = PackedAnnOwnedSimilarityTestSupport.CreateDatabase();
        var space = PackedAnnTestData.Space(metric, dimension);
        var source = PackedAnnOwnedSimilarityTestSupport.Vectors(dimension);
        PackedAnnIndexTestSupport.PersistVectors(database, space, PackedAnnTestData.Field(metric), source);
        var records = PackedAnnTestData.Load(database, metric);
        await PackedAnnOwnedSimilarityAssertions.VerifyPackedScoresAsync(database, space, metric, records);
    }
}
