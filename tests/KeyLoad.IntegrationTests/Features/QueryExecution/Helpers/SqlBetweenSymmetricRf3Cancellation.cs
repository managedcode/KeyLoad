namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlBetweenSymmetricRf3Cancellation
{
    internal static async Task RunAsync(ClusterFixture fixture, SqlBetweenSymmetricRf3State state,
        global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsRf3Callers root, CancellationToken token)
    {
        var before = await SqlBetweenSymmetricRf3Image.ReadAsync(root, state, token);
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
                    var item = SqlBetweenSymmetricRf3Cases.All[SqlBetweenSymmetricRf3State.First];
                    var sql = SqlBetweenSymmetricRf3Cases.Prefix + item.Clause + SqlBetweenSymmetricRf3Cases.Tail;
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
                    await SqlBetweenSymmetricRf3Pages.CaseAsync(caller, wire, item, state.LowRevision, token);
                }, failures);
            }, failures);
            global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
            return true;
        }, token);
        await global::TUnit.Assertions.Assert.That(await SqlBetweenSymmetricRf3Image.ReadAsync(root, state, token)).IsEqualTo(before);
    }
}
