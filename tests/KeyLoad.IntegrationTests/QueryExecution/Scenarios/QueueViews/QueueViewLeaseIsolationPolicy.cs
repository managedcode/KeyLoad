using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class QueueViewLeaseIsolationPolicy
{
    internal static async Task RunAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        QueueViewLeaseIsolationState state, CancellationToken token)
    {
        await PersistAsync(root, state, false, QueueViewLeaseIsolationProtocol.DeniedEpoch, token);
        await QueueViewLeaseIsolationRefusals.DeniedAsync(root, reader, state, token);
        await PersistAsync(root, state, true, QueueViewLeaseIsolationProtocol.RepairedEpoch, token);
        await QueueViewLeaseIsolationReads.RunAsync(root, reader, state, token);
    }

    private static async Task PersistAsync(RequestCqrsClusterCallers root, QueueViewLeaseIsolationState state,
        bool inspect, long epoch, CancellationToken token)
    {
        var expected = state.Reader.Principal with
        {
            PolicyEpoch = epoch,
            Grants = [new(state.Scenario.Partition.DatabaseId, SqlModelViewClusterScenario.Queue,
                Capability.Query | (inspect ? Capability.QueueInspect : Capability.None))]
        };
        await SqlClusterProtocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
            await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token)));
    }
}
