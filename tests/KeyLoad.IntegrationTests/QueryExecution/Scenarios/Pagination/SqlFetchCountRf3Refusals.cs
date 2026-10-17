using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlFetchCountRf3Refusals
{
    internal static async Task DenyRepairAsync(ClusterFixture fixture, SqlFetchCountRf3State state,
        KeyLoadClient root, CancellationToken token)
    {
        var original = state.Reader.Principal;
        var denied = original with
        {
            PolicyEpoch = original.PolicyEpoch + SqlFetchCountRf3Protocol.DenialAdvance,
            Grants = [new(PostgresRf3State.Partition.DatabaseId,
            SqlFetchCountRf3Protocol.Collection, Capability.DocumentsRead)]
        };
        await PersistAsync(root, denied, token);
        var image = await SqlFetchCountRf3Assertions.ImageAsync(root, state, token);
        await RejectAsync(fixture, state, SqlFetchCountRf3Protocol.FirstOne,
            ErrorCode.PermissionDenied, PostgresRf3Protocol.PermissionSqlState, token);
        await global::TUnit.Assertions.Assert.That(await SqlFetchCountRf3Assertions.ImageAsync(root, state, token)).IsEqualTo(image);
        await PersistAsync(root, original with
        {
            PolicyEpoch = original.PolicyEpoch + SqlFetchCountRf3Protocol.RepairAdvance
        }, token);
        await SqlFetchCountRf3Pages.HealthyAsync(fixture, state, root, token);
    }

    internal static async Task InvalidAsync(ClusterFixture fixture, SqlFetchCountRf3State state,
        KeyLoadClient root, CancellationToken token)
    {
        var image = await SqlFetchCountRf3Assertions.ImageAsync(root, state, token);
        await RejectAsync(fixture, state, SqlFetchCountRf3Protocol.BadSql,
            ErrorCode.Validation, SqlFetchCountRf3Protocol.ValidationSqlState, token);
        await RejectAsync(fixture, state, SqlFetchCountRf3Protocol.TiesSql,
            ErrorCode.UnsupportedCapability, SqlFetchCountRf3Protocol.UnsupportedSqlState, token);
        await global::TUnit.Assertions.Assert.That(await SqlFetchCountRf3Assertions.ImageAsync(root, state, token)).IsEqualTo(image);
        await SqlFetchCountRf3Pages.HealthyAsync(fixture, state, root, token);
    }

    private static async Task RejectAsync(ClusterFixture fixture, SqlFetchCountRf3State state,
        string sql, ErrorCode code, string sqlState, CancellationToken token)
    {
        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
            state.Reader.Secret, async caller =>
        {
            var request = SqlFetchCountRf3Protocol.Operation(sql);
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
                    if (code != ErrorCode.PermissionDenied)
                    {
                        await SqlFetchCountRf3Assertions.PageAsync(await wire.ExecuteAsync<QueryPage>(SqlFetchCountRf3Protocol.FirstOne, [], prepared: false, token), 1);
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
