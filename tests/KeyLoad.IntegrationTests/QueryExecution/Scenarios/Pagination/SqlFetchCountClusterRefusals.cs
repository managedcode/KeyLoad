using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
namespace KeyLoad.IntegrationTests.QueryExecution;

internal static class SqlFetchCountClusterRefusals
{
    internal static async Task DenyRepairAsync(ClusterFixture fixture, SqlFetchCountClusterState state,
        KeyLoadClient root, CancellationToken token)
    {
        var original = state.Reader.Principal;
        var denied = original with
        {
            PolicyEpoch = original.PolicyEpoch + SqlFetchCountClusterProtocol.DenialAdvance,
            Grants = [new(PostgresClusterState.Partition.DatabaseId,
            SqlFetchCountClusterProtocol.Collection, Capability.DocumentsRead)]
        };
        await PersistAsync(root, denied, token);
        var image = await SqlFetchCountClusterAssertions.ImageAsync(root, state, token);
        await RejectAsync(fixture, state, SqlFetchCountClusterProtocol.FirstOne,
            ErrorCode.PermissionDenied, PostgresClusterProtocol.PermissionSqlState, token);
        await global::TUnit.Assertions.Assert.That(await SqlFetchCountClusterAssertions.ImageAsync(root, state, token)).IsEqualTo(image);
        await PersistAsync(root, original with
        {
            PolicyEpoch = original.PolicyEpoch + SqlFetchCountClusterProtocol.RepairAdvance
        }, token);
        await SqlFetchCountClusterPages.HealthyAsync(fixture, state, root, token);
    }

    internal static async Task InvalidAsync(ClusterFixture fixture, SqlFetchCountClusterState state,
        KeyLoadClient root, CancellationToken token)
    {
        var image = await SqlFetchCountClusterAssertions.ImageAsync(root, state, token);
        await RejectAsync(fixture, state, SqlFetchCountClusterProtocol.BadSql,
            ErrorCode.Validation, SqlFetchCountClusterProtocol.ValidationSqlState, token);
        await RejectAsync(fixture, state, SqlFetchCountClusterProtocol.TiesSql,
            ErrorCode.UnsupportedCapability, SqlFetchCountClusterProtocol.UnsupportedSqlState, token);
        await global::TUnit.Assertions.Assert.That(await SqlFetchCountClusterAssertions.ImageAsync(root, state, token)).IsEqualTo(image);
        await SqlFetchCountClusterPages.HealthyAsync(fixture, state, root, token);
    }

    private static async Task RejectAsync(ClusterFixture fixture, SqlFetchCountClusterState state,
        string sql, ErrorCode code, string sqlState, CancellationToken token)
    {
        await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
            state.Reader.Secret, async caller =>
        {
            var request = SqlFetchCountClusterProtocol.Operation(sql);
            var result = await caller.Sdk.ExecuteSqlAsync(request, token);
            await global::TUnit.Assertions.Assert.That(result.IsSuccess).IsFalse();
            await global::TUnit.Assertions.Assert.That(result.Value.ValueKind).IsEqualTo(global::System.Text.Json.JsonValueKind.Undefined);
            await global::TUnit.Assertions.Assert.That(result.Problem?.ErrorCode).IsEqualTo(code.ToString());
            _ = (await McpCallerAssertions.ErrorAsync(await caller.Mcp.CallAsync(SqlOperationProtocol.ToolName, request, token), code, dispatched: true)).Value;
            var failures = new List<Exception>();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await using var wire = await PostgresClusterCaller.ConnectAsync(fixture.App, McpCallerProtocol.Node3,
                    state.Reader.Principal.Id, state.Reader.Secret, PostgresClusterCertificate.RootPath(fixture), token);
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    await PostgresClusterAssertions.ErrorAsync(wire.ExecuteAsync<QueryPage>(sql, [], prepared: true, token), sqlState, code);
                    if (code != ErrorCode.PermissionDenied)
                    {
                        await SqlFetchCountClusterAssertions.PageAsync(await wire.ExecuteAsync<QueryPage>(SqlFetchCountClusterProtocol.FirstOne, [], prepared: false, token), 1);
                    }
                }, failures);
            }, failures);
            global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
            return true;
        }, token);
    }

    private static async Task PersistAsync(KeyLoadClient root, PrincipalRecord expected, CancellationToken token)
        => await SqlClusterProtocol.EqualAsync(expected,
            await McpCallerAssertions.SdkSuccessAsync(await root.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token)));
}
