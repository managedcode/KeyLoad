using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;

namespace KeyLoad.ClusterTests.TimeSeries;

internal static class TimeSeriesAggregateScanBoundClusterTrial
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var state = await AdminAsync(fixture, async administrator =>
        {
            var seeded = await TimeSeriesAggregateScanBoundClusterSeed.CreateAsync(fixture, administrator, token);
            await ObserveAsync(fixture, administrator, seeded, token);
            return seeded;
        }, token);
        await TimeSeriesAggregateScanBoundClusterRestart.RunAsync(fixture, token);
        state = await AdminAsync(fixture, async administrator =>
        {
            await ObserveAsync(fixture, administrator, state, token);
            var appended = await TimeSeriesAggregateScanBoundClusterSeed.AppendAsync(administrator, state, token);
            await ObserveAsync(fixture, administrator, appended, token);
            return appended;
        }, token);
        await TimeSeriesAggregateScanBoundClusterRestart.RunAsync(fixture, token);
        _ = await AdminAsync(fixture, async administrator =>
        {
            await ObserveAsync(fixture, administrator, state, token);
            return true;
        }, token);
    }

    private static Task<T> AdminAsync<T>(ClusterFixture fixture,
        Func<RequestCqrsClusterCallers, Task<T>> operation, CancellationToken token)
        => RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, operation, token);

    private static Task<bool> ObserveAsync(ClusterFixture fixture, RequestCqrsClusterCallers administrator,
        TimeSeriesAggregateScanBoundClusterState state, CancellationToken token)
        => RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
            state.Identity.Secret, async reader =>
            {
                var baseline = await McpCallerAssertions.SdkSuccessAsync(
                    await administrator.Sdk.OutboxStatusAsync(state.Scenario.Partition, token));
                Func<Task> current = () => TimeSeriesAggregateScanBoundClusterAssertions.CurrentAsync(
                    reader, administrator, state, baseline, token);
                await TimeSeriesAggregateScanBoundClusterRefusal.RunAsync(reader, state, current, token);
                await TimeSeriesAggregateScanBoundClusterSeed.ReplayAsync(administrator, state.Command, state.Receipt, current, token);
                if (state.FreshCommand is not null)
                {
                    await TimeSeriesAggregateScanBoundClusterSeed.ReplayAsync(administrator, state.FreshCommand, state.FreshReceipt!, current, token);
                }
                await current();
                return true;
            }, token);
}
