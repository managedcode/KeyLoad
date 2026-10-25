using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.QueryExecution;

namespace KeyLoad.ClusterTests.TimeSeries;

internal static class SeriesRetentionAtomicPublicReplay
{
    internal static async Task AllAsync(RequestCqrsClusterCallers administrator, RequestCqrsClusterCallers reader,
        SeriesRetentionAtomicPublicState state, CancellationToken token)
    {
        foreach (var original in state.Originals)
        {
            await SqlClusterProtocol.EqualAsync(original.Receipt,
                await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(original.Command, token)));
            await SeriesRetentionAtomicPublicCurrent.AssertAsync(administrator, reader, state, token);
            await SqlClusterProtocol.EqualAsync(original.Receipt,
                (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                    await administrator.Mcp.CallAsync(McpCallerTools.DocumentsCommit, original.Command, token))).Value);
            await SeriesRetentionAtomicPublicCurrent.AssertAsync(administrator, reader, state, token);
        }
    }
}
