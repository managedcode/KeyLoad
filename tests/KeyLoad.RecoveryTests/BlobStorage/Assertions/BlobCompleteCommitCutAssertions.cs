using KeyLoad.Core;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication.Commands;

namespace KeyLoad.RecoveryTests.Features.BlobStorage;

internal static class BlobCompleteCommitCutAssertions
{
    private const string HeadSpace = "blob-head-v1";
    private const string StateSpace = "blob-state-v1";
    private const string PartSpace = "blob-part-v1";
    private const string PartMetaSpace = "blob-partmeta-v1";
    private const string QuotaSpace = "blob-quota-v1";
    private const string GlobalSpace = "blob-global-v1";
    private const string GuidFormat = "N";
    private const int Format = 1;
    private const int OriginalLength = 4;
    private const long TotalBytes = 65_557;

    internal static async Task PublishedAsync(DatabaseEngine owner, BlobCommitCutSeedProof proof,
        Command<DatabaseCommand> selected, bool committed, bool healthy = false)
    {
        var blob = BlobCommitCutProtocol.Blob;
        var oldBytes = BlobCompleteCommitCutOracle.OriginalBytes();
        var newBytes = BlobCompleteCommitCutOracle.MultipartPayload();
        var oldHash = BlobCompleteCommitCutOracle.Chain(proof.Incarnation, blob, BlobCommitCutProtocol.OriginalUpload, oldBytes);
        var newHash = BlobCompleteCommitCutOracle.Chain(proof.Incarnation, blob, BlobCommitCutProtocol.NewUpload, newBytes);
        var prior = new BlobMetadata(blob, 1, BlobCommitCutProtocol.OriginalUpload, OriginalLength, 1,
            oldHash, new RowAccess(), new DateTimeOffset(BlobCompleteCommitCutOracle.PriorComplete(proof).Timestamp));
        var current = new BlobMetadata(blob, 2, BlobCommitCutProtocol.NewUpload, newBytes.Length, 2,
            newHash, new RowAccess(), new DateTimeOffset(selected.Timestamp));
        var expected = committed ? current : prior;
        var operations = new BlobStorageOperations(owner);
        await BlobCompleteCommitCutOracle.ExactAsync(operations.Metadata(BlobCommitCutProtocol.Principal, new(blob)), expected);
        await BlobCompleteCommitCutOracle.ExactAsync(owner.Store.Read(view => view.GetRecord<BlobHead>(
            KeySpace.Partition(HeadSpace, blob.Partition, blob.Resource, blob.Id))), new BlobHead(Format, proof.Incarnation, expected));
        await StateAsync(owner, proof, BlobCommitCutProtocol.OriginalUpload, oldBytes, oldHash, BlobUploadStatus.Complete, committed);
        await StateAsync(owner, proof, BlobCommitCutProtocol.NewUpload, newBytes, newHash,
            committed ? BlobUploadStatus.Complete : BlobUploadStatus.Active, retired: false);
        await PartsAsync(owner, blob, BlobCommitCutProtocol.OriginalUpload, oldBytes);
        await PartsAsync(owner, blob, BlobCommitCutProtocol.NewUpload, newBytes);
        await QuotasAsync(owner, TotalBytes + (healthy ? OriginalLength : 0), healthy ? 2 : 1, healthy ? 3 : 2, committed ? 0 : 1);
        await RangeAsync(operations, expected, committed ? newBytes : oldBytes);
        await BlobCommitCutNativeProof.AssertAuthorityAsync((ZoneTreeStore)owner.Store, proof);
    }

    private static async Task StateAsync(DatabaseEngine owner, BlobCommitCutSeedProof proof, Guid upload,
        byte[] bytes, string hash, BlobUploadStatus status, bool retired)
    {
        var blob = BlobCommitCutProtocol.Blob;
        var begin = BlobCompleteCommitCutOracle.Begin(proof, upload);
        var revision = upload == BlobCommitCutProtocol.OriginalUpload ? 0 : 1;
        var parts = upload == BlobCommitCutProtocol.OriginalUpload ? 1 : 2;
        var expiry = new DateTimeOffset(begin.Timestamp).AddSeconds(BlobCompleteCommitCutOracle.UploadLifetimeSeconds);
        var state = new BlobState(Format, proof.Incarnation, proof.Incarnation, begin.CommandId,
            BlobCommitCutProtocol.Principal, blob, upload, new RowAccess(), bytes.Length, revision, expiry,
            parts, bytes.Length, hash, status, 0, 0, retired);
        await BlobCompleteCommitCutOracle.ExactAsync(owner.Store.Read(view => view.GetRecord<BlobState>(
            KeySpace.Partition(StateSpace, blob.Partition, blob.Resource, blob.Id, upload.ToString(GuidFormat)))), state);
        await BlobCompleteCommitCutOracle.ExactAsync(new BlobStorageOperations(owner).UploadInfo(
            BlobCommitCutProtocol.Principal, new(blob, upload)),
            new BlobUploadInfo(blob, upload, bytes.Length, revision, parts, bytes.Length, expiry, status, hash));
    }

