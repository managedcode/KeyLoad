using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class EventSelectiveAppendAuthorityRf3Refusals
{
    internal static async Task RequireAsync(RequestCqrsRf3Callers administrator,
        RequestCqrsRf3Callers reader, EventSelectiveAppendAuthorityRf3State state,
        CommandRequest command, bool changedPolicy, CancellationToken token)
    {
        var detail = changedPolicy ? EventSelectiveAppendAuthorityRf3Expected.ChangedPolicyDetail
            : EventSelectiveAppendAuthorityRf3Expected.ScopeDeniedDetail;
        var sdk = await reader.Sdk.CommitAsync(command, token);
        await Assert.That(sdk.IsSuccess).IsFalse();
        await Assert.That(sdk.Value).IsNull();
        await Assert.That(sdk.Problem).IsNotNull();
        await SqlRf3Protocol.EqualAsync(Errors.Problem(ErrorCode.PermissionDenied, detail), sdk.Problem);
        await McpEventStreamAssertions.DoesNotDiscloseProblemAsync(sdk.Problem!,
            state.Identity.Secret, McpEventStreamTokens.PrivateMarker);
        await EventSelectiveAppendAuthorityRf3Current.ObserveAsync(administrator, state, token);
        var reply = await reader.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token);
        await McpCallerAssertions.ErrorAsync(reply, ErrorCode.PermissionDenied, dispatched: true);
        await McpCallerAssertions.DoesNotDiscloseAsync(reply,
            state.Identity.Secret, McpEventStreamTokens.PrivateMarker);
        await EventSelectiveAppendAuthorityRf3Current.ObserveAsync(administrator, state, token);
    }
}
