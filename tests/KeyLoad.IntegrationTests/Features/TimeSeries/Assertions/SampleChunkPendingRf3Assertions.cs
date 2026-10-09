using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleChunkPendingRf3Assertions
{
    internal static SampleChunkWindowResult Expected(SampleChunkPendingRf3Item item, bool merged)
        => new(item.WindowId, item.Scope.Window.From, item.Scope.Window.Until,
            merged ? SampleChunkRf3Protocol.MergedGeneration : SampleChunkRf3Protocol.SealedGeneration,
            merged ? SampleChunkRf3Protocol.MergedRevision : SampleChunkRf3Protocol.CorrectedRevision,
            SampleChunkRf3Protocol.CorrectedSequence, null, [.. item.Expected], default);

    internal static async Task PendingAsync(SampleChunkPendingRf3Item item, KeyLoadClient sdk, CancellationToken token)
    {
        var actual = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadSampleChunkWindowAsync(item.Request, token)
            .ConfigureAwait(false));
        var minimum = item.CorrectionReceipt ?? throw new InvalidOperationException(SampleChunkPendingRf3Protocol.Missing);
        await Assert.That(actual.CutPosition >= minimum.Token.Position).IsTrue();
        await SampleChunkRetentionRf3ReadOracle.EqualAsync(actual, Expected(item, merged: false) with { CutPosition = actual.CutPosition });
    }

    internal static async Task CompleteAsync(SampleChunkPendingRf3Item item, KeyLoadClient sdk,
        McpOfficialClient mcp, bool merged, CancellationToken token)
    {
        var minimum = item.CorrectionReceipt ?? throw new InvalidOperationException(SampleChunkPendingRf3Protocol.Missing);
        await SampleChunkRetentionRf3ReadOracle.WindowAsync(sdk, mcp, item.Request, Expected(item, merged), minimum, token);
        await SampleChunkRetentionRf3ReadOracle.RawAsync(sdk, mcp, new(item.Partition, TimeSeriesRf3Scenario.Set,
            item.Series, item.Scope.Window.From, item.Scope.Window.Until, SampleChunkRf3Protocol.OutputLimit), item.Expected, token);
        await SampleChunkRetentionRf3WriteOracle.ReplayAsync(sdk, mcp,
            item.OriginalSeal ?? throw new InvalidOperationException(SampleChunkPendingRf3Protocol.Missing),
            item.SealReceipt ?? throw new InvalidOperationException(SampleChunkPendingRf3Protocol.Missing), token);
        await SampleChunkRetentionRf3WriteOracle.ReplayAsync(sdk, mcp,
            item.OriginalCorrection ?? throw new InvalidOperationException(SampleChunkPendingRf3Protocol.Missing), minimum, token);
        await SampleChunkRetentionRf3ReadOracle.WindowAsync(sdk, mcp, item.Request, Expected(item, merged), minimum, token);
    }

    internal static async Task WaitMergedAsync(SampleChunkPendingRf3Item item, KeyLoadClient sdk, CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var actual = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadSampleChunkWindowAsync(item.Request, token)
                .ConfigureAwait(false));
            if (actual.Generation == SampleChunkRf3Protocol.MergedGeneration) { return; }
            await PendingAsync(item, sdk, token).ConfigureAwait(false);
            await Task.Delay(SampleChunkRf3Protocol.PollInterval, token).ConfigureAwait(false);
        }
    }
}
