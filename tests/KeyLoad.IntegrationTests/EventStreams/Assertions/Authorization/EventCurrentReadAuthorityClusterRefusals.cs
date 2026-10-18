using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class EventCurrentReadAuthorityClusterRefusals
{
    internal static async Task RequireAsync(RequestCqrsClusterCallers administrator,
        RequestCqrsClusterCallers reader, EventCurrentReadAuthorityClusterState state,
        ReadStreamRequest request, ErrorCode verifiedCode, CancellationToken token)
    {
        var sdk = await reader.Sdk.ReadStreamAsync(request, token);
        await Assert.That(sdk.IsSuccess).IsFalse();
        await Assert.That(sdk.Value).IsNull();
        await Assert.That(sdk.Problem).IsNotNull();
        await Assert.That(sdk.Problem!.ErrorCode).IsEqualTo(verifiedCode.ToString());
        var detail = verifiedCode switch
        {
            ErrorCode.PermissionDenied => EventCurrentReadAuthorityClusterExpected.ScopeDeniedDetail,
            ErrorCode.CursorExpired => EventCurrentReadAuthorityClusterExpected.CursorExpiredDetail,
            _ => throw new InvalidOperationException("An unsupported event-read refusal oracle was selected.")
        };
        await SqlClusterProtocol.EqualAsync(Errors.Problem(verifiedCode, detail), sdk.Problem);
        await McpEventStreamAssertions.DoesNotDiscloseProblemAsync(sdk.Problem,
            state.Identity.Secret, McpEventStreamTokens.PrivateMarker);
        await EventCurrentReadAuthorityClusterCurrent.ObserveAsync(administrator, state, token);
        var reply = await reader.Mcp.CallAsync(McpCallerTools.StreamsRead, request, token);
        await McpCallerAssertions.ErrorAsync(reply, verifiedCode, dispatched: true);
        await McpCallerAssertions.DoesNotDiscloseAsync(reply,
            state.Identity.Secret, McpEventStreamTokens.PrivateMarker);
        await EventCurrentReadAuthorityClusterCurrent.ObserveAsync(administrator, state, token);
    }
}
