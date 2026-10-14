using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleChunkSubnormalRf3Seed
{
    internal static async Task<SampleChunkSubnormalRf3State> RunAsync(ClusterFixture fixture,
        KeyLoadClient sdk, McpOfficialClient mcp, CancellationToken token)
    {
        var native = await TimeSeriesRf3Scenario.CreateAsync(fixture, token).ConfigureAwait(false);
        var from = TimeProvider.System.GetUtcNow();
        var scenario = new SampleChunkRf3Scenario(native, Guid.NewGuid(), from,
            from.AddHours(SampleChunkRf3Protocol.WindowHours));
        var owner = await SampleChunkSubnormalRf3Receipts.PlacementAsync(scenario, sdk, mcp, token);
        var open = Request(scenario, new OpenSampleChunkWindow(TimeSeriesRf3Scenario.Set,
            TimeSeriesRf3Scenario.Series, scenario.WindowId, scenario.From, scenario.Until));
        var opened = await CommitAsync(sdk, open, token);
        await SampleChunkSubnormalRf3Receipts.RequireAsync(owner, open, opened, SampleChunkProtocol.OpenKind,
            TimeSeriesRf3Scenario.Series, SampleChunkRf3Protocol.OpenRevision);
        var append = Request(scenario, new AppendSamples(TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series,
            [SampleChunkSubnormalRf3Corpus.First(scenario), SampleChunkSubnormalRf3Corpus.Equal(scenario)],
            TimeSeriesRf3Scenario.PrivateTags));
        var appended = await CommitAsync(sdk, append, token);
        await SampleChunkSubnormalRf3Receipts.RequireAsync(owner, append, appended,
            SampleChunkRetentionRf3Protocol.AppendKind, TimeSeriesRf3Scenario.Series,
            SampleChunkRf3Protocol.InitialSequence, opened);
        await SampleChunkRetentionRf3ReadOracle.RawAsync(sdk, mcp,
            SampleChunkSubnormalRf3Results.Raw(scenario, TimeSeriesRf3Scenario.Series),
            SampleChunkSubnormalRf3Corpus.Initial(scenario), token);
        var seal = Request(scenario, new SealSampleChunkWindow(TimeSeriesRf3Scenario.Set,
            TimeSeriesRf3Scenario.Series, scenario.WindowId, SampleChunkRf3Protocol.AppendedRevision));
        var sealedReceipt = await CommitAsync(sdk, seal, token);
        await SampleChunkSubnormalRf3Receipts.RequireAsync(owner, seal, sealedReceipt, SampleChunkProtocol.SealKind,
            TimeSeriesRf3Scenario.Series, SampleChunkRf3Protocol.SealedRevision, appended);
        var correction = Request(scenario, new AppendSamples(TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series,
            [scenario.Late], TimeSeriesRf3Scenario.PrivateTags));
        await SampleChunkSubnormalRf3Results.SealedAsync(scenario, sealedReceipt, sdk, mcp, token);
        var corrected = await CommitAsync(sdk, correction, token);
        await SampleChunkSubnormalRf3Receipts.RequireAsync(owner, correction, corrected,
            SampleChunkRetentionRf3Protocol.AppendKind, TimeSeriesRf3Scenario.Series,
            SampleChunkRf3Protocol.CorrectedSequence, sealedReceipt);
        var state = new SampleChunkSubnormalRf3State(scenario, owner, open, opened, append, appended,
            seal, sealedReceipt, correction, corrected);
        await scenario.WaitForActualMergeAsync(sdk, token).ConfigureAwait(false);
        await SampleChunkSubnormalRf3Replay.RunAsync(state, sdk, mcp, corrected, false, token);
        return state;
    }

    internal static CommandRequest Request(SampleChunkRf3Scenario scenario, Mutation mutation)
        => new(Guid.NewGuid(), scenario.Native.Partition, [mutation]);
    internal static async Task<CommitReceipt> CommitAsync(KeyLoadClient sdk, CommandRequest request, CancellationToken token)
        => await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(request, token).ConfigureAwait(false));
}
