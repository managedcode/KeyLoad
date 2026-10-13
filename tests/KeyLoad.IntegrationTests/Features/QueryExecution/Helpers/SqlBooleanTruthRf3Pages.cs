using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlBooleanTruthRf3Pages
{
    internal static async Task HealthyAsync(ClusterFixture fixture, SqlBooleanTruthRf3State state, CancellationToken token)
    {
        await global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerProtocol.Node2, state.Seed.Reader.Secret, async caller =>
        {
            var capabilities = await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.QueryCapabilitiesAsync(token));
            await global::TUnit.Assertions.Assert.That(capabilities.ReadProfiles.Contains(SqlBooleanTruthRf3Cases.Profile)).IsTrue();
            var mcp = (await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SuccessAsync<QueryCapabilityManifest>(
                await caller.Mcp.CallAsync(global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerTools.QueryCapabilities, new { }, token))).Value;
            await global::TUnit.Assertions.Assert.That(mcp.ReadProfiles.Contains(SqlBooleanTruthRf3Cases.Profile)).IsTrue();
            var failures = new List<Exception>();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await using var wire = await PostgresRf3Caller.ConnectAsync(fixture.App, global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerProtocol.Node3,
                    state.Seed.Reader.Principal.Id, state.Seed.Reader.Secret, PostgresRf3Certificate.RootPath(fixture), token);
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    foreach (var item in SqlBooleanTruthRf3Cases.All)
                    { await CaseAsync(caller, wire, item, state.LowRevision, token); }
                }, failures);
            }, failures);
            global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
            return true;
        }, token);
    }

    internal static async Task CaseAsync(global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsRf3Callers caller, PostgresRf3Caller wire,
        SqlBooleanTruthRf3Case item, long revision, CancellationToken token)
    {
        var sql = SqlBooleanTruthRf3Cases.Prefix + item.Clause + SqlBooleanTruthRf3Cases.Tail;
        var request = SqlNullOrderingRf3Protocol.Operation(sql);
        await SqlBooleanTruthRf3Image.PageAsync(await SqlRf3Protocol.SdkAsync<QueryPage>(caller.Sdk, request, token), item, revision);
        await SqlBooleanTruthRf3Image.PageAsync(await SqlRf3Protocol.McpAsync<QueryPage>(caller.Mcp, request, token), item, revision);
        var ast = SqlBooleanTruthRf3Cases.Ast(PostgresRf3State.Partition, item);
        await SqlBooleanTruthRf3Image.PageAsync(await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.QueryAstAsync(ast, token)), item, revision);
        await SqlBooleanTruthRf3Image.PageAsync((await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SuccessAsync<QueryPage>(
            await caller.Mcp.CallAsync(global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerTools.QueryAst, ast, token))).Value, item, revision);
        await SqlBooleanTruthRf3Image.PageAsync(await wire.ExecuteAsync<QueryPage>(sql, [], prepared: true, token), item, revision);
        await SqlBooleanTruthRf3Image.PageAsync(await wire.ExecuteAsync<QueryPage>(sql, [], prepared: false, token), item, revision);
    }
}
