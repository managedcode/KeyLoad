using KeyLoad.Core;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Storage;

namespace KeyLoad.RecoveryTests.Features.BlobStorage;

internal static class BlobLaterPartAssertions
{
    internal static async Task PublishedAsync(DatabaseEngine owner, BlobPriorPublicationState seed)
    {
        await BlobPriorPublicationAssertions.AuthorityAsync(owner, seed);
        var operations = new BlobStorageOperations(owner);
        await BlobPriorPublicationImage.ExactAsync(operations.Metadata("root", new(seed.Blob)), seed.Metadata);
        await RangeAsync(operations, seed, 0, 65_536);
        await RangeAsync(operations, seed, 65_536, 17);
        await RangeAsync(operations, seed, 65_520, 33);
        await RangeAsync(operations, seed, 65_553, 0);
        await RecordsAsync(owner, seed, retired: false);
        foreach (var command in seed.Commands)
        { await BlobPriorPublicationImage.ReplayAsync(owner, command); }
    }

    internal static async Task RetiredAsync(DatabaseEngine owner, BlobRetiredPartState seed)
    {
        await BlobRetiredPartAssertions.TombstoneAsync(owner, seed);
        await RecordsAsync(owner, seed.Publication, retired: true);
        foreach (var command in seed.Publication.Commands)
        { await BlobPriorPublicationImage.ReplayAsync(owner, command); }
        await BlobPriorPublicationImage.ReplayAsync(owner, seed.Deletion);
    }

    private static async Task RangeAsync(BlobStorageOperations operations, BlobPriorPublicationState seed, long offset, int length)
        => await BlobPriorPublicationImage.ExactAsync(operations.Read("root", new(seed.Blob, 1, offset, length)),
            new BlobReadResult(seed.Metadata, offset, seed.Bytes.AsMemory((int)offset, length)));

    private static async Task RecordsAsync(DatabaseEngine owner, BlobPriorPublicationState seed, bool retired)
    {
        await BlobPriorPublicationImage.ExactAsync(owner.Store.Read(view => view.GetRecord<BlobState>(
            BlobPriorPublicationOracle.Key("blob-state-v1", seed))), seed.State with { Retired = retired });
        await BlobPriorPublicationImage.ExactAsync(new BlobStorageOperations(owner).UploadInfo("root", new(seed.Blob, seed.Upload)),
            new BlobUploadInfo(seed.Blob, seed.Upload, 65_553, 0, 2, 65_553,
                seed.State.ExpiresAt, BlobUploadStatus.Complete, seed.Metadata.IntegrityHash!));
        if (!retired)
        {
            await BlobPriorPublicationImage.ExactAsync(owner.Store.Read(view => view.GetRecord<BlobHead>(
            BlobPriorPublicationOracle.Key("blob-head-v1", seed))), new BlobHead(1, owner.Store.Identity.Incarnation, seed.Metadata));
        }
        for (var ordinal = 0; ordinal < 2; ordinal++)
        {
            var bytes = seed.Bytes.AsMemory(ordinal * 65_536, ordinal == 0 ? 65_536 : 17);
            await Assert.That(owner.Store.Read(view => view.ReadOwnedValue(BlobLaterPartOracle.Key("blob-part-v1", seed, ordinal)))!
                .AsSpan().SequenceEqual(bytes.Span)).IsTrue();
            await BlobPriorPublicationImage.ExactAsync(owner.Store.Read(view => view.GetRecord<BlobPartMetadata>(
                BlobLaterPartOracle.Key("blob-partmeta-v1", seed, ordinal))),
                new BlobPartMetadata(1, bytes.Length, BlobPriorPublicationOracle.Hash(bytes.Span)));
        }
        await BlobPriorPublicationAssertions.QuotasAsync(owner, seed, 65_553, 1);
    }
}
