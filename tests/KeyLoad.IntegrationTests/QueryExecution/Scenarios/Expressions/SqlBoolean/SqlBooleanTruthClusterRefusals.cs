namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlBooleanTruthClusterRefusals
{
    internal static async Task RejectAsync(ClusterFixture fixture, SqlBooleanTruthClusterState state, string clause,
        ErrorCode code, CancellationToken token, bool ast = false, bool healthy = false,
        SqlBooleanTruthClusterCase? astCase = null)
    {
        await global::KeyLoad.IntegrationTests.Features.OrleansExecution.RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerProtocol.Node2, state.Seed.Reader.Secret, async caller =>
        {
            var sql = SqlBooleanTruthClusterCases.Prefix + clause + SqlBooleanTruthClusterCases.Tail;
            var request = SqlNullOrderingClusterProtocol.Operation(sql);
            var result = await caller.Sdk.ExecuteSqlAsync(request, token);
            await global::TUnit.Assertions.Assert.That(result.IsSuccess).IsFalse();
            await global::TUnit.Assertions.Assert.That(result.Value.ValueKind).IsEqualTo(global::System.Text.Json.JsonValueKind.Undefined);
            await global::TUnit.Assertions.Assert.That(result.Problem?.ErrorCode).IsEqualTo(code.ToString());
            await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.ErrorAsync(await caller.Mcp.CallAsync(SqlOperationProtocol.ToolName, request, token), code, dispatched: true);
            if (ast)
            {
                var operation = SqlBooleanTruthClusterCases.Ast(PostgresClusterState.Partition,
                    astCase ?? SqlBooleanTruthClusterCases.All[SqlBooleanTruthClusterState.AgainstOne]);
                var typed = await caller.Sdk.QueryAstAsync(operation, token);
                await global::TUnit.Assertions.Assert.That(typed.IsSuccess).IsFalse();
                await global::TUnit.Assertions.Assert.That(typed.Value).IsNull();
                await global::TUnit.Assertions.Assert.That(typed.Problem?.ErrorCode).IsEqualTo(code.ToString());
                await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.ErrorAsync(await caller.Mcp.CallAsync(global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerTools.QueryAst, operation, token), code, dispatched: true);
            }
            var failures = new List<Exception>();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await using var wire = await PostgresClusterCaller.ConnectAsync(fixture.App, global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerProtocol.Node3,
                    state.Seed.Reader.Principal.Id, state.Seed.Reader.Secret, PostgresClusterCertificate.RootPath(fixture), token);
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    await PostgresClusterAssertions.ErrorAsync(wire.ExecuteAsync<QueryPage>(sql, [], prepared: true, token),
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
        global::KeyLoad.IntegrationTests.Features.OrleansExecution.RequestCqrsClusterCallers root, CancellationToken token)
    {
        var before = await SqlBooleanTruthClusterImage.ReadAsync(root, state, token);
        await SqlBooleanOperandClusterControls.InvalidAsync(fixture, state, root, token);
        foreach (var clause in new[] { "score IS", "score IS NOT",
            "score IS 'TRUE'", "score IS @absent" })
        {
            await RejectAsync(fixture, state, clause, ErrorCode.Validation, token);
        }
        await global::TUnit.Assertions.Assert.That(await SqlBooleanTruthClusterImage.ReadAsync(root, state, token)).IsEqualTo(before);
        await SqlBooleanTruthClusterPages.HealthyAsync(fixture, state, token);
    }
}
