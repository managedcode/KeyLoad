using Npgsql;
namespace KeyLoad.ClusterTests.QueryExecution;

internal static class SqlNullDistinctClusterRefusals
{
    internal static async Task RejectAsync(ClusterFixture fixture, SqlNullDistinctClusterState state, Func<NpgsqlCommand> command,
        ErrorCode code, CancellationToken token, bool ast = false, bool healthy = false,
        SqlNullDistinctClusterCase? astCase = null, bool dispatched = true)
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
                var operation = SqlNullDistinctClusterCases.Ast(PostgresClusterState.Partition,
                    astCase ?? SqlNullDistinctClusterCases.All[SqlNullDistinctClusterState.AgainstOne]);
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
        global::KeyLoad.ClusterTests.OrleansExecution.RequestCqrsClusterCallers root, CancellationToken token)
    {
        var before = await SqlNullDistinctClusterImage.ReadAsync(root, state, token);
        await SqlStringContinuationClusterControls.InvalidAsync(fixture, state, root, token);
        await SqlDollarStringClusterControls.InvalidAsync(fixture, state, root, token);
        await SqlRadixIntegerClusterControls.InvalidAsync(fixture, state, root, token);
        await SqlUnicodeStringClusterControls.InvalidAsync(fixture, state, root, token);
        await SqlUnicodeIdentifierClusterControls.InvalidAsync(fixture, state, root, token);
        await SqlNumericSeparatorClusterControls.InvalidAsync(fixture, state, root, token);
        foreach (var command in new Func<NpgsqlCommand>[]
        {
            () => new(SqlNullDistinctClusterCases.Prefix + "score IS DISTINCT NULL" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "score IS DISTINCT FROM" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "score IS 'DISTINCT' FROM NULL" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "score IS ISNULL" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "score IS NOT NOTNULL" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "score 'ISNULL'" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "score NOT ISNULL" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "score = +*1" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "score = +" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "score = +)" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "score = +(@absent" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "score = + 1.0.0" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "score = +79228162514264337593543950336" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "score = .e1" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "score = ..5" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "score = .5e+" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "score = .5.0" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "score = +.x" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "score = -.x" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "score = 0xZ" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "score = 0xG" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "score = 79228162514264337593543950336" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "score = -79228162514264337593543950336" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "label = E'\\u123'" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "label = E'\\U00110000'" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "label = E'\\uD800'" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "label = E'\\uDC00'" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "label = E'\\000'" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "label = E'\\x00'" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "label = E'\\xff'" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "label = E'\\xc3'" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "label = E'unterminated" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "label = E'\\'" + SqlNullDistinctClusterCases.Tail)
        })
        {
            await RejectAsync(fixture, state, command, ErrorCode.Validation, token, healthy: true, dispatched: false);
            await global::TUnit.Assertions.Assert.That(await SqlNullDistinctClusterImage.ReadAsync(root, state, token)).IsEqualTo(before);
        }
        await global::TUnit.Assertions.Assert.That(await SqlNullDistinctClusterImage.ReadAsync(root, state, token)).IsEqualTo(before);
        await RejectAsync(fixture, state,
            () => new(SqlNullDistinctClusterCases.Prefix + "score IS NOT DISTINCT FROM @absent" + SqlNullDistinctClusterCases.Tail),
            ErrorCode.Validation, token, healthy: true, dispatched: true);
        await global::TUnit.Assertions.Assert.That(await SqlNullDistinctClusterImage.ReadAsync(root, state, token)).IsEqualTo(before);
        await InvalidWindowsAsync(fixture, state, root, before, token);
        await SqlNullDistinctClusterPages.HealthyAsync(fixture, state, token);
    }

    private static async Task InvalidWindowsAsync(ClusterFixture fixture, SqlNullDistinctClusterState state,
        global::KeyLoad.ClusterTests.OrleansExecution.RequestCqrsClusterCallers root, string before, CancellationToken token)
    {
        foreach (var command in new Func<NpgsqlCommand>[]
        {
            () => new(SqlNullDistinctClusterCases.WindowPrefix + SqlNullDistinctClusterCases.PositiveClause
                + " ORDER BY label ASC " + "OFFSET -1 ROWS FETCH NEXT 1 ROWS ONLY"),
            () => new(SqlNullDistinctClusterCases.WindowPrefix + SqlNullDistinctClusterCases.PositiveClause
                + " ORDER BY label ASC " + "OFFSET 0 ROWS FETCH NEXT +0 ROWS ONLY"),
            () => new(SqlNullDistinctClusterCases.WindowPrefix + SqlNullDistinctClusterCases.PositiveClause
                + " ORDER BY label ASC " + "OFFSET 0 ROWS FETCH NEXT -1 ROWS ONLY"),
            () => new(SqlNullDistinctClusterCases.WindowPrefix + SqlNullDistinctClusterCases.PositiveClause
                + " ORDER BY label ASC " + "OFFSET .5 ROWS FETCH NEXT 1 ROWS ONLY"),
            () => new(SqlNullDistinctClusterCases.WindowPrefix + SqlNullDistinctClusterCases.PositiveClause
                + " ORDER BY label ASC " + "OFFSET 0 ROWS FETCH NEXT .5 ROWS ONLY"),
            () => new(SqlNullDistinctClusterCases.WindowPrefix + SqlNullDistinctClusterCases.PositiveClause
                + " ORDER BY label ASC " + "OFFSET 0 ROWS FETCH NEXT 1e0 ROWS ONLY"),
            () => new(SqlNullDistinctClusterCases.WindowPrefix + SqlNullDistinctClusterCases.PositiveClause
                + " ORDER BY label ASC " + "OFFSET 0x1 ROWS FETCH NEXT 1 ROWS ONLY"),
            () => new(SqlNullDistinctClusterCases.WindowPrefix + SqlNullDistinctClusterCases.PositiveClause
                + " ORDER BY label ASC " + "OFFSET 0 ROWS FETCH NEXT 0x1 ROWS ONLY"),
            () => new(SqlNullDistinctClusterCases.WindowPrefix + SqlNullDistinctClusterCases.PositiveClause
                + " ORDER BY label ASC " + "OFFSET 0 ROWS FETCH NEXT 0x0 ROWS ONLY")
        })
        {
            await RejectAsync(fixture, state, command, ErrorCode.Validation, token, healthy: true, dispatched: false);
            await global::TUnit.Assertions.Assert.That(await SqlNullDistinctClusterImage.ReadAsync(root, state, token)).IsEqualTo(before);
        }
        foreach (var command in new Func<NpgsqlCommand>[]
        {
            () => new(SqlNullDistinctClusterCases.Prefix + "score COLLATE Latin1_General_BIN = 1" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "CAST(score AS int) = 1" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "ABS(score) = 1" + SqlNullDistinctClusterCases.Tail),
            () => new(SqlNullDistinctClusterCases.Prefix + "score + 1 = 2" + SqlNullDistinctClusterCases.Tail)
        })
        {
            await RejectAsync(fixture, state, command, ErrorCode.UnsupportedCapability, token, healthy: true, dispatched: false);
            await global::TUnit.Assertions.Assert.That(await SqlNullDistinctClusterImage.ReadAsync(root, state, token)).IsEqualTo(before);
        }
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
