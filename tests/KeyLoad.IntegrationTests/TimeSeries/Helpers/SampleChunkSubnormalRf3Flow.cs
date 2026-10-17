namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleChunkSubnormalRf3Flow
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var original = await SampleChunkSubnormalRf3Callers.RunOwnedAsync(fixture,
            (sdk, mcp) => SampleChunkSubnormalRf3Seed.RunAsync(fixture, sdk, mcp, token), token);
        await SampleChunkSubnormalRf3Cold.RunAsync(fixture, original, token);
        await SampleChunkSubnormalRf3Cold.RunAsync(fixture, original, token);
        await SampleChunkSubnormalRf3Callers.RunOwnedAsync(fixture, async (sdk, mcp) =>
        {
            var fresh = SampleChunkSubnormalRf3Seed.Request(original.Scenario,
                new AppendSamples(TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.EmptySeries,
                    [SampleChunkSubnormalRf3Corpus.Healthy(original.Scenario)], TimeSeriesRf3Scenario.PrivateTags));
            var receipt = await SampleChunkSubnormalRf3Seed.CommitAsync(sdk, fresh, token);
            await SampleChunkSubnormalRf3Receipts.RequireAsync(original.Placement, fresh, receipt,
                SampleChunkRetentionRf3Protocol.AppendKind, TimeSeriesRf3Scenario.EmptySeries,
                SampleChunkRf3Protocol.FirstSequence, original.CorrectionReceipt);
            Task ObserveAsync() => SampleChunkSubnormalRf3Results.HealthyAsync(original, sdk, mcp, receipt, token);
            await ObserveAsync();
            await SampleChunkSubnormalRf3Receipts.ReplayAsync(sdk, mcp, fresh, receipt, ObserveAsync, token);
            await SampleChunkSubnormalRf3Replay.RunAsync(original, sdk, mcp, receipt, true, token);
            return original;
        }, token);
    }
}
