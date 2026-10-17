using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class ScopedSampleIdentityPublicObservation
{
    internal static async Task ReadAsync<T>(RequestCqrsRf3Callers administrator,
        RequestCqrsRf3Callers reader, ScopedSampleIdentityPublicState state, string tool, object request,
        T expected, Func<CancellationToken, Task<ManagedCode.Communication.Result<T>>> read,
        CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await read(token)));
        await ScopedSampleIdentityPublicChecks.CurrentAsync(administrator, state, token);
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<T>(
            await reader.Mcp.CallAsync(tool, request, token))).Value);
        await ScopedSampleIdentityPublicChecks.CurrentAsync(administrator, state, token);
    }

    internal static async Task ReplayAsync(RequestCqrsRf3Callers administrator,
        RequestCqrsRf3Callers reader, ScopedSampleIdentityPublicState state, CancellationToken token)
    {
        foreach (var (command, receipt) in state.Originals)
        {
            await SqlRf3Protocol.EqualAsync(receipt,
                await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(command, token)));
            await ScopedSampleIdentityPublicChecks.CurrentAsync(administrator, state, token);
            await ScopedSampleIdentityPublicChecks.HealthyAsync(administrator, reader, state, token);
            await SqlRf3Protocol.EqualAsync(receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await administrator.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token))).Value);
            await ScopedSampleIdentityPublicChecks.CurrentAsync(administrator, state, token);
            await ScopedSampleIdentityPublicChecks.HealthyAsync(administrator, reader, state, token);
        }
    }
}
