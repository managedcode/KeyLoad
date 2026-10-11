using KeyLoad.Query.Features.Search;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal static class AdaptiveFallbackWholeFlow
{
    private const int QueryOrdinal = 22_917;
    private const int EligibleCount = 100;
    internal static async Task RunAsync()
    {
        using var database = new TestDatabase();
        PackedAnnTestData.Configure(database);
        var metric = DistanceMetric.DotProduct;
        var space = PackedAnnTestData.Space(metric, PackedAnnTestData.QualityDimension);
        PackedAnnTestData.Seed(database, PackedAnnTestData.QualityRecordCount, space.Dimension, metric);
        var records = PackedAnnTestData.Load(database, metric);
        var options = new PackedAnnOptions { ExactThreshold = 0, EfSearch = 1 };
        var budget = PackedAnnIndexTestSupport.Budget(database);
        var index = PackedAnnBuildObservationRunner.Build(PackedAnnBuildScenario.AdaptiveFallback,
            space, records, options, budget);
        var query = PackedAnnTestData.Vector(QueryOrdinal, space.Dimension, PackedAnnTestData.CorpusSeed);
        var bottom = AdaptiveFilteredPlannerOracle.Rank(records, query,
            Enumerable.Range(0, records.Length).ToArray()).TakeLast(EligibleCount).Select(item => item.SourceOrdinal).ToArray();
        var bitmap = AdaptiveFilteredPlannerOracle.Bitmap(records.Length, bottom);
        var position = database.Store.Position;
        var original = JsonDefaults.Serialize(records);
        var measured = index.Search(query, bottom.Length, bitmap, PackedAnnIndexTestSupport.Budget(database));

        await Assert.That(measured.Mode).IsEqualTo(AnnSearchMode.ExactAfterInsufficientCandidates);
        await Assert.That(measured.EligibleCount).IsEqualTo(bottom.Length);
        await Assert.That(measured.ExpansionPasses).IsGreaterThan(0);
        await Assert.That(measured.FallbackDistanceEvaluations).IsEqualTo(bottom.Length);
        await Assert.That(measured.DistanceEvaluations >= measured.FallbackDistanceEvaluations).IsTrue();
        await AssertExactAsync(measured, records, query, bottom);
        await AssertBudgetExceededAsync(() => index.Search(query, bottom.Length, bitmap,
            PackedAnnIndexTestSupport.Budget(database, measured.WorkUnits - 1)));
        await UnchangedAsync(database, metric, position, original);
        var healthy = index.Search(query, bottom.Length, bitmap, PackedAnnIndexTestSupport.Budget(database));
        await AssertExactAsync(healthy, records, query, bottom);
        await UnchangedAsync(database, metric, position, original);
    }

    private static async Task AssertExactAsync(AnnSearchResult result, VectorRecord[] records,
        float[] query, int[] eligible)
    {
        var expected = AdaptiveFilteredPlannerOracle.Rank(records, query, eligible);
        await Assert.That(result.Mode).IsEqualTo(AnnSearchMode.ExactAfterInsufficientCandidates);
        await Assert.That(result.Candidates.Length).IsEqualTo(expected.Length);
        await Assert.That(result.EligibleCount).IsEqualTo(eligible.Length);
        await Assert.That(result.Candidates.Select(candidate => candidate.SourceOrdinal).ToArray())
            .IsEquivalentTo(expected.Select(candidate => candidate.SourceOrdinal).ToArray(), CollectionOrdering.Matching);
        await AdaptiveFilteredPlannerOracle.AssertReturnedCandidatesAsync(result, records, query, eligible);
    }

    private static async Task UnchangedAsync(TestDatabase database, DistanceMetric metric,
        long position, byte[] original)
    {
        await Assert.That(database.Store.Position).IsEqualTo(position);
        var current = PackedAnnTestData.Load(database, metric);
        await Assert.That(current.Length).IsEqualTo(PackedAnnTestData.QualityRecordCount);
        await Assert.That(JsonDefaults.Serialize(current).AsSpan().SequenceEqual(original)).IsTrue();
        await Assert.That(database.Store.Position).IsEqualTo(position);
    }

    private static async Task AssertBudgetExceededAsync(Action operation)
    {
        var failure = Assert.ThrowsExactly<KeyLoadException>(operation);
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }
}
