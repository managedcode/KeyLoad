using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class EventingRequiredClaimRf3Final
{
    internal static async Task RunAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        EventingRequiredClaimRf3State state, CancellationToken token)
    {
        await EventingRequiredClaimRf3Refusals.ReceiveAsync(root, reader, state, ErrorCode.PermissionDenied,
            RequiredProcessingPolicyRf3State.EpochChanged, token);
        for (var route = 0; route < 4; route++)
        {
            await EventingRequiredClaimRf3Current.ObserveAsync(root, state, async () => await SqlRf3Protocol.EqualAsync(
                state.Receipt, await RequiredProcessingPolicyRf3Routes.ProcessAsync(reader, state.Input, state.Healthy, route, token)), token);
        }
        await EventingRequiredClaimRf3Tenant.RefuseAsync(root, reader, state, token);
        await EventingRequiredClaimRf3Export.RefuseAsync(root, reader, state, token);
        foreach (var producer in state.Input.Producers)
        {
            await EventingRequiredClaimRf3Current.ObserveAsync(root, state, async () =>
            {
                await SqlRf3Protocol.EqualAsync(producer.Receipt,
                    await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(producer.Request, token)));
                await SqlRf3Protocol.EqualAsync(producer.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                    await root.Mcp.CallAsync(McpCallerTools.DocumentsCommit, producer.Request, token))).Value);
            }, token);
        }
    }
}
