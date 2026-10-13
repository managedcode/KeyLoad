using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.Search;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class TimeSeriesCurrentCapabilityRf3Seed
{
    internal const string First = "capability-first";
    internal const string Second = "capability-second";
    internal const string Third = "capability-reader-original";
    internal const string Fourth = "capability-reader-fresh";
    private const string WriteGrant = "pii.write";

    internal static async Task<TimeSeriesCurrentCapabilityRf3State> CreateAsync(ClusterFixture fixture,
        RequestCqrsRf3Callers administrator, CancellationToken token)
    {
        var scenario = await TimeSeriesRf3Scenario.CreateAsync(fixture, token);
        var command = new CommandRequest(Guid.NewGuid(), scenario.Partition,
            [new AppendSamples(TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series,
                [TimeSeriesRf3Scenario.Data(First, TimeSeriesRf3Scenario.Start, 3d),
                    TimeSeriesRf3Scenario.Data(Second, TimeSeriesRf3Scenario.Start.AddTicks(1), 7d)],
                TimeSeriesRf3Scenario.PrivateTags)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(command, token));
        await ReceiptAsync(command, receipt, scenario.Partition);
        var principal = new PrincipalRecord("series-capability-" + Guid.NewGuid().ToString("N"),
            scenario.Partition.TenantId, [new(scenario.Partition.DatabaseId, TimeSeriesRf3Scenario.Set,
                Capability.SeriesRead | Capability.SeriesAppend)], [WriteGrant]);
        var identity = await NativeTextRf3Scenario.ConfigureIdentityAsync(fixture, principal, token);
        await SqlRf3Protocol.EqualAsync(principal, identity.Principal);
        var readerCommand = Append(scenario, Third, 2, 5d);
        var readerReceipt = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
            identity.Secret, async reader => await McpCallerAssertions.SdkSuccessAsync(
                await reader.Sdk.CommitAsync(readerCommand, token)), token);
        await ReceiptAsync(readerCommand, readerReceipt, scenario.Partition);
        return new(scenario, identity, command, receipt, readerCommand, readerReceipt);
    }

    internal static CommandRequest Append(TimeSeriesRf3Scenario scenario, string id, int tick, double value)
        => new(Guid.NewGuid(), scenario.Partition, [new AppendSamples(TimeSeriesRf3Scenario.Set,
            TimeSeriesRf3Scenario.Series, [TimeSeriesRf3Scenario.Data(id,
                TimeSeriesRf3Scenario.Start.AddTicks(tick), value)], TimeSeriesRf3Scenario.PrivateTags)]);

    internal static async Task ReceiptAsync(CommandRequest command, CommitReceipt receipt, PartitionRef partition)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
    }

    internal static async Task ReplayAsync(RequestCqrsRf3Callers caller,
        CommandRequest command, CommitReceipt receipt, Func<Task> observeCurrent, CancellationToken token)
    {
        await observeCurrent();
        await SqlRf3Protocol.EqualAsync(receipt,
            await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.CommitAsync(command, token)));
        await observeCurrent();
        await SqlRf3Protocol.EqualAsync(receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await caller.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token))).Value);
        await observeCurrent();
    }
}
