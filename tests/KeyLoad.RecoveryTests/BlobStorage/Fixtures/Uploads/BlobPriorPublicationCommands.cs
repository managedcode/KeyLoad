using KeyLoad.Core;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.CrashHost;

namespace KeyLoad.RecoveryTests.Features.BlobStorage;

internal static class BlobPriorPublicationCommands
{
    internal static BlobPriorPublicationCommand Send<T>(DatabaseEngine owner, OperationKind kind, Guid id, T payload)
    {
        var operation = CrashDatabase.Operation(owner, kind, payload, id);
        return new(operation, owner.Apply(operation));
    }

    internal static async Task<BlobPriorPublicationState> SeedAsync(string root)
    {
        using var store = BlobPriorPublicationTrial.Open(root);
        var owner = CrashDatabase.Create(store, tenantId: QueueLifecycleCrashProtocol.Partition.TenantId);
        var definition = new ResourceDefinition("prior-publication-files", ResourceKind.BlobStore,
            QueueLifecycleCrashProtocol.Partition.TransactionDomainId);
        var configured = Send(owner, OperationKind.ConfigureResource, Guid.NewGuid(), new ConfigureResourceRequest(
            QueueLifecycleCrashProtocol.Partition.TenantId, QueueLifecycleCrashProtocol.Partition.DatabaseId, definition));
        await BlobPriorPublicationImage.ExactAsync(configured.Result.Get<ResourceDefinition>(), definition);
        var blob = new BlobRef(QueueLifecycleCrashProtocol.Partition, definition.Name, "published");
        var principal = store.Read(view => view.ReadOwnedValue(KeySpace.Principal("root")))!;
        var rootPrincipal = NativeSerialization.Deserialize<PrincipalRecord>(principal);
        await Assert.That(rootPrincipal.TenantId).IsEqualTo("tenant");
        await Assert.That(rootPrincipal.ClusterAdministrator).IsTrue();
        return await PublishAsync(owner, blob, Guid.NewGuid(), [1, 2, 3, 4], 0,
            NativeSerialization.Serialize(store.Identity), principal);
    }

    internal static async Task<BlobPriorPublicationState> PublishAsync(DatabaseEngine owner, BlobRef blob, Guid upload,
        byte[] bytes, long revision, byte[] identity, byte[] principal)
    {
        var begin = new BeginBlobUploadRequest(Guid.NewGuid(), blob, upload, 4, revision);
        var begun = Send(owner, OperationKind.BeginBlobUpload, begin.CommandId, begin);
        var initial = BlobPriorPublicationOracle.Initial(owner.Store.Identity.Incarnation, blob, upload);
        var info = new BlobUploadInfo(blob, upload, 4, revision, 0, 0,
            new DateTimeOffset(begun.Operation.Timestamp).AddSeconds(3_600), BlobUploadStatus.Active, initial);
        await BlobPriorPublicationImage.ExactAsync(begun.Result.Get<BlobCommitResult<BlobUploadInfo>>().Value, info);
        await BlobPriorPublicationImage.ReceiptAsync(owner, begun.Result.Get<BlobCommitResult<BlobUploadInfo>>().Receipt, begin.CommandId, blob);
        var part = new WriteBlobPartRequest(Guid.NewGuid(), blob, upload, 0, bytes, BlobPriorPublicationOracle.Hash(bytes));
        var written = Send(owner, OperationKind.WriteBlobPart, part.CommandId, part);
        var hash = BlobPriorPublicationOracle.Complete(initial, bytes);
        await BlobPriorPublicationImage.ExactAsync(written.Result.Get<BlobCommitResult<BlobUploadInfo>>().Value,
            info with
            {
                NextOrdinal = 1,
                StoredBytes = 4,
                IntegrityHash = hash
            });
        await BlobPriorPublicationImage.ReceiptAsync(owner, written.Result.Get<BlobCommitResult<BlobUploadInfo>>().Receipt, part.CommandId, blob);
        var complete = new CompleteBlobUploadRequest(Guid.NewGuid(), blob, upload, hash);
        var completed = Send(owner, OperationKind.CompleteBlobUpload, complete.CommandId, complete);
        var metadata = new BlobMetadata(blob, revision + 1, upload, 4, 1, hash, new RowAccess(), new DateTimeOffset(completed.Operation.Timestamp));
        await BlobPriorPublicationImage.ExactAsync(completed.Result.Get<BlobCommitResult<BlobMetadata>>().Value, metadata);
        await BlobPriorPublicationImage.ReceiptAsync(owner, completed.Result.Get<BlobCommitResult<BlobMetadata>>().Receipt, complete.CommandId, blob);
        var incarnation = owner.Store.Identity.Incarnation;
        var state = new BlobState(1, incarnation, incarnation, begin.CommandId, "root", blob, upload, new RowAccess(),
            4, revision, info.ExpiresAt, 1, 4, hash, BlobUploadStatus.Complete, 0, 0);
        var result = new BlobPriorPublicationState(blob, upload, bytes, metadata, state,
            [begun, written, completed], identity, principal);
        await BlobPriorPublicationAssertions.HealthyAsync(owner, result);
        return result;
    }
}
