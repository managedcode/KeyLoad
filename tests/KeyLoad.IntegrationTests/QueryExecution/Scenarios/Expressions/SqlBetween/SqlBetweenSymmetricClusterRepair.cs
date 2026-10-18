namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlBetweenSymmetricClusterRepair
{
    internal static async Task<SqlBetweenSymmetricClusterState> RunAsync(ClusterFixture fixture, SqlBetweenSymmetricClusterState state,
        global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsClusterCallers root, CancellationToken token)
    {
        var fault = new CommandRequest(Guid.NewGuid(), PostgresClusterState.Partition,
            [new PutDocument(SqlNullOrderingClusterProtocol.Collection, SqlBetweenSymmetricClusterState.Low,
                SqlBetweenSymmetricClusterState.FaultJson, SqlNullOrderingClusterProtocol.SeedRevision)]);
        var faultReceipt = await CommitAsync(fixture, state, fault, SqlBetweenSymmetricClusterState.FaultRevision, token);
        var dirty = state with
        {
            LowRevision = SqlBetweenSymmetricClusterState.FaultRevision,
            LowJson = SqlBetweenSymmetricClusterState.FaultJson,
            Current = faultReceipt
        };
        var before = await SqlBetweenSymmetricClusterImage.ReadAsync(root, dirty, token);
        await SqlBetweenSymmetricClusterRefusals.RejectAsync(fixture, dirty,
            SqlBetweenSymmetricClusterCases.All[SqlBetweenSymmetricClusterState.AgainstOne].Clause, ErrorCode.Validation, token, ast: true);
        await SqlBetweenSymmetricClusterRefusals.RejectAsync(fixture, dirty,
            SqlBetweenSymmetricClusterCases.AsymmetricFault, ErrorCode.Validation, token);
        await global::TUnit.Assertions.Assert.That(await SqlBetweenSymmetricClusterImage.ReadAsync(root, dirty, token)).IsEqualTo(before);
        var repair = new CommandRequest(Guid.NewGuid(), PostgresClusterState.Partition,
            [new PutDocument(SqlNullOrderingClusterProtocol.Collection, SqlBetweenSymmetricClusterState.Low,
                SqlBetweenSymmetricClusterState.HealthyJson, SqlBetweenSymmetricClusterState.FaultRevision)]);
        var receipt = await CommitAsync(fixture, dirty, repair, SqlBetweenSymmetricClusterState.RepairedRevision, token);
        await global::TUnit.Assertions.Assert.That(repair.CommandId).IsNotEqualTo(fault.CommandId);
        var healthy = state with
        {
            LowRevision = SqlBetweenSymmetricClusterState.RepairedRevision,
            LowJson = SqlBetweenSymmetricClusterState.HealthyJson,
            Current = receipt
        };
        var repairedImage = await SqlBetweenSymmetricClusterImage.ReadAsync(root, healthy, token);
        await CommitAsync(fixture, healthy, fault, SqlBetweenSymmetricClusterState.FaultRevision, token, faultReceipt);
        await CommitAsync(fixture, healthy, repair, SqlBetweenSymmetricClusterState.RepairedRevision, token, receipt);
        await global::TUnit.Assertions.Assert.That(await SqlBetweenSymmetricClusterImage.ReadAsync(root, healthy, token)).IsEqualTo(repairedImage);
        await SqlBetweenSymmetricClusterPages.HealthyAsync(fixture, healthy, token);
        return healthy;
    }

    private static async Task<CommitReceipt> CommitAsync(ClusterFixture fixture, SqlBetweenSymmetricClusterState state,
        CommandRequest command, long revision, CancellationToken token, CommitReceipt? replay = null)
    {
        return await global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerProtocol.Node2, state.Seed.Writer.Secret, async writer =>
        {
            var actual = await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SdkSuccessAsync(await writer.Sdk.CommitAsync(command, token));
            var expected = replay ?? new CommitReceipt(command.CommandId, actual.Token,
                [new MutationReceipt(SqlBetweenSymmetricClusterState.PutKind, SqlNullOrderingClusterProtocol.Collection,
                    SqlBetweenSymmetricClusterState.Low, revision)], DurabilityProfile.ProcessDurable, 3);
            await SqlClusterProtocol.EqualAsync(expected, actual);
            await global::TUnit.Assertions.Assert.That(actual.Token.AtomicPartitionId).IsEqualTo(command.Partition.AtomicPartitionId);
            await global::TUnit.Assertions.Assert.That(actual.Token.Position).IsGreaterThan(default(long));
            await SqlClusterProtocol.EqualAsync(expected, (await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await writer.Mcp.CallAsync(global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerTools.DocumentsCommit, command, token))).Value);
            return actual;
        }, token);
    }
}
