using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

internal static class FeedReconnectRf3Trial
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
        => await FeedLiveRf3Connections.RunAsync(fixture, owner => RunOwnedAsync(owner, token));

    private static async Task RunOwnedAsync(FeedLiveRf3Connections owner, CancellationToken token)
    {
        var fixture = owner.Fixture;
        var administrator = await owner.ConnectAsync(fixture.AdminKey, token);
        var scenario = await FeedLiveRf3Scenario.CreateAsync(fixture, administrator, token);
        string cursor;
        var reader = await owner.ConnectAsync(scenario.Identity.Secret, token);
        await FeedLiveRf3NoEffects.RequireAsync(administrator, scenario,
            () => FeedLiveRf3Assertions.DeniedAsync(reader,
                scenario.Feed with { MaxBytes = FeedLiveRf3Protocol.RejectedFirstEntryBytes },
                ErrorCode.BudgetExceeded, token), token);
        {
            var initial = await FeedLiveRf3Assertions.FeedAsync(reader, scenario, scenario.Feed,
                scenario.Receipt.Token, FeedLiveRf3Protocol.FirstSequence, FeedLiveRf3Protocol.FirstRevision,
                FeedLiveRf3Protocol.FirstProjected, false, FeedLiveRf3Protocol.HiddenSequence, token);
            await Assert.That(initial.HasMore).IsTrue();
            cursor = initial.Cursor;
        }
        await owner.CloseAsync(reader);
        var updated = await scenario.UpdateAsync(administrator, token);
        var originalOwner = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.StatusAsync(token));
        await FeedLiveRf3Cold.RestartAsync(fixture, token);
        var recoveredAdmin = await owner.ConnectAsync(fixture.AdminKey, token);
        var reconnected = await owner.ConnectAsync(scenario.Identity.Secret, token);
        var hidden = await FeedGapRf3Assertions.AdvanceAsync(reconnected, scenario, cursor, token);
        var request = scenario.Feed with { Cursor = hidden.Cursor };
        var page = await FeedLiveRf3Assertions.FeedAsync(reconnected, scenario, request, updated.Token,
            FeedLiveRf3Protocol.UpdatedSequence, FeedLiveRf3Protocol.UpdatedRevision,
            FeedLiveRf3Protocol.UpdatedProjected, true, FeedLiveRf3Protocol.UpdatedSequence, token);
        await Assert.That(page.HasMore).IsFalse();
        await FeedLiveRf3Assertions.FeedAsync(reconnected, scenario, request, updated.Token,
            FeedLiveRf3Protocol.UpdatedSequence, FeedLiveRf3Protocol.UpdatedRevision,
            FeedLiveRf3Protocol.UpdatedProjected, true, FeedLiveRf3Protocol.UpdatedSequence, token);
        var recoveredOwner = await McpCallerAssertions.SdkSuccessAsync(await recoveredAdmin.Sdk.StatusAsync(token));
        await Assert.That(recoveredOwner.NodeId).IsEqualTo(originalOwner.NodeId);
        await Assert.That(recoveredOwner.Incarnation).IsEqualTo(originalOwner.Incarnation);
        await Assert.That(recoveredOwner.ReadGeneration).IsGreaterThanOrEqualTo(originalOwner.ReadGeneration);
        await RevokeAndRestoreAsync(recoveredAdmin, reconnected, scenario, request, token);
        var head = await McpCallerAssertions.SdkSuccessAsync(await recoveredAdmin.Sdk.OutboxStatusAsync(scenario.Partition, token));
        await SqlRf3Protocol.EqualAsync(scenario.Receipt, await McpCallerAssertions.SdkSuccessAsync(
            await recoveredAdmin.Sdk.CommitAsync(scenario.Original, token)));
        await SqlRf3Protocol.EqualAsync(head, await McpCallerAssertions.SdkSuccessAsync(
            await recoveredAdmin.Sdk.OutboxStatusAsync(scenario.Partition, token)));
    }

    private static async Task RevokeAndRestoreAsync(RequestCqrsRf3Callers administrator,
        RequestCqrsRf3Callers reader, FeedLiveRf3Scenario scenario, ReadChangeFeedRequest original,
        CancellationToken token)
    {
        var revoked = scenario.Identity.Principal with
        {
            Grants = [],
            PolicyEpoch = scenario.Identity.Principal.PolicyEpoch + FeedLiveRf3Protocol.EpochStep
        };
        await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), revoked, token));
        await FeedLiveRf3NoEffects.RequireAsync(administrator, scenario,
            () => FeedLiveRf3Assertions.DeniedAsync(reader, original, ErrorCode.PermissionDenied, token), token);
        var restored = scenario.Identity.Principal with { PolicyEpoch = revoked.PolicyEpoch + FeedLiveRf3Protocol.EpochStep };
        await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), restored, token));
        await FeedLiveRf3NoEffects.RequireAsync(administrator, scenario,
            () => FeedLiveRf3Assertions.DeniedAsync(reader, original, ErrorCode.TokenInvalidated, token), token);
        await FeedLiveRf3Assertions.FeedAsync(reader, scenario, scenario.Feed, scenario.Receipt.Token,
            FeedLiveRf3Protocol.FirstSequence, FeedLiveRf3Protocol.FirstRevision,
            FeedLiveRf3Protocol.FirstProjected, false, FeedLiveRf3Protocol.UpdatedSequence, token);
    }
}
