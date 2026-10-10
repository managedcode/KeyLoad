using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextMaintenanceRf3Flow
{
    internal static async Task RunAsync(ClusterFixture fixture, KeyLoadClient sdk, McpOfficialClient mcp,
        NativeTextMaintenancePath path, NativeTextMaintenanceFlowObservation observation,
        List<Exception> failures, CancellationToken token)
    {
        var scenario = new NativeTextMaintenanceRf3Scenario(NativeTextMaintenanceRf3Scenario.CreatePartition());
        await observation.MeasureAsync(NativeTextMaintenanceFlowPhase.Seed, () =>
            scenario.SeedAsync(sdk, token));
        var request = await observation.MeasureAsync(NativeTextMaintenanceFlowPhase.Request, () =>
            scenario.RequestAsync(sdk, mcp, token));
        if (!await observation.MeasureAsync(NativeTextMaintenanceFlowPhase.DeniedOwner, () =>
            NativeTextMaintenanceRf3Denied.OwnerAsync(sdk, mcp, scenario, request, failures, token)))
        { return; }
        var built = await observation.MeasureAsync(NativeTextMaintenanceFlowPhase.Build, async () =>
        {
            var value = await NativeTextMaintenanceRf3Call.ExecuteAsync(sdk, mcp, request, path, token);
            await NativeTextMaintenanceRf3Assertions.ResultAsync(value, request);
            await Assert.That(value.IndexedThroughSequence).IsEqualTo(value.Source!.ThroughSequence);
            return value;
        });
        await observation.MeasureAsync(NativeTextMaintenanceFlowPhase.OriginalCanonical, () =>
            NativeTextMaintenanceRf3Assertions.CanonicalAsync(sdk, mcp, scenario, changed: false, token));
        await observation.MeasureAsync(NativeTextMaintenanceFlowPhase.OriginalSelected, () =>
            NativeTextSelectedRf3Assertions.HealthyAsync(sdk, mcp, scenario, request, path, changed: false, token));
        var original = await observation.MeasureAsync(NativeTextMaintenanceFlowPhase.Mutation, () =>
            NativeTextMaintenanceRf3Mutation.CommitAsync(sdk, scenario, token));
        await observation.MeasureAsync(NativeTextMaintenanceFlowPhase.StaleSelected, () =>
            NativeTextSelectedRf3Assertions.StaleAsync(sdk, mcp, scenario, request, path, token));
        await observation.MeasureAsync(NativeTextMaintenanceFlowPhase.OriginalPrefix, () =>
            NativeTextMaintenanceRf3OriginalPrefix.VerifyAsync(sdk, mcp, request, built, path, token));
        var restore = request with { CommandId = Guid.NewGuid(), Mode = TextIndexMaintenanceMode.Restore };
        var updated = await observation.MeasureAsync(NativeTextMaintenanceFlowPhase.Restore, async () =>
        {
            var value = await NativeTextMaintenanceRf3Call.ExecuteAsync(sdk, mcp, restore, path, token);
            await NativeTextMaintenanceRf3Assertions.ResultAsync(value, restore);
            await Assert.That(value.IndexedThroughSequence).IsEqualTo(value.Source!.ThroughSequence);
            await Assert.That(value.Source!.ThroughSequence).IsGreaterThan(built.Source!.ThroughSequence);
            await Assert.That(value.IndexSha256).IsNotEqualTo(built.IndexSha256);
            return value;
        });
        await observation.MeasureAsync(NativeTextMaintenanceFlowPhase.ChangedCanonical, () =>
            NativeTextMaintenanceRf3Assertions.CanonicalAsync(sdk, mcp, scenario, changed: true, token));
        await observation.MeasureAsync(NativeTextMaintenanceFlowPhase.ChangedSelected, () =>
            NativeTextSelectedRf3Assertions.HealthyAsync(sdk, mcp, scenario, restore, path, changed: true, token));
        await observation.MeasureAsync(NativeTextMaintenanceFlowPhase.MutationReplay, () =>
            NativeTextMaintenanceRf3Mutation.ReplayAsync(sdk, mcp, original, token));
        await observation.MeasureAsync(NativeTextMaintenanceFlowPhase.RestoreReplay, async () =>
        {
            var repeated = await NativeTextMaintenanceRf3Call.ExecuteAsync(sdk, mcp, restore, path, token);
            await NativeTextMaintenanceRf3Assertions.ResultAsync(repeated, restore);
            await Assert.That(repeated.IndexSha256).IsEqualTo(updated.IndexSha256);
            await Assert.That(repeated.Source!.ThroughSequence).IsEqualTo(updated.Source!.ThroughSequence);
        });
        await observation.MeasureAsync(NativeTextMaintenanceFlowPhase.FinalCanonical, () =>
            NativeTextMaintenanceRf3Assertions.CanonicalAsync(sdk, mcp, scenario, changed: true, observation, token));
        await observation.MeasureAsync(NativeTextMaintenanceFlowPhase.ColdContinuation, () =>
            NativeTextMaintenanceRf3Cold.RunAsync(fixture, sdk, mcp, scenario, request, restore, updated, path, token));
    }
}
