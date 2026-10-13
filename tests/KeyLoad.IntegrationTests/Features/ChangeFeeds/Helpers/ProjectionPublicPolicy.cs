using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

internal static class ProjectionPublicPolicy
{
    internal static async Task<ProjectionPublicSettlement> RunAsync(RequestCqrsRf3Callers root,
        RequestCqrsRf3Callers caller, ProjectionPublicState state, CancellationToken token)
    {
        var before = await ProjectionPublicOperations.ImageAsync(root, state, 2L, false, false, token);
        var denied = state.Identity.Principal with { ClusterAdministrator = false, PolicyEpoch = 2L };
        await ProjectionPublicOperations.PolicyAsync(root, denied, token);
        await ProjectionPublicAssertions.ErrorAsync(caller, state.Complete, ErrorCode.PermissionDenied, token);
        var deniedRead = await caller.Sdk.ReadProjectionAsync(new(state.Consumer), token);
        await global::TUnit.Assertions.Assert.That(deniedRead.IsSuccess).IsFalse();
        await global::TUnit.Assertions.Assert.That(deniedRead.Problem?.ErrorCode).IsEqualTo(ErrorCode.PermissionDenied.ToString());
        await McpCallerAssertions.ErrorAsync(await caller.Mcp.CallAsync(McpCallerTools.ProjectionsRead,
            new ReadProjectionBatchRequest(state.Consumer), token), ErrorCode.PermissionDenied, dispatched: true);
        await ProjectionPublicAssertions.ExactAsync(before,
            await ProjectionPublicOperations.ImageAsync(root, state, 2L, false, false, token));
        var current = denied with { ClusterAdministrator = true, PolicyEpoch = 3L };
        await ProjectionPublicOperations.PolicyAsync(root, current, token);
        await ProjectionPublicAssertions.ErrorAsync(caller, state.Complete, ErrorCode.PermissionDenied, token);
        await ProjectionPublicAssertions.ExactAsync(before,
            await ProjectionPublicOperations.ImageAsync(root, state, 2L, false, false, token));
        var fresh = state.Complete with { CommandId = Guid.NewGuid() };
        var duplicate = await ProjectionPublicOperations.CommitBothAsync(caller, fresh, token);
        await ProjectionPublicAssertions.ExactAsync(new ProjectionBatchResult(state.Completed.Receipt, true, 1L), duplicate);
        await ProjectionPublicAssertions.ExactAsync(before,
            await ProjectionPublicOperations.ImageAsync(root, state, 2L, false, false, token));
        var release = new ReleaseProjectionConsumerRequest(Guid.NewGuid(), state.Consumer, 1L);
        var released = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.ReleaseProjectionAsync(release, token));
        await ProjectionPublicAssertions.ExactAsync(new ProjectionConsumerInfo(state.Consumer, state.Definition, 2L, true), released);
        await ProjectionPublicAssertions.ExactAsync(released, (await McpCallerAssertions.SuccessAsync<ProjectionConsumerInfo>(
            await caller.Mcp.CallAsync(McpCallerTools.ProjectionsRelease, release, token))).Value);
        await ProjectionPublicAssertions.ErrorAsync(caller, fresh, ErrorCode.TokenInvalidated, token);
        var purge = new PurgeOutboxRequest(Guid.NewGuid(), state.Partition, 2L);
        var purged = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.PurgeOutboxAsync(purge, token));
        await ProjectionPublicAssertions.ExactAsync(new OutboxHead(2L, 3L, 0L, 0L), purged);
        await ProjectionPublicAssertions.ExactAsync(purged, (await McpCallerAssertions.SuccessAsync<OutboxHead>(
            await caller.Mcp.CallAsync(McpCallerTools.OutboxPurge, purge, token))).Value);
        await ProjectionPublicOperations.ImageAsync(root, state, 2L, true, true, token);
        return new(state, current, fresh, duplicate, release, released, purge, purged);
    }
}
