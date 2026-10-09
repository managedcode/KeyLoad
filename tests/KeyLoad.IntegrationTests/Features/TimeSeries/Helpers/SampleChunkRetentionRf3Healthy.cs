using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleChunkRetentionRf3Healthy
{
    internal static async Task RequireAsync(SampleChunkRetentionRf3Continuation original, KeyLoadClient sdk,
        McpOfficialClient mcp, CancellationToken token)
    {
        var scenario = original.Original;
        var until = original.FreshSample.Timestamp.AddHours(SampleChunkRf3Protocol.WindowHours);
        var rows = new SampleRecord[] { new(TimeSeriesRf3Scenario.Series, original.FreshSample,
            SampleChunkRetentionRf3Protocol.FreshSequence, TimeSeriesRf3Scenario.PrivateTags) };
        await SampleChunkRetentionRf3ReadOracle.WindowAsync(sdk, mcp,
            scenario.Request with { WindowId = original.FreshWindowId },
            new(original.FreshWindowId, scenario.Until, until, SampleChunkRetentionRf3Protocol.OpenGeneration,
                SampleChunkRetentionRf3Protocol.FreshWindowRevision, SampleChunkRetentionRf3Protocol.FreshSequence,
                scenario.Until.UtcTicks, [.. rows], original.OriginalAppendReceipt.Token.Position),
            original.OriginalAppendReceipt, token);
        await SampleChunkRetentionRf3ReadOracle.RawAsync(sdk, mcp,
            new(scenario.Native.Partition, TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series,
                scenario.From, until, SampleChunkRf3Protocol.OutputLimit), rows, token);
        await SampleChunkRetentionRf3ReadOracle.RollupAsync(sdk, mcp,
            new(scenario.Native.Partition, TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series, scenario.Until, until),
            new(SampleChunkRetentionRf3Protocol.FirstRevision,
                new(scenario.Until, until, SampleChunkRetentionRf3Protocol.FreshSequence, scenario.Until,
                    new(SampleChunkRetentionRf3Protocol.RemainingCount, SampleChunkRetentionRf3Protocol.FreshValue,
                        SampleChunkRetentionRf3Protocol.FreshValue, SampleChunkRetentionRf3Protocol.FreshValue,
                        SampleChunkRetentionRf3Protocol.FreshValue))), token);
        await SampleChunkRetentionRf3ReadOracle.RollupAsync(sdk, mcp,
            new(scenario.Native.Partition, TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series,
                SampleChunkRetentionRf3Seed.Late(scenario).Timestamp, scenario.Until),
            new(SampleChunkRetentionRf3Protocol.DroppedRevision, null), token);
        await SampleChunkRetentionRf3DenialOracle.RollupAsync(sdk, mcp,
            new(scenario.Native.Partition, TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series, scenario.From, scenario.Until), token);
        await SampleChunkRetentionRf3DenialOracle.WindowAsync(sdk, mcp, scenario.Request, token);
        await SampleChunkRetentionRf3WriteOracle.ReplayAsync(sdk, mcp, original.OriginalSourceRequest, original.OriginalSourceReceipt, token);
        await SampleChunkRetentionRf3WriteOracle.ReplayAsync(sdk, mcp, original.OriginalAppend, original.OriginalAppendReceipt, token);
        await SampleChunkRetentionRf3DenialOracle.WindowAsync(sdk, mcp, scenario.Request, token);
    }
}
