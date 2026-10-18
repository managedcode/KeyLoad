using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

internal static class DocumentLiveProfileClusterCalls
{
    internal static async Task<LiveQuerySnapshot> StartAsync(RequestCqrsClusterCallers reader, DocumentLiveProfileClusterState state,
        AstQueryRequest query, int route, CancellationToken token)
    {
        var request = new StartLiveQueryRequest(query);
        var sql = SqlClusterProtocol.Call(state.Partition, FeedLiveClusterProtocol.LiveStart, request);
        return route switch
        {
            0 => await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.StartLiveQueryAsync(request, token)),
            1 => (await McpCallerAssertions.SuccessAsync<LiveQuerySnapshot>(await reader.Mcp.CallAsync(FeedLiveClusterProtocol.LiveStart, request, token))).Value,
            2 => await SqlClusterProtocol.SdkAsync<LiveQuerySnapshot>(reader.Sdk, sql, token),
            _ => await SqlClusterProtocol.McpAsync<LiveQuerySnapshot>(reader.Mcp, sql, token)
        };
    }
    internal static async Task<LiveQueryPage> ReadAsync(RequestCqrsClusterCallers reader, DocumentLiveProfileClusterState state,
        ReadLiveQueryRequest request, int route, CancellationToken token)
    {
        var sql = SqlClusterProtocol.Call(state.Partition, FeedLiveClusterProtocol.LiveRead, request);
        return route switch
        {
            0 => await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.ReadLiveQueryAsync(request, token)),
            1 => (await McpCallerAssertions.SuccessAsync<LiveQueryPage>(await reader.Mcp.CallAsync(FeedLiveClusterProtocol.LiveRead, request, token))).Value,
            2 => await SqlClusterProtocol.SdkAsync<LiveQueryPage>(reader.Sdk, sql, token),
            _ => await SqlClusterProtocol.McpAsync<LiveQueryPage>(reader.Mcp, sql, token)
        };
    }
    internal static async Task CancelledAsync(RequestCqrsClusterCallers reader, DocumentLiveProfileClusterState state, CancellationToken token)
    {
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(token);
        await cancelled.CancelAsync();
        const string detail = "The read response is unavailable.";
        var start = new StartLiveQueryRequest(state.Query);
        var read = new ReadLiveQueryRequest(state.Query, state.OriginalCursor);
        await DocumentLiveProfileClusterRefusals.SdkAsync(await reader.Sdk.StartLiveQueryAsync(start, cancelled.Token), ErrorCode.Cancelled, detail);
        await DocumentLiveProfileClusterRefusals.SdkAsync(await reader.Sdk.ReadLiveQueryAsync(read, cancelled.Token), ErrorCode.Cancelled, detail);
        await DocumentLiveProfileClusterRefusals.SqlAsync(await reader.Sdk.ExecuteSqlAsync(
            SqlClusterProtocol.Call(state.Partition, FeedLiveClusterProtocol.LiveStart, start), cancelled.Token), ErrorCode.UnknownWriteOutcome, "The write response is unavailable. Retry the same command ID.");
        await DocumentLiveProfileClusterRefusals.SqlAsync(await reader.Sdk.ExecuteSqlAsync(
            SqlClusterProtocol.Call(state.Partition, FeedLiveClusterProtocol.LiveRead, read), cancelled.Token), ErrorCode.UnknownWriteOutcome, "The write response is unavailable. Retry the same command ID.");
        foreach (var route in new[] { 1, 3 })
        {
            var startFailure = await Assert.ThrowsAsync<OperationCanceledException>(() => StartAsync(reader, state, state.Query, route, cancelled.Token));
            var readFailure = await Assert.ThrowsAsync<OperationCanceledException>(() => ReadAsync(reader, state, read, route, cancelled.Token));
            await Assert.That(startFailure!.CancellationToken).IsEqualTo(cancelled.Token);
            await Assert.That(readFailure!.CancellationToken).IsEqualTo(cancelled.Token);
        }
    }
}
