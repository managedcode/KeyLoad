namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlBooleanTruthClusterFlow
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
                SqlBooleanTruthClusterState state;
                using (var deadline = global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerDeadline.Create())
                {
                    var seed = await SqlBooleanTruthClusterSetup.CreateAsync(fixture, deadline.Token);
                    state = new(seed, SqlNullOrderingClusterProtocol.SeedRevision, SqlBooleanTruthClusterState.HealthyJson, seed.Receipt);
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

    private static async Task<SqlBooleanTruthClusterState> WaveAsync(ClusterFixture fixture, SqlBooleanTruthClusterState state, bool faults)
    {
        using var deadline = global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerDeadline.Create();
        return await global::KeyLoad.IntegrationTests.Features.OrleansExecution.RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerProtocol.Node1, fixture.AdminKey, async root =>
        {
            var before = await SqlBooleanTruthClusterImage.ReadAsync(root, state, deadline.Token);
            await SqlBooleanTruthClusterPages.HealthyAsync(fixture, state, deadline.Token);
            await SqlNullOrderingClusterPages.ReplayAsync(fixture, state.Seed, deadline.Token);
            await global::TUnit.Assertions.Assert.That(await SqlBooleanTruthClusterImage.ReadAsync(root, state, deadline.Token)).IsEqualTo(before);
            if (!faults)
            {
                return state;
            }
            await SqlBooleanTruthClusterRefusals.InvalidAsync(fixture, state, root, deadline.Token);
            await SqlBooleanTruthClusterCancellation.RunAsync(fixture, state, root, deadline.Token);
            await SqlBooleanTruthClusterPolicy.RunAsync(fixture, state, root, deadline.Token);
            return await SqlBooleanTruthClusterRepair.RunAsync(fixture, state, root, deadline.Token);
        }, deadline.Token);
    }
}
