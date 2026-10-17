using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class EventCurrentReadAuthorityRf3Refusals
{
    internal static async Task RequireAsync(RequestCqrsRf3Callers administrator,
        RequestCqrsRf3Callers reader, EventCurrentReadAuthorityRf3State state,
        ReadStreamRequest request, ErrorCode verifiedCode, CancellationToken token)
    {
        var sdk = await reader.Sdk.ReadStreamAsync(request, token);
        await Assert.That(sdk.IsSuccess).IsFalse();
        await Assert.That(sdk.Value).IsNull();
        await Assert.That(sdk.Problem).IsNotNull();
        await Assert.That(sdk.Problem!.ErrorCode).IsEqualTo(verifiedCode.ToString());
        var detail = verifiedCode switch
        {
            ErrorCode.PermissionDenied => EventCurrentReadAuthorityRf3Expected.ScopeDeniedDetail,
            ErrorCode.CursorExpired => EventCurrentReadAuthorityRf3Expected.CursorExpiredDetail,
            _ => throw new InvalidOperationException("An unsupported event-read refusal oracle was selected.")
        };
        await SqlRf3Protocol.EqualAsync(Errors.Problem(verifiedCode, detail), sdk.Problem);
        await McpEventStreamAssertions.DoesNotDiscloseProblemAsync(sdk.Problem,
            state.Identity.Secret, McpEventStreamTokens.PrivateMarker);
        await EventCurrentReadAuthorityRf3Current.ObserveAsync(administrator, state, token);
        var reply = await reader.Mcp.CallAsync(McpCallerTools.StreamsRead, request, token);
        await McpCallerAssertions.ErrorAsync(reply, verifiedCode, dispatched: true);
        await McpCallerAssertions.DoesNotDiscloseAsync(reply,
            state.Identity.Secret, McpEventStreamTokens.PrivateMarker);
        await EventCurrentReadAuthorityRf3Current.ObserveAsync(administrator, state, token);
    }
}
