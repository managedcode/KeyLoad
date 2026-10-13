using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresInt16Rf3Policy
{
    internal static async Task RunAsync(ClusterFixture fixture, SqlNullOrderingRf3State state,
        KeyLoadClient root, CancellationToken token)
    {
        var original = state.Reader.Principal;
        var denied = original with
        {
            PolicyEpoch = original.PolicyEpoch + SqlNullOrderingRf3Protocol.DenialAdvance,
            Grants = [new(PostgresRf3State.Partition.DatabaseId,
                SqlNullOrderingRf3Protocol.Collection, Capability.DocumentsRead)]
        };
        var image = await SqlNullOrderingRf3Assertions.ImageAsync(root, state, token);
        await PersistAsync(root, denied, token);
        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
            state.Reader.Secret, async caller =>
        {
            var operation = PostgresInt16Rf3Protocol.Operation(PostgresInt16Rf3Protocol.ProbeValue);
            var sdk = await caller.Sdk.ExecuteSqlAsync(operation, token);
            await global::TUnit.Assertions.Assert.That(sdk.IsSuccess).IsFalse();
            await global::TUnit.Assertions.Assert.That(sdk.Value.ValueKind)
                .IsEqualTo(global::System.Text.Json.JsonValueKind.Undefined);
            await global::TUnit.Assertions.Assert.That(sdk.Problem?.ErrorCode).IsEqualTo(ErrorCode.PermissionDenied.ToString());
            await McpCallerAssertions.ErrorAsync(await caller.Mcp.CallAsync(SqlOperationProtocol.ToolName,
                operation, token), ErrorCode.PermissionDenied, dispatched: true);
            var ast = PostgresInt16Rf3Protocol.Ast(PostgresInt16Rf3Protocol.ProbeValue);
            var astSdk = await caller.Sdk.QueryAstAsync(ast, token);
            await global::TUnit.Assertions.Assert.That(astSdk.IsSuccess).IsFalse();
            await global::TUnit.Assertions.Assert.That(astSdk.Problem?.ErrorCode).IsEqualTo(ErrorCode.PermissionDenied.ToString());
            await McpCallerAssertions.ErrorAsync(await caller.Mcp.CallAsync(McpCallerTools.QueryAst,
                ast, token), ErrorCode.PermissionDenied, dispatched: true);
            await WireAsync(fixture, state, token);
            return true;
        }, token);
        await global::TUnit.Assertions.Assert.That(await SqlNullOrderingRf3Assertions.ImageAsync(root, state, token)).IsEqualTo(image);
        await PersistAsync(root, original with
        { PolicyEpoch = original.PolicyEpoch + SqlNullOrderingRf3Protocol.RepairAdvance }, token);
        await PostgresInt16Rf3Pages.HealthyAsync(fixture, state, token);
        await global::TUnit.Assertions.Assert.That(await SqlNullOrderingRf3Assertions.ImageAsync(root, state, token)).IsEqualTo(image);
    }

    private static async Task WireAsync(ClusterFixture fixture, SqlNullOrderingRf3State state, CancellationToken token)
    {
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var wire = await PostgresRf3Caller.ConnectAsync(fixture.App, McpCallerProtocol.Node3,
                state.Reader.Principal.Id, state.Reader.Secret, PostgresRf3Certificate.RootPath(fixture), token);
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                var parameter = new global::Npgsql.NpgsqlParameter(PostgresInt16Rf3Protocol.Parameter,
                    global::NpgsqlTypes.NpgsqlDbType.Smallint)
                { Value = PostgresInt16Rf3Protocol.ProbeValue };
                await PostgresRf3Assertions.ErrorAsync(wire.ExecuteAsync<QueryPage>(
                    PostgresInt16Rf3Protocol.DriverSql, [parameter], prepared: true, token),
                    PostgresRf3Protocol.PermissionSqlState, ErrorCode.PermissionDenied);
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task PersistAsync(KeyLoadClient root, PrincipalRecord expected, CancellationToken token)
        => await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
            await root.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token)));
}
