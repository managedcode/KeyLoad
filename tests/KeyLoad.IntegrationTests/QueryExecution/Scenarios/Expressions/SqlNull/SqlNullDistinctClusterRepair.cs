namespace KeyLoad.IntegrationTests.QueryExecution;

internal static class SqlNullDistinctClusterRepair
{
    internal static async Task<SqlNullDistinctClusterState> RunAsync(ClusterFixture fixture, SqlNullDistinctClusterState state,
        global::KeyLoad.IntegrationTests.OrleansExecution.RequestCqrsClusterCallers root, CancellationToken token)
    {
        var fault = new CommandRequest(Guid.NewGuid(), PostgresClusterState.Partition,
            [new PutDocument(SqlNullOrderingClusterProtocol.Collection, SqlNullDistinctClusterState.Low,
                SqlNullDistinctClusterState.FaultJson, SqlNullOrderingClusterProtocol.SeedRevision)]);
        var faultReceipt = await CommitAsync(fixture, state, fault, SqlNullDistinctClusterState.FaultRevision, token);
        var dirty = state with
        {
            LowRevision = SqlNullDistinctClusterState.FaultRevision,
            LowJson = SqlNullDistinctClusterState.FaultJson,
            Current = faultReceipt
        };
        var before = await SqlNullDistinctClusterImage.ReadAsync(root, dirty, token);
        await SqlNullDistinctClusterRefusals.RejectAsync(fixture, dirty,
            SqlNullDistinctClusterCases.All[SqlNullDistinctClusterState.AgainstOne].Clause, ErrorCode.Validation, token, ast: true);
        await global::TUnit.Assertions.Assert.That(await SqlNullDistinctClusterImage.ReadAsync(root, dirty, token)).IsEqualTo(before);
        var positive = SqlNullDistinctClusterCases.All.Single(value => value.Clause == SqlNullDistinctClusterCases.PositiveClause);
        await SqlNullDistinctClusterRefusals.RejectAsync(fixture, dirty, positive.Clause, ErrorCode.Validation,
            token, ast: true, astCase: positive);
        await global::TUnit.Assertions.Assert.That(await SqlNullDistinctClusterImage.ReadAsync(root, dirty, token)).IsEqualTo(before);
        var leading = SqlNullDistinctClusterCases.All.Single(value => value.Clause == SqlNullDistinctClusterCases.LeadingClause);
        await SqlNullDistinctClusterRefusals.RejectAsync(fixture, dirty, leading.Clause, ErrorCode.Validation,
            token, ast: true, astCase: leading);
        await global::TUnit.Assertions.Assert.That(await SqlNullDistinctClusterImage.ReadAsync(root, dirty, token)).IsEqualTo(before);
        var hex = SqlNullDistinctClusterCases.All.Single(value => value.Clause == SqlNullDistinctClusterCases.HexClause);
        await SqlNullDistinctClusterRefusals.RejectAsync(fixture, dirty, hex.Clause, ErrorCode.Validation,
            token, ast: true, astCase: hex);
        await global::TUnit.Assertions.Assert.That(await SqlNullDistinctClusterImage.ReadAsync(root, dirty, token)).IsEqualTo(before);
        var escaped = SqlNullDistinctClusterCases.All.Single(value => value.Clause == SqlNullDistinctClusterCases.EscapeClause);
        await SqlNullDistinctClusterRefusals.RejectAsync(fixture, dirty, escaped.Clause, ErrorCode.Validation,
            token, ast: true, astCase: escaped);
        await global::TUnit.Assertions.Assert.That(await SqlNullDistinctClusterImage.ReadAsync(root, dirty, token)).IsEqualTo(before);
        var continuation = SqlNullDistinctClusterCases.All.Single(value => value.Clause == SqlNullDistinctClusterCases.ContinuationClause);
        await SqlNullDistinctClusterRefusals.RejectAsync(fixture, dirty, continuation.Clause, ErrorCode.Validation,
            token, ast: true, astCase: continuation);
        await global::TUnit.Assertions.Assert.That(await SqlNullDistinctClusterImage.ReadAsync(root, dirty, token)).IsEqualTo(before);
        var dollar = SqlNullDistinctClusterCases.All.Single(value => value.Clause == SqlNullDistinctClusterCases.DollarClause);
        await SqlNullDistinctClusterRefusals.RejectAsync(fixture, dirty, dollar.Clause, ErrorCode.Validation,
            token, ast: true, astCase: dollar);
        await SqlRadixIntegerClusterControls.RequireAsync(fixture, dirty, root, ErrorCode.Validation, token);
        await SqlUnicodeStringClusterControls.RequireAsync(fixture, dirty, root, ErrorCode.Validation, token);
        await SqlUnicodeIdentifierClusterControls.RequireAsync(fixture, dirty, root, ErrorCode.Validation, token);
        await SqlNumericSeparatorClusterControls.RequireAsync(fixture, dirty, root, ErrorCode.Validation, token);
        await global::TUnit.Assertions.Assert.That(await SqlNullDistinctClusterImage.ReadAsync(root, dirty, token)).IsEqualTo(before);
        var repair = new CommandRequest(Guid.NewGuid(), PostgresClusterState.Partition,
            [new PutDocument(SqlNullOrderingClusterProtocol.Collection, SqlNullDistinctClusterState.Low,
                SqlNullDistinctClusterState.HealthyJson, SqlNullDistinctClusterState.FaultRevision)]);
        var receipt = await CommitAsync(fixture, dirty, repair, SqlNullDistinctClusterState.RepairedRevision, token);
        await global::TUnit.Assertions.Assert.That(repair.CommandId).IsNotEqualTo(fault.CommandId);
        var healthy = state with
        {
            LowRevision = SqlNullDistinctClusterState.RepairedRevision,
            LowJson = SqlNullDistinctClusterState.HealthyJson,
            Current = receipt
        };
        var repairedImage = await SqlNullDistinctClusterImage.ReadAsync(root, healthy, token);
        await CommitAsync(fixture, healthy, fault, SqlNullDistinctClusterState.FaultRevision, token, faultReceipt);
        await CommitAsync(fixture, healthy, repair, SqlNullDistinctClusterState.RepairedRevision, token, receipt);
        await global::TUnit.Assertions.Assert.That(await SqlNullDistinctClusterImage.ReadAsync(root, healthy, token)).IsEqualTo(repairedImage);
        await SqlNullDistinctClusterPages.HealthyAsync(fixture, healthy, token);
        return healthy;
    }

