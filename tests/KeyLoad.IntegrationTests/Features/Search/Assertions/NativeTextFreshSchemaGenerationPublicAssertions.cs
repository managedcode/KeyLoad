using System.Security.Cryptography;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextFreshSchemaGenerationPublicAssertions
{
    internal static async Task FirstAsync(TextIndexMaintenanceResult actual, TextIndexMaintenanceRequest request,
        NativeTextSelectedSchemaPublicState original, ResourceDefinition schema, NodeStatus before, NodeStatus after)
    {
        await NativeTextMaintenanceRf3Assertions.ResultAsync(actual, request);
        await Assert.That(actual.Source!.SchemaVersion).IsEqualTo(2L);
        await Assert.That(actual.IndexedThroughSequence).IsEqualTo(original.Built.IndexedThroughSequence);
        var digest = Convert.ToHexStringLower(SHA256.HashData(NativeSerialization.Serialize(schema)));
        var authority = original.Built.Source! with { SchemaVersion = 2L, ResourceSha256 = digest };
        await NativeTextMaintenanceRf3ReplayAssertions.SourceAsync(actual.Source!, authority, before, after);
        await Assert.That(actual.IndexedThroughSequence).IsEqualTo(actual.Source!.ThroughSequence);
    }

    internal static async Task ReplayAsync(RequestCqrsRf3Callers caller,
        NativeTextSelectedSchemaPublicState current, OutboxStatus baseline, CancellationToken token)
    {
        foreach (var path in new[] { NativeTextMaintenancePath.Sdk, NativeTextMaintenancePath.Mcp })
        {
            var before = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.StatusAsync(token));
            var replay = await NativeTextMaintenanceRf3Call.ExecuteAsync(caller.Sdk, caller.Mcp,
                current.Build, path, token);
            var after = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.StatusAsync(token));
            await NativeTextMaintenanceRf3Assertions.ResultAsync(replay, current.Build);
            await NativeTextMaintenanceRf3ReplayAssertions.StableAsync(replay, current.Built);
            await NativeTextMaintenanceRf3ReplayAssertions.SourceAsync(replay.Source!, current.Built.Source!, before, after);
            await NativeTextSelectedSchemaPublicAssertions.PagesAsync(caller, current, selected: true, token);
            await NativeTextSelectedSchemaPublicAssertions.OutboxAsync(caller, current.Scenario.Partition, baseline, token);
        }
    }
}
