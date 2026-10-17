namespace KeyLoad.UnitTests.Features.Search;

internal sealed class PackedAnnConstructionValueTests
{
    private static readonly DistanceMetric[] Metrics =
        [DistanceMetric.Cosine, DistanceMetric.Euclidean, DistanceMetric.DotProduct];

    [Test]
    public async Task AcAnn011OwnedAndClassTraversalMatchOnPersistedGraphsForEveryMetric()
    {
        foreach (var metric in Metrics)
        {
            using var database = new TestDatabase();
            PackedAnnConstructionValueFixture.Seed(database);
            var fixture = PackedAnnConstructionValueFixture.Build(database, metric);
            await PackedAnnConstructionValueAssertions.AssertEquivalentAsync(database, fixture, metric,
                fixture.State.EntryPoint);
        }
    }

    [Test]
    public async Task AcAnn011ZeroDotProductsKeepExactOrdinalTieOrder()
    {
        using var database = new TestDatabase();
        PackedAnnTestData.Configure(database);
        var fixture = PackedAnnConstructionValueFixture.BuildZeroDotProduct(database);
        await PackedAnnConstructionValueAssertions.AssertEquivalentAsync(database, fixture,
            DistanceMetric.DotProduct, fixture.State.EntryPoint);
        await PackedAnnConstructionValueAssertions.AssertZeroTieOrderAsync(database, fixture);
    }

    [Test, NotInParallel]
    public async Task AcAnn011WarmedOwnedTraversalAllocatesZeroBytesOnTheCallingThread()
    {
        using var database = new TestDatabase();
        PackedAnnConstructionValueFixture.Seed(database);
        foreach (var metric in Metrics)
        {
            var fixture = PackedAnnConstructionValueFixture.Build(database, metric);
            var observation = PackedAnnConstructionValueTraversal.Measure(database, fixture, metric);
            await Assert.That(observation.AllocatedBytes).IsEqualTo(0L);
            await Assert.That(observation.CandidateCount).IsGreaterThan(0);
            await Assert.That(observation.WorkUnits).IsGreaterThan(0L);
            await Assert.That(observation.DistanceEvaluations).IsGreaterThan(0L);
            await Assert.That(observation.EdgeVisits).IsGreaterThan(0L);
        }
    }

    [Test]
    public async Task AcAnn011ConstrainedScoreKeepsTypedOrdinalValidation()
    {
        using var database = new TestDatabase();
        PackedAnnConstructionValueFixture.Seed(database);
        var fixture = PackedAnnConstructionValueFixture.Build(database, DistanceMetric.Cosine);
        await PackedAnnConstructionValueAssertions.AssertInvalidOrdinalAsync(database, fixture,
            DistanceMetric.Cosine);
    }
}
