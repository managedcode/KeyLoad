using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.DocumentStorage;
using ManagedCode.Communication;
using ModelContextProtocol.Protocol;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class CrossTenantRf3ErrorAssertions
{
    private const string Detail = "The principal cannot perform this operation in this scope.";
    internal static async Task SdkAsync<T>(Result<T> result, string secret)
    {
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Problem!.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        await Assert.That(result.Problem.Detail).IsEqualTo(Detail);
        await PrivateAsync(JsonSerializer.Serialize(result.Problem, JsonDefaults.Options), secret);
    }
    internal static async Task McpAsync(CallToolResult result, string secret)
    {
        await McpCallerAssertions.ErrorAsync(result, ErrorCode.PermissionDenied, dispatched: true);
        var error = result.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error);
        await Assert.That(error.GetProperty(McpCallerProtocol.ProblemDetail).GetString()).IsEqualTo(Detail);
        await PrivateAsync(JsonSerializer.Serialize(result), secret);
    }
    private static async Task PrivateAsync(string result, string secret)
    {
        await Assert.That(result.Contains(secret, StringComparison.Ordinal)).IsFalse();
        await Assert.That(result.Contains(McpDocumentProtocol.PrivateValue, StringComparison.Ordinal)).IsFalse();
        await Assert.That(result.Contains(McpDocumentProtocol.ConflictValue, StringComparison.Ordinal)).IsFalse();
        await Assert.That(result.Contains(CrossTenantRf3WholeFlow.HealthyCanary, StringComparison.Ordinal)).IsFalse();
    }
}
