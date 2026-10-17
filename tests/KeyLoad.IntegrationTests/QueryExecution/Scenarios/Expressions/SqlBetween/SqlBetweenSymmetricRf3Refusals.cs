namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlBetweenSymmetricRf3Refusals
{
    internal static async Task RejectAsync(ClusterFixture fixture, SqlBetweenSymmetricRf3State state, string clause,
        ErrorCode code, CancellationToken token, bool ast = false, bool healthy = false)
    {
        await global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerProtocol.Node2, state.Seed.Reader.Secret, async caller =>
        {
            var sql = SqlBetweenSymmetricRf3Cases.Prefix + clause + SqlBetweenSymmetricRf3Cases.Tail;
            var request = SqlNullOrderingRf3Protocol.Operation(sql);
            var result = await caller.Sdk.ExecuteSqlAsync(request, token);
            await global::TUnit.Assertions.Assert.That(result.IsSuccess).IsFalse();
            await global::TUnit.Assertions.Assert.That(result.Value.ValueKind).IsEqualTo(global::System.Text.Json.JsonValueKind.Undefined);
            await global::TUnit.Assertions.Assert.That(result.Problem?.ErrorCode).IsEqualTo(code.ToString());
            await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.ErrorAsync(await caller.Mcp.CallAsync(SqlOperationProtocol.ToolName, request, token), code, dispatched: true);
            if (ast)
            {
                var operation = SqlBetweenSymmetricRf3Cases.Ast(PostgresRf3State.Partition,
                    SqlBetweenSymmetricRf3Cases.All[SqlBetweenSymmetricRf3State.AgainstOne]);
                var typed = await caller.Sdk.QueryAstAsync(operation, token);
                await global::TUnit.Assertions.Assert.That(typed.IsSuccess).IsFalse();
                await global::TUnit.Assertions.Assert.That(typed.Value).IsNull();
                await global::TUnit.Assertions.Assert.That(typed.Problem?.ErrorCode).IsEqualTo(code.ToString());
                await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.ErrorAsync(await caller.Mcp.CallAsync(global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerTools.QueryAst, operation, token), code, dispatched: true);
            }
            var failures = new List<Exception>();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await using var wire = await PostgresRf3Caller.ConnectAsync(fixture.App, global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerProtocol.Node3,
                    state.Seed.Reader.Principal.Id, state.Seed.Reader.Secret, PostgresRf3Certificate.RootPath(fixture), token);
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    await PostgresRf3Assertions.ErrorAsync(wire.ExecuteAsync<QueryPage>(sql, [], prepared: true, token),
                        code == ErrorCode.Validation ? SqlNullOrderingRf3Protocol.ValidationSqlState : PostgresRf3Protocol.PermissionSqlState, code);
                    if (healthy)
                    {
                        await SqlBetweenSymmetricRf3Pages.CaseAsync(caller, wire,
                            SqlBetweenSymmetricRf3Cases.All.Single(value => value.Clause == SqlBetweenSymmetricRf3Cases.AsymmetricHealthy),
                            state.LowRevision, token);
                    }
                }, failures);
            }, failures);
            global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
            return true;
        }, token);
    }

    internal static async Task InvalidAsync(ClusterFixture fixture, SqlBetweenSymmetricRf3State state,
        global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsRf3Callers root, CancellationToken token)
    {
        var before = await SqlBetweenSymmetricRf3Image.ReadAsync(root, state, token);
        foreach (var clause in new[] { "score BETWEEN SYMMETRIC 2 1", "score BETWEEN SYMMETRIC 2 OR 1",
            "score BETWEEN SYMMETRIC (2) AND 1", "score NOT BETWEEN SYMMETRIC 2 1",
            "score BETWEEN ASYMMETRIC 2 1", "score NOT BETWEEN ASYMMETRIC 2 OR 1",
            "score BETWEEN ASYMMETRIC ASYMMETRIC 1 AND 2",
            "score BETWEEN SYMMETRIC ASYMMETRIC 1 AND 2" })
        {
            await RejectAsync(fixture, state, clause, ErrorCode.Validation, token, healthy: true);
        }
        await global::TUnit.Assertions.Assert.That(await SqlBetweenSymmetricRf3Image.ReadAsync(root, state, token)).IsEqualTo(before);
        await SqlBetweenSymmetricRf3Pages.HealthyAsync(fixture, state, token);
    }
}
