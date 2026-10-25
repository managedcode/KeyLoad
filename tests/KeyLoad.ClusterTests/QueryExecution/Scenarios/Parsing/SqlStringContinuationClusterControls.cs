using Npgsql;
namespace KeyLoad.ClusterTests.QueryExecution;

internal static class SqlStringContinuationClusterControls
{
    private const string SameLine = "label = 'lo' 'w'";
    private const string BlockSeparator = "label = 'lo' /*\n*/ 'w'";
    private const string PrefixedNext = "label = 'lo'\nE'w'";
    private const string OpenContinuation = "label = 'lo'\n'unterminated";
    private const string InvalidBytes = "label = E'\\xc3'\n'plain'";

    internal static async Task InvalidAsync(ClusterFixture fixture, SqlNullDistinctClusterState state,
        global::KeyLoad.ClusterTests.OrleansExecution.RequestCqrsClusterCallers root,
        CancellationToken token)
    {
        var before = await SqlNullDistinctClusterImage.ReadAsync(root, state, token);
        await global::KeyLoad.ClusterTests.OrleansExecution.RequestCqrsClusterCallers.RunOwnedAsync(
            fixture.App, global::KeyLoad.ClusterTests.ClientApi.McpCallerProtocol.Node2,
            state.Seed.Reader.Secret, async caller =>
        {
            var failures = new List<Exception>();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await using var wire = await PostgresClusterCaller.ConnectAsync(fixture.App,
                    global::KeyLoad.ClusterTests.ClientApi.McpCallerProtocol.Node3,
                    state.Seed.Reader.Principal.Id, state.Seed.Reader.Secret,
                    token);
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    await RefuseThenHealthyAsync(caller, wire, root, state, token);
                }, failures);
            }, failures);
            global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
            await global::TUnit.Assertions.Assert.That(await SqlNullDistinctClusterImage.ReadAsync(root, state, token)).IsEqualTo(before);
            return true;
        }, token);
    }

    private static async Task RefuseThenHealthyAsync(
        global::KeyLoad.ClusterTests.OrleansExecution.RequestCqrsClusterCallers caller,
        PostgresClusterCaller wire,
        global::KeyLoad.ClusterTests.OrleansExecution.RequestCqrsClusterCallers root,
        SqlNullDistinctClusterState state, CancellationToken token)
    {
        var before = await SqlNullDistinctClusterImage.ReadAsync(root, state, token);
        foreach (var (command, code) in Cases())
        {
            var sql = PostgresClusterCommands.Sql(command);
            var request = SqlNullOrderingClusterProtocol.Operation(sql);
            var result = await caller.Sdk.ExecuteSqlAsync(request, token);
            await global::TUnit.Assertions.Assert.That(result.IsSuccess).IsFalse();
            await global::TUnit.Assertions.Assert.That(result.Value.ValueKind)
                .IsEqualTo(global::System.Text.Json.JsonValueKind.Undefined);
            await global::TUnit.Assertions.Assert.That(result.Problem?.ErrorCode).IsEqualTo(code.ToString());
            _ = (await global::KeyLoad.ClusterTests.ClientApi.McpCallerAssertions.ErrorAsync(
                await caller.Mcp.CallAsync(SqlOperationProtocol.ToolName, request, token), code, dispatched: false)).Value;
            await PostgresClusterAssertions.ErrorAsync(wire.ExecuteAsync<QueryPage>(command, [], prepared: true, token),
                code == ErrorCode.Validation ? SqlNullOrderingClusterProtocol.ValidationSqlState
                    : PostgresClusterProtocol.UnsupportedSqlState, code);
            await global::TUnit.Assertions.Assert.That(await SqlNullDistinctClusterImage.ReadAsync(root, state, token)).IsEqualTo(before);
            var item = SqlNullDistinctClusterCases.All.Single(item => item.Clause == SqlNullDistinctClusterCases.ContinuationClause);
            await SqlNullDistinctClusterPages.CaseAsync(caller, wire, item, state.LowRevision, token);
            await global::TUnit.Assertions.Assert.That(await SqlNullDistinctClusterImage.ReadAsync(root, state, token)).IsEqualTo(before);
        }
    }

    private static IEnumerable<(Func<NpgsqlCommand> Command, ErrorCode Code)> Cases()
    {
        yield return (() => new(SqlNullDistinctClusterCases.Prefix + SameLine + SqlNullDistinctClusterCases.Tail), ErrorCode.Validation);
        yield return (() => new(SqlNullDistinctClusterCases.Prefix + BlockSeparator + SqlNullDistinctClusterCases.Tail), ErrorCode.Validation);
        yield return (() => new(SqlNullDistinctClusterCases.Prefix + PrefixedNext + SqlNullDistinctClusterCases.Tail), ErrorCode.Validation);
        yield return (() => new(SqlNullDistinctClusterCases.Prefix + OpenContinuation + SqlNullDistinctClusterCases.Tail), ErrorCode.Validation);
        yield return (() => new(SqlNullDistinctClusterCases.Prefix + InvalidBytes + SqlNullDistinctClusterCases.Tail), ErrorCode.Validation);
    }
}
