using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleChunkRetentionRf3Full
{
    internal static async Task<SampleChunkRetentionRf3Continuation> RunAsync(SampleChunkRetentionRf3Original original,
        KeyLoadClient sdk, McpOfficialClient mcp, CancellationToken token)
    {
        var scenario = original.Scenario;
        await SampleChunkRetentionRf3WriteOracle.CommitAsync(sdk, mcp,
            SampleChunkRetentionRf3Seed.Request(scenario, new ExpireSamples(TimeSeriesRf3Scenario.Set,
                TimeSeriesRf3Scenario.Series, scenario.Until, SampleChunkRf3Protocol.OutputLimit)),
            [SampleChunkRetentionRf3Seed.Mutation(SampleChunkRetentionRf3Protocol.ExpireKind,
                SampleChunkRetentionRf3Protocol.FullPurged)], false, token);
        await SampleChunkRetentionRf3DenialOracle.RollupAsync(sdk, mcp,
            new(scenario.Native.Partition, TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series,
                SampleChunkRetentionRf3Seed.Late(scenario).Timestamp, scenario.Until), token);
        await SampleChunkRetentionRf3WriteOracle.CommitAsync(sdk, mcp,
            SampleChunkRetentionRf3Seed.Request(scenario, new DropSampleChunkWindow(TimeSeriesRf3Scenario.Set,
                TimeSeriesRf3Scenario.Series, scenario.WindowId, SampleChunkRf3Protocol.MergedRevision)),
            [SampleChunkRetentionRf3Seed.Mutation(SampleChunkProtocol.DropKind,
                SampleChunkRf3Protocol.MergedRevision + SampleChunkRetentionRf3Protocol.FirstRevision)], true, token);
        await SampleChunkRetentionRf3DenialOracle.WindowAsync(sdk, mcp, scenario.Request, token);
        await SampleChunkRetentionRf3WriteOracle.ReplayAsync(sdk, mcp, original.Append, original.Receipt, token);
        await SampleChunkRetentionRf3DenialOracle.WindowAsync(sdk, mcp, scenario.Request, token);
        await SampleChunkRetentionRf3WriteOracle.CommitAsync(sdk, mcp,
            SampleChunkRetentionRf3Seed.Request(scenario, new DropSampleRollup(TimeSeriesRf3Scenario.Set,
                TimeSeriesRf3Scenario.Series, SampleChunkRetentionRf3Seed.Late(scenario).Timestamp, scenario.Until,
                SampleChunkRetentionRf3Protocol.FirstRevision)),
            [SampleChunkRetentionRf3Seed.Mutation(SampleRollupProtocol.DropKind,
                SampleChunkRetentionRf3Protocol.DroppedRevision)], false, token);
        return await FreshAsync(original, sdk, mcp, token).ConfigureAwait(false);
    }

    private static async Task<SampleChunkRetentionRf3Continuation> FreshAsync(SampleChunkRetentionRf3Original original,
        KeyLoadClient sdk, McpOfficialClient mcp, CancellationToken token)
    {
        var scenario = original.Scenario;
        var id = Guid.NewGuid();
        var sample = new SampleData(SampleChunkRetentionRf3Protocol.FreshId, TimeProvider.System.GetUtcNow(),
            SampleChunkRetentionRf3Protocol.FreshValue);
        var until = sample.Timestamp.AddHours(SampleChunkRf3Protocol.WindowHours);
        await SampleChunkRetentionRf3WriteOracle.CommitAsync(sdk, mcp,
            SampleChunkRetentionRf3Seed.Request(scenario, new OpenSampleChunkWindow(TimeSeriesRf3Scenario.Set,
                TimeSeriesRf3Scenario.Series, id, scenario.Until, until)),
            [SampleChunkRetentionRf3Seed.Mutation(SampleChunkProtocol.OpenKind,
                SampleChunkRetentionRf3Protocol.FirstRevision)], false, token);
        var append = SampleChunkRetentionRf3Seed.Request(scenario, new AppendSamples(TimeSeriesRf3Scenario.Set,
            TimeSeriesRf3Scenario.Series, [sample], TimeSeriesRf3Scenario.PrivateTags));
        var receipt = await SampleChunkRetentionRf3WriteOracle.CommitAsync(sdk, mcp, append,
            [SampleChunkRetentionRf3Seed.Mutation(SampleChunkRetentionRf3Protocol.AppendKind,
                SampleChunkRetentionRf3Protocol.FreshSequence)], true, token);
        await SampleChunkRetentionRf3WriteOracle.CommitAsync(sdk, mcp,
            SampleChunkRetentionRf3Seed.Request(scenario, new RefreshSampleRollup(TimeSeriesRf3Scenario.Set,
                TimeSeriesRf3Scenario.Series, scenario.Until, until, SampleChunkRetentionRf3Protocol.AbsentRevision,
                SampleChunkRf3Protocol.OutputLimit)),
            [SampleChunkRetentionRf3Seed.Mutation(SampleRollupProtocol.RefreshKind,
                SampleChunkRetentionRf3Protocol.FirstRevision)], false, token);
        var continuation = new SampleChunkRetentionRf3Continuation(scenario, original.Append,
            original.Receipt, id, sample, append, receipt);
        await SampleChunkRetentionRf3Healthy.RequireAsync(continuation, sdk, mcp, token);
        return continuation;
    }
}
