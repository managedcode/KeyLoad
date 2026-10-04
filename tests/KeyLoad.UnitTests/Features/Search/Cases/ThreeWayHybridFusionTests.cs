using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class ThreeWayHybridFusionTests
{
    private static readonly string[] AllHits = [ThreeWayHybridTestSupport.A, ThreeWayHybridTestSupport.B,
        ThreeWayHybridTestSupport.C, ThreeWayHybridTestSupport.D];
    private static readonly string[] RestrictedHits = [ThreeWayHybridTestSupport.B,
        ThreeWayHybridTestSupport.C, ThreeWayHybridTestSupport.D];

    [Test]
    public async Task AcGsearch003ThreeBranchesMatchIndependentWeightedOracleAndOrdinalTie()
    {
        using var database = CreateDatabase();
        var result = await new SearchEngine(database.Database).GraphSearchAsync(ThreeWayHybridTestSupport.Reader,
            ThreeWayHybridTestSupport.Request(database.Partition), TestContext.Current!.Execution.CancellationToken);
        var expected = ThreeWayHybridRrfOracle.Rank(AllHits, AllHits);
        await AssertHitsAsync(result.Hits, expected);
        await Assert.That(result.Hits.Select(hit => hit.Document.Reference.Id).ToArray())
            .IsEquivalentTo([ThreeWayHybridTestSupport.A, ThreeWayHybridTestSupport.B,
                ThreeWayHybridTestSupport.C, ThreeWayHybridTestSupport.D],
                TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(result.Hits[0].Score).IsEqualTo(result.Hits[1].Score)
            .Within(ThreeWayHybridTestSupport.Tolerance);
        await Assert.That(result.Hits.Any(hit => hit.Document.Reference.Id == ThreeWayHybridTestSupport.E)).IsFalse();
    }

    [Test]
    public async Task AcGsearch003ScopeAndAllowlistIntersectBeforeIndependentBranchRanks()
    {
        using var database = CreateDatabase();
        var request = ThreeWayHybridTestSupport.Request(database.Partition, [.. RestrictedHits], scoped: true);
        var result = await new SearchEngine(database.Database).GraphSearchAsync(ThreeWayHybridTestSupport.Reader,
            request, TestContext.Current!.Execution.CancellationToken);
        var expected = ThreeWayHybridRrfOracle.Rank(
            [ThreeWayHybridTestSupport.A, ThreeWayHybridTestSupport.B, ThreeWayHybridTestSupport.C], RestrictedHits);
        await AssertHitsAsync(result.Hits, expected);
        await Assert.That(result.Hits.Select(hit => hit.Document.Reference.Id).ToArray())
            .IsEquivalentTo([ThreeWayHybridTestSupport.B, ThreeWayHybridTestSupport.C],
                TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task AcGsearch003ExpansionIsSeparateAndZeroWeightsStillAuthorize()
    {
        using var database = CreateDatabase();
        var engine = new SearchEngine(database.Database);
        var token = TestContext.Current!.Execution.CancellationToken;
        var plain = await engine.GraphSearchAsync(ThreeWayHybridTestSupport.Reader,
            ThreeWayHybridTestSupport.Request(database.Partition), token);
        var expanded = await engine.GraphSearchAsync(ThreeWayHybridTestSupport.Reader,
            ThreeWayHybridTestSupport.Request(database.Partition, expansion: true), token);
        await AssertHitsAsync(expanded.Hits, ThreeWayHybridRrfOracle.Rank(AllHits, AllHits));
        await Assert.That(expanded.Hits.Select(hit => hit.Document.Reference.Id).ToArray())
            .IsEquivalentTo(plain.Hits.Select(hit => hit.Document.Reference.Id).ToArray(),
                TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(expanded.Expansion!.Documents.Select(item => item.Document.Reference.Id).ToArray())
            .IsEquivalentTo([ThreeWayHybridTestSupport.Context],
                TUnit.Assertions.Enums.CollectionOrdering.Matching);

        var zero = await engine.GraphSearchAsync(ThreeWayHybridTestSupport.Reader,
            ThreeWayHybridTestSupport.Request(database.Partition, allZero: true), token);
        await Assert.That(zero.Hits).IsEmpty();
        var denied = (await Assert.ThrowsExactlyAsync<KeyLoadException>(() => engine.GraphSearchAsync(
            ThreeWayHybridTestSupport.TextOnlyReader,
            ThreeWayHybridTestSupport.Request(database.Partition, allZero: true),
            token)))!;
        await Assert.That(denied.Code).IsEqualTo(ErrorCode.PermissionDenied);
        var labeled = ThreeWayHybridTestSupport.Request(database.Partition, allZero: true, labeled: true);
        var labelDenied = (await Assert.ThrowsExactlyAsync<KeyLoadException>(() => engine.GraphSearchAsync(
            ThreeWayHybridTestSupport.LabelDeniedReader, labeled, token)))!;
        await Assert.That(labelDenied.Code).IsEqualTo(ErrorCode.PermissionDenied);
    }

    private static TestDatabase CreateDatabase()
    {
        var database = new TestDatabase();
        ThreeWayHybridTestSupport.Configure(database);
        ThreeWayHybridTestSupport.Seed(database);
        ThreeWayHybridTestSupport.PersistReader(database, vectorGrant: true);
        ThreeWayHybridTestSupport.PersistReader(database, vectorGrant: false);
        ThreeWayHybridTestSupport.PersistReader(database, vectorGrant: true,
            principalId: ThreeWayHybridTestSupport.LabelDeniedReader);
        return database;
    }

    private static async Task AssertHitsAsync(IReadOnlyList<RankedDocument> actual, (string Id, double Score)[] expected)
    {
        await Assert.That(actual.Select(hit => hit.Document.Reference.Id).ToArray())
            .IsEquivalentTo(expected.Select(hit => hit.Id).ToArray(),
                TUnit.Assertions.Enums.CollectionOrdering.Matching);
        for (var index = 0; index < expected.Length; index++)
        {
            await Assert.That(actual[index].Score).IsEqualTo(expected[index].Score)
                .Within(ThreeWayHybridTestSupport.Tolerance);
        }
    }
}
