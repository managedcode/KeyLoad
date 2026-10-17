using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlNullOrderingRf3Flow
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
                    SqlNullOrderingRf3State state;
                    using (var setupDeadline = McpCallerDeadline.Create())
                    {
                        state = await SqlNullOrderingRf3Setup.CreateAsync(fixture, setupDeadline.Token);
                    }
                    await WaveAsync(fixture, state, faultControls: true);
                    for (var cold = 0; cold < SqlNullOrderingRf3Protocol.ColdCuts; cold++)
                    {
                        using var deadline = McpCallerDeadline.Create();
                        await ModelSqlRf3Cold.RunAsync(fixture, deadline.Token);
                        await WaveAsync(fixture, state, faultControls: false);
                    }
                }
                catch (Exception) { selection.RetainEvidence(); throw; }
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task WaveAsync(ClusterFixture fixture, SqlNullOrderingRf3State state, bool faultControls)
    {
        using var deadline = McpCallerDeadline.Create();
        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async root =>
        {
            var image = await SqlNullOrderingRf3Assertions.ImageAsync(root.Sdk, state, deadline.Token);
            await SqlNullOrderingRf3Pages.HealthyAsync(fixture, state, root.Sdk, deadline.Token);
            await SqlNullOrderingRf3Pages.ReplayAsync(fixture, state, deadline.Token);
            await global::TUnit.Assertions.Assert.That(await SqlNullOrderingRf3Assertions.ImageAsync(root.Sdk, state, deadline.Token)).IsEqualTo(image);
            if (faultControls)
            {
                await SqlNullOrderingRf3Refusals.InvalidAsync(fixture, state, root.Sdk, deadline.Token);
                await SqlNullOrderingRf3Cancellation.RunAsync(fixture, state, root.Sdk, deadline.Token);
                await SqlNullOrderingRf3Refusals.DenyRepairAsync(fixture, state, root.Sdk, deadline.Token);
            }
            return true;
        }, deadline.Token);
    }
}
