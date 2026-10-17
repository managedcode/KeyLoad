using System.Collections.Immutable;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SeriesExtremeAggregateSeed
{
    private const string AppendKind = "appendSamples";
    private const long NoPosition = 0;
    private const long OpenRevision = 1;
    private const long BeforeSealRevision = 8;
    private const long SealedRevision = 9;

    internal static async Task AppendAsync(RequestCqrsRf3Callers administrator, SeriesExtremeAggregateState state,
        ImmutableArray<SampleData> samples, long sequence, CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), state.Scenario.Partition,
            [new AppendSamples(TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series, samples, TimeSeriesRf3Scenario.PublicTags)]);
        var actual = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(command, token));
        await Assert.That(actual.Token.Position).IsGreaterThan(NoPosition);
        var expected = new CommitReceipt(command.CommandId,
            new(state.Placement.Incarnation, command.Partition.AtomicPartitionId, actual.Token.Position, state.Placement.PlacementEpoch),
            [new(AppendKind, TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series, sequence)], DurabilityProfile.QuorumProcessDurable);
        await SqlRf3Protocol.EqualAsync(expected, actual);
        state.Receipts.Add((command, expected));
    }

    internal static async Task OpenAsync(RequestCqrsRf3Callers administrator, SeriesExtremeAggregateState state,
        CancellationToken token)
        => await WindowCommitAsync(administrator, state,
            new OpenSampleChunkWindow(TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series, state.WindowId,
                DateTimeOffset.MinValue, DateTimeOffset.MaxValue), "openSampleChunkWindow", OpenRevision, token);

    internal static async Task SealAsync(RequestCqrsRf3Callers administrator, SeriesExtremeAggregateState state,
        CancellationToken token)
        => await WindowCommitAsync(administrator, state,
            new SealSampleChunkWindow(TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series, state.WindowId, BeforeSealRevision),
            "sealSampleChunkWindow", SealedRevision, token);

    private static async Task WindowCommitAsync(RequestCqrsRf3Callers administrator, SeriesExtremeAggregateState state,
        Mutation mutation, string kind, long revision, CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), state.Scenario.Partition, [mutation]);
        var actual = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(command, token));
        await Assert.That(actual.Token.Position).IsGreaterThan(NoPosition);
        var expected = new CommitReceipt(command.CommandId,
            new(state.Placement.Incarnation, command.Partition.AtomicPartitionId, actual.Token.Position, state.Placement.PlacementEpoch),
            [new(kind, TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series, revision)], DurabilityProfile.QuorumProcessDurable);
        await SqlRf3Protocol.EqualAsync(expected, actual);
        state.Receipts.Add((command, expected));
    }

    internal static async Task CaptureAsync(RequestCqrsRf3Callers administrator, SeriesExtremeAggregateState state,
        CancellationToken token)
    {
        await SeriesExtremeAggregateCurrent.RowsAsync(administrator, state, token);
        state.Outbox = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(state.Scenario.Partition, token));
    }
}
