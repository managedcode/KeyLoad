using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
namespace KeyLoad.IntegrationTests.QueryExecution;

internal static class SqlNullOrderingClusterFlow
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
                SqlNullOrderingClusterState state;
                using (var setupDeadline = McpCallerDeadline.Create())
                {
                    state = await SqlNullOrderingClusterSetup.CreateAsync(fixture, setupDeadline.Token);
                }
                await WaveAsync(fixture, state, faultControls: true);
                for (var cold = 0; cold < SqlNullOrderingClusterProtocol.ColdCuts; cold++)
                {
                    using var deadline = McpCallerDeadline.Create();
                    await ModelSqlClusterCold.RunAsync(fixture, deadline.Token);
                    await WaveAsync(fixture, state, faultControls: false);
                }
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task WaveAsync(ClusterFixture fixture, SqlNullOrderingClusterState state, bool faultControls)
    {
        using var deadline = McpCallerDeadline.Create();
        await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async root =>
        {
            var image = await SqlNullOrderingClusterAssertions.ImageAsync(root.Sdk, state, deadline.Token);
            await SqlNullOrderingClusterPages.HealthyAsync(fixture, state, root.Sdk, deadline.Token);
            await SqlNullOrderingClusterPages.ReplayAsync(fixture, state, deadline.Token);
            await global::TUnit.Assertions.Assert.That(await SqlNullOrderingClusterAssertions.ImageAsync(root.Sdk, state, deadline.Token)).IsEqualTo(image);
            if (faultControls)
            {
                await SqlNullOrderingClusterRefusals.InvalidAsync(fixture, state, root.Sdk, deadline.Token);
                await SqlNullOrderingClusterCancellation.RunAsync(fixture, state, root.Sdk, deadline.Token);
                await SqlNullOrderingClusterRefusals.DenyRepairAsync(fixture, state, root.Sdk, deadline.Token);
            }
            return true;
        }, deadline.Token);
    }
}
