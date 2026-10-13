namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlNullDistinctRf3Repair
{
    internal static async Task<SqlNullDistinctRf3State> RunAsync(ClusterFixture fixture, SqlNullDistinctRf3State state,
        global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsRf3Callers root, CancellationToken token)
    {
        var fault = new CommandRequest(Guid.NewGuid(), PostgresRf3State.Partition,
            [new PutDocument(SqlNullOrderingRf3Protocol.Collection, SqlNullDistinctRf3State.Low,
                SqlNullDistinctRf3State.FaultJson, SqlNullOrderingRf3Protocol.SeedRevision)]);
        var faultReceipt = await CommitAsync(fixture, state, fault, SqlNullDistinctRf3State.FaultRevision, token);
        var dirty = state with
        {
            LowRevision = SqlNullDistinctRf3State.FaultRevision,
            LowJson = SqlNullDistinctRf3State.FaultJson,
            Current = faultReceipt
        };
        var before = await SqlNullDistinctRf3Image.ReadAsync(root, dirty, token);
        await SqlNullDistinctRf3Refusals.RejectAsync(fixture, dirty,
            SqlNullDistinctRf3Cases.All[SqlNullDistinctRf3State.AgainstOne].Clause, ErrorCode.Validation, token, ast: true);
        await global::TUnit.Assertions.Assert.That(await SqlNullDistinctRf3Image.ReadAsync(root, dirty, token)).IsEqualTo(before);
        var positive = SqlNullDistinctRf3Cases.All.Single(value => value.Clause == SqlNullDistinctRf3Cases.PositiveClause);
        await SqlNullDistinctRf3Refusals.RejectAsync(fixture, dirty, positive.Clause, ErrorCode.Validation,
            token, ast: true, astCase: positive);
        await global::TUnit.Assertions.Assert.That(await SqlNullDistinctRf3Image.ReadAsync(root, dirty, token)).IsEqualTo(before);
        var leading = SqlNullDistinctRf3Cases.All.Single(value => value.Clause == SqlNullDistinctRf3Cases.LeadingClause);
        await SqlNullDistinctRf3Refusals.RejectAsync(fixture, dirty, leading.Clause, ErrorCode.Validation,
            token, ast: true, astCase: leading);
        await global::TUnit.Assertions.Assert.That(await SqlNullDistinctRf3Image.ReadAsync(root, dirty, token)).IsEqualTo(before);
        var repair = new CommandRequest(Guid.NewGuid(), PostgresRf3State.Partition,
            [new PutDocument(SqlNullOrderingRf3Protocol.Collection, SqlNullDistinctRf3State.Low,
                SqlNullDistinctRf3State.HealthyJson, SqlNullDistinctRf3State.FaultRevision)]);
        var receipt = await CommitAsync(fixture, dirty, repair, SqlNullDistinctRf3State.RepairedRevision, token);
        await global::TUnit.Assertions.Assert.That(repair.CommandId).IsNotEqualTo(fault.CommandId);
        var healthy = state with
        {
            LowRevision = SqlNullDistinctRf3State.RepairedRevision,
            LowJson = SqlNullDistinctRf3State.HealthyJson,
            Current = receipt
        };
        var repairedImage = await SqlNullDistinctRf3Image.ReadAsync(root, healthy, token);
        await CommitAsync(fixture, healthy, fault, SqlNullDistinctRf3State.FaultRevision, token, faultReceipt);
        await CommitAsync(fixture, healthy, repair, SqlNullDistinctRf3State.RepairedRevision, token, receipt);
        await global::TUnit.Assertions.Assert.That(await SqlNullDistinctRf3Image.ReadAsync(root, healthy, token)).IsEqualTo(repairedImage);
        await SqlNullDistinctRf3Pages.HealthyAsync(fixture, healthy, token);
        return healthy;
    }

    private static async Task<CommitReceipt> CommitAsync(ClusterFixture fixture, SqlNullDistinctRf3State state,
        CommandRequest command, long revision, CancellationToken token, CommitReceipt? replay = null)
    {
        return await global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerProtocol.Node2, state.Seed.Writer.Secret, async writer =>
        {
            var actual = await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SdkSuccessAsync(await writer.Sdk.CommitAsync(command, token));
            var expected = replay ?? new CommitReceipt(command.CommandId, actual.Token,
                [new MutationReceipt(SqlNullDistinctRf3State.PutKind, SqlNullOrderingRf3Protocol.Collection,
                    SqlNullDistinctRf3State.Low, revision)], DurabilityProfile.QuorumProcessDurable);
            await SqlRf3Protocol.EqualAsync(expected, actual);
            await global::TUnit.Assertions.Assert.That(actual.Token.AtomicPartitionId).IsEqualTo(command.Partition.AtomicPartitionId);
            await global::TUnit.Assertions.Assert.That(actual.Token.Position).IsGreaterThan(default(long));
            await SqlRf3Protocol.EqualAsync(expected, (await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await writer.Mcp.CallAsync(global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerTools.DocumentsCommit, command, token))).Value);
            return actual;
        }, token);
    }
}
