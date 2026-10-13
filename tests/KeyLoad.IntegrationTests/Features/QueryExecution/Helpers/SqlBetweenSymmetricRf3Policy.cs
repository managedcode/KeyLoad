namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlBetweenSymmetricRf3Policy
{
    internal static async Task RunAsync(ClusterFixture fixture, SqlBetweenSymmetricRf3State state,
        global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsRf3Callers root, CancellationToken token)
    {
        var original = state.Seed.Reader.Principal;
        var denied = original with
        {
            PolicyEpoch = original.PolicyEpoch + SqlNullOrderingRf3Protocol.DenialAdvance,
            Grants = [new(PostgresRf3State.Partition.DatabaseId, SqlNullOrderingRf3Protocol.Collection, Capability.DocumentsRead)]
        };
        await SqlRf3Protocol.EqualAsync(denied, await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SdkSuccessAsync(
            await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), denied, token)));
        var before = await SqlBetweenSymmetricRf3Image.ReadAsync(root, state, token);
        await SqlBetweenSymmetricRf3Refusals.RejectAsync(fixture, state,
            SqlBetweenSymmetricRf3Cases.All[SqlBetweenSymmetricRf3State.AgainstOne].Clause, ErrorCode.PermissionDenied, token, ast: true);
        await global::TUnit.Assertions.Assert.That(await SqlBetweenSymmetricRf3Image.ReadAsync(root, state, token)).IsEqualTo(before);
        var repaired = original with { PolicyEpoch = original.PolicyEpoch + SqlNullOrderingRf3Protocol.RepairAdvance };
        await SqlRf3Protocol.EqualAsync(repaired, await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SdkSuccessAsync(
            await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), repaired, token)));
        await SqlBetweenSymmetricRf3Pages.HealthyAsync(fixture, state, token);
    }
}
