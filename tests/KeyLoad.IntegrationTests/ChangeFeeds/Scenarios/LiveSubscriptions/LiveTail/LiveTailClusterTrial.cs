using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.ChangeFeeds;

internal static class LiveTailClusterTrial
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
        => await FeedLiveClusterConnections.RunAsync(fixture, owner => RunOwnedAsync(owner, token));

    private static async Task RunOwnedAsync(FeedLiveClusterConnections owner, CancellationToken token)
    {
        var fixture = owner.Fixture;
        var administrator = await owner.ConnectAsync(fixture.AdminKey, token);
        var scenario = await FeedLiveClusterScenario.CreateAsync(fixture, administrator, token);
        var reader = await owner.ConnectAsync(scenario.Identity.Secret, token);
        var (request, fresh) = await LiveTailClusterInitialPhase.RunAsync(administrator, reader, scenario, token);
        (administrator, reader) = await LiveSubscriptionClusterCold.RestartAsync(owner, administrator, reader,
            scenario, token);
        var deletion = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(
            new(Guid.NewGuid(), scenario.Partition, [new DeleteDocument(FeedLiveClusterProtocol.Collection,
                FeedLiveClusterProtocol.First, FeedLiveClusterProtocol.UpdatedRevision)]), token));
        var deletedRequest = request with
        {
            Cursor = fresh.Cursor
        };
        var deletedCut = await LiveQueryCutClusterAssertions.CreateAsync(administrator, reader, scenario, deletion,
            [], () => LiveTailClusterAssertions.QueryAsync(reader, scenario, token), token);
        var deletionBefore = await deletedCut();
        await LiveReadBackpressureR49.RequireAsync(administrator, reader, scenario, deletedRequest, deletion, token);
        var deleted = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.ReadLiveQueryAsync(deletedRequest, token));
        await LiveQueryCutClusterAssertions.BoundsAsync(deletionBefore, deleted.CutPosition, await deletedCut());
        await SqlClusterProtocol.EqualAsync(new[] { new LiveQueryChange(FeedLiveClusterProtocol.DeletedSequence,
            deletion.Token, LiveQueryChangeKind.Remove, scenario.StoredReference, FeedLiveClusterProtocol.DeletedRevision, null) }, deleted.Changes.ToArray());
        await Assert.That(deleted.ThroughSequence).IsEqualTo(FeedLiveClusterProtocol.DeletedSequence);
        await Assert.That(deleted.HasMore).IsFalse();
        await LiveTailUnrelatedCommitClusterControl.RequireAsync(administrator, reader, scenario, deletion, token);
        await LiveTailClusterAssertions.AllPathsAsync(administrator, reader, scenario, deletedRequest, deleted, deletion, token);
        var repaired = await RevokeAndRestoreAsync(administrator, reader, scenario, deletedRequest, deletion, token);
        await LiveRepairedClusterContinuation.RunAsync(owner, administrator, reader, scenario,
            deletedRequest, repaired, token);
    }

    private static async Task<LiveQuerySnapshot> RevokeAndRestoreAsync(RequestCqrsClusterCallers administrator,
        RequestCqrsClusterCallers reader, FeedLiveClusterScenario scenario, ReadLiveQueryRequest original, CommitReceipt deletion, CancellationToken token)
    {
        var revoked = scenario.Identity.Principal with
        {
            Grants = [],
            PolicyEpoch = scenario.Identity.Principal.PolicyEpoch + FeedLiveClusterProtocol.EpochStep
        };
        await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), revoked, token));
        await FeedLiveClusterNoEffects.RequireAsync(administrator, scenario,
            FeedLiveClusterNoEffects.Rows(FeedLiveClusterProtocol.DeletedRevision, null), FeedLiveClusterProtocol.DeletedSequence,
            () => LiveTailClusterAssertions.DeniedAsync(reader, original, ErrorCode.PermissionDenied, token), token);
        var restored = scenario.Identity.Principal with
        {
            PolicyEpoch = revoked.PolicyEpoch + FeedLiveClusterProtocol.EpochStep
        };
        await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), restored, token));
        await FeedLiveClusterNoEffects.RequireAsync(administrator, scenario,
            FeedLiveClusterNoEffects.Rows(FeedLiveClusterProtocol.DeletedRevision, null), FeedLiveClusterProtocol.DeletedSequence,
            () => LiveTailClusterAssertions.DeniedAsync(reader, original, ErrorCode.TokenInvalidated, token), token);
        var observeCut = await LiveQueryCutClusterAssertions.CreateAsync(administrator, reader, scenario, deletion,
            [], () => LiveTailClusterAssertions.QueryAsync(reader, scenario, token), token);
        var before = await observeCut();
        var fresh = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.StartLiveQueryAsync(new(scenario.Query), token));
        await LiveQueryCutClusterAssertions.BoundsAsync(before, fresh.CutPosition, await observeCut());
        await Assert.That(fresh.Rows.IsEmpty).IsTrue();
        var readBefore = await observeCut();
        var healthy = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.ReadLiveQueryAsync(
            new(scenario.Query, fresh.Cursor), token));
        await LiveQueryCutClusterAssertions.BoundsAsync(readBefore, healthy.CutPosition, await observeCut());
        await Assert.That(healthy.Changes.IsEmpty).IsTrue();
        await Assert.That(healthy.HasMore).IsFalse();
        await Assert.That(healthy.ThroughSequence).IsEqualTo(FeedLiveClusterProtocol.DeletedSequence);
        await LiveTailClusterAssertions.AllPathsAsync(administrator, reader, scenario, new(scenario.Query, fresh.Cursor), healthy, deletion, token);
        return fresh;
    }
}
