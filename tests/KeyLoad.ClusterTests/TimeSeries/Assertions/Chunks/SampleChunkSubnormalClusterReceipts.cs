using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.TimeSeries;

internal static class SampleChunkSubnormalClusterReceipts
{
    internal static async Task RequireAsync(CommandRequest original, CommitReceipt actual, string kind, string series, long revision,
        CommitReceipt? previous = null)
    {
        var minimum = previous?.Token.Position ?? SampleChunkSubnormalClusterCorpus.EmptySequence;
        await Assert.That(actual.Token.Position > minimum).IsTrue();
        var expected = new CommitReceipt(original.CommandId, new CommitToken(actual.Token.Incarnation,
            original.Partition.AtomicPartitionId, actual.Token.Position),
            [new MutationReceipt(kind, TimeSeriesClusterScenario.Set, series, revision)],
            DurabilityProfile.ProcessDurable, 3);
        await SampleChunkRetentionClusterReadOracle.EqualAsync(actual, expected);
    }

    internal static async Task ReplayAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        CommandRequest command, CommitReceipt original, Func<Task> observe, CancellationToken token)
    {
        await SampleChunkRetentionClusterReadOracle.EqualAsync(
            await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(command, token).ConfigureAwait(false)), original);
        await observe();
        var official = await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token).ConfigureAwait(false));
        await SampleChunkRetentionClusterReadOracle.EqualAsync(official.Value, original);
        await observe();
    }
}
