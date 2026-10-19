using KeyLoad.Core.Features.Search;
using KeyLoad.Query;
using KeyLoad.Query.Features.Search;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal static class AnnLineageReplayOracle
{
    internal enum Stage
    {
        Initial, Hidden, Reprojected
    }
    private const string Field = "/embedding";
    private const int Count = 3;
    private const long Revision = 1;
    private static readonly float[] Query = [1, 0, 0];
    private static readonly VectorSpace Space = new("seed-space", 3, DistanceMetric.Cosine, "seed-model", "v1");

    internal static async Task AssertSeedAsync(AnnSeed seed, Stage stage)
    {
        var expected = Expected(stage);
        await Assert.That(seed.CorpusSha256).IsEqualTo(AnnSeedDigestOracle.Compute(Field, Space, expected));
        await Assert.That(seed.Records.Length).IsEqualTo(expected.Length);
        await Assert.That(seed.Scope.Field).IsEqualTo(Field);
        await Assert.That(seed.Scope.Space).IsEqualTo(Space);
        for (var index = 0; index < expected.Length; index++)
        {
            var actual = seed.Records[index];
            await Assert.That(actual.DocumentId).IsEqualTo(expected[index].DocumentId);
            await Assert.That(actual.DocumentRevision).IsEqualTo(Revision);
            await Assert.That(actual.Field).IsEqualTo(Field);
            await Assert.That(actual.Space).IsEqualTo(Space);
            await Assert.That(actual.Values).IsEquivalentTo(expected[index].Values, CollectionOrdering.Matching);
        }
    }

    internal static async Task AssertSearchAsync(PackedAnnIndex index, AnnWorkBudget work)
    {
        var expected = Expected(Stage.Reprojected);
        var result = index.Search(Query, Count, null, work);
        int[] ordinals = [2, 1, 0];
        await Assert.That(result.Candidates.Length).IsEqualTo(Count);
        await Assert.That(result.EligibleCount).IsEqualTo(Count);
        await Assert.That(result.Mode).IsEqualTo(AnnSearchMode.ExactSmallSet);
        for (var rank = 0; rank < Count; rank++)
        {
            var ordinal = ordinals[rank];
            var candidate = result.Candidates[rank];
            await Assert.That(candidate.SourceOrdinal).IsEqualTo(ordinal);
            await Assert.That(candidate.DocumentId).IsEqualTo(expected[ordinal].DocumentId);
            await Assert.That(candidate.DocumentRevision).IsEqualTo(Revision);
            await Assert.That(candidate.Score).IsEqualTo(SearchEngine.Similarity(Query,
                expected[ordinal].Values.ToArray(), DistanceMetric.Cosine));
        }
    }

    private static VectorRecord[] Expected(Stage stage)
    {
        VectorRecord[] rows =
        [
            new("seed-00000", Field, Space, stage == Stage.Reprojected ? [4, 3, 2] : [3, 2, 1], Revision),
            new("seed-00001", Field, Space, [1.25f, 1, -0.5f], Revision),
            new("seed-00002", Field, Space, [2.25f, 1, -0.5f], Revision)
        ];
        return stage == Stage.Hidden ? rows[1..] : rows;
    }
}
