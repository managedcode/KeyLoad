using System.Globalization;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleChunkPendingRf3Seed
{
    internal static async Task<SampleChunkPendingRf3Item> CreateAsync(SampleChunkJobRevocationScenario scope,
        int ordinal, KeyLoadClient creator, KeyLoadClient administrator, CancellationToken token)
    {
        var item = new SampleChunkPendingRf3Item(scope, SampleChunkPendingRf3Protocol.SeriesPrefix
            + ordinal.ToString(CultureInfo.InvariantCulture), Guid.NewGuid());
        await CommitAsync(creator, administrator, new(Guid.NewGuid(), item.Partition,
            [new OpenSampleChunkWindow(TimeSeriesRf3Scenario.Set, item.Series, item.WindowId,
                scope.Window.From, scope.Window.Until)]),
            new(SampleChunkProtocol.OpenKind, TimeSeriesRf3Scenario.Set, item.Series, SampleChunkRf3Protocol.OpenRevision), token);
        await CommitAsync(creator, administrator, new(Guid.NewGuid(), item.Partition,
            [new AppendSamples(TimeSeriesRf3Scenario.Set, item.Series,
                [scope.Window.First, scope.Window.Equal], TimeSeriesRf3Scenario.PrivateTags)]),
            new(SampleChunkPendingRf3Protocol.AppendKind, TimeSeriesRf3Scenario.Set, item.Series,
                SampleChunkRf3Protocol.InitialSequence), token);
        item.OriginalSeal = new(Guid.NewGuid(), item.Partition,
            [new SealSampleChunkWindow(TimeSeriesRf3Scenario.Set, item.Series, item.WindowId,
                SampleChunkRf3Protocol.AppendedRevision)]);
        item.SealReceipt = await CommitAsync(creator, administrator, item.OriginalSeal,
            new(SampleChunkProtocol.SealKind, TimeSeriesRf3Scenario.Set, item.Series, SampleChunkRf3Protocol.SealedRevision), token);
        return item;
    }

    internal static async Task CorrectAsync(SampleChunkPendingRf3Item item, KeyLoadClient administrator,
        CancellationToken token)
    {
        item.OriginalCorrection = new(Guid.NewGuid(), item.Partition,
            [new AppendSamples(TimeSeriesRf3Scenario.Set, item.Series,
                [item.Scope.Window.Late], TimeSeriesRf3Scenario.PrivateTags)]);
        item.CorrectionReceipt = await CommitAsync(administrator, administrator, item.OriginalCorrection,
            new(SampleChunkPendingRf3Protocol.AppendKind, TimeSeriesRf3Scenario.Set, item.Series,
                SampleChunkRf3Protocol.CorrectedSequence), token);
    }

    private static async Task<CommitReceipt> CommitAsync(KeyLoadClient sdk, KeyLoadClient administrator, CommandRequest original,
        MutationReceipt expectedMutation, CancellationToken token)
    {
        var actual = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(original, token).ConfigureAwait(false));
        var owner = await McpCallerAssertions.SdkSuccessAsync(await administrator.ReadAtomicPartitionPlacementAsync(
            new(SampleChunkRf3Protocol.PlacementVersion, original.Partition), token).ConfigureAwait(false));
        var expected = new CommitReceipt(original.CommandId, new(owner.Incarnation, original.Partition.AtomicPartitionId,
            actual.Token.Position, owner.PlacementEpoch), [expectedMutation], DurabilityProfile.QuorumProcessDurable);
        await SampleChunkRetentionRf3ReadOracle.EqualAsync(actual, expected);
        return actual;
    }
}
