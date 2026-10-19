namespace KeyLoad.UnitTests.Features.Search;

[NotInParallel]
internal sealed class PackedAnnDeleteReinsertStressTests
{
    [Test]
    [Arguments(DistanceMetric.Cosine)]
    [Arguments(DistanceMetric.Euclidean)]
    [Arguments(DistanceMetric.DotProduct)]
    public async Task RepeatedDeleteReinsertRejectsFailedRebuildThenColdRestoresExactCandidates(
        DistanceMetric metric)
    {
        using var database = new TestDatabase();
        PackedAnnTestData.Configure(database);
        PackedAnnTestData.Seed(database, PackedAnnDeleteReinsertStress.RecordCount,
            PackedAnnDeleteReinsertStress.Dimension, metric);
        await PackedAnnDeleteReinsertStress.RunAsync(database, metric,
            TestContext.Current!.Execution.CancellationToken);
    }
}
