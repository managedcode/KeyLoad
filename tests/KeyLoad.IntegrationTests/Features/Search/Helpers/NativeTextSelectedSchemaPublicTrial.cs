using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextSelectedSchemaPublicTrial
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var prepared = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async caller =>
            {
                var state = await NativeTextSelectedSchemaPublicSeed.CreateAsync(caller, token);
                var schema = state.Resource with
                {
                    SchemaVersion = 2L
                };
                await SqlRf3Protocol.EqualAsync(schema, await McpCallerAssertions.SdkSuccessAsync(
                    await caller.Sdk.ConfigureResourceAsync(Guid.NewGuid(), new(state.Scenario.Partition.TenantId,
                        state.Scenario.Partition.DatabaseId, schema), token)));
                await NativeTextSelectedSchemaPublicAssertions.SchemaAsync(caller, state, token);
                await NativeTextSelectedSchemaPublicAssertions.PagesAsync(caller, state, selected: false, token);
                var baseline = await McpCallerAssertions.SdkSuccessAsync(
                    await caller.Sdk.OutboxStatusAsync(state.Scenario.Partition, token));
                await CheckAsync(caller, state, baseline, token);
                return (State: state, Outbox: baseline,
                    Status: await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.StatusAsync(token)));
            }, token);
        for (var cycle = 0; cycle < 2; cycle++)
        {
            await NativeTextProtectedPublicColdRestart.RunAsync(fixture, token);
            _ = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
                fixture.AdminKey, async caller =>
                {
                    var current = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.StatusAsync(token));
                    await Assert.That(current.NodeId).IsEqualTo(prepared.Status.NodeId);
                    await Assert.That(current.Incarnation).IsEqualTo(prepared.Status.Incarnation);
                    await Assert.That(current.ReadGeneration).IsGreaterThanOrEqualTo(prepared.Status.ReadGeneration);
                    await Assert.That(current.Applied).IsGreaterThanOrEqualTo(prepared.Status.Applied);
                    await CheckAsync(caller, prepared.State, prepared.Outbox, token);
                    return true;
                }, token);
        }
    }

    private static async Task CheckAsync(RequestCqrsRf3Callers caller,
        NativeTextSelectedSchemaPublicState state, OutboxStatus baseline, CancellationToken token)
    {
        await NativeTextSelectedSchemaPublicAssertions.SchemaAsync(caller, state, token);
        await NativeTextSelectedSchemaPublicAssertions.RefuseAsync(caller, state, baseline, token);
        await NativeTextSelectedSchemaPublicAssertions.ReplayAsync(caller, state, baseline, token);
    }
}
