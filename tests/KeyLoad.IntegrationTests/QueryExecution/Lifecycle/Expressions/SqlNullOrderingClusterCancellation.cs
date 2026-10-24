using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
namespace KeyLoad.IntegrationTests.QueryExecution;

internal static class SqlNullOrderingClusterCancellation
{
    internal static async Task RunAsync(ClusterFixture fixture, SqlNullOrderingClusterState state,
        KeyLoadClient root, CancellationToken token)
    {
        var image = await SqlNullOrderingClusterAssertions.ImageAsync(root, state, token);
        await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
            state.Reader.Secret, async caller =>
        {
            var failures = new List<Exception>();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await using var wire = await PostgresClusterCaller.ConnectAsync(fixture.App, McpCallerProtocol.Node3,
                    state.Reader.Principal.Id, state.Reader.Secret, PostgresClusterCertificate.RootPath(fixture), token);
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    using var cancelled = new CancellationTokenSource();
                    await cancelled.CancelAsync();
                    foreach (var sql in new[] { SqlNullOrderingClusterProtocol.AscFirst,
                        SqlAllClusterPages.Explicit(SqlNullOrderingClusterProtocol.AscFirst), SqlImplicitAliasClusterPages.BasicSql,
                        SqlNullOrderingClusterProtocol.Using(false, true) })
                    {
                        var operation = SqlNullOrderingClusterProtocol.Operation(sql);
                        var sdk = await global::TUnit.Assertions.Assert.ThrowsAsync<OperationCanceledException>(
                            () => caller.Sdk.ExecuteSqlAsync(operation, cancelled.Token));
                        await global::TUnit.Assertions.Assert.That(sdk?.CancellationToken).IsEqualTo(cancelled.Token);
                        var mcp = await global::TUnit.Assertions.Assert.ThrowsAsync<OperationCanceledException>(
                            () => caller.Mcp.CallAsync(SqlOperationProtocol.ToolName, operation, cancelled.Token));
                        await global::TUnit.Assertions.Assert.That(mcp?.CancellationToken).IsEqualTo(cancelled.Token);
                        var driver = await global::TUnit.Assertions.Assert.ThrowsAsync<OperationCanceledException>(
                            () => wire.ExecuteAsync<QueryPage>(sql, [], prepared: false, cancelled.Token));
                        await global::TUnit.Assertions.Assert.That(driver?.CancellationToken).IsEqualTo(cancelled.Token);
                        await global::TUnit.Assertions.Assert.That(await SqlNullOrderingClusterAssertions.ImageAsync(root, state, token)).IsEqualTo(image);
                        await SqlNullOrderingClusterPages.CaseAsync(caller, wire, false, true, token);
                        await SqlImplicitAliasClusterPages.HealthyAsync(caller, wire, token);
                    }
                }, failures);
            }, failures);
            global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
            return true;
        }, token);
        await global::TUnit.Assertions.Assert.That(await SqlNullOrderingClusterAssertions.ImageAsync(root, state, token)).IsEqualTo(image);
    }
}
