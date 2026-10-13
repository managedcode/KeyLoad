using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class EventByteBoundPublicRefusals
{
    internal static async Task RefuseAsync(RequestCqrsRf3Callers admin, RequestCqrsRf3Callers reader,
        EventByteBoundPublicState state, ReadStreamRequest request, CancellationToken token)
    {
        var result = await reader.Sdk.ReadStreamAsync(request, token);
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Value).IsNull();
        await SqlRf3Protocol.EqualAsync(Errors.Problem(ErrorCode.BudgetExceeded,
            EventByteBoundPublicExpected.BytesDetail), result.Problem);
        await EventByteBoundPublicCurrent.ObserveAsync(admin, state, token);
        var reply = await reader.Mcp.CallAsync(McpCallerTools.StreamsRead, request, token);
        await McpCallerAssertions.ErrorAsync(reply, ErrorCode.BudgetExceeded, dispatched: true);
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, state.Reader.Secret, McpEventStreamTokens.PrivateMarker);
        await EventByteBoundPublicCurrent.ObserveAsync(admin, state, token);
    }
}
