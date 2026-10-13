using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresDefaultJoinCalls
{
    private const string ExplicitJoin = " INNER JOIN ";
    private const string DefaultJoin = " JOIN ";
    private const string On = " ON ";
    private const string Where = " WHERE ";
    private const string MissingOn = "The original joined query has no ON clause.";

    internal static async Task RefuseRepairAsync(PostgresRf3State state, PostgresRf3Caller wire,
        RequestCqrsRf3Callers reader, KeyLoadClient root, CancellationToken token)
    {
        if (state.Dialect != PostgresRf3Protocol.JoinDialect)
        { return; }
        var plain = state.Sql.Replace(ExplicitJoin, DefaultJoin, StringComparison.Ordinal);
        var on = plain.IndexOf(On, StringComparison.Ordinal);
        if (on < default(int))
        { throw new InvalidOperationException(MissingOn); }
        var before = await PostgresRf3Assertions.ImageAsync(root, token);
        foreach (var bad in new[] { plain[..on], plain.Replace(On, Where, StringComparison.Ordinal) })
        {
            var operation = new SqlOperationRequest(PostgresRf3State.Partition, bad,
                AllowFullScan: true, QueryDialectVersion: state.Dialect);
            var failure = await reader.Sdk.ExecuteSqlAsync(operation, token);
            await TUnit.Assertions.Assert.That(failure.IsSuccess).IsFalse();
            await TUnit.Assertions.Assert.That(failure.Value.ValueKind).IsEqualTo(System.Text.Json.JsonValueKind.Undefined);
            await TUnit.Assertions.Assert.That(failure.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.Validation));
            await McpCallerAssertions.ErrorAsync(await reader.Mcp.CallAsync(SqlOperationProtocol.ToolName,
                operation, token), ErrorCode.Validation, dispatched: true);
            await PostgresRf3Assertions.ErrorAsync(wire.ExecuteAsync<QueryPage>(bad, [], prepared: false, token),
                SqlNullOrderingRf3Protocol.ValidationSqlState, ErrorCode.Validation);
            await TUnit.Assertions.Assert.That(await PostgresRf3Assertions.ImageAsync(root, token)).IsEqualTo(before);
            await HealthyAsync(state, reader, wire, token);
            await TUnit.Assertions.Assert.That(await PostgresRf3Assertions.ImageAsync(root, token)).IsEqualTo(before);
        }
    }

    internal static async Task DeniedAsync(PostgresRf3State state, RequestCqrsRf3Callers reader,
        PostgresRf3Caller wire, CancellationToken token)
    {
        if (state.Dialect != PostgresRf3Protocol.JoinDialect)
        { return; }
        var plain = state.Sql.Replace(ExplicitJoin, DefaultJoin, StringComparison.Ordinal);
        var operation = new SqlOperationRequest(PostgresRf3State.Partition, plain,
            AllowFullScan: true, QueryDialectVersion: state.Dialect);
        await PostgresRf3Assertions.ErrorAsync(wire.ExecuteAsync<QueryPage>(plain, [], prepared: false, token),
            PostgresRf3Protocol.PermissionSqlState, ErrorCode.PermissionDenied);
        var result = await reader.Sdk.ExecuteSqlAsync(operation, token);
        await TUnit.Assertions.Assert.That(result.IsSuccess).IsFalse();
        await TUnit.Assertions.Assert.That(result.Value.ValueKind).IsEqualTo(System.Text.Json.JsonValueKind.Undefined);
        await TUnit.Assertions.Assert.That(result.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        await McpCallerAssertions.ErrorAsync(await reader.Mcp.CallAsync(SqlOperationProtocol.ToolName,
            operation, token), ErrorCode.PermissionDenied, dispatched: true);
    }

    internal static async Task HealthyAsync(PostgresRf3State state, RequestCqrsRf3Callers reader,
        PostgresRf3Caller wire, CancellationToken token)
    {
        if (state.Dialect != PostgresRf3Protocol.JoinDialect)
        { return; }
        var plain = state.Sql.Replace(ExplicitJoin, DefaultJoin, StringComparison.Ordinal);
        var operation = new SqlOperationRequest(PostgresRf3State.Partition, plain,
            AllowFullScan: true, QueryDialectVersion: state.Dialect);
        await PostgresRf3Assertions.PageAsync(state, await SqlRf3Protocol.SdkAsync<QueryPage>(reader.Sdk, operation, token));
        await PostgresRf3Assertions.PageAsync(state, await SqlRf3Protocol.McpAsync<QueryPage>(reader.Mcp, operation, token));
        await PostgresRf3Assertions.PageAsync(state, await wire.ExecuteAsync<QueryPage>(plain, [], prepared: false, token));
        await PostgresRf3Assertions.PageAsync(state, await wire.ExecuteAsync<QueryPage>(plain, [], prepared: true, token));
    }
}
