using System.Text.Json;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using static KeyLoad.IntegrationTests.QueryExecution.QueryEquivalentIntegerClusterDecimalProtocol;

namespace KeyLoad.IntegrationTests.QueryExecution;

internal static class QueryEquivalentIntegerClusterCalls
{
    private const int SqlRoute = 0;
    private const int JsonRoute = 1;
    private const int BuilderRoute = 2;
    private const string RuntimeDenied = "The principal cannot perform this operation in this scope.";
    private const string McpDenied = RuntimeDenied;
    internal static async Task MatrixAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        QueryEquivalentIntegerClusterState state, bool denied, CancellationToken token)
    {
        foreach (var kind in Enum.GetValues<QueryEquivalentIntegerClusterKind>())
        {
            foreach (var boundary in new[] { false, true })
            {
                await CaseAsync(root, reader, state, kind, boundary, denied, token);
            }
        }
    }
    private static async Task CaseAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        QueryEquivalentIntegerClusterState state, QueryEquivalentIntegerClusterKind kind, bool boundary,
        bool denied, CancellationToken token)
    {
        var minimum = boundary ? Maximum(kind) - Fraction : Threshold;
        var sql = QueryEquivalentIntegerClusterDecimalRequests.Sql(state.Partition, kind, minimum);
        var json = QueryEquivalentIntegerClusterDecimalRequests.Ast(state.Partition, kind, minimum);
        var builder = QueryEquivalentIntegerClusterDecimalRequests.CSharp(state.Partition, kind, minimum);
        foreach (var route in new[] { SqlRoute, JsonRoute, BuilderRoute })
        {
            await QueryEquivalentIntegerClusterCurrent.ObserveAsync(root, state, async () =>
            {
                var sdk = route == SqlRoute ? await reader.Sdk.QueryAsync(sql, token)
                    : await reader.Sdk.QueryAstAsync(route == JsonRoute ? json : builder, token);
                if (denied)
                {
                    await Assert.That(sdk.IsFailed).IsTrue();
                    await Assert.That(sdk.Value).IsNull();
                    await SqlClusterProtocol.EqualAsync(Errors.Problem(ErrorCode.PermissionDenied, RuntimeDenied), sdk.Problem);
                }
                else
                {
                    await QueryEquivalentIntegerClusterCurrent.PageAsync(await McpCallerAssertions.SdkSuccessAsync(sdk), Expected(boundary));
                }
            }, token);
            await QueryEquivalentIntegerClusterCurrent.ObserveAsync(root, state, async () =>
            {
                var reply = route == SqlRoute ? await reader.Mcp.CallAsync(McpCallerTools.QueryExecute, sql, token)
                    : await reader.Mcp.CallAsync(McpCallerTools.QueryAst, route == JsonRoute ? json : builder, token);
                if (denied)
                {
                    await McpCallerAssertions.ErrorAsync(reply, ErrorCode.PermissionDenied, dispatched: true);
                    var expected = JsonSerializer.SerializeToElement(Errors.Problem(ErrorCode.PermissionDenied, McpDenied), JsonDefaults.Options);
                    await Assert.That(JsonElement.DeepEquals(expected, reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error))).IsTrue();
                    await McpCallerAssertions.DoesNotDiscloseAsync(reply, state.Identity.Secret, MaximumJson);
                }
                else
                {
                    await QueryEquivalentIntegerClusterCurrent.PageAsync((await McpCallerAssertions.SuccessAsync<QueryPage>(reply)).Value,
                        Expected(boundary));
                }
            }, token);
        }
    }
}
