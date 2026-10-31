using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.Messaging;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.QueryExecution;

namespace KeyLoad.ClusterTests.Authorization;

internal static class EventingRequiredClaimClusterReceive
{
    internal static async Task ReplayAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        EventingRequiredClaimClusterState state, ReceiveRequest request, ReceiveResult expected, CancellationToken token)
    {
        for (var route = 0; route < 4; route++)
        {
            await EventingRequiredClaimClusterCurrent.ObserveAsync(root, state, async () =>
                await SqlClusterProtocol.EqualAsync(expected, await QueueLifecyclePublicRoutes.CallAsync(reader, route,
                    state.Input.Partition, McpCallerTools.MessagesReceive, request, () => reader.Sdk.ReceiveAsync(request, token), token)), token);
        }
    }
}
