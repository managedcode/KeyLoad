using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleChunkSubnormalRf3Receipts
{
    internal static async Task<AtomicPartitionPlacementResolution> PlacementAsync(
        SampleChunkRf3Scenario scenario, KeyLoadClient sdk, McpOfficialClient mcp, CancellationToken token)
    {
        var request = new AtomicPartitionPlacementReadRequest(SampleChunkRf3Protocol.PlacementVersion,
            scenario.Native.Partition);
        var placement = await McpCallerAssertions.SdkSuccessAsync(
            await sdk.ReadAtomicPartitionPlacementAsync(request, token).ConfigureAwait(false));
        await SampleChunkRetentionRf3ReadOracle.EqualAsync(placement,
            (await McpCallerAssertions.SuccessAsync<AtomicPartitionPlacementResolution>(
                await mcp.CallAsync(McpCallerTools.AdminPartitionPlacementRead, request, token).ConfigureAwait(false))).Value);
        await Assert.That(placement.Partition).IsEqualTo(scenario.Native.Partition);
        return placement;
    }

    internal static async Task RequireAsync(AtomicPartitionPlacementResolution owner,
        CommandRequest original, CommitReceipt actual, string kind, string series, long revision,
        CommitReceipt? previous = null)
    {
        var minimum = previous?.Token.Position ?? SampleChunkSubnormalRf3Corpus.EmptySequence;
        await Assert.That(actual.Token.Position > minimum).IsTrue();
        var expected = new CommitReceipt(original.CommandId, new CommitToken(owner.Incarnation,
            original.Partition.AtomicPartitionId, actual.Token.Position, owner.PlacementEpoch),
            [new MutationReceipt(kind, TimeSeriesRf3Scenario.Set, series, revision)],
            DurabilityProfile.QuorumProcessDurable);
        await SampleChunkRetentionRf3ReadOracle.EqualAsync(actual, expected);
    }

    internal static async Task ReplayAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        CommandRequest command, CommitReceipt original, Func<Task> observe, CancellationToken token)
    {
        await SampleChunkRetentionRf3ReadOracle.EqualAsync(
            await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(command, token).ConfigureAwait(false)), original);
        await observe();
        var official = await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token).ConfigureAwait(false));
        await SampleChunkRetentionRf3ReadOracle.EqualAsync(official.Value, original);
        await observe();
    }
}
