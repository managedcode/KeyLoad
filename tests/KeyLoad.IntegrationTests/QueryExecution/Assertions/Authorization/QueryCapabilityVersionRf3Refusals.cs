using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Query;
using ModelContextProtocol.Protocol;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class QueryCapabilityVersionRf3Refusals
{
    internal static async Task RunAsync(RequestCqrsRf3Callers reader, QueryCapabilityVersionRf3State state, bool denied, CancellationToken token)
    {
        var original = QueryCapabilityVersionRf3Requests.Ast(state.Partition, 1);
        var plan = original with
        {
            Query = original.Query with
            {
                Explain = true
            }
        };
        var newer = original with
        {
            AstVersion = 3
        };
        var unknown = original with
        {
            Query = original.Query with
            {
                Filter = new Comparison(new FieldOperand("/score"), "LIKE", ValueOperand.Create(20m))
            }
        };
        var requests = denied ? new[] { original, plan } : new[] { newer, unknown };
        var code = denied ? ErrorCode.PermissionDenied : ErrorCode.UnsupportedCapability;
        foreach (var request in requests)
        {
            var detail = denied ? "The principal cannot perform this operation in this scope."
                : request.AstVersion == 3 ? "The query AST version is unsupported." : "The query comparison is unsupported.";
            var result = await reader.Sdk.QueryAstAsync(request, token);
            await Assert.That(result.IsFailed).IsTrue();
            await Assert.That(result.Value).IsNull();
            await SqlRf3Protocol.EqualAsync(Errors.Problem(code, detail), result.Problem);
            await ProblemAsync(await reader.Mcp.CallAsync(McpCallerTools.QueryAst, request, token), code,
                denied ? detail : "The database operation could not be completed.");
        }
        await QueryCapabilityVersionRf3Manifest.RunAsync(reader, state.Partition, token);
    }
    private static async Task ProblemAsync(CallToolResult reply, ErrorCode code, string detail)
    {
        await McpCallerAssertions.ErrorAsync(reply, code, dispatched: true);
        await Assert.That(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(Errors.Problem(code, detail), JsonDefaults.Options),
            reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error))).IsTrue();
    }
}
