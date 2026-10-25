using KeyLoad.Query.Search;

namespace KeyLoad.UnitTests.Search;

internal sealed class PackedAnnOwnedSimilarityGraphTests
{
    private const int RecordCount = 768;
    private const int Dimension = 4;

    [Test]
    public async Task AcAnn009DotProductRankSelectionKeepsUniqueBoundedGraphAdjacency()
    {
        using var database = PackedAnnOwnedSimilarityTestSupport.CreateDatabase();
        const DistanceMetric metric = DistanceMetric.DotProduct;
        var space = PackedAnnTestData.Space(metric, Dimension);
        PackedAnnTestData.Seed(database, RecordCount, Dimension, metric);
        var records = PackedAnnTestData.Load(database, metric);
        var state = PackedAnnBuilder.Build(space, records, UnitExecutionOptions.PackedAnn(new()), PackedAnnIndexTestSupport.Budget());
        await PackedAnnOwnedSimilarityAssertions.AssertGraphAdjacencyAsync(state,
            PackedAnnIndexTestSupport.Budget());
    }
}
