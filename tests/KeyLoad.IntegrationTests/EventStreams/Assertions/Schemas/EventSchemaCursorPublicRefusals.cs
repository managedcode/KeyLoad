using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.EventStreams;

internal static class EventSchemaCursorPublicRefusals
{
    private const string DetailKey = "detail";
    internal static async Task OldAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers reader,
        EventSchemaCursorPublicState state, CancellationToken token)
    {
        foreach (var captured in state.Captures.Where(item => item.Schema != state.Definition.SchemaVersion))
        {
            var request = EventSchemaCursorPublicExpected.Resume(state, captured.Direction, captured.Page);
            var result = await reader.Sdk.ReadStreamAsync(request, token);
            await Assert.That(result.IsFailed).IsTrue();
            await Assert.That(result.Value).IsNull();
            await SqlClusterProtocol.EqualAsync(Errors.Problem(ErrorCode.CursorExpired,
                EventSchemaCursorPublicExpected.ExpiredDetail), result.Problem);
            await EventSchemaCursorPublicCalls.HealthyAsync(admin, reader, state, token);
            var reply = await reader.Mcp.CallAsync(McpCallerTools.StreamsRead, request, token);
            _ = (await McpCallerAssertions.ErrorAsync(reply, ErrorCode.CursorExpired, dispatched: true)).Value;
            await Assert.That(reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
                .GetProperty(DetailKey).GetString()).IsEqualTo(EventSchemaCursorPublicExpected.SafeMcpDetail);
            await McpCallerAssertions.DoesNotDiscloseAsync(reply, state.Reader.Secret, McpEventStreamTokens.PrivateMarker);
            await EventSchemaCursorPublicCalls.HealthyAsync(admin, reader, state, token);
        }
    }
}
