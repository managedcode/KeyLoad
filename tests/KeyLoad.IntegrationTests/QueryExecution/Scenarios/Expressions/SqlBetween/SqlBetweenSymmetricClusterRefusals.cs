namespace KeyLoad.IntegrationTests.QueryExecution;

internal static class SqlBetweenSymmetricClusterRefusals
{
    internal static async Task RejectAsync(ClusterFixture fixture, SqlBetweenSymmetricClusterState state, string clause,
        ErrorCode code, CancellationToken token, bool ast = false, bool healthy = false)
    {
        await global::KeyLoad.IntegrationTests.OrleansExecution.RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, global::KeyLoad.IntegrationTests.ClientApi.McpCallerProtocol.Node2, state.Seed.Reader.Secret, async caller =>
        {
            var sql = SqlBetweenSymmetricClusterCases.Prefix + clause + SqlBetweenSymmetricClusterCases.Tail;
            var request = SqlNullOrderingClusterProtocol.Operation(sql);
            var result = await caller.Sdk.ExecuteSqlAsync(request, token);
            await global::TUnit.Assertions.Assert.That(result.IsSuccess).IsFalse();
            await global::TUnit.Assertions.Assert.That(result.Value.ValueKind).IsEqualTo(global::System.Text.Json.JsonValueKind.Undefined);
            await global::TUnit.Assertions.Assert.That(result.Problem?.ErrorCode).IsEqualTo(code.ToString());
            await global::KeyLoad.IntegrationTests.ClientApi.McpCallerAssertions.ErrorAsync(await caller.Mcp.CallAsync(SqlOperationProtocol.ToolName, request, token), code, dispatched: true);
            if (ast)
            {
                var operation = SqlBetweenSymmetricClusterCases.Ast(PostgresClusterState.Partition,
                    SqlBetweenSymmetricClusterCases.All[SqlBetweenSymmetricClusterState.AgainstOne]);
                var typed = await caller.Sdk.QueryAstAsync(operation, token);
                await global::TUnit.Assertions.Assert.That(typed.IsSuccess).IsFalse();
                await global::TUnit.Assertions.Assert.That(typed.Value).IsNull();
                await global::TUnit.Assertions.Assert.That(typed.Problem?.ErrorCode).IsEqualTo(code.ToString());
                await global::KeyLoad.IntegrationTests.ClientApi.McpCallerAssertions.ErrorAsync(await caller.Mcp.CallAsync(global::KeyLoad.IntegrationTests.ClientApi.McpCallerTools.QueryAst, operation, token), code, dispatched: true);
            }
            var failures = new List<Exception>();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await using var wire = await PostgresClusterCaller.ConnectAsync(fixture.App, global::KeyLoad.IntegrationTests.ClientApi.McpCallerProtocol.Node3,
                    state.Seed.Reader.Principal.Id, state.Seed.Reader.Secret, PostgresClusterCertificate.RootPath(fixture), token);
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    await PostgresClusterAssertions.ErrorAsync(wire.ExecuteAsync<QueryPage>(sql, [], prepared: true, token),
                        code == ErrorCode.Validation ? SqlNullOrderingClusterProtocol.ValidationSqlState : PostgresClusterProtocol.PermissionSqlState, code);
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
        global::KeyLoad.IntegrationTests.OrleansExecution.RequestCqrsClusterCallers root, CancellationToken token)
    {
        var before = await SqlBetweenSymmetricClusterImage.ReadAsync(root, state, token);
        foreach (var clause in new[] { "score BETWEEN SYMMETRIC 2 1", "score BETWEEN SYMMETRIC 2 OR 1",
            "score BETWEEN SYMMETRIC (2) AND 1", "score NOT BETWEEN SYMMETRIC 2 1",
            "score BETWEEN ASYMMETRIC 2 1", "score NOT BETWEEN ASYMMETRIC 2 OR 1",
            "score BETWEEN ASYMMETRIC ASYMMETRIC 1 AND 2",
            "score BETWEEN SYMMETRIC ASYMMETRIC 1 AND 2" })
        {
            await RejectAsync(fixture, state, clause, ErrorCode.Validation, token, healthy: true);
        }
        await global::TUnit.Assertions.Assert.That(await SqlBetweenSymmetricClusterImage.ReadAsync(root, state, token)).IsEqualTo(before);
        await SqlBetweenSymmetricClusterPages.HealthyAsync(fixture, state, token);
    }
}
