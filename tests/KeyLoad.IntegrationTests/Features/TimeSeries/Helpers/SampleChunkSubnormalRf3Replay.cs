using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleChunkSubnormalRf3Replay
{
    internal static async Task RunAsync(SampleChunkSubnormalRf3State original,
        KeyLoadClient sdk, McpOfficialClient mcp, CommitReceipt minimum, bool healthy,
        CancellationToken token)
    {
        async Task ObserveAsync()
        {
            if (healthy)
            { await SampleChunkSubnormalRf3Results.HealthyAsync(original, sdk, mcp, minimum, token); }
            else
            { await SampleChunkSubnormalRf3Results.CurrentAsync(original, sdk, mcp, minimum, token); }
        }
        await ObserveAsync();
        foreach (var (command, receipt) in original.Originals)
        { await SampleChunkSubnormalRf3Receipts.ReplayAsync(sdk, mcp, command, receipt, ObserveAsync, token); }
    }
}
