namespace KeyLoad.ClusterTests.QueryExecution;

internal static class SqlBetweenSymmetricClusterPolicy
{
    internal static async Task RunAsync(ClusterFixture fixture, SqlBetweenSymmetricClusterState state,
        global::KeyLoad.ClusterTests.OrleansExecution.RequestCqrsClusterCallers root, CancellationToken token)
    {
        var original = state.Seed.Reader.Principal;
        var denied = original with
        {
            PolicyEpoch = original.PolicyEpoch + SqlNullOrderingClusterProtocol.DenialAdvance,
            Grants = [new(PostgresClusterState.Partition.DatabaseId, SqlNullOrderingClusterProtocol.Collection, Capability.DocumentsRead)]
        };
        await SqlClusterProtocol.EqualAsync(denied, await global::KeyLoad.ClusterTests.ClientApi.McpCallerAssertions.SdkSuccessAsync(
            await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), denied, token)));
        var before = await SqlBetweenSymmetricClusterImage.ReadAsync(root, state, token);
        await SqlBetweenSymmetricClusterRefusals.RejectAsync(fixture, state,
            SqlBetweenSymmetricClusterCases.All[SqlBetweenSymmetricClusterState.AgainstOne].Clause, ErrorCode.PermissionDenied, token, ast: true);
        await SqlBetweenSymmetricClusterRefusals.RejectAsync(fixture, state,
            SqlBetweenSymmetricClusterCases.AsymmetricHealthy, ErrorCode.PermissionDenied, token);
        await global::TUnit.Assertions.Assert.That(await SqlBetweenSymmetricClusterImage.ReadAsync(root, state, token)).IsEqualTo(before);
        var repaired = original with
        {
            PolicyEpoch = original.PolicyEpoch + SqlNullOrderingClusterProtocol.RepairAdvance
        };
        await SqlClusterProtocol.EqualAsync(repaired, await global::KeyLoad.ClusterTests.ClientApi.McpCallerAssertions.SdkSuccessAsync(
            await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), repaired, token)));
        await SqlBetweenSymmetricClusterPages.HealthyAsync(fixture, state, token);
    }
}
