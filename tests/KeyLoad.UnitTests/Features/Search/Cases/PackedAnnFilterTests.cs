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
    public async Task AcAnn004InsufficientFilteredCandidatesFallBackToCompleteExactEligibleTopK()
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
        var index = PackedAnnBuildObservationRunner.Build(PackedAnnBuildScenario.FilteredFallback,
            space, records, options, budget);
        var selected = Enumerable.Range(0, records.Length).Where(ordinal => ordinal % 100 == 0).ToArray();
        var bitmap = Bits(records.Length, selected);
        var query = PackedAnnTestData.Vector(901, space.Dimension, PackedAnnTestData.CorpusSeed);
        var result = index.Search(query, selected.Length, bitmap, PackedAnnIndexTestSupport.Budget(database));
        var exact = records.Select((record, ordinal) => (record, ordinal))
            .Where(item => Array.BinarySearch(selected, item.ordinal) >= 0)
            .Select(item => new AnnCandidate(item.ordinal, item.record.DocumentId, item.record.DocumentRevision,
                SearchEngine.Similarity(query, item.record.Values.ToArray(), metric)))
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.DocumentId, StringComparer.Ordinal).ToArray();
        await Assert.That(records.Length).IsEqualTo(PackedAnnTestData.QualityRecordCount);
        await Assert.That(result.Mode).IsEqualTo(AnnSearchMode.ExactAfterInsufficientCandidates);
        await Assert.That(result.Candidates.Select(candidate => candidate.DocumentId))
            .IsEquivalentTo(exact.Select(candidate => candidate.DocumentId), CollectionOrdering.Matching);
        for (var ordinal = 0; ordinal < exact.Length; ordinal++)
        {
            await Assert.That(result.Candidates[ordinal].DocumentId).IsEqualTo(exact[ordinal].DocumentId);
            await Assert.That(result.Candidates[ordinal].SourceOrdinal).IsEqualTo(exact[ordinal].SourceOrdinal);
            await Assert.That(result.Candidates[ordinal].DocumentRevision).IsEqualTo(exact[ordinal].DocumentRevision);
            await Assert.That(result.Candidates[ordinal].Score).IsEqualTo(exact[ordinal].Score);
        }
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
