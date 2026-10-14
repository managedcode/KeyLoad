using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresDecimalRf3Cancellation
{
    internal static async Task RunAsync(ClusterFixture fixture, SqlNullOrderingRf3State state,
        KeyLoadClient root, CancellationToken token)
    {
        var image = await SqlNullOrderingRf3Assertions.ImageAsync(root, state, token);
        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
            state.Reader.Secret, async caller =>
        {
            var failures = new List<Exception>();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await using var wire = await PostgresRf3Caller.ConnectAsync(fixture.App, McpCallerProtocol.Node3,
                    state.Reader.Principal.Id, state.Reader.Secret, PostgresRf3Certificate.RootPath(fixture), token);
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    using var cancelled = new CancellationTokenSource();
                    await cancelled.CancelAsync();
                    var operation = PostgresDecimalRf3Protocol.Operation(PostgresDecimalRf3Protocol.BetweenScores);
                    var sdk = await global::TUnit.Assertions.Assert.ThrowsAsync<OperationCanceledException>(
                        () => caller.Sdk.ExecuteSqlAsync(operation, cancelled.Token));
                    await global::TUnit.Assertions.Assert.That(sdk?.CancellationToken).IsEqualTo(cancelled.Token);
                    var mcp = await global::TUnit.Assertions.Assert.ThrowsAsync<OperationCanceledException>(
                        () => caller.Mcp.CallAsync(SqlOperationProtocol.ToolName, operation, cancelled.Token));
                    await global::TUnit.Assertions.Assert.That(mcp?.CancellationToken).IsEqualTo(cancelled.Token);
                    var parameter = new global::Npgsql.NpgsqlParameter(PostgresDecimalRf3Protocol.Parameter,
                        global::NpgsqlTypes.NpgsqlDbType.Numeric)
                    {
                        Value = PostgresDecimalRf3Protocol.BetweenScores
                    };
                    var driver = await global::TUnit.Assertions.Assert.ThrowsAsync<OperationCanceledException>(
                        () => wire.ExecuteAsync<QueryPage>(PostgresDecimalRf3Protocol.DriverSql,
                            [parameter], prepared: true, cancelled.Token));
                    await global::TUnit.Assertions.Assert.That(driver?.CancellationToken).IsEqualTo(cancelled.Token);
                }, failures);
            }, failures);
            global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
            return true;
        }, token);
        await global::TUnit.Assertions.Assert.That(await SqlNullOrderingRf3Assertions.ImageAsync(root, state, token)).IsEqualTo(image);
        await PostgresDecimalRf3Pages.HealthyAsync(fixture, state, token);
    }
}
