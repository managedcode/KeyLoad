using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class EventByteBoundPublicCalls
{
    internal static async Task<(StreamPage Sdk, StreamPage Mcp)> ReadAsync(RequestCqrsRf3Callers admin,
        RequestCqrsRf3Callers reader, EventByteBoundPublicState state, ReadStreamRequest request,
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

    internal static async Task ReplayAsync(RequestCqrsRf3Callers admin, EventByteBoundPublicState state, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(state.Receipt,
            await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.CommitAsync(state.Command, token)));
        await EventByteBoundPublicCurrent.ObserveAsync(admin, state, token);
        await SqlRf3Protocol.EqualAsync(state.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await admin.Mcp.CallAsync(McpCallerTools.DocumentsCommit, state.Command, token))).Value);
        await EventByteBoundPublicCurrent.ObserveAsync(admin, state, token);
    }
}
