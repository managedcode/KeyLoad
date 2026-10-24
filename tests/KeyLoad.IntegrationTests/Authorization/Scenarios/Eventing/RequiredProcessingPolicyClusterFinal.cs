using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.Messaging;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.Authorization;

internal static class RequiredProcessingPolicyClusterFinal
{
    internal static Task RunAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader, RequiredProcessingPolicyClusterState state, CancellationToken token)
        => RequiredProcessingPolicyClusterValues.ObserveAsync(root, state, 3, async () =>
        {
            await RequiredProcessingPolicyClusterRefusals.AllAsync(reader, state, state.Original, RequiredProcessingPolicyClusterState.EpochChanged, token);
            await RequiredProcessingPolicyClusterRefusals.AllAsync(reader, state, state.Refused, RequiredProcessingPolicyClusterState.EpochChanged, token);
            await ReplayProcessingAsync(root, reader, state, state.Healthy, state.HealthyReceipt, 3, token);
            foreach (var producer in state.Producers)
            {
                for (var route = 0; route < 4; route++)
                {
                    await SqlClusterProtocol.EqualAsync(producer.Receipt, await QueueLifecyclePublicRoutes.CallAsync(root, route, state.Partition,
                        McpCallerTools.DocumentsCommit, producer.Request, () => root.Sdk.CommitAsync(producer.Request, token), token));
                }
            }
        }, token);
    internal static async Task ReplayProcessingAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        RequiredProcessingPolicyClusterState state, ProcessingRequest request, CommitReceipt receipt, int phase, CancellationToken token)
    {
        for (var route = 0; route < 4; route++)
        {
            await RequiredProcessingPolicyClusterValues.ObserveAsync(root, state, phase, async () =>
                await SqlClusterProtocol.EqualAsync(receipt, await RequiredProcessingPolicyClusterRoutes.ProcessAsync(reader, state, request, route, token)), token);
        }
    }
}
