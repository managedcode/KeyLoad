using KeyLoad.Query.Search;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Search;

internal static class AdaptiveCandidateBoundWholeFlow
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
        var index = PackedAnnBuildObservationRunner.Build(PackedAnnBuildScenario.AdaptiveCandidateBound,
            space, records, options, budget);
        var query = PackedAnnTestData.Vector(QueryOrdinal, space.Dimension, PackedAnnTestData.CorpusSeed);
        var bottom = AdaptiveFilteredPlannerOracle.Rank(records, query,
            Enumerable.Range(0, records.Length).ToArray()).TakeLast(EligibleCount).Select(item => item.SourceOrdinal).ToArray();
        var bitmap = AdaptiveFilteredPlannerOracle.Bitmap(records.Length, bottom);
        var position = database.Store.Position;
        var original = JsonDefaults.Serialize(records);
        AnnSearchResult? partial = null;
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => partial = index.Search(query, bottom.Length,
            bitmap, PackedAnnIndexTestSupport.Budget(database)));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(failure.Message).IsEqualTo("The bounded ANN graph did not supply the requested eligible candidates.");
        await Assert.That(partial).IsNull();
        await UnchangedAsync(database, metric, position, original);
        var exactIndex = PackedAnnIndexTestSupport.Build(space, records,
            options with { ExactThreshold = EligibleCount }, PackedAnnIndexTestSupport.Budget(database));
        var exact = exactIndex.Search(query, bottom.Length, bitmap, PackedAnnIndexTestSupport.Budget(database));
        await Assert.That(exact.Mode).IsEqualTo(AnnSearchMode.ExactSmallSet);
        var expected = AdaptiveFilteredPlannerOracle.Rank(records, query, bottom);
        await Assert.That(exact.Candidates.Select(candidate => candidate.SourceOrdinal).ToArray())
            .IsEquivalentTo(expected.Select(candidate => candidate.SourceOrdinal).ToArray(), CollectionOrdering.Matching);
        await AdaptiveFilteredPlannerOracle.AssertReturnedCandidatesAsync(exact, records, query, bottom);
        await AssertBudgetExceededAsync(() => exactIndex.Search(query, bottom.Length, bitmap,
            PackedAnnIndexTestSupport.Budget(database, exact.WorkUnits - 1)));
        var healthy = index.Search(query, 1, null, PackedAnnIndexTestSupport.Budget(database));
        await Assert.That(healthy.Mode).IsEqualTo(AnnSearchMode.Approximate);
        await Assert.That(healthy.Candidates.Length).IsEqualTo(1);
        await AdaptiveFilteredPlannerOracle.AssertReturnedCandidatesAsync(healthy, records, query,
            Enumerable.Range(0, records.Length).ToArray());
        await UnchangedAsync(database, metric, position, original);
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
