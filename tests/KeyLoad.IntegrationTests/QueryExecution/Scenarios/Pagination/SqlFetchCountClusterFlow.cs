using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlFetchCountClusterFlow
{
    internal static async Task RunAsync()
    {
        var selection = new PostgresClusterSelection(PostgresClusterProtocol.ScalarDialect);
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture(selection);
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                SqlFetchCountClusterState state;
                using (var setupDeadline = McpCallerDeadline.Create())
                {
                    state = await SqlFetchCountClusterSetup.CreateAsync(fixture, setupDeadline.Token);
                }
                await WaveAsync(fixture, state, faultControls: true);
                for (var cold = 0; cold < SqlFetchCountClusterProtocol.ColdCuts; cold++)
                {
                    using var deadline = McpCallerDeadline.Create();
                    await ModelSqlClusterCold.RunAsync(fixture, deadline.Token);
                    await WaveAsync(fixture, state, faultControls: false);
                }
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task WaveAsync(ClusterFixture fixture, SqlFetchCountClusterState state, bool faultControls)
    {
        using var deadline = McpCallerDeadline.Create();
        await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async root =>
        {
            var image = await SqlFetchCountClusterAssertions.ImageAsync(root.Sdk, state, deadline.Token);
            await SqlFetchCountClusterPages.HealthyAsync(fixture, state, root.Sdk, deadline.Token);
            await SqlFetchCountClusterPages.ReplayAsync(fixture, state, deadline.Token);
            await global::TUnit.Assertions.Assert.That(await SqlFetchCountClusterAssertions.ImageAsync(root.Sdk, state, deadline.Token)).IsEqualTo(image);
            if (faultControls)
            {
                await SqlFetchCountClusterRefusals.InvalidAsync(fixture, state, root.Sdk, deadline.Token);
                await SqlFetchCountClusterCancellation.RunAsync(fixture, state, root.Sdk, deadline.Token);
                await SqlFetchCountClusterRefusals.DenyRepairAsync(fixture, state, root.Sdk, deadline.Token);
            }
            return true;
        }, deadline.Token);
    }
}
