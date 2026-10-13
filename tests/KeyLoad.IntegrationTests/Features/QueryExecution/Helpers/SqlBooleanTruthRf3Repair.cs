namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlBooleanTruthRf3Repair
{
    internal static async Task<SqlBooleanTruthRf3State> RunAsync(ClusterFixture fixture, SqlBooleanTruthRf3State state,
        global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsRf3Callers root, CancellationToken token)
    {
        var fault = new CommandRequest(Guid.NewGuid(), PostgresRf3State.Partition,
            [new PutDocument(SqlNullOrderingRf3Protocol.Collection, SqlBooleanTruthRf3State.Low,
                SqlBooleanTruthRf3State.FaultJson, SqlNullOrderingRf3Protocol.SeedRevision)]);
        var faultReceipt = await CommitAsync(fixture, state, fault, SqlBooleanTruthRf3State.FaultRevision, token);
        var dirty = state with
        {
            LowRevision = SqlBooleanTruthRf3State.FaultRevision,
            LowJson = SqlBooleanTruthRf3State.FaultJson,
            Current = faultReceipt
        };
        var before = await SqlBooleanTruthRf3Image.ReadAsync(root, dirty, token);
        await SqlBooleanTruthRf3Refusals.RejectAsync(fixture, dirty,
            SqlBooleanTruthRf3Cases.All[SqlBooleanTruthRf3State.AgainstOne].Clause, ErrorCode.Validation, token, ast: true);
        await SqlBooleanOperandRf3Controls.RequireAsync(fixture, dirty, root, ErrorCode.Validation, token);
        await global::TUnit.Assertions.Assert.That(await SqlBooleanTruthRf3Image.ReadAsync(root, dirty, token)).IsEqualTo(before);
        var repair = new CommandRequest(Guid.NewGuid(), PostgresRf3State.Partition,
            [new PutDocument(SqlNullOrderingRf3Protocol.Collection, SqlBooleanTruthRf3State.Low,
                SqlBooleanTruthRf3State.HealthyJson, SqlBooleanTruthRf3State.FaultRevision)]);
        var receipt = await CommitAsync(fixture, dirty, repair, SqlBooleanTruthRf3State.RepairedRevision, token);
        await global::TUnit.Assertions.Assert.That(repair.CommandId).IsNotEqualTo(fault.CommandId);
        var healthy = state with
        {
            LowRevision = SqlBooleanTruthRf3State.RepairedRevision,
            LowJson = SqlBooleanTruthRf3State.HealthyJson,
            Current = receipt
        };
        var repairedImage = await SqlBooleanTruthRf3Image.ReadAsync(root, healthy, token);
        await CommitAsync(fixture, healthy, fault, SqlBooleanTruthRf3State.FaultRevision, token, faultReceipt);
        await CommitAsync(fixture, healthy, repair, SqlBooleanTruthRf3State.RepairedRevision, token, receipt);
        await global::TUnit.Assertions.Assert.That(await SqlBooleanTruthRf3Image.ReadAsync(root, healthy, token)).IsEqualTo(repairedImage);
        await SqlBooleanTruthRf3Pages.HealthyAsync(fixture, healthy, token);
        return healthy;
    }

    private static async Task<CommitReceipt> CommitAsync(ClusterFixture fixture, SqlBooleanTruthRf3State state,
        CommandRequest command, long revision, CancellationToken token, CommitReceipt? replay = null)
    {
        return await global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerProtocol.Node2, state.Seed.Writer.Secret, async writer =>
        {
            var actual = await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SdkSuccessAsync(await writer.Sdk.CommitAsync(command, token));
            var expected = replay ?? new CommitReceipt(command.CommandId, actual.Token,
                [new MutationReceipt(SqlBooleanTruthRf3State.PutKind, SqlNullOrderingRf3Protocol.Collection,
                    SqlBooleanTruthRf3State.Low, revision)], DurabilityProfile.QuorumProcessDurable);
            await SqlRf3Protocol.EqualAsync(expected, actual);
            await global::TUnit.Assertions.Assert.That(actual.Token.AtomicPartitionId).IsEqualTo(command.Partition.AtomicPartitionId);
            await global::TUnit.Assertions.Assert.That(actual.Token.Position).IsGreaterThan(default(long));
            await SqlRf3Protocol.EqualAsync(expected, (await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await writer.Mcp.CallAsync(global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerTools.DocumentsCommit, command, token))).Value);
            return actual;
        }, token);
    }
}
