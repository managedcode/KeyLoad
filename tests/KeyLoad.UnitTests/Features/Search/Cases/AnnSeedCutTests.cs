using System.Collections.Immutable;
using KeyLoad.Core;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class AnnSeedCutTests
{
    private const string HighBmpId = "\uE000";
    private const string SupplementaryId = "\U00010000";
    private const string EmptyObjectJson = "{}";

    [Test]
    public async Task CutMatchesOnePersistedStoreReadAndUnchangedCaptureFingerprintIsStable()
    {
        using var database = AnnSeedTestSupport.Create(2);
        var seed = AnnSeedTestSupport.Capture(database);
        var expected = database.Store.Read(view =>
        {
            var identity = database.Store.Identity;
            var head = database.Database.ReadOutboxHead(view, database.Partition);
            var applied = view.ReadOwnedValue(KeySpace.Applied.ToArray()) is { } bytes
                ? NativeSerialization.Deserialize<long>(bytes) : 0;
            return (identity.NodeId, identity.Incarnation, identity.FormatVersion, identity.KeyCodecVersion,
                identity.ReadGeneration, database.Store.Position, applied, head.Tail, head.FirstAvailable);
        });
        var repeated = AnnSeedTestSupport.Capture(database);

        await Assert.That(seed.Cut.NodeId).IsEqualTo(expected.NodeId);
        await Assert.That(seed.Cut.Incarnation).IsEqualTo(expected.Incarnation);
        await Assert.That(seed.Cut.StoreFormatVersion).IsEqualTo(expected.FormatVersion);
        await Assert.That(seed.Cut.KeyCodecVersion).IsEqualTo(expected.KeyCodecVersion);
        await Assert.That(seed.Cut.ReadGeneration).IsEqualTo(expected.ReadGeneration);
        await Assert.That(seed.Cut.Position).IsEqualTo(expected.Position);
        await Assert.That(seed.Cut.AppliedPosition).IsEqualTo(expected.applied);
        await Assert.That(seed.Cut.OutboxTail).IsEqualTo(expected.Tail);
        await Assert.That(seed.Cut.OutboxFirstAvailable).IsEqualTo(expected.FirstAvailable);
        await Assert.That(seed.Scope.SchemaVersion).IsEqualTo(1L);
        await Assert.That(seed.CorpusSha256).IsEqualTo(repeated.CorpusSha256);
    }

    [Test]
    public async Task SameRevisionVectorBitChangeAdvancesCutAndChangesExactCorpusDigest()
    {
        using var database = AnnSeedTestSupport.Create(1);
        AnnSeedTestSupport.CommitVector(database, AnnSeedTestSupport.Id(0),
            ImmutableArray.Create(0f, 1f, 2f));
        var positiveZero = AnnSeedTestSupport.Capture(database);
        AnnSeedTestSupport.CommitVector(database, AnnSeedTestSupport.Id(0),
            ImmutableArray.Create(-0f, 1f, 2f));
        var negativeZero = AnnSeedTestSupport.Capture(database);

        await Assert.That(negativeZero.Records.Single().DocumentRevision)
            .IsEqualTo(positiveZero.Records.Single().DocumentRevision);
        await Assert.That(BitConverter.SingleToInt32Bits(positiveZero.Records.Single().Values[0]))
            .IsEqualTo(BitConverter.SingleToInt32Bits(0f));
        await Assert.That(BitConverter.SingleToInt32Bits(negativeZero.Records.Single().Values[0]))
            .IsEqualTo(BitConverter.SingleToInt32Bits(-0f));
        await Assert.That(negativeZero.Cut.Position).IsGreaterThan(positiveZero.Cut.Position);
        await Assert.That(negativeZero.Cut.OutboxTail).IsGreaterThan(positiveZero.Cut.OutboxTail);
        await Assert.That(negativeZero.CorpusSha256).IsNotEqualTo(positiveZero.CorpusSha256);
    }

    [Test]
    public async Task ReturnedRecordsUseOrdinalIdentityOrderIndependentOfStorageKeyOrder()
    {
        using var database = AnnSeedTestSupport.Create(0);
        database.Commit(
            new PutDocument(AnnSeedTestSupport.Collection, HighBmpId, EmptyObjectJson, Access: new(AnnSeedTestSupport.Owner)),
            new PutVector(AnnSeedTestSupport.Collection, HighBmpId, AnnSeedTestSupport.Field, [1, 2, 3], AnnSeedTestSupport.Space(), 1),
            new PutDocument(AnnSeedTestSupport.Collection, SupplementaryId, EmptyObjectJson, Access: new(AnnSeedTestSupport.Owner)),
            new PutVector(AnnSeedTestSupport.Collection, SupplementaryId, AnnSeedTestSupport.Field, [3, 2, 1], AnnSeedTestSupport.Space(), 1));

        var storageOrder = database.Store.Read(view =>
        {
            var ids = new List<string>();
            view.VisitRange(KeySpace.Partition("vector", database.Partition, AnnSeedTestSupport.Collection,
                AnnSeedTestSupport.Field), 4, (key, value) =>
            {
                ids.Add(NativeSerialization.Deserialize<VectorRecord>(value).DocumentId);
                return true;
            });
            return ids.ToArray();
        });
        var seed = AnnSeedTestSupport.Capture(database);
        var ordinalOrder = seed.Records.Select(record => record.DocumentId).ToArray();

        await Assert.That(storageOrder).IsNotEquivalentTo(ordinalOrder, CollectionOrdering.Matching);
        await Assert.That(ordinalOrder).IsEquivalentTo([SupplementaryId, HighBmpId], CollectionOrdering.Matching);
    }
}
