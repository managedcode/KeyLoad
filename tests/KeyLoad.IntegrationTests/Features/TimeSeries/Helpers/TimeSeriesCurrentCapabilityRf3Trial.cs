using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class TimeSeriesCurrentCapabilityRf3Trial
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var state = await AdminAsync(fixture, async administrator =>
        {
            var original = await TimeSeriesCurrentCapabilityRf3Seed.CreateAsync(fixture, administrator, token);
            await ReadersAsync(fixture, administrator, original, denied: false, token);
            await PolicyAsync(administrator, original, allowed: false, 2L, token);
            await ReadersAsync(fixture, administrator, original, denied: true, token);
            return original;
        }, token);
        await TimeSeriesCurrentCapabilityRf3Restart.RunAsync(fixture, token);
        state = await AdminAsync(fixture, async administrator =>
        {
            await ReadersAsync(fixture, administrator, state, denied: true, token);
            await PolicyAsync(administrator, state, allowed: true, 3L, token);
            var repaired = await RepairAsync(fixture, administrator, state, token);
            await ReadersAsync(fixture, administrator, repaired, denied: false, token);
            return repaired;
        }, token);
        await TimeSeriesCurrentCapabilityRf3Restart.RunAsync(fixture, token);
        _ = await AdminAsync(fixture, async administrator =>
        {
            await ReadersAsync(fixture, administrator, state, denied: false, token);
            return true;
        }, token);
    }

    private static Task<T> AdminAsync<T>(ClusterFixture fixture, Func<RequestCqrsRf3Callers, Task<T>> operation,
        CancellationToken token) => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, operation, token);

    private static async Task PolicyAsync(RequestCqrsRf3Callers administrator,
        TimeSeriesCurrentCapabilityRf3State state, bool allowed, long epoch, CancellationToken token)
    {
        var expected = state.Identity.Principal with
        {
            PolicyEpoch = epoch,
            Grants = [new(state.Scenario.Partition.DatabaseId, TimeSeriesRf3Scenario.Set,
            allowed ? Capability.SeriesRead | Capability.SeriesAppend : Capability.SeriesAppend)],
            Revoked = false
        };
        var actual = await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token));
        await SqlRf3Protocol.EqualAsync(expected, actual);
    }

    private static Task<TimeSeriesCurrentCapabilityRf3State> RepairAsync(ClusterFixture fixture,
        RequestCqrsRf3Callers administrator, TimeSeriesCurrentCapabilityRf3State state, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Identity.Secret,
            async reader =>
            {
                await TimeSeriesCurrentCapabilityRf3Assertions.HealthyAsync(reader, state, token);
                await TimeSeriesCurrentCapabilityRf3Refusal.FencedOriginalAsync(reader, administrator, state, token);
                var command = TimeSeriesCurrentCapabilityRf3Seed.Append(state.Scenario,
                    TimeSeriesCurrentCapabilityRf3Seed.Fourth, 3, 11d);
                var receipt = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.CommitAsync(command, token));
                await TimeSeriesCurrentCapabilityRf3Seed.ReceiptAsync(command, receipt, state.Scenario.Partition);
                return state with
                {
                    FreshCommand = command,
                    FreshReceipt = receipt
                };
            }, token);

    private static Task<bool> ReadersAsync(ClusterFixture fixture, RequestCqrsRf3Callers administrator,
        TimeSeriesCurrentCapabilityRf3State state, bool denied, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Identity.Secret,
            async reader =>
            {
                if (denied)
                {
                    await TimeSeriesCurrentCapabilityRf3Refusal.RunAsync(reader, administrator, state, token);
                }
                else
                {
                    var before = await McpCallerAssertions.SdkSuccessAsync(
                        await administrator.Sdk.OutboxStatusAsync(state.Scenario.Partition, token));
                    Func<Task> current = () => TimeSeriesCurrentCapabilityRf3Refusal.CurrentAsync(
                        reader, administrator, state, before, token);
                    await current();
                    await TimeSeriesCurrentCapabilityRf3Seed.ReplayAsync(administrator, state.Command, state.Receipt, current, token);
                    if (state.FreshCommand is null)
                    {
                        await TimeSeriesCurrentCapabilityRf3Seed.ReplayAsync(reader, state.ReaderCommand, state.ReaderReceipt, current, token);
                    }
                    else
                    {
                        await TimeSeriesCurrentCapabilityRf3Refusal.FencedOriginalAsync(reader, administrator, state, token);
                        await TimeSeriesCurrentCapabilityRf3Seed.ReplayAsync(reader, state.FreshCommand, state.FreshReceipt!, current, token);
                    }
                    await current();
                }
                return true;
            }, token);
}
