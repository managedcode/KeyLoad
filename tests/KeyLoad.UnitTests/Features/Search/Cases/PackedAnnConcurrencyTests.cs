using KeyLoad.Query.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class PackedAnnConcurrencyTests
{
    [Test]
    public async Task AcAnn003ConcurrentQueriesShareOnlyImmutableIndexState()
    {
        using var database = new TestDatabase();
        PackedAnnTestData.Configure(database);
        PackedAnnTestData.Seed(database, 512, PackedAnnTestData.QualityDimension, DistanceMetric.Cosine);
        var records = PackedAnnTestData.Load(database, DistanceMetric.Cosine);
        var space = PackedAnnTestData.Space(DistanceMetric.Cosine, PackedAnnTestData.QualityDimension);
        var index = PackedAnnIndexTestSupport.Build(space, records, new PackedAnnOptions { ExactThreshold = 0 },
            PackedAnnIndexTestSupport.Budget(database));
        var queries = Enumerable.Range(0, 16).Select(number =>
            PackedAnnTestData.Vector(number, space.Dimension, PackedAnnTestData.CorpusSeed)).ToArray();
        var expected = queries.Select(query => index.Search(query, 10, null,
            PackedAnnIndexTestSupport.Budget(database))).ToArray();

        var tasks = queries.Select(query => Task.Run(() => index.Search(query, 10, null,
            PackedAnnIndexTestSupport.Budget(database)))).ToArray();
        var results = await Task.WhenAll(tasks);
        for (var queryIndex = 0; queryIndex < queries.Length; queryIndex++)
        {
            await Assert.That(results[queryIndex].Candidates.Select(candidate => candidate.DocumentId))
                .IsEquivalentTo(expected[queryIndex].Candidates.Select(candidate => candidate.DocumentId));
            for (var rank = 0; rank < 10; rank++)
            {
                await Assert.That(results[queryIndex].Candidates[rank].DocumentId)
                    .IsEqualTo(expected[queryIndex].Candidates[rank].DocumentId);
                await Assert.That(results[queryIndex].Candidates[rank].Score).IsEqualTo(expected[queryIndex].Candidates[rank].Score);
                await Assert.That(results[queryIndex].Candidates[rank].SourceOrdinal)
                    .IsEqualTo(expected[queryIndex].Candidates[rank].SourceOrdinal);
            }
        }
        await Assert.That(results.All(result => result.WorkUnits > 0 && result.EdgeVisits > 0)).IsTrue();
    }
}
