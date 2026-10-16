using KeyLoad.UnitTests.Features.ClusterRouting;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class ReplicaForwardedOriginalWorkNodes
{
    internal static RequestCqrsCohortNativeNode[] All(RequestCqrsHostedRuntimeFixture fixture)
        => fixture.Cluster.Silos.OfType<InProcessSiloHandle>().Select(silo =>
            silo.ServiceProvider.GetRequiredService<RequestCqrsCohortNativeNode>()).ToArray();

    internal static async Task<RequestCqrsCohortNativeNode> LeaderAsync(RequestCqrsCohortNativeNode[] nodes,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            foreach (var node in nodes)
            {
                if (await node.Journal.Consensus.IsLeaderAsync(cancellationToken))
                {
                    return node;
                }
            }
            await Task.Delay(ReplicaForwardedOriginalWorkProtocol.LeaderPoll, TimeProvider.System, cancellationToken);
        }
    }

    internal static async Task BarrierAsync(RequestCqrsCohortNativeNode[] nodes, CancellationToken cancellationToken)
    {
        foreach (var node in nodes)
        {
            await node.Journal.Consensus.ReadBarrierAsync(cancellationToken);
        }
    }
}
