using System.Text.Json;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.DocumentStorage;
using ManagedCode.Communication;
using ModelContextProtocol.Protocol;

namespace KeyLoad.ClusterTests.Authorization;

internal static class CrossTenantClusterErrorAssertions
{
    private const string Detail = "The principal cannot perform this operation in this scope.";
    internal static async Task SdkAsync<T>(Result<T> result, string secret) where T : class?
    {
        await Assert.That(result.IsFailed).IsTrue();
        await McpCallerAssertions.NoValueAsync(result);
        await Assert.That(result.Problem!.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        await Assert.That(result.Problem.Detail).IsEqualTo(Detail);
        await PrivateAsync(JsonSerializer.Serialize(result, JsonDefaults.Options), secret);
    }
    internal static async Task McpAsync(CallToolResult result, string secret)
    {
        _ = (await McpCallerAssertions.ErrorAsync(result, ErrorCode.PermissionDenied, dispatched: true)).Value;
        var error = result.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error);
        await Assert.That(error.GetProperty(McpCallerProtocol.ProblemDetail).GetString()).IsEqualTo(Detail);
        await PrivateAsync(JsonSerializer.Serialize(result), secret);
    }
    private static async Task PrivateAsync(string result, string secret)
    {
        await Assert.That(result.Contains(secret, StringComparison.Ordinal)).IsFalse();
        await Assert.That(result.Contains(McpDocumentProtocol.PrivateValue, StringComparison.Ordinal)).IsFalse();
        await Assert.That(result.Contains(McpDocumentProtocol.ConflictValue, StringComparison.Ordinal)).IsFalse();
        await Assert.That(result.Contains(CrossTenantClusterWholeFlow.HealthyCanary, StringComparison.Ordinal)).IsFalse();
    }
}
