using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
namespace KeyLoad.IntegrationTests.QueryExecution;

internal static class PostgresInt16ClusterCancellation
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
                    var operation = PostgresInt16ClusterProtocol.Operation(PostgresInt16ClusterProtocol.ProbeValue);
                    await SqlClusterProtocol.SdkCancelledAsync(await caller.Sdk.ExecuteSqlAsync(operation, cancelled.Token));
                    var mcp = await global::TUnit.Assertions.Assert.ThrowsAsync<OperationCanceledException>(
                        () => caller.Mcp.CallAsync(SqlOperationProtocol.ToolName, operation, cancelled.Token));
                    await global::TUnit.Assertions.Assert.That(mcp?.CancellationToken).IsEqualTo(cancelled.Token);
                    var parameter = new global::Npgsql.NpgsqlParameter(PostgresInt16ClusterProtocol.Parameter,
                        global::NpgsqlTypes.NpgsqlDbType.Smallint)
                    {
                        Value = PostgresInt16ClusterProtocol.ProbeValue
                    };
                    var driver = await global::TUnit.Assertions.Assert.ThrowsAsync<OperationCanceledException>(
                        () => wire.ExecuteAsync<QueryPage>(PostgresInt16ClusterProtocol.DriverSql,
                            [parameter], prepared: true, cancelled.Token));
                    await global::TUnit.Assertions.Assert.That(driver?.CancellationToken).IsEqualTo(cancelled.Token);
                }, failures);
            }, failures);
            global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
            return true;
        }, token);
        await global::TUnit.Assertions.Assert.That(await SqlNullOrderingClusterAssertions.ImageAsync(root, state, token)).IsEqualTo(image);
        await PostgresInt16ClusterPages.HealthyAsync(fixture, state, token);
    }
}
