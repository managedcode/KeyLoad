using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal static class EarlyAdmissionQualificationReplay
{
    internal static async Task RunAsync(RequestCqrsRf3Callers root, EarlyAdmissionQualificationState state,
        CancellationToken token)
    {
        await EarlyAdmissionQualificationCurrent.ObserveAsync(root, state, async () =>
            await SqlRf3Protocol.EqualAsync(state.Receipt, await McpCallerAssertions.SdkSuccessAsync(
                await root.Sdk.CommitAsync(state.Seed, token))), false, token);
        await EarlyAdmissionQualificationCurrent.ObserveAsync(root, state, async () =>
            await SqlRf3Protocol.EqualAsync(state.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await root.Mcp.CallAsync(McpCallerTools.DocumentsCommit, state.Seed, token))).Value), false, token);
    }
}
