namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlStringContinuationRf3Controls
{
    private const string SameLine = "label = 'lo' 'w'";
    private const string BlockSeparator = "label = 'lo' /*\n*/ 'w'";
    private const string PrefixedNext = "label = 'lo'\nE'w'";
    private const string OpenContinuation = "label = 'lo'\n'unterminated";
    private const string InvalidBytes = "label = E'\\xc3'\n'plain'";

    internal static async Task InvalidAsync(ClusterFixture fixture, SqlNullDistinctRf3State state,
        global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsRf3Callers root,
        CancellationToken token)
    {
        var before = await SqlNullDistinctRf3Image.ReadAsync(root, state, token);
        await global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsRf3Callers.RunOwnedAsync(
            fixture.App, global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerProtocol.Node2,
            state.Seed.Reader.Secret, async caller =>
        {
            var failures = new List<Exception>();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await using var wire = await PostgresRf3Caller.ConnectAsync(fixture.App,
                    global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerProtocol.Node3,
                    state.Seed.Reader.Principal.Id, state.Seed.Reader.Secret,
                    PostgresRf3Certificate.RootPath(fixture), token);
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    await RefuseThenHealthyAsync(caller, wire, root, state, token);
                }, failures);
            }, failures);
            global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
            await global::TUnit.Assertions.Assert.That(await SqlNullDistinctRf3Image.ReadAsync(root, state, token)).IsEqualTo(before);
            return true;
        }, token);
    }

    private static async Task RefuseThenHealthyAsync(
        global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsRf3Callers caller,
        PostgresRf3Caller wire,
        global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsRf3Callers root,
        SqlNullDistinctRf3State state, CancellationToken token)
    {
        var before = await SqlNullDistinctRf3Image.ReadAsync(root, state, token);
        foreach (var (clause, code) in Cases())
        {
            var sql = SqlNullDistinctRf3Cases.Prefix + clause + SqlNullDistinctRf3Cases.Tail;
            var request = SqlNullOrderingRf3Protocol.Operation(sql);
            var result = await caller.Sdk.ExecuteSqlAsync(request, token);
            await global::TUnit.Assertions.Assert.That(result.IsSuccess).IsFalse();
            await global::TUnit.Assertions.Assert.That(result.Value.ValueKind)
                .IsEqualTo(global::System.Text.Json.JsonValueKind.Undefined);
            await global::TUnit.Assertions.Assert.That(result.Problem?.ErrorCode).IsEqualTo(code.ToString());
            await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.ErrorAsync(
                await caller.Mcp.CallAsync(SqlOperationProtocol.ToolName, request, token), code, dispatched: true);
            await PostgresRf3Assertions.ErrorAsync(wire.ExecuteAsync<QueryPage>(sql, [], prepared: true, token),
                code == ErrorCode.Validation ? SqlNullOrderingRf3Protocol.ValidationSqlState
                    : PostgresRf3Protocol.UnsupportedSqlState, code);
            await global::TUnit.Assertions.Assert.That(await SqlNullDistinctRf3Image.ReadAsync(root, state, token)).IsEqualTo(before);
            var item = SqlNullDistinctRf3Cases.All.Single(item => item.Clause == SqlNullDistinctRf3Cases.ContinuationClause);
            await SqlNullDistinctRf3Pages.CaseAsync(caller, wire, item, state.LowRevision, token);
            await global::TUnit.Assertions.Assert.That(await SqlNullDistinctRf3Image.ReadAsync(root, state, token)).IsEqualTo(before);
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
