using System.Text.Json;
using KeyLoad.IntegrationTests.ClientApi;
using ManagedCode.Communication;
using ModelContextProtocol.Protocol;

namespace KeyLoad.IntegrationTests.QueryExecution;

internal static class SqlModelViewClusterDenialAssertions
{
    private const string ForeignScopeDetail = "The principal cannot perform this operation in this scope.";

    internal static async Task SdkAsync(Result<QueryPage> result, string secret, bool foreignScope = false)
    {
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Value).IsNull();
        await Assert.That(result.Problem!.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        if (foreignScope)
        {
            await Assert.That(result.Problem.Detail).IsEqualTo(ForeignScopeDetail);
        }
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
        foreach (var canary in new[] { secret, SqlModelViewClusterScenario.EventCanary,
            SqlModelViewClusterScenario.EventHeaderCanary, SqlModelViewClusterScenario.QueueCanary,
            SqlModelViewClusterScenario.QueueHeaderCanary })
        {
            await Assert.That(actual.Contains(canary, StringComparison.Ordinal)).IsFalse();
        }
    }
}
