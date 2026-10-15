using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using static KeyLoad.IntegrationTests.Features.QueryExecution.QueryEquivalentIntegerRf3DecimalProtocol;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class QueryEquivalentIntegerRf3Calls
{
    private const int SqlRoute = 0;
    private const int JsonRoute = 1;
    private const int BuilderRoute = 2;
    private const string NativeDenied = "The principal cannot perform this operation in this scope.";
    private const string McpDenied = NativeDenied;
    internal static async Task MatrixAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        QueryEquivalentIntegerRf3State state, bool denied, CancellationToken token)
    {
        foreach (var kind in Enum.GetValues<QueryEquivalentIntegerRf3Kind>())
        {
            foreach (var boundary in new[] { false, true })
            {
                await CaseAsync(root, reader, state, kind, boundary, denied, token);
            }
        }
    }
    private static async Task CaseAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        QueryEquivalentIntegerRf3State state, QueryEquivalentIntegerRf3Kind kind, bool boundary,
        bool denied, CancellationToken token)
    {
        var minimum = boundary ? Maximum(kind) - Fraction : Threshold;
        var sql = QueryEquivalentIntegerRf3DecimalRequests.Sql(state.Partition, kind, minimum);
        var json = QueryEquivalentIntegerRf3DecimalRequests.Ast(state.Partition, kind, minimum);
        var builder = QueryEquivalentIntegerRf3DecimalRequests.CSharp(state.Partition, kind, minimum);
        foreach (var route in new[] { SqlRoute, JsonRoute, BuilderRoute })
        {
            await QueryEquivalentIntegerRf3Current.ObserveAsync(root, state, async () =>
            {
                var sdk = route == SqlRoute ? await reader.Sdk.QueryAsync(sql, token)
                    : await reader.Sdk.QueryAstAsync(route == JsonRoute ? json : builder, token);
                if (denied)
                {
                    await Assert.That(sdk.IsFailed).IsTrue();
                    await Assert.That(sdk.Value).IsNull();
                    await SqlRf3Protocol.EqualAsync(Errors.Problem(ErrorCode.PermissionDenied, NativeDenied), sdk.Problem);
                }
                else
                {
                    await QueryEquivalentIntegerRf3Current.PageAsync(await McpCallerAssertions.SdkSuccessAsync(sdk), Expected(boundary));
                }
            }, token);
            await QueryEquivalentIntegerRf3Current.ObserveAsync(root, state, async () =>
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
                    await QueryEquivalentIntegerRf3Current.PageAsync((await McpCallerAssertions.SuccessAsync<QueryPage>(reply)).Value,
                        Expected(boundary));
                }
            }, token);
        }
    }
}
