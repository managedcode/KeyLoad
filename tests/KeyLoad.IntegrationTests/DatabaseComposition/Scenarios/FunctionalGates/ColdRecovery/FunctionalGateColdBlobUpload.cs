using System.Security.Cryptography;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class FunctionalGateColdBlobUpload
{
    internal static async Task<FunctionalGateColdBlob> RunAsync(FunctionalGateContext context, int index, CancellationToken token)
    {
        var blob = new BlobRef(context.Partition, FunctionalGateColdProtocol.Resource("blobs", index), FunctionalGateColdProtocol.Id);
        var bytes = FunctionalGateBlobUpload.Content(context, blob, index);
        var begin = new BeginBlobUploadRequest(Guid.NewGuid(), blob, Guid.NewGuid(), bytes.Length, 0, new RowAccess());
        var begun = await context.SdkAsync(context.Writer.Sdk.BeginBlobUploadAsync(begin, token));
        await FunctionalGateBlobAssertions.ReceiptAsync(begun.Receipt, begin.CommandId, blob);
        var current = new BlobUploadInfo(blob, begin.UploadId, bytes.Length, 0, 0, 0, begun.Value.ExpiresAt,
            BlobUploadStatus.Active, BlobIntegrity.InitialHash(begun.Receipt.Token.Incarnation, blob, begin.UploadId, bytes.Length));
        await SqlClusterProtocol.EqualAsync(current, begun.Value);
        var parts = new List<FunctionalGateColdPart>();
        for (var ordinal = 0; ordinal < 2; ordinal++)
        {
            var offset = ordinal * BlobLimits.RawPartBytes;
            var part = bytes.AsMemory(offset, Math.Min(BlobLimits.RawPartBytes, bytes.Length - offset));
            var hash = Convert.ToHexStringLower(SHA256.HashData(part.Span));
            var request = new WriteBlobPartRequest(Guid.NewGuid(), blob, begin.UploadId, ordinal, part, hash);
            var result = await context.McpAsync<BlobCommitResult<BlobUploadInfo>>(context.Writer,
                "keyload_blobs_write_part", request, token);
            await FunctionalGateBlobAssertions.ReceiptAsync(result.Receipt, request.CommandId, blob);
            current = current with
            {
                NextOrdinal = ordinal + 1,
                StoredBytes = offset + part.Length,
                IntegrityHash = BlobIntegrity.NextHash(current.IntegrityHash, ordinal, part.Length, hash)
            };
            await SqlClusterProtocol.EqualAsync(current, result.Value);
            parts.Add(new(request, result));
        }
        var complete = new CompleteBlobUploadRequest(Guid.NewGuid(), blob, begin.UploadId, current.IntegrityHash);
        var published = await context.SdkAsync(context.Writer.Sdk.CompleteBlobUploadAsync(complete, token));
        await FunctionalGateBlobAssertions.ReceiptAsync(published.Receipt, complete.CommandId, blob);
        await SqlClusterProtocol.EqualAsync(new BlobMetadata(blob, 1, begin.UploadId, bytes.Length, 2,
            current.IntegrityHash, new RowAccess(), published.Value.UpdatedAt), published.Value);
        return new(begin, begun, [.. parts], complete, published, bytes);
    }

    internal static async Task ReplayAsync(FunctionalGateContext context, FunctionalGateColdBlob ledger, CancellationToken token)
    {
        await SqlClusterProtocol.EqualAsync(ledger.Begun, await context.SdkAsync(context.Writer.Sdk.BeginBlobUploadAsync(ledger.Begin, token)));
        await SqlClusterProtocol.EqualAsync(ledger.Begun, await context.McpAsync<BlobCommitResult<BlobUploadInfo>>(
            context.Writer, "keyload_blobs_begin_upload", ledger.Begin, token));
        foreach (var part in ledger.Parts)
        {
            await SqlClusterProtocol.EqualAsync(part.Result, await context.SdkAsync(context.Writer.Sdk.WriteBlobPartAsync(part.Request, token)));
            await SqlClusterProtocol.EqualAsync(part.Result, await context.McpAsync<BlobCommitResult<BlobUploadInfo>>(
                context.Writer, "keyload_blobs_write_part", part.Request, token));
        }
        await SqlClusterProtocol.EqualAsync(ledger.Published, await context.McpAsync<BlobCommitResult<BlobMetadata>>(
            context.Writer, "keyload_blobs_complete_upload", ledger.Complete, token));
        await SqlClusterProtocol.EqualAsync(ledger.Published, await context.SdkAsync(context.Writer.Sdk.CompleteBlobUploadAsync(ledger.Complete, token)));
        await FunctionalGateBlobAssertions.ContentAsync(context, ledger.Published.Value, ledger.Bytes, token);
    }
}
