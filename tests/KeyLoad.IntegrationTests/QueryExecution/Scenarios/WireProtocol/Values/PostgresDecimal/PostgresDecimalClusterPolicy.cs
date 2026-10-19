using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresDecimalClusterPolicy
{
    internal static async Task RunAsync(ClusterFixture fixture, SqlNullOrderingClusterState state,
        KeyLoadClient root, CancellationToken token)
    {
        var original = state.Reader.Principal;
        var denied = original with
        {
            PolicyEpoch = original.PolicyEpoch + SqlNullOrderingClusterProtocol.DenialAdvance,
            Grants = [new(PostgresClusterState.Partition.DatabaseId,
                SqlNullOrderingClusterProtocol.Collection, Capability.DocumentsRead)]
        };
        var image = await SqlNullOrderingClusterAssertions.ImageAsync(root, state, token);
        await PersistAsync(root, denied, token);
        await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
            state.Reader.Secret, async caller =>
        {
            var operation = PostgresDecimalClusterProtocol.Operation(PostgresDecimalClusterProtocol.BetweenScores);
            var sdk = await caller.Sdk.ExecuteSqlAsync(operation, token);
            await global::TUnit.Assertions.Assert.That(sdk.IsSuccess).IsFalse();
            await global::TUnit.Assertions.Assert.That(sdk.Value.ValueKind)
                .IsEqualTo(global::System.Text.Json.JsonValueKind.Undefined);
            await global::TUnit.Assertions.Assert.That(sdk.Problem?.ErrorCode).IsEqualTo(ErrorCode.PermissionDenied.ToString());
            await McpCallerAssertions.ErrorAsync(await caller.Mcp.CallAsync(SqlOperationProtocol.ToolName,
                operation, token), ErrorCode.PermissionDenied, dispatched: true);
            var ast = PostgresDecimalClusterProtocol.Ast(PostgresDecimalClusterProtocol.BetweenScores);
            var astSdk = await caller.Sdk.QueryAstAsync(ast, token);
            await global::TUnit.Assertions.Assert.That(astSdk.IsSuccess).IsFalse();
            await global::TUnit.Assertions.Assert.That(astSdk.Problem?.ErrorCode).IsEqualTo(ErrorCode.PermissionDenied.ToString());
            await McpCallerAssertions.ErrorAsync(await caller.Mcp.CallAsync(McpCallerTools.QueryAst,
                ast, token), ErrorCode.PermissionDenied, dispatched: true);
            await WireAsync(fixture, state, token);
            return true;
        }, token);
        await global::TUnit.Assertions.Assert.That(await SqlNullOrderingClusterAssertions.ImageAsync(root, state, token)).IsEqualTo(image);
        await PersistAsync(root, original with
        {
            PolicyEpoch = original.PolicyEpoch + SqlNullOrderingClusterProtocol.RepairAdvance
        }, token);
        await PostgresDecimalClusterPages.HealthyAsync(fixture, state, token);
        await global::TUnit.Assertions.Assert.That(await SqlNullOrderingClusterAssertions.ImageAsync(root, state, token)).IsEqualTo(image);
    }

    private static async Task WireAsync(ClusterFixture fixture, SqlNullOrderingClusterState state, CancellationToken token)
    {
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var wire = await PostgresClusterCaller.ConnectAsync(fixture.App, McpCallerProtocol.Node3,
                state.Reader.Principal.Id, state.Reader.Secret, PostgresClusterCertificate.RootPath(fixture), token);
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                var parameter = new global::Npgsql.NpgsqlParameter(PostgresDecimalClusterProtocol.Parameter,
                    global::NpgsqlTypes.NpgsqlDbType.Numeric)
                {
                    Value = PostgresDecimalClusterProtocol.BetweenScores
                };
                await PostgresClusterAssertions.ErrorAsync(wire.ExecuteAsync<QueryPage>(
                    PostgresDecimalClusterProtocol.DriverSql, [parameter], prepared: true, token),
                    PostgresClusterProtocol.PermissionSqlState, ErrorCode.PermissionDenied);
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task PersistAsync(KeyLoadClient root, PrincipalRecord expected, CancellationToken token)
        => await SqlClusterProtocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
            await root.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token)));
}
