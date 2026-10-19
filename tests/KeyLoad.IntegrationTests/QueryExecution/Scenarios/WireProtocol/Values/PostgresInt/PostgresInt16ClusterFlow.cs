using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresInt16ClusterFlow
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
                using (var setup = McpCallerDeadline.Create())
                {
                    state = await SqlNullOrderingClusterSetup.CreateAsync(fixture, setup.Token);
                }
                await WaveAsync(fixture, state, controls: true);
                for (var cold = 0; cold < SqlNullOrderingClusterProtocol.ColdCuts; cold++)
                {
                    using var deadline = McpCallerDeadline.Create();
                    await ModelSqlClusterCold.RunAsync(fixture, deadline.Token);
                    await WaveAsync(fixture, state, controls: false);
                }
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task WaveAsync(ClusterFixture fixture, SqlNullOrderingClusterState state, bool controls)
    {
        using var deadline = McpCallerDeadline.Create();
        await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async root =>
        {
            var image = await SqlNullOrderingClusterAssertions.ImageAsync(root.Sdk, state, deadline.Token);
            await PostgresInt16ClusterPages.HealthyAsync(fixture, state, deadline.Token);
            await SqlNullOrderingClusterPages.ReplayAsync(fixture, state, deadline.Token);
            if (controls)
            {
                await PostgresInt16ClusterWireControls.RunAsync(fixture, state, deadline.Token);
                await global::TUnit.Assertions.Assert.That(await SqlNullOrderingClusterAssertions.ImageAsync(root.Sdk, state, deadline.Token)).IsEqualTo(image);
                await PostgresInt16ClusterCancellation.RunAsync(fixture, state, root.Sdk, deadline.Token);
                await PostgresInt16ClusterPolicy.RunAsync(fixture, state, root.Sdk, deadline.Token);
            }
            await global::TUnit.Assertions.Assert.That(await SqlNullOrderingClusterAssertions.ImageAsync(root.Sdk, state, deadline.Token)).IsEqualTo(image);
            return true;
        }, deadline.Token);
    }
}
