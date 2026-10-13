using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlNullOrderingRf3Refusals
{
    internal static async Task DenyRepairAsync(ClusterFixture fixture, SqlNullOrderingRf3State state,
        KeyLoadClient root, CancellationToken token)
    {
        var original = state.Reader.Principal;
        var denied = original with
        {
            PolicyEpoch = original.PolicyEpoch + SqlNullOrderingRf3Protocol.DenialAdvance,
            Grants = [new(PostgresRf3State.Partition.DatabaseId,
            SqlNullOrderingRf3Protocol.Collection, Capability.DocumentsRead)]
        };
        await PersistAsync(root, denied, token);
        var image = await SqlNullOrderingRf3Assertions.ImageAsync(root, state, token);
        await RejectAsync(fixture, state, SqlNullOrderingRf3Protocol.AscFirst,
            ErrorCode.PermissionDenied, PostgresRf3Protocol.PermissionSqlState, token);
        await RejectAsync(fixture, state, SqlAllRf3Pages.DuplicateSql,
            ErrorCode.PermissionDenied, PostgresRf3Protocol.PermissionSqlState, token);
        await RejectAsync(fixture, state, SqlImplicitAliasRf3Pages.BasicSql,
            ErrorCode.PermissionDenied, PostgresRf3Protocol.PermissionSqlState, token);
        await global::TUnit.Assertions.Assert.That(await SqlNullOrderingRf3Assertions.ImageAsync(root, state, token)).IsEqualTo(image);
        await PersistAsync(root, original with { PolicyEpoch = original.PolicyEpoch + SqlNullOrderingRf3Protocol.RepairAdvance }, token);
        await SqlNullOrderingRf3Pages.HealthyAsync(fixture, state, root, token);
    }

    internal static async Task InvalidAsync(ClusterFixture fixture, SqlNullOrderingRf3State state,
        KeyLoadClient root, CancellationToken token)
    {
        var image = await SqlNullOrderingRf3Assertions.ImageAsync(root, state, token);
        await RejectAsync(fixture, state, SqlNullOrderingRf3Protocol.BadSql,
            ErrorCode.Validation, SqlNullOrderingRf3Protocol.ValidationSqlState, token);
        await RejectAsync(fixture, state, "SELECT ALL ALL label FROM null_ordering LIMIT 5",
            ErrorCode.Validation, SqlNullOrderingRf3Protocol.ValidationSqlState, token);
        await RejectAsync(fixture, state, "SELECT score copied,label copied FROM null_ordering LIMIT 5",
            ErrorCode.Validation, SqlNullOrderingRf3Protocol.ValidationSqlState, token);
        await global::TUnit.Assertions.Assert.That(await SqlNullOrderingRf3Assertions.ImageAsync(root, state, token)).IsEqualTo(image);
        await SqlNullOrderingRf3Pages.HealthyAsync(fixture, state, root, token);
    }

    private static async Task RejectAsync(ClusterFixture fixture, SqlNullOrderingRf3State state,
        string sql, ErrorCode code, string sqlState, CancellationToken token)
    {
        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
            state.Reader.Secret, async caller =>
        {
            var request = SqlNullOrderingRf3Protocol.Operation(sql);
            var result = await caller.Sdk.ExecuteSqlAsync(request, token);
            await global::TUnit.Assertions.Assert.That(result.IsSuccess).IsFalse();
            await global::TUnit.Assertions.Assert.That(result.Value.ValueKind).IsEqualTo(global::System.Text.Json.JsonValueKind.Undefined);
            await global::TUnit.Assertions.Assert.That(result.Problem?.ErrorCode).IsEqualTo(code.ToString());
            await McpCallerAssertions.ErrorAsync(await caller.Mcp.CallAsync(SqlOperationProtocol.ToolName, request, token), code, dispatched: true);
            var failures = new List<Exception>();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await using var wire = await PostgresRf3Caller.ConnectAsync(fixture.App, McpCallerProtocol.Node3,
                    state.Reader.Principal.Id, state.Reader.Secret, PostgresRf3Certificate.RootPath(fixture), token);
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    await PostgresRf3Assertions.ErrorAsync(wire.ExecuteAsync<QueryPage>(sql, [], prepared: true, token), sqlState, code);
                    if (code == ErrorCode.Validation)
                    {
                        await SqlNullOrderingRf3Assertions.PageAsync(await wire.ExecuteAsync<QueryPage>(SqlNullOrderingRf3Protocol.AscFirst, [], prepared: false, token), false, true);
                        await SqlNullOrderingRf3Assertions.PageAsync(await wire.ExecuteAsync<QueryPage>(SqlAllRf3Pages.Explicit(SqlNullOrderingRf3Protocol.AscFirst), [], prepared: false, token), false, true);
                        await SqlImplicitAliasRf3Pages.HealthyAsync(caller, wire, token);
                    }
                }, failures);
            }, failures);
            global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
            return true;
        }, token);
    }

    private static async Task PersistAsync(KeyLoadClient root, PrincipalRecord expected, CancellationToken token)
        => await SqlRf3Protocol.EqualAsync(expected,
            await McpCallerAssertions.SdkSuccessAsync(await root.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token)));
}
