using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

[NotInParallel]
internal sealed class SqlCreateTableRf3WholeFlowTests
{
    private const int ColdWaves = 2;
    private const int FinalCold = 1;
    [Test]
    public async Task OriginalCallerIdentityCreatesOnlyOnceAcrossSdkOfficialMcpNativeSessionAndTwoColdOwners()
    {
        var selection = new PostgresRf3Selection(SqlCreateTableRf3Literal.Dialect, ObserveCancellation: true);
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture(selection);
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                try
                {
                    var state = await SqlCreateTableRf3Deadline.RunAsync(async token =>
                    {
                        var seeded = await SqlCreateTableRf3Hot.SeedAsync(fixture, token);
                        await SqlCreateTableRf3Concurrency.RunAsync(fixture, seeded, token);
                        await WaveAsync(fixture, seeded, token);
                        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
                            fixture.AdminKey, async root =>
                        {
                            await SqlCreateTableRf3Cancellation.RunAsync(fixture, selection, seeded, root.Sdk, token);
                            return true;
                        }, token);
                        return await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
                            fixture.AdminKey, root => SqlCreateTableRf3Policy.RunAsync(fixture, seeded, root.Sdk, token), token);
                    });
                    for (var cold = 0; cold < ColdWaves; cold++)
                    {
                        state = await SqlCreateTableRf3Deadline.RunAsync(async token =>
                        {
                            await ModelSqlRf3Cold.RunAsync(fixture, token);
                            await WaveAsync(fixture, state, token);
                            return await SqlCreateTableRf3Healthy.RunAsync(fixture, state, cold == FinalCold, token);
                        });
                    }
                }
                catch (Exception) { selection.RetainEvidence(); throw; }
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task WaveAsync(ClusterFixture fixture, SqlCreateTableRf3State state, CancellationToken token)
        => await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async root =>
        {
            var image = await SqlCreateTableRf3Images.ReadAsync(root.Sdk, token);
            await SqlCreateTableRf3Wire.RunAsync(fixture, state, root.Sdk, token);
            await SqlCreateTableRf3Pages.RunAsync(fixture, state, token);
            await SqlCreateTableRf3Images.RequireAsync(root.Sdk, image, token);
            return true;
        }, token);
}
