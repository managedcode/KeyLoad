using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextReleasedRepairPublicTrial
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var state = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            caller => SetupAsync(caller, token), token);
        for (var cycle = 0; cycle < 2; cycle++)
        {
            await NativeTextProtectedPublicColdRestart.RunAsync(fixture, token);
            _ = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
                async caller =>
                {
                    var baseline = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.OutboxStatusAsync(state.Original.Scenario.Partition, token));
                    await NativeTextReleasedRepairPublicReplay.RunAsync(caller, state, baseline, token);
                    return true;
                }, token);
        }
    }

    private static async Task<NativeTextReleasedRepairPublicState> SetupAsync(RequestCqrsRf3Callers caller, CancellationToken token)
    {
        var original = await NativeTextReleasedRepairPublicSeed.CreateAsync(caller, token);
        var before = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.OutboxStatusAsync(original.Scenario.Partition, token));
        var consumer = before.Consumers.Single(x => x.Consumer == original.Build.Consumer);
        var release = original.Build with { CommandId = Guid.NewGuid(), Mode = TextIndexMaintenanceMode.Release };
        var released = await NativeTextMaintenanceRf3Call.ExecuteAsync(caller.Sdk, caller.Mcp, release, NativeTextMaintenancePath.Mcp, token);
        var expected = new TextIndexMaintenanceResult(release.CommandId, release.Consumer, release.IndexGeneration,
            TextIndexMaintenancePhase.Completed, null, 0, null, null, consumer with { Released = true });
        await SqlRf3Protocol.EqualAsync(expected, released);
        var postRelease = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.OutboxStatusAsync(original.Scenario.Partition, token));
        await NativeTextReleasedRepairPublicAssertions.RefuseAsync(caller, original, null, postRelease, token);
        var fresh = original.Build with { CommandId = Guid.NewGuid(), Consumer = new(original.Scenario.Partition, "released-repair"), IndexGeneration = 2L };
        var beforeBuild = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.StatusAsync(token));
        var built = await NativeTextMaintenanceRf3Call.ExecuteAsync(caller.Sdk, caller.Mcp, fresh, NativeTextMaintenancePath.Sdk, token);
        var afterBuild = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.StatusAsync(token));
        await NativeTextReleasedRepairPublicAssertions.FirstAsync(built, fresh, beforeBuild, afterBuild);
        var current = original with { Build = fresh, Built = built };
        var state = new NativeTextReleasedRepairPublicState(original, release, released, current);
        var baseline = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.OutboxStatusAsync(original.Scenario.Partition, token));
        await NativeTextReleasedRepairPublicReplay.RunAsync(caller, state, baseline, token);
        return state;
    }
}
