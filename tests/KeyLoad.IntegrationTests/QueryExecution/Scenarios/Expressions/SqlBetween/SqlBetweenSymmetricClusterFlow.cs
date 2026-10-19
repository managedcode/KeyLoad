namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlBetweenSymmetricClusterFlow
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
                SqlBetweenSymmetricClusterState state;
                using (var deadline = global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerDeadline.Create())
                {
                    var seed = await SqlBetweenSymmetricClusterSetup.CreateAsync(fixture, deadline.Token);
                    state = new(seed, SqlNullOrderingClusterProtocol.SeedRevision, SqlBetweenSymmetricClusterState.HealthyJson, seed.Receipt);
                }
                state = await WaveAsync(fixture, state, faults: true);
                for (var cold = 0; cold < SqlNullOrderingClusterProtocol.ColdCuts; cold++)
                {
                    using var deadline = global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerDeadline.Create();
                    await ModelSqlClusterCold.RunAsync(fixture, deadline.Token);
                    state = await WaveAsync(fixture, state, faults: false);
                }
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task<SqlBetweenSymmetricClusterState> WaveAsync(ClusterFixture fixture, SqlBetweenSymmetricClusterState state, bool faults)
    {
        using var deadline = global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerDeadline.Create();
        return await global::KeyLoad.IntegrationTests.Features.OrleansExecution.RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerProtocol.Node1, fixture.AdminKey, async root =>
        {
            var before = await SqlBetweenSymmetricClusterImage.ReadAsync(root, state, deadline.Token);
            await SqlBetweenSymmetricClusterPages.HealthyAsync(fixture, state, deadline.Token);
            await SqlNullOrderingClusterPages.ReplayAsync(fixture, state.Seed, deadline.Token);
            await global::TUnit.Assertions.Assert.That(await SqlBetweenSymmetricClusterImage.ReadAsync(root, state, deadline.Token)).IsEqualTo(before);
            if (!faults)
            {
                return state;
            }
            await SqlBetweenSymmetricClusterRefusals.InvalidAsync(fixture, state, root, deadline.Token);
            await SqlBetweenSymmetricClusterCancellation.RunAsync(fixture, state, root, deadline.Token);
            await SqlBetweenSymmetricClusterPolicy.RunAsync(fixture, state, root, deadline.Token);
            return await SqlBetweenSymmetricClusterRepair.RunAsync(fixture, state, root, deadline.Token);
        }, deadline.Token);
    }
}
