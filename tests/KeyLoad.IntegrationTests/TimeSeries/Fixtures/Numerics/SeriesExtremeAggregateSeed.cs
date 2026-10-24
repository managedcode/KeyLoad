using System.Collections.Immutable;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.TimeSeries;

internal static class SeriesExtremeAggregateSeed
{
    private const string AppendKind = "appendSamples";
    private const long NoPosition = 0;
    private const long OpenRevision = 1;
    private const long BeforeSealRevision = 8;
    private const long SealedRevision = 9;

    internal static async Task AppendAsync(RequestCqrsClusterCallers administrator, SeriesExtremeAggregateState state,
        ImmutableArray<SampleData> samples, long sequence, CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), state.Scenario.Partition,
            [new AppendSamples(TimeSeriesClusterScenario.Set, TimeSeriesClusterScenario.Series, samples, TimeSeriesClusterScenario.PublicTags)]);
        var actual = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(command, token));
        await Assert.That(actual.Token.Position).IsGreaterThan(NoPosition);
        var expected = new CommitReceipt(command.CommandId,
            new(actual.Token.Incarnation, command.Partition.AtomicPartitionId, actual.Token.Position),
            [new(AppendKind, TimeSeriesClusterScenario.Set, TimeSeriesClusterScenario.Series, sequence)], DurabilityProfile.ProcessDurable, 3);
        await SqlClusterProtocol.EqualAsync(expected, actual);
        state.Receipts.Add((command, expected));
    }

    internal static async Task OpenAsync(RequestCqrsClusterCallers administrator, SeriesExtremeAggregateState state,
        CancellationToken token)
        => await WindowCommitAsync(administrator, state,
            new OpenSampleChunkWindow(TimeSeriesClusterScenario.Set, TimeSeriesClusterScenario.Series, state.WindowId,
                DateTimeOffset.MinValue, DateTimeOffset.MaxValue), "openSampleChunkWindow", OpenRevision, token);

    internal static async Task SealAsync(RequestCqrsClusterCallers administrator, SeriesExtremeAggregateState state,
        CancellationToken token)
        => await WindowCommitAsync(administrator, state,
            new SealSampleChunkWindow(TimeSeriesClusterScenario.Set, TimeSeriesClusterScenario.Series, state.WindowId, BeforeSealRevision),
            "sealSampleChunkWindow", SealedRevision, token);

    private static async Task WindowCommitAsync(RequestCqrsClusterCallers administrator, SeriesExtremeAggregateState state,
        Mutation mutation, string kind, long revision, CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), state.Scenario.Partition, [mutation]);
        var actual = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(command, token));
        await Assert.That(actual.Token.Position).IsGreaterThan(NoPosition);
        var expected = new CommitReceipt(command.CommandId,
            new(actual.Token.Incarnation, command.Partition.AtomicPartitionId, actual.Token.Position),
            [new(kind, TimeSeriesClusterScenario.Set, TimeSeriesClusterScenario.Series, revision)], DurabilityProfile.ProcessDurable, 3);
        await SqlClusterProtocol.EqualAsync(expected, actual);
        state.Receipts.Add((command, expected));
    }

    internal static async Task CaptureAsync(RequestCqrsClusterCallers administrator, SeriesExtremeAggregateState state,
        CancellationToken token)
    {
        await SeriesExtremeAggregateCurrent.RowsAsync(administrator, state, token);
        state.Outbox = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(state.Scenario.Partition, token));
    }
}
