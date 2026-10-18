using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class ChunkMultiblockClusterReceipt
{
    private const long NoCommittedPosition = 0;

    internal static async Task<CommitReceipt> FirstAsync(RequestCqrsClusterCallers administrator,
        CommandRequest command, MutationReceipt mutation, bool official, CancellationToken token)
    {
        var actual = official ? (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await administrator.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token))).Value
            : await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(command, token));
        await PositiveAsync(actual);
        var expected = new CommitReceipt(command.CommandId, new(actual.Token.Incarnation, command.Partition.AtomicPartitionId,
            actual.Token.Position), [mutation], DurabilityProfile.ProcessDurable, 3);
        await SampleChunkRetentionClusterReadOracle.EqualAsync(actual, expected);
        return actual;
    }

    internal static async Task PositiveAsync(CommitReceipt actual)
        => await Assert.That(actual.Token.Position).IsGreaterThan(NoCommittedPosition);
}
