using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

internal static class LivePageRf3Assertions
{
    internal static async Task RequireAsync(LiveQueryPage actual, LiveQueryPage expected,
        ReadLiveQueryRequest original, Func<ReadLiveQueryRequest, Task<LiveQueryPage>> read)
    {
        await Assert.That(string.IsNullOrEmpty(actual.Cursor)).IsFalse();
        await SqlRf3Protocol.EqualAsync(expected with { Cursor = actual.Cursor }, actual);
        var continuation = await read(original with { Cursor = actual.Cursor });
        await Assert.That(string.IsNullOrEmpty(continuation.Cursor)).IsFalse();
        await SqlRf3Protocol.EqualAsync(new LiveQueryPage([], continuation.Cursor,
            expected.ThroughSequence, false, expected.CutPosition), continuation);
    }

    internal static async Task DeniedSqlAsync(RequestCqrsRf3Callers reader, SqlOperationRequest request,
        ErrorCode expected, CancellationToken token)
    {
        var sdk = await reader.Sdk.ExecuteSqlAsync(request, token);
        await Assert.That(sdk.IsSuccess).IsFalse();
        await Assert.That(sdk.Problem!.ErrorCode).IsEqualTo(expected.ToString());
        await Assert.That(sdk.Value.ValueKind).IsEqualTo(JsonValueKind.Undefined);
        await McpCallerAssertions.ErrorAsync(await reader.Mcp.CallAsync(SqlOperationProtocol.ToolName,
            request, token), expected, dispatched: true);
    }
}
