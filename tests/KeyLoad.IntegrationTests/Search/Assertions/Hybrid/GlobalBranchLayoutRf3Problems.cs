using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using ModelContextProtocol.Protocol;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class GlobalBranchLayoutRf3Problems
{
    internal static async Task OfficialAsync(CallToolResult reply, ErrorCode code, string detail)
    {
        var expected = JsonDefaults.Deserialize<System.Text.Json.JsonElement>(JsonDefaults.Serialize(Errors.Problem(code, detail)));
        await SqlRf3Protocol.EqualAsync(expected, reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error));
    }
}
