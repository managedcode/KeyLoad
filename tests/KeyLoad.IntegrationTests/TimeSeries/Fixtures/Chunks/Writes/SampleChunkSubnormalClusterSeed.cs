using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleChunkSubnormalClusterSeed
{
    internal static async Task<SampleChunkSubnormalClusterState> RunAsync(ClusterFixture fixture,
        KeyLoadClient sdk, McpOfficialClient mcp, CancellationToken token)
    {
        var native = await TimeSeriesClusterScenario.CreateAsync(fixture, token).ConfigureAwait(false);
        var from = TimeProvider.System.GetUtcNow();
        var scenario = new SampleChunkClusterScenario(native, Guid.NewGuid(), from,
            from.AddHours(SampleChunkClusterProtocol.WindowHours));
        var open = Request(scenario, new OpenSampleChunkWindow(TimeSeriesClusterScenario.Set,
            TimeSeriesClusterScenario.Series, scenario.WindowId, scenario.From, scenario.Until));
        var opened = await CommitAsync(sdk, open, token);
        await SampleChunkSubnormalClusterReceipts.RequireAsync(open, opened, SampleChunkProtocol.OpenKind,
            TimeSeriesClusterScenario.Series, SampleChunkClusterProtocol.OpenRevision);
        var append = Request(scenario, new AppendSamples(TimeSeriesClusterScenario.Set, TimeSeriesClusterScenario.Series,
            [SampleChunkSubnormalClusterCorpus.First(scenario), SampleChunkSubnormalClusterCorpus.Equal(scenario)],
            TimeSeriesClusterScenario.PrivateTags));
        var appended = await CommitAsync(sdk, append, token);
        await SampleChunkSubnormalClusterReceipts.RequireAsync(append, appended,
            SampleChunkRetentionClusterProtocol.AppendKind, TimeSeriesClusterScenario.Series,
            SampleChunkClusterProtocol.InitialSequence, opened);
        await SampleChunkRetentionClusterReadOracle.RawAsync(sdk, mcp,
            SampleChunkSubnormalClusterResults.Raw(scenario, TimeSeriesClusterScenario.Series),
            SampleChunkSubnormalClusterCorpus.Initial(scenario), token);
        var seal = Request(scenario, new SealSampleChunkWindow(TimeSeriesClusterScenario.Set,
            TimeSeriesClusterScenario.Series, scenario.WindowId, SampleChunkClusterProtocol.AppendedRevision));
        var sealedReceipt = await CommitAsync(sdk, seal, token);
        await SampleChunkSubnormalClusterReceipts.RequireAsync(seal, sealedReceipt, SampleChunkProtocol.SealKind,
            TimeSeriesClusterScenario.Series, SampleChunkClusterProtocol.SealedRevision, appended);
        var correction = Request(scenario, new AppendSamples(TimeSeriesClusterScenario.Set, TimeSeriesClusterScenario.Series,
            [scenario.Late], TimeSeriesClusterScenario.PrivateTags));
        await SampleChunkSubnormalClusterResults.SealedAsync(scenario, sealedReceipt, sdk, mcp, token);
        var corrected = await CommitAsync(sdk, correction, token);
        await SampleChunkSubnormalClusterReceipts.RequireAsync(correction, corrected,
            SampleChunkRetentionClusterProtocol.AppendKind, TimeSeriesClusterScenario.Series,
            SampleChunkClusterProtocol.CorrectedSequence, sealedReceipt);
        var state = new SampleChunkSubnormalClusterState(scenario, open, opened, append, appended,
            seal, sealedReceipt, correction, corrected);
        await scenario.WaitForActualMergeAsync(sdk, token).ConfigureAwait(false);
        await SampleChunkSubnormalClusterReplay.RunAsync(state, sdk, mcp, corrected, false, token);
        return state;
    }

    internal static CommandRequest Request(SampleChunkClusterScenario scenario, Mutation mutation)
        => new(Guid.NewGuid(), scenario.Native.Partition, [mutation]);
    internal static async Task<CommitReceipt> CommitAsync(KeyLoadClient sdk, CommandRequest request, CancellationToken token)
        => await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(request, token).ConfigureAwait(false));
}
