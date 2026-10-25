using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.QueryExecution;

namespace KeyLoad.ClusterTests.EventStreams;

internal static class EventByteBoundPublicRefusals
{
    internal static async Task RefuseAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers reader,
        EventByteBoundPublicState state, ReadStreamRequest request, CancellationToken token)
    {
        var result = await reader.Sdk.ReadStreamAsync(request, token);
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Value).IsNull();
        await SqlClusterProtocol.EqualAsync(Errors.Problem(ErrorCode.BudgetExceeded,
            EventByteBoundPublicExpected.BytesDetail), result.Problem);
        await EventByteBoundPublicCurrent.ObserveAsync(admin, state, token);
        var reply = await reader.Mcp.CallAsync(McpCallerTools.StreamsRead, request, token);
        _ = (await McpCallerAssertions.ErrorAsync(reply, ErrorCode.BudgetExceeded, dispatched: true)).Value;
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, state.Reader.Secret, McpEventStreamTokens.PrivateMarker);
        await EventByteBoundPublicCurrent.ObserveAsync(admin, state, token);
    }
}
