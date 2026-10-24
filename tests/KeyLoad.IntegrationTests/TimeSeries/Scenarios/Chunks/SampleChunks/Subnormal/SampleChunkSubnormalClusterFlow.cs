namespace KeyLoad.IntegrationTests.TimeSeries;

internal static class SampleChunkSubnormalClusterFlow
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var original = await SampleChunkSubnormalClusterCallers.RunOwnedAsync(fixture,
            (sdk, mcp) => SampleChunkSubnormalClusterSeed.RunAsync(fixture, sdk, mcp, token), token);
        await SampleChunkSubnormalClusterCold.RunAsync(fixture, original, token);
        await SampleChunkSubnormalClusterCold.RunAsync(fixture, original, token);
        await SampleChunkSubnormalClusterCallers.RunOwnedAsync(fixture, async (sdk, mcp) =>
        {
            var fresh = SampleChunkSubnormalClusterSeed.Request(original.Scenario,
                new AppendSamples(TimeSeriesClusterScenario.Set, TimeSeriesClusterScenario.EmptySeries,
                    [SampleChunkSubnormalClusterCorpus.Healthy(original.Scenario)], TimeSeriesClusterScenario.PrivateTags));
            var receipt = await SampleChunkSubnormalClusterSeed.CommitAsync(sdk, fresh, token);
            await SampleChunkSubnormalClusterReceipts.RequireAsync(fresh, receipt,
                SampleChunkRetentionClusterProtocol.AppendKind, TimeSeriesClusterScenario.EmptySeries,
                SampleChunkClusterProtocol.FirstSequence, original.CorrectionReceipt);
            Task ObserveAsync() => SampleChunkSubnormalClusterResults.HealthyAsync(original, sdk, mcp, receipt, token);
            await ObserveAsync();
            await SampleChunkSubnormalClusterReceipts.ReplayAsync(sdk, mcp, fresh, receipt, ObserveAsync, token);
            await SampleChunkSubnormalClusterReplay.RunAsync(original, sdk, mcp, receipt, true, token);
            return original;
        }, token);
    }
}
