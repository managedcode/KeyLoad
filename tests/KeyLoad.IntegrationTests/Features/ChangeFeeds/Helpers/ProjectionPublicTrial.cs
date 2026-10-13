using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

internal static class ProjectionPublicTrial
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var settled = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            async root =>
            {
                var setup = await ProjectionPublicSetup.CreateAsync(root, token);
                return await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, setup.Identity.Secret,
                    async caller =>
                    {
                        var state = await ProjectionPublicWarm.RunAsync(root, caller, setup.Partition, setup.Identity, token);
                        return await ProjectionPublicPolicy.RunAsync(root, caller, state, token);
                    }, token);
            }, token);
        for (var cut = 0; cut < ProjectionPublicProtocol.ColdCuts; cut++)
        {
            await FeedLiveRf3Cold.RestartAsync(fixture, token);
            await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node3, fixture.AdminKey,
                async root => await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
                    settled.Original.Identity.Secret, async caller =>
                    { await ProjectionPublicCold.VerifyAsync(root, caller, settled, token); return true; }, token), token);
        }
        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, fixture.AdminKey,
            async root => await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node3,
                settled.Original.Identity.Secret, async caller =>
                { await ProjectionPublicHealthy.RunAsync(root, caller, settled, token); return true; }, token), token);
    }
}
