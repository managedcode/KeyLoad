using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.TimeSeries;

internal static class SeriesUtcTiePublicSeed
{
    internal const string FreshId = "fresh-tie";
    private const string AppendKind = "appendSamples";
    private const string ExpireKind = "expireSamples";
    internal static CommandRequest Original(SeriesUtcTiePublicState state)
        => new(Guid.NewGuid(), state.Scenario.Partition,
            [new AppendSamples(TimeSeriesClusterScenario.Set, TimeSeriesClusterScenario.Series,
                state.Original, TimeSeriesClusterScenario.PrivateTags)]);

    internal static CommandRequest Expire(SeriesUtcTiePublicState state)
        => new(Guid.NewGuid(), state.Scenario.Partition,
            [new ExpireSamples(TimeSeriesClusterScenario.Set, TimeSeriesClusterScenario.Series, TimeSeriesClusterScenario.Start, 1)]);

    internal static CommandRequest Deduplicate(SeriesUtcTiePublicState state)
        => new(Guid.NewGuid(), state.Scenario.Partition,
            [new AppendSamples(TimeSeriesClusterScenario.Set, TimeSeriesClusterScenario.Series,
                [state.Original[0]], TimeSeriesClusterScenario.PrivateTags)]);

    internal static SampleData FreshSample()
        => new(FreshId, TimeSeriesClusterScenario.Start.ToOffset(TimeSpan.FromHours(-3)), 8d);

    internal static CommandRequest Fresh(SeriesUtcTiePublicState state)
        => new(Guid.NewGuid(), state.Scenario.Partition,
            [new AppendSamples(TimeSeriesClusterScenario.Set, TimeSeriesClusterScenario.Series,
                [FreshSample()], TimeSeriesClusterScenario.PrivateTags)]);

    internal static async Task CommitAsync(RequestCqrsClusterCallers administrator,
        SeriesUtcTiePublicState state, CommandRequest command, long sequence, bool expire, CancellationToken token)
    {
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(command, token));
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(state.Scenario.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await Assert.That(receipt.PersistedCopies).IsEqualTo(3);
        await SqlClusterProtocol.EqualAsync(new[] { new MutationReceipt(expire ? ExpireKind : AppendKind,
            TimeSeriesClusterScenario.Set, TimeSeriesClusterScenario.Series, sequence) }, receipt.Mutations.ToArray());
        state.Receipts.Add((command, receipt));
    }
}
