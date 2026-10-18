using KeyLoad.Query;
using KeyLoad.Query.Features.Search;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class PackedAnnFilterTests
{
    [Test]
    public async Task AcAnn004BitmapValidationAndSmallEligibleSetUseExactOrderedResults()
    {
        using var database = new TestDatabase();
        PackedAnnTestData.Configure(database);
        var metric = DistanceMetric.DotProduct;
        var space = PackedAnnTestData.Space(metric, 2);
        PackedAnnIndexTestSupport.PersistVectors(database, space, PackedAnnTestData.Field(metric),
            [[1, 0], [1, 0], [0, 1], [-1, 0], [0, -1]]);
        var records = PackedAnnTestData.Load(database, metric);
        var index = PackedAnnIndexTestSupport.Build(database, metric, records);
        var eligible = Bits(records.Length, 0, 1);
        var result = index.Search(new float[] { 1, 0 }, 2, eligible, PackedAnnIndexTestSupport.Budget(database));
        await Assert.That(result.Mode).IsEqualTo(AnnSearchMode.ExactSmallSet);
        await Assert.That(result.Candidates.Select(candidate => candidate.DocumentId))
            .IsEquivalentTo(new[] { records[0].DocumentId, records[1].DocumentId }, CollectionOrdering.Matching);
        await Assert.That(result.Candidates[0].DocumentId).IsEqualTo(records[0].DocumentId);
        await Assert.That(result.Candidates[1].DocumentId).IsEqualTo(records[1].DocumentId);
        await Assert.That(result.Candidates[0].Score).IsEqualTo(result.Candidates[1].Score);
        await AssertValidation(() => index.Search(new float[] { 1, 0 }, 1, Array.Empty<ulong>(),
            PackedAnnIndexTestSupport.Budget(database)));
        await AssertValidation(() => index.Search(new float[] { 1, 0 }, 1, new ulong[] { 1UL << records.Length },
            PackedAnnIndexTestSupport.Budget(database)));
    }

    [Test]
    public async Task AcAnn004FilteredApproximateTraversalNeverReturnsDisallowedNavigationNodes()
    {
        using var database = new TestDatabase();
        PackedAnnTestData.Configure(database);
        var metric = DistanceMetric.Cosine;
        var space = PackedAnnTestData.Space(metric, PackedAnnTestData.QualityDimension);
        PackedAnnTestData.Seed(database, 512, space.Dimension, metric);
        var records = PackedAnnTestData.Load(database, metric);
        var options = new PackedAnnOptions { Connections = 4, EfConstruction = 64, EfSearch = 16, ExactThreshold = 8 };
        var index = PackedAnnIndexTestSupport.Build(space, records, options, PackedAnnIndexTestSupport.Budget(database));
        var bitmap = Bits(records.Length, Enumerable.Range(0, records.Length).Where(indexValue => indexValue % 2 == 0).ToArray());
        var query = PackedAnnTestData.Vector(88, space.Dimension, PackedAnnTestData.CorpusSeed);
        var result = index.Search(query, 10, bitmap, PackedAnnIndexTestSupport.Budget(database));
        await Assert.That(result.Mode).IsEqualTo(AnnSearchMode.Approximate);
        await Assert.That(result.EdgeVisits > 0).IsTrue();
        await Assert.That(result.Candidates.Length).IsEqualTo(10);
        foreach (var candidate in result.Candidates)
        {
            await Assert.That(candidate.SourceOrdinal % 2).IsEqualTo(0);
            await Assert.That(candidate.DocumentId).IsEqualTo(records[candidate.SourceOrdinal].DocumentId);
        }
    }

    [Test, NotInParallel]
    public async Task AcAnn004InsufficientFilteredCandidatesRefuseWithoutPartialAndHealthyQueryContinues()
    {
        using var database = new TestDatabase();
        PackedAnnTestData.Configure(database);
        var metric = DistanceMetric.DotProduct;
        var space = PackedAnnTestData.Space(metric, PackedAnnTestData.QualityDimension);
        PackedAnnTestData.Seed(database, PackedAnnTestData.QualityRecordCount, space.Dimension, metric);
        var records = PackedAnnTestData.Load(database, metric);
        var options = new PackedAnnOptions
        {
            Connections = 4,
            EfConstruction = 64,
            EfSearch = 1,
            ExactThreshold = 0,
            Seed = PackedAnnTestData.CorpusSeed
        };
        var budget = PackedAnnIndexTestSupport.Budget(database);
        var index = PackedAnnBuildObservationRunner.Build(PackedAnnBuildScenario.FilteredCandidateBound,
            space, records, options, budget);
        var selected = Enumerable.Range(0, records.Length).Where(ordinal => ordinal % 100 == 0).ToArray();
        var bitmap = Bits(records.Length, selected);
        var query = PackedAnnTestData.Vector(901, space.Dimension, PackedAnnTestData.CorpusSeed);
        var position = database.Store.Position;
        var original = JsonDefaults.Serialize(records);
        AnnSearchResult? partial = null;
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => partial = index.Search(query, selected.Length,
            bitmap, PackedAnnIndexTestSupport.Budget(database)));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(failure.Message).IsEqualTo("The bounded ANN graph did not supply the requested eligible candidates.");
        await Assert.That(partial).IsNull();
        var healthy = index.Search(query, 1, null, PackedAnnIndexTestSupport.Budget(database));
        await Assert.That(healthy.Mode).IsEqualTo(AnnSearchMode.Approximate);
        await Assert.That(healthy.Candidates.Length).IsEqualTo(1);
        await AdaptiveFilteredPlannerOracle.AssertReturnedCandidatesAsync(healthy, records, query,
            Enumerable.Range(0, records.Length).ToArray());
        await Assert.That(records.Length).IsEqualTo(PackedAnnTestData.QualityRecordCount);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(JsonDefaults.Serialize(PackedAnnTestData.Load(database, metric)).AsSpan().SequenceEqual(original)).IsTrue();
    }

    private static ulong[] Bits(int count, params int[] ordinals)
    {
        var result = new ulong[(count + 63) / 64];
        foreach (var ordinal in ordinals)
        {
            result[ordinal / 64] |= 1UL << (ordinal % 64);
        }
        return result;
    }

    private static async Task AssertValidation(Action operation)
    {
        var error = Assert.ThrowsExactly<KeyLoadException>(operation);
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Validation);
    }
}
