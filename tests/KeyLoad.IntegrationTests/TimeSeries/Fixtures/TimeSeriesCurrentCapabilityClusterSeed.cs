using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.Search;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class TimeSeriesCurrentCapabilityClusterSeed
{
    internal const string First = "capability-first";
    internal const string Second = "capability-second";
    internal const string Third = "capability-reader-original";
    internal const string Fourth = "capability-reader-fresh";
    private const string WriteGrant = "pii.write";

    internal static async Task<TimeSeriesCurrentCapabilityClusterState> CreateAsync(ClusterFixture fixture,
        RequestCqrsClusterCallers administrator, CancellationToken token)
    {
        var scenario = await TimeSeriesClusterScenario.CreateAsync(fixture, token);
        var command = new CommandRequest(Guid.NewGuid(), scenario.Partition,
            [new AppendSamples(TimeSeriesClusterScenario.Set, TimeSeriesClusterScenario.Series,
                [TimeSeriesClusterScenario.Data(First, TimeSeriesClusterScenario.Start, 3d),
                    TimeSeriesClusterScenario.Data(Second, TimeSeriesClusterScenario.Start.AddTicks(1), 7d)],
                TimeSeriesClusterScenario.PrivateTags)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(command, token));
        await ReceiptAsync(command, receipt, scenario.Partition);
        var principal = new PrincipalRecord("series-capability-" + Guid.NewGuid().ToString("N"),
            scenario.Partition.TenantId, [new(scenario.Partition.DatabaseId, TimeSeriesClusterScenario.Set,
                Capability.SeriesRead | Capability.SeriesAppend)], [WriteGrant]);
        var identity = await TextClusterScenario.ConfigureIdentityAsync(fixture, principal, token);
        await SqlClusterProtocol.EqualAsync(principal, identity.Principal);
        var readerCommand = Append(scenario, Third, 2, 5d);
        var readerReceipt = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
            identity.Secret, async reader => await McpCallerAssertions.SdkSuccessAsync(
                await reader.Sdk.CommitAsync(readerCommand, token)), token);
        await ReceiptAsync(readerCommand, readerReceipt, scenario.Partition);
        return new(scenario, identity, command, receipt, readerCommand, readerReceipt);
    }

    internal static CommandRequest Append(TimeSeriesClusterScenario scenario, string id, int tick, double value)
        => new(Guid.NewGuid(), scenario.Partition, [new AppendSamples(TimeSeriesClusterScenario.Set,
            TimeSeriesClusterScenario.Series, [TimeSeriesClusterScenario.Data(id,
                TimeSeriesClusterScenario.Start.AddTicks(tick), value)], TimeSeriesClusterScenario.PrivateTags)]);

    internal static async Task ReceiptAsync(CommandRequest command, CommitReceipt receipt, PartitionRef partition)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await Assert.That(receipt.PersistedCopies).IsEqualTo(3);
    }

    internal static async Task ReplayAsync(RequestCqrsClusterCallers caller,
        CommandRequest command, CommitReceipt receipt, Func<Task> observeCurrent, CancellationToken token)
    {
        await observeCurrent();
        await SqlClusterProtocol.EqualAsync(receipt,
            await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.CommitAsync(command, token)));
        await observeCurrent();
        await SqlClusterProtocol.EqualAsync(receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await caller.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token))).Value);
        await observeCurrent();
    }
}
