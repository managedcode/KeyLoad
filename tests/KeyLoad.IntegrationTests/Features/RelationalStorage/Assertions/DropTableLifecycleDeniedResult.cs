using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using ManagedCode.Communication;
using ModelContextProtocol.Protocol;

namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

internal static class DropTableLifecycleDeniedResult
{
    internal static async Task TypedAsync<T>(Result<T> result) where T : class
    {
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Value).IsNull();
        await ProblemAsync(result.Problem?.ErrorCode, result.Problem?.StatusCode);
    }

    internal static async Task SqlAsync(Result<JsonElement> result)
    {
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Value.ValueKind).IsEqualTo(JsonValueKind.Undefined);
        await ProblemAsync(result.Problem?.ErrorCode, result.Problem?.StatusCode);
    }

    internal static async Task McpAsync(CallToolResult result)
        => _ = await McpCallerAssertions.ErrorAsync(result, ErrorCode.PermissionDenied, dispatched: true);

    private static async Task ProblemAsync(string? code, int? status)
    {
        await Assert.That(code).IsEqualTo(ErrorCode.PermissionDenied.ToString());
        await Assert.That(status).IsEqualTo(Errors.Status(ErrorCode.PermissionDenied));
    }
}
