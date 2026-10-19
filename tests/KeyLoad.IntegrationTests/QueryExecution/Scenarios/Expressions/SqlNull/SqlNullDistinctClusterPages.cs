using KeyLoad.Query;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlNullDistinctClusterPages
{
    internal static async Task HealthyAsync(ClusterFixture fixture, SqlNullDistinctClusterState state, CancellationToken token)
    {
        await global::KeyLoad.IntegrationTests.Features.OrleansExecution.RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerProtocol.Node2, state.Seed.Reader.Secret, async caller =>
        {
            var capabilities = await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.QueryCapabilitiesAsync(token));
            await global::TUnit.Assertions.Assert.That(capabilities.ReadProfiles.Contains(SqlNullDistinctClusterCases.Profile)).IsTrue();
            await global::TUnit.Assertions.Assert.That(capabilities.ReadProfiles.Contains(SqlNullDistinctClusterCases.NullSynonymProfile)).IsTrue();
            await global::TUnit.Assertions.Assert.That(capabilities.ReadProfiles.Contains(SqlNullDistinctClusterCases.PositiveProfile)).IsTrue();
            await global::TUnit.Assertions.Assert.That(capabilities.ReadProfiles.Contains(SqlNullDistinctClusterCases.LeadingProfile)).IsTrue();
            await global::TUnit.Assertions.Assert.That(capabilities.ReadProfiles.Contains(SqlNullDistinctClusterCases.HexProfile)).IsTrue();
            await global::TUnit.Assertions.Assert.That(capabilities.ReadProfiles.Contains(SqlNullDistinctClusterCases.EscapeProfile)).IsTrue();
            await global::TUnit.Assertions.Assert.That(capabilities.ReadProfiles.Contains(SqlNullDistinctClusterCases.DollarProfile)).IsTrue();
            await global::TUnit.Assertions.Assert.That(capabilities.ReadProfiles.Contains(SqlNullDistinctClusterCases.BinaryOctalProfile)).IsTrue();
            await global::TUnit.Assertions.Assert.That(capabilities.ReadProfiles.Contains(SqlNullDistinctClusterCases.UnicodeProfile)).IsTrue();
            await global::TUnit.Assertions.Assert.That(capabilities.ReadProfiles.Contains(SqlNullDistinctClusterCases.NumericSeparatorProfile)).IsTrue();
            var mcp = (await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SuccessAsync<QueryCapabilityManifest>(
                await caller.Mcp.CallAsync(global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerTools.QueryCapabilities, new
                {
                }, token))).Value;
            await global::TUnit.Assertions.Assert.That(mcp.ReadProfiles.Contains(SqlNullDistinctClusterCases.Profile)).IsTrue();
            await global::TUnit.Assertions.Assert.That(mcp.ReadProfiles.Contains(SqlNullDistinctClusterCases.NullSynonymProfile)).IsTrue();
            await global::TUnit.Assertions.Assert.That(mcp.ReadProfiles.Contains(SqlNullDistinctClusterCases.PositiveProfile)).IsTrue();
            await global::TUnit.Assertions.Assert.That(mcp.ReadProfiles.Contains(SqlNullDistinctClusterCases.LeadingProfile)).IsTrue();
            await global::TUnit.Assertions.Assert.That(mcp.ReadProfiles.Contains(SqlNullDistinctClusterCases.HexProfile)).IsTrue();
            await global::TUnit.Assertions.Assert.That(mcp.ReadProfiles.Contains(SqlNullDistinctClusterCases.EscapeProfile)).IsTrue();
            await global::TUnit.Assertions.Assert.That(mcp.ReadProfiles.Contains(SqlNullDistinctClusterCases.DollarProfile)).IsTrue();
            await global::TUnit.Assertions.Assert.That(mcp.ReadProfiles.Contains(SqlNullDistinctClusterCases.BinaryOctalProfile)).IsTrue();
            await global::TUnit.Assertions.Assert.That(mcp.ReadProfiles.Contains(SqlNullDistinctClusterCases.UnicodeProfile)).IsTrue();
            await global::TUnit.Assertions.Assert.That(mcp.ReadProfiles.Contains(SqlNullDistinctClusterCases.NumericSeparatorProfile)).IsTrue();
            await global::TUnit.Assertions.Assert.That(capabilities.ReadProfiles.Contains(SqlUnicodeIdentifierCases.Profile)).IsTrue();
            await global::TUnit.Assertions.Assert.That(mcp.ReadProfiles.Contains(SqlUnicodeIdentifierCases.Profile)).IsTrue();
            var failures = new List<Exception>();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await using var wire = await PostgresClusterCaller.ConnectAsync(fixture.App, global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerProtocol.Node3,
                    state.Seed.Reader.Principal.Id, state.Seed.Reader.Secret, PostgresClusterCertificate.RootPath(fixture), token);
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    foreach (var item in SqlNullDistinctClusterCases.All)
                    {
                        await CaseAsync(caller, wire, item, state.LowRevision, token);
                    }
                }, failures);
            }, failures);
            global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
            return true;
        }, token);
    }

    internal static async Task CaseAsync(global::KeyLoad.IntegrationTests.Features.OrleansExecution.RequestCqrsClusterCallers caller, PostgresClusterCaller wire,
        SqlNullDistinctClusterCase item, long revision, CancellationToken token)
    {
        var sql = SqlNullDistinctClusterCases.Prefix + item.Clause + SqlNullDistinctClusterCases.Tail;
        var request = SqlNullOrderingClusterProtocol.Operation(sql);
        await SqlNullDistinctClusterImage.PageAsync(await SqlClusterProtocol.SdkAsync<QueryPage>(caller.Sdk, request, token), item, revision);
        await SqlNullDistinctClusterImage.PageAsync(await SqlClusterProtocol.McpAsync<QueryPage>(caller.Mcp, request, token), item, revision);
        var ast = SqlNullDistinctClusterCases.Ast(PostgresClusterState.Partition, item);
        await SqlNullDistinctClusterImage.PageAsync(await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.QueryAstAsync(ast, token)), item, revision);
        await SqlNullDistinctClusterImage.PageAsync((await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SuccessAsync<QueryPage>(
            await caller.Mcp.CallAsync(global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerTools.QueryAst, ast, token))).Value, item, revision);
        await SqlNullDistinctClusterImage.PageAsync(await wire.ExecuteAsync<QueryPage>(sql, [], prepared: true, token), item, revision);
        await SqlNullDistinctClusterImage.PageAsync(await wire.ExecuteAsync<QueryPage>(sql, [], prepared: false, token), item, revision);
        if (item.Clause == SqlNullDistinctClusterCases.EscapeClause)
        {
            await SqlEscapeStringClusterParameters.RequireAsync(wire, item, revision, token);
        }
        if (item.Clause == SqlNullDistinctClusterCases.UnicodeClause)
        {
            await SqlUnicodeStringClusterParameters.RequireAsync(wire, item, revision, token);
        }
        if (item.Clause == SqlNullDistinctClusterCases.NumericSeparatorClause)
        {
            await SqlNumericSeparatorClusterParameters.RequireAsync(caller, wire, revision, token);
        }
        if (item.Clause == SqlUnicodeIdentifierCases.Clause)
        {
            await SqlUnicodeIdentifierClusterParameters.RequireAsync(caller, wire, revision, token);
        }
        if (item.Clause == SqlNullDistinctClusterCases.DollarClause)
        {
            await SqlDollarStringClusterParameters.RequireAsync(wire, item, revision, token);
        }
    }
}
