using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class FunctionalGateBlobUpload
{
    internal static byte[] Content(FunctionalGateContext context, BlobRef blob, int iteration)
    {
        var bytes = new byte[BlobLimits.RawPartBytes + 97];
        for (var index = 0; index < bytes.Length; index++)
        {
            bytes[index] = (byte)((index * 31L + iteration * 17L) & 255);
        }
        var header = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            context.Anchor,
            Blob = blob,
            Iteration = iteration
        }, JsonDefaults.Options));
        header.CopyTo(bytes, 0);
        return bytes;
    }

    internal static async Task<FunctionalGateBlobVersion> PublishAsync(FunctionalGateContext context,
        BlobRef blob, int iteration, CancellationToken token)
    {
        var bytes = Content(context, blob, iteration);
        var begin = new BeginBlobUploadRequest(Guid.NewGuid(), blob, Guid.NewGuid(), bytes.Length, 0L, new RowAccess());
        var begun = await context.SdkAsync(context.Writer.Sdk.BeginBlobUploadAsync(begin, token));
        await FunctionalGateBlobAssertions.ReceiptAsync(begun.Receipt, begin.CommandId, blob);
        var initial = BlobIntegrity.InitialHash(begun.Receipt.Token.Incarnation, blob, begin.UploadId, bytes.Length);
        var current = new BlobUploadInfo(blob, begin.UploadId, bytes.Length, 0L, 0, 0L,
            begun.Value.ExpiresAt, BlobUploadStatus.Active, initial);
        await SqlClusterProtocol.EqualAsync(current, begun.Value);
        await SqlClusterProtocol.EqualAsync(begun, await context.McpAsync<BlobCommitResult<BlobUploadInfo>>(
            context.Writer, "keyload_blobs_begin_upload", begin, token));
        for (var ordinal = 0; ordinal < 2; ordinal++)
        {
            current = await PartAsync(context, begin, bytes, ordinal, current, token);
        }
        var complete = new CompleteBlobUploadRequest(Guid.NewGuid(), blob, begin.UploadId, current.IntegrityHash);
        var published = await context.McpAsync<BlobCommitResult<BlobMetadata>>(
            context.Writer, "keyload_blobs_complete_upload", complete, token);
        await FunctionalGateBlobAssertions.ReceiptAsync(published.Receipt, complete.CommandId, blob);
        var expected = new BlobMetadata(blob, 1L, begin.UploadId, bytes.Length, 2, current.IntegrityHash,
            new RowAccess(), published.Value.UpdatedAt);
        await SqlClusterProtocol.EqualAsync(expected, published.Value);
        await SqlClusterProtocol.EqualAsync(published,
            await context.SdkAsync(context.Writer.Sdk.CompleteBlobUploadAsync(complete, token)));
        var info = new BlobUploadInfoRequest(blob, begin.UploadId);
        await SqlClusterProtocol.EqualAsync(current with
        {
            Status = BlobUploadStatus.Complete
        },
            (await context.SdkAsync(context.Writer.Sdk.GetBlobUploadInfoAsync(info, token))).Upload);
        return new(begin, bytes, complete, published);
    }

    private static async Task<BlobUploadInfo> PartAsync(FunctionalGateContext context,
        BeginBlobUploadRequest begin, byte[] bytes, int ordinal, BlobUploadInfo previous, CancellationToken token)
    {
        var offset = ordinal * BlobLimits.RawPartBytes;
        var part = bytes.AsMemory(offset, Math.Min(BlobLimits.RawPartBytes, bytes.Length - offset));
        var hash = Convert.ToHexStringLower(SHA256.HashData(part.Span));
        var request = new WriteBlobPartRequest(Guid.NewGuid(), begin.Blob, begin.UploadId, ordinal, part, hash);
        var written = await context.McpAsync<BlobCommitResult<BlobUploadInfo>>(
            context.Writer, "keyload_blobs_write_part", request, token);
        var expected = previous with
        {
            NextOrdinal = ordinal + 1,
            StoredBytes = offset + part.Length,
            IntegrityHash = BlobIntegrity.NextHash(previous.IntegrityHash, ordinal, part.Length, hash)
        };
        await SqlClusterProtocol.EqualAsync(expected, written.Value);
        await FunctionalGateBlobAssertions.ReceiptAsync(written.Receipt, request.CommandId, begin.Blob);
        await SqlClusterProtocol.EqualAsync(written,
            await context.SdkAsync(context.Writer.Sdk.WriteBlobPartAsync(request, token)));
        return expected;
    }
}
