using System.Collections.Immutable;
using KeyLoad.Core.Features.Search;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class AnnSeedFingerprintTests
{
    private const string EmptyJson = "{}";
    private const string OtherCollection = "ann-seed-other-records";

    [Test]
    public async Task DigestMatchesIndependentFrozenPreimageOracle()
    {
        using var database = AnnSeedTestSupport.Create(1);
        var seed = AnnSeedTestSupport.Capture(database);
        var expectedRecord = new VectorRecord(AnnSeedTestSupport.Id(0), AnnSeedTestSupport.Field,
            AnnSeedTestSupport.Space(), ImmutableArray.Create(0.25f, 1f, -0.5f), 1);
        var expected = AnnSeedDigestOracle.Compute(AnnSeedTestSupport.Field, AnnSeedTestSupport.Space(),
            [expectedRecord]);

        await Assert.That(seed.CorpusSha256).IsEqualTo(expected);
    }

    [Test]
    public async Task DigestUsesIndependentOrdinalOrderForFixedTwoRecordCorpus()
    {
        const string HighBmpId = "\uE000";
        const string SupplementaryId = "\U00010000";
        using var database = AnnSeedTestSupport.Create(0);
        database.Commit(
            new PutDocument(AnnSeedTestSupport.Collection, HighBmpId, EmptyJson,
                Access: new(AnnSeedTestSupport.Owner)),
            new PutVector(AnnSeedTestSupport.Collection, HighBmpId, AnnSeedTestSupport.Field,
                [1, 0, 0], AnnSeedTestSupport.Space(), 1),
            new PutDocument(AnnSeedTestSupport.Collection, SupplementaryId, EmptyJson,
                Access: new(AnnSeedTestSupport.Owner)),
            new PutVector(AnnSeedTestSupport.Collection, SupplementaryId, AnnSeedTestSupport.Field,
                [0, 1, 0], AnnSeedTestSupport.Space(), 1));
        var seed = AnnSeedTestSupport.Capture(database, "root");
        VectorRecord[] expectedRecords =
        [
            new(SupplementaryId, AnnSeedTestSupport.Field, AnnSeedTestSupport.Space(),
                ImmutableArray.Create(0f, 1f, 0f), 1),
            new(HighBmpId, AnnSeedTestSupport.Field, AnnSeedTestSupport.Space(),
                ImmutableArray.Create(1f, 0f, 0f), 1)
        ];
        var expected = AnnSeedDigestOracle.Compute(AnnSeedTestSupport.Field,
            AnnSeedTestSupport.Space(), expectedRecords);

        await Assert.That(seed.Records.Select(record => record.DocumentId).ToArray())
            .IsEquivalentTo([SupplementaryId, HighBmpId], CollectionOrdering.Matching);
        await Assert.That(seed.CorpusSha256).IsEqualTo(expected);
    }

    [Test]
    public async Task NonemptyCorpusDigestIncludesEveryFullVectorSpaceIdentityField()
    {
        using var database = AnnSeedTestSupport.Create(1);
        var alternateSpace = AnnSeedTestSupport.Space(id: "alternate-space", model: "alternate-model", version: "v2");
        database.Configure(OtherCollection, ResourceKind.Collection, fields:
            [new(AnnSeedTestSupport.Field, "embedding", AnnSeedTestSupport.FieldRead,
                AnnSeedTestSupport.FieldUse, AnnSeedTestSupport.FieldWrite)]);
        database.Commit(new PutDocument(OtherCollection, AnnSeedTestSupport.Id(0), EmptyJson,
            Access: new(AnnSeedTestSupport.Owner)), new PutVector(OtherCollection, AnnSeedTestSupport.Id(0),
            AnnSeedTestSupport.Field, [0.25f, 1f, -0.5f], alternateSpace, 1));

        var first = AnnSeedTestSupport.Capture(database, "root", AnnSeedTestSupport.Space());
        var second = AnnSeedCollector.Capture(database.Database, "root", database.Partition, OtherCollection,
            AnnSeedTestSupport.Field, alternateSpace, new(), new(database.Database.Limits));
        var firstExpected = new VectorRecord(AnnSeedTestSupport.Id(0), AnnSeedTestSupport.Field,
            AnnSeedTestSupport.Space(), ImmutableArray.Create(0.25f, 1f, -0.5f), 1);
        var secondExpected = firstExpected with { Space = alternateSpace };

        await Assert.That(first.Records).HasSingleItem();
        await Assert.That(second.Records).HasSingleItem();
        await Assert.That(first.CorpusSha256).IsEqualTo(AnnSeedDigestOracle.Compute(AnnSeedTestSupport.Field,
            AnnSeedTestSupport.Space(), [firstExpected]));
        await Assert.That(second.CorpusSha256).IsEqualTo(AnnSeedDigestOracle.Compute(AnnSeedTestSupport.Field,
            alternateSpace, [secondExpected]));
        await Assert.That(first.CorpusSha256).IsNotEqualTo(second.CorpusSha256);
    }

    [Test]
    public async Task PatchDeleteAndReinsertProduceFreshVisibleSeedAtMonotoneRevisions()
    {
        using var database = AnnSeedTestSupport.Create(1);
        var initial = AnnSeedTestSupport.Capture(database);
        await Assert.That(initial.Records.Single().DocumentRevision).IsEqualTo(1L);

        database.Commit(new PatchDocument(AnnSeedTestSupport.Collection, AnnSeedTestSupport.Id(0),
            [new("/state", PatchKind.Set, "\"patched\"")], 1));
        var stale = AnnSeedTestSupport.Capture(database);
        await Assert.That(stale.Records).IsEmpty();
        await Assert.That(stale.Cut.OutboxTail).IsGreaterThan(initial.Cut.OutboxTail);

        database.Commit(new DeleteDocument(AnnSeedTestSupport.Collection, AnnSeedTestSupport.Id(0), 2));
        var deleted = AnnSeedTestSupport.Capture(database);
        await Assert.That(deleted.Records).IsEmpty();
        await Assert.That(deleted.Cut.Position).IsGreaterThan(stale.Cut.Position);

        database.Commit(new PutDocument(AnnSeedTestSupport.Collection, AnnSeedTestSupport.Id(0), EmptyJson, 3,
            new(AnnSeedTestSupport.Owner), ExplicitReplacement: true));
        database.Commit(new PutVector(AnnSeedTestSupport.Collection, AnnSeedTestSupport.Id(0), AnnSeedTestSupport.Field,
            [4, 5, 6], AnnSeedTestSupport.Space(), 4));
        var recreated = AnnSeedTestSupport.Capture(database);

        await Assert.That(recreated.Records).HasSingleItem();
        await Assert.That(recreated.Records.Single().DocumentRevision).IsEqualTo(4L);
        await Assert.That(recreated.Cut.OutboxTail).IsGreaterThan(deleted.Cut.OutboxTail);
        await Assert.That(recreated.CorpusSha256).IsNotEqualTo(initial.CorpusSha256);
    }

    [Test]
    public async Task ValidDifferentVectorSpaceValuesDoNotContributeToCorpusDigest()
    {
        using var database = AnnSeedTestSupport.Create(1);
        database.Commit(new PutVector(AnnSeedTestSupport.Collection, AnnSeedTestSupport.Id(0),
            AnnSeedTestSupport.Field, [9, 8, 7], AnnSeedTestSupport.Space(version: "other-version"), 1));
        var firstExcludedSpace = AnnSeedTestSupport.Capture(database);
        database.Commit(new PutVector(AnnSeedTestSupport.Collection, AnnSeedTestSupport.Id(0),
            AnnSeedTestSupport.Field, [9, 8, 7], AnnSeedTestSupport.Space(version: "another-version"), 1));
        var secondExcludedSpace = AnnSeedTestSupport.Capture(database);

        await Assert.That(firstExcludedSpace.Records).IsEmpty();
        await Assert.That(secondExcludedSpace.Records).IsEmpty();
        await Assert.That(firstExcludedSpace.CorpusSha256).IsEqualTo(secondExcludedSpace.CorpusSha256);
    }
}
