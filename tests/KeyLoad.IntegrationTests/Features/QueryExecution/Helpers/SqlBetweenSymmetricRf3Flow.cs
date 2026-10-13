namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlBetweenSymmetricRf3Flow
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
                    SqlBetweenSymmetricRf3State state;
                    using (var deadline = global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerDeadline.Create())
                    {
                        var seed = await SqlBetweenSymmetricRf3Setup.CreateAsync(fixture, deadline.Token);
                        state = new(seed, SqlNullOrderingRf3Protocol.SeedRevision, SqlBetweenSymmetricRf3State.HealthyJson, seed.Receipt);
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

    private static async Task<SqlBetweenSymmetricRf3State> WaveAsync(ClusterFixture fixture, SqlBetweenSymmetricRf3State state, bool faults)
    {
        using var deadline = global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerDeadline.Create();
        return await global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerProtocol.Node1, fixture.AdminKey, async root =>
        {
            var before = await SqlBetweenSymmetricRf3Image.ReadAsync(root, state, deadline.Token);
            await SqlBetweenSymmetricRf3Pages.HealthyAsync(fixture, state, deadline.Token);
            await SqlNullOrderingRf3Pages.ReplayAsync(fixture, state.Seed, deadline.Token);
            await global::TUnit.Assertions.Assert.That(await SqlBetweenSymmetricRf3Image.ReadAsync(root, state, deadline.Token)).IsEqualTo(before);
            if (!faults)
            { return state; }
            await SqlBetweenSymmetricRf3Refusals.InvalidAsync(fixture, state, root, deadline.Token);
            await SqlBetweenSymmetricRf3Cancellation.RunAsync(fixture, state, root, deadline.Token);
            await SqlBetweenSymmetricRf3Policy.RunAsync(fixture, state, root, deadline.Token);
            return await SqlBetweenSymmetricRf3Repair.RunAsync(fixture, state, root, deadline.Token);
        }, deadline.Token);
    }
}
