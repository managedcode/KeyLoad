using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextFreshSchemaGenerationPublicTrial
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var prepared = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async caller =>
            {
                var original = await NativeTextSelectedSchemaPublicSeed.CreateAsync(caller, token);
                var schema = original.Resource with
                {
                    SchemaVersion = 2L
                };
                await SqlRf3Protocol.EqualAsync(schema, await McpCallerAssertions.SdkSuccessAsync(
                    await caller.Sdk.ConfigureResourceAsync(Guid.NewGuid(), new(original.Scenario.Partition.TenantId,
                        original.Scenario.Partition.DatabaseId, schema), token)));
                await NativeTextSelectedSchemaPublicAssertions.SchemaAsync(caller, original, token);
                await NativeTextSelectedSchemaPublicAssertions.PagesAsync(caller, original, selected: false, token);
                var beforeBuild = await McpCallerAssertions.SdkSuccessAsync(
                    await caller.Sdk.OutboxStatusAsync(original.Scenario.Partition, token));
                await NativeTextSelectedSchemaPublicAssertions.RefuseAsync(caller, original, beforeBuild, token);
                var request = (await original.Scenario.RequestAsync(caller.Sdk, caller.Mcp, token)) with
                {
                    Consumer = new(original.Scenario.Partition, "schema2-native-text"),
                    IndexGeneration = 2L
                };
                var before = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.StatusAsync(token));
                var built = await NativeTextMaintenanceRf3Call.ExecuteAsync(caller.Sdk, caller.Mcp,
                    request, NativeTextMaintenancePath.Mcp, token);
                var after = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.StatusAsync(token));
                await NativeTextFreshSchemaGenerationPublicAssertions.FirstAsync(built, request,
                    original, schema, before, after);
                var current = original with
                {
                    Resource = schema,
                    Build = request,
                    Built = built
                };
                await NativeTextSelectedSchemaPublicAssertions.PagesAsync(caller, current, selected: true, token);
                var baseline = await McpCallerAssertions.SdkSuccessAsync(
                    await caller.Sdk.OutboxStatusAsync(original.Scenario.Partition, token));
                await CheckAsync(caller, original, current, baseline, token);
                return (Original: original, Current: current, Outbox: baseline,
                    Status: await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.StatusAsync(token)));
            }, token);
        for (var cycle = 0; cycle < 2; cycle++)
        {
            await NativeTextProtectedPublicColdRestart.RunAsync(fixture, token);
            _ = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
                fixture.AdminKey, async caller =>
                {
                    var status = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.StatusAsync(token));
                    await Assert.That(status.NodeId).IsEqualTo(prepared.Status.NodeId);
                    await Assert.That(status.Incarnation).IsEqualTo(prepared.Status.Incarnation);
                    await Assert.That(status.Applied).IsGreaterThanOrEqualTo(prepared.Status.Applied);
                    await Assert.That(status.ReadGeneration).IsGreaterThanOrEqualTo(prepared.Status.ReadGeneration);
                    await CheckAsync(caller, prepared.Original, prepared.Current, prepared.Outbox, token);
                    return true;
                }, token);
        }
    }

    private static async Task CheckAsync(RequestCqrsRf3Callers caller,
        NativeTextSelectedSchemaPublicState original, NativeTextSelectedSchemaPublicState current,
        OutboxStatus baseline, CancellationToken token)
    {
        await NativeTextSelectedSchemaPublicAssertions.SchemaAsync(caller, original, token);
        await NativeTextSelectedSchemaPublicAssertions.RefuseAsync(caller, original, baseline, token);
        await NativeTextSelectedSchemaPublicAssertions.PagesAsync(caller, current, selected: true, token);
        await NativeTextSelectedSchemaPublicAssertions.ReplayAsync(caller, original, baseline, token);
        await NativeTextFreshSchemaGenerationPublicAssertions.ReplayAsync(caller, current, baseline, token);
    }
}
