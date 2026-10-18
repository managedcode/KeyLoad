using System.Buffers.Binary;
using System.Security.Cryptography;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.RelationalStorage;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobMaximumRangePublicPublication
{
    internal const int PartBytes = 65536;
    internal const int Length = 131073;
    private const string ObjectId = "maximum-unaligned-range";

    internal static byte[] Bytes(long offset, int count)
        => Enumerable.Range(0, count).Select(index => checked((byte)(((offset + index) * 17 + 3) % 251))).ToArray();

    internal static async Task<BlobMaximumRangePublicState> CreateAsync(RequestCqrsClusterCallers callers, CancellationToken token)
    {
        var scenario = await RelationalSqlClusterScenario.CreateAsync(callers.Sdk, token);
        var blob = new BlobRef(scenario.Partition, RelationalSqlClusterTokens.Blobs, ObjectId);
        var begin = new BeginBlobUploadRequest(Guid.NewGuid(), blob, Guid.NewGuid(), Length, 0L);
        var begun = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.BeginBlobUploadAsync(begin, token));
        await SqlClusterProtocol.EqualAsync(new BlobUploadInfo(blob, begin.UploadId, Length, 0L, 0, 0L,
            begun.Value.ExpiresAt, BlobUploadStatus.Active, begun.Value.IntegrityHash), begun.Value);
        var parts = new WriteBlobPartRequest[3];
        var written = new BlobCommitResult<BlobUploadInfo>[3];
        var hash = begun.Value.IntegrityHash;
        for (var ordinal = 0; ordinal < 3; ordinal++)
        {
            var bytes = Bytes((long)ordinal * PartBytes, ordinal == 2 ? 1 : PartBytes);
            var sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
            parts[ordinal] = new(Guid.NewGuid(), blob, begin.UploadId, ordinal, bytes, sha);
            written[ordinal] = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.WriteBlobPartAsync(parts[ordinal], token));
            hash = Next(hash, ordinal, bytes.Length, sha);
            await SqlClusterProtocol.EqualAsync(new BlobUploadInfo(blob, begin.UploadId, Length, 0L, ordinal + 1,
                Math.Min((long)(ordinal + 1) * PartBytes, Length), begun.Value.ExpiresAt, BlobUploadStatus.Active, hash), written[ordinal].Value);
        }
        var complete = new CompleteBlobUploadRequest(Guid.NewGuid(), blob, begin.UploadId, hash);
        var completed = (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobMetadata>>(
            await callers.Mcp.CallAsync("keyload_blobs_complete_upload", complete, token))).Value;
        await SqlClusterProtocol.EqualAsync(new BlobMetadata(blob, 1L, begin.UploadId, Length, 3,
            hash, new RowAccess(), completed.Value.UpdatedAt), completed.Value);
        var state = new BlobMaximumRangePublicState(begin, begun, parts, written, complete, completed);
        await BlobMaximumRangePublicAssertions.ReceiptAsync(begun.Receipt, begin.CommandId, state);
        for (var index = 0; index < 3; index++)
        {
            await BlobMaximumRangePublicAssertions.ReceiptAsync(written[index].Receipt, parts[index].CommandId, state);
        }
        await BlobMaximumRangePublicAssertions.ReceiptAsync(completed.Receipt, complete.CommandId, state);
        return state;
    }

    private static string Next(string previous, int ordinal, int count, string part)
    {
        var bytes = new byte[72];
        Convert.FromHexString(previous).CopyTo(bytes, 0);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(32, 4), ordinal);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(36, 4), count);
        Convert.FromHexString(part).CopyTo(bytes, 40);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}
