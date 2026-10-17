using KeyLoad.Core;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.BlobStorage;

internal static class BlobWholeFlowAssertions
{
    internal static async Task Exact<T>(T actual, T expected)
        => await Assert.That(NativeSerialization.Serialize(actual).SequenceEqual(NativeSerialization.Serialize(expected))).IsTrue();

    internal static async Task Quotas(DatabaseEngine database, BlobRef blob, long bytes, int versions, int uploads)
    {
        var expected = new BlobQuota(BlobKeys.FormatVersion, database.Store.Identity.Incarnation,
            bytes, BlobReclaimWholeFlowFixture.SingleCount, versions, uploads);
        var resource = database.Store.Read(view => BlobRecordReader.Get<BlobQuota>(view, BlobKeys.Quota(blob)));
        var global = database.Store.Read(view => BlobRecordReader.Get<BlobQuota>(view, BlobKeys.Global));
        await Exact(resource, expected);
        await Exact(global, expected);
    }

    internal static async Task Receipt(DatabaseEngine database, CommitReceipt receipt, Guid command, BlobRef blob)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(command);
        await Assert.That(receipt.Token.Position).IsEqualTo(database.Store.Position);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(blob.Partition.AtomicPartitionId);
        await Assert.That(receipt.Durability).IsEqualTo(database.Store.Identity.Durability);
        await Assert.That(receipt.Mutations).IsEmpty();
    }

    internal static async Task Unchanged(ZoneTreeStore store, long position, string[] image)
    {
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(store)).IsEquivalentTo(image, CollectionOrdering.Matching);
    }

    internal static async Task Parts(DatabaseEngine database, BlobReclaimWholeFlowFixture fixture, bool firstPresent, bool secondPresent)
    {
        await Part(database, fixture, BlobReclaimWholeFlowFixture.FirstOrdinal, firstPresent);
        await Part(database, fixture, BlobReclaimWholeFlowFixture.SecondOrdinal, secondPresent);
    }

    private static async Task Part(DatabaseEngine database, BlobReclaimWholeFlowFixture fixture, int ordinal, bool present)
    {
        var raw = database.Store.Read(view => view.ReadOwnedValue(BlobKeys.Part(fixture.Blob, fixture.UploadId, ordinal)));
        var meta = database.Store.Read(view => BlobRecordReader.Get<BlobPartMetadata>(view,
            BlobKeys.PartMeta(fixture.Blob, fixture.UploadId, ordinal)));
        if (!present)
        {
            await Assert.That(raw).IsNull();
            await Assert.That(meta).IsNull();
            return;
        }
        var expected = BlobReclaimWholeFlowFixture.Payload.AsMemory(ordinal * BlobLimits.RawPartBytes, BlobLimits.RawPartBytes);
        await Assert.That(raw!.AsSpan().SequenceEqual(expected.Span)).IsTrue();
        await Exact(meta, new BlobPartMetadata(BlobKeys.FormatVersion, expected.Length, BlobIntegrity.PartHash(expected.Span)));
    }
}
