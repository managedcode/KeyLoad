using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.Messaging;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class EventingRequiredClaimClusterReceive
{
    internal static Task AcceptProbeAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        EventingRequiredClaimClusterState state, CancellationToken token)
        => EventingRequiredClaimClusterCurrent.ObserveAsync(root, state, async () =>
        {
            var actual = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.ReceiveAsync(state.EpochProbe, token));
            await Assert.That(actual.RequestId).IsEqualTo(state.EpochProbe.RequestId);
            await Assert.That(actual.Deliveries).IsEmpty();
            await Assert.That(actual.Token.Incarnation).IsEqualTo(state.Input.Producers[0].Receipt.Token.Incarnation);
            await Assert.That(actual.Token.AtomicPartitionId).IsEqualTo(state.Input.Partition.AtomicPartitionId);
            await Assert.That(actual.Token.Position).IsGreaterThan(state.Input.Producers[0].Receipt.Token.Position);
            state.EpochReceipt = actual;
        }, token);

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
