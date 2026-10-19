using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class CapabilityOutputClusterPages
{
    internal static async Task RunAsync(RequestCqrsClusterCallers caller, CapabilityOutputClusterState state,
        int mode, CancellationToken token)
    {
        var sdkManifest = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.QueryCapabilitiesAsync(token));
        var officialManifest = (await McpCallerAssertions.SuccessAsync<QueryCapabilityManifest>(
            await caller.Mcp.CallAsync(McpCallerTools.QueryCapabilities, new
            {
            }, token))).Value;
        await SqlClusterProtocol.EqualAsync(state.Manifest, sdkManifest);
        await SqlClusterProtocol.EqualAsync(state.Manifest, officialManifest);
        foreach (var version in new[] { 1 })
        {
            await RefuseAsync(caller, false, version, ErrorCode.BudgetExceeded, token);
            await HealthyAsync(caller, version, sdkManifest.MaxBytes, token);
        }
        await RefuseAsync(caller, true, 3, ErrorCode.UnsupportedCapability, token);
        await HealthyAsync(caller, 1, sdkManifest.MaxBytes, token);
        await Assert.That(sdkManifest.MaxBytes).IsEqualTo(CapabilityOutputClusterProtocol.Expected(mode));
        await Assert.That(sdkManifest.MaxBytes).IsNotEqualTo(CapabilityOutputClusterProtocol.RequestBytes);
        await Assert.That(sdkManifest.SqlDialect).IsEqualTo("TSQL.ScriptDOM180");
        await Assert.That(sdkManifest.AstVersion).IsEqualTo(1);
        await Assert.That(sdkManifest.SupportedAstVersions).IsEquivalentTo(new[] { 1 },
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(sdkManifest.SupportedQueryDialectVersions).IsEquivalentTo(new[] { 1 },
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    internal static async Task RefuseAsync(RequestCqrsClusterCallers caller, bool compact, int version,
        ErrorCode code, CancellationToken token)
    {
        var sql = await caller.Sdk.QueryAsync(CapabilityOutputClusterProtocol.Sql(compact, version), token);
        var ast = await caller.Sdk.QueryAstAsync(CapabilityOutputClusterProtocol.Ast(compact, version), token);
        await Assert.That(sql.IsSuccess).IsFalse();
        await Assert.That(ast.IsSuccess).IsFalse();
        await Assert.That(sql.Value).IsNull();
        await Assert.That(ast.Value).IsNull();
        await Assert.That(sql.Problem?.ErrorCode).IsEqualTo(code.ToString());
        await Assert.That(ast.Problem?.ErrorCode).IsEqualTo(code.ToString());
        await McpCallerAssertions.ErrorAsync(await caller.Mcp.CallAsync(McpCallerTools.QueryExecute,
            CapabilityOutputClusterProtocol.Sql(compact, version), token), code, dispatched: true);
        await McpCallerAssertions.ErrorAsync(await caller.Mcp.CallAsync(McpCallerTools.QueryAst,
            CapabilityOutputClusterProtocol.Ast(compact, version), token), code, dispatched: true);
    }

    internal static async Task HealthyAsync(RequestCqrsClusterCallers caller, int version, int bytes, CancellationToken token)
    {
        await PageAsync(await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.QueryAsync(
            CapabilityOutputClusterProtocol.Sql(true, version), token)), bytes);
        await PageAsync(await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.QueryAstAsync(
            CapabilityOutputClusterProtocol.Ast(true, version), token)), bytes);
        await PageAsync((await McpCallerAssertions.SuccessAsync<QueryPage>(await caller.Mcp.CallAsync(
            McpCallerTools.QueryExecute, CapabilityOutputClusterProtocol.Sql(true, version), token))).Value, bytes);
        await PageAsync((await McpCallerAssertions.SuccessAsync<QueryPage>(await caller.Mcp.CallAsync(
            McpCallerTools.QueryAst, CapabilityOutputClusterProtocol.Ast(true, version), token))).Value, bytes);
    }

    private static async Task PageAsync(QueryPage actual, int bytes)
    {
        var expected = new QueryPage([new("a", 1L, "{\"key\":\"a\"}", RedactedFields: []),
            new("b", 1L, "{\"key\":\"b\"}", RedactedFields: [])], null, actual.CutPosition, "bounded-full-scan");
        await SqlClusterProtocol.EqualAsync(expected, actual);
        await Assert.That(actual.CutPosition).IsGreaterThanOrEqualTo(0L);
        await Assert.That(JsonDefaults.Serialize(actual).Length).IsLessThanOrEqualTo(bytes);
    }
}
