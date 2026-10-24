using System.Text.Json;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.ChangeFeeds;

internal static class LivePageClusterAssertions
{
    internal static async Task RequireAsync(LiveQueryPage expected,
        ReadLiveQueryRequest original, Func<Task<long>> observeCut,
        Func<ReadLiveQueryRequest, Task<LiveQueryPage>> read)
    {
        var before = await observeCut();
        var actual = await read(original);
        var after = await observeCut();
        await LiveQueryCutClusterAssertions.BoundsAsync(before, actual.CutPosition, after);
        await Assert.That(string.IsNullOrEmpty(actual.Cursor)).IsFalse();
        await SqlClusterProtocol.EqualAsync(expected with
        {
            Cursor = actual.Cursor,
            CutPosition = actual.CutPosition
        }, actual);
        var continuationBefore = await observeCut();
        var continuation = await read(original with
        {
            Cursor = actual.Cursor
        });
        var continuationAfter = await observeCut();
        await LiveQueryCutClusterAssertions.BoundsAsync(continuationBefore, continuation.CutPosition, continuationAfter);
        await Assert.That(string.IsNullOrEmpty(continuation.Cursor)).IsFalse();
        await SqlClusterProtocol.EqualAsync(new LiveQueryPage([], continuation.Cursor,
            expected.ThroughSequence, false, continuation.CutPosition), continuation);
    }

    internal static async Task DeniedSqlAsync(RequestCqrsClusterCallers reader, SqlOperationRequest request,
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
