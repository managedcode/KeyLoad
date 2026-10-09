using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleChunkRetentionRf3Seed
{
    internal static async Task<SampleChunkRetentionRf3Original> RunAsync(ClusterFixture fixture, KeyLoadClient sdk,
        McpOfficialClient mcp, CancellationToken token)
    {
        var native = await TimeSeriesRf3Scenario.CreateAsync(fixture, token).ConfigureAwait(false);
        var scenario = new SampleChunkRf3Scenario(native, Guid.NewGuid(), TimeSeriesRf3Scenario.Start,
            TimeSeriesRf3Scenario.Start.AddHours(SampleChunkRf3Protocol.WindowHours));
        await scenario.CommitAsync(sdk, new OpenSampleChunkWindow(TimeSeriesRf3Scenario.Set,
            TimeSeriesRf3Scenario.Series, scenario.WindowId, scenario.From, scenario.Until), token).ConfigureAwait(false);
        var original = new CommandRequest(Guid.NewGuid(), native.Partition,
        [new AppendSamples(TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series,
            [scenario.First, scenario.Equal, Late(scenario)], TimeSeriesRf3Scenario.PrivateTags)]);
        var receipt = await SampleChunkRetentionRf3WriteOracle.CommitAsync(sdk, mcp, original,
            [Mutation(SampleChunkRetentionRf3Protocol.AppendKind, SampleChunkRf3Protocol.CorrectedSequence)], false, token);
        var actual = await scenario.WaitForActualMergeAsync(sdk, token).ConfigureAwait(false);
        await SampleChunkRetentionRf3ReadOracle.WindowAsync(sdk, mcp, scenario.Request,
            new(scenario.WindowId, scenario.From, scenario.Until, SampleChunkRf3Protocol.MergedGeneration,
                SampleChunkRf3Protocol.MergedRevision, SampleChunkRf3Protocol.CorrectedSequence, null,
                [new(TimeSeriesRf3Scenario.Series, scenario.First, SampleChunkRf3Protocol.FirstSequence, TimeSeriesRf3Scenario.PrivateTags),
                 new(TimeSeriesRf3Scenario.Series, scenario.Equal, SampleChunkRf3Protocol.InitialSequence, TimeSeriesRf3Scenario.PrivateTags),
                 new(TimeSeriesRf3Scenario.Series, Late(scenario), SampleChunkRf3Protocol.CorrectedSequence, TimeSeriesRf3Scenario.PrivateTags)],
                actual.CutPosition), receipt, token);
        return new(scenario, original, receipt);
    }

    internal static SampleData Late(SampleChunkRf3Scenario scenario)
        => scenario.Late with { Value = SampleChunkRetentionRf3Protocol.LateValue };
    internal static MutationReceipt Mutation(string kind, long revision)
        => new(kind, TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series, revision);
    internal static CommandRequest Request(SampleChunkRf3Scenario scenario, Mutation mutation)
        => new(Guid.NewGuid(), scenario.Native.Partition, [mutation]);
}
