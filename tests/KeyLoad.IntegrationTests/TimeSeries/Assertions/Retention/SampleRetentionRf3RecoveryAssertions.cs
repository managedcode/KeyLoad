using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleRetentionRf3RecoveryAssertions
{
    internal static async Task VerifyAllNodesAsync(KeyLoadClient[] clients, McpOfficialClient mcp,
        TimeSeriesRf3Scenario scenario, SampleRetentionStatus status, string[] expectedIds,
        CancellationToken cancellationToken)
    {
        foreach (var client in clients)
        {
            await SampleRetentionRf3CallerOracle.WaitForNodeAsync(client, scenario, status,
                expectedIds, cancellationToken);
            await SampleRetentionRf3CallerOracle.VerifyClientAsync(client, scenario, status,
                expectedIds, cancellationToken);
        }
        await SampleRetentionRf3CallerOracle.VerifyMcpAsync(mcp, scenario, status,
            expectedIds, cancellationToken);
    }

    internal static async Task VerifyActiveNodesAsync(KeyLoadClient[] clients, int stoppedIndex,
        McpOfficialClient mcp, TimeSeriesRf3Scenario scenario, SampleRetentionStatus status,
        string[] expectedIds, CancellationToken cancellationToken)
    {
        foreach (var index in Enumerable.Range(0, clients.Length).Where(index => index != stoppedIndex))
        {
            await SampleRetentionRf3CallerOracle.WaitForNodeAsync(clients[index], scenario, status,
                expectedIds, cancellationToken);
            await SampleRetentionRf3CallerOracle.VerifyClientAsync(clients[index], scenario, status,
                expectedIds, cancellationToken);
        }
        await SampleRetentionRf3CallerOracle.VerifyMcpAsync(mcp, scenario, status,
            expectedIds, cancellationToken);
    }
}
