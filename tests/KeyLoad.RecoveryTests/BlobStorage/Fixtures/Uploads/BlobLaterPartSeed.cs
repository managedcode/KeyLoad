using KeyLoad.Core;

namespace KeyLoad.RecoveryTests.Features.BlobStorage;

internal static class BlobLaterPartSeed
{
    internal static async Task<BlobRetiredPartState> CreateAsync(string root)
    {
        using var store = BlobPriorPublicationTrial.Open(root);
        var owner = CrashDatabase.Create(store, tenantId: QueueLifecycleCrashProtocol.Partition.TenantId);
        var definition = new ResourceDefinition("later-part-files", ResourceKind.BlobStore,
            QueueLifecycleCrashProtocol.Partition.TransactionDomainId);
        var configured = BlobPriorPublicationCommands.Send(owner, OperationKind.ConfigureResource, Guid.NewGuid(),
            new ConfigureResourceRequest(QueueLifecycleCrashProtocol.Partition.TenantId,
                QueueLifecycleCrashProtocol.Partition.DatabaseId, definition));
        await BlobPriorPublicationImage.ExactAsync(configured.Result.Get<ResourceDefinition>(), definition);
        var principal = store.Read(view => view.ReadOwnedValue(KeySpace.Principal("root")))!;
        var rootPrincipal = NativeSerialization.Deserialize<PrincipalRecord>(principal);
        await Assert.That(rootPrincipal.TenantId).IsEqualTo("tenant");
        await Assert.That(rootPrincipal.ClusterAdministrator).IsTrue();
        var blob = new BlobRef(QueueLifecycleCrashProtocol.Partition, definition.Name, "published-two-parts");
        var publication = await BlobLaterPartPublication.PublishAsync(owner, blob, Guid.NewGuid(),
            NativeSerialization.Serialize(store.Identity), principal);
        var request = new DeleteBlobRequest(Guid.NewGuid(), publication.Blob, 1);
        var deletion = BlobPriorPublicationCommands.Send(owner, OperationKind.DeleteBlob, request.CommandId, request);
        var tombstone = publication.Metadata with
        {
            Revision = 2,
            VersionId = null,
            Length = 0,
            PartCount = 0,
            IntegrityHash = null,
            UpdatedAt = new DateTimeOffset(deletion.Operation.Timestamp),
            Deleted = true
        };
        await BlobPriorPublicationImage.ExactAsync(deletion.Result.Get<BlobCommitResult<BlobMetadata>>().Value, tombstone);
        await BlobPriorPublicationImage.ReceiptAsync(owner, deletion.Result.Get<BlobCommitResult<BlobMetadata>>().Receipt,
            deletion, publication.Blob);
        var result = new BlobRetiredPartState(publication, deletion, tombstone);
        await BlobLaterPartAssertions.RetiredAsync(owner, result);
        return result;
    }
}
