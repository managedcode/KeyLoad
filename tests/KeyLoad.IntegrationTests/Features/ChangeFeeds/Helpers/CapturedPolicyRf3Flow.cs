using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

internal static class CapturedPolicyRf3Flow
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
        => await FeedLiveRf3Connections.RunAsync(fixture, owner => RunOwnedAsync(owner, token));

    private static async Task RunOwnedAsync(FeedLiveRf3Connections owner, CancellationToken token)
    {
        var administrator = await owner.ConnectAsync(owner.Fixture.AdminKey, token);
        var scenario = await FeedLiveRf3Scenario.CreateAsync(owner.Fixture, administrator, token);
        var reader = await owner.ConnectAsync(scenario.Identity.Secret, token);
        var original = await CapturedPolicyRf3Pages.CaptureAsync(administrator, reader, scenario, token);
        var revoked = scenario.Identity.Principal with
        {
            Grants = [new(scenario.Partition.DatabaseId, FeedLiveRf3Protocol.Collection,
                Capability.Query | Capability.DocumentsRead)],
            PolicyEpoch = scenario.Identity.Principal.PolicyEpoch + FeedLiveRf3Protocol.EpochStep
        };
        await ConfigureAsync(administrator, revoked, token);
        await RevokedAsync(administrator, reader, scenario, original.Feed, original.Live, token);
        (administrator, reader) = await LiveSubscriptionRf3Cold.RestartAsync(owner, administrator, reader, scenario, token);
        await RevokedAsync(administrator, reader, scenario, original.Feed, original.Live, token);
        var restored = scenario.Identity.Principal with
        { PolicyEpoch = revoked.PolicyEpoch + FeedLiveRf3Protocol.EpochStep };
        await ConfigureAsync(administrator, restored, token);
        await CapturedPolicyRf3Refusals.RequireAsync(administrator, reader, scenario, original.Feed,
            original.Live, ErrorCode.TokenInvalidated, false, token);
        var repaired = await CapturedPolicyRf3Pages.CaptureAsync(administrator, reader, scenario, token);
        var update = new CommandRequest(Guid.NewGuid(), scenario.Partition,
            [new PutDocument(FeedLiveRf3Protocol.Collection, FeedLiveRf3Protocol.First,
                FeedLiveRf3Protocol.UpdatedJson, FeedLiveRf3Protocol.FirstRevision,
                ExplicitReplacement: true, Access: new(FeedLiveRf3Protocol.Owner))]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(update, token));
        await Assert.That(receipt.CommandId).IsEqualTo(update.CommandId);
        await CapturedPolicyRf3Pages.DeltaAsync(administrator, reader, scenario, repaired.Live, receipt, token);
        var retained = await CapturedPolicyRf3Pages.FeedDeltaAsync(administrator, reader, scenario, repaired.Feed, receipt, null, token);
        await CapturedPolicyRf3Replay.RequireAsync(administrator, reader, scenario, scenario.Original, scenario.Receipt, token);
        await CapturedPolicyRf3Replay.RequireAsync(administrator, reader, scenario, update, receipt, token);
        (administrator, reader) = await LiveSubscriptionRf3Cold.RestartAsync(owner, administrator, reader, scenario, token);
        await CapturedPolicyRf3Refusals.RequireAsync(administrator, reader, scenario, original.Feed,
            original.Live, ErrorCode.TokenInvalidated, true, token);
        await CapturedPolicyRf3Pages.DeltaAsync(administrator, reader, scenario, repaired.Live, receipt, token);
        await CapturedPolicyRf3Pages.FeedDeltaAsync(administrator, reader, scenario, repaired.Feed, receipt, retained, token);
        await CapturedPolicyRf3Replay.RequireAsync(administrator, reader, scenario, scenario.Original, scenario.Receipt, token);
        await CapturedPolicyRf3Replay.RequireAsync(administrator, reader, scenario, update, receipt, token);
    }

    private static async Task ConfigureAsync(RequestCqrsRf3Callers administrator,
        PrincipalRecord principal, CancellationToken token)
        => await SqlRf3Protocol.EqualAsync(principal, await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), principal, token)));

    private static async Task RevokedAsync(RequestCqrsRf3Callers administrator,
        RequestCqrsRf3Callers reader, FeedLiveRf3Scenario scenario, ReadChangeFeedRequest feed,
        ReadLiveQueryRequest live, CancellationToken token)
    {
        await CapturedPolicyRf3Refusals.RequireAsync(administrator, reader, scenario,
            feed, live, ErrorCode.PermissionDenied, false, token);
        await CapturedPolicyRf3Refusals.StartAsync(administrator, reader, scenario, token);
    }
}
