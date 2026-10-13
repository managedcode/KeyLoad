using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresErrorReceiverFlow
{
    internal static async Task RunAsync(int dialect)
    {
        var selection = new PostgresRf3Selection(dialect, ObserveCancellation: true, ObserveErrorDelivery: true);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture(selection);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                try
                {
                    var state = await PostgresRf3Flow.SeedAsync(fixture, dialect);
                    using (var deadline = McpCallerDeadline.Create())
                    { await PostgresErrorReceiverHealthy.RunAsync(fixture, state, deadline.Token); }
                    using (var deadline = McpCallerDeadline.Create())
                    {
                        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
                            fixture.AdminKey, async root =>
                        {
                            var control = selection.ErrorDelivery
                                ?? throw new InvalidOperationException(PostgresErrorReceiverProtocol.Invalid);
                            await PostgresErrorReceiverFault.RunAsync(fixture, state, control, root.Sdk, deadline.Token);
                            await PostgresErrorReceiverPolicy.SetAsync(root.Sdk, state, denied: false, deadline.Token);
                            return true;
                        }, deadline.Token);
                    }
                    using (var deadline = McpCallerDeadline.Create())
                    { await PostgresErrorReceiverHealthy.RunAsync(fixture, state, deadline.Token); }
                    for (var cold = PostgresRf3Protocol.FirstColumn; cold < PostgresErrorReceiverProtocol.ColdWaves; cold++)
                    {
                        using var deadline = McpCallerDeadline.Create();
                        await ModelSqlRf3Cold.RunAsync(fixture, deadline.Token);
                        await PostgresErrorReceiverHealthy.RunAsync(fixture, state, deadline.Token);
                    }
                }
                catch (Exception) { selection.RetainEvidence(); throw; }
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
