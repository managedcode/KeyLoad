using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

internal static class LiveTailRf3Trial
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
        => await FeedLiveRf3Connections.RunAsync(fixture, owner => RunOwnedAsync(owner, token));

    private static async Task RunOwnedAsync(FeedLiveRf3Connections owner, CancellationToken token)
    {
        var fixture = owner.Fixture;
        var administrator = await owner.ConnectAsync(fixture.AdminKey, token);
        var scenario = await FeedLiveRf3Scenario.CreateAsync(fixture, administrator, token);
        var reader = await owner.ConnectAsync(scenario.Identity.Secret, token);
        var (request, fresh) = await LiveTailRf3InitialPhase.RunAsync(administrator, reader, scenario, token);
        (administrator, reader) = await LiveSubscriptionRf3Cold.RestartAsync(owner, administrator, reader,
            scenario, token);
        var deletion = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(
            new(Guid.NewGuid(), scenario.Partition, [new DeleteDocument(FeedLiveRf3Protocol.Collection,
                FeedLiveRf3Protocol.First, FeedLiveRf3Protocol.UpdatedRevision)]), token));
        var deletedRequest = request with { Cursor = fresh.Cursor };
        var deletedCut = await LiveQueryCutRf3Assertions.CreateAsync(administrator, reader, scenario, deletion,
            [], () => LiveTailRf3Assertions.QueryAsync(reader, scenario, token), token);
        var deletionBefore = await deletedCut();
        var deleted = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.ReadLiveQueryAsync(deletedRequest, token));
        await LiveQueryCutRf3Assertions.BoundsAsync(deletionBefore, deleted.CutPosition, await deletedCut());
        await SqlRf3Protocol.EqualAsync(new[] { new LiveQueryChange(FeedLiveRf3Protocol.DeletedSequence,
            deletion.Token, LiveQueryChangeKind.Remove, scenario.Reference, FeedLiveRf3Protocol.DeletedRevision, null) }, deleted.Changes.ToArray());
        await Assert.That(deleted.ThroughSequence).IsEqualTo(FeedLiveRf3Protocol.DeletedSequence);
        await Assert.That(deleted.HasMore).IsFalse();
        await LiveTailUnrelatedCommitRf3Control.RequireAsync(administrator, reader, scenario, deletion, token);
        await LiveTailRf3Assertions.AllPathsAsync(administrator, reader, scenario, deletedRequest, deleted, deletion, token);
        var repaired = await RevokeAndRestoreAsync(administrator, reader, scenario, deletedRequest, deletion, token);
        await LiveRepairedRf3Continuation.RunAsync(owner, administrator, reader, scenario,
            deletedRequest, repaired, token);
    }

    private static async Task<LiveQuerySnapshot> RevokeAndRestoreAsync(RequestCqrsRf3Callers administrator,
        RequestCqrsRf3Callers reader, FeedLiveRf3Scenario scenario, ReadLiveQueryRequest original, CommitReceipt deletion, CancellationToken token)
    {
        var revoked = scenario.Identity.Principal with
        {
            Grants = [],
            PolicyEpoch = scenario.Identity.Principal.PolicyEpoch + FeedLiveRf3Protocol.EpochStep
        };
        await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), revoked, token));
        await FeedLiveRf3NoEffects.RequireAsync(administrator, scenario,
            FeedLiveRf3NoEffects.Rows(FeedLiveRf3Protocol.DeletedRevision, null), FeedLiveRf3Protocol.DeletedSequence,
            () => LiveTailRf3Assertions.DeniedAsync(reader, original, ErrorCode.PermissionDenied, token), token);
        var restored = scenario.Identity.Principal with { PolicyEpoch = revoked.PolicyEpoch + FeedLiveRf3Protocol.EpochStep };
        await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), restored, token));
        await FeedLiveRf3NoEffects.RequireAsync(administrator, scenario,
            FeedLiveRf3NoEffects.Rows(FeedLiveRf3Protocol.DeletedRevision, null), FeedLiveRf3Protocol.DeletedSequence,
            () => LiveTailRf3Assertions.DeniedAsync(reader, original, ErrorCode.TokenInvalidated, token), token);
        var observeCut = await LiveQueryCutRf3Assertions.CreateAsync(administrator, reader, scenario, deletion,
            [], () => LiveTailRf3Assertions.QueryAsync(reader, scenario, token), token);
        var before = await observeCut();
        var fresh = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.StartLiveQueryAsync(new(scenario.Query), token));
        await LiveQueryCutRf3Assertions.BoundsAsync(before, fresh.CutPosition, await observeCut());
        await Assert.That(fresh.Rows.IsEmpty).IsTrue();
        var readBefore = await observeCut();
        var healthy = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.ReadLiveQueryAsync(
            new(scenario.Query, fresh.Cursor), token));
        await LiveQueryCutRf3Assertions.BoundsAsync(readBefore, healthy.CutPosition, await observeCut());
        await Assert.That(healthy.Changes.IsEmpty).IsTrue();
        await Assert.That(healthy.HasMore).IsFalse();
        await Assert.That(healthy.ThroughSequence).IsEqualTo(FeedLiveRf3Protocol.DeletedSequence);
        await LiveTailRf3Assertions.AllPathsAsync(administrator, reader, scenario, new(scenario.Query, fresh.Cursor), healthy, deletion, token);
        return fresh;
    }
}
