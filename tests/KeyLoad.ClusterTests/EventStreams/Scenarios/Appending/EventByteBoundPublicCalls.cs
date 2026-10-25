using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.QueryExecution;

namespace KeyLoad.ClusterTests.EventStreams;

internal static class EventByteBoundPublicCalls
{
    internal static async Task<(StreamPage Sdk, StreamPage Mcp)> ReadAsync(RequestCqrsClusterCallers admin,
        RequestCqrsClusterCallers reader, EventByteBoundPublicState state, ReadStreamRequest request,
        int[] indices, bool more, long? snapshot, CancellationToken token)
    {
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.ReadStreamAsync(request, token));
        await EventByteBoundPublicExpected.PageAsync(state, sdk, indices, more, snapshot);
        await EventByteBoundPublicCurrent.ObserveAsync(admin, state, token);
        var reply = await reader.Mcp.CallAsync(McpCallerTools.StreamsRead, request, token);
        var mcp = (await McpCallerAssertions.SuccessAsync<StreamPage>(reply)).Value;
        await EventByteBoundPublicExpected.PageAsync(state, mcp, indices, more, snapshot);
        await Assert.That(System.Text.Json.JsonSerializer.Serialize(reply)
            .Contains(state.Reader.Secret, StringComparison.Ordinal)).IsFalse();
        await EventByteBoundPublicCurrent.ObserveAsync(admin, state, token);
        return (sdk, mcp);
    }

    internal static async Task ReplayAsync(RequestCqrsClusterCallers admin, EventByteBoundPublicState state, CancellationToken token)
    {
        await SqlClusterProtocol.EqualAsync(state.Receipt,
            await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.CommitAsync(state.Command, token)));
        await EventByteBoundPublicCurrent.ObserveAsync(admin, state, token);
        await SqlClusterProtocol.EqualAsync(state.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await admin.Mcp.CallAsync(McpCallerTools.DocumentsCommit, state.Command, token))).Value);
        await EventByteBoundPublicCurrent.ObserveAsync(admin, state, token);
    }
}
