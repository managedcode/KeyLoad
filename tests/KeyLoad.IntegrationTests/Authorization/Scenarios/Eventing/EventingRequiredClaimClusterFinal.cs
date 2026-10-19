using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class EventingRequiredClaimClusterFinal
{
    internal static async Task RunAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        EventingRequiredClaimClusterState state, CancellationToken token)
    {
        await EventingRequiredClaimClusterRefusals.ReceiveAsync(root, reader, state, ErrorCode.PermissionDenied,
            RequiredProcessingPolicyClusterState.EpochChanged, token);
        for (var route = 0; route < 4; route++)
        {
            await EventingRequiredClaimClusterCurrent.ObserveAsync(root, state, async () => await SqlClusterProtocol.EqualAsync(
                state.Receipt, await RequiredProcessingPolicyClusterRoutes.ProcessAsync(reader, state.Input, state.Healthy, route, token)), token);
        }
        await EventingRequiredClaimClusterTenant.RefuseAsync(root, reader, state, token);
        await EventingRequiredClaimClusterExport.RefuseAsync(root, reader, state, token);
        foreach (var producer in state.Input.Producers)
        {
            await EventingRequiredClaimClusterCurrent.ObserveAsync(root, state, async () =>
            {
                await SqlClusterProtocol.EqualAsync(producer.Receipt,
                    await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(producer.Request, token)));
                await SqlClusterProtocol.EqualAsync(producer.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                    await root.Mcp.CallAsync(McpCallerTools.DocumentsCommit, producer.Request, token))).Value);
            }, token);
        }
    }
}
