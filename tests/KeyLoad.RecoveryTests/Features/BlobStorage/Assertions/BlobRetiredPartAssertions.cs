using KeyLoad.Core;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Storage;

namespace KeyLoad.RecoveryTests.Features.BlobStorage;

internal static class BlobRetiredPartAssertions
{
    internal static async Task HealthyAsync(DatabaseEngine owner, BlobRetiredPartState seed)
    {
        await TombstoneAsync(owner, seed);
        var publication = seed.Publication;
        await BlobPriorPublicationImage.ExactAsync(owner.Store.Read(view => view.GetRecord<BlobState>(
            BlobPriorPublicationOracle.Key("blob-state-v1", publication))), publication.State with { Retired = true });
        await BlobPriorPublicationImage.ExactAsync(new BlobStorageOperations(owner).UploadInfo("root",
            new(publication.Blob, publication.Upload)), new BlobUploadInfo(publication.Blob, publication.Upload,
            4, 0, 1, 4, publication.State.ExpiresAt, BlobUploadStatus.Complete, publication.Metadata.IntegrityHash!));
        await Assert.That(owner.Store.Read(view => view.ReadOwnedValue(BlobPriorPublicationOracle.Key("blob-part-v1", publication)))!
            .AsSpan().SequenceEqual(publication.Bytes)).IsTrue();
        await BlobPriorPublicationImage.ExactAsync(owner.Store.Read(view => view.GetRecord<BlobPartMetadata>(
            BlobPriorPublicationOracle.Key("blob-partmeta-v1", publication))),
            new BlobPartMetadata(1, 4, BlobPriorPublicationOracle.Hash(publication.Bytes)));
        await BlobPriorPublicationAssertions.QuotasAsync(owner, publication, 4, 1);
        foreach (var command in publication.Commands)
        { await BlobPriorPublicationImage.ReplayAsync(owner, command); }
        await BlobPriorPublicationImage.ReplayAsync(owner, seed.Deletion);
    }

    internal static async Task TombstoneAsync(DatabaseEngine owner, BlobRetiredPartState seed)
    {
        await BlobPriorPublicationAssertions.AuthorityAsync(owner, seed.Publication);
        var operations = new BlobStorageOperations(owner);
        await BlobPriorPublicationImage.ExactAsync(operations.Metadata("root", new(seed.Publication.Blob)), seed.Tombstone);
        await BlobPriorPublicationImage.ExactAsync(owner.Store.Read(view => view.GetRecord<BlobHead>(
            BlobPriorPublicationOracle.Key("blob-head-v1", seed.Publication))),
            new BlobHead(1, owner.Store.Identity.Incarnation, seed.Tombstone));
        var error = Assert.ThrowsExactly<KeyLoadException>(() => operations.Read("root", new(seed.Publication.Blob, 2, 0, 4)));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.NotFound);
        await Assert.That(error.Message).IsEqualTo("The published blob is unavailable.");
    }
}
