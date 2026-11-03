using System.Text.Json;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.Query;
using ModelContextProtocol.Protocol;

namespace KeyLoad.ClusterTests.QueryExecution;

internal static class QueryCapabilityParityClusterRefusals
{
    internal static async Task RunAsync(RequestCqrsClusterCallers reader, QueryCapabilityParityClusterState state, bool denied, CancellationToken token)
    {
        var original = QueryCapabilityParityClusterRequests.Ast(state.Partition);
        var plan = original with
        {
            Query = original.Query with
            {
                Explain = true
            }
        };
        var unknown = original with
        {
            Query = original.Query with
            {
                Filter = new Comparison(new FieldOperand("/score"), "LIKE", ValueOperand.Create(20m))
            }
        };
        var requests = denied ? new[] { original, plan } : new[] { unknown };
        var code = denied ? ErrorCode.PermissionDenied : ErrorCode.UnsupportedCapability;
        foreach (var request in requests)
        {
            var detail = denied ? "The principal cannot perform this operation in this scope."
                : "The query comparison is unsupported.";
            var result = await reader.Sdk.QueryAstAsync(request, token);
            await Assert.That(result.IsFailed).IsTrue();
            await Assert.That(result.Value).IsNull();
            await SqlClusterProtocol.EqualAsync(Errors.Problem(code, detail), result.Problem);
            await ProblemAsync(await reader.Mcp.CallAsync(McpCallerTools.QueryAst, request, token), code,
                denied ? detail : "The database operation could not be completed.");
        }
        await QueryCapabilityParityClusterManifest.RunAsync(reader, state.Partition, token);
    }
    private static async Task ProblemAsync(CallToolResult reply, ErrorCode code, string detail)
    {
        _ = (await McpCallerAssertions.ErrorAsync(reply, code, dispatched: true)).Value;
        await Assert.That(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(Errors.Problem(code, detail), JsonDefaults.Options),
            reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error))).IsTrue();
    }
}
