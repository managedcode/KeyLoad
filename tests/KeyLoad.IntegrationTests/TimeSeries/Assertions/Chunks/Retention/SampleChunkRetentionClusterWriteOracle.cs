using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleChunkRetentionClusterWriteOracle
{
    internal static async Task<CommitReceipt> CommitAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        CommandRequest request, MutationReceipt[] mutations, bool official, CancellationToken token)
    {
        var actual = official ? (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await mcp.CallAsync(McpCallerTools.DocumentsCommit, request, token).ConfigureAwait(false))).Value
            : await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(request, token).ConfigureAwait(false));
        var expected = new CommitReceipt(request.CommandId, new(actual.Token.Incarnation, request.Partition.AtomicPartitionId,
            actual.Token.Position),
            [.. mutations], DurabilityProfile.ProcessDurable, 3);
        await SampleChunkRetentionClusterReadOracle.EqualAsync(actual, expected);
        await ReplayAsync(sdk, mcp, request, actual, token);
        return actual;
    }

    internal static async Task ReplayAsync(KeyLoadClient sdk, McpOfficialClient mcp, CommandRequest request,
        CommitReceipt expected, CancellationToken token)
    {
        await SampleChunkClusterAssertions.OriginalReceiptAsync(sdk, mcp, request, expected, token);
        var call = SqlClusterProtocol.Call(request.Partition, McpCallerTools.DocumentsCommit, request);
        await SampleChunkRetentionClusterReadOracle.EqualAsync(await SqlClusterProtocol.SdkAsync<CommitReceipt>(sdk, call, token)
            .ConfigureAwait(false), expected);
        await SampleChunkRetentionClusterReadOracle.EqualAsync(await SqlClusterProtocol.McpAsync<CommitReceipt>(mcp, call, token)
            .ConfigureAwait(false), expected);
    }
}
