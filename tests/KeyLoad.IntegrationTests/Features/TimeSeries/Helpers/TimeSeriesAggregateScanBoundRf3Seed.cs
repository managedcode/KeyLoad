using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class TimeSeriesAggregateScanBoundRf3Seed
{
    internal const string First = "scan-bound-first";
    internal const string Second = "scan-bound-second";
    internal const string Third = "scan-bound-third";
    internal const string Fourth = "scan-bound-fourth";

    internal static async Task<TimeSeriesAggregateScanBoundRf3State> CreateAsync(ClusterFixture fixture,
        RequestCqrsRf3Callers administrator, CancellationToken token)
    {
        var scenario = await TimeSeriesRf3Scenario.CreateAsync(fixture, token);
        var command = new CommandRequest(Guid.NewGuid(), scenario.Partition,
            [new AppendSamples(TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series,
                [TimeSeriesRf3Scenario.Data(First, TimeSeriesRf3Scenario.Start, 3d),
                 TimeSeriesRf3Scenario.Data(Second, TimeSeriesRf3Scenario.Start.AddTicks(1), 7d),
                 TimeSeriesRf3Scenario.Data(Third, TimeSeriesRf3Scenario.Start.AddTicks(2), 5d)],
                TimeSeriesRf3Scenario.PrivateTags)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(command, token));
        await ReceiptAsync(command, receipt, scenario.Partition);
        var identity = await McpPersistedIdentity.CreateAsync(fixture, scenario.Partition,
            TimeSeriesRf3Scenario.Set, Capability.SeriesRead, token);
        return new(scenario, identity, command, receipt);
    }

    internal static async Task<TimeSeriesAggregateScanBoundRf3State> AppendAsync(RequestCqrsRf3Callers administrator,
        TimeSeriesAggregateScanBoundRf3State state, CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), state.Scenario.Partition,
            [new AppendSamples(TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series,
                [TimeSeriesRf3Scenario.Data(Fourth, TimeSeriesRf3Scenario.Start.AddTicks(3), 11d)],
                TimeSeriesRf3Scenario.PrivateTags)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(command, token));
        await ReceiptAsync(command, receipt, state.Scenario.Partition);
        return state with { FreshCommand = command, FreshReceipt = receipt };
    }

    private static async Task ReceiptAsync(CommandRequest command, CommitReceipt receipt, PartitionRef partition)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
    }

    internal static async Task ReplayAsync(RequestCqrsRf3Callers caller,
        CommandRequest command, CommitReceipt receipt, Func<Task> current, CancellationToken token)
    {
        await current();
        await SqlRf3Protocol.EqualAsync(receipt,
            await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.CommitAsync(command, token)));
        await current();
        await SqlRf3Protocol.EqualAsync(receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await caller.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token))).Value);
        await current();
    }
}
