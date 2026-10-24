namespace KeyLoad.IntegrationTests.QueryExecution;

internal static class SqlStringContinuationClusterControls
{
    private const string SameLine = "label = 'lo' 'w'";
    private const string BlockSeparator = "label = 'lo' /*\n*/ 'w'";
    private const string PrefixedNext = "label = 'lo'\nE'w'";
    private const string OpenContinuation = "label = 'lo'\n'unterminated";
    private const string InvalidBytes = "label = E'\\xc3'\n'plain'";

    internal static async Task InvalidAsync(ClusterFixture fixture, SqlNullDistinctClusterState state,
        global::KeyLoad.IntegrationTests.OrleansExecution.RequestCqrsClusterCallers root,
        CancellationToken token)
    {
        var before = await SqlNullDistinctClusterImage.ReadAsync(root, state, token);
        await global::KeyLoad.IntegrationTests.OrleansExecution.RequestCqrsClusterCallers.RunOwnedAsync(
            fixture.App, global::KeyLoad.IntegrationTests.ClientApi.McpCallerProtocol.Node2,
            state.Seed.Reader.Secret, async caller =>
        {
            var failures = new List<Exception>();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await using var wire = await PostgresClusterCaller.ConnectAsync(fixture.App,
                    global::KeyLoad.IntegrationTests.ClientApi.McpCallerProtocol.Node3,
                    state.Seed.Reader.Principal.Id, state.Seed.Reader.Secret,
                    PostgresClusterCertificate.RootPath(fixture), token);
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
        global::KeyLoad.IntegrationTests.OrleansExecution.RequestCqrsClusterCallers caller,
        PostgresClusterCaller wire,
        global::KeyLoad.IntegrationTests.OrleansExecution.RequestCqrsClusterCallers root,
        SqlNullDistinctClusterState state, CancellationToken token)
    {
        var before = await SqlNullDistinctClusterImage.ReadAsync(root, state, token);
        foreach (var (clause, code) in Cases())
        {
            var sql = SqlNullDistinctClusterCases.Prefix + clause + SqlNullDistinctClusterCases.Tail;
            var request = SqlNullOrderingClusterProtocol.Operation(sql);
            var result = await caller.Sdk.ExecuteSqlAsync(request, token);
            await global::TUnit.Assertions.Assert.That(result.IsSuccess).IsFalse();
            await global::TUnit.Assertions.Assert.That(result.Value.ValueKind)
                .IsEqualTo(global::System.Text.Json.JsonValueKind.Undefined);
            await global::TUnit.Assertions.Assert.That(result.Problem?.ErrorCode).IsEqualTo(code.ToString());
            await global::KeyLoad.IntegrationTests.ClientApi.McpCallerAssertions.ErrorAsync(
                await caller.Mcp.CallAsync(SqlOperationProtocol.ToolName, request, token), code, dispatched: true);
            await PostgresClusterAssertions.ErrorAsync(wire.ExecuteAsync<QueryPage>(sql, [], prepared: true, token),
                code == ErrorCode.Validation ? SqlNullOrderingClusterProtocol.ValidationSqlState
                    : PostgresClusterProtocol.UnsupportedSqlState, code);
            await global::TUnit.Assertions.Assert.That(await SqlNullDistinctClusterImage.ReadAsync(root, state, token)).IsEqualTo(before);
            var item = SqlNullDistinctClusterCases.All.Single(item => item.Clause == SqlNullDistinctClusterCases.ContinuationClause);
            await SqlNullDistinctClusterPages.CaseAsync(caller, wire, item, state.LowRevision, token);
            await global::TUnit.Assertions.Assert.That(await SqlNullDistinctClusterImage.ReadAsync(root, state, token)).IsEqualTo(before);
        }
    }

    private static IEnumerable<(string Clause, ErrorCode Code)> Cases()
    {
        yield return (SameLine, ErrorCode.UnsupportedCapability);
        yield return (BlockSeparator, ErrorCode.UnsupportedCapability);
        yield return (PrefixedNext, ErrorCode.UnsupportedCapability);
        yield return (OpenContinuation, ErrorCode.Validation);
        yield return (InvalidBytes, ErrorCode.Validation);
    }
}
