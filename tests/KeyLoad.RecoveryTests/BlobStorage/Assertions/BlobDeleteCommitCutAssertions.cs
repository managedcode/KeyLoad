using ManagedCode.Communication.Commands;
using KeyLoad.Core;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.RecoveryHost.Features.BlobStorage;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.BlobStorage;

internal static class BlobDeleteCommitCutAssertions
{
    private const string HeadSpace = "blob-head-v1";
    private const string StateSpace = "blob-state-v1";
    private const string PartSpace = "blob-part-v1";
    private const string PartMetaSpace = "blob-partmeta-v1";
    private const string QuotaSpace = "blob-quota-v1";
    private const string GlobalSpace = "blob-global-v1";
    private const string GuidFormat = "N";

    internal static async Task PublishedAsync(DatabaseEngine owner, BlobCommitCutSeedProof proof,
        Command<DatabaseCommand> selected, bool deleted, bool reclaimed = false, int objects = 1)
    {
        var blob = BlobCommitCutProtocol.Blob;
        var upload = BlobCommitCutProtocol.OriginalUpload;
        byte[] bytes = [1, 2, 3, 4];
        var hash = BlobDeleteCommitCutOracle.Chain(proof.Incarnation, blob, upload, bytes);
        var metadata = deleted
            ? new BlobMetadata(blob, 2, null, 0, 0, null, new RowAccess(), new DateTimeOffset(selected.Timestamp), true)
            : new BlobMetadata(blob, 1, upload, 4, 1, hash, new RowAccess(), new DateTimeOffset(BlobDeleteCommitCutOracle.PriorComplete(proof).Timestamp));
        var operations = new BlobStorageOperations(owner);
        await BlobDeleteCommitCutOracle.ExactAsync(operations.Metadata(BlobCommitCutProtocol.Principal, new(blob)), metadata);
        await BlobDeleteCommitCutOracle.ExactAsync(owner.Store.Read(view => view.GetRecord<BlobHead>(Key(HeadSpace))),
            new BlobHead(1, proof.Incarnation, metadata));
        await RetainedAsync(owner, proof, hash, deleted, reclaimed);
        await QuotasAsync(owner, reclaimed ? 0 : 4, reclaimed ? 0 : 1, objects);
        if (deleted)
        {
            var missing = Assert.ThrowsExactly<KeyLoadException>(() => operations.Read(BlobCommitCutProtocol.Principal, new(blob, 2, 0, 4)));
            await Assert.That(missing.Code).IsEqualTo(ErrorCode.NotFound);
        }
        else
        {
            await BlobDeleteCommitCutOracle.ExactAsync(operations.Read(BlobCommitCutProtocol.Principal, new(blob, 1, 0, 4)), new BlobReadResult(metadata, 0, bytes));
        }
        await BlobCommitCutNativeProof.AssertAuthorityAsync((ZoneTreeStore)owner.Store, owner, proof);
    }

    private static async Task RetainedAsync(DatabaseEngine owner, BlobCommitCutSeedProof proof, string hash, bool retired, bool reclaimed)
    {
        var blob = BlobCommitCutProtocol.Blob;
        var begin = BlobDeleteCommitCutOracle.Begin(proof, BlobCommitCutProtocol.OriginalUpload);
        var expiry = new DateTimeOffset(begin.Timestamp).AddSeconds(3_600);
        var stateKey = Key(StateSpace, version: true);
        var rawKey = Key(PartSpace, version: true, part: true);
        var manifestKey = Key(PartMetaSpace, version: true, part: true);
        if (reclaimed)
        {
            await Assert.That(owner.Store.Read(view => view.ReadOwnedValue(stateKey))).IsNull();
            await Assert.That(owner.Store.Read(view => view.ReadOwnedValue(rawKey))).IsNull();
            await Assert.That(owner.Store.Read(view => view.ReadOwnedValue(manifestKey))).IsNull();
            return;
        }
        var expected = new BlobState(1, proof.Incarnation, proof.Incarnation, begin.CommandId, BlobCommitCutProtocol.Principal,
            blob, BlobCommitCutProtocol.OriginalUpload, new RowAccess(), 4, 0, expiry, 1, 4, hash, BlobUploadStatus.Complete, 0, 0, retired);
        await BlobDeleteCommitCutOracle.ExactAsync(owner.Store.Read(view => view.GetRecord<BlobState>(stateKey)), expected);
        await BlobDeleteCommitCutOracle.ExactAsync(new BlobStorageOperations(owner).UploadInfo(BlobCommitCutProtocol.Principal,
            new(blob, BlobCommitCutProtocol.OriginalUpload)), new BlobUploadInfo(blob, BlobCommitCutProtocol.OriginalUpload, 4, 0, 1, 4, expiry, BlobUploadStatus.Complete, hash));
        byte[] bytes = [1, 2, 3, 4];
        await Assert.That(owner.Store.Read(view => view.ReadOwnedValue(rawKey))!.AsSpan().SequenceEqual(bytes)).IsTrue();
        await BlobDeleteCommitCutOracle.ExactAsync(owner.Store.Read(view => view.GetRecord<BlobPartMetadata>(manifestKey)),
            new BlobPartMetadata(1, 4, BlobDeleteCommitCutOracle.Hash(bytes)));
    }

