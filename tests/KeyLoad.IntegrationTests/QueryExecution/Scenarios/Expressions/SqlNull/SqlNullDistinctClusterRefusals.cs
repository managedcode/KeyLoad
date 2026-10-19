namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlNullDistinctClusterRefusals
{
    internal static async Task RejectAsync(ClusterFixture fixture, SqlNullDistinctClusterState state, string clause,
        ErrorCode code, CancellationToken token, bool ast = false, bool healthy = false,
        string? sqlOverride = null, SqlNullDistinctClusterCase? astCase = null)
    {
        await global::KeyLoad.IntegrationTests.Features.OrleansExecution.RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerProtocol.Node2, state.Seed.Reader.Secret, async caller =>
        {
            var sql = sqlOverride ?? SqlNullDistinctClusterCases.Prefix + clause + SqlNullDistinctClusterCases.Tail;
            var request = SqlNullOrderingClusterProtocol.Operation(sql);
            var result = await caller.Sdk.ExecuteSqlAsync(request, token);
            await global::TUnit.Assertions.Assert.That(result.IsSuccess).IsFalse();
            await global::TUnit.Assertions.Assert.That(result.Value.ValueKind).IsEqualTo(global::System.Text.Json.JsonValueKind.Undefined);
            await global::TUnit.Assertions.Assert.That(result.Problem?.ErrorCode).IsEqualTo(code.ToString());
            await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.ErrorAsync(await caller.Mcp.CallAsync(SqlOperationProtocol.ToolName, request, token), code, dispatched: true);
            if (ast)
            {
                var operation = SqlNullDistinctClusterCases.Ast(PostgresClusterState.Partition,
                    astCase ?? SqlNullDistinctClusterCases.All[SqlNullDistinctClusterState.AgainstOne]);
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
                        code switch
                        {
                            ErrorCode.Validation => SqlNullOrderingClusterProtocol.ValidationSqlState,
                            ErrorCode.UnsupportedCapability => PostgresClusterProtocol.UnsupportedSqlState,
                            _ => PostgresClusterProtocol.PermissionSqlState
                        }, code);
                    if (healthy)
                    {
                        await SqlNullDistinctClusterPages.CaseAsync(caller, wire,
                            SqlNullDistinctClusterCases.All[SqlNullDistinctClusterState.First], state.LowRevision, token);
                        foreach (var item in SqlNullDistinctClusterCases.All.Where(IsHealthyClause))
                        {
                            await SqlNullDistinctClusterPages.CaseAsync(caller, wire, item, state.LowRevision, token);
                        }
                    }
                }, failures);
            }, failures);
            global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
            return true;
        }, token);
    }

    internal static async Task InvalidAsync(ClusterFixture fixture, SqlNullDistinctClusterState state,
        global::KeyLoad.IntegrationTests.Features.OrleansExecution.RequestCqrsClusterCallers root, CancellationToken token)
    {
        var before = await SqlNullDistinctClusterImage.ReadAsync(root, state, token);
        await SqlStringContinuationClusterControls.InvalidAsync(fixture, state, root, token);
        await SqlDollarStringClusterControls.InvalidAsync(fixture, state, root, token);
        await SqlRadixIntegerClusterControls.InvalidAsync(fixture, state, root, token);
        await SqlUnicodeStringClusterControls.InvalidAsync(fixture, state, root, token);
        await SqlUnicodeIdentifierClusterControls.InvalidAsync(fixture, state, root, token);
        await SqlNumericSeparatorClusterControls.InvalidAsync(fixture, state, root, token);
        foreach (var clause in new[] { "score IS DISTINCT NULL", "score IS DISTINCT FROM",
            "score IS 'DISTINCT' FROM NULL", "score IS NOT DISTINCT FROM @absent", "score IS ISNULL",
            "score IS NOT NOTNULL", "score 'ISNULL'", "score NOT ISNULL", "score = ++1", "score = +",
            "score = +true", "score = +@absent", "score = + 1", "score = +79228162514264337593543950336",
            "score = .e1", "score = ..5", "score = .5e+", "score = .5.0", "score = +.x", "score = -.x",
            "score = 0x", "score = 0xG", "score = 0x1000000000000000000000000", "score = -0x1000000000000000000000000",
            "label = E'\\u123'", "label = E'\\U00110000'", "label = E'\\uD800'",
            "label = E'\\uDC00'", "label = E'\\000'", "label = E'\\x00'", "label = E'\\xff'",
            "label = E'\\xc3'", "label = E'unterminated", "label = E'\\'" })
        {
            await RejectAsync(fixture, state, clause, ErrorCode.Validation, token, healthy: true);
            await global::TUnit.Assertions.Assert.That(await SqlNullDistinctClusterImage.ReadAsync(root, state, token)).IsEqualTo(before);
        }
        await global::TUnit.Assertions.Assert.That(await SqlNullDistinctClusterImage.ReadAsync(root, state, token)).IsEqualTo(before);
        foreach (var window in new[] { "LIMIT +1", "OFFSET +1 LIMIT 1", "FETCH FIRST +1 ROW ONLY",
            "LIMIT .5", "OFFSET .5 LIMIT 1", "FETCH FIRST .5 ROW ONLY",
            "LIMIT 0x1", "OFFSET 0x1 LIMIT 1", "FETCH FIRST 0x1 ROW ONLY" })
        {
            var sql = SqlNullDistinctClusterCases.Prefix + SqlNullDistinctClusterCases.PositiveClause
                + " ORDER BY label ASC " + window;
            await RejectAsync(fixture, state, SqlNullDistinctClusterCases.PositiveClause, ErrorCode.Validation, token,
                healthy: true, sqlOverride: sql);
            await global::TUnit.Assertions.Assert.That(await SqlNullDistinctClusterImage.ReadAsync(root, state, token)).IsEqualTo(before);
        }
        foreach (var clause in new[] { "score = 0x1.0", "score = 0x1_0.0", "score = 0b1.0", "score = 0o1.0" })
        {
            await RejectAsync(fixture, state, clause, ErrorCode.UnsupportedCapability, token, healthy: true);
            await global::TUnit.Assertions.Assert.That(await SqlNullDistinctClusterImage.ReadAsync(root, state, token)).IsEqualTo(before);
        }
        await SqlNullDistinctClusterPages.HealthyAsync(fixture, state, token);
    }

    private static bool IsHealthyClause(SqlNullDistinctClusterCase item)
        => item.Clause == SqlNullDistinctClusterCases.NullClause
            || item.Clause == SqlNullDistinctClusterCases.NotNullClause
            || item.Clause == SqlNullDistinctClusterCases.PositiveClause
            || item.Clause == SqlNullDistinctClusterCases.LeadingClause
            || item.Clause == SqlNullDistinctClusterCases.HexClause
            || item.Clause == SqlNullDistinctClusterCases.EscapeClause
            || item.Clause == SqlNullDistinctClusterCases.ContinuationClause
            || item.Clause == SqlNullDistinctClusterCases.UnicodeClause
            || item.Clause == SqlNullDistinctClusterCases.UnicodeCustomClause
            || item.Clause == SqlNullDistinctClusterCases.NumericSeparatorClause
            || item.Clause == SqlNullDistinctClusterCases.NumericRadixSeparatorClause
            || item.Clause == SqlNullDistinctClusterCases.NumericOctalSeparatorClause
            || item.Clause == SqlNullDistinctClusterCases.DollarClause
            || item.Clause == SqlNullDistinctClusterCases.BinaryClause
            || item.Clause == SqlNullDistinctClusterCases.OctalClause || SqlUnicodeIdentifierClusterControls.IsSelected(item);
}
