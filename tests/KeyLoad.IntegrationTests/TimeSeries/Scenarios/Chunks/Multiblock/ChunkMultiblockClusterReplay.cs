using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.TimeSeries;

internal static class ChunkMultiblockClusterReplay
{
    internal static async Task RunAsync(RequestCqrsClusterCallers administrator, ChunkMultiblockClusterState state,
        CommandRequest command, CommitReceipt expected, CancellationToken token)
    {
        await RouteAsync(administrator, state, expected, async () => await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.CommitAsync(command, token)), token);
        await RouteAsync(administrator, state, expected, async () => (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await administrator.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token))).Value, token);
        var call = SqlClusterProtocol.Call(command.Partition, McpCallerTools.DocumentsCommit, command);
        await RouteAsync(administrator, state, expected, () => SqlClusterProtocol.SdkAsync<CommitReceipt>(administrator.Sdk, call, token), token);
        await RouteAsync(administrator, state, expected, () => SqlClusterProtocol.McpAsync<CommitReceipt>(administrator.Mcp, call, token), token);
    }

    private static async Task RouteAsync(RequestCqrsClusterCallers administrator, ChunkMultiblockClusterState state,
        CommitReceipt expected, Func<Task<CommitReceipt>> read, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var actual = await read();
            await ChunkMultiblockClusterReceipt.PositiveAsync(actual);
            await SampleChunkRetentionClusterReadOracle.EqualAsync(actual, expected);
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => ChunkMultiblockClusterCurrent.RequireAsync(administrator, administrator, state, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
