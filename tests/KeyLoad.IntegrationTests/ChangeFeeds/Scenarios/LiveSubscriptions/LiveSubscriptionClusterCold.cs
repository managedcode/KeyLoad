using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

internal static class LiveSubscriptionClusterCold
{
    internal static async Task<(RequestCqrsClusterCallers Administrator, RequestCqrsClusterCallers Reader)> RestartAsync(
        FeedLiveClusterConnections owner, RequestCqrsClusterCallers administrator, RequestCqrsClusterCallers reader,
        FeedLiveClusterScenario scenario, CancellationToken token)
    {
        var before = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.StatusAsync(token));
        await owner.CloseAsync(reader);
        await owner.CloseAsync(administrator);
        await FeedLiveClusterCold.RestartAsync(owner.Fixture, token);
        var recoveredAdministrator = await owner.ConnectAsync(owner.Fixture.AdminKey, token);
        var recoveredReader = await owner.ConnectAsync(scenario.Identity.Secret, token);
        var after = await McpCallerAssertions.SdkSuccessAsync(await recoveredAdministrator.Sdk.StatusAsync(token));
        await Assert.That(after.NodeId).IsEqualTo(before.NodeId);
        await Assert.That(after.Incarnation).IsEqualTo(before.Incarnation);
        await Assert.That(after.ReadGeneration).IsGreaterThanOrEqualTo(before.ReadGeneration);
        return (recoveredAdministrator, recoveredReader);
    }
}
