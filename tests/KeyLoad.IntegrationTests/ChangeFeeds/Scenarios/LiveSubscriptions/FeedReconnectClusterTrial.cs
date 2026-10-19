using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

internal static class FeedReconnectClusterTrial
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
        => await FeedLiveClusterConnections.RunAsync(fixture, owner => RunOwnedAsync(owner, token));

    private static async Task RunOwnedAsync(FeedLiveClusterConnections owner, CancellationToken token)
    {
        var fixture = owner.Fixture;
        var administrator = await owner.ConnectAsync(fixture.AdminKey, token);
        var scenario = await FeedLiveClusterScenario.CreateAsync(fixture, administrator, token);
        string cursor;
        var reader = await owner.ConnectAsync(scenario.Identity.Secret, token);
        await FeedLiveClusterNoEffects.RequireAsync(administrator, scenario,
            FeedLiveClusterNoEffects.Rows(FeedLiveClusterProtocol.FirstRevision, FeedLiveClusterProtocol.FirstJson), FeedLiveClusterProtocol.HiddenSequence,
            () => FeedLiveClusterAssertions.DeniedAsync(reader,
                scenario.Feed with
                {
                    MaxBytes = FeedLiveClusterProtocol.RejectedFirstEntryBytes
                },
                ErrorCode.BudgetExceeded, token), token);
        {
            var initial = await FeedLiveClusterAssertions.FeedAsync(reader, scenario, scenario.Feed,
                scenario.Receipt.Token, FeedLiveClusterProtocol.FirstSequence, FeedLiveClusterProtocol.FirstRevision,
                FeedLiveClusterProtocol.FirstProjected, false, FeedLiveClusterProtocol.HiddenSequence, token);
            await Assert.That(initial.HasMore).IsTrue();
            cursor = initial.Cursor;
        }
        await owner.CloseAsync(reader);
        var updated = await scenario.UpdateAsync(administrator, token);
        var originalOwner = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.StatusAsync(token));
        await FeedLiveClusterCold.RestartAsync(fixture, token);
        var recoveredAdmin = await owner.ConnectAsync(fixture.AdminKey, token);
        var reconnected = await owner.ConnectAsync(scenario.Identity.Secret, token);
        var hidden = await FeedGapClusterAssertions.AdvanceAsync(reconnected, scenario, cursor, token);
        var request = scenario.Feed with
        {
            Cursor = hidden.Cursor
        };
        var page = await FeedLiveClusterAssertions.FeedAsync(reconnected, scenario, request, updated.Token,
            FeedLiveClusterProtocol.UpdatedSequence, FeedLiveClusterProtocol.UpdatedRevision,
            FeedLiveClusterProtocol.UpdatedProjected, true, FeedLiveClusterProtocol.UpdatedSequence, token);
        await Assert.That(page.HasMore).IsFalse();
        await FeedLiveClusterAssertions.FeedAsync(reconnected, scenario, request, updated.Token,
            FeedLiveClusterProtocol.UpdatedSequence, FeedLiveClusterProtocol.UpdatedRevision,
            FeedLiveClusterProtocol.UpdatedProjected, true, FeedLiveClusterProtocol.UpdatedSequence, token);
        var recoveredOwner = await McpCallerAssertions.SdkSuccessAsync(await recoveredAdmin.Sdk.StatusAsync(token));
        await Assert.That(recoveredOwner.NodeId).IsEqualTo(originalOwner.NodeId);
        await Assert.That(recoveredOwner.Incarnation).IsEqualTo(originalOwner.Incarnation);
        await Assert.That(recoveredOwner.ReadGeneration).IsGreaterThanOrEqualTo(originalOwner.ReadGeneration);
        await RevokeAndRestoreAsync(recoveredAdmin, reconnected, scenario, request, token);
        var head = await McpCallerAssertions.SdkSuccessAsync(await recoveredAdmin.Sdk.OutboxStatusAsync(scenario.Partition, token));
        await SqlClusterProtocol.EqualAsync(scenario.Receipt, await McpCallerAssertions.SdkSuccessAsync(
            await recoveredAdmin.Sdk.CommitAsync(scenario.Original, token)));
        await SqlClusterProtocol.EqualAsync(head, await McpCallerAssertions.SdkSuccessAsync(
            await recoveredAdmin.Sdk.OutboxStatusAsync(scenario.Partition, token)));
    }

    private static async Task RevokeAndRestoreAsync(RequestCqrsClusterCallers administrator,
        RequestCqrsClusterCallers reader, FeedLiveClusterScenario scenario, ReadChangeFeedRequest original,
        CancellationToken token)
    {
        var revoked = scenario.Identity.Principal with
        {
            Grants = [],
            PolicyEpoch = scenario.Identity.Principal.PolicyEpoch + FeedLiveClusterProtocol.EpochStep
        };
        await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), revoked, token));
        await FeedLiveClusterNoEffects.RequireAsync(administrator, scenario,
            FeedLiveClusterNoEffects.Rows(FeedLiveClusterProtocol.UpdatedRevision, FeedLiveClusterProtocol.UpdatedJson), FeedLiveClusterProtocol.UpdatedSequence,
            () => FeedLiveClusterAssertions.DeniedAsync(reader, original, ErrorCode.PermissionDenied, token), token);
        var restored = scenario.Identity.Principal with
        {
            PolicyEpoch = revoked.PolicyEpoch + FeedLiveClusterProtocol.EpochStep
        };
        await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), restored, token));
        await FeedLiveClusterNoEffects.RequireAsync(administrator, scenario,
            FeedLiveClusterNoEffects.Rows(FeedLiveClusterProtocol.UpdatedRevision, FeedLiveClusterProtocol.UpdatedJson), FeedLiveClusterProtocol.UpdatedSequence,
            () => FeedLiveClusterAssertions.DeniedAsync(reader, original, ErrorCode.TokenInvalidated, token), token);
        await FeedLiveClusterAssertions.FeedAsync(reader, scenario, scenario.Feed, scenario.Receipt.Token,
            FeedLiveClusterProtocol.FirstSequence, FeedLiveClusterProtocol.FirstRevision,
            FeedLiveClusterProtocol.FirstProjected, false, FeedLiveClusterProtocol.UpdatedSequence, token);
    }
}
