using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

internal static class DocumentLiveProfileRf3Calls
{
    internal static async Task<LiveQuerySnapshot> StartAsync(RequestCqrsRf3Callers reader, DocumentLiveProfileRf3State state,
        AstQueryRequest query, int route, CancellationToken token)
    {
        var request = new StartLiveQueryRequest(query);
        var sql = SqlRf3Protocol.Call(state.Partition, FeedLiveRf3Protocol.LiveStart, request);
        return route switch
        {
            0 => await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.StartLiveQueryAsync(request, token)),
            1 => (await McpCallerAssertions.SuccessAsync<LiveQuerySnapshot>(await reader.Mcp.CallAsync(FeedLiveRf3Protocol.LiveStart, request, token))).Value,
            2 => await SqlRf3Protocol.SdkAsync<LiveQuerySnapshot>(reader.Sdk, sql, token),
            _ => await SqlRf3Protocol.McpAsync<LiveQuerySnapshot>(reader.Mcp, sql, token)
        };
    }
    internal static async Task<LiveQueryPage> ReadAsync(RequestCqrsRf3Callers reader, DocumentLiveProfileRf3State state,
        ReadLiveQueryRequest request, int route, CancellationToken token)
    {
        var sql = SqlRf3Protocol.Call(state.Partition, FeedLiveRf3Protocol.LiveRead, request);
        return route switch
        {
            0 => await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.ReadLiveQueryAsync(request, token)),
            1 => (await McpCallerAssertions.SuccessAsync<LiveQueryPage>(await reader.Mcp.CallAsync(FeedLiveRf3Protocol.LiveRead, request, token))).Value,
            2 => await SqlRf3Protocol.SdkAsync<LiveQueryPage>(reader.Sdk, sql, token),
            _ => await SqlRf3Protocol.McpAsync<LiveQueryPage>(reader.Mcp, sql, token)
        };
    }
    internal static async Task CancelledAsync(RequestCqrsRf3Callers reader, DocumentLiveProfileRf3State state, CancellationToken token)
    {
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(token);
        await cancelled.CancelAsync();
        const string detail = "The read response is unavailable.";
        var start = new StartLiveQueryRequest(state.Query);
        var read = new ReadLiveQueryRequest(state.Query, state.OriginalCursor);
        await DocumentLiveProfileRf3Refusals.SdkAsync(await reader.Sdk.StartLiveQueryAsync(start, cancelled.Token), ErrorCode.Cancelled, detail);
        await DocumentLiveProfileRf3Refusals.SdkAsync(await reader.Sdk.ReadLiveQueryAsync(read, cancelled.Token), ErrorCode.Cancelled, detail);
        await DocumentLiveProfileRf3Refusals.SqlAsync(await reader.Sdk.ExecuteSqlAsync(
            SqlRf3Protocol.Call(state.Partition, FeedLiveRf3Protocol.LiveStart, start), cancelled.Token), ErrorCode.UnknownWriteOutcome, "The write response is unavailable. Retry the same command ID.");
        await DocumentLiveProfileRf3Refusals.SqlAsync(await reader.Sdk.ExecuteSqlAsync(
            SqlRf3Protocol.Call(state.Partition, FeedLiveRf3Protocol.LiveRead, read), cancelled.Token), ErrorCode.UnknownWriteOutcome, "The write response is unavailable. Retry the same command ID.");
        foreach (var route in new[] { 1, 3 })
        {
            var startFailure = await Assert.ThrowsAsync<OperationCanceledException>(() => StartAsync(reader, state, state.Query, route, cancelled.Token));
            var readFailure = await Assert.ThrowsAsync<OperationCanceledException>(() => ReadAsync(reader, state, read, route, cancelled.Token));
            await Assert.That(startFailure!.CancellationToken).IsEqualTo(cancelled.Token);
            await Assert.That(readFailure!.CancellationToken).IsEqualTo(cancelled.Token);
        }
    }
}
