using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.TimeSeries;

internal static class SampleChunkSubnormalClusterResults
{
    internal static async Task SealedAsync(SampleChunkClusterScenario scenario, CommitReceipt seal,
        KeyLoadClient sdk, McpOfficialClient mcp, CancellationToken token)
        => await CompleteAsync(scenario, sdk, mcp, seal,
            SampleChunkClusterProtocol.SealedGeneration, SampleChunkClusterProtocol.SealedRevision,
            SampleChunkClusterProtocol.InitialSequence, SampleChunkSubnormalClusterCorpus.Initial(scenario), token);

    internal static async Task CurrentAsync(SampleChunkSubnormalClusterState original,
        KeyLoadClient sdk, McpOfficialClient mcp, CommitReceipt minimum, CancellationToken token)
        => await CompleteAsync(original.Scenario, sdk, mcp, minimum,
            SampleChunkClusterProtocol.MergedGeneration, SampleChunkClusterProtocol.MergedRevision,
            SampleChunkClusterProtocol.CorrectedSequence, SampleChunkSubnormalClusterCorpus.Corrected(original.Scenario), token);

    private static async Task CompleteAsync(SampleChunkClusterScenario scenario, KeyLoadClient sdk,
        McpOfficialClient mcp, CommitReceipt minimum, long generation, long revision,
        long sequence, SampleRecord[] expected, CancellationToken token)
    {
        var window = new SampleChunkWindowResult(scenario.WindowId, scenario.From, scenario.Until,
            generation, revision, sequence, null, [.. expected], minimum.Token.Position);
        async Task CurrentRowsAsync()
        {
            await SampleChunkRetentionClusterReadOracle.WindowAsync(sdk, mcp, scenario.Request, window, minimum, token);
            await SampleChunkRetentionClusterReadOracle.RawAsync(sdk, mcp, Raw(scenario, TimeSeriesClusterScenario.Series), expected, token);
        }
        await CurrentRowsAsync();
        var count = generation == SampleChunkClusterProtocol.SealedGeneration
            ? SubnormalWholeAggregateClusterOracle.SealedCount : SubnormalWholeAggregateClusterOracle.CorrectedCount;
        await SubnormalWholeAggregateClusterOracle.PairAsync(scenario, sdk, mcp, count, CurrentRowsAsync, token);
    }

    internal static ReadSamplesRequest Raw(SampleChunkClusterScenario scenario, string series)
        => new(scenario.Execution.Partition, TimeSeriesClusterScenario.Set, series,
            scenario.From, scenario.Until, SampleChunkClusterProtocol.OutputLimit);

    internal static async Task HealthyAsync(SampleChunkSubnormalClusterState original,
        KeyLoadClient sdk, McpOfficialClient mcp, CommitReceipt minimum, CancellationToken token)
    {
        await CurrentAsync(original, sdk, mcp, minimum, token);
        Task HealthyRowsAsync() => SampleChunkRetentionClusterReadOracle.RawAsync(sdk, mcp,
            Raw(original.Scenario, TimeSeriesClusterScenario.EmptySeries),
            SampleChunkSubnormalClusterCorpus.HealthyRecords(original.Scenario), token);
        await HealthyRowsAsync();
        await SubnormalWholeAggregateClusterOracle.HealthyAsync(original.Scenario, sdk, mcp, HealthyRowsAsync, token);
    }
}
