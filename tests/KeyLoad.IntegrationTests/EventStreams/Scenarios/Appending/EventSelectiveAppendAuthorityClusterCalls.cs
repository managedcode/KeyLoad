using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.EventStreams;

internal static class EventSelectiveAppendAuthorityClusterCalls
{
    internal static async Task<(StreamPage Sdk, StreamPage Mcp)> ReadAsync(
        RequestCqrsClusterCallers callers, ReadStreamRequest request, Func<Task> observe,
        CancellationToken token)
    {
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadStreamAsync(request, token));
        await observe();
        var mcp = await McpCallerAssertions.SuccessAsync<StreamPage>(
            await callers.Mcp.CallAsync(McpCallerTools.StreamsRead, request, token));
        await observe();
        return (sdk, mcp.Value);
    }

    internal static async Task ReplayAsync(RequestCqrsClusterCallers administrator,
        CommandRequest command, CommitReceipt original, Func<Task> observe, CancellationToken token)
    {
        await SqlClusterProtocol.EqualAsync(original,
            await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(command, token)));
        await observe();
        var replay = await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await administrator.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token));
        await SqlClusterProtocol.EqualAsync(original, replay.Value);
        await observe();
    }
}
