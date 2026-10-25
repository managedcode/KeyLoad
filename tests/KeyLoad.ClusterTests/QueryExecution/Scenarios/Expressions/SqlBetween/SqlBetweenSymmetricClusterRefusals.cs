using Npgsql;
namespace KeyLoad.ClusterTests.QueryExecution;

internal static class SqlBetweenSymmetricClusterRefusals
{
    internal static async Task RejectAsync(ClusterFixture fixture, SqlBetweenSymmetricClusterState state, Func<NpgsqlCommand> command,
        ErrorCode code, CancellationToken token, bool ast = false, bool healthy = false, bool dispatched = true)
    {
        await global::KeyLoad.ClusterTests.OrleansExecution.RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, global::KeyLoad.ClusterTests.ClientApi.McpCallerProtocol.Node2, state.Seed.Reader.Secret, async caller =>
        {
            var sql = PostgresClusterCommands.Sql(command);
            var request = SqlNullOrderingClusterProtocol.Operation(sql);
            var result = await caller.Sdk.ExecuteSqlAsync(request, token);
            await global::TUnit.Assertions.Assert.That(result.IsSuccess).IsFalse();
            await global::TUnit.Assertions.Assert.That(result.Value.ValueKind).IsEqualTo(global::System.Text.Json.JsonValueKind.Undefined);
            await global::TUnit.Assertions.Assert.That(result.Problem?.ErrorCode).IsEqualTo(code.ToString());
            _ = (await global::KeyLoad.ClusterTests.ClientApi.McpCallerAssertions.ErrorAsync(await caller.Mcp.CallAsync(SqlOperationProtocol.ToolName, request, token), code, dispatched)).Value;
            if (ast)
            {
                var operation = SqlBetweenSymmetricClusterCases.Ast(PostgresClusterState.Partition,
                    SqlBetweenSymmetricClusterCases.All[SqlBetweenSymmetricClusterState.AgainstOne]);
                var typed = await caller.Sdk.QueryAstAsync(operation, token);
                await global::TUnit.Assertions.Assert.That(typed.IsSuccess).IsFalse();
                await global::TUnit.Assertions.Assert.That(typed.Value).IsNull();
                await global::TUnit.Assertions.Assert.That(typed.Problem?.ErrorCode).IsEqualTo(code.ToString());
                _ = (await global::KeyLoad.ClusterTests.ClientApi.McpCallerAssertions.ErrorAsync(await caller.Mcp.CallAsync(global::KeyLoad.ClusterTests.ClientApi.McpCallerTools.QueryAst, operation, token), code, dispatched: true)).Value;
            }
            var failures = new List<Exception>();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await using var wire = await PostgresClusterCaller.ConnectAsync(fixture.App, global::KeyLoad.ClusterTests.ClientApi.McpCallerProtocol.Node3,
                    state.Seed.Reader.Principal.Id, state.Seed.Reader.Secret, token);
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    await PostgresClusterAssertions.ErrorAsync(wire.ExecuteAsync<QueryPage>(command, [], prepared: true, token),
                        code switch
                        {
                            ErrorCode.Validation => SqlNullOrderingClusterProtocol.ValidationSqlState,
                            ErrorCode.UnsupportedCapability => PostgresClusterProtocol.UnsupportedSqlState,
                            _ => PostgresClusterProtocol.PermissionSqlState
                        }, code);
                    if (healthy)
                    {
                        await SqlBetweenSymmetricClusterPages.CaseAsync(caller, wire,
                            SqlBetweenSymmetricClusterCases.All.Single(value => value.Clause == SqlBetweenSymmetricClusterCases.AsymmetricHealthy),
                            state.LowRevision, token);
                    }
                }, failures);
            }, failures);
            global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
            return true;
        }, token);
    }

    internal static async Task InvalidAsync(ClusterFixture fixture, SqlBetweenSymmetricClusterState state,
        global::KeyLoad.ClusterTests.OrleansExecution.RequestCqrsClusterCallers root, CancellationToken token)
    {
        var before = await SqlBetweenSymmetricClusterImage.ReadAsync(root, state, token);
        foreach (var (command, code) in new (Func<NpgsqlCommand>, ErrorCode)[]
        {
            (() => new(SqlBetweenSymmetricClusterCases.Prefix + "score BETWEEN 2" + SqlBetweenSymmetricClusterCases.Tail), ErrorCode.Validation),
            (() => new(SqlBetweenSymmetricClusterCases.Prefix + "score BETWEEN 2 OR 1" + SqlBetweenSymmetricClusterCases.Tail), ErrorCode.Validation),
            (() => new(SqlBetweenSymmetricClusterCases.Prefix + "score BETWEEN SYMMETRIC 2 1" + SqlBetweenSymmetricClusterCases.Tail), ErrorCode.Validation),
            (() => new(SqlBetweenSymmetricClusterCases.Prefix + "score BETWEEN SYMMETRIC 2 OR 1" + SqlBetweenSymmetricClusterCases.Tail), ErrorCode.Validation),
            (() => new(SqlBetweenSymmetricClusterCases.Prefix + "score NOT BETWEEN SYMMETRIC 2 1" + SqlBetweenSymmetricClusterCases.Tail), ErrorCode.Validation),
            (() => new(SqlBetweenSymmetricClusterCases.Prefix + "score BETWEEN ASYMMETRIC 2 1" + SqlBetweenSymmetricClusterCases.Tail), ErrorCode.Validation),
            (() => new(SqlBetweenSymmetricClusterCases.Prefix + "score NOT BETWEEN ASYMMETRIC 2 OR 1" + SqlBetweenSymmetricClusterCases.Tail), ErrorCode.Validation),
            (() => new(SqlBetweenSymmetricClusterCases.Prefix + "score BETWEEN ASYMMETRIC ASYMMETRIC 1 AND 2" + SqlBetweenSymmetricClusterCases.Tail), ErrorCode.Validation),
            (() => new(SqlBetweenSymmetricClusterCases.Prefix + "score BETWEEN SYMMETRIC ASYMMETRIC 1 AND 2" + SqlBetweenSymmetricClusterCases.Tail), ErrorCode.Validation),
            (() => new(SqlBetweenSymmetricClusterCases.Prefix + "score BETWEEN SYMMETRIC (2) AND 1" + SqlBetweenSymmetricClusterCases.Tail), ErrorCode.UnsupportedCapability),
            (() => new(SqlBetweenSymmetricClusterCases.Prefix + "score BETWEEN ABS(2) AND 1" + SqlBetweenSymmetricClusterCases.Tail), ErrorCode.UnsupportedCapability)
        })
        {
            await RejectAsync(fixture, state, command, code, token, healthy: true, dispatched: false);
        }
        await global::TUnit.Assertions.Assert.That(await SqlBetweenSymmetricClusterImage.ReadAsync(root, state, token)).IsEqualTo(before);
        await SqlBetweenSymmetricClusterPages.HealthyAsync(fixture, state, token);
    }
}
