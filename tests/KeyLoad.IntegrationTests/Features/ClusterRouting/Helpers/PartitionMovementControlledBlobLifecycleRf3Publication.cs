using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PartitionMovementControlledBlobLifecycleRf3Publication
{
    private const long InitialRevision = 0;
    private const long PublishedRevision = 1;
    private const int FirstOrdinal = 0;
    private const int PartLength = 8;
    private const byte PartByte = 0x46;
    private const int OnePart = 1;
    private const int NoParts = 0;

    internal static async Task RunAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed, BlobRef blob,
        List<PartitionMovementControlledBlobCommandRf3Proof> proofs, CancellationToken token)
    {
        var (upload, metadata) = await CompleteAsync(seed, blob, proofs, token).ConfigureAwait(false);
        await Assert.That(metadata.Revision).IsEqualTo(PublishedRevision);
        var wrong = new DeleteBlobRequest(Guid.NewGuid(), blob, checked(metadata.Revision + PublishedRevision));
        var refused = PartitionMovementControlledBlobLifecycleRf3Calls.Capture<DeleteBlobRequest, BlobCommitResult<BlobMetadata>>(
            OperationKind.DeleteBlob, wrong.CommandId, BlobToolNames.Delete, wrong, null, ErrorCode.RevisionConflict,
            false, ct => seed.Source.DeleteBlobAsync(wrong, ct));
        await PartitionMovementControlledBlobFailedCutRf3Trial.RequireAsync(wave, seed, refused, token).ConfigureAwait(false);
        var unchanged = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.GetBlobMetadataAsync(new(blob), token).ConfigureAwait(false));
        await SqlRf3Protocol.EqualAsync(metadata, unchanged);
        proofs.Add(refused);
        var delete = new DeleteBlobRequest(Guid.NewGuid(), blob, metadata.Revision);
        var deleted = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.DeleteBlobAsync(delete, token).ConfigureAwait(false));
        var expected = metadata with
        {
            Revision = checked(PublishedRevision + PublishedRevision),
            VersionId = null,
            Length = NoParts,
            PartCount = NoParts,
            IntegrityHash = null,
            Deleted = true,
            UpdatedAt = deleted.Value.UpdatedAt
        };
        await SqlRf3Protocol.EqualAsync(expected, deleted.Value);
        proofs.Add(PartitionMovementControlledBlobLifecycleRf3Calls.Capture(OperationKind.DeleteBlob, delete.CommandId,
            BlobToolNames.Delete, delete, deleted, null, false, ct => seed.Source.DeleteBlobAsync(delete, ct)));
        var reclaim = new ReclaimBlobRequest(Guid.NewGuid(), blob, upload, OnePart);
        var reclaimed = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.ReclaimBlobAsync(reclaim, token).ConfigureAwait(false));
        await SqlRf3Protocol.EqualAsync(new BlobReclaimResult(upload, OnePart, NoParts, PartLength, true), reclaimed.Value);
        proofs.Add(PartitionMovementControlledBlobLifecycleRf3Calls.Capture(OperationKind.ReclaimBlob, reclaim.CommandId,
            BlobToolNames.Reclaim, reclaim, reclaimed, null, true, ct => seed.Source.ReclaimBlobAsync(reclaim, ct)));
        var tombstone = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.GetBlobMetadataAsync(new(blob), token).ConfigureAwait(false));
        await SqlRf3Protocol.EqualAsync(expected, tombstone);
        var absent = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.GetBlobUploadInfoAsync(new(blob, upload), token).ConfigureAwait(false));
        await Assert.That(absent).IsNull();
    }

    private static async Task<(Guid Upload, BlobMetadata Metadata)> CompleteAsync(PartitionMovementPublicParentRf3Seed seed,
        BlobRef blob, List<PartitionMovementControlledBlobCommandRf3Proof> proofs, CancellationToken token)
    {
        var upload = Guid.NewGuid();
        var begin = new BeginBlobUploadRequest(Guid.NewGuid(), blob, upload, PartLength, InitialRevision);
        var begun = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.BeginBlobUploadAsync(begin, token).ConfigureAwait(false));
        proofs.Add(PartitionMovementControlledBlobLifecycleRf3Calls.Capture(OperationKind.BeginBlobUpload, begin.CommandId,
            BlobToolNames.BeginUpload, begin, begun, null, true, ct => seed.Source.BeginBlobUploadAsync(begin, ct)) with
        { LifetimeRemoved = true });
        var bytes = new byte[PartLength];
        Array.Fill(bytes, PartByte);
        var part = new WriteBlobPartRequest(Guid.NewGuid(), blob, upload, FirstOrdinal, bytes, BlobIntegrity.PartHash(bytes));
        var written = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.WriteBlobPartAsync(part, token).ConfigureAwait(false));
        proofs.Add(PartitionMovementControlledBlobLifecycleRf3Calls.Capture(OperationKind.WriteBlobPart, part.CommandId,
            BlobToolNames.WritePart, part, written, null, true, ct => seed.Source.WriteBlobPartAsync(part, ct)) with
        { LifetimeRemoved = true });
        var complete = new CompleteBlobUploadRequest(Guid.NewGuid(), blob, upload, written.Value.IntegrityHash);
        var completed = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.CompleteBlobUploadAsync(complete, token).ConfigureAwait(false));
        proofs.Add(PartitionMovementControlledBlobLifecycleRf3Calls.Capture(OperationKind.CompleteBlobUpload, complete.CommandId,
            BlobToolNames.CompleteUpload, complete, completed, null, true, ct => seed.Source.CompleteBlobUploadAsync(complete, ct)) with
        { LifetimeRemoved = true });
        foreach (var proof in proofs.Where(value => NativeCommandUpload(value.Request) == upload && value.Error is null))
        { await PartitionMovementControlledBlobLifecycleRf3Calls.RequireAsync(seed, proof, null, token).ConfigureAwait(false); }
        return (upload, completed.Value);
    }

    private static Guid? NativeCommandUpload(object request) => request switch
    {
        BeginBlobUploadRequest begin => begin.UploadId,
        WriteBlobPartRequest part => part.UploadId,
        CompleteBlobUploadRequest complete => complete.UploadId,
        _ => null
    };
}
