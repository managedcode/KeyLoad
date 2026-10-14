using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class ChunkMultiblockRf3Receipt
{
    private const long NoCommittedPosition = 0;

    internal static async Task<CommitReceipt> FirstAsync(RequestCqrsRf3Callers administrator,
        CommandRequest command, MutationReceipt mutation, bool official, CancellationToken token)
    {
        var owner = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ReadAtomicPartitionPlacementAsync(
            new(SampleChunkRf3Protocol.PlacementVersion, command.Partition), token));
        var actual = official ? (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await administrator.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token))).Value
            : await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(command, token));
        await PositiveAsync(actual);
        var expected = new CommitReceipt(command.CommandId, new(owner.Incarnation, command.Partition.AtomicPartitionId,
            actual.Token.Position, owner.PlacementEpoch), [mutation], DurabilityProfile.QuorumProcessDurable);
        await SampleChunkRetentionRf3ReadOracle.EqualAsync(actual, expected);
        return actual;
    }

    internal static async Task PositiveAsync(CommitReceipt actual)
        => await Assert.That(actual.Token.Position).IsGreaterThan(NoCommittedPosition);
}
