using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using ManagedCode.Communication;
using ModelContextProtocol.Protocol;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlModelViewRf3DenialAssertions
{
    private const string ForeignScopeDetail = "The principal cannot perform this operation in this scope.";

    internal static async Task SdkAsync(Result<QueryPage> result, string secret, bool foreignScope = false)
    {
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Value).IsNull();
        await Assert.That(result.Problem!.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        if (foreignScope)
        { await Assert.That(result.Problem.Detail).IsEqualTo(ForeignScopeDetail); }
        await PrivateAsync(JsonSerializer.Serialize(result, JsonDefaults.Options), secret);
    }

    internal static async Task McpAsync(CallToolResult result, string secret, bool foreignScope = false)
    {
        await McpCallerAssertions.ErrorAsync(result, ErrorCode.PermissionDenied, dispatched: true);
        if (foreignScope)
        {
            await Assert.That(result.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
                .GetProperty(McpCallerProtocol.ProblemDetail).GetString()).IsEqualTo(ForeignScopeDetail);
        }
        await PrivateAsync(JsonSerializer.Serialize(result), secret);
    }

    private static async Task PrivateAsync(string actual, string secret)
    {
        foreach (var canary in new[] { secret, SqlModelViewRf3Scenario.EventCanary,
            SqlModelViewRf3Scenario.EventHeaderCanary, SqlModelViewRf3Scenario.QueueCanary,
            SqlModelViewRf3Scenario.QueueHeaderCanary })
        { await Assert.That(actual.Contains(canary, StringComparison.Ordinal)).IsFalse(); }
    }
}
