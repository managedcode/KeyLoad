using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleRetentionClusterRecoveryAssertions
{
    internal static async Task VerifyAllNodesAsync(KeyLoadClient[] clients, McpOfficialClient mcp,
        TimeSeriesClusterScenario scenario, SampleRetentionStatus status, string[] expectedIds,
        CancellationToken cancellationToken)
    {
        foreach (var client in clients)
        {
            await SampleRetentionClusterCallerOracle.WaitForNodeAsync(client, scenario, status,
                expectedIds, cancellationToken);
            await SampleRetentionClusterCallerOracle.VerifyClientAsync(client, scenario, status,
                expectedIds, cancellationToken);
        }
        await SampleRetentionClusterCallerOracle.VerifyMcpAsync(mcp, scenario, status,
            expectedIds, cancellationToken);
    }

    internal static async Task VerifyActiveNodesAsync(KeyLoadClient[] clients, int stoppedIndex,
        McpOfficialClient mcp, TimeSeriesClusterScenario scenario, SampleRetentionStatus status,
        string[] expectedIds, CancellationToken cancellationToken)
    {
        foreach (var index in Enumerable.Range(0, clients.Length).Where(index => index != stoppedIndex))
        {
            await SampleRetentionClusterCallerOracle.WaitForNodeAsync(clients[index], scenario, status,
                expectedIds, cancellationToken);
            await SampleRetentionClusterCallerOracle.VerifyClientAsync(clients[index], scenario, status,
                expectedIds, cancellationToken);
        }
        await SampleRetentionClusterCallerOracle.VerifyMcpAsync(mcp, scenario, status,
            expectedIds, cancellationToken);
    }
}
