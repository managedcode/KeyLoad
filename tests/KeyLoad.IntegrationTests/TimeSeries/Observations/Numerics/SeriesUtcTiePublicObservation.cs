using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SeriesUtcTiePublicObservation
{
    internal static async Task ReadAsync<T>(RequestCqrsRf3Callers administrator,
        RequestCqrsRf3Callers caller, SeriesUtcTiePublicState state, string tool, object request, T expected,
        Func<CancellationToken, Task<ManagedCode.Communication.Result<T>>> read, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await read(token)));
        await OutboxAsync(administrator, state, token);
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<T>(await caller.Mcp.CallAsync(tool, request, token))).Value);
        await OutboxAsync(administrator, state, token);
    }

    internal static async Task OutboxAsync(RequestCqrsRf3Callers administrator, SeriesUtcTiePublicState state, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(state.Outbox, await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(state.Scenario.Partition, token)));
        await SqlRf3Protocol.EqualAsync(state.Outbox, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await administrator.Mcp.CallAsync(McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(state.Scenario.Partition), token))).Value);
    }

    internal static async Task ReplayAsync(RequestCqrsRf3Callers administrator,
        RequestCqrsRf3Callers reader, SeriesUtcTiePublicState state, CancellationToken token)
    {
        foreach (var (command, receipt) in state.Receipts)
        {
            await SqlRf3Protocol.EqualAsync(receipt, await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(command, token)));
            await SeriesUtcTiePublicAssertions.RequireAsync(administrator, reader, state, token);
            await SqlRf3Protocol.EqualAsync(receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await administrator.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token))).Value);
            await SeriesUtcTiePublicAssertions.RequireAsync(administrator, reader, state, token);
        }
    }
}
