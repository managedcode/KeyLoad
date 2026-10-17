using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class TimeSeriesAggregateScanBoundRf3Trial
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var state = await AdminAsync(fixture, async administrator =>
        {
            var seeded = await TimeSeriesAggregateScanBoundRf3Seed.CreateAsync(fixture, administrator, token);
            await ObserveAsync(fixture, administrator, seeded, token);
            return seeded;
        }, token);
        await TimeSeriesAggregateScanBoundRf3Restart.RunAsync(fixture, token);
        state = await AdminAsync(fixture, async administrator =>
        {
            await ObserveAsync(fixture, administrator, state, token);
            var appended = await TimeSeriesAggregateScanBoundRf3Seed.AppendAsync(administrator, state, token);
            await ObserveAsync(fixture, administrator, appended, token);
            return appended;
        }, token);
        await TimeSeriesAggregateScanBoundRf3Restart.RunAsync(fixture, token);
        _ = await AdminAsync(fixture, async administrator =>
        {
            await ObserveAsync(fixture, administrator, state, token);
            return true;
        }, token);
    }

    private static Task<T> AdminAsync<T>(ClusterFixture fixture,
        Func<RequestCqrsRf3Callers, Task<T>> operation, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, operation, token);

    private static Task<bool> ObserveAsync(ClusterFixture fixture, RequestCqrsRf3Callers administrator,
        TimeSeriesAggregateScanBoundRf3State state, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
            state.Identity.Secret, async reader =>
            {
                var baseline = await McpCallerAssertions.SdkSuccessAsync(
                    await administrator.Sdk.OutboxStatusAsync(state.Scenario.Partition, token));
                Func<Task> current = () => TimeSeriesAggregateScanBoundRf3Assertions.CurrentAsync(
                    reader, administrator, state, baseline, token);
                await TimeSeriesAggregateScanBoundRf3Refusal.RunAsync(reader, state, current, token);
                await TimeSeriesAggregateScanBoundRf3Seed.ReplayAsync(administrator, state.Command, state.Receipt, current, token);
                if (state.FreshCommand is not null)
                {
                    await TimeSeriesAggregateScanBoundRf3Seed.ReplayAsync(administrator, state.FreshCommand, state.FreshReceipt!, current, token);
                }
                await current();
                return true;
            }, token);
}
