namespace KeyLoad.IntegrationTests.QueryExecution;

internal static class SqlNullDistinctClusterCancellation
{
    internal static async Task RunAsync(ClusterFixture fixture, SqlNullDistinctClusterState state,
        global::KeyLoad.IntegrationTests.OrleansExecution.RequestCqrsClusterCallers root, CancellationToken token)
    {
        var before = await SqlNullDistinctClusterImage.ReadAsync(root, state, token);
        await global::KeyLoad.IntegrationTests.OrleansExecution.RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, global::KeyLoad.IntegrationTests.ClientApi.McpCallerProtocol.Node2, state.Seed.Reader.Secret, async caller =>
        {
            var failures = new List<Exception>();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await using var wire = await PostgresClusterCaller.ConnectAsync(fixture.App, global::KeyLoad.IntegrationTests.ClientApi.McpCallerProtocol.Node3,
                    state.Seed.Reader.Principal.Id, state.Seed.Reader.Secret, PostgresClusterCertificate.RootPath(fixture), token);
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    using var cancelled = new CancellationTokenSource();
                    await cancelled.CancelAsync();
                    foreach (var item in SqlNullDistinctClusterCases.All.Where(item =>
                        item.Clause == SqlNullDistinctClusterCases.All[SqlNullDistinctClusterState.First].Clause
                        || item.Clause == SqlNullDistinctClusterCases.NullClause
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
                            || item.Clause == SqlNullDistinctClusterCases.OctalClause || SqlUnicodeIdentifierClusterControls.IsSelected(item)))
                    {
                        var sql = SqlNullDistinctClusterCases.Prefix + item.Clause + SqlNullDistinctClusterCases.Tail;
                        var operation = SqlNullOrderingClusterProtocol.Operation(sql);
                        await SqlClusterProtocol.SdkCancelledAsync(await caller.Sdk.ExecuteSqlAsync(operation, cancelled.Token));
                        var mcp = await global::TUnit.Assertions.Assert.ThrowsAsync<OperationCanceledException>(
                            () => caller.Mcp.CallAsync(SqlOperationProtocol.ToolName, operation, cancelled.Token));
                        await global::TUnit.Assertions.Assert.That(mcp?.CancellationToken).IsEqualTo(cancelled.Token);
                        var driver = await global::TUnit.Assertions.Assert.ThrowsAsync<OperationCanceledException>(
                            () => wire.ExecuteAsync<QueryPage>(sql, [], prepared: false, cancelled.Token));
                        await global::TUnit.Assertions.Assert.That(driver?.CancellationToken).IsEqualTo(cancelled.Token);
                        await SqlNullDistinctClusterPages.CaseAsync(caller, wire, item, state.LowRevision, token);
                    }
                }, failures);
            }, failures);
            global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
            return true;
        }, token);
        await global::TUnit.Assertions.Assert.That(await SqlNullDistinctClusterImage.ReadAsync(root, state, token)).IsEqualTo(before);
    }
}
