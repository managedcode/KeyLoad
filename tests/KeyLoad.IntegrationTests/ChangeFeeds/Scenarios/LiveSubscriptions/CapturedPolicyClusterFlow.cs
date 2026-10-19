using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

internal static class CapturedPolicyClusterFlow
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
        => await FeedLiveClusterConnections.RunAsync(fixture, owner => RunOwnedAsync(owner, token));

    private static async Task RunOwnedAsync(FeedLiveClusterConnections owner, CancellationToken token)
    {
        var administrator = await owner.ConnectAsync(owner.Fixture.AdminKey, token);
        var scenario = await FeedLiveClusterScenario.CreateAsync(owner.Fixture, administrator, token);
        var reader = await owner.ConnectAsync(scenario.Identity.Secret, token);
        var original = await CapturedPolicyClusterPages.CaptureAsync(administrator, reader, scenario, token);
        var revoked = scenario.Identity.Principal with
        {
            Grants = [new(scenario.Partition.DatabaseId, FeedLiveClusterProtocol.Collection,
                Capability.Query | Capability.DocumentsRead)],
            PolicyEpoch = scenario.Identity.Principal.PolicyEpoch + FeedLiveClusterProtocol.EpochStep
        };
        await ConfigureAsync(administrator, revoked, token);
        await RevokedAsync(administrator, reader, scenario, original.Feed, original.Live, token);
        (administrator, reader) = await LiveSubscriptionClusterCold.RestartAsync(owner, administrator, reader, scenario, token);
        await RevokedAsync(administrator, reader, scenario, original.Feed, original.Live, token);
        var restored = scenario.Identity.Principal with
        {
            PolicyEpoch = revoked.PolicyEpoch + FeedLiveClusterProtocol.EpochStep
        };
        await ConfigureAsync(administrator, restored, token);
        await CapturedPolicyClusterRefusals.RequireAsync(administrator, reader, scenario, original.Feed,
            original.Live, ErrorCode.TokenInvalidated, false, token);
        var repaired = await CapturedPolicyClusterPages.CaptureAsync(administrator, reader, scenario, token);
        var update = new CommandRequest(Guid.NewGuid(), scenario.Partition,
            [new PutDocument(FeedLiveClusterProtocol.Collection, FeedLiveClusterProtocol.First,
                FeedLiveClusterProtocol.UpdatedJson, FeedLiveClusterProtocol.FirstRevision,
                ExplicitReplacement: true, Access: new(FeedLiveClusterProtocol.Owner))]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(update, token));
        await Assert.That(receipt.CommandId).IsEqualTo(update.CommandId);
        await CapturedPolicyClusterPages.DeltaAsync(administrator, reader, scenario, repaired.Live, receipt, token);
        var retained = await CapturedPolicyClusterPages.FeedDeltaAsync(administrator, reader, scenario, repaired.Feed, receipt, null, token);
        await CapturedPolicyClusterReplay.RequireAsync(administrator, reader, scenario, scenario.Original, scenario.Receipt, token);
        await CapturedPolicyClusterReplay.RequireAsync(administrator, reader, scenario, update, receipt, token);
        (administrator, reader) = await LiveSubscriptionClusterCold.RestartAsync(owner, administrator, reader, scenario, token);
        await CapturedPolicyClusterRefusals.RequireAsync(administrator, reader, scenario, original.Feed,
            original.Live, ErrorCode.TokenInvalidated, true, token);
        await CapturedPolicyClusterPages.DeltaAsync(administrator, reader, scenario, repaired.Live, receipt, token);
        await CapturedPolicyClusterPages.FeedDeltaAsync(administrator, reader, scenario, repaired.Feed, receipt, retained, token);
        await CapturedPolicyClusterReplay.RequireAsync(administrator, reader, scenario, scenario.Original, scenario.Receipt, token);
        await CapturedPolicyClusterReplay.RequireAsync(administrator, reader, scenario, update, receipt, token);
    }

    private static async Task ConfigureAsync(RequestCqrsClusterCallers administrator,
        PrincipalRecord principal, CancellationToken token)
        => await SqlClusterProtocol.EqualAsync(principal, await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), principal, token)));

    private static async Task RevokedAsync(RequestCqrsClusterCallers administrator,
        RequestCqrsClusterCallers reader, FeedLiveClusterScenario scenario, ReadChangeFeedRequest feed,
        ReadLiveQueryRequest live, CancellationToken token)
    {
        await CapturedPolicyClusterRefusals.RequireAsync(administrator, reader, scenario,
            feed, live, ErrorCode.PermissionDenied, false, token);
        await CapturedPolicyClusterRefusals.StartAsync(administrator, reader, scenario, token);
    }
}
