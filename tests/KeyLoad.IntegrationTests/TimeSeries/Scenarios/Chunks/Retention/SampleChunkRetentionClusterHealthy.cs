using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleChunkRetentionClusterHealthy
{
    internal static async Task RequireAsync(SampleChunkRetentionClusterContinuation original, KeyLoadClient sdk,
        McpOfficialClient mcp, CancellationToken token)
    {
        var scenario = original.Original;
        var until = original.FreshSample.Timestamp.AddHours(SampleChunkClusterProtocol.WindowHours);
        var rows = new SampleRecord[] { new(TimeSeriesClusterScenario.Series, original.FreshSample,
            SampleChunkRetentionClusterProtocol.FreshSequence, TimeSeriesClusterScenario.PrivateTags) };
        await SampleChunkRetentionClusterReadOracle.WindowAsync(sdk, mcp,
            scenario.Request with
            {
                WindowId = original.FreshWindowId
            },
            new(original.FreshWindowId, scenario.Until, until, SampleChunkRetentionClusterProtocol.OpenGeneration,
                SampleChunkRetentionClusterProtocol.FreshWindowRevision, SampleChunkRetentionClusterProtocol.FreshSequence,
                scenario.Until.UtcTicks, [.. rows], original.OriginalAppendReceipt.Token.Position),
            original.OriginalAppendReceipt, token);
        await SampleChunkRetentionClusterReadOracle.RawAsync(sdk, mcp,
            new(scenario.Native.Partition, TimeSeriesClusterScenario.Set, TimeSeriesClusterScenario.Series,
                scenario.From, until, SampleChunkClusterProtocol.OutputLimit), rows, token);
        await SampleChunkRetentionClusterReadOracle.RollupAsync(sdk, mcp,
            new(scenario.Native.Partition, TimeSeriesClusterScenario.Set, TimeSeriesClusterScenario.Series, scenario.Until, until),
            new(SampleChunkRetentionClusterProtocol.FirstRevision,
                new(scenario.Until, until, SampleChunkRetentionClusterProtocol.FreshSequence, scenario.Until,
                    new(SampleChunkRetentionClusterProtocol.RemainingCount, SampleChunkRetentionClusterProtocol.FreshValue,
                        SampleChunkRetentionClusterProtocol.FreshValue, SampleChunkRetentionClusterProtocol.FreshValue,
                        SampleChunkRetentionClusterProtocol.FreshValue))), token);
        await SampleChunkRetentionClusterReadOracle.RollupAsync(sdk, mcp,
            new(scenario.Native.Partition, TimeSeriesClusterScenario.Set, TimeSeriesClusterScenario.Series,
                SampleChunkRetentionClusterSeed.Late(scenario).Timestamp, scenario.Until),
            new(SampleChunkRetentionClusterProtocol.DroppedRevision, null), token);
        await SampleChunkRetentionClusterDenialOracle.RollupAsync(sdk, mcp,
            new(scenario.Native.Partition, TimeSeriesClusterScenario.Set, TimeSeriesClusterScenario.Series, scenario.From, scenario.Until), token);
        await SampleChunkRetentionClusterDenialOracle.WindowAsync(sdk, mcp, scenario.Request, token);
        await SampleChunkRetentionClusterWriteOracle.ReplayAsync(sdk, mcp, original.OriginalSourceRequest, original.OriginalSourceReceipt, token);
        await SampleChunkRetentionClusterWriteOracle.ReplayAsync(sdk, mcp, original.OriginalAppend, original.OriginalAppendReceipt, token);
        await SampleChunkRetentionClusterDenialOracle.WindowAsync(sdk, mcp, scenario.Request, token);
    }
}
