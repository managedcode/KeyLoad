namespace KeyLoad.ClusterTests.QueryExecution;

internal static class SqlBooleanTruthClusterRepair
{
    internal static async Task<SqlBooleanTruthClusterState> RunAsync(ClusterFixture fixture, SqlBooleanTruthClusterState state,
        global::KeyLoad.ClusterTests.OrleansExecution.RequestCqrsClusterCallers root, CancellationToken token)
    {
        var fault = new CommandRequest(Guid.NewGuid(), PostgresClusterState.Partition,
            [new PutDocument(SqlNullOrderingClusterProtocol.Collection, SqlBooleanTruthClusterState.Low,
                SqlBooleanTruthClusterState.FaultJson, SqlNullOrderingClusterProtocol.SeedRevision)]);
        var faultReceipt = await CommitAsync(fixture, state, fault, SqlBooleanTruthClusterState.FaultRevision, token);
        var dirty = state with
        {
            LowRevision = SqlBooleanTruthClusterState.FaultRevision,
            LowJson = SqlBooleanTruthClusterState.FaultJson,
            Current = faultReceipt
        };
        var before = await SqlBooleanTruthClusterImage.ReadAsync(root, dirty, token);
        await SqlBooleanTruthClusterRefusals.RejectAsync(fixture, dirty,
            SqlBooleanTruthClusterCases.All[SqlBooleanTruthClusterState.AgainstOne].Clause, ErrorCode.Validation, token, ast: true);
        await SqlBooleanOperandClusterControls.RequireAsync(fixture, dirty, root, ErrorCode.Validation, token);
        await global::TUnit.Assertions.Assert.That(await SqlBooleanTruthClusterImage.ReadAsync(root, dirty, token)).IsEqualTo(before);
        var repair = new CommandRequest(Guid.NewGuid(), PostgresClusterState.Partition,
            [new PutDocument(SqlNullOrderingClusterProtocol.Collection, SqlBooleanTruthClusterState.Low,
                SqlBooleanTruthClusterState.HealthyJson, SqlBooleanTruthClusterState.FaultRevision)]);
        var receipt = await CommitAsync(fixture, dirty, repair, SqlBooleanTruthClusterState.RepairedRevision, token);
        await global::TUnit.Assertions.Assert.That(repair.CommandId).IsNotEqualTo(fault.CommandId);
        var healthy = state with
        {
            LowRevision = SqlBooleanTruthClusterState.RepairedRevision,
            LowJson = SqlBooleanTruthClusterState.HealthyJson,
            Current = receipt
        };
        var repairedImage = await SqlBooleanTruthClusterImage.ReadAsync(root, healthy, token);
        await CommitAsync(fixture, healthy, fault, SqlBooleanTruthClusterState.FaultRevision, token, faultReceipt);
        await CommitAsync(fixture, healthy, repair, SqlBooleanTruthClusterState.RepairedRevision, token, receipt);
        await global::TUnit.Assertions.Assert.That(await SqlBooleanTruthClusterImage.ReadAsync(root, healthy, token)).IsEqualTo(repairedImage);
        await SqlBooleanTruthClusterPages.HealthyAsync(fixture, healthy, token);
        return healthy;
    }

    private static async Task<CommitReceipt> CommitAsync(ClusterFixture fixture, SqlBooleanTruthClusterState state,
        CommandRequest command, long revision, CancellationToken token, CommitReceipt? replay = null)
    {
        return await global::KeyLoad.ClusterTests.OrleansExecution.RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, global::KeyLoad.ClusterTests.ClientApi.McpCallerProtocol.Node2, state.Seed.Writer.Secret, async writer =>
        {
            var actual = await global::KeyLoad.ClusterTests.ClientApi.McpCallerAssertions.SdkSuccessAsync(await writer.Sdk.CommitAsync(command, token));
            var expected = replay ?? new CommitReceipt(command.CommandId, actual.Token,
                [new MutationReceipt(SqlBooleanTruthClusterState.PutKind, SqlNullOrderingClusterProtocol.Collection,
                    SqlBooleanTruthClusterState.Low, revision)], DurabilityProfile.ProcessDurable, 3);
            await SqlClusterProtocol.EqualAsync(expected, actual);
            await global::TUnit.Assertions.Assert.That(actual.Token.AtomicPartitionId).IsEqualTo(command.Partition.AtomicPartitionId);
            await global::TUnit.Assertions.Assert.That(actual.Token.Position).IsGreaterThan(default(long));
            await SqlClusterProtocol.EqualAsync(expected, (await global::KeyLoad.ClusterTests.ClientApi.McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await writer.Mcp.CallAsync(global::KeyLoad.ClusterTests.ClientApi.McpCallerTools.DocumentsCommit, command, token))).Value);
            return actual;
        }, token);
    }
}
