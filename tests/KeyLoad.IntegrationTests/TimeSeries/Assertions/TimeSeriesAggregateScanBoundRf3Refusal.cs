using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class TimeSeriesAggregateScanBoundRf3Refusal
{
    internal static async Task RunAsync(RequestCqrsRf3Callers reader,
        TimeSeriesAggregateScanBoundRf3State state, Func<Task> current, CancellationToken token)
    {
        await current();
        var denied = await reader.Sdk.AggregateSamplesAsync(state.Aggregate(1), token);
        await Assert.That(denied.IsFailed).IsTrue();
        await Assert.That(denied.Value).IsNull();
        await Assert.That(denied.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.BudgetExceeded));
        await current();
        await McpCallerAssertions.ErrorAsync(await reader.Mcp.CallAsync(McpCallerTools.SeriesAggregate,
            state.Aggregate(1), token), ErrorCode.BudgetExceeded, dispatched: true);
        await current();
        SampleRecord[] first = [TimeSeriesAggregateScanBoundRf3Assertions.Expected(
            state.FreshCommand is not null, TimeSeriesRf3Scenario.ProjectedTags)[0]];
        await SqlRf3Protocol.EqualAsync(first,
            await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.ReadSamplesAsync(state.Range(1), token)));
        await current();
        await SqlRf3Protocol.EqualAsync(first, (await McpCallerAssertions.SuccessAsync<SampleRecord[]>(
            await reader.Mcp.CallAsync(McpCallerTools.SeriesRead, state.Range(1), token))).Value);
        await current();
    }
}
