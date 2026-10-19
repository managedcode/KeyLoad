namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlBooleanTruthClusterCancellation
{
    internal static async Task RunAsync(ClusterFixture fixture, SqlBooleanTruthClusterState state,
        global::KeyLoad.IntegrationTests.Features.OrleansExecution.RequestCqrsClusterCallers root, CancellationToken token)
    {
        var before = await SqlBooleanTruthClusterImage.ReadAsync(root, state, token);
        await global::KeyLoad.IntegrationTests.Features.OrleansExecution.RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerProtocol.Node2, state.Seed.Reader.Secret, async caller =>
        {
            var failures = new List<Exception>();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await using var wire = await PostgresClusterCaller.ConnectAsync(fixture.App, global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerProtocol.Node3,
                    state.Seed.Reader.Principal.Id, state.Seed.Reader.Secret, PostgresClusterCertificate.RootPath(fixture), token);
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    using var cancelled = new CancellationTokenSource();
                    await cancelled.CancelAsync();
                    foreach (var current in SqlBooleanTruthClusterCases.All.Where(item => item.Clause == SqlBooleanTruthClusterCases.All[SqlBooleanTruthClusterState.First].Clause
                        || item.Clause == SqlBooleanTruthClusterCases.BareClause || item.Clause == SqlBooleanTruthClusterCases.NegatedBareClause))
                    {
                        var item = current;
                        var sql = SqlBooleanTruthClusterCases.Prefix + item.Clause + SqlBooleanTruthClusterCases.Tail;
                        var operation = SqlNullOrderingClusterProtocol.Operation(sql);
                        var sdk = await global::TUnit.Assertions.Assert.ThrowsAsync<OperationCanceledException>(
                            () => caller.Sdk.ExecuteSqlAsync(operation, cancelled.Token));
                        await global::TUnit.Assertions.Assert.That(sdk?.CancellationToken).IsEqualTo(cancelled.Token);
                        var mcp = await global::TUnit.Assertions.Assert.ThrowsAsync<OperationCanceledException>(
                            () => caller.Mcp.CallAsync(SqlOperationProtocol.ToolName, operation, cancelled.Token));
                        await global::TUnit.Assertions.Assert.That(mcp?.CancellationToken).IsEqualTo(cancelled.Token);
                        var driver = await global::TUnit.Assertions.Assert.ThrowsAsync<OperationCanceledException>(
                            () => wire.ExecuteAsync<QueryPage>(sql, [], prepared: false, cancelled.Token));
                        await global::TUnit.Assertions.Assert.That(driver?.CancellationToken).IsEqualTo(cancelled.Token);
                        await SqlBooleanTruthClusterPages.CaseAsync(caller, wire, item, state.LowRevision, token);
                    }
                }, failures);
            }, failures);
            global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
            return true;
        }, token);
        await global::TUnit.Assertions.Assert.That(await SqlBooleanTruthClusterImage.ReadAsync(root, state, token)).IsEqualTo(before);
    }
}
