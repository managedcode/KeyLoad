using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PartitionMovementBlobCurrentReadsRf3Assertions
{
    private const int FirstOffset = 0;
    private const byte FirstByte = 0x41;
    private const byte TailByte = 0x42;

    internal static async Task RequireAsync(PartitionMovementPublicParentRf3Seed seed, CancellationToken token)
    {
        var metadata = new BlobMetadataRequest(seed.Blob.Blob);
        var actual = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.GetBlobMetadataAsync(metadata, token).ConfigureAwait(false));
        await RequireRoutesAsync(seed, BlobToolNames.Metadata, metadata, seed.Blob, actual, token).ConfigureAwait(false);
        var upload = new BlobUploadInfoRequest(seed.Blob.Blob, seed.BlobOriginals.Begin.UploadId);
        var expectedUpload = seed.BlobOriginals.TailReceipt.Value with { Status = BlobUploadStatus.Complete };
        var info = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.GetBlobUploadInfoAsync(upload, token).ConfigureAwait(false));
        await RequireRoutesAsync(seed, BlobToolNames.UploadInfo, upload, expectedUpload, info, token).ConfigureAwait(false);
        var firstBytes = new byte[BlobLimits.RawPartBytes];
        Array.Fill(firstBytes, FirstByte);
        await RequireRangeAsync(seed, FirstOffset, firstBytes, token).ConfigureAwait(false);
        var tailBytes = new byte[seed.BlobOriginals.Tail.Bytes.Length];
        Array.Fill(tailBytes, TailByte);
        await RequireRangeAsync(seed, BlobLimits.RawPartBytes, tailBytes, token).ConfigureAwait(false);
        await RequireListAsync(seed, token).ConfigureAwait(false);
    }

    internal static async Task RequireDeniedAsync(PartitionMovementPublicParentRf3Seed seed, CancellationToken token)
    {
        var request = new BlobReadRequest(seed.Blob.Blob, seed.Blob.Revision, FirstOffset,
            PartitionMovementPublicParentRf3Seed.BlobRange.Length);
        var sdk = await seed.Source.ReadBlobRangeAsync(request, token).ConfigureAwait(false);
        await Assert.That(sdk.IsFailed).IsTrue();
        await Assert.That(sdk.Value).IsNull();
        await Assert.That(sdk.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        await McpCallerAssertions.ErrorAsync(await seed.Official.CallAsync(BlobToolNames.ReadRange, request, token)
            .ConfigureAwait(false), ErrorCode.PermissionDenied, dispatched: true);
        var sql = SqlRf3Protocol.Call(seed.Partition, BlobToolNames.ReadRange, request);
        var q1 = await seed.Source.ExecuteSqlAsync(sql, token).ConfigureAwait(false);
        await Assert.That(q1.IsFailed).IsTrue();
        await Assert.That(q1.Value.ValueKind).IsEqualTo(JsonValueKind.Undefined);
        await Assert.That(q1.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        await McpCallerAssertions.ErrorAsync(await seed.Official.CallAsync(SqlOperationProtocol.ToolName, sql, token)
            .ConfigureAwait(false), ErrorCode.PermissionDenied, dispatched: true);
    }

    private static async Task RequireRangeAsync(PartitionMovementPublicParentRf3Seed seed,
        long offset, byte[] bytes, CancellationToken token)
    {
        var request = new BlobReadRequest(seed.Blob.Blob, seed.Blob.Revision, offset, bytes.Length);
        var expected = new BlobReadResult(seed.Blob, offset, bytes);
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.ReadBlobRangeAsync(request, token).ConfigureAwait(false));
        await RequireRoutesAsync(seed, BlobToolNames.ReadRange, request, expected, sdk, token).ConfigureAwait(false);
    }

    private static async Task RequireListAsync(PartitionMovementPublicParentRf3Seed seed, CancellationToken token)
    {
        var request = new BlobListRequest(seed.Partition, seed.Blob.Blob.Resource, BlobLimits.MaxListItems);
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.ListBlobsAsync(request, token).ConfigureAwait(false));
        await Assert.That(sdk.Items.Any(item => item.Blob == seed.Blob.Blob)).IsTrue();
        await SqlRf3Protocol.EqualAsync(seed.Blob, sdk.Items.Single(item => item.Blob == seed.Blob.Blob));
        await RequireRoutesAsync(seed, BlobToolNames.List, request, sdk, sdk, token).ConfigureAwait(false);
    }

    private static async Task RequireRoutesAsync<TRequest, TResult>(PartitionMovementPublicParentRf3Seed seed,
        string tool, TRequest request, TResult expected, TResult sdk, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(expected, sdk);
        var official = (await McpCallerAssertions.SuccessAsync<TResult>(await seed.Official.CallAsync(tool, request, token)
            .ConfigureAwait(false))).Value;
        await SqlRf3Protocol.EqualAsync(expected, official);
        var sql = SqlRf3Protocol.Call(seed.Partition, tool, request);
        await SqlRf3Protocol.EqualAsync(expected, await SqlRf3Protocol.SdkAsync<TResult>(seed.Source, sql, token).ConfigureAwait(false));
        await SqlRf3Protocol.EqualAsync(expected, await SqlRf3Protocol.McpAsync<TResult>(seed.Official, sql, token).ConfigureAwait(false));
    }
}
