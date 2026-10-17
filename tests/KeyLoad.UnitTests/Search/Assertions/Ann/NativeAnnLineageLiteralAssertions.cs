using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal static class NativeAnnLineageLiteralAssertions
{
    private const string Field = "/embedding";

    internal static async Task AssertAsync(TestDatabase database, AnnSourceCut original, AnnSourceCut rebuilt)
    {
        var space = new VectorSpace("seed-space", 3, DistanceMetric.Cosine, "seed-model", "v1");
        VectorRecord[] expected =
        [
            new("seed-00000", Field, space, [3, 2, 1], 1),
            new("seed-00001", Field, space, [1.25f, 1, -0.5f], 1),
            new("seed-00002", Field, space, [2.25f, 1, -0.5f], 1)
        ];
        var digest = AnnSeedDigestOracle.Compute(Field, space, expected);
        await Assert.That(original.CorpusSha256).IsEqualTo(digest);
        await Assert.That(rebuilt.CorpusSha256).IsEqualTo(digest);
        var actual = AnnSeedTestSupport.Capture(database, AnnProjectionPinTestSupport.Principal);
        await Assert.That(actual.CorpusSha256).IsEqualTo(digest);
        await Assert.That(actual.Records.Length).IsEqualTo(expected.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            await Assert.That(actual.Records[index].DocumentId).IsEqualTo(expected[index].DocumentId);
            await Assert.That(actual.Records[index].DocumentRevision).IsEqualTo(expected[index].DocumentRevision);
            await Assert.That(actual.Records[index].Field).IsEqualTo(Field);
            await Assert.That(actual.Records[index].Space).IsEqualTo(space);
            await Assert.That(actual.Records[index].Values).IsEquivalentTo(expected[index].Values, CollectionOrdering.Matching);
        }
    }
}
