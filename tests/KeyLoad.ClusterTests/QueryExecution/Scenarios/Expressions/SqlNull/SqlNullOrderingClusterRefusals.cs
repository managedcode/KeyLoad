using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
namespace KeyLoad.ClusterTests.QueryExecution;

internal static class SqlNullOrderingClusterRefusals
{
    internal static async Task DenyRepairAsync(ClusterFixture fixture, SqlNullOrderingClusterState state,
        KeyLoadClient root, CancellationToken token)
    {
        var original = state.Reader.Principal;
        var denied = original with
        {
            PolicyEpoch = original.PolicyEpoch + SqlNullOrderingClusterProtocol.DenialAdvance,
            Grants = [new(PostgresClusterState.Partition.DatabaseId,
            SqlNullOrderingClusterProtocol.Collection, Capability.DocumentsRead)]
        };
        await PersistAsync(root, denied, token);
        var image = await SqlNullOrderingClusterAssertions.ImageAsync(root, state, token);
        await RejectAsync(fixture, state, SqlNullOrderingClusterProtocol.AscFirst,
            ErrorCode.PermissionDenied, PostgresClusterProtocol.PermissionSqlState, token);
        await RejectAsync(fixture, state, SqlNullOrderingClusterProtocol.Using(false, true),
            ErrorCode.PermissionDenied, PostgresClusterProtocol.PermissionSqlState, token);
        await RejectAsync(fixture, state, SqlAllClusterPages.DuplicateSql,
            ErrorCode.PermissionDenied, PostgresClusterProtocol.PermissionSqlState, token);
        await RejectAsync(fixture, state, SqlImplicitAliasClusterPages.BasicSql,
            ErrorCode.PermissionDenied, PostgresClusterProtocol.PermissionSqlState, token);
        await global::TUnit.Assertions.Assert.That(await SqlNullOrderingClusterAssertions.ImageAsync(root, state, token)).IsEqualTo(image);
        await PersistAsync(root, original with
        {
            PolicyEpoch = original.PolicyEpoch + SqlNullOrderingClusterProtocol.RepairAdvance
        }, token);
        await SqlNullOrderingClusterPages.HealthyAsync(fixture, state, root, token);
    }

    internal static async Task InvalidAsync(ClusterFixture fixture, SqlNullOrderingClusterState state,
        KeyLoadClient root, CancellationToken token)
    {
        var image = await SqlNullOrderingClusterAssertions.ImageAsync(root, state, token);
        await RejectAsync(fixture, state, SqlNullOrderingClusterProtocol.BadSql,
            ErrorCode.Validation, SqlNullOrderingClusterProtocol.ValidationSqlState, token);
        await UsingInvalidAsync(fixture, state, root, token);
        await RejectAsync(fixture, state, "SELECT ALL ALL label FROM null_ordering LIMIT 5",
            ErrorCode.Validation, SqlNullOrderingClusterProtocol.ValidationSqlState, token);
        await RejectAsync(fixture, state, "SELECT score copied,label copied FROM null_ordering LIMIT 5",
            ErrorCode.Validation, SqlNullOrderingClusterProtocol.ValidationSqlState, token);
        await global::TUnit.Assertions.Assert.That(await SqlNullOrderingClusterAssertions.ImageAsync(root, state, token)).IsEqualTo(image);
        await SqlNullOrderingClusterPages.HealthyAsync(fixture, state, root, token);
    }

    private static async Task RejectAsync(ClusterFixture fixture, SqlNullOrderingClusterState state,
        string sql, ErrorCode code, string sqlState, CancellationToken token)
    {
        await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
            state.Reader.Secret, async caller =>
        {
            var request = SqlNullOrderingClusterProtocol.Operation(sql);
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
                    global::Npgsql.NpgsqlParameter[] arguments = sql.Contains(SqlNullOrderingClusterProtocol.DirectionOperand, StringComparison.Ordinal)
                        ? [new() { ParameterName = SqlNullOrderingClusterProtocol.DirectionName,
                            NpgsqlDbType = global::NpgsqlTypes.NpgsqlDbType.Text, Value = SqlNullOrderingClusterProtocol.DirectionValue }]
                        : [];
                    await PostgresClusterAssertions.ErrorAsync(wire.ExecuteAsync<QueryPage>(sql, arguments, prepared: true, token), sqlState, code);
                    if (code is ErrorCode.Validation or ErrorCode.UnsupportedCapability)
                    {
                        await SqlNullOrderingClusterAssertions.PageAsync(await wire.ExecuteAsync<QueryPage>(SqlNullOrderingClusterProtocol.AscFirst, [], prepared: false, token), false, true);
                        await SqlNullOrderingClusterAssertions.PageAsync(await wire.ExecuteAsync<QueryPage>(SqlAllClusterPages.Explicit(SqlNullOrderingClusterProtocol.AscFirst), [], prepared: false, token), false, true);
                        await SqlImplicitAliasClusterPages.HealthyAsync(caller, wire, token);
                        var repaired = SqlNullOrderingClusterProtocol.Operation(SqlNullOrderingClusterProtocol.Using(false, true));
                        await SqlNullOrderingClusterAssertions.PageAsync(await SqlClusterProtocol.SdkAsync<QueryPage>(caller.Sdk, repaired, token), false, true);
                        await SqlNullOrderingClusterAssertions.PageAsync(await SqlClusterProtocol.McpAsync<QueryPage>(caller.Mcp, repaired, token), false, true);
                        await SqlNullOrderingClusterAssertions.PageAsync(await wire.ExecuteAsync<QueryPage>(repaired.Sql, [], prepared: false, token), false, true);
                    }
                }, failures);
            }, failures);
            global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
            return true;
        }, token);
    }

    private static async Task UsingInvalidAsync(ClusterFixture fixture, SqlNullOrderingClusterState state,
        KeyLoadClient root, CancellationToken token)
    {
        var image = await SqlNullOrderingClusterAssertions.ImageAsync(root, state, token);
        foreach (var (direction, code) in new[]
        {
            (" USING ", ErrorCode.Validation), (" USING '>' ", ErrorCode.Validation),
            (" USING @direction ", ErrorCode.Validation), (" USING = ", ErrorCode.UnsupportedCapability),
            (" USING <= ", ErrorCode.UnsupportedCapability), (" ASC USING < ", ErrorCode.UnsupportedCapability),
            (" USING < DESC ", ErrorCode.UnsupportedCapability)
        })
        {
            var sql = SqlNullOrderingClusterProtocol.AscFirst.Replace(SqlNullOrderingClusterProtocol.Ascending, direction, StringComparison.Ordinal);
            await RejectAsync(fixture, state, sql, code,
                code == ErrorCode.Validation ? SqlNullOrderingClusterProtocol.ValidationSqlState : PostgresClusterProtocol.UnsupportedSqlState, token);
            await global::TUnit.Assertions.Assert.That(await SqlNullOrderingClusterAssertions.ImageAsync(root, state, token)).IsEqualTo(image);
        }
    }

    private static async Task PersistAsync(KeyLoadClient root, PrincipalRecord expected, CancellationToken token)
        => await SqlClusterProtocol.EqualAsync(expected,
            await McpCallerAssertions.SdkSuccessAsync(await root.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token)));
}
