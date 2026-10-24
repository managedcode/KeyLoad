using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.EventStreams;

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
        await McpCallerAssertions.ErrorAsync(reply, ErrorCode.BudgetExceeded, dispatched: true);
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, state.Reader.Secret, McpEventStreamTokens.PrivateMarker);
        await EventByteBoundPublicCurrent.ObserveAsync(admin, state, token);
    }
}
