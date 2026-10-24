using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.TimeSeries;

internal static class SampleChunkSubnormalClusterReplay
{
    internal static async Task RunAsync(SampleChunkSubnormalClusterState original,
        KeyLoadClient sdk, McpOfficialClient mcp, CommitReceipt minimum, bool healthy,
        CancellationToken token)
    {
        async Task ObserveAsync()
        {
            if (healthy)
            {
                await SampleChunkSubnormalClusterResults.HealthyAsync(original, sdk, mcp, minimum, token);
            }
            else
            {
                await SampleChunkSubnormalClusterResults.CurrentAsync(original, sdk, mcp, minimum, token);
            }
        }
        await ObserveAsync();
        foreach (var (command, receipt) in original.Originals)
        {
            await SampleChunkSubnormalClusterReceipts.ReplayAsync(sdk, mcp, command, receipt, ObserveAsync, token);
        }
    }
}
