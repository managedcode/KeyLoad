using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleChunkRetentionClusterPartial
{
    internal static async Task RunAsync(SampleChunkClusterScenario scenario, KeyLoadClient sdk,
        McpOfficialClient mcp, CancellationToken token)
    {
        var full = new ReadSampleRollupRequest(scenario.Execution.Partition, TimeSeriesClusterScenario.Set,
            TimeSeriesClusterScenario.Series, scenario.From, scenario.Until);
        await RefreshAsync(scenario, sdk, mcp, scenario.From, false, token);
        await SampleChunkRetentionClusterReadOracle.RollupAsync(sdk, mcp, full,
            new(SampleChunkRetentionClusterProtocol.FirstRevision, new(scenario.From, scenario.Until,
                SampleChunkClusterProtocol.CorrectedSequence, null,
                new(SampleChunkRetentionClusterProtocol.InitialCount, SampleChunkRetentionClusterProtocol.InitialSum,
                    SampleChunkClusterProtocol.FirstValue, SampleChunkRetentionClusterProtocol.LateValue,
                    SampleChunkRetentionClusterProtocol.InitialAverage))), token);
        var late = SampleChunkRetentionClusterSeed.Late(scenario);
        var expired = await SampleChunkRetentionClusterWriteOracle.CommitAsync(sdk, mcp,
            SampleChunkRetentionClusterSeed.Request(scenario, new ExpireSamples(TimeSeriesClusterScenario.Set,
                TimeSeriesClusterScenario.Series, late.Timestamp, SampleChunkClusterProtocol.OutputLimit)),
            [SampleChunkRetentionClusterSeed.Mutation(SampleChunkRetentionClusterProtocol.ExpireKind,
                SampleChunkRetentionClusterProtocol.PartialPurged)], true, token);
        await SampleChunkRetentionClusterReadOracle.WindowAsync(sdk, mcp, scenario.Request,
            new(scenario.WindowId, scenario.From, scenario.Until, SampleChunkClusterProtocol.MergedGeneration,
                SampleChunkClusterProtocol.MergedRevision, SampleChunkClusterProtocol.CorrectedSequence, late.Timestamp.UtcTicks,
                [new(TimeSeriesClusterScenario.Series, late, SampleChunkClusterProtocol.CorrectedSequence,
                    TimeSeriesClusterScenario.PrivateTags)], expired.Token.Position), expired, token);
        await SampleChunkRetentionClusterReadOracle.RawAsync(sdk, mcp, new(scenario.Execution.Partition,
            TimeSeriesClusterScenario.Set, TimeSeriesClusterScenario.Series, scenario.From, scenario.Until, SampleChunkClusterProtocol.OutputLimit),
            [new(TimeSeriesClusterScenario.Series, late, SampleChunkClusterProtocol.CorrectedSequence, TimeSeriesClusterScenario.PrivateTags)], token);
        await SampleChunkRetentionClusterDenialOracle.RollupAsync(sdk, mcp, full, token);
        await RefreshAsync(scenario, sdk, mcp, late.Timestamp, true, token);
        await SampleChunkRetentionClusterReadOracle.RollupAsync(sdk, mcp,
            full with
            {
                From = late.Timestamp
            }, new(SampleChunkRetentionClusterProtocol.FirstRevision,
                new(late.Timestamp, scenario.Until, SampleChunkClusterProtocol.CorrectedSequence, late.Timestamp,
                    new(SampleChunkRetentionClusterProtocol.RemainingCount, SampleChunkRetentionClusterProtocol.LateValue,
                        SampleChunkRetentionClusterProtocol.LateValue, SampleChunkRetentionClusterProtocol.LateValue,
                        SampleChunkRetentionClusterProtocol.LateValue))), token);
    }

    internal static Task<CommitReceipt> RefreshAsync(SampleChunkClusterScenario scenario, KeyLoadClient sdk,
        McpOfficialClient mcp, DateTimeOffset from, bool official, CancellationToken token)
        => SampleChunkRetentionClusterWriteOracle.CommitAsync(sdk, mcp,
            SampleChunkRetentionClusterSeed.Request(scenario, new RefreshSampleRollup(TimeSeriesClusterScenario.Set,
                TimeSeriesClusterScenario.Series, from, scenario.Until, SampleChunkRetentionClusterProtocol.AbsentRevision,
                SampleChunkClusterProtocol.OutputLimit)),
            [SampleChunkRetentionClusterSeed.Mutation(SampleRollupProtocol.RefreshKind,
                SampleChunkRetentionClusterProtocol.FirstRevision)], official, token);
}
