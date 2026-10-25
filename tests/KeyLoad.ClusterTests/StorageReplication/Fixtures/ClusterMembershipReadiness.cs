using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.StorageReplication;

/// <summary>Waits until the Orleans membership that each listed node reports holds exactly the listed silos.</summary>
/// <remarks>After an abrupt kill, Orleans keeps routing calls to activations on the lost silo until its membership
/// removes that silo, and a read reports such an Orleans RPC failure as OwnershipLost (OrleansExecution REQ-ROUTE-009).
/// A status read takes the same route, so OwnershipLost only means that the change is not observed yet; any other
/// refusal fails the caller. A rejoined silo is a write owner only once every node reports it active.</remarks>
internal static class ClusterMembershipReadiness
{
    /// <summary>Polls every listed node through its own endpoint until each reports the listed nodes as its active silos.</summary>
    /// <param name="fixture">The real Aspire cluster.</param>
    /// <param name="nodes">Every node expected to be an active silo.</param>
    /// <param name="cancellationToken">The test operation lifetime.</param>
    /// <returns>The status each node reported when the membership was observed.</returns>
    internal static async Task<NodeStatus[]> WaitForActiveSilosAsync(ClusterFixture fixture, IReadOnlyList<string> nodes,
        CancellationToken cancellationToken)
    {
        var connections = nodes.Select(node => McpCallerHttp.Create(fixture, node)).ToArray();
        try
        {
            return await WaitForActiveSilosAsync([.. connections.Select(http => new KeyLoadClient(http, fixture.AdminKey,
                IntegrationClientOptions.Execution()))], cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            foreach (var connection in connections)
            {
                connection.Dispose();
            }
        }
    }

    /// <summary>Polls one administrator per active node until each reports as many active silos as there are callers.</summary>
    /// <param name="administrators">One administrator client per node expected to be an active silo.</param>
    /// <param name="cancellationToken">The test operation lifetime.</param>
    /// <returns>The status each node reported when the membership was observed.</returns>
    internal static async Task<NodeStatus[]> WaitForActiveSilosAsync(IReadOnlyList<KeyLoadClient> administrators,
        CancellationToken cancellationToken)
    {
        var observed = new NodeStatus[administrators.Count];
        await ClusterReplicationTestSupport.EventuallyAsync(async () =>
        {
            for (var index = 0; index < administrators.Count; index++)
            {
                var reply = await administrators[index].StatusAsync(cancellationToken).ConfigureAwait(false);
                if (reply.IsFailed && reply.Problem.ErrorCode == nameof(ErrorCode.OwnershipLost))
                {
                    return false;
                }
                observed[index] = ClusterReplicationTestSupport.Success(reply);
            }
            return observed.All(status => status.RoutingReady && status.SiloCount == administrators.Count);
        }, cancellationToken).ConfigureAwait(false);
        return observed;
    }
}
