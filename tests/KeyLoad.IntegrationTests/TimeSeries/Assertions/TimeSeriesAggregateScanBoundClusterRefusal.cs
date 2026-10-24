using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.TimeSeries;

internal static class TimeSeriesAggregateScanBoundClusterRefusal
{
    internal static async Task RunAsync(RequestCqrsClusterCallers reader,
        TimeSeriesAggregateScanBoundClusterState state, Func<Task> current, CancellationToken token)
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
        SampleRecord[] first = [TimeSeriesAggregateScanBoundClusterAssertions.Expected(
            state.FreshCommand is not null, TimeSeriesClusterScenario.ProjectedTags)[0]];
        await SqlClusterProtocol.EqualAsync(first,
            await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.ReadSamplesAsync(state.Range(1), token)));
        await current();
        await SqlClusterProtocol.EqualAsync(first, (await McpCallerAssertions.SuccessAsync<SampleRecord[]>(
            await reader.Mcp.CallAsync(McpCallerTools.SeriesRead, state.Range(1), token))).Value);
        await current();
    }
}
