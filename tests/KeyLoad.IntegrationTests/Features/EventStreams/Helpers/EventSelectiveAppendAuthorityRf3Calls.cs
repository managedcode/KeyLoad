using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class EventSelectiveAppendAuthorityRf3Calls
{
    internal static async Task<(StreamPage Sdk, StreamPage Mcp)> ReadAsync(
        RequestCqrsRf3Callers callers, ReadStreamRequest request, Func<Task> observe,
        CancellationToken token)
    {
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadStreamAsync(request, token));
        await observe();
        var mcp = await McpCallerAssertions.SuccessAsync<StreamPage>(
            await callers.Mcp.CallAsync(McpCallerTools.StreamsRead, request, token));
        await observe();
        return (sdk, mcp.Value);
    }

    internal static async Task ReplayAsync(RequestCqrsRf3Callers administrator,
        CommandRequest command, CommitReceipt original, Func<Task> observe, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(original,
            await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(command, token)));
        await observe();
        var replay = await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await administrator.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token));
        await SqlRf3Protocol.EqualAsync(original, replay.Value);
        await observe();
    }
}