    private static async Task PartsAsync(DatabaseEngine owner, BlobRef blob, Guid upload, byte[] bytes)
    {
        for (var offset = 0; offset < bytes.Length; offset += BlobCompleteCommitCutOracle.ChunkBytes)
        {
            var ordinal = offset / BlobCompleteCommitCutOracle.ChunkBytes;
            var part = bytes.AsMemory(offset, Math.Min(BlobCompleteCommitCutOracle.ChunkBytes, bytes.Length - offset));
            var key = KeySpace.Partition(PartSpace, blob.Partition, blob.Resource, blob.Id, upload.ToString(GuidFormat), (long)ordinal);
            await Assert.That(owner.Store.Read(view => view.ReadOwnedValue(key))!.AsSpan().SequenceEqual(part.Span)).IsTrue();
            var metadata = KeySpace.Partition(PartMetaSpace, blob.Partition, blob.Resource, blob.Id, upload.ToString(GuidFormat), (long)ordinal);
            await BlobCompleteCommitCutOracle.ExactAsync(owner.Store.Read(view => view.GetRecord<BlobPartMetadata>(metadata)),
                new BlobPartMetadata(Format, part.Length, BlobCompleteCommitCutOracle.Hash(part.Span)));
        }
    }

    private static async Task RangeAsync(BlobStorageOperations operations, BlobMetadata metadata, byte[] bytes)
    {
        var blob = metadata.Blob;
        var first = Math.Min(bytes.Length, BlobCompleteCommitCutOracle.ChunkBytes);
        await BlobCompleteCommitCutOracle.ExactAsync(operations.Read(BlobCommitCutProtocol.Principal, new(blob, metadata.Revision, 0, first)),
            new BlobReadResult(metadata, 0, bytes.AsMemory(0, first)));
        if (bytes.Length > first)
        {
            await BlobCompleteCommitCutOracle.ExactAsync(operations.Read(BlobCommitCutProtocol.Principal,
                new(blob, metadata.Revision, first, BlobCompleteCommitCutOracle.TailBytes)),
                new BlobReadResult(metadata, first, bytes.AsMemory(first)));
            await BlobCompleteCommitCutOracle.ExactAsync(operations.Read(BlobCommitCutProtocol.Principal,
                new(blob, metadata.Revision, 65_520, 33)), new BlobReadResult(metadata, 65_520, bytes.AsMemory(65_520, 33)));
        }
        await BlobCompleteCommitCutOracle.ExactAsync(operations.Read(BlobCommitCutProtocol.Principal,
            new(blob, metadata.Revision, bytes.Length, 0)), new BlobReadResult(metadata, bytes.Length, ReadOnlyMemory<byte>.Empty));
    }

    internal static async Task QuotasAsync(DatabaseEngine owner, long bytes, int objects, int versions, int uploads)
    {
        var blob = BlobCommitCutProtocol.Blob;
        var expected = new BlobQuota(Format, owner.Store.Identity.Incarnation, bytes, objects, versions, uploads);
        var resource = KeyCodec.Encode(QuotaSpace, blob.Partition.TenantId, blob.Partition.DatabaseId,
            blob.Partition.TransactionDomainId, blob.Resource);
        await BlobCompleteCommitCutOracle.ExactAsync(owner.Store.Read(view => view.GetRecord<BlobQuota>(resource)), expected);
        await BlobCompleteCommitCutOracle.ExactAsync(owner.Store.Read(view => view.GetRecord<BlobQuota>(KeyCodec.Encode(GlobalSpace))), expected);
    }

    internal static async Task DeniedAsync(DatabaseEngine owner, Command<DatabaseCommand> selected)
    {
        var store = (ZoneTreeStore)owner.Store;
        var image = BlobCommitCutNativeProof.Image(store);
        var cut = store.Position;
        var operations = new BlobStorageOperations(owner);
        var metadata = Assert.ThrowsExactly<KeyLoadException>(() => operations.Metadata(BlobCommitCutProtocol.DeniedPrincipal, new(BlobCommitCutProtocol.Blob)));
        var range = Assert.ThrowsExactly<KeyLoadException>(() => operations.Read(BlobCommitCutProtocol.DeniedPrincipal, new(BlobCommitCutProtocol.Blob, 2, 0, 4)));
        var denied = owner.CreateCommand(selected.Value!.Kind, selected.CommandId, BlobCommitCutProtocol.DeniedPrincipal,
            new DateTimeOffset(selected.Timestamp), NativeSerialization.Serialize(NativeSerialization.Deserialize<CompleteBlobUploadRequest>((selected).Value!.Input.Span)));
        var outcome = owner.ResolveOutcome(denied);
        await Assert.That(Errors.Code(outcome.Problem)).IsEqualTo(ErrorCode.PermissionDenied);

        await Assert.That(metadata.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(range.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(BlobCommitCutNativeProof.Image(store)).IsEqualTo(image);
        await Assert.That(store.Position).IsEqualTo(cut);
    }
}
