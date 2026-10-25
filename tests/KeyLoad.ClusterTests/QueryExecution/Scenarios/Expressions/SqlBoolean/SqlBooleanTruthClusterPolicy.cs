namespace KeyLoad.ClusterTests.QueryExecution;

internal static class SqlBooleanTruthClusterPolicy
{
    internal static async Task RunAsync(ClusterFixture fixture, SqlBooleanTruthClusterState state,
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
        var before = await SqlBooleanTruthClusterImage.ReadAsync(root, state, token);
        await SqlBooleanTruthClusterRefusals.RejectAsync(fixture, state, SqlBooleanTruthClusterCases.All[SqlBooleanTruthClusterState.AgainstOne].Command, ErrorCode.PermissionDenied, token, ast: true);
        await SqlBooleanOperandClusterControls.RequireAsync(fixture, state, root, ErrorCode.PermissionDenied, token);
        await global::TUnit.Assertions.Assert.That(await SqlBooleanTruthClusterImage.ReadAsync(root, state, token)).IsEqualTo(before);
        var repaired = original with
        {
            PolicyEpoch = original.PolicyEpoch + SqlNullOrderingClusterProtocol.RepairAdvance
        };
        await SqlClusterProtocol.EqualAsync(repaired, await global::KeyLoad.ClusterTests.ClientApi.McpCallerAssertions.SdkSuccessAsync(
            await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), repaired, token)));
        await SqlBooleanTruthClusterPages.HealthyAsync(fixture, state, token);
    }
}
