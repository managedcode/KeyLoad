namespace KeyLoad.ClusterTests.QueryExecution;

internal static class SqlBetweenSymmetricClusterCancellation
{
    internal static async Task RunAsync(ClusterFixture fixture, SqlBetweenSymmetricClusterState state,
        global::KeyLoad.ClusterTests.OrleansExecution.RequestCqrsClusterCallers root, CancellationToken token)
    {
        var before = await SqlBetweenSymmetricClusterImage.ReadAsync(root, state, token);
        await global::KeyLoad.ClusterTests.OrleansExecution.RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, global::KeyLoad.ClusterTests.ClientApi.McpCallerProtocol.Node2, state.Seed.Reader.Secret, async caller =>
        {
            var failures = new List<Exception>();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await using var wire = await PostgresClusterCaller.ConnectAsync(fixture.App, global::KeyLoad.ClusterTests.ClientApi.McpCallerProtocol.Node3,
                    state.Seed.Reader.Principal.Id, state.Seed.Reader.Secret, token);
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    using var cancelled = new CancellationTokenSource();
                    await cancelled.CancelAsync();
                    foreach (var item in new[] { SqlBetweenSymmetricClusterCases.All[SqlBetweenSymmetricClusterState.First],
                        SqlBetweenSymmetricClusterCases.All.Single(value => value.Clause == SqlBetweenSymmetricClusterCases.AsymmetricHealthy) })
                    {
                        var sql = SqlBetweenSymmetricClusterCases.Prefix + item.Clause + SqlBetweenSymmetricClusterCases.Tail;
                        var operation = SqlNullOrderingClusterProtocol.Operation(sql);
                        await SqlClusterProtocol.SdkCancelledAsync(await caller.Sdk.ExecuteSqlAsync(operation, cancelled.Token));
                        var mcp = await global::TUnit.Assertions.Assert.ThrowsAsync<OperationCanceledException>(
                            () => caller.Mcp.CallAsync(SqlOperationProtocol.ToolName, operation, cancelled.Token));
                        await global::TUnit.Assertions.Assert.That(mcp?.CancellationToken).IsEqualTo(cancelled.Token);
                        var driver = await global::TUnit.Assertions.Assert.ThrowsAsync<OperationCanceledException>(
                            () => wire.ExecuteAsync<QueryPage>(sql, [], prepared: false, cancelled.Token));
                        await global::TUnit.Assertions.Assert.That(driver?.CancellationToken).IsEqualTo(cancelled.Token);
                        await SqlBetweenSymmetricClusterPages.CaseAsync(caller, wire, item, state.LowRevision, token);
                    }
                }, failures);
            }, failures);
            global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
            return true;
        }, token);
        await global::TUnit.Assertions.Assert.That(await SqlBetweenSymmetricClusterImage.ReadAsync(root, state, token)).IsEqualTo(before);
    }
}
