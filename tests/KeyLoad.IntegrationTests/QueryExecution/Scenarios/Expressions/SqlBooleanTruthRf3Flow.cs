namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlBooleanTruthRf3Flow
{
    internal static async Task RunAsync()
    {
        var selection = new PostgresRf3Selection(PostgresRf3Protocol.ScalarDialect);
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture(selection);
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                try
                {
                    SqlBooleanTruthRf3State state;
                    using (var deadline = global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerDeadline.Create())
                    {
                        var seed = await SqlBooleanTruthRf3Setup.CreateAsync(fixture, deadline.Token);
                        state = new(seed, SqlNullOrderingRf3Protocol.SeedRevision, SqlBooleanTruthRf3State.HealthyJson, seed.Receipt);
                    }
                    state = await WaveAsync(fixture, state, faults: true);
                    for (var cold = 0; cold < SqlNullOrderingRf3Protocol.ColdCuts; cold++)
                    {
                        using var deadline = global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerDeadline.Create();
                        await ModelSqlRf3Cold.RunAsync(fixture, deadline.Token);
                        state = await WaveAsync(fixture, state, faults: false);
                    }
                }
                catch (Exception) { selection.RetainEvidence(); throw; }
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task<SqlBooleanTruthRf3State> WaveAsync(ClusterFixture fixture, SqlBooleanTruthRf3State state, bool faults)
    {
        using var deadline = global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerDeadline.Create();
        return await global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerProtocol.Node1, fixture.AdminKey, async root =>
        {
            var before = await SqlBooleanTruthRf3Image.ReadAsync(root, state, deadline.Token);
            await SqlBooleanTruthRf3Pages.HealthyAsync(fixture, state, deadline.Token);
            await SqlNullOrderingRf3Pages.ReplayAsync(fixture, state.Seed, deadline.Token);
            await global::TUnit.Assertions.Assert.That(await SqlBooleanTruthRf3Image.ReadAsync(root, state, deadline.Token)).IsEqualTo(before);
            if (!faults)
            {
                return state;
            }
            await SqlBooleanTruthRf3Refusals.InvalidAsync(fixture, state, root, deadline.Token);
            await SqlBooleanTruthRf3Cancellation.RunAsync(fixture, state, root, deadline.Token);
            await SqlBooleanTruthRf3Policy.RunAsync(fixture, state, root, deadline.Token);
            return await SqlBooleanTruthRf3Repair.RunAsync(fixture, state, root, deadline.Token);
        }, deadline.Token);
    }
}
