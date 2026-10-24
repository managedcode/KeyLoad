using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.ResourceExecution;

internal static class EarlyAdmissionQualificationReplay
{
    internal static async Task RunAsync(RequestCqrsClusterCallers root, EarlyAdmissionQualificationState state,
        CancellationToken token)
    {
        await EarlyAdmissionQualificationCurrent.ObserveAsync(root, state, async () =>
            await SqlClusterProtocol.EqualAsync(state.Receipt, await McpCallerAssertions.SdkSuccessAsync(
                await root.Sdk.CommitAsync(state.Seed, token))), false, token);
        await EarlyAdmissionQualificationCurrent.ObserveAsync(root, state, async () =>
            await SqlClusterProtocol.EqualAsync(state.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await root.Mcp.CallAsync(McpCallerTools.DocumentsCommit, state.Seed, token))).Value), false, token);
    }
}
