using KeyLoad.Query.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class PackedAnnOwnedSimilarityGraphTests
{
    private const int RecordCount = 768;
    private const int Dimension = 4;

    [Test]
    public async Task AcAnn009DotProductSimpleSelectionKeepsUniqueBoundedNativeGraphAdjacency()
    {
        using var database = PackedAnnOwnedSimilarityTestSupport.CreateDatabase();
        const DistanceMetric metric = DistanceMetric.DotProduct;
        var space = PackedAnnTestData.Space(metric, Dimension);
        PackedAnnTestData.Seed(database, RecordCount, Dimension, metric);
        var records = PackedAnnTestData.Load(database, metric);
        var state = PackedAnnBuilder.Build(space, records, UnitExecutionOptions.PackedAnn(new()), PackedAnnIndexTestSupport.Budget(database));
        await PackedAnnOwnedSimilarityAssertions.AssertGraphAdjacencyAsync(state,
            PackedAnnIndexTestSupport.Budget(database));
    }
}
