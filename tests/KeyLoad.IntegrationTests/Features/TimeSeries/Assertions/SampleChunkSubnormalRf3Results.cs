using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleChunkSubnormalRf3Results
{
    internal static async Task SealedAsync(SampleChunkRf3Scenario scenario, CommitReceipt seal,
        KeyLoadClient sdk, McpOfficialClient mcp, CancellationToken token)
        => await CompleteAsync(scenario, sdk, mcp, seal,
            SampleChunkRf3Protocol.SealedGeneration, SampleChunkRf3Protocol.SealedRevision,
            SampleChunkRf3Protocol.InitialSequence, SampleChunkSubnormalRf3Corpus.Initial(scenario), token);

    internal static async Task CurrentAsync(SampleChunkSubnormalRf3State original,
        KeyLoadClient sdk, McpOfficialClient mcp, CommitReceipt minimum, CancellationToken token)
        => await CompleteAsync(original.Scenario, sdk, mcp, minimum,
            SampleChunkRf3Protocol.MergedGeneration, SampleChunkRf3Protocol.MergedRevision,
            SampleChunkRf3Protocol.CorrectedSequence, SampleChunkSubnormalRf3Corpus.Corrected(original.Scenario), token);

    private static async Task CompleteAsync(SampleChunkRf3Scenario scenario, KeyLoadClient sdk,
        McpOfficialClient mcp, CommitReceipt minimum, long generation, long revision,
        long sequence, SampleRecord[] expected, CancellationToken token)
    {
        var window = new SampleChunkWindowResult(scenario.WindowId, scenario.From, scenario.Until,
            generation, revision, sequence, null, [.. expected], minimum.Token.Position);
        await SampleChunkRetentionRf3ReadOracle.WindowAsync(sdk, mcp, scenario.Request, window, minimum, token);
        await SampleChunkRetentionRf3ReadOracle.RawAsync(sdk, mcp, Raw(scenario, TimeSeriesRf3Scenario.Series), expected, token);
    }

    internal static ReadSamplesRequest Raw(SampleChunkRf3Scenario scenario, string series)
        => new(scenario.Native.Partition, TimeSeriesRf3Scenario.Set, series,
            scenario.From, scenario.Until, SampleChunkRf3Protocol.OutputLimit);

    internal static async Task HealthyAsync(SampleChunkSubnormalRf3State original,
        KeyLoadClient sdk, McpOfficialClient mcp, CommitReceipt minimum, CancellationToken token)
    {
        await CurrentAsync(original, sdk, mcp, minimum, token);
        await SampleChunkRetentionRf3ReadOracle.RawAsync(sdk, mcp,
            Raw(original.Scenario, TimeSeriesRf3Scenario.EmptySeries),
            SampleChunkSubnormalRf3Corpus.HealthyRecords(original.Scenario), token);
    }
}
