using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresRf3Flow
{
    internal static async Task RunAsync(int dialect)
    {
        var selection = new PostgresRf3Selection(dialect, ObserveCancellation: true);
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture(selection);
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                try
                {
                    var state = await SeedAsync(fixture, dialect);
                    await PostgresRf3StalledBootstrap.RunAsync(fixture, state);
                    var backpressure = await PostgresRf3BackpressureFlow.CreateAndRunAsync(fixture, selection);
                    using (var deadline = McpCallerDeadline.Create())
                    {
                        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
                            fixture.AdminKey, async root =>
                        {
                            var image = await PostgresRf3Assertions.ImageAsync(root.Sdk, deadline.Token);
                            await PostgresRf3Bootstrap.RunAsync(fixture, state, deadline.Token);
                            await PostgresRf3Capacity.RunAsync(fixture, state, root.Sdk, deadline.Token);
                            await global::TUnit.Assertions.Assert.That(await PostgresRf3Assertions.ImageAsync(root.Sdk, deadline.Token)).IsEqualTo(image);
                            return true;
                        }, deadline.Token);
                        await PostgresRf3Parity.RunAsync(fixture, state, deadline.Token);
                        await PostgresRf3Receipts.RunAsync(fixture, state, deadline.Token);
                        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
                            fixture.AdminKey, async root =>
                        {
                            await PostgresRf3Policy.RunAsync(fixture, state, root.Sdk, deadline.Token);
                            await PostgresRf3Cancellation.RunAsync(fixture, selection, state, root.Sdk, deadline.Token);
                            return true;
                        }, deadline.Token);
                    }
                    for (var cold = PostgresRf3Protocol.FirstColumn; cold < ColdWaves; cold++)
                    {
                        using var deadline = McpCallerDeadline.Create();
                        await ModelSqlRf3Cold.RunAsync(fixture, deadline.Token);
                        await PostgresRf3Parity.RunAsync(fixture, state, deadline.Token);
                        await PostgresRf3Receipts.RunAsync(fixture, state, deadline.Token);
                        await PostgresRf3BackpressureFlow.HealthyAsync(fixture, backpressure, deadline.Token);
                    }
                }
                catch (Exception) { selection.RetainEvidence(); throw; }
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task<PostgresRf3State> SeedAsync(ClusterFixture fixture, int dialect)
    {
        using var deadline = McpCallerDeadline.Create();
        return await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async root =>
        {
            var identities = await PostgresRf3Seed.ConfigureAsync(root.Sdk, deadline.Token);
            var command = PostgresRf3Seed.Command();
            CommitReceipt? receipt = null;
            var failures = new List<Exception>();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await using var wire = await PostgresRf3Caller.ConnectAsync(fixture.App, McpCallerProtocol.Node3,
                    identities.Writer.Principal.Id, identities.Writer.Secret,
                    PostgresRf3Certificate.RootPath(fixture), deadline.Token);
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    receipt = await wire.CommitAsync(command, prepared: true, deadline.Token);
                    await PostgresRf3Assertions.ReceiptAsync(command, receipt);
                    await SqlRf3Protocol.EqualAsync(receipt, await wire.CommitAsync(command, prepared: false, deadline.Token));
                }, failures);
            }, failures);
            global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
            return new PostgresRf3State(identities.Writer, identities.Reader, command,
                receipt ?? throw new InvalidOperationException(PostgresRf3Protocol.InvalidResult), dialect);
        }, deadline.Token);
    }

    private const int ColdWaves = 2;
}
