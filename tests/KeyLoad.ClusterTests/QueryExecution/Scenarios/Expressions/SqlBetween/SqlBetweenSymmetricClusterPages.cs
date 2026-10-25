using KeyLoad.Query;

namespace KeyLoad.ClusterTests.QueryExecution;

internal static class SqlBetweenSymmetricClusterPages
{
    internal static async Task HealthyAsync(ClusterFixture fixture, SqlBetweenSymmetricClusterState state, CancellationToken token)
    {
        await global::KeyLoad.ClusterTests.OrleansExecution.RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, global::KeyLoad.ClusterTests.ClientApi.McpCallerProtocol.Node2, state.Seed.Reader.Secret, async caller =>
        {
            var capabilities = await global::KeyLoad.ClusterTests.ClientApi.McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.QueryCapabilitiesAsync(token));
            await global::TUnit.Assertions.Assert.That(capabilities.ReadProfiles.Contains(SqlBetweenSymmetricClusterCases.Profile)).IsTrue();
            var mcp = (await global::KeyLoad.ClusterTests.ClientApi.McpCallerAssertions.SuccessAsync<QueryCapabilityManifest>(
                await caller.Mcp.CallWithoutBodyAsync(global::KeyLoad.ClusterTests.ClientApi.McpCallerTools.QueryCapabilities, token))).Value;
            await global::TUnit.Assertions.Assert.That(mcp.ReadProfiles.Contains(SqlBetweenSymmetricClusterCases.Profile)).IsTrue();
            var failures = new List<Exception>();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await using var wire = await PostgresClusterCaller.ConnectAsync(fixture.App, global::KeyLoad.ClusterTests.ClientApi.McpCallerProtocol.Node3,
                    state.Seed.Reader.Principal.Id, state.Seed.Reader.Secret, PostgresClusterCertificate.RootPath(fixture), token);
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    foreach (var item in SqlBetweenSymmetricClusterCases.All)
                    {
                        await CaseAsync(caller, wire, item, state.LowRevision, token);
                    }
                }, failures);
            }, failures);
            global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
            return true;
        }, token);
    }

    internal static async Task CaseAsync(global::KeyLoad.ClusterTests.OrleansExecution.RequestCqrsClusterCallers caller, PostgresClusterCaller wire,
        SqlBetweenSymmetricClusterCase item, long revision, CancellationToken token)
    {
        var sql = SqlBetweenSymmetricClusterCases.Prefix + item.Clause + SqlBetweenSymmetricClusterCases.Tail;
        var request = SqlNullOrderingClusterProtocol.Operation(sql);
        await SqlBetweenSymmetricClusterImage.PageAsync(await SqlClusterProtocol.SdkAsync<QueryPage>(caller.Sdk, request, token), item, revision);
        await SqlBetweenSymmetricClusterImage.PageAsync(await SqlClusterProtocol.McpAsync<QueryPage>(caller.Mcp, request, token), item, revision);
        var ast = SqlBetweenSymmetricClusterCases.Ast(PostgresClusterState.Partition, item);
        await SqlBetweenSymmetricClusterImage.PageAsync(await global::KeyLoad.ClusterTests.ClientApi.McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.QueryAstAsync(ast, token)), item, revision);
        await SqlBetweenSymmetricClusterImage.PageAsync((await global::KeyLoad.ClusterTests.ClientApi.McpCallerAssertions.SuccessAsync<QueryPage>(
            await caller.Mcp.CallAsync(global::KeyLoad.ClusterTests.ClientApi.McpCallerTools.QueryAst, ast, token))).Value, item, revision);
        await SqlBetweenSymmetricClusterImage.PageAsync(await wire.ExecuteAsync<QueryPage>(sql, [], prepared: true, token), item, revision);
        await SqlBetweenSymmetricClusterImage.PageAsync(await wire.ExecuteAsync<QueryPage>(sql, [], prepared: false, token), item, revision);
    }
}
