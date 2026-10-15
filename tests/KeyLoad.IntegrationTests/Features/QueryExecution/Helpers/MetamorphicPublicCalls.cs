using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Query;
using ManagedCode.Communication;
using ModelContextProtocol.Protocol;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class MetamorphicPublicCalls
{
    internal static Task<Result<QueryPage>> SdkAsync(RequestCqrsRf3Callers reader, MetamorphicPublicPlan plan,
        int route, string? cursor, CancellationToken token)
        => route == 0 ? reader.Sdk.QueryAsync(plan.Sql with
        {
            Cursor = cursor
        }, token)
            : reader.Sdk.QueryAstAsync(Ast(plan, route) with
            {
                Cursor = cursor
            }, token);
    internal static Task<CallToolResult> McpAsync(RequestCqrsRf3Callers reader, MetamorphicPublicPlan plan,
        int route, string? cursor, CancellationToken token)
        => route == 0 ? reader.Mcp.CallAsync(McpCallerTools.QueryExecute, plan.Sql with
        {
            Cursor = cursor
        }, token)
            : reader.Mcp.CallAsync(McpCallerTools.QueryAst, Ast(plan, route) with
            {
                Cursor = cursor
            }, token);
    private static AstQueryRequest Ast(MetamorphicPublicPlan plan, int route)
        => route == 1 ? plan.Parsed : route == 2 ? plan.Json : plan.CSharp;
    internal static async Task DeniedAsync(RequestCqrsRf3Callers reader, MetamorphicPublicPlan plan, int route, string secret, CancellationToken token)
    {
        var sdk = await SdkAsync(reader, plan, route, null, token);
        await Assert.That(sdk.IsFailed).IsTrue();
        await Assert.That(sdk.Value).IsNull();
        await SqlRf3Protocol.EqualAsync(Errors.Problem(ErrorCode.PermissionDenied,
            "The query requires a protected field-use grant."), sdk.Problem);
        var reply = await McpAsync(reader, plan, route, null, token);
        await McpCallerAssertions.ErrorAsync(reply, ErrorCode.PermissionDenied, dispatched: true);
        var expected = JsonSerializer.SerializeToElement(Errors.Problem(ErrorCode.PermissionDenied,
            "The authenticated principal is not permitted to perform this operation."), JsonDefaults.Options);
        await Assert.That(JsonElement.DeepEquals(expected, reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error))).IsTrue();
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, secret, "PRIVATE-54");
    }
}
