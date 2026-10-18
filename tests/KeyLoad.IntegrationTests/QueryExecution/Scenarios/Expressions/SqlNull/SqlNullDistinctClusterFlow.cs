namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlNullDistinctClusterFlow
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
                SqlNullDistinctClusterState state;
                using (var deadline = global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerDeadline.Create())
                {
                    var seed = await SqlNullOrderingClusterSetup.CreateAsync(fixture, deadline.Token);
                    state = new(seed, SqlNullOrderingClusterProtocol.SeedRevision, SqlNullDistinctClusterState.HealthyJson, seed.Receipt);
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

    private static async Task<SqlNullDistinctClusterState> WaveAsync(ClusterFixture fixture, SqlNullDistinctClusterState state, bool faults)
    {
        using var deadline = global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerDeadline.Create();
        return await global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerProtocol.Node1, fixture.AdminKey, async root =>
        {
            var before = await SqlNullDistinctClusterImage.ReadAsync(root, state, deadline.Token);
            await SqlNullDistinctClusterPages.HealthyAsync(fixture, state, deadline.Token);
            await SqlNullOrderingClusterPages.ReplayAsync(fixture, state.Seed, deadline.Token);
            await global::TUnit.Assertions.Assert.That(await SqlNullDistinctClusterImage.ReadAsync(root, state, deadline.Token)).IsEqualTo(before);
            if (!faults)
            {
                return state;
            }
            await SqlNullDistinctClusterRefusals.InvalidAsync(fixture, state, root, deadline.Token);
            await SqlNullDistinctClusterCancellation.RunAsync(fixture, state, root, deadline.Token);
            await SqlNullDistinctClusterPolicy.RunAsync(fixture, state, root, deadline.Token);
            return await SqlNullDistinctClusterRepair.RunAsync(fixture, state, root, deadline.Token);
        }, deadline.Token);
    }
}
