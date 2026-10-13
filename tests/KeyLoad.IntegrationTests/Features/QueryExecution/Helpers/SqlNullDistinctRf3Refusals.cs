namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlNullDistinctRf3Refusals
{
    internal static async Task RejectAsync(ClusterFixture fixture, SqlNullDistinctRf3State state, string clause,
        ErrorCode code, CancellationToken token, bool ast = false, bool healthy = false,
        string? sqlOverride = null, SqlNullDistinctRf3Case? astCase = null)
    {
        await global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerProtocol.Node2, state.Seed.Reader.Secret, async caller =>
        {
            var sql = sqlOverride ?? SqlNullDistinctRf3Cases.Prefix + clause + SqlNullDistinctRf3Cases.Tail;
            var request = SqlNullOrderingRf3Protocol.Operation(sql);
            var result = await caller.Sdk.ExecuteSqlAsync(request, token);
            await global::TUnit.Assertions.Assert.That(result.IsSuccess).IsFalse();
            await global::TUnit.Assertions.Assert.That(result.Value.ValueKind).IsEqualTo(global::System.Text.Json.JsonValueKind.Undefined);
            await global::TUnit.Assertions.Assert.That(result.Problem?.ErrorCode).IsEqualTo(code.ToString());
            await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.ErrorAsync(await caller.Mcp.CallAsync(SqlOperationProtocol.ToolName, request, token), code, dispatched: true);
            if (ast)
            {
                var operation = SqlNullDistinctRf3Cases.Ast(PostgresRf3State.Partition,
                    astCase ?? SqlNullDistinctRf3Cases.All[SqlNullDistinctRf3State.AgainstOne]);
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
                        await SqlNullDistinctRf3Pages.CaseAsync(caller, wire,
                            SqlNullDistinctRf3Cases.All[SqlNullDistinctRf3State.First], state.LowRevision, token);
                        foreach (var item in SqlNullDistinctRf3Cases.All.Where(item => item.Clause == SqlNullDistinctRf3Cases.NullClause
                            || item.Clause == SqlNullDistinctRf3Cases.NotNullClause
                            || item.Clause == SqlNullDistinctRf3Cases.PositiveClause
                            || item.Clause == SqlNullDistinctRf3Cases.LeadingClause))
                        { await SqlNullDistinctRf3Pages.CaseAsync(caller, wire, item, state.LowRevision, token); }
                    }
                }, failures);
            }, failures);
            global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
            return true;
        }, token);
    }

    internal static async Task InvalidAsync(ClusterFixture fixture, SqlNullDistinctRf3State state,
        global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsRf3Callers root, CancellationToken token)
    {
        var before = await SqlNullDistinctRf3Image.ReadAsync(root, state, token);
        foreach (var clause in new[] { "score IS DISTINCT NULL", "score IS DISTINCT FROM",
            "score IS 'DISTINCT' FROM NULL", "score IS NOT DISTINCT FROM @absent", "score IS ISNULL",
            "score IS NOT NOTNULL", "score 'ISNULL'", "score NOT ISNULL", "score = ++1", "score = +",
            "score = +true", "score = +@absent", "score = + 1", "score = +79228162514264337593543950336",
            "score = .e1", "score = ..5", "score = .5e+", "score = .5.0", "score = +.x", "score = -.x" })
        {
            await RejectAsync(fixture, state, clause, ErrorCode.Validation, token, healthy: true);
            await global::TUnit.Assertions.Assert.That(await SqlNullDistinctRf3Image.ReadAsync(root, state, token)).IsEqualTo(before);
        }
        await global::TUnit.Assertions.Assert.That(await SqlNullDistinctRf3Image.ReadAsync(root, state, token)).IsEqualTo(before);
        foreach (var window in new[] { "LIMIT +1", "OFFSET +1 LIMIT 1", "FETCH FIRST +1 ROW ONLY",
            "LIMIT .5", "OFFSET .5 LIMIT 1", "FETCH FIRST .5 ROW ONLY" })
        {
            var sql = SqlNullDistinctRf3Cases.Prefix + SqlNullDistinctRf3Cases.PositiveClause
                + " ORDER BY label ASC " + window;
            await RejectAsync(fixture, state, SqlNullDistinctRf3Cases.PositiveClause, ErrorCode.Validation, token,
                healthy: true, sqlOverride: sql);
            await global::TUnit.Assertions.Assert.That(await SqlNullDistinctRf3Image.ReadAsync(root, state, token)).IsEqualTo(before);
        }
        await SqlNullDistinctRf3Pages.HealthyAsync(fixture, state, token);
    }
}
