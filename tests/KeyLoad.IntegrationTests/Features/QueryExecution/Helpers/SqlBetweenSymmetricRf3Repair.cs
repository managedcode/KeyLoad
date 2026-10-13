namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlBetweenSymmetricRf3Repair
{
    internal static async Task<SqlBetweenSymmetricRf3State> RunAsync(ClusterFixture fixture, SqlBetweenSymmetricRf3State state,
        global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsRf3Callers root, CancellationToken token)
    {
        var fault = new CommandRequest(Guid.NewGuid(), PostgresRf3State.Partition,
            [new PutDocument(SqlNullOrderingRf3Protocol.Collection, SqlBetweenSymmetricRf3State.Low,
                SqlBetweenSymmetricRf3State.FaultJson, SqlNullOrderingRf3Protocol.SeedRevision)]);
        var faultReceipt = await CommitAsync(fixture, state, fault, SqlBetweenSymmetricRf3State.FaultRevision, token);
        var dirty = state with
        {
            LowRevision = SqlBetweenSymmetricRf3State.FaultRevision,
            LowJson = SqlBetweenSymmetricRf3State.FaultJson,
            Current = faultReceipt
        };
        var before = await SqlBetweenSymmetricRf3Image.ReadAsync(root, dirty, token);
        await SqlBetweenSymmetricRf3Refusals.RejectAsync(fixture, dirty,
            SqlBetweenSymmetricRf3Cases.All[SqlBetweenSymmetricRf3State.AgainstOne].Clause, ErrorCode.Validation, token, ast: true);
        await global::TUnit.Assertions.Assert.That(await SqlBetweenSymmetricRf3Image.ReadAsync(root, dirty, token)).IsEqualTo(before);
        var repair = new CommandRequest(Guid.NewGuid(), PostgresRf3State.Partition,
            [new PutDocument(SqlNullOrderingRf3Protocol.Collection, SqlBetweenSymmetricRf3State.Low,
                SqlBetweenSymmetricRf3State.HealthyJson, SqlBetweenSymmetricRf3State.FaultRevision)]);
        var receipt = await CommitAsync(fixture, dirty, repair, SqlBetweenSymmetricRf3State.RepairedRevision, token);
        await global::TUnit.Assertions.Assert.That(repair.CommandId).IsNotEqualTo(fault.CommandId);
        var healthy = state with
        {
            LowRevision = SqlBetweenSymmetricRf3State.RepairedRevision,
            LowJson = SqlBetweenSymmetricRf3State.HealthyJson,
            Current = receipt
        };
        var repairedImage = await SqlBetweenSymmetricRf3Image.ReadAsync(root, healthy, token);
        await CommitAsync(fixture, healthy, fault, SqlBetweenSymmetricRf3State.FaultRevision, token, faultReceipt);
        await CommitAsync(fixture, healthy, repair, SqlBetweenSymmetricRf3State.RepairedRevision, token, receipt);
        await global::TUnit.Assertions.Assert.That(await SqlBetweenSymmetricRf3Image.ReadAsync(root, healthy, token)).IsEqualTo(repairedImage);
        await SqlBetweenSymmetricRf3Pages.HealthyAsync(fixture, healthy, token);
        return healthy;
    }

    private static async Task<CommitReceipt> CommitAsync(ClusterFixture fixture, SqlBetweenSymmetricRf3State state,
        CommandRequest command, long revision, CancellationToken token, CommitReceipt? replay = null)
    {
        return await global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerProtocol.Node2, state.Seed.Writer.Secret, async writer =>
        {
            var actual = await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SdkSuccessAsync(await writer.Sdk.CommitAsync(command, token));
            var expected = replay ?? new CommitReceipt(command.CommandId, actual.Token,
                [new MutationReceipt(SqlBetweenSymmetricRf3State.PutKind, SqlNullOrderingRf3Protocol.Collection,
                    SqlBetweenSymmetricRf3State.Low, revision)], DurabilityProfile.QuorumProcessDurable);
            await SqlRf3Protocol.EqualAsync(expected, actual);
            await global::TUnit.Assertions.Assert.That(actual.Token.AtomicPartitionId).IsEqualTo(command.Partition.AtomicPartitionId);
            await global::TUnit.Assertions.Assert.That(actual.Token.Position).IsGreaterThan(default(long));
            await SqlRf3Protocol.EqualAsync(expected, (await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await writer.Mcp.CallAsync(global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerTools.DocumentsCommit, command, token))).Value);
            return actual;
        }, token);
    }
}
