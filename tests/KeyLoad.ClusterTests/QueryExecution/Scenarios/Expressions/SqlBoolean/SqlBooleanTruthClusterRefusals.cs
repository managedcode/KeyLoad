using Npgsql;
namespace KeyLoad.ClusterTests.QueryExecution;

internal static class SqlBooleanTruthClusterRefusals
{
    internal static async Task RejectAsync(ClusterFixture fixture, SqlBooleanTruthClusterState state, Func<NpgsqlCommand> command,
        ErrorCode code, CancellationToken token, bool ast = false, bool healthy = false,
        SqlBooleanTruthClusterCase? astCase = null, bool dispatched = true)
    {
        await global::KeyLoad.ClusterTests.OrleansExecution.RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, global::KeyLoad.ClusterTests.ClientApi.McpCallerProtocol.Node2, state.Seed.Reader.Secret, async caller =>
        {
            var sql = PostgresClusterCommands.Sql(command);
            var request = SqlNullOrderingClusterProtocol.Operation(sql) with { Parameters = SqlBooleanTruthClusterCases.Parameters() };
            var result = await caller.Sdk.ExecuteSqlAsync(request, token);
            await global::TUnit.Assertions.Assert.That(result.IsSuccess).IsFalse();
            await global::TUnit.Assertions.Assert.That(result.Value.ValueKind).IsEqualTo(global::System.Text.Json.JsonValueKind.Undefined);
            await global::TUnit.Assertions.Assert.That(result.Problem?.ErrorCode).IsEqualTo(code.ToString());
            _ = (await global::KeyLoad.ClusterTests.ClientApi.McpCallerAssertions.ErrorAsync(await caller.Mcp.CallAsync(SqlOperationProtocol.ToolName, request, token), code, dispatched)).Value;
            if (ast)
            {
                var operation = SqlBooleanTruthClusterCases.Ast(PostgresClusterState.Partition,
                    astCase ?? SqlBooleanTruthClusterCases.All[SqlBooleanTruthClusterState.AgainstOne]);
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
                    await PostgresClusterAssertions.ErrorAsync(wire.ExecuteAsync<QueryPage>(command, SqlBooleanTruthClusterCases.WireParameters(), prepared: true, token),
                        code == ErrorCode.Validation ? SqlNullOrderingClusterProtocol.ValidationSqlState : PostgresClusterProtocol.PermissionSqlState, code);
                    if (healthy)
                    {
                        foreach (var item in SqlBooleanTruthClusterCases.All)
                        {
                            await SqlBooleanTruthClusterPages.CaseAsync(caller, wire, item, state.LowRevision, token);
                        }
                    }
                }, failures);
            }, failures);
            global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
            return true;
        }, token);
    }

    internal static async Task InvalidAsync(ClusterFixture fixture, SqlBooleanTruthClusterState state,
        global::KeyLoad.ClusterTests.OrleansExecution.RequestCqrsClusterCallers root, CancellationToken token)
    {
        var before = await SqlBooleanTruthClusterImage.ReadAsync(root, state, token);
        await SqlBooleanOperandClusterControls.InvalidAsync(fixture, state, root, token);
        foreach (var command in new Func<NpgsqlCommand>[]
        {
            () => new(SqlBooleanTruthClusterCases.Prefix + "score IS" + SqlBooleanTruthClusterCases.Tail),
            () => new(SqlBooleanTruthClusterCases.Prefix + "score IS NOT" + SqlBooleanTruthClusterCases.Tail),
            () => new(SqlBooleanTruthClusterCases.Prefix + "score IS 'TRUE'" + SqlBooleanTruthClusterCases.Tail),
            () => new(SqlBooleanTruthClusterCases.Prefix + "score IS @absent" + SqlBooleanTruthClusterCases.Tail)
        })
        {
            await RejectAsync(fixture, state, command, ErrorCode.Validation, token, dispatched: false);
        }
        await global::TUnit.Assertions.Assert.That(await SqlBooleanTruthClusterImage.ReadAsync(root, state, token)).IsEqualTo(before);
        await SqlBooleanTruthClusterPages.HealthyAsync(fixture, state, token);
    }
}
