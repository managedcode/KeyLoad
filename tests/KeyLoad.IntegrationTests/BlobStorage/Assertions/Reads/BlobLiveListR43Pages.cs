using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.RelationalStorage;
namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobLiveListR43Pages
{
    private const string ProblemDetailKey = "detail";
    internal static async Task<BlobListPage[]> BothAsync(RequestCqrsClusterCallers callers, BlobLiveListR43State state,
        BlobListRequest sdkRequest, BlobListRequest mcpRequest, BlobListPage expected, int target, CancellationToken token)
    {
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ListBlobsAsync(sdkRequest, token));
        await SqlClusterProtocol.EqualAsync(expected, sdk);
        await CurrentAsync(callers, state, target, token);
        var mcp = (await McpCallerAssertions.SuccessAsync<BlobListPage>(
            await callers.Mcp.CallAsync(SqlClusterProtocol.BlobListTool, mcpRequest, token))).Value;
        await SqlClusterProtocol.EqualAsync(expected, mcp);
        await CurrentAsync(callers, state, target, token);
        return [sdk, mcp];
    }
    internal static async Task CurrentAsync(RequestCqrsClusterCallers callers, BlobLiveListR43State state,
        int target, CancellationToken token)
    {
        var request = new BlobListRequest(state.Partition, RelationalSqlClusterTokens.Blobs, 8, null);
        var expected = new BlobListPage([.. state.Visible()], null);
        await SqlClusterProtocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ListBlobsAsync(request, token)));
        await SqlClusterProtocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<BlobListPage>(
            await callers.Mcp.CallAsync(SqlClusterProtocol.BlobListTool, request, token))).Value);
        await BytesAsync(callers, state, target, token);
        await SqlClusterProtocol.EqualAsync(state.Outbox, await OutboxAsync(callers, state.Partition, token));
    }
    private static async Task BytesAsync(RequestCqrsClusterCallers callers, BlobLiveListR43State state,
        int target, CancellationToken token)
    {
        var version = state.Versions[target];
        if (state.Phase == 3 && target == 1)
        {
            await BlobBoundedRetirementPublicAssertions.HeadAsync(callers, state.Deleted.Value, token);
            var missing = new BlobReadRequest(version.Begin.Blob, 1L, 0L, 2);
            var sdk = await callers.Sdk.ReadBlobRangeAsync(missing, token);
            await Assert.That(sdk.IsSuccess).IsFalse();
            await Assert.That(sdk.Value).IsNull();
            await SqlClusterProtocol.EqualAsync(Errors.Problem(ErrorCode.NotFound, "The published blob is unavailable."), sdk.Problem);
            var reply = await callers.Mcp.CallAsync("keyload_blobs_read_range", missing, token);
            await McpCallerAssertions.ErrorAsync(reply, ErrorCode.NotFound, dispatched: true);
            await Assert.That(reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
                .GetProperty(ProblemDetailKey).GetString()).IsEqualTo("The database operation could not be completed.");
            return;
        }
        var range = new BlobReadRequest(version.Begin.Blob, 1L, 0L, 2);
        var bytes = new BlobReadResult(version.Completed.Value, 0L, BlobLiveListR43State.Bytes(target));
        await SqlClusterProtocol.EqualAsync(bytes, await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadBlobRangeAsync(range, token)));
        await SqlClusterProtocol.EqualAsync(bytes, (await McpCallerAssertions.SuccessAsync<BlobReadResult>(
            await callers.Mcp.CallAsync("keyload_blobs_read_range", range, token))).Value);
    }
    internal static async Task<OutboxStatus> OutboxAsync(RequestCqrsClusterCallers callers,
        PartitionRef partition, CancellationToken token)
    {
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.OutboxStatusAsync(partition, token));
        await SqlClusterProtocol.EqualAsync(sdk, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await callers.Mcp.CallAsync(McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(partition), token))).Value);
        return sdk;
    }
    internal static async Task AllBytesAsync(RequestCqrsClusterCallers callers, BlobLiveListR43State state, CancellationToken token)
    {
        int[] indices = state.Phase switch
        {
            0 => [0, 1],
            1 => [0, 1, 2],
            _ => [0, 1, 2, 3]
        };
        foreach (var index in indices)
        {
            await BytesAsync(callers, state, index, token);
        }
    }
}