    private static async Task<CommitReceipt> CommitAsync(ClusterFixture fixture, SqlNullDistinctClusterState state,
        CommandRequest command, long revision, CancellationToken token, CommitReceipt? replay = null)
    {
        return await global::KeyLoad.IntegrationTests.OrleansExecution.RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, global::KeyLoad.IntegrationTests.ClientApi.McpCallerProtocol.Node2, state.Seed.Writer.Secret, async writer =>
        {
            var actual = await global::KeyLoad.IntegrationTests.ClientApi.McpCallerAssertions.SdkSuccessAsync(await writer.Sdk.CommitAsync(command, token));
            var expected = replay ?? new CommitReceipt(command.CommandId, actual.Token,
                [new MutationReceipt(SqlNullDistinctClusterState.PutKind, SqlNullOrderingClusterProtocol.Collection,
                    SqlNullDistinctClusterState.Low, revision)], DurabilityProfile.ProcessDurable, 3);
            await SqlClusterProtocol.EqualAsync(expected, actual);
            await global::TUnit.Assertions.Assert.That(actual.Token.AtomicPartitionId).IsEqualTo(command.Partition.AtomicPartitionId);
            await global::TUnit.Assertions.Assert.That(actual.Token.Position).IsGreaterThan(default(long));
            await SqlClusterProtocol.EqualAsync(expected, (await global::KeyLoad.IntegrationTests.ClientApi.McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await writer.Mcp.CallAsync(global::KeyLoad.IntegrationTests.ClientApi.McpCallerTools.DocumentsCommit, command, token))).Value);
            return actual;
        }, token);
    }
}
