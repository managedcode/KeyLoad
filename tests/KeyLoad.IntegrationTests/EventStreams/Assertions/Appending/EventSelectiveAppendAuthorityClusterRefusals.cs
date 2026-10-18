using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class EventSelectiveAppendAuthorityClusterRefusals
{
    internal static async Task RequireAsync(RequestCqrsClusterCallers administrator,
        RequestCqrsClusterCallers reader, EventSelectiveAppendAuthorityClusterState state,
        CommandRequest command, bool changedPolicy, CancellationToken token)
    {
        var detail = changedPolicy ? EventSelectiveAppendAuthorityClusterExpected.ChangedPolicyDetail
            : EventSelectiveAppendAuthorityClusterExpected.ScopeDeniedDetail;
        var sdk = await reader.Sdk.CommitAsync(command, token);
        await Assert.That(sdk.IsSuccess).IsFalse();
        await Assert.That(sdk.Value).IsNull();
        await Assert.That(sdk.Problem).IsNotNull();
        await SqlClusterProtocol.EqualAsync(Errors.Problem(ErrorCode.PermissionDenied, detail), sdk.Problem);
        await McpEventStreamAssertions.DoesNotDiscloseProblemAsync(sdk.Problem!,
            state.Identity.Secret, McpEventStreamTokens.PrivateMarker);
        await EventSelectiveAppendAuthorityClusterCurrent.ObserveAsync(administrator, state, token);
        var reply = await reader.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token);
        await McpCallerAssertions.ErrorAsync(reply, ErrorCode.PermissionDenied, dispatched: true);
        await McpCallerAssertions.DoesNotDiscloseAsync(reply,
            state.Identity.Secret, McpEventStreamTokens.PrivateMarker);
        await EventSelectiveAppendAuthorityClusterCurrent.ObserveAsync(administrator, state, token);
    }
}
