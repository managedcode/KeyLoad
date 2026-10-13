using KeyLoad.CrashHost.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Publishes the original chunked blob through fresh signed native requests and independent log receipts.</summary>
internal static class ControlledPartitionMovementNativeBlob
{
    private const int FirstOrdinal = 0;
    private const int InitialRevision = 0;
    private const int PublishedRevision = 1;
    private const int PublishedPartCount = 1;
    private static readonly Guid ConfigureId = Guid.Parse("a3aa5b55-c605-4c3b-9e49-871b876ef608");
    private static readonly Guid BeginId = Guid.Parse("28175434-9a57-434e-b201-b376c9206f2d");
    private static readonly Guid PartId = Guid.Parse("5738caef-a9e4-4623-ad93-d91e3838aff6");

    internal static async Task<(CompleteBlobUploadRequest Request, BlobCommitResult<BlobMetadata> Expected)> PublishAsync(
        ControlledPartitionMovementNativeHost source, CancellationToken cancellationToken)
    {
        var partition = ControlledPartitionMovementCorpus.Partition;
        await SubmitAsync(source, OperationKind.ConfigureResource, ConfigureId,
            new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId,
                new(ControlledPartitionMovementBlobSeed.Resource, ResourceKind.BlobStore, partition.TransactionDomainId)), cancellationToken);
        var bytes = ControlledPartitionMovementBlobSeed.Bytes();
        await SubmitAsync(source, OperationKind.BeginBlobUpload, BeginId,
            new BeginBlobUploadRequest(BeginId, ControlledPartitionMovementBlobSeed.Blob,
                ControlledPartitionMovementBlobSeed.UploadId, bytes.Length, InitialRevision), cancellationToken);
        await SubmitAsync(source, OperationKind.WriteBlobPart, PartId,
            new WriteBlobPartRequest(PartId, ControlledPartitionMovementBlobSeed.Blob,
                ControlledPartitionMovementBlobSeed.UploadId, FirstOrdinal, bytes,
                ControlledPartitionMovementBlobSeed.PartSha256), cancellationToken);
        var initial = BlobIntegrity.InitialHash(source.Store.Identity.Incarnation, ControlledPartitionMovementBlobSeed.Blob,
            ControlledPartitionMovementBlobSeed.UploadId, bytes.Length);
        var integrity = BlobIntegrity.NextHash(initial, FirstOrdinal, bytes.Length, ControlledPartitionMovementBlobSeed.PartSha256);
        var request = new CompleteBlobUploadRequest(ControlledPartitionMovementBlobSeed.CompleteId,
            ControlledPartitionMovementBlobSeed.Blob, ControlledPartitionMovementBlobSeed.UploadId, integrity);
        var reply = await source.SubmitAsync(OperationKind.CompleteBlobUpload, request.CommandId,
            PhysicalShardCatalogFixture.RootPrincipalId, NativeSerialization.Serialize(request), cancellationToken);
        var entry = await ControlledPartitionMovementNativeEntryReader.ReadAsync(source, request.CommandId, cancellationToken);
        var original = entry.Operation ?? throw new InvalidOperationException("The original blob entry has no operation.");
        var metadata = new BlobMetadata(request.Blob, PublishedRevision, request.UploadId, bytes.Length, PublishedPartCount,
            integrity, new RowAccess(), original.EvaluatedAt, false);
        var receipt = new CommitReceipt(request.CommandId,
            new(source.Store.Identity.Incarnation, partition.AtomicPartitionId, entry.Index, PublishedRevision),
            [], DurabilityProfile.QuorumProcessDurable);
        var expected = new BlobCommitResult<BlobMetadata>(receipt, metadata);
        await ControlledPartitionMovementNativeBlobAssertions.ReplyAsync(reply, expected);
        await ControlledPartitionMovementNativeBlobAssertions.ReadAsync(source, metadata, cancellationToken);
        return (request, expected);
    }

    private static async Task SubmitAsync<T>(ControlledPartitionMovementNativeHost source, OperationKind kind,
        Guid commandId, T request, CancellationToken cancellationToken)
    {
        var reply = await source.SubmitAsync(kind, commandId, PhysicalShardCatalogFixture.RootPrincipalId,
            NativeSerialization.Serialize(request), cancellationToken);
        await Assert.That(reply.Error).IsNull();
        await Assert.That(reply.SafeDetail).IsNull();
        await Assert.That(reply.Payload.IsEmpty).IsFalse();
    }
}
