using KeyLoad.IntegrationTests.Features.ClientApi;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresPortalOwnershipFlow
{
    internal static async Task RunAsync(int dialect)
    {
        var selection = new PostgresRf3Selection(dialect);
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture(selection);
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                try
                {
                    var state = await PostgresRf3Flow.SeedAsync(fixture, dialect);
                    await PostgresPortalOwnershipWave.RunAsync(fixture, state, policyControls: true);
                    for (var cold = PostgresRf3Protocol.FirstColumn; cold < PostgresPortalOwnershipProtocol.ColdCuts; cold++)
                    {
                        using (var deadline = McpCallerDeadline.Create())
                        { await ModelSqlRf3Cold.RunAsync(fixture, deadline.Token); }
                        await PostgresPortalOwnershipWave.RunAsync(fixture, state, policyControls: false);
                    }
                }
                catch (Exception) { selection.RetainEvidence(); throw; }
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}
