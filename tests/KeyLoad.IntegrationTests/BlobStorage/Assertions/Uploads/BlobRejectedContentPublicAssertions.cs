using System.Buffers.Binary;
using System.Security.Cryptography;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using ManagedCode.Communication;
using ModelContextProtocol.Protocol;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobRejectedContentPublicAssertions
{
    private const int ChainPreimageBytes = 72;
    private const int PreviousDigestBytes = 32;
    private const int OrdinalOffset = 32;
    private const int LengthOffset = 36;
    private const int PartDigestOffset = 40;

    private const string ErrorDetailProperty = "detail";
    private const string InvalidDetail = "The blob request or policy is invalid.";
    private const string ConflictDetail = "The blob upload conflicts with its current state.";
    private const string SafeMcpDetail = "The database operation could not be completed.";

    internal static string IndependentNext(string previous)
    {
        var digest = Convert.FromHexString(previous);
        if (digest.Length != PreviousDigestBytes)
        {
            throw new InvalidOperationException("The original public chain seed has invalid digest length.");
        }
        Span<byte> preimage = stackalloc byte[ChainPreimageBytes];
        digest.AsSpan().CopyTo(preimage);
        BinaryPrimitives.WriteInt32BigEndian(preimage[OrdinalOffset..], 0);
        BinaryPrimitives.WriteInt32BigEndian(preimage[LengthOffset..], 4);
        Convert.FromHexString(BlobRejectedContentPublicTrial.OriginalPartSha256).AsSpan().CopyTo(preimage[PartDigestOffset..]);
        return Convert.ToHexStringLower(SHA256.HashData(preimage));
    }

    internal static async Task PartRefusedAsync(RequestCqrsClusterCallers callers, WriteBlobPartRequest request,
        ErrorCode code, Func<Task> healthy, CancellationToken token)
    {
        var detail = code == ErrorCode.Validation ? InvalidDetail : ConflictDetail;
        await RefusedAsync(await callers.Sdk.WriteBlobPartAsync(request, token), code, detail);
        await healthy();
        await RefusedAsync(await callers.Mcp.CallAsync("keyload_blobs_write_part", request, token), code, SafeMcpDetail);
        await healthy();
    }

    internal static async Task CompleteRefusedAsync(RequestCqrsClusterCallers callers,
        CompleteBlobUploadRequest request, Func<Task> healthy, CancellationToken token)
    {
        await RefusedAsync(await callers.Sdk.CompleteBlobUploadAsync(request, token), ErrorCode.Validation, InvalidDetail);
        await healthy();
        await RefusedAsync(await callers.Mcp.CallAsync("keyload_blobs_complete_upload", request, token),
            ErrorCode.Validation, SafeMcpDetail);
        await healthy();
    }

    private static async Task RefusedAsync<T>(Result<T> value, ErrorCode code, string detail)
    {
        await Assert.That(value.IsFailed).IsTrue();
        await Assert.That(value.Value).IsNull();
        await Assert.That(value.Problem?.ErrorCode).IsEqualTo(code.ToString());
        await Assert.That(value.Problem?.Detail).IsEqualTo(detail);
    }

    private static async Task RefusedAsync(CallToolResult value, ErrorCode code, string detail)
    {
        await McpCallerAssertions.ErrorAsync(value, code, dispatched: true);
        await Assert.That(value.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
            .GetProperty(ErrorDetailProperty).GetString()).IsEqualTo(detail);
    }

    internal static async Task HiddenAsync(RequestCqrsClusterCallers callers, BeginBlobUploadRequest begin,
        BlobUploadInfo expected, CancellationToken token)
    {
        var query = new BlobUploadInfoRequest(begin.Blob, begin.UploadId);
        await SqlClusterProtocol.EqualAsync(expected,
            await PublicReadAssertions.UploadAsync(await callers.Sdk.GetBlobUploadInfoAsync(query, token)));
        await SqlClusterProtocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<BlobUploadInfoReadResult>(
            await callers.Mcp.CallAsync("keyload_blobs_upload_info", query, token))).Value.Upload);
        await Assert.That(await PublicReadAssertions.MetadataAsync(
            await callers.Sdk.GetBlobMetadataAsync(new(begin.Blob), token))).IsNull();
        await Assert.That((await McpCallerAssertions.SuccessAsync<BlobMetadataReadResult>(
            await callers.Mcp.CallAsync("keyload_blobs_metadata", new BlobMetadataRequest(begin.Blob), token))).Value.Metadata).IsNull();
    }

    internal static async Task WrittenAsync(BeginBlobUploadRequest request, BlobUploadInfo before,
        BlobUploadInfo actual, string chain)
    {
        await Assert.That(before.StoredBytes).IsEqualTo(0L);
        await Assert.That(before.NextOrdinal).IsEqualTo(0);
        await SqlClusterProtocol.EqualAsync(before with
        {
            StoredBytes = request.Length,
            NextOrdinal = 1,
            IntegrityHash = chain
        }, actual);
    }
}
