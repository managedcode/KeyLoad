using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextMaintenanceRf3Flow
{
    internal static async Task RunAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        NativeTextMaintenancePath path, List<Exception> failures, CancellationToken token)
    {
        var scenario = new NativeTextMaintenanceRf3Scenario(NativeTextMaintenanceRf3Scenario.CreatePartition());
        await scenario.SeedAsync(sdk, token);
        var request = await scenario.RequestAsync(sdk, mcp, token);
        if (!await NativeTextMaintenanceRf3Denied.OwnerAsync(sdk, mcp, scenario, request, failures, token))
        { return; }
        var built = await NativeTextMaintenanceRf3Call.ExecuteAsync(sdk, mcp, request, path, token);
        await NativeTextMaintenanceRf3Assertions.ResultAsync(built, request);
        await Assert.That(built.IndexedThroughSequence).IsEqualTo(built.Source!.ThroughSequence);
        await NativeTextMaintenanceRf3Assertions.CanonicalAsync(sdk, mcp, scenario, changed: false, token);
        await NativeTextSelectedRf3Assertions.HealthyAsync(sdk, mcp, scenario, request, path, changed: false, token);
        var original = await NativeTextMaintenanceRf3Mutation.CommitAsync(sdk, scenario, token);
        await NativeTextSelectedRf3Assertions.StaleAsync(sdk, mcp, scenario, request, path, token);
        await NativeTextMaintenanceRf3OriginalPrefix.VerifyAsync(sdk, mcp, request, built, path, token);
        var restore = request with { CommandId = Guid.NewGuid(), Mode = TextIndexMaintenanceMode.Restore };
        var updated = await NativeTextMaintenanceRf3Call.ExecuteAsync(sdk, mcp, restore, path, token);
        await NativeTextMaintenanceRf3Assertions.ResultAsync(updated, restore);
        await Assert.That(updated.IndexedThroughSequence).IsEqualTo(updated.Source!.ThroughSequence);
        await Assert.That(updated.Source!.ThroughSequence).IsGreaterThan(built.Source!.ThroughSequence);
        await Assert.That(updated.IndexSha256).IsNotEqualTo(built.IndexSha256);
        await NativeTextMaintenanceRf3Assertions.CanonicalAsync(sdk, mcp, scenario, changed: true, token);
        await NativeTextSelectedRf3Assertions.HealthyAsync(sdk, mcp, scenario, restore, path, changed: true, token);
        await NativeTextMaintenanceRf3Mutation.ReplayAsync(sdk, mcp, original, token);
        var repeated = await NativeTextMaintenanceRf3Call.ExecuteAsync(sdk, mcp, restore, path, token);
        await NativeTextMaintenanceRf3Assertions.ResultAsync(repeated, restore);
        await Assert.That(repeated.IndexSha256).IsEqualTo(updated.IndexSha256);
        await Assert.That(repeated.Source!.ThroughSequence).IsEqualTo(updated.Source.ThroughSequence);
        await NativeTextMaintenanceRf3Assertions.CanonicalAsync(sdk, mcp, scenario, changed: true, token);
        await NativeTextMaintenanceRf3Release.RunAsync(sdk, mcp, scenario, restore, path, token);
    }
}
