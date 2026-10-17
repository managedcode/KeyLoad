using KeyLoad.Query.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class PackedAnnRecallTests
{
    [Test, NotInParallel]
    public async Task AcAnn005TenThousandCanonicalRowsMeetRecallForEveryMetricFilterAndCorrelationCell()
    {
        using var database = new TestDatabase();
        PackedAnnTestData.Configure(database);
        var metrics = new[] { DistanceMetric.Cosine, DistanceMetric.Euclidean, DistanceMetric.DotProduct };
        PackedAnnTestData.Seed(database, PackedAnnTestData.QualityRecordCount,
            PackedAnnTestData.QualityDimension, metrics);
        var options = new PackedAnnOptions { Seed = PackedAnnTestData.CorpusSeed };
        var settings = new QualitySettings(options.Connections, options.EfConstruction, options.EfSearch,
            options.MaxLevel, options.ExactThreshold, options.MaxRecords, options.MaxIndexBytes,
            options.MaxScratchBytes, options.Seed);
        await Assert.That(settings).IsEqualTo(new QualitySettings(16, 128, 128, 16, 256,
            5_000_000, 268_435_456, 8_388_608, PackedAnnTestData.CorpusSeed));
        foreach (var metric in metrics)
        {
            var records = PackedAnnTestData.Load(database, metric);
            await Assert.That(records.Length).IsEqualTo(PackedAnnTestData.QualityRecordCount);
            var space = PackedAnnTestData.Space(metric, PackedAnnTestData.QualityDimension);
            var budget = PackedAnnIndexTestSupport.Budget(database);
            var index = PackedAnnBuildObservationRunner.Build(PackedAnnBuildScenario.RecallMetric,
                space, records, options, budget);
            await Assert.That(index.Count).IsEqualTo(PackedAnnTestData.QualityRecordCount);
            foreach (var cell in PackedAnnRecallSupport.Cells)
            {
                var observation = await PackedAnnRecallSupport.MeasureCell(database, records, index, metric, cell);
                await PackedAnnRecallSupport.AssertCell(observation, metric, cell, options);
            }
        }
    }

    private readonly record struct QualitySettings(int Connections, int EfConstruction, int EfSearch, int MaxLevel,
        int ExactThreshold, int MaxRecords, long MaxIndexBytes, long MaxScratchBytes, ulong Seed);
}
