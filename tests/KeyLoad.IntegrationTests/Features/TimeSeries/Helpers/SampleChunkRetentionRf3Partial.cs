using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleChunkRetentionRf3Partial
{
    internal static async Task RunAsync(SampleChunkRf3Scenario scenario, KeyLoadClient sdk,
        McpOfficialClient mcp, CancellationToken token)
    {
        var full = new ReadSampleRollupRequest(scenario.Native.Partition, TimeSeriesRf3Scenario.Set,
            TimeSeriesRf3Scenario.Series, scenario.From, scenario.Until);
        await RefreshAsync(scenario, sdk, mcp, scenario.From, false, token);
        await SampleChunkRetentionRf3ReadOracle.RollupAsync(sdk, mcp, full,
            new(SampleChunkRetentionRf3Protocol.FirstRevision, new(scenario.From, scenario.Until,
                SampleChunkRf3Protocol.CorrectedSequence, null,
                new(SampleChunkRetentionRf3Protocol.InitialCount, SampleChunkRetentionRf3Protocol.InitialSum,
                    SampleChunkRf3Protocol.FirstValue, SampleChunkRetentionRf3Protocol.LateValue,
                    SampleChunkRetentionRf3Protocol.InitialAverage))), token);
        var late = SampleChunkRetentionRf3Seed.Late(scenario);
        var expired = await SampleChunkRetentionRf3WriteOracle.CommitAsync(sdk, mcp,
            SampleChunkRetentionRf3Seed.Request(scenario, new ExpireSamples(TimeSeriesRf3Scenario.Set,
                TimeSeriesRf3Scenario.Series, late.Timestamp, SampleChunkRf3Protocol.OutputLimit)),
            [SampleChunkRetentionRf3Seed.Mutation(SampleChunkRetentionRf3Protocol.ExpireKind,
                SampleChunkRetentionRf3Protocol.PartialPurged)], true, token);
        await SampleChunkRetentionRf3ReadOracle.WindowAsync(sdk, mcp, scenario.Request,
            new(scenario.WindowId, scenario.From, scenario.Until, SampleChunkRf3Protocol.MergedGeneration,
                SampleChunkRf3Protocol.MergedRevision, SampleChunkRf3Protocol.CorrectedSequence, late.Timestamp.UtcTicks,
                [new(TimeSeriesRf3Scenario.Series, late, SampleChunkRf3Protocol.CorrectedSequence,
                    TimeSeriesRf3Scenario.PrivateTags)], expired.Token.Position), expired, token);
        await SampleChunkRetentionRf3ReadOracle.RawAsync(sdk, mcp, new(scenario.Native.Partition,
            TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series, scenario.From, scenario.Until, SampleChunkRf3Protocol.OutputLimit),
            [new(TimeSeriesRf3Scenario.Series, late, SampleChunkRf3Protocol.CorrectedSequence, TimeSeriesRf3Scenario.PrivateTags)], token);
        await SampleChunkRetentionRf3DenialOracle.RollupAsync(sdk, mcp, full, token);
        await RefreshAsync(scenario, sdk, mcp, late.Timestamp, true, token);
        await SampleChunkRetentionRf3ReadOracle.RollupAsync(sdk, mcp,
            full with { From = late.Timestamp }, new(SampleChunkRetentionRf3Protocol.FirstRevision,
                new(late.Timestamp, scenario.Until, SampleChunkRf3Protocol.CorrectedSequence, late.Timestamp,
                    new(SampleChunkRetentionRf3Protocol.RemainingCount, SampleChunkRetentionRf3Protocol.LateValue,
                        SampleChunkRetentionRf3Protocol.LateValue, SampleChunkRetentionRf3Protocol.LateValue,
                        SampleChunkRetentionRf3Protocol.LateValue))), token);
    }

    internal static Task<CommitReceipt> RefreshAsync(SampleChunkRf3Scenario scenario, KeyLoadClient sdk,
        McpOfficialClient mcp, DateTimeOffset from, bool official, CancellationToken token)
        => SampleChunkRetentionRf3WriteOracle.CommitAsync(sdk, mcp,
            SampleChunkRetentionRf3Seed.Request(scenario, new RefreshSampleRollup(TimeSeriesRf3Scenario.Set,
                TimeSeriesRf3Scenario.Series, from, scenario.Until, SampleChunkRetentionRf3Protocol.AbsentRevision,
                SampleChunkRf3Protocol.OutputLimit)),
            [SampleChunkRetentionRf3Seed.Mutation(SampleRollupProtocol.RefreshKind,
                SampleChunkRetentionRf3Protocol.FirstRevision)], official, token);
}
