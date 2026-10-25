using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using Npgsql;
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
        await RejectAsync(fixture, state, () => new(SqlNullOrderingClusterProtocol.CurrentSql),
            ErrorCode.PermissionDenied, PostgresClusterProtocol.PermissionSqlState, token);
        await RejectAsync(fixture, state, () => new("SELECT ALL TOP (5) label AS label FROM null_ordering ORDER BY score ASC"),
            ErrorCode.PermissionDenied, PostgresClusterProtocol.PermissionSqlState, token);
        await RejectAsync(fixture, state, () => new(SqlAllClusterPages.DuplicateSql),
            ErrorCode.PermissionDenied, PostgresClusterProtocol.PermissionSqlState, token);
        await RejectAsync(fixture, state, () => new(SqlImplicitAliasClusterPages.BasicSql),
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
        await RejectAsync(fixture, state, () => new(SqlNullOrderingClusterProtocol.BadSql),
            ErrorCode.Validation, SqlNullOrderingClusterProtocol.ValidationSqlState, token);
        await UsingInvalidAsync(fixture, state, root, token);
        foreach (var command in new Func<NpgsqlCommand>[]
        { () => new("SELECT TOP (5) label FROM null_ordering ORDER BY score NULLS FIRST"), () => new("SELECT TOP (5) label FROM null_ordering ORDER BY score NULLS LAST") })
        {
            await RejectAsync(fixture, state, command, ErrorCode.Validation, SqlNullOrderingClusterProtocol.ValidationSqlState, token);
        }
        foreach (var command in new Func<NpgsqlCommand>[]
        { () => new("SELECT TOP (5) WITH TIES label FROM null_ordering ORDER BY score"), () => new("SELECT TOP (50) PERCENT label FROM null_ordering ORDER BY score") })
        {
            await RejectAsync(fixture, state, command, ErrorCode.UnsupportedCapability, PostgresClusterProtocol.UnsupportedSqlState, token);
        }
        await RejectAsync(fixture, state, () => new("SELECT ALL ALL TOP (5) label FROM null_ordering"),
            ErrorCode.Validation, SqlNullOrderingClusterProtocol.ValidationSqlState, token);
        await RejectAsync(fixture, state, () => new("SELECT TOP (5) score copied,label copied FROM null_ordering"),
            ErrorCode.Validation, SqlNullOrderingClusterProtocol.ValidationSqlState, token);
        await global::TUnit.Assertions.Assert.That(await SqlNullOrderingClusterAssertions.ImageAsync(root, state, token)).IsEqualTo(image);
        await SqlNullOrderingClusterPages.HealthyAsync(fixture, state, root, token);
    }

    private static async Task RejectAsync(ClusterFixture fixture, SqlNullOrderingClusterState state,
        Func<NpgsqlCommand> command, ErrorCode code, string sqlState, CancellationToken token)
    {
        await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
            state.Reader.Secret, async caller =>
        {
            var sql = PostgresClusterCommands.Sql(command);
            var request = SqlNullOrderingClusterProtocol.Operation(sql);
            var result = await caller.Sdk.ExecuteSqlAsync(request, token);
            await global::TUnit.Assertions.Assert.That(result.IsSuccess).IsFalse();
            await global::TUnit.Assertions.Assert.That(result.Value.ValueKind).IsEqualTo(global::System.Text.Json.JsonValueKind.Undefined);
            await global::TUnit.Assertions.Assert.That(result.Problem?.ErrorCode).IsEqualTo(code.ToString());
            _ = (await McpCallerAssertions.ErrorAsync(await caller.Mcp.CallAsync(SqlOperationProtocol.ToolName, request, token), code, dispatched: code == ErrorCode.PermissionDenied)).Value;
            var failures = new List<Exception>();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await using var wire = await PostgresClusterCaller.ConnectAsync(fixture.App, McpCallerProtocol.Node3,
                    state.Reader.Principal.Id, state.Reader.Secret, token);
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    global::Npgsql.NpgsqlParameter[] arguments = sql.Contains(SqlNullOrderingClusterProtocol.DirectionOperand, StringComparison.Ordinal)
                        ? [new() { ParameterName = SqlNullOrderingClusterProtocol.DirectionName,
                            NpgsqlDbType = global::NpgsqlTypes.NpgsqlDbType.Text, Value = SqlNullOrderingClusterProtocol.DirectionValue }]
                        : [];
                    await PostgresClusterAssertions.ErrorAsync(wire.ExecuteAsync<QueryPage>(command, arguments, prepared: true, token), sqlState, code);
                    if (code is ErrorCode.Validation or ErrorCode.UnsupportedCapability)
                    {
                        await SqlNullOrderingClusterPages.CaseAsync(caller, wire, false, true, token);
                        await SqlAllClusterPages.DuplicatesAsync(caller, wire, token);
                        await SqlImplicitAliasClusterPages.HealthyAsync(caller, wire, token);
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
        foreach (var command in new Func<NpgsqlCommand>[]
        {
            () => new("SELECT TOP (5) label AS label FROM null_ordering ORDER BY score USING "),
            () => new("SELECT TOP (5) label AS label FROM null_ordering ORDER BY score USING '>' "),
            () => new("SELECT TOP (5) label AS label FROM null_ordering ORDER BY score USING @direction "),
            () => new("SELECT TOP (5) label AS label FROM null_ordering ORDER BY score USING = "),
            () => new("SELECT TOP (5) label AS label FROM null_ordering ORDER BY score USING <= "),
            () => new("SELECT TOP (5) label AS label FROM null_ordering ORDER BY score ASC USING < "),
            () => new("SELECT TOP (5) label AS label FROM null_ordering ORDER BY score USING < DESC ")
        })
        {
            await RejectAsync(fixture, state, command, ErrorCode.Validation, SqlNullOrderingClusterProtocol.ValidationSqlState, token);
            await global::TUnit.Assertions.Assert.That(await SqlNullOrderingClusterAssertions.ImageAsync(root, state, token)).IsEqualTo(image);
        }
    }

    private static async Task PersistAsync(KeyLoadClient root, PrincipalRecord expected, CancellationToken token)
        => await SqlClusterProtocol.EqualAsync(expected,
            await McpCallerAssertions.SdkSuccessAsync(await root.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token)));
}
