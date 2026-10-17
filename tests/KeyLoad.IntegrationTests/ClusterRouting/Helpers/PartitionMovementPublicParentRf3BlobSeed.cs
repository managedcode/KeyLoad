using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.RelationalStorage;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PartitionMovementPublicParentRf3BlobSeed
{
    private const int OriginalPartCount = 2;
    private const int OriginalFirstPart = 0;
    private const int OriginalTailPart = 1;

    internal static async Task CreateAsync(
        PartitionMovementPublicParentRf3Seed seed, List<Func<CancellationToken, Task>> blobReplays,
        Action<BlobMetadata> publishBlob, Action<PartitionMovementPublicParentRf3BlobOriginals> publishOriginals,
        CancellationToken cancellationToken)
    {
        var blob = new BlobRef(seed.Partition, RelationalSqlRf3Tokens.Blobs, "parent-native-parts");
        var upload = Guid.NewGuid();
        var begin = new BeginBlobUploadRequest(Guid.NewGuid(), blob, upload, BlobLimits.RawPartBytes + PartitionMovementPublicParentRf3Seed.BlobRange.Length, 0);
        var begun = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.BeginBlobUploadAsync(begin, cancellationToken));
        blobReplays.Add(async token =>
        {
            await SqlRf3Protocol.EqualAsync(begun, await McpCallerAssertions.SdkSuccessAsync(await seed.Source.BeginBlobUploadAsync(begin, token)));
            await SqlRf3Protocol.EqualAsync(begun, (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobUploadInfo>>(
                await seed.Official.CallAsync("keyload_blobs_begin_upload", begin, token))).Value);
        });
        var firstBytes = new byte[BlobLimits.RawPartBytes];
        Array.Fill(firstBytes, (byte)0x41);
        var first = new WriteBlobPartRequest(Guid.NewGuid(), blob, upload, 0, firstBytes, BlobIntegrity.PartHash(firstBytes));
        var tailBytes = new byte[] { 0x42, 0x42, 0x42, 0x42 };
        var tail = new WriteBlobPartRequest(Guid.NewGuid(), blob, upload, 1, tailBytes, BlobIntegrity.PartHash(tailBytes));
        var partReceipts = new List<BlobCommitResult<BlobUploadInfo>>(OriginalPartCount);
        foreach (var request in new[] { first, tail })
        {
            var receipt = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.WriteBlobPartAsync(request, cancellationToken));
            partReceipts.Add(receipt);
            blobReplays.Add(async token =>
            {
                await SqlRf3Protocol.EqualAsync(receipt, await McpCallerAssertions.SdkSuccessAsync(await seed.Source.WriteBlobPartAsync(request, token)));
                await SqlRf3Protocol.EqualAsync(receipt, (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobUploadInfo>>(
                    await seed.Official.CallAsync("keyload_blobs_write_part", request, token))).Value);
            });
        }
        var hash = BlobIntegrity.NextHash(begun.Value.IntegrityHash, first.Ordinal, first.Bytes.Length, first.Sha256);
        hash = BlobIntegrity.NextHash(hash, tail.Ordinal, tail.Bytes.Length, tail.Sha256);
        var complete = new CompleteBlobUploadRequest(Guid.NewGuid(), blob, upload, hash);
        var completed = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.CompleteBlobUploadAsync(complete, cancellationToken));
        publishBlob(completed.Value);
        publishOriginals(new PartitionMovementPublicParentRf3BlobOriginals(begin, begun, first, partReceipts[OriginalFirstPart], tail, partReceipts[OriginalTailPart], complete, completed));
        blobReplays.Add(async token =>
        {
            await SqlRf3Protocol.EqualAsync(completed, await McpCallerAssertions.SdkSuccessAsync(await seed.Source.CompleteBlobUploadAsync(complete, token)));
            await SqlRf3Protocol.EqualAsync(completed, (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobMetadata>>(
                await seed.Official.CallAsync("keyload_blobs_complete_upload", complete, token))).Value);
        });
    }

}
