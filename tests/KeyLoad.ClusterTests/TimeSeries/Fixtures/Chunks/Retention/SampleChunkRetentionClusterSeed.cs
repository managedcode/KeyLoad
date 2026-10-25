using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.TimeSeries;

internal static class SampleChunkRetentionClusterSeed
{
    internal static async Task<SampleChunkRetentionClusterOriginal> RunAsync(ClusterFixture fixture, KeyLoadClient sdk,
        McpOfficialClient mcp, CancellationToken token)
    {
        var execution = await TimeSeriesClusterScenario.CreateAsync(fixture, token).ConfigureAwait(false);
        var scenario = new SampleChunkClusterScenario(execution, Guid.NewGuid(), TimeSeriesClusterScenario.Start,
            TimeSeriesClusterScenario.Start.AddHours(SampleChunkClusterProtocol.WindowHours));
        await scenario.CommitAsync(sdk, new OpenSampleChunkWindow(TimeSeriesClusterScenario.Set,
            TimeSeriesClusterScenario.Series, scenario.WindowId, scenario.From, scenario.Until), token).ConfigureAwait(false);
        var original = new CommandRequest(Guid.NewGuid(), execution.Partition,
        [new AppendSamples(TimeSeriesClusterScenario.Set, TimeSeriesClusterScenario.Series,
            [scenario.First, scenario.Equal, Late(scenario)], TimeSeriesClusterScenario.PrivateTags)]);
        var receipt = await SampleChunkRetentionClusterWriteOracle.CommitAsync(sdk, mcp, original,
            [Mutation(SampleChunkRetentionClusterProtocol.AppendKind, SampleChunkClusterProtocol.CorrectedSequence)], false, token);
        var actual = await scenario.WaitForActualMergeAsync(sdk, token).ConfigureAwait(false);
        await SampleChunkRetentionClusterReadOracle.WindowAsync(sdk, mcp, scenario.Request,
            new(scenario.WindowId, scenario.From, scenario.Until, SampleChunkClusterProtocol.MergedGeneration,
                SampleChunkClusterProtocol.MergedRevision, SampleChunkClusterProtocol.CorrectedSequence, null,
                [new(TimeSeriesClusterScenario.Series, scenario.First, SampleChunkClusterProtocol.FirstSequence, TimeSeriesClusterScenario.PrivateTags),
                 new(TimeSeriesClusterScenario.Series, scenario.Equal, SampleChunkClusterProtocol.InitialSequence, TimeSeriesClusterScenario.PrivateTags),
                 new(TimeSeriesClusterScenario.Series, Late(scenario), SampleChunkClusterProtocol.CorrectedSequence, TimeSeriesClusterScenario.PrivateTags)],
                actual.CutPosition), receipt, token);
        return new(scenario, original, receipt);
    }

    internal static SampleData Late(SampleChunkClusterScenario scenario)
        => scenario.Late with
        {
            Value = SampleChunkRetentionClusterProtocol.LateValue
        };
    internal static MutationReceipt Mutation(string kind, long revision)
        => new(kind, TimeSeriesClusterScenario.Set, TimeSeriesClusterScenario.Series, revision);
    internal static CommandRequest Request(SampleChunkClusterScenario scenario, Mutation mutation)
        => new(Guid.NewGuid(), scenario.Execution.Partition, [mutation]);
}
