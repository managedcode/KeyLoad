using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlBooleanTruthClusterPages
{
    internal static async Task HealthyAsync(ClusterFixture fixture, SqlBooleanTruthClusterState state, CancellationToken token)
    {
        await global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerProtocol.Node2, state.Seed.Reader.Secret, async caller =>
        {
            var capabilities = await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.QueryCapabilitiesAsync(token));
            await global::TUnit.Assertions.Assert.That(capabilities.ReadProfiles.Contains(SqlBooleanTruthClusterCases.Profile)).IsTrue();
            await global::TUnit.Assertions.Assert.That(capabilities.ReadProfiles.Contains(SqlBooleanTruthClusterCases.OperandProfile)).IsTrue();
            var mcp = (await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SuccessAsync<QueryCapabilityManifest>(
                await caller.Mcp.CallAsync(global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerTools.QueryCapabilities, new
                {
                }, token))).Value;
            await global::TUnit.Assertions.Assert.That(mcp.ReadProfiles.Contains(SqlBooleanTruthClusterCases.Profile)).IsTrue();
            await global::TUnit.Assertions.Assert.That(mcp.ReadProfiles.Contains(SqlBooleanTruthClusterCases.OperandProfile)).IsTrue();
            var failures = new List<Exception>();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await using var wire = await PostgresClusterCaller.ConnectAsync(fixture.App, global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerProtocol.Node3,
                    state.Seed.Reader.Principal.Id, state.Seed.Reader.Secret, PostgresClusterCertificate.RootPath(fixture), token);
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    foreach (var item in SqlBooleanTruthClusterCases.All)
                    {
                        await CaseAsync(caller, wire, item, state.LowRevision, token);
                    }
                }, failures);
            }, failures);
            global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
            return true;
        }, token);
    }

    internal static async Task CaseAsync(global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsClusterCallers caller, PostgresClusterCaller wire,
        SqlBooleanTruthClusterCase item, long revision, CancellationToken token)
    {
        var sql = SqlBooleanTruthClusterCases.Prefix + item.Clause + SqlBooleanTruthClusterCases.Tail;
        var request = SqlNullOrderingClusterProtocol.Operation(sql);
        await SqlBooleanTruthClusterImage.PageAsync(await SqlClusterProtocol.SdkAsync<QueryPage>(caller.Sdk, request, token), item, revision);
        await SqlBooleanTruthClusterImage.PageAsync(await SqlClusterProtocol.McpAsync<QueryPage>(caller.Mcp, request, token), item, revision);
        var ast = SqlBooleanTruthClusterCases.Ast(PostgresClusterState.Partition, item);
        await SqlBooleanTruthClusterImage.PageAsync(await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.QueryAstAsync(ast, token)), item, revision);
        await SqlBooleanTruthClusterImage.PageAsync((await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SuccessAsync<QueryPage>(
            await caller.Mcp.CallAsync(global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerTools.QueryAst, ast, token))).Value, item, revision);
        await SqlBooleanTruthClusterImage.PageAsync(await wire.ExecuteAsync<QueryPage>(sql, [], prepared: true, token), item, revision);
        await SqlBooleanTruthClusterImage.PageAsync(await wire.ExecuteAsync<QueryPage>(sql, [], prepared: false, token), item, revision);
        if (item.Clause == SqlBooleanTruthClusterCases.BareClause)
        {
            await SqlBooleanOperandClusterParameters.RequireAsync(caller, wire, revision, token);
        }
    }
}
