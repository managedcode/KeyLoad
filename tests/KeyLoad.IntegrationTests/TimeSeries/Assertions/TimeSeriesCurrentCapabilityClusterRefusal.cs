using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class TimeSeriesCurrentCapabilityClusterRefusal
{
    internal static async Task RunAsync(RequestCqrsClusterCallers reader, RequestCqrsClusterCallers administrator,
        TimeSeriesCurrentCapabilityClusterState state, CancellationToken token)
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
        await SqlClusterProtocol.EqualAsync(before, await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.OutboxStatusAsync(state.Scenario.Partition, token)));
        await SqlClusterProtocol.EqualAsync(before, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await administrator.Mcp.CallAsync(McpCallerTools.OutboxStatus,
                new GetOutboxStatusRequest(state.Scenario.Partition), token))).Value);
    }

    internal static async Task FencedOriginalAsync(RequestCqrsClusterCallers reader,
        RequestCqrsClusterCallers administrator, TimeSeriesCurrentCapabilityClusterState state, CancellationToken token)
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

    internal static async Task CurrentAsync(RequestCqrsClusterCallers reader,
        RequestCqrsClusterCallers administrator, TimeSeriesCurrentCapabilityClusterState state,
        OutboxStatus before, CancellationToken token)
    {
        await TimeSeriesCurrentCapabilityClusterAssertions.HealthyAsync(reader, state, token);
        await UnchangedAsync(administrator, state, before, token);
    }

    private static async Task UnchangedAsync(RequestCqrsClusterCallers administrator,
        TimeSeriesCurrentCapabilityClusterState state, OutboxStatus before, CancellationToken token)
    {
        await CanonicalAsync(administrator, state, token);
        await SqlClusterProtocol.EqualAsync(before, await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.OutboxStatusAsync(state.Scenario.Partition, token)));
        await SqlClusterProtocol.EqualAsync(before, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await administrator.Mcp.CallAsync(McpCallerTools.OutboxStatus,
                new GetOutboxStatusRequest(state.Scenario.Partition), token))).Value);
    }

    private static async Task CanonicalAsync(RequestCqrsClusterCallers administrator,
        TimeSeriesCurrentCapabilityClusterState state, CancellationToken token)
        => await SqlClusterProtocol.EqualAsync(TimeSeriesCurrentCapabilityClusterAssertions.Expected(state.FreshCommand is not null, TimeSeriesClusterScenario.PrivateTags),
            await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ReadSamplesAsync(state.Range(), token)));

    private static async Task DeniedAsync<T>(ManagedCode.Communication.Result<T> result)
    {
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Value).IsNull();
        await Assert.That(result.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
    }
}
