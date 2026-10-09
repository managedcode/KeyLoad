using System.Text.Json;
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
        var start = reader.Sdk.StartLiveQueryAsync(new(scenario.Query), token);
        var write = scenario.UpdateAsync(administrator, token);
        await Task.WhenAll(start, write);
        var snapshot = await McpCallerAssertions.SdkSuccessAsync(await start);
        var receipt = await write;
        var request = new ReadLiveQueryRequest(scenario.Query, snapshot.Cursor);
        var tail = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.ReadLiveQueryAsync(request, token));
        await LiveTailRf3Assertions.SnapshotTailAsync(snapshot, tail, scenario, receipt);
        await LiveTailRf3Assertions.AllPathsAsync(reader, scenario, request, tail, token);
        var ordinary = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.QueryAstAsync(scenario.Query, token));
        await SqlRf3Protocol.EqualAsync(new[] { LiveTailRf3Assertions.UpdatedRow }, ordinary.Rows.ToArray());
        await SqlRf3Protocol.EqualAsync(new FeedLiveRf3Scenario.FeedLiveOrder(
            FeedLiveRf3Protocol.UpdatedNumber, FeedLiveRf3Protocol.MatchingStatus),
            JsonSerializer.Deserialize<FeedLiveRf3Scenario.FeedLiveOrder>(ordinary.Rows.Single().Json, JsonDefaults.Options)!);
        var fresh = await McpCallerAssertions.SuccessAsync<LiveQuerySnapshot>(await reader.Mcp.CallAsync(
            FeedLiveRf3Protocol.LiveStart, new StartLiveQueryRequest(scenario.Query), token));
        await SqlRf3Protocol.EqualAsync(new[] { LiveTailRf3Assertions.UpdatedRow }, fresh.Value.Rows.ToArray());
        (administrator, reader) = await LiveSubscriptionRf3Cold.RestartAsync(owner, administrator, reader,
            scenario, token);
        var deletion = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(
            new(Guid.NewGuid(), scenario.Partition, [new DeleteDocument(FeedLiveRf3Protocol.Collection,
                FeedLiveRf3Protocol.First, FeedLiveRf3Protocol.UpdatedRevision)]), token));
        var deletedRequest = request with { Cursor = fresh.Value.Cursor };
        var deleted = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.ReadLiveQueryAsync(deletedRequest, token));
        await SqlRf3Protocol.EqualAsync(new[] { new LiveQueryChange(FeedLiveRf3Protocol.DeletedSequence,
            deletion.Token, LiveQueryChangeKind.Remove, scenario.Reference, FeedLiveRf3Protocol.DeletedRevision, null) }, deleted.Changes.ToArray());
        await LiveTailRf3Assertions.AllPathsAsync(reader, scenario, deletedRequest, deleted, token);
        await RevokeAndRestoreAsync(administrator, reader, scenario, deletedRequest, token);
    }

    private static async Task RevokeAndRestoreAsync(RequestCqrsRf3Callers administrator,
        RequestCqrsRf3Callers reader, FeedLiveRf3Scenario scenario, ReadLiveQueryRequest original, CancellationToken token)
    {
        var revoked = scenario.Identity.Principal with
        {
            Grants = [],
            PolicyEpoch = scenario.Identity.Principal.PolicyEpoch + FeedLiveRf3Protocol.EpochStep
        };
        await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), revoked, token));
        await FeedLiveRf3NoEffects.RequireAsync(administrator, scenario,
            () => LiveTailRf3Assertions.DeniedAsync(reader, original, ErrorCode.PermissionDenied, token), token);
        var restored = scenario.Identity.Principal with { PolicyEpoch = revoked.PolicyEpoch + FeedLiveRf3Protocol.EpochStep };
        await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), restored, token));
        await FeedLiveRf3NoEffects.RequireAsync(administrator, scenario,
            () => LiveTailRf3Assertions.DeniedAsync(reader, original, ErrorCode.TokenInvalidated, token), token);
        var fresh = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.StartLiveQueryAsync(new(scenario.Query), token));
        await Assert.That(fresh.Rows.IsEmpty).IsTrue();
        var healthy = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.ReadLiveQueryAsync(
            new(scenario.Query, fresh.Cursor), token));
        await Assert.That(healthy.Changes.IsEmpty).IsTrue();
        await Assert.That(healthy.HasMore).IsFalse();
        await Assert.That(healthy.ThroughSequence).IsEqualTo(FeedLiveRf3Protocol.DeletedSequence);
        await LiveTailRf3Assertions.AllPathsAsync(reader, scenario, new(scenario.Query, fresh.Cursor), healthy, token);
    }
}
