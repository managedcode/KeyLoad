using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleChunkRetentionRf3WriteOracle
{
    internal static async Task<CommitReceipt> CommitAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        CommandRequest request, MutationReceipt[] mutations, bool official, CancellationToken token)
    {
        var actual = official ? (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await mcp.CallAsync(McpCallerTools.DocumentsCommit, request, token).ConfigureAwait(false))).Value
            : await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(request, token).ConfigureAwait(false));
        var owner = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadAtomicPartitionPlacementAsync(
            new(SampleChunkRf3Protocol.PlacementVersion, request.Partition), token).ConfigureAwait(false));
        var expected = new CommitReceipt(request.CommandId, new(owner.Incarnation, request.Partition.AtomicPartitionId,
            actual.Token.Position, owner.PlacementEpoch),
            [.. mutations], DurabilityProfile.QuorumProcessDurable);
        await SampleChunkRetentionRf3ReadOracle.EqualAsync(actual, expected);
        await ReplayAsync(sdk, mcp, request, actual, token);
        return actual;
    }

    internal static async Task ReplayAsync(KeyLoadClient sdk, McpOfficialClient mcp, CommandRequest request,
        CommitReceipt expected, CancellationToken token)
    {
        await SampleChunkRf3Assertions.OriginalReceiptAsync(sdk, mcp, request, expected, token);
        var call = SqlRf3Protocol.Call(request.Partition, McpCallerTools.DocumentsCommit, request);
        await SampleChunkRetentionRf3ReadOracle.EqualAsync(await SqlRf3Protocol.SdkAsync<CommitReceipt>(sdk, call, token)
            .ConfigureAwait(false), expected);
        await SampleChunkRetentionRf3ReadOracle.EqualAsync(await SqlRf3Protocol.McpAsync<CommitReceipt>(mcp, call, token)
            .ConfigureAwait(false), expected);
    }
}
