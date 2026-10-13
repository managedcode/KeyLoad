namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlBooleanTruthRf3Cancellation
{
    internal static async Task RunAsync(ClusterFixture fixture, SqlBooleanTruthRf3State state,
        global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsRf3Callers root, CancellationToken token)
    {
        var before = await SqlBooleanTruthRf3Image.ReadAsync(root, state, token);
        await global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerProtocol.Node2, state.Seed.Reader.Secret, async caller =>
        {
            var failures = new List<Exception>();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await using var wire = await PostgresRf3Caller.ConnectAsync(fixture.App, global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerProtocol.Node3,
                    state.Seed.Reader.Principal.Id, state.Seed.Reader.Secret, PostgresRf3Certificate.RootPath(fixture), token);
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    using var cancelled = new CancellationTokenSource();
                    await cancelled.CancelAsync();
                    foreach (var current in SqlBooleanTruthRf3Cases.All.Where(item => item.Clause == SqlBooleanTruthRf3Cases.All[SqlBooleanTruthRf3State.First].Clause
                        || item.Clause == SqlBooleanTruthRf3Cases.BareClause || item.Clause == SqlBooleanTruthRf3Cases.NegatedBareClause))
                    {
                        var item = current;
                        var sql = SqlBooleanTruthRf3Cases.Prefix + item.Clause + SqlBooleanTruthRf3Cases.Tail;
                        var operation = SqlNullOrderingRf3Protocol.Operation(sql);
                        var sdk = await global::TUnit.Assertions.Assert.ThrowsAsync<OperationCanceledException>(
                            () => caller.Sdk.ExecuteSqlAsync(operation, cancelled.Token));
                        await global::TUnit.Assertions.Assert.That(sdk?.CancellationToken).IsEqualTo(cancelled.Token);
                        var mcp = await global::TUnit.Assertions.Assert.ThrowsAsync<OperationCanceledException>(
                            () => caller.Mcp.CallAsync(SqlOperationProtocol.ToolName, operation, cancelled.Token));
                        await global::TUnit.Assertions.Assert.That(mcp?.CancellationToken).IsEqualTo(cancelled.Token);
                        var driver = await global::TUnit.Assertions.Assert.ThrowsAsync<OperationCanceledException>(
                            () => wire.ExecuteAsync<QueryPage>(sql, [], prepared: false, cancelled.Token));
                        await global::TUnit.Assertions.Assert.That(driver?.CancellationToken).IsEqualTo(cancelled.Token);
                        await SqlBooleanTruthRf3Pages.CaseAsync(caller, wire, item, state.LowRevision, token);
                    }
                }, failures);
            }, failures);
            global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
            return true;
        }, token);
        await global::TUnit.Assertions.Assert.That(await SqlBooleanTruthRf3Image.ReadAsync(root, state, token)).IsEqualTo(before);
    }
}
