using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class TimeSeriesCurrentCapabilityRf3Refusal
{
    internal static async Task RunAsync(RequestCqrsRf3Callers reader, RequestCqrsRf3Callers administrator,
        TimeSeriesCurrentCapabilityRf3State state, CancellationToken token)
    {
        var before = await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.OutboxStatusAsync(state.Scenario.Partition, token));
        await CanonicalAsync(administrator, state, token);
        await DeniedAsync(await reader.Sdk.ReadSamplesAsync(state.Range(), token));
        await UnchangedAsync(administrator, state, before, token);
        await DeniedAsync(await reader.Sdk.ReadLatestSampleAsync(state.Scenario.Latest(), token));
        await UnchangedAsync(administrator, state, before, token);
        await DeniedAsync(await reader.Sdk.AggregateSamplesAsync(state.Aggregate(), token));
        await UnchangedAsync(administrator, state, before, token);
        await McpCallerAssertions.ErrorAsync(await reader.Mcp.CallAsync(McpCallerTools.SeriesRead,
            state.Range(), token), ErrorCode.PermissionDenied, dispatched: true);
        await UnchangedAsync(administrator, state, before, token);
        await McpCallerAssertions.ErrorAsync(await reader.Mcp.CallAsync(McpCallerTools.SeriesLatest,
            state.Scenario.Latest(), token), ErrorCode.PermissionDenied, dispatched: true);
        await UnchangedAsync(administrator, state, before, token);
        await McpCallerAssertions.ErrorAsync(await reader.Mcp.CallAsync(McpCallerTools.SeriesAggregate,
            state.Aggregate(), token), ErrorCode.PermissionDenied, dispatched: true);
        await UnchangedAsync(administrator, state, before, token);
        await CanonicalAsync(administrator, state, token);
        await SqlRf3Protocol.EqualAsync(before, await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.OutboxStatusAsync(state.Scenario.Partition, token)));
        await SqlRf3Protocol.EqualAsync(before, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await administrator.Mcp.CallAsync(McpCallerTools.OutboxStatus,
                new GetOutboxStatusRequest(state.Scenario.Partition), token))).Value);
    }

    internal static async Task FencedOriginalAsync(RequestCqrsRf3Callers reader,
        RequestCqrsRf3Callers administrator, TimeSeriesCurrentCapabilityRf3State state, CancellationToken token)
    {
        var before = await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.OutboxStatusAsync(state.Scenario.Partition, token));
        await CurrentAsync(reader, administrator, state, before, token);
        await DeniedAsync(await reader.Sdk.CommitAsync(state.ReaderCommand, token));
        await CurrentAsync(reader, administrator, state, before, token);
        await McpCallerAssertions.ErrorAsync(await reader.Mcp.CallAsync(McpCallerTools.DocumentsCommit,
            state.ReaderCommand, token), ErrorCode.PermissionDenied, dispatched: true);
        await CurrentAsync(reader, administrator, state, before, token);
    }

    internal static async Task CurrentAsync(RequestCqrsRf3Callers reader,
        RequestCqrsRf3Callers administrator, TimeSeriesCurrentCapabilityRf3State state,
        OutboxStatus before, CancellationToken token)
    {
        await TimeSeriesCurrentCapabilityRf3Assertions.HealthyAsync(reader, state, token);
        await UnchangedAsync(administrator, state, before, token);
    }

    private static async Task UnchangedAsync(RequestCqrsRf3Callers administrator,
        TimeSeriesCurrentCapabilityRf3State state, OutboxStatus before, CancellationToken token)
    {
        await CanonicalAsync(administrator, state, token);
        await SqlRf3Protocol.EqualAsync(before, await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.OutboxStatusAsync(state.Scenario.Partition, token)));
        await SqlRf3Protocol.EqualAsync(before, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await administrator.Mcp.CallAsync(McpCallerTools.OutboxStatus,
                new GetOutboxStatusRequest(state.Scenario.Partition), token))).Value);
    }

    private static async Task CanonicalAsync(RequestCqrsRf3Callers administrator,
        TimeSeriesCurrentCapabilityRf3State state, CancellationToken token)
        => await SqlRf3Protocol.EqualAsync(TimeSeriesCurrentCapabilityRf3Assertions.Expected(state.FreshCommand is not null, TimeSeriesRf3Scenario.PrivateTags),
            await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ReadSamplesAsync(state.Range(), token)));

    private static async Task DeniedAsync<T>(ManagedCode.Communication.Result<T> result)
    {
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Value).IsNull();
        await Assert.That(result.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
    }
}
