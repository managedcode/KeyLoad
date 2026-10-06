using KeyLoad.Core;
using KeyLoad.Query.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class PackedAnnBudgetTests
{
    [Test]
    public async Task AcAnn003ExactAndExcessBuildAndSearchWorkLimitsAreEnforced()
    {
        using var database = new TestDatabase();
        PackedAnnTestData.Configure(database);
        PackedAnnTestData.Seed(database, 64, 8, DistanceMetric.Cosine);
        var records = PackedAnnTestData.Load(database, DistanceMetric.Cosine);
        var space = PackedAnnTestData.Space(DistanceMetric.Cosine, 8);
        var measuredBuild = new AnnWorkBudget(new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(database.Database.Limits)),
            PackedAnnIndexTestSupport.GenerousWorkLimit);
        var index = PackedAnnIndex.Build(space, records, UnitExecutionOptions.PackedAnn(new()), measuredBuild);
        var exactBuild = Build(space, records, measuredBuild.WorkUnits);
        await Assert.That(exactBuild.Count).IsEqualTo(index.Count);
        await AssertBudgetExceeded(() => Build(space, records, measuredBuild.WorkUnits - 1));

        var query = PackedAnnTestData.Vector(200, space.Dimension, PackedAnnTestData.CorpusSeed);
        var measuredSearchBudget = PackedAnnIndexTestSupport.Budget(database);
        var measuredSearch = index.Search(query, 10, null, measuredSearchBudget);
        await Assert.That(measuredSearchBudget.WorkUnits).IsEqualTo(measuredSearch.WorkUnits);
        await Assert.That(measuredSearchBudget.DistanceEvaluations).IsEqualTo(measuredSearch.DistanceEvaluations);
        await Assert.That(measuredSearchBudget.EdgeVisits).IsEqualTo(measuredSearch.EdgeVisits);
        var exactSearch = Run(index, database, query, measuredSearch.WorkUnits);
        await Assert.That(exactSearch.WorkUnits).IsEqualTo(measuredSearch.WorkUnits);
        await Assert.That(exactSearch.Candidates.Select(candidate => candidate.DocumentId))
            .IsEquivalentTo(measuredSearch.Candidates.Select(candidate => candidate.DocumentId));
        await AssertBudgetExceeded(() => index.Search(query, 10, null,
            PackedAnnIndexTestSupport.Budget(database, maxWorkUnits: measuredSearch.WorkUnits - 1)));
        var sharedBudget = PackedAnnIndexTestSupport.Budget(database);
        var beforeFirst = sharedBudget.WorkUnits;
        var distancesBeforeFirst = sharedBudget.DistanceEvaluations;
        var edgesBeforeFirst = sharedBudget.EdgeVisits;
        var first = index.Search(query, 10, null, sharedBudget);
        var beforeSecond = sharedBudget.WorkUnits;
        var distancesBeforeSecond = sharedBudget.DistanceEvaluations;
        var edgesBeforeSecond = sharedBudget.EdgeVisits;
        var second = index.Search(query, 10, null, sharedBudget);
        await Assert.That(first.WorkUnits).IsEqualTo(beforeSecond - beforeFirst);
        await Assert.That(first.DistanceEvaluations).IsEqualTo(distancesBeforeSecond - distancesBeforeFirst);
        await Assert.That(first.EdgeVisits).IsEqualTo(edgesBeforeSecond - edgesBeforeFirst);
        await Assert.That(second.WorkUnits).IsEqualTo(sharedBudget.WorkUnits - beforeSecond);
        await Assert.That(second.DistanceEvaluations).IsEqualTo(sharedBudget.DistanceEvaluations - distancesBeforeSecond);
        await Assert.That(second.EdgeVisits).IsEqualTo(sharedBudget.EdgeVisits - edgesBeforeSecond);
    }

    [Test, NotInParallel(PackedAnnBuildResources.AdmissionKey)]
    public async Task AcAnn003RealCancellationAndDeadlineInterruptWideCanonicalSearchAndAllowHealthySearch()
    {
        using var database = new TestDatabase();
        PackedAnnTestData.Configure(database);
        PackedAnnTestData.Seed(database, PackedAnnTestData.QualityRecordCount,
            PackedAnnTestData.QualityDimension, DistanceMetric.DotProduct);
        var records = PackedAnnTestData.Load(database, DistanceMetric.DotProduct);
        await Assert.That(records.Length).IsEqualTo(PackedAnnTestData.QualityRecordCount);
        var space = PackedAnnTestData.Space(DistanceMetric.DotProduct, PackedAnnTestData.QualityDimension);
        var options = new PackedAnnOptions { ExactThreshold = 0, EfSearch = 4_096 };
        var budget = PackedAnnIndexTestSupport.Budget(database);
        var index = PackedAnnBuildObservationRunner.Build(PackedAnnBuildScenario.WideSearchSetup,
            space, records, options, budget);
        var query = PackedAnnTestData.Vector(41, space.Dimension, PackedAnnTestData.CorpusSeed);
        await Assert.That(index.Count).IsEqualTo(PackedAnnTestData.QualityRecordCount);

        await PackedAnnWideSearchBoundary.AssertCancellationAsync(index, query, database.Database.Limits);
        await PackedAnnWideSearchBoundary.AssertOneSecondDeadlineAsync(index, query);

        var healthy = index.Search(query, 10, null, PackedAnnIndexTestSupport.Budget(database));
        await Assert.That(healthy.Candidates.Length).IsEqualTo(10);
        await Assert.That(healthy.EdgeVisits).IsGreaterThan(0);
    }

    private static PackedAnnIndex Build(VectorSpace space, VectorRecord[] records, long workLimit)
        => PackedAnnIndex.Build(space, records, UnitExecutionOptions.PackedAnn(new()), new(new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(new())), workLimit));

    private static AnnSearchResult Run(PackedAnnIndex index, TestDatabase database, float[] query, long workLimit)
        => index.Search(query, 10, null, PackedAnnIndexTestSupport.Budget(database, maxWorkUnits: workLimit));

    private static async Task AssertBudgetExceeded(Action operation)
    {
        var failure = Assert.ThrowsExactly<KeyLoadException>(operation);
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }
}
