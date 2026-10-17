using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

internal static class LiveSubscriptionRf3Cold
{
    internal static async Task<(RequestCqrsRf3Callers Administrator, RequestCqrsRf3Callers Reader)> RestartAsync(
        FeedLiveRf3Connections owner, RequestCqrsRf3Callers administrator, RequestCqrsRf3Callers reader,
        FeedLiveRf3Scenario scenario, CancellationToken token)
    {
        var before = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.StatusAsync(token));
        await owner.CloseAsync(reader);
        await owner.CloseAsync(administrator);
        await FeedLiveRf3Cold.RestartAsync(owner.Fixture, token);
        var recoveredAdministrator = await owner.ConnectAsync(owner.Fixture.AdminKey, token);
        var recoveredReader = await owner.ConnectAsync(scenario.Identity.Secret, token);
        var after = await McpCallerAssertions.SdkSuccessAsync(await recoveredAdministrator.Sdk.StatusAsync(token));
        await Assert.That(after.NodeId).IsEqualTo(before.NodeId);
        await Assert.That(after.Incarnation).IsEqualTo(before.Incarnation);
        await Assert.That(after.ReadGeneration).IsGreaterThanOrEqualTo(before.ReadGeneration);
        return (recoveredAdministrator, recoveredReader);
    }
}
