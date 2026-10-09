using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PartitionMovementControlledBlobLifecycleRf3Trial
{
    private const string BlobId = "parent-controlled-six-kind";
    private const long InitialRevision = 0;
    private const int Ordinal = 0;
    private const int PartLength = 8;
    private const byte PartByte = 0x45;
    private const int OnePart = 1;
    private const int NoParts = 0;

    internal static async Task<PartitionMovementControlledBlobCommandRf3Proof[]> RunAsync(
        TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed, PartitionMoveResult terminal, CancellationToken token)
    {
        var blob = new BlobRef(seed.Partition, seed.Blob.Blob.Resource, BlobId);
        var proofs = new List<PartitionMovementControlledBlobCommandRf3Proof>();
        var aborted = await AbortAsync(wave, seed, blob, proofs, token).ConfigureAwait(false);
        await ReclaimAsync(seed, blob, aborted, proofs, token).ConfigureAwait(false);
        await PartitionMovementControlledBlobLifecycleRf3Publication.RunAsync(wave, seed, blob, proofs, token).ConfigureAwait(false);
        foreach (var proof in proofs)
        {
            if (proof.Value is BlobCommitResult<BlobUploadInfo> upload)
            { await PartitionMovementControlledBlobLifecycleRf3Assertions.RequireReceiptAsync(seed, terminal, proof.CommandId, upload.Receipt); }
            if (proof.Value is BlobCommitResult<BlobMetadata> metadata)
            { await PartitionMovementControlledBlobLifecycleRf3Assertions.RequireReceiptAsync(seed, terminal, proof.CommandId, metadata.Receipt); }
            if (proof.Value is BlobCommitResult<BlobReclaimResult> reclaim)
            { await PartitionMovementControlledBlobLifecycleRf3Assertions.RequireReceiptAsync(seed, terminal, proof.CommandId, reclaim.Receipt); }
        }
        return proofs.ToArray();
    }

    private static async Task<Guid> AbortAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed, BlobRef blob,
        List<PartitionMovementControlledBlobCommandRf3Proof> proofs, CancellationToken token)
    {
        var upload = Guid.NewGuid();
        var begin = new BeginBlobUploadRequest(Guid.NewGuid(), blob, upload, PartLength, InitialRevision);
        var begun = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.BeginBlobUploadAsync(begin, token).ConfigureAwait(false));
        proofs.Add(PartitionMovementControlledBlobLifecycleRf3Calls.Capture(OperationKind.BeginBlobUpload, begin.CommandId,
            BlobToolNames.BeginUpload, begin, begun, null, true, ct => seed.Source.BeginBlobUploadAsync(begin, ct)) with { LifetimeRemoved = true });
        var bytes = new byte[PartLength];
        Array.Fill(bytes, PartByte);
        var part = new WriteBlobPartRequest(Guid.NewGuid(), blob, upload, Ordinal, bytes, BlobIntegrity.PartHash(bytes));
        var written = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.WriteBlobPartAsync(part, token).ConfigureAwait(false));
        proofs.Add(PartitionMovementControlledBlobLifecycleRf3Calls.Capture(OperationKind.WriteBlobPart, part.CommandId,
            BlobToolNames.WritePart, part, written, null, true, ct => seed.Source.WriteBlobPartAsync(part, ct)) with { LifetimeRemoved = true });
        var forbidden = new ReclaimBlobRequest(Guid.NewGuid(), blob, upload, OnePart);
        var refused = PartitionMovementControlledBlobLifecycleRf3Calls.Capture<ReclaimBlobRequest, BlobCommitResult<BlobReclaimResult>>(
            OperationKind.ReclaimBlob, forbidden.CommandId, BlobToolNames.Reclaim, forbidden, null, ErrorCode.Conflict,
            false, ct => seed.Source.ReclaimBlobAsync(forbidden, ct));
        await PartitionMovementControlledBlobFailedCutRf3Trial.RequireAsync(wave, seed, refused, token).ConfigureAwait(false);
        var unchanged = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.GetBlobUploadInfoAsync(new(blob, upload), token).ConfigureAwait(false));
        await KeyLoad.IntegrationTests.Features.QueryExecution.SqlRf3Protocol.EqualAsync(written.Value, unchanged);
        proofs.Add(refused);
        var abort = new AbortBlobUploadRequest(Guid.NewGuid(), blob, upload);
        var result = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.AbortBlobUploadAsync(abort, token).ConfigureAwait(false));
        await KeyLoad.IntegrationTests.Features.QueryExecution.SqlRf3Protocol.EqualAsync(
            written.Value with { Status = BlobUploadStatus.Aborted }, result.Value);
        var aborted = PartitionMovementControlledBlobLifecycleRf3Calls.Capture(OperationKind.AbortBlobUpload, abort.CommandId,
            BlobToolNames.AbortUpload, abort, result, null, true, ct => seed.Source.AbortBlobUploadAsync(abort, ct));
        foreach (var proof in proofs.Where(value => value.LifetimeRemoved))
        { await PartitionMovementControlledBlobLifecycleRf3Calls.RequireAsync(seed, proof, null, token).ConfigureAwait(false); }
        await PartitionMovementControlledBlobLifecycleRf3Calls.RequireAsync(seed, aborted, null, token).ConfigureAwait(false);
        proofs.Add(aborted with { LifetimeRemoved = true });
        return upload;
    }

    private static async Task ReclaimAsync(PartitionMovementPublicParentRf3Seed seed, BlobRef blob, Guid upload,
        List<PartitionMovementControlledBlobCommandRf3Proof> proofs, CancellationToken token)
    {
        var request = new ReclaimBlobRequest(Guid.NewGuid(), blob, upload, OnePart);
        var result = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.ReclaimBlobAsync(request, token).ConfigureAwait(false));
        await KeyLoad.IntegrationTests.Features.QueryExecution.SqlRf3Protocol.EqualAsync(
            new BlobReclaimResult(upload, OnePart, NoParts, PartLength, true), result.Value);
        proofs.Add(PartitionMovementControlledBlobLifecycleRf3Calls.Capture(OperationKind.ReclaimBlob, request.CommandId,
            BlobToolNames.Reclaim, request, result, null, true, ct => seed.Source.ReclaimBlobAsync(request, ct)));
        var absent = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.GetBlobUploadInfoAsync(new(blob, upload), token).ConfigureAwait(false));
        await Assert.That(absent).IsNull();
        foreach (var proof in proofs.Where(value => value.LifetimeRemoved))
        { await PartitionMovementControlledBlobLifecycleRf3Calls.RequireAsync(seed, proof, ErrorCode.TokenInvalidated, token).ConfigureAwait(false); }
    }
}
