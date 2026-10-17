using KeyLoad.Core;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Storage;

namespace KeyLoad.RecoveryTests.Features.BlobStorage;

internal static class BlobPriorPublicationAssertions
{
    internal static async Task AuthorityAsync(DatabaseEngine owner, BlobPriorPublicationState seed)
    {
        await Assert.That(NativeSerialization.Serialize(owner.Store.Identity).AsSpan().SequenceEqual(seed.Identity)).IsTrue();
        await Assert.That(NativeSerialization.Serialize(owner.ReadPhysicalShardCatalog("root")).AsSpan().SequenceEqual(seed.Catalog)).IsTrue();
        await Assert.That(owner.Store.Read(view => view.ReadOwnedValue(KeySpace.Principal("root")))!.AsSpan().SequenceEqual(seed.Principal)).IsTrue();
    }

    internal static async Task HealthyAsync(DatabaseEngine owner, BlobPriorPublicationState seed)
    {
        await AuthorityAsync(owner, seed);
        var operations = new BlobStorageOperations(owner);
        await BlobPriorPublicationImage.ExactAsync(operations.Metadata("root", new(seed.Blob)), seed.Metadata);
        await BlobPriorPublicationImage.ExactAsync(operations.Read("root", new(seed.Blob, seed.Metadata.Revision, 0, 4)),
            new BlobReadResult(seed.Metadata, 0, seed.Bytes));
        await BlobPriorPublicationImage.ExactAsync(operations.Read("root", new(seed.Blob, seed.Metadata.Revision, 1, 2)),
            new BlobReadResult(seed.Metadata, 1, seed.Bytes.AsMemory(1, 2)));
        await BlobPriorPublicationImage.ExactAsync(operations.Read("root", new(seed.Blob, seed.Metadata.Revision, 4, 0)),
            new BlobReadResult(seed.Metadata, 4, ReadOnlyMemory<byte>.Empty));
        await BlobPriorPublicationImage.ExactAsync(operations.UploadInfo("root", new(seed.Blob, seed.Upload)),
            new BlobUploadInfo(seed.Blob, seed.Upload, 4, seed.State.ExpectedRevision, 1, 4,
                seed.State.ExpiresAt, BlobUploadStatus.Complete, seed.Metadata.IntegrityHash!));
        await BlobPriorPublicationImage.ExactAsync(owner.Store.Read(view => view.GetRecord<BlobState>(
            BlobPriorPublicationOracle.Key("blob-state-v1", seed))), seed.State);
        await BlobPriorPublicationImage.ExactAsync(owner.Store.Read(view => view.GetRecord<BlobHead>(
            BlobPriorPublicationOracle.Key("blob-head-v1", seed))), new BlobHead(1, owner.Store.Identity.Incarnation, seed.Metadata));
        await Assert.That(owner.Store.Read(view => view.ReadOwnedValue(BlobPriorPublicationOracle.Key("blob-part-v1", seed)))!
            .AsSpan().SequenceEqual(seed.Bytes)).IsTrue();
        await BlobPriorPublicationImage.ExactAsync(owner.Store.Read(view => view.GetRecord<BlobPartMetadata>(
            BlobPriorPublicationOracle.Key("blob-partmeta-v1", seed))), new BlobPartMetadata(1, 4, BlobPriorPublicationOracle.Hash(seed.Bytes)));
        await QuotasAsync(owner, seed, 4, 1);
        foreach (var command in seed.Commands)
        {
            await BlobPriorPublicationImage.ReplayAsync(owner, command);
        }
    }

    internal static async Task QuotasAsync(DatabaseEngine owner, BlobPriorPublicationState seed, long bytes, int versions)
    {
        var blob = seed.Blob;
        var expected = new BlobQuota(1, owner.Store.Identity.Incarnation, bytes, 1, versions, 0);
        var resource = KeyCodec.Encode("blob-quota-v1", blob.Partition.TenantId, blob.Partition.DatabaseId,
            blob.Partition.TransactionDomainId, blob.Resource);
        await BlobPriorPublicationImage.ExactAsync(owner.Store.Read(view => view.GetRecord<BlobQuota>(resource)), expected);
        await BlobPriorPublicationImage.ExactAsync(owner.Store.Read(view => view.GetRecord<BlobQuota>(KeyCodec.Encode("blob-global-v1"))), expected);
    }
}
