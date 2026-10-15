using KeyLoad.IntegrationTests.Features.ClientApi;
using ManagedCode.Communication;
using ModelContextProtocol.Protocol;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class DistributedQueryAuthorityProblems
{
    internal static async Task SdkAsync(Result<PartitionQueryPageV1> actual, ErrorCode code, string detail)
    {
        await Assert.That(actual.IsSuccess).IsFalse();
        await Assert.That(actual.Value).IsNull();
        await SqlRf3Protocol.EqualAsync(Errors.Problem(code, detail), actual.Problem);
    }
    internal static async Task McpAsync(CallToolResult actual, ErrorCode code, string detail)
    {
        await McpCallerAssertions.ErrorAsync(actual, code, dispatched: true);
        var expected = JsonDefaults.Deserialize<System.Text.Json.JsonElement>(JsonDefaults.Serialize(Errors.Problem(code, detail)));
        await SqlRf3Protocol.EqualAsync(expected, actual.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error));
    }
}
