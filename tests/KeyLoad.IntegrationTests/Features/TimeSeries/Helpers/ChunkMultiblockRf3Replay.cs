using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class ChunkMultiblockRf3Replay
{
    internal static async Task RunAsync(RequestCqrsRf3Callers administrator, ChunkMultiblockRf3State state,
        CommandRequest command, CommitReceipt expected, CancellationToken token)
    {
        await RouteAsync(administrator, state, expected, async () => await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.CommitAsync(command, token)), token);
        await RouteAsync(administrator, state, expected, async () => (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await administrator.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token))).Value, token);
        var call = SqlRf3Protocol.Call(command.Partition, McpCallerTools.DocumentsCommit, command);
        await RouteAsync(administrator, state, expected, () => SqlRf3Protocol.SdkAsync<CommitReceipt>(administrator.Sdk, call, token), token);
        await RouteAsync(administrator, state, expected, () => SqlRf3Protocol.McpAsync<CommitReceipt>(administrator.Mcp, call, token), token);
    }

    private static async Task RouteAsync(RequestCqrsRf3Callers administrator, ChunkMultiblockRf3State state,
        CommitReceipt expected, Func<Task<CommitReceipt>> read, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var actual = await read();
            await ChunkMultiblockRf3Receipt.PositiveAsync(actual);
            await SampleChunkRetentionRf3ReadOracle.EqualAsync(actual, expected);
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => ChunkMultiblockRf3Current.RequireAsync(administrator, administrator, state, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
