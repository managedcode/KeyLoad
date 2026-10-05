using KeyLoad.Query.Features.Search;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class AdaptiveFilteredPlannerTests
{
    private const int RecordCount = PackedAnnTestData.QualityRecordCount;
    private const int TopCount = PackedAnnTestData.TopK;

    [Test, NotInParallel(PackedAnnBuildResources.AdmissionKey)]
    public async Task AcFilter004SelectivityPlanAndPackedSearchMatchIndependentScalarCohortOracles()
    {
        using var database = new TestDatabase();
        PackedAnnTestData.Configure(database);
        var metric = DistanceMetric.DotProduct;
        var space = PackedAnnTestData.Space(metric, PackedAnnTestData.QualityDimension);
        PackedAnnTestData.Seed(database, RecordCount, space.Dimension, metric);
        var records = PackedAnnTestData.Load(database, metric);
        var options = new PackedAnnOptions { ExactThreshold = 32, Seed = PackedAnnTestData.CorpusSeed };
        var budget = PackedAnnIndexTestSupport.Budget(database);
        var index = PackedAnnBuildObservationRunner.Build(PackedAnnBuildScenario.AdaptiveSelectivity,
            space, records, options, budget);
        var query = PackedAnnTestData.Vector(31_337, space.Dimension, PackedAnnTestData.CorpusSeed);

        foreach (var cohort in AdaptiveFilteredPlannerOracle.Cohorts(records, query))
        {
            await VerifyCohortAsync(database, records, index, query, cohort);
        }
    }

    [Test]
    public async Task AcFilter004PlannerUsesCeilingBreadthAndCapsWithCheckedCorpusArithmetic()
    {
        using var database = new TestDatabase();
        var work = PackedAnnIndexTestSupport.Budget(database);
        var full = FilteredVectorPlanner.Create(10_000, 10, 10_000, 32, 128, 4_096, work);
        var tenPercent = FilteredVectorPlanner.Create(10_000, 10, 1_000, 32, 128, 4_096, work);
        var onePercent = FilteredVectorPlanner.Create(10_000, 10, 100, 32, 128, 4_096, work);
        var smallest = FilteredVectorPlanner.Create(10_000, 10, 10, 32, 128, 4_096, work);
        var capped = FilteredVectorPlanner.Create(5_000_000, 1_000, 1, 0, 1, 4_096, work);
        var integerBoundary = FilteredVectorPlanner.Create(int.MaxValue, int.MaxValue,
            int.MaxValue, 0, 1, 4_096, work);

        await Assert.That(full.InitialBreadth).IsEqualTo(128);
        await Assert.That(tenPercent.InitialBreadth).IsEqualTo(128);
        await Assert.That(onePercent.InitialBreadth).IsEqualTo(1_000);
        await Assert.That(smallest.UseExact).IsTrue();
        await Assert.That(smallest.InitialBreadth).IsZero();
        await Assert.That(capped.InitialBreadth).IsEqualTo(4_096);
        await Assert.That(integerBoundary.InitialBreadth).IsEqualTo(4_096);
    }

    [Test]
    public async Task AcFilter004SmallAndEmptyBitmapsEnumerateOnlyAdmittedRows()
    {
        using var database = new TestDatabase();
        PackedAnnTestData.Configure(database);
        var metric = DistanceMetric.DotProduct;
        var space = PackedAnnTestData.Space(metric, 2);
        PackedAnnTestData.Seed(database, 1_024, space.Dimension, metric);
        var records = PackedAnnTestData.Load(database, metric);
        var options = new PackedAnnOptions { ExactThreshold = 16 };
        var index = PackedAnnIndexTestSupport.Build(space, records, options,
            PackedAnnIndexTestSupport.Budget(database));
        var query = PackedAnnTestData.Vector(77, space.Dimension, PackedAnnTestData.CorpusSeed);
        var selected = new[] { 13, 511, 1_023 };
        var exact = AdaptiveFilteredPlannerOracle.Rank(records, query, selected);
        var small = index.Search(query, selected.Length, AdaptiveFilteredPlannerOracle.Bitmap(records.Length, selected),
            PackedAnnIndexTestSupport.Budget(database));
        var empty = index.Search(query, TopCount, new ulong[(records.Length + 63) / 64],
            PackedAnnIndexTestSupport.Budget(database));

        await Assert.That(small.Mode).IsEqualTo(AnnSearchMode.ExactSmallSet);
        await Assert.That(small.EligibleCount).IsEqualTo(selected.Length);
        await Assert.That(small.Candidates.Select(candidate => candidate.SourceOrdinal).ToArray())
            .IsEquivalentTo(exact.Select(candidate => candidate.SourceOrdinal).ToArray(), CollectionOrdering.Matching);
        await Assert.That(small.DistanceEvaluations).IsEqualTo(selected.Length);
        await Assert.That(small.ExpansionPasses).IsEqualTo(0);
        await Assert.That(small.FallbackDistanceEvaluations).IsEqualTo(0);
        await Assert.That(empty.Candidates).IsEmpty();
        await Assert.That(empty.EligibleCount).IsZero();
        await Assert.That(empty.DistanceEvaluations).IsZero();
        await Assert.That(empty.ExpansionPasses).IsEqualTo(0);
    }

    [Test, NotInParallel(PackedAnnBuildResources.AdmissionKey)]
    public async Task AcFilter004InsufficientAdaptiveCandidatesUseChargedExactFallbackThenHealthySearch()
    {
        using var database = new TestDatabase();
        PackedAnnTestData.Configure(database);
        var metric = DistanceMetric.DotProduct;
        var space = PackedAnnTestData.Space(metric, PackedAnnTestData.QualityDimension);
        PackedAnnTestData.Seed(database, RecordCount, space.Dimension, metric);
        var records = PackedAnnTestData.Load(database, metric);
        var options = new PackedAnnOptions { ExactThreshold = 0, EfSearch = 1 };
        var budget = PackedAnnIndexTestSupport.Budget(database);
        var index = PackedAnnBuildObservationRunner.Build(PackedAnnBuildScenario.AdaptiveFallback,
            space, records, options, budget);
        var query = PackedAnnTestData.Vector(22_917, space.Dimension, PackedAnnTestData.CorpusSeed);
        var bottom = AdaptiveFilteredPlannerOracle.Rank(records, query,
            Enumerable.Range(0, records.Length).ToArray()).TakeLast(100).Select(item => item.SourceOrdinal).ToArray();
        var bitmap = AdaptiveFilteredPlannerOracle.Bitmap(records.Length, bottom);
        var measured = index.Search(query, bottom.Length, bitmap, PackedAnnIndexTestSupport.Budget(database));

        await Assert.That(measured.Mode).IsEqualTo(AnnSearchMode.ExactAfterInsufficientCandidates);
        await Assert.That(measured.EligibleCount).IsEqualTo(bottom.Length);
        await Assert.That(measured.ExpansionPasses).IsGreaterThan(0);
        await Assert.That(measured.FallbackDistanceEvaluations).IsEqualTo(bottom.Length);
        await Assert.That(measured.DistanceEvaluations >= measured.FallbackDistanceEvaluations).IsTrue();
        await AssertBudgetExceededAsync(() => index.Search(query, bottom.Length, bitmap,
            PackedAnnIndexTestSupport.Budget(database, measured.WorkUnits - 1)));
        var healthy = index.Search(query, bottom.Length, bitmap, PackedAnnIndexTestSupport.Budget(database));
        await Assert.That(healthy.Candidates.Select(candidate => candidate.SourceOrdinal).ToArray())
            .IsEquivalentTo(measured.Candidates.Select(candidate => candidate.SourceOrdinal).ToArray(), CollectionOrdering.Matching);
    }

    [Test]
    public async Task AcFilter004AdaptiveWalkMayNavigateIneligibleVerticesToAnIsolatedEligibleEndpoint()
    {
        using var database = new TestDatabase();
        PackedAnnTestData.Configure(database);
        var metric = DistanceMetric.DotProduct;
        var space = PackedAnnTestData.Space(metric, PackedAnnTestData.QualityDimension);
        PackedAnnTestData.Seed(database, 512, space.Dimension, metric);
        var records = PackedAnnTestData.Load(database, metric);
        var index = PackedAnnIndexTestSupport.Build(space, records,
            new PackedAnnOptions { ExactThreshold = 0, EfSearch = 4 }, PackedAnnIndexTestSupport.Budget(database));
        var endpoint = records.Length - 1;
        var eligibility = AdaptiveFilteredPlannerOracle.Bitmap(records.Length, [endpoint]);
        var result = index.Search(records[endpoint].Values.AsMemory(), 1, eligibility,
            PackedAnnIndexTestSupport.Budget(database));

        await Assert.That(result.Mode is AnnSearchMode.Approximate or AnnSearchMode.ExactAfterInsufficientCandidates).IsTrue();
        await Assert.That(result.Candidates.Select(candidate => candidate.SourceOrdinal).ToArray())
            .IsEquivalentTo([endpoint], CollectionOrdering.Matching);
        await Assert.That(result.Candidates[0].DocumentId).IsEqualTo(records[endpoint].DocumentId);
        await Assert.That(result.EligibleCount).IsEqualTo(1);
        await Assert.That(result.EdgeVisits).IsGreaterThan(0);
        await Assert.That(result.ExpansionPasses).IsGreaterThan(0);
        await Assert.That(result.FallbackDistanceEvaluations == 0
            || result.FallbackDistanceEvaluations == 1).IsTrue();
    }

    [Test]
    public async Task AcFilter004CanceledPlannerLeavesTheNextPackedQueryHealthy()
    {
        using var database = new TestDatabase();
        PackedAnnTestData.Configure(database);
        var metric = DistanceMetric.DotProduct;
        var space = PackedAnnTestData.Space(metric, 2);
        PackedAnnTestData.Seed(database, 256, space.Dimension, metric);
        var records = PackedAnnTestData.Load(database, metric);
        var index = PackedAnnIndexTestSupport.Build(database, metric, records);
        var query = PackedAnnTestData.Vector(99, space.Dimension, PackedAnnTestData.CorpusSeed);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var failure = Assert.ThrowsExactly<OperationCanceledException>(() => index.Search(query, 10, null,
            PackedAnnIndexTestSupport.Budget(database, token: cancellation.Token)));
        var healthy = index.Search(query, 10, null, PackedAnnIndexTestSupport.Budget(database));

        await Assert.That(failure).IsNotNull();
        await Assert.That(healthy.Candidates.Length).IsEqualTo(10);
        await Assert.That(healthy.EligibleCount).IsEqualTo(records.Length);
    }

    private static async Task VerifyCohortAsync(TestDatabase database, VectorRecord[] records,
        PackedAnnIndex index, float[] query, AdaptiveFilteredCohort cohort)
    {
        var bitmap = AdaptiveFilteredPlannerOracle.Bitmap(records.Length, cohort.Ordinals);
        var result = index.Search(query, TopCount, bitmap, PackedAnnIndexTestSupport.Budget(database));
        var expected = AdaptiveFilteredPlannerOracle.Rank(records, query, cohort.Ordinals).Take(TopCount).ToArray();
        var repeated = index.Search(query, TopCount, bitmap, PackedAnnIndexTestSupport.Budget(database));
        var relevant = result.Candidates.Count(candidate => expected.Any(item => item.SourceOrdinal == candidate.SourceOrdinal));

        await Assert.That(result.EligibleCount).IsEqualTo(cohort.Ordinals.Length);
        await Assert.That(result.Candidates.Length).IsEqualTo(TopCount);
        await Assert.That((double)relevant / TopCount).IsGreaterThanOrEqualTo(0.95);
        await Assert.That(repeated.Candidates.AsSpan().SequenceEqual(result.Candidates)).IsTrue();
        await Assert.That(result.WorkUnits).IsGreaterThan(0);
        await Assert.That(result.DistanceEvaluations).IsGreaterThan(0);
        await Assert.That(result.ScratchBytesUpperBound).IsGreaterThan(0);
        await AssertCohortMetricsAsync(result, cohort.Ordinals.Length);
        await AdaptiveFilteredPlannerOracle.AssertReturnedCandidatesAsync(result, records, query, cohort.Ordinals);
    }

    private static async Task AssertCohortMetricsAsync(AnnSearchResult result, int eligibleCount)
    {
        if (eligibleCount <= 32)
        {
            await Assert.That(result.Mode).IsEqualTo(AnnSearchMode.ExactSmallSet);
            await Assert.That(result.ExpansionPasses).IsZero();
            await Assert.That(result.FallbackDistanceEvaluations).IsZero();
            return;
        }
        await Assert.That(result.ExpansionPasses).IsGreaterThan(0);
        await Assert.That(result.ExpansionPasses).IsLessThanOrEqualTo(8);
        var fallback = result.Mode == AnnSearchMode.ExactAfterInsufficientCandidates;
        await Assert.That(result.FallbackDistanceEvaluations > 0).IsEqualTo(fallback);
        if (fallback)
        {
            await Assert.That(result.FallbackDistanceEvaluations).IsEqualTo(eligibleCount);
        }
    }

    private static async Task AssertBudgetExceededAsync(Action operation)
    {
        var failure = Assert.ThrowsExactly<KeyLoadException>(operation);
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }
}
