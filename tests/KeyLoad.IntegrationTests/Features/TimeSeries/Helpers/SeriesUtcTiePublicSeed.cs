using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SeriesUtcTiePublicSeed
{
    internal const string FreshId = "fresh-tie";
    private const string AppendKind = "appendSamples";
    private const string ExpireKind = "expireSamples";
    internal static CommandRequest Original(SeriesUtcTiePublicState state)
        => new(Guid.NewGuid(), state.Scenario.Partition,
            [new AppendSamples(TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series,
                state.Original, TimeSeriesRf3Scenario.PrivateTags)]);

    internal static CommandRequest Expire(SeriesUtcTiePublicState state)
        => new(Guid.NewGuid(), state.Scenario.Partition,
            [new ExpireSamples(TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series, TimeSeriesRf3Scenario.Start, 1)]);

    internal static CommandRequest Deduplicate(SeriesUtcTiePublicState state)
        => new(Guid.NewGuid(), state.Scenario.Partition,
            [new AppendSamples(TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series,
                [state.Original[0]], TimeSeriesRf3Scenario.PrivateTags)]);

    internal static SampleData FreshSample()
        => new(FreshId, TimeSeriesRf3Scenario.Start.ToOffset(TimeSpan.FromHours(-3)), 8d);

    internal static CommandRequest Fresh(SeriesUtcTiePublicState state)
        => new(Guid.NewGuid(), state.Scenario.Partition,
            [new AppendSamples(TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series,
                [FreshSample()], TimeSeriesRf3Scenario.PrivateTags)]);

    internal static async Task CommitAsync(RequestCqrsRf3Callers administrator,
        SeriesUtcTiePublicState state, CommandRequest command, long sequence, bool expire, CancellationToken token)
    {
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(command, token));
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(state.Scenario.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(state.Placement.Incarnation);
        await Assert.That(receipt.Token.OwnershipEpoch).IsEqualTo(state.Placement.PlacementEpoch);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await SqlRf3Protocol.EqualAsync(new[] { new MutationReceipt(expire ? ExpireKind : AppendKind,
            TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series, sequence) }, receipt.Mutations.ToArray());
        state.Receipts.Add((command, receipt));
    }
}
