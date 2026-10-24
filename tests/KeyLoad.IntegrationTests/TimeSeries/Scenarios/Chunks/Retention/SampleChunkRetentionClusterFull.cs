using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.TimeSeries;

internal static class SampleChunkRetentionClusterFull
{
    internal static async Task<SampleChunkRetentionClusterContinuation> RunAsync(SampleChunkRetentionClusterOriginal original,
        KeyLoadClient sdk, McpOfficialClient mcp, CancellationToken token)
    {
        var scenario = original.Scenario;
        await SampleChunkRetentionClusterWriteOracle.CommitAsync(sdk, mcp,
            SampleChunkRetentionClusterSeed.Request(scenario, new ExpireSamples(TimeSeriesClusterScenario.Set,
                TimeSeriesClusterScenario.Series, scenario.Until, SampleChunkClusterProtocol.OutputLimit)),
            [SampleChunkRetentionClusterSeed.Mutation(SampleChunkRetentionClusterProtocol.ExpireKind,
                SampleChunkRetentionClusterProtocol.FullPurged)], false, token);
        await SampleChunkRetentionClusterDenialOracle.RollupAsync(sdk, mcp,
            new(scenario.Execution.Partition, TimeSeriesClusterScenario.Set, TimeSeriesClusterScenario.Series,
                SampleChunkRetentionClusterSeed.Late(scenario).Timestamp, scenario.Until), token);
        await SampleChunkRetentionClusterWriteOracle.CommitAsync(sdk, mcp,
            SampleChunkRetentionClusterSeed.Request(scenario, new DropSampleChunkWindow(TimeSeriesClusterScenario.Set,
                TimeSeriesClusterScenario.Series, scenario.WindowId, SampleChunkClusterProtocol.MergedRevision)),
            [SampleChunkRetentionClusterSeed.Mutation(SampleChunkProtocol.DropKind,
                SampleChunkClusterProtocol.MergedRevision + SampleChunkRetentionClusterProtocol.FirstRevision)], true, token);
        await SampleChunkRetentionClusterDenialOracle.WindowAsync(sdk, mcp, scenario.Request, token);
        await SampleChunkRetentionClusterWriteOracle.ReplayAsync(sdk, mcp, original.Append, original.Receipt, token);
        await SampleChunkRetentionClusterDenialOracle.WindowAsync(sdk, mcp, scenario.Request, token);
        await SampleChunkRetentionClusterWriteOracle.CommitAsync(sdk, mcp,
            SampleChunkRetentionClusterSeed.Request(scenario, new DropSampleRollup(TimeSeriesClusterScenario.Set,
                TimeSeriesClusterScenario.Series, SampleChunkRetentionClusterSeed.Late(scenario).Timestamp, scenario.Until,
                SampleChunkRetentionClusterProtocol.FirstRevision)),
            [SampleChunkRetentionClusterSeed.Mutation(SampleRollupProtocol.DropKind,
                SampleChunkRetentionClusterProtocol.DroppedRevision)], false, token);
        var refused = await SampleChunkRetainedIdentityClusterFlow.RefuseAsync(scenario, sdk, mcp, token);
        var healthy = await FreshAsync(original, sdk, mcp, refused, token).ConfigureAwait(false);
        var retained = await SampleChunkRetainedIdentityClusterFlow.CaptureAsync(refused.Request, sdk, mcp, token);
        var complete = healthy with
        {
            RetainedIdentity = retained
        };
        await SampleChunkRetainedIdentityClusterFlow.HealthyAsync(complete, sdk, mcp, token);
        return complete;
    }

    private static async Task<SampleChunkRetentionClusterContinuation> FreshAsync(SampleChunkRetentionClusterOriginal original,
        KeyLoadClient sdk, McpOfficialClient mcp, SampleChunkRetainedIdentityClusterState retained, CancellationToken token)
    {
        var scenario = original.Scenario;
        var id = Guid.NewGuid();
        var sample = new SampleData(SampleChunkRetentionClusterProtocol.FreshId, TimeProvider.System.GetUtcNow(),
            SampleChunkRetentionClusterProtocol.FreshValue);
        var until = sample.Timestamp.AddHours(SampleChunkClusterProtocol.WindowHours);
        await SampleChunkRetentionClusterWriteOracle.CommitAsync(sdk, mcp,
            SampleChunkRetentionClusterSeed.Request(scenario, new OpenSampleChunkWindow(TimeSeriesClusterScenario.Set,
                TimeSeriesClusterScenario.Series, id, scenario.Until, until)),
            [SampleChunkRetentionClusterSeed.Mutation(SampleChunkProtocol.OpenKind,
                SampleChunkRetentionClusterProtocol.FirstRevision)], false, token);
        var append = SampleChunkRetentionClusterSeed.Request(scenario, new AppendSamples(TimeSeriesClusterScenario.Set,
            TimeSeriesClusterScenario.Series, [sample], TimeSeriesClusterScenario.PrivateTags));
        var receipt = await SampleChunkRetentionClusterWriteOracle.CommitAsync(sdk, mcp, append,
            [SampleChunkRetentionClusterSeed.Mutation(SampleChunkRetentionClusterProtocol.AppendKind,
                SampleChunkRetentionClusterProtocol.FreshSequence)], true, token);
        await SampleChunkRetentionClusterWriteOracle.CommitAsync(sdk, mcp,
            SampleChunkRetentionClusterSeed.Request(scenario, new RefreshSampleRollup(TimeSeriesClusterScenario.Set,
                TimeSeriesClusterScenario.Series, scenario.Until, until, SampleChunkRetentionClusterProtocol.AbsentRevision,
                SampleChunkClusterProtocol.OutputLimit)),
            [SampleChunkRetentionClusterSeed.Mutation(SampleRollupProtocol.RefreshKind,
                SampleChunkRetentionClusterProtocol.FirstRevision)], false, token);
        var continuation = new SampleChunkRetentionClusterContinuation(scenario, original.Append,
            original.Receipt, id, sample, append, receipt, retained);
        await SampleChunkRetentionClusterHealthy.RequireAsync(continuation, sdk, mcp, token);
        return continuation;
    }
}
