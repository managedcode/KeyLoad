using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class CapabilityOutputRf3Pages
{
    internal static async Task RunAsync(RequestCqrsRf3Callers caller, CapabilityOutputRf3State state,
        int mode, CancellationToken token)
    {
        var sdkManifest = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.QueryCapabilitiesAsync(token));
        var officialManifest = (await McpCallerAssertions.SuccessAsync<QueryCapabilityManifest>(
            await caller.Mcp.CallAsync(McpCallerTools.QueryCapabilities, new
            {
            }, token))).Value;
        await SqlRf3Protocol.EqualAsync(state.Manifest, sdkManifest);
        await SqlRf3Protocol.EqualAsync(state.Manifest, officialManifest);
        foreach (var version in new[] { 1, 2 })
        {
            await RefuseAsync(caller, false, version, ErrorCode.BudgetExceeded, token);
            await HealthyAsync(caller, version, sdkManifest.MaxBytes, token);
        }
        await RefuseAsync(caller, true, 3, ErrorCode.UnsupportedCapability, token);
        await HealthyAsync(caller, 1, sdkManifest.MaxBytes, token);
        await Assert.That(sdkManifest.MaxBytes).IsEqualTo(CapabilityOutputRf3Protocol.Expected(mode));
        await Assert.That(sdkManifest.MaxBytes).IsNotEqualTo(CapabilityOutputRf3Protocol.RequestBytes);
        await Assert.That(sdkManifest.SqlDialect).IsEqualTo("TSQL.ScriptDOM180.v1");
        await Assert.That(sdkManifest.AstVersion).IsEqualTo(1);
        await Assert.That(sdkManifest.SupportedAstVersions).IsEquivalentTo(new[] { 1, 2 },
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(sdkManifest.SupportedQueryDialectVersions).IsEquivalentTo(new[] { 1, 2 },
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    internal static async Task RefuseAsync(RequestCqrsRf3Callers caller, bool compact, int version,
        ErrorCode code, CancellationToken token)
    {
        var sql = await caller.Sdk.QueryAsync(CapabilityOutputRf3Protocol.Sql(compact, version), token);
        var ast = await caller.Sdk.QueryAstAsync(CapabilityOutputRf3Protocol.Ast(compact, version), token);
        await Assert.That(sql.IsSuccess).IsFalse();
        await Assert.That(ast.IsSuccess).IsFalse();
        await Assert.That(sql.Value).IsNull();
        await Assert.That(ast.Value).IsNull();
        await Assert.That(sql.Problem?.ErrorCode).IsEqualTo(code.ToString());
        await Assert.That(ast.Problem?.ErrorCode).IsEqualTo(code.ToString());
        await McpCallerAssertions.ErrorAsync(await caller.Mcp.CallAsync(McpCallerTools.QueryExecute,
            CapabilityOutputRf3Protocol.Sql(compact, version), token), code, dispatched: true);
        await McpCallerAssertions.ErrorAsync(await caller.Mcp.CallAsync(McpCallerTools.QueryAst,
            CapabilityOutputRf3Protocol.Ast(compact, version), token), code, dispatched: true);
    }

    internal static async Task HealthyAsync(RequestCqrsRf3Callers caller, int version, int bytes, CancellationToken token)
    {
        await PageAsync(await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.QueryAsync(
            CapabilityOutputRf3Protocol.Sql(true, version), token)), bytes);
        await PageAsync(await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.QueryAstAsync(
            CapabilityOutputRf3Protocol.Ast(true, version), token)), bytes);
        await PageAsync((await McpCallerAssertions.SuccessAsync<QueryPage>(await caller.Mcp.CallAsync(
            McpCallerTools.QueryExecute, CapabilityOutputRf3Protocol.Sql(true, version), token))).Value, bytes);
        await PageAsync((await McpCallerAssertions.SuccessAsync<QueryPage>(await caller.Mcp.CallAsync(
            McpCallerTools.QueryAst, CapabilityOutputRf3Protocol.Ast(true, version), token))).Value, bytes);
    }

    private static async Task PageAsync(QueryPage actual, int bytes)
    {
        var expected = new QueryPage([new("a", 1L, "{\"key\":\"a\"}", RedactedFields: []),
            new("b", 1L, "{\"key\":\"b\"}", RedactedFields: [])], null, actual.CutPosition, "bounded-full-scan");
        await SqlRf3Protocol.EqualAsync(expected, actual);
        await Assert.That(actual.CutPosition).IsGreaterThanOrEqualTo(0L);
        await Assert.That(JsonDefaults.Serialize(actual).Length).IsLessThanOrEqualTo(bytes);
    }
}
