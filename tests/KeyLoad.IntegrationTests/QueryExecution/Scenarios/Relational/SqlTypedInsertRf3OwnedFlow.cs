using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlTypedInsertRf3OwnedFlow
{
    internal static async Task RunAsync(ClusterFixture fixture, PostgresRf3Selection selection)
    {
        var state = await SqlCreateTableRf3Deadline.RunAsync(async token =>
        {
            var seeded = await SqlTypedInsertRf3Hot.SeedAsync(fixture, token);
            return await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
                fixture.AdminKey, async root =>
            {
                await SqlTypedInsertRf3Wave.RunAsync(fixture, seeded, root.Sdk, token);
                await SqlTypedInsertRf3Faults.RunAsync(fixture, seeded, root.Sdk, token);
                await SqlTypedInsertRf3ResourcePolicy.RunAsync(fixture, seeded, root.Sdk, token);
                var cancelled = await SqlTypedInsertRf3Cancellation.RunAsync(fixture, selection, seeded, root.Sdk, token);
                await SqlTypedInsertRf3Wave.RunAsync(fixture, cancelled, root.Sdk, token);
                var restored = await SqlTypedInsertRf3PrincipalPolicy.RunAsync(fixture, cancelled, root.Sdk, token);
                await SqlTypedInsertRf3Wave.RunAsync(fixture, restored, root.Sdk, token);
                return restored;
            }, token);
        });
        for (var cold = FirstCold; cold <= LastCold; cold++)
        {
            state = await SqlCreateTableRf3Deadline.RunAsync(async token =>
            {
                await ModelSqlRf3Cold.RunAsync(fixture, token);
                await WaveAsync(fixture, state, token);
                var next = await SqlTypedInsertRf3ColdHealthy.RunAsync(fixture, state, cold == LastCold, token);
                await WaveAsync(fixture, next, token);
                return next;
            });
        }
    }
    private static async Task WaveAsync(ClusterFixture fixture, SqlTypedInsertRf3State state, CancellationToken token)
        => await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async root =>
        {
            await SqlTypedInsertRf3Wave.RunAsync(fixture, state, root.Sdk, token);
            return true;
        }, token);
    private const int FirstCold = 1;
    private const int LastCold = 2;
}
