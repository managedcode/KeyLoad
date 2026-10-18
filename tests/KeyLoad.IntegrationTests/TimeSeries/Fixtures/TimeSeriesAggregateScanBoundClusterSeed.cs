using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class TimeSeriesAggregateScanBoundClusterSeed
{
    internal const string First = "scan-bound-first";
    internal const string Second = "scan-bound-second";
    internal const string Third = "scan-bound-third";
    internal const string Fourth = "scan-bound-fourth";

    internal static async Task<TimeSeriesAggregateScanBoundClusterState> CreateAsync(ClusterFixture fixture,
        RequestCqrsClusterCallers administrator, CancellationToken token)
    {
        var scenario = await TimeSeriesClusterScenario.CreateAsync(fixture, token);
        var command = new CommandRequest(Guid.NewGuid(), scenario.Partition,
            [new AppendSamples(TimeSeriesClusterScenario.Set, TimeSeriesClusterScenario.Series,
                [TimeSeriesClusterScenario.Data(First, TimeSeriesClusterScenario.Start, 3d),
                 TimeSeriesClusterScenario.Data(Second, TimeSeriesClusterScenario.Start.AddTicks(1), 7d),
                 TimeSeriesClusterScenario.Data(Third, TimeSeriesClusterScenario.Start.AddTicks(2), 5d)],
                TimeSeriesClusterScenario.PrivateTags)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(command, token));
        await ReceiptAsync(command, receipt, scenario.Partition);
        var identity = await McpPersistedIdentity.CreateAsync(fixture, scenario.Partition,
            TimeSeriesClusterScenario.Set, Capability.SeriesRead, token);
        return new(scenario, identity, command, receipt);
    }

    internal static async Task<TimeSeriesAggregateScanBoundClusterState> AppendAsync(RequestCqrsClusterCallers administrator,
        TimeSeriesAggregateScanBoundClusterState state, CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), state.Scenario.Partition,
            [new AppendSamples(TimeSeriesClusterScenario.Set, TimeSeriesClusterScenario.Series,
                [TimeSeriesClusterScenario.Data(Fourth, TimeSeriesClusterScenario.Start.AddTicks(3), 11d)],
                TimeSeriesClusterScenario.PrivateTags)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(command, token));
        await ReceiptAsync(command, receipt, state.Scenario.Partition);
        return state with
        {
            FreshCommand = command,
            FreshReceipt = receipt
        };
    }

    private static async Task ReceiptAsync(CommandRequest command, CommitReceipt receipt, PartitionRef partition)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
    }

    internal static async Task ReplayAsync(RequestCqrsClusterCallers caller,
        CommandRequest command, CommitReceipt receipt, Func<Task> current, CancellationToken token)
    {
        await current();
        await SqlClusterProtocol.EqualAsync(receipt,
            await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.CommitAsync(command, token)));
        await current();
        await SqlClusterProtocol.EqualAsync(receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await caller.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token))).Value);
        await current();
    }
}
