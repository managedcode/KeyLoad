using System.Collections.Immutable;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class AnnSeedVisibilityTests
{
    private const string HiddenOwner = "another-owner";
    private const string StaleId = "stale-vector";
    private const string DeletedId = "deleted-vector";
    private const string DifferentSpaceId = "different-space-id";
    private const string DifferentModelId = "different-space-model";
    private const string DifferentVersionId = "different-space-version";
    private const string DifferentMetricId = "different-space-metric";
    private const string DifferentDimensionId = "different-space-dimension";

    [Test]
    public async Task CaptureRetainsOnlyVisibleCurrentRowsInExactFullVectorSpace()
    {
        using var database = AnnSeedTestSupport.Create(0);
        database.Commit(
            new PutDocument(AnnSeedTestSupport.Collection, "visible", "{}", Access: new(AnnSeedTestSupport.Owner)),
            new PutVector(AnnSeedTestSupport.Collection, "visible", AnnSeedTestSupport.Field, [1, 0, 0], AnnSeedTestSupport.Space(), 1),
            new PutDocument(AnnSeedTestSupport.Collection, "hidden", "{}", Access: new(HiddenOwner)),
            new PutVector(AnnSeedTestSupport.Collection, "hidden", AnnSeedTestSupport.Field, [0, 1, 0], AnnSeedTestSupport.Space(), 1),
            new PutDocument(AnnSeedTestSupport.Collection, StaleId, "{}", Access: new(AnnSeedTestSupport.Owner)),
            new PutVector(AnnSeedTestSupport.Collection, StaleId, AnnSeedTestSupport.Field, [0, 0, 1], AnnSeedTestSupport.Space(), 1),
            new PutDocument(AnnSeedTestSupport.Collection, DeletedId, "{}", Access: new(AnnSeedTestSupport.Owner)),
            new PutVector(AnnSeedTestSupport.Collection, DeletedId, AnnSeedTestSupport.Field, [1, 1, 0], AnnSeedTestSupport.Space(), 1),
            new PutDocument(AnnSeedTestSupport.Collection, DifferentSpaceId, "{}", Access: new(AnnSeedTestSupport.Owner)),
            new PutVector(AnnSeedTestSupport.Collection, DifferentSpaceId, AnnSeedTestSupport.Field,
                [0.5f, 0.5f, 0.5f], AnnSeedTestSupport.Space(id: "another-valid-space"), 1),
            new PutDocument(AnnSeedTestSupport.Collection, DifferentModelId, "{}", Access: new(AnnSeedTestSupport.Owner)),
            new PutVector(AnnSeedTestSupport.Collection, DifferentModelId, AnnSeedTestSupport.Field,
                [0.5f, 0.5f, 0.5f], AnnSeedTestSupport.Space(model: "another-valid-model"), 1),
            new PutDocument(AnnSeedTestSupport.Collection, DifferentVersionId, "{}", Access: new(AnnSeedTestSupport.Owner)),
            new PutVector(AnnSeedTestSupport.Collection, DifferentVersionId, AnnSeedTestSupport.Field,
                [0.5f, 0.5f, 0.5f], AnnSeedTestSupport.Space(version: "v2"), 1),
            new PutDocument(AnnSeedTestSupport.Collection, DifferentMetricId, "{}", Access: new(AnnSeedTestSupport.Owner)),
            new PutVector(AnnSeedTestSupport.Collection, DifferentMetricId, AnnSeedTestSupport.Field,
                [0.5f, 0.5f, 0.5f], AnnSeedTestSupport.Space(metric: DistanceMetric.Euclidean), 1),
            new PutDocument(AnnSeedTestSupport.Collection, DifferentDimensionId, "{}", Access: new(AnnSeedTestSupport.Owner)),
            new PutVector(AnnSeedTestSupport.Collection, DifferentDimensionId, AnnSeedTestSupport.Field,
                [0.5f, 0.5f], AnnSeedTestSupport.Space(dimension: 2), 1));
        database.Commit(new PatchDocument(AnnSeedTestSupport.Collection, StaleId,
            [new("/status", PatchKind.Set, "\"updated\"")], 1));
        database.Commit(new DeleteDocument(AnnSeedTestSupport.Collection, DeletedId, 1));
        var seed = AnnSeedTestSupport.Capture(database);

        await Assert.That(seed.Records.Select(record => record.DocumentId).ToArray())
            .IsEquivalentTo(["visible"], CollectionOrdering.Matching);
        await Assert.That(seed.Records.Single().DocumentRevision).IsEqualTo(1L);
        await Assert.That(seed.ReadBytes).IsGreaterThan(0L);
    }

    [Test]
    public async Task CaptureCountsValidDifferentSpacesAsExaminedButExcludesThem()
    {
        using var database = AnnSeedTestSupport.Create(1);
        var original = AnnSeedTestSupport.Capture(database);
        AnnSeedTestSupport.CommitVector(database, AnnSeedTestSupport.Id(0),
            ImmutableArray.Create(1f, 2f, 3f), AnnSeedTestSupport.Space(id: "other-valid-space"));

        var seed = AnnSeedTestSupport.Capture(database);

        await Assert.That(seed.Records).IsEmpty();
        await Assert.That(seed.ReadBytes).IsGreaterThan(original.ReadBytes);
    }
}
