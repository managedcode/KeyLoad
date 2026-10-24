using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.TimeSeries;

internal static class ScopedSampleIdentityPublicObservation
{
    internal static async Task ReadAsync<T>(RequestCqrsClusterCallers administrator,
        RequestCqrsClusterCallers reader, ScopedSampleIdentityPublicState state, string tool, object request,
        T expected, Func<CancellationToken, Task<ManagedCode.Communication.Result<T>>> read,
        CancellationToken token)
    {
        await SqlClusterProtocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await read(token)));
        await ScopedSampleIdentityPublicChecks.CurrentAsync(administrator, state, token);
        await SqlClusterProtocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<T>(
            await reader.Mcp.CallAsync(tool, request, token))).Value);
        await ScopedSampleIdentityPublicChecks.CurrentAsync(administrator, state, token);
    }

    internal static async Task ReplayAsync(RequestCqrsClusterCallers administrator,
        RequestCqrsClusterCallers reader, ScopedSampleIdentityPublicState state, CancellationToken token)
    {
        foreach (var (command, receipt) in state.Originals)
        {
            await SqlClusterProtocol.EqualAsync(receipt,
                await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(command, token)));
            await ScopedSampleIdentityPublicChecks.CurrentAsync(administrator, state, token);
            await ScopedSampleIdentityPublicChecks.HealthyAsync(administrator, reader, state, token);
            await SqlClusterProtocol.EqualAsync(receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await administrator.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token))).Value);
            await ScopedSampleIdentityPublicChecks.CurrentAsync(administrator, state, token);
            await ScopedSampleIdentityPublicChecks.HealthyAsync(administrator, reader, state, token);
        }
    }
}
