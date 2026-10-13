using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobArchiveActivePublicPublication
{
    internal static byte[] Bytes(int ordinal, int count)
        => Enumerable.Range(0, count).Select(index => (byte)((index + ordinal * 29 + 11) % 251)).ToArray();

    internal static async Task<BlobArchiveActivePublicVersion> CreateAsync(KeyLoad.Client.KeyLoadClient sdk,
        McpOfficialClient official, BlobRef blob, int fullParts, bool complete, long revision, CancellationToken token)
    {
        var placement = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadAtomicPartitionPlacementAsync(new(1, blob.Partition), token));
        var begin = new BeginBlobUploadRequest(Guid.NewGuid(), blob, Guid.NewGuid(), fullParts * 65536L + 17L, revision);
        var begun = await McpCallerAssertions.SdkSuccessAsync(await sdk.BeginBlobUploadAsync(begin, token));
        await SqlRf3Protocol.EqualAsync(begun, (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobUploadInfo>>(
            await official.CallAsync("keyload_blobs_begin_upload", begin, token))).Value);
        await ReceiptAsync(begun.Receipt, begin.CommandId, blob, placement, token);
        var parts = new List<WriteBlobPartRequest>();
        var written = new List<BlobCommitResult<BlobUploadInfo>>();
        var hash = begun.Value.IntegrityHash;
        for (var ordinal = 0; ordinal < (complete ? fullParts + 1 : fullParts); ordinal++)
        {
            var bytes = Bytes(ordinal, ordinal == fullParts ? 17 : 65536);
            var request = new WriteBlobPartRequest(Guid.NewGuid(), blob, begin.UploadId, ordinal, bytes, BlobIntegrity.PartHash(bytes));
            var result = await McpCallerAssertions.SdkSuccessAsync(await sdk.WriteBlobPartAsync(request, token));
            await SqlRf3Protocol.EqualAsync(result, (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobUploadInfo>>(
                await official.CallAsync("keyload_blobs_write_part", request, token))).Value);
            await ReceiptAsync(result.Receipt, request.CommandId, blob, placement, token);
            hash = BlobIntegrity.NextHash(hash, ordinal, bytes.Length, request.Sha256);
            await Assert.That(result.Value.IntegrityHash).IsEqualTo(hash);
            await Assert.That(result.Value.NextOrdinal).IsEqualTo(ordinal + 1);
            parts.Add(request);
            written.Add(result);
        }
        var end = complete ? new CompleteBlobUploadRequest(Guid.NewGuid(), blob, begin.UploadId, hash) : null;
        var completed = end is null ? null : await McpCallerAssertions.SdkSuccessAsync(await sdk.CompleteBlobUploadAsync(end, token));
        if (end is not null && completed is not null)
        {
            await SqlRf3Protocol.EqualAsync(completed, (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobMetadata>>(
                await official.CallAsync("keyload_blobs_complete_upload", end, token))).Value);
            await ReceiptAsync(completed.Receipt, end.CommandId, blob, placement, token);
            await Assert.That(completed.Value.Length).IsEqualTo(begin.Length);
            await Assert.That(completed.Value.PartCount).IsEqualTo(fullParts + 1);
            await Assert.That(completed.Value.Revision).IsEqualTo(revision + 1);
            await Assert.That(completed.Value.VersionId).IsEqualTo(begin.UploadId);
            await Assert.That(completed.Value.IntegrityHash).IsEqualTo(hash);
        }
        return new(begin, begun, parts.ToArray(), written.ToArray(), end, completed);
    }
    private static async Task ReceiptAsync(CommitReceipt receipt, Guid id, BlobRef blob,
        AtomicPartitionPlacementResolution placement, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await Assert.That(receipt.CommandId).IsEqualTo(id);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(placement.Incarnation);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(blob.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.OwnershipEpoch).IsEqualTo(placement.PlacementEpoch);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(receipt.Mutations).IsEmpty();
    }
}
