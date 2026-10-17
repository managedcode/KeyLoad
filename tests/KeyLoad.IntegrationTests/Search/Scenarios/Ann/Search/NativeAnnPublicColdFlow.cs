using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeAnnPublicColdFlow
{
    private const int ColdCuts = 2;

    internal static async Task RunAsync(ClusterFixture fixture)
    {
        using var deadline = McpCallerDeadline.Create();
        var token = deadline.Token;
        var seed = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async caller =>
            {
                var actual = await NativeAnnPublicColdSeed.CreateAsync(fixture, caller.Sdk, caller.Mcp, token);
                await NativeAnnPublicColdAssertions.StateAsync(caller, actual, token);
                var restore = actual.Pin with
                {
                    CommandId = Guid.NewGuid(),
                    Mode = AnnMaintenanceMode.Restore
                };
                await NativeAnnPublicColdAssertions.RefuseWrongOwnerAsync(caller, actual, restore, token);
                await NativeAnnPublicColdAssertions.RestoreAsync(caller, actual, restore, token);
                return await NativeAnnPublicColdReader.RunAsync(fixture, caller, actual, restore, token);
            }, token);
        for (var cut = 0; cut < ColdCuts; cut++)
        {
            await NativeAnnPublicColdOwners.RestartAsync(fixture, token);
            seed = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
                fixture.AdminKey, async caller =>
                {
                    var fresh = await seed.Scenario.RequestAsync(caller.Sdk, caller.Mcp, token);
                    var restore = fresh with
                    {
                        Mode = AnnMaintenanceMode.Restore
                    };
                    await NativeAnnPublicColdAssertions.RefuseWrongOwnerAsync(caller, seed, restore, token);
                    await NativeAnnPublicColdAssertions.RestoreAsync(caller, seed, restore, token);
                    await NativeAnnPublicColdAssertions.StateAsync(caller, seed, token);
                    return await NativeAnnPublicColdReader.RunAsync(fixture, caller, seed, restore, token);
                }, token);
        }
        _ = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async caller =>
            {
                var fresh = await seed.Scenario.RequestAsync(caller.Sdk, caller.Mcp, token);
                await NativeAnnMaintenanceReleaseAssertions.AssertAsync(caller.Sdk, caller.Mcp,
                    seed.Scenario, fresh, token);
                var denied = fresh with
                {
                    CommandId = Guid.NewGuid(),
                    Mode = AnnMaintenanceMode.Restore
                };
                _ = await McpCallerAssertions.ErrorAsync(await caller.Mcp.CallAsync(
                    NativeAnnMaintenanceRf3Scenario.Tool, denied, token), ErrorCode.Conflict, dispatched: true);
                await NativeAnnPublicColdAssertions.StateAsync(caller, seed, token);
                return true;
            }, token);
    }
}
