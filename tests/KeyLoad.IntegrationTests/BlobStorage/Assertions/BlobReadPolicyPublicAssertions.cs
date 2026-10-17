using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobReadPolicyPublicAssertions
{
    internal static async Task HealthyAsync(RequestCqrsRf3Callers callers,
        BlobSupersessionPublicTrial.Version published, CancellationToken token)
    {
        var request = new BlobMetadataRequest(published.Begin.Blob);
        var metadata = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.GetBlobMetadataAsync(request, token));
        var official = (await McpCallerAssertions.SuccessAsync<BlobMetadataReadResult>(
            await callers.Mcp.CallAsync("keyload_blobs_metadata", request, token))).Value;
        await SqlRf3Protocol.EqualAsync(published.Completed.Value, metadata.Metadata);
        await SqlRf3Protocol.EqualAsync(metadata, official);
        foreach (var (offset, bytes) in new (long, byte[])[] { (0L, [11, 23, 37, 53]), (4L, []) })
        {
            var range = new BlobReadRequest(published.Begin.Blob, 1, offset, bytes.Length);
            var actual = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadBlobRangeAsync(range, token));
            var mcp = (await McpCallerAssertions.SuccessAsync<BlobReadResult>(
                await callers.Mcp.CallAsync("keyload_blobs_read_range", range, token))).Value;
            await SqlRf3Protocol.EqualAsync(new BlobReadResult(published.Completed.Value, offset, bytes), actual);
            await SqlRf3Protocol.EqualAsync(actual, mcp);
        }
    }

    internal static async Task DeniedAsync(RequestCqrsRf3Callers reader, RequestCqrsRf3Callers administrator,
        BlobSupersessionPublicTrial.Version published, string secret, CancellationToken token)
    {
        var blob = published.Begin.Blob;
        var before = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(blob.Partition, token));
        await RefusedAsync(reader, await reader.Sdk.GetBlobMetadataAsync(new(blob), token),
            "keyload_blobs_metadata", new BlobMetadataRequest(blob), secret, token);
        await UnchangedAsync(administrator, published, before, token);
        foreach (var range in new[] { new BlobReadRequest(blob, 1, 0, 4), new BlobReadRequest(blob, 1, 4, 0) })
        {
            await RefusedAsync(reader, await reader.Sdk.ReadBlobRangeAsync(range, token),
                "keyload_blobs_read_range", range, secret, token);
            await UnchangedAsync(administrator, published, before, token);
        }
    }

    private static async Task RefusedAsync<T>(RequestCqrsRf3Callers reader, Result<T> sdk,
        string tool, object request, string secret, CancellationToken token)
    {
        await Assert.That(sdk.IsFailed).IsTrue();
        await Assert.That(sdk.Value).IsNull();
        await Assert.That(sdk.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        var official = await reader.Mcp.CallAsync(tool, request, token);
        await McpCallerAssertions.ErrorAsync(official, ErrorCode.PermissionDenied, dispatched: true);
        await McpCallerAssertions.DoesNotDiscloseAsync(official, secret,
            Convert.ToBase64String(new byte[] { 11, 23, 37, 53 }));
        await Assert.That(System.Text.Json.JsonSerializer.Serialize(sdk, JsonDefaults.Options)
            .Contains(secret, StringComparison.Ordinal)).IsFalse();
    }

    private static async Task UnchangedAsync(RequestCqrsRf3Callers administrator,
        BlobSupersessionPublicTrial.Version published, OutboxStatus before, CancellationToken token)
    {
        await HealthyAsync(administrator, published, token);
        var after = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(
            published.Begin.Blob.Partition, token));
        await SqlRf3Protocol.EqualAsync(before, after);
        var official = (await McpCallerAssertions.SuccessAsync<OutboxStatus>(await administrator.Mcp.CallAsync(
            McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(published.Begin.Blob.Partition), token))).Value;
        await SqlRf3Protocol.EqualAsync(before, official);
    }
}