    internal static async Task RetainedAfterHealthyAsync(DatabaseEngine owner, BlobCommitCutSeedProof proof, Command<DatabaseCommand> selected)
    {
        var metadata = new BlobMetadata(BlobCommitCutProtocol.Blob, 2, null, 0, 0, null, new RowAccess(), new DateTimeOffset(selected.Timestamp), true);
        await BlobDeleteCommitCutOracle.ExactAsync(new BlobStorageOperations(owner).Metadata(BlobCommitCutProtocol.Principal,
            new(BlobCommitCutProtocol.Blob)), metadata);
        await BlobDeleteCommitCutOracle.ExactAsync(owner.Store.Read(view => view.GetRecord<BlobHead>(Key(HeadSpace))),
            new BlobHead(1, proof.Incarnation, metadata));
        await RetainedAsync(owner, proof, string.Empty, retired: true, reclaimed: true);
        var missing = Assert.ThrowsExactly<KeyLoadException>(() => new BlobStorageOperations(owner).Read(
            BlobCommitCutProtocol.Principal, new(BlobCommitCutProtocol.Blob, 2, 0, 4)));
        await Assert.That(missing.Code).IsEqualTo(ErrorCode.NotFound);
    }

    internal static async Task QuotasAsync(DatabaseEngine owner, long bytes, int versions, int objects = 1)
    {
        var blob = BlobCommitCutProtocol.Blob;
        var expected = new BlobQuota(1, owner.Store.Identity.Incarnation, bytes, objects, versions, 0);
        var key = KeyCodec.Encode(QuotaSpace, blob.Partition.TenantId, blob.Partition.DatabaseId, blob.Partition.TransactionDomainId, blob.Resource);
        await BlobDeleteCommitCutOracle.ExactAsync(owner.Store.Read(view => view.GetRecord<BlobQuota>(key)), expected);
        await BlobDeleteCommitCutOracle.ExactAsync(owner.Store.Read(view => view.GetRecord<BlobQuota>(KeyCodec.Encode(GlobalSpace))), expected);
    }

    internal static async Task DeniedAsync(DatabaseEngine owner)
    {
        var store = (ZoneTreeStore)owner.Store;
        var image = BlobCommitCutNativeProof.Image(store);
        var cut = store.Position;
        var operations = new BlobStorageOperations(owner);
        var metadata = Assert.ThrowsExactly<KeyLoadException>(() => operations.Metadata(BlobCommitCutProtocol.DeniedPrincipal, new(BlobCommitCutProtocol.Blob)));
        var range = Assert.ThrowsExactly<KeyLoadException>(() => operations.Read(BlobCommitCutProtocol.DeniedPrincipal, new(BlobCommitCutProtocol.Blob, 2, 0, 4)));
        await Assert.That(metadata.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(range.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(BlobCommitCutNativeProof.Image(store)).IsEqualTo(image);
        await Assert.That(store.Position).IsEqualTo(cut);
    }

    private static byte[] Key(string space, bool version = false, bool part = false)
    {
        var blob = BlobCommitCutProtocol.Blob;
        var upload = BlobCommitCutProtocol.OriginalUpload.ToString(GuidFormat);
        return part ? KeySpace.Partition(space, blob.Partition, blob.Resource, blob.Id, upload, 0L)
            : version ? KeySpace.Partition(space, blob.Partition, blob.Resource, blob.Id, upload)
            : KeySpace.Partition(space, blob.Partition, blob.Resource, blob.Id);
    }
}
